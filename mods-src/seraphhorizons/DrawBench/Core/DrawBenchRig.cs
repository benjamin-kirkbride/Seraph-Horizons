using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>
/// The draw bench's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/drawbench-rig.json (written by DrawBench/tools/make_shape.py).
/// Native, south-facing frame, controller cell at (0,0,0) (the die end), blocks. The keys are the
/// model's contract with gameplay (DrawBench/README.md, "Rig schema"); unknown keys are ignored.
/// Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class DrawBenchRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    public Int3 PowerCell { get; }
    public Side PowerFace { get; }
    /// <summary>The face a drawn section leaves by.</summary>
    public Side OutputSide { get; }
    /// <summary>Where a drawn section comes off (on the output face, by the trough's north end).</summary>
    public Float3 Output { get; }
    /// <summary>The die's mouth, on the draw line: metal dust and lubricant smoke.</summary>
    public Float3 Die { get; }
    /// <summary>The oiler's spout over the hollow: the oil drips.</summary>
    public Float3 Drip { get; }
    public WorkQuantity Work { get; }
    /// <summary>Axle turns per section of each class, [none, lead, copper], as the model is drawn.</summary>
    public IReadOnlyList<float> TurnsPerSection { get; }
    public RigParts MovingParts { get; }

    public DrawBenchRig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Side outputSide,
                        Float3 output, Float3 die, Float3 drip, WorkQuantity work, float turnsPerSectionLead, float turnsPerSectionCopper,
                        RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the draw bench has no hollow cells");
        if (!positions.Contains(powerCell))
            throw new FormatException($"powerCell {powerCell} is not one of the cells");
        if (powerCell == Int3.Zero)
            throw new FormatException("powerCell is the controller's cell; it must be a ghost cell");
        if (positions.Contains(powerCell + powerFace.Normal()))
            throw new FormatException($"powerCell {powerCell}'s face {powerFace.Code()} is inside the footprint");
        if (!(turnsPerSectionLead > 0) || !float.IsFinite(turnsPerSectionLead) || !(turnsPerSectionCopper > 0) || !float.IsFinite(turnsPerSectionCopper))
            throw new FormatException("draw.turnsPerSection must be above 0 for both metals");
        if (work.Ends[1] != Drawing.SectionsPerHollow || work.Ends[2] != Drawing.SectionsPerHollow)
            throw new FormatException($"work.end must be {{thin: {Drawing.SectionsPerHollow}, thick: {Drawing.SectionsPerHollow}}}, the sections a hollow draws");
        Cells = cells;
        PowerCell = powerCell;
        PowerFace = powerFace;
        OutputSide = outputSide;
        Output = output;
        Die = die;
        Drip = drip;
        Work = work;
        TurnsPerSection = [0, turnsPerSectionLead, turnsPerSectionCopper];
        MovingParts = movingParts ?? new RigParts([], work);
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>,
    /// so the bench extends away from them with the die end (the controller) nearest: the bench runs
    /// along native south, which a facing turns to itself.</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell, where the drawn sections
    /// are dropped.</summary>
    public Int3 OutputNeighbour()
    {
        var cell = new Int3((int)MathF.Floor(Output.X), (int)MathF.Floor(Output.Y), (int)MathF.Floor(Output.Z));
        var step = OutputSide.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        while (occupied.Contains(cell))
            cell += step;
        return cell;
    }

    /// <summary>Where a drawn section is dropped: <see cref="Output"/>
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

    /// <summary>Parses drawbench-rig.json.</summary>
    public static DrawBenchRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var draw = Required(root, "draw", JsonValueKind.Object);
        var turns = Required(draw, "turnsPerSection", JsonValueKind.Object);
        var per = Required(draw, "sectionsPerHollow", JsonValueKind.Number);
        if (per.GetDouble() != Drawing.SectionsPerHollow)
            throw new FormatException($"draw.sectionsPerHollow must be {Drawing.SectionsPerHollow}");
        var handOut = Required(draw, "handOut", JsonValueKind.Number);
        if (Math.Abs(handOut.GetDouble() - Drawing.HandOut) > 1e-6)
            throw new FormatException($"draw.handOut must be {Drawing.HandOut}, where the gameplay hands a section out");
        var hollows = Required(draw, "hollows", JsonValueKind.Object);
        if (Str(hollows, "thin") is var thin && thin != Drawing.LeadHollow)
            throw new FormatException($"draw.hollows.thin is {thin ?? "missing"}, the bench draws {Drawing.LeadHollow} as thin");
        if (Str(hollows, "thick") is var thick && thick != Drawing.CopperHollow)
            throw new FormatException($"draw.hollows.thick is {thick ?? "missing"}, the bench draws {Drawing.CopperHollow} as thick");
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, DrawBenchRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new DrawBenchRig(
            Cells(root),
            Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
            SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("die"),
            Anchor("drip"),
            work,
            Required(turns, "thin", JsonValueKind.Number).GetSingle(),
            Required(turns, "thick", JsonValueKind.Number).GetSingle(),
            parts);
    }
}
