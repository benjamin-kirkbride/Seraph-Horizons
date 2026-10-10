# Concentrator (wash house)

Part of the Seraph Horizons mod (`../README.md`): the ore line's gravity concentration stage (#734, model
#735, the ore processing epic #684). One machine upgraded in place (#711): a timber **wash house** built at
its tier 4 size, into which each tier's working parts are fitted, the next tier's set replacing the last.
From tier 2, **amalgamation plates** can be fitted too: one part at any tier, not a tier of their own (they
replace the separate amalgamator machine).

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what #734 will build against.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/` and `site/`, which are the repository's.

## The machine

**Footprint.** 90 cells: 6 long (x, the machines' length, head at the west), 3 high, 5 wide (z). In the
native frame with the controller at `[0,0,0]`, the wash house's north-west corner at the head end: x 0..5,
y 0..2, z 0..4. The frame is the same at every tier; early tiers stand in it with room to spare.

**The tiers** (the rig's `tiers`, one `requires` value a set; the site's states, one a tier):

| Tier | `requires` | Fitted | #734's figures |
|---|---|---|---|
| — | none | The wash house frame alone | |
| 1 | `longtom` | A long tom: the tom, its riddle (the tom iron), the riffle box with its cleats, and a rake combing the gravel across the riddle | 65 %, 2 L/s, fed and emptied by hand |
| 2 | `jig` | A two-compartment Hartz jig: the hutch, its two sieves, two plungers | 80 %, 4 L/s, hopper in, output collects |
| 3 | `table` | A shaking table: the riffled deck, the enclosed head motion, the water launder and feed box | 92 %, 8 L/s, continuous |
| 4 | `table`, `vanner` | The table as tier 3, and a Frue-style vanner taking the table's middlings | 99 %, 4 L/s recirculated, continuous |
| 2 to 4 | `plates` (+ `dressing`) | Optional, on top of the tier: three copper amalgamation plates in the frame's plate table, with a mercury trap; `dressing` is their mercury coat | Free gold and silver caught as amalgam; the rest passes over unchanged |

**The fixed points.** Everything a tier's parts lead to or from is the frame's, at the same cell and face
for every tier (the rig's anchors, block units):

| Anchor | Where | What |
|---|---|---|
| `powerCell`, `powerFace` | `[0,2,2]`, west | The vanilla axle comes in along x at the face's centre (y 40, z 40 voxels), onto the entry shaft's oak cross (the axle's two 4 × 2 boards) |
| `waterCell`, `waterFace` | `[0,1,0]`, north | A ppex pipe meets the inlet end to end: ppex's 6 × 6 section on the face's centre (x 8, y 24), with an 8 × 8 flange and four bolt heads on the face, as ppex's own pipes end |
| `feedCell`, `feedFace` | `[1,1,0]`, north | A chute from outside (by hand at tier 1) pours into the open upper end of the frame's feed chute |
| `feedSpout` | `[1.6875, 1.9375, 1.0]` | The feed chute's lip: every tier's head is under it (the tom's head, the jig's first sieve, the table's feed box) |
| `waterSpout` | `[2.0625, 1.875, 0.75]` | The head cock's nozzle: over the tom's head, the jig's sieve side, the table's water launder |
| `concentrate`, `concentrateSide` | `[6.0, 0.0604, 1.2156]`, east | The plate table's notch, out through the east wall at the floor: the concentrate's way out at every tier, over the plates when they are fitted |
| `tailings`, `tailingsSide` | `[0.0, 0.0625, 2.6875]`, west | The tailings launder's open end through the west end, at the floor: the heap forms outside |

Tier 1's concentrate stays behind the riffles (emptied by hand); tier 2's is drawn off at the hutch's
spigots into a launder to the concentrate launder; tiers 3 and 4 pour into it. The concentrate launder spills
onto the plate table, and the plate table's notch is the outlet, so the plates, where fitted, are after
every tier's concentrating stage and the outlet does not move when they are. Every tier's tailings reach
the tailings launder. The rig's `drive` gives each tier's working strokes per main shaft turn and the
vanner belt's creep per turn, for #734.

## How it works

Positions are in voxels in the generator's build frame (x west to east, y up, z north to south, from the
machine box's north-west bottom corner), which is the shipped frame times 16.

**The frame.** Oak posts at the corners and the middle of the long walls, sills along the long walls, wall
plates and four tie beams at the top (the west end, a drive beam at x 12..16, the middle, the east end),
knee braces. The ends are open at the floor, where the launders leave. Fixed to it:

- *The drive.* The entry shaft (the axle's oak cross, then steel) runs from the power face through two cast
  cheeks, one bolted to the west tie beam and one to the drive beam. It carries A1 (12 teeth) and A2 (9),
  module 0.75. A1 meshes B1 (12) directly; A2 meshes B2 (9) through an idler (9) on a stud in the east
  cheek's arm. B1 and B2 are loose on the **main shaft** below with one-way catches, so the main shaft turns
  forward (+) whichever way the axle turns, once an axle turn. The main shaft (steel, y 31, z 40, x 0.8..52)
  runs in both cheeks and a hanger from the middle tie beam. Every tier is belted from it.
- *The feed chute*, through the north wall at x 24.5..29.5, falling south to its lip at (27, 31, 16), on a
  post on the north sill with an iron knee.
- *The water.* The inlet stub, a riser to the header (4 × 4 past the inlet's reducer) along the north wall
  at y 40, carried by brackets from the north plate and a hanger from the middle tie beam. A drop at x 33
  ends in the brass **head cock**, its nozzle at (33, 30, 12); a drop at x 73.5 ends in the **vanner's
  cock**, its outlet pointing south, shut until the vanner is fitted.
- *The tailings launder* (z 39..47) falls west from x 80 (floor y 3.6) to the west end (1.0), on oak blocks.
- *The concentrate launder* (x 80..88) falls north from z 76 (floor 4.3) to z 5 (3.0), on oak blocks; at
  its north end its east wall stops and it spills east over a lip onto
- *the plate table*: a plank bed east of it (x 88.6..95.6, z 5.5..21.5, 7 wide outside), falling south 1 in
  12 from 2.1 to 0.8, a low curb under the lip and sides beyond, on two oak blocks. At its foot a notch in its
  east side and a short spout lead out through the east wall: the concentrate's outlet. Bare, the bed just
  carries the concentrate there.

**Tier 1, the long tom.** The tom (x 24..52, 12 wide) falls 1 in 12; its last 8 is the tom iron, a
perforated plate turned up 35° at the tail. Under it the riffle box (x 42..70.5, 16 wide) falls 1 in 16,
five cleats across its floor; a bar on its sides carries the tom's tail. The riffle box's open tail is over
the tail chute, which runs south to the tailings launder. **The rake:** two gallows frames straddle the
riddle; a crank shaft along x on their beams (y 35, z 16) is belted 1:1 from the main shaft. A crank throw
(radius 3, between two webs) runs its pin in the upright slot of a yoke on the rake's crosshead, which
slides on iron guide bars under the gallows' beams: the rake moves across the tom exactly 3 · sin of the
crank's angle (a Scotch yoke, so a plain `slide` draws it exactly), its five tines 0.7 over the riddle,
combing z 12.7..19.3 inside the tom's channel (11..21).

**Tier 2, the jig.** The hutch (x 22..60, z 9..33, its floor at y 6 on two sills, its top 24.5) is split
lengthwise: the sieve side north (z 10..19.5), the plunger side south. The long partition stops 7 above the
floor so the water passes under it. A cross partition makes two compartments; the first overflows into the
second at y 22, the second over the tail lip (y 21, a lip board over the tail chute). The sieves (mesh) lie
on ledges at y 18. Each plunger (13.5 × 9.5) is pumped 3 up and down by its own throw (radius 1.5) on a
crank shaft over the plunger side (y 31, z 26.25), running in a yoke whose slot is level: a Scotch yoke
again, the rod through an eye in a guide bar on the hutch. The throws are opposite, so one plunger rises as
the other falls. The shaft runs on two pedestals, belted from the main shaft (pulleys 4 and 5: 0.8 strokes
a turn). Two spigots on the north wall near the floor draw the hutch product into a launder along the north
wall to the concentrate launder; the tail chute runs south to the tailings launder.

**Tier 3, the shaking table.** The deck (x 22..78, z 9..33, its top 25 along the upper edge) falls 2.5°
south across its width. Twelve riffles run along it, the low ones shorter, ending on a diagonal; the water
launder runs along the upper edge (fed by the head cock), the feed box sits at the head corner (fed by the
feed chute); the end lip reaches over the concentrate launder. Two joists slide on iron plates on legs. The
**head motion** is a cast box (x 5.5..17) on an oak base: its input shaft comes in at its west face, belted
from the main shaft (pulleys 4 and 5), and a pull rod comes out of its east face to a bracket under the
deck's head. The toggle inside is enclosed, as a period head motion's is. The deck moves
x = 0.6 (sin t − sin 2t / 2), t the input shaft's angle: slow forward (east, to the concentrate end) for two
thirds of a stroke and quick back for a third, 1.56 end to end. The low edge runs onto an apron into the
tailings launder; near its tail (x 65..70, by the tail corner, where the middlings leave) it runs into the
**middlings box**, whose spout pours into the tailings launder at tier 3.

**Tier 4, the vanner.** The table as tier 3, and south of it a Frue-style vanner. Its **shaking frame**
(two oak rails, x 25.5..66, iron arms on to the head roller) rests on iron slides at x 36.5 and 64.2 and is
shaken along z ±1 by three throws (radius 1) on a crank shaft along its north side (y 6, z 51.5), each pin
running in the upright slot of a yoke cantilevered from the north rail. The crank shaft runs on three
pedestals, belted 1:1 from the main shaft. On the frame: the **foot roller** (x 28) and the **head roller**
(x 74, 1.5 higher: the belt rises 1 in 31 to the head), three carrying rollers under the upper run, and an
endless **belt** 14 wide with raised edges. Under the head roller is the **wash tank**; the head roller's
bottom dips 1 into it, and the lower run climbs out of it over a **deflector roller** (outside the loop) on
the tank's west wall, then runs back to the foot. **The creep:** a small belt from the crank shaft drives a
worm shaft on brackets on the north rail; its single-start worm turns the worm wheel (16 teeth) on the head
roller's north journal, so the head roller turns 1/16 of a turn a turn and the belt creeps up-slope 1.18
voxels a turn, whichever way the axle turns. The belt is drawn still, as the gear cutter's is: a plain belt
moving along its own length looks the same, and the rollers, the deflector and the worm show its motion.
The **feed box** (on the frame, over the belt near the head) takes the table's middlings by a chute from
the middlings box's spout, over the tailings launder; the **water distributor** over the head roller takes
water from the vanner's cock by a pipe over the table, an open drop into its inlet box. The concentrate
rides over the head into the tank, whose spout pours into the concentrate launder; the tailings run off
the foot into a **tails box** beside it, whose spout pours into the tailings launder.

**The amalgamation plates** (`plates`, from tier 2). Three copper plates (5.5 wide, 3.5 long, 0.25 thick)
laid end to end in the plate table's bed, and an iron riffle across the bed after them, 0.8 high: the trap,
where the amalgam the pulp carries off the plates settles. The concentrate from the launder's lip lands on
the head plate and runs over all three and the trap to the notch. **The dressing** (`dressing`): the mercury
rubbed into each plate, a thin silvery coat (0.06, the game's silver sheet texture) inset 0.15 from the
plate's edges, and the mercury lying in the trap's well. The plates are fitted bare (new copper); dressed,
they catch free gold and silver as amalgam. Nothing moves: no driver.

## Every moving part

θ is the axle's angle, ψ its travel either way (the main shaft's angle). Ratios are radians per radian of
the driver's input; slides in voxels.

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft, A1, A2 (`entry`) | The vanilla axle | B1; the idler | `rotate` x 1 (θ) |
| B1 and its catch (`rectb1`) | A1 | The main shaft when the axle turns − | `rotate` x −1 (θ) |
| Idler (`idler`) | A2 | B2 | `rotate` x −1 (θ) |
| B2 and its catch (`rectb2`) | The idler | The main shaft when the axle turns + | `rotate` x 1 (θ) |
| Main shaft (`mainshaft`) | Whichever catch bites | Every tier's pulley | `rotate` x 1 (ψ) |
| Rake's pulley on the main shaft (`ltpulley`) [longtom] | Keyed to the main shaft | The rake's belt | rides `mainshaft` |
| Rake's crank shaft, pulley, throw (`ltcrank`) [longtom] | The belt (1:1) | The rake's yoke | `rotate` x 1 (ψ) |
| Rake: crosshead, shoes, tines, yoke (`ltrake`) [longtom] | The crank pin in the yoke's slot | Combs the gravel | `slide` z 3 sin (ψ) |
| Long tom's fixed parts (`longtom`) [longtom] | — | — | none |
| Jig's pulley on the main shaft (`jgpulley`) [jig] | Keyed to the main shaft | The jig's belt | rides `mainshaft` |
| Jig's crank shaft, pulley, two throws (`jgshaft`) [jig] | The belt (4:5) | The plungers' yokes | `rotate` x 0.8 (ψ) |
| Plunger 1, rod, yoke (`jgplunger1`) [jig] | Its crank pin | The water through sieve 1 | `slide` y −1.5 sin (0.8 ψ) |
| Plunger 2, rod, yoke (`jgplunger2`) [jig] | Its crank pin (opposite) | The water through sieve 2 | `slide` y 1.5 sin (0.8 ψ) |
| Jig's fixed parts (`jig`) [jig] | — | — | none |
| Table's pulley on the main shaft (`tbpulley`) [table] | Keyed to the main shaft | The table's belt | rides `mainshaft` |
| Head motion's input shaft and pulley (`tbinput`) [table] | The belt (4:5) | The toggle in the box | `rotate` x 0.8 (ψ) |
| Deck: boards, joists, riffles, launder, feed box, lip, bracket, pull rod (`tbdeck`) [table] | The pull rod (the head motion) | Shakes the ore | `slide` x 0.6 sin (0.8 ψ), `slide` x −0.3 sin (1.6 ψ) |
| Table's fixed parts (`table`) [table] | — | — | none |
| Vanner's pulley on the main shaft (`vnpulley`) [vanner] | Keyed to the main shaft | The vanner's belt | rides `mainshaft` |
| Vanner's crank shaft, pulleys, three throws (`vncrank`) [vanner] | The belt (1:1) | The frame's yokes; the small belt | `rotate` x 1 (ψ) |
| Shaking frame: rails, arms, ties, yokes, bearings, belt, feed box, distributor (`vnframe`) [vanner] | The crank pins in its yokes | Carries the rollers and the belt | `slide` z 1 sin (ψ) |
| Worm shaft, pulley, worm (`vnworm`) [vanner] | The small belt (1:1) | The worm wheel | `rotate` x 1 (ψ), rides `vnframe` |
| Head roller and worm wheel (`vnhead`) [vanner] | The worm | The belt | `rotate` z −1/16 (ψ), rides `vnframe` |
| Foot roller (`vnfoot`) [vanner] | The belt | — | `rotate` z −1/16 (ψ), rides `vnframe` |
| Deflector roller (`vndeflect`) [vanner] | The belt (outside the loop) | — | `rotate` z 0.2083 (ψ), rides `vnframe` |
| Carrying rollers (`vncarry1`, `vncarry2`, `vncarry3`) [vanner] | The belt | — | `rotate` z −0.15625 (ψ), ride `vnframe` |
| Vanner's fixed parts (`vanner`) [vanner] | — | — | none |
| Amalgamation plates and their trap (`plates`) [plates] | — | — | none |
| The plates' mercury dressing (`dressing`) [dressing] | — | — | none |
| Frame (`frame`) | Static | — | none |

Toothed wheels and what they mesh: A1 ↔ B1; A2 ↔ idler ↔ B2; the worm ↔ the worm wheel. Every pulley drives
a belt; the rollers carry the vanner's belt. Each tier's pulley on the main shaft is its own (it goes and
comes with its set), keyed to the shaft.

**Why the drive is the frame's.** The power cell is fixed and the frame is placed first, so the frame takes
the axle and turns it into one forward-turning main shaft under the roof; each tier brings only what it needs
to take power from it (a pulley, a belt and its own countershaft), since the tiers take it at different
places and paces. The rectifier is in the frame because two tiers care which way their shaft turns (the
table's quick return and the vanner's creep); the rake and the plungers would not, but run off the same shaft.

## The rig (`assets/seraphhorizons/config/concentrator-rig.json`)

The bucking sawmill's rig format (`../BuckingSawmill/README.md`, "Rig schema") with no new driver: rotations
on θ and ψ and sinusoidal slides on ψ. There is no `work`: nothing moves with progress, only with the shaft.

**Keys.** `cells` (90, with lids over every column: the roof deck at the plates' top), `powerCell` and
`powerFace`, `waterCell` and `waterFace`, `feedCell` and `feedFace`, `feedSpout`, `waterSpout`,
`concentrate` and `concentrateSide`, `tailings` and `tailingsSide`, `tiers` (each tier's number, name and
`requires` values), `plates` (`requires` `plates`, `dressing` `dressing`, `fromTier` 2: one optional part at
any tier from tier 2, the dressing drawn over the plates while they are dressed), `drive` (`strokesPerTurn`
per tier, `beltPerTurn`) and `parts`. `requires` values: `longtom`, `jig`, `table`, `vanner`, `plates`,
`dressing`, or null (the frame's parts: `entry`, `rectb1`, `idler`, `rectb2`, `mainshaft`, `frame`).

**Cells.** Built, as the other machines' are, from the shipped shape posed at rest by the shipped rig, with
every tier's parts at once (the eidolon gantry's are built with every winch stage). Two cells hold nothing
and are `hollow`.

**In the viewer** (`site/models.json`): one select, **Tier** (the scenario's `states`, as the eidolon
gantry's): the frame alone; tier 1; tier 2; tier 2 with the plates bare; tier 3; tier 3 with the plates
dressed; tier 4; tier 4 with the plates dressed, which it opens at (everything fitted). One list rather than
a second select for the plates, so the plates are only ever offered from tier 2; the checkboxes under it
still fit or take off the plates and their dressing by hand; θ's slider and Play turn the axle, the
"turn backwards" toggle shows the rectifier keeping everything after it running forward. `tiers` and `drive`
are listed as not drawn.

## Regenerating

```sh
python3 mods-src/seraphhorizons/Concentrator/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Concentrator/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Concentrator/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

It writes `assets/seraphhorizons/shapes/block/concentrator.json` (everything, 1060 elements),
`concentrator_frame.json` (the frame part only, 114), the rig and `tests/Concentrator/rig-reference.json`
(every part's matrix at 15 poses). A full run takes about 45 seconds. Output is deterministic.

## Validation (`tools/validate_concentrator.py`)

Every run, and the script exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part. **Tiers:** every part the frame's, one tier's or the plates'; no part riding another tier's; the
  rig's tiers the generator's states; the plates only on a tier from 2, dressed only when fitted.
- **Textures by role:** oak timber (and the entry's oak cross), oak planks for boards, iron castings and
  plates, steel shafts, gears, pins and pulleys, the mesh for the riddle and the sieves, leather belts, ppex's
  lead for the water pipes, brass cocks, copper plates and their silvery mercury dressing.
- **Nothing floats:** the frame alone and the frame with each state's parts (each tier, and tiers 2 to 4
  with their plates) at rest are one piece from the ground.
- **Containment:** nothing leaves the 6 × 3 × 5 box at any sampled angle, every tier fitted at once.
- **Anchors:** the entry's oak cross meets the power face at the cell's centre in the axle's profile; the
  water inlet ends on the water face's centre in ppex's 6 × 6 section, flanged; the feed chute comes in
  through the feed cell's face; the outlets are on the end faces; the power, water and feed cells differ.
- **Flows:** material dropped at each lip lands first in its receiver: the feed and the water on every tier's
  head; the riddle's fines in the riffle box; each tier's tailings in the tailings launder and its
  concentrate in the concentrate launder (the deck's end and edges and the vanner's feed box checked through
  their shake); the middlings into the tailings launder at tier 3 and the vanner's chute at tier 4; the
  concentrate launder's lip onto the bare bed, or onto the head plate (bare, or its dressing), and the pulp
  past the trap onto the bed before the notch; the tailings launder out of the west face and the plate
  table's spout out of the east.
- **Gearing:** the rectifier's three meshes tangent and rolling alike; for either sign of the axle exactly
  one loose wheel carries the main shaft, always forward; the worm on its wheel's pitch circle, the wheel a
  sixteenth of a turn a worm turn; the head roller's top running up-slope either way.
- **Belts:** each pulley pair's rims at one speed, the runs on their tangents.
- **Yokes:** at every step of a turn each crank pin is on its yoke's slot line (the yoke follows exactly)
  and inside its length; the rake's tines stay over the riddle inside the tom.
- **Supports:** every shaft in two bearings or more (the main shaft three), the idler on its stud in the
  cheek's arm, the rollers in the frame's bearings and arms; the plunger rods through their eyes; the deck's
  joists on their slides and the vanner's rails on theirs through a turn.
- **Clearances:** at 24 steps of a turn in each tier (and the frame alone, and each tier with its plates
  at 4 steps), no two parts touch but the intended contacts listed in `ALLOWED`; and (full runs) the moving
  parts every 3.75° of a turn in each tier.
- **No z-fighting** (full runs): no coplanar overlapping faces in any tier at four angles, nor with the
  plates bare at tier 2 and dressed at tier 4, after `fix_coplanar` (which sees only the parts that show
  together in a state).
- **Files:** every texture declared; lids over every column; the power, water and feed cells and the
  controller's not hollow; the shipped model is the checked one moved.

`tools/tests/test_concentrator_model.py` holds the written files to these without the generator's state
(the reference poses, the cells, the rectifier, the quick return, the creep, the anchors, the site's tier
states against the rig's tiers, and this README's parts table); `site/test/concentrator.test.ts` replays
the reference poses in the viewer's maths and checks the tier states.

## Element names

The rig's interface (first-match globs, in the rig's order): `entry_*`, `rectb1_*`, `idler_*`, `rectb2_*`,
`mainshaft_*`; `ltpulley_*`, `ltcrank_*`, `ltrake_*`, `lt_*`; `jgpulley_*`, `jgshaft_*`, `jgplunger1_*`,
`jgplunger2_*`, `jg_*`; `tbpulley_*`, `tbinput_*`, `tbdeck_*`, `tb_*`; `vnpulley_*`, `vncrank_*`,
`vnframe_*`, `vnworm_*`, `vnhead_*`, `vnfoot_*`, `vndeflect_*`, `vncarry1_*`..`vncarry3_*`, `vn_*`; `pl_*`
(the plates and the trap) and `pd_*` (their dressing); and `fr_*` for the frame (the plate table's bed
`fr_pt_*`, the concentrate launder's lip `fr_cl_lip`). Hand edits are lost when the script runs again: port them into `make_shape.py`.

## Open for the owner

1. **Tier 1's powered motion.** The period long tom was static, its gravel stirred and forked on the riddle
   by a man with a shovel. Here a rake on a crosshead, pushed by a slotted crank, does that stirring across
   the riddle, and the tom keeps its static trough, riddle and riffles (what makes it a long tom). A rocking
   or shaking tom was the other option; it would turn it into a cradle (the hand tier's rocker, #716) or a
   shaking sluice. The rake combs across the tom, not along it, because its crank shaft, like every shaft,
   runs along x off the main shaft; a rake along the riddle would need a cross shaft and bevel gears.
2. **The head motion is a closed box.** Its toggle (or cam and spring) is not modelled; the deck's quick
   return is two harmonics of the input shaft's angle. Opening it (a cam and a return spring, Rittinger's
   way) is possible.
3. **Motions are drawn larger than life** so they read: the deck's stroke 1.56 voxels end to end (a period
   table's is about 3/4 inch, 0.3 voxel at a block a metre), the vanner's shake ±1 (about an inch), its belt
   1.2 voxels a turn (a period vanner's belt creeps about two hundred times slower than its shake), the
   plungers' stroke 3.
4. **Size and heights.** 6 × 3 × 5 blocks, 90 cells, is set by tier 4 (the table and the vanner side by side
   with their launders). The deck stands high (its top 25 voxels) so its middlings fall by gravity to the
   vanner's feed box; the tom, the feed chute and the cocks stand high with it.
5. **One set of collision boxes for every tier.** The cells' boxes are built with every tier's parts at once
   (as the gantry's are with every winch stage), so at tier 1 a player would bump into the table's legs. Boxes
   per tier would need the rig to carry them per tier (#734).
6. **No roof.** The wash house is an open frame (posts, plates, tie beams, braces); a roof of boards or
   shingles would be period-right but hides the works in the viewer and the game.
7. **The screens** (the riddle, the sieves) use the game's metal mesh texture, which has holes: the renderer
   must draw them where alpha cuts out, or they draw solid.
8. **No material or water is drawn**: no gravel on the riddle, no bed on the sieves, no water sheet or
   concentrate band on the deck or the belt.
9. **The vanner's tails** fall off the belt's curve at the foot into the tails box beside it, whose channel
   stops 0.3 short of the belt's west extreme.
10. **Outlets at the floor.** The concentrate leaves the east face and the tailings the west face at floor
    level, and the feed comes in about 2 blocks up; a chute to the next machine works downhill, as period
    mills stood on hillsides.
11. **The jig's spigots** are 2 voxels above the hutch's floor, so the jig's launder can fall to the
    concentrate launder; the hutch has no hoppered bottom.
12. **The water header** narrows to 4 × 4 past the inlet (ppex's 6 × 6 at the face only); the vanner's cock
    stands shut at tiers 1 to 3.
13. **The vanner's frame rides on slides**, not the Frue's flexible uprights, and its feed box and water
    distributor shake with it (fed by open drops); its belt dips through the tank under the head roller and
    climbs out over a deflector, a simplification of the Frue's tank rollers.
14. **The amalgamation plates' place, size and slope.** They treat the concentrate, after every tier's
    concentrating stage, at the one place all of it passes (the plate table between the concentrate launder
    and the outlet). Mills ran their whole pulp over plates on the battery apron, before concentration; if
    the plates should take the tailings, or everything, they would go on the tailings launder instead. The
    table is small (three plates 5.5 × 3.5 voxels, the bed a block long and under half a block wide) and
    flat for a plate table (1 in 12; mills used about 1 in 8), as it sits at the floor between the
    concentrate launder and the outlet: a bigger, steeper table needs the concentrate launder higher, which
    the jig's spigots and the vanner's tank spout hold down.
15. **The dressing is an overlay part**, `dressing`, drawn over the plates: dressed = `plates` and `dressing`
    fitted. Gameplay (#734) would draw it while the plates hold mercury. It could instead be a second copy of
    the plates in the amalgam texture, one or the other drawn (the rosser's pipes' way); the overlay keeps one
    geometry and the copper showing at the plates' edges.
16. **What each tier costs and its build stages** are not designed: each tier is one `requires` value, one
    set, as the brief asked; the order of fitting within the frame and each tier is for later.
