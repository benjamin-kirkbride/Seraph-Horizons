using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Visitors.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Visitors;

/// <summary>
/// Travelling merchants at player-built inns (#456), switch
/// <see cref="SeraphHorizonsConfig.TravellingMerchants"/>. Every raised inn flag is an inn
/// (<see cref="InnBook"/>, saved as <see cref="SaveKey"/>), whose visit cycle
/// (<see cref="VisitPlanner"/>) this steps every 2 s on the visitor clock: the calendar plus the days
/// <c>/sh trade simulate</c> added (<see cref="InnBook.Offset"/>, advanced on
/// <see cref="EconomySystem.SimulatedDay"/>). Once a day an idle inn is evaluated (the building by
/// <see cref="InnRules"/>, the owner's standing, the region's supply); when everything passes, a
/// visitor is on its way. Arrivals and evaluations wait for the inn's chunk to be loaded: a visitor
/// only arrives where someone is to see it. A visitor's leaving is checked by the visitor itself
/// (<see cref="EntityVisitingTrader"/>), so one in an unloaded chunk leaves as soon as it loads.
///
/// ExecuteOrder after the economy (0.65): the trade lists, standing and supply are all set up.
/// </summary>
public class InnSystem : ModSystem
{
    public const string SaveKey = "seraphhorizons:inns";
    /// <summary>How far from the caller <c>/sh trade inn</c> looks for an inn flag.</summary>
    public const int CommandRadius = 16;
    /// <summary>Players this close hear a visitor arrive and leave.</summary>
    public const int HearingRadius = 48;
    public static readonly AssetLocation FlagCode = new(SeraphHorizonsSystem.HarmonyId, "innflag");

    private ICoreServerAPI? _sapi;
    private long _tick;

    public static InnSystem? Of(ICoreAPI? api) => api?.ModLoader.GetModSystem<InnSystem>();

    public override double ExecuteOrder() => 0.66;

    /// <summary>On, on the server (the switch, and the trading system present).</summary>
    public bool Active { get; private set; }

    public InnBook Book { get; private set; } = new();
    public InnSettings Rules { get; } = new();
    public VisitSettings Visits { get; } = new();
    public ConditionSettings Conditions { get; private set; } = new();

    public double Now => (_sapi?.World.Calendar.TotalDays ?? 0) + Book.Offset;

    public override void Start(ICoreAPI api)
    {
        // Whatever the switch: a client must know the classes of what the server sends it.
        api.RegisterBlockClass(BlockInnFlag.ClassName, typeof(BlockInnFlag));
        api.RegisterEntity(EntityVisitingTrader.ClassName, typeof(EntityVisitingTrader));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        var config = SeraphHorizonsSystem.ConfigFor(api);
        if (!config.TravellingMerchants)
        {
            api.Logger.Notification("[seraphhorizons] Travelling merchants: off (TravellingMerchants in ModConfig/{0}); visitors leave",
                SeraphHorizonsSystem.ConfigFile);
            return;
        }
        if (TradingSystem.Of(api) is null)
        {
            api.Logger.Warning("[seraphhorizons] Travelling merchants: no trading system; off");
            return;
        }
        Conditions = new ConditionSettings
        {
            MinSupply = Math.Max(0, config.TravellingMerchantMinSupply),
            CampRadius = Math.Max(0, config.TraderStandingSpilloverKm) * 1000,
        };
        Active = true;
        api.Event.SaveGameLoaded += Load;
        api.Event.GameWorldSave += Save;
        if (EconomySystem.Of(api) is { } economy)
            economy.SimulatedDay += _ =>
            {
                Book.Offset += 1;
                Update();
            };
        _tick = api.Event.RegisterGameTickListener(_ => Update(), 2000);
        InnCommands.Register(api, this);
        api.Logger.Notification("[seraphhorizons] Travelling merchants: on (standing tier {0} within {1} blocks, supply {2})",
            Conditions.MinTier, Conditions.CampRadius, Conditions.MinSupply);
    }

    public override void Dispose()
    {
        if (_sapi != null && _tick != 0) _sapi.Event.UnregisterGameTickListener(_tick);
    }

    private void Load()
    {
        var api = _sapi!;
        try
        {
            var json = api.WorldManager.SaveGame.GetData<string>(SaveKey);
            Book = json is null ? new InnBook() : InnBook.FromJson(json);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Travelling merchants: the saved inns do not load ({0}); the old data stays under {1}.broken",
                e.Message, SaveKey);
            api.WorldManager.SaveGame.StoreData(SaveKey + ".broken", api.WorldManager.SaveGame.GetData(SaveKey));
            Book = new InnBook();
        }
    }

    private void Save() => _sapi!.WorldManager.SaveGame.StoreData(SaveKey, Book.ToJson());

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    public static string KindName(string kind) => L("trading-inn-kind-" + kind);

    // ---- The flag ----

    public void Raised(BlockPos pos, IServerPlayer? player)
    {
        if (!Active) return;
        Book.Raise(new InnPos(pos.X, pos.Y, pos.Z), player?.PlayerUID ?? "");
        if (player != null) Tell(player, pos);
    }

    public void Lowered(BlockPos pos)
    {
        if (!Active || Book.Lower(new InnPos(pos.X, pos.Y, pos.Z)) is not { } record) return;
        if (record.Phase == InnPhase.Visiting && Loaded(record.EntityId) is { } visitor) Depart(visitor);
    }

    /// <summary>A player right-clicked (or raised) a flag: what the inn still lacks.</summary>
    public void Tell(IServerPlayer player, BlockPos pos)
    {
        if (!Active) return;
        var record = Book.Get(new InnPos(pos.X, pos.Y, pos.Z));
        var eval = Evaluate(pos, record?.Owner is { Length: > 0 } owner ? owner : player.PlayerUID);
        var missing = eval.Missing.Select(m => L("trading-inn-missing-" + m)).ToList();
        string line = record?.Phase switch
        {
            InnPhase.Pending => L("trading-inn-tell-pending", KindName(record.Kind)),
            InnPhase.Visiting => L("trading-inn-tell-visiting", KindName(record.Kind)),
            InnPhase.Cooldown => L("trading-inn-tell-cooldown", Math.Ceiling(record.CooldownUntil - Now)),
            _ => missing.Count == 0 ? L("trading-inn-tell-ready") : L("trading-inn-tell-missing", string.Join(", ", missing)),
        };
        player.SendMessage(GlobalConstants.GeneralChatGroup, line, EnumChatType.Notification);
    }

    // ---- Evaluation ----

    /// <summary>The inn around pos (a flag or anywhere): the building, the owner's standing, the supply.</summary>
    public InnEvaluation Evaluate(BlockPos pos, string ownerUid)
    {
        var api = _sapi!;
        var report = InnProbe.Evaluate(api.World, pos, Rules);
        return new InnEvaluation(report, StandingAt(pos, ownerUid), SupplyAt(pos));
    }

    private StandingCheck StandingAt(BlockPos pos, string ownerUid)
    {
        var api = _sapi!;
        var trading = TradingSystem.Of(api);
        // Standing is with camps; a world without the grid (or without standing) has none to ask.
        if (StandingSystem.Of(api) is not { Enabled: true } standing || trading is not { GridReady: true })
            return new StandingCheck(true, 0, null, 0, Skipped: true);
        var camps = trading.Camps!.Registry.Snapshot().Where(r => r.Status == CampStatus.Placed)
            .Select(r => new CampSite(Standing.Core.TraderIds.Camp(r.CellX, r.CellZ), r.X, r.Z)).ToList();
        if (ownerUid.Length == 0)
            return new StandingCheck(false, 0, null, camps.Count);
        return VisitConditions.Standing(camps, pos.X, pos.Z, id => standing.ViewFor(ownerUid, id).TierIndex, Conditions);
    }

    private Dictionary<string, SupplyCheck> SupplyAt(BlockPos pos)
    {
        var api = _sapi!;
        var result = new Dictionary<string, SupplyCheck>();
        var economy = EconomySystem.Of(api);
        var lists = TradingSystem.Of(api)?.Lists;
        foreach (string kind in VisitorKinds.All)
        {
            if (economy is not { RegionalSupply: true } || Conditions.MinSupply <= 0)
            {
                result[kind] = new SupplyCheck(true, 0, Skipped: true);
                continue;
            }
            var def = lists?.For(VisitorKinds.TypeOf(kind));
            var side = def?.Buying;
            var codes = side is null ? [] : side.Core.Concat(side.Rotating.List).Select(e => BuyerIndex.FullCode(e.Code)).ToList();
            string region = SupplyRegion.KeyOf(pos.X, pos.Z);
            result[kind] = VisitConditions.Supply(codes, code => economy.Supply.Level(region, code), Conditions);
        }
        return result;
    }

    // ---- The cycle ----

    private bool ChunkLoaded(BlockPos pos) => _sapi!.World.BlockAccessor.GetChunkAtBlockPos(pos) != null;

    private EntityVisitingTrader? Loaded(long entityId) =>
        entityId != 0 && _sapi!.World.LoadedEntities.TryGetValue(entityId, out var e) && e is EntityVisitingTrader v && v.Alive ? v : null;

    public void Update()
    {
        if (!Active) return;
        foreach (var record in Book.Inns.Values.ToList())
        {
            try
            {
                Step(record);
            }
            catch (Exception e)
            {
                _sapi!.Logger.Error("[seraphhorizons] Travelling merchants: the inn at {0} failed to update: {1}", record.Key, e);
            }
        }
    }

    private void Step(InnRecord record)
    {
        var api = _sapi!;
        var pos = new BlockPos(record.X, record.Y, record.Z);
        bool loaded = ChunkLoaded(pos);
        if (loaded && api.World.BlockAccessor.GetBlock(pos).Code != FlagCode)
        {
            // The flag went without OnBlockRemoved (worldedit, a schematic): the inn goes too.
            Lowered(pos);
            return;
        }
        switch (VisitPlanner.Due(record, Now))
        {
            case VisitAction.Evaluate when loaded:
                var eval = Evaluate(pos, record.Owner);
                if (VisitPlanner.Evaluated(record, Now, eval.PassingKinds, eval.Missing, api.World.Rand.NextDouble(), api.World.Rand.NextDouble(), Visits) is { } kind)
                    TellOwner(record, L("trading-inn-scheduled", Rel(pos), KindName(kind), Math.Ceiling(record.ArriveDay - Now)));
                break;
            case VisitAction.Arrive when loaded:
                Arrive(record, out _);
                break;
            case VisitAction.Leave:
                if (Loaded(record.EntityId) is { } visitor) Depart(visitor);
                VisitPlanner.Left(record, Now, Visits);
                break;
            case VisitAction.CooldownOver:
                VisitPlanner.CooledDown(record);
                break;
        }
    }

    /// <summary>The visitor arrives now, at the stall. False (with why) when it cannot spawn; the
    /// visit then counts as over, so the inn does not retry every tick.</summary>
    public bool Arrive(InnRecord record, out string error)
    {
        var api = _sapi!;
        var world = api.World;
        var pos = new BlockPos(record.X, record.Y, record.Z);
        var report = InnProbe.Evaluate(world, pos, Rules);
        var at = report.SpawnAt ?? report.Stall ?? new InnPos(pos.X, pos.Y + 1, pos.Z);
        string type = VisitorKinds.TypeOf(record.Kind);
        var atPos = new BlockPos(at.X, at.Y, at.Z);
        var climate = world.BlockAccessor.GetClimateAt(atPos, EnumGetClimateMode.WorldGenValues);
        string outfit = climate is null ? "temperate" : TraderTypes.OutfitClimate(climate.Temperature, climate.Rainfall);
        string gender = world.Rand.NextDouble() < 0.5 ? "male" : "female";
        var code = new AssetLocation(SeraphHorizonsSystem.HarmonyId, $"visitor-{gender}-{type}-{outfit}");
        if (world.GetEntityType(code) is not { } props || world.ClassRegistry.CreateEntity(props) is not EntityVisitingTrader visitor)
        {
            error = $"no visitor entity {code}";
            api.Logger.Error("[seraphhorizons] Travelling merchants: {0}", error);
            VisitPlanner.Left(record, Now, Visits);
            return false;
        }
        visitor.Pos.SetPos(at.X + 0.5, at.Y, at.Z + 0.5);
        visitor.WatchedAttributes.SetString(StandingSystem.TraderIdAttr, VisitorKinds.TraderId(record.Kind));
        visitor.InnPos = pos;
        visitor.SetStockTier(StockTierFor(record));
        world.SpawnEntity(visitor);
        VisitPlanner.Arrived(record, visitor.EntityId, Now, world.Rand.NextDouble(), Visits);
        visitor.LeaveDay = record.LeaveDay;
        string line = L("trading-inn-arrived", KindName(record.Kind), Rel(pos));
        Announce(visitor.Pos.AsBlockPos, line);
        TellOwner(record, line, heardNearby: true);
        error = "";
        return true;
    }

    /// <summary>The tier the visitor stocks for: its inn owner's standing with visitors of its kind,
    /// so rare goods (standingTier 3) come to the inns of their regulars.</summary>
    private int StockTierFor(InnRecord record) =>
        record.Owner.Length > 0 && StandingSystem.Of(_sapi!) is { Enabled: true } standing
            ? standing.ViewFor(record.Owner, VisitorKinds.TraderId(record.Kind)).TierIndex
            : 0;

    /// <summary>A loaded visitor's check (every second, from its tick): leave when the visit is over
    /// or its inn is gone.</summary>
    public void CheckVisitor(EntityVisitingTrader visitor)
    {
        var record = Book.ByEntity(visitor.EntityId);
        if (record is { Phase: InnPhase.Visiting } && Now < record.LeaveDay) return;
        if (record is { Phase: InnPhase.Visiting }) VisitPlanner.Left(record, Now, Visits);
        Depart(visitor);
    }

    public void Depart(EntityVisitingTrader visitor)
    {
        Announce(visitor.Pos.AsBlockPos, L("trading-inn-leaves", KindName(visitor.Kind)));
        visitor.Die(EnumDespawnReason.Removed);
    }

    /// <summary><c>/sh trade inn dismiss</c>: the visitor leaves now (cooldown follows), or a pending
    /// visit is called off. False when there is neither.</summary>
    public bool Dismiss(InnRecord record)
    {
        switch (record.Phase)
        {
            case InnPhase.Visiting:
                if (Loaded(record.EntityId) is { } visitor) Depart(visitor);
                VisitPlanner.Left(record, Now, Visits);
                return true;
            case InnPhase.Pending:
                VisitPlanner.Cancelled(record);
                return true;
            default:
                return false;
        }
    }

    // ---- Chat ----

    /// <summary>A position as players see it (relative to the world spawn, as the camp list shows them).</summary>
    public string Rel(BlockPos pos)
    {
        var spawn = _sapi!.World.DefaultSpawnPosition.XYZInt;
        return $"{pos.X - spawn.X}, {pos.Y}, {pos.Z - spawn.Z}";
    }

    public string Rel(InnPos pos) => Rel(new BlockPos(pos.X, pos.Y, pos.Z));

    private void Announce(BlockPos at, string line)
    {
        foreach (var player in _sapi!.World.AllOnlinePlayers.OfType<IServerPlayer>())
            if (player.Entity?.Pos is { } p && p.DistanceTo(at.ToVec3d()) <= HearingRadius)
                player.SendMessage(GlobalConstants.GeneralChatGroup, line, EnumChatType.Notification);
    }

    /// <param name="heardNearby">The line was announced at the inn, so an owner there has heard it.</param>
    private void TellOwner(InnRecord record, string line, bool heardNearby = false)
    {
        if (record.Owner.Length == 0 || _sapi!.World.PlayerByUid(record.Owner) is not IServerPlayer owner || owner.ConnectionState != EnumClientState.Playing)
            return;
        if (heardNearby && owner.Entity?.Pos is { } p && p.DistanceTo(new Vec3d(record.X, record.Y, record.Z)) <= HearingRadius) return;
        owner.SendMessage(GlobalConstants.GeneralChatGroup, line, EnumChatType.Notification);
    }
}
