# Creative inventory: hidden variants and collapsible groups (#256)

Part of #252. Client only. Code: `mods-src/tidyvariants/Game/Creative/` (game layer) and
`mods-src/tidyvariants/Core/CreativeView.cs` (grouping logic, unit-tested in `tests/CreativeViewTests.cs`).
Hooks and the reasons for them are in [hooks.md](hooks.md) §1, §4 and §6.

## Behaviour

- **Hidden variants** (orientation and open/closed duplicates, override `hide` rules) never appear in a
  creative tab, in search either: they are removed from the grid's `availableSlots`, which vanilla and
  Dovidarium search both read.
- **Groups collapse after search.** Search runs over the flat list as before (vanilla or Dovidarium's
  cache). What is left is then collapsed: each group with two or more matching members becomes one tile at
  the position of its first match, showing the best-ranked matching member (the representative). A tile
  contains only matching members, so search never loses an item. A group with one match is a plain slot.
  If only one group is left, it is shown expanded.
- **A tile is the representative's real slot.** Click, shift-click, right-click and the mouse wheel are
  vanilla and take the representative. Slot ids never change and the creative inventory itself is never
  touched (the server resolves clicks by slot id and tab index against its own copy).
- **Expand / collapse:** Alt+left-click on a tile expands its group inline; Alt+left-click on any member
  collapses it again. Many Linux window managers take Alt+click for themselves, so the hotkey
  **Ctrl+G** (Controls settings, "Creative inventory: expand or collapse the hovered variant group",
  rebindable) toggles the group of the slot under the mouse. Alt+click with an item on the cursor stays
  vanilla. Alt+click on a member of an auto-expanded group does nothing (there is nothing to collapse).
  The scroll position is kept.
- **Marks:** expanded members get an amber border; a collapsed tile gets a small amber square in its top
  right corner. Members of an auto-expanded group get a fainter border.
- **Tooltip** of a tile or member adds the group title (the group's lang key, else a title derived from the members'
  names, else the representative's name; see the handbook doc's **Title**) with the number of matching members, and the expand/collapse hint naming the current hotkey. It is
  added in `ItemSlot.GetStackDescription`, which is not on the search path, so the hint is not searchable.
- **Result count:** "N results" as before, or "N results in M tiles" when grouping shortened the list.
- **Expand state persists per client** in `ModConfig/tidyvariants-creative.json`
  (`{"Version": 1, "Expanded": ["<group id>", ...]}`), by group id. A missing or unreadable file means
  everything collapsed (a warning is logged, the file is rewritten on the next toggle). Ids of groups that
  don't exist in the current pack are kept.

## Patches

`TidyCreativeModSystem` (`ShouldLoad` client only) applies them by hand in `StartClientSide` with the Harmony
id **`tidyvariants.creative`** and removes them on `Dispose`. No `[HarmonyPatch]` attributes, so the main
system's `PatchAll` (both sides) never applies them; `tests/PackTests/TidyVariantsCreativeScenarios.cs`
checks the system is disabled and the id patched nothing on the server. Each patch is applied on its own
(a missing target logs a warning and only that part stays vanilla), and every patch body catches its
exceptions: the first failure per place is logged, that call stays vanilla. With no client resolution
(`TidyVariantsModSystem.ForSide(Client)` null) every patch is a no-op. Only grids whose inventory has
`ClassName == "creative"` are touched.

| Target (verified in decompiled 1.22.7) | Patch | Does |
|---|---|---|
| `GuiElementItemSlotGrid.DetermineAvailableSlots(int[] visibleSlots)` | postfix | drops hidden slots from `availableSlots` and `renderedSlots` (Dovidarium policy AllowAny) |
| `GuiDialogInventory.OnTextChanged(string text)` (private) | postfix | regroups `renderedSlots` in place, fixes `rows`, recomposes moved durability overlays (calls the private `ComposeSlotOverlays`, as Dovidarium does), recomputes the scrollbar height and the result count |
| `GuiElementItemSlotGridBase.SlotClick(ICoreClientAPI, int, EnumMouseButton, bool, bool, bool)` | prefix | Alt+left on a grouped slot with an empty cursor: toggle, return false (no packet) |
| `ItemSlot.GetStackDescription(IClientWorldAccessor, bool)` | postfix | tooltip lines, `ItemSlotCreative` in the current grouped view only |
| `GuiDialog.OnRenderGUI(float)` | postfix | borders and tile marks, `GuiDialogInventory` showing its creative composer only, scissored to the grid's clip bounds, z 160 |

A toggle calls the dialog's private `update()` (re-runs the search, cheap with Dovidarium's cache, then our
`OnTextChanged` postfix) and restores the scroll position afterwards. None of the targets is gated by
Dovidarium or patched by TooManyTabs; nothing on the do-not-patch list in hooks.md §6 is patched (only called).

## Manual test checklist (in game, creative mode, the full pack)

The GUI could not be run in CI or by the agent that wrote it; run this before release.

1. **Logs.** In `client-main.log`: `[tidyvariants] client: ... entries ...` and
   `[tidyvariants] creative inventory: 5/5 patches applied`, no `creative inventory ... failed`. Compare
   Dovidarium's lines with a run without this build: `patch-status side=client phase=startup active=20/25`
   and `phase=late-audit active=19/25` with `disabled-by-late-conflict=1` (the TooManyTabs one) must not
   change; no new `incompatible Harmony` or `disabled ...` line naming `tidyvariants`.
2. **Hidden.** Search `chest`: one chest per wood/type, no east/south/west copies. Search a door or trapdoor:
   no open copies. Clear the search: the count drops accordingly.
3. **Tiles.** The "Blocks"/"Items" tabs show ingots, ores, rock blocks etc. as one tile each with the corner
   mark. Hover a tile: the tooltip shows the group title, "N variants" and the hint naming Ctrl+G.
4. **Click takes the representative.** Click a tile: the cursor holds the tile's item. Shift-click: a full
   stack goes to the hotbar. Right-click and Ctrl+wheel behave as vanilla. Drop the item back on the grid
   (vanilla deletes it).
5. **Alt+click.** Alt+left-click a tile: the group opens in place with amber borders, the tile's slot
   still under the mouse, scroll position unchanged. Alt+click any member: it collapses. If the window
   manager eats Alt+click, note it and use step 6.
6. **Hotkey.** Hover a tile, press Ctrl+G: expands. Hover a member, Ctrl+G: collapses. With the search box
   focused, Ctrl+G does nothing in the grid. Rebind it in Settings > Controls and check the tooltip hint
   follows. Outside the inventory, Ctrl+G must not do anything new.
7. **Search.** Type `copper`: ingot/ore tiles contain only copper members (expand to check), the
   representative is one of the matches. Type a word matching one group only (e.g. a specific ore name):
   that group is shown expanded with fainter borders. The count reads "N results in M tiles" while
   grouping shortens the list, "N results" otherwise. Nothing that matched before is missing (expand tiles).
8. **Persistence.** Expand two groups, close and reopen the inventory, switch tabs, then relog: still
   expanded. Check `ModConfig/tidyvariants-creative.json`. Corrupt it (write `{`), relog: a warning, all
   collapsed, the next toggle rewrites the file.
9. **Scrolling.** Long tabs scroll to the end with no blank rows and no clipped last row; the scrollbar
   handle size matches the shorter list. Scroll down, expand a group: position kept.
10. **TooManyTabs.** With the tab columns full, scroll the tab list with the mouse wheel over it, switch to a
    tab far down: the grid regroups for that tab, the tab scroll keeps working, only one tab is active.
11. **Dovidarium.** Creative stacks are at full durability, so no durability bar may appear on any tile
    or member after searching, expanding and collapsing (a stray bar means an overlay index went stale).
    Fast typing in the search box stays smooth; scrolling a long tab is as fast as before.
12. **Survival.** Switch to survival (`/gm 0`) and open the inventory: no borders, marks or tooltip lines;
    switch back to creative: grouping is back.
13. **Handbook and other grids.** Chests, the hotbar, the handbook's item lists: no borders, no extra
    tooltip lines, Alt+click behaves as before.
