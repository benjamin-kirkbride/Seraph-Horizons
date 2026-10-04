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
- **Mod tabs**: the left column keeps the default tabs it has in default mode, in the same order (the 16 the
  game puts first: in this pack the 14 that `config/creativetabs.json` orders, then two others). The right column
  holds one tab per mod that has creative-listed stacks, each holding every stack the game gathers into any
  default tab (`CreativeInventoryTabs` and `CreativeInventoryStacks`), once, in the game's own order (blocks by
  material, then items by tool). Nothing unlisted.
  - Attribution is by the stack's collectible code domain, mapped to the **owning mod**: `game` → the base
    game; else the mod whose modid is the domain; else the mod with the most blocktype and itemtype files in
    the domain (it defines those collectibles: `p1explosives` for `bomb`,
    `ageofflaxfork` for `ageofflax`, `oilsresoaped` for `oils`); else the mod with the most assets in it; else
    the raw domain. A mod's domains share one tab (none in the pack as locked has creative stacks in two).
    Ties go to the smaller modid (`DomainOwners`).
  - Tabs: "Vintage Story" (domain `game`, lang key `creativemodtabs-tab-game`) first, then by the mod's
    display name, ignoring case. Labels are the name only.
  - Both columns are the game's own, so everything that applies to them in default mode applies here:
    TooManyTabs widens the right column to its longest name, scrolls it with the mouse wheel one tab per notch
    and with its scrollbar, keeps one tab selected across both columns and the selected tab in view. Without
    TooManyTabs, Dovidarium's right-column scroll takes over (read in its code, not tried); with neither, the
    right column doesn't scroll, as in default mode. The pack pins TooManyTabs.
- Search works as in vanilla: over the current tab only. Tidy Variants hides and groups in mod tabs as in
  default tabs (its rules are keyed by stack, not by tab).
- The mode and the last tab of each mode persist per client in `ModConfig/seraphhorizons-creativemodtabs.json`
  (`{"Version": 1, "Mode": "Mod", "DefaultTab": "<code>", "ModTab": "<code>"}`; `ModTab` may be a default tab of
  the left column). Missing means default mode; unreadable logs a warning and means default mode. A flip stays
  on the selected tab when the other mode has it too (a tab of the left column), else goes to that mode's last
  tab, else its first. Flipping keeps the search text. After joining, a saved mod mode opens on its saved tab.
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

### (b) Mod mode: a second tab list, laid out by the game

On the client the mod tabs never enter the game's own tab list. `ComposeCreativeInvDialog` lays out every tab of
`creativeInv.CreativeTabs.Tabs`, 16 left and the rest right; mod tabs in that list would land among the default
ones and shift the split. Instead the client swaps the inventory's public `tabs` field:

- **default mode**: `tabs` is the game's own `CreativeTabs` object, untouched, so the dialog is vanilla to the
  last tab (same codes, indices, order, columns, and TooManyTabs' or Dovidarium's handling of them);
- **mod mode**: `tabs` is a second `CreativeTabs` holding the default tabs of default mode's left column and all
  the mod tabs, the same tab objects. The mod tabs keep the server's indices: they go into `TabsByCode` directly,
  since `CreativeTabs.Add` numbers tabs from its own counter. Nothing needs the indices to be contiguous: the
  dialog's tabs carry `GuiTab.DataInt` = the tab's `Index`, and `InventoryPlayerCreative.SetTab` finds the tab
  whose `Index` matches.

**The order.** `Tabs` iterates in insertion order (`OrderedDictionary.ValuesOrdered`). The dialog orders them by
`listOrder` from `config/creativetabs.json` (read with `capi.Assets.TryGet`, so mods can patch it; a code it
doesn't name gets 1.0), inserting each tab before the first one already placed with an equal or higher order. So
lower orders come first and tabs of equal order come out in the **reverse** of iteration order; the first 16 go
left, the rest right. Vanilla names 14 codes, all below 1.0; every other tab, the mod tabs included, is 1.0. The
mod mode's list is therefore built as: the mod tabs in reverse, then the kept default tabs in the game's own
iteration order. The dialog then shows default mode's left column unchanged and the mod tabs in order as the
right column. `TabLayout` (`Core/`, unit-tested) mirrors the dialog's rule and builds that arrangement; it is the
only one that works, since list order dominates. When no arrangement can (a left tab ordered after 1.0, a mod
tab code given an order, or fewer than 16 default tabs), the list is built the same way, every tab is in one of
the columns, and one notification says so. `CreativeModTabsScenarios` checks the arrangement against the pack's
real tab codes and config.

The dialog's own tab click handlers select tabs in both columns (the right column's handler calls
`OnTabClicked(i + 16, tabs[i + 16])`), so the dialog's current tab, the inventory's `SetTab`,
`DetermineAvailableSlots` (Tidy Variants' hide), the search and Tidy Variants' regroup all run as for any tab,
and slot clicks send the mod tab's index. Flipping the mode swaps `tabs`, sets the dialog's `currentTabIndex` and
the inventory's tab (both must name a tab of the new list, or the next build selects nothing), and calls the
public `ComposeGui(false)`. Dovidarium's composer reuse compares a signature of the tab codes in order and the
current tab index, so it lets that rebuild run; on a later reopen it reuses the composer as usual.

**Names.** The dialog labels a tab `Lang.Get("tabname-" + code)`, which looks up `game:tabname-<code>` in the
current language and falls back to the default one. The client adds an entry per mod tab to both, with the
server's name for the mod (or its own `creativemodtabs-tab-game` text for the game's tab), braces doubled since the
game formats every entry. There is no API to add an entry; `ITranslationService.GetAllEntries()` returns the
language's own dictionary. That dictionary isn't thread-safe and the game's background search pass reads lang
entries, so the entries are added once that pass is done (when the button appears), and again before each flip
to mod mode where missing (a reloaded language). The dialog sizes its left column to the widest name of all its
tabs, both columns (vanilla), so a long mod name widens the left column in mod mode.

**Remembering the tab.** With the game's handlers selecting tabs, the client reads `inv.CurrentTab` when it
matters: before a flip, when the dialog closes (`GuiDialog.OnClosed`, a public event) and when the world is left
(the mod system's `Dispose`). Nothing is patched for it.

Rejected: a scrolling strip of our own drawing the mod tabs in our own composer, with the dialog given only the
selected mod tab and its column hidden (the first version): its scrolling never matched TooManyTabs' (step,
snapping, scrollbar), the left column was lost, and it needed a second patch to hide the column; the mod tabs
alone in mod mode's list (the dialog would put the first 16 of them in the left column); patching
`ComposeCreativeInvDialog` or `ComposeGui` (Dovidarium gates them Exclusive), or
`GuiComposerHelpers.AddVerticalTabs` (Exclusive for Dovidarium's right-tab width, which is active in a pack
without TooManyTabs); filtering the `CreativeTabs.Tabs` getter only while composing (no clean signal for
"composing" without one of those patches); keeping all tabs in the list and editing the composed columns (the
right column's click handler indexes the full ordered array).

### (c) The button, TooManyTabs and Dovidarium

The button lives in its own composer, `seraphhorizons-creativemodtabs`, added to the inventory dialog's composers
while it shows the creative composer (added after each creative compose, removed after a survival compose out of
creative mode). Its own because the creative composer is the game's to build: the button comes and goes with the
mod tabs' readiness and the game mode without a rebuild of the dialog, and nothing is added to what the game,
TooManyTabs and Dovidarium compose and inspect. Its bounds are a child of the creative composer's bounds, placed as
the game places its right column (`FixedRightOf(dialog).WithFixedAlignmentOffset(-4, 0)`), above y 35 where the
column starts. The composer is created once and cleared and refilled on each fresh creative compose (creating one
while the game recomposes all composers would change the collection it iterates); a recompose of the same creative
composer (window resize, GUI scale) only recomposes it. The flip runs on the next frame, since it rebuilds the
composer whose mouse handler is running.

**Composer order.** `GuiDialog.OnRenderGUI` takes `MouseOverCursor` from each composer in turn, last one wins, so
the creative composer must come after the overlay or the search box loses its text cursor. The composers are an
order-keeping dictionary (`ConcurrentSmallDictionary`): setting an existing key keeps its place, a new key goes
last, removing keeps the rest in order, and readers iterate a snapshot. So when the overlay is added,
`"maininventory"` is taken out and put back after it, the same composer under the same key (Dovidarium's reuse
compares the composer object, so it sees no change). When the game itself removes and re-adds `"maininventory"`
(mode change, backpack resize) it lands after the overlay anyway, and `Composers["maininventory"] = x` replaces
in place.

- **TooManyTabs** prefixes `GuiComposer.Compose` and engages on a composer with exactly two non-empty
  `GuiElementVerticalTabs`, one 500 to 600 px high: the creative dialog in both modes (the button is no vertical
  tab). Its state is per column element (`States`), and a compose makes new elements, so after a flip it lays the
  columns out afresh. Its `LastSelectedDataInt` is static and may name the other mode's tab after a flip: its
  compose prefix then marks no tab active, and the dialog's own `SetValue(…, false)` right after composing runs its
  postfix, which takes the current tab, makes it the only active one in both columns and scrolls it into view.
  Its width has no upper bound, so the longest mod name sets the right column's width.
- **Dovidarium**: nothing it gates is patched. Its composer reuse sees a flip as a changed tab list (above). Its
  right-tab width and scroll replaces `verticalTabsR`; with TooManyTabs it is off (hooks.md §4), without it it acts
  on the right column of both modes alike. Its search cache keys on the cache dictionaries, which the mod tabs
  have their own of.

## Patches

Client, Harmony id `seraphhorizons.modtabs.client`, applied in `ModTabsModSystem.StartClientSide` when the switch
is on; server, `seraphhorizons.modtabs`. Applied by hand, never `PatchAll`; a missing target logs a warning and
the tweak does nothing on that side. Every body catches its exceptions (logged once per place).

| Target (verified in decompiled 1.22.7) | Side | Patch | Does |
|---|---|---|---|
| `InventoryPlayerCreative.UpdateFromWorld(IWorldAccessor)` (internal) | server | postfix | appends the mod tabs after the default ones, once per inventory |
| `GuiComposer.Compose(bool)` | client | postfix | `"inventory-creative"`: builds the mod tabs if still pending, then adds or recomposes the overlay composer; `"inventory-backpack"` out of creative: removes it. (Its prefix only notes whether the call composes.) |

No other pack mod references `UpdateFromWorld` (string scan of every DLL in `build/mods`). Called, not patched:
`GuiDialogInventory.ComposeGui(bool)`; subscribed: `GuiDialog.OnClosed`; read or written by reflection:
`GuiDialogInventory.creativeInvDialog`, `currentTabIndex`; written: the lang dictionaries from
`Lang.AvailableLanguages[...].GetAllEntries()`.

`tests/PackTests/CreativeModTabsScenarios.cs` (Atlas) builds a creative inventory on the server with the whole
pack and requires: the default tabs identical to a build without the tweak, the mod tabs after them with the
announced codes and counts, every distinct creative stack in exactly one mod tab, the game tab first and the rest
by name, and `ageofflax`, `bomb` and `oils` under their owning mods. It sends the packet through
protobuf-net and back (the real plan, an empty packet, and entries with every field missing or an empty domain
list) and requires the same specs. It then takes the client's path from the round-tripped packet (scan, place by
the server's domains, the check, build the tabs on an inventory without the server's postfix) and requires every
mod tab slot to equal what the server's inventory returns for that tab index and slot id after `SetTab`, which is
how the server resolves a click. It also arranges mod mode's list from the inventory's real tab codes and the
loaded `config/creativetabs.json` and requires the ideal layout (default mode's left column, the mod tabs alone on
the right, in order). All of this runs on one world, so it checks the build and the indices on both
paths, not a client that loads different mods (that case is the client's count and hash check, unit-tested in
`tests/CreativeModTabsTests.cs`). `SwitchesOffScenarios` boots with the switch off.

A click can't reach a server inventory that lacks the mod tabs: the server leaves them out of an inventory only
when its default tab count differs from the plan, and then every inventory and every client differ alike (the
client compares its own count with the announced one and shows nothing).

## Manual test checklist (in game, creative mode, the full pack)

The GUI cannot run in CI or headless Atlas; check this before release.

1. **Logs.** `server-main.log`: `Creative mod tabs: N tabs for M creative stacks from D domains, after K default
   tabs` (with the pack as locked when this was written: 71 tabs, 29,039 stacks, 48 default tabs; the tab list
   is in `server-debug.log`). `client-main.log`: `client patch applied`, then `N mod tabs with M stacks built`; no `not shown`,
   `failed` or `share the left column`. Dovidarium's `patch-status` lines (`active=20/25`, late audit `19/25` with
   `disabled-by-late-conflict=1`) are unchanged, and no `incompatible Harmony` line names `seraphhorizons`.
2. **Default mode is vanilla.** First join: the dialog looks exactly as without the tweak (16 tabs left, the
   rest right, TooManyTabs scrolling the right column), plus `Tabs: Default ⇄` above the right column, not
   overlapping anything. Hover it: highlighted.
3. **Flip to mod mode.** The left column is unchanged (same 16 tabs, same order; it may be wider, sized to the
   longest mod name). The right column shows "Vintage Story" first, then the mods by name, as names (never a
   `tabname-seraphhorizons-modtab-…` key). With a left tab selected before the flip, it stays selected; with a right
   one, the first mod tab (or the last one used) is. The button reads `Tabs: Mod ⇄`.
4. **Right column.** It scrolls exactly as the default right column does: TooManyTabs' scrollbar, one tab per wheel
   notch, snapped, all the way to the last tab; the scrollbar drags. Wheel over the grid scrolls the grid. Click a
   tab far down: it is the only highlighted tab in both columns and the grid shows that mod's items; click a left
   tab, then a mod tab again: one highlight at a time. Close and reopen the inventory: the same tab, scrolled into
   view. With the mouse over the search box the text cursor shows.
5. **Attribution.** Sensible Explosives' tab holds its
   bombs (`bomb`), AgeOfFlax's the flax tools (`ageofflax`). A stack listed in several default tabs (e.g. a
   block in "General" and in its mod's own default tab) appears once. (Once the pack pins ppex 0.7.1, the
   creative steam source, listed in two default tabs, appears once, in the Seraph Horizons tab.)
6. **Clicks.** In a mod tab: left-click takes the item, shift-click a full stack, middle-click works, and the item
   that lands is the one clicked (a desync would give another item). The same in a left default tab in mod mode.
   Drop an item on the grid: deleted.
7. **Search.** In a mod tab, type part of a name: only that tab's matches, count right. Flip the mode with text in
   the box: the text stays and filters the new tab.
8. **Tidy Variants.** In the game tab and a mod tab: hidden variants are gone, groups are tiles, right-click and
   Ctrl+G expand and collapse, borders and tooltips as in default tabs.
9. **Back to default.** From a mod tab, flip back: the default tab selected before is selected again (also one of
   the right column, scrolled into view), only that tab highlighted, both columns as before. From a left tab: it
   stays selected.
10. **Persistence.** Select a default tab of the right column, flip to mod mode, select a mod tab, close the
    inventory (no flip), relog: the dialog opens in mod mode on the same mod tab (after the few seconds the button
    needs); flip back: the default tab selected before the relog. Select a left tab in mod mode, relog: mod mode on
    that tab. Check `ModConfig/seraphhorizons-creativemodtabs.json`. Write `{` into it, relog: a warning, default
    mode. Delete it: default mode.
11. **Survival.** `/gm 0` with the dialog open and closed: no button. `/gm 1`: back, in the saved mode, on the
    selected tab.
12. **Resize.** Change the window size and the GUI scale with mod mode shown: the button and both columns follow
    the dialog; the right column still scrolls.
13. **Without TooManyTabs** (remove it from the pack): default mode as vanilla with Dovidarium's right-column width
    and scroll; mod mode's right column the same.
14. **Switch off** (`"CreativeModTabs": false` on the client, then on the server): no button; the other side logs
    nothing unusual.
