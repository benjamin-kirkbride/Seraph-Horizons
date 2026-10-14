using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// The rock the game's strata put in a column, before any chunk of it generates (#946): a replay of
/// <c>GenRockStrataNew.genBlockColumn</c> (game 1.22.7, the Terrain pass) with the generator's own
/// strata (<c>worldgen/rockstrata.json</c> as the geology mods patch it), its noise layers and its
/// distortion noise, read from the live generator, and the geologic province map rebuilt from
/// <c>GenMaps</c>' province layer. Everything there is a function of the seed but one thing: the
/// column's surface height (<c>WorldGenTerrainHeightMap</c>), which the strata hang from. Top-down
/// strata (sedimentary, volcanic, the upper metamorphics) lie under the surface, bottom-up ones
/// (the igneous and lower metamorphic rocks) stack from y 1 and do not depend on it, and the default
/// rock (granite) is what neither reaches. So the caller passes the height it assumes.
/// <para>Nothing here writes to the game's generators: the province layer's own noise object keeps a
/// position seed between calls (<c>NoiseBase</c>), so its lookup is redone here from its seed, and the
/// rest (simplex noise, the strata layers) is read-only. Thread-safe.</para>
/// </summary>
public sealed class RockStrataPrediction
{
    private readonly RockStratum[] _strata;
    private readonly MapLayerBase[] _noises;
    private readonly SimplexNoise _distortX, _distortZ;
    private readonly int _defaultRock;
    private readonly int _regionChunks;
    private readonly int _strataCells;
    private readonly int _seaLevel;
    private readonly GeologicProvinceVariant[] _provinces;
    private readonly NormalizedSimplexNoise _provinceWobbleX, _provinceWobbleZ;
    private readonly float _provinceWobble;
    private readonly long _provinceSeed;
    private readonly int _provinceWeightSum;
    private readonly int _provinceCells;
    private readonly object _lock = new();
    private readonly Dictionary<long, LerpedWeightedIndex2DMap> _provinceMaps = new();
    private readonly Dictionary<long, int[]> _cells = new();

    private RockStrataPrediction(RockStratum[] strata, MapLayerBase[] noises, SimplexNoise dx, SimplexNoise dz, int defaultRock,
        int regionSize, GeologicProvinceVariant[] provinces, NormalizedSimplexNoise wx, NormalizedSimplexNoise wz, float wobble, long provinceSeed)
    {
        _strata = strata;
        _noises = noises;
        _distortX = dx;
        _distortZ = dz;
        _defaultRock = defaultRock;
        _regionChunks = regionSize / Vintagestory.API.Config.GlobalConstants.ChunkSize;
        _strataCells = regionSize / TerraGenConfig.rockStrataScale;
        _seaLevel = TerraGenConfig.seaLevel;
        _provinces = provinces;
        _provinceWobbleX = wx;
        _provinceWobbleZ = wz;
        _provinceWobble = wobble;
        _provinceSeed = provinceSeed;
        _provinceWeightSum = provinces.Sum(p => p.Weight);
        _provinceCells = regionSize / TerraGenConfig.geoProvMapScale;
    }

    /// <summary>The block ids of the world's strata and of its default rock.</summary>
    public IEnumerable<int> RockIds => _strata.Select(s => s.BlockId).Append(_defaultRock);

    /// <summary>A prediction bound to the running world's generators, or null with why (a generator
    /// not loaded yet, or one whose fields changed).</summary>
    public static RockStrataPrediction? TryCreate(ICoreServerAPI api, out string? why)
    {
        why = null;
        try
        {
            var gen = api.ModLoader.GetModSystem<GenRockStrataNew>();
            var maps = api.ModLoader.GetModSystem<GenMaps>();
            if (gen == null || maps == null) return Fail("GenRockStrataNew or GenMaps is not loaded", out why);
            var config = Field(gen, "strata") as RockStrataConfig;
            var noises = Field(gen, "strataNoises") as MapLayerBase[];
            var dx = Field(gen, "distort2dx") as SimplexNoise;
            var dz = Field(gen, "distort2dz") as SimplexNoise;
            if (config?.Variants is not { Length: > 0 } strata || noises == null || noises.Length != strata.Length || dx == null || dz == null)
                return Fail("the rock strata generator is not initialised (or its fields changed)", out why);
            var layer = maps.geologicprovinceGen;
            if (layer == null || layer.GetType().Name != "MapLayerGeoProvince")
                return Fail("GenMaps' geologic province layer is missing", out why);
            var wx = Field(layer, "noisegenX") as NormalizedSimplexNoise;
            var wz = Field(layer, "noisegenY") as NormalizedSimplexNoise;
            var wobble = Field(layer, "wobbleIntensity");
            var noise = Field(layer, "noiseGeoProvince");
            var seed = noise == null ? null : Field(noise, "mapGenSeed");
            if (wx == null || wz == null || wobble is not float w || seed is not long s || noise is not NoiseGeoProvince)
                return Fail("the geologic province layer's fields changed", out why);
            if (NoiseGeoProvince.provinces?.Variants is not { Length: > 0 } provinces || provinces.Any(p => p.RockStrataIndexed == null))
                return Fail("the geologic provinces are not loaded", out why);
            return new RockStrataPrediction(strata, noises, dx, dz, gen.rockBlockId, api.WorldManager.RegionSize, provinces, wx, wz, w, s);
        }
        catch (Exception e)
        {
            return Fail(e.Message, out why);
        }
    }

    private static RockStrataPrediction? Fail(string message, out string? why)
    {
        why = message;
        return null;
    }

    private static object? Field(object o, string name) =>
        AccessTools.Field(o.GetType(), name) is FieldInfo f ? f.GetValue(o) : null;

    /// <summary>
    /// Fills <paramref name="rock"/>[y] for y 0 to <paramref name="surface"/> with the block id the
    /// strata leave at block column (<paramref name="x"/>, <paramref name="z"/>) if its surface is at
    /// <paramref name="surface"/>: <c>genBlockColumn</c> step for step (y 0 is the mantle, left 0).
    /// </summary>
    public void Column(int x, int z, int surface, int[] rock)
    {
        int chunkX = x >> 5, chunkZ = z >> 5, lx = x & 31, lz = z & 31;
        int regionX = chunkX / _regionChunks, regionZ = chunkZ / _regionChunks;
        int rdx = chunkX % _regionChunks, rdz = chunkZ % _regionChunks;
        float lerpMapInv = 1f / TerraGenConfig.geoProvMapScale;
        float chunkInRegionX = rdx * lerpMapInv * 32f;
        float chunkInRegionZ = rdz * lerpMapInv * 32f;

        var groupMax = new float[4];
        var groupCurrent = new int[4];
        var weights = new float[_provinces.Length];
        ProvinceMap(regionX, regionZ).WeightsAt(chunkInRegionX + lx * lerpMapInv, chunkInRegionZ + lz * lerpMapInv, weights);
        for (int i = 0; i < weights.Length; i++)
        {
            float w = weights[i];
            if (w == 0f) continue;
            var indexed = _provinces[i].RockStrataIndexed;
            for (int g = 0; g < 4; g++) groupMax[g] += indexed[g].ScaledMaxThickness * w;
        }
        float distortX = (float)_distortX.Noise(x, z);
        float distortZ = (float)_distortZ.Noise(x, z);
        float thicknessScale = GameMath.Clamp((distortX + distortZ) / 30f, 0.9f, 1.1f);
        float cellsPerChunk = (float)_strataCells / _regionChunks;

        int ylo = 1, yhi = surface;
        Array.Fill(rock, _defaultRock, 0, Math.Min(rock.Length, surface + 1));
        if (rock.Length > 0) rock[0] = 0;
        int index = -1;
        RockStratum? stratum = null;
        int group = 0;
        float left = 0f;
        while (ylo <= yhi)
        {
            if (--left <= 0f)
            {
                index++;
                if (index >= _strata.Length) break;
                stratum = _strata[index];
                group = (int)stratum.RockGroup;
                float room = groupMax[group] * thicknessScale - groupCurrent[group];
                float sx = Math.Max(rdx * cellsPerChunk + cellsPerChunk * (lx + distortX) / 32f, -1.499f);
                float sz = Math.Max(rdz * cellsPerChunk + cellsPerChunk * (lz + distortZ) / 32f, -1.499f);
                left = Math.Min(room, Lerped(index, regionX, regionZ, sx, sz));
                if (stratum.RockGroup == EnumRockGroup.Sedimentary)
                    left -= Math.Max(0, yhi - _seaLevel) * 0.5f;
                if (left < 2f)
                {
                    left = -1f;
                    continue;
                }
                if (stratum.BlockId == _defaultRock)
                {
                    int n = (int)left;
                    groupCurrent[group] += n;
                    if (stratum.GenDir == EnumStratumGenDir.BottomUp) ylo += n;
                    else yhi -= n;
                    continue;
                }
            }
            groupCurrent[group]++;
            if (stratum!.GenDir == EnumStratumGenDir.BottomUp)
            {
                if (ylo < rock.Length) rock[ylo] = stratum.BlockId;
                ylo++;
            }
            else
            {
                if (yhi < rock.Length) rock[yhi] = stratum.BlockId;
                yhi--;
            }
        }
    }

    // IntDataMap2D.GetIntLerpedCorrectly on the region's strata map, whose cell i is the layer's
    // value at regionX * cells + i - padding (GenRockStrataNew.OnMapRegionGen).
    private float Lerped(int stratum, int regionX, int regionZ, float x, float z)
    {
        int ix = (int)Math.Floor(x - 0.5f), iz = (int)Math.Floor(z - 0.5f);
        float fx = x - (ix + 0.5f), fz = z - (iz + 0.5f);
        int gx = regionX * _strataCells + ix, gz = regionZ * _strataCells + iz;
        return GameMath.BiLerp(Cell(gx, gz)[stratum], Cell(gx + 1, gz)[stratum], Cell(gx, gz + 1)[stratum], Cell(gx + 1, gz + 1)[stratum], fx, fz);
    }

    private int[] Cell(int gx, int gz)
    {
        long key = ((long)gx << 32) | (uint)gz;
        lock (_lock)
        {
            if (_cells.TryGetValue(key, out var values)) return values;
        }
        var computed = new int[_noises.Length];
        for (int i = 0; i < _noises.Length; i++) computed[i] = _noises[i].GenLayer(gx, gz, 1, 1)[0];
        lock (_lock)
        {
            if (_cells.Count > 1 << 20) _cells.Clear();
            _cells[key] = computed;
        }
        return computed;
    }

    // GenMaps.OnMapRegionGen's province map for a region, smoothed as GenRockStrataNew smooths it.
    private LerpedWeightedIndex2DMap ProvinceMap(int regionX, int regionZ)
    {
        long key = ((long)regionX << 32) | (uint)regionZ;
        lock (_lock)
        {
            if (_provinceMaps.TryGetValue(key, out var map)) return map;
        }
        int pad = TerraGenConfig.geoProvMapPadding;
        int size = _provinceCells + 2 * pad;
        int x0 = regionX * _provinceCells - pad, z0 = regionZ * _provinceCells - pad;
        var data = new int[size * size];
        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                // MapLayerGeoProvince.GenLayer
                int wx = (int)(_provinceWobble * _provinceWobbleX.Noise(x0 + i, z0 + j));
                int wz = (int)(_provinceWobble * _provinceWobbleZ.Noise(x0 + i, z0 + j));
                data[j * size + i] = ProvinceAt((x0 + i + wx) / TerraGenConfig.geoProvMapScale, (z0 + j + wz) / TerraGenConfig.geoProvMapScale);
            }
        }
        var built = new LerpedWeightedIndex2DMap(data, size, TerraGenConfig.geoProvSmoothingRadius, pad, pad);
        lock (_lock)
        {
            if (_provinceMaps.Count > 4096) _provinceMaps.Clear();
            _provinceMaps[key] = built;
        }
        return built;
    }

    // NoiseGeoProvince.GetProvinceIndexAt with NoiseBase's position seed kept local.
    private int ProvinceAt(int xpos, int zpos)
    {
        const long mul = 6364136223846793005L, add = 1442695040888963407L;
        long seed = _provinceSeed;
        unchecked
        {
            seed *= seed * mul + add;
            seed += xpos;
            seed *= seed * mul + add;
            seed += zpos;
            seed *= seed * mul + add;
            seed += xpos;
            seed *= seed * mul + add;
            seed += zpos;
        }
        int pick = (int)((seed >> 24) % _provinceWeightSum);
        if (pick < 0) pick += _provinceWeightSum;
        int i;
        for (i = 0; i < _provinces.Length; i++)
        {
            pick -= _provinces[i].Weight;
            if (pick <= 0) return _provinces[i].Index;
        }
        return _provinces[^1].Index;
    }
}

/// <summary>
/// How much of a district zone's country rock is its host rock (#946), from the predicted strata
/// (<see cref="RockStrataPrediction"/>). The surface height a column's strata hang from is not
/// known before it generates, so each column is predicted at every height of
/// <c>surfaceAboveSeaLevel</c> (<c>config/ore-districts.json</c>: the 10th, 30th, 50th, 70th and
/// 90th percentile of the pack's terrain) and the shares averaged; above that height a column holds
/// no rock.
/// </summary>
public sealed class HostRock(RockStrataPrediction strata, int hostId, IReadOnlyList<int> surfaceAboveSeaLevel, int seaLevel)
{
    private const int Slab = 8;

    /// <summary>
    /// The share of an ore shoot's country rock that is host rock, at each height from 0 to sea level
    /// (above it 0: no band lies there), from sample points of its plan with their weights
    /// (<see cref="Core.ShootShape.Samples"/>), each shifted across by the shoot's dip as it goes down
    /// (IOG's <c>OreShootSampler</c>: <c>dip × (top − y)</c> along the fault's across direction), a
    /// column for every eight layers. The fault rock among them is predicted as the country rock it
    /// replaces, a small part of the whole.
    /// </summary>
    public double[] ShootShare(IReadOnlyList<(double X, double Z, double Weight)> points, double acrossX, double acrossZ, float dip, int top)
    {
        var share = new double[seaLevel + 1];
        if (points.Count == 0) return share;
        double total = points.Sum(p => p.Weight) * surfaceAboveSeaLevel.Count;
        var rock = new int[seaLevel + Math.Max(0, surfaceAboveSeaLevel.Max()) + 2];
        for (int y0 = 1; y0 <= seaLevel; y0 += Slab)
        {
            int y1 = Math.Min(seaLevel, y0 + Slab - 1);
            double shift = dip * (top - (y0 + y1) / 2.0);
            var hits = new double[y1 - y0 + 1];
            foreach (var (px, pz, w) in points)
            {
                int x = (int)Math.Floor(px + acrossX * shift), z = (int)Math.Floor(pz + acrossZ * shift);
                foreach (int above in surfaceAboveSeaLevel)
                {
                    int surface = seaLevel + above;
                    strata.Column(x, z, surface, rock);
                    for (int y = y0; y <= y1 && y <= surface; y++)
                        if (rock[y] == hostId) hits[y - y0] += w;
                }
            }
            for (int y = y0; y <= y1; y++) share[y] = hits[y - y0] / total;
        }
        return share;
    }

    /// <summary>The share of one column's layers <paramref name="ylo"/> to <paramref name="yhi"/>
    /// that is host rock (a horsetail lens, at its centre).</summary>
    public double Share(double x, double z, int ylo, int yhi)
    {
        ylo = Math.Max(1, ylo);
        if (yhi < ylo) return 0;
        var rock = new int[Math.Max(yhi, seaLevel + Math.Max(0, surfaceAboveSeaLevel.Max())) + 2];
        double hits = 0;
        foreach (int above in surfaceAboveSeaLevel)
        {
            int surface = seaLevel + above;
            strata.Column((int)Math.Floor(x), (int)Math.Floor(z), surface, rock);
            for (int y = ylo; y <= yhi && y <= surface; y++)
                if (rock[y] == hostId) hits++;
        }
        return hits / ((yhi - ylo + 1) * surfaceAboveSeaLevel.Count);
    }
}
