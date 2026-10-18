using System.Text.Json;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// An ore machine's rig as the shared game code reads it (#711; <c>config/{machine}-rig.json</c>,
/// written by the machine's generator): its cells (native, south-facing frame, the controller at
/// [0,0,0]), its anchors (every <c>{name}Cell</c>/<c>{name}Face</c> pair, <see cref="MachineAnchors"/>),
/// its build stages' <c>requires</c> values and its moving parts. A part's <c>requires</c> is a build
/// stage's (<c>buildStages[].adds</c>) or one of the machine's own (<paramref name="extraRequires"/> of
/// <see cref="Parse"/>: content drawn by the machine's state, the arrastra's charge). Unknown keys are the
/// machine's own and ignored here. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class OreMachineRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    public IReadOnlyDictionary<string, MachineAnchor> Anchors { get; }
    /// <summary>Every <c>requires</c> the build stages add, in order.</summary>
    public IReadOnlyList<string> StageRequires { get; }
    public RigParts Parts { get; }

    public OreMachineRig(IReadOnlyList<RigCell> cells, IReadOnlyDictionary<string, MachineAnchor> anchors,
        IReadOnlyList<string> stageRequires, RigParts parts)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        foreach (var a in anchors.Values)
        {
            if (!positions.Contains(a.Cell))
                throw new FormatException($"{a.Name}Cell {a.Cell} is not one of the cells");
            if (positions.Contains(a.Cell + a.Face.Normal()))
                throw new FormatException($"{a.Name}Cell {a.Cell}'s face {a.Face.Code()} is inside the footprint");
        }
        Cells = cells;
        Anchors = anchors;
        StageRequires = stageRequires;
        Parts = parts;
    }

    public const string Power = "power", Infeed = "infeed", Water = "water";

    /// <summary>An anchor by name, or null.</summary>
    public MachineAnchor? Anchor(string name) => Anchors.TryGetValue(name, out var a) ? a : null;

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> OtherCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The anchors whose cell is <paramref name="cell"/>.</summary>
    public IEnumerable<MachineAnchor> AnchorsAt(Int3 cell) => Anchors.Values.Where(a => a.Cell == cell);

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>: the
    /// machine's native north (its front, where the ore machines' controllers are) faces the player, so
    /// the facing is the way they look (south is the identity).</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The native axis of the shaft the power face takes: along x for an east or west face, z
    /// for north or south; null for up or down (a vertical shaft).</summary>
    public Axis? ShaftAxis => Anchor(Power)?.Face switch
    {
        CellFace.East or CellFace.West => Axis.X,
        CellFace.North or CellFace.South => Axis.Z,
        _ => null,
    };

    /// <summary>Parses a rig; <paramref name="extraRequires"/> are the machine's own part
    /// <c>requires</c> besides its stages'.</summary>
    public static OreMachineRig Parse(string json, IEnumerable<string>? extraRequires = null)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var stageRequires = new List<string>();
        if (root.TryGetProperty("buildStages", out var stages))
        {
            if (stages.ValueKind != JsonValueKind.Array)
                throw new FormatException("\"buildStages\" must be an array");
            foreach (var s in stages.EnumerateArray())
                if (s.TryGetProperty("adds", out var adds) && adds.ValueKind == JsonValueKind.Array)
                    stageRequires.AddRange(adds.EnumerateArray().Select(a => a.GetString() ?? throw new FormatException("a buildStages add is not a string")));
        }
        // the furnaces' and the arrastra's form: build.stages[], each its requires (or its adds)
        if (root.TryGetProperty("build", out var buildEl) && buildEl.ValueKind == JsonValueKind.Object
            && buildEl.TryGetProperty("stages", out var buildStages) && buildStages.ValueKind == JsonValueKind.Array)
            foreach (var s in buildStages.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object)
                    continue;
                if (s.TryGetProperty("requires", out var r) && r.ValueKind == JsonValueKind.String)
                    stageRequires.Add(r.GetString()!);
                if (s.TryGetProperty("adds", out var adds) && adds.ValueKind == JsonValueKind.Array)
                    stageRequires.AddRange(adds.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!));
                // a stage of several fits (the reverberatory roaster's, #737): each fit's requires and its states
                if (s.TryGetProperty("fits", out var fits) && fits.ValueKind == JsonValueKind.Array)
                    foreach (var f in fits.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
                    {
                        if (f.TryGetProperty("requires", out var fr) && fr.ValueKind == JsonValueKind.String)
                            stageRequires.Add(fr.GetString()!);
                        if (f.TryGetProperty("also", out var also) && also.ValueKind == JsonValueKind.Array)
                            stageRequires.AddRange(also.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString()!));
                    }
            }
        var known = new HashSet<string>(stageRequires, StringComparer.Ordinal);
        foreach (var r in extraRequires ?? [])
            known.Add(r);
        var progress = RigProgress.Of(root);
        var parts = root.TryGetProperty("parts", out var partsJson)
            ? partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, known, progress)
                : throw new FormatException("\"parts\" must be an array")
            : new RigParts([], progress);
        return new OreMachineRig(Cells(root), MachineAnchors.Read(root), stageRequires, parts);
    }
}
