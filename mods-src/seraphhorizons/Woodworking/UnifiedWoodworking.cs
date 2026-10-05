using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Immersive Woodworking + Logging Expanded as one woodworking system (<c>UnifiedWoodworking</c>):
/// Immersive Woodworking's chopping block becomes the splitting block, with Logging Expanded's
/// tiers and looks; Logging Expanded's sawhorses are the only sawhorses; and the stations either
/// mod had for the same job are retired. Each piece is a <see cref="WoodworkingPart"/>.
///
/// This owns the tweak's lifecycle on one side. The server decides: it runs the tweak with its
/// switch on and both mods installed, and only if every part binds and applies. It binds every
/// part before it applies any, and if a part does not find what it needs, or throws while it is
/// applied, it logs one warning, undoes what was applied and applies nothing, so both mods stay as
/// shipped (a half-applied tweak could leave a world with no splitting block).
///
/// The server writes whether the tweak runs to the world config (<see cref="RunningKey"/>), which
/// the game sends each client before the client starts its mods. A client runs the tweak when the
/// server does, whatever its own switch says, so its predictions, looks, text and handbook match
/// what the server does (if the client then fails to bind, it logs the warning and runs nothing).
/// Server patches go under <see cref="ServerHarmonyId"/>, client ones under
/// <see cref="ClientHarmonyId"/>.
/// </summary>
public sealed class UnifiedWoodworking
{
    // Their own ids: undoing a failed apply unpatches the tweak's id and no other tweak's, and in
    // singleplayer the server's unpatch does not take the client's patches with it.
    public const string ServerHarmonyId = "seraphhorizons.woodworking.server";
    public const string ClientHarmonyId = "seraphhorizons.woodworking";

    /// <summary>The world config key under which the server says whether the tweak runs there.</summary>
    public const string RunningKey = "seraphhorizons:unifiedWoodworking";

    /// <summary>The tweak's parts, in the order their hooks run. One line per part.</summary>
    private static List<WoodworkingPart> CreateParts() =>
    [
        new WoodworkingSettings(),
        new RetiredStations(),
        new WoodworkingText(),
        new SplittingBlock(),
        new Sawhorses(),
        new FrameUpgrades(),
        new MachineUpgrades(),
        new ChopperBed(),
        new WoodworkingGuide(),
    ];

    private readonly List<WoodworkingPart> _parts = CreateParts();
    private Harmony? _harmony;

    /// <summary>Whether the tweak runs on this side: on the server, switched on, both mods
    /// installed and every part bound and applied; on a client, the server runs it and every part
    /// bound and applied here. Known from <see cref="Start"/> on.</summary>
    public bool Active { get; private set; }

    /// <summary>Immersive Woodworking and Logging Expanded, found by name; null unless
    /// <see cref="Active"/>.</summary>
    public WoodworkingMods? Mods { get; private set; }

    /// <summary>The part of type <typeparamref name="T"/> on this side, for a part that needs
    /// another's state.</summary>
    public T Part<T>() where T : WoodworkingPart => _parts.OfType<T>().Single();

    /// <summary>Whether the server says it runs the tweak: what a client reads, from the world
    /// config the server sent it.</summary>
    public static bool RunsOnServer(ICoreAPI api) => api.World?.Config?.GetBool(RunningKey) ?? false;

    /// <param name="switchedOn">This side's own <c>UnifiedWoodworking</c> setting. A client
    /// follows the server instead.</param>
    public void Start(ICoreAPI api, bool switchedOn)
    {
        foreach (var part in _parts)
            part.RegisterClasses(api);
        bool wanted = api.Side == EnumAppSide.Server ? switchedOn : FollowServer(api, switchedOn);
        Active = wanted && WoodworkingMods.Applies(api) && Apply(api);
        if (api.Side == EnumAppSide.Server)
            api.World.Config.SetBool(RunningKey, Active);
        if (Active)
            return;
        foreach (var part in _parts)
            part.Off(api);
    }

    private static bool FollowServer(ICoreAPI api, bool switchedOn)
    {
        bool server = RunsOnServer(api);
        if (server != switchedOn)
            api.Logger.Notification($"[seraphhorizons] Unified woodworking: the server {(server ? "runs" : "does not run")} it, "
                                    + $"so this client {(server ? "runs it too" : "does not")}, whatever its own setting says");
        return server;
    }

    /// <summary>Binds every part, then applies them in order. On a failed bind or an exception,
    /// logs one warning, undoes what was applied (the patches, then each applied part's
    /// <see cref="WoodworkingPart.Undo"/>, last first) and returns false.</summary>
    private bool Apply(ICoreAPI api)
    {
        var applied = new List<WoodworkingPart>();
        try
        {
            if (!Bind(api))
                return false;
            _harmony = new Harmony(api.Side == EnumAppSide.Server ? ServerHarmonyId : ClientHarmonyId);
            foreach (var part in _parts)
            {
                applied.Add(part);
                part.Start(api, _harmony);
            }
            return true;
        }
        catch (Exception e)
        {
            _harmony?.UnpatchAll(_harmony.Id);
            _harmony = null;
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                try
                {
                    applied[i].Undo(api);
                }
                catch (Exception undo)
                {
                    api.Logger.Error($"[seraphhorizons] Unified woodworking: could not undo {applied[i].Name}: {undo}");
                }
            }
            Mods = null;
            api.Logger.Warning($"[seraphhorizons] Unified woodworking: {(applied.Count > 0 ? applied[^1].Name : "binding")} "
                               + $"threw {e.GetType().Name} ({e.Message}); Immersive Woodworking or Logging Expanded changed, "
                               + $"so the tweak is undone and both are left as they ship\n{e}");
            return false;
        }
    }

    private bool Bind(ICoreAPI api)
    {
        Mods = WoodworkingMods.Bind(api, out string? missing);
        var failures = new List<string>();
        if (Mods == null)
            failures.Add(missing!);
        else
            foreach (var part in _parts)
                if (part.Bind(api, Mods) is { } reason)
                    failures.Add($"{part.Name}: {reason}");
        if (failures.Count == 0)
            return true;
        Mods = null;
        api.Logger.Warning($"[seraphhorizons] Unified woodworking: {string.Join("; ", failures)}; Immersive "
                           + "Woodworking or Logging Expanded changed, so both are left as they ship");
        return false;
    }

    public void AssetsLoaded(ICoreAPI api)
    {
        if (!Active)
            return;
        foreach (var part in _parts)
            Run(api, part, "text", () => part.AssetsLoaded(api));
        if (api is ICoreServerAPI sapi)
            foreach (var part in _parts)
                Run(api, part, "asset edits", () => part.EditAssets(sapi));
    }

    public void AssetsFinalize(ICoreAPI api)
    {
        if (!Active)
            return;
        foreach (var part in _parts)
            Run(api, part, "checks", () => part.AssetsFinalize(api));
    }

    // A hook after Start: the patches are in and the assets are being read, so the tweak runs on;
    // what the hook does is left as the mod ships it, with a warning.
    private static void Run(ICoreAPI api, WoodworkingPart part, string what, System.Action hook)
    {
        try
        {
            hook();
        }
        catch (Exception e)
        {
            api.Logger.Warning($"[seraphhorizons] Unified woodworking: {part.Name}'s {what} threw {e.GetType().Name} "
                               + $"({e.Message}), so what it would change is left as it ships\n{e}");
        }
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(_harmony.Id);
        _harmony = null;
        foreach (var part in _parts)
            part.Dispose();
        Active = false;
        Mods = null;
    }
}
