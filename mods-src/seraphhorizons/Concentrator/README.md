# Concentrator (wash house)

Part of the Seraph Horizons mod (`../README.md`): the ore line's gravity concentration stage, the spine of
the progression (#734, model #735, the ore processing epic #684). One machine upgraded in place (#711): an
open timber **wash house**, built at its final size, into which four **levels** of parts are fitted. A level
keeps the last level's parts and adds its own; the one part ever taken out is the sluice, which the jig
replaces at level 2 (see "The levels"). From level 2, **amalgamation plates** can be fitted as well: one
part, not a level of their own.

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what #734 will build against. The rig's per-level collision
(`fitted` boxes on its cells, below) is read by the site's viewer; **the game's rig loader
(`Machines/Core`) does not read `fitted` yet**, so #734 needs that first.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/` and `site/`, which are the repository's. The generic half of
the generator is `Machines/tools/machinegen/`, unchanged by this machine: no new driver.

## The machine

**Footprint.** 24 cells: 4 long (x, the line shaft's way), 2 high, 3 wide (z), the controller `[0,0,0]` the
north-west bottom corner, under the feed box. The old model was 6 × 3 × 5 (90 cells); this one keeps the four
machines and the plates in 4 × 2 × 3 by stacking them in one cascade, the feed at the top of the north-west
corner and the outlets at the floor, and by putting each drive straight on the line shaft.

**The levels** (the rig's `levels`; the site's **Level** select):

| Level | Tier | `requires` fitted | What it adds | #734's figures |
|---|---|---|---|---|
| — | | none | The wash house frame (below) | |
| 1 | water | `sluice` | A riffled sluice under the feed box, under the water line's head nozzle. No power | 65 %, 2 L/s, the player feeds it and cleans the riffles up |
| 2 | water | `lineshaft`, `jig` | The line shaft from the axle, and a Hartz jig **in the sluice's place**: the sluice is taken out | 80 %, 4 L/s, the player feeds the box, the output collects |
| 3 | mill | + `table` | A shaking table on the jig's tailings | 92 %, 8 L/s, continuous, chute-fed |
| 4 | mill | + `vanner` | A Frue vanner on the table's tailings, its countershaft and drive belt, its worm shaft and ratchet | 99 %, 4 L/s recirculated (a gameplay rule), unattended |
| 2 to 4 | | `plates` (+ `dressing`) | Optional, alongside the level: three copper plates and a mercury trap in the plate table; `dressing` is their mercury coat | Free gold and silver caught as amalgam |
| all | | `pipecopper` or `pipelead` | The water line in the metal supplied (one of them is drawn) | |

**Why the sluice is retired.** A period wash house that put in jigs took its sluices out: the jig does the
sluice's job (catching the coarse, heavy grains at the head of the line) better and from a hopper, and a
sluice before a jig would only be in the way. So the jig stands where the sluice stood, under the feed box,
and the sluice's parts come off at level 2. Every other level adds to the last. Running jigs on a sluice's
tailings was not done; if the owner prefers the sluice kept, it can stay only as a tail sluice somewhere
else (it would need its own place and launder).

**The returns (the owner's ruling).** Nothing inside the machine carries a stream back: no return launder,
sump, pump or elevator. A return stream leaves at a plain output anchor and is handled outside the machine,
by placing machines so one feeds another below it, or later by Archimedes screws. Here that is level 4's
water: "recirculated" is a gameplay rule (its draw is lower), not geometry, and the one ppex water inlet
stays. The water leaves over the tail box's overflow at `returnWaterCell`'s east face, a cell apart from the
tailings' spigot.

**The fixed points** (the frame's, the same at every level; the rig's anchors, in blocks):

| Anchor | Where | What |
|---|---|---|
| `powerCell`, `powerFace` | `[0,1,1]`, west | The vanilla axle comes in along x at the face's centre (y 24, z 24 voxels), onto the line shaft's oak cross (the axle's two 4 × 2 boards). Level 1 needs no power |
| `waterCell`, `waterFace` | `[1,1,0]`, north | A ppex pipe meets the water line's inlet end to end: ppex's 6 × 6 tube on the face's centre (x 24, y 24), the soldered pipe beyond bringing its own half joint, as against any block |
| `feedCell`, `feedFace` | `[0,1,0]`, north | The feed box against the north wall: a chute pours in over its low north wall (the mill levels), or the player tips the ore in (the water levels) |
| `feedSpout` | `[0.5, 1.5625, 0.7188]` | The feed box's spout lip, over the sluice's head, then the jig's sieve |
| `waterSpout` | `[0.875, 1.25, 0.8125]` | The water line's head nozzle, over the same place |
| `concentrate`, `concentrateSide` | west face, at the floor | The plate table's outlet spout: the concentrate's way out at every level, over the plates when they are fitted |
| `tailings`, `tailingsSide` | east face, `[3,0,2]` | The tail box's spigot: the settled tailings |
| `returnWaterCell`, `returnWaterFace` | `[3,0,0]`, east | The tail box's overflow lip: level 4's return water (above) |

**The flows.** The feed and the head nozzle's water fall onto the head of the line: the sluice's head at level 1,
the jig's sieve from level 2. Each machine's tailings either fall onto the next machine or, while there is
none, into a launder to the tail box: the sluice's tail into **launder N** (north side, along x); the jig's
weir spout onto the table's feed box, or into launder N at level 2; the table's tails launder (along its low
edge, falling south) onto the vanner's feed box, or into **launder S** (under the vanner) at level 3; the
vanner's tails off the belt's foot into launder S. Both launders fall east into the **tail box** across the
east end. Every concentrate reaches the **concentrate launder** (along the middle at the floor): the jig's
spigot, the table's lip at the deck's south end, and the vanner's concentrate box under its head roller, by
its spout. The launder falls west and spills onto the head of the **plate table** at the south-west, whose bed
falls south to the outlet spout through the west wall.

## How it works

Positions are in voxels in the generator's build frame (x west to east, y up, z north to south, from the
machine box's north-west bottom corner), which is the shipped frame times 16.

**The frame.** Oak posts 3 × 3 at the corners and the middle of the long walls, sills along the long walls,
wall plates and three tie beams at the top (y 29..32), knee braces (only in the end walls and the north wall
east of the feed box and the inlet; none in the south wall, the side the vanner is seen from). Fixed to it:
the **feed box** hung from the north plate with its spout; the water line's supports (an iron strap from the
middle tie beam, an oak post, an iron hanger from the south plate); **launder N** (z 8.5..15.5, falling east 1 in 25) and **launder S** (z 36..41.6), the **tail box** (x
54.5..62, z 4..44) with a notch in its west wall for each launder, its overflow weir and lip, and its spigot;
the **concentrate launder** (z 28..32, falling west, nearly level); the **plate table** (x 2..14, z 28..45,
falling south 1 in 11), each on oak blocks.

**The water line** (`pipecopper` or `pipelead`, at every level; the owner's ruling). Every water line is Pipes
and Power Expanded's straight pipe as `UnifiedPipes` draws it in copper and lead, soldered, built by the same
module as the rosser's drip (`../Pipes/tools/solderedpipe.py`): ppex's tube 6 across round its bore, the same
texture mapping, a hub where lengths meet, a thin bead of solder at a joint. The rig has a part per metal, the
same elements in `game:block/metal/sheet-plain/copper4` and `lead4`, the joints and the end's plug in
`game:block/metal/ingot/leadsolder`, and only the supplied metal's is drawn (the viewer's **Water pipes**
select). The line: the inlet runs south from the water face (x 24, y 24) to a tee at z 13; west from the tee
to a closed hub over the head of the line, a nozzle under it (the sluice's head, then the jig's sieve); a
nozzle under the tee itself, over the table's water launder; east from the tee along the house to an elbow at
x 45.5, down to a second elbow under the line shaft (y 16.5), and south in two lengths with a joint between to
a plugged end over the vanner's belt, three nozzles under it, near the head roller. It is there from the
frame on, as the inlet and launders are: the outlets over the table's and the vanner's places wait for them
(the gameplay opens them). The sluice was lowered to pass under it.

**Level 1, the sluice.** A plank box 34 long (x 6..40), its channel 7 wide, falling east 1 in 12 from under
the feed box's spout and the head nozzle, seven oak riffles across its floor (1 × 1.2), its tail open over
launder N, on two trestles. Nothing moves.

**Level 2, the line shaft and the jig.** The **line shaft** continues the axle: the axle's oak cross at the
power face, a coupling, then a steel shaft (an octagon 1.6 across) the length of the house at y 24, z 24,
in three bearings each hung by an iron strap from a tie beam, collars either side of the middle one. The
**jig** stands in the sluice's place: a plank hutch 14 × 24 × 17 (x 3..17, z 3..27) on three sills, a
partition across it open below (the sieve compartment north, the plunger compartment south), the sieve
(mesh) on ledges, a weir notch in the east wall over the sieve with a spout east (the overflow, the jig's
tailings), and a spigot out of the south wall at the hutch's floor into the concentrate launder (its
concentrate). **The plunger** is driven straight from the line shaft: an **eccentric sheave** keyed on the
shaft runs in an iron **yoke** whose top and bottom bars bear on it (a horizontal slot); the yoke, its rod
and the plunger board are one part, guided by an iron eye between two bars across the compartment, and move
up and down exactly as the sheave's centre does: y = 0.6 sin θ, a stroke of 1.2.

**Level 3, the shaking table.** A deck 16 × 23.5 (x 21..37, z 3.5..27) on two joists, falling east 2.5°
across its width; four oak riffles along it (1 × 1, the east ones shorter, ending on a diagonal); the feed box
at its north-west corner under the jig's spout; the water launder along its upper (west) edge, fed by the
water line's nozzle under the tee; the lip past its south end over the concentrate launder. The joists slide on iron
plates on four legs. **The deck** is driven like the plunger, across: its sheave runs in an upright yoke (the
north and south bars on the sheave) and a drop bar from the yoke comes down beside the deck's west edge to an
arm under the deck, bolted to the west joist; the deck moves z = 0.5 sin θ, a stroke of 1.0 along its length.
Its tailings run off the low edge into the **tails launder** along it (fixed), falling south past the deck's
end to a spout (P3) over launder S or the vanner's feed box.

**Level 4, the vanner.** A Frue vanner in the south lane, its belt along x: two oak rails (x 18..50, y 7..10)
carry the **head roller** (x 46, the east end, under the table's spout) and the **foot roller** (x 22) in iron
bearings; the endless leather belt (7.2 wide, drawn still: a plain belt moving along its own length looks the
same) rises 1 in 30 to the head, with raised edges on its upper run. The frame rides on iron slides on posts.
**The drive** is the one belt in the machine: a pulley on the line shaft's west end belts a **countershaft**
along the vanner's south side 1:1. On the countershaft, two sheaves in upright yokes hung from the south rail
shake the frame across, z = 0.5 sin θ (the Frue's side shake). **The creep** (the owner's ruling: a ratchet,
so the belt only ever creeps the right way): in line beyond the countershaft's end, a short **worm shaft** in
two pedestals carries a 12-tooth **ratchet wheel** and a single-start **worm** over a 12-tooth wheel on the
head roller's south journal. A **pawl** on a bracket from the shaking frame's south rail sits under the
ratchet's lowest teeth: on one half of each shake it pushes the wheel round by the stroke (1.0, a tooth's
pitch), on the other it rides back over the teeth. So the worm shaft only ever turns forward, a push a turn of
the axle whichever way the axle turns, and the head roller a twelfth of that: the belt creeps to the head
0.1 voxel a turn, slow as a vanner's belt is. The rig draws the stepwise push as an even turn of the axle's
travel ψ (as the draw bench's and the gear cutter's rectified feeds are drawn). The wheel slides along its own
axis across the worm as the frame shakes, as a spur wheel on a worm can. Fixed: the pedestals of both shafts,
the slides, the **feed box** over the belt under P3 (open to the foot), hung from the water line's run beside
it, and the **concentrate box** under the head roller, on two bearers over launder S, its spout north into the
concentrate launder. Its wash water falls from the water line's three nozzles near the head.

**The amalgamation plates** (`plates`, from level 2). Three copper plates (3.7 down the bed, 9.6 wide) laid
in the plate table's bed and an iron riffle across it after them, the trap where the amalgam the pulp carries
off the plates settles. **The dressing** (`dressing`): the mercury rubbed into each plate, a thin coat in the
amalgam pan's mercury texture, and the mercury lying behind the trap. They sit after concentration, on the
concentrate's way out, as #734 has them; that is also why they come with the jig: from level 2 the
concentrate first runs out by itself. Nothing moves.

## Every moving part

θ is the axle's angle (radians); the line shaft turns with it. ψ is its travel either way (it only grows).
Slides in voxels.

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Line shaft: the axle's oak cross, coupling, shaft, collars (`lineshaft`) [lineshaft] | The vanilla axle | Every sheave; the vanner's pulley | `rotate` x 1 |
| The jig's eccentric sheave (`jigsheave`) [jig] | Keyed to the line shaft | The plunger's yoke | rides `lineshaft` |
| The table's eccentric sheave (`tablesheave`) [table] | Keyed to the line shaft | The deck's yoke | rides `lineshaft` |
| The vanner's drive pulley (`vannerpulley`) [vanner] | Keyed to the line shaft | The drive belt | rides `lineshaft` |
| Plunger: yoke, rod, boss, board (`jigplunger`) [jig] | Its sheave, in the yoke's horizontal slot | The water through the sieve | `slide` y 0.6 sin θ |
| Deck: yoke, drop bar, arm, boards, joists, riffles, feed box, water launder, lip (`tabledeck`) [table] | Its sheave, in the yoke's upright slot | Shakes the ore along the deck | `slide` z 0.5 sin θ |
| Countershaft: shaft, pulley, two sheaves, collars (`vannershaft`) [vanner] | The drive belt (1:1) | The frame's yokes | `rotate` x 1 |
| Worm shaft: shaft, ratchet wheel, worm, collars (`vannerworm`) [vanner] | The pawl on the frame, a push a stroke | The worm wheel | `rotate` x 0.0796 ψ |
| Shaking frame: rails, ties, bearings, yokes and their hangers, pawl, belt, edges (`vannerframe`) [vanner] | The countershaft's sheaves in its yokes | Carries the rollers and the belt; pushes the ratchet | `slide` z 0.5 sin θ |
| Head roller and its worm wheel (`vannerhead`) [vanner] | The worm | The belt | `rotate` z −0.0796 ψ / 12, rides `vannerframe` |
| Foot roller (`vannerfoot`) [vanner] | The belt | — | `rotate` z −0.0796 ψ / 12, rides `vannerframe` |
| The sluice (`sluice`) [sluice] | — | — | none |
| The line shaft's bearings and straps (`linehangers`) [lineshaft] | — | — | none |
| The jig's hutch, sieve, weir and spout, guide, spigot (`jig`) [jig] | — | — | none |
| The table's legs and slides, tails launder (`table`) [table] | — | — | none |
| The vanner's slides, pedestals, feed box and its hanger, concentrate box, drive belt (`vanner`) [vanner] | — | — | none |
| Amalgamation plates and their trap (`plates`) [plates] | — | — | none |
| The plates' mercury dressing (`dressing`) [dressing] | — | — | none |
| The water line in copper (`pipecopper`) [pipecopper] | — | — | none |
| The water line in lead (`pipelead`) [pipelead] | — | — | none |
| The wash house frame (`frame`) | Static | — | none |

Toothed wheels and what they mesh: the worm ↔ the worm wheel; the ratchet ↔ the pawl. The two pulleys drive
the one belt; the rollers
carry the vanner's belt. Each eccentric sheave runs in its yoke (a slotted crosshead, the Scotch yoke's way:
the bars on the sheave follow its centre exactly, the other pair clears its swing).

**Why the drives are as they are.** Every machine on the line shaft takes its stroke from an eccentric in a
yoke, one readable vocabulary with no linkage to approximate. The jig's plunger is under the shaft; the deck
runs across the house, so its stroke along its length is the shaft's sideways stroke; the vanner's belt runs
along the house, so its side shake is the countershaft's sideways stroke. The vanner alone has a countershaft,
because its worm must sit at its head roller, low down; a belt from the line shaft is the period way to reach
it.

**Either way the axle turns** (the owner's rule: every machine tolerates it). Every motion was checked for
direction:

| Motion | Turned backwards |
|---|---|
| The line shaft, the countershaft, the drive belt | Turn backwards; an open belt drives either way |
| The jig's plunger (an eccentric in a yoke) | The same stroke, a sine: a jig's pulse is symmetric |
| The table's deck (an eccentric in a yoke) | The same stroke, a sine; no quick return is drawn, so nothing is lost |
| The vanner's shake (two eccentrics in yokes) | The same shake, a sine |
| The vanner's creep (the ratchet, the worm, the rollers) | Forward still: the pawl pushes the ratchet on every shake, whichever way it comes, so the worm shaft follows the travel ψ |
| The sluice, the plates | No motion |

## The rig (`assets/seraphhorizons/config/concentrator-rig.json`)

The bucking sawmill's rig format (`../BuckingSawmill/README.md`, "Rig schema") with no new driver: rotations
and sinusoidal slides of θ, and rotations of ψ (`"input": "travel"`) for the ratchet's train. There is no
`work`: nothing moves with progress, only with the shaft.

**Keys.** `cells` (24, with lids over every column at the frame's top), `powerCell` and `powerFace`,
`waterCell` and `waterFace`, `feedCell` and `feedFace`, `feedSpout`, `waterSpout`, `concentrate` and
`concentrateSide`, `tailings` and `tailingsSide`, `returnWaterCell` and `returnWaterFace`, `levels` (each
level's number, tier, name and `requires` values), `plates` (`requires` `plates`, `dressing` `dressing`,
`fromLevel` 2), `pipes` (the water line's `requires` per metal), `drive` (`strokesPerTurn` per machine,
`beltPerTurn`, the belt's creep in blocks a turn of the axle either way, and `ratchet`: its teeth and its turns
a turn) and `parts`. `requires` values: `sluice`, `lineshaft`, `jig`, `table`, `vanner`, `plates`,
`dressing`, `pipecopper`, `pipelead`, or null (the frame). The rig reads θ and ψ (the ratchet's train).

**Collision per level.** A cell's `boxes` are the frame's, there at every level; its `fitted` boxes, keyed by
a `requires` value, are there only while that part is fitted (`docs/recipe-browser/models.md`, "Collision by
what is fitted"). So at level 1 a player meets the sluice and not the table's legs, and the sluice's boxes go
when it is taken out. All are built, as the other machines' are, from the shipped shape posed at rest by the
shipped rig (each `requires` group on its own), so every reader rebuilds them the same way. The game's rig
loader does not read `fitted` yet.

**In the viewer** (`site/models.json`): two selects, as the eidolon gantry has. **Level**: the frame alone,
then levels 1 to 4 (it opens at level 4). **Amalgamation plates**: none, bare or dressed (it opens dressed);
it waits for the jig (`needs`), so the plates are only offered from level 2. **Water pipes**: copper or
lead (a choice, as the rosser's drip pipes). θ's slider and Play turn the axle; "turn backwards" shows the
ratchet keeping the creep forward. The collision overlay follows what is fitted. `levels`, `plates`, `pipes`
and `drive` are listed as not drawn.

## Regenerating

```sh
python3 mods-src/seraphhorizons/Concentrator/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Concentrator/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Concentrator/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

It writes `assets/seraphhorizons/shapes/block/concentrator.json` (everything, 631 elements: 81 for the
frame, 64 for the water line in each metal, and with it in one metal 145 shown with nothing fitted, 162 at
level 1, 216 at level 2, 265 at level 3, 542 at level 4, 550 with the dressed plates),
`concentrator_frame.json` (the frame part only), the rig and `tests/Concentrator/rig-reference.json` (every
part's matrix at 14 poses of θ and ψ). A full run takes about two minutes. Output is deterministic.

## Validation (`tools/validate_concentrator.py`)

Every run, and the script exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part. **Levels:** every part the frame's, a level's or the plates'; a level only adds, but for the sluice;
  levels 1 and 2 the water tier's, 3 and 4 the mill tier's; the plates only with the jig, dressed only when
  fitted; a part only rides a part fitted with it.
- **Textures by role:** oak timber (and the line shaft's oak cross), oak planks for boards, iron castings and
  straps, steel shafts, sheaves, pulleys, rods, the ratchet, the worm and its wheel, the mesh for the sieve,
  leather belts, the water line in its metal's pipe texture with solder joints, copper plates and their mercury
  dressing. **The pipes' metals:** the lead part is the copper part's elements, in lead.
- **Thickness:** nothing thinner than 0.6 voxel across but the thin-by-nature pieces (sieve, belt, plates,
  coats, lips, teeth).
- **Nothing floats:** the frame alone and the frame with each state's parts at rest are one piece from the
  ground.
- **Containment:** nothing leaves the 4 × 2 × 3 box at any sampled angle.
- **Anchors:** the line shaft's oak cross meets the power face at the cell's centre in the axle's profile;
  the water line's inlet (each metal's) ends on the water face's centre in ppex's 6 × 6 tube; the feed box takes a
  chute through the feed cell's face; the concentrate leaves by the west face and the tailings and the return
  water by the east, the return water through its own cell; the power, water and feed cells differ.
- **Flows:** material dropped at each lip lands first in its receiver, at every level it holds in (the feed
  and the water on the sluice's head or the jig's sieve; the sluice's tail, the jig's weir, the table's tails
  and the vanner's foot each on the next machine or a launder; the deck's edge and lip checked through its
  shake; the vanner's feed box and distributor on the belt; every concentrate into the concentrate launder,
  onto the bare bed, the bare plates or the dressed ones, past the trap; the launders into the tail box; the
  outlets).
- **Yokes:** at every step of a turn each sheave's centre is on its yoke's slot line (exactly), the bars a
  hundredth off the sheave's corners, the sheave inside the slot's length.
- **Drive:** the drive belt's rims at one speed and its runs on their tangents; the pawl under the ratchet's
  teeth through the shake, its stroke a tooth's pitch; the worm on its wheel's pitch circle in its plane, the
  wheel a tooth a worm turn; the worm shaft turning forward and the belt's upper run moving to the head with the
  axle turned either way.
- **Supports:** the line shaft in its three bearings, the countershaft in its three, the worm shaft in two,
  each roller in two; the
  plunger rod through its eye; the deck's joists on their slides and the vanner's rails on theirs through a
  turn.
- **Clearances:** at 24 steps of a turn at every level (with the plates bare and dressed), no two parts touch
  but the contacts listed in `ALLOWED`; and (full runs) the moving parts every 3.75° of a turn at each level.
- **No z-fighting** (full runs): no coplanar overlapping faces at any level at four angles, after
  `fix_coplanar` (which sees only the parts that show together).
- **Files:** every texture declared; lids over every column; the anchored cells and the controller's not
  hollow; fitted boxes keyed only by the rig's `requires` values; the shipped model is the checked one moved.

`tools/tests/test_concentrator_model.py` holds the written files to these without the generator's state (the
reference poses, the cells and their per-level collision, the levels, the yokes, the creep either way, the
pipes in both metals, the anchors, the site's states against the rig's levels, and this README's parts table);
`site/test/concentrator.test.ts` replays the reference poses in the viewer's maths and checks the creep
reversed, the selects, the pipes' choice and the fitted collision.

## Element names

The rig's interface (first-match globs, in the rig's order): `lineshaft_*`, `jigsheave_*`, `tablesheave_*`,
`vannerpulley_*`, `jigplunger_*`, `tabledeck_*`, `vannershaft_*`, `vannerworm_*`, `vannerframe_*`,
`vannerhead_*`, `vannerfoot_*`; `sl_*` (the sluice), `ls_*` (the line shaft's bearings), `jg_*`, `tb_*`,
`vn_*`; `pl_*` (the plates and the trap) and `pd_*` (their dressing); `pipecopper_*` and `pipelead_*` (the
water line); and `fr_*` for the frame. Hand edits are lost when the
script runs again: port them into `make_shape.py`.

## Open for the owner

The old model's sixteen open items, settled with these defaults:

1. **Level 1's powered rake** is gone: level 1 is a plain riffled sluice the player feeds, with no power.
2. **The table's head motion** is no longer a closed box: an eccentric in an open yoke and a drop bar to the
   deck's edge, all visible. The motion is a plain sine; a period head motion (Rittinger's cam and spring,
   Wilfley's toggles) gives a quick return, which is not drawn: the game supplies the separation.
3. **Motions toned down:** the plunger's stroke 1.2 voxels (was 3), the deck's 1.0 (was 1.56), the vanner's
   shake ±0.5 (was ±1), the creep a twelfth of a turn a turn (still far faster than life, so it reads).
4. **Size:** 4 × 2 × 3 = 24 cells (was 90). The cascade sets the heights: the feed box's spout at 25, the jig's
   weir at 19.6, the deck at 16.8, the table's spout at 14.3, the vanner's belt at about 12, the concentrate
   launder and the plate table at the floor.
5. **Collision per level:** the cells' `fitted` boxes (a new, generic rig key; the viewer draws them). The
   game's rig loader must learn `fitted` before #734 can use it.
6. **No roof:** still an open frame; boards or shingles would hide the works.
7. **The jig's sieve** uses the game's metal mesh texture, which has holes: the renderer must draw it with
   alpha cut out, or it draws solid.
8. **No material or water is drawn.**
9. **The vanner's tails** fall off the belt's foot straight into launder S.
10. **Outlets:** the concentrate leaves at the floor of the west face, the tailings and the return water at
    the floor of the east face; the feed comes in at the top of the north face; a chute to the next machine
    works downhill.
11. **The jig** has one sieve and one plunger (not two), and one spigot at its floor; no hoppered bottom.
12. **The water** (the owner's ruling): every water line is ppex's straight pipe, soldered, in copper or lead,
    as the rosser's drip; one line from the one inlet, at every level, with nozzles over the head of the line,
    the table's water launder and the vanner's belt. No cocks: the gameplay opens the outlets.
13. **The vanner** rides on slides, not the Frue's flexible uprights; its feed box and the water line's
    nozzles are fixed over the shaking belt; its belt does not dip through a tank (the concentrate falls into
    a box under the head roller): simplifications.
14. **The plates** are on the concentrate's way out, after concentration, as #734 says (mills ran their whole
    pulp over plates before concentration, on the battery apron). They are bigger and steeper than before:
    three plates 3.7 × 9.6 on a bed falling 1 in 11.
15. **The dressing** stays an overlay part (`dressing`) over the plates.
16. **Build stages and costs** are still not designed: each level is its `requires` values, split where a
    stage could be.

New questions:

17. **The sluice is retired at level 2** (above). Keep that, or keep the sluice as a tail sluice somewhere
    else?
18. **A reversed axle** (the owner's ruling): the vanner's creep goes through a ratchet pushed by the shake, so
    it only ever goes forward, and every other motion is a symmetric stroke ("Either way the axle turns").
    The ratchet's push is drawn as an even turn; a game renderer could step it a tooth a stroke instead.
19. **The vanner treats the table's tailings**; the period vanner usually took the slimes. Equivalent for the
    game's arithmetic; say if a separate middlings stream from the table is wanted (it would leave at a plain
    output anchor too, under the returns ruling).
20. **One belt** (line shaft to the vanner's countershaft) is the only belt; everything else is direct. The
    worm shaft is a separate shaft in line beyond the countershaft's end, turned only by its ratchet.
21. **The concentrate launder is nearly level** (1 in 77): the vanner's concentrate box is low, and the
    launder must pass over the plates. Real launders for concentrate fell 1 in 30 or more.
22. **The returns** follow the owner's ruling: level 4's recirculation is a gameplay rule; the return water
    leaves at `returnWaterCell`, and nothing inside carries it back.
