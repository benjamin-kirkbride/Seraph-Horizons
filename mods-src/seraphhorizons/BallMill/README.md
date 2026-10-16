# Ball mill

Part of the Seraph Horizons mod (`../README.md`): the continuous grinder of the ore line (epic #684). A
**Krupp (Gruson) ball mill** of the 1890s: a short drum on a hollow trunnion, turning on its side, half full
of iron balls that tumble over the ore and grind it, and round the drum the **screens** that let out only
what is ground fine enough. Crushed ore (and the shaking table's middlings, merged into the same chute line
by the player) comes in by a chute; ground ore leaves by a chute. It lifts the poor-ore × 0.4 and the
unclassified × 0.85 and frees the middlings' locked share (#915, #765).

This folder holds the model's generator and its rig (`tools/`). The gameplay is #915 and is not built yet;
the model is #916. "Gameplay" below says what the rig offers it.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator; the
generic half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one
(no new driver: every motion is a plain `rotate` on the axle's angle).

## The machine

**Footprint.** 3 long (x) × 2 high × 2 deep (z, away from the player): 12 cells, all of them the machine's.
The controller `[0,0,0]` is the middle of the front (north) row at ground level. The drum's axis runs along
the length, 1.25 blocks up and centred across the depth. Every dimension is what has to fit:

- **Long (3):** the feed box (half a block) at the west end, the trunnion's bearing, the drum between its
  two heads (1.64 blocks), the girth wheel and the journal's bearing at the east end.
- **High (2):** the drum (1.48 blocks across over its heads) on its axis at 1.25 blocks, over the discharge
  hopper and the output chute, whose floor must be a placed chute's (5 voxels up).
- **Deep (2):** the drum, the hopper's V sides round it, and the pinion low at the front under the girth
  wheel.

**Anchors** (`assets/seraphhorizons/config/ballmill-rig.json`, shipped cells):

| Anchor | Cell, face | What meets it |
|---|---|---|
| `powerCell`, `powerFace` | `[1,0,0]` east | The vanilla axle, along x at the cell face's centre: it is the pinion shaft's line, so the axle drives the pinion's shaft directly (no overhead shaft, no head frame) |
| `infeedCell`, `infeedFace` | `[-1,1,0]` west | A chute line from the west (`chute-straight-we`), a block up: the feed box's chute section is the game's, wall for wall, at the face |
| `waterCell`, `waterFace` | `[-1,1,1]` west | The ppex water pipe, on the face's axis (the rosser's key) |
| `outputCell`, `outputFace` | `[0,0,1]` south | A chute line to the south (`chute-straight-ns`), low: the hopper's chute section is the game's at the face |
| `output.pos` | `[0.5, 0.3125, 2.0]` | The middle of the output chute's floor on the face, where the ground ore leaves |

There is no return anchor: the middlings come back by the infeed (the owner's ruling, #915), and the
machine's water is used up (no return water, #711).

**Material's way.**

1. *In:* the infeed chute's section runs through the feed box's west wall, its floor at 21 voxels. The box's
   V floor falls from the walls (21) to a level strip on the drum's axis (20.4); the strip runs on through the
   east wall as the **feed spout**, a trough falling a voxel into the hollow west trunnion's bore, clear of it
   all round (2.58 from the axis at most, the bore 3.4). The water's pipe opens into the box over the V
   floor's south side.
2. *Grinding:* the iron balls tumble the ore in the drum of riveted grinding plates. The balls are not drawn
   (the owner's ruling): the model is the drum, not its charge.
3. *Out:* the ground pulp passes the coarse screen (perforated plate) and the fine screen (wire cloth) and
   falls into the **discharge hopper** under them: its floor's wings fall from the end plates (7.2) to the
   middle channel (5.6), which falls south to the output chute's floor (5.0) at its mouth in the south wall;
   the chute runs level to the face.

### Every turning part

θ is the axle's angle. A part that rides another turns with it; nothing else moves. Turns are per axle
turn; the whole machine repeats every three (the viewer's θ slider spans three turns).

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Pinion: the vanilla axle's cross, the steel shaft, its two collars, 8 stainless teeth (`pinion`) [pinion] | The vanilla axle at the power face | The girth wheel | `rotate` x, 1 |
| Drum: hollow west trunnion, two heads, riveted grinding plates, solid east journal, the cast flange on it (`drum`) [drum] | The girth wheel (bolted to its flange) | Lifts and tumbles the balls (not drawn) | `rotate` x, −1/3 |
| Girth wheel, wooden: two courses of six oak felloes, an iron tyre round them, 24 oak cogs mortised through both, 12 iron bolts (`girth`) [girth] | The pinion | The drum | rides `drum` |
| Screens: coarse (perforated plate), fine (wire cloth), 12 bars, a middle hoop (`screens`) [screens] | The drum (they turn with it, as Krupp's did) | — | rides `drum` |
| Feed box, its legs, V floor and spout (`feedbox`) [feedbox] | Fixed | — | none |
| Discharge hopper, its feet and the output chute's bracket (`hopper`) [hopper] | Fixed | — | none |
| Chute sections: the infeed through the feed box's west wall, the output from the hopper's channel (`chutes`) [chutes] | Fixed | — | none |
| Water pipe, in copper (`pipecopper`) [pipecopper] or lead (`pipelead`) [pipelead] | Fixed, the metal supplied drawn | — | none |
| Bed, pedestals, bearings, the pinion's pillow blocks (`frame`) [null] | Fixed | — | none |

Toothed wheels and what they mesh: the pinion and the girth wheel, nothing else. The module (0.901) is set by
the centre distance the cells fix (the power face's centre to the drum's axis, 14.42 voxels) and the teeth
(8 and 24, so the drum turns a third of the axle, as with v3's first 10 and 30).

**The girth wheel is wooden** (the owner's ruling), a period mortise wheel: two courses of six oak felloes,
the second course turned half a felloe so their joints break, bolted with twelve iron bolts to a cast flange on
the drum's journal, an iron tyre shrunk on round the felloes (under the cogs' roots, clear of the mesh),
and 24 oak cogs mortised through felloes and tyre, their tails showing inside the rim. The felloes run
down over the flange, which stays behind them, to a thin boss on the journal, so the wheel's face is oak
rather than iron (the owner, 2026-10-15: it read as iron). Wood
needs a coarser pitch than iron, so the pair went from 10:30 to 8:24 (pitch 2.83 voxels against 2.27), and
the cogs take 0.55 of the pitch (1.56 voxels thick) against the stainless pinion's 0.34 (0.96), the usual
share when wood runs with metal. The wheel's tips (r 11.72) stay inside the heads' 11.8.

### Build stages

The owner's order (2026-10-15). The frame is placed as the machine and needs nothing; then each stage is one
group of `requires`, fitted in order, its fits in order, services last. The rig's `buildStages` lists them
(`BUILD_STAGES` in `make_shape.py`), and the viewer's Build stage select has one state a stage, named for it
(`BUILD_STATES`), with the water pipe's metal a select of its own once services are in. Materials are the
"Current figures" of the stages proposal (metal measured and paid at 128 vox an ingot, a plate 2 ingots;
timber 1,024 vox a plank); "~" marks a guess or a choice of item.

| # | Stage (state id) | `requires` | Materials |
|---|---|---|---|
| F | Bed, pedestals, bearings, the pinion's pillow blocks (`frame`), placed | — | 2 stone brick block (granite bed), 4 iron plate, 5 iron nails and strips |
| 1 | Drum (`drum`) | `drum` | 17 iron plate |
| 2 | Gearing (`gearing`): the girth wheel, then the pinion meshing it | `girth`, `pinion` | 1 `game:spurgear-s` (wooden girth wheel), 1 plank (its hub and arms), 2 iron nails and strips (its iron tyre, 42 vox), 1 `seraphhorizons:gear-stainless` (pinion), 1 steel rod (pinion shaft) |
| 3 | Screens (`screens`) | `screens` | 13 iron chain (fine cloth, 1,609 vox²), 12 iron chain (coarse screen, 1,475 vox²), 1 iron plate |
| 4 | Sheet iron (`sheetiron`): the discharge hopper, then the feed box | `hopper`, `feedbox` | 4 iron plate, 12 iron nails and strips (the hopper's 2 and 7 and the feed box's 2 and 5, merged) |
| 5 | Services (`services`): the chute sections and the water pipe | `chutes`, `pipecopper` or `pipelead` (a choice) | 2 chute section, 2 pipe section (copper or lead) |

The balls are not drawn and are no stage (a charge, like the ore and water). No part wears as a stage.

### Either way round

The axle may turn either way (the owner's rule for every machine), and the ball mill needs nothing for it
(#915: "the drum is symmetric: no rectifier"):

- **The gearing** is a plain spur pair, its motion a `rotate` that runs backwards with θ.
- **The drum** is drawn with plain grinding plates. Krupp's were stepped (overlapping, each step lifting the
  balls), which is right for one direction only; plain plates read right both ways (Open, 4).
- **The ore's way** does not depend on the turn: the feed falls into the trunnion by the spout, and the
  screens let the pulp through into the hopper whichever way they turn.

### Why this layout

- **The drive.** #916 drops v2's overhead shaft: the axle goes onto the pinion's shaft directly. The pinion
  shaft is therefore the axle's line, through the power face's centre, which puts it low at the front
  (`[1,0,0]` east, y 8, z 8); the girth wheel is sized to reach it from the drum's axis. Period mills were
  belt-driven to a pinion shaft; the axle takes the belt's place.
- **The infeed a block up.** The ore must fall into the trunnion's bore (its bottom 16.6 voxels up), so the
  feed comes in above it: a chute at the west face of the upper cell is the lowest a chute can run in by
  gravity. A line therefore steps down a block to the mill, as period mills stepped down a hillside.
- **The water beside it**, on the same west face, into an elbow on the feed box's south side: the box stays
  half a block wide (the chute's cell) and the pipe is one straight length and a soldered bend.
- **The output at the back.** Under the drum there is half a block; the output chute's floor is a placed
  chute's (5 voxels), so it leaves through the hopper's south wall, where the screens are high enough over it.
  The front stays clear for the player: the drum, the girth wheel and the pinion face them.
- **No casing over the screens.** Krupp's drum ran in a sheet-iron casing whose lower half was the discharge
  hopper. The model keeps the hopper (up to two thirds of the way to the axis: the wet pulp leaves the drum
  low) and leaves the upper half off, so the screens show (Open, 3).

### Collision

Every cell has boxes (`cells[].boxes`), built from every fitted part's elements, the turning parts swept over
the three-turn cycle, the water pipe in its first metal. The top layer's six cells have a `lid` (the deck over
the drum, `machinegen.checks.with_lids`), so no one walking over the mill drops between its boxes.

## Model

| Part | Dimensions (voxels, build frame: x 0..48, y 0..32, z 0..32) |
|---|---|
| Bed | Cut granite (`stonebrick`), y 0..3, x 0.5..47.5, z 0.5..31.5, in block-sized stones |
| Pedestals and bearings | Cast iron: sole plates, columns, bearings closed round the trunnion and journal (r 4.8, walls 1.4), caps bolted down; the east bearing's end cover over the journal's end |
| Drum | Axis y 20, z 16. West trunnion r 3.4..4.8, x 8.6..13.3; heads r 3.4..11.8, x 13.3..14.7 and 38.2..39.6; grinding plates r 8.2..9.0 (riveted); east journal r 4.8, x 39.5..46.9 |
| Screens | Coarse r 9.5..9.9 (`perforated`, the game's mesh1), fine r 10.5..10.8 (`wirecloth`, mesh4), 12 bars r 10.8..11.4, a hoop r 10.8..11.6, x 14.7..38.2 |
| Girth wheel | Oak: 24 cogs, module 0.901, pitch r 10.82, tips r 11.72, 1.56 thick, tails to r 5.3; felloes r 5.8..9.19 in two courses x 41.2..42.1 and 42.1..43.0, the iron tyre r 9.19..9.59 round them; 12 bolts at r 7.6, heads 1.0 across, 0.15 proud |
| Drum's flange | Cast iron on the journal: r 4.6..8.2, x 40.6..41.2 (behind the felloes), its boss r 5.2 to x 43.1 |
| Pinion | Stainless, 8 teeth 0.96 thick, pitch r 3.61, x 41.3..42.9, on a steel shaft (y 8, z 8) in two pillow blocks, collars outside them, the axle's cross from x 46.6 to the face |
| Feed box | Sheet iron, x 0..8.2, y 20..30.4, z 3.2..21 on four legs; V floor 21 → 20.4; spout to x 11.4, falling to 19.4 |
| Hopper | Sheet iron, end plates x 12.6..13.0 and 39.9..40.3, low walls z 6.6..7.0 and 25.0..25.4 (y 4.4..10), V sides up to y 14.6; floor wings 7.2 → 5.6, channel 5.6 → 5.0 |
| Chutes | The game's straight chute's walls (copper sheet): infeed x 0..0.8 at the west face (y 20..28, z 4..12), output z 25..32 at the south face (x 20..28, y 4..12) |
| Water pipe | ppex's 6 × 6 tube (`Pipes/tools/solderedpipe.py`): inlet x 0..1.1 at (y 24, z 24), an elbow centred x 4.1, a run north through the feed box's south wall to z 19.6 |

**Textures** by role (`validate_ballmill.ROLES`): cut granite for the bed; cast iron (`iron`) for the
castings (pedestals, bearings, heads, trunnion and journal, the drum's flange, the screens' bars and hoop, the
spout, legs, feet and brackets) and the girth wheel's bolts and tyre; oak (`oak`, end grain `oak-end` on the cogs'
tips and tails, as the gantry's timbers) for the girth wheel; stainless (`stainless`) for the pinion; sheet iron
(`sheet`) for the feed box and the hopper; steel for the pinion's shaft and collars; riveted iron for the grinding plates; the game's mesh textures (they have holes, so
in the game the drum shows through the screens) for the screens; the game's chute copper for the chute
sections; the vanilla axle's own wood for its cross; the pipe's metal and solder.

### Regenerating

```sh
python3 mods-src/seraphhorizons/BallMill/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/BallMill/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/BallMill/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

A full run takes about 75 seconds. Output is deterministic (two runs into two folders `diff -r` clean).
`tools/validate_ballmill.py` holds the checks; every run checks its output and exits non-zero if one fails.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part. **Chunky:** no element is a rod thinner than 0.9 voxels both ways (a plate may be thin).
- **Size:** the drum's measured size against the issue's (printed; fails under 1.25 across or 1.5 long).
- **The build:** every part but the frame needs its own `requires`; the rig's `buildStages` are the owner's
  (drum; gearing; screens; sheet iron; services, the chutes and the pipe's metals), every `requires` in one
  stage; `BUILD_STATES` one a stage, cumulative, no chute or pipe before services; at rest the frame, then each
  fitted part in turn (`FIT_ORDER`, the stages' fits in order), joined to the ground through what is already
  there (nothing floats).
- **Textures** by role (above).
- **Anchors:** the anchor faces on the footprint's outside, looking out, all different, and no anchor in the
  power cell; the axle's cross at the power face's centre on the pinion shaft's axis; the infeed and output
  chute sections reaching their faces inside their squares; the inlet pipe ppex's section on the water face's
  axis; `output.pos` the output chute's floor at the face.
- **Chute profile:** each chute section, at its face, is a placed chute's (`chute-straight-we` at the infeed,
  `chute-straight-ns` at the output) wall for wall in the cell's place, in the chute's own texture;
  `VANILLA_CHUTE` is compared with the game's `straight.json` when the game is installed (`VINTAGE_STORY`,
  else `~/Games/vintagestory`).
- **The ore's way:** the infeed chute's floor over the V floor where it ends; the V floor falling to the strip
  on both sides; the strip running on as the spout, which falls into the trunnion's bore, every corner of it
  within the bore less 0.3; the water pipe opening inside the box above its floor; the drum's lowest point
  over the hopper's floor; the wings falling to the channel, the channel to the chute's floor at its mouth;
  the V sides under the axis and at least 0.3 clear of the turning drum.
- **Supports:** the west trunnion through its bearing and the east journal into its, each bearing closed round
  it on four sides; the pinion's shaft in its two pillow blocks, a collar against each one's outer face; the
  pinion on the shaft's end, the drum's flange hub on the journal.
- **Containment:** nothing leaves the box over the cycle. **Cells:** every footprint cell holds something.
- **Gearing:** the pitch points move together and the ratio is the tooth counts'; over a whole turn of the
  pinion, every 2.5 degrees, no tooth runs into the other wheel and the mesh is always engaged.
- **Wooden girth wheel:** oak felloes and cogs, only the bolts and the tyre iron, the tyre round the felloes
  under the cogs' root; the felloes' face on the drum's flange,
  overlapping it in radius; two courses with joints broken; the bolts on the felloes' face inside the flange's
  radius; the cogs mortised through (tails inside the felloes), thicker than the pinion's teeth; the tips inside
  the heads' radius; the face oak (felloes at least 3 deep, the flange inside their outside, the iron boss no
  more than 0.5 round the journal and under the cogs' tails); the pinion stainless.
- **Gaps:** the screens over the hopper, the heads by its end plates, the girth wheel and the pinion by it,
  the drum's flange by both, the trunnion's mouth by the feed box, the spout in the bore, the girth wheel by the east bearing, the pinion
  by the east pedestal: each at least 0.2 apart over the cycle.
- **Clearances:** every pair of parts that move against each other, over the three-turn cycle (every 15
  degrees; every 5 in full runs), touches only at the intended contacts (`ALLOWED`: the trunnion and journal in
  their bearings, the pinion's shaft in its pillow blocks, meshing teeth).
- **No z-fighting** (full runs): no coplanar overlapping faces at four axle angles, after the fix.
- **Files:** every texture declared; the frame shape the frame part alone; 12 cells, each with boxes inside it,
  the top layer lidded; the shipped model the checked one moved.

`tools/tests/test_ballmill_model.py` holds the written files to these rules without running the generator;
`site/test/ballmill.test.ts` replays the reference poses and checks the page; `tests/BallMill/BallMillRigTests.cs`
holds the C# rig maths to the same poses.

### Rig schema (`ballmill-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"). The only input is θ; the pinion's
and the drum's drivers are `rotate`s about x on it, and the rest of the parts ride the drum or stand still.
Keys: `cells` (12, with `boxes` and, on the top layer, `lid`), `powerCell` and `powerFace`, `infeedCell` and
`infeedFace`, `waterCell` and `waterFace` (the rosser's key), `outputCell`, `outputFace` and `output`,
`buildStages` (each `{stage, id, name, adds}`: the `requires` it fits, in order) and `parts`. `requires`
values: `pinion`, `drum`, `girth`, `screens`, `feedbox`, `hopper`, `chutes`, `pipecopper`, `pipelead` (the
pipe's metal), or null (the frame).

### Editing by hand

Element names are the rig's interface (first-match globs, a part's id and `_`): `pinion_*`, `drum_*`,
`girth_*`, `screens_*`, `feedbox_*`, `hopper_*`, `chutes_*`, `pipecopper_*`, `pipelead_*` and `fr_*` (the
frame). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Provisional pending #711

The shared machine code has not landed, so these anchors follow the v2 branches and the rosser and may move
with it: the chute anchors as cell and face (`infeedCell`/`infeedFace`, `outputCell`/`outputFace`) with the
chute sections drawn as the game's chute at those faces; `output.pos`; the water as `waterCell`/`waterFace`
(the rosser's key) with the soldered pipe on its axis. Whether a wet machine's reservoir is drawn (#711) is
open: the feed box stands for it.

## Open for the owner

1. **The drum's length.** 1.48 blocks across over its heads (the issue's "about 1.5") but 1.64 long between
   its heads (1.86 with the girth wheel), against "two long": the feed box, two bearings and the girth wheel
   share the three blocks with it. A longer drum needs a shorter feed box or the girth wheel on the head.
2. **The infeed a block up** (`[-1,1,0]` west), so the mill stands a block below the battery's output chute,
   or the line drops a block on the way. A top-face infeed (`up`) is possible but higher still, and it would
   widen the feed box into the trunnion's bearing.
3. **No casing over the screens.** Period mills enclosed the drum in a sheet-iron casing; the model keeps its
   lower half, the hopper, and shows the screens. A hood (sheet iron, a door) can go on if wanted.
4. **Plain grinding plates.** Krupp's stepped, overlapping plates lift the balls one way only; drawn plain to
   keep the drum symmetric as #915 has it.
5. **The water's elbow** on the feed box's south side: one straight length and a soldered bend. The other way
   is a feed box the whole depth of the end, both anchors in its west wall.
6. **The east journal is solid** (the pulp leaves by the screens, not by it), under an end cover on its
   bearing; #915 says "hollow trunnions". Only the feed one needs to be hollow; the other can be drawn so.
7. **The build stages are settled** (above, 2026-10-15). Within the gearing the girth wheel goes on before
   the pinion, and within the sheet iron the hopper before the feed box; either order stands on what is
   already there, so these can be swapped.
8. **The lids** on the top layer make the drum walkable at two blocks. Without them a player on the mill
   stands among the drum's boxes.
9. **Nothing inside the drum is drawn:** neither the balls (the owner's ruling) nor the ore or pulp (no work
   input). No manhole is drawn for charging the balls.
10. **The girth wheel** is a little smaller than the heads (pitch r 10.8, tips 11.72, against 11.8) and turns outside the
    hopper; period spur wheels were about the drum's size.

Where the v2 branch (`733-grinder-model-v2`) disagrees with the issues, the issues were followed:
its drum (0.95 × 0.6 blocks) is now about 1.5 × 1.7; its overhead power shaft is gone (the axle drives the
pinion's shaft); its discharge lip into the arrastra's pit became the screens, the discharge hopper and the
output chute; its chute-head infeed from above became a chute into the feed box's side.

## Known weak spots, and what is not checked

1. **Box teeth.** Spur teeth and cogs are radial boxes.
2. **The hopper's floor is shallow**: half a block under the drum leaves it 2.2 voxels of fall to the chute.
3. **The screens' holes** are the game's mesh textures' alpha; the viewer draws flat colours, and whether the
   machine's renderer draws them cut out is unchecked until it exists.
4. **Not seen in a client:** there is no block or renderer, so lighting, the textures' scale and the machine at
   play distance are unchecked. It was reviewed in renders of the written files and the site's model viewer.
5. **The oak's grain** on the felloes is turned to run round the wheel, assuming the game's debarked oak runs
   its grain up the texture; unchecked in a client.

## Gameplay

What the rig offers #915: the four anchors and `output.pos` above; the parts' `requires` for what is fitted;
θ for the renderer (the pinion at the axle's angle, the drum at a third of it the other way). The draw
(resistance 1.1, 0.22 empty), the rate (6 rock an hour at the reference speed) and the water (25 L a rock)
are #915's and #940's, not the model's.
