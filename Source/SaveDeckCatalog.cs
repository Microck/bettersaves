using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Celeste;

namespace Celeste.Mod.BetterSaves;

internal static class SaveDeckCatalog {
    internal const int MaxSaveNameLength = 12;

    public static List<SaveDeckEntry> LoadEntries(bool includeArchived, string filter, SaveDeckSort sortMode) {
        string savesDirectory = SavesDirectory;
        if (!Directory.Exists(savesDirectory)) {
            return new List<SaveDeckEntry>();
        }

        string normalizedFilter = (filter ?? "").Trim();
        IEnumerable<SaveDeckEntry> entries = Directory.GetFiles(savesDirectory, "*.celeste")
            .Select(TryLoadEntry)
            .Where(entry => entry != null)
            .Cast<SaveDeckEntry>()
            .Where(entry => includeArchived || !entry.IsArchived);

        if (normalizedFilter.Length > 0) {
            entries = entries.Where(entry =>
                entry.Name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                entry.Slot.ToString(CultureInfo.InvariantCulture).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase));
        }

        return SortEntries(entries, sortMode).ToList();
    }

    public static int NextFreeSlot() {
        string savesDirectory = SavesDirectory;
        HashSet<int> usedSlots = Directory.Exists(savesDirectory)
            ? Directory.GetFiles(savesDirectory, "*.celeste")
                .Select(path => TryParseSlot(Path.GetFileName(path), out int slot) ? slot : -1)
                .Where(slot => slot >= 0)
                .ToHashSet()
            : new HashSet<int>();

        for (int slot = 0; slot < int.MaxValue; slot++) {
            if (!usedSlots.Contains(slot)) {
                return slot;
            }
        }

        throw new InvalidOperationException("No free save slot found.");
    }

    public static int Duplicate(SaveDeckEntry entry) {
        int targetSlot = NextFreeSlot();
        string targetPath = SavePathForSlot(targetSlot);
        Directory.CreateDirectory(SavesDirectory);
        File.Copy(entry.Path, targetPath, overwrite: false);
        PatchSaveName(targetPath, DuplicateName(entry.Name));
        CopySlotSidecars(entry.Slot, targetSlot);
        return targetSlot;
    }

    public static int CreateNew(string name, bool assistMode, bool variantMode) {
        int targetSlot = NextFreeSlot();
        Directory.CreateDirectory(SavesDirectory);
        string normalizedName = string.IsNullOrWhiteSpace(name) ? "Madeline" : name.Trim();

        SaveData saveData = new SaveData {
            FileSlot = targetSlot,
            Name = normalizedName,
            AssistMode = assistMode,
            VariantMode = variantMode
        };

        byte[] serialized = UserIO.Serialize(saveData);
        if (!UserIO.Save<SaveData>(SaveData.GetFilename(targetSlot), serialized)) {
            throw new IOException("Celeste could not save slot " + targetSlot.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return targetSlot;
    }

    public static void Rename(SaveDeckEntry entry, string name) {
        PatchSaveName(entry.Path, name);
    }

    public static void RestoreBackup(SaveDeckEntry entry) {
        File.Copy(entry.BackupPath, entry.Path, overwrite: true);
    }

    public static bool PermanentlyDelete(SaveDeckEntry entry) {
        bool deleted = SaveData.TryDelete(entry.Slot);
        DeleteSlotSidecars(entry.Slot);
        if (File.Exists(entry.Path)) {
            File.Delete(entry.Path);
            deleted = true;
        }

        if (File.Exists(entry.BackupPath)) {
            File.Delete(entry.BackupPath);
        }

        return deleted;
    }

    private static SaveDeckEntry? TryLoadEntry(string path) {
        if (!TryParseSlot(Path.GetFileName(path), out int slot)) {
            return null;
        }

        SaveData? saveData = null;
        try {
            saveData = UserIO.Load<SaveData>(SaveData.GetFilename(slot));
            if (saveData != null) {
                saveData.FileSlot = slot;
            }
        } catch (Exception exception) {
            Logger.Log(LogLevel.Warn, nameof(BetterSavesModule), "Failed to load save slot " + slot.ToString(CultureInfo.InvariantCulture) + ": " + exception);
        }

        BetterSavesModuleSettings settings = BetterSavesModule.Settings;
        bool archived = settings.ArchivedSlots.Contains(slot);
        bool pinned = settings.PinnedSlots.Contains(slot);
        bool hasBackup = File.Exists(path + ".bak");
        bool modded = HasModSidecar(slot) || IsModdedSave(saveData);
        return new SaveDeckEntry(slot, path, saveData, File.GetLastWriteTime(path), pinned, archived, modded, hasBackup);
    }

    private static string SavesDirectory => ActiveUserIOSaveDirectory();

    private static string SavePathForSlot(int slot) =>
        Path.Combine(SavesDirectory, SaveData.GetFilename(slot) + ".celeste");

    private static void CopySlotSidecars(int sourceSlot, int targetSlot) {
        if (!Directory.Exists(SavesDirectory)) {
            return;
        }

        string sourcePrefix = SaveData.GetFilename(sourceSlot);
        string targetPrefix = SaveData.GetFilename(targetSlot);
        foreach (string sourcePath in Directory.GetFiles(SavesDirectory, sourcePrefix + "-mod*.celeste")) {
            string fileName = Path.GetFileName(sourcePath);
            if (!fileName.StartsWith(sourcePrefix + "-", StringComparison.Ordinal)) {
                continue;
            }

            string targetPath = Path.Combine(SavesDirectory, targetPrefix + fileName.Substring(sourcePrefix.Length));
            File.Copy(sourcePath, targetPath, overwrite: false);
        }
    }

    private static IEnumerable<SaveDeckEntry> SortEntries(IEnumerable<SaveDeckEntry> entries, SaveDeckSort sortMode) {
        IOrderedEnumerable<SaveDeckEntry> sorted = entries
            .OrderBy(entry => entry.IsArchived ? 2 : entry.IsPinned ? 0 : 1);

        return sortMode switch {
            SaveDeckSort.Slot => sorted
                .ThenBy(entry => entry.Slot),
            SaveDeckSort.Name => sorted
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Slot),
            SaveDeckSort.Completion => sorted
                .ThenByDescending(entry => entry.IsComplete)
                .ThenByDescending(entry => entry.UnlockedAreas)
                .ThenByDescending(entry => entry.TotalStrawberries)
                .ThenBy(entry => entry.Slot),
            SaveDeckSort.LastChapter => sorted
                .ThenByDescending(entry => entry.LastAreaId)
                .ThenByDescending(entry => entry.LastWriteTime)
                .ThenBy(entry => entry.Slot),
            _ => sorted
                .ThenByDescending(entry => entry.IsPinned ? entry.LastWriteTime : DateTime.MinValue)
                .ThenByDescending(entry => entry.LastWriteTime)
                .ThenBy(entry => entry.Slot)
        };
    }

    private static bool HasModSidecar(int slot) {
        if (!Directory.Exists(SavesDirectory)) {
            return false;
        }

        return Directory.GetFiles(SavesDirectory, SaveData.GetFilename(slot) + "-mod*.celeste").Length > 0;
    }

    private static bool IsModdedSave(SaveData? data) {
        string sid = data?.LastArea.SID ?? "";
        return sid.Length > 0 && !sid.StartsWith("Celeste/", StringComparison.Ordinal);
    }

    private static string DuplicateName(string sourceName) {
        string suffix = " Copy";
        string baseName = string.IsNullOrWhiteSpace(sourceName) ? "File" : sourceName.Trim();
        int maxBaseLength = Math.Max(1, MaxSaveNameLength - suffix.Length);
        if (baseName.Length > maxBaseLength) {
            baseName = baseName.Substring(0, maxBaseLength).TrimEnd();
        }

        return baseName + suffix;
    }

    private static void PatchSaveName(string path, string name) {
        XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidDataException("Save file has no root element: " + path);
        XElement? nameElement = root.Element("Name");
        if (nameElement == null) {
            XElement? versionElement = root.Element("Version");
            nameElement = new XElement("Name");
            if (versionElement != null) {
                versionElement.AddAfterSelf(nameElement);
            } else {
                root.AddFirst(nameElement);
            }
        }

        nameElement.Value = name;
        document.Save(path);
    }

    private static void DeleteSlotSidecars(int slot) {
        if (!Directory.Exists(SavesDirectory)) {
            return;
        }

        string slotPrefix = SaveData.GetFilename(slot);
        foreach (string sidecarPath in Directory.GetFiles(SavesDirectory, slotPrefix + "-mod*.celeste")) {
            File.Delete(sidecarPath);
        }
    }

    private static bool TryParseSlot(string fileName, out int slot) {
        slot = -1;
        if (!fileName.EndsWith(".celeste", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        string stem = fileName.Substring(0, fileName.Length - ".celeste".Length);
        return int.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out slot);
    }

    private static string ActiveUserIOSaveDirectory() {
        FieldInfo? savePathField = typeof(UserIO).GetField("SavePath", BindingFlags.NonPublic | BindingFlags.Static);
        if (savePathField?.GetValue(null) is string savePath && !string.IsNullOrWhiteSpace(savePath)) {
            return savePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Celeste",
            "Saves");
    }
}
