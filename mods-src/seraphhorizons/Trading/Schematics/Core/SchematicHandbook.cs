using System.Globalization;

namespace SeraphHorizons.Mod.Trading.Schematics.Core;

/// <summary>One line of a schematic's handbook section: its text, and whether the rusty gear's
/// icon goes before it (a seller's line, as the value line has it).</summary>
public sealed record SchematicHandbookLine(string Text, bool Gear = false);

/// <summary>
/// What a schematic's handbook page says in place of the value line (README "Schematics"): who
/// sells it from their catalog, for how much and from which standing tier, and that no trader buys
/// it. Game-independent; <c>Trading/Values/Game/ValueHandbook.cs</c> draws it on the client.
///
/// <para>Only for what no trader buys (<see cref="SchematicTable.NeverBoughtBack"/>: a schematic or
/// a rolled copy of one); null for anything else, which keeps its value line. The "Sold by" part
/// follows <see cref="SchematicCatalog.Sellers"/>: none when nobody sells it (a rolled copy, or a
/// machine schematic whose switch is off). The price is flat, the same for every buyer, so one
/// number. With standing off (<see cref="StandingSwitch"/>) or a tier 0 sale, there is no standing
/// to reach.</para>
/// </summary>
public static class SchematicHandbook
{
    /// <summary>The switch without which nothing is locked behind standing.</summary>
    public const string StandingSwitch = "TraderStanding";

    public const string Domain = "seraphhorizons:";

    /// <summary>
    /// The section's lines for <paramref name="code"/>, or null when it is not a schematic.
    /// <paramref name="tierCode"/> gives a tier index's code (<c>config/standing-tiers.json</c>);
    /// <paramref name="lang"/> is <c>Lang.Get</c> (a parameter for the tests).
    /// </summary>
    public static List<SchematicHandbookLine>? Lines(SchematicTable table, string code, Func<string, bool> isOff,
        Func<int, string> tierCode, Func<string, object[], string> lang)
    {
        if (!table.NeverBoughtBack(code)) return null;
        var lines = new List<SchematicHandbookLine>();
        if (SchematicCatalog.Sellers(table, code, isOff) is { } sold)
        {
            lines.Add(new(lang(Domain + "trading-schematics-handbook-soldby", [])));
            bool standing = !isOff(StandingSwitch) && sold.Tier > 0;
            string tier = standing ? Capitalise(lang(Domain + "trading-standing-tier-" + tierCode(sold.Tier), [])) : "";
            foreach (string seller in sold.Sellers)
            {
                string type = Capitalise(lang(Domain + "trading-type-" + seller, []));
                lines.Add(new(standing
                    ? lang(Domain + "trading-schematics-handbook-seller", [type, sold.Price, tier])
                    : lang(Domain + "trading-schematics-handbook-seller-anyone", [type, sold.Price]), Gear: true));
            }
        }
        lines.Add(new(lang(Domain + "trading-economy-refused-schematic", [])));
        return lines;
    }

    private static string Capitalise(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0], CultureInfo.InvariantCulture) + s[1..];
}
