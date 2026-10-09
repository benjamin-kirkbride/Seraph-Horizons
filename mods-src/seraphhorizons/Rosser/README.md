# Rosser

Part of the Seraph Horizons mod (`../README.md`), switched by `Rosser` in
`ModConfig/seraphhorizons.json`: a mechanically powered ring debarker that draws Logging Expanded
(`loggingmod`) tree trunks lengthwise through a spinning cutter ring. A limb breaker in the throat
snaps off the branches, which fall as sticks; four scraper arms on the ring, tipped with bark spud
heads, strip the bark, which falls as Immersive Woodworking's (`immersivewoodworking`) bark; and the
trunk comes out as Logging Expanded's own trunk in a third, debarked state. It is built in the
world from a frame and existing items (Immersive Woodworking's sawmill crankshaft and levers, the
game's large gear sections, hoops, rods and plates, four straight copper or lead pipes of Pipes and
Power Expanded's for the drip, and four bark spud heads), and it hands the
debarked trunk on to a Trunk Storage Rack or to a bucking mill (`../BuckingSawmill/README.md`)
placed in line. Neither mod is referenced at build time.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` or `BuckingSawmill/`, which are this folder's
neighbours. `Core/` is the game-independent part, `Game/` the blocks, block entities, renderer
and mod system (`RosserSystem`), and `tools/` the model's generator. `DebarkedTrunks.cs` is the
debarked trunk (below). The rig maths, footprint, trunk path and trunk box, and the Logging
Expanded bridge are shared with the bucking mill in `Machines/` (`Machines/Core/`,
`Machines/Game/`), as is the generic half of the generator (`Machines/tools/machinegen/`).

The rosser needs both mods, and the debarked trunk, which the same switch adds. With the switch
off, either mod not installed, or the debarked trunk not there (Logging Expanded's trunk not as the
patch expects), the server marks the rosser's four blocktypes and its recipe disabled before the
game loads them (`RosserSystem.Disable`), so the blocks do not exist at all; rossers already placed
in a world are then lost, and so are debarked trunks. Pipes and Power Expanded (`ppex`) is
optional: it waters the drip, and its straight pipe in copper and lead (the metals the `UnifiedPipes`
switch adds) is the drip's pipes, a stage of the build. Without ppex, or with `UnifiedPipes` off,
there are no such pipes, and the rosser is built without them (**Assembly**). The classes are
registered on both sides whatever the setting, and a client follows the server.

## The debarked trunk

The rosser's output is Logging Expanded's own trunk block with a third state of its `branches`
variant: `loggingmod:treetrunk-{wood}-{size}-debarked-{side}`, named "<Wood> Tree Trunk
(Debarked)". It is the mod README's to describe in full (the `Rosser` switch); in short:

- **Patch.** `assets/seraphhorizons/patches/rosser-debarkedtrunk.json` adds the state
  (`/variantgroups/2/states/-`), gives it the clean trunk's shape (a `shapeByType` that resolves into
  the trunk's own `shape`, so only `base` changes and `rotateYByType` stays), and puts
  `block/wood/debarked/<wood>` on the bark faces (`wood-h`) and `block/wood/treetrunk/debarked/<wood>`
  on the ends (`wood`, the vanilla debarked log's end grain, with no bark ring) of each wood's texture
  entry. Cherry, which the game has no wood of, takes oak's; Wildcraft: Trees' woods (not in the
  pack) point their bark into its domain, as Logging Expanded's does, and keep Logging Expanded's
  ends. The 55 names are in `lang/en.json`. It is not in the creative
  inventory.
- **Check.** `DebarkedTrunks.Bind` checks Logging Expanded's `blocktypes/treetrunk.json` against
  the patch in `Start` (the third variant group is `branches` with states `yes` and `no`, the shape
  is `treetrunk-{size}-{branches}` with no `shapeByType`, and the texture entries are exactly the
  ones the patch targets, each with a `wood-h`, and a `wood` wherever the patch replaces the ends) and
  finds every Harmony target; on any failure it
  logs one warning and empties the patch, and there is no debarked trunk (nor rosser).
- **The log stack's mark.** `Trunks.Debark` (`Machines/Game/Trunks.cs`) makes it: the same wood,
  size and side, the attributes cloned (logs, resin, char), `branchCount` removed, and the stored
  log stack marked with `seraphhorizons:debarked`, because a sawhorse keeps only that stack.
- **What takes it.** The bucking mill cuts it into `debarkedlog-<wood>-ud`; the sawhorses' axe
  gives debarked logs, the spud does nothing and no bark drops, and unloading gives the debarked
  trunk back; the Trunk Storage Rack stores and returns it (drawn with bark: the rack has its own
  shapes per wood); Carry On and Cartwright's Caravan patch the whole trunk blocktype, so they take
  it too. The knife and shears do nothing (it has no branch count). An axe on a placed debarked
  trunk, or on a debarked trunk entity with trunk entities (`../TrunkEntities/README.md`), gives
  debarked logs, and the bark spud makes one from a clean trunk entity. The heating rack drains its
  resin as before.

## The machine

**Blocks.** The controller is `seraphhorizons:rosser-frame-{side}` (item form `-north`), the middle
of the machine's near end at ground level, which is its outfeed end. **Placing** it is the mill's
rule (`BlockRosser.TryPlaceBlock`, `Footprint.PlacedFacing`): the rosser extends away from the
player along their line of sight, so the infeed end is the far one. Placing needs room for every
cell of the rig and stamps an invisible ghost into each other cell: `rosser-ghost`,
`rosser-ghostpower-{side}` in the cell that takes the axle and `rosser-ghostwater-{side}` in the cell
a water pipe connects to. Ghosts store the controller's position (`IMachineGhost.Principal`) and
pass interaction, breaking, the pick-block stack, name, info and help to it. Every cell's collision
and selection boxes come from the controller (`BERosser.CellBoxes`): its own from the rig, plus its
part of the trunk. Its collision boxes (`CollisionBoxes`) add the cell's lid on a station column's top
cell (below). The controller re-stamps missing ghosts once a second. Breaking the frame or any
ghost breaks the whole rosser.

**Rig.** The footprint and anchors are data, in `assets/seraphhorizons/config/rosser-rig.json`
(written by `tools/make_shape.py`; schema under [Rig schema](#rig-schema-rosser-rigjson)). In the
native, south-facing frame with the controller at `[0,0,0]` the machine box is 16 × 4 × 5 cells, x
−15..0 (west to east, the way the trunk travels), y 0..3, z −2..2:

- the station, x −12..−4: the full section, y 0..3, z −2..2;
- the beds, x −15..−13 (infeed) and −3..0 (outfeed): y 0..2, z −1..1, so the bucking mill's power
  feed cell, `[0,3,0]` here, stays free;
- 243 cells, 41 of them `hollow`: cells over the trunk's path that no element of the model reaches.
  A hollow cell is a ghost (nothing can be built in the trunk's way) but has no box of its own: it
  is solid only where the trunk's box is.
- 45 lids, the mill's (`../BuckingSawmill/README.md`, **Rig**), over the station only: a
  collision-only box 1/16 thick over the whole of each station column's top cell, at y 4, so a
  player on top cannot drop into the hollow cells or the gaps about the ring and the rolls and get
  stuck, or be caught there by a moving trunk. A hollow cell's lid is still collision only: its
  selection box list is empty without the trunk. The beds have none: there a lid would be an
  invisible floor at y 3 (the top of their hollow cells), two blocks above the bed rolls, which was
  awkward to walk on. The generator
  lids only the station's columns (`with_lids(cells, station_column)`) and fails on a station column
  without a lid or a bed column with one (`lid_gaps`).

The power cell is `[-9,3,-2]`, taking the axle on its north face: high on the side, just upstream of
the ring, on the right as you look along the machine from its near end. The water cell is `[-8,2,2]`,
its pipe on the south face, on the other side. Bark and sticks come out of the chute on the south
side under the ring. Turning to a facing is the mill's (`Machines/Core/Footprint.cs`): the shape
turns by rotateY north 180, east 90, south 0, west 270. `Core/RosserRig.cs` parses the file.

**Assembly.** Right-click the frame or any ghost holding a part, recognised by full code
(`Core/RosserParts.cs`). Nine stages, each a `requires` name of the rig but the pipes, whose parts
are one per metal:

| Stage | `requires` | Items | Draws |
|---|---|---|---|
| Drive | `shaft` | 1 `immersivewoodworking:sawmillcrankshaft` | The entry shaft, flywheel and crown disc, the two rectifier pinions, the main shaft with its ring pinion and both worms |
| Ring | `ring` | 4 `game:largegearsection-wood` | The cutter ring (body, rim teeth, arm pins and springs), its four rollers and the four scraper arms |
| Tyres | `tyres` | 2 `game:hoop-{metal}` | The ring's two iron tyres. Needs the ring. |
| Infeed rolls | `rollsin` | 2 `game:rod-{metal}` | The infeed lay shaft and worm wheel, change gears, selector, cross shaft, top-roll arms and top roll, and bottom roll |
| Outfeed rolls | `rollsout` | 2 `game:rod-{metal}` | The same at the outfeed |
| Limb breaker | `breaker` | 2 `game:metalplate-{metal}` | The V bars |
| Levers | `levers` | 1 `immersivewoodworking:sawmilllevers` | The treadle, its lever and the pushrod, the rock shaft, and both selector levers |
| Pipes | `pipecopper` or `pipelead` (the stage is `pipes`) | 4 `ppex:pipe-straight-*-copper` or `-lead`, one metal, from one stack | The drip's water line, in that metal's pipe texture: the inlet from the water face, the riser beside the ring, the run over the roller's bracket and the header across the trunk with its nozzles |
| Heads | `heads` | 4 `immersivewoodworking:barkspudhead-{metal}`, one metal, from one stack | The scraper tips, in that metal. Needs the ring. |

- Any order, except that the tyres and the heads go on the ring (`NeedsRing`).
- A click takes from the held stack as many as the stage still needs: rods fill the infeed rolls
  first, then the outfeed rolls, up to four from one stack. The heads and the pipes need a stack of
  four (`NeedsFullSet`), so they are always one metal.
- Hoops, rods and plates must be iron, meteoric iron or steel while the woodworking machines are
  iron work (`IronWoodworkingMachines` on, as it is by default; `RosserSystem.PartMetals`), and may
  be any metal with it off. The heads may be any metal. A save keeps every fitted item's code, and
  is restored with any metal, so turning that switch on never takes a fitted part away.
- **The pipes** are Pipes and Power Expanded's straight pipe (`ppex:pipe-straight-{ns,we,ud}-{metal}`,
  any orientation; it goes back as it came) in copper or lead, the metals `UnifiedPipes` adds to ppex's
  pipes (`patches/unifiedpipes-ppex.json`), whatever `IronWoodworkingMachines` says: iron and steel
  pipes are refused (`error-wrong-pipe-metal`). The model draws them in that metal: the rig has a part
  per metal, `pipecopper` and `pipelead`, the same elements in `game:block/metal/sheet-plain/copper4`
  and `lead4`, the textures `UnifiedPipes` gives ppex's copper and lead pipes, and the renderer draws
  the fitted metal's (`RosserParts.Fitted`, `PipeMetal`; the block info names it). Until they are in,
  a straight pipe in hand is a part, so a click on the water face fits it (sneak to place one there);
  once they are in, the click is not the rosser's and places the pipe against it as against any
  block, as a player connects the water face (`RosserParts.TakesClick`). Water comes in only
  through them (**Water**).
- **Without the pipes.** The pipes are a stage only while ppex's straight pipe exists in copper and
  lead (`RosserSystem.PipesNeeded`, which each side asks of its own world: ppex installed and
  `UnifiedPipes` on). Without them the stage is left out (`RosserParts.PipesNeeded`): the rosser is
  complete without pipes, draws none, refuses none (a pipe in hand is never a part), and takes water
  from a pipe on the water face as before. A rosser saved before the pipes were a stage has none:
  it stops, its block info lists them, and it runs again once they are fitted.
- Parts are used up outside creative mode. In creative mode one item fits all its stages take
  (a single rod fills both roll sets), and nothing is taken.
- Ctrl + right-click takes the trunk back if it is waiting or delivered, refuses while it is in the
  rolls, and with no trunk takes the four heads back while they are unused. With trunk entities
  (`../TrunkEntities/README.md`) the trunk goes into the player's Carry On hands, and stays on the
  rosser with an error while those are full (`TrunkStations.GiveToHands`); without Carry On it is
  laid on the ground two cells beyond the infeed end, across the line (`TrunkStations.DropBeyond`). Used heads stay on until
  they are spent. The other parts come back only by breaking the frame.

The parts are drawn as they are fitted (the rig's `requires`). The cradles, the rocker and the
frame are always drawn: the frame item carries them.

**Creative shortcut.** The woodworking stations' (the mod README's unified woodworking section,
`Core/CreativeUpgrades.cs`): a player in creative mode who right-clicks an unassembled rosser with
Ctrl held and not Shift gets its next stage fitted at once, whatever they hold, with nothing
taken: drive, ring, tyres, infeed rolls, outfeed rolls, limb breaker, levers, four copper pipes
(while the pipes are a stage), then four steel heads (`AssembledMachines.DefaultMetal`; steel for
the hoops, rods and plates too; the pipes copper, steel being no pipe metal), one stage per click
(`RosserParts.NextPart`). On an assembled rosser Ctrl does what it does in survival. It runs while
`UnifiedWoodworking` does (`BERosser.CreativeShortcut`); the server decides, from its own player
data, and a player in creative sees the help line
(`seraphhorizons:woodworking-help-creative-upgrade`) while a stage is missing.

**Power.** `BEBehaviorRosserMP`, a mechanical power consumer on the power ghost, connects only
through the rig's power face, turned to the rosser's facing (`BlockRosserGhostPower`). It loads the
shaft with the mill's 0.005 until the rosser is complete, then with `Resistance` (times
`MachineOilSettings.DryResistanceMultiplier` while its oil tank is dry: with `MachineOil` on, the
rosser keeps an oil tank as the bucking mill does, built empty, filled by a click with oil on any
cell, drained by `MachineOilSettings.Rosser.DrainPerJob` per stored log of each delivered trunk,
rounded up; `../README.md`, "Machines need oil"). The axle comes in
along native z, on a side face, so the shaft's sign is `MillMotion.NativeShaftAngle(side, angle,
Axis.Z)`: − facing south or east, + facing north or west, which turns the entry shaft with a vanilla
axle on the power face (unit-tested per facing, and checked in Atlas against a real axle). The
mechanism is rectified (see [Model](#model)), so it does not matter which way the network turns.
The rosser is **running** when it is complete (heads fitted and not spent) and its shaft turns at
`MinSpeed` or faster; it feeds, and takes from a rack, only while running. A delivered trunk goes onto
a rack whenever the shaft turns that fast, running or not: only a new trip needs heads. The ring
spins whenever the shaft turns.

**Trunks.** One at a time, held as the trunk's whole item stack. Any `loggingmod:treetrunk-*` goes
in, branched or not (the limb breaker deals with branches; Logging Expanded's
`RequireBranchRemovalForProcessing` is not asked). Refused, each with an in-game message: a trunk
already debarked (`error-already-debarked`), one holding no logs, a second trunk, and any trunk while
the rosser is not complete. Power is not needed to load. By hand, right-click with an empty hand
while carrying the trunk in Carry On's hands (trunk entities, `../TrunkEntities/README.md`: a trunk
is never in an inventory, and Carry On lets the click through because
`patches/trunkentities-stations.json` gives the rosser's blocks its `CarryableInteract`); without
trunk entities or Carry On, right-click holding a trunk, or with an empty hand to take the first
trunk the rosser would take from the hotbar, then the backpack.
Loading fixes the trip's pace: the trunk's class and the heads' metal (below).

The trunk is shown, and boxed, as the mill shows it (`Trunks.ShownBlock`, `TrunkBox`): a thin trunk
(sizes `xs` to `lg`) as Logging Expanded's `lg` model, 1×1×4 blocks, and a thick one (`xl`,
`xxl`) as the `xxl` model, 2×2×5, of its wood and without branches, centred on the trunk path's
axis (`MillMotion.TrunkOnAxis`). While loaded it is solid and selectable: one box of the shown
model's size from tail to nose (`TrunkBox.BoundsOnPath`), clipped into the rosser's cells (hollow
ones included) and added to their boxes. The boxes follow the trunk in 1/16-block steps: the block
entity rebuilds its whole box table when the step changes, on both sides, and collision lookups off
the main thread only read the published table. A click on the trunk acts on the rosser; one with an
unrelated item in hand, which the item would otherwise use, is taken and does nothing.

**The trip.** All states are derived from the trip (`Core/RosserTrip.cs`), never stored:

| State | Condition | |
|---|---|---|
| Empty | No trunk | A rack at the infeed end can feed it |
| Waiting | T = 0 | On the infeed bed, nose just short of the infeed rolls; its weight is on the treadle |
| Feeding | 0 < T < T_end | In the rolls; stalled when not running |
| Delivered | T = T_end | Debarked, on the outfeed bed, tail just past the outfeed rolls |

T is the trunk's travel in blocks along the rig's `trunkPath`: the nose is at `nose0` + T and the
tail L_k behind it (L = 4 thin, 5 thick), and the trip ends when the tail reaches `tailStop`, at
T_end = `tailStop` + L_k − `nose0` (9.59 blocks thin, 10.59 thick). Each server tick (50 ms) a
running rosser turns T on by the shaft's angle change (`ShaftClock.AngleAdvance`, unsigned) times
the trip's rate. The feed is geared and never slips, so the rate is fixed at load for the trunk's
class and the heads' metal (`Core/RosserPace.cs`):

```
BlocksPerTurn(k) = T_end(k) / TypicalTurns(k)             blocks per shaft turn at copper speed
TypicalTurns(k)  = logs_k × RevolutionsPerStoredLog + branches_k × RevolutionsPerBranch
Rate(k, tier)    = BlocksPerTurn(k) × HeadSpeed(tier) / 2π   blocks per radian of shaft
HeadSpeed(tier)  = 1 + HeadSpeedPerTier × max(0, tier − 2)   (Cutting.BladeSpeed, the mill's rule)
```

So every thin trunk takes `TypicalTurns(1)` / HeadSpeed shaft turns for its whole trip (68 at
copper with the defaults) and every thick one `TypicalTurns(2)` / HeadSpeed (169). The heads' tier
is looked up as the mill looks up its blade kit's (`RosserSystem.HeadTier`): Immersive Woodworking's
bark spud of that metal if it has a tool tier (1.3.11's has none), else the game's saw of that
metal, else the metal's tier in the game's metal properties plus one, else `HeadMetals.SawTier`.
With the default 0.35: copper, gold and silver 1×, the bronzes 1.35×, iron and meteoric iron 1.7×,
steel 2.05×. The server saves T and the rate, and syncs them; the client advances its own T with
the shaft between syncs (`RosserVisuals.EstimateTravel`), takes the server's when they differ by
more than 1/16 block, and draws T part way between its last two ticks.

**Sticks and bark.** They drop as the relevant part of the trunk passes, not at the end, at the
rig's chute (`chute.pos`, turned to the facing), pushed out of its side:

- **Sticks.** S = floor(branches × `StickFraction`), from the stack's `branchCount` (0.5 by default:
  one stick for every two branches, where Logging Expanded's knife gives one per branch). Stick i
  (0-based) drops when (nose − breaker) / L_k ≥ (i + 1) / S, so evenly as the trunk goes through the
  limb breaker, as `game:stick` with the stick-break sound.
- **Bark.** Log j's bark drops when (nose − ring) / L_k ≥ (j + 1) / logs, counted over the stored
  logs. Each log is one roll of Immersive Woodworking's bark for the trunk's wood (`BarkDrops.Roll`,
  `../Woodworking/BarkDrops.cs`, as on the sawhorses): the kind from its bark table at a chance
  multiplier, the count the table's for the species (3 by default). Dry, the multiplier is
  `DryBarkMultiplier` (1.0, a plain spud's). Equal stacks from one tick are merged. If Immersive
  Woodworking's bark cannot be bound, one warning is logged and trunks are still debarked, with no
  bark.
- The trip counts what has dropped (`SticksDone`, `BarkDone`) and saves the counts, so a step only
  gives what is due beyond them: nothing drops twice across a save, an unload or a restart. A save
  without the counts takes everything due at its T as dropped.

**Water.** Optional, from a Pipes and Power Expanded water pipe on the water ghost's water face
(`Game/PpexWater.cs`), through the drip's own pipes: while they are a stage, the reservoir takes no
water until they are fitted (`BERosser.DrawWater`). The drip's inlet ends at the water face on the
middle of the cell's face, in ppex's 6 x 6 section, so a pipe there meets it end to end. Once a
second the controller asks the pipe's network for what the reservoir
lacks, at most `WaterIntakeLitresPerSecond` (10) a second up to `ReservoirLitres` (20). Each log
scraped while the reservoir holds `WaterPerLog` (2) is wet and spends that much, so a full
reservoir wets ten logs. A wet log's roll uses `WetBarkMultiplier` (1.5, so the special kinds of
bark come more often) and its count is multiplied by `WetBarkCountMultiplier` (1.5): floor(n × 1.5),
and one more with chance frac(n × 1.5), so 3 pieces become 4 or 5. Only water is drawn (the network's
`MediumType` is `Water`). The binder tries ppex 0.7.1 / exlib 0.8.4's names, then 0.6.8 / 0.7.2's
(pinned before), by reflection; it uses the network manager's `GetConnectedNetworkAcross`, which
finds the pipe beyond the face whose connector points back, so the water ghost has no ppex type of
its own. It overrides `CanAttachBlockAt` on its water face only, so ppex keeps a pipe's connector
into it and does not break the pipe. Without ppex, or with its members not as expected (one
warning), the rosser runs dry. Whether it is wet is synced, for the drip's particles.

**Wear and spent heads.** The four heads are the wearing part. Fitted, they get a capacity of
`HeadWearMultiple` (4) × the durability of Immersive Woodworking's bark spud of their metal
(`RosserSystem.HeadCapacity`; `HeadMetals.SpudDurability` when the item is missing): copper 1000,
iron 3600, steel 9000 with the default. Each delivered trunk costs ceil(stored logs ×
`HeadWearPerStoredLog`) points. At 0 the heads are spent: the tool-break sound plays, they are gone
(nothing comes back, also on breaking), and the rosser is incomplete until four new ones are
fitted. The trunk that spent them is still delivered, and still leaves: onto a rack at the outfeed
end while the shaft turns, to a mill in line, or by hand. The next trunk waits until four new heads
are on. The block info
shows the heads' metal, their points left and their feed speed.

**Racks at both ends.** Once a second, and on delivery:

- **Infeed.** An empty, running rosser with `AutoPullFromRack` on takes the top trunk of a Trunk
  Storage Rack standing in one of the three ground cells just beyond its infeed end
  (`RosserRig.InfeedNeighbours`: native `[-16,0,-1..1]`, in line with the beds and only beyond the
  end row, never beside the wider station). Either of the rack's two cells counts
  (`LoggingBridge.FindRack`, shared with the mill). A top trunk already debarked, or with no logs,
  stays and holds the line; the block info says so (`RosserRackState`). With trunk entities and no
  rack ready, a trunk entity lying in those cells (its middle's column, at its underside's height,
  `TrunkStations.FindInCells`) is taken the same way and under the same rules (`PullFromGround`); a
  rack there goes first. The block info speaks only of racks.
- **Outfeed.** A delivered trunk, while the shaft turns at `MinSpeed` (spent heads do not stop it)
  and with `AutoPushToRack` on, goes onto a rack in
  one of the three ground cells just beyond the outfeed end (`OutfeedNeighbours`, `[1,0,-1..1]`)
  if it holds fewer than four trunks. The count is checked first because Logging Expanded's rack
  drops a fifth trunk silently (`RosserTrip.RackHasRoom`). A full rack leaves the trunk on the bed,
  and it goes as soon as there is room. Then a running rosser pulls its next trunk. With
  `AutoPushToRack` off the trunk waits for a hand, and the block info says so rather than pointing
  at the rack.

The block info says what the infeed rack offers while the rosser is empty, and where a delivered
trunk goes (`RosserOutfeedState`: none, rack, rack full, mill, or pushing switched off).

**The mill in line.** A bucking mill placed with its far (infeed) end against the rosser's near
end, facing the same way, takes the delivered trunk itself. To place them, set the mill down first,
then place the rosser on the cell just beyond the mill's far end, in line with the mill's middle,
looking the same way; the rosser's controller is then the mill's `[-6,0,0]`, its outfeed end fills
the mill's three infeed cells, and the mill's power feed cell (`[-6,3,0]`) stays free. The rosser
implements `ITrunkFeeder` (`Machines/Game/ITrunkFeeder.cs`) and its ghosts `IMachineGhost`:

- `HasOutfeedCell` answers only for the controller's cell, the outfeed end's cell on the trunk's
  line. A mill offset one block sideways still finds it (its side infeed cell is the controller);
  one further off does not.
- `Busy` is true while a trunk is waiting or feeding; `PeekFinished` gives the delivered trunk
  without taking it; `TakeFinished` takes it, clears the bed and its boxes, and syncs.
- The rosser counts a mill as in line (`BERosser.MillInLine`) when a mill facing its way has the
  controller's cell among its infeed cells, and the mills' `AutoPullFromRack` is on. Then it does not
  push to a rack: the trunk is the mill's, which takes it at the top of its saws' cycle under its
  own gates (`../BuckingSawmill/README.md`, "A rosser in line"). Until it does, the trunk stays the
  rosser's: Ctrl takes it back by hand, and breaking the rosser drops it once.

The thick trunk's axis is 1.6875 blocks up in the rosser and 1.44 in the mill (0.97 thin): the
hand-off is a transfer, not a slide, and the step shows only at that instant.

**Breaking.** Breaking the frame or any ghost drops the frame, every fitted part by its code (the
heads only while unused), and the trunk as far as it got (`RosserTrip.Broken`; with trunk entities
it lands as a trunk entity), so breaking and
running it again never gives its sticks or bark twice:

| Where the trunk is | It drops as |
|---|---|
| Waiting, or the nose not past the limb breaker | As loaded |
| Nose past the breaker, not past the ring | Debranched (`Trunks.Debranch`: the clean trunk, branch count gone; the sticks already dropped are all there are) |
| Nose past the ring, or delivered | Debarked |

The ghosts are cleared and the renderer disposed.

**Logging Expanded** is read through `Machines/Game/LoggingBridge.cs`, by reflection, as the mill
reads it: the rack's block entity (`PeekTrunk`, `PopTrunk`, `PushTrunk`, `TrunkCount`), and
`TreeManager`. The log count and wood come from the trunk stack's own attributes. If a member is
missing the mod logs one warning and the rosser takes no trunks.

**Sounds and particles** belong to states. Server side: a latch at load and delivery (the feed
throwing in and out), a stick crack per stick drop, and the tool break when the heads are spent.
Client side, while feeding and running: Immersive Woodworking's debarking sound
(`immersivewoodworking:sounds/debark/debarking{1..3}`, referenced, not copied) every 1.2 s and
bark-brown chips every 0.1 s while the trunk is under the spud heads (`trunkPath.tips` for its
class, downstream of the ring's plane), and drips every 0.15 s while wet and the trunk is under the
drip. Silent on purpose: an empty rosser whose ring turns (the network's axles make their own
noise), a waiting trunk without power, a stalled trunk, a delivered trunk, and a feeding trunk not
yet at the heads or past them.

## Config

`RosserSettings` in `ModConfig/seraphhorizons.json` (`Core/RosserConfig.cs`), next to the `Rosser`
switch. Values out of range fall back to the default with a warning. The server's values are used.

| Setting | Default | |
|---|---|---|
| `Resistance` | 0.2 | Load of the assembled rosser on its shaft (0.005 before) |
| `MinSpeed` | 0.05 | Shaft speed below which it does not feed, or take from or give to a rack |
| `RevolutionsPerStoredLog` | 6 | Shaft turns a typical trunk's trip takes per stored log, at copper speed |
| `RevolutionsPerBranch` | 0.316 | Shaft turns a typical trunk's trip takes per branch |
| `TypicalThinLogs`, `TypicalThinBranches` | 10, 25 | The typical thin trunk the thin speed is set on (67.9 turns) |
| `TypicalThickLogs`, `TypicalThickBranches` | 25, 61 | The typical thick trunk the thick speed is set on (169.3 turns) |
| `HeadSpeedPerTier` | 0.35 | How much faster it feeds per tool tier above copper's of the heads' metal; 0 makes every metal copper's speed |
| `StickFraction` | 0.5 | Sticks per branch, rounded down over the trunk |
| `DryBarkMultiplier`, `WetBarkMultiplier` | 1.0, 1.5 | Immersive Woodworking's bark roll's chance multiplier, dry and wet |
| `WetBarkCountMultiplier` | 1.5 | A wet log's bark count multiplier, the fraction a chance of one more |
| `HeadWearMultiple` | 4 | The heads' capacity, as a multiple of their metal's bark spud durability |
| `HeadWearPerStoredLog` | 1 | Points the heads lose per stored log, rounded up over the trunk |
| `WaterPerLog`, `ReservoirLitres`, `WaterIntakeLitresPerSecond` | 2, 20, 10 | The drip's litres per log, the reservoir, and the intake |
| `AutoPullFromRack`, `AutoPushToRack` | true, true | Whether it takes trunks from a rack at the infeed end, and puts them on one at the outfeed end |

**How the pace is set.** The class speeds come from turns per stored log and per branch on a
typical trunk of each class, so the two figures read like the mill's `RevolutionsPerStoredLog`, but
every trunk of a class then feeds at that class's one speed (the feed is geared; see the model).
They are tuned so one rosser keeps one bucking mill of the same metal fed: the mill takes
8 × logs / speed + 6 turns per trunk (its cut, then its saws wound up), and the rosser's trip takes
TypicalTurns / speed. With the defaults the rosser is never the slower for a thick trunk (25 to 48
logs), nor for a thin one of 8 logs or more; for smaller thin trunks the mill waits a little. A test
holds this at tiers 2 to 5 against the mill's defaults.

The typical trunks' branch counts are measured. `RosserScenarios.Felled_trees_branch_counts_are_measured`
grows trees with the game's own generators (oak, birch, pine, maple, larch and acacia, three sizes
each) and fells them through Logging Expanded: 18 trees gave 284 logs and 695 branches, about 2.45
branches per log. Most broadleaves and conifers gave 2 to 3 per log, small young trees far more, and
acacia almost none. So the typical thin trunk has 10 logs and 25 branches, and the thick one 25 and
61. The turns per branch were then lowered to 0.316, which keeps the two typical trips at the 68 and
169 turns the gears are drawn for: 10 × 6 + 25 × 0.316 = 67.9 (0.15 % under) and 25 × 6 + 61 ×
0.316 = 169.3 (0.16 % over), well inside the 3 % the gear test allows. The drawn gears are held to the defaults (`feed.gear`,
below): other settings change the pace but not the model, so the selector's dog faces, which are
plain, then show the difference.

## Crafting

The frame: four Immersive Woodworking sawmill frames, one to a corner, a stack of thirty-two nails
and strips in the middle, sixteen support beams above and sixteen below, and two copper chute
sections, with a hammer (`FBF,HNC,FBF`; `assets/seraphhorizons/recipes/grid/rosser.json`): about
three times the mill frame, for a machine four times its size. With `IronWoodworkingMachines` on,
the nails and strips must be iron, meteoric iron or steel (`patches/woodworking-machine-costs.json`),
and each sawmill frame carries its own eight. The parts are existing items: the game's and
Immersive Woodworking's.

## Model

Most of the rosser's model was made for this mod. Its crown disc and its two rectifier pinions are
taken from Immersive Woodworking's sawmill model by Bobrik00, and every other toothed wheel is built
from one of that model's teeth (`../CREDITS.md`). It is generated, not drawn:
`tools/make_shape.py` reads IW's `shapes/block/sawmill/sawmill.json` from
`build/mods/immersivewoodworking_*.zip` and Logging Expanded's trunk models from
`build/mods/LoggingMod*.zip`, and writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/rosser.json` | The whole machine, every moving part and both metals' pipes (1489 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/rosser_frame.json` | The static frame only (277 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/rosser-rig.json` | Footprint, anchors, the trunk path, the feed constants and the part rig (47 parts). |
| `tests/Rosser/rig-reference.json` | Every part's matrix at a grid of poses (197), from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native, south-facing axes (x
west to east along the trunk's travel, y up, z north to south) in voxels, measured from the machine
box's north-west bottom corner. The shipped files are the same model moved so the controller cell,
build cell (15, 0, 2), is `[0,0,0]`: shipped (blocks) = build / 16 − (15, 0, 2).

The trunk moves lengthwise through the machine on one axis, y 27, z 43 (z is the mill's bed
centreline, so a mill in line has its trunk on the same line). Going east:

1. **Infeed bed** (x 0..88): the trunk waits on the infeed cradle's saddle, its nose at x 86, its
   weight on the treadle plate in the saddle (x 62).
2. **Infeed station** (x 95): a driven top roll on swinging arms over an idle bottom roll on the
   cradle.
3. **Limb breaker** (x 104): two iron V bars on a cross pin, hanging in the throat; the trunk's nose
   rides under them and lifts them, and they snap the branches off.
4. **Drip** (x 112.4): the drip's pipes, ppex's 6 x 6 section in the fitted metal. The inlet comes
   in from the water face on the south on the axis of a pipe beyond it (x 120, y 40), between the
   ring's south posts; a riser goes up beside the ring, south of its upper roller (z 72.5); a run
   goes west at y 52 over that roller's bracket; and the header crosses the trunk from z 69.5 to its
   capped north end at z 33, its underside at 49, over the breaker bars a thick trunk lifts, with
   five nozzles under it. Two iron straps of the frame hang it from the top beams.
5. **Ring** (x 118..124, plane x 121): the cutter ring, 3.25 blocks across its teeth, with a bore of
   radius 20.5 that clears a thick trunk's knots (19.45) by 1.05. Its scraper arms reach downstream
   into x 124..140.
6. **Outfeed station** (x 169.5): as the infeed's.
7. **Outfeed bed** (x 168..256): a delivered trunk's tail stops at x 175.5.

Power comes in high on the north side, beside the ring; the gearing runs along the north side and
over the top; the selector levers and the rock shaft are on the south; the chute opens on the south.

**The weighing cradle.** The ring's scrapers need the trunk on the ring's axis for both sizes, and
a fixed support cannot do that: a thin trunk on a fixed support lies with its axis 7.5 voxels lower
than a thick one's. So the bed saddles and both bottom rolls stand on two cradles in vertical
guides, tied by one rocker shaft (low on the north side) with counterweights heavier than a thin
trunk and lighter than a thick one. Empty or thin, the cradles rest on their upper stops; a thick
trunk weighs them down 7.55 voxels to their lower stops. Either way the trunk's axis is at y 27 the
whole way through. The bottom rolls idle; the top rolls are the driven ones, on weighted arms that
swing about the cross shaft that drives them, so they stay in mesh as they rise.

**The drive.** The axle turns IW's crown disc on the entry shaft (along z, with a flywheel). The disc
meshes both of IW's pinions, each loose on the main shaft (along x, y 56, z 29.5) on a one-way catch
of opposite hand: the mill's rectifier, with the input on the disc. Whichever way the axle turns,
the main shaft turns one way, 2.38 times per axle turn. Its ring pinion (14 teeth) drives the
ring's toothed rim (50 teeth). The ring has no axle: it runs on four flanged rollers (two above,
two below) bearing on its two iron tyres. Two worms on the main shaft, one at each station, each
drive a worm wheel (10 teeth) on a lay shaft across the machine.

**The two-speed feed and its selector.** The feed is geared, never slipping, with one speed for thin
trunks and a slower one for thick, changed by the same weight that moves the cradles:

- Each lay shaft carries two change gears, in constant mesh with two gears loose on the cross shaft
  beside it: fast, lay 13 to cross 14, and slow, lay 8 to cross 19 (module 0.7, centre distance
  9.45).
- A **three-position selector dog**, keyed to the cross shaft, has three places: neutral (nothing
  past it turns), on the fast gear (thin) and on the slow gear (thick). The cross shaft drives the
  top roll through a banjo (pivot gear 8, roll gear 20) on the arm.
- **The floating lever** moves the selector. The rock shaft's arm pushes the lever's top pin; its
  fork rides in the selector's groove; and it pivots on a pin on an iron upright standing on the
  cradle. Cradle up (no trunk, or a thin one), the pivot is between the pin and the fork, so the
  push sends the selector onto the fast gear. A thick trunk lowers the cradle 7.55 voxels, the
  pivot drops below the fork, and the same push sends the selector the other way, onto the slow
  gear. Rock out, the lever stands upright and the selector is neutral, whatever the cradle does.

**The rock shaft and its holds.** There is no player control: the trunk runs the cycle.

1. A trunk loaded on the infeed bed presses the treadle plate down (1.5 voxels thin, 9.05 thick).
   The treadle lever lifts a pushrod, which throws the rock shaft in (12°). The extra travel of a
   thick trunk is lost in the pushrod's stirrup, and the rock stops at full throw on a stop.
2. The rock in, both selector levers put both selectors on their class's gear, and the rolls turn
   as soon as the shaft does. The infeed top roll draws the nose into the nip and rises; an iron arc
   on its arm comes under one of the rock shaft's fingers and holds the rock in once the tail has
   left the treadle.
3. The nose reaches the outfeed rolls, whose arm's arc holds the rock too, before the infeed arc lets
   go. (The thin trunk's infeed roll drops slowly behind the tail, over 20 voxels, so the two holds
   overlap.)
4. The tail leaves the outfeed rolls: the last arc passes from under its finger, the rock shaft's
   weight throws it out, the selectors go to neutral and the rolls stop, with the trunk on the
   outfeed bed. Taking it away lets the cradles rise again.

**The scraper arms.** Four iron arms, pivoted on pins tangential to the ring on its downstream face,
at the ring's top, north, bottom and south, reach downstream and inward; their tips are the spud
heads. Torsion springs on the pins close them until their tails rest on the ring's face; a trunk's
nose pushes them open, and the tips ride its surface as the ring turns. Logging Expanded's trunk
sections are rounded squares (the `xxl` model's flats are 15.05 voxels from the axis and its
corners 18.5; `lg` 7.5 and 9.0), so each arm also swings with the ring's turn to follow the corners:
a series of harmonics fitted to the arm's real geometry against the section (1, 2, 3, 4, 6 and 8,
sine and cosine), which makes 13 gauge drivers per arm.

Every moving part, as built (requires in brackets; input θ the axle angle, ψ the shaft's rectified
travel, φ the feed, T the trunk's travel, k its class, p its presence):

| Part (rig id) | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft, flywheel, crown disc (`entry`) [shaft] | The vanilla axle on the power face | Both pinions | `rotate` z, ratio 1 (θ) |
| Rectifier pinions with their catches (`pinion_w`, `pinion_e`) [shaft] | The crown disc, always, opposite ways | The main shaft, through whichever catch bites | `rotate` x, ratio ∓2.38 (θ) |
| Main shaft, collars, ring pinion, both worms (`main`) [shaft] | The biting pinion | The ring's rim, both worm wheels | `rotate` x, 2.38 (ψ) |
| Cutter ring: body, rim teeth, straps, arm lugs, pins and springs (`ring`) [ring] | The ring pinion | The arms (carried); the rollers (through the tyres) | `rotate` x about the trunk's axis, −0.667 (ψ) |
| Tyres (`ringtyre`) [tyres] | Ride the ring | The rollers | rides `ring` |
| Rollers (`roller1`..`roller4`) [ring] | The tyres | (carry the ring) | `rotate` x about each pin, 5.34 (ψ) |
| Scraper arms (`arm1`..`arm4`) [ring] | Their springs close them; the trunk's nose opens them; its section moves them as the ring turns | The tips | 13 `gauge` rotations (rise and drop windows per class, harmonics); ride `ring` |
| Scraper tips, the spud heads (`tip1`..`tip4`) [heads] | Ride their arm | (scrape) | rides `armN` |
| Limb breaker V bars (`breaker`) [breaker] | Gravity; the trunk lifts them | (break the branches) | `gauge` rotate z (windows per class) |
| Lay shaft, worm wheel, change gears (`lay_in`, `lay_out`) [rollsin, rollsout] | The worm, always | The loose cross gears | `rotate` z, 0.238 (ψ) |
| Loose fast and slow gears (`fast_*`, `slow_*`) [rollsin, rollsout] | The lay gears, always | The selector, when it is on them | `rotate` z, −0.221 and −0.100 (ψ) |
| Selector dog (`sel_*`) [rollsin, rollsout] | Slid by its lever's fork; keyed to the cross shaft | Locks the class's gear to the cross shaft | `gauge` slide z (thin +0.8, thick −0.55 voxels, the rock's windows); `rotate` z −1 (φ) |
| Selector lever (`sellever_*`) [levers] | The rock shaft's pin (push); the cradle's fulcrum (which way) | The selector | two `gauge` rotations x, about the fulcrum up and down, one per class, on the rock's windows |
| Cross shaft, banjo pivot gear (`cross_*`) [rollsin, rollsout] | The selector | The banjo | `rotate` z −1 (φ) |
| Top-roll arms, weights, tie, hold arc (`toparm_*`) [rollsin, rollsout] | Their weight presses down; the trunk lifts them; at rest on their stops | The top roll (carried); the hold arc | `gauge` rotate z about the cross shaft (rise and drop windows per class) |
| Top roll and banjo roll gear (`toproll_*`) [rollsin, rollsout] | The banjo | The trunk (no slip) | `rotate` z 0.4 (φ), plus the epicyclic `gauge`; rides `toparm_*` |
| Cradle: skids, bearers, tongues, cheeks, fulcrum upright (`cradle_*`) [none] | Counterweights hold it up; a thick trunk takes it down | The bottom roll; the lever's fulcrum | `gauge` slide y −7.55 voxels, `present`, thick only |
| Bottom roll (`botroll_*`) [rollsin, rollsout] | The trunk rolling over it | — | `roll` (T); rides `cradle_*` |
| Rocker shaft, arms, counterweights (`rocker`) [none] | The cradles (a thick trunk's weight) | Ties both cradles; the counterweights lift them back | `gauge` rotate x, `present`, thick only |
| Treadle plate and stem (`treadle`) [levers] | The waiting trunk's weight | The treadle lever | `gauge` slide y −1.5 / −9.05 voxels (treadle window) |
| Treadle lever, axle, tail roller, counterweight (`treadlever`) [levers] | The plate (down); its counterweight (back up) | The pushrod's stirrup | `gauge` rotate x (treadle window) |
| Pushrod with its stirrup (`pushrod`) [levers] | The lever's tail roller, up to the rock's stop; gravity | The rock's tappet | `gauge` slide y, gain 6.03 for thick (the lost motion) |
| Rock shaft: tappet, lever arms and pins, hold fingers, throw-out weight (`rock`) [levers] | Thrown in by the pushrod; held by either arc; thrown out by its weight; stopped at full throw | Both selector levers | `gauge` rotate x 12°, windows: the treadle, then a rise and a drop window per station and class (max) |
| Drip pipes: inlet, riser, run, header and nozzles (`pipecopper`, `pipelead`) [pipecopper, pipelead] | Static; only the fitted metal's is drawn | — | none |
| Frame (`frame`) | Static | — | none |

Toothed wheels and what they mesh (nothing toothed meshes nothing; the selector, flywheel, rollers,
rolls and collars are plain):

| Wheel | Meshes with |
|---|---|
| Crown disc | Both pinions |
| Pinions | The crown disc |
| Ring pinion (14) | The ring's rim (50) |
| Worm (single start) | Its worm wheel (10) |
| Lay fast (13) / slow (8) | Cross fast (14) / slow (19), in constant mesh |
| Banjo pivot gear (8) | Banjo roll gear (20) |

**The two-speed change's visible causes**, both ways:
- in, thin: treadle → pushrod → rock in → the lever turns about its upper fulcrum → selector onto the
  fast gear;
- in, thick: the trunk's weight drops both cradles (the rocker), which carries the fulcrum below the
  fork → the same push puts the selector onto the slow gear;
- out: the last roll drops → its arc leaves the finger → the rock's weight throws it out → the lever
  stands upright → neutral;
- back to thin: the trunk is taken away → the counterweights lift the cradles → the fulcrum returns
  between pin and fork.

**Gear ratios and the pace.** Per axle turn the main shaft turns 2.38 (IW's peg rings, 6.05 / 2.54,
as the mill), the ring 0.667 (14 / 50 of that), and the lay shafts 0.238 (a tenth). The cross shaft
then turns 0.221 (fast) or 0.100 (slow) per axle turn, and the top roll 0.4 of the cross shaft. A top
roll's radius is 4 voxels, so the trunk travels 0.1 blocks per radian of the cross shaft: the rig's
`feed.blocksPerRadian`. The feed input φ is defined by the trunk, not by the shaft: the renderer grows
φ by ΔT / `blocksPerRadian` (`RosserVisuals.FeedAdvance`), so everything on `"input": "feed"` turns
exactly with the trunk for both classes, every metal and every setting, and never runs back. The
drawn ratio between the shaft and φ is `feed.gear` (thin 0.221175, thick 0.10029 feed radians per
axle radian); against the gameplay's pace at the default settings and copper heads it is −1.5 % thin
and +0.5 % thick, and a test holds it within 3 %. Above copper the heads' tier makes the feed faster
than the gears say: that difference is at the selector's plain dog faces, where no teeth show it.

### Regenerating

```sh
python3 tools/packtool.py fetch                                     # puts IW's and Logging Expanded's zips in build/mods
python3 mods-src/seraphhorizons/Rosser/tools/make_shape.py          # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Rosser/tools/make_shape.py --out DIR   # or writes them into DIR instead
python3 mods-src/seraphhorizons/Rosser/tools/make_shape.py --quick     # skips the slow checks (below)
```

A full run takes about 90 s. The generic half of the script is `Machines/tools/machinegen/`, shared
with the mill's generator; `tools/validate_rosser.py` holds the rosser's checks. The output is
deterministic: two runs into two folders must be `diff -r` identical. The reference poses
(`tests/Rosser/rig-reference.json`) are the rig's own maths at a grid of θ, ψ, φ, T (every window's
edges and mid-ramps, for each class), k and p; the site's tests (`site/test/rosser.test.ts`) and
`tools/tests/test_rosser_model.py` replay them.

Every run checks its output and exits non-zero if a check fails. `--quick` skips the z-fighting
fix, the scraper arms, the clearances and the swept paths. Checks marked *(review)* were added
because a review of the renders found a fault:

- **Parts:** the Euler round trip; every element in the part it was built for (counts printed); no
  duplicate names, no empty part; the glob traps (`ring_` against `ringtyre` and `ringpinion`,
  `rock_` against `rocker_`, `toproll_` against `toparm_`, no frame name matching a moving glob).
- **Logging Expanded's models:** the `lg` and `xxl` sections' flats and corners, measured from the
  zip, match the rig's radii, so a change to Logging Expanded's trunks fails the run.
- **Containment:** every element at rest in a declared cell; nothing leaves the box over the sampled
  poses.
- **Anchors:** the power and water cells are cells, not at ground level, on opposite faces, with the
  cells beyond them outside the footprint and away from the racks' cells and the chute; the entry
  shaft reaches the power face, and the drip's inlet reaches the water face on the middle of the
  cell's face in ppex's 6 x 6 section, so a pipe there meets it end to end; a mill in line keeps its
  power feed cell, and both trunk heights are printed.
- **Pipes:** each metal's pipes are the first metal's elements exactly (the same boxes and faces,
  renamed, in their own texture), made after the z-fighting fix so they get its insets too; the
  header spans a thin trunk's width. Only one metal's are ever drawn, so the z-fighting, clearance
  and swept-path checks see copper's alone.
- **Gearing:** every meshing pair (ring pinion and rim, rollers and tyres, both change pairs, the
  banjos at thin and thick, the worms by lead and centre distance) has tangent pitch circles, and its
  contact points move together, by finite differences of the posed rig, with the shaft turning either
  way. The rectifier: for either sign of θ exactly one pinion turns with the main shaft.
- **Selector on its gear:** per class, the selector's dog face and its gear move together at the drawn
  ratio.
- **No slip:** the top rolls' undersides move exactly with the trunk, and the bottom rolls' tops.
- **Pace:** `feed.gear` within 3 % of the gameplay's pace at the defaults; thick slower than thin.
- **Ring:** concentric with the trunk path; the bore clears the knots by 1.05; each roller tangent to
  its tyre at 16 ring angles; no gap of 180° or more between rollers; the flanges straddle the tyre
  band.
- **Scraper tips:** over 72 ring angles, each tip on the trunk's surface (pressing in at most 0.3,
  standing off at most 0.8) for both classes; the arm bodies at least 0.5 outside it; the nose riding
  under them pressing in at most 0.3.
- **Rolls:** top and bottom rolls and the saddles bear on both classes' flats; a top roll at rest is 2
  below a thin trunk's top; a nose lifting a top roll presses in at most 0.3.
- **Cradle and rocker:** the rocker's tip rollers bear under the cradles at both stops, with the
  counterweights on the lifting side.
- **Treadle, pushrod, levers and rock:** the plate 1.5 proud empty and flush under both classes; the
  pushrod on the tappet while the treadle holds the rock, never pressing into it after; the rock's
  pin on the lever's slot, the fulcrum on its pin, the fork in the groove; the dog faces meeting the
  fast gear's hub (thin) and the slow gear's (thick), neutral clear of both; the throw-out weight on
  the out side over the whole throw; each finger resting on its raised arc and clear of it at rest;
  the rock fully in for the whole trip of both classes and out exactly at T_end.
- **Breaker:** resting on both classes; the nose lifting it presses in at most 0.3; at rest it hangs
  below a thin trunk's top.
- **Nothing floats:** all 277 frame elements joined to the ground.
- **Supports and bearings:** every shaft in at least two bearings (main shaft 7, entry 2, rock 4,
  rocker 2, breaker pin 2, treadle axle 2, each lay and cross shaft 2, each roller pin 2), each
  bearing enclosing its shaft at rest and turned 45°, loose wheels located on both faces.
- **Textures** *(review)*: iron for the wearing surfaces and linkage (feed rolls, tips, worms,
  selectors, levers, pins, the drip's straps), oak for structure, each metal's pipe texture on that
  metal's pipes and nowhere else.
- **Chute** *(review)*: the boards fall to the south face under the anchor, and the mouth is clear.
- **Stops** *(review)*: the top-roll arms rest on their brackets; the rock's tappet meets its stop
  when in and clears it when out.
- **No z-fighting:** no coplanar overlapping faces at rest and mid-trip, thin and thick, after
  `fix_coplanar`.
- **Clearances:** over 117 poses (θ, ψ, φ, every window edge for both classes, the load ramps) no two
  parts touch except the listed intended contacts.
- **Swept paths:** each class's real Logging Expanded model, every 1/16 block from waiting to
  delivered, touches only its intended contacts (rolls, saddles, the treadle plate, the tips and arms,
  the breaker bars), clears everything else by 0.1, and stays in declared cells.
- **Files:** every texture declared; the shipped model is the checked one moved; `[0,0,0]` is a cell.

The cells' boxes are rebuilt from the shipped, rounded shape posed by the shipped rig, unlike the
mill's, so every reader (the Python test, the site's test) gets the same split.

### How it derives from IW's sawmill

- **Taken as is:** the crown disc (`Rotor_default_3_011..014` and `017..027`, as
  `entry_Rotor_default_3_*`) and the two pinions (all of `Rotor_default_1_*` and
  `Rotor_default_2_*`, as `gear_pinion_w_*` and `gear_pinion_e_*`).
- **The tooth:** every `*_iwtooth*` and `*_iwthread*` element (the ring's rim, the ring pinion, the
  worm wheels, the change gears, the banjo gears and the worms' threads) is a box carrying the faces
  and UVs of `MainRotor_twoway_013.001`, IW's main rotor flange peg, squared up.
- **Templates, faces and UVs only**, as the mill uses them: `Frame.011` (posts), `Frame.119`
  (beams), `Frame.088` (bearing blocks), `sash_001` and `leveler_metal_static_003` (iron),
  `MainRotor_twoway_001/002` (the cross-profile shafts: entry, main, lay and cross),
  `MainRotor_twoway_021` (gear bodies, collars, the flywheel, roll bodies).
- **Not used:** IW's spring, crank and ratchet.

Texture codes are `oak`, `metal`, `pipecopper` and `pipelead`. The tips use `metal`, which the
renderer swaps for the heads' metal; everything else that is `metal` stays the block's iron plate.
Iron for the wearing surfaces and linkage, oak for structure, as the mill's rule. The pipes wear
`pipecopper` (`game:block/metal/sheet-plain/copper4`) or `pipelead` (`lead4`), the textures
`UnifiedPipes` gives ppex's copper and lead pipes. The frame block (`blocktypes/rosser/frame.json`)
declares all four: the renderer draws the moving parts with the block's textures, and a code the
block lacks would render white (`tools/tests/test_rosser_model.py` holds them together).

### Rig schema (`rosser-rig.json`)

Everything is in the native frame, in blocks, with the controller at `[0,0,0]`. It is the mill's
schema (`../BuckingSawmill/README.md`, "Rig schema") with these differences, parsed by
`Core/RosserRig.cs` (a broken file is logged and the rosser cannot be placed):

| Field | Meaning |
|---|---|
| `cells` | As the mill's (`lid` included), plus `"hollow": true` on a cell with no boxes of its own: a ghost, solid only where the trunk's box is. A hollow cell with `boxes` is an error; any other cell with none is still a full cube. The controller's cell cannot be hollow. A hollow cell may have a `lid`; it counts as 1 high for the deck's height. |
| `powerCell`, `powerFace` | `[-9,3,-2]`, `north`: a ghost cell, not hollow, whose face looks out of the footprint. |
| `waterCell`, `waterFace` | `[-8,2,2]`, `south`: the same rules, and not the power cell. |
| `infeedSide`, `outputSide` | `west` and `east`: they must be the path's ends (for a path along z, `north` and `south`). |
| `chute` | `{ "pos": [-6.5, 0.1125, 3.05], "side": "south" }`: where bark and sticks spawn, and the face they are pushed out of, which must be opposite `powerFace`. The top-level `chuteSide` is for the site's viewer only. |
| `trunkPath` | `origin` `[-15, 1.6875, 0.6875]`, `axis` `x`, `length` 16 (the line the viewer draws), `nose0` −9.625, `lengths` `{thin 4, thick 5}`, `tailStop` −4.03125, `radius` `{thin [7.5, 9.0], thick [15.05, 18.5]}` (flats and corners, voxels, for drawing), and `stations` along the axis: `treadle` −11.125, `infeed` −9.0625, `breaker` −8.5, `drip` −7.975, `ring` −7.4375, `outfeed` −4.40625. `breaker` and `ring` are required, with `nose0` < `breaker` < `ring` ≤ `tailStop`, so every stick and log's bark is due by the end of the trip. `tips` `{thin, thick}` (optional; the ring's position without it) is where the spud heads touch a trunk of each class along the axis: the tip block's centre with the arm at the class's mean opening, downstream of the ring's plane and further for the thick trunk; `ring` ≤ `tips` ≤ `tailStop`. The bark switch, the scraping sound and the chips are there, not at the ring's plane. |
| `feed` | `blocksPerRadian` 0.1 (trunk travel per radian of φ) and `gear` `{thin 0.221175, thick 0.10029}` (the drawn change gears, feed radians per axle radian; thick below thin). |
| `parts` | As the mill's, with the new inputs and drivers below, and the rosser's `requires` vocabulary: `shaft`, `ring`, `tyres`, `rollsin`, `rollsout`, `breaker`, `levers`, `pipecopper`, `pipelead`, `heads`, or null (`RosserRequires.KnownRequires`; a metal's pipes are drawn while the pipes fitted are that metal). |

The rosser's `trunkPath` is the trunk-flavoured case of a rig's generic **work** (a named progress
quantity with a unit, a step and an end per class; `docs/recipe-browser/models.md`, "The work and its
drivers"): its keys and this rig are unchanged, and T below is the work W, in blocks (`"trunk"` is W's
trunk-flavoured spelling, in a driver's `input` and a pose).

**Inputs.** The mill's θ, depth, lifting and ψ, plus four:
- T, the trunk's travel (blocks), 0 to T_end(k);
- k, its class: 0 none, 1 thin, 2 thick (`TrunkBox.ClassOf` of the stack's size);
- p, its presence, 0..1: eased in on the client (`RosserVisuals.EasePresence`, rate 12 a second)
  while a trunk is loaded and out when not, with k held at its last value until p reaches 0, so
  parts ease back rather than jump;
- φ, the feed's travel (radians), grown by ΔT / `feed.blocksPerRadian`, never decreasing.

**Drivers.** Every addition is opt-in, so the mill's rig and reference poses do not change:

| Driver | Parameters | Motion |
|---|---|---|
| `"input"` on `rotate`, `slide`, `swing` | `"theta"` (default), `"travel"` (ψ), `"feed"` (φ), `"trunk"` (T) or `"oil"` | The driver's angle or offset with that input in place of θ. `"rectified": true` is the mill's way of writing `"travel"`; a driver with both keys is an error, as is `input` on any other driver except `stretch`. `"oil"` is how full the machine's oil tank is, 0..1 (added for the gear cutter's sight-feed cup; `RigInput.Oil`, default 0). |
| `"input"` on `stretch` | `"depth"` (default) or `"oil"` | The stretch scales by (`length` + `travel` × that input) / `length`. Anything else is an error. |

| `gauge` | `motion` (`slide` or `rotate`), `axis`, `pivot` (rotate), `amount: {thin, thick}`, `mode` (`occupy`, the default, or `present`), `windows: [{from, to, ease, gain: {thin, thick}}]` (occupy), `lobes: {ratio, phase, amplitude: {thin, thick}}` (rotate only) | A motion set by the trunk at a place. With no trunk e = 0; `present`: e = p; `occupy`: e = p × the most engaged window's min(1, gain[k] × occupancy), occupancy = clamp((nose − from) / ease, 0, 1) × clamp((to − tail) / ease, 0, 1). It moves by amount[k] × e, plus e × amplitude[k] × cos(ratio × ψ + phase). A window's gain defaults to 1; windows combine by max (held in by any contact), not sum. |
| `roll` | `axis`, `pivot`, `at` (a place on the path), `ratio` (radians per block) | An idle roller the trunk turns: by ratio × clamp(nose − at, 0, L_k) while k > 0, the identity without a trunk. It ignores p. |

Parse errors (the same in Python, C# and TypeScript): `ease` ≤ 0, `to` ≤ `from`, an `amount` without
both classes, an `occupy` gauge without windows, `lobes` on a slide or without `ratio`, an unknown
`motion` or `mode`, a rotating gauge or a roll without `pivot`, a roll without `at` or `ratio`, and a
roll or `occupy` gauge in a rig without a `trunkPath`.

### Rendering

`RosserRenderer` (client only, made by `BERosser` only while its block is the rosser, disposed on
removal and unload, and by the client tick when the block is gone) draws everything that moves.
The static frame is the block's own shape, `rosser_frame.json`, drawn in the chunk mesh; the
renderer skips parts with no `requires`, ride or drivers.

- One mesh per moving rig part from `rosser.json` (`Machines/Game/MachineMeshes.cs`), drawn with its
  rig matrix (`RigParts.Matrices(RigInput)` in `Machines/Core/RigAnimation.cs`) turned to the facing,
  only when its `requires` is fitted. The `heads` parts take `game:block/metal/ingot/{metal}`. The
  pipes are drawn as they are, in the block's pipe textures: `pipecopper` while copper pipes are
  fitted, `pipelead` while lead ones are.
  The meshes are one tessellation of the shape split by part (each element's `JointId` set to its
  part + 1, read back per vertex from `TesselateShapeWithJointIds`' `CustomInts`;
  `MachineMeshes.PartMeshes`), cached for the session by `MachinePartMeshes` and shared by every
  rosser: the plain parts once, the tips once per head metal. The cache disposes them when the client
  leaves the world; a renderer disposes only its trunk's segments.
- The opaque pass and both shadow passes; nothing beyond 64 blocks from the ring; light from the cell
  above the highest cell over the ring's station, outside the model.
- θ from the power ghost's `AngleRad` (`NativeShaftAngle` about z), accumulated; ψ adds up its size;
  T is the block entity's per-frame interpolated travel; φ grows with T; p eases; k is held while p
  eases out. Nothing is hard-coded from the shape or the rig.
- **The trunk** is Logging Expanded's clean and debarked blocks of the shown size and wood,
  tessellated and cut into one-block segments along its length (`MachineMeshes.Segments`). A segment
  is drawn debarked once its centre is past the spud heads (`trunkPath.tips` for its class, where the
  heads hide the change), and every segment once delivered. Branches are not drawn. If a mesh is not
  made of four-vertex faces it cannot be cut, and the whole trunk changes as it passes the heads.

### Editing by hand

If you edit the shapes in VS Model Creator, keep the element names: the rig finds its parts through
them (first-match globs, in this order).

| Prefix | Part |
|---|---|
| `entry_*` (`entry_shaft_*`, `entry_flywheel_*`, `entry_Rotor_default_3_*`) | `entry` |
| `gear_pinion_w_*`, `gear_pinion_e_*` | `pinion_w`, `pinion_e` |
| `main_*`, `worm_in_*`, `worm_out_*`, `ringpinion_*` | `main` |
| `ringtyre*` | `ringtyre` |
| `ring_*` | `ring` |
| `roller<n>_*`, `arm<n>_*`, `tip<n>_*` | `roller<n>`, `arm<n>`, `tip<n>` |
| `breaker_*` | `breaker` |
| `lay_<s>_*`, `fast_<s>_*`, `slow_<s>_*`, `sel_<s>_*`, `sellever_<s>_*`, `cross_<s>_*`, `toproll_<s>_*`, `toparm_<s>_*`, `botroll_<s>_*`, `cradle_<s>_*` (s `in` or `out`) | the part of the same name |
| `rocker_*`, `treadle_*`, `treadlever_*`, `pushrod_*`, `rock_*` | the part of the same name |
| `pipecopper_*`, `pipelead_*` | `pipecopper`, `pipelead` (the drip's pipes, one copy per metal) |
| `fr_*` | `frame` (every frame element) |

Hand edits are lost when the script runs again. Either port them into `make_shape.py`, or stop
regenerating.

## Tests

- `tests/Rosser/` (part of the mod's unit tests, `dotnet test mods-src/seraphhorizons/tests`, no game
  needed) compiles `Core/` and tests: the rig parser (its own fixture,
  `tests/Rosser/fixtures/rosser-rig-test.json`, hollow cells, anchors, both ends' neighbours, 24
  broken rigs, and the shipped `rosser-rig.json`, whose `feed.gear` must match the default pace within
  3 %); the parts rules (recognition, quantities, the ring first, heads as a set of four, the pipes
  as four of copper or lead with a pipe's click passing to placement once they are in, the build
  without them when they do not exist and a save from before them, the metal rule and `WithMetals`,
  the requires vocabulary and each metal's pipes drawn only with that metal, the creative order, wear
  and capacity, unworn heads out, breaking returns, save and restore); the pace (class speeds, tiers, settings, the feed ratio, and one rosser keeping
  one mill of the same metal fed at tiers 2 to 5); the trip (every stick and log's bark dropped once
  and delivered once, at several step sizes and trunk shapes; the thresholds; nothing twice across a
  save and reload at several points, and a save without counters; breaking per T; states, load
  verdicts, rack offers, take-back; every info state's lang line); the water (filling, ten logs per
  reservoir, wet counts with an injected random source); the visual values (presence easing, the
  held class, the client's estimate, and φ over a whole trip turning the rolls exactly as far as the
  trunk goes); and the config.
- `tests/Machines/` tests the shared code: `DriverFixtureTests` replays
  `tests/Machines/driver-fixture.json` (every driver alone, a composed rig with ride chains, and
  the drivers every parser must refuse), and `MachinesCoreTests` the shaft's sign along x and z on
  every facing, the trunk path, hollow cells and the trunk's box on the path. `TrunkVariantsTests`
  tests the trunk code and variant rules (`../Core/TrunkVariants.cs`).
- `RosserRigTests.The_shipped_rig_matches_the_python_reference_poses` replays every pose of
  `tests/Rosser/rig-reference.json` (θ, ψ, φ, T, k and p) through `RigParts` against the shipped rig,
  at the mill's tolerance. `site/test/rosser.test.ts` replays every pose in TypeScript and rebuilds the cells' boxes from the shipped shape;
  `tools/tests/test_rosser_model.py` (no fetched mods needed, so CI runs it) replays every fifth pose
  in Python and checks the parts and their globs, the cells rebuilt from the shape, the anchors, the
  trunk path against the generator's constants, the feed constants against the drawn gears, the
  gears' pitch and centre distances, and the pipes: the same elements in each metal, each in the
  texture `patches/unifiedpipes-ppex.json` gives that metal's ppex pipes, the inlet on the water
  face's middle, and the frame block declaring every texture code of the shape. `tools/tests/test_machinegen.py` tests the shared generator
  package and that the driver fixture is what its maths writes.
- `tools/make_shape.py` checks its own output every time it regenerates the model.
- `tests/PackTests/RosserScenarios.cs` (Atlas, part of `WoodworkingRosserScenarios`, its ModConfig seeded
  from `tests/PackTests/fixtures/buckingsawmill`, where `RevolutionsPerStoredLog` is 0.2 and
  `RevolutionsPerBranch` 0.08, so a trip takes a few turns) loads this build with every locked mod.
  Each scenario builds on a floor of its own high above the ground.
  - Loading and placing: the rosser loads cleanly with its blocks, recipe, part items and ppex
    binding; a player looking each way places one that extends away, with every ghost; breaking it
    clears the cells and drops the frame and parts.
  - Assembly follows the rules, and in creative mode Ctrl + right-click fits the next stage, one per
    click, in order. The pipes go on as four copper or lead straight pipes from one stack (iron, or
    three, are refused), and once they are in a pipe in hand is not the rosser's.
  - Power, on every facing: the power face alone takes an axle (an axle against any other face turns
    nothing), the shaft turns as the axle beside it is drawn, and the trunk feeds forward with the
    network turning either way.
  - Water: a ppex pipe placed against the water face stays and does not leak; filled through its
    network, it fills the reservoir, and wet acacia gives all tan bark, 4 or 5 pieces a log, where a
    dry rosser gives exactly 3. A pipe against any other face is not held.
  - Trips on a creative rotor: thin and thick, branchy and clean trunks give their sticks
    (floor(branches × 0.5)) and three pieces of bark per log at the chute, come out as the debarked
    trunk with their logs, other contents and the mark, at their class's rate, and wear the heads a
    point per log. The block info and help follow each state: empty, waiting, feeding, stalled when
    the shaft stops, delivered.
  - Spent heads: the rosser stops until four new heads go on, but the trunk that spent them still
    leaves, onto a rack at the outfeed end or to a mill in line.
  - Racks: racks placed by a player against every cell at both ends, on every facing, feed it and
    take from it; a debarked trunk, or one with no logs, waits on the infeed rack; a full outfeed
    rack leaves the trunk on the bed, nothing lost, until there is room. With `AutoPullFromRack` off
    nothing is taken, and with `AutoPushToRack` off the trunk waits for a hand; the block info says
    both.
  - Breaking mid-trip, at each stage of the trip, gives the trunk as far as it got (as loaded,
    debranched or debarked) and the parts, and running that trunk again gives nothing twice.
  - The trunk's boxes follow it on every facing, fill it and no more, hollow cells hold only the
    trunk (and, for collision, their lids), and a click on the trunk is the rosser's.
  - The station's top is a deck on every facing, empty and with a thick trunk half way: every
    station column's top cell has its lid in its collision boxes and not in its selection boxes,
    and the collision boxes reaching the lid's height cover the whole cell; no bed cell has a lid in
    the rig, and no bed column's top cell collides with one.
  - The mill in line, both placed by a player on every facing: the mill takes the trunk at the top of
    its saws' cycle and cuts debarked logs; breaking the rosser before the mill takes it drops that
    trunk once. A mill in line wins over a rack beside it, but not while the mills'
    `AutoPullFromRack` is off: then the trunk goes to the rack.
  - Saving: a rosser unloaded and loaded again mid-trip keeps its travel and what has dropped, and
    mid hand-off keeps the trunk until the mill takes it once. The test saves the world
    (`/autosavenow`) before it unloads the chunk column: an unsaved column comes back as it was
    generated.
  - One scenario only measures felled trees' branch counts (above, "How the pace is set").

  `tests/PackTests/MillFeederScenarios.cs` tests the mill's side of `ITrunkFeeder` with a stub, and
  `tests/PackTests/DebarkedTrunkScenarios.cs` the debarked trunk (the mod README), including its
  bark and end textures. With the switch off, `SwitchesOffScenarios` requires no rosser blocks, no
  recipe, no debarked trunk and nothing logged.

## Known weak spots, and what is not checked in the game

Nothing client-side of the rosser has been run in the game: the renderer, the trunk's segments,
particles, sounds, help icons, item transforms and the feel of the hitboxes are compiled but
unseen. Before release, play it through the modelling guide's list
(`docs/modeling/pitfalls.md`, "What no check covers"), and in particular:

- the moving parts' textures (no white question marks), the tips in the heads' metal, the drip's
  pipes in copper and in lead, and the inlet meeting a ppex pipe on the water face;
- a pipe placed against the water face by a click once the pipes are in, and by a sneak-click
  before;
- the shaft's direction on every facing against a vanilla axle (unit-tested and checked in Atlas
  against the axle's own angle, but not seen);
- the trunk's segments changing from bark to debarked inside the ring, and a delivered trunk wholly
  debarked;
- walking into and clicking the trunk at every point of its trip, and hovering the hollow cells
  (their selection box list is empty when the trunk is not in them);
- walking on the top (Atlas checks the lids' boxes, not a player on them), and stepping from the
  station's deck down onto the beds;
- the item in hand, on the ground and in the handbook (the transforms are guesses);
- how the busy frame reads at play distance.

Also not checked: the rosser without Pipes and Power Expanded. The pack always loads it, so Atlas
never sees the dry fallback's "no ppex" path; it is a few lines (`PpexWater.Bound`) and runs dry.
Nor the rosser built without pipes (ppex missing, or `UnifiedPipes` off): the unit tests hold the
parts rule (`RosserParts.PipesNeeded`), and Atlas only ever has the pipes.
And water from a real pump: the Atlas pipe is filled through its network directly.

Known compromises in the model and gameplay:

1. **Tier factor.** Above copper, the trunk travels faster than the drawn gears say, by the heads'
   tier factor; the difference sits at the selector's plain dog faces.
2. **Settings and the model.** Changing `RevolutionsPerStoredLog`, `RevolutionsPerBranch` or the
   typical trunks changes the pace but not the gears, in the same way.
3. **Load transients.** While p eases in (about 0.1 s) the lever's fulcrum moves with p but the lever
   turns about the thick fulcrum, so its pin can sit up to about 0.3 voxels off the slot. The levers
   are checked only at p = 1.
4. **Treadle lever.** It turns up to 37° for a thick trunk; the plate's stem rides it within 0.29
   voxels (a linear gauge against an arc).
5. **Roll drops.** The thin trunk's infeed roll drops slowly behind the tail (over 20 voxels) so the
   holds overlap; the outfeed roll drops in 1.5 for a thick trunk so the trip ends inside the box. A
   delivered thick trunk is 0.5 voxels from the east face.
6. **Bottom rolls** (`roll`) snap back when the class goes to none after unloading. Parts on φ never
   jump.
7. **Close contacts.** The arms on a thick trunk stand off up to 0.72 voxels (the rule is 0.8), and
   the arm bodies clear it by 0.55 (the rule 0.5).
8. **Renderer cost.** About 47 part meshes (shared by every rosser, from one tessellation per
   texture variant), 13 gauges per arm, and ten trunk segment meshes per rosser; the box table is rebuilt every 1/16 block of travel. Not profiled.
9. **Not built from the design:** fixed side rails along the beds.
10. **Hand-off height.** A trunk drops about 4 voxels (thick) or 11.5 (thin) as the mill takes it.
11. **The rack draws debarked trunks with bark**: it draws stored trunks from placed log meshes
    of its own, not from the trunk block.
12. **Pace.** Small thin trunks (under 8 logs) feed slower than the mill cuts them. The typical
    trunks' branch counts come from 18 generated trees; real worlds vary.
13. **The pipes' cells.** The cells' boxes come from the whole model at rest, the pipes included, so
    the boxes over the drip's pipes are there before the pipes are fitted, as every part's are.
