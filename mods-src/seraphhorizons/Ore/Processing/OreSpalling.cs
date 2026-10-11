using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Spalling (#747; README "Ore processing: spalling"): raw ore and chunks set down one at a time on
/// the ground (ground storage, one item a block, <c>patches/oreprocessing-ore.json</c>) are broken
/// where they lie with a hammer, a left-click a blow, into crushed ore by the 5-unit rule (the item's
/// own crushing, <see cref="OreProcessingItems"/>), with no loss. The server counts the blows (in
/// memory: an ore left half struck and unloaded starts again) and wears the hammer a blow.
/// </summary>
public class OreSpalling(ICoreServerAPI api, SpallingConfig config)
{
    public static readonly AssetLocation HitSound = new("game", "sounds/block/rock-hit-pickaxe");
    public static readonly AssetLocation BreakSound = new("game", "sounds/block/rock-break-pickaxe");

    public SpallingConfig Config { get; } = config;

    private readonly Dictionary<BlockPos, (ItemStack Ore, int Blows)> _struck = new();
    private readonly Dictionary<string, long> _lastBlow = new(StringComparer.Ordinal);

    /// <summary>The blows struck on the ore at <paramref name="pos"/> so far (0 if none or another ore).</summary>
    public int BlowsAt(BlockPos pos) =>
        _struck.TryGetValue(pos, out var s) && Target(api.World, pos) is { } slot && ReferenceEquals(slot.Itemstack, s.Ore) ? s.Blows : 0;

    /// <summary>Blows the ore in <paramref name="slot"/> takes in all.</summary>
    public int BlowsNeeded(ItemSlot slot) =>
        Config.BlowsFor(OreProducts.FormOfGrade(slot.Itemstack?.Collectible.Variant["grade"]));

    /// <summary>
    /// The ground storage slot at <paramref name="pos"/> holding an ore that spalls, or null. On the
    /// server it must also have its crushing (set with ore processing on).
    /// </summary>
    public static ItemSlot? Target(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage be)
            return null;
        foreach (var slot in be.Inventory)
        {
            if (slot.Empty)
                continue;
            if (slot.Itemstack.Collectible is not ItemGradedOre { Spalls: true } ore)
                return null;
            if (world.Side == EnumAppSide.Server && ore.CrushingProps?.CrushedStack?.ResolvedItemstack == null)
                return null;
            return slot;
        }
        return null;
    }

    /// <summary>
    /// One blow by <paramref name="player"/> with the hammer in <paramref name="hammer"/> on the ore at
    /// <paramref name="pos"/>: counted, the hammer worn, and on the last blow the ore taken and its
    /// crushed ore dropped where it lay. Returns whether a blow was struck (false for no ore there, no
    /// access, or a blow too soon after the last).
    /// </summary>
    public bool Blow(IPlayer player, ItemSlot hammer, BlockPos pos)
    {
        var world = api.World;
        if (Target(world, pos) is not { } slot || world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage be)
            return false;
        if (!world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak))
            return false;
        long now = world.ElapsedMilliseconds;
        if (_lastBlow.TryGetValue(player.PlayerUID, out long last) && now - last < Spalling.BlowIntervalMs)
            return false;
        _lastBlow[player.PlayerUID] = now;

        var ore = slot.Itemstack!;
        var key = pos.Copy();
        int before = _struck.TryGetValue(key, out var s) && ReferenceEquals(s.Ore, ore) ? s.Blows : 0;
        var (blows, breaks) = Spalling.Strike(before, BlowsNeeded(slot));
        if (Config.HammerWearPerBlow > 0 && !hammer.Empty)
            hammer.Itemstack.Collectible.DamageItem(world, player.Entity, hammer, Config.HammerWearPerBlow);

        var centre = new Vec3d(pos.X + 0.5, pos.Y + 0.15, pos.Z + 0.5);
        if (!breaks)
        {
            _struck[key] = (ore, blows);
            world.PlaySoundAt(HitSound, centre.X, centre.Y, centre.Z, null, true, 16f);
            return true;
        }

        _struck.Remove(key);
        var oreCode = ore.Collectible.Code;
        var crushed = ore.Collectible.CrushingProps!.CrushedStack.ResolvedItemstack.Clone();
        slot.TakeOut(1);
        slot.MarkDirty();
        world.SpawnItemEntity(crushed, centre, new Vec3d(0, 0.1, 0));
        world.PlaySoundAt(BreakSound, centre.X, centre.Y, centre.Z, null, true, 16f);
        world.Logger.Audit("{0} spalled 1x{1} into {2}x{3} at {4}.", player.PlayerName, oreCode,
            crushed.StackSize, crushed.Collectible.Code, pos);
        if (be.Inventory.Empty)
        {
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }
        else
        {
            be.MarkDirty(true);
        }
        Prune(now);
        return true;
    }

    // Ores struck and left: forgotten after ten minutes, so the table does not grow.
    private void Prune(long now)
    {
        if (_lastBlow.Count > 64)
            foreach (var uid in _lastBlow.Where(kv => now - kv.Value > 600_000).Select(kv => kv.Key).ToList())
                _lastBlow.Remove(uid);
        if (_struck.Count > 256)
            foreach (var pos in _struck.Keys.Where(p => api.World.BlockAccessor.GetBlockEntity(p) is not BlockEntityGroundStorage).ToList())
                _struck.Remove(pos);
    }

    /// <summary>
    /// Shift + right-click on a placed ore with another in hand: the one in hand goes on the next
    /// block (beside the face clicked, or for the top face the next one the player faces), not on
    /// top. Returns whether it was set down.
    /// </summary>
    public static bool PlaceBeside(IWorldAccessor world, BlockPos placed, IPlayer player, BlockSelection blockSel)
    {
        var face = blockSel.Face is { IsHorizontal: true } f ? f : BlockFacing.HorizontalFromYaw(player.Entity.Pos.Yaw);
        var target = placed.AddCopy(face);
        var below = target.DownCopy();
        if (world.GetBlock(new AssetLocation("groundstorage")) is not BlockGroundStorage storage
            || world.BlockAccessor.GetBlock(target).Replaceable < 6000
            || !world.BlockAccessor.GetBlock(below).CanAttachBlockAt(world.BlockAccessor, storage, below, BlockFacing.UP))
        {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(world, "spallingnoroom", Lang.Get("seraphhorizons:spalling-noroom"));
            return false;
        }
        return storage.CreateStorage(world, new BlockSelection { Position = below, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) }, player);
    }
}

/// <summary>
/// On the game's hammers with ore processing on (added on the server, sent to clients with the items):
/// a left-click on an ore set down on the ground is a blow (<see cref="OreSpalling"/>), not a block
/// break. It runs before the hammer's own swing (<c>AnimationAuthoritative</c>), which still plays.
/// </summary>
public class CollectibleBehaviorSpalling(CollectibleObject collObj) : CollectibleBehavior(collObj)
{
    public const string Name = "seraphhorizons.OreSpalling";

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
        ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        if (blockSel?.Position == null || byEntity is not EntityPlayer { Player: { } player } || entitySel != null)
            return;
        var world = byEntity.World;
        if (OreSpalling.Target(world, blockSel.Position) is not { } ore)
            return;
        // No block break: the swing plays, and the server counts the blow.
        handHandling = EnumHandHandling.PreventDefaultAction;
        if (world.Side == EnumAppSide.Client)
        {
            world.SpawnCubeParticles(blockSel.Position.ToVec3d().Add(blockSel.HitPosition), ore.Itemstack, 0.25f, 4, 0.5f, player, new Vec3f(0, 1, 0));
            return;
        }
        if (byEntity.Api.ModLoader.GetModSystem<OreProcessingSystem>()?.Spalling is { } spalling)
            spalling.Blow(player, slot, blockSel.Position);
    }
}
