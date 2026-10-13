# Sluice: the model

Part of the Seraph Horizons mod (`../README.md`): the generated model of the **sluice**, the first concentrator
of the ore line (epic #684; behaviour #734, model #735). A riffled plank sluice: "10 to 15 feet of sluice boxes
fitted with transverse riffles" [Louis 1902, p. 293], which a stamp mill ran its pulp through after the plates,
carrying it in a stream of water; the heavy grains settle behind the riffles and are cleaned up by hand. The
line, the glossary and the period sources are #939; the power and water budget #940.

**Status: model only.** This folder's `tools/` holds the model's generator and its checks. The gameplay is #734
and is not built; "Gameplay", below, says what the rig offers it.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` or `Pipes/`, this folder's neighbours. The generic half of the
generator is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one: **no
driver at all**, and no change to the package. The water inlet is built by `Pipes/tools/solderedpipe.py`, as the
rosser's drip is.

## The sluice

**It takes no power and nothing moves.** There is no axle, no `powerCell` and no driver: every part is a fitted
part or a state, drawn or not. Its services are water (a ppex pipe) and chutes (vanilla's copper chute in and
out).

**Footprint.** 1 wide (x) × 1 high (y) × 3 long (z): cells `[0,0,0]`, `[0,0,1]`, `[0,0,2]`. The controller is
the **head cell**, `[0,0,0]`, nearest the player; the sluice runs away from them, south (+z), and falls that way.
Each dimension is what has to fit: one block high because the infeed chute comes down into the head from above and
the tailings chute leaves the tail at its face's centre (y 4..12), so the floor runs from the head box's sill
(y 8.08) down to just over the tailings chute's bore floor (y 5.5 over 5) at 1 in 12, which takes the 31 voxels
from the head box to the tail; one wide because the channel is the chute's bore; three long for the head box
(10), the riffled run (31) and the tailings chute's section (4).

**The flow.** Ore (crushed or ground rock, or placer gravel) comes down a vanilla chute into the head cell's top
face and falls into the **head box**, where the water inlet runs in through the box's end wall; the pulp
overflows the box's **sill** onto the sluice, runs down over the seven **riffles**, and leaves off the tail into the
**tailings chute**, flush with the tail cell's south face. The **concentrate** settles behind the riffles and stays
there until the player cleans them up by hand.

**Anchors** (in `assets/seraphhorizons/config/sluice-rig.json`; all provisional pending #711, the shared chute and
water code):

| Key | Value | What it is |
|---|---|---|
| `infeedCell`, `infeedFace` | `[0,0,0]`, `up` | A vanilla chute from above meets the infeed section, centred in the face and flush with it |
| `waterCell`, `waterFace` | `[0,0,0]`, `north` | A ppex pipe meets the water inlet, end to end, on the face's centre (ppex's pipe is its block's 5..11) |
| `tailingsCell`, `tailingsFace` | `[0,0,2]`, `south` | The tailings chute leaves the tail: chuted on, the tailings go to the next machine; else they are discarded beyond this face (#694: gravel, a layer a rock) |
| `feedSpout.pos` | `[0.5, 0.75, 0.5]` | Where the ore falls into the head box (the infeed chute's mouth): particles, sound |
| `waterSpout.pos` | `[0.5, 0.5, 0.3125]` | The nozzle's mouth: splash |
| `concentrate.pos` | `[0.5, 0.414, 1.906]` | The middle of the riffled run, on the floor: the clean-up's sound and particles |
| `fill.requires` | `concentrate1`, `concentrate2`, `concentrate3` | The concentrate's states, emptiest first (below) |

There is no `powerCell`, `powerFace`, `infeedSide`, `outputSide` or work, and no return-water anchor (the water is
used up, #694).

**Fitted parts** (the rig's `requires`). Everything with `requires` null is the frame (`sluice_frame.json`): the
head box, the sluice box and the four trestles.

| `requires` | Part | Draws |
|---|---|---|
| `riffles` | `riffles` | The seven riffles: oak bars across the channel, each faced with strap iron |
| `infeedchute` | `infeedchute` | Vanilla's straight chute section, upright, from the head cell's top face into the head box |
| `tailingschute` | `tailingschute` | Vanilla's straight chute section, from the tail into the tail cell's south face |
| `pipecopper`, `pipelead` | `pipecopper`, `pipelead` | The water inlet and its nozzle, soldered, in copper or in lead: the same elements, one drawn |
| `concentrate1`, `concentrate2`, `concentrate3` | the same | The concentrate behind the riffles: about a third, about two thirds, full; one drawn at a time, or none |

The build stages (what the frame is made of, what each stage takes) are not designed yet: the ids split the
sluice where a stage could, and the stages are worked out with the owner afterwards (`Open for the owner`).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. The boards are the game's oak planks (`game:block/wood/planks/oak1`, the floors' boards turned to run
along the trough), the timbers its debarked oak, the riffles' straps its iron plate, both chute sections vanilla's
chute in copper (`game:block/metal/sheet/copper1`, as `shapes/block/metal/chute/straight.json` wears it), the water
inlet UnifiedPipes' copper or lead pipe (`game:block/metal/sheet-plain/{copper,lead}4`, as the rosser's), and the
concentrate the game's basalt sand (`game:block/stone/sand/basalt`), a dark heavy sand, under its own texture code
`concentrate` so a renderer can set it to the ore's look. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/sluice.json` | The whole sluice: the frame, every fitted part, both pipe metals and all three concentrate states (87 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/sluice_frame.json` | The frame only (27 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/sluice-rig.json` | The three cells, the anchors and the part rig (9 parts, none with a driver). |

There are **no reference poses** (`tests/Sluice/rig-reference.json`): no part has a driver or rides another, so
every part's matrix is the identity at any input, which the generator's checks, `site/test/sluice.test.ts` and
`tools/tests/test_sluice_model.py` check instead.

### How it is built

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north
to south) in voxels, measured from the head cell's north-west bottom corner; the build frame's corner is the
controller's, so the shipped files are the build frame divided by 16. The machine's centre line is x 8: the
channel's, both chutes' and the inlet's.

**The head box** is a plank box 10 × 10 outside (x 3..13, z 3..13), walls 1 thick, its floor's underside at y 5
and its walls' top at y 13: inside it is 8 × 8, the infeed chute's outside, and the chute comes down into it from
the top face to y 12, held by the walls round it for its last voxel. Its floor's top is at **y 6**, 2 under the
sill: the pool the ore falls into and the water stirs. Its north wall is pierced by the water inlet (x 5..11,
y 5..11, the pipe's bottom the box's); its south wall stands full height beside the channel and stops at the
**sill** across it (y 8.08, a hair wider than the channel, so the sluice's sides, cut square to the slope, sink
into the wall beside it unseen). It stands on **two trestles** (z 4.4 and 11.6): a cross beam under the floor, its
top at y 5, mortised through two oak legs (1.6 square) that rise beside the walls to y 12.

**The sluice box** is a plank trough from the head box's south wall (z 13) to the tailings chute's mouth (z 44): a
floor 1 thick between two sides 0.9 thick, standing 3 over the floor. The **channel** is 6.1 wide (x 4.95..11.05),
the chute's 6 bore and a hair: the sides (x 4.05..4.95, 11.05..11.95) run into the chute's walls (x 4..5, 11..12) at
the tail, so the pulp runs straight from the floor into the bore. The floor falls **1 in 12**, from y 8.08 at the
sill to **y 5.5** at the tail, half a voxel over the tailings chute's bore floor (y 5). Floor and sides are two
boards' lengths each (no piece longer than a texture), butted at z 28.5 over the first of the sluice's **two
trestles** (z 28.5 and 42), each a beam under the floor and two legs rising beside the sides to half a voxel under
their top. Every sloped piece is cut square to the slope: at the head its top corner meets the head box's wall
(the rest runs into the wall), at the tail its bottom corner meets the chute's mouth (the rest runs 0.08 to 0.33
into the chute's walls, inside them), so neither end shows a gap.

**The riffles.** Seven, every 4 voxels along the floor's top line (upstream faces at z 18, 22, .., 42), leaving 5
of plain floor at the head for the pulp to spread on and 1 between the last and the lip. Each is an oak bar 1
along the flow and 1.1 high, standing on the floor square to the slope from side to side, faced on top with a strap
of iron 0.2 thick that overhangs its downstream face by 0.3 (a Hungarian riffle's lip: the wearing surface, and the
eddy behind it).

**The concentrate** lies in the six **pockets** behind the riffles: pocket *i* from riffle *i*'s downstream face to
riffle *i* + 1's upstream face (3 along the floor). A full pocket is one block of sand to 1.0 over the floor (0.1
under the straps' lips), into both riffles; a part-full one a wedge in two steps, deepest against the riffle behind
which it settles: a lower layer most of the way along and a deeper one half as long. Each runs a hair into the floor,
the sides and its riffle, so no gap shows. The heavy grains settle first behind the upper riffles, so the pockets
fill **from the head down** (`FILL`, by pocket):

| State | `requires` | Pockets (fill) | Of full, by volume |
|---|---|---|---|
| None | — | none drawn | 0 |
| About a third | `concentrate1` | 1, 0.8, 0.6, 0.35, 0.1, 0 | 33 % |
| About two thirds | `concentrate2` | 1, 1, 1, 0.9, 0.6, 0.3 | 67 % |
| Full | `concentrate3` | every pocket brimming | 100 % |

**The chutes.** Both sections are vanilla's straight chute exactly (`VANILLA_CHUTE`, salvaged from the
concentrator's v2 generator: an 8 × 8 tube 1 thick, centred in the block, a 6 × 6 bore, two full-width walls and
two between them), checked against the game's `shapes/block/metal/chute/straight.json` wherever the game is
installed: the **infeed** upright (its "ud" variant) from y 16, flush with the head cell's top face, down to y 12;
the **tailings chute** north-south (its "ns" variant) from z 44 to z 48, flush with the tail cell's south face.

**The water inlet** is UnifiedPipes' soldered pipe in the rig's copper or lead (`Pipes/tools/solderedpipe.py`:
ppex's 6 × 6 tube round a 4 × 4 bore): a straight length on the water face's centre (x 8, y 8) from the face (z 0)
through the head box's north wall to its inside face (z 4), with no joint at the face (the pipe beyond brings its
own, as on the rosser); on its end, inside the box, a **reducer** plate standing on the box's floor and the
**nozzle** in two steps from its middle (3 × 3, then 2 × 2), pointing down the sluice, its mouth at z 5, the edge of
the falling ore's path.

Every part (nothing moves; θ and every other input change nothing):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Riffles (`riffles`) [riffles] | Static | — | none |
| Concentrate (`concentrate1..3`) [concentrate1..3] | Static: one drawn by how full the riffles are | — | none |
| Infeed chute section (`infeedchute`) [infeedchute] | Static | — | none |
| Tailings chute section (`tailingschute`) [tailingschute] | Static | — | none |
| Water inlet (`pipecopper`, `pipelead`) [the same] | Static: the supplied metal's drawn | — | none |
| Frame (`frame`): head box, sluice box, trestles | Static | — | none |

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
- **Nothing floats:** the frame is one piece from the ground; each fitted part and concentrate state is held by the
  frame (the concentrate by the frame or a riffle), through its own elements.
- **Textures** by role: planks for the boxes' boards, oak for the trestles and the riffles' bars, iron for the
  straps, `chute` for both sections, each pipe metal its own, `concentrate` for the concentrate; and no element
  longer than a texture (16), so none is stretched.
- **Containment:** nothing leaves the three cells.
- **Chutes:** both sections vanilla's walls exactly, flush with their faces (the infeed's top at y 16, the tailings
  chute's end at z 48), in the chute's texture; `VANILLA_CHUTE` is the game's file, where the game is here; the
  infeed, water and tailings faces differ.
- **Water:** the inlet meets a ppex pipe end to end on the water face's centre, ppex's 6 × 6 tube with its 4 × 4
  bore, runs through the head box's north wall to its inside face; the reducer and the nozzle are inside the box,
  on its floor, short of the falling ore's path; `waterSpout` is the nozzle's mouth.
- **The flow** *(the pulp's way through)*: ore dropped down the infeed chute's bore (nine points) lands on the head
  box's floor; the box's floor is under its sill and the sill is the sluice's head floor across the channel; the
  channel is the chute's bore or a hair wider, over it; the floor falls at exactly 1 in 12 from the sill's edge; a
  grain dropped every half voxel down the channel's centre line, in every state, lands on the floor, a riffle or the
  concentrate; the lip stands over the tailings chute's bore floor and under its top, the last riffle too, and the
  sluice's pieces reach the chute's mouth.
- **Riffles:** seven, evenly spaced, each bar on the floor from side to side and square to the slope, its strap on
  its top over its downstream face; 3 or more of plain floor at the head, the last short of the lip.
- **Concentrate:** each deposit in its pocket behind its riffle, on the floor and just into the sides, under the
  straps; each state holds at least as much as the last in every pocket (by volume, measured on a grid), the pockets
  fill from the head down, and full is every pocket brimming.
- **Supports:** each trestle's beam under what it carries (within 0.02), its legs on the ground beside it; the
  sluice boards' joint over a trestle's beam.
- **Clearances:** in every state and either metal, no two parts touch except the intended contacts listed in
  `ALLOWED` (the riffles and the concentrate on the floor and against the sides, the concentrate against the
  riffles, the infeed in the head box's mouth, the sluice's tail into the tailings chute, the inlet through the wall
  and the reducer on the floor).
- **No z-fighting** (full runs): no coplanar overlapping faces in any state. The model is built with none before
  the fix (the deposits' two steps stagger their hidden faces, the sill's notch is a hair wider than the channel);
  the fix only removes the faces pressed against their own part (35).
- **Files:** every texture declared; three cells, each with boxes and none hollow, no lid (the channel is narrower
  than a player, and the boxes are the deck); no power cell, work or sides; the point anchors inside the footprint;
  `fill` lists the concentrate's states in order; the shipped model is the checked one (no move).

The cells' boxes are rebuilt from the shipped, rounded shape at rest, every part shown, as the other machines' are.

### Rig schema (`sluice-rig.json`)

The amalgam pan's schema (`../AmalgamPan/README.md`, "Rig schema") without even its θ: nothing is added to the
shared rig maths.

- **Inputs.** None. Every part's `drivers` is empty and its `ride` null.
- **Keys.** `cells` (three, each with its boxes), `infeedCell`/`infeedFace`, `waterCell`/`waterFace`,
  `tailingsCell`/`tailingsFace`, the points `feedSpout`, `waterSpout`, `concentrate`, `fill` (`requires`: the
  concentrate's states, emptiest first) and `parts`. `requires` values: `riffles`, `infeedchute`, `tailingschute`,
  `pipecopper`, `pipelead`, `concentrate1`, `concentrate2`, `concentrate3`, or null. The texture code a renderer may
  set: `concentrate` (the ore's look).

**In the viewer** (`site/models.json`, `sluice`): the riffles and the two chute sections are checkboxes, the inlet's
metal a choice (copper, lead), and the concentrate a "Concentrate behind the riffles" select of four states (None,
About a third, About two thirds, Full), opening on two thirds, which waits on the riffles: untick them and the
concentrate is not drawn. There is no Play: nothing turns.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `riffle_<i>_bar`, `riffle_<i>_strap`;
`conc<k>_pocket<i>` (full) or `conc<k>_pocket<i>a`/`b` (a wedge's two steps); `infeedchute_{n,s,e,w}`;
`tailingschute_{e,w,bottom,top}`; `pipecopper_*` and `pipelead_*` (`inlet_*`, `reducer`, `nozzle1`, `nozzle2`); and
`fr_*` for the frame (`fr_hb_floor`, `fr_hb_wall_{w,e,nw,ne,nt,sw,se}`, `fr_hb_sill`, `fr_hb_trestle<i>_{legw,lege,beam}`,
`fr_sl_floor<i>`, `fr_sl_side_{w,e}<i>`, `fr_sl_trestle<i>_{legw,lege,beam}`). Hand edits are lost when the script
runs again: port them into `make_shape.py`, or stop regenerating.

## Salvaged and new

From **`735-concentrator-model-v2`** (`Concentrator/tools/make_shape.py`, read with `git show`, not merged):
`VANILLA_CHUTE`, `chute_profile`, `vanilla_profile` and `read_vanilla_chute` (the chute check, extended to a
north-south section), the slope helpers (`sloped`, `slope_len`, `along`, `floor_y`, `under_slope`), `skin` and
`box`, `build_pipes` and `pipe_copies`, and from `build_sluice` the sloped plank trough, seven riffles at a pitch of 4,
1 in 12 and the trestles. The validator's `ray_down`/`first_below` flow check is v2's too.

New: the head box with its sill and the inlet through its wall, the reducer and nozzle, the tailings chute at the
tail's end face, the channel narrowed to the chute's bore, the floor lowered to one block (v2's head was at y 18.3
under a wash house), the riffles' iron straps, the concentrate's three states, the trestles' legs rising beside what
they carry, and the checks of the flow, the riffles, the concentrate and the supports.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in a client
or in a browser with WebGL (the standalone viewer's controls were checked headless, without WebGL).

1. **No water or pulp is drawn.** The head box and the trough are dry; nothing shows the stream. A "running" state
   (a thin film over the floor, the pool in the box at the sill) would be another `requires` value gameplay draws
   while water flows.
2. **The concentrate's states are steps.** Three looks, not a level that rises; gameplay picks one by how full the
   riffles are.
3. **The nozzle is inside the head box**, behind its north wall, seen from above.
4. **Collision follows the shapes at rest, every part shown:** both chute sections are in the boxes whether fitted
   or not.
5. **The concentrate's colour in the viewer** by texture is the stone family's grey (its path names `stone`): the
   viewer has no family for it (`site/src/lib/model-view.ts`), and none was added, the file being shared.

## Open for the owner

1. **Width: the channel is 6.1, not the issue's 7.** Matched to the chute's 6 bore, so the tail pours straight into
   the tailings chute with nothing to narrow it; 7 would need a tail spout or box to come down to the bore. Period
   mill sluices were 12 to 18 inches wide; 6.1 voxels is about 15 inches.
2. **The water inlet low in the head box's end wall.** The ppex pipe meets a face at its centre (y 8), and the head
   box and the trough fill the cell's width and the space over the floor, so a pipe cannot rise beside them to a
   nozzle over the head; it runs in through the box's north wall instead, to a nozzle on the box's floor pointing
   down the sluice, with the box's floor (y 6) 2 under the sill so the water and ore have a pool to mix in. The
   alternatives: the inlet through a side face (west or east of the head cell, the nozzle pointing across the box), or
   a pipe from above through the middle cell's top face running back over the head (it would cover the first
   riffles).
3. **The head box's pool** (2 deep under the sill) would catch heavy grains in a real box; no concentrate is drawn
   there.
4. **Concentrate behind the riffles = downstream of each**, in the lee of its iron lip, filling the pocket to the
   next riffle. The thresholds for the three states are the gameplay's: suggested, none at 0 of 32, `concentrate1`
   up to a third, `concentrate2` up to just short of full, `concentrate3` at 32 (full: the sluice passes everything
   on until cleaned up), so "full" always reads as full.
5. **The riffles' iron straps** (Hungarian riffles) are new: iron for the wearing surface. Plain oak bars, as v2's,
   if they are not wanted.
6. **The chute sections as fitted parts** (`infeedchute`, `tailingschute`), so a renderer can draw each only while a
   chute is there, or as stages; they could be the frame's instead.
7. **The concentrate's texture** (basalt sand, its own code for a renderer to set to the ore's).
8. **No lid** over the cells: the channel is narrower than a player, and the boxes are the deck. The press brake and
   the amalgam pan have one.
9. **Build stages** (open on #735): the frame recipe, the stages and their materials, measured from the model, as
   the crucible furnace's were. The `requires` ids split the sluice where a stage could: the riffles, each chute
   section, the water inlet.
10. **The anchors are provisional pending #711** (the shared chute, reservoir and water code): their names
    (`infeedCell`/`Face`, `tailingsCell`/`Face`, `waterCell`/`Face`) and `fill`.
11. **No C# rig test and no reference poses**: nothing moves, so there is nothing for the shared maths to compute; a
    test that the shipped rig parses with `RigParts.Parse` would come with the gameplay.

## Gameplay

Not built: #734 (crushed or ground rock and placer gravel in, r = 0.55, 16 rock an hour, 10 L of water a rock from a
20 L reservoir; 32 concentrate held behind the riffles, cleaned up by hand; full, it passes everything on;
tailings by chute, or discarded as gravel). What the rig offers it:

- **Placement.** The controller is the head cell; the sluice runs away from the player (`side` the way they look, as
  the press brake's), three cells long. The infeed face is the head's top, the water face its north end, the
  tailings face the tail's south end, each turned with the facing.
- **Parts.** The frame shape is the block's. A renderer draws the parts whose `requires` are fitted: the riffles, the
  chute sections, the supplied metal's inlet, and one of `fill.requires` (or none) by how full the riffles are. No
  part moves, so a renderer needs no clock: the parts could as well be tessellated into the block's mesh when they
  change.
- **Points.** `feedSpout` (ore falling into the box), `waterSpout` (the nozzle) and `concentrate` (the clean-up).
