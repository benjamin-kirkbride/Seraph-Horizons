using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Standing;

/// <summary>
/// Standing per trader (#452) and companies (#463), server side, switch
/// <see cref="SeraphHorizonsConfig.TraderStanding"/>. Holds the <see cref="StandingLedger"/> (saved
/// with the world, <see cref="SaveKey"/>), the rules from <c>config/standing-tiers.json</c>, and
/// serves them to the traders as <see cref="TradingSystem.Standing"/>. Companies are vanilla groups:
/// membership is read live from the server's player data at every use, and patches on joining,
/// leaving and disbanding (<see cref="GroupHooks"/>) sync it at once; without them it syncs at the
/// next use.
///
/// ExecuteOrder after <see cref="TradingSystem"/>, whose <c>/sh trade</c> node this adds to.
/// </summary>
public class StandingSystem : ModSystem, IStandingSource
{
    public const string SaveKey = "seraphhorizons:standing";
    public const string HarmonyId = "seraphhorizons.standing";
    public static readonly AssetLocation RulesAsset = new("seraphhorizons", "config/standing-tiers.json");
    /// <summary>How far a trader's own camp may be from it for the trader to be the camp's.</summary>
    public const int CampReach = 96;
    public const string TraderIdAttr = "seraphhorizons:traderid";

    private ICoreServerAPI? _sapi;
    private TradingSystem? _trading;
    private Harmony? _harmony;

    public static StandingSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<StandingSystem>();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override double ExecuteOrder() => 0.61;

    public bool Enabled { get; private set; }
    public StandingLedger Ledger { get; private set; } = new(new StandingRules());
    public StandingRules Rules => Ledger.Rules;
    public double SpilloverRadius { get; private set; } = 6000;
    /// <summary>Whether the group patches bound (else companies sync lazily).</summary>
    public bool GroupHooksBound { get; private set; }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        var config = SeraphHorizonsSystem.ConfigFor(api);
        if (!config.TraderStanding)
        {
            api.Logger.Notification("[seraphhorizons] Trader standing: off (TraderStanding in ModConfig/{0})", SeraphHorizonsSystem.ConfigFile);
            return;
        }
        _trading = TradingSystem.Of(api);
        if (_trading is null)
        {
            api.Logger.Warning("[seraphhorizons] Trader standing: no trading system; off");
            return;
        }
        var rules = LoadRules(api);
        if (rules is null) return;
        Ledger = new StandingLedger(rules);
        SpilloverRadius = Math.Max(0, config.TraderStandingSpilloverKm) * 1000;
        Enabled = true;
        _trading.Standing = this;
        api.Event.SaveGameLoaded += Load;
        api.Event.GameWorldSave += Save;
        StandingCommands.Register(api, this);
        _harmony = new Harmony(HarmonyId);
        GroupHooksBound = GroupHooks.Patch(_harmony, this, api.Logger);
        api.Logger.Notification("[seraphhorizons] Trader standing: on, {0} tiers, spillover {1} within {2} blocks; companies sync {3}",
            rules.Tiers.Count, rules.SpilloverShare, SpilloverRadius, GroupHooksBound ? "on join, leave and disband" : "at each use");
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        GroupHooks.Owner = null;
    }

    private static StandingRules? LoadRules(ICoreServerAPI api)
    {
        var asset = api.Assets.TryGet(RulesAsset);
        if (asset is null)
        {
            api.Logger.Warning("[seraphhorizons] Trader standing: {0} is missing; off", RulesAsset);
            return null;
        }
        try
        {
            var rules = JObject.Parse(asset.ToText(), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }).ToObject<StandingRules>()
                        ?? throw new InvalidDataException("empty");
            var problems = rules.Problems();
            if (problems.Count > 0)
            {
                api.Logger.Warning("[seraphhorizons] Trader standing: {0}: {1}; off", RulesAsset, string.Join("; ", problems));
                return null;
            }
            return rules;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Trader standing: {0} does not parse ({1}); off", RulesAsset, e.Message);
            return null;
        }
    }

    private void Load()
    {
        var api = _sapi!;
        try
        {
            var data = api.WorldManager.SaveGame.GetData(SaveKey);
            var state = data is null ? null : JsonConvert.DeserializeObject<StandingState>(System.Text.Encoding.UTF8.GetString(data));
            Ledger = new StandingLedger(Ledger.Rules, state);
        }
        catch (Exception e)
        {
            // Kept apart, not overwritten, so an admin can recover it.
            api.Logger.Error("[seraphhorizons] Trader standing: the saved standing does not load ({0}); starting afresh, the old data stays under {1}.broken",
                e.Message, SaveKey);
            api.WorldManager.SaveGame.StoreData(SaveKey + ".broken", api.WorldManager.SaveGame.GetData(SaveKey));
            Ledger = new StandingLedger(Ledger.Rules);
        }
    }

    private void Save()
    {
        string json = JsonConvert.SerializeObject(Ledger.State);
        _sapi!.WorldManager.SaveGame.StoreData(SaveKey, System.Text.Encoding.UTF8.GetBytes(json));
    }

    private double Day => _sapi!.World.Calendar.TotalDays;

    // ---- Companies ----

    /// <summary>The player's groups in the order the server holds them (the order they joined),
    /// and whether a group uid exists.</summary>
    public IReadOnlyList<int> Memberships(string playerUid) =>
        _sapi?.PlayerData.GetPlayerDataByUid(playerUid)?.PlayerGroupMemberships?.Keys.ToList() ?? [];

    public bool GroupExists(int uid) => _sapi?.Groups.PlayerGroupsById.ContainsKey(uid) == true;

    /// <summary>The player's company now, with the records brought up to date (merge on joining,
    /// forget on leaving).</summary>
    public int? CompanyOf(string playerUid)
    {
        var company = Ledger.Companies.Resolve(playerUid, Memberships(playerUid), GroupExists);
        Ledger.Companies.Sync(playerUid, company, Day);
        return company;
    }

    internal void OnJoined(string playerUid, int group)
    {
        Ledger.Companies.Joined(playerUid, group, Memberships(playerUid), GroupExists);
        CompanyOf(playerUid);
    }

    internal void OnLeft(string playerUid) => CompanyOf(playerUid);

    internal void OnDisbanded(int group) => Ledger.Companies.Disbanded(group);

    /// <summary>Chooses the player's company; null when they are not in that group.</summary>
    public int? Designate(string playerUid, int group)
    {
        if (!Memberships(playerUid).Contains(group) || !GroupExists(group)) return null;
        Ledger.Companies.Designate(playerUid, group);
        return CompanyOf(playerUid);
    }

    // ---- Traders ----

    public string TraderIdOf(EntitySeraphTrader trader)
    {
        string id = trader.WatchedAttributes.GetString(TraderIdAttr, "");
        if (TraderIds.IsValid(id)) return id;
        id = TraderIds.Entity(trader.EntityId);
        var cell = TraderGrid.CellOf((int)trader.Pos.X, (int)trader.Pos.Z);
        if (_trading?.Camps?.Registry.Get(cell) is { Status: CampStatus.Placed } camp
            && Math.Abs(camp.X - trader.Pos.X) <= CampReach && Math.Abs(camp.Z - trader.Pos.Z) <= CampReach)
            id = TraderIds.Camp(cell.X, cell.Z);
        trader.WatchedAttributes.SetString(TraderIdAttr, id);
        return id;
    }

    /// <summary>The ids of the other traders of the trader's type within the spillover radius: the
    /// grid's placed camps (traders outside camps are not known anywhere, so only camps spill).</summary>
    public List<string> NeighboursOf(string traderId, string type, double x, double z)
    {
        if (_trading?.Camps?.Registry is not { } registry || SpilloverRadius <= 0) return [];
        var sites = registry.Snapshot().Where(r => r.Status == CampStatus.Placed)
            .Select(r => new TraderSite(TraderIds.Camp(r.CellX, r.CellZ), r.Type, r.X, r.Z));
        return Spillover.Neighbours(new TraderSite(traderId, type, x, z), sites, SpilloverRadius).Select(s => s.Id).ToList();
    }

    public StandingView ViewFor(string playerUid, EntitySeraphTrader trader)
    {
        string id = TraderIdOf(trader);
        return Ledger.View(playerUid, CompanyOf(playerUid), id, NeighboursOf(id, trader.TraderType, trader.Pos.X, trader.Pos.Z));
    }

    /// <summary>A view by trader id, for a trader that need not be loaded (a camp: spillover from
    /// its neighbours; an entity: none).</summary>
    public StandingView ViewFor(string playerUid, string traderId)
    {
        var neighbours = new List<string>();
        if (traderId.StartsWith("camp:", StringComparison.Ordinal) && CellKey.TryParse(traderId["camp:".Length..], out var cell)
            && _trading?.Camps?.Registry.Get(cell) is { Status: CampStatus.Placed } camp)
            neighbours = NeighboursOf(traderId, camp.Type, camp.X, camp.Z);
        return Ledger.View(playerUid, CompanyOf(playerUid), traderId, neighbours);
    }

    public int TierFor(IPlayer player, EntitySeraphTrader trader) => ViewFor(player.PlayerUID, trader).TierIndex;

    public TierUnlocks UnlocksFor(IPlayer player, EntitySeraphTrader trader) => ViewFor(player.PlayerUID, trader).Tier.Unlocks;

    public double PriceFactorFor(IPlayer player, EntitySeraphTrader trader, PriceSide side)
    {
        var unlocks = UnlocksFor(player, trader);
        return side == PriceSide.PlayerBuys ? unlocks.BuyPriceFactor : unlocks.SellPriceFactor;
    }

    public double WalletFactorFor(EntitySeraphTrader trader)
    {
        string id = TraderIdOf(trader);
        double best = Rules.Tier(0).Unlocks.WalletFactor;
        foreach (string player in Ledger.RecentPlayers(id, Day - Rules.RecentDays).ToList())
            best = Math.Max(best, ViewFor(player, trader).Tier.Unlocks.WalletFactor);
        return best;
    }

    public int ShelfTierFor(EntitySeraphTrader trader)
    {
        string id = TraderIdOf(trader);
        int best = 0;
        foreach (string player in Ledger.RecentPlayers(id, Day - Rules.RecentDays).ToList())
            best = Math.Max(best, ViewFor(player, trader).TierIndex);
        return best;
    }

    public TierUnlocks UnlocksOfTier(int tier) => Rules.Tier(tier).Unlocks;

    public void OnDeal(IPlayer player, EntitySeraphTrader trader, int gearsPaid, int gearsReceived)
    {
        var before = ViewFor(player.PlayerUID, trader);
        Ledger.OnDeal(player.PlayerUID, before.CompanyUid, TraderIdOf(trader), gearsPaid, gearsReceived, Day);
        var after = ViewFor(player.PlayerUID, trader);
        SeraphHorizons.Mod.Admin.AdminLogs.Trade?.Write("standing", $"{player.PlayerName} deal at {TraderIdOf(trader)} ({gearsPaid} paid, {gearsReceived} received): {before.Effective:0} -> {after.Effective:0}");
        if (after.TierIndex > before.TierIndex && player is IServerPlayer sp)
            sp.SendMessage(GlobalConstants.GeneralChatGroup, StandingText.TierUp(trader, after), EnumChatType.Notification);
    }

    public void OnOrderDone(string playerUid, string traderId) =>
        Ledger.OnOrderDone(playerUid, CompanyOf(playerUid), traderId, Day);

    public void OnDeliveryDone(string playerUid, string fromTraderId, string toTraderId, bool bothEnds) =>
        Ledger.OnDeliveryDone(playerUid, CompanyOf(playerUid), fromTraderId, toTraderId, bothEnds, Day);

    public void OnDeliveryFailed(string playerUid, string fromTraderId) =>
        Ledger.OnDeliveryFailed(playerUid, CompanyOf(playerUid), fromTraderId, Day);

    public void OnOrderAbandoned(string playerUid, string traderId) =>
        Ledger.OnOrderAbandoned(playerUid, CompanyOf(playerUid), traderId, Day);
}

/// <summary>The player-facing lines (lang keys <c>trading-standing-*</c>).</summary>
public static class StandingText
{
    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    public static string TierName(StandingTier tier) => L("trading-standing-tier-" + tier.Code);

    public static string TypeName(EntitySeraphTrader trader) => L("trading-type-" + trader.TraderType);

    public static string TierUp(EntitySeraphTrader trader, StandingView v) =>
        L("trading-standing-tierup", TypeName(trader), TierName(v.Tier));
}
