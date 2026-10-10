using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Sets ore processing's rules on the loaded items (#686, #687, #688), on the server once every item
/// is loaded and patched, so the result does not depend on the order other mods' patches ran in:
/// <list type="bullet">
/// <item>vanilla's ore item <c>game:ore-{grade}-{ore}-{rock}</c>: raw ore stacks to 4, chunks to 16;
/// both crush to <c>game:crushed-{ore}-{grain}</c>, units ÷ 5 of them with no loss (a 20-unit
/// medium chromite gives 4); chunks smelt at half and fit a crucible, raw ore does not smelt. Its
/// <c>metalUnits</c> and the ore blocks' drops are left as they are (deposit measurement reads them).
/// Crystallised ore crushes and smelts the same by its grade, its stack left alone;</item>
/// <item>nuggets crush to one coarse crushed ore of their ore (Expanded Matter's and smex's nugget
/// crushing replaced, whatever order those patches ran in);</item>
/// <item>crushed ore smelts at half, concentrate whole, roasted concentrate whole, litharge to lead
/// at 20 in 21; ground ore and amalgam do not smelt;</item>
/// <item>a sulfide's concentrate does not smelt: it roasts in the firepit (#720), any fuel, into
/// its roasted concentrate, one item at a time at <see cref="OreRoasting.MeltingPoint"/> for the
/// configured seconds, at the firepit's share (<see cref="ItemOreProduct.RoastShare"/>, kept by its
/// <c>DoSmelt</c>). An item has one smelting, so it points at the roasted concentrate.</item>
/// </list>
/// What every form smelts to is what the ore's nugget smelts to (<see cref="OreProducts.NuggetOf"/>):
/// metal, melting point and smelting type, so the crucible, the bloomery (iron ore, which smelts to a
/// bloom), crucibulum and the crucible furnace take them as they take the nugget. The shares are
/// <see cref="OreRecovery.SmeltShare"/>'s, in the game's whole-number ratios
/// (<see cref="OreProducts.Rate"/>).
/// </summary>
public static class OreProcessingItems
{
    /// <summary>What was set, for the log and the scenarios.</summary>
    public sealed class Report
    {
        public int OreItems, CrystallisedOre, Nuggets, Crushed, Concentrate, Roasted, Litharge, LooseOres;
        /// <summary>Sulfide concentrates that roast in the firepit.</summary>
        public int Roasting;
        /// <summary>Ores whose nugget does not smelt (or is missing): their forms do not smelt.</summary>
        public SortedSet<string> NoSmeltTarget = new(StringComparer.Ordinal);
        /// <summary>Ore items with no <c>metalUnits</c>: left as they are.</summary>
        public List<string> NoUnits = [];
    }

    /// <summary>The size an item must be within to go in the game's crucible (its
    /// <c>maxContentDimensions</c> is 0.125 across); a chunk is given it, as a nugget has.</summary>
    public static readonly Size3f CrucibleSize = new(0.0625f, 0.0625f, 0.0625f);

    public static Report Apply(IWorldAccessor world, OreRecovery recovery, ILogger logger, float roastSeconds = (float)OreRoasting.DefaultSeconds)
    {
        var report = new Report();
        var ores = new HashSet<string>(OreProducts.Ores, StringComparer.Ordinal);
        double perItem = recovery.ConcentrateUnits;
        var smeltsLike = new Dictionary<string, CombustibleProperties?>(StringComparer.Ordinal);

        CombustibleProperties? Target(string ore)
        {
            string nugget = OreProducts.NuggetOf(ore);
            if (!smeltsLike.TryGetValue(nugget, out var props))
            {
                props = world.GetItem(new AssetLocation("game", "nugget-" + nugget))?.CombustibleProps;
                if (props?.SmeltedStack?.ResolvedItemstack == null || props.MeltingPoint <= 0)
                    props = null;
                smeltsLike[nugget] = props;
            }
            if (props == null)
                report.NoSmeltTarget.Add(ore);
            return props;
        }

        CombustibleProperties? Smelting(string ore, double unitsPerItem, double share, SmeltRate? fixedRate = null)
        {
            var rate = fixedRate ?? OreProducts.Rate(unitsPerItem, share);
            if (rate is not { } r || Target(ore) is not { } target)
                return null;
            return WithRate(world, target, r);
        }

        foreach (var item in world.Items)
        {
            if (item?.Code is not { Domain: "game" or OreProducts.Domain } code)
                continue;
            var parts = code.Path.Split('-');
            switch (parts[0])
            {
                case "ore" or "crystalizedore" when code.Domain == "game" && parts.Length == 4 && ores.Contains(parts[2])
                                                   && OreProducts.FormOfGrade(parts[1]) is { } form:
                {
                    string grade = parts[1], ore = parts[2];
                    double units = item.Attributes?["metalUnits"].AsDouble(0) ?? 0;
                    if (!(units > 0))
                    {
                        report.NoUnits.Add(code.ToString());
                        continue;
                    }
                    bool chunkOfOre = parts[0] == "ore";
                    if (chunkOfOre && OreProducts.StackOfGrade(grade) is { } stack)
                        item.MaxStackSize = stack;
                    item.CrushingProps = Crushing(world, item, OreProducts.CrushedCode(ore, OreProducts.GrainOfGrade(grade)),
                        OreProducts.CrushedCount(units, perItem));
                    var smelting = Smelting(ore, units, recovery.SmeltShare(recovery.Ore(ore), form));
                    item.CombustibleProps = smelting;
                    if (smelting != null)
                        item.Dimensions = CrucibleSize;
                    if (chunkOfOre) report.OreItems++;
                    else report.CrystallisedOre++;
                    break;
                }
                case "nugget" when code.Domain == "game" && parts.Length == 2 && OreProducts.OreOfNugget(parts[1]) is { } nuggetOre:
                    item.CrushingProps = Crushing(world, item, OreProducts.CrushedCode(nuggetOre, OreGrain.Coarse),
                        OreProducts.CrushedCount(perItem, perItem));
                    report.Nuggets++;
                    break;
                case "crushed" when code.Domain == "game" && parts.Length == 3 && ores.Contains(parts[1]):
                    item.CombustibleProps = Smelting(parts[1], perItem, recovery.SmeltShare(recovery.Ore(parts[1]), OreForm.Crushed));
                    report.Crushed++;
                    break;
                case "concentrate" when code.Domain == OreProducts.Domain && parts.Length == 2:
                {
                    var spec = recovery.Ore(parts[1]);
                    if (OreProducts.Roasts(spec))
                    {
                        item.CombustibleProps = Roasting(world, item, OreProducts.RoastedCode(parts[1]), roastSeconds);
                        if (item is ItemOreProduct product)
                            product.RoastShare = item.CombustibleProps == null ? 0 : recovery.Roasting(spec, Roaster.Firepit);
                        if (item.CombustibleProps != null)
                            report.Roasting++;
                    }
                    else
                        item.CombustibleProps = Smelting(parts[1], perItem, recovery.SmeltShare(spec, OreForm.Concentrate));
                    report.Concentrate++;
                    break;
                }
                case "roastedconcentrate" when code.Domain == OreProducts.Domain && parts.Length == 2:
                    item.CombustibleProps = Smelting(parts[1], perItem, recovery.SmeltShare(recovery.Ore(parts[1]), OreForm.RoastedConcentrate));
                    report.Roasted++;
                    break;
                case "litharge" when code.Domain == OreProducts.Domain && parts.Length == 1:
                    item.CombustibleProps = Smelting("galena", perItem, 1, OreProducts.LithargeRate);
                    report.Litharge++;
                    break;
            }
        }
        // Loose argentiferous galena gave a native silver nugget (vanilla's "*_nativesilver-*" drop); a
        // lead ore now (#690), it gives a galena nugget, its silver left to cupellation.
        if (world.GetItem(new AssetLocation("game", "nugget-" + OreProducts.NuggetOf(OreProducts.Argentiferous))) is { } leadNugget)
            foreach (var block in world.Blocks)
                if (block?.Code is { Domain: "game" } bc && bc.Path.StartsWith("looseores-" + OreProducts.Argentiferous + "-", StringComparison.Ordinal))
                {
                    block.Drops = [new BlockDropItemStack(new ItemStack(leadNugget))];
                    report.LooseOres++;
                }
        logger.Notification("[seraphhorizons] Ore processing: {0} ore items and {1} crystallised ore by grade, {2} nuggets, "
                            + "{3} crushed, {4} concentrate ({8} roasting in the firepit), {5} roasted concentrate, {6} litharge set; "
                            + "forms of {7} do not smelt (their nugget does not)",
            report.OreItems, report.CrystallisedOre, report.Nuggets, report.Crushed, report.Concentrate, report.Roasted,
            report.Litharge, report.NoSmeltTarget.Count == 0 ? "no ore" : string.Join(", ", report.NoSmeltTarget), report.Roasting);
        if (report.NoUnits.Count > 0)
            logger.Warning("[seraphhorizons] Ore processing: {0} ore items have no metalUnits and are left as they are: {1}",
                report.NoUnits.Count, string.Join(", ", report.NoUnits.Take(10)));
        return report;
    }

    /// <summary>Crushing into <paramref name="count"/> of <paramref name="crushedCode"/>, exactly (no
    /// random spread), at the item's own hardness tier if it had one (the pulverizer's pounders cap
    /// it), else 2.</summary>
    public static CrushingProperties? Crushing(IWorldAccessor world, CollectibleObject item, string crushedCode, int count)
    {
        var stack = new JsonItemStack { Type = EnumItemClass.Item, Code = new AssetLocation(crushedCode), StackSize = count };
        if (!stack.Resolve(world, "ore processing crushing of " + item.Code, printWarningOnError: false))
            return item.CrushingProps;
        return new CrushingProperties
        {
            CrushedStack = stack,
            Quantity = NatFloat.One,
            HardnessTier = item.CrushingProps?.HardnessTier ?? 2,
        };
    }

    /// <summary>A sulfide concentrate's roasting (#720): into one <paramref name="roastedCode"/> per item
    /// in the game's terms (the share is <see cref="ItemOreProduct.DoSmelt"/>'s), at
    /// <see cref="OreRoasting.MeltingPoint"/> for <paramref name="seconds"/>, with no container, so any
    /// firepit on any fuel takes it and the crucible refuses it. Null if the roasted item is missing.</summary>
    public static CombustibleProperties? Roasting(IWorldAccessor world, CollectibleObject item, string roastedCode, float seconds)
    {
        var stack = new JsonItemStack { Type = EnumItemClass.Item, Code = new AssetLocation(roastedCode), StackSize = 1 };
        if (!stack.Resolve(world, "ore processing roasting of " + item.Code, printWarningOnError: false))
            return null;
        return new CombustibleProperties
        {
            SmeltedStack = stack,
            SmeltedRatio = 1,
            MeltingPoint = OreRoasting.MeltingPoint,
            MeltingDuration = seconds,
            RequiresContainer = false,
            SmeltingType = EnumSmeltType.Convert,
        };
    }

    /// <summary>A copy of <paramref name="like"/> (the nugget's smelting) at <paramref name="rate"/>.</summary>
    public static CombustibleProperties WithRate(IWorldAccessor world, CombustibleProperties like, SmeltRate rate)
    {
        var props = like.Clone();
        props.SmeltedRatio = rate.Ratio;
        props.SmeltedStack.StackSize = rate.Output;
        props.SmeltedStack.Resolve(world, "ore processing smelting", true);
        if (props.SmeltedStack.ResolvedItemstack != null)
            props.SmeltedStack.ResolvedItemstack.StackSize = rate.Output;
        return props;
    }
}
