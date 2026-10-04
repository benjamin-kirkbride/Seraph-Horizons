# Seraph Horizons

The pack's own mod, named after the pack: every install of the pack downloads it, so its ModDB
page shows the pack's download count and is where people browsing the ModDB find the pack. Its
modid is the pack's id, so the pack's meta-mod (`packtool assemble`) is `seraphhorizonspack`.

It is a code mod holding the pack's own tweaks: gameplay changes to other mods, Tidy Variants, which
tidies the creative inventory and the handbook, Map Reveal, which shows already generated
terrain on the world map, and the bucking sawmill, a machine that cuts tree trunks into logs. These are choices for this pack, not bug fixes, so
they live together here and not in a mod each. Every tweak has its own switch in
`ModConfig/seraphhorizons.json` (all on by default). A tweak whose mod is not installed is skipped.
One whose mod has changed shape logs a warning and leaves that mod alone.

Only the game's own assemblies are referenced at build time: each tweak to another mod finds what
it patches by name, so the mod builds from the game alone (`mod-release.yml` needs nothing else).

`"side": "Universal"`, required on the client. The server does the boiler behavior, drops the
chopper's output, feeds the creative steam source and runs `/clear`; Tidy Variants, cart reach and
the creative search tweaks run on the client; Map Reveal has a half on each side, and the creative mod tabs need both. The
client needs the mod because the steam source is a block with its own classes: the game cannot
build a block whose class it does not know, so a client without the mod could not join a server
that has it (a server with the steam source switched off, or without ppex, has no such block).
Unified woodworking runs on both sides too (the server does the work, the client draws the
splitting block, predicts its upgrades and arranges the handbook), and its splitting block has a
block entity behavior of this mod, which a client needs in the same way. So are the bucking
sawmill's blocks: the server runs the mill, the client draws its moving parts.

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

### One olla: Primitive Survival's irrigation vessel is retired (`IrrigationVesselRetired`)

The pack had two buried clay pots that water the farmland around them: Primitive Survival's
irrigation vessel (`primitivesurvival` 5.1.4, `primitivesurvival:irrigationvessel-*`, 50 L, ten
colours, made in the grid by chiselling a fired storage vessel and turned back into one with resin)
and Olla's olla (`olla` 1.4.0, `olla:olla-raw-*` clay formed from 16 clay and fired in a pit kiln
into `olla:olla-fired-*`, 60 L, watering the 5 x 5 around it). The codes do not collide; the pack
keeps Olla's. Primitive Survival has no setting for its vessel, so:

- its four grid recipes are disabled (`enabled: false`): the two that make it and the two that turn
  it back into a storage vessel, whose handbook page would otherwise list a block nobody can get;
- it is left out of the creative inventory (its `creativeinventory` removed) and the handbook
  (`attributes.handbook.exclude`);
- BetterRuins' Primitive Survival compatibility patch adds its nine coloured vessels to the ruins'
  clay loot (`game:stackrandomizer-clayproducts`, `attributesByType.*-clayproducts.stacks`). On the
  server, in `AssetsLoaded` after the patch loader and before the item types are read, the tweak takes
  every `primitivesurvival:irrigationvessel-*` stack out of that list again, as Assembled Machines
  edits its blocktypes. The vessels are at the end of the list, so BetterRuins' ConfigKit loot
  settings, which address its own stacks by index, still point at the same stacks. If the loot item is
  not as expected or holds no vessel, it logs a warning and the loot stays as it is.

The block type stays registered, so a vessel already placed in a world keeps its water and still
works, and breaking it still drops it. It can be placed again, but no new one can be had.

The recipes and the creative and handbook changes are a JSON patch,
`assets/seraphhorizons/patches/irrigationvessel-primitivesurvival.json`, `"side": "server"` and
`dependsOn` primitivesurvival. With the switch off, or without Primitive Survival, the system empties
that patch file in `Start`, as for Hydrate or Diedrate's tun, and the loot is left alone. The switch
that counts is the server's.

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
`Tabs: Mod ⇄`, flips that column between the game's tabs (exactly as without the mod) and one tab per mod, each
holding every creative-listed stack of that mod once, in the game's order, whichever default tabs the mod put it
in. The left column keeps its default tabs in both modes. A stack belongs to the mod that owns its code's domain
(a mod's several domains share a tab, e.g. `ageofflax` is Age of Flax's); the base game's tab, "Vintage Story",
comes first, then the mods by name. Both columns are the game's own, so TooManyTabs scrolls the mod tabs exactly
as it scrolls the default ones. Search covers the current tab, as in vanilla, and Tidy Variants hides and groups
there as in the default tabs. Each client keeps its choice and its last tab of each mode in
`ModConfig/seraphhorizons-creativemodtabs.json`.

Both sides take part: the server resolves a creative click by tab index and slot id, so the mod tabs are real
creative tabs on both sides. The server decides which domain goes to which mod (the two sides load different
mods), appends the tabs to its creative inventories and sends the list to each client; the client builds the
same tabs from it, checks them against the server's counts and hashes, and in mod mode gives the inventory a
second tab list (the left column's default tabs plus the mod tabs, ordered so that the game's own layout puts the
mod tabs in the right column) and names them through lang entries. Off on a side means nothing is patched, added
or shown there. The design, the hooks (`GuiComposer.Compose` on the client, `InventoryPlayerCreative.UpdateFromWorld`
on the server), how it fits TooManyTabs and Dovidarium, and the in-game checklist are in
`docs/variant-grouping/creative-mod-tabs.md`. `CreativeModTabs/Core/` is game-independent (the tab plan, domain
owners, the state file, mod mode's tab layout) and tested in `tests/`.

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

### One woodworking system (`UnifiedWoodworking`)

Immersive Woodworking (`immersivewoodworking` 1.3.11) and Logging Expanded (`loggingmod` 0.3.6)
each bring a chopping station, a sawhorse and a handbook guide for the same jobs. This tweak makes
them one system: Immersive Woodworking's chopping block is the splitting block, with Logging
Expanded's four tiers and looks; Logging Expanded's three sawhorses are the only sawhorses and
also saw support beams; the stations either mod had for the same job are retired; and one
handbook guide covers it all. The code is in `Woodworking/`, one class per part.

**The splitting block** is Immersive Woodworking's `immersivewoodworking:choppingblock` (its wood in
the stack's and block entity's `wood` and `woodDomain`), named "Splitting block". Everything it
does as a chopping block stays: lay a log on it, hold right click with an axe, and the log splits
into 2 half-logs and each half-log into firewood; firewood splits into 2 sticks; a maul splits a
whole log in one pass; Ctrl+right click takes wood back, and Shift+right click sticks an axe in
it. On top of that:

- **tiers**: primitive, debarked, bound and advanced, Logging Expanded's four splitting log stages.
  The tier is a string attribute, `seraphhorizons:tier`, on the item stack and in the block entity's
  saved attributes (a block entity behavior, `seraphhorizons.SplittingBlockTier`, added to the
  blocktype); none, or a name it does not know, is primitive, so every chopping block made before
  is one. The stack is named for the tier and wood, "Oak bound splitting block" (a postfix on
  `GetHeldItemName`, with Immersive Woodworking's own wood wording).
- **making**: right-click an upright placed log (`log-placed-<wood>-ud`) with any axe, as for
  Logging Expanded's splitting log: a prefix on its `BlockBehaviorLogConvert.OnBlockInteractStart`
  makes a primitive splitting block of that log's wood and domain instead, for 1 axe durability.
  A sideways log does nothing. There is no grid recipe: a prefix skips Immersive Woodworking's
  `RegisterChoppingBlockRecipes` (one recipe per wood, made on the server in AssetsFinalize).
  Breaking the block gives it back, wood and tier kept (a postfix on `OnPickBlock`, which Immersive
  Woodworking's `GetDrops` drops). A block with no wood (placed from the creative or handbook
  stack, from `/giveblock`, or the bed of a chopper assembled before that bed had a wood) gets a
  plain stack from `GetDrops` instead, so a postfix there puts the tier on it too; such a block,
  placed or held, looks like oak, the wood Immersive Woodworking falls back to.
- **upgrades**, each to the next tier, only on an empty block (nothing on it, no axe stuck in it)
  and with neither Shift nor Ctrl held, which is where Immersive Woodworking's own interactions do
  nothing with these items:
  - debark: hold right click with Immersive Woodworking's bark spud, or with an axe and a hammer
    in the offhand, as long as Immersive Woodworking takes to debark a log with that tool (its
    `DebarkSeconds`, 1.5 s, over the spud's sharpness and metal factors, or over
    `AxeHammerDebarkSpeedMultiplier`, 0.3, for the axe: 5 s), with its debarking animation and
    sound. The tool, and the hammer with an axe, lose `DebarkDurabilityPerLog` (1). The block's
    wood drops one log's bark on top, rolled as on the sawhorses below.
  - bind: right-click with 2 iron hoops (`game:hoop-iron`), at once.
  - nail: hold right click 3 s with 8 iron nails and strips (`game:metalnailsandstrips-iron`) and
    a hammer in the offhand, which loses 1; Logging Expanded's costs.

  A player in creative pays nothing, and a Ctrl + right click (the game's `ctrl` key, sprint by
  default; not with Shift) on an empty block below the advanced tier makes the next tier at once,
  whatever is held, taking and dropping nothing (no bark from the debark), as the game's water
  wheel (below). On an empty block Immersive Woodworking's Ctrl takes nothing back, so the only
  thing this replaces there is laying what is held, which a plain right click still does; on a
  block with something on it or an axe stuck in it, Ctrl takes it back as before. Prefixes on `BlockChoppingBlock`'s `OnBlockInteractStart`,
  `Step`, `Stop` and `Cancel`, on both sides, take the interaction when an upgrade applies: the
  client drives the hold, the server makes the upgrade when it is released at its full time (less
  0.1 s, as Logging Expanded counts its holds). The server counts that time through Immersive
  Woodworking's `HonestHoldSeconds`, as its own holds do: no more than the time since the hold began
  on the server, so a modified client cannot claim a finished hold at once. The server makes the
  creative upgrade too, reading the game mode from its own player data, not the client's word. A
  client postfix on `GetPlacedBlockInteractionHelp` adds the next upgrade to an empty block's help,
  and for a player in creative the Ctrl line (`seraphhorizons:woodworking-help-creative-upgrade`).
- **yields**: 6 firewood per log on the primitive, debarked and bound tiers, 8 on the advanced
  (Logging Expanded's 6 and 8; Immersive Woodworking's default is 8), so a half-log gives 3 or 4.
  Immersive Woodworking's `FirewoodPerLog` is set to 6 (below), and a server prefix on
  `BlockEntityChoppingBlock.Chop` raises it to 8 for an advanced block's chop, with a finalizer
  that puts it back. The maul gives the same per log. Sticks per firewood do not depend on the tier.
- **not choppable itself**: Immersive Woodworking counts a chopping block as a log, so one could be
  laid on another and chopped, losing the iron of a bound or advanced one. A postfix on its
  `IsChoppable`, a prefix on `TryPut` (in case the JIT inlined the former there) and a prefix on
  `AutoRestockUtil.FindRestockSlot`, which refills the block from the hotbar after a chop, leave
  splitting blocks out. The last is on both sides: the client predicts the restock, and would
  otherwise swing again at an empty block.
- **look**, on the client: Logging Expanded's model for the tier (`splittinglog`,
  `debarkedsplittinglog`, `boundsplittinglog`, `advancedsplittingblock`) in the block's own wood,
  placed, held and in inventories. The textures come from the wood the way Immersive Woodworking
  finds them for any wood (`block/wood/bark/<wood>` on the primitive's sides,
  `block/wood/debarked/<wood>` on the others', `block/wood/treetrunk/<wood>` on the primitive's
  top, each in the wood's domain and else the game's), not from Logging Expanded's per-wood tables,
  which name Wildcraft Trees textures the pack does not have; iron for the hoops and nails. A
  missing texture logs one warning and shows the unknown texture.
- **height**: the models are 15/16 tall, Immersive Woodworking's block 11/16. The collision box
  rises to 15/16 and the selection box to 1.25 (Immersive Woodworking's 5/16 over the top), and a
  prefix on the block entity's `OnTesselation` draws the model and lifts what lies on the block,
  and a stuck axe, by 4/16. Immersive Woodworking's static mesh builders, which its chopper shares,
  are left alone. A model is built on the main thread the first time it is wanted (the
  tesselation thread may not add textures to the atlas); until then the block draws as Immersive
  Woodworking's, and it is redrawn once the model is there.
- **the chopper's bed**: Immersive Woodworking's mechanical chopper takes only an advanced
  splitting block as its bed, so its yield is that tier's (`ChopperFirewoodPerLog` is set to 8). A
  prefix on `BlockEntityChopper.TryAddPart`, on both sides (the client predicts the install),
  refuses a lower tier with an in-game error. The chopper keeps only the bed's wood, so a postfix on
  `BedStack` stamps the advanced tier on the bed that breaking the frame gives back (and oak on a
  bed without a wood, as Immersive Woodworking drew it). On the client,
  a prefix on `BuildBedMesh` draws the advanced model, squashed to 11/15 of its height to fit
  Immersive Woodworking's 11/16 bed (sunk 4/16 instead, its lower hoop would sit on the floor line
  and the model would poke into a hopper under the frame), and a postfix on `BlockChopper.OnLoaded`
  shows an advanced oak bed in the frame's interaction help. The assembled creative chopper
  (`AssembledMachinesInCreative`) has an oak bed, which comes back advanced too.

**The sawhorses** are Logging Expanded's primitive, standard and advanced ones (`sawhorse`,
`sawhorsestandard`, `sawhorseadvanced`), which keep all they do: load a debranched trunk or up to 16
logs, and each hold (0.75 s) works one log: an axe takes it off, a saw cuts 9, 12 or 18 boards, and
an axe with a hammer in the offhand debarks it (the advanced one 3 for every 2 a trunk holds). They
also take over what Immersive Woodworking's sawhorse did:

- **support beams**: Shift + saw cuts 2 / 2 / 3 per log by tier (Logging Expanded's splitting logs
  gave 2 and 3), with the boards' hold, sound and wear. The beam is `supportbeam-<wood>` in the
  log's domain, else the game's; a wood with none is sawn into boards, with one warning, and the
  server lists every such wood at startup in one notification (the pack has one, Material Needs'
  `darkaged`). A sawhorse loaded with firewood makes no beams.
- **the bark spud** debarks, as the axe and hammer do at that tier, for `DebarkDurabilityPerLog` per
  log.
- **bark**: debarking with either drops Immersive Woodworking's bark, one roll per log taken off
  the sawhorse (the advanced sawhorse's 2-for-3 is 2 rolls: the third log is Logging Expanded's
  bonus, not one that had bark), thrown toward the player with the logs. A roll is Immersive
  Woodworking's own (`BarkDrops.cs`, after its sawhorse's `DropRolledBark`): the kind from its bark
  table for the species (`config/barkdrops.json`, else ordinary bark) at the tool set's chance
  multiplier, a spud's `BarkChanceMultSpud` (1.0) plus `BarkChanceMultSpudSteelBonus` (0.25) times
  its metal tier fraction, an axe and hammer's `BarkChanceMultAxeHammer` (0.75); the count is the
  table's for the species, else `BarkPerLog` (3).

The work is done by a server prefix and postfix on `BlockSawhorse.ProcessWithTool` and
`BlockSawhorseAdvanced.ProcessWithTool` (which overrides it without calling it; the standard
sawhorse inherits the primitive's), when a hold completes. The spud has no tool type, and a hold
only starts for a tool, so a postfix on the hold's eligibility, a lambda the compiler put in
`BlockWorkstation+<>c` (found as its one `bool (IWorldAccessor, IPlayer, BlockSelection)` method),
lets it in on a loaded sawhorse, on both sides. That name is the likeliest thing to break, and
only the spud needs it: if it is not found, the spud does nothing on a sawhorse, with one warning,
and the rest of the tweak runs. A client postfix on `BlockSawhorse.GetPlacedBlockInteractionHelp`
adds the beam and spud lines.

**Creative upgrades.** The game's water wheel (`RightClickConstruction.tryConsumeIngredients` in
`VSSurvivalMod`, 1.22.7) lets a player in creative mode who right-clicks with Ctrl held build its
next stage with nothing in hand (`CurrentGameMode == Creative && Controls.CtrlKey`); its help shows
nothing for it. The stations do the same (`Core/CreativeUpgrades.cs`), with Ctrl and not Shift,
which Immersive Woodworking's chopping block reads first, and with a help line
(`seraphhorizons:woodworking-help-creative-upgrade`, Ctrl + right click) shown to a player in
creative only:

- the splitting block, one tier per click on an empty block (above);
- Logging Expanded's frames, each into the next stage its own interaction makes with items: the
  large stick frame into the primitive sawhorse, the board frame into a standard sawhorse frame,
  that into a copper standard sawhorse, advanced sawhorse frames A into B and B into the advanced
  sawhorse, the storage rack frame into a trunk storage rack, the heating rack frame into a trunk
  heating rack, the stick storage frame into a stick storage. A frame with two ways on takes the
  one its help lists first, the station it is a frame of; the other ways (a fired bowl on the stick
  frame for a heating rack frame, 4 iron rods on the board frame for a storage rack frame, 2 iron
  plates on the standard sawhorse frame for the advanced frames) still take their items.

  Logging Expanded builds the stage itself (`Woodworking/FrameUpgrades.cs`): on the server, a
  prefix on the frame's `OnBlockInteractStart` puts the stage's items in the player's hands (and an
  iron hammer in the offhand where the stage takes one), runs Logging Expanded's completion (its
  `OnBlockInteractStart` for a click, `OnBlockInteractStop` at the full 3 s for a hold), then puts
  the hands back as they were. So the block, the frame's wood and facing, a two-block frame's second
  block, the space check and the sound are all Logging Expanded's, and nothing is taken. The client
  only takes the click; the server reads the game mode from its own player data. A frame class
  that is not found gets no shortcut, with one warning, and the rest of the tweak runs.

Left out: a finished sawhorse has no next stage (each tier is built from its own frames, not from
the tier below); the stick pile and board pile a knife or hammer turns into the first frames are
the game's ground storage, and the hand tool is all they take besides what is in the pile; and
Immersive Woodworking's chopper and sawmill are assembled from parts that each are an item of their
own (a bed of some wood, a drive), where Ctrl + right click already takes a part back, and the
assembled ones are in the creative inventory (`AssembledMachinesInCreative`).

**Retired**: Logging Expanded's four splitting logs, Immersive Woodworking's sawhorse, its pit saw
and its pit saw blade (which only worked at that sawhorse). Their block and item types are taken
out of the creative inventory and the handbook (creative tabs and stacks removed,
`attributes.handbook.exclude`) on the server, before the game reads them, but stay registered, so
a world that has one, and code that looks one up, keep working (placed splitting logs are not
migrated). Nothing makes them: the pit saw's recipes go with `RemovePitSaw`, the axe on a log makes
the splitting block, and Immersive Woodworking's sawhorse, which is built by right-clicking a
block's top with a stick, loses that: the operation of its `patches/sawhorse_build_behavior.json`
that gives the stick its `iw-sawhorsebuild` behavior is removed before the game's patch loader
runs.

**Immersive Woodworking's settings** are set in memory on the server, in `Start`: after its
`StartPre` has loaded (and written) its config file, before its `AssetsLoaded` strips recipes by
them, and before its `StartServerSide` takes the copy it sends each joining client, so clients get
them too. Nothing is written to the player's file.

| Setting | Immersive Woodworking | Here |
|---|---|---|
| `RemovePitSaw` | false | true |
| `CraftableSawhorse` | false | false, whatever the file says |
| `AllowKnifeDebark` | true | false (the knife only debarked on the retired sawhorse; the handbook's bark table drops its row) |
| `FirewoodPerLog` | 8 | 6 |
| `FirewoodDropCount` | 4 | at most 6 (Immersive Woodworking requires at most `FirewoodPerLog`) |
| `ChopperFirewoodPerLog` | -1 (the hand's `FirewoodPerLog`) | 8 |

They are not in `pack/config`: that ships only in the `.cairn` file, so a dedicated server from
the server zip, or a player with the meta-mod, would not get them, and a player's own file could
undo them; the tweak needs them wherever it runs, and only then. Logging Expanded's settings need
no change: its splitting log yields go with its splitting logs, and its sawhorses keep their own.

**The handbook** has one woodworking guide of six pages in place of the two mods' guides
("Crafting Mechanic: Immersive Woodworking" and "Crafting Mechanic: Logging Expanded"): an overview,
"Woodworking", and the chapters Trunks, Sawhorses, Splitting block, Bark and Machines. The pages
are this mod's (`assets/seraphhorizons/config/handbook/woodworking-*.json`, text in `lang/en.json`);
the overview keeps Immersive Woodworking's page code, `craftinginfo-woodworking`, so every link its
item pages make to it opens the new one. A client handler on
`ModSystemSurvivalHandbook.OnInitCustomPages` arranges the page list each time the handbook builds
it (`Core/WoodworkingGuidePages.cs`): it drops the two old guides, each told by page code and title
key (Logging Expanded's code is a generic `introduction`, and Immersive Woodworking's is now the
overview's too), and puts the six, in reading order, where the first of them stood. The handler is
added whatever the switch, and with the tweak not running it drops the six instead, so the pages
this mod ships whatever the switch never show next to the mods' own.

Both mods' woodworking text is rewritten to match and to link to the guide, and nothing names a
retired station:

- Immersive Woodworking's text it composes onto the game's logs, boards, beams, firewood, sticks and
  debarked logs (`iwsource-*`), and the sections of the splitting block, half-log, maul, chopper,
  bark spud and tanning bark, are replaced whole (`LangText.Replace`). Immersive Woodworking
  composes some of them at display time or on `LevelFinalize`, after the edits, so its own sections
  show the new text. The chopping block's name is an exact-passage edit, and the tiers' names are
  this mod's (`seraphhorizons:splittingblock-*`).
- Logging Expanded's block texts are pattern keys (`loggingmod:block-handbooktext-loggingmod:sawhorse-*`),
  which the game keeps apart from the exact entries: one `*` at the end in a wildcard table, any
  other in a regex table (`Core/LangPatternKeys.cs`). They are replaced where the game keeps them,
  `TranslationService`'s private `wildcardCache` and `regexCache` (1.22.7), found by reflection; if
  those are not there, one warning, and Logging Expanded's text stays.
- Two of Logging Expanded's numbers are put right on the way: the advanced sawhorse's last step
  takes 20 nails, not 10, and the trunk storage rack 10, not 12.
- English only. Every Immersive Woodworking key whose English changes is removed from its
  translations (de, pt-br, ru, zh-cn; `LangText.Remove`), so those languages show the new English
  instead of instructions that are now wrong. So are the five sentences it composes into the
  splitting block's section (`choppingblock-handbook-frag-*`, `WoodworkingText.ComposedFragments`),
  or a translated sentence would stand in the English section. The sections' titles stay
  translated. Logging Expanded ships English only.

The text is changed on each side in AssetsLoaded when the tweak runs there (on a client, when the
server runs it; below).

The recipe export lists the handbook a player sees, but the page list is arranged on the client.
So the server lists the guide pages a player does not see in `ObjectCache["handbook-hiddenGuides"]`
as `(pageCode, title lang key)` tuples (`WoodworkingGuidePages.Hidden`), and `tools/recipe-export`
(`Items/Guides.cs`) leaves out a guide listed there: with the tweak running, the two replaced
guides; without it, the six pages this mod ships whatever the switch. (Emptying their files on the
server does not last: the game unloads config assets after startup and reads them from the mod
again.)

**How it runs.** `UnifiedWoodworking.cs` runs the parts (`WoodworkingPart`) in the game's phase
order. The server decides: it runs the tweak with the switch on and both mods installed. It binds
every part before it applies any: each finds every type, member and asset it will patch, call or
edit, by name, and changes nothing. Then each part applies itself in `Start`, every patch
included. If a part fails to bind, or throws while it is applied, the mod logs one warning naming
it, unpatches, puts back what the applied parts changed (Immersive Woodworking's settings, the
sawhorse build patch, the handbook handler) and runs the tweak as switched off, so both mods stay
as they ship: half the tweak could leave a world with no way to make a splitting block. (The spud's
lambda, above, is the one exception.) It runs last in the mod's `Start` and catches its own
failures, so the other tweaks' work there (Age of Flax, Food Hydration) always happens.

The blocktype edits (the splitting block's tier behavior and boxes, the retired types' hiding) run
after the game's patch loader, so another mod's JSON patch may have changed an asset since `Bind`
checked it. Each edit then stands alone: one that fails (or throws) leaves its asset as patched,
with one warning saying what that means in the game. A retired type stays in the creative
inventory and the handbook, though nothing makes it. Without the tier behavior no splitting block
has a tier: none is upgraded, each chops 6 firewood per log and keeps Immersive Woodworking's
placed look, and the chopper takes a splitting block of any tier as its bed rather than none. A
hook after `Start` that throws is logged and the rest runs.

Clients follow the server. In `Start` the server writes whether the tweak runs to the world
config (`seraphhorizons:unifiedWoodworking`), which the game sends a client in its server
identification, before the client starts its mods; in singleplayer too. A client runs the tweak
exactly when that says so, whatever its own `UnifiedWoodworking` setting (a mismatch logs one
notification), so its interaction predictions, the looks, the help lines, the text and the
handbook all match what the server does, from the client's `Start` on: nothing waits for a late
packet, and nothing follows the client's own switch. A server without this key (an older build of
this mod) counts as not running it. If the server runs it and the client then fails to bind (a
different Immersive Woodworking or Logging Expanded on the client), the client logs the warning and
runs nothing: the server's tweak still works, but that client predicts, draws and explains both
mods as they ship. The key stays in the save; the server rewrites it at every start.

Server patches go under `seraphhorizons.woodworking.server`, client ones under
`seraphhorizons.woodworking`: undoing a failed apply unpatches no other tweak, and singleplayer's
server unpatch leaves the client's alone. In singleplayer each side keeps the statics its patches
read until the next bind sets them again, as the other side's patches may still run after one side
disposes. The block entity behavior class is registered on both sides whatever the switch, as the
server decides whether the blocktype carries it. With the tweak not running nothing is patched,
set or hidden.

The rules that need no game types are in `Core/`, and `tests/` runs them without the game:
`SplittingBlockTier.cs` (the attribute key, tier names, yields, which tier the chopper takes),
`SplittingBlockRules.cs` (which upgrade what is held makes, and its cost),
`CreativeUpgrades.cs` (when a click is the creative shortcut, and each frame's stage), `SawhorseWork.cs` (which
tool set does what on a sawhorse, beams per tier, bark rolls per debark), `WoodworkingGuidePages.cs`
(the handbook's page list, and the guides the export leaves out), `LangEntries.cs` and `LangPatternKeys.cs` (whole-entry lang changes and
where the game keeps a pattern key).

Known limits:

- Immersive Woodworking's chop swing is aimed at its own 11/16 block, so the axe lands about 4/16
  below the lifted log.
- The upgrade holds show no progress bar (and the debark no bark particles).
- The creative inventory has only primitive splitting blocks (Immersive Woodworking's stacks, one
  per wood, carry no tier); the others are made by upgrading.
- Immersive Woodworking's clay-coating recipe still lists pit saw blades: it coats a blade there
  already is, and makes none.
- A wood with no `supportbeam-<wood>` saws into boards with Shift too (Material Needs' `darkaged`).
- Logging Expanded's sawhorse holds only logs with their bark on, so beams on a sawhorse come from
  logs, not debarked logs (the automated sawmill takes those).

### Bucking sawmill (`BuckingSawmill`, `BuckingSawmillSettings`)

Immersive Woodworking (`immersivewoodworking`, 1.3.11) and Logging Expanded (`loggingmod`, 0.3.6).
A machine of this mod's own: a mechanically powered pair of drag saws that cross-cut Logging
Expanded tree trunks into logs. Its frame is crafted from two Immersive Woodworking sawmill frames
and four support beams, placed as a six by three by four multiblock, and fitted in the world with
Immersive Woodworking's sawmill parts (two sashes, a crankshaft, feed levers and two blade kits).
An axle drives it at the far end; trunks go on by hand or from a Trunk Storage Rack under the
axle, and the logs come out of the near end. While it turns it never stops: the saws sink through
the trunk, a windlass winds them back up, and a new trunk can go on only at the top of the cycle.

`BuckingSawmillSettings` holds its figures (shaft load, turns per log, logs per stored log, blade
wear, whether it pulls from a rack). With the switch off, or either mod missing, its blocks and
recipe are left out before the game loads them, as the creative steam source's is; neither mod is
referenced at build time, and Logging Expanded is reached by reflection.

Everything else is in [`BuckingSawmill/README.md`](BuckingSawmill/README.md): the blocks, the rig
file that ties the model to the code, the cycle, the settings, the generated model and how to
regenerate it, and its tests. Most of the model was made for this mod; its gears, saw blades, saw
heads and cranks are from Immersive Woodworking's sawmill model by Bobrik00, used with the
author's permission and not covered by the repository's license (`CREDITS.md`).

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

### The creative inventory keeps your place (`CreativeKeepsPlace`)

Closing the creative inventory empties its search box (`GuiDialogInventory.OnGuiClosed` sets it to
`""`, which runs the search and scrolls the grid to the top), so every reopen starts at the top of
the whole tab. With this tweak, reopening it in the same session puts back the search text, and the
scroll position too if the same tab is shown (clamped to the list's height). The selected tab was
already kept by the game, the creative mod tabs keep their mode and tab, and Tidy Variants keeps its
expanded groups in its own file, so the inventory comes back as it was left. Client side, in memory
only: leaving the world forgets it. A close outside creative restores nothing, and neither does an
open after a game mode change while it was open; the scroll is not put back on another tab.

Hooks (`CreativeSearch/`, Harmony id `seraphhorizons.creativesearch`, client only): a prefix on
`GuiDialog.TryClose` for the inventory dialog reads the text, tab and scroll while the dialog is
still open and its box still full, and a postfix on `GuiDialogInventory.OnGuiOpened` restores them
after the open's compose (or Dovidarium's reuse of the last composer): `SetValue` on the box, which
runs the dialog's own search and Tidy Variants' regrouping, then the scrollbar. Not a prefix on
`OnGuiClosed`: Dovidarium's craftable panel takes only foreign postfixes there and would turn itself
off (`docs/variant-grouping/hooks.md` §7). Flipping the creative mod tabs recomposes the open dialog
without closing it, so it never meets these hooks.

With text put back, the box is treated as left, as vanilla treats a search the player comes back
to: the first Backspace in it clears the whole text (the game's `DeleteOnRefocusBackSpace`). So
Ctrl+F opens the inventory on the remembered search with the box focused: typing adds to it,
Backspace (or a right-click, below) starts afresh.

To check by hand in the game (creative, the full pack): search `iron`, scroll down, close with E and
with Escape, reopen with E and with Ctrl+F: same text, results and scroll; with Ctrl+F, Backspace
clears the box. Close on one tab, reopen: same tab and scroll. Search with a group expanded: still
expanded after reopening. Flip the mod tabs with text in the box, scroll, close, reopen: same mod
tab, text and scroll. `/gm 0` with the inventory open, close, `/gm 1`, reopen: empty search at the top. Leave and
rejoin the world: empty search.

### Right-click clears the search box (`SearchRightClickClears`)

A right-click on the creative inventory's search box empties it, so the grid shows the whole tab
again, and leaves it focused, so the player can type straight away. The handbook's search box (its
list page) does the same. Every other text box is left alone. Client side.

Hook: a postfix on `GuiElementEditableTextBase.OnMouseDownOnElement` (the element under the press,
as the GUI routes it, so a dialog on top keeps its clicks), right button only, for the creative
composer's `searchbox` or the handbook overview's `searchField` in an open dialog; it calls the
box's `SetValue("")`, which runs the dialog's own search. Focus is vanilla's: the element marks the
press handled for any button and the composer then focuses it (a right-click on the box already
focused it, and did nothing else). With an item on the cursor nothing else happens: the creative
dialog's `OnMouseDown` returns as soon as a composer has handled the press, before the code that
would put the held item into the creative inventory. No pack mod patches that method, and
Dovidarium does not gate it.

To check by hand in the game: right-click the creative search box with text in it (whole tab back,
caret in the box, typing works), with an item on the cursor (still held, nothing deleted), and with
the box empty (nothing happens); right-click the handbook's search box; right-click the chat input
and a sign's text (unchanged).

## Tests

`tests/` (xunit, no game): Tidy Variants' rule engine and the shipped override and lang files,
cart reach's entity matching and reach rule, where the chopper drops its piles, `/clear`'s
daytime, dry-spell search and saved lock, and unified woodworking's rules: splitting block tiers,
upgrades and yields, the creative shortcut and the frames' stages, sawhorse work, the handbook's page list (and that the guides the export hides
are what it drops) and the lang entry changes (`Core/`), Map Reveal's `Core/`, the creative mod
tabs' plan, domain owners, state file and mod mode's tab layout (`CreativeModTabs/Core/`), and the
bucking sawmill's rig, footprint, assembly rules, cut arithmetic, cycle and animation
(`BuckingSawmill/Core/`, described in `BuckingSawmill/README.md`).
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
speed modifier and the storm's distance. With the switch off, `SwitchesOffScenarios` requires no
`/clear`. A real restart is not run: Atlas boots each class once, so the
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
inventory with no drops and no recipe. With the switch off, `SwitchesOffScenarios` requires the
block not to exist. While the pack pins a ppex older than 0.7.1, the
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
to the new files. With the switch off, `SwitchesOffScenarios` requires Age of Flax as it ships.

`tests/PackTests/ChopperOutputScenarios.cs` (Atlas) builds a chopper frame of each facing on a
granite floor, chops ten oak logs through the chopper's own `GetChopBatch` and `EjectBatch`, and
requires every piece at rest in the cell in front, at least 0.15 from its edges, and the patch
applied. With a hopper sunk into the floor there (a chest under it) it requires the hopper to catch
every piece, for two facings. It also requires the cell in front, and the one above it, to be
outside the chopper's footprint (`GetCells`). When it fails after an Immersive Woodworking update,
check whether `EjectBatch` or the footprint changed. With the switch off, `SwitchesOffScenarios`
requires the chopper unpatched and throwing its batch past the cell in front.

`tests/PackTests/HydrationCoverageScenarios.cs` (Atlas) requires a `hydration` attribute on every
food the server loads: anything eaten, used as a meal ingredient or drunk. An explicit 0 counts. When
it fails after a mod is added or updated, it lists the foods to give a value in `patches/hydration-*.json`.

`tests/PackTests/TunScenarios.cs` (Atlas) requires Hydrate or Diedrate's tun with no recipe, not in
the creative inventory and excluded from the handbook, and one placed still Hydrate or Diedrate's
block entity, taking 950 L of water. It requires the tun rack's block, field and liquid slot all at
950 L, the constructor patched, and a placed rack with a tun taking 950 L. With both switches off,
`SwitchesOffScenarios` requires both tuns as they ship (the rack at 500 L in all three
places). When it fails after a Food Shelves update, check whether `BETunRack` still keeps its own
capacity, and whether the block's `capacityLitres` moved.

`tests/PackTests/IrrigationVesselScenarios.cs` (Atlas) requires none of Primitive Survival's ten
irrigation vessels in a grid recipe (as output or ingredient), in the creative inventory or listed in
the handbook, none in BetterRuins' clay loot (the rest of Primitive Survival's clay loot still there),
and one placed still Primitive Survival's block entity, taking 50 L of water. It also requires Olla's
raw ollas clay formed, in the creative inventory and fired in a pit kiln into its fired ollas, which
are in the creative inventory and the handbook. With the switch off, `SwitchesOffScenarios` requires
the vessel as it ships, its loot included. When it fails after a Primitive
Survival or BetterRuins update, check the recipe file's order and the loot item's `*-clayproducts`
stacks.

`tests/PackTests/BarrelRackKegsScenarios.cs` (Atlas) places a barrel rack and has a player
right-click it through the rack block's own `OnBlockInteractStart`: an untapped keg holding 80 L
goes in (the liquid in the rack, none in the item), fills to 100 L and no further, and comes back
out holding 100 L; a barrel still holds 50 L and stays put while full; the perish speed in the
rack with each keg is 0.15 and 0.65 of a barrel's; and breaking the rack drops a tapped keg with
its 30 L in it. It also requires the restriction to mark both kegs and still the barrel, and every `LangEdits`
passage reworded. When it
fails after a Food Shelves or Hydrate or Diedrate update, check the restriction file, `BEBarrelRack`
and Hydrate's `ContainersConfig`. With the switch off, `SwitchesOffScenarios` requires the rack to
refuse a keg. The rack's look and the client's side of the interactions
are checked by hand in the game.

`tests/PackTests/MapRevealScenarios.cs` (Atlas) has a test player run `/revealmap`, decodes what
it is sent as the client would, and requires it to agree with the loaded chunks (so the savegame
reader reads the blocks the game does: when it fails after a game update, check `StoredChunk`'s
field numbers and `ChunkData.DecompressFrom`), the columns not generated yet skipped and still
not generated afterwards, the command refused in survival without `controlserver` and allowed in
creative, `stop`, and the shading
helpers equal to the game's `BlurTool.Blur` and `ColorUtil.ColorMultiply3Clamped`.
With the switch off, `SwitchesOffScenarios` requires no command. The client
half (drawing into the map and its database) needs a game client and is not tested.

`tests/PackTests/CreativeModTabsScenarios.cs` (Atlas) builds a creative inventory on the server with
the whole pack: the default tabs are the same as without the tweak, the mod tabs follow with every
creative stack in exactly one of them, the base game's tab first, `ageofflax`, `bomb` and
`oils` under their owning mods. The packet survives protobuf-net, and tabs a client builds from it have,
slot for slot, what the server's inventory returns for a click there (on the same world: a client with
other mods is the count and hash check's job), and mod mode's tab list, arranged from the real tab codes
and `config/creativetabs.json`, keeps the left column and puts the mod tabs alone on the right. With the switch
off, `SwitchesOffScenarios` requires no mod tabs. The GUI is checked by hand (the doc's checklist).

`tests/PackTests/UnifiedWoodworkingScenarios.cs` (Atlas) works the blocks the way a client's clicks
reach the server, through their own interaction methods, so the patches on those are what is under
test. It requires the tweak bound to both mods with nothing logged about them, its server
patches in under its own id, the world config telling clients it runs, Immersive Woodworking's
settings set, and the new name in English with its translations gone. Then: an axe on an upright
log makes a primitive splitting block of its wood (a sideways log nothing); each upgrade's cost,
wear and tier, and none with Shift, on a loaded block or past advanced; an upgrade hold that the
client claims but the server did not see last refused; in creative, Ctrl + right click raising an
empty block a tier at a time for nothing (no bark, the held stack kept, not with Shift, and Ctrl
still taking a log back off a loaded block), and building each Logging Expanded frame into its
next stage with the hands left as they were, while a survival player's empty-handed Ctrl click
does nothing; the tier through breaking, placing, and
saving and loading, and through breaking a block of no wood; 6 firewood per log through
half-logs and the maul (8 on an advanced block), 2 sticks per firewood, the setting put back after
an advanced chop, and no splitting block laid on one or taken by the restock; the chopper refusing
each lower tier, taking an advanced one, yielding 8 and giving it back advanced; the assembled
creative chopper's oak bed, and an older one's bed of no wood coming back advanced oak; on each sawhorse, beams with Shift and boards without, the spud's
debark and bark, and the advanced one's two rolls for two trunk logs; and the retired stations
hidden, registered and made by no recipe. When it fails after an update of either mod, the
warning in the log names what a part no longer finds. With the switch off, `SwitchesOffScenarios`
requires both mods as they ship: nothing of either patched (`ChopperDropsInFront` is off there too),
the world config telling clients it does not run, Immersive Woodworking's default
settings, its grid recipes, Logging Expanded's splitting log from an axe, a plain chopping block as
the chopper's bed, and a recipe export that lists the two mods' guides and none of the six pages.

`tests/PackTests/WoodworkingHandbookScenarios.cs` (Atlas) requires the six pages' assets and text and
the two guides they replace (by code and title; if those change, the client would show both),
every rewritten entry and pattern entry reading the new text, Immersive Woodworking's translations
of them gone, its composed splitting block section reading the same in each translation as in
English (no translated fragment in it), no woodworking text naming a retired station, and every link in the guide opening a
page. Which pages the handbook shows is client code, covered by the unit tests. `ItemExportScenarios`
requires the export's woodworking guides to be the six. What only a client shows or decides (the
models and textures, the lifted content, the chopper's bed, the help lines, the spud on a sawhorse
from a real client, the page list, a client following the server's world config key, the client's
restock prediction) is checked by hand in the game. Undoing a part that throws while it is applied,
and an asset edit failing after another mod's patch, are not reached in Atlas (the real mods bind
cleanly), and are covered by reading.

`tests/PackTests/TidyVariants*Scenarios.cs` (Atlas) resolve the rules on a server with the whole
pack: every creative entry maps to its stack and back, the handbook layout keeps one listed page per
group, the creative and handbook systems stay off on the server, and the report
(`docs/variant-grouping/report.md`). With the switch off, `SwitchesOffScenarios` requires nothing
resolved or patched and the boiler tweak still applied.

`CreativeKeepsPlace` and `SearchRightClickClears` are client GUI only, with no logic apart from the
game's: they need checking by hand in the game (the lists in their sections).

`tests/PackTests/SwitchesOffScenarios.cs` (Atlas) is every switch's off check on one server, seeded
with `fixtures/switches-off/seraphhorizons.json`: each class boots its own server, which costs far
more than the scenarios. `BoilerLidBlowsOpen` and the switches with no off check stay on.

The test project loads this directory's build as a mod, and leaves out a pinned copy from the
ModDB (`seraphhorizons_*.zip` in `build/mods`).

## Adding a tweak

Add a class next to `BoilerLidRelief.cs`, a `bool` setting for it in `SeraphHorizonsConfig`, and
the call in `SeraphHorizonsSystem` behind that setting. Then add scenarios and a section above. Its
off check goes in `SwitchesOffScenarios`, with its key in `fixtures/switches-off`, not in a class of
its own, unless what it requires needs another switch on. A
tweak big enough for mod systems of its own gets a folder, as `TidyVariants/` does; its systems
read their switch with `SeraphHorizonsSystem.ConfigFor(api)`.

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`SeraphHorizons.csproj`, merge, tag `seraphhorizons-v<version>` on main, upload the zip from the
GitHub Release to the ModDB (keep the file name), then pin it in `pack/pack.toml` (the first
release adds the entry, `side = "universal"`) and run `packtool lock`.

Between releases, every push to main that passes CI republishes the rolling
[`seraphhorizons-next`](https://github.com/benjamin-kirkbride/Seraph-Horizons/releases/tag/seraphhorizons-next)
pre-release (`.github/workflows/next.yml`): the zip CI's cairn job built from that commit, with
its `SHA256SUMS`. The pack's rolling `next` Cairn pack, published right after it from the same
commit, installs the mod from there by its sha256, so `next` plays with the mod as it is on main
before any of it reaches the ModDB. The zip keeps `modinfo.json`'s version, so it is not newer
than the release of that version as far as the game is concerned: swap it in for that copy,
don't add it next to one.

For a local build: `dotnet build mods-src/seraphhorizons -c Release` (needs `VINTAGE_STORY`) writes
`build/seraphhorizons_<version>.zip`: the DLL, `modinfo.json` and `CREDITS.md` at the top level, and
`assets/`.
