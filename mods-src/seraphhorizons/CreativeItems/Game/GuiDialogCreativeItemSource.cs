using System.Globalization;
using SeraphHorizons.Mod.CreativeItems.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.CreativeItems;

/// <summary>
/// The item source's dialog: the item's ghost slot, the rate, Unlimited and Paused switches, the
/// count limit, the count and rate, and Reset. Every change goes to the server as a block entity
/// packet, and the server's answer (the block entity's data) refreshes the slot and the counts;
/// the fields the player types in are set only when the dialog opens, so an answer never
/// overwrites a number being typed.
/// </summary>
public class GuiDialogCreativeItemSource : GuiDialogBlockEntity
{
    private const double Width = 330;

    private readonly BlockEntityCreativeItemSource _source;
    private readonly GhostInventory? _ghost;
    private bool _unlimited;
    private bool _paused;

    public GuiDialogCreativeItemSource(BlockEntityCreativeItemSource source, ICoreClientAPI capi)
        : base(Lang.Get("seraphhorizons:block-creativeitemsource"), source.Pos, capi)
    {
        _source = source;
        if (IsDuplicate)
            return;
        _ghost = new GhostInventory(capi, OnSlotClicked);
        _unlimited = source.Feed.Unlimited;
        _paused = source.Feed.Paused;
        Compose();
    }

    private void Compose()
    {
        var font = CairoFont.WhiteSmallText();
        var slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30, 1, 1);
        var itemBounds = ElementBounds.Fixed(60, 40, Width - 60, 40);
        var rateLabel = ElementBounds.Fixed(0, 100, 190, 25);
        var rateInput = ElementBounds.Fixed(200, 95, 100, 29);
        var unlimitedSwitch = ElementBounds.Fixed(0, 135, 30, 30);
        var unlimitedLabel = ElementBounds.Fixed(40, 140, Width - 40, 25);
        var pausedSwitch = ElementBounds.Fixed(0, 175, 30, 30);
        var pausedLabel = ElementBounds.Fixed(40, 180, Width - 40, 25);
        var limitLabel = ElementBounds.Fixed(0, 225, 190, 25);
        var limitInput = ElementBounds.Fixed(200, 220, 100, 29);
        var statusBounds = ElementBounds.Fixed(0, 265, Width, 75);
        var resetBounds = ElementBounds.Fixed(0, 345, 120, 28);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        SingleComposer = capi.Gui.CreateCompo("seraphhorizons-creativeitemsource-" + BlockEntityPosition, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bgBounds)
                .AddItemSlotGrid(_ghost!, _ => { }, 1, slotBounds, "item")
                .AddDynamicText("", font, itemBounds, "itemname")
                .AddStaticText(Lang.Get("seraphhorizons:creativeitemsource-rate"), font, rateLabel)
                .AddNumberInput(rateInput, OnRateChanged, font, "rate")
                .AddSwitch(OnUnlimited, unlimitedSwitch, "unlimited")
                .AddStaticText(Lang.Get("seraphhorizons:creativeitemsource-unlimited"), font, unlimitedLabel)
                .AddSwitch(OnPaused, pausedSwitch, "paused")
                .AddStaticText(Lang.Get("seraphhorizons:creativeitemsource-paused"), font, pausedLabel)
                .AddStaticText(Lang.Get("seraphhorizons:creativeitemsource-limit"), font, limitLabel)
                .AddNumberInput(limitInput, OnLimitChanged, font, "limit")
                .AddDynamicText("", font, statusBounds, "status")
                .AddSmallButton(Lang.Get("seraphhorizons:creativeitems-reset"), OnReset, resetBounds, EnumButtonStyle.Normal, "reset")
            .EndChildElements()
            .Compose();

        var feed = _source.Feed;
        SingleComposer.GetNumberInput("rate").SetValue(feed.Rate.ToString("0.##", CultureInfo.InvariantCulture), false);
        SingleComposer.GetNumberInput("limit").SetValue(feed.Limit.ToString(CultureInfo.InvariantCulture), false);
        SingleComposer.GetSwitch("unlimited").SetValue(_unlimited);
        SingleComposer.GetSwitch("paused").SetValue(_paused);
        Refresh();
    }

    /// <summary>Shows the block entity's item, count and rate as the server last sent them.</summary>
    public void Refresh()
    {
        if (_ghost == null || SingleComposer == null)
            return;
        _ghost[0].Itemstack = _source.Item?.Clone();
        _ghost[0].MarkDirty();
        SingleComposer.GetDynamicText("itemname").SetNewText(
            _source.Item?.GetName() ?? Lang.Get("seraphhorizons:creativeitemsource-putitem"));
        var lines = new List<string>
        {
            _source.SettingText(),
            Lang.Get("seraphhorizons:creativeitemsource-total", _source.Feed.Total),
            Lang.Get("seraphhorizons:creativeitems-rate", _source.PerMinute.ToString("0.#")),
        };
        SingleComposer.GetDynamicText("status").SetNewText(string.Join("\n", lines));
    }

    // The ghost slot clicked: with an item on the mouse the server copies its own view of that
    // stack; with none it clears the slot. The mouse's stack is never touched.
    private void OnSlotClicked(bool holding)
    {
        DoSendPacket(holding ? CreativeItemBlocks.PacketSetItem : CreativeItemBlocks.PacketClearItem);
    }

    private void OnRateChanged(string text)
    {
        if (TryParse(text, out double rate))
            SendSettings(rate: rate);
    }

    private void OnLimitChanged(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            SendSettings(limit: 0);
        else if (TryParse(text, out double limit))
            SendSettings(limit: (long)Math.Min(Math.Max(0, Math.Floor(limit)), ItemFeed.MaxLimit));
    }

    private void OnUnlimited(bool on)
    {
        _unlimited = on;
        SendSettings();
    }

    private void OnPaused(bool on)
    {
        _paused = on;
        SendSettings();
    }

    private bool OnReset()
    {
        DoSendPacket(CreativeItemBlocks.PacketReset);
        return true;
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    // The other settings as the block entity has them.
    private void SendSettings(double? rate = null, long? limit = null)
    {
        var settings = new CreativeItemSourceSettings
        {
            Rate = rate ?? _source.Feed.Rate,
            Unlimited = _unlimited,
            Paused = _paused,
            Limit = limit ?? _source.Feed.Limit,
        };
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, CreativeItemBlocks.PacketSettings, SerializerUtil.Serialize(settings));
    }

    private void DoSendPacket(int packetId) => capi.Network.SendBlockEntityPacket(BlockEntityPosition, packetId);
}

/// <summary>
/// The dialog's one ghost slot, on the client only: a click hands the dialog whether the mouse
/// holds an item and moves nothing (no inventory packet is sent; the dialog sends its own).
/// </summary>
public class GhostInventory : DummyInventory
{
    private readonly Action<bool> _clicked;

    public GhostInventory(ICoreAPI api, Action<bool> clicked) : base(api, 1) => _clicked = clicked;

    public override object? ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Shift-click hands the slot itself as the source: nothing to copy, nothing to move.
        if (!op.ShiftDown)
            _clicked(sourceSlot.Itemstack != null);
        return null;
    }
}
