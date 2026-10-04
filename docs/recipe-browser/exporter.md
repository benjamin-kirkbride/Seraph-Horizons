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

## Butchery (the Butchering mod)

Not a recipe registry either. The Butchering mod (`butchering`) adds an entity behavior
(`EntityBehaviorButcherable`, code `butcherable`) to the creatures it handles, and the rest is
in its C# classes, which `Recipes/Butchery.cs` finds by name through the class registry and
reads by reflection (the exporter cannot reference the mod). What a player does, from the
decompiled 1.14.3:

1. **Pick up.** Right-click the dead creature with an empty hand: it becomes the behavior's
   `item` (a carcass in state `dead`, `butchering:dead<creature>-...-<texture>-dead`; the
   texture variant follows the entity's texture index, falling back to texture 1). The stack
   remembers the creature's `harvestable` drops, condition and dead-decay block.
2. **Skin.** Put it on a block whose `processesState` is `dead` (the hooks) and hold right
   click with a knife (`ItemKnife`). Drops the item's `skinningRewards` plus the creature's
   harvestable drops whose path starts with one of `SkinningRackExclusives` (`hide-`, `fat`,
   `fleece-`, ...), less the item's `excludeRewards`. The carcass becomes `-1-skinned`.
3. **Bleed.** It stays on the hook for `hoursToBleedOut<Workload>` in-game hours (1, 2 or 4
   for the item's `butcheringWorkLoad` small, medium, large), then becomes `-bledout`. A
   bucket (any `BlockEntityBucket`, up to six blocks below) gets `bloodAmount` of `bloodType`.
4. **Butcher.** On a block whose `processesState` is `bledout` (the tables), with a knife or,
   on a `BlockButcherTable` only, a cleaver: `butcheringRewards` plus every other harvestable
   drop, less `excludeRewards`. The table leaves the creature's dead-decay block (bones).
5. **Or harvest it where it lies** with a knife, as without the mod: the creature's
   harvestable drops, which the mod's Harmony patch on
   `EntityBehaviorHarvestable.dropQuantityMultiplier` cuts (to half by default).

Tool durability per workload is `knifedurabilityloss<workload>` and
`cleaverdurabilityloss<workload>` on the workstation block entity. Workstation drops are
multiplied by the block's `butcheringEfficiency` times the mod's config multiplier
(`SkinningRackLootMultiplier` for hooks, `butcheringTableLootMultiplier` for tables); food,
what smelts into food, and `AnimalWeightDoesApply` (sinew, offal) also by the creature's
condition. The exporter reads all of these, the constants included, from the loaded mod. The
field harvesting cut is measured: a bare `EntityAgent`, never spawned or initialised, is
given a harvestable behavior and asked for `dropQuantityMultiplier` without and then with the
butchering behavior; the ratio is the cut, whatever the config says.

One record per entity type and carcass item, `butchery|<entity type>|<carcass>` (entity
variants that pick up as the same carcass, such as the deer subspecies of one size, share
it). Its variants are the entity variants that give the same; `butchery.variants` names
them and gives each output's yield (average and spread, before efficiency and condition, or
null). `ingredients` hold the carcass in each state, the stations and the tools, `outputs`
everything any stage gives, and `butchery.stages` says which stage needs and gives which. The
item's "Harvested" sources of these creatures carry the cut, already applied, in
`extra.multiplier`.

Not covered: antlers the skinning hook hands back (`EntityBehaviorAntlerGrowth`'s inventory,
decided at runtime), the player's stats (`butcheringSpeedMul`, `animalLootDropRate`, XSkills),
how long each right click takes, and the mod's smoking rack (`transformsWhenSmoked`, a
separate process, not butchery).

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
- butchery: the whitetail deer's stages, stations, tools, bleed time, blood and rewards from
  the Butchering mod's assets; its harvestable drops split between hook and table and halved
  in the field; every butcherable entity variant in exactly one record, with aligned yields;
- records per type against the definitions counted with the engine's asset loader, and
  variants per type against the sizes of the engine's registries;
- the structural rules of the document, schema validation (JsonSchema.Net, draft
  2020-12), determinism, and the failure path (a registry without a list, and one whose
  class has no recognisable ingredients, both throw and name the registry).

`tools/tests/test_packtool_export.py` covers the packtool side
(`python3 -m unittest discover -s tools/tests`).
