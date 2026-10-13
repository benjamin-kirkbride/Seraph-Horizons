# Amalgamation plates

Part of the Seraph Horizons mod (`../README.md`): the amalgamation stage of ore processing (epic #684), as a
machine. A **plate table** of the 1890s stamp mill, as its own piece of equipment: an inclined plank bed on
oak trestles, three copper plates laid down it and dressed with mercury, a **distributor** at the head that
spreads the crushed ore across their width, and a **spray pipe** across the head. The pulp runs down the
plates in a thin sheet; the free gold and silver that touch them are held as **amalgam**, which builds up on
the plates and is scraped off by hand, and the rest falls off the foot into a **tail box** and leaves by
chute for the concentrators. **Level 2** fits a **mercury trap** in the tail box: a cast-iron well with a
hanging baffle the pulp passes under and a weir it passes over, catching the amalgam and mercury the stream
scours off the plates.

This folder holds the model's generator and validator (`tools/`). The gameplay is #917 and is not built yet;
the model is #918. "Gameplay" below says what the rig offers it.

**No power.** The plates take water and no power (#917: "Water, no power"), and nothing in them moves, so
the rig has no `powerCell`, no `powerFace` and no driver: every part stands still, and is fitted or drawn by
its `requires`. The modeling skill's rule "power like every other machine" does not apply: there is no
mechanism to drive (`Open for the owner`, 1).

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` and `Pipes/`, this folder's neighbours. The generic half of the
generator is `Machines/tools/machinegen/`, unchanged by this machine; the spray pipe is built by
`Pipes/tools/solderedpipe.py`, as the rosser's drip is.

## The machine

**Footprint.** 1 wide × 1 high × 3 long (x, y, z), three cells in a column along z: `[0,0,0]` the head (the
controller: the distributor, the infeed and the spray pipe), `[0,0,1]` the middle of the plates, `[0,0,2]` the
foot (the tail box, the trap and the outlet). Each dimension is what has to fit (#918): 16 voxels across
take a plate 13.2 wide between the bed's side boards (a period plate was about 1.4 m); 1 high takes the bed
falling 1 in 10 over three plates with the distributor over its head and the tail box under its foot; 3 long
takes the distributor, three plates of 8.4 and the trap with the outlet. The bed falls from y 9.4 at the head
to 6.82 at the foot's lip.

**Orientation.** The controller is the front, the cell nearest the player who places it; the plates fall
away from them, south in the native frame, to the outlet at the far end, where the next machine of the line
continues (#939: the plates before the sluice).

**Anchors** (`assets/seraphhorizons/config/amalgamationplates-rig.json`, native frame, blocks):

| Key | Value | What |
|---|---|---|
| `feedCell`, `feedFace` | `[0,0,0]`, `up` | The infeed: vanilla's straight chute section, upright, in the head cell's top face, flush with it; a chute in the cell above meets it with no gap or step. It ends in the distributor, over its floor |
| `waterCell`, `waterFace` | `[0,0,0]`, `west` | The water: the spray pipe's open end on the head cell's west face, ppex's 6 × 6 section on the face's centre, so a ppex pipe there meets it end to end (the rosser's rule) |
| `outputCell`, `outputFace` | `[0,0,2]`, `south` | The outlet: vanilla's straight chute section along z in the foot cell's end face, flush with it, its end butting the tail box's south wall, whose sill pours into its bore |
| `feedSpout` | `[0.5, 0.7125, 0.8438]` | Where the pulp leaves the distributor through its holes onto the head of the plates |
| `spray` | `[0.5, 0.6188, 0.775]` | The spray's nozzles, over the head of the plates |
| `amalgam` | `[0.5, 0.5263, 1.5437]` | The middle of the plates, on the dressing: scraping sounds and particles |
| `trapWell` | `[0.5, 0.1062, 2.4406]` | The mercury in the trap's well (level 2's clean-up) |
| `levels` | | What each level fits, by `requires` |
| `amalgamStates` | | The three amalgam states, one drawn at a time |
| `pipes` | | The spray pipe's `requires` per metal |

No return-water anchor (#694, #711: the water is used up). No power cell.

**Fitted parts and states** (the rig's `requires`; `null` is the frame):

| `requires` | Part | Draws |
|---|---|---|
| null | `frame` | Two oak sills, the front posts, three trestles, the plank bed with its side boards and head board, the distributor with its cleats and cross bars, the tail box with its collar, and the spray pipe's rest |
| `chutes` | `chutes` | The two chute sections, infeed and outlet, in vanilla's copper chute texture |
| `plates` | `plates` | The three copper plates |
| `dressing` | `dressing` | The mercury rubbed into each plate, a thin coat |
| `amalgam1` .. `amalgam3` | `amalgam1` .. `amalgam3` | The amalgam on the plates, one state at a time: a film of patches at the head; the first plate's upper part covered and the second's head patched; two plates covered thick, ridged at the head, and the third's head patched |
| `trap` | `trap` | Level 2: the cast-iron well, its baffle and lugs, the mercury in its bottom and the drain plug |
| `pipecopper`, `pipelead` | `pipecopper`, `pipelead` | The spray pipe in copper or lead (the same elements, the supplied metal's drawn) |

**Levels** (the rig's `levels`): **1, the table and plates** (`plates`, `dressing`); **2, the mercury trap**
(`plates`, `dressing`, `trap`). A level only adds: the footprint, the chutes and the pipe never move.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. The timber is the game's debarked oak and the boards its oak planks; the plates are the
game's plain copper sheet (`game:block/metal/sheet-plain/copper1`), the trap its iron plate; the chute
sections are vanilla's own chute texture (`game:block/metal/sheet/copper1`); the spray pipe is UnifiedPipes'
copper or lead pipe with a soldered cap. The mercury (`seraphhorizons:block/liquid/mercury`) and the amalgam
(the game's lead solder ingot, `game:block/metal/ingot/leadsolder`) are the amalgam pan's, so the two
machines' amalgam reads the same (a check). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/amalgamationplates.json` | The whole machine, every fitted part, every amalgam state and both pipe metals (115 elements) |
| `assets/seraphhorizons/shapes/block/amalgamationplates_frame.json` | The frame only (53 elements). The block draws it and the inventory shows it |
| `assets/seraphhorizons/config/amalgamationplates-rig.json` | The cells, the anchors and the part rig (10 parts) |

There are no reference poses: no part has a driver, so every part's matrix is the identity (the eidolon
gantry's precedent).

### How the machine works

Positions here are in the generator's build frame: the native axes (x west to east, y up, z north to south) in
voxels, from the machine box's north-west bottom corner, which is the controller's; the shipped files are the
build frame divided by 16.

**The frame.** Two oak sills on the ground (x 0.4..2.4 and 13.6..15.6, z 0.4..35.8), two front posts on them
at the head (to y 15.4), and three trestles across (z 12.4, 23.4, 33.8): two legs on the sills and a bearer
just under the bed.

**The bed** is a plank floor 0.6 thick falling 1 in 10 (5.71°) from z 11.8 (its top at y 9.4) to the foot's
lip at z 37.6 (y 6.82), between side boards 0.8 thick standing 1.6 over the floor, low enough to scrape the
plates over; a head board across the upper end stands 0.3 over the floor. Each is in two lengths, so no
texture stretches. **The plates** (`plates`) are three copper plates 13.2 wide and 8.4 long, a tenth apart,
flat on the floor from z 12.0 to 37.4; **the dressing** (`dressing`) is a mercury coat 0.06 thick a quarter
inside each plate's edges.

**The distributor** is a plank box across the head (x 0.6..15.4, z 2.0..15.6, y 11.4..15.4), its north end on
the front posts and its south end on two oak cleats on the side boards. The infeed chute section comes down
into it from the top face (to y 13.0), held between two oak cross bars on its walls' tops; the pulp falls onto
its floor and leaves through a row of seven holes along the floor's lower edge (z 12.8..14.2), over the head
of the first plate.

**The spray pipe** (`pipecopper` or `pipelead`) is one length of ppex's straight pipe drawn soldered, from the
west face straight across the head under the distributor (y 5..11, z 5..11) to a soldered cap at x 14.4,
resting on an oak block at its east end; five nozzles on its south face (y 9.9..10.9, to z 12.4) play over
the head board onto the head of the first plate.

**The tail box** is a plank box on the ground past the sills' ends, the whole width of the bed (x 0.2..15.8,
z 36.0..43.4), its walls (to y 6.1) under the bed's foot, which overhangs it: the pulp falls off the lip into
it and leaves over a sill (y 5.5) in its south wall, straight into the outlet chute's bore (y 5..11). Over
the sill the south wall rises in a collar round the chute's end (to y 12.6), which butts against it all round.

**The mercury trap** (`trap`, level 2) is a cast-iron well standing on the tail box's floor under the lip
(x 0.9..15.1, z 36.9..41.2, walls to y 3.4), the whole width of the lip inside it, with mercury 0.5 deep in
its bottom; an iron baffle hung across it from two lugs on the tail box's walls' tops, its foot at y 2.2 (over
the mercury, under the weir), and a drain plug out through the box's east wall. The pulp falls into the well
upstream of the baffle, passes under it over the mercury, rises over the well's south wall (its weir) and
leaves over the box's sill as before; the amalgam and mercury it scoured off the plates stay in the well.

**The amalgam** builds up where most of the gold is caught, at the head: `amalgam1` is five patches over the
first plate's upper part (0.08..0.12 thick); `amalgam2` covers the first plate's upper four fifths (0.16) and
patches its foot and the second plate's head; `amalgam3` covers the first plate (0.32) with three ridges
across its head and the second plate's upper part (0.2), and patches the third's head. It starts on the first
plate past the nozzles' tips.

Every part, and what moves it:

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Frame (`frame`) | Still | — | none |
| Chute sections (`chutes`) [chutes] | Still | — | none |
| Plates (`plates`) [plates] | Still | — | none |
| Dressing (`dressing`) [dressing] | Still | — | none |
| Amalgam, three states (`amalgam1` .. `amalgam3`) [same] | Still: drawn by gameplay, one at a time | — | none |
| Mercury trap (`trap`) [trap] | Still | — | none |
| Spray pipe (`pipecopper`, `pipelead`) [same] | Still: the supplied metal's drawn | — | none |

There are no gears, shafts or levers: the water and the pulp do all the work.

### Regenerating

```sh
python3 mods-src/seraphhorizons/AmalgamationPlates/tools/make_shape.py              # stdlib only; rewrites the three files
python3 mods-src/seraphhorizons/AmalgamationPlates/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/AmalgamationPlates/tools/make_shape.py --quick      # skips the z-fighting fix and check (not for files that ship)
```

A full run takes about a second and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_amalgamationplates.py` holds the checks; every run checks its output and exits
non-zero if one fails. A state (`STATES` in the generator) is a set of fitted parts: the frame alone, with the
chutes and pipe, bare plates, level 1, level 1 with thick amalgam, level 2, and level 2 with each amalgam
state. The checks run over every state:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part.
- **Rig:** no driver, ride, power cell or work; the `requires` vocabulary; the levels only add (level 2 is
  level 1 and the trap); the pipe metals are the same elements; no state dresses plates it has not fitted,
  puts amalgam on undressed plates or draws two amalgam states or two pipe metals.
- **Textures** by role, and the amalgam and mercury the amalgam pan's, the chute sections vanilla's chute's.
- **Thickness:** nothing thinner than 0.6 across but the plates, coats, amalgam, mercury and pipe walls.
- **Containment:** nothing leaves the 1 × 1 × 3 box.
- **Nothing floats:** in every state, everything shown is joined to the ground.
- **Anchors:** the spray pipe meets a ppex pipe end to end on the water face's centre in ppex's 6 × 6 tube;
  the infeed is vanilla's upright chute section at the feed cell's top face (its walls' section, place and
  length), its foot inside the distributor over its floor; the outlet is vanilla's chute section along z at
  the output cell's end face, the sill inside its bore and its end butting the wall all round; `VANILLA_CHUTE`
  is the game's `shapes/block/metal/chute/straight.json` where the game is installed (`$VINTAGE_STORY` or
  `~/Games/vintagestory`).
- **The bed:** it falls 1 in 10; the plates lie flat on its floor between the side boards, in order, from
  past the head board to short of the lip; the side boards stand a voxel or more over the plates; each
  trestle's bearer is just under the bed.
- **Flows** (ray drops, 146 over the states): the infeed onto the distributor's floor; each hole onto the
  head of the plates (the amalgam, the dressing, the bare plates or the bed, by state); each nozzle's jet onto
  the plates past the head board; the lip, at both edges and the middle of the plates, into the tail box, or
  into the trap's mercury at level 2; the sill into the outlet.
- **The trap:** the well on the box's floor inside its walls, under the lip; the mercury under the baffle's
  foot, the foot under the weir's top, the weir under the sill, the baffle's top over the sill (the pulp must
  pass under and over); the baffle wall to wall, on its lugs on the box's walls; the plug through the east
  wall.
- **The amalgam:** each state on the dressing within its plate's coat (ridges on their sheet); each state
  covers more, reaches further down and stands thicker than the last; the side boards stand at least half a
  voxel over it everywhere; it starts past the nozzles' tips.
- **Clearances:** in every state, no two parts touch but the plug in the box's wall (`ALLOWED`).
- **No z-fighting** (full runs): no coplanar overlapping faces in any state; the fix only removes the faces
  pressed against their own part.
- **Files:** every texture declared; three cells, none hollow, a lid over every column; no driver or power.

The cells' boxes are rebuilt from the shipped, rounded shape at rest, every part included (they lie within
the frame's outline), as the other machines' are; `tools/tests/test_amalgamationplates_model.py` holds the
rig file to them.

### Rig schema (`amalgamationplates-rig.json`)

The amalgam pan's schema (`../AmalgamPan/README.md`) with no input at all: nothing is added to the shared rig
maths.

- **Inputs.** None: no part has a driver. Every part's matrix is the identity.
- **Keys.** `cells` (three, each with its boxes and a `lid`), `feedCell`/`feedFace`, `waterCell`/`waterFace`,
  `outputCell`/`outputFace`, the points `feedSpout`, `spray`, `amalgam`, `trapWell`, `levels`,
  `amalgamStates`, `pipes` and `parts`. `requires` values: `chutes`, `plates`, `dressing`, `amalgam1`,
  `amalgam2`, `amalgam3`, `trap`, `pipecopper`, `pipelead`, or null.

**In the viewer** (`site/models.json`, `amalgamation-plates`): no clock or Play (nothing moves). A **Level**
select (the table alone, bare plates, level 1, level 2, opening on level 2), an **Amalgam on the plates**
select (clean, a film, building up, thick; opening on building up) that waits for dressed plates, the
**spray pipe**'s metal as a choice, and the chute sections as a checkbox. Coloured by texture, the mercury and
the amalgam have colours of their own (the amalgam pan's texture families).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `am1_*`, `am2_*`, `am3_*` (the
amalgam states), `pd_coat1`..`3`, `pl_plate1`..`3`, `tr_*` (`floor`, `wall_n`, `wall_w`, `wall_e`, `weir`,
`baffle`, `lug_w`, `lug_e`, `plug`, `mercury`), `ch_in_*` and `ch_out_*`, `pipecopper_*` and `pipelead_*`
(`spray_*`, `cap`, `nozzle1`..`5`), and `fr_*` for the frame (`sill_*`, `post_*`, `bearer*`, `leg*`,
`bed_floor*`, `bed_side_*`, `bed_head`, `dist_*`, `tb_*`, `pipe_rest`). Hand edits are lost when the script
runs again: port them into `make_shape.py`, or stop regenerating.

## Salvage (#918)

From `735-concentrator-model-v2` (copied, not merged; its evaluation is
`scratchpad/evals/735-concentrator.md`):

- **Kept and adapted:** `build_plate_table` (the bed, its side boards and head board, now 26 long on its own
  trestles, falling 1 in 10 where the wash house's fell 1 in 8), `build_plates` (three copper plates and the
  mercury dressing; the plates now 8.4 long and 13.2 wide), the `sloped` helper (now along z only), the box
  helpers, `VANILLA_CHUTE` with its profile check and `read_vanilla_chute`, the soldered pipe pattern
  (`pipe_boxes`, `build_pipes`, `pipe_copies`), and the validator's ray-drop flows and texture roles.
- **Changed, as #918 asks:** its own frame (sills, posts, trestles); a head distributor and the infeed chute;
  the spray pipe at the head; the west spout became the outlet chute in the end face, out of a tail box; the
  iron riffle across the bed became a proper trap, a box (`trap`), and is level 2; the amalgam in three
  states over the plates.
- **Dropped:** the wash house (timbers, launders, tail box, concentrate launder), its line shaft and levels,
  and its plates' place in the house (the plates were optional parts after the jig; they are now their own
  machine, before the concentrators).
- **Not ported:** commit `9fa58a5` (per-cell `fitted` collision in the site): the game does not read it, and
  the trap barely changes the foot's boxes.

## Provisional

- **The anchors** (#711 has not landed): the chute sections follow `VANILLA_CHUTE` as the v2 branches did, the
  water face the rosser's rule. The feed and the water both use the controller cell (two faces of it), so the
  controller itself would take the chute and the pipe; if #711's shared code wants a ghost per service, the
  water would need its own cell, which 1 × 1 × 3 does not have at the head (`Open for the owner`, 2).
- **The mercury texture** is the amalgam pan's provisional one.
- **No client run:** reviewed in projections rendered from the written files and in the site's viewer only.

## Open for the owner

1. **No power.** #917 and #940 say the plates take no power; the modeling skill says "power like every other
   machine". The model has no axle, `powerCell` or `powerFace`, as the spec says.
2. **The water face is the controller's.** A 1-high cell's side faces all centre at y 8, and the bed's head
   must be lower than the spray, so the only face the spray pipe can come in at is the head cell's: west
   (the front, north, would put the pipe where the player stands). The feed is the same cell's top face.
3. **The trap's form.** A cast-iron well with one hanging baffle, standing in the frame's tail box (the
   box is level 1's: the pulp must reach the outlet without the trap). #917 says "traps"; this is one. Its
   mercury is part of `trap`; the amalgam it catches is not drawn.
4. **The dressing as its own `requires`.** It lets a stage (mercury rubbed into the plates) or a state
   (undressed plates) be drawn; drop it into `plates` if the plates always come dressed.
5. **The amalgam states**: exclusive parts, the amalgam pan's lead-solder paste, growing from the head down
   (patches, then sheets, then ridges); whether they should instead grow evenly, and the thicknesses
   (0.12, 0.16, 0.52 at most).
6. **The plates' width**: 13.2 voxels between the side boards, not the full 16; a plate 16 wide would need
   the bed to fill the footprint with no side boards, or the bed 2 wide (#918's alternative).
7. **The outlet in the end face**, not the bottom face: the plates stand on the ground and the next machine
   sits a block lower, its infeed in its top face. A bottom-face outlet would need the plates raised.
8. **Head at the front.** The controller is the head (the distributor), and the plates fall away from the
   player; the other way round would put the outlet where the player stands.
9. **The spray pipe** is ppex's own 6 × 6 pipe across the head, under the distributor, with five stub
   nozzles; a period spray pipe was a small perforated pipe. A smaller pipe would need a reducer ppex does not
   have.
10. **The distributor**: a box with a row of holes in its floor, across the whole width, resting on cleats
    on the side boards at its south end; a splash plate or a box with notched lips are the alternatives.
11. **The lids**: every column lidded at the top of the block (the head cell's chute reaches it), so a player
    walks over the plates at y 1, not on them; the rosser leaves its beds unlidded.
12. **Build stages** (#918): not designed. The `requires` split where a stage could: the chute sections, the
    plates, the dressing, the trap, the pipe.

## Gameplay

Not built: #917. What the rig offers it:

- **Parts.** No clock and no renderer maths: every part is still. The frame shape is the block's; gameplay
  draws each fitted part (`chutes`, `plates`, `dressing`, `trap`, the supplied pipe metal) and one amalgam
  state (none when just scraped) by its `requires`, by whatever means it chooses (a renderer with the shared
  rig maths, whose matrices are all the identity, or shape overlays).
- **Anchors.** The infeed at `feedCell`/`feedFace`, the water at `waterCell`/`waterFace` (the reservoir's
  pipe), the outlet at `outputCell`/`outputFace` (where the tailings leave, or are dumped as sand beside it
  when no chute takes them, #694); `amalgam` for scraping, `trapWell` for the trap's clean-up.
- **Tests.** `tools/tests/test_amalgamationplates_model.py` and `site/test/amalgamationplates.test.ts` hold
  the generated files to their rules and the site's maths. No C# test: with no driver there are no poses to
  replay.
