using System.Collections.Generic;
using Celeste.Mod;

namespace Celeste.Mod.BetterSaves;

public enum SaveDeckSort {
    Recent,
    Slot,
    Name,
    Completion,
    LastChapter
}

public sealed class BetterSavesModuleSettings : EverestModuleSettings {
    public HashSet<int> PinnedSlots { get; set; } = new();
    public HashSet<int> ArchivedSlots { get; set; } = new();
    public SaveDeckSort SortMode { get; set; } = SaveDeckSort.Recent;

    internal void Normalize() {
        PinnedSlots ??= new HashSet<int>();
        ArchivedSlots ??= new HashSet<int>();
    }
}
