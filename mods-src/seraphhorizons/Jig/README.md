# Jig

Part of the Seraph Horizons mod (`../README.md`): the second concentrator of the ore line (epic #684), a
**Harz plunger jig** of the later nineteenth century [Richards 1903, I, Harz jigs, figs. 306–307]. A plank
box of water, the **hutch**, is divided across by a **partition** open below it: the **sieve compartment**
at the front, the **plunger compartment** behind. An **eccentric** on the jig's own shaft works the
**plunger** up and down; each stroke pulses the water up through the ore bed on the sieve, so the heavy
grains sink through it into the hutch and leave by the **spout**, and the light ones are carried along the
sieve to its tail end and over the **weir**. Crushed or ground ore comes in by chute; concentrate leaves by one chute, tailings by another
(#919: r = 0.60, 8 rock an hour, on the sluice's tailings, before the table).

This folder holds the model's generator (`tools/`). The gameplay is #919 and is not built yet; the model
is #920. It is one machine, built in stages (below).

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/` and `site/`, which are the repository's. The generic half
of the generator is `Machines/tools/machinegen/`, unchanged by this machine: no new driver.

## The machine

**Footprint.** 4 cells, 1 wide (x), 2 high, 2 deep (z, away from the player): the controller `[0,0,0]`
is the front bottom cell, the hutch's spout end, nearest the player who placed it; the jig runs south
from it. The hutch is 13 × 26 × 17.5 voxels (x 2.5..15.5, z 3..29, its floor's top at 5, its rim at 22.5;
its west wall's head rises to 28 round the tailings chute's end) on three oak sills; the drive stands on it
at the back, the feed at the front.

**Services.** Each on a face of its own, centred in it, so the vanilla block placed beyond meets it with
no gap or step (anchors in `assets/seraphhorizons/config/jig-rig.json`; provisional until #711 settles
the shared ones):

| Anchor | Cell, face | What meets it |
|---|---|---|
| `powerCell`, `powerFace` | `[0,1,1]`, west | The vanilla axle along x at the face's centre (y 24, z 24 voxels), on the shaft's oak end: the axle's own cross profile, 4 × 2 and 2 × 4 |
| `infeedCell`, `infeedFace` | `[0,1,0]`, up | A vanilla chute above (`chute-straight-ud`) pushes down into the infeed: vanilla's straight chute section, upright, from the top face down onto the feed trough |
| `tailingsCell`, `tailingsFace` | `[0,1,0]`, west | The tailings chute: vanilla's section along x from the port in the tail end's head (the weir's lip in its mouth) out of the face, to a `chute-straight-we` beyond |
| `concentrateCell`, `concentrateFace` | `[0,0,0]`, north | The concentrate chute: vanilla's section along z from the front wall (the spout's lip in its mouth) out of the face, to a `chute-straight-ns` beyond |
| `waterCell`, `waterFace` | `[0,0,1]`, south | A ppex pipe meets the water inlet end to end on the face's centre (x 8, y 8) |

Also `sieve.pos` (the middle of the sieve's top: sounds and splashes), `levels` (the weir's sill, the
sieve's top and the rim, drawn as level lines in the viewer) and three keys the viewer lists as not
drawn: `pipes`, `contents`, `drive`. No return water and no return-water anchor (#919, #694): the water is
used up and leaves with the outputs.

**Why the faces are these.** The shaft must be at the power face's centre, y 24, so the drive stands over
the back half and the plunger compartment is there; the sieve compartment is at the front. A chute section
is centred in its cell's face too, so the weir's chute must be in the upper row (its bore's floor at
y 21: the weir's sill stands just over it, at 21.25), on a side face of the front top cell; it shares the
west side with the axle, both needing the hutch's west wall set in 2.5 (the oak end turns within 2.24 of
the axle). The infeed comes down the front top cell's top face; the concentrate leaves at the floor, out
of the front; the water comes in low at the back, under the plunger (the upper row there is the drive's).
Every block beyond a face is a different one.

**The flows.** The feed falls from the infeed chute onto the **feed trough**, closed at its west end and
falling east 1 in 10, which drops it at the sieve's east end, the **head end**; the water carries it along
the sieve (9 voxels, along the partition, as a Harz jig's flow runs) to the **tail end**, the west wall.
Two ways out, one each side of the sieve:

- **Tailings** (the light grains, on top of the bed): over the **weir** at the tail end. The bed's top
  stands 1.65 under the weir's sill (the sieve is high in the hutch, at 17.2..17.6, the bed two voxels on
  it), so the light grains lifted on each pulse rise into the overflow: through a **port** in the west
  wall (z 5.2..10.8, from the sill at 21.25 up to 24) over the weir's **iron lip**, which reaches from the
  wall's inner face half a voxel into the tailings chute's mouth, and down onto the chute's floor (y 21).
  The west wall rises over the rim as a plank **head** round the chute's end (two cheeks and a lintel over
  the port, to y 28, the chute section's top), so the chute leaves a wall, not the rim, and the port and
  lip show at the end of its bore.
- **Concentrate** (the heavy grains): down through the bed and the sieve into the hutch, whose bottom under
  the sieve compartment **slopes** (boards on an oak cleat at the partition, 1 in 6) down to the front
  wall; there an iron **spout** lines a port through the front wall's foot (x 5.2..10.8, y 5..8.5), its
  floor flush with the slope's foot (5.65), its lip 0.6 into the concentrate chute's mouth, 0.65 over the
  chute's floor, so it pours.

**Why the tailings leave by the west face.** It is where the flow ends: the feed lands at the east end and
the overflow is the far end of the sieve from it, the tail end. A chute section is centred in its face, so
the tailings chute must be in the upper row (its bore's floor at 21, the weir's sill just over it); the
front top cell's north face would put the tail end across the flow, at the front wall, with the feed
landing beside it. The water comes in under the plunger and leaves with the products.

## Build stages

Settled 2026-10-15. The hutch is placed as the machine; then each stage is fitted in order, and the jig runs
only when the last is in. The materials are the stages' iron and timber measured from the model at the
settled rate (128 vox of iron or steel an ingot, a rod an ingot, nails and strips 4 an ingot; boards in planks at 128 vox, timbers in support beams at 256), the chute and pipe sections counted.

| # | Stage | `requires` | Sits on | Materials |
|---|---|---|---|---|
| F | The hutch (the frame) | none (placed) | The ground | 3 iron nails and strips, 20 plank, 2 support beam |
| 1 | The sieve | `sieve` | The ledges round the sieve compartment (frame) | 1 iron chain (the sieve, 119 vox²) |
| 2 | The drive and plunger | `drive`, `plunger` | The standards on the side walls' rims; the sheave keyed on the shaft, the board in the plunger compartment | 1 iron rod, 1 steel rod, 2 plank |
| 3 | Services | `infeed`, `outlets`, `pipecopper` or `pipelead` | The infeed on its bearers over the sieve; the outlets on their bracket and block on the hutch; the inlet through the back wall | 3 chute section, 1 pipe section (copper or lead), 1 iron nails and strips, 1 plank, 1 support beam |

Every chute section and the water inlet are in services; the frame holds none. The water inlet is a choice
of metal, copper or lead (one part each, the same elements). The water and the ore bed (`water`, `bed`) are
what the jig holds while it works, in no stage; nothing in it wears out as a part.

## How it works

Positions are in voxels in the generator's build frame (x west to east, y up, z north to south, from the
machine box's north-west bottom corner), which is the shipped frame times 16: the controller's corner is
the build frame's.

**The hutch** (the frame, `hutch_*`). Three oak sills across (4 high, under the front wall, the partition
and the back wall), a plank floor on them, four plank walls 1.5 thick to the rim. The west wall, the tail
end, is notched over the sieve compartment for the **weir**: its sill at 21.25 under an iron lip that
reaches into the tailings chute; over the rim it rises as the **head** (cheeks and a lintel, to 28) round
the chute's end, leaving the **port** over the lip. The **partition** (z 16.5..17.5) runs from the rim
down to y 10, so the water passes under it. Oak **ledges** round the sieve compartment carry the sieve at
y 17.2..17.6. Under the sieve compartment the hutch's **bottom slopes** from 7.65 at the partition (on an
oak cleat) to 5.65 at the front wall, where the iron **spout** (a floor, two sides and a head, 0.5
thick round a 4.6 × 2.35 bore) lines a port through the wall's foot into the concentrate chute's mouth.

**The corner irons** (in the frame, `hutch_strap_*`), as a period plank tank was bound. Each of the
hutch's four vertical corners has an angle-iron **strap** (0.3 thick): a leg 0.9 wide flat on each of its
two outer faces, the north or south face's leg lapping the other's edge so the two meet in an L round the
corner, from the sills' top (y 4) to the rim; at the tail end's corner (north-west), where the west wall
rises as the head, it runs up the head to its top (28). On the three corners at the rim a flat **corner
iron** lies on the rim, an arm 2.4 along each wall's top, lapping the strap's head. Every leg has three nail
heads (the head corner's four), every corner iron's arm one. They keep clear of the services: the
north-west strap's west leg stops 0.1 short of the tailings chute's north wall, and nothing else comes
within 0.2 of them (the outlets, the infeed, the water inlet, the axle's flange and the plunger's path).

**The sieve** (`sieve`): an iron mesh (`game:block/metal/mesh1`) across the sieve compartment, on its
ledges.

**The drive** (`drive`). The **shaft** turns with the axle: the axle's oak cross at the power face, an
iron flange it is socketed in, then a steel shaft (an octagon 1.1 across) in two iron **bushes**, one in
each **standard**, an oak post on each side wall's rim over the plunger compartment, strapped down to its
wall. The flange and a collar locate it on either face of the west bush, two collars on the east one. A
**guide beam** runs between the standards' heads (y 28.5..30), an iron guide on it.

**The plunger** (`plunger`). The **eccentric sheave**, keyed on the shaft (a 12-sided steel disc, its
centre 0.5 off the shaft's), runs in an iron **yoke**, a slotted crosshead whose top and bottom bars bear
on it (the Scotch yoke's way): the yoke follows the sheave's centre up and down exactly, y = 0.5 sin θ, a
stroke of 1.0. From the yoke's bottom bar the **plunger rod** goes down to an iron boss on the oak
**plunger board**, which nearly fills the plunger compartment (0.4 clear all round) and works about the
sieve's level, below the water; from its top bar a **tail rod** rises through the guide on the beam, which
keeps the yoke upright. Nothing else guides it: the board in its compartment and the tail rod in its guide.

**The infeed** (`infeed`). Vanilla's straight chute section, upright, from the top face down to y 28.5
(3.5 long), its mouth open over the **feed trough** (planks, its inside the chute's bore wide) with 2.45
of clear fall to the trough's boards, so the feed visibly drops from the chute into the trough and runs
down it onto the bed; the trough lies on two oak **bearers** that span the sieve compartment from the
front wall's rim to the partition's, and four iron straps rise from the bearers up the chute's north and
south walls to y 31.5 and carry it over the gap.

**The outlets** (`outlets`). Vanilla's straight chute section from the weir out of the west face (2.5
long, on an iron bracket from the hutch's west wall), and from the spout out of the north face (3 long, on
an oak block). Each is the game's section exactly: its four walls (an 8 × 8 tube 1 thick, two full-width
walls and two between them, centred in the face), in the texture a placed chute shows in the pack,
`game:block/metal/sheet/copper1`. The generator keeps the game's profile as a constant (`VANILLA_CHUTE`),
turns it for each run (`chute_walls`: along x as the game's `we` shape, along z as its `ns`, upright as its
`ud`), and holds it to the game's `shapes/block/metal/chute/straight.json` when the game is installed.

**The water inlet** (`pipecopper` or `pipelead`). Pipes and Power Expanded's straight pipe as
`UnifiedPipes` draws it in copper and lead, soldered (`../Pipes/tools/solderedpipe.py`, as the rosser's
drip): one straight length from the south face through the back wall, its end open in the plunger
compartment under the plunger. One part per metal, the same elements; only the supplied metal's is drawn.
No joint at the face: the pipe beyond brings its own.

**The contents** (`water`, `bed`): not parts of the build but what the jig holds while it works, drawn as
`requires` values the gameplay sets (the amalgam pan's contents are the precedent). The **water**: its
surface in each compartment, a sheet 0.1 thick at the weir's sill, in `game:block/liquid/waterportion` in
the transparent pass. The **ore bed**: two voxels of crushed rock on the sieve (`game:block/stone/gravel/granite`,
provisional). They answer the plunger as the water would: down it goes and pushes the water under the
partition and up through the sieve, so the sieve compartment's surface rises by the board's displacement
over its area (−0.35 sin θ), the plunger compartment's falls with the board (0.42 sin θ, the board's area
over the compartment's), and the bed lifts a little on each pulse and settles (−0.15 sin θ).

## Every moving part

θ is the axle's angle (radians). Slides in voxels.

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Shaft: the axle's oak cross, flange, shaft, collars (`shaft`) [drive] | The vanilla axle | The sheave | `rotate` x 1 about (y 24, z 24) |
| Eccentric sheave (`sheave`) [plunger] | Keyed to the shaft | The yoke | rides `shaft` |
| Plunger: yoke, tail rod, plunger rod, boss, board (`plunger`) [plunger] | The sheave, in the yoke's horizontal slot | The water | `slide` y 0.5 sin θ |
| The sieve compartment's water (`watersieve`) [water] | The plunger, under the partition | The bed | `slide` y −0.35 sin θ |
| The plunger compartment's water (`waterplunger`) [water] | The plunger board | — | `slide` y 0.42 sin θ |
| The ore bed (`bed`) [bed] | The water pulsing up through it | — | `slide` y −0.15 sin θ |
| Standards, bushes, straps, guide beam and guide (`stand`) [drive] | — | — | none |
| The sieve (`sieve`) [sieve] | — | — | none |
| Infeed chute, feed trough, bearers, straps (`infeed`) [infeed] | — | — | none |
| Tailings and concentrate chutes, their bracket and block (`outlets`) [outlets] | — | — | none |
| The water inlet in copper (`pipecopper`) [pipecopper] | — | — | none |
| The water inlet in lead (`pipelead`) [pipelead] | — | — | none |
| The hutch, with its corner straps and rim irons (`frame`) | — | — | none |

No toothed wheel: the eccentric runs in its yoke (its bars on the sheave follow its centre exactly, its end
bars clear its swing by 0.3).

**Either way the axle turns** (#711: the axle may turn either way). An eccentric is symmetric: turned
backwards, the plunger makes the same stroke, a sine of θ, and so do the water and the bed. The jig needs
no rectifier (#920).

## The rig (`assets/seraphhorizons/config/jig-rig.json`)

The bucking sawmill's rig format (`../BuckingSawmill/README.md`, "Rig schema") with no new driver: a
rotation and sinusoidal slides of θ. There is no `work`: nothing moves with progress, only with the axle.

**Keys.** `cells` (4, each with its boxes, a `lid` over each column's top cell at the top), the five
service anchors above (`powerCell`/`powerFace`, `infeedCell`/`infeedFace`, `tailingsCell`/`tailingsFace`,
`concentrateCell`/`concentrateFace`, `waterCell`/`waterFace`), `sieve` (a point), `buildStages` (each
stage's number, name and the `requires` it adds, in order), `levels` (`weirY`,
`sieveY`, `rimY`, blocks), `pipes` (the inlet's `requires` per metal), `contents` (the `requires` values
for the water and the bed), `drive` (`strokesPerTurn` 1 and the `stroke` in blocks, either way the axle
turns) and `parts`. `requires` values: `sieve`, `drive`, `plunger`, `infeed`, `outlets`, `pipecopper`,
`pipelead`, `water`, `bed`, or null (the hutch).

**Cells.** Their boxes are the finished machine's at rest (every fitted part, the inlet in copper, no
contents), rebuilt from the shipped, rounded shape posed by the shipped rig, as the other machines' are
(`shipped_cells`; `tools/tests/test_jig_model.py` rebuilds them and compares).

**In the viewer** (`site/models.json`, `jig`): θ's slider and Play turn the axle (Play at 2.1 s a turn,
the reference speed 0.6's 0.48 turns a second); "turn backwards" shows the same stroke. A **Build stage**
select: the hutch, 1 the sieve, 2 the drive and plunger, 3 services with the inlet in copper or in lead
(the default copper). A **Contents** select, once the jig is built: dry and empty, water with no ore, or
working (water and the bed; the default). The viewer draws the water opaque (it has no
transparency), so untick Water to see the bed.

## Regenerating

```sh
python3 mods-src/seraphhorizons/Jig/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Jig/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Jig/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

It writes `assets/seraphhorizons/shapes/block/jig.json` (the whole machine, 167 elements),
`assets/seraphhorizons/shapes/block/jig_frame.json` (the hutch, 76), `assets/seraphhorizons/config/jig-rig.json`
and `tests/Jig/rig-reference.json` (every part's matrix at ten values of θ). A full run takes about 2 s and
is deterministic (two runs into two folders are byte-identical, logs included).

## Validation (`tools/validate_jig.py`)

Every run checks its output and exits non-zero if a check fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part. **Requires:** the vocabulary above; a part riding another fitted with it in every state;
  the lead inlet the copper one's elements.
- **Textures** by role (`ROLES`): oak timber, plank boards, iron castings and straps, steel shaft, sheave
  and rods, the mesh, vanilla's chute copper, the pipe's metal, the contents'; only the water's surfaces in
  the transparent pass. **Thickness:** nothing thinner than half a voxel but the mesh, the lip, the
  water's surfaces and the straps (the corner irons and their nail heads among them).
- **Build stages** (`check_build_stages`): the rig's `buildStages` are the settled ones (the sieve; the
  drive and plunger; services last, the chutes and the inlet), every fitted part in one stage, the frame
  and the contents in none, and each stage's state fits the stages up to it.
- **Nothing floats**, at rest: the hutch alone, after each build stage, the finished machine dry, with
  water, working, and each service alone on the machine before services.
- **Containment:** nothing leaves the four cells over a turn.
- **Anchors:** the shaft's oak end from the power face at its centre in the axle's profile; each chute
  vanilla's section (its walls the game's, for its run), centred in its face, flush with it, in the
  chute's texture, and `VANILLA_CHUTE` the game's own shape when the game is here; the inlet ppex's 6 × 6
  tube on the water face's centre, open in the plunger compartment under the plunger; every service on a
  face of its own, the blocks beyond them all outside the machine and apart.
- **Levels:** the weir's sill over the tailings chute's bore floor and under the rim; the bed and the
  plunger board under the water through the stroke; the yoke at least 0.3 over the plunger compartment's
  surface through a turn (they move together); the partition open over the slope and under the plunger's
  stroke; the spout's floor flush with the slope's foot, over the concentrate chute's bore floor, the
  spout inside the chute's bore and its lip in the chute's mouth; the contents' amplitudes and directions.
- **Infeed drop** (`check_infeed_drop`): under the infeed chute's section, the first thing below its mouth
  is the feed trough, at least `INFEED_DROP` (2.0) lower: open fall, nothing crowding the mouth.
- **Flows** (ray drops, the water's surface not counted): the infeed onto the feed trough at its corners
  and middle; the trough's lip onto the sieve (and onto the bed, through a turn), clear of the bearers;
  the weir's lip into the tailings chute's floor across its width; through the sieve onto the hutch's
  slope; the spout's lip into the concentrate chute's floor across its bore; the feed landing at least 8
  from the weir.
- **Ways out** (`check_paths`): each output has an open way from where its product collects to its
  chute's face. A grain (a probe 0.6 across) walked along it in steps of 0.1, at four poses of a turn,
  touches nothing but the water and the bed: the tailings from the bed's top at the tail end up over the
  weir's lip, through the port, down onto the tailings chute's floor and along its bore to the west face;
  the concentrate from under the sieve down onto the slope, down it into the spout, off its lip onto the
  concentrate chute's floor and along its bore to the north face. Each way ends inside its chute's bore on
  its anchor's face, the tailings' stays over the sieve and the concentrate's under it, so the two leave
  by separate ways.
- **The yoke:** through a turn the sheave's centre on the slot's line, the bars 0.01 off its corners, the
  sheave inside the slot's length; the plunger at 0.5 sin θ and the same stroke backwards; the sheave
  enclosing the shaft off its centre.
- **Supports:** the shaft in both bushes (`supports`), located on both faces of each by the flange or a
  collar; the tail rod through the beam and in its guide, and the plunger board in its compartment, through
  a turn; the standards on the side walls' rims.
- **Corner irons** (`check_corner_straps`): every vertical corner of the hutch, found from its walls, has
  an angle-iron strap, a leg flat on each of its two outer faces, the legs meeting along the corner and
  wrapping it, from the sills' top to the corner's top (the head's at the tail end), each leg with two
  nails or more; each corner at the rim has a corner iron on it lapping the strap's head, nailed; and the
  irons stand 0.1 or more clear of the outlets, the infeed, the water inlet, and the shaft, sheave and
  plunger through a turn.
- **Clearances:** over a turn every 15°, working and dry, and backwards, no two parts touch but the
  intended contacts listed in `ALLOWED` (the shaft in its bushes and standards, the sheave on the shaft and
  in the yoke, the tail rod in its guide and beam, the inlet through the back wall, the water against the
  walls, the straps and the plunger rod, the bed on the sieve). **Swept paths** (full runs): every moving
  part at every 3.75° of a turn.
- **No z-fighting** (full runs): no coplanar overlapping faces at ten poses, working and dry; the fix
  removes the faces pressed against their own part (64) and insets the rest.
- **Files:** every texture declared; four cells, each with boxes, a lid on each column; the shipped model
  is the checked one (no move: the build frame's corner is the controller's).

## Element names

First-match globs, in the rig's order: `shaft_*` (`entrya`, `entryb`, `flange_*`, `rod_*`, `collar1..3_*`),
`sheave_*`, `plunger_*` (`yoke_top`, `yoke_bottom`, `yoke_n`, `yoke_s`, `tail`, `board`, `boss`, `rod`),
`watersieve_surface`, `waterplunger_surface`, `bed_ore`, `stand_*` (`post_w`, `post_e`, `bush_w`, `bush_e`,
`strap_w1`.., `beam`, `guide`), `sieve_mesh`, `infeed_*` (`chute_n/s/e/w`, `trough_floor`, `trough_n`,
`trough_s`, `trough_end`, `bearer1`, `bearer2`, `strap1n`..), `outlet_*` (`tailchute_n/s/bottom/top`,
`tail_arm`, `tail_leg`, `concchute_w/e/bottom/top`, `conc_block`), `pipecopper_*` and `pipelead_*`
(`inlet_*`), and `hutch_*` for the frame (`sill1..3`, `floor1..2`, `wall_n`, `wall_s`, `wall_e1..2`,
`wall_nw`, `wall_ne`, `wall_w1..2`, `wall_wn`, `wall_ws`, `weir_lip`, `head_n`, `head_s`, `head_lintel`, `partition`,
`ledge_w/e/n/s`, `hopper`, `hopper_cleat`, `spigot_bottom/top/w/e`, and the corner irons
`strap_<nw|ne|sw|se>_x` and `_z` (the legs on the north or south face and on the west or east face),
`_xnail1..`, `_znail1..`, `_rimx`, `_rimz`, `_rimxnail`, `_rimznail`). Hand edits are
lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Salvaged from the v2 branch

From `origin/735-concentrator-model-v2` (the wash house; `Concentrator/tools/`, copied, not merged):
`build_jig` (the hutch, partition, sieve and ledges, weir, spigot), `build_jig_moving` (the sheave, yoke,
rod, boss and board on `slide` y E sin θ), the helpers `skin`, `box`, `beam`, `disc`, `sloped`,
`slope_len`, `floor_y`, `under_slope` and `yoke`, the vanilla chute profile and its check against the
game's file, the soldered pipe's `pipe_boxes`/`build_pipes`/`pipe_copies`, and the validator's structure.
Changed, as #920 asks: its own shaft from its own axle in two bushes (it hung on the wash house's line
shaft, in three bearings from its tie beams); the infeed chute onto the sieve (the house's hopper fed it);
the weir and the spigot each ending in a chute section (its weir spouted onto the table, its spigot into a
launder); its own water inlet (the house's line had one nozzle over the sieve). New: the standards, the
guide beam and the tail rod, the feed trough, the contents, the levels and flows for this layout, and the
tests.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files, not yet in a client.

1. **The hutch's bottom slopes one way only.** It falls from the partition to the front wall, the full
   inside width; a period hutch narrows to its spigot every way, so the corners either side of the spout
   (x 4..5.2 and 10.8..14) are dead. Only a section shows it.
2. **The water is two surfaces, not a volume**, and the water over the weir is not drawn falling into
   the chute; nothing shows the feed in the trough or the concentrate in the chutes.
3. **The bed's lift is a sine of θ,** like the stroke: a real bed opens on the pulsion stroke and packs
   on the suction stroke, not symmetrically.
4. **The mesh texture has alpha.** In the game the sieve may need a cut-out render pass to show its mesh
   (the v2 branch's open item); in the viewer it draws solid.
5. **The water's render pass** (transparent) is the shape's; the renderer drawing the moving water must
   draw it in that pass too.

## Open for the owner

1. **The hutch's size.** 13 × 26 × 17.5 (#920 says about 14 × 24 × 17): its west wall set in 2.5 for the
   axle's oak end and the tailings chute, its rim at 22.5 so the weir spills into an upper-row chute.
2. **The faces** (table above): power and tailings both on the west side, the infeed on top at the front,
   the concentrate out of the controller's front face, the water in at the back, low. Another layout (the
   sieve compartment at the back, say) moves them all.
3. **The feed trough.** A chute is centred in its face, so the infeed lands in the middle of the sieve,
   beside the weir; the trough carries the ore to the sieve's east end so it crosses the sieve to the weir.
   A Harz jig was fed at its head end by a launder, which this stands for.
4. **Water under the plunger** ("hutch water"). The inlet must be centred in a face; the upper row at the
   back is the drive's, so the pipe comes in low, under the plunger, rather than over it.
5. **The yoke's guide.** The tail rod in a guide on a beam over the shaft, not a rod guide at the rim:
   the water stands within a voxel of the yoke's lowest point (the weir must spill into an upper-row chute,
   and the shaft is at the face's centre), which leaves no room for a guide between them.
6. **The stroke.** 1 voxel (0.5 throw) at one stroke a turn of the axle: 0.48 a second at the reference
   speed 0.6, where a Harz jig made 100 to 200 strokes a minute of ½ to 2 in. The game counts turns, so the
   drawn pace is the axle's.
7. **The contents.** Water (two surfaces) and the ore bed as `requires` values; their amplitudes from the
   compartments' and the board's areas. Whether gameplay should draw them, and the bed's texture
   (granite gravel now; the ore's host rock, as #694's tailings are, would need the renderer to set it).
8. **Build stages** settled 2026-10-15 (above). The screen's item (`game:metalchain-iron`) is the
   settled rate's, not a mesh of its own.
9. **Anchors provisional pending #711**: the `<name>Cell`/`<name>Face` pairs for chutes and water.
10. **Box ironwork** (ruled 2026-10-14: corner straps). Angle-iron straps on the four vertical corners,
    nailed, and corner irons on the three rim corners the head does not cover. No tie rods or bands across
    the long walls: whether the 26-long side walls want a band at mid-height as well is open.
11. **One concentrate, the hutch product.** The concentrate is the fine heavy grains through the sieve,
    drawn off the hutch's bottom by the spout. A Harz jig also drew the coarse heavy grains lying on the
    sieve off through a gate at the tail end under the tail board; that would be a third product and a
    third way out, which #919 does not have.
12. **The spout runs open.** No plug or gate on it (a period spigot was throttled by a plug with a hole);
    the game meters the output, so nothing regulates it in the model.
13. **The sieve raised** from 14 to 17.2 this pass, so the bed's top stands just under the weir (1.65)
    and the tailings plainly overflow it; the plunger and the levels are unchanged.

## Tests

`tools/tests/test_jig_model.py` (the generated files against their own rules: the rig's vocabulary, its
reference poses, the cells rebuilt from the shipped shape, the motion, the anchors, the chutes' section,
the port in the tail end's head and the spout's open bore, the hutch's corner straps, the textures), `site/test/jig.test.ts` (the site's rig maths against the reference poses, the anchors the
viewer finds, the page's build stages and contents) and `tests/Jig/JigRigTests.cs` (the shipped rig through
`Machines/Core`'s parser and maths against the same poses).
