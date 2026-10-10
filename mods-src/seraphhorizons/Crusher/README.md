# Crusher

Part of the Seraph Horizons mod (`../README.md`): the crushing stage of the ore-processing line (epic #684). One
machine **upgraded in place** (#711): a timber mill frame whose footprint is its tier 4 size from the start, and
the working parts each tier fits into it. Tier 1 is vanilla's pulverizer, not this model.

| Tier | Fitted (`requires`) | What it is |
|---|---|---|
| 2 | `mortar`, `camshaft`, `stamps` | A five-stamp battery: a hopper's worth of ore a load |
| 3 | `jaw` | A Blake jaw crusher: continuous, its product down a launder to the spout |
| 4 | `jaw`, `rolls` | The jaw crusher and crushing rolls under it, which also take the classifier's oversize |

Fitting the next tier's set takes the last tier's working parts off (they drop back), so the battery never
stands with the jaw; tier 4 keeps tier 3's jaw and adds the rolls. The rig's `tiers` key says this for gameplay.

**Status: the model only** (#729). This folder holds the generator (`tools/`); there is no block, block entity or
renderer yet (#728, #711). The model and its rig are what those will be built against.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator; the generic
half of it is `Machines/tools/machinegen/`, unchanged by this machine (no new driver: every motion is an existing one).

## The machine

**Footprint.** 27 cells, 3 × 3 × 3, fixed for every tier (#711: pipes, chutes and the tailings heap never move as
the machine is upgraded). In the shipped, native frame the controller is `[0,0,0]`, the bottom middle cell of the
north face, over the product spout: the cells run x −1..1, y 0..2, z 0..2. The shafts run along x (west to east);
the jaw crusher's length and the ore's way through it run along z.

**Anchors** (in `assets/seraphhorizons/config/crusher-rig.json`, shipped coordinates). The same cells at every tier:

| Anchor | Cell, face | Point | What comes or goes there |
|---|---|---|---|
| `powerCell`, `powerFace` | `[1, 0, 1]`, east | the face's centre | The vanilla axle, into the entry shaft |
| `infeedCell`, `infeedFace`, `infeed` | `[0, 2, 1]`, up | `[0.375, 3.0, 1.734]`, the feed hopper's mouth | Raw ore, by chute or by hand, into the frame's hopper |
| `outputCell`, `outputFace`, `output` | `[0, 0, 0]`, north | `[0.375, 0.05, 0.0]`, the product spout's lip | Crushed ore, out along the spout to the classifier (#730) |
| `oversizeCell`, `oversizeFace`, `oversize` | `[−1, 1, 1]`, west | `[−1.0, 1.363, 1.834]`, the oversize inlet's floor | The classifier's oversize, back into the rolls (tier 4) |

Feed in and product out are at the same cells at every tier. The oversize inlet is the frame's at every tier, but
only tier 4 has a chute behind it (into the rolls' hopper); at tiers 2 and 3 it leads nowhere (open for the owner).
The oversize comes in from the west rather than the north, beside the product spout, because the jaw crusher's
north bearer runs across the frame just where a chute from the north would pass under the jaw.

**Power.** One power input serves every tier. The vanilla axle comes in on the east face of `[1, 0, 1]`, along x at
the cell's centre (build frame y 8, z 24), into the entry shaft. A **rectifier**, as the draw bench's and the gear
cutter's, turns the **line shaft** the same way whichever way the axle turns: A1 (12 teeth) on the entry shaft meshes
B1 (20) loose on the line shaft directly; A2 (9) drives B2 (15) through an idler (8); each of B1 and B2 has a one-way
catch, and whichever turns the line shaft's way carries it, at 0.6 of the axle's travel. The line shaft runs west
through the east frame's middle post to an 8-tooth **pinion** inside the frame, and every tier's driven wheel
meshes that one pinion, in the same plane: the battery's bull wheel, the jaw crusher's spur wheel, the north roll's
wheel. The direction matters: the stamps' cams lift only one way round, and the rolls must turn towards their nip.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. Textures by role: oak (`game:block/wood/debarked/oak`) for the frame's timbers, the oak mortar block,
the guide girts and the entry shaft's continuation of the vanilla axle; oak planks (`game:block/wood/planks/oak1`)
for the hopper and the chutes; iron plate (`game:block/metal/plate/iron`) for castings, wheels and the jaw's frame;
plain steel sheet for shafts, small gears, shoes, dies, stems, jaw plates, toggles, rolls' shells and springs; and
`game:block/metal/mesh1` for the battery's screen. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/crusher.json` | The whole machine, every tier's parts together (955 elements). The rig's `requires` say which tier each part belongs to. |
| `assets/seraphhorizons/shapes/block/crusher_frame.json` | The frame only (46 elements). A block would draw it and the inventory show it. |
| `assets/seraphhorizons/config/crusher-rig.json` | Footprint, anchors, the work, the battery's figures, the tiers and the part rig (31 parts). |
| `tests/Crusher/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes in voxels, measured from the
machine box's north-west bottom corner, x 0..48, y 0..48, z 0..48. The shipped files are the build frame moved one
block west (−16 in x), so the controller is `[0,0,0]`.

**The frame** (every tier's). Two oak side frames, west (x 1..6) and east (x 37..42): sills, posts (north and south,
and on the east a middle post at z 21..27 that carries the entry shaft's and the line shaft's bearings), caps, and
three rails between the posts: the deck rails (y 17..21), the cam rails (y 26.7..30.2) and the upper rails
(y 38.5..41). Ties across the top at both ends, a cross sill at the south, two **bearers** across the deck rails
(y 21..24, at z 1.5..5.5 and 33..38) for the jaw crusher, and two **hopper beams** under the caps' top carrying the
**feed hopper** (four boards, its throat y 43.6 over z 24..32.5, x 18..26). At the north face the iron **product
spout** (x 16..28, on the ground); at the west face the iron **oversize inlet** on the west deck rail
(z 26.5..32.2). On the east, outside the frame, the rectifier's gears and the outer bearing bracket at the east face.
The frame carries both kinds of machine: the battery's camshaft on the cam rails and its guide girts on the deck and
upper rails; the jaw crusher on the bearers; the rolls on the ground between the side frames.

**Tier 2, the stamp battery.** Five stamps on a line along x (z 10.5, x 11.6 to 29.2, 4.4 apart), each a steel shoe
on an iron head, a steel stem through two oak guide girts (y 21..23.5 on the deck rails, y 41..43.5 on the upper
rails) and a square iron tappet. They stand in a cast-iron **mortar box** on an oak mortar block, each over a steel
die: ore comes in through the feed opening in the back wall (south), down the feed chute from the frame's hopper,
which passes behind and under the camshaft as a battery's feed does; crushed ore leaves through the **screen** in
the front wall (north) onto the **apron**, two guide boards turning it into the product spout.

The **camshaft** (y 32.16, z 15.2, south of the stems, in pillow blocks on the cam rails) carries ten involute
**cams**, a double cam beside each stem (its plate x 0.75..1.65 east of the stem's axis, so it sweeps past the stem),
lifting the tappet's underside on its edge on the camshaft's side. An involute of a base circle of radius 3 lifts a
flat follower 3 voxels a radian of the camshaft, at a fixed line, so the cam's face is that curve exactly: each arm is
12 chords of it (vertices on the curve, 1 deep behind), joined to the hub by a web. Each stamp is lifted 3.2 over
0.17 of a camshaft turn, let go as the arm's tip passes the tappet's edge (it moves in towards the camshaft as it
rises on), falls back onto its die over 0.02 of a turn, and rests there till its cam's other arm comes round: two
drops a stamp a turn, ten a turn in the order 1-3-5-2-4. The **bull wheel** (16 teeth) runs loose on the camshaft,
meshing the line shaft's pinion; a **cone clutch** on a feather on the camshaft goes 0.6 into a cup on the bull
wheel's hub while a load is on, thrown in and out by a hand **lever** whose yoke rides in the cone's grooved collar,
on a pin in a bracket off the east deck rail.

The battery is the one tier whose motion is the work's (W, camshaft turns into a load): the cams' faces and the
stamps' lifts are ramps, a stamp lifted at the cam's rate and dropped, which no sinusoid of the axle's angle can draw
with the stamp resting still on its die between lifts. So the camshaft turns with W, the stamps are gauges (one ramp
each lift and each drop, open to the end of the load, as the gear cutter's index is), and the clutch is where the
axle's half meets the work's: at the drawn pace (3.333 axle turns a camshaft turn) the cone turns with the bull
wheel, and a check holds this. A load is 4 camshaft turns (`stamps.revsPerLoad`), at whose end every stamp stands as
it did at its start (stamp 4 part way up on its cam), so loads follow one another without a jump; between loads the
clutch is out, the bull wheel turns and the camshaft stands.

**Tier 3, the Blake jaw crusher.** On the bearers, between two cast-iron cheeks (x 13..16 and 28..31): a steel
**fixed jaw** at the south (face z 33) and a **swinging jaw** hung from a hinge pin at its top (y 41.7, z 20.3), its
steel face sloping from z 23.5 at the mouth (y 41.5) to z 30 at the bottom (y 26.5), a gap of 3 there. The
**eccentric shaft** (y 34.54, z 16.39) runs in the cheeks' bearings with a **flywheel** outside the west cheek and an
18-tooth **spur wheel** outside the east, meshing the line shaft's pinion. Its eccentric (throw 1.2) works the
**pitman**, which hangs from it to its foot at the toggles' level; the foot carries the inner ends of two steel
**toggle plates**, the back one seated on the back wall (z 6.6) and the front one on a seat on the swinging jaw's back.
The toggles sag 2 below their outer seats at the eccentric's mean, so as it lifts the pitman they straighten and push
the jaw's lower end towards the fixed jaw; two **tension rods** from the jaw's lower back through the back wall, with
springs under their nuts, pull it back. The jaw's gap at the bottom runs 2.67..3.58: a throw of 0.91. The ore drops
into the mouth from the hopper, and the product falls out of the gap through the deck to a **launder** (iron, x 16..28)
on the ground, which slopes 1 in 13 north to the product spout.

The linkage is solved exactly (the pitman's, the back toggle's and the front toggle's lengths, by Newton's method, at
128 angles of the eccentric) and each moving body drawn at its mean pose; the rig moves it from there by the harmonics
of its exact motion in the eccentric's angle (up to four each, the axle's travel times n × the eccentric shaft's
ratio): a `swing` about its pivot, and for the pitman and the front toggle `slide`s in y and z too. A check holds every
pin of the posed rig to the exact linkage through a turn: within 1e-4 voxels.

**Tier 4, the rolls.** Under the jaw's discharge, two steel-shelled **rolls** 12 across and 12 long (x 16..28): the
north roll (y 12.04, z 25) in fixed bearings in its pedestals' housings, the south roll (z 38) in blocks in the
pedestals' slides with two springs behind each (drawn still: they would yield to tramp iron). Equal **pair gears**
(13 teeth) at their west ends turn them alike towards each other; the north roll's 16-tooth wheel at its east end
meshes the line shaft's pinion. Both their faces at the nip (z 31.5, a gap of 1) move down. A **hopper** of boards
over the nip takes the jaw's product and the **oversize chute** from the west face's inlet; the rolls' product falls
to the jaw's launder under them and out of the spout.

**Gearing and pace** (θ the axle's angle, ψ its travel either way):

| Shaft | Turns per radian of ψ | Through |
|---|---|---|
| Line shaft and its pinion | −0.6 | the rectifier, 12:20 (and 9:8:15) |
| Bull wheel (tier 2) | +0.3 | the pinion's 8 to its 16 |
| Camshaft (tier 2) | +0.3 with the clutch in: W advances 0.3/2π a radian, 3.333 axle turns a camshaft turn | the cone clutch |
| Eccentric shaft (tier 3) | +0.2667 | the pinion's 8 to its 18 |
| North roll, south roll (tier 4) | +0.3, −0.3 | the pinion's 8 to its 16; the pair gears 13:13 |

Every moving part (θ the axle angle, ψ its travel, W camshaft turns into a load, k the load: 1 or 2 alike, p its
presence):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft, A1, A2 (`entry`) | The vanilla axle | B1; the idler | `rotate` x 1 (θ) |
| B1 with its catch (`rectb1`) | A1 | The line shaft when the axle turns + | `rotate` x −0.6 (θ) |
| Idler (`idler`) | A2 | B2 | `rotate` x −1.125 (θ) |
| B2 with its catch (`rectb2`) | The idler | The line shaft when the axle turns − | `rotate` x 0.6 (θ) |
| Line shaft and pinion (`line`) | Whichever catch bites | The bull wheel, the spur wheel, the north roll's wheel | `rotate` x −0.6 (ψ) |
| Bull wheel and cup (`bullwheel`) [camshaft] | The pinion | The cone, while in | `rotate` x 0.3 (ψ) |
| Camshaft, its ten cams, collars (`camshaft`) [camshaft] | The cone (the clutch) | The tappets | `rotate` x 2π (W) |
| Cone, grooved collar (`cone`) [camshaft] | The bull wheel's cup (friction); the lever's yoke | The camshaft (a feather) | `rotate` x 2π (W); `gauge` slide x 0.6 (`present`) |
| Clutch lever (`clutchlever`) [camshaft] | The operator: in with a load on | The cone | `gauge` rotate z −0.059 rad (`present`) |
| Pillow blocks, the lever's bracket and pin (`camframe`) [camshaft] | Static | — | none |
| Stamps 1 to 5: shoe, head, stem, tappet (`stamp1`..`stamp5`) [stamps] | Its double cam | The ore on its die | `gauge` slide y: +3.2 over each lift, −3.2 over each drop, one ramp each (16 a stamp, 18 for stamp 4 with its `present` term) |
| Guide girts (`guides`) [stamps] | Static | — | none |
| Mortar box, dies, screen, apron, feed chute (`mortar`) [mortar] | Static | — | none |
| Eccentric shaft, eccentric, flywheel, spur wheel (`eshaft`) [jaw] | The pinion | The pitman | `rotate` x 0.2667 (ψ) |
| Pitman (`pitman`) [jaw] | The eccentric | The toggles' inner ends | `swing` x ×4, `slide` y ×4, `slide` z ×4 (ψ, harmonics) |
| Back toggle (`backtoggle`) [jaw] | The pitman's foot | — (its seat on the back wall) | `swing` x ×4 about its seat |
| Front toggle (`fronttoggle`) [jaw] | The pitman's foot | The swinging jaw | `swing` x ×4, `slide` y ×4, `slide` z ×4 |
| Swinging jaw (`swingjaw`) [jaw] | The front toggle | The ore | `swing` x ×4 about its hinge pin |
| Tension rods, nuts and pins (`rods`) [jaw] | The swinging jaw | The springs | `slide` y ×4, `slide` z ×4 |
| Rod springs, coil by coil (`spring1`..`spring3`) [jaw] | The nuts | — | `slide` z ×4, each its share of the nuts' travel |
| Jaw's cheeks, walls, fixed jaw, hinge pin, launder (`jawframe`) [jaw] | Static | — | none |
| North roll, its pair gear and wheel (`roll1`) [rolls] | The pinion | The south roll | `rotate` x 0.3 (ψ) |
| South roll, its pair gear (`roll2`) [rolls] | The north roll's pair gear | — | `rotate` x −0.3 (ψ) |
| Pedestals, springs, the rolls' hopper, the oversize chute (`rollframe`) [rolls] | Static | — | none |
| Frame (`frame`) | Static | — | none |

Toothed wheels and what they mesh (nothing toothed meshes nothing): A1 ↔ B1; A2 ↔ idler ↔ B2; the pinion ↔ the bull
wheel (tier 2), the eccentric shaft's spur wheel (tiers 3, 4) and the north roll's wheel (tier 4); the pair gears.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Crusher/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Crusher/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Crusher/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about a minute and a half, most of it the z-fighting fix and the swept paths. `tools/validate_crusher.py`
holds the checks; every run checks its output and exits non-zero if one fails. Checks that set parts against each
other run per build state (the frame; tier 2; tier 3; tier 4): a part is checked with the frame and its own tier's
parts, never another tier's, which it never stands with.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; the
  `requires` are the tiers' sets.
- **Nothing floats:** the frame one piece from the ground, and each build state at rest.
- **Containment:** nothing leaves the 3 × 3 × 3 box over 12 poses of every tier's parts.
- **Anchors:** the entry shaft meets the power face at the power cell's centre; the feed, product and oversize points
  on their cells' faces; tier 2's apron ends over the spout, tier 3's launder on its floor, tier 4's oversize chute at
  the inlet.
- **Textures** by role, every element ruled.
- **Gearing:** every meshing pair's centre distance is the sum of the pitch radii, and the arcs rolled on the two pitch
  circles are equal (finite differences of the posed rig): the rectifier's three, the pinion's three, the pair gears. The
  rectifier turns the line shaft the same way for either sign of the axle; the clutch's cone turns with the bull
  wheel through a load at the drawn pace, is in the cup with a load on and out of it without; the rolls turn alike,
  their faces at the nip moving down.
- **Stamps:** over 237 poses in the lifts every tappet rides its cam (at most 0.003 over its chords, never into them);
  each stamp rests on its die between lifts; at W 0 and at the end of a load, with the load's presence anywhere
  from 0 to 1, each stands where it is drawn; each drops twice a turn, in the order 1-3-5-2-4.
- **Jaw:** through a turn of the eccentric (72 steps) the pitman's strap on the eccentric, its foot on both toggles'
  ends and where the solved linkage puts it, the back toggle on its seat, the front toggle on the jaw's seat, the
  rods on the jaw's lugs, all within 1e-4; the toggles never pass straight; the gap at the bottom never under 1.
- **Supports:** the entry shaft and the line shaft in the east post's plates and the outer bracket, the camshaft in
  its two pillow blocks, the eccentric shaft in the cheeks' two bearings, the rolls in their housings and blocks, the
  hinge pin in the cheeks' hinge bearings, the idler on its stud.
- **Clearances:** over 133 poses (every build state at rest; the battery every 0.05 of a load; the jaw and the rolls
  at 24 angles of the eccentric each) no two parts touch except the intended contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the battery through a whole load every 0.004 of a camshaft turn, and the jaw and the
  rolls every 2° of the eccentric: 1181 poses.
- **No z-fighting** (full runs): no coplanar overlapping faces at six poses of the three tiers, each against its own
  tier and the frame.
- **Files:** every texture declared; lids over every column; the shipped model is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, every tier's parts
together (whatever is fitted lies inside them), as the rosser's and the gear cutter's are.
`tools/tests/test_crusher_model.py` and `site/test/crusher.test.ts` hold the written files to these rules without
the generator's state, and replay the reference poses.

### Rig schema (`crusher-rig.json`)

The gear cutter's and the draw bench's schema (`../DrawBench/README.md`, "Rig schema"), with nothing added to the
shared rig maths: a `work` quantity, gauges, rotations on θ and ψ, and swings and slides on ψ.

- **The work.** `{ "name": "load crushed", "unit": "camshaft turns", "step": 0.005, "end": { "thin": 4, "thick": 4 } }`:
  W counts camshaft turns into a load at the stamp battery. k is the load (1 or 2: two classes the model treats alike,
  for gameplay to give a meaning, e.g. raw ore and ore chunks); p its presence. The gameplay should advance W with the
  shaft at `1 / stamps.turnsPerRev` a turn from 0 when a load goes on, hold it at the end, ease p out, and start the
  next load at 0 (the stamps stand at both ends alike). Tiers 3 and 4 read no work: their parts follow the axle's travel.
- **`stamps`:** `turnsPerRev` (3.333333, axle turns a camshaft turn as drawn), `revsPerLoad` (4), `dropsPerRev` (2, a
  stamp), `firingOrder` ([1, 3, 5, 2, 4]).
- **`tiers`:** `{ "2": ["mortar", "camshaft", "stamps"], "3": ["jaw"], "4": ["jaw", "rolls"] }`: the requires values each
  tier fits. A renderer draws a part when its `requires` is null or in the fitted tier's list.
- **Keys.** `cells`, `powerCell`/`powerFace`, `infeedCell`/`infeedFace`/`infeed`, `outputCell`/`outputFace`/`output`,
  `oversizeCell`/`oversizeFace`/`oversize`, `work`, `stamps`, `tiers` and `parts`. `requires` values: `mortar`,
  `camshaft`, `stamps`, `jaw`, `rolls`, or null.

**In the viewer** (`site/models.json`, the site's model page): a **Tier** select (the scenario's `states`): the frame
alone, tier 2, tier 3 and tier 4 (the default), each with a note. The jaw and the rolls move with the axle; for the
battery, choose a load ("Load in the battery") and press Play: W runs with the axle at the drawn pace (`play.turnsPerWork`
`"stamps.turnsPerRev"`) through the load's four turns, then stands.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `rectb1_*`, `idler_*`,
`rectb2_*`, `line_*`, `bullwheel_*`, `camshaft_*` (the shaft, collars, hubs and cams: `camshaft_cam<stamp><a|b><nn>`),
`cone_*`, `clutchlever_*`, `camframe_*`, `stamp1_*`..`stamp5_*`, `guides_*`, `mortar_*`, `eshaft_*`, `pitman_*`,
`backtoggle_*`, `fronttoggle_*`, `swingjaw_*`, `rods_*`, `spring1_*`..`spring3_*`, `jawframe_*`, `roll1_*`, `roll2_*`,
`rollframe_*`, and `fr_*` for the frame. Hand edits are lost when the script runs again: port them into
`make_shape.py`, or stop regenerating.

## Choices made, and why

- **One power input, the frame's.** Every tier's wheel meshes the frame's line-shaft pinion, in one plane inside the
  east frame, so the axle never moves. The line shaft sits where the three tiers' shafts are within one gear pair of it;
  the battery's camshaft, the eccentric shaft and the north roll are placed on circles round it. The rectifier's B1
  is large (20 teeth) because the power cell's centre is two of its radii from the line shaft.
- **The battery runs on the work, the jaw and the rolls on the axle.** A stamp rests still on its die between lifts,
  which no driver of the axle's angle can draw (they are sinusoids), so the battery is the gear cutter's pattern: the
  camshaft on W, ramps for the stamps, a clutch where the axle's half meets the work's. Its loads are "a hopper's worth"
  (#728). The jaw and the rolls are continuous and run on ψ.
- **Cams beside the stems, lifting the tappet's edge.** An involute arm in the stem's own plane would sweep through the
  stem; beside it, it passes. The contact line is 0.2 inside the tappet's edge nearest the camshaft, so the arm's tip
  leaves the tappet as it moves in, and the falling tappet never meets it.
- **The jaw's harmonics.** The Blake linkage's motion is not a sinusoid; four harmonics of each body's exact motion hold
  every pin to it within 1e-4, with existing drivers.
- **Two flywheels, one of them toothed.** A Blake crusher has a flywheel at each end of its eccentric shaft; here the
  east one is the spur wheel the line shaft's pinion drives, as the drive plane inside the east frame leaves no room for
  a third wheel.
- **The oversize comes in from the west** (see Anchors).

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in the site's
viewer with the owner, or in a client.

1. **Box teeth and chords.** Gear teeth are boxes; a cam's face is twelve chords of its involute, so a tappet rides up
   to 0.003 over the true curve between vertices.
2. **Linear stamp motion.** A stamp rises at the cam's constant rate and falls at a constant rate over 0.02 of a turn
   (a real one falls under gravity, faster as it goes); it does not turn on its stem as a real one does.
3. **The springs do not yield.** The rolls' spring-held bearings and the jaw's rod springs are drawn as they would sit
   at rest with no tramp iron; nothing in the model moves them.
4. **The cells' boxes are every tier's together.** A block with one tier fitted would still collide where another
   tier's parts would be; per-tier boxes would need the rig to carry them (`tiers` could).
5. **No ore is drawn**, in the hopper, the mortar, the jaw or between the rolls.

## Open for the owner

1. **The fitting order within each tier.** Tier 2 has three `requires` (mortar, camshaft, stamps) and tiers 3 and 4 one
   each; the stages, their items and their order are to be worked out together, as for the gantry and the draw bench.
2. **Water.** #684 has the crusher draw water at every tier (a stamp battery is fed water with its ore; rolls were run
   wet). The model has no water pipe yet: a ppex pipe inlet on a fixed face (the frame's) and each tier's pipe to it
   would follow the feed and the spout's pattern.
3. **The oversize inlet at tiers 2 and 3.** It is the frame's, so the classifier's chute never moves, but nothing is
   behind it until tier 4. Should the oversize go to the battery's feed or the jaw's mouth before then, or be refused?
4. **The two load classes.** The rig's work needs two; the model treats them alike. Raw ore and ore chunks, or one
   class only?
5. **The load's length.** A load is 4 camshaft turns (13.3 axle turns) as drawn; a longer one is `STAMP_LOAD`, at 4 more
   gauges a stamp a turn.
6. **The battery's clutch lever** is thrown by hand in the model's story (in with a load on, out when it is done); a
   gameplay that runs unattended at tier 2 (hopper in, output collects, #711) might want a visible automatic trip
   instead, like the draw bench's knock-off.
7. **Size and power.** The battery, the jaw and the rolls are scaled down to fit three blocks; a real five-stamp
   battery stands about five blocks tall. Resistance figures for the power network are gameplay's.
