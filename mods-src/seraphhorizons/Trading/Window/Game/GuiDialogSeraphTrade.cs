using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>
/// The pack's trade window (mockup "A · Tabs"), in place of vanilla's GuiDialogTrader for the pack's
/// traders. A thin layer over <see cref="TradeWindowModel"/> and <see cref="HoldTimer"/>: it lays out
/// what they say and sends what the player holds to the server (<see cref="TradeWindowSystem.Request"/>).
///
/// <list type="bullet">
/// <item>Header: the trader's name, type and region, the player's standing tier with a bar to the
/// next ("Regular [310 / 800]"); tabs Trade, Orders (n), Deliveries (n), Maps &amp; leads, Standing
/// (a switched-off feature has none); footer: the player's gears, the trader's, its side budget.</item>
/// <item>Trade: the Sells and Buys shelves are the trader's own slots (item, price and stock on
/// hover); the goods the player's standing does not unlock yet are hatched with the tier that does.
/// No carts, no Deal button: a press selects a good, holding it buys one unit (the ring of
/// <see cref="HoldRingRenderer"/>), and holding on buys the next. The sell slot is the trader's first
/// selling-cart slot; Hold to sell sells one unit of what is in it.</item>
/// <item>Orders, Deliveries, Maps &amp; leads and Standing as their names say.</item>
/// </list>
/// It closes as vanilla's does when the player walks away (the trader's own tick) and on the
/// server's packet 1212.
/// </summary>
public sealed class GuiDialogSeraphTrade : GuiDialog
{
    /// <summary>The open window (one at a time), for the tooltip patch and the network handler.</summary>
    public static GuiDialogSeraphTrade? Current { get; private set; }

    private const int Cols = 4;
    private const string SellTarget = "sell";

    private readonly EntitySeraphTrader _trader;
    private readonly InventoryTrader _inv;
    private readonly HoldTimer _hold = new();
    private readonly List<(GuiElementItemSlotGridBase Grid, int[] Ids, string Kind)> _grids = [];
    private TradeWindowState _state;
    private WindowTab _tab = WindowTab.Trade;
    private HoldRingRenderer? _ring;
    private LockedInventory? _locked;
    private ElementBounds? _sellButton;
    private ElementBounds? _holdBounds;
    private long _tick;
    private string _layout = "";
    private int _stateVersion;
    private (string Kind, int Id) _selected = ("", -1);
    private string _status = "";

    public GuiDialogSeraphTrade(ICoreClientAPI capi, EntitySeraphTrader trader) : base(capi)
    {
        _trader = trader;
        _inv = trader.Inventory;
        _state = TradeWindowSystem.Of(capi)?.StateFor(trader.EntityId) ?? new TradeWindowState { TraderId = trader.EntityId };
        Compose();
    }

    public long TraderId => _trader.EntityId;

    public override string? ToggleKeyCombinationCode => null;

    public override bool PrefersUngrabbedMouse => false;

    public override float ZSize => 300;

    private TradeWindowSystem? System => TradeWindowSystem.Of(capi);

    private string T(Text text) => WindowText.Resolve(capi, text);

    private string L(string key, params object[] args) => T(new Text(key, args));

    private string TraderName => _trader.GetBehavior<EntityBehaviorNameTag>()?.DisplayName ?? _trader.GetName();

    private bool Female => _trader.Code.Path.Split('-') is { Length: >= 2 } parts && parts[1] == "female";

    // ---- Slots ----

    private static bool IsOffer(ItemSlot slot) => slot.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) != null;

    private int[] SellIds() => Enumerable.Range(0, 16).Where(i => _inv[i].Itemstack != null && !IsOffer(_inv[i])).ToArray();

    private int[] BuyIds() => Enumerable.Range(20, 16).Where(i => _inv[i].Itemstack != null).ToArray();

    private int[] MapIds() => Enumerable.Range(0, 16).Where(i => _inv[i].Itemstack != null && IsOffer(_inv[i])).ToArray();

    private MapOfferStatus StatusOf(int slotId)
    {
        var slot = (ItemSlotTrade)_inv[slotId];
        var a = slot.Itemstack!.Attributes;
        return TradeWindowModel.MapStatus(a.GetString(MapOfferAttrs.Offer), a.GetString(MapOfferAttrs.LeadKind), slot.TradeItem?.Stock ?? 0, _state.LeadsToTraders);
    }

    /// <summary>What decides the layout: the tab, which slots hold what, and the server's state.</summary>
    private string Layout() =>
        $"{_tab}|{string.Join(",", SellIds())}|{string.Join(",", BuyIds())}|{string.Join(",", MapIds())}|{_stateVersion}";

    // ---- Composing ----

    private void Compose()
    {
        foreach (var (grid, _, _) in _grids) grid.OnGuiClosed(capi);
        _grids.Clear();
        _sellButton = null;
        _layout = Layout();
        var tabs = TradeWindowModel.Tabs(_state);
        if (!tabs.Any(t => t.Tab == _tab)) _tab = WindowTab.Trade;

        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;
        var gridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, Cols, 4).FixedGrow(2 * pad, 2 * pad);
        double gridW = gridBounds.fixedWidth, width = 2 * gridW + 20;

        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);
        var font = CairoFont.WhiteSmallText();
        var dim = CairoFont.WhiteDetailText();
        var gui = capi.Gui.CreateCompo("seraphtrade-" + _trader.EntityId, dialog)
            .AddShadedDialogBG(bg, true)
            .AddDialogTitleBar(TraderName, () => TryClose())
            .AddHorizontalTabs(tabs.Select(t => new GuiTab { Name = T(t.Label), DataInt = (int)t.Tab }).ToArray(),
                ElementBounds.Fixed(0, -24, width, 25), OnTab, dim, dim.Clone().WithColor(GuiStyle.ActiveButtonTextColor), "tabs")
            .BeginChildElements(bg);
        gui.GetHorizontalTabs("tabs").activeElement = Math.Max(0, tabs.FindIndex(t => t.Tab == _tab));

        // Header: who, and the standing bar.
        var region = _trader.Region;
        double y = 25;
        gui.AddStaticText(T(TradeWindowModel.Header(TraderName, _trader.TraderType, region.Climate, region.Rock)), font, ElementBounds.Fixed(0, y, width, 20));
        y += 22;
        if (TradeWindowModel.Bar(_state.Standing, _state.Switches) is { } bar)
        {
            gui.AddStaticText(T(bar.Label), dim, ElementBounds.Fixed(0, y, 190, 18));
            gui.AddStaticElement(new GuiElementStandingBar(capi, ElementBounds.Fixed(195, y + 4, width - 195, 9), bar.Fraction));
            y += 22;
        }
        y += 6;

        y = _tab switch
        {
            WindowTab.Orders => ComposeOrders(gui, y, width),
            WindowTab.Deliveries => ComposeDeliveries(gui, y, width),
            WindowTab.Maps => ComposeMaps(gui, y, width),
            WindowTab.Standing => ComposeStanding(gui, y, width),
            _ => ComposeTrade(gui, y, width, gridW),
        };

        gui.AddDynamicText("", dim, ElementBounds.Fixed(0, y + 4, width, 18), "status");
        gui.AddDynamicText("", font, ElementBounds.Fixed(0, y + 24, width, 20), "footer");
        SingleComposer = gui.EndChildElements().Compose();
        // The shelves are pressed and held (OnMouseDown), never clicked into a cart; the sell slot
        // takes and gives stacks as any slot.
        foreach (var (grid, _, kind) in _grids)
            if (kind != "sellslot") grid.CanClickSlot = _ => false;
        _sellButton = gui.GetButton("sellbutton")?.Bounds;
        if (_hold.Target != null && BoundsOf(_hold.Target) is { } held) _holdBounds = held;
        else if (_hold.Target != null) _hold.Release();
        UpdateDynamic();
    }

    private void OnTab(int tab)
    {
        _tab = (WindowTab)tab;
        _hold.Release();
        _selected = ("", -1);
        Compose();
    }

    private GuiElementItemSlotGridBase AddGrid(GuiComposer gui, IInventory inv, int[] ids, int cols, double x, double y, string key, string kind)
    {
        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;
        int rows = Math.Max(1, (ids.Length + cols - 1) / cols);
        var bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, x + pad, y + pad, cols, rows).FixedGrow(2 * pad, 2 * pad);
        // Only the sell slot moves stacks, which the server must hear of (as vanilla's dialog sends
        // its slots' packets); the shelves and locked goods are read only.
        gui.AddItemSlotGrid(inv, kind == "sellslot" ? SendToTrader : (Action<object>)(_ => { }), cols, ids, bounds, key);
        var grid = gui.GetSlotGrid(key);
        _grids.Add((grid, ids, kind));
        return grid;
    }

    private void SendToTrader(object packet) => capi.Network.SendEntityPacket(_trader.EntityId, packet);

    private static double GridHeight(int count, int cols)
    {
        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;
        int rows = Math.Max(1, (count + cols - 1) / cols);
        return ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, cols, rows).FixedGrow(2 * pad, 2 * pad).fixedHeight;
    }

    private double ComposeTrade(GuiComposer gui, double y, double width, double gridW)
    {
        var dim = CairoFont.WhiteDetailText();
        var font = CairoFont.WhiteSmallText();
        gui.AddStaticText(L("trading-window-sells"), font, ElementBounds.Fixed(0, y, gridW, 20));
        gui.AddStaticText(L("trading-window-buys"), font, ElementBounds.Fixed(gridW + 20, y, gridW, 20));
        y += 22;
        var sell = SellIds();
        var buy = BuyIds();
        if (sell.Length > 0) AddGrid(gui, _inv, sell, Cols, 0, y, "sells", "sell");
        else gui.AddStaticText(L("trading-window-sells-none"), dim, ElementBounds.Fixed(0, y + 10, gridW, 40));
        if (buy.Length > 0) AddGrid(gui, _inv, buy, Cols, gridW + 20, y, "buys", "buy");
        else gui.AddStaticText(L("trading-window-buys-none"), dim, ElementBounds.Fixed(gridW + 20, y + 10, gridW, 40));
        y += Math.Max(GridHeight(Math.Max(1, sell.Length), Cols), GridHeight(Math.Max(1, buy.Length), Cols)) + 8;

        // Locked stock, hatched, with the tier that unlocks it in the tooltip and the details.
        if (_state.Switches.Standing && _state.Locked.Count > 0)
        {
            var rows = _state.Locked;
            _locked = new LockedInventory(rows.Count, capi);
            for (int i = 0; i < rows.Count; i++)
            {
                var slot = (LockedSlot)_locked[i];
                slot.Itemstack = LockedStack(rows[i]);
                slot.Note = WindowText.Lines(capi, TradeWindowModel.LockedDetails(rows[i], _state.Standing).Skip(1));
            }
            var ids = Enumerable.Range(0, rows.Count).Where(i => _locked[i].Itemstack != null).ToArray();
            if (ids.Length > 0)
            {
                gui.AddStaticText(L("trading-window-locked"), dim, ElementBounds.Fixed(0, y, width, 18));
                y += 20;
                var grid = AddGrid(gui, _locked, ids, 8, 0, y, "locked", "locked");
                gui.AddInteractiveElement(new GuiElementSlotHatch(capi, ElementBounds.Fixed(0, y, width, 10), grid, Enumerable.Range(0, ids.Length).ToList()), "hatch-locked");
                y += GridHeight(ids.Length, 8) + 6;
            }
        }

        // The selected good, and the sell slot with what the trader pays for its stack.
        gui.AddDynamicText("", dim, ElementBounds.Fixed(0, y, gridW + 10, 90), "details");
        double sx = gridW + 20;
        gui.AddStaticText(L("trading-window-sell"), font, ElementBounds.Fixed(sx, y, gridW, 20));
        AddGrid(gui, _inv, [SeraphTraderInventory.SellSlot], 1, sx, y + 20, "sellslot", "sellslot");
        double slotW = GridHeight(1, 1);
        gui.AddSmallButton(L("trading-window-hold-sell"), () => true, ElementBounds.Fixed(sx + slotW + 10, y + 32, 0, 0).WithFixedPadding(8, 5), EnumButtonStyle.Normal, "sellbutton");
        gui.AddDynamicText("", dim, ElementBounds.Fixed(sx, y + 22 + slotW, gridW, 72), "selloffer");
        return y + Math.Max(94, 22 + slotW + 74);
    }

    private ItemStack? LockedStack(LockedRow row)
    {
        var collectible = Orders.TraderFinder.Collectible(capi.World, row.Code);
        if (collectible is null) return null;
        var stack = new ItemStack(collectible, Math.Max(1, row.StackSize));
        if (row.Attributes is { Length: > 0 } json)
            try
            {
                var attrs = new Vintagestory.API.Datastructures.JsonObject(Newtonsoft.Json.Linq.JToken.Parse(json)).ToAttribute();
                if (attrs is Vintagestory.API.Datastructures.ITreeAttribute tree) stack.Attributes = tree;
            }
            catch (Exception)
            {
                // A stack without its attributes still shows what it is.
            }
        return stack;
    }

    private double ComposeOrders(GuiComposer gui, double y, double width)
    {
        var font = CairoFont.WhiteSmallText();
        var lines = TradeWindowModel.Orders(_state);
        if (lines.Count == 0)
        {
            gui.AddStaticText(L("trading-window-orders-none"), font, ElementBounds.Fixed(0, y, width, 40));
            return y + 44;
        }
        gui.AddStaticText(L("trading-window-orders-intro"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, y, width, 36));
        y += 38;
        foreach (var line in lines.Take(6))
        {
            int id = line.Row.Id;
            gui.AddStaticText(T(line.Line), font, ElementBounds.Fixed(0, y, width - 110, 54));
            if (line.CanTake)
                gui.AddSmallButton(L("trading-window-take"), () => Request(TradeAction.TakeOrder, id: id),
                    ElementBounds.Fixed(width - 100, y, 0, 0).WithFixedPadding(8, 4), EnumButtonStyle.Normal, "take-" + id);
            else if (line.Row.Mine)
                gui.AddSmallButton(L("trading-window-handin"), () => Request(TradeAction.HandInOrder, id: id),
                    ElementBounds.Fixed(width - 100, y, 0, 0).WithFixedPadding(8, 4), EnumButtonStyle.Normal, "handin-" + id);
            y += 58;
        }
        return y;
    }

    private double ComposeDeliveries(GuiComposer gui, double y, double width)
    {
        var font = CairoFont.WhiteSmallText();
        var lines = TradeWindowModel.Deliveries(_state);
        if (lines.Count == 0)
        {
            gui.AddStaticText(L("trading-window-deliveries-none"), font, ElementBounds.Fixed(0, y, width, 40));
            return y + 44;
        }
        int n = 0;
        foreach (var line in lines.Take(6))
        {
            int id = line.Row?.Id ?? 0;
            gui.AddStaticText(T(line.Line), font, ElementBounds.Fixed(0, y, width - 120, 72));
            double by = y;
            if (line.CanTake)
            {
                gui.AddSmallButton(L("trading-window-take"), () => Request(TradeAction.TakeDelivery),
                    ElementBounds.Fixed(width - 110, by, 0, 0).WithFixedPadding(8, 4), EnumButtonStyle.Normal, "dtake-" + n);
                by += 28;
            }
            if (line.CanHandIn)
            {
                gui.AddSmallButton(L("trading-window-handin"), () => Request(TradeAction.HandInDelivery),
                    ElementBounds.Fixed(width - 110, by, 0, 0).WithFixedPadding(8, 4), EnumButtonStyle.Normal, "dhandin-" + n);
                by += 28;
            }
            if (line.CanMark)
                gui.AddSmallButton(L("trading-window-mark"), () => Request(TradeAction.MarkDelivery, id: id),
                    ElementBounds.Fixed(width - 110, by, 0, 0).WithFixedPadding(8, 4), EnumButtonStyle.Normal, "dmark-" + n);
            y += 76;
            n++;
        }
        return y;
    }

    private double ComposeMaps(GuiComposer gui, double y, double width)
    {
        var font = CairoFont.WhiteSmallText();
        var dim = CairoFont.WhiteDetailText();
        var ids = MapIds();
        if (ids.Length == 0)
            gui.AddStaticText(L("trading-window-maps-none"), font, ElementBounds.Fixed(0, y, width, 40));
        else
        {
            var grid = AddGrid(gui, _inv, ids, 8, 0, y, "maps", "map");
            var locked = Enumerable.Range(0, ids.Length).Where(i => StatusOf(ids[i]) == MapOfferStatus.Locked).ToList();
            if (locked.Count > 0) gui.AddInteractiveElement(new GuiElementSlotHatch(capi, ElementBounds.Fixed(0, y, width, 10), grid, locked), "hatch-maps");
            y += GridHeight(ids.Length, 8) + 6;
            foreach (int id in ids)
            {
                gui.AddStaticText(MapLine(id), dim, ElementBounds.Fixed(0, y, width, 34));
                y += 34;
            }
        }
        if (!_state.LeadsToTraders && _state.LeadsTier >= 0 && _state.Standing?.Tiers.ElementAtOrDefault(_state.LeadsTier) is { } tier)
        {
            gui.AddStaticText(L("trading-window-maps-leadslocked", TradeWindowModel.TierName(tier.Code)), dim, ElementBounds.Fixed(0, y, width, 36));
            y += 38;
        }
        gui.AddDynamicText("", dim, ElementBounds.Fixed(0, y, width, 72), "details");
        return y + 76;
    }

    /// <summary>One map offer: what, how far, how precise, and whether this player can have it.</summary>
    private string MapLine(int slotId)
    {
        var slot = (ItemSlotTrade)_inv[slotId];
        var stack = slot.Itemstack!;
        var a = stack.Attributes;
        string what = a.GetString(MapOfferAttrs.Offer) switch
        {
            MapOfferAttrs.OreMap => T(TradeWindowModel.OreMapLine(a.GetString(MapOfferAttrs.Metal) ?? "", a.GetString(MapOfferAttrs.SizeTier),
                a.GetDouble(MapOfferAttrs.Distance), a.GetAsInt(MapOfferAttrs.Precision, 1))),
            MapOfferAttrs.GravelMap => L("trading-window-map-gravel", TradeWindowModel.F(Math.Round(a.GetDouble(MapOfferAttrs.Distance)))),
            MapOfferAttrs.Lead => T(TradeWindowModel.LeadLine(a.GetString(MapOfferAttrs.LeadKind), a.GetString(MapOfferAttrs.Type) ?? "",
                Math.Sqrt(Sq(a.GetAsInt(MapOfferAttrs.X) - _trader.Pos.X) + Sq(a.GetAsInt(MapOfferAttrs.Z) - _trader.Pos.Z)),
                TradeWindowModel.Direction(a.GetAsInt(MapOfferAttrs.X) - _trader.Pos.X, a.GetAsInt(MapOfferAttrs.Z) - _trader.Pos.Z))),
            _ => stack.GetName(),
        };
        string status = StatusOf(slotId) switch
        {
            MapOfferStatus.SoldOut => L("trading-window-map-soldout"),
            MapOfferStatus.Locked => L("trading-window-map-locked", _state.Standing?.Tiers.ElementAtOrDefault(_state.LeadsTier) is { } t
                ? T(TradeWindowModel.TierName(t.Code)) : ""),
            _ => L("trading-window-map-price", slot.TradeItem?.Price ?? 0),
        };
        return $"{what} — {status}";
    }

    private static double Sq(double v) => v * v;

    private double ComposeStanding(GuiComposer gui, double y, double width)
    {
        if (_state.Standing is not { } s) return y;
        var vtml = new System.Text.StringBuilder();
        foreach (var tier in TradeWindowModel.Tiers(s))
        {
            string line = WindowText.Escape(T(tier.Line));
            vtml.Append(tier.Current ? $"<font color=\"#e8c86a\"><strong>» {line}</strong></font>" : $"<font color=\"#b0a890\">   {line}</font>").Append("<br>");
        }
        foreach (var (heading, lines) in TradeWindowModel.StandingSections(_state))
        {
            vtml.Append("<br><strong>").Append(WindowText.Escape(T(heading))).Append("</strong><br>");
            foreach (var line in lines) vtml.Append("- ").Append(WindowText.Escape(T(line))).Append("<br>");
        }
        gui.AddRichtext(vtml.ToString(), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, y, width, 380), "standing");
        return y + 384;
    }

    // ---- Dynamic text ----

    private void UpdateDynamic()
    {
        var gui = SingleComposer;
        if (gui is null) return;
        int gears = InventoryTrader.GetPlayerAssets(capi.World.Player.Entity);
        int? side = EconomySystem.IsPriced(_trader) ? EconomySystem.SideBudgetOf(_trader) : null;
        gui.GetDynamicText("footer")?.SetNewText(string.Join(" · ", TradeWindowModel.Footer(gears, TraderName, _inv.GetTraderAssets(), side, Female).Select(T)));
        gui.GetDynamicText("status")?.SetNewText(_status);
        gui.GetDynamicText("details")?.SetNewText(Details());
        if (gui.GetDynamicText("selloffer") is { } offer)
            offer.SetNewText(_inv[SeraphTraderInventory.SellSlot].Itemstack is { } stack
                ? PriceText(stack, stack.StackSize) ?? ""
                : L("trading-window-sell-hint"));
    }

    private string Details()
    {
        var (kind, id) = _selected;
        if (id < 0) return L(_tab == WindowTab.Maps ? "trading-window-maps-hint" : "trading-window-select-hint");
        switch (kind)
        {
            case "sell" or "buy" or "map":
            {
                if (_inv[id] is not ItemSlotTrade { Itemstack: { } stack, TradeItem: { } item }) return "";
                bool sells = kind != "buy";
                var lines = TradeWindowModel.ShelfDetails(stack.GetName(), item.Stack?.StackSize ?? stack.StackSize, item.Price, item.Stock, sells,
                    kind == "map" && StatusOf(id) == MapOfferStatus.Locked);
                return WindowText.Lines(capi, lines);
            }
            case "locked" when id < _state.Locked.Count:
            {
                var lines = TradeWindowModel.LockedDetails(_state.Locked[id], _state.Standing);
                if (_locked?[id]?.Itemstack is { } stack) lines[0] = new Text("trading-window-selected", _state.Locked[id].StackSize, stack.GetName());
                return WindowText.Lines(capi, lines);
            }
        }
        return "";
    }

    /// <summary>What the trader pays for <paramref name="stack"/> (per unit, from which budget), or why
    /// it does not; with <paramref name="stackSize"/>, how many units that many make. For the sell slot
    /// and the tooltips of the player's own items.</summary>
    public string? PriceText(ItemStack stack, int? stackSize = null)
    {
        var condition = _inv.GetBuyingConditionsSlot(stack);
        Offer? offer = null;
        bool listed = false;
        if (condition is OffListSlot off) offer = off.Offer;
        else if (condition?.TradeItem is { Stack: { } unit } item)
        {
            listed = true;
            offer = new Offer(Refusal.None, item.Price, 1, EconomySystem.SupplyFactor(_trader, stack.Collectible.Code.ToString()), 1,
                Math.Max(1, unit.StackSize), item.Price, 1, Budget.Main);
        }
        else if (EconomySystem.IsPriced(_trader) && EconomySystem.Of(capi) is { } economy)
            offer = economy.QuoteOffList(_trader, stack, capi.World.Player.PlayerUID);
        var lines = TradeWindowModel.OfferLines(offer, listed, EconomySystem.SideBudgetOf(_trader), stackSize);
        if (listed && condition?.TradeItem is { Stock: <= 0 }) lines.Add(new Text("trading-window-selected-nodemand"));
        return L("trading-window-pays-header", TraderName) + "\n" + WindowText.Lines(capi, lines);
    }

    // ---- Holding ----

    /// <summary>Where a hold target is now (a shelf slot's bounds, or the sell button).</summary>
    private ElementBounds? BoundsOf(string target)
    {
        if (target == SellTarget) return _sellButton;
        if (!target.StartsWith("buy:", StringComparison.Ordinal) || !int.TryParse(target[4..], out int slotId)) return null;
        foreach (var (grid, ids, kind) in _grids)
        {
            if (kind is not ("sell" or "map")) continue;
            int i = Array.IndexOf(ids, slotId);
            if (i >= 0 && grid.SlotBounds != null && i < grid.SlotBounds.Length) return grid.SlotBounds[i];
        }
        return null;
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Button == EnumMouseButton.Left)
        {
            if (_sellButton?.PointInside(args.X, args.Y) == true && _inv[SeraphTraderInventory.SellSlot].Itemstack != null)
                StartHold(SellTarget, _sellButton);
            else
                foreach (var (grid, ids, kind) in _grids)
                {
                    if (grid.SlotBounds is null || kind == "sellslot") continue;
                    for (int i = 0; i < grid.SlotBounds.Length && i < ids.Length; i++)
                    {
                        if (!grid.SlotBounds[i].PointInside(args.X, args.Y)) continue;
                        _selected = (kind, ids[i]);
                        bool buyable = kind == "sell" || (kind == "map" && StatusOf(ids[i]) == MapOfferStatus.Available);
                        if (buyable && _inv[ids[i]] is ItemSlotTrade { TradeItem.Stock: > 0 }) StartHold("buy:" + ids[i], grid.SlotBounds[i]);
                        UpdateDynamic();
                        break;
                    }
                }
        }
        base.OnMouseDown(args);
    }

    private void StartHold(string target, ElementBounds bounds)
    {
        _hold.Press(target);
        _holdBounds = bounds;
    }

    public override void OnMouseUp(MouseEvent args)
    {
        _hold.Release();
        base.OnMouseUp(args);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);
        if (_hold.State == HoldState.Holding && _holdBounds != null && !_holdBounds.PointInside(capi.Input.MouseX, capi.Input.MouseY)) _hold.Release();
        if (_hold.Update(deltaTime) && _hold.Target is { } target)
        {
            if (target == SellTarget) Request(TradeAction.Sell);
            else if (int.TryParse(target[4..], out int slot)) Request(TradeAction.Buy, slot);
        }
        if (_ring != null)
        {
            _ring.Visible = _hold.State is HoldState.Holding or HoldState.Waiting;
            _ring.Progress = (float)_hold.Progress;
        }
    }

    private bool Request(TradeAction action, int slot = 0, int id = 0)
    {
        System?.Request(_trader.EntityId, action, slot, id);
        return true;
    }

    // ---- From the server ----

    public void OnState(TradeWindowState state)
    {
        _state = state;
        _stateVersion++;
        Compose();
    }

    public void OnResult(TradeResult result)
    {
        bool trade = result.Action is TradeAction.Buy or TradeAction.Sell;
        if (trade)
        {
            if (result.Ok) _hold.Confirmed();
            else _hold.Refused();
        }
        string text = result.Key is { } key ? T(new Text(key, [.. result.Args])) : "";
        if (result.Ok)
        {
            if (trade)
            {
                capi.Gui.PlaySound(new AssetLocation("sounds/effect/cashregister"), false, 0.25f);
                _trader.TalkUtil?.Talk(EnumTalkType.Purchase);
            }
            _status = text;
        }
        else
        {
            _trader.TalkUtil?.Talk(EnumTalkType.Complain);
            if (text.Length > 0) capi.TriggerIngameError(this, "seraphhorizons-trade", text);
            _status = "";
        }
        UpdateDynamic();
    }

    // ---- Open and close ----

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Current = this;
        _ring = new HoldRingRenderer(capi);
        _tick = capi.Event.RegisterGameTickListener(_ =>
        {
            if (Layout() != _layout) Compose();
            else UpdateDynamic();
        }, 250);
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (Current == this) Current = null;
        _hold.Release();
        _ring?.Dispose();
        _ring = null;
        if (_tick != 0) capi.Event.UnregisterGameTickListener(_tick);
        _tick = 0;
        _trader.TalkUtil?.Talk(EnumTalkType.Goodbye);
        capi.World.Player.InventoryManager.CloseInventoryAndSync(_inv);
        foreach (var (grid, _, _) in _grids) grid.OnGuiClosed(capi);
    }
}
