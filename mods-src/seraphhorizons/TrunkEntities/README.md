# Trunk entities

Part of the Seraph Horizons mod (`../README.md`), switched by `TrunkEntities` in
`ModConfig/seraphhorizons.json` (on by default), with its figures in `TrunkEntitiesSettings`.
Logging Expanded's (`loggingmod` 0.3.6) tree trunks stop being items you pocket. A felled tree
leaves a **trunk entity** lying on the ground, which you drag by hand or with a rope, shove by
walking into it, float down a river, work with tools where it lies, or shoulder very slowly through
Carry On (`carryon`) to load a station, a rack or a cart. A trunk is never in an inventory: the
only "slot" that holds one is Carry On's hands.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, `Rosser/`, `BuckingSawmill/` or `Woodworking/`,
which are this folder's neighbours. `Core/` is the game-independent part (settings, weights and
boxes, unit-tested without the game), `Game/` the entity, its renderer, the mod systems and the
patches. No other mod is referenced at build time: Logging Expanded is read through
`Machines/Game/LoggingBridge.cs`, Carry On and the stations are found by name and patched with
Harmony.

**What it needs.** Logging Expanded. With the switch off, Logging Expanded missing, or
`LoggingBridge` not resolving (Logging Expanded not as expected, one warning), nothing below
happens and Logging Expanded's trunks are items again, as it ships them. Carry On is optional:
without it there is one warning ("trunks cannot be carried: drag or rope them") and everything that
goes through hands is gone. A trunk can then be dragged, roped, shoved, floated and worked with
tools, and fed to a rosser or a bucking mill by dragging it into their infeed cells, but Logging
Expanded's sawhorses, Trunk Storage Rack and heating rack cannot be loaded at all (no trunk is in a
hand or an inventory to load from), carts and sleds take none, and Ctrl + right-click on a rosser
or mill lays the trunk on the ground beyond its infeed end. The bark spud on a trunk needs the
debarked trunk (the `Rosser` switch, `../Rosser/README.md`); without it the spud does nothing to a
trunk. Its bark needs Immersive Woodworking (`immersivewoodworking`); without it (or with its bark
roll not as expected, one warning) the spud still debarks, with no bark.

## The entity

Two entity types, `seraphhorizons:trunk-thin` and `seraphhorizons:trunk-thick`
(`assets/seraphhorizons/entities/trunk-*.json`), both of class `seraphhorizons.EntityTrunk`
(`Game/EntityTrunk.cs`). They are the two display classes the machines already draw trunks in
(`Machines/Core/TrunkBox.cs`): Logging Expanded's sizes xs, sm, md and lg (up to 24 logs) are thin,
shown as its `lg` model, 1 × 1 × 4 blocks; xl and xxl (25 logs and up) are thick, shown as `xxl`,
2 × 2 × 5. The type is picked from the stack's `size` variant when the entity is spawned
(`TrunkSpawns.Spawn`), and the type decides the boxes.

| | Thin | Thick |
|---|---|---|
| Sizes | xs, sm, md, lg | xl, xxl |
| Shown as | `lg`, 1 × 1 × 4 | `xxl`, 2 × 2 × 5 |
| Collision (`passivephysicsmultibox`) | four 1 × 1 × 1 boxes along z, x −0.5..0.5, z −2..2 | five 2 × 2 × 1 boxes, x −1..1, z −2.5..2.5 |
| `hitboxSize` | 1 × 1 | 2 × 2 |
| Floats with | half its height (0.5) under | half its height (1) under |

**Payload.** The trunk's own item stack (`loggingmod:treetrunk-{wood}-{size}-{branches}-{side}`,
its logs in `slots/0`, `branchCount`, resin and char in its attributes) is the entity's watched
attribute `trunk`, so it syncs to clients and saves with the entity, and whatever takes the trunk
takes that stack unchanged. `SetTrunk` rewrites it; a stack with no logs (or none) removes the
entity, nothing dropped. An entity that loads with no trunk or no logs removes itself.

**Behaviours** (both sides unless noted): `repulseagents` with `movable: true` (walking into a trunk
nudges it; the entity counts as a creature, `IsCreature`, so players' shoving finds it, as the
game's boat is found), `passivephysicsmultibox` (gravity factor 1, ground drag 1, falling air drag
0.5), `ropetieable`, and on the client `interpolateposition`. With Carry On the server's list also
gets the pack's pick-up behaviour (`seraphhorizons.trunkcarry`, below). The game's multi-box physics
moves each box's middle round with the yaw but keeps the box itself axis-aligned, as for the raft,
so a thin trunk lying at 45° collides as a staircase of four cubes. No despawn, no decay.

**Buoyancy.** `MaterialDensity` 700 (water is 1000) and `SwimmingOffsetY` half the display class's
height, so a trunk floats about half under and the game's passive physics moves it with flowing
water as it moves items.

**Weight.** 10 + 8 per stored log (`WeightPerLog`, `Core/TrunkWeight.cs`): a 4-log trunk weighs 42,
a 10-log one 90 (about a cart), a 48-log one 394. The game's rope pull on an entity
(`ClothPoint.update`) is scaled by `clamp(50 / weight, 0.1, 2)` once the rope is taut, so weight is
what makes a big trunk follow slowly: about 1.2 at 4 logs, 0.56 at 10, 0.24 at 25, 0.13 at 48, and
the floor of 0.1 only past 61 logs, so every trunk Logging Expanded makes can be dragged by one
player. `Entity.Properties` is the type's shared object, not a copy per entity, so each trunk clones
it in `Initialize` before setting its own weight; the server keeps the weight in the watched
attribute `seraphhorizons:weight`, which a client applies to its copy.

**Shown.** The client renderer (`seraphhorizons.trunk`, `Game/TrunkEntityRenderer.cs`) tessellates
Logging Expanded's block for the stack in its display size (`Trunks.ShownBlock`: thin as `lg`,
thick as `xxl`, the debarked block if the trunk is debarked, `../Rosser/README.md`), laid along z
with its underside's middle on the entity's position, turned by the yaw as the collision boxes
are. It re-tessellates when the shown block changes (debranched, debarked). The info text names
the wood, the logs, the branches while there are any, "Debarked", the weight and, while a grab
holds it, who is dragging it. The name is the stack's ("Oak Tree Trunk"); the types' own lang names
(`item-creature-trunk-thin`, `-thick`) are "Tree trunk".

**Selection.** The entity's square hitbox is only its width across, so `EntityTrunk.IntersectsRay`
picks against the collision boxes turned with the yaw (`TrunkBoxes.Turned`), each also turned to the
nearest quarter so the row stays one box wide: a trunk is picked along its whole length whichever
way it lies. A trunk entity is never collected (`CanCollect` false).

**Help.** Looking at a trunk: hold to drag (empty hand), tie a rope (with a `game:rope`), shoulder
it (Shift, empty hand, with Carry On), and each tool that would work it in its present state (the
knife while it has branches, shears from twelve, the axe and saw once they may, the spud on a clean
trunk with bark). Logging Expanded lists all four tools on a placed trunk whatever its state.

## How a trunk comes to be

**The spawn swap** (`Game/TrunkSpawns.cs`). Logging Expanded's `FellingListener.SpawnTrunk` throws
the felled tree's trunk as an ordinary item entity (`SpawnItemEntity`). The server's
`OnEntitySpawn` hook removes every item entity whose stack is a trunk with logs, and spawns a trunk
entity in its place, lying the way the item was thrown (or any way, if it was not moving), one per
stack item. So felling, a sawhorse or station unloading onto the ground, a machine broken with a
trunk on it, or anything else that drops a trunk makes a trunk entity, and the pack's own code may
keep calling `SpawnItemEntity`. Trunk items saved in a world from before are swapped as they load
(`OnEntityLoaded`, a tick later, not mid-load). A trunk item holding no logs stays an item.

**Unpocketable.** In `AssetsFinalize`, after Logging Expanded's (this system's `ExecuteOrder` is
0.15, after its 0.1, whose `TreeTrunkBackpackOnly` sets the trunks to `Backpack`), every
`loggingmod:treetrunk-*` block gets the storage flag `Custom10`, which no inventory accepts, so
`TryGiveItemstack` fails everywhere: a trunk cannot go into a hotbar, a backpack, a chest or a
ground pile. Both sides do it, so a client's blocks match the server's. Carry On's hands are not an
inventory and are unaffected. Logging Expanded's `AttachableStorageBypass`, which lets
backpack-only trunks into entity attachment slots, registers only while its `TreeTrunkBackpackOnly`
is on, so with Carry On the pack registers every trunk code with it (`TrunkCarry.RegisterCartBypass`):
the cart slots below need it either way.

## Moving a trunk

**Grab** (`Game/TrunkGrab.cs`). Right-click and hold on a trunk with an empty hand, not sneaking
(sneak is Carry On's): the server makes a game rope with no rope item, as the game's `ItemRope`
makes one, from the player's hand to the trunk, and ties it through the trunk's `ropetieable`, so
the game's own pull drags the trunk after the player. The rope is as long as the hand is from the
trunk's middle, at least the game's 1.5 blocks and at most a block short of `GrabRange`, because the
game pulls only once a rope is stretched and the grab must pull before it is out of reach.
Refused, each with an in-game error: a trunk someone else is dragging, one heavier than
`MaxGrabWeight` (0, the default, is no limit), and one further than `GrabRange` (3 blocks) from the
hand. One grab per player and one per trunk; grabbing another trunk lets go of the first. A trunk
with a rope of the game's own tied to it is not grabbed: the empty-hand click goes on to the game,
which takes that rope off. Checked every 100 ms on the server, the grab lets go (and the rope goes)
when the player releases the right button, holds anything, is further than `GrabRange` from the
trunk, dies, or leaves the game, or the trunk or its rope is gone. A grab never outlives the session:
a trunk saved while grabbed clears its grab when it loads.

**Rope.** `game:rope` ties to a trunk like to any `ropetieable` entity: to a fence post, an animal
or a cart, untouched by the pack.

**Shove.** Walking into a trunk nudges it (`repulseagents`, by its hitbox).

**Water.** Trunks float and drift (above).

## Carry On

`Game/TrunkCarry.cs`, the Carry On bridge, found by name at run time (`CarryOn.CarrySystem`'s
`CarryManager`, CarryOnLib's `CarriedBlock` and `CarrySlot`, Carry On's placement and drop
services). If any of it is not as expected there is one warning and carrying is off, as without
Carry On. The stations and machines use it through `TryGive`, `Take`, `Carried` and `HandsFull`.

- **Pick up.** Carry On's own sneak + right-click targets blocks, so the pack handles it on the
  trunk entity: sneak + right-click with an empty hand puts the trunk's stack in the player's Carry
  On hands slot and removes the entity, at once, with the trunk's place sound
  (`EntityBehaviorTrunkCarry`, server side, added to both entity types by
  `patches/trunkentities-carryon.json`). Hands already full: the error "Your hands are full. Put down
  what you are carrying first." (`trunkentities-hands-full`). The carried stack gets a small block
  entity tree (`blockCode`, `type`), because Carry On attaches a carried block to a cart only with
  block entity data.
- **Speed.** While a trunk is carried, the player's `walkspeed` stat gets the code
  `seraphhorizons:trunk`, so the walk speed is `TrunkWeight.CarrySpeed` of its logs:
  `CarrySpeedAtFourLogs` (0.25) up to 4 logs, falling linearly to `CarrySpeedAtMaxLogs` (0.02) at 48
  logs and beyond. About 0.22 at 10 logs and 0.14 at 25. Carry On's own slot modifier is set to 0 for
  trunks by the patch and cancelled out in the value besides. The server checks every online player
  every 250 ms (and at once when the pack itself gives or takes a trunk) and removes the code once
  no trunk is carried; the game syncs stats to the client.
- **Animation.** A second `Carryable` on `loggingmod:blocktypes/treetrunk`
  (`patches/trunkentities-carryon.json`), which Carry On merges into Logging Expanded's own
  (`patchPriority` 1 with `overrideExistingProperties`, so the order of the two patches does not
  matter): slot `Hands` with Logging Expanded's `trunkcarry` animation for xs, sm, md and lg and
  `trunkcarryheavy` for xl and xxl (its player patches add both; nothing of its own starts them),
  Carry On's `carry-trunk` transform (on the shoulder), and `walkSpeedModifier` 0. The game merges
  `propertiesByType` into `properties` with arrays concatenated, so the shared settings sit in
  `properties` only.
- **Put down.** Carry On's place-down of a carried trunk (`CarryPlacementService.TryPlaceDown`,
  prefixed on both sides: the client predicts, then asks) never places a block. The server checks
  Carry On's permission for the cell, then lays a trunk entity in the cell Carry On chose, along the
  player's view, reaching away from them; the hands are emptied on both sides. If the entity cannot
  be spawned the trunk stays carried, with a warning in the log.
- **Drops.** When Carry On drops a carried block (death, damage, a quick drop, its own
  carried-block entity: `CarryDropService.DropCarriedBlock` and `DropBlockAsEntityOrItem`), a trunk
  is laid as a trunk entity where the carrier stands, never a block nor an item.
- **Racks are not carried.** Logging Expanded's `patches/carryon.json` gives its Trunk Storage Rack a
  Carryable, and a carried rack takes its four trunks with it. In `AssetsFinalize` the pack strips
  Carry On's `BlockBehaviorCarryable` from every `loggingmod:trunkstorage-*` block
  (`StripRacks`): in code, after Carry On's own asset pass merges and maps Carryables, rather than a
  JSON patch racing Logging Expanded's. The heating rack and stick storage keep theirs.
- **Carts and sleds.** Cartwright's Caravan ships Carry On's `attachablecarryable` on its cart and
  sled disabled; `patches/trunkentities-carts.json` adds it to both entity types' server and client
  behaviour lists, so a carried trunk goes into a cart's or sled's storage slot by Carry On's attach.
  How it looks there is Logging Expanded's existing patch (the `treetrunk-xs-cart` shape per slot).
  Taking it off by Carry On's own key puts it in the hands; so does the game's empty-hand take from
  an attachment slot (`EntityBehaviorAttachable.TryRemoveAttachment`, prefixed: a trunk fits no
  inventory), refused with the hands-full error while they are full and for a cart someone else
  owns. If that method is gone, one warning, and only Carry On's key takes a trunk off. Carry On's
  cart leftovers on the stack (`backpack`, `carryonbackup`, an empty `type`) are removed whenever a
  trunk leaves the hands.
- **Clicks while carrying.** Carry On lets a right-click through to a block while something is
  carried only if the block has its `CarryableInteract` behaviour, after its short hold (0.8 s by
  default, unless Carry On's `RemoveInteractDelayWhileCarrying` is on), and sends it on to the server.
  `patches/trunkentities-stations.json` adds `CarryableInteract`, for a carried `BlockTreeTrunk` only,
  to Logging Expanded's three sawhorses, Trunk Storage Rack and heating rack, and to the rosser's
  and bucking mill's frame and ghosts. Without it a carried trunk could not reach a station.

## Tools where it lies

Every knife, shears, axe and saw (by its tool type) and Immersive Woodworking's bark spud (by its
code, as `Woodworking/BarkDrops.cs` knows it) gets a `TrunkToolBehavior` in `AssetsFinalize`, on
both sides (`Game/TrunkToolsSystem.cs`). Some tools' classes override the held interaction without
calling their behaviours (the game's knife does in its steps), so each such override on those
classes is prefixed to run the behaviour first; the server log says how many tools and overrides.

A right-click hold on a trunk entity with one of them is taken at its start, steps until its time,
and does the work when it is let go at that time (less Logging Expanded's 0.1 s); let go early,
cancelled, or looked away from on the client, it does nothing. A refused hold says why (Logging
Expanded's `treetrunk-branches-first`, or `rosser-error-already-debarked`) and idles until let go.
After a completed hold the client waits 300 ms before starting the next, so a held button repeats
the work at a pace, as Logging Expanded's own harvest hold does. The client shows the held item's
swing and the game's progress bar; the spud plays Immersive Woodworking's debarking animation. A
hold that does not apply (a knife on a clean trunk) is not taken and the tool keeps its other uses.

The rules are Logging Expanded's for a placed trunk (`BlockTreeTrunk.OnBlockInteractStart/Stop`,
0.3.6), its settings and hooks read through `LoggingBridge` (`Game/TrunkHarvest.cs`):

| Tool | Hold | Needs | Does |
|---|---|---|---|
| Knife | 2 s | branches | Cuts min(branches, 12): that many sticks through Logging Expanded's `StickYieldModifier` hook, times the player's `stickDropRate`; at none left the trunk becomes the clean (`no`) one. The knife loses as much as branches came off. |
| Shears | 2 s | 12 branches or more | 12 branches for the wood's `game:sapling-{wood}-free`, a second on Logging Expanded's `BonusSaplingRoll` hook; 1 durability. At none left the trunk becomes the clean one (Logging Expanded leaves a placed trunk branchy with a count of none). |
| Axe | 0.75 s | no branches while `RequireBranchRemovalForProcessing` | Takes one log off, two while it holds two or more (Logging Expanded decrements twice), for `TreeTrunkLogYield` (1) of the wood's placed log; with a hammer in the offhand `TreeTrunkDebarkYield` (1) of its debarked log. A debarked trunk gives the debarked log at the plain yield, as a placed one does under the `Rosser` switch. 1 durability. |
| Saw | 0.75 s | as the axe | One log off for `TreeTrunkPlankYield` (6) of the wood's planks (`TreeManager.GetPlankCode`); 1 durability. |
| Bark spud | logs × `SpudSecondsPerLog` (0.5), 2 s at least | no branches counted, not debarked, the debarked trunk existing | The whole trunk becomes the debarked trunk (`Trunks.Debark`, the stored logs marked as the rosser marks them). Every stored log rolls Immersive Woodworking's bark once (`BarkDrops.Roll` with the spud's chance multiplier, dry, as the rosser rolls a dry log), the drops merged by kind. The spud loses Immersive Woodworking's `DebarkDurabilityPerLog` (1) per log, and plays one of its debarking sounds. |

The spud's hold is refused with "branches first" while the trunk has branches counted, whatever
`RequireBranchRemovalForProcessing` says: the rosser's limb breaker takes branches, a spud does
not. A 4-log trunk takes 2 s, a 10-log one 5 s, a 48-log one 24 s. What a tool makes is thrown
toward the player from the trunk's nearest point, as Logging Expanded throws a placed trunk's. The
sounds are Logging Expanded's (leaves for the knife and shears, wood for the axe at 0.75 volume,
the saw's), played by the server for everyone. A trunk whose last log is taken goes.

## Stations and machines

**Logging Expanded's stations** (`Game/TrunkStations.cs`, applied by `Game/TrunkStationsSystem.cs`
on both sides while trunk entities run with Carry On, under their own Harmony id). A prefix on each
block's `OnBlockInteractStart`, found by name: the sawhorses' `BlockWorkstation`, the Trunk Storage
Rack's `BlockTrunkStorage` and the heating rack's `BlockResinRack`.

- **Loading.** Carrying a trunk, hold right-click on the station (Carry On's short hold, above): the
  station takes it if it can, with a wood sound, and the hands are emptied. A sawhorse takes it when
  empty, and debranched while `RequireBranchRemovalForProcessing` holds (else Logging Expanded's
  "branches first" message); the rack while it holds fewer than four and its own `CanStoreTrunk`
  agrees; the heating rack when empty, and debranched under the same rule, as its own empty-hand load
  asks. A station that cannot take it does nothing, so a carried trunk never makes it unload. The
  sawhorse loads the trunk's stored log stack, as Logging Expanded's own load does.
- **Unloading.** With an empty hand and nothing carried, a click on a station that would give a
  trunk back puts it into the hands: the sawhorse's unload stack when it is a trunk
  (`BuildUnloadStack`; one loaded with logs or firewood gives those back as Logging Expanded does),
  the rack's top trunk, the heating rack's trunk. While the hands are full, the error, and the trunk
  stays (the heating rack's is stored back, its retrieve having written its state into the stack).
- Anything else (a tool, logs, a knife on the heating rack, Carry On's own sneak clicks) is the
  original's. The client's prefix only says a carried trunk's click is the station's.

If a station's type or members are not found, that station is left as it ships, with one warning
naming what is missing; the server log says how many of the three were patched.

**The rosser and the bucking mill** (`../Rosser/Game/BERosser.cs`,
`../BuckingSawmill/Game/BEBuckingMill.cs`) do the same in their own block entities:

- **Loading by hand.** With an empty hand, a right-click loads the trunk carried in Carry On's
  hands under each machine's own rules (the rosser refuses a debarked or empty trunk and any while
  incomplete or occupied; the mill refuses a branched one while the branch rule holds, and loads
  only with its saws at the top, keeping a held click until they come up). With trunk entities and
  no Carry On, or without trunk entities, it is the old load from the hand, hotbar or backpack.
- **Ctrl + right-click** takes the trunk back into the hands; while they are full, the hands-full
  error and the trunk stays on the machine. With trunk entities but no Carry On, the trunk is laid on
  the ground as an entity two cells beyond the middle of the infeed cells, across the machine's line,
  so the machine does not take it straight back (`TrunkStations.DropBeyond`).
- **The ground pull.** Each machine's infeed cells are where it takes a rack's top trunk: the three
  ground cells just beyond its infeed end (`InfeedNeighbours` of each rig). A trunk entity lying there
  is taken as a rack's trunk is, under the same gates, at the same moments (the rosser's
  once-a-second rack poll; the mill every tick its saws are at or pass the top, and once a second):
  the machine empty, complete and turning at `MinSpeed`, `AutoPullFromRack` on, and the trunk one it
  takes (the rosser: not debarked, with logs; the mill: not branched while the branch rule holds, with
  logs). A trunk counts as in a cell by its middle's column, at its underside's height
  (`TrunkStations.FindInCells`, floor of x and z, floor of y + 0.5): a trunk resting on the floor of a
  cell is in it however long it is, and one whose middle lies outside the three cells is not. **A rack,
  or for the mill a feeder (a rosser in line), comes first**: the ground is looked at only when none
  of them has a trunk ready (`PullFromRack` falls through to `PullFromGround`). The entity is removed
  and its stack goes on unchanged. The block info speaks only of racks and feeders.

## Old worlds

Placed trunk multiblocks, from before trunk entities ran, are **deleted** as they load, with nothing
given back and no migration (`Game/OldTrunkBlocks.cs`): a Harmony postfix on Logging Expanded's
`BETreeTrunk.Initialize` (server side, while the feature runs) queues the controller's removal for
the next tick, the game's multiblock behaviour takes the filler blocks with it, and each removal is
logged. That is the pack's call: such trunks were few, and a trunk can no longer be placed. If the
block entity type is not found, one warning, and placed trunks are left as they are.

Trunk item entities lying in a world are swapped as they load (above). A trunk in an inventory from
before (a hotbar, a backpack, a chest) stays where it is: the storage flag only stops it going in.
Thrown out of the inventory it becomes a trunk entity. Placed as a block from a hotbar, it would be
a new trunk multiblock, and the postfix above runs on any `BETreeTrunk` that initialises, so it is
deleted the next tick (read from the code, not tested).

## Config

`TrunkEntities` (bool, default true) switches the feature; `TrunkEntitiesSettings` in
`ModConfig/seraphhorizons.json` (`Core/TrunkEntityConfig.cs`) holds its figures, next to `Rosser`.
Values out of range fall back to the default with a warning. The server's values are used.

| Setting | Default | Range | |
|---|---|---|---|
| `WeightPerLog` | 8 | 0..1000 | Weight a stored log adds: weight = 10 + logs × this. What a rope or a grab pulls against. |
| `CarrySpeedAtFourLogs` | 0.25 | 0..1 | Walk speed, as a multiple of the normal one, carrying a trunk of 4 logs or fewer |
| `CarrySpeedAtMaxLogs` | 0.02 | 0..1 | Walk speed carrying one of 48 logs or more; linear in logs between the two |
| `SpudSecondsPerLog` | 0.5 | 0..60 | The bark spud's hold per stored log, 2 s at least |
| `GrabRange` | 3 | 1..10 | Blocks from the hand beyond which a grab cannot start, and lets go |
| `MaxGrabWeight` | 0 | 0 and up | Trunks heavier than this cannot be grabbed by hand, only roped; 0 is no limit |

**Who decides.** The classes (the entity, the pick-up behaviour, the renderer) are registered on
both sides whatever the setting, so both entity types always exist. The server decides in `Start`
(the switch on, Logging Expanded installed, `LoggingBridge` resolving) and writes it to the world
config (`seraphhorizons:trunkEntities`), which the game sends a client before it starts its mods; a
client follows that, whatever its own setting (a mismatch logs one notification), as
`UnifiedWoodworking` does. **Off**, nothing runs: no spawn swap, no storage flag, no grab, no tool
behaviour, no station or machine change, no deletion, and the feature's three patch files are emptied
before the game's patch loader runs (`carryon` and `carts` by `TrunkEntitySystem.DisablePatches`,
`stations` by `TrunkStationsSystem`, which also empties it when Carry On is missing). Logging
Expanded's trunks are then items, as it ships them. Trunk entities already in a world stay
entities, with nothing to pick them up; turn the switch back on to use them.

## Tests

- `tests/TrunkEntities/TrunkEntityCoreTests.cs` (part of the mod's unit tests,
  `dotnet test mods-src/seraphhorizons/tests`, no game needed) compiles `Core/` and tests the
  documented defaults, out-of-range values falling back and edge values kept; the weight (10 + 8 per
  log, and following the setting); the carry speed, linear from 4 to 48 logs and never rising; the
  spud's hold (half a second a log, 2 s at least); `MaxGrabWeight` 0 meaning any weight; the boxes
  per class (four cubes, five 2 × 2 slices, none for none); the radius; and the turned boxes at
  yaw 0, a half turn and a quarter turn (laid along x, one box wide), their middles kept at their
  distance.
- The Atlas scenarios (`tests/PackTests`) load this build with every locked mod, Carry On included,
  and the pack's default settings; each works on a floor of its own high in the sky. A player is a
  fake one whose clicks reach the server through the entity's or the item's own interaction methods.
  - `TrunkEntityScenarios.cs`: the feature runs with the world config key, the storage flag and both
    types; a trunk item spawned as felling spawns it (`SpawnItemEntity`) becomes a thin trunk entity
    with its stack, branches and info, and a thick one for xl; a trunk cannot be given to a player;
    the weight follows the logs per entity and leaves the type's alone, and no logs removes it; a
    trunk dropped from a height rests on the ground; a placed trunk multiblock is removed when it
    loads, nothing dropped; and the grab: sneak shoulders instead, an empty hand ties the grab's rope
    between player and trunk, the trunk follows a player who steps away, letting go removes the rope,
    and too far refuses.
  - `TrunkToolScenarios.cs`: every tool kind gets the behaviour; the axe takes a log, with a hammer
    a debarked log; the knife cuts sticks and leaves a clean trunk; shears make a sapling from twelve
    branches; the saw cuts planks; the axe and saw refuse a branched trunk; the spud debarks a clean
    trunk in one hold and drops bark; a trunk at its last log is gone after the axe.
  - `TrunkCarryScenarios.cs`: carrying runs; a carried trunk has Logging Expanded's animation by
    size; Trunk Storage Racks cannot be carried; a trunk given goes into empty hands and back out;
    carrying slows by the logs and the speed goes when it is put away; sneak-clicking a trunk entity
    shoulders it; putting it down lays a trunk entity and no block; a dropped carried trunk lands as a
    trunk entity; a cart takes a carried trunk and gives it back to the hands.
  - `TrunkStationScenarios.cs` (on the woodworking world, `WoodworkingScenarios`, where trunk
    entities run with Carry On): a running rosser, and a running mill with a debranched trunk, take a
    trunk entity from their infeed cells only; Ctrl on the mill puts the trunk in the hands and full
    hands refuse; the sawhorse, the Trunk Storage Rack and the heating rack load from the hands and
    unload into them.
  - The rosser's, the mill's and the debarked trunk's own scenarios (`RosserScenarios.cs`,
    `BuckingSawmillScenarios.cs`, `DebarkedTrunkScenarios.cs`) run with trunk entities on and hand
    their trunks over through Carry On's hands.

```sh
dotnet test mods-src/seraphhorizons/tests --filter "FullyQualifiedName~TrunkEntities"
TMPDIR=~/.cache/atlas-tmp VINTAGE_STORY=<game> dotnet test tests/PackTests \
  --filter "FullyQualifiedName~TrunkEntityScenarios|FullyQualifiedName~TrunkToolScenarios|FullyQualifiedName~TrunkCarryScenarios|FullyQualifiedName~TrunkStationScenarios"
```

With the switch off (`fixtures/switches-off`, `TrunkEntities: false`) the other switches' scenarios
run on Logging Expanded's trunks as items; no scenario there checks the trunks themselves.

## Known limits, and what is not checked in the game

Nothing client-side has been run in the game. Atlas runs a server only, so these are compiled but
unseen: the renderer (the trunk's place, turn and texture against its boxes, the debarked look, the
shadow pass), the client's picking along the turned boxes, the interaction help, the tools' swing,
progress bar and the spud's animation, the client's repeat pause, Carry On's client prediction of
a put-down (the same patched method, which a server cannot run), the carry animations and the
`carry-trunk` transform on a trunk, and how the grab's rope looks and feels. Before release, play
through:

- felling a real tree (Atlas spawns the trunk item as Logging Expanded's felling does, not by
  felling), and the trunk's look and rest on uneven ground and slopes;
- floating and drifting in still and flowing water (buoyancy is not in any scenario);
- a rope item tied to a trunk and to a post, an animal and a cart (the game's, but untested here),
  and shoving by walking into a trunk;
- dragging a thick 48-log trunk by hand, and whether the pull feels right;
- the 0.8 s Carry On hold before a station, rosser or mill takes a carried trunk;
- a carried trunk on a sled, and taking one off a cart with an empty hand from a real client;
- a trunk lying at an angle (its collision is a staircase of axis-aligned boxes, above).

Also not checked: the axe on a debarked trunk without a hammer (debarked logs, by reading; the
placed-trunk scenario for it went with placed trunks), Carry On's death, damage and quick drops
(one dropped-trunk scenario covers the drop path), a trunk from an old inventory placed as a block
(above), the heating rack's hands-full put-back, and the feature without Carry On or without
Immersive Woodworking: the pack always loads both, so Atlas never sees those paths.

Known compromises:

1. **Display size.** Every thin trunk is shown, boxed and dragged as a 4-block `lg` trunk and every
   thick one as a 5-block `xxl`, whatever its own length, as the machines show them.
2. **The ground pull finds a trunk by its middle.** A trunk lying across the infeed cells with its
   middle outside them is not taken; one needs to lie in line with them.
3. **The switch off strands entities.** Trunk entities already in a world stay entities with the
   switch off, and nothing then picks them up or works them.
4. **The handbook describes trunk entities.** The woodworking guide and the machines' handbook pages
   say how trunks are moved and loaded with the feature on, and are not rewritten when it is off.
