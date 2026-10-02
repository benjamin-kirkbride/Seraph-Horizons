# Tidy Variants: the handbook side (#257)

Part of #252. Code: `mods-src/tidyvariants/Core/HandbookPlan.cs` (decisions, unit-tested in
`tests/HandbookPlanTests.cs`) and `mods-src/tidyvariants/Game/Handbook/` (applies them, client only).
Read [hooks.md](hooks.md) §3 first: it is why this differs from the issue's plan.

## Why not just `groupBy`

The issue planned to write `attributes.handbook.groupBy` and let the handbook group variants.
In 1.22.7 `groupBy` never shortens the list: it is only read by `SlideshowItemstackTextComponent`
(the cycling icons in detail-page sections such as "Created by"). The list shows every page whose
`IsDuplicate` is false, and vanilla's grouped page class is dead code.

The spike suggested marking members with the **attribute** `handbook.isDuplicate`. That has a
side effect found while writing this: in the slideshow constructor, when the head stack has a
`groupBy`, *every* other stack whose collectible has `isDuplicate` is removed from the section and
not shown, whether it matches the pattern or not. With thousands of members marked, sections like
"Ingredient for" would silently lose most variants. So members are **not** marked through the
attribute; only their page objects are (below).

## What is written, and when

All on the client; the server and anything that syncs is untouched.

1. **Client `AssetsFinalize`** (`TidyVariantsModSystem.Resolved`, before the `LevelFinalize` event
   where `ModSystemSurvivalHandbook` gathers its stacks). `HandbookAttributes` writes, copy-on-write
   (the collectible gets a fresh `JsonObject`, so a tree shared between collectibles is never
   touched):
   - `handbook.groupBy = [pattern]` on every collectible of a group that has an exact pattern
     (`Handbook.Build`), **replacing** whatever the mod shipped, so one source wins and one pattern
     per item avoids VintageStory-Issues#7476. Groups with no exact pattern (`groupby-inexact`,
     `groupby-conflict`) keep whatever was shipped.
   - `handbook.exclude = true` on collectibles all of whose creative entries are hidden.
   - Patterns are verified with `verifyAcrossKinds: true`: the slideshow matches a pattern against
     every stack's code, blocks and items alike (`WildcardUtil.Match` on the code only), so a
     pattern must not catch a same-code collectible of the other kind. Page codes do include the
     class (`block-…`/`item-…`), which matters only for the list part below.
2. **After the handbook has built its pages** (`GuiDialogHandbook.LoadPages_Async` on the thread
   pool, right after `LevelFinalize`; and again after every rebuild, which happens on hotkey changes
   and `.debug reloadhandbook`). A 50 ms client tick watches the dialog's `loadingPagesAsync` and
   page list; when a new set is ready, `HandbookLayout.Collapse` decides and the system applies:
   - **One list entry per group.** The representative's page (the engine's choice, else the first
     member whose page qualifies) stays listed; every other member page gets its private
     `GuiHandbookItemStackPage.isDuplicate` field set, so `FilterItems` (vanilla's and Dovidarium's,
     both read `page.IsDuplicate` live) skips it. This is per **page**, so attribute-stack groups
     (clutter, shields) collapse too, and the attribute stays false, so slideshows are unaffected.
   - **Hidden variants:** their pages (where a mod's `GetHandBookStacks` lists them despite
     `exclude`) leave the list the same way.
   - **Nothing becomes unreachable.** A page leaves the list only if every entry with that page code
     is hidden or a non-representative member of a collapsed group, and it is no group's
     representative. A group collapses only if a member page can represent it: listed, plain
     `GuiHandbookItemStackPage`, its collectible has vanilla's exact
     `CollectibleBehaviorHandbookTextAndExtraInfo`, and no other `ICustomHandbookPageContent` (the
     interface lookup returns the first match, so ours must be the one). Members the handbook has no
     page for (a mod's own `GetHandBookStacks`) are ignored; a group with fewer than two pages is
     left alone.
   - **Variants section.** Every member page of a collapsed group (the representative's included)
     ends with "*Title*: N variants" and one clickable icon per member page, which opens that page.
     It is a client-only `CollectibleBehavior` implementing `ICustomHandbookPageContent`, which
     vanilla's `GetHandbookInfo` calls last; it looks the page up by page code, so one collectible
     with stacks in several groups shows the right group on each page.
   - **Member pages stay openable**: a duplicate page is still in `allHandbookPages` and
     `pageNumberByPageCode` (verified: `LoadPages_Async` indexes every page,
     `OpenDetailPageFor` doesn't look at `IsDuplicate`), so recipe links, `handbook://` links,
     the variants section and Shift+H on a held/looked-at member all open the member's own page.
   - **Title.** The group's lang title if it has a translation (`TidyGroup.Title`), else the
     representative's name. The representative's list entry is drawn as "*Title* (N)": the list
     draws a lazily made name texture (`Texture`), and once the page has made one the tick swaps
     ours in (it watches only while the handbook is open). The detail page itself keeps the
     representative's own name as its heading.
   - **Search.** The representative page's search text (`TextCacheAll`) gets every member's name
     appended, and with a lang title, its title (`TextCacheTitle`) becomes the group title. So a
     member's name finds its group's page (ranked as a text match, below title matches). Member
     pages themselves no longer appear in search results.

If a game field read by reflection (`ModSystemSurvivalHandbook.dialog`,
`GuiDialogHandbook.allHandbookPages`/`loadingPagesAsync`, `GuiHandbookItemStackPage.isDuplicate`)
is missing, the list stays vanilla (groupBy is still written). An exception while applying undoes
the page flags it set and stops the tick. Everything logs under `[tidyvariants] handbook:`.

No Harmony patch. Nothing touches the methods Dovidarium guards (`LoadPages_Async`,
`OnNewScrollbarvalueOverviewPage`, `initOverviewGui`, `OnGuiOpened`, `FilterItems`), so none of its
gates turn off.

## Numbers (whole pack, server-side approximation)

From `TidyVariantsHandbookScenarios` (every creative entry taken as a handbook page, every page able
to represent): 29,449 stack pages → 6,580 listed; 1,449 groups collapse, 22,869 pages leave the
list. `groupBy` on 18,338 collectibles, `exclude` on 2. Pattern exactness across kinds: 83
`groupby-inexact`, 0 `groupby-conflict`, 1,300 patterns (same-kind only would give 16 / 0 / 1,367;
the 67 groups that differ keep their shipped pattern, e.g. vanilla's shared ore pattern). The client
logs its real counts (`stack pages listed A -> B`).

## Dovidarium (0.9.5)

- Its filter cache reads `page.IsDuplicate` per query and resets when pages load
  (`PagesLoadedPostfix`). Our flags are set on the first tick after loading; a search typed in that
  window (tens of ms, normally still the loading screen) could be cached with the members listed
  until the next page reload or cache eviction.
- Its page-text cache holds `GetPageText()` per page, also reset on load; same window.
- Its detail-composer cache skips pages whose collectible has an `ICustomHandbookPageContent`, so
  grouped pages are composed like vanilla each time they open (slower to open, not wrong).
- Category tabs use `PageCodeForStack(page.Stack)`; the representative is a normal stack page, so
  the group sits in its representative's category.

## Not covered

- Members with the representative's page code but different handbook stacks (a mod's
  `GetHandBookStacks` that adds attributes) are matched by page code only; such pages stay listed.
- After a GUI-scale change the list briefly shows the representative's name until the tick swaps
  the label back (only while the handbook is open).
- Groups with no exact pattern keep the shipped `groupBy`, which may still pull in non-members in
  detail sections (#7476).

## Manual in-game checklist

None of the GUI behaviour above has been run: the graphical client can't be launched here. The
decisions are unit-tested and checked on the whole pack server-side
(`tests/PackTests/TidyVariantsHandbookScenarios.cs`); the reflection targets and call order are
verified against decompiled 1.22.7 only. To check in a client with the pack:

1. `client-main.log` has `[tidyvariants] handbook: groupBy on … collectibles` and, a moment after
   world load, `… groups collapsed …, stack pages listed A -> B` with B well below A, and no
   `[tidyvariants]` errors; Dovidarium's `patch-status … late-audit` shows the same active count as
   without Tidy Variants.
2. Open the handbook (H), "Everything": scroll past doors, chairs, ores; each family shows one entry
   "Title (N)" (e.g. "Native copper ore (…)" style titles from `en.json`), not one per wood/rock.
3. Open such an entry: the page ends with "Title: N variants" and N icons; hovering shows each
   name, clicking opens that variant's page, Back returns.
4. On a member page (from the icons): the same section is there; its recipes and "Created by"
   sections are as without the mod (slideshows cycle variants of the same group).
5. Search a member's name (e.g. "birch door"): the group's entry is in the results; a hidden
   orientation variant (e.g. a `-north` chest) never is.
6. Look at a placed member block and press Shift+H: its own page opens.
7. Click a recipe ingredient that is a member: its page opens.
8. Clutter / shields (attribute-stack groups): one entry per group, the variants section lists
   the stacks, each opens.
9. Change a hotkey (rebuilds the pages) and reopen: still collapsed (log line repeats).
10. Change GUI scale with the handbook open: labels come back as "Title (N)" within a moment.
11. Dovidarium category tabs: grouped entries appear once in their category, search still works.
