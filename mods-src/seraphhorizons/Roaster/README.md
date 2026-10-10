# Roaster

Part of the Seraph Horizons mod (`../README.md`): the ore line's roasting stage (epic #684), one of its
upgradeable machines (#711). It roasts sulfide concentrate into roasted concentrate, giving off sulfur
(behaviour #736: level 1, the stall roaster, 92 %, where sulfur starts; level 2, the reverberatory, chute-fed,
100 %). This folder holds the model's generator (`tools/`) and this record of it (#737). **There is no
gameplay yet**: no block, block entity or renderer reads these files; the rig is written as the contract the
gameplay will be built against.

**One machine, upgraded in place** (#711): the frame is placed at its final size, then level 1's parts, then
level 2's. Nothing that connects to the outside moves between levels: the power cell, the hopper (the infeed),
the output spout, the flue chamber with its sulfur door and the stack are the frame's or are kept from level 1.

| Level | Tier | What it is |
|---|---|---|
| The frame | | A stone plinth under the whole footprint (5 voxels) with the **discharge pit** sunk in it east of the middle and a tunnel to the **spout** on the east face; the **flue chamber** at the cold end (brick, its throat in its north wall, a **sulfur door** on its east face); the **stack** on it, brick with a stone cap and two iron bands, five blocks to its top. |
| 1 | water | A **stall roaster**. Three open-fronted stalls in rubble masonry against a back wall on the west, opening east onto a forecourt where the pit is. Each stall's flue rises in the back wall from a mouth at its foot to the brick **collecting flue** on the wall's top, which a **header** at the cold end carries into the flue chamber's throat (closing it), so the stalls' fumes pass through the chamber, where the sulfur condenses, and up the stack. The ore is heaped in each stall in courses on a bed of firewood. An iron **hopper** at the infeed cell, on an oak **trestle** standing on the header, feeds the southern stall down a trough; the roasted ore is raked into the pit. No power. |
| 2 | mill | A **reverberatory roaster, chute-fed**. The stalls, their flue and the trestle are pulled down; the **hopper stays where it is**. A long low furnace on the same plinth: red-brick side and end walls, a firebrick hearth and bridge wall, a segmental firebrick arch on firebrick skewbacks, held by iron buckstays with tie rods over the arch and skewback bars. The **firebox** at the fire end burns on a **step grate** of four iron treads stepping down to the bridge, under the throat of a **coal bunker** on the fire end that a chute keeps filled: the coal slides down the steps as it burns, so there is no stoking door. The flame passes over the bridge, along the hearth and into the flue chamber's throat. Two **rabbles** turn in the hearth on spindles through glands in the arch's crown, each driven by a **bevel wheel** from a **bevel pinion** on the **line shaft** on the roof, which continues the vanilla axle; they plough the charge from under the feed chute to the **discharge hole** by the bridge, over the pit. A **fluted feed roll** keyed on the line shaft's end turns in a casing under the hopper's slot and meters the charge down the **feed chute** through the arch. The **work doors** beside each rabble, both sides, stand open, their plates raised in their guides. |

Roasting is dry: the roaster takes **no process water** at either level, so it has no pipe inlet (level 1 is a
water-tier machine only in the global tier's sense: fed by the player, poor ore worth roasting).

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, unchanged by this machine (no new driver: every motion is a `rotate` on θ).

## The machine

**Footprint.** 44 cells: the body, 3 wide (x) × 2 high × 6 long (z), every cell; over it, the drive's row along
the roof's middle (5 cells, one high: the line shaft, the bevel gears, the feeder and the hopper); and the stack,
a column three cells high over the flue chamber. In the native, south-facing frame with the controller at
`[0,0,0]`: x −1..1, y 0..1, z 0..5, plus x 0, y 2, z 0..4 and x 0, y 2..4, z 5. The footprint is level 2's: level
1 needs only the body, the hopper's cell and the stack, but the drive row is fixed from the frame on, so nothing
moves when the furnace is built. The **controller** is the middle of the fire end at ground level, `[0,0,0]`,
the block the player clicks. Placed like the draw bench, the roaster runs away from the player along their line
of sight, the fire end nearest them: the fire end is native north, the stack native south.

**Anchors** (all fixed; `assets/seraphhorizons/config/roaster-rig.json`):

| Anchor | Where (shipped, blocks) | What |
|---|---|---|
| `powerCell` `[0,2,0]`, `powerFace` north | Over the fire end, on the roof's middle line | The vanilla axle comes in along z at the cell's centre (0.5, 2.5) and is continued by the line shaft. Level 2 takes power; level 1 does not. |
| `infeedCell` `[0,2,4]`, `infeedFace` up | Over the cold end, beside the stack | The hopper's mouth is this cell's top (y 3), at both levels: filled by hand at level 1, by the previous machine's chute at level 2. `infeed.pos` is the mouth's middle. |
| `outputCell` `[1,0,2]`, `outputFace` east | The east face at ground level, by the bridge | The frame's spout, the iron-lined mouth of a tunnel from the discharge pit: where the roasted ore leaves, both levels. `output.pos` is the mouth's middle. |
| `fuelCell` `[-1,1,0]`, `fuelFace` up | The fire end's west corner, on the furnace's top | Level 2's coal bunker spans the fire end; a chute drops coal into its west end here (the middle column's top is the power cell). `fuel.pos` is that end of its mouth. |
| `charge` | The hearth under the feed chute | Where the charge lands (level 2). |
| `discharge` | The discharge hole in the hearth by the bridge | Over the pit (level 2). |
| `sulfur`, `sulfurSide` east | The flue chamber's clean-out door | Where the sulfur is drawn (both levels; provisional, below). |
| `smoke` | The stack's top | For smoke and fumes, if particles are wanted. |

`levels` lists the `requires` values each level fits (the gameplay's sets), and `contents` what each level shows
while it roasts (the heaps; the charge and the fire); the site's states are a level's parts and its contents (a
test holds them equal). `gearing` records the drawn ratios; nothing reads it.

### The levels

| State | Fitted (`requires`) | Contents | Retired from the level before |
|---|---|---|---|
| The frame | — (`requires` null) | | |
| Level 1 (water): stall roaster | `stalls`, `stallflue`, `trestle`, `hopper` | `heaps` | |
| Level 2 (mill): reverberatory, chute-fed | `hopper`, `hearth`, `walls`, `arch`, `ironwork`, `firebox`, `drive`, `rabbles`, `feeder` | `charge`, `fire` | `stalls`, `stallflue`, `trestle` |

**Why level 2 pulls level 1 down.** A stall roaster is open masonry stalls; a reverberatory is a closed furnace
under an arch, and one cannot be made into the other. Works that went over from stall roasting to reverberatory
roasting (in the period, as concentration made fines that stalls could not roast) built the furnace in the
stalls' place and kept what both use: here the plinth, the pit and its spout, the flue chamber, the stack and the
hopper. The rest of level 1 (its stalls, their collecting flue and header, and the hopper's trestle and trough)
is the only part of this machine retired at an upgrade. The alternative, stalls and furnace side by side, needs a
footprint twice as wide for stalls that do nothing at level 2.

The build order within a level is not designed yet (to be worked out with the owner): each level's values are
grouped by what reads as one fitting.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. The textures are the game's: dressed granite (`game:block/stone/brick/granite1`) for the
plinth and the stack's cap, granite rubble (`stone/drystone/granite1`) for the stalls, red brick
(`clay/brick/four/running/red1`) for the furnace's and the chamber's walls, the stack and the stalls' flue,
firebrick (`clay/brick/four/running/fire1`) for the hearth, the bridge, the arch and the skewbacks, iron
(`metal/plate/iron`) for all the ironwork, the hopper, the bunker and the gears, oak (`wood/debarked/oak`) for the
level 1 trestle and trough and the line shaft's entry (the vanilla axle's), `pile/oldore` for the heaped ore and
the charge, `wood/firepit/log` for the firewood, `coal/bituminous` for the coal in the bunker and `coal/charcoal`
for the fire on the grate. The fire's texture code is `fire`, so a renderer can set it to `game:block/coal/ember`
while the furnace burns. Masonry is cut at the block grid with its UVs at its place in the grid, so courses run
on unbroken across the boxes it is built of. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/roaster.json` | The whole machine, both levels' parts in one shape (600 elements: 102 in the frame, 194 drawn at level 1, 516 at level 2). A renderer draws the fitted level's parts. |
| `assets/seraphhorizons/shapes/block/roaster_frame.json` | The frame only (102 elements): plinth, pit and spout, flue chamber, stack. |
| `assets/seraphhorizons/config/roaster-rig.json` | Footprint and collision by what is fitted, anchors, the levels, the gearing and the part rig (20 parts). |
| `tests/Roaster/rig-reference.json` | Every part's matrix at a spread of axle angles, from the reference maths. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north
to south) in voxels, from the machine box's north-west bottom corner; the controller cell is the build frame's
cell (1, 0, 0), so the shipped files are the build frame moved one block west and divided by 16.

**The stalls** (level 1). The back wall x 2..12 runs z 2..74, rubble to y 24, with a flue mouth cut at the foot
of each stall's back; the end and partition walls (z 2..6, 22..26, 42..46, 62..66) run east to the open fronts
at x 30. The collecting flue (brick, x 2..12, y 24..32) on the back wall runs to the header (x 2..32, z 74..80,
to y 32), which closes the flue chamber's throat. The hopper (x 16.5..31.5, y 42..48, z 71.5..80) stands on four
oak legs on the header; from the slot in its floor a trough runs down north over the last partition wall, on a
post, into the southern stall.

**The furnace** (level 2). Side walls x 2..8 and 40..46, z 6..80, and the fire end's wall z 1..6, from the plinth
(y 5) to the springing at y 21. Inside them (x 8..40): the firebox z 6..20, the bridge wall z 20..25 to y 17, the
hearth z 25..80 with its floor at y 12, opening at the cold end into the flue chamber's throat (x 18..30,
y 12..24). The arch spans x 8..40 with a rise of 5 (intrados 26 at the crown, extrados 30), seven firebrick
strips round its centre, from z 11 (the coal bunker's back) to 80. The buckstays (x 1..2 and 46..47, four
pairs, at z 12, 22, 52.5 and 79.2) stand from the plinth to y 32, tied over the arch at y 30.3..31.1, and press
the skewback bars. The work doors (16 wide, y 12..20) are through the side walls beside each rabble, each in an
iron frame with its plate raised in two guides above it. The discharge hole (x 33..39, z 31..37) goes down through
the hearth into the pit (x 32..40, z 30..38); the tunnel runs from the pit east to the spout.

**The firing** (level 2). The coal bunker (iron, x 2..46, z 1..11, y 21..32) sits on the fire end's wall and the
side walls' tops; the slot in its floor (x 9..39, z 7..10) drops through a throat to y 17, just over the top of
the step grate. The grate is four iron treads across the firebox (x 8..40), each 3.5 deep and 2 lower than the
last (tops at y 15, 13, 11, 9), from the fire end's wall to the bridge, built into the side walls; air comes up
between them from the ash pit, whose clean-out door is the only door on the fire end, at the plinth's top. Coal
sinks down the bunker's throat onto the top tread as the bed burns and slides down the steps.

**The drive.** The line shaft runs along z at (24, 40), the power cell's centre: an oak entry with the vanilla
axle's cross profile from the power face (z 0..4), then an iron shaft to z 79, in three pedestal bearings on the
arch's crown (z 14..17, 25..28, 48..51) and the feeder casing's end walls. Each rabble (spindles at z 40 and 64)
is driven by a bevel pair: an 8-tooth pinion on the line shaft (pitch radius 2.8, at z 34.4 and 58.4) on the
north rim of a 16-tooth wheel on the spindle (pitch radius 5.6, its pitch plane at y 37.2, module 0.7); their
pitch cones meet where the shaft's and the spindle's axes cross. Driven from the north rim, a wheel turns back:
the rabbles turn at −1/2 of the axle. Each spindle runs from the hearth up through a gland in the crown strip and a
bearing on the roof, the wheel's hub resting on the bearing. In the hearth it carries a hub with two arms (11.6
long), each with three ploughs set 40° across it; the sweeps reach 12 from the spindle, so the first rabble's
sweep takes in the discharge hole and the second's the charge under the feed chute, and the two meet between them.

**The feeder** (level 2). A fluted roll (six flutes, radius 2.6) keyed on the line shaft's end turns in an iron
casing (x 20..28, y 31.3..42, z 72..79) under the hopper's slot, whose end walls are the shaft's last bearings;
the charge it carries over drops down the feed chute (x 21.5..26.5, z 73.5..77.5) through the arch's crown, its
flange on an iron seat on the crown.

Every moving part (θ the axle angle; none rides another):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Line shaft: oak entry, iron shaft, bevel pinions (`lineshaft`) [drive] | The vanilla axle at the power face | The bevel wheels; the feed roll | `rotate` z, ratio 1, about (24, 40) |
| Rabble 1, rabble 2: spindle, bevel wheel, hub, arms, ploughs (`rabble1`, `rabble2`) [rabbles] | Its bevel wheel, from the line shaft's pinion on its north rim, 8:16 | The charge on the hearth | `rotate` y, ratio −1/2, about its spindle |
| Feed roll: body and flutes (`feedroll`) [feeder] | Keyed on the line shaft's end | The charge from the hopper | `rotate` z, ratio 1, about the shaft |
| Everything else | Static | — | none |

Every shaft is carried: the line shaft in three pedestal bearings and the casing's end walls, each spindle in its
gland and its bearing. No toothed wheel meshes with nothing.

### Collision

A cell's `boxes` are the frame's, there at every level; its `fitted` boxes are those of each `requires` value's
parts, there only while that value is fitted (the shared format, `docs/recipe-browser/models.md`, "Collision by
what is fitted"). So at level 1 the stalls and the forecourt in front of them are open where level 2's furnace
walls and hearth would be solid, and at level 2 the stalls' walls are gone. Each is rebuilt from the shipped,
rounded shape posed at rest by the shipped rig, as the other machines' cells are. Two limits:

- **The game does not read `fitted` yet** (`Machines/Core/RigJson.cs`): until it does, a cell with fitted boxes
  and no frame boxes reads as a full cube. The roaster's gameplay needs that first.
- **The lids are not by level.** A cell's `lid` (the collision-only deck on a column's top cell) has no fitted
  form, so the lids are the final level's: on every column, at y 2 on the side columns and y 3 on the drive row.
  At level 1 the drive row's lids are a deck over an empty roof, and the side columns' over the stalls keep a
  player from standing in the stalls or the forecourt (they are worked from outside the east face).

### Regenerating

```sh
python3 mods-src/seraphhorizons/Roaster/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Roaster/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Roaster/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 40 s (the z-fighting fix, once per level). `tools/validate_roaster.py` holds the checks;
every run checks its output and exits non-zero if one fails. The levels share one place, so every check that looks
at parts together looks at one state at a time (the frame, level 1, level 2), never at both levels at once:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part;
  the element count drawn in each state.
- **Textures** by role (stone, rubble, brick, firebrick, iron, oak, ore, firewood, coal, fire), every element.
- **Levels:** level 2 keeps exactly the hopper of level 1's parts and retires only `RETIRED`; contents are never
  fitted parts; every `requires` value is in a level or a level's contents.
- **Nothing floats,** in each state: the frame and that state's static parts are one piece from the ground.
- **Containment:** every element, at every sampled angle, inside the footprint's cells (not just the box).
- **Anchors:** the power, infeed, output and fuel cells are in the footprint with their faces outside it; the
  line shaft's entry meets the power face at the cell's centre; the hopper's mouth is the infeed cell's top; the
  spout is in the output cell, its mouth on the face; the stack, the chamber, the plinth, the spout and the
  sulfur door are the frame's.
- **The flue:** level 1's collecting flue runs from the first stall into the header, which closes the chamber's
  throat; each stall has a flue mouth at the foot of its back wall; level 2's hearth opens into the throat under
  the arch.
- **The charge's path** *(review: the first model's charge dropped out of the rabbles' reach)*: the discharge hole
  is over the pit, past the bridge and in the first rabble's sweep; the charge drops in the last rabble's sweep;
  the sweeps meet; they clear the bridge and the flue chamber by a voxel; the pit is in front of the stalls.
- **The firing** *(review: an unattended level must not look hand-stoked)*: the treads step down to the bridge,
  the bunker's throat ends over the top tread, its mouth is the fuel cell's top, and the fire end's only door (the
  ash pit's) is below the grate.
- **The work doors** *(review: the furnace's inside could not be seen)*: from outside each open door, sight lines
  to the spindle at the arms' and the hearth's height cross no static element.
- **Supports:** the line shaft in at least two pedestal bearings and the casing's end walls; each spindle in its
  gland and its bearing, the wheel's hub resting on the bearing.
- **Gearing** *(review: the first model's teeth were spindly cogs)*: each bevel pair's pitch cones share the
  apex where the axes meet; the contact point moves alike on both gears either way round; tooth and gap stay
  within a tenth of a tooth at the contact; the box teeth run at most 0.3 voxels into each other over a tooth's
  cycle (the gear cutter's limit for its mitre bevels).
- **The feed roll:** keyed on the shaft, under the hopper's slot, over the chute, its flutes inside the slot.
- **Clearances:** every moving part against everything drawn with it at level 2, at 24 angles; and **swept
  paths** (full runs) every 5° of the shaft over the rabbles' two turns (144 angles). Intended contacts are
  listed in `ALLOWED` (shafts in their bearings, glands and casing, the roll on the shaft, meshing teeth, the
  ploughs in the charge).
- **No z-fighting** (full runs): no coplanar overlapping faces in any state, at rest and, for level 2, at three
  more angles.
- **Files and collision** *(review: level 1's stalls stood inside the furnace's collision)*: every texture
  declared; every cell has boxes at some level; lids over every column; the anchor cells are cells; `fitted` keys
  are the rig's values; at level 1 each stall's middle and the forecourt are open, at level 2 the furnace's wall
  is solid there; the shipped model is the checked one moved.

`tools/tests/test_roaster_model.py` and `site/test/roaster.test.ts` hold the written files to the rig's own
maths (the reference poses), the cells and their fitted boxes, the levels (the site's states are the rig's), the
gearing and the feed roll, and the anchors.

### Rig schema (`roaster-rig.json`)

The gear cutter's schema (`../GearCutter/README.md`, "Rig schema") with no work quantity: θ is the only input.

- `cells` (44, with lids on every column's top cell): each `pos`, the frame's `boxes`, and `fitted`, boxes by
  `requires` value. `powerCell` and `powerFace`, `infeedCell` and `infeedFace`, `outputCell` and `outputFace`,
  `fuelCell` and `fuelFace`: fixed from the frame on, written as a cell and a face (the chutes' ends).
- Points: `infeed`, `output`, `fuel`, `charge`, `discharge`, `sulfur` (with `sulfurSide`), `smoke`.
- `levels`: `{ "1": [...], "2": [...] }`, each level's fitted `requires` values; `contents`: what each shows
  while roasting.
- `gearing`: `rabbleTurnsPerAxleTurn` (1/2), `feedRollTurnsPerAxleTurn` (1), `pinionTeeth`, `wheelTeeth`: as
  drawn, for reference.
- `parts`: in the levels' order, so the viewer lists the `requires` values level by level.

`requires` values: `stalls`, `stallflue`, `trestle`, `heaps` (level 1), `hopper` (both), `hearth`, `walls`,
`arch`, `ironwork`, `firebox`, `drive`, `rabbles`, `feeder`, `charge`, `fire` (level 2); null for the frame.

**In the viewer** (`site/models.json`): one select, **Level**, with the frame alone and levels 1 and 2, opening
at level 2; the checkboxes under it show and hide each value. The θ slider spans two turns (the rabbles'
cycle); Play turns the axle (no phases: nothing in the roaster has a cycle of its own). The hearth and the
rabbles are seen through the open work doors (the **Side** view looks straight through them).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `stalls_*`, `stallflue_*`,
`trestle_*`, `heaps_*`, `hopper_*`, `hearth_*`, `walls_*`, `arch_*`, `ironwork_*`, `firebox_*`, `lineshaft_*`,
`pedestal*`, `rabble1_*`, `rabble2_*`, `rabblemount*`, `feedroll_*`, `feeder_*`, `charge_*`, `fire_*`, and `fr_*`
for the frame. Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

## Choices, and why

- **The rabbles: mechanical, on vertical spindles.** Reverberatory roasting was rabbled by hand through the
  work doors, but level 2 runs unattended, so the furnace needs a mechanism with a visible cause. Of the period's
  mechanical roasters, the Edwards furnace (1890s) is a straight reverberatory with rotating rabbles on spindles
  through the arch, driven from a line shaft over the roof: the same furnace, its drive in plain sight, and only
  `rotate` drivers. A rabble carriage drawn along the hearth by a chain (the Ropp and O'Harra furnaces) needs
  per-link chain drivers and a slot along the side wall, for less to see.
- **Bevel gears like the other machines'.** The first model drew a crown wheel and a spur pinion with thin
  cogs (0.4 wide, the gap three times the tooth). Each pair is now a bevel pair built like the gear cutter's mitre
  bevels: a body and teeth of the module's proportions (a tooth 1 wide, a little under half the 2.2 pitch, 1.6
  high), tilted along their pitch cones, the pinion's body stepped under its roots. 8:16 makes the wheel as big as
  the drive row allows (its tips 6.8 from the spindle, inside the cell) and turns the rabbles at half the axle.
- **Power: the vanilla axle over the fire end, continued by the line shaft.** No water wheel or engine.
  Level 1 takes no power: a stall roaster is heaps burning in stalls, raked by hand.
- **Firing at level 2: a step grate under a coal bunker** (review: no hand-stoking door at an unattended
  level). The step grate (Treppenrost, in use under roasting furnaces from the 1860s) is fed by gravity from a
  hopper over its top step and needs no stoking, only its ash pit cleared; a chute keeps the bunker filled. A
  mechanical stoker (a travelling chain grate, the Juckes furnace of the 1840s) would also do, but needs a moving
  chain. The bunker spans the fire end under the line shaft's entry; its chute comes in at the west corner,
  because the middle column's top is the power cell.
- **The feeder: a fluted feed roll keyed on the line shaft's end.** The first model's plate feeder (a Scotch
  yoke on a crank) needed room between the last gear and the hopper that the bevel wheels now take. A fluted roll
  under a hopper's slot is the period's simplest positive feeder, it turns with the shaft that is already there,
  and it is one `rotate`. It is inside its casing, so its motion is seen only from above with the hopper hidden.
- **The hopper is level 1's, kept.** The infeed is the same box at the same place at both levels: on a trestle,
  filled by hand, at level 1; on the feeder's casing, filled by the previous machine's chute, at level 2.
- **The inside: open work doors.** An Edwards furnace had a work door opposite each rabble, both sides, for
  inspecting and changing the ploughs; they are drawn open, their plates lifted in their guides (a counterweighted
  lift door, as on period furnaces), 16 wide and 8 high, so the hearth, the charge and the rabbles' arms can be
  seen through them, and straight through the furnace from the side. No wall or roof is cut away.
- **No process water.** Roasting is dry; nothing in either level takes a pipe.
- **Where the fumes go, and the sulfur.** Both levels' fumes go up the one stack through the frame's flue chamber
  (level 1's by its collecting flue and header), so the sulfur is drawn at one place: the chamber's clean-out
  door, where sublimed sulfur condenses (as it was recovered from stall roasting).
- **Where the roasted ore goes.** Into the frame's pit by the bridge: raked from the stalls at level 1, dropped
  through the hearth's discharge hole at level 2, and out of the spout into the next machine's chute.

## Open for the owner

1. **Level 2 retires level 1's stalls** (above). The alternative, keeping them beside the furnace, doubles the
   footprint for stalls that do nothing at level 2.
2. **The mechanism for level 2:** rotating rabbles (Edwards) as drawn, a rabble carriage, or hand-rabbling with
   only the feed mechanised. Two rabbles fit the 6-block hearth; real ones had more.
3. **Direction.** Nothing rectifies the axle: turned backwards, the rabbles plough back (and the roll feeds
   back). A rectifier like the draw bench's could keep them ploughing towards the discharge.
4. **Fuel at level 2.** Drawn as coal fed by a chute into the bunker at the `fuelCell`: a second chute input for
   the gameplay. The other way is to let a sulfide charge burn on its own sulfur once lit (pyritic ore roasts
   itself), with the bunker and grate only for lighting. Level 1's heaps are drawn on firewood, as stall roasting
   was started.
5. **The sulfur's way out:** the flue chamber's door (`sulfur`), as drawn, or somewhere else.
6. **Collision limits:** the game's rig loader does not read `fitted` yet, and the lids are not by level (above).
7. **Readability:** open work doors as drawn, or a cut-away side wall as in a sectional drawing, if the doors are
   not enough.
8. **Sizes.** The furnace is 3 × 6 with a 5-block stack; the infeed's mouth is 3 blocks up, over the roof. The
   output spout is at ground level on the east face.
9. **Build order within each level** (the `requires` values are grouped, not ordered).

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (a standalone copy, `site/scripts/standalone-viewer.ts`),
at both levels, from several angles and through the axle's cycle. No one has looked at it in a client; there is
no renderer.

1. **Box teeth.** The bevel teeth are boxes tilted along their pitch cones, so they run up to 0.29 voxels into
   each other in mesh, where a real tooth form would not (held to the gear cutter's limit of 0.3).
2. **The arch is seven strips.** Its outside is a seven-sided polygon; inside, the strips' inner corners step.
3. **Static material.** The heaps, the charge and the fire are parts, not work: nothing burns down, roasts or
   moves through the furnace.
4. **The feed roll is hidden** in its casing under the hopper.
5. **Lids at every column's top** stand over the drive row (y 3) and the stack's cap, like the other machines';
   at level 1 they are the final level's (above).
