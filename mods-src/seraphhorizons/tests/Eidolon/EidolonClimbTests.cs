using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The scripted climb up a step of two (<see cref="EidolonClimb"/>): which steps are climbed,
/// and where it is as the climb goes.</summary>
public class EidolonClimbTests
{
    private static readonly PathCell Foot = new(4, 0, 0), Top = new(5, 2, 0);

    private static double At(int frame) => frame / EidolonClimb.FramesPerSecond;

    [Fact]
    public void OnlyAStepOfTwoOrMoreUpIsClimbed()
    {
        Assert.True(EidolonClimb.IsClimb(Foot, Top));
        Assert.False(EidolonClimb.IsClimb(Foot, new PathCell(5, 1, 0)));
        Assert.False(EidolonClimb.IsClimb(Foot, new PathCell(5, 0, 0)));
        Assert.False(EidolonClimb.IsClimb(Top, Foot));
    }

    [Fact]
    public void TheNextClimbOnAPath()
    {
        PathCell[] path = [new(2, 0, 0), new(3, 0, 0), Foot, Top, new(6, 2, 0), new(7, 3, 0), new(8, 5, 0)];
        Assert.Equal(2, EidolonClimb.NextClimb(path, 0));
        Assert.Equal(2, EidolonClimb.NextClimb(path, 2));
        Assert.Equal(5, EidolonClimb.NextClimb(path, 3));
        Assert.Equal(-1, EidolonClimb.NextClimb(path, 6));
        Assert.Equal(-1, EidolonClimb.NextClimb([Foot], 0));
    }

    [Fact]
    public void ToTheFootThenUpThenForward()
    {
        // begun a little short of the foot
        (double X, double Y, double Z) start = (3.8, 0, 0.1);
        (double, double, double) Where(int frame) => EidolonClimb.At(At(frame), start.X, start.Y, start.Z, Foot, Top);

        Assert.Equal(start, Where(0));
        // at the foot when its hands reach the ledge, and still there, rising, until it is up
        Assert.Equal((4.0, 0.0, 0.0), Where(EidolonClimb.GrabFrame));
        var half = Where((EidolonClimb.GrabFrame + EidolonClimb.TopFrame) / 2);
        Assert.Equal((4.0, 0.0), (half.Item1, half.Item3));
        Assert.Equal(1.0, half.Item2, 3);
        Assert.Equal((4.0, 2.0, 0.0), Where(EidolonClimb.TopFrame));
        // then forward at the top's height, onto the top, and there to the end
        var on = Where((EidolonClimb.TopFrame + EidolonClimb.StandFrame) / 2);
        Assert.Equal(4.5, on.Item1, 3);
        Assert.Equal(2.0, on.Item2);
        Assert.Equal((5.0, 2.0, 0.0), Where(EidolonClimb.StandFrame));
        Assert.Equal((5.0, 2.0, 0.0), Where(EidolonClimb.Frames));
        Assert.Equal((5.0, 2.0, 0.0), EidolonClimb.At(60, start.X, start.Y, start.Z, Foot, Top));
    }

    [Fact]
    public void NeverForwardBeforeItIsUp()
    {
        // up then forward, never both: the box is only ever over the foot or at the top's height
        for (int f = 0; f <= EidolonClimb.Frames; f++)
        {
            var (x, y, _) = EidolonClimb.At(At(f), Foot.X, Foot.Y, Foot.Z, Foot, Top);
            Assert.True(x == Foot.X || y == Top.Y, $"frame {f}: ({x}, {y})");
        }
    }

    [Fact]
    public void ItFacesTheStep()
    {
        Assert.Equal(MathF.PI / 2, EidolonClimb.Yaw(Foot, Top), 4);
        Assert.Equal(0f, EidolonClimb.Yaw(Foot, new PathCell(4, 2, 1)), 4);
        Assert.Equal(MathF.PI / 4, EidolonClimb.Yaw(Foot, new PathCell(5, 2, 1)), 4);
    }

    [Fact]
    public void TheFramesAreInOrderWithinTheAnimation()
    {
        Assert.True(0 < EidolonClimb.GrabFrame);
        Assert.True(EidolonClimb.GrabFrame < EidolonClimb.TopFrame);
        Assert.True(EidolonClimb.TopFrame < EidolonClimb.StandFrame);
        Assert.True(EidolonClimb.StandFrame < EidolonClimb.Frames);
        Assert.Equal(2.0, EidolonClimb.Seconds);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void TheClimbSettingIsOneOrTwo(int set, int want)
    {
        var c = new EidolonConfig { MaxClimbBlocks = set };
        var fixes = c.Sanitise();
        Assert.Equal(want, c.MaxClimbBlocks);
        Assert.Equal(set == want ? 0 : 1, fixes.Count);
    }
}
