using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SeraphHorizons.PackTests;

/// <summary>
/// seraphhorizons' <c>RuinsOnMedianGround</c> (mods-src/seraphhorizons/RuinSurfaceMedian.cs): the
/// game's own <c>WorldGenStructure.TryGenerateRuinAtSurface</c>, run on synthetic terrain, seats a
/// 20 x 20 ruin on the median of the heights it samples rather than the lowest; and the foundation
/// laid under a placed ruin (RuinFoundations.cs) fills the air under its low side, with the game's
/// own blocks. Its off check is in <see cref="SwitchesOffScenarios"/>.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Ruins_on_median_ground_a_surface_ruin_sits_on_the_median_sample()
    {
        Assert.True(RuinSurfaceMedian.Patched, "RuinsOnMedianGround patched nothing");
        var (low, high) = RuinSeating.Heights(W);
        Assert.Equal(high, RuinSeating.SeatedHeight(W, low, high));
    }

    [AtlasScenario]
    public void Ruins_on_median_ground_a_ruin_on_a_slope_stands_on_a_foundation()
    {
        Assert.True(Harmony.GetPatchInfo(RuinSurfaceMedian.Target)?.Postfixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) ?? false,
            "RuinsOnMedianGround laid no foundation");
        RuinFooting.Check(W);
    }
}

/// <summary>The foundation under a 6 x 7 ruin like BetterRuins' waystone (OffsetY -1, seated on
/// 141, a cobblestone floor at 140) on ground falling from 141 on the north edge to 137 on the south,
/// in a block accessor of its own (a dictionary of blocks and one chunk's heightmaps). Every column
/// under the floor has ground up to 139 after it, grass back on top, except four left as they are:
/// one with water in the gap, one over a hole deeper than the foundation goes, one whose gap another
/// structure holds, and one with nothing in the ruin above.</summary>
internal static class RuinFooting
{
    private const int X0 = 1000, Z0 = 1000, SizeX = 6, SizeY = 4, SizeZ = 7, Floor = 140, OffsetY = -1;
    private static readonly int[] GroundByRow = [141, 140, 140, 139, 138, 138, 137];
    private static readonly (int I, int J) Wet = (5, 6), Hole = (4, 6), Taken = (3, 6), Empty = (2, 5);

    public static void Check(IWorldAccessor w)
    {
        Block B(string code) => w.GetBlock(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}");
        Block grass = B("game:soil-medium-normal"), soil = B("game:soil-medium-none"), rock = B("game:rock-granite"),
            floor = B("game:cobblestone-granite"), water = B("game:water-still-7");

        var accessor = DispatchProxy.Create<IBlockAccessor, WorldAccessor>();
        var world = (WorldAccessor)(object)accessor;
        world.Blocks = w;
        var chunk = DispatchProxy.Create<IMapChunk, ChunkProxy>();
        var heights = (ChunkProxy)(object)chunk;
        world.Chunk = chunk;
        var region = DispatchProxy.Create<IMapRegion, RegionProxy>();
        ((RegionProxy)(object)region).Structures =
            [new GeneratedStructure { Code = "test", Location = new Cuboidi(X0 + Taken.I, 130, Z0 + Taken.J, X0 + Taken.I + 1, 140, Z0 + Taken.J + 1) }];
        world.Region = region;

        var schematic = new BlockSchematicStructure
        {
            SizeX = SizeX, SizeY = SizeY, SizeZ = SizeZ, OffsetY = OffsetY,
            blocksByPos = new Block[SizeX + 1, SizeY + 1, SizeZ + 1],
        };
        int Ground(int i, int j) => (i, j) == Hole ? 120 : GroundByRow[j];
        for (int i = 0; i < SizeX; i++)
            for (int j = 0; j < SizeZ; j++)
            {
                int x = X0 + i, z = Z0 + j, ground = Ground(i, j);
                for (int y = 100; y < ground; y++) world.Solid[(x, y, z)] = y < 110 ? rock.Id : soil.Id;
                world.Solid[(x, ground, z)] = grass.Id;
                heights.Terrain[Index(x, z)] = (ushort)ground;
                heights.Rain[Index(x, z)] = (ushort)ground;
                heights.Rock[Index(x, z)] = rock.Id;
                if ((i, j) == Empty) continue;
                schematic.blocksByPos[i, 0, j] = floor;
                world.Solid[(x, Floor, z)] = floor.Id;
            }
        world.Fluid[(X0 + Wet.I, 138, Z0 + Wet.J)] = water.Id;

        var location = new Cuboidi(X0, Floor, Z0, X0 + SizeX, Floor + SizeY, Z0 + SizeZ);
        int filled = SeraphHorizons.Mod.RuinFoundations.Lay(accessor, location, schematic);

        int expected = 0;
        for (int i = 0; i < SizeX; i++)
            for (int j = 0; j < SizeZ; j++)
            {
                int x = X0 + i, z = Z0 + j, ground = Ground(i, j);
                int Id(int y) => world.Solid.GetValueOrDefault((x, y, z));
                if ((i, j) == Wet || (i, j) == Hole || (i, j) == Taken || (i, j) == Empty || ground >= Floor - 1)
                {
                    for (int y = ground + 1; y < Floor; y++) Assert.True(Id(y) == 0, $"{i},{j} filled at {y}");
                    Assert.Equal(ground, heights.Terrain[Index(x, z)]);
                    continue;
                }
                expected += Floor - 1 - ground;
                for (int y = ground; y < Floor - 1; y++) Assert.True(Id(y) == soil.Id, $"{i},{j} at {y}: {w.GetBlock(Id(y))?.Code}");
                Assert.True(Id(Floor - 1) == grass.Id, $"{i},{j}: no grass on top of the foundation");
                Assert.Equal(Floor - 1, heights.Terrain[Index(x, z)]);
                Assert.Equal(Floor - 1, heights.Rain[Index(x, z)]);
            }
        Assert.True(expected > 0);
        Assert.Equal(expected, filled);
    }

    private static int Index(int x, int z) => z % 32 * 32 + x % 32;

    public class WorldAccessor : DispatchProxy
    {
        public IWorldAccessor Blocks = null!;
        public IMapChunk Chunk = null!;
        public IMapRegion Region = null!;
        public readonly Dictionary<(int, int, int), int> Solid = [];
        public readonly Dictionary<(int, int, int), int> Fluid = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IBlockAccessor.GetMapChunk) when args is [int, int]:
                    return Chunk;
                case "get_" + nameof(IBlockAccessor.RegionSize):
                    return 512;
                case nameof(IBlockAccessor.GetMapRegion) when args is [int, int]:
                    return Region;
                case nameof(IBlockAccessor.GetBlock) when args is [int id]:
                    return Blocks.GetBlock(id);
                case nameof(IBlockAccessor.GetBlock) when args is [BlockPos pos, int layer]:
                    var cells = layer == BlockLayersAccess.Fluid ? Fluid : Solid;
                    return Blocks.GetBlock(cells.GetValueOrDefault((pos.X, pos.Y, pos.Z)));
                case nameof(IBlockAccessor.SetBlock) when args is [int id, BlockPos pos, int layer]:
                    (layer == BlockLayersAccess.Fluid ? Fluid : Solid)[(pos.X, pos.Y, pos.Z)] = id;
                    return null;
                default:
                    throw new NotSupportedException($"the test's block accessor has no {method.Name}({string.Join(", ", args?.Select(a => a?.GetType().Name) ?? [])})");
            }
        }
    }

    public class ChunkProxy : DispatchProxy
    {
        public readonly ushort[] Terrain = new ushort[32 * 32];
        public readonly ushort[] Rain = new ushort[32 * 32];
        public readonly int[] Rock = new int[32 * 32];

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_" + nameof(IMapChunk.WorldGenTerrainHeightMap) => Terrain,
            "get_" + nameof(IMapChunk.RainHeightMap) => Rain,
            "get_" + nameof(IMapChunk.TopRockIdMap) => Rock,
            _ => throw new NotSupportedException($"the test's map chunk has no {method.Name}"),
        };
    }

    public class RegionProxy : DispatchProxy
    {
        public List<GeneratedStructure> Structures = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_" + nameof(IMapRegion.GeneratedStructures) => Structures,
            _ => throw new NotSupportedException($"the test's map region has no {method.Name}"),
        };
    }
}

/// <summary>Runs the game's surface ruin placement for a synthetic 20 x 20 schematic on terrain
/// that is <c>high</c> everywhere but the start column, which is <c>low</c> (3 lower, the default
/// <c>MaxYDiff</c>). The game samples the start twice and nine other spots for a ruin this size,
/// so the lowest is <c>low</c> and the median <c>high</c>. The block accessor answers water at the
/// first liquid check, right after the seating, so the call stops there and changes nothing.</summary>
internal static class RuinSeating
{
    private const int Size = 20;
    private const int OffsetY = -2;

    public static (int Low, int High) Heights(IWorldAccessor w) => (w.SeaLevel + 17, w.SeaLevel + 20);

    /// <summary>The height the game seated the ruin on (its base, before <c>OffsetY</c>).</summary>
    public static int SeatedHeight(IWorldAccessor w, int low, int high)
    {
        var schematic = new BlockSchematicStructure
        {
            SizeX = Size, SizeY = 4, SizeZ = Size, OffsetY = OffsetY, MaxYDiff = 3, EntranceRotation = -1,
            // Already unpacked: nothing to rotate or resolve.
            blocksByPos = new Block[Size + 1, 5, Size + 1],
        };
        var structure = new WorldGenStructure();
        AccessTools.Field(typeof(WorldGenStructure), "schematicDatas")
            .SetValue(structure, new[] { new[] { schematic, schematic, schematic, schematic } });
        AccessTools.Field(typeof(WorldGenStructure), "rand").SetValue(structure, new LCGRandom(1));

        var start = new BlockPos(1000, 0, 1000);
        var accessor = DispatchProxy.Create<IBlockAccessor, TerrainAccessor>();
        var terrain = (TerrainAccessor)(object)accessor;
        terrain.Height = pos => pos.X == start.X && pos.Z == start.Z ? low : high;
        terrain.Liquid = w.GetBlock(new AssetLocation("game:water-still-7"))
                         ?? throw new Xunit.Sdk.XunitException("no game:water-still-7");

        var placed = (bool)RuinSurfaceMedian.Target!.Invoke(structure, [accessor, w, start, "seraphhorizons-test"])!;
        Assert.False(placed);
        Assert.True(terrain.SeatedY.HasValue, "the placement gave up before seating the ruin");
        Assert.True(terrain.Samples >= 11, $"{terrain.Samples} terrain samples");
        // The first liquid check is at startPos.Y + 1, and startPos.Y = base + OffsetY.
        return terrain.SeatedY.Value - 1 - OffsetY;
    }

    public class TerrainAccessor : DispatchProxy
    {
        public System.Func<BlockPos, int> Height = _ => 0;
        public Block Liquid = null!;
        public int? SeatedY;
        public int Samples;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IBlockAccessor.GetTerrainMapheightAt):
                    Samples++;
                    return Height((BlockPos)args![0]!);
                case nameof(IBlockAccessor.GetBlock) when args is [BlockPos pos, int]:
                    SeatedY ??= pos.Y;
                    return Liquid;
                default:
                    throw new NotSupportedException($"the test's block accessor has no {method.Name}");
            }
        }
    }
}
