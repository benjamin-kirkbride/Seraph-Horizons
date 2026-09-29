using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SeraphHorizons.IconExport.Core;

/// <summary>
/// One written icon. <see cref="CustomRenderer"/>: a mod draws this collectible's GUI icon itself
/// (RegisterItemstackRenderer), so the export's shader reset may not cover all of its draw.
/// </summary>
public sealed record IconEntry(string Code, IconKind Kind, int Size, ImageCheck Check, bool CustomRenderer = false);

public sealed record FailedEntry(string Code, IconKind? Kind, string Reason);

/// <summary>
/// manifest.json in the output directory: every written file with its code and kind, and the
/// codes that could not be rendered. tools/icons.py imports from it. Written sorted and
/// indented, so two runs that did the same thing give the same file.
/// </summary>
public sealed class Manifest
{
    public const int SchemaVersion = 1;
    public const string FileName = "manifest.json";

    public string Generator { get; set; } = "";
    public SortedDictionary<string, IconEntry> Icons { get; } = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, FailedEntry> _failed = new(StringComparer.Ordinal);

    public IEnumerable<FailedEntry> Failed => _failed.Values;

    public void AddIcon(string relativePath, IconEntry entry)
    {
        Icons[relativePath] = entry;
        _failed.Remove(FailedKey(entry.Code, entry.Kind));
        _failed.Remove(FailedKey(entry.Code, null));
    }

    public void AddFailure(FailedEntry entry) => _failed[FailedKey(entry.Code, entry.Kind)] = entry;

    private static string FailedKey(string code, IconKind? kind) =>
        code + "|" + (kind is { } k ? IconKinds.Name(k) : "");

    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        // The relaxed encoder keeps "&" and "<" readable; the file is never embedded in HTML.
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var w = new Utf8JsonWriter(stream, options))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteString("generator", Generator);
            w.WriteString("layout", "<domain>/<item|block>/<path>.png; bytes outside [a-z0-9_-] are %XX (UTF-8)");
            w.WriteStartObject("icons");
            foreach (var (path, e) in Icons)
            {
                w.WriteStartObject(path);
                w.WriteString("code", e.Code);
                w.WriteString("kind", IconKinds.Name(e.Kind));
                w.WriteNumber("size", e.Size);
                w.WriteString("check", ImageChecks.Name(e.Check));
                if (e.CustomRenderer)
                {
                    w.WriteBoolean("customRenderer", true);
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartArray("failed");
            foreach (FailedEntry f in _failed.Values)
            {
                w.WriteStartObject();
                w.WriteString("code", f.Code);
                if (f.Kind is { } k)
                {
                    w.WriteString("kind", IconKinds.Name(k));
                }
                else
                {
                    w.WriteNull("kind");
                }
                w.WriteString("reason", f.Reason);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    public static Manifest FromJson(byte[] json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out JsonElement v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != SchemaVersion)
        {
            throw new FormatException($"not a schemaVersion {SchemaVersion} icon manifest");
        }
        var m = new Manifest();
        if (root.TryGetProperty("generator", out JsonElement g) && g.ValueKind == JsonValueKind.String)
        {
            m.Generator = g.GetString()!;
        }
        if (root.TryGetProperty("icons", out JsonElement icons) && icons.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in icons.EnumerateObject())
            {
                JsonElement e = p.Value;
                string code = e.GetProperty("code").GetString() ?? throw new FormatException($"{p.Name}: no code");
                if (!IconKinds.TryParse(e.GetProperty("kind").GetString(), out IconKind kind))
                {
                    throw new FormatException($"{p.Name}: bad kind");
                }
                int size = e.TryGetProperty("size", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
                ImageCheck check = e.TryGetProperty("check", out JsonElement c) ? ImageChecks.Parse(c.GetString()) : ImageCheck.Unchecked;
                bool custom = e.TryGetProperty("customRenderer", out JsonElement cr) && cr.ValueKind == JsonValueKind.True;
                m.Icons[p.Name] = new IconEntry(code, kind, size, check, custom);
            }
        }
        if (root.TryGetProperty("failed", out JsonElement failed) && failed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement f in failed.EnumerateArray())
            {
                string code = f.GetProperty("code").GetString() ?? "";
                IconKind? kind = f.TryGetProperty("kind", out JsonElement k) && IconKinds.TryParse(k.ValueKind == JsonValueKind.String ? k.GetString() : null, out IconKind kk) ? kk : null;
                string reason = f.TryGetProperty("reason", out JsonElement r) ? r.GetString() ?? "" : "";
                m.AddFailure(new FailedEntry(code, kind, reason));
            }
        }
        return m;
    }

    /// <summary>Loads the manifest in <paramref name="dir"/>, or an empty one if there is none.</summary>
    public static Manifest LoadOrNew(string dir)
    {
        string path = Path.Combine(dir, FileName);
        return File.Exists(path) ? FromJson(File.ReadAllBytes(path)) : new Manifest();
    }

    /// <summary>Writes through a temporary file, so a crash never leaves half a manifest.</summary>
    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, FileName);
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Adds an entry for every icon file under <paramref name="dir"/> that the manifest lacks
    /// (a run that crashed before it saved). The path alone gives the code. Returns how many.
    /// </summary>
    public int AdoptFiles(string dir, int size)
    {
        if (!Directory.Exists(dir))
        {
            return 0;
        }
        int adopted = 0;
        foreach (string file in Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            if (Icons.ContainsKey(rel) || !IconPaths.TryParse(rel, out string code, out IconKind kind))
            {
                continue;
            }
            AddIcon(rel, new IconEntry(code, kind, size, ImageCheck.Unchecked));
            adopted++;
        }
        return adopted;
    }

    public override string ToString() => Encoding.UTF8.GetString(ToJson());
}
