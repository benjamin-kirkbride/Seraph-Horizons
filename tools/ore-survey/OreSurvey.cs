using System.Diagnostics;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SeraphHorizons.OreSurvey;

/// <summary>
/// Generates ORE_SURVEY_SIZE x ORE_SURVEY_SIZE chunk columns around the map centre, tile by
/// tile, counts every block in them, and records each ore and rich gravel block by 8-block cell
/// with the shallowest depth below the worldgen surface seen in that cell. With
/// ORE_SURVEY_MODE=dump it writes the deposit definitions, ore drops and metal units instead.
/// Either way it stops the server when done. Output formats are in README.md.
/// </summary>
public class OreSurveySystem : ModSystem
{
    // Chunk columns per tile edge. A tile is loaded, counted and unloaded before the next one
    // is asked for, which keeps memory flat however large the area.
    const int Tile = 8;

    ICoreServerAPI sapi = null!;
    long[] counts = [];
    // A per-id sum of y for a mean height; per-y histograms for every id would be large.
    long[] ySum = [];
    bool[] recorded = [];
    // (blockId, cell x, z, y) -> blocks, shallowest depth below the worldgen surface
    readonly Dictionary<long, (int n, int minDepth)> cells = new();
    int size, tiles, next, originX, originZ;
    bool busy;
    string outPath = "", logPath = "";
    readonly Stopwatch clock = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        outPath = Environment.GetEnvironmentVariable("ORE_SURVEY_OUT") ?? "orescan.json";
        logPath = outPath + ".log";
        size = int.Parse(Environment.GetEnvironmentVariable("ORE_SURVEY_SIZE") ?? "64");
        if (size <= 0 || size % Tile != 0)
            throw new ArgumentException($"ORE_SURVEY_SIZE must be a positive multiple of {Tile}, got {size}");
        tiles = size / Tile;
        File.WriteAllText(logPath, "");
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            counts = new long[api.World.Blocks.Count];
            ySum = new long[api.World.Blocks.Count];
            recorded = api.World.Blocks.Select(b => b?.Code != null && Recorded(b)).ToArray();
            originX = api.WorldManager.MapSizeX / 32 / 2 - size / 2;
            originZ = api.WorldManager.MapSizeZ / 32 / 2 - size / 2;
            Log($"start seed={api.World.Seed} sealevel={api.World.SeaLevel} mapY={api.WorldManager.MapSizeY} origin chunk=({originX},{originZ}) size={size}x{size} chunks");
            if (Environment.GetEnvironmentVariable("ORE_SURVEY_MODE") == "dump") { Dump(); return; }
            clock.Start();
            api.Event.RegisterGameTickListener(_ => Step(), 20);
        });
    }

    // Interesting Ore Gen's saltpeter is not a BlockOre and has no ore- prefix; loose ores are
    // the surface signs, kept so their spread can be checked too.
    static bool Recorded(Block b) =>
        b.Code.Path.StartsWith("ore-") || b.Class == "BlockOre" || b.Code.Path.Contains("saltpeterore")
        || b.Code.Path.StartsWith("looseores") || b.Code.Path.StartsWith("richgravel-");

    void Dump()
    {
        static bool Oreish(CollectibleObject c) => c.Code != null && (c.Code.Path.StartsWith("ore-") || c.Code.Path.StartsWith("nugget-") || c.Code.Path.StartsWith("crystalizedore-") || c.Code.Path.Contains("saltpeterore"));
        var blocks = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var b in sapi.World.Blocks)
        {
            if (b?.Code == null || !(Oreish(b) || b.Class == "BlockOre")) continue;
            blocks[b.Code.ToString()] = (b.Drops ?? []).Select(d => new { code = d.Type + " " + d.Code, avg = d.Quantity?.avg ?? 0, last = d.LastDrop }).ToArray();
        }
        var items = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var c in sapi.World.Collectibles)
        {
            if (c?.Code == null || !Oreish(c)) continue;
            var cp = c.CombustibleProps;
            items[c.ItemClass + " " + c.Code] = new
            {
                cls = c.Class,
                metalUnits = c.Attributes?["metalUnits"].Exists == true ? c.Attributes["metalUnits"].AsFloat() : (float?)null,
                smeltsTo = cp?.SmeltedStack?.Code?.ToString(),
                smeltsToSize = cp?.SmeltedStack?.ResolvedItemstack?.StackSize,
                smeltedRatio = cp?.SmeltedRatio
            };
        }
        object Dep(DepositVariant d) => new { d.Code, d.fromFile, d.TriesPerChunk, d.Generator, d.WithOreMap, d.OreMapScale, d.OreMapContrast, d.OreMapSub, attributes = d.Attributes?.ToString(), children = d.ChildDeposits?.Select(Dep).ToArray() };
        var deposits = sapi.ModLoader.GetModSystem<GenDeposits>().Deposits.Select(Dep).ToArray();
        File.WriteAllText(outPath, JsonSerializer.Serialize(new { blocks, items, deposits }, new JsonSerializerOptions { WriteIndented = true }));
        Log("dump done");
        sapi.Server.ShutDown();
    }

    void Log(string line)
    {
        File.AppendAllText(logPath, line + "\n");
        sapi.Logger.Notification("[oresurvey] " + line);
    }

    void Step()
    {
        if (busy) return;
        if (next >= tiles * tiles) { busy = true; Finish(); return; }
        busy = true;
        int tx = next % tiles, tz = next / tiles;
        int x1 = originX + tx * Tile, z1 = originZ + tz * Tile;
        int x2 = x1 + Tile - 1, z2 = z1 + Tile - 1;
        sapi.WorldManager.LoadChunkColumnPriority(x1, z1, x2, z2, new ChunkLoadOptions
        {
            KeepLoaded = true,
            OnLoaded = () =>
            {
                Count(x1, z1, x2, z2);
                for (int cx = x1; cx <= x2; cx++)
                    for (int cz = z1; cz <= z2; cz++)
                        sapi.WorldManager.UnloadChunkColumn(cx, cz);
                next++;
                if (next % tiles == 0) Log($"row {next / tiles}/{tiles} elapsed={clock.Elapsed.TotalSeconds:F0}s");
                busy = false;
            }
        });
    }

    void Count(int x1, int z1, int x2, int z2)
    {
        int chunksY = sapi.WorldManager.MapSizeY / 32;
        for (int cx = x1; cx <= x2; cx++)
            for (int cz = z1; cz <= z2; cz++)
                for (int cy = 0; cy < chunksY; cy++)
                {
                    var chunk = sapi.WorldManager.GetChunk(cx, cy, cz)
                        ?? throw new InvalidOperationException($"chunk {cx},{cy},{cz} not loaded");
                    chunk.Unpack();
                    var data = chunk.Data;
                    var hm = chunk.MapChunk.WorldGenTerrainHeightMap;
                    for (int i = 0; i < 32 * 32 * 32; i++)
                    {
                        int id = data[i];
                        if (id == 0) continue;
                        int y = cy * 32 + (i >> 10);
                        counts[id]++;
                        ySum[id] += y;
                        if (!recorded[id]) continue;
                        int lx = i & 31, lz = (i >> 5) & 31;
                        // id: bits 40+, cell x: 26..39, cell z: 12..25, cell y: 0..11
                        long key = ((long)id << 40) | ((long)(((cx - originX) * 32 + lx) >> 3) << 26) | ((long)(((cz - originZ) * 32 + lz) >> 3) << 12) | (long)(y >> 3);
                        int depth = hm[lz * 32 + lx] - y;
                        cells[key] = cells.TryGetValue(key, out var c) ? (c.n + 1, Math.Min(c.minDepth, depth)) : (1, depth);
                    }
                }
    }

    void Finish()
    {
        var blocks = new SortedDictionary<string, object>(StringComparer.Ordinal);
        for (int id = 0; id < counts.Length; id++)
        {
            if (counts[id] == 0) continue;
            var b = sapi.World.Blocks[id];
            blocks[b.Code.ToString()] = new { count = counts[id], meanY = Math.Round((double)ySum[id] / counts[id], 1), cls = b.Class };
        }
        var doc = new
        {
            seed = sapi.World.Seed,
            seaLevel = sapi.World.SeaLevel,
            mapSizeY = sapi.WorldManager.MapSizeY,
            chunkColumns = size * size,
            areaKm2 = size * 32.0 * size * 32.0 / 1e6,
            seconds = clock.Elapsed.TotalSeconds,
            blocks
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        // Same columns as the scratch scan's, so its results stay readable: code, cell x, cell z,
        // cell y, blocks in the cell, shallowest depth below the worldgen surface.
        using (var w = new StreamWriter(outPath + ".cells.csv"))
            foreach (var (key, c) in cells)
                w.WriteLine($"{sapi.World.Blocks[(int)(key >> 40)].Code},{(key >> 26) & 0x3fff},{(key >> 12) & 0x3fff},{key & 0xfff},{c.n},{c.minDepth}");
        Log($"done in {clock.Elapsed.TotalSeconds:F0}s");
        sapi.Server.ShutDown();
    }
}
