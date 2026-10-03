using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class EntityReachTests
{
    // The default setting, and the Cartwright's Caravan 1.9.1 entity codes it is for.
    private static readonly EntityReach Default = new(["cartwrightscaravan:*"]);

    [Theory]
    [InlineData("cartwrightscaravan:cart-basiccart-classic-oak")]
    [InlineData("cartwrightscaravan:cart-slimcart-classic-aged")]
    [InlineData("cartwrightscaravan:sled-oak")]
    [InlineData("cartwrightscaravan:marketstall-smallstall-classic-oak")]
    public void DefaultCoversCartwrightsEntities(string code) => Assert.True(Default.Applies(code));

    [Theory]
    [InlineData("game:boat-raft")]
    [InlineData("game:player")]
    [InlineData("yangtransport:sglocomotive")]
    [InlineData("cartwrightscaravanextra:cart-x")]
    public void DefaultLeavesOtherEntitiesAlone(string code) => Assert.False(Default.Applies(code));

    [Fact]
    public void PatternsAreNormalized()
    {
        var rule = new EntityReach(["  CartwrightsCaravan:Cart-* ", "boat-*", "", null, "   ", "boat-*"]);
        Assert.Equal(["cartwrightscaravan:cart-*", "game:boat-*"], rule.Patterns);
        Assert.True(rule.Applies("cartwrightscaravan:cart-basiccart-classic-oak"));
        Assert.False(rule.Applies("cartwrightscaravan:sled-oak"));
        // No domain means game, for the code as for the pattern.
        Assert.True(rule.Applies("game:boat-sailed-oak"));
        Assert.True(rule.Applies("boat-sailed-oak"));
        Assert.False(rule.Applies("othermod:boat-sailed-oak"));
    }

    [Fact]
    public void NoPatternsMatchNothing()
    {
        Assert.Empty(new EntityReach(null).Patterns);
        Assert.False(new EntityReach([]).Applies("cartwrightscaravan:cart-basiccart-classic-oak"));
    }

    [Theory]
    [InlineData(4.4, true)]
    [InlineData(4.5, true)]
    [InlineData(4.51, false)]
    [InlineData(10, false)]
    public void ReachIsThePickingRangeToTheHit(double distance, bool inReach) =>
        Assert.Equal(inReach, EntityReach.InReach(distance * distance, 4.5));

    [Fact]
    public void AFarHitReplacesOnlyWhatIsFartherAway()
    {
        const double range = 4.5;
        // Nothing picked: any hit in reach.
        Assert.True(EntityReach.Replaces(4.0 * 4.0, range, double.PositiveInfinity));
        // Out of reach never, even with nothing picked.
        Assert.False(EntityReach.Replaces(4.6 * 4.6, range, double.PositiveInfinity));
        // A nearer block or entity stays picked; a tie keeps the game's pick.
        Assert.False(EntityReach.Replaces(3.0 * 3.0, range, 2.0 * 2.0));
        Assert.False(EntityReach.Replaces(3.0 * 3.0, range, 3.0 * 3.0));
        Assert.True(EntityReach.Replaces(3.0 * 3.0, range, 3.5 * 3.5));
    }
}
