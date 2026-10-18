using System.Runtime.CompilerServices;
using HarmonyLib;
using SeraphHorizons.Mod.RecipeSelector.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.RecipeSelector.Game;

/// <summary>
/// The recipe selector with a search box and a scrollbar (README, "Recipe selection has a search and a
/// scrollbar"; client main thread only). Knapping, clay forming and smithing all open the game's
/// <c>GuiDialogBlockEntityRecipeSelector</c> (<c>BlockEntityKnappingSurface</c>, <c>BlockEntityClayForm</c>
/// and <c>BlockEntityAnvil.OpenDialog</c>, 1.22.7), whose constructor composes a grid of every recipe
/// output with no scrollbar and no search. Once it has (<see cref="Rebuild"/>, from a constructor
/// postfix), its composer is replaced by one laid out the same way with, above the grid, a search box and
/// the creative inventory's result count, and the grid in a clip with a scrollbar beside it when there are
/// more than <see cref="SelectorLayout.MaxVisibleRows"/> rows. The dialog's own fields and handlers stay:
/// the skill items it built (whose ingredient counts the anvil and clay forming set after the constructor),
/// its hover text and its selection, which is given the recipe's own index whatever the search shows
/// (<see cref="RecipeSlotGrid"/>), so the game's callback picks the recipe that was clicked.
///
/// The search is the creative inventory's (<see cref="RecipeSearch"/>) over each output's name and its
/// search text, read as the creative search cache reads it, once, at the first search in that dialog.
/// </summary>
internal static class SearchableSelector
{
    /// <summary>The search box's key, as the creative inventory's (and so <c>SearchClear</c>'s).</summary>
    public const string SearchBoxKey = "searchbox";

    static ICoreClientAPI? capi;
    static bool failed;
    static AccessTools.FieldRef<GuiDialogBlockEntityRecipeSelector, List<SkillItem>>? itemsRef;
    static AccessTools.FieldRef<GuiDialogBlockEntityRecipeSelector, BlockPos>? posRef;
    static Action<GuiDialogBlockEntityRecipeSelector, int>? slotClick, slotOver;
    static Action<GuiDialogBlockEntityRecipeSelector>? setupDialog;
    static AccessTools.FieldRef<GuiElementTextInput, bool>? deleteOnRefocusRef;
    static System.Func<GuiDialogBlockEntityRecipeSelector, bool>? foreignManaged;

    /// <summary>Dovidarium's mod id: from 1.0.5 it has its own anvil recipe browser on this dialog.</summary>
    public const string DovidariumId = "dovidarium";

    /// <summary>The dialogs this tweak composed, so the right-click clear knows their box.</summary>
    static readonly ConditionalWeakTable<GuiDialogBlockEntityRecipeSelector, object> ours = new();

    /// <summary>Binds the dialog's fields and handlers; false (and a warning) if it has changed shape.</summary>
    public static bool Init(ICoreClientAPI api)
    {
        Reset();
        try
        {
            var type = typeof(GuiDialogBlockEntityRecipeSelector);
            itemsRef = AccessTools.FieldRefAccess<GuiDialogBlockEntityRecipeSelector, List<SkillItem>>("skillItems");
            posRef = AccessTools.FieldRefAccess<GuiDialogBlockEntityRecipeSelector, BlockPos>("blockEntityPos");
            slotClick = Bind<Action<GuiDialogBlockEntityRecipeSelector, int>>(type, "OnSlotClick", typeof(int));
            slotOver = Bind<Action<GuiDialogBlockEntityRecipeSelector, int>>(type, "OnSlotOver", typeof(int));
            setupDialog = Bind<Action<GuiDialogBlockEntityRecipeSelector>>(type, "SetupDialog");
            // Optional: without it the box only lacks creative's clear-on-refocus Backspace.
            try { deleteOnRefocusRef = AccessTools.FieldRefAccess<GuiElementTextInput, bool>("DeleteOnRefocusBackSpace"); }
            catch { deleteOnRefocusRef = null; }
            foreignManaged = BindDovidarium(api);
            capi = api;
            return true;
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[seraphhorizons] Recipe selector search: the game's recipe selector has changed shape ({0}); off", ex.Message);
            Reset();
            return false;
        }
    }

    /// <summary>
    /// Dovidarium 1.0.5's anvil recipe browser (<c>Dovidarium.AnvilRecipeSelectorPatch</c>) composes this
    /// dialog itself when the anvil opens it and keeps it in a table, read by its
    /// <c>internal static bool IsManaged(GuiDialogBlockEntityRecipeSelector)</c>; a dialog it manages is left
    /// to it (its render prefix needs its own composer). Found by name; null without the mod, and a warning
    /// (the search then composes over every selector, as before) if the mod is there but the method is not.
    /// </summary>
    static System.Func<GuiDialogBlockEntityRecipeSelector, bool>? BindDovidarium(ICoreClientAPI api)
    {
        if (!api.ModLoader.IsModEnabled(DovidariumId)) return null;
        var method = AccessTools.TypeByName("Dovidarium.AnvilRecipeSelectorPatch") is { } type
            ? AccessTools.DeclaredMethod(type, "IsManaged", [typeof(GuiDialogBlockEntityRecipeSelector)])
            : null;
        if (method is null || method.ReturnType != typeof(bool))
        {
            api.Logger.Warning("[seraphhorizons] Recipe selector search: Dovidarium's AnvilRecipeSelectorPatch.IsManaged was not found; the search also composes over its anvil recipe browser");
            return null;
        }
        return AccessTools.MethodDelegate<System.Func<GuiDialogBlockEntityRecipeSelector, bool>>(method);
    }

    static T Bind<T>(Type type, string name, params Type[] args) where T : Delegate =>
        AccessTools.MethodDelegate<T>(AccessTools.DeclaredMethod(type, name, args)
                                      ?? throw new MissingMethodException(type.Name, name));

    public static void Reset()
    {
        capi = null;
        failed = false;
        itemsRef = null;
        posRef = null;
        slotClick = slotOver = null;
        setupDialog = null;
        deleteOnRefocusRef = null;
        foreignManaged = null;
        ours.Clear();
    }

    /// <summary>Logs the first failure; that dialog then stays (or goes back to) the game's.</summary>
    public static void Fail(string where, Exception ex)
    {
        if (failed) return;
        failed = true;
        capi?.Logger.Error("[seraphhorizons] Recipe selector search: {0} failed (logged once); that selector stays vanilla: {1}", where, ex);
    }

    /// <summary>Whether <paramref name="box"/> is the search box of a selector this tweak composed.</summary>
    public static bool IsSearchBox(GuiDialog dialog, GuiElementTextInput box) =>
        dialog is GuiDialogBlockEntityRecipeSelector selector && ours.TryGetValue(selector, out _)
        && ReferenceEquals(selector.SingleComposer?.GetTextInput(SearchBoxKey), box);

    /// <summary>
    /// The constructor's postfix: composes the dialog again with the search and the scrollbar, under the
    /// game's composer name (the game's composer manager disposes the one it replaces). Only the game's own
    /// class: a subclass may build on the game's layout. Not a dialog Dovidarium's anvil browser manages
    /// (<see cref="BindDovidarium"/>).
    /// </summary>
    public static void Rebuild(GuiDialogBlockEntityRecipeSelector dialog, ItemStack[]? outputs)
    {
        if (capi is null || itemsRef is null || posRef is null || dialog.GetType() != typeof(GuiDialogBlockEntityRecipeSelector)) return;
        if (foreignManaged?.Invoke(dialog) == true) return;
        try
        {
            Compose(dialog, outputs ?? []);
            ours.AddOrUpdate(dialog, true);
        }
        catch (Exception ex)
        {
            Fail("composing the selector", ex);
            ours.Remove(dialog);
            // Put the game's own composer back: ours replaced it under the same name, or failed half way.
            try { setupDialog?.Invoke(dialog); }
            catch (Exception again) { capi.Logger.Error("[seraphhorizons] Recipe selector search: could not compose the game's selector again: {0}", again); }
        }
    }

    static void Compose(GuiDialogBlockEntityRecipeSelector dialog, ItemStack[] outputs)
    {
        var api = capi!;
        List<SkillItem> items = itemsRef!(dialog);
        BlockPos pos = posRef!(dialog);
        int count = items.Count;

        // The game's layout (SetupDialog), the grid moved down under the search row and cut at
        // MaxVisibleRows rows, with the scrollbar to its right when the whole list is taller.
        int cols = SelectorLayout.Columns(count);
        bool scrolls = SelectorLayout.Scrolls(count, cols);
        double cell = RecipeSlotGrid.Cell;
        double gridWidth = cols * cell;
        double viewHeight = SelectorLayout.VisibleRows(count, cols) * cell;
        double scrollbarWidth = GuiElementScrollbar.DefaultScrollbarWidth;
        double width = Math.Max(300, gridWidth + (scrolls ? 5 + scrollbarWidth : 0));
        const double searchY = 30, searchHeight = 30, resultsWidth = 100;
        double gridY = searchY + searchHeight + 10;

        var searchBounds = ElementBounds.Fixed(0, searchY, width - resultsWidth - 10, searchHeight);
        var resultsBounds = ElementBounds.Fixed(width - resultsWidth, searchY + 7, resultsWidth, searchHeight);
        var clipBounds = ElementBounds.Fixed(0, gridY, gridWidth, viewHeight);
        var gridBounds = ElementBounds.Fixed(0, 0, gridWidth, SelectorLayout.Rows(count, cols) * cell);
        var scrollbarBounds = ElementBounds.Fixed(gridWidth + 5, gridY, scrollbarWidth, viewHeight);
        var nameBounds = ElementBounds.Fixed(0, gridY + viewHeight + 20, width, 33);
        var descBounds = nameBounds.BelowCopy(0, 10);
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var grid = new RecipeSlotGrid(api, items, cols, gridBounds)
        {
            OnRecipeClick = recipe => slotClick!(dialog, recipe),
            OnRecipeOver = recipe => slotOver!(dialog, recipe),
        };
        List<SearchEntry>? entries = null;
        GuiComposer? composer = null;

        void OnScroll(float value)
        {
            gridBounds.fixedY = -value;
            gridBounds.CalcWorldBounds();
        }

        void OnTextChanged(string text)
        {
            if (composer is null) return;
            try
            {
                List<int> shown;
                if (RecipeSearch.Query(text).Length == 0)
                    shown = Enumerable.Range(0, count).ToList();
                else
                    shown = RecipeSearch.Filter(entries ??= Entries(api.World, items, outputs), text);
                grid.SetShown(shown);
                if (composer.GetScrollbar("scrollbar") is { } bar)
                {
                    bar.SetNewTotalHeight((float)Math.Max(viewHeight, SelectorLayout.Rows(shown.Count, cols) * cell));
                    bar.SetScrollbarPosition(0);
                }
                else OnScroll(0);
                composer.GetDynamicText("results")?.SetNewText(Lang.Get("creative-searchresults", shown.Count));
            }
            catch (Exception ex) { Fail("searching", ex); }
        }

        composer = api.Gui.CreateCompo("toolmodeselect" + pos, ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bgBounds, true, 5.0, 0.75f)
            .AddDialogTitleBar(Lang.Get("Select Recipe"), () => dialog.TryClose())
            .BeginChildElements(bgBounds)
                // First, so it is the composer's first focusable element (tab index 0).
                .AddTextInput(searchBounds, OnTextChanged, null, SearchBoxKey)
                .AddDynamicText(Lang.Get("creative-searchresults", count),
                    CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right), resultsBounds, "results")
                .BeginClip(clipBounds)
                    .AddInteractiveElement(grid, "recipegrid")
                .EndClip()
                .AddIf(scrolls)
                    .AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar")
                .EndIf()
                // The game's hover text, under the keys its OnSlotOver writes to.
                .AddDynamicText("", CairoFont.WhiteSmallishText(), nameBounds, "name")
                .AddDynamicText("", CairoFont.WhiteDetailText(), descBounds, "desc")
                .AddDynamicText("", CairoFont.WhiteDetailText(), descBounds.BelowCopy(0, 20), "ingredient")
            .EndChildElements()
            // A list long enough to scroll opens with the box focused, ready for typing; a short one
            // leaves the keys to the game until the box is clicked.
            .Compose(focusFirstElement: scrolls);

        var box = composer.GetTextInput(SearchBoxKey);
        box.SetPlaceHolderText(Lang.Get("Search..."));
        // As creative's box (internal in the API): the first Backspace after coming back to the box clears it.
        if (deleteOnRefocusRef is not null) deleteOnRefocusRef(box) = true;
        composer.GetScrollbar("scrollbar")?.SetHeights((float)viewHeight, (float)(SelectorLayout.Rows(count, cols) * cell));
        dialog.SingleComposer = composer;
    }

    /// <summary>
    /// Each output's search entry, as the creative search cache builds it (<c>CreativeTab.CreateSearchCache</c>):
    /// the name, and the collectible's own search text or else its description. The game's skill items
    /// were built from the same outputs in the same order; without an output, an entry is the item's name.
    /// </summary>
    static List<SearchEntry> Entries(IWorldAccessor world, List<SkillItem> items, ItemStack[] outputs)
    {
        var entries = new List<SearchEntry>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var stack = i < outputs.Length ? outputs[i] : null;
            if (stack?.Collectible is null)
            {
                entries.Add(SearchEntry.Of(items[i]?.Name, ""));
                continue;
            }
            string name;
            try { name = stack.GetName(); }
            catch { name = items[i]?.Name ?? ""; }
            string text;
            try
            {
                var slot = new DummySlot(stack);
                text = (stack.Collectible as ISearchTextProvider)?.GetSearchText(world, slot)
                       ?? RecipeSearch.DescriptionText(stack.GetDescription(world, slot));
            }
            catch
            {
                // One output's description failing leaves it searchable by name.
                text = "";
            }
            entries.Add(SearchEntry.Of(name, text));
        }
        return entries;
    }
}
