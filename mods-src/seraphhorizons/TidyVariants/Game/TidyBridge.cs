using System.Diagnostics;
using SeraphHorizons.Mod.TidyVariants.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.TidyVariants;

/// <summary>
/// The rule engine's result for one side, tied to that side's loaded game: entry index (into
/// <see cref="Resolution"/>.Entries) to collectible and stack, and back from any creative-slot stack.
/// Built once per side by <see cref="TidyVariantsModSystem"/>; read-only afterwards, so safe to read from any thread.
/// </summary>
public sealed class TidyBridge
{
    readonly CreativeSource[] sources;
    readonly IWorldAccessor world;
    // Plain entries by (class, id); attribute-stack entries by (class, id) then canonical attribute JSON.
    readonly Dictionary<(EnumItemClass, int), int> plainByCollectible = [];
    readonly Dictionary<(EnumItemClass, int), Dictionary<string, int>> stacksByCollectible = [];

    internal TidyBridge(IWorldAccessor world, EnumAppSide side, TidyResolution resolution, CreativeCollector.Result collected,
        IReadOnlyDictionary<string, IReadOnlyList<string>> worldProperties, OverrideFile overrides,
        bool overridesPresent, IReadOnlyList<string> overrideErrors, TimeSpan collectTime, TimeSpan assetsTime, TimeSpan resolveTime)
    {
        this.world = world;
        Side = side;
        Resolution = resolution;
        WorldProperties = worldProperties;
        Overrides = overrides;
        OverridesPresent = overridesPresent;
        OverrideErrors = overrideErrors;
        CollectTime = collectTime;
        AssetsTime = assetsTime;
        ResolveTime = resolveTime;
        SkippedStacks = collected.SkippedStacks;
        sources = collected.Sources.ToArray();
        for (int i = 0; i < sources.Length; i++)
        {
            var s = sources[i];
            var id = (s.Stack.Class, s.Stack.Id);
            if (s.AttributeKey.Length == 0) plainByCollectible.TryAdd(id, i);
            else
            {
                if (!stacksByCollectible.TryGetValue(id, out var d)) stacksByCollectible[id] = d = new(StringComparer.Ordinal);
                d.TryAdd(s.AttributeKey, i);
            }
        }
    }

    public EnumAppSide Side { get; }
    public TidyResolution Resolution { get; }
    /// <summary>Number of engine entries (= <c>Resolution.Entries.Count</c>).</summary>
    public int Count => sources.Length;
    /// <summary>The worldproperties lists passed to the engine (<c>domain:path</c> to variant codes).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> WorldProperties { get; }
    /// <summary>The override file in effect (<see cref="OverrideFile.Empty"/> if absent or invalid).</summary>
    public OverrideFile Overrides { get; }
    public bool OverridesPresent { get; }
    /// <summary>Parse errors of the override file (empty when it parsed or was absent).</summary>
    public IReadOnlyList<string> OverrideErrors { get; }
    public TimeSpan CollectTime { get; }
    public TimeSpan AssetsTime { get; }
    public TimeSpan ResolveTime { get; }
    /// <summary>Creative stacks that did not resolve and were skipped (the game skips them too).</summary>
    public int SkippedStacks { get; }

    /// <summary>Where entry <paramref name="entry"/> came from.</summary>
    public CreativeSource SourceOf(int entry) => sources[entry];
    public CollectibleObject CollectibleOf(int entry) => sources[entry].Collectible;
    /// <summary>The entry's stack. Shared and never changed by this mod: clone it before modifying or handing it out.</summary>
    public ItemStack StackOf(int entry) => sources[entry].Stack;

    /// <summary>
    /// The engine entry a creative stack shows, or -1. Matches the collectible (class + id) and the attributes
    /// (key order does not matter; stack size is ignored). A stack with attributes whose collectible has no
    /// attribute-stack entries falls back to the collectible's plain entry (attributes the game added at runtime).
    /// </summary>
    public int EntryOf(ItemStack? stack)
    {
        if (stack is null) return -1;
        var id = (stack.Class, stack.Id);
        var attrs = stack.Attributes;
        if (attrs is not null && attrs.Count > 0)
        {
            if (stacksByCollectible.TryGetValue(id, out var d))
                return d.TryGetValue(CreativeCollector.AttributeKey(stack, world), out int e) ? e : -1;
        }
        return plainByCollectible.TryGetValue(id, out int p) ? p : -1;
    }

    public bool TryGetEntry(ItemStack? stack, out int entry) => (entry = EntryOf(stack)) >= 0;

    /// <summary>Entry index per stack (-1 where unknown), e.g. a creative tab's slot stacks.</summary>
    public int[] EntriesOf(IReadOnlyList<ItemStack?> stacks)
    {
        var result = new int[stacks.Count];
        for (int i = 0; i < result.Length; i++) result[i] = EntryOf(stacks[i]);
        return result;
    }

    /// <summary>One line: entries, visible, hidden, groups, issues and timings.</summary>
    public string Summary()
    {
        var r = Resolution;
        int multi = r.Groups.Count(g => g.Members.Count > 1);
        return $"{Side.ToString().ToLowerInvariant()}: {r.Entries.Count} entries, {r.VisibleCount} visible, " +
               $"{r.Entries.Count - r.VisibleCount} hidden, {r.Groups.Count} groups ({multi} with 2+ members), {r.Issues.Count} issues; " +
               $"collect {CollectTime.TotalMilliseconds:0} ms, assets {AssetsTime.TotalMilliseconds:0} ms, resolve {ResolveTime.TotalMilliseconds:0} ms";
    }

    /// <summary>Collects the creative entries and asset inputs from a loaded game and resolves them.</summary>
    internal static TidyBridge Build(ICoreAPI api)
    {
        var sw = Stopwatch.StartNew();
        var collected = CreativeCollector.Collect(api.World);
        var collectTime = sw.Elapsed;

        sw.Restart();
        var props = AssetInputs.LoadWorldProperties(api);
        var overrides = AssetInputs.LoadOverrides(api, out bool present, out var errors);
        var assetsTime = sw.Elapsed;

        sw.Restart();
        var resolution = TidyEngine.Resolve(collected.Entries, props, overrides);
        var resolveTime = sw.Elapsed;

        return new TidyBridge(api.World, api.Side, resolution, collected, props, overrides, present, errors, collectTime, assetsTime, resolveTime);
    }
}
