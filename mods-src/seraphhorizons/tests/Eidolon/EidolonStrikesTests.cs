using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Self-defence's blows (<see cref="EidolonStrikes"/>): which it strikes, when one lands, how
/// far it leads a creature on the move, and that each names an animation the shape has, of its length.</summary>
public class EidolonStrikesTests
{
    private const double Radii = (1.7 + 1.2) / 2; // the eidolon's box and a wolf-sized one

    private static Strike[] All => [EidolonStrikes.Punch, EidolonStrikes.Kick, EidolonStrikes.Slam, EidolonStrikes.Jab, EidolonStrikes.Hammer];

    [Fact]
    public void StandingBlowsGoPunchKickSlamAndOnlyTheThirdIsHeavy()
    {
        Assert.Equal(["punch", "kick", "slam", "punch"], Enumerable.Range(0, 4).Select(n => EidolonStrikes.Standing(n).Animation));
        Assert.Equal([false, false, true, false], Enumerable.Range(0, 4).Select(EidolonStrikes.Heavy));
        Assert.All(Enumerable.Range(0, 3), n => Assert.False(EidolonStrikes.Standing(n).Moving));
    }

    [Fact]
    public void OnTheMoveItJabsTwiceThenHammersAndOnlyHammersALowCreature()
    {
        Assert.Equal(["strike-jab", "strike-jab", "strike-hammer", "strike-jab"],
            Enumerable.Range(0, 4).Select(n => EidolonStrikes.OnTheMove(n, false).Animation));
        Assert.All(Enumerable.Range(0, 3), n => Assert.Equal(EidolonStrikes.Hammer, EidolonStrikes.OnTheMove(n, true)));
        Assert.True(EidolonStrikes.Jab.Moving && EidolonStrikes.Hammer.Moving);
    }

    [Fact]
    public void EveryBlowLandsWithinItsAnimationAndTheMovingOnesSooner()
    {
        Assert.All(All, s => Assert.InRange(s.HitFrame, 1, s.Frames - 1));
        Assert.All(All, s => Assert.InRange(s.Reach, 0.3, 1.5));
        double slowestMoving = Math.Max(EidolonStrikes.Jab.HitAt, EidolonStrikes.Hammer.HitAt);
        Assert.True(slowestMoving < new[] { EidolonStrikes.Punch, EidolonStrikes.Kick, EidolonStrikes.Slam }.Min(s => s.HitAt));
        // Standing off, every blow reaches.
        Assert.All(All, s => Assert.True(EidolonStrikes.StandOff < s.Reach, s.Animation));
    }

    [Fact]
    public void ACreatureStandingInReachGetsAStandingBlow()
    {
        double dx = Radii + 0.5; // half a block between the boxes
        Assert.Equal(EidolonStrikes.Punch, EidolonStrikes.Choose(0, dx, 0, Radii, 0, 0, 0, 0, false));
        Assert.Equal(EidolonStrikes.Kick, EidolonStrikes.Choose(1, dx, 0, Radii, 0.2, 0, 0, 0, false));
    }

    [Fact]
    public void ACreatureOutOfReachIsClosedOnNotStruck()
    {
        Assert.Null(EidolonStrikes.Choose(0, Radii + 3, 0, Radii, 0, 0, 0, 0, false));
        // Walking away at 1.5 blocks a second from just inside reach, with the eidolon standing: by the
        // time even the jab lands it is out of reach.
        Assert.Null(EidolonStrikes.Choose(0, Radii + 1.0, 0, Radii, 1.5, 0, 0, 0, false));
    }

    [Fact]
    public void ACreatureWalkingAwayIsStruckOnTheMoveWhenItIsBeingCaught()
    {
        // 1.4 blocks between the boxes, walking away at 1.5 a second; the eidolon running after at 4.
        var strike = EidolonStrikes.Choose(0, Radii + 1.4, 0, Radii, 1.5, 0, 4, 0, false);
        Assert.Equal(EidolonStrikes.Jab, strike);
        // A creature standing in reach while the eidolon is still moving is still the standing blow.
        Assert.Equal(EidolonStrikes.Punch, EidolonStrikes.Choose(0, Radii + 0.5, 0, Radii, 0, 0, 2, 0, false));
        // A low creature on the move: the hammer, which reaches less.
        Assert.Equal(EidolonStrikes.Hammer, EidolonStrikes.Choose(0, Radii + 0.9, 0, Radii, 1.5, 0, 4, 0, true));
        Assert.Null(EidolonStrikes.Choose(0, Radii + 1.4, 0, Radii, 1.5, 0, 2, 0, true));
    }

    [Fact]
    public void ACreatureComingInIsStruckBeforeItArrives()
    {
        // 2.2 blocks off and running in at 4 a second: within the jab's reach when it lands.
        Assert.Equal(EidolonStrikes.Jab, EidolonStrikes.Choose(0, Radii + 2.2, 0, Radii, -4, 0, 0, 0, false));
    }

    [Fact]
    public void ACreatureCrossingInFrontIsJudgedWhereItWillBe()
    {
        // Half a block in front, crossing at 8 blocks a second: gone sideways when the jab lands.
        Assert.Null(EidolonStrikes.Choose(0, Radii + 0.5, 0, Radii, 0, 8, 0, 0, false));
    }

    [Theory]
    [InlineData(1.0, 0, true, true)]
    [InlineData(1.55, 0, true, true)]   // the jab's reach and the slack
    [InlineData(1.7, 0, true, false)]   // beyond
    [InlineData(1.0, 70, true, true)]   // to the side, within the arc
    [InlineData(1.0, -80, true, false)] // beside it
    [InlineData(1.0, 0, false, false)]  // above or below
    public void AJabLandsOnlyOnACreatureReallyThereWhenItLands(double gap, double off, bool level, bool lands) =>
        Assert.Equal(lands, EidolonStrikes.Lands(EidolonStrikes.Jab, gap, off, level));

    [Fact]
    public void ItLeadsACreatureByTheTimeToGetThereAtMostASecond()
    {
        Assert.Equal(0, EidolonStrikes.LeadSeconds(0, 4));
        Assert.Equal(0.5, EidolonStrikes.LeadSeconds(2, 4), 6);
        Assert.Equal(EidolonStrikes.MaxLeadSeconds, EidolonStrikes.LeadSeconds(20, 4));
        Assert.Equal(EidolonStrikes.MaxLeadSeconds, EidolonStrikes.LeadSeconds(3, 0)); // standing: no faster than a block a second
    }

    [Fact]
    public void GapAfterCarriesTheMotionOn()
    {
        Assert.Equal(2.0, EidolonStrikes.GapAfter(3, 0, 0, 0, 1, 1), 6);
        Assert.Equal(4.0, EidolonStrikes.GapAfter(3, 0, 2, 0, 1, 1), 6);
        Assert.Equal(4.0, EidolonStrikes.GapAfter(3, 0, 0, 4, 1, 1), 6); // 3-4-5
    }

    [Fact]
    public void EveryBlowIsAnAnimationOfTheEntityTypeAndTheShapeOfItsLength()
    {
        string entity = File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "entities", "eidolon.json"));
        using var shape = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "shapes", "entity", "eidolon", "eidolon.json")));
        var frames = shape.RootElement.GetProperty("animations").EnumerateArray()
            .ToDictionary(a => a.GetProperty("code").GetString()!, a => a.GetProperty("quantityframes").GetInt32());
        foreach (var strike in All)
        {
            var m = Regex.Match(entity, $"code: \"{Regex.Escape(strike.Animation)}\", animation: \"([\\w-]+)\"");
            Assert.True(m.Success, strike.Animation);
            Assert.Equal(strike.Frames, frames[m.Groups[1].Value]);
        }
    }

    private static string ModDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
