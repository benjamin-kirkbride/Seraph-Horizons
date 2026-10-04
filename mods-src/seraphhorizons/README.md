# Seraph Horizons

The pack's own mod, named after the pack: every install of the pack downloads it, so its ModDB
page shows the pack's download count and is where people browsing the ModDB find the pack. Its
modid is the pack's id, so the pack's meta-mod (`packtool assemble`) is `seraphhorizonspack`.

It is a code mod holding the pack's own tweaks: gameplay changes to other mods, Tidy Variants, which
tidies the creative inventory and the handbook, and Map Reveal, which shows already generated
terrain on the world map. These are choices for this pack, not bug fixes, so
they live together here and not in a mod each. Every tweak has its own switch in
`ModConfig/seraphhorizons.json` (all on by default). A tweak whose mod is not installed is skipped.
One whose mod has changed shape logs a warning and leaves that mod alone.

Only the game's own assemblies are referenced at build time: each tweak to another mod finds what
it patches by name, so the mod builds from the game alone (`mod-release.yml` needs nothing else).

`"side": "Universal"`, required on the client. The server does the boiler behavior, drops the
chopper's output, feeds the creative steam source and runs `/clear`; Tidy Variants and cart reach
run on the client; Map Reveal has a half on each side, and the creative mod tabs need both. The
client needs the mod because the steam source is a block with its own classes: the game cannot
build a block whose class it does not know, so a client without the mod could not join a server
that has it (a server with the steam source switched off, or without ppex, has no such block).

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

### Assembled chopper and sawmill in creative (`AssembledMachinesInCreative`)

Immersive Woodworking (`immersivewoodworking`, 1.3.11). Its chopper and sawmill are in the creative
inventory only as empty frames, which take each part by hand. `AssembledMachines` adds a second
creative entry of each frame, named "Chopper" and "Sawmill", that places the machine assembled:
every mandatory part installed, with a new steel chopper head or blade kit. The sawmill's optional
flywheel is left out. The entry is the frame's own block with an `assembledWith` attribute naming
the metal (any metal the head or blade kit comes in; the creative entry is steel). It has the
frame's icon, and no handbook page: the frame's page stays as it is.

Nothing of Immersive Woodworking is patched or referenced. On the server, before the game reads
blocktypes, the two frame blocktypes get the creative stack and two behaviors of this mod: a block
behavior that names the stack, and a block entity behavior that, when the frame is placed from
such a stack, sets the part flags and the head or blade kit in the block entity's own saved
attributes and loads them back, the way a saved machine loads. Breaking the machine drops the
frame and its parts, as for one built by hand. If a blocktype or the saved attributes are not as
expected the mod logs a warning and that frame stays as Immersive Woodworking ships it.

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

### The handbook says how a well shaft holds water (`WellShaftExplained`)

Hydrate or Diedrate (`hydrateordiedrate` 2.5.6). A well spring counts the shaft above it level by
level (`BlockEntityWellSpring.OnPeriodicShaftCheck`), and holds 70 liters per counted level. A
level counts when its cell is open and all four horizontal neighbors are full liquid barriers, so
a shaft wider than one block counts no levels and the spring reads "Max Well Volume: 0 liters".
Each wall block is rated for a depth: 5 by default (`WellwaterDepthMaxBase`), 7 for `game:brick*`
(`WellwaterDepthMaxClay`) and 10 for `game:stonebrick*` (`WellwaterDepthMaxStone`). Those are
prefixes of the block code: `brickcourse-*` (the clay bricks named after their bond) and
`stonebricks-*` (ashlar blocks) are rated, while `claybricks-*` (fireclay and uneven bricks) and
`agedstonebricks-*` are not. The count stops
at the first level that is no lower than the lowest rating seen so far, so one lesser block caps
the well at its own depth, or just below itself when it is higher than that. The Wells handbook
page only says to dig straight down, and lists the depths as "default", "bricks" and "ashlar".

`WellShaftText` rewords the Deep Well passages of that page to say all of that, with the liters
each depth holds and the spring's look-at line (Hydrate or Diedrate's own `well.retentionVolume`).
Text only, as exact-passage `LangEdits` like ppex's. English only: Hydrate or Diedrate's other
translations of the page differ in structure, and two are of an older text.

### The barrel rack takes kegs (`BarrelRackKegs`)

Food Shelves (`foodshelves` 3.1.0) and Hydrate or Diedrate (`hydrateordiedrate` 2.5.6). The barrel
rack (`barrelrack-*`, `BEBarrelRack`) takes a cask into its slot 0 and keeps the liquid in its own
slot 1, an `ItemSlotLiquidOnly`. What it takes is data: Food Shelves reads
`config/restrictions/barrels/barrelrack.json` (`"CollectibleTypes": ["game:BlockBarrel"]`) on the
server in `AssetsLoaded`, after the patch loader, and marks every match `fsbarrelrack` in
`AssetsFinalize`; the client gets that attribute with the block types. A patch here
(`patches/barrelrack-kegs.json`) adds `hydrateordiedrate:keg-*`, tapped and untapped, to its
`CollectibleCodes`. That alone would let a keg in, but a keg is not a barrel in three ways, which
`BarrelRackKegs` patches (Harmony, on both sides):

- **Its liquid.** A keg item carries its liquid (Hydrate's `KegDropWithLiquid`, on by default),
  where a vanilla barrel item is always empty. Racked as it ships, a keg's liquid would sit in the
  item, hidden, never perishing, while the rack's slot took another load. So after every
  interaction with the rack (`OnInteract`) a racked keg's liquid moves into the rack's slot; taking
  the keg out with an empty hand moves the rack's liquid back into it first, so it leaves full, as
  it would picked up from the floor; and breaking the rack does the same before the slots drop.
  Barrels keep the rack's rule (empty it before taking the barrel out). With `KegDropWithLiquid`
  off, kegs follow that rule too, and rot is never put back in a keg (Hydrate spills rot when a keg
  breaks; the rack hands rot out with an empty hand). Nothing is copied: the liquid is in one place
  at a time.
- **Its capacity.** The rack's capacity is its block's `capacityLitres` (50), read by
  `TryPutLiquid(BlockPos, ...)`, which every pour into it goes through. For a rack holding a keg
  that call runs with the keg's capacity (`BlockKeg.CapacityLitres`, Hydrate's `KegCapacityLitres`,
  100 by default) in the rack block's field, put back right after (a finalizer). A barrel's rack
  stays at 50. The field rather than a postfix on the `CapacityLitres` getter, which the JIT may
  inline into its callers.
- **Its spoil rate.** The rack halves perishing for whatever it holds (Food Shelves'
  `PerishMultiplier` 0.5, times its world settings `GlobalPerishMultiplier` and `GlobalBlockBuffs`).
  A keg has its own rate: Hydrate's `SpoilRateUntapped` 0.15 sealed and `SpoilRateTapped` 0.65 once
  tapped. A racked keg gets both, multiplied (0.075 and 0.325 by default): a vanilla barrel's own
  rate is 1, so the rack does to a keg what it does to a barrel, and the rack's "stay fresh 100%
  longer" holds for both. A racked keg is never worse off than standing, and tapped still spoils
  faster than sealed. The perish speed the rack shows is multiplied the same way. Curing is the
  rack's own (0.8), as for a barrel.

A racked keg looks like a racked barrel: the rack draws its own horizontal barrel shape
(`horizontalbarrel.json`) with the cask's textures, and the keg's `aged`, `bottom` and
`blackbronze4` are the barrel's. Tapped and untapped look the same in the rack.

The English text says so: the handbook's "Can hold" for the rack lists kegs, and the message for
a cask it refuses says barrels and kegs (`LangEdits`; Food Shelves' other languages keep their text).

Hydrate's config is read each time it is needed, so a change in ConfigLib applies at once. The
client predicts the interactions and pours, and refuses a pour past what it thinks is full before
asking the server, so both sides are patched, once per process (singleplayer runs both in one) with
their own Harmony id. The switch that decides what the rack takes is the server's. If the rack,
the keg or Hydrate's keg settings are not as expected, the mod logs a warning and leaves the rack
as it ships, the restriction patch included.

### The powered chopper drops its output in front (`ChopperDropsInFront`)

Immersive Woodworking (`immersivewoodworking` 1.3.11). The powered chopper (the multiblock frame,
`BlockEntityChopper`; not the hand chopping block) ejects each finished batch through
`EjectBatch(ItemStack template, int dropCount)`: the batch split into `dropCount` piles
(`DropDistribution.SplitEvenly`), spawned 0.7 out from the master block's centre on the output
side (opposite the frame's `Facing`) and 0.8 up, and thrown outward at 0.084 with up to ±0.04
sideways. The pieces fly over the cell in front and come to rest 2.1 to 2.3 blocks from the master's
centre, spread over three cells, so no single hopper catches them. The sawmill drops its planks gently instead: over the
middle of the cell behind its footprint, 0.4 up, with a 0.02 push and no sideways velocity, so one
hopper sunk into the floor there catches everything.

`ChopperOutput` prefixes `EjectBatch` and drops the batch the sawmill's way, into the cell right in
front of the master block on the output side, which is not part of the chopper's footprint
(`Core/ChopperEject.cs`): the same piles, each over the middle of that cell, spread across the
output side over 0.3 (the chopper's spread is 0.4) with the chopper's ±0.05 jitter, 0.1 above the
floor and with no velocity at all, so every pile lands at least 0.3 from the cell's edges. The drop
is short because the game blows a falling item along with the wind (an `EntityItem` takes the wind
while it touches nothing): in the Atlas world (wind about 1), firewood dropped from the sawmill's
0.4 drifted 0.17 sideways, from 0.1 about 0.06. A hopper sunk into the floor in that cell, with its top at the floor's level, catches
the whole batch; vanilla hoppers only take items that rest on their top face, so one standing at
the machine's level (the items would spawn inside it) catches nothing. Give the hopper somewhere to
pass the items on to, such as a chest under it, or it drops them out of its bottom.

Server side, where the chopper runs. Immersive Woodworking has no setting for this. If
`EjectBatch(ItemStack, int)` or the chopper's `Facing` is gone, the mod logs a warning and the
chopper keeps its own throw.

### One tun: Hydrate or Diedrate's is retired, Food Shelves' holds 950 L (`HydrateTunRetired`, `LargerTunRack`)

The pack had two tuns, both 2 x 2 x 2 liquid containers: Hydrate or Diedrate's (`hydrateordiedrate`
2.5.6, `hydrateordiedrate:tun-*`, 950 L by its `TunCapacityLitres` setting) and Food Shelves' tun,
which sits in a tun rack (`foodshelves` 3.1.0, `foodshelves:tunrack-*` holding `foodshelves:tun-normal`,
500 L). The pack keeps Food Shelves' one, at Hydrate or Diedrate's size. Two switches:

- **`HydrateTunRetired`**: Hydrate or Diedrate's tun has no grid recipe (`enabled: false`) and is
  left out of the creative inventory (its `creativeinventoryStacksByType` removed) and the handbook
  (`attributes.handbook.exclude`). The block type stays registered, so a tun already placed in a world
  keeps its liquid and still works, and breaking it still drops it. It can be placed again, but no new
  one can be made. Hydrate or Diedrate's "Craft a Tun" achievement needs Nat's Achievements, which the
  pack does not have, and its configuration page still lists the tun settings, which still apply to
  the tuns already placed.
- **`LargerTunRack`**: the tun rack holds 950 L instead of 500. Food Shelves keeps that number in
  two places, and both are set: the block's `attributes.capacityLitres`, which
  `BlockLiquidContainerBase` reads in `OnLoaded` and every fill and pour checks, and `BETunRack`'s own
  `private readonly int capacityLitres = 500`, which its constructor gives the liquid slot
  (`ItemSlotLiquidOnly.CapacityLitres`, read by other code that fills a liquid slot) and its
  `Initialize` sets again. A Harmony postfix on the constructor (server side) sets the field and the
  slot to 950, so `Initialize` sets the same. A rack that holds more than 500 L when the switch is
  turned off keeps it, but takes no more until it is below 500.

Both are JSON patches, `assets/seraphhorizons/patches/tun-hydrateordiedrate.json` and
`tun-foodshelves.json`, `"side": "server"` (block types and recipes are loaded on the server, and the
client gets the blocks, attributes and creative stacks included, from it) and each `dependsOn` its
mod. With a switch off the system empties that patch file in `Start`, before the patch loader runs in
`AssetsLoaded`, as for Age of Flax. If `BETunRack`, its constructor or its `int capacityLitres` field
is gone, the mod logs a warning and leaves the rack as Food Shelves ships it, block attribute included,
so the two never disagree. The switches that count are the server's.

### Map Reveal (`MapReveal`)

`/revealmap <radius>` shows on your world map (M) the terrain already generated within radius
chunks of you (1 to 256; a chunk is 32 blocks), without going there: what `/wgen pregen` made, or
what other players explored. It generates nothing: columns not generated yet are skipped. It is
for players in creative mode and for holders of the `controlserver` privilege (admins, as for
`/wgen pregen`); a survival player without it is refused. It works only in the main world, and
`/revealmap stop` stops it. A new `/revealmap` replaces the running one; leaving stops it.

The game draws its map only from chunks the client has loaded (`ChunkMapLayer`, VSEssentials),
caching the pieces in the client's map database (`Maps/<world id>.db`); its only map commands are
`/map purgedb`, `/map redraw` and `/worldmapsize`. So the two halves:

- **Server** (`MapReveal/Game/MapRevealServer.cs`): saves the world first (what `/autosavenow`
  does: everyone sees "Saving game world...."), so terrain generated or changed since the last
  autosave is in the savegame, and waits for the chunk thread to write it. A worker thread then
  reads each column from the savegame through a read-only connection of its own
  (`SavegameReader`): nothing is loaded into the world, no entity or block entity is created, and
  the server's own connection is not touched. It reads the map chunk's rain height map, skips a
  column that is missing or still part-way through world generation, and decodes only the chunks
  the surface falls in. Per block column it keeps what the map colours by, as the game's map does
  on the client: the top block (the one under snow), whether water is lake or shore, and the slope
  shading from the column's and its north-west neighbours' heights. Batches of 64 columns go to
  the player, at most one per 100 ms (about 640 columns a second; a typical column is well under
  1 KB), with the worker at most 4 batches ahead. Progress is reported in chat every 5 s, and the
  totals at the end.
- **Client** (`MapReveal/Game/MapRevealClient.cs`): turns each column into the map's 32 × 32
  pixels with the map's own colours and the game's shading arithmetic (`MapReveal/Core/TerrainShade.cs`,
  ported from `GenerateChunkImage`), and stores them in the map database, where explored chunks go.
  So they show like explored terrain, now and in later sessions, and a chunk the client later loads
  is redrawn from its own data as usual. The database belongs to the map's background thread, so
  this runs there, in a Harmony postfix on `ChunkMapLayer.OnOffThreadTick` (Harmony id
  `seraphhorizons.mapreveal`); chunks an open map is showing are then queued for the map to redraw.
  It uses `ChunkMapLayer`'s private `mapdb`, `chunksToGen`, `chunksToGenLock` and
  `curVisibleChunks`; if one is gone, the client logs a warning and ignores what it is sent.

With a colour-accurate world map (`colorAccurateWorldmap`), the server also sends each cell's
height and the client colours the block as the game does there; the climate tint of a region the
client has not loaded may differ from what exploring it would show.

The server's reader also uses game internals: `ChunkData.DecompressFrom` (by reflection), the
protobuf field numbers of a saved chunk's blocks (`SavegameReader.StoredChunk`, 1.22.7's
`ServerChunk`), and `ServerMain.chunkThread` to tell when a save is written (without it, it waits
3 s). If `DecompressFrom` is gone, the command says so and does nothing.

`MapReveal/Core/`: which columns, in what order (`RevealArea`), the sampling (`ColumnSampler`),
the shading (`TerrainShade`) and the wire format (`RevealCodec`). Game-independent, tested in
`tests/`. With the switch off there is no command; on a client, nothing is patched.

### Creative mod tabs (`CreativeModTabs`)

The game's own creative inventory. A button above the right-hand tab column, `Tabs: Default ⇄` /
`Tabs: Mod ⇄`, flips between the game's tabs (exactly as without the mod) and one tab per mod, each holding
every creative-listed stack of that mod once, in the game's order, whichever default tabs the mod put it
in. A stack belongs to the mod that owns its code's domain (a mod's several domains share a tab, e.g.
`ageofflax` is Age of Flax's); the base game's tab, "Vintage Story", comes first, then the mods by name.
All mod tabs are in one scrolling column on the right. Search covers the current tab, as in vanilla, and Tidy
Variants hides and groups there as in the default tabs. Each client keeps its choice and its last tab of each
kind in `ModConfig/seraphhorizons-creativemodtabs.json`.

Both sides take part: the server resolves a creative click by tab index and slot id, so the mod tabs are real
creative tabs on both sides. The server decides which domain goes to which mod (the two sides load different
mods), appends the tabs to its creative inventories and sends the list to each client; the client builds the
same tabs from it, checks them against the server's counts and hashes, and shows them in its own composer
next to the dialog. Off on a side means nothing is patched, added or shown there. The design, the hooks
(`GuiComposer.Compose` on the client, `InventoryPlayerCreative.UpdateFromWorld` on the server), how it stays
out of TooManyTabs' and Dovidarium's way, and the in-game checklist are in
`docs/variant-grouping/creative-mod-tabs.md`. `CreativeModTabs/Core/` is game-independent (the tab plan,
domain owners, the state file, the strip's scrolling) and tested in `tests/`.

### Clear weather and daytime on command (`ClearCommand`)

An admin command, not a change to another mod. It needs the `controlserver` privilege, as the
game's `/weather` does; of the game's default roles only `admin` has it.

- `/clear` sets the time to day, ends a temporal storm and clears the weather, once.
- `/clear stay` does the same and holds it: time stands still, the weather stays clear and no
  temporal storm comes, until `/clear stop`.
- `/clear stop` puts back what `stay` changed, as it was before.

`/clear stay` when it already holds, and `/clear stop` when nothing is held, say so and change
nothing. Any other word after `/clear` is an error.

**Time.** If the hour is between 8:00 and 16:00 it is left alone, otherwise the clock moves forward
to the next 12:00 (the game's `/time set day`; the clock never goes back). Hours scale with the
world's day length (`Core/ClearSkyPlan.cs`). Moving the clock is the same as `/time set`: crops,
cooking, spoilage and so on catch up with the skipped hours.

**Weather.** Each 512-block region has a weather pattern (clouds), a wind pattern and a weather
event (thunder, hail). Every loaded region gets the clear sky pattern and no event, at once, as
`/weather seti clearsky` and `/weather setev noevent` do, each with a full duration ahead. Wind is
left alone: a still or stormy wind is not rain, and windmills need it. Rain is not part of a region:
it is one noise over the whole world, shifted in time by the weather system's `RainCloudDaysOffset`.
`/clear` moves that offset on to the next dry spell (below 0.04, `/weather stoprain`'s threshold)
where every online player and the spawn are dry, a day long if one starts within 21 days, or else
the longest shorter one. Like `/weather stoprain` this moves the rain everywhere, not just there. If
the rain is fixed with `/weather setprecip`, `/clear` leaves it and says so. After `/clear` the
weather changes as usual.

**Temporal storms.** A storm that is on ends, as when it runs out (half its creatures leave and the
next storm is scheduled from now, a full interval away). A storm the game is about to announce or
has announced (within 0.35 days) is skipped the same way: the next one is scheduled a full interval
from now. A storm further off is left as scheduled. Both go through the game's own storm tick
(`SystemTemporalStability.onTempStormTick`), with its "waning" and "imminent" messages left out.
With temporal storms off in the world settings, or in a creative world, there is nothing to do.

**Holding it (`stay`).** The lock is the game's own switches, as the matching commands set them:

| Held | As | Put back by `stop` |
|---|---|---|
| no rain anywhere | `/weather setprecip -1` | the override it had (`/weather setprecip`), or none |
| patterns and events stay as they are | `/weather acp off` | auto-changing on or off, as it was |
| time stands still | the calendar's `baseline` time speed (`/time speed`, `/time stop`) set so that all the speed modifiers sum to 0 | `baseline` as it was, or removed if there was none |
| the next temporal storm as far off as at the lock | its scheduled day, kept that many days ahead | that many days ahead of the time at `stop` |

The temporal storm world setting is never changed, so storms switched off stay off. Every second
the lock is enforced again: every loaded region clear (a region loaded since comes up clear within
the second), no override other than -1, auto-changing off, a storm that was started ended. Time is
held as soon as any speed modifier changes (a postfix on `GameCalendar.CalculateCurrentTimeSpeed`),
so sleeping players, another speed modifier or `/time resume` do not move the clock. The
lock and what it replaced are kept in the savegame (`seraphhorizons:clearlock`) and reapplied on
load, so it outlasts a restart; the game saves the override and the time speed itself, but not
auto-changing patterns.

Time standing still stops whatever the game times by its calendar, as `/time stop` does: for
example crops on farmland, barrels, pit kilns, food spoiling and other item transitions (drying,
curing), the season, the sun and the moon, snow accumulation, and the next temporal storm. Whatever
runs on real seconds goes on: for example a firepit's or bloomery's cooking, mechanical power, and
creatures moving. Wind stays as it was, since patterns do not change.

With the switch off there is no `/clear`, and a lock left in the savegame is released when the
server starts. If the mod is removed while a lock holds, the `baseline` speed stays where the lock
left it and time stays still: `/time speed 60` (or the old value) puts it back, and
`/weather setprecipa` and `/weather acp on` the weather.

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
cart reach's entity matching and reach rule, where the chopper drops its piles, and `/clear`'s
daytime, dry-spell search and saved lock (`Core/`), Map Reveal's `Core/`, and the creative mod
tabs' plan, domain owners, state file and strip scrolling (`CreativeModTabs/Core/`).
`dotnet test mods-src/seraphhorizons/tests`.

`tests/PackTests/ClearCommandScenarios.cs` (Atlas, a `surviveandbuild` world so temporal storms
run) requires `/clear` to be this mod's and `controlserver` the admin role's alone, and a player
with the `suplayer` role refused all three forms. At night, with thunder over cumulonimbus in every
region, rain at the spawn and a temporal storm on, `/clear` must set day, end the storm (the next
one more than 0.35 days off), clear every loaded region for a while, dry the spawn and hold nothing.
A storm 0.1 days off must be skipped and one 3 days off left alone. With an override, a `baseline`
speed and a storm 3 days off set first, `/clear stay` must hold it all (no time passing, a sleeping
speed-up cancelled as it is set, `/weather`-style changes undone within the second, regions loaded
after a teleport clear, a storm started by hand ended), survive a reload of its savegame entry, say
so when run twice, and `/clear stop` must put back the override, auto-changing patterns, every
speed modifier and the storm's distance. `ClearCommandOffScenarios` boots a server with the switch
off and requires no `/clear`. A real restart is not run: Atlas boots each class once, so the
reload reads the lock back from the savegame data in the same server.

`tests/PackTests/SeraphHorizonsModScenarios.cs` (Atlas) places a Cornish boiler, calls `Explode()` with
the lid shut and with it open, and requires the boiler still standing with its lid open. It
requires `BlowSound` to be ExpandedLib's `ExSounds.MediumExplosion` and present in the assets. It also
requires every `LangEdits` passage reworded, and an edit set for every language ppex ships: when
either fails after a ppex update, match the edits to ppex's new text or add the new language. The
same goes for `ChimneyVentText.LangEdits`, whose passages must also quote ppex's look-at line for a
venting chimney. `WellShaftText.LangEdits` must be reworded too, quote Hydrate or Diedrate's
look-at line for an empty well, and give the depths and liters of its default settings: when that
fails after a Hydrate or Diedrate update, match the edits to its new text or settings. A second
scenario stands wells in the air and requires what the page says of them: a one-block shaft holds
5 levels in rock, fireclay bricks, uneven bricks and aged ashlar, 7 in bricks and 10 in ashlar; one
rock block in an ashlar shaft caps it at 5 from the third level and at 7 from the eighth; and four
springs under a 2x2 shaft hold nothing. When it fails, the rules changed: reword the edits.

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

It also requires each Immersive Woodworking frame to have the assembled creative stack next to the
plain one, named after the machine, and places both: the plain frame is incomplete, and the
assembled one is complete with a steel head or blade kit and drops its parts.

`tests/PackTests/AgeOfFlaxRebalanceScenarios.cs` (Atlas) reads the loaded ripples' and hatchels'
yields (what the tools use, set from the patched balance file), requires steel and no iron in the
advanced recipes and both fats in every break, rolls a flax plant's drops at stages 9, 8 and 5 on
farmland (seeds and bundles), checks the seeds in the blocktype's drops, and requires every
`LangEdits` passage reworded. When it fails after an Age of Flax update, match the patches and edits
to the new files. `AgeOfFlaxRebalanceOffScenarios` boots a server with the switch off and requires
Age of Flax as it ships.

`tests/PackTests/ChopperOutputScenarios.cs` (Atlas) builds a chopper frame of each facing on a
granite floor, chops ten oak logs through the chopper's own `GetChopBatch` and `EjectBatch`, and
requires every piece at rest in the cell in front, at least 0.15 from its edges, and the patch
applied. With a hopper sunk into the floor there (a chest under it) it requires the hopper to catch
every piece, for two facings. It also requires the cell in front, and the one above it, to be
outside the chopper's footprint (`GetCells`). When it fails after an Immersive Woodworking update,
check whether `EjectBatch` or the footprint changed. `ChopperOutputOffScenarios` boots a server with
the switch off and requires the chopper unpatched and throwing its batch past the cell in front.

`tests/PackTests/HydrationCoverageScenarios.cs` (Atlas) requires a `hydration` attribute on every
food the server loads: anything eaten, used as a meal ingredient or drunk. An explicit 0 counts. When
it fails after a mod is added or updated, it lists the foods to give a value in `patches/hydration-*.json`.

`tests/PackTests/TunScenarios.cs` (Atlas) requires Hydrate or Diedrate's tun with no recipe, not in
the creative inventory and excluded from the handbook, and one placed still Hydrate or Diedrate's
block entity, taking 950 L of water. It requires the tun rack's block, field and liquid slot all at
950 L, the constructor patched, and a placed rack with a tun taking 950 L. `TunOffScenarios` boots a
server with both switches off and requires both tuns as they ship (the rack at 500 L in all three
places). When it fails after a Food Shelves update, check whether `BETunRack` still keeps its own
capacity, and whether the block's `capacityLitres` moved.

`tests/PackTests/BarrelRackKegsScenarios.cs` (Atlas) places a barrel rack and has a player
right-click it through the rack block's own `OnBlockInteractStart`: an untapped keg holding 80 L
goes in (the liquid in the rack, none in the item), fills to 100 L and no further, and comes back
out holding 100 L; a barrel still holds 50 L and stays put while full; the perish speed in the
rack with each keg is 0.15 and 0.65 of a barrel's; and breaking the rack drops a tapped keg with
its 30 L in it. It also requires the restriction to mark both kegs and still the barrel, and every `LangEdits`
passage reworded. When it
fails after a Food Shelves or Hydrate or Diedrate update, check the restriction file, `BEBarrelRack`
and Hydrate's `ContainersConfig`. `BarrelRackKegsOffScenarios` boots a server with the switch off
and requires the rack to refuse a keg. The rack's look and the client's side of the interactions
are checked by hand in the game.

`tests/PackTests/MapRevealScenarios.cs` (Atlas) has a test player run `/revealmap`, decodes what
it is sent as the client would, and requires it to agree with the loaded chunks (so the savegame
reader reads the blocks the game does: when it fails after a game update, check `StoredChunk`'s
field numbers and `ChunkData.DecompressFrom`), the columns not generated yet skipped and still
not generated afterwards, the command refused in survival without `controlserver` and allowed in
creative, `stop`, and the shading
helpers equal to the game's `BlurTool.Blur` and `ColorUtil.ColorMultiply3Clamped`.
`MapRevealOffScenarios` boots a server with the switch off and requires no command. The client
half (drawing into the map and its database) needs a game client and is not tested.

`tests/PackTests/CreativeModTabsScenarios.cs` (Atlas) builds a creative inventory on the server with
the whole pack: the default tabs are the same as without the tweak, the mod tabs follow with every
creative stack in exactly one of them, the base game's tab first, `ageofflax`, `bomb` and
`oils` under their owning mods. The packet survives protobuf-net, and tabs a client builds from it have,
slot for slot, what the server's inventory returns for a click there (on the same world: a client with
other mods is the count and hash check's job). `CreativeModTabsOffScenarios` boots
with the switch off and requires no mod tabs. The GUI is checked by hand (the doc's checklist).

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
