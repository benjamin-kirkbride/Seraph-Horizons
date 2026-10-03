# Creative inventory: mod tabs

A Seraph Horizons tweak (switch `CreativeModTabs`), not part of Tidy Variants, but it lives next to its
creative docs because it hooks the same dialog. Code: `mods-src/seraphhorizons/CreativeModTabs/` (`Core/`
is game-independent and unit-tested in `tests/CreativeModTabsTests.cs`; `Game/` is the rest). Engine facts
about the creative dialog, Dovidarium and TooManyTabs are in [hooks.md](hooks.md) §1 and §4; everything
below was read against decompiled 1.22.7, Dovidarium 0.9.5 and TooManyTabs 1.0.0.

## Behaviour

- A button sits above the creative dialog's right-hand tab column: `Tabs: Default ⇄` or `Tabs: Mod ⇄`
  (the arrows are drawn, so no font needs the glyph; the words are lang keys `creativemodtabs-mode-*`).
  Clicking it flips the mode. With 16 default tabs or fewer the game has no right column; the button is in
  the same place.
- **Default tabs**: the dialog exactly as the game builds it.
- **Mod tabs**: one tab per mod that has creative-listed stacks, holding every stack the game gathers into any
  default tab (`CreativeInventoryTabs` and `CreativeInventoryStacks`), once, in the game's own order (blocks by
  material, then items by tool). Nothing unlisted.
  - Attribution is by the stack's collectible code domain, mapped to the **owning mod**: `game` → the base
    game; else the mod whose modid is the domain; else the mod with the most blocktype and itemtype files in
    the domain (it defines those collectibles: `vintageengineering` for `vinteng`, `p1explosives` for `bomb`,
    `ageofflaxfork` for `ageofflax`, `oilsresoaped` for `oils`); else the mod with the most assets in it; else
    the raw domain. A mod's domains share one tab (none in the pack as locked has creative stacks in two).
    Ties go to the smaller modid (`DomainOwners`).
  - Tabs: "Vintage Story" (domain `game`, lang key `creativemodtabs-tab-game`) first, then by the mod's
    display name, ignoring case. Labels are the name only.
  - All mod tabs are in one column on the right, under the button, which scrolls with the mouse wheel and
    keeps the selected tab in view; a thin bar shows where it is. The left column is hidden.
- Search works as in vanilla: over the current tab only. Tidy Variants hides and groups in mod tabs as in
  default tabs (its rules are keyed by stack, not by tab).
- The mode and the last tab of each mode persist per client in `ModConfig/seraphhorizons-creativemodtabs.json`
  (`{"Version": 1, "Mode": "Mod", "DefaultTab": "<code>", "ModTab": "<code>"}`). Missing means default mode;
  unreadable logs a warning and means default mode. Flipping keeps the search text.
- The button shows once the mod tabs are built and their search is ready, a few seconds after joining (the
  game builds the default tabs' search text in the background first).
- With the switch off on a side, that side patches, adds, sends and shows nothing. Both sides need it on
  for the button to appear (the client builds nothing without the server's list).

## Design

### (a) One authority for the tab list, real tabs on both sides

A creative click travels as `(slot id, TabIndex)`; the server does `SetTab(TabIndex)` and reads its own
`inventory[slot]` (hooks.md §1). So a tab the client shows must be a real `CreativeTab` with the same index
and the same slots on the server. The mod tabs are therefore real tabs appended after the default ones on
both sides, numbered on from the default count.

Which domain goes to which tab must come from one side: the server and the client load different mods
(server-only and client-only ones), so the owner of a domain could differ. The **server** plans
(`ModTabsModSystem.EnsureAuthority`, at `WorldReady`, when collectibles are final): it scans the creative
stacks (`CreativeStacks.Scan`, a mirror of `InventoryPlayerCreative.GatherTabStacks` that keeps each stack
once), maps their domains to owners from the loaded mods' asset origins (each mod's folder is an asset
origin at `<folder>/assets`, and every loaded asset knows its origin), and orders the tabs. It sends the list
`{code, name, isGame, domains, count, hash}` plus its default tab count to each client on `PlayerNowPlaying`
(channel `seraphhorizons-creativemodtabs`, `ModTabsPacket`), and appends the tabs to every
`InventoryPlayerCreative` in a postfix on `UpdateFromWorld` (which builds the default tabs once per inventory).

The **client** scans its own collectibles the same way (it has the server's), places each stack by the
server's domain lists, and builds the same tabs with the server's indices. Before using them it checks its
default tab count and each tab's stack count and FNV hash of collectible codes against the server's
(`ModTabPlanner.Verify`); any difference logs a warning and the client shows no mod tabs. Timing: the list
arrives after the client has loaded, normally after the creative inventory exists; if not, every compose of
the creative dialog retries. The game builds the default tabs' search caches on the thread pool after the
dialog's first build (`OnOwnPlayerDataReceived`); the client waits for them (polling), then copies each mod
tab slot's search text from the first default slot with the same stack (same text as
`CreativeTab.CreateSearchCache` would make, without a second pass over 29,000 descriptions), and only then
shows the button and applies a saved mod mode. Waiting also keeps the inventory's tab list unchanged while the
game's background pass walks it.

### (b) Keeping the mod tabs out of the default columns

On the client the mod tabs never enter the game's tab list. `ComposeCreativeInvDialog` lays out every tab of
`creativeInv.CreativeTabs.Tabs` by `config/creativetabs.json` order, 16 left and the rest right; mod tabs in that
list would land among the default ones and shift the split. Instead the client swaps the inventory's public
`tabs` field:

- **default mode**: `tabs` is the game's own `CreativeTabs` object, untouched, so the dialog is vanilla to the
  last tab (same codes, indices, order, columns, and TooManyTabs' or Dovidarium's handling of them);
- **mod mode**: `tabs` is a `CreativeTabs` holding only the selected mod tab (its index kept). The dialog then
  composes a single tab in its left column and no right column. A prefix on `GuiComposer.Compose` (filtered to
  `"inventory-creative"`) swaps that column for a `HiddenVerticalTabs`, a `GuiElementVerticalTabs` subclass
  with the same bounds and tab that draws nothing and takes no input (the dialog still casts it with
  `GetVerticalTab` and calls `SetValue` on it after composing).

Selecting a mod tab swaps that tab in and calls the dialog's private `OnTabClicked(0, tab)`, so the dialog's
current tab, the inventory's `SetTab`, `DetermineAvailableSlots` (Tidy Variants' hide), the search and Tidy
Variants' regroup all run as for a default tab, and slot clicks send the mod tab's index. Flipping the mode
swaps `tabs`, sets the dialog's `currentTabIndex` and the inventory's tab, and calls the public
`ComposeGui(false)`. Dovidarium's composer reuse compares a signature of the tab codes and the current tab
index, so it lets that rebuild run; on a later reopen it reuses the composer as usual.

Rejected: patching `ComposeCreativeInvDialog` or `ComposeGui` (Dovidarium gates them Exclusive), or
`GuiComposerHelpers.AddVerticalTabs` (Exclusive for Dovidarium's right-tab width, which is active in a pack
without TooManyTabs); filtering the `CreativeTabs.Tabs` getter only while composing (no clean signal for
"composing" without one of those patches); keeping all tabs in the list and editing the composed columns (the
right column's click handler indexes the full ordered array, and TooManyTabs engages on two columns).

### (c) The scrolling strip, TooManyTabs and Dovidarium

The button and the strip live in their own composer, `seraphhorizons-creativemodtabs`, added to the inventory
dialog's composers while it shows the creative composer (added after each creative compose, removed after a
survival compose out of creative mode). Its bounds are a child of the creative composer's bounds, placed as the
game places its right column (`Fixed(0, 35, w, 545).FixedRightOf(dialog).WithFixedAlignmentOffset(-4, 0)`), with
the button above y 35. Its own composer because the GUI manager hands the mouse wheel to a dialog only when one
of its composers' bounds holds the mouse (else to whichever dialog takes it first, e.g. the hotbar), and the
game's own tab columns lie outside the creative composer: TooManyTabs and Dovidarium patch `GuiDialog.OnMouseWheel`
or `GuiManager.OnMouseWheel` to get around that, which this tweak cannot do without tripping Dovidarium's guard.
The composer is created once and cleared and refilled on each fresh creative compose (creating one while the
game recomposes all composers would change the collection it iterates); a recompose of the same creative
composer (window resize, GUI scale) only recomposes it. The flip runs on the next frame, since it rebuilds the
composer whose mouse handler is running.

**Composer order.** The overlay must come before `"maininventory"` in the dialog's composers.
`GuiDialog.OnMouseWheel` asks the composers in order, and `GuiComposer.OnMouseWheel` first offers the wheel to the
elements under the mouse, then to every element regardless of position. The grid's `GuiElementScrollbar` takes it
without a position check whenever the tab overflows, so with the creative composer first a wheel over the strip
would scroll the grid. `GuiDialog.OnRenderGUI` also takes `MouseOverCursor` from each composer in turn, last one
wins, so the creative composer must be last for the search box's text cursor. The composers are an order-keeping
dictionary (`ConcurrentSmallDictionary`): setting an existing key keeps its place, a new key goes last, removing
keeps the rest in order, and readers iterate a snapshot. So when the overlay is added, `"maininventory"` is taken
out and put back after it, the same composer under the same key (Dovidarium's reuse compares the composer
object, so it sees no change). When the game itself removes and re-adds `"maininventory"` (mode change, backpack
resize) it lands after the overlay anyway, and `Composers["maininventory"] = x` replaces in place. The strip also
takes the wheel only with the mouse over it, so whichever order the composers are in, a wheel over the grid
scrolls the grid.

`ModTabStrip` is a plain `GuiElementTextBase`, not a `GuiElementVerticalTabs`: it draws the game's tab look
itself (one texture for all tabs, one selected texture per tab), scissored to its bounds, scrolls by
`TabScroll` (unit-tested), and is invisible to everything that patches or counts vertical tab columns.

- **TooManyTabs** prefixes `GuiComposer.Compose` and engages only on a composer with exactly two non-empty
  `GuiElementVerticalTabs`, one 500 to 600 px high. Default mode: unchanged, the button is no vertical tab.
  Mod mode: one (hidden) column, so it stays out. Its `SetValue` postfix and `LastSelectedDataInt` only touch
  columns it engaged; back in default mode, the dialog's own `SetValue` after composing resets its selection.
- **Dovidarium**: nothing it gates is patched. Its right-tab width feature acts on `verticalTabsR`, which
  mod mode doesn't have; its search cache keys on the cache dictionaries, which the mod tabs have their own of.

## Patches

Client, Harmony id `seraphhorizons.modtabs.client`, applied in `ModTabsModSystem.StartClientSide` when the switch
is on; server, `seraphhorizons.modtabs`. Applied by hand, never `PatchAll`; a missing target logs a warning and
the tweak does nothing on that side. Every body catches its exceptions (logged once per place).

| Target (verified in decompiled 1.22.7) | Side | Patch | Does |
|---|---|---|---|
| `InventoryPlayerCreative.UpdateFromWorld(IWorldAccessor)` (internal) | server | postfix | appends the mod tabs after the default ones, once per inventory |
| `GuiComposer.Compose(bool)` | client | prefix | `"inventory-creative"` in mod mode: swaps the one-tab column for `HiddenVerticalTabs` |
| | | postfix | `"inventory-creative"`: builds the mod tabs if still pending, then adds or recomposes the overlay composer; `"inventory-backpack"` out of creative: removes it |

No other pack mod references `UpdateFromWorld` (string scan of every DLL in `build/mods`). Called, not patched:
`GuiDialogInventory.ComposeGui(bool)`, `OnTabClicked(int, GuiTab)`; read or written by reflection:
`GuiDialogInventory.creativeInvDialog`, `currentTabIndex`, `GuiComposer.interactiveElements`/`staticElements`,
`GuiElementVerticalTabs.tabs`.

`tests/PackTests/CreativeModTabsScenarios.cs` (Atlas) builds a creative inventory on the server with the whole
pack and requires: the default tabs identical to a build without the tweak, the mod tabs after them with the
announced codes and counts, every distinct creative stack in exactly one mod tab, the game tab first and the rest
by name, and `vinteng`, `ageofflax`, `bomb` and `oils` under their owning mods. It sends the packet through
protobuf-net and back (the real plan, an empty packet, and entries with every field missing or an empty domain
list) and requires the same specs. It then takes the client's path from the round-tripped packet (scan, place by
the server's domains, the check, build the tabs on an inventory without the server's postfix) and requires every
mod tab slot to equal what the server's inventory returns for that tab index and slot id after `SetTab`, which is
how the server resolves a click. All of this runs on one world, so it checks the build and the indices on both
paths, not a client that loads different mods (that case is the client's count and hash check, unit-tested in
`tests/CreativeModTabsTests.cs`). `CreativeModTabsOffScenarios` boots with the switch off.

A click can't reach a server inventory that lacks the mod tabs: the server leaves them out of an inventory only
when its default tab count differs from the plan, and then every inventory and every client differ alike (the
client compares its own count with the announced one and shows nothing).

## Manual test checklist (in game, creative mode, the full pack)

The GUI cannot run in CI or headless Atlas; check this before release.

1. **Logs.** `server-main.log`: `Creative mod tabs: N tabs for M creative stacks from D domains, after K default
   tabs` (with the pack as locked when this was written: 77 tabs, 29,539 stacks, 53 default tabs; the tab list
   is in `server-debug.log`). `client-main.log`: `client patch applied`, then `N mod tabs with M stacks built`; no `not shown` or
   `failed`. Dovidarium's `patch-status` lines (`active=20/25`, late audit `19/25` with
   `disabled-by-late-conflict=1`) are unchanged, and no `incompatible Harmony` line names `seraphhorizons`.
2. **Default mode is vanilla.** First join: the dialog looks exactly as without the tweak (16 tabs left, the
   rest right, TooManyTabs scrolling the right column), plus `Tabs: Default ⇄` above the right column, not
   overlapping anything. Hover it: highlighted.
3. **Flip to mod mode.** The left column disappears; the right shows "Vintage Story" first, then the mods by name.
   The grid shows the game tab's items. The button reads `Tabs: Mod ⇄`.
4. **Strip.** Wheel over the strip scrolls it, all the way to the last tab (not the hotbar, not the grid); the bar
   on its right moves. Wheel over the grid scrolls the grid, not the strip. Click a tab far down: it is
   highlighted and the grid shows that mod's items. Close and reopen the inventory: the same tab, scrolled into
   view. Long mod names end in "...". With the mouse over the search box the text cursor shows.
5. **Attribution.** Vintage Engineering's tab holds its machines (domain `vinteng`), Sensible Explosives' its
   bombs (`bomb`), AgeOfFlax's the flax tools (`ageofflax`). A stack listed in several default tabs (e.g. a
   block in "General" and in its mod's own default tab) appears once. (Once the pack pins ppex 0.7.1, the
   creative steam source, listed in two default tabs, appears once, in the Seraph Horizons tab.)
6. **Clicks.** In a mod tab: left-click takes the item, shift-click a full stack, middle-click works, and the item
   that lands is the one clicked (a desync would give another item). Drop an item on the grid: deleted.
7. **Search.** In a mod tab, type part of a name: only that tab's matches, count right. Flip the mode with text in
   the box: the text stays and filters the new tab.
8. **Tidy Variants.** In the game tab and a mod tab: hidden variants are gone, groups are tiles, right-click and
   Ctrl+G expand and collapse, borders and tooltips as in default tabs.
9. **Back to default.** Flip back: the default tab selected before is selected again, both columns as before,
   TooManyTabs scrolling and its single-active-tab behaviour intact.
10. **Persistence.** Select a default tab other than the first, flip to mod mode, select a mod tab, relog: the
    dialog opens in mod mode on the same mod tab (after the few seconds the button needs); flip back: the default
    tab selected before the relog. Check `ModConfig/seraphhorizons-creativemodtabs.json`. Write `{` into it, relog: a warning,
    default mode. Delete it: default mode.
11. **Survival.** `/gm 0` with the dialog open and closed: no button, no strip. `/gm 1`: back, in the saved mode;
    with the dialog open, the strip and the grid still each scroll only under the mouse.
12. **Resize.** Change the window size and the GUI scale with mod mode shown: the strip and the button follow the
    dialog.
13. **Without TooManyTabs** (remove it from the pack): default mode as vanilla with Dovidarium's right-column width
    and scroll; mod mode as above.
14. **Switch off** (`"CreativeModTabs": false` on the client, then on the server): no button; the other side logs
    nothing unusual.
