using HarmonyLib;
using ProtoBuf;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Deliveries;
using SeraphHorizons.Mod.Trading.Deliveries.Core;
using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Orders;
using SeraphHorizons.Mod.Trading.Orders.Core;
using SeraphHorizons.Mod.Trading.Standing;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>The trade window's one message: its kind and a JSON body (<see cref="TradeWindowState"/>,
/// <see cref="TradeRequest"/> or <see cref="TradeResult"/>), so the wire format is the Core's, tested
/// without the game.</summary>
[ProtoContract]
public sealed class TradeWindowPacket
{
    public const int State = 1, DialogueState = 2, Result = 3, Request = 4;

    [ProtoMember(1)] public int Kind { get; set; }
    [ProtoMember(2)] public long TraderId { get; set; }
    [ProtoMember(3)] public string Json { get; set; } = "";
}

/// <summary>
/// The pack's trade window (the playtest after #436): the network between it and the server, the
/// server's side of every action, and the client's hooks.
///
/// <list type="bullet">
/// <item><b>Server</b>: every request (<see cref="TradeRequest"/>) passes <see cref="TradeGuard"/> (the
/// trading player, next to a live trader) and is handled by the features' own code: buying and
/// selling one unit through vanilla's deal (<see cref="EntitySeraphTrader.BuyUnit"/>,
/// <see cref="EntitySeraphTrader.SellUnit"/>), orders (<see cref="OrdersSystem.Accept"/>,
/// <see cref="OrdersSystem.HandIn"/>), deliveries (<see cref="DeliveriesSystem.Begin"/>,
/// <see cref="DeliveriesSystem.HandIn"/>, a waypoint). The answer (<see cref="TradeResult"/>) and the
/// window's state (<see cref="BuildState"/>) go back after each, and the state when the window opens
/// and when a conversation with the trader starts (the dialogue's standing reply reads it).</item>
/// <item><b>Client</b>: keeps the last state per trader, hands it and the answers to the open window
/// (<see cref="GuiDialogSeraphTrade"/>); with Harmony, writes the dialogue's standing reply
/// (<c>DlgTalkComponent.genText</c>, the component <see cref="StandingComponent"/>) and adds what
/// the trader pays to the tooltip of the player's own items while the window is open
/// (<c>ItemSlot.GetStackDescription</c>).</item>
/// </list>
/// </summary>
public class TradeWindowSystem : ModSystem
{
    public const string Channel = "seraphhorizons-trade";
    public const string HarmonyId = "seraphhorizons.tradewindow";

    /// <summary>The dialogue component (the pack's trader dialogue and BetterRuins' two, patched) whose
    /// text is the trader's view of the player's standing.</summary>
    public const string StandingComponent = "seraphhorizons-standing";

    /// <summary>The entity variable (scope <c>entity</c>) the dialogue's standing option is conditioned
    /// on: set to <c>on</c> on every pack trader while standing is on.</summary>
    public const string StandingVariable = "shstanding";

    private ICoreAPI? _api;
    private ICoreServerAPI? _sapi;
    private ICoreClientAPI? _capi;
    private Harmony? _harmony;
    private readonly Dictionary<long, TradeWindowState> _states = new();

    public static TradeWindowSystem? Of(ICoreAPI? api) => api?.ModLoader.GetModSystem<TradeWindowSystem>();

    public override double ExecuteOrder() => 0.69;

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(Channel).RegisterMessageType<TradeWindowPacket>();
    }

    // ---- Server ----

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        api.Network.GetChannel(Channel).SetMessageHandler<TradeWindowPacket>(OnClientPacket);
    }

    private void OnClientPacket(IServerPlayer player, TradeWindowPacket packet)
    {
        if (packet.Kind != TradeWindowPacket.Request) return;
        TradeRequest request;
        try
        {
            request = TradeRequest.FromJson(packet.Json);
        }
        catch (Exception)
        {
            return;
        }
        if (_sapi!.World.GetEntityById(packet.TraderId) is not EntitySeraphTrader trader)
        {
            Send(player, TradeWindowPacket.Result, packet.TraderId, TradeResult.Refused(request.Action, TradeGuard.Key(GuardRefusal.Gone)).ToJson());
            return;
        }
        var result = Handle(player, trader, request);
        Send(player, TradeWindowPacket.Result, trader.EntityId, result.ToJson());
        SendState(player, trader);
    }

    private void Send(IServerPlayer player, int kind, long trader, string json) =>
        _sapi?.Network.GetChannel(Channel).SendPacket(new TradeWindowPacket { Kind = kind, TraderId = trader, Json = json }, player);

    /// <summary>Sends the player the window's state for this trader (for the open window, or for the
    /// dialogue's standing reply when a conversation starts).</summary>
    public void SendState(IServerPlayer player, EntitySeraphTrader trader, bool forDialogue = false)
    {
        if (_sapi is null || player.ConnectionState != EnumClientState.Playing) return;
        Send(player, forDialogue ? TradeWindowPacket.DialogueState : TradeWindowPacket.State, trader.EntityId, BuildState(player, trader).ToJson());
    }

    /// <summary>What the player asked of the trader through the window, done or refused.</summary>
    public TradeResult Handle(IServerPlayer player, EntitySeraphTrader trader, TradeRequest request)
    {
        var action = request.Action;
        var guard = TradeGuard.Check(trader.Alive, trader.WatchedAttributes.GetString("tradingPlayerUID"), player.PlayerUID,
            player.Entity.Pos.SquareDistanceTo(trader.Pos));
        if (guard != GuardRefusal.None) return TradeResult.Refused(action, TradeGuard.Key(guard));
        switch (action)
        {
            case TradeAction.Refresh:
                return TradeResult.Done(action);
            case TradeAction.Buy:
                return Unit(action, trader.BuyUnit(player, request.Slot, request.Code, request.Price, (shelf, unit) => BeforeBuy(player, trader, shelf, unit)));
            case TradeAction.Sell:
                return Unit(action, trader.SellUnit(player));
            case TradeAction.TakeOrder:
            {
                if (OrdersSystem.Of(_sapi!) is not { Enabled: true } orders) return TradeResult.Refused(action, "trading-window-off");
                return orders.Accept(player, trader, request.Id) is { } key
                    ? TradeResult.Refused(action, key, request.Id)
                    : TradeResult.Done(action, "trading-window-order-took", request.Id);
            }
            case TradeAction.HandInOrder:
            {
                if (OrdersSystem.Of(_sapi!) is not { Enabled: true } orders) return TradeResult.Refused(action, "trading-window-off");
                return orders.HandIn(player, trader, request.Id) is { } e ? TradeResult.Refused(action, e.Key, e.Args) : TradeResult.Done(action);
            }
            case TradeAction.TakeDelivery:
            {
                if (DeliveriesSystem.Of(_sapi!) is not { Enabled: true } deliveries) return TradeResult.Refused(action, "trading-window-off");
                var (offer, why) = deliveries.OfferFor(player, trader);
                if (offer is null) return TradeResult.Refused(action, why ?? "trading-deliveries-nowhere");
                // The package needs a slot of its own; no room, no deposit taken.
                if (_sapi!.World.GetItem(ItemPackage.PackageCode) is { } package && !HasRoom(player, new ItemStack(package)))
                    return TradeResult.Refused(action, TradeGuard.NoRoomKey);
                return deliveries.Begin(player, offer, out var d) is { } error
                    ? TradeResult.Refused(action, error, offer.Deposit)
                    : TradeResult.Done(action, "trading-window-delivery-taken", d!.Id);
            }
            case TradeAction.HandInDelivery:
            {
                if (DeliveriesSystem.Of(_sapi!) is not { Enabled: true } deliveries) return TradeResult.Refused(action, "trading-window-off");
                return deliveries.HandIn(player, trader) is { } error ? TradeResult.Refused(action, error) : TradeResult.Done(action);
            }
            case TradeAction.MarkDelivery:
                return MarkDelivery(player, trader, request.Id);
        }
        return TradeResult.Refused(action, "trading-window-failed");
    }

    /// <summary>A buy's checks before any gears move: a map or lead the player may not have (sold,
    /// gone, above their standing, or one they have already: <see cref="MapsSystem.Refusal"/>), then
    /// room in their bags for the very stack (<see cref="HasRoom"/>). A refusal ends the hold.</summary>
    private EntitySeraphTrader.UnitDeal? BeforeBuy(IServerPlayer player, EntitySeraphTrader trader, ItemSlotTrade shelf, ItemStack unit)
    {
        if (unit.Attributes.GetString(MapOfferAttrs.Offer) != null
            && _sapi!.ModLoader.GetModSystem<MapsSystem>() is { Active: true } maps
            && maps.Refusal(trader, player, unit, shelf) is { } refusal)
            return new EntitySeraphTrader.UnitDeal(EnumTransactionResult.Failure, refusal.Key, refusal.Args);
        if (!HasRoom(player, unit)) return new EntitySeraphTrader.UnitDeal(EnumTransactionResult.Failure, TradeGuard.NoRoomKey);
        return null;
    }

    /// <summary>Whether the whole <paramref name="stack"/> fits in the player's hotbar and backpack
    /// (bags included) as the game would give it: empty slots that hold it and room in stacks it
    /// merges with (<see cref="TradeGuard.Fits"/>).</summary>
    public static bool HasRoom(IPlayer player, ItemStack stack) => TradeGuard.Fits(stack.StackSize, RoomFor(player, stack));

    private static IEnumerable<int> RoomFor(IPlayer player, ItemStack stack)
    {
        var source = new DummySlot(stack);
        foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (player.InventoryManager.GetOwnInventory(name) is not { } inv) continue;
            foreach (var slot in inv)
            {
                int limit = Math.Min(stack.Collectible.MaxStackSize, slot.MaxSlotStackSize);
                if (slot.Empty) yield return slot.CanHold(source) ? limit : 0;
                else if (slot.Itemstack.Collectible.GetMergableQuantity(slot.Itemstack, stack, EnumMergePriority.AutoMerge) > 0)
                    yield return Math.Max(0, limit - slot.Itemstack.StackSize);
                else yield return 0;
            }
        }
    }

    private static TradeResult Unit(TradeAction action, EntitySeraphTrader.UnitDeal deal) =>
        deal.Ok ? TradeResult.Done(action) : TradeResult.Refused(action, deal.Key ?? "trading-window-failed", deal.Args ?? []);

    /// <summary>A waypoint at the delivery offer's destination (<paramref name="id"/> 0) or the
    /// player's delivery <paramref name="id"/>'s.</summary>
    private TradeResult MarkDelivery(IServerPlayer player, EntitySeraphTrader trader, int id)
    {
        var action = TradeAction.MarkDelivery;
        if (DeliveriesSystem.Of(_sapi!) is not { Enabled: true } deliveries) return TradeResult.Refused(action, "trading-window-off");
        double x, z;
        string type;
        if (id == 0)
        {
            if (deliveries.OfferFor(player, trader).Offer is not { } offer) return TradeResult.Refused(action, "trading-deliveries-nowhere");
            (x, z, type) = (offer.To.X, offer.To.Z, offer.To.Type);
        }
        else
        {
            if (deliveries.Book.Get(id) is not { IsActive: true } d || d.PlayerUid != player.PlayerUID) return TradeResult.Refused(action, "trading-deliveries-notactive", id);
            (x, z, type) = (d.ToX, d.ToZ, d.ToType);
        }
        string lang = player.LanguageCode ?? Lang.DefaultLocale;
        string title = Lang.GetL(lang, "seraphhorizons:trading-window-waypoint", Lang.GetL(lang, "seraphhorizons:trading-type-" + type));
        var pos = new Vec3d(x + 0.5, _sapi!.World.SeaLevel, z + 0.5);
        var layer = _sapi.ModLoader.GetModSystem<WorldMapManager>()?.MapLayers.OfType<WaypointMapLayer>().FirstOrDefault();
        if (!_sapi.World.Config.GetBool("allowMap", true) || layer is null)
        {
            var dist = pos.Clone().Sub(player.Entity.Pos.XYZ);
            dist.Y = 0;
            return TradeResult.Done(action, "trading-maps-lead-distance", title, (int)dist.Length());
        }
        if (layer.Waypoints.Any(w => w.OwningPlayerUid == player.PlayerUID && w.Position.X == pos.X && w.Position.Z == pos.Z))
            return TradeResult.Done(action, "oremap-marked-already");
        layer.AddWaypoint(new Waypoint
        {
            Color = ColorUtil.ColorFromRgba(220, 170, 60, 255),
            Icon = "trader",
            Pinned = true,
            Position = pos,
            OwningPlayerUid = player.PlayerUID,
            Title = title,
        }, player);
        return TradeResult.Done(action, "trading-maps-lead-marked", title);
    }

    /// <summary>Everything the window shows about this trader beyond its shelves, for this player.</summary>
    public TradeWindowState BuildState(IServerPlayer player, EntitySeraphTrader trader)
    {
        var api = _sapi!;
        var trading = TradingSystem.Of(api);
        var standing = trading?.Standing ?? NoStanding.Instance;
        var economy = EconomySystem.Of(api);
        var orders = OrdersSystem.Of(api);
        var deliveries = DeliveriesSystem.Of(api);
        var maps = api.ModLoader.GetModSystem<MapsSystem>();
        var state = new TradeWindowState
        {
            TraderId = trader.EntityId,
            Switches = new WindowSwitches
            {
                Standing = standing.Enabled,
                Orders = orders?.Enabled == true,
                Deliveries = deliveries?.Enabled == true,
                Maps = maps?.Active == true,
                EverythingPriced = economy?.EverythingHasAPrice == true,
                StandingPrices = economy is { EverythingHasAPrice: true } or { RegionalSupply: true },
            },
        };
        string id = TraderFinder.IdOf(api, trader);
        double today = api.World.Calendar.TotalDays;
        int tier = 0;
        if (standing is StandingSystem { Enabled: true } system)
        {
            var view = system.ViewFor(player.PlayerUID, trader);
            tier = view.TierIndex;
            state.Standing = new StandingSummary
            {
                TierIndex = view.TierIndex,
                Points = view.Effective,
                Spill = view.Spill,
                Company = view.Company is double c && c > view.Personal ? c : null,
                Earn = system.Rules.Points,
                Tiers = system.Rules.Tiers.Select(t => new TierView
                {
                    Code = t.Code, Points = t.Points, Unlocks = t.Unlocks, MapPrecision = MapOffers.MaxPrecision(t.Unlocks.MapTier),
                }).ToList(),
            };
            var leads = system.Rules.Tiers.Select(t => t.Unlocks.MapsToTraders).ToList();
            state.LeadsToTraders = view.Tier.Unlocks.MapsToTraders;
            state.LeadsTier = LockedStock.FirstTier(leads);
            if (trading?.Lists?.For(trader.TraderType) is { } def)
            {
                var shelved = (trader.WatchedAttributes[EntitySeraphTrader.SellingKeysAttr] as Vintagestory.API.Datastructures.StringArrayAttribute)?.value ?? [];
                state.Locked = LockedStock.Of(def.Selling, trader.Region, tier, system.Rules.Tiers.Select(t => t.Unlocks.RareStock).ToList(),
                    shelved.Where(k => k.Length > 0).ToHashSet());
            }
        }
        else state.LeadsToTraders = true;

        // Map and lead offers this player has already (marked on their map, or a copy carried).
        if (maps?.Active == true && MapMarksSystem.Of(api) is { } marks)
            for (int i = 0; i < 16; i++)
                if (trader.Inventory.GetSellingSlot(i)?.Itemstack is { } offer && offer.Attributes.GetString(MapOfferAttrs.Offer) is not (null or MapOfferAttrs.SoldOut)
                    && marks.Check(player, offer) != MarkCheck.Free)
                    state.OwnedMaps.Add(i);

        if (orders is { Enabled: true })
            foreach (var o in orders.Book.OpenAt(id).Where(o => o.State == OrderState.Offered || o.PlayerUid == player.PlayerUID))
                state.Orders.Add(new OrderRow
                {
                    Id = o.Id, Item = o.Item, Quantity = o.Quantity, Delivered = o.Delivered, Lot = o.Lot, UnitPrice = o.UnitPrice,
                    Premium = o.Premium, PremiumPaid = o.PremiumPaid, DaysLeft = o.Deadline - today, Days = o.Days,
                    Mine = o.State == OrderState.Accepted, Held = o.State == OrderState.Accepted ? OrdersSystem.Carried(player, o.Item) : 0,
                });

        if (deliveries is { Enabled: true })
        {
            double hoursPerDay = api.World.Calendar.HoursPerDay;
            var (offer, why) = deliveries.OfferFor(player, trader);
            if (offer != null)
                state.DeliveryOffer = new DeliveryOfferRow
                {
                    ToType = offer.To.Type, Distance = offer.Distance, Dx = offer.To.X - trader.Pos.X, Dz = offer.To.Z - trader.Pos.Z,
                    Days = offer.Days, Deposit = offer.Deposit, Fee = offer.Fee,
                };
            else if (why != "trading-deliveries-already") state.DeliveryWhy = why;
            foreach (var d in deliveries.Book.All.Where(d => d.IsActive && d.PlayerUid == player.PlayerUID && (d.From == id || d.To == id)))
                state.Deliveries.Add(new DeliveryRow
                {
                    Id = d.Id, ToType = d.ToType, ForHere = d.To == id, Distance = d.Distance, Dx = d.ToX - trader.Pos.X, Dz = d.ToZ - trader.Pos.Z,
                    HoursLeft = (d.Deadline - today) * hoursPerDay, DaysLeft = d.Deadline - today, Deposit = d.Deposit, Fee = d.Fee,
                    Carried = DeliveriesSystem.PackageSlots(player, d.Id).Any(s => !s.Itemstack!.Attributes.GetBool(ItemPackage.AttrFailed)),
                });
        }
        return state;
    }

    /// <summary>Server: the dialogue's standing option shows on the pack's traders while standing is
    /// on (an <c>entity</c> scope variable the option's condition reads, synced with the trader).</summary>
    public static void MarkStanding(EntitySeraphTrader trader, bool on)
    {
        var tree = trader.WatchedAttributes.GetTreeAttribute("variables");
        string want = on ? "on" : "off";
        if (tree?.GetString(StandingVariable) == want) return;
        if (tree is null)
        {
            tree = new Vintagestory.API.Datastructures.TreeAttribute();
            trader.WatchedAttributes["variables"] = tree;
        }
        tree.SetString(StandingVariable, want);
        trader.WatchedAttributes.MarkPathDirty("variables");
    }

    // ---- Client ----

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        api.Network.GetChannel(Channel).SetMessageHandler<TradeWindowPacket>(OnServerPacket);
        _harmony = new Harmony(HarmonyId);
        TradeWindowPatches.Patch(_harmony, api);
    }

    private void OnServerPacket(TradeWindowPacket packet)
    {
        switch (packet.Kind)
        {
            case TradeWindowPacket.State or TradeWindowPacket.DialogueState:
            {
                TradeWindowState state;
                try
                {
                    state = TradeWindowState.FromJson(packet.Json);
                }
                catch (Exception e)
                {
                    _capi?.Logger.Warning("[seraphhorizons] Trade window: a state the client cannot read ({0})", e.Message);
                    return;
                }
                _states[packet.TraderId] = state;
                if (packet.Kind == TradeWindowPacket.State && GuiDialogSeraphTrade.Current is { } window && window.TraderId == packet.TraderId)
                    window.OnState(state);
                break;
            }
            case TradeWindowPacket.Result:
            {
                TradeResult result;
                try
                {
                    result = TradeResult.FromJson(packet.Json);
                }
                catch (Exception e)
                {
                    _capi?.Logger.Warning("[seraphhorizons] Trade window: an answer the client cannot read ({0})", e.Message);
                    return;
                }
                if (GuiDialogSeraphTrade.Current is { } window && window.TraderId == packet.TraderId) window.OnResult(result);
                else if (!result.Ok && result.Key is { } key) _capi?.TriggerIngameError(this, "seraphhorizons-trade", WindowText.Resolve(_capi, new Text(key, [.. result.Args])));
                break;
            }
        }
    }

    /// <summary>The last state the server sent for a trader, if any.</summary>
    public TradeWindowState? StateFor(long traderId) => _states.GetValueOrDefault(traderId);

    /// <summary>Client: asks the server to do something at the trader.</summary>
    public void Request(long traderId, TradeAction action, int slot = 0, int id = 0, string? code = null, int? price = null)
    {
        _capi?.Network.GetChannel(Channel).SendPacket(new TradeWindowPacket
        {
            Kind = TradeWindowPacket.Request,
            TraderId = traderId,
            Json = new TradeRequest { Action = action, Slot = slot, Id = id, Code = code, Price = price }.ToJson(),
        });
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        _states.Clear();
    }
}
