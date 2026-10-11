using System.Globalization;
using System.Text.Json;

namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A rig's classes of work, k: 0 is none; 1 thin and 2 thick are named (a trunk's two sizes, the
/// gear cutter's masters, a hollow's metal); 3 .. <see cref="Max"/> are keyed by their number (the
/// mandrel station's spring is 3). A per-class value in a rig (a gauge's <c>amount</c>, a window's
/// <c>gain</c>, lobes' <c>amplitude</c>, a work's <c>end</c>) is an object keyed by class
/// (<c>"thin"</c>, <c>"thick"</c>, <c>"3"</c> .. <c>"9"</c>; any other key is refused) that names one
/// class or more; a class it does not name has none of it (amount and amplitude 0, gain 1, end 0). A
/// class outside 1 .. <see cref="Max"/> is none. Machines/tools/machinegen/rigmath.py is the reference.
/// </summary>
public static class RigClasses
{
    /// <summary>The last class.</summary>
    public const int Max = 9;

    /// <summary>Whether <paramref name="k"/> is a class (1 .. <see cref="Max"/>); anything else is none.</summary>
    public static bool IsClass(int k) => k >= 1 && k <= Max;

    /// <summary>Class <paramref name="k"/>'s key: <c>thin</c>, <c>thick</c>, <c>3</c> .. <c>9</c>; null for none.</summary>
    public static string? Key(int k) => k switch
    {
        1 => "thin",
        2 => "thick",
        _ => IsClass(k) ? k.ToString(CultureInfo.InvariantCulture) : null,
    };

    /// <summary>The class a key names, or null for a key that names none (<c>"1"</c>, <c>"03"</c>, <c>"10"</c>, ...).</summary>
    public static int? Of(string key)
    {
        if (key == "thin")
            return 1;
        if (key == "thick")
            return 2;
        return int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int k) && k >= 3 && IsClass(k)
               && k.ToString(CultureInfo.InvariantCulture) == key ? k : null;
    }

    /// <summary>A value for every class alike, indexed by class ([0] unused, 0).</summary>
    public static float[] Uniform(float value)
    {
        var a = new float[Max + 1];
        for (int k = 1; k <= Max; k++)
            a[k] = value;
        return a;
    }

    /// <summary>A per-class object as an array indexed by class (0 .. <see cref="Max"/>; [0] is 0): each
    /// class it names, <paramref name="missing"/> for the rest. It must be an object keyed by class, each value
    /// a number, naming a class or more (none at all only when <paramref name="mayBeEmpty"/>).</summary>
    public static float[] Parse(JsonElement v, string what, float missing, bool mayBeEmpty = false)
    {
        if (v.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{what} must be an object keyed by class");
        var a = Uniform(missing);
        int named = 0;
        foreach (var p in v.EnumerateObject())
        {
            int k = Of(p.Name) ?? throw new FormatException($"{what}: \"{p.Name}\" is not a class (thin, thick, or 3 .. {Max})");
            if (p.Value.ValueKind != JsonValueKind.Number)
                throw new FormatException($"{what}.{p.Name} must be a number");
            a[k] = p.Value.GetSingle();
            named++;
        }
        if (named == 0 && !mayBeEmpty)
            throw new FormatException($"{what} names no class");
        return a;
    }
}
