using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod;

/// <summary>
/// The foundation under a surface ruin <see cref="RuinSurfaceMedian"/> seated on the median of its
/// ground (<c>RuinsOnMedianGround</c>): on a slope the ruin's low side stands off the ground, and
/// once the game has placed it, the air under it is filled with the column's own ground
/// (<see cref="RuinFoundationColumn"/>). A BetterRuins waystone (<c>ogdred-waystones</c>, 6 by 7) with
/// its floor at 140, seated on ground falling from 141 to 137, had its south half on 1 to 3 blocks of
/// air.
///
/// Per column of the footprint: the lowest block of the placed schematic there with a collision box
/// (its <c>blocksByPos</c>, meta blocks aside) is what the ruin stands on; down from just under it
/// (no higher than the seat), air and plants are filled until the ground, as the trader camps'
/// levelling fills (#599): with the block under the column's top, the top block put back on the new
/// top, and the chunk's <c>WorldGenTerrainHeightMap</c> and <c>RainHeightMap</c> raised so later
/// passes see the new ground. A column with a liquid in the gap, with no ground within
/// <see cref="RuinFoundationColumn.MaxDepth"/>, whose gap crosses another generated structure, or
/// outside the placing chunk and its neighbours (all the worldgen block accessor reaches; see
/// docs/trading.md, "Camp placement (#599)") is left as it is.
/// </summary>
public static class RuinFoundations
{
    /// <summary>Lays the foundation under the schematic just placed at <paramref name="location"/>
    /// (<c>WorldGenStructure.LastPlacedSchematicLocation</c>); returns the blocks filled.</summary>
    public static int Lay(IBlockAccessor blocks, Cuboidi location, BlockSchematicStructure schematic)
    {
        var byPos = schematic.blocksByPos;
        if (byPos == null) return 0;
        int size = GlobalConstants.ChunkSize;
        int seat = location.Y1 - schematic.OffsetY;
        int chunkX = location.X1 / size, chunkZ = location.Z1 / size;
        int sizeX = Math.Min(schematic.SizeX, byPos.GetLength(0)), sizeY = Math.Min(schematic.SizeY, byPos.GetLength(1));
        int sizeZ = Math.Min(schematic.SizeZ, byPos.GetLength(2));
        var others = OtherStructures(blocks, location);
        var pos = new BlockPos(0);
        int filled = 0;

        for (int i = 0; i < sizeX; i++)
            for (int j = 0; j < sizeZ; j++)
            {
                int x = location.X1 + i, z = location.Z1 + j;
                if (Math.Abs(x / size - chunkX) > 1 || Math.Abs(z / size - chunkZ) > 1) continue;
                if (blocks.GetMapChunk(x / size, z / size) is not { } chunk) continue;
                int li = i, lj = j;
                var lowest = RuinFoundationColumn.LowestSolid(y => StandsOn(byPos[li, y, lj]), sizeY);
                if (RuinFoundationColumn.Top(lowest + location.Y1, seat) is not { } top) continue;
                if (RuinFoundationColumn.Span(top, y => Cell(blocks, pos.Set(x, y, z))) is not var (from, to)) continue;
                var gap = new Cuboidi(x, from, z, x + 1, to + 1, z + 1);
                if (others.Any(o => o.Intersects(gap))) continue;

                var ground = blocks.GetBlock(pos.Set(x, from - 1, z), BlockLayersAccess.Solid);
                var under = blocks.GetBlock(pos.Set(x, from - 2, z), BlockLayersAccess.Solid);
                int index = z % size * size + x % size;
                var fill = IsGround(under) ? under : IsGround(ground) ? ground : blocks.GetBlock(chunk.TopRockIdMap[index]);
                if (fill == null || fill.Id == 0) continue;
                var cover = IsGround(ground) ? ground : fill;
                // The old top is buried: it becomes fill, and the cover goes on the new top.
                if (IsGround(ground) && ground.Id != fill.Id)
                    blocks.SetBlock(fill.Id, pos.Set(x, from - 1, z), BlockLayersAccess.Solid);
                for (int y = from; y <= to; y++)
                    blocks.SetBlock(y == to ? cover.Id : fill.Id, pos.Set(x, y, z), BlockLayersAccess.Solid);
                filled += to - from + 1;
                chunk.WorldGenTerrainHeightMap[index] = (ushort)Math.Max(chunk.WorldGenTerrainHeightMap[index], to);
                chunk.RainHeightMap[index] = (ushort)Math.Max(chunk.RainHeightMap[index], to);
            }
        return filled;
    }

    /// <summary>Whether the ruin stands on a schematic block: one with a collision box, not air, a
    /// fluid or a meta block.</summary>
    private static bool StandsOn(Block? block) =>
        block != null && block.Id != 0 && !block.ForFluidsLayer
        && block.Id != BlockSchematic.UndergroundBlockId && block.Id != BlockSchematic.AbovegroundBlockId
        && block.Id != BlockSchematic.FillerBlockId && block.Id != BlockSchematic.PathwayBlockId
        && block.CollisionBoxes is { Length: > 0 };

    private static FoundationCell Cell(IBlockAccessor blocks, BlockPos pos)
    {
        if (blocks.GetBlock(pos, BlockLayersAccess.Fluid) is { } fluid && fluid.IsLiquid()) return FoundationCell.Liquid;
        var solid = blocks.GetBlock(pos, BlockLayersAccess.Solid);
        return solid == null || solid.Id == 0 || (solid.CollisionBoxes is not { Length: > 0 } && solid.EntityClass == null)
            ? FoundationCell.Open
            : FoundationCell.Ground;
    }

    private static bool IsGround(Block? block) =>
        block?.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Soil or EnumBlockMaterial.Sand or EnumBlockMaterial.Gravel;

    /// <summary>The structures already generated in the map regions under the footprint (the ruin
    /// itself is recorded only after the placement returns).</summary>
    private static List<Cuboidi> OtherStructures(IBlockAccessor blocks, Cuboidi location)
    {
        var column = new Cuboidi(location.X1, 0, location.Z1, location.X2, location.Y2, location.Z2);
        int regionSize = blocks.RegionSize;
        var found = new List<Cuboidi>();
        if (regionSize <= 0) return found;
        for (int rx = location.X1 / regionSize; rx <= (location.X2 - 1) / regionSize; rx++)
            for (int rz = location.Z1 / regionSize; rz <= (location.Z2 - 1) / regionSize; rz++)
                if (blocks.GetMapRegion(rx, rz)?.GeneratedStructures is { } placed)
                    found.AddRange(placed.Select(g => g.Location).Where(l => l != null && l.Intersects(column)));
        return found;
    }
}
