using System.Net;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.CreativeItems;

/// <summary>
/// The item sink's dialog: the total and rate, every item received with its count (most first)
/// in a scrolled list, and Reset. Refreshed whenever the server sends the block entity's data.
/// </summary>
public class GuiDialogCreativeItemSink : GuiDialogBlockEntity
{
    private const double Width = 400;
    private const double ListHeight = 260;

    private readonly BlockEntityCreativeItemSink _sink;

    public GuiDialogCreativeItemSink(BlockEntityCreativeItemSink sink, ICoreClientAPI capi)
        : base(Lang.Get("seraphhorizons:block-creativeitemsink"), sink.Pos, capi)
    {
        _sink = sink;
        if (IsDuplicate)
            return;
        Compose();
    }

    private void Compose()
    {
        var font = CairoFont.WhiteSmallText();
        var statusBounds = ElementBounds.Fixed(0, 30, Width, 50);
        var clipBounds = ElementBounds.Fixed(0, 90, Width - 24, ListHeight);
        var insetBounds = clipBounds.ForkBoundingParent(3, 3, 3, 3);
        var listBounds = ElementBounds.Fixed(0, 0, Width - 34, ListHeight);
        var scrollbarBounds = ElementBounds.Fixed(Width - 14, 90, 20, ListHeight);
        var resetBounds = ElementBounds.Fixed(0, 90 + ListHeight + 16, 120, 28);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        SingleComposer = capi.Gui.CreateCompo("seraphhorizons-creativeitemsink-" + BlockEntityPosition, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bgBounds)
                .AddDynamicText("", font, statusBounds, "status")
                .AddInset(insetBounds, 3)
                .BeginClip(clipBounds)
                    .AddRichtext(ListText(), font, listBounds, "list")
                .EndClip()
                .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .AddSmallButton(Lang.Get("seraphhorizons:creativeitems-reset"), OnReset, resetBounds, EnumButtonStyle.Normal, "reset")
            .EndChildElements()
            .Compose();
        Refresh();
    }

    /// <summary>Shows the counts as the server last sent them.</summary>
    public void Refresh()
    {
        if (SingleComposer == null)
            return;
        SingleComposer.GetDynamicText("status").SetNewText(
            Lang.Get("seraphhorizons:creativeitemsink-total", _sink.Tally.Total) + "\n"
            + Lang.Get("seraphhorizons:creativeitems-rate", _sink.PerMinute.ToString("0.#")));
        var list = SingleComposer.GetRichtext("list");
        list.SetNewText(ListText(), CairoFont.WhiteSmallText());
        SingleComposer.GetScrollbar("scrollbar").SetHeights((float)ListHeight, (float)Math.Max(list.Bounds.fixedHeight, ListHeight));
    }

    private string ListText()
    {
        var entries = _sink.Tally.Ordered();
        if (entries.Count == 0)
            return Lang.Get("seraphhorizons:creativeitemsink-empty");
        return string.Join("<br>", entries.Select(e => Lang.Get("seraphhorizons:creativeitemsink-line", e.Value,
            WebUtility.HtmlEncode(BlockEntityCreativeItemSink.StackOf(capi.World, e.Key)?.GetName() ?? e.Key))));
    }

    private void OnScroll(float value)
    {
        var list = SingleComposer.GetRichtext("list");
        list.Bounds.fixedY = -value;
        list.Bounds.CalcWorldBounds();
    }

    private bool OnReset()
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, CreativeItemBlocks.PacketReset);
        return true;
    }
}
