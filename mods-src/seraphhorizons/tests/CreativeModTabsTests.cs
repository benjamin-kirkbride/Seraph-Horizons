using SeraphHorizons.Mod.CreativeModTabs.Core;

namespace SeraphHorizons.Mod.Tests;

public class DomainOwnersTests
{
    private static ModAssets Mod(string id, string name, params (string Domain, int Defining, int Total)[] domains) =>
        new(id, name, domains.ToDictionary(d => d.Domain, d => new DomainAssets(d.Defining, d.Total)));

    // Trimmed from the pack: VintageEngineering defines vinteng, Age of Flax (fork) defines ageofflax, MPE has a
    // second domain, a compat mod adds a few assets to vinteng, and several mods add assets under game.
    private static readonly IReadOnlyList<ModAssets> Mods =
    [
        Mod("vintageengineering", "Vintage Engineering", ("vinteng", 412, 3100), ("game", 0, 12)),
        Mod("vecompat", "VE Compat", ("vinteng", 0, 4), ("vecompat", 0, 2)),
        Mod("ageofflaxfork", "Age of Flax (fork)", ("ageofflax", 30, 200)),
        Mod("mechanicalpowerexpansion", "Mechanical Power Expansion", ("mechanicalpowerexpansion", 40, 300), ("mpegearbox", 6, 50)),
        Mod("seraphhorizons", "Seraph Horizons", ("seraphhorizons", 1, 10), ("game", 0, 3)),
        Mod("langonly", "Lang Only", ("orphan", 0, 3)),
    ];

    [Fact]
    public void GameIsTheBaseGameWhoeverAddsToIt()
    {
        var o = DomainOwners.Resolve("game", Mods, "Vintage Story");
        Assert.Equal(new TabOwner("game", "Vintage Story", true), o);
    }

    [Fact]
    public void ModidWinsItsOwnDomain()
    {
        var o = DomainOwners.Resolve("mechanicalpowerexpansion", Mods, "VS");
        Assert.Equal(new TabOwner("mechanicalpowerexpansion", "Mechanical Power Expansion", false), o);
    }

    [Theory]
    [InlineData("vinteng", "vintageengineering")]   // the definer, not the compat mod with a few assets there
    [InlineData("ageofflax", "ageofflaxfork")]
    [InlineData("mpegearbox", "mechanicalpowerexpansion")]
    [InlineData("orphan", "langonly")]               // no blocks or items anywhere: most assets of any kind
    public void DomainGoesToTheModThatDefinesIt(string domain, string owner) =>
        Assert.Equal(owner, DomainOwners.Resolve(domain, Mods, "VS").Key);

    [Fact]
    public void UnownedDomainStandsForItself()
    {
        var o = DomainOwners.Resolve("ghost", Mods, "VS");
        Assert.Equal(new TabOwner("ghost", "ghost", false), o);
    }

    [Fact]
    public void TiesGoToTheSmallerModid()
    {
        IReadOnlyList<ModAssets> mods = [Mod("zeta", "Zeta", ("shared", 5, 5)), Mod("alpha", "Alpha", ("shared", 5, 9))];
        Assert.Equal("alpha", DomainOwners.Resolve("shared", mods, "VS").Key);
        Assert.Equal("alpha", DomainOwners.Resolve("shared", mods.Reverse().ToList(), "VS").Key);
    }

    [Fact]
    public void BlankModNameFallsBackToTheModid()
    {
        IReadOnlyList<ModAssets> mods = [Mod("noname", "  ", ("noname", 1, 1))];
        Assert.Equal("noname", DomainOwners.Resolve("noname", mods, "VS").Name);
    }
}

public class ModTabPlannerTests
{
    private static StackRef S(string code, string? key = null)
    {
        string domain = code[..code.IndexOf(':')];
        return new StackRef(key ?? code, code, domain);
    }

    private static readonly Dictionary<string, TabOwner> Owners = new()
    {
        ["game"] = new("game", "Vintage Story", true),
        ["vinteng"] = new("vintageengineering", "Vintage Engineering", false),
        ["mechanicalpowerexpansion"] = new("mechanicalpowerexpansion", "Mechanical Power Expansion", false),
        ["mpegearbox"] = new("mechanicalpowerexpansion", "Mechanical Power Expansion", false),
        ["ageofflax"] = new("ageofflaxfork", "age of Flax (fork)", false),
        ["butchering"] = new("butchering", "Butchering", false),
    };

    [Fact]
    public void DedupeKeepsTheFirstOfEachStack()
    {
        // A stack in several default tabs is gathered once per tab; a stack list may repeat another's stack.
        var stacks = new[] { S("game:a"), S("game:b"), S("game:a"), S("vinteng:x", "k1"), S("vinteng:x", "k2"), S("vinteng:x", "k1") };
        Assert.Equal([0, 1, 3, 4], ModTabPlanner.Dedupe(stacks));
    }

    [Fact]
    public void GameFirstThenModsByNameIgnoringCase()
    {
        var stacks = new[] { S("vinteng:x"), S("butchering:b"), S("ageofflax:f"), S("game:a"), S("mechanicalpowerexpansion:m") };
        var tabs = ModTabPlanner.Plan(stacks, Owners);
        Assert.Equal(["Vintage Story", "age of Flax (fork)", "Butchering", "Mechanical Power Expansion", "Vintage Engineering"],
            tabs.Select(t => t.Name));
        Assert.True(tabs[0].IsGame);
        Assert.Equal("seraphhorizons-modtab-game", tabs[0].Code);
        Assert.Equal("seraphhorizons-modtab-vintageengineering", tabs[^1].Code);
    }

    [Fact]
    public void ModsSeveralDomainsShareOneTabInGameOrder()
    {
        var stacks = new[] { S("mpegearbox:g1"), S("game:a"), S("mechanicalpowerexpansion:m1"), S("mpegearbox:g2") };
        var tabs = ModTabPlanner.Plan(stacks, Owners);
        var mpe = Assert.Single(tabs, t => t.Code == "seraphhorizons-modtab-mechanicalpowerexpansion");
        Assert.Equal(["mechanicalpowerexpansion", "mpegearbox"], mpe.Domains);
        Assert.Equal(3, mpe.Count);
        Assert.Equal(ModTabPlanner.Hash(["mpegearbox:g1", "mechanicalpowerexpansion:m1", "mpegearbox:g2"]), mpe.Hash);

        var assigned = ModTabPlanner.Assign(stacks, tabs);
        int m = tabs.IndexOf(mpe);
        Assert.Equal([m, 0, m, m], assigned);
    }

    [Fact]
    public void ModsWithoutStacksGetNoTabAndUnknownDomainsTheirOwn()
    {
        var stacks = new[] { S("game:a"), S("ghost:z") };
        var tabs = ModTabPlanner.Plan(stacks, Owners);
        Assert.Equal(["seraphhorizons-modtab-game", "seraphhorizons-modtab-ghost"], tabs.Select(t => t.Code));
        Assert.Equal("ghost", tabs[1].Name);
        Assert.Empty(ModTabPlanner.Plan([], Owners));
    }

    [Fact]
    public void SameNameOrdersByCode()
    {
        var owners = new Dictionary<string, TabOwner>
        {
            ["b"] = new("bmod", "Same", false),
            ["a"] = new("amod", "same", false),
        };
        var tabs = ModTabPlanner.Plan([S("b:1"), S("a:1")], owners);
        Assert.Equal(["seraphhorizons-modtab-amod", "seraphhorizons-modtab-bmod"], tabs.Select(t => t.Code));
    }

    [Fact]
    public void VerifyAcceptsTheSameStacksAndNamesWhatDiffers()
    {
        var server = new[] { S("game:a"), S("vinteng:x"), S("game:b") };
        var tabs = ModTabPlanner.Plan(server, Owners);
        Assert.Null(ModTabPlanner.Verify(server, tabs, ModTabPlanner.Assign(server, tabs)));

        // A client with one stack more in a tab, a different stack, or a domain the server never saw.
        var more = new[] { S("game:a"), S("vinteng:x"), S("game:b"), S("game:c") };
        Assert.Contains("3 stacks here, 2 on the server", ModTabPlanner.Verify(more, tabs, ModTabPlanner.Assign(more, tabs)));
        var other = new[] { S("game:a"), S("vinteng:x"), S("game:z") };
        Assert.Contains("differ", ModTabPlanner.Verify(other, tabs, ModTabPlanner.Assign(other, tabs)));
        var stray = new[] { S("game:a"), S("vinteng:x"), S("game:b"), S("clientonly:q") };
        Assert.Contains("no tab for domain clientonly", ModTabPlanner.Verify(stray, tabs, ModTabPlanner.Assign(stray, tabs)));
    }

    [Fact]
    public void HashDependsOnOrderAndBoundaries()
    {
        Assert.NotEqual(ModTabPlanner.Hash(["a", "b"]), ModTabPlanner.Hash(["b", "a"]));
        Assert.NotEqual(ModTabPlanner.Hash(["ab"]), ModTabPlanner.Hash(["a", "b"]));
        Assert.Equal(ModTabPlanner.Hash(["game:a"]), ModTabPlanner.Hash(["game:a"]));
    }
}

public class ModTabsStateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingFileIsDefaultModeWithoutError(string? json)
    {
        var s = ModTabsState.Parse(json, out var error);
        Assert.Null(error);
        Assert.Equal(TabsMode.Default, s.Mode);
        Assert.Null(s.DefaultTab);
        Assert.Null(s.ModTab);
    }

    [Fact]
    public void RoundTrips()
    {
        var s = new ModTabsState { Mode = TabsMode.Mod, DefaultTab = "blocks", ModTab = "seraphhorizons-modtab-game" };
        var back = ModTabsState.Parse(s.ToJson(), out var error);
        Assert.Null(error);
        Assert.Equal(TabsMode.Mod, back.Mode);
        Assert.Equal("blocks", back.DefaultTab);
        Assert.Equal("seraphhorizons-modtab-game", back.ModTab);
        Assert.Contains("\"Mode\": \"Mod\"", s.ToJson());
    }

    [Fact]
    public void ToleratesCaseUnknownPropertiesAndNumbers()
    {
        var s = ModTabsState.Parse("""{"version": 1, "mode": "mod", "Extra": [1, 2], "ModTab": " "}""", out var error);
        Assert.Null(error);
        Assert.Equal(TabsMode.Mod, s.Mode);
        Assert.Null(s.ModTab);
        Assert.Equal(TabsMode.Mod, ModTabsState.Parse("""{"Mode": 1}""", out _).Mode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"Mode": "Sideways"}""")]
    [InlineData("""{"Mode": 7}""")]
    [InlineData("[]")]
    public void UnreadableIsDefaultModeWithAnError(string json)
    {
        var s = ModTabsState.Parse(json, out var error);
        Assert.NotNull(error);
        Assert.Equal(TabsMode.Default, s.Mode);
    }
}


public class TabLayoutTests
{
    // config/creativetabs.json in 1.22.7: 14 codes, all ordered before the unlisted ones (1.0).
    static readonly Dictionary<string, double> Vanilla = new()
    {
        ["general"] = 0, ["flora"] = 0.1, ["terrain"] = 0.2, ["decorative"] = 0.3, ["clutter"] = 0.35,
        ["construction"] = 0.4, ["mechanics"] = 0.45, ["aquatic"] = 0.47, ["items"] = 0.5, ["liquids"] = 0.55,
        ["tools"] = 0.6, ["clothing"] = 0.7, ["creatures"] = 0.8, ["meta"] = 0.9,
    };

    static Func<string, double> OrderOf(Dictionary<string, double> config) =>
        code => config.TryGetValue(code, out double o) ? o : TabLayout.UnlistedOrder;

    // As the pack has them: the vanilla tabs among 39 mods' own, in the order the game gathers them.
    static List<string> PackDefaults()
    {
        var codes = new List<string>();
        var vanilla = Vanilla.Keys.Reverse().ToList();   // gathered in some other order than listed
        for (int i = 0; i < 39; i++)
        {
            if (i % 3 == 0 && vanilla.Count > 0) { codes.Add(vanilla[^1]); vanilla.RemoveAt(vanilla.Count - 1); }
            codes.Add($"mod{i:00}");
        }
        codes.AddRange(vanilla);
        return codes;
    }

    static readonly List<string> ModTabs = Enumerable.Range(0, 77).Select(i => $"seraphhorizons-modtab-{i:00}").ToList();

    [Fact]
    public void LowerOrdersFirstEqualOrdersReversed()
    {
        var order = OrderOf(new() { ["a"] = 0.5, ["b"] = 0.1, ["c"] = 0.5 });
        Assert.Equal(["b", "c", "a", "z", "y"], TabLayout.Order(["a", "y", "b", "c", "z"], order));
        Assert.Equal(["e", "d", "c"], TabLayout.Order(["c", "d", "e"], _ => 1));
        Assert.Empty(TabLayout.Order([], _ => 1));
    }

    [Fact]
    public void VanillaListsItsTabsThenTheLastGatheredUnlistedOnes()
    {
        var ordered = TabLayout.Order(PackDefaults(), OrderOf(Vanilla));
        Assert.Equal(53, ordered.Count);
        Assert.Equal(Vanilla.OrderBy(kv => kv.Value).Select(kv => kv.Key), ordered.Take(14));
        Assert.Equal(["mod38", "mod37"], ordered.Skip(14).Take(2));
    }

    [Fact]
    public void ModModeKeepsTheLeftColumnAndShowsTheModTabsRight()
    {
        var defaults = PackDefaults();
        var layout = TabLayout.ForModMode(defaults, ModTabs, OrderOf(Vanilla));
        var defaultLeft = TabLayout.Order(defaults, OrderOf(Vanilla)).Take(16);
        Assert.True(layout.Ideal);
        Assert.Equal(defaultLeft, layout.Left);
        Assert.Equal(ModTabs, layout.Right);
        Assert.Equal(16 + 77, layout.Iteration.Count);
        Assert.Equal(layout.Left.Concat(layout.Right), TabLayout.Order(layout.Iteration, OrderOf(Vanilla)));
        // The mod tabs first, reversed; then the kept default tabs in the game's own iteration order.
        Assert.Equal(ModTabs.AsEnumerable().Reverse(), layout.Iteration.Take(77));
        Assert.Equal(defaults.Where(defaultLeft.Contains), layout.Iteration.Skip(77));
    }

    [Fact]
    public void ARightColumnTabOrderedLastIsLeftOut()
    {
        var config = new Dictionary<string, double>(Vanilla) { ["mod05"] = 2.0 };
        var layout = TabLayout.ForModMode(PackDefaults(), ModTabs, OrderOf(config));
        Assert.True(layout.Ideal);
        Assert.DoesNotContain("mod05", layout.Iteration);
    }

    [Fact]
    public void LeftTabsOrderedAfterTheModTabsCantBeIdealButAllTabsShow()
    {
        // Only 14 vanilla tabs and three ordered after 1.0: those three are in the left 16 and sort after the mod tabs.
        var config = new Dictionary<string, double>(Vanilla) { ["late1"] = 3, ["late2"] = 3, ["late3"] = 3 };
        var defaults = Vanilla.Keys.Concat(["late1", "late2", "late3", "plain"]).ToList();
        var layout = TabLayout.ForModMode(defaults, ModTabs, OrderOf(config));
        Assert.False(layout.Ideal);
        Assert.Equal(TabLayout.Order(defaults, OrderOf(config)).Take(16).Concat(ModTabs).Order(), layout.Iteration.Order());
        Assert.Equal(16 + 77, layout.Left.Count + layout.Right.Count);
    }

    [Fact]
    public void FewDefaultTabsShareTheLeftColumnWithModTabs()
    {
        var defaults = new List<string> { "general", "flora", "mod00" };
        var layout = TabLayout.ForModMode(defaults, ModTabs.Take(20).ToList(), OrderOf(Vanilla));
        Assert.False(layout.Ideal);
        Assert.Equal(["general", "flora", "mod00", .. ModTabs.Take(13)], layout.Left);
        Assert.Equal(ModTabs.Skip(13).Take(7), layout.Right);

        var sixteen = PackDefaults().Where(c => Vanilla.ContainsKey(c) || c is "mod00" or "mod01").ToList();
        Assert.True(TabLayout.ForModMode(sixteen, ModTabs, OrderOf(Vanilla)).Ideal);
    }

    [Fact]
    public void AModTabGivenAnOrderIsNotIdeal()
    {
        var config = new Dictionary<string, double>(Vanilla) { ["seraphhorizons-modtab-05"] = 0.05 };
        var layout = TabLayout.ForModMode(PackDefaults(), ModTabs, OrderOf(config));
        Assert.False(layout.Ideal);
        Assert.Contains("seraphhorizons-modtab-05", layout.Left);
    }

    [Theory]
    [InlineData("Plain", "Plain")]
    [InlineData("A {b} c", "A {{b}} c")]
    public void LangValueDoublesBraces(string name, string value)
    {
        Assert.Equal(value, TabLayout.LangValue(name));
        Assert.Equal(name, string.Format(TabLayout.LangValue(name)));
    }
}
