using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// The block accessors stable veins (#946) hand Interesting Ore Gen's vein generator instead of the
/// worldgen one. The generator reaches the world only through <c>IsValidPos</c>,
/// <c>GetChunkAtBlockPos</c> (then the chunk's <c>Data</c>: <c>GetBlockIdUnsafe</c>,
/// <c>SetBlockUnsafe</c>, <c>SetFluid</c>), <c>GetBlock</c> and <c>MapSizeY</c> (IOG 2.3.8), so
/// both are <see cref="DispatchProxy"/>s that answer those and refuse anything else (a new IOG
/// that calls more shows up as an error, not as a silent difference).
/// <list type="bullet">
/// <item><see cref="HostRockWorld"/>: every block in the world is one host rock, and writes are
/// kept, in order, in <see cref="HostRockWorld.Writes"/>. A vein replayed in it draws the same
/// random numbers in every pass, whatever is loaded or already placed.</item>
/// <item><see cref="ColumnWorld"/>: the real worldgen accessor, but writes outside the chunk
/// column being generated are dropped, so a vein of an unmanaged ore (gems, quartz, olivine) never
/// writes into a column other than the one whose pass placed it.</item>
/// </list>
/// </summary>
public static class StableVeinWorlds
{
    /// <summary>A world of one host rock, recording what is written into it.</summary>
    public sealed class HostRockWorld
    {
        public readonly int HostId;
        public readonly int MapSizeY;
        public readonly List<(int X, int Y, int Z, int Id)> Writes = new();
        private readonly Dictionary<long, int> _blocks = new();
        private readonly Dictionary<long, IServerChunk> _chunks = new();
        private readonly IList<Block> _blockList;
        public readonly IBlockAccessor Accessor;

        public HostRockWorld(int hostId, int mapSizeY, IList<Block> blocks)
        {
            HostId = hostId;
            MapSizeY = mapSizeY;
            _blockList = blocks;
            var a = DispatchProxy.Create<IBlockAccessor, AccessorProxy>();
            ((AccessorProxy)(object)a).Valid = Valid;
            ((AccessorProxy)(object)a).Chunk = Chunk;
            ((AccessorProxy)(object)a).BlockAt = p => _blockList[Get(p.X, p.Y, p.Z)];
            ((AccessorProxy)(object)a).SizeY = mapSizeY;
            Accessor = a;
        }

        private static long Key(int x, int y, int z) => ((long)x << 36) ^ ((long)z << 10) ^ (uint)y;

        public int Get(int x, int y, int z) => _blocks.TryGetValue(Key(x, y, z), out var id) ? id : HostId;

        private void Set(int x, int y, int z, int id)
        {
            _blocks[Key(x, y, z)] = id;
            Writes.Add((x, y, z, id));
        }

        private bool Valid(BlockPos p) => p.Y >= 0 && p.Y < MapSizeY && p.X >= 0 && p.Z >= 0;

        private IServerChunk? Chunk(BlockPos p)
        {
            if (!Valid(p)) return null;
            int cx = p.X >> 5, cy = p.Y >> 5, cz = p.Z >> 5;
            long key = ((long)cx << 32) ^ ((long)cz << 8) ^ (uint)cy;
            if (_chunks.TryGetValue(key, out var chunk)) return chunk;
            var data = DispatchProxy.Create<IChunkBlocks, BlocksProxy>();
            var bp = (BlocksProxy)(object)data;
            bp.GetId = i => Get(cx * 32 + (i & 31), cy * 32 + (i >> 10), cz * 32 + ((i >> 5) & 31));
            bp.SetId = (i, id) => Set(cx * 32 + (i & 31), cy * 32 + (i >> 10), cz * 32 + ((i >> 5) & 31), id);
            var c = DispatchProxy.Create<IServerChunk, ChunkProxy>();
            ((ChunkProxy)(object)c).Blocks = data;
            return _chunks[key] = c;
        }
    }

    /// <summary>The worldgen accessor with writes kept to one chunk column (see the class).</summary>
    public static IBlockAccessor ColumnWorld(IBlockAccessor real, int chunkX, int chunkZ)
    {
        var a = DispatchProxy.Create<IBlockAccessor, AccessorProxy>();
        var proxy = (AccessorProxy)(object)a;
        proxy.Valid = real.IsValidPos;
        proxy.SizeY = real.MapSizeY;
        proxy.BlockAt = p => real.GetBlock(p);
        proxy.Chunk = p =>
        {
            if (real.GetChunkAtBlockPos(p) is not IServerChunk chunk) return null;
            if (p.X >> 5 == chunkX && p.Z >> 5 == chunkZ) return chunk;
            // Another column: read it as IOG would, write nothing. No data: as IOG sees it, no chunk.
            if (chunk.Data is not { } real3d) return null;
            var data = DispatchProxy.Create<IChunkBlocks, BlocksProxy>();
            var bp = (BlocksProxy)(object)data;
            bp.GetId = i => real3d.GetBlockIdUnsafe(i);
            bp.SetId = (_, _) => { };
            var c = DispatchProxy.Create<IServerChunk, ChunkProxy>();
            ((ChunkProxy)(object)c).Blocks = data;
            return c;
        };
        return a;
    }

    public class AccessorProxy : DispatchProxy
    {
        public System.Func<BlockPos, bool> Valid = _ => false;
        public System.Func<BlockPos, IServerChunk?> Chunk = _ => null;
        public System.Func<BlockPos, Block> BlockAt = _ => throw new InvalidOperationException();
        public int SizeY;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case nameof(IBlockAccessor.IsValidPos) when args is [BlockPos p]: return Valid(p);
                case nameof(IBlockAccessor.GetChunkAtBlockPos) when args is [BlockPos p]: return Chunk(p);
                case nameof(IBlockAccessor.GetBlock) when args is [BlockPos p]: return BlockAt(p);
                case "get_" + nameof(IBlockAccessor.MapSizeY): return SizeY;
            }
            throw new NotSupportedException($"stable veins: IBlockAccessor.{method?.Name} is not supported in a vein replay");
        }
    }

    public class ChunkProxy : DispatchProxy
    {
        public IChunkBlocks? Blocks;

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == "get_" + nameof(IWorldChunk.Data)
                ? Blocks
                : throw new NotSupportedException($"stable veins: IServerChunk.{method?.Name} is not supported in a vein replay");
    }

    public class BlocksProxy : DispatchProxy
    {
        public System.Func<int, int> GetId = _ => 0;
        public Action<int, int> SetId = (_, _) => { };

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case nameof(IChunkBlocks.GetBlockIdUnsafe) when args is [int i]: return GetId(i);
                case nameof(IChunkBlocks.SetBlockUnsafe) when args is [int i, int id]:
                    SetId(i, id);
                    return null;
                case nameof(IChunkBlocks.SetFluid): return null; // the fluid layer: the apply clears it where it places
            }
            throw new NotSupportedException($"stable veins: IChunkBlocks.{method?.Name} is not supported in a vein replay");
        }
    }
}
