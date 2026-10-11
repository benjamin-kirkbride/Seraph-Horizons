using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The block the eidolon carries (#676; README "Eidolon", carrying): one load at a time, in its
/// watched attributes (<see cref="LoadKey"/>) in Carry On's own carried form (<see cref="EidolonCarryOn"/>:
/// the block's stack, its block entity's data, the block it was and its turn, wall signs on it), so
/// it is saved with the entity, outlives a chunk unload, and is drawn by every client
/// (<see cref="EidolonShapeRenderer"/>, at the shape's <c>Carry</c> point).
///
/// <para>While it holds a load it walks with <c>carry-walk</c> (<see cref="IEidolonStance"/>) and stands
/// with <c>carry-idle</c>. Set down (<see cref="TryPlace"/>, the set-down order's release frame) the
/// block goes back into the world as it stood (its own block, turn and data, its contents with it).
/// Removed from the world for good (anything but an unload), it sets its load down near where it
/// stood, or drops it and its contents there (<see cref="Release"/>).</para>
///
/// <para>A load that is a container (a generic typed container: a chest, a trunk, a storage vessel)
/// opens for its owner and their company by right-click on the eidolon when no other interaction
/// takes the click (charge, oil, repair, the command tool come first): the server opens an inventory
/// on the load's own contents, which it writes back into the load as it changes, and tells the
/// client to show it (<see cref="OpenPacket"/>).</para>
/// </summary>
public class EntityBehaviorEidolonCarry(Entity entity) : EntityBehavior(entity), IEidolonStance, IEidolonCarrier
{
    public const string Code = "seraphhorizons.eidolonCarry";
    public const string LoadKey = "seraphhorizons:load";

    /// <summary>Server to client: open the carried container's dialog.</summary>
    public const int OpenPacket = 7861;

    /// <summary>The block's stand animation while it holds a load.</summary>
    public const string IdleAnimation = "carry-idle";
    public const string WalkAnimation = "carry-walk";

    private ItemStack? _stack;
    private ITreeAttribute? _stackOf;
    private InventoryGeneric? _inv;
    private GuiDialogCreatureContents? _dialog;

    /// <summary>Set while a one-shot (lift, setdown) plays, so the stand animation keeps off.</summary>
    public bool Busy { get; set; }

    public override string PropertyName() => Code;

    /// <summary>The load (Carry On's carried form), or null.</summary>
    public ITreeAttribute? Load => entity.WatchedAttributes.GetTreeAttribute(LoadKey) is { } tree && tree.HasAttribute("Stack") ? tree : null;

    public bool Carrying => Load != null;

    /// <summary>The load's block stack, resolved (cached while the load is the same).</summary>
    public ItemStack? LoadStack
    {
        get
        {
            var load = Load;
            if (load == null)
                return _stack = null;
            if (ReferenceEquals(load, _stackOf) && _stack != null)
                return _stack;
            _stackOf = load;
            _stack = load.GetItemstack("Stack");
            if (_stack != null && !_stack.ResolveBlockOrItem(entity.World))
                _stack = null;
            return _stack;
        }
    }

    /// <summary>The load's block entity data, or null.</summary>
    public ITreeAttribute? LoadData => Load?["Data"] as ITreeAttribute;

    public string? MoveAnimation(bool run) => Carrying ? WalkAnimation : null;

    // ---- holding ----

    /// <summary>Takes <paramref name="load"/> (Carry On's carried form) as its load. Server side.</summary>
    public void Hold(ITreeAttribute load)
    {
        entity.WatchedAttributes[LoadKey] = load;
        entity.WatchedAttributes.MarkPathDirty(LoadKey);
    }

    private void Clear()
    {
        CloseForAll();
        entity.WatchedAttributes.RemoveAttribute(LoadKey);
        if (entity.AnimManager.IsAnimationActive(IdleAnimation))
            entity.AnimManager.StopAnimation(IdleAnimation);
    }

    /// <summary>The block the load is set down as: the one it was taken as when it still exists (its
    /// turn, a variant), else its stack's.</summary>
    public Block? PlacedBlock()
    {
        if (LoadStack is not { Block: { } block })
            return null;
        if (Load?.GetString("OriginalBlockCode") is { } code && entity.World.GetBlock(new AssetLocation(code)) is { Id: > 0 } original)
            return original;
        return block;
    }

    /// <summary>Whether the load could be set down at <paramref name="pos"/> now (as the game asks a
    /// player's placing, with no player: the cell free, no other creature in it, a multiblock's room).</summary>
    public bool CanPlace(BlockPos pos, out string reason)
    {
        reason = "nothing";
        var world = entity.World;
        if (PlacedBlock() is not { } block)
            return false;
        reason = "occupied";
        if (!world.BlockAccessor.IsValidPos(pos) || !world.BlockAccessor.GetBlock(pos).IsReplacableBy(block))
            return false;
        var sel = new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0, 0.5) };
        string failure = "";
        try
        {
            if (!block.CanPlaceBlock(world, null, sel, ref failure))
            {
                reason = failure == "entityintersecting" ? "entity" : "occupied";
                return false;
            }
        }
        catch (Exception e)
        {
            // A behaviour that wants a player: the cell is free, which is what counts here.
            world.Logger.VerboseDebug("[seraphhorizons] Eidolon: {0}.CanPlaceBlock without a player: {1}", block.Code, e.Message);
        }
        reason = "";
        return true;
    }

    /// <summary>
    /// Sets the load down at <paramref name="pos"/> as it stood when taken: its own block (turned as it
    /// was), its block entity's data (a container's contents) and the wall signs that were on it, and
    /// clears the load; false (and nothing changes) when it cannot go there (<see cref="CanPlace"/>).
    /// Needs no Carry On. Server side.
    /// </summary>
    public bool TryPlace(BlockPos pos, out string reason)
    {
        if (!CanPlace(pos, out reason) || Load is not { } load || LoadStack is not { } stack || PlacedBlock() is not { } block)
            return false;
        var world = entity.World;
        world.BlockAccessor.SetBlock(block.Id, pos, stack.Clone());
        Restore(world, pos, load["Data"] as ITreeAttribute, load.HasAttribute("OriginalMeshAngle") ? load.GetFloat("OriginalMeshAngle") : null);
        if (load["Children"] is ITreeAttribute children)
            PlaceChildren(world, pos, children);
        world.BlockAccessor.MarkBlockDirty(pos);
        world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        if (block.Sounds is { } sounds && sounds.Place.Location is { } sound)
            world.PlaySoundAt(sound, pos.X + 0.5, pos.Y, pos.Z + 0.5, null);
        world.Logger.Audit("[seraphhorizons] Eidolon {0} set down {1} at {2}", entity.EntityId, block.Code, pos);
        Clear();
        return true;
    }

    private static void Restore(IWorldAccessor world, BlockPos pos, ITreeAttribute? data, float? meshAngle)
    {
        if (data == null || world.BlockAccessor.GetBlockEntity(pos) is not { } be)
            return;
        var tree = data.Clone();
        tree.SetInt("posx", pos.X);
        tree.SetInt("posy", pos.Y);
        tree.SetInt("posz", pos.Z);
        if (meshAngle is { } angle)
            tree.SetFloat("meshAngle", angle);
        be.FromTreeAttributes(tree, world);
        be.MarkDirty(true);
    }

    // Carry On's wall signs (CarryAttachedWallSigns): each at its offset from the block, unturned since
    // the block is set down as it stood; one with no room is dropped.
    private static void PlaceChildren(IWorldAccessor world, BlockPos pos, ITreeAttribute children)
    {
        foreach (var entry in children)
        {
            if (entry.Value is not ITreeAttribute child || child.GetItemstack("Stack") is not { } stack || !stack.ResolveBlockOrItem(world) || stack.Block == null)
                continue;
            var at = pos.AddCopy(child.GetInt("OffsetX"), child.GetInt("OffsetY"), child.GetInt("OffsetZ"));
            var block = child.GetString("OriginalBlockCode") is { } code && world.GetBlock(new AssetLocation(code)) is { Id: > 0 } original ? original : stack.Block;
            if (!world.BlockAccessor.GetBlock(at).IsReplacableBy(block))
            {
                world.SpawnItemEntity(stack, at.ToVec3d().Add(0.5, 0.5, 0.5));
                continue;
            }
            world.BlockAccessor.SetBlock(block.Id, at, stack);
            Restore(world, at, child["Data"] as ITreeAttribute, child.HasAttribute("OriginalMeshAngle") ? child.GetFloat("OriginalMeshAngle") : null);
            world.BlockAccessor.MarkBlockDirty(at);
        }
    }

    /// <summary>
    /// Lets go of the load where it stands (it is leaving the world for good): set down on the
    /// nearest free cell with ground under it within 3 blocks, else dropped there as items, the block
    /// and its contents. Server side.
    /// </summary>
    public void Release()
    {
        if (Load is not { } load || entity.Api.Side != EnumAppSide.Server)
            return;
        var world = entity.World;
        var feet = entity.Pos.AsBlockPos;
        for (int r = 0; r <= 3; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                continue;
            foreach (int dy in new[] { 0, 1, -1, 2 })
            {
                var at = feet.AddCopy(dx, dy, dz);
                if (world.BlockAccessor.GetBlock(at.DownCopy()).SideSolid[BlockFacing.UP.Index] && TryPlace(at, out _))
                    return;
            }
        }
        var where = entity.Pos.XYZ.Add(0, 0.5, 0);
        if (LoadStack is { } stack)
            world.SpawnItemEntity(stack.Clone(), where);
        if (Contents(load) is { } contents)
            foreach (var slot in contents)
                if (!slot.Empty)
                    world.SpawnItemEntity(slot.Itemstack, where);
        world.Logger.Audit("[seraphhorizons] Eidolon {0} dropped its load {1} at {2}", entity.EntityId, LoadStack?.Collectible?.Code, feet);
        Clear();
    }

    /// <summary>The load's contents as an inventory (not opened to anyone), or null when it holds none.</summary>
    private InventoryGeneric? Contents(ITreeAttribute load)
    {
        if ((load["Data"] as ITreeAttribute)?.GetTreeAttribute("inventory") is not { } tree || tree.GetInt("qslots") <= 0)
            return null;
        var inv = new InventoryGeneric(tree.GetInt("qslots"), "eidolonloaddrop-" + entity.EntityId, entity.Api);
        inv.FromTreeAttributes(tree);
        return inv;
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);
        if (entity.Api.Side == EnumAppSide.Client)
        {
            _dialog?.TryClose();
            return;
        }
        if (despawn?.Reason is EnumDespawnReason.Unload or EnumDespawnReason.Disconnect)
        {
            CloseForAll();
            return;
        }
        Release();
    }

    // ---- standing with it ----

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server)
            return;
        var anims = entity.AnimManager;
        bool slumped = entity.WatchedAttributes.GetBool(EntityLaborEidolon.SlumpedKey);
        bool stand = Carrying && !Busy && !slumped && !anims.IsAnimationActive(WalkAnimation);
        if (stand && !anims.IsAnimationActive(IdleAnimation))
            anims.StartAnimation(IdleAnimation);
        else if (!stand && anims.IsAnimationActive(IdleAnimation))
            anims.StopAnimation(IdleAnimation);
    }

    // ---- a carried container ----

    /// <summary>Whether the load is a container that opens while carried: a generic typed container
    /// (chest, trunk, storage vessel) with an inventory in its data.</summary>
    public bool CarriesContainer()
    {
        if (PlacedBlock() is not { EntityClass: { } beClass } || LoadData?.GetTreeAttribute("inventory") is not { } inv || inv.GetInt("qslots") <= 0)
            return false;
        var type = entity.Api.ClassRegistry.GetBlockEntity(beClass);
        return type != null && typeof(BlockEntityGenericTypedContainer).IsAssignableFrom(type);
    }

    /// <summary>A carried container's contents (server side), for the jobs that take from it
    /// (<see cref="IEidolonCarrier"/>: the fell order's replanting); taking out writes back into the load.</summary>
    public IInventory? CarriedInventory => entity.Api?.Side == EnumAppSide.Server ? Inventory() : null;

    private string InventoryId => "eidolonload-" + entity.EntityId;

    /// <summary>The carried container's contents as an inventory (this side's, made on first use).</summary>
    private InventoryGeneric? Inventory()
    {
        if (_inv != null)
            return _inv;
        if (LoadData?.GetTreeAttribute("inventory") is not { } tree || tree.GetInt("qslots") <= 0)
            return null;
        _inv = new InventoryGeneric(tree.GetInt("qslots"), InventoryId, entity.Api);
        _inv.FromTreeAttributes(tree);
        if (entity.Api.Side == EnumAppSide.Server)
            _inv.SlotModified += _ => WriteBack();
        return _inv;
    }

    // Its contents back into the load, as they change (server side).
    private void WriteBack()
    {
        if (_inv == null || LoadData is not { } data)
            return;
        var tree = new TreeAttribute();
        _inv.ToTreeAttributes(tree);
        data["inventory"] = tree;
        entity.WatchedAttributes.MarkPathDirty(LoadKey);
    }

    private void CloseForAll()
    {
        if (_inv == null)
            return;
        if (entity.Api is ICoreServerAPI sapi)
            foreach (var uid in _inv.openedByPlayerGUIds.ToList())
                if (sapi.World.PlayerByUid(uid) is IServerPlayer player)
                {
                    player.InventoryManager.CloseInventory(_inv);
                    sapi.Network.SendEntityPacket(player, entity.EntityId, OpenPacket, [0]);
                }
        _inv = null;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || !itemslot.Empty || byEntity is not EntityPlayer || !CarriesContainer())
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity.Api.Side != EnumAppSide.Server || entity is not EntityLaborEidolon eidolon
            || (byEntity as EntityPlayer)?.Player is not IServerPlayer player || !eidolon.RefuseUnlessCommander(player))
            return;
        if (Inventory() is not { } inv)
            return;
        player.InventoryManager.OpenInventory(inv);
        ((ICoreServerAPI)entity.Api).Network.SendEntityPacket(player, entity.EntityId, OpenPacket, [1]);
    }

    public override void OnReceivedClientPacket(IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled)
    {
        if (packetid >= 1000 || _inv == null)
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity is not EntityLaborEidolon eidolon || !eidolon.MayCommand(player) || !Carrying
            || player.Entity.Pos.DistanceTo(entity.Pos.XYZ) > 8)
        {
            _inv.InvNetworkUtil.SendInventoryRollback(player, packetid, data);
            return;
        }
        _inv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data, ref EnumHandling handled)
    {
        if (packetid != OpenPacket || entity.Api is not ICoreClientAPI capi)
            return;
        handled = EnumHandling.PreventSubsequent;
        if (data.Length == 0 || data[0] == 0)
        {
            _dialog?.TryClose();
            _inv = null;
            return;
        }
        if (_dialog != null)
            return;
        _inv = null;
        if (Inventory() is not { } inv)
            return;
        var player = capi.World.Player;
        player.InventoryManager.OpenInventory(inv);
        _dialog = new GuiDialogCreatureContents(inv, entity, capi, "seraphhorizons-eidolonload", LoadStack?.GetName());
        _dialog.OnClosed += () =>
        {
            _dialog?.Dispose();
            _dialog = null;
            _inv = null;
        };
        if (_dialog.TryOpen())
            capi.Network.SendPacketClient(inv.Open(player));
    }

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled) =>
        CarriesContainer()
            ? [new WorldInteraction { ActionLangCode = "seraphhorizons:eidolon-help-openload", MouseButton = EnumMouseButton.Right, RequireFreeHand = true }]
            : null;

    public override void GetInfoText(StringBuilder infotext)
    {
        if (LoadStack is { } stack)
            infotext.AppendLine(Lang.Get("seraphhorizons:eidolon-info-load", stack.GetName()));
    }
}
