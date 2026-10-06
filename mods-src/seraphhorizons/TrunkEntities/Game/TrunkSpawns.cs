using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Where trunk entities come from. <see cref="Spawn"/> makes one from a trunk stack; and while trunk
/// entities run, every trunk item entity the server spawns (Logging Expanded's felling, a machine
/// broken or giving back its trunk, a station unloading onto the ground) is swapped for one as it
/// spawns (<see cref="OnEntitySpawn"/>), so code that drops a trunk may keep calling
/// <c>SpawnItemEntity</c>.
/// </summary>
public static class TrunkSpawns
{
    /// <summary>
    /// Spawns a trunk entity holding <paramref name="trunk"/> (taken as it is, not copied) with
    /// its underside's middle at <paramref name="pos"/>, lying along <paramref name="yaw"/>: the
    /// thick type for Logging Expanded's sizes xl and xxl, the thin one otherwise. Null when the
    /// stack is not a trunk or holds no logs. Server side.
    /// </summary>
    public static EntityTrunk? Spawn(IWorldAccessor world, ItemStack trunk, Vec3d pos, float yaw) => Spawn(world, trunk, pos, yaw, 0);

    /// <inheritdoc cref="Spawn(IWorldAccessor, ItemStack, Vec3d, float)"/>
    public static EntityTrunk? Spawn(IWorldAccessor world, ItemStack trunk, Vec3d pos, float yaw, int dimension)
    {
        if (world.Side != EnumAppSide.Server || !trunk.ResolveBlockOrItem(world) || !Trunks.IsTrunk(trunk) || Trunks.StoredLogs(trunk, world) <= 0)
            return null;
        var code = TrunkBox.ClassOf(trunk.Block.Variant["size"]) == TrunkClass.Thick ? TrunkEntitySystem.ThickCode : TrunkEntitySystem.ThinCode;
        if (world.GetEntityType(code) is not { } type || world.ClassRegistry.CreateEntity(type) is not EntityTrunk entity)
        {
            world.Logger.Error("[seraphhorizons] Trunk entities: entity type {0} is missing; a trunk is lost", code);
            return null;
        }
        entity.WatchedAttributes.SetItemstack(EntityTrunk.TrunkKey, trunk);
        entity.Pos.SetPos(pos);
        entity.Pos.Yaw = yaw;
        entity.Pos.Dimension = dimension;
        entity.PositionBeforeFalling.Set(pos);
        world.SpawnEntity(entity);
        return entity;
    }

    /// <summary>The server's spawn hook (and, a tick late, its load hook): a trunk item entity is removed and a trunk entity takes its
    /// place, lying the way the item was thrown (or any way, if it was not). A trunk with no logs
    /// stays an item.</summary>
    public static void OnEntitySpawn(IWorldAccessor world, Entity entity)
    {
        if (entity is not EntityItem { Alive: true } item || item.Itemstack is not { } stack || !Trunks.IsTrunk(stack)
            || Trunks.StoredLogs(stack, world) <= 0)
            return;
        var motion = item.Pos.Motion;
        float yaw = motion.X * motion.X + motion.Z * motion.Z > 1e-6
            ? (float)Math.Atan2(motion.X, motion.Z)
            : (float)(world.Rand.NextDouble() * Math.PI * 2);
        var pos = item.Pos.XYZ;
        int dimension = item.Pos.Dimension;
        item.Die(EnumDespawnReason.Removed);
        for (int i = 0; i < Math.Max(1, stack.StackSize); i++)
        {
            var one = stack.Clone();
            one.StackSize = 1;
            Spawn(world, one, pos, yaw, dimension);
        }
    }
}
