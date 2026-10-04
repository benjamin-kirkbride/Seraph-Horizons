# Spike: creative and handbook hooks, Dovidarium and TooManyTabs overlap (#253)

Part of #252. Read against game 1.22.7 and the pack as locked. Names below come from
decompiling `VintagestoryLib.dll`, `VintagestoryAPI.dll`, `Mods/VSCreativeMod.dll`,
`Mods/VSEssentials.dll`, `Mods/VSSurvivalMod.dll`, `Dovidarium.dll` (0.9.5) and
`TooManyTabs.dll` (1.0.0) with ilspycmd. Everything is **verified** in decompiled code
unless marked *inferred*.

**The big correction:** vanilla 1.22.7 does not group the handbook list by `groupBy`,
and Dovidarium 0.9.5 has no "variant families" code. Writing `groupBy` alone will not
shorten the handbook list. See section 3.

## 1. Creative inventory: where the list is built and drawn

**Data.** `Vintagestory.Common.InventoryPlayerCreative` (VintagestoryLib, public) holds
`CreativeTabs tabs`. `UpdateFromWorld(IWorldAccessor)` runs once (it returns if tabs
already exist) on `Open` and on `CreativeNetworkUtil.UpdateFromPacket`:

- `GatherTabStacks(CollectibleObject[])`: blocks ordered by `BlockMaterial`, then items
  ordered by `Tool` (stable sorts, so ids order within a key). Per collectible, one
  `new ItemStack(coll)` per `CreativeInventoryTabs` entry, then every
  `CreativeInventoryStacks[k].Stacks[m].ResolvedItemstack.Clone()` for each tab in
  `CreativeInventoryStacks[k].Tabs`.
- `CreateTab(code, stacks)` makes `CreativeTab(code, new CreativeInventoryTab(n, ...))`
  (`CreativeInventoryTab : InventoryGeneric`, slots are `ItemSlotCreative`). Tab
  `Index` is the dictionary insertion order.
- `InventoryPlayerCreative.this[slotId]` and `Count` forward to `currentTab.Inventory`;
  `SetTab(int)` switches it.

**The server resolves clicks by slot id.** `SlotClick` sends `ActivateSlot` packets with
the slot id and `TabIndex`; the server's `InventoryNetworkUtil` calls
`SetTab(packet.ActivateInventorySlot.TabIndex)` and reads `inventory[targetSlot]` from
its own `InventoryPlayerCreative`, built the same way. So **never change the creative
inventory's contents or a collectible's `CreativeInventoryTabs`/`CreativeInventoryStacks`
on one side only**: slot ids and tab indices would desync. Do all hiding and grouping on
the client grid's view (`availableSlots`/`renderedSlots`), which keeps real slot ids.

**Dialog.** `Vintagestory.Client.NoObf.GuiDialogInventory` (VintagestoryLib):

- `ComposeGui(bool firstBuild)` → `ComposeCreativeInvDialog()` (private). Tabs are
  ordered by `config/creativetabs.json` `ListOrder` (`CreativeTabsConfig.TabConfigs`),
  first 16 in `"verticalTabs"`, the rest in `"verticalTabsR"`. The grid is
  `AddItemSlotGrid(creativeInv, SendInvPacket, cols=15, bounds, "slotgrid")` inside a clip
  bound, plus `"scrollbar"`, text input `"searchbox"`, dynamic text `"searchResults"`.
  Composer name `"inventory-creative"`, stored in `Composers["maininventory"]`.
- `OnTabClicked(int, GuiTab)`: `creativeInv.SetTab(tab.DataInt)`, then
  `slotGrid.DetermineAvailableSlots()`, then `update()`.
- `update()` (private) → `OnTextChanged(searchbox text)` (private, `void OnTextChanged(string)`):
  calls `slotGrid.FilterItemsBySearchText(text, CurrentTab.SearchCache, CurrentTab.SearchCacheNames)`,
  then sets the scrollbar height from `renderedSlots.Count`, scrolls to 0 and writes
  `Lang.Get("creative-searchresults", renderedSlots.Count)`. Every path that changes the
  shown list (compose, tab click, typing) ends here.
- Fields read by reflection elsewhere: `creativeInv`, `creativeInvDialog`, `currentTabIndex`, `cols`.

**Grid.** `GuiElementItemSlotGrid : GuiElementItemSlotGridBase` (VintagestoryAPI):

- `public OrderedDictionary<int, ItemSlot> availableSlots, renderedSlots`; keys are
  inventory slot ids, order is display order. `ElementBounds[] SlotBounds` is laid out
  by visual index (0..`availableSlots.Count`), and render, hover and click all use
  `renderedSlots.GetKeyAtIndex(i)` for `SlotBounds[i]`. Shrinking `renderedSlots`
  compacts the grid with no re-layout.
- `public void DetermineAvailableSlots(int[] visibleSlots = null)` (on `GuiElementItemSlotGrid`)
  fills both dictionaries from every inventory slot. Called from the constructor and `OnTabClicked`.
- `public override void RenderInteractiveElements(float)` draws `renderedSlots` only
  (item at z 90, slot textures at z 50).
- `OnMouseDownOnElement` / `OnMouseMove` call
  `public virtual void SlotClick(ICoreClientAPI api, int slotId, EnumMouseButton mouseButton, bool shiftPressed, bool ctrlPressed, bool altPressed)`
  with the real slot id. `OnMouseDownOnElement` calls it once per press with the pressed button;
  `OnMouseMove` calls it with `Right` only during a right-drag (`isRightMouseDownStartedInsideElem`,
  set at the press when the cursor holds an item) and with `Left` during a left-drag distribute.
  On a creative slot, right-click with an empty cursor moves nothing (`ItemSlotCreative.ActivateSlotRightClick`
  puts the empty cursor into the slot); with an item held it voids one. See creative.md, "Right-click".
  (Alt+click is unused by vanilla too, but holding Alt frees the cursor for mouse-look, so it is unusable.)
- `private bool ComposeSlotOverlays(ItemSlot slot, int slotId, int slotIndex)` builds the
  durability bar texture for visual index `slotIndex` in `slotQuantityTextures`.
- Identify the creative grid by `inventory.ClassName == "creative"` (protected field
  `inventory`), as Dovidarium does.

**Tooltip.** Hover → `TriggerOnMouseEnterSlot` → `HudMouseTools.OnRequireInfoText(ItemSlot)`
(private) → `ItemSlot.GetStackDescription(IClientWorldAccessor, bool)` (public virtual;
`ItemSlotCreative` doesn't override it) → `ItemStack.GetDescription` →
`CollectibleObject.GetHeldItemInfo`. Do not add lines in `GetHeldItemInfo` or
`ItemStack.GetDescription`: `CreativeTab.CreateSearchCache` builds the search text from
`itemstack.GetDescription(world, slot)`, so the hint would become searchable.
`GetStackDescription` is not on the search path.

## 2. Search

- `CreativeTabs.CreateSearchCache` runs once on the thread pool from
  `GuiDialogInventory.OnOwnPlayerDataReceived`. Per tab, `SearchCacheNames[slotId]` is the
  lowercased search-friendly name and `SearchCache[slotId]` is
  `name + " " + (ISearchTextProvider.GetSearchText ?? description)`.
- `GuiElementItemSlotGridBase.FilterItemsBySearchText(string text, Dictionary<int,string> searchCache, Dictionary<int,string> searchCacheNames)`
  iterates **`availableSlots`**, rebuilds `renderedSlots` (empty query: all non-empty
  slots in order; else weighted name/description matches, sorted by weight), then calls
  `ComposeInteractiveElements()`. Search covers only the current tab.
- With Dovidarium active, its prefix usually replaces this method (see 4) but still
  writes `renderedSlots` from `availableSlots`-derived results.

So: **hide** by removing slot ids from `availableSlots` (both vanilla and Dovidarium
search, and Dovidarium's index, read only `availableSlots`); **group** after
`FilterItemsBySearchText` returns, by rewriting `renderedSlots`.

## 3. Handbook: `groupBy`, `exclude`, `isDuplicate`, timing

All client-side, in VSSurvivalMod.

**Build order (client).** `ClientSystemStartup.HandleLevelFinalize`: every client system's
`OnLevelFinalize()` first, with `SystemModHandler.OnLevelFinalize` running
`ModRunPhase.AssetsFinalize` (so **client `ModSystem.AssetsFinalize` runs here**), then
`api.Event.TriggerLevelFinalize()`. `ModSystemSurvivalHandbook.Event_LevelFinalize`
(a `LevelFinalize` handler) builds `ObjectCacheUtil "handbookallstacks"` from
`collectible.GetHandBookStacks(capi)` for every collectible, then constructs
`GuiDialogSurvivalHandbook`, whose `loadEntries()` runs `initCustomPages()` (fires
`ModSystemSurvivalHandbook.OnInitCustomPages`) and queues `LoadPages_Async` (one
`GuiHandbookItemStackPage` per stack).

**Where each attribute is read:**

| Attribute | Read in | When |
|---|---|---|
| `handbook.exclude` | `CollectibleObject.GetHandBookStacks` (base only) | building `handbookallstacks`, at LevelFinalize |
| `handbook.include`, `ignoreCreativeInvStacks` | same | same |
| `handbook.isDuplicate` | `GuiHandbookItemStackPage` ctor → `IsDuplicate`; `SlideshowItemstackTextComponent` ctor | page creation (thread pool, right after LevelFinalize); detail page compose |
| `handbook.groupBy` | `SlideshowItemstackTextComponent` ctor; `CollectibleBehaviorHandbookTextAndExtraInfo` host-rock sections (`[0]` only) | when a detail page is composed (lazy) |

`GuiDialogHandbook.FilterItems()` (the list and its search) skips `page.IsDuplicate`.
Nothing reads `groupBy` for the list.

**Latest safe write point:** client `AssetsFinalize` (verified to run before the
`LevelFinalize` event). A `LevelFinalize` handler registered before
`ModSystemSurvivalHandbook`'s would also work but depends on mod-system order; don't.
Server `AssetsFinalize` also works, since attributes reach the client as JSON in the
block/item packet, which is built after it (see `docs/recipe-browser/spike-findings.md` §1).
`groupBy` is read lazily, but write it at the same time.

**What `groupBy` actually does.** `SlideshowItemstackTextComponent(capi, itemstackgroup, allstacks, size, float, "groupBy", onClick)`:
each pattern is passed through `IHandbookGrouping.GetWildcardForHandbookGrouping`
(clutter `BlockShapeMaterialFromAttributes`, shields `ItemShieldFromAttributes`), gets the
item's domain if it has no `:`, and is matched with
`WildcardUtil.Match(AssetLocation, AssetLocation)` (domain must be equal or `*`; `*` glob;
a leading `@` makes the path a regex) against each other stack's code (or
`IHandbookGrouping.GetCodeForHandbookGrouping`). Patterns are OR'd: a stack joins if
**any** pattern matches. That's VintageStory-Issues#7476 (closed "incomplete"): with
`["trapdoor-plate-{material}-*", "trapdoor-bars-{material}-*"]` both kinds join one
group. One pattern per item avoids it. Matching stacks are removed from the section's
`allstacks` and shown as one tile that cycles through them. Any stack with `isDuplicate`
is also removed and **not shown**. Effect: in detail-page sections ("Created by",
"Ingredient for", ...) variants collapse into one cycling icon. The handbook list
still shows every variant.

**The grouped page is dead code.**
`GuiHandbookGroupedItemstackPage(capi, stack) : GuiHandbookItemStackPage(capi, null)`
(`Stacks`, `Name`, cycling `RenderListEntryTo`, page text of `Stacks[0]`) is never
constructed in vanilla, and its constructor passes `null` to a base constructor that
calls `stack.GetName()`, so it would throw. Don't use it as-is.

**What it takes to shorten the list without Harmony** (Dovidarium gates the list
methods, see 4):

- Members: `handbook.isDuplicate = true` hides their pages from the list and search.
  Their pages still exist (`handbook://` links work), but slideshows drop isDuplicate
  stacks, so their only remaining route is creative.
- One list entry per group: add our own page (a subclass of `GuiHandbookItemStackPage`
  built with the representative stack, with its own `PageCode`, cycling
  `RenderListEntryTo`, and text that links each member) to the list passed to
  `ModSystemSurvivalHandbook.OnInitCustomPages` (`List<GuiHandbookPage>`, runs on the
  main thread before `LoadPages_Async`). Then mark every member, including the
  representative, `isDuplicate`. Dovidarium's category tabs find a page's categories
  through `PageCodeForStack(page.Stack)` (`is GuiHandbookItemStackPage`), so a subclass
  is categorised by its representative.
- `groupBy` (one pattern) keeps detail-page sections collapsed too.

*Inferred:* several pack mods override `GetHandBookStacks` (ACulinaryArtillery, Alchemy,
crucibulum, ElectricalProgressive, ImmersiveWoodworking, primitivesurvival, StoneQuarry,
Substrate, VintageEngineering, MPE gears). Overrides may ignore `exclude`, so for hiding
prefer `isDuplicate`, which the page reads regardless of where the stack came from.

## 4. Dovidarium 0.9.5 and TooManyTabs 1.0.0 patches

**Dovidarium's conflict guard matters most.** Dovidarium registers each feature as a
gate with a policy per target method (`PatchSafety.Register`). At apply time
(`StartPre`) and again in a late audit (client `LevelFinalize`), it looks at
`Harmony.GetPatchInfo` and **turns a feature off** if a foreign patch breaks the
policy: `Exclusive` (any foreign patch), `PostfixesOnly`, `VoidPrefixesOnly` (prefix
returning void, no `__runOriginal`), `AllowAny`. Its patches stay installed but do
nothing. So patching an `Exclusive` target, even with a postfix, silently costs a
Dovidarium speed-up. Harmony ids: `dovidarium.universal`, `dovidarium.client`.

Creative and inventory patches (client):

| Feature | Target | Patch | Policy |
|---|---|---|---|
| creative composer reuse | `GuiDialogInventory.ComposeGui(bool)` | prefix (skips re-compose when nothing changed) + postfix | Exclusive |
| creative right-tab auto width | `GuiDialogInventory.ComposeCreativeInvDialog`, `GuiElementVerticalTabs.ComposeTextElements`, `GuiComposerHelpers.AddVerticalTabs(5 args)`, `GuiComposer.OnMouseWheel`, `GuiDialog.OnMouseWheel` | prefixes (+postfix/finalizer on ComposeCreativeInvDialog); own `ScrollableVerticalTabs` | Exclusive; `GuiComposer.AddInteractiveElement` and `GuiManager.OnMouseWheel` VoidPrefixesOnly |
| creative search layout and result cache | `GuiElementItemSlotGridBase.FilterItemsBySearchText(string, Dict, Dict)` | prefix (cached results + token index over `availableSlots`, writes `renderedSlots`, returns false) + postfix | Exclusive |
| | `GuiElementItemSlotGrid.DetermineAvailableSlots(int[])` | postfix (invalidates its state) | AllowAny |
| creative visible-slot render window | `GuiElementItemSlotGridBase.RenderInteractiveElements(float)` | prefix (priority 0) temporarily swaps `renderedSlots`/`SlotBounds` to the visible window, finalizer restores | Exclusive |
| | `InventoryItemRenderer.RenderItemstackToGui(10 args)` | prefix (priority 200) + postfix: cold-item budget | AllowAny |
| creative slot-overlay fast path | `GuiElementItemSlotGridBase.ComposeSlotOverlays` | prefix | Exclusive |
| ItemCreature mesh pipeline / frame budget | `ItemCreature.CreateOverlaidMeshRef`, `OnBeforeRender`, `OnUnloaded`; `GuiComposer.Render(float)` | prefixes, finalizers | `OnBeforeRender` VoidPrefixesOnly; `GuiComposer.Render` AllowAny |
| global GUI work budget | `GuiComposer.Render(float)` | prefix + finalizer | AllowAny |
| craftable panel (off by default) | `GuiDialogInventory.ComposeSurvivalInvDialog`, `ComposeGui`, `OnGuiOpened`, `OnGuiClosed` | postfixes | AllowAny / PostfixesOnly |
| crafting drag coalescing | `GuiElementItemSlotGridBase.OnMouseUp`, `OnGuiClosed`, `InventoryNetworkUtil.UpdateFromPacket` ×2, `CraftingInventoryNetworkUtil.UpdateFromPacket`, ... | prefixes/postfixes | Exclusive |

Handbook patches (client): `GuiDialogHandbook.FilterItems` prefix (result/data cache;
policy accepts only prefixes named `Prefix` on a class containing
`GuiDialogHandbook_FilterItems_Patch`), `LoadPages_Async` postfix (Exclusive),
`OnNewScrollbarvalueOverviewPage` postfix (lazy append, Exclusive),
`initOverviewGui`/`OnGuiOpened` (overview reuse, Exclusive), `initDetailGui`/`Dispose`/`ReloadPage`
(detail composer cache, PostfixesOnly), `GuiDialogSurvivalHandbook.genTabs`,
`LoadPages_Async`, `initOverviewGui`, `initDetailGui`, `selectTab` (category tabs, AllowAny),
`CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` transpiler (PostfixesOnly),
`addIngredientForInfo`, `addCreatedByInfo`, `addStorableInfo`, `addStoredInInfo`
(prefix/transpiler, PostfixesOnly), `ModSystemSurvivalHandbook.CreateCachedMealRecipeStacks`
and `GuiHandbookMealRecipePage.getIngredientStacks` (off by default),
`BlockGenericTypedContainer.OnBeforeRender`, `GuiElementDialogTitleBar.OnMouseUp`.
Universal: `InventoryCraftingGrid.FindMatchingRecipe`/`ActivateSlot`/`DidModifyItemSlot`/`EndCraft`,
`GridRecipe.ConsumeInput`, `ServerCoreAPI.RegisterCraftingRecipe`.

**Variant families (question 5).** Dovidarium 0.9.5 has none. Its modinfo says it
"groups related variants", but the decompiled code never reads or writes `groupBy` or
`isDuplicate`. It only *honours* `IsDuplicate` in its filter cache (like vanilla), and its
"grouping" is category tabs (flora, terrain, tools, ...). So it neither picks up nor
fights `groupBy` we write, and an `isDuplicate`-based list collapse works with its
filter cache. A page whose `GetType() != typeof(GuiHandbookItemStackPage)` just skips
its page-text cache.

**TooManyTabs 1.0.0** (client, Harmony id `toomanytabs`, patched in `StartClientSide`):
`GuiComposer.Compose` prefix (only when a composer has exactly two `GuiElementVerticalTabs`
with tabs and one is 500–600 high, i.e. the creative dialog: widens to fit names, max
220, and keeps the original Y for scrolling); `GuiElementVerticalTabs.RenderInteractiveElements`
prefix+postfix (scissor and scroll offset, draws a scrollbar); `OnMouseDownOnElement`
prefix; `SetValue(int)`, `SetValue(int,bool)` postfixes (only one active tab across both
columns, scroll to selected); `Dispose` prefix; `GuiDialog.OnMouseWheel`, `OnMouseDown`,
`OnMouseMove`, `OnMouseUp` prefixes, which return false only when the mouse is over its tab
column or scrollbar.

**They conflict, and Dovidarium backs off.** TooManyTabs' prefix on
`GuiDialog.OnMouseWheel` is a foreign patch on an `Exclusive` target of Dovidarium's
"creative right-tab auto width". Evidence from the pack's real client log
(`~/.cairn/packs/seraphhorizons-fallenstar/data/Logs/Archive/2026-10-01_18_35_00/client-main.log`;
the `~/.config/VintagestoryData` logs are from sessions without the pack):

```
Eternal Seraph: Dovidarium 0.9.5: patch-status side=client phase=startup active=20/25; ... disabled-by-conflict=1; ... disabled-by-config=2
Eternal Seraph: Dovidarium: handbook CreatedBy has a non-additive Harmony patch; created-by recipe index is inactive for compatibility.
Eternal Seraph: Dovidarium 0.9.5: patch-status side=client phase=late-audit active=19/25; ... disabled-by-late-conflict=1
Eternal Seraph: Dovidarium: disabled creative right-tab auto width; incompatible Harmony prefix toomanytabs:TooManyTabs.CreativeInventoryTabFixPatches.GuiDialog_OnMouseWheel_Prefix on Vintagestory.API.Client.GuiDialog.OnMouseWheel. Vanilla behavior remains active.
```

So TooManyTabs' tab layout is what players get, and nothing is broken. The CreatedBy
conflict is a third mod (*inferred* ACulinaryArtillery or VintageEngineering; both
reference `addCreatedByInfo`). The two disabled-by-config gates are the meal index and
craftable panel defaults.

## 5. Data available to the rule engine at runtime

- On both sides every `CollectibleObject` (`RegistryObject`) keeps `Code`,
  `VariantStrict` (`OrderedDictionary<string,string>`, group code → value, in
  `variantgroups` order) and `Variant` (relaxed read-only view), `Class`, `Attributes`,
  `CreativeInventoryTabs`, `CreativeInventoryStacks`. The client gets them from
  `BlockTypeNet`/`ItemTypeNet` packets (`packet.Variant`, `Attributes` as JSON,
  creative tabs and stacks).
- The `variantgroups` **definitions are gone.** `ModRegistryObjectTypeLoader` (VSEssentials,
  server only, ExecuteOrder 0.2) holds `RegistryObjectType.VariantGroups`
  (`RegistryObjectVariantGroup { Code, States, LoadFromProperties, LoadFromPropertiesCombine, Combine, OnVariant, IsValue }`)
  and `worldProperties` only during `AssetsLoaded`, and `FreeRam()` nulls them at its end.
  `blocktypes`/`itemtypes` assets are `EnumAppSide.Server` categories, so the client never
  has them. To know a dimension came from `loadFromProperties`, re-read the type assets on
  the server (`api.Assets.GetMany<JObject>(logger, "blocktypes/")`, patched), or classify
  by value overlap, which works on both sides.
- `worldproperties` is a `Universal` asset category, and `ModJsonPatchLoader` runs on
  both sides. Read lists on client or server with
  `api.Assets.GetMany<StandardWorldProperty>(logger, "worldproperties/")`
  (`StandardWorldProperty.Code`, `Variants[].Code`). Vanilla files: `worldproperties/block/`
  `rock`, `rockwithdeposit`, `wood`, `metal`, `toolmetal`, `ore-graded`, `ore-ungraded`,
  `ore-nugget`, `ore-gem-*`, `flower`, `herb`, `mushroom`, `fruit`, ...; `abstract/`
  `horizontalorientation`, `verticalorientation`, `alloy`, `coating`, `rockgroup`, ...
  Asset domain `game` (the folder is `survival/`). There is no `color` world property;
  colors are inline `states`.

## 6. Recommended hook plan

Client-side, Harmony id `seraphhorizons.creative`, patched in `StartClientSide`. None
of these targets is gated `Exclusive` by Dovidarium or touched by TooManyTabs, and no
other pack mod references them by name (string scan of every DLL in `build/mods`).

| Need | Hook | Notes |
|---|---|---|
| (a) hide | postfix `GuiElementItemSlotGrid.DetermineAvailableSlots(int[])` | creative grid only (`inventory.ClassName == "creative"`): remove hidden slot ids from `availableSlots` and `renderedSlots`. Dovidarium policy AllowAny; its own postfix only invalidates. Hidden items drop out of vanilla and Dovidarium search |
| (b) group after search, auto-expand single group | postfix `GuiDialogInventory.OnTextChanged(string)` (private) | rewrite `slotGrid.renderedSlots`: first matching member's position gets the representative (or the members, if expanded), other members removed; then redo what `OnTextChanged` did with the new count (`GetScrollbar("scrollbar").SetNewTotalHeight`, `SetScrollbarPosition(0)`, `"searchResults"` text). Refresh overlays for moved visual indices (reflect `ComposeSlotOverlays(slot, slotId, visualIndex)`), since Dovidarium's overlay bookkeeping is per visual index |
| (d) right-click | prefix `GuiElementItemSlotGridBase.SlotClick(...)` | creative grid, right button, no shift, empty mouse slot, not in a right-drag, tile or player-expanded member: toggle the group, call the dialog's private `update()` (cheap with Dovidarium's cache), return false so no packet is sent. Everything else falls through on the representative's real slot id |
| (c) tinted border | postfix `GuiDialog.OnRenderGUI(float)` filtered to `GuiDialogInventory` in creative | iterate `renderedSlots` and draw a border texture or `Render.RenderRectangle` on `SlotBounds[i]` for expanded members, inside `PushScissor(grid.Bounds.ParentBounds)`, z above 90. Not `RenderInteractiveElements` (Exclusive, and its postfix would see Dovidarium's swapped window). `GuiComposer.Render(float)` (AllowAny) also works but runs for every composer |
| (e) tooltip | postfix `ItemSlot.GetStackDescription(IClientWorldAccessor, bool)` | only when `__instance is ItemSlotCreative` and the slot is a collapsed representative (map by slot reference). Off the search path; handbook `DummySlot`s are excluded by type |
| handbook | client `AssetsFinalize`: write `attributes.handbook.groupBy` (one pattern), `isDuplicate` for members; subscribe `ModSystemSurvivalHandbook.OnInitCustomPages` to add group pages | no Harmony on the handbook. `Attributes` may be null; create the `handbook` object as `CollectibleBehaviorSqueezable` does via `Attributes.Token` |

**Do not patch** (each would turn off a Dovidarium feature or fight it):
`GuiElementItemSlotGridBase.FilterItemsBySearchText`, `RenderInteractiveElements`,
`ComposeSlotOverlays`, `OnMouseUp`, `OnGuiClosed`; `GuiDialogInventory.ComposeGui`,
`ComposeCreativeInvDialog`; `GuiDialog.OnMouseWheel`; `GuiDialogHandbook.FilterItems`,
`LoadPages_Async`, `OnNewScrollbarvalueOverviewPage`, `initOverviewGui`, `OnGuiOpened`.
Calling them (reflection, `update()`) is fine.

**Priority.** No shared targets except `DetermineAvailableSlots`, where order doesn't
matter. Leave default priority, and add a test that asserts the late audit stays at
`disabled-by-late-conflict=1` (the TooManyTabs one).

**Risks and open points.**

- Dovidarium's compose reuse skips `ComposeCreativeInvDialog` on reopen, so `OnTextChanged`
  isn't re-run and our `renderedSlots` must stay valid between opens. It does, because
  we mutate in place. The same reuse keeps the first build, which `OnOwnPlayerDataReceived`
  runs before our rules are resolved: `CreativeUi.RefreshBuiltDialog` redoes it on `Resolved`
  (creative.md, **The first build**).
- Members aren't always contiguous: blocks are sorted by `BlockMaterial`, so variants of
  one type with different materials can split, and search reorders by weight. Collapse to
  the first member's position.
- Group lookup per slot: build `slot id → group` per `CreativeTab` lazily (tab
  inventories and slots are stable after `UpdateFromWorld`). Key groups by collectible
  id plus stack attributes for `CreativeInventoryStacks` families.
- Alt+click, the first choice for (d), does not work: holding Alt in Vintage Story frees
  the cursor for mouse-look (found in game). Right-click replaced it, with a hotkey over
  the hovered slot (Ctrl+G) as a second way.
- `"creative-searchresults"` will count tiles, not items, unless we override the text.
- Hiding through `isDuplicate` also hides members from handbook slideshows. Creative is
  then their only route, which the epic's "reachable from the UI" rule accepts only
  because creative counts.
- The epic's assumption that we "reuse vanilla's grouped handbook page and Dovidarium's
  handbook features" doesn't hold (section 3). The handbook side needs our own
  group-page class (no Harmony) or accepts `isDuplicate`-only hiding.

## 7. Search box: keeping the place, right-click to clear

For `CreativeKeepsPlace` and `SearchRightClickClears` (`mods-src/seraphhorizons/CreativeSearch/`,
Harmony id `seraphhorizons.creativesearch`, client only). Verified in the decompiled 1.22.7 game
(`VintagestoryAPI.dll`, `VintagestoryLib.dll`, `VSSurvivalMod.dll`), Dovidarium 0.9.5 and TooManyTabs 1.0.0.

**Close and open.** `GuiDialog.TryClose()` (virtual; `GuiDialogInventory` doesn't override it) sets
`opened = false`, calls `UnFocus()`, then, if it was open, `OnGuiClosed()` and the `OnClosed` event.
Nothing in the game library calls `OnGuiClosed` any other way. `GuiDialogInventory.OnGuiClosed` in
creative does `searchbox.SetValue("")` (→ `OnTextChanged("")`: search, `SetScrollbarPosition(0)`),
`slotgrid.OnGuiClosed` and the close packet. `TryOpen(bool)` sets `opened = true`, then
`OnGuiOpened()` and the `OnOpened` event; `GuiDialogInventory.OnGuiOpened` runs `ComposeGui(false)`.
Without Dovidarium that builds a new composer (empty box, `update()`, scroll 0); with its composer
reuse it returns early with the last composer, whose box the close emptied. `currentTabIndex` is
never reset, so the tab survives either way. The Ctrl+F hotkey (`onSearchCreative`) calls `TryOpen()`
and then focuses the box, so it runs after `OnGuiOpened` and its postfixes.

| Hook | Why |
|---|---|
| prefix `GuiDialog.TryClose()`, for `GuiDialogInventory` while `IsOpened()` | the last point where the box still holds the text and the scrollbar its position. Dovidarium registers `GuiDialogInventory.OnGuiClosed` (and `OnGuiOpened`, `ComposeGui`) for its craftable panel as **PostfixesOnly**, so a prefix there would turn that feature off at the late audit; `GuiDialog.TryClose` is registered by neither mod, and no pack mod names it |
| postfix `GuiDialogInventory.OnGuiOpened()` | runs after the compose or the reuse; a postfix is what PostfixesOnly allows |
| postfix `GuiElementEditableTextBase.OnMouseDownOnElement(ICoreClientAPI, MouseEvent)` | see below; patched by no pack mod, gated by no Dovidarium feature (TooManyTabs' `OnMouseDownOnElement` prefix is on `GuiElementVerticalTabs`) |

The scroll goes back as Tidy Variants' toggle keeps it: `scrollbar.CurrentYPosition = y`, then
`SetNewTotalHeight(total)` with the total `OnTextChanged` just set (vanilla's
`ElementStdBounds.SlotGrid(cols, ceil(renderedSlots / cols)).fixedHeight + 3`, over the grid Tidy
Variants has already regrouped), which clamps the handle and calls the dialog's
`OnNewScrollbarvalue` (it moves the grid only while `IsOpened()`, which is true in the postfix).

**Mouse-down routing.** `ClientMain.UpdateMouseButtonState` fires `capi.Event.MouseDown` first (a
handler may set `Handled` and stop everything), then the client systems; `GuiManager.OnMouseDown`
offers the press to each loaded dialog in turn until one handles it. `GuiDialogInventory.OnMouseDown`
overrides `GuiDialog.OnMouseDown` without calling it (so TooManyTabs' prefix on the base doesn't run
for it): it returns as soon as a composer handled the press, and only then, inside the grid's clip
bounds, drops a held item into the creative inventory. `GuiComposer.OnMouseDown` gives the press to
its interactive elements (`OnMouseDown` → `OnMouseDownOnElement` when inside), and after the first
one handles it, focuses that element if focusable and unfocuses the others.
`GuiElementEditableTextBase.OnMouseDownOnElement` marks the press handled (base) and returns for any
button but the left one; `GuiElementTextInput` doesn't override it. So a right-click on a search box
already focuses it and does nothing else, and a postfix there only runs for presses the GUI gave to
that box (not `capi.Event.MouseDown`, which runs before the GUI and knows nothing of dialogs on top).

**Handbook.** `GuiDialogHandbook` (VSSurvivalMod) builds its list page as composer
`"handbook-overview"` with the text input `"searchField"` (handler `FilterItemsBySearchText`) and
shows it as `SingleComposer`; Dovidarium's overview reuse builds the same name and key itself and
restores the search text on reopen. `SetValue("")` on that box runs the same filter as typing.
