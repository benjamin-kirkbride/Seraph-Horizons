using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary><c>WideDensityProspecting</c>'s figures and its one-walk-per-player gate.</summary>
public class DensityProspectingTests
{
    [Fact]
    public void Zone_area_is_the_folded_constant_BetterEr_ships_and_four_times_it_widened()
    {
        Assert.Equal(4096, DensityProspecting.ZoneArea(DensityProspecting.ShippedRadius));
        Assert.Equal(16384, DensityProspecting.ZoneArea(DensityProspecting.WideRadius));
        Assert.Equal(4 * DensityProspecting.ZoneArea(32), DensityProspecting.ZoneArea(64));
    }

    [Fact]
    public void Chunk_offsets_are_BetterErs_at_its_radius_and_reach_two_chunks_at_64()
    {
        Assert.Equal([-32, 0, 32], DensityProspecting.ChunkOffsets(32));
        Assert.Equal([-64, -32, 0, 32, 64], DensityProspecting.ChunkOffsets(64));
        Assert.Equal([0], DensityProspecting.ChunkOffsets(0));
        Assert.Equal([-40, -32, 0, 32, 40], DensityProspecting.ChunkOffsets(40));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(64)]
    public void Reprospect_loads_every_chunk_the_walk_reads(int radius)
    {
        var offsets = DensityProspecting.ChunkOffsets(radius);
        foreach (int x in new[] { 0, 1, 31, 32, 33, 63, 64, 65, 500, 511, 512, 513, 1_024_000, 1_024_031 })
            foreach (int z in new[] { 0, 17, 32, 95, 512_000, 512_031 })
            {
                var loaded = DensityProspecting.ChunksLoaded(x, z, offsets).ToHashSet();
                foreach (var chunk in DensityProspecting.ChunksWalked(x, z, radius))
                    Assert.True(loaded.Contains(chunk), $"r{radius} at ({x}, {z}): chunk {chunk} walked, not loaded");
            }
    }

    [Fact]
    public void BetterErs_own_offsets_miss_chunks_of_a_wide_walk()
    {
        var loaded = DensityProspecting.ChunksLoaded(1000, 1000, DensityProspecting.ChunkOffsets(32)).ToHashSet();
        Assert.Contains(DensityProspecting.ChunksWalked(1000, 1000, 64), c => !loaded.Contains(c));
    }

    [Fact]
    public void Gate_lets_one_walk_per_player()
    {
        var gate = new ProbeGate();
        Assert.True(gate.TryBegin("a"));
        Assert.False(gate.TryBegin("a"));
        Assert.True(gate.TryBegin("b"));
        Assert.True(gate.Busy("a"));
        Assert.Equal(2, gate.Count);
        gate.End("a");
        Assert.False(gate.Busy("a"));
        Assert.True(gate.TryBegin("a"));
        gate.End("nobody");
        gate.Clear();
        Assert.Equal(0, gate.Count);
        Assert.True(gate.TryBegin("b"));
    }

    [Fact]
    public void Gate_holds_under_contention()
    {
        var gate = new ProbeGate();
        int won = 0;
        Parallel.For(0, 64, _ =>
        {
            if (gate.TryBegin("racer"))
                Interlocked.Increment(ref won);
        });
        Assert.Equal(1, won);
    }
}
