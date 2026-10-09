# Multiblock viewer

The site's multiblock viewer (`#/<version>/multiblocks`, `#/<version>/multiblock/<id>`) shows the
structures a player builds **by placing blocks by hand**, which a block then checks against a
layout: Steelmaking Expanded's blast furnace, cowper stove, Bessemer converter and smoke stack,
Pipes and Power Expanded's Cornish and Lancashire boilers, and the base game's beehive kiln and
cementation furnace (stone coffin). Each block is drawn in its real shape (its shape file's
elements: stairs, hatches, pipes), one flat colour per kind of block, with the kinds listed as a
legend that is also the bill of materials. The structure can be sliced layer by layer.

It is a sibling of the [model viewer](models.md) (`#/models`), which shows the pack's own machines
that are placed as one item and fill their own ghost cells (the bucking sawmill, the rosser, the
gear cutter). Those are not multiblocks in this sense and are not listed here.

## Where the data comes from

The layouts and the shapes of the blocks belong to third-party mods whose files the pack may not
re-host (`redistribute = false` for smex, ppex and exlib in `pack/pack.toml`), so nothing is
copied into the repository. They come from the **recipe export** instead: the CI exporter runs in
a server with every mod loaded and writes, per structure, the cells and the shape of a block that
fits each cell, the same way it writes the names and recipes of those mods' items. A version's
page shows that version's structures, like every other page under a version.

Every layout is read the one way the game itself stores them: the `multiblockStructure` attribute
on the block that checks it (`blockNumbers`, code patterns numbered, and `offsets`, cells with a
number). The game's `MultiblockStructure` reads it for the kiln and the stone coffin; exlib's
`BlockEntityMultiblockStructure` reads it for every smex and ppex structure, and resolves exlib's
ASCII `multiblockLayout` and its C# layout builders into it while the assets load. So the
exporter needs no code per mod: any block whose resolved attributes carry a `multiblockStructure`
is a structure (`tools/recipe-export/MultiblockSection.cs`). Variants that carry the same layout
(facings, tiers) are one structure.

Considered and not done: reading the layouts from the mod zips at site build time (re-hosting
their content in the published site from files the build would have to fetch, and no resolved
shapes), or decompiling them (exlib also builds layouts in code).

### A structure that comes in sizes

A structure whose layout varies (the pack's crucible furnace: one to four melting holes, a chimney
of at least six blocks) declares its sizes on the checking block, next to or instead of
`multiblockStructure`:

```json
"multiblockSizes": {
  "label": "Melting holes",
  "default": 1,
  "sizes": [
    { "label": "1 hole", "multiblockStructure": { "blockNumbers": { ... }, "offsets": [ ... ] } },
    { "label": "2 holes", "multiblockStructure": { ... } }
  ]
}
```

The page then has a size picker, starting at `default` (an index). A chimney "at least N" is
shown at N. Written in the blocktype JSON, or into the block's attributes in code before the
server runs (as exlib does); either way the exporter reads the runtime attributes. **Adding a
structure is that one attribute; nothing in the exporter or the site changes.**

## The export

`multiblocks`, an optional top-level object of the export (an additive change: `schemaVersion`
stayed 1, and older exports simply have none; [schema.md](schema.md#multiblocks)):

```json
"multiblocks": {
  "structures": [
    {
      "id": "smex:blastfurnacedoor",
      "code": "smex:blastfurnacedoor-tier1",
      "name": "Blast Furnace Door (Refractory Tier 1)",
      "mod": "smex",
      "codes": ["smex:blastfurnacedoor-tier1", "smex:blastfurnacedoor-tier2", "smex:blastfurnacedoor-tier3"],
      "parts": [
        { "pattern": "game:refractorybricks-good-tier*", "block": "game:refractorybricks-good-tier1", "name": "Refractory bricks (Tier 1)", "accepts": ["..."] },
        { "pattern": "game:@(air|coalpile)", "air": true, "block": "game:coalpile", "name": "Pile of coal", "accepts": ["game:coalpile"] },
        { "pattern": "game:air", "air": true }
      ],
      "sizes": [{ "cells": [[1, -3, 0, 0], [0, 0, 0, 2]] }]
    }
  ],
  "shapes": {
    "game:refractorybricks-good-tier1": { "draw": "cube" },
    "game:multiblock-monolithic-0-p1-0": { "draw": "none" },
    "smex:hopperbell": { "draw": "shape", "shapes": [{ "elements": [ ... ], "rotateY": 90 }] }
  }
}
```

- **id**: the checking block's domain and first code part, the page's address.
- **cells**: `[x, y, z, part]`, blocks from the checking block (x east, y up, z south), in the
  layout's own frame, as written: the game turns the layout to the way the block was placed.
- **parts**: one per code pattern. `block` is the block a cell is drawn with: a north-facing
  variant if there is one, else the first in registry order, preferring a block another cell of
  the structure is already drawn with (the smoke stack's "any brick" cells take its refractory
  bricks). `accepts` lists up to 24 blocks that fit, `acceptsMore` counts the rest. `air` marks a
  cell that may be empty; without `block`, it must be.
- **shapes**: `cube` for a block drawn as a full cube, `none` for one with nothing to draw (the
  invisible top half of a two-block door, exlib's structure filler), `shape` with the block's
  shape and overlays: their elements as the shape file has them (voxels, children relative to
  their parent's `from`, rotations about `rotationOrigin`), trimmed to what places a box, and the
  block's own `rotateX/Y/Z`, `offset` and `scale`. A block whose shape will not load is a cube.

The whole section is about 190 kB in the pack's export.

### Limits

- **Facing.** A layout is written facing one way and turned by the game as it is placed; which
  facing a mod writes it in is the mod's own code (smex's cowper stove adds 180° to its intake's
  side, its converter control 180° to its own). The viewer draws the layout as written, each block
  in its north variant where it has one (or the facing the pattern names, such as
  `smex:blastfurnacetap-*-west`), so a block whose facing the structure checks may be drawn facing
  another way. The legend names the exact pattern each cell wants.
- **Connectors.** exlib's `multiblockConnectors` (which faces of a pipe cell must connect) and
  `multiblockFacings` are not exported.
- **What a cell is drawn with** is one block of those that fit: tier 1 refractory bricks stand for
  tiers 1 to 3. The legend's pattern and `accepts` say what else fits.

## The page

- **List** (`#/<version>/multiblocks`, linked from the header and the start page when the version
  has any): every structure, its mod and size.
- **Structure** (`#/<version>/multiblock/<id>`):
  - The 3D view: drag to orbit, right-drag or two fingers to pan, scroll or pinch to zoom; buttons
    for angled, opposite, front (from the south), side (from the west) and top views. A floor grid
    under the lowest layer.
  - Each cell is drawn with its part's block, flat in the part's colour, with box edges (a toggle).
    Cells to leave empty (`air`) and cells that fill themselves (`none`) are dashed-colour
    outlines, which a toggle hides.
  - **Slice**: per axis a slider and an "only this one" box. Layer (y) first: layers 1 to N, or
    layer N alone; then west to east (x) and north to south (z), cutting the structure open from
    the east or the south. "Show everything" undoes them.
  - **Size**: a select, for a structure with sizes.
  - **Materials**: one entry per kind, blocks first, most first, then the cells left empty and those
    that fill themselves. Each shows its colour, how many cells take it (as "shown / all" while a
    slice hides some), its name linked to the item's page, and under it the layout's pattern and
    the blocks that fit. Clicking an entry, or a block in the view, picks that kind out and dims
    the rest (an outlined kind shows even while outlines are off); again, or Esc, shows all.
- Without WebGL the stage says so; the slice and the materials still work, counts included.

## Data step

`prepare-data` (`site/src/lib/prepare.ts`, `multiblockFiles`) writes, per version:

```
<dir>/multiblocks.json      id, name, mod, cell count and size labels of every structure
<dir>/multiblocks/<n>.json  one structure, with the shapes of only the blocks it is drawn with
```

and `meta.json`'s `multiblockCount`, absent when there are none (the header link is hidden).

## Code and tests

| File | |
|---|---|
| `tools/recipe-export/MultiblockSection.cs` | The export: finds layouts, groups variants, resolves patterns with the game's `WildcardUtil`, picks blocks, reads their shapes. |
| `tools/site-data/src/checks.ts` | `checkMultiblocks`: cells name a part, no two cells share a place, a part's block has a shape, ids are unique. |
| `site/src/lib/multiblock-view.ts` | Pure: parts' kinds, colours and counts, each block's boxes (flattened with `rig.ts`'s `flattenShape`, turned as the block turns them), slicing. |
| `site/src/viewer/multiblock-scene.ts` | The three.js scene, loaded only by the structure page. |
| `site/src/components/MultiblocksRoute.svelte`, `MultiblockPage.svelte` | The list and the page, a lazily loaded chunk. |

`tests/PackTests/RecipeExportMultiblockScenarios.cs` holds the export to the layouts in the mods'
files (the blast furnace's 147 cells, its air and coal-pile cells, the smoke stack's 72, the
Cornish boiler's 64, the kiln door's four variants as one) and the shapes (bricks a cube, the
door's top half nothing, the hopper a shape); the export scenarios validate the whole document
against the schema. `site/test/multiblock.test.ts` covers the routes, the data step, the view,
block boxes (rotation about the cell's centre, children, offsets) and slicing on
`schema/examples/minimal.json`'s kiln; `site/e2e/multiblocks.spec.ts` the pages on a real export.
