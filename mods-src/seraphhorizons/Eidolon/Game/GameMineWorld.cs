using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The world as the mine planner reads it (README "Eidolon", mining, *Stations*), from the game's block
/// accessor: a block is solid when it has a collision box there half a block high or more (what the
/// eidolon's box and its footing meet, <see cref="Solid"/>), and a block of the area, or of the layer over it, is to the order what <see cref="Rule"/> says;
/// a column is loaded when its chunk is. The land claim is not asked here (the order asks it of a block it
/// comes to, <see cref="MineOrder"/>): a block its job leaves for the order's life (<see cref="Skip"/>: one
/// its owner may not break, one that would not break) reads as <see cref="MineRule.Claimed"/>.
/// </summary>
public sealed class GameMineWorld(IWorldAccessor world, int dimension, MarkArea area, Item? pick, int maxTier, string[] mineThrough) : IMineWorld
{
    private readonly BlockPos _pos = new(dimension);
    private readonly BlockPos _near = new(dimension);

    public IReadOnlySet<MarkPos>? Skip { get; init; }

    public bool Loaded(int x, int z) =>
        world.BlockAccessor.GetChunkAtBlockPos(_pos.Set(x, Math.Clamp(area.Max.Y, 0, world.BlockAccessor.MapSizeY - 1), z)) != null;

    public MineVoxel Read(int x, int y, int z)
    {
        var blocks = world.BlockAccessor;
        if (y < 0)
            return new MineVoxel(true, MineRule.Air);
        if (y >= blocks.MapSizeY)
            return new MineVoxel(false, MineRule.Air);
        _pos.Set(x, y, z);
        var block = blocks.GetBlock(_pos);
        bool solid = block.Id != 0 && Solid(block.GetCollisionBoxes(blocks, _pos));
        bool ruled = x >= area.Min.X && x <= area.Max.X && z >= area.Min.Z && z <= area.Max.Z && y >= area.Min.Y && y <= area.Max.Y + 1;
        if (!ruled)
            return new MineVoxel(solid, MineRule.Air);
        var rule = Rule(world, _pos, block, _near, pick, maxTier, mineThrough);
        return new MineVoxel(solid, rule == MineRule.Mine && Skip?.Contains(new MarkPos(x, y, z)) == true ? MineRule.Claimed : rule);
    }

    /// <summary>Whether collision boxes make a block solid to the planner: one reaching half a block up or
    /// more (a slab, a full block); a thin cover (a snow layer, a carpet, a path's top) is walked on as the
    /// block under it, and so does not stir a new plan each time it settles.</summary>
    public static bool Solid(Cuboidf[]? boxes)
    {
        if (boxes == null)
            return false;
        foreach (var box in boxes)
            if (box != null && box.Y2 >= 0.5f)
                return true;
        return false;
    }

    /// <summary>What a block is to the order (<see cref="EidolonMining.Rule"/>), read from the world, with
    /// the land claim not asked: air, a liquid, one a liquid touches, a block entity it leaves (all but the
    /// ground blocks it mines through, <paramref name="mineThrough"/>), too hard for the pick
    /// (<paramref name="maxTier"/>, and whether <paramref name="pick"/> has a speed for its material), and
    /// whether it falls. <paramref name="near"/>: scratch, for the faces.</summary>
    public static MineRule Rule(IWorldAccessor world, BlockPos pos, Block block, BlockPos near, Item? pick, int maxTier, string[] mineThrough)
    {
        var blocks = world.BlockAccessor;
        bool liquid = block.IsLiquid() || blocks.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid();
        if (block.Id == 0 && !liquid)
            return MineRule.Air;
        bool beside = false;
        near.dimension = pos.dimension;
        foreach (var face in BlockFacing.ALLFACES)
        {
            near.Set(pos.X + face.Normali.X, pos.Y + face.Normali.Y, pos.Z + face.Normali.Z);
            if (blocks.GetBlock(near).IsLiquid() || blocks.GetBlock(near, BlockLayersAccess.Fluid).IsLiquid())
            {
                beside = true;
                break;
            }
        }
        int tier = block.GetRequiredMiningTier(world, pos);
        bool pickHas = pick?.MiningSpeed?.ContainsKey(block.GetBlockMaterial(blocks, pos)) == true;
        // A block entity is left but on the ground blocks it mines through (Desire Paths' worn soil).
        bool leaves = (block.EntityClass != null || blocks.GetBlockEntity(pos) != null)
            && EidolonMining.LeavesBlockEntity(block.Code.ToString(), mineThrough);
        var info = new MineBlock(false, liquid, beside, leaves, true,
            tier, EidolonMining.PickBreaks(tier, pickHas), block.HasBehavior<BlockBehaviorUnstableFalling>());
        return EidolonMining.Rule(info, maxTier);
    }
}
