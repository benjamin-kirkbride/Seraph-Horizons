using System.Text.Json;
using System.Text.Json.Nodes;

namespace SeraphHorizons.Mod.Core;

/// <summary>A tower structure's new figures: its <c>chance</c> (tries per chunk column, before the
/// game's chance multiplier) and its <c>minGroupDistance</c> in blocks (null leaves it).</summary>
public sealed record TowerRate(double Chance, int? MinGroupDistance);

/// <summary>
/// Rewrites Battle Towers' patch file (<c>battletowers:patches/survival-worldgen-structures.json</c>),
/// whose operations append its tower structures to <c>game:worldgen/structures.json</c>: each
/// operation whose <c>value.code</c> has a rate gets that rate's chance and minimum group distance;
/// everything else in the file is left as it is.
/// </summary>
public static class BattleTowerRates
{
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads the rates file (<c>config/battletowers-rates.json</c>): an object of structure
    /// codes, each <c>{ "chance": ..., "minGroupDistance": ... }</c>, comments allowed.</summary>
    public static IReadOnlyDictionary<string, TowerRate> ParseRates(string json)
    {
        var rates = new Dictionary<string, TowerRate>();
        if (JsonNode.Parse(json, documentOptions: Lenient) is not JsonObject root)
            throw new FormatException("the battle tower rates are not a JSON object");
        foreach (var (code, node) in root)
        {
            if (node is not JsonObject rate || rate["chance"] is not JsonValue chance)
                throw new FormatException($"the battle tower rate for {code} has no chance");
            double value = chance.GetValue<double>();
            if (value < 0)
                throw new FormatException($"the battle tower rate for {code} has a negative chance");
            int? distance = rate["minGroupDistance"] is JsonValue d ? d.GetValue<int>() : null;
            rates[code] = new TowerRate(value, distance);
        }
        return rates;
    }

    /// <summary>Applies <paramref name="rates"/> to the patch file's text. Returns the new text and
    /// the codes it changed; a code with a rate that the file does not add is left out of
    /// <c>Applied</c>, so the caller can say Battle Towers changed.</summary>
    public static (string Text, IReadOnlyList<string> Applied) Apply(string patchJson, IReadOnlyDictionary<string, TowerRate> rates)
    {
        if (JsonNode.Parse(patchJson, documentOptions: Lenient) is not JsonArray ops)
            throw new FormatException("Battle Towers' structures patch is not a JSON array");
        var applied = new List<string>();
        foreach (var op in ops)
        {
            if (op?["value"] is not JsonObject value || value["code"]?.GetValue<string>() is not { } code
                || !rates.TryGetValue(code, out var rate))
                continue;
            value["chance"] = rate.Chance;
            if (rate.MinGroupDistance is { } distance)
                value["minGroupDistance"] = distance;
            applied.Add(code);
        }
        return (ops.ToJsonString(), applied);
    }
}
