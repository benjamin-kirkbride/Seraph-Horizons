using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

/// <summary>
/// Trading/Core/StoryTraders.cs: the game's treasure hunter becomes the pack's treasure hunter (same
/// gender and outfit set), a type of its own that the grid never draws and that is no visitor, and
/// one standing in its story structure is known to standing by that structure.
/// </summary>
public class StoryTradersTests
{
    [Theory]
    [InlineData("game:trader-male-treasurehunter-temperate", "trader-male-treasurehunter-temperate")]
    [InlineData("game:trader-female-treasurehunter-cold", "trader-female-treasurehunter-cold")]
    [InlineData("trader-female-treasurehunter-desert", "trader-female-treasurehunter-desert")]
    [InlineData("game:trader-male-agriculture-temperate", null)]
    [InlineData("seraphhorizons:trader-male-treasurehunter-temperate", null)]
    [InlineData("somemod:trader-male-treasurehunter-temperate", null)]
    [InlineData("game:humanoid-trader-treasurehunter", null)]
    [InlineData("game:villager-nadiya-female-tailor-alba", null)]
    public void OnlyTheGamesTreasureHunterIsTakenOver(string code, string? pack)
    {
        Assert.Equal(pack, StoryTraders.PackPath(code));
        if (pack != null) Assert.Equal(TraderTypes.TreasureHunter, pack.Split('-')[2]);
    }

    [Fact]
    public void TheTreasureHunterIsAListedTypeButNeitherACampTypeNorAVisitor()
    {
        Assert.Contains(TraderTypes.TreasureHunter, TraderTypes.Story);
        Assert.DoesNotContain(TraderTypes.TreasureHunter, TraderTypes.All);
        Assert.DoesNotContain(TraderTypes.TreasureHunter, TraderTypes.Visitors);
        Assert.Contains(TraderTypes.TreasureHunter, TraderTypes.Listed);
        // Every story type has its structure.
        Assert.All(TraderTypes.Story, t => Assert.True(StoryTraders.Structures.ContainsKey(t), t));
        Assert.Equal(TraderTypes.TreasureHunter, StoryTraders.TypeOfStructure("treasurehunter"));
        Assert.Null(StoryTraders.TypeOfStructure("resonancearchive"));
    }

    [Fact]
    public void InItsOwnStructureTheTreasureHunterIsThatStructure()
    {
        Assert.Equal("story:treasurehunter", StoryTraders.IdAt(TraderTypes.TreasureHunter, "treasurehunter"));
        Assert.True(TraderIds.IsValid(StoryTraders.IdAt(TraderTypes.TreasureHunter, "treasurehunter")!));
        // Anywhere else (spawned by a command, carried off) it is an entity as any trader outside a camp.
        Assert.Null(StoryTraders.IdAt(TraderTypes.TreasureHunter, null));
        Assert.Null(StoryTraders.IdAt(TraderTypes.TreasureHunter, "village"));
        // A camp type in a story structure is not made a story trader.
        Assert.Null(StoryTraders.IdAt(TraderTypes.CurioDealer, "treasurehunter"));
    }
}
