using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Chunk loading's rules: when an eidolon keeps its chunks loaded, which columns, the counted
/// holds and what is saved for a restart.</summary>
public class EidolonChunksTests
{
    private static readonly Func<ChunkColumn, bool> NoneForced = _ => false;

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]   // the setting off
    [InlineData(true, false, true, false)]   // slumped, dry, docked
    [InlineData(true, true, false, false)]   // following, staying, guarding, done, no axe
    public void ItKeepsItsChunksOnlyAtWorkAndAbleToWork(bool enabled, bool canWork, bool workLeft, bool keeps) =>
        Assert.Equal(keeps, EidolonChunks.Keeps(enabled, canWork, workLeft));

    [Theory]
    [InlineData(0.0, 0.0, 0, 0)]
    [InlineData(31.9, 31.9, 0, 0)]
    [InlineData(32.0, 64.5, 1, 2)]
    [InlineData(-0.1, -32.0, -1, -1)]
    [InlineData(-32.1, 1000.0, -2, 31)]
    public void AColumnIsTheBlockOverTheChunkSizeRoundedDown(double x, double z, int cx, int cz) =>
        Assert.Equal(new ChunkColumn(cx, cz), EidolonChunks.At(x, z));

    [Fact]
    public void RadiusOneIsItsOwnColumnAndTheEightRoundIt()
    {
        var around = EidolonChunks.Around(100, 40, 1).ToList();
        Assert.Equal(9, around.Count);
        Assert.Equal(9, around.Distinct().Count());
        Assert.Contains(new ChunkColumn(3, 1), around);
        Assert.Contains(new ChunkColumn(2, 0), around);
        Assert.Contains(new ChunkColumn(4, 2), around);
        Assert.Equal([new ChunkColumn(3, 1)], EidolonChunks.Around(100, 40, 0));
        Assert.Equal(25, EidolonChunks.Around(100, 40, 2).Count());
    }

    [Fact]
    public void AnAreaCoversEveryColumnItTouches()
    {
        // 32 wide but across a chunk edge: two columns each way.
        var area = new MarkArea(new MarkPos(20, 60, 50), new MarkPos(51, 70, 81));
        Assert.Equal(4, EidolonChunks.CoveredCount(area));
        Assert.Equal(
            [new ChunkColumn(0, 1), new ChunkColumn(0, 2), new ChunkColumn(1, 1), new ChunkColumn(1, 2)],
            EidolonChunks.Covering(area).OrderBy(c => c.X).ThenBy(c => c.Z));
        Assert.Equal(1, EidolonChunks.CoveredCount(EidolonChunks.Block(5, 5, 5)));
        // The mine order's widest: 1024 a side is 32 × 32 columns (33 × 33 off the grid).
        Assert.Equal(33 * 33, EidolonChunks.CoveredCount(new MarkArea(new MarkPos(10, 1, 10), new MarkPos(1033, 100, 1033))));
    }

    [Fact]
    public void ItHoldsItsOwnColumnsAndItsWorkAreasButNotATooLargeOne()
    {
        var area = new MarkArea(new MarkPos(320, 60, 320), new MarkPos(351, 70, 351));
        var machine = EidolonChunks.Block(500, 64, 0);
        var wanted = EidolonChunks.Wanted(0, 0, 1, [area, machine], 16);
        Assert.Equal(9 + 1 + 1, wanted.Count);
        Assert.Contains(new ChunkColumn(10, 10), wanted);
        Assert.Contains(new ChunkColumn(15, 0), wanted);
        Assert.Contains(new ChunkColumn(-1, -1), wanted);

        var quarry = new MarkArea(new MarkPos(0, 1, 0), new MarkPos(1023, 100, 1023));
        Assert.Equal(9, EidolonChunks.Wanted(0, 0, 1, [quarry], 16).Count);
        // Overlapping its own: counted once.
        Assert.Equal(9, EidolonChunks.Wanted(0, 0, 1, [EidolonChunks.Block(1, 1, 1)], 16).Count);
    }

    [Fact]
    public void HoldsAreCountedSoASharedColumnIsLetGoByTheLast()
    {
        var holds = new ChunkHolds();
        var a = new ChunkColumn(0, 0);
        var b = new ChunkColumn(1, 0);
        var c = new ChunkColumn(2, 0);
        var (force, release) = holds.Set(1, [a, b], NoneForced);
        Assert.Equal([a, b], force.OrderBy(x => x.X));
        Assert.Empty(release);

        (force, release) = holds.Set(2, [b, c], NoneForced);
        Assert.Equal([c], force);
        Assert.Empty(release);

        // The first moves on: a is let go, b stays for the second.
        (force, release) = holds.Set(1, [b], NoneForced);
        Assert.Empty(force);
        Assert.Equal([a], release);

        Assert.Empty(holds.Release(1));
        Assert.Equal([b, c], holds.Release(2).OrderBy(x => x.X));
        Assert.Empty(holds.Holders);
        Assert.Empty(holds.All);
    }

    [Fact]
    public void AColumnForcedForSomethingElseIsNeverForcedOrLetGo()
    {
        var holds = new ChunkHolds();
        var spawn = new ChunkColumn(10, 10);
        var mine = new ChunkColumn(11, 10);
        var (force, _) = holds.Set(1, [spawn, mine], c => c == spawn);
        Assert.Equal([mine], force);
        Assert.True(holds.Holds(spawn));
        Assert.Equal([mine], holds.Release(1));

        // Forced here first, it is ours to let go even if the server's list has it later.
        (force, _) = holds.Set(1, [mine], NoneForced);
        Assert.Equal([mine], force);
        (force, _) = holds.Set(2, [mine], _ => true);
        Assert.Empty(force);
        Assert.Empty(holds.Release(1));
        Assert.Equal([mine], holds.Release(2));
    }

    [Fact]
    public void SettingTheSameColumnsAgainForcesAndLetsGoNothing()
    {
        var holds = new ChunkHolds();
        holds.Set(7, EidolonChunks.Around(0, 0, 1), NoneForced);
        var (force, release) = holds.Set(7, EidolonChunks.Around(5, 5, 1), NoneForced);
        Assert.Empty(force);
        Assert.Empty(release);
        // A chunk east: a row of three comes, a row of three goes.
        (force, release) = holds.Set(7, EidolonChunks.Around(40, 5, 1), NoneForced);
        Assert.Equal(3, force.Count);
        Assert.All(force, c => Assert.Equal(2, c.X));
        Assert.Equal(3, release.Count);
        Assert.All(release, c => Assert.Equal(-1, c.X));
    }

    [Fact]
    public void TheHoldsRoundTripThroughTheSavegame()
    {
        var holds = new ChunkHolds();
        holds.Set(42, EidolonChunks.Around(0, 0, 1), NoneForced);
        holds.Set(-5, [new ChunkColumn(100, 200)], NoneForced);
        var saved = ChunkHolds.FromBytes(holds.ToBytes());
        Assert.Equal(2, saved.Count);
        Assert.Equal(9, saved.Single(h => h.EntityId == 42).Columns.Count);
        Assert.Equal([new ChunkColumn(100, 200)], saved.Single(h => h.EntityId == -5).Columns);
        Assert.Empty(ChunkHolds.FromBytes(null));
        Assert.Empty(ChunkHolds.FromBytes([]));
        Assert.Empty(ChunkHolds.FromBytes(new ChunkHolds().ToBytes()));
    }

    [Fact]
    public void TheDefaultsHoldItsOwnColumnAndTheEightRoundItAndAFellArea()
    {
        var config = new EidolonConfig();
        Assert.True(config.KeepWorkChunksLoaded);
        Assert.Equal(1, config.ChunkLoadRadius);
        // The fell and crew area (32 a side) covers at most 4 columns.
        Assert.True(EidolonChunks.CoveredCount(new MarkArea(new MarkPos(31, 0, 31), new MarkPos(62, 0, 62))) <= config.MaxWorkAreaChunks);
        Assert.Empty(config.Sanitise());
        config.ChunkLoadRadius = EidolonChunks.MaxRadius + 1;
        config.MaxWorkAreaChunks = -1;
        Assert.Equal(2, config.Sanitise().Count);
        Assert.Equal(1, config.ChunkLoadRadius);
        Assert.Equal(16, config.MaxWorkAreaChunks);
    }
}
