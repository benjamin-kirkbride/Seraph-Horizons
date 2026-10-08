namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>One replacement in a config file's text.</summary>
public readonly record struct TextEdit(int Start, int End, string Text);

/// <summary>A setting's place in a file: the keys from the top, e.g.
/// <c>["TrunkEntitiesSettings", "CarrySpeedAtMaxLogs"]</c>.</summary>
public static class SettingPath
{
    public static string Show(IReadOnlyList<string> path) => string.Join(".", path);
}

/// <summary>Parsing a config file by its format, and changing values in its text in place.</summary>
public static class ConfigText
{
    public static ConfigFormat FormatOf(string fileName) =>
        fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            ? ConfigFormat.Yaml
            : ConfigFormat.Json;

    public static ConfigFormat? ParseFormat(string? name) => name?.ToLowerInvariant() switch
    {
        "json" => ConfigFormat.Json,
        "yaml" => ConfigFormat.Yaml,
        _ => null,
    };

    /// <summary>The file's values. Throws <see cref="FormatException"/> on text the format's reader
    /// cannot read.</summary>
    public static ConfigNode Parse(string text, ConfigFormat format) =>
        format == ConfigFormat.Yaml ? SimpleYaml.Parse(text) : LenientJson.Parse(text);

    /// <summary>The text with the edits made; edits must not overlap.</summary>
    public static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var sorted = edits.OrderByDescending(e => e.Start).ToList();
        for (int k = 1; k < sorted.Count; k++)
            if (sorted[k].End > sorted[k - 1].Start)
                throw new InvalidOperationException("overlapping edits");
        foreach (var e in sorted)
            text = text[..e.Start] + e.Text + text[e.End..];
        return text;
    }

    /// <summary>The text with the value at <paramref name="path"/> replaced by
    /// <paramref name="raw"/> (the value's own text: <c>1200</c>, <c>"name"</c>, <c>true</c>).
    /// Null when the file has no such setting, or it is one this reader keeps as it is.</summary>
    public static string? SetValue(string text, ConfigFormat format, IReadOnlyList<string> path, string raw)
    {
        var node = Parse(text, format).At(path);
        if (node == null || node.Kind == NodeKind.Opaque) return null;
        return Apply(text, [new TextEdit(node.Start, node.End, raw)]);
    }

    /// <summary>The text with the key at <paramref name="from"/> renamed to <paramref name="to"/>
    /// in the same object, when the object has <paramref name="from"/> and not <paramref name="to"/>;
    /// otherwise the text as it is.</summary>
    public static string Rename(string text, ConfigFormat format, IReadOnlyList<string> from, string to)
    {
        if (from.Count == 0) return text;
        var root = Parse(text, format);
        var parent = from.Count == 1 ? root : root.At(from.Take(from.Count - 1).ToList());
        var prop = parent?.Properties?.FirstOrDefault(p => p.Key == from[^1]);
        if (parent == null || prop == null || parent.Properties!.Any(p => p.Key == to)) return text;
        char first = text[prop.KeyStart];
        string key = first is '"' or '\'' ? $"{first}{Escape(to, first, format)}{first}" : to;
        if (format == ConfigFormat.Yaml && first is not ('"' or '\'') && (to.Contains(": ") || to.StartsWith('#')))
            key = $"\"{Escape(to, '"', format)}\"";
        return Apply(text, [new TextEdit(prop.KeyStart, prop.KeyEnd, key)]);
    }

    private static string Escape(string s, char quote, ConfigFormat format) =>
        quote == '\'' && format == ConfigFormat.Yaml
            ? s.Replace("'", "''")
            : s.Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote);
}
