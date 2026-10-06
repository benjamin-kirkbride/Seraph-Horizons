using System.Text.Json;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>A cell-local box, 0..1 in each axis for a box that fills its cell.</summary>
public readonly record struct Box(float X1, float Y1, float Z1, float X2, float Y2, float Z2);

/// <summary>One occupied cell of a machine, in the native frame. <see cref="Boxes"/> is empty for a
/// full cube, unless the cell is <see cref="Hollow"/>: a ghost with no boxes of its own (nothing can
/// be built there), solid only where a loaded trunk's box is. <see cref="Lid"/>, on the top cell of
/// each of the machine's columns, is the cell-local height of the top of a collision-only box over
/// the whole cell (<see cref="LidBox"/>), so a player walking on the machine cannot drop between
/// its boxes; the selection ray does not find it.</summary>
public sealed record RigCell(Int3 Pos, IReadOnlyList<Box> Boxes, bool Hollow = false, float? Lid = null)
{
    /// <summary>A lid's thickness, in blocks.</summary>
    public const float LidThickness = 1 / 16f;

    /// <summary>The lid's box, cell-local; the same on every facing, as it fills the cell across.</summary>
    public Box? LidBox => Lid is { } top ? new Box(0, top - LidThickness, 0, 1, top, 1) : null;
}

/// <summary>
/// Reading a machine's rig.json (the bucking mill's and the rosser's): the document options and
/// the checked readers both rigs share. Every reader throws <see cref="FormatException"/> naming
/// what is wrong.
/// </summary>
public static class RigJson
{
    public static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parses <paramref name="json"/> into a document whose root is an object.</summary>
    public static JsonDocument Parse(string json)
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
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.Dispose();
            throw new FormatException("the root is not an object");
        }
        return doc;
    }

    /// <summary>The rig's <c>cells</c>: each a <c>pos</c>, optional cell-local <c>boxes</c> (none is
    /// a full cube), optional <c>"hollow": true</c>, which a cell with boxes cannot have, and an
    /// optional <c>lid</c> height from <see cref="RigCell.LidThickness"/> to 1.</summary>
    public static List<RigCell> Cells(JsonElement root)
    {
        var cells = new List<RigCell>();
        foreach (var cell in Required(root, "cells", JsonValueKind.Array).EnumerateArray())
        {
            var pos = Int3Of(Required(cell, "pos", JsonValueKind.Array), "cells[].pos");
            var boxes = new List<Box>();
            if (cell.TryGetProperty("boxes", out var b) && b.ValueKind == JsonValueKind.Array)
                foreach (var box in b.EnumerateArray())
                    boxes.Add(BoxOf(box, pos));
            bool hollow = Bool(cell, "hollow");
            if (hollow && boxes.Count > 0)
                throw new FormatException($"cell {pos} is hollow but has boxes of its own");
            float? lid = null;
            if (cell.TryGetProperty("lid", out var l))
            {
                float top = l.ValueKind == JsonValueKind.Number ? l.GetSingle() : float.NaN;
                if (!(top >= RigCell.LidThickness - 1e-6f && top <= 1))
                    throw new FormatException($"the lid of cell {pos} must be a height from {RigCell.LidThickness} to 1");
                lid = top;
            }
            cells.Add(new RigCell(pos, boxes, hollow, lid));
        }
        return cells;
    }

    public static JsonElement Required(JsonElement obj, string key, JsonValueKind kind)
    {
        if (!obj.TryGetProperty(key, out var value))
            throw new FormatException($"missing \"{key}\"");
        if (value.ValueKind != kind)
            throw new FormatException($"\"{key}\" is {value.ValueKind}, expected {kind}");
        return value;
    }

    public static double[] Numbers(JsonElement array, int count, string what)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != count)
            throw new FormatException($"{what} must be an array of {count} numbers");
        return array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : throw new FormatException($"{what} must be an array of {count} numbers")).ToArray();
    }

    public static Int3 Int3Of(JsonElement array, string what)
    {
        var n = Numbers(array, 3, what);
        if (n.Any(v => v != Math.Floor(v)))
            throw new FormatException($"{what} must be whole numbers");
        return new Int3((int)n[0], (int)n[1], (int)n[2]);
    }

    public static Float3 Float3Of(JsonElement array, string what)
    {
        var n = Numbers(array, 3, what);
        return new Float3((float)n[0], (float)n[1], (float)n[2]);
    }

    public static Box BoxOf(JsonElement array, Int3 cell)
    {
        var n = Numbers(array, 6, $"a box of cell {cell}");
        var box = new Box((float)n[0], (float)n[1], (float)n[2], (float)n[3], (float)n[4], (float)n[5]);
        if (box.X1 > box.X2 || box.Y1 > box.Y2 || box.Z1 > box.Z2)
            throw new FormatException($"a box of cell {cell} has its corners the wrong way round");
        return box;
    }

    public static Side SideOf(JsonElement value, string what) =>
        Sides.TryParse(value.GetString(), out var side)
            ? side
            : throw new FormatException($"{what} \"{value.GetString()}\" is not north, east, south or west");

    /// <summary>"x", "y" or "z".</summary>
    public static Axis AxisOf(string? code, string what) => code switch
    {
        "x" => Axis.X,
        "y" => Axis.Y,
        "z" => Axis.Z,
        _ => throw new FormatException($"{what} \"{code}\" is not x, y or z"),
    };

    public static bool Bool(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    public static string? Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static float Num(JsonElement obj, string key, float fallback) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}
