using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuckingSawmill.Core;

/// <summary>
/// 4×4 matrices as 16 floats in column-major order, the layout of the game's <c>Mat4f</c> and
/// <c>Matrixf</c> (translation in elements 12..14). Points are transformed as <c>M · p</c>.
/// </summary>
public static class Mat4
{
    public static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    public static float[] Translation(float x, float y, float z)
    {
        var m = Identity();
        m[12] = x;
        m[13] = y;
        m[14] = z;
        return m;
    }

    /// <summary>a · b: b applies first.</summary>
    public static float[] Multiply(float[] a, float[] b)
    {
        var r = new float[16];
        for (int col = 0; col < 4; col++)
            for (int row = 0; row < 4; row++)
            {
                float s = 0;
                for (int k = 0; k < 4; k++)
                    s += a[k * 4 + row] * b[col * 4 + k];
                r[col * 4 + row] = s;
            }
        return r;
    }

    /// <summary>A right-handed rotation by <paramref name="radians"/> about a principal axis
    /// through <paramref name="pivot"/>.</summary>
    public static float[] Rotation(Axis axis, double radians, Float3 pivot)
    {
        float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
        var m = Identity();
        switch (axis)
        {
            case Axis.X:
                m[5] = c; m[9] = -s; m[6] = s; m[10] = c;
                break;
            case Axis.Y:
                m[0] = c; m[8] = s; m[2] = -s; m[10] = c;
                break;
            default:
                m[0] = c; m[4] = -s; m[1] = s; m[5] = c;
                break;
        }
        return Multiply(Translation(pivot.X, pivot.Y, pivot.Z), Multiply(m, Translation(-pivot.X, -pivot.Y, -pivot.Z)));
    }

    public static Float3 Apply(float[] m, Float3 p) => new(
        m[0] * p.X + m[4] * p.Y + m[8] * p.Z + m[12],
        m[1] * p.X + m[5] * p.Y + m[9] * p.Z + m[13],
        m[2] * p.X + m[6] * p.Y + m[10] * p.Z + m[14]);

    /// <summary>The native (south-facing) frame to a mill facing <paramref name="facing"/>, in
    /// blocks from the controller cell's corner: the turn <see cref="Footprint"/> uses for cells
    /// and points, which is also the block shape's rotateY.</summary>
    public static float[] Facing(Side facing)
    {
        var n = facing.Normal();
        // (x, z) -> (x·nz + z·nx, −x·nx + z·nz), about the controller cell's centre
        var r = Identity();
        r[0] = n.Z; r[8] = n.X;
        r[2] = -n.X; r[10] = n.Z;
        return Multiply(Translation(0.5f, 0, 0.5f), Multiply(r, Translation(-0.5f, 0, -0.5f)));
    }
}

public enum Axis { X, Y, Z }

public enum DriverType { Rotate, Slide, Swing, Feed }

/// <summary>One motion of a rig part, as rig.json's <c>parts[].drivers</c> describes it. θ is the
/// shaft angle, p the cut's progress (0..1); distances are blocks, angles radians.</summary>
public sealed record Driver(DriverType Type, Axis Axis, Float3 Pivot, float Ratio, float Amplitude, float Phase, float Travel)
{
    /// <summary>rotate: angle ratio·θ about pivot. slide: offset amplitude·sin(ratio·θ + phase).
    /// swing: angle amplitude·sin(ratio·θ + phase) about pivot. feed: offset travel·p.</summary>
    public float[] Matrix(double theta, double progress)
    {
        switch (Type)
        {
            case DriverType.Rotate:
                return Mat4.Rotation(Axis, Ratio * theta, Pivot);
            case DriverType.Swing:
                return Mat4.Rotation(Axis, Amplitude * Math.Sin(Ratio * theta + Phase), Pivot);
            default:
                float off = Type == DriverType.Slide
                    ? (float)(Amplitude * Math.Sin(Ratio * theta + Phase))
                    : (float)(Travel * progress);
                return Mat4.Translation(Axis == Axis.X ? off : 0, Axis == Axis.Y ? off : 0, Axis == Axis.Z ? off : 0);
        }
    }
}

/// <summary>A moving part of the mill's model: the elements its <see cref="Match"/> globs claim,
/// drawn only when <see cref="Requires"/> is fitted, moved by its drivers and then by its
/// <see cref="Ride"/> part's whole transform.</summary>
public sealed record RigPart(string Id, IReadOnlyList<string> Match, string? Requires, string? Ride, IReadOnlyList<Driver> Drivers)
{
    /// <summary>The <c>requires</c> values the gameplay knows.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { "crankshaft", "sash1", "sash2", "blade1", "blade2" };

    /// <summary>Whether a part needing <paramref name="requires"/> is drawn with these parts fitted.</summary>
    public static bool Fitted(string? requires, int sashes, bool crankshaft, int blades) => requires switch
    {
        null => true,
        "crankshaft" => crankshaft,
        "sash1" => sashes >= 1,
        "sash2" => sashes >= 2,
        "blade1" => blades >= 1,
        "blade2" => blades >= 2,
        _ => false,
    };
}

/// <summary>Where a trunk lies: <see cref="Origin"/> is the centre of the bed's top surface.</summary>
public sealed record TrunkBed(Float3 Origin, Axis Axis, float Length);

/// <summary>The rig's parts in order, with the maths that poses them.</summary>
public sealed class RigParts
{
    public IReadOnlyList<RigPart> Parts { get; }
    private readonly Regex[][] _globs;
    private readonly int[] _order;   // parts sorted so a ride parent comes before its riders
    private readonly int[] _ride;    // index of each part's ride parent, or -1

    public RigParts(IReadOnlyList<RigPart> parts)
    {
        var ids = new Dictionary<string, int>();
        for (int i = 0; i < parts.Count; i++)
            if (!ids.TryAdd(parts[i].Id, i))
                throw new FormatException($"part \"{parts[i].Id}\" is listed twice");
        _ride = new int[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            var ride = parts[i].Ride;
            if (ride == null)
                _ride[i] = -1;
            else if (!ids.TryGetValue(ride, out _ride[i]))
                throw new FormatException($"part \"{parts[i].Id}\" rides \"{ride}\", which is not a part");
        }
        var order = new List<int>();
        var state = new int[parts.Count]; // 0 new, 1 visiting, 2 done
        void Visit(int i)
        {
            if (state[i] == 2)
                return;
            if (state[i] == 1)
                throw new FormatException($"part \"{parts[i].Id}\" rides itself through a cycle");
            state[i] = 1;
            if (_ride[i] >= 0)
                Visit(_ride[i]);
            state[i] = 2;
            order.Add(i);
        }
        for (int i = 0; i < parts.Count; i++)
            Visit(i);
        _order = order.ToArray();
        Parts = parts;
        _globs = parts.Select(p => p.Match.Select(Glob).ToArray()).ToArray();
    }

    /// <summary>A case-sensitive glob where <c>*</c> matches any run of characters.</summary>
    public static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", RegexOptions.CultureInvariant);

    public int IndexOf(string id)
    {
        for (int i = 0; i < Parts.Count; i++)
            if (Parts[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>The first part with a glob matching any name in an element's chain (its
    /// ancestors' names and its own), or -1.</summary>
    public int PartOf(IEnumerable<string> nameChain)
    {
        var names = nameChain as IList<string> ?? nameChain.ToList();
        for (int i = 0; i < Parts.Count; i++)
            foreach (var glob in _globs[i])
                foreach (var name in names)
                    if (glob.IsMatch(name))
                        return i;
        return -1;
    }

    /// <summary>One native-frame matrix per part, in part order: the drivers applied in list
    /// order to the authored geometry (pivots in the authored frame), then the ride part's whole
    /// transform.</summary>
    public float[][] Matrices(double theta, double progress)
    {
        var result = new float[Parts.Count][];
        foreach (int i in _order)
        {
            var m = Mat4.Identity();
            foreach (var d in Parts[i].Drivers)
                m = Mat4.Multiply(d.Matrix(theta, progress), m);
            if (_ride[i] >= 0)
                m = Mat4.Multiply(result[_ride[i]], m);
            result[i] = m;
        }
        return result;
    }

    // ---- parsing (rig.json's "parts") ----

    internal static RigParts Parse(JsonElement array)
    {
        var parts = new List<RigPart>();
        foreach (var p in array.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                throw new FormatException("parts[] must be objects");
            string id = Str(p, "id") ?? throw new FormatException("a part has no \"id\"");
            if (!p.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Array || match.GetArrayLength() == 0)
                throw new FormatException($"part \"{id}\" has no \"match\" globs");
            var globs = match.EnumerateArray().Select(g => g.ValueKind == JsonValueKind.String
                ? g.GetString()!
                : throw new FormatException($"part \"{id}\": match globs must be strings")).ToList();
            var requires = Str(p, "requires");
            if (requires != null && !RigPart.KnownRequires.Contains(requires))
                throw new FormatException($"part \"{id}\" requires \"{requires}\", which the gameplay does not know");
            var drivers = new List<Driver>();
            if (p.TryGetProperty("drivers", out var ds) && ds.ValueKind == JsonValueKind.Array)
                foreach (var d in ds.EnumerateArray())
                    drivers.Add(DriverOf(d, id));
            parts.Add(new RigPart(id, globs, requires, Str(p, "ride"), drivers));
        }
        return new RigParts(parts);
    }

    private static Driver DriverOf(JsonElement d, string part)
    {
        string where = $"part \"{part}\"";
        var type = Str(d, "type") switch
        {
            "rotate" => DriverType.Rotate,
            "slide" => DriverType.Slide,
            "swing" => DriverType.Swing,
            "feed" => DriverType.Feed,
            var t => throw new FormatException($"{where}: unknown driver type \"{t}\""),
        };
        var axis = Str(d, "axis") switch
        {
            "x" => Axis.X,
            "y" => Axis.Y,
            "z" => Axis.Z,
            var a => throw new FormatException($"{where}: driver axis \"{a}\" is not x, y or z"),
        };
        var pivot = new Float3(0, 0, 0);
        if (type is DriverType.Rotate or DriverType.Swing)
        {
            if (!d.TryGetProperty("pivot", out var pv) || pv.ValueKind != JsonValueKind.Array || pv.GetArrayLength() != 3)
                throw new FormatException($"{where}: a {Str(d, "type")} driver needs a pivot of 3 numbers");
            var n = pv.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            pivot = new Float3(n[0], n[1], n[2]);
        }
        return new Driver(type, axis, pivot, Num(d, "ratio", 1), Num(d, "amplitude", 0), Num(d, "phase", 0), Num(d, "travel", 0));
    }

    private static string? Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static float Num(JsonElement obj, string key, float fallback) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}

/// <summary>The client-side clock of the model's motion.</summary>
public static class MillMotion
{
    /// <summary>
    /// The native-frame shaft angle θ from the power ghost's <c>AngleRad</c>. A vanilla axle along
    /// the world's x or z axis draws itself turned by −AngleRad about that axis (its AxisSign is
    /// −1), and the mill's input shaft turns with it: native +x points along world +x (south), −x
    /// (north), −z (east) or +z (west). The rig's rotations are right-handed about the native axis.
    /// </summary>
    public static double NativeShaftAngle(Side facing, double angleRad) =>
        facing is Side.South or Side.West ? -angleRad : angleRad;

    /// <summary>The shortest signed step from <paramref name="from"/> to <paramref name="to"/>,
    /// in (−π, π].</summary>
    public static double WrappedDelta(double from, double to)
    {
        double d = (to - from) % (2 * Math.PI);
        if (d > Math.PI)
            d -= 2 * Math.PI;
        else if (d <= -Math.PI)
            d += 2 * Math.PI;
        return d;
    }

    /// <summary>How many half turns (strokes) start between two accumulated angles: crossings
    /// of a multiple of π, either way.</summary>
    public static int StrokesBetween(double from, double to) =>
        Math.Abs((int)Math.Floor(to / Math.PI) - (int)Math.Floor(from / Math.PI));

    /// <summary>IW's speed bands for its stroke sounds: s(low) below 0.34, f(ast) from 0.67.</summary>
    public static char SpeedBand(float speed) => speed < 0.34f ? 's' : speed >= 0.67f ? 'f' : 'm';

    /// <summary>
    /// Lays a trunk mesh on the bed: <paramref name="min"/>..<paramref name="max"/> is the mesh's
    /// bounding box as tessellated (blocks). The trunk's long horizontal axis is turned onto the
    /// bed's axis, its footprint centred on the bed's origin and its underside put on it.
    /// </summary>
    public static float[] TrunkPlacement(Float3 min, Float3 max, TrunkBed bed)
    {
        var centre = new Float3((min.X + max.X) / 2, min.Y, (min.Z + max.Z) / 2);
        bool alongZ = max.Z - min.Z > max.X - min.X + 1e-4f;
        bool wantZ = bed.Axis == Axis.Z;
        var m = Mat4.Translation(-centre.X, -centre.Y, -centre.Z);
        if (alongZ != wantZ)
            m = Mat4.Multiply(Mat4.Rotation(Axis.Y, Math.PI / 2, new Float3(0, 0, 0)), m);
        return Mat4.Multiply(Mat4.Translation(bed.Origin.X, bed.Origin.Y, bed.Origin.Z), m);
    }
}
