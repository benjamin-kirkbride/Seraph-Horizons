# Seraph Horizons

The pack's own mod, named after the pack: the pack and it are one thing with one version, released
together (Releasing, below). Its ModDB page, where it is uploaded after a release, is where people
browsing the ModDB find the pack. Its modid is the pack's id.

It is a code mod holding the pack's own tweaks: gameplay changes to other mods, Tidy Variants, which
tidies the creative inventory and the handbook, Map Reveal, which shows already generated
terrain on the world map, and two machines: the bucking sawmill, which cuts tree trunks into logs,
and the rosser, which strips their branches and bark first; the gears (#484): rusty gears reclaimed
into steel gears through the pot, the pickling tub and the barrel (`GearReclamation`), every machine
recipe taking the steel gear (`GearConsumers`), steel bits back into steel (`SteelBitsRecovery`),
steel gear blanks (`GearBlanks`) and the gear cutter that cuts them into steel gears
(`GearCutter`); one pipe network, Pipes and Power Expanded's in copper, lead, iron and steel with
bronze valves (`UnifiedPipes`), its iron and steel pipe cast in a pipe mold on smex's canal (`CastPipes`) and its lead and copper pipe sections drawn from tube blanks (hollow sections) on a draw bench (`DrawBench`) or folded from plates on a hand-worked press brake (`PressBrake`); ore cells, which spread each metal's deposits on a grid (Ore), and the trader overhaul (Trading): traders on a grid of
camps, item values, regional supply, standing, schematics, orders, maps and admin tools; a check that the installed mods are the pack's (`PackVersionCheck`); and every mod's settings following the pack's defaults when they change (`FollowPackDefaults`). These are choices for this pack, not bug fixes, so
they live together here and not in a mod each. Every tweak has its own switch in
`ModConfig/seraphhorizons.json` (all on by default). A tweak whose mod is not installed is skipped.
One whose mod has changed shape logs a warning and leaves that mod alone.

Only the game's own assemblies are referenced at build time: each tweak to another mod finds what
it patches by name, so the mod builds from the game alone (`release.yml` needs nothing else).

`"side": "Universal"`, required on the client. The server does the boiler behavior, drops the
chopper's output, corrects a rotor's ratio at a gearbox, feeds the creative steam source and runs `/clear`; Tidy Variants, cart reach, Carry On's
icon reset, the creative search tweaks and the NaN motion diagnostics run on the client; Map Reveal has a half on each side, and the creative mod tabs need both. The
client needs the mod because the steam source is a block with its own classes: the game cannot
build a block whose class it does not know, so a client without the mod could not join a server
that has it (a server with the steam source switched off, or without ppex, has no such block).
Unified woodworking runs on both sides too (the server does the work, the client draws the
splitting block, predicts its upgrades and arranges the handbook), and its splitting block has a
block entity behavior of this mod, which a client needs in the same way. So are the bucking
sawmill's, the rosser's, the gear cutter's, the draw bench's and the press brake's blocks: the server runs the machines, the client draws
their moving parts. Machine oil runs on both sides too: the server pours, drains and loads the
shafts, the client takes the click, shows the tank and draws the smoke. So are the trunk entities:
the server runs them, the client draws them and drives the tools' holds on them. So is the pickling
tub: the server runs its batches, the client draws the liquid and the gears in it. Steel bits
recovery patches the stone coffin on both sides: the server fills it, the client predicts the click.
The pack version check runs on each side for that side's own mods: the server logs and tells
admins, the client shows its dialog. So do the config defaults, each side for its own ModConfig.

## Tweaks

### Boilers blow their lid instead of exploding (`BoilerLidBlowsOpen`)

Pipes and Power Expanded (`ppex`, 0.7.1). A boiler whose steam run is sealed and that keeps firing
at its choke pressure for `BoilerOverpressureSeconds` (ppex config, 30 s) calls
`BlockEntityBoiler.Explode()`, which deletes the boiler, drops a fraction of its materials and
blasts the area. A run open at an end never gets there: every pipe connector facing air leaks, and
ppex blows such a boiler down to about 1 atm. `BoilerLidRelief` prefixes `Explode()` with the
boiler's own `ToggleLid()` and skips the blast: the access lid swings open with its sound and
animation, and ppex vents steam through the open lid (`BoilerLidVentRate`, 200 L/s) and resets the
over-pressure timer while it stays open. The player closes the lid by hand, and a boiler still with
nowhere to send its steam will blow it again.

The lid blowing open bangs like a ppex engine blowing up: it plays the sound ppex's
`BlockEntityEngine.Break()` plays, `game:sounds/effect/mediumexplosion` (ExpandedLib's
`ExSounds.MediumExplosion`), from the server at the boiler, over 24 blocks at half volume and
unrandomized pitch, as the engine does. The lid's own creak is still `ToggleLid()`'s.

The text that promised an explosion is reworded in place, in every language ppex ships (English,
Russian, Ukrainian): the over-pressure line in the boiler's info ("until the lid blows open!") and
the passages in the Steam Power and Boilers handbook pages. The Boilers page keeps ppex's point that
an unpiped steam outlet or a run left open at an end blows the boiler down to about 1 atm, which now
keeps the lid shut but leaves the run no working pressure. Each edit replaces one exact passage of
ppex's text (`LangEdits`). If ppex rewords it, that edit logs a warning and does nothing. Languages
other than the current one load lazily, so the mod loads those three when it starts.

### The handbook says where a chimney vents (`ChimneyVentingExplained`)

Pipes and Power Expanded (`ppex`, 0.7.1). ppex vents a pipe network through a chimney only when the
chimney stands directly on a Pipe Outlet, a Pipe Passthrough or a Passthrough Bend with a connector
on its top face, at `ChimneyGasDrawRate` (16 L/s) per chimney. On a plain pipe the chimney is not a
vent, and because it is not air it is not a leak either: it caps that end and the run keeps its
pressure. A boiler's fire also needs draught on its exhaust run (`PipeNetwork.HasDraught`): a
venting chimney, or a smoke stack (a block entity that is exlib's `IPipeDraught`). An open end leaks
the gas but gives no draught, and a choked fire goes out after `BoilerChokeExtinguishSeconds` (10
s). ppex's Fittings page only says that a chimney on an outlet vents; its Boilers page has the
draught rule, but not where a chimney counts.

`ChimneyVentText` rewords the chimney passage of the Fittings handbook page to say all of that,
including the draught rule, and that a venting chimney's look-at info says so (ppex's own
`chimney-info-venting` line), in every language ppex ships. Text only, as exact-passage `LangEdits`
like the boiler's; the Russian and Ukrainian passages are the pack's own translations.

### Rarer battle towers (`RarerBattleTowers`)

Battle Towers (`battletowers`, 1.1.0, #519) has no settings, and its towers were everywhere: in
the pack's worlds about 0.9 surface towers, 0.5 hard towers and 59 underground towers per km², an
underground tower every 130 m of cave. With this switch there is about one surface tower per
16 km² (a 4 km square), one hard tower per 66 km² (an 8 km square) and one underground tower per
17 km² (a 4 km square): about one tower of any kind per 7.5 km², so finding one is an event.

| Tower (`code`) | Battle Towers: chance, spacing | Here | Per km², before → here |
|---|---|---|---|
| surface (`surfacetowers`) | 0.03, 200 blocks (0.01, 600 with the pack's old ConfigKit setting) | 0.0007, 1500 | 0.88 → 0.062 |
| hard (`surfacehardtowers`) | 0.005, 1000 blocks | 0.00015, 3000 | 0.51 → 0.015 |
| underground (`undergroundtowers`) | 200, 50 blocks | 0.2, 50 | 59 → 0.059 |

The game's `GenStructures` gives each structure chance × chanceMultiplier tries per chunk column (977
per km²) at random spots, and a try places a tower only where the schematic fits. The multiplier is
0.45 in the pack (BetterRuins cuts vanilla's 0.46 to 0.30, `pack/config/ModConfig/betterruins.yaml`
puts it back). The share of tries that place a tower was measured in an Atlas world: 20% for a
surface tower, 23% for a hard one, 0.067% for an underground one, which must open onto a cave. The
spacing (`minGroupDistance`) is only checked against towers in loaded map regions, and never for
the underground towers, whose schematics attach to caves: the chance does the work, and the
spacings only keep two towers from landing side by side. The figures and the maths are in
`assets/seraphhorizons/config/battletowers-rates.json`.

Battle Towers appends its towers to `game:worldgen/structures.json` (`/structures/-`), so where they
end up in that list depends on every other mod adding structures, and a JSON patch of ours could
not address them; it could not patch Battle Towers' patch file either, as the game reads every patch
file before it applies any. So `BattleTowers.cs`, on the server in `Start`, before the patch loader
runs, rewrites Battle Towers' own `patches/survival-worldgen-structures.json` in memory, each tower
found by its code (`Core/BattleTowerRates.cs`). A tower Battle Towers no longer adds is logged and
left alone. This replaces the two ConfigKit settings (`battletowers_surface_chance`,
`battletowers_surface_min_distance`) that thinned only the surface towers. Worldgen only: it changes
the chunks generated from then on, in any world, and never a tower already placed. With the switch
off, or without Battle Towers, the towers are as Battle Towers ships them.

### Surface ruins sit on the median of their ground (`RuinsOnMedianGround`)

The game seats every ruin placed `surfaceruin` (BetterRuins' and its own) on the lowest of the
terrain heights it samples around the footprint, so on a slope the uphill side is buried. The
game's `WorldGenStructure.TryGenerateRuinAtSurface` samples the worldgen terrain height at the four
corners (which lie one block outside the footprint), at the start corner a second time (it means to
sample the centre and samples the start instead), and, for a side of 15 blocks or more, at that
side's midpoints and centre line (more for 30 or more). It gives up when the highest and lowest
samples differ by more than the schematic's `MaxYDiff` (3 by default), and otherwise sets the
ruin's base to the lowest. A BetterRuins bakery (`largeruins-shikitochi-o6-tinkersbakery`, 25 by
25) on ground at 136 to 137 with its south edge at 134 was seated at 134, its floor 2 to 3 blocks
below the ground around it.

With this switch the base is the median of the same samples (for an even count, the lower of the
two middle ones, so always a height the game sampled; `Core/RuinSurfaceHeight.cs`). The `MaxYDiff`
rejection, still on highest minus lowest, and everything after the seating (the liquid, overlap and
distance checks, the placement) are the game's. On flat ground nothing changes; on a slope the ruin
sits at the height most of the samples are at, and its low edge, not its high one, is the one off
the ground.

`RuinSurfaceMedian.cs` is a transpiler on that method, server side: each terrain height call goes
through a wrapper that returns the game's answer and notes it, and the minimum read where the base
is set (`startPos.Y = min + OffsetY`) goes through a call that answers the median of the notes. A
transpiler rather than a prefix copying the method: the method is long and works on the structure's
private state, and a copy would drift from the game's on an update without a sign, where the
transpiler checks the exact shape it rewrites before patching (at least five height samples, one
local holding the minimum, one place storing it plus `OffsetY` into `BlockPos.Y`) and, finding
anything else, logs a warning and patches nothing. Worldgen only: it changes the ruins of chunks
generated from then on, never a ruin already placed. With the switch off nothing is patched.

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
`ExpandedLib.Networks.BlockNetworkNode`; with an older ppex (0.6.8 and before) those names are
not there and the block is left out. If ppex has changed shape (`IPipeNode.TryProduce(float, float,
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

### Carry On's help icons are built for each world (`CarryOnIconsPerWorld`)

Carry On (`carryon` 2.0.0-pre.8), #401, upstream
[NerdScurvy/CarryOn#87](https://github.com/NerdScurvy/CarryOn/issues/87). Looking at a carryable
block (a chest, a crate, a barrel) in the second world of one client run crashes the client when the
two worlds number their items differently:

```
System.Exception: Error while rendering item in slot Vintagestory.API.Common.DummySlot (1x Item Id 13955, Code carryon:icon-handsfree)
 ---> System.IndexOutOfRangeException: Index was outside the bounds of the array.
   at Vintagestory.Client.NoObf.InventoryItemRenderer.GetItemStackRenderInfo(...)
```

Carry On builds the item stacks its interaction help shows (`carryon:icon-handsfree`,
`-nohandsfree`, `-put` and `-take`) the first time it needs them and keeps them in static fields that
nothing clears, so the next world in the same client run is shown the first world's `Item` objects
and the renderer indexes past the end of its item model array. There are seven such fields, all of
the static item stack fields in Carry On's assembly: `handsfreeStacks` and `nohandsfreeStacks` of
`CarryableInteractionHelpBuilder` (blocks) and of `EntityCarriedBlock` (a dropped carried block), and
`putStacks`, `takeStacks` and `nohandsfreeStacks` of `EntityBehaviorAttachableCarryable` (cart and
boat slots).

`CarryOnIcons` sets all seven back to null when the client leaves a world (the client's
`SeraphHorizonsSystem` is disposed), and once more when the client side starts, before the new world
draws anything. Each field is filled only while it is null, so the next world's first interaction
help builds them from its own items. It is a world-leave hook and not a Harmony patch: Carry On's code
is right for one world, only the state it keeps between worlds is wrong. The server never builds
these stacks (interaction help is the client's), so nothing happens there. Carry On is found by its
mod system's name (`CarryOn.CarrySystem`); a field that is gone or no longer an `ItemStack[]` is
logged as a warning and left out, and the client logs how many fields it clears. Without the tweak,
restart the client before opening another world.

Remove this tweak once Carry On clears the fields itself (upstream #87) and the pack pins that
version.

### NaN motion diagnostics (`NanMotionDiagnostics`)

A diagnostic, not a fix, for #405: the client crashes with

```
System.ArgumentException: Given pos contained NaN: XYZ: 511823.82/114/512083.63, YPR -8.098406/3.5433109/0, Dim 0 for entity game:player
   at Vintagestory.GameContent.EntityBehaviorPlayerPhysics.SimPhysics_Patch6(EntityBehaviorPlayerPhysics this, Single dt, EntityPos pos)
   at Vintagestory.GameContent.EntityBehaviorPlayerPhysics.OnRenderFrame(Single dt, EnumRenderStage stage)
```

The position is finite and the player's `Pos.Motion` is NaN: `EntityBehaviorControlledPhysics.ApplyTests`
checks both before anything else, and throws (the frame shown is `SimPhysics` because gondolacablecar's
finalizer on it rethrows, which restarts the trace there). It has happened standing still and AFK, and
nobody has reproduced it. This finds what made the motion NaN and leaves the crash as it is: nothing is
repaired, dropped or caught.

`NanMotion/NanMotionDiagnostics.cs` traces, on the client's main thread, every place that can write
the local player's motion, with a checkpoint at the start of each call (before any other mod's prefix)
and one at its end (after any other mod's postfix), and keeps the first one that sees the motion go
from finite to non-finite (`NanMotion/Core/NanTracker.cs`): inside a call (finite at its start, NaN at
its end: that call, or code it calls that is not traced on its own, made it), or between two
checkpoints (code that is not traced made it; the record names both ends). Each checkpoint checks the
player's motion, not its own entity's, because another entity's behavior can push the player. Traced:

- every `EntityBehavior.OnGameTick` and `OnReceivedServerPos` override and every `Entity.OnGameTick`
  and `OnReceivedServerPos` in every loaded assembly (the game's, every code mod's), found by
  reflection when the level is final (about 115 methods in the pack);
- the player's physics: every `PModule.DoApply` override, `EntityBehaviorPlayerPhysics.OnRenderFrame`,
  `SetState`, `MotionAndCollision`, `ApplyTests`, `AfterPhysicsTick` (the traversed blocks'
  `OnEntityInside`) and `CollisionTester.ApplyTerrainCollision`; and `SimPhysics` with a checkpoint
  before every mod's prefix and one after them all, so a NaN made in the physics step, one that arrives
  from outside and one a mod's patch on `SimPhysics` makes (gondolacablecar's prefix moves the player)
  are told apart;
- everything else on the client's main thread that runs mods' code: tick listeners
  (`GameTickListener.OnTriggered`, naming the handler), each renderer (a transpiler on
  `ClientEventManager.TriggerRenderStage` calls each `OnRenderFrame` through a traced wrapper), queued
  main thread tasks, server packets, mods' network messages (naming the channel), and the server's
  entity positions (`SystemNetworkProcess.HandleSinglePacket`, which also writes the motion it carries
  into the entity, the local player's included);
- the player's repulsion: after each neighbour `EntityBehaviorRepulseAgents.WalkEntity` walks, the push
  is checked, and the first neighbour that leaves it non-finite is named with its position and
  repulsion position (the game's repulsion takes a NaN neighbour as it is: a NaN distance fails its
  `>=` range test and goes into the push).

The first trip is logged at once as a warning, with the stack and the patched methods on it, and
tracing stops. When the crash comes (`ApplyTests` entered with a non-finite position or motion, or,
as a backstop, an exception about a NaN thrown on the client thread, seen through
`AppDomain.FirstChanceException` before anything catches it), the full report
(`NanMotion/NanMotionReport.cs`) goes to the client log as an error and to
`Logs/seraphhorizons-nanmotion-<time>.txt`, before the exception propagates: the first-NaN record (or a
statement that nothing traced saw it, which narrows it to what is not traced), the repulsion culprit,
the player's position, previous server position, motion, flags, mount, controls, walk speed,
`GetWalkSpeedMultiplier`, game mode and every stat with each of its parts, the player's physics
behavior and each module's fields (`PModuleOnGround`'s `motionDeltaX`/`Z` are kept between frames), the
blocks at and under it, every entity within 16 blocks and every entity anywhere with a non-finite
value, who patches the methods on the physics path, the last 20 physics frames
(`NanMotion/Core/MotionRing.cs`) and what the diagnostics patched. Each section, and each line about
the player, is written on its own, so one failure loses only itself. There is no Harmony finalizer: Harmony rethrows a
finalizer's exception, which would cut the crash log's stack trace short at the patched method.

Cheap while nothing is wrong: a checkpoint reads the thread id, the player's motion and three doubles,
and allocates nothing (the start's probe is a value type, the site a constant: one trace class per
site, not `__originalMethod`, which Harmony computes on every call). Calls on other threads (the
server's, in singleplayer) return after the thread check. Client side only, its own Harmony id
(`seraphhorizons.nanmotion`), patched when the level is final and unpatched when the world is left.
On by default: it is meant to be in every player's game until #405 is found. Off, nothing is patched.
Remove it once #405 is fixed.

What the game's code already suggests (1.22.7): standing still, the player's ground module
(`PModuleOnGround`) adds `WalkVector * GetWalkSpeedMultiplier()` every frame, and `0 * NaN` is NaN, so
a NaN walk speed multiplier makes the motion NaN with no input at all. It is the `walkspeed` stat
(`EntityPlayer.walkSpeed`, read every tick; `GameMath.Clamp` passes NaN through) times the block
under the feet's `WalkSpeedMultiplier` (in creative, divided by it). The module also keeps
`motionDeltaX`/`Z` between frames, so once NaN they stay NaN. The report shows each of these.

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

### A rotor turns at its own speed through a gearbox (`GearboxSourceRatio`)

MPE Gearbox (`mpegearbox` 1.0.1), #462. A power source (a rotor, the creative or auto rotor)
driving a machine through one of its gearboxes could run at the wrong speed, depending on the order
things were built in: an auto rotor set to 1 rps on a 1:5 gearbox's low side turned the network at
about 0.19 rps, so the rosser on the high side ran at about 0.9 rps instead of about 3.6; one on the
high side races instead. The game asks a neighbour for its geared ratio, `GetGearedRatio(face)`, in two ways:
`BEBehaviorMPBase.tryConnect`, when a block joins a network, passes the direction from the asker
toward the neighbour; `CreateJoinAndDiscoverNetwork`, when a source creates the network itself and
spreads it (a rotor placed after the gearbox, or every source again when `MechanicalPowerMod`
rebuilds a network after a block on it is broken), passes the neighbour's own connector face. The
two agree for every vanilla block, which has one ratio on all its faces. MPE Gearbox's
`BEBehaviorGearbox12` answers only the first way right, so the rotor stored the far side's ratio
(5 on the low side, 0.2 on the high side) and throttled its torque at the wrong network speed. The
gearbox and what lies beyond it were right; only the rotor was off.

`GearboxSourceRatio` postfixes `BEBehaviorMPBase.CreateJoinAndDiscoverNetwork`: when the neighbour
the block discovered through is MPE Gearbox's, on the same network and connected on the face that
touches it, it asks the gearbox again the first way (`GetGearedRatio(powerOutFacing)`) and sets
that ratio when it differs (`Core/GearboxCoupling.cs`). Only the ratio is set, not the propagation
direction, which the game got right, and nothing is changed next to any other block, or when the
discovering block is a gearbox itself (its stored ratio is its low side's).

Server side, where networks are discovered; clients get the ratio with the block entity. MPE Gearbox
is not referenced at build time: if `MPEGearbox.BEBehaviorGearbox12` is missing or no longer a
`BEBehaviorMPBase`, the mod logs a warning and patches nothing. Reported upstream
(<https://mods.vintagestory.at/mpegearbox#cmt-245057>); once MPE Gearbox answers both ways, the
switch's off check fails and the tweak can go.

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

### Blood sausage and black pudding come from the mixing bowl (`BloodSausageInMixingBowl`)

Butchering (`butchering` 1.14.3) makes raw blood sausage and raw black pudding
(`butchering:sausage-bloodsausage-raw`, `butchering:sausage-blackpudding-raw`) two ways. In the grid:
clean offal, a wooden bucket of blood (0.2 L for a sausage, 0.8 L for a pudding) and, for the sausage,
2 red meat, 4 bushmeat or 3 poultry (`recipes/grid/bloodsausage.json`, three recipes), for the pudding
rendered fat (`recipes/grid/blackpudding.json`, one). And in A Culinary Artillery's mixing bowl, by
its own kneading recipes (`recipes/kneading/bloodmeatnuggetsausages.json`: Expanded Foods' meat
nuggets for the sausage), which ship `enabled: false` and which its Expanded Foods compatibility patch
enables. The pack keeps the mixing bowl's, with the rest of its sausages: the four grid recipes are
disabled (`enabled: false`). The items, their cooking and smoking stay as Butchering ships them.

A JSON patch, `assets/seraphhorizons/patches/bloodsausage-butchering.json`, `"side": "server"` and
`dependsOn` butchering and expandedfoods. With the switch off, or without Butchering, Expanded Foods
or A Culinary Artillery (without which nothing else makes them), the system empties that patch file
in `Start`, as for Hydrate or Diedrate's tun. The switch that counts is the server's.

### Recipes that duplicate or undercut another are off (`DuplicateRecipes`)

Some mods declare a recipe for something another recipe in the pack already makes, the same way or
for less. The pack keeps one, and the other is disabled (`enabled: false`):

- Expanded Foods (`expandedfoods` 2.0.0-dev.15) kneads its sausages in A Culinary Artillery's mixing
  bowl from fat and meat or fish nuggets: `recipes/kneading/sausage.json` (`expandedfoods:sausage-{meat}-raw`,
  `-curing`, `-{meat}cheese-raw`, `-{meat}cheese-curing`) and `recipes/kneading/sausagefish.json`
  (`expandedfoods:sausagefish-normal-raw`, `-normal-curing`, `-cheese-raw`, `-cheese-curing`), four
  recipes each, all off. Butchering (`butchering` 1.14.3) makes the same eight with clean offal added
  (`recipes/kneading/meatnuggetsausages.json`, `fishnuggetsausages.json`), recipes it ships
  `enabled: false` and enables with Expanded Foods; those stay. Needs both mods.
- Expanded Foods' scrap brazier (`recipes/grid/braziers/brazier.json` `/0`: 4 firewood, 2 metal
  scraps, an oil lamp and 0.2 L of food oil make `hqzlights:brazier-scrap`) undercuts HQZ Lights'
  own (`hqzlights` 1.1.3, `recipes/grid/brazier.json`: 16 firewood, or 10 aged, 2 metal scraps and
  2 fat), which stays. The file's `/1` names `hqzlights:metalstrip-*`, which no mod adds, so it
  makes nothing and is left alone. Needs both mods.
- Material Needs (`materialneeds` 2.0.0) re-declares three of the game's recipes for aged wood, which
  the game's wildcards already make: `recipes/grid/wood/substitutions/mn-roofing.json` (the seven
  aged roofing pieces), `mn-raft.json` (the aged crude oar and raft) and `mn-shield.json` (the very
  aged plank round shield), all off. Its ashlar, roof beam, dirty gravel and mud brick recipes stay.
- The game's barrel cottage cheese (`game:recipes/barrel/cheesemaking/cottagecheese.json`: 5 L of
  curdled milk and salt, sealed 24 h, give 5 L) is replaced by Expanded Foods' mixing bowl recipe
  (`recipes/kneading/cottagecheese.json`: 10 vinegar and 10 pasteurised milk give 20 portions).
  Needs Expanded Foods.
- The game's sandstone daub (`game:recipes/grid/daub-raw.json` `/14`: soil, 2 dry grass, sandstone
  sand and any clay give 8 yellow daub) is GeoAddons' (`geoaddons` 1.4.8,
  `game:recipes/grid/daub-raw-geo.json`) sandstone recipe giving 12; GeoAddons' stays. Only that one
  of the file's recipes: the rest are for rocks GeoAddons' file does not cover, and Material Needs
  appends its gravel daubs to the same file. Needs GeoAddons.

MEA Pineapple Turpentine's and Oils Resoaped's oil lamp recipes look like the game's but are not:
the first fills the lamp with camphine, the second takes 1 L of any oil (flax too) in a bucket, so
both stay.

A JSON patch, `assets/seraphhorizons/patches/duplicaterecipes.json`, `"side": "server"`. Each entry
`dependsOn` the mod it patches and the mod whose recipe stays, so the patch loader skips it when
either is missing and nothing is left without a recipe. With the switch off, the system empties that
patch file in `Start`, as for Hydrate or Diedrate's tun. The switch that counts is the server's.

### Panning gives no wool, awls, uranium or buttons (`PanningDropsTrimmed`)

The game's pan (`game:pan-wooden`, `attributes.panningDrops` in `blocktypes/wood/pan.json`) has
none of these; three mods add them with a JSON patch on that file, each to the bony soil list
(`@(bonysoil|bonysoil-.*)`) and the sand and gravel list (`@(sand|gravel|sandwavy)-.*`):

| Mod | Taken out | Bony soil | Sand, gravel |
|---|---|---|---|
| Wool (`wool` 1.9.6) | wool fibers, `wool:fibers-generic-*` | brown, gray, black, redbrown, lightbrown, yellow | plain, gray |
| Tailor's Delight (`tailorsdelight` 2.2.2) | stitching awls, `tailorsdelight:awl-*`, and the copper thorn an awl is made from, `awlthorn-copper` | awl-flint, awl-obsidian, awlthorn-copper | awl-flint |
| Tailor's Delight | buttons and clasps, `tailorsdelight:buttons-*` | horn, brass, silver, gold, emerald, lapislazuli | zinc, copper |
| Expanded Matter (`em` 3.8.1) | `game:nugget-uranium` | yes | yes |

Everything else those patches add stays: Tailor's Delight's twine and needles, Expanded Matter's
fluorite, corundum, phosphorite, rhodochrosite and kernite. The patterns are
`Core/PanningDropRules.cs`: `wool:*`, `tailorsdelight:awl-*`, `tailorsdelight:awlthorn-*`,
`tailorsdelight:buttons-*` and `game:nugget-uranium`. Wilderlands Panning's rich gravel lists add
none of them, and nothing else in the pack has a panning table (Panning Machine pans with the
wooden pan's).

Wool and Expanded Matter have a ConfigLib setting for their whole panning patch, but that would
also take Expanded Matter's other ores, and Tailor's Delight has none. So `PanningDrops` edits the
pan's asset on the server in `AssetsLoaded`, after the game's patch loader (0.05) has applied every
mod's patches, in whatever order they load, and before the block types are read from the assets
(0.2): every entry of every material's list (in `attributes`, or an `attributesByType` entry) whose
code matches is removed, and the server logs how many. The pan, Panning Machine and the handbook
(clients get the block's attributes from the server) all see the trimmed table, and so does the
recipe export. If the pan's asset is missing or has no `panningDrops`, the mod logs a warning and
leaves panning as it is.

Tailor's Delight's handbook line for the buttons says they are found while panning; that passage is
taken out in each language it ships (en, de, fr, pl, ru; `LangEdits`), on each side. With the switch
off, panning and that text are as the mods ship them. The switch that counts for panning is the
server's.

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
- `/clear stay` does the same and holds it: the sun stays at summer noon while the hours go by as
  usual, the weather stays clear and no temporal storm comes, until `/clear stop`.
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
| the sun at summer noon, time running on | the calendar's sun (`OnGetSolarSphericalCoords`, the survival mod's) asked for noon at midsummer whatever the hour and the day, on the server and on every client | the sun as it was |
| the next temporal storm as far off as at the lock | its scheduled day, kept that many days ahead | that many days ahead of the time at `stop` |

The temporal storm world setting is never changed, so storms switched off stay off. Every second
the lock is enforced again: every loaded region clear (a region loaded since comes up clear within
the second), no override other than -1, auto-changing off, a storm that was started ended. The
lock and what it replaced are kept in the savegame (`seraphhorizons:clearlock`) and reapplied on
load, so it outlasts a restart; the game saves the override itself, but not auto-changing patterns.

**The sun.** Every sun position and daylight strength the game works out goes through the
calendar's `OnGetSolarSphericalCoords`, which `GameCalendar.GetCelestialAngles` hands the fraction
of the year and of the day; the survival mod installs the real one
(`SurvivalCoreSystem.GetSolarSphericalCoords`, from those two fractions alone) on the server's
calendar and on each client's. While held, that one is wrapped and asked for noon (the day half
gone) at the hemisphere's midsummer: the year 10 days short of half gone in the north (the survival
mod's year starts 10 days after the northern winter solstice), 10 days short of its end in the
south (`Core/ClearSkyPlan.cs`). The wrap is put back within a tenth of a second if something
installs another sun over it (the survival mod does, at load). The server tells each client whether
the sun is held (channel `seraphhorizons:clearsky`) when the lock starts and ends and as a player
joins, since the sky, the sun and the light are drawn from the client's own calendar.

The clock is not stopped: the hours pass as usual, sleeping speeds them up, and whatever the game
times by its calendar runs on, for example a forge heating, crops on farmland, barrels, pit kilns
and charcoal pits, food spoiling and other item transitions (drying, curing), beehives, body
temperature, the season and its temperatures, and the next temporal storm. What the hold changes
is the sun and the daylight it gives: whatever goes by daylight sees a lasting day, for example
creatures that spawn in the dark (above ground; caves stay dark), rifts, resting butterflies, bees
around a hive, and a firepit's night sounds. The moon still moves, and the few creature behaviours
that read the hour itself (the survival mod's `TimeOfDayCondition` and `JumpAction`) see the real
time. Wind stays as it was, since patterns do not change.

A world saved while an older version of the mod held it, when `stay` stopped time with the
calendar's `baseline` time speed (set so that all the speed modifiers summed to 0), gets `baseline`
back as it was before that lock, once, on load; the lock goes on as above and is saved without it.

With the switch off there is no `/clear`, and a lock left in the savegame is released when the
server starts. If the mod is removed while a lock holds, the sun is back to normal at once (the
wrap lives in memory only, nothing of it is saved), and `/weather setprecipa` and
`/weather acp on` put the weather back.

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
`sawhorsestandard`, `sawhorseadvanced`), which keep all they do: load a debranched trunk (carried
in Carry On's hands, with `TrunkEntities`, below) or up to 16 logs, and each hold (0.75 s) works one log: an axe takes it off, a saw cuts 9, 12 or 18 boards, and
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

- Immersive Woodworking's chopper and sawmill (`Woodworking/MachineUpgrades.cs`), one part per
  click on an unassembled machine, in the order the machine lists them as missing (the chopper's
  drive, arm and bed, an advanced oak splitting block; the sawmill's sash, crankshaft, levers and
  carriage), then a steel head or blade kit, held items kept. A prefix on the block entity's
  `OnInteract`, which the frame and its ghosts call on both sides, hands the part to the machine's
  own `TryAddPart` in a slot of its own, so it is fitted as one put in by hand. On the assembled
  machine Ctrl takes the log or the tool out, as Immersive Woodworking has it. A machine whose
  members are not found gets no shortcut, with one warning.

- the bucking sawmill (`BuckingSawmill/README.md`, "Creative shortcut"), one part per click on an
  unassembled mill: the sashes, the crankshaft, the levers and a steel blade kit, held items kept.
  It is the mill's own interaction, not a patch, and runs while this tweak does. On the assembled
  mill Ctrl takes the trunk or the kit back, as in survival.

- the rosser (`Rosser/README.md`, "Creative shortcut"), one stage per click on an incomplete
  rosser: the crankshaft, the ring's four gear sections, the hoops, each set of feed-roll rods, the
  breaker plates, the levers and four steel bark spud heads. It is the rosser's own interaction too,
  and runs while this tweak does; on the complete rosser Ctrl takes the trunk or the unused heads
  back, as in survival.

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

They are not pack values (`pack/config`, Settings follow the pack's defaults below): a player's
own file could undo those, and the tweak needs them wherever it runs, and only then. Logging Expanded's settings need
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
  logs, not debarked logs (the automated sawmill takes those). A debarked trunk (`Rosser`) is the
  exception: its logs are debarked, and Shift + saw makes beams of them.

### Bucking sawmill (`BuckingSawmill`, `BuckingSawmillSettings`)

Immersive Woodworking (`immersivewoodworking`, 1.3.11) and Logging Expanded (`loggingmod`, 0.3.6).
A machine of this mod's own: a mechanically powered pair of drag saws that cross-cut Logging
Expanded tree trunks into logs. Its frame is crafted from two Immersive Woodworking sawmill frames,
four support beams and eight nails and strips, placed as a six by three by four multiblock, and fitted in the world with
Immersive Woodworking's sawmill parts (two sashes, a crankshaft, feed levers and one blade kit,
which puts a blade in both saws). An axle drives it at the far end; trunks go on by hand, from a
Trunk Storage Rack under the axle, or from a rosser placed in line there (below), and the logs come
out of the near end. A debarked trunk gives debarked logs. While it turns it never
stops: the saws sink through the trunk, a windlass winds them back up, and a new trunk can go on
only at the top of the cycle. A loaded trunk is shown as Logging Expanded's 1×1×4 or 2×2×5 model
(thin or thick, whatever its own length) and is solid and selectable where it is shown. A better
blade cuts faster, by the tool tier of the game's saw of its metal (steel about twice copper), and
each cut costs the kit one durability per log in the trunk. A trunk offered while the saws are not
at the top goes on as they come up if the button is held; a stopped mill's saws are wound up by
holding right-click with an empty hand; and the block info says what the rack or rosser at the
far end offers, or why nothing is coming. In creative, Ctrl + right click fits the next part, as on the
woodworking stations (`UnifiedWoodworking`). Its model is checked for z-fighting faces when it is
generated.

`BuckingSawmillSettings` holds its figures (shaft load, turns per log, logs per stored log, blade
wear, how much faster each tool tier cuts, whether it pulls from a rack or a rosser in line,
`AutoPullFromRack`). With the switch off, or either mod missing, its blocks and
recipe are left out before the game loads them, as the creative steam source's is; neither mod is
referenced at build time, and Logging Expanded is reached by reflection.

Everything else is in [`BuckingSawmill/README.md`](BuckingSawmill/README.md): the blocks, the rig
file that ties the model to the code, the cycle, the settings, the generated model and how to
regenerate it, and its tests. Most of the model was made for this mod; its gears, saw blades, saw
heads and cranks are from Immersive Woodworking's sawmill model by Bobrik00, used with the
author's permission and not covered by the repository's license (`CREDITS.md`).

**A rosser in line.** The mill takes trunks from a second kind of feeder as well as a rack: a
machine at its far end whose block entity implements `ITrunkFeeder` (`Machines/Game/ITrunkFeeder.cs`),
which is the rosser. In each of its three infeed cells `CheckRack` first looks for a feeder (the
block entity there, or a ghost's controller through `IMachineGhost`) facing the mill's own way whose
outfeed cell that is, and only then for a rack. It takes the rosser's finished trunk under the same
gates as a rack's (assembled, bed empty, saws at the top, `MinSpeed`, `AutoPullFromRack`), peeking
first and taking only once it will load it, so a trunk it does not take stays on the rosser. The
block info names the rosser's state (`FeederEmpty`, `FeederBusy`, `FeederReady`). Details are in the
mill's README ("A rosser in line").

### Rosser (`Rosser`, `RosserSettings`)

Immersive Woodworking (`immersivewoodworking`, 1.3.11) and Logging Expanded (`loggingmod`, 0.3.6);
Pipes and Power Expanded (`ppex`) optional. A second machine of this mod's own: a mechanically
powered ring debarker that draws a Logging Expanded tree trunk lengthwise through a spinning cutter
ring. A limb breaker in the throat snaps off its branches, which fall as sticks (one for every two
branches by default, where Logging Expanded's knife gives one each); four spring-closed scraper arms
on the ring, tipped with bark spud heads, strip its bark, which falls as Immersive Woodworking's bark
for the trunk's wood, log by log; and the trunk comes out debarked. Its frame is crafted from four
Immersive Woodworking sawmill frames, thirty-two support beams, thirty-two nails and strips and two
copper chute sections, placed as a sixteen by five by four multiblock (long, wide, high), and fitted in the world with existing items only: a sawmill
crankshaft, four large gear sections, two hoops, four rods, two metal plates, sawmill feed levers and
four bark spud heads of one metal, which are its wearing part and last four times their metal's bark
spud. The axle connects on a side face beside the ring and may turn either way. A trunk goes on by
hand or from a Trunk Storage Rack at the far end; its weight on a treadle starts a geared feed, at
one speed for every thin trunk and a slower one for every thick trunk (about 68 and 169 shaft turns with
copper heads; better heads feed faster by their tool tier, as the mill's blade kit cuts). The
debarked trunk waits on the outfeed bed until a hand takes it, a rack at the near end with room takes
it, or a bucking mill placed in line takes it at the top of its saws' cycle; it leaves this way even
when it was the trunk that wore the heads out (only a new trunk needs heads). A Pipes and Power
Expanded water pipe on its other side fills a drip, and wet logs give more bark, and more often the
special kinds. In creative, Ctrl + right click fits the next stage, as on the woodworking stations.
Its model is generated and checked like the mill's.

`RosserSettings` holds its figures (shaft load, the typical trunks and turns per log and per branch
the two feed speeds are set on, how much faster each tool tier feeds, sticks per branch, the bark
multipliers dry and wet, head wear, the drip's water, and whether it pulls from and pushes to
racks). With the switch off, either mod missing, or the debarked trunk not there, its blocks and
recipe are left out before the game loads them, as the mill's are; neither mod is referenced at
build time.

Everything else is in [`Rosser/README.md`](Rosser/README.md): the blocks, the rig, the trip, the
settings, the generated model (its mechanism, the weighing cradle and the two-speed feed) and how to
regenerate it, and its tests. Most of the model was made for this mod; its crown disc and two
pinions, and the tooth its other gears are built from, are taken from Immersive Woodworking's
sawmill model by Bobrik00 and are not covered by the repository's license (`CREDITS.md`).

**The debarked trunk.** The same switch gives Logging Expanded's trunk a third state of its
`branches` variant, `debarked`: `loggingmod:treetrunk-{wood}-{size}-debarked-{side}`, "<Wood> Tree
Trunk (Debarked)", for every wood, size and side the clean trunk has (1320 blocks). It needs only
Logging Expanded, and is not in the creative inventory: the rosser makes it.

- **The patch.** `patches/rosser-debarkedtrunk.json` (server side, `dependsOn` loggingmod) adds the
  state to `/variantgroups/2/states`; adds a `shapeByType` for `*-debarked-*` that the game resolves
  into the trunk's own `shape` and merges, so only its `base` changes (to the clean trunk's,
  `treetrunk-{size}-no`) and its `rotateYByType` stays; and gives each wood's texture entry a
  `wood-hByType`, which resolves into its `wood-h` (the bark faces) as `block/wood/debarked/<wood>`,
  and a `woodByType`, which resolves into its `wood` (the ends) as
  `block/wood/treetrunk/debarked/<wood>`: the textures the vanilla debarked log has there, so the
  ends show no bark ring. Cherry, which the game has no wood of, takes oak's; Wildcraft: Trees'
  woods, which the pack does not have, point their bark into its domain, as Logging Expanded's own
  bark does, and keep Logging Expanded's ends. The names are this mod's lang entries,
  keyed in Logging Expanded's domain. In `Start`, before the patch loader runs, `DebarkedTrunks.Bind`
  checks Logging Expanded's trunk blocktype against the patch (the third variant group is `branches`
  with states `yes` and `no`, the shape is by size and branches with no `shapeByType`, and the
  texture entries are exactly the ones the patch targets, each with a `wood-h`, and with a `wood`
  wherever the patch replaces the ends) and finds the members
  below; if anything differs it logs one warning and empties the patch, and there is no debarked
  trunk and no rosser.
- **What works with it.** Logging Expanded carries the state wherever it carries the block: placing
  and picking up keep it (with `TrunkEntities` there is no placing: a trunk entity keeps the stack),
  and the Trunk Storage Rack and the heating rack keep whole stacks. Carry On
  and Cartwright's Caravan patch the whole trunk blocktype, so they carry it too. The bucking mill
  cuts it into the wood's debarked logs (`debarkedlog-<wood>-ud`), at the same yield. On a sawhorse
  the axe gives debarked logs at Logging Expanded's own yields, the bark spud has nothing to do and no
  bark drops (`UnifiedWoodworking`'s sawhorse guards), the saw gives boards and beams as usual, and
  unloading gives the debarked trunk back. On the ground (a trunk entity, with `TrunkEntities`) an
  axe gives debarked logs, and the bark spud debarks a clean trunk whole. A debarked trunk
  has no branch count, so the knife and shears do nothing to it. The rosser refuses it.
- **How.** A sawhorse keeps only the trunk's stored log stack, so `Trunks.Debark` marks that stack
  (`seraphhorizons:debarked`), and the mark goes wherever the logs go. Server-side Harmony patches
  (`Rosser/DebarkedTrunks.cs`): a prefix on `TreeManager.GetPlacedLogCode` answers the wood's
  debarked log while a flag is set; prefixes and finalizers on `BlockTreeTrunk.OnBlockInteractStop`
  (a debarked trunk on the ground) and on both sawhorses' `ProcessWithTool` (a marked load) set and
  clear it; and a postfix on `BEWorkstation.BuildUnloadStack` gives back the debarked trunk where
  Logging Expanded would rebuild a clean one.
- **Known limits.** The Trunk Storage Rack draws a debarked trunk with bark (it has its own shapes
  per wood). The heating rack still drains a debarked pine or acacia trunk's resin. A debarked trunk
  made without `Trunks.Debark` (only `/giveblock`) has no mark and acts as a clean trunk on a
  sawhorse. With the switch turned off, debarked trunks already in a world are lost, as are rossers.

### Machines need oil (`MachineOil`, `MachineOilSettings`)

The game, Immersive Woodworking (`immersivewoodworking`, 1.3.11) and this mod's two machines. The
heavy mechanical power machines have an oil tank: the game's helve hammer and pulverizer, Immersive
Woodworking's plank sawmill and powered chopper, the bucking sawmill, the rosser, the draw bench and the gear
cutter (whose oil wears its cutter kit instead of loading its shaft: "Gear cutter" below). Nothing else
does: the quern, axles, gears and every other transmission part are exempt. A machine is built
**dry**, and runs dry again when its oil is used up. Dry, its load on its shaft is
`DryResistanceMultiplier` (3) times what it is otherwise; nothing else changes (it wears and works
as before). Any oil at all is not dry. While dry and turning, it puffs dark smoke.

- **Oil.** Right-click any cell of the machine (any cell of a multiblock) holding a liquid
  container of a listed oil: as much as fits goes in, in whole items, taken from the container the
  way a barrel takes it (a stack of containers has one split off). Holding oil, the click always
  pours, whatever the cell would do otherwise. The listed liquids (`OilLiquids`) are the oils the
  locked mods ship: the game's `oilportion-*` (flax and olive; its melted fat, `-fat` and
  `-fatsolid`, are skipped variants but count if a mod enables them), Expanded Foods'
  `foodoilportion-*` (flax, rice, seed, soy, sunflower, peanut, olive, and walnut from Oils
  Resoaped's patch) and its `lard` and `hardlardliquid` (liquid rendered fat, hardened or not).
  The game's tallow, rendered fat (`game:fat-rendered`), is a solid item with no liquid form in the
  game, so it goes in by hand from the stack, at `OilLumps`' half a litre a lump (what its skipped
  melted form, `oilportion-fat`, holds: 2 items to the litre). Turpentine and Oils Resoaped's wood
  finish are not oils for this.
- **The tank** is in points, 100 to the litre (one item of a 100-per-litre oil is a point). It
  only fills: oil cannot be drained back out, and breaking the machine loses it. A setting that
  makes a tank smaller keeps what fits.
- **The drain** is by the job, and idle turning is free: the helve hammer per strike on an anvil
  with work on it, the pulverizer per item crushed, the plank sawmill per log sawn, the chopper per
  log chopped, the bucking sawmill and the rosser per log stored in the trunk, rounded up over
  the trunk (as their blade and head wear are), the gear cutter per gear cut (a large one
  double), and the draw bench per pipe section drawn.
- **Block info:** `Oil: <points> of <tank>` (rounded up, so a tank with any oil never shows 0, and
  an empty one 0, not -0), then the load on its shaft: oiled `Load: 17 kN`, and while dry the load
  now against the load it takes oiled (`Dry: a load of 51 kN on its shaft, 3× the 17 kN it takes
  oiled`). The load is what the shaft last asked of the machine (`OilState.Load`, set in every
  `GetResistance`; the server syncs it when it changes), so until the network has asked once there
  is no load line, and the dry line only gives the multiplier. kN is the game's own scale
  (`OilText.KiloNewtonsPerUnit`): its creative rotor shows its torque times 100 as `Torque: {0} kN`
  (its windmill's sails line about the same), and a load is in the same units as a torque. The game
  shows whole kN; `OilText.Load` keeps one place, so a 0.005 frame load reads 0.5 kN, not 0. The handbook has a page of its own, "Oiling
  machines" (`config/handbook/machineoil.json`): which machines, which oils, what dry means and how
  long a tank lasts. Its text quotes the default settings.

| Setting | Default | |
|---|---|---|
| `OilLiquids` | the four patterns above | Liquid codes (`domain:path`, `*` wildcards) that oil a machine |
| `OilLumps` | `game:fat-rendered`: 0.5 | Solid oils by the lump: code pattern and litres a lump |
| `DryResistanceMultiplier` | 3 | What a dry machine's shaft load is multiplied by (1 to 100) |
| `HelveHammer` | tank 1000, 0.1 a strike | |
| `Pulverizer` | tank 1000, 0.5 an item | |
| `Sawmill` | tank 1000, 2 a log | Immersive Woodworking's plank sawmill |
| `Chopper` | tank 1000, 1 a log | Immersive Woodworking's powered chopper |
| `BuckingMill` | tank 1000, 2 a stored log | |
| `Rosser` | tank 1000, 2 a stored log | |
| `GearCutter` | tank 1000, 10 a gear | A large gear drains double; never a dry load ("Gear cutter") |
| `DrawBench` | tank 1000, 2 a pipe section | Four pipe sections a hollow, 8 points ("Draw bench") |

Each machine's entry is `{ "Tank": points, "DrainPerJob": points }`; a value out of range falls back
to its default with a warning.

**Why these sizes.** Every tank holds 1000 points, one 10 litre bucket, so "a bucket fills a dry
machine" holds for all six and the block info reads the same everywhere. The drains are set so
that a full tank lasts several days of steady play at a busy workshop's pace, by each machine's
own rate of work: a helve hammer strikes about three times a second at speed 1, so 0.1 a strike
is 10 000 strikes (some 50 minutes of hammering, a few weeks of smithing sessions); a pulverizer
2000 items; the plank sawmill 500 logs and the chopper 1000 (firewood goes through it fastest);
the bucking sawmill and the rosser 500 stored logs (a thin trunk stores about 10). Oil is not
cheap: vanilla flax oil comes 0.1 litre a cooking pot, a press gives more, and tallow is half a
litre a lump, so a bucket is a real cost that is paid rarely.

**How.** The four foreign machines are patched (`MachineOil/ForeignMachines.cs`), on both sides
and once per process (own Harmony id `seraphhorizons.machineoil`): their block entities cannot
carry a field of this mod's, so the tank is kept beside each block entity and written into, and
read from, its tree under `seraphhorizons:oil` (postfixes on `ToTreeAttributes` and
`FromTreeAttributes`), which saves it with the world and syncs it to clients. The targets:

| Machine | Load (postfix) | Drain | Pouring (prefix) | Tank and info |
|---|---|---|---|---|
| Helve hammer | `BEBehaviorMPToggle.GetResistance` (the wooden toggle carries the hammer's load; it finds the hammer on either side) | `BEHelveHammer.onEvery25ms`, prefix and postfix: a strike is the swing (`accumHits`) wound back a quarter turn, onto an anvil whose `WorkItemStack` was set | `BlockHelveHammer.OnBlockInteractStart` | `BEHelveHammer.To/FromTreeAttributes`; `BlockEntity.GetBlockInfo` for the hammer alone (it has none of its own) |
| Pulverizer | `BEBehaviorMPPulverizer.GetResistance` | `BEPulverizer.Crush` (one item a call) | `BlockPulverizer` and `BlockMPMultiblockPulverizer.OnBlockInteractStart` | `BEPulverizer.To/FromTreeAttributes`, `GetBlockInfo` |
| Sawmill | `BEBehaviorSawmillMP.GetResistance` (its private `Master`) | `BlockEntitySawmill.CompletePass`, prefix and postfix: a pass that empties the input slot | `BlockSawmill` and `BlockSawmillGhost.OnBlockInteractStart` (the power ghost inherits it) | `BlockEntitySawmill.To/FromTreeAttributes`, `GetBlockInfo` (the ghosts pass theirs to it) |
| Chopper | `BEBehaviorChopperMP.GetResistance` | `BlockEntityChopper.CompletePass`, likewise | `BlockChopper` and `BlockChopperGhost.OnBlockInteractStart` | `BlockEntityChopper.To/FromTreeAttributes`, `GetBlockInfo` |

A click on a multiblock cell finds the machine through the cell's `Principal` (`BEMPMultiblock`,
Immersive Woodworking's `BEMultiblockSawmill` and `BEMultiblockChopper`). Immersive Woodworking is
found by name; if any of its members is missing, its two machines are left as they ship with a
warning, and the game's two are oiled regardless. The bucking sawmill and the rosser keep their
tank themselves (`BEBuckingMill`, `BERosser`, the same tree key), check for oil first in their
`OnInteract`, drain in `FinishCut` and `Deliver`, and multiply in their own MP behaviours. The
shared maths (tank, drain, code patterns, settings) is in `Machines/Core/MachineOil.cs`, the
shared game side (tree, pouring, info, smoke) in `Machines/Game/Oil.cs`. The client smokes the
foreign machines from one tick of `MachineOilSystem`, the two of this mod's from their own.

The server decides: it writes the tank into the tree only with the switch on, and a client shows
and smokes only a tank it was sent. Which held item is oil is judged on each side by its own
settings (the client only says the click is taken; the server pours). With the switch off nothing
is patched, the mill and the rosser keep no tank, every machine loads its shaft and works as it
did before, tanks already saved are dropped at the next save, and the handbook page is hidden (the
client removes it from the handbook; the server lists it under the hidden guides key, so the
recipe export leaves it out).

### Trunk entities (`TrunkEntities`, `TrunkEntitiesSettings`)

Logging Expanded (`loggingmod`, 0.3.6); Carry On (`carryon`) optional. Logging Expanded's tree
trunks are never items in an inventory. A felled tree leaves a trunk entity lying on the ground
(`seraphhorizons:trunk-thin` or `-thick`, shown, boxed and selected as the machines show trunks:
Logging Expanded's `lg` model, 1 × 1 × 4, up to 24 logs, its `xxl` model, 2 × 2 × 5, above). It
holds the trunk's own stack, weighs 10 + 8 per log, floats and drifts in water, and is solid to
whoever walks into it. Right-click it with an empty hand to take that end and drive it on foot like a
sled (you are mounted on it, standing just beyond the end and facing along it: W pushes it away
from you, the far end leading, S draws it back as you walk backwards, A and D turn it about its
middle; a walk at one log, half of it at 48, it steps up one block, and sneak lets go; your client
moves it, so the keys act at once, and with Cartwright's Caravan you use its sled pusher's
animations; no one else can touch a trunk while you drive it), or tie a
rope to it as to any rope-tieable entity, where it pulls from the nearer end. Both are easier in
water. A knife, shears, an axe or a saw held on
it works it by Logging Expanded's rules for a placed trunk, and Immersive Woodworking's bark spud
debarks the whole trunk in one hold of half a second per log and drops each log's bark (the debarked
trunk is the `Rosser` switch's). With Carry On, sneak + right-click shoulders it into Carry On's
hands, at a walk speed falling from 0.8 (4 logs) to half (48 logs), with Logging Expanded's
own `trunkcarry` or `trunkcarryheavy` animation; putting it down, or dropping it, lays a trunk
entity again. Carried, it loads a sawhorse, a Trunk Storage Rack, a heating rack, a rosser or a
bucking mill, or goes into a cart's or sled's storage slot; unloading any of them, or taking it off
a cart, puts it back in the hands. Trunk Storage Racks can no longer be carried. The rosser and the
mill also take a trunk entity lying in their infeed cells, after a rack or a rosser in line there.

Every trunk item entity is swapped for a trunk entity as it spawns or loads, so felling, a station
unloading onto the ground and a broken machine all leave one; a trunk is never given to a survival
player's inventory (picked up, unloaded or taken back), though one already in a slot moves freely
and thrown out becomes a trunk entity; and **trunk multiblocks already placed in a world are deleted as
they load, with nothing given back**. Without Carry On (one warning) nothing goes through hands:
trunks are driven, roped and worked where they lie, the rosser and mill take them from the ground
and lay them there on Ctrl, and Logging Expanded's stations and the carts take none. With the switch
off, Logging Expanded missing or not as expected, nothing changes and its trunks are items as it
ships them (trunk entities already in a world turn back into trunk items as they load); the server
decides and a client follows it through the world config (`seraphhorizons:trunkEntities`), as
`UnifiedWoodworking` does. Nothing of Carry On, Logging Expanded or Cartwright's Caravan is
referenced at build time.

`TrunkEntitiesSettings` holds its figures (weight per log, the carry speeds at 1 and 48 logs, the
spud's seconds per log; the drive's figures are constants); values
out of range fall back to the default with a warning. Everything else is in
[`TrunkEntities/README.md`](TrunkEntities/README.md): the entity and its boxes, the spawn swap, the
drive (and why the server, not the driver's client, moves the trunk), Carry On (pick-up, speed, animation, put-down, drops, racks, carts, and the `CarryableInteract`
that lets a carried trunk's click through to a station), each tool's rule, the stations and the
machines' ground pull, old worlds, the settings table, the tests and what is not checked in the game.

### Gear reclamation (`GearReclamation`, `GearReclamationSettings`)

The gears epic (#484): the gear items (#474), degreasing (#475), and neutralizing, oiling and the
roll (#477). The pickling tub and brine bath, the consumer patches, steel bits recovery and the gear
blanks follow in sections of their own; the gear cutter is still to come. Rusty gears are salvage.
They are reclaimed a batch at a time into steel gears, one in ten sound, the rest steel bits:

| Step | Where | In | Out |
|---|---|---|---|
| Boil | cooking pot over a fire | rusty gears + 0.25 L of an alkali a gear | `seraphhorizons:gear-degreased` |
| Pickle | the pickling tub (its own step) | degreased gears + acid | `seraphhorizons:gear-pickled` |
| Neutralize | barrel, sealed 2 h | pickled gears + 0.1 L lime water (`game:limewaterportion`) a gear | `seraphhorizons:gear-neutralized` |
| Oil | barrel, sealed 4 h | neutralized gears + 0.1 L lard (`expandedfoods:lard`) or 0.2 L hardened lard (`expandedfoods:hardlardliquid`, 1 item) a gear | `seraphhorizons:gear-oiled` |
| Roll | a player's inventory | oiled gears | `seraphhorizons:gear-steel` with `UsableGearChance`, else `BitsPerFailedGear` × `game:metalbit-steel` |

**The gears** (`itemtypes/gear.json`, one item type `gear` with `type` steel, degreased, pickled,
neutralized, oiled) are the game's rusty gear shape (`game:item/gear-rusty`) with its one texture,
`rusty-iron`, replaced: the steel gear with the steel ingot's (as ppex's steel gear does),
degreased with the game's dull corroded metal, pickled with the iron ingot's bare grey,
neutralized with blister steel's darker grey, oiled with stainless steel's sheen. No new textures.
They stack 64, are ground-storable in the rusty gear's pile, shelvable and display-caseable like
it, and are in the general and items tabs. `largegear-steel` (`itemtypes/largegear.json`) is the
game's large temporal gear shape (`block/machine/jonas/tempgear`) with both its textures steel; it
has no recipe yet (the cutter makes it). The rusty gear itself is unchanged except for a handbook
section (`patches/gearreclamation-rustygear.json`): "Boil, pickle, neutralize, oil. One in ten
comes out sound." Each reclaimed gear's handbook page has a section pointing to the guide page
"Gears: reclaiming, cutting, rusting" (`config/handbook/gearreclamation.json`, #483), which tells
the whole chain with the chemistry as its story (degrease, pickle, neutralize, oil, the one in ten,
steel bits back through the coffin or the Bessemer, blanks, the gear cutter's ten stages and master,
the brine bath) and quotes the default settings; so do the bare steel gear, the blanks, their molds
and the pickling tub. Tidy Variants leaves the five gears as single tiles (its Atlas report;
they are not variants of one thing to the player, so no override groups them).

**Flash rust.** Pickled and neutralized gears carry the game's Perish transition to
`game:gear-rusty`, ratio 1: fresh for `FlashRustHours`, then rusting over a quarter of that again
(8 and 2 hours by default), after which the stack is a stack of rusty gears. Perish runs at the
game's rates, so a cellar slows it as it slows food. The barrel carries the pickled gears' freshness
over to the neutralized ones (vanilla `CarryOverFreshness`), so the clock runs from the tub, not
from the barrel. The hours are set on the server after the items load
(`GearReclamationSystem.AssetsFinalize`) for every item with the attribute
`seraphhorizonsFlashRust: true`, so another step's bare gear follows the setting by carrying it.

**Degreasing is a cooking recipe** (`recipes/cooking/gear-degrease.json`): the game's cooking pot
returns a plain item stack cleanly (`cooksInto`, as the game's candles and glue do), so no barrel
fallback was needed. The pot cooks by servings, every slot of an ingredient holding the same number
of items, and a recipe's output per serving is fixed; so there are three recipes, the gears in one,
two or three slots, each a quarter litre of alkali per gear: 4 gears and 1 litre in one slot, 2 + 2
gears and 1 litre, 3 × 2 gears and 1.5 litres. A clay pot holds 6 servings (up to 18 gears in three
slots), a metal pot 24. The liquid must be exact, as for every cooking recipe. Afterwards the pot is
dirty, as after candles. The game has no lye of its own: the alkalis are Oils Resoaped's lye
(`oils:lyeportion`) and Expanded Matter's caustic soda and washing soda (`em:causticsodaportion`,
`em:washingsodaportion`, the issue's `em:washingsoda`). Each is dropped from the recipe when its
mod is not loaded (no domain), and a recipe left with none is left out with a warning.

**The oil step.** The game's tallow, rendered fat, is a solid item with no liquid form in the
game, and a barrel holds one solid and one liquid, so it cannot go in by the lump beside the gears.
Expanded Foods' lard is the liquid form of rendered fat (melted, or hardened, which spills as
rendered fat), so both lards oil. A barrel recipe whose liquid's mod is not loaded is left out.

**The roll.** A barrel recipe cannot roll dice, so `gear-oiled` is a lottery item. Its item class
(`ItemReclaimedGear`) hooks `OnModifiedInInventorySlot`, which the game calls whenever a stack's slot
changes (taken from the barrel, picked up, moved, merged, given): on the server, in a slot of a
player's own inventory (`InventoryBasePlayer`, the mouse cursor included, the creative inventory
not), the slot is resolved on the next tick, after the move that put it there is done. Each gear is
one draw of the server world's random: sound gears stay in the slot, bits go to the player's
inventory or drop at their feet, so nothing is lost and no oiled gear is left. Every 2 seconds the
server also looks through every online player's inventories, and through a player's when they join,
for an oiled gear the hook missed (a direct write to a slot). The game's stack randomizer resolves
through `IResolvableCollectible`, which only fills one slot; the oiled gear does not implement it,
as it needs to give two stacks (an oiled gear in a chest, on the ground or in a barrel stays oiled
until a player takes it). The arithmetic is in `GearReclamation/Core/GearReclamation.cs`.

| Setting | Default | |
|---|---|---|
| `FlashRustHours` | 8 | In-game hours a pickled or neutralized gear stays bare before it rusts (0.5 to 8760); it is rusty a quarter as long again after |
| `UsableGearChance` | 0.1 | Chance each oiled gear is sound (0 to 1) |
| `BitsPerFailedGear` | 1 | Steel bits for each oiled gear that is not, and for each bare steel gear that rusts through (0 to 20) |
| `FlashRustLossChance` | 0.25 | Chance each bare steel gear that flash-rusts rusts through to steel bits instead of into a rusty gear (0 to 1) |

With the switch off (the server decides), the gear items still exist, since other steps and
mods' recipes name them, but the three steps' recipes are left out, oiled gears are never rolled,
the rusty gear has no salvage section and the guide page is hidden (from the client's handbook, and
from the recipe export through the hidden guides key, as machine oil does).

### The pickling tub and the brine bath (`PicklingTubSettings`)

Part of gear reclamation (#484), under its switch: the tub is its pickling step (#476) and the end
game's brine bath (#482). `seraphhorizons:picklingtub` is a wooden tub lined with pitch, one block, open at the top,
crafted from six boards, a nails and strips and two of Immersive Woodworking's bark tar
(`immersivewoodworking:barktar`, the lining); without Immersive Woodworking the tub has no recipe.
It holds one liquid, up to `CapacityLitres` (10), and one batch of up to `BatchSize` (8) gears, and
shows both: the liquid's surface at its level and the gears lying in it, as they are now.

- **Liquid.** Right-click with a bucket, jug or any liquid container of a liquid a rule names: as
  much as fits goes in, in whole items, taken from the container as a barrel takes it. One liquid at
  a time; another is refused, and so is one that does nothing for the gears waiting in the tub. An
  empty container takes the free liquid back out.
- **Gears.** Right-click with gears a rule takes, up to the batch size from the stack. The batch
  starts when the tub holds a liquid with a rule for it and a litre of it (`LitresPerBatch`), which
  goes into the batch. Gears with no liquid for them wait. More of the same gears added to a running
  batch start its clock again; other gears are refused until the batch is out, and so is more for a
  finished batch. The large steel gear (`RefusedGears`, `seraphhorizons:largegear-*`) is refused
  with a word: it has no currency form.
- **Taking out.** Right-click with an empty hand. Before done, the gears that went in come back
  unchanged and the batch's litre goes back into the tub. From done, what the batch became: its
  litre is used up. Breaking the tub drops the batch the same way and spills the liquid.
- **Block info:** the liquid, the batch and its progress; at done "the metal looks grey and clean"
  (a pickle) or "rusted through" (brine), and how many gears the acid has eaten. The tub's handbook
  page says how it works and that the acids are one part to ten of water.

The rules are a table (`AcidRules`): a liquid code pattern, the gear in and the gear out, the hours
to done, a grace after done, the hours between gears lost after the grace, and what a lost gear
becomes (`Failure`, `FailureQuantity` of it). The defaults:

| Liquid | In | Out | Hours | Grace | A gear lost every |
|---|---|---|---|---|---|
| vinegar (`game:vinegarportion`; Expanded Foods makes it) | `gear-degreased` | `gear-pickled` | 24 | 12 | 3 h |
| sulfuric acid (`game:acid-full-sulfuric`) | `gear-degreased` | `gear-pickled` | 8 | 4 | 1 h |
| hydrochloric acid (`game:acid-full-hydrochloric`; Expanded Matter) | `gear-degreased` | `gear-pickled` | 2 | 1 | 15 min |
| vinegar | `gear-steel` | `gear-steel-bare` | 6 | 3 | 45 min |
| sulfuric acid | `gear-steel` | `gear-steel-bare` | 2 | 1 | 15 min |
| hydrochloric acid | `gear-steel` | `gear-steel-bare` | 0.5 | 0.25 | 3.75 min |

Every lost gear is one `game:metalbit-steel`. Past done plus the grace, the first gear goes and then
one every interval, until only bits are left: an hour of game time is two real minutes at the
default speed, so a batch of eight in hydrochloric acid is gone a quarter of an hour of play after
its grace. Brine (`BrineLiquids`, `game:brineportion`) has two rules of its own, built from three
settings: `gear-steel` rusts into the game's `gear-rusty` in `BrineRustHours` (48), a bare steel
gear in `BareBrineRustHours` (4), and each gear has `OverRustChance` (0.1) of over-rusting to a
steel bit instead, rolled when the batch starts and shown at done. Brine eats nothing by time. The
rusty gear is the game's own, full currency.

`seraphhorizons:gear-steel-bare` is the one item the tub adds: a steel gear after the dip, the
rusty gear's model in bright steel, with the bare gears' flash rust (a perish transition to
`game:gear-rusty`, its hours from `GearReclamationSettings.FlashRustHours` through the
`seraphhorizonsFlashRust` attribute). A steel gear must not become a rusty gear for nothing, so its
flash rust loses a share (#477, #482): when the transition completes, the gear reclamation item
class it shares (`ItemReclaimedGear.OnTransitionNow`) rolls each gear as the oiled gears are rolled,
a rusty gear with 1 − `GearReclamationSettings.FlashRustLossChance` (0.75) and otherwise
`BitsPerFailedGear` steel bits. The rusty gears keep the slot (the bits, if none is); the bits go to
the player whose inventory it is, else to another slot of the same inventory, and what does not fit
drops at the player, the container's block or the dropped stack. The other gear items are gear
reclamation's; the tub only names their codes.

**How.** `PicklingTub/Core/` holds the rules and the batch, with no game: `TubConfig.cs` (the
settings and their sanitising: a value out of range falls back to its default, a broken rule is
dropped, each with a warning), `TubRules.cs` (matching a liquid and a gear to a rule, what may go in)
and `TubBatch.cs` (a batch is its gears, its liquid, its start hour, its litre and the loss rolled at
the start, and its stage at any hour is a function of those: waiting, soaking, done, eating,
dissolved). `PicklingTub/Game/` holds the system (registration; on the server, with the switch off,
the tub, its recipe and the bare gear are disabled before the game loads them), the block, the block
entity and the meshes of the gears in the tub. The block entity keeps the batch's rule with it, so a
running batch keeps its timings when the settings change and a client reads them from the tree. Its
server tick only starts a waiting batch and resends the tree when the stage changes; the clock is
the calendar's total hours. The liquid in the tub does not perish.

With the switch off, the tub, its recipe and the bare steel gear do not exist, and tubs already
placed are lost. The server's setting decides. `tests/PackTests/PicklingTubScenarios.cs` (Atlas)
fills a tub with each acid and brine, adds gears, ages the batch by moving its start back, and takes
it out early, at done and eaten; rusts steel gears both ways; and counts the over-rusted share of
320 gears. `SwitchesOffScenarios` requires no tub, bare gear or recipe with the switch off.

### Machines take the steel gear, not the rusty gear (`GearConsumers`)

The gears epic (#484), its first step (#473). A rusty gear (`game:gear-rusty`) is salvage and
money: traders still take it at its currency value, but nothing is built from one. Every recipe in
the pack that took one takes the pack's steel gear (`seraphhorizons:gear-steel`, #474) in the same
number, and Pipes and Power Expanded's anvil gears go, so no hand-forged shortcut remains:

| Mod | Recipes (steel gears per recipe) |
|---|---|
| The game | glider (2) |
| Pipes and Power Expanded | Cornish engine (4), Watt engine (2), fluid pump (1), manual fluid pump (2), mechanical power generator (4), valve (2), pressure valve (4; both off with `UnifiedPipes`, whose bronze valves take no gear) |
| Steelmaking Expanded | bell hopper (8), air blower (2), converter transmission (16) |
| BetterRuins | Jonas gearbox (3, 5), oscillator (3), spring (2), gears (3, 5); the unlit Jonas lamps (1) |
| Immersive Woodworking | sawmill carriage (1) |
| Flying Machine | chain drive (2) |
| Player Corpse Forked | corpse compass (4) |
| Spinning Wheel | the wheel's mechanism (1) |
| Sprinklers | tier one sprinkler (2), tier two (4) |
| Butchering | temporal butcher box (16; it ships switched off) |

The pack's own machines pay the same way, in their own recipes rather than a patch (so whatever this
switch says): the draw bench frame 2, for the gearing it carries ("Draw bench"), and the gear cutter
frame 3 ("Gear cutter"). The first steel gears come from reclamation, so neither waits on the cutter.

ppex and smex give each machine two recipes, one taking the rusty gear and one ppex's gears
(`ppex:gear-*`); the first takes the steel gear and the second is switched off. ppex's anvil gears
and large gears (`ppex:gear-*`, `ppex:largegear-*`) are not smithed any more and are hidden from
the creative inventory and the handbook. Smex's Bessemer converter is raised from its control block
with a large gear in the hotbar, named in its code (`BlockEntityConverterControl.IsSpawnGear`, and
`BlockConverterBessemer.GetDrops` gives it back when the vessel is broken): Harmony postfixes make it
take, and give back, the steel large gear (`seraphhorizons:largegear-steel`, cut on the gear cutter,
#480) instead, on both sides (the client checks the hotbar before it sends the click), and the
converter's refusal and its handbook page say so. If smex has changed those methods, a warning is
logged and the converter keeps taking ppex's large gears, which are no longer made.

Left alone: the rusty gear amulet (the gear on a string, uncrafted back into it), the game's gray and
black barrel dyes (the rust is the pigment), BetterLoot's gear parts (change: four make a gear) and
Cartwright's rusty gear sign (decoration). The game's own Jonas devices, and Abyssal Depths, Rickety
Translocation, Industrial Deco and the walking stick, take temporal gears or Jonas parts, no rusty
gear, so they are not patched. The rusty gear stays currency, and the mechanic no longer buys or
sells ppex's gears (#436).

JSON patches, `patches/gearconsumers-{modid}.json`, one per mod patched, each `dependsOn` that mod
(the game's has none), rewrite the ingredient's code or add `"enabled": false` to the recipe. With
the switch off, `GearConsumers.DisablePatches` empties them in `Start`, as for the other patch
tweaks, and the converter is not patched.

`tools/tests/test_gear_consumers.py` reads every locked mod's recipe files from its zip in
`build/mods` (and the game's, when `VINTAGE_STORY` is set) and fails on any ingredient taking a
rusty or ppex gear that no patch covers and is not on its exemption list, and on any patch whose
target has moved, so a mod update that adds a use fails it. CI runs it on the Atlas job's `rest`
shard, where the mods are fetched; it skips without them. `tests/PackTests/GearConsumersScenarios.cs`
(Atlas, `SharedWorldScenarios`) requires every patch to apply and resolve, no registered grid or barrel recipe and no
exported recipe of any type to take a rusty or ppex gear outside the exemptions, the patched recipes
to take the steel gear in their numbers, ppex's gears to be neither smithed nor shown, and the
converter to take and give back the steel large gear; `SwitchesOffScenarios` requires all of it
undone with the switch off.

### Steel bits back into steel (`SteelBitsRecovery`)

The game and Steelmaking Expanded (`smex` 0.10.1), part of the gears epic (#484, #478). A steel bit
(`game:metalbit-steel`) melts at 1502 °C, and the hottest fuel the game has, coke, burns at 1340 °C:
no forge, firepit or crucible melts one, so in the game steel bits are dead ends. Reclaiming rusty
gears leaves mostly steel bits, so two ways take them back into steel.

**The cementation furnace** (the game's stone coffin). What the coffin takes as an ingot is data,
not code: any item whose attributes have `carburizableProps` (in the game only the iron ingot),
turned into its `carburizedOutput` one for one when the firing completes; it holds 16 such items,
filled 4 at a time between 5 layers of 8 coke or charcoal, and fires only full. So:

- **Packed steel bits** (`seraphhorizons:steelbitcharge`, `itemtypes/steelbitcharge.json`): 20 steel
  bits, the bit's own `smeltedRatio`, with `carburizableProps` out `game:ingot-blistersteel`. Made
  shapeless in the crafting grid from one slot of 20 bits (`recipes/grid/steelbitcharge.json`). The
  item exists whatever the switch says, so charges already made are never lost.
- **Bits straight in.** Holding 20 or more steel bits, the sneak-click that puts an ingot in puts 20
  bits in as one packed charge, and a stack goes in 20 a click. With fewer, it is refused ("Each
  ingot's place takes 20 steel bits") and nothing is taken.
- A full coffin is 16 charges, **320 bits, and gives 16 blister steel ingots**, which the anvil
  works into steel as usual: exactly the 20 to 1 a steel bit would smelt at. Bits and iron ingots
  do not share a coffin (the game's own "Cannot mix ingots"), so a coffin is all iron or all bits;
  packed charges and bits put in directly are the same item and mix. A coffin broken unfired drops
  the charges, not the bits. The coffin draws its contents as iron ingots whatever they are.

`SteelBits/SteelBitsSystem.cs` (Harmony, own id `seraphhorizons.steelbits`, on both sides, once per
process: the client predicts the click) patches one method, the coffin's private
`BlockEntityStoneCoffin.AddIngot(ItemSlot)`. Its prefix, when the held slot holds steel bits, hands
the game's method a slot holding one packed charge in place of the held one, so every check and
message is the game's (full, mixed, the move into its slot, the redraw); its postfix takes the 20
bits from the held slot only if the method returned true, which it does only when the charge went
in. If the method is not found, a warning says so and bits go in only packed.

**The Bessemer converter** (Steelmaking Expanded). It takes cold scrap with a raw iron charge before
the blow, by its `BessemerScrapCodes` setting (`ModConfig/ex_values.json`, section `smex`); each
item is worth `MoltenUnitsPerBit` units, 5, and a steel ingot's mold `MoldDefaultUnits`, 100, so
**a steel bit is a twentieth of an ingot, returned as steel unit for unit**, and costs the bath
`BessemerColdScrapLossCoefficient` × 5 = 4 °C, which more blast pressure buys back. Its default
list is `game:metalbit-iron,game:metalbit-steel`, so nothing needs shipping. In `AssetsFinalize`,
after smex has read its file in `Start`, `SteelBits/SmexScrap.cs` reads the live setting by name
(`SteelmakingExpanded.SmexValues`); if it does not list the steel bit, it adds it to the live
setting (`SmexValues._store.Config`), not to the file, with a log line. exlib sends the server's
live settings to every client that joins. Without smex nothing is done; if its members are not as
expected, a warning says so and its setting decides.

**Handbook.** The steel bit's page, and the packed bits', get a section, "Back into steel", with
both ways (`patches/steelbits-handbook.json` adds it to the steel bit alone, by
`extraSectionsByType`, since the metal bits share one item file; the pack's Tidy Variants keeps
each bit's own page openable).

With the switch off nothing is patched, the coffin refuses bits as the game does, the recipe and
the steel bit's section are left out (the patch file emptied and the recipe disabled in `Start`),
and smex's setting is as its file says. Packed steel bits made before still go into a coffin by
their own attribute.

### Steel gear blanks (`GearBlanks`)

Stock for the gear cutter (gears epic #484, #479): `seraphhorizons:gearblank-steel`, a steel disc
with a bore the size of the game's rusty gear (6.7 voxels across, 1.4 thick), and
`seraphhorizons:largegearblank-steel`, one the size of the large temporal gear (10.2 across, 2
thick). Both stack (32 and 8), go on a shelf or in a display case, and can be set on the ground
(small ones in quadrants, a large one in the middle). They sit in the creative inventory's items
tab, the molds in its construction tab, as the game's molds do. Two ways to make one:

- **Cast.** Clay-form a gear blank mold (a floor and the walls round a round cavity with a core for
  the bore, one layer deep) or a large one (two layers deep) from blue, red or fire clay, and fire
  it in a pit kiln or a beehive kiln as the game's tool molds fire. Pour 100 units of molten steel
  (one ingot) into the small mold from a crucible, 200 into the large one, and right-click the blank
  out once it has hardened; the mold stays. A mold takes steel only: there is no blank of any other
  metal, and the game's mold refuses a metal its drop has no item for.
- **Forged.** Smith one at the anvil from a steel ingot (36 voxels of the ingot's 42), or a large
  one from two (84 voxels, exactly two ingots: put the second hot ingot on the work piece). The
  helve hammer forges both, as it forges a plate.

The molds are the game's own: `seraphhorizons:toolmold-{color}-{raw|fired}-{gearblank|largegearblank}`,
on its `BlockToolMold` and `ToolMold` block entity, with the attributes its tool molds have
(`requiredUnits` 100 and 200, `fillHeight` 1 and 2, `drop` `seraphhorizons:gearblank-{metal}`, the
pit kiln's `combustibleProps` and the `beehivekiln` table). So the crucible pours into them as into
any tool mold (`ILiquidMetalSink`), and Steelmaking Expanded's canal pedestal, which takes any small
`BlockToolMold` (any but the anvil and helve hammer molds, which go on its tap), takes both and casts
in them; by smex's source, its own patches on the tool mold (the held metal's render, a hardened
casting handed over, an unfinished one picked up with its metal) apply to them as to the game's.
Nothing of smex is referenced. Unlike a crucible, smex's pedestal does not check the metal against
the mold's drop, so a canal of iron fills a gear blank mold with iron that casts nothing; smex then
lets the mold be picked up with its metal. A crucible held over a gear blank mold with any metal
but steel logs a warning from the game each time it asks (it resolves the drop to ask), as it does
for the game's molds with a metal they have no item for.

The handbook text is under the key the handbook builds for an item of another domain,
`seraphhorizons:item-handbooktext-seraphhorizons:gearblank-steel`: a lang key with a colon is taken
as it is, so the file carries the domain twice.

The helve hammer works a work item only when the selected smithing recipe's name is `plate` or
`blistersteel` (`ItemWorkItem.GetHelveWorkableMode`, which the anvil asks on every helve hit; any
other recipe is `NotWorkable`). Both blank recipes are named `plate` (`recipes/smithing/gearblank.json`):
the recipe's name is otherwise unused by the game, and the recipe browser's export tells them from
the game's plate by their file. A smithing recipe takes one ingot; a second goes on the work item as
for any recipe of more than 42 voxels, and the helve hammer finishes the large blank only once both
are on (it moves the voxels it has into the recipe's shape, and cannot make more).

All of it is assets on the game's classes (`Gears/GearBlanks.cs` holds the codes): with the switch
off, the server marks the two item types, the two mold block types and both recipe files disabled
before the game loads them, so neither the blanks nor the molds exist, and those already in a world
are lost.

### Gear cutter (`GearCutter`, `GearCutterSettings`)

The end of the gears epic (#484; the machine is #480, its cutter kit and oil #481): a mechanically
powered generating gear cutter that cuts steel gear blanks into steel gears (12 teeth) and large
steel gears (20). Its model, rig and generator are described in `GearCutter/README.md`; this is the
gameplay, in `GearCutter/` (rules in `GearCutter/Core/`, the game side in `GearCutter/Game/`).

**Blocks.** `seraphhorizons:gearcutter-frame-{side}` is the controller, placed as the mill and the
rosser are (`Footprint.PlacedFacing`: the machine runs away from the player), with a ghost in each of
the rig's other seven cells (`gearcutter-ghost`) and the power ghost
(`gearcutter-ghostpower-{side}`), which takes the axle on the column's back face (native west).
The frame is a grid recipe: eight steel ingots (four in each ingot slot; steel only, as the end of
the gears epic), four planks and 32 iron (or meteoric iron or steel) nails and strips, with a hammer.

**Stages.** Fitted by right-click on the frame or any ghost with the item, one item each, in this
order only; the next missing stage is the only one a click fills, and anything of a later stage is
refused with a message naming the next:

| # | Stage (`requires`) | Item | Made |
|---|---|---|---|
| 1 | `spindle` | `seraphhorizons:gearcutterspindle` | Grid: 2 steel rods over 2 steel plates over iron nails and strips |
| 2 | `feedscrew` | `seraphhorizons:gearcutterfeedscrew` | Smithing, 1 steel ingot |
| 3 | `camfeed` | `game:jonasframes-gearbox02` | Looted, or BetterRuins' conversion |
| 4 | `camindex` | `game:jonasframes-gearbox02`, a second | As above |
| 5 | `liftcam` | `seraphhorizons:gearcutterliftcam` | Smithing, 1 steel ingot |
| 6 | `index` | `seraphhorizons:gearcutterindex` | Grid: 2 steel rods, 2 steel plates, iron nails and strips in a row |
| 7 | `oiler` | `game:jonasparts-valve01` | Looted |
| 8 | `head` | `game:jonasframes-gears02` or `game:jonasframes-gears01` | Looted, or converted |
| 9 | `cutter` | `seraphhorizons:gearcutterkit-steel` (500 durability) | Smithing, 1 steel ingot |
| 10 | `master` / `masterlarge` | `game:gear-temporal` / `game:largegear-temporal` | Loot; never worn, never consumed |

The three smithing recipes are named after their part, not `plate`, so the helve hammer does not
work them. Items are taken from the hand outside creative mode; in creative mode Ctrl + right-click
on an incomplete cutter fits its next stage with nothing taken (the small master, a new kit).
**Ctrl + right-click** takes back the cutter kit, with what durability it has left; then a blank on
the arbor (its cut lost); then the master. Every other stage comes back only by breaking the frame,
which drops the frame, every fitted item by code, the kit with its durability and the blank. A save
keeps every fitted code and the kit's durability.

**Work.** Complete is every stage in, the kit with durability left. A blank goes on the arbor by
right-click, or from a chest or hopper (any container) against the infeed face (native north, the
gearbox end, the end nearest the placed block), one at a time while the shaft turns. The master
decides the size: the temporal gear takes `seraphhorizons:gearblank-steel` and cuts
`seraphhorizons:gear-steel`, the large temporal gear takes `seraphhorizons:largegearblank-steel` and
cuts `seraphhorizons:largegear-steel`; the other size is refused with a message. The cut needs the
shaft at `MinSpeed` and advances by its angle as the mill's saws do (`ShaftClock.AngleAdvance`), one
tooth per `TurnsPerTooth` turns: 144 turns a small gear and 240 a large one at 12. The finished gear
goes into a container against the output face (native south, the cell beyond `output.pos`), else
drops just outside that face, and the next blank comes in. W, the rig's `work`, is the teeth cut;
the server syncs it every tenth of a tooth and the renderer runs it on with the shaft between.

**Oil and wear (#481).** The cutter is a MachineOil machine (`MachineOilSettings.GearCutter`: tank
1000, 10 points a gear, a large one 20), filled by right-click with oil, but it opts out of the dry
load: its `Resistance` is the same oiled or dry. Oil wears the kit instead: when a gear finishes the
kit loses `CutterWearPerGear` / fill points (the tank's fill then, before that gear's oil is
drained), times 20/12 for a large gear, rounded up: full, 10; half, 20; a tenth, 100; empty, all
it has left, so it breaks on that gear (with the tool-break sound; the gear still comes out). A
spent kit stops the cutter with the blank on until a new kit is fitted; the cut resumes where it
was. With MachineOil off there is no tank and the kit wears at its base. Steady at a full tank a
kit cuts 50 small gears; cut from full without topping up (the tank falling 10 a gear) about 40. A
dry cutter smokes while it cuts. The block info shows the next stage, the master, the kit's
durability and about how many gears it has left at this fill, the cut's teeth, MachineOil's oil
line with its load, the same oiled or dry (never the dry line: `Oil.Asked` with `dryLoad: false`
records it unmultiplied) and the wear multiplier, or that the next gear breaks the kit.

**Drawn.** The block draws `gearcutter_frame.json`; the renderer splits `gearcutter.json` by element
name into the rig's parts and draws each whose `requires` is fitted: the cover always, a blank's
parts while that blank is on the arbor, one master's while it is fitted. θ is the power ghost's
angle about native x; k is the master's class (held while its presence eases out); the reservoir's
oil follows the tank's fill (`oil`). While it cuts: chips and sparks at `chips.pos`, and with oil
in the tank a spray from the injection valve's nozzle at `drip.pos`.

| Setting | Default | |
|---|---|---|
| `TurnsPerTooth` | 12 | Axle turns per tooth; the rig's `cut.turnsPerTooth` (a test holds them together) |
| `CutterWearPerGear` | 10 | Kit durability a small gear costs at a full tank |
| `Resistance` | 0.2 | The complete cutter's load, as the rosser's |
| `MinSpeed` | 0.05 | Below it the cutter neither cuts nor takes a blank from its infeed |

With the switch off the server marks the three block types, the five new item types and both
recipe files disabled before the game loads them, so none of it exists and cutters already placed
are lost. The steel gears it makes exist either way. Not yet: the recipe export of the cut (#483),
and a schematic for the frame (`MachineSchematics`). The five new parts' items are drawn as the parts
are in the machine (`shapes/item/gearcutter/`, generated with the machine's model).

### Unified pipes (`UnifiedPipes`, `UnifiedPipesSettings`)

One pipe network for water, steam and exhaust: Pipes and Power Expanded's (`ppex` 0.7.1, on
ExpandedLib's `exlib` 0.8.4). Its straight, bend, T- and X-junction pipes come in **copper** and
**lead** as well as iron and steel: new states of their `material` variant, on ppex's own models with
`game:block/metal/sheet-plain/copper4` and `lead4` in place of its `iron4`
(`patches/unifiedpipes-ppex.json`). Every metal couples with every other (ppex joins pipes by family,
not metal), so a run may mix them. ppex's fluid intake, outlet and passthroughs have no metal and are
left as they are. The display name is ppex's, with the game's metal name: "Piping (Straight) (Copper)".

**Valves are bronze.** ppex's valve and pressure valve get tin, bismuth and black bronze states
(`sheet-plain/<bronze>4`); the creative inventory and the handbook list only those. The iron and steel
valves stay as blocks, so those already placed keep working, but nothing makes them any more.

**The chain.** Copper and lead pipe is made in three pieces, iron and steel pipe in one:

- The **angle**, `seraphhorizons:angle-{copper,lead}` ("Copper angle"): a plate bent once at a right
  angle, two legs 8 across the outside, 1 voxel thick, 8 long (`shapes/item/angle.json`, the press
  brake's contract), so two make the chute section's 8 x 8 box. Forged on the anvil from one ingot
  (`recipes/smithing/angle.json`: a 7 x 4 flange with a 7-long web two layers up its back edge, 42
  voxels, Hydrate or Diedrate 2.5.6's old pipe section pattern re-authored for this item), or folded
  from one plate on the press brake (`PressBrake`).
- The **hollow section**, named the **tube blank** in game, is the game's chute section
  (`game:chutesection-{material}`, a square tube;
  the game has copper only). `patches/unifiedpipes-chutesection.json` adds `lead` to its `material`
  variant (its texture is `game:block/metal/sheet/{material}1`, so lead takes its own sheet; iron and
  steel never make hollows), lists only copper in the creative inventory's mechanics tab, gives it a
  handbook section (`chutesection-handbook-*`), and switches off the game's two recipes for it: the
  anvil's (`game:recipes/smithing/chutesection.json`, one recipe, `/enabled`) and the grid's one-step
  "copper plate + 2 solder bars + soldering iron + hammer = 2 sections" (`game:recipes/grid/chute.json`
  5, the last). **2 angles + 2 tin or silver solder bars + a soldering iron (2 durability, as the
  game's soldering) = 1 chute section** of the metal (`recipes/grid/chutesection.json`) is the only way
  to one. The names are `game:item-chutesection-{metal}` keys in this mod's lang file ("Copper Tube
  Blank", "Lead Tube Blank": what it is to the player, the blank pipe is made from, though copper's
  still makes chutes), and the handbook and the machines' text call it the tube blank. The game's chutes take
  `chutesection-copper` by name, so lead makes no chute.
- The **pipe section**, `seraphhorizons:pipesection-{copper,lead,iron,steel}` ("Copper pipe
  section"): a square tube 6 voxels across (ppex's pipe is 6/16 wide) with 1-voxel walls, half a block
  long (`shapes/item/pipesection.json`), textured as ppex's pipes are
  (`game:block/metal/sheet-plain/{metal}4`). No grid or anvil recipe makes it: copper and lead ones
  come from a chute section on the mandrel station (2) or the draw bench (4), iron and steel ones from
  smex's canal in the pipe mold (`CastPipes`, 2 a fill of 100 units).

Per ingot of copper or lead: 1 angle, so half a hollow section, so 1 pipe off the mandrel station
and 2 off the draw bench; per ingot of iron or steel, 2 cast (`Pipes/Core/PipeSections.cs` holds the
figures).

**Chutes take solder.** The same patch makes the game's five chute recipes (`chute.json` 0-4:
elbow, straight, cross, T, 3-way) take a tin or silver solder bar per section (elbow 2, straight 2, T 3,
3-way 3, cross 4) and the soldering iron in their free cells (keys `S` and `T`, the pattern and size
replaced to fit); their yields and copper-only sections stay the game's. Better Ruins (0.6.4) makes
8 straight, 4 elbow, 4 T, 4 3-way and 4 cross chutes from sections and its Machinist's Mechanism
Blueprint with no solder (`betterruins:recipes/grid/schematic-mechanical/mechanical.json` 6-10):
`patches/unifiedpipes-betterruins.json` (`dependsOn` betterruins) switches those five off. The rest of
that file (riveted and plain metal blocks, sheets, fences, beams, chains, axles, gears, the hopper),
the blueprint, its trader entry and its schematic gate are left alone.

The guard (`Pipes/Core/ChuteSections.cs`), on the server before the patch loader runs: the item is coded
`chutesection` with its first variant group `material` holding copper alone, one `metaltex` texture by
`{material}` and no texture by type, a creative inventory, and no handbook attributes yet; recipe 5 of
`chute.json` makes 2 copper sections from a copper plate and solder, no other recipe there makes a
section, and recipes 0-4 make their chutes from copper sections alone in the game's patterns and sizes;
the anvil recipe is one recipe making a copper section from a copper ingot, not yet switched off.
If the game has changed any of the three files, one warning (`Unified pipes: the game changed ...`) and the whole
switch stands down as if off. With Better Ruins installed, its recipes 6-10 must each make their chute
from copper sections and the blueprint alone; if not, one warning (`Unified pipes: Better Ruins changed
...`) and only its patch is emptied (its chutes stay solderless, the rest goes ahead). `tools/tests/test_unified_pipes.py` holds the patch to the game's files
(with `VINTAGE_STORY` set).

**Recipes.** ppex's plate-and-nails recipes for the straight, bend, T- and X-junction pipes
(`ppex:recipes/grid/pipes.json` 0-3) and its iron and steel valves and pressure valves (7-10) are
switched off. Pipe is made from pipe sections laid in the game's chute patterns (one alone, an elbow, a
T, a cross): copper and lead soldered as a chute is (a solder bar a section and the soldering iron),
iron and steel banded with one nails and strips of their metal and a hammer
(`recipes/grid/unifiedpipes.json`). Chute sections make chutes and never pipe:

| Makes | Copper or lead | Iron or steel |
|---|---|---|
| 1 straight pipe | 1 pipe section + 1 solder bar + soldering iron | 1 pipe section + 1 nails and strips + hammer |
| 1 bend | 2 pipe sections (elbow) + 2 solder bars + soldering iron | 2 pipe sections (elbow) + 1 nails and strips + hammer |
| 1 T-junction | 3 pipe sections (T) + 3 solder bars + soldering iron | 3 pipe sections (T) + 1 nails and strips + hammer |
| 1 X-junction | 4 pipe sections (cross) + 4 solder bars + soldering iron | 4 pipe sections (cross) + 1 nails and strips + hammer |
| 1 valve, of a bronze | 1 straight pipe of any metal + 1 ingot of that bronze + hammer | |
| 1 pressure valve, of a bronze | 1 straight pipe of any metal + 1 ingot and 1 plate of that bronze + hammer | |

Solder bars are tin or silver; the soldering iron loses 2 durability a craft. Copper and lead pay
solder twice, soldering the angles into a hollow and joining the pipe sections: that is intended. The
sections and the nails and strips share the metal; every pipe is the sections' metal. So a pipe
section is a straight pipe's worth, and a bend, tee or cross costs the sections in it. The valves
take no gear (ppex's took two rusty gears, the steel gear with `GearConsumers`). exlib's recipe cost
levels (`/exmod steam cheap`) rewrite only ppex's own recipes (below), so these keep their quantities
at every level.

**Burst pressures.** ppex gives iron 5 atm and steel 10 and treats any other metal as iron; here each
metal has its own figure, the weakest pipe still caps its run, and a gas run held at that figure for
exlib's 30 s bursts a pipe (the pipe drops as itself, with ppex's plume and bang). Liquid runs never
burst. Valves never burst; a pressure valve's gate goes up to its metal's figure.

| Setting | Default | |
|---|---|---|
| `LeadBurstPressure` | 0.5 | below every gas producer (a boiler's exhaust runs at 0.8) |
| `CopperBurstPressure` | 3 | carries exhaust and blast air (2-2.5); a choked Cornish (5) or Lancashire (12) boiler bursts it |
| `IronBurstPressure` | 5 | ppex's |
| `SteelBurstPressure` | 10 | ppex's |
| `BronzeBurstPressure` | 5 | the bronze pressure valve's highest gate |

A value that is not a number, 0 or less, or above 1000 falls back to its default with a warning.

**Lead is for water only.** A run holding steam or exhaust bursts one of its lead pipes every second,
at any pressure, until it has none, with ppex's own effects; air (blast air) and water are safe in it.
Lead water has no effect on whoever drinks it. Since a burst pipe drops as itself, one put back on a
live steam run bursts again.

**Text.** ppex's steam power page names the four figures (from this side's settings) and says lead is
for water only; its fittings page says the valves are bronze, made from any pipe, and gives the
pressure valve's highest gate (English; ppex's Russian and Ukrainian pages still name only iron and
steel). The lead pipes' descriptions say "water only".

**By name, with Harmony** (its own id, `seraphhorizons.unifiedpipes`, patched once per process): a
postfix on ppex's `BlockPipe.BurstPressure` getter gives each metal its figure; a postfix on exlib's
`PipeNetwork.OnTick` bursts a lead pipe through exlib's own private `ExecuteBurst` while the run's
medium is `Steam` or `Exhaust`; a postfix on exlib's private `ExRecipeCosts.GridRecipesFor` keeps
ppex's recipe cost levels to recipes from ppex's own files. Before the patch loader runs, the server
checks ppex's six pipe and valve blocktypes and its pipe recipes against the patch
(`Pipes/Core/PipeAssetGuard.cs`: the `material` group third with states iron and steel, an `iron4`
texture by type, and the recipes at indices 0-3 and 7-10 making the pipes and valves the patch
means). If ppex has changed any of that, or the first two members are not found, one warning says
so and the switch stands down as if off. If only `GridRecipesFor` is missing, a warning says ppex's
cost levels may change this mod's pipe recipes, and the rest goes ahead.
`patches/gearconsumers-ppex.json` patches the same valve recipes (their gear); the two files'
operations do not depend on which is applied first, and `tools/tests/test_unified_pipes.py` holds this
patch to ppex's zip and to that file.

With the switch off, or without ppex, the three patch files are emptied in `Start`, the angle, the pipe
section and this mod's recipes (pipe, soldering the hollow, forging the angle) are marked disabled, and
nothing is patched: ppex's pipes, the game's chute section and its anvil, chute and plate recipes, and
Better Ruins' chutes are as they ship, and copper, lead and bronze pipes and valves already placed, and
angles, pipe sections and lead chute sections, are lost. `CastPipes` then stands
down too.

#### Hydrate or Diedrate's pipes go; its hand pump works on ppex's

Hydrate or Diedrate (`hydrateordiedrate` 2.5.6) has copper and lead pipes (`pipe-{metal}`), shut-off
valves (`shutoffvalve-{metal}`) and a pipe section to solder them from (`pipesection-{metal}`). They
carried nothing: they were only ever the hand pump's way to a wellspring. The pump searched through
them, on demand, for a spring beside a pipe (or right under the pump), primed for one stroke in 3
pipes (its `HandPumpPrimingBlocksPerStroke`), and pumped 2 liters a stroke straight from the spring.
The winch, the keg and the wellspring never touch a pipe. With the switch on (and ppex's side bound):

- **They are gone.** No recipe for the pipe, the valve or the pipe section; all three out of the
  creative inventory and the handbook (`patches/pipes-hydrateordiedrate.json`). Placed pipes and valves
  are removed as they load, nothing given back: a Harmony postfix on each one's block entity's
  `Initialize` (`BlockEntityPipe`, `BlockEntityShutoffValve`, declared on those types) queues the
  removal for the next server tick, as for the old trunk blocks, with one log line each. A pipe's
  disguise block goes with it. A leftover pipe placed by hand vanishes the same way. The block and item
  types stay registered, so the removal finds them.
- **The hand pump finds its spring through ppex pipes.** A postfix on `BlockEntityHandPump.FindWellViaNetwork`
  keeps Hydrate's own find (a spring right under the pump) and otherwise walks, breadth first, from the
  cell under the pump through ppex pipe cells that couple both ways (exlib's
  `NetworkMembership.CouplesAt`, the rule its network is built by, so the pipe under the pump must open
  upward; a closed valve, `IsConnectionBroken`, stops the walk) to the nearest wellspring beside any of
  them, on any face, as Hydrate's did. On the server the run under the pump must be empty or hold
  water (`PipeNetwork.State.MediumType`; steam, exhaust or air reach no well) and only its graph's
  nodes count. Priming: a prefix on `ComputePrimingStrokes` gives, for a spring found that way, the
  pipes walked over Hydrate's setting (7 pipes, 2 strokes at its default). Every metal and a mix of
  them serve. The pipes still carry none of the pump's water.
- **The pump's cache.** Hydrate keeps the spring until its `FluidNetworkState` version changes, which
  its own pipes bumped: a postfix on exlib's `PipeNetwork.OnTopologyChanged` calls its
  `InvalidateNetwork()`, so a pipe broken or placed or a valve turned makes every pump look again.
  The client has no ppex networks, so its pump looks again each time it is asked (its look-at text).
- **No leak at the ends.** A pipe's connector against a hand pump's underside, or against a wellspring,
  counts as sealed (a postfix on exlib's `BlockNetworkNode.IsValidNonNetworkConnection`), not as an open
  end leaking the run's water.
- **The recipe.** The hand pump takes two ppex straight pipes of its metal (copper or lead) where it took
  two of Hydrate's; its handbook page says how it finds a spring (`handpump-handbook-*`).
- **A fluid intake drains the well.** ppex's fluid intake accepts any water, Hydrate's well water
  included, and never took any from the world: an intake over a well was an endless source. A prefix
  and postfix on `BlockEntityFluidIntake.ProduceWater` find well water where the intake looks for its
  water (ppex's `FluidIntakeWaterDepth`, 3: three deep and one out each way), ask no more than the
  governing spring holds (`WellBlockUtils.FindGoverningSpring`), and take what went in out of the spring
  (`TryChangeVolume`), so the well's water falls as under the winch or the pump. Well water that no
  spring governs gives none. (The intake needs a full pool under it, wider than a one-block shaft.)

Hydrate or Diedrate's licence forbids redistribution, so nothing of it is referenced at build time:
every type and member is found by name (`Pipes/Game/HydratePipes.cs`, `Pipes/Game/HandPumpBridge.cs`),
with parameter types for overloaded names. With a member missing, the part that needs it is left out
with one warning. The pump's patches are both sides (own id `seraphhorizons.handpump`, once per
process); the removal, cache, seals and intake are server side (`seraphhorizons.hydratepipes`). With
the switch off (or ppex's side not bound, or without Hydrate or Diedrate) the patch file is emptied in
`Start` and nothing is patched: Hydrate's pipes are as it ships them, but those removed are gone.

### Cast pipes (`CastPipes`)

Steelmaking Expanded (`smex` 0.10.1) and Pipes and Power Expanded (`ppex` 0.7.1). The ferrous stage
of the pipe chain: iron and steel pipe sections are cast from the molten canal, the only way to them
(`UnifiedPipes` adds the pipe section and switches off ppex's plate-and-nails recipes; this stands
down without it, and runs after it, `ExecuteOrder` 0.11). Straight pipes per ingot: copper or lead
1 off the mandrel station and 2 off the draw bench, **cast iron or steel 2**.

- **The pipe mold.** smex's tool mold gets a fourth tool type beside plate, double ingot and quad
  rod: `smex:toolmold-{color}-raw-pipe` (blue, red or fire clay) and `smex:toolmold-{color}-fired-pipe`
  (the ten colours smex's fired molds come in). Clay-form the raw mold from 14 x 14 clay, two layers:
  a floor, and the walls round a trough closed at both ends with a core down its middle for the bore
  (`recipes/clayforming/pipemold.json`); fire it in a pit kiln or a beehive kiln as smex's molds fire.
- **Casting.** The fired mold takes `requiredUnits` 100, **one ingot, and casts two
  `seraphhorizons:pipesection-{metal}`**. smex's canal pedestal takes it as any small tool mold (sneak-click it
  on, sprint-click to pour), draws 100 units from the canal and holds it; take it off hardened, set it
  down and take the sections out with an empty hand, as with smex's own molds. A crucible pours into one
  on the ground too. The canal carries iron (blast furnace) and steel (Bessemer converter); the game's
  mold takes any metal whose drop exists, so it refuses a metal with no pipe section (tin bronze, gold)
  but a crucible could pour copper or lead into it, casting two pipe sections of those as well.
- **Then pipe.** Iron and steel pipe sections are banded on the grid with nails and strips of their
  metal and a hammer (`UnifiedPipes`' recipes, "Unified pipes"): **1 pipe section + 1 nails and strips
  + a hammer = 1 `ppex:pipe-straight-ns-{metal}`**, so 100 units make 2 pipes.

The molds are in smex's place (general and construction); the fired mold carries a "Casting pipe"
handbook section.

**The patch** (`patches/castpipes-smexmold.json`, on the server, `dependsOn` smex) adds to smex's
`blocktypes/molds/toolmoldraw.json` and `toolmoldfired.json`: `pipe` to `/variantgroups/2/states`,
`/shapebytype/*-pipe` (`seraphhorizons:block/clay/mold/pipe`, turned 90 degrees as smex's are), and on
the fired mold `/attributesByType/toolmold-*-fired-pipe`: `requiredUnits` 100, `fillHeight` 1,
`fillQuadsByLevel`, `moldrackable`, the drop `seraphhorizons:pipesection-{metal}` (quantity 2) and
the handbook section. The game merges a type's attributes by type into its attributes, so the pipe
mold keeps what smex's molds share (reinforcing, the tong transforms). The raw mold needs nothing
more: smex keys its pit kiln and beehive kiln firing by clay colour with `{tooltype}` in the fired
code. smex's mold gating (its command that disables molds) names only its own three tool types, so it never disables
the pipe mold. The names are lang entries in smex's domain (`smex:block-toolmold-*-raw-pipe`, ...).

**The guard.** In `Start`, on the server, before the patch loader runs, `Pipes/Core/CastPipeMold.cs`
checks smex's two files are what the patch assumes: the fired one a `BlockToolMold` coded `toolmold`;
in both, the variant groups color, materialtype and `tooltype` third, without `pipe`; a shape by type
and no plain shape; on the fired one no attributes by type key that would answer for the pipe mold
before the patch's (the game takes the first match); on the raw one, each clay's kiln firing by
`{tooltype}`. If anything differs, one warning (`Cast pipes: Steelmaking Expanded changed ..., so
there is no pipe mold`) and the feature stands down as if off.

**The mold's names.** They are in this mod's `lang/en.json` under smex's domain
(`smex:block-toolmold-*-raw-pipe`, `-fired-pipe` and their `blockdesc-` lines), which the game reads
from any mod's file. Expanded Lib's lang coverage check does not: it reads a block's own domain's lang
assets, and would log the patched state as untranslated on every start. So in `AssetsLoaded` the
server copies those keys into smex's lang assets in memory (`ForeignLangKeys`, `NameMoldsForSmex`),
adding only what smex lacks; a file that fails to parse is left alone with one warning.

With the switch off, without smex or ppex, with `UnifiedPipes` not on (one notification), or after
that warning, the patch file is emptied in `Start` and the mold's clay-forming recipe is marked disabled
before the game loads it: there is no pipe mold, and those already in a world are lost.

### Draw bench (`DrawBench`, `DrawBenchSettings`)

The drawn rung of the pipe chain: a mechanically powered chain draw bench, as lead and copper pipe was
drawn from the 1790s to the 1880s, that draws one lead or copper hollow section (the tube blank in game), the game's chute
section (`game:chutesection-lead`, `-copper`: the copper the game's own, the lead the state
`UnifiedPipes`' patch adds), over the mandrel into four pipe sections of its metal
(`seraphhorizons:pipesection-lead`, `-copper`). Two angles and solder make a hollow section on the
grid; a hollow gives two pipe sections on the mandrel station or four here; a pipe section and solder
make a pipe ("Unified pipes"). Its model, rig and generator are described in `DrawBench/README.md`, which also records the
gameplay's decisions; this is the gameplay, in `DrawBench/` (rules in `DrawBench/Core/`, the game side
in `DrawBench/Game/`).

**Blocks.** `seraphhorizons:drawbench-frame-{side}` is the controller: the die end, the block the
player clicks, with the bench running four blocks away from them along their line of sight, a ghost in
each of the rig's other three cells (`drawbench-ghost`) and the power ghost
(`drawbench-ghostpower-{side}`) at the far end, which takes the axle on the player's right (native
west). The frame is a grid recipe: two metal plates and four rods of iron, meteoric iron or steel, 12
nails and strips of the same, two steel gears (the rectifier's train, the return shaft's gear and the
barrel gear, which the frame carries), two oak logs, oak planks and a hammer (about 11 ingots of iron).

**Stages.** Fitted by right-click on the frame or any ghost with the item, in this order only; anything
of a later stage is refused with a message naming the next. The chain and the mandrel take two of their
item, both from the held stack in one click (fewer are refused, and stay in hand); the rest one each:

| # | Stage (`requires`) | Item | Made |
|---|---|---|---|
| 1 | `gearbox` | `game:jonasframes-gearbox01` (the clutch and change gear) | Looted, or BetterRuins' conversion |
| 2 | `chain` | Two of `game:metalchain-iron`, `-meteoriciron` or `-steel` | Smithing (the game's) |
| 3 | `dog` | `game:bracket-heavy-iron`, `-meteoriciron` or `-steel` | Smithing (the game's) |
| 4 | `mandrel` | Two of `game:rod-iron`, `-meteoriciron` or `-steel` | Smithing (the game's) |
| 5 | `die` | `seraphhorizons:drawdie-iron` or `-steel` (100 durability) | Smithing, 2 ingots of its metal (80 voxels) |

The die recipe is named `drawdie`, not `plate`, so the helve hammer does not work it. In creative mode
Ctrl + right-click on an incomplete bench fits its next stage with nothing taken (a steel die last).
**Ctrl + right-click** takes the die back, with what durability it has left, while no hollow is on the
bench. Every other stage comes back only by breaking the frame, which drops the frame, every fitted
item, the die with its durability, and a hollow no pipe section has been drawn from yet.

**Work.** A lead or copper hollow section goes on by right-click, or from a chest or hopper in front of
the die end (native north), one at a time while the shaft turns; an ingot, an angle or a pipe section
(what comes off) is never taken.
**The die decides the metal:** an iron die draws lead only, a steel die lead and copper (`DieMetals`);
copper on an iron die is refused with a message. The draw needs the shaft at `MinSpeed` and advances W,
the pipe sections drawn, with its angle: one section per `TurnsPerSectionLead` (7.72) turns for lead, the
fast gear, and `TurnsPerSectionCopper` (15.45) for copper. Each time W passes 1, 2, 3 and 4 a pipe section
of the metal goes into a container against the output face by the die end (native east, where the
trough under the bed brings them), else drops just outside it. At 4 the hollow is used up and the die
loses `DieWearPerHollow` (1), whatever the oil; a worn-out die is gone (the tool-break sound) and the
bench stops until a new one goes in. The next hollow comes in from the infeed a moment after.

**Oil.** A MachineOil machine (`MachineOilSettings.DrawBench`: tank 1000, 2 points a pipe section, 8 a
hollow), with the
standard dry load: three times its `ResistanceLead` (0.2) or `ResistanceCopper` (0.35) while dry, and
it smokes. The block info shows the next stage, the die's metal, what it draws and its hollows left,
the pipe sections drawn of the hollow on, the oil and the speed.

**Drawn.** The block draws `drawbench_frame.json`; the renderer splits `drawbench.json` into the rig's
parts and draws each whose stage is fitted, the die in its metal, and the hollow and pipe sections of the
job's metal while it is on (and while they ease out after). θ is the power ghost's angle about native x; W
runs on with the shaft between the server's syncs. While it draws: metal dust at the die's mouth, oil
drips from the oiler with oil in the tank, and the gears' sound.

| Setting | Default | |
|---|---|---|
| `DieDurability` | 100 | Hollow sections a new die draws (the die item's durability) |
| `DieWearPerHollow` | 1 | Die durability a hollow section costs |
| `TurnsPerSectionLead` | 7.72 | Axle turns per lead pipe section; the rig's `draw.turnsPerSection.thin` (a test holds them together) |
| `TurnsPerSectionCopper` | 15.45 | Axle turns per copper pipe section; the rig's `draw.turnsPerSection.thick` |
| `ResistanceLead` | 0.2 | The complete bench's load, empty or drawing lead |
| `ResistanceCopper` | 0.35 | Its load drawing copper |
| `MinSpeed` | 0.05 | Below it the bench neither draws nor takes a hollow from its infeed |
| `DieMetals` | iron: lead; steel: lead, copper | What each die draws (lead and copper only) |

With the switch off the server marks the three block types, the die's item type and both recipe files
disabled before the game loads them, so none of it exists and benches already placed are lost; the
machine oil page and the mod's own text no longer name it. The hollow and pipe sections exist either
way. With `UnifiedPipes` off there is no lead chute section, so the bench draws copper only. Not yet: a schematic for the frame (`MachineSchematics`).

### Press brake (`PressBrake`, `PressBrakeSettings`)

The folded rung of the pipe ladder: a hand-worked leaf brake (a cornice brake) of the early-to-mid
1800s, oak with iron wearing edges and iron clamp screws, that bends one lead or copper plate
(`game:metalplate-lead`, `-copper`) once, at a right angle, into one angle (`seraphhorizons:angle-lead`,
`-copper`, `UnifiedPipes`' item), as the anvil forges one from an ingot. Two angles, two solder bars and
a soldering iron make a hollow section (the game's chute section, the tube blank in game) on the grid; hollow sections make
chutes, or pipe sections on the mandrel station or the draw bench. Its model, rig and generator are described in `PressBrake/README.md`,
which also records the gameplay's decisions; this is the gameplay, in `PressBrake/` (rules in
`PressBrake/Core/`, the game side in `PressBrake/Game/`). A hand machine: no mechanical power, no oil,
and nothing on it wears.

**Blocks.** `seraphhorizons:pressbrake-frame-{side}` is the controller: the leaf end, the block the
player clicks, with the brake running two blocks away from them along their line of sight and a ghost
in the far cell (`pressbrake-ghost`). The frame is a grid recipe: three oak planks (the leaf, the bar,
the hand bar), two oak logs (the cheeks and bed), four nails and strips of iron, meteoric iron or steel
(the gallows and their nuts, the hinge pins, the bail arms) and a hammer.

**Stages.** Fitted by right-click on the frame or the ghost with the item, one item each, in this
order only; the edges before the screws are refused with a message naming the next:

| # | Stage (`requires`) | Item | Made |
|---|---|---|---|
| 1 | `screws` | `game:rod-iron`, `-meteoriciron` or `-steel` (turned into the two clamp screws) | Smithing (the game's) |
| 2 | `edge` | `game:metalplate-iron` or `-steel` (cut into the bed's, leaf's and bar's edges) | Smithing (the game's) |

In creative mode Ctrl + right-click on an incomplete brake fits its next stage with nothing taken.
**Ctrl + right-click** takes a plate back off while it is still flat; with no plate on, it takes the
last part fitted back out (the edges, then the screws). Breaking the frame drops the frame, both parts
and a plate not yet folded (one being folded is lost).

**Work.** A lead or copper plate goes on the bed by right-click (an angle, what comes off, never does,
nor a hollow section, a pipe section or an ingot). Then the player **holds right-click** on the brake, as on the quern: the fold runs only while
someone holds. The lever clock turns a turn a second while held, and the fold cycle W (0..1) advances
by its turns over the plate's `LeverTurnsPerPlateLead` (3) or `LeverTurnsPerPlateCopper` (4.5): about
three seconds of work for lead, four and a half for copper. The bend is heard at the leaf's one fold; at
W = 1 the plate is used up and one angle of its metal goes into a container against the output face
(native north, in front of the leaf end), else drop just outside it. Worked with nothing on the bed,
the brake takes the next plate from a chest or hopper beyond its far end (native south), so a player
can hold from one plate to the next; it never takes a plate by itself. The block info shows the next
stage and the plate on, how far folded.

**Drawn.** The block draws `pressbrake_frame.json`; the renderer splits `pressbrake.json` into the
rig's parts and draws each whose stage is fitted (the screws and edges in their metal) and the sheet of
the plate's metal while it is on. W runs on with the lever between the server's syncs. While the leaf
swings: metal dust at the folding edge; while worked: the frame creaks.

| Setting | Default | |
|---|---|---|
| `LeverTurnsPerPlateLead` | 3 | Lever turns (seconds held) a lead plate takes; the rig's `fold.leverTurnsPerPlate.thin` (a test holds them together) |
| `LeverTurnsPerPlateCopper` | 4.5 | Lever turns a copper plate takes; the rig's `fold.leverTurnsPerPlate.thick` |

With the switch off the server marks both block types and the recipe file disabled before the game
loads them, so none of it exists and brakes already placed are lost; the mod's own text no longer
links it. The plates and the angles exist either way (the angles are `UnifiedPipes`'; with that switch
off there are none, and the brake refuses plates with a message). Not yet: a schematic for
the frame (`MachineSchematics`).

### Mandrel forging station (`MandrelStation`, `MandrelStationSettings`)

The forged rung of the pipe ladder: a smith's mandrel station of the 1700s, a squared oak stump with an
iron hoop carrying an iron bracket that holds a square iron mandrel cantilevered over its far end, on
which one lead or copper hollow section (the game's chute section, the tube blank in game, `game:chutesection-lead`, `-copper`)
is hammered down, blow by blow, into two pipe sections (`seraphhorizons:pipesection-lead`, `-copper`,
`UnifiedPipes`' item). Two angles and solder make a hollow; the draw bench makes four pipe sections of
one; a pipe section and solder make a pipe. Its model, rig and generator are described in
`MandrelStation/README.md`, which also records the gameplay's decisions; this is the gameplay, in
`MandrelStation/` (rules in `MandrelStation/Core/`, the game side in `MandrelStation/Game/`). A hand
station: no mechanical power, no oil, and nothing of it wears.

**Blocks.** `seraphhorizons:mandrelstation-frame-{side}` is the controller: the stump, the block the
player clicks, with the station running two blocks away from them along their line of sight and a ghost
in the far cell, under the mandrel's tip (`mandrelstation-ghost`). The frame is a grid recipe: an oak log
(the stump), an iron or steel plate (the bracket and the swage), two nails and strips of iron, meteoric
iron or steel (the hoop and the straps) and a hammer.

**The mandrel.** Fitted by right-click on the stump or the ghost: `game:rod-iron`, `-meteoriciron` or
`-steel` (smithing, the game's). In creative mode Ctrl + right-click on a station with no mandrel fits an
iron one with nothing taken. **Ctrl + right-click** takes a hollow back off before its first blow; with
nothing on, it takes the mandrel back out. Breaking the frame drops the frame, the mandrel and a hollow
not yet struck (one being forged is lost).

**Work.** A lead or copper hollow section goes on the mandrel by right-click (nothing else does: not an
angle, a pipe section, an ingot or a plate). Then **each right-click with a hammer** (any of the game's)
is a blow, as on the anvil, at most one each 0.3 s: the anvil's sound and sparks where it lands, the
hammer's durability paid (`HammerWearPerBlow`), and the forging W (0..1) advanced by the hammer's tool
tier over `BaseHammerTier` (2, the copper hammer's), over the hollow's `BlowsPerHollowLead` (6) or
`BlowsPerHollowCopper` (9), at most half a hollow a blow; a hammer with no tier counts as the base. With
the game's tiers (copper, gold, silver 2; bronzes 3; iron, meteoric iron 4; steel 5) lead takes 6, 4, 3
and 3 blows and copper 9, 6, 5 and 4. When W reaches 1 the hollow is used up
and two pipe sections of its metal go into a container beyond the tip (native south), else drop just
past it. A blow on a bare mandrel takes the next hollow from a chest or hopper beside the station (native
west), so a player can keep striking; it never takes one by itself. The block info shows what the
station needs and the hollow on, its blows struck and how far it is forged.

**Drawn.** The block draws `mandrelstation_frame.json`; the renderer splits `mandrelstation.json` into
the rig's parts and draws the mandrel once fitted, in its rod's metal, and the hollow of the metal on
while it is on, easing to each blow as it lands.

| Setting | Default | |
|---|---|---|
| `BlowsPerHollowLead` | 6 | Blows of the base hammer a lead hollow takes; the rig's `forge.blowsPerHollow.thin` (a test holds them together) |
| `BlowsPerHollowCopper` | 9 | Blows of the base hammer a copper hollow takes; the rig's `forge.blowsPerHollow.thick` |
| `BaseHammerTier` | 2 | The tool tier those blows are counted for, the game's copper hammer's; a blow of a hammer of tier t forges t / this of a base blow |
| `HammerWearPerBlow` | 1 | Durability a blow costs the hammer, as a blow on the anvil does |

With the switch off the server marks both block types and the recipe file disabled before the game
loads them, so none of it exists and stations already placed are lost; the mod's own text no longer
links it. The hollows and the pipe sections exist either way (the sections are `UnifiedPipes`'; with
that switch off there are none, and the station refuses hollows with a message). Not yet: a schematic
for the frame (`MachineSchematics`).

### Handcar (`Handcar`, `HandcarSettings`)

A standard-gauge rail car for Yang's Transport Tycoon, pumped by hand: a walking beam on an A-frame in
the middle of the deck drives the front axle through a pitman, a crank and a 3 to 1 gear. Two riders
stand at the beam's ends, facing each other, hands on its handles, and a chest or crate rides on the
deck behind them. Its model, rig, generator and what it is built on in Yang's code are described in
`Handcar/README.md`; this is the gameplay, in `Handcar/` (rules in `Handcar/Core/`, the game side in
`Handcar/Game/`). It is Yang's own standard-gauge vehicle (`yangtransport.sglocomotive`: its renderer,
seats, couplings, cargo slot, pickup and deconstruction) with this mod's behaviour in place of a steam
engine, and three Harmony patches on Yang's sealed classes found by name.

**Riding and pumping.** Placed by its item on standard-gauge track. Right-click on the deck at either end
steps on, sneak steps off. Forward pumps the car the way the rider faces, back the other way; let go and
it coasts down by its drag. Pumping against the way it rolls brakes it to a stop, then it goes back. One
rider pumping makes the solo top speed, two pumping the same way the pair's; a second rider only rides
until they pump too, and two pumping against each other lock the beam and hold the car. Load (a chest
or crate, a coupled wagon) costs top speed. Pumping costs a survival rider what sprinting does. Left or
right moves the branch selector beside the stand one place that way, as the rider faces: the car takes
that branch at the next switch.

**The riders' animation.** The car's wheels, gears, crank, pitman and beam and both riders' arms move by
the distance the car has rolled, every frame on the client, so the hands stay on the handles at any
speed and when it stands; the pump's lean fades in while a rider pumps and out when they stop. The
animations are added to the game's seraph by `patches/handcar-riders.json`, solved for its arms by the
generator.

**Taking it up.** Yang's own: with nobody on and the car stopped, a sneak click with an empty hand picks
it up, or Yang's wrench held six seconds with a hammer in the other hand takes it apart; either gives
the handcar back. The recipe (copper age): four of Yang's minecarts, two planks, a wooden spur gear, a
wooden axle and two copper rods.

| Setting | Default | |
|---|---|---|
| `TopSpeedOne` | 4.2 | Blocks a second, one rider pumping: 60 % of Yang's primitive engine cart (7) |
| `TopSpeedTwo` | 5.95 | Two pumping: 85 % |
| `AccelerationOne` | 0.6 | Blocks a second squared, one rider (the engine cart pulls 0.8) |
| `AccelerationTwo` | 0.9 | Two |
| `SpeedLossPerWeight` | 0.5 | Top speed lost per weight over the bare car's 2 (a chest or crate is 1) |
| `MinTopSpeed` | 0.8 | The least top speed however loaded |
| `CoastDrag` | 0.35 | Slowing while coasting (Yang's `Roll0`, written into the entity type at load) |
| `DragPerWeight` | 0.05 | More of it per weight over 1 (`RollW`) |
| `BrakeDeceleration` | 2.5 | Braking when pumped against the way it rolls (`BrakeDecel`) |
| `SatietyPerPumpSecond` | 1.5 | A pumping rider's satiety a second, as sprinting's |
| `PumpFadeSeconds` | 0.35 | How fast the pump's lean fades in and out (each client's own) |

With the switch off (or without Yang's Transport Tycoon, or with it changed so the patches cannot bind,
which is logged) the server marks the entity type, the item type and the recipe disabled before the game
loads them and empties the riders' patch, so none of it exists, in the handbook or the recipe export;
handcars already in a world are lost. A client follows the server. Not yet: an icon for the recipe
browser.

### Felling a tree costs the axe a flat figure (`FlatFellingWear`, `FlatFellingWearSettings`)

Logging Expanded (`loggingmod`, 0.3.6). Felling a tree that leaves a trunk costs the axe
`ThinTree` (4) durability, or `ThickTree` (8) for a tree with a two-by-two trunk (the game's log
sections: the redwood), whatever the tree's height, in place of the game's one durability per log
block. A trunk's logs then cost their own at the stations and the machines: Logging Expanded's
sawhorses take one per log (the advanced one per two), its axe on a trunk lying there one per
swing (two logs off while it holds two or more), the splitting block one per log, the bucking
mill's blade one per log stored, about what the game's felling alone charged, so the whole chain
costs about what it did before trunks, and most of it is paid working the trunk. A felling that leaves no trunk (fewer logs than Logging Expanded's
`MinLogsForTrunk`, a wood it does not know, so loose logs drop) costs the game's one per log.

The game's axe (`ItemAxe.OnBlockBrokenWith`) breaks every block of the tree and damages itself once
per wood block; leaves cost nothing. Logging Expanded fells in the server's `BreakBlock` event,
which runs first: it marks the tree's blocks to drop nothing, throws the trunk (a trunk entity,
with `TrunkEntities`) and raises its public `FellingListener.OnTreeFelled` when it made one.
`FellingWear.cs` (server side, by name): hears that callback and notes the player and the stump; a
prefix on `ItemAxe.OnBlockBrokenWith` for that player, when the tree the axe is about to break
holds that stump, takes the note, decides thick or thin from the tree's blocks
(`Core/FellingWearRules.cs`: any `logsection-` block) and opens a window in which a prefix on
`CollectibleObject.DamageItem` skips every hit on the axe's slot; a finalizer closes it and charges
the flat cost with the game's own `DamageItem`, so an axe at or below it fells the whole tree and
shatters after, as it would on its last block, and an axe that `damagedby` says nothing breaks
loses nothing. The client predicts the per-log loss on its own copy of the axe; the server's figure
replaces it as the slot syncs. `FlatFellingWearSettings` holds the two figures (0 to 10000; a value
out of range falls back to its default with a warning). With the switch off, without Logging
Expanded, or with its callback not as expected (one warning), nothing is patched and every felling
costs one per log.

### A locomotive keeps its riders (`LocomotiveRidersStayOn`, `LocomotiveRidersBreathe`)

Yang's Transport Tycoon (`yangtransport`, 1.0.3), its standard-gauge locomotives
(`yangtransport:sglocomotive-{tier}`, every tier). Two switches, both in `LocomotiveSeats.cs`.

`LocomotiveRidersStayOn`: blocks beside the track no longer throw a rider off. The standard and
advanced tiers set `CollisionCheckSeats` in their type's attributes, and while a player rides one
Yang's `RailVehicleSeat` tests the rider's collision box at the seat every 20 ms on the client; any
colliding block (a tree's leaves collide) sends the locomotive a packet, and the server unmounts the
rider. Both ends first ask Yang's `internal static RailVehicleSeat.CollisionChecksEnabled(Entity)`:
the seat before it starts the client's check, the locomotive before it acts on the packet. A
postfix answers false for a locomotive, so neither runs; nothing else reads it, and a rider who steps
off is still put beside the track by Yang's own placement. Patched on both sides, once per process
with its own Harmony id; either side's switch alone keeps riders on. Other vehicles that set the
attribute keep the check: this mod's handcar does. Without Yang's mod, or with the method gone or
changed (one warning), nothing is patched.

`LocomotiveRidersBreathe`: with the check off, a rider's head passes through the leaves and walls
beside the track, and the game's `EntityBehaviorBreathe.Check` (server side, once a second) takes
the air of anyone whose eye is inside a block's collision box. A postfix gives the air back to an
entity seated in a locomotive (its seat's entity's code), unless the eye is under a liquid's
surface, by the game's own test: a locomotive driven into deep water still drowns its rider.
Oxygen then refills as it does in the open. Server side; without Yang's mod nothing is patched.
In game 1.22.7 the game's block test never takes the air away anywhere but near the world's origin
(it intersects the block's box, in world coordinates, with the entity's selection box, in the
entity's own), so for now this is a guard for when the game fixes it: the postfix only acts when
`Check` has taken the air.

### Sawmill blade kits last three times as long (`DurableSawmillBlades`)

Immersive Woodworking (`immersivewoodworking`, 1.3.11). Its sawmill blade kits
(`immersivewoodworking:sawmillblade-{metal}`) have three times its durability: gold 210, silver
270, copper 750, tin bronze 1200, bismuth bronze 1350, black bronze 1500, iron 2700, meteoric iron
3600, steel 6750. The bucking sawmill wears its kit by one for every log in a trunk it cuts, so a
copper kit lasts 750 logs. The durability is the item's, so Immersive Woodworking's own plank
sawmill's kits last three times as long too, as does the assembled creative sawmill's steel kit
(`AssembledMachinesInCreative`, which places a new kit of the item).

A JSON patch, `patches/sawmillblade-durability.json`, replaces each value of the blade's
`durabilitybytype` (server side; clients get the items from the server). With the switch off, or
without Immersive Woodworking, `SawmillBladeDurability.DisablePatches` empties it in `Start`, before
the game's patch loader runs, as for the tun patches.

### The blueprint makes 4 support chains, not 64 (`FewerSupportChains`)

Better Ruins (`betterruins`, 0.6.4). Its Machinist's Mechanism Blueprint
(`recipes/grid/schematic-mechanical/mechanical.json`, kept on crafting) makes support chains from a
solder bar and one or two nails and strips, with a soldering iron: `game:supportchain-twonew` (`/26`)
and `-fournew` (`/27`) from iron, `game:supportchain-two` (`/28`) and `-four` (`/29`) from black
bronze. Each gave 64, a stack for next to nothing; they give 4.

A JSON patch, `patches/supportchains-betterruins.json`, `"side": "server"`, replaces the four
recipes' output quantity by index. With the switch off, or without Better Ruins,
`SupportChains.DisablePatches` empties it in `Start`, before the game's patch loader runs, as for
the tun patches. The switch that counts is the server's.

### The woodworking machines are iron work (`IronWoodworkingMachines`)

Immersive Woodworking (`immersivewoodworking`, 1.3.11). Its sawmill and chopper take any metal and
little of it: 8 nails and strips and a plate for a sawmill, 2 nails and strips and 4 plates for a
chopper. With this, the two frames and the six fitted parts take iron, meteoric iron or steel
(nails and strips, plates and rods alike) and many more nails and strips:

| Part | Immersive Woodworking | With the switch on |
|---|---|---|
| Sawmill frame | no metal | 8 nails and strips (in place of one of 3 planks) |
| Saw sash | 4 nails and strips | 16 nails and strips, 1 rod |
| Sawmill crankshaft | 2 nails and strips | 8 nails and strips, 1 rod |
| Sawmill feed levers | 2 nails and strips, 1 plate | 8 nails and strips, 1 plate |
| Sawmill carriage | no metal (a rusty gear) | 8 nails and strips (in place of 2 of 6 planks) |
| Chopper frame | 2 plates | 8 nails and strips (in place of one of 5 beams), 2 plates |
| Chopper drive | 1 nails and strips, 1 plate | 8 nails and strips, 1 plate, 1 rod |
| Chopper arm | 1 nails and strips, 1 plate | 8 nails and strips, 1 plate |

A sawmill is 48 nails and strips, 1 plate and 2 rods in all (about 16 ingots, at 4 nails and strips
to an ingot); a chopper 24 nails and strips, 4 plates and 1 rod (about 15). Wood, resin, rope, the
carriage's gear (a steel gear with `GearConsumers`, above) and the tools stay as they are. The blade kit and the chopper head keep their recipes and
take any metal: their metal already sets the machine's durability and speed. The bucking sawmill is
built from two sawmill frames and 8 nails and strips of its own, and takes two sashes, a crankshaft
and feed levers, so it comes to 72 nails and strips, 1 plate and 3 rods. The rosser is built from
four sawmill frames and 32 nails and strips, and takes a crankshaft and feed levers, 80 nails and
strips, 1 plate and 1 rod; its fitted hoops, rods and plates come on top. The two frames' own nails
and strips, and the rosser's fitted hoops, rods and plates, follow the same metal rule while this
switch is on (the patch file's last entries put `allowedVariants` on the two frame recipes), and
take any metal with it off. So the sawmill is no longer a copper
age machine; the chopper already needed iron for its bed, the advanced splitting block
(`UnifiedWoodworking`).

A JSON patch, `patches/woodworking-machine-costs.json`, rewrites the eight recipe files. Immersive
Woodworking ships them disabled and registers each recipe itself from the file, after the patch
loader, so the registered recipe has the pattern, the quantities and the `allowedVariants` of the
patched file (the allowedVariants Fix is not involved: the recipe keeps its wildcards). With the
switch off, or without Immersive Woodworking, `WoodworkingMachineCosts.DisablePatches` empties the
patch in `Start`, as for the blade kits. With the switch on, the handbook's Machines chapter
(`UnifiedWoodworking`'s guide) gains a paragraph with the two totals, by a `LangEdit`.

### The heating rack stays where it is put (`HeatingRackKeepsPosition`)

Logging Expanded (`loggingmod` 0.3.6). Its Trunk Heating Rack (`loggingmod:resinrack-*`,
`BlockResinRack`, block entity `BEResinRack`) keeps its trunk and resin when picked up: its
`OnPickBlock` writes the block entity's whole tree into the stack, and its `DoPlaceBlock` loads that
tree back into the new block entity. The tree includes the base `BlockEntity`'s `posx`, `posy` and
`posz`, so a rack placed from that stack has a block entity whose `Pos` is where it was picked up
(#374): its firepit check and dirty-marking go to the old spot. Carry On builds its carried stack
from `OnPickBlock`; its server puts the block entity right after the place-down, its client does
not, and a creative pick and place has nothing to put it right.

`HeatingRackPosition` (Harmony, on both sides, once per process with its own id) postfixes
`OnPickBlock` to take the three keys out of the stack it returns, and prefixes `DoPlaceBlock` to take
them out of the stack being placed: racks already picked up before the tweak, in inventories or
creative hotbars, still carry them. With no position in the tree, `BEResinRack.FromTreeAttributes`
keeps the one the game gave it. If the rack or either method is not as expected, the mod logs a
warning and leaves the rack as it ships.

### The heating rack stands on the block it is placed on (`HeatingRackStandsOnBlock`)

Logging Expanded (`loggingmod` 0.3.6). The Trunk Heating Rack's legs reach a full block below its
own cell (`shapes/resinrack.json` runs from y -16 to +14), with the bowl on the cell's floor and the
firepit meant to go in the cell under it: binding a heating rack frame builds the rack one block above
the ground, and `BEResinRack` reads its firepit from `Pos.DownCopy()`. `BlockResinRack` has no
placement of its own, so a rack placed from a stack (the hotbar, a creative pick, Carry On's
place-down, which runs the block's `TryPlaceBlock` too) goes in the cell over the face aimed at, like
any block, its legs drawn inside the floor.

`HeatingRackPlacement` (Harmony, on both sides, once per process with its own id) prefixes the rack's
`DoPlaceBlock`, the one method on the rack's own class that every placement from a stack reaches (its
`HorizontalOrientable` behavior runs `CanPlaceBlock` and then `DoPlaceBlock`). It moves the selection
one cell up when all of these hold:

- the placement was offset off an up face (`DidOffset` set, face up): aimed at the top face of a
  block, not at a side, and not at a replaceable block such as grass, where the rack goes into the
  aimed-at cell as it ships;
- the block aimed at (the cell below) is not a firepit (`BlockFirepit` or `BlockEntityFirepit`, any
  stage): over a firepit the rack already goes in the cell right above it, where its firepit check
  looks;
- the cell above can take the rack: in the world, replaceable, no entity in it, and the player may
  build there (the claim is tested, not tried, so no message is sent). If not, the rack goes where it
  would have gone without the tweak.

The selection's `Position` is moved in place, never replaced, so everyone holding that selection sees
the lifted cell: the game's client sends it to the server with the placement, and Carry On 2.0's
`TryPlaceDownAt` (whose `placedAt` is the same `BlockPos` it passes on) reports it, restores the block
entity's tree there (`RestoreBlockEntityData`) and plays the sound there. The face stays up and
`DidOffset` stays set, since the rack was still placed off that up face. A server receiving a lifted
position finds air under it, not a block, so it does not lift again. If the placement fails after the
lift, a postfix moves the position back down, so the game's client, which undoes its own offset by
the face, ends where it started. A rack Carry On drops (a dropped carry, not a place-down) goes
through `ExchangeBlock`, not `DoPlaceBlock`, and is not lifted; nor is the rack the frame binding builds,
whose selection is not offset. Each side reads its own switch, so the two should agree (both are on
by default): with it on on the client only, the server places at the client's lifted cell; on the
server only, the server lifts a rack the client predicted one cell lower. If the rack or the method
is not as expected, the mod logs a warning and leaves the rack as it ships.

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

### The mod checks the installed mods against the pack (`PackVersionCheck`)

The mod is released with the pack, at the pack's version (Releasing, below), and carries the
pack's `pack/lock.json` inside its DLL (an embedded resource, `seraphhorizons.pack-lock.json`, from
the csproj). Each side compares what it actually loaded with that lock and reports every
difference:

- a locked mod loaded at another version than locked, higher or lower (`exlib: expected 1.2.0,
  found 1.3.0`);
- a locked mod not loaded (`missing`), counted only on a side it loads on: a dedicated server never
  loads a `client` mod, nor a client a `server` one;
- a loaded mod the lock does not have (`not in the pack`). The game's own mods (`game`, `creative`,
  `survival`) and this mod are not counted; there is
  no allow-list;
- another game version than the pack's `game_version`;
- this mod at another version than the pack it was built with.

The server logs one `Warning` per finding (`[seraphhorizons] Pack version check: ...`) and sends the
list in chat to each player with the `controlserver` privilege as they join. A client logs one
warning line and, a second after it is in the world, opens a dialog: what the install should be,
the findings in a scrolled list, and two buttons. **Close** leaves it to come back on the next
join; **Don't show again until this changes** stores a hash of the findings in the client's own
`ModConfig/seraphhorizons-packcheck.json` (`DismissedFingerprint`), and the dialog stays away until
the findings are another set. The list only goes quiet once the install matches the release this
copy of the mod shipped with: install that release again (Cairn's `.cairn` file does this), or
update to a newer one. Each side's own `PackVersionCheck` decides for it; off means nothing is
checked or shown.

Expect findings wherever the install is not a release as shipped: a Cairn local mods folder (its
mods are extras, or another version of a locked one), and CI's smoke run, which also loads the
recipe exporter (`seraphexport: not in the pack`, a warning, which smoke does not fail on), and
Atlas, whose own mod `atlasbridge` is the one finding there (`PackCheckScenarios.cs` requires exactly
that, and `SwitchesOffScenarios` none with the switch off). The
`next` build carries main's lock, so it matches the `next` pack.

The comparison is `PackCheck/Core/PackComparison.cs` (unit-tested in `tests/PackCheck/`, including
against the real lock), the game side `PackCheck/Game/` (`PackCheckSystem`, `PackCheckDialog`).

To check by hand in the game: put a mod the pack does not have in the mods folder and join a world
(dialog with that line; Close, rejoin: back; Don't show again, rejoin: gone; remove the mod, add
another, rejoin: back); on a dedicated server, join as an admin (chat lines) and as a player
(none).

### Settings follow the pack's defaults (`FollowPackDefaults`)

A mod keeps every value already in its config file and only adds keys the file lacks, so a default
a mod (this one included) changes never reaches an existing install: `CarrySpeedAtMaxLogs` stayed at
0.02 on installs made then, through three changes of its default. This fixes that for every mod in
the pack, any file a fresh server of the pack writes into `ModConfig`, JSON or ConfigKit's YAML.
The design, the decisions and the how-tos are in
[docs/config-defaults.md](../../docs/config-defaults.md); in short:

- Each pack version's defaults (a **snapshot**: every `ModConfig` file a fresh server of that
  version writes, byte for byte) ship in this mod, under
  `assets/seraphhorizons/config/configdefaults/<pack version>/`, generated by `packtool smoke
  --config-defaults write`; CI's smoke job fails when the current version's is out of date.
- Each install records the pack version its files were last brought to, in
  `ModConfig/seraphhorizons-configdefaults.json`. At start, before any mod reads its config
  (`ConfigDefaults/Game/ConfigDefaultsSystem.cs`, the first `StartPre` of all), a setting that still
  has that version's default, where the running version's differs, gets the new default. Any other
  value was chosen, and is kept. The first start with this records the version and changes nothing.
- A file is only changed when the mods that write it are loaded at the snapshot's versions; one
  that is not is left, and caught up on a later start once they are. A build with no snapshot
  for its pack version changes nothing.
- The values the pack sets instead of a mod's own default (`pack/config/ModConfig/`: BetterRuins'
  ruin spacing and structure chance) are part of each version's defaults: a fresh server gets the
  file written with them before the mod reads it, and changing one moves untouched installs. This
  replaces the Cairn pack file's `modConfig`, which only Cairn installs got.
- Each moved setting is a log line (file, setting, old and new value, why), and admins who join a
  server afterwards get the list in chat once.

Off means nothing in `ModConfig` is changed or written; each side's own setting decides for it.
`seraphhorizons.json` goes through it like any other file, before this mod loads it; the switch is
read from the raw file first.

The readers, the reconciler and the run are `ConfigDefaults/Core/` (unit-tested in
`tests/ConfigDefaults/`, with the shipped snapshots); `ConfigDefaultsScenarios` boots an install
recorded at a made-up earlier version (a stale carry speed and BetterRuins spacing that follow, a
blade wear and a megastructure spacing set by hand that stay), and `SharedWorldScenarios`' fresh
install gets `betterruins.yaml` with the pack's values.

## Ore

The ore overhaul (epic #435), in `Ore/`: where deposits generate, the gravel fields that give the
first metal, and the registry the traders and their maps read. Design, engine facts and the survey
numbers are in `docs/oregen.md`. The admin commands under `/sh ore` are described with `/sh trade`'s
in Trading, "Admin tools".

### Ore cells (`OreCells`, `OreCellSizeMetres`, `OreCellSizeByMetal`, `NoSurfaceCopper`, `SmallerDeposits`, `RarerDistricts`)

Ore is rare and placed by the seed (epic #435): a deposit of a given metal is a few kilometres
away, and one deposit carries a group for a long while, then runs out. Four switches, all on by
default, all server side, all **for new worlds only**: they change how chunks generate, so the
world records at its first start which of them it was created with
(`seraphhorizons:oreworld` in the savegame, `Ore/Core/OreWorldRecord.cs`). A world created with
a switch off, or before this existed, never gets it, whatever the config says later; switching
one off in the config does take effect (for chunks generated from then on). The cell sizes are
fixed at creation. The server log says what the world has
(`Ore worldgen for this world (...): ore cells on, 5000 m, ...`).

- **`OreCells`** (#438): at most one deposit of each metal per cell, a square of
  `OreCellSizeMetres` (5,000) blocks, or `OreCellSizeByMetal`'s size for that metal (by metal
  group: `{"gold": 8000}`; at least 500). Metals are groups of ores (`Ore/Core/OreMetals.cs`):
  copper is one deposit per cell whether the rock makes it malachite or native copper. Coal and
  each industrial mineral (borax, rhodochrosite, ...) count as their own metal; gems, quartz and
  olivine are not managed and keep Interesting Ore Gen's own spacing.

  For every (world seed, metal, cell), a stable hash picks eight spots inside the cell, at least a
  tenth of the cell from its edge (500 m for 5 km cells): the first is the deposit's spot, the
  rest are fallbacks (`Ore/Core/OreCells.cs`). Interesting Ore Gen (2.3.8) still makes its vein
  tries as before (the game's `GenDeposits` asks each chunk for its tries from its own seeded
  random sequence), but its spacing filter, `TiltedDiscDepositGenerator.TryApproveOreSpawnSeed`,
  is replaced (a Harmony prefix, id `seraphhorizons.ore`) by the cell rule: a try is approved only
  if it comes from the chunk holding the cell's active spot (its anchor) and is the first try of
  that metal from that chunk whose vein can start there. For disc and tube veins that means its
  centre is in a rock its ore takes: IOG draws the centre's height right after the filter, so the
  rule replays those draws on a copy of the try's random and looks at the block; a chimney grows
  through whatever host rock it meets, so its first try counts. Every chunk around the anchor sees
  the same tries in the same order, so they all approve the same one and the vein comes out
  whole, whatever order chunks generate in (a chunk generated before the anchor's own, from which
  the anchor's rock can't be read yet, approves none and misses its part of a disc or tube vein,
  as IOG's own veins do there). Which ore and vein shape it is follows from IOG's tries and the
  rock. A metal with fewer than 2 tries per chunk (borax has 0.01) gets its tries raised to 2,
  each ore in proportion, so an anchor chunk usually has some; every try away from an anchor is
  turned down before anything is drawn.

  When the anchor's own chunk column has been generated, its blocks are checked for the metal's
  ore. With none (no try of that metal from the chunk, or rock that can't host it), the spot
  failed and the next fallback becomes active; a fallback whose chunk was already generated
  before its turn is skipped; with none left the cell has no deposit of that metal. The primary
  spot depends only on the seed; which fallback is used depends on what was explored first, so
  each cell's state is kept in the savegame (`seraphhorizons:orecells`, `Ore/Core/OreCellBook.cs`)
  and logged as it changes (`Ore cells: copper cell 102, 98 spot 0 at ...: deposit placed`). Only
  metals that IOG has vein variants with tries for are managed (the log line
  `Ore cells: bound ...` lists them): gold, silver, nickel, titanium and chromium come from
  hydrothermal districts, which keep their own placement (`SmallerDeposits` sizes and counts their veins).
- **`NoSurfaceCopper`** (#440): the game's surface copper and surface cassiterite pockets, which
  IOG leaves on (it switches off only the deep vanilla deposits), are not tried
  (`patches/ore-nosurfacecopper.json`). IOG's surface signs of its own deep veins (boulders and
  prospecting veins) are part of those veins and stay. Bog iron is still the game's.
- **`SmallerDeposits`** (#439): IOG's veins are shrunk per metal to the epic's typical deposit
  (copper and iron 400 ingots; tin, zinc, bismuth, lead and nickel 150; silver and gold 80;
  titanium, chromium and platinum 150), by `config/ore-sizes.json`'s target over the median
  measured before. Disc veins (`fault`, `seam`) lose radius (√factor: volume goes with
  radius² × thickness); tube veins (`chimney`, `hydrotube`) lose tendrils, down to one, then
  length (their radius only sets which chunks they are drawn in) (`Ore/Core/VeinScaling.cs`).
  Grade is untouched, and each vein's spread is kept, so small and large deposits stay apart.
  Coal and the industrial minerals lose a quarter. It acts on the generators the game builds from
  the deposit files (a postfix on `GenDeposits.initAssets`), so every variant, whatever patched
  it in, is covered; the log says what was scaled. The factors began as a first cut; the survey of
  #445 found anchored veins about twice the target, so copper, iron, bismuth, zinc and platinum
  carry half that factor outright (`docs/oregen.md`, "Survey").

  It covers hydrothermal districts too, whose ore IOG places itself, outside the game's deposit
  generators (`Ore/Game/DistrictVeinSizes.cs`, a postfix on `HydrothermalDistrict.DetectOreZones`;
  rules in `Ore/Core/DistrictVeins.cs`, data in `config/ore-districts.json`). A district stays a
  rich field of veins, but finite ones: at most 8 veins (ore shoots and ladder veins) of each metal
  per district, chosen by a stable hash, except gold, silver, platinum and chromium, which only
  districts provide and which keep every vein; the others' ore in further veins and in horsetail
  lenses is not placed. IOG's veins run from the bottom of the world to the top, so each kept vein
  is cut to a band of heights under sea level that gives a size drawn from the metal's small,
  typical and large in `config/ore-sizes.json` (the band's height from the vein's geometry, its
  ores' shares and the ingots of their poorest grade). Gems and minerals are left as IOG makes
  them. The log has a line per district (`Smaller deposits: district 'magmatic-granitic-deep' at
  (...): 65 of 310 ore zones kept; veins per metal bismuth 8 (was 69), ...`).
- **`RarerDistricts`** (#441): IOG's hydrothermal districts, the only source of gold and silver
  quartz and most chromite and platinum, tile the world in 7 km squares instead of 4–6 km
  (`patches/ore-rarerdistricts.json`, on its `minDistanceBetweenDistricts`); with IOG's hard-coded
  40% chance per tile that is one per 122 km², about one per 11 km square.

Admin commands (`controlserver`), under the pack's `/sh` root, to check the rule:

- `/sh ore cell <x> <z> <metal>`: the cell holding the absolute block position for a metal, the
  seed, its eight spots (block, chunk and spawn-relative positions) and how each stands (active,
  placed, failed, skipped, fallback), and which chunk's first try places the vein.
- `/sh ore here`: for every managed metal, the active spot of the cell you stand in, its distance
  and direction, and how it stands.

Code: `Ore/Core/` (the grid and spot hash, the cell states, the world record, the vein scaling;
game-independent, tested in `tests/Ore/`), `Ore/Game/` (`OreSystem`, `OreCellPlacement`,
`DepositSizes`, `DistrictVeinSizes`, `ProPickShutdownGuard`, `Commands/`). If IOG is missing or its filter or fields changed, the server logs a
warning and leaves IOG's own rule and sizes; the districts patch does nothing without IOG.

With no switch, `OreSystem` also guards the prospecting pick's own deposit setup
(`Ore/Game/ProPickShutdownGuard.cs`): vanilla's `ProPickWorkSpace` builds a second set of deposit
generators on the thread pool, which takes seconds with IOG, and nothing waits for it. A server
stopped before it ends has nulled its logger, IOG's next warning throws on that thread, and the
process dies (a test host, or a world left right after loading). A finalizer on that task drops
what it throws once the server is shutting down.

### Placer fields, deposits and ore maps (`PlacerFields`, `PlacerCellSizeMetres`)

The rest of the ore epic's first half (#435; details and the traders' API in `docs/oregen.md`), in
`Ore/`, next to the ore cells above.

**Placer fields** (#442, server, default on, **new worlds only**, recorded with the ore switches in
`seraphhorizons:oreworld`): one rich gravel field per placer cell, a square of
`PlacerCellSizeMetres` (1,500) blocks, the first metal a new player can reach (about 750 m away).
The seed picks sixteen spots per cell, as ore cells do (`Ore/Core/PlacerCells.cs`, its own salt);
when the chunk column holding the active spot generates (the TerrainFeatures pass, after terrain,
soil and water, before plants), the field goes in that column if its terrain suits: dry ground of
at most 8 blocks' relief under the disc, mostly soil, gravel or sand, with water within 8 blocks or
on low ground (2 or more blocks under the column's edges). Otherwise the spot fails and the next
becomes active; a cell with none left has no field (the log says why each spot failed:
`Placer fields: cell 341, 341 spot 1 at ...: unsuitable (water 0/1024, heights 128-143, ...)`). A
field is 300–600 blocks (seeded) of `richgravel-{rock}` in a disc one or two blocks thick, flush
with the surface or one block under it, of the rock found under it, so Wilderlands Panning's per-rock
tables decide what it pans. State in `seraphhorizons:placercells`. Also, in such worlds:

- the scattered rich gravel (Wilderlands Panning's `richgravel` deposit, 75 tries per chunk) is cut
  to a quarter (a postfix on `GenDeposits.initAssets`);
- every rock's rich gravel pans native copper. Wilderlands Panning 1.0.9 gives copper at 3% to rich
  gravel without a table of its own, and has fourteen per-rock tables (which win for their rock):
  basalt and chert 2%, andesite 2%, bauxite 1.5%, sandstone 1%, and none for conglomerate,
  limestone, peridotite, phyllite, slate, chalk, claystone, granite and shale, among the commonest
  surface rocks. Those nine get `nugget-nativecopper` at 1%, added to the pan's table on the server
  after the patch loader (the log lists them). Panning yield is still to be checked in game (#442).

**Deposit registry** (#443): `OreSystem.Deposits` (`Ore/Game/DepositService.cs`) lists, from the
seed and the cell books alone, every metal's deposit and every gravel field within a radius
(metal, cell, active spot, state); verifies one by generating its spot's column if need be (which
places the vein or moves the cell to its next spot, which is followed), then its column and the
eight around it, and counting the metal's ore there as the survey does (each block's drops times
their `metalUnits`; 5 units a nugget, 20 nuggets an ingot); classes it small, medium or large by
thirds of the metal's range (`config/ore-sizes.json`'s `smallIngots`..`largeIngots`: copper and
iron 150–1,000, ...) and marks it sold out below a tenth of small. The world-wide record
(`Ore/Core/DepositRegistry.cs`, `seraphhorizons:deposits`) holds per (metal, cell) and gravel cell:
unsold, sold (to whom, game day), or sold out, and the last measurement. Coal and the minerals are
not listed.

**Ore and gravel maps** (#444): `seraphhorizons:oremap` and `seraphhorizons:gravelmap`
(`Ore/Game/ItemOreMap.cs`, after the game's locator map), stack size 1, ordinary items. A map holds
its deposit, metal, size tier, precision and marker position; right-click adds a pinned waypoint
("Copper deposit (large)", "Rich gravel (granite)") and keeps the map. Precision 1 marks within
400 m, 2 within 150 m, 3 the deposit itself (its measured centre); the offset is seeded from the
deposit, so every copy agrees and a better tier's marker lies between the worse one's and the
deposit (`MapPrecision`). Gravel maps are exact. `OreSystem.Maps.Issue(player, deposit, precision)`
(`Ore/Game/MapIssuer.cs`) builds the map and marks the deposit sold, refusing one already sold or
sold out: the call the traders make (#455). The items exist in every world; without ore cells or
placer fields there is nothing to issue.

Admin commands (`controlserver`), under `/sh ore`: `list [metal] [radius] [--unsold|--sold|--soldout]`
(default radius 6,000; ids `copper:102,102`), `gravel [radius]`, `verify <id>` (answers when the
chunks are ready), `tp <id>`, `registry mark <id> sold|soldout`, `registry reset <id>`, and
`givemap <player> <metal|gravel> <precision 1-3>` (the nearest unsold deposit to the player,
verified, issued to them).

Tests: `tests/Ore/PlacerCellsTests.cs`, `DepositRegistryTests.cs` (spots, field sizes, terrain
suitability, the placer book, the registry's states, size tiers, map offsets);
`tests/PackTests/OreMapsScenarios.cs` (Atlas, a fixed seed: the switch recorded, the scattered gravel
cut, every rock panning copper, the registry listing from the seed, verifying an ungenerated deposit,
`givemap` and the waypoint, a gravel cell resolving to a field of rich gravel and its map).

## Trading

The trader overhaul (epic #436), in `Trading/`: traders on a grid of camps, what everything is
worth, regional supply, standing, schematics, maps, orders and travelling merchants, the pack's own
trade window, and the admin tools for both overhauls. Design and engine notes are in
`docs/trading.md`.

### Trade window (no switch)

The pack's traders trade through a window of their own (`Trading/Window/`), opened by the
dialogue's "Got anything to trade?"; vanilla's trade dialog stays for every other trader (story
NPCs, vanilla worlds). The header names the trader, its type and region and shows your standing:
the tier and a bar to the next, the raw numbers in brackets ("Regular [310 / 800]"). Tabs:

- **Trade**: what the trader sells and what it buys (item, price and stock on hover), and below,
  hatched, the goods your standing does not unlock yet with the tier that does (its rare stock,
  schematics and other gated goods). There are no carts and no Deal button: click a good to see it,
  hold the mouse on it to buy one lot (a ring fills, as Carry On's hold to pick up), keep holding to
  buy the next. To sell, put a stack in the sell slot: it shows what the trader pays for it, or why
  it won't take it; hold Hold to sell to sell one lot, keep holding for more. What is left in the
  sell slot comes back to you when the window closes. While the window is open, the tooltip of
  anything in your inventory says what this trader pays for it (with the breakdown and which budget
  pays) or why not.
- **Orders (n)**: the trader's orders on offer and yours there: the item, how many and how many
  delivered, the price per item, the premium and the time left; Take, and Hand in (from anywhere in
  your inventory).
- **Deliveries (n)**: the trader's offer (where, how far and which way, the deposit, the fee, the
  time) with Take and Mark on map, your packages from or for it, and Hand in at the receiver.
- **Maps & leads**: ore maps (metal, size, distance, precision), the gravel map and leads (target,
  distance, direction); sold-out and locked ones say why. Bought by holding, as goods.
- **Standing**: the five tiers with their thresholds, yours marked, what it gives you against the
  next, and how to earn standing.

The footer shows your gears, the trader's, and its side budget for goods off its list. A feature
switched off (`TraderStanding`, `TraderOrders`, `TraderDeliveries`, `TraderMaps`,
`EverythingHasAPrice`) has no tab or line. The window works only next to the trader: it closes when
you walk away, and the server refuses a trade from anyone but the player trading, from further
than the trade's reach. Every trade is made at once on the server, one lot at a time, through the
game's own deal, so money, stock, the wallet and side budget, supply, standing, orders and the map
checks apply as they did.

Talking to a pack trader also offers "How do you see me these days?" (while `TraderStanding` is
on): the trader says where you stand, what that gives you and what the next tier unlocks, and how to
earn more, with the numbers in square brackets after each line. The pack ships the dialogue its
traders use (`assets/seraphhorizons/config/dialogue/trader.json`, made from the game's by
`Trading/tools/make_entities.py`); BetterRuins' quest dialogues for the curio dealer and the farmer
get the same option by patch.

Tests: `tests/Trading/Window/` (tabs, header and footer, every tab's lines, the standing reply, hold
timing, the request guard, locked stock, the wire format); `tests/PackTests/TradingWindowScenarios.cs`
(Atlas: one lot bought moving gears, stock and standing; one sold off the list from the side budget,
an empty side budget and money refused, the rest given back on closing; refused from afar, for
another player and for a trader gone; an order taken and handed in; a delivery taken and marked on
the map; every pack trader's dialogue, BetterRuins' included, with the standing option, the reply's
numbers, and every line the window shows in the lang file).

### Traders (`TraderGrid`)

The trader overhaul's first wave (epic #436; design and engine notes in `docs/trading.md`), in
`Trading/`. Eleven trader types take over from vanilla's nine and the other mods' traders: smith,
mechanic, prospector, farmer, cook, tailor, carpenter, mason, animal dealer, general store and curio
dealer (`seraphhorizons:trader-{gender}-{type}-{climate}`, vanilla's trader with the pack's class).
Each stocks from its own list (`assets/seraphhorizons/config/tradelists/trader-{type}.json`): a core
always on the shelf, a few rotating slots, more by the camp's climate (cold, temperate, hot) and rock
(sedimentary, igneous, metamorphic), and a bigger wallet than vanilla's (60–150 gears). Every good
vanilla's lists and the pack's mods trade has a place in one of them. Metal and metal goods, glass and
fired goods, leather and fine cloth and machine parts are player-supplied: listed, but never on a
shelf until the supply system (a later wave) puts them there, so a fresh world's smith sells fuel and
flux and buys metal.

`TraderGrid` (server, default on) places lone camps on a seeded 2 km grid, about one per 2 km cell,
in place of the game's randomly placed ones; the camp kinds are the game's, BetterTraders' and the
other mods' camp buildings, chosen by climate as the game does, and the trader in a camp is the
cell's type. Each cell has eight seeded spots: any spot whose chunk generates while the cell has no
camp tries, and the first to place wins; if all eight miss, up to 24 of the cell's later chunks get a
second chance. A spot's chunk tries every camp kind, schematic and rotation at its positions with
the game's own checks, but on ground up to 2 blocks uneven (`slopeTolerance` in
`config/trading/camps.json`; 0 is the game's rule of exactly level ground), the camp seated on the
median of the game's samples and the ground under it levelled (#599; `docs/trading.md`, "Camp
placement"). Worlds saved before keep their camps; a cell still waiting tries its spots not yet
generated in any order, and a cell that had given up gets second chances in its new chunks. A trader spawner inside a story structure (vanilla's treasure hunter, or one a mod adds)
keeps its own trader: a story NPC, with its own dialogue. Neighbouring cells never have the same
type, and a prospector is never more than two cells away. Every 8 km cell keeps its centre free for a settlement (later). New worlds only: a world
takes the grid at its first start with this mod if the switch is on then, and keeps that; an existing
world keeps vanilla's camps and traders. Turned off later, the world's new chunks get the game's camps
again. The trader types and their lists exist either way.

Admin commands (privilege `controlserver`): `/sh trade camps [radius]` lists the grid cells within
radius blocks (default 4096) with their type and camp, the next spot not generated yet, or that every
spot missed; `/sh trade tp <cellX,cellZ>` goes to a cell's camp, generating its next spot first if needed.

Tests: `tests/Trading/` (grid, types and their bias, regions, list resolution, restock, the camp
state with its order, second chances and old saves, the camp ground test and levelling, the shipped
lists and the curation fixture); `tests/PackTests/TradingCoreScenarios.cs`
(Atlas, a fixed seed: the 66 entity types, every list resolving in the pack with stock everywhere,
every trader's trade window title and name, a spawned trader stocking from its list, the game's camps
taken over, the spawner rewrite, cells' camps being decided, one on uneven ground with its footprint
levelled, `/sh trade camps` and `/sh trade tp` to it, and nothing logged);
`tests/PackTests/TradingStoryScenarios.cs` (Atlas, a survival world with its story locations: the
treasure hunter's spawner keeps its trader, a camp's 150 blocks off is still rewritten).

### Item base values (no switch)

Every item's base value in rusty gears (#449, part of the trader overhaul #436), for the trading
features to price with: `config/item-values.json`, generated by `tools/item-values` from the pack's
recipe export (raws priced by hand, everything else from its cheapest recipe plus a labour markup;
the rules and the numbers are in `tools/item-values/README.md`). It changes nothing in play by
itself, so it has no switch.

The table's `values` are gears per item, except liquids: the codes in its `perLitre` object (code
to the liquid's items per litre, 100 for most) are priced in gears per litre, the unit a player
thinks in (a portion is a hundredth of a litre). `floorZero` keeps its meaning for them (under a
gear per full stack of portions).

`Trading/Values/`: `Core/ItemValues.cs` is the lookup (no game). `ValueOf(code)` is gears per item,
for a liquid its per-litre value over its items per litre (trading prices stacks of portions, so
every caller, the economy's off-list offers, orders and the value checks, gets per item without
knowing the unit), 0 for an item worth under a gear per full stack (`floorZero`) or unknown; `IsWorthless(code)` is
true for a known item worth nothing. A code missing from the table falls back to its variant
family: the longest prefix ending at a `-` that table codes share, never shorter than the path's
first segment (`game:plank-oak` takes the average of `game:plank-*`), and a code with `*` averages
its matches. A family or wildcard averages per-item values; when every member is a liquid with the
same items per litre (`game:ciderportion-cherry` in the ciders), the answer is per litre too, the
average per-litre value rounded to three decimals; any other family is per item. The display form
is `PerLitre(code)` (items per litre, or null for per item) and `DisplayValue(code)` (the value in
its unit and that), and `Lookup(code)` carries both (`PerLitre`, `Display`) next to the per-item
`Value`. A table never changes once loaded, so it caches each wildcard's answer: `/sh trade
values suspicious` looks up the same few hundred patterns across some 80,000 grid recipes, and
uncached that took close to a minute. `Game/ItemValuesSystem.cs` loads the asset on the server when assets load and serves
it: `ItemValuesSystem.For(api)`.

`/sh trade value [item code]` (`controlserver`) prints an item's value and where it comes from
(direct, family fallback with the family, or missing); without a code, the held item's. A liquid's
reads "1.85 gears per litre (0.0185 per item, 100 items per litre)"; the JSON has `value` in its
`unit` (`"litre"` or `"item"`), `itemsPerLitre`, and `perItem` and `effective` per item. The `/sh`
root and its `trade` branch are shared with the other trading features (`GetOrCreate`).

Schematics have no value in `item-values.json` (`tools/item-values`, #506): they are kept on
crafting and traders are their only source, at their lists' prices. A machine gated behind a
schematic is worth its consumed parts and labour. `seraphhorizons:gear-steel` takes its cheapest
route (the gear cutter, or the reclamation lottery: ten oiled gears less the nine steel bits the
failed rolls give), and `seraphhorizons:largegear-steel` its gear cutter route; neither is a hand
price.

After a pack change that adds, removes or re-recipes items, rebuild the table from a fresh export
(`tools/item-values/README.md`). CI's export job fails when an item of this mod's trade lists
(`config/tradelists/`) has no value or is retired with nothing valuing it, and when the shipped
table differs from a rebuild from the export (smoke loads this mod, so the export has its items).
The table's `switches` names, per code, the config switches its value exists by (#523).

**In the handbook** (#506): every item and block page shows, under its name and description, the
rusty gear's icon and the item's value (a family fallback shows like a direct value; a liquid's
reads "1.85 per litre", `itemvalues-handbook-perlitre`), or "No trade
value" when the table has none or when a switch its value depends on is off. The table's optional
`switches` object maps a code to the switches its value exists only with (its cheapest route takes a
recipe or an item they add; `tools/item-values` works that out from the export's `switch` fields,
below); `ItemValues.SwitchesOf(code)` reads it and `ItemValues.Shown(code, isOff)` decides the line.
The server writes its off switches to the world config (`seraphhorizons:switchesOff`) in `Start`,
as the unified woodworking does its state, and the client reads them from there.
`Game/ValueHandbook.cs` is a client-side Harmony postfix (own id `seraphhorizons.itemvalues`) on the
game's private `CollectibleBehaviorHandbookTextAndExtraInfo.addGeneralInfo`, which appends an
inline item stack component (the rusty gear) and the number; not a collectible behaviour, since the
handbook calls only the first `ICustomHandbookPageContent` of a collectible and Tidy Variants' groups
need theirs to be it. The rusty gear's own page adds a sentence saying what the number is, and that liquids are priced
per litre. If the
game's method changes, a warning says so and pages show no value.

#### Switch ownership

Which switch adds a recipe or an item, for the recipe export (`recipes[i].switch`,
`items[code].switch`, docs/recipe-browser/schema.md) and so for the table's `switches`.
`SwitchRegistry.cs` builds it from what each feature itself leaves out with its switch off: the
recipe files and the block and item type files its system disables (`GearCutterSystem.TypeAssets`
and `RecipeAssets`, `GearBlanks`', `BuckingSawmillSystem`'s and `RosserSystem`'s `BlockAssets` and
`RecipeAsset`, `PicklingTubSystem`'s tub, bare gear and recipe, `GearReclamationSystem`'s cooking
and barrel recipes, `SteelBitsSystem.RecipeAsset`, `HandcarSystem.TypeAssets` and `RecipeAsset`, `CreativeSteamSource.BlockAsset`,
`CastPipesSystem.TypeAssets` and `RecipeAssets`), reading each
type file's `code` for the codes it defines (`domain:code` and `domain:code-*`). What no such list
says is hand-listed in `Core/SwitchOwnership.cs` (`HandListed`): the debarked trunks the Rosser
switch's patch adds to Logging Expanded's trunk (`loggingmod:treetrunk-*-debarked-*`), the pipe molds
the CastPipes switch's patch adds to smex's tool mold (`smex:toolmold-*-pipe`), and the
exporter's own recipe types for the gear chain (`picklingtub` and `lottery`: `GearReclamation`;
`gearcutter`: `GearCutter`; `drawbench`: `DrawBench`, whose type and recipe files are
`DrawBenchSystem.TypeAssets` and `RecipeAssets`; `pressbrake`: `PressBrake`, whose files are
`PressBrakeSystem.TypeAssets` and `RecipeAssets`). `Core/SwitchOwnership.cs` answers `SwitchForRecipe(id)` (by the export
id's type, its source file, or the code it is keyed by, as a transition or a casting is) and
`SwitchForCode(code)`; the exporter calls `SwitchRegistry.For(api)` by reflection.

| Switch | Owns |
|---|---|
| `BuckingSawmill` | the mill's blocks (`seraphhorizons:buckingmill*`) and `recipes/grid/buckingmill.json` |
| `Rosser` | the rosser's blocks (`seraphhorizons:rosser*`), `recipes/grid/rosser.json`, the debarked trunks |
| `GearReclamation` | the pickling tub, the bare steel gear, the tub's, degreasing and barrel recipes, the tub and lottery records |
| `GearBlanks` | the blanks, their molds (`seraphhorizons:toolmold-*`), their clay forming and smithing recipes, the molds' casting |
| `GearCutter` | the cutter's blocks and parts, its grid and smithing recipes, the cutter records |
| `DrawBench` | the bench's blocks and dies, its grid and smithing recipes, the draw bench records |
| `PressBrake` | the brake's blocks, its grid recipe, the press brake records |
| `SteelBitsRecovery` | the packing recipe (the packed charge itself exists either way) |
| `Handcar` | the handcar's item (`seraphhorizons:handcar`) and `recipes/grid/handcar.json` |
| `CreativeSteamSource` | the creative steam source block |
| `CastPipes` | the cast pipe blanks, the pipe molds (`smex:toolmold-*-pipe`), `recipes/clayforming/pipemold.json` and `recipes/grid/castpipe.json`, the molds' casting |

A switch that only takes things away (`HydrateTunRetired`, `IrrigationVesselRetired`,
`BloodSausageInMixingBowl`, `PanningDropsTrimmed`, `TraderSchematics`, the retired stations of
`UnifiedWoodworking`) or only changes what an existing recipe takes (`GearConsumers`,
`IronWoodworkingMachines`, `AgeOfFlaxRebalance`, `MachineSchematics`) owns nothing: no value exists
only because it is on. A new feature that disables its own assets with its switch adds its lists to
`SwitchRegistry.Features()`. Tested by `tests/SwitchOwnershipTests.cs` (the matching) and, against
the pack, `RecipeExportValueScenarios` (every listed asset exists, every hand-listed pattern matches
a registered code, every hand-listed recipe type is in the export) and `SwitchesOffScenarios`
(nothing a switch owns is registered with it off).

### Everything has a price (`EverythingHasAPrice`, `BuySpread`)

The trader overhaul's pricing (#450; `Trading/Economy/`, notes in `docs/trading.md`). A trader of the
pack takes any item, not only what its list buys, and pays a fifth of what it is worth, a pawnshop's
spread (`BuySpread`, default 0.2); what it asks when it sells is unchanged. Listed goods keep the
list's price, which already holds the spread (the lists' buying prices were divided by five on
2026-10-06, and do not follow `BuySpread`), and are paid from the trader's wallet. Anything else is
priced from the item's base value × the spread × the fit: three quarters for goods a related trader
buys, 0.6 for a weak link (and the curio dealer for anything another trader buys), half otherwise
(`assets/seraphhorizons/config/trading/trader-relations.json`). It is paid from a **side budget**, a
quarter of the trader's wallet, refilled at every restock. Cheap goods sell by the fewest items worth
a gear (a trade is priced in whole gears), and a cheap listed good is bought by a bigger lot for the
same reason. Refused: maps and leads (the `refused` prefixes), money, goods worth less than a gear per
stack, goods the value table doesn't know, and goods that at this trader come to under a gear per full
stack. The trade window's sell slot and the tooltips of your own items show the offer's breakdown
(value × spread × fit × supply) and which budget pays, a refused good says why, and the window's
footer shows what is left in the side budget. Switches: `EverythingHasAPrice` (default on) and `BuySpread` (0.01–1,
default 0.2); the server's settings go to its clients with each trader.

Admin: `/sh trade price [item]` shows what the nearest trader (16 blocks) pays for an item, or the
held one, and why.

### Regional supply (`RegionalSupply`, `SupplyHalfLifeDays`, `SupplySpreadFraction`)

Per item and 8 km region (the grid's settlement cells, about sixteen camps), a supply level saved
with the world (#451). Selling to any of the pack's traders raises it by the goods' value in 10-gear
units, so a stack of planks and an iron ingot move their items' levels about alike. Buying from a
trader lowers it. Once a calendar day every level decays (half-life
`SupplyHalfLifeDays`, default 10 days), and a share (`SupplySpreadFraction`, default 0.1) moves to the
eight neighbouring regions, thinning with distance. Every price at a trader, buying and selling, is
scaled by `0.3 + 0.7 / (1 + level / 5)`: ten iron ingots sold take its price to about 0.7×, and it
never goes under 0.3×. Player-supplied goods are shelved at a restock only from level 1 up, with half
the region's supply (counted in the entry's stacks) as stock, at most twice the entry's own. Per item,
not per metal. Switch: `RegionalSupply` (server, default on).

Admin (privilege `controlserver`, in the caller's region or the spawn's): `/sh trade supply
[item|all]`, `/sh trade supply set <item> <level>`, `add <item> <amount>`, `reset [item|all]`,
`trace <item>` (its last changes), and `/sh trade simulate <days>` (supply decays and spreads for
that many days, and the loaded traders' restock clocks move on as much).

Tests: `tests/Trading/Economy/` (the fit table, the price curve and offers, the buy spread, the side
budget, supply decay, spread, shelving and saving, and the shipped lists' buying prices held to a
fifth of the value table and under 0.6 × the lowest ask, `ShippedListPayTests`); `tests/PackTests/TradingEconomyScenarios.cs` (Atlas: an off-list
sale paid from the side budget through vanilla's own deal, worthless goods, money and an overdrawn side
budget refused, supply rising, falling over `simulate 20`, and player-supplied iron and steel shelved
once supply is high).

### Standing (`TraderStanding`)

Each trader remembers you (#452, `Trading/Standing/`, server side, default on). Standing is kept per
player and trader: a camp's trader is its grid cell (`camp:x,z`, so the camp's next trader knows you
too), any other of the pack's traders its entity (`entity:n`). Every deal in the trade window
earns a point per gear changing hands, either way; orders and deliveries earn more, at
both ends of a delivery made on time. Standing is lost only by failing a delivery or abandoning an
order, never below 0. A tenth of your best standing with another trader of the same type within
`TraderStandingSpilloverKm` (default 6) counts too, through the grid's placed camps.

Five tiers, in `assets/seraphhorizons/config/standing-tiers.json`: stranger (0), known (60), regular
(250), trusted (800), partner (2000). Each tier's unlocks are data for the features that read them:
map tier and maps to other traders, a price factor each way, the wallet tier, order and delivery
size, rare stock. The wallet and the shelves follow the best tier among players who traded with the
trader in the last 14 days (its gears are topped up towards that tier's wallet at the weekly
restock; its rare stock, schematics and further leads are shelved for it), and the player trading
gets their own tier's prices and map precision (see "Maps and leads"). The trade window's header
and Standing tab show your standing there, and the trader tells you when you ask "How do you see me
these days?" (see "Trade window"); reaching a tier says so in chat.

Admin (`controlserver`): `/sh trade standing <player> [trader]` lists a player's standing with every
trader they or their company have a record with, or one trader in detail with its recent events;
`/sh trade standing set <player> <trader> <points>` and `reset <player> [trader]` change the
player's own record. A trader is its id as listed, or `near` for the one next to you. Saved with
the world (`seraphhorizons:standing`).

### Companies

Standing is pooled by company (#463): a company is one of the player's vanilla groups (`/group
create`, `/group join`), the first they joined unless they choose another with `/sh company <group>`
(any player; `/sh company` alone shows it). Every gain goes to the player's own record and the
company's, and a trader reads the better of the two. Forming or joining a company raises its standing
with each trader to the joiner's own if that is higher, once per membership. Leaving, being kicked or
the group disbanding takes nothing along: the player keeps their own record, the company keeps its
own. A failed delivery or an abandoned order costs the company and the player who took the job, not
the other members. Groups are the server's (shared by its worlds), the records are the world's, keyed
by the group's uid; a group that no longer exists is no company. Joining, leaving and disbanding are
caught by patches on the server's group methods; if those move in a game update, a log line says so
and companies catch up the next time standing is read.

Admin: `/sh trade company <player> [group]` shows the player's company, its members pooled and its
standing per trader, or makes `group` (one they are in) their company.

Tests: `tests/Trading/Standing/` (tiers and thresholds, deal and penalty points, spillover, the
shipped tiers, company choice, merge by max on joining, leaving keeping the personal record, penalty
routing, disband); `tests/PackTests/TradingStandingScenarios.cs` (Atlas: a deal through the trade
packet raises standing by its gears and a failed one does not, a higher tier raising the wallet at the
next restock, a company made with the server's group manager pooling by max through `/sh company`
and `/group leave` leaving the player their own).

### Schematics (`TraderSchematics`, `MachineSchematics`)

Schematics are sold by traders and found nowhere else (#468), and every machine and vehicle needs
one (#469); in `Trading/Schematics/`, driven by `assets/seraphhorizons/config/schematic-gates.json`.
Server side; clients get the changed recipes and item types from the server.

`TraderSchematics` (default on) covers every schematic in the pack: the game's glider and
translocator, BetterRuins' 30 `br-schematic-*`, Cartwright's Caravan's `cartschematics-*`, Abyssal
Depths' diving gear, the walking stick's flintlock and this mod's own.

- **Out of loot**: any loot list entry naming one (stack randomizers: BetterRuins' gear randomizer,
  Abyssal Depths' four; loot vessels; panning drops) is taken out of the item and block type assets
  before the game reads them. In structures (ruins, BetterRuins' story locations, the Resonance
  Archive), a schematic in a chest becomes parchment. Tobias still hands over the translocator
  schematic his translocator's repair needs.
- **Not copyable, not craftable**: a recipe that makes one (other than a one-slot conversion) is
  removed: the game's glider copy and Cartwright's carts and signs from parchment and charcoal.
  BetterRuins and Abyssal Depths ship their copy recipes disabled (BetterRuins' ConfigKit setting
  turns its on; it would be removed too). Scrolled's rolling and unrolling are one for one and stay.
- **Kept on crafting**: every recipe that uses one keeps it (`consume: false`). Cartwright's and
  Abyssal Depths' used to consume and give back; the walking stick's hidden gun ate it once Scrolled
  removed the item's old `noConsumeOnCrafting`.

`MachineSchematics` (default on) adds `seraphhorizons:schematic-{machine}` (one item, a `machine`
variant group, the game's schematic sheet with the glider's or the translocator's drawing) to each
machine's first-stage grid recipe, kept on crafting:

| Schematic | Gates | Seller, standing tier |
|---|---|---|
| `windmill` | windmill rotors (the game's, Millwright's) | carpenter, 1 |
| `waterwheel` | the water wheel (its first stage) | carpenter, 1 |
| `handcrank` | the hand crank | general store, 1 |
| `panningmachine` | the panning machine (one recipe, every tier) | prospector, 1 |
| `transmission` | axles, angled and spur gears, large gear and sections, clutch, transmission, brake; Millwright's brake and axle passthroughs | mechanic, 2 |
| `helvehammer` | the helve hammer base | mechanic, 2 |
| `pulverizer` | the pulverizer frame | mechanic, 2 |
| `sawmill`, `chopper` | Immersive Woodworking's frames | carpenter, 2 |
| `buckingmill`, `rosser` | this mod's frames | mechanic, 2 |
| `gearbox`, `centeredspurgear` | Mechanical Power Expanded's | smith, 2 |
| `cablecar` | Gondola's route planner, which places every station and tower | mechanic, 4 |
| `biplane` | the biplane's trestles | mechanic, 4 |

The schematic takes the recipe's first empty slot. A narrow or short recipe gets a column or a row.
In a full 3×3 grid, two slots of the ingredient filling the most slots become one at double quantity
(the large gear, the pulverizer frame, the metal rotor, the sawmill and rosser frames, the panning
machine). MadMechanics' vertical clutch and transmission are conversions of the game's and stay as
they are. Off, the recipes are as their mods ship them and no trader lists the machine schematics.

**Sellers**: each schematic is in its seller's core from its standing tier (`standingTier` on the
trade list entry; the core shows it once the buyer's tier reaches it). Everyday BetterRuins families
are tier 1 at the carpenter, mason, smith, tailor, farmer, general store or curio dealer; the cart and
ship wrights and Cartwright's carts are tier 2 at the mechanic; the glider, translocator, flintlock
and diving gear are tier 3 at the curio dealer. Prices are hand-set in the lists, 10 to 250 gears.
Cartwright's canopies and sides schematics gate nothing in 1.9.1 and are not sold.

Tests: `tests/Trading/Schematics/` (the table, the item variants and their text, every sale in its
seller's list at its tier, slot placement, the recipe rules, `standingTier`);
`tests/PackTests/TradingSchematicsScenarios.cs` (Atlas: every gated output's recipes take their
schematic, a helve hammer base crafts and keeps it, BetterRuins' recipes keep theirs, no recipe makes
or copies one, the stack randomizers and every loot list hold none, structures hand out parchment,
every sale resolves in its seller's list).

### Maps and leads (`TraderMaps`)

Traders sell maps to deposits and leads to other camps (#455; `Trading/Maps/`, server side, default
on; notes in `docs/trading.md` and `docs/oregen.md`). Prices are in
`assets/seraphhorizons/config/trading/map-prices.json`, before standing's price factor.

- **Ore maps**, from prospectors: one offer per metal, the nearest unsold deposit of the deposit
  registry within 5 km (whether or not anyone has generated its chunks), at most four metals,
  nearest first. A stranger is offered precision 1 (within 400 m); standing's map tier buys
  precision 2 (tier 1, "known") and exact maps (tier 2 up). Price by precision and the deposit's
  last measured size (5–32 gears, "unsurveyed" until measured), times the metal's factor.
- **Gravel maps**, from every trader: the nearest unsold rich gravel field within 2 km, 5 gears.
- **Leads** (`seraphhorizons:traderlead`), from every trader: a lead to the nearest camp for
  anyone (2 gears); with `mapsToTraders` (tier 2 up), a lead to a prospector, to a camp two or
  three cells away and to the nearest ground kept for a settlement. Right-click a lead to put the
  camp on your world map (icon `trader`). A lead may point at a camp nobody has generated yet: the
  sale generates its spot's chunk and draws the lead to where the camp was placed.

A bought map is checked before it is handed over: the deposit is reserved, verified (its chunks
generated and its ore counted; the chat says "the prospector is checking the claim" while that
takes), then marked sold and the map arrives in place of the "being checked" sheet. A deposit sold
meanwhile or worked out refunds the price. No deposit is sold twice, by any trader; once every
deposit in reach is sold the shelf shows "Ore maps: sold out" (unavailable). Traders never buy maps
or leads back.

The shelf is shared, so it is stocked for the best customer of the last 14 days: the further leads
(and rare stock) stay on it for a stranger until the next restock after that customer stops coming,
but a stranger can't buy them (the window shows them hatched, with the tier that sells them). The
trade window shows the player trading their own prices and map precision (one player trades with a
trader at a time).

Tests: `tests/Trading/Maps/` (offer selection, sold out, precision by map tier, the shipped price
table, lead targets); `tests/PackTests/TradingMapsScenarios.cs` (Atlas, a fixed seed: a prospector's
ore map offers, buying one through the trade window, the registry marked sold and no other trader
offering it, a gravel map offered exactly when a field is in reach, a lead marking a camp, standing
changing a trader's prices and map precision).

### Orders and deliveries (`TraderOrders`, `TraderDeliveries`)

Traders give work (#453, #454; `Trading/Orders/`, `Trading/Deliveries/`, notes in
`docs/trading.md`), server side, both default on. Players take and hand in both in the trade
window's Orders and Deliveries tabs (see "Trade window").

**Standing orders** (`TraderOrders`): at every restock a trader puts up to one or two orders on
offer, each for something its list buys where it stands: about 5 gears' worth at its normal price,
in whole lots, with a premium of 1.3–1.6× over that price, held back from its wallet then (a trader
too poor makes none). The Orders tab lists them and yours there; Take takes one, scaled by your
standing's `orderScale` (1 for a stranger, 4 for a partner) as far as the wallet covers the bigger
premium, and gives you 3–6 days. Sell the goods to the trader as usual, or carry them and Hand in:
each item pays its normal price and its share of the premium,
the last one the rest and standing (`order` points). An order you took and delivered nothing for by
the deadline is abandoned and costs standing; delivered in part, it just ends.

**Deliveries** (`TraderDeliveries`): the Deliveries tab shows a trader's offer: a package for
another camp within `deliveryScale` × 3 km (none for strangers; a camp of another type where there is
one), with a deadline from the walk (5 minutes a km, half again as slack, at least 5 minutes, in game
time at the world's calendar speed: 2 km is 7.5 game hours by default), a deposit of 10–30 % of the
package's value from your gears and a fee of 20–40 %. Take takes it and hands you a
`seraphhorizons:package` (can't be opened, says where it goes and how long is left); one at a time
per sender; Mark on map puts the destination on your world map. Hand in at the receiver: on time, your deposit back, the fee from its
wallet and standing at both ends; up to a game day late, the deposit and half the fee and standing at
the receiver; later, the delivery fails: the deposit is gone, standing with the sender drops, and the
package is junk. Packages go only to the grid's camps, so a world without the grid has no deliveries.

Admin (`controlserver`): `/sh trade orders [trader|player]` (open orders, a trader's by id or `near`,
or a player's), `orders create <trader> <item> <qty> <days>`, `orders complete|cancel <id>`;
`/sh trade deliveries [player]`, `deliveries create <from> <to> <player>` (any two loaded traders or
placed camps; the deposit comes from the player), `deliveries complete <id>` (on time, wherever the
package is), `fail <id>`, `expire <id>` (the deadline is now). `/sh trade simulate <days>` runs both
clocks on. Saved with the world (`seraphhorizons:orders`, `seraphhorizons:deliveries`).

Tests: `tests/Trading/Orders/`, `tests/Trading/Deliveries/` (generation and scaling, the premium
maths, deadline conversion, both state machines, which outcome calls which standing hook);
`tests/PackTests/TradingOrdersScenarios.cs` (Atlas: a spawned trader's orders, one taken and filled
by selling in the trade window paying its premium and standing, one abandoned by `simulate`, a
delivery between two spawned traders handed in on time through the receiver's window for deposit and
fee, one failing past its grace and keeping the deposit).

### Travelling merchants (`TravellingMerchants`, `TravellingMerchantMinSupply`)

A player who builds a small inn and raises an inn flag in it gets visits from two travelling traders
(#456) that no camp has: a **travelling merchant** (seeds from far off, exotic fruit, olive soap,
candles; rare: a flute, meteoric iron shears and chisel) who buys copper, bronze and iron ingots,
leather, linen and twine, and a **travelling curio dealer** (paintings, coloured glass, shells,
amber; rare: the fish-and-rain painting, the spice merchant's coat, red spinel) who buys gold, silver
and gems. In `Trading/Visitors/`; server side (default on). Off, any visitor still about leaves.

**The inn** (`Core/InnRules.cs`, checked around the flag):

- **Stall**: a Cartwright's Caravan market stall (the entity) or the pack's **inn sign**
  (`seraphhorizons:innsign`: a ground sign and charcoal) within 12 blocks of the flag.
- **Walls**: a flood fill from the stall through open blocks (air, plants, liquids) stays within 12
  blocks of it. Any other block is wall; doors and trapdoors are wall too, open or shut. **Roof**: a
  block at most 8 above the stall.
- "In the room" is a block the fill reaches or one touching it, so furniture counts. **Bed**: any bed
  (`BlockBed`, or a block code starting `bed-`). **Table**: a block with a `table` part in its code,
  with food on it or beside it: an edible block, or a container (crock, bowl, pot, pie, shelf, ground
  storage) holding something edible.
- **Light**: lamplight at the stall at least 7, from lamps in the room as the game spreads it (a
  source's level less one per block); the sun never counts, so it is the stall's light at night. A
  torch three blocks off gives 11.

**The flag** (`seraphhorizons:innflag`: a linen block, charcoal and two sticks) makes an inn of the
building; whoever places it owns it, and right-clicking it says what the inn still lacks. Taking it
down ends the inn and any visit.

**The visit**: once a day an idle inn is checked. When the inn passes, **the owner is regular** (tier
2) with some placed camp within `TraderStandingSpilloverKm` (6 km), and **the region trades** in what
a visitor buys (the summed supply level of its buying list in the inn's 8 km supply region is at
least `TravellingMerchantMinSupply`, default 2, i.e. 20 gears' worth sold there and not yet drained),
one of the kinds that pass sets out: it arrives 2–4 days later (the owner is told), stays 3–5 days and
leaves; the next can come 10 days after. A world without the grid or standing skips the standing
condition, and one without regional supply (or the setting at 0) the supply one. A visitor arrives
only while the inn's chunk is loaded.

**The visitor** is the pack's trader (`seraphhorizons:visitor-{gender}-{type}-{climate}`, entity class
`SeraphHorizons.VisitingTrader`): its own list (`config/tradelists/trader-travellingmerchant.json`,
`-travellingcurio.json`), the economy's prices and supply, standing under the trader id
`visitor:general` or `visitor:curio`, the same at every inn. Its rare goods (`standingTier` 3) are on
the shelf when the inn's owner is trusted by its kind. It takes no damage, never fights or flees,
wanders at most 4 blocks, is never a camp for the grid, and says goodbye to players nearby when it
leaves. Inns and visits are saved with the world (`seraphhorizons:inns`); the visitor keeps its visit
on itself, and one whose visit ended while its chunk was unloaded leaves as soon as it loads.

**Commands** (privilege `controlserver`): `/sh trade inn check [pos]` (every rule and condition, and
the visit, for the inn around a position or the nearest flag within 16 blocks),
`/sh trade inn call <general|curio> [now]` (a visitor to the nearest inn, skipping the conditions and
the cooldown: on its way, or here at once with `now` or `--now`), `/sh trade inn dismiss` (the
visitor leaves and the cooldown starts, or a pending visit is called off). `/sh trade simulate <days>`
moves the visits on too.

Tests: `tests/Trading/Visitors/` (the rules on block grids: a complete inn, a hole in the wall, no
roof, doors, food on and beside the table, light by distance and walls, furniture outside, stall
entities; the visit cycle and its ranges; the book's JSON; the conditions; both lists: no problems,
10–15 specials no camp sells, rare goods by tier); `tests/PackTests/TradingVisitorsScenarios.cs`
(Atlas: an inn built in the world passes `inn check`, `inn call general --now` brings a merchant with
its specials that takes no harm and leaves after `simulate 5`, a curio dealer called the slow way
arrives on its day and is dismissed, an inn without a bed or light says so and is never visited, a
market stall stands in for the sign, a missing roof fails).

### Admin tools (`AdminTools`)

Debugging tools for server admins (#458, #459; JSON shapes, export format and hooks in
`docs/admin-tools.md`), under `/sh ore` and `/sh trade`. Server side, default on; they change
nothing in play. Every subcommand of both:

- needs `controlserver` (enforced for the whole tree, whichever feature registered the command;
  `/sh company` is the players' own and sits outside it);
- prints a one-line summary and the detail below it, and with `--json` anywhere in its arguments one
  JSON object instead (`command`, `ok`, `summary`, `lines`, plus the command's own fields);
- validates its fixed words (`on|off`, `missing|suspicious`, log channels) with the game's word-range
  parser, which lists them in `/help`; the 1.22 client has no tab completion for server commands.

A trader is `near` (the pack's nearest within 16 blocks), `camp:x,z`, `entity:n`, or a camp cell
`x,z`; a deposit is `copper:12,-3` or `gravel:341,340`. Files are plain names, read and written in
the server's `seraphhorizons-admin` folder (next to `Saves`), `.json` added when there is no
extension.

| Command | What it does |
|---|---|
| `/sh ore cells [radius]` | every metal's (and gravel's) cells within the radius (2,500): the active spot, primary or which fallback, and how each earlier spot failed (generated without a vein, generated before its turn) |
| `/sh ore cell <x> <z> <metal>` | one (metal, cell)'s decision trace from the seed |
| `/sh ore here` | the cell you stand in, per metal: active spot, distance and bearing |
| `/sh ore list [metal] [radius] [--unsold\|--sold\|--soldout]` | deposits from the registry: spot, generated, state, last measurement |
| `/sh ore gravel [radius]` | gravel fields: blocks, rock, distance |
| `/sh ore verify <id>` | generate and measure a deposit; answers with the result and the time it took |
| `/sh ore tp <id>` | to a deposit or gravel field (generated first) |
| `/sh ore count [radius]` | ore blocks by metal and grade in the loaded chunk columns within the radius (48, at most 256), in blocks and ingots |
| `/sh ore districts [radius]` | Interesting Ore Gen's hydrothermal district tiles within the radius (20,000): whether the tile rolled a district (from the seed), and for districts built this run their config, radius, faults and ore zones |
| `/sh ore markers [radius]`, `markers clear` | deposits and gravel fields as waypoints (`[sh]` in the title), for clients without the overlay |
| `/sh ore survey <chunks> <file>` | the ore survey tool's scan of chunks × chunks columns around you, written as the tool writes it (`<file>.json` and `<file>.json.cells.csv`; `ore_survey.py summary` reads it); answers in chat when done |
| `/sh ore registry mark <id> sold\|soldout`, `registry reset <id>` | a deposit's state by hand |
| `/sh ore registry clear <metal\|gravel\|all>` | forget every record of a kind |
| `/sh ore registry export <file>`, `registry import <file>` | the registry to and from a file (import also takes a `/sh trade export` file) |
| `/sh ore givemap <player> <metal\|gravel> <1-3>` | a map to the nearest unsold deposit, sold to the player |
| `/sh ore log on\|off` | every ore cell and placer field decision and every verification to `Logs/seraphhorizons-ore.log` |
| `/sh ore map on\|off [radius]` | the ore overlay on your world map (radius 12,000) |
| `/sh trade camps [radius]` | camp cells: type, placed camp, the next spot, or open (every spot missed) |
| `/sh trade tp <camp>` | to a cell's camp (generated first) |
| `/sh trade inspect [trader]` | type, region, list core and pool, current slots (core or rotating), wallet and its target, side budget, next restock, every player's and company's standing with it |
| `/sh trade restock [trader] [--full]` | the weekly restock now; `--full` draws every rotating slot anew and refills the wallet |
| `/sh trade reroll [trader]` | new rotating slots |
| `/sh trade wallet <trader> <gears>`, `budget <trader> <gears>` | set the gears, or the side budget |
| `/sh trade value [item]` | an item's base value and where it comes from |
| `/sh trade price [item]` | what the nearest trader pays for it, and why |
| `/sh trade values missing\|suspicious` | trade list entries and creative items with no value; items valued below the ingredients of their cheapest grid recipe |
| `/sh trade supply [item\|all]`, `supply set\|add\|reset\|trace` | regional supply where you are |
| `/sh trade simulate <days>` | advance supply and the loaded traders' restock clocks |
| `/sh trade standing <player> [trader]`, `standing set\|reset` | a player's standing |
| `/sh trade company <player> [group]` | a player's company |
| `/sh trade maps [trader]` | the deposits and gravel fields around a trader and whether a map of each could be sold now (and why not) |
| `/sh trade export <file>`, `import <file>` | supply, standing and the deposit registry (and the sections later systems register) as one JSON file |
| `/sh trade log on\|off [channel]` | `supply`, `standing`, `orders`, `deliveries`, `maps`, `visitors` (all without a channel) to `Logs/seraphhorizons-trade.log` |
| `/sh trade map on\|off [item]` | the trade overlay on your world map: camp cells by type, placed camps, settlement reserves, supply heat for the item (the held one) |

Orders, deliveries, the map shop and inn visitors add their own subcommands (`orders`, `deliveries`,
`givemap`, `inn`); they get `--json` and the privilege the same way.

**Admin map layer**: a world map tab, "Admin overlays" (`Admin/AdminMap.cs`), drawn on the client
from what the server sends over the `seraphhorizons-admin` channel to players with
`controlserver` who switched an overlay on, refreshed every 30 s. A minimal layer: outlined cells and
rings, small filled squares for deposits (coloured by metal; smaller once sold, grey once sold out),
camps and supply, with their label under the mouse. The tab shows for everyone; only admins are sent
anything to draw.

Tests: `tests/Admin/` (the JSON answer, file names, the state export round trip with a fake section,
the channel log, the overlay format), `tests/Ore/OreTallyTests.cs`, `RegistryReplaceTests.cs`,
`tests/Trading/Values/ValueChecksTests.cs`, `tests/Trading/Economy/SupplyReplaceTests.cs`;
`tests/PackTests/OreAdminScenarios.cs` and `TradingAdminScenarios.cs` (Atlas: every subcommand
answers in text and JSON, privileges, `count` finding a verified deposit's ore, the registry and
state exports reading back, a live survey's files, the overlays reaching admins only, the logs).

## Tests

`tests/` (xunit, no game): Tidy Variants' rule engine and the shipped override and lang files,
cart reach's entity matching and reach rule, the NaN motion diagnostics' first-NaN tracker, frame
ring and report formatting (`NanMotion/Core/`, `tests/NanMotion/`), which panning drops are taken out, where the chopper drops its piles, which ratio a source next to a gearbox takes, `/clear`'s
daytime, dry-spell search and saved lock, and unified woodworking's rules: splitting block tiers,
upgrades and yields, the creative shortcut and the frames' stages, sawhorse work, the handbook's page list (and that the guides the export hides
are what it drops) and the lang entry changes (`Core/`), Map Reveal's `Core/`, the creative mod
tabs' plan, domain owners, state file and mod mode's tab layout (`CreativeModTabs/Core/`), the
machines' shared rig maths, footprint, trunk path and trunk box, held to the driver fixture every
implementation replays (`Machines/Core/`, `tests/Machines/`), the bucking sawmill's rig, assembly
rules, cut arithmetic, cycle and animation (`BuckingSawmill/Core/`, described in
`BuckingSawmill/README.md`), the rosser's rig, parts, pace, trip, water and client-side values
(`Rosser/Core/`, described in `Rosser/README.md`), machine oil's tank, drain, oil codes and settings
(`Machines/Core/MachineOil.cs`, `tests/Machines/MachineOilTests.cs`), the trunk code and variant rules of the
debarked trunk (`Core/TrunkVariants.cs`, `TrunkVariantsTests`), the item value table's lookup
and family fallback, that its wildcard cache answers as the uncached scan does, and that the shipped table parses (`Trading/Values/Core/`, `tests/Trading/Values/`),
the trunk entities' settings, weights, carry speeds, spud holds and boxes (`TrunkEntities/Core/`,
described in `TrunkEntities/README.md`), the pack version check's comparison with the
embedded lock (`PackCheck/Core/`, `tests/PackCheck/`), the config defaults' JSON and YAML readers,
reconciler and run, held to the shipped snapshots (`ConfigDefaults/Core/`, `tests/ConfigDefaults/`), gear reclamation's roll, flash rust hours, settings and
optional ingredients, held to the shipped gear item types, degreasing recipes and lang entries
(`GearReclamation/Core/`, `tests/GearReclamation/`), the gear cutter's build order, take-back,
drops, cut arithmetic, kit wear by oil, settings and rig (`GearCutter/Core/`,
`tests/GearCutter/GearCutterGameplayTests.cs`), the draw bench's build order, die metal, take-back,
drops, draw arithmetic, die wear, renderer clock, settings and rig (`DrawBench/Core/`,
`tests/DrawBench/DrawBenchGameplayTests.cs`, `DrawBenchRigTests.cs`), the press brake's build order,
take-back, plates by metal, fold arithmetic (W only while held, two sections at 1), lever holds, renderer
clock, settings and rig (`PressBrake/Core/`, `tests/PressBrake/PressBrakeGameplayTests.cs`,
`PressBrakeRigTests.cs`), the mandrel station's mandrel, take-back, hollows by metal,
blows (two sections at the last), renderer clock, settings and rig (`MandrelStation/Core/`,
`tests/MandrelStation/MandrelStationGameplayTests.cs`, `MandrelStationRigTests.cs`), the handcar's drive (pumping by
facing, solo and pair, braking and holding, load), branch selector, distance rolled, stroke phase, effort fade,
easing, settings and rig, held to its entity type and rider reference (`Handcar/Core/`, `tests/Handcar/`), the pickling tub's rules, timings,
over-pickling order, early take-out, brine loss and settings (`PicklingTub/Core/`, `tests/PicklingTub/`),
and the unified pipes' burst figures, lead rule, recipe cost filter and the guard on ppex's assets, held
to the shipped patch and recipes (`Pipes/Core/`, `tests/Pipes/UnifiedPipesTests.cs`), the guard on the
game's chute section, its anvil recipe and the chute recipes (held to copies of 1.22.7's, the item and
chutes trimmed, and to the shipped patch applied to them) and the two-angle soldering recipe
(`Pipes/Core/ChuteSections.cs`, `tests/Pipes/ChuteSectionsTests.cs`), the angle's item, L shape and
anvil recipe, the pipe section's item and tube shape, every pipe recipe from pipe sections and the
chain's figures (`Pipes/Core/PipeSections.cs`, `tests/Pipes/PipeSectionsTests.cs`), and the cast pipes'
guard on smex's two mold files (held to trimmed copies of smex 0.10.1's, and to the shipped patch
applied to them) and the chain's two pipe sections an ingot, held to the shipped patch
(`Pipes/Core/CastPipeMold.cs`, `tests/Pipes/CastPipeMoldTests.cs`), and the hand pump on ppex pipes:
its spring search, priming count, the intake's scan and allowance and the name fallbacks
(`Pipes/Core/HandPumpSearch.cs`, `tests/Pipes/HandPumpSearchTests.cs`), and that every block and
item drawn with one of the pack's own shapes sits centred held, dropped and in a slot, from its
transforms and the shape's bounds: held, the game keeps a transform's origin where it is instead of
moving it to the hand, so a machine's item form, centred on a footprint many blocks long, needs a
translation of (spot by the hand - origin) / scale to bring it back (`tests/HeldTransformTests.cs`).
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

`tests/PackTests/SeraphHorizonsModScenarios.cs` (Atlas) places a Cornish boiler, calls `Explode()`
with the lid shut and with it open, and requires the boiler still standing with its lid open. It
requires `BlowSound` to be ExpandedLib's `ExSounds.MediumExplosion` and present in the assets. It
also requires every `LangEdits` passage reworded, and an edit set for every language ppex ships:
when either fails after a ppex update, match the edits to ppex's new text or add the new language.
The same goes for `ChimneyVentText.LangEdits`, whose passages must also quote ppex's look-at line
for a venting chimney, and whose English one must say "no draught" while ppex's choked boiler does.
`WellShaftText.LangEdits` must be reworded too, quote Hydrate or Diedrate's look-at line for an
empty well, and give the depths and liters of its default settings: when that fails after a Hydrate
or Diedrate update, match the edits to its new text or settings. A second scenario stands wells in
the air and requires what the page says of them: a one-block shaft holds 5 levels in rock, fireclay
bricks, uneven bricks and aged ashlar, 7 in bricks and 10 in ashlar; one rock block in an ashlar
shaft caps it at 5 from the third level and at 7 from the eighth; and four springs under a 2x2 shaft
hold nothing. When it fails, the rules changed: reword the edits.

It also reads the patched `game:worldgen/structures.json` and requires each battle tower at the
chance and spacing in `battletowers-rates.json` (`RarerBattleTowers`): when that fails after a
Battle Towers update, match `BattleTowers.PatchAsset` and the rates' codes to its new patch file.
With the switch off, `SwitchesOffScenarios` requires the three towers as Battle Towers 1.1.0 ships
them. `BattleTowerRatesTests` (no game) parses the shipped rates and rewrites a trimmed copy of
Battle Towers' patch file.

The same class places the creative steam source against a closed iron pipe and requires the pipe
full of steam at the set pressure, and no higher; it also requires the block in the creative
inventory with no drops and no recipe. With the switch off, `SwitchesOffScenarios` requires the
block not to exist. Run against a ppex older than 0.7.1, the steam scenarios require the block left
out instead.

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

`tests/PackTests/CarryOnIconsScenarios.cs` (Atlas) requires the pinned Carry On's static fields that
can hold an item stack or a collectible to be exactly the seven `CarryOnIcons` clears (a Carry On
update that adds, renames or retypes one fails it), and `CarryOnIcons.Find` to find all seven. It
then runs Carry On's block interaction help on the server's world for a chest (initialising the help
builder as Carry On's client does, and putting it back): the stacks are built once and kept, cleared
by `CarryOnIcons.Clear`, and built again from the world's items. That a client leaving one world and
opening another no longer crashes is checked by hand in the game, since Atlas runs no client.

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

`tests/PackTests/GearboxSourceRatioScenarios.cs` (Atlas) builds a creative rotor, MPE Gearbox's 1:5
gearbox and a wooden toggle (what drives a helve hammer) in a row, each connected as its own
placement code connects it, and requires every block's stored geared ratio: rotor 1, gearbox 1,
toggle 5 with the rotor on the low side, built rotor first (the order the game already handles)
and gearbox and toggle first; rotor 1, gearbox and toggle 0.2 with the rotor on the high side; and
the low-side row still right after the toggle is broken (the network rebuilt from its rotor) and
placed again. Without the patch the rotor stores 5, 0.2 and 5 in the three cases that discover
through the gearbox. With the switch off, `SwitchesOffScenarios` requires nothing patched and the
rotor placed last on the low side at 5, as MPE Gearbox ships it; when that fails with the rotor at
1, MPE Gearbox has fixed it and the tweak can go.

`tests/PackTests/SteelBitsScenarios.cs` (Atlas) is the stone coffin's fragility guard:
`BlockEntityStoneCoffin.AddIngot(ItemSlot)` resolves, returns `bool`, and carries the prefix and
postfix, and the private fields the scenarios read are there; when it fails after a game update,
find the method that now puts an ingot in and point `SteelBitsSystem.Start` at it. It requires the
steel bit to melt above coke's burn temperature at 20 to an ingot, packed steel bits to carburize
to one blister steel ingot and their recipe to take one slot of 20 bits, and the handbook section
on the steel bit alone. It builds a cementation furnace from the game's own multiblock description,
fills it by sneak-clicking its second half: 19 bits refused and kept, 20 taken from a stack of 50,
an iron ingot refused after them, a packed charge taken, then the rest to 16 places and 5 coal
layers, more bits refused when full; puts the lids on, lights a coal pile of coke under each half,
waits for heat and progress, skips the 160 hours and requires 16 blister steel ingots and 32 coke
left. With smex loaded, its live scrap list must name the steel bit, its converter's own `IsScrap`
take a steel bit (not a steel ingot), a bit be worth 5 units to a 100 unit ingot, and a list set to
iron only get the steel bit back from `SmexScrap.Ensure` (the setting put back after); without smex
it passes saying so. With the switch off, `SwitchesOffScenarios` requires nothing patched, no
recipe or section, smex's setting untouched, the packed item still there, and a coffin that keeps
40 held bits and takes an iron ingot.

`tests/PackTests/MachineOilScenarios.cs` (Atlas) is the fragility guard and the game's machines:
every patch target in `ForeignMachines.Targets` (27: the helve hammer's and pulverizer's 13 and
Immersive Woodworking's 14) resolves against the locked versions and carries its prefix or postfix,
Immersive Woodworking's machines are bound, and the default oils exist in the pack. When it fails
after a game or Immersive Woodworking update, the target it names was renamed or changed: find the
new method that does that job and point `ForeignMachines.Bind` at it. A pulverizer must be built
dry at three times the game's 0.085, take four lumps of tallow (200 points, back to 0.085), take
800 of a bucket's 1000 items of flax oil and leave the rest in the bucket, refuse more when full
(the click still the oil's), save the tank in its tree, drain by `DrainPerJob` for each item
`Crush` takes and run dry, and be dry again when broken and placed again. A helve hammer next to
a wooden toggle must load the toggle at three times 0.125 once it has a head, and 0.125 when oiled.
`MachineOilMillScenarios.cs` (`WoodworkingScenarios`) builds a bucking sawmill: dry at three
times its `Resistance`, oiled by a bucket on its power cell, and a four-log trunk's cut costing
`OilDrain.PerTrunk`. The mill's and the rosser's assembly scenarios require three times their
`Resistance` once assembled and their own once oiled, and every other mill and rosser scenario fills
the tank as it assembles (a creative rotor at its default settings cannot turn a dry mill).
`ItemExportScenarios` requires the handbook page in the export. With the
switch off, `SwitchesOffScenarios` requires nothing patched, a pulverizer at 0.085 with no tank
and refusing tallow, and the page among the hidden guides. The strike detection, the smoke and
the client's side of the click need a client and are checked by hand in the game.

`tests/PackTests/GearReclamationScenarios.cs` (Atlas, `SharedWorldScenarios`) requires the five
gears on the rusty gear's shape with the item class, ground storage, shelf and display case, a
name and the general tab, every texture they name present (under a full install), the large steel
gear on the temporal gear's shape, and the rusty gear's currency value kept with the salvage
section added. Flash rust: the pickled and neutralized gears' Perish hours are the setting's, half
the fresh time leaves a stack as it was, and past fresh and rusting it is a stack of the same number
of rusty gears. The pot: three degreasing recipes with every alkali of the pack resolved; 4 gears
and 1 litre of lye in one slot, 2 + 2 with washing soda, and 3 × 2 with caustic soda match their
recipe at the right servings and `DoSmelt` leaves that many degreased gears and a dirty pot; the
wrong amount of lye is the pot's recipe error. The barrels: 20 pickled gears in 3 litres of lime
water are not done half an hour short of 2 hours sealed and are 20 neutralized gears with a litre
left at 2; those in lard are oiled at 4 hours, hardened lard too. Taking the 20 oiled gears from the
barrel into a hand (`TryPutInto`) leaves, a few ticks later, only steel gears and bits that add up
to 20. 40 stacks of 64 oiled gears put in a hotbar slot come out within four standard deviations of
one in ten sound, with one bit for every other gear; a stack written into a slot without the hook
is resolved by `ResolveAll` (as on login) and by the sweep. `The_gear_recipes_export` requires the
three steps' recipes in the export. With the switch off, `SwitchesOffScenarios` requires the items
still there, none of the recipes, no salvage section, and an oiled gear in a hand left oiled. The
handbook pages, the item colours and the shelf and ground piles need a client and are checked by hand.

`tests/PackTests/HydrationCoverageScenarios.cs` (Atlas) requires a `hydration` attribute on every
food the server loads: anything eaten, used as a meal ingredient or drunk. An explicit 0 counts. When
it fails after a mod is added or updated, it lists the foods to give a value in `patches/hydration-*.json`.

`tests/PackTests/BuckingSawmillScenarios.cs` (Atlas) requires every blade kit at three times
Immersive Woodworking's durability (`DurableSawmillBlades`); with the switch off,
`SwitchesOffScenarios` requires Immersive Woodworking's own. The rest of its scenarios are the
bucking sawmill's (`BuckingSawmill/README.md`).

`tests/PackTests/SupportChainsScenarios.cs` (Atlas) requires one grid recipe from Better Ruins'
mechanical blueprint for each of the four support chains, giving 4 (`FewerSupportChains`); with the
switch off, `SwitchesOffScenarios` requires each giving 64. When it fails after a Better Ruins
update, check the file's order (the patch addresses `/26` to `/29`).

`tests/PackTests/RosserScenarios.cs` (Atlas) is the rosser's (`Rosser/README.md`, "Tests"): loading,
placing and breaking on every facing; assembly, and the creative shortcut fitting one stage per
click; the power face on every facing and the shaft against a vanilla axle; a ppex pipe watering the
drip; whole trips of thin and thick, branchy and clean trunks; spent heads, and the trunk that spent
them still leaving by rack or mill; racks at both ends on every facing, and both rack switches off
(nothing taken; the trunk waits for a hand and the info says so); breaking mid-trip; the trunk's
boxes; a mill in line placed by a player on every facing, a mill in line winning over a rack except
while the mills' `AutoPullFromRack` is off; unloading mid-trip and mid hand-off (the world saved
first with `/autosavenow`, as an unsaved chunk column comes back as generated); and one scenario that
measures Logging Expanded's branch counts on felled trees (about 2.45 per log; the typical trunks
the pace is set on use it). `tests/PackTests/MillFeederScenarios.cs`
(Atlas) tests the mill's side of `ITrunkFeeder` with a stub feeder and a stub ghost, block entity
classes of the test assembly registered at run time: found in each of the three infeed cells on every
facing, directly and through a ghost, ignored when it faces another way or the cell is not its
outfeed cell, never taken from by an unassembled mill, the block info for each state, and a running
mill taking a finished debarked trunk once, at the top of its cycle, and cutting it into debarked
logs. `tests/PackTests/DebarkedTrunkScenarios.cs` (Atlas) requires a debarked trunk for each of the
1320 clean ones with the clean one's shape, rotation, behaviours, multiblock size, held animation
and Cartwright's attribute, its name and no creative tab; the patched asset's state, shape and
textures (bark faces and ends), each texture file present (the server has no block textures to read); `Trunks.Debark`
keeping wood, size, side and logs and dropping the branch count; the Trunk Storage Rack storing and
returning it, mark and all; each sawhorse giving debarked logs with no bark and unloading it
debarked; and the bucking mill cutting it into debarked logs. Trunk entities run in that world, so
its trunks reach the stations through Carry On's hands and come back into them; a debarked trunk on
the ground is a trunk entity, worked in `TrunkToolScenarios` (below). With the switch off, `SwitchesOffScenarios` requires no debarked trunk, no
rosser blocks or recipe, and nothing logged about either. When these fail after a Logging Expanded
update, `DebarkedTrunks.Bind`'s warning names what changed in its trunk blocktype or members.

`tests/PackTests/TrunkEntityScenarios.cs`, `TrunkToolScenarios.cs`, `TrunkCarryScenarios.cs` and
`TrunkStationScenarios.cs` (Atlas) are the trunk entities' (`TrunkEntities/README.md`, "Tests"): a
spawned trunk item becoming a thin or thick trunk entity, no trunk given to a player, the weight by
logs, a trunk at rest on the ground, a placed trunk multiblock removed as it loads, a trunk left in
a hotbar laid down as an entity rather than placed, and the drive (W, S, A and D, a light trunk
twice as fast as a 48-log one, up a step, faster afloat), kept from another player and cleared when
a trunk saved with it loads; each tool on a trunk entity, the
axe and saw refusing a branched one and the spud debarking a clean one whole with its bark, a thick
trunk cut down to lg becoming a thin one; carrying through the pinned Carry On (the animation by
size, racks not carryable, an item in either hand refusing a trunk, the speed by logs, sneak to
shoulder, put-down and a drop laying a trunk entity, a cart taking a carried trunk and giving it
back to the hands); and the rosser and mill pulling a trunk entity from their infeed cells, Ctrl
into the hands, and the sawhorse, Trunk Storage Rack and heating rack loading from the hands and
unloading into them. When they fail after a Logging Expanded or Carry On update, the warning in the
log names what the bridge no longer finds. With the switch off, `SwitchesOffScenarios` requires
Logging Expanded's trunks, rack and Cartwright's carts as the mods ship them, no tool behaviour and
no patch of the feature, and a trunk entity turning back into its trunk item.

`tests/PackTests/WoodworkingMachineCostsScenarios.cs` (Atlas) requires each of the eight parts'
one recipe to take the nails and strips, plates and rods of `WoodworkingMachineCosts.Parts`, all
limited to iron, meteoric iron and steel; the saw sash to match on the grid with iron work and not
with bronze, copper or 3 nails and strips to a slot; and the Machines chapter to give the totals of
`Parts`. `RecipeExportScenarios` requires the recipe browser's export to carry the sash's pattern,
quantities and metals, and `ItemExportScenarios` its Machines guide to give the totals. With the
switch off, `SwitchesOffScenarios` requires Immersive Woodworking's own counts, of any metal, and
the chapter without the paragraph. When these fail after an Immersive Woodworking update, compare
its `recipes/grid/sawmill_*.json` and `chopper_*.json` with the patch.

`tests/PackTests/FellingWearScenarios.cs` (Atlas, in `WoodworkingScenarios`) grows the game's trees
in the sky (its English oak, its redwood for a thick one) and fells each as the server does, Logging
Expanded's felling listener and then the axe's own break: an oak costs the axe `ThinTree`, a redwood
`ThickTree`, both felled whole with every log in trunks; a felling without the listener (no trunk)
costs the game's one per log and leaves no trunk; and an axe with less durability than the cost
fells the whole tree, its logs all in trunks, and is gone after. `tests/FellingWearTests.cs`
(xunit, no game) covers the thick rule, the cost and the settings' range. With the switch off,
`SwitchesOffScenarios` requires nothing patched and a felling that leaves a trunk to cost one per
log.

`tests/PackTests/RuinSurfaceMedianScenarios.cs` (Atlas, in `SharedWorldScenarios`) runs the
game's own `TryGenerateRuinAtSurface`, patched, for a synthetic 20 by 20 schematic over a block
accessor whose terrain is 3 lower at the start column only, and whose first liquid check, right
after the seating, answers water so the call stops there: the ruin is seated on the higher ground
(the median), and with the switch off (`SwitchesOffScenarios`, nothing patched) on the start
column's (the lowest). When the first fails with nothing patched, the game's method changed shape:
the warning in the log says which part of `RuinSurfaceMedian.Rewrite`'s pattern is missing.
`tests/RuinSurfaceHeightTests.cs` (xunit, no game) covers the median, its tie rule and its range.

`tests/PackTests/GearBlankScenarios.cs` (Atlas) requires both blanks to stack, be ground storable
and be named; both molds to be clay-formed from each clay (two and four layers) and fire, in a pit
kiln and in the beehive kiln's four levels, to fired molds of the same kind; a fired mold placed in
the world to refuse copper, take steel 100 units at a time as a crucible pours it, give nothing while
hot, and give the blank to a player's empty hand once hardened, through the block's own right-click;
Steelmaking Expanded's `MoldKinds.FitsPedestal` to take both molds on its canal pedestal (and not the
game's anvil mold); the two smithing recipes to take steel and not iron, at 36 and 84 voxels, named `plate`; the helve
hammer, hit by hit on a steel anvil, to finish the small blank from one ingot and the large one from
two but not from one; and the export to carry both smithing recipes, both clay-forming recipes, the
raw molds' firing and the blanks' handbook text. With the switch off, `SwitchesOffScenarios`
requires no blank, no mold and no recipe for either.

`tests/PackTests/CastPipesScenarios.cs` (Atlas, the shared world) requires the system on with no
guard warning and smex's own molds at their units; the raw pipe mold clay-formed from each clay (two
layers) and fired, in a pit kiln and the beehive kiln's four levels, to a fired pipe mold, which exists
in all ten colours and is named; the fired mold's `requiredUnits` 100, its drop of two pipe sections,
smex's shared attributes kept beside the patch's (the game merges them) and its handbook section, and
`MoldKinds.FitsPedestal` taking it; the iron and steel sections there; a fired mold on the ground
refusing tin bronze, taking 100 units of iron or steel and, cooled, casting two sections of the metal;
smex's canal pedestal, driven by its own members (`AddMold`, `TryTogglePouring`, `PushMetal` into its
cell, `RemoveMold`), filling the mold to 100 units of iron and the mold set down hardened casting two
iron sections; and the grid recipe, one per metal, making one straight ppex pipe from one pipe
section, nails and strips of the same metal (not the other) and a hammer. With the switch off,
`SwitchesOffScenarios` requires no pipe mold (smex's quad rod mold still there), no recipe for one and
nothing logged about them.

`tests/PackTests/GearCutterScenarios.cs` (Atlas, the shared world) requires the gear cutter's
blocks, parts and recipes (none of its smithing recipes helve-able); placing on all four facings
with every ghost, the axle face turned and every cell cleared on breaking; the stages fitted in
order by real clicks on the frame and ghosts, a later stage's part, a third gearbox and a blank
before the master refused, and a save keeping every code; the creative shortcut fitting the ten
stages one per click; under a creative rotor, the shaft cutting, a small blank to a steel gear
dropped beyond the output face, the master changed over, the wrong size refused and a large blank
to a large gear (oil drained 10 and 20, kit worn 10 and 17); the kit's wear at full, half and a
tenth of a tank, the load the same dry, a dry gear breaking the kit and no blank going on after, and
a refit resuming a half-cut blank; Ctrl + right-click giving back the kit, the blank and the master
in turn, and breaking giving back every part, the kit with its wear and the blank; and blanks taken
from a chest at the infeed (not while the shaft stands, never the other size) and gears put in a
chest at the output. With the switch off, `SwitchesOffScenarios` requires none of its blocks, items
or recipes.

`tests/PackTests/DrawBenchScenarios.cs` (Atlas, the shared world, the gear cutter's player) requires
the draw bench's blocks, the parts every stage takes, the lead and copper hollow sections (the game's
chute section, lead the state `UnifiedPipes` adds) and pipe sections and the dies (their durability the
setting's), its recipes (the die not helve-able), the settings' pace the rig's and the
machine oil page naming it; placing on all four facings, the bench running away along the facing,
every ghost pointing home, every cell's lid, the axle face turned, and every cell cleared and the frame
dropped on breaking; the stages fitted in order by real clicks on the frame and ghosts, a later stage's
part, a second chain and a hollow before the die refused, and a save keeping every code; the creative
shortcut fitting the five stages with a steel die; under a creative rotor, the shaft drawing the first
lead pipe section by itself from a lead hollow on an iron die, a pipe section (lead or copper), a lead,
copper or iron ingot and an angle never handled, a copper hollow refused by it, the die kept in while the
hollow is on, and the four pipe sections dropped beyond the output face, the hollow used up (die 99, oil
992); the iron die taken back with its durability and a steel die drawing copper at twice the turns and
lead too, a point a hollow; a die wearing only when a hollow is done and out on its last one, the load
three times dry, no hollow without a die, a worn-out die refused and a new one drawing again; the die
back by Ctrl from a ghost, and breaking giving back every part, the die with its wear and an undrawn
hollow (not one with a pipe section off it); and hollows taken from a chest at the infeed (not while the
shaft stands, never copper on an iron die, a pipe section or an ingot, the next a moment after the last)
and pipe sections put in a chest at the output. With the switch off, `SwitchesOffScenarios` requires none of its blocks,
dies or recipes, no link to them and the machine oil page without it, and nothing logged.
`RecipeExportDrawBenchScenarios.cs` requires one `machine` record per metal (`drawbench|game:chutesection-{metal}|0`):
the hollow, the four kept stages (two chains and two rods among them), the dies that draw it as a tool worn 1 (`fixed`), 8 points of oil, the
frame, four pipe sections.

`tests/PackTests/PressBrakeScenarios.cs` (Atlas, the shared world, the gear cutter's player) requires
the press brake's blocks, the rods and plates its stages take, the lead and copper plates and open
sections, its recipe, and the settings' pace the rig's; placing on all four facings, the brake running
away along the facing, the ghost pointing home, both cells' lids, the infeed and outfeed cells, and both
cells cleared and the frame dropped on breaking; the stages fitted in order by real clicks on the frame
and the ghost, the edges before the screws, a plate before both, an empty hand on a bare frame and a
copper rod refused, a save keeping every code, Ctrl taking the edges then the screws back, and the
creative shortcut; a lead plate folded only while right-click is held (the block's own start, steps
and stop, from the ghost), nothing moving when let go, a closed chute section, an ingot, a tin plate and
an open section never taken, a plate in hand working the lever on a loaded bed, and two open sections
dropped beyond the output face, the plate used up; copper at half as many turns again; a flat plate
back by Ctrl, a half-folded one and the parts staying, and breaking giving back the parts and a flat
plate (not a half-folded one); and plates taken from a chest at the infeed only when the lever is
worked (never an ingot or an open section) and sections put in a chest at the output. With the switch
off, `SwitchesOffScenarios` requires none of its blocks or recipes, no link to it, and nothing logged.
`RecipeExportPressBrakeScenarios.cs` requires one `machine` record per metal
(`pressbrake|game:metalplate-{metal}|0`): the plate, the two kept stages, the frame, two open sections,
power `hand` at the lever's 6 or 9 turns, no wear and no oil.

`tests/PackTests/MandrelStationScenarios.cs` (Atlas, the shared world, the gear cutter's player)
requires the station's blocks, the rods it takes as its mandrel, the lead and copper hollows and pipe
sections and a hammer, its recipe, its place in the mechanics tab, and the settings' pace the rig's;
placing on all four facings, the station running away along the facing, the ghost pointing home, both
cells' lids, the infeed and outfeed cells, and both cells cleared and the frame dropped on breaking; the
mandrel fitted by a real click on the ghost, a hollow and a blow before it, a copper rod and an empty
hand refused, a second rod refused, a save keeping it, Ctrl taking it back, and the creative shortcut; a
lead hollow struck through by real right-clicks with a hammer from the stump and the ghost, a blow a
sixth with a copper hammer, an angle, a pipe section, an ingot and a plate never taken, the hammer paying
a point a blow, a save keeping the blows, and two lead pipe sections dropped beyond the tip at the sixth,
the hollow used up; copper at nine blows; a steel hammer forging lead in three blows, each two and a half
of a copper one's, and copper in four; a steel hammer with its tier taken away forging as the copper one; an unstruck hollow back by Ctrl, a struck one and the mandrel staying, and
breaking giving back the mandrel and an unstruck hollow (not a struck one); and hollows taken from a
chest beside the stump only when struck (never an ingot, an angle or a pipe section) and sections put in
a chest beyond the tip. With the switch off, `SwitchesOffScenarios` requires none of its blocks or
recipes, no link to it, and nothing logged. `RecipeExportMandrelStationScenarios.cs` requires one
`machine` record per metal (`mandrelstation|game:chutesection-{metal}|0`): the hollow, the kept mandrel,
the copper (base) hammer worn its blows (`fixed`), the frame, two pipe sections, power `hand` at 6 or 9
blows, no oil.

`tests/PackTests/HandcarScenarios.cs` (Atlas, a plain world of its own: it joins four players, and the
shared world has no slots left) requires the handcar registered with its
item, recipe, the riders' animations on the seraph and the player, and Yang's wrench patch; placed by its
item on standard-gauge track, two players mounted on the deck facing each other; one pumping to the solo
top speed and paying satiety over the other; both to the pair speed; pumping back braking and reversing;
two against each other stopping and holding; coasting by its drag; the branch selector stepped by both
riders as they face; sneak getting them off; Yang's wrench with a hammer taking it apart into its item, a
sneak click picking it up; and, in the game's own animator on the patched seraph and the car's shape,
each rider's hands on its handles at sampled frames and blends. With the switch off,
`SwitchesOffScenarios` requires no entity, item, recipe or rider animations, nothing patched, and
nothing logged.

`tests/PackTests/LocomotiveSeatsScenarios.cs` (Atlas, a part of `HandcarScenarios`, on its track)
places a standard locomotive and a handcar by their items and requires both patches in, Yang's seat
check answered no for the locomotive and still yes for the handcar (both types set
`CollisionCheckSeats`), and the breathing postfix on the game's check giving a survival player
seated in the locomotive with granite at the eye their air back, but not with water at the eye and
not once they are off (run on a lost breath: the game's own block test never loses it, above). With the
switches off, `SwitchesOffScenarios` requires nothing patched and the check answered yes for a
standard locomotive.

`tests/PackTests/UnifiedPipesScenarios.cs` (Atlas, the shared world) requires the switch bound with
nothing logged; every pipe shape in copper and lead, at the default figures with iron and steel, on
ppex's model with the metal's texture; the valves and pressure valves in the three bronzes at the
bronze figure, and the iron and steel ones unlisted; none of ppex's plate-and-nails or valve recipes
left, and this mod's resolving (every shape in all four metals from pipe sections of the metal, in
the right number, with a solder bar a section and the soldering iron or one nails and strips and a
hammer, no pipe from a chute section, and the bronze valves); the chain: the angle in copper and lead,
named, with its handbook section and forged from one ingot of its metal; the chute section in copper
and lead only, named, only copper in the mechanics tab, with its handbook section, no anvil recipe for
it and on the grid only two angles, two solder bars and the soldering iron making one; the pipe
section in all four metals, named, with its handbook section and no grid or anvil recipe; the game's
chutes taking copper chute sections only, a solder bar a section and the soldering iron and no hammer,
Better Ruins' blueprint chutes gone and its riveted block still made; exlib's `GridRecipesFor` patched and seeing only ppex's recipes; the
steam power page's figures and "water only"; and, beside a creative steam source at 2 atm, a lead pipe
bursting within seconds and dropping as itself while copper holds. With the switch off,
`SwitchesOffScenarios` requires ppex's pipes as they ship (no new states, its recipes back, its text
and figures), the chute section copper alone with its anvil and plate recipes back, chutes from
sections alone and Better Ruins' back, no angle or pipe section and nothing patched. `tools/tests/test_unified_pipes.py` holds the patch to ppex's zip, and the chute section patch
to the game's files.

`tests/PackTests/HydratePipesScenarios.cs` (Atlas, the shared world) requires Hydrate or Diedrate's
pipes, valves and pipe sections without a recipe, out of the creative inventory and the handbook, and
its hand pump made from two ppex straight pipes of its metal with its handbook section; a placed
Hydrate pipe and valve gone within ticks with nothing dropped, while a hand pump and a chest stay; a
pump over seven upright ppex pipes of mixed metals finding the spring beside the lowest, primed as
Hydrate primes for seven (2 strokes at its default), the pipe ends under the pump and against the
spring sealed, the spring lost and found again as a pipe is taken out and put back (Hydrate's network
version moving) and as a ppex valve in the run is closed and opened, a spring right under a pump still
found with no priming, and none through a run holding steam; and, over a full well, what a fluid
intake produces taken out of the spring, never more than the spring holds. When it fails after a
Hydrate or Diedrate or ppex update, check the names in `HandPumpBridge` and `HydratePipes` against the
new versions.

`tests/PackTests/HeatingRackScenarios.cs` (Atlas, `WoodworkingScenarios`) places a heating rack, takes
its stack from `OnPickBlock` and places it elsewhere through the block's own `TryPlaceBlock`, as
Carry On's client and a creative pick do: the stack must carry no `posx` and the new block entity's
`Pos` must be the new position. It also carries a rack with Carry On's server calls and breaks one
in survival. With the switch off, `SwitchesOffScenarios` requires nothing patched and the picked
stack carrying the rack's position. When it fails after a Logging Expanded update, check whether
the rack still writes its tree into the stack, and whether `FromTreeAttributes` still keeps its
position when the tree has none.

The same file holds `HeatingRackStandsOnBlock`'s scenarios. A rack placed from a stack through
`TryPlaceBlock`, with the selection the game makes for a click on the granite floor's top face (the
cell over it, `DidOffset` set), must stand one cell higher, the cell between empty and the block
entity's `Pos` the rack's, and the selection must say so; aimed at an extinct firepit's top face it
must stand right above the firepit; with the cell above taken, or aimed at a side face, it goes in
the cell over the face. Carry On's place-down (the scenario above) must report the lifted cell and
restore the block entity's tree there (a banked resin figure set before the pickup). With the switch
off, `SwitchesOffScenarios` requires nothing patched and the rack in the cell over the floor. When
they fail after a Logging Expanded update, check whether the rack gained a placement of its own
(`TryPlaceBlock` or a different `DoPlaceBlock`) or a new shape; after a Carry On update, whether
`TryPlaceDownAt` still hands the block the same `BlockSelection` whose `Position` it reports and
restores at.

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

`tests/PackTests/BloodSausageScenarios.cs` (Atlas) requires both raw items, no grid recipe making
either, and both among the outputs of A Culinary Artillery's enabled recipes, read from its
registries as the recipe exporter reads them. With the switch off, `SwitchesOffScenarios` requires
Butchering's three blood sausage and one black pudding grid recipes. When it fails after a Butchering
update, check the two grid files' order and whether its kneading recipes are still enabled with
Expanded Foods.

`tests/PackTests/DuplicateRecipesScenarios.cs` (Atlas) requires no enabled kneading recipe
from Expanded Foods' two sausage files, every one making its meat or fish sausages to take clean
offal, and one to make each; no grid recipe for the scrap brazier taking fewer than 10 firewood, and HQZ Lights' still
there; no Material Needs grid recipe for any aged roofing piece, the aged oar and raft or the very
aged iron round shield, and the game's for each; no barrel recipe making cottage cheese and the mixing bowl's making it; and yellow
daub from sandstone giving 12, and only that. With the switch off, `SwitchesOffScenarios` requires
each duplicate back. When it fails after an update, check the patched files' order, whether
Butchering still enables its kneading sausages with Expanded Foods, and whether the game's or
GeoAddons' daub file changed.

For panning, `SeraphHorizonsModScenarios` reads every block's `panningDrops` on the loaded server
and requires none of the removed codes in any list, nor in the pan's table as `BlockPan` reads it
(`PanningDrop`s), with Tailor's Delight's twine and needles and Expanded Matter's fluorite still
there. It also requires the buttons text reworded in every language Tailor's Delight ships it in.
When it fails after a mod update, check whether a mod adds one of them another way, or whether one
stopped adding its own (then its pattern can go). With the switch off, `SwitchesOffScenarios`
requires all four groups in the pan's table and the text as it ships.

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

For the same reason the other scenario files above are not classes of their own but parts of a
few partial classes, one server each: `SharedWorldScenarios` (`SharedWorldScenarios.cs`), every
feature that needs only the plain world; `TradingScenarios` (`TradingScenarios.cs`), the trading
features on the plain world, sharing its players; `WoodworkingScenarios`
(`WoodworkingScenarios.cs`), the woodworking chain with the machines' fixture
(`fixtures/buckingsawmill`, which shortens the mill's cut and cycle and the rosser's trip), and
`WoodworkingRosserScenarios` (`WoodworkingRosserScenarios.cs`), the rosser and the debarked trunk
on the same fixture, a class of its own only because its real-time trips would otherwise make the
one class's CI shard too long. Only a different world (a play style, ModConfig fixtures, a seeded
standard world) gets a class of its own, as `/clear`'s, the off checks and the ore and trading
camp scenarios do. Their doc
comments say what sharing a world asks of a scenario: its own build sites and player names, and
nothing changed world-wide.

The test project loads this directory's build as a mod, and leaves out any other copy
(`seraphhorizons_*.zip` in `build/mods`).

`tests/PackTests/OreCellsScenarios.cs` (Atlas, a new standard world with a fixed seed) requires
the four ore switches read and recorded in the savegame, IOG's `TryApproveOreSpawnSeed` patched,
only the first copper try from the active spot's chunk approved (called on IOG's own generators),
the spawn cell's copper, iron and tin anchors resolving as they generate, with the ore in the
column of one that holds a deposit,
`/sh ore cell` answering with the spots `OreCells` computes for that seed and `/sh ore here`
listing every managed metal, no surface copper or cassiterite tried and no other generator trying
a managed metal, IOG's native copper and hematite veins scaled and a gem's not, and the
hydrothermal districts' 7 km tiles. Generating enough ore to measure deposits is too slow for
Atlas: sizes and spacing are checked with the survey tool (#458). `SwitchesOffScenarios` requires
a world created with them off to have none of it.

## Adding a tweak

Add a class next to `BoilerLidRelief.cs`, a `bool` setting for it in `SeraphHorizonsConfig`, and
the call in `SeraphHorizonsSystem` behind that setting. Then add scenarios, in a new partial file
of `SharedWorldScenarios` (`WoodworkingScenarios` for woodworking, `WoodworkingRosserScenarios` for
the rosser, `TradingScenarios` for trading), and a section above. Its
off check goes in `SwitchesOffScenarios`, with its key in `fixtures/switches-off`, not in a class of
its own, unless what it requires needs another switch on. A
tweak big enough for mod systems of its own gets a folder, as `TidyVariants/` does; its systems
read their switch with `SeraphHorizonsSystem.ConfigFor(api)`.

## Releasing

With the pack: one release, one version. Bump `modinfo.json`, `SeraphHorizons.csproj` and
`pack/pack.toml`'s `[pack] version` together (`tools/tests/test_mods_src.py` requires the three
equal), run `packtool lock` and cog (`CLAUDE.md`), write the new version's config defaults
snapshot (`packtool smoke --config-defaults write`, docs/config-defaults.md; CI requires it, and
keeps the released ones as they are), merge, and tag `v<version>` on main.
`.github/workflows/release.yml` builds `seraphhorizons_<version>.zip` from the tag and publishes it
in the pack's release, whose `.cairn` file installs it from there by its sha256 and whose server
bundle carries it. The mod is never pinned in `pack/pack.toml`, and `mod-release.yml` (the other
`mods-src/` mods' way) refuses a `seraphhorizons-v*` tag.

Uploading the zip from the release to the ModDB (keep the file name) is optional, for people who
find mods there; the release never waits on it.

Between releases, every push to main that passes CI republishes the pack's rolling
[`next`](https://github.com/benjamin-kirkbride/Seraph-Horizons/releases/tag/next)
pre-release (`.github/workflows/next.yml`), which carries the zip CI's cairn job built from that
commit, named after it (`seraphhorizons_next_<sha7>.zip`, so that Cairn, which sees a new
address but not a new hash, fetches every build; like every file on `next`, its name says `next`
where a versioned release's says the version) and listed in its `SHA256SUMS`; only the current
build's zip is on the release. The release's Cairn pack, published in the same step,
installs the mod from there by its sha256, so `next` plays with the mod as it is on main before
any of it is released. (The mod's own rolling release, `seraphhorizons-next`, is retired.) The zip keeps `modinfo.json`'s version, so it is not newer
than the release of that version as far as the game is concerned: swap it in for that copy,
don't add it next to one.

For a local build: `dotnet build mods-src/seraphhorizons -c Release` (needs `VINTAGE_STORY`) writes
`build/seraphhorizons_<version>.zip`: the DLL, `modinfo.json` and `CREDITS.md` at the top level, and
`assets/`.
