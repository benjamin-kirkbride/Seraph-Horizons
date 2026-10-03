using System.Globalization;
using SeraphHorizons.Mod.CreativeModTabs.Core;
using SeraphHorizons.Mod.TidyVariants;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>
/// Every creative-listed stack, deduplicated, in the game's creative order: what the mod tabs are made of.
/// Built the same way on both sides from the collectibles (which the client gets from the server), so both
/// get the same list.
/// </summary>
public sealed class CreativeStacks
{
    /// <summary>One per distinct stack (<see cref="ModTabPlanner.Dedupe"/>), in order.</summary>
    public required List<StackRef> Refs { get; init; }
    /// <summary>The stack of each ref: a fresh stack, as <c>GatherTabStacks</c> makes it. Clone before use.</summary>
    public required List<ItemStack> Stacks { get; init; }
    /// <summary>The default tab codes in the order <c>UpdateFromWorld</c> creates them (their indices).</summary>
    public required List<string> DefaultTabCodes { get; init; }

    /// <summary>
    /// Mirrors <c>InventoryPlayerCreative.GatherTabStacks</c> (VintagestoryLib 1.22.7): per collectible in
    /// <see cref="CreativeCollector.CreativeOrder"/>, a plain stack for each of its <c>CreativeInventoryTabs</c>,
    /// then each <c>CreativeInventoryStacks</c> list's resolved stacks for each of the list's tabs. A tab exists
    /// as soon as it is named, even with no stack. Here each stack is taken once.
    /// </summary>
    public static CreativeStacks Scan(IWorldAccessor world)
    {
        var all = new List<StackRef>();
        var stacks = new List<ItemStack>();
        var tabCodes = new List<string>();
        var tabSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var coll in CreativeCollector.CreativeOrder(world))
        {
            if (coll.CreativeInventoryTabs is { } tabs)
            {
                foreach (var t in tabs) NameTab(t);
                if (tabs.Length > 0) Add(new ItemStack(coll));
            }
            if (coll.CreativeInventoryStacks is not { } lists) continue;
            foreach (var list in lists)
            {
                if (list?.Tabs is not { Length: > 0 } listTabs) continue;
                foreach (var t in listTabs) NameTab(t);
                if (list.Stacks is null) continue;
                foreach (var js in list.Stacks)
                {
                    if (js?.ResolvedItemstack is not { } resolved) continue;
                    var stack = resolved.Clone();
                    stack.ResolveBlockOrItem(world);
                    if (stack.Collectible?.Code is null) continue;
                    Add(stack);
                }
            }
        }

        var keep = ModTabPlanner.Dedupe(all);
        return new CreativeStacks
        {
            Refs = keep.Select(i => all[i]).ToList(),
            Stacks = keep.Select(i => stacks[i]).ToList(),
            DefaultTabCodes = tabCodes,
        };

        void NameTab(string? code)
        {
            if (code is not null && tabSeen.Add(code)) tabCodes.Add(code);
        }

        void Add(ItemStack stack)
        {
            var code = stack.Collectible.Code;
            all.Add(new StackRef(KeyOf(stack, world), code.ToString(), code.Domain));
            stacks.Add(stack);
        }
    }

    /// <summary>A stack's identity: its class and id, and its attributes in a canonical form
    /// (<see cref="CreativeCollector.AttributeKey"/>, which reads a container's contents the same before and after
    /// the game resolves them).</summary>
    public static string KeyOf(ItemStack stack, IWorldAccessor world) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)stack.Class}:{stack.Id}:{CreativeCollector.AttributeKey(stack, world)}");

    /// <summary>For each tab of <paramref name="tabs"/>, the indices of its refs, in order (refs with no tab are left out).</summary>
    public List<int>[] Members(IReadOnlyList<ModTabSpec> tabs, int[] assignment)
    {
        var members = new List<int>[tabs.Count];
        for (int t = 0; t < tabs.Count; t++) members[t] = [];
        for (int i = 0; i < Refs.Count; i++)
            if (assignment[i] >= 0) members[assignment[i]].Add(i);
        return members;
    }

    /// <summary>
    /// The mod tabs as real creative tabs, as <c>InventoryPlayerCreative.CreateTab</c> makes them, with their
    /// indices after the <paramref name="firstIndex"/>-1 default ones. Each slot holds its own clone.
    /// </summary>
    public List<CreativeTab> BuildTabs(InventoryPlayerCreative inv, ICoreAPI api, IReadOnlyList<ModTabSpec> tabs, List<int>[] members, int firstIndex)
    {
        var result = new List<CreativeTab>(tabs.Count);
        for (int t = 0; t < tabs.Count; t++)
        {
            var list = members[t];
            var tab = new CreativeTab(tabs[t].Code, new CreativeInventoryTab(list.Count, inv.InventoryID, api)) { Index = firstIndex + t };
            for (int s = 0; s < list.Count; s++) tab.Inventory[s]!.Itemstack = Stacks[list[s]].Clone();
            result.Add(tab);
        }
        return result;
    }
}
