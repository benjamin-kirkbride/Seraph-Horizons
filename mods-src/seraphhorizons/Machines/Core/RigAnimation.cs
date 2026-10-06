using System.Text.Json;
using System.Text.RegularExpressions;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Machines.Core;

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

    /// <summary>A scale by <paramref name="factor"/> along one axis about the plane through
    /// <paramref name="anchor"/> normal to it; the other axes are unchanged.</summary>
    public static float[] Stretch(Axis axis, float factor, Float3 anchor)
    {
        var m = Identity();
        switch (axis)
        {
            case Axis.X:
                m[0] = factor; m[12] = anchor.X * (1 - factor);
                break;
            case Axis.Y:
                m[5] = factor; m[13] = anchor.Y * (1 - factor);
                break;
            default:
                m[10] = factor; m[14] = anchor.Z * (1 - factor);
                break;
        }
        return m;
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

public enum DriverType { Rotate, Slide, Swing, Feed, Step, Stretch, Gauge, Roll }

/// <summary>The input a driver runs on (rig.json's <c>input</c>): a rotate, slide or swing reads θ, ψ
/// (<c>"travel"</c>, also what <c>"rectified": true</c> means), φ (<c>"feed"</c>), W (<c>"work"</c>,
/// or <c>"trunk"</c>, its trunk-flavoured spelling) or the oil tank's fill (<c>"oil"</c>); a stretch
/// reads the depth (the default) or the oil.</summary>
public enum DriverInput { Theta, Travel, Feed, Work, Oil, Depth }

/// <summary>How a <see cref="DriverType.Step"/> driver treats the lifting input.</summary>
public enum LiftGate { None, Hold, Block, Trip }

/// <summary>
/// Everything a rig is posed by. The bucking mill uses the first four; the work inputs default to
/// none. θ the signed shaft angle; d the saw's depth (0..1); L lifting (eased 0..1);
/// ψ (<see cref="Travel"/>) the shaft's travel, the total angle it has turned through either way,
/// |θ| when null; W (<see cref="Work"/>) how far the machine's work has got, in its progress's unit
/// (a trunk's travel along a trunkPath, blocks; the gear cutter's teeth cut); k
/// (<see cref="Class"/>) the work's class, 0 none, 1 thin, 2 thick; p (<see cref="Presence"/>) its
/// eased presence, 0..1; φ (<see cref="Feed"/>) the feed's travel, radians; <see cref="Oil"/>
/// how full the machine's oil tank is, 0..1.
/// </summary>
public readonly record struct RigInput(double Theta, double Depth = 0, double Lifting = 0, double? Travel = null,
                                       double Work = 0, int Class = 0, double Presence = 0, double Feed = 0,
                                       double Oil = 0)
{
    /// <summary>ψ: <see cref="Travel"/>, or |θ| when it is null.</summary>
    public double Psi => Travel ?? Math.Abs(Theta);

    /// <summary>k when it is 1 or 2, else 0 (no trunk).</summary>
    public int K => Class is 1 or 2 ? Class : 0;
}

/// <summary>A stretch of the trunk path a <see cref="DriverType.Gauge"/> feels the trunk over, from
/// <see cref="From"/> to <see cref="To"/> (positions along the path, blocks), eased in and out over
/// <see cref="Ease"/> blocks. <see cref="Gains"/> is per class, [none, thin, thick].</summary>
public sealed record GaugeWindow(float From, float To, float Ease, IReadOnlyList<float> Gains)
{
    /// <summary>occ = clamp((nose − from) / ease, 0, 1) · clamp((to − tail) / ease, 0, 1).</summary>
    public double Occupancy(double nose, double tail) =>
        Math.Clamp((nose - From) / Ease, 0, 1) * Math.Clamp((To - tail) / Ease, 0, 1);
}

/// <summary>A gauge's four-lobe term: amplitude[k] · cos(ratio · ψ + phase), scaled by the gauge's
/// engagement. <see cref="Amplitudes"/> is per class, [none, thin, thick].</summary>
public sealed record GaugeLobes(float Ratio, float Phase, IReadOnlyList<float> Amplitudes);

/// <summary>
/// One motion of a rig part, as rig.json's <c>parts[].drivers</c> describes it, posed by a
/// <see cref="RigInput"/>. Distances are blocks, angles radians; rotations are right-handed about
/// the positive axis. The mod's README defines each type; the reference implementations are
/// <c>driver_matrix</c> in BuckingSawmill/tools/make_shape.py (the mill's types) and in
/// Machines/tools/machinegen (all of them).
/// </summary>
/// <param name="Pivot">rotate, swing, roll, a rotating step or gauge: the pivot; stretch: the anchor.</param>
/// <param name="Rotates">step, gauge: true for <c>"motion": "rotate"</c>, false for <c>"slide"</c>.</param>
/// <param name="Length">stretch: signed authored distance from the anchor to the free end.</param>
/// <param name="Input">rotate, slide, swing: the input in place of θ. <see cref="DriverInput.Travel"/>
/// (ψ, the shaft's travel) keeps turning the same way whichever way the shaft turns (a one-way
/// catch's gear); the mill writes it as <c>"rectified": true</c>.</param>
/// <param name="Amounts">gauge: the full motion per class, [none, thin, thick].</param>
/// <param name="Windows">gauge, <c>occupy</c> mode: where the trunk is felt.</param>
/// <param name="Present">gauge: <c>"mode": "present"</c>, engaged by presence alone.</param>
/// <param name="Lobes">gauge, rotating only: the four-lobe term, or null.</param>
/// <param name="At">roll: the roller's position along the path.</param>
public sealed record Driver(DriverType Type, Axis Axis, Float3 Pivot, float Ratio, float Amplitude, float Phase, float Travel,
                            bool Rotates = false, float Amount = 0, float From = 0, float To = 1, LiftGate Lifting = LiftGate.None,
                            float Length = 1, DriverInput Input = DriverInput.Theta, float Top = 0,
                            IReadOnlyList<float>? Amounts = null, IReadOnlyList<GaugeWindow>? Windows = null, bool Present = false,
                            GaugeLobes? Lobes = null, float At = 0)
{
    /// <summary>Runs on ψ, the shaft's travel (<c>"rectified": true</c>).</summary>
    public bool Rectified => Input == DriverInput.Travel;

    /// <summary>A step driver's fraction: d's progress through [From, To], then held at 1
    /// (<see cref="LiftGate.Hold"/>) or kept at 0 (<see cref="LiftGate.Block"/>) while lifting.
    /// <see cref="LiftGate.Trip"/>: thrown over [From, To] at the bottom on the way down and back
    /// over [0, Top] at the top on the way up, so e = (1 − L)·down + L·up with up =
    /// clamp(d / Top, 0, 1); the two agree at both ends, where the direction changes.</summary>
    public double StepFraction(double depth, double lifting)
    {
        double e = Math.Clamp((depth - From) / (To - From), 0, 1);
        return Lifting switch
        {
            LiftGate.Hold => Math.Max(e, lifting),
            LiftGate.Block => e * (1 - lifting),
            LiftGate.Trip => e * (1 - lifting) + (Top > 0 ? Math.Clamp(depth / Top, 0, 1) : 1) * lifting,
            _ => e,
        };
    }

    /// <summary>
    /// A gauge's engagement e and motion a. With no trunk (k 0) both are 0. <c>present</c>: e = p.
    /// <c>occupy</c>: e = p · max over windows of min(1, gain[k] · occ), occ from the trunk's nose
    /// and tail on <paramref name="path"/> (0 without one). a = amount[k] · e, plus
    /// e · amplitude[k] · cos(ratio · ψ + phase) with lobes.
    /// </summary>
    public double GaugeAmount(in RigInput input, IWorkProgress? path)
    {
        int k = input.K;
        if (k == 0)
            return 0;
        double e;
        if (Present)
            e = input.Presence;
        else
        {
            double best = 0;
            if (path != null && Windows != null)
            {
                double nose = path.Nose(input.Work), tail = path.Tail(input.Work, k);
                foreach (var w in Windows)
                    best = Math.Max(best, Math.Min(1, w.Gains[k] * w.Occupancy(nose, tail)));
            }
            e = input.Presence * best;
        }
        double a = (Amounts?[k] ?? 0) * e;
        if (Lobes != null)
            a += e * Lobes.Amplitudes[k] * Math.Cos(Lobes.Ratio * input.Psi + Lobes.Phase);
        return a;
    }

    /// <summary>The mill's pose: <see cref="Matrix(in RigInput, IWorkProgress?)"/> with no work.
    /// <paramref name="shaftTravel"/> is ψ; when null it is |θ|.</summary>
    public float[] Matrix(double theta, double depth, double lifting = 0, double? shaftTravel = null) =>
        Matrix(new RigInput(theta, depth, lifting, shaftTravel), null);

    /// <summary>rotate: angle ratio·x about pivot, x the driver's <see cref="Input"/>. slide: offset
    /// amplitude·sin(ratio·x + phase). swing: angle amplitude·sin(ratio·x + phase) about pivot.
    /// feed: offset travel·d. step: offset or angle amount·e (<see cref="StepFraction"/>).
    /// stretch: scale (length + travel·x) / length along the axis about the anchor, x the depth or the
    /// oil (its <see cref="Input"/>). gauge: offset or
    /// angle <see cref="GaugeAmount"/>. roll: with a trunk, angle ratio·clamp(nose − at, 0, L[k])
    /// about pivot (it turns only while the trunk passes over it, and ignores p); without, none.</summary>
    public float[] Matrix(in RigInput input, IWorkProgress? path)
    {
        switch (Type)
        {
            case DriverType.Rotate:
                return Mat4.Rotation(Axis, Ratio * InputValue(input), Pivot);
            case DriverType.Swing:
                return Mat4.Rotation(Axis, Amplitude * Math.Sin(Ratio * InputValue(input) + Phase), Pivot);
            case DriverType.Slide:
                return Along(Amplitude * Math.Sin(Ratio * InputValue(input) + Phase));
            case DriverType.Feed:
                return Along(Travel * input.Depth);
            case DriverType.Step:
                double e = StepFraction(input.Depth, input.Lifting);
                return Rotates ? Mat4.Rotation(Axis, Amount * e, Pivot) : Along(Amount * e);
            case DriverType.Gauge:
                double a = GaugeAmount(input, path);
                return Rotates ? Mat4.Rotation(Axis, a, Pivot) : Along(a);
            case DriverType.Roll:
                int k = input.K;
                if (k == 0 || path is not TrunkPath trunk)
                    return Mat4.Identity();
                return Mat4.Rotation(Axis, Ratio * Math.Clamp(trunk.Nose(input.Work) - At, 0, trunk.LengthOf(k)), Pivot);
            default:
                return Mat4.Stretch(Axis, (float)((Length + Travel * InputValue(input)) / Length), Pivot);
        }
    }

    private double InputValue(in RigInput input) => Input switch
    {
        DriverInput.Travel => input.Psi,
        DriverInput.Feed => input.Feed,
        DriverInput.Work => input.Work,
        DriverInput.Oil => input.Oil,
        DriverInput.Depth => input.Depth,
        _ => input.Theta,
    };

    private float[] Along(double offset)
    {
        float off = (float)offset;
        return Mat4.Translation(Axis == Axis.X ? off : 0, Axis == Axis.Y ? off : 0, Axis == Axis.Z ? off : 0);
    }
}

/// <summary>A moving part of a machine's model: the elements its <see cref="Match"/> globs claim,
/// drawn only when <see cref="Requires"/> is fitted (each machine has its own vocabulary, e.g.
/// <c>MillRequires</c>), moved by its drivers and then by its <see cref="Ride"/> part's whole
/// transform.</summary>
public sealed record RigPart(string Id, IReadOnlyList<string> Match, string? Requires, string? Ride, IReadOnlyList<Driver> Drivers);

/// <summary>Where a trunk lies: <see cref="Origin"/> is the centre of the bed's top surface.</summary>
public sealed record TrunkBed(Float3 Origin, Axis Axis, float Length);

/// <summary>The rig's parts in order, with the maths that poses them.</summary>
public sealed class RigParts
{
    public IReadOnlyList<RigPart> Parts { get; }
    /// <summary>The rig's progress (its work or trunkPath) the gauges and rolls read, or null (the mill has none).</summary>
    public IWorkProgress? Path { get; }
    private readonly Regex[][] _globs;
    private readonly int[] _order;   // parts sorted so a ride parent comes before its riders
    private readonly int[] _ride;    // index of each part's ride parent, or -1

    public RigParts(IReadOnlyList<RigPart> parts, IWorkProgress? path = null)
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
        Path = path;
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

    /// <summary>The mill's pose: <see cref="Matrices(in RigInput)"/> with no trunk. θ is the shaft
    /// angle, <paramref name="depth"/> the saw's depth (0..1), <paramref name="lifting"/> 1 while
    /// the saws are wound back up (eased 0..1), <paramref name="shaftTravel"/> ψ, the total angle
    /// the shaft has turned through either way (|θ| when null).</summary>
    public float[][] Matrices(double theta, double depth, double lifting = 0, double? shaftTravel = null) =>
        Matrices(new RigInput(theta, depth, lifting, shaftTravel));

    /// <summary>One native-frame matrix per part, in part order: the drivers applied in list
    /// order to the authored geometry (pivots in the authored frame), then the ride part's whole
    /// transform.</summary>
    public float[][] Matrices(in RigInput input)
    {
        var result = new float[Parts.Count][];
        foreach (int i in _order)
        {
            var m = Mat4.Identity();
            foreach (var d in Parts[i].Drivers)
                m = Mat4.Multiply(d.Matrix(input, Path), m);
            if (_ride[i] >= 0)
                m = Mat4.Multiply(result[_ride[i]], m);
            result[i] = m;
        }
        return result;
    }

    // ---- parsing (rig.json's "parts") ----

    /// <summary>Parses rig.json's <c>parts</c>. A part's <c>requires</c> must be one of
    /// <paramref name="knownRequires"/>, the machine's vocabulary. Rolls and <c>occupy</c> gauges
    /// need the rig's progress, <paramref name="path"/> (<see cref="RigProgress.Of"/>); rolls a trunkPath.</summary>
    public static RigParts Parse(JsonElement array, IReadOnlySet<string> knownRequires, IWorkProgress? path)
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
            if (requires != null && !knownRequires.Contains(requires))
                throw new FormatException($"part \"{id}\" requires \"{requires}\", which the gameplay does not know");
            var drivers = new List<Driver>();
            if (p.TryGetProperty("drivers", out var ds) && ds.ValueKind == JsonValueKind.Array)
                foreach (var d in ds.EnumerateArray())
                    drivers.Add(DriverOf(d, id, path));
            parts.Add(new RigPart(id, globs, requires, Str(p, "ride"), drivers));
        }
        return new RigParts(parts, path);
    }

    private static Driver DriverOf(JsonElement d, string part, IWorkProgress? path)
    {
        string where = $"part \"{part}\"";
        var type = Str(d, "type") switch
        {
            "rotate" => DriverType.Rotate,
            "slide" => DriverType.Slide,
            "swing" => DriverType.Swing,
            "feed" => DriverType.Feed,
            "step" => DriverType.Step,
            "stretch" => DriverType.Stretch,
            "gauge" => DriverType.Gauge,
            "roll" => DriverType.Roll,
            var t => throw new FormatException($"{where}: unknown driver type \"{t}\""),
        };
        var axis = Str(d, "axis") switch
        {
            "x" => Axis.X,
            "y" => Axis.Y,
            "z" => Axis.Z,
            var a => throw new FormatException($"{where}: driver axis \"{a}\" is not x, y or z"),
        };
        bool rotates = false;
        if (type is DriverType.Step or DriverType.Gauge)
            rotates = Str(d, "motion") switch
            {
                "rotate" => true,
                "slide" => false,
                var m => throw new FormatException($"{where}: a {Str(d, "type")} driver's motion \"{m}\" is not slide or rotate"),
            };
        var pivot = new Float3(0, 0, 0);
        if (type is DriverType.Rotate or DriverType.Swing or DriverType.Roll || rotates)
            pivot = Point(d, "pivot", where, Str(d, "type")!);
        else if (type == DriverType.Stretch)
            pivot = Point(d, "anchor", where, "stretch");
        float from = Num(d, "from", 0), to = Num(d, "to", 1);
        if (type == DriverType.Step && !(to > from))
            throw new FormatException($"{where}: a step driver's window needs to > from");
        float length = Num(d, "length", 0);
        if (type == DriverType.Stretch && length == 0)
            throw new FormatException($"{where}: a stretch driver needs a non-zero length");
        var gate = Str(d, "lifting") switch
        {
            null => LiftGate.None,
            "hold" => LiftGate.Hold,
            "block" => LiftGate.Block,
            "trip" => LiftGate.Trip,
            var g => throw new FormatException($"{where}: lifting \"{g}\" is not hold, block or trip"),
        };
        float top = Num(d, "top", 0);
        if (gate == LiftGate.Trip && !(top > 0))
            throw new FormatException($"{where}: a trip step needs a top above 0");
        var input = InputOf(d, type, where);

        IReadOnlyList<float>? amounts = null;
        List<GaugeWindow>? windows = null;
        bool present = false;
        GaugeLobes? lobes = null;
        float at = 0;
        if (type == DriverType.Gauge)
        {
            amounts = PerClass(d, "amount", where, null);
            present = Str(d, "mode") switch
            {
                null or "occupy" => false,
                "present" => true,
                var m => throw new FormatException($"{where}: a gauge's mode \"{m}\" is not occupy or present"),
            };
            if (d.TryGetProperty("windows", out var ws) && ws.ValueKind == JsonValueKind.Array)
            {
                windows = [];
                foreach (var w in ws.EnumerateArray())
                    windows.Add(WindowOf(w, where));
            }
            if (!present && (windows == null || windows.Count == 0))
                throw new FormatException($"{where}: an occupy gauge needs windows");
            if (!present && path == null)
                throw new FormatException($"{where}: an occupy gauge needs the rig's work or trunkPath");
            if (d.TryGetProperty("lobes", out var lb))
            {
                if (!rotates)
                    throw new FormatException($"{where}: lobes are for a rotating gauge, not a slide");
                if (lb.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"{where}: lobes must be an object");
                lobes = new GaugeLobes(Required(lb, "ratio", JsonValueKind.Number).GetSingle(), Num(lb, "phase", 0),
                                       PerClass(lb, "amplitude", where + " lobes", null));
            }
        }
        else if (type == DriverType.Roll)
        {
            if (!d.TryGetProperty("at", out var atJson) || atJson.ValueKind != JsonValueKind.Number)
                throw new FormatException($"{where}: a roll driver needs an at");
            at = atJson.GetSingle();
            if (!d.TryGetProperty("ratio", out var ratioJson) || ratioJson.ValueKind != JsonValueKind.Number)
                throw new FormatException($"{where}: a roll driver needs a ratio");
            if (path is not TrunkPath)
                throw new FormatException($"{where}: a roll driver needs the rig's trunkPath");
        }
        return new Driver(type, axis, pivot, Num(d, "ratio", 1), Num(d, "amplitude", 0), Num(d, "phase", 0), Num(d, "travel", 0),
                          rotates, Num(d, "amount", 0), from, to, gate, length, input, top,
                          amounts, windows, present, lobes, at);
    }

    /// <summary><c>input</c> (theta, travel, feed, trunk or oil), or <c>"rectified": true</c> for
    /// travel; on rotate, slide and swing only, and not both. A stretch reads <c>input</c> depth (the
    /// default) or oil.</summary>
    private static DriverInput InputOf(JsonElement d, DriverType type, string where)
    {
        bool hasInput = d.TryGetProperty("input", out var inputJson);
        if (type == DriverType.Stretch)
        {
            if (d.TryGetProperty("rectified", out _))
                throw new FormatException($"{where}: rectified is for rotate, slide and swing drivers");
            if (!hasInput)
                return DriverInput.Depth;
            return (inputJson.ValueKind == JsonValueKind.String ? inputJson.GetString() : null) switch
            {
                "depth" => DriverInput.Depth,
                "oil" => DriverInput.Oil,
                var i => throw new FormatException($"{where}: a stretch's input \"{i}\" is not depth or oil"),
            };
        }
        bool hasRectified = d.TryGetProperty("rectified", out _);
        if (hasInput && hasRectified)
            throw new FormatException($"{where}: a driver has both input and rectified");
        if (!hasInput && !Bool(d, "rectified"))
            return DriverInput.Theta;
        if (type is not (DriverType.Rotate or DriverType.Slide or DriverType.Swing))
            throw new FormatException($"{where}: input and rectified are for rotate, slide and swing drivers");
        if (!hasInput)
            return DriverInput.Travel;
        return (inputJson.ValueKind == JsonValueKind.String ? inputJson.GetString() : null) switch
        {
            "theta" => DriverInput.Theta,
            "travel" => DriverInput.Travel,
            "feed" => DriverInput.Feed,
            "work" or "trunk" => DriverInput.Work,
            "oil" => DriverInput.Oil,
            var i => throw new FormatException($"{where}: input \"{i}\" is not theta, travel, feed, work, trunk or oil"),

        };
    }

    private static GaugeWindow WindowOf(JsonElement w, string where)
    {
        if (w.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{where}: a gauge's windows must be objects");
        float From(string key) => w.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetSingle()
            : throw new FormatException($"{where}: a gauge window needs a {key}");
        float from = From("from"), to = From("to"), ease = From("ease");
        if (!(ease > 0))
            throw new FormatException($"{where}: a gauge window's ease must be above 0");
        if (!(to > from))
            throw new FormatException($"{where}: a gauge window needs to > from");
        return new GaugeWindow(from, to, ease, PerClass(w, "gain", where + " window", 1));
    }

    /// <summary>A <c>{"thin": a, "thick": b}</c> object as [0, a, b]. With a
    /// <paramref name="fallback"/> the object and each key are optional; without one both keys are
    /// required.</summary>
    private static float[] PerClass(JsonElement obj, string key, string where, float? fallback)
    {
        if (!obj.TryGetProperty(key, out var v))
            return fallback is float f ? [0, f, f] : throw new FormatException($"{where}: needs an {key} of {{\"thin\", \"thick\"}}");
        if (v.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{where}: {key} must be an object of {{\"thin\", \"thick\"}}");
        float One(string name) => v.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number ? n.GetSingle()
            : fallback is float f ? f
            : throw new FormatException($"{where}: {key} needs a {name}");
        return [0, One("thin"), One("thick")];
    }

    private static Float3 Point(JsonElement d, string key, string where, string type)
    {
        if (!d.TryGetProperty(key, out var pv) || pv.ValueKind != JsonValueKind.Array || pv.GetArrayLength() != 3)
            throw new FormatException($"{where}: a {type} driver needs a {key} of 3 numbers");
        var n = pv.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new Float3(n[0], n[1], n[2]);
    }
}

/// <summary>The client-side clock of the model's motion.</summary>
public static class MillMotion
{
    /// <summary>
    /// The native-frame shaft angle θ from the power ghost's <c>AngleRad</c>, for an input shaft
    /// along native <paramref name="shaftAxis"/> (x or z). A vanilla axle along the world's x or z
    /// axis draws itself turned by −AngleRad about that axis (its AxisSign is −1), and the machine's
    /// input shaft turns with it. The rig's rotations are right-handed about the native axis, so θ is
    /// −AngleRad when the native axis points along world +x or +z, else +AngleRad. Along x (the
    /// mill): native +x points along world +x (south), −x (north), −z (east) or +z (west). Along z:
    /// native +z points along world +z (south), −z (north), +x (east) or −x (west).
    /// </summary>
    public static double NativeShaftAngle(Side facing, double angleRad, Axis shaftAxis = Axis.X)
    {
        var unit = shaftAxis switch
        {
            Axis.X => new Int3(1, 0, 0),
            Axis.Z => new Int3(0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(shaftAxis), "an input shaft is horizontal"),
        };
        var world = Footprint.ToWorld(unit, facing);
        return world.X + world.Z > 0 ? -angleRad : angleRad;
    }

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

    /// <summary>
    /// Lays a trunk mesh on a through-feed machine's path: <paramref name="min"/>..<paramref name="max"/>
    /// is the mesh's bounding box as tessellated (blocks). The trunk's long horizontal axis is turned
    /// onto the path's axis and its box centred on the trunk's axis, halfway between its tail and its
    /// nose for class <paramref name="k"/> at travel <paramref name="travel"/>: (nose − L/2, H, z) on
    /// a path along x.
    /// </summary>
    public static float[] TrunkOnAxis(Float3 min, Float3 max, TrunkPath path, int k, double travel)
    {
        var centre = new Float3((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
        bool alongZ = max.Z - min.Z > max.X - min.X + 1e-4f;
        bool wantZ = path.Axis == Axis.Z;
        var m = Mat4.Translation(-centre.X, -centre.Y, -centre.Z);
        if (alongZ != wantZ)
            m = Mat4.Multiply(Mat4.Rotation(Axis.Y, Math.PI / 2, new Float3(0, 0, 0)), m);
        float mid = (float)(path.Nose(travel) - path.LengthOf(k) / 2.0);
        var o = path.Origin;
        return Mat4.Multiply(wantZ ? Mat4.Translation(o.X, o.Y, mid) : Mat4.Translation(mid, o.Y, o.Z), m);
    }
}
