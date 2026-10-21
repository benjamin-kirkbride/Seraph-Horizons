using System.Text.Json;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A rig's named points: every top-level object of its JSON with a <c>pos</c> array (<c>"chips": {"pos": [x, y, z]}</c>),
/// a point in the native frame, blocks from the controller's corner, as <see cref="MachinePoints"/> turns into the
/// world. Machines with a rig record of their own read the points they need by name; the generic ones (the ore
/// machines, the fired ones) look them up here, where the particles hang (<c>spray</c>, <c>sieve</c>,
/// <c>fireDoor</c>, <c>ash</c>, <c>mouth</c>, ...). Names match without regard to case (the acid parting
/// vessel's <c>firedoor</c> is the others' <c>fireDoor</c>).
/// </summary>
public static class RigPoints
{
    public static readonly IReadOnlyDictionary<string, Float3> None =
        new Dictionary<string, Float3>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every top-level <c>{name}: {"pos": [x, y, z]}</c> in a rig's root, by name; a <c>pos</c> that is not
    /// three numbers throws <see cref="FormatException"/>.</summary>
    public static IReadOnlyDictionary<string, Float3> Read(JsonElement root)
    {
        var points = new Dictionary<string, Float3>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
            if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("pos", out var pos))
                points[prop.Name] = pos.ValueKind == JsonValueKind.Array
                    ? RigJson.Float3Of(pos, prop.Name + ".pos")
                    : throw new FormatException($"{prop.Name}.pos must be an array of 3 numbers");
        return points;
    }

    /// <summary>The points of a rig's JSON text.</summary>
    public static IReadOnlyDictionary<string, Float3> Read(string rigJson)
    {
        using var doc = RigJson.Parse(rigJson);
        return Read(doc.RootElement);
    }
}
