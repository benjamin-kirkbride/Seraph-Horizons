using System.Text;

namespace SeraphHorizons.IconExport.Core;

public enum IconKind
{
    Item,
    Block,
}

public static class IconKinds
{
    public static string Name(IconKind kind) => kind == IconKind.Block ? "block" : "item";

    public static bool TryParse(string? s, out IconKind kind)
    {
        switch (s)
        {
            case "item":
                kind = IconKind.Item;
                return true;
            case "block":
                kind = IconKind.Block;
                return true;
            default:
                kind = IconKind.Item;
                return false;
        }
    }
}

/// <summary>
/// Item codes as the recipe export and icons/index.json write them: "domain:path". The rule
/// matches CODE in tools/icons.py, so every code the mod writes can be imported.
/// </summary>
public static class IconCode
{
    public static bool TrySplit(string? code, out string domain, out string path, out string error)
    {
        domain = path = "";
        if (string.IsNullOrEmpty(code))
        {
            error = "empty code";
            return false;
        }
        int colon = code.IndexOf(':');
        if (colon < 0)
        {
            error = "no domain (expected domain:path, for example game:stick)";
            return false;
        }
        domain = code[..colon];
        path = code[(colon + 1)..];
        if (domain.Length == 0 || path.Length == 0)
        {
            error = "empty domain or path";
            return false;
        }
        foreach (char c in domain)
        {
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'))
            {
                error = $"domain has '{c}' (only a-z, 0-9, _ and - are allowed)";
                return false;
            }
        }
        foreach (char c in path)
        {
            if (c == ':' || char.IsWhiteSpace(c) || char.IsControl(c))
            {
                error = c == ':' ? "more than one ':'" : "whitespace or a control character in the path";
                return false;
            }
        }
        error = "";
        return true;
    }

    public static bool IsValid(string? code) => TrySplit(code, out _, out _, out _);
}

/// <summary>
/// Where an icon is written, relative to the output directory:
/// <c>&lt;domain&gt;/&lt;item|block&gt;/&lt;path&gt;.png</c>. Each "/" in the code's path is a
/// subdirectory. Every other byte outside [a-z0-9_-] is percent-encoded (UTF-8, upper-case hex),
/// "." included, so a name can never be "..", never end in a dot and never clash with ".png".
/// Upper-case letters are encoded too, so case-insensitive file systems cannot merge two codes.
/// The mapping is one to one: <see cref="TryParse"/> accepts only the exact string that
/// <see cref="ToRelativePath"/> produces.
/// </summary>
public static class IconPaths
{
    // Names Windows reserves for devices, with or without an extension.
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "con", "prn", "aux", "nul",
        "com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    public static string ToRelativePath(string code, IconKind kind)
    {
        if (!IconCode.TrySplit(code, out string domain, out string path, out string error))
        {
            throw new ArgumentException($"{code}: {error}", nameof(code));
        }
        string[] segments = path.Split('/');
        // An empty segment ("a//b", a leading or trailing "/") cannot be a directory name, so
        // such a path stays one file name with its slashes encoded.
        string encoded = segments.Any(s => s.Length == 0)
            ? EncodeSegment(path)
            : string.Join('/', segments.Select(EncodeSegment));
        return $"{EncodeSegment(domain)}/{IconKinds.Name(kind)}/{encoded}.png";
    }

    /// <summary>The code and kind a relative path stands for; "\" is read as "/".</summary>
    public static bool TryParse(string relativePath, out string code, out IconKind kind)
    {
        code = "";
        kind = IconKind.Item;
        string rel = relativePath.Replace('\\', '/');
        string[] parts = rel.Split('/');
        if (parts.Length < 3 || !IconKinds.TryParse(parts[1], out kind) || !rel.EndsWith(".png", StringComparison.Ordinal))
        {
            return false;
        }
        string rest = string.Join('/', parts, 2, parts.Length - 2);
        rest = rest[..^".png".Length];
        if (!TryDecode(parts[0], out string domain) || !TryDecode(rest, out string path))
        {
            return false;
        }
        string candidate = domain + ":" + path;
        if (!IconCode.IsValid(candidate) || ToRelativePath(candidate, kind) != rel)
        {
            return false;
        }
        code = candidate;
        return true;
    }

    private static string EncodeSegment(string segment)
    {
        var sb = new StringBuilder(segment.Length);
        foreach (byte b in Encoding.UTF8.GetBytes(segment))
        {
            if (b is >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'-')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2"));
            }
        }
        string s = sb.ToString();
        if (Reserved.Contains(s))
        {
            // Encoding the first letter keeps the name readable and no longer reserved.
            s = "%" + ((byte)s[0]).ToString("X2") + s[1..];
        }
        return s;
    }

    /// <summary>Percent-decodes; "/" is kept as it is. Fails on malformed escapes or UTF-8.</summary>
    private static bool TryDecode(string s, out string decoded)
    {
        decoded = "";
        var bytes = new List<byte>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '%')
            {
                if (i + 2 >= s.Length || !IsHex(s[i + 1]) || !IsHex(s[i + 2]))
                {
                    return false;
                }
                bytes.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (c > 0x7F)
            {
                return false;
            }
            else
            {
                bytes.Add((byte)c);
            }
        }
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
}
