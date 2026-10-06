using SeraphHorizons.Mod.Trading.Visitors.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Visitors;

/// <summary>
/// The world around an inn as <see cref="InnRules"/> reads it, block by block (cached for one
/// evaluation). What a block is:
/// <list type="bullet">
/// <item>Solid: anything but air, liquids and blocks the game lets you build through (plants,
/// snow: replaceable from 5000); a torch is solid too, which only shrinks the room by a cell.</item>
/// <item>Door: a <c>BlockBaseDoor</c> (doors, trapdoors, gates), a wall whether open or shut.</item>
/// <item>Bed: a <c>BlockBed</c>, or any block whose code starts <c>bed-</c> (mods' beds).</item>
/// <item>Table: a block whose code has a <c>table</c> part (<c>game:table-normal</c>, mods' tables).</item>
/// <item>Food: an edible block, or a block entity with an inventory holding something edible (a
/// crock, a bowl or pot of a meal, a pie, food in ground storage or on a shelf).</item>
/// <item>Stall: the pack's inn sign. Cartwright's Caravan's market stalls are entities
/// (<c>cartwrightscaravan:marketstall-*</c>), found by <see cref="StallEntities"/>.</item>
/// </list>
/// </summary>
public sealed class InnProbe(IWorldAccessor world) : IInnArea
{
    public const string StallDomain = "cartwrightscaravan";
    public const string StallPrefix = "marketstall";
    public const string SignPrefix = "innsign";

    private readonly IBlockAccessor _ba = world.BlockAccessor;
    private readonly Dictionary<InnPos, (InnCell Cell, int Light)> _cache = new();
    private readonly BlockPos _tmp = new(0);

    public InnCell At(InnPos p) => Read(p).Cell;

    public int LightAt(InnPos p) => Read(p).Light;

    private (InnCell Cell, int Light) Read(InnPos p)
    {
        if (_cache.TryGetValue(p, out var v)) return v;
        _tmp.Set(p.X, p.Y, p.Z);
        var block = _ba.GetBlock(_tmp);
        v = (Classify(block), block.Id == 0 ? 0 : block.GetLightHsv(_ba, _tmp)?[2] ?? 0);
        _cache[p] = v;
        return v;
    }

    private InnCell Classify(Block block)
    {
        if (block.Id == 0) return InnCell.Open;
        var cell = InnCell.Open;
        if (block is BlockBaseDoor) cell |= InnCell.Door;
        else if (!block.IsLiquid() && block.Replaceable < 5000) cell |= InnCell.Solid;
        string path = block.Code?.Path ?? "";
        if (block is BlockBed || path.StartsWith("bed-", StringComparison.Ordinal)) cell |= InnCell.Bed;
        if (path.Split('-').Contains("table")) cell |= InnCell.Table;
        if (block.Code?.Domain == SeraphHorizonsSystem.HarmonyId && path.StartsWith(SignPrefix, StringComparison.Ordinal)) cell |= InnCell.Stall;
        if (HasFood(block)) cell |= InnCell.Food;
        return cell;
    }

    private bool HasFood(Block block)
    {
        if (block.NutritionProps != null) return true;
        if (block.EntityClass is null || _ba.GetBlockEntity(_tmp) is not IBlockEntityContainer { Inventory: { } inv }) return false;
        for (int i = 0; i < inv.Count; i++)
            if (inv[i]?.Itemstack is { } stack && stack.Collectible.GetNutritionProperties(world, stack, null) != null)
                return true;
        return false;
    }

    /// <summary>Cartwright's Caravan's market stalls within <paramref name="radius"/> of a position.</summary>
    public static List<InnPos> StallEntities(IWorldAccessor world, BlockPos around, int radius)
    {
        var center = new Vec3d(around.X + 0.5, around.Y + 0.5, around.Z + 0.5);
        return world.GetEntitiesAround(center, radius + 1, radius + 1, IsStall)
            .Select(e => new InnPos((int)Math.Floor(e.Pos.X), (int)Math.Floor(e.Pos.Y), (int)Math.Floor(e.Pos.Z)))
            .ToList();
    }

    public static bool IsStall(Entity e) =>
        e.Code?.Domain == StallDomain && e.Code.Path.StartsWith(StallPrefix, StringComparison.Ordinal) && e.Alive;

    public static InnReport Evaluate(IWorldAccessor world, BlockPos origin, InnSettings settings) =>
        InnRules.Evaluate(new InnProbe(world), new InnPos(origin.X, origin.Y, origin.Z),
            StallEntities(world, origin, settings.Radius), settings);
}
