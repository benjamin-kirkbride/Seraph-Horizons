using Cairo;
using SeraphHorizons.Mod.RecipeSelector.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SeraphHorizons.Mod.RecipeSelector.Game;

/// <summary>
/// The selector's grid of recipe outputs, as the game's <c>GuiElementSkillItemGrid</c> draws and hit-tests
/// it (1.22.7), but showing only the recipes a search left (<see cref="SetShown"/>), in the search's order,
/// and drawn wholly per frame so it can scroll inside a clip: the game's grid paints its slot backgrounds
/// into the dialog's static texture, which would stay put while the items moved. Its bounds are as tall as
/// every row shown; the dialog moves them under the clip as the scrollbar goes. Clicks and hovers report
/// the recipe's own index (into the game's skill items), never the slot's.
/// </summary>
internal sealed class RecipeSlotGrid : GuiElement
{
    readonly IReadOnlyList<SkillItem> items;
    readonly int cols;
    IReadOnlyList<int> shown;
    LoadedTexture slotTexture, hoverTexture;

    /// <summary>A recipe was clicked: its index into the game's skill items.</summary>
    public Action<int>? OnRecipeClick;

    /// <summary>The mouse is over a recipe (every frame, as the game's grid reports it).</summary>
    public Action<int>? OnRecipeOver;

    public RecipeSlotGrid(ICoreClientAPI capi, IReadOnlyList<SkillItem> items, int cols, ElementBounds bounds) : base(capi, bounds)
    {
        this.items = items;
        this.cols = Math.Max(1, cols);
        shown = Enumerable.Range(0, items.Count).ToList();
        slotTexture = new LoadedTexture(capi);
        hoverTexture = new LoadedTexture(capi);
        Bounds.fixedWidth = this.cols * Cell;
        Bounds.fixedHeight = SelectorLayout.Rows(shown.Count, this.cols) * Cell;
    }

    /// <summary>A slot and its padding, unscaled (the game's selector uses the same).</summary>
    public static double Cell => GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;

    /// <summary>The recipes to show, by their index into the skill items, in order.</summary>
    public void SetShown(IReadOnlyList<int> recipes)
    {
        shown = recipes;
        Bounds.fixedHeight = SelectorLayout.Rows(shown.Count, cols) * Cell;
        Bounds.CalcWorldBounds();
    }

    public int ShownCount => shown.Count;

    public override void ComposeElements(Context ctxStatic, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        double size = scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        int px = Math.Max(1, (int)size);

        // The game's slot: a faint white rounded square, embossed inwards.
        using (var slot = new ImageSurface(Format.Argb32, px, px))
        using (var ctx = genContext(slot))
        {
            ctx.SetSourceRGBA(1, 1, 1, 0.2);
            RoundRectangle(ctx, 0, 0, size, size, GuiStyle.ElementBGRadius);
            ctx.Fill();
            EmbossRoundRectangleElement(ctx, 0, 0, size, size, inverse: true);
            generateTexture(slot, ref slotTexture);
        }
        // The game's hover (GuiElementSkillItemGrid.ComposeHover).
        using (var hover = new ImageSurface(Format.Argb32, Math.Max(1, px - 2), Math.Max(1, px - 2)))
        using (var ctx = genContext(hover))
        {
            ctx.SetSourceRGBA(1, 1, 1, 0.7);
            RoundRectangle(ctx, 1, 1, size, size, GuiStyle.ElementBGRadius);
            ctx.Fill();
            generateTexture(hover, ref hoverTexture);
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double pad = scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        double size = scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        double cell = size + pad;
        int mouseX = api.Input.MouseX, mouseY = api.Input.MouseY;
        // Only within the clip: the bounds run on above and below it as the grid scrolls.
        bool mouseIn = IsPositionInside(mouseX, mouseY);
        int over = mouseIn ? SelectorLayout.SlotAt(mouseX - Bounds.absX, mouseY - Bounds.absY, cell, cols, shown.Count) : -1;
        float guiScale = RuntimeEnv.GUIScale;
        int rows = SelectorLayout.Rows(shown.Count, cols);
        var clip = InsideClipBounds;

        for (int i = 0; i < rows * cols; i++)
        {
            double x = i % cols * cell, y = i / cols * cell;
            double top = Bounds.renderY + y;
            // Rows wholly outside the clip are skipped (the clip's scissor hides them anyway).
            if (clip is not null && (top + size < clip.renderY || top > clip.renderY + clip.InnerHeight)) continue;

            api.Render.Render2DTexturePremultipliedAlpha(slotTexture.TextureId, Bounds.renderX + x, top, size, size);
            if (i == over)
            {
                api.Render.Render2DTexture(hoverTexture.TextureId, (float)(Bounds.renderX + x), (float)top, (float)size, (float)size);
                OnRecipeOver?.Invoke(shown[i]);
            }
            if (i >= shown.Count || shown[i] < 0 || shown[i] >= items.Count || items[shown[i]] is not { } item) continue;

            // As the game draws a skill item: scissored to its slot (within the clip's scissor).
            var slotBounds = ElementBounds.Fixed((Bounds.renderX + x + 1) / guiScale, (top + 1) / guiScale,
                GuiElementPassiveItemSlot.unscaledSlotSize - 2, GuiElementPassiveItemSlot.unscaledSlotSize - 2).WithParent(api.Gui.WindowBounds);
            slotBounds.CalcWorldBounds();
            api.Render.PushScissor(slotBounds, stacking: true);
            if (item.Texture != null)
            {
                if (item.TexturePremultipliedAlpha)
                    api.Render.Render2DTexturePremultipliedAlpha(item.Texture.TextureId, Bounds.renderX + x + 1, top + 1, size, size);
                else
                    api.Render.Render2DTexture(item.Texture.TextureId, (float)(Bounds.renderX + x + 1), (float)(top + 1), (float)size, (float)size);
            }
            item.RenderHandler?.Invoke(item.Code, deltaTime, Bounds.renderX + x + 1, top + 1);
            api.Render.PopScissor();
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        double cell = scaled(GuiElementPassiveItemSlot.unscaledSlotSize) + scaled(GuiElementItemSlotGridBase.unscaledSlotPadding);
        int i = SelectorLayout.SlotAt(api.Input.MouseX - Bounds.absX, api.Input.MouseY - Bounds.absY, cell, cols, shown.Count);
        if (i >= 0) OnRecipeClick?.Invoke(shown[i]);
    }

    public override void Dispose()
    {
        base.Dispose();
        slotTexture.Dispose();
        hoverTexture.Dispose();
    }
}
