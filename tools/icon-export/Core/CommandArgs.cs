using System.Text;

namespace SeraphHorizons.IconExport.Core;

public enum ExportMode
{
    One,
    Hand,
    List,
    All,
}

/// <summary>A parsed <c>.seraphicons one|hand|list|all ...</c> command.</summary>
public sealed class ExportRequest
{
    public const int DefaultSize = 128;
    public const int MinSize = 16;
    public const int MaxSize = 1024;

    public ExportMode Mode { get; init; }
    /// <summary>The code for <c>one</c>, the file for <c>list</c>, the domain filter for <c>all</c>.</summary>
    public string? Argument { get; init; }
    public int Size { get; init; } = DefaultSize;
    public bool Force { get; init; }
    public string? OutDir { get; init; }
    /// <summary>
    /// Debug: which GUI shader uniforms to reset before each draw. Null means all of them (the
    /// normal case); "none" resets nothing; a uniform name resets only that one.
    /// </summary>
    public string? Reset { get; init; }

    public const string Usage =
        ".seraphicons one <code> [size] | hand [size] | list <file> [size] | all [size] [domain] | stop | status | probe"
        + "  (options: --force, --out <dir>, --reset <uniform|all|none>)";

    public static bool TryParse(string sub, string? rest, out ExportRequest request, out string error)
    {
        request = new ExportRequest();
        if (!Tokenize(rest ?? "", out List<string> tokens, out error))
        {
            return false;
        }
        var positional = new List<string>();
        bool force = false;
        string? outDir = null, reset = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i];
            switch (t)
            {
                case "--force":
                    force = true;
                    break;
                case "--out":
                case "--reset":
                    if (i + 1 >= tokens.Count)
                    {
                        error = $"{t} needs a value";
                        return false;
                    }
                    if (t == "--out")
                    {
                        outDir = tokens[++i];
                    }
                    else
                    {
                        reset = tokens[++i];
                    }
                    break;
                default:
                    if (t.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"unknown option {t}";
                        return false;
                    }
                    positional.Add(t);
                    break;
            }
        }
        if (reset == "all")
        {
            reset = null;
        }

        ExportMode mode;
        string? argument = null;
        int sizeAt;
        int maxPositional;
        switch (sub)
        {
            case "one":
                mode = ExportMode.One;
                if (positional.Count < 1)
                {
                    error = "one needs an item code, for example: .seraphicons one game:pickaxe-copper";
                    return false;
                }
                argument = positional[0];
                if (!IconCode.TrySplit(argument, out _, out _, out string codeError))
                {
                    error = $"{argument}: {codeError}";
                    return false;
                }
                sizeAt = 1;
                maxPositional = 2;
                break;
            case "hand":
                mode = ExportMode.Hand;
                sizeAt = 0;
                maxPositional = 1;
                break;
            case "list":
                mode = ExportMode.List;
                if (positional.Count < 1)
                {
                    error = "list needs a file: a recipe export (recipes.json) or a text file with one code per line";
                    return false;
                }
                argument = positional[0];
                sizeAt = 1;
                maxPositional = 2;
                break;
            case "all":
                mode = ExportMode.All;
                sizeAt = 0;
                maxPositional = 2;
                break;
            default:
                error = $"unknown subcommand {sub}";
                return false;
        }
        if (positional.Count > maxPositional)
        {
            error = $"too many arguments: {string.Join(' ', positional.Skip(maxPositional))}";
            return false;
        }
        int size = DefaultSize;
        if (positional.Count > sizeAt)
        {
            if (!int.TryParse(positional[sizeAt], out size) || size < MinSize || size > MaxSize)
            {
                error = $"size must be a whole number from {MinSize} to {MaxSize}, not {positional[sizeAt]}";
                return false;
            }
        }
        if (mode == ExportMode.All && positional.Count > 1)
        {
            argument = positional[1];
        }
        request = new ExportRequest { Mode = mode, Argument = argument, Size = size, Force = force, OutDir = outDir, Reset = reset };
        error = "";
        return true;
    }

    /// <summary>Splits on whitespace; double quotes group words (for paths with spaces).</summary>
    public static bool Tokenize(string s, out List<string> tokens, out string error)
    {
        tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, any = false;
        foreach (char c in s)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (inQuotes)
        {
            error = "unclosed quote";
            return false;
        }
        if (any)
        {
            tokens.Add(current.ToString());
        }
        error = "";
        return true;
    }
}
