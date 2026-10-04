# The export format

One export is one JSON document: everything the pack's server knows about items and
recipes at one commit. The format is defined by
[`schema/recipe-export.schema.json`](../../schema/recipe-export.schema.json) (JSON Schema,
draft 2020-12). [`schema/examples/minimal.json`](../../schema/examples/minimal.json) is a
small export written by hand that shows every shape.

CI validates each export with `tools/site-data` (`validate`), which also checks the rules
below that JSON Schema cannot express.

## Top level

| Field | Content |
|---|---|
| `schemaVersion` | Integer. Bumped on any change a reader could notice. |
| `generator` | Name and version of the export mod. |
| `pack` | Pack id, pack version and game version. |
| `mods` | Every loaded mod, keyed by mod id. The base game's own mods are included. |
| `items` | Items and blocks, keyed by full code. |
| `recipes` | One record per recipe definition, sorted by `id`. |
| `recipeTypes` | One entry per recipe type, keyed by type code. |
| `guides` | Handbook guide pages that are not tied to one item. |

## Codes

A code is always `domain:path`, lower case, with the domain present: `game:ingot-copper`.
Codes stay in the data everywhere, next to English names, so other languages can be added
later without a new export format.

## Items

`items` holds every collectible that is visible in the handbook, plus everything a recipe
references. The second group has `handbookVisible: false` when the handbook hides it.

An attribute that does not apply is left out. A stick has no `toolTier`.

`sources` lists ways to get the item other than a recipe: block drops, entity drops and
trader stock. Much of this is driven by code in the game, so the list is best effort.

## Recipes

A recipe record describes the **definition**, as the mod author wrote it, and lists the
resolved forms under `variants`.

- `ingredients` and `outputs` are the definition. Codes here may contain `*` wildcards and
  `{name}` placeholders.
- `variants` are concrete. Each has one list of accepted stacks per ingredient, in the
  same order as `ingredients`, and concrete outputs. `bindings` gives the wildcard values
  of that variant, for example `{"wood": "oak"}`.
- A recipe without wildcards has exactly one variant.

`id` is `<type>|<source asset>|<index in that asset>`. It is unique, and stable between
two exports of the same pack, so versions can be compared.

`mod` is the mod whose asset defines the recipe, which is not always the mod that owns
the output item.

### Type-specific data

`recipeTypes[type].shape` says which block a record of that type carries:

| Shape | Block | Used by |
|---|---|---|
| `grid` | `grid`: width, height, shapeless, pattern | Crafting grid |
| `voxels` | `voxels`: layers of rows, `#` filled and `_` empty | Smithing, knapping, clayforming |
| `barrel` | `barrel`: sealing time | Barrel |
| `alloy` | `alloy`; ratios are on the ingredients | Alloying |
| `cooking` | `cooking`; slot quantities are on the ingredients | Cooking |
| `construction` | `construction`: stages, each listing the ingredients it consumes | Blocks built in place |
| `butchery` | `butchery`: stages with what each needs and gives, yields per variant | The Butchering mod |
| `generic` | none | Every type without a dedicated serialiser |

In a grid pattern each character is the `key` of an ingredient and `_` is an empty cell.

### Blocks built in place

Some blocks are not crafted whole: the player places the block, then right-clicks it with
the items of each stage in their hotbar until it is complete. The water wheel works this
way, and so do ppex's pump, boilers and engines. These are records of type `construction`.
Their `outputs` is the block, `ingredients` lists every stack a stage consumes, and
`construction.stages` says which stage consumes which ingredient. The first stage is the
block as placed and consumes nothing. A stage whose right-click is not "Construct" (the
water wheel's last stage, "Launch") has an `action`.

A stage can remember the variant of what it consumed (`extra.storeWildCard` on the
ingredient) and a later stage can ask for the same variant with a `{name}` placeholder:
the water wheel's planks are of the wood of its support beams. Each value is a variant with
a binding, as for recipes.

### Butchery

With the Butchering mod a dead creature is picked up whole, skinned on a hook, left there to
bleed out, and butchered on a table; or it is harvested where it lies for less. Records of
type `butchery` are one creature type and one carcass item. `ingredients` are the carcass in
each state (role `carcass`), the stations (role `station`, with each block's yield
multiplier in `extra.efficiency`) and the tools; `outputs` are everything any stage gives.
`butchery.stages` lists, per `step` (`pickUp`, `skin`, `bleed`, `butcher`, `harvest`), the
ingredients it needs, `options` it needs one of (knife or cleaver), `optional` ones (a
bucket), `hours` it takes and the outputs it gives. `harvest` is the alternative to the
others, with the mod's cut in `multiplier`, already applied.

Variants are the creatures that give the same. `butchery.variants`, aligned with
`variants`, names them (`entities`) and gives each output's `yields`: average and spread,
or null when that variant does not give it. A variant's `outputs` stacks are the outputs it
yields, with the average as quantity. An output's `extra.scaledBy` says what multiplies it
in game (`efficiency`, `condition`), `extra.needs` the optional ingredients it needs, and
`extra.alternatives` other items it may be instead (carcasses of another coat).
`butchery.condition` is the range of the creature's condition (`min`, `max`; the game's
`animalWeight`), which multiplies the outputs scaled by `condition`.

The `butchery` shape and block are optional additions, so `schemaVersion` stayed 1; so is
`condition`, which older exports lack.

## Rules beyond the schema

- Every recipe `type` is a key of `recipeTypes`, and `count` equals the number of records.
- Every `mod` on an item or recipe is a key of `mods`.
- Every code in a variant is a key of `items`.
- Recipe ids are unique and sorted by UTF-16 code unit (ordinal order).
- `variants[].ingredients` is as long as `ingredients`.
- A grid pattern has `height` rows of `width` characters and uses only keys that exist.
- Each ingredient of a `construction` record is consumed by exactly one stage.
- Each ingredient and each output of a `butchery` record belongs to exactly one stage;
  `butchery.variants` is as long as `variants`, each one's `yields` as long as `outputs`, and
  a variant's output stacks are the outputs it yields (with their alternatives).

## `extra`

Most records have an optional `extra` object for data that has no field yet. Readers
ignore what they do not know. A field that proves useful moves out of `extra` into the
schema with a version bump.

## Later uses

The format is meant to serve a progression graph, a calculator and diffs between pack
versions as well as lookup. That is why recipes have stable ids, quantities are numbers
and not display text, every ingredient and output is a code that is a key of `items`, and
tools are marked as not consumed.

## Changing the format

See [deploy.md](deploy.md) for how to bump `schemaVersion` and add a migration.
