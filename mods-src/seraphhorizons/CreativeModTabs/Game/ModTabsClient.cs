using HarmonyLib;
using SeraphHorizons.Mod.CreativeModTabs.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>
/// The client side of the creative mod tabs (main thread only; docs/variant-grouping/creative-mod-tabs.md).
///
/// <b>The tabs.</b> From the server's list (<see cref="OnPacket"/>) and this side's own creative stacks, it builds the
/// mod tabs as real <see cref="CreativeTab"/>s with the server's indices, checks them against the server's counts
/// and hashes, and keeps them apart from the inventory's own <c>tabs</c>. Their search caches are copied from the
/// default tabs' once the game has built those (same stack, same text), and only then does the button show.
///
/// <b>The mode.</b> In default mode the inventory's <c>tabs</c> is the game's own object, untouched, so the dialog is
/// exactly vanilla. In mod mode <c>tabs</c> holds only the selected mod tab: the dialog then composes one tab, which
/// <see cref="BeforeCreativeCompose"/> swaps for a <see cref="HiddenVerticalTabs"/>, and the mod tabs are shown in
/// their own composer (<see cref="OverlayName"/>, the button and the <see cref="ModTabStrip"/>) next to the dialog's.
/// Selecting a mod tab swaps that one tab in and calls the dialog's own <c>OnTabClicked</c>, so the grid, search and
/// Tidy Variants run as for any tab, and slot clicks carry the mod tab's index to the server, which has the same tab.
///
/// Failures are logged once per place (<see cref="Fail"/>) and leave the game's tabs as they are.
/// </summary>
internal static class ModTabsClient
{
    public const string HarmonyId = "seraphhorizons.modtabs.client";
    public const string StateFile = "seraphhorizons-creativemodtabs.json";
    /// <summary>The overlay composer's name, also its key in the dialog's composers.</summary>
    public const string OverlayName = "seraphhorizons-creativemodtabs";
    /// <summary>Where the game puts its right-hand tab column in the creative dialog (ComposeCreativeInvDialog).</summary>
    const double StripY = 35, StripHeight = 545;

    static ICoreClientAPI? capi;
    static Harmony? harmony;
    static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    static ModTabsState state = new();

    static ModTabsPacket? announced;
    static string? buildError;
    // Built for one inventory from the announcement.
    static InventoryPlayerCreative? inv;
    static CreativeTabs? defaultTabs;
    static List<CreativeTab> modTabs = [];
    static List<ModTabSpec> specs = [];
    static CreativeTabs?[] singles = [];
    static List<int>[] members = [];
    static CreativeStacks? stacks;
    static bool ready, polling, persistedApplied;
    static int polls;

    static GuiComposer? overlay;
    static ElementBounds? overlayParent;
    static bool overlayModMode;
    static ModTabStrip? strip;
    static double stripScroll;

    static AccessTools.FieldRef<GuiDialogInventory, GuiComposer>? composerRef;
    static AccessTools.FieldRef<GuiDialogInventory, int>? currentTabIndexRef;
    static Action<GuiDialogInventory, int, GuiTab>? onTabClicked;
    static AccessTools.FieldRef<GuiComposer, Dictionary<string, GuiElement>>? interactiveRef, staticRef;
    static AccessTools.FieldRef<GuiElementVerticalTabs, GuiTab[]>? tabsRef;

    public static void Init(ICoreClientAPI api)
    {
        Reset();
        capi = api;
        composerRef = Bind(() => AccessTools.FieldRefAccess<GuiDialogInventory, GuiComposer>("creativeInvDialog"), "GuiDialogInventory.creativeInvDialog");
        currentTabIndexRef = Bind(() => AccessTools.FieldRefAccess<GuiDialogInventory, int>("currentTabIndex"), "GuiDialogInventory.currentTabIndex");
        onTabClicked = Bind(() => AccessTools.MethodDelegate<Action<GuiDialogInventory, int, GuiTab>>(
            AccessTools.DeclaredMethod(typeof(GuiDialogInventory), "OnTabClicked", [typeof(int), typeof(GuiTab)])), "GuiDialogInventory.OnTabClicked");
        interactiveRef = Bind(() => AccessTools.FieldRefAccess<GuiComposer, Dictionary<string, GuiElement>>("interactiveElements"), "GuiComposer.interactiveElements");
        staticRef = Bind(() => AccessTools.FieldRefAccess<GuiComposer, Dictionary<string, GuiElement>>("staticElements"), "GuiComposer.staticElements");
        tabsRef = Bind(() => AccessTools.FieldRefAccess<GuiElementVerticalTabs, GuiTab[]>("tabs"), "GuiElementVerticalTabs.tabs");
        if (composerRef is null || currentTabIndexRef is null || onTabClicked is null || interactiveRef is null || staticRef is null || tabsRef is null)
        {
            api.Logger.Warning("[seraphhorizons] Creative mod tabs: the creative dialog has changed shape; no mod tabs");
            return;
        }
        state = LoadState();
        harmony = new Harmony(HarmonyId);
        if (!ModTabsPatches.Apply(harmony, api.Logger)) harmony = null;
        else api.Logger.Notification("[seraphhorizons] Creative mod tabs: client patch applied; waiting for the server's tab list");
    }

    public static void Reset()
    {
        if (harmony is not null)
        {
            try { harmony.UnpatchAll(HarmonyId); } catch { /* shutting down */ }
            harmony = null;
        }
        if (inv is not null && defaultTabs is not null) inv.tabs = defaultTabs;
        capi = null;
        failed.Clear();
        state = new ModTabsState();
        announced = null;
        buildError = null;
        inv = null;
        defaultTabs = null;
        modTabs = [];
        specs = [];
        singles = [];
        members = [];
        stacks = null;
        ready = polling = persistedApplied = false;
        polls = 0;
        overlay = null;
        overlayParent = null;
        strip = null;
        stripScroll = 0;
    }

    static T? Bind<T>(Func<T> get, string what) where T : class
    {
        try { return get() ?? throw new MissingMemberException(what); }
        catch (Exception ex)
        {
            capi?.Logger.Warning("[seraphhorizons] Creative mod tabs: {0} not found ({1})", what, ex.Message);
            return null;
        }
    }

    /// <summary>Logs the first failure per place.</summary>
    public static void Fail(string where, Exception ex)
    {
        if (failed.Add(where))
            capi?.Logger.Error("[seraphhorizons] Creative mod tabs: {0} failed (logged once): {1}", where, ex);
    }

    static bool Active => capi is not null && harmony is not null;

    /// <summary>Mod mode: the inventory holds one of our tabs instead of the game's own tab list.</summary>
    static bool InModMode => inv is not null && defaultTabs is not null && !ReferenceEquals(inv.tabs, defaultTabs);

    static bool IsCreative => capi?.World?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    static GuiDialogInventory? Dialog() => capi?.Gui.LoadedGuis.OfType<GuiDialogInventory>().FirstOrDefault();

    // ---- the tabs -----------------------------------------------------------------------------------

    /// <summary>The server's list arrived (once per join, after the client finished loading).</summary>
    public static void OnPacket(ModTabsPacket packet)
    {
        if (!Active) return;
        announced = packet;
        try { TryBuild(); }
        catch (Exception ex) { Fail("building the mod tabs", ex); buildError = ex.Message; }
    }

    /// <summary>Builds the mod tabs once the list is here and the game has built the creative inventory.
    /// Called again from every creative compose until it has.</summary>
    static void TryBuild()
    {
        if (announced is null || modTabs.Count > 0 || buildError is not null || capi is null) return;
        if (capi.World?.Player?.InventoryManager?.GetOwnInventory("creative") is not InventoryPlayerCreative creative
            || creative.tabs.TabsByCode.Count == 0) return;
        if (announced.Version != ModTabsPacket.CurrentVersion) { Refuse($"the server's list is version {announced.Version}"); return; }
        var list = announced.Specs();
        if (list.Count == 0) { Refuse("the server has no mod tabs"); return; }
        if (creative.tabs.TabsByCode.Count != announced.DefaultTabCount)
        {
            Refuse($"{creative.tabs.TabsByCode.Count} default tabs here, {announced.DefaultTabCount} on the server");
            return;
        }
        var scan = CreativeStacks.Scan(capi.World);
        var assignment = ModTabPlanner.Assign(scan.Refs, list);
        if (ModTabPlanner.Verify(scan.Refs, list, assignment) is { } mismatch) { Refuse(mismatch); return; }

        members = scan.Members(list, assignment);
        modTabs = scan.BuildTabs(creative, capi, list, members, announced.DefaultTabCount);
        specs = list;
        stacks = scan;
        singles = new CreativeTabs?[modTabs.Count];
        inv = creative;
        defaultTabs = creative.tabs;
        capi.Logger.Notification("[seraphhorizons] Creative mod tabs: {0} mod tabs with {1} stacks built", modTabs.Count, scan.Refs.Count);
        PollCaches();
    }

    static void Refuse(string why)
    {
        buildError = why;
        capi?.Logger.Warning("[seraphhorizons] Creative mod tabs: not shown, the client's creative stacks don't match the server's list: {0}", why);
    }

    /// <summary>The game builds the default tabs' search caches on the thread pool after the dialog's first build.
    /// Waits for them (polling twice a second), then copies them into the mod tabs and shows the button.</summary>
    static void PollCaches()
    {
        if (ready || polling || capi is null || defaultTabs is null) return;
        bool done = defaultTabs.Tabs.All(t => t.SearchCache is not null && t.SearchCacheNames is not null);
        if (!done && ++polls < 240)
        {
            polling = true;
            capi.Event.RegisterCallback(_ => { polling = false; PollCaches(); }, 500);
            return;
        }
        if (!done) capi.Logger.Warning("[seraphhorizons] Creative mod tabs: the game's search caches are still missing after 2 minutes; building the mod tabs' own");
        try { FillSearchCaches(); }
        catch (Exception ex) { Fail("the mod tabs' search", ex); }
        ready = true;
        OnReady();
    }

    /// <summary>Each mod tab slot gets the search text of the first default slot with the same stack
    /// (<c>CreativeTab.CreateSearchCache</c>'s, the same text), else computes it the way that method does.</summary>
    static void FillSearchCaches()
    {
        if (capi is null || defaultTabs is null || stacks is null) return;
        var world = capi.World;
        var from = new Dictionary<string, (CreativeTab Tab, int Slot)>(StringComparer.Ordinal);
        foreach (var tab in defaultTabs.Tabs)
            for (int i = 0; i < tab.Inventory.Count; i++)
                if (tab.Inventory[i]?.Itemstack is { } s) from.TryAdd(CreativeStacks.KeyOf(s, world), (tab, i));

        for (int t = 0; t < modTabs.Count; t++)
        {
            var tab = modTabs[t];
            var names = new Dictionary<int, string>(members[t].Count);
            var cache = new Dictionary<int, string>(members[t].Count);
            for (int s = 0; s < members[t].Count; s++)
            {
                if (from.TryGetValue(stacks.Refs[members[t][s]].Key, out var src)
                    && src.Tab.SearchCacheNames?.TryGetValue(src.Slot, out var n) == true
                    && src.Tab.SearchCache?.TryGetValue(src.Slot, out var c) == true)
                {
                    names[s] = n;
                    cache[s] = c;
                    continue;
                }
                var slot = tab.Inventory[s];
                if (slot?.Itemstack is not { } stack) continue;
                string name = stack.GetName();
                names[s] = name.ToSearchFriendly().ToLowerInvariant();
                cache[s] = name + " " + ((stack.Collectible as ISearchTextProvider)?.GetSearchText(world, slot)
                    ?? stack.GetDescription(world, slot).ToSearchFriendly().ToLowerInvariant());
            }
            tab.SearchCacheNames = names;
            tab.SearchCache = cache;
        }
    }

    static void OnReady()
    {
        if (!persistedApplied)
        {
            persistedApplied = true;
            if (state.Mode == TabsMode.Mod) SetMode(TabsMode.Mod, save: false);
        }
        RefreshOverlay();
    }

    static CreativeTabs Single(int k)
    {
        if (singles[k] is { } c) return c;
        c = new CreativeTabs();
        c.TabsByCode.Add(modTabs[k].Code, modTabs[k]);   // not Add(): that would renumber the tab
        return singles[k] = c;
    }

    static int IndexOfMod(string? code) => code is null ? -1 : modTabs.FindIndex(t => t.Code == code);

    // ---- the mode -----------------------------------------------------------------------------------

    static void SetMode(TabsMode mode, bool save = true)
    {
        if (!ready || inv is null || defaultTabs is null || modTabs.Count == 0 || capi is null) return;
        bool toMod = mode == TabsMode.Mod;
        if (toMod == InModMode) return;
        // The dialog's own current tab must follow, or its next build selects a tab the list doesn't have.
        if (Dialog() is not { } dialog || currentTabIndexRef is null) return;

        // Remember where we were in the mode we leave.
        string? current = inv.CurrentTab?.Code;
        if (InModMode) state.ModTab = current ?? state.ModTab;
        else state.DefaultTab = current ?? state.DefaultTab;

        CreativeTab target;
        if (toMod)
        {
            int k = Math.Max(0, IndexOfMod(state.ModTab));
            target = modTabs[k];
            inv.tabs = Single(k);
        }
        else
        {
            target = defaultTabs.Tabs.FirstOrDefault(t => t.Code == state.DefaultTab)
                     ?? defaultTabs.Tabs.FirstOrDefault(t => t.Index == 0) ?? defaultTabs.Tabs.First();
            inv.tabs = defaultTabs;
        }
        state.Mode = mode;
        currentTabIndexRef(dialog) = target.Index;
        inv.SetTab(target.Index);
        if (save) SaveState();
        Recompose(dialog);
    }

    /// <summary>Rebuilds the creative dialog for the new tab list, keeping the search text. Dovidarium's
    /// composer reuse sees the tab list changed and lets the build run.</summary>
    static void Recompose(GuiDialogInventory? dialog)
    {
        if (dialog is null || composerRef is null || !IsCreative || composerRef(dialog) is not { } main) return;
        string? text = main.GetTextInput("searchbox")?.GetText();
        dialog.ComposeGui(firstBuild: false);
        if (!string.IsNullOrEmpty(text)) composerRef(dialog)?.GetTextInput("searchbox")?.SetValue(text);
    }

    static void OnModeButton()
    {
        // Deferred: flipping rebuilds the overlay composer, whose mouse handler is running right now.
        capi?.Event.EnqueueMainThreadTask(() =>
        {
            try { SetMode(InModMode ? TabsMode.Default : TabsMode.Mod); }
            catch (Exception ex) { Fail("switching the tabs", ex); }
        }, "seraphhorizons-creativemodtabs-flip");
    }

    static void SelectModTab(int k)
    {
        try
        {
            if (!InModMode || inv is null || k < 0 || k >= modTabs.Count || onTabClicked is null || Dialog() is not { } dialog) return;
            var tab = modTabs[k];
            inv.tabs = Single(k);
            capi!.Gui.PlaySound("menubutton_wood");
            // The dialog's own tab click: sets its current tab, the inventory's, the grid and the search.
            onTabClicked(dialog, 0, new GuiTab { DataInt = tab.Index, Name = specs[k].Name, Active = true });
            strip?.SetActive(k);
            state.ModTab = tab.Code;
            SaveState();
        }
        catch (Exception ex) { Fail("selecting a mod tab", ex); }
    }

    // ---- the dialog ---------------------------------------------------------------------------------

    /// <summary>Before the creative composer composes, in mod mode: swap the dialog's one-tab column for a hidden one.</summary>
    public static void BeforeCreativeCompose(GuiComposer main)
    {
        if (!InModMode || capi is null || interactiveRef is null || staticRef is null || tabsRef is null) return;
        var inter = interactiveRef(main);
        var stat = staticRef(main);
        foreach (string key in (string[])["verticalTabs", "verticalTabsR"])
        {
            if (!inter.TryGetValue(key, out var el) || el is not GuiElementVerticalTabs old || old is HiddenVerticalTabs) continue;
            // Same bounds object, so the composer's bounds tree is unchanged.
            var hidden = new HiddenVerticalTabs(capi, tabsRef(old), old.Bounds)
            {
                TabIndex = old.TabIndex, InsideClipBounds = old.InsideClipBounds, RenderAsPremultipliedAlpha = old.RenderAsPremultipliedAlpha,
            };
            inter[key] = hidden;
            if (stat.ContainsKey(key)) stat[key] = hidden;
            old.Dispose();
        }
    }

    /// <summary>After the creative composer composed: show the overlay (building the tabs first if the list came
    /// before the inventory). A recompose of the same composer (window resize, GUI scale) only recomposes it.</summary>
    public static void AfterCreativeCompose(GuiComposer main)
    {
        if (!Active) return;
        if (!ready)
        {
            TryBuild();
            PollCaches();
            return;
        }
        if (Dialog() is not { } dialog || composerRef is null || !ReferenceEquals(composerRef(dialog), main)) return;
        if (!IsCreative) { RemoveOverlay(dialog); return; }
        if (overlay is not null && ReferenceEquals(overlayParent, main.Bounds) && overlayModMode == InModMode
            && ReferenceEquals(dialog.Composers[OverlayName], overlay))
        {
            overlay.ReCompose();
            return;
        }
        BuildOverlay(dialog, main);
    }

    /// <summary>The survival composer composed: out of creative, the overlay goes. (In creative the game also
    /// composes it, when the backpack size changes, and keeps the creative one shown.)</summary>
    public static void AfterSurvivalCompose()
    {
        if (Active && !IsCreative && Dialog() is { } dialog) RemoveOverlay(dialog);
    }

    static void RefreshOverlay()
    {
        try
        {
            if (Dialog() is not { } dialog || composerRef is null) return;
            if (!ready || !IsCreative || composerRef(dialog) is not { Composed: true } main) { RemoveOverlay(dialog); return; }
            BuildOverlay(dialog, main);
        }
        catch (Exception ex) { Fail("the mod tab button", ex); }
    }

    static void RemoveOverlay(GuiDialogInventory dialog)
    {
        if (dialog.Composers.ContainsKey(OverlayName)) dialog.Composers.Remove(OverlayName);
    }

    /// <summary>
    /// The overlay: its own composer, so the game routes the mouse wheel to it (the GUI manager hands the wheel
    /// to a dialog whose composer bounds hold the mouse; the dialog's own tab column lies outside its composer).
    /// Placed as the game places its right-hand tab column: a child of the dialog's bounds, at its right edge,
    /// the button above where the column starts and, in mod mode, the strip where the column is.
    /// </summary>
    static void BuildOverlay(GuiDialogInventory dialog, GuiComposer main)
    {
        if (capi is null) return;
        bool mod = InModMode;
        var font = TabLook.Font();
        var selected = TabLook.SelectedFont();
        string label = Lang.Get(mod ? "seraphhorizons:creativemodtabs-mode-mod" : "seraphhorizons:creativemodtabs-mode-default");
        string[] names = mod ? specs.Select(s => s.IsGame ? Lang.Get("seraphhorizons:creativemodtabs-tab-game") : s.Name).ToArray() : [];
        double bw = ModeButton.FixedWidthFor(font, label);
        double sw = mod ? ModTabStrip.FixedWidthFor(font, names) : 0;
        var root = ElementBounds.Fixed(0, 0, Math.Max(bw, sw), mod ? StripY + StripHeight : StripY - 2)
            .FixedRightOf(main.Bounds).WithFixedAlignmentOffset(-4, 0);
        root.ParentBounds = main.Bounds;

        // Created once, then cleared and refilled: creating composers while the game recomposes all of
        // them would change the collection it iterates.
        if (overlay is null) overlay = capi.Gui.CreateCompo(OverlayName, root);
        else overlay.Clear(root);
        overlay.AddInteractiveElement(new ModeButton(capi, label, font, selected, ElementBounds.Fixed(0, 3, bw, 28), OnModeButton), "mode");
        strip = null;
        if (mod)
        {
            int active = inv?.CurrentTab is { } cur ? modTabs.IndexOf(cur) : -1;
            strip = new ModTabStrip(capi, names, font, selected, ElementBounds.Fixed(0, StripY, sw, StripHeight),
                active, stripScroll, SelectModTab, s => stripScroll = s);
            overlay.AddInteractiveElement(strip, "tabs");
        }
        overlay.Compose(focusFirstElement: false);
        overlayParent = main.Bounds;
        overlayModMode = mod;
        dialog.Composers[OverlayName] = overlay;
    }

    // ---- persistence --------------------------------------------------------------------------------

    static string StatePath => Path.Combine(GamePaths.ModConfig, StateFile);

    static ModTabsState LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return new ModTabsState();
            var s = ModTabsState.Parse(File.ReadAllText(StatePath), out string? error);
            if (error is not null)
                capi?.Logger.Warning("[seraphhorizons] Creative mod tabs: could not read ModConfig/{0}, starting with the default tabs: {1}", StateFile, error);
            return s;
        }
        catch (Exception ex)
        {
            capi?.Logger.Warning("[seraphhorizons] Creative mod tabs: could not read ModConfig/{0}, starting with the default tabs: {1}", StateFile, ex.Message);
            return new ModTabsState();
        }
    }

    static void SaveState()
    {
        try
        {
            Directory.CreateDirectory(GamePaths.ModConfig);
            File.WriteAllText(StatePath, state.ToJson());
        }
        catch (Exception ex) { Fail("saving ModConfig/" + StateFile, ex); }
    }
}
