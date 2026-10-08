using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

public class HandbookTradersTests
{
    private static readonly ListedTrader[] Listed =
    [
        new("game:trader-male-agriculture-temperate", "EntityTrader"),
        new("game:trader-female-agriculture-cold", "EntityTrader"),
        new("game:trader-male-treasurehunter-temperate", "EntityTrader"),
        new("game:trader-male-treasurehunter-desert", "EntityTrader"),
        new("game:trader-female-treasurehunter-cold", "EntityTrader"),
        new("game:villager-nadiya-female-tailor-alba", "EntityVillager"),
        new("aculinaryartillery:trader-kitchenware", "EntityTrader"),
        new("game:trader-domesticanimal-male", "EntityTradingHumanoid"),
        new("somemod:wandering-trader-bob", "EntityTradingHumanoid"),
        new("seraphhorizons:trader-male-smith-temperate", "EntitySeraphTrader"),
    ];

    [Fact]
    public void OtherTradersAreTheGamesAndOtherModsNeverThePacks()
    {
        Assert.True(HandbookTraders.IsOtherTrader("game", "trader-male-agriculture-temperate", "EntityTrader"));
        Assert.True(HandbookTraders.IsOtherTrader("game", "trader-domesticanimal-male", "EntityTradingHumanoid"));
        Assert.True(HandbookTraders.IsOtherTrader("somemod", "wandering-trader-bob", null));
        Assert.True(HandbookTraders.IsOtherTrader("somemod", "merchant", "EntityTrader"));
        Assert.False(HandbookTraders.IsOtherTrader("game", "villager-nadiya-female-tailor-alba", "EntityVillager"));
        Assert.False(HandbookTraders.IsOtherTrader("seraphhorizons", "trader-male-smith-temperate", "EntityTrader"));
    }

    [Fact]
    public void StoryTradersStayInEveryClimateAndTheRestOfTheOthersGo()
    {
        var hidden = HandbookTraders.Hidden(Listed, ["game:trader-male-treasurehunter-temperate", "trader-female-treasurehunter-temperate"]);
        Assert.Equal(
        [
            "aculinaryartillery:trader-kitchenware",
            "game:trader-domesticanimal-male",
            "game:trader-female-agriculture-cold",
            "game:trader-male-agriculture-temperate",
            "somemod:wandering-trader-bob",
        ], hidden);
    }

    [Fact]
    public void WithoutStoryStructuresTheStoryTradersGoToo()
    {
        var hidden = HandbookTraders.Hidden(Listed, []);
        Assert.Contains("game:trader-male-treasurehunter-temperate", hidden);
        Assert.DoesNotContain("game:villager-nadiya-female-tailor-alba", hidden);
        Assert.DoesNotContain("seraphhorizons:trader-male-smith-temperate", hidden);
    }

    [Fact]
    public void ANameStaysWhileATraderStillMetHasIt()
    {
        var names = new[]
        {
            ("game:trader-male-agriculture-temperate", "Agriculture trader"),
            ("game:trader-female-agriculture-cold", "Agriculture trader"),
            ("game:trader-male-treasurehunter-temperate", "Treasure hunter"),
            ("game:trader-female-treasurehunter-cold", "Treasure hunter"),
            ("aculinaryartillery:trader-kitchenware", "Kitchenware trader"),
            ("somemod:wandering-trader-bob", "Treasure hunter"),
        };
        var hidden = HandbookTraders.HiddenNames(names,
            ["game:trader-male-agriculture-temperate", "game:trader-female-agriculture-cold", "aculinaryartillery:trader-kitchenware", "somemod:wandering-trader-bob"]);
        Assert.Equal(new HashSet<string> { "Agriculture trader", "Kitchenware trader" }, hidden);
    }
}
