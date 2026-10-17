using System.Text.Json;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>A cell face: the four sides or up and down.</summary>
public enum CellFace { North, East, South, West, Up, Down }

public static class CellFaces
{
    public static string Code(this CellFace face) => face.ToString().ToLowerInvariant();

    /// <summary>Parses a lower-case face code (<c>north</c> .. <c>down</c>).</summary>
    public static bool TryParse(string? code, out CellFace face)
    {
        foreach (var f in Enum.GetValues<CellFace>())
            if (f.Code() == code)
            {
                face = f;
                return true;
            }
        face = default;
        return false;
    }

    /// <summary>The unit step out of the cell through this face.</summary>
    public static Int3 Normal(this CellFace face) => face switch
    {
        CellFace.Up => new Int3(0, 1, 0),
        CellFace.Down => new Int3(0, -1, 0),
        _ => ((Side)(int)face).Normal(),
    };

    /// <summary>A native-frame face on a machine facing <paramref name="facing"/>: the sides turn, up
    /// and down do not.</summary>
    public static CellFace ToWorld(CellFace native, Side facing) =>
        native is CellFace.Up or CellFace.Down ? native : (CellFace)(int)Footprint.ToWorld((Side)(int)native, facing);
}

/// <summary>
/// A chute or service anchor of a machine's rig (#711): a cell in the native frame and the face of it
/// through which the chute, pipe or axle meets the machine. Read from the rig's <c>{name}Cell</c> and
/// <c>{name}Face</c> pairs (<c>infeedCell</c>/<c>infeedFace</c>, <c>outputCell</c>/<c>outputFace</c>,
/// <c>concentrateCell</c>, <c>tailingsCell</c>, <c>middlingsCell</c>, <c>waterCell</c>, <c>powerCell</c>, ...).
/// </summary>
public readonly record struct MachineAnchor(string Name, Int3 Cell, CellFace Face)
{
    /// <summary>The anchor's cell as a world offset from the controller, and its face in the world, for a
    /// machine facing <paramref name="facing"/>.</summary>
    public (Int3 Cell, CellFace Face) ToWorld(Side facing) => (Footprint.ToWorld(Cell, facing), CellFaces.ToWorld(Face, facing));

    /// <summary>The cell beyond the face (where the chute, container or pipe stands), as a world offset
    /// from the controller.</summary>
    public Int3 Beyond(Side facing)
    {
        var (cell, face) = ToWorld(facing);
        return cell + face.Normal();
    }
}

/// <summary>How a machine takes its feed and gives its products (#711).</summary>
public enum FeedMode
{
    /// <summary>Continuous: one infeed by vanilla chute (or hand feeding into its mouth), outputs by
    /// chute at the model's anchors; an output not chuted on is held (or, for tailings, discarded).</summary>
    Continuous,
    /// <summary>Batch: loaded and emptied by hand, no chute anchor (the arrastra, the roasters and furnaces).</summary>
    Batch,
}

public static class MachineAnchors
{
    /// <summary>Every <c>{name}Cell</c>/<c>{name}Face</c> pair in a rig's root, by name (the
    /// <c>Cell</c> suffix taken off). A cell without a face, or a face without a cell, is no anchor
    /// (furnaces' <c>outletCell</c> has its own rules); a malformed pair throws <see cref="FormatException"/>.</summary>
    public static IReadOnlyDictionary<string, MachineAnchor> Read(JsonElement root)
    {
        var anchors = new Dictionary<string, MachineAnchor>(StringComparer.Ordinal);
        foreach (var prop in root.EnumerateObject())
        {
            if (!prop.Name.EndsWith("Cell", StringComparison.Ordinal) || prop.Name.Length == 4)
                continue;
            string name = prop.Name[..^4];
            if (!root.TryGetProperty(name + "Face", out var faceEl))
                continue;
            var cell = prop.Value;
            if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() != 3
                || cell.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out _)))
                throw new FormatException($"{prop.Name} is not three integers");
            var c = cell.EnumerateArray().Select(e => e.GetInt32()).ToArray();
            if (faceEl.ValueKind != JsonValueKind.String || !CellFaces.TryParse(faceEl.GetString(), out var face))
                throw new FormatException($"{name}Face is not north, east, south, west, up or down");
            anchors[name] = new MachineAnchor(name, new Int3(c[0], c[1], c[2]), face);
        }
        return anchors;
    }

    /// <summary>The anchors of a rig's JSON text.</summary>
    public static IReadOnlyDictionary<string, MachineAnchor> Read(string rigJson)
    {
        using var doc = RigJson.Parse(rigJson);
        return Read(doc.RootElement);
    }
}
