using Atlas.XUnit;
using Vintagestory.API.Common;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, mining on ground from the game's own world generation (seed
/// <see cref="RealTerrain.Seed"/>, a standard world): each kind of ground a player marks an area on found near spawn
/// by a fixed search (<see cref="RealTerrain"/>: a point and a radius each), a crew of one to three spawned beside it
/// and ordered with the command tool, run on the virtual clock and held to the owner's bars (<see cref="RealMineRun"/>:
/// done means done, red never grows, everyone works). Small areas (16 × 8 × 12) run to the job's end; large ones
/// (37 × 12 × 28, the owner's) for <see cref="Minutes"/> of game time. A class's scenarios share its world, each on a
/// spot of its own.
/// </summary>
public abstract class EidolonRealTerrainBase(ITestOutputHelper output) : AtlasScenarioBase
{
    /// <summary>A large area's span of game time (the hillside fixture's, EidolonHillsideScenarios).</summary>
    protected const double Minutes = 8;

    private IWorldAccessor W => World.Api.World;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-realterrain-test] {0}", what);
        output.WriteLine(what);
    }

    /// <summary>Finds <paramref name="kind"/> within 16 of spawn + (<paramref name="dx"/>, <paramref name="dz"/>) and
    /// has <paramref name="crew"/> mine it, to the end or for <paramref name="minutes"/>.</summary>
    private protected async Task Mine(TerrainKind kind, int dx, int dz, int crew, string player, int sx = 16, int sy = 8, int sz = 12,
        double? minutes = null)
    {
        var spot = await RealTerrain.Find(World, kind, sx, sy, sz, World.Spawn.AddCopy(dx, 0, dz), 16, crew, Log);
        var run = await new RealMineRun(World, Log).Run(spot, crew, player, minutes, maxTicks: minutes is { } m ? (int)(m * 60 * 30) + 600 : 60_000);
        Assert.True(run.Faults.Count == 0, $"{kind} at {RealTerrain.Describe(spot.Area)} (seed {RealTerrain.Seed}), crew {crew}: "
            + string.Join(" || ", run.Faults));
        Assert.True(run.Mined > 0, $"{kind}: nothing mined");
    }
}

/// <summary>Slopes: a hillside, part covered and part open, two finishing it; a heading driven into a mountain from the
/// foot of its face by two; and a cut into a mountain face twice a heading high, whose top band is out of reach from
/// the foot (README "Eidolon", mining, *Tunnels*: a step in front of the face would bring it in reach) and the floor
/// under it kept, by two, the bars held all the same.</summary>
[AtlasWorld(Seed = RealTerrain.Seed, WorldType = "standard")]
public class EidolonRealSlopeScenarios(ITestOutputHelper output) : EidolonRealTerrainBase(output)
{
    [AtlasScenario(TimeoutMs = 240_000)]
    public Task Two_finish_a_hillside() => Mine(TerrainKind.Hillside, 0, 12, 2, "realhillside");

    [AtlasScenario(TimeoutMs = 240_000)]
    public Task Two_drive_a_heading_into_a_mountain() => Mine(TerrainKind.Mountain, -8, -86, 2, "realmountain", sy: 4);

    [AtlasScenario(TimeoutMs = 240_000)]
    public Task Two_on_a_cut_into_a_mountain_twice_a_heading_high() => Mine(TerrainKind.Mountain, -180, 74, 2, "realmountaintall");
}

/// <summary>Open ground: a pit in flat ground, one digging it; rough ground (soil, gravel, loose stones, plants, a tree's
/// edge), three.</summary>
[AtlasWorld(Seed = RealTerrain.Seed, WorldType = "standard")]
public class EidolonRealGroundScenarios(ITestOutputHelper output) : EidolonRealTerrainBase(output)
{
    [AtlasScenario(TimeoutMs = 240_000)]
    public Task One_digs_a_pit_in_flat_ground() => Mine(TerrainKind.Pit, -16, 38, 1, "realpit");

    [AtlasScenario(TimeoutMs = 240_000)]
    public Task Three_dig_rough_ground() => Mine(TerrainKind.Rough, 34, -8, 3, "realrough");
}

/// <summary>The owner's size of area, 37 × 12 × 28, each for <see cref="EidolonRealTerrainBase.Minutes"/>: on a
/// hillside at spawn, two at it, as the owner's were; on rough ground with trees, gravel and loose stones in it, three.</summary>
[AtlasWorld(Seed = RealTerrain.Seed, WorldType = "standard")]
public class EidolonRealLargeScenarios(ITestOutputHelper output) : EidolonRealTerrainBase(output)
{
    [AtlasScenario(TimeoutMs = 240_000)]
    public Task Two_on_a_large_hillside_for_eight_minutes() =>
        Mine(TerrainKind.Hillside, -2, 2, 2, "realhilllarge", 37, 12, 28, Minutes);

    [AtlasScenario(TimeoutMs = 240_000)]
    public Task Three_on_large_rough_ground_for_eight_minutes() =>
        Mine(TerrainKind.Rough, 84, 90, 3, "realroughlarge", 37, 12, 28, Minutes);
}
