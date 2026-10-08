using System.Text.Json;

namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>A value the pack sets instead of a mod's own default: in <paramref name="File"/>, the
/// setting at <paramref name="Path"/> is <paramref name="Value"/> (the value's text as the file
/// writes it).</summary>
public sealed record PackValue(string File, IReadOnlyList<string> Path, string Value);

/// <summary>A setting a mod renamed: in <paramref name="File"/>, the key at <paramref name="From"/>
/// is now <paramref name="To"/>, in the same object.</summary>
public sealed record KeyRename(string File, IReadOnlyList<string> From, string To);

/// <summary>A file of the snapshot: its format and the mods whose versions decide what it holds.</summary>
public sealed record SnapshotFile(string Name, ConfigFormat Format, IReadOnlyList<string> Owners);

/// <summary>
/// A pack version's <c>index.json</c> (written by <c>tools/configdefaults.py</c>): the mods and
/// versions that wrote the snapshot, the files (each under <c>files/</c> next to the index, as a
/// fresh server writes it), the values the pack sets in them, and the renames.
/// </summary>
public sealed class SnapshotIndex
{
    public const int FormatVersion = 1;

    public required string PackVersion { get; init; }
    public required string GameVersion { get; init; }
    public required IReadOnlyDictionary<string, string> Mods { get; init; }
    public required IReadOnlyDictionary<string, SnapshotFile> Files { get; init; }
    public required IReadOnlyList<PackValue> PackValues { get; init; }
    public required IReadOnlyList<KeyRename> Renames { get; init; }

    /// <summary>Throws <see cref="FormatException"/> on anything but an index this code reads.</summary>
    public static SnapshotIndex Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int format = root.GetProperty("format").GetInt32();
            if (format != FormatVersion)
                throw new FormatException($"index format {format}, this build reads {FormatVersion}");
            var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in root.GetProperty("mods").EnumerateObject())
                mods[m.Name] = m.Value.GetString() ?? "";
            var files = new Dictionary<string, SnapshotFile>(StringComparer.Ordinal);
            foreach (var f in root.GetProperty("files").EnumerateObject())
            {
                var fmt = ConfigText.ParseFormat(f.Value.GetProperty("format").GetString())
                          ?? throw new FormatException($"{f.Name}: unknown format");
                var owners = f.Value.GetProperty("owners").EnumerateArray().Select(o => o.GetString() ?? "").ToList();
                files[f.Name] = new SnapshotFile(f.Name, fmt, owners);
            }
            var values = root.TryGetProperty("packValues", out var pv)
                ? pv.EnumerateArray().Select(v => new PackValue(
                    v.GetProperty("file").GetString()!,
                    v.GetProperty("path").EnumerateArray().Select(k => k.GetString()!).ToList(),
                    v.GetProperty("value").GetString()!)).ToList()
                : [];
            var renames = root.TryGetProperty("renames", out var rn)
                ? rn.EnumerateArray().Select(v => new KeyRename(
                    v.GetProperty("file").GetString()!,
                    v.GetProperty("from").EnumerateArray().Select(k => k.GetString()!).ToList(),
                    v.GetProperty("to").GetString()!)).ToList()
                : [];
            return new SnapshotIndex
            {
                PackVersion = root.GetProperty("packVersion").GetString()!,
                GameVersion = root.GetProperty("gameVersion").GetString()!,
                Mods = mods, Files = files, PackValues = values, Renames = renames,
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or NullReferenceException)
        {
            throw new FormatException($"not a config defaults index: {e.Message}", e);
        }
    }
}

/// <summary>
/// One pack version's defaults: its index and its files, read through <paramref name="readFile"/>
/// (the file's name under <c>files/</c> to its text, or null).
/// </summary>
public sealed class DefaultsSnapshot(SnapshotIndex index, Func<string, string?> readFile)
{
    private readonly Dictionary<string, string?> _effective = new(StringComparer.Ordinal);

    public SnapshotIndex Index => index;
    public string Version => index.PackVersion;

    /// <summary>The settings the pack itself sets in <paramref name="file"/>, by <see cref="SettingPath.Show"/>.</summary>
    public IReadOnlySet<string> PackSet(string file) =>
        index.PackValues.Where(v => v.File == file).Select(v => SettingPath.Show(v.Path)).ToHashSet();

    public bool HasPackValues(string file) => index.PackValues.Any(v => v.File == file);

    /// <summary>
    /// The file as a fresh install of this pack version has it: as the mods write it, with the
    /// pack's own values set. Null when the snapshot lacks the file; a pack value whose setting is
    /// not in the file is reported to <paramref name="problems"/> and left out.
    /// </summary>
    public string? Effective(string file, ICollection<string>? problems = null)
    {
        if (_effective.TryGetValue(file, out var cached)) return cached;
        if (!index.Files.TryGetValue(file, out var meta)) return _effective[file] = null;
        var text = readFile(file);
        if (text != null)
        {
            foreach (var v in index.PackValues.Where(v => v.File == file))
            {
                var set = ConfigText.SetValue(text, meta.Format, v.Path, v.Value);
                if (set == null)
                    problems?.Add($"{file}: the pack sets {SettingPath.Show(v.Path)}, which the file does not have");
                else
                    text = set;
            }
        }
        return _effective[file] = text;
    }
}
