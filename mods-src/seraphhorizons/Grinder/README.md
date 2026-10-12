# Grinder

Part of the Seraph Horizons mod (`../README.md`): the ore line's **grind stage** (behaviour #732, model #733,
epic #684), one machine upgraded in place on the shared upgradeable-machine code (#711). A frame, then each
level's parts fitted into it, one item each, in order. Grinding never loses metal: it is what lifts poor ore's
× 0.4.

| Level | Tier | Fitted | Period machine | In the model |
|---|---|---|---|---|
| 1 | water | arrastra | a stone-paved pit at ground level, drag stones on a sweep turned from a water wheel's shaft through gearing overhead | the frame's paved pit; the hopper's rim on the frame's bin, the mouth a player fills; a post stepped on its centre stone, turned at a third of the axle by a bevel pinion on the overhead power shaft, sweeping four basalt drag stones round the paving on chains |
| 2 | mill | ball mill | a short riveted drum on hollow trunnions, a girth gear, trunnion feed, middlings reground | the arrastra and the hopper's rim come out; the drum goes into its pit on two pedestals, turned at a third of the axle by a pinion where the arrastra's was; the game's chute section is set into the bin's mouth for a chute line; fed through the north trunnion from the same chute, discharging into the pit |

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what #732 will be built against. Paths here are from this folder
unless they start with `assets/` or `tests/`, which are the mod's (`mods-src/seraphhorizons/`), or `Machines/`
and `Pipes/`, this folder's neighbours. The generic half of the generator is `Machines/tools/machinegen/`,
unchanged by this machine: no new driver, every motion a `rotate` on the axle's angle or a ride.

## The machine

**Footprint, fixed at its final size from the frame on: 10 cells.** The box is 3 × 2 × 2 (x, y, z): three
wide, two high, two deep. In the native frame (x west to east, y up, z north to south) the ground layer is all
six cells (the pit fills it), and the upper layer is the hopper's cell over the middle of the north side and
the whole south row (the head frame, the power shaft, and at level 2 the tips of the girth gear). The two upper
cells over the pit's north corners, beside the hopper, hold nothing of the machine at any level, so they are
not the machine's: a player may build there. The controller `[0,0,0]` is the middle of the north side at ground
level, under the hopper. The build frame (below) is measured from the box's north-west bottom corner, so the
shipped files are it moved one block west.

**The drive comes in high on the south; ore and water come in by the hopper on the north; the pulp leaves low on
the east.** Every anchor is at the same cell at every level (shipped coordinates):

| Anchor | Cell, face | What is there |
|---|---|---|
| `powerCell`, `powerFace` | `[0,1,1]`, south | The vanilla axle comes in along z at the upper south face's centre (x 24, y 24 voxels in the build frame) onto the power shaft. High, because the arrastra is driven from overhead. |
| `infeedCell`, `infeedFace` | `[0,1,0]`, up | Each level's infeed over the frame's bin: at level 1 the hopper's rim, the mouth a player tips ore into; at level 2 the game's chute section, which a chute line from the cell above meets with no gap or step (below). At level 2 the concentrator's middlings come back the same way, by the chute line: there is no separate return. |
| `waterCell`, `waterFace` | `[0,1,0]`, north | The ppex process-water inlet: ppex's straight pipe, drawn as the rosser's drip pipes are (below), through the hopper's north wall on the axis of a ppex pipe beyond the face. Water runs into the hopper and down the chute with the ore, at both levels. |
| `outputCell`, `outputFace`, `output.pos` | `[1,0,1]`, east, `[2.0, 0.2125, 1.35]` | The launder's lip on the east face, low: the ground pulp leaves towards the next machine (the concentrator). |

**The frame** (`requires` null) is what never changes between levels:

- the **pit**: a drystone curb two blocks across round cobbled paving laid on the ground in rows, a course of
  edging stones against the curb, and a granite centre stone a little proud of the paving; one curb stone, on the
  east-south-east, is cut down to a sill (the **overflow notch**), and a plank **launder** runs from it, falling,
  to the output face on a post;
- the **head frame** over the pit: four oak posts set in the curb, two stringers along z on them, knee braces,
  and two transoms between the stringers (T1 over the pit's centre, T2 over the power shaft);
- the **power shaft**: the vanilla axle's cross at the south face, then an octagonal iron shaft north to its end
  (z 20.6), carried in two iron drop hangers under T2, collars either side of the first; it turns with the axle in
  every state, the bare frame's included;
- the hopper's **bin** between the stringers, its walls up to y 28.8, its floor falling north at 45° to an outlet
  over a steep plank **chute**, which runs down south towards the pit's north side; the **water pipe's inlet**
  through its north wall (below). Above the bin, up to the infeed's top face, is each level's infeed (below).

**The infeed at each level.** At level 1 (the water tier, fed by the player) it is the **hopper's rim**: four plank
boards on the bin's walls, the mouth a player tips ore into. At level 2 (the mill tier, chute-fed and unattended) the
rim comes out and the **chute head** goes in: the game's own chute section, set into the bin's mouth from y 28.8 to
the infeed's top face, with plank head boards closing the bin's mouth either side of it. It is drawn as the owner's
rule for every chute-fed infeed says, as the vanilla chute, so a player's chute line meets it seamlessly: its walls
are the game's straight chute's elements (`shapes/block/metal/chute/straight.json`, kept in the generator as
`VANILLA_CHUTE`) turned as a placed `chute-straight-ud` is (the blocktype's `rotateZ` 90 about the block's centre),
an 8 × 8 square tube with walls 1 thick, centred in the cell, the north and south walls full width, in the chute's
own texture, `game:block/metal/sheet/copper1`, with the game's face UVs. The pack's lead patch
(`patches/unifiedpipes-chutesection.json`) is the chute *section* item's; a placed chute shows the game's copper
sheet. A `chute-straight-ud` placed in the cell above meets it wall for wall with no gap or step
(`check_chute_profile`).

**The water pipe** is drawn as the rosser draws its drip pipes (the owner's rule for every water inlet and line):
Pipes and Power Expanded's straight pipe as `UnifiedPipes` draws it soldered, built by the same module,
`Pipes/tools/solderedpipe.py` (ppex's 6 × 6 tube round its 4 × 4 bore, the same texture mapping, a hub where two
lengths meet and beads of solder at the joints). The rig has a part per metal, `pipecopper` and `pipelead`: the same
elements in `game:block/metal/sheet-plain/copper4` and `lead4`, the textures `UnifiedPipes` gives ppex's copper and
lead pipes, joints in `game:block/metal/ingot/leadsolder`; whichever metal is supplied is drawn. The grinder's is
one straight length from the water face through the hopper's wall, so it has no hub and no joint of its own: the
ppex pipe placed beyond the face brings its own half joint to it, as a soldered pipe does against any block (the
rosser's inlet is the same). It is the frame's, there at every level, like the anchor it serves.

### Why this layout

1. **A pit at ground level, so the drive is overhead.** An arrastra was a pit dug into the ground and paved; the
   old raised basin on piers is gone. Water-driven arrastras took their power from the wheel's shaft through
   gearing above the sweep, the post held at the top in a frame over the pit. Here the vanilla axle stands in for
   the wheel's shaft: it comes in high on the south, and a bevel pinion on its end turns a bevel wheel on the post
   under it.
2. **Two deep, not three.** The pit is two blocks across outside its curb, the smallest that still reads as an
   arrastra with four stones. The hopper and the drive fit over the pit's north and south edges rather than in
   rows of their own, so the box is 3 × 2 × 2, and two of its cells are dropped (above): 10 cells, against the
   old 18.
3. **One way in, at the top; out low, by the pit's own overflow.** The infeed takes fresh ore, from a player at
   level 1 and a chute line at level 2, and at level 2 the concentrator's middlings, which arrive by the same infeed
   as any other feed: the machine models no return path of its own (the owner's ruling, below). The pulp of both levels leaves by the same notch and launder: at
   level 1 it is the arrastra's overflow, at level 2 the drum discharges into the pit, which is its sump.
4. **The drive from the side, never the ore's ends**, as before: the axle comes in on the south, the ore runs
   north (in) to east (out).

### Level 2 retires the arrastra (and why)

The ball mill does not sit beside the arrastra: it goes into the arrastra's pit, and the arrastra's post, arms,
stones, step, top bearing and bevel pinion come out. This is the one place a level takes parts out, and it is
what the period did:

- **Period-correct.** An arrastra was a batch grinder: a charge was ground for hours, then the pit was drained and
  the paving cleaned up. A mill that put in a continuous ball mill retired its arrastras; nobody ran a ball mill's
  pulp through one (its few tons a day would have choked on the drum's output), and the mill tier is continuous
  and unattended by definition.
- **Readable.** One grinder in the pit at a time: a reader sees an arrastra or a ball mill, never both
  half-hidden, and the footprint is the larger of the two rather than their sum.
- **What stays is the frame**, which both levels use: the pit (at level 2 the drum's sump and foundation; its
  overflow and launder carry the pulp out exactly as they did), the head frame, the power shaft, the hopper's bin,
  the chute and the water inlet. The hopper's rim, the player's mouth, gives way to the chute head: a chute-fed
  mill has no need of a hand-filled hopper. The ball mill's pinion goes on the power shaft's end where the arrastra's was.

Reusing the pit as the drum's sump is a simplification: a period mill would have built the ball mill its own
foundation and launders. It is said here so the owner can rule on it.

### Powered substitutes for the period drives

No animal, water wheel or engine: power comes from the axle, and both levels work whichever way it turns.

- **The arrastra** was mule- or water-driven. The water-driven ones had the drive overhead, as here; the 3:1
  bevel pair stands in for the wheel's own gearing.
- **The ball mill** keeps its girth gear and pinion (3:1), the pinion straight on the power shaft over the
  gear: period mills were belt-driven to a pinion shaft; the axle takes the belt's place.

Gearing ratios are the model's (the turns the work makes per axle turn); gameplay's throughput is its own.

### Either way round

The axle may turn either way (the owner's rule for every machine), and nothing in the grinder depends on which:

- **The gearing** has no catch, ratchet or one-way part: both bevel pairs and the girth gear and pinion are
  symmetric teeth, and every part's motion is a plain `rotate` that runs backwards with θ.
- **The arrastra's drag stones** hang on bridles of two chains, one fore and one aft of each stone, so the same
  bridle drags it whichever way the arms go; a period stone on one leading chain would trail the wrong way in
  reverse.
- **The ball mill's charge** is drawn still, its top level. A running charge heaps up the side the drum lifts, so
  a heap drawn up one side would be right for one direction only; the level charge reads right both ways.
- **The ore's way** does not depend on the turn: the feed falls in by the chute and the trunnion, the arrastra
  sweeps round either way, and the drum discharges over its lip and the pit overflows whichever way they turn.

### Every turning part

θ is the axle's angle. A part that rides another turns with it; nothing else moves. Turns are per axle turn;
the whole machine repeats every three (the viewer's θ slider spans three turns).

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| **Frame** | | | |
| Power shaft: the axle's cross, the iron shaft, its collars (`shaft`) [null] | The vanilla axle | The fitted level's pinion | `rotate` z, 1 |
| Pit, launder, head frame, hangers, the hopper's bin, chute (`frame`) [null] | Fixed | — | none |
| Water pipe, in copper (`pipecopper`) [pipecopper] or lead (`pipelead`) [pipelead] | Fixed, the metal supplied drawn | — | none |
| **Level 1: arrastra** | | | |
| Bevel pinion, 6 teeth, on the power shaft's end (`l1pinion`) [l1post] | The power shaft (keyed) | The post's bevel wheel | rides `shaft` |
| Post: iron gudgeons, oak post, iron bands and cap, bevel wheel (18 teeth) and hub (`l1post`) [l1post] | The pinion | The arms | `rotate` y, +1/3 |
| Sweep arms and their staples (`l1arms`) [l1arms] | The post (through it) | The chains | rides `l1post` |
| Drag stones, their eyes and chain bridles (`l1stones`) [l1stones] | The arms, by the chains | Grind on the paving | rides `l1post` |
| Footstep and top bearing (`l1step`) [l1post]; the hopper's rim (`l1hopper`) [l1hopper] | Fixed | — | none |
| **Level 2: ball mill** | | | |
| Pinion, 8 teeth, and hub on the power shaft's end (`l2pinion`) [l2drive] | The power shaft (keyed) | The girth gear | rides `shaft` |
| Drum: trunnions, heads, riveted shell, manhole, girth gear (24), discharge lip (`l2drum`) [l2drum] | The pinion | Lifts the charge | `rotate` z, −1/3 |
| Sole plates, pedestals, trunnion bearings (`l2bed`) [l2bed]; feed lip (`l2feed`) [l2feed]; chute head (`l2chute`) [l2chute] | Fixed | — | none |
| The charge (`l2balls`) [l2balls] | Lies in the drum's bottom, its top level (either way round, above) | — | none |

At one axle turn a second the arrastra's post turns 20 times a minute, the drum 20. Toothed wheels and what
they mesh (nothing toothed meshes nothing): the arrastra's pinion and wheel; the ball mill's pinion and girth
gear. The two pinions are never on the shaft together.

### The levels' fitted parts (provisional)

Each level's set goes on in this order, one item each (the rig's `levels`). The items are #732's to choose; the
`requires` ids and the order are the model's proposal.

| Level | # | `requires` | What it fits |
|---|---|---|---|
| 1 | 1 | `l1hopper` | The hopper's rim on the bin: the mouth a player tips ore into |
| 1 | 2 | `l1post` | The post with its footstep on the centre stone, its top bearing under T1, its bevel wheel, and the pinion on the power shaft |
| 1 | 3 | `l1arms` | The four sweep arms through the post |
| 1 | 4 | `l1stones` | The four drag stones and their chain bridles |
| 2 | 1 | `l2bed` | The two sole plates on the paving, the pedestals and the trunnion bearings |
| 2 | 2 | `l2drum` | The drum with its trunnions, heads, manhole, girth gear and discharge lip |
| 2 | 3 | `l2drive` | The pinion on the power shaft's end |
| 2 | 4 | `l2balls` | The charge of iron balls, in through the manhole |
| 2 | 5 | `l2feed` | The feed lip from the chute into the north trunnion's bore |
| 2 | 6 | `l2chute` | The chute head in the bin's mouth, for a chute line, and its head boards |

Level 2 retires level 1's four (`retires` in the rig). **In the viewer** (`site/models.json`): one select,
**Level**, steps from "The frame" through "Level 1 (water): arrastra" (the default) to "Level 2 (mill): ball
mill", each fitting that level's whole set and none of the other's; the checkboxes under it show a level's
stages one at a time. A second select, **Water pipe**, draws the pipe in copper (the default) or lead, as the
rosser's drip pipes. θ has a slider and Play.

### Collision

The rig's cells use the shared format (`docs/recipe-browser/models.md`, "Collision by what is fitted"): a cell's
`boxes` are the frame's, there at every level (the pit, the head frame, the hopper and the shaft), and its
`fitted` holds the boxes each `requires` value adds while it is fitted, so the arrastra's stones are not
there at level 2 and the drum is not there at level 1. A turning part's boxes are its bounding boxes over the
whole cycle (the sweep's arms and stones fill the pit while they turn; the drum is round anyway). No lids:
whether the top is walked on is open. The game's rig loader does not read `fitted` yet.

## Model

Everything in the model was made for this mod: no other mod's model is used. The water pipe is built with the
mod's own soldered pipe (`Pipes/tools/solderedpipe.py`), as the rosser's. Textures are the game's, referenced:
debarked oak and oak planks, the generic planks of the vanilla axle's cross, iron plate, plain steel sheet,
riveted iron (the drum's shell), iron chain, copper and lead sheet (the pipe, as UnifiedPipes gives ppex's copper
and lead pipes; a joint would be in lead solder, but this one length has none), the game's chute copper
(`block/metal/sheet/copper1`, level 2's chute head), and granite as
cobblestone (paving), dry stone (curb) and rock (centre stone); the drag stones are basalt, a hard dark rock as
they were chosen, which also reads apart from the paving. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/grinder.json` | The frame, the pipe in both metals and both levels' parts (387 elements), each under its `requires`. |
| `assets/seraphhorizons/shapes/block/grinder_frame.json` | The static frame only (77 elements): what a block would draw (the pipe is drawn in its metal, as the rosser's). |
| `assets/seraphhorizons/config/grinder-rig.json` | Footprint, anchors, the levels, the part rig (16 parts). |
| `tests/Grinder/rig-reference.json` | Every part's matrix at a grid of axle angles, from the reference maths. |

Elements drawn: the frame alone 95 (with the shaft and one metal's pipe), level 1 199, level 2 279.

### Dimensions (voxels, build frame)

- **Pit**: centre (24, 16). Paving y 0..1.5 in nine rows; edging radius 12..13.5, top 1.8; centre stone radius 3.2,
  top 2.0; curb radius 13.5..16, y 0..6 (two blocks across outside); notch sill y 4 on the east-south-east;
  launder z 19.6..23.6 from x 37 (floor at y 4) to the east face (y 3.4).
- **Head frame**: posts 3 × 2.8 at x 17..20 and 28..31, z 0..2.8 and 29.2..32, up to y 27; stringers y 27..31;
  T1 z 14..18, T2 z 24.8..30.4.
- **Power shaft** along z at (x 24, y 24), from z 20.6 to the south face; the axle's cross from z 30.6; hangers
  at z 25.4..27.4 and 28.4..30.
- **Hopper's bin** x 20..28, z 1..13, its walls up to y 28.8 and down to y 19.6 north of z 8.2 and 23.8 south of it
  (over the bevel wheel); its floor from (z 12.2, y 28.2) down to (z 4.4, y 20.4); the chute from there to
  (z 5.6, y 13). Level 1's rim y 28.8..32 round the bin; level 2's chute head y 28.8..32 at x 20..28, z 4..12 (the
  infeed cell's 4..12 both ways), walls 1 thick, head boards y 28.8..29.6 either side.
  Water pipe: ppex's 6 × 6 tube at (x 24, y 24), z 0..1.8, through the hopper's north wall.
- **Level 1**: post radius 2 from y 3.6 to 24.4 on an iron step on the centre stone, its top gudgeon in a bearing
  y 24.8..26.4 under T1; bevel wheel radius 7.5 (18 teeth, module 0.83) under the pinion radius 2.5 (6 teeth),
  apex (24, 24, 16); arms y 8.5..11.5 to radius 12; drag stones radius 4.6..11.5, 4 wide, y 1.5..4.5, on chains
  1 voxel thick to staples under the arms.
- **Level 2**: drum axis along z at (x 24, y 12); shell radius 7..7.6 (about 0.95 m across), z 10.6..20.4 (0.6 m
  long: a short drum, as Krupp's were); heads to radius 8.2; trunnions radius 2.6..3.8, z 6..9.4 (north, the
  feed) and 21.6..28 (south, the lip to 28.7); girth gear pitch radius 9 (24 teeth, module 0.75) on the south head
  under the pinion pitch radius 3 (8 teeth); bearings z 6.6..8.8 and 24.4..27.2 on pedestals on sole plates on
  the paving.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Grinder/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Grinder/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Grinder/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

A full run takes about a minute. Output is deterministic (two runs into two folders `diff -r` clean).
`tools/validate_grinder.py` holds the checks; every run checks its output and exits non-zero if one fails.
The model holds three states never drawn together (the frame alone, and the frame with level 1 or level 2),
and every check comparing parts does it a state at a time.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part. **Chunky:** no element is a rod thinner than 0.9 voxels both ways (a plate may be thin).
- **The build:** every part but the frame's in one level's stage, needing its `requires`; no `requires` in two
  stages; level 2 retires exactly level 1's; at rest the frame, then each of a level's stages in turn, joined to
  the ground through what is already there (nothing floats).
- **Textures** by role (the list in "Model" above).
- **Anchors:** the anchor faces on the footprint's outside, looking out, all different, and no anchor in the
  power cell; the axle's cross at the power face's centre; each level's infeed (the rim, the chute head) the infeed
  cell's top face; the inlet pipe ppex's section, on the water face's axis, opening into the hopper above its floor; the launder at
  the output face, `output.pos` its end.
- **The pit:** the paving on the ground with its top at 1.5 and nothing of the frame under it (no piers); the curb
  on the ground with the notch's sill its lowest top; the drag stones on the paving at rest and inside the curb
  over the cycle.
- **Chute profile:** level 2's chute head, at the infeed face, is a placed `chute-straight-ud`'s section wall for
  wall in the cell's place, its top on the face (no gap or step), in the chute's own texture; `VANILLA_CHUTE` is
  compared with the game's `straight.json` when the game is installed (`VINTAGE_STORY`, else
  `~/Games/vintagestory`).
- **The ore's way:** in each state only that level's infeed reaches the top face (one way in, for the middlings
  too; the bare frame has none); the bin's floor runs down to the outlet; the chute runs from under the outlet; at level 1 its end is over the stones' track and clear above
  the arms; at level 2 the feed lip runs from the chute's end into the north trunnion's bore; the drum's lip is over
  the paving inside the curb; the launder falls from the notch's sill to the face.
- **Supports:** the power shaft in its two hangers; the post on its step and in its top bearing; each trunnion
  through a bearing closed round it on four sides; each pinion's hub on the power shaft's end.
- **Footprint:** nothing at any pose reaches the two dropped cells (each element's box against the cell's).
- **Containment:** nothing leaves the box over the cycle.
- **Gearing:** for each mesh the pitch points move together and the ratio is the tooth counts'; over a whole
  turn of the faster wheel, every 2.5 degrees, no tooth runs into the other wheel and the mesh is always engaged.
- **Gaps:** the arms under the chute, the bevel wheel under the hopper, the drum under the power shaft and clear
  of the chute, each at least 0.3 apart over the cycle.
- **Clearances:** every pair of parts that move against each other, over the three-turn cycle (every 15 degrees;
  every 5 in full runs), touches only at the intended contacts (`ALLOWED`: shafts in their bearings and step,
  keyed hubs, meshing teeth, the stones on the paving, the charge on the shell).
- **No z-fighting** (full runs): no coplanar overlapping faces in any state at three axle angles, after the fix
  (`fix_coplanar`, a level at a time with the frame).
- **Files:** every texture declared; the frame shape the frame part alone; 10 cells, fitted boxes keyed by
  requires values and inside their cells, no lids; the rig's `levels` naming every `requires` once, water then
  mill; the shipped model the checked one moved.

`tools/tests/test_grinder_model.py` holds the written files to these rules without running the generator, and
this README's level table to the generator's; `site/test/grinder.test.ts` replays the reference poses and the
viewer's states.

### Rig schema (`grinder-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"). The only input is θ; every driver
is a `rotate` on it, and the rest of the parts ride or stand still. Keys: `cells` (10, with `boxes` and
`fitted` as in "Collision"; no lids), `powerCell` and `powerFace`, `infeedCell` and `infeedFace`, `waterCell`
and `waterFace` (the rosser's key), `outputCell`, `outputFace` and `output`, `levels`
(`[{level, tier, name, fitted, retires?}]`: each level's tier, its `requires` values in fitting order, and the
values it takes out) and `parts`. `requires` values: `l1hopper`, `l1post`, `l1arms`, `l1stones`, `l2bed`,
`l2drum`, `l2drive`, `l2balls`, `l2feed`, `l2chute`, `pipecopper`, `pipelead` (the pipe's metal, not a level's),
or null.

### Editing by hand

Element names are the rig's interface (first-match globs, a part's id and `_`): `sh_*` (the power shaft),
`fr_*` (the frame), and `l1pinion_*`, `l1post_*`, `l1arms_*`, `l1stones_*`, `l1step_*`, `l2pinion_*`, `l2drum_*`,
`l2bed_*`, `l2feed_*`, `l2balls_*`, `l1hopper_*`, `l2chute_*`, `pipecopper_*`, `pipelead_*`. Hand edits are lost when the script runs again: port them into
`make_shape.py`, or stop regenerating.

## Open for the owner

1. **Retiring the arrastra at level 2** (above): the ball mill goes into the arrastra's pit, which becomes its
   sump, and the arrastra's working parts come out. The alternative, keeping both, needs a bigger footprint and
   gives the arrastra no job.
2. **The water and the infeed share the hopper's cell** (`[0,1,0]`: the top face takes ore, the north face the
   pipe). If gameplay wants one role per cell, the inlet can come in on the west face of `[-1,1,0]` instead, a
   pipe along the hopper's west side, at the cost of that cell joining the footprint (11 cells).
3. **The dropped cells.** The footprint is the 3 × 2 × 2 box less the two upper cells over the pit's north
   corners, which hold nothing at any level. #711 may prefer the plain box (12 cells).
4. **The power high on the south.** The overhead drive is what makes a ground-level pit work; it puts the axle a
   block up (y + 1) on the south face.
5. **The items and the finer stages** within each level (the `requires` ids and order above are a proposal).
6. **Sizes and speeds:** the pit two blocks across outside (period arrastras were 3 to 6 m), the drum about
   0.95 m by 0.6 m; the post and the drum each at a third of the axle.
7. **The charge** is drawn lying still in the drum's bottom, seen only with the drum unticked; the ore and pulp
   are not drawn at all (no work input).

Decided: **no return path inside the machine** (the owner's ruling). Returns are handled outside it, by placing
machines so that one feeds another above it, or later by Archimedes screws; the concentrator's middlings simply
come back in through the normal infeed, the hopper's mouth, with the fresh ore. Nothing in the model carries
them: there is no return funnel, launder or anchor.

## Known weak spots, and what is not checked

1. **Box teeth.** Spur teeth are radial boxes; bevel teeth are boxes along the pitch cone at mid-face size (they
   do not taper), the wheel's lengthened into its body so they meet its back.
2. **Rigid chains.** The bridles do not swing.
3. **The paving's rows** end under the edging course; seen from directly above, the edging's stones overlap where
   they meet (their tops are stepped by hundredths, so they do not flicker).
4. **Not seen in a client:** there is no block or renderer, so lighting, the textures' scale and the machine at
   play distance are unchecked. It was reviewed in the site's model viewer.
