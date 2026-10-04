# The recipe exporter

`tools/recipe-export` is a server-side mod that dumps the pack's recipes and items to one
JSON file (`schema/recipe-export.schema.json`). It is only used in CI and is never
shipped. This page covers the recipe half (`RecipeSection` and `Recipes/`). The engine
facts behind it are in [spike-findings.md](spike-findings.md).

## Running it

```sh
python3 tools/packtool.py smoke --export build/recipes.json
```

This builds `tools/recipe-export` against `$VINTAGE_STORY` and stages it as a folder mod in
the smoke run's own `Mods` (never in `build/mods`, so it is not part of the lock or of
`assemble`). It then boots the server with `SERAPH_EXPORT_PATH`, `SERAPH_PACK_ID` and
`SERAPH_PACK_VERSION` set, from `pack/lock.json`. The smoke check fails if the file was not
written, is not JSON, or its `recipeTypes` counts disagree with its records. It prints the
recipe count per type, and the same lines go into the job summary. If the exporter throws
(a registry it cannot serialise), it logs `[seraphexport] export failed: ...` and writes
nothing. Smoke then fails and shows that line.

The Atlas scenarios call `Exporter.Build` on their own server instead (`ExportUnderTest`).

## How recipes are exported

1. **Registries.** `Registries.Find` reads the engine's registry dictionary by reflection
   and takes each registry's recipe list and element type. Every registry is exported.
   None is skipped silently: one that cannot be read throws `RecipeExportException` with
   its code. The exceptions are named in `Registries.NotRecipes`: registries a mod
   uses only to send data to clients, such as ConfigKit's `configkit:configs`.
2. **Readers.** Each registered recipe is read into a `RecipeForm`. Dedicated readers
   handle `GridRecipe`, `LayeredVoxelRecipe` (smithing, knapping, clay forming),
   `BarrelRecipe`, `AlloyRecipe` and `CookingRecipe`. Any other `IRecipeBase` uses its
   `RecipeIngredients`/`RecipeOutput`. Everything else goes through a reflective reader
   that looks for public `Ingredients`/`Ingredient`/`Inputs`/`Input` and
   `Output(s)`/`Product(s)`/`Result(s)` members holding ingredients, `JsonItemStack`s or
   `ItemStack`s, or objects with an array of them (one slot with alternatives). If there
   are none, it looks for `CombustibleProperties.SmeltedStack` (ACulinaryArtillery
   simmering). The other simple public values of a mod recipe (power, temperature, ...)
   go to `extra`.
3. **Definitions.** The same reader reads each definition asset of the type, parsed the
   way the loaders do (`JsonUtil.ToObject<T>` with the asset's domain) but not expanded.
   The files are `recipes/<type>/` plus every other file a registered recipe's `Name`
   points at. ACulinaryArtillery loads `simmerrecipes` from `recipes/simmering`.
4. **Grouping.** `Grouper` assigns each registered recipe to the definition it was
   generated from. That is the definition in the file its `Name` names, or with that
   explicit `name`, that it matches: ingredient and output code patterns against its
   concrete codes, plus quantities, attributes, recipe attributes, tag conditions,
   returned stacks, and the grid pattern, voxels or seal time. Many files hold entries that
   differ only in one of these (which oil a bucket must hold, knife against cleaver).
   Matching also yields the `bindings` (`*` of a named wildcard binds the ingredient's
   `name`, `{x}` binds `x`). If several definitions match, it picks the first one at or
   after the last match, since loaders register in definition order. Code mods may add
   alternatives to a slot (hydrateordiedrate's kinds of water), so a registered slot may
   accept more than the definition lists. Definitions are parsed both with the asset's
   domain and with `game` for domain-less codes, because ACulinaryArtillery parses with
   plain Newtonsoft. Registered recipes that match no definition (made by code, such as
   hydrateordiedrate's copies per water kind) are grouped by `Name` and slot layout into
   records `id = <type>|<Name or "code">|r<n>` with `extra.registeredByCode`.
5. **Records.** One record per definition. `ingredients` and `outputs` come from the
   definition (patterns, `wildcardName`, `allowedVariants`, `skipVariants`). There is one
   `variant` per registered recipe, sorted by content for stable output. Each variant slot
   lists the registered stacks it accepts: an unnamed wildcard (`saw-*`), a tag condition or
   a list of cooking stacks is expanded against `api.World.Collectibles` with the rules of
   `CraftingRecipeIngredient.SatisfiesAsIngredient`. Disabled definitions are records with
   `enabled: false` (or `extra.disabledButRegistered` if a loader registered them anyway). Definitions none of whose forms resolved are records with no variants
   and `extra.resolved: false`. A definition that does not parse is logged and left out.

## Blocks built in place

These are not recipes in any registry. The engine's `BEBehaviorRightClickConstructable`
(the block JSON's `entityBehaviors`) builds a placed block up in stages, each consuming
stacks from the hotbar; exlib's `ExRightClickConstructable` subclasses it and only changes
what breaking the block drops. `InPlaceBuilds` finds every block whose block-entity
behaviors include that class or a subclass, and reads the behavior's `stages` from the
registered block, so JSON patches apply, parsed as the behavior parses them.

- Blocks with the same first code part and equal stages are one record (the four sides of
  a pump). Its output is the first of them with a handbook page, and all of them are in
  `extra.members`. The id is `construction|<that block>|<index of the behavior>`.
- Stage 0 is the block as placed. The engine never consumes its `requireStacks`, so
  neither does the export.
- `storeWildCard` remembers the variant of the stack consumed, and later stages fill
  `{name}` placeholders from it. A group some later stage uses is a binding with a variant
  per value that the storing slot accepts, and that slot then only takes the bound value.
  A group stored but never used (ppex stores `metal` on every slot) binds nothing, so each
  slot takes what its own wildcard allows, as in game.
- The ingredient's `name`, a lang code the game shows for wildcard slots, is in
  `extra.name` in English.

`mod` is the mod whose files hold the definition asset (vanilla recipes are `survival`),
and `source` is its asset location. Types from base-game registries are bare (`grid`);
mod registries are `<modid>:<registry code without "recipes">`, e.g.
`aculinaryartillery:simmer`. `recipeTypes` lists every registry found, including empty ones
(`count: 0`).

## Where the schema does not fit (data in `extra`)

- Cooking slots accept several stacks, but an ingredient has one `code`. The record keeps
  one ingredient per slot (`role` is the slot code) with the first valid stack as `code`
  and all of them in `extra.validStacks`. The variant lists every accepted stack.
- Tags-only ingredients have no code. `code` is `tag:<required tags joined by +>` and the
  full condition is in `extra.tags` (`disjunctive`, and `conditions` of `required` and
  `forbidden` tag names).
- Barrel `consumeQuantity`/`consumeLitres`, ingredient `recipeAttributes`, a grid
  `recipeGroup`, cooking `typeName`, `portionSizeLitres`, `cookedStack` and `isFood`, the
  recipe's own `name` and `attributes`, and the machine values of mod recipes are all in
  `extra`.
- Size: every variant lists its accepted stacks in full. With this pack that is about 80 MB
  (80 000 grid variants; doorvariants alone has 10 080 variants per door recipe). A shared
  table of accepted-stack lists, referenced from variants, would shrink it a lot.

## Tests

`tests/PackTests/RecipeExportScenarios.cs` checks, on the full pack:

- known vanilla recipes of every base-game type, with values copied from the asset
  files (patterns, codes, quantities, voxels, wildcard variants and bindings);
- a vanilla recipe changed by a mod's JSON patch (BetterRuins adds cupronickel nails);
- blocks built in place: the water wheel's stages and wood bindings, ppex's pump (exlib's
  subclass of the behavior), and every block with the behavior in exactly one record;
- records per type against the definitions counted with the engine's asset loader, and
  variants per type against the sizes of the engine's registries;
- the structural rules of the document, schema validation (JsonSchema.Net, draft
  2020-12), determinism, and the failure path (a registry without a list, and one whose
  class has no recognisable ingredients, both throw and name the registry).

`tools/tests/test_packtool_export.py` covers the packtool side
(`python3 -m unittest discover -s tools/tests`).
