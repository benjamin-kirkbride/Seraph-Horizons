using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Sets ore processing's rules on the loaded items (#686, #687, #688, #847), on the server once every
/// item is loaded and patched, so the result does not depend on the order other mods' patches ran in:
/// <list type="bullet">
/// <item>vanilla's ore item <c>game:ore-{grade}-{ore}-{rock}</c> is raw ore at every grade, 16 a stack
/// (no chunks, #863), and crushes to one <c>seraphhorizons:crushedore-{ore}-{grade}</c>, which holds
/// its units. Its <c>metalUnits</c> are the pack's (<see cref="OreProducts.RawUnits"/>, set by
/// <c>patches/oreprocessing-ore.json</c>), which deposit measurement reads; the ore blocks' drops are
/// left as they are. Crystallised ore crushes to as many crushed ore of its grade as its units make
/// (two), its stack left alone;</item>
/// <item>every rock form (raw, crushed and ground ore, crystallised ore) smelts at its grade's share
/// (<see cref="OreRecovery.SmeltShare"/>), and fits a crucible; a sulfide's does not smelt: its raw
/// and crushed ore roast in the firepit (#720), any fuel, one for one into roasted rock of its grade
/// at <see cref="OreRoasting.MeltingPoint"/>, in the configured seconds per 5 units it holds; roasted
/// rock smelts at its grade's share times the firepit's;</item>
/// <item>nuggets no longer crush (Expanded Matter's and smex's nugget crushing taken off, whatever order
/// those patches ran in): a nugget is already metal, and smelts whole;</item>
/// <item>the quern grinds no ore: grinding is taken off every ore item, and the game's and Expanded
/// Matter's crushed ores that recipes once took (<see cref="OreProcessingSystem.LegacyCrushed"/>, and
/// the game's crushed iron);</item>
/// <item>concentrate smelts whole, roasted concentrate whole, litharge to lead at 20 in 21, retorted
/// sponge (the gold or silver an amalgam leaves in the still, #726) whole; amalgam does not smelt;</item>
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
        public int OreItems, CrystallisedOre, Nuggets, Crushed, Ground, RoastedRock, Concentrate, Roasted, Sponge, Litharge, LooseOres;
        /// <summary>Bullion and rich lead items set to smelt plainly (#740), and smeltings pointed at a
        /// by-product ore's bullion.</summary>
        public int Bullion, ToBullion;
        /// <summary>By-product ores whose bullion item is missing: their forms smelt to the plain metal.</summary>
        public SortedSet<string> NoBullion = new(StringComparer.Ordinal);
        /// <summary>Sulfide concentrates, raw ore and crushed ore that roast in the firepit.</summary>
        public int Roasting;
        /// <summary>Items the quern no longer takes.</summary>
        public int QuernOff;
        /// <summary>Ores whose nugget does not smelt (or is missing): their forms do not smelt.</summary>
        public SortedSet<string> NoSmeltTarget = new(StringComparer.Ordinal);
        /// <summary>Ore items with no <c>metalUnits</c>: left as they are.</summary>
        public List<string> NoUnits = [];
        /// <summary>Rock forms whose units are not the raw ore's of their ore and grade: carried units
        /// would be off, so the type file wants fixing.</summary>
        public List<string> UnitsOff = [];
    }

    /// <summary>The size an item must be within to go in the game's crucible (its
    /// <c>maxContentDimensions</c> is 0.125 across); raw ore is given it, as a nugget has.</summary>
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

        // A by-product ore's forms smelt to its bullion (#740): the nugget's smelting, into the bar. Null
        // (the plain metal) if the bullion item is missing.
        var bullionLike = new Dictionary<string, CombustibleProperties?>(StringComparer.Ordinal);
        CombustibleProperties? BullionTarget(OreSpec spec, CombustibleProperties plain)
        {
            if (!bullionLike.TryGetValue(spec.Ore, out var props))
            {
                props = plain.Clone();
                props.SmeltedStack = new JsonItemStack { Type = EnumItemClass.Item, Code = new AssetLocation(Bullion.Code(spec)), StackSize = 1 };
                if (!props.SmeltedStack.Resolve(world, "ore processing bullion of " + spec.Ore, printWarningOnError: false))
                {
                    report.NoBullion.Add(spec.Ore);
                    props = null;
                }
                bullionLike[spec.Ore] = props;
            }
            return props;
        }

        // An ore's smelting at a share of its units; a by-product ore's into its bullion unless plain
        // (litharge, rich lead and bullion itself smelted again give the plain metal).
        CombustibleProperties? Smelting(string ore, double unitsPerItem, double share, SmeltRate? fixedRate = null, bool plain = false)
        {
            var rate = fixedRate ?? OreProducts.Rate(unitsPerItem, share);
            if (rate is not { } r || Target(ore) is not { } target)
                return null;
            var spec = recovery.Ore(ore);
            if (!plain && Bullion.Makes(spec) && BullionTarget(spec, target) is { } bullion)
            {
                report.ToBullion++;
                target = bullion;
            }
            return WithRate(world, target, r);
        }

        // A rock form's smelting: by its grade, or, for a sulfide's raw and crushed ore, its roast
        // into roasted rock (none for anything else of a sulfide's).
        CombustibleProperties? Rock(CollectibleObject item, string ore, string grade, double units, OreForm form, bool roasts)
        {
            var spec = recovery.Ore(ore);
            if (spec.IsSulfide && form is OreForm.Raw or OreForm.Crushed)
            {
                if (!roasts)
                    return null;
                var roast = Roasting(world, item, OreProducts.RoastedRockCode(ore, grade), OreRoasting.RockSeconds(roastSeconds, units, perItem));
                if (roast != null)
                    report.Roasting++;
                return roast;
            }
            return Smelting(ore, units, recovery.SmeltShare(spec, form, grade));
        }

        foreach (var item in world.Items)
        {
            if (item?.Code == null)
                continue;
            if (QuernTakesNoMore(item))
            {
                item.GrindingProps = null;
                report.QuernOff++;
            }
            if (item.Code is not { Domain: "game" or OreProducts.Domain } code)
                continue;
            var parts = code.Path.Split('-');
            switch (parts[0])
            {
                case "ore" or "crystalizedore" when code.Domain == "game" && parts.Length == 4 && ores.Contains(parts[2])
                                                   && OreProducts.IsGrade(parts[1]):
                {
                    string grade = parts[1], ore = parts[2];
                    double units = ItemOreProduct.UnitsOf(item);
                    if (!(units > 0))
                    {
                        report.NoUnits.Add(code.ToString());
                        continue;
                    }
                    bool raw = parts[0] == "ore";
                    if (raw)
                        item.MaxStackSize = OreProducts.RockStack;
                    var crushed = world.GetItem(new AssetLocation(OreProducts.CrushedCode(ore, grade)));
                    double crushedUnits = crushed != null ? ItemOreProduct.UnitsOf(crushed) : 0;
                    if (raw && crushed != null && crushedUnits != units)
                        report.UnitsOff.Add($"{crushed.Code} ({crushedUnits}, raw {units})");
                    item.CrushingProps = Crushing(world, item, OreProducts.CrushedCode(ore, grade),
                        OreProducts.CrushedCount(units, crushedUnits > 0 ? crushedUnits : units));
                    // Crystallised ore crushes to two raw ores' worth; roasted whole it would carry
                    // only one, so it is crushed first.
                    var smelting = Rock(item, ore, grade, units, OreForm.Raw, roasts: raw);
                    item.CombustibleProps = smelting;
                    if (smelting is { RequiresContainer: true })
                        item.Dimensions = CrucibleSize;
                    if (raw) report.OreItems++;
                    else report.CrystallisedOre++;
                    break;
                }
                case "nugget" when code.Domain == "game" && parts.Length == 2:
                    if (item.CrushingProps != null)
                    {
                        item.CrushingProps = null;
                        report.Nuggets++;
                    }
                    break;
                case "crushedore" or "groundore" or "roastedore" when code.Domain == OreProducts.Domain && parts.Length == 3
                                                                     && ores.Contains(parts[1]) && OreProducts.IsGrade(parts[2]):
                {
                    string ore = parts[1], grade = parts[2];
                    double units = ItemOreProduct.UnitsOf(item);
                    if (OreProducts.RawUnits(ore, grade) is { } expected && units != expected)
                        report.UnitsOff.Add($"{code} ({units}, raw {expected})");
                    var form = parts[0] switch { "crushedore" => OreForm.Crushed, "groundore" => OreForm.Ground, _ => OreForm.RoastedRock };
                    item.CombustibleProps = units > 0 ? Rock(item, ore, grade, units, form, roasts: true) : null;
                    if (form == OreForm.Crushed) report.Crushed++;
                    else if (form == OreForm.Ground) report.Ground++;
                    else report.RoastedRock++;
                    break;
                }
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
                case "sponge" when code.Domain == OreProducts.Domain && parts.Length == 2:
                    // The gold or silver an amalgam leaves in the still (#726): whole, as concentrate.
                    item.CombustibleProps = Smelting(parts[1], perItem, recovery.SmeltShare(recovery.Ore(parts[1]), OreForm.Sponge));
                    report.Sponge++;
                    break;
                case "litharge" when code.Domain == OreProducts.Domain && parts.Length == 1:
                    item.CombustibleProps = Smelting("galena", perItem, 1, OreProducts.LithargeRate, plain: true);
                    report.Litharge++;
                    break;
                case "bullion" when code.Domain == OreProducts.Domain && parts.Length == 3 && ores.Contains(parts[1]):
                {
                    // Bullion smelted plainly (#740): its main metal at the ore's unparted share, the
                    // by-product lost.
                    var spec = recovery.Ore(parts[1]);
                    item.CombustibleProps = Smelting(parts[1], Bullion.UnitsPerItem, Bullion.PlainShare(spec), plain: true);
                    if (item.CombustibleProps != null) item.Dimensions = CrucibleSize;
                    report.Bullion++;
                    break;
                }
                case "richlead" when code.Domain == OreProducts.Domain && parts.Length == 1:
                    // The Parkes kettle's rich lead smelted plainly: its lead, the silver lost.
                    item.CombustibleProps = Smelting("galena", Bullion.UnitsPerItem, 1, plain: true);
                    if (item.CombustibleProps != null) item.Dimensions = CrucibleSize;
                    report.Bullion++;
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
        logger.Notification("[seraphhorizons] Ore processing: {0} ore items and {1} crystallised ore by grade, {2} crushed, {3} ground and "
                            + "{4} roasted rock, {5} concentrate, {6} roasted concentrate, {7} litharge set ({8} roasting in the firepit); "
                            + "{9} nuggets no longer crush, {10} items no longer grind in the quern; forms of {11} do not smelt (their nugget does not)",
            report.OreItems, report.CrystallisedOre, report.Crushed, report.Ground, report.RoastedRock, report.Concentrate, report.Roasted,
            report.Litharge, report.Roasting, report.Nuggets, report.QuernOff,
            report.NoSmeltTarget.Count == 0 ? "no ore" : string.Join(", ", report.NoSmeltTarget));
        logger.Notification("[seraphhorizons] Ore processing: {0} smeltings of by-product ores go to their bullion, {1} bullion and rich lead "
                            + "items smelt plainly{2}", report.ToBullion, report.Bullion,
            report.NoBullion.Count == 0 ? "" : "; no bullion item for " + string.Join(", ", report.NoBullion));
        if (report.NoUnits.Count > 0)
            logger.Warning("[seraphhorizons] Ore processing: {0} ore items have no metalUnits and are left as they are: {1}",
                report.NoUnits.Count, string.Join(", ", report.NoUnits.Take(10)));
        if (report.UnitsOff.Count > 0)
            logger.Warning("[seraphhorizons] Ore processing: {0} rock forms do not hold their raw ore's units, so crushing or grinding "
                           + "changes the metal: {1}", report.UnitsOff.Count, string.Join(", ", report.UnitsOff.Take(10)));
        return report;
    }

    /// <summary>
    /// Whether the quern no longer takes <paramref name="item"/> (#847: there is no hand grinding;
    /// grinding starts at the water tier): vanilla's graded and crystallised ore, ore processing's
    /// own forms, and the game's and Expanded Matter's crushed ores that recipes once took
    /// (<see cref="OreProcessingSystem.LegacyCrushed"/>, and the game's crushed iron), whatever grinding
    /// a mod gave them.
    /// </summary>
    public static bool QuernTakesNoMore(CollectibleObject item)
    {
        if (item.GrindingProps == null || item.Code is not { } code)
            return false;
        var parts = code.Path.Split('-');
        if (code.Domain == "game" && parts[0] is "ore" or "crystalizedore" && parts.Length == 4 && OreProducts.IsGrade(parts[1]))
            return true;
        if (code.Domain == OreProducts.Domain && item is ItemOreProduct)
            return true;
        string full = code.ToString();
        return full == "game:crushed-iron"
               || (OreProcessingSystem.LegacyCrushed.ContainsKey(full) && !full.StartsWith("em:crushed-metal-", StringComparison.Ordinal));
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

    /// <summary>A sulfide's roasting (#720): into one <paramref name="roastedCode"/> per item in the game's
    /// terms (for concentrate the share is <see cref="ItemOreProduct.DoSmelt"/>'s; rock roasts one for
    /// one), at
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
