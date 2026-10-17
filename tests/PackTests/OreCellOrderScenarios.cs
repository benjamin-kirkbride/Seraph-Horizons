using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Atlas.XUnit;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Server;
using Vintagestory.Server;
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
/// <see cref="OreCellsScenarios.A_cell_vein_is_the_same_whatever_order_its_columns_generate_in"/>,
/// deletes the area in one world and generates it again in the other order.
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

    // How long a column may take to generate (the bound was 3000 ticks of real time).
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(100);

    /// <summary>Loads (generating it first if new) one chunk column and waits for it. A column already
    /// loaded answers within the call, so it costs no tick. Generation runs on the chunk thread in wall
    /// time, which a virtual clock's passes outrun (tests/PackTests/README.md), so the wait is bounded
    /// in wall time and works with a clock on or off.</summary>
    public static async Task Load(Atlas.Api.IWorldSession world, ChunkPos c)
    {
        var sapi = (ICoreServerAPI)world.Api;
        bool loaded = false;
        sapi.WorldManager.LoadChunkColumnPriority(c.X, c.Z, new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => loaded = true });
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!loaded && waited.Elapsed < LoadTimeout) await world.Ticks(1);
        if (!loaded) throw new Xunit.Sdk.XunitException($"chunk column {c.X},{c.Z} did not load in {LoadTimeout.TotalSeconds} s");
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
    public static Task Run(Atlas.Api.IWorldSession world, ITestOutputHelper output, string name, Func<ChunkPos, IEnumerable<ChunkPos>, IEnumerable<ChunkPos>> order) =>
        Run(world, output, name, order, "spawn", Seed, Metals.Select(m => (m, (CellPos?)null)));

    /// <summary>The same for given cells (null: the metal's cell at the spawn), as the set <paramref name="set"/>.</summary>
    public static async Task Run(Atlas.Api.IWorldSession world, ITestOutputHelper output, string name, Func<ChunkPos, IEnumerable<ChunkPos>, IEnumerable<ChunkPos>> order,
        string set, int seed, IEnumerable<(string Metal, CellPos? Cell)> cells)
    {
        var sapi = (ICoreServerAPI)world.Api;
        var placement = sapi.ModLoader.GetModSystem<OreSystem>().Placement ?? throw new Xunit.Sdk.XunitException("ore cells are not bound");
        Assert.NotNull(placement.Veins); // the switch is on in this world
        // No player joins: one at the spawn would have the chunk thread generate its view around the
        // spawn in its own order, among the columns loaded here in the order under test, and slow every
        // load. Only these columns generate, one after another; on the virtual clock no pass sleeps,
        // so each is taken up the moment the chunk thread hands it over.
        var spawn = sapi.World.DefaultSpawnPosition.AsBlockPos;
        using var clock = VirtualClock.Start(world);
        var results = new Dictionary<string, Result>();
        foreach (var (metal, given) in cells)
        {
            var cell = given ?? placement.Cells.CellOf(metal, spawn.X, spawn.Z);
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
            // The vein's boulder indicators laid in its area, block by block as they stand in the world.
            var boulders = Boulders(sapi, placement, spot.Chunk);
            results[metal + " boulders"] = new Result(spot.Chunk, vein.Variant, boulders, []);
            output.WriteLine($"{name}: {metal} cell {cell} spot {spot.Index} anchor {spot.Chunk}: {vein.Variant} in {vein.Host}, {ore.Count} ore blocks, "
                + $"{boulders.Count} boulder blocks");
        }
        Assert.Equal(0, placement.MissedReplays);
        string key = $"{set}/{name}";
        Results[key] = results;
        Blocks = sapi.World.Blocks;
        Save(seed, set, name, results, sapi);
        // Worlds booted one after another in one process used to come out unlike: a second world's
        // deposits sat at other depths than the same world's booted in a process of its own (#1012,
        // fixed in OreSystem.Start). The orders are compared across processes, each class run alone
        // in its own `dotnet test` (its world the first in the process) with
        // ORE_CELL_ORDER_COMPARE=1; otherwise (CI's shard, many worlds in one process) each only
        // records.
        var other = Results.Keys.FirstOrDefault(k => k != key && k.StartsWith(set + "/", StringComparison.Ordinal));
        if (other != null)
            output.WriteLine($"{name}: '{other}' ran in this process too; it is compared only from its own process's file (see the code)");
        if (Environment.GetEnvironmentVariable("ORE_CELL_ORDER_COMPARE") != "1")
        {
            output.WriteLine($"{name}: recorded in {FileOf(seed, set, name)}; set ORE_CELL_ORDER_COMPARE=1 and run each order alone to compare");
            return;
        }
        var otherName = name == "anchor first" ? "far first" : "anchor first";
        if (Load(seed, set, otherName, sapi) is not { } fromFile)
        {
            output.WriteLine($"{name}: recorded in {FileOf(seed, set, name)}; '{otherName}' has no file from another process yet, nothing to compare");
            return;
        }
        Compare(output, fromFile, results, otherName, name);
    }

    // Results kept between processes: per metal, anchor, variant and every ore block by code.
    private static string FileOf(int seed, string set, string name) =>
        Path.Combine(Path.GetTempPath(), $"orecellorder-{seed}-{set}-{name.Replace(' ', '-')}.json");

    private sealed record Saved(int Pid, Dictionary<string, SavedMetal> Metals);

    private sealed record SavedMetal(int AnchorX, int AnchorZ, string Variant, List<int[]> Positions, List<string> Codes);

    private static void Save(int seed, string set, string name, Dictionary<string, Result> results, ICoreServerAPI sapi)
    {
        var metals = results.ToDictionary(kv => kv.Key, kv => new SavedMetal(kv.Value.Anchor.X, kv.Value.Anchor.Z, kv.Value.Variant,
            kv.Value.Ore.Keys.Select(k => new[] { k.X, k.Y, k.Z }).ToList(),
            kv.Value.Ore.Values.Select(id => sapi.World.Blocks[id].Code.ToString()).ToList()));
        File.WriteAllText(FileOf(seed, set, name), System.Text.Json.JsonSerializer.Serialize(new Saved(Environment.ProcessId, metals)));
    }

    private static Dictionary<string, Result>? Load(int seed, string set, string name, ICoreServerAPI sapi)
    {
        if (!File.Exists(FileOf(seed, set, name))) return null;
        var saved = System.Text.Json.JsonSerializer.Deserialize<Saved>(File.ReadAllText(FileOf(seed, set, name)));
        if (saved == null || saved.Pid == Environment.ProcessId) return null;
        return saved.Metals.ToDictionary(kv => kv.Key, kv => new Result(new ChunkPos(kv.Value.AnchorX, kv.Value.AnchorZ), kv.Value.Variant,
            kv.Value.Positions.Zip(kv.Value.Codes).ToDictionary(p => (p.First[0], p.First[1], p.First[2]),
                p => sapi.World.GetBlock(new Vintagestory.API.Common.AssetLocation(p.Second))?.BlockId ?? -1),
            []));
    }

    /// <summary>The blocks of every boulder indicator stable veins laid with a site in the columns
    /// within reach of the anchor, as they stand in the world (positions in those columns only).</summary>
    public static Dictionary<(int X, int Y, int Z), int> Boulders(ICoreServerAPI sapi, OreCellPlacement placement, ChunkPos anchor)
    {
        var area = Around(anchor).ToHashSet();
        var blocks = new Dictionary<(int, int, int), int>();
        foreach (var (site, b) in placement.BoulderSites)
        {
            if (!area.Contains(new ChunkPos(site.X >> 5, site.Z >> 5))) continue;
            foreach (var (x, y, z) in StableVeinApply.BoulderBlocks(site.X, site.Y, site.Z, b.MinRadius, b.MaxRadius))
            {
                if (!area.Contains(new ChunkPos(x >> 5, z >> 5)) || y <= 0 || y >= sapi.WorldManager.MapSizeY) continue;
                var chunk = sapi.WorldManager.GetChunk(x >> 5, y >> 5, z >> 5)!;
                chunk.Unpack();
                blocks[(x, y, z)] = chunk.Data.GetBlockIdUnsafe(((y & 31) * 32 + (z & 31)) * 32 + (x & 31));
            }
        }
        return blocks;
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

/// <summary>A seam pocket (IOG's multi-ore fault pocket, <c>hematite</c>): seed 101's iron cell
/// 103,101, whose first spot holds a <c>hematite#1</c> seam in granite.</summary>
public static class OreCellOrderSeam
{
    public const int Seed = 101;
    public static readonly (string, CellPos?)[] Cells = [("iron", new CellPos(103, 101))];
}

[AtlasWorld(Seed = OreCellOrderSeam.Seed, WorldType = "standard")]
public class OreCellOrderSeamAnchorFirst(ITestOutputHelper output) : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 900_000)]
    public Task Seam_vein_generated_anchor_first() =>
        OreCellOrder.Run(World, output, "anchor first", (anchor, cols) => cols.OrderBy(c => OreCellOrder.Ring(anchor, c)).ThenBy(c => c.X).ThenBy(c => c.Z),
            "seam", OreCellOrderSeam.Seed, OreCellOrderSeam.Cells);
}

[AtlasWorld(Seed = OreCellOrderSeam.Seed, WorldType = "standard")]
public class OreCellOrderSeamFarFirst(ITestOutputHelper output) : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 900_000)]
    public Task Seam_vein_generated_from_far_to_near() =>
        OreCellOrder.Run(World, output, "far first", (anchor, cols) => cols.OrderByDescending(c => c.X).ThenByDescending(c => c.Z),
            "seam", OreCellOrderSeam.Seed, OreCellOrderSeam.Cells);
}

/// <summary>One world: an anchor's area generated anchor first, deleted, and generated again from
/// the far corner in; the metal's ore must come out the same block for block. Its world is
/// <see cref="OreCellsScenarios"/>' (the same seed and switches), so it runs on that class's boot, and
/// last (<see cref="RunsLastAttribute"/>): it deletes and regenerates the area around the spawn cell's
/// copper anchor, and <c>Generated_anchors_hold_their_deposit</c> needs the spawn cells unsettled.
/// Settled there already, the cell is as this check's own settling would leave it (only its anchor's
/// column loaded), so the check is the same.</summary>
public partial class OreCellsScenarios
{
    private const int Cleared = 9; // the reach and the halo its columns' generation pulls in

    [AtlasScenario(TimeoutMs = 300_000), RunsLast]
    public async Task A_cell_vein_is_the_same_whatever_order_its_columns_generate_in()
    {
        var sapi = (ICoreServerAPI)World.Api;
        var placement = sapi.ModLoader.GetModSystem<OreSystem>().Placement ?? throw new Xunit.Sdk.XunitException("ore cells are not bound");
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
            // Loaded (and kept) as the settling above leaves it, also when an earlier scenario settled
            // the cell and its column has been unloaded since: Clear deletes loaded columns only.
            await OreCellOrder.Load(World, anchor);
            using var clock = VirtualClock.Start(World);

            await Clear(sapi, anchor);
            foreach (var c in around.OrderBy(c => OreCellOrder.Ring(anchor, c))) await OreCellOrder.Load(World, c);
            var first = OreCellOrder.OreAround(sapi, anchor, metal);
            await Clear(sapi, anchor);
            foreach (var c in around.OrderByDescending(c => c.X).ThenByDescending(c => c.Z)) await OreCellOrder.Load(World, c);
            var second = OreCellOrder.OreAround(sapi, anchor, metal);

            int differ = first.Keys.Union(second.Keys).Count(k => first.GetValueOrDefault(k) != second.GetValueOrDefault(k));
            output.WriteLine($"{metal} cell {cell} anchor {anchor}: {first.Count} then {second.Count} ore blocks; {differ} positions differ");
            Assert.True(differ == 0, $"{metal}'s vein at {anchor} came out differently in two generation orders");
            return;
        }
        throw new Xunit.Sdk.XunitException("no copper, iron or tin deposit placed near the spawn");
    }

    /// <summary>Deletes the loaded columns around the anchor, with the server's chunk loading idle
    /// before and after (#1010).</summary>
    /// <remarks>
    /// <c>DeleteChunkColumn</c> unloads a column on the game thread and queues its deletion for the
    /// chunk database thread, whose <c>deleteChunkColumns</c> despawns the entities of any chunk of it
    /// loaded by then, reading the chunk's <c>Entities</c> without a null check: a chunk that never
    /// held an entity (most below ground) has none, and the thread throws and shuts the server down.
    /// So nothing may load a column between its unload and its deletion. That happened two ways: a
    /// column finished before the clear but not yet handed to the game thread (it lands after the
    /// unload), and the next <see cref="OreCellOrder.Load"/>, queued while the database thread was
    /// past its deletions in the tick, loading the column from the database before the deletion ran.
    /// So the clear waits until nothing is loading or generating, and generating starts again only
    /// once the deletions have been taken off their queue: the thread deletes before it loads, so a
    /// load queued after that runs after them. Only columns loaded whole are deleted, as before: a
    /// halo column part way through its passes (left by the previous generation, waiting on nothing)
    /// stays.
    /// </remarks>
    private async Task Clear(ICoreServerAPI sapi, ChunkPos anchor)
    {
        var loading = ChunkLoading.Of((ServerMain)sapi.World);
        await World.Until(loading.Idle, 3000);
        for (int dx = -Cleared; dx <= Cleared; dx++)
            for (int dz = -Cleared; dz <= Cleared; dz++)
                if (sapi.WorldManager.GetChunk(anchor.X + dx, 0, anchor.Z + dz) != null)
                    sapi.WorldManager.DeleteChunkColumn(anchor.X + dx, anchor.Z + dz);
        await World.Until(loading.Idle, 3000);
    }

    /// <summary>The server's chunk loading queues, internal to the game and read by reflection.</summary>
    private sealed class ChunkLoading
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ServerMain _server;
        private readonly object[] _queues; // load requests, fast and simple loads, deletions: all must be empty
        private readonly IDictionary _generating; // the chunk thread's requests by column, until the game thread loads them
        private readonly PropertyInfo _disposed;

        private ChunkLoading(ServerMain server)
        {
            _server = server;
            _queues = [Field(server, "requestedChunkColumns"), Field(server, "fastChunkQueue"), Field(server, "simpleLoadRequests"), Field(server, "deleteChunkColumns")];
            _generating = (IDictionary)Field(Field(Field(server, "chunkThread"), "requestedChunkColumns"), "elementsByIndex");
            _disposed = typeof(ChunkColumnLoadRequest).GetProperty("Disposed", Any)
                ?? throw new NotSupportedException("ChunkColumnLoadRequest.Disposed is missing: a game update renamed it");
        }

        public static ChunkLoading Of(ServerMain server) => new(server);

        /// <summary>Nothing queued to load or delete, and no column generating: each request the chunk
        /// thread holds is at the pass it was asked for (a halo column part way, or one let go) and none
        /// has finished without the game thread having loaded it yet.</summary>
        public bool Idle()
        {
            lock (Field(_server, "requestedChunkColumnsLock"))
                if (_queues.Any(q => CountOf(q) > 0)) return false;
            foreach (var request in _generating.Values.Cast<ChunkColumnLoadRequest>().ToList())
            {
                if (request.CurrentIncompletePass == EnumWorldGenPass.Done) return false;
                if (!(bool)_disposed.GetValue(request)! && request.CurrentIncompletePass_AsInt < (int)request.GenerateUntilPass) return false;
            }
            return true;
        }

        private static int CountOf(object queue) => queue is ICollection c ? c.Count
            : (int)(queue.GetType().GetProperty("Count", Any)?.GetValue(queue) ?? throw new NotSupportedException($"{queue.GetType().Name} has no Count"));

        private static object Field(object o, string name) =>
            o.GetType().GetField(name, Any)?.GetValue(o)
            ?? throw new NotSupportedException($"{o.GetType().Name}.{name} is missing: a game update renamed it (find it again by decompiling, tests/PackTests/README.md)");
    }
}
