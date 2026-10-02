using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphtweaks, Tidy Variants: the feature hides orientation and open/closed variants
/// and groups the rest in the creative inventory and the handbook (#252). This build is loaded
/// instead of a pinned copy (PackTests.csproj); the rule engine itself is unit-tested in
/// mods-src/seraphtweaks/tests. With its switch off: TidyVariantsOffScenarios.
/// </summary>
[AtlasWorld]
public class TidyVariantsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const string ModId = "seraphtweaks";

    // The game loads its own copy of the mod assembly, so its types are reached by name and
    // `dynamic`, never by a compile-time reference.
    private dynamic Bridge()
    {
        var system = World.Api.ModLoader.GetMod(ModId).Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.SeraphTweaks.TidyVariants.TidyVariantsModSystem");
        object? bridge = system.GetType().GetProperty("Bridge")!.GetValue(system);
        Assert.True(bridge is not null, "the server resolution is null (see the log for the error)");
        return bridge!;
    }

    // `dynamic` binds to the runtime type, where an array's Count is an explicit interface member.
    private static int Count(object collection) => ((System.Collections.ICollection)collection).Count;

    [AtlasScenario]
    public void Feature_loads_on_the_server_with_its_system_and_assets()
    {
        var loader = World.Api.ModLoader;
        Assert.True(loader.IsModEnabled(ModId), $"{ModId} is not enabled");
        // The game loads its own copy of the assembly, so match the system by name, not type.
        const string system = "SeraphHorizons.SeraphTweaks.TidyVariants.TidyVariantsModSystem";
        Assert.Contains(loader.GetMod(ModId).Systems, s => s.GetType().FullName == system);
        Assert.True(loader.IsModSystemEnabled(system), "the resolution system is not enabled on the server");
        // assets/ reached the game: the feature's lang keys (a group title) and its override file.
        Assert.Equal("Iron ore (hematite)", Lang.Get($"{ModId}:tidyvariants-group-game-ore-hematite"));
        Assert.NotNull(World.Api.Assets.TryGet(new AssetLocation(ModId, "config/tidyvariants-overrides.json")));
    }

    // The bridge (Game/): collects the creative entries from the loaded pack, resolves them once
    // at AssetsFinalize, and maps entries to stacks and back.
    [AtlasScenario(TimeoutMs = 120_000)]
    public void Server_resolves_the_whole_pack()
    {
        dynamic bridge = Bridge();
        dynamic res = bridge.Resolution;
        int entries = Count(res.Entries), visible = res.VisibleCount, groups = Count(res.Groups);
        var issues = ((IEnumerable<object>)res.Issues).Select(i => ((string)((dynamic)i).Kind, (string)((dynamic)i).Message)).ToList();

        var lines = new List<string>
        {
            (string)bridge.Summary(),
            $"overrides present: {bridge.OverridesPresent}, errors: {Count(bridge.OverrideErrors)}, worldproperties: {Count(bridge.WorldProperties)}, skipped stacks: {bridge.SkippedStacks}",
        };
        foreach (var g in issues.GroupBy(i => i.Item1).OrderByDescending(g => g.Count()))
        {
            lines.Add($"issue {g.Key}: {g.Count()}");
            lines.AddRange(g.Take(5).Select(i => "  " + i.Item2));
        }
        var statsType = ((object)res).GetType().Assembly.GetType("SeraphHorizons.SeraphTweaks.TidyVariants.Core.TidyStats")!;
        dynamic stats = statsType.GetMethod("Compute", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [res, 15])!;
        lines.Add($"stats: before {stats.EntriesBefore}, after {stats.EntriesAfter}, hidden {stats.Hidden} (rule {stats.HiddenByVariantRule}, override {stats.HiddenByOverride}), groups {stats.Groups}, grouped entries {stats.GroupedEntries}");
        foreach (dynamic g in stats.LargestGroups)
            lines.Add($"  group {g.Id}: {g.Members} members, rep {g.RepresentativeCode}");
        foreach (var l in lines) output.WriteLine(l);

        Assert.InRange(entries, 15_000, 60_000);
        Assert.True(visible < entries, $"visible {visible} of {entries}");
        Assert.True(groups > 0 && groups <= visible);
        Assert.Equal(entries, (int)bridge.Count);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_entry_maps_to_its_stack_and_back()
    {
        dynamic bridge = Bridge();
        int n = bridge.Count, attributeStacks = 0;
        for (int i = 0; i < n; i++)
        {
            ItemStack stack = bridge.StackOf(i);
            // A fresh clone, as the creative inventory holds: still the same entry.
            Assert.Equal(i, (int)bridge.EntryOf(stack.Clone()));
            if (stack.Attributes.Count > 0) attributeStacks++;
            Assert.Equal(stack.Collectible.Code.ToString(), (string)bridge.Resolution.Entries[i].Code);
        }
        Assert.True(attributeStacks > 0, "no attribute-stack entries (clutter, shields, ...)");

        // A filled container (bucket, flask): the game rewrites ucontents to contents in place the first
        // time it reads them, as the client does when it draws the slot. Still the same entry.
        int filled = 0;
        for (int i = 0; i < n; i++)
        {
            ItemStack stack = bridge.StackOf(i);
            if (!stack.Attributes.HasAttribute("ucontents") || stack.Collectible is not BlockContainer container) continue;
            var clone = stack.Clone();
            container.GetContents(World.Api.World, clone);
            Assert.False(clone.Attributes.HasAttribute("ucontents"), $"{stack.Collectible.Code}: ucontents not resolved");
            Assert.True(i == (int)bridge.EntryOf(clone), $"{stack.Collectible.Code}: entry {i} lost once its contents resolved");
            filled++;
        }
        Assert.True(filled > 0, "no filled containers among the creative stacks");
        Assert.Equal(-1, (int)bridge.EntryOf((ItemStack?)null));

        // The creative inventory's own stacks, as it builds them: plain per tab, plus resolved clones.
        var known = 0;
        foreach (var coll in World.Api.World.Blocks.Concat<CollectibleObject>(World.Api.World.Items))
        {
            if (coll?.Code is null) continue;
            if (coll.CreativeInventoryTabs is { Length: > 0 })
            {
                Assert.True((int)bridge.EntryOf(new ItemStack(coll)) >= 0, $"{coll.Code} has no entry");
                known++;
            }
            // A list with no tabs is in no tab (tar lists its bucket that way), so it has no entry either.
            foreach (var list in (coll.CreativeInventoryStacks ?? []).Where(l => l.Tabs is { Length: > 0 }))
                foreach (var js in list.Stacks)
                {
                    if (js.ResolvedItemstack is null) continue;
                    var s = js.ResolvedItemstack.Clone();
                    s.ResolveBlockOrItem(World.Api.World);
                    if ((int)bridge.EntryOf(s) < 0)
                    {
                        var same = Enumerable.Range(0, n).Where(i => ((ItemStack)bridge.StackOf(i)).Collectible == s.Collectible)
                            .Select(i => ((ItemStack)bridge.StackOf(i)).Attributes.ToJsonToken());
                        Assert.Fail($"{coll.Code}: creative stack {s.Collectible.Code} {s.Attributes.ToJsonToken()} has no entry; entries of it: " + string.Join(" | ", same));
                    }
                    known++;
                }
        }
        output.WriteLine($"{n} entries, {attributeStacks} attribute stacks, {known} creative slots mapped");
    }
}
