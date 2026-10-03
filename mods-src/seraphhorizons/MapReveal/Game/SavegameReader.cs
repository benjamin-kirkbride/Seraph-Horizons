using System.Reflection;
using HarmonyLib;
using ProtoBuf;
using SeraphHorizons.Mod.MapReveal.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace SeraphHorizons.Mod.MapReveal;

/// <summary>
/// Reads chunk columns straight from the savegame file, through a read-only connection of its own,
/// without loading them into the world: nothing is generated, no entity or block entity is created,
/// and the server's own connection (owned by its chunk thread) is never touched. The server keeps
/// its savegame in SQLite's WAL mode (with <c>CorruptionProtection</c>, the default), so this
/// connection reads alongside it safely and sees what the server last committed.
///
/// Only what the map needs is decoded: each map chunk's rain height map and generation pass
/// (<c>ServerMapChunk.FromBytes</c>), and the block data of the chunks those heights fall in, through
/// <see cref="StoredChunk"/> and the game's <c>ChunkData.DecompressFrom</c> (internal, by reflection).
/// One reader belongs to one thread.
/// </summary>
internal sealed class SavegameReader : ISurfaceSource, IDisposable
{
    private readonly GameDatabase _db;
    private readonly ChunkDataPool _pool;
    private readonly int _sectionsY;
    private readonly Lru<ColumnPos, ushort[]?> _heights = new(2048);
    private readonly Lru<(int, int, int), ChunkData?> _sections = new(768);

    private delegate void DecompressFrom(ChunkData data, byte[]? blocks, byte[]? light, byte[]? lightSat, byte[]? fluids, int version);
    private static readonly DecompressFrom? Decompress = BindDecompress();

    /// <summary>Why the reader can't work on this game version, or null if it can.</summary>
    public static string? Unsupported => Decompress is null ? "ChunkData.DecompressFrom(byte[], byte[], byte[], byte[], int) is gone" : null;

    private SavegameReader(GameDatabase db, ChunkDataPool pool, int sectionsY)
    {
        _db = db;
        _pool = pool;
        _sectionsY = sectionsY;
    }

    public int SectionsY => _sectionsY;

    /// <summary>Opens the server's savegame read-only. Call on the server's main thread; use the
    /// reader on one thread only.</summary>
    public static SavegameReader Open(ICoreServerAPI api)
    {
        if (Unsupported is { } why) throw new NotSupportedException(why);
        var server = (ServerMain)api.World;
        var db = new GameDatabase(api.Logger);
        string path = api.WorldManager.CurrentWorldFilepath;
        // The journal mode pragma the connection sends must match the file's, or a read-only
        // connection can't open it: WAL with corruption protection, off without.
        if (!db.OpenConnection(path, GameVersion.DatabaseVersion, out string error, requireWriteAccess: false,
                server.Config.CorruptionProtection, doIntegrityCheck: false))
        {
            db.Dispose();
            throw new IOException($"Could not open {path} for reading: {error}");
        }
        return new SavegameReader(db, new ChunkDataPool(GlobalConstants.ChunkSize, server),
            api.WorldManager.MapSizeY / GlobalConstants.ChunkSize);
    }

    public ushort[]? Heights(ColumnPos column)
    {
        if (_heights.TryGet(column, out var cached)) return cached;
        ushort[]? heights = null;
        byte[]? data = _db.GetMapChunk(column.X, column.Z);
        if (data is not null)
        {
            IMapChunk mapChunk = ServerMapChunk.FromBytes(data);
            if (mapChunk.CurrentPass >= EnumWorldGenPass.Done && mapChunk.RainHeightMap?.Length == TerrainShade.Area)
                heights = mapChunk.RainHeightMap;
        }
        _heights.Set(column, heights);
        return heights;
    }

    public bool TryBlock(ColumnPos column, int x, int y, int z, out int blockId)
    {
        blockId = 0;
        if (y < 0 || Heights(column) is null) return false;
        int cy = y / GlobalConstants.ChunkSize;
        if (cy >= _sectionsY || Section(column.X, cy, column.Z) is not { } section) return false;
        blockId = section.GetBlockId((y % GlobalConstants.ChunkSize * GlobalConstants.ChunkSize + z) * GlobalConstants.ChunkSize + x, 3);
        return true;
    }

    private ChunkData? Section(int cx, int cy, int cz)
    {
        var key = (cx, cy, cz);
        if (_sections.TryGet(key, out var cached)) return cached;
        ChunkData? section = null;
        byte[]? data = _db.GetChunk(cx, cy, cz, 0);
        if (data is not null)
        {
            StoredChunk stored;
            using (var stream = new MemoryStream(data)) stored = Serializer.Deserialize<StoredChunk>(stream);
            section = _pool.Request();
            // Light only matters to the pre-palette format (0), which unpacks it with the blocks.
            bool old = stored.CompressionVersion == 0;
            Decompress!(section, stored.Blocks, old ? stored.Light : null, old ? stored.LightSat : null, stored.Fluids,
                stored.CompressionVersion);
        }
        _sections.Set(key, section);
        return section;
    }

    public void Dispose() => _db.Dispose();

    private static DecompressFrom? BindDecompress()
    {
        var method = AccessTools.Method(typeof(ChunkData), "DecompressFrom",
            [typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(int)]);
        return method is null ? null : (DecompressFrom)Delegate.CreateDelegate(typeof(DecompressFrom), method);
    }

    /// <summary>
    /// The fields of a saved <c>Vintagestory.Server.ServerChunk</c> (protobuf) that hold its blocks:
    /// deserializing the real class would also build every entity and block entity in the chunk.
    /// The field numbers are the 1.22.7 game's; the Atlas scenarios compare what this reads with the
    /// loaded chunks.
    /// </summary>
    [ProtoContract]
    internal sealed class StoredChunk
    {
        [ProtoMember(1)] public byte[]? Blocks { get; set; }
        [ProtoMember(2)] public byte[]? Light { get; set; }
        [ProtoMember(3)] public byte[]? LightSat { get; set; }
        [ProtoMember(15)] public int CompressionVersion { get; set; }
        [ProtoMember(16)] public byte[]? Fluids { get; set; }
    }

    /// <summary>A small least-recently-used cache.</summary>
    private sealed class Lru<TKey, TValue>(int capacity) where TKey : notnull
    {
        private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
        private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

        public bool TryGet(TKey key, out TValue value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (_map.Remove(key, out var old)) _order.Remove(old);
            _map[key] = _order.AddFirst((key, value));
            if (_map.Count > capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }
}
