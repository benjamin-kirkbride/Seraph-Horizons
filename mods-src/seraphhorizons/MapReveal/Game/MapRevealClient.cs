using System.Collections.Concurrent;
using HarmonyLib;
using SeraphHorizons.Mod.MapReveal.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.MapReveal;

/// <summary>
/// The client half of Map Reveal: turns the columns the server sends into world map pieces, the way
/// the game's terrain layer (<c>ChunkMapLayer</c>, VSEssentials) does for chunks the client loads,
/// and stores them in that layer's map database, where explored chunks go. The map then shows them
/// like explored ones, now and in later sessions.
///
/// The layer's database connection belongs to the map's own background thread (the layer's
/// <c>OnOffThreadTick</c>, every 20 ms), and is not thread-safe, so the pieces are rendered and
/// stored there, in a Harmony postfix on that method. The chunks then showing on an open map are
/// queued for the layer to redraw (its <c>chunksToGen</c>, under its lock), from the main thread,
/// which owns the set of visible chunks: the layer reads each back from the database (or draws it
/// from the client's own chunks if it has them loaded).
///
/// Private members of <c>ChunkMapLayer</c> used: <c>mapdb</c>, <c>chunksToGen</c>,
/// <c>chunksToGenLock</c>, <c>curVisibleChunks</c>. If any is gone, the feature logs a warning and
/// ignores what the server sends.
/// </summary>
internal sealed class MapRevealClient
{
    public const string HarmonyId = "seraphhorizons.mapreveal";

    /// <summary>Batches decoded and stored per map-thread tick: the layer's own work waits for them.</summary>
    private const int BatchesPerTick = 2;

    private static readonly AccessTools.FieldRef<ChunkMapLayer, MapDB>? MapDb = Field<MapDB>("mapdb");
    private static readonly AccessTools.FieldRef<ChunkMapLayer, UniqueQueue<FastVec2i>>? ChunksToGen = Field<UniqueQueue<FastVec2i>>("chunksToGen");
    private static readonly AccessTools.FieldRef<ChunkMapLayer, object>? ChunksToGenLock = Field<object>("chunksToGenLock");
    private static readonly AccessTools.FieldRef<ChunkMapLayer, HashSet<FastVec2i>>? VisibleChunks = Field<HashSet<FastVec2i>>("curVisibleChunks");

    // The client in this process, for the postfix (one per process; singleplayer's server has none).
    private static MapRevealClient? _instance;

    private readonly ICoreClientAPI _capi;
    private readonly Harmony _harmony = new(HarmonyId);
    private readonly ConcurrentQueue<byte[]> _inbox = new();
    private readonly ConcurrentQueue<FastVec2i> _stored = new();
    private int[]? _colors;
    private int _waterEdge;
    private bool _warned;

    private MapRevealClient(ICoreClientAPI capi) => _capi = capi;

    /// <summary>Why the client can't store pieces on this game version, or null if it can.</summary>
    public static string? Unsupported =>
        MapDb is null || ChunksToGen is null || ChunksToGenLock is null || VisibleChunks is null
            ? "ChunkMapLayer's mapdb, chunksToGen, chunksToGenLock or curVisibleChunks is gone"
            : null;

    public static MapRevealClient? Start(ICoreClientAPI capi, IClientNetworkChannel channel)
    {
        if (Unsupported is { } why)
        {
            capi.Logger.Warning("[seraphhorizons] Map Reveal is off on this client: {0}", why);
            channel.SetMessageHandler<MapRevealPacket>(_ => { });
            return null;
        }
        var client = new MapRevealClient(capi);
        client._harmony.Patch(AccessTools.Method(typeof(ChunkMapLayer), nameof(ChunkMapLayer.OnOffThreadTick)),
            postfix: new HarmonyMethod(typeof(MapRevealClient), nameof(OffThreadTickPostfix)));
        channel.SetMessageHandler<MapRevealPacket>(packet => client._inbox.Enqueue(packet.Columns));
        capi.Event.RegisterGameTickListener(_ => client.QueueVisibleForRedraw(), 250);
        _instance = client;
        return client;
    }

    public void Dispose()
    {
        _harmony.UnpatchAll(HarmonyId);
        if (_instance == this) _instance = null;
    }

    private static void OffThreadTickPostfix(ChunkMapLayer __instance)
    {
        var client = _instance;
        if (client is null || client._inbox.IsEmpty) return;
        try { client.StoreBatches(__instance); }
        catch (Exception e) { client._capi.Logger.Error("[seraphhorizons] Map Reveal: could not store revealed chunks: {0}", e); }
    }

    // Map thread: decode, render and store up to BatchesPerTick batches.
    private void StoreBatches(ChunkMapLayer layer)
    {
        if (layer.block2Color is null) return; // the layer is not loaded yet
        var colors = _colors ??= LayerColors(layer);
        var pieces = new Dictionary<FastVec2i, MapPieceDB>();
        for (int i = 0; i < BatchesPerTick && _inbox.TryDequeue(out var data); i++)
        {
            List<ColumnSample> columns;
            try { columns = RevealCodec.Decode(data); }
            catch (InvalidDataException e)
            {
                if (!_warned) _capi.Logger.Warning("[seraphhorizons] Map Reveal: ignoring a batch the server sent: {0}", e.Message);
                _warned = true;
                continue;
            }
            foreach (var column in columns)
                pieces[new FastVec2i(column.X, column.Z)] = new MapPieceDB { Pixels = Render(layer, colors, column) };
        }
        if (pieces.Count == 0) return;
        MapDb!(layer).SetMapPieces(pieces);
        foreach (var pos in pieces.Keys) _stored.Enqueue(pos);
    }

    // The layer's colours by index (its private `colors` is built from colorsByCode the same way).
    private int[] LayerColors(ChunkMapLayer layer)
    {
        var colors = new int[layer.colorsByCode.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = layer.colorsByCode.GetValueAtIndex(i);
        _waterEdge = layer.colorsByCode["wateredge"];
        return colors;
    }

    private int[] Render(ChunkMapLayer layer, int[] colors, ColumnSample column)
    {
        var cellColors = new int[TerrainShade.Area];
        var blocks = _capi.World.Blocks;
        float randomWeight = (float)_capi.World.Config.GetDecimal("colorRandomizationWeight", 0.6000000238418579);
        var pos = new BlockPos(0);
        for (int k = 0; k < TerrainShade.Area; k++)
        {
            int id = column.BlockIds[k];
            switch (column.Kinds[k])
            {
                case CellKind.None:
                    break;
                case CellKind.WaterEdge:
                    cellColors[k] = _waterEdge;
                    break;
                default:
                    if (column.Heights is { } heights && id < blocks.Count && blocks[id] is { } block)
                    {
                        // The colour-accurate map, as ChunkMapLayer.GenerateChunkImage colours it.
                        pos.Set(column.X * TerrainShade.Size + k % TerrainShade.Size, heights[k], column.Z * TerrainShade.Size + k / TerrainShade.Size);
                        int color = block.GetColor(_capi, pos);
                        int random = block.GetRandomColor(_capi, pos, BlockFacing.UP, GameMath.MurmurHash3Mod(pos.X, pos.Y, pos.Z, 30));
                        random = ((random & 0xFF) << 16) | (((random >> 8) & 0xFF) << 8) | ((random >> 16) & 0xFF);
                        cellColors[k] = ColorUtil.ColorOverlay(color, random, randomWeight);
                    }
                    else if (id < layer.block2Color.Length)
                        cellColors[k] = colors[layer.block2Color[id]];
                    break;
            }
        }
        return TerrainShade.Render(cellColors, column.Shadow);
    }

    // Main thread: the stored chunks an open map shows are redrawn from the database.
    private void QueueVisibleForRedraw()
    {
        if (_stored.IsEmpty) return;
        var manager = _capi.ModLoader.GetModSystem<WorldMapManager>();
        var layer = manager?.MapLayers.OfType<ChunkMapLayer>().FirstOrDefault();
        if (layer is null || !manager!.IsOpened)
        {
            _stored.Clear();
            return;
        }
        var visible = VisibleChunks!(layer);
        var redraw = new List<FastVec2i>();
        while (_stored.TryDequeue(out var pos))
            if (visible.Contains(pos)) redraw.Add(pos);
        if (redraw.Count == 0) return;
        lock (ChunksToGenLock!(layer))
        {
            var queue = ChunksToGen!(layer);
            foreach (var pos in redraw) queue.Enqueue(pos);
        }
    }

    private static AccessTools.FieldRef<ChunkMapLayer, T>? Field<T>(string name) where T : class
    {
        try { return AccessTools.FieldRefAccess<ChunkMapLayer, T>(name); }
        catch (Exception) { return null; }
    }
}
