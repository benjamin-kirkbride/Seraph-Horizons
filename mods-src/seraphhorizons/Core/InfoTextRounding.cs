using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Rounded numbers in the block and entity info HUD (<c>WailaRounding</c>): rewrites the decimal
/// numbers in the HUD's info text, the numbers on a health line to whole numbers and every other
/// number with more than one decimal to one decimal. Game-independent, so tests/ runs it without
/// the game.
///
/// The text is the game's rich text (VTML): only the text between tags is rewritten, never a tag's
/// attributes. A number is a run of digits with at most one decimal separator and an optional
/// exponent (<c>1.5E-05</c>, as <c>float.ToString()</c> writes a tiny value), standing on its own:
/// not right after a letter, digit or underscore (an identifier or a code), not inside a dotted run
/// of digits (a version <c>1.22.7</c>, an address), and not right after a dash that follows a letter
/// (a code such as <c>plank-1.25</c>). A unit may follow it (<c>0.75m</c>, <c>12.34°C</c>).
///
/// Elsewhere, an integer and a number with one decimal are left as they are; one with more decimals
/// or an exponent becomes one decimal (<c>0.0</c>), rounded half away from zero. On a health line,
/// every number becomes a whole number. A health line is a line whose text without its tags matches
/// one of the health templates: the game's format strings with <c>{0}</c>-style placeholders, so
/// the label is the localized one (<c>"Health: {0}/{1}"</c>, or its translation). A rounded zero
/// loses its minus sign.
/// </summary>
public sealed class InfoTextRounding
{
    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new(@"\{\d+(?:[,:][^}]*)?\}", RegexOptions.CultureInvariant);

    private readonly Regex _number;
    private readonly Regex[] _health;

    /// <param name="healthTemplates">Format strings of a health line (<c>{0}</c>-style placeholders).
    /// Blank ones and ones without a placeholder are ignored.</param>
    /// <param name="decimalSeparator">The decimal separator of the culture the text was formatted in.
    /// <c>.</c> is always one too.</param>
    public InfoTextRounding(IEnumerable<string?> healthTemplates, string? decimalSeparator = ".")
    {
        var seps = new HashSet<char> { '.' };
        if (decimalSeparator is { Length: 1 } sep && !char.IsLetterOrDigit(sep[0]) && !char.IsWhiteSpace(sep[0]))
            seps.Add(sep[0]);
        string s = "[" + Regex.Escape(string.Concat(seps.OrderBy(c => c))).Replace("]", @"\]") + "]";
        _number = new Regex(
            // A minus sign not after a word character, or the first digit not after a word
            // character, a dash after a letter (a code) or a digit and a separator (1.2.3).
            @"(?:(?<sign>(?<!\w)-)|(?<!\w)(?<![\p{L}_]-)(?<!\d" + s + @"))"
            + @"(?<int>\d+)(?:(?<sep>" + s + @")(?<frac>\d+))?(?<exp>[eE][-+]?\d+)?"
            // Not before another digit or a separator and a digit (1.2.3).
            + @"(?!\d)(?!" + s + @"\d)",
            RegexOptions.CultureInvariant);
        _health = healthTemplates
            .Where(t => !string.IsNullOrWhiteSpace(t) && Placeholder.IsMatch(t!))
            .Distinct(StringComparer.Ordinal)
            .Select(t => TemplatePattern(t!))
            .ToArray();
    }

    /// <summary>A template as a whole-line pattern: its literal text exact, each placeholder one
    /// value (no whitespace), surrounding whitespace allowed.</summary>
    public static Regex TemplatePattern(string template)
    {
        var pattern = new StringBuilder(@"^\s*");
        int at = 0;
        foreach (Match m in Placeholder.Matches(template))
        {
            pattern.Append(Regex.Escape(template[at..m.Index])).Append(@"(\S+?)");
            at = m.Index + m.Length;
        }
        pattern.Append(Regex.Escape(template[at..])).Append(@"\s*$");
        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant);
    }

    /// <summary>Whether a line (rich text) is a health line.</summary>
    public bool IsHealthLine(string line)
    {
        string plain = Tag.Replace(line, "");
        return _health.Any(h => h.IsMatch(plain));
    }

    /// <summary>The text with its numbers rounded; null and empty text as they are.</summary>
    public string? Round(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = RoundLine(lines[i], IsHealthLine(lines[i]));
        return string.Join('\n', lines);
    }

    /// <summary>One line, rounded to whole numbers or to one decimal; tags left as they are.</summary>
    public string RoundLine(string line, bool whole)
    {
        var sb = new StringBuilder(line.Length);
        int at = 0;
        foreach (Match tag in Tag.Matches(line))
        {
            sb.Append(RoundText(line[at..tag.Index], whole)).Append(tag.Value);
            at = tag.Index + tag.Length;
        }
        sb.Append(RoundText(line[at..], whole));
        return sb.ToString();
    }

    private string RoundText(string text, bool whole) =>
        text.Length == 0 ? text : _number.Replace(text, m => RoundNumber(m, whole));

    private static string RoundNumber(Match m, bool whole)
    {
        int decimals = m.Groups["frac"].Success ? m.Groups["frac"].Length : 0;
        bool exp = m.Groups["exp"].Success;
        if (!exp && decimals <= (whole ? 0 : 1))
            return m.Value;
        string sep = m.Groups["sep"].Success ? m.Groups["sep"].Value : ".";
        string invariant = m.Groups["int"].Value + (decimals > 0 ? "." + m.Groups["frac"].Value : "") + m.Groups["exp"].Value;
        decimal value;
        if (exp)
        {
            if (!double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                || double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 7.9e27)
                return m.Value;
            value = (decimal)d;
        }
        else if (!decimal.TryParse(invariant, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
            return m.Value;
        value = Math.Round(value, whole ? 0 : 1, MidpointRounding.AwayFromZero);
        string body = value.ToString(whole ? "0" : "0.0", CultureInfo.InvariantCulture).Replace(".", sep);
        bool negative = m.Groups["sign"].Success && value != 0m;
        return (negative ? "-" : "") + body;
    }
}
