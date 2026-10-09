using SeraphHorizons.Mod.Trading.Economy;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Maps;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>
/// The pack's trade window (mockup "A · Tabs"), in place of vanilla's GuiDialogTrader for the pack's
/// traders. A thin layer over <see cref="TradeWindowModel"/>, <see cref="WindowLayout"/> and
/// <see cref="HoldTimer"/>: it lays out what they say, as measured boxes, and sends what the player
/// holds to the server (<see cref="TradeWindowSystem.Request"/>).
///
/// <list type="bullet">
/// <item>Header: the trader's name, type and region, the player's standing tier with a bar to the
/// next ("Regular [310 / 800]"); tabs Trade, Orders (n), Deliveries (n), Maps &amp; leads, Standing
/// (a switched-off feature has none); footer: the player's gears, the trader's, its side budget.</item>
/// <item>Trade: the Sells and Buys shelves are the trader's own slots (item, price and stock on
/// hover); the goods the player's standing does not unlock yet are hatched with the tier that does.
/// No carts, no Deal button: a press selects a good, holding it buys one unit (the ring of
/// <see cref="HoldRingRenderer"/>), and holding on buys the next. The four sell slots are the
/// trader's selling cart; Hold to sell sells one lot of what is in them, pooled
/// (<see cref="SellPool"/>).</item>
/// <item>Orders, Deliveries, Maps &amp; leads and Standing as their names say, in a scroll area when
/// they run longer than the screen has room for.</item>
/// </list>
/// Every block of text is measured wrapped to its width (<see cref="Flow"/>); the window recomposes
/// when what it shows changes (never while a mouse button is down, so a drag or a hold is not cut
/// short). It sits right, below the minimap and its coordinates (<see cref="WindowPlacement"/>). It
/// closes as vanilla's does when the player walks away (the trader's own tick) and on the server's
/// packet 1212.
/// </summary>
public sealed class GuiDialogSeraphTrade : GuiDialog
{
    /// <summary>The open window (one at a time), for the tooltip patch and the network handler.</summary>
    public static GuiDialogSeraphTrade? Current { get; private set; }

    private const int Cols = 4;
    private const string SellTarget = "sell";
    private const double ScrollbarWidth = WindowLayout.ScrollbarWidth;

    private readonly EntitySeraphTrader _trader;
    private readonly SeraphTraderInventory? _own;
    private readonly InventoryTrader _inv;
    private readonly HoldTimer _hold = new();
    private readonly List<(GuiElementItemSlotGridBase Grid, int[] Ids, string Kind)> _grids = [];
    private readonly GameMeasure _measure;
    private TradeWindowState _state;
    private WindowTab _tab = WindowTab.Trade;
    private HoldRingRenderer? _ring;
    private LockedInventory? _locked;
    private ElementBounds? _sellButton;
    private ElementBounds? _holdBounds;
    private GuiElementContainer? _scrollBody;
    private float _scroll;
    private long _tick;
    private string _composedKey = "";
    private bool _dirty;
    private int _stateVersion;
    private (string Kind, int Id) _selected = ("", -1);
    private string _status = "";

    public GuiDialogSeraphTrade(ICoreClientAPI capi, EntitySeraphTrader trader) : base(capi)
    {
        _trader = trader;
        _inv = trader.Inventory;
        _own = _inv as SeraphTraderInventory;
        _measure = new GameMeasure(capi);
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

    private static double Scale => RuntimeEnv.GUIScale;

    // ---- Slots ----

    private static bool IsOffer(ItemSlot slot) => slot.Itemstack?.Attributes.GetString(MapOfferAttrs.Offer) != null;

    private int[] SellIds() => Enumerable.Range(0, 16).Where(i => _inv[i].Itemstack != null && !IsOffer(_inv[i])).ToArray();

    private int[] BuyIds() => Enumerable.Range(20, 16).Where(i => _inv[i].Itemstack != null).ToArray();

    private int[] MapIds() => Enumerable.Range(0, 16).Where(i => _inv[i].Itemstack != null && IsOffer(_inv[i])).ToArray();

    private static readonly int[] SellSlotIds = Enumerable.Range(SeraphTraderInventory.SellSlot, SeraphTraderInventory.SellSlots).ToArray();

    private bool SellSlotsHoldGoods => SellSlotIds.Any(i => _inv[i].Itemstack != null);

    private MapOfferStatus StatusOf(int slotId)
    {
        var slot = (ItemSlotTrade)_inv[slotId];
        var a = slot.Itemstack!.Attributes;
        return TradeWindowModel.MapStatus(a.GetString(MapOfferAttrs.Offer), a.GetString(MapOfferAttrs.LeadKind), slot.TradeItem?.Stock ?? 0, _state.LeadsToTraders,
            _state.OwnedMaps.Contains(slotId));
    }

    // ---- What it shows ----

    private string FooterText()
    {
        int gears = InventoryTrader.GetPlayerAssets(capi.World.Player.Entity);
        int? side = EconomySystem.IsPriced(_trader) ? EconomySystem.SideBudgetOf(_trader) : null;
        return string.Join(" · ", TradeWindowModel.Footer(gears, TraderName, _inv.GetTraderAssets(), side, Female).Select(T));
    }

    private List<SellLine> SellLines() => _own?.SellLines() ?? [];

    private string OfferText() => WindowText.Lines(capi, TradeWindowModel.SellOffer(SellLines(), SellSlotsHoldGoods));

    /// <summary>The breakdown of every sell slot's offer, for the offer's tooltip.</summary>
    private string OfferBreakdown()
    {
        var parts = new List<string>();
        foreach (int id in SellSlotIds)
            if (_inv[id].Itemstack is { } stack)
                parts.Add(stack.StackSize + " × " + stack.GetName() + "\n" + (PriceText(stack, header: false) ?? ""));
        return parts.Count == 0 ? L("trading-window-sell-hint") : L("trading-window-sell-breakdown") + "\n\n" + string.Join("\n\n", parts);
    }

    /// <summary>Everything that decides what the window shows: when it changes, the window recomposes.</summary>
    private string Key()
    {
        var parts = new List<string>
        {
            _tab.ToString(), string.Join(",", SellIds()), string.Join(",", BuyIds()), string.Join(",", MapIds()), _stateVersion.ToString(),
            FooterText(), _status, Details(),
            capi.Render.FrameWidth + "x" + capi.Render.FrameHeight + "@" + Scale, StackKey(),
        };
        if (_tab == WindowTab.Trade)
        {
            parts.Add(OfferText());
            foreach (int id in SellSlotIds) parts.Add(_inv[id].Itemstack is { } s ? s.Collectible.Code + "x" + s.StackSize : "-");
        }
        if (_tab == WindowTab.Maps)
            foreach (int id in MapIds()) parts.Add(((ItemSlotTrade)_inv[id]).TradeItem?.Stock.ToString() ?? "");
        return string.Join("|", parts);
    }

    /// <summary>The right-hand HUDs the window keeps clear of (the minimap, the coordinates, others').</summary>
    private (List<ScreenRect> Top, List<ScreenRect> Bottom) Stacks()
    {
        static List<ScreenRect> Of(IEnumerable<ElementBounds> bounds, ElementBounds? mine) =>
            bounds.Where(b => b != mine).Select(b => new ScreenRect(b.absX, b.absY, b.OuterWidth, b.OuterHeight)).ToList();
        var mine = SingleComposer?.Bounds;
        return (Of(capi.Gui.GetDialogBoundsInArea(EnumDialogArea.RightTop), mine), Of(capi.Gui.GetDialogBoundsInArea(EnumDialogArea.RightBottom), mine));
    }

    private string StackKey()
    {
        var (top, bottom) = Stacks();
        return string.Join(";", top.Concat(bottom).Select(r => $"{r.X:0},{r.Y:0},{r.W:0},{r.H:0}"));
    }

    // ---- Composing ----

    private void RequestCompose()
    {
        _dirty = true;
        if (!MouseDown) Compose();
    }

    private bool MouseDown => capi.Input.MouseButton.Left || capi.Input.MouseButton.Right;

    private void Compose()
    {
        _dirty = false;
        // A recompose drops the old grids before their mouse-up: the inventories must not stay paused.
        (_inv as InventoryBase)!.InvNetworkUtil.PauseInventoryUpdates = false;
        if (capi.World.Player?.InventoryManager.MouseItemSlot?.Inventory is InventoryBase mouse) mouse.InvNetworkUtil.PauseInventoryUpdates = false;
        foreach (var (grid, _, _) in _grids) grid.OnGuiClosed(capi);
        _grids.Clear();
        _sellButton = null;
        _scrollBody = null;
        _composedKey = Key();
        var tabs = TradeWindowModel.Tabs(_state);
        if (!tabs.Any(t => t.Tab == _tab)) _tab = WindowTab.Trade;

        var slots = new SlotMetrics(GuiElementPassiveItemSlot.unscaledSlotSize, GuiElementItemSlotGridBase.unscaledSlotPadding);
        double width = WindowLayout.ContentWidth(slots);
        double pad = GuiStyle.ElementToDialogPadding;

        // Header, body, a long tab's scroll area and the footer, measured and placed.
        var region = _trader.Region;
        var bar = TradeWindowModel.Bar(_state.Standing, _state.Switches);
        Action<Flow>? body = null, scrollBuild = null;
        switch (_tab)
        {
            case WindowTab.Trade:
                body = f => WindowLayout.Trade(f, TradeText(), slots);
                break;
            case WindowTab.Maps:
            {
                var ids = MapIds();
                if (ids.Length > 0)
                    body = f =>
                    {
                        f.Element("maps", 0, slots.GridWidth(Math.Min(8, ids.Length)), slots.GridHeight(ids.Length, 8));
                        f.Next(6);
                    };
                var lines = ids.Length == 0 && _state.LeadOffers.Count == 0 ? [L("trading-window-maps-none")] : ids.Select(MapLine).ToList();
                string? leads = LeadsLockedText();
                string details = Details();
                // Camp leads: this player's own, off the shelf, each with its price and a Buy button.
                var campLeads = _state.LeadOffers.Select((row, i) =>
                    new ActionLine(T(TradeWindowModel.LeadOfferLine(row)), [(L("trading-window-buy"), "lead-" + i, i)])).ToList();
                string? note = TradeWindowModel.LeadOffersNote(_state) is { } n ? T(n) : null;
                scrollBuild = f => WindowLayout.MapLines(f, lines, leads, details, campLeads, note);
                break;
            }
            default:
                scrollBuild = BuildScrollTab;
                break;
        }
        var (top, bottom) = Stacks();
        var frame = WindowLayout.Compose(_measure, slots, T(TradeWindowModel.Header(TraderName, _trader.TraderType, region.Climate, region.Rock)),
            bar is null ? null : T(bar.Label), body, scrollBuild, _status, FooterText(), capi.Render.FrameWidth, capi.Render.FrameHeight, Scale, top, bottom);
        var scroll = frame.Scroll;
        double visible = frame.Visible;

        var bg = ElementBounds.Fill.WithFixedPadding(pad);
        bg.BothSizing = ElementSizing.FitToChildren;
        var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.None).WithFixedPosition(frame.Place.X / Scale, frame.Place.Y / Scale);
        var dim = CairoFont.WhiteDetailText();
        var gui = capi.Gui.CreateCompo("seraphtrade-" + _trader.EntityId, dialog)
            .AddShadedDialogBG(bg, true)
            .AddDialogTitleBar(TraderName, () => TryClose())
            .AddHorizontalTabs(tabs.Select(t => new GuiTab { Name = T(t.Label), DataInt = (int)t.Tab }).ToArray(),
                ElementBounds.Fixed(0, -24, width, 25), OnTab, dim, dim.Clone().WithColor(GuiStyle.ActiveButtonTextColor), "tabs")
            .BeginChildElements(bg);
        gui.GetHorizontalTabs("tabs").activeElement = Math.Max(0, tabs.FindIndex(t => t.Tab == _tab));

        foreach (var box in frame.Main.Boxes) AddBox(gui, box, bar?.Fraction ?? 0);
        if (scroll != null && frame.Main.Boxes.FirstOrDefault(b => b.Key == "scroll") is { } area) AddScroll(gui, area, scroll);
        foreach (var box in frame.Footer.Boxes) AddBox(gui, box, 0);

        SingleComposer = gui.EndChildElements().Compose();
        // The shelves are pressed and held (OnMouseDown), never clicked into a cart; the sell slots
        // take and give stacks as any slot.
        foreach (var (grid, _, kind) in _grids)
            if (kind != "sellslot") grid.CanClickSlot = _ => false;
        _sellButton = gui.GetButton("sellbutton")?.Bounds;
        if (scroll != null && gui.GetScrollbar("scrollbar") is { } bar2)
        {
            bar2.SetHeights((float)visible, (float)scroll.Bottom);
            _scroll = Math.Clamp(_scroll, 0, (float)Math.Max(0, scroll.Bottom - visible));
            bar2.CurrentYPosition = _scroll;
            OnScroll(_scroll);
        }
        if (_hold.Target != null && BoundsOf(_hold.Target) is { } held) _holdBounds = held;
        else if (_hold.Target != null) _hold.Release();
    }

    private void BuildScrollTab(Flow f)
    {
        switch (_tab)
        {
            case WindowTab.Orders:
            {
                var lines = TradeWindowModel.Orders(_state).Select(line => new ActionLine(T(line.Line),
                    line.CanTake ? [(L("trading-window-take"), "take-" + line.Row.Id, line.Row.Id)]
                    : line.Row.Mine ? [(L("trading-window-handin"), "handin-" + line.Row.Id, line.Row.Id)] : [])).ToList();
                WindowLayout.Actions(f, L("trading-window-orders-intro"), lines, L("trading-window-orders-none"));
                break;
            }
            case WindowTab.Deliveries:
            {
                int n = 0;
                var lines = new List<ActionLine>();
                foreach (var line in TradeWindowModel.Deliveries(_state))
                {
                    int id = line.Row?.Id ?? 0;
                    var buttons = new List<(string, string, int)>();
                    if (line.CanTake) buttons.Add((L("trading-window-take"), "dtake-" + n, id));
                    if (line.CanHandIn) buttons.Add((L("trading-window-handin"), "dhandin-" + n, id));
                    if (line.CanMark) buttons.Add((L("trading-window-mark"), "dmark-" + n, id));
                    lines.Add(new ActionLine(T(line.Line), buttons));
                    n++;
                }
                WindowLayout.Actions(f, null, lines, L("trading-window-deliveries-none"));
                break;
            }
            case WindowTab.Standing when _state.Standing is { } s:
                WindowLayout.Standing(f, TradeWindowModel.Tiers(s).Select(t => (T(t.Line), t.Current)).ToList(),
                    TradeWindowModel.StandingSections(_state).Select(sec => new TextSection(T(sec.Heading), sec.Lines.Select(T).ToList())).ToList());
                break;
        }
    }

    private string? LeadsLockedText() =>
        !_state.LeadsToTraders && _state.LeadsTier >= 0 && _state.Standing?.Tiers.ElementAtOrDefault(_state.LeadsTier) is { } tier
            ? L("trading-window-maps-leadslocked", TradeWindowModel.TierName(tier.Code)) : null;

    private TradeTabText TradeText()
    {
        var lockedIds = LockedRows();
        return new TradeTabText(L("trading-window-sells"), L("trading-window-buys"), SellIds().Length, BuyIds().Length,
            L("trading-window-sells-none"), L("trading-window-buys-none"),
            lockedIds.Count > 0 ? L("trading-window-locked") : null, lockedIds.Count, Details(),
            L("trading-window-sell"), L("trading-window-hold-sell"), OfferText());
    }

    /// <summary>Locked stock (standing on): rows whose item exists here.</summary>
    private List<int> LockedRows()
    {
        if (!_state.Switches.Standing || _state.Locked.Count == 0) return [];
        if (_locked is null || _locked.Count != _state.Locked.Count)
        {
            _locked = new LockedInventory(_state.Locked.Count, capi);
            for (int i = 0; i < _state.Locked.Count; i++)
            {
                var slot = (LockedSlot)_locked[i];
                slot.Itemstack = LockedStack(_state.Locked[i]);
                slot.Note = WindowText.Lines(capi, TradeWindowModel.LockedDetails(_state.Locked[i], _state.Standing).Skip(1));
            }
        }
        return Enumerable.Range(0, _state.Locked.Count).Where(i => _locked[i].Itemstack != null).ToList();
    }

    private CairoFont FontOf(UiFont font, string? color = null)
    {
        var f = font switch
        {
            UiFont.Detail => CairoFont.WhiteDetailText(),
            UiFont.Bold => CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
            UiFont.Button => CairoFont.SmallButtonText(),
            _ => CairoFont.WhiteSmallText(),
        };
        if (color != null) f = f.WithColor(ColorUtil.Hex2Doubles(color));
        return f;
    }

    private void AddBox(GuiComposer gui, UiBox box, double fraction)
    {
        switch (box.Kind)
        {
            case BoxKind.Text:
                if (box.H <= 0) return;
                gui.AddStaticText(box.Text, FontOf(box.Font, box.Color), EnumTextOrientation.Left, ElementBounds.Fixed(box.X, box.Y, box.W, box.H + 2), "text-" + box.Key);
                if (box.Key == "offer")
                    gui.AddHoverText(OfferBreakdown(), CairoFont.WhiteDetailText(), 360, ElementBounds.Fixed(box.X, box.Y, box.W, box.H), "offerhover");
                return;
            case BoxKind.Button:
                if (box.Key == "sellbutton")
                    gui.AddSmallButton(box.Text, () => true, ButtonBounds(box), EnumButtonStyle.Normal, "sellbutton");
                return;
        }
        switch (box.Key)
        {
            case "bar":
                gui.AddStaticElement(new GuiElementStandingBar(capi, ElementBounds.Fixed(box.X, box.Y, box.W, box.H), fraction));
                break;
            case "sells":
                AddGrid(gui, _inv, SellIds(), Cols, box.X, box.Y, "sells", "sell");
                break;
            case "buys":
                AddGrid(gui, _inv, BuyIds(), Cols, box.X, box.Y, "buys", "buy");
                break;
            case "locked" when _locked != null:
            {
                var ids = LockedRows().ToArray();
                var grid = AddGrid(gui, _locked, ids, 8, box.X, box.Y, "locked", "locked");
                gui.AddInteractiveElement(new GuiElementSlotHatch(capi, ElementBounds.Fixed(box.X, box.Y, box.W, 10), grid, Enumerable.Range(0, ids.Length).ToList()), "hatch-locked");
                break;
            }
            case "sellslots":
            {
                var grid = AddGrid(gui, _inv, SellSlotIds, Cols, box.X, box.Y, "sellslot", "sellslot");
                gui.AddInteractiveElement(new GuiElementSlotNote(capi, ElementBounds.Fixed(box.X, box.Y, box.W, 10), grid,
                    i => i < SellSlotIds.Length && _inv[SellSlotIds[i]].Itemstack is { } s && _own?.RefusalOf(s) != null, L("trading-window-sell-wontbuy")), "sellnotes");
                break;
            }
            case "maps":
            {
                var ids = MapIds();
                var grid = AddGrid(gui, _inv, ids, 8, box.X, box.Y, "maps", "map");
                // Locked offers and ones the player has already are greyed out (hatched).
                var locked = Enumerable.Range(0, ids.Length).Where(i => StatusOf(ids[i]) is MapOfferStatus.Locked or MapOfferStatus.Owned).ToList();
                if (locked.Count > 0) gui.AddInteractiveElement(new GuiElementSlotHatch(capi, ElementBounds.Fixed(box.X, box.Y, box.W, 10), grid, locked), "hatch-maps");
                break;
            }
        }
    }

    private static ElementBounds ButtonBounds(UiBox box) =>
        ElementBounds.Fixed(box.X, box.Y, box.W - 2 * Flow.ButtonPadX, box.H - 2 * Flow.ButtonPadY).WithFixedPadding(Flow.ButtonPadX, Flow.ButtonPadY);

    /// <summary>A long tab's content in a clipped, scrolled container.</summary>
    private void AddScroll(GuiComposer gui, UiBox area, Flow content)
    {
        bool bar = content.Bottom > area.H + 0.5;
        var clip = ElementBounds.Fixed(area.X, area.Y, bar ? area.W - ScrollbarWidth : area.W, area.H);
        var inner = ElementBounds.Fixed(0, 0, clip.fixedWidth, content.Bottom);
        var container = new GuiElementContainer(capi, inner) { unscaledCellSpacing = 0 };
        gui.BeginClip(clip).AddInteractiveElement(container, "scrollbody").EndClip();
        if (bar) gui.AddVerticalScrollbar(OnScroll, ElementBounds.Fixed(area.Right - ScrollbarWidth + 6, area.Y, ScrollbarWidth - 6, area.H), "scrollbar");
        foreach (var box in content.Boxes)
        {
            var bounds = ElementBounds.Fixed(box.X, box.Y, box.W, box.Kind == BoxKind.Text ? box.H + 2 : box.H);
            switch (box.Kind)
            {
                case BoxKind.Text when box.H > 0:
                    container.Add(new GuiElementStaticText(capi, box.Text, EnumTextOrientation.Left, bounds, FontOf(box.Font, box.Color)));
                    break;
                case BoxKind.Button:
                {
                    var action = ActionFor(box.Key, box.Id);
                    container.Add(new GuiElementTextButton(capi, box.Text, CairoFont.SmallButtonText(),
                        CairoFont.SmallButtonText().WithColor(GuiStyle.ActiveButtonTextColor), action, ButtonBounds(box), EnumButtonStyle.Normal));
                    break;
                }
            }
        }
        _scrollBody = container;
    }

    private ActionConsumable ActionFor(string key, int id)
    {
        if (key.StartsWith("take-", StringComparison.Ordinal)) return () => Request(TradeAction.TakeOrder, id: id);
        if (key.StartsWith("handin-", StringComparison.Ordinal)) return () => Request(TradeAction.HandInOrder, id: id);
        if (key.StartsWith("dtake-", StringComparison.Ordinal)) return () => Request(TradeAction.TakeDelivery);
        if (key.StartsWith("dhandin-", StringComparison.Ordinal)) return () => Request(TradeAction.HandInDelivery);
        if (key.StartsWith("dmark-", StringComparison.Ordinal)) return () => Request(TradeAction.MarkDelivery, id: id);
        if (key.StartsWith("lead-", StringComparison.Ordinal) && _state.LeadOffers.ElementAtOrDefault(id) is { } lead)
        {
            string cell = lead.Pity ? MapsSystem.PityCode : lead.Cell;
            int price = lead.Price;
            return () => RequestLead(cell, price);
        }
        return () => true;
    }

    private void OnScroll(float value)
    {
        _scroll = value;
        if (_scrollBody is null) return;
        _scrollBody.Bounds.fixedY = -value;
        _scrollBody.Bounds.CalcWorldBounds();
    }

    private void OnTab(int tab)
    {
        _tab = (WindowTab)tab;
        _hold.Release();
        _selected = ("", -1);
        _scroll = 0;
        Compose();
    }

    private GuiElementItemSlotGridBase AddGrid(GuiComposer gui, IInventory inv, int[] ids, int cols, double x, double y, string key, string kind)
    {
        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;
        int rows = Math.Max(1, (ids.Length + cols - 1) / cols);
        var bounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, x + pad, y + pad, cols, rows).FixedGrow(2 * pad, 2 * pad);
        // Only the sell slots move stacks, which the server must hear of (as vanilla's dialog sends
        // its slots' packets); the shelves and locked goods are read only.
        gui.AddItemSlotGrid(inv, kind == "sellslot" ? SendToTrader : (Action<object>)(_ => { }), cols, ids, bounds, key);
        var grid = gui.GetSlotGrid(key);
        _grids.Add((grid, ids, kind));
        return grid;
    }

    private void SendToTrader(object packet) => capi.Network.SendEntityPacket(_trader.EntityId, packet);

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
        var lockedTier = _state.Standing?.Tiers.ElementAtOrDefault(_state.LeadsTier) is { } t ? TradeWindowModel.TierName(t.Code) : null;
        string status = TradeWindowModel.MapStatusText(StatusOf(slotId), lockedTier) is { } why
            ? T(why)
            : L("trading-window-map-price", slot.TradeItem?.Price ?? 0);
        return $"{what} — {status}";
    }

    private static double Sq(double v) => v * v;

    // ---- Changing text ----

    /// <summary>Recomposes when anything it shows has changed.</summary>
    private void Refresh()
    {
        if (SingleComposer is null) return;
        if (_dirty || Key() != _composedKey) RequestCompose();
    }

    private string Details()
    {
        var (kind, id) = _selected;
        if (id < 0) return _tab switch
        {
            WindowTab.Maps => L("trading-window-maps-hint"),
            WindowTab.Trade => L("trading-window-select-hint"),
            _ => "",
        };
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

    /// <summary>What the trader pays for <paramref name="stack"/> (per unit, from which budget, the
    /// breakdown), or why it does not. For the tooltips: the player's own items and the sell slots.</summary>
    public string? PriceText(ItemStack stack, bool header = true)
    {
        if (!SeraphTraderInventory.MaySell(stack)) return (header ? L("trading-window-pays-header", TraderName) + "\n" : "") + L("trading-window-sell-never");
        var condition = _inv.GetBuyingConditionsSlot(stack);
        Offer? offer = null;
        bool listed = false;
        if (condition is OffListSlot off) offer = off.Offer;
        else if (condition?.TradeItem is { Stack: { } unit } item)
        {
            listed = true;
            offer = new Offer(Refusal.None, item.Price, 1, EconomySystem.SupplyFactor(_trader, stack.Collectible.Code.ToString()), 1,
                Math.Max(1, unit.StackSize), item.Price, Budget.Main);
        }
        else if (EconomySystem.IsPriced(_trader) && EconomySystem.Of(capi) is { } economy)
            offer = economy.QuoteOffList(_trader, stack, capi.World.Player.PlayerUID);
        var lines = TradeWindowModel.OfferLines(offer, listed, EconomySystem.SideBudgetOf(_trader));
        if (listed && condition?.TradeItem is { Stock: <= 0 }) lines.Add(new Text("trading-window-selected-nodemand"));
        return (header ? L("trading-window-pays-header", TraderName) + "\n" : "") + WindowText.Lines(capi, lines);
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
            if (_sellButton?.PointInside(args.X, args.Y) == true && SellSlotsHoldGoods)
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
                        // The details change: shown once the button is up (no recompose under a press).
                        _dirty = true;
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
        // After the elements had their mouse-up (a slot drop, a button click).
        if (_dirty || Key() != _composedKey) capi.Event.EnqueueMainThreadTask(() => { if (IsOpened()) Refresh(); }, "seraphtrade-refresh");
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);
        if (_hold.State == HoldState.Holding && _holdBounds != null && !_holdBounds.PointInside(capi.Input.MouseX, capi.Input.MouseY)) _hold.Release();
        if (_hold.Update(deltaTime) && _hold.Target is { } target)
        {
            if (target == SellTarget) Request(TradeAction.Sell);
            else if (int.TryParse(target[4..], out int slot) && _inv[slot] is ItemSlotTrade { Itemstack: { } stack, TradeItem: { } item })
                // What the player sees: the server refuses if the slot was restocked or repriced meanwhile.
                System?.Request(_trader.EntityId, TradeAction.Buy, slot, code: stack.Collectible.Code.ToString(), price: item.Price);
            else _hold.Refused();
        }
        if (_ring != null)
        {
            _ring.Visible = _hold.State is HoldState.Holding or HoldState.Waiting;
            _ring.Progress = (float)_hold.Progress;
        }
    }

    private bool RequestLead(string cell, int price)
    {
        System?.Request(_trader.EntityId, TradeAction.BuyLead, code: cell, price: price);
        return true;
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
        _locked = null;
        RequestCompose();
    }

    public void OnResult(TradeResult result)
    {
        bool trade = result.Action is TradeAction.Buy or TradeAction.Sell;
        if (trade)
        {
            if (result.Ok)
            {
                _hold.Confirmed();
                _ring?.Pulse();
            }
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
        Refresh();
    }

    // ---- Open and close ----

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Current = this;
        _ring = new HoldRingRenderer(capi);
        _tick = capi.Event.RegisterGameTickListener(_ =>
        {
            // A trader gone (a visitor leaving, killed, unloaded) takes the window with it.
            if (!_trader.Alive || _trader.State == EnumEntityState.Despawned || capi.World.GetEntityById(_trader.EntityId) != _trader)
            {
                TryClose();
                return;
            }
            Refresh();
        }, 250);
        // Placed once it is among the open dialogs (the stacks it keeps clear of are read then).
        Compose();
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

    /// <summary>The game's own text measuring, in unscaled units (what the static texts draw with).</summary>
    private sealed class GameMeasure(ICoreClientAPI capi) : ITextMeasure
    {
        private static CairoFont Font(UiFont font) => font switch
        {
            UiFont.Detail => CairoFont.WhiteDetailText(),
            UiFont.Bold => CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
            UiFont.Button => CairoFont.SmallButtonText(),
            _ => CairoFont.WhiteSmallText(),
        };

        public double Height(UiFont font, string text, double width) =>
            capi.Gui.Text.GetMultilineTextHeight(Font(font), text, GuiElement.scaled(width)) / RuntimeEnv.GUIScale;

        public double Width(UiFont font, string text) => Font(font).GetTextExtents(text).XAdvance / RuntimeEnv.GUIScale;
    }
}
