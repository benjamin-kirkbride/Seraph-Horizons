using System.Text;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Stable veins (#946, switch <c>StableVeins</c>, new worlds only): an ore cell's vein is a pure
/// function of the seed, whatever order chunks generate in and whatever is loaded.
///
/// Why it was not (Interesting Ore Gen 2.3.8, <c>TiltedDiscDepositGenerator</c>): the game's
/// <c>GenDeposits</c> replays every try of every source chunk within 3 chunks for each column it
/// generates, and expects each pass to write its own column only. IOG's vein shapes write through
/// the worldgen accessor into any chunk it reaches, and read the world as they go: the centre
/// block picks the host rock and gates the whole vein (<c>GenerateMultiOreFaultPocket</c>,
/// <c>GenerateTendrils</c>), each chimney step reads its block, and <c>TryPlaceMainOre</c> draws
/// from the vein's random sequence only where it finds a loaded host rock. So each pass drew a
/// different vein, and what a column held was whichever passes reached it, in whichever order.
/// The cell rule added its own: it read the try's centre in the anchor chunk at each pass (vanilla
/// cracked rock lands there during the anchor's own pass), and approved nothing where the anchor
/// chunk was not reachable.
///
/// What it does instead:
/// <list type="number">
/// <item>Decide once. During TerrainFeatures no managed vein is placed at all. In the anchor
/// column's own <c>GenDeposits</c> pass, which reads only that column, the cell rule picks the
/// first try of the metal from the anchor whose centre is in a host rock (a chimney's: the host rock
/// nearest its centre in the column), and when the pass ends the vein is replayed (below) to see
/// whether it reaches rock in the anchor column: it holds the deposit, or the cell moves on. The
/// decision, the try's seed and the host rock are kept in <see cref="StableVeinBook"/>
/// (<c>seraphhorizons:stableveins</c>).</item>
/// <item>Replay everywhere alike. At every column's PreDone pass, every held vein whose anchor is
/// within 3 chunks is replayed: IOG's own <c>GenDeposit</c>, seeded as <c>GenDeposits</c> seeds the
/// try, against a world that is the host rock everywhere (<see cref="StableVeinWorlds.HostRockWorld"/>),
/// so every draw is made whatever is real. Its writes in this column are laid on the real blocks
/// (<see cref="StableVeinApply"/>). A column's PreDone pass comes after every column within 3 has
/// finished TerrainFeatures (each pass waits for its neighbours to finish the one before), so the
/// anchor's decision is always made by then.</item>
/// <item>Keep other veins in their column. IOG's unmanaged veins (gems, quartz, olivine) keep
/// IOG's own rule but write only into the column being generated
/// (<see cref="StableVeinWorlds.ColumnWorld"/>), so no pass changes another column's blocks.</item>
/// </list>
/// Grade pockets (#944) run inside the replay as before: they depend on the position and seed only.
/// </summary>
public sealed partial class OreCellPlacement
{
    public const string StableVeinsKey = "seraphhorizons:stableveins";

    private bool _stable;
    private StableVeinBook _veins = new();
    private bool _veinsDirty;
    private Dictionary<DepositGeneratorBase, string>? _variantKeys;
    private Dictionary<string, DepositGeneratorBase>? _generators;
    private int _missed;

    [ThreadStatic] private static ChunkPos? _passTarget;
    [ThreadStatic] private static bool _replaying;
    // Decided in the anchor's pass, recorded when it ends (DecideStable).
    [ThreadStatic] private static Dictionary<(string Metal, CellPos Cell, int Spot), StableVein>? _pending;

    /// <summary>The stable veins decided so far; null without the switch.</summary>
    public StableVeinBook? Veins => _stable ? _veins : null;

    /// <summary>Columns whose PreDone pass found a vein within reach not yet decided (should stay 0).</summary>
    public int MissedReplays => _missed;

    private static System.Reflection.MethodInfo? GenDepositMethod => AccessTools.Method(GeneratorType, "GenDeposit");

    // IOG's generators keep their state in instance fields and share GenDeposits' one DepositRand,
    // and worldgen passes can run on more than one thread: a replay must never interleave with a
    // TerrainFeatures pass running a vein. Taken around every IOG GenDeposit call and every replay.
    private static readonly object IogLock = new();

    private void BindStable(Harmony harmony)
    {
        _stable = true;
        _veins = LoadVeins(_api);
        harmony.Patch(GenDepositMethod, prefix: new HarmonyMethod(typeof(OreCellPlacement), nameof(GenDepositPrefix)),
            finalizer: new HarmonyMethod(typeof(OreCellPlacement), nameof(GenDepositFinalizer)));
        _api.Event.ChunkColumnGeneration(OnPreDone, EnumWorldGenPass.PreDone, "standard");
        _api.Logger.Notification("[seraphhorizons] Stable veins: ore cell veins decided in their anchor's pass and replayed at PreDone; IOG's other veins kept to their column");
    }

    /// <summary>Why stable veins can't bind (beyond the cell rule's own checks), or null.</summary>
    public static string? StableUnsupported()
    {
        if (GeneratorType is not { } type) return $"{GeneratorTypeName} is gone";
        var m = AccessTools.Method(type, "GenDeposit");
        if (m == null || m.GetParameters().Length != 6 || m.GetParameters()[0].ParameterType != typeof(IBlockAccessor))
            return $"{GeneratorTypeName}.GenDeposit changed";
        return null;
    }

    // The cell rule in the anchor's own pass: the first try of the metal from the anchor whose
    // centre (as this pass reads its own column) is a host rock is the cell's vein. Nothing is placed.
    private bool ApproveStable(DepositGeneratorBase generator, string metal, CellPos cell, OreSpot spot, BlockPos pos)
    {
        if (_passTarget != spot.Chunk) return false;
        var key = (metal, cell, spot.Index);
        var pending = _pending ??= new();
        if (pending.ContainsKey(key) || _veins.Get(metal, cell, spot.Index) != null) return false;
        if (CentreHost(generator, pos) is not { } host) return false;
        if (VariantKey(generator) is not { } variant || generator.DepositRand is not { } rand) return false;
        pending[key] = new StableVein(metal, cell, spot.Index, OreCells.FloorDiv(pos.X, OreCells.ChunkSize), OreCells.FloorDiv(pos.Z, OreCells.ChunkSize),
            rand.worldSeed, pos.X, pos.Z, _api.World.Blocks[host].Code.ToString(), variant, Holds: false);
        return false;
    }

    /// <summary>The host rock at the try's centre (its height replayed as IOG draws it), or for a
    /// chimney the host rock nearest its centre up or down the column; null if none.</summary>
    private int? CentreHost(DepositGeneratorBase generator, BlockPos pos)
    {
        var type = generator.GetType();
        if (AccessTools.Field(type, "YPosRel")?.GetValue(generator) is not NatFloat yPosRel
            || AccessTools.Field(type, "Radius")?.GetValue(generator) is not NatFloat radius
            || generator.DepositRand is not { } source)
            return null;
        var rand = new LCGRandom { worldSeed = source.worldSeed, mapGenSeed = source.mapGenSeed, currentSeed = source.currentSeed };
        if ((int)radius.nextFloat(1f, rand) <= 0) return null;
        rand.NextFloat();
        int y = (int)yPosRel.nextFloat(1f, rand);
        int height = _api.WorldManager.MapSizeY;
        if (y <= 0 || y >= height) return null;
        var hosts = HostRocks(generator);
        bool chimney = VariantOf!(generator)?.Attributes?["inblock"]?["veintype"]?.AsString()?.StartsWith("chimney", StringComparison.Ordinal) == true;
        for (int d = 0; d < (chimney ? height : 1); d++)
            foreach (int yy in d == 0 ? [y] : new[] { y + d, y - d })
            {
                if (yy <= 0 || yy >= height) continue;
                if (_passAccessor?.GetChunkAtBlockPos(new BlockPos(pos.X, yy, pos.Z, 0)) is not IServerChunk { Data: { } data }) return null;
                int size = OreCells.ChunkSize;
                int id = data.GetBlockIdUnsafe((GameMath.Mod(yy, size) * size + GameMath.Mod(pos.Z, size)) * size + GameMath.Mod(pos.X, size));
                if (hosts.Contains(id)) return id;
            }
        return null;
    }

    // At the end of the anchor's pass: does the decided vein reach rock in the anchor column?
    private bool DecideStable(string metal, CellPos cell, OreSpot spot, IChunkColumnGenerateRequest request)
    {
        var key = (metal, cell, spot.Index);
        if (_pending?.Remove(key, out var vein) != true || vein == null)
        {
            _api.Logger.Notification("[seraphhorizons] Stable veins: {0} cell {1}, {2} spot {3}: no try of the metal from the anchor starts in a host rock",
                metal, cell.X, cell.Z, spot.Index);
            return false;
        }
        int ore = 0;
        if (Replay(vein, request.Chunks, request.ChunkX, request.ChunkZ) is { } writes)
        {
            var overlay = new Dictionary<(int, int, int), int>();
            int Get(int x, int y, int z) => overlay.TryGetValue((x, y, z), out var id) ? id : BlockAt(request.Chunks, x, y, z);
            int hostId = HostId(vein);
            StableVeinApply.Apply(InColumn(writes, request.ChunkX, request.ChunkZ), hostId, Get,
                (x, y, z, id) =>
                {
                    overlay[(x, y, z)] = id;
                    if (id != hostId) ore++;
                }, IsRock);
        }
        bool holds = ore > 0;
        _veins.Add(vein with { Holds = holds });
        _veinsDirty = true;
        _api.Logger.Notification("[seraphhorizons] Stable veins: {0} cell {1}, {2} spot {3}: {4} at {5}, {6} in {7}, {8} ore blocks in the anchor column",
            metal, cell.X, cell.Z, spot.Index, vein.Variant, vein.CentreX, vein.CentreZ, vein.Host, ore);
        return holds;
    }

    /// <summary>A peek: a column the game generates on the side, never kept (vanilla and
    /// NoOceanTranslocators peek for translocator targets). Its map chunk is its own, not the
    /// world's. Stable veins neither decide from nor lay into one.</summary>
    private bool IsPeek(IChunkColumnGenerateRequest request) =>
        request.Chunks is not { Length: > 0 } chunks
        || !ReferenceEquals(_api.WorldManager.GetMapChunk(request.ChunkX, request.ChunkZ), chunks[0].MapChunk);

    /// <summary>The end of a peeked column's TerrainFeatures pass: its decisions are dropped.</summary>
    private bool SkipPeek(IChunkColumnGenerateRequest request)
    {
        if (!_stable || !IsPeek(request)) return false;
        _pending?.Clear();
        return true;
    }

    // PreDone: lay every held vein within reach into this column.
    private void OnPreDone(IChunkColumnGenerateRequest request)
    {
        try
        {
            if (IsPeek(request)) return;
            var c = new ChunkPos(request.ChunkX, request.ChunkZ);
            const int reach = 3, size = OreCells.ChunkSize;
            foreach (var metal in Managed)
            {
                var lo = Cells.CellOf(metal, (c.X - reach) * size, (c.Z - reach) * size);
                var hi = Cells.CellOf(metal, (c.X + reach + 1) * size - 1, (c.Z + reach + 1) * size - 1);
                for (int cx = lo.X; cx <= hi.X; cx++)
                    for (int cz = lo.Z; cz <= hi.Z; cz++)
                    {
                        var cell = new CellPos(cx, cz);
                        var state = StateOf(metal, cell);
                        if (state.None) continue;
                        var spot = SpotsOf(metal, cell)[state.Active];
                        if (Math.Max(Math.Abs(spot.Chunk.X - c.X), Math.Abs(spot.Chunk.Z - c.Z)) > reach) continue;
                        if (!state.Placed)
                        {
                            // The anchor is within reach but undecided: its pass has not run. It always
                            // has by now (see the class); count it if not.
                            if (Interlocked.Increment(ref _missed) <= 10)
                                _api.Logger.Warning("[seraphhorizons] Stable veins: {0} cell {1}, {2}: column {3}, {4} reached PreDone before its anchor (spot {5}, chunk {6}, {7}; map chunk {8}) was decided",
                                    metal, cx, cz, c.X, c.Z, state.Active, spot.Chunk.X, spot.Chunk.Z,
                                    _api.WorldManager.GetMapChunk(spot.Chunk.X, spot.Chunk.Z) == null ? "missing" : "there");
                            continue;
                        }
                        if (_veins.Get(metal, cell, state.Active) is not { Holds: true } vein) continue;
                        if (Replay(vein, request.Chunks, c.X, c.Z) is not { } writes) continue;
                        Lay(vein, writes, request.Chunks, c);
                    }
            }
        }
        catch (Exception e)
        {
            Warn(e);
        }
    }

    private void Lay(StableVein vein, List<(int X, int Y, int Z, int Id)> writes, IServerChunk[] chunks, ChunkPos c)
    {
        StableVeinApply.Apply(InColumn(writes, c.X, c.Z), HostId(vein), (x, y, z) => BlockAt(chunks, x, y, z),
            (x, y, z, id) =>
            {
                var data = chunks[y / OreCells.ChunkSize].Data;
                int i = Index(x, y, z);
                data.SetBlockUnsafe(i, id);
                data.SetFluid(i, 0);
            }, IsRock);
    }

    /// <summary>IOG's own generator for the vein's try, run against a world of its host rock; its
    /// writes in order, or null if the try's variant is gone.</summary>
    private List<(int X, int Y, int Z, int Id)>? Replay(StableVein vein, IServerChunk[] chunks, int chunkX, int chunkZ)
    {
        if (Generator(vein.Variant) is not { DepositRand: { } rand } generator) return null;
        var world = new StableVeinWorlds.HostRockWorld(HostId(vein), _api.WorldManager.MapSizeY, _api.World.Blocks);
        Monitor.Enter(IogLock);
        rand.SetWorldSeed(vein.WorldSeed);
        rand.InitPositionSeed(vein.SourceX, vein.SourceZ);
        _replaying = true;
        try
        {
            var subDeposits = new Dictionary<BlockPos, DepositVariant>();
            generator.GenDeposit(world.Accessor, chunks, chunkX, chunkZ, new BlockPos(vein.CentreX, -99, vein.CentreZ, 0), ref subDeposits);
        }
        finally
        {
            _replaying = false;
            Monitor.Exit(IogLock);
        }
        return world.Writes;
    }

    private static IEnumerable<(int X, int Y, int Z, int Id)> InColumn(List<(int X, int Y, int Z, int Id)> writes, int chunkX, int chunkZ) =>
        writes.Where(w => w.X >> 5 == chunkX && w.Z >> 5 == chunkZ);

    private int HostId(StableVein vein) => _api.World.GetBlock(new AssetLocation(vein.Host))?.BlockId ?? 0;

    private bool IsRock(int id) =>
        _api.World.Blocks[id]?.Code?.Path is { } path && (path == "stone" || path.StartsWith("rock-", StringComparison.Ordinal));

    private static int Index(int x, int y, int z) => ((y & 31) * 32 + (z & 31)) * 32 + (x & 31);

    private static int BlockAt(IServerChunk[] chunks, int x, int y, int z) =>
        y < 0 || y / OreCells.ChunkSize >= chunks.Length ? 0 : chunks[y / OreCells.ChunkSize].Data.GetBlockIdUnsafe(Index(x, y, z));

    // Every IOG vein variant the game's deposit generators run, named "code#n" (n: its place among
    // variants of that code), so a saved vein finds its generator after a restart.
    private void BuildVariantKeys()
    {
        _variantKeys = new();
        _generators = new();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in _api.ModLoader.Systems.OfType<GenDeposits>().SelectMany(g => g.Deposits ?? []))
        {
            if (v.GeneratorInst is not { } gen || !GeneratorType!.IsInstanceOfType(gen)) continue;
            int n = counts.GetValueOrDefault(v.Code) + 1;
            counts[v.Code] = n;
            string key = $"{v.Code}#{n}";
            _variantKeys[gen] = key;
            _generators[key] = gen;
        }
    }

    private string? VariantKey(DepositGeneratorBase generator)
    {
        if (_variantKeys == null) BuildVariantKeys();
        return _variantKeys!.GetValueOrDefault(generator);
    }

    private DepositGeneratorBase? Generator(string key)
    {
        if (_generators == null) BuildVariantKeys();
        return _generators!.GetValueOrDefault(key);
    }

    private void SaveStable()
    {
        if (!_stable || !_veinsDirty) return;
        _veinsDirty = false;
        _api.WorldManager.SaveGame.StoreData(StableVeinsKey, Encoding.UTF8.GetBytes(_veins.Serialize()));
    }

    private static StableVeinBook LoadVeins(ICoreServerAPI api)
    {
        try
        {
            var bytes = api.WorldManager.SaveGame.GetData(StableVeinsKey);
            return StableVeinBook.Parse(bytes == null ? null : Encoding.UTF8.GetString(bytes));
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Stable veins: could not read the decided veins from the savegame, starting afresh: {0}", e.Message);
            return new StableVeinBook();
        }
    }

    // Harmony: IOG's TiltedDiscDepositGenerator.GenDeposit(blockAccessor, chunks, chunkX, chunkZ, pos, ref subDeposits).
    // Outside a replay, every IOG vein writes only into the column being generated.
    private static void GenDepositPrefix(ref IBlockAccessor __0, int __2, int __3, out bool __state)
    {
        __state = false;
        if (_current is not { _stable: true }) return;
        Monitor.Enter(IogLock);
        __state = true;
        if (!_replaying) __0 = StableVeinWorlds.ColumnWorld(__0, __2, __3);
    }

    private static Exception? GenDepositFinalizer(Exception? __exception, bool __state)
    {
        if (__state) Monitor.Exit(IogLock);
        return __exception;
    }
}
