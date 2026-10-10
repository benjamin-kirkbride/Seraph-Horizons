# Roaster

Part of the Seraph Horizons mod (`../README.md`): the ore line's roasting stage (epic #684), one of its
upgradeable machines (#711). It roasts sulfide concentrate into roasted concentrate, giving off sulfur
(behaviour #736: tier 2 stall roaster 92 %, tier 3 reverberatory roaster 100 %, tier 4 reverberatory,
chute-fed). This folder holds the model's generator (`tools/`) and this record of it (#737). **There is no
gameplay yet**: no block, block entity or renderer reads these files; the rig is written as the contract the
gameplay will be built against.

**One machine, upgraded in place** (#711): the frame is placed at its tier 4 size, then one tier's parts after
another; fitting the next tier's set replaces the previous tier's working parts, which drop back. Nothing that
connects to the outside moves between tiers: the power cell, the infeed, the output chute's exit and the stack
are the frame's. Early tiers look sparse inside the footprint: tier 2's stalls stand on the plinth where the
furnace of tiers 3 and 4 will stand.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, unchanged by this machine (no new driver: every motion is a `rotate` or a
`slide` on θ).

## The machine

**Footprint.** 44 cells: the body, 3 wide (x) × 2 high × 6 long (z), every cell; over it, the drive's row
along the roof's middle (5 cells, one high: the line shaft, the crown wheels and the hopper); and the stack, a
column three cells high over the flue chamber. In the native, south-facing frame with the controller at
`[0,0,0]`: x −1..1, y 0..1, z 0..5, plus x 0, y 2, z 0..4 and x 0, y 2..4, z 5. The **controller** is the
middle of the fire end at ground level, `[0,0,0]`, the block the player clicks (the fire door's). Placed like
the draw bench, the roaster runs away from the player along their line of sight, the fire end nearest them:
the fire end is native north, the stack native south.

**Anchors** (all the frame's; `assets/seraphhorizons/config/roaster-rig.json`):

| Anchor | Where (shipped, blocks) | What |
|---|---|---|
| `powerCell` `[0,2,0]`, `powerFace` north | Over the fire end, on the roof's middle line | The vanilla axle comes in along z at the cell's centre (0.5, 2.5) and is continued by the line shaft. Tiers 3 and 4 take power; tier 2 does not (open below). |
| `infeedCell` `[0,2,4]`, `infeedFace` up | Over the cold end, beside the stack | Every tier's hopper has its mouth at this cell's top (y 3): filled by hand at tiers 2 and 3, by the previous machine's chute at tier 4. `infeed.pos` is the mouth's middle. |
| `outputCell` `[1,0,2]`, `outputFace` east | The east face at ground level, by the bridge | The frame's spout, the iron-lined mouth of a tunnel from the discharge pit: the output chute's end from tier 3 (where tier 2's roasted ore collects). `output.pos` is the mouth's middle. |
| `charge` | The hearth's cold end, under the charging hole | Where the charge lands (tiers 3 and 4). |
| `discharge` | The discharge hole in the hearth by the bridge | Over the pit (tiers 3 and 4). |
| `fuel`, `fuelSide` north | The fire door | Stoking (tiers 3 and 4). |
| `sulfur`, `sulfurSide` east | The flue chamber's clean-out door | Where the sulfur is drawn (every tier; provisional, below). |
| `smoke` | The stack's top | For smoke and fumes, if particles are wanted. |

`tiers` lists the `requires` values each tier fits, as the gameplay's sets and the site's states (a test holds
them equal). `gearing` records the drawn ratios; nothing reads it.

### The tiers

| State | Fitted (`requires`) | What it is |
|---|---|---|
| The frame | — (`requires` null) | A stone plinth under the whole footprint (5 voxels), with the **discharge pit** sunk in it east of the middle and a tunnel to the **spout** on the east face; the **flue chamber** at the cold end (brick, its throat in its north wall, a **sulfur door** on its east face); the **stack** on it, brick with a stone cap and two iron bands, five blocks to its top. |
| Tier 2: stall roaster | `stalls`, `stallflue`, `stallbin`, `heaps` | Three open-fronted stalls in rubble masonry against a back wall on the west, opening east onto a forecourt where the pit is. Each stall's flue rises in the back wall from a mouth at its foot to the brick **collecting flue** on the wall's top, which a **header** at the cold end carries into the flue chamber's throat (closing it), so the stalls' fumes go up the stack. The ore is heaped in each stall in courses on a bed of firewood. A wooden **bin** on a trestle at the cold end is the hopper; a trough from it runs down into the southern stall. |
| Tier 3: reverberatory roaster | `hearth`, `walls`, `arch`, `ironwork`, `fire`, `drive`, `rabbles`, `charger` | The stalls are gone (their parts drop back). A long low furnace: red-brick side and end walls, a firebrick hearth and bridge wall, a segmental firebrick arch on firebrick skewbacks, held by iron buckstays with tie rods over the arch and skewback bars. The **firebox** at the fire end: a grate of 15 bars on two bearers, the ash pit under it, the fire door and the ash-pit door on the end wall's face. The flame passes over the bridge wall, along the hearth and through the flue chamber's throat. Two side **work doors** each side, at the rabbles. Two **rabbles** turn in the hearth on spindles through glands in the arch's crown, driven by crown wheels from the **line shaft** on the roof, which continues the vanilla axle; they plough the charge towards the **discharge hole** by the bridge, over the pit. The **charging box** over a hole in the arch at the cold end is filled by hand; its slide gate drops the charge. |
| Tier 4: reverberatory, chute-fed | as tier 3, less `charger`, plus `hopper`, `feeder` | The same furnace. The charging box gives way to a **hopper** the previous machine's chute fills, over a **feed box** in which a plate is shuttled across under the hopper's throat by a **Scotch yoke** on a **crank** keyed on the line shaft's end, metering the charge down the **feed chute** through the arch. |

The build order within a tier is not designed yet (to be worked out with the owner): each tier's values are
grouped by what reads as one fitting (the walls, the arch, the ironwork, the drive, the rabbles, ...), so the
viewer can show and hide them, and the furnace's inside can be seen by hiding the arch and the walls.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. The textures are the game's: dressed granite (`game:block/stone/brick/granite1`) for the
plinth and the stack's cap, granite rubble (`stone/drystone/granite1`) for the stalls, red brick
(`clay/brick/four/running/red1`) for the furnace's and the chamber's walls, the stack and the stalls' flue,
firebrick (`clay/brick/four/running/fire1`) for the hearth, the bridge, the arch and the skewbacks, iron
(`metal/plate/iron`) for all the ironwork, oak (`wood/debarked/oak`) for the tier 2 bin and the line shaft's
entry (the vanilla axle's), `pile/oldore` for the heaped ore, `wood/firepit/log` for the firewood, and
`coal/charcoal` for the fire on the grate. The fire's texture code is `fire`, so a renderer can set it to
`game:block/coal/ember` while the furnace burns. Masonry is cut at the block grid with its UVs at its place in
the grid, so courses run on unbroken across the boxes it is built of. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/roaster.json` | The whole machine, every tier's parts in one shape (667 elements). A renderer draws the fitted tier's parts. |
| `assets/seraphhorizons/shapes/block/roaster_frame.json` | The frame only (102 elements): plinth, pit and spout, flue chamber, stack. |
| `assets/seraphhorizons/config/roaster-rig.json` | Footprint, anchors, the tiers, the gearing and the part rig (19 parts). |
| `tests/Roaster/rig-reference.json` | Every part's matrix at a spread of axle angles, from the reference maths. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z
north to south) in voxels, from the machine box's north-west bottom corner; the controller cell is the build
frame's cell (1, 0, 0), so the shipped files are the build frame moved one block west and divided by 16.

**The furnace** (tiers 3 and 4). Side walls x 2..8 and 40..46, the fire end's wall z 1..6, from the plinth (y 5)
to the springing at y 21. Inside them (x 8..40): the firebox z 6..22 (grate at y 11..12, its fire bed on it),
the bridge wall z 22..27 to y 17, the hearth z 27..80 with its floor at y 12, opening at the cold end into the
flue chamber's throat (x 18..30, y 12..24). The arch spans x 8..40 with a rise of 5 (intrados 26 at the crown,
extrados 30), seven firebrick strips round its centre, from z 1 to 80; at the fire end a firebrick gable fills it
under the arch. The buckstays (x 0.6..2 and 46..47.4, six pairs) stand from the plinth to y 32, tied over the
arch at y 30.3..31.1 with nuts outside them, and press the skewback bars. The discharge hole (x 33..39,
z 31..37) goes down through the hearth into the pit (x 32..40, z 30..38); the tunnel runs from the pit east to
the spout (x 46.5..48, z 32..38, y 1..4).

**The drive.** The line shaft runs along z at (24, 40), the power cell's centre: an oak entry with the vanilla
axle's cross profile from the power face (z 0..4), then an iron shaft to z 69.6, in four pedestal bearings on
the arch's crown. On it, an 8-tooth pinion over each crown wheel's north rim (module 0.5). Each rabble's
spindle runs from the hearth up through a gland in the crown strip and a bearing on the roof; on its top a
24-tooth crown wheel, teeth up, its hub resting on the bearing. Driven from the north rim, a crown wheel turns
back: the rabbles turn at −1/3 of the axle. In the hearth each spindle carries a hub with two arms, each with
three ploughs set 40° across it, reaching 9.5 voxels (rabbles at z 38.5 and 58.5: the first reaches the
discharge hole, the second towards the charging hole).

**The feeder** (tier 4). A crank disc keyed on the line shaft's end (z 69..70.2), its pin 2.2 out, rides in the
slot of a yoke (two cheeks over a foot); the yoke's push rod runs through a slot in the feed box's north wall
to the feed plate inside, on runners under the hopper's throat. The pin's x is −2.2 sin θ, so the yoke slides
exactly that (a Scotch yoke): the plate goes 2.2 either side once a turn of the axle.

Every moving part (θ the axle angle; none rides another):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Line shaft: oak entry, iron shaft, pinions (`lineshaft`) [drive] | The vanilla axle at the power face | The crown wheels; the crank | `rotate` z, ratio 1, about (24, 40) |
| Rabble 1, rabble 2: spindle, crown wheel, hub, arms, ploughs (`rabble1`, `rabble2`) [rabbles] | Its crown wheel, from the line shaft's pinion on its north rim, 8:24 | The charge on the hearth | `rotate` y, ratio −1/3, about its spindle |
| Crank: disc and pin (`crank`) [feeder] | Keyed on the line shaft's end | The yoke | `rotate` z, ratio 1, about the shaft |
| Yoke: cheeks, foot, push rod, plate (`yoke`) [feeder] | The crank pin in its slot | The charge under the hopper's throat | `slide` x, amplitude −2.2/16, ratio 1 |
| Everything else | Static | — | none |

Every shaft is carried: the line shaft in four bearings, each spindle in its gland and its bearing, the crank
on the shaft. No toothed wheel meshes with nothing.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Roaster/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Roaster/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Roaster/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 45 s (the z-fighting fix, once per working state). `tools/validate_roaster.py` holds
the checks; every run checks its output and exits non-zero if one fails. The tiers share one place, so every
check that looks at parts together looks at one state at a time (the frame, tier 2, tier 3, tier 4), never at
every tier at once:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Textures** by role (stone, rubble, brick, firebrick, iron, oak, ore, firewood, fire), every element.
- **Tiers:** tier 2 shares no part with the furnace; tier 4 is tier 3 less the charging box plus the hopper and
  the feeder; every `requires` value is in a tier.
- **Nothing floats,** in each state: the frame and that state's static parts are one piece from the ground.
- **Containment:** every element, at every sampled angle, inside the footprint's cells (not just the box).
- **Anchors:** the power, infeed and output cells are in the footprint with their faces outside it; the line
  shaft's entry meets the power face at the cell's centre; every tier's hopper (the bin, the charging box, the
  hopper) has its mouth at the infeed cell's top; the spout is in the output cell, its mouth on the face; the
  stack, the chamber, the plinth, the spout and the sulfur door are the frame's.
- **The flue:** tier 2's collecting flue runs from the first stall into the header, which closes the chamber's
  throat; each stall has a flue mouth at the foot of its back wall; the hearth opens into the throat under the
  arch.
- **The discharge:** the hole is over the pit, between the bridge and the first rabble, in its reach; the pit is
  in front of the stalls, not under them.
- **Supports:** the line shaft in at least two bearings; each spindle in its gland and its bearing, the crown's
  hub resting on the bearing.
- **Gearing:** each pinion on its crown wheel's north rim at the pitch plane; the rig's ratio is −8/24; over the
  cycle, the pinion's tooth and the crown's gap at the contact stay within a tenth of a tooth.
- **The yoke:** over a turn, the crank pin stays in the middle of the yoke's slot and within its cheeks.
- **Clearances:** every moving part against everything drawn with it, in tiers 3 and 4, at 24 angles; and
  **swept paths** (full runs) every 5° of the shaft over the rabbles' three turns (216 angles): the gear teeth,
  the rabbles in the hearth, the yoke and the plate in the feed box. Intended contacts are listed in
  `ALLOWED` (shafts in their bearings and glands, the crank on the shaft, the push rod in its slot, the plate on
  its runners).
- **No z-fighting** (full runs): no coplanar overlapping faces in any state, at rest and, for tiers 3 and 4, at
  three more angles.
- **Files:** every texture declared; no hollow cell; lids over every column; the anchor cells are cells; the
  shipped model is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, every tier's
parts together, as the other machines' are (see "Open for the owner").

`tools/tests/test_roaster_model.py` and `site/test/roaster.test.ts` hold the written files to the rig's own
maths (the reference poses), the cells, the tiers (the site's states are the rig's), the gearing and the yoke,
and the anchors.

### Rig schema (`roaster-rig.json`)

The gear cutter's schema (`../GearCutter/README.md`, "Rig schema") with no work quantity: θ is the only input.

- `cells` (44, with lids on every column's top cell), `powerCell` and `powerFace`, `infeedCell` and
  `infeedFace`, `outputCell` and `outputFace`: the frame's, fixed from the frame on. The infeed and the output
  are written as a cell and a face (the chutes' ends), not as the older machines' `infeedSide`/`outputSide`.
- Points: `infeed`, `output`, `charge`, `discharge`, `fuel` (with `fuelSide`), `sulfur` (with `sulfurSide`),
  `smoke`.
- `tiers`: `{ "2": [...], "3": [...], "4": [...] }`, each tier's `requires` values.
- `gearing`: `rabbleTurnsPerAxleTurn` (1/3), `feederStrokesPerAxleTurn` (1), `pinionTeeth`, `crownTeeth`: as
  drawn, for reference.
- `parts`: in the tiers' order, so the viewer lists the `requires` values tier by tier.

`requires` values: `stalls`, `stallflue`, `stallbin`, `heaps` (tier 2); `hearth`, `walls`, `arch`, `ironwork`,
`fire`, `drive`, `rabbles`, `charger` (tier 3); `hopper`, `feeder` (tier 4); null for the frame.

**In the viewer** (`site/models.json`): one select, **Tier**, with the frame alone and tiers 2, 3 and 4,
opening at tier 4; the checkboxes under it show and hide each value. The θ slider spans three turns (the
rabbles' cycle); Play turns the axle (no phases: nothing in the roaster has a cycle of its own).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `stalls_*`, `stallflue_*`,
`stallbin_*`, `heaps_*`, `hearth_*`, `walls_*`, `arch_*`, `ironwork_*`, `fire_*`, `lineshaft_*`,
`pedestal*`, `rabble1_*`, `rabble2_*`, `rabblemount*`, `charger_*`, `hopper_*`, `crank_*`, `yoke_*`, and `fr_*`
for the frame. Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

## Choices, and why

- **The rabbles: mechanical, on vertical spindles.** Reverberatory roasting was rabbled by hand through the
  work doors, but #711 runs tiers 3 and 4 continuously, so the furnace needs a mechanism with a visible
  cause. Of the period's mechanical roasters, the Edwards furnace (1890s) is a straight reverberatory with
  rotating rabbles on spindles through the arch, driven from a line shaft over the roof: the same furnace,
  with its drive in plain sight, and only `rotate` drivers. The alternatives were a rabble carriage drawn
  along the hearth by a chain (the Ropp and O'Harra furnaces: a slot along the side wall, and per-link chain
  drivers like the draw bench's) and a reciprocating rake (a long bar through the end wall); both need more
  parts moving with less to see.
- **Power: the vanilla axle over the fire end, continued by the line shaft.** The axle comes in at a cell
  above the furnace (the shaft runs over the roof's crown, where the pinions meet the crown wheels' rims), at
  the end the player works from. No water wheel or engine.
- **Tier 2 takes no power.** A stall roaster is heaps burning in stalls, raked by hand: nothing in it moves.
- **Tier 4's feeder: a reciprocating plate feeder on a Scotch yoke.** It meters the chute's concentrate into
  the furnace from the line shaft that is already there, its motion exactly a `slide` on θ (a crank and
  connecting rod would not be: its rod's angle is not a sine). The hopper, its feed box and the feed chute
  replace tier 3's charging box at the same cell, with the same mouth.
- **Firing.** Tiers 3 and 4 fire a grate: the fire bed on it is its own value (`fire`), with its own texture
  code. Tier 2's heaps are drawn on beds of firewood, as stall roasting was started; after that a sulfide heap
  burns on its own sulfur.
- **Where the fumes go, and the sulfur.** Every tier's fumes go up the one stack through the frame's flue
  chamber (tier 2's by its collecting flue and header), so the sulfur can be drawn at one place at every tier:
  the chamber's clean-out door, where sublimed sulfur condenses (as it was recovered from stall roasting).
- **Where the roasted ore goes.** Into the frame's pit, by the bridge: raked from the stalls at tier 2 (where it
  collects for the player), dropped through the hearth's discharge hole at tiers 3 and 4, and out of the spout
  into the next machine's chute.

## Open for the owner

1. **The mechanism for tiers 3 and 4** (above): rotating rabbles (Edwards) as drawn, or a rabble carriage, or
   hand-rabbling with only the feed mechanised. Two rabbles fit the 6-block hearth; real ones had more.
2. **Direction.** Nothing rectifies the axle: turned backwards, the rabbles turn back (their ploughs are set to
   work one way). A rectifier like the draw bench's could keep them ploughing towards the fire.
3. **Power at tier 2:** none, as drawn. The power cell is there from the frame on, empty until tier 3's drive.
4. **Fuel and fire.** Whether tiers 3 and 4 burn fuel (stoked at the fire door, `fuel`) or a sulfide charge is
   left to burn on its own once lit; whether tier 2 takes firewood. The fire bed is drawn in charcoal; the
   viewer cannot show glow, flame, smoke or fumes, so none is drawn: a renderer could set `fire` to ember and
   give `smoke` (the stack's top) and `sulfur` particles.
5. **The sulfur's way out:** the flue chamber's door (`sulfur`), as drawn, or somewhere else.
6. **Tier 2's hopper.** #711 gives tier 2 "hopper in", so a bin stands at the infeed cell on a trestle and a
   trough feeds the southern stall; whether the player should rather load the stalls directly.
7. **The fixed footprint's boxes.** The cells' boxes (and the lids over them) are the finished machine's, every
   tier's parts together, as the generator builds them: at tier 2 they are the furnace's, so the stalls would
   stand inside invisible walls. Boxes per tier are for the gameplay to decide.
8. **Sizes.** The furnace is 3 × 6 with a 5-block stack; the infeed's mouth is 3 blocks up, over the roof. The
   output spout is at ground level on the east face.
9. **Build order within each tier** (the `requires` values are grouped, not ordered).
10. **The stack's height and the chute connection's face** (up, for a chute coming down into the hopper).

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a
standalone copy, `site/scripts/standalone-viewer.ts`). No one has looked at it in a client; there is no
renderer.

1. **The gears are boxes.** A crown wheel driven by a spur pinion is a face gear, whose teeth change shape
   across the face; with box teeth, the pinion is as thick as the crown's teeth are long in its plane (1) and
   both have thin teeth (0.4, the gap 1.17), so the teeth clear (the swept check); the engagement is short of a
   full addendum.
2. **The arch is seven strips.** Its outside is a seven-sided polygon; inside, the strips' inner corners step.
3. **The inside of the furnace is closed.** The rabbles, the grate and the fire are seen only with the walls
   and the arch hidden; the doors are drawn shut.
4. **Static material.** The heaps and the fire are parts, not work: nothing burns down, roasts or moves through
   the furnace, and the charge on the hearth is not drawn.
5. **Lids at every column's top** stand over the drive's row (y 3) and the stack's cap, like the other
   machines'; whether the player should walk on the furnace's roof is the gameplay's.
