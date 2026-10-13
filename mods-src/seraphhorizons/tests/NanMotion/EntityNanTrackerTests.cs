using SeraphHorizons.Mod.NanMotion.Core;

namespace SeraphHorizons.Mod.Tests.NanMotion;

public class EntityNanTrackerTests
{
    private static string Name(object site, object? instance) => instance == null ? $"{site}" : $"{site}/{instance}";

    [Fact]
    public void FiniteEntitiesNeverTrip()
    {
        var t = new EntityNanTracker();
        var p = t.Before("tick", "cow", "cow", 7, 100, 1, 2, 3, 0, 0, 0, true, out bool tripped);
        Assert.False(tripped);
        Assert.True(p.Watched);
        Assert.False(t.After("tick", "cow", "cow", 7, 100, in p, 1, 2, 3, 0.1, 0, 0, true));
        Assert.False(t.Sweep("sweep", "cow", 7, 500, 1, 2, 3, 0, 0, 0));
        t.SweepDone(500);
        Assert.False(t.Tripped);
        Assert.Equal(2, t.Checkpoints);
        Assert.Equal(1, t.CleanSweeps);
        Assert.Equal(500, t.LastCleanSweepMs);
    }

    [Fact]
    public void ACallThatMakesAPositionNaNTripsInsideIt()
    {
        var t = new EntityNanTracker();
        var p = t.Before("interpolate", "frame", "pig", 42, 1000, 10, 64, 10, 0, 0, 0, true, out _);
        Assert.True(t.After("interpolate", "frame", "pig", 42, 1016, in p, double.NaN, double.NaN, double.NaN, 0, 0, 0, true));
        var trip = t.Trip!;
        Assert.Equal(EntityTripKind.Inside, trip.Kind);
        Assert.Equal(42, trip.EntityId);
        Assert.Equal("pig", trip.Entity);
        Assert.Equal(1016, trip.AtMs);
        Assert.True(trip.Before.Finite);
        Assert.False(trip.After.PositionFinite);
        Assert.True(trip.After.MotionFinite);
        string text = string.Join("\n", t.Describe(Name));
        Assert.Contains("INSIDE interpolate/frame", text);
        Assert.Contains("at its start: pos (10.000000, 64.000000, 10.000000)", text);
        Assert.Contains("at its end:   pos (NaN, NaN, NaN)" + NanFormat.Flag, text);
        Assert.Contains("entity #42", text);
    }

    [Fact]
    public void AMotionThatGoesNaNTripsToo()
    {
        var t = new EntityNanTracker();
        var p = t.Before("remote physics", null, "hen", 1, 0, 0, 0, 0, 0, 0, 0, true, out _);
        Assert.True(t.After("remote physics", null, "hen", 1, 0, in p, 0, 0, 0, 0, double.PositiveInfinity, 0, true));
        Assert.False(t.Trip!.After.MotionFinite);
    }

    [Fact]
    public void ACallWhoseOwnStateGoesNonFiniteTripsBeforeThePositionDoes()
    {
        var t = new EntityNanTracker();
        var p = t.Before("interpolate", null, "cart", 3, 0, 5, 5, 5, 0, 0, 0, true, out _);
        Assert.True(t.After("interpolate", null, "cart", 3, 0, in p, 5, 5, 5, 0, 0, 0, stateFinite: false));
        var trip = t.Trip!;
        Assert.True(trip.After.PositionFinite);
        Assert.False(trip.After.Finite);
        Assert.Contains("its traced state non-finite", string.Join("\n", t.Describe(Name)));
    }

    [Fact]
    public void AnEntityThatArrivesNonFiniteIsBlamedOnUntracedCodeSinceTheLastCleanSweep()
    {
        var t = new EntityNanTracker();
        t.SweepDone(2000);
        t.Before("tick", "eidolon", "eidolon", 9, 2300, double.NaN, 1, 1, 0, 0, 0, true, out bool tripped);
        Assert.True(tripped);
        var trip = t.Trip!;
        Assert.Equal(EntityTripKind.Arrived, trip.Kind);
        Assert.Equal(2000, trip.LastCleanSweepMs);
        Assert.False(trip.Before.Watched);
        string text = string.Join("\n", t.Describe(Name));
        Assert.Contains("Already non-finite at the start of tick/eidolon", text);
        Assert.Contains("ran at 2000 ms, 300 ms before", text);
    }

    [Fact]
    public void TheSweepFindsWhatNoTracedCallSaw()
    {
        var t = new EntityNanTracker();
        Assert.False(t.Sweep("sweep", "a", 1, 500, 0, 0, 0, 0, 0, 0));
        t.SweepDone(500);
        Assert.True(t.Sweep("sweep", "b", 2, 1000, 0, 0, 0, double.NaN, 0, 0));
        t.SweepDone(1000);
        var trip = t.Trip!;
        Assert.Equal(EntityTripKind.Sweep, trip.Kind);
        Assert.Equal(2, trip.EntityId);
        Assert.Equal(500, trip.LastCleanSweepMs);
        // A sweep that tripped is not a clean one.
        Assert.Equal(500, t.LastCleanSweepMs);
        Assert.Equal(1, t.CleanSweeps);
        string text = string.Join("\n", t.Describe(Name));
        Assert.Contains("Found by the sweep of loaded entities, sweep/b", text);
        Assert.Contains("500 ms before", text);
    }

    [Fact]
    public void AfterTheFirstTripNothingIsLookedAt()
    {
        var t = new EntityNanTracker();
        Assert.True(t.Sweep("sweep", "a", 1, 0, double.NaN, 0, 0, 0, 0, 0));
        var first = t.Trip;
        long checkpoints = t.Checkpoints;
        var p = t.Before("tick", null, "b", 2, 0, double.NaN, 0, 0, 0, 0, 0, true, out bool again);
        Assert.False(again);
        Assert.False(p.Watched);
        Assert.False(t.After("tick", null, "b", 2, 0, in p, double.NaN, 0, 0, 0, 0, 0, true));
        Assert.False(t.Sweep("sweep", "c", 3, 0, double.NaN, 0, 0, 0, 0, 0));
        Assert.Same(first, t.Trip);
        Assert.Equal(checkpoints, t.Checkpoints);
    }

    [Fact]
    public void AnUnwatchedStartIsIgnoredAtTheEnd()
    {
        var t = new EntityNanTracker();
        Assert.False(t.After("tick", null, "a", 1, 0, default, double.NaN, 0, 0, 0, 0, 0, true));
        Assert.False(t.Tripped);
        Assert.Equal(0, t.Checkpoints);
    }

    [Fact]
    public void WithoutATripTheDescriptionSaysSo()
    {
        var t = new EntityNanTracker();
        t.SweepDone(1500);
        string text = string.Join("\n", t.Describe(Name));
        Assert.Contains("No other entity was seen with a non-finite position or motion", text);
        Assert.Contains("1 clean sweeps, the last at 1500 ms", text);
    }
}

public class FrameRingTests
{
    [Fact]
    public void KeepsTheLastFramesOldestFirst()
    {
        var ring = new FrameRing(3);
        for (int i = 1; i <= 5; i++)
            ring.Add(new ClientFrame(i * 16, 0.016f, false));
        Assert.Equal(3, ring.Count);
        Assert.Equal([48L, 64L, 80L], ring.Frames().Select(f => f.ElapsedMs));
    }

    [Fact]
    public void FlagsOddDtsAndPauses()
    {
        var ring = new FrameRing(5);
        ring.Add(new ClientFrame(0, 0.016f, false));
        ring.Add(new ClientFrame(16, 0f, true));
        ring.Add(new ClientFrame(1500, 1.484f, false));
        ring.Add(new ClientFrame(1516, float.NaN, false));
        var lines = ring.Describe().ToList();
        Assert.Equal(4, lines.Count);
        Assert.DoesNotContain("ODD", lines[0]);
        Assert.Contains("t  -1516ms", lines[0]);
        Assert.Contains("paused", lines[1]);
        Assert.Contains("ODD DT", lines[1]);
        Assert.Contains("ODD DT", lines[2]);
        Assert.Contains("dt NaN", lines[3]);
        Assert.Contains("ODD DT", lines[3]);
    }

    [Fact]
    public void AnEmptyRingSaysSo() => Assert.Equal(["(no client frame recorded)"], new FrameRing(2).Describe());

    [Fact]
    public void ACapacityBelowOneIsRefused() => Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRing(0));
}
