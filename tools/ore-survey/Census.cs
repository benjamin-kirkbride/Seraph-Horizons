using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SeraphHorizons.OreSurvey.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.OreSurvey;

/// <summary>
/// The census (ORE_SURVEY_MODE=census, README "Census"): measures each deposit of an area by a
/// flood over chunk columns (<see cref="ColumnFlood"/>) instead of generating the whole area.
/// Two kinds of deposit: every ore cell of a sized metal that the area touches (the pack's
/// <c>OreCellPlacement</c>, read by reflection: its spots and cell book, followed from spot to
/// spot as <c>DepositService.Verify</c> does), and every kept vein (ore shoot or ladder vein) of a
/// sized metal in the hydrothermal districts whose centre lies in the area (Interesting Ore Gen's
/// <c>HydrothermalDistrictSystem</c>, its districts built from the seed up front, after the pack's
/// <c>DistrictVeinSizes</c> has cut them). Each flood starts at the deposit's anchor column and
/// counts only that metal's ore. Writes ORE_SURVEY_OUT (records), .cells.csv (the deposit id, then
/// the scan's six columns, cells absolute) and .log, then stops the server.
/// </summary>
public sealed class Census
{
    public const int Margin = 2;
    public const int Cap = 16;
    const int ChunkSize = 32;

    readonly ICoreServerAPI sapi;
    readonly Action<string> log;
    readonly string outPath;
    readonly int originX, originZ, size; // area: chunk columns
    readonly int parallel;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Action<IEnumerable<string>> onDone;
    // ORE_CENSUS_REVERSE=1: each batch asked for in reverse order (furthest first), to show whether
    // the order columns generate in changes the ore.
    readonly bool reverse = Environment.GetEnvironmentVariable("ORE_CENSUS_REVERSE") == "1";
    // ORE_CENSUS_LEAD=dx,dz: before each flood, the column at that offset from the anchor is
    // generated first (an experiment on generation order).
    readonly Column? lead = Environment.GetEnvironmentVariable("ORE_CENSUS_LEAD") is { Length: > 0 } l
        ? new Column(int.Parse(l.Split(',')[0]), int.Parse(l.Split(',')[1])) : null;

    string?[] metalOfId = [];
    bool[] rockId = [];
    readonly Dictionary<Column, ColumnScan> scans = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<Column, ColumnScan> earlyScans = new();
    // ORE_CENSUS_KINDS=cells or districts: census only ore cells or only district veins (default both).
    readonly string kinds = Environment.GetEnvironmentVariable("ORE_CENSUS_KINDS") ?? "";
    // ORE_CENSUS_EARLY=0: only columns asked for and complete are read (every flood column asked for).
    readonly bool early = Environment.GetEnvironmentVariable("ORE_CENSUS_EARLY") != "0";
    // How far around a column asked for its neighbours are expected to pass Vegetation.
    readonly int cover = int.Parse(Environment.GetEnvironmentVariable("ORE_CENSUS_COVER") ?? "3");
    int columnsEarly, earlyErrors, earlyChecked, earlyMismatch, columnsCompleted;
    readonly HashSet<Column> loaded = new();
    readonly Dictionary<Column, int> holds = new();
    readonly Dictionary<Column, List<Action>> waiting = new();
    readonly Queue<Job> queue = new();
    readonly List<Job> active = new();
    readonly List<Dictionary<string, object?>> records = new();
    StreamWriter? cellsOut;
    int nextId, done;
    double lastProgress;
    bool finished;

    // The pack's ore cells, by reflection.
    object? placement;
    MethodInfo? spotsOf, stateOf, cellSizeOf;
    ConstructorInfo? cellPosCtor;

    sealed class ColumnScan
    {
        // metal -> (block id, cell x, cell z, cell y) -> blocks, shallowest depth
        public readonly Dictionary<string, Dictionary<(int Id, int X, int Z, int Y), (int N, int MinDepth)>> Cells = new();
        // rock block id -> blocks per 8-block slab of height, for a district vein's country rock
        public readonly Dictionary<int, int[]> Rocks = new();
        public int Missing;
        public bool Has(string metal) => Cells.ContainsKey(metal);

        public bool SameOre(ColumnScan o) =>
            Cells.Count == o.Cells.Count && Cells.All(kv => o.Cells.TryGetValue(kv.Key, out var other)
                && kv.Value.Count == other.Count && kv.Value.All(c => other.TryGetValue(c.Key, out var v) && v.N == c.Value.N));
    }

    sealed class Job
    {
        public int Id;
        public string Kind = "cell";
        public string Metal = "";
        public int CellX, CellZ, Spot = -1, Rounds;
        public Dictionary<string, object?> District = new();
        public int BandY1, BandY2;
        public int AnchorX, AnchorZ;
        public ColumnFlood? Flood;
        public readonly HashSet<Column> Held = new();
        public readonly List<Column> Pending = new();
        public int Generated;
        public Stopwatch Clock = new();
        public string Status = "";
    }

    public Census(ICoreServerAPI sapi, string outPath, int size, int parallel, Action<string> log, Action<IEnumerable<string>> onDone)
    {
        this.sapi = sapi;
        this.outPath = outPath;
        this.size = size;
        this.parallel = Math.Max(1, parallel);
        this.log = log;
        this.onDone = onDone;
        (originX, originZ) = OreSurveySystem.Origin(sapi, size);
    }

    public void Start(IReadOnlyCollection<string>? only)
    {
        var blocks = sapi.World.Blocks;
        metalOfId = new string?[blocks.Count];
        rockId = new bool[blocks.Count];
        for (int id = 0; id < blocks.Count; id++)
            if (blocks[id]?.Code is { } code)
            {
                metalOfId[id] = OreNames.MetalOfPath(code.Path);
                rockId[id] = code.Path.StartsWith("rock-", StringComparison.Ordinal);
            }
        var sized = SizedMetals();
        if (only != null) sized.IntersectWith(only);
        cellsOut = new StreamWriter(outPath + ".cells.csv");
        int bx1 = originX * ChunkSize, bz1 = originZ * ChunkSize, bx2 = bx1 + size * ChunkSize, bz2 = bz1 + size * ChunkSize;
        log($"census: area blocks x {bx1}..{bx2}, z {bz1}..{bz2} ({size}x{size} chunks); sized metals {string.Join(", ", sized.Order())}; {parallel} floods at once, margin {Margin}, cap {Cap}");
        if (kinds != "districts") EnqueueCells(sized, bx1, bz1, bx2, bz2);
        if (kinds != "cells") EnqueueDistricts(sized, bx1, bz1, bx2, bz2);
        log($"census: {queue.Count} deposits queued");
        if (early) sapi.Event.ChunkColumnGeneration(OnVegetation, EnumWorldGenPass.Vegetation, "standard");
        sapi.Event.RegisterGameTickListener(_ => Pump(), 50);
        sapi.Event.RegisterGameTickListener(_ => Watchdog(), 60_000);
    }

    HashSet<string> SizedMetals()
    {
        var asset = sapi.Assets.TryGet(new AssetLocation("seraphhorizons", "config/ore-sizes.json"));
        var table = asset?.ToObject<Dictionary<string, SizeEntry>>() ?? new();
        return table.Where(kv => kv.Value.SmallIngots > 0).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
    }

    sealed class SizeEntry
    {
        public double SmallIngots { get; set; }
    }

    // ---------------------------------------------------------------------------------------
    // Enumeration

    void EnqueueCells(HashSet<string> sized, int bx1, int bz1, int bx2, int bz2)
    {
        var oreSystem = sapi.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "SeraphHorizons.Mod.Ore.OreSystem");
        placement = oreSystem?.GetType().GetProperty("Placement")?.GetValue(oreSystem);
        if (placement == null)
        {
            log("census: no ore cells in this world (the pack's OreSystem.Placement is null): no cell deposits");
            return;
        }
        var t = placement.GetType();
        var managed = ((IEnumerable<string>)t.GetProperty("Managed")!.GetValue(placement)!).ToList();
        var cells = t.GetProperty("Cells")!.GetValue(placement)!;
        cellSizeOf = cells.GetType().GetMethod("CellSize", [typeof(string)]);
        spotsOf = t.GetMethod("SpotsOf");
        stateOf = t.GetMethod("StateOf");
        var cellPos = t.Assembly.GetType("SeraphHorizons.Mod.Ore.Core.CellPos")!;
        cellPosCtor = cellPos.GetConstructor([typeof(int), typeof(int)]);
        foreach (var metal in managed.Where(sized.Contains).Order(StringComparer.Ordinal))
        {
            int cs = (int)cellSizeOf!.Invoke(cells, [metal])!;
            for (int cx = FloorDiv(bx1, cs); cx <= FloorDiv(bx2 - 1, cs); cx++)
                for (int cz = FloorDiv(bz1, cs); cz <= FloorDiv(bz2 - 1, cs); cz++)
                    queue.Enqueue(new Job { Id = nextId++, Kind = "cell", Metal = metal, CellX = cx, CellZ = cz });
        }
    }

    void EnqueueDistricts(HashSet<string> sized, int bx1, int bz1, int bx2, int bz2)
    {
        var sys = sapi.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "InterestingOreGen.Generators.HydrothermalDistrictSystem");
        if (sys == null)
        {
            log("census: no hydrothermal districts (Interesting Ore Gen not loaded)");
            return;
        }
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var t = sys.GetType();
        var spawn = t.GetMethod("TrySpawnDistrictsNear", Any)!;
        int tile = 8000;
        if (t.GetField("_configs", Any)?.GetValue(sys) is IList { Count: > 0 } configs
            && configs[0]!.GetType().GetField("MinDistanceBetweenDistricts", Any)?.GetValue(configs[0]) is int ts and > 0)
            tile = ts;
        // Builds the 3 x 3 tiles around each chunk's tile (IOG's own trigger), from the seed.
        for (int tx = FloorDiv(bx1, tile); tx <= FloorDiv(bx2 - 1, tile); tx++)
            for (int tz = FloorDiv(bz1, tile); tz <= FloorDiv(bz2 - 1, tile); tz++)
                spawn.Invoke(sys, [(int)((tx * (long)tile + tile / 2) / ChunkSize), (int)((tz * (long)tile + tile / 2) / ChunkSize)]);
        var districts = new List<object>();
        lock (t.GetField("_districtLock", Any)?.GetValue(sys) ?? new object())
            if (t.GetField("_activeDistricts", Any)?.GetValue(sys) is IDictionary d)
                foreach (DictionaryEntry e in d)
                    if (e.Value != null) districts.Add(e.Value);
        int veins = 0;
        foreach (var district in districts)
        {
            var dt = district.GetType();
            var centre = (Vec2i)dt.GetProperty("Centre", Any)!.GetValue(district)!;
            var config = dt.GetProperty("Config", Any)!.GetValue(district)!;
            string code = config.GetType().GetField("Code", Any)?.GetValue(config) as string ?? "";
            string host = config.GetType().GetField("HostMaterialCode", Any)?.GetValue(config) as string ?? "";
            var zones = (IList)dt.GetProperty("OreZones", Any)!.GetValue(district)!;
            log($"census: district '{code}' at ({centre.X}, {centre.Y}), host {host}, {zones.Count} zones");
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i]!;
                var zt = zone.GetType();
                string kind = zt.GetField("ZoneType", Any)!.GetValue(zone)!.ToString()!;
                if (kind != "OreShoot" && kind != "LadderVein") continue;
                var box = (Cuboidd)zt.GetField("BoundingBox", Any)!.GetValue(zone)!;
                int x = (int)Math.Floor((box.X1 + box.X2) / 2), z = (int)Math.Floor((box.Z1 + box.Z2) / 2);
                if (x < bx1 || x >= bx2 || z < bz1 || z >= bz2) continue;
                var ores = new List<string>();
                var metals = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var a in (IList)zt.GetField("EligibleOres", Any)!.GetValue(zone)!)
                {
                    var at = a!.GetType();
                    string oreCode = at.GetField("OreCode", Any)?.GetValue(a) as string ?? "";
                    float density = at.GetField("Density", Any)?.GetValue(a) is float f ? f : 0;
                    if (oreCode.Length == 0 || density <= 0) continue;
                    ores.Add(oreCode);
                    if (OreNames.MetalOfPath(oreCode) is { } m && sized.Contains(m)) metals.Add(m);
                }
                foreach (var metal in metals)
                {
                    veins++;
                    queue.Enqueue(new Job
                    {
                        Id = nextId++, Kind = "district", Metal = metal, AnchorX = x, AnchorZ = z,
                        BandY1 = (int)Math.Floor(box.Y1), BandY2 = (int)Math.Ceiling(box.Y2),
                        District = new()
                        {
                            ["code"] = code, ["host"] = host, ["centreX"] = centre.X, ["centreZ"] = centre.Y,
                            ["zone"] = i, ["zoneKind"] = kind, ["ores"] = ores,
                            ["box"] = new[] { box.X1, box.Y1, box.Z1, box.X2, box.Y2, box.Z2 },
                        },
                    });
                }
            }
        }
        log($"census: {districts.Count} districts built near the area; {veins} kept veins of sized metals centred in it");
    }

    // ---------------------------------------------------------------------------------------
    // Driving

    void Pump()
    {
        if (finished) return;
        while (active.Count < parallel && queue.Count > 0)
        {
            var job = queue.Dequeue();
            active.Add(job);
            job.Clock.Start();
            Begin(job);
        }
        if (active.Count == 0 && queue.Count == 0) Finish();
    }

    void Begin(Job job)
    {
        if (job.Kind == "district")
        {
            StartFlood(job, new Column(FloorDiv(job.AnchorX, ChunkSize), FloorDiv(job.AnchorZ, ChunkSize)));
            return;
        }
        Resolve(job);
    }

    // Follows the cell's spots as DepositService.Verify does: generating the active spot's column
    // settles it (placed, or the next spot becomes active).
    void Resolve(Job job)
    {
        var cell = cellPosCtor!.Invoke([job.CellX, job.CellZ]);
        var state = stateOf!.Invoke(placement, [job.Metal, cell])!;
        var st = state.GetType();
        bool none = (bool)st.GetProperty("None")!.GetValue(state)!;
        bool placed = (bool)st.GetProperty("Placed")!.GetValue(state)!;
        int activeSpot = (int)st.GetProperty("Active")!.GetValue(state)!;
        if (none)
        {
            End(job, "none");
            return;
        }
        var spot = ((Array)spotsOf!.Invoke(placement, [job.Metal, cell])!).GetValue(activeSpot)!;
        int x = (int)spot.GetType().GetProperty("X")!.GetValue(spot)!, z = (int)spot.GetType().GetProperty("Z")!.GetValue(spot)!;
        job.Spot = activeSpot;
        job.AnchorX = x;
        job.AnchorZ = z;
        var col = new Column(FloorDiv(x, ChunkSize), FloorDiv(z, ChunkSize));
        if (placed)
        {
            StartFlood(job, col);
            return;
        }
        if (job.Rounds++ > 9)
        {
            End(job, "unresolved");
            return;
        }
        // A column seen at Vegetation has had its TerrainFeatures pass, where the cell rule decides
        // the spot: the state has moved on already, and is read again (bounded by Rounds).
        if (Known(col) != null) Resolve(job);
        else Request(job, [col], () => Resolve(job));
    }

    void StartFlood(Job job, Column anchor)
    {
        job.Flood = new ColumnFlood(anchor, Margin, Cap);
        if (lead is { } d && job.Rounds >= 0)
        {
            // Experiment: generate another column first, to show the vein depends on the order.
            job.Rounds = -1;
            Request(job, [new Column(anchor.X + d.X, anchor.Z + d.Z)], () => Step(job));
            return;
        }
        Step(job);
    }

    // Reports every column of the flood's current batch that is known (scanned when it completed,
    // or early, at its Vegetation pass); asks for a few of the rest, each of which brings its
    // neighbours through Vegetation too, and comes back when they are done.
    void Step(Job job)
    {
        var flood = job.Flood!;
        while (true)
        {
            if (job.Pending.Count == 0)
            {
                var batch = flood.NextBatch();
                if (batch.Count == 0)
                {
                    if (flood.Done) End(job, "flooded");
                    return;
                }
                job.Pending.AddRange(reverse ? batch.AsEnumerable().Reverse() : batch);
            }
            job.Pending.RemoveAll(c =>
            {
                if (Known(c) is not { } scan) return false;
                flood.Report(c, scan.Has(job.Metal));
                return true;
            });
            if (job.Pending.Count > 0) break;
        }
        // A greedy cover: a column asked for brings those within `cover` of it through Vegetation
        // (when it is generated, not merely loaded); any still unknown are asked for next round.
        var ask = new List<Column>();
        foreach (var c in job.Pending)
            if (!ask.Any(a => a.Chebyshev(c) <= cover))
                ask.Add(c);
        Request(job, ask, () => Step(job));
    }

    /// <summary>A column's scan if its ore is final and seen: scanned when it completed, or at its
    /// Vegetation pass (<see cref="OnVegetation"/>).</summary>
    ColumnScan? Known(Column c)
    {
        if (scans.TryGetValue(c, out var scan)) return scan;
        if (early && earlyScans.TryGetValue(c, out scan))
        {
            scans[c] = scan;
            return scan;
        }
        return null;
    }

    /// <summary>Loads (generating if need be) and scans the columns, held for the job until it
    /// ends, then calls back once all are scanned.</summary>
    void Request(Job job, IReadOnlyList<Column> columns, Action then)
    {
        int left = columns.Count;
        void One()
        {
            if (--left == 0) then();
        }
        foreach (var c in columns)
        {
            if (job.Held.Add(c)) holds[c] = holds.GetValueOrDefault(c) + 1;
            if (loaded.Contains(c))
            {
                One();
                continue;
            }
            if (waiting.TryGetValue(c, out var list))
            {
                list.Add(One);
                continue;
            }
            waiting[c] = [One];
            job.Generated++;
            var col = c;
            sapi.WorldManager.LoadChunkColumnPriority(c.X, c.Z, new ChunkLoadOptions
            {
                KeepLoaded = true,
                OnLoaded = () =>
                {
                    var scan = Scan(col, cy => sapi.WorldManager.GetChunk(col.X, cy, col.Z));
                    columnsCompleted++;
                    // The check on the early scans: the same column, read again once complete.
                    if (earlyScans.TryGetValue(col, out var seen))
                    {
                        earlyChecked++;
                        if (!seen.SameOre(scan) && earlyMismatch++ < 10)
                            log($"census: column {col.X},{col.Z} holds other ore when complete than at its Vegetation pass");
                    }
                    scans[col] = scan;
                    loaded.Add(col);
                    lastProgress = clock.Elapsed.TotalSeconds;
                    var calls = waiting[col];
                    waiting.Remove(col);
                    foreach (var call in calls) call();
                },
            });
        }
    }

    // Harmony-free hook: the census's own handler in the Vegetation pass. Every chunk column's ore
    // is final by then (its neighbours have finished TerrainFeatures, where all ore is placed, and
    // nothing after writes ore), so its scan stands for the complete column's. Worldgen thread.
    void OnVegetation(IChunkColumnGenerateRequest request)
    {
        try
        {
            var c = new Column(request.ChunkX, request.ChunkZ);
            var chunks = request.Chunks;
            earlyScans[c] = Scan(c, cy => cy < chunks.Length ? chunks[cy] : null);
            Interlocked.Increment(ref columnsEarly);
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref earlyErrors) == 1) sapi.Logger.Error("[oresurvey] census: early scan failed: {0}", e);
        }
    }

    ColumnScan Scan(Column c, System.Func<int, IWorldChunk?> chunkAt)
    {
        var scan = new ColumnScan();
        int chunksY = sapi.WorldManager.MapSizeY / ChunkSize;
        for (int cy = 0; cy < chunksY; cy++)
        {
            var chunk = chunkAt(cy);
            if (chunk == null)
            {
                scan.Missing++;
                continue;
            }
            chunk.Unpack();
            var data = chunk.Data;
            var hm = chunk.MapChunk.WorldGenTerrainHeightMap;
            for (int i = 0; i < ChunkSize * ChunkSize * ChunkSize; i++)
            {
                int id = data[i];
                if (id <= 0 || id >= metalOfId.Length) continue;
                int y = cy * ChunkSize + (i >> 10);
                if (rockId[id])
                {
                    if (!scan.Rocks.TryGetValue(id, out var slabs)) scan.Rocks[id] = slabs = new int[sapi.WorldManager.MapSizeY / 8];
                    slabs[y >> 3]++;
                    continue;
                }
                if (metalOfId[id] is not { } metal) continue;
                int lx = i & 31, lz = (i >> 5) & 31;
                if (!scan.Cells.TryGetValue(metal, out var cells)) scan.Cells[metal] = cells = new();
                var key = (id, (c.X * ChunkSize + lx) >> 3, (c.Z * ChunkSize + lz) >> 3, y >> 3);
                int depth = hm[lz * ChunkSize + lx] - y;
                cells[key] = cells.TryGetValue(key, out var v) ? (v.N + 1, Math.Min(v.MinDepth, depth)) : (1, depth);
            }
        }
        return scan;
    }

    void End(Job job, string status)
    {
        job.Status = status;
        job.Clock.Stop();
        var rec = new Dictionary<string, object?>
        {
            ["id"] = job.Id, ["kind"] = job.Kind, ["metal"] = job.Metal, ["status"] = status,
            ["anchorX"] = job.AnchorX, ["anchorZ"] = job.AnchorZ,
        };
        if (job.Kind == "cell")
        {
            rec["cellX"] = job.CellX;
            rec["cellZ"] = job.CellZ;
            rec["spot"] = job.Spot;
        }
        else
        {
            rec["district"] = job.District;
            rec["bandY1"] = job.BandY1;
            rec["bandY2"] = job.BandY2;
        }
        int blocks = 0;
        if (job.Flood is { } flood)
        {
            int x1 = int.MaxValue, y1 = int.MaxValue, z1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue, z2 = int.MinValue;
            foreach (var c in flood.Asked.OrderBy(c => c.X).ThenBy(c => c.Z))
            {
                if (!scans[c].Cells.TryGetValue(job.Metal, out var cells)) continue;
                foreach (var ((id, cx, cz, cy), v) in cells)
                {
                    cellsOut!.WriteLine($"{job.Id},{sapi.World.Blocks[id].Code},{cx},{cz},{cy},{v.N},{v.MinDepth}");
                    blocks += v.N;
                    x1 = Math.Min(x1, cx * 8); y1 = Math.Min(y1, cy * 8); z1 = Math.Min(z1, cz * 8);
                    x2 = Math.Max(x2, cx * 8 + 7); y2 = Math.Max(y2, cy * 8 + 7); z2 = Math.Max(z2, cz * 8 + 7);
                }
            }
            rec["columns"] = flood.Asked.Count;
            rec["oreColumns"] = flood.Found.Count;
            rec["capped"] = flood.Capped;
            if (blocks > 0) rec["bbox"] = new[] { x1, y1, z1, x2, y2, z2 };
            if (job.Kind == "district") rec["rocks"] = BandRocks(job, flood.Anchor);
        }
        rec["blocks"] = blocks;
        rec["generated"] = job.Generated;
        rec["seconds"] = Math.Round(job.Clock.Elapsed.TotalSeconds, 2);
        records.Add(rec);
        foreach (var c in job.Held)
        {
            if ((holds[c] -= 1) > 0) continue;
            holds.Remove(c);
            if (loaded.Remove(c)) sapi.WorldManager.UnloadChunkColumn(c.X, c.Z);
        }
        active.Remove(job);
        done++;
        log($"census: {done}/{done + active.Count + queue.Count} {job.Kind} {job.Metal} #{job.Id} {status}: {blocks} blocks, "
            + $"{(job.Flood?.Asked.Count ?? 0)} columns ({job.Generated} new){(job.Flood?.Capped == true ? ", capped" : "")}, {job.Clock.Elapsed.TotalSeconds:F1}s; "
            + $"elapsed {clock.Elapsed.TotalSeconds:F0}s, {columnsCompleted} columns asked for, {columnsEarly} seen at Vegetation");
    }

    /// <summary>The rocks in the vein's band of heights over the columns around its anchor, by
    /// share, most first: what its country rock is (a mafic district's shoot is ore across its whole
    /// ellipse only where that is basalt, docs/oregen.md).</summary>
    Dictionary<string, double> BandRocks(Job job, Column anchor)
    {
        var counts = new Dictionary<int, long>();
        int s1 = Math.Max(0, job.BandY1) >> 3, s2 = (Math.Min(sapi.WorldManager.MapSizeY, job.BandY2) - 1) >> 3;
        for (int dx = -Margin; dx <= Margin; dx++)
            for (int dz = -Margin; dz <= Margin; dz++)
            {
                if (!scans.TryGetValue(new Column(anchor.X + dx, anchor.Z + dz), out var scan)) continue;
                foreach (var (id, slabs) in scan.Rocks)
                    for (int k = s1; k <= s2 && k < slabs.Length; k++)
                        counts[id] = counts.GetValueOrDefault(id) + slabs[k];
            }
        long total = counts.Values.Sum();
        return counts.OrderByDescending(kv => kv.Value).Take(4)
            .ToDictionary(kv => sapi.World.Blocks[kv.Key].Code.Path, kv => Math.Round((double)kv.Value / Math.Max(1, total), 3));
    }

    void Watchdog()
    {
        if (finished) return;
        double idle = clock.Elapsed.TotalSeconds - lastProgress;
        if (idle > 300)
            log($"census: no column for {idle:F0}s; {active.Count} active ({string.Join(", ", active.Select(j => $"#{j.Id} {j.Metal}"))}), {waiting.Count} columns awaited");
    }

    void Finish()
    {
        finished = true;
        cellsOut?.Dispose();
        var doc = new Dictionary<string, object?>
        {
            ["seed"] = sapi.World.Seed,
            ["seaLevel"] = sapi.World.SeaLevel,
            ["mapSizeY"] = sapi.WorldManager.MapSizeY,
            ["area"] = new { x = originX * ChunkSize, z = originZ * ChunkSize, size = size * ChunkSize, chunkColumns = size * size },
            ["areaKm2"] = size * 32.0 * size * 32.0 / 1e6,
            ["margin"] = Margin,
            ["cap"] = Cap,
            ["columnsCompleted"] = columnsCompleted,
            ["columnsEarly"] = columnsEarly,
            ["early"] = early,
            ["earlyChecked"] = earlyChecked,
            ["earlyMismatch"] = earlyMismatch,
            ["seconds"] = clock.Elapsed.TotalSeconds,
            ["deposits"] = records,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        log($"census done in {clock.Elapsed.TotalSeconds:F0}s: {records.Count} deposits, {columnsCompleted} columns asked for, {columnsEarly} seen at Vegetation; "
            + $"early scans checked against the complete column: {earlyChecked}, {earlyMismatch} differ");
        onDone([]);
    }

    static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return (value % divisor != 0 && (value < 0) != (divisor < 0)) ? q - 1 : q;
    }
}
