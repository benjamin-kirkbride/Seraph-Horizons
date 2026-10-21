using Atlas.Api;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>The kinds of ground a player marks a mining area on (EidolonRealTerrain* scenarios).</summary>
internal enum TerrainKind
{
    /// <summary>Straddling a slope steep enough that part of the area is covered (rock within
    /// <see cref="EidolonTunnels.RoofRise"/> over its top, <see cref="EidolonTunnels.Covered"/>) and part open:
    /// from the lowest ground up.</summary>
    Hillside,

    /// <summary>Flat ground, dug down from its surface: no column a block higher than another, nothing over it.</summary>
    Pit,

    /// <summary>Uneven open ground dug down from its highest surface: soil, gravel, loose stones, plants and a tree or
    /// its leaves in it.</summary>
    Rough,

    /// <summary>Into a mountain from the foot of its face: covered all over or nearly (four fifths of its columns or
    /// more), mostly a tunnel, its floor level with the feet of one standing on the ground in front.</summary>
    Mountain,
}

/// <summary>A spot found for a kind: the area to mark, the side it is walked onto from (0 north, -z; 1 south, +z;
/// 2 west, -x; 3 east, +x), where a crew stands to be ordered (outside the area on that side, on the ground) and
/// what was found there, for the log.</summary>
internal sealed record TerrainSpot(TerrainKind Kind, MarkArea Area, int Side, IReadOnlyList<Vec3d> Stands, string Why);

/// <summary>
/// Spots for mining on terrain from the game's own world generation (seed <see cref="Seed"/>, a standard world):
/// the chunk columns round a fixed point generated, then corners within a radius of it tried in a fixed order
/// (nearest first, then by x and z) and the first whose ground is the kind asked taken, so a run finds the same
/// spot every time on the same pack. Nothing is built: the area is marked on the ground as generated. The kinds'
/// tests read the blocks as the mine planner does (<see cref="MineOrder"/>'s roof, the collision boxes'
/// solidity). A kind not found fails, saying what the nearest candidates were.
/// </summary>
internal static class RealTerrain
{
    /// <summary>A seed with every kind near its spawn (512000, 162, 512000): found by dumping the 17 × 17 chunk
    /// columns round spawn of three seeds; 777001's mountains near spawn are ruins, 515151 has none.</summary>
    public const int Seed = 31337;

    // The areas found on each world so far: a class's scenarios share its world, so none marks another's spot.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IWorldAccessor, List<MarkArea>> Taken = new();

    /// <summary>How far round the area the chunks are generated and the side strips read.</summary>
    private const int Margin = 12;

    /// <summary>The game's roof (MineOrder.Roof, internal to the mod): a full block of ground over the area makes
    /// its column covered (<see cref="EidolonTunnels.Covered"/>).</summary>
    public static bool Roof(Block block) =>
        block.Id != 0 && !block.IsLiquid() && block.SideSolid[BlockFacing.DOWN.Index]
        && block.BlockMaterial is EnumBlockMaterial.Stone or EnumBlockMaterial.Ore or EnumBlockMaterial.Soil or EnumBlockMaterial.Gravel
            or EnumBlockMaterial.Sand or EnumBlockMaterial.Brick or EnumBlockMaterial.Ceramic or EnumBlockMaterial.Mantle;

    /// <summary>Generates the chunk columns round <paramref name="from"/> (<paramref name="radius"/> and the area
    /// round it) and finds the <paramref name="kind"/>'s spot nearest it, an area
    /// <paramref name="sx"/> × <paramref name="sy"/> × <paramref name="sz"/>, with stands for
    /// <paramref name="crew"/>.</summary>
    public static async Task<TerrainSpot> Find(IWorldSession world, TerrainKind kind, int sx, int sy, int sz, BlockPos from, int radius,
        int crew, Action<string> log)
    {
        var sapi = (ICoreServerAPI)world.Api;
        var w = sapi.World;
        int size = GlobalConstants.ChunkSize;
        int x0 = from.X - radius - sx - Margin, x1 = from.X + radius + sx + Margin;
        int z0 = from.Z - radius - sz - Margin, z1 = from.Z + radius + sz + Margin;
        var columns = new List<(int X, int Z)>();
        for (int cx = x0 / size; cx <= x1 / size; cx++)
        for (int cz = z0 / size; cz <= z1 / size; cz++)
            columns.Add((cx, cz));
        foreach (var (cx, cz) in columns)
            sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
        long since = Environment.TickCount64;
        await world.Until(() => columns.All(c => sapi.WorldManager.GetMapChunk(c.X, c.Z)?.CurrentPass == EnumWorldGenPass.Done
                                                 && w.BlockAccessor.GetChunk(c.X, 0, c.Z) != null), 60_000);
        log($"{kind}: {columns.Count} chunk columns round {from} generated in {(Environment.TickCount64 - since) / 1000.0:0.0} s");

        var taken = Taken.GetOrCreateValue(w);
        var terrain = new Reader(w, sapi, x0, z0, x1, z1);
        var tried = new List<(int Distance, int X, int Z)>();
        for (int x = from.X - radius; x <= from.X + radius; x += 2)
        for (int z = from.Z - radius; z <= from.Z + radius; z += 2)
            tried.Add((Math.Abs(x + sx / 2 - from.X) + Math.Abs(z + sz / 2 - from.Z), x, z));
        var near = new List<(int Distance, string What)>();
        foreach (var (distance, x, z) in tried.OrderBy(t => t.Distance).ThenBy(t => t.X).ThenBy(t => t.Z))
        {
            // clear of the spots an earlier scenario on this world took, their stands and a margin round them
            if (taken.Any(t => x <= t.Max.X + Margin && x + sx - 1 >= t.Min.X - Margin && z <= t.Max.Z + Margin && z + sz - 1 >= t.Min.Z - Margin))
                continue;
            var (spot, what) = terrain.Try(kind, x, z, sx, sy, sz, crew);
            if (spot != null)
            {
                taken.Add(spot.Area);
                log($"{kind}: found at {distance} from {from}, after {near.Count} rejected: {spot.Why}");
                return spot;
            }
            if (near.Count < 6 && what != null)
                near.Add((distance, what));
        }
        throw new Xunit.Sdk.XunitException($"no {kind} {sx}x{sy}x{sz} within {radius} of {from} on seed {Seed} "
            + $"(the pack's world generation changed?); nearest rejected: {string.Join(" | ", near.Select(n => $"{n.Distance}: {n.What}"))}");
    }

    /// <summary>The generated ground in a box of columns, read once: each column's surface (the world generator's
    /// terrain height) and blocks on demand.</summary>
    private sealed class Reader(IWorldAccessor w, ICoreServerAPI sapi, int x0, int z0, int x1, int z1)
    {
        private readonly BlockPos _pos = new(0);
        private readonly Dictionary<(int, int), int> _height = new();

        private Block At(int x, int y, int z) => w.BlockAccessor.GetBlock(_pos.Set(x, y, z));

        private bool Bad(int x, int y, int z)
        {
            var b = At(x, y, z);
            return b.IsLiquid() || w.BlockAccessor.GetBlock(_pos, BlockLayersAccess.Fluid).IsLiquid()
                || b.Code?.Path.StartsWith("lakeice") == true || b.Code?.Path.StartsWith("glacierice") == true;
        }

        private bool Solid(int x, int y, int z)
        {
            var b = At(x, y, z);
            return b.Id != 0 && GameMineWorld.Solid(b.GetCollisionBoxes(w.BlockAccessor, _pos));
        }

        public int Height(int x, int z)
        {
            if (_height.TryGetValue((x, z), out int h))
                return h;
            int size = GlobalConstants.ChunkSize;
            var map = sapi.WorldManager.GetMapChunk(x / size, z / size);
            h = map.WorldGenTerrainHeightMap[(z % size) * size + x % size];
            _height[(x, z)] = h;
            return h;
        }

        // The kinds' tests at one corner: the spot, or why not (null for a corner not worth naming).
        public (TerrainSpot? Spot, string? Why) Try(TerrainKind kind, int ax, int az, int sx, int sy, int sz, int crew)
        {
            if (ax - Margin < x0 || az - Margin < z0 || ax + sx + Margin > x1 || az + sz + Margin > z1)
                return (null, null);
            int lo = int.MaxValue, hi = int.MinValue;
            for (int x = ax; x < ax + sx; x++)
            for (int z = az; z < az + sz; z++)
            {
                lo = Math.Min(lo, Height(x, z));
                hi = Math.Max(hi, Height(x, z));
            }
            int spread = hi - lo;
            switch (kind)
            {
                case TerrainKind.Pit:
                case TerrainKind.Rough:
                {
                    var area = Box(ax, az, hi - sy + 1, sx, sy, sz);
                    if (Read(area) is not { } r)
                        return (null, $"{Describe(area)}: {_rejected}");
                    string what = $"{Describe(area)} spread {spread}, {r}";
                    bool ok = kind == TerrainKind.Pit
                        ? r.Covered == 0 && spread <= 1 && r.Count("log") == 0
                        : r.Covered <= 0.1 && spread is >= 2 and <= 5 && r.Count("log") + r.Count("leaves") > 0
                          && r.Count("loosestones") + r.Count("looseflints") > 0 && r.Count("tallgrass") + r.Count("flower") > 0
                          && r.Count("soil") > 0 && r.Count("gravel") > 0;
                    // walked onto from the side with the most ground within a climb of the top
                    return ok ? Spot(kind, area, Sides(area).OrderByDescending(s => Strip(area, s, area.Max.Y - 2, area.Max.Y + 1)).First(),
                        what, crew) : (null, what);
                }
                case TerrainKind.Hillside:
                {
                    var area = Box(ax, az, lo, sx, sy, sz);
                    if (Read(area) is not { } r)
                        return (null, $"{Describe(area)}: {_rejected}");
                    // the open side: the most ground out there at the area's floor
                    int side = Sides(area).OrderByDescending(s => Strip(area, s, lo - 2, lo + 1)).First();
                    double open = Strip(area, side, lo - 2, lo + 1);
                    string what = $"{Describe(area)} spread {spread}, {r}, open side {side} {open:0.00}";
                    bool ok = r.Covered is >= 0.25 and <= 0.7 && r.SolidShare >= 0.35 && open >= 0.5;
                    return ok ? Spot(kind, area, side, what, crew) : (null, what);
                }
                case TerrainKind.Mountain:
                {
                    // each side's foot: the ground in front of it, level to a block over most of it; the floor on it
                    string? last = null;
                    foreach (int side in Sides(Box(ax, az, lo, sx, sy, sz)))
                    {
                        var strip = StripHeights(ax, az, sx, sz, side, 1).OrderBy(h => h).ToList();
                        int foot = strip[strip.Count / 2];
                        if (strip.Count(h => Math.Abs(h - foot) <= 1) < 0.7 * strip.Count)
                            continue;
                        var area = Box(ax, az, foot + 1, sx, sy, sz);
                        if (Read(area) is not { } r)
                        {
                            last = $"{Describe(area)} from side {side}: {_rejected}";
                            continue;
                        }
                        last = $"{Describe(area)} from side {side} (foot {foot}), {r}";
                        if (r.Covered >= 0.8 && r.SolidShare >= 0.8)
                            return Spot(kind, area, side, last, crew);
                    }
                    return (null, last);
                }
            }
            return (null, null);
        }

        private static MarkArea Box(int x, int z, int y, int sx, int sy, int sz) =>
            new(new MarkPos(x, y, z), new MarkPos(x + sx - 1, y + sy - 1, z + sz - 1));

        private static int[] Sides(MarkArea area) => [0, 1, 2, 3];

        // The surface heights of a side's strip, `out` columns outside the area.
        private IEnumerable<int> StripHeights(int ax, int az, int sx, int sz, int side, int @out)
        {
            if (side <= 1)
                for (int x = ax; x < ax + sx; x++)
                    yield return Height(x, side == 0 ? az - @out : az + sz - 1 + @out);
            else
                for (int z = az; z < az + sz; z++)
                    yield return Height(side == 2 ? ax - @out : ax + sx - 1 + @out, z);
        }

        // The share of a side's strip, 2 out, whose surface lies within [from, to].
        private double Strip(MarkArea area, int side, int from, int to)
        {
            var h = StripHeights(area.Min.X, area.Min.Z, area.SizeX, area.SizeZ, side, 2).ToList();
            return h.Count(v => v >= from && v <= to) / (double)h.Count;
        }

        private sealed record Read_(double Covered, double SolidShare, Dictionary<string, int> Mix)
        {
            public int Count(string what) => Mix.GetValueOrDefault(what);
            public override string ToString() =>
                $"covered {Covered:0.00}, solid {SolidShare:0.00}, " + string.Join(" ", Mix.OrderByDescending(m => m.Value).Take(10).Select(m => $"{m.Key} {m.Value}"));
        }

        // The area's blocks: null if a liquid or ice is in it or a block round it (the planner leaves those and what
        // touches them: no kind of ground asked for here).
        // why the last Read refused its area
        private string? _rejected;

        private Read_? Read(MarkArea area)
        {
            _rejected = "out of the world";
            if (area.Min.Y < 2 || area.Max.Y + EidolonTunnels.RoofRise >= w.BlockAccessor.MapSizeY)
                return null;
            for (int x = area.Min.X - 1; x <= area.Max.X + 1; x++)
            for (int z = area.Min.Z - 1; z <= area.Max.Z + 1; z++)
            for (int y = area.Min.Y - 1; y <= area.Max.Y + EidolonTunnels.RoofRise; y++)
                if (Bad(x, y, z))
                {
                    var fluid = w.BlockAccessor.GetBlock(_pos.Set(x, y, z), BlockLayersAccess.Fluid);
                    _rejected = $"{At(x, y, z).Code?.ToShortString()}{(fluid.Id != 0 ? $" in {fluid.Code?.ToShortString()}" : "")} at ({x},{y},{z})";
                    return null;
                }
            var covered = EidolonTunnels.Covered(area, c => Roof(At(c.X, c.Y, c.Z)));
            var mix = new Dictionary<string, int>();
            int solid = 0;
            for (int x = area.Min.X; x <= area.Max.X; x++)
            for (int z = area.Min.Z; z <= area.Max.Z; z++)
            for (int y = area.Min.Y; y <= area.Max.Y; y++)
            {
                var b = At(x, y, z);
                if (b.Id == 0)
                    continue;
                string name = b.Code.Path.Split('-')[0];
                mix[name] = mix.GetValueOrDefault(name) + 1;
                if (Solid(x, y, z))
                    solid++;
            }
            return new Read_(covered.Count / (double)(area.SizeX * area.SizeZ), solid / (double)area.Volume, mix);
        }

        // Stands for the crew outside the area, where a player would bring them: on the ground 2 to 8 columns out, the
        // eidolon's box (2 × 2 columns, 4 high) clear, along its side from the middle, then along the sides beside it
        // where that side has too few; a spot needs one stand each.
        private (TerrainSpot? Spot, string? Why) Spot(TerrainKind kind, MarkArea area, int side, string what, int crew)
        {
            var stands = new List<Vec3d>();
            int[] beside = side <= 1 ? [2, 3] : [0, 1];
            foreach (int s in new[] { side }.Concat(beside))
                if (Stands(area, s, crew, stands) is { } found)
                    return (new TerrainSpot(kind, area, side, found, what + $"; stands {string.Join(" ", found.Select(v => $"({v.X:0},{v.Y:0},{v.Z:0})"))}"), what);
            return (null, what + $"; only {stands.Count} of {crew} stands on side {side} and beside it");
        }

        private List<Vec3d>? Stands(MarkArea area, int side, int crew, List<Vec3d> stands)
        {
            bool alongX = side <= 1;
            int length = alongX ? area.SizeX : area.SizeZ;
            // from the side's middle outward, both ways in turn
            var offsets = Enumerable.Range(0, 2 * length).Select(i => length / 2 - 1 + (i % 2 == 0 ? i / 2 : -(i + 1) / 2))
                .Where(a => a >= 0 && a <= length - 2).ToList();
            foreach (int @out in new[] { 3, 4, 2, 5, 6, 7, 8 })
            {
                foreach (int along in offsets)
                {
                    int x = alongX ? area.Min.X + along : side == 2 ? area.Min.X - @out - 1 : area.Max.X + @out;
                    int z = alongX ? (side == 0 ? area.Min.Z - @out - 1 : area.Max.Z + @out) : area.Min.Z + along;
                    if (Stand(x, z, area) is not { } feet)
                        continue;
                    var at = new Vec3d(x + 1, feet, z + 1);
                    if (stands.Any(s => Math.Abs(s.X - at.X) < 3 && Math.Abs(s.Z - at.Z) < 3))
                        continue;
                    stands.Add(at);
                    if (stands.Count == crew)
                        return stands;
                }
            }
            return null;
        }

        // The feet of a 2 × 2 stand at columns (x, z)-(x+1, z+1): on the highest of its four columns' ground (a solid
        // block, from 3 under the area's floor to 3 over its top), none of the others more than a block lower, and 4 of
        // air (or plants) over it in all four.
        private int? Stand(int x, int z, MarkArea area)
        {
            var ground = new int[4];
            for (int i = 0; i < 4; i++)
            {
                ground[i] = -1;
                for (int y = area.Max.Y + 3; y >= area.Min.Y - 3; y--)
                    if (Solid(x + i / 2, y, z + i % 2))
                    {
                        ground[i] = y;
                        break;
                    }
            }
            if (ground.Min() < 0 || ground.Max() - ground.Min() > 1)
                return null;
            int feet = ground.Max() + 1;
            for (int i = 0; i < 4; i++)
            for (int y = feet; y <= feet + 3; y++)
                if (Solid(x + i / 2, y, z + i % 2) || Bad(x + i / 2, y, z + i % 2))
                    return null;
            return feet;
        }
    }

    /// <summary>Puts the server's autosave out of reach (MagicNum.ServerAutoSave, seconds; the world is thrown away after
    /// the run): under load, an autosave while the chunk thread loads the columns a search asked for crashed the chunk
    /// database thread ("Execute requires the command to have a transaction object", as tools/ore-survey found).
    /// Process-wide, as the field is.</summary>
    public static void NoAutosave(Action<string> log)
    {
        var field = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Vintagestory.Server.MagicNum", false))
            .FirstOrDefault(t => t != null)?.GetField("ServerAutoSave");
        if (field == null)
            throw new Xunit.Sdk.XunitException("MagicNum.ServerAutoSave not found: the engine changed (tools/ore-survey's NoAutosave too)");
        field.SetValue(null, Convert.ChangeType(1_000_000_000, field.FieldType));
        log("autosave put out of reach");
    }

    public static string Describe(MarkArea a) => $"({a.Min.X},{a.Min.Y},{a.Min.Z})-({a.Max.X},{a.Max.Y},{a.Max.Z})";
}
