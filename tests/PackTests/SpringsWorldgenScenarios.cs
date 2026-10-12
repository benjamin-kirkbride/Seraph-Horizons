using Atlas.XUnit;
using SeraphHorizons.Mod.Springs;
using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, Springs (Springs/): in a new standard world (the shared world is flat
/// and has no rivulets), the game's rivulet generator places springs where it placed water and
/// rapids sources: each with its block entity, no water left in its cell, drawn as a rock round it,
/// and pouring once its chunk is done.
/// </summary>
[AtlasWorld(Seed = Seed, WorldType = "standard")]
public class SpringsWorldgenScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Seed = 515151;

    private IWorldAccessor W => World.Api.World;

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task New_chunks_have_springs_where_the_generator_put_water_rivulets()
    {
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        var origin = World.Spawn;
        var columns = new List<(int X, int Z)>();
        for (int cx = -2; cx <= 2; cx++)
            for (int cz = -2; cz <= 2; cz++)
                columns.Add((origin.X / size + cx, origin.Z / size + cz));
        foreach (var (x, z) in columns)
            sapi.WorldManager.LoadChunkColumnPriority(x, z);
        await World.Until(() => columns.All(c => sapi.WorldManager.GetMapChunk(c.X, c.Z)?.CurrentPass == EnumWorldGenPass.Done
                                                 && W.BlockAccessor.GetChunk(c.X, 0, c.Z) != null), 240_000);
        await World.Ticks(40);
        output.WriteLine($"springs generated: {SpringsSystem.Generated} of {SpringsSystem.Rivulets} rivulets (lava ones too)");
        Assert.Equal(2, SpringsSystem.Hooked);
        Assert.True(SpringsSystem.Generated > 0, "the generator placed no spring");

        // Find them in the columns round spawn.
        var springIds = W.Blocks.OfType<BlockWaterSpring>().Select(b => b.Id).ToHashSet();
        var found = new List<BlockPos>();
        foreach (var (x, z) in columns)
            for (int cy = 0; cy < W.BlockAccessor.MapSizeY / size; cy++)
            {
                var chunk = W.BlockAccessor.GetChunk(x, cy, z);
                if (chunk == null)
                    continue;
                chunk.Unpack();
                for (int i = 0; i < size * size * size; i++)
                    if (springIds.Contains(chunk.Data.GetBlockIdUnsafe(i)))
                        found.Add(new BlockPos(x * size + i % size, cy * size + i / (size * size), z * size + i / size % size));
            }
        output.WriteLine($"{found.Count} springs in the {columns.Count} columns round spawn: "
                         + string.Join(", ", found.Take(12).Select(p => $"{W.BlockAccessor.GetBlock(p).Code.Path} at {p}")));
        Assert.NotEmpty(found);
        int pouring = 0;
        foreach (var pos in found)
        {
            var block = (BlockWaterSpring)W.BlockAccessor.GetBlock(pos);
            Assert.NotNull(W.BlockAccessor.GetBlockEntity(pos) as BlockEntityWaterSpring);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid).Id);
            var rocks = BlockFacing.ALLFACES.Select(f => W.BlockAccessor.GetBlock(pos.AddCopy(f)))
                .Select(b => b.Code == null ? null : HostRock.RockOf(b.Code.Domain, b.Code.Path, b.Variant?["rock"])).ToList();
            Assert.True(rocks.Contains(block.Rock) || block.Rock == HostRock.Fallback,
                $"{block.Code} at {pos}: {block.Rock} is not a rock round it ({string.Join(", ", rocks)})");
            if (BlockFacing.ALLFACES.Any(f => block.Pours(W.BlockAccessor.GetBlock(pos.AddCopy(f), BlockLayersAccess.Fluid))))
                pouring++;
        }
        // Each pours once its chunk and its neighbours are done (the generator's scheduled update).
        output.WriteLine($"{pouring} of {found.Count} pouring");
        Assert.True(pouring > 0, "no generated spring pours");
    }
}
