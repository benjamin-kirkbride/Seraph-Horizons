using System.Runtime.InteropServices;

namespace SeraphHorizons.Mod.TidyVariants.Core;

/// <summary>One visual slot of the creative grid after grouping: a real inventory slot id plus what it shows.</summary>
public readonly struct ViewSlot(int slotId, int position, int entry, int group, int memberCount, DisplayFlags flags)
{
    /// <summary>The inventory slot id (unchanged: the server resolves clicks by it).</summary>
    public int SlotId { get; } = slotId;
    /// <summary>Index of this slot in the input list (the flat search result).</summary>
    public int Position { get; } = position;
    /// <summary>Engine entry the slot shows, or -1 for a slot the engine doesn't know.</summary>
    public int Entry { get; } = entry;
    /// <summary>Group index for tiles and expanded members, else -1.</summary>
    public int Group { get; } = group;
    /// <summary>Matching members of the group (tiles and expanded members), else 0.</summary>
    public int MemberCount { get; } = memberCount;
    public DisplayFlags Flags { get; } = flags;
    public bool IsTile => (Flags & DisplayFlags.Tile) != 0;
    public bool IsExpandedMember => (Flags & DisplayFlags.ExpandedMember) != 0;
    public bool IsAutoExpanded => (Flags & DisplayFlags.AutoExpanded) != 0;
    public bool IsGrouped => Group >= 0;

    public override string ToString() => $"#{SlotId}@{Position} e{Entry} g{Group}x{MemberCount} {Flags}";
}

/// <summary>
/// The creative grid at slot-id level: wraps <see cref="DisplayListBuilder"/> for a list of real inventory slot
/// ids (the search result, in display order) and their engine entries. Slots the engine doesn't know (entry -1)
/// and repeats of an entry already listed keep their place as plain slots, so nothing the search found is lost;
/// hidden entries are dropped. Reuses its buffers; not thread-safe (one per GUI).
/// </summary>
public sealed class CreativeView
{
    readonly TidyResolution res;
    readonly DisplayListBuilder builder;
    readonly int[] pos, stamp;
    readonly List<int> surviving = [];
    readonly List<int> extras = [];
    readonly List<ViewSlot> slots = [];
    int cur;

    public CreativeView(TidyResolution resolution)
    {
        res = resolution;
        builder = new DisplayListBuilder(resolution);
        pos = new int[resolution.Entries.Count];
        stamp = new int[resolution.Entries.Count];
    }

    public TidyResolution Resolution => res;

    /// <summary>The grid from the last <see cref="Build"/>, in display order.</summary>
    public IReadOnlyList<ViewSlot> Slots => slots;

    /// <summary>Input slots kept by the last <see cref="Build"/> (all but hidden ones): the number of matching items.</summary>
    public int ItemCount { get; private set; }

    /// <summary>The group expanded because it was the only one left, or -1.</summary>
    public int AutoExpandedGroup => builder.AutoExpandedGroup;

    /// <summary>True if <paramref name="entry"/> is a known entry the rules hide.</summary>
    public bool IsHidden(int entry) => (uint)entry < (uint)pos.Length && res.IsHidden(entry);

    /// <param name="slotIds">Inventory slot ids in display order.</param>
    /// <param name="entryOfSlot">The engine entry of each slot (same length), -1 where unknown.</param>
    /// <param name="expanded">Expanded group indices, or null.</param>
    public void Build(ReadOnlySpan<int> slotIds, ReadOnlySpan<int> entryOfSlot, IReadOnlySet<int>? expanded = null)
    {
        if (slotIds.Length != entryOfSlot.Length) throw new ArgumentException("slotIds and entryOfSlot differ in length");
        surviving.Clear();
        extras.Clear();
        slots.Clear();
        if (++cur == int.MaxValue) { Array.Clear(stamp); cur = 1; }

        int kept = 0;
        for (int p = 0; p < slotIds.Length; p++)
        {
            int e = entryOfSlot[p];
            if ((uint)e >= (uint)pos.Length) { extras.Add(p); kept++; continue; }
            if (res.IsHidden(e)) continue;
            kept++;
            if (stamp[e] == cur) { extras.Add(p); continue; }   // the same entry listed twice: keep as a plain slot
            stamp[e] = cur;
            pos[e] = p;
            surviving.Add(e);
        }
        ItemCount = kept;

        builder.Build(CollectionsMarshal.AsSpan(surviving), expanded);

        // Merge the builder's items (each at the position of its first member) with the extras (own position).
        var items = builder.Items;
        var members = builder.Members;
        int j = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int key = it.Group >= 0 ? pos[members[it.MembersStart]] : pos[it.Entry];
            while (j < extras.Count && extras[j] < key) AddExtra(slotIds, entryOfSlot, extras[j++]);
            int p = pos[it.Entry];
            slots.Add(new ViewSlot(slotIds[p], p, it.Entry, it.Group, it.Group >= 0 ? it.MembersCount : 0, it.Flags));
        }
        while (j < extras.Count) AddExtra(slotIds, entryOfSlot, extras[j++]);
    }

    void AddExtra(ReadOnlySpan<int> slotIds, ReadOnlySpan<int> entryOfSlot, int p) =>
        slots.Add(new ViewSlot(slotIds[p], p, entryOfSlot[p], -1, 0, DisplayFlags.None));

    /// <summary>
    /// The group an expand/collapse gesture on <paramref name="slot"/> toggles, or -1: tiles and members of a
    /// group the player expanded. Members of an auto-expanded group (the only group left) have nothing to toggle.
    /// </summary>
    public static int ToggleTarget(in ViewSlot slot) =>
        slot.IsGrouped && !slot.IsAutoExpanded && (slot.IsTile || slot.IsExpandedMember) ? slot.Group : -1;

    /// <summary>
    /// The group a right-click on <paramref name="slot"/> toggles, or -1 to leave the click to vanilla
    /// (docs/variant-grouping/creative.md, "Right-click"). Only a press with an empty cursor that is not part
    /// of a right-drag toggles: with an item held, vanilla right-click (void one, right-drag) is kept.
    /// </summary>
    /// <param name="cursorEmpty">The mouse slot holds nothing.</param>
    /// <param name="rightDragging">The grid is in a right-drag (vanilla <c>isRightMouseDownStartedInsideElem</c>).</param>
    public static int RightClickTarget(in ViewSlot slot, bool cursorEmpty, bool rightDragging) =>
        cursorEmpty && !rightDragging ? ToggleTarget(slot) : -1;
}

/// <summary>
/// Which groups the player expanded, by group id (stable across sessions, unlike indices). Ids the current
/// resolution doesn't know are kept, so a pack change that drops a group and brings it back keeps its state.
/// </summary>
public sealed class ExpandState
{
    readonly HashSet<string> ids = new(StringComparer.Ordinal);
    readonly HashSet<int> indices = [];
    TidyResolution? res;

    public ExpandState(IEnumerable<string>? ids = null)
    {
        if (ids is null) return;
        foreach (var id in ids) if (!string.IsNullOrEmpty(id)) this.ids.Add(id);
    }

    /// <summary>Every expanded id, sorted (ordinal), for saving.</summary>
    public IReadOnlyList<string> Ids => ids.OrderBy(s => s, StringComparer.Ordinal).ToList();

    /// <summary>Expanded group indices of <paramref name="resolution"/> (the set handed to the builder).</summary>
    public IReadOnlySet<int> IndicesFor(TidyResolution resolution)
    {
        if (!ReferenceEquals(res, resolution))
        {
            res = resolution;
            indices.Clear();
            foreach (var id in ids)
                if (resolution.GroupById(id) is { } g) indices.Add(g.Index);
        }
        return indices;
    }

    public bool IsExpanded(TidyResolution resolution, int group) => IndicesFor(resolution).Contains(group);

    /// <summary>Flips <paramref name="group"/>; returns whether it is now expanded.</summary>
    public bool Toggle(TidyResolution resolution, int group)
    {
        var set = IndicesFor(resolution);
        string id = resolution.Groups[group].Id;
        if (set.Contains(group)) { indices.Remove(group); ids.Remove(id); return false; }
        indices.Add(group); ids.Add(id);
        return true;
    }
}
