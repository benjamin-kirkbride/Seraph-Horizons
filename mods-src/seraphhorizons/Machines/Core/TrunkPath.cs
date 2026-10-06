using System.Text.Json;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// The line a trunk travels along through a through-feed machine (rig.json's <c>trunkPath</c>, the trunk-flavoured
/// case of a rig's progress, <see cref="IWorkProgress"/>: W is the trunk's travel), in
/// native-frame blocks. Positions along the path (<see cref="Nose0"/>, <see cref="TailStop"/>, the
/// stations, a gauge's windows, a roll's <c>at</c>) are coordinates on <see cref="Axis"/>; the
/// trunk's axis is the line through <see cref="Origin"/> along it. A trunk of class k (0 none,
/// 1 thin, 2 thick) is <see cref="Lengths"/>[k] long; at travel T its nose is at
/// <see cref="Nose0"/> + T and its tail <see cref="Lengths"/>[k] behind.
/// </summary>
/// <param name="Origin">A point on the trunk's axis; the line anchor starts here.</param>
/// <param name="Length">The line anchor's length along <see cref="Axis"/>.</param>
/// <param name="Lengths">The shown trunk's length per class, [none, thin, thick]: [0, 4, 5].</param>
/// <param name="TailStop">Where the tail is when the trip ends.</param>
/// <param name="Radii">The shown trunk's [flats, corners] half-widths per class, [none, thin, thick];
/// for drawing.</param>
public sealed record TrunkPath(Float3 Origin, Axis Axis, float Length, float Nose0, IReadOnlyList<float> Lengths, float TailStop,
                               IReadOnlyDictionary<string, float> Stations, IReadOnlyList<(float Flats, float Corners)> Radii) : IWorkProgress
{
    /// <summary>The shown trunk's length for class <paramref name="k"/>; 0 for none or an unknown class.</summary>
    public float LengthOf(int k) => k is 1 or 2 ? Lengths[k] : 0;

    /// <summary>The nose's position along the path at travel <paramref name="travel"/>.</summary>
    public double Nose(double travel) => Nose0 + travel;

    /// <summary>The tail's position along the path for class <paramref name="k"/>.</summary>
    public double Tail(double travel, int k) => Nose(travel) - LengthOf(k);

    /// <summary>T_end(k): the travel at which the tail reaches <see cref="TailStop"/>.</summary>
    public double End(int k) => TailStop + LengthOf(k) - Nose0;

    /// <summary>Parses rig.json's <c>trunkPath</c>: <c>origin</c>, <c>axis</c> (x or z),
    /// <c>nose0</c>, <c>lengths</c> <c>{"thin", "thick"}</c> (both above 0) and <c>tailStop</c> are
    /// required; <c>length</c>, <c>stations</c> (name to position) and <c>radius</c>
    /// (<c>{"thin": [flats, corners], "thick": [...]}</c>) are optional.</summary>
    public static TrunkPath Parse(JsonElement path)
    {
        if (path.ValueKind != JsonValueKind.Object)
            throw new FormatException("\"trunkPath\" must be an object");
        var axis = AxisOf(Str(path, "axis"), "trunkPath.axis");
        if (axis == Axis.Y)
            throw new FormatException("trunkPath.axis must be x or z");
        var lengths = Required(path, "lengths", JsonValueKind.Object);
        float thin = Required(lengths, "thin", JsonValueKind.Number).GetSingle(), thick = Required(lengths, "thick", JsonValueKind.Number).GetSingle();
        if (!(thin > 0) || !(thick > 0))
            throw new FormatException("trunkPath.lengths must be above 0");
        var stations = new Dictionary<string, float>();
        if (path.TryGetProperty("stations", out var st))
        {
            if (st.ValueKind != JsonValueKind.Object)
                throw new FormatException("trunkPath.stations must be an object");
            foreach (var s in st.EnumerateObject())
                stations[s.Name] = s.Value.ValueKind == JsonValueKind.Number
                    ? s.Value.GetSingle()
                    : throw new FormatException($"trunkPath.stations.{s.Name} must be a number");
        }
        var radii = new (float, float)[3];
        if (path.TryGetProperty("radius", out var r))
        {
            if (r.ValueKind != JsonValueKind.Object)
                throw new FormatException("trunkPath.radius must be an object");
            for (int k = 1; k <= 2; k++)
            {
                string name = k == 1 ? "thin" : "thick";
                if (r.TryGetProperty(name, out var pair))
                {
                    var n = Numbers(pair, 2, $"trunkPath.radius.{name}");
                    radii[k] = ((float)n[0], (float)n[1]);
                }
            }
        }
        return new TrunkPath(
            Float3Of(Required(path, "origin", JsonValueKind.Array), "trunkPath.origin"),
            axis,
            Num(path, "length", 0),
            Required(path, "nose0", JsonValueKind.Number).GetSingle(),
            [0, thin, thick],
            Required(path, "tailStop", JsonValueKind.Number).GetSingle(),
            stations,
            radii);
    }
}
