using System.Text.Json;
using System.Text.Json.Nodes;

namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// What <c>patches/unifiedpipes-solderedjoints.json</c> assumes of Pipes and Power Expanded's pipe
/// blocktypes (0.7.1), checked before the patch loader runs. The patch draws ppex's copper and lead
/// pipes (the states <c>unifiedpipes-ppex.json</c> adds) on this mod's soldered models: each of its
/// operations replaces one pipe blocktype's <c>shapebytype</c> with ppex's own table, each entry
/// preceded by one per soldered metal (<see cref="PipeRules.AddedPipeMaterials"/>) keyed for that
/// metal, with that entry's rotations and this mod's shape of the pipe
/// (<c>shapes/block/pipes/soldered-{type}.json</c>, written by <c>Pipes/tools/make_pipe_shapes.py</c>).
/// So the patch's table, less those entries, must be ppex's table as it is now, key for key in order;
/// otherwise the patch would put back a table ppex no longer has.
/// </summary>
public static class SolderedJoints
{
    public const string ShapePrefix = "seraphhorizons:block/pipes/soldered-";
    public const string ShapeKey = "shapebytype";

    /// <summary>The patch's operations against ppex's blocktypes, read by <paramref name="ppexFile"/>
    /// (a path under ppex's assets, its text or null when missing). Returns the problem, or null.</summary>
    public static string? Check(string patchJson, Func<string, string?> ppexFile)
    {
        JsonArray ops;
        try
        {
            ops = PipeAssetGuard.Parse(patchJson) as JsonArray ?? throw new JsonException("not a list");
        }
        catch (JsonException e)
        {
            return $"the soldered joints' patch does not parse ({e.Message})";
        }
        var files = ops.Select(op => (string?)op?["file"]).ToList();
        var wanted = PipeAssetGuard.PipeBlocktypes.Select(f => "ppex:" + f).ToList();
        if (!files.SequenceEqual(wanted))
            return "the soldered joints' patch (not one operation per pipe blocktype)";
        foreach (var op in ops)
        {
            var file = ((string)op!["file"]!)["ppex:".Length..];
            if ((string?)op["op"] != "replace" || (string?)op["path"] != "/" + ShapeKey || op["value"] is not JsonObject table)
                return $"the soldered joints' patch ({file} is not a replaced {ShapeKey})";
            var text = ppexFile(file);
            if (text == null)
                return $"ppex:{file} (missing)";
            JsonNode? ppex;
            try
            {
                ppex = PipeAssetGuard.Parse(text);
            }
            catch (JsonException e)
            {
                return $"ppex:{file} does not parse ({e.Message})";
            }
            if (ppex?[ShapeKey] is not JsonObject theirs)
                return $"ppex:{file}'s shapes (no {ShapeKey})";
            if (CheckTable(Kind(file), theirs, table) is { } problem)
                return $"ppex:{file}'s shapes ({problem})";
        }
        return null;
    }

    /// <summary>"blocktypes/pipes/bend.json" is the bend.</summary>
    public static string Kind(string file) => Path.GetFileNameWithoutExtension(file);

    /// <summary>The table the patch puts in place of ppex's <paramref name="theirs"/> for the pipe
    /// <paramref name="kind"/>: before each of ppex's entries (its key ending in the material
    /// wildcard), one per soldered metal with the same rotations and this mod's shape.</summary>
    public static JsonObject? Soldered(string kind, JsonObject theirs)
    {
        var table = new JsonObject();
        foreach (var (key, value) in theirs)
        {
            if (!key.EndsWith("-*", StringComparison.Ordinal) || value is not JsonObject entry)
                return null;
            foreach (var metal in PipeRules.AddedPipeMaterials)
            {
                var ours = (JsonObject)entry.DeepClone();
                ours["base"] = ShapePrefix + kind;
                table[key[..^1] + metal] = ours;
            }
            table[key] = entry.DeepClone();
        }
        return table;
    }

    /// <summary>The patch's <paramref name="table"/> is <see cref="Soldered"/> of ppex's
    /// <paramref name="theirs"/>, key for key in order. Returns the problem, or null.</summary>
    public static string? CheckTable(string kind, JsonObject theirs, JsonObject table)
    {
        if (Soldered(kind, theirs) is not { } expected)
            return "an entry not keyed by its material wildcard";
        var keys = table.Select(p => p.Key).ToList();
        if (!keys.SequenceEqual(expected.Select(p => p.Key)))
            return "its orientations are not the ones the patch was written for";
        foreach (var (key, value) in expected)
            if (!JsonNode.DeepEquals(value, table[key]))
                return $"{key} is not as the patch has it";
        return null;
    }
}
