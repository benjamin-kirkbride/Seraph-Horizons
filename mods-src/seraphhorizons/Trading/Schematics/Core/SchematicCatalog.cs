namespace SeraphHorizons.Mod.Trading.Schematics.Core;

/// <summary>One schematic in a trader type's catalog: its full code, flat price in gears, the
/// standing tier (an index, 0 stranger … 4 partner) that unlocks it, and whether the viewer's tier
/// reaches it.</summary>
public sealed record CatalogRow(string Code, int Price, int Tier, bool Unlocked);

/// <summary>
/// The schematic catalogs (README "Schematics"): what each trader type sells off the shelf, on the
/// trade window's Schematics tab, from <see cref="SchematicTable.Sales"/>. Game-independent, for the
/// server's window state and purchase, and for anything else that says who sells a schematic (the
/// handbook, the recipe site).
///
/// <para>A sale is in the catalogs (<see cref="ForSale"/>) unless a switch takes it out:
/// <paramref name="isOff"/> answers by a <c>SeraphHorizonsConfig</c> bool's name, as the world
/// config's <c>seraphhorizons:switchesOff</c> list does (<c>ValueHandbook.OffKey</c>,
/// <c>SwitchOwnership.DecodeOff(...).Contains</c>). A machine schematic
/// (<c>seraphhorizons:schematic-*</c>) needs <see cref="MachineSwitch"/> on and its gate's own
/// <see cref="SchematicGate.Switch"/> (if any) on.</para>
///
/// <para>A row is unlocked when the viewer's tier is at least its tier; the server passes the best
/// tier with that trader among the player and the members of every player group they belong to
/// (<see cref="GroupTier"/>). A null tier means standing is off: everything is unlocked.</para>
/// </summary>
public static class SchematicCatalog
{
    /// <summary>The switch without which no machine schematic is for sale (and no recipe takes one).</summary>
    public const string MachineSwitch = "MachineSchematics";

    /// <summary>Whether the sale is in the catalogs with these switches.</summary>
    public static bool ForSale(SchematicTable table, SchematicSale sale, Func<string, bool> isOff)
    {
        if (!table.IsMachineSchematic(sale.Code)) return true;
        if (isOff(MachineSwitch)) return false;
        return table.GateOf(sale.Code)?.Switch is not { Length: > 0 } sw || !isOff(sw);
    }

    /// <summary>
    /// The catalog of trader type <paramref name="traderType"/>: every sale it sells that is for sale
    /// (<see cref="ForSale"/>), lowest tier first, then cheapest, then by code; each unlocked when
    /// <paramref name="tier"/> reaches it (null: standing off, all unlocked).
    /// </summary>
    public static List<CatalogRow> For(SchematicTable table, string traderType, Func<string, bool> isOff, int? tier) =>
        table.Sales
            .Where(s => s.Sellers.Contains(traderType) && ForSale(table, s, isOff))
            .Select(s => new CatalogRow(CodePattern.Normalise(s.Code), s.Price, s.Tier, tier is not int t || t >= s.Tier))
            .OrderBy(r => r.Tier).ThenBy(r => r.Price).ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>The sale of a schematic that is for sale, or null (unknown, or switched off).</summary>
    public static SchematicSale? Sale(SchematicTable table, string code, Func<string, bool> isOff)
    {
        string c = CodePattern.Normalise(code);
        return table.Sales.FirstOrDefault(s => CodePattern.Normalise(s.Code) == c && ForSale(table, s, isOff));
    }

    /// <summary>The trader types that sell <paramref name="code"/>, with its tier and price, or
    /// null when nobody does (for "Sold by" lines).</summary>
    public static (IReadOnlyList<string> Sellers, int Tier, int Price)? Sellers(SchematicTable table, string code, Func<string, bool> isOff) =>
        Sale(table, code, isOff) is { } s ? (s.Sellers, s.Tier, s.Price) : null;

    /// <summary>The tier that unlocks catalogs for a player: the best of their own and their group
    /// members' tiers with the trader.</summary>
    public static int GroupTier(int own, IEnumerable<int> members) => members.Aggregate(own, Math.Max);
}
