# Liquation furnace: the model

Part of the Seraph Horizons mod (`../README.md`): the generated model of the liquation furnace (model #930, behaviour
#929), the ore line's parting step for teallite's and franckeite's lead: tin and lead parted by heat, the tin sweating
out at 232 °C and running off, the lead (327 °C) staying behind. It is the machine form of the clay liquation pan
(`Ore/Processing/BlockLiquationPan*.cs`, "Liquation" in the mod README), and replaces it at 95 % against the pan's
85 % (#929). Part of the ore processing epic #684; the line, the glossary and the sources are in #939.

**Status: the model only.** This folder holds the generator and its checks (`tools/`). There is no block, block
entity or renderer yet: the model and its rig are what those will be built against (#929), on the crucible furnace's
pattern (`../CrucibleFurnace/`), whose conventions every furnace of the line follows.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, unchanged by this one: no new driver and no change to the package.

## The furnace

A liquation (sweating) furnace of the period: a **cast-iron hearth plate falling to a spout, under a low vault, over a
fire** (#929; Hofman's crust liquation used a 10 × 5 ft plate, Hofman 1904, pp. 453–466). The model is one such
furnace as one block of brickwork **two blocks wide (x), two deep (z) and two high**, on the crucible furnace's
conventions: the **brickwork** (the base, the outer walls, the niche, the ash pit and the ash mouth, the brick over
the vault) is **any one type of the game's fired brick**, and the walls take that brick's texture ("The brick",
below); the **hot faces are lined** with tier-3 refractory brick 3 voxels thick, the vault with them; it is **bound
with angle iron** in one frame ("The binding", below), its side faces plain brick but for the angles' legs, flush,
so furnaces stand in a row; it is fired on **natural
draught only**, through an **ash mouth with a three-place damper**; and its smoke leaves by an **outlet on the top**,
where the player stands the chimney, or which a flat iron plate caps. Its top is flat, a deck.

- **The front: the working opening.** The plate's low end, under a segmental **barrel vault** of refractory open at
  the front (span 16, rise 4, eight staves 3 thick, their front ends the arch's ring), its springing 21 voxels up. The
  cakes go on and the lead comes off from in front, through the opening. Under the plate's low end is a **niche** the
  chamber's width (16), 11 high, open to the front and, in front of the plate's low end, above: there the **receiving
  pot** stands on the niche's floor, a cast-iron kettle 7.9 across and 6.6 tall, as wide as the niche's depth allows.
- **The plate** is one cast-iron slab 16 wide, from its low end 5 voxels behind the front face to its high end 24
  behind it, falling to the front at one in five (11.3°): its top at 13 voxels at the low end, 16.8 at the high end.
  Its edges run against the side walls' lining; it lies on an **iron cross bearer under its low end** (over the
  niche), on **the front wall's top** (between the niche and the firebox) and on **a second bearer under its high
  end** (over the firebox), each touching its underside; the bearers' ends are built into the side walls. **Raised
  rims** run along both sides and across the high end, and across the low end but for a gap in the middle, where the
  **spout** leaves: a short open iron trough, steeper than the plate (25°), from the low end forward to its **lip**,
  over the pot's mouth. The tin runs down the plate's middle, off through the spout and falls into the pot.
- **The charge**: four **cakes** of tin-lead (5.8 by 4 and 1.8 thick) on the plate's upper part, two by two between its
  rims, over the fire, 2 voxels under the hanging wall's foot. Sweated, the **lead** is left where each cake lay, a
  flattened, spongy lump, smaller than the cake, with a knob of it standing up; the **tin** runs and fills the pot.
- **The fire**: under the plate, a **grate** of iron firebars (six rows of 1.2, two lengths a row, gaps of 0.96) on three
  iron bearers running front to back, their ends built into the front and back walls, over an **ash pit** 4 high. The
  firebox runs from the front wall (5 thick: 2 of brick facing the niche, 3 of lining) to the back wall (6 thick), 12
  deep; the plate's high end is over its back half, so the cakes lie over the fire and the tin runs down to the cooler
  low end.
- **The back: the fire door and the ash mouth**, both centred in the back face. The **fire hole** (8 wide, 5 high, its
  sill the grate's top) has iron jambs from the ground and an iron lintel; the **fire door**, a cast-iron plate with a
  handle, **slides up in iron guides** in a shallow recess, open clear of the hole (the rest pose) or shut over it.
  Under it the **ash mouth** (6 wide, 3.5 high, under an iron lintel) and its **damper**, an iron plate with a knob that
  **slides sideways** between two iron rails in its own recess: **open** (east of the mouth), **half** (over its east
  half) or **shut** (over all of it). A sliding door and a sideways damper keep everything that moves inside the
  furnace's two blocks: a hinged door would swing beyond the back face, and a damper sliding up would cross the fire
  hole.
- **The draught.** The fire's air comes in at the ash mouth, up through the grate and the fire, along the plate's
  underside up to its high end and up behind it into the **throat**. The chamber's air comes in at the working opening,
  over the plate and under the **hanging wall** (the chamber's back wall, 3 of lining from 17 to 20 behind the front
  face, its foot 20 voxels up, over the plate's upper part) into the throat. From the throat the **stack** rises to
  the **outlet**: as the crucible furnace's, a **square flue 8 by 8 centred under the chimney's line**, the middle of
  the outlet's cell, from the hanging wall's foot (where it opens off the throat's top and its east side) to the top,
  so the chimney the player stands on the outlet sits square over its mouth. **The model stops at the
  outlet**: the player stands the chimney on it ("The chimney", below). The outlet has two states, open or **capped**
  with a flat iron plate (9 square) set flush in its **curb** (the lining's refractory, 11 square, centred on the
  mouth: a rim 1 wide round a seat half a voxel down, the seat a ledge 0.5 wide round the mouth), as every other
  furnace in a row sharing one chimney is. The curb meets the back and east top bands.
- **The outlet is in the back row's east cell**, centred in it, not on the furnace: a chimney stands on one cell, and a
  two-wide furnace has no middle cell. Everything else is centred left to right.
- **The binding** is **angle iron** (an L section, 1 voxel thick, legs 2.5 wide), set flush into the brick so nothing
  stands proud of the furnace's box, and **one frame**: an **angle up each of the four vertical corners**, wrapping it,
  one leg on the front or back face and one on the side face, its inside corner bedded against the brick's corner, so
  it holds the brick in on both faces; and along **each of the four top edges an angle band** wrapping that edge, one
  leg down the face between the corner angles and one on the top. The four bands' top legs meet at the top corners, a
  ring, and each band's face leg meets the corner angles at both its ends, so every member is joined to every other.
- **Furnaces stand in a row**, side by side along their width (x), up to five, sharing walls: the side faces are flush,
  plain brickwork but for the corner angles' side legs and the side bands' legs along the top (flush), with nothing
  beyond the footprint; two furnaces' angles meet face to face there. The flues between them
  are implied, not modelled.

**Footprint and cells.** The furnace's 8 cells, all solid (no hollow cell: nothing moves outside the brickwork). The
cell over the outlet, `[1,2,1]`, is the chimney's, and its neighbour `[0,2,1]` is free: neither is the furnace's. The
**controller is the front west cell at ground level**, `[0,0,0]`, half of the niche's front: a two-wide furnace has no
front middle cell. The furnace extends a block east (x) and a block back (z) from it.

**Why 2 × 2 × 2** (the issue's "about 2 × 2 × 2", held to the crucible furnace's standard, dimension by dimension):

- **Two wide**: a chamber and firebox 16 wide (a plate 16 wide, four cakes two by two between its rims) with side walls
  of 8 (5 of brick, 3 of lining) is 32 voxels, two blocks. The stack (8 square, centred in the east cell) has 3 of
  lining and 1 of brick outside it to the east face (and the same to the back).
- **Two deep**: the niche and the pot (9), the front wall (5), the firebox (12) and the back wall (6) are 32. The plate
  runs from 5 behind the front face over the niche and the front wall to over the firebox's back half; the throat
  (from the hanging wall at 20 to the back wall at 26) and the stack (20 to 28) rise over the firebox's back end and
  the back wall, so they cost no depth of their own. The vault runs from the front face to 17, the hanging wall 17 to
  20: shorter than the plate, so the square stack, centred in the back cell, clears the chamber by the hanging wall's 3.
- **Two high**: the ash pit (4) and the grate (3) under the plate, the plate falling from 16.8 to 13, a chamber over it
  to the vault's springing at 21 and its crown at 25, the vault's lining (3, its backing to 28.25) and the brick over it: 32.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `plate`, `plateSide` | `[1,0.9613,1.0562]`, `north` | The middle of the cakes' place on the plate's top: charged and raked from the front, through the opening |
| `pot` | `[1,0.475,0.2812]` | The middle of the receiving pot's mouth: the tin taken out |
| `spout` | `[1,0.7542,0.1875]` | The spout's lip, over the pot |
| `opening` | `[1,1.1875,0]` | The middle of the working opening in the front face: the chamber's light and fumes |
| `fireDoor`, `fireDoorSide` | `[1,0.6562,2]`, `south` | The middle of the fire hole in the back face: the fuel goes in (the stoker's) |
| `ash`, `ashSide` | `[1,0.1719,2]`, `south` | The middle of the ash mouth in the back face: the damper, worked from behind |
| `outletCell`, `outletFace` | `[1,1,1]`, `up` | The stack's top cell, in the back row's east cell; the chimney (or the cap) on its top, the chimney from `[1,2,1]` up, centred over the stack's square mouth |
| `chimney` | origin `[1.5,5,1.5]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `build` | | The build's stages and their materials ("The build", below); not drawn |
| `bricks` | | The fired brick types and their textures (the crucible furnace's list); not drawn |
| `furnace` | | `fuelLayers` 2, `cakes` 4, `minChimney` 6, `rowMax` 5, `rowAxis` `x`, `doorTravel` 0.375, `plateSlope` 0.2; not drawn |

**Decks, boxes.** Each of the four top cells has a `lid` (a collision deck) at the top. The cells' boxes are built from
the furnace as built (the iron, the brickwork, the lining, the plate, the pot, the door shut); the box splitter makes
every cell one whole box, the front cells' too, so the working opening and the niche are closed to collision (the
crucible furnace's hole is the same). Clicks on the plate, the pot, the fire door and the ash mouth are gameplay's, by
the anchors.

## The build

Four stages, after the crucible furnace's: an iron frame by grid recipe, then the brickwork, the lining and the
fittings, each a `requires` value of its parts but the frame's (the rig's `build`, in order). **The brickwork's and
the lining's counts are measured from the model; the frame's recipe and the fittings' materials are proposals** for
the owner to settle ("Open for the owner").

| | Stage | `requires` | What it puts in | Materials |
|---|---|---|---|---|
| Frame | **Iron frame**: a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (the frame part) | What stands on its own: the angle-iron binding, one frame (the four corner angles and the ring of four top bands), the grate's bearers and firebars, the plate's two cross bearers, the fire hole's jambs and lintel, the ash mouth's lintel, the fire door's guides and the damper's rails, on 7 short posts hidden in the brickwork | proposed: 1 iron plate, 4 iron rods, 8 iron nails and strips |
| 1 | **Brickwork** | `brickwork` | The base, the outer walls, the niche, the ash pit and the ash mouth, the brick over the vault: 3.58 blocks | **8 fired bricks**, any one type (`game:burnedbrick-*`) |
| 2 | **Lining** | `lining` | Tier-3 refractory 3 thick on the hot faces (the firebox, the chamber's side walls, the hanging wall, the throat, the stack, the outlet's curb) and the vault with its backing: 1.83 blocks (the vault 0.48) | **8 tier-3 refractory bricks**, **4 fire clay** |
| 3 | **Fittings** | `fittings` (the parts `plate`, `pot`, `door`; and the damper's place) | The hearth plate with its rims and spout, the receiving pot, the fire door, the damper | proposed: 4 iron plates |
| | *Outlet cap* | `outletcap` | Not a stage: put on (or taken off) by hand, any time after the lining | 1 iron plate |

**The counts are measured from the model**, as the crucible furnace's are: the brick is a lattice (every void's faces,
the lining's zones' and the blocks' boundaries along each axis) merged into boxes, the brickwork's and the lining's
apart; the vault's zone (its staves and their backing) is its box less the chamber's segment under the arc. At the
game's rates (2 fired bricks a block, 4 refractory bricks a block, rounded up, fire clay one per 2.5 refractory
bricks): 7.16 → 8 fired bricks, 7.32 → 8 refractory bricks, 4 fire clay.

**The ties.** So that nothing floats before the brick is in, the least iron joins the frame to the ground, all of it
inside the brickwork: a post under the back end of each outer grate bearer and under the front end of the middle one
(the ash mouth is under its back end), each half a voxel clear of the ash pit, and a post under each end of each of the
plate's bearers, in the side walls. The fire hole's jambs stand on the ground and carry its lintel, the ash mouth's
lintel, the door's guides and the damper's rails; they are seen, as the hole's iron frame.

### The brick

**Any fired brick**, as the crucible furnace's: the brickwork takes any `game:burnedbrick-*`, the first brick put in
sets the type, the rest must match, and the walls take that type's block texture, which the renderer sets the shape's
`brick` code to from the rig's `bricks` (the crucible furnace's list exactly, every type of the game's `burnedbrick`;
a test holds the two the same). In the viewer, a Brick select with the crucible furnace's colours.

### No power, no forced air

Fired by hand on **natural draught only**: no `powerCell` or `powerFace` and no air port (the validator fails if either
appears). As with the crucible furnace, θ is a clock that moves nothing: a `rotate` of ratio 0 on the fire door
carries it, so the viewer offers Play, which opens and shuts the door.

### The chimney

As the crucible furnace's: counted **from the outlet up**, at least **six shaft blocks** stood on the outlet (the
crucible furnace's `MinChimneyHeight`; the rig's `furnace.minChimney`), topped by one `ChimneyTopBlocks` block, which
does not count. The viewer's `chimney` line draws the six shaft blocks from the outlet (y 2 to 8).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. **No new texture**: every texture is the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/liquationfurnace.json` | The whole furnace, every part (444 elements) |
| `assets/seraphhorizons/shapes/block/liquationfurnace_frame.json` | The iron frame only (66 elements): what the placed frame draws, before any stage |
| `assets/seraphhorizons/config/liquationfurnace-rig.json` | Cells, anchors, the build and the brick types, and the part rig (18 parts) |
| `tests/LiquationFurnace/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

| Code | Texture | On |
|---|---|---|
| `brick` | The furnace's brick type's block texture, set by the renderer (`bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork: world-mapped, so the many boxes read as one wall |
| `lining` | `game:block/clay/refractory/tier3/front1` (the crucible furnace's) | The lining, the vault's staves and backing, the curb: world-mapped (the staves, turned, mapped to their size) |
| `iron` | `game:block/metal/plate/iron` | The frame, the hearth plate and its spout, the pot, the fire door, the damper, the cap |
| `cake` | `game:block/metal/ingot/leadsolder` (the game's solder is tin and lead) | The tin-lead cakes |
| `tin` | `game:block/metal/ingot/tin` | The tin running and the tin in the pot |
| `lead` | `game:block/metal/ingot/lead` | The lead left on the plate |
| `fuel` | `game:block/coal/charcoal` | The fuel on the grate |
| `ember` | `game:block/coal/ember` | The same fuel, burning; its faces glow 200 (the crucible furnace's coke) |

**Nothing else glows**: the tin and the lead, at 232 to 327 °C, are far under the game's glow, which starts at 500 °C
(the viewer's `hotGlow`, vanilla's rule).

### How the furnace works

Nothing turns. The fire door is the only part that moves; everything else that shows or not is a `requires`,
gameplay's state.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The iron frame (`frame`, no requires) | fixed: placed as the machine | carries the grate and the plate's bearers; its binding holds the brick; its ties hold it up before the brick is in |
| The brickwork (`brickwork`) | stage 1 | the base, the outer walls, the niche, the ash pit and the ash mouth; plain brick on the side faces for a row |
| The lining (`lining`) | stage 2 | the hot faces: the firebox, the chamber's side walls, the hanging wall, the throat, the stack, the curb; the vault |
| The grate's bearers and firebars (`frame`) | fixed: the bearers built into the front and back walls, the bars across them | carry the fuel; the draught comes up between the bars |
| The plate's bearers (`frame`) | fixed: their ends built into the side walls | carry the plate at its low end and its high end |
| The hearth plate, its rims and spout (`plate`) | stage 3 (fittings), laid on its bearers and the front wall's top | carries the cakes over the fire; its fall runs the tin to the low end, off through the spout into the pot |
| The receiving pot (`pot`) | stage 3 (fittings), on the niche's floor | takes the tin from the spout |
| The fire door (`door`) | stage 3 (fittings); then depth: slid up its guides from shut (1) to open (0, the rest pose), by the stoker | closes the fire hole: the fire then draws only up through the grate |
| The cakes (`cakes`) | charged, by hand from the front | sweat their tin out over the fire |
| The tin running (`tinrun`) | the charge sweating | the stream down the plate's middle, the spout and into the pot |
| The tin (`tin`) | the tin run into the pot | taken out by hand |
| The lead (`lead`) | the charge sweated | stays on the plate, raked off from the front (or melted off with more heat) |
| The fuel, two layers (`fuel1`, `fuel2`) | stoked through the fire door | the fire |
| The fuel burning (`fire1`, `fire2`) | lit: the same layers in their burning texture, glowing; the bottom one also drops cinders on the ash pit's floor | heats the plate from under it |
| The damper (`damperopen`, `damperhalf`, `dampershut`) | fitted with the fittings; then one of its three places | sets the draught |
| The cap (`outletcap`) | fitted on every furnace in a row but the one carrying the chimney | closes the outlet |
| The draught (no part) | the chimney on the outlet, natural draught only | the fire's: in at the ash mouth, up through the grate, under the plate to its high end, into the throat; the chamber's: in at the opening, over the plate, under the hanging wall into the throat; both up the stack |

### Regenerating

```sh
python3 mods-src/seraphhorizons/LiquationFurnace/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/LiquationFurnace/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/LiquationFurnace/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run takes about a minute and a half and is deterministic (two runs into two folders are byte-identical). It
exits non-zero if a check fails (`tools/validate_liquationfurnace.py`). The brick types are checked against the game's
files when they are at hand (`$VINTAGE_STORY`, else `~/Games/vintagestory`); without them that check says it was
skipped. Most checks are the crucible furnace's:

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **The stages**: at each stage (the iron frame alone, with the brickwork, with the lining, with the fittings) nothing
  floats; the iron frame is iron only, with no thin post over half the furnace (its tallest, a plate bearer's post,
  14.2); every tie is hidden in the brickwork.
- **Textures by role**; **containment** in the eight cells over the door's travel; **a flat top** but for the outlet;
  **a row**: plain brick side faces but for the corner angles' and side bands' flush legs, nothing else reaching them, two furnaces
  side by side touching only at their walls.
- **The binding** *(owner's rule)*: every corner binder and top band is an angle of two legs at most 1 thick, 2 to 3
  wide, meeting along its corner, flush with the furnace's two faces there, each leg's inside face bedded on brick its
  whole length (2820 points); each band touches the corner angles at both its ends and the bands beside it at the top
  corners; every piece of the binding is joined to every other (one frame, 32 pieces); no flat strap.
- **The walls**: everything centred left to right but the outlet, whose stack's mouth lies in the outlet's cell; the
  wall round the fire and the chamber at least `MIN_WALL` (5): 8 at the sides, 5 at the front (to the niche), 6 at the
  back outside the fire hole, the ash mouth and their recesses.
- **The lining**: behind every hot face at least 3 of the lining's brick; the niche, the ash pit and the ash mouth the
  brickwork's.
- **The vault**: its staves in their zone; solid lining over the arc up to the brickwork, open under it.
- **The stages' counts**: measured from the lattice and the vault's zone, as the rig's.
- **The plate**: its top and underside where the slope puts them, falling to the front, wall to wall, its edges
  against the lining, touching its three supports exactly; the rims on it; the spout's lip over the pot's mouth; the
  cakes between the rims, over the firebox, at least a voxel under the hanging wall's foot and the springing; the lead
  where they lay; the tin's run from under the cakes to the spout, its stream from the lip to the tin in the pot.
- **The pot**: on the niche's floor, in the niche, its rim at least a voxel under the plate's front bearer; the tin in
  it at least a voxel under the rim.
- **The fuel**: its layers stack from the grate, across it, at least a voxel under the plate; the burning ones the same
  boxes.
- **The fire door**: in its recess between its guides over its travel; shut over the fire hole, open clear of it; θ
  moves nothing, depth only the door.
- **The damper**: in each place between its rails, spanning the mouth's height; shut over the whole mouth, half over
  its east half, open clear of it.
- **The outlet** *(owner's review)*: the flue measured from the elements just under the cap's seat and down the
  stack is **square, 8 by 8 (the crucible furnace's), and centred under the chimney's line**, which is the middle of
  the outlet's cell; the curb square and centred with it; the cap square, flush in the curb's seat over the mouth;
  the curb the lining's (rim and ledge every half voxel).
- **The draught**: both ways sampled point by point, clear with the door shut; the stack's mouth clear at every depth
  but for the cap; every firebar on a bearer at each end, the bearers built into the walls, the gaps at least 0.8.
- **Clearances** in fourteen scenes, the door slid every 0.02: no two parts overlap (a fuel layer's lumps in the layer
  over them excepted).
- **No z-fighting** at 30 poses (ten scenes, the build's stages among them, three depths each), after the fix.
- **Files**: the brick types against the game's, every texture declared, glow on the burning fuel only, the frame's
  shape iron only, no power cell or air port, the cells and the decks, the outlet under the chimney's cell in the back
  row's east cell, the chimney's line and the stack's mouth both centred on the outlet's cell, the anchors (the pot,
  the spout and the opening at the front, the fire door and the ash mouth in the back face's middle).

`tools/tests/test_liquationfurnace_model.py` holds the written files to these without the generator, and
`site/models.json`'s states and Play to the generator's; `site/test/liquationfurnace.test.ts` replays the reference
poses in the viewer's maths and holds the Brick select to the rig's types (and to the crucible furnace's colours).

### Rig schema (`liquationfurnace-rig.json`)

The crucible furnace's format with nothing added to the shared rig maths: two inputs, and parts gated by `requires`.

- **Inputs.** depth, the fire door (0 open, 1 shut); θ, the stoker's clock, read at ratio 0. No work, class, presence,
  feed or oil.
- **Drivers.** The fire door: a `rotate` of ratio 0 (θ), then a `step` slide along y, −0.375 blocks over depth 0..1
  (from open to shut). Every other part has none.
- **`requires`** values: the stages, `brickwork`, `lining` and `fittings` (the parts `plate`, `pot` and `door`);
  `cakes`, `tinrun`, `tin`, `lead`; `fuel1`, `fuel2`, `fire1`, `fire2`; `damperopen`, `damperhalf`, `dampershut` (one at
  a time) and `outletcap`; the iron frame has none.
- **Keys:** `cells`, `plate`/`plateSide`, `pot`, `spout`, `opening`, `fireDoor`/`fireDoorSide`, `ash`/`ashSide`,
  `outletCell`/`outletFace`, `chimney`, `build`, `bricks`, `furnace` and `parts`.
- **`build`**: the crucible furnace's: `frame` (`recipe` grid, `tool` hammer, `ingredients`), `stages` in order
  (`requires`, `materials`, the brickwork's and the lining's `volumeBlocks` and `perClick` `stack`, the brickwork's
  `oneType`, the fittings' `parts` (the parts it fits) and `also` (the damper's places it fits one of)), `outletCap`.

**In the viewer** (`site/models.json`, `liquation-furnace`): the "Fire door" slider is depth, and Play opens and shuts
it (open 2 s, shut in 1 s, sweating 3 s, open in 1 s). Five selects of states: **Build** (the iron frame, 1: brickwork,
2: lining and vault, 3: fittings, complete; each with its materials), **The charge** (empty, charged, sweating, sweated,
the tin taken), **The fire** (no fire, laid, lit, burnt low), **Ash-mouth damper** (open, half, shut) and **Outlet**
(open or capped); the first four after the build wait for the fittings, the outlet for the lining. The page opens with
the furnace built, the charge sweating, the fire lit, the door open, the damper open and the outlet open. A **Brick**
select, as the crucible furnace's.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`ln_*` the lining (`ln_brick_NNN`, the vault's staves `ln_vault<i>_<j>`, their backing `ln_backing<nn>_<j>`),
`plate_*` (`slab`, `rim_w`, `rim_e`, `rim_high`, `rim_loww`, `rim_lowe`, `spout`, `spout_w`, `spout_e`), `pot_*`
(`foot`, `lower`, `body`), `door_*` (`plate`, `handle`), `cakes_<n>`, `tinrun_*` (`plate`, `spout`, `stream`),
`tin_pool`, `lead_<n>` and `lead_knob<n>`, `fuel<j>_*` and `fire<j>_*` (`bedw`, `bede`, the lumps, the burning bottom
layer's cinders), `damperopen_*`, `damperhalf_*`, `dampershut_*` (`plate`, `knob`), `outletcap_*`; and `fr_*` the iron
frame (`fr_bearer`, `fr_bar`, `fr_platebearer`, `fr_jamb`, `fr_doorlintel`, `fr_ashlintel`, `fr_guide`, `fr_rail`,
`fr_angle_<n|s><w|e>_face`/`_side`, `fr_band_<n|s|w|e>_face`/`_top`, `fr_tie_*`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or
stop regenerating.

## Salvage

From `origin/741-parting-furnace-model-v2`'s `build_liquation`, as its evaluation advised (adapt, rescale, drop
`liq_world`): the **mechanism** is the v2 furnace's (a brick base over a fire, an inclined cast-iron hearth plate with
raised rims, cakes on its slope, a low vault open at the low end, a spout out of the open end into a receiving pot),
**rebuilt in its own frame** (no quarter turn: the plate falls to the front) on the crucible furnace's generator: its
box and lattice helpers, world-mapped brick, lining zones, ties (the binding is angle iron here, not its flat straps), the damper, the cap and curb, the build's
counts, the brick types, the shipped cells, the coplanar fix. Changed from v2:

- **The fire door** from the side to the **back** (a row's sides are shared); with it the ash mouth and its damper.
- **The plate's support**: v2 laid it on fire-brick steps over a firebox in the base; here the fire is **under the
  plate**, which lies on two iron bearers and the front wall's top.
- **The vault**: v2's staves (along x, `vault()`), here along z over the chamber, with a backing of strips filling up to
  the brickwork, so the front face shows a refractory arch ring flush in its surround.
- **The pot**: v2's small octagon on the floor is now a kettle in a niche under the plate's low end.
- **Dropped**: v2's shared floor and stack, `liq_world`, its fixed brick textures (`eight/running/orange1`, `cream1`),
  `TEX=64` proportional UVs, and its dross (the lead left on the plate takes its place). Never ported: commit `9fa58a5`
  (per-cell `fitted` collision).

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a standalone
copy). No one has looked at it in a client.

1. **Texture seams.** The brick is 270 boxes (159 of brickwork, 71 of lining, 40 of the vault's backing), world-mapped;
   the vault's 16 staves are mapped to their own size.
   That the brick runs on across them is not yet seen in the game.
2. **Small slivers under the plate**: a cross bearer's top touches the plate's underside along its front edge; behind
   it the slope opens a gap of up to 0.3 (the front bearer, seen from the niche) and over the front wall's top one of
   up to 1.0 (facing the firebox, not seen).
3. **The glow is fixed**: 200 on the burning fuel, whatever the fire's heat (scaling it is the renderer's).
4. **Whole-cell boxes**: every cell's box is the whole cell, the front ones too; the decks make the top one floor.
5. **The pot is drawn full** (its tin 1.1 under the rim), whatever is in it; it holds about 150 units at the crucible
   furnace's 1.1 cubic voxels a unit (not checked: no capacity is settled, #929).
6. **The fuel is two flat layers** with lumps, as the crucible furnace's coke.
7. **The outlet is off the furnace's centre**, in the east cell (centred in that cell): a row of these furnaces carries
   its chimney on one furnace's east back cell. The square stack leaves 4 (3 of lining, 1 of brick) between it and the
   east and back faces (`MIN_WALL` is for the fire and the chamber; the stack is not checked against it).

## Open for the owner

1. **The fire door at the back** (#929 left front or back open). Chosen so the front is the metal's (the cakes on, the
   lead off, the tin out of the pot) and the back the fire's (fuel, ash, damper), and because a front door would sit
   under the plate's low end where the pot and the spout are. The front would need the pot and the spout off-centre or
   the fire at the plate's low end.
2. **A sliding fire door and a sideways damper**, so everything moving stays inside the two blocks (a hinged door
   would swing out of the footprint; a damper sliding up would cross the fire hole). Period furnaces had both kinds.
3. **The frame's recipe** (proposed: 1 iron plate, 4 iron rods, 8 iron nails and strips; the crucible furnace's is 2,
   4 and 10) and **the fittings' materials** (proposed: 4 iron plates for the hearth plate, the pot, the door and the
   damper). The brick counts are measured: 8 fired bricks, 8 tier-3 refractory bricks, 4 fire clay.
4. **The build's stages** themselves (frame, brickwork, lining, fittings), to settle as the crucible furnace's were.
5. **The charge's item** (#929's open question): the model draws **cakes of tin-lead** (the solder texture), as v2
   did; if the furnace takes roasted teallite and franckeite concentrate as the clay pan does, the cakes become a heap
   of concentrate on the plate.
6. **Thinner walls than the crucible furnace's**: its walls are at least 8 round the hole; here 8 at the sides, 5 at
   the front (to the niche: 2 of brick and 3 of lining, which keeps the pot warm) and 6 at the back. `MIN_WALL` is 5. A
   gentle fire (the tin point is 232 °C) and the two-deep footprint.
7. **The outlet in the east cell**, the only thing off-centre (a chimney stands on one cell); its square stack,
   centred in that cell, has 4 of wall to the east and back faces (3 of lining, 1 of brick).
8. **The fuel**: drawn as charcoal; the issue says only "fuel by hand". How much a layer holds is gameplay's.
9. **Provisional pending #711**: nothing of #711's service anchors applies (batch, no chutes, no water, no power); the
   furnace's anchors follow the crucible furnace's (`outletCell`/`outletFace`, `chimney`, `ash`/`ashSide`) plus the
   fire door, the plate, the pot, the spout and the opening.
