using SeraphHorizons.Mod.ConfigDefaults.Core;
using SeraphHorizons.Mod.PackCheck;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.ConfigDefaults;

/// <summary>
/// Settings follow the pack's defaults (switch <see cref="SeraphHorizonsConfig.FollowPackDefaults"/>,
/// docs/config-defaults.md). In <see cref="StartPre"/>, first of every mod system in the game
/// (<see cref="ExecuteOrder"/>), so before any mod reads its config in its own StartPre, Start or
/// later: works out the running pack version (the lock this build carries), finds that version's
/// defaults snapshot in this mod's assets (<see cref="SnapshotFolder"/>), and runs
/// <see cref="DefaultsRun"/> over ModConfig with this side's loaded mods. The server also writes a
/// fresh install's files that carry the pack's own values, and tells each admin who joins once
/// what it changed. Each side runs for its own config folder (in singleplayer the two share one,
/// and the second finds nothing left to do).
/// </summary>
public sealed class ConfigDefaultsSystem : ModSystem
{
    public const string StateFile = "seraphhorizons-configdefaults.json";

    /// <summary>Set to <c>capture</c> (by <c>packtool smoke --config-defaults</c>) to change and
    /// write nothing, so the server writes ModConfig exactly as its mods do: what the snapshot is
    /// made of. It also lists the files already in ModConfig when this system starts, which a fresh
    /// server's mods wrote before any StartPre (<see cref="CaptureReport"/>).</summary>
    public const string CaptureVariable = "SERAPH_CONFIG_DEFAULTS";

    /// <summary>In capture mode, written to the data folder (not ModConfig).</summary>
    public const string CaptureReport = "configdefaults-capture.json";

    /// <summary>Snapshots for versions this build does not carry, for tests and dev builds:
    /// <c>&lt;data folder&gt;/ConfigDefaults/&lt;version&gt;/</c>. The mod's own copy wins.</summary>
    public const string ExtraSnapshotFolder = "ConfigDefaults";

    private DefaultsState? _state;
    private string? _modConfig;

    /// <summary>What this side's run changed; empty when nothing did (tests read it).</summary>
    public IReadOnlyList<SettingChange> Changes { get; private set; } = [];

    /// <summary>Files this side's run wrote whole for a fresh install.</summary>
    public IReadOnlyList<string> Created { get; private set; } = [];

    // First of all mod systems: the game orders StartPre by this, and a mod reading its config
    // in its own StartPre must find it already brought up to date.
    public override double ExecuteOrder() => double.MinValue;

    /// <summary>Where this mod carries a pack version's snapshot.</summary>
    public string SnapshotFolder(string version) =>
        Path.Combine(ModFolder(), "assets", "seraphhorizons", "config", "configdefaults", version);

    /// <summary>The mod's files on disk: a zip's unpacked copy (the game unpacks every zip mod
    /// before any StartPre), or the folder of a folder mod. The asset manager has no mod assets
    /// yet in StartPre, so the snapshot is read from here.</summary>
    private string ModFolder() =>
        (Mod as Vintagestory.Common.ModContainer)?.FolderPath
        ?? (Directory.Exists(Mod.SourcePath) ? Mod.SourcePath : "");

    public override void StartPre(ICoreAPI api)
    {
        _modConfig = GamePaths.ModConfig;
        try
        {
            Run(api);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Config defaults: failed, so ModConfig is left as it is: {0}", e);
        }
    }

    private void Run(ICoreAPI api)
    {
        if (Environment.GetEnvironmentVariable(CaptureVariable) == "capture")
        {
            Capture(api);
            return;
        }
        if (!SwitchOn(api)) return;

        string version;
        try
        {
            version = PackCheckSystem.ReadEmbeddedLock().PackVersion;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Config defaults: could not read the pack's lock this build carries, so ModConfig is left as it is: {0}", e.Message);
            return;
        }
        var current = Snapshot(version, api);
        if (current == null)
        {
            api.Logger.Notification("[seraphhorizons] Config defaults: this build carries no defaults snapshot for pack {0} (a dev, next or local build, or another pack), so ModConfig is left as it is",
                version);
            return;
        }

        var files = new FolderFiles(_modConfig!);
        DefaultsState? state = null;
        var stateText = files.Read(StateFile);
        if (stateText != null)
        {
            try
            {
                state = DefaultsState.Parse(stateText);
            }
            catch (FormatException e)
            {
                api.Logger.Warning("[seraphhorizons] Config defaults: ModConfig/{0} is not readable ({1}); starting over from pack {2}, so nothing is changed this time",
                    StateFile, e.Message, version);
            }
        }

        var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in api.ModLoader.Mods)
            if (m?.Info?.ModID is { Length: > 0 } id)
                loaded.TryAdd(id, m.Info.Version ?? "");

        var result = DefaultsRun.Run(current, v => Snapshot(v, api), state, loaded,
            createMissing: api.Side == EnumAppSide.Server, files);
        _state = result.State;
        Changes = result.Changes;
        Created = result.Created;

        foreach (var m in result.Messages)
        {
            if (m.Warning) api.Logger.Warning("[seraphhorizons] Config defaults: {0}", m.Text);
            else api.Logger.Notification("[seraphhorizons] Config defaults: {0}", m.Text);
        }
        foreach (var c in result.Changes)
            api.Logger.Notification("[seraphhorizons] Config defaults: {0}", c.Describe());
        if (state == null)
            api.Logger.Notification("[seraphhorizons] Config defaults: first start with this; recorded pack {0}, changed nothing", version);
        else if (state.PackVersion != version || state.Files.Count > 0)
            api.Logger.Notification("[seraphhorizons] Config defaults: from pack {0} to {1}: {2} setting(s) changed{3}",
                state.PackVersion, version, result.Changes.Count,
                result.State.Files.Count > 0 ? $", {result.State.Files.Count} file(s) left behind" : "");
        try
        {
            files.Write(StateFile, result.State.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            api.Logger.Warning("[seraphhorizons] Config defaults: could not write ModConfig/{0}: {1}", StateFile, e.Message);
        }
    }

    /// <summary>The switch, read from the file itself: <see cref="SeraphHorizonsSystem"/> loads its
    /// config later, after this has brought it up to date. A file that is missing or unreadable
    /// counts as on, the default.</summary>
    private bool SwitchOn(ICoreAPI api)
    {
        try
        {
            var path = Path.Combine(_modConfig!, SeraphHorizonsSystem.ConfigFile);
            if (!File.Exists(path)) return true;
            var root = LenientJson.Parse(File.ReadAllText(path));
            if (root.Find(nameof(SeraphHorizonsConfig.FollowPackDefaults))?.Value is { Kind: NodeKind.Bool, Bool: false })
            {
                api.Logger.Notification("[seraphhorizons] Config defaults: off ({0} in ModConfig/{1}); ModConfig is left as it is",
                    nameof(SeraphHorizonsConfig.FollowPackDefaults), SeraphHorizonsSystem.ConfigFile);
                return false;
            }
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            // SeraphHorizonsSystem reports an unreadable file itself.
        }
        return true;
    }

    private DefaultsSnapshot? Snapshot(string version, ICoreAPI api)
    {
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.Contains("..")) return null;
        foreach (var dir in new[] { SnapshotFolder(version), Path.Combine(GamePaths.DataPath, ExtraSnapshotFolder, version) })
        {
            var index = Path.Combine(dir, "index.json");
            if (!File.Exists(index)) continue;
            try
            {
                var parsed = SnapshotIndex.Parse(File.ReadAllText(index));
                var filesDir = Path.Combine(dir, "files");
                return new DefaultsSnapshot(parsed, name =>
                {
                    var p = Path.Combine(filesDir, name);
                    return File.Exists(p) ? File.ReadAllText(p) : null;
                });
            }
            catch (Exception e) when (e is FormatException or IOException)
            {
                api.Logger.Warning("[seraphhorizons] Config defaults: the snapshot for pack {0} ({1}) is not readable: {2}", version, index, e.Message);
            }
        }
        return null;
    }

    private void Capture(ICoreAPI api)
    {
        var present = Directory.Exists(_modConfig)
            ? Directory.EnumerateFiles(_modConfig!, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_modConfig!, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList()
            : [];
        api.Logger.Notification("[seraphhorizons] Config defaults: capture mode ({0}=capture), nothing is changed; {1} file(s) already in ModConfig at the first StartPre{2}",
            CaptureVariable, present.Count, present.Count > 0 ? ": " + string.Join(", ", present) : "");
        if (api.Side != EnumAppSide.Server) return;
        try
        {
            File.WriteAllText(Path.Combine(GamePaths.DataPath, CaptureReport),
                System.Text.Json.JsonSerializer.Serialize(new { presentAtFirstStartPre = present }) + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            api.Logger.Warning("[seraphhorizons] Config defaults: could not write {0}: {1}", CaptureReport, e.Message);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (_state?.Notice is not { Changes.Count: > 0 }) return;
        api.Event.PlayerJoin += player =>
        {
            var notice = _state?.Notice;
            if (notice == null || !player.HasPrivilege(Privilege.controlserver)
                || notice.NotifiedPlayers.Contains(player.PlayerUID)) return;
            var lines = new List<string>
            {
                Lang.Get("seraphhorizons:configdefaults-chat-header", notice.FromVersion, notice.ToVersion, notice.Changes.Count),
            };
            lines.AddRange(notice.Changes.Select(c => "- " + c));
            lines.Add(Lang.Get("seraphhorizons:configdefaults-chat-footer"));
            player.SendMessage(GlobalConstants.GeneralChatGroup, string.Join("\n", lines), EnumChatType.Notification);
            notice.NotifiedPlayers.Add(player.PlayerUID);
            try
            {
                new FolderFiles(_modConfig!).Write(StateFile, _state!.ToJson());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                api.Logger.Warning("[seraphhorizons] Config defaults: could not write ModConfig/{0}: {1}", StateFile, e.Message);
            }
        };
    }

    /// <summary>ModConfig on disk.</summary>
    private sealed class FolderFiles(string root) : IConfigFiles
    {
        public string? Read(string name)
        {
            var p = Path.Combine(root, name);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        public void Write(string name, string text)
        {
            var p = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, text);
        }
    }
}
