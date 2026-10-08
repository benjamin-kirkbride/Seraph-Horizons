using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>How a camp structure is placed (the game's <c>placement</c>): every camp is
/// <c>surface</c> but vanilla's shallow-water camp.</summary>
public enum CampPlacement { Surface, ShallowWater }

/// <summary>Ground a camp's samples passed: <see cref="Base"/> is the terrain height it is seated on
/// (its floor at <c>Base + 1 + OffsetY</c>, as the game's), <see cref="Centre"/> the centre sample
/// (the game's sea-depth check reads it), <see cref="Slope"/> highest minus lowest sample.</summary>
public readonly record struct GroundFit(int Base, int Centre, int Slope);

/// <summary>What levelling does to one column of the footprint: blocks filled in from
/// <see cref="FillFrom"/> to <see cref="FillTo"/>, cleared from <see cref="CutFrom"/> to
/// <see cref="CutTo"/> (inclusive; empty when From is above To).</summary>
public readonly record struct ColumnWork(int FillFrom, int FillTo, int CutFrom, int CutTo)
{
    public bool Fills => FillFrom <= FillTo;
    public bool Cuts => CutFrom <= CutTo;
}

/// <summary>A column of the skirt around a levelled footprint (<see cref="CampGround.Skirt"/>): its
/// terrain height and the height it is cut or filled to.</summary>
public readonly record struct SkirtColumn(int X, int Z, int Height, int Target);

/// <summary>
/// The ground test for a trader camp (#599), game-independent. It mirrors the game's own surface
/// placement (<c>WorldGenStructure.TryGenerateAtSurface</c>, VSEssentials 1.22.7): the terrain height
/// (<c>WorldGenTerrainHeightMap</c>) sampled at five points of the schematic's footprint, its centre
/// (start + ceil(size / 2)) and its four corners at start, +SizeX, +SizeZ and both, one block past
/// the footprint and so often in the next chunk. The game wants all five exactly equal; on rolling
/// terrain (Conquest Landform Overhaul's) a spot rarely has such a position. A camp takes ground
/// whose samples differ by at most a tolerance (<c>slopeTolerance</c> in
/// <c>config/trading/camps.json</c>, 2 by default; 0 is the game's rule), seated on the median of
/// the samples as surface ruins are under <c>RuinsOnMedianGround</c> (<see cref="RuinSurfaceHeight"/>),
/// and the terrain under its footprint is levelled to that height: filled up where lower, cut down
/// where higher (<see cref="Level"/>), so nothing floats or is buried, and the terrain around it is
/// blended into the pad (<see cref="Skirt"/>), so it isn't left in a cut or on a plinth. The
/// shallow-water camp keeps the game's own rule (samples exactly one apart, no levelling: it stands
/// in water).
/// </summary>
public static class CampGround
{
    public const int DefaultTolerance = 2;

    /// <summary>The game's five sample points for a footprint whose corner is (x, z): the centre
    /// first, then the corners at x, x + sizeX, z, z + sizeZ.</summary>
    public static (int X, int Z)[] SamplePoints(int x, int z, int sizeX, int sizeZ) =>
    [
        (x + HalfUp(sizeX), z + HalfUp(sizeZ)),
        (x, z),
        (x + sizeX, z),
        (x, z + sizeZ),
        (x + sizeX, z + sizeZ),
    ];

    /// <summary>Whether a footprint at a chunk-local corner (0..31) keeps every sample in the chunk
    /// or its +X, +Z and diagonal neighbours: the chunks whose terrain the TerrainFeatures pass is
    /// sure to have (the engine runs it only once all eight neighbours have finished Terrain).</summary>
    public static bool InNeighbourhood(int localX, int localZ, int sizeX, int sizeZ) =>
        localX >= 0 && localZ >= 0 && localX + sizeX < 64 && localZ + sizeZ < 64;

    /// <summary>The ground a placement takes, from samples in <see cref="SamplePoints"/>' order, or
    /// null if it doesn't.</summary>
    public static GroundFit? Fit(CampPlacement placement, IReadOnlyList<int> samples, int tolerance) =>
        placement == CampPlacement.ShallowWater ? ShallowWater(samples) : Surface(samples, tolerance);

    /// <summary>Surface camps: samples at most <paramref name="tolerance"/> apart, seated on their
    /// median (with all five equal, the game's own height).</summary>
    public static GroundFit? Surface(IReadOnlyList<int> samples, int tolerance)
    {
        if (samples.Count != 5) return null;
        int min = samples.Min(), max = samples.Max();
        if (max - min > Math.Max(0, tolerance)) return null;
        return new GroundFit(RuinSurfaceHeight.Median(samples, samples[0]), samples[0], max - min);
    }

    /// <summary>The shallow-water camp, as the game places it: samples exactly one apart, seated one
    /// above the highest.</summary>
    public static GroundFit? ShallowWater(IReadOnlyList<int> samples)
    {
        if (samples.Count != 5) return null;
        int min = samples.Min(), max = samples.Max();
        return max - min == 1 ? new GroundFit(max, samples[0], 1) : null;
    }

    /// <summary>The game's sea-depth limit: the centre sample more than the structure's
    /// <c>MaxBelowSealevel</c> under the sea.</summary>
    public static bool TooDeep(int centre, int seaLevel, int maxBelowSealevel) => centre < seaLevel - maxBelowSealevel;

    /// <summary>The schematic's footprint, [x, x + sizeX) × [z, z + sizeZ).</summary>
    public static bool InFootprint(int px, int pz, int x, int z, int sizeX, int sizeZ) =>
        px >= x && px < x + sizeX && pz >= z && pz < z + sizeZ;

    /// <summary>The terrain height once levelled: the base inside the footprint, unchanged outside.</summary>
    public static int LevelledHeight(int terrain, bool inFootprint, int @base) => inFootprint ? @base : terrain;

    /// <summary>Levelling a footprint column of terrain height <paramref name="height"/> to
    /// <paramref name="base"/>: lower ground is filled up to the base, higher ground cleared down to
    /// it.</summary>
    public static ColumnWork Level(int height, int @base) =>
        height < @base ? new ColumnWork(height + 1, @base, 0, -1)
        : height > @base ? new ColumnWork(0, -1, @base + 1, height)
        : new ColumnWork(0, -1, 0, -1);

    /// <summary>The most a footprint column is filled or cut. The samples only see the footprint's
    /// edge and middle; a column further off the base than this is a ravine or a crag the samples
    /// missed, not a slope to even out, and the camp doesn't go there.</summary>
    public const int MaxLevelling = 6;

    /// <summary>Whether every column of a footprint (its terrain heights) is within
    /// <see cref="MaxLevelling"/> of the base.</summary>
    public static bool Levellable(IEnumerable<int> heights, int @base) => heights.All(h => Math.Abs(h - @base) <= MaxLevelling);

    /// <summary>The most rings of skirt around a levelled footprint.</summary>
    public const int MaxSkirt = MaxLevelling;

    /// <summary>
    /// The skirt that blends a levelled footprint into the terrain around it, so the pad doesn't
    /// sit in a cut or on a plinth: a column outside the footprint at Chebyshev distance d (its
    /// ring) is held to [base − d, base + d], cut down or filled up to that (a slope of at most one
    /// block per block away from the pad). Rings are worked outward from the footprint until one
    /// needs nothing; null when the skirt can't be done: a column <paramref name="height"/> doesn't
    /// reach (null) or has no terrain (0 or below), one more than <see cref="MaxLevelling"/> off its
    /// bound (a crag or ravine beside the camp), or ring <see cref="MaxSkirt"/> + 1 still needing
    /// work (the ground falls or climbs away steeper than the skirt can take up). A column worked
    /// keeps every step to its neighbours no higher than it was, and the pad's edge steps at most one.
    /// </summary>
    public static List<SkirtColumn>? Skirt(int x, int z, int sizeX, int sizeZ, int @base, Func<int, int, int?> height)
    {
        var work = new List<SkirtColumn>();
        for (int d = 1; d <= MaxSkirt + 1; d++)
        {
            bool any = false;
            foreach (var (px, pz) in Ring(x, z, sizeX, sizeZ, d))
            {
                if (height(px, pz) is not { } h || h <= 0) return null;
                int target = Math.Clamp(h, @base - d, @base + d);
                if (target == h) continue;
                if (d > MaxSkirt || Math.Abs(h - target) > MaxLevelling) return null;
                work.Add(new SkirtColumn(px, pz, h, target));
                any = true;
            }
            if (!any) return work;
        }
        return null;
    }

    /// <summary>The columns at Chebyshev distance <paramref name="d"/> (1 or more) from the footprint
    /// [x, x + sizeX) × [z, z + sizeZ).</summary>
    public static IEnumerable<(int X, int Z)> Ring(int x, int z, int sizeX, int sizeZ, int d)
    {
        int x0 = x - d, x1 = x + sizeX - 1 + d, z0 = z - d, z1 = z + sizeZ - 1 + d;
        for (int px = x0; px <= x1; px++)
        {
            yield return (px, z0);
            yield return (px, z1);
        }
        for (int pz = z0 + 1; pz < z1; pz++)
        {
            yield return (x0, pz);
            yield return (x1, pz);
        }
    }

    /// <summary>The game's underground check (stone, soil, sand or gravel there) against levelled
    /// ground: inside the footprint a block the levelling fills counts as ground, one it clears does
    /// not; anywhere else (null) the block as it is decides.</summary>
    public static bool? LevelledGround(int y, int terrain, bool inFootprint, int @base)
    {
        if (!inFootprint) return null;
        if (y > @base) return false;
        return y > terrain ? true : null;
    }

    /// <summary>The game's liquid checks for a surface placement seated on <paramref name="base"/>
    /// (no liquid at any of these): the centre and corners at the ground, the corners one and two
    /// above it.</summary>
    public static (int X, int Y, int Z)[] SurfaceLiquidChecks(int x, int z, int sizeX, int sizeZ, int @base)
    {
        int y = @base + 1;
        int cx = x + HalfUp(sizeX), cz = z + HalfUp(sizeZ);
        return
        [
            (cx, y - 1, cz), (x, y - 1, z), (x + sizeX, y - 1, z), (x, y - 1, z + sizeZ), (x + sizeX, y - 1, z + sizeZ),
            (x, y, z), (x + sizeX, y, z + sizeZ), (x + sizeX, y, z), (x, y, z + sizeZ),
            (x, y + 1, z), (x + sizeX, y + 1, z), (x, y + 1, z + sizeZ), (x + sizeX, y + 1, z + sizeZ),
        ];
    }

    /// <summary>The game's liquid checks for the shallow-water camp: the centre and corners at the
    /// centre's ground (seabed).</summary>
    public static (int X, int Y, int Z)[] ShallowLiquidChecks(int x, int z, int sizeX, int sizeZ, int centre)
    {
        int cx = x + HalfUp(sizeX), cz = z + HalfUp(sizeZ);
        return [(cx, centre, cz), (x, centre, z), (x + sizeX, centre, z), (x, centre, z + sizeZ), (x + sizeX, centre, z + sizeZ)];
    }

    private static int HalfUp(int n) => (n + 1) / 2;
}
