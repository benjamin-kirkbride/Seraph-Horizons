# Recipe browser site

The app in `site/`: Vite, Svelte 5 and TypeScript. It is a static single-page app. The
build uses a relative base and keeps routes in the URL hash (`#/<version>/item/<code>`),
so the same `dist/` works at any sub-path and a reload on a deep link only ever asks
the server for `index.html`.

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

## Tests

```sh
npm --prefix site test                                        # unit tests (Vitest)
RECIPE_EXPORT=path/to/recipes.json npm --prefix site run e2e  # end to end (Playwright)
```

The unit tests in `site/test/` cover the logic in `site/src/lib/`: the prepare-data
transform and its reverse indexes, wildcard matching, search ranking, VTML sanitising,
routes, icon paths and recipe layout. They use `schema/examples/minimal.json` and small
hand-written exports.

The end-to-end tests in `site/e2e/` only run against a real export; without
`RECIPE_EXPORT` the run fails. `e2e/serve.ts` builds the site, runs prepare-data on the
export (published twice, as versions `next` and `v0.0.0-e2e`, so the version switcher
can be tested), adds a test icon for `game:ingot-copper` and the real icons committed
under `site/e2e/icons/` (plain files, not LFS, so CI needs no LFS fetch), and serves the result at
`http://127.0.0.1:4317/Seraph-Horizons/`. The same build is served at `/noicons/` with no
`icons/` directory. The tests check facts from the vanilla 1.22.7 assets, and each names
the asset file it relies on. `e2e/icons.spec.ts` screenshots the tile with its contents
hidden, decodes the PNG (`e2e/png.ts`) and compares the centre and corners with colours
sampled from the in-game screenshot, in both colour schemes and at every size an icon is
shown; it checks text and icon contrast with the WCAG formulas in `site/src/lib/contrast.ts`. Output goes to `site/e2e/.work/` (traces and screenshots of
failures in `site/e2e/.work/results/`); with `CI` set there is also an HTML report in
`site/playwright-report/`.

Browsers: `npx --prefix site playwright install --with-deps chromium`.

## Data format

`prepare-data --export <export.json> --out <dir>` reads one export at the current
`schemaVersion` and writes:

```
<dir>/meta.json          pack, generator, mods, recipe types, item and recipe counts, chunk starts
<dir>/search.json        every item, column-wise and sorted by code
<dir>/items/<n>.json     item details and reverse indexes, a few hundred items per file
<dir>/recipes/<n>.json   recipe records as in the export, up to 60 per file
<dir>/entities.json      every creature and trader, column-wise and sorted by code
<dir>/entities/<n>.json  what each entity gives, a few hundred entities per file
```

The app loads `data/versions.json` (written by `tools/site-data`, see README.md) and, for
the version in the URL, `meta.json` and `search.json` up front. Everything else is
fetched when an item page needs it. The types are in `site/src/lib/format.ts`.

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

`mod` indexes `mods`. `flags` has bit 1 for handbook-visible and bit 2 for blocks.

`meta.json` has `itemChunks` and `recipeChunks`, the first index held by each chunk file,
ascending. Item `i` is in `items/<n>.json` for the last `n` whose start is at most `i`,
and the same for recipes. A chunk is `{ "start": <first index>, "items": [...] }` or
`{ "start": ..., "recipes": [...] }`.

Each item entry has, where present:

- `description`, `attributes`, `sources`: as in the export.
- `madeBy`: recipe type to the indices of recipes whose outputs include the item.
- `usedIn`: recipe type to the indices of recipes with an ingredient slot that accepts it.
- `smeltedFrom`: indices of items whose `attributes.smelting.output` is this item;
  `smeltsInto` the reverse.

Recipe indices count the enabled recipes sorted by `id`. Recipes with `enabled: false`
are left out.

The reverse indexes are built from each recipe's resolved `variants` and, in case an
export does not list every variant, by matching each ingredient's code pattern against
every item code: `*` matches any run of characters, a path starting with `@` is a regular
expression, and `allowedVariants` and `skipVariants` filter the value of the first `*`.
So a recipe asking for `game:plank-*` is a use of `game:plank-birch`. An output with a
`{name}` placeholder is only matched this way when the recipe has no variants, limited to
the values its named ingredient allows.

### Creatures and traders

The export has no list of entities. prepare-data turns the item `sources` of type
`entityDrop`, `traderSells` and `traderBuys` around: every code named in their `from` is
an entity, and each such source becomes a row of that entity, with `from` and `fromName`
replaced by `item`, the item's index. An entity that gives nothing the export lists is
therefore missing, and so is what it gives that is not an item of the export.

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
domain itself. `drops` and `trades` count distinct items, so the list page
(`#/<version>/entities`) needs no chunk. `meta.json` has `entityCount` and `entityChunks`,
read like `itemChunks`; a chunk is `{ "start": ..., "entities": [[{ "code", "name",
"sources": [row, ...] }, ...], ...] }`, each type's variants sorted by code.

The type page (`#/<version>/entity/<type>`) shows every variant merged by default: one row
per item and way of getting it, with the range of quantities and how many variants give
it. Variants that give exactly the same are one chip (male and female traders of one kind
in every climate are one), and `?variant=<code>` shows that chip's own rows. Rows are split
into dropped on death (no `note`), harvested (`note` "Harvested"), one section per other
behavior note, sells and buys. Item pages link every creature and trader source to its
type's page on its variant.

`DATA_FORMAT` in `format.ts` is written to `meta.json` as `format`. The app and the data
are always built together, so there is no migration between formats.
