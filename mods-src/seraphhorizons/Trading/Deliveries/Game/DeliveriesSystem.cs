using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Orders;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Deliveries;

/// <summary>
/// Deliveries between camps (#454), switch <see cref="SeraphHorizonsConfig.TraderDeliveries"/>;
/// the package item's class is registered on both sides whatever the switch.
///
/// <list type="bullet">
/// <item>The trade window's Deliveries tab shows a trader's offer to the player: a placed camp within
/// <c>deliveryScale</c> × 3 km (<see cref="DeliveryPlanner.Destination"/>), of another type where
/// there is one, with a deadline from the walk there, a value, a deposit and a fee
/// (<see cref="DeliveryPlanner.Offer"/>). The offer is seeded by the trader, the player and the
/// day, so it stays put until the next day. Taking it (<see cref="Begin"/>) takes the deposit from the
/// player's gears and hands over a <c>seraphhorizons:package</c>.</item>
/// <item>Handing it in at the receiver's window (<see cref="HandIn"/>): on time, the deposit back plus the fee
/// (new money, not the receiver's wallet) and standing at both ends; late (within
/// <see cref="DeliveryPlanner.GraceDays"/>), the deposit and half the fee, standing at the receiver.</item>
/// <item>Past the grace (<see cref="Tick"/>): failed, the deposit kept, standing with the sender
/// lost; the package in the player's inventory turns to junk now if they are online, else when they
/// next join.</item>
/// </list>
///
/// Deadline: <see cref="DeliveryPlanner.DaysPerKm"/> game day a km of the straight way, at least
/// <see cref="DeliveryPlanner.MinDays"/> (<see cref="DeliveryPlanner.DeadlineDays"/>), in game time,
/// so the calendar's speed does not enter. A delivery keeps the deadline it was made with.
///
/// Saved with the world (<see cref="SaveKey"/>). Destinations come from the camp grid's placed
/// camps; a world without the grid has none, so its traders offer no deliveries (admins can still
/// make one between any two loaded traders).
/// </summary>
public class DeliveriesSystem : ModSystem
{
    public const string SaveKey = "seraphhorizons:deliveries";

    private ICoreServerAPI? _sapi;
    private TradingSystem? _trading;
    private EconomySystem? _economy;
    private long _tick;

    public static DeliveriesSystem? Of(ICoreAPI api) => api.ModLoader.GetModSystem<DeliveriesSystem>();

    public override double ExecuteOrder() => 0.66;

    public bool Enabled { get; private set; }

    public DeliveryBook Book { get; private set; } = new();

    private double Today => _sapi!.World.Calendar.TotalDays;

    public override void Start(ICoreAPI api) => api.RegisterItemClass(ItemPackage.ClassName, typeof(ItemPackage));

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        if (!SeraphHorizonsSystem.ConfigFor(api).TraderDeliveries)
        {
            api.Logger.Notification("[seraphhorizons] Trader deliveries: off (TraderDeliveries in ModConfig/{0})", SeraphHorizonsSystem.ConfigFile);
            return;
        }
        _trading = TradingSystem.Of(api);
        if (_trading is null)
        {
            api.Logger.Warning("[seraphhorizons] Trader deliveries: no trading system; off");
            return;
        }
        _economy = EconomySystem.Of(api);
        Enabled = true;
        api.Event.SaveGameLoaded += Load;
        api.Event.GameWorldSave += Save;
        api.Event.PlayerJoin += MarkFailedPackages;
        if (_economy != null) _economy.SimulatedDay += OnSimulatedDay;
        _tick = api.Event.RegisterGameTickListener(_ => Tick(), 5000);
        DeliveryCommands.Register(api, this);
        api.Logger.Notification("[seraphhorizons] Trader deliveries: on");
    }

    public override void Dispose()
    {
        if (_economy != null) _economy.SimulatedDay -= OnSimulatedDay;
        if (_sapi != null && _tick != 0) _sapi.Event.UnregisterGameTickListener(_tick);
    }

    private void Load()
    {
        var api = _sapi!;
        try
        {
            var json = api.WorldManager.SaveGame.GetData<string>(SaveKey);
            Book = json is null ? new DeliveryBook() : DeliveryBook.FromJson(json);
        }
        catch (Exception e)
        {
            api.Logger.Error("[seraphhorizons] Trader deliveries: the saved deliveries do not load ({0}); starting afresh, the old data stays under {1}.broken",
                e.Message, SaveKey);
            api.WorldManager.SaveGame.StoreData(SaveKey + ".broken", api.WorldManager.SaveGame.GetData(SaveKey));
            Book = new DeliveryBook();
        }
    }

    private void Save() => _sapi!.WorldManager.SaveGame.StoreData(SaveKey, Book.ToJson());

    private void OnSimulatedDay(int day)
    {
        Book.Advance(1);
        Tick();
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    // ---- Where traders are ----

    /// <summary>The grid's placed camps, by their standing ids.</summary>
    public List<TraderSite> Camps() =>
        _trading?.Camps?.Registry.Snapshot().Where(r => r.Status == CampStatus.Placed)
            .Select(r => new TraderSite(TraderIds.Camp(r.CellX, r.CellZ), r.Type, r.X, r.Z)).ToList() ?? [];

    public TraderSite SiteOf(EntitySeraphTrader trader) =>
        new(TraderFinder.IdOf(_sapi!, trader), trader.TraderType, trader.Pos.X, trader.Pos.Z);

    /// <summary>A trader by id: loaded, or a placed camp.</summary>
    public TraderSite? SiteOf(string id)
    {
        if (TraderFinder.ById(_sapi!, id) is { } t) return SiteOf(t);
        return Camps().FirstOrDefault(s => s.Id == id) is { Id: not null } s ? s : null;
    }

    // ---- Offers ----

    public double ScaleFor(IPlayer player, EntitySeraphTrader trader)
    {
        var standing = _trading!.Standing;
        return standing.Enabled ? standing.UnlocksFor(player, trader).DeliveryScale : 1;
    }

    private double[] Rolls(string from, string playerUid, int n)
    {
        int day = (int)Math.Floor(Today);
        return Enumerable.Range(0, n).Select(i => StableHash.Unit(_sapi!.World.Seed, "delivery:" + from + ":" + playerUid, day, 0, i)).ToArray();
    }

    /// <summary>The trader's offer to the player today, or the lang key saying why there is none.</summary>
    public (DeliveryOffer? Offer, string? Why) OfferFor(IPlayer player, EntitySeraphTrader trader)
    {
        var from = SiteOf(trader);
        if (Book.ActiveFrom(player.PlayerUID, from.Id) != null) return (null, "trading-deliveries-already");
        double scale = ScaleFor(player, trader);
        if (scale <= 0) return (null, "trading-deliveries-notyet");
        var rolls = Rolls(from.Id, player.PlayerUID, 4);
        if (DeliveryPlanner.Destination(from, Camps(), scale, rolls[0]) is not { } to) return (null, "trading-deliveries-nowhere");
        return (DeliveryPlanner.Offer(from, to, scale, rolls[1..]), null);
    }

    /// <summary>Takes the deposit, records the delivery and hands over its package; the error's lang
    /// key, or null.</summary>
    public string? Begin(IServerPlayer player, DeliveryOffer offer, out Delivery? delivery)
    {
        delivery = null;
        if (Book.ActiveFrom(player.PlayerUID, offer.From.Id) != null) return "trading-deliveries-already";
        if (InventoryTrader.GetPlayerAssets(player.Entity) < offer.Deposit) return "trading-deliveries-nodeposit";
        InventoryTrader.DeductFromEntity(_sapi!, player.Entity, offer.Deposit);
        delivery = Book.Create(offer, player.PlayerUID, player.PlayerName, Today);
        var stack = new ItemStack(_sapi!.World.GetItem(ItemPackage.PackageCode), 1);
        var a = stack.Attributes;
        a.SetInt(ItemPackage.AttrId, delivery.Id);
        a.SetString(ItemPackage.AttrFrom, delivery.From);
        a.SetString(ItemPackage.AttrTo, delivery.To);
        a.SetString(ItemPackage.AttrToType, delivery.ToType);
        a.SetDouble(ItemPackage.AttrToX, delivery.ToX);
        a.SetDouble(ItemPackage.AttrToZ, delivery.ToZ);
        a.SetDouble(ItemPackage.AttrDeadline, delivery.Deadline);
        if (!player.InventoryManager.TryGiveItemstack(stack, true))
            _sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        return null;
    }

    // ---- Handing in ----

    /// <summary>The player's slots holding a package of this delivery.</summary>
    public static IEnumerable<ItemSlot> PackageSlots(IPlayer player, int deliveryId) =>
        player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName != GlobalConstants.creativeInvClassName)
            .SelectMany(inv => inv)
            .Where(slot => ItemPackage.IdOf(slot.Itemstack) == deliveryId && deliveryId != 0);

    /// <summary>Hands in the player's package for this trader; the error's lang key, or null.</summary>
    public string? HandIn(IServerPlayer player, EntitySeraphTrader trader)
    {
        string id = TraderFinder.IdOf(_sapi!, trader);
        var mine = Book.All.Where(d => d.IsActive && d.PlayerUid == player.PlayerUID && d.To == id)
            .FirstOrDefault(d => PackageSlots(player, d.Id).Any(s => !s.Itemstack!.Attributes.GetBool(ItemPackage.AttrFailed)));
        if (mine is null) return "trading-deliveries-handin-none";
        if (Book.HandIn(mine.Id, player.PlayerUID, id, Today) is not { } change) return "trading-deliveries-handin-none";
        var slot = PackageSlots(player, mine.Id).First();
        slot.TakeOut(1);
        slot.MarkDirty();
        Settle(change);
        return null;
    }

    /// <summary>Pays out a settled or failed delivery and applies its standing call.</summary>
    public void Settle(DeliveryChange change)
    {
        var d = change.Delivery;
        var player = _sapi!.World.PlayerByUid(d.PlayerUid) as IServerPlayer;
        bool online = player?.Entity != null && player.ConnectionState == EnumClientState.Playing;
        int fee = 0;
        if (!change.Standing.Failed)
        {
            // The fee is new money, as an order's payout: never the receiver's wallet.
            fee = change.Fee;
            if (online) TraderFinder.GiveGears(_sapi, player!.Entity, change.DepositBack + fee);
        }
        var s = change.Standing;
        if (s.Failed) _trading!.Standing.OnDeliveryFailed(s.PlayerUid, s.From);
        else _trading!.Standing.OnDeliveryDone(s.PlayerUid, s.From, s.To, s.BothEnds);
        if (!online) return;
        string key = change.To switch
        {
            DeliveryState.OnTime => "trading-deliveries-ontime",
            DeliveryState.Late => "trading-deliveries-late",
            _ => "trading-deliveries-failed",
        };
        player!.SendMessage(GlobalConstants.GeneralChatGroup, L(key, d.Id, TraderFinder.TypeName(d.ToType), d.Deposit, fee), EnumChatType.Notification);
        if (s.Failed) MarkFailedPackages(player);
    }

    // ---- Time ----

    public void Tick()
    {
        if (!Enabled || _sapi is null) return;
        foreach (var change in Book.Tick(Today)) Settle(change);
    }

    /// <summary>Turns the player's packages of deliveries that are no longer active to junk.</summary>
    private void MarkFailedPackages(IServerPlayer player)
    {
        if (!Enabled) return;
        foreach (var slot in player.InventoryManager.Inventories.Values
                     .Where(inv => inv.ClassName != GlobalConstants.creativeInvClassName).SelectMany(inv => inv))
        {
            int id = ItemPackage.IdOf(slot.Itemstack);
            if (id == 0 || slot.Itemstack!.Attributes.GetBool(ItemPackage.AttrFailed)) continue;
            if (Book.Get(id) is { State: DeliveryState.Active }) continue;
            slot.Itemstack.Attributes.SetBool(ItemPackage.AttrFailed, true);
            slot.MarkDirty();
        }
    }
}
