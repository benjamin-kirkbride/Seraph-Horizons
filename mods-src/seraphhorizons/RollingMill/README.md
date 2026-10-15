# Rolling mill

Part of the Seraph Horizons mod (`../README.md`): a mechanically powered **two-high mill with grooved rolls**
after Henry Cort's patent of 1783, rolling a hot iron bar into **angle iron**. A flat billet goes through three
passes of diminishing size, cut half in each roll, and comes out of the last as an angle of the press brake's
section (legs 4 voxels outside, 1 thick; `../PressBrake/README.md`, "The angle").

This folder holds the model's generator and its validation (`tools/`). **There is no gameplay yet**: no
`Core/`, no `Game/`, no switch, recipes or export record. The model is the first deliverable; what the mill
takes and makes, its build stages and its pace are the owner's to settle ("Open for the owner").

**The period machine.** Cort's grooved rolls replaced the slitting mill and the tilt hammer for finishing bar. A
period two-high mill has two heavy cast-iron **housings** (standards) on a bed, their open windows holding the
roll necks in **chocks with brasses**; a **top** and a **bottom roll**, whose barrels carry a row of grooves
(passes) of diminishing size, cut half in each roll; a **screw** through the top of each housing bearing on the
top roll's chocks, set by hand; a **pinion housing** beside the stand whose two equal **pinions** turn the rolls
at one speed in opposite directions, through short **spindles with wobbler (clover-leaf) ends in coupling
boxes**; a water wheel on the bottom pinion's shaft; and a **fore-plate** in front of the passes with a catching
side behind. Men with tongs fed the hot bar into a pass, caught it behind and handed it back over the top roll
for the next pass: the mill did not reverse.

**The period machine and this one.** Everything in the stand is the period mill's: housings, chocks and
brasses, the screws (they stand set: adjustment, not working motion), the rolls and their grooves, the pinion
housing, the spindles and wobblers, the fore-plate and the catching side. What differs:

- **Power** comes, as on every machine in the pack, from a vanilla axle at a power cell, not a water wheel. It
  enters along z on the south face and turns the bottom pinion's shaft through a **crown wheel and pinion**, 1:3
  (27 and 9 teeth): a period form of gearing (the mill-wright's crown wheel), turning the drive through a right
  angle and taking the axle down to a slow, heavy roll speed, as the wheel-driven mills ran theirs (a few tens of
  turns a minute). The axle's height is the bottom roll's, so the crown wheel's axis meets the entry shaft's.
- **The bar is handled by the mill, not by men** (see "The handling"): the period catcher's hand-back over the
  top roll is kept, the top roll's own crown doing it (its top runs back towards the front), with three powered
  helps, each with its cause in the model: a **tumbling shaft**, worm-driven from the entry shaft once a pass,
  whose **cams** tilt the **catcher's table** against the top roll's back, and whose **barrel cam** works the
  **transfer fingers** that move the rolled bar one groove along; and a **counterweighted fore tray** that takes
  the bar from the top roll and swings down under its weight, so the bar slides into the next groove. The
  finished angle is pushed off the table's east end and handed out.

**A MachineOil machine.** The two top chocks' brasses each have a sight-feed cup on their housing's cap, a pipe
down into the brass; its level is the rig's `oil` input, the fill (0..1) of the machine's MachineOil tank: one
tank, two faces, as the cylinder borer's bushings. The necks in their brasses are the machine's working bearings
and carry the rolling load, which is what MachineOil stands for on the borer, the draw bench and the gear
cutter. The cups sit on the caps, where a pipe reaches the top brasses from above; the bottom brasses, under the
rolls, have none drawn (two cups show the one tank, as the borer's two do). No oil anchor or fill face of its own.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, shared with the other machines and unchanged by this one (no new driver: every
motion is an existing one).

## The machine

**Footprint.** 24 cells: 4 wide (x, the rolls' axis), 2 high (y), 3 long (z, the bar's way). In the native frame
with the controller at `[0,0,0]`: x −2..1, y 0..1, z 0..2. The **controller** is `[0,0,0]`, the fore tray's
cell at the north end, where the billet is laid on: the operator's end. The bar travels south through the passes:
the fore tray (z 0), the stand (z 1), the catcher's table (z 2). The drive end is the west quarter (x −2): the
pinion housing, the crown wheel and the entry shaft. Placed as the draw bench is (the block's `side` the way the
player looks, the fore tray nearest them; to settle with the gameplay), the axle comes in at the far end on the
player's right.

**Power.** The vanilla axle comes in on the south face of `[-2,0,2]`, along z at the cell's centre (x 8, y 8
voxels in its cell), into the entry shaft: oak, the axle's cross profile, in its outer bearing, then a round iron
shaft north to the crown wheel's pinion under the pinion housing's west side.

**Anchors** (in `assets/seraphhorizons/config/rollingmill-rig.json`, blocks from the controller): `powerCell` and
`powerFace`; `infeedSide` north (the billet onto the fore tray); `outputSide` east with `output.pos` (the east face
behind the stand, where the finished angle is pushed to); `bite.pos` (the middle groove at the line of centres:
scale and sparks); `drip.pos` (the west cup's pipe, where it enters the cap).

**The work.** One class (k = 1, "thin" in the rig's per-class keys): a hot iron bar. The billet and each pass's
bar, in voxels (lengths about constant volume):

| Piece | Section | Length |
|---|---|---|
| Billet (in) | flat, 7 × 2 | 8 |
| Out of pass 1 | a 150° vee (apex up), legs 3.75 outside, 1.6 thick | 10 |
| Out of pass 2 | a 120° vee, legs 3.9 × 1.25 | 12.5 |
| Out of pass 3: the angle | 90°, legs 4 outside, 1 thick: the press brake's angle (`shapes/item/angle.json`) | 16 (four of the brake's 4-long angles) |

The bar is rolled apex up, its legs' inner tips its lowest points, on the pass line (y 13.55): it rests on every
table on them. Which metal, what goes in (a bar, a bloom, plate?) and what comes out (one long angle, or several)
are the gameplay's ("Open for the owner").

**Fitted parts** (the rig's `requires`; provisional: the build stages are worked out with the owner later). The
frame carries everything with `requires` null: the sleepers and sole plates, the housings with their chocks,
brasses, distance pieces, caps and screws, the pinion housing and its brasses, every bearing and post, the
fore-plate and delivery guide, the transfer's rails, the hinge's and the tray's bearings, the lift rods' guides,
the oil cups; and the entry shaft with its pinion and worm (as the borer's entry shaft, it is the frame item's).

| `requires` | Draws |
|---|---|
| `pinions` | The two pinions on their shafts and the crown wheel on the bottom one's |
| `spindles` | The two spindles with their wobbler ends and coupling boxes |
| `rolls` | The top and the bottom roll, each with its three passes, necks and west wobbler |
| `catcher` | The catcher's gear: the tumbling shaft with its worm wheel, two lift cams and the barrel cam; the two lift rods; the catcher's table; the transfer (slide bars, fingers and fork) |
| `tray` | The fore tray with its arms, pivot pins, counterweights and stop |
| `bar` | The work: the billet and the bar out of each pass (one class: `requiresClass` `thin` in the viewer) |

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. Textures are the game's, referenced: oak (`game:block/wood/debarked/oak`), wrought iron
(`game:block/metal/plate/iron`), the castings in `sheet-plain/iron5`, the rolls' and pinions' turned faces in
`sheet-plain/iron1`, brass (`sheet/brass1`), the oilers' glass in the Transparent pass and their oil
(`game:block/liquid/honey`), and the bar in the iron or the steel ingot's texture (`block/metal/ingot/iron`,
`.../steel`). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/rollingmill.json` | The whole machine, every moving part and both metals' bars (999 elements). |
| `assets/seraphhorizons/shapes/block/rollingmill_frame.json` | The static frame only (200 elements). |
| `assets/seraphhorizons/config/rollingmill-rig.json` | Footprint, anchors, the work, the mill's figures and the part rig (23 parts). |
| `tests/RollingMill/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

No blocktype, item or lang file exists yet: they come with the gameplay.

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north
to south) in voxels, measured from the machine box's north-west bottom corner. The controller is cell `[2,0,0]`
of it, so the shipped files are the build frame moved 2 blocks west and divided by 16.

**The stand.** Two cast-iron housings (x 28..31.5 and 56.5..60), each two posts (z 13..16.5 and 23.5..27) on a
foot and a cap bolted across their tops (y 23.5..27), stand on two sole plates tied across at their ends, on oak
sleepers, clear of the rolls' sweep. In each housing's window are a **bottom chock** and a **top chock**, cast-iron
blocks round the necks with a **brass** between (the brasses stand a little proud of the chocks' faces), two iron
**distance pieces** between the chocks, and the **screw** down through a boss on the cap onto the top chock, with a
capstan head and its bar. The screws stand set.

**The rolls** (axes along x at z 20; the bottom one at y 8, the top one at 19.5; collars of radius 5.5, 0.5 apart)
carry three **passes** in lanes at x 36.5, 44.5 and 52.5, eight apart: each a vee groove turned in the top roll to
the bar's outside and a ridge on the bottom roll to its inside, both 0.05 off the finished section, in four turned
steps a flank. The bar is rolled apex up, its legs' inner tips just over the bottom roll's collars, on the **pass
line** y 13.55. Each roll has a neck through each housing's brasses, and a wobbler (two crossed bars) on its west
neck's end, in a coupling box.

**The drive.** The **entry shaft** comes in on the south face at (x 8, y 8) and runs north in two bearings; its
**pinion** (9 teeth) meshes the **crown wheel** (27, its teeth standing out of its east face) on the bottom pinion's
shaft, whose axis it meets: the bottom pinion's shaft turns a third of the axle. That shaft runs from an outer
bearing west of the crown wheel through the **pinion housing** (two cast cheeks with brasses, tied over the top),
where the two equal **pinions** (23 teeth, module 0.5, on centres the rolls' 11.5 apart) mesh, the top one on a
stub shaft. From each pinion's shaft a short **spindle** (a body and a wobbler at each end) in two **coupling
boxes** turns its roll. The bottom roll's top runs south: it takes the bar through; the top roll's crown runs north.

**The handling.** A double-start **worm** on the entry shaft (z 40..45) turns the **worm wheel** (18 teeth) of the
**tumbling shaft**, along x at (y 4, z 42.4) under the catcher's table: once a pass, 9 axle turns. On it:

- two **lift cams**, under the table's ends, each lifting a **lift rod** by a roller at its foot; the rod slides in a
  guide and bears with a pad under a wear plate on the table's middle beam, 1.33 at the most;
- the transfer's **barrel cam**, a drum (radius 3) whose rib the transfer's fork straddles at its top.

The **catcher's table** (oak beams under an iron top in three strips, the fingers' slots between them; z 27.4..46.6)
is hinged on a pin through its side beams at its far end (y 12, z 45.6) and is tilted 24.6° by the lift rods. The
**transfer** is two slide bars on rails under the table, each with three **fingers** standing up through the slots
west of the three lanes, joined by a cross bar carrying the fork: a stroke of 8.3 east and back each pass. In front,
the **fore tray** (an iron plate on oak rails, its stop at the far end) hangs from stub pins on two posts by two arms;
counterweights on arms outboard of its bearings hold it up empty against the top roll's front, and a bar laid on it
swings it 55° down under its weight onto the **fore-plate**'s line (5° down towards the rolls), where the bar slides
into its groove; lane guides on the fore-plate keep it in line. Behind the rolls, a **delivery guide** carries the
bar from the rolls onto the table.

**The cycle**, the same each pass (u is the fraction of the pass, W = pass − 1 + u):

| u | What moves |
|---|---|
| 0.00..0.06 | The bar on the fore tray (the billet, at the first pass) swings down with the tray under its weight. |
| 0.06..0.12 | It slides down the swung-down tray and the fore-plate into its groove; from half way its end meets the rolls (the bite). |
| 0.12..0.22, 0.24, 0.27 | The rolls take it through (passes 1, 2, 3: 10, 12.5 and 16 long out, at the rolls' surface speed, 104 voxels a pass): it shrinks into the bite in front and grows out of it behind, onto the delivery guide. The tray, empty, swings back up. |
| then ..+0.08 | It runs out 8.4 onto the table, clear of the top roll. |
| 0.36..0.60 | The fingers push it one lane east (8; the last pass's angle to the table's east end, x 60.5, where it is handed out at W 2.6). |
| 0.60..0.72 | The table tilts up 24.6°: the bar's lowest line comes tangent to the top roll's back. |
| 0.72..0.82 | The top roll's back, running up, pulls the bar up the tilted table along its length (9.8) until its end is past the tangent point. |
| 0.82..0.88 | The top roll carries it over its crown: it turns 74.6° about the roll's axis as it rolls along it (7.0), tangent all the way, and comes to lie along the roll's front, on the empty fore tray. |
| 0.88..0.93 | It slides down the tray to its stop (10.0). |
| 0.74..0.98, 0.87..0.99 | The fingers come back; the table comes back down. |

The ride radius is 5.37, the most, across the bar, of the top roll's radius under it less the bar's bottom's height
there (the bar out of pass 2, over the third lane's notch); the bar out of pass 1 rides 0.57 high over the second
lane's.

Every moving part (θ the axle angle, W the job in passes, k the bar's metal: 1 iron, 2 steel, p its presence, oil
the tank's fill):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft, its pinion, the worm (`entry`) | The vanilla axle | The crown wheel; the worm wheel | `rotate` z 1 (θ) |
| Crown wheel (`crown`) [pinions] | The entry shaft's pinion, 9:27 | The bottom pinion's shaft | `rotate` x 1/3 (θ) |
| Bottom pinion and its shaft (`pinionbottom`) [pinions] | The crown wheel (keyed on its shaft) | The top pinion; the bottom spindle | `rotate` x 1/3 (θ) |
| Top pinion (`piniontop`) [pinions] | The bottom pinion | The top spindle | `rotate` x −1/3 (θ) |
| Spindles and coupling boxes (`spindlebottom`, `spindletop`) [spindles] | Their pinions, by the wobblers | The rolls, by the wobblers | `rotate` x ±1/3 (θ) |
| Rolls (`rollbottom`, `rolltop`) [rolls] | Their spindles | The bar: through the passes; over the top roll's crown | `rotate` x ±1/3 (θ) |
| Tumbling shaft, worm wheel, lift cams, barrel cam (`tumble`) [catcher] | The worm, 2 starts on 18 teeth | The lift rods; the transfer's fork | `rotate` x 2π a pass (W) |
| Lift rods with rollers and pads (`liftrods`) [catcher] | The lift cams | The table | `gauge` slide y 1.33, a window a pass |
| Catcher's table (`table`) [catcher] | The lift rods (up); its weight (down) | The bar, up to the top roll | `gauge` rotate x 24.6° about its hinge, a window a pass |
| Transfer: slide bars, fingers, fork (`transfer`) [catcher] | The barrel cam's rib | The bar, one lane | `gauge` slide x 8.3, a window a pass |
| Fore tray, arms, pins, counterweights, stop (`tray`) [tray] | The bar's weight (down); the counterweights (up) | The bar, down to its groove | `gauge` rotate x 55° about its pivot, from each bar's laying on until the rolls have taken it |
| The billet (`billetiron`, `billetsteel`) [bariron, barsteel] | The tray (down); its weight (into the groove); the rolls (shrinks into the bite) | (none) | `gauge` stretch z to 0 at the bite; slide z; rotate x (the tray's slope; the swing) |
| The bar out of each pass (`bar1iron`..`bar3iron`, `bar1steel`..`bar3steel`) [bariron, barsteel] | The rolls (grows out of the bite, runs out); the fingers; the table; the top roll; the tray; its weight | (none) | `gauge` stretch z from nothing over its pass, to nothing over the next; slides z along its length (run-out, pull, over, slide, feed); slide x a lane; rotates x about the hinge, the top roll's axis and the tray's pivot. The angle (`bar3*`): stretched to nothing at hand-out |
| Oil in the cups (`oillevel`) | The machine's oil tank (MachineOil) | (shows it) | `stretch` y, 0.05 → 1.34 tall (oil) |
| Frame (`frame`) | Static | (none) | none |

**The gear train.**

| Stage | Teeth | Ratio |
|---|---|---|
| Entry shaft's pinion to the crown wheel | 9 : 27, module 1/3, pitch radii 1.5 and 4.5, axes meeting | the rolls 1/3 of the axle |
| Bottom pinion to top pinion | 23 : 23, module 0.5, centres 11.5 (the rolls') apart | equal and opposite |
| Worm on the entry shaft to the tumbling shaft's wheel | 2 starts : 18 teeth | once a pass, 9 axle turns |

Toothed wheels and what they mesh (nothing toothed meshes nothing): the crown wheel and its pinion, the two
pinions, the worm and its wheel. The cams, the drum, the rollers and the couplings are plain.

### Regenerating

```sh
python3 mods-src/seraphhorizons/RollingMill/tools/make_shape.py              # rewrites the four files
python3 mods-src/seraphhorizons/RollingMill/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/RollingMill/tools/make_shape.py --quick      # skips the z-fighting fix and samples less (not for files that ship)
```

The generator is stdlib only. A full run takes about four and a half minutes, nearly all of it the z-fighting fix
(a quick run, four seconds). `tools/validate_rollingmill.py` holds the checks; every run checks its output and exits
non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part;
  no part but the frame that a renderer would never draw.
- **Containment:** nothing leaves the 4 × 2 × 3 box over the job, both metals.
- **Anchors:** the entry shaft meets the south power face at the power cell's centre; the output on the east face;
  the drip anchor at the west cup's pipe; every anchor inside the machine.
- **Textures** by role: oak timber and the axle's continuation; the castings; the rolls and pinions turned bright;
  brass brasses and oilers; wrought iron for shafts, plates, pins and links; the bar in its metal.
- **Gearing:** the crown wheel's and its pinion's pitch arcs equal; the pinions equal and opposite on centres their
  pitch diameter apart, the spindles and rolls with them; no tooth of a pair more than 0.12 into another over 40
  angles; the tumbling shaft once a pass at the drawn pace (`turnsPerPass` the worm wheel's teeth over the worm's
  starts).
- **Passes:** each pass's section lies in its groove with both rolls 0.05 off it, sampled across it, and no further
  than a turned step allows; the last section is the press brake's angle; the bar drawn out of each pass is its
  section, in its lane, on the pass line.
- **The bar's way** (both metals): it grows out of the line of centres as it is rolled; lies on the table's top from
  its run-out until it is pulled (its far end until it leaves); each finger meets its bar's west tip as it pushes; it
  moves one lane; over the crown its lowest line stays the ride radius from the top roll's axis; it lies on the tray
  and comes to its stop, swings down on it and reaches the bite (its entering end on the line of centres, on the pass
  line), where it stays while the next pass takes it; the billet the same from the tray; the angle reaches the
  delivery place and is drawn no more after hand-out.
- **Cams:** over 96 points of a pass, each lift rod's roller on its cam (−0.05..+0.25: the cam is drawn in 3° spokes,
  each the least rise in its bin), each rod's pad under the table's wear plate (−0.05..+0.15), the fork's prongs off
  the barrel cam's rib along their length.
- **Supports:** each shaft in its bearings (the bottom pinion's three, the top pinion's two, each roll's two brasses,
  the entry shaft's two, the tumbling shaft's three, the table's hinge and the tray's pivot two each), each lift rod
  in its guide, up and down.
- **Nothing floats:** every frame element joined to the ground.
- **Clearances:** over 303 poses (the job every 0.01 of a pass, the metals alternating) no two parts touch except the
  intended contacts in `ALLOWED` (shafts in bearings, gears in mesh, wobblers in their boxes, followers on cams, the
  bar on the tables, the tray, the fore-plate and the delivery guide, the fingers on the bar) and the bar in the rolls
  while it is fed into the bite and rolled, and over the top roll while it is carried over.
- **Oiler:** each cup's level 0.05 tall empty and 1.34 full, linear between, seen through glass from every side.
- **No z-fighting** (full runs): no coplanar overlapping faces at six poses of both metals (the other metal's bar
  left out of the fix and the check: `shown`).
- **Files:** every texture declared; lids over every column whose top cell is not hollow; the shipped model is the
  checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, leaving out the bar.
The four top cells over the catcher's table (y 1, z 2) hold nothing at rest and are `hollow` (the table tilts up
through them), with no lid.

**A posing caveat.** machinegen's `posed` sizes a scaled element's axes by summing the scales its components fall on,
which is right for an element square to the world and overstates a turned one; the bar's legs are turned about z and
grown and shrunk along z, so the generator poses with its own `posed_el`, which measures each axis by how far the
matrix stretches it. The game and the viewer apply the matrix to the vertices and are unaffected. Fixing `posed`
itself would hold every machine to the byte-identical regeneration gate; it is left as it is (see "Open").

### Rig schema (`rollingmill-rig.json`)

The cylinder borer's schema (`../CylinderBorer/README.md`, "Rig schema"), with nothing added to the shared rig maths: a
`work` quantity, windowed gauges (slides, rotations and stretches), rotations on θ and on W, and a `stretch` on the oil.

- **The work.** `{ "name": "passes", "unit": "passes", "step": 0.005, "end": { "thin": 3, "thick": 3 } }`: one pass a
  unit, the three passes the job. W is geared to the axle at `mill.turnsPerPass` (9) axle turns a pass: the tumbling
  shaft turns once a pass on W and the worm on θ, and they agree only at that pace (as the gear cutter's camshaft and
  worm). The cycle's motions are gauges over windows of W.
- **Inputs.** θ (the axle; the rolls turn θ/3, so the viewer's θ slider spans three turns); W, k and p; the oil. k is
  the bar's metal (1 iron, 2 steel, 0 none): two sets of bar parts with the same geometry and motions, each moving for
  its own class only. p its presence; ψ (travel) is read by nothing.
- **`mill`**: `turnsPerPass` (9), `rollRatio` (1/3), `passes` (3), `handOut` (2.6: W at which the finished angle, at
  the table's east end, leaves the model), `sections` (each groove's vee: included angle, leg, thickness), `lengths`
  (the bar out of each pass), `billet`, `laneX` (each groove's centre, blocks from the controller).

**Keys.** `cells`, `powerCell` and `powerFace`, `infeedSide`, `outputSide`, `output`, `bite`, `drip`, `work`, `mill`
and `parts`. `requires` values: `pinions`, `spindles`, `rolls`, `catcher`, `tray`, `bariron`, `barsteel`, or null.

**In the viewer** (`site/models.json`, `rolling-mill`): the size select names the metals ("Iron bar", "Steel bar"),
each metal's bar shows only with it (`requiresClass`), the bar glows at the chosen heat, the oil has a slider, and Play
turns the axle at the reader's input speed (starting at 4 a second) and moves W with it at `mill.turnsPerPass`. A
standalone copy for review: `cd site && node --import tsx scripts/standalone-viewer.ts rolling-mill ../build/rollingmill-viewer.html`.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`; `crown_*`; `bpin_*`,
`tpin_*` (the pinions); `bsp_*`, `tsp_*` (the spindles); `broll_*`, `troll_*` (the rolls: `_bNN` the barrel's steps,
`_neckw`, `_necke`, `_wob`); `tumble_*` (`_cam1_`, `_cam2_`, `_drum`, `_ribNN`); `lrod*`; `transfer_*`; `table_*`;
`tray_*`; `ibillet_*`, `ibar1_*`..`ibar3_*` (iron), `sbillet_*`, `sbar1_*`..`sbar3_*` (steel); `oillevel_*`; and `fr_*`
for the frame. Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a standalone
copy). No one has looked at it in a client, and nothing of it is in the game yet.

1. **Box teeth.** The gears' teeth are boxes and mesh by their pitch circles, with up to 0.12 of overlap allowed. The
   worm is a helix of short boxes; its mesh with the wheel is an intended contact, not checked tooth by tooth.
2. **Turned steps.** Each flank of a groove or a ridge is four steps of a 12-gon, not a cone; the rolls stand 0.05 to
   0.76 off the finished section (the step's depth on the steepest flank).
3. **A rigid bar.** The bar is carried over the top roll as a straight bar tangent to it (turning about the roll's
   axis as it rolls along it), not bent over it. The bar out of pass 1 rides 0.57 over the collars it rests on there,
   as the ride radius is the bar out of pass 2's.
4. **The rolling is drawn, not formed.** In a pass the old section shrinks into the bite from its far end while the
   new one grows out of the line of centres; the old, thicker section overlaps the rolls while it is fed and rolled.
5. **Linear ramps.** The table, the lift rods, the transfer and the tray move in straight ramps, and the cams are
   drawn from the same ramps: the lift rods' rollers stay within −0.05..+0.09 of their cams.
6. **A steep barrel cam.** The transfer's stroke (8.3) over 0.24 of a pass leans its rib up to 59°: a real barrel cam
   would want a bigger drum or a lever. The fork's prongs are short and stand 0.42 off the rib, loose on the dwells.
7. **Weight and timing.** The fore tray swings down under the bar's weight and back up under its counterweights; the
   rig times both by W, at the moments the bar is laid on and taken by the rolls.
8. **The drawn pace.** The tumbling shaft turns on W and the worm on θ; they agree only at 9 axle turns a pass. A
   different gameplay pace would show at the worm.
9. **Reversing.** The rolls turn with the axle either way; the handling only goes forward (W). Turned backwards, the
   rolls would run against the bar, as the borer's cutters rub.
10. **The footprint is a box.** The four top cells over the catcher's table are hollow, and the west one (over the
    entry shaft) never holds anything.
11. **Hand-out.** The finished angle is pushed to the table's east end and drawn to nothing at W 2.6, over 0.005: the
    gameplay hands it out there; the table still tilts, empty, on the third pass.
12. **`posed`.** See "A posing caveat" above.

## Open for the owner

1. **The powered handling** (the main choice). Chosen: Cort's own return, over the top roll, done by the top roll,
   whose crown runs back towards the front, with three helps each with a visible cause: the **catcher's table**, tilted
   by cams on a tumbling shaft worm-driven from the entry shaft, lays the bar against the top roll's back, which pulls
   it up and over; the **transfer fingers**, on a barrel cam on the same shaft, move it a groove along first; the
   **counterweighted fore tray** takes it from the roll and swings down under its weight, so it slides into the next
   groove. Why: it keeps the period mill's non-reversing two-high stand and its hand-back, needs no new driver, and
   every motion has a cause in the model; the gear cutter's camshaft is the precedent for a worm-driven cam shaft geared
   to the work. Alternatives: **driven table rollers** and a lifting table (the later 19th century's), simpler to read
   but no hand-back; a **three-high stand** (a later development), which rolls both ways and needs no return, but is not
   Cort's two-high; a **single finishing pass** from a bar formed elsewhere, which needs no handling at all but loses the row
   of passes; or a **carriage on an overhead traveller** carrying the bar from back to front.
2. **The drive.** A crown wheel and pinion, 1:3, from an axle along z on the south face at the drive end: chosen
   because a mill-wright's crown wheel is period gearing, turns the drive square to the rolls and lets the axle come in
   at the bottom roll's height; a spur pair from an axle along x would do the same work.
3. **The pace.** 9 axle turns a pass (27 a billet), the rolls a third of the axle; any gameplay pace must equal the
   drawn one (8 above) or the worm will show it.
4. **The work.** Drawn: one bar, iron or steel (the same geometry and passes), a flat billet 7 × 2 × 8 rolled to an
   angle 16 long. To settle: the metals (the press brake's angles are lead and copper; rolled angle is iron's), what
   goes in (a bar, a bloom, a plate, heated to working temperature?), what comes out (one long angle, or four of the
   brake's 4-long ones), and whether it must be hot.
5. **The fitted parts.** `pinions`, `spindles`, `rolls`, `catcher` and `tray` are provisional ids; the build stages
   are to be worked out.
6. **MachineOil.** Chosen: a sight-feed cup on each housing's cap for the top brasses, one tank, as the borer's
   bushings; or a cup on all four chocks, or grease and no tank.
7. **The footprint.** 4 × 2 × 3, 24 cells (the crucible furnace's size). Two passes would fit three blocks wide; three
   need four with the pinion housing and the spindles in line.
8. **The screws** stand set. A period mill set its roll gap with them by hand: a settable gap (a section size) would be
   gameplay, and nothing moves for it now.
9. **The fore tray** stands up at 50° against the top roll's front when empty, so from the front it hides the passes.
10. **machinegen's `posed`** (see above): a fix there would be checked by regenerating every machine byte for byte.

## Tests

- `tools/tests/test_rollingmill_model.py` (no game, no fetched mods): the shipped rig parses with the shared maths and
  uses the `requires` vocabulary, every non-frame part drawable; every element has a part and every part an element;
  every texture declared (and, with `VINTAGE_STORY` set, in the game); every third reference pose is the rig's own
  maths; the cells are rebuilt from the shipped shape; the work is three passes and the tumbling shaft turns once a
  pass at `turnsPerPass`; the rolls a third of the axle, opposite ways; the last section is the press brake's angle
  (and the angle item's legs); each bar is its pass's length, each metal moving for its own class only, the angle
  drawn to nothing at hand-out; three lanes a stroke apart; 24 cells, lids, the power face, the anchors; the oil level
  follows `oil`.
- `site/test/rollingmill.test.ts` replays every reference pose through the site's `rig.ts`, checks the inputs the
  viewer offers (three θ turns), the `requires` vocabulary, the bar's textures and every element's part;
  `site/test/models.test.ts` checks the scenario against the rig.
- `make_shape.py` checks its own output every time it regenerates the model.
- No C# or Atlas tests: there is no gameplay yet.
