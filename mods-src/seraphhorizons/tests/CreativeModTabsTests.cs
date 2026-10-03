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

public class TabScrollTests
{
    // The strip at GUI scale 1: 25 px tabs, 5 px apart, a 545 px viewport; 75 tabs make 2250 px.
    const double H = 25, Sp = 5, View = 545;
    static readonly double Content = TabScroll.ContentHeight(75, H, Sp);

    [Fact]
    public void ContentAndClamp()
    {
        Assert.Equal(2250, Content);
        Assert.Equal(0, TabScroll.ContentHeight(0, H, Sp));
        Assert.Equal(0, TabScroll.Clamp(-10, Content, View));
        Assert.Equal(2250 - 545, TabScroll.Clamp(1e9, Content, View));
        Assert.Equal(0, TabScroll.Clamp(double.NaN, Content, View));
        Assert.Equal(0, TabScroll.Clamp(100, 300, View));   // fits: never scrolls
    }

    [Fact]
    public void WheelUpScrollsUp()
    {
        Assert.Equal(90, TabScroll.Wheel(180, 1, 90, Content, View));
        Assert.Equal(270, TabScroll.Wheel(180, -1, 90, Content, View));
        Assert.Equal(0, TabScroll.Wheel(30, 2, 90, Content, View));
    }

    [Fact]
    public void EnsureVisibleScrollsTheLeastNeeded()
    {
        Assert.Equal(0, TabScroll.EnsureVisible(0, 3, H, Sp, Content, View));            // already in view
        Assert.Equal(40 * 30 + 25 - 545, TabScroll.EnsureVisible(0, 40, H, Sp, Content, View));   // below: bottom edge
        Assert.Equal(10 * 30, TabScroll.EnsureVisible(1000, 10, H, Sp, Content, View)); // above: top edge
        Assert.Equal(74 * 30 + 25 - 545, TabScroll.EnsureVisible(0, 74, H, Sp, Content, View));
        Assert.Equal(200, TabScroll.EnsureVisible(200, -1, H, Sp, Content, View));
    }

    [Fact]
    public void HitTestFindsTheTabUnderTheMouse()
    {
        Assert.Equal(0, TabScroll.HitTest(0, 0, 75, H, Sp, View));
        Assert.Equal(0, TabScroll.HitTest(24.9, 0, 75, H, Sp, View));
        Assert.Equal(-1, TabScroll.HitTest(27, 0, 75, H, Sp, View));    // the gap
        Assert.Equal(1, TabScroll.HitTest(30, 0, 75, H, Sp, View));
        Assert.Equal(11, TabScroll.HitTest(30, 300, 75, H, Sp, View));  // scrolled by 10 tabs
        Assert.Equal(-1, TabScroll.HitTest(-1, 0, 75, H, Sp, View));
        Assert.Equal(-1, TabScroll.HitTest(View, 0, 75, H, Sp, View));
        Assert.Equal(-1, TabScroll.HitTest(100, 0, 2, H, Sp, View));    // past the last tab
    }

    [Fact]
    public void IndicatorOnlyWhenOverflowing()
    {
        Assert.Null(TabScroll.Indicator(0, 300, View, 20));
        var top = TabScroll.Indicator(0, Content, View, 20)!.Value;
        Assert.Equal(0, top.Top);
        Assert.Equal(View * View / Content, top.Length, 6);
        var bottom = TabScroll.Indicator(TabScroll.MaxOffset(Content, View), Content, View, 20)!.Value;
        Assert.Equal(View, bottom.Top + bottom.Length, 6);
    }
}
