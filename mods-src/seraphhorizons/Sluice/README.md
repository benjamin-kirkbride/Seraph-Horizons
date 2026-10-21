# Sluice: the model

Part of the Seraph Horizons mod (`../README.md`): the generated model of the **sluice**, the first concentrator
of the ore line (epic #684; behaviour #734, model #735). A riffled plank sluice: "10 to 15 feet of sluice boxes
fitted with transverse riffles" [Louis 1902, p. 293], which a stamp mill ran its pulp through after the plates,
carrying it in a stream of water; the heavy grains settle behind the riffles. Here the riffles' catch is **drawn
off continuously** and leaves by its own chute: nothing is cleaned up by hand (the owner's
ruling on #734). The line, the glossary and the period sources are #939; the power and water budget #940.

**Status: built.** This folder's `tools/` holds the model's generator and its checks; `Game/` the gameplay (#734,
"Gameplay" below), on the ore machines' shared code (`Machines/`).

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` or `Pipes/`, this folder's neighbours. The generic half of the
generator is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one: **no
driver at all**, and no change to the package. The water inlet is built by `Pipes/tools/solderedpipe.py`, as the
rosser's drip is.

## The sluice

**It takes no power and nothing moves.** There is no axle, no `powerCell` and no driver: every part is a fitted
part, or the concentrate drawn while the sluice runs. Its services are water (a ppex pipe) and chutes (vanilla's
copper chute: one in, two out).

**Footprint.** 1 wide (x) × 1 high (y) × 4 long (z): cells `[0,0,0]` to `[0,0,3]`. The controller is the
**head cell**, `[0,0,0]`, nearest the player; the sluice runs away from them, south (+z), and falls that way.
Each dimension is what has to fit: one block high, because the infeed chute comes down into the head from above,
the water pipe comes in under the head box at its face's centre (y 5..11), and the sluice's floor runs from the
head box's sill (y 13) at 1 in 12 to its lip (y 9.08) in the tailings chute's mouth (bore y 5..11), which leaves
room under the riffles for the concentrate launder and, under its end, for the upright concentrate chute (y 0..7);
one wide, because the channel is the chute's bore; four long (the owner's review, from three), so that the two
products leave by **different cells and faces**: the head box (cell 0), the riffles over cells 1 and 2 with the
concentrate's way out under them and down out of cell 2's bottom face, and cell 3 a plain run-off to the tailings
chute in its south face.

**The flow.** Ore (crushed or ground rock, or placer gravel) comes down a vanilla chute into the head cell's top
face and falls into the **head box**, where the water comes up through the box's floor from a nozzle; the pulp
overflows the box's **sill** onto the sluice and runs down over the seven **riffles**. Two streams leave:

- **The concentrate.** The heavy grains settle behind the riffles and drop, as they settle, through a **draw
  slot** in the floor right behind each riffle into the **concentrate launder** slung under the floor, which falls
  with the sluice and, just past the last riffle, pours off its open end **straight down** into the **concentrate
  chute**, a vanilla chute section standing upright in cell 2 from its **bottom** face: the concentrate leaves into
  a chute (or a container) in the block below.
- **The tailings** run on over cell 3's plain floor and off the tail's lip into the **tailings chute**, flush with
  the tail cell's **south** face.

The two ways are visibly apart: the concentrate goes through the floor and down, under the riffles; the tailings
go over the floor and out of the far end, a block further on. A chute line to the jig (#919, on the tailings) runs
on south from the tail; the concentrate's chute goes down from the middle, under the sluice, clear of it.

**Anchors** (in `assets/seraphhorizons/config/sluice-rig.json`; all provisional pending #711, the shared chute and
water code):

| Key | Value | What it is |
|---|---|---|
| `infeedCell`, `infeedFace` | `[0,0,0]`, `up` | A vanilla chute from above meets the infeed section, centred in the face and flush with it |
| `waterCell`, `waterFace` | `[0,0,0]`, `north` | A ppex pipe meets the water inlet, end to end, on the face's centre (ppex's pipe is its block's 5..11) |
| `tailingsCell`, `tailingsFace` | `[0,0,3]`, `south` | The tailings chute leaves the tail: chuted on, the tailings go to the next machine (the jig); else they are discarded beyond this face (#694: gravel, a layer a rock) |
| `concentrateCell`, `concentrateFace` | `[0,0,2]`, `down` | The concentrate chute leaves through the bottom face, continuously, while the sluice runs: a vanilla chute (or a container) in the block below takes it |
| `feedSpout.pos` | `[0.5, 0.906, 0.5]` | Where the ore falls into the head box (the infeed chute's mouth): while it washes, grit of the ore's colour falling |
| `waterSpout.pos` | `[0.5, 0.806, 0.4375]` | The nozzle's mouth, in the pool: while it washes, a splash of clear water |
| `concentrateSpout.pos` | `[0.5, 0.512, 2.4615]` | Where the launder pours down into the concentrate chute: sound, and while it washes the pulp pouring, the ore's colour |

There is no `powerCell`, `powerFace`, `infeedSide`, `outputSide` or work, no return-water anchor (the water is used
up, #694), and no hand clean-up point.

**Fitted parts** (the rig's `requires`). Everything with `requires` null is the frame (`sluice_frame.json`): the
head box with its legs and bearers, the sluice box and its tail board, the concentrate launder, and the three
trestles.

| `requires` | Part | Draws |
|---|---|---|
| `riffles` | `riffles` | The seven riffles: oak bars across the channel, each faced with strap iron |
| `infeedchute` | `infeedchute` | Vanilla's straight chute section, upright, from the head cell's top face into the head box |
| `tailingschute` | `tailingschute` | Vanilla's straight chute section, from the tail into the tail cell's south face |
| `concentratechute` | `concentratechute` | Vanilla's straight chute section, upright, from cell 2's bottom face up to its open top under the launder's end |
| `pipecopper`, `pipelead` | `pipecopper`, `pipelead` | The water inlet, its riser and nozzle, soldered, in copper or in lead: the same elements, one drawn |
| `concentrate` | `concentrate` | The concentrate while the sluice runs: a bed behind each riffle over its slot, a sheet falling through each slot, a layer running down the launder and a stream falling off its end down the concentrate chute |

## Build stages

Measured from the model (#734's gameplay, pending the owner's word: `Open for the owner`), as the jig's were: boards in
planks at 128 vox, timbers in support beams at 256, iron 4 nails and strips an ingot of 128 vox, plus 1 iron nails and
strips per 4 planks' worth of timber (a support beam 2), the chute and pipe sections counted. The frame is all the
timber, so there is no timber stage: its grid recipe holds it within the grid limit. Then each stage is fitted in order
by right-click (`config/builds/sluice.json`, the rig's `buildStages`), and the sluice runs only when the last is in.

| # | Stage | `requires` | Sits on | Materials |
|---|---|---|---|---|
| F | The frame: the head box, the sluice box and tail board, the concentrate launder, the legs, bearers and trestles | none (placed) | The ground | 8 plank (998 vox of boards), 2 support beam (431 vox of timbers), 3 iron nails and strips, a hammer |
| 1 | The riffles | `riffles` | The sluice box's floor | 1 support beam (the seven oak bars, 47 vox), 1 iron nails and strips (their straps, 11 vox) |
| 2 | Services | `infeedchute`, `tailingschute`, `concentratechute`, `pipecopper` or `pipelead` | The head box's mouth; the tail; between the outlet trestle's legs; up through the head box's floor | 3 straight chute, 1 pipe section (copper or lead), 1 iron nails and strips |

The water inlet is a choice of metal (one part each, the same elements). The concentrate (`concentrate`) is what the
sluice draws while it works, in no stage; nothing in it wears out.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. The boards are the game's oak planks (`game:block/wood/planks/oak1`, the floors' boards turned to run
along the trough), the timbers its debarked oak, the riffles' straps its iron plate, the three chute sections
vanilla's chute in copper (`game:block/metal/sheet/copper1`, as `shapes/block/metal/chute/straight.json` wears it),
the water inlet UnifiedPipes' copper or lead pipe (`game:block/metal/sheet-plain/{copper,lead}4`, as the rosser's),
and the concentrate the game's basalt sand (`game:block/stone/sand/basalt`), a dark heavy sand, under its own texture
code `concentrate` so a renderer can set it to the ore's look. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/sluice.json` | The whole sluice: the frame, every fitted part, both pipe metals and the concentrate (138 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/sluice_frame.json` | The frame only (50 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/sluice-rig.json` | The four cells, the anchors and the part rig (8 parts, none with a driver). |

There are **no reference poses** (`tests/Sluice/rig-reference.json`): no part has a driver or rides another, so
every part's matrix is the identity at any input, which the generator's checks, `site/test/sluice.test.ts` and
`tools/tests/test_sluice_model.py` check instead.

### How it is built

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north
to south) in voxels, measured from the head cell's north-west bottom corner; the build frame's corner is the
controller's, so the shipped files are the build frame divided by 16. The machine's centre line is x 8: the
channel's, the infeed's, the tailings chute's and the water inlet's.

**The head box** is a plank box 10 × 10 outside (x 3..13, z 3..13), walls 1 thick, its floor's underside at y 11
and its walls' top at y 15.5: inside it is 8 × 8, the infeed chute's outside, and the chute comes down into it from
the top face to y 14.5, held by the walls round it for its last voxel. Its floor's top is at **y 12**, a voxel under
the sill: the pool the ore falls into and the water stirs. Its south wall stands full height beside the channel and
stops at the **sill** across it (y 13, a hair wider than the channel, so the sluice's sides, cut square to the slope,
sink into the wall beside it unseen). It stands on two oak **bearers** along its west and east walls (y 9.6..11),
each mortised into two of the four **corner legs** (1.6 square), which rise beside the walls to y 14.5; the space
between the bearers, under the floor, is the water pipe's.

**The water inlet** is UnifiedPipes' soldered pipe in the rig's copper or lead (`Pipes/tools/solderedpipe.py`:
ppex's 6 × 6 tube round a 4 × 4 bore): a straight length on the water face's centre (x 8, y 8) from the face (z 0)
south under the head box, its top on the floor's underside, to a **bend** (a hub, as UnifiedPipes' soldered bend
draws one) at z 7 that turns it up; the **riser** comes up through the box's floor (x 5..11, z 4..10), and on its end,
on the floor, a **reducer** plate and the **nozzle** in two steps from its middle (3 × 3, then 2 × 2), pointing up,
its mouth at y 12.9, in the pool under the sill, where the ore lands. No joint at the face: the pipe beyond brings
its own, as on the rosser.

**The sluice box** is a plank trough from the head box's south wall (z 13) to the tailings chute's mouth (z 60):
a floor 1 thick between two sides 0.9 thick, standing 2.5 over the floor. The **channel** is 6.1 wide
(x 4.95..11.05), the chute's 6 bore and a hair: the sides (x 4.05..4.95, 11.05..11.95) run into the tailings
chute's walls (x 4..5, 11..12) at the tail. The floor falls **1 in 12**, from y 13 at the sill to **y 10.42** at the
lip, which stands in the tailings chute's mouth, under its bore's top (y 11). The floor is laid in lengths between
the seven draw slots, and cut at the sides' joints past them (ten in all); the sides in four boards' lengths, butted
at z 27.5, 43.1 and 51.5 over the sluice's **three trestles**, each a beam under what it carries (the launder at the
first, the floor at the others) mortised into two legs. The first and third trestles' legs rise beside the sides to
half a voxel under their top; the second is the **outlet trestle** (below). At the tail a plank **tail board**
(x 4..12, z 59..60) stands on the ground against the tailings chute's mouth, carrying the tail and closing the mouth
under the lip.
Every sloped piece is cut square to the slope: at the head its top corner meets the head box's wall (the rest runs
into the wall), at the tail its bottom corner meets the chute's mouth (the rest runs 0.08 to 0.29 into the chute's
walls), so neither end shows a gap.

**The riffles.** Seven, every 3.5 voxels along the floor's top line (upstream faces at z 16, 19.5, .., 37), over
cells 1 and 2, leaving 3 of plain floor at the head for the pulp to spread on, and 22 of plain floor past the last,
over the concentrate chute's cell and the whole tail cell, down which the tailings run off. Each is an oak bar 1
along the flow and 1.1 high, standing on the floor square to the slope from side to side, faced on top with a strap
of iron 0.2 thick that overhangs its downstream face by 0.3 (a Hungarian riffle's lip: the wearing surface, and the
eddy behind it).

**The draw slots.** Right behind every riffle, a slot 1 long (along the slope) runs through the floor across the
channel, under the strap's lip: in the lee where the heavy grains settle.

**The concentrate launder** is a closed plank trough slung under the sluice's floor at its grade (1 in 12): its
boards 0.8 thick, its sides under the sluice's sides (x 4.15..4.95, 11.05..11.85) with their tops on the floor's
underside, its inside the channel's width and 1.6 deep, so each slot drops straight into it. It is closed at its
head (z 15.8, upstream of the first slot) and open at its end (its lip y 8.19 at z 39.38), just past the last
slot, over the concentrate chute's open top, in two lengths.

**The concentrate outlet.** The **concentrate chute** stands upright in cell 2 (x 4..12, z 36..44, its bore x 5..11,
z 37..43), from the cell's **bottom face** (y 0) up to its open top at y 7, 0.4 under the launder's underside. The
launder's lip lies over the bore's north half, so what comes off it falls down the bore and out of the bottom face;
nothing of the machine lies in the bore's column below the launder (a check drops 49 grains over the bore and every
one leaves by the bottom face). The chute is held between the legs of the **outlet trestle** (z 43.1), which stand
against its east and west walls (x 2.4..4 and 12..13.6) along their whole height and stop a tenth under their beam,
which carries the sluice's floor (y 8.0..9.4, over the chute's south wall, clear of the bore where the concentrate
falls).

**The chutes.** All three sections are vanilla's straight chute exactly (`VANILLA_CHUTE`, salvaged from the
concentrator's v2 generator: an 8 × 8 tube 1 thick, centred in the block, a 6 × 6 bore, two full-width walls and
two between them), checked against the game's `shapes/block/metal/chute/straight.json` wherever the game is
installed: the **infeed** upright (its "ud" variant) from y 16, flush with the head cell's top face, down to y 14.5;
the **tailings chute** north-south (its "ns" variant) from z 60 to z 64, flush with the tail cell's south face; the
**concentrate chute** upright too, from y 0, flush with cell 2's bottom face, to y 7, under the launder's end.

**The concentrate** (drawn while the sluice runs): in each pocket behind the seven riffles, a bed over its slot, a
wedge in two steps deepest against the riffle, deeper at the head (0.75 to 0.45 of the riffle's height under the
strap), the deeper step always over the whole slot; under each slot a sheet falling through it onto the launder's
floor; a layer running down the launder's floor to its end; and a stream (4 across) falling off the lip down the
concentrate chute's bore and out of the bottom face. Each runs a hair into what it lies on and against, so no gap shows.

Every part (nothing moves; θ and every other input change nothing):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Riffles (`riffles`) [riffles] | Static | — | none |
| Concentrate (`concentrate`) [concentrate] | Static: drawn while the sluice runs | — | none |
| Infeed chute section (`infeedchute`) [infeedchute] | Static | — | none |
| Tailings chute section (`tailingschute`) [tailingschute] | Static | — | none |
| Concentrate chute section (`concentratechute`) [concentratechute] | Static | — | none |
| Water inlet (`pipecopper`, `pipelead`) [the same] | Static: the supplied metal's drawn | — | none |
| Frame (`frame`): head box, sluice box, tail board, launder, legs, bearers, trestles | Static | — | none |

There are no gears, shafts or bearings, so no toothed wheel meshes with nothing and no shaft wants carrying.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Sluice/tools/make_shape.py              # stdlib only; rewrites the three files
python3 mods-src/seraphhorizons/Sluice/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Sluice/tools/make_shape.py --quick      # skips the z-fighting fix and its check (not for files that ship)
```

A full run takes about 2 s and is deterministic (two runs into two folders are byte-identical, logs included).
`tools/validate_sluice.py` holds the checks; every run checks its output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Nothing moves:** no part has a driver or a ride, every part's matrix is the identity at any input, and the rig
  has no work and no power cell.
- **Nothing floats:** the frame is one piece from the ground; each fitted part and the concentrate is held by the
  frame (the concentrate by the frame or a riffle), through its own elements.
- **Textures** by role: planks for the head box's, the sluice's, the tail board's and the launder's boards, oak for
  the legs, bearers, trestles and the riffles' bars, iron for the straps, `chute` for the three sections, each pipe metal its
  own, `concentrate` for the concentrate; and no element longer than a texture (16), so none is stretched.
- **Containment:** nothing leaves the four cells.
- **Chutes:** all three sections vanilla's walls exactly, flush with their faces (the infeed's top at y 16, the
  tailings chute's end at z 64, the concentrate chute's foot at y 0), in the chute's texture; `VANILLA_CHUTE` is the game's
  file, where the game is here; the infeed, water, tailings and concentrate faces all differ.
- **Water:** the inlet meets a ppex pipe end to end on the water face's centre, ppex's 6 × 6 tube with its 4 × 4
  bore; it runs under the head box (its top on the floor's underside), between the bearers, and its riser comes up
  through the floor; the reducer stands on the floor and the nozzle's mouth is in the pool under the sill;
  `waterSpout` is the nozzle's mouth.
- **The flow** *(the pulp's way through)*: ore dropped down the infeed chute's bore (nine points) lands on the head
  box's floor or the nozzle; the box's floor is under its sill and the sill is the sluice's head floor across the
  channel; the channel is the chute's bore or a hair wider, over it; the floor falls at exactly 1 in 12 from the sill's
  edge; a grain dropped every quarter voxel down the channel's centre line, with the concentrate drawn and not, lands
  on the floor, a riffle, the concentrate or, through a slot, the launder; the lip stands in the tailings chute's
  mouth, over its bore floor and under its top, the sluice's pieces reach the mouth, and a grain off the lip lands on
  the tailings chute's floor.
- **Riffles:** seven, evenly spaced, each bar on the floor from side to side and square to the slope, its strap on
  its top over its downstream face; 3 or more of plain floor at the head, the last short of the lip.
- **The concentrate's way out** *(the owner's rulings: by chute, continuously; out of the bottom, apart from the
  tailings)*: a slot right behind every riffle, through which a grain lands on the launder's floor; the launder falls
  at 1 in 12, closed upstream of the first slot and open past the last; a grain off its lip is over the concentrate
  chute's bore, above its top, and falls out of the bottom face with nothing in its way; a grid of 49 grains over the
  bore all leave by the bottom face (the outlet is open to a chute below); the chute stands from y 0 up under the
  launder, clear of it; the concentrate face is `down`, in another cell and face than the tailings', and nothing of
  the concentrate's way (riffles, launder, chute, concentrate) reaches into the tail cell; the beds lie in their
  pockets over their slots, on the floor, just into the sides, under the straps; a sheet through each slot, the layer
  in the launder and the stream down the chute's bore to the bottom face.
- **Supports:** each beam and bearer under what it carries (within 0.02), each leg on the ground beside the box it
  carries, the outlet trestle's legs against the concentrate chute's walls along its height, stopping 0.1 under their
  beam, which lies clear of where the concentrate falls; the tail board on the ground under the tail, against the
  tailings chute's mouth; the sluice sides' joints over the trestles' beams.
- **Clearances:** with the concentrate drawn and not, either metal, no two parts touch except the intended contacts
  listed in `ALLOWED` (the riffles and the concentrate on the floor and against the sides, the concentrate in the
  launder and against the riffles, the infeed in the head box's mouth, the sluice's tail into the tailings chute,
  the concentrate chute between the outlet trestle's legs, the riser up through the head box's floor and the reducer
  on it).
- **No z-fighting** (full runs): no coplanar overlapping faces, with the concentrate drawn and not. The model is built
  with none before the fix (the beds' two steps stagger their hidden faces, the sill's notch is a hair wider than the
  channel, the launder's concentrate is butted away from the launder's own joint); the fix only removes the faces
  pressed against their own part (86).
- **Files:** every texture declared; four cells, each with boxes and none hollow, no lid (the channel is narrower
  than a player, and the boxes are the deck); no power cell, work, sides or `fill`, and no key naming a hand clean-up;
  the point anchors inside the footprint; the shipped model is the checked one (no move).

The cells' boxes are rebuilt from the shipped, rounded shape at rest, every part shown, as the other machines' are.

### Rig schema (`sluice-rig.json`)

The amalgam pan's schema (`../AmalgamPan/README.md`, "Rig schema") without even its θ: nothing is added to the
shared rig maths.

- **Inputs.** None. Every part's `drivers` is empty and its `ride` null.
- **Keys.** `cells` (four, each with its boxes), `infeedCell`/`infeedFace`, `waterCell`/`waterFace`,
  `tailingsCell`/`tailingsFace`, `concentrateCell`/`concentrateFace`, the points `feedSpout`, `waterSpout`,
  `concentrateSpout`, `buildStages` (each stage's number, name and `adds`, the generator's `BUILD_STAGES`), and `parts`. `requires` values: `riffles`, `infeedchute`, `tailingschute`, `concentratechute`,
  `pipecopper`, `pipelead`, `concentrate`, or null. The texture code a renderer may set: `concentrate` (the ore's look).

**In the viewer** (`site/models.json`, `sluice`): the riffles and the three chute sections are checkboxes, the
inlet's metal a choice (copper, lead), and the concentrate a select, "The sluice": Stopped (none drawn) or Running
(the concentrate drawn off), opening on Running, which waits on the riffles: untick them and the concentrate is not
drawn. There is no Play: nothing turns.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `riffle_<i>_bar`, `riffle_<i>_strap`;
`conc_pocket<i>a`/`b` (a bed's two steps), `conc_slot<i>`, `conc_launder<i>`, `conc_drop`; `infeedchute_{n,s,e,w}`;
`tailingschute_{e,w,bottom,top}`; `concentratechute_{n,s,e,w}`; `pipecopper_*` and `pipelead_*` (`inlet_*`,
`bend_*`, `riser_*`, `reducer`, `nozzle1`, `nozzle2`); and `fr_*` for the frame (`fr_hb_floor_{w,e,s}`,
`fr_hb_wall_{w,e,n,sw,se}`, `fr_hb_sill`, `fr_hb_leg_{nw,ne,sw,se}`, `fr_hb_bearer_{w,e}`, `fr_sl_floor<i>`,
`fr_sl_side_{w,e}<i>`, `fr_sl_trestle<i>_{legw,lege,beam}` (2 is the outlet trestle), `fr_sl_tailboard`,
`fr_ld_floor<i>`, `fr_ld_side_{w,e}<i>`, `fr_ld_head`). Hand edits are lost when the script runs again: port them
into `make_shape.py`, or stop regenerating.

## Salvaged and new

From **`735-concentrator-model-v2`** (`Concentrator/tools/make_shape.py`, read with `git show`, not merged):
`VANILLA_CHUTE`, `chute_profile`, `vanilla_profile` and `read_vanilla_chute` (the chute check, extended to the
upright, north-south and east-west sections), the slope helpers (`sloped`, `slope_len`, `along`, `floor_y`,
`under_slope`), `skin` and `box`, `build_pipes` and `pipe_copies`, and from `build_sluice` the sloped plank trough,
seven riffles at a pitch of 4, 1 in 12 and the trestles. The validator's `ray_down`/`first_below` flow check is v2's
too.

New: the head box with its sill, the water inlet under it and up through its floor, the reducer and nozzle, the
draw slots, the concentrate launder, the upright concentrate chute out of the bottom face and its outlet trestle, the
tail board, the tailings chute at the tail's end face, the
channel narrowed to the chute's bore, the floor set to one block (v2's head was at y 18.3 under a wash house), the
riffles' iron straps, the trestles' legs rising beside what they carry, and the checks of the flow, the riffles, the
concentrate's way out and the supports.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in a client
or in a browser with WebGL (the standalone viewer's controls were checked headless, without WebGL).

1. **No water or pulp is drawn.** The head box and the trough are dry; nothing shows the stream. A "running" film
   over the floor and the pool in the box at the sill would be more of the `concentrate` kind of part, drawn while it
   runs.
2. **The draw slots are narrow from above**: 1 long, 0.3 of it under the strap's lip, so about 0.7 shows behind each
   riffle when the concentrate is not drawn.
3. **The outlet trestle's beam lies over the concentrate chute's south wall** and a half voxel of its bore (z 42.5..43),
   0.9 over its top: clear of where the concentrate falls (z 39.4..40.2), but seen from above or below it crosses the
   open top's south edge. The legs had to stand against the chute's walls inside the chute's z, so the beam is there.
4. **Collision follows the shapes at rest, every part shown:** the three chute sections are in the boxes whether
   fitted or not.
5. **The concentrate's colour in the viewer** by texture is the stone family's grey (its path names `stone`): the
   viewer has no family for it (`site/src/lib/model-view.ts`), and none was added, the file being shared.

## Open for the owner

1. **Width: the channel is 6.1, not the issue's 7.** Matched to the chute's 6 bore, so the tail pours straight into
   the tailings chute with nothing to narrow it; 7 would need a tail spout or box to come down to the bore. Period
   mill sluices were 12 to 18 inches wide; 6.1 voxels is about 15 inches.
2. **The concentrate out of the bottom face, in its own cell (the owner's review).** The sluice is now four long,
   not three: the riffles and the concentrate's outlet over cells 1 and 2, cell 3 a plain run-off of 22 voxels (no
   riffles) to the tailings chute. The concentrate chute stands upright in cell 2 from its bottom face, so the
   block under cell 2 must be a chute (or a container) to take it, and the sluice stands a block up off the ground
   there, or over a trench. The other way round (concentrate chute in cell 3, tailings off a longer sluice into cell
   4) would make it five long. Riffles could be added over cell 3 only without slots (their catch could not reach the
   launder, which falls the other way).
3. **The water comes up through the head box's floor.** The ppex pipe meets a face at its centre (y 5..11) and the
   sluice had to rise to leave room under its floor for the launder, so the head box's floor (y 12) is now over the
   pipe: it runs in under the box and rises through the floor to a nozzle pointing up into the pool, where the ore
   lands (a "boil"). The alternative was a pipe through a side face into a much deeper box, which would settle out the
   heavy grains before the riffles. The longer sluice does not change this (the head is where it was; the extra block
   is at the tail), so the inlet is kept as it was.
4. **The draw-off.** Slots in the floor behind the riffles into a launder underneath (a period draw-off box under the
   riffles; an undercurrent proper would be a second, wider sluice beside this one). The slot is 1 long, the launder
   1.6 deep; every riffle has one (the riffles closed up to a pitch of 3.5 from 4 so the seven fit before the outlet).
5. **The riffles' iron straps** (Hungarian riffles) are new: iron for the wearing surface. Plain oak bars, as v2's,
   if they are not wanted.
6. **The chute sections as fitted parts** (`infeedchute`, `tailingschute`, `concentratechute`): all three go in with
   the services stage, so each is drawn once it is fitted.
7. **The concentrate's texture** (basalt sand, its own code for a renderer to set to the ore's), and drawing it only
   while the sluice runs (`concentrate`).
8. **No lid** over the cells: the channel is narrower than a player, and the boxes are the deck. The press brake and
   the amalgam pan have one.
9. **Build stages** (open on #735): set by the gameplay (#734) from the model ("Build stages" above): the frame is
   all the timber (8 planks, 2 support beams, 3 nails and strips), then the riffles, then the services. A timber stage
   like the jig's would need the frame split under a new `requires`, a model change; the frame fits the grid limit as
   it is.
10. **The anchors are provisional pending #711** (the shared chute, reservoir and water code): their names
    (`infeedCell`/`Face`, `tailingsCell`/`Face`, `concentrateCell`/`Face`, `waterCell`/`Face`).
11. **No C# rig test and no reference poses**: nothing moves, so there is nothing for the shared maths to compute; a
    test that the shipped rig parses with `RigParts.Parse` would come with the gameplay.

## Gameplay

Built (#734; `Game/`, switch `Sluice`, which needs `OreProcessing`). The frame is the shared ore machine block
(`seraphhorizons:sluice-{side}`, class `seraphhorizons.OreMachine`, its block type `blocktypes/sluice/frame.json`), its
cells the shared ones, its block entity `BESluice` on `BEOreMachine` (`Machines/Game/`): the staged build, the
reservoir, the infeed, the chutes out and the tailings heap are the shared code's; the sluice adds what it washes,
its clock and its products.

- **Placement.** The head (the controller) is the block clicked; the sluice runs away from the player, four blocks.
  The block under cell 2 is the concentrate's: a chute laid to take from above, or a container. The water face is the
  head's north end (nearest the player), the infeed its top, the tailings face the tail's south end.
- **Feed.** Crushed or ground ore (`seraphhorizons:crushedore-*`, `groundore-*`, with whatever state they carry:
  riddled, amalgamated, a concentrator's tailings), Wilderlands Panning's rich gravel (`game:richgravel-{rock}`) and
  Geology Additions' bog iron (`game:stone-bogiron`), by a chute into the head's top face or by hand (right-click).
  Raw ore needs breaking first.
- **Work.** No power: with an item's water (10 L, `machines.sluice.water`) in its reservoir, filled from a ppex pipe on
  the water face at up to 10 L/s (20 L), it washes 16 rock an in-game hour (`machines.sluice.perHour`), counted in
  in-game hours (`WorkHours`). Each item is one pass of the recovery core (`ConcentratorWash`, `OreRecovery.Concentrate`,
  r = 0.55): crushed or ground ore its ore's concentrate (the locked shares applied at this first concentrator);
  rich gravel the gold pan's table at 0.55 / 0.45 of the pan (nuggets, no one-item cap), its stones and flint at the
  pan's rate (`PlacerLine`, `PlacerTable`, #701); bog iron limonite concentrate (14 units a stone). Whole items leave
  by the concentrate chute, the rest carried to the next item (`UnitCarry`, saved with it).
- **Tailings.** The item goes on as tailings, the same item carrying what is left (`oreUnits`, `oreLocked`,
  `oreWorked`), by the tailings chute to the jig, or, with nothing beyond the tail face, onto a heap of gravel in its
  rock beside the tail, a layer a rock (#694).
- **Drawn.** The renderer (the shared `MachineRenderer`) draws each part once its stage is in, the supplied metal's
  inlet, and the concentrate in the riffles while the sluice works.
- **Bog iron** (#701, the owner's ruling of 2026-10-14): with the switch on, its block drops exactly one stone and
  the stone loses its raw smelt to a bloom (`SluiceSystem.BogIronOnTheLine`).
- **Recipe browser.** A `sluice` record per feed item (`RecipeSection.Sluice.cs`, `SluiceSystem.ExportYields` by
  reflection), and the shared `construction` record of its build.
