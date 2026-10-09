using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Trading.Standing;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The player-built eidolon (Eidolon/README.md): its entity type <c>seraphhorizons:eidolon</c>
/// (<see cref="EntityLaborEidolon"/>, with <see cref="EntityBehaviorEidolonCharge"/>,
/// <see cref="EntityBehaviorEidolonOrders"/> and <see cref="AiTaskEidolonOrder"/>), the creative
/// spawner <c>seraphhorizons:creature-eidolon</c>, the built-in orders <c>stay</c> and <c>goto</c>,
/// and the admin command <c>/sh eidolon</c>.
///
/// <para>The classes are registered on both sides whatever the setting. The server decides in
/// <see cref="Start"/> from the <c>Eidolon</c> switch and writes it to the world config
/// (<see cref="RunningKey"/>), which a client follows. Off, the entity type and the spawner are
/// disabled before the game loads them, so neither exists, and eidolons already in a world are lost.</para>
/// </summary>
public class EidolonSystem : ModSystem
{
    public const string Domain = "seraphhorizons";
    public const string RunningKey = "seraphhorizons:eidolon";

    public static readonly AssetLocation EntityCode = new(Domain, "eidolon");
    public static readonly AssetLocation EntityAsset = new(Domain, "entities/eidolon.json");
    public static readonly AssetLocation SpawnerAsset = new(Domain, "itemtypes/creature-eidolon.json");

    /// <summary>The block and item type files the switch owns (<see cref="SwitchRegistry"/>).</summary>
    public static readonly AssetLocation[] TypeAssets = [SpawnerAsset];

    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    private ICoreAPI? _api;
    private EidolonConfig? _config;

    public static EidolonSystem? Of(ICoreAPI? api) => api?.ModLoader.GetModSystem<EidolonSystem>();

    /// <summary>Whether eidolons are in the game on this side: on the server, its switch; on a client,
    /// as the server says. Known from <see cref="Start"/> on.</summary>
    public bool Enabled { get; private set; }

    /// <summary>This side's settings (the server's are what count).</summary>
    public EidolonConfig Config => _config ??= LoadConfig(_api!);

    public static bool RunsOnServer(ICoreAPI api) => api.World?.Config?.GetBool(RunningKey) ?? false;

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterEntity("seraphhorizons.EntityLaborEidolon", typeof(EntityLaborEidolon));
        api.RegisterEntityBehaviorClass(EntityBehaviorEidolonCharge.Code, typeof(EntityBehaviorEidolonCharge));
        api.RegisterEntityBehaviorClass(EntityBehaviorEidolonOrders.Code, typeof(EntityBehaviorEidolonOrders));
        AiTaskRegistry.Register<AiTaskEidolonOrder>(AiTaskEidolonOrder.Code);
        EidolonOrders.Register(StayOrder.OrderCode, (_, _) => new StayOrder());
        EidolonOrders.Register(GoToOrder.OrderCode, (_, args) => GoToOrder.From(args));
        if (api.Side == EnumAppSide.Server)
        {
            Enabled = SeraphHorizonsSystem.ConfigFor(api).Eidolon;
            api.World.Config.SetBool(RunningKey, Enabled);
        }
        else
            Enabled = RunsOnServer(api);
    }

    // Types are read from the assets later in this phase (the game's loaders run at 0.2), on the
    // server only.
    public override void AssetsLoaded(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server && !Enabled)
            Disable(api);
    }

    /// <summary>Leaves the eidolon out of the game: its entity type and its spawner disabled.</summary>
    public static void Disable(ICoreAPI api)
    {
        foreach (var location in new[] { EntityAsset, SpawnerAsset })
        {
            if (api.Assets.TryGet(location) is not { } asset)
                continue;
            var json = JObject.Parse(asset.ToText(), IgnoreComments);
            json["enabled"] = false;
            asset.Data = Encoding.UTF8.GetBytes(json.ToString());
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (Enabled)
            EidolonCommands.Register(api, this);
    }

    /// <summary>
    /// Spawns an eidolon at <paramref name="pos"/> facing <paramref name="yaw"/>, owned by
    /// <paramref name="owner"/> (null: nobody), charged with <paramref name="gears"/> temporal gears'
    /// worth; with <paramref name="activate"/> it plays <c>activate</c> as it appears, the gantry's
    /// waking (#672: place it at the gantry's <c>body</c> point, facing out of its front). Server side;
    /// null when eidolons are off.
    /// </summary>
    public EntityLaborEidolon? Spawn(IWorldAccessor world, Vec3d pos, float yaw, IPlayer? owner, bool activate, double gears = 1)
    {
        if (!Enabled || world.Side != EnumAppSide.Server || world.GetEntityType(EntityCode) is not { } type)
            return null;
        if (world.ClassRegistry.CreateEntity(type) is not EntityLaborEidolon eidolon)
            return null;
        eidolon.Pos.SetPos(pos);
        eidolon.Pos.Yaw = yaw;
        eidolon.PositionBeforeFalling.Set(pos);
        eidolon.SetOwner(owner?.PlayerUID, owner?.PlayerName);
        eidolon.WatchedAttributes.SetBool("noSpawnAnim", true);
        world.SpawnEntity(eidolon);
        if (eidolon.GetBehavior<EntityBehaviorEidolonCharge>() is { } charge)
            charge.ChargeDays = gears * charge.DaysPerGear;
        if (activate)
            eidolon.Activate();
        return eidolon;
    }

    /// <summary>Whether <paramref name="playerUid"/> may command an eidolon owned by
    /// <paramref name="ownerUid"/>: the owner and their company (<see cref="EidolonOwnership"/>), the
    /// trading standing's companies when it runs, else any vanilla group the two share.</summary>
    public bool MayCommand(string? ownerUid, string playerUid)
    {
        if (string.IsNullOrEmpty(ownerUid) || ownerUid == playerUid)
            return true;
        if (_api is not ICoreServerAPI sapi)
            return false;
        if (StandingSystem.Of(sapi) is { Enabled: true } standing)
            return EidolonOwnership.MayCommand(ownerUid, playerUid, standing.CompanyOf(ownerUid), standing.CompanyOf(playerUid));
        IEnumerable<int> mine = sapi.PlayerData.GetPlayerDataByUid(ownerUid)?.PlayerGroupMemberships?.Keys ?? Enumerable.Empty<int>();
        IEnumerable<int> theirs = sapi.PlayerData.GetPlayerDataByUid(playerUid)?.PlayerGroupMemberships?.Keys ?? Enumerable.Empty<int>();
        return EidolonOwnership.MayCommand(ownerUid, playerUid, null, null, mine.Intersect(theirs).Any());
    }

    /// <summary>The loaded eidolons nearest first within <paramref name="range"/> blocks of <paramref name="pos"/>.</summary>
    public static IEnumerable<EntityLaborEidolon> Near(ICoreServerAPI api, Vec3d pos, double range) =>
        api.World.LoadedEntities.Values.OfType<EntityLaborEidolon>()
            .Where(e => e.Alive && e.Pos.DistanceTo(pos) <= range)
            .OrderBy(e => e.Pos.DistanceTo(pos));

    // The settings object is the one in the mod's loaded config, so the fixes hold for every reader.
    private static EidolonConfig LoadConfig(ICoreAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api).EidolonSettings ?? new EidolonConfig();
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Eidolon: ModConfig/{SeraphHorizonsSystem.ConfigFile}, EidolonSettings: {fix}");
        return config;
    }
}
