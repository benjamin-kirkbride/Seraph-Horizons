using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>
/// The only per-install state: <c>ModConfig/seraphhorizons-configdefaults.json</c>. Which pack
/// version's defaults this install's config files were last brought to, a file left behind at an
/// older one (because the mod that writes it was not the pack's version), and the notice for
/// admins after a change.
/// </summary>
public sealed class DefaultsState
{
    /// <summary>The pack version whose defaults the files were last brought to.</summary>
    public string PackVersion { get; set; } = "";

    /// <summary>A file still at an older pack version's defaults than <see cref="PackVersion"/>
    /// (file name to that version). Usually empty.</summary>
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What the last run changed, for admins who have not seen it in chat yet.</summary>
    public DefaultsNotice? Notice { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Throws <see cref="FormatException"/> on text that is not a state file.</summary>
    public static DefaultsState Parse(string json)
    {
        try
        {
            var state = JsonSerializer.Deserialize<DefaultsState>(json, Options) ?? throw new FormatException("empty");
            state.Files = new Dictionary<string, string>(state.Files ?? [], StringComparer.Ordinal);
            if (string.IsNullOrEmpty(state.PackVersion)) throw new FormatException("no PackVersion");
            return state;
        }
        catch (JsonException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options) + "\n";
}

/// <summary>The settings a run moved, shown once in chat to each admin who joins.</summary>
public sealed class DefaultsNotice
{
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public List<string> Changes { get; set; } = [];
    /// <summary>Player UIDs who have had the message.</summary>
    public List<string> NotifiedPlayers { get; set; } = [];
}
