# Recipe browser site

The app in `site/`: Vite, Svelte 5 and TypeScript. It is a static single-page app. The
build uses a relative base and keeps routes in the URL hash (`#/<version>/item/<code>`,
`#/<version>/type/<code>`, `#/<version>/entity/<type>`, `#/<version>/search?q=`,
`#/<version>/values`),
so the same `dist/` works at any sub-path and a reload on a deep link only ever asks
the server for `index.html`.

The model viewer (`#/models`, [models.md](models.md)) is part of the same app but reads no
recipe data, so its routes carry no version.

## Running it locally

Needs Node 22.22 or newer (jsdom, used by the unit tests, requires it).

```sh
npm --prefix site ci

# data for one version, from an export (see README.md for where exports come from)
npm --prefix site run prepare-data -- --export path/to/recipes.json --out site/public/data/main
echo '{"default":"main","versions":[{"id":"main","label":"main"}]}' > site/public/data/versions.json
# optional: cp -r icons site/public/icons

npm --prefix site run dev       # http://localhost:5173/
npm --prefix site run build     # site/dist/
npm --prefix site run check     # svelte-check and tsc, warnings fail it
```

Without `site/public/icons/index.json` every item shows a lettered placeholder.

## Themes

The site has a light and a dark theme. By default it follows the reader's system setting
(`prefers-color-scheme`); the Theme picker in the header pins one or the other. Every
colour token in `site/src/app.css` is a `light-dark()` pair, so the only thing a theme
changes is `color-scheme` on `<html>`: `data-theme="light"` or `"dark"` pins it, and no
attribute means "light dark". The choice is kept in `localStorage` under `theme`
(`site/src/lib/theme.ts`), and a small inline script in `site/index.html` applies it before
first paint so a dark page never flashes light. The icon tile below is the same in both.

## Icon background

Every icon, and every recipe slot, sits on a tile that copies the game's item slot, the
same in both themes. The tokens are in `site/src/app.css`: `--icon-fill` paints it,
from `--icon-bg` (the centre), `--icon-shade` and `--icon-falloff`; `--icon-border` and
`--icon-text` (placeholder letters) go with it.

The game draws the slot in code, not from a texture. `GuiElementItemstackInfo` (the
tooltip and handbook header) fills a square with `GuiStyle.DialogSlotBackColor`
(`ColorSchematic`, `#ffe2c2`), strokes its edge 5 px wide in `DialogSlotFrontColor`
(`ColorWood`, `#845c43`), blurs it three times so the stroke bleeds inwards, and adds a
faint inset emboss. Inventory slots (`GuiElementItemSlotGridBase`) use the same two
colours with a thinner, less blurred stroke, rounded corners and a black outline. The
site follows the tooltip, which is what a player compared it with. The centre (`#faddbd`)
and the falloff are measured from a screenshot of the tooltip rather than taken from the
code: the screenshot's brightest point is a little darker than `#ffe2c2`, and the blur
cannot be copied exactly. Over the centre colour, the site lays `#845c43` at up to 28%
along each axis in two linear gradients, so the corners get it twice, much as the blurred
stroke does. The stops are percentages, so the falloff scales with the tile from 24 px
links to 64 px headers. It is within a few levels of the screenshot at every point.

The tile is light because most of the pack's icons are dark: half of them have a mean
luminance under 0.09, and iron, steel and machinery all but vanished on the dark theme's
surfaces. The game has the same limits as the site: pale items (eggs, bone, white wool,
milk, white dye) are harder to see on it than dark ones. In the dark theme the page frames
the tile the way the game's dark panel does; in the light theme the tile's outline is
`#845c43` so that it still reads as a tile on the near-white page.

## Mod links

Every place that names a mod (item and creature pages, recipe cards, search results, the
creature list and the credits) links it to its ModDB page in a new tab, through
`modLink` in `site/src/lib/credits.ts` and `ModLink.svelte`. The base game's mods (`game`,
`survival`, `creative`) link to vintagestory.at, and the CI-only exporter (`seraphexport`)
is not linked. In credits a mod's own `website`, if it gives one, is a second, smaller
link. In a search or creature-list row the mod link sits beside the row's link, not inside
it, since links cannot nest.

The link is `https://mods.vintagestory.at/show/mod/<assetId>`. The ModDB serves a mod page
at its URL alias, which its author picks, and has no route by modid: in October 2026 the
alias was the modid for only 75 of the pack's 123 mods, and `/<modid>` was a 404 for the
rest, or another mod's page (`/scaffolding` is Simply Scaffolding; ours is `/scafolding`).
The asset id never changes, so `packtool lock` records it from the ModDB API as `assetId`
in `pack/lock.json`, and prepare-data copies it onto each mod in `meta.json` (`--lock`,
by default the repo's `pack/lock.json`). Every version is built with the current lock, so
a mod an old release had but the pack has since dropped has no asset id; it is named but
not linked, since guessing `/<modid>` can land on the wrong mod. The same goes for every
mod when the lock is missing or predates `assetId`, which prepare-data warns about.

## Tests

```sh
npm --prefix site test                                        # unit tests (Vitest)
RECIPE_EXPORT=path/to/recipes.json npm --prefix site run e2e  # end to end (Playwright)
```

The unit tests in `site/test/` cover the logic in `site/src/lib/`: the prepare-data
transform and its reverse indexes, wildcard matching, search ranking, VTML sanitising,
routes, icon paths and recipe layout, and the model viewer's maths, manifest and play
script ([models.md](models.md)). They use `schema/examples/minimal.json` and small
hand-written exports, the bucking sawmill's and the rosser's shipped shapes, rigs and reference
poses, and the machines' driver fixture (`mods-src/seraphhorizons/tests/Machines/driver-fixture.json`).

The end-to-end tests in `site/e2e/` only run against a real export; without
`RECIPE_EXPORT` the run fails. `e2e/serve.ts` builds the site, runs prepare-data on the
export (published twice, as versions `next` and `v0.0.0-e2e`, so the version switcher
can be tested), adds a test icon for `game:ingot-copper` and the real icons committed
under `site/e2e/icons/` (plain files, not LFS, so CI needs no LFS fetch), and serves the result at
`http://127.0.0.1:4317/Seraph-Horizons/`. It
also writes a few fixed item values over the export's (`E2E_VALUES` in `e2e/config.ts`:
copper ingot 2.5, a `floorZero` stick, apple cider 4 per litre, rot without one), and when the export has no values
of its own it gives nine items in ten a made-up one, so `e2e/values.spec.ts` tests the
values page at full size. It adds two variant groups the same way (`E2E_GROUPS`: four
ingots, three of them at one price, and three planks), after taking their members and the
items of `E2E_VALUES` out of the export's own groups, so group rows are tested on any export; the gear's icon is committed under `site/e2e/icons/` too. The same build is served at `/noicons/` with no
`icons/` directory. The tests check facts from the vanilla 1.22.7 assets, and each names
the asset file it relies on. `e2e/icons.spec.ts` screenshots the tile with its contents
hidden, decodes the PNG (`e2e/png.ts`) and compares the centre and corners with colours
sampled from the in-game screenshot, in both colour schemes and at every size an icon is
shown; it checks text and icon contrast with the WCAG formulas in `site/src/lib/contrast.ts`. Output goes to `site/e2e/.work/` (traces and screenshots of
failures in `site/e2e/.work/results/`); with `CI` set there is also an HTML report in
`site/playwright-report/`.

Browsers: `npx --prefix site playwright install --with-deps chromium`.

## Data format

`prepare-data --export <export.json> --out <dir> [--lock <lock.json>]` reads one export at
the current `schemaVersion`, plus the mods' ModDB asset ids from the lock (see Mod links),
and writes:

```
<dir>/meta.json          pack, generator, mods (with ModDB asset ids), recipe types (with their first recipe index), item, recipe and valued-item counts, chunk starts
<dir>/search.json        every item, column-wise and sorted by code, with its value, and the variant groups
<dir>/items/<n>.json     item details and reverse indexes, a few hundred items per file
<dir>/recipes/<n>.json   recipe records as in the export, up to 60 per file
<dir>/entities.json      every creature and trader, column-wise and sorted by code
<dir>/entities/<n>.json  what each entity gives, a few hundred entities per file
```

The app loads `data/versions.json` (written by `tools/site-data`, see README.md) and, for
the version in the URL, `meta.json` and `search.json` up front. Everything else is
fetched when a page needs it. The types are in `site/src/lib/format.ts`.

An item's index in `search.json` is its id everywhere else:

```json
{
  "mods": ["examplemod", "game"],
  "codes": ["examplemod:widget", "game:flint", "..."],
  "names": ["Widget", "Flint", "..."],
  "mod": [0, 1],
  "flags": [0, 1]
}
```

`mod` indexes `mods`. `flags` has bit 1 for handbook-visible, bit 2 for blocks, bit 4
for `floorZero` and bit 8 for `valuePerLitre`, a value per litre (see Item values).

When the export gives any item a `value`, `search.json` also has `value`, one entry per
item: its value in rusty gears (per litre for a liquid, flag 8), or `null`. `valueSwitches` maps an item's index (as a
string) to the config switches its value depends on, for the items that have any, and
`meta.json` has `valueCount`, the number of valued items. An export without values gets
none of the three. A value is one number per row, about a tenth of the file's size before
compression for a pack where nearly every item has one (2.0 to 2.2 MB for 27,000 items
with three-decimal values), and the item page, search and the values page then need no
other file. Item chunks do not repeat it.

When the export has `variantGroups` (Tidy Variants' groups and the handbook's, [schema.md](schema.md)),
`search.json` also has `groups`: `titles`, one title per group, and `members`, each
group's item indices, best representative first, groups in id order:

```json
"groups": { "titles": ["Gravel", "Plank"], "members": [[812, 809, 815], [2210, 2204]] }
```

An item is in at most one group, so the app builds the reverse map itself rather than the
file carrying a column for every item. The group ids are left out: nothing reads them. A
member the export does not have, or one already in an earlier group, is skipped, and a
group left with fewer than two members is dropped (site-data rejects both, but an older
export may predate the check). An export without groups gets no `groups`.

`meta.json` has `itemChunks` and `recipeChunks`, the first index held by each chunk file,
ascending. Item `i` is in `items/<n>.json` for the last `n` whose start is at most `i`,
and the same for recipes. A chunk is `{ "start": <first index>, "items": [...] }` or
`{ "start": ..., "recipes": [...] }`.

Each item entry has, where present:

- `description`, `attributes`, `sources`: as in the export.
- `madeBy`: recipe type to the indices of recipes whose outputs include the item.
- `madeByElsewhere`: recipe type to the number of recipes that make the item but are not
  in its `madeBy` (see below).
- `usedIn`: recipe type to the indices of recipes with an ingredient slot that accepts it.
- `smeltedFrom`: indices of items whose `attributes.smelting.output` is this item;
  `smeltsInto` the reverse.

Recipe indices count the enabled recipes sorted by type, then `id`. Recipes with
`enabled: false` are left out. Each entry of `recipeTypes` in `meta.json` has the type's
`name`, `shape`, `mod` if any, `count` and `start`, the index of its first recipe: sorting
by type first makes a type's recipes the run `start` to `start + count - 1`, so the type's
page needs no index file. An id starts with its type (`<type>|<asset>|<index>`), so this is
the id order except where one type is a prefix of another (`grid2|…` sorts before `grid|…`
by id, since `2` is below `|`).

Transitions over time (records of shape `transition`: drying, curing, perishing, ...) are
recipes like any other, so wet sinew's page lists its curing under "Used in" and dry
sinew's under "Made by". The one exception is perishing into `game:rot`: nearly every food
does it (about 2,700 records in the pack), and listing them all would bury rot's own
recipes. Those records are indexed on the food's side only, and rot's `madeByElsewhere`
counts them; its page says how many items rot into it and that each one's page shows it.
Perishing into anything else, wine into vinegar or a bush cutting into sticks, is indexed
on both sides (`listedOnSourceOnly` in `prepare.ts`).

A transition card (`TransitionRecipe.svelte`) shows what turns, any station it needs
(marked "station": the smoking rack, whose page then lists it under "Used in"), and when it
turns:
the fresh hours plus the transition hours, as minutes, hours or days of game time, with the
range a stack can draw when the export gives a spread, then both parts separately when
both take time, and the stack size factor when the ratio is not 1. Without a station, a `Hint` on the
time says it is the rate in an ordinary inventory: containers change it (a cellar slows
spoiling), and a stack that has started to spoil no longer dries or cures (the engine stops
every later transition once `Perish` has begun).

The gear chain's shapes ([schema.md](schema.md#the-gear-chain)) have cards of their own. A
tub card (`TubRecipe.svelte`) lists the batch, the liquid and the tub by role, then the time
to done, the batch size and litres, and how gears are lost: the grace and the rate at which a
liquid eats a batch left too long, or each gear's chance to come out lost, with the item a
lost gear becomes; the card's heading shows only what the batch becomes (`cardOutputs` leaves
the failure out). A lottery card (`LotteryRecipe.svelte`) shows the item, when it is decided,
and each outcome with its chance, likeliest first. A machine card (`MachineRecipe.svelte`,
the gear cutter) notes each ingredient's part: the master as kept, never consumed; the cutter
kit as a tool with its wear per job and how the oil divides it; the oil; the machine itself;
then the shaft turns a job takes and the oil it drains. The text is built by `tubLines`,
`lotteryOutcomes`, `machineLines` and `machineRole` in `recipe-view.ts`. A record whose block
is missing gets the generic card.

The reverse indexes are built from each recipe's resolved `variants` and, in case an
export does not list every variant, by matching each ingredient's code pattern against
every item code: `*` matches any run of characters, a path starting with `@` is a regular
expression, and `allowedVariants` and `skipVariants` filter the value of the first `*`.
So a recipe asking for `game:plank-*` is a use of `game:plank-birch`. An output with a
`{name}` placeholder is only matched this way when the recipe has no variants, limited to
the values its named ingredient allows.

### Item values

The pack prices every item in rusty gears (`game:gear-rusty` = 1, three decimals), a
table its own mod ships; the exporter writes it as `value` on each item of the export
([schema.md](schema.md)). The site shows the value for the pack's default config; the
config switches a value depends on (`valueSwitches`) are only named, in the tooltip of the
value on the values page. A value is the gear's icon followed by the number, with "rusty
gears" for screen readers only (`GearValue.svelte`, `formatGears` in
`site/src/lib/values.ts`). A `floorZero` item, worth under a gear for a full stack, which
traders treat as worthless, still shows its number, dimmed, with the reason on hover.
A liquid is priced per litre (the export's `valuePerLitre`, flag 8 in `search.json`): its
number is followed by a muted "/ L", and the screen reader word and the hover say "rusty
gears per litre". The values page's intro says liquids are priced per litre. Sorting by
value, in search and on the values page, compares the numbers as shown, a litre against an
item, and a liquid's unit keeps its variants apart from per-item ones in a group row.

- Item page: the value sits beside the item's name in the header, or "No trade value" when
  the item has none. With an export that has no values at all the header says nothing.
- Search: each result row shows its value, and an Order menu sorts the results by value,
  lowest or highest first, as well as best match. The order is in the address
  (`#/<version>/search?q=gear&sort=value-desc`) and a new query keeps it. Sorting by value
  sorts every match, not just the best hundred, then shows the first hundred; items
  without a value come last either way, and equal values keep their ranking.
- Values page (`#/<version>/values`, linked from the header and the start page when the
  version has values): the whole table, a row per valued item with its icon and name
  (linking to its page), its mod (the item's `mod`, linked as in Mod links) and its
  value. A click on a column header sorts by it and a second click turns it round; value
  starts highest first, item and mod start at A, ties go by name. The filter box keeps the
  rows whose name, code, mod id and mod name contain every word typed. A checkbox adds
  the items without a value, which sort last whichever way the value column is sorted.
  A segmented control (radio buttons, so arrow keys move along it) keeps one kind: All,
  Items, Blocks or Liquids (liquids are the per-litre rows, flag 8; blocks flag 2; items
  everything else). Two selects show the worthless rows (`floorZero`, flag 4) and the rows
  not in the handbook (flag 1 off) with the rest, alone, or not at all. A row of several
  items is worthless when they are (it is part of their price), not in the handbook when
  none of them is (Tidy Variants hides a block's other orientations, so the canonical one
  decides), and of a kind when any item is. All of it, the filter text and the order too, is
  in the address (`#/<version>/values?q=cider&sort=name-asc&unvalued=1&kind=liquids&worthless=hide&unlisted=only`,
  defaults left out; `ValuesView` in `route.ts`), so a link or a reload keeps it; a change
  replaces the history entry, as the search page's order does.

Items the reader couldn't tell apart share a row, in two ways. Variants the game shows as
one creative-menu tile or one handbook page (Tidy Variants, and the handbook's `groupBy` for
items it never sees, such as juices; `search.json`'s `groups`) share a row when they share a
price, as gravels of every rock or planks of every wood do.
`valueRows` (`values.ts`) splits each group by price: value, `floorZero` and `valueSwitches`
all have to agree, and members with no value make one row of their own, shown with the
unvalued items. A part of two or more members is one row: the icon of its best-ranked
member, the group's title, a button that says how many variants the row holds ("12
variants", or "12 of 14 variants" when the group is split across prices) and opens the
list of them inline (name and code, linking to their pages), the mod or the distinct mods of
the members, and the value. A member alone at its price is a row of its own and does not
carry the group's title. Then look-alikes fold together: a block's orientations and states
(`mpegearbox:gearbox14-down`, `-east`, `-north`, ...) would otherwise fill pages with
copies. Items fold when they have the same name, mod, value (or both none), floor-zero
flag and value switches, and the same code up to the first `-` after the domain
(`codeBase`). The code part keeps apart different blocks that happen to share a name and
price (`game:carcass-large` and `game:drycarcass-large`, Primitive Survival's grown and
placed tree hollows); only the first part is used because variant parts sit anywhere
after it (a door's stone is mid-code). An item whose name is still a lang key
(`game:clutter-`) is never folded, since the name says nothing. A look-alike of a group
row's member joins that row: Tidy Variants hides a block's other orientations, so only
the canonical one is in its group, and the rest belong with it. A folded row of its own
links to its lowest code and opens like a group row. The tooltip of the button lists the
row's codes (the first twenty, then how many more: a termite mound has 148). A group row
sorts by its title, a folded row by its name, and either by its mods' names joined; a row
matches the filter when the title together with one item's name, code, mod id and mod name
has every word typed. The count line gives rows and items ("1,234 rows, 25,112 items")
once any row holds more than one. On the 27,000-item export the 21,900 valued items make
about 6,700 rows.

The values page reads `search.json`, which the app has loaded anyway, instead of a file of
its own. `ValueTable` (`values.ts`) builds the rows and what the filter searches when the
page opens (about 150 ms in Node for 27,163 items in 983 groups), and each column's order
once, when first sorted on (about 50 ms for all three), and a query walks that order
keeping the rows that match, 2 to 35 ms depending on how many pass. The page shows 100
rows at a time with a pager, so the DOM never holds more than a page of icons and links,
and it filters as the reader types without a debounce. Sort, filter and page are the
page's own state, not in the address.

### Recipe type pages

Each type on the start page links to its page, `#/<version>/type/<code>` (a type code
needs no domain: `grid`, `aculinaryartillery:simmer`). It shows the type's name, mod and
count, then its recipes 24 at a time (`TYPE_PAGE_SIZE`), the later pages at
`?page=<n>`. A page reads its 24 indices from the type's run and fetches only the recipe
chunks that hold them, usually one or two (a chunk holds up to 60), so the crafting grid's thousands
cost no more than a small type. The pager links the first and last pages and the two
either side of the current one (`pageLinks`). A page number past the end shows the last
page. A code no type of the version has says so, as an unknown item does; the version
switcher keeps the type and goes back to its first page, since another version can have
fewer.

### Creatures and traders

The export has no list of entities. prepare-data turns the item `sources` of type
`entityDrop`, `traderSells` and `traderBuys` around: every code named in their `from` is
an entity, and each such source becomes a row of that entity, with `from` and `fromName`
replaced by `item`, the item's index. An entity that gives nothing the export lists is
therefore missing, and so is what it gives that is not an item of the export.

A trade marked `extra.replaced` is left out everywhere (`shownSources` in `prepare.ts`), on
the item's page and as a row of its trader: a trader the pack's trader grid replaces, never
met in its worlds, which the pack's handbook leaves out too (seraphhorizons README,
"Traders"). A trader with only such trades is not listed at all.

Entities are grouped by type: the entity type file whose variant the entity is
(`game:wolf` for `game:wolf-eurasian-adult-male`), which the exporter writes as
`extra.entityType` on each source. An export from before that field makes each entity a
type of its own. The game has no names for types, so `entityTypeName` in `prepare.ts`
takes the words all variant names start or end with ("Wolf", "Drifter", "Trader"), or
failing that the code ("Fish (saltwater)").

```json
{ "codes": ["game:wolf"], "names": ["Wolf"], "mod": ["game"],
  "variantNames": [["Wolf (female)", "Wolf (male)", "Wolf pup (female)", "Wolf pup (male)"]],
  "drops": [7], "trades": [0] }
```

`mod` is the mod with the code's domain as its id or in its `domains`; failing that, the
domain itself. `recipes` lists, per type, the indices of its `butchery` records (the
Butchering mod); the creatures those name are variants of the type even when no item source
names them. `drops` and `trades` count distinct items, so the list page
(`#/<version>/entities`) needs no chunk. `meta.json` has `entityCount` and `entityChunks`,
read like `itemChunks`; a chunk is `{ "start": ..., "entities": [[{ "code", "name",
"sources": [row, ...] }, ...], ...] }`, each type's variants sorted by code.

The type page (`#/<version>/entity/<type>`) shows every variant merged by default: one row
per item and way of getting it, with the range of quantities and how many variants give
it. Variants that give exactly the same are one chip (male and female traders of one kind
in every climate are one), and `?variant=<code>` shows that chip's own rows. Rows are split
into dropped on death (no `note`), harvested (`note` "Harvested"), one section per other
behavior note, sells and buys. A type with butchery records ends with a Butchery section of
their cards; with a chip picked it shows only the records, and in each only the variants,
that cover the chip's creatures.

A butchery card is headed by the carcass rather than its outputs, names the creatures of the
variant shown (each a link to its chip), and lists the stages in order, each with what it
needs (carcass, station with its yield range, tool with its durability cost, "or" between
options, optional ones apart) and what it gives in that variant, with average ± spread and
what scales it. Field harvesting comes last as the alternative. Item pages link every creature and trader source to its
type's page on its variant.

Each slot on the card keeps its notes (durability, yield range, spread, what it needs, what
scales it) under its name, beside the icon, through `Slot`'s children: after the name they
wrapped under the icon whenever a longer name cycled in. The generic card does the same
for a slot's role. "Optional" heads the optional slots on its own line. "times the
station's yield" and "times the creature's condition" are `Hint`s: the first lists the
stations of that output's stage with their multipliers (hooks for skinning, tables for
butchering), the second the creature's condition range from `butchery.condition`. A `Hint`
is a button styled as dotted text whose explanation shows on hover, focus or tap, is the
button's description for a screen reader, and is fixed to the window so a table that
scrolls sideways does not clip it; the item page's density and panning chance hints use
it too.

On a type with butchery records that cover every harvested creature shown, the "Harvested
from the body" section is a table built by `butcheryYields` (`entity-view.ts`) instead of
the plain list: per item, what harvesting where it lies gives and what the hook and table
give, each at the lowest and highest condition (only the outputs scaled by condition
differ). The full process is the sum of every stage but the harvest, without the carcass
in its states, at a station yield of ×1; the note under the table gives the hooks' and
tables' ranges, the mod's cut and the condition range. Cells are averages (the cards give
the spread). With several butchery variants shown (all variants, or a chip that covers
more than one) a cell is the range of their averages, counting only the variants that give
the item. An export without `butchery.condition` gets one column per way. The table has
a fixed layout so it fits a 360 px screen without scrolling sideways.

### Other sources on an item page

An item page lists its `sources` in one table; `sourceRows` in `site/src/lib/recipe-view.ts`
decides the rows. Sources that would read the same are one row: a block's orientations
all drop the item under one name. Sources of type `other` also fold the variants of one
block type (the code up to the first `-` of its path, so `game:richgravel-granite` is a
`game:richgravel`) into one row. The row names the first block and lists the others under
"and N more". Block drops are not folded, so flint still has a row for each rock's flint
ore.

A source of type `other` with a note the site knows (`Panned`, `Harvested`, in
`t.sourceNotes`) shows the note as its kind ("Panning", "Harvested") instead of "Other".

Harvested sources fold only when quantity and `extra` are equal. Panned sources fold when
everything but the chance is equal, `extra` included, so a stat-scaled drop never merges
with a plain one. The chance shown is `extra.chancePerPan`, as a percentage with two
significant digits below 1% (`0.0001382` is "0.014%"): a pan gives at most one item, so
it is lower than the declared chance in `quantity.avg`, which is only used when an export
has no `chancePerPan`. A row whose blocks differ shows the range ("4.9–6.98%" for flint
from rich gravel, which depends on the rock), and each block in its list shows its own.
A block that lists the item twice (each gravel lists its stone twice) is one source with
the chances added. So flint has three panning rows (gravel, sand, rich gravel) instead of 114. `extra.stat`
is named in the last column; the rest of `extra` (`attributes`) is not shown.

A block's own page has the same sources turned around, under "Gives": `prepareData` copies
every source that is not a creature's or trader's to `gives` of the item its `from` names,
with `item` in place of `from` (an `ItemDetail` field, like `smeltsInto`). `giveRows`
lists what breaking and harvesting give first, then what panning gives, likeliest first,
with an item the block's list holds twice added up. A block that is not an item of the
export (wavy sand) has no page, so nothing shows it.

`DATA_FORMAT` in `format.ts` is written to `meta.json` as `format`. The app and the data
are always built together, so there is no migration between formats. Format 2 added
`start` to the recipe types and sorted recipes by type first. Format 3 added item values:
`value`, `valueSwitches` and the `floorZero` flag in `search.json`, `valueCount` in
`meta.json`. Format 4 added `groups` to `search.json`.
