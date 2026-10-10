# Crucible furnace: the model

Part of the Seraph Horizons mod (`../README.md`, "Crucible furnace"): the generated model of the whole
crucible furnace as one machine (#753), placed as a multiblock with ghost cells like the pack's other
machines, in place of today's row of block-built melting holes.

**Status: the model only.** This folder's `tools/` holds the model's generator and its checks. The game
still uses the melting holes, their pot and the chimney row (`Core/`, `Game/`, the mod README's "Crucible
furnace"); nothing in the game reads this model yet. What wiring it in will take is under "Wiring it into
the game", below.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, shared with the other machines and unchanged by this one: no new driver and
no change to the package.

## The furnace

A Huntsman (Sheffield) crucible melting furnace of about 1850, as one block of tier-3 refractory
brickwork **three blocks wide (x), two deep (z) and two high**, bound with iron (an angle iron up each
corner, a band round the top edge), with **two melting holes** set into its top, side by side at the front.

- **The holes.** Each is 12 voxels square inside (the period's two feet, at the game's scale) and from its
  grate to its mouth 17.5 deep, lined with the firebrick. At its mouth a seat 14 square and 2.5 deep, a
  one-voxel ledge round the hole, takes the **cover**, firebrick in an iron frame, flush with the floor,
  with a pocket in its middle and an iron bar across the pocket for the melter's hook.
- **The cover is lifted aside** straight back into a **bay** of its own in the floor behind the hole (the
  back row), where it lies flush on an iron plate: lifted 2.6 voxels out of the seat, carried a block back
  over the floor, set down. So the floor is clear either way, and the open hole is reached from the front.
- **Under each hole a grate**: six iron firebars across it, lying on two iron bearers that run front to
  back and are built into the front and back walls. On the grate stands the **fireclay stand**, and on it
  the **pot** (a fireclay crucible, an octagonal barrel 8 across at the belly and 11 tall from its narrow
  foot: the size of the game's `meltingpot` block), packed round with **coke** up to its shoulders.
- **One ashpit, the cave**, runs under both grates and the pier between them, 8 voxels high. It is open at
  the front under each hole (an **ash mouth** with an iron lintel), where the air comes in on the chimney's
  draught and where the fire's glow shows when the covers are on. At the back, in the middle, an **air
  port** (an iron flange with a sleeve, flush with the back face) leads through a duct into the cave: a pipe
  from smex's or ppex's air blowers is laid to it for forced air.
- **The flue.** Near the top of each hole's back wall, above the coke and under the seat, a **flue
  opening** leads into a **back flue** running behind both holes, under the bays, to the **stack** in the
  middle of the back row. The stack rises to an **outlet** on the furnace's top, a square mouth with a
  flush iron curb. **The model stops at the outlet**: the player stands the chimney on it (clay bricks,
  refractory bricks, stone bricks or smex's smoke stack, today's `ChimneyBlocks`), and the chimney's
  height stays the draught's lever.

**Footprint and cells.** The brickwork's 12 cells, and 4 **hollow cells** over the two holes and their
bays (`[-1,2,0]`, `[-1,2,1]`, `[1,2,0]`, `[1,2,1]`): a cover being carried rises 2.6 voxels over the
floor, so nothing may be built there. The cell over the outlet, `[0,2,1]`, is not the furnace's: it is
the chimney's first block. In the native, south-facing frame with the controller at `[0,0,0]`: x −1..1,
y 0..1 (2 over the holes and bays), z 0..1. The **controller is the front middle cell at ground
level**, `[0,0,0]`, under the floor between the holes. Placed like the press brake and the mandrel
station, the furnace extends away from the player along their line of sight: the holes and the ash
mouths face them, the bays, stack and air port are behind.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `hole1Cell`, `hole1Face` | `[-1,1,0]`, `up` | Hole 1 (west): worked from its top |
| `hole2Cell`, `hole2Face` | `[1,1,0]`, `up` | Hole 2 (east) |
| `outletCell`, `outletFace` | `[0,1,1]`, `up` | The stack's base; the chimney stands on its top, in `[0,2,1]` |
| `airCell`, `airFace` | `[0,0,1]`, `south` | The air port: a pipe in the cell south of it, against its south face |
| `chimney` | origin `[0.5,5,1.5]`, `y`, 6 | The shortest chimney on the outlet (`MinChimneyHeight` blocks), drawn as a line |
| `mouth1`, `mouth2` | `[-0.5,2,0.5]`, `[1.5,2,0.5]` | The middle of each hole's mouth at the floor: flames, the glow's light and the fire's sound while it is open |
| `furnace` | | `holes` 2, `cokeLayers` 3, `cokePerLayer` 2, `minChimney` 6 (not drawn) |

**Lids, boxes.** Each of the six top cells of the brickwork has a `lid` at the floor (1.0): a player
walks over an open hole and cannot fall in. The cells' boxes are built from the brickwork, its iron and
the covers at rest; the box splitter cannot leave a hole open in three boxes, so every cell's box is the
whole cell (a click anywhere on a hole's cell is that hole's).

### Why two holes

A generated machine has one footprint, so the furnace's size is fixed where today's row takes 1 to 4
holes. Two holes:

- **A pair of holes to a stack** was the common arrangement of the period's melting shops (a shop was a
  row of such pairs), and it is the shape that closes: the stack goes behind the middle, each hole's flue
  runs straight back to it, and each cover has a bay of its own behind its hole.
- **It is the size of the pack's machines**: 3 × 2 × 2 blocks, a little more than the gear cutter
  (2 × 2 × 2) and far less than the sawmill or the rosser. Four holes would be 5 long with the stack in the
  middle, the outer holes' flues running behind the inner ones, and a 5-block furnace to build before the
  first pot.
- **Capacity grows the period's way**: two pots of 200 units a heat (four ingots of stainless); more is
  another furnace with its own chimney.

The number is one constant (`HOLES`, `HOLE_X`); the layout (bays behind, the stack between them) is a
pair's.

### No power

The furnace is fired with coke: it takes no mechanical power, so the rig has **no `powerCell` or
`powerFace`** and no power ghost (the validator fails if one appears). `machinegen` assumes no power
cell (the hand stations, the press brake, the squaring shear and the mandrel station, have none), so
nothing had to change.
As with them, the rig's θ is a clock that moves nothing: a `rotate` of ratio 0 on `h1cover` carries it,
so the viewer offers Play, which lifts the covers on and off.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. **No new texture**: every texture is the game's (below). `tools/make_shape.py`
writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/cruciblefurnace.json` | The whole furnace, every part (461 elements) |
| `assets/seraphhorizons/shapes/block/cruciblefurnace_frame.json` | The static frame only (209 elements): the brickwork, its iron, the grates and the stands. A block draws it |
| `assets/seraphhorizons/config/cruciblefurnace-rig.json` | Cells, anchors and the part rig (21 parts) |
| `tests/CrucibleFurnace/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

**Textures** (all the game's; the shape names them, a blocktype may map them):

| Code | Texture | On |
|---|---|---|
| `brick` | `game:block/clay/refractory/tier3/front1` (today's melting hole's) | The brickwork and the covers' infill: world-mapped, so the many boxes read as one wall |
| `iron` | `game:block/metal/plate/iron` (the other machines' iron) | Firebars, bearers, lintels, the bays' plates, the outlet's curb, the air port's flange and sleeve, the binding, the covers' frames and hook bars |
| `clay` | `game:block/clay/hardened/fire` (the fired pot's) | The pots and their stands |
| `coke` | `game:block/coal/coke` | The coke |
| `ember` | `game:block/coal/ember` | The same coke, burning; its faces glow 200 (the game's lit firepit's embers glow 160) |
| `melt` | `game:block/metal/ingot/stainlesssteel` | The melt; glows 255. The renderer may set the pot's metal |
| `charge` | `game:block/metal/ingot/iron` | The charge's pieces; the renderer may set it |

### How the furnace works

Nothing turns. The covers are the only parts that move; everything else that shows or not is a `requires`,
gameplay's state of each hole.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The brickwork, its iron binding (`frame`) | fixed | carries everything; its voids are the holes, seats, bays, cave, ash mouths, flue openings, back flue, stack and air duct |
| The bearers and firebars (`frame`) | fixed: the bearers built into the front and back walls, the bars lying across them | carry the stand and the coke; the draught comes up between the bars |
| The stand (`frame`) | fixed, on the grate | carries the pot |
| Hole *i*'s cover (`h<i>cover`, no requires) | depth, the cover's place: lifted 2.6 over depth 0..0.2, carried a block forward over 0.2..0.8, set down into the seat over 0.8..1. In the game each hole's cover by its own | shuts the hole: the fire then draws only up through the grate, out at the flue opening |
| The pot (`h<i>pot`) | put in, pulled: fitted or not | holds the charge, then the melt |
| The charge (`h<i>charge`) | charged | fills the pot to 22.6 with pieces standing up to 26, over the rim |
| The melt (`h<i>melt`) | the charge melted | the pot's molten surface at 22.6, glowing |
| The coke, three layers (`h<i>coke1`..`3`) | the coke in the hole: two coke a layer, from the grate up | packed round the pot to its shoulders, a lump of coke in each corner of each layer's top |
| The coke burning (`h<i>fire1`..`3`) | lit: the same layers in their burning texture, glowing; the bottom one also drops cinders on the cave's floor under the grate | the glow: from above when the cover is off, at the ash mouth and up through the bars when it is on |
| The draught (no part) | the chimney on the outlet, or forced air at the port | in at the ash mouths (or the port), up through the grate and the coke, out at the flue opening, along the back flue, up the stack and the chimney |

**The coke's layers.** Gameplay's coke (0..`CokeCapacity`, 6) is drawn as its layers
`h<i>coke1..ceil(coke / 2)`: 1 or 2 coke the bottom layer (the grate to 16), 3 or 4 the middle (to 19.5),
5 or 6 the top (to 22.5, the pot's shoulders). While lit, the same layers burning (`h<i>fire…`) in place
of them. Each layer is a square ring from the hole's walls to the pot's octagon, with corner fills, its
opening the pot's widest section in the layer, so at each layer's top the coke meets the pot with no gap.
With no pot in, the bed shows the pot's hollow down to the stand, as after a pull.

**The rest pose is the covers aside** (depth 0), so the viewer opens on open holes. The shapes are
authored there: a cover's place in the game is its hole's (0 aside, 1 shut).

### The period machine, and where the model departs from it

Huntsman's shops melted in holes set into the floor of the melting house, each about two feet square and
three deep, lined with firebrick or ganister, under a heavy cover flush with the floor, lifted aside to
charge and to pull the pot; under each, a grate of firebars over an ashpit, the cave, through which the
air came; the pot on a fireclay stand on the grate, packed round with coke to its shoulders; a flue near
the top of each hole to a shared flue and stack. The model keeps all of that. It departs where the game
needs it:

- **It stands as a block**, two high: the period's cellar is the cave inside it, open at the front. Set
  into a floor with a step down in front of the ash mouths, it is the period's shop; stood on the ground,
  the holes are worked from a step.
- **Each cover has a bay** behind its hole, so it is never left lying on the floor (and the machine's
  cells hold it).
- **One air port**, for the pack's forced air, into the shared cave.
- **The pot has no lid** (a crucible was charged under a fireclay lid), so the charge and the melt show.

### Regenerating

```sh
python3 mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run takes about 40 s and is deterministic (two runs into two folders are byte-identical, logs
included). It exits non-zero if a check fails (`tools/validate_cruciblefurnace.py`):

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **Frame:** joined to the ground as one piece (209 elements).
- **Textures by role:** brick, iron, clay, coke, ember, melt and charge where the table above puts them.
- **Containment:** nothing leaves the brickwork's cells with the covers aside or seated, or the brickwork
  and the four hollow cells over the whole motion (every 0.01 of depth); nothing over the floor at rest.
- **Covers:** flush in the bay at depth 0 and in the seat at depth 1; clear of the floor by 0.1 while
  carried; on the seat's brick ledge and the bay's plate; θ moves nothing, depth only the covers.
- **Pot:** on its stand on the grate, its mouth under the seated cover, 2 voxels of coke round its belly;
  the charge and the melt inside it, the charge's pieces under the cover.
- **Coke:** the layers stack from the grate to the shoulders, two coke a layer (six in all); at each
  layer's top the coke meets the pot with no gap; every layer whole across (sampled on a grid: each point
  coke or the pot's place); its top, lumps and all, under the flue openings; the burning layers the same
  boxes as the cold ones.
- **Draught:** sampled points from outside each ash mouth and the air port, through the cave, up through
  every gap of the grate, out at each flue opening, along the back flue and up the stack to the outlet, in
  no element of the frame; the stack and the outlet's mouth clear of every element at every depth; the
  firebars on both bearers, the bearers built into the walls; the bars' gaps at least 0.8.
- **Clearances:** in each of the scenario's seven states of the holes, no two parts overlap at rest
  (lumps of a coke layer inside the layer over them excepted), and the covers, carried every 0.02 of
  depth, touch nothing.
- **No z-fighting** at 25 poses (five states, five depths) with only what each state shows, after the
  fix (291 faces pressed against their own part removed, the rest stepped in by 0.015).
- **Files:** every texture code declared; glow on the burning coke and the melt only; no power cell;
  the hollow cells the covers' lift; lids on the brickwork's top cells at the floor; the outlet under the
  chimney's cell, which is not the furnace's; the chimney's line on the outlet; the shipped files are the
  checked model moved by a block.

`tools/tests/test_cruciblefurnace_model.py` holds the written files to these without the generator, and
`site/models.json`'s states to the generator's; `site/test/cruciblefurnace.test.ts` replays the reference
poses in the viewer's maths.

### Rig schema (`cruciblefurnace-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema") with nothing added to the
shared rig maths: two inputs, and parts gated by `requires`.

- **Inputs.** depth, a cover's place (0 aside, 1 seated); θ, the melter's clock, read at ratio 0. No
  work, class, presence, feed or oil.
- **Drivers.** Each cover: three `step` slides on depth (y +2.6 over 0..0.2, z −16 over 0.2..0.8, y
  −2.6 over 0.8..1), and on `h1cover` a `rotate` of ratio 0 first. Every other part has none.
- **`requires`** values: per hole *i* (1 west, 2 east) `h<i>pot`, `h<i>charge`, `h<i>melt`,
  `h<i>coke1..3` and `h<i>fire1..3`; the covers and the frame have none.
- **Keys:** `cells`, `hole1Cell`/`hole1Face`, `hole2Cell`/`hole2Face`, `outletCell`/`outletFace`,
  `airCell`/`airFace`, `chimney`, `mouth1`, `mouth2`, `furnace` and `parts`.

**In the viewer** (`site/models.json`, `crucible-furnace`): the "Covers on" slider is depth and moves both
covers; Play runs them: aside 2 s, on in 1.6 s, firing 3 s, aside in 1.6 s. Two selects, **Hole 1 (west)**
and **Hole 2 (east)**, give each hole's state (the scenario's `states`, one group a hole): cold and empty,
a pot on its stand, charged with the coke packed round, lit, melted, melted with the coke burnt low, and
the pot pulled with the fire still lit. The page opens with hole 1 melted and hole 2 charged, the covers
aside.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): per hole *i* `h<i>cover_*`
(`frame_*`, `fill_*`, `pocket`, `hook`), `h<i>pot_*` (`foot`, `lower`, `belly`, `upper`), `h<i>charge_*`,
`h<i>melt_*`, `h<i>coke<j>_*` and `h<i>fire<j>_*` (`n`, `s`, `w`, `e`, the corner fills `ne`..`sw`, the
lumps, the burning bottom layer's cinders); and `fr_*` for the frame (`fr_brick_NNN`, `fr_bearer`,
`fr_bar`, `fr_lintel`, `fr_plate`, `fr_curb`, `fr_flange`, `fr_sleeve`, `fr_corner`, `fr_kerb`,
`fr_stand`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a
standalone copy). No one has looked at it in a client.

1. **Texture seams.** The brickwork is 143 boxes, world-mapped by the mandrel station's reading of the
   game's face directions; that the brick runs on across them is not yet seen in the game.
2. **The glow is fixed**: 200 on the burning coke, 255 on the melt, whatever the temperature.
3. **The coke is flat-topped** but for a lump in each corner of each layer.
4. **The covers' path** is checked against the frame and the holes' contents; a player standing on the
   floor where a cover passes is gameplay's.
5. **Whole-cell boxes**: the cells' boxes are full cubes, and the lids make the top one floor.

## Open for the owner

1. **Two holes**, fixed (above, "Why two holes"), against four, or a choice of sizes as two machines.
2. **The covers go straight back into bays** behind the holes, lifted 2.6 and carried a block; the rest
   pose is aside. A hinged cover, or one slid aside on the floor in front, would change the back row.
3. **The furnace stands two blocks high**, the cave inside it with its mouths at the front: to be set into
   a floor (the period's shop) or stood on the ground and worked from a step. Its top is the melting floor.
4. **The chimney's height**: today `MinChimneyHeight` (6) counts from the holes' level. The chimney now
   stands on the outlet, the furnace's top, a block above the holes: 6 blocks on the outlet (as the
   `chimney` line draws), or 5 with the stack's base counted.
5. **Forced air leaks** at the open ash mouths. Ash doors (a part per mouth, shut for forced air) would
   seal the cave; none are drawn.
6. **Coke in three layers of two**, each drawn cold or burning, and the bed's pot-shaped hollow when there
   is no pot.
7. **The glow scaled to the temperature** (#753) is the renderer's: the model's glow values are fixed.
8. **No pot lid**, so the charge and the melt show.
9. **The iron binding** (corner angles, the band round the top) is the model's own addition to the brief.
10. **The charge** is drawn as a few iron and ferrochrome pieces in one texture code, whatever the recipe.
11. **Whole-cell boxes** with a lid on every top cell: no falling into a hole, and no clicking a single
    part inside one.
12. **What an existing world's holes become** once the furnace is one machine (#753 asks it of the grid
    recipe too).
13. **The build stages**, to be worked out once the finished furnace is settled.

## Wiring it into the game

A follow-up, once the model is settled. It will take:

- **Blocks**: a controller `seraphhorizons:cruciblefurnace-frame-{side}` drawing `cruciblefurnace_frame.json`
  (its textures every code of both shapes), a ghost for the other brickwork cells and one for the four
  hollow cells, placed from the rig's cells by the player's look (`Footprint.PlacedFacing`), as the press
  brake and the mandrel station are; selection boxes from the cells, collision with their lids.
- **One block entity for both holes**: today's `BEMeltingHole` state (pot, heats, charge, melt, coke,
  temperature, lit, cover) kept per hole in the controller, a click routed to a hole by its cell
  (`hole1Cell`, `hole2Cell` and the cells under them); `FurnaceRow` replaced by the fixed footprint and a
  check of the chimney over `outletCell` (`MinChimneyHeight` blocks of `ChimneyBlocks`), shared by both
  holes; forced air from a pipe in the cell south of `airCell`, through exlib's pipe node as today.
- **A renderer** on `Machines/Core` (`RigAnimation`) drawing each cover posed with its own hole's cover
  place as depth (eased), and each hole's `requires` parts from its state (coke layers by
  `ceil(coke / cokePerLayer)`, `fire` in place of `coke` while lit), `melt` and `charge` textures set from
  the pot's contents, the glow from the temperature; flames, light and sound at `mouth<i>` while open,
  smoke at the chimney's top.
- **Recipes and data**: the frame's recipe (refractory bricks and iron) and its stages; the recipe
  browser's multiblock data for the new block; retiring `meltinghole` and its grid recipe; the handbook.
- **Tests**: the rig and its reference poses replayed in C#, as the mandrel station's are; Atlas
  scenarios for placement, the two holes and the chimney.
