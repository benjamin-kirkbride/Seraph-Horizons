using System.Text;
using SeraphHorizons.Mod.CreativeItems.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CreativeItems;

/// <summary>
/// The item source: a copy of one item (its code and every attribute, its temperature kept as it
/// was when copied) and the feed's settings (<see cref="ItemFeed"/>). Every <see cref="TickMs"/>
/// the server puts what is due into the hopper directly under it (the game's hopper, not a chute
/// or a crate), as a chute would: into the slot its inventory finds best for the stack, as much as
/// that slot takes. Not a container: chutes and hoppers neither push into it nor pull from it, and
/// breaking it drops nothing.
/// </summary>
public class BlockEntityCreativeItemSource : BlockEntity, ICreativeItemsBlockEntity
{
    public const int TickMs = 200;

    // How often the server sends the count and rate to clients while they change.
    private const long SyncMs = 1000;

    private readonly ItemFeed _feed = new();
    private RateWindow? _window;
    private long _syncedAt;
    private long _syncedTotal;
    private double _syncedRate;
    // Client side: the rate as the server last sent it, and the open dialog.
    private double _perMinute;
    private GuiDialogCreativeItemSource? _dialog;

    /// <summary>The copied item (stack size 1), or null.</summary>
    public ItemStack? Item { get; private set; }

    public ItemFeed Feed => _feed;

    /// <summary>Items per minute over the last minute (on a client, as last sent).</summary>
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
        long due = _feed.Due(dt);
        if (due > 0 && Item != null)
        {
            int given = FeedHopper(due);
            _feed.Delivered(given);
            _window!.Add(Now, given);
        }
        SyncIfChanged();
    }

    /// <summary>Puts up to <paramref name="due"/> copies of the item into the hopper under the
    /// source, filling its slots as the game's chutes do; returns how many went in.</summary>
    private int FeedHopper(long due)
    {
        var below = Pos.DownCopy();
        if (Api.World.BlockAccessor.GetBlock(below) is not BlockHopper
            || Api.World.BlockAccessor.GetBlockEntity(below) is not BlockEntityItemFlow hopper)
            return 0;
        int given = 0;
        while (given < due)
        {
            var stack = Copy((int)Math.Min(due - given, Math.Max(1, Item!.Collectible.MaxStackSize)));
            var source = new DummySlot(stack);
            var target = hopper.Inventory.GetBestSuitedSlot(source).slot;
            if (target == null)
                break;
            var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, stack.StackSize);
            int moved = source.TryPutInto(target, ref op);
            if (moved <= 0)
                break;
            target.MarkDirty();
            given += moved;
        }
        if (given > 0)
            hopper.MarkDirty();
        return given;
    }

    /// <summary>A stack of <paramref name="size"/> of the item, at the temperature it was copied at.</summary>
    private ItemStack Copy(int size)
    {
        var stack = Item!.Clone();
        stack.StackSize = size;
        if (Item.Attributes["temperature"] is ITreeAttribute heat)
            stack.Collectible.SetTemperature(Api.World, stack, heat.GetFloat("temperature"), false);
        return stack;
    }

    /// <summary>Copies <paramref name="stack"/> as the item to feed (null clears it); the stack
    /// itself is left as it is.</summary>
    public void SetItem(ItemStack? stack)
    {
        if (stack?.Collectible == null)
            Item = null;
        else
        {
            var copy = stack.Clone();
            copy.StackSize = 1;
            if (copy.Attributes.HasAttribute("temperature"))
                copy.Collectible.SetTemperature(Api.World, copy, stack.Collectible.GetTemperature(Api.World, stack), false);
            Item = copy;
        }
        MarkDirty();
    }

    /// <summary>Sets the feed, clamped (<see cref="ItemFeed.Configure"/>).</summary>
    public void Configure(double rate, bool unlimited, bool paused, long limit)
    {
        _feed.Configure(rate, unlimited, paused, limit);
        MarkDirty();
    }

    /// <summary>Zeroes the count and the rate.</summary>
    public void ResetCounts()
    {
        _feed.Reset();
        _window?.Reset(Now);
        MarkDirty();
    }

    private void SyncIfChanged()
    {
        long now = Api.World.ElapsedMilliseconds;
        double rate = Math.Round(PerMinute, 1);
        if ((_feed.Total == _syncedTotal && rate == _syncedRate) || now - _syncedAt < SyncMs)
            return;
        MarkDirty(false);
    }

    public override void MarkDirty(bool redrawOnClient = false, IPlayer? skipPlayer = null)
    {
        if (Api?.Side == EnumAppSide.Server)
        {
            _syncedAt = Api.World.ElapsedMilliseconds;
            _syncedTotal = _feed.Total;
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
        _dialog = new GuiDialogCreativeItemSource(this, capi);
        _dialog.OnClosed += () => _dialog = null;
        if (!_dialog.TryOpen())
            _dialog = null;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
        if (packetid < CreativeItemBlocks.PacketSetItem || packetid > CreativeItemBlocks.PacketReset
            || !CreativeItemBlocks.CanUse(Api.World, fromPlayer, Pos))
            return;
        switch (packetid)
        {
            case CreativeItemBlocks.PacketSetItem:
                // The server's own copy of what the player holds on the mouse, never the client's word.
                SetItem(fromPlayer.InventoryManager.MouseItemSlot?.Itemstack);
                break;
            case CreativeItemBlocks.PacketClearItem:
                SetItem(null);
                break;
            case CreativeItemBlocks.PacketSettings:
                CreativeItemSourceSettings settings;
                try
                {
                    settings = SerializerUtil.Deserialize<CreativeItemSourceSettings>(data);
                }
                catch (Exception e)
                {
                    Api.Logger.Warning("[seraphhorizons] Bad creative item source settings from {0}: {1}",
                        fromPlayer.PlayerName, e.Message);
                    return;
                }
                Configure(settings.Rate, settings.Unlimited, settings.Paused, settings.Limit);
                break;
            case CreativeItemBlocks.PacketReset:
                ResetCounts();
                break;
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (Item != null)
            tree.SetItemstack("item", Item);
        else
            tree.RemoveAttribute("item");
        tree.SetDouble("rate", _feed.Rate);
        tree.SetBool("unlimited", _feed.Unlimited);
        tree.SetBool("paused", _feed.Paused);
        tree.SetLong("limit", _feed.Limit);
        tree.SetLong("total", _feed.Total);
        tree.SetDouble("owed", _feed.Owed);
        tree.SetDouble("perMinute", PerMinute);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var item = tree.GetItemstack("item");
        Item = item != null && item.ResolveBlockOrItem(worldAccessForResolve) ? item : null;
        if (Item != null)
            Item.StackSize = 1;
        _feed.Configure(tree.GetDouble("rate", ItemFeed.DefaultRate), tree.GetBool("unlimited"),
            tree.GetBool("paused"), tree.GetLong("limit"));
        _feed.Restore(tree.GetLong("total"), tree.GetDouble("owed"));
        double perMinute = tree.GetDouble("perMinute");
        _perMinute = double.IsFinite(perMinute) ? Math.Max(0, perMinute) : 0;
        _dialog?.Refresh();
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping) =>
        Item?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(Item), blockIdMapping, itemIdMapping);

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
        Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        if (Item?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve) == false)
            Item = null;
        Item?.Collectible.OnLoadCollectibleMappings(worldForResolve, new DummySlot(Item), oldBlockIdMapping, oldItemIdMapping, resolveImports);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("seraphhorizons:creativeitemsource-info-item",
            Item?.GetName() ?? Lang.Get("seraphhorizons:creativeitemsource-noitem")));
        dsc.AppendLine(SettingText());
        dsc.AppendLine(Lang.Get("seraphhorizons:creativeitemsource-total", _feed.Total));
        dsc.AppendLine(Lang.Get("seraphhorizons:creativeitems-rate", PerMinute.ToString("0.#")));
    }

    /// <summary>What the source is doing, in a line.</summary>
    public string SettingText()
    {
        if (_feed.LimitReached)
            return Lang.Get("seraphhorizons:creativeitemsource-info-limitreached", _feed.Limit);
        string text = _feed.Paused ? Lang.Get("seraphhorizons:creativeitemsource-info-paused")
            : _feed.Unlimited ? Lang.Get("seraphhorizons:creativeitemsource-info-unlimited")
            : Lang.Get("seraphhorizons:creativeitemsource-info-rate", _feed.Rate.ToString("0.##"));
        return _feed.Limit > 0 ? text + " " + Lang.Get("seraphhorizons:creativeitemsource-info-limit", _feed.Limit) : text;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        _dialog?.TryClose();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        _dialog?.TryClose();
    }
}
