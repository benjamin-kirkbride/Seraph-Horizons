using System.Collections.Concurrent;
using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Stable veins (#946): an ore cell's vein is the same whatever order its chunk columns generate
/// in. Before them it was not (tools/ore-survey/README.md, "Against a full scan"): Interesting Ore
/// Gen's veins read and wrote neighbouring chunks as each was generated, and the cell rule read the
/// try's centre as it stood at each pass. Two worlds of one seed generate the area around the
/// spawn's copper, iron, tin, bismuth and zinc anchors in two orders: <see cref="OreCellOrderAnchorFirst"/>
/// each anchor first and then outwards (as a census or a trader's check does),
/// <see cref="OreCellOrderFarFirst"/> from the far corner in, so the columns around the anchor
/// are generated before it (as a walk towards it does). Whichever class finishes second compares
/// the ore of each metal block for block; run alone, a class only records. A third check,
/// <see cref="OreCellOrderScenarios"/>, deletes the area in one world and generates it again in
/// the other order.
/// </summary>
public static class OreCellOrder
{
    public const int Seed = 424242;
    public const int Reach = 3; // GenDeposits' reach: the columns a vein from the anchor can be in

    public static readonly string[] Metals = ["copper", "iron", "tin", "bismuth", "zinc"];

    // Order name -> metal -> (anchor, variant, ore blocks by position).
    private static readonly ConcurrentDictionary<string, Dictionary<string, Result>> Results = new();
    private static IList<Vintagestory.API.Common.Block>? Blocks;

    public sealed record Result(ChunkPos Anchor, string Variant, Dictionary<(int X, int Y, int Z), int> Ore, int[] AnchorColumn);

    public static async Task Load(Atlas.Api.IWorldSession world, ChunkPos c)
    {
        var sapi = (ICoreServerAPI)world.Api;
        bool loaded = false;
        sapi.WorldManager.LoadChunkColumnPriority(c.X, c.Z, new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => loaded = true });
        await world.Until(() => loaded, 3000);
    }

    public static List<ChunkPos> Around(ChunkPos anchor)
    {
        var list = new List<ChunkPos>();
        for (int dx = -Reach; dx <= Reach; dx++)
            for (int dz = -Reach; dz <= Reach; dz++)
                list.Add(new ChunkPos(anchor.X + dx, anchor.Z + dz));
        return list;
    }

    public static int Ring(ChunkPos a, ChunkPos c) => Math.Max(Math.Abs(c.X - a.X), Math.Abs(c.Z - a.Z));

    /// <summary>The metal's ore blocks in the columns within reach of the anchor, by position.</summary>
    public static Dictionary<(int X, int Y, int Z), int> OreAround(ICoreServerAPI sapi, ChunkPos anchor, string metal)
    {
        var ore = new Dictionary<(int, int, int), int>();
        foreach (var c in Around(anchor))
            for (int cy = 0; cy < sapi.WorldManager.MapSizeY / 32; cy++)
            {
                if (sapi.WorldManager.GetChunk(c.X, cy, c.Z) is not { } chunk) throw new Xunit.Sdk.XunitException($"chunk {c.X},{cy},{c.Z} is not loaded");
                chunk.Unpack();
                for (int i = 0; i < 32 * 32 * 32; i++)
                {
                    int id = chunk.Data.GetBlockIdUnsafe(i);
                    if (id > 0 && sapi.World.Blocks[id]?.Code is { } code && OreMetals.MetalOf(OreMetals.OreOfBlockPath(code.Path)) == metal)
                        ore[(c.X * 32 + (i & 31), cy * 32 + (i >> 10), c.Z * 32 + ((i >> 5) & 31))] = id;
                }
            }
        return ore;
    }

    /// <summary>Settles each metal's cell at the spawn by generating around its active spot in the
    /// order given (the anchor's columns sorted by <paramref name="order"/>), moving on while the
    /// spot fails, then records the ore around the placed anchor.</summary>
    public static async Task Run(Atlas.Api.IWorldSession world, ITestOutputHelper output, string name, Func<ChunkPos, IEnumerable<ChunkPos>, IEnumerable<ChunkPos>> order)
    {
        var sapi = (ICoreServerAPI)world.Api;
        var placement = sapi.ModLoader.GetModSystem<OreSystem>().Placement ?? throw new Xunit.Sdk.XunitException("ore cells are not bound");
        Assert.NotNull(placement.Veins); // the switch is on in this world
        await world.JoinAtSpawn("surveyor");
        var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
        var results = new Dictionary<string, Result>();
        foreach (var metal in Metals)
        {
            var cell = placement.Cells.CellOf(metal, spawn.X, spawn.Z);
            for (int round = 0; round < OreCells.SpotCount; round++)
            {
                var state = placement.StateOf(metal, cell);
                if (state.None || state.Placed) break;
                var anchor = placement.SpotsOf(metal, cell)[state.Active].Chunk;
                foreach (var c in order(anchor, Around(anchor))) await Load(world, c);
            }
            var settled = placement.StateOf(metal, cell);
            if (!settled.Placed)
            {
                output.WriteLine($"{name}: {metal} cell {cell}: no deposit");
                continue;
            }
            var spot = placement.SpotsOf(metal, cell)[settled.Active];
            foreach (var c in order(spot.Chunk, Around(spot.Chunk))) await Load(world, c);
            var vein = placement.Veins!.Get(metal, cell, spot.Index)!;
            var ore = OreAround(sapi, spot.Chunk, metal);
            results[metal] = new Result(spot.Chunk, vein.Variant, ore, Column(sapi, spot.Chunk));
            output.WriteLine($"{name}: {metal} cell {cell} spot {spot.Index} anchor {spot.Chunk}: {vein.Variant} in {vein.Host}, {ore.Count} ore blocks");
        }
        Assert.Equal(0, placement.MissedReplays);
        Results[name] = results;
        Blocks = sapi.World.Blocks;
        Save(name, results, sapi);
        // Worlds booted one after another in one process do not come out alike: a second world's
        // vanilla deposits (cracked rock) differ from the same world booted in a process of its
        // own, so something outlives a server in the process (#946, open). The
        // orders are compared across processes: run each class in its own `dotnet test`.
        var other = Results.Keys.FirstOrDefault(k => k != name);
        if (other != null)
            output.WriteLine($"{name}: '{other}' ran in this process too; worlds booted in one process differ (see the code), so it is compared only from its own process's file");
        var otherName = name == "anchor first" ? "far first" : "anchor first";
        if (Load(otherName, sapi) is not { } fromFile)
        {
            output.WriteLine($"{name}: recorded in {FileOf(name)}; '{otherName}' has no file from another process yet, nothing to compare");
            return;
        }
        Compare(output, fromFile, results, otherName, name);
    }

    // Results kept between processes: per metal, anchor, variant and every ore block by code.
    private static string FileOf(string name) =>
        Path.Combine(Path.GetTempPath(), $"orecellorder-{Seed}-{name.Replace(' ', '-')}.json");

    private sealed record Saved(int Pid, Dictionary<string, SavedMetal> Metals);

    private sealed record SavedMetal(int AnchorX, int AnchorZ, string Variant, List<int[]> Positions, List<string> Codes);

    private static void Save(string name, Dictionary<string, Result> results, ICoreServerAPI sapi)
    {
        var metals = results.ToDictionary(kv => kv.Key, kv => new SavedMetal(kv.Value.Anchor.X, kv.Value.Anchor.Z, kv.Value.Variant,
            kv.Value.Ore.Keys.Select(k => new[] { k.X, k.Y, k.Z }).ToList(),
            kv.Value.Ore.Values.Select(id => sapi.World.Blocks[id].Code.ToString()).ToList()));
        File.WriteAllText(FileOf(name), System.Text.Json.JsonSerializer.Serialize(new Saved(Environment.ProcessId, metals)));
    }

    private static Dictionary<string, Result>? Load(string name, ICoreServerAPI sapi)
    {
        if (!File.Exists(FileOf(name))) return null;
        var saved = System.Text.Json.JsonSerializer.Deserialize<Saved>(File.ReadAllText(FileOf(name)));
        if (saved == null || saved.Pid == Environment.ProcessId) return null;
        return saved.Metals.ToDictionary(kv => kv.Key, kv => new Result(new ChunkPos(kv.Value.AnchorX, kv.Value.AnchorZ), kv.Value.Variant,
            kv.Value.Positions.Zip(kv.Value.Codes).ToDictionary(p => (p.First[0], p.First[1], p.First[2]),
                p => sapi.World.GetBlock(new Vintagestory.API.Common.AssetLocation(p.Second))?.BlockId ?? -1),
            []));
    }

    /// <summary>Every block of a chunk column, bottom up (for telling what else differs).</summary>
    public static int[] Column(ICoreServerAPI sapi, ChunkPos c)
    {
        int chunks = sapi.WorldManager.MapSizeY / 32;
        var all = new int[chunks * 32 * 32 * 32];
        for (int cy = 0; cy < chunks; cy++)
        {
            var chunk = sapi.WorldManager.GetChunk(c.X, cy, c.Z)!;
            chunk.Unpack();
            for (int i = 0; i < 32 * 32 * 32; i++) all[cy * 32768 + i] = chunk.Data.GetBlockIdUnsafe(i);
        }
        return all;
    }

    public static void Compare(ITestOutputHelper output, Dictionary<string, Result> a, Dictionary<string, Result> b, string an, string bn)
    {
        var failures = new List<string>();
        foreach (var metal in a.Keys.Union(b.Keys))
        {
            if (!a.TryGetValue(metal, out var ra) || !b.TryGetValue(metal, out var rb))
            {
                failures.Add($"{metal}: placed in one order only");
                continue;
            }
            var diff = ra.Ore.Keys.Union(rb.Ore.Keys).Where(k => ra.Ore.GetValueOrDefault(k) != rb.Ore.GetValueOrDefault(k)).ToList();
            int differ = diff.Count;
            output.WriteLine($"{metal} ({ra.Variant}): {an} {ra.Ore.Count} / {bn} {rb.Ore.Count} ore blocks, {differ} positions differ"
                + (differ > 0 ? ": by column " + string.Join(", ", diff.GroupBy(k => (k.X >> 5, k.Z >> 5)).OrderByDescending(g => g.Count()).Take(12)
                    .Select(g => $"({g.Key.Item1 - ra.Anchor.X},{g.Key.Item2 - ra.Anchor.Z}) {g.Count()} y{g.Min(k => k.Y)}-{g.Max(k => k.Y)}")) : ""));
            if (ra.Anchor != rb.Anchor || differ > 0) failures.Add($"{metal}: anchors {ra.Anchor} / {rb.Anchor}, {differ} positions differ");
            if (ra.Anchor == rb.Anchor && Blocks is { } blocks && ra.AnchorColumn.Length > 0 && ra.AnchorColumn.Length == rb.AnchorColumn.Length)
            {
                var pairs = new Dictionary<(int, int), int>();
                for (int i = 0; i < ra.AnchorColumn.Length; i++)
                    if (ra.AnchorColumn[i] != rb.AnchorColumn[i]) pairs[(ra.AnchorColumn[i], rb.AnchorColumn[i])] = pairs.GetValueOrDefault((ra.AnchorColumn[i], rb.AnchorColumn[i])) + 1;
                if (pairs.Count > 0)
                    output.WriteLine($"  {metal}'s anchor column, blocks that differ ({an} -> {bn}): " + string.Join(", ",
                        pairs.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{blocks[kv.Key.Item1]?.Code?.Path} -> {blocks[kv.Key.Item2]?.Code?.Path} x{kv.Value}")));
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}

[AtlasWorld(Seed = OreCellOrder.Seed, WorldType = "standard")]
public class OreCellOrderAnchorFirst(ITestOutputHelper output) : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 900_000)]
    public Task Cell_veins_generated_anchor_first() =>
        OreCellOrder.Run(World, output, "anchor first", (anchor, cols) => cols.OrderBy(c => OreCellOrder.Ring(anchor, c)).ThenBy(c => c.X).ThenBy(c => c.Z));
}

[AtlasWorld(Seed = OreCellOrder.Seed, WorldType = "standard")]
public class OreCellOrderFarFirst(ITestOutputHelper output) : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 900_000)]
    public Task Cell_veins_generated_from_far_to_near() =>
        OreCellOrder.Run(World, output, "far first", (anchor, cols) => cols.OrderByDescending(c => c.X).ThenByDescending(c => c.Z));
}

/// <summary>One world: an anchor's area generated anchor first, deleted, and generated again from
/// the far corner in; the metal's ore must come out the same block for block.</summary>
[AtlasWorld(Seed = OreCellOrder.Seed, WorldType = "standard")]
public class OreCellOrderScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private const int Cleared = 9; // the reach and the halo its columns' generation pulls in

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task A_cell_vein_is_the_same_whatever_order_its_columns_generate_in()
    {
        var sapi = (ICoreServerAPI)World.Api;
        var placement = sapi.ModLoader.GetModSystem<OreSystem>().Placement ?? throw new Xunit.Sdk.XunitException("ore cells are not bound");
        await World.JoinAtSpawn("surveyor");
        var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
        foreach (var metal in new[] { "copper", "iron", "tin" })
        {
            var cell = placement.Cells.CellOf(metal, spawn.X, spawn.Z);
            for (int round = 0; round < OreCells.SpotCount; round++)
            {
                var state = placement.StateOf(metal, cell);
                if (state.None || state.Placed) break;
                await OreCellOrder.Load(World, placement.SpotsOf(metal, cell)[state.Active].Chunk);
            }
            var settled = placement.StateOf(metal, cell);
            if (!settled.Placed) continue;
            var anchor = placement.SpotsOf(metal, cell)[settled.Active].Chunk;
            var around = OreCellOrder.Around(anchor);

            Clear(sapi, anchor);
            foreach (var c in around.OrderBy(c => OreCellOrder.Ring(anchor, c))) await OreCellOrder.Load(World, c);
            var first = OreCellOrder.OreAround(sapi, anchor, metal);
            Clear(sapi, anchor);
            foreach (var c in around.OrderByDescending(c => c.X).ThenByDescending(c => c.Z)) await OreCellOrder.Load(World, c);
            var second = OreCellOrder.OreAround(sapi, anchor, metal);

            int differ = first.Keys.Union(second.Keys).Count(k => first.GetValueOrDefault(k) != second.GetValueOrDefault(k));
            output.WriteLine($"{metal} cell {cell} anchor {anchor}: {first.Count} then {second.Count} ore blocks; {differ} positions differ");
            Assert.True(differ == 0, $"{metal}'s vein at {anchor} came out differently in two generation orders");
            return;
        }
        throw new Xunit.Sdk.XunitException("no copper, iron or tin deposit placed near the spawn");
    }

    private static void Clear(ICoreServerAPI sapi, ChunkPos anchor)
    {
        // Only complete columns: the game's deleteChunkColumns crashes its chunk database thread on
        // one still generating (a halo column part way through its passes).
        for (int dx = -Cleared; dx <= Cleared; dx++)
            for (int dz = -Cleared; dz <= Cleared; dz++)
                if (sapi.WorldManager.GetChunk(anchor.X + dx, 0, anchor.Z + dz) != null)
                    sapi.WorldManager.DeleteChunkColumn(anchor.X + dx, anchor.Z + dz);
    }
}
