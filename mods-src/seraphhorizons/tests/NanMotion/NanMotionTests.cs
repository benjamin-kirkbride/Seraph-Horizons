using SeraphHorizons.Mod.NanMotion.Core;

namespace SeraphHorizons.Mod.Tests.NanMotion;

public class NanTrackerTests
{
    private static string Name(object site, object? instance) => instance == null ? $"{site}" : $"{site}/{instance}";

    [Fact]
    public void FiniteMotionNeverTrips()
    {
        var t = new NanTracker();
        var p = t.Before("tick", null, 0.1, -0.2, 0, out bool tripped);
        Assert.False(tripped);
        Assert.False(t.After("tick", null, in p, 0.3, 0, 1e6));
        Assert.False(t.Tripped);
        Assert.Equal(2, t.Checkpoints);
        Assert.Equal("tick", t.LastFinite.Site);
        Assert.Equal(CheckPhase.After, t.LastFinite.Phase);
    }

    [Fact]
    public void ACallThatMakesTheNaNTripsInsideIt()
    {
        var t = new NanTracker();
        var p = t.Before("repulse", "cart", 0.01, 0, 0.02, out _);
        Assert.True(t.After("repulse", "cart", in p, double.NaN, 0, 0.02));
        var trip = t.Trip!;
        Assert.Equal(TripKind.Inside, trip.Kind);
        Assert.Equal("repulse", trip.Site);
        Assert.Equal("cart", trip.Instance);
        Assert.Equal((0.01, 0.0, 0.02), trip.Before);
        Assert.True(double.IsNaN(trip.After.X));
        Assert.Contains("INSIDE repulse/cart", string.Join("\n", t.Describe(Name)));
    }

    [Fact]
    public void ANaNThatArrivesBetweenCheckpointsNamesBothEnds()
    {
        var t = new NanTracker();
        var p = t.Before("a", null, 1, 2, 3, out _);
        t.After("a", null, in p, 1, 2, 3);
        t.Before("b", "x", double.PositiveInfinity, 2, 3, out bool tripped);
        Assert.True(tripped);
        var trip = t.Trip!;
        Assert.Equal(TripKind.Between, trip.Kind);
        Assert.Equal("a", trip.LastFiniteSite);
        Assert.Equal(CheckPhase.After, trip.LastFinitePhase);
        Assert.Equal("b", trip.Site);
        Assert.Equal((1.0, 2.0, 3.0), trip.Before);
        string text = string.Join("\n", t.Describe(Name));
        Assert.Contains("BETWEEN", text);
        Assert.Contains("at the end of a", text);
        Assert.Contains("at the start of b/x", text);
        Assert.Contains("+Infinity", text);
    }

    [Fact]
    public void TheInnermostCallIsBlamedAndTheOuterOnesAreNot()
    {
        var t = new NanTracker();
        var outer = t.Before("entity tick", null, 0, 0, 0, out _);
        var inner = t.Before("behavior tick", null, 0, 0, 0, out _);
        Assert.True(t.After("behavior tick", null, in inner, 0, double.NaN, 0));
        Assert.False(t.After("entity tick", null, in outer, 0, double.NaN, 0));
        Assert.Equal("behavior tick", t.Trip!.Site);
    }

    [Fact]
    public void AfterTheFirstTripNothingIsLookedAt()
    {
        var t = new NanTracker();
        t.Check("start", null, CheckPhase.Before, double.NaN, 0, 0, out bool first);
        Assert.True(first);
        long checkpoints = t.Checkpoints;
        var p = t.Before("later", null, 0, 0, 0, out bool again);
        Assert.False(again);
        Assert.False(p.Watched);
        Assert.False(t.After("later", null, in p, double.NaN, 0, 0));
        Assert.Equal(checkpoints, t.Checkpoints);
        Assert.Equal("start", t.Trip!.Site);
        // Nothing seen finite before it.
        Assert.Contains("never", string.Join("\n", t.Describe(Name)));
    }

    [Fact]
    public void AnUnwatchedStartIsIgnoredAtTheEnd()
    {
        var t = new NanTracker();
        Assert.False(t.After("x", null, default, double.NaN, 0, 0));
        Assert.False(t.Tripped);
        Assert.Equal(0, t.Checkpoints);
    }

    [Fact]
    public void WithoutATripTheDescriptionSaysSoAndNamesTheLastFiniteCheckpoint()
    {
        var t = new NanTracker();
        t.Check("SimPhysics body", null, CheckPhase.Before, 0.5, 0, 0, out _);
        string text = string.Join("\n", t.Describe(Name));
        Assert.Contains("No traced checkpoint saw the motion non-finite", text);
        Assert.Contains("at the start of SimPhysics body", text);
    }
}

public class MotionRingTests
{
    private static MotionFrame Frame(long ms, double mx = 0) => new(ms, mx, 0, 0, 1, 2, 3, 0.5f, true, false, 1f);

    [Fact]
    public void KeepsTheLastFramesOldestFirst()
    {
        var ring = new MotionRing(3);
        Assert.Empty(ring.Frames());
        ring.Add(Frame(10));
        ring.Add(Frame(20));
        Assert.Equal([10L, 20L], ring.Frames().Select(f => f.ElapsedMs));
        ring.Add(Frame(30));
        ring.Add(Frame(40));
        ring.Add(Frame(50));
        Assert.Equal(3, ring.Count);
        Assert.Equal([30L, 40L, 50L], ring.Frames().Select(f => f.ElapsedMs));
    }

    [Fact]
    public void DescribesFramesRelativeToTheLastAndFlagsNaN()
    {
        var ring = new MotionRing(2);
        ring.Add(Frame(100));
        ring.Add(Frame(117, double.NaN));
        var lines = ring.Describe().ToList();
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("t   -17ms", lines[0]);
        Assert.DoesNotContain(NanFormat.Flag, lines[0]);
        Assert.Contains("NaN", lines[1]);
        Assert.Contains(NanFormat.Flag, lines[1]);
    }

    [Fact]
    public void ACapacityBelowOneIsRefused() => Assert.Throws<ArgumentOutOfRangeException>(() => new MotionRing(0));
}

public class NanFormatTests
{
    [Fact]
    public void FormatsInvariantlyAndFlagsNonFinite()
    {
        Assert.Equal("1.500000", NanFormat.Num(1.5));
        Assert.Equal("NaN", NanFormat.Num(double.NaN));
        Assert.Equal("-Infinity", NanFormat.Num(float.NegativeInfinity));
        Assert.Equal("(0.000000, 1.000000, -2.000000)", NanFormat.Vec(0, 1, -2));
        Assert.Equal("(NaN, 1.000000, 0.000000)" + NanFormat.Flag, NanFormat.Vec(double.NaN, 1, 0));
        Assert.Equal("3.000000", NanFormat.Value(3));
        Assert.Equal("NaN" + NanFormat.Flag, NanFormat.Value(float.NaN));
    }

    [Fact]
    public void AFailingSectionIsReportedAndTheOthersAreWritten()
    {
        var r = new ReportWriter();
        r.Line("header");
        r.Section("first", sb => sb.Append("  fine\n"));
        r.Section("second", sb =>
        {
            sb.Append("  half a line");
            throw new InvalidOperationException("boom");
        });
        r.Section("third", sb => sb.Append("  also fine"));
        string text = r.ToString();
        Assert.Equal(1, r.Failures);
        Assert.Contains("== first ==\n  fine\n", text);
        Assert.Contains("  half a line\n  (this section failed: InvalidOperationException: boom)\n", text);
        Assert.Contains("== third ==\n  also fine\n", text);
    }
}
