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
| `generic` | none | Every type without a dedicated serialiser |

In a grid pattern each character is the `key` of an ingredient and `_` is an empty cell.

## Rules beyond the schema

- Every recipe `type` is a key of `recipeTypes`, and `count` equals the number of records.
- Every `mod` on an item or recipe is a key of `mods`.
- Every code in a variant is a key of `items`.
- Recipe ids are unique and sorted by UTF-16 code unit (ordinal order).
- `variants[].ingredients` is as long as `ingredients`.
- A grid pattern has `height` rows of `width` characters and uses only keys that exist.

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
