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
/// 20 x 20 ruin on the median of the heights it samples rather than the lowest. Its off check is in
/// <see cref="SwitchesOffScenarios"/>.
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
