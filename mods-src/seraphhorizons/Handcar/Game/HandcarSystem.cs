using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Handcar.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Handcar;

/// <summary>
/// The handcar (Handcar/README.md): a standard-gauge rail car for Yang's Transport Tycoon, pumped by
/// hand by up to two riders. It is Yang's own standard-gauge vehicle (class <c>yangtransport.sglocomotive</c>,
/// its renderer, its seats, its attachable cargo and its deconstruction) with this mod's
/// <see cref="EntityBehaviorHumanPowered"/> and three patches (<see cref="HandcarPatches"/>), and the
/// riders' animations, which <see cref="HandcarAnimator"/> runs by the distance the car rolls.
///
/// <para>The classes are registered on both sides whatever the setting. The server decides in
/// <see cref="Start"/>: the <c>Handcar</c> switch on, Yang installed and <see cref="YangBridge"/>
/// resolving. It writes that to the world config (<see cref="RunningKey"/>), which a client follows.
/// Off, the server marks the car's entity and item types and its recipe disabled before the game
/// loads them, and empties the riders' patch, so none of it exists; handcars already in a world are
/// lost.</para>
/// </summary>
public class HandcarSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string RunningKey = "seraphhorizons:handcar";

    public static readonly AssetLocation EntityCode = new(Domain, "handcar");
    public static readonly AssetLocation EntityAsset = new(Domain, "entities/handcar.json");
    public static readonly AssetLocation ItemAsset = new(Domain, "itemtypes/handcar.json");
    public static readonly AssetLocation RecipeAsset = new(Domain, "recipes/grid/handcar.json");
    public static readonly AssetLocation RidersPatch = new(Domain, "patches/handcar-riders.json");
    public static readonly AssetLocation RigAsset = new(Domain, "config/handcar-rig.json");

    /// <summary>The block and item type files the switch owns (<see cref="SwitchRegistry"/>).</summary>
    public static readonly AssetLocation[] TypeAssets = [ItemAsset];

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private HandcarConfig? _config;
    private HandcarRig? _rig;
    private bool _rigLoaded;
    private Harmony? _harmony;
    private bool _seatPatched;

    public static HandcarSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<HandcarSystem>();

    /// <summary>Whether the handcar is in the game on this side: on the server, its switch on, Yang
    /// installed and bound; on a client, as the server says. Known from <see cref="Start"/> on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Yang's members the car uses, when it is enabled.</summary>
    public YangBridge? Bridge { get; private set; }

    /// <summary>This side's settings (the server's count for the drive; a client's for its own fades).</summary>
    public HandcarConfig Config => _config ??= LoadConfig(_api!);

    /// <summary>The client's animator, while the handcar runs there.</summary>
    public HandcarAnimator? Animator { get; private set; }

    /// <summary>The rig, or null when it is missing or broken (logged once).</summary>
    public HandcarRig? Rig
    {
        get
        {
            if (!_rigLoaded && _api != null)
            {
                _rigLoaded = true;
                _rig = LoadRig(_api);
            }
            return _rig;
        }
    }

    public static bool RunsOnServer(ICoreAPI api) => api.World?.Config?.GetBool(RunningKey) ?? false;

    public override void Start(ICoreAPI api)
    {
        _api = api;
        _config = LoadConfig(api);
        api.RegisterEntityBehaviorClass(EntityBehaviorHumanPowered.Code, typeof(EntityBehaviorHumanPowered));
        if (api.Side == EnumAppSide.Server)
        {
            bool wanted = SeraphHorizonsSystem.ConfigFor(api).Handcar;
            bool yang = api.ModLoader.IsModEnabled(YangBridge.ModId);
            if (wanted && yang)
            {
                Bridge = YangBridge.Resolve(out var problems);
                if (Bridge == null)
                    api.Logger.Warning("[seraphhorizons] Handcar: Yang's Transport Tycoon is not as expected, so there is no handcar: {0}",
                        string.Join("; ", problems));
            }
            Enabled = wanted && yang && Bridge != null;
            api.World.Config.SetBool(RunningKey, Enabled);
            if (!Enabled)
                DisablePatches(api);
        }
        else
        {
            Enabled = RunsOnServer(api);
            if (!Enabled)
                DisablePatches(api);
            else
            {
                Bridge = YangBridge.Resolve(out var problems);
                if (Bridge == null)
                {
                    api.Logger.Warning("[seraphhorizons] Handcar: the server runs it, but this client's Yang's Transport Tycoon is not as expected, "
                                       + "so its riders face the car's front and stand still: {0}", string.Join("; ", problems));
                }
            }
        }
    }

    /// <summary>Empties the riders' patch (the seraph's animations and the player's metadata),
    /// before the game's patch loader applies it: on the server, and on a client the server says
    /// runs no handcar (the client patches its own seraph shape).</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        if (api.Assets.TryGet(RidersPatch) is { } asset)
            asset.Data = "[]"u8.ToArray();
    }

    // Types and recipes are read from the assets later in this phase (the game's loaders run at 0.2
    // and 1, this system at the default 0.1), on the server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        _ = Rig;
        if (api.Side != EnumAppSide.Server)
            return;
        if (!Enabled)
        {
            Disable(api);
            return;
        }
        if (!ApplyDrive(api, Config))
            api.Logger.Warning("[seraphhorizons] Handcar: {0} has no SGLocomotive.Drive to set, so it drives on Yang's defaults", EntityAsset);
    }

    /// <summary>Leaves the handcar out of the game: its entity and item types and its recipe disabled.</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in new[] { EntityAsset, ItemAsset })
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), IgnoreComments);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
        if (api.Assets.TryGet(RecipeAsset) is { } recipes)
        {
            var json = JArray.Parse(recipes.ToText(), IgnoreComments);
            foreach (var recipe in json.OfType<JObject>())
                recipe["enabled"] = false;
            recipes.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    /// <summary>Writes the settings' drag and braking into the car's entity type (Yang's
    /// <c>SGLocomotive.Drive</c>, which its vehicles read when they load), so they can be tuned without
    /// a release. Returns false when the asset has no such object.</summary>
    public static bool ApplyDrive(ICoreAPI api, HandcarConfig config)
    {
        if (api.Assets.TryGet(EntityAsset) is not { } asset)
            return false;
        var json = JObject.Parse(asset.ToText(), IgnoreComments);
        if (json["attributes"]?["SGLocomotive"]?["Drive"] is not JObject drive)
            return false;
        drive["Roll0"] = config.CoastDrag;
        drive["RollW"] = config.DragPerWeight;
        drive["BrakeDecel"] = config.BrakeDeceleration;
        asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        return true;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!Enabled || Bridge == null)
            return;
        _harmony = new Harmony(HandcarPatches.ServerHarmonyId);
        HandcarPatches.PatchServer(_harmony, Bridge);
        HandcarPatches.PatchSeat(Bridge);
        _seatPatched = true;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!Enabled || Bridge == null)
            return;
        _harmony = new Harmony(HandcarPatches.ClientHarmonyId);
        HandcarPatches.PatchClient(_harmony, Bridge, api);
        HandcarPatches.PatchSeat(Bridge);
        _seatPatched = true;
        Animator = new HandcarAnimator(api, this);
    }

    public override void Dispose()
    {
        Animator?.Dispose();
        Animator = null;
        if (_harmony != null)
            HandcarPatches.Unpatch(_harmony, _harmony.Id, _api?.Side == EnumAppSide.Client);
        _harmony = null;
        if (_seatPatched)
            HandcarPatches.UnpatchSeat();
        _seatPatched = false;
    }

    private static HandcarRig? LoadRig(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(RigAsset);
        if (asset == null)
        {
            api.Logger.Error("[seraphhorizons] Handcar: {0} is missing; riders stand still", RigAsset);
            return null;
        }
        try
        {
            return HandcarRig.Parse(asset.ToText());
        }
        catch (FormatException e)
        {
            api.Logger.Error("[seraphhorizons] Handcar: {0} is broken, so riders stand still: {1}", RigAsset, e.Message);
            return null;
        }
    }

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static HandcarConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).HandcarSettings ?? new HandcarConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Handcar: ModConfig/{SeraphHorizonsSystem.ConfigFile}, HandcarSettings: {fix}");
        return config;
    }
}
