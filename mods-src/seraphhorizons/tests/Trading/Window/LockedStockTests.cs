using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Window.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Window;

public class LockedStockTests
{
    private static TradeEntry E(string code, int tier = 0, bool rare = false, string? kind = null) =>
        new() { Code = code, Stock = new NatSpec(1, 0), StandingTier = tier, Rare = rare, Kind = kind };

    private static readonly bool[] Rare = [false, false, false, true, true];

    private static TradeSide Side() => new()
    {
        Core = [E("coke"), E("schematic-a", tier: 2), E("schematic-b", tier: 3), E("anthracite", rare: true), E("lead", kind: "lead")],
        Rotating = new RotatingList { MaxItems = 2, List = [E("bow", rare: true), E("charcoal"), E("crock", tier: 1)] },
    };

    private static readonly Region Here = new(Region.Temperate, Region.Sedimentary);

    [Fact]
    public void AStrangerSeesEveryGatedAndRareGoodWithTheTierThatUnlocksIt()
    {
        var rows = LockedStock.Of(Side(), Here, 0, Rare, new HashSet<string>());
        // By tier, then list order (the core before the rotating pool).
        Assert.Equal(["game:crock", "game:schematic-a", "game:anthracite", "game:schematic-b", "game:bow"], rows.Select(r => r.Code));
        Assert.Equal([1, 2, 3, 3, 3], rows.Select(r => r.Tier));
        Assert.Equal(LockReason.Rare, rows.Single(r => r.Code == "game:anthracite").Reason);
        Assert.Equal(LockReason.Tier, rows.Single(r => r.Code == "game:schematic-b").Reason);
        Assert.True(rows.Single(r => r.Code == "game:bow").Rotating);
        Assert.True(rows.Single(r => r.Code == "game:crock").Rotating);
    }

    [Fact]
    public void ARegularHasTheirTiersGoods()
    {
        var rows = LockedStock.Of(Side(), Here, 2, Rare, new HashSet<string>());
        Assert.Equal(["game:anthracite", "game:schematic-b", "game:bow"], rows.Select(r => r.Code));
    }

    [Fact]
    public void WhatIsOnTheShelfAnywayIsNotLocked()
    {
        var shelved = new HashSet<string> { E("schematic-b", tier: 3).Key };
        var rows = LockedStock.Of(Side(), Here, 0, Rare, shelved);
        Assert.DoesNotContain(rows, r => r.Code == "game:schematic-b");
    }

    [Fact]
    public void ATrustedPlayerHasNothingLockedAndMapsAreLeftToTheirTab()
    {
        Assert.Empty(LockedStock.Of(Side(), Here, 3, Rare, new HashSet<string>()));
        Assert.DoesNotContain(LockedStock.Of(Side(), Here, 0, Rare, new HashSet<string>()), r => r.Code == "game:lead");
    }

    [Fact]
    public void AtMostTheShownCount() =>
        Assert.Equal(2, LockedStock.Of(Side(), Here, 0, Rare, new HashSet<string>(), max: 2).Count);

    [Fact]
    public void FirstTierFindsTheLowestTierWithTheUnlock()
    {
        Assert.Equal(3, LockedStock.FirstTier(Rare));
        Assert.Equal(-1, LockedStock.FirstTier([false, false]));
    }
}
