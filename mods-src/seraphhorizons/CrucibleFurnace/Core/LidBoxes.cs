using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>A shape element's box, voxels, with its rotation about its origin (degrees, the game's
/// order: x, then y, then z).</summary>
public sealed record ShapeBox(string Name, Float3 From, Float3 To, Float3 Origin, float RotX, float RotY, float RotZ)
{
    /// <summary>Its eight corners in the native frame, blocks, its own rotation applied.</summary>
    public IEnumerable<Float3> Corners()
    {
        var m = Mat4.Identity();
        var o = new Float3(Origin.X / 16f, Origin.Y / 16f, Origin.Z / 16f);
        const double Deg = Math.PI / 180;
        if (RotZ != 0)
            m = Mat4.Multiply(Mat4.Rotation(Axis.Z, RotZ * Deg, o), m);
        if (RotY != 0)
            m = Mat4.Multiply(Mat4.Rotation(Axis.Y, RotY * Deg, o), m);
        if (RotX != 0)
            m = Mat4.Multiply(Mat4.Rotation(Axis.X, RotX * Deg, o), m);
        foreach (float x in new[] { From.X, To.X })
            foreach (float y in new[] { From.Y, To.Y })
                foreach (float z in new[] { From.Z, To.Z })
                    yield return Mat4.Apply(m, new Float3(x / 16f, y / 16f, z / 16f));
    }
}

/// <summary>The lid's bounds open (resting on its stops) and everything it sweeps between open and
/// shut, native blocks.</summary>
public sealed record LidSweep(Box Open, Box Swept);

/// <summary>
/// The lid's collision (#796): what the open lid stands in, and what it sweeps while it turns, as
/// boxes in the native frame (blocks), measured from the furnace's shape and posed by the rig's lid
/// driver, so they follow the model. The lid's elements are top-level boxes, each turned about its own
/// centre (the shape is authored open); children of an element are not read.
/// </summary>
public static class LidBoxes
{
    /// <summary>The top-level elements of <paramref name="shapeJson"/> whose names <paramref name="part"/>'s
    /// globs claim.</summary>
    public static IReadOnlyList<ShapeBox> Elements(string shapeJson, RigPart part)
    {
        using var doc = JsonDocument.Parse(shapeJson, RigJson.Options);
        var globs = part.Match.Select(RigParts.Glob).ToList();
        var boxes = new List<ShapeBox>();
        foreach (var e in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!globs.Any(g => g.IsMatch(name)))
                continue;
            Float3 V(string key, Float3 fallback) => e.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array
                ? RigJson.Float3Of(a, name + "." + key)
                : fallback;
            float R(string key) => e.TryGetProperty(key, out var r) && r.ValueKind == JsonValueKind.Number ? r.GetSingle() : 0;
            var from = V("from", default);
            var to = V("to", default);
            boxes.Add(new ShapeBox(name, from, to, V("rotationOrigin", new Float3(0, 0, 0)), R("rotationX"), R("rotationY"), R("rotationZ")));
        }
        return boxes;
    }

    /// <summary>The bounds of <paramref name="elements"/> posed by <paramref name="matrix"/>; null for none.</summary>
    public static Box? Bounds(IEnumerable<ShapeBox> elements, float[] matrix)
    {
        float x1 = float.MaxValue, y1 = float.MaxValue, z1 = float.MaxValue;
        float x2 = float.MinValue, y2 = float.MinValue, z2 = float.MinValue;
        bool any = false;
        foreach (var p in elements.SelectMany(e => e.Corners()).Select(c => Mat4.Apply(matrix, c)))
        {
            any = true;
            x1 = Math.Min(x1, p.X); y1 = Math.Min(y1, p.Y); z1 = Math.Min(z1, p.Z);
            x2 = Math.Max(x2, p.X); y2 = Math.Max(y2, p.Y); z2 = Math.Max(z2, p.Z);
        }
        return any ? new Box(x1, y1, z1, x2, y2, z2) : null;
    }

    /// <summary>The lid's bounds at <paramref name="depth"/> (0 open, 1 shut).</summary>
    public static Box? At(IEnumerable<ShapeBox> elements, RigParts parts, int lid, double depth) =>
        Bounds(elements, parts.Matrices(new RigInput(0, Depth: depth))[lid]);

    /// <summary>Everything the lid passes through from open to shut, sampled every
    /// <paramref name="step"/> of depth: the union's bounds.</summary>
    public static Box? Swept(IReadOnlyList<ShapeBox> elements, RigParts parts, int lid, double step = 0.02)
    {
        Box? all = null;
        for (double d = 0; d <= 1 + 1e-9; d += step)
            if (At(elements, parts, lid, Math.Min(1, d)) is { } b)
                all = all is { } a ? new Box(Math.Min(a.X1, b.X1), Math.Min(a.Y1, b.Y1), Math.Min(a.Z1, b.Z1),
                                             Math.Max(a.X2, b.X2), Math.Max(a.Y2, b.Y2), Math.Max(a.Z2, b.Z2)) : b;
        return all;
    }
}
