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
    public void ShippedTableParses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "item-values.json");
        var table = ItemValues.Parse(File.ReadAllText(path));
        Assert.True(table.Count > 10_000);
        Assert.Equal(1.0, table.ValueOf("game:gear-rusty"));
        Assert.InRange(table.ValueOf("game:ingot-copper"), 1, 4);
    }
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
