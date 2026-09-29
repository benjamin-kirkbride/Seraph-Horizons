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
can be tested), adds an icon for `game:ingot-copper` only, and serves the result at
`http://127.0.0.1:4317/Seraph-Horizons/`. The same build is served at `/noicons/` with no
`icons/` directory. The tests check facts from the vanilla 1.22.7 assets, and each names
the asset file it relies on. Output goes to `site/e2e/.work/` (traces and screenshots of
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

`DATA_FORMAT` in `format.ts` is written to `meta.json` as `format`. The app and the data
are always built together, so there is no migration between formats.
