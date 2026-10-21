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

    /// <summary>The pack's ModConfig switches (<c>SeraphHorizonsConfig</c> bools) the machine needs,
    /// none, one or several: with any off, the schematic is for sale nowhere
    /// (<see cref="SchematicCatalog"/>). In the JSON, <c>switch</c> is a name or a list of names.</summary>
    [JsonPropertyName("switch")]
    [JsonConverter(typeof(OneOrManyConverter))]
    public List<string> Switches { get; set; } = [];

    public string Schematic => SchematicTable.MachineSchematicPrefix + Machine;
}

/// <summary>A schematic in the catalogs (the trade window's Schematics tab, never a shelf): the
/// trader types that sell it, the standing tier (an index, 0 stranger … 4 partner) from which it
/// is unlocked, and its flat price in gears.</summary>
public sealed class SchematicSale
{
    public string Code { get; set; } = "";
    public string Mod { get; set; } = "game";
    public List<string> Sellers { get; set; } = [];
    public int Tier { get; set; }
    /// <summary>Gears, the same for every buyer: no standing factor, no supply, no variation.</summary>
    public int Price { get; set; }
}

/// <summary>
/// <c>config/schematic-gates.json</c> (#468, #469): which collectibles are schematics sold only by
/// traders (<see cref="Sold"/>: taken out of loot and structures, not copyable, kept on crafting,
/// never bought back), Scrolled's rolled copies of them (<see cref="RolledCopies"/>, never bought
/// back either), the machines' gates (<see cref="Gates"/>), and each trader type's catalog
/// (<see cref="Sales"/>, resolved by <see cref="SchematicCatalog"/>).
/// </summary>
public sealed class SchematicTable
{
    public const string MachineSchematicPrefix = "seraphhorizons:schematic-";

    public List<ModCode> Sold { get; set; } = [];
    public List<ModCode> RolledCopies { get; set; } = [];
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

    /// <summary>A trader never buys it, listed or not (<c>Refusal.Schematic</c>): a sold schematic
    /// or a rolled copy of one.</summary>
    public bool NeverBoughtBack(string code) => IsSold(code) || RolledCopies.Any(r => r.Matches(code));

    public bool IsMachineSchematic(string code) => CodePattern.Normalise(code).StartsWith(MachineSchematicPrefix, StringComparison.Ordinal);

    /// <summary>The gate of a recipe output, among the gates whose mods are loaded.</summary>
    public SchematicGate? GateFor(string outputCode, Func<string, bool> modLoaded) =>
        Gates.FirstOrDefault(g => g.Outputs.Any(o => modLoaded(o.Mod) && o.Matches(outputCode)));

    /// <summary>The gate whose schematic <paramref name="schematicCode"/> is, or null (not a machine's).</summary>
    public SchematicGate? GateOf(string schematicCode)
    {
        string code = CodePattern.Normalise(schematicCode);
        return Gates.FirstOrDefault(g => g.Schematic == code);
    }

    /// <summary>What is wrong with the table: a gate without outputs, a machine twice, an output in
    /// two gates, a switch that is not one of <paramref name="switches"/> (when given), a sale
    /// without sellers, out of the tier range or without a price, a machine sold nowhere.</summary>
    public List<string> Problems(IReadOnlyCollection<string> traderTypes, int maxTier, IReadOnlyCollection<string>? switches = null)
    {
        var problems = new List<string>();
        foreach (var dup in Gates.GroupBy(g => g.Machine).Where(g => g.Count() > 1))
            problems.Add($"machine '{dup.Key}' has {dup.Count()} gates");
        foreach (var g in Gates)
        {
            if (string.IsNullOrWhiteSpace(g.Machine) || g.Machine.Contains(' ')) problems.Add($"gate '{g.Machine}' has no usable name");
            if (g.Outputs.Count == 0) problems.Add($"{g.Schematic} gates nothing");
            if (!Sales.Any(s => CodePattern.Normalise(s.Code) == g.Schematic)) problems.Add($"{g.Schematic} is sold by no one");
            foreach (string sw in g.Switches.Where(sw => sw.Length == 0 || switches != null && !switches.Contains(sw)))
                problems.Add($"{g.Schematic}: no switch '{sw}'");
            foreach (var dup in g.Switches.GroupBy(sw => sw).Where(d => d.Count() > 1))
                problems.Add($"{g.Schematic}: switch '{dup.Key}' twice");
        }
        foreach (var dup in Gates.SelectMany(g => g.Outputs.Select(o => (o.Code, g.Machine))).GroupBy(x => x.Code).Where(x => x.Count() > 1))
            problems.Add($"output {dup.Key} is in gates {string.Join(", ", dup.Select(x => x.Machine))}");
        foreach (var s in Sales)
        {
            if (!IsSold(s.Code)) problems.Add($"{s.Code} is sold but not in 'sold'");
            if (s.Sellers.Count == 0) problems.Add($"{s.Code} has no sellers");
            foreach (string t in s.Sellers.Where(t => !traderTypes.Contains(t))) problems.Add($"{s.Code}: no trader type '{t}'");
            foreach (var dup in s.Sellers.GroupBy(t => t).Where(g => g.Count() > 1)) problems.Add($"{s.Code}: seller '{dup.Key}' twice");
            if (s.Tier < 0 || s.Tier > maxTier) problems.Add($"{s.Code}: tier {s.Tier} is outside 0..{maxTier}");
            if (s.Price <= 0) problems.Add($"{s.Code} has no price");
            if (IsMachineSchematic(s.Code) && GateOf(s.Code) is null) problems.Add($"{s.Code} is a machine schematic with no gate");
        }
        foreach (var dup in Sales.GroupBy(s => CodePattern.Normalise(s.Code)).Where(g => g.Count() > 1))
            problems.Add($"{dup.Key} is in 'sales' {dup.Count()} times");
        return problems;
    }
}

/// <summary>A string or an array of strings, read as a list (a gate's <c>switch</c>).</summary>
public sealed class OneOrManyConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return [];
            case JsonTokenType.String: return [reader.GetString()!];
            case JsonTokenType.StartArray:
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String) throw new JsonException("a switch is a name");
                    list.Add(reader.GetString()!);
                }
                return list;
            default: throw new JsonException("switch is a name or a list of names");
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string s in value) writer.WriteStringValue(s);
        writer.WriteEndArray();
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
