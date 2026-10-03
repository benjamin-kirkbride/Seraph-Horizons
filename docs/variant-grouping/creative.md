# Creative inventory: hidden variants and collapsible groups (#256)

Part of #252. Client only. Code: `mods-src/seraphhorizons/TidyVariants/Game/Creative/` (game layer) and
`mods-src/seraphhorizons/TidyVariants/Core/CreativeView.cs` (grouping logic, unit-tested in `tests/CreativeViewTests.cs`).
Hooks and the reasons for them are in [hooks.md](hooks.md) §1, §4 and §6. The mod tabs button on the same dialog
(a separate tweak) is in [creative-mod-tabs.md](creative-mod-tabs.md); grouping applies in mod tabs too.

## Behaviour

- **Hidden variants** (orientation and open/closed duplicates, override `hide` rules) never appear in a
  creative tab, in search either: they are removed from the grid's `availableSlots`, which vanilla and
  Dovidarium search both read.
- **Groups collapse after search.** Search runs over the flat list as before (vanilla or Dovidarium's
  cache). What is left is then collapsed: each group with two or more matching members becomes one tile at
  the position of its first match, showing the best-ranked matching member (the representative). A tile
  contains only matching members, so search never loses an item. A group with one match is a plain slot.
  If only one group is left, it is shown expanded.
- **A tile is the representative's real slot.** Left-click, shift-click, middle-click and the mouse wheel
  are vanilla and take the representative. Slot ids never change and the creative inventory itself is
  never touched (the server resolves clicks by slot id and tab index against its own copy).
- **Expand / collapse: right-click** (see [Right-click](#right-click) for the exact rule). Right-click on a
  tile expands its group inline; right-click on any member of an expanded group collapses it again. The
  hotkey **Ctrl+G** (Controls settings, "Creative inventory: expand or collapse the hovered variant
  group", rebindable) does the same for the slot under the mouse. The scroll position is kept, and after
  an expand the slot under the mouse is the group's first member, at the tile's place. (Alt+click was the
  first design; it was dropped because holding Alt in Vintage Story frees the cursor for mouse-look.)
- **Marks:** expanded members get an amber border; a collapsed tile gets a small amber square in its top
  right corner. Members of an auto-expanded group get a fainter border.
- **Tooltip** of a tile or member adds the group title (the group's lang key, else a title derived from the members'
  names, else the representative's name; see the handbook doc's **Title**) with the number of matching members, and the expand/collapse hint naming the current hotkey. It is
  added in `ItemSlot.GetStackDescription`, which is not on the search path, so the hint is not searchable.
- **Result count:** "N results" as before, or "N results in M tiles" when grouping shortened the list.
- **Expand state persists per client** in `ModConfig/seraphhorizons-tidyvariants-creative.json`
  (`{"Version": 1, "Expanded": ["<group id>", ...]}`), by group id. A missing or unreadable file means
  everything collapsed (a warning is logged, the file is rewritten on the next toggle). Ids of groups that
  don't exist in the current pack are kept.

## Right-click

**What vanilla does** (decompiled 1.22.7). `GuiElementItemSlotGridBase.OnMouseDownOnElement` calls
`SlotClick(api, slotId, args.Button, ...)` once per press for whichever button (`EnumMouseButton`: Left 0,
Middle 1, Right 2), after setting `isRightMouseDownStartedInsideElem = button == Right && the cursor holds
an item`. `SlotClick` (not shift) runs `InventoryPlayerCreative.ActivateSlot` → `ItemSlotCreative.ActivateSlotRightClick`:

- **empty cursor:** `cursor.TryPutInto(creativeSlot)` with nothing in the cursor moves nothing. A plain
  right-click on a creative slot is a no-op (it still sends an ActivateInventorySlot packet, a no-op on the
  server too). So the gesture is free for us.
- **item on the cursor:** `cursor.TakeOut(1)`: one item of the held stack is voided, whatever the slot
  shows. Holding the button and moving (`OnMouseMove`, while `isRightMouseDownStartedInsideElem`) repeats
  that once per newly entered slot whose stack equals the held one. `OnMouseUp` clears the drag state.

**The rule** (`CreativeView.RightClickTarget`, unit-tested; applied in the `SlotClick` prefix):

| Slot | Cursor empty, not in a right-drag | Item on the cursor, or a right-drag in progress |
|---|---|---|
| tile (collapsed group, 2+ matching members) | expand the group, no packet | vanilla |
| member of a group the player expanded | collapse the group, no packet | vanilla |
| member of an auto-expanded group (the only group left) | vanilla (a no-op) | vanilla |
| plain slot | vanilla (a no-op) | vanilla |

Left-click, shift-click (either button) and middle-click are never touched.

**One toggle per press, no drag state.** The toggle only happens in the mouse-down call: vanilla's
only other caller with `Right` is `OnMouseMove` during a right-drag, which needs an item on the cursor at
the press and is also declined explicitly by reading `isRightMouseDownStartedInsideElem` (so a drag whose
held stack runs out mid-drag still cannot toggle). A toggle press leaves that flag false and puts nothing
in `distributeStacks*`, so holding the button and moving over the reshuffled grid does nothing, and the
next press starts fresh (vanilla resets the flags on every press). `OnMouseDownOnElement` breaks out of
its slot loop right after `SlotClick` and only touches the clicked slot id afterwards, so regrouping inside
the click is safe; `OnMouseUp` is not patched (Dovidarium guards it).

## Patches

`TidyCreativeModSystem` (`ShouldLoad` client only) applies them by hand in `StartClientSide`, when the
`TidyVariants` switch in `ModConfig/seraphhorizons.json` is on, with the Harmony id **`seraphhorizons.creative`**
and removes them on `Dispose`. The mod has no `[HarmonyPatch]` attributes and never calls `PatchAll`;
`tests/PackTests/TidyVariantsCreativeScenarios.cs` checks the system is disabled and the id patched nothing
on the server. Each patch is applied on its own
(a missing target logs a warning and only that part stays vanilla), and every patch body catches its
exceptions: the first failure per place is logged, that call stays vanilla. With no client resolution
(`TidyVariantsModSystem.ForSide(Client)` null) every patch is a no-op. Only grids whose inventory has
`ClassName == "creative"` are touched.

| Target (verified in decompiled 1.22.7) | Patch | Does |
|---|---|---|
| `GuiElementItemSlotGrid.DetermineAvailableSlots(int[] visibleSlots)` | postfix | drops hidden slots from `availableSlots` and `renderedSlots` (Dovidarium policy AllowAny) |
| `GuiDialogInventory.OnTextChanged(string text)` (private) | postfix | regroups `renderedSlots` in place, fixes `rows`, recomposes moved durability overlays (calls the private `ComposeSlotOverlays`, as Dovidarium does), recomputes the scrollbar height and the result count |
| `GuiElementItemSlotGridBase.SlotClick(ICoreClientAPI, int, EnumMouseButton, bool, bool, bool)` | prefix | right button on a tile or a player-expanded member, empty cursor, no right-drag: toggle, return false (no packet); reads the private `isRightMouseDownStartedInsideElem` |
| `ItemSlot.GetStackDescription(IClientWorldAccessor, bool)` | postfix | tooltip lines, `ItemSlotCreative` in the current grouped view only |
| `GuiDialog.OnRenderGUI(float)` | postfix | borders and tile marks, `GuiDialogInventory` showing its creative composer only, scissored to the grid's clip bounds, z 160 |

A toggle calls the dialog's private `update()` (re-runs the search, cheap with Dovidarium's cache, then our
`OnTextChanged` postfix) and restores the scroll position afterwards. None of the targets is gated by
Dovidarium or patched by TooManyTabs; nothing on the do-not-patch list in hooks.md §6 is patched (only called).

**The first build.** The game builds the creative dialog in `OnOwnPlayerDataReceived`, before level
finalize, so before `TidyVariantsModSystem` has resolved the rules (its `AssetsFinalize`): that build's grid
hides and groups nothing, and Dovidarium's compose reuse keeps it for the first open. So `TidyCreativeModSystem`
listens to `TidyVariantsModSystem.Resolved` (the client's bridge only; in singleplayer the server's fires too,
on the server thread) and `CreativeUi.RefreshBuiltDialog` redoes what the build did on the dialog in
`capi.Gui.LoadedGuis`: `DetermineAvailableSlots()` and `update()`, which run the hide and regroup postfixes.

## Manual test checklist (in game, creative mode, the full pack)

The GUI could not be run in CI or by the agent that wrote it; run this before release.

1. **Logs.** In `client-main.log`: `[seraphhorizons] Tidy Variants: client: ... entries ...` and
   `[seraphhorizons] Tidy Variants creative inventory: 5/5 patches applied`, no `creative inventory ... failed`. Compare
   Dovidarium's lines with a run without this build: `patch-status side=client phase=startup active=20/25`
   and `phase=late-audit active=19/25` with `disabled-by-late-conflict=1` (the TooManyTabs one) must not
   change; no new `incompatible Harmony` or `disabled ...` line naming `seraphhorizons`.
2. **First open.** The very first time the inventory is opened after joining, groups are already
   collapsed into tiles and hidden variants are gone (the dialog is built before the rules resolve; see
   **The first build** above). Switch to survival and back, and reopen: still grouped.
3. **Hidden.** Search `chest`: one chest per wood/type, no east/south/west copies. Search a door or trapdoor:
   no open copies. Clear the search: the count drops accordingly.
4. **Tiles.** The "Blocks"/"Items" tabs show ingots, ores, rock blocks etc. as one tile each with the corner
   mark. Hover a tile: the tooltip shows the group title, "N variants" and "Right-click to expand (or
   Ctrl+G)"; hover an expanded member: "Right-click to collapse (or Ctrl+G)".
5. **Click takes the representative.** Click a tile: the cursor holds the tile's item. Shift-click: a full
   stack goes to the hotbar. Middle-click and Ctrl+wheel behave as vanilla. Drop the item back on the grid
   (vanilla deletes it).
6. **Right-click.** With an empty cursor, right-click a tile: the group opens in place with amber borders,
   its first member under the mouse, scroll position unchanged, nothing lands on the cursor. Right-click
   any member: it collapses. Hold the right button after a toggle and sweep across the grid: nothing else
   toggles or changes; release and right-click again: it toggles again (one toggle per press). Right-click
   a plain slot: nothing happens (as vanilla). With an item on the cursor (e.g. 10 planks): right-click a
   tile or member voids one, as vanilla, and does not toggle; right-drag over several plank slots voids one
   per slot, as vanilla. Search so only one group is left (auto-expanded): right-click a member does
   nothing.
7. **Hotkey.** Hover a tile, press Ctrl+G: expands. Hover a member, Ctrl+G: collapses. With the search box
   focused, Ctrl+G does nothing in the grid. Rebind it in Settings > Controls and check the tooltip hint
   follows. Outside the inventory, Ctrl+G must not do anything new.
8. **Search.** Type `copper`: ingot/ore tiles contain only copper members (expand to check), the
   representative is one of the matches. Type a word matching one group only (e.g. a specific ore name):
   that group is shown expanded with fainter borders. The count reads "N results in M tiles" while
   grouping shortens the list, "N results" otherwise. Nothing that matched before is missing (expand tiles).
9. **Persistence.** Expand two groups, close and reopen the inventory, switch tabs, then relog: still
   expanded. Check `ModConfig/seraphhorizons-tidyvariants-creative.json`. Corrupt it (write `{`), relog: a warning, all
   collapsed, the next toggle rewrites the file.
10. **Scrolling.** Long tabs scroll to the end with no blank rows and no clipped last row; the scrollbar
   handle size matches the shorter list. Scroll down, expand a group: position kept.
11. **TooManyTabs.** With the tab columns full, scroll the tab list with the mouse wheel over it, switch to a
    tab far down: the grid regroups for that tab, the tab scroll keeps working, only one tab is active.
12. **Dovidarium.** Creative stacks are at full durability, so no durability bar may appear on any tile
    or member after searching, expanding and collapsing (a stray bar means an overlay index went stale).
    Fast typing in the search box stays smooth; scrolling a long tab is as fast as before.
13. **Survival.** Switch to survival (`/gm 0`) and open the inventory: no borders, marks or tooltip lines;
    switch back to creative: grouping is back.
14. **Handbook and other grids.** Chests, the hotbar, the handbook's item lists: no borders, no extra
    tooltip lines, right-click behaves as before (e.g. right-click a chest slot with an empty cursor
    takes half the stack).
