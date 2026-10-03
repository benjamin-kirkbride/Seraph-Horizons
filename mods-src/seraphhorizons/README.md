# Seraph Horizons

The pack's own mod, named after the pack: every install of the pack downloads it, so its ModDB
page shows the pack's download count and is where people browsing the ModDB find the pack. Its
modid is the pack's id, so the pack's meta-mod (`packtool assemble`) is `seraphhorizonspack`.

It is a code mod holding the pack's own tweaks: gameplay changes to other mods, and Tidy Variants, which
tidies the creative inventory and the handbook. These are choices for this pack, not bug fixes, so
they live together here and not in a mod each. Every tweak has its own switch in
`ModConfig/seraphhorizons.json` (all on by default). A tweak whose mod is not installed is skipped.
One whose mod has changed shape logs a warning and leaves that mod alone.

Only the game's own assemblies are referenced at build time: each tweak to another mod finds what
it patches by name, so the mod builds from the game alone (`mod-release.yml` needs nothing else).

`"side": "Universal"`, required on the client. The server does the boiler behavior and feeds the
creative steam source; Tidy Variants and cart reach run on the client. The client needs the mod because the
steam source is a block with its own classes: the game cannot build a block whose class it does not
know, so a client without the mod could not join a server that has it (a server with the steam
source switched off, or without ppex, has no such block).

## Tweaks

### Boilers blow their lid instead of exploding (`BoilerLidBlowsOpen`)

Pipes and Power Expanded (`ppex`, 0.7.x). A boiler that keeps firing at its choke pressure for
`BoilerOverpressureSeconds` (ppex config, 30 s) calls `BlockEntityBoiler.Explode()`, which deletes
the boiler, drops a fraction of its materials and blasts the area. `BoilerLidRelief` prefixes
`Explode()` with the boiler's own `ToggleLid()` and skips the blast: the access lid swings open
with its sound and animation, and ppex vents steam through the open lid (`BoilerLidVentRate`,
200 L/s) and resets the over-pressure timer while it stays open. The player closes the lid by hand,
and a boiler still with nowhere to send its steam will blow it again.

The lid blowing open bangs like a ppex engine blowing up: it plays the sound ppex's
`BlockEntityEngine.Break()` plays, `game:sounds/effect/mediumexplosion` (ExpandedLib's
`ExSounds.MediumExplosion`), from the server at the boiler, over 24 blocks at half volume and
unrandomized pitch, as the engine does. The lid's own creak is still `ToggleLid()`'s.

The text that promised an explosion is reworded in place, in every language ppex ships (English,
Russian, Ukrainian): the over-pressure line in the boiler's info ("until the lid blows open!") and
the passages in the Steam Power and Boilers handbook pages. Each edit replaces one exact passage of
ppex's text (`LangEdits`). If ppex rewords it, that edit logs a warning and does nothing. Languages
other than the current one load lazily, so the mod loads those three when it starts.

### The handbook says where a chimney vents (`ChimneyVentingExplained`)

Pipes and Power Expanded (`ppex`, 0.6.8 and 0.7.x: the text is the same). ppex vents a pipe network
through a chimney only when the chimney stands directly on a Pipe Outlet, a Pipe Passthrough or a
Passthrough Bend with a connector on its top face, at `ChimneyGasDrawRate` (16 L/s) per chimney. On
a plain pipe the chimney is not a vent, and because it is not air it is not a leak either: it caps
that end and the run keeps its pressure. ppex's handbook only says that a chimney on an outlet
vents.

`ChimneyVentText` rewords the chimney passage of the Fittings handbook page to say all of that, and
that a venting chimney's look-at info says so (ppex's own `chimney-info-venting` line), in every
language ppex ships. Text only, as exact-passage `LangEdits` like the boiler's; the Russian and
Ukrainian passages are the pack's own translations.

### Fewer surface battle towers (ConfigKit settings)

Battle Towers (`battletowers`, 1.1.0) has no settings: a surface tower has a 0.03 chance per chunk
and only 200 blocks between two of them, so they outnumber every other surface structure.
`assets/seraphhorizons/config/configlib-patches.json` declares two settings, which ConfigKit writes
into Battle Towers' own patch file (`patches/survival-worldgen-structures.json`, entry 0) before
the game applies it:

| Setting (`ModConfig/seraphhorizons.yaml`) | Battle Towers | Here |
|---|---|---|
| `battletowers_surface_chance` | 0.03 | 0.01 |
| `battletowers_surface_min_distance` | 200 | 600 |

The hard and underground towers are left as Battle Towers ships them. This tweak is data, not a
class: it has no switch in `seraphhorizons.json`, does nothing without ConfigKit, and is changed in
ConfigKit's settings screen or, for the pack, in `pack/config/ModConfig/seraphhorizons.yaml`. Like
any worldgen setting it only affects chunks not generated yet.

### Creative steam source (`CreativeSteamSource`)

Pipes and Power Expanded (`ppex`). A creative-only block, `seraphhorizons:creativesteamsource` ("Steam
source (Creative)"), that keeps every ppex pipe connected to it full of steam: the steam
counterpart of the game's creative auto rotor, for testing engines and pipe layouts without a
boiler. It is a plain cube with no texture of its own (the game's missing-texture look), in the
General and Pipes and Power Expanded creative tabs. It has no recipe, drops nothing and is left
out of the handbook.

It is set up the way the auto rotor is, by right-clicking it (the block's info shows both
settings):

- right-click: raise the output pressure, Ctrl+right-click: lower it (1 to 10 atm, from 3);
- Shift+right-click: raise the flow rate, Ctrl+Shift+right-click: lower it (0 to 100 L/s in
  steps of 10, from 30; 0 is off).

Each setting wraps around at its ends, as the auto rotor's do. Place a pipe against any face of
the block with a connector pointing at it (placing the pipe by clicking the block does that). Each
connected pipe takes its share of the flow, as steam at the set pressure, through ppex's own
`IPipeNode.TryProduce` on the pipe: the network fills until it reaches that pressure, never past
the weakest pipe's burst pressure, exactly as from a boiler. At or above a pipe's burst pressure
(iron 5 atm, steel 10 atm by ppex's defaults) ppex's over-pressure timer bursts a pipe as usual,
and a leaking network is capped at 1 atm as ppex caps any source.

Without ppex, or with the switch off, the blocktype is disabled before the game loads it, so the
block does not exist at all. It targets ppex 0.7.1 and exlib 0.8.4, where the pipe interface is
exlib's `ExpandedLib.Industry.Pipes.IPipeNode` and the node base class is
`ExpandedLib.Networks.BlockNetworkNode`; with an older ppex (0.6.8, still the pinned one) those
names are not there and the block is left out. If ppex has changed shape (`IPipeNode.TryProduce(float, float,
string, float, bool)` or exlib's `BlockNetworkNode.HasConnectorAt(BlockFacing)` is gone) the mod
logs a warning and the block is left out the same way.

### Age of Flax rebalance (`AgeOfFlaxRebalance`)

Age of Flax (fork) (`ageofflaxfork` 1.1.6, asset domain `ageofflax`). Ripe flax drops flax bundles
(avg 1.2, Age of Flax's), which go through the ripple (grain), a barrel (retting), drying, the break
and the hatchel (fibers). Each tool comes in three tiers, which process 1, 2 and 4 bundles per use.
This tweak:

- **seeds**: the flax plant drops vanilla flax's seeds again, avg 1.2 when ripe (stage 9) and avg
  0.7 at stage 8, on top of Age of Flax's bundles (avg 1.2 and 0.5); stages below 8 drop Age of
  Flax's seeds as before. The ripple drops no seeds at any tier.
- **yields**: the ripple's grain and the hatchel's fibers per ripe plant are 2/3 of vanilla flax's
  (grain avg 3, fibers avg 4) with primitive tools, the same with simple ones and 4/3 with advanced
  ones:

  | tier | grain per bundle | grain per ripe plant | fibers per bundle | fibers per ripe plant |
  | --- | --- | --- | --- | --- |
  | primitive | 1.67 (±0.25) | 2 | 2.22 (±0.35) | 2.67 |
  | simple | 2.5 (±0.4) | 3 | 3.33 (±0.5) | 4 |
  | advanced | 3.33 (±0.5) | 4 | 4.44 (±0.65) | 5.33 |

  Age of Flax rolls once per use (a Gaussian of avg and the ± as its standard deviation, rounded at
  random) and multiplies by the bundles processed, so the mean is exact; the spreads are small
  enough that a roll below zero, which would drop nothing and raise the mean, practically never
  happens. Was: seeds 1.2 / 2.1 / 2.5 and grain 6 / 8 / 12 per bundle, fibers 5 / 6 / 8.
- **steel**: the advanced ripple, hatchel and break take steel nails and strips (and the break a
  steel rod) instead of iron.
- **fat**: every break takes raw or rendered fat (`game:fat*`), as the ModDB mod
  [Age of Flax Fork Break Patch](https://mods.vintagestory.at/ageofflaxforkbreakpatch)
  (`ageofflaxforkbreakpatch` 1.0.0) does; its only content is that patch to `breaks.json`.
- **text**: Age of Flax's English text (the only language it ships) says all of this: the seeds come
  from the plant and the ripple strips the grain (the guide, the unprocessed bundle and the ripples),
  the guide gives each tier's yields and materials, and the drying rack dries 3x faster, its real
  `dryingRackSpeedMultiplier` (the text said 2x). It also puts right the dried and broken bundles'
  descriptions, which a duplicate key in Age of Flax's lang file mixed up, and two "Extact" typos.

The yields and recipes are JSON patches in `assets/seraphhorizons/patches/ageofflax-*.json`, each
`dependsOn` ageofflaxfork. Age of Flax reads `ageofflax:config/balance.json` again in each tool's
and the crop's `OnLoaded`, after the patch loader has run, so the patched values are the ones used.
With the switch off (or Age of Flax changed) the system empties those patch files in `Start`, before
the patch loader runs in `AssetsLoaded`. Age of Flax's `BlockCropFlax.GetDrops` drops only bundles
at stages 8 and 9 and never reads the blocktype's drops, so the seeds come from a Harmony postfix on
it (server side); the same seeds are added to the flax blocktype's `*-8` and `*-9` drops, which the
handbook lists. The text edits are `LangEdits` exact-passage edits, as for ppex (`LangText`); the
ripple and drying rack descriptions are wildcard keys, so they are set on each variant's own key.
The switch is read on each side: a client with it off keeps Age of Flax's own text.

### A cart's far slots are in reach (`CartReach`)

Cartwright's Caravan (`cartwrightscaravan` 1.9.1). In survival, the two rear storage slots of a
basic cart (`RightStorage3AP`, `LeftStorage3AP`) do nothing when clicked from behind the cart; from
the side, or in creative, they work. The game picks an entity under the crosshair only from those
whose origin is within the picking range of the eye (`GameMain.RayTraceForSelection` takes them
from `GetEntitiesAround(eye, range, range)`), 4.5 blocks in survival and 100 in creative. A basic
cart's origin is near its front and its rear slots end about 4 blocks behind it, so from behind the
cart a slot can be well within reach while the cart is not looked at at all.

`CartReach` postfixes that method, on the client, for its own world only. After the game has
picked, it looks again at the entities it left out that match `CartReachEntities`, out to 6 blocks
past the range, and tests their selection boxes against the same ray. It takes one only if the point
the ray hits is within the picking range and nearer than the block or entity the game picked, so
the reach stays the game's, measured to the slot instead of to the cart. The setting is a list of
entity codes with `*` wildcards (no domain means `game`), by default `cartwrightscaravan:*`: the
carts, the sled and the market stall, all of which have slots beyond their hitbox. The patch goes in
when the level is loaded and some entity type matches; it logs how many do.

The server needs nothing: the client sends the entity and the slot it picked, and the server finds
the entity within the picking range + 10 of the player and uses the slot from the packet. One
exception: with the server's `AntiAbuse` setting on (`Basic` or `Pedantic`; it is `Off` by
default), the server also requires the eye within the picking range + 0.25 of the entity's hitbox,
so a rear slot clicked from well behind a cart is still refused there.

Two mods in the pack patch the same code for Yang's Transport Tycoon's locomotives, and this tweak
leaves both alone: `yttenhancedinteractionfiltering` transpiles `RayTraceForSelection` and requires
exactly one `GetEntitiesAround` call in it, which a postfix does not add to (Harmony hands
transpilers the method's body, never another mod's prefixes or postfixes, whichever loads first);
`evilinteractionrangehack` transpiles the server's `HandleEntityInteraction`, which this tweak does
not touch. The pure logic (which codes match, when a far hit wins) is `Core/EntityReach.cs`.

### Every food has a hydration value (`FoodHydration`)

Hydrate or Diedrate (`hydrateordiedrate` 2.5.6). Eating a food changes thirst by its `hydration`
attribute (positive quenches, negative costs), which Hydrate or Diedrate fills in at load from its
pattern lists (`config/hod.additemhydration.json`, `hod.addblockhydration.json`). Foods no pattern
matches get 0: some are newer than the lists, some patterns are misspelled (Expanded Foods'
`pemmicanfish-*` for `fishpemmican`, `gelatinfish-*` for a `gelatinfish` with no variants), and
some name a code that has since gained variants (`game:butter`, `butchering:offal`). Meal
ingredients count too: a meal's hydration comes from its ingredients'. This
tweak gives each of them a value modelled on Hydrate or Diedrate's for a similar food (#319):

| Mod | Food | Hydration | Modelled on |
|---|---|---|---|
| game | fish chunk, cooked, sizes 2 / 4 / 7 / 9 | -40 / -80 / -140 / -180 | `fish-cooked` -20 at satiety 100, scaled by satiety (200 to 900) |
| game | egg, raw / boiled / pickled (Expanded Foods adds the last two) | +10 / +5 / +10 | no close match: `limeegg` +1, dough +5, pickled vegetables +15 to +30; raw above cooked, as for fish |
| game | butter, salted and unsalted | -80 | Hydrate or Diedrate's `butter`, which no longer matches the variants |
| game | raw cassava, raw / soaked / dried | +15 / +20 / -30 | turnip and parsnip / carrot and onion / `grain-*` and flour (`vegetable-cassava`'s +200 is an outlier) |
| game | salt (a meal ingredient by Expanded Foods' patch) | -50 | no close match: between cooked fish (-20) and cured fish (-70); salt water is -600 a litre |
| game | pineapple (the whole fruit, a block) | +420 | the 12 `fruit-pineapple` it is cut into, +35 each |
| game | fat, raw and rendered | -80 | `butter` |
| game | honeycomb | -75 | `honeyportion` |
| game | walnut (`treeseed-walnut`) | -2 | Wildcraft's `nut-*` |
| game | mushroom: sickener, laughing jim | -30 | earthball and elfin saddle (health -8 and -7; these are -7 and -10) |
| game | mushroom: fool's conecap | -35 | deathcap (deadly, as it is) |
| game | mushroom: goldcap, liberty cap, wavy cap, blue meanie | +5 | no close match (psychedelic, no health effect): the low end of the edible mushrooms |
| bdcrop | buckwheat grain / dough | -30 / +5 | `grain-*` / `dough-*` |
| bdcrop | buckwheat bread, part-baked / perfect / charred | -40 / -50 / -80 | spelt bread |
| bdcrop | potato | +20 | vanilla `vegetable-*`; bdcrop's own patch misses it (it is in `game-vegetable.json`, not `vegetable.json`) |
| butchering | offal, bloody and clean | +3 | Hydrate or Diedrate's `butchering:offal`, which no longer matches the variants |
| efchefstricks | bread crumb feed | -5 | `expandedfoods:breadcrumbs-*` |
| expandedfoods | fish, crab and snake pemmican, every state | -25 | `pemmican-*` (and the misspelled patterns' own value) |
| expandedfoods | fish gelatin | +1 | `gelatin-*` (and the misspelled pattern's) |
| expandedfoods | fruit leather, sliced | -5 | dried fruit (`dryfruit-*`, `dehydratedfruit-*`) and `fruitbar-*` |
| primitivesurvival | fish fillet, raw / cooked | +15 / -20 | vanilla `fish-raw` / `fish-cooked` |
| primitivesurvival | fish fillet, part-baked / charred | -15 / -30 | no close match: between and beyond raw and cooked, as bread's states are (about 0.8 and 1.6 of perfect) |
| primitivesurvival | fish eggs raw / cooked, caviar (cured) | +15 / -20 / -70 | vanilla `fish-raw` / `fish-cooked` / `fish-cured` |
| primitivesurvival | nightcrawler | +10 | `insect-*` |

Only foods with no value are covered, eaten by themselves or in meals: wet or dried fruit leather,
curing caviar and bdcrop's flour are neither, and bdcrop sets its vegetables other than tomatoes
to 0 on purpose.

The values are JSON patches in `assets/seraphhorizons/patches/hydration-*.json`, one file per mod
patched, each adding `hydration` (or `hydrationByType`) to the item or block type's `attributes` (for
bdcrop's potato, to its `attributesByType` entry, where its food attributes are)
with `addmerge` (which creates `attributes` where there is none) and `dependsOn` hydrateordiedrate
and the patched mod, so each is a no-op without them. Hydrate or Diedrate only sets a hydration
attribute that is not set yet, so these values stay put, and one it adds later for the same food
does not replace them. They are `"side": "server"`, as bdcrop's own hydration patches are: item and
block types are loaded from their JSON on the server only, and the client gets them, attributes
included, from the server, so its tooltip shows the same values. With the switch off (or without
Hydrate or Diedrate) the system empties those patch files in `Start`, before the patch loader runs
in `AssetsLoaded`, as for Age of Flax. The switch that counts is the server's.

### Tidy Variants (`TidyVariants`)

The pack's creative inventory has about 29,000 entries, mostly variant multiplication (ores ×
rocks, doors × rocks × woods, foods × cooking states). From one set of rules, Tidy Variants:

- **hides** orientation and open/closed-state variants: gone from the creative and handbook lists,
  search included. `/giveitem` still works.
- **groups** everything else: one creative tile and one handbook page per group, showing a
  representative member. Right-click a tile (or Ctrl+G over it) to expand the group inline, and
  right-click any of its members to collapse it; left-click still takes the item. Search runs over
  the flat list first, so it never loses an item.

Nothing decorative becomes unreachable from the UI. The design and its decisions are in #252, the
details in `docs/variant-grouping/` (creative inventory, handbook, game hooks, the Atlas report).

All of it acts on the client. The server resolves the same rules too, but only so the Atlas
scenarios and the report can read them. With the switch off nothing is resolved, patched or
changed, on either side. The creative inventory patches are applied by hand, on the client only,
under the Harmony id `seraphhorizons.creative`. Each client keeps its expanded groups in
`ModConfig/seraphhorizons-tidyvariants-creative.json`.

- `TidyVariants/Core/`: the rule engine. Game-independent (the BCL only), so `tests/` compiles
  these files directly and runs without the game. The format of the override file is in
  `TidyVariants/Core/README.md`.
- `TidyVariants/Game/`: the game-facing code (rule input from the loaded collectibles, the
  handbook's `groupBy`, the creative GUI patches) and the feature's mod systems.
- `assets/seraphhorizons/config/tidyvariants-overrides.json`: the pack's override rules, and
  `assets/seraphhorizons/lang/en.json`: their group titles and the feature's UI text. The feature's
  lang keys all start with `tidyvariants-` (`seraphhorizons:tidyvariants-group-...`).

Some rules in the override file are ported from Handbook Declutterer (actioninja), which builds
on Fix Handbook Clutter (Craluminum2413), MIT. The notice and lineage are in `CREDITS.md`, which
ships in the mod zip.

## Tests

`tests/` (xunit, no game): Tidy Variants' rule engine and the shipped override and lang files,
and cart reach's entity matching and reach rule (`Core/`). `dotnet test mods-src/seraphhorizons/tests`.

`tests/PackTests/SeraphHorizonsModScenarios.cs` (Atlas) places a Cornish boiler, calls `Explode()` with
the lid shut and with it open, and requires the boiler still standing with its lid open. It
requires `BlowSound` to be ExpandedLib's `ExSounds.MediumExplosion` and present in the assets. It also
requires every `LangEdits` passage reworded, and an edit set for every language ppex ships: when
either fails after a ppex update, match the edits to ppex's new text or add the new language. The
same goes for `ChimneyVentText.LangEdits`, whose passages must also quote ppex's look-at line for a
venting chimney.

It also reads the patched `game:worldgen/structures.json` and requires the surface tower's chance
and spacing above, with the hard tower's unchanged: when that fails after a Battle Towers update,
match the paths in `configlib-patches.json` to its new patch file.

The same class places the creative steam source against a closed iron pipe and requires the pipe
full of steam at the set pressure, and no higher; it also requires the block in the creative
inventory with no drops and no recipe. `CreativeSteamSourceOffScenarios` boots a server with the
switch off and requires the block not to exist. While the pack pins a ppex older than 0.7.1, the
steam scenarios require the block left out instead, and run in full once the pin moves.

For cart reach, the same class requires `CartReachEntities`' default to match Cartwright's carts,
sled and market stalls, each with selection boxes. It then runs the game's selection code on the
server's world (the same `GameMain` code the client runs) against a basic cart, posed at rest and
with its boxes loaded as the client has them: from 2 blocks behind the rear right slot the game picks
nothing with survival reach and the slot with creative reach, the tweak's second look picks the
slot, from 6 blocks behind it picks nothing, and a block in between stays picked. When it fails
after a Cartwright's update, check whether the cart's slots moved, or whether the game now picks the
cart by itself and the tweak can go. A third scenario applies yttenhancedinteractionfiltering's
transpiler (its assembly is loaded on the server, its system is not) and cart reach's postfix to the
method, in both orders, and requires the transpiler to have swapped in its own call and the method
to run. The patch itself only goes in on a client, which Atlas does not run, so whether it is applied
and acts there is checked by hand in the game.

`tests/PackTests/AgeOfFlaxRebalanceScenarios.cs` (Atlas) reads the loaded ripples' and hatchels'
yields (what the tools use, set from the patched balance file), requires steel and no iron in the
advanced recipes and both fats in every break, rolls a flax plant's drops at stages 9, 8 and 5 on
farmland (seeds and bundles), checks the seeds in the blocktype's drops, and requires every
`LangEdits` passage reworded. When it fails after an Age of Flax update, match the patches and edits
to the new files. `AgeOfFlaxRebalanceOffScenarios` boots a server with the switch off and requires
Age of Flax as it ships.

`tests/PackTests/HydrationCoverageScenarios.cs` (Atlas) requires a `hydration` attribute on every
food the server loads: anything eaten, used as a meal ingredient or drunk. An explicit 0 counts. When
it fails after a mod is added or updated, it lists the foods to give a value in `patches/hydration-*.json`.

`tests/PackTests/TidyVariants*Scenarios.cs` (Atlas) resolve the rules on a server with the whole
pack: every creative entry maps to its stack and back, the handbook layout keeps one listed page per
group, the creative and handbook systems stay off on the server, and the report
(`docs/variant-grouping/report.md`). `TidyVariantsOffScenarios` boots a server with the switch off.

The test project loads this directory's build as a mod, and leaves out a pinned copy from the
ModDB (`seraphhorizons_*.zip` in `build/mods`).

## Adding a tweak

Add a class next to `BoilerLidRelief.cs`, a `bool` setting for it in `SeraphHorizonsConfig`, and
the call in `SeraphHorizonsSystem` behind that setting. Then add scenarios and a section above. A
tweak big enough for mod systems of its own gets a folder, as `TidyVariants/` does; its systems
read their switch with `SeraphHorizonsSystem.ConfigFor(api)`.

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`SeraphHorizons.csproj`, merge, tag `seraphhorizons-v<version>` on main, upload the zip from the
GitHub Release to the ModDB (keep the file name), then pin it in `pack/pack.toml` (the first
release adds the entry, `side = "universal"`) and run `packtool lock`.

For a local build: `dotnet build mods-src/seraphhorizons -c Release` (needs `VINTAGE_STORY`) writes
`build/seraphhorizons_<version>.zip`: the DLL, `modinfo.json` and `CREDITS.md` at the top level, and
`assets/`.
