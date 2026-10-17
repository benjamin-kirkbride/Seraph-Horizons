using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.StampBattery.Core;
using Xunit;

namespace SeraphHorizons.Tests.StampBattery;

/// <summary>The stamp battery's job (#728): what it takes, and that a rock comes out crushed, classified,
/// one for one and with no loss.</summary>
public class StampingTests
{
    [Theory]
    [InlineData("game:ore-rich-galena-granite", OreState.None, StampFeed.Crush)]
    [InlineData("game:ore-poor-quartz_nativegold-andesite", OreState.None, StampFeed.Crush)]
    [InlineData("game:crystalizedore-medium-nativecopper-basalt", OreState.None, StampFeed.Crush)]
    [InlineData("seraphhorizons:crushedore-galena-rich", OreState.None, StampFeed.Screen)]
    [InlineData("seraphhorizons:crushedore-galena-rich", OreState.Amalgamated, StampFeed.Screen)]
    [InlineData("seraphhorizons:crushedore-galena-rich", OreState.Classified, StampFeed.None)]
    [InlineData("seraphhorizons:groundore-galena-rich", OreState.None, StampFeed.None)]
    [InlineData("seraphhorizons:concentrate-galena", OreState.None, StampFeed.None)]
    [InlineData("game:ore-quartz-granite", OreState.None, StampFeed.None)]
    [InlineData("game:ore-rich-notanore-granite", OreState.None, StampFeed.None)]
    [InlineData("game:rock-granite", OreState.None, StampFeed.None)]
    [InlineData(null, OreState.None, StampFeed.None)]
    public void It_takes_raw_ore_and_unclassified_crushed_ore(string? code, OreState state, StampFeed expected) =>
        Assert.Equal(expected, Stamping.FeedOf(code, state));

    [Fact]
    public void Raw_ore_comes_out_crushed_classified_with_its_units_and_rock()
    {
        var feed = OreStream.FromRock("galena", "rich", OreForm.Raw);
        var product = Stamping.Stamp("game:ore-rich-galena-granite", feed)!.Value;
        Assert.Equal("seraphhorizons:crushedore-galena-rich", product.Code);
        Assert.Equal(1, product.Count);
        Assert.Equal(7, product.Stream.Units);
        Assert.True(product.Stream.State.HasFlag(OreState.Classified));
        Assert.Equal("granite", product.Stream.Rock);
        Assert.Null(product.Stream.Locked);
    }

    [Fact]
    public void Crystallised_ore_comes_out_as_two_holding_all_its_units()
    {
        var feed = OreStream.FromRock("nativecopper", "medium", OreForm.Raw, units: 10);
        var product = Stamping.Stamp("game:crystalizedore-medium-nativecopper-basalt", feed)!.Value;
        Assert.Equal("seraphhorizons:crushedore-nativecopper-medium", product.Code);
        Assert.Equal(2, product.Count);
        Assert.Equal(10, product.Count * product.Stream.Units);
        Assert.Equal("basalt", product.Stream.Rock);
    }

    [Fact]
    public void Crushed_ore_keeps_its_stream_and_is_classified()
    {
        var feed = new OreStream("galena", "rich", 7, null, OreState.Amalgamated, WorkedBy.None, "slate");
        var product = Stamping.Stamp("seraphhorizons:crushedore-galena-rich", feed)!.Value;
        Assert.Equal("seraphhorizons:crushedore-galena-rich", product.Code);
        Assert.Equal(1, product.Count);
        Assert.Equal(OreState.Amalgamated | OreState.Classified, product.Stream.State);
        Assert.Equal("slate", product.Stream.Rock);
        Assert.Equal(7, product.Stream.Units);
    }

    [Fact]
    public void A_rock_it_does_not_take_gives_nothing() =>
        Assert.Null(Stamping.Stamp("seraphhorizons:crushedore-galena-rich",
            new OreStream("galena", "rich", 7, null, OreState.Classified)));

    [Fact]
    public void Screening_lifts_the_unclassified_share_once()
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<OreProcessingConfig>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        var recovery = new OreRecovery(config);
        var raw = OreStream.FromRock("hematite", "medium", OreForm.Raw);
        var stamped = Stamping.Stamp("game:ore-medium-hematite-granite", raw)!.Value.Stream;
        Assert.Equal(1.0, recovery.LockFactor(recovery.Ore("hematite"), "medium", stamped.State), 9);
        Assert.Equal(0.85, recovery.LockFactor(recovery.Ore("hematite"), "medium", raw.State), 9);
    }
}
