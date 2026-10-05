using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>
/// The mill's footprint and anchor points, from assets/seraphhorizons/config/buckingmill-rig.json (written by
/// the model's tooling). Everything is in the native, south-facing frame with the controller cell
/// at (0,0,0), in blocks. The moving parts and the trunk bed are optional (the gameplay does not
/// need them), and so is the saws' travel (defaults apply); keys this parser does not know are
/// ignored.
/// </summary>
public sealed class Rig
{
    public IReadOnlyList<RigCell> Cells { get; }
    public Int3 PowerCell { get; }
    /// <summary>The native-frame face of <see cref="PowerCell"/> that takes the axle.</summary>
    public Side PowerFace { get; }
    /// <summary>The native-frame side a trunk rack must touch.</summary>
    public Side InfeedSide { get; }
    /// <summary>The native-frame side cut logs leave by.</summary>
    public Side OutputSide { get; }
    /// <summary>Where cut logs appear, a point in native-frame blocks.</summary>
    public Float3 OutputPos { get; }
    /// <summary>The model's moving parts (rig.json's <c>parts</c>); empty when the file has none.</summary>
    public RigParts MovingParts { get; }
    /// <summary>Where a loaded trunk is drawn, or null.</summary>
    public TrunkBed? TrunkBed { get; }
    /// <summary>How far the saws travel (rig.json's <c>saw</c>), or <see cref="SawTravel.Default"/>
    /// when the file has none.</summary>
    public SawTravel Saw { get; }

    public Rig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Side infeedSide, Side outputSide, Float3 outputPos,
               RigParts? movingParts = null, TrunkBed? trunkBed = null, SawTravel? saw = null)
    {
        if (!cells.Any(c => c.Pos == Int3.Zero))
            throw new FormatException("the cells do not include the controller's, [0,0,0]");
        if (cells.Select(c => c.Pos).Distinct().Count() != cells.Count)
            throw new FormatException("a cell is listed twice");
        if (!cells.Any(c => c.Pos == powerCell))
            throw new FormatException($"powerCell {powerCell} is not one of the cells");
        if (powerCell == Int3.Zero)
            throw new FormatException("powerCell is the controller's cell; it must be a ghost cell");
        Cells = cells;
        PowerCell = powerCell;
        PowerFace = powerFace;
        InfeedSide = infeedSide;
        OutputSide = outputSide;
        OutputPos = outputPos;
        MovingParts = movingParts ?? new RigParts([]);
        TrunkBed = trunkBed;
        Saw = saw ?? SawTravel.Default;
        if (Saw.TopY <= Saw.BottomY)
            throw new FormatException($"saw.topY {Saw.TopY} must be above saw.bottomY {Saw.BottomY}");
    }

    /// <summary>The cells other than the controller's, in file order.</summary>
    public IEnumerable<RigCell> GhostCells => Cells.Where(c => c.Pos != Int3.Zero);

    public RigCell? CellAt(Int3 pos) => Cells.FirstOrDefault(c => c.Pos == pos);

    /// <summary>The ground-level cells just outside the infeed side: where a rack has to stand.
    /// Native frame.</summary>
    public IEnumerable<Int3> InfeedNeighbours()
    {
        var step = InfeedSide.Normal();
        var occupied = Cells.Select(c => c.Pos).ToHashSet();
        return Cells.Where(c => c.Pos.Y == 0)
            .Select(c => c.Pos + step)
            .Where(p => !occupied.Contains(p))
            .Distinct();
    }

    /// <summary>Parses rig.json. Throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static Rig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var cells = Cells(root);
        var output = Required(root, "output", JsonValueKind.Object);
        RigParts? parts = null;
        if (root.TryGetProperty("parts", out var partsJson))
            parts = partsJson.ValueKind == JsonValueKind.Array
                ? RigParts.Parse(partsJson, MillRequires.KnownRequires, null)
                : throw new FormatException("\"parts\" must be an array");
        TrunkBed? bed = null;
        if (root.TryGetProperty("trunkBed", out var bedJson) && bedJson.ValueKind == JsonValueKind.Object)
            bed = new TrunkBed(
                Float3Of(Required(bedJson, "origin", JsonValueKind.Array), "trunkBed.origin"),
                Required(bedJson, "axis", JsonValueKind.String).GetString() switch
                {
                    "x" => Axis.X,
                    "z" => Axis.Z,
                    var a => throw new FormatException($"trunkBed.axis \"{a}\" is not x or z"),
                },
                bedJson.TryGetProperty("length", out var len) && len.ValueKind == JsonValueKind.Number ? len.GetSingle() : 0);
        SawTravel? saw = null;
        if (root.TryGetProperty("saw", out var sawJson))
            saw = sawJson.ValueKind == JsonValueKind.Object
                ? new SawTravel(Required(sawJson, "topY", JsonValueKind.Number).GetSingle(),
                                Required(sawJson, "bottomY", JsonValueKind.Number).GetSingle())
                : throw new FormatException("\"saw\" must be an object");
        return new Rig(
            cells,
            Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
            SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
            SideOf(Required(root, "infeedSide", JsonValueKind.String), "infeedSide"),
            SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
            Float3Of(Required(output, "pos", JsonValueKind.Array), "output.pos"),
            parts, bed, saw);
    }
}
