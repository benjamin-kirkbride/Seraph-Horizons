using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// What an admin command under <c>/sh ore</c> and <c>/sh trade</c> answers (#458, #459): a one-line
/// summary, the detail below it, and with <c>--json</c> one JSON object instead of both
/// (docs/admin-tools.md). A command that knows its data attaches it (<see cref="Data"/>); every
/// other command still answers with the generic shape, its text split into summary and lines.
/// </summary>
public sealed class AdminOutput
{
    /// <summary>The flag that turns a command's answer into JSON.</summary>
    public const string JsonFlag = "--json";

    private static readonly JsonSerializerOptions Options = new()
    {
        // Item codes and VTML in summaries are kept readable rather than \u-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AdminOutput(string command, bool ok = true)
    {
        Command = command;
        Ok = ok;
    }

    /// <summary>The command's path without the slash and root, e.g. <c>ore list</c>.</summary>
    public string Command { get; }

    public bool Ok { get; set; }

    public string Summary { get; set; } = "";

    public List<string> Lines { get; } = [];

    /// <summary>Structured fields, merged into the JSON object next to the generic ones.</summary>
    public JsonObject Data { get; } = new();

    /// <summary>Splits a command's text answer: the first line is the summary, the rest the detail.</summary>
    public static AdminOutput FromText(string command, string? text, bool ok = true)
    {
        var output = new AdminOutput(command, ok);
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        output.Summary = lines[0];
        output.Lines.AddRange(lines.Skip(1).Where(l => l.Length > 0));
        return output;
    }

    public string ToText() => Lines.Count == 0 ? Summary : Summary + "\n" + string.Join("\n", Lines);

    /// <summary>
    /// <c>{"command", "ok", "summary", "lines", ...data}</c>. A data field named like one of the
    /// generic ones replaces it, except <c>command</c> and <c>ok</c>, which always say what ran and
    /// whether it worked.
    /// </summary>
    public JsonObject ToJsonObject()
    {
        var o = new JsonObject
        {
            ["command"] = Command,
            ["ok"] = Ok,
            ["summary"] = Summary,
            ["lines"] = new JsonArray(Lines.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()),
        };
        foreach (var (key, value) in Data)
        {
            if (key is "command" or "ok") continue;
            o[key] = value?.DeepClone();
        }
        return o;
    }

    public string ToJson() => ToJsonObject().ToJsonString(Options);

    /// <summary>Takes every <see cref="JsonFlag"/> out of a command's words; true if there was one.</summary>
    public static bool StripFlag(List<string> words)
    {
        int removed = words.RemoveAll(w => string.Equals(w, JsonFlag, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    /// <summary>A JSON array of rows made by <paramref name="row"/>.</summary>
    public static JsonArray Rows<T>(IEnumerable<T> items, Func<T, JsonObject> row) =>
        new(items.Select(i => (JsonNode?)row(i)).ToArray());
}

/// <summary>
/// Where the admin commands read and write files (<c>/sh ore survey</c>, <c>registry export</c>,
/// <c>/sh trade export</c>): only a plain file name is taken, inside one folder of the server's
/// data path, so a command can't write anywhere else on the server.
/// </summary>
public static class AdminFiles
{
    /// <summary>The folder under the server's data path.</summary>
    public const string Folder = "seraphhorizons-admin";

    /// <summary>The full path for <paramref name="name"/> in <paramref name="directory"/>, with
    /// <paramref name="extension"/> added when the name has none; null for a name that isn't a plain
    /// file name (a path, <c>..</c>, empty).</summary>
    public static string? Resolve(string directory, string? name, string extension = ".json")
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        if (!Path.HasExtension(name)) name += extension;
        return Path.Combine(directory, name);
    }
}
