using SeraphHorizons.Mod.PackCheck.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.PackCheck;

/// <summary>
/// The pack version check (switch <see cref="SeraphHorizonsConfig.PackVersionCheck"/>): each side
/// compares its own loaded mods and game version with the pack this build was released with,
/// <c>pack/lock.json</c> embedded at build time (<see cref="LockResource"/>), and reports what
/// differs (<see cref="PackComparison"/>). The server logs one warning per finding and tells a
/// joining admin in chat; a client shows <see cref="PackCheckDialog"/> once it is in the world,
/// until the player dismisses that set of findings.
/// </summary>
public class PackCheckSystem : ModSystem
{
    /// <summary>The csproj embeds <c>pack/lock.json</c> under this name.</summary>
    public const string LockResource = "seraphhorizons.pack-lock.json";

    /// <summary>Per client: the findings the player said not to show again (<see cref="PackCheckClientState"/>).</summary>
    public const string ClientStateFile = "seraphhorizons-packcheck.json";

    private List<Finding> _findings = [];
    private PackLock? _pack;

    /// <summary>What this side found; empty with the switch off or nothing wrong.</summary>
    public IReadOnlyList<Finding> Findings => _findings;

    /// <summary>The pack this build was released with; null with the switch off.</summary>
    public PackLock? Pack => _pack;

    /// <summary>The embedded lock, parsed.</summary>
    public static PackLock ReadEmbeddedLock()
    {
        using var stream = typeof(PackCheckSystem).Assembly.GetManifestResourceStream(LockResource)
                           ?? throw new FormatException($"the build has no {LockResource}");
        using var reader = new StreamReader(stream);
        return PackComparison.ParseLock(reader.ReadToEnd());
    }

    private bool Run(ICoreAPI api, CheckSide side)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).PackVersionCheck)
        {
            api.Logger.Notification("[seraphhorizons] Pack version check is off (PackVersionCheck in ModConfig/{0})",
                SeraphHorizonsSystem.ConfigFile);
            return false;
        }
        try
        {
            _pack = ReadEmbeddedLock();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Pack version check: could not read the pack's lock this build carries, so nothing is checked: {0}", e.Message);
            return false;
        }
        var loaded = api.ModLoader.Mods
            .Where(m => m?.Info?.ModID is { Length: > 0 })
            .Select(m => new LoadedMod(m.Info.ModID, m.Info.Version ?? ""));
        _findings = PackComparison.Compare(_pack, loaded, side, GameVersion.ShortGameVersion,
            Mod.Info.ModID, Mod.Info.Version);
        return _findings.Count > 0;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!Run(api, CheckSide.Server)) return;
        api.Logger.Warning("[seraphhorizons] Pack version check: this server's mods differ from Seraph Horizons {0} (game {1}) in {2} way(s):",
            _pack!.PackVersion, _pack.GameVersion, _findings.Count);
        foreach (var f in _findings)
            api.Logger.Warning("[seraphhorizons] Pack version check: {0}", f.Describe());
        api.Event.PlayerJoin += player =>
        {
            if (!player.HasPrivilege(Privilege.controlserver)) return;
            player.SendMessage(GlobalConstants.GeneralChatGroup, ChatText(), EnumChatType.Notification);
        };
    }

    private string ChatText()
    {
        var lines = new List<string>
        {
            Lang.Get("seraphhorizons:packcheck-chat-header", _pack!.PackVersion, _pack.GameVersion, _findings.Count),
        };
        lines.AddRange(_findings.Select(f => "- " + PackCheckText.Line(f)));
        lines.Add(Lang.Get("seraphhorizons:packcheck-chat-footer"));
        return string.Join("\n", lines);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!Run(api, CheckSide.Client)) return;
        api.Logger.Warning("[seraphhorizons] Pack version check: this client's mods differ from Seraph Horizons {0} (game {1}) in {2} way(s): {3}",
            _pack!.PackVersion, _pack.GameVersion, _findings.Count, string.Join("; ", _findings.Select(f => f.Describe())));
        var fingerprint = PackComparison.Fingerprint(_findings);
        if (LoadClientState(api).DismissedFingerprint == fingerprint)
        {
            api.Logger.Notification("[seraphhorizons] Pack version check: the player dismissed these findings; no dialog");
            return;
        }
        // Once the player is in the world; a moment later, so the dialog opens over the HUD and
        // not into the loading screen's last frames.
        api.Event.LevelFinalize += () => api.Event.RegisterCallback(_ =>
        {
            var dialog = new PackCheckDialog(api, _pack, _findings, dismiss: () =>
            {
                try { api.StoreModConfig(new PackCheckClientState { DismissedFingerprint = fingerprint }, ClientStateFile); }
                catch (Exception e) { api.Logger.Warning("[seraphhorizons] Pack version check: could not store ModConfig/{0}: {1}", ClientStateFile, e.Message); }
            });
            dialog.TryOpen();
        }, 1000);
    }

    private static PackCheckClientState LoadClientState(ICoreAPI api)
    {
        try
        {
            return api.LoadModConfig<PackCheckClientState>(ClientStateFile) ?? new PackCheckClientState();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Pack version check: ModConfig/{0} is not readable, so the dialog shows: {1}", ClientStateFile, e.Message);
            return new PackCheckClientState();
        }
    }
}

/// <summary>A client's own <c>ModConfig/seraphhorizons-packcheck.json</c>.</summary>
public class PackCheckClientState
{
    /// <summary><see cref="PackComparison.Fingerprint"/> of the findings the player chose not to see
    /// again. The dialog returns when the findings are any other set.</summary>
    public string? DismissedFingerprint { get; set; }
}

/// <summary>A finding in the player's language (<c>lang/en.json</c>'s <c>packcheck-*</c>).</summary>
public static class PackCheckText
{
    public static string Line(Finding f) => f.Kind switch
    {
        FindingKind.GameVersion => Lang.Get("seraphhorizons:packcheck-game", f.Expected, f.Found),
        FindingKind.OwnVersion => Lang.Get("seraphhorizons:packcheck-own", f.Subject, f.Expected, f.Found),
        FindingKind.WrongVersion => Lang.Get("seraphhorizons:packcheck-wrongversion", f.Subject, f.Expected, f.Found),
        FindingKind.Missing => Lang.Get("seraphhorizons:packcheck-missing", f.Subject, f.Expected),
        FindingKind.NotInPack => Lang.Get("seraphhorizons:packcheck-notinpack", f.Subject, f.Found),
        _ => f.Describe(),
    };
}
