using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, creative mod tabs (docs/creative-mod-tabs.md): the server appends one creative tab
/// per mod after the default ones, from the plan it also sends to clients. Run on the server's own creative
/// inventory code: a fresh <c>InventoryPlayerCreative</c> built by <c>UpdateFromWorld</c>, as for a joining
/// player. The GUI (the button and the strip) is client only and checked by hand (the doc's checklist); the
/// planning logic is unit-tested in mods-src/seraphhorizons/tests. With the switch off:
/// CreativeModTabsOffScenarios.
/// </summary>
[AtlasWorld]
public class CreativeModTabsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const string ModId = "seraphhorizons";
    private const string Prefix = "seraphhorizons-modtab-";
    private static readonly MethodInfo UpdateFromWorld =
        typeof(InventoryPlayerCreative).GetMethod("UpdateFromWorld", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // The game loads its own copy of the mod assembly: its types are reached by name.
    private Type ModType(string name) =>
        World.Api.ModLoader.GetMod(ModId).Systems.First().GetType().Assembly.GetType(name, throwOnError: true)!;

    private Type SystemType => ModType("SeraphHorizons.Mod.CreativeModTabs.ModTabsModSystem");

    private dynamic Authority()
    {
        object? a = SystemType.GetProperty("Authority", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        Assert.True(a is not null, "no mod tab plan on the server: " + SystemType.GetProperty("AuthorityError")!.GetValue(null));
        return a!;
    }

    private string KeyOf(ItemStack stack) =>
        (string)ModType("SeraphHorizons.Mod.CreativeModTabs.CreativeStacks").GetMethod("KeyOf")!.Invoke(null, [stack, World.Api.World])!;

    private InventoryPlayerCreative Build(string uid)
    {
        var inv = new InventoryPlayerCreative("creative", uid, World.Api);
        UpdateFromWorld.Invoke(inv, [World.Api.World]);
        return inv;
    }

    /// <summary>A creative inventory as the game builds it without the tweak: the plan hidden while it is built.</summary>
    private InventoryPlayerCreative BuildVanilla(string uid)
    {
        var field = SystemType.GetField("_authority", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? saved = field.GetValue(null);
        field.SetValue(null, null);
        try { return Build(uid); }
        finally { field.SetValue(null, saved); }
    }

    private static List<CreativeTab> Tabs(InventoryPlayerCreative inv) => inv.CreativeTabs.Tabs.ToList();

    private static IEnumerable<ItemStack> StacksOf(CreativeTab tab)
    {
        for (int i = 0; i < tab.Inventory.Count; i++)
            if (tab.Inventory[i]?.Itemstack is { } s) yield return s;
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Mod_tabs_follow_the_default_tabs_and_hold_every_creative_stack_once()
    {
        await World.Ticks(2);   // past WorldReady, where the server plans
        dynamic plan = Authority();
        int defaults = plan.DefaultTabCount;

        var vanilla = Tabs(BuildVanilla("atlas-modtabs-vanilla"));
        var all = Tabs(Build("atlas-modtabs"));
        Assert.Equal(defaults, vanilla.Count);
        Assert.True(all.Count > defaults, "no mod tabs were added");

        // The default tabs are untouched: same codes, indices and stacks, in the same order.
        for (int t = 0; t < defaults; t++)
        {
            Assert.Equal(vanilla[t].Code, all[t].Code);
            Assert.Equal(vanilla[t].Index, all[t].Index);
            Assert.Equal(t, all[t].Index);
            var a = StacksOf(vanilla[t]).ToList();
            var b = StacksOf(all[t]).ToList();
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
                Assert.True(a[i].Equals(World.Api.World, b[i], GlobalConstants.IgnoredStackAttributes), $"{all[t].Code}[{i}] differs");
        }

        // Then the mod tabs, numbered on, as the server announces them.
        var specs = ((System.Collections.IEnumerable)plan.Tabs).Cast<dynamic>().ToList();
        var mods = all.Skip(defaults).ToList();
        Assert.Equal(specs.Count, mods.Count);
        for (int k = 0; k < mods.Count; k++)
        {
            Assert.Equal(defaults + k, mods[k].Index);
            Assert.Equal((string)specs[k].Code, mods[k].Code);
            Assert.StartsWith(Prefix, mods[k].Code);
            Assert.Equal((int)specs[k].Count, mods[k].Inventory.Count);
        }

        // Every distinct stack of the default tabs is in exactly one mod tab, and nothing else is.
        var inDefaults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tab in all.Take(defaults))
            foreach (var s in StacksOf(tab)) inDefaults.Add(KeyOf(s));
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tab in mods)
        {
            var domains = ((IEnumerable<string>)specs[mods.IndexOf(tab)].Domains).ToHashSet();
            foreach (var s in StacksOf(tab))
            {
                string key = KeyOf(s);
                Assert.True(seen.TryAdd(key, tab.Code), $"{s.Collectible.Code} is in {seen.GetValueOrDefault(key)} and {tab.Code}");
                Assert.Contains(s.Collectible.Code.Domain, domains);
            }
        }
        Assert.Equal(inDefaults.Count, seen.Count);
        Assert.Subset(inDefaults, seen.Keys.ToHashSet());

        output.WriteLine($"{defaults} default tabs, {mods.Count} mod tabs, {seen.Count} distinct creative stacks");
        foreach (var spec in specs)
            output.WriteLine($"  {spec.Code} \"{spec.Name}\" {spec.Count}: {string.Join(", ", (IEnumerable<string>)spec.Domains)}");
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Base_game_tab_comes_first_then_mods_by_name_and_domains_go_to_their_owning_mod()
    {
        await World.Ticks(2);
        dynamic plan = Authority();
        var specs = ((System.Collections.IEnumerable)plan.Tabs).Cast<dynamic>().ToList();

        Assert.Equal(Prefix + "game", (string)specs[0].Code);
        Assert.True((bool)specs[0].IsGame);
        Assert.Contains("game", (IEnumerable<string>)specs[0].Domains);
        Assert.Equal(1, specs.Count(s => (bool)s.IsGame));
        var names = specs.Skip(1).Select(s => (string)s.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);

        // Domains that differ from their mod's modid land under that mod, by its display name.
        var owners = (System.Collections.IDictionary)plan.Owners;
        foreach (var (domain, modid) in new[] { ("vinteng", "vintageengineering"), ("ageofflax", "ageofflaxfork"), ("bomb", "p1explosives"), ("oils", "oilsresoaped") })
        {
            Assert.True(owners.Contains(domain), $"no creative stacks in domain {domain}");
            dynamic owner = owners[domain]!;
            Assert.Equal(modid, (string)owner.Key);
            var tab = Assert.Single(specs, s => (string)s.Code == Prefix + modid);
            Assert.Contains(domain, (IEnumerable<string>)tab.Domains);
            Assert.Equal(World.Api.ModLoader.GetMod(modid).Info.Name, (string)tab.Name);
        }
        // A mod with several domains has one tab.
        Assert.Equal(specs.Count, specs.Select(s => (string)s.Code).Distinct().Count());
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_click_in_a_mod_tab_resolves_on_the_server_and_the_client_check_accepts_the_plan()
    {
        await World.Ticks(2);
        dynamic plan = Authority();
        int defaults = plan.DefaultTabCount;
        var inv = Build("atlas-modtabs-click");

        // The server handles a creative click with SetTab(packet tab index) and reads its slot by id.
        var specs = ((System.Collections.IEnumerable)plan.Tabs).Cast<dynamic>().ToList();
        int k = specs.FindIndex(s => (string)s.Code == Prefix + "vintageengineering");
        Assert.True(k >= 0);
        inv.SetTab(defaults + k);
        Assert.Equal(Prefix + "vintageengineering", inv.CurrentTab.Code);
        Assert.Equal("vinteng", inv[0]?.Itemstack?.Collectible.Code.Domain);

        // What a client does with the packet: scan its own collectibles, place them by the server's domains,
        // and require the server's counts and hashes (here on the same world, so they must match).
        var stacksType = ModType("SeraphHorizons.Mod.CreativeModTabs.CreativeStacks");
        var planner = ModType("SeraphHorizons.Mod.CreativeModTabs.Core.ModTabPlanner");
        dynamic packet = plan.ToPacket();
        Assert.Equal(defaults, (int)packet.DefaultTabCount);
        object fromPacket = packet.Specs();
        dynamic scan = stacksType.GetMethod("Scan")!.Invoke(null, [World.Api.World])!;
        object assignment = planner.GetMethod("Assign")!.Invoke(null, [scan.Refs, fromPacket])!;
        object? mismatch = planner.GetMethod("Verify")!.Invoke(null, [scan.Refs, fromPacket, assignment]);
        Assert.Null(mismatch);
    }

    [AtlasScenario]
    public void Client_patches_are_not_applied_on_the_server()
    {
        var owners = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m)?.Owners ?? []).ToHashSet();
        Assert.Contains("seraphhorizons.modtabs", owners);
        Assert.DoesNotContain("seraphhorizons.modtabs.client", owners);
    }
}

/// <summary>
/// Creative mod tabs with the switch off (<c>"CreativeModTabs": false</c>, seeded from fixtures/creativemodtabs-off):
/// no plan, no patch, and the creative inventory has only the game's tabs.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/creativemodtabs-off", TargetPath = "ModConfig")]
public class CreativeModTabsOffScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Switched_off_there_are_no_mod_tabs()
    {
        await World.Ticks(2);
        var system = World.Api.ModLoader.GetMod("seraphhorizons").Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.Mod.CreativeModTabs.ModTabsModSystem");
        Assert.Null(system.GetType().GetProperty("Authority", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.False(World.Api.LoadModConfig("seraphhorizons.json")["CreativeModTabs"].AsBool(true));

        var owners = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m)?.Owners ?? []).ToHashSet();
        Assert.DoesNotContain("seraphhorizons.modtabs", owners);

        var inv = new InventoryPlayerCreative("creative", "atlas-modtabs-off", World.Api);
        typeof(InventoryPlayerCreative).GetMethod("UpdateFromWorld", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inv, [World.Api.World]);
        Assert.NotEmpty(inv.CreativeTabs.Tabs);
        Assert.DoesNotContain(inv.CreativeTabs.Tabs, t => t.Code.StartsWith("seraphhorizons-modtab-", StringComparison.Ordinal));
    }
}
