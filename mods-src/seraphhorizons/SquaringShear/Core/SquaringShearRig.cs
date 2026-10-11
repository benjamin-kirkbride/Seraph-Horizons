using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.SquaringShear.Core;

/// <summary>
/// The squaring shear's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/squaringshear-rig.json (written by SquaringShear/tools/make_shape.py).
/// Native, south-facing frame, controller cell at (0,0,0) (the table and the treadle's foot), blocks.
/// The keys are the model's contract with gameplay (SquaringShear/README.md, "Rig schema"); unknown
/// keys are ignored. A hand machine: a rig naming a power cell or face is refused. Throws
/// <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class SquaringShearRig
{
    /// <summary>The part whose gauge windows are the strokes (the contract's stable id).</summary>
    public const string CrossheadPart = "crosshead";

    public IReadOnlyList<RigCell> Cells { get; }
    /// <summary>The face the half plates leave by (over the table, towards the operator).</summary>
    public Side OutputSide { get; }
    /// <summary>Where the half plates lie at W = 1.</summary>
    public Float3 Output { get; }
    /// <summary>The middle of the laid plate: the loading sound.</summary>
    public Float3 Plate { get; }
    /// <summary>The middle of the cut: the blades' sound and metal dust.</summary>
    public Float3 Edge { get; }
    public WorkQuantity Work { get; }
    /// <summary>Treadle strokes per plate of each class, [none, lead, copper], as the model is drawn.</summary>
    public IReadOnlyList<float> StrokesPerPlate { get; }
    public RigParts MovingParts { get; }

    /// <summary>The crosshead's strokes, as stretches of the cut cycle W (from the <c>crosshead</c>
    /// part's gauge windows): the cut, one a plate. Empty when the rig has no parts.</summary>
    public IReadOnlyList<(float From, float To)> Strokes { get; }

    /// <summary>The middle of each stroke, the blade at the bottom: where the cut is heard.</summary>
    public IReadOnlyList<double> CutMoments => Strokes.Select(f => (f.From + (double)f.To) / 2).ToList();

    public SquaringShearRig(IReadOnlyList<RigCell> cells, Side outputSide, Float3 output, Float3 plate, Float3 edge,
                            WorkQuantity work, float strokesLead, float strokesCopper, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the squaring shear has no hollow cells");
        if (!(strokesLead > 0) || !float.IsFinite(strokesLead) || !(strokesCopper > 0) || !float.IsFinite(strokesCopper))
            throw new FormatException("cut.strokesPerPlate must be above 0 for both metals");
        if (work.Ends[1] != 1 || work.Ends[2] != 1)
            throw new FormatException("work.end must be {thin: 1, thick: 1}, one plate's cut cycle");
        Cells = cells;
        OutputSide = outputSide;
        Output = output;
        Plate = plate;
        Edge = edge;
        Work = work;
        StrokesPerPlate = [0, strokesLead, strokesCopper];
        MovingParts = movingParts ?? new RigParts([], work);
        Strokes = MovingParts.Parts.Where(p => p.Id == CrossheadPart)
            .SelectMany(p => p.Drivers)
            .Where(d => d.Type == DriverType.Gauge && d.Windows != null)
            .SelectMany(d => d.Windows!)
            .Select(w => (w.From, w.To))
            .Distinct()
            .OrderBy(w => w.From)
            .ToList();
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>,
    /// so the shear extends away from them with the table end (the controller) nearest: it runs along
    /// native south, which a facing turns to itself (the press brake's rule).</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell, where the half
    /// plates are dropped.</summary>
    public Int3 OutputNeighbour()
    {
        var cell = new Int3((int)MathF.Floor(Output.X), (int)MathF.Floor(Output.Y), (int)MathF.Floor(Output.Z));
        var step = OutputSide.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        while (occupied.Contains(cell))
            cell += step;
        return cell;
    }

    /// <summary>Where the half plates are dropped: <see cref="Output"/>
    /// moved out through the output face to <paramref name="beyond"/> blocks past it.</summary>
    public Float3 OutputDrop(float beyond = 0.15f)
    {
        var step = OutputSide.Normal();
        var face = OutputNeighbour();
        float Plane(int along, int n) => n > 0 ? along : along + 1;
        return step switch
        {
            { X: not 0 } => Output with { X = Plane(face.X, step.X) + step.X * beyond },
            { Z: not 0 } => Output with { Z = Plane(face.Z, step.Z) + step.Z * beyond },
            _ => Output,
        };
    }

    /// <summary>Whether W is inside one of the strokes: the blade is moving.</summary>
    public bool IsCutting(double work) => Strokes.Any(f => work >= f.From && work <= f.To);

    /// <summary>Parses squaringshear-rig.json.</summary>
    public static SquaringShearRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        foreach (var key in new[] { "powerCell", "powerFace" })
            if (root.TryGetProperty(key, out _))
                throw new FormatException($"the squaring shear is a hand machine: the rig has no {key}");
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var cut = Required(root, "cut", JsonValueKind.Object);
        var strokes = Required(cut, "strokesPerPlate", JsonValueKind.Object);
        if (cut.TryGetProperty("halfPlatesPerPlate", out var per)
            && (per.ValueKind != JsonValueKind.Number || per.GetDouble() != Cutting.HalfPlatesPerPlate))
            throw new FormatException($"cut.halfPlatesPerPlate must be {Cutting.HalfPlatesPerPlate}");
        void Expect(string group, string name, string? expected)
        {
            if (cut.TryGetProperty(group, out var g) && Str(g, name) is { } got && got != expected)
                throw new FormatException($"cut.{group}.{name} is {got}, the shear's is {expected}");
        }
        Expect("plates", "thin", Cutting.PlateFor(1));
        Expect("plates", "thick", Cutting.PlateFor(2));
        Expect("halfPlates", "thin", Cutting.HalfPlateFor(1));
        Expect("halfPlates", "thick", Cutting.HalfPlateFor(2));
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, SquaringShearRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new SquaringShearRig(
            Cells(root),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("plate"),
            Anchor("edge"),
            work,
            Required(strokes, "thin", JsonValueKind.Number).GetSingle(),
            Required(strokes, "thick", JsonValueKind.Number).GetSingle(),
            parts);
    }
}
