using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Riddle;

/// <summary>
/// What the riddle does to ore items: marks the fines classified, and turns the oversize back into raw ore.
/// <para>Classified is ore-core's (#765) stream state: the same item code with the <c>oreState</c> attribute's
/// <see cref="OreState.Classified"/> flag (<see cref="OreStreamAttributes.AddState"/>), so classified and unclassified crushed ore do not stack together. The
/// concentrators read it and lift the unclassified × 0.85.</para>
/// <para>The oversize is rock again (#714, 2026-10-13): raw ore of the charge's ore and grade
/// (<c>game:ore-{grade}-{ore}-{rock}</c>), which holds the same metal as the crushed ore and spalls back into it,
/// so nothing is lost. Its rock is the charge's own if it carries one (the <c>rock</c> attribute), else the rock
/// the station stands on, else granite (the model's default); the crushed ore itself when no such raw ore exists.</para>
/// </summary>
public static class RiddleOre
{
    public const string RockKey = "rock";
    public const string DefaultRock = "granite";

    public static bool IsClassified(ItemStack stack) =>
        OreStreamAttributes.StateOf(stack).HasFlag(OreState.Classified);

    public static void MarkClassified(ItemStack stack) => OreStreamAttributes.AddState(stack, OreState.Classified);

    /// <summary>The ore and grade of a crushed ore code (<c>seraphhorizons:crushedore-{ore}-{grade}</c>).</summary>
    public static (string Ore, string Grade)? Split(string? code)
    {
        if (!Core.Riddling.IsCrushedOre(code))
            return null;
        var rest = code!.Substring(Core.Riddling.CrushedOrePrefix.Length);
        int dash = rest.LastIndexOf('-');
        return dash > 0 && dash < rest.Length - 1 ? (rest[..dash], rest[(dash + 1)..]) : null;
    }

    public static string? OreOf(string? code) => Split(code)?.Ore;

    /// <summary>The rock of the block under <paramref name="pos"/> when it is plain rock (<c>game:rock-{rock}</c>), else null.</summary>
    public static string? RockUnder(IWorldAccessor? world, BlockPos? pos)
    {
        if (world == null || pos == null)
            return null;
        var below = world.BlockAccessor.GetBlock(pos.DownCopy());
        return below?.Code is { Domain: "game" } c && c.Path.StartsWith("rock-", StringComparison.Ordinal) ? below.Variant["rock"] : null;
    }

    /// <summary>The oversize of <paramref name="charge"/>: <paramref name="count"/> raw ore of its ore and grade, in its
    /// own rock, else <paramref name="rockUnder"/>, else granite, else any rock it comes in; null when there is none.</summary>
    public static ItemStack? Oversize(IWorldAccessor world, ItemStack charge, int count, string? rockUnder)
    {
        if (Split(charge.Collectible.Code.ToString()) is not var (ore, grade))
            return null;
        Item? Raw(string? rock) =>
            rock == null ? null : world.GetItem(new AssetLocation("game", $"ore-{grade}-{ore}-{rock}")) is { IsMissing: false, Id: > 0 } i ? i : null;
        var item = Raw(charge.Attributes?.GetString(RockKey)) ?? Raw(rockUnder) ?? Raw(DefaultRock)
                   ?? world.Items.FirstOrDefault(i => i?.Code is { Domain: "game" } c && !i.IsMissing
                                                     && c.Path.StartsWith($"ore-{grade}-{ore}-", StringComparison.Ordinal)
                                                     && c.Path.Count(ch => ch == '-') == 3);
        return item == null ? null : new ItemStack(item, count);
    }
}
