using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>
/// The cylinder borer's footprint, anchors, work and moving parts, from
/// assets/seraphhorizons/config/cylinderborer-rig.json (written by CylinderBorer/tools/make_shape.py).
/// Native, south-facing frame, controller cell at (0,0,0) (the bench end), blocks. The keys are the
/// model's contract with gameplay (CylinderBorer/README.md, "Rig schema"); unknown keys are ignored.
/// Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
public sealed class CylinderBorerRig
{
    public IReadOnlyList<RigCell> Cells { get; }
    public Int3 PowerCell { get; }
    public Side PowerFace { get; }
    /// <summary>The face a chest or hopper feeds castings through.</summary>
    public Side InfeedSide { get; }
    /// <summary>The face a bored cylinder leaves by.</summary>
    public Side OutputSide { get; }
    /// <summary>Where a bored cylinder comes off (on the output face, beside the cradle).</summary>
    public Float3 Output { get; }
    /// <summary>The bore's drive end, where the cutters come out: chips and smoke.</summary>
    public Float3 Chips { get; }
    /// <summary>The far bushing's oil cup's pipe: the oil drips.</summary>
    public Float3 Drip { get; }
    public WorkQuantity Work { get; }
    /// <summary>Axle turns per block of the head's travel for each class, [none, iron, steel], as the rig gives them.</summary>
    public IReadOnlyList<float> TurnsPerBlock { get; }
    /// <summary>The casting's bounds in the native frame (blocks from the controller's corner), for
    /// its collision box while it is in the cradle: the cells' boxes leave the work out.</summary>
    public Box Casting { get; }
    public RigParts MovingParts { get; }

    public CylinderBorerRig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Side infeedSide, Side outputSide,
                            Float3 output, Float3 chips, Float3 drip, WorkQuantity work, float turnsPerBlockIron, float turnsPerBlockSteel,
                            Box casting, RigParts? movingParts = null)
    {
        var positions = cells.Select(c => c.Pos).ToHashSet();
        if (!positions.Contains(Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (positions.Count != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (cells.Any(c => c.Hollow))
            throw new FormatException("the cylinder borer has no hollow cells");
        if (!positions.Contains(powerCell))
            throw new FormatException($"powerCell {powerCell} is not one of the cells");
        if (powerCell == Int3.Zero)
            throw new FormatException("powerCell is the controller's cell; it must be a ghost cell");
        if (positions.Contains(powerCell + powerFace.Normal()))
            throw new FormatException($"powerCell {powerCell}'s face {powerFace.Code()} is inside the footprint");
        if (infeedSide == outputSide)
            throw new FormatException("infeedSide and outputSide are the same face");
        if (!(turnsPerBlockIron > 0) || !float.IsFinite(turnsPerBlockIron) || !(turnsPerBlockSteel > 0) || !float.IsFinite(turnsPerBlockSteel))
            throw new FormatException("bore.turnsPerBlock must be above 0 for both metals");
        if (Math.Abs(work.Ends[1] - Boring.Travel) > 1e-4 || Math.Abs(work.Ends[2] - Boring.Travel) > 1e-4)
            throw new FormatException($"work.end must be {{thin: {Boring.Travel}, thick: {Boring.Travel}}}, the cutter head's travel through a bore");
        if (casting.X1 >= casting.X2 || casting.Y1 >= casting.Y2 || casting.Z1 >= casting.Z2)
            throw new FormatException("bore.box is empty or the wrong way round");
        Cells = cells;
        PowerCell = powerCell;
        PowerFace = powerFace;
        InfeedSide = infeedSide;
        OutputSide = outputSide;
        Output = output;
        Chips = chips;
        Drip = drip;
        Work = work;
        TurnsPerBlock = [0, turnsPerBlockIron, turnsPerBlockSteel];
        Casting = casting;
        MovingParts = movingParts ?? new RigParts([], work);
    }

    /// <summary>The <c>side</c> variant placed for a player looking along <paramref name="look"/>,
    /// so the borer extends away from them with the bench end (the controller) nearest: it runs along
    /// native south, which a facing turns to itself, as the draw bench does.</summary>
    public static Side PlacedSide(Side look) => look;

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    /// <summary>The casting's box cut into the cells it crosses, each part cell-local (0..1), for
    /// the cells of the footprint only.</summary>
    public IReadOnlyDictionary<Int3, Box> CastingByCell()
    {
        var out_ = new Dictionary<Int3, Box>();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        var b = Casting;
        for (int x = (int)MathF.Floor(b.X1); x < (int)MathF.Ceiling(b.X2); x++)
        for (int y = (int)MathF.Floor(b.Y1); y < (int)MathF.Ceiling(b.Y2); y++)
        for (int z = (int)MathF.Floor(b.Z1); z < (int)MathF.Ceiling(b.Z2); z++)
        {
            var cell = new Int3(x, y, z);
            if (!occupied.Contains(cell))
                continue;
            var part = new Box(Math.Max(b.X1, x) - x, Math.Max(b.Y1, y) - y, Math.Max(b.Z1, z) - z,
                               Math.Min(b.X2, x + 1) - x, Math.Min(b.Y2, y + 1) - y, Math.Min(b.Z2, z + 1) - z);
            if (part.X2 - part.X1 > 1e-4 && part.Y2 - part.Y1 > 1e-4 && part.Z2 - part.Z1 > 1e-4)
                out_[cell] = part;
        }
        return out_;
    }

    /// <summary>The cells just outside a face of the footprint, every level: where a container
    /// against that face stands.</summary>
    public IEnumerable<Int3> Neighbours(Side side)
    {
        var step = side.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        return Cells.Select(c => c.Pos + step).Where(p => !occupied.Contains(p)).Distinct();
    }

    /// <summary>Where a chest or hopper feeds castings from (along the cradle's side).</summary>
    public IEnumerable<Int3> InfeedNeighbours() => Neighbours(InfeedSide);

    /// <summary>The cell just beyond the output face from <see cref="Output"/>'s cell: a container
    /// there takes the bored cylinder.</summary>
    public Int3 OutputNeighbour()
    {
        var cell = new Int3((int)MathF.Floor(Output.X), (int)MathF.Floor(Output.Y), (int)MathF.Floor(Output.Z));
        var step = OutputSide.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        while (occupied.Contains(cell))
            cell += step;
        return cell;
    }

    /// <summary>Where a bored cylinder is dropped when no container takes it: <see cref="Output"/>
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

    /// <summary>Parses cylinderborer-rig.json.</summary>
    public static CylinderBorerRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var work = WorkQuantity.Parse(Required(root, "work", JsonValueKind.Object));
        var bore = Required(root, "bore", JsonValueKind.Object);
        var turns = Required(bore, "turnsPerBlock", JsonValueKind.Object);
        void Holds(string key, string thin, string thick)
        {
            var codes = Required(bore, key, JsonValueKind.Object);
            if (Str(codes, "thin") is var a && a != thin)
                throw new FormatException($"bore.{key}.thin is {a ?? "missing"}, the borer's iron one is {thin}");
            if (Str(codes, "thick") is var b && b != thick)
                throw new FormatException($"bore.{key}.thick is {b ?? "missing"}, the borer's steel one is {thick}");
        }
        Holds("blanks", Boring.BlankIron, Boring.BlankSteel);
        Holds("cylinders", Boring.CylinderIron, Boring.CylinderSteel);
        var box = Numbers(Required(bore, "box", JsonValueKind.Array), 6, "bore.box");
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, CylinderBorerRequires.KnownRequires, work)
                : throw new FormatException("\"parts\" must be an array");
        Float3 Anchor(string key) => Float3Of(Required(Required(root, key, JsonValueKind.Object), "pos", JsonValueKind.Array), key + ".pos");
        return new CylinderBorerRig(
            Cells(root),
            Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
            SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
            SideOf(Required(root, "infeedSide", JsonValueKind.String), "infeedSide"),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Anchor("output"),
            Anchor("chips"),
            Anchor("drip"),
            work,
            Required(turns, "thin", JsonValueKind.Number).GetSingle(),
            Required(turns, "thick", JsonValueKind.Number).GetSingle(),
            new Box((float)box[0], (float)box[1], (float)box[2], (float)box[3], (float)box[4], (float)box[5]),
            parts);
    }
}
