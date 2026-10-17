using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Tailings in the world (#694): what a machine or hand station discards goes on a heap of vanilla
/// layered gravel or sand in the ore's host rock beside its tailings point
/// (<see cref="TailingsHeap"/>). Server side.
/// </summary>
public static class Tailings
{
    /// <summary>The rock a discarded stack's tailings are in: the code's <c>rock</c> variant (raw ore,
    /// placer gravel), else the stack's <c>rock</c> attribute (<c>ItemOreMap.AttrRock</c>, which ore
    /// processing carries on what it makes from raw ore), else <paramref name="fallback"/>.</summary>
    public static string RockOf(ItemStack? stack, string fallback = "granite")
    {
        if (stack?.Collectible?.Variant?["rock"] is { Length: > 0 } rock)
            return rock;
        if (stack?.Attributes?.GetString("rock") is { Length: > 0 } attr)
            return attr;
        return fallback;
    }

    /// <summary>Whether the world has tailings blocks of <paramref name="form"/> in <paramref name="rock"/>
    /// (a rock with no gravel or sand of its own, as some mods' rocks have none, falls back to granite).</summary>
    public static string ExistingRock(IWorldAccessor world, TailingsForm form, string rock) =>
        world.GetBlock(new AssetLocation(TailingsBlocks.Code(form, rock, 1))) is { Id: > 0 } ? rock : "granite";

    /// <summary>Puts <paramref name="layers"/> layers of <paramref name="form"/> in <paramref name="rock"/>
    /// on the heap at <paramref name="at"/>; returns how many it placed (a full heap takes no more).</summary>
    public static int Discard(IWorldAccessor world, BlockPos at, TailingsForm form, string rock, int layers)
    {
        if (layers <= 0)
            return 0;
        rock = ExistingRock(world, form, rock);
        var ba = world.BlockAccessor;
        var cells = TailingsHeap.Offsets.Select(o => at.AddCopy(o.X, o.Y, o.Z)).ToArray();
        var (writes, unplaced) = TailingsHeap.Place(i => Read(ba, cells[i], form, rock), layers);
        foreach (var (cell, n) in writes)
        {
            var block = world.GetBlock(new AssetLocation(TailingsBlocks.Code(form, rock, n)));
            if (block == null || block.Id == 0)
                continue;
            ba.SetBlock(block.Id, cells[cell]);
            ba.TriggerNeighbourBlockUpdate(cells[cell]);
        }
        return layers - unplaced;
    }

    /// <summary>A heap cell as the heap sees it.</summary>
    private static TailingsCell Read(IBlockAccessor ba, BlockPos pos, TailingsForm form, string rock)
    {
        var block = ba.GetBlock(pos);
        var below = ba.GetBlock(pos.DownCopy());
        bool supported = below.SideSolid[BlockFacing.UP.Index];
        if (TailingsBlocks.Parse(block.Code?.ToString()) is { } t)
        {
            if (t.Layers < TailingsBlocks.LayersPerBlock && t.Form == form && t.Rock == rock)
                return new TailingsCell(TailingsCellKind.Layered, t.Layers, supported);
            return new TailingsCell(TailingsCellKind.Blocked, t.Layers, supported);
        }
        bool open = block.Id == 0 || block.IsReplacableBy(ba.GetBlock(new AssetLocation(TailingsBlocks.Code(form, rock, 1))))
                                      && !block.IsLiquid();
        return new TailingsCell(open ? TailingsCellKind.Open : TailingsCellKind.Blocked, 0, supported);
    }
}
