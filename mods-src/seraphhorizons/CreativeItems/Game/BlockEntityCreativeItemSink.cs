using System.Text;
using SeraphHorizons.Mod.CreativeItems.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CreativeItems;

/// <summary>
/// The item sink: a container to the game's chutes and hoppers (they push into a
/// <see cref="BlockEntityContainer"/>'s inventory, at the slot <c>GetAutoPushIntoSlot</c> gives),
/// whose one slot (<see cref="CreativeItemSinkInventory"/>) takes any stack from any face. What
/// lands in it is counted by item (<see cref="ItemTally"/>) and voided the next time anything pushes
/// into the slot, or on the next tick, whichever is first: not at once, since the game's chute
/// still reads the stack it pushed. So the slot is always empty to a pusher, and the sink never
/// fills. Nothing pulls from it.
/// </summary>
public class BlockEntityCreativeItemSink : BlockEntityContainer, ICreativeItemsBlockEntity
{
    public const int TickMs = 200;

    private const long SyncMs = 1000;

    private readonly CreativeItemSinkInventory _inventory;
    private readonly ItemTally _tally = new();
    private RateWindow? _window;
    private long _syncedAt;
    private long _syncedTotal;
    private double _syncedRate;
    private double _perMinute;
    private GuiDialogCreativeItemSink? _dialog;

    public BlockEntityCreativeItemSink() => _inventory = new CreativeItemSinkInventory(OnReceived);

    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => CreativeItemBlocks.SinkCode;

    public ItemTally Tally => _tally;

    public double PerMinute => Api?.Side == EnumAppSide.Server && _window != null ? _window.PerMinute(Now) : _perMinute;

    private double Now => Api.World.ElapsedMilliseconds / 1000.0;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            _window = new RateWindow(Now);
            RegisterGameTickListener(OnServerTick, TickMs);
        }
    }

    private void OnServerTick(float dt)
    {
        _inventory.Drain();
        SyncIfChanged();
    }

    /// <summary>The tally's key for a stack: its class and code.</summary>
    public static string KeyOf(ItemStack stack) =>
        (stack.Class == EnumItemClass.Block ? "block:" : "item:") + stack.Collectible.Code;

    /// <summary>The stack a key names, for its name (null if it no longer exists).</summary>
    public static ItemStack? StackOf(IWorldAccessor world, string key)
    {
        int colon = key.IndexOf(':');
        if (colon < 0)
            return null;
        var code = new AssetLocation(key[(colon + 1)..]);
        CollectibleObject? collectible = key[..colon] == "block" ? world.GetBlock(code) : world.GetItem(code);
        return collectible == null ? null : new ItemStack(collectible);
    }

    private void OnReceived(ItemStack stack)
    {
        if (Api?.Side != EnumAppSide.Server || stack.Collectible == null || stack.StackSize <= 0)
            return;
        _tally.Add(KeyOf(stack), stack.StackSize);
        _window?.Add(Now, stack.StackSize);
    }

    /// <summary>Zeroes every count and the rate.</summary>
    public void ResetCounts()
    {
        _inventory.Drain();
        _tally.Reset();
        _window?.Reset(Now);
        MarkDirty();
    }

    /// <summary>Counts and voids whatever is in the slot now (for tests: the server does it each tick).</summary>
    public void Drain() => _inventory.Drain();

    private void SyncIfChanged()
    {
        long now = Api.World.ElapsedMilliseconds;
        double rate = Math.Round(PerMinute, 1);
        if ((_tally.Total == _syncedTotal && rate == _syncedRate) || now - _syncedAt < SyncMs)
            return;
        MarkDirty(false);
    }

    public override void MarkDirty(bool redrawOnClient = false, IPlayer? skipPlayer = null)
    {
        if (Api?.Side == EnumAppSide.Server)
        {
            _syncedAt = Api.World.ElapsedMilliseconds;
            _syncedTotal = _tally.Total;
            _syncedRate = Math.Round(PerMinute, 1);
        }
        base.MarkDirty(redrawOnClient, skipPlayer);
    }

    public void ToggleDialog()
    {
        if (Api is not ICoreClientAPI capi)
            return;
        if (_dialog != null)
        {
            _dialog.TryClose();
            return;
        }
        _dialog = new GuiDialogCreativeItemSink(this, capi);
        _dialog.OnClosed += () => _dialog = null;
        if (!_dialog.TryOpen())
            _dialog = null;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
        if (packetid == CreativeItemBlocks.PacketReset && CreativeItemBlocks.CanUse(Api.World, fromPlayer, Pos))
            ResetCounts();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var counts = new TreeAttribute();
        foreach (var (key, count) in _tally.Ordered())
            counts.SetLong(key, count);
        tree["counts"] = counts;
        tree.SetDouble("perMinute", PerMinute);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var saved = new List<KeyValuePair<string, long>>();
        if (tree["counts"] is ITreeAttribute counts)
            foreach (var (key, value) in counts)
                if (value is LongAttribute count)
                    saved.Add(new(key, count.value));
        _tally.Restore(saved);
        double perMinute = tree.GetDouble("perMinute");
        _perMinute = double.IsFinite(perMinute) ? Math.Max(0, perMinute) : 0;
        _dialog?.Refresh();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        // Not the container's: its perish rate means nothing here.
        dsc.AppendLine(Lang.Get("seraphhorizons:creativeitemsink-total", _tally.Total));
        dsc.AppendLine(Lang.Get("seraphhorizons:creativeitems-rate", PerMinute.ToString("0.#")));
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        // Count what is in the slot, so the container drops nothing.
        _inventory.Drain();
        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        _inventory.Drain();
        base.OnBlockRemoved();
        _dialog?.TryClose();
    }

    public override void OnBlockUnloaded()
    {
        _inventory.Drain();
        base.OnBlockUnloaded();
        _dialog?.TryClose();
    }
}

/// <summary>
/// The sink's one slot, open to any stack from any face, never to a pull. Whatever a pusher puts
/// in stays until <see cref="Drain"/> hands it to the sink and empties the slot; every push asks for
/// the slot first (<see cref="GetAutoPushIntoSlot"/>), which drains it, so it is empty for each
/// one. Nothing is saved: the slot's stack is counted when it lands or is lost with the block.
/// </summary>
public class CreativeItemSinkInventory : InventoryBase
{
    private readonly ItemSlot _slot;
    private readonly Action<ItemStack> _received;

    public CreativeItemSinkInventory(Action<ItemStack> received) : base(CreativeItemBlocks.SinkCode, "0", null)
    {
        _received = received;
        _slot = new ItemSlotCreativeSink(this);
    }

    public override int Count => 1;

    public override ItemSlot this[int slotId]
    {
        get => _slot;
        set { }
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        Drain();
        return _slot;
    }

    public override ItemSlot? GetAutoPullFromSlot(BlockFacing atBlockFace) => null;

    /// <summary>Hands the slot's stack to the sink and empties the slot.</summary>
    public void Drain()
    {
        if (_slot.Itemstack is not { } stack)
            return;
        _slot.Itemstack = null;
        _received(stack);
    }

    public override void FromTreeAttributes(ITreeAttribute tree) { }

    public override void ToTreeAttributes(ITreeAttribute tree) { }
}

/// <summary>The sink's slot: takes any item, as many as come.</summary>
public class ItemSlotCreativeSink(InventoryBase inventory) : ItemSlot(inventory)
{
    public override int MaxSlotStackSize => int.MaxValue;

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) =>
        sourceSlot.Itemstack?.Collectible != null && (Itemstack == null
            || Itemstack.Collectible.GetMergableQuantity(Itemstack, sourceSlot.Itemstack, priority) > 0);

    public override bool CanHold(ItemSlot sourceSlot) => sourceSlot.Itemstack?.Collectible != null;

    public override bool CanTake() => false;
}
