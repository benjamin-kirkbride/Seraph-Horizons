using System.Reflection;
using System.Text;
using HarmonyLib;
using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace SeraphHorizons.SeraphTweaks.TidyVariants;

/// <summary>Per-client expand state, saved as ModConfig/<see cref="CreativeUi.ConfigFile"/>.</summary>
public sealed class TidyCreativeConfig
{
    public int Version { get; set; } = 1;
    /// <summary>Ids of the groups the player expanded.</summary>
    public List<string> Expanded { get; set; } = [];
}

/// <summary>
/// The creative inventory UI behind <see cref="CreativePatches"/> (client main thread only).
///
/// Never touches the creative inventory itself: slot ids and tabs stay the server's. It only rewrites the
/// client grid's <c>availableSlots</c> (hide) and <c>renderedSlots</c> (group, after search), in place,
/// with the real slot ids, so a tile is the representative's own slot and vanilla click handling, the
/// server's slot resolution, Dovidarium's search cache and visible-slot window all keep working.
/// The grouping logic itself is <see cref="CreativeView"/> (Core, unit-tested).
///
/// Robustness: with no resolution (bridge null) everything is a no-op; every entry point is wrapped by the
/// patches, which call <see cref="Fail"/> (logged once per place) and leave that call vanilla.
/// </summary>
internal static class CreativeUi
{
    public const string ConfigFile = "seraphtweaks-tidyvariants-creative.json";
    const float MarkZ = 160f;   // above slot items (z 90), below the dialog's next slab (ZSize 250)
    static readonly int TintColor = ColorUtil.ToRgba(235, 232, 176, 72);       // ARGB amber
    static readonly int BadgeFill = ColorUtil.ToRgba(255, 255, 255, 255);
    static readonly int BadgeRim = ColorUtil.ToRgba(255, 20, 16, 12);
    static readonly int TintColorAuto = ColorUtil.ToRgba(150, 232, 176, 72);

    static ICoreClientAPI? capi;
    static readonly HashSet<string> failed = new(StringComparer.Ordinal);
    static ExpandState expand = new();

    // Built for one bridge (one world); reset when the bridge changes.
    static TidyBridge? bridge;
    static CreativeView? view;
    static readonly Dictionary<ItemSlot, int> entryCache = new(ReferenceEqualityComparer.Instance);

    // The grouped grid as last written, for clicks, hotkey, tooltip and borders.
    static WeakReference<GuiElementItemSlotGridBase>? viewGrid;
    static WeakReference<GuiDialogInventory>? viewDialog;
    static ViewSlot[] rows = [];
    static int rowCount;
    static readonly List<int> groupedRows = [];
    static readonly Dictionary<int, int> rowBySlotId = [];
    static readonly Dictionary<ItemSlot, int> rowBySlot = new(ReferenceEqualityComparer.Instance);
    static float? pendingScrollY;

    // Input buffers for CreativeView.Build.
    static int[] bufSlotIds = [], bufEntries = [];
    static ItemSlot[] bufSlots = [];

    // Reflection (bound in Init; null means "that part stays vanilla").
    static AccessTools.FieldRef<GuiElementItemSlotGridBase, IInventory>? inventoryRef;
    static AccessTools.FieldRef<GuiElementItemSlotGridBase, int>? gridRowsRef;
    static AccessTools.FieldRef<GuiDialogInventory, GuiComposer>? composerRef;
    static AccessTools.FieldRef<GuiDialogInventory, int>? colsRef;
    static AccessTools.FieldRef<GuiElementItemSlotGridBase, bool>? rightDragRef;
    static MethodInfo? composeSlotOverlays;
    static Action<GuiDialogInventory>? updateDialog;

    public static void Init(ICoreClientAPI api)
    {
        Reset();
        capi = api;
        inventoryRef = Bind(() => AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, IInventory>("inventory"), "grid.inventory");
        gridRowsRef = Bind(() => AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, int>("rows"), "grid.rows");
        composerRef = Bind(() => AccessTools.FieldRefAccess<GuiDialogInventory, GuiComposer>("creativeInvDialog"), "GuiDialogInventory.creativeInvDialog");
        colsRef = Bind(() => AccessTools.FieldRefAccess<GuiDialogInventory, int>("cols"), "GuiDialogInventory.cols");
        rightDragRef = Bind(() => AccessTools.FieldRefAccess<GuiElementItemSlotGridBase, bool>("isRightMouseDownStartedInsideElem"),
            "grid.isRightMouseDownStartedInsideElem");
        composeSlotOverlays = Bind(() => AccessTools.DeclaredMethod(typeof(GuiElementItemSlotGridBase), "ComposeSlotOverlays",
            [typeof(ItemSlot), typeof(int), typeof(int)]), "grid.ComposeSlotOverlays");
        updateDialog = Bind(() => AccessTools.MethodDelegate<Action<GuiDialogInventory>>(
            AccessTools.DeclaredMethod(typeof(GuiDialogInventory), "update", Type.EmptyTypes)), "GuiDialogInventory.update");
        expand = new ExpandState(LoadConfig()?.Expanded);
    }

    public static void Reset()
    {
        capi = null;
        bridge = null;
        view = null;
        entryCache.Clear();
        ClearView();
        failed.Clear();
        pendingScrollY = null;
    }

    static T? Bind<T>(Func<T> get, string what) where T : class
    {
        try { return get() ?? throw new MissingMemberException(what); }
        catch (Exception ex)
        {
            capi?.Logger.Warning("[seraphtweaks] Tidy Variants creative inventory: {0} not found ({1}); that part stays vanilla", what, ex.Message);
            return null;
        }
    }

    /// <summary>Logs the first failure per place; the caller then leaves that call vanilla.</summary>
    public static void Fail(string where, Exception ex)
    {
        if (failed.Add(where))
            capi?.Logger.Error("[seraphtweaks] Tidy Variants creative inventory {0} failed; falling back to vanilla for it (logged once): {1}", where, ex);
    }

    // ---- state -------------------------------------------------------------------------------------

    /// <summary>The client's resolution; rebuilds the caches when it changed. Null means do nothing.</summary>
    static TidyBridge? Current()
    {
        var b = TidyVariantsModSystem.ForSide(EnumAppSide.Client);
        if (!ReferenceEquals(b, bridge))
        {
            bridge = b;
            view = b is null ? null : new CreativeView(b.Resolution);
            entryCache.Clear();
            ClearView();
        }
        return capi is null ? null : b;
    }

    static void ClearView()
    {
        viewGrid = null;
        viewDialog = null;
        rowCount = 0;
        groupedRows.Clear();
        rowBySlotId.Clear();
        rowBySlot.Clear();
    }

    static bool IsCreativeGrid(GuiElementItemSlotGridBase grid) =>
        inventoryRef is not null && string.Equals(inventoryRef(grid)?.ClassName, "creative", StringComparison.Ordinal);

    static int EntryOf(TidyBridge b, ItemSlot? slot)
    {
        if (slot is null) return -1;
        if (entryCache.TryGetValue(slot, out int e)) return e;
        e = b.EntryOf(slot.Itemstack);
        if (slot.Itemstack is not null) entryCache[slot] = e;
        return e;
    }

    /// <summary>The grid our rows describe, if it still shows exactly what we wrote.</summary>
    static bool IsViewCurrent(GuiElementItemSlotGridBase grid) =>
        viewGrid is not null && viewGrid.TryGetTarget(out var g) && ReferenceEquals(g, grid) && grid.renderedSlots.Count == rowCount;

    // ---- (a) hide: DetermineAvailableSlots postfix --------------------------------------------------

    public static void HideFrom(GuiElementItemSlotGrid grid)
    {
        if (Current() is not { } b || view is null || !IsCreativeGrid(grid)) return;
        var avail = grid.availableSlots;
        HashSet<int>? hidden = null;
        foreach (var kv in avail)
            if (view.IsHidden(EntryOf(b, kv.Value))) (hidden ??= []).Add(kv.Key);
        if (hidden is null) return;

        // Build both lists first, then swap them in: a failure above leaves the grid untouched.
        var keepAvail = new List<KeyValuePair<int, ItemSlot>>(avail.Count - hidden.Count);
        foreach (var kv in avail) if (!hidden.Contains(kv.Key)) keepAvail.Add(kv);
        var keepRendered = new List<KeyValuePair<int, ItemSlot>>(grid.renderedSlots.Count);
        foreach (var kv in grid.renderedSlots) if (!hidden.Contains(kv.Key)) keepRendered.Add(kv);

        avail.Clear();
        foreach (var kv in keepAvail) avail.Add(kv.Key, kv.Value);
        grid.renderedSlots.Clear();
        foreach (var kv in keepRendered) grid.renderedSlots.Add(kv.Key, kv.Value);
        ClearView();
    }

    // ---- (b) group after search: GuiDialogInventory.OnTextChanged postfix ---------------------------

    public static void Regroup(GuiDialogInventory dialog)
    {
        float? keepY = pendingScrollY;
        pendingScrollY = null;
        if (Current() is not { } b || view is null || composerRef is null || colsRef is null) return;
        if (capi!.World?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Creative) return;
        var composer = composerRef(dialog);
        if (composer?.GetSlotGrid("slotgrid") is not { } grid || !IsCreativeGrid(grid)) return;

        var rendered = grid.renderedSlots;
        int n = rendered.Count;
        if (bufSlotIds.Length < n) { bufSlotIds = new int[n]; bufEntries = new int[n]; bufSlots = new ItemSlot[n]; }
        int k = 0;
        foreach (var kv in rendered)
        {
            bufSlotIds[k] = kv.Key;
            bufSlots[k] = kv.Value;
            bufEntries[k] = EntryOf(b, kv.Value);
            k++;
        }
        view.Build(bufSlotIds.AsSpan(0, n), bufEntries.AsSpan(0, n), expand.IndicesFor(b.Resolution));
        var vs = view.Slots;
        int m = vs.Count;
        int cols = Math.Max(1, colsRef(dialog));

        bool changed = m != n;
        for (int i = 0; !changed && i < m; i++) changed = vs[i].SlotId != bufSlotIds[i];
        if (changed)
        {
            // In place: the dialog, Dovidarium's window patch and its compose reuse all hold this dictionary.
            rendered.Clear();
            for (int i = 0; i < m; i++) rendered.Add(vs[i].SlotId, bufSlots[vs[i].Position]);
            if (gridRowsRef is not null) gridRowsRef(grid) = (int)Math.Ceiling(m / (double)cols);
            RefreshOverlays(grid, vs, n);
        }
        Array.Clear(bufSlots, 0, n);

        Remember(dialog, grid, vs);
        UpdateScrollAndCount(composer, cols, m, view.ItemCount, keepY);
    }

    static void Remember(GuiDialogInventory dialog, GuiElementItemSlotGridBase grid, IReadOnlyList<ViewSlot> vs)
    {
        ClearView();
        if (rows.Length < vs.Count) rows = new ViewSlot[Math.Max(vs.Count, rows.Length * 2)];
        for (int i = 0; i < vs.Count; i++)
        {
            var row = vs[i];
            rows[i] = row;
            if (!row.IsGrouped) continue;
            groupedRows.Add(i);
            rowBySlotId[row.SlotId] = i;
            if (grid.renderedSlots.GetValueAtIndex(i) is { } slot) rowBySlot[slot] = i;
        }
        rowCount = vs.Count;
        viewGrid = new WeakReference<GuiElementItemSlotGridBase>(grid);
        viewDialog = new WeakReference<GuiDialogInventory>(dialog);
    }

    /// <summary>
    /// Durability overlays are per visual index (slotQuantityTextures). Recompose the ones whose slot moved,
    /// as Dovidarium's own RefreshOverlay does; ComposeSlotOverlays is called, not patched.
    /// </summary>
    static void RefreshOverlays(GuiElementItemSlotGridBase grid, IReadOnlyList<ViewSlot> vs, int oldCount)
    {
        var tex = grid.slotQuantityTextures;
        if (tex is null) return;
        for (int i = 0; i < vs.Count && i < tex.Length; i++)
        {
            if (i < oldCount && vs[i].SlotId == bufSlotIds[i]) continue;
            var slot = grid.renderedSlots.GetValueAtIndex(i);
            var stack = slot?.Itemstack;
            if (stack?.Collectible is { } coll && coll.ShouldDisplayItemDamage(stack) && composeSlotOverlays is not null
                && grid.SlotBounds is { } sb && i < sb.Length)
            {
                composeSlotOverlays.Invoke(grid, [slot, vs[i].SlotId, i]);
                continue;
            }
            var t = tex[i];
            if (t is null || t.Disposed || t.TextureId != 0)
            {
                t?.Dispose();
                tex[i] = new LoadedTexture(capi);
            }
        }
    }

    /// <summary>Redo what OnTextChanged did with the flat count: scrollbar height, scroll position, result count.</summary>
    static void UpdateScrollAndCount(GuiComposer composer, int cols, int shown, int items, float? keepY)
    {
        if (composer.GetScrollbar("scrollbar") is { } sb)
        {
            int gridRows = (int)Math.Ceiling(shown / (double)cols);
            float total = (float)(ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, cols, gridRows).fixedHeight + 3.0);
            sb.SetNewTotalHeight(total);
            if (keepY is { } y)
            {
                sb.CurrentYPosition = y;
                sb.SetNewTotalHeight(total);   // clamps the handle and moves the grid
            }
            else sb.SetScrollbarPosition(0);
        }
        string text = shown == items
            ? Lang.Get("creative-searchresults", shown)
            : Lang.Get("seraphtweaks:tidyvariants-creative-searchresults-grouped", items, shown);
        composer.GetDynamicText("searchResults")?.SetNewText(text);
    }

    // ---- (d) expand / collapse: right-click and hotkey ----------------------------------------------

    /// <summary>
    /// Right-click on a tile or a member of a group the player expanded, with an empty cursor: toggles the group.
    /// True if consumed (no packet is sent). Everything else stays vanilla (<see cref="CreativeView.RightClickTarget"/>).
    /// One toggle per press: vanilla calls SlotClick(Right) once on mouse-down, and from OnMouseMove only while
    /// <c>isRightMouseDownStartedInsideElem</c> is set, which needs an item on the cursor at the press; a toggle
    /// never sets it, so no drag state is left behind and moving the held button over the new layout does nothing.
    /// </summary>
    public static bool TryRightClick(GuiElementItemSlotGridBase grid, int slotId)
    {
        if (Current() is null || view is null || !IsViewCurrent(grid)) return false;
        if (!rowBySlotId.TryGetValue(slotId, out int r)) return false;
        bool cursorEmpty = capi!.World.Player.InventoryManager.MouseItemSlot?.Empty != false;
        bool dragging = rightDragRef is not null && rightDragRef(grid);   // missing field: the empty-cursor check alone
        if (CreativeView.RightClickTarget(rows[r], cursorEmpty, dragging) < 0) return false;
        Toggle(rows[r]);
        return true;
    }

    /// <summary>The hotkey (default Ctrl+G): toggles the group of the hovered creative slot.</summary>
    public static bool OnToggleHotkey(KeyCombination _)
    {
        try
        {
            if (Current() is null || view is null || viewDialog is null || viewGrid is null) return false;
            if (!viewDialog.TryGetTarget(out var dialog) || !dialog.IsOpened()) return false;
            if (!viewGrid.TryGetTarget(out var grid) || !IsViewCurrent(grid)) return false;
            if (composerRef is null || dialog.Composers["maininventory"] is not { } main || !ReferenceEquals(main, composerRef(dialog))) return false;
            int id = grid.hoverSlotId;
            if (id < 0 || !rowBySlotId.TryGetValue(id, out int r) || CreativeView.ToggleTarget(rows[r]) < 0) return false;
            Toggle(rows[r]);
            return true;
        }
        catch (Exception ex) { Fail("hotkey", ex); return false; }
    }

    static void Toggle(in ViewSlot row)
    {
        int g = CreativeView.ToggleTarget(row);
        if (g < 0 || bridge is null) return;
        expand.Toggle(bridge.Resolution, g);
        SaveConfig();
        if (updateDialog is null || viewDialog is null || !viewDialog.TryGetTarget(out var dialog)) return;
        // update() re-runs the search (cheap with Dovidarium's cache) and our OnTextChanged postfix; keep the scroll.
        pendingScrollY = composerRef?.Invoke(dialog)?.GetScrollbar("scrollbar")?.CurrentYPosition;
        try { updateDialog(dialog); }
        finally { pendingScrollY = null; }
    }

    // ---- (e) tooltip: ItemSlot.GetStackDescription postfix ------------------------------------------

    public static string DescribeSlot(ItemSlot slot, string desc)
    {
        if (rowCount == 0 || bridge is null || capi is null || !rowBySlot.TryGetValue(slot, out int r)) return desc;
        var row = rows[r];
        var res = bridge.Resolution;
        if ((uint)row.Group >= (uint)res.Groups.Count) return desc;
        var group = res.Groups[row.Group];

        string key = capi.Input.GetHotKeyByCode(TidyCreativeModSystem.HotkeyCode)?.CurrentMapping?.ToString() ?? "Ctrl+G";
        string hint = row.IsAutoExpanded ? Lang.Get("seraphtweaks:tidyvariants-creative-hint-auto")
            : row.IsTile ? Lang.Get("seraphtweaks:tidyvariants-creative-hint-expand", key)
            : Lang.Get("seraphtweaks:tidyvariants-creative-hint-collapse", key);

        var sb = new StringBuilder(desc ?? "");
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
        sb.Append("\n<font color=\"#e8b048\">")
          .Append(Lang.Get("seraphtweaks:tidyvariants-creative-group-title", GroupTitle(group), row.MemberCount))
          .Append("</font>\n<font color=\"#a0a0a0\">").Append(hint).Append("</font>");
        return sb.ToString();
    }

    static string GroupTitle(TidyGroup group) =>
        bridge is not null ? GroupTitles.Of(bridge, group.Index) : GroupTitles.LangTitle(group) ?? group.Id;

    // ---- (c) tinted borders: GuiDialog.OnRenderGUI postfix ------------------------------------------

    public static void RenderMarks(GuiDialogInventory dialog)
    {
        if (rowCount == 0 || groupedRows.Count == 0 || capi is null || viewDialog is null || viewGrid is null) return;
        if (!viewDialog.TryGetTarget(out var d) || !ReferenceEquals(d, dialog)) return;
        if (composerRef is null || dialog.Composers["maininventory"] is not { } main || !ReferenceEquals(main, composerRef(dialog))) return;
        if (!viewGrid.TryGetTarget(out var grid) || !IsViewCurrent(grid)) return;
        var bounds = grid.SlotBounds;
        var clip = grid.Bounds?.ParentBounds;
        if (bounds is null || clip is null) return;

        var render = capi.Render;
        float corner = (float)Math.Max(8.0, Math.Round(GuiElement.scaled(11.0)));
        float rim = Math.Max(2f, corner / 5f);
        render.PushScissor(clip, stacking: true);
        try
        {
            foreach (int i in groupedRows)
            {
                if (i >= bounds.Length) break;
                var b = bounds[i];
                if (b is null || !b.PartiallyInside(clip)) continue;
                if (grid.renderedSlots.GetKeyAtIndex(i) != rows[i].SlotId) return;   // someone changed the grid
                float x = (float)b.renderX, y = (float)b.renderY, w = (float)b.OuterWidth, h = (float)b.OuterHeight;
                var row = rows[i];
                if (row.IsExpandedMember)
                {
                    int c = row.IsAutoExpanded ? TintColorAuto : TintColor;
                    render.RenderRectangle(x, y, MarkZ, w, h, c);
                    render.RenderRectangle(x + 1, y + 1, MarkZ, w - 2, h - 2, c);
                }
                else if (row.IsTile)
                {
                    // A white square with a dark rim in the top-right corner marks a collapsed group
                    // (amber did not show against the slot background).
                    float cx = x + w - corner - 2, cy = y + 2;
                    for (float s = corner, o = 0; s > 0; s -= 2, o += 1)
                        render.RenderRectangle(cx + o, cy + o, MarkZ, s, s, o < rim ? BadgeRim : BadgeFill);
                }
            }
        }
        finally { render.PopScissor(); }
    }

    // ---- persistence --------------------------------------------------------------------------------

    static TidyCreativeConfig? LoadConfig()
    {
        if (capi is null) return null;
        try { return capi.LoadModConfig<TidyCreativeConfig>(ConfigFile); }
        catch (Exception ex)
        {
            capi.Logger.Warning("[seraphtweaks] Tidy Variants: could not read ModConfig/{0}; starting with every group collapsed: {1}", ConfigFile, ex.Message);
            return null;
        }
    }

    static void SaveConfig()
    {
        if (capi is null) return;
        try { capi.StoreModConfig(new TidyCreativeConfig { Expanded = [.. expand.Ids] }, ConfigFile); }
        catch (Exception ex) { Fail("saving ModConfig/" + ConfigFile, ex); }
    }

    // ---- (f) the dialog built before the rules were resolved ----------------------------------------

    /// <summary>
    /// The game builds the creative dialog when the own player data arrives, before level finalize,
    /// so before the client's resolution exists: that build's grid hides and groups nothing, and
    /// Dovidarium's compose reuse keeps it for the first open. Called once the resolution is there:
    /// redoes what the build did, DetermineAvailableSlots (our hide postfix) and update() (vanilla
    /// filter, then our regroup postfix). Nothing to do when the dialog is not built yet, or is in
    /// survival mode: its next compose takes the rules.
    /// </summary>
    public static void RefreshBuiltDialog()
    {
        if (Current() is null || view is null || composerRef is null || updateDialog is null) return;
        var dialog = capi!.Gui.LoadedGuis.OfType<GuiDialogInventory>().FirstOrDefault();
        if (dialog is null || composerRef(dialog)?.GetSlotGrid("slotgrid") is not { } grid || !IsCreativeGrid(grid)) return;
        grid.DetermineAvailableSlots();
        updateDialog(dialog);
    }
}
