using System.Text.Json;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A machine's build file (<c>config/builds/{machine}.json</c>, #711): the recipe of each build stage
/// after the frame, from the model README's stage table, and its wearing parts.
/// <code>
/// {
///   "machine": "stampbattery",                  // the id its settings and tailings use
///   "block": "seraphhorizons:stampbattery-north", // the placed frame, for the recipe browser (optional)
///   "feed": "continuous",                       // or "batch" (#711)
///   "stages": [ { "id": "timber", "adds": ["timber"],
///                 "materials": [ { "codes": ["game:supportbeam-*"], "quantity": 41 } ] }, ... ],
///   // a material with "oneType": true takes any of its codes, but all of one (one brick type)
///   "wearing": [ { "id": "shoes", "codes": ["game:poundercap-steel"], "count": 5, "uses": 400, "stage": "stamps" } ]
/// }
/// </code>
/// A material's <c>picks</c> (<c>{ "seraphhorizons:pipesection-copper": "pipecopper" }</c>) draw a
/// <c>requires</c> by the item put in; its <c>draws</c> are <c>requires</c> drawn once it is all in, before
/// the rest of its stage. A wearing part with a <c>stage</c> has its first set fitted as one more
/// material of that stage (the stamp battery's shoes with its stamps), or as its only one when the stage
/// lists none (<c>"materials": []</c>, the vanner's belt); without one, it goes in by
/// right-click once the machine is built. The stages and their <c>adds</c> must be the rig's
/// <c>buildStages</c>, in order (<see cref="CheckAgainstRig"/>).
/// </summary>
public sealed record MachineBuild(string Machine, string? Block, FeedMode Feed, IReadOnlyList<MachineStage> Stages,
    IReadOnlyList<WearingPart> Wearing)
{
    /// <summary>A new, empty build state on these stages.</summary>
    public StagedBuild NewBuild() => new(Stages);

    public WearingParts NewWearing() => new(Wearing);
}

public static class MachineBuildJson
{
    /// <summary>Parses a build file; throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static MachineBuild Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        return Parse(doc.RootElement);
    }

    public static MachineBuild Parse(JsonElement root)
    {
        string machine = Str(root, "machine") ?? throw new FormatException("machine is missing");
        string? block = Str(root, "block");
        var feed = Str(root, "feed") switch
        {
            null or "continuous" => FeedMode.Continuous,
            "batch" => FeedMode.Batch,
            var f => throw new FormatException($"feed {f} is not continuous or batch"),
        };
        if (!root.TryGetProperty("stages", out var stagesEl) || stagesEl.ValueKind != JsonValueKind.Array || stagesEl.GetArrayLength() == 0)
            throw new FormatException("stages is missing or empty");
        var stages = new List<MachineStage>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in stagesEl.EnumerateArray())
        {
            string id = Str(s, "id") ?? throw new FormatException($"stage {stages.Count + 1} has no id");
            if (!ids.Add(id))
                throw new FormatException($"stage {id} is listed twice");
            var adds = Strings(s, "adds", $"stage {id}'s adds");
            // a stage may list no materials of its own when a wearing part is fitted with it (the vanner's
            // belt): checked once the wearing parts are read
            if (!s.TryGetProperty("materials", out var mats) || mats.ValueKind != JsonValueKind.Array)
                throw new FormatException($"stage {id} has no materials");
            var materials = new List<BuildMaterial>();
            foreach (var m in mats.EnumerateArray())
            {
                var codes = Strings(m, "codes", $"stage {id}'s material {materials.Count + 1}'s codes");
                if (codes.Count == 0)
                    throw new FormatException($"stage {id}'s material {materials.Count + 1} has no codes");
                int quantity = m.TryGetProperty("quantity", out var q) && q.TryGetInt32(out int n) ? n : 0;
                if (quantity <= 0)
                    throw new FormatException($"stage {id}'s material {codes[0]} has no positive quantity");
                Dictionary<string, string>? picks = null;
                if (m.TryGetProperty("picks", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    picks = new(StringComparer.Ordinal);
                    foreach (var kv in p.EnumerateObject())
                    {
                        if (kv.Value.ValueKind != JsonValueKind.String || !adds.Contains(kv.Value.GetString()!))
                            throw new FormatException($"stage {id}'s pick {kv.Name} is not one of its adds");
                        picks[CodePattern.Normalise(kv.Name)!] = kv.Value.GetString()!;
                    }
                }
                List<string>? draws = m.TryGetProperty("draws", out _) ? Strings(m, "draws", $"stage {id}'s material {codes[0]}'s draws") : null;
                if (draws?.FirstOrDefault(d => !adds.Contains(d)) is { } stray)
                    throw new FormatException($"stage {id}'s material {codes[0]} draws {stray}, which is not one of its adds");
                bool oneType = m.TryGetProperty("oneType", out var one) && one.ValueKind == JsonValueKind.True;
                materials.Add(new BuildMaterial(codes.Select(c => CodePattern.Normalise(c)!).ToList(), quantity, picks, draws, OneType: oneType));
            }
            stages.Add(new MachineStage(id, adds, materials, Str(s, "name")));
        }
        var wearing = new List<WearingPart>();
        if (root.TryGetProperty("wearing", out var wearEl) && wearEl.ValueKind == JsonValueKind.Array)
            foreach (var w in wearEl.EnumerateArray())
            {
                string id = Str(w, "id") ?? throw new FormatException($"wearing part {wearing.Count + 1} has no id");
                var codes = Strings(w, "codes", $"wearing part {id}'s codes");
                int count = w.TryGetProperty("count", out var c) && c.TryGetInt32(out int cn) ? cn : 0;
                double uses = w.TryGetProperty("uses", out var u) && u.TryGetDouble(out double un) ? un : 0;
                if (codes.Count == 0 || count <= 0 || !(uses > 0))
                    throw new FormatException($"wearing part {id} needs codes, a positive count and positive uses");
                if (wearing.Any(x => x.Id == id))
                    throw new FormatException($"wearing part {id} is listed twice");
                var part = new WearingPart(id, codes.Select(x => CodePattern.Normalise(x)!).ToList(), count, uses, Str(w, "requires"));
                wearing.Add(part);
                // fitted with a stage: its first set is one more material of that stage
                if (Str(w, "stage") is { } stageId)
                {
                    int s = stages.FindIndex(x => x.Id == stageId);
                    if (s < 0)
                        throw new FormatException($"wearing part {id} is fitted with stage {stageId}, which is not a stage");
                    stages[s] = stages[s] with
                    {
                        Materials = [.. stages[s].Materials, new BuildMaterial(part.Codes, count, Wearing: id)],
                    };
                }
            }
        if (stages.FirstOrDefault(x => x.Materials.Count == 0) is { } bare)
            throw new FormatException($"stage {bare.Id} has no materials");
        return new MachineBuild(machine, block, feed, stages, wearing);
    }

    /// <summary>
    /// What disagrees between a build file and its machine's rig (<c>{machine}-rig.json</c>): the rig's
    /// <c>buildStages</c> must list the same stages in the same order (by <c>id</c> where the rig gives
    /// one), each adding the same <c>requires</c>, and every part's <c>requires</c> that a stage adds
    /// must be one the rig's parts use. A rig with no <c>buildStages</c> (a hand station's) is held to
    /// the parts' rule only. Empty when they agree.
    /// </summary>
    public static IReadOnlyList<string> CheckAgainstRig(MachineBuild build, JsonElement rig)
    {
        var problems = new List<string>();
        // a rig without buildStages (a hand station's, a furnace's build) is held to its parts only
        var rigStages = rig.TryGetProperty("buildStages", out var rs) && rs.ValueKind == JsonValueKind.Array
            ? rs.EnumerateArray().ToList()
            : build.Stages.Select(_ => default(JsonElement)).ToList();
        bool listed = rigStages.All(s => s.ValueKind == JsonValueKind.Object);
        if (rigStages.Count != build.Stages.Count)
            problems.Add($"the rig has {rigStages.Count} stages, the build {build.Stages.Count}");
        for (int i = 0; listed && i < Math.Min(rigStages.Count, build.Stages.Count); i++)
        {
            var stage = build.Stages[i];
            if (Str(rigStages[i], "id") is { } rigId && rigId != stage.Id)
                problems.Add($"stage {i + 1} is {rigId} in the rig, {stage.Id} in the build");
            var rigAdds = Strings(rigStages[i], "adds", "the rig's adds");
            if (!rigAdds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(stage.Adds.OrderBy(x => x, StringComparer.Ordinal)))
                problems.Add($"stage {stage.Id} adds {string.Join(", ", rigAdds)} in the rig, {string.Join(", ", stage.Adds)} in the build");
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (rig.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            foreach (var p in parts.EnumerateArray())
                if (p.TryGetProperty("requires", out var r) && r.ValueKind == JsonValueKind.String)
                    used.Add(r.GetString()!);
        foreach (var stage in build.Stages)
            foreach (var add in stage.Adds.Where(a => !used.Contains(a)))
                problems.Add($"stage {stage.Id} adds {add}, which no part of the rig requires");
        return problems;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement e, string name, string what)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{what} is missing");
        return v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : throw new FormatException($"{what} holds a non-string")).ToList();
    }
}
