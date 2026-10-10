using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.PressBrake.Core;

/// <summary>
/// The press brake's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/pressbrake-rig.json (written by PressBrake/tools/make_shape.py).
/// Native, south-facing frame, controller cell at (0,0,0) (the leaf end), blocks. The keys are the
/// model's contract with gameplay (PressBrake/README.md, "Rig schema"); unknown keys are ignored. A
/// hand machine: a rig naming a power cell or face is refused. Throws <see cref="FormatException"/>
/// naming what is wrong.
/// </summary>
public sealed class PressBrakeRig
{
    /// <summary>The part whose gauge windows are the folds (the contract's stable id).</summary>
    public const string LeafPart = "leaf";

    public IReadOnlyList<RigCell> Cells { get; }
    /// <summary>The face the folded angle leaves by (over the leaf, towards the operator).</summary>
    public Side OutputSide { get; }
    /// <summary>Where the angle lies at W = 1.</summary>
    public Float3 Output { get; }
    /// <summary>The middle of the laid plate: the loading sound.</summary>
    public Float3 Plate { get; }
    /// <summary>The middle of the folding edge: the bend's sound and metal dust.</summary>
    public Float3 Edge { get; }
    public WorkQuantity Work { get; }
    /// <summary>Lever turns per plate of each class, [none, lead, copper], as the model is drawn.</summary>
    public IReadOnlyList<float> LeverTurnsPerPlate { get; }
    public RigParts MovingParts { get; }

    /// <summary>The leaf's swings, as stretches of the fold cycle W (from the <c>leaf</c> part's
    /// gauge windows): the fold, one a plate. Empty when the rig has no parts.</summary>
    public IReadOnlyList<(float From, float To)> Folds { get; }

    /// <summary>The middle of each fold: where the bend is heard.</summary>
    public IReadOnlyList<double> FoldMoments => Folds.Select(f => (f.From + (double)f.To) / 2).ToList();

    public PressBrakeRig(IReadOnlyList<RigCell> cells, Side outputSide, Float3 output, Float3 plate, Float3 edge,
                         WorkQuantity work, float leverTurnsLead, float leverTurnsCopper, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the press brake has no hollow cells");
        if (!(leverTurnsLead > 0) || !float.IsFinite(leverTurnsLead) || !(leverTurnsCopper > 0) || !float.IsFinite(leverTurnsCopper))
            throw new FormatException("fold.leverTurnsPerPlate must be above 0 for both metals");
        if (work.Ends[1] != 1 || work.Ends[2] != 1)
            throw new FormatException("work.end must be {thin: 1, thick: 1}, one plate's fold cycle");
        Cells = cells;
        OutputSide = outputSide;
        Output = output;
        Plate = plate;
        Edge = edge;
        Work = work;
        LeverTurnsPerPlate = [0, leverTurnsLead, leverTurnsCopper];
        MovingParts = movingParts ?? new RigParts([], work);
        Folds = MovingParts.Parts.Where(p => p.Id == LeafPart)
            .SelectMany(p => p.Drivers)
            .Where(d => d.Type == DriverType.Gauge && d.Rotates && d.Windows != null)
            .SelectMany(d => d.Windows!)
            .Select(w => (w.From, w.To))
            .Distinct()
            .OrderBy(w => w.From)
            .ToList();
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>,
    /// so the brake extends away from them with the leaf end (the controller) nearest: it runs along
    /// native south, which a facing turns to itself (the draw bench's rule).</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell, where the folded angles
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

    /// <summary>Where a folded angle is dropped: <see cref="Output"/>
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

    /// <summary>Whether W is inside one of the folds: the leaf is swinging.</summary>
    public bool IsFolding(double work) => Folds.Any(f => work >= f.From && work <= f.To);

    /// <summary>Parses pressbrake-rig.json.</summary>
    public static PressBrakeRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        foreach (var key in new[] { "powerCell", "powerFace" })
            if (root.TryGetProperty(key, out _))
                throw new FormatException($"the press brake is a hand machine: the rig has no {key}");
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var fold = Required(root, "fold", JsonValueKind.Object);
        var turns = Required(fold, "leverTurnsPerPlate", JsonValueKind.Object);
        if (fold.TryGetProperty("anglesPerPlate", out var per) && (per.ValueKind != JsonValueKind.Number || per.GetDouble() != Folding.AnglesPerPlate))
            throw new FormatException($"fold.anglesPerPlate must be {Folding.AnglesPerPlate}");
        void Expect(string group, string name, string? expected)
        {
            if (fold.TryGetProperty(group, out var g) && Str(g, name) is { } got && got != expected)
                throw new FormatException($"fold.{group}.{name} is {got}, the brake's is {expected}");
        }
        Expect("plates", "thin", Folding.PlateFor(1));
        Expect("plates", "thick", Folding.PlateFor(2));
        Expect("angles", "thin", Folding.AngleFor(1));
        Expect("angles", "thick", Folding.AngleFor(2));
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, PressBrakeRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new PressBrakeRig(
            Cells(root),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("plate"),
            Anchor("edge"),
            work,
            Required(turns, "thin", JsonValueKind.Number).GetSingle(),
            Required(turns, "thick", JsonValueKind.Number).GetSingle(),
            parts);
    }
}
