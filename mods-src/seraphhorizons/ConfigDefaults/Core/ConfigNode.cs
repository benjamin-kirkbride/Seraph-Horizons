using System.Globalization;

namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>The formats mods in the pack write their settings in (docs/config-defaults.md).</summary>
public enum ConfigFormat
{
    /// <summary>JSON as the game reads it: Newtonsoft's, which also takes comments, unquoted keys,
    /// single quotes and trailing commas (Yang's Transport Tycoon writes those).</summary>
    Json,

    /// <summary>The YAML ConfigKit writes for a content mod's declared settings: <c>key: scalar</c>
    /// lines between comments. Nested mappings are read; sequences, flow collections and block
    /// scalars are kept as they are and never changed.</summary>
    Yaml,
}

public enum NodeKind { Object, Array, String, Number, Bool, Null, Opaque }

/// <summary>A value in a config file, with where its text is: <see cref="Start"/> to
/// <see cref="End"/> (exclusive) in the text it was parsed from. Changing a setting replaces that
/// span and nothing else, so comments and layout stay as they were.</summary>
public sealed class ConfigNode
{
    public required NodeKind Kind { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }
    /// <summary>The value's own text.</summary>
    public required string Raw { get; init; }
    public List<ConfigProperty>? Properties { get; init; }
    public List<ConfigNode>? Items { get; init; }
    public string? Text { get; init; }
    public double Number { get; init; }
    public bool Bool { get; init; }

    /// <summary>The property of an object by key: exactly, or else ignoring case (Newtonsoft reads
    /// keys without case). Null for a missing key or a node that is not an object.</summary>
    public ConfigProperty? Find(string key)
    {
        if (Properties == null) return null;
        return Properties.FirstOrDefault(p => p.Key == key)
               ?? Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The node at <paramref name="path"/> below this one, or null.</summary>
    public ConfigNode? At(IReadOnlyList<string> path)
    {
        var node = this;
        foreach (var key in path)
        {
            node = node.Find(key)?.Value;
            if (node == null) return null;
        }
        return node;
    }

    /// <summary>Whether two values are the same setting: numbers by value (<c>0.10</c> is
    /// <c>0.1</c>), strings exactly, arrays item by item, objects key by key in any order; an
    /// opaque value by its text.</summary>
    public static bool Same(ConfigNode a, ConfigNode b)
    {
        if (a.Kind != b.Kind) return false;
        switch (a.Kind)
        {
            case NodeKind.Number: return a.Number.Equals(b.Number);
            case NodeKind.String: return a.Text == b.Text;
            case NodeKind.Bool: return a.Bool == b.Bool;
            case NodeKind.Null: return true;
            case NodeKind.Opaque: return a.Raw.Trim() == b.Raw.Trim();
            case NodeKind.Array:
                if (a.Items!.Count != b.Items!.Count) return false;
                for (int i = 0; i < a.Items.Count; i++)
                    if (!Same(a.Items[i], b.Items[i])) return false;
                return true;
            case NodeKind.Object:
                if (a.Properties!.Count != b.Properties!.Count) return false;
                foreach (var p in a.Properties)
                {
                    var q = b.Properties.FirstOrDefault(x => x.Key == p.Key);
                    if (q == null || !Same(p.Value, q.Value)) return false;
                }
                return true;
            default: return false;
        }
    }

    internal static double ParseNumber(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>A key of an object, with where the key's own text is (quotes included), for a rename.</summary>
public sealed record ConfigProperty(string Key, int KeyStart, int KeyEnd, ConfigNode Value);
