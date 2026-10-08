using System.Text;
using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>
/// Reads the YAML mods in the pack write: ConfigKit's (and ConfigLib's) <c>key: scalar</c> lines
/// between comment lines, with nested mappings by indentation. A key's value keeps its span
/// (<see cref="ConfigNode"/>), so changing it rewrites that scalar and leaves every comment and
/// blank line alone. What this does not model, a sequence, a flow collection (<c>[..]</c>,
/// <c>{..}</c>), a block scalar (<c>|</c>, <c>&gt;</c>), an anchor or a tag, is an
/// <see cref="NodeKind.Opaque"/> value: compared by its text and never changed. Throws
/// <see cref="FormatException"/> on a line it cannot place.
/// </summary>
public static partial class SimpleYaml
{
    private readonly record struct Line(int Start, int End, int Indent, string Content)
    {
        public bool Significant => Content.Length > 0 && Content[0] != '#';
    }

    public static ConfigNode Parse(string text)
    {
        var lines = new List<Line>();
        int pos = 0;
        if (text.Length > 0 && text[0] == '﻿') pos = 1;
        while (pos <= text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            int end = nl < 0 ? text.Length : nl;
            int contentEnd = end > pos && text[end - 1] == '\r' ? end - 1 : end;
            int indent = 0;
            while (pos + indent < contentEnd && text[pos + indent] == ' ') indent++;
            lines.Add(new Line(pos, contentEnd, indent, text[(pos + indent)..contentEnd].TrimEnd()));
            if (nl < 0) break;
            pos = nl + 1;
        }
        int i = 0;
        int rootIndent = lines.FirstOrDefault(l => l.Significant).Indent;
        var props = Block(text, lines, ref i, rootIndent);
        for (; i < lines.Count; i++)
            if (lines[i].Significant)
                throw new FormatException($"YAML line {i + 1}: indented less than the first key");
        return new ConfigNode { Kind = NodeKind.Object, Start = 0, End = text.Length, Raw = text, Properties = props };
    }

    private static int NextSignificant(List<Line> lines, int i)
    {
        while (i < lines.Count && !lines[i].Significant) i++;
        return i;
    }

    private static List<ConfigProperty> Block(string text, List<Line> lines, ref int i, int indent)
    {
        var props = new List<ConfigProperty>();
        while (true)
        {
            i = NextSignificant(lines, i);
            if (i >= lines.Count) return props;
            var line = lines[i];
            if (line.Indent < indent) return props;
            if (line.Indent > indent)
                throw new FormatException($"YAML line {i + 1}: indented more than its mapping");
            if (line.Content.StartsWith("- ", StringComparison.Ordinal) || line.Content == "-")
                throw new FormatException($"YAML line {i + 1}: a sequence where a key was expected");

            int keyStart = line.Start + line.Indent;
            var (key, afterKey) = Key(text, keyStart, line.End, i);
            int keyEnd = afterKey;
            int v = afterKey + 1; // past ':'
            while (v < line.End && text[v] == ' ') v++;
            string rest = text[v..line.End].TrimEnd();
            i++;

            if (rest.Length == 0 || rest[0] == '#')
            {
                int next = NextSignificant(lines, i);
                if (next < lines.Count && lines[next].Indent > indent
                    && !lines[next].Content.StartsWith("- ", StringComparison.Ordinal) && lines[next].Content != "-")
                {
                    int childStart = lines[next].Start;
                    int child = next;
                    var childProps = Block(text, lines, ref child, lines[next].Indent);
                    int childEnd = lines[child - 1].End;
                    i = child;
                    props.Add(new ConfigProperty(key, keyStart, keyEnd, new ConfigNode
                    {
                        Kind = NodeKind.Object, Start = childStart, End = childEnd,
                        Raw = text[childStart..childEnd], Properties = childProps,
                    }));
                    continue;
                }
                // A sequence below the key (at its indentation or deeper), or nothing: kept as is.
                int end = SkipDeeper(lines, ref i, indent, sequenceAtIndent: true);
                int start = end < 0 ? v : lines[next].Start;
                end = end < 0 ? v : end;
                props.Add(new ConfigProperty(key, keyStart, keyEnd, Opaque(text, start, end)));
                continue;
            }

            if (rest[0] is '|' or '>')
            {
                int end = SkipDeeper(lines, ref i, indent, sequenceAtIndent: false);
                props.Add(new ConfigProperty(key, keyStart, keyEnd, Opaque(text, v, end < 0 ? v + rest.Length : end)));
                continue;
            }
            props.Add(new ConfigProperty(key, keyStart, keyEnd, Scalar(text, v, line.End, i)));
        }
    }

    /// <summary>Consumes the lines below a key that belong to it; returns the end of the last one,
    /// or -1 for none.</summary>
    private static int SkipDeeper(List<Line> lines, ref int i, int indent, bool sequenceAtIndent)
    {
        int end = -1;
        while (true)
        {
            int next = NextSignificant(lines, i);
            if (next >= lines.Count) return end;
            var l = lines[next];
            bool belongs = l.Indent > indent
                           || (sequenceAtIndent && l.Indent == indent && (l.Content.StartsWith("- ", StringComparison.Ordinal) || l.Content == "-"));
            if (!belongs) return end;
            end = l.End;
            i = next + 1;
        }
    }

    private static (string Key, int Colon) Key(string text, int start, int end, int lineNo)
    {
        if (text[start] is '"' or '\'')
        {
            int close = text.IndexOf(text[start], start + 1);
            if (close < 0 || close + 1 >= end || text[close + 1] != ':')
                throw new FormatException($"YAML line {lineNo + 1}: a quoted key without ':'");
            return (text[(start + 1)..close], close + 1);
        }
        for (int k = start; k < end; k++)
        {
            if (text[k] == ':' && (k + 1 == end || text[k + 1] == ' '))
                return (text[start..k].TrimEnd(), k);
            if (text[k] == '#' && k > start && text[k - 1] == ' ')
                break;
        }
        throw new FormatException($"YAML line {lineNo + 1}: no 'key:' here");
    }

    private static ConfigNode Opaque(string text, int start, int end) =>
        new() { Kind = NodeKind.Opaque, Start = start, End = end, Raw = text[start..end] };

    private static ConfigNode Scalar(string text, int start, int lineEnd, int lineNo)
    {
        char c = text[start];
        if (c is '"' or '\'')
        {
            var sb = new StringBuilder();
            int k = start + 1;
            while (true)
            {
                if (k >= lineEnd) throw new FormatException($"YAML line {lineNo}: unclosed quote");
                char ch = text[k];
                if (c == '\'' && ch == '\'' && k + 1 < lineEnd && text[k + 1] == '\'') { sb.Append('\''); k += 2; continue; }
                if (ch == c) { k++; break; }
                if (c == '"' && ch == '\\' && k + 1 < lineEnd)
                {
                    char e = text[k + 1];
                    sb.Append(e switch { 'n' => '\n', 't' => '\t', '0' => '\0', _ => e });
                    k += 2;
                    continue;
                }
                sb.Append(ch);
                k++;
            }
            return new ConfigNode { Kind = NodeKind.String, Start = start, End = k, Raw = text[start..k], Text = sb.ToString() };
        }
        // A plain scalar ends at a comment (" #") or the end of the line.
        int end = lineEnd;
        int hash = text.IndexOf(" #", start, lineEnd - start, StringComparison.Ordinal);
        if (hash >= 0) end = hash;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        string raw = text[start..end];
        if (c is '[' or '{' or '&' or '*' or '!' or '%' or '@' or '`')
            return Opaque(text, start, end);
        if (raw is "~" or "null" or "Null" or "NULL")
            return new ConfigNode { Kind = NodeKind.Null, Start = start, End = end, Raw = raw };
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
            return new ConfigNode { Kind = NodeKind.Bool, Start = start, End = end, Raw = raw, Bool = true };
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            return new ConfigNode { Kind = NodeKind.Bool, Start = start, End = end, Raw = raw, Bool = false };
        if (NumberPattern().IsMatch(raw))
            return new ConfigNode { Kind = NodeKind.Number, Start = start, End = end, Raw = raw, Number = ConfigNode.ParseNumber(raw) };
        return new ConfigNode { Kind = NodeKind.String, Start = start, End = end, Raw = raw, Text = raw };
    }

    [GeneratedRegex(@"^[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?$")]
    private static partial Regex NumberPattern();
}
