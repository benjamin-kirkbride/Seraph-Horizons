using System.Collections;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Ore;

/// <summary>One hydrothermal district tile around a position (<c>/sh ore districts</c>).</summary>
public sealed record DistrictTile(int TileX, int TileZ, int TileSize, bool Rolled, int CentreX, int CentreZ,
    bool Generated, string? Config, double Radius, int MajorFaults, int MinorFaults, int Horsetails, int OreZones);

/// <summary>
/// Interesting Ore Gen's hydrothermal districts as admins see them (#458). IOG 2.3.8
/// (<c>HydrothermalDistrictSystem</c>) tiles the world in squares of its first config's
/// <c>minDistanceBetweenDistricts</c> (7 km with RarerDistricts) and rolls each tile once, with
/// <c>LCGRandom.InitPositionSeed(seed ^ tileX * 1000033, tileZ * 998244353)</c>: a district if the
/// first draw is under 0.4, centred at the next two draws times the tile size. That roll is
/// reproduced here from the seed, so a tile's district is known before any chunk of it generates;
/// a district IOG has built (its <c>_activeDistricts</c>, filled lazily as chunks within a tile of it
/// generate) adds its config, radius, faults and ore zones, read by reflection. Built this run only:
/// IOG keeps them in memory and rebuilds them as chunks generate after a restart.
/// </summary>
public static class OreDistricts
{
    public const string SystemTypeName = "InterestingOreGen.Generators.HydrothermalDistrictSystem";

    /// <summary>IOG's hard-coded chance of a district per tile.</summary>
    public const double Chance = 0.4;

    private static ModSystem? System(ICoreAPI api) =>
        api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == SystemTypeName);

    /// <summary>Why districts can't be read, or null.</summary>
    public static string? Unsupported(ICoreAPI api)
    {
        var system = System(api);
        if (system == null) return "Interesting Ore Gen's hydrothermal districts are not loaded";
        var t = system.GetType();
        return AccessTools.Field(t, "_configs") == null || AccessTools.Field(t, "_activeDistricts") == null || AccessTools.Field(t, "_worldSeed") == null
            ? "HydrothermalDistrictSystem's _configs, _activeDistricts or _worldSeed is gone"
            : null;
    }

    /// <summary>The tile size in blocks, as IOG reads it (8000 with no config).</summary>
    public static int TileSize(ICoreAPI api)
    {
        var system = System(api);
        if (system == null) return 8000;
        if (AccessTools.Field(system.GetType(), "_configs")?.GetValue(system) is IList { Count: > 0 } configs
            && Traverse.Create(configs[0]).Field("MinDistanceBetweenDistricts").GetValue() is int size and > 0)
            return size;
        return 8000;
    }

    /// <summary>Every tile a circle of <paramref name="radius"/> around (x, z) reaches.</summary>
    public static List<DistrictTile> Around(ICoreAPI api, int x, int z, int radius)
    {
        var system = System(api) ?? throw new InvalidOperationException("no hydrothermal district system");
        int seed = (int)(AccessTools.Field(system.GetType(), "_worldSeed")!.GetValue(system) ?? 0);
        int size = TileSize(api);
        var active = new Dictionary<long, object>();
        var lockObject = AccessTools.Field(system.GetType(), "_districtLock")?.GetValue(system) ?? new object();
        lock (lockObject)
            if (AccessTools.Field(system.GetType(), "_activeDistricts")!.GetValue(system) is IDictionary districts)
                foreach (DictionaryEntry e in districts)
                    if (e.Key is long k && e.Value != null) active[k] = e.Value;
        var tiles = new List<DistrictTile>();
        foreach (var cell in Core.CellSearch.Within(size, x, z, radius))
        {
            var (rolled, cx, cz) = Roll(seed, cell.X, cell.Z, size);
            long key = ((long)cell.X << 32) | (uint)cell.Z;
            if (!active.TryGetValue(key, out var d))
            {
                tiles.Add(new DistrictTile(cell.X, cell.Z, size, rolled, cx, cz, false, null, 0, 0, 0, 0, 0));
                continue;
            }
            var t = Traverse.Create(d);
            var centre = t.Property("Centre").GetValue() as Vec2i;
            tiles.Add(new DistrictTile(cell.X, cell.Z, size, true, centre?.X ?? cx, centre?.Y ?? cz, true,
                Traverse.Create(t.Property("Config").GetValue()).Field("Code").GetValue() as string,
                Convert.ToDouble(t.Property("Radius").GetValue() ?? 0f),
                Count(t.Property("MajorFaults").GetValue()), Count(t.Property("MinorFaults").GetValue()),
                Count(t.Property("HorsetailSplays").GetValue()), Count(t.Property("OreZones").GetValue())));
        }
        return tiles.OrderBy(t => Dist(t.CentreX, t.CentreZ, x, z)).ToList();
    }

    /// <summary>IOG's roll for a tile (<c>TryGenerateDistrictInTile</c>), int arithmetic and all.</summary>
    public static (bool Rolled, int X, int Z) Roll(int worldSeed, int tileX, int tileZ, int tileSize)
    {
        var rand = new LCGRandom();
        unchecked
        {
            rand.InitPositionSeed(worldSeed ^ (tileX * 1000033), tileZ * 998244353);
        }
        bool rolled = rand.NextDouble() < Chance;
        double ox = rand.NextDouble() * tileSize, oz = rand.NextDouble() * tileSize;
        return (rolled, (int)(tileX * (double)tileSize + ox), (int)(tileZ * (double)tileSize + oz));
    }

    private static int Count(object? list) => list is ICollection c ? c.Count : 0;

    private static double Dist(int x, int z, int fx, int fz) => Math.Sqrt((double)(x - fx) * (x - fx) + (double)(z - fz) * (z - fz));
}
