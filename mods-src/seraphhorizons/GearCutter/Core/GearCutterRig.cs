using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.GearCutter.Core;

/// <summary>
/// The gear cutter's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/gearcutter-rig.json (written by GearCutter/tools/make_shape.py).
/// Native, south-facing frame, controller cell at (0,0,0), blocks. The keys are the README's ("Rig
/// schema"); unknown keys are ignored. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class GearCutterRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    public Int3 PowerCell { get; }
    public Side PowerFace { get; }
    /// <summary>The face a finished gear leaves by.</summary>
    public Side OutputSide { get; }
    /// <summary>Where a finished gear drops (inside the machine, by the output face).</summary>
    public Float3 Output { get; }
    /// <summary>The cutter: chips and sparks.</summary>
    public Float3 Chips { get; }
    /// <summary>The injection valve's nozzle: the oil spray.</summary>
    public Float3 Drip { get; }
    public WorkQuantity Work { get; }
    /// <summary>Axle turns per tooth as the model is drawn.</summary>
    public float TurnsPerTooth { get; }
    public RigParts MovingParts { get; }

    public GearCutterRig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Side outputSide,
                         Float3 output, Float3 chips, Float3 drip, WorkQuantity work, float turnsPerTooth, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the gear cutter has no hollow cells");
        if (!positions.Contains(powerCell))
            throw new FormatException($"powerCell {powerCell} is not one of the cells");
        if (powerCell == Int3.Zero)
            throw new FormatException("powerCell is the controller's cell; it must be a ghost cell");
        if (positions.Contains(powerCell + powerFace.Normal()))
            throw new FormatException($"powerCell {powerCell}'s face {powerFace.Code()} is inside the footprint");
        if (!(turnsPerTooth > 0) || !float.IsFinite(turnsPerTooth))
            throw new FormatException("cut.turnsPerTooth must be above 0");
        if (work.Ends[1] != GearCut.Teeth(1) || work.Ends[2] != GearCut.Teeth(2))
            throw new FormatException($"work.end must be {{thin: {GearCut.Teeth(1)}, thick: {GearCut.Teeth(2)}}}, the teeth of a small and a large gear");
        Cells = cells;
        PowerCell = powerCell;
        PowerFace = powerFace;
        OutputSide = outputSide;
        Output = output;
        Chips = chips;
        Drip = drip;
        Work = work;
        TurnsPerTooth = turnsPerTooth;
        MovingParts = movingParts ?? new RigParts([], work);
    }

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell, where the finished gear
    /// is dropped.</summary>
    public Int3 OutputNeighbour()
    {
        var cell = new Int3((int)MathF.Floor(Output.X), (int)MathF.Floor(Output.Y), (int)MathF.Floor(Output.Z));
        var step = OutputSide.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        while (occupied.Contains(cell))
            cell += step;
        return cell;
    }

    /// <summary>Where a finished gear is dropped: <see cref="Output"/>
    /// moved out through the output face to <paramref name="beyond"/> blocks past it.</summary>
    public Float3 OutputDrop(float beyond = 0.15f)
    {
        var step = OutputSide.Normal();
        var face = OutputNeighbour();
        // the face plane: the neighbour cell's near side
        float Plane(int along, int n) => n > 0 ? along : along + 1;
        return step switch
        {
            { X: not 0 } => Output with { X = Plane(face.X, step.X) + step.X * beyond },
            _ => Output with { Z = Plane(face.Z, step.Z) + step.Z * beyond },
        };
    }

    /// <summary>Parses gearcutter-rig.json.</summary>
    public static GearCutterRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var cut = Required(root, "cut", JsonValueKind.Object);
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, GearCutterRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new GearCutterRig(
            Cells(root),
            Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
            SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("chips"),
            Anchor("drip"),
            work,
            Required(cut, "turnsPerTooth", JsonValueKind.Number).GetSingle(),
            parts);
    }
}
