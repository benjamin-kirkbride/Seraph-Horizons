namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>One product of a concentrator's pass: an item code and its units, which a
/// <see cref="UnitCarry"/> makes whole items of at <paramref name="UnitsPerItem"/> each (a concentrate's
/// <see cref="OreRecovery.ConcentrateUnits"/>, a nugget or by-product 1).</summary>
public sealed record WashProduct(string Code, double Units, double UnitsPerItem);

/// <summary>What one item through a concentrator gives: its products and the tailings stream that goes on.</summary>
/// <param name="Whole">The concentrate came off the whole ideal route (<see cref="ConcentratorPass.Whole"/>).</param>
public sealed record WashResult(IReadOnlyList<WashProduct> Products, OreStream Tailings, OreStream? Middlings, bool Whole);

/// <summary>
/// One item through a concentrator (the sluice, jig, shaking table, vanner; #765, #701): the recovery core's
/// pass (<see cref="OreRecovery.Concentrate"/>) turned into products. Crushed or ground ore gives its ore's
/// concentrate; placer gravel the gold pan's table scaled by what the pass caught (<see cref="PlacerLine.GravelYield"/>);
/// bog iron limonite concentrate. The tailings carry the rest on.
/// </summary>
public static class ConcentratorWash
{
    /// <summary>The pass of <paramref name="machine"/> on <paramref name="feed"/>. <paramref name="gravelDrops"/>
    /// gives the pan's table for placer gravel (code, chance a pan, metal), asked for only then.</summary>
    public static WashResult Wash(OreRecovery recovery, OreMachine machine, OreStream feed,
        Func<IEnumerable<(string Code, double ChancePerPan, bool Metal)>>? gravelDrops = null)
    {
        var pass = recovery.Concentrate(machine, feed);
        IReadOnlyList<WashProduct> products;
        switch (feed.Ore)
        {
            case PlacerLine.GravelOre:
                bool first = feed.Worked == WorkedBy.None;
                products = gravelDrops == null
                    ? []
                    : PlacerLine.GravelYield(pass.Concentrate, first, recovery.Figure(HandStation.Pan), gravelDrops())
                        .Select(o => new WashProduct(o.Code, o.Count, 1)).ToList();
                break;
            case PlacerLine.BogIronOre:
                products = Concentrate(PlacerLine.BogIronConcentrateOre, pass.Concentrate, recovery);
                break;
            default:
                products = Concentrate(feed.Ore, pass.Concentrate, recovery);
                break;
        }
        return new WashResult(products, pass.Tailings, pass.Middlings, pass.Whole);
    }

    private static IReadOnlyList<WashProduct> Concentrate(string ore, double units, OreRecovery recovery) =>
        units > 0 ? [new WashProduct(OreProducts.ConcentrateCode(ore), units, recovery.ConcentrateUnits)] : [];
}
