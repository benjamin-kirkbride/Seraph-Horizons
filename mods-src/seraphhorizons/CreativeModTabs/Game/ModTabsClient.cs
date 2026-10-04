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
/// exactly vanilla. In mod mode <c>tabs</c> is a second list (<see cref="modModeTabs"/>): the default tabs the dialog
/// puts in its left column plus every mod tab, arranged (<see cref="TabLayout.ForModMode"/>) so that the dialog's own
/// compose keeps that left column and makes the mod tabs its right column. Everything else is the game's: its tab
/// clicks select tabs, slot clicks carry the mod tab's index to the server (which has the same tab), and TooManyTabs
/// scrolls the right column as in default mode. The mod tabs' names reach the dialog through lang entries
/// (<c>tabname-&lt;code&gt;</c>, <see cref="RegisterTabNames"/>). The button lives in its own composer
/// (<see cref="OverlayName"/>) above the right column.
///
/// Failures are logged once per place (<see cref="Fail"/>) and leave the game's tabs as they are.
/// </summary>
internal static class ModTabsClient
{
    public const string HarmonyId = "seraphhorizons.modtabs.client";
    public const string StateFile = "seraphhorizons-creativemodtabs.json";
    /// <summary>The overlay composer's name, also its key in the dialog's composers.</summary>
    public const string OverlayName = "seraphhorizons-creativemodtabs";
    /// <summary>Where the game's right-hand tab column starts in the creative dialog (ComposeCreativeInvDialog).</summary>
    const double ColumnTop = 35;

    static ICoreClientAPI? capi;
    static Harmony? harmony;
    static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    static ModTabsState state = new();

    static ModTabsPacket? announced;
    static string? buildError;
    // Built for one inventory from the announcement.
    static InventoryPlayerCreative? inv;
    static CreativeTabs? defaultTabs;
    /// <summary>Mod mode's tab list: the left column's default tabs and the mod tabs, the same tab objects.</summary>
    static CreativeTabs? modModeTabs;
    static List<CreativeTab> modTabs = [];
    /// <summary>Lang key (<c>game:tabname-&lt;code&gt;</c>) to entry, per mod tab.</summary>
    static Dictionary<string, string> tabNames = new(StringComparer.Ordinal);
    static List<int>[] members = [];
    static CreativeStacks? stacks;
    static bool ready, polling, persistedApplied;
    static int polls;
    static GuiDialogInventory? watched;

    static GuiComposer? overlay;
    static ElementBounds? overlayParent;
    static bool overlayModMode;

    static AccessTools.FieldRef<GuiDialogInventory, GuiComposer>? composerRef;
    static AccessTools.FieldRef<GuiDialogInventory, int>? currentTabIndexRef;

    public static void Init(ICoreClientAPI api)
    {
        Reset();
        capi = api;
        composerRef = Bind(() => AccessTools.FieldRefAccess<GuiDialogInventory, GuiComposer>("creativeInvDialog"), "GuiDialogInventory.creativeInvDialog");
        currentTabIndexRef = Bind(() => AccessTools.FieldRefAccess<GuiDialogInventory, int>("currentTabIndex"), "GuiDialogInventory.currentTabIndex");
        if (composerRef is null || currentTabIndexRef is null)
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
        // Leaving the world: keep the tab last selected, then give the inventory its own list back.
        try { if (Remember()) SaveState(); } catch { /* shutting down */ }
        if (watched is not null) watched.OnClosed -= OnDialogClosed;
        watched = null;
        if (inv is not null && defaultTabs is not null) inv.tabs = defaultTabs;
        capi = null;
        failed.Clear();
        state = new ModTabsState();
        announced = null;
        buildError = null;
        inv = null;
        defaultTabs = null;
        modModeTabs = null;
        modTabs = [];
        tabNames = new(StringComparer.Ordinal);
        members = [];
        stacks = null;
        ready = polling = persistedApplied = false;
        polls = 0;
        overlay = null;
        overlayParent = null;
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

    /// <summary>Mod mode: the inventory holds <see cref="modModeTabs"/> instead of the game's own tab list.</summary>
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
        stacks = scan;
        inv = creative;
        defaultTabs = creative.tabs;
        modModeTabs = BuildModModeTabs(defaultTabs);
        string gameName = Lang.Get("seraphhorizons:creativemodtabs-tab-game");
        for (int k = 0; k < modTabs.Count; k++)
            tabNames["game:tabname-" + modTabs[k].Code] = TabLayout.LangValue(list[k].IsGame ? gameName : list[k].Name);
        capi.Logger.Notification("[seraphhorizons] Creative mod tabs: {0} mod tabs with {1} stacks built", modTabs.Count, scan.Refs.Count);
        PollCaches();
    }

    /// <summary>
    /// Mod mode's tab list (<see cref="TabLayout.ForModMode"/>): the default tabs the dialog puts in its left column
    /// and the mod tabs, in the order that makes the dialog show the same left column and the mod tabs as its right
    /// column. List orders are read as the dialog reads them (<c>config/creativetabs.json</c>, which mods can patch).
    /// </summary>
    static CreativeTabs BuildModModeTabs(CreativeTabs defaults)
    {
        var configs = capi?.Assets.TryGet("config/creativetabs.json")?.ToObject<CreativeTabsConfig>()?.TabConfigs ?? [];
        double ListOrder(string code) => configs.FirstOrDefault(c => c?.Code == code)?.ListOrder ?? TabLayout.UnlistedOrder;
        var layout = TabLayout.ForModMode(defaults.Tabs.Select(t => t.Code).ToList(), modTabs.Select(t => t.Code).ToList(), ListOrder);
        if (!layout.Ideal)
            capi?.Logger.Notification("[seraphhorizons] Creative mod tabs: with {0} default tabs and this tab order, the mod tabs share the "
                + "left column or follow a default tab there; every tab is shown, as the game orders them", defaults.TabsByCode.Count);
        var byCode = defaults.Tabs.Concat(modTabs).ToDictionary(t => t.Code, StringComparer.Ordinal);
        var result = new CreativeTabs();
        foreach (string code in layout.Iteration)
            result.TabsByCode.Add(code, byCode[code]);   // not Add(): that would renumber the tab
        return result;
    }

    /// <summary>
    /// Makes the dialog's <c>Lang.Get("tabname-" + code)</c> name each mod tab: adds the entries to the current
    /// language and the fallback one (the game's lookup falls back to it), where missing. There is no API to add a
    /// lang entry; <c>GetAllEntries</c> hands out the language's own dictionary. Called once the game's background
    /// pass over the default tabs is done (it reads lang entries), and before each flip to mod mode, in case the
    /// language was reloaded since.
    /// </summary>
    static void RegisterTabNames()
    {
        foreach (string? locale in new[] { Lang.CurrentLocale, Lang.DefaultLocale }.Distinct())
        {
            if (locale is null || !Lang.AvailableLanguages.TryGetValue(locale, out var language)) continue;
            var entries = language.GetAllEntries();
            foreach (var (key, value) in tabNames)
                if (!entries.TryGetValue(key, out var old) || old != value) entries[key] = value;
        }
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
        try { RegisterTabNames(); }
        catch (Exception ex) { Fail("naming the mod tabs", ex); }
        if (Dialog() is { } dialog && !ReferenceEquals(watched, dialog))
        {
            if (watched is not null) watched.OnClosed -= OnDialogClosed;
            watched = dialog;
            dialog.OnClosed += OnDialogClosed;
        }
        if (!persistedApplied)
        {
            persistedApplied = true;
            if (state.Mode == TabsMode.Mod) SetMode(TabsMode.Mod, restore: true);
        }
        RefreshOverlay();
    }

    // ---- the mode -----------------------------------------------------------------------------------

    /// <summary>Notes the selected tab as its mode's last one; true if that changed. The dialog's own tab clicks
    /// select tabs, so this reads the inventory's current tab when it matters: on a flip, on closing the dialog,
    /// on leaving the world.</summary>
    static bool Remember()
    {
        if (!ready || inv?.CurrentTab?.Code is not { } code) return false;
        if (InModMode)
        {
            if (state.ModTab == code) return false;
            state.ModTab = code;
        }
        else
        {
            if (state.DefaultTab == code) return false;
            state.DefaultTab = code;
        }
        return true;
    }

    static void OnDialogClosed()
    {
        try { if (Remember()) SaveState(); }
        catch (Exception ex) { Fail("remembering the tab", ex); }
    }

    /// <summary>
    /// Swaps the inventory's tab list and rebuilds the dialog. A flip stays on the current tab when the other list
    /// has it too (a default tab of the left column), else goes to that mode's last tab, else its first.
    /// <paramref name="restore"/>: a saved mod mode applied after joining, which goes to the saved tab; the default
    /// tab then is just the game's first, and the saved one must survive.
    /// </summary>
    static void SetMode(TabsMode mode, bool restore = false)
    {
        if (!ready || inv is null || defaultTabs is null || modModeTabs is null || modTabs.Count == 0 || capi is null) return;
        bool toMod = mode == TabsMode.Mod;
        if (toMod == InModMode) return;
        // The dialog's own current tab must follow, or its next build selects a tab the list doesn't have.
        if (Dialog() is not { } dialog || currentTabIndexRef is null) return;

        if (!restore) Remember();
        var target = toMod ? modModeTabs : defaultTabs;
        string? current = inv.CurrentTab?.Code, last = toMod ? state.ModTab : state.DefaultTab;
        CreativeTab tab = !restore && current is not null && target.TabsByCode.TryGetValue(current, out var same) ? same
            : last is not null && target.TabsByCode.TryGetValue(last, out var remembered) ? remembered
            : toMod ? modTabs[0]
            : defaultTabs.Tabs.FirstOrDefault(t => t.Index == 0) ?? defaultTabs.Tabs.First();

        if (toMod)
        {
            try { RegisterTabNames(); }
            catch (Exception ex) { Fail("naming the mod tabs", ex); }
        }
        inv.tabs = target;
        state.Mode = mode;
        if (toMod) state.ModTab = tab.Code;
        else state.DefaultTab = tab.Code;
        currentTabIndexRef(dialog) = tab.Index;
        inv.SetTab(tab.Index);
        if (!restore) SaveState();
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

    // ---- the dialog ---------------------------------------------------------------------------------

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
    /// The overlay: the mode button in its own composer, placed as the game places its right-hand tab column (a
    /// child of the dialog's bounds, at its right edge), just above where that column starts. Its own composer
    /// because the creative composer is the game's to build: the button comes and goes with the mod tabs' readiness
    /// and the game mode without the dialog being rebuilt, and nothing is added to what the game, TooManyTabs and
    /// Dovidarium compose and inspect.
    /// </summary>
    static void BuildOverlay(GuiDialogInventory dialog, GuiComposer main)
    {
        if (capi is null) return;
        bool mod = InModMode;
        var font = TabLook.Font();
        string label = Lang.Get(mod ? "seraphhorizons:creativemodtabs-mode-mod" : "seraphhorizons:creativemodtabs-mode-default");
        double bw = ModeButton.FixedWidthFor(font, label);
        var root = ElementBounds.Fixed(0, 0, bw, ColumnTop - 2).FixedRightOf(main.Bounds).WithFixedAlignmentOffset(-4, 0);
        root.ParentBounds = main.Bounds;

        // Created once, then cleared and refilled: creating composers while the game recomposes all of
        // them would change the collection it iterates.
        if (overlay is null) overlay = capi.Gui.CreateCompo(OverlayName, root);
        else overlay.Clear(root);
        overlay.AddInteractiveElement(new ModeButton(capi, label, font, TabLook.SelectedFont(), ElementBounds.Fixed(0, 3, bw, 28), OnModeButton), "mode");
        overlay.Compose(focusFirstElement: false);
        overlayParent = main.Bounds;
        overlayModMode = mod;
        AddOverlayFirst(dialog, overlay);
    }

    /// <summary>
    /// Puts the overlay ahead of "maininventory" in the dialog's composers: <c>GuiDialog.OnRenderGUI</c> takes
    /// <c>MouseOverCursor</c> from each composer in turn, so the creative composer must come last or the search box
    /// loses its text cursor. The composers are an order-keeping dictionary: setting an existing key keeps its place
    /// (the game's and Dovidarium's <c>Composers["maininventory"] = x</c>), a new key goes last, and removing keeps the
    /// rest in order. So: when the overlay is not in yet, take "maininventory" out and put it back after the overlay.
    /// When the game itself removes and re-adds "maininventory" (mode change, backpack resize) it lands after the
    /// overlay anyway. Every reader iterates a snapshot (<c>ToArray</c> or the dictionary's snapshot enumerator), and
    /// the same composer object goes back under the same key, so Dovidarium's composer reuse sees no change.
    /// </summary>
    static void AddOverlayFirst(GuiDialogInventory dialog, GuiComposer composer)
    {
        var composers = dialog.Composers;
        if (composers.ContainsKey(OverlayName))
        {
            composers[OverlayName] = composer;   // already ahead of it; keeps its place
            return;
        }
        var main = composers["maininventory"];
        if (main is not null) composers.Remove("maininventory");
        composers[OverlayName] = composer;
        if (main is not null) composers["maininventory"] = main;
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
