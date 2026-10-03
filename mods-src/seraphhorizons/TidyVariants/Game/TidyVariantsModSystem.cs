using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TidyVariants;

/// <summary>
/// Tidy Variants (a Seraph Horizons feature, switch <see cref="SeraphHorizonsConfig.TidyVariants"/>):
/// hides orientation and open/closed variants, and groups the remaining variants in the creative
/// inventory and the handbook, from one set of rules (README.md, #252).
///
/// The rules are resolved by the game-independent engine in Core/; the game-facing code (rule
/// input from the loaded collectibles, handbook groupBy, creative GUI patches) lives in Game/.
/// This system applies no Harmony patches: the creative inventory's are applied, on the client
/// only, by <see cref="TidyCreativeModSystem"/>.
///
/// It resolves the rules once per side, once every collectible's creative tabs and stacks are
/// final (several blocks build their CreativeInventoryStacks in OnLoaded: clutter, antler
/// mounts, shields): on the server at the WorldReady run phase (blocks' OnLoaded runs in
/// LoadGamePre, after mods' AssetsFinalize); on the client in <see cref="AssetsFinalize"/>, which
/// runs inside level finalize, after blocks' OnLoaded and before the handbook gathers its stacks
/// (the <c>LevelFinalize</c> event) or the creative inventory is first built. Both run late among
/// mods (<see cref="ExecuteOrder"/>). The result is <see cref="Bridge"/> /
/// <see cref="ForSide"/>, announced by <see cref="Resolved"/>. A failure, or the switch off, leaves
/// the bridge null, i.e. vanilla behaviour. Only the client acts on it; the server's resolution
/// is there for the Atlas scenarios and the report.
/// </summary>
public class TidyVariantsModSystem : ModSystem
{
    // Per side, since singleplayer runs both in one process.
    private static TidyBridge? _server, _client;

    /// <summary>This side's resolution, or null before it is built (see the class summary), after a
    /// failure, or with the feature switched off.</summary>
    public TidyBridge? Bridge { get; private set; }

    /// <summary>The resolution of the given side in this process, or null.</summary>
    public static TidyBridge? ForSide(EnumAppSide side) => side == EnumAppSide.Server ? _server : _client;

    /// <summary>Raised once per side right after its resolution is built (on that side's main thread).</summary>
    public static event Action<TidyBridge>? Resolved;

    /// <summary>Late, so other mods' changes to creative tabs and stacks are in.</summary>
    public override double ExecuteOrder() => 10;

    public override void Start(ICoreAPI api)
    {
        if (!Enabled(api))
            api.Logger.Notification("[seraphhorizons] Tidy Variants is off (TidyVariants in ModConfig/{0}); the creative inventory and handbook stay vanilla",
                SeraphHorizonsSystem.ConfigFile);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Client && Enabled(api)) Resolve(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Enabled(api))
            api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, () => Resolve(api));
    }

    /// <summary>Whether the feature is switched on in ModConfig/seraphhorizons.json. Every Tidy Variants
    /// system checks it before doing anything.</summary>
    internal static bool Enabled(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).TidyVariants;

    private void Resolve(ICoreAPI api)
    {
        try
        {
            var bridge = TidyBridge.Build(api);
            Bridge = bridge;
            if (api.Side == EnumAppSide.Server) _server = bridge; else _client = bridge;
            api.Logger.Notification("[seraphhorizons] Tidy Variants: {0}", bridge.Summary());
            if (bridge.SkippedStacks > 0)
                api.Logger.Debug("[seraphhorizons] Tidy Variants: {0} unresolved creative stacks skipped", bridge.SkippedStacks);
            foreach (var g in bridge.Resolution.Issues.GroupBy(i => i.Kind).OrderByDescending(g => g.Count()))
                api.Logger.Debug("[seraphhorizons] Tidy Variants: issue {0}: {1}", g.Key, g.Count());
            foreach (var issue in bridge.Resolution.Issues)
                api.Logger.VerboseDebug("[seraphhorizons] Tidy Variants: {0}: {1}", issue.Kind, issue.Message);
        }
        catch (Exception ex)
        {
            Bridge = null;
            api.Logger.Error("[seraphhorizons] Tidy Variants: rule resolution failed; creative inventory and handbook stay vanilla: {0}", ex);
            return;
        }
        try { Resolved?.Invoke(Bridge); }
        catch (Exception ex) { api.Logger.Error("[seraphhorizons] Tidy Variants: a Resolved handler failed: {0}", ex); }
    }

    public override void Dispose()
    {
        if (Bridge is not null)
        {
            if (ReferenceEquals(_server, Bridge)) _server = null;
            if (ReferenceEquals(_client, Bridge)) _client = null;
            Bridge = null;
        }
    }
}
