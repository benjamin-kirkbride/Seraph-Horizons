using System.Text.Json;

namespace BuckingSawmill.Core;

/// <summary>A cell-local box, 0..1 in each axis for a box that fills its cell.</summary>
public readonly record struct Box(float X1, float Y1, float Z1, float X2, float Y2, float Z2);

/// <summary>One occupied cell of the mill, in the native frame. <see cref="Boxes"/> is empty for a
/// full cube.</summary>
public sealed record RigCell(Int3 Pos, IReadOnlyList<Box> Boxes);

/// <summary>
/// The mill's footprint and anchor points, from assets/buckingsawmill/config/rig.json (written by
/// the model's tooling). Everything is in the native, south-facing frame with the controller cell
/// at (0,0,0), in blocks. The moving parts and the trunk bed are optional (the gameplay does not
/// need them); keys this parser does not know are ignored.
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

    public Rig(IReadOnlyList<RigCell> cells, Int3 powerCell, Side powerFace, Side infeedSide, Side outputSide, Float3 outputPos,
               RigParts? movingParts = null, TrunkBed? trunkBed = null)
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

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parses rig.json. Throws <see cref="FormatException"/> naming what is wrong.</summary>
    public static Rig Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException e)
        {
            throw new FormatException("not valid JSON: " + e.Message, e);
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the root is not an object");
            var cells = new List<RigCell>();
            foreach (var cell in Required(root, "cells", JsonValueKind.Array).EnumerateArray())
            {
                var pos = Int3Of(Required(cell, "pos", JsonValueKind.Array), "cells[].pos");
                var boxes = new List<Box>();
                if (cell.TryGetProperty("boxes", out var b) && b.ValueKind == JsonValueKind.Array)
                    foreach (var box in b.EnumerateArray())
                        boxes.Add(BoxOf(box, pos));
                cells.Add(new RigCell(pos, boxes));
            }
            var output = Required(root, "output", JsonValueKind.Object);
            RigParts? parts = null;
            if (root.TryGetProperty("parts", out var partsJson))
                parts = partsJson.ValueKind == JsonValueKind.Array
                    ? RigParts.Parse(partsJson)
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
            return new Rig(
                cells,
                Int3Of(Required(root, "powerCell", JsonValueKind.Array), "powerCell"),
                SideOf(Required(root, "powerFace", JsonValueKind.String), "powerFace"),
                SideOf(Required(root, "infeedSide", JsonValueKind.String), "infeedSide"),
                SideOf(Required(root, "outputSide", JsonValueKind.String), "outputSide"),
                Float3Of(Required(output, "pos", JsonValueKind.Array), "output.pos"),
                parts, bed);
        }
    }

    private static JsonElement Required(JsonElement obj, string key, JsonValueKind kind)
    {
        if (!obj.TryGetProperty(key, out var value))
            throw new FormatException($"missing \"{key}\"");
        if (value.ValueKind != kind)
            throw new FormatException($"\"{key}\" is {value.ValueKind}, expected {kind}");
        return value;
    }

    private static double[] Numbers(JsonElement array, int count, string what)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != count)
            throw new FormatException($"{what} must be an array of {count} numbers");
        return array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : throw new FormatException($"{what} must be an array of {count} numbers")).ToArray();
    }

    private static Int3 Int3Of(JsonElement array, string what)
    {
        var n = Numbers(array, 3, what);
        if (n.Any(v => v != Math.Floor(v)))
            throw new FormatException($"{what} must be whole numbers");
        return new Int3((int)n[0], (int)n[1], (int)n[2]);
    }

    private static Float3 Float3Of(JsonElement array, string what)
    {
        var n = Numbers(array, 3, what);
        return new Float3((float)n[0], (float)n[1], (float)n[2]);
    }

    private static Box BoxOf(JsonElement array, Int3 cell)
    {
        var n = Numbers(array, 6, $"a box of cell {cell}");
        var box = new Box((float)n[0], (float)n[1], (float)n[2], (float)n[3], (float)n[4], (float)n[5]);
        if (box.X1 > box.X2 || box.Y1 > box.Y2 || box.Z1 > box.Z2)
            throw new FormatException($"a box of cell {cell} has its corners the wrong way round");
        return box;
    }

    private static Side SideOf(JsonElement value, string what) =>
        Sides.TryParse(value.GetString(), out var side)
            ? side
            : throw new FormatException($"{what} \"{value.GetString()}\" is not north, east, south or west");
}
