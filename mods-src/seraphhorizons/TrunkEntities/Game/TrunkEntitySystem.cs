using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Trunk entities: Logging Expanded's tree trunks are never items in an inventory. A trunk in the
/// world is an <see cref="EntityTrunk"/> lying on the ground, dragged by hand
/// (<see cref="TrunkGrab"/>) or with a rope, shoved, floated; a loose trunk item entity is swapped
/// for one as it spawns (<see cref="TrunkSpawns"/>), trunk stacks get a storage flag no inventory
/// takes, and trunk multiblocks already placed are deleted (<see cref="OldTrunkBlocks"/>).
/// With Carry On, a trunk is also shouldered, put down, loaded and attached to carts through it
/// (<see cref="TrunkCarry"/>).
///
/// The classes are registered on both sides whatever the setting. The server decides in
/// <see cref="Start"/> (the <c>TrunkEntities</c> switch, Logging Expanded installed and
/// <see cref="LoggingBridge"/> resolving) and writes it to the world config
/// (<see cref="RunningKey"/>), which a client follows. Off, there is no swap, no flag, no deletion
/// and no grab, the feature's asset patches are emptied (<see cref="DisablePatches"/>), and Logging
/// Expanded's trunks behave as it ships them. The two entity types exist either way.
/// </summary>
public class TrunkEntitySystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string LeModId = LoggingBridge.ModId;
    public const string CarryOnModId = "carryon";
    public const string HarmonyId = "seraphhorizons.trunkentities";

    /// <summary>The world config key under which the server says whether trunk entities run.</summary>
    public const string RunningKey = "seraphhorizons:trunkEntities";

    public static readonly AssetLocation ThinCode = new(Domain, "trunk-thin");
    public static readonly AssetLocation ThickCode = new(Domain, "trunk-thick");

    /// <summary>The feature's JSON patches (<c>patches/trunkentities-*.json</c>), emptied in
    /// <see cref="Start"/> when it is off. A patch added to the feature is listed here.</summary>
    public static readonly List<AssetLocation> PatchAssets =
    [
        new(Domain, "patches/trunkentities-carryon.json"),
        new(Domain, "patches/trunkentities-carts.json"),
    ];

    /// <summary>The storage flag trunk stacks get: no inventory takes it.</summary>
    public const EnumItemStorageFlags NoStorage = EnumItemStorageFlags.Custom10;

    private ICoreAPI? _api;
    private TrunkEntityConfig? _config;
    private LoggingBridge? _logging;
    private bool _loggingResolved;
    private Harmony? _harmony;
    private bool _carrying;
    private long _speedListener;

    public static TrunkEntitySystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<TrunkEntitySystem>();

    /// <summary>Whether trunk entities run on this side: on the server, its switch on, Logging
    /// Expanded installed and the bridge resolved; on a client, as the server says. Known from
    /// <see cref="Start"/> on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>This side's settings (the server's are what count).</summary>
    public TrunkEntityConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>Logging Expanded, or null when something the feature needs is missing (logged once).</summary>
    public LoggingBridge? Logging
    {
        get
        {
            if (!_loggingResolved && _api != null)
            {
                _loggingResolved = true;
                _logging = LoggingBridge.Resolve(_api, out var problems);
                if (_logging == null)
                    _api.Logger.Warning("[seraphhorizons] Trunk entities: Logging Expanded is not as expected, so trunks stay items: {0}",
                        string.Join("; ", problems));
            }
            return _logging;
        }
    }

    /// <summary>The rope-less grabs (server side; null on a client).</summary>
    public TrunkGrab? Grabs { get; private set; }

    /// <summary>Whether Carry On is installed (carrying, racks by hand and cart slots need it).</summary>
    public bool CarryOn => _api?.ModLoader.IsModEnabled(CarryOnModId) ?? false;

    /// <summary>Whether the server says trunk entities run: what a client reads.</summary>
    public static bool RunsOnServer(ICoreAPI api) => api.World?.Config?.GetBool(RunningKey) ?? false;

    // After Logging Expanded's systems (0.1), so its AssetsFinalize has set the trunks' own storage
    // flag before this one's replaces it.
    public override double ExecuteOrder() => 0.15;

    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterEntity("seraphhorizons.EntityTrunk", typeof(EntityTrunk));
        api.RegisterEntityBehaviorClass(TrunkCarry.BehaviorCode, typeof(EntityBehaviorTrunkCarry));
        if (api.Side == EnumAppSide.Server)
        {
            Enabled = SeraphHorizonsSystem.ConfigFor(api).TrunkEntities && api.ModLoader.IsModEnabled(LeModId) && Logging != null;
            api.World.Config.SetBool(RunningKey, Enabled);
        }
        else
        {
            Enabled = RunsOnServer(api);
            if (Enabled != SeraphHorizonsSystem.ConfigFor(api).TrunkEntities)
                api.Logger.Notification($"[seraphhorizons] Trunk entities: the server {(Enabled ? "runs" : "does not run")} them, "
                                        + $"so this client {(Enabled ? "does too" : "does not")}, whatever its own setting says");
        }
        if (!Enabled)
            DisablePatches(api);
        else if (!CarryOn)
            api.Logger.Warning("[seraphhorizons] Trunk entities: Carry On is not installed, so trunks cannot be carried: drag or rope them");
        else
            _carrying = TrunkCarry.Start(api);
    }

    /// <summary>Empties the feature's patch files, before the game's patch loader applies them.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        foreach (var location in PatchAssets)
            if (api.Assets.TryGet(location) is { } asset)
                asset.Data = "[]"u8.ToArray();
    }

    // Both sides: a client's own copy of the blocks is then as the server's.
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (!Enabled)
            return;
        int count = 0;
        foreach (var block in api.World.Blocks)
            if (block?.Code is { Domain: LeModId } code && code.Path.StartsWith("treetrunk-", StringComparison.Ordinal))
            {
                block.StorageFlags = NoStorage;
                count++;
            }
        api.Logger.Notification("[seraphhorizons] Trunk entities: {0} tree trunk variants fit in no inventory", count);
        if (_carrying)
        {
            int racks = TrunkCarry.StripRacks(api);
            api.Logger.Notification("[seraphhorizons] Trunk entities: {0} Trunk Storage Rack variants can no longer be carried", racks);
            TrunkCarry.RegisterCartBypass(api);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!Enabled)
            return;
        api.Event.OnEntitySpawn += entity => TrunkSpawns.OnEntitySpawn(api.World, entity);
        // Trunk items saved before trunk entities ran: swapped on the next tick, not mid-load.
        api.Event.OnEntityLoaded += entity =>
        {
            if (entity is EntityItem item && Trunks.IsTrunk(item.Itemstack))
                api.Event.RegisterCallback(_ => TrunkSpawns.OnEntitySpawn(api.World, item), 0);
        };
        Grabs = new TrunkGrab(api, this);
        if (_carrying)
            _speedListener = api.Event.RegisterGameTickListener(_ => TrunkCarry.UpdateSpeeds(api), TrunkCarry.SpeedCheckMs);
        _harmony = new Harmony(HarmonyId);
        if (!OldTrunkBlocks.Patch(_harmony, api))
            api.Logger.Warning("[seraphhorizons] Trunk entities: Logging Expanded's trunk block entity is not as expected, so placed trunks are left as they are");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.RegisterEntityRendererClass("seraphhorizons.trunk", typeof(TrunkEntityRenderer));
    }

    public override void Dispose()
    {
        Grabs?.Dispose();
        Grabs = null;
        if (_speedListener != 0)
            (_api as ICoreServerAPI)?.Event.UnregisterGameTickListener(_speedListener);
        _speedListener = 0;
        if (_carrying)
            TrunkCarry.Stop();
        _carrying = false;
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static TrunkEntityConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).TrunkEntitiesSettings ?? new TrunkEntityConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Trunk entities: ModConfig/{SeraphHorizonsSystem.ConfigFile}, TrunkEntitiesSettings: {fix}");
        return config;
    }
}
