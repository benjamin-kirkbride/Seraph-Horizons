using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Trading.Maps.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Maps;

public class MapDistanceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(499, 0)]
    [InlineData(500, 1)]
    [InlineData(1000, 1)]
    [InlineData(1499, 1)]
    [InlineData(1500, 2)]
    [InlineData(2210.4, 2)]
    [InlineData(12_749, 13)]
    [InlineData(-5, 0)]
    public void DistancesRoundToTheNearestKm(double blocks, int km) => Assert.Equal(km, MapDistance.Km(blocks));

    [Fact]
    public void UnderHalfAKmIsSaidSoAndTheRestInKm()
    {
        Assert.Equal((MapDistance.UnderKey, 0), (MapDistance.Of(120).Key, MapDistance.Of(120).Args.Length));
        Assert.Equal(MapDistance.UnderKey, MapDistance.Of(499.9).Key);
        var (key, args) = MapDistance.Of(3400);
        Assert.Equal(MapDistance.KmKey, key);
        Assert.Equal([3], args);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 7)]
    [InlineData(5, 11)]
    public void ATiersReachInRingsIsSaidInKm(int rings, int km) => Assert.Equal(km, MapDistance.ReachKm(rings));

    [Fact]
    public void TheLangHasBothDistanceKeys()
    {
        var lang = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "lang-en.json")))!.AsObject();
        Assert.True(lang.ContainsKey(MapDistance.KmKey));
        Assert.True(lang.ContainsKey(MapDistance.UnderKey));
        Assert.False(lang.ContainsKey("trading-window-fact-leads-one"));
    }
}
