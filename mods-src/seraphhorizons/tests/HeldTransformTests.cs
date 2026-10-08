using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace SeraphHorizons.Tests;

/// <summary>
/// Every pack block and item drawn with one of the pack's own shapes sits where it should when held,
/// dropped and shown in a slot, whatever the size of the shape and wherever its cells lie about the
/// block it is placed from (the machines' shapes span their whole footprint, the handcar's its
/// length).
///
/// How the game applies the transforms (1.22.7): held, in first and third person alike
/// (<c>EntityShapeRenderer.RenderItem</c>; <c>fpHandTransform</c> is no longer read), a point
/// <c>p</c> of the mesh lands at <c>origin + scale * (translation + R * (p - origin))</c> in the hand's
/// frame, the hand's attachment point being at its own (0, 0, 0). The origin is not moved to the
/// hand: it stays where it is, in blocks, so an origin at the middle of a 16-block rosser held the
/// rosser 8 blocks from the player. Vanilla puts the model's middle on the hand (its default item
/// transform lands it 0.05 blocks off), so the translation has to bring the origin back:
/// <c>translation = (hand point - origin) / scale</c>. Dropped (<c>EntityItemRenderer</c>) and in a
/// slot (<c>InventoryItemRenderer</c>) the origin is the point placed on the item's position or the
/// slot's middle, so there the origin only needs to be the middle of the mesh (dropped, at a fifth of
/// the scale).
/// </summary>
public class HeldTransformTests
{
    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    private static string AssetsDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "assets", "seraphhorizons"));

    /// <summary>How far, in blocks, the middle of a held model may be from the hand (vanilla's
    /// two-handed crate is 0.23 off, its default item transform 0.05).</summary>
    private const double HeldReach = 0.5;

    private static IEnumerable<string> PackShapedFiles() =>
        from dir in new[] { "blocktypes", "itemtypes" }
        from file in Directory.GetFiles(Path.Combine(AssetsDir(), dir), "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
        where ItemShape(Parse(file)) is { } shape && shape.Base.StartsWith("seraphhorizons:", StringComparison.Ordinal)
        select Path.GetRelativePath(AssetsDir(), file).Replace('\\', '/');

    public static TheoryData<string> PackShapedTypes() => new(PackShapedFiles());

    [Fact]
    public void The_machines_and_the_handcar_are_covered()
    {
        var covered = PackShapedFiles().ToHashSet();
        foreach (var f in new[] { "blocktypes/rosser/frame.json", "blocktypes/buckingmill/frame.json", "blocktypes/gearcutter/frame.json",
                     "blocktypes/drawbench/frame.json", "blocktypes/mandrelstation/frame.json", "blocktypes/pressbrake/frame.json", "blocktypes/squaringshear/frame.json", "itemtypes/handcar.json" })
            Assert.Contains(f, covered);
    }

    [Theory]
    [MemberData(nameof(PackShapedTypes))]
    public void Held_dropped_and_in_a_slot_the_model_is_centred(string file)
    {
        var type = Parse(Path.Combine(AssetsDir(), file));
        bool isBlock = file.StartsWith("blocktypes/", StringComparison.Ordinal);
        var shape = ItemShape(type)!.Value;
        // A variant placeholder in the shape's path takes the group's first state.
        foreach (var g in (type["variantgroups"] as JArray ?? []).OfType<JObject>())
            if (g["states"] is JArray states && states.Count > 0)
                shape = shape with { Base = shape.Base.Replace("{" + (string)g["code"]! + "}", (string)states[0]!) };
        var (lo, hi) = Bounds(shape.Base, shape.RotateY);
        var mid = Mid(lo, hi);

        if (type["tpHandTransform"] is JObject tp)
        {
            // Omitted translation falls back to the game's default for blocks or items.
            var fallback = isBlock ? new Vec(-2.1, -1.8, -1.5) : new Vec(-1.5, -1.6, -1.4);
            var (origin, translation, scale) = Read(tp, fallback);
            // Where the model's middle lands, as far off as any rotation could put it.
            double off = (origin + translation * scale).Length + scale * (mid - origin).Length;
            Assert.True(off <= HeldReach,
                $"{file}: held, the middle of the model is up to {off:F2} blocks from the hand (mesh middle {mid}, origin {origin}, " +
                $"translation {translation}, scale {scale}); with the origin at the mesh middle, the translation should be about {(new Vec(0, 0.14, 0.18) - mid) * (1 / scale)}");
        }
        if (type["groundTransform"] is JObject ground)
        {
            // The dropped item renderer draws at a fifth of the transform's scale.
            var (origin, _, scale) = Read(ground, new Vec(0, 0, 0));
            double off = 0.2 * scale * Math.Sqrt(Sq(mid.X - origin.X) + Sq(mid.Z - origin.Z));
            Assert.True(off <= 0.25, $"{file}: dropped, the model is {off:F2} blocks off its position (mesh middle {mid}, origin {origin})");
        }
        if (type["guiTransform"] is JObject gui)
        {
            var (origin, _, scale) = Read(gui, new Vec(0, 0, 0));
            double off = scale * (mid - origin).Length;
            Assert.True(off <= 0.15, $"{file}: in a slot, the model is {off:F2} blocks (of a slot's 1) off its middle (mesh middle {mid}, origin {origin})");
        }
    }

    // ---- Assets ----------------------------------------------------------------------------

    private static JObject Parse(string path) => (JObject)JToken.Parse(File.ReadAllText(path), Lenient);

    private readonly record struct ShapeRef(string Base, double RotateY);

    /// <summary>The shape the item form is drawn with: <c>shape</c>, or the <c>-north</c> entry of
    /// <c>shapeByType</c> (the machines' item form), or its first.</summary>
    private static ShapeRef? ItemShape(JObject type)
    {
        JToken? s = type["shapeInventory"] ?? type["shape"];
        if (s is null && (type["shapeByType"] ?? type["shapebytype"]) is JObject byType)
            s = byType.Properties().FirstOrDefault(p => p.Name.EndsWith("-north", StringComparison.Ordinal))?.Value ?? byType.Properties().FirstOrDefault()?.Value;
        if (s?["base"] is not JValue b) return null;
        return new ShapeRef((string)b!, (double?)s["rotateY"] ?? 0);
    }

    private static (Vec Origin, Vec Translation, double Scale) Read(JObject t, Vec fallbackTranslation) =>
        (V(t["origin"]) ?? new Vec(0.5, 0.5, 0.5), V(t["translation"]) ?? fallbackTranslation, (double?)t["scale"] ?? 1);

    private static Vec? V(JToken? t) => t is JObject o ? new Vec((double?)o["x"] ?? 0, (double?)o["y"] ?? 0, (double?)o["z"] ?? 0) : null;

    /// <summary>The mesh's bounds in blocks, with the elements' rotations and nesting, turned about
    /// the block's middle as the composite shape's <c>rotateY</c> turns it.</summary>
    private static (Vec Lo, Vec Hi) Bounds(string shapeCode, double rotateY)
    {
        var path = shapeCode["seraphhorizons:".Length..];
        var shape = Parse(Path.Combine(AssetsDir(), "shapes", path + ".json"));
        var pts = new List<Vec>();
        Walk((JArray)shape["elements"]!, M.Identity, pts);
        var turn = M.Translate(0.5, 0, 0.5) * M.RotateY(rotateY) * M.Translate(-0.5, 0, -0.5);
        pts = pts.Select(p => turn * (p * (1 / 16.0))).ToList();
        return (new Vec(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)), new Vec(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)));
    }

    private static void Walk(JArray elements, M parent, List<Vec> pts)
    {
        foreach (var e in elements.Cast<JObject>())
        {
            var o = Arr(e["rotationOrigin"]) ?? [0, 0, 0];
            var m = parent * M.Translate(o[0], o[1], o[2])
                * M.RotateX((double?)e["rotationX"] ?? 0) * M.RotateY((double?)e["rotationY"] ?? 0) * M.RotateZ((double?)e["rotationZ"] ?? 0)
                * M.Translate(-o[0], -o[1], -o[2]);
            var f = Arr(e["from"])!;
            var to = Arr(e["to"])!;
            foreach (var x in new[] { f[0], to[0] })
                foreach (var y in new[] { f[1], to[1] })
                    foreach (var z in new[] { f[2], to[2] })
                        pts.Add(m * new Vec(x, y, z));
            if (e["children"] is JArray children)
                Walk(children, m * M.Translate(f[0], f[1], f[2]), pts);
        }
    }

    private static double[]? Arr(JToken? t) => t is JArray a ? a.Select(v => (double)v).ToArray() : null;

    private static Vec Mid(Vec lo, Vec hi) => (lo + hi) * 0.5;

    private static double Sq(double v) => v * v;

    private readonly record struct Vec(double X, double Y, double Z)
    {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }

    /// <summary>A 4x4 affine matrix acting on column vectors.</summary>
    private sealed class M(double[,] a)
    {
        private readonly double[,] _a = a;

        public static M Identity => new(new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } });

        public static M Translate(double x, double y, double z) => new(new double[,] { { 1, 0, 0, x }, { 0, 1, 0, y }, { 0, 0, 1, z }, { 0, 0, 0, 1 } });

        public static M RotateX(double deg)
        {
            double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
            return new(new double[,] { { 1, 0, 0, 0 }, { 0, c, -s, 0 }, { 0, s, c, 0 }, { 0, 0, 0, 1 } });
        }

        public static M RotateY(double deg)
        {
            double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
            return new(new double[,] { { c, 0, s, 0 }, { 0, 1, 0, 0 }, { -s, 0, c, 0 }, { 0, 0, 0, 1 } });
        }

        public static M RotateZ(double deg)
        {
            double c = Math.Cos(deg * Math.PI / 180), s = Math.Sin(deg * Math.PI / 180);
            return new(new double[,] { { c, -s, 0, 0 }, { s, c, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } });
        }

        public static M operator *(M l, M r)
        {
            var o = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    for (int k = 0; k < 4; k++)
                        o[i, j] += l._a[i, k] * r._a[k, j];
            return new(o);
        }

        public static Vec operator *(M m, Vec p) => new(
            m._a[0, 0] * p.X + m._a[0, 1] * p.Y + m._a[0, 2] * p.Z + m._a[0, 3],
            m._a[1, 0] * p.X + m._a[1, 1] * p.Y + m._a[1, 2] * p.Z + m._a[1, 3],
            m._a[2, 0] * p.X + m._a[2, 1] * p.Y + m._a[2, 2] * p.Z + m._a[2, 3]);
    }
}
