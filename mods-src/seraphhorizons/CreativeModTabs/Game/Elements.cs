using Cairo;
using SeraphHorizons.Mod.CreativeModTabs.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>
/// Stands in for the creative dialog's own tab column while the mod tabs are shown: the dialog still
/// composes it (from the one mod tab it is given) and calls <c>SetValue</c> on it, but it draws nothing
/// and takes no input. A <see cref="GuiElementVerticalTabs"/> so the dialog's <c>GetVerticalTab</c> cast
/// holds; it overrides everything it would draw, so the base methods other mods patch never run for it.
/// </summary>
internal sealed class HiddenVerticalTabs(ICoreClientAPI capi, GuiTab[] tabs, ElementBounds bounds)
    : GuiElementVerticalTabs(capi, tabs, CairoFont.WhiteDetailText(), CairoFont.WhiteDetailText(), bounds, (_, _) => { })
{
    public override void ComposeTextElements(Context ctxStatic, ImageSurface surfaceStatic) { }
    public override void RenderInteractiveElements(float deltaTime) { }
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args) { }
    public override bool IsPositionInside(int posX, int posY) => false;
    public override bool Focusable => false;
}

/// <summary>Shared look of the tab strip and the mode button: the game's vertical tab, opening to the right.</summary>
internal static class TabLook
{
    public const double TabHeight = 25, TabSpacing = 5, TabPadding = 3;
    public const double MinWidth = 130, MaxWidth = 220;

    public static CairoFont Font() => CairoFont.WhiteDetailText().WithFontSize(17f);
    public static CairoFont SelectedFont() => CairoFont.WhiteDetailText().WithFontSize(17f).WithColor(GuiStyle.ActiveButtonTextColor);

    /// <summary>Unscaled width that fits the widest text, within [<see cref="MinWidth"/>, <see cref="MaxWidth"/>].</summary>
    public static double FixedWidthFor(CairoFont font, IEnumerable<string> texts, double extra = 0)
    {
        double widest = 0;
        foreach (var t in texts) widest = Math.Max(widest, font.GetTextExtents(t ?? "").Width);
        double scale = Math.Max(GuiElement.scaled(1), 0.01);
        return Math.Clamp(Math.Ceiling(widest / scale + 2 * TabPadding + 4 + extra), MinWidth, MaxWidth);
    }

    /// <summary>The game's tab outline (GuiElementVerticalTabs, Right = true) at (x, y).</summary>
    public static void TabPath(Context ctx, double x, double y, double width, double height)
    {
        double r = GuiElement.scaled(1);
        ctx.NewPath();
        ctx.MoveTo(x, y + height);
        ctx.LineTo(x, y);
        ctx.LineTo(x + width + r, y);
        ctx.ArcNegative(x + width, y + r, r, 4.71238899230957, 3.1415927410125732);
        ctx.ArcNegative(x + width, y - r + height, r, 3.1415927410125732, 1.5707963705062866);
        ctx.ClosePath();
    }

    /// <summary>The selected tab's frame, as GuiElementVerticalTabs draws it (without its blur).</summary>
    public static void SelectedFrame(Context ctx, double width, double height)
    {
        double r = GuiElement.scaled(1);
        ctx.SetSourceRGBA(1, 1, 1, 0);
        ctx.Paint();
        ctx.NewPath();
        ctx.MoveTo(width, height + 1);
        ctx.LineTo(width, 0);
        ctx.LineTo(r, 0);
        ctx.ArcNegative(0, r, r, 4.71238899230957, 3.1415927410125732);
        ctx.ArcNegative(0, height - r, r, 3.1415927410125732, 1.5707963705062866);
        ctx.ClosePath();
        var bg = GuiStyle.DialogDefaultBgColor;
        ctx.SetSourceRGBA(bg[0], bg[1], bg[2], bg[3]);
        ctx.Fill();
        ctx.NewPath();
        ctx.LineTo(1, 1);
        ctx.LineTo(width, 1);
        ctx.LineTo(width, height - 1);
        ctx.LineTo(1, height - 1);
        ctx.SetSourceRGBA(GuiStyle.DialogLightBgColor[0] * 1.6, GuiStyle.DialogStrongBgColor[1] * 1.6, GuiStyle.DialogStrongBgColor[2] * 1.6, 1);
        ctx.LineWidth = 3.5;
        ctx.StrokePreserve();
        ctx.SetSourceRGBA(45 / 255.0, 35 / 255.0, 33 / 255.0, 1);
        ctx.LineWidth = 2;
        ctx.Stroke();
    }

    /// <summary>The text, shortened with "..." to fit <paramref name="maxWidth"/> scaled pixels.</summary>
    public static string Fit(Context ctx, string text, double maxWidth)
    {
        if (ctx.TextExtents(text).Width <= maxWidth) return text;
        for (int n = text.Length - 1; n > 0; n--)
        {
            string s = text[..n].TrimEnd() + "...";
            if (ctx.TextExtents(s).Width <= maxWidth) return s;
        }
        return "...";
    }
}

/// <summary>
/// The button above the right-hand tabs: "Tabs: Default" or "Tabs: Mod" with a flip glyph (two opposed
/// arrows, drawn, so no font needs the character). Clicking it calls back; the owner flips the mode.
/// </summary>
internal sealed class ModeButton : GuiElementTextBase
{
    const double GlyphWidth = 16;
    readonly Action onClick;
    readonly CairoFont selectedFont;
    LoadedTexture normal, hover;

    public ModeButton(ICoreClientAPI capi, string label, CairoFont font, CairoFont selectedFont, ElementBounds bounds, Action onClick)
        : base(capi, label, font, bounds)
    {
        this.onClick = onClick;
        this.selectedFont = selectedFont;
        normal = new LoadedTexture(capi);
        hover = new LoadedTexture(capi);
    }

    public static double FixedWidthFor(CairoFont font, string label) => TabLook.FixedWidthFor(font, [label], GlyphWidth + 6);

    public override void ComposeTextElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        int w = (int)Bounds.InnerWidth + 1, h = (int)Bounds.InnerHeight + 1;
        Draw(ref normal, w, h, selected: false);
        Draw(ref hover, w, h, selected: true);
    }

    void Draw(ref LoadedTexture into, int w, int h, bool selected)
    {
        using var surface = new ImageSurface(Format.Argb32, w, h);
        using var ctx = genContext(surface);
        double tabH = h - 1, pad = scaled(TabLook.TabPadding), glyph = scaled(GlyphWidth);
        if (selected) TabLook.SelectedFrame(ctx, w - 1, tabH);
        else
        {
            TabLook.TabPath(ctx, 1, 0, w - 2, tabH);
            var bg = GuiStyle.DialogDefaultBgColor;
            ctx.SetSourceRGBA(bg[0], bg[1], bg[2], bg[3]);
            ctx.FillPreserve();
            ShadePath(ctx);
        }
        var font = selected ? selectedFont : Font;
        font.SetupContext(ctx);
        double textY = (tabH + 1 - font.GetFontExtents().Height) / 2;
        string label = TabLook.Fit(ctx, text, w - 3 * pad - glyph);
        DrawTextLineAt(ctx, label, pad + 2, textY);

        // ⇄: an arrow to the right above an arrow to the left, in the text colour.
        double gx = w - pad - glyph, cy = tabH / 2, dy = tabH * 0.16, head = glyph * 0.3;
        font.SetupContext(ctx);
        ctx.LineWidth = Math.Max(1.2, scaled(1.4));
        ctx.NewPath();
        ctx.MoveTo(gx, cy - dy); ctx.LineTo(gx + glyph, cy - dy);
        ctx.MoveTo(gx + glyph - head, cy - dy - head); ctx.LineTo(gx + glyph, cy - dy); ctx.LineTo(gx + glyph - head, cy - dy + head);
        ctx.MoveTo(gx + glyph, cy + dy); ctx.LineTo(gx, cy + dy);
        ctx.MoveTo(gx + head, cy + dy - head); ctx.LineTo(gx, cy + dy); ctx.LineTo(gx + head, cy + dy + head);
        ctx.Stroke();
        generateTexture(surface, ref into);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        float x = (int)Bounds.renderX, y = (int)Bounds.renderY, w = (int)Bounds.InnerWidth + 1, h = (int)Bounds.InnerHeight + 1;
        if (IsPositionInside(api.Input.MouseX, api.Input.MouseY))
            api.Render.Render2DTexturePremultipliedAlpha(hover.TextureId, x, y, w, h);
        else
            api.Render.Render2DTexture(normal.TextureId, x, y, w, h);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        args.Handled = true;
        api.Gui.PlaySound("menubutton_wood");
        onClick();
    }

    public override void Dispose()
    {
        base.Dispose();
        normal.Dispose();
        hover.Dispose();
    }
}

/// <summary>
/// The mod tabs: one column on the right of the creative dialog, in the game's tab look, that scrolls with the
/// mouse wheel and keeps the selected tab in view (the game's tab column doesn't scroll, and TooManyTabs only
/// takes over the dialog's own two columns). The geometry is <see cref="TabScroll"/>.
/// </summary>
internal sealed class ModTabStrip : GuiElementTextBase
{
    readonly string[] names;
    readonly CairoFont selectedFont;
    readonly Action<int> onSelect;
    readonly Action<double> onScroll;
    LoadedTexture baseTexture;
    LoadedTexture[] selectedTextures;
    double tabHeight, spacing, contentHeight, scrollOffset;
    int active;

    public ModTabStrip(ICoreClientAPI capi, string[] names, CairoFont font, CairoFont selectedFont, ElementBounds bounds,
        int active, double scrollOffset, Action<int> onSelect, Action<double> onScroll)
        : base(capi, "", font, bounds)
    {
        this.names = names;
        this.selectedFont = selectedFont;
        this.active = active;
        this.scrollOffset = scrollOffset;
        this.onSelect = onSelect;
        this.onScroll = onScroll;
        baseTexture = new LoadedTexture(capi);
        selectedTextures = names.Select(_ => new LoadedTexture(capi)).ToArray();
    }

    public static double FixedWidthFor(CairoFont font, IEnumerable<string> names) => TabLook.FixedWidthFor(font, names, 6);

    double Viewport => Bounds.InnerHeight;

    public override void ComposeTextElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        tabHeight = scaled(TabLook.TabHeight);
        spacing = scaled(TabLook.TabSpacing);
        double pad = scaled(TabLook.TabPadding);
        contentHeight = TabScroll.ContentHeight(names.Length, tabHeight, spacing);
        int w = (int)Bounds.InnerWidth + 1;
        int tabW = w - (int)Math.Ceiling(scaled(6));   // room for the scroll indicator
        int h = (int)Math.Ceiling(Math.Max(contentHeight, 1)) + 1;

        using (var surface = new ImageSurface(Format.Argb32, w, h))
        using (var ctx = genContext(surface))
        {
            Font.Color[3] = 0.85;
            Font.SetupContext(ctx);
            double textY = (tabHeight + 1 - Font.GetFontExtents().Height) / 2;
            var bg = GuiStyle.DialogDefaultBgColor;
            for (int i = 0; i < names.Length; i++)
            {
                double y = TabScroll.TabTop(i, tabHeight, spacing);
                TabLook.TabPath(ctx, 1, y, tabW, tabHeight);
                ctx.SetSourceRGBA(bg[0], bg[1], bg[2], bg[3]);
                ctx.FillPreserve();
                ShadePath(ctx);
                Font.SetupContext(ctx);
                DrawTextLineAt(ctx, TabLook.Fit(ctx, names[i], tabW - 2 * pad), 1 + pad, y + textY);
            }
            Font.Color[3] = 1;
            generateTexture(surface, ref baseTexture);
        }

        for (int i = 0; i < names.Length; i++)
        {
            using var surface = new ImageSurface(Format.Argb32, tabW + 1, (int)tabHeight + 1);
            using var ctx = genContext(surface);
            TabLook.SelectedFrame(ctx, tabW + 1, tabHeight);
            selectedFont.SetupContext(ctx);
            DrawTextLineAt(ctx, TabLook.Fit(ctx, names[i], tabW - 2 * pad), pad + 2, textYFor(selectedFont));
            generateTexture(surface, ref selectedTextures[i]);
        }
        scrollOffset = TabScroll.EnsureVisible(scrollOffset, active, tabHeight, spacing, contentHeight, Viewport);
        onScroll(scrollOffset);

        double textYFor(CairoFont f) => (tabHeight + 1 - f.GetFontExtents().Height) / 2;
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        var r = api.Render;
        double x = (int)Bounds.renderX, y = (int)Bounds.renderY;
        r.PushScissor(Bounds, true);
        try
        {
            // As GuiElementVerticalTabs: the base plainly, the selected tab premultiplied.
            r.Render2DTexture(baseTexture.TextureId, (float)x, (float)(y - scrollOffset), baseTexture.Width, baseTexture.Height);
            int hovered = HitTest(api.Input.MouseX, api.Input.MouseY);
            for (int i = 0; i < selectedTextures.Length; i++)
            {
                if (i != active && i != hovered) continue;
                var t = selectedTextures[i];
                r.Render2DTexturePremultipliedAlpha(t.TextureId, (float)x, (float)(y + TabScroll.TabTop(i, tabHeight, spacing) - scrollOffset), t.Width, t.Height);
            }
            if (TabScroll.Indicator(scrollOffset, contentHeight, Viewport, scaled(20)) is { } ind)
            {
                float bw = (float)Math.Max(2, scaled(3));
                r.RenderRectangle((float)(x + Bounds.InnerWidth - bw - 1), (float)(y + ind.Top), 50f, bw, (float)ind.Length,
                    ColorUtil.ToRgba(160, 230, 200, 160));
            }
        }
        finally { r.PopScissor(); }
    }

    int HitTest(int mouseX, int mouseY)
    {
        if (!IsPositionInside(mouseX, mouseY)) return -1;
        return TabScroll.HitTest(mouseY - Bounds.absY, scrollOffset, names.Length, tabHeight, spacing, Viewport);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        int i = HitTest(api.Input.MouseX, api.Input.MouseY);
        if (i < 0) return;
        args.Handled = true;
        if (i != active) onSelect(i);
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        // GuiComposer.OnMouseWheel offers the wheel to every element, under the mouse or not, until one takes it.
        if (!IsPositionInside(api.Input.MouseX, api.Input.MouseY) || TabScroll.MaxOffset(contentHeight, Viewport) <= 0) return;
        double delta = Math.Abs(args.deltaPrecise) > 0.0001 ? args.deltaPrecise : args.delta;
        if (Math.Abs(delta) < 0.0001) return;
        scrollOffset = TabScroll.Wheel(scrollOffset, delta, 3 * (tabHeight + spacing), contentHeight, Viewport);
        onScroll(scrollOffset);
        args.SetHandled(true);
    }

    /// <summary>Marks tab <paramref name="index"/> selected and scrolls it into view.</summary>
    public void SetActive(int index)
    {
        active = index;
        scrollOffset = TabScroll.EnsureVisible(scrollOffset, index, tabHeight, spacing, contentHeight, Viewport);
        onScroll(scrollOffset);
    }

    public override void Dispose()
    {
        base.Dispose();
        baseTexture.Dispose();
        foreach (var t in selectedTextures) t.Dispose();
    }
}
