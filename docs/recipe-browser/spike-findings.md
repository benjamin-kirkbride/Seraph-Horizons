# Spike findings: exporting recipes from a dedicated server (#159)

Measured against the 1.22.7 dedicated server and the pack as locked. The engine
assemblies are not obfuscated; file and member names below are from decompiling
`VintagestoryLib.dll`, `VintagestoryAPI.dll` and `Mods/VSSurvivalMod.dll` with ilspycmd.

## 1. What `GridRecipe.FreeRAMServer()` discards, and when to export

```csharp
public virtual void FreeRAMServer() { IngredientPattern = null; Ingredients = null; }
```

It keeps `ResolvedIngredients` (one ingredient per grid cell, each a clone whose `Id` is
the pattern key), `Width`, `Height`, `Shapeless`, `Output`, `Name` and the `RecipeBase`
fields. Only grid recipes are affected: `RecipeRegistryGeneric<T>.FreeRAMServer` calls it
for `GridRecipe` entries and does nothing for other types.

It is called from `ServerMain.RecipesToPacket`, part of `BuildServerAssetsPacket`, which
`StartBuildServerAssetsPacket` queues on the thread pool right after the `WorldReady` run
phase, before worldgen starts and before `RunGame`. `Block.FreeRAMServer` and
`Item.FreeRAMServer` run in the same task; they drop only client-side rendering data
(shapes, textures, transforms). So by the `RunGame` phase the grid data may be gone, and
whether it is gone depends on thread timing. Atlas scenarios run later still, when it is
always gone.

The exporter does not need what is dropped:

- the grid pattern is rebuilt from `ResolvedIngredients` (cell `Id`s, `_` for empty cells),
  which is also exactly what the game matches against;
- the definition-level data that no registered recipe keeps anyway (the wildcard code
  `plank-*` before expansion, its `name`, `allowedVariants`, `skipVariants`, and which entry
  of which file a recipe came from) is read again from the recipe assets.

Wildcard expansion loses the definition for every recipe type, not only grid recipes:
`RecipeBase.GenerateRecipesForAllIngredientCombinations` clones the definition once per
combination, replaces `*` and `{name}` in the clone's codes, and sets `AllowedVariants` and
`SkipVariants` to null. The index of a definition within its file is never stored. So the
assets have to be read again whatever the timing.

Re-reading assets at `RunGame` or later gives the patched data. JSON patches edit
`IAsset.Data` in memory and set `IsPatched`. After "now running",
`AssetManager.UnloadUnpatchedAssets()` drops only the unpatched assets' data, and those
reload unchanged from disk when asked for again. The exporter reads them through
`api.Assets.GetMany`, which reloads what was dropped.

**Decision:** `ExportModSystem` exports at `EnumServerRunPhase.RunGame`. That is after
every registry is final (loaders register during `AssetsLoaded`/`AssetsFinalize`) and before
"Dedicated Server now running" is logged, which is what `packtool.py smoke` waits for. The
exporter reads only data that survives `FreeRAMServer`, so the result does not depend on the
race with the asset packet thread. `Two_exports_are_identical` and the Atlas scenarios, which
run after the free, check this.

## 2. Enumerating recipe registries

Yes. `ICoreAPICommon.RegisterRecipeRegistry<T>(code)` ends in
`GameMain.RegisterRecipeRegistry`, which stores the instance in
`GameMain.recipeRegistries` (`Dictionary<string, RecipeRegistryBase>`, public field on
the internal world class, not on any API interface). `GetRecipeRegistry(code)` only looks
up a code you already know. The exporter finds the dictionary by reflection on the type
of `api.World` (the first instance field assignable to
`IDictionary<string, RecipeRegistryBase>`, found by type, not by name).

Each registry in the pack is a `RecipeRegistryGeneric<T>` with a public `List<T> Recipes`.
The exporter takes the first generic `IList` field or property of the registry, and the
element type from the list. A registry with no such list, or whose element type no reader
can handle, throws `RecipeExportException` naming the registry.

Registries in this pack (22):

| registry | registered by | element type | shape |
|---|---|---|---|
| gridrecipes | engine (GameMain constructor) | GridRecipe | grid |
| smithingrecipes, knappingrecipes, clayformingrecipes | survival (RecipeRegistrySystem) | *Recipe : LayeredVoxelRecipe | voxels |
| barrelrecipes | survival | BarrelRecipe | barrel |
| alloyrecipes | survival | AlloyRecipe (not a RecipeBase) | alloy |
| cookingrecipes | survival | CookingRecipe (not a RecipeBase) | cooking |
| mixingrecipes | aculinaryartillery | CookingRecipe | cooking |
| doughrecipes, simmerrecipes | aculinaryartillery | DoughRecipe, SimmerRecipe (IByteSerializable only) | generic |
| vemetalpressrecipes, velogsplitterrecipes, vesawmillrecipes, veextruderrecipes, vecrusherrecipes, vekilnrecipes, vemixerrecipes, vecreosoteoven, veblastfurnace, vedistillation, vechemplant, vetempforge | vintageengineering | Recipe* : IVEMachineRecipeBase (IByteSerializable only) | generic |

None of the mod recipe classes derive from `RecipeBase`, so the generic reader works by
reflection on public members (see exporter.md).

Which mod registered a registry is not recorded either. The exporter takes the mod whose
mod system holds a reference to the registry or its recipe list, since every registering
system keeps one to use its recipes. Base-game mods win ties, and registries no mod system
holds (`gridrecipes`) belong to `game`.

## 3. Server-side collectible data

- `api.World.Collectibles` holds every registered item and block. Missing ones and ones with
  no code are already filtered out (`GameMain.LoadCollectibles`). `api.World.Items` has
  null entries (unused ids) and must be null-checked. `GetItem`/`GetBlock` return null or a
  block with `IsMissing` for unknown codes; the exporter treats both as "not registered".
- Every code that ends up in an export variant is registered. `Referenced_codes_cover_every_variant_stack`
  checks each code against `GetItem`/`GetBlock`.
- Collectible tags are numeric on the server (`TagSet` of ushort). Names come back
  through `api.CollectibleTagRegistry.SlowEnumerateTagNames`.
- Display names: the dedicated server loads `Lang` itself. `ServerSystemModHandler` calls
  `Lang.Load(logger, AssetManager, Config.ServerLanguage)` after mod assets are in, and the
  server ships `assets/*/lang/*.json`. So `Lang.Get` and `ItemStack.GetName()` should give
  names in the server language (English by default). That comes from reading the code; the
  recipe half exports no names and I did not check real output. ItemSection owns names.

## Other things found on the way

- `mod` of a recipe is the origin of its asset, not its domain. Vanilla recipes are
  domain `game` but come from `assets/survival` (a `PathOrigin` added by the survival mod),
  so their `mod` is `survival`. A mod shipping `assets/game/recipes/...` gets its own id.
  A patch that edits another file does not change that file's `mod`; the engine records no
  per-patch origin.
- Tags-only ingredients (`tags: ["tool-hammer"]`, no code) are common in 1.22. With 10 or
  fewer concrete matches the engine expands them into separate recipes too (these become
  more variants of one record). The schema requires a code, so these export as
  `tag:<required tags>` with the full condition in `extra.tags`.
- `LoadRecipe` sets `Name` to the asset location only when the definition has no `name`.
  Many vanilla smithing and grid recipes set one ("Chisel", "4 sets of nails"), so the
  exporter matches those by explicit name and ingredient/output patterns.
- Inside the Claude sandbox, Harmony patching fails because MonoMod creates
  `/tmp/mm-exhelper.so.*` with a hard-coded `/tmp` (strace: EROFS), which TMPDIR does not
  change. CI is not affected.
