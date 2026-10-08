using System.Globalization;
using System.Text;

namespace SeraphHorizons.Mod.NanMotion.Core;

/// <summary>Number formatting for the NaN motion report (#405): invariant, fixed precision, and
/// every non-finite value flagged so it stands out in a long log.</summary>
public static class NanFormat
{
    /// <summary>The marker after a vector or value that holds a NaN or an infinity.</summary>
    public const string Flag = " <-- NON-FINITE";

    public static string Num(double v) =>
        double.IsFinite(v) ? v.ToString("0.000000", CultureInfo.InvariantCulture) : NonFinite(v);

    public static string Num(float v) => Num((double)v);

    /// <summary>A vector, flagged when any component is not finite.</summary>
    public static string Vec(double x, double y, double z) =>
        $"({Num(x)}, {Num(y)}, {Num(z)})" + (NanTracker.IsFinite(x, y, z) ? "" : Flag);

    /// <summary>A single value, flagged when it is not finite.</summary>
    public static string Value(double v) => Num(v) + (double.IsFinite(v) ? "" : Flag);

    private static string NonFinite(double v) =>
        double.IsNaN(v) ? "NaN" : v > 0 ? "+Infinity" : "-Infinity";
}

/// <summary>
/// Builds the NaN motion report (#405) section by section. Each section's body runs in its own
/// try/catch: one that throws gets a line saying so and the report goes on, so no single failure
/// (a mod's entity in an odd state, a reflection lookup that the game changed) keeps the rest from
/// being written.
/// </summary>
public sealed class ReportWriter
{
    private readonly StringBuilder _sb = new();

    /// <summary>How many sections failed.</summary>
    public int Failures { get; private set; }

    public ReportWriter Line(string text)
    {
        _sb.Append(text).Append('\n');
        return this;
    }

    public ReportWriter Section(string title, Action<StringBuilder> body)
    {
        _sb.Append("== ").Append(title).Append(" ==\n");
        int mark = _sb.Length;
        try
        {
            body(_sb);
        }
        catch (Exception e)
        {
            Failures++;
            if (_sb.Length > mark && _sb[^1] != '\n')
                _sb.Append('\n');
            _sb.Append("  (this section failed: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append(")\n");
        }
        if (_sb.Length > 0 && _sb[^1] != '\n')
            _sb.Append('\n');
        _sb.Append('\n');
        return this;
    }

    public override string ToString() => _sb.ToString();
}
