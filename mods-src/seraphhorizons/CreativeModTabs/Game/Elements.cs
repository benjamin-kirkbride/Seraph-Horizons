using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>The mode button's look: the game's vertical tab, opening to the right.</summary>
internal static class TabLook
{
    public const double TabPadding = 3;
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
