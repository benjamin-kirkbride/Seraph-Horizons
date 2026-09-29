using System.Text;
using System.Text.Json;

namespace SeraphHorizons.IconExport.Core;

/// <summary>One code to render. <see cref="Kind"/> is null when the list does not say.</summary>
public sealed record ListEntry(string Code, IconKind? Kind);

public sealed record RejectedLine(int Line, string Text, string Reason);

public sealed class ListResult
{
    public List<ListEntry> Entries { get; } = new();
    public List<RejectedLine> Rejected { get; } = new();
    public int Duplicates { get; set; }
    /// <summary>"recipe export", "json array" or "text".</summary>
    public string Format { get; set; } = "";
}

/// <summary>
/// Reads the codes to render from either a recipe export (the keys of its "items" object, with
/// each item's "kind" when present), a JSON array of code strings, or plain text with one code
/// per line. In text, "#" starts a comment and blank lines are ignored. A code listed twice is
/// kept once, at its first position.
/// </summary>
public static class ListFile
{
    public static ListResult Parse(byte[] content)
    {
        int start = 0;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            start = 3;
        }
        while (start < content.Length && content[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }
        var body = new ReadOnlySpan<byte>(content, start, content.Length - start);
        if (body.Length > 0 && body[0] == (byte)'{')
        {
            return ParseExport(body);
        }
        if (body.Length > 0 && body[0] == (byte)'[')
        {
            return ParseArray(body);
        }
        return ParseText(Encoding.UTF8.GetString(body));
    }

    public static ListResult ParseText(string text)
    {
        var result = new ListResult { Format = "text" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r');
            int hash = raw.IndexOf('#');
            string code = (hash >= 0 ? raw[..hash] : raw).Trim();
            if (code.Length == 0)
            {
                continue;
            }
            Add(result, seen, code, null, i + 1);
        }
        return result;
    }

    private static ListResult ParseArray(ReadOnlySpan<byte> json)
    {
        var result = new ListResult { Format = "json array" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        reader.Read();
        int index = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            index++;
            if (reader.TokenType != JsonTokenType.String)
            {
                result.Rejected.Add(new RejectedLine(index, reader.TokenType.ToString(), "not a string"));
                reader.Skip();
                continue;
            }
            Add(result, seen, reader.GetString()!, null, index);
        }
        return result;
    }

    // The recipe export can be ~100 MB; a forward-only reader keeps only the keys.
    private static ListResult ParseExport(ReadOnlySpan<byte> json)
    {
        var result = new ListResult { Format = "recipe export" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        reader.Read();
        bool foundItems = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name != "items" || reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }
            foundItems = true;
            int index = 0;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                index++;
                string code = reader.GetString()!;
                reader.Read();
                IconKind? kind = null;
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    // Every value but "kind" is skipped whole, so only the item's own
                    // properties are ever read here, never a nested "kind".
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName)
                        {
                            string prop = reader.GetString()!;
                            reader.Read();
                            if (prop == "kind" && reader.TokenType == JsonTokenType.String
                                && IconKinds.TryParse(reader.GetString(), out IconKind k))
                            {
                                kind = k;
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
                Add(result, seen, code, kind, index);
            }
        }
        if (!foundItems)
        {
            throw new FormatException("a JSON object without an \"items\" object: not a recipe export");
        }
        return result;
    }

    private static void Add(ListResult result, HashSet<string> seen, string code, IconKind? kind, int line)
    {
        if (!IconCode.TrySplit(code, out _, out _, out string error))
        {
            result.Rejected.Add(new RejectedLine(line, code, error));
            return;
        }
        if (!seen.Add(code))
        {
            result.Duplicates++;
            return;
        }
        result.Entries.Add(new ListEntry(code, kind));
    }
}
