using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>The header's standing progress: a dark track and the part done in the GUI's gold, drawn
/// once into the dialog's static texture.</summary>
public sealed class GuiElementStandingBar(ICoreClientAPI capi, ElementBounds bounds, double fraction) : GuiElement(capi, bounds)
{
    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        double x = Bounds.drawX, y = Bounds.drawY, w = Bounds.InnerWidth, h = Bounds.InnerHeight;
        ctx.SetSourceRGBA(0, 0, 0, 0.45);
        RoundRectangle(ctx, x, y, w, h, 2);
        ctx.Fill();
        if (fraction > 0)
        {
            ctx.SetSourceRGBA(0.82, 0.68, 0.36, 0.9);
            RoundRectangle(ctx, x + 1, y + 1, Math.Max(2, (w - 2) * Math.Clamp(fraction, 0, 1)), h - 2, 2);
            ctx.Fill();
        }
        ctx.SetSourceRGBA(0.55, 0.47, 0.33, 0.8);
        ctx.LineWidth = 1;
        RoundRectangle(ctx, x, y, w, h, 2);
        ctx.Stroke();
    }
}

/// <summary>
/// Hatching over some cells of a slot grid: the trade window's locked goods (and offers locked to
/// this player), as a dark veil with diagonal strokes over the item. Drawn after the grid (draw
/// order 1) at the grid's own slot bounds, so it follows the grid wherever it is laid out.
/// </summary>
public sealed class GuiElementSlotHatch(ICoreClientAPI capi, ElementBounds bounds, GuiElementItemSlotGridBase grid, IReadOnlyCollection<int> cells)
    : GuiElement(capi, bounds)
{
    private LoadedTexture? _texture;

    public override double DrawOrder => 1;

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        int size = (int)Math.Ceiling(scaled(GuiElementPassiveItemSlot.unscaledSlotSize));
        using var surface = new ImageSurface(Format.Argb32, size, size);
        using var ctx = genContext(surface);
        ctx.SetSourceRGBA(0.05, 0.04, 0.03, 0.55);
        ctx.Rectangle(0, 0, size, size);
        ctx.Fill();
        ctx.SetSourceRGBA(0.8, 0.72, 0.55, 0.45);
        ctx.LineWidth = scaled(1.5);
        double step = scaled(7);
        for (double d = -size; d < size; d += step)
        {
            ctx.MoveTo(d, size);
            ctx.LineTo(d + size, 0);
        }
        ctx.Stroke();
        _texture ??= new LoadedTexture(api);
        generateTexture(surface, ref _texture);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (_texture is null || grid.SlotBounds is null) return;
        foreach (int i in cells)
        {
            if (i < 0 || i >= grid.SlotBounds.Length) continue;
            var b = grid.SlotBounds[i];
            api.Render.Render2DTexturePremultipliedAlpha(_texture.TextureId, b.renderX, b.renderY, b.OuterWidth, b.OuterHeight, 260);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _texture?.Dispose();
    }
}

/// <summary>A display-only slot: the trade window's locked goods. Nothing goes in or out; its tooltip
/// starts with why it is locked.</summary>
public sealed class LockedSlot(InventoryBase inventory) : ItemSlot(inventory)
{
    public string? Note { get; set; }

    public override bool CanTake() => false;
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
    public override bool CanHold(ItemSlot sourceSlot) => false;
    public override bool TryFlipWith(ItemSlot itemSlot) => false;
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op) { }
    public override int TryPutInto(ItemSlot sinkSlot, ref ItemStackMoveOperation op) => 0;

    public override string GetStackDescription(IClientWorldAccessor world, bool extendedDebugInfo) =>
        Note is null ? base.GetStackDescription(world, extendedDebugInfo) : Note + "\n\n" + base.GetStackDescription(world, extendedDebugInfo);
}

/// <summary>The locked goods' slots, client side only (never opened on the server).</summary>
public sealed class LockedInventory : InventoryGeneric
{
    public LockedInventory(int count, ICoreClientAPI capi)
        : base(count, "seraphhorizons-locked", "-" + capi.World.Player.PlayerUID, capi, (_, inv) => new LockedSlot(inv)) { }

    public override object? ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op) => null;
}
