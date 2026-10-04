using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.CreativeModTabs.Core;

/// <summary>Which tabs the creative inventory shows.</summary>
public enum TabsMode
{
    /// <summary>The game's own tabs, exactly as without the mod.</summary>
    Default,
    /// <summary>One tab per mod, next to the default tabs of the left column.</summary>
    Mod,
}

/// <summary>
/// The per-client choice, kept in <c>ModConfig/seraphhorizons-creativemodtabs.json</c>:
/// <c>{"Version": 1, "Mode": "Mod", "DefaultTab": "blocks", "ModTab": "seraphhorizons-modtab-game"}</c>.
/// The tabs are remembered by code, so a pack change that adds or removes tabs keeps the others.
/// </summary>
public sealed class ModTabsState
{
    public int Version { get; set; } = 1;
    public TabsMode Mode { get; set; } = TabsMode.Default;
    /// <summary>The tab last selected in default mode, or null.</summary>
    public string? DefaultTab { get; set; }
    /// <summary>The tab last selected in mod mode (a mod tab or one of the left column's default tabs), or null.</summary>
    public string? ModTab { get; set; }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Reads the file's text. Missing (null or blank) means the defaults with no error; unreadable JSON
    /// or a mode it doesn't know means the defaults and <paramref name="error"/> says why. Unknown
    /// properties are ignored.
    /// </summary>
    public static ModTabsState Parse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return new ModTabsState();
        try
        {
            var state = JsonSerializer.Deserialize<ModTabsState>(json, Options) ?? new ModTabsState();
            if (!Enum.IsDefined(state.Mode)) { error = $"unknown mode {(int)state.Mode}"; state.Mode = TabsMode.Default; }
            state.DefaultTab = Blank(state.DefaultTab);
            state.ModTab = Blank(state.ModTab);
            state.Version = 1;
            return state;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            error = ex.Message;
            return new ModTabsState();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
