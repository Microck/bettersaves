using System;
using System.Globalization;
using Celeste;

namespace Celeste.Mod.BetterSaves;

internal sealed class SaveDeckEntry {
    public int Slot { get; }
    public string Path { get; }
    public string Name { get; }
    public long TimeTicks { get; }
    public int TotalDeaths { get; }
    public int TotalStrawberries { get; }
    public int LastAreaId { get; }
    public string LastAreaSid { get; }
    public string MapName { get; }
    public int UnlockedAreas { get; }
    public DateTime LastWriteTime { get; }
    public bool IsPinned { get; }
    public bool IsArchived { get; }
    public bool IsComplete { get; }
    public bool IsModded { get; }
    public bool HasBackup { get; }
    public SaveData? Data { get; }

    public SaveDeckEntry(
        int slot,
        string path,
        SaveData? data,
        DateTime lastWriteTime,
        bool isPinned,
        bool isArchived,
        bool isModded,
        bool hasBackup) {
        Slot = slot;
        Path = path;
        Data = data;
        LastWriteTime = lastWriteTime;
        IsPinned = isPinned;
        IsArchived = isArchived;
        IsModded = isModded;
        HasBackup = hasBackup;

        Name = string.IsNullOrWhiteSpace(data?.Name) ? "File " + slot.ToString(CultureInfo.InvariantCulture) : data.Name;
        TimeTicks = data?.Time ?? 0L;
        TotalDeaths = data?.TotalDeaths ?? 0;
        TotalStrawberries = data?.TotalStrawberries ?? 0;
        LastAreaId = data?.LastArea.ID ?? -1;
        LastAreaSid = data?.LastArea.SID ?? "";
        MapName = ResolveMapName(LastAreaId, LastAreaSid);
        UnlockedAreas = data?.UnlockedAreas ?? 0;
        IsComplete = UnlockedAreas >= 10 || LastAreaId >= 8;
    }

    public bool IsInProgress => !IsArchived && !IsComplete;

    public string RowLabel {
        get {
            string marker = IsPinned ? "[pin] " : "";
            string archive = IsArchived ? " [archived]" : "";
            return marker + Name + " - " + FormattedTime + " - " +
                TotalStrawberries.ToString(CultureInfo.InvariantCulture) + " berries - " +
                RelativeAge + archive;
        }
    }

    public string DetailLabel => MapName +
        " - deaths " + TotalDeaths.ToString(CultureInfo.InvariantCulture) +
        (IsModded ? " - modded" : IsComplete ? " - complete" : "") +
        (HasBackup ? " - backup available" : "");

    public string FormattedTime => FormatTime(TimeTicks);

    public string RelativeAge => FormatRelativeTime(LastWriteTime);

    public string BackupPath => Path + ".bak";

    private static string FormatTime(long ticks) {
        if (ticks <= 0) {
            return "0:00";
        }

        TimeSpan time = TimeSpan.FromTicks(ticks);
        if (time.TotalHours >= 1) {
            return ((int) time.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" +
                time.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                time.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        return time.Minutes.ToString(CultureInfo.InvariantCulture) + ":" +
            time.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string FormatRelativeTime(DateTime lastWriteTime) {
        TimeSpan age = DateTime.Now - lastWriteTime;
        if (age.TotalDays >= 2) {
            return ((int) age.TotalDays).ToString(CultureInfo.InvariantCulture) + "d ago";
        }

        if (age.TotalDays >= 1) {
            return "yesterday";
        }

        if (age.TotalHours >= 1) {
            return ((int) age.TotalHours).ToString(CultureInfo.InvariantCulture) + "h ago";
        }

        return "today";
    }

    private static string ResolveMapName(int areaId, string sid) {
        if (!string.IsNullOrWhiteSpace(sid) && !sid.StartsWith("Celeste/", StringComparison.Ordinal)) {
            int lastSlash = sid.LastIndexOf('/');
            string rawName = lastSlash >= 0 ? sid.Substring(lastSlash + 1) : sid;
            return HumanizeIdentifier(rawName);
        }

        return areaId switch {
            0 => "Prologue",
            1 => "Forsaken City",
            2 => "Old Site",
            3 => "Celestial Resort",
            4 => "Golden Ridge",
            5 => "Mirror Temple",
            6 => "Reflection",
            7 => "The Summit",
            8 => "Core",
            9 => "Farewell",
            _ => "Unknown Map"
        };
    }

    private static string HumanizeIdentifier(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "Unknown Map";
        }

        string spaced = value.Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }
}
