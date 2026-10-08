using SeraphHorizons.Mod.Trading.Values.Core;

namespace SeraphHorizons.Mod.Trading.Values.Tests;

public class ItemValuesTests
{
    private static readonly ItemValues Table = ItemValues.Parse("""
        {
          "values": {
            "game:plank-birch": 0.06,
            "game:plank-pine": 0.08,
            "game:axe-felling-copper": 4.0,
            "game:axe-felling-tinbronze": 6.0,
            "game:axe-chopping-copper": 10.0,
            "game:stone-granite": 0.008,
            "game:ingot-copper": 2.07
          },
          "floorZero": ["game:stone-granite"]
        }
        """);

    [Fact]
    public void DirectHit()
    {
        var l = Table.Lookup("game:ingot-copper");
        Assert.Equal(ValueSource.Direct, l.Source);
        Assert.Equal(2.07, l.Value);
        Assert.Equal(2.07, Table.ValueOf("game:ingot-copper"));
        Assert.False(Table.IsWorthless("game:ingot-copper"));
    }

    [Fact]
    public void MissingDomainMeansGame() => Assert.Equal(2.07, Table.ValueOf("ingot-copper"));

    [Fact]
    public void FloorZeroIsWorthlessButKeepsItsValue()
    {
        var l = Table.Lookup("game:stone-granite");
        Assert.True(l.FloorZero);
        Assert.Equal(0.008, l.Value);
        Assert.Equal(0, l.Effective);
        Assert.True(Table.IsWorthless("game:stone-granite"));
    }

    [Fact]
    public void MissingVariantFallsBackToItsFamilyAverage()
    {
        var l = Table.Lookup("game:plank-oak");
        Assert.Equal(ValueSource.Family, l.Source);
        Assert.Equal("game:plank-*", l.Family);
        Assert.Equal(2, l.Members);
        Assert.Equal(0.07, l.Value, 3);
    }

    [Fact]
    public void ClosestFamilyWins()
    {
        // The felling axes, not every axe.
        var l = Table.Lookup("game:axe-felling-iron");
        Assert.Equal("game:axe-felling-*", l.Family);
        Assert.Equal(5.0, l.Value, 3);
        Assert.Equal("game:axe-*", Table.Lookup("game:axe-bearded-iron").Family);
    }

    [Fact]
    public void FamilyKeepsTheFirstSegment()
    {
        // game:plank-oak must never average over all of game:.
        Assert.Equal(ValueSource.Missing, Table.Lookup("game:log-placed-oak-ud").Source);
        Assert.Equal(ValueSource.Missing, Table.Lookup("game:gear").Source);
        Assert.Equal(0, Table.ValueOf("game:log-placed-oak-ud"));
        Assert.False(Table.IsWorthless("game:log-placed-oak-ud"));
    }

    [Fact]
    public void WildcardAveragesItsMatches()
    {
        var l = Table.Lookup("game:axe-*-copper");
        Assert.Equal(ValueSource.Family, l.Source);
        Assert.Equal(7.0, l.Value, 3);
        Assert.Equal(ValueSource.Missing, Table.Lookup("game:nothing-*").Source);
    }

    [Fact]
    public void FamilyOfOnlyWorthlessMembersIsWorthless() =>
        Assert.True(Table.IsWorthless("game:stone-andesite"));

    [Fact]
    public void FamilyPrefixesLongestFirst() =>
        Assert.Equal(["game:a-b-c-", "game:a-b-", "game:a-"], ItemValues.FamilyPrefixes("game:a-b-c-d"));

    [Fact]
    public void CachedWildcardMatchesUncached()
    {
        foreach (var p in new[] { "game:plank-*", "game:axe-*-copper", "game:*", "game:a*", "game:*-copper", "game:nothing-*" })
        {
            var uncached = Table.WildcardUncached(p);
            Assert.Equal(uncached, Table.Lookup(p));
            Assert.Equal(uncached, Table.Lookup(p));
        }
        // Normalised before the cache: the same answer under any spelling.
        Assert.Equal(Table.Lookup("game:plank-*"), Table.Lookup("Plank-*"));
    }

    [Fact]
    public void WildcardCacheBelongsToItsTable()
    {
        // The table is immutable; a reload builds a new one, whose answers come from its own codes.
        Assert.Equal(0.07, Table.Lookup("game:plank-*").Value);
        var reloaded = ItemValues.Parse("""{ "values": { "game:plank-oak": 1.0 } }""");
        Assert.Equal(1.0, reloaded.Lookup("game:plank-*").Value);
        Assert.Equal(1, reloaded.Lookup("game:plank-*").Members);
        Assert.Equal(0.07, Table.Lookup("game:plank-*").Value);
    }

    [Fact]
    public void ShippedTableWildcardsMatchUncached()
    {
        // Patterns built like recipe wildcards: each code's last segment, and a middle one, starred.
        var table = ItemValues.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "item-values.json")));
        var patterns = table.Codes.Where(c => c.Contains('-'))
            .SelectMany(c => new[] { c[..(c.LastIndexOf('-') + 1)] + "*", c[..(c.IndexOf('-') + 1)] + "*" + c[c.LastIndexOf('-')..] })
            .Distinct().Take(300).ToList();
        foreach (var p in patterns)
            Assert.Equal(table.WildcardUncached(p), table.Lookup(p));
    }

    [Fact]
    public void ShippedTableParses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "item-values.json");
        var table = ItemValues.Parse(File.ReadAllText(path));
        Assert.True(table.Count > 10_000);
        Assert.Equal(1.0, table.ValueOf("game:gear-rusty"));
        Assert.InRange(table.ValueOf("game:ingot-copper"), 1, 4);
    }
    // Liquids are priced per litre (perLitre: items per litre); everything else per item.
    private static readonly ItemValues Liquids = ItemValues.Parse("""
        {
          "values": {
            "game:ciderportion-apple": 1.85,
            "game:ciderportion-pear": 2.15,
            "game:waterportion": 0.01,
            "game:juiceportion-apple": 0.9,
            "game:juiceportion-cherry": 1.3,
            "game:juiceportion-pressed": 0.5,
            "game:mixed-portion-a": 3.0,
            "game:mixed-solid-b": 1.0,
            "game:odd-litre-a": 2.0,
            "game:odd-litre-b": 2.0,
            "game:ingot-copper": 2.07
          },
          "floorZero": ["game:waterportion"],
          "perLitre": {
            "game:ciderportion-apple": 100,
            "game:ciderportion-pear": 100,
            "game:waterportion": 100,
            "game:juiceportion-apple": 100,
            "game:juiceportion-cherry": 100,
            "game:juiceportion-pressed": 100,
            "game:mixed-portion-a": 100,
            "game:odd-litre-a": 100,
            "game:odd-litre-b": 4.0,
            "game:notinthetable": 100
          },
          "switches": {}
        }
        """);

    [Fact]
    public void PerLitreDirectHit()
    {
        var l = Liquids.Lookup("game:ciderportion-apple");
        Assert.Equal(ValueSource.Direct, l.Source);
        Assert.Equal(100, l.PerLitre);
        Assert.Equal(1.85, l.Display);
        Assert.Equal(1.85, l.PerLitreValue);
        Assert.Equal(0.0185, l.Value, 12);
        Assert.Equal(100, Liquids.PerLitre("ciderportion-apple"));
        Assert.Equal((1.85, 100), Liquids.DisplayValue("game:ciderportion-apple"));
    }

    [Fact]
    public void ValueOfIsPerItemForALiquid()
    {
        // Trading prices stacks of portions: a litre's worth of portions is worth a litre's value.
        Assert.Equal(0.0185, Liquids.ValueOf("game:ciderportion-apple"), 12);
        Assert.Equal(1.85, Liquids.ValueOf("game:ciderportion-apple") * 100, 9);
        Assert.Equal(0.0185, Liquids.Lookup("game:ciderportion-apple").Effective, 12);
        Assert.False(Liquids.IsWorthless("game:ciderportion-apple"));
    }

    [Fact]
    public void SolidsStayPerItem()
    {
        var l = Liquids.Lookup("game:ingot-copper");
        Assert.Null(l.PerLitre);
        Assert.Null(l.PerLitreValue);
        Assert.Equal(2.07, l.Display);
        Assert.Equal(2.07, Liquids.ValueOf("game:ingot-copper"));
        Assert.Equal((2.07, (int?)null), Liquids.DisplayValue("game:ingot-copper"));
        Assert.Null(Liquids.PerLitre("game:ingot-copper"));
        Assert.Null(Liquids.PerLitre("game:nothing"));
        // A perLitre code with no value is still missing, and not a liquid.
        Assert.Equal(ValueSource.Missing, Liquids.Lookup("game:notinthetable").Source);
        Assert.Null(Liquids.PerLitre("game:notinthetable"));
    }

    [Fact]
    public void ShownIsPerLitreForALiquid()
    {
        Assert.Equal(1.85, Liquids.Shown("game:ciderportion-apple", _ => false));
        var l = Liquids.ShownLookup("game:ciderportion-apple", _ => false);
        Assert.Equal(100, l?.PerLitre);
        Assert.Equal(2.07, Liquids.Shown("game:ingot-copper", _ => false));
        Assert.Null(Liquids.ShownLookup("game:nothing", _ => false));
    }

    [Fact]
    public void FamilyOfLiquidsIsPerLitre()
    {
        // A missing cider falls back to the ciders, per litre: (1.85 + 2.15) / 2 = 2.0 a litre.
        var l = Liquids.Lookup("game:ciderportion-cherry");
        Assert.Equal(ValueSource.Family, l.Source);
        Assert.Equal("game:ciderportion-*", l.Family);
        Assert.Equal(2, l.Members);
        Assert.Equal(100, l.PerLitre);
        Assert.Equal(2.0, l.Display, 9);
        Assert.Equal(0.02, l.Value, 12);
        Assert.Equal(0.02, Liquids.ValueOf("game:ciderportion-cherry"), 12);
        // Rounded to three decimals per litre, not per item (which would take most of a portion's
        // value): (0.9 + 1.3 + 0.5) / 3 = 0.9 a litre, 0.009 a portion.
        var juice = Liquids.Lookup("game:juiceportion-grape");
        Assert.Equal(0.9, juice.Display, 9);
        Assert.Equal(0.009, juice.Value, 12);
    }

    [Fact]
    public void WildcardOfLiquidsIsPerLitre()
    {
        var l = Liquids.Lookup("game:ciderportion-*");
        Assert.Equal(100, l.PerLitre);
        Assert.Equal(2.0, l.Display, 9);
        Assert.Equal(0.02, l.Value, 12);
        Assert.Equal(Liquids.WildcardUncached("game:ciderportion-*"), l);
    }

    [Fact]
    public void MixedFamilyIsPerItem()
    {
        // A liquid and a solid: the average of their per-item values (0.03 and 1.0), per item.
        var l = Liquids.Lookup("game:mixed-other-c");
        Assert.Equal("game:mixed-*", l.Family);
        Assert.Null(l.PerLitre);
        Assert.Equal(0.515, l.Value, 9);
        Assert.Equal(0.515, l.Display, 9);
        Assert.Null(Liquids.Lookup("game:mixed-*").PerLitre);
        // Two liquids at different items per litre (100 and 4): per item, 0.02 and 0.5.
        var odd = Liquids.Lookup("game:odd-litre-c");
        Assert.Null(odd.PerLitre);
        Assert.Equal(0.26, odd.Value, 9);
    }

    [Fact]
    public void FloorZeroOnALiquid()
    {
        // Water: worth under a gear per full stack, so trading gives nothing; the value stays per litre.
        var l = Liquids.Lookup("game:waterportion");
        Assert.True(l.FloorZero);
        Assert.Equal(0.01, l.Display);
        Assert.Equal(0.0001, l.Value, 12);
        Assert.Equal(0, l.Effective);
        Assert.Equal(0, Liquids.ValueOf("game:waterportion"));
        Assert.True(Liquids.IsWorthless("game:waterportion"));
        Assert.Equal(0.01, Liquids.Shown("game:waterportion", _ => false));
    }

    [Fact]
    public void ItemsPerLitreMustBePositive() =>
        Assert.Throws<ArgumentException>(() => ItemValues.Parse("""
            { "values": { "game:x-a": 1 }, "perLitre": { "game:x-a": 0 } }
            """));

    [Fact]
    public void ATableWithoutPerLitreIsAllPerItem() =>
        Assert.All(Table.Codes, c => Assert.Null(Table.PerLitre(c)));

    private static readonly ItemValues Switched = ItemValues.Parse("""
        {
          "values": {
            "seraphhorizons:gear-steel": 14.2,
            "seraphhorizons:largegear-steel": 40.5,
            "game:gear-rusty": 1.0,
            "game:plank-pine": 0.08
          },
          "floorZero": [],
          "switches": {
            "seraphhorizons:gear-steel": ["GearCutter", "GearBlanks"],
            "seraphhorizons:largegear-steel": ["GearCutter"]
          }
        }
        """);

    [Fact]
    public void SwitchesParse()
    {
        Assert.Equal(["GearCutter", "GearBlanks"], Switched.SwitchesOf("seraphhorizons:gear-steel"));
        Assert.Empty(Switched.SwitchesOf("game:gear-rusty"));
        Assert.Empty(Switched.SwitchesOf("game:nothing"));
        Assert.Empty(Table.SwitchesOf("game:ingot-copper"));
    }

    [Fact]
    public void ShownHidesAValueWhoseSwitchIsOff()
    {
        Assert.Equal(14.2, Switched.Shown("seraphhorizons:gear-steel", _ => false));
        Assert.Null(Switched.Shown("seraphhorizons:gear-steel", s => s == "GearBlanks"));
        Assert.Equal(14.2, Switched.Shown("seraphhorizons:gear-steel", s => s == "Rosser"));
        Assert.Equal(1.0, Switched.Shown("gear-rusty", _ => true));
    }

    [Fact]
    public void ShownHasNothingForAMissingCodeAndFallsBackToFamilies()
    {
        Assert.Null(Switched.Shown("game:nothing", _ => false));
        Assert.Equal(0.08, Switched.Shown("game:plank-oak", _ => false));
    }
}
