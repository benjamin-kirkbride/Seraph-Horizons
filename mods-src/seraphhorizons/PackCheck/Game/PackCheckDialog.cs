using SeraphHorizons.Mod.PackCheck.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.PackCheck;

/// <summary>
/// What a client's mods differ from the pack in (<see cref="PackCheckSystem"/>): a title, a line
/// on what it means, the findings in a scrolled list, and two buttons. "Close" leaves it to come
/// back next time the player joins a world; "Don't show again until this changes" stores the
/// findings' fingerprint, so it stays away until they are another set. Modal: it keeps the input
/// while open, and Escape closes it as Close does.
/// </summary>
public class PackCheckDialog : GuiDialog
{
    private const double Width = 560;
    private const double MaxListHeight = 300;
    private const double LineHeight = 22;

    private readonly IReadOnlyList<Finding> _findings;
    private readonly PackLock _pack;
    private readonly Action _dismiss;
    private double _listHeight;

    public PackCheckDialog(ICoreClientAPI capi, PackLock pack, IReadOnlyList<Finding> findings, Action dismiss) : base(capi)
    {
        _pack = pack;
        _findings = findings;
        _dismiss = dismiss;
        Compose();
    }

    public override string? ToggleKeyCombinationCode => null;
    public override EnumDialogType DialogType => EnumDialogType.Dialog;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => true;
    public override bool UnregisterOnClose => true;

    private void Compose()
    {
        var font = CairoFont.WhiteSmallText();
        var introFont = CairoFont.WhiteDetailText();
        var listText = string.Join("<br>", _findings.Select(f => "• " + PackCheckText.Line(f)));

        // Measured once the text is laid out (below); this first guess sizes the clip.
        double guess = Math.Min(MaxListHeight, Math.Max(LineHeight, _findings.Count * LineHeight + 6));
        var introBounds = ElementBounds.Fixed(0, 30, Width, 60);
        var clipBounds = ElementBounds.Fixed(0, 100, Width - 24, guess);
        var insetBounds = clipBounds.ForkBoundingParent(3, 3, 3, 3);
        var textBounds = ElementBounds.Fixed(0, 0, Width - 34, guess);
        var scrollbarBounds = ElementBounds.Fixed(Width - 14, 100, 20, guess);
        double buttonY = 100 + guess + 16;
        var closeBounds = ElementBounds.Fixed(0, buttonY, 120, 28);
        var dismissBounds = ElementBounds.Fixed(Width - 300 + 6, buttonY, 300, 28);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

        SingleComposer = capi.Gui.CreateCompo("seraphhorizons-packcheck", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get("seraphhorizons:packcheck-title"), OnClose)
            .BeginChildElements(bgBounds)
                .AddStaticText(Lang.Get("seraphhorizons:packcheck-intro", _pack.PackVersion, _pack.GameVersion), introFont, introBounds)
                .AddInset(insetBounds, 3)
                .BeginClip(clipBounds)
                    .AddRichtext(listText, font, textBounds, "findings")
                .EndClip()
                .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .AddSmallButton(Lang.Get("seraphhorizons:packcheck-close"), OnCloseButton, closeBounds, EnumButtonStyle.Normal, "close")
                .AddSmallButton(Lang.Get("seraphhorizons:packcheck-dismiss"), OnDismissButton, dismissBounds, EnumButtonStyle.Normal, "dismiss")
            .EndChildElements()
            .Compose();

        // The rich text sets its own height to the laid-out text's when composed.
        var text = SingleComposer.GetRichtext("findings");
        _listHeight = text.Bounds.fixedHeight > 0 ? text.Bounds.fixedHeight : _findings.Count * LineHeight;
        SingleComposer.GetScrollbar("scrollbar").SetHeights((float)clipBounds.fixedHeight, (float)Math.Max(_listHeight, clipBounds.fixedHeight));
    }

    private void OnScroll(float value)
    {
        var text = SingleComposer.GetRichtext("findings");
        text.Bounds.fixedY = -value;
        text.Bounds.CalcWorldBounds();
    }

    private void OnClose() => TryClose();

    private bool OnCloseButton()
    {
        TryClose();
        return true;
    }

    private bool OnDismissButton()
    {
        _dismiss();
        TryClose();
        return true;
    }
}
