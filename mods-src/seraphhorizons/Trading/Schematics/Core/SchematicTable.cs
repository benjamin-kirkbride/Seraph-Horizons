using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Trading.Schematics.Core;

/// <summary>A collectible code pattern and the mod it belongs to (the gate or rule is skipped
/// without that mod). <c>*</c> matches any run of characters; a code without a domain is
/// <c>game:</c>'s.</summary>
public sealed class ModCode
{
    public string Mod { get; set; } = "game";
    public string Code { get; set; } = "";

    public bool Matches(string code) => CodePattern.Matches(Code, code);

    public override string ToString() => $"{Code} ({Mod})";
}

/// <summary>One machine's gate: the schematic <c>seraphhorizons:schematic-{machine}</c> that every
/// grid recipe making one of <see cref="Outputs"/> takes, kept on crafting.</summary>
public sealed class SchematicGate
{
    public string Machine { get; set; } = "";
    /// <summary>The recipe outputs gated: the machine's first stage where it is built in stages.</summary>
    public List<ModCode> Outputs { get; set; } = [];

    public string Schematic => SchematicTable.MachineSchematicPrefix + Machine;
}

/// <summary>Where a schematic is sold: the trader types whose core carries it, and the standing
/// tier from which it is there. The trade lists hold the real entries (with prices); this is what
/// the tests hold them to.</summary>
public sealed class SchematicSale
{
    public string Code { get; set; } = "";
    public string Mod { get; set; } = "game";
    public List<string> Sellers { get; set; } = [];
    public int Tier { get; set; }
}

/// <summary>
/// <c>config/schematic-gates.json</c> (#468, #469): which collectibles are schematics sold only by
/// traders (<see cref="Sold"/>: taken out of loot and structures, not copyable, kept on crafting),
/// the machines' gates (<see cref="Gates"/>), and who sells each schematic (<see cref="Sales"/>).
/// </summary>
public sealed class SchematicTable
{
    public const string MachineSchematicPrefix = "seraphhorizons:schematic-";

    public List<ModCode> Sold { get; set; } = [];
    public List<SchematicGate> Gates { get; set; } = [];
    public List<SchematicSale> Sales { get; set; } = [];
    /// <summary>What a schematic in a structure's chest becomes.</summary>
    public string Replacement { get; set; } = "game:paper-parchment";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static SchematicTable Parse(string json) =>
        JsonSerializer.Deserialize<SchematicTable>(json, Options) ?? throw new InvalidDataException("empty schematic table");

    public bool IsSold(string code) => Sold.Any(s => s.Matches(code));

    public bool IsMachineSchematic(string code) => CodePattern.Normalise(code).StartsWith(MachineSchematicPrefix, StringComparison.Ordinal);

    /// <summary>The gate of a recipe output, among the gates whose mods are loaded.</summary>
    public SchematicGate? GateFor(string outputCode, Func<string, bool> modLoaded) =>
        Gates.FirstOrDefault(g => g.Outputs.Any(o => modLoaded(o.Mod) && o.Matches(outputCode)));

    /// <summary>What is wrong with the table: a gate without outputs, a machine twice, an output in
    /// two gates, a sale without sellers or out of the tier range, a machine sold nowhere.</summary>
    public List<string> Problems(IReadOnlyCollection<string> traderTypes, int maxTier)
    {
        var problems = new List<string>();
        foreach (var dup in Gates.GroupBy(g => g.Machine).Where(g => g.Count() > 1))
            problems.Add($"machine '{dup.Key}' has {dup.Count()} gates");
        foreach (var g in Gates)
        {
            if (string.IsNullOrWhiteSpace(g.Machine) || g.Machine.Contains(' ')) problems.Add($"gate '{g.Machine}' has no usable name");
            if (g.Outputs.Count == 0) problems.Add($"{g.Schematic} gates nothing");
            if (!Sales.Any(s => CodePattern.Normalise(s.Code) == g.Schematic)) problems.Add($"{g.Schematic} is sold by no one");
        }
        foreach (var dup in Gates.SelectMany(g => g.Outputs.Select(o => (o.Code, g.Machine))).GroupBy(x => x.Code).Where(x => x.Count() > 1))
            problems.Add($"output {dup.Key} is in gates {string.Join(", ", dup.Select(x => x.Machine))}");
        foreach (var s in Sales)
        {
            if (!IsSold(s.Code)) problems.Add($"{s.Code} is sold but not in 'sold'");
            if (s.Sellers.Count == 0) problems.Add($"{s.Code} has no sellers");
            foreach (string t in s.Sellers.Where(t => !traderTypes.Contains(t))) problems.Add($"{s.Code}: no trader type '{t}'");
            if (s.Tier < 1 || s.Tier > maxTier) problems.Add($"{s.Code}: tier {s.Tier} is outside 1..{maxTier}");
        }
        foreach (var dup in Sales.GroupBy(s => CodePattern.Normalise(s.Code)).Where(g => g.Count() > 1))
            problems.Add($"{dup.Key} is in 'sales' {dup.Count()} times");
        return problems;
    }
}

/// <summary>Collectible code patterns as the game writes them: <c>*</c> for any run of characters,
/// <c>game:</c> when no domain is given.</summary>
public static class CodePattern
{
    public static string Normalise(string code) => code.Contains(':') ? code : "game:" + code;

    public static bool Matches(string pattern, string code)
    {
        string p = Normalise(pattern), c = Normalise(code);
        return Glob(p, 0, c, 0);
    }

    private static bool Glob(string p, int pi, string c, int ci)
    {
        while (pi < p.Length)
        {
            if (p[pi] == '*')
            {
                for (int k = ci; k <= c.Length; k++)
                    if (Glob(p, pi + 1, c, k)) return true;
                return false;
            }
            if (ci >= c.Length || p[pi] != c[ci]) return false;
            pi++;
            ci++;
        }
        return ci == c.Length;
    }
}
