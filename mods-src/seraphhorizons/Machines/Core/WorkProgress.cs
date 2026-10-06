using System.Text.Json;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A rig's progress: the quantity W its gauges' windows and rolls are placed on (the rig's
/// <c>work</c>, or its <c>trunkPath</c>, which is the trunk-flavoured case). A class k (0 none, 1 thin,
/// 2 thick) of work at W has a leading point, <see cref="Nose"/>, and a trailing one,
/// <see cref="Tail"/>, <see cref="LengthOf"/> behind it; W runs from 0 to <see cref="End"/>.
/// </summary>
public interface IWorkProgress
{
    /// <summary>The leading point's position at <paramref name="work"/>.</summary>
    double Nose(double work);

    /// <summary>The trailing point's position for class <paramref name="k"/>.</summary>
    double Tail(double work, int k);

    /// <summary>The work's length for class <paramref name="k"/>: a trunk's; 0 for a plain quantity.</summary>
    float LengthOf(int k);

    /// <summary>Where W ends for class <paramref name="k"/>.</summary>
    double End(int k);
}

/// <summary>
/// rig.json's <c>work</c>: a machine's progress as a named quantity in its own unit (the gear
/// cutter's teeth cut), with a slider step and an end per class. It is a point on its own scale:
/// nose and tail are both W, so a gauge window is occupied while W is in it.
/// </summary>
/// <param name="Ends">[none, thin, thick]: [0, end.thin, end.thick].</param>
public sealed record WorkQuantity(string? Name, string Unit, float Step, IReadOnlyList<float> Ends) : IWorkProgress
{
    public double Nose(double work) => work;

    public double Tail(double work, int k) => work;

    public float LengthOf(int k) => 0;

    public double End(int k) => k is 1 or 2 ? Ends[k] : 0;

    /// <summary>Parses rig.json's <c>work</c>: <c>unit</c> and <c>end</c> <c>{"thin", "thick"}</c> (both
    /// above 0) are required, <c>name</c> and <c>step</c> (default 1/16, above 0) optional; a trunkPath's
    /// keys are refused.</summary>
    public static WorkQuantity Parse(JsonElement work)
    {
        if (work.ValueKind != JsonValueKind.Object)
            throw new FormatException("\"work\" must be an object");
        string unit = Str(work, "unit") is { Length: > 0 } u ? u : throw new FormatException("work needs a unit");
        string? name = null;
        if (work.TryGetProperty("name", out var n))
            name = n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s
                ? s
                : throw new FormatException("work.name must be a non-empty string");
        float step = Num(work, "step", 1f / 16);
        if (!(step > 0))
            throw new FormatException("work.step must be above 0");
        var end = Required(work, "end", JsonValueKind.Object);
        float thin = Required(end, "thin", JsonValueKind.Number).GetSingle(), thick = Required(end, "thick", JsonValueKind.Number).GetSingle();
        if (!(thin > 0) || !(thick > 0))
            throw new FormatException("work.end must be above 0");
        foreach (var key in new[] { "nose0", "lengths", "tailStop" })
            if (work.TryGetProperty(key, out _))
                throw new FormatException($"work has no {key}: that is a trunkPath's");
        return new WorkQuantity(name, unit, step, [0, thin, thick]);
    }
}

/// <summary>Reading a rig's progress.</summary>
public static class RigProgress
{
    /// <summary>The rig's <c>work</c>, its <c>trunkPath</c>, or null; a rig with both is refused.</summary>
    public static IWorkProgress? Of(JsonElement rig)
    {
        bool work = rig.TryGetProperty("work", out var w), trunk = rig.TryGetProperty("trunkPath", out var t);
        if (work && trunk)
            throw new FormatException("a rig has work or a trunkPath, not both");
        return work ? WorkQuantity.Parse(w) : trunk ? TrunkPath.Parse(t) : null;
    }
}
