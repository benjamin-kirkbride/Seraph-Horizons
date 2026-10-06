using System.Text.Json.Nodes;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// One system's share of an admin state export (<c>/sh trade export</c>, #459): supply, standing,
/// the deposit registry, and later orders, deliveries and visitors. <see cref="Export"/> gives the
/// system's whole state; <see cref="Import"/> replaces it with one an export gave (it throws on data
/// it can't read, and should then leave the state as it was).
/// </summary>
public interface IAdminState
{
    /// <summary>The section's name in the export, e.g. <c>supply</c>.</summary>
    string Section { get; }

    JsonNode Export();

    void Import(JsonNode data);
}

/// <summary>What happened to one section on import.</summary>
public enum ImportOutcome { Imported, Failed, NotInFile, UnknownSection }

/// <summary>
/// The admin state sections, by name, and the export file around them:
/// <c>{"format": "seraphhorizons-admin-state", "version": 1, "sections": {name: data}}</c>. Each system
/// registers its own section (<see cref="Register"/>); the export holds every registered one, and an
/// import replaces those the file has, leaving the others alone.
/// </summary>
public sealed class AdminStateBook
{
    public const string Format = "seraphhorizons-admin-state";
    public const int Version = 1;

    private readonly SortedDictionary<string, IAdminState> _sections = new(StringComparer.Ordinal);

    public IEnumerable<string> Sections => _sections.Keys;

    /// <summary>Adds a section, replacing one of the same name (a system started twice).</summary>
    public void Register(IAdminState state) => _sections[state.Section] = state;

    public bool Unregister(string section) => _sections.Remove(section);

    /// <summary>Every section (or those named), with the sections that failed to export and why.</summary>
    public (JsonObject File, Dictionary<string, string> Errors) Export(IEnumerable<string>? only = null, Func<string>? stamp = null)
    {
        var wanted = only?.ToHashSet(StringComparer.Ordinal);
        var sections = new JsonObject();
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, state) in _sections)
        {
            if (wanted != null && !wanted.Contains(name)) continue;
            try
            {
                sections[name] = state.Export();
            }
            catch (Exception e)
            {
                errors[name] = e.Message;
            }
        }
        var file = new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
        };
        if (stamp != null) file["exported"] = stamp();
        file["sections"] = sections;
        return (file, errors);
    }

    /// <summary>Imports what the file holds, section by section; a section that fails leaves the
    /// others imported. Throws <see cref="FormatException"/> for a file that isn't an export.</summary>
    public Dictionary<string, (ImportOutcome Outcome, string? Error)> Import(JsonNode? file, IEnumerable<string>? only = null)
    {
        if (file is not JsonObject root || root["format"]?.GetValue<string>() != Format)
            throw new FormatException($"not a {Format} file");
        if (root["sections"] is not JsonObject sections)
            throw new FormatException("the file has no sections");
        var wanted = only?.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, (ImportOutcome, string?)>(StringComparer.Ordinal);
        foreach (var (name, data) in sections)
        {
            if (wanted != null && !wanted.Contains(name)) continue;
            if (!_sections.TryGetValue(name, out var state))
            {
                result[name] = (ImportOutcome.UnknownSection, null);
                continue;
            }
            try
            {
                state.Import(data ?? new JsonObject());
                result[name] = (ImportOutcome.Imported, null);
            }
            catch (Exception e)
            {
                result[name] = (ImportOutcome.Failed, e.Message);
            }
        }
        foreach (var name in _sections.Keys)
            if (!result.ContainsKey(name) && (wanted == null || wanted.Contains(name)))
                result[name] = (ImportOutcome.NotInFile, null);
        return result;
    }
}

/// <summary>A section made of two delegates, for systems whose state is already a JSON string.</summary>
public sealed class DelegateAdminState(string section, Func<JsonNode> export, Action<JsonNode> import) : IAdminState
{
    public string Section { get; } = section;

    public JsonNode Export() => export();

    public void Import(JsonNode data) => import(data);
}
