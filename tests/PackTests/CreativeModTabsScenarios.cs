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
        // With no plan and an error set, the postfix neither appends nor plans again.
        var field = SystemType.GetField("_authority", BindingFlags.NonPublic | BindingFlags.Static)!;
        var error = SystemType.GetProperty("AuthorityError", BindingFlags.Public | BindingFlags.Static)!;
        object? saved = field.GetValue(null);
        field.SetValue(null, null);
        error.SetValue(null, "hidden by the scenario");
        try { return Build(uid); }
        finally
        {
            error.SetValue(null, null);
            field.SetValue(null, saved);
        }
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
        foreach (var (domain, modid) in new[] { ("ageofflax", "ageofflaxfork"), ("bomb", "p1explosives"), ("oils", "oilsresoaped") })
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

    /// <summary>The packet through protobuf-net, as the game's network channel sends it.</summary>
    private static object RoundTrip(object packet)
    {
        using var ms = new MemoryStream();
        ProtoBuf.Serializer.NonGeneric.Serialize(ms, packet);
        ms.Position = 0;
        return ProtoBuf.Serializer.NonGeneric.Deserialize(packet.GetType(), ms);
    }

    private static List<dynamic> List(object list) => ((System.Collections.IEnumerable)list).Cast<dynamic>().ToList();

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_packet_survives_protobuf_including_empty_and_missing_fields()
    {
        await World.Ticks(2);
        dynamic plan = Authority();
        dynamic sent = plan.ToPacket();
        dynamic got = RoundTrip(sent);
        Assert.Equal(1, (int)got.Version);
        Assert.Equal((int)sent.DefaultTabCount, (int)got.DefaultTabCount);
        var a = List(sent.Specs());
        var b = List(got.Specs());
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal((string)a[i].Code, (string)b[i].Code);
            Assert.Equal((string)a[i].Name, (string)b[i].Name);
            Assert.Equal((bool)a[i].IsGame, (bool)b[i].IsGame);
            Assert.Equal((int)a[i].Count, (int)b[i].Count);
            Assert.Equal((uint)a[i].Hash, (uint)b[i].Hash);
            Assert.Equal(((IEnumerable<string>)a[i].Domains).ToList(), ((IEnumerable<string>)b[i].Domains).ToList());
        }

        // Defaults and empty or missing fields: protobuf writes nothing for them, and Specs() reads them back safely.
        var packetType = ModType("SeraphHorizons.Mod.CreativeModTabs.ModTabsPacket");
        var entryType = ModType("SeraphHorizons.Mod.CreativeModTabs.ModTabEntry");
        dynamic empty = RoundTrip(Activator.CreateInstance(packetType)!);
        Assert.Equal(1, (int)empty.Version);
        Assert.Equal(0, (int)empty.DefaultTabCount);
        Assert.Empty(List(empty.Specs()));

        dynamic odd = Activator.CreateInstance(packetType)!;
        var tabs = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType))!;
        dynamic bare = Activator.CreateInstance(entryType)!;            // every field missing
        dynamic noDomains = Activator.CreateInstance(entryType)!;
        noDomains.Code = "seraphhorizons-modtab-x";
        noDomains.Domains = new List<string>();                         // empty: arrives as null
        noDomains.Hash = uint.MaxValue;
        tabs.Add(bare);
        tabs.Add(noDomains);
        packetType.GetProperty("Tabs")!.SetValue(odd, tabs);
        var specs = List(((dynamic)RoundTrip(odd)).Specs());
        Assert.Equal(2, specs.Count);
        Assert.Equal("", (string)specs[0].Code);
        Assert.Equal("", (string)specs[0].Name);
        Assert.False((bool)specs[0].IsGame);
        Assert.Empty((IEnumerable<string>)specs[0].Domains);
        Assert.Equal("seraphhorizons-modtab-x", (string)specs[1].Code);
        Assert.Empty((IEnumerable<string>)specs[1].Domains);
        Assert.Equal(uint.MaxValue, (uint)specs[1].Hash);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_client_built_from_the_packet_has_the_servers_slots_and_clicks_resolve_to_them()
    {
        await World.Ticks(2);
        dynamic plan = Authority();
        int defaults = plan.DefaultTabCount;
        var server = Build("atlas-modtabs-click");

        // The client's path, as ModTabsClient.TryBuild takes it: the packet after protobuf, its own scan, the
        // server's domains, the check, and its own tabs on an inventory without the server's postfix. Same world
        // here, so this checks the client-side build and indices, not a client that loads other mods.
        var stacksType = ModType("SeraphHorizons.Mod.CreativeModTabs.CreativeStacks");
        var planner = ModType("SeraphHorizons.Mod.CreativeModTabs.Core.ModTabPlanner");
        dynamic packet = RoundTrip(plan.ToPacket());
        Assert.Equal(defaults, (int)packet.DefaultTabCount);
        object fromPacket = packet.Specs();
        object scan = stacksType.GetMethod("Scan")!.Invoke(null, [World.Api.World])!;
        object refs = stacksType.GetProperty("Refs")!.GetValue(scan)!;
        object assignment = planner.GetMethod("Assign")!.Invoke(null, [refs, fromPacket])!;
        Assert.Null(planner.GetMethod("Verify")!.Invoke(null, [refs, fromPacket, assignment]));
        object members = stacksType.GetMethod("Members")!.Invoke(scan, [fromPacket, assignment])!;
        var clientInv = BuildVanilla("atlas-modtabs-client");
        Assert.Equal(defaults, clientInv.CreativeTabs.TabsByCode.Count);
        var clientTabs = ((System.Collections.IEnumerable)stacksType.GetMethod("BuildTabs")!
            .Invoke(scan, [clientInv, World.Api, fromPacket, members, defaults])!).Cast<CreativeTab>().ToList();

        var serverTabs = Tabs(server).Skip(defaults).ToList();
        Assert.Equal(serverTabs.Count, clientTabs.Count);
        for (int k = 0; k < serverTabs.Count; k++)
        {
            var s = serverTabs[k];
            var c = clientTabs[k];
            Assert.Equal(s.Code, c.Code);
            Assert.Equal(s.Index, c.Index);
            Assert.Equal(s.Inventory.Count, c.Inventory.Count);

            // A creative click: SetTab(packet tab index), then the slot by id, on the server's own inventory.
            server.SetTab(c.Index);
            Assert.Same(s, server.CurrentTab);
            for (int i = 0; i < c.Inventory.Count; i++)
            {
                var clicked = server[i]?.Itemstack;
                var shown = c.Inventory[i]?.Itemstack;
                Assert.True(clicked is not null && shown is not null && KeyOf(clicked) == KeyOf(shown),
                    $"{c.Code}[{i}]: server {clicked?.Collectible.Code}, client {shown?.Collectible.Code}");
            }
        }
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
