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
`assemble`). The pack's own mod goes there the same way: every smoke run builds
`mods-src/seraphhorizons` (Release, its `build/seraphhorizons_<version>.zip`) and loads that zip in
place of any pinned copy of the modid, as `tests/PackTests` does for Atlas, so the export carries
the mod's items and recipes as the tree has them. It then boots the server with `SERAPH_EXPORT_PATH`, `SERAPH_PACK_ID` and
`SERAPH_PACK_VERSION` set, from `pack/lock.json`. The smoke check fails if the file was not
written, is not JSON, or its `recipeTypes` counts disagree with its records. It prints the
recipe count per type, and the same lines go into the job summary. If the exporter throws
(a registry it cannot serialise), it logs `[seraphexport] export failed: ...` and writes
nothing. Smoke then fails and shows that line.

The lock does not carry the pack's own mod (it is released with the pack, never pinned), so
without that build the export would miss its items, its recipes and the schematics it gates
other mods' recipes behind, and the item-values check on it would read the wrong world (#506).
Any mod already staged under its modid (a pinned zip) is left out of the run's copy, so the game
cannot load the pin instead. Smoke fails if the build fails or the mod is not loaded, and its
summary names what the build replaced. There is no flag: every smoke run does this.

The Atlas scenarios call `Exporter.Build` on their own server instead (`ExportUnderTest`).
One that reads only the guide pages calls `Exporter.Guides`, which returns the `guides`
section alone without the cost of a full export.

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
3. **Water copies.** Hydrate or Diedrate (2.5.6, `RecipeGenerator`) copies every
   registered recipe, in every recipe list, that takes `game:waterportion` in a slot or in
   a container's `recipeAttributes.requiresContent`: one copy per kind of its clean water
   (`hydrateordiedrate:waterportion-boiled-natural-clean`, `-boiled-rain-clean`,
   `-fresh-distilled-clean`, `-fresh-rain-clean`, `-fresh-well-clean`), and for
   `game:saltwaterportion` one with `-salt-well-clean`. Every water slot of a copy gets the
   same water, its `Name` becomes `hydrateordiedrate:-HoD-<Name path>-<water path>`, and
   its liquid quantities are resolved again from their litres; nothing else changes. That
   is about 700 recipes (barrel, grid, ACulinaryArtillery simmering), each of which looked
   like its original on the site. `Recipes/WaterClones.cs` folds them before grouping: a
   recipe with such a `Name` is folded into another recipe of the same registry that is
   equal to it in everything but the water (slots in order with their codes, quantities,
   or litres for liquids, attributes and recipe attributes; outputs; the type block),
   where every slot that differs holds `game:waterportion` (or salt water for the salt
   kind) in the original and the copy's one water in the copy. The copy's water is
   appended to the original slot's alternatives, so it shows in the variants; for a
   container slot (grid) the container is already there and the water is only recorded.
   The record carries `extra.waterCopies`: `mod`, the number of copies folded into it
   (`recipes`, over all its variants) and their `water` codes. A copy with no such
   original stays a record of its own. HoD appends its waters to slots that already list
   alternatives (ACulinaryArtillery's kneading) instead of copying, which grouping
   handles.
4. **Definitions.** The same reader reads each definition asset of the type, parsed the
   way the loaders do (`JsonUtil.ToObject<T>` with the asset's domain) but not expanded.
   The files are `recipes/<type>/` plus every other file a registered recipe's `Name`
   points at. ACulinaryArtillery loads `simmerrecipes` from `recipes/simmering`.
5. **Grouping.** `Grouper` assigns each registered recipe to the definition it was
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
   plain Newtonsoft. Registered recipes that match no definition (made by code) are
   grouped by `Name` and slot layout into records `id = <type>|<Name or "code">|r<n>`
   with `extra.registeredByCode`.
6. **Records.** One record per definition. `ingredients` and `outputs` come from the
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
stacks from the hotbar. exlib's `ExRightClickConstructable` subclassed it up to exlib 0.7;
from 0.8 it is its own behavior (`ExRightClickConstruction`) whose stages have the same
JSON fields and the same `storeWildCard` and placeholder rules.
`InPlaceBuilds.IsConstructable` takes the engine's class or a subclass, or exlib's class
(by name) or a subclass; `InPlaceBuilds` finds every block whose block-entity behaviors
include one, and reads the behavior's `stages` from the registered block, so JSON patches
apply, parsed as the behavior parses them.

- Blocks with the same first code part and equal stages are one record (the four sides of
  a pump). Its output is the first of them with a handbook page, and all of them are in
  `extra.members`. The id is `construction|<that block>|<index of the behavior>`.
- Stage 0 is the block as placed. The engine never consumes its `requireStacks`, so
  neither does the export.
- `storeWildCard` remembers the variant of the stack consumed, and later stages fill
  `{name}` placeholders from it. A group some later stage uses is a binding with a variant
  per value that the storing slot accepts, and that slot then only takes the bound value.
  A group stored but never used (ppex stores `metal` on most slots, but only the
  mechanical pump's pipe uses it) binds nothing, so each slot takes what its own wildcard
  allows, as in game. exlib's own behavior (0.8 on) also refuses a stage paid with one
  group in two variants, so there the other slots of the storing slot's stage that store
  the same group take only the bound value too.
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
condition. The condition is the creature's `animalWeight` (vanilla
`EntityBehaviorHarvestable`), copied onto the carcass when it is picked up; the game's tick
keeps it between `Math.Max(0.5f, ...)` (the class's private constant `minimumWeight`) and
`Math.Min(1f, ...)`. It rises while the creature has eaten in the last four months (fast
within a week of a meal) and falls in freezing weather when it has not; with the world's
harsh winters off it is always 1. The exporter reads `minimumWeight` by reflection and
writes `butchery.condition` with it and 1 (a literal it cannot read). The exporter reads all
of these, the constants included, from the loaded mod. The
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
and how long each right click takes. The mod's smoking rack is a separate process with
records of its own ([below](#the-smoking-rack-the-butchering-mod)).

`mod` is the mod whose files hold the definition asset (vanilla recipes are `survival`),
and `source` is its asset location. Types from base-game registries are bare (`grid`);
mod registries are `<modid>:<registry code without "recipes">`, e.g.
`aculinaryartillery:simmer`. `recipeTypes` lists every registry found, including empty ones
(`count: 0`).

## Transitions over time

Not a registry either: a collectible's `TransitionableProps` (asset `transitionableProps`,
usually by type) lists what a stack of it turns into after some hours, and the engine checks
that list itself whenever it updates a stack (`CollectibleObject.
UpdateAndGetTransitionStatesNative`, decompiled 1.22.7). `Recipes/Transitions.cs` reads the
list of every registered collectible, so ByType is resolved and JSON patches are applied.
Each entry whose kind is not `None` and whose `transitionedStack` resolved is one record of
shape `transition` (an entry whose stack did not resolve is logged and counted, since the
engine could not carry it out either). The record type is the kind:

| `EnumTransitionType` | Record type | In the pack (1.22.7, this commit) |
|---|---|---|
| `Perish` | `perishing` | 3326, 2685 of them into `game:rot` |
| `Dry` | `drying` | 116 |
| `Cure` | `curing` | 237 |
| `Ripen` | `ripening` | 2 (raw cheese) |
| `Melt` | `melting` | 27 (snow, ice) |
| `Harden` | `hardening` | 4 (hot glue, lard, resin) |
| `Burn`, `Convert` | `burning`, `converting` | 0 |

Every kind gets an entry in `recipeTypes`, with `count: 0` when nothing has it.

What the engine does, from the decompiled code:

- A stack draws its own fresh and transition hours from the two `NatFloat`s when it is
  created, and turns when the hours it has spent pass both. The new stack's size is the old
  one's times `transitionRatio`, rounded at random; the transitioned stack's own quantity is
  ignored. So the record's output quantity is the ratio.
- Time counts at the rate `InventoryBase.GetTransitionSpeedMul` gives: 1 for perish, dry,
  cure, ripen, melt and harden in any ordinary inventory, 0 for burn and convert, and
  whatever a container's `OnAcquireTransitionSpeed` handlers or override make of it (a
  cellar, a mod's drying rack). Perishing also stops above 75 °C and is scaled by
  `GlobalConstants.PerishSpeedModifier`. The export gives the hours at rate 1.
- Once a stack has started to perish, every later entry of its list stops advancing: raw
  meat that is dry-aging stops drying when it begins to rot.
- A collectible class may override `GetTransitionableProperties` per stack (meals and liquid
  containers take their contents'); the export reads the collectible's own list only.

### Which mod a transition belongs to

`mod` and `source` name the file the transition comes from: the JSON patch that wrote it
into the item's type file, or else the type file itself. Expanded Foods' dry-aging of
vanilla raw poultry is `expandedfoods`, source `game:patches/poultry.json` (a file in
Expanded Foods' zip); raw poultry's own perishing stays `survival`, source
`game:itemtypes/food/poultry.json`. This differs on purpose from other recipe kinds, where a
patched definition stays with the file it patches (BetterRuins' cupronickel nails in a
vanilla grid recipe are `survival`).

A collectible remembers neither, so `TransitionOrigins` (in `Recipes/Transitions.cs`)
reconstructs it:

1. The item's type file is the one `ModIndex` already maps it to (the type whose code is
   the longest dash-separated prefix of the item's code).
2. The patch files are read as the engine's `ModJsonPatchLoader` (VSEssentials, decompiled
   1.22.7) selects them: `patches/` of every domain, in the same order, parsed as its
   `JsonPatch`, skipping disabled patches, client-only ones, unmet world-config conditions
   and unmet `dependsOn`. A `file` ending in `*` covers every file it prefixes.
3. A patch on that file is the origin when its value, at its `path`, holds an entry equal to
   the live one: same kind, same output (a domain-less code takes the type file's domain,
   `{placeholders}` match any value), same fresh hours, transition hours and ratio (an
   absent field counts as the engine's default: 36, 12 and 1), and under
   `transitionablePropsByType/<key>` with a key that matches the item's code (the engine's
   `WildcardUtil`), or under `transitionableProps`. The last such patch in load order wins,
   as the last one applied is in effect. No match: the type file.

It finds the right file whenever a patch adds or replaces whole entries. In the pack that
is 210 records credited to a patch: Expanded Foods (cider, juice, spirits, fruit, meat, fish), Primitive
Survival (live fish perish into raw fish) and Expanded Matter (graded powder), each checked
against the patch files. Where it is wrong:

- A patch that rewrites a whole list takes the credit for entries it copies over unchanged.
- A patch that changes only one field of an entry (`replace` on `.../freshHours`) carries no
  whole entry, so the entry stays with the type file, though its hours are the patch's.
- `move` and `copy` operations have no value and never match (Expanded Foods moves raw
  mash's list; the content is the game's, so the game keeps it, which is right).
- Entries a mod adds in code, not by patch, are credited to the type file.

## The smoking rack (the Butchering mod)

The Butchering mod's smoking rack (`blocktypes/smokingrack.json`, entity class `MeatHook`,
`BlockEntityMeatHook` in the decompiled 1.14.3) is not a transition: an item whose attribute
`transformsWhenSmoked` names another item can be hung on it, one per slot, 16 slots. While
the firepit directly below burns, each item's `smokingTime` grows; past the class's
constant `smokingTimeHours` (4) it becomes one of the named item. When the fire is out the
time of everything on it goes back to 0. The rack reads `Itemstack.Item`, so blocks cannot
be hung, and the output code goes through `new AssetLocation(code)`, so a domain-less one is
in `game`.

`Recipes/Smoking.cs` finds the class by name through the class registry (the exporter cannot
reference the mod), the racks as every block whose entity class is it, the hours from the
constant by reflection, and the items from their live attributes (ByType resolved, the mod's
own patches on vanilla meat and fish applied). Each item is a record `smoking|<item>|0` of
type `smoking` ("Smoking rack") and shape `transition`: the item, then the racks (role
`station`, not consumed), the smoked item as the one output, `transition.type` `smoke` with
0 fresh hours and 4 transition hours, and the firepit in `requirements`. 10 records in the
pack: Butchering's prime meat and sausages and vanilla red meat, bushmeat and fish, raw and
cured.

## The gear chain (seraphhorizons)

The pack's own mod has three processes no registry holds (shapes in
[schema.md](schema.md#the-gear-chain)). `Recipes/GearChain.cs` reads them from the mod's
loaded systems by reflection, by type name, since the exporter is built on its own and cannot
reference the mod; what it reads is the server's own settings, `ModConfig` included. Each one
is left out when its switch is off or what it names is not registered.

- The pickling tub: `PicklingTubSystem.Rules.Rules`, the acid table and the brine bath's two
  rules per brine, each a `picklingtub` record; `Config.BatchSize` and `LitresPerBatch`. A
  rule whose liquid matches no registered item (hydrochloric acid without Expanded Matter) is
  skipped with a note in the log.
- The oiled gear: `GearReclamationSystem.Config.UsableGearChance` and `BitsPerFailedGear`, one
  `lottery` record.
- The gear cutter: `config/gearcutter-rig.json` gives the turns per tooth (`cut.turnsPerTooth`),
  the masters (`cut.masters`) and the teeth (`work.end`) of each class (`thin`, `thick`); the
  blank and gear of each class are constants (`GearChain.ClassStock`). The wear per gear and the
  turns per tooth come from `SeraphHorizonsConfig.GearCutterSettings` (`CutterWearPerGear`,
  `TurnsPerTooth`), the oil from `MachineOilSettings.GearCutter` (`Tank`, `DrainPerJob`) and
  the oils from `OilLiquids` and `OilLumps`, each read when the mod has it; until the cutter's
  gameplay lands, `GearChain`'s defaults stand in (wear 10, oil 10 points, tank 1000). A large
  gear's wear is the small one's times its teeth over the small gear's, rounded up, and its oil
  double. The `GearCutter` switch, once there, leaves it out when off.
- The draw bench (`Recipes/DrawBenchExport.cs`, `RecipeSection.DrawBench.cs`, type `drawbench`, shape
  `machine`): one record per metal, `drawbench|game:ingot-{metal}|0`. `config/drawbench-rig.json`
  gives the sections an ingot (`draw.sectionsPerIngot`), each class's ingot (`draw.ingots`) and the turns
  a section as a fallback; `SeraphHorizonsConfig.DrawBenchSettings` gives the turns a section
  (`TurnsPerSectionLead`, `TurnsPerSectionCopper`), the die's wear (`DieWearPerIngot`, rule `fixed`) and
  which dies draw the metal (`DieMetals`); `MachineOilSettings.DrawBench` the oil per section and the
  tank. The gearbox, chain, dog and mandrel are `kept`, their alternatives the variant's stacks; the
  output is three of the game's chute sections of the metal (work unit `sections`), and a metal whose
  chute section is not registered (lead with `UnifiedPipes` off) has no record. The `DrawBench` switch leaves it out when off.
- The press brake (`Recipes/PressBrakeExport.cs`, `RecipeSection.PressBrake.cs`, type `pressbrake`,
  shape `machine`): one record per metal, `pressbrake|game:metalplate-{metal}|0`.
  `config/pressbrake-rig.json` gives each class's plate and open section (`fold.plates`,
  `fold.sections`), the sections a plate (`fold.sectionsPerPlate`) and the lever turns a plate as a
  fallback; `SeraphHorizonsConfig.PressBrakeSettings` gives the lever turns a plate
  (`LeverTurnsPerPlateLead`, `LeverTurnsPerPlateCopper`). A hand machine: `power` is `hand`, `turns`
  the lever's (a turn a second while right-click is held), and there is no `wear` and no `oil`. The
  screws and edges are `kept`, their alternatives the variant's stacks; the output is two open chute
  sections of the metal, and a metal whose open section is not registered (`UnifiedPipes` off) has no
  record. The `PressBrake` switch leaves it out when off.

## Casting in tool molds

`Recipes/Casting.cs` exports every block of the game's `BlockToolMold` class: the game's tool
molds, and the pack's gear blank molds, which use the same class. The game pours
`requiredUnits` (100 when unset) of molten metal into one and gives its `drop` (or each of its
`drops`) with `{metal}` replaced by the poured stack's last code part; the mold refuses a metal
whose drop does not exist. So each `ingot-<metal>` item stands for a metal (the `game` one when
several domains have it), and a variant is exported for each metal whose drops all resolve.
Molds that differ only in colour are one record, id `casting|<first mold code>|0`. Ingot molds
(`BlockIngotMold`) are not exported.

## Other time-based processes (not covered)

Checked in the pack's code (decompiled) and assets; none is exported:

| Mod | Mechanism | Why not |
|---|---|---|
| Primitive Survival 5.1.4 | Smoker (`BESmoker`): trussed raw meat becomes `path.Replace("raw", "smoked")`, and taking it out gives 4 of `smokedmeat-<part>-raw` | Outputs are built from code strings, and the time (5/12 of a day after lighting) is a literal in a method, which reflection cannot read |
| Age of Flax (fork) 1.1.6 | Drying rack (`BlockEntityDryingRack`) for retted flax | Only speeds the flax bundle's own `Dry` transition (×3 by its balance config); the transition is exported |
| Alchemy 2.2.0-rc.12 | Herb racks (`BlockEntityHerbRacks`) for `herbrackable` items | Only a speed: `Dry` and `Melt` ×4, or ×5 in a closed room |
| Food Shelves 3.1.0 | Ceiling racks, jars, barrel and tun racks | Only speeds (drying ×4.5, curing ×0.74 to ×0.8) through `OnAcquireTransitionSpeed` |
| Immersive Woodworking 1.3.11 | Green bark dries faster in ground storage (`ItemIwGreenBark.GetTransitionRateMul`) | Only a speed |
| Purposeful Storage, A Culinary Artillery, Turpentine, Yang Transport, Hydrate or Diedrate | Their assemblies hook `OnAcquireTransitionSpeed`, `GetTransitionSpeedMul` or `TransitionableSpeedMulByType` | Speeds, by the hooks they use; not decompiled further |
| Compost Bin 1.3.15 | A bin that composts perishables, with a Harmony patch on `GetTransitionRateMul` for perishing | Composting, not drying; not examined further |
| Stone Bake Oven 1.4.0 | "Smoke" on the oven grill | Only the grill block's hot/cold look, not a process |

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
  own behavior, with a metal binding and stages paid in one metal), and every block with
  either behavior in exactly one record;
- butchery: the whitetail deer's stages, stations, tools, bleed time, blood and rewards from
  the Butchering mod's assets; its harvestable drops split between hook and table and halved
  in the field; every butcherable entity variant in exactly one record, with aligned yields;
- transitions: wet sinew curing into dry sinew (Butchering's asset), a vanilla bowstave
  drying, raw cheese ripening and then perishing (list positions, ratio 4) and a bush cutting
  perishing into a quarter as many sticks; every transition of every registered collectible
  in exactly one record of its kind's type;
- transition origins: Expanded Foods' dry-aging patch on raw poultry credited to it, raw
  poultry's own perishing to the game;
- the smoking rack: prime meat and patched-in vanilla red meat, 4 hours, both racks as
  stations, and every item with `transformsWhenSmoked` in exactly one record;
- the gear chain (`RecipeExportGearScenarios.cs`): the tub's rules with their hours, losses
  and failure output, the oiled gear's one in ten, the gear cutter's two blank sizes (turns,
  kept master, kit wear, oil), the gear blank molds and two vanilla tool molds cast, and every
  link of the gear chain's handbook page;
- the draw bench (`RecipeExportDrawBenchScenarios.cs`): a record per metal, its kept stages, the
  dies that draw it, the oil and three chute sections;
- the press brake (`RecipeExportPressBrakeScenarios.cs`): a record per metal, its kept stages,
  power `hand` at the lever's turns, and two open sections;
- records per type against the definitions counted with the engine's asset loader, and
  variants per type against the sizes of the engine's registries;
- the structural rules of the document, schema validation (JsonSchema.Net, draft
  2020-12), determinism, and the failure path (a registry without a list, and one whose
  class has no recognisable ingredients, both throw and name the registry).

`tools/tests/test_packtool_export.py` covers the packtool side
(`python3 -m unittest discover -s tools/tests`).
