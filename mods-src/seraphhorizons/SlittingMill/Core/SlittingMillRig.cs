using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.SlittingMill.Core;

/// <summary>
/// The slitting mill's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/slittingmill-rig.json (written by SlittingMill/tools/make_shape.py).
/// Native frame, controller cell at (0,0,0) (the feed table's west half, nearest the player), blocks. The keys
/// are the model's contract with gameplay (SlittingMill/README.md, "Rig schema"); unknown keys are ignored.
/// Its power cell and face are the shared machine base's <c>power</c> anchor (<see cref="IMachineRig"/>).
/// Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class SlittingMillRig : IMachineRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    public Int3 PowerCell { get; }
    public Side PowerFace { get; }
    /// <summary>The face a chest or hopper feeds plates through: the feed table's end.</summary>
    public Side InfeedSide { get; }
    /// <summary>The face the nails and strips leave by: the nail box's.</summary>
    public Side OutputSide { get; }
    /// <summary>Where the nails and strips come off (the nail box's south face, at its top).</summary>
    public Float3 Output { get; }
    /// <summary>The plate's middle on the table: loading sounds.</summary>
    public Float3 Plate { get; }
    /// <summary>The nip between the arbors: the slitting's sound and dust.</summary>
    public Float3 Nip { get; }
    /// <summary>The cut, at the bed knife's back face: the cut's sound.</summary>
    public Float3 Knife { get; }
    public WorkQuantity Work { get; }
    /// <summary>Axle turns a knife stroke, as the model is drawn.</summary>
    public float TurnsPerStroke { get; }
    public RigParts MovingParts { get; }

    RigParts IMachineRig.Parts => MovingParts;

    /// <summary>Its one anchor for the shared machine base: <c>power</c>, the axle on <see cref="PowerFace"/> of
    /// <see cref="PowerCell"/>.</summary>
    public IReadOnlyDictionary<string, MachineAnchor> Anchors =>
        _anchors ??= new Dictionary<string, MachineAnchor> { [MachineRigs.Power] = new(MachineRigs.Power, PowerCell, PowerFace.Face()) };

    private IReadOnlyDictionary<string, MachineAnchor>? _anchors;

    public MachineAnchor? Anchor(string name) => Anchors.TryGetValue(name, out var a) ? a : null;

    public SlittingMillRig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Side infeedSide, Side outputSide,
                           Float3 output, Float3 plate, Float3 nip, Float3 knife, WorkQuantity work, float turnsPerStroke,
                           RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the slitting mill has no hollow cells");
        if (!positions.Contains(powerCell))
            throw new FormatException($"powerCell {powerCell} is not one of the cells");
        if (powerCell == Int3.Zero)
            throw new FormatException("powerCell is the controller's cell; it must be a ghost cell");
        if (positions.Contains(powerCell + powerFace.Normal()))
            throw new FormatException($"powerCell {powerCell}'s face {powerFace.Code()} is inside the footprint");
        if (infeedSide == outputSide)
            throw new FormatException("infeedSide and outputSide are the same face");
        if (!(turnsPerStroke > 0) || !float.IsFinite(turnsPerStroke))
            throw new FormatException("slit.turnsPerStroke must be above 0");
        if (Math.Abs(work.End(1) - Slitting.HandOut) > 1e-4)
            throw new FormatException($"work.end must be {{thin: {Slitting.HandOut}}}, the knife strokes a plate takes");
        Cells = cells;
        PowerCell = powerCell;
        PowerFace = powerFace;
        InfeedSide = infeedSide;
        OutputSide = outputSide;
        Output = output;
        Plate = plate;
        Nip = nip;
        Knife = knife;
        Work = work;
        TurnsPerStroke = turnsPerStroke;
        MovingParts = movingParts ?? new RigParts([], work);
    }

    /// <summary>The cells just outside a face of the footprint, every level: where a container
    /// against that face stands.</summary>
    public IEnumerable<Int3> Neighbours(Side side)
    {
        var step = side.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        return Cells.Select(c => c.Pos + step).Where(p => !occupied.Contains(p)).Distinct();
    }

    /// <summary>Where a chest or hopper feeds plates from (at the feed table's end).</summary>
    public IEnumerable<Int3> InfeedNeighbours() => Neighbours(InfeedSide);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell: a container
    /// there takes the nails and strips.</summary>
    public Int3 OutputNeighbour()
    {
        // the anchor lies on the face itself: step back into the footprint first
        var n = OutputSide.Normal();
        var cell = new Int3((int)MathF.Floor(Output.X - n.X * 1e-3f), (int)MathF.Floor(Output.Y - n.Y * 1e-3f), (int)MathF.Floor(Output.Z - n.Z * 1e-3f));
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        while (occupied.Contains(cell))
            cell += n;
        return cell;
    }

    /// <summary>Where the nails and strips are dropped when no container takes them: <see cref="Output"/>
    /// moved out through the output face to <paramref name="beyond"/> blocks past it.</summary>
    public Float3 OutputDrop(float beyond = 0.15f)
    {
        var step = OutputSide.Normal();
        var face = OutputNeighbour();
        float Plane(int along, int n) => n > 0 ? along : along + 1;
        return step switch
        {
            { X: not 0 } => Output with { X = Plane(face.X, step.X) + step.X * beyond },
            _ => Output with { Z = Plane(face.Z, step.Z) + step.Z * beyond },
        };
    }

    /// <summary>Parses slittingmill-rig.json.</summary>
    public static SlittingMillRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var slit = Required(root, "slit", JsonValueKind.Object);
        if (Str(slit, "plate") is var plate && plate != Slitting.PlatePrefix + "{metal}")
            throw new FormatException($"slit.plate is {plate ?? "missing"}, the mill's is {Slitting.PlatePrefix}{{metal}}");
        if (Str(slit, "output") is var output && output != Slitting.OutputPrefix + "{metal}")
            throw new FormatException($"slit.output is {output ?? "missing"}, the mill's is {Slitting.OutputPrefix}{{metal}}");
        if (Math.Abs(Required(slit, "handOut", JsonValueKind.Number).GetDouble() - Slitting.HandOut) > 1e-6)
            throw new FormatException($"slit.handOut must be {Slitting.HandOut}, the gameplay's");
        var cuts = Required(slit, "cuts", JsonValueKind.Array).EnumerateArray().Select(c => c.GetDouble()).ToList();
        if (!cuts.SequenceEqual(Slitting.Cuts))
            throw new FormatException($"slit.cuts must be [{string.Join(", ", Slitting.Cuts)}], the gameplay's");
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, SlittingMillRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new SlittingMillRig(
            Cells(root),
            Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
            SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
            SideOf(Required(root, "infeedSide", JsonValueKind.String), "infeedSide"),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("plate"),
            Anchor("nip"),
            Anchor("knife"),
            work,
            Required(slit, "turnsPerStroke", JsonValueKind.Number).GetSingle(),
            parts);
    }
}
