# Crucible furnace: the model

Part of the Seraph Horizons mod (`../README.md`, "Crucible furnace"): the generated model of the crucible furnace
as one machine (#753), placed as a multiblock with ghost cells like the pack's other machines, in place of today's
row of block-built melting holes. The game side is #796.

**Status: the model only.** This folder's `tools/` holds the model's generator and its checks. The game still uses
the melting holes, their pot and the chimney row (`Core/`, `Game/`, the mod README's "Crucible furnace"); nothing in
the game reads this model yet. What wiring it in will take is under "Wiring it into the game", below.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, shared with the other machines and unchanged by this one: no new driver and no change
to the package.

## The furnace

Strictly, a crucible furnace was **one hole**: a Sheffield melting shop was rows of them, each about two feet square
and three deep, its top level with the floor, closed by a **hinged lid of firebrick in an iron frame, with a
handle**, the pots on firebars over an ash pit worked from a cellar, each hole with its own flue to a stack that
served five or six holes in a line, fired on natural draught only, which a "cellar lad" worked at the ash-pit
opening; and by the late nineteenth century a hole held two crucibles (HMS Archaeology Datasheet 302; Historic
England's listings for Hillsborough and Sanderson Kayser's Darnall works; Sheffield Museums on Abbeydale). The model
is one such hole, as one block of brickwork **three blocks wide (x), three deep (z) and two high**, bound with iron on
its front and back. The brick is in **two roles**: the **brickwork** (the base, the ash pit and the ash mouth, the
thick outer walls) is **any one type of the game's fired brick**, and the walls take that brick's texture ("The
brick", below); the **hot faces are lined** with tier-3 refractory brick 3 voxels thick. **Its top is flat**, the
melting floor, broken only by the lid's seat and the outlet; the hinge's knuckles and pins, and the two stops behind
the lid, are all that stands on it.

- **The hole** is 28 voxels wide and 14 deep inside, **centred left to right** in the front two rows, with **thick
  walls**: 10 voxels of brick on each side (9.25 beside the lid's seat), 11 in front (10 behind the damper's recess,
  10.25 in front of the seat) and 23 behind, outside the flue's way. The inner 3 of them, from the grate's bearers up
  to the floor, are the **lining**; the seat's ledge round the hole's mouth is the lining's too. It holds **two pots
  side by side**, each on a **refractory stand** on the **grate** (seven rows of iron firebars, two lengths a row,
  lying on three iron bearers that run front to back and are built into the front and back walls), packed round in
  **one bed of coke** up to their shoulders: 2.7 of coke between a belly and a side wall, 5.4 between the two, 2.7
  before and behind.
- **Each pot** is a fireclay crucible, an octagonal barrel 8.6 across at the belly and 12 tall from its narrow foot,
  a little larger than the game's `meltingpot` block (8 by 11), as it holds 300 units ("A full pot", below): 600 a
  firing, teemed from both into one mould for #639's cylinder blank.
- **The lid** is tier-3 firebrick in an iron frame, 29.5 by 15.5 and 2 thick, flush with the floor when shut, with a
  pocket near its front edge and an iron handle bar across the pocket, under its top. It is **hinged at the back,
  about its own top back edge**: an iron **knuckle** at each back corner turns on an iron **pin** that runs on 1.5
  into the side wall, bedded in the brick. The knuckles lie in beds behind the seat that they fill (0.06 clear at the
  floor) and stand 0.45 over the floor, the pins 0.25. Hinged there, nothing of the lid passes behind its back edge
  under the floor as it turns, so **there is no hinge pit and no groove** behind the shut lid. The lid's front face
  is stepped back down its height (0.005 at the floor to 0.13 at its underside), so that it clears the seat's front
  wall: turning about its top back edge, the face's lower part swings forward first. It swings back 100° and **rests
  just past upright on two iron stops** behind it (1.5 wide, standing 1.6 over the floor), its back face on their top
  front edges, its weight 0.34 behind the axis (past 97.4° it falls back onto them by itself). Open, it stands over
  the middle row, 15.5 high and at most 2.7 behind the axis, out of the chimney's cell behind.
- **The ash pit** (the cave) runs under the grate across the hole's plan, 8 voxels high, open at the front through
  the **ash mouth**, centred on the front face (12 wide, 6 high, through the 11 of the front wall, an iron lintel over
  it): natural draught only, the air comes in there. Both are the brickwork's. Over the mouth the **damper**, an iron
  plate with a knob, slides in a shallow recess in the front face between iron guides, in three places: **open**
  (clear of the mouth), **half** (over its upper half) and **shut** (over all of it). There is no air port.
- **The flue.** Near the top of the hole's back wall, above the coke and under the lid, a **flue opening** (8 wide,
  3 high) leads into a flue in the back wall and up a **stack** to an **outlet** on the top **in the middle of the
  back row**, centred behind the hole and clear of the lid's swing and its stops; all of it is lined, 3 thick. The
  outlet's **curb** is the lining's brick, flush: a rim round a seat a voxel down, a ledge round the stack's mouth.
  **The model stops at the outlet**: the player stands the chimney on it ("The chimney", below). The outlet has
  **two states**: open, with the chimney on it, or **capped** with a flat iron plate set flush in the curb's seat, as
  every other furnace in a row sharing one chimney is.
- **Furnaces stand in a row**, up to six side by side along their width (x), sharing walls: the side faces are flush,
  plain brickwork with nothing beyond the footprint (the binding is on the front and back only), so two furnaces'
  side walls meet and touch nowhere else. A row of six is **18 blocks** long. The flues under the floor between them
  are implied, not modelled.

**Footprint and cells.** The brickwork's 18 cells, and the 6 cells over its front two rows, **hollow**: the lid
stands open over the middle row, and while it is under about 50° open its front edge sweeps the front row's. Cells
a moving part passes through are the machine's, with no box of their own (`docs/modeling/pitfalls.md`, "Hollow
cells"), so nothing is built where the lid swings; a player stands in them on the top's deck. The cell over the
outlet, `[0,2,2]`, is the chimney's, and its neighbours `[-1,2,2]` and `[1,2,2]` are free: none is the furnace's,
and nothing of it reaches them at any point of the lid's swing. The **controller is the front middle cell at ground
level**, `[0,0,0]`, the ash mouth's: the templates' convention (`docs/modeling/animated-machine-models.md`: "Make the
controller the block the player clicks: the sawmill's is the middle of the near end"), not the front-left cell of
the two-wide model. The furnace extends a block to each side (x) and two back (z) from it. Placed like the press
brake and the mandrel station, it extends away from the player along their line of sight: the hole, the ash mouth
and the damper face them, the stack and the outlet are behind.

**Why 3 × 3 × 2.** As small as two pots, walls of about ten voxels, the lid's swing and the outlet allow:

- **Three wide**: two pots side by side, with 2.7 of coke between each belly and its side wall and 5.4 between them,
  need a hole 28 wide; with side walls of 10 that is 48 voxels, three blocks. A row of six is 18 blocks long.
- **Three deep**: a front wall of 11, the hole's 14, then the back half of the middle row for the hinge, its stops
  and the open lid (to z 28.4), and the back row for the flue, the stack and the outlet, behind the lid's swing and
  centred behind the hole. The lid must open towards the back (the front is the melter's, the sides are the
  neighbours'); hinged at its top back edge, it stands open within the middle row's air, clear of the chimney on the
  outlet.
- **Two high**: the ash pit and the grate under a hole 18 deep from the grate to the lid; the period's cellar is
  folded into the block. Set into a floor with a step down in front of the ash mouth, it is the period's shop;
  stood on the ground, the hole is worked from a step.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `holeCell`, `holeFace` | `[0,1,1]`, `up` | The cell over the hole's middle, worked from its top: both pots stand under it |
| `pot1`, `pot2` | `[0.0625,1.625,1.125]`, `[0.9375,1.625,1.125]` | The middle of each pot's mouth, pot 1 west and pot 2 east: a click on the hole is the nearer pot's |
| `outletCell`, `outletFace` | `[0,1,2]`, `up` | The stack's top cell, in the back row's middle; the chimney (or the cap) on its top, the chimney from `[0,2,2]` up |
| `ash`, `ashSide` | `[0.5,0.25,0]`, `north` | The middle of the ash mouth in the controller's front face: the damper, worked from in front |
| `mouth` | `[0.5,2,1.125]` | The middle of the hole's mouth at the floor: flames, the glow's light and the fire's sound while the lid is open |
| `hinge` | origin `[0.5,2,1.6094]`, `x` | The lid's axis, its top back edge, drawn as a line |
| `chimney` | origin `[0.5,5,2.5]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `build` | | The build's stages and their materials ("The build", below); not drawn |
| `bricks` | | The fired brick types and their textures ("The brick", below); not drawn |
| `furnace` | | `pots` 2, `potUnits` 300, `cokeLayers` 3, `cokePerLayer` 2, `minChimney` 6, `rowMax` 6, `rowAxis` `x`, `lidOpenDegrees` 100; not drawn |

**Decks, boxes.** Each of the nine top cells of the brickwork has a `lid` (a collision deck) at the floor: a player
walks over the open hole and cannot fall in. The cells' boxes are built from the furnace as built (the iron, the
brickwork, the lining and the lid shut); the box splitter cannot leave a hole open in three boxes, so every cell's
box is the whole cell. The open lid has no box in the rig's cells: it moves, so its collision is gameplay's. The
knuckles and the stops stand in the hollow cells and have no box either.

## The build

Settled by the owner. Four stages, each a `requires` value of its parts but the frame's (the rig's `build`, in
order), and the viewer's **Build** select steps through them:

| | Stage | `requires` | What it puts in | Materials | Gear |
|---|---|---|---|---|---|
| Frame | **Iron frame**: a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (the frame part) | What stands on its own: the binding's corner straps and top bands, the grate's bearers and firebars, the ash mouth's lintel and the damper's guides, and 5 short posts under the bearers and the lintel, hidden in the brickwork (below) | 2 iron plates, 4 iron rods, **10 iron nails and strips** | 39.92 |
| 1 | **Brickwork** | `brickwork` (and the `hinge` part) | The base, the ash pit and the ash mouth, the outer walls: 12.34 blocks of brick; and with it, at no cost of its own, the hinge's pins and the lid's stops on their 8 ties | **25 fired bricks**, any one type (`game:burnedbrick-*`) | 8.50 (brown brick: 4.38) |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on the hot faces (the hole's walls and the seat's ledge, the flue and the stack), the outlet's curb and the pots' stands: 2.15 blocks | **9 tier-3 refractory bricks** (`game:refractorybrick-fired-tier3`), **4 fire clay** (`game:clay-fire`) | 9.00 |
| 3 | **Lid and damper** | `lid` (and the damper's place) | The lid, and the damper in its recess | 4 tier-3 refractory bricks, 2 iron plates | 19.37 |
| | *Outlet cap* | `outletcap` | Not a stage: put on (or taken off) by hand, any time after the lining | 1 iron plate | 7.71 |

A furnace costs **76.80** in gear (72.67 in brown brick), and each capped furnace in a row 7.71 more. Values from
`assets/seraphhorizons/config/item-values.json`: iron plate 7.712, iron rod 3.84, iron nails and strips 0.914, fired
brick 0.34 (brown 0.175), tier-3 refractory brick 0.987, fire clay 0.03.

**The counts are measured from the model.** The generator builds the brick as a lattice (every void's faces, the
lining's zones' and the blocks' boundaries along each axis) and merges it into boxes, the brickwork's and the
lining's apart; the stages' volumes are those boxes' volumes added up (each lattice box is in exactly one, which a
check holds), the stands' octagons with the lining's: 50,533 cubic voxels of brickwork (12.34 blocks) and 8,803 of
lining (2.15 blocks). At the game's rates, 2 fired bricks a block (8 bricks and a mortar make 4 `claybricks` blocks)
and 4 refractory bricks a block (8 and a mortar make 2), rounded up, that is 25 fired bricks (24.67) and 9 refractory
bricks (8.60); fire clay one per 2.5 refractory bricks, 4. The owner's estimate was about 24 and 10. The lid stage's
materials and the frame's recipe are as the owner settled them (the lid's firebrick alone measures 0.16 blocks).

**Brickwork and lining take a stack a click**: as many of the held stack as the stage still needs; the stage is
complete when all its materials are in. The hole's contents, the lid and the damper are drawn only once the furnace
is built, the cap only once the lining (its curb) is in.

**The ties.** So that nothing floats before the brick is in, the least iron joins each piece of iron to the ground,
all of it inside the brickwork (voids are cut for it), so it is hidden once the brick is in. The iron frame stands on
its own, on 5 short posts: one under the front end of each outer bearer and one under the back end of the middle one
(the ash mouth is under its front end), each half a voxel clear of the ash pit, and one each side of the ash mouth
under the lintel's ends, behind the damper's recess (the guides stand against the lintel's face). **The hinge's pins
and the lid's stops come with the brickwork** *(review)*, on their own ties (a bar on from each pin along the side
wall under the floor, a bar back from each stop under the floor, each on a post as tall as the furnace), so the frame
stage has no thin posts standing alone, and the brickwork's stage costs nothing more for them. Their posts are in the
brickwork; the bars reach into the lining's place, where the pins and the stops themselves are, so with the
brickwork alone in, they show there with what they hold.

### The brick

**Any fired brick.** The brickwork takes any `game:burnedbrick-*`. **The first brick put in sets the furnace's
brick type**; the rest of the stage must match, and a different type is refused with a message. The walls then take
that type's block texture: the renderer sets the shape's `brick` code to it from the rig's `bricks`. These are every
type the game's `burnedbrick` item has (1.22.7; there is no blue fired brick, though the recipes' skip lists still
name one), with the block their bricks make by the game's grid recipe and that block's first texture (the game picks
among a course's alternates; the furnace's walls take one). A type not in the list, another mod's, is refused.

| Type (`game:burnedbrick-…`) | Block its bricks make | `brick` texture (`game:block/clay/brick/four/running/…`) |
|---|---|---|
| `fire` | `claybricks-good-fire` | `fire1` |
| `black` | `brickcourse-four-running-black` | `cream1`, overlay `black1` |
| `brown` | `brickcourse-four-running-brown` | `cream1`, overlay `brown1` |
| `cream` | `brickcourse-four-running-cream` | `cream1` |
| `gray` | `brickcourse-four-running-gray` | `cream1`, overlay `gray1` |
| `orange` | `brickcourse-four-running-orange` | `cream1`, overlay `orange1` |
| `red` (the default) | `brickcourse-four-running-red` | `cream1`, overlay `red1` |
| `tan` | `brickcourse-four-running-tan` | `cream1`, overlay `tan1` |
| `clinker` | `brickcourse-four-running-clinker` (no recipe makes it: a ruin's) | `clinker1` |

The coloured courses are the cream brick with the colour drawn over it, a partly transparent overlay (alpha 0.64
to 1), so the renderer must draw the base with its overlays. A shape names one texture per code, so the shape's own
`brick` is the red brick's colour layer, `red1`, alone: never what the game should draw.

**In the viewer**, no Brick select: the viewer draws no texture images, only a flat colour per texture code (by the
material its code or path names), and nothing in a scenario can change a code's texture. The smallest change that
would give one, without a rig driver: a scenario key `textures`, a list of `{ "label", "code", "default",
"options": [{ "id", "label", "path", "colour" }] }`, shown as a select that sets the code's path (in the legend and
the picked element's details) and its colour in the texture colouring; in `site/src/lib/model-scenario.ts` (the type
and `checkScenario`: the code one of the shape's, the default one of the options), `site/src/lib/model-view.ts` (the
texture's path and colour from the pick) and `site/src/components/ModelPage.svelte` (the select). The furnace's entry
would then list the nine types, each with its colour.

### No power, no forced air

The furnace is fired with coke on **natural draught only**: no mechanical power (no `powerCell` or `powerFace`, no
power ghost) and no air port (the validator fails if either appears). `machinegen` assumes no power cell (the hand
stations, the press brake, the squaring shear and the mandrel station, have none), so nothing had to change. As
with them, the rig's θ is a clock that moves nothing: a `rotate` of ratio 0 on the lid carries it, so the viewer
offers Play, which opens and shuts the lid.

### The chimney

Counted **from the outlet up**; the furnace's own stack base inside the block does not count. At least **six chimney
blocks** (`MinChimneyHeight`, the rig's `furnace.minChimney`) stood on the outlet, so the chimney's top is at least 8
blocks over the ground the furnace stands on; each block above six draws harder, up to eleven (#796). A chimney is a
column of today's `ChimneyBlocks` (clay bricks, clay brick chimney, refractory bricks, stone bricks). smex's smoke
stack is not one here: it is a multiblock 3 × 3 across with its own course of bricks under its intake, and cannot
stand on the outlet. The viewer's `chimney` line draws the six from the outlet (y 2 to 8), and a check holds it there.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. **No new texture**: every texture is the game's (below). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/cruciblefurnace.json` | The whole furnace, every part (536 elements) |
| `assets/seraphhorizons/shapes/block/cruciblefurnace_frame.json` | The iron frame only (39 elements): what the placed frame draws, before any stage. The brick (with the hinge's iron), the lining and the lid are stages |
| `assets/seraphhorizons/config/cruciblefurnace-rig.json` | Cells, anchors, the build and the brick types, and the part rig (21 parts) |
| `tests/CrucibleFurnace/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

**Textures** (all the game's; the shape names them, a blocktype or the renderer may map them):

| Code | Texture | On |
|---|---|---|
| `brick` | The furnace's brick type's block texture, set by the renderer (`bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork: world-mapped, so the many boxes read as one wall |
| `lining` | `game:block/clay/refractory/tier3/front1` (the old melting hole's) | The lining, the curb, the stands, the lid's infill: world-mapped |
| `iron` | `game:block/metal/plate/iron` (the other machines' iron) | The iron frame (firebars, bearers, the lintel and the damper's guides, the binding, its ties), the hinge's pins and stops and their ties, the lid's frame, knuckles and handle, the damper, the cap |
| `clay` | `game:block/clay/hardened/fire` (the fired pot's) | The pots |
| `coke` | `game:block/coal/coke` | The coke |
| `ember` | `game:block/coal/ember` | The same coke, burning; its faces glow 200 (the game's lit firepit's embers glow 160) |
| `melt` | `game:block/metal/ingot/stainlesssteel` | The melts; glow 255. The renderer may set a pot's metal |
| `charge` | `game:block/metal/ingot/iron` | The charges' pieces; the renderer may set it |

### How the furnace works

Nothing turns. The lid is the only part that moves; everything else that shows or not is a `requires`, gameplay's
state.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The iron frame (`frame`, no requires) | fixed: placed as the machine | carries the grate; its binding holds the brick; its ties hold it up before the brick is in |
| The brickwork (`brickwork`) | stage 1 | the base, the ash pit and the ash mouth, the outer walls; plain brick on the side faces for a row |
| The lining (`lining`) | stage 2 | the hot faces: the hole's walls, the flue and the stack; the outlet's curb; the pots' stands |
| The bearers and firebars (`frame`) | fixed: the bearers built into the front and back walls, the bars lying across them | carry the stands and the coke; the draught comes up between the bars |
| The hinge: pins and stops (`hinge`, requires `brickwork`) | stage 1, with the brickwork, on their ties: the pins bedded in the side walls, the stops set into the floor behind the lid | carry the lid at its back corners through its knuckles; the stops hold it open |
| The lid, with its knuckles (`lid`) | stage 3; then depth: turned about its top back edge from open (0, 100° back, resting on the stops) to shut (1, flush in its seat). The melter, with tongs while it is too hot to touch | shuts the hole: the fire then draws only up through the grate, out at the flue opening |
| The pots (`pot1`, `pot2`) | put in, pulled: fitted or not, once the furnace is built | hold the charges, then the melts |
| The charges (`charge1`, `charge2`) | charged | fill each pot to 24.5 (a full pot, 300 units) with pieces standing up to 27.9, over the rim |
| The melts (`melt1`, `melt2`) | the charge melted | each pot's molten surface at 24.5, a full pot of 300 units, 1.5 under the rim, glowing |
| The coke, three layers (`coke1`..`3`) | the coke in the hole: two coke a layer, from the grate up | one bed packed round both pots to their shoulders, lumps of coke on each layer's top |
| The coke burning (`fire1`..`3`) | lit: the same layers in their burning texture, glowing; the bottom one also drops cinders on the cave's floor under the pots | the glow: from above when the lid is open, at the ash mouth and up through the bars when it is shut |
| The damper (`damperopen`, `damperhalf`, `dampershut`) | fitted with the lid (stage 3); then the cellar lad: one of its three places | sets the draught (#796: open full, half at half the heating, shut banked) |
| The cap (`outletcap`) | fitted on every furnace in a row but the one carrying the chimney | closes the outlet |
| The draught (no part) | the chimney on the outlet, natural draught only | in at the ash mouth, up through the grate and the coke, out at the flue opening, along the back flue, up the stack and the chimney |

**The coke's layers.** Gameplay's coke (0..`CokeCapacity`, 6 today) is drawn as its layers
`coke1..ceil(coke / cokePerLayer)`: the bottom layer from the grate to 16, the middle to 20, the top to 23.5, the
pots' shoulders. While lit, the same layers burning (`fire…`) in place of them. Each layer is one bed across the
hole, strips and corner fills round both pots' octagons, its openings the pots' widest section in the layer, so at
each layer's top the coke meets both pots with no gap. With no pots in, the bed shows their hollows down to the
stands, as after a pull.

**A full pot.** A pot holds 300 units (`PotCapacityUnits`, 200 before #768), and the model draws each full: its
melt's surface, and the top of its charge's fill, stand where its inside holds 300 units at 1.1 cubic voxels a unit
(`POT_UNITS`, `VOXELS_PER_UNIT`, `pot_level`): the first pass's figure, when a pot the size of the game's block held
200. For 300 the pot is 8.6 across the belly (foot 6.4, lower body and mouth 7.8) and 12 tall; the melt stands at
24.5, 1.5 under the rim, and the mouth is 4 under the shut lid. A part-filled pot is drawn full too.

### Regenerating

```sh
python3 mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/CrucibleFurnace/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run takes about three minutes and is deterministic (two runs into two folders are byte-identical, logs
included). It exits non-zero if a check fails (`tools/validate_cruciblefurnace.py`). The brick types are checked
against the game's files when they are at hand (`$VINTAGE_STORY`, else `~/Games/vintagestory`); without them that
check says it was skipped. Those marked *(review)* came from the owner's review:

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **The stages** *(review)*: at each stage (the iron frame alone, with the brickwork and the hinge's iron, with the
  lining) nothing floats: every element drawn is joined to the ground. The iron frame stands on its own: it is iron
  only, holds no pin or stop, and no thin post stands in it taller than half the furnace (a vertical element under
  1.5 across both ways; the corner straps are bands 2.5 wide; its tallest thin piece is a damper guide, 13.2). The
  ties are hidden once the brick is in (points just off every face of every tie lie in brick or other iron); the
  posts are in the brickwork, so only the bars by the pins and the stops show while the lining is not in.
- **Textures by role:** brick, lining, iron, clay, coke, ember, melt and charge where the table above puts them.
- **Containment:** nothing leaves the brickwork and the six cells over its front two rows over the lid's whole swing
  (every 0.01 of depth), so nothing reaches the chimney's cell or its neighbours; with the lid shut nothing stands
  over the floor but the hinge (the knuckles and the pins) and the stops, to at most the stops' 1.6, and with it open
  only the lid besides.
- **A flat top, no groove** *(review)*: every half voxel of the top, but the seat, the outlet's mouth and the hinge's
  beds, is brick or flush iron whose top is the floor (6868 points); of the frame only the pins and the stops stand
  above it. Along lines across the hinge, the lid's front edge and the seat's sides, every hundredth of a voxel just
  under the floor, the widest run in no element with the lid shut is at most 0.1 (it is 0.06, the knuckles' beds).
- **A row** *(review)*: each side face is plain brickwork over its whole area (12288 points over the two), nothing
  but the brickwork reaches either side plane at any depth, and two furnaces side by side touch nowhere but at their
  shared walls over the lid's whole swing.
- **Centred, thick walls** *(review)*: the hole, the ash mouth, the damper's recess, the stack and the outlet's curb
  are centred left to right; the brick (or iron set into it) round the hole and the seat, sampled every half voxel
  from the grate to the floor, runs at least `MIN_WALL` (8) out to the face: 9.25 west and east, 10 in front, 22.25
  behind outside the flue's way.
- **The lining** *(review)*: behind every hot face (the hole's walls from the bearers to the seat, every face of the
  flue opening, the back flue and the stack with brick behind it; 2434 points, walked out every quarter) the brick is
  the lining's for at least `LINING_T` (3), or to where the brick ends; the ash pit's and the ash mouth's faces are
  the brickwork's.
- **The stages' counts** *(review)*: the brick boxes' volumes add up to the lattice's, the brickwork's and the
  lining's apart, and give the rig's quantities at the game's rates, rounded up (25 fired bricks, 9 refractory bricks,
  4 fire clay); the lid stage's materials and the frame's recipe (2 iron plates, 4 iron rods, 10 iron nails and
  strips) are the settled ones; every stage's `requires` is a part's.
- **The lid** *(review)*: shut, flush in its seat, on the seat's ledge all round; every corner of it within its top
  front edge's distance of the axis, so it never swings into the seat's front wall; nothing of it but the knuckles
  behind its back edge under the floor at any depth (no pit); the pins through both knuckles at every depth and 1.5
  into each side wall, bedded in brick; open, 100° about the axis, its weight behind it, its back face on both stops'
  top front edges (to 1e-4), the stops behind the lid; θ moves nothing, depth only the lid.
- **The pots:** each on its stand on the grate (its foot covering the stand), its mouth under the shut lid, at least
  1.5 of coke round it, to the walls and between the two (2.7 and 5.4); the charges and the melts inside them, the
  charges' pieces under the lid.
- **A full pot** *(review)*: each pot's inside from its floor to its melt's top, measured from its walls' elements
  (the octagon inside their inner faces at each height, every 0.01), holds `POT_UNITS` (300) at `VOXELS_PER_UNIT`,
  within 2 %; the melt at least a voxel under the rim.
- **Coke:** the layers stack from the grate to the shoulders, two coke a layer (six in all); at each layer's top the
  coke meets both pots with no gap; every layer whole across (sampled on a grid: each point coke or a pot's place);
  its top, lumps and all, under the flue opening; the burning layers the same boxes as the cold ones.
- **The damper** *(review)*: in each place a plate in its recess behind the front face, between its guides; shut it
  covers the whole ash mouth, half its upper half (the mouth open below its middle), open none of it.
- **The outlet** *(review)*: open, clear to the sky through the stack; the cap flush with the floor in the curb's
  seat, over the stack's mouth; the curb the lining's brick, its rim round the cap and its ledge under the cap's edge
  (sampled every voxel).
- **Draught:** sampled points from outside the ash mouth, through the front wall and the cave, up through every gap
  of the grate, out at the flue opening, along the back flue and up the stack to the outlet, in no element of the
  brick or the frame; the flue opening, the back flue and the stack run into each other; the stack and the outlet's
  mouth clear of every element at every depth but the cap; every firebar on a bearer at each end, the bearers built
  into the walls; the bars' gaps at least 0.8.
- **Clearances:** in each of thirteen scenes (the seven states of the pots, the damper's three places with the cap on,
  and the three stages before the furnace is built), no two parts overlap with the lid open (the iron, the
  brickwork and the lining are parts apart: the brick's voids hold the iron; lumps of a coke layer inside the layer
  over them, and the pins in the lid's knuckles, excepted), and the lid, swung every 0.02 of depth, touches nothing.
- **No z-fighting** at 30 poses (ten scenes, among them the three stages before the furnace is built, three depths)
  with only what each shows, after the fix (150 faces pressed against their own part removed). A brickwork face
  against the lining is kept (it is the wall's face until the lining is in); a lining face against the brickwork is
  left out.
- **Any fired brick** *(review)*: the rig's `bricks` name every type of the game's `burnedbrick` and nothing else,
  each with the block its bricks make; with the game's files at hand, every texture of the mapping (15) and of the
  shape (8) is a file there, every block one of the game's (`claybricks-good-fire`, or a running brick course of a
  colour its block type has), and the types the game's; the shape's own `brick` is the default type's colour layer.
- **Files:** every texture code declared; glow on the burning coke and the melts only; the frame's shape the iron
  frame alone;
  no power cell and no air port; the hollow cells the lid's; decks on the top cells at the floor; the hole's cell a
  top cell holding both pots' mouths, pot 1 west; the outlet in the back row's middle under the chimney's cell, which
  is not the furnace's, nor are its neighbours; the chimney's line six blocks from the outlet up, its top 8 over the
  ground; the ash mouth on the controller's front face; the hinge's line the lid's axis; the shipped files are the
  checked model.

`tools/tests/test_cruciblefurnace_model.py` holds the written files to these without the generator (the stages,
their requires and their counts measured again from the shape's brick, the hinge's iron with the brickwork, the
frame's shape the iron frame alone with no thin post over half the furnace, the brick types
and, with the game's files at hand, their textures; the lid shut and open and its swing in its own cells with no pit,
the melts' level for 300 units, both pots in the hole's cell, the centring and the walls, the side faces for a row,
the chimney's six from the outlet), and `site/models.json`'s states and Play to the generator's;
`site/test/cruciblefurnace.test.ts` replays the reference poses in the viewer's maths.

### Rig schema (`cruciblefurnace-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema") with nothing added to the shared rig
maths: two inputs, and parts gated by `requires`.

- **Inputs.** depth, the lid (0 open, 1 shut); θ, the melter's clock, read at ratio 0. No work, class, presence, feed
  or oil.
- **Drivers.** The lid: a `rotate` of ratio 0 (θ), then a `step` rotation about x through its top back edge, −100°
  over depth 0..1 (from open to shut). Every other part has none.
- **`requires`** values: the stages, `brickwork` (the `brickwork` and `hinge` parts), `lining` and `lid`; `pot1`,
  `pot2`, `charge1`, `charge2`, `melt1`, `melt2`, `coke1..3`, `fire1..3`, `damperopen`, `damperhalf`, `dampershut` (one
  at a time) and `outletcap`; the iron frame has none.
- **Keys:** `cells`, `holeCell`/`holeFace`, `pot1`, `pot2`, `outletCell`/`outletFace`, `ash`/`ashSide`, `mouth`,
  `hinge`, `chimney`, `build`, `bricks`, `furnace` and `parts`.
- **`build`**: `frame` (`recipe` grid, `tool` hammer, `ingredients`), `stages` in order (`requires`, `materials` as
  `{ code, quantity }`, the brickwork's and the lining's `volumeBlocks` and `perClick` `stack`, the brickwork's
  `oneType` and `alsoParts` (the `hinge` part it fits at no cost), the lid's `also`: the damper's places it fits one
  of), `outletCap`.
- **`bricks`**: `code` (`brick`), `item` (`game:burnedbrick-{type}`), `default` (`red`), and `types`: for each,
  `block`, `base` and, for the coloured courses, `overlays`.

**In the viewer** (`site/models.json`, `crucible-furnace`): the "Lid" slider is depth, and Play opens and shuts it
(open 2 s, shut in 1.4 s, firing 3 s, open in 1.4 s). Four selects of states: **Build** (the iron frame, 1:
brickwork, 2: lining, 3: lid and damper, complete; each with its materials, the page opening complete), **The pots
and the fire** (cold and empty, two pots on their stands, charged with the coke packed round, lit, melted, melted
with the coke burnt low, the pots pulled with the fire still lit; it waits for the furnace to be built), **Ash-pit
damper** (open, half, shut; it waits for the lid stage) and **Outlet** (open, the chimney standing on it, or capped;
it waits for the lining). The page opens with the furnace built, the lid open, the pots melted, the damper open and
the outlet open. There is no Brick select ("The brick", above).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`hg_*` the hinge's iron (`hg_pin`, `hg_stop`, `hg_tie_*`), `ln_*` the lining (`ln_brick_NNN`, `ln_stand<i>`), `lid_*` (`frame_na`..`frame_nd`, the front bar's steps,
`frame_s`, `frame_w`, `frame_e`, `fill_*`, `pocket`, `handle`, `knuckle_w`, `knuckle_e`), `pot1_*` and `pot2_*`
(`foot`, `lower`, `belly`, `upper`), `charge1_*`, `charge2_*`, `melt1_*`, `melt2_*`, `coke<j>_*` and `fire<j>_*`
(`n`, `s`, `w`, `m`, `e`, the corner fills `ne1`..`sw2`, the lumps, the burning bottom layer's cinders),
`damperopen_*`, `damperhalf_*`, `dampershut_*` (`plate`, `knob`), `outletcap_*`; and `fr_*` the iron frame
(`fr_bearer`, `fr_bar`, `fr_lintel`, `fr_guide`, `fr_strap`, `fr_kerb`, `fr_tie_*`). Hand edits
are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a standalone
copy). No one has looked at it in a client.

1. **Texture seams.** The brick is 224 boxes (129 of brickwork, 95 of lining), world-mapped by the
   mandrel station's reading of the game's face directions; that the brick runs on across them is not yet seen in the
   game.
2. **One texture a brick type**: the game draws a brick course with one of several textures per block; the furnace's
   walls take the first throughout.
3. **The glow is fixed**: 200 on the burning coke, 255 on the melts, whatever the temperature (scaling it is the
   renderer's, below).
4. **The coke is flat-topped** but for its lumps.
5. **The lid's swing** is checked against the frame and the hole's contents and kept in its hollow cells; a player
   standing in the front row's, where its front edge passes, is gameplay's.
6. **Whole-cell boxes**: the cells' boxes are full cubes, and the decks make the top one floor: no falling into the
   hole, and no clicking a single part inside it. They are the built furnace's; a part-built one's are gameplay's.
7. **A pot is drawn full**, 300 units, whatever is in it; the pot in the furnace is a little larger than the game's
   `meltingpot` block (8.6 by 12 against 8 by 11).
8. **The hinge stands on the top**: the knuckles 0.45 and the pins 0.25 over the floor at the lid's back corners, and
   the two stops 1.6. A pin on the lid's top edge stands its radius proud, and a stop under the floor would need the
   pit back (the lid's back face, through the axis, meets nothing at the floor behind it short of lying flat).
9. **The lid's front face is stepped** back from the seat's front wall, by 0.13 at its underside; at the floor the
   gap is 0.005.
10. **The hinge's ties show while the lining is not in**: the bars by the pins and the stops (fitted with the
    brickwork), which hold iron that sits in the lining's place.

## Open for the owner

Nothing for the model: the build is settled. A follow-up for the viewer: **a Brick select**, which needs the viewer
change proposed under "The brick" (a scenario `textures` select); left out of this model for now.

## Wiring it into the game

The game-side rules are #796 ("crucible furnace: one two-pot hole per furnace, natural draught by chimney height,
shared chimneys, ash-pit damper"). It will take:

- **The iron frame**: an item from a grid recipe with a hammer (2 iron plates, 4 iron rods, 10 iron nails and strips;
  the rig's `build.frame`), placed as the machine: a controller `seraphhorizons:cruciblefurnace-frame-{side}` at the
  front middle cell drawing `cruciblefurnace_frame.json` (the iron frame; its textures every code of both shapes), a
  ghost for the other seventeen brickwork cells and one for the six hollow cells over the front two rows, placed from
  the rig's cells by the player's look (`Footprint.PlacedFacing`), as the press brake and the mandrel station are.
  While it is being built, its selection and collision boxes are gameplay's (the rig's are the built furnace's).
- **The stages** (`build.stages`, in order): a click with a stage's material puts in as many of the held stack as it
  still needs (`perClick` `stack`); when all its materials are in, the stage is complete and fits its `requires`
  (the brickwork's also draws the `hinge` part, its `alsoParts`: the pins and the stops, which the lid needs);
  a stage takes nothing before the one ahead of it is complete. **Brickwork**: 25 fired bricks (`oneType`): the first
  brick put in sets the furnace's brick type, saved with it; a brick of another type is refused with a message
  naming the furnace's ("This furnace is built of red brick"), and a type not in `bricks.types` is refused. **Lining**:
  9 tier-3 refractory bricks and 4 fire clay, in any order. **Lid and damper**: 4 tier-3 refractory bricks and 2 iron
  plates; it fits the lid and the damper, open. Nothing is fired, charged or capped before its stage: the hole and
  the damper take nothing until the furnace is built, the cap nothing until the lining is in. What breaking a
  part-built furnace gives back is gameplay's.
- **The outlet's cap**: not a stage. A click on the outlet's top with an iron plate caps it (`outletcap`), and a
  click with an empty hand takes the plate back; on any member of a row but the one carrying the chimney.
- **The brick's texture**: the renderer sets the shape's `brick` code to the furnace's type's texture from the rig's
  `bricks.types` (a composite texture, `base` with its `overlays` over it); before any brick is in, the brickwork is
  not drawn.
- **The lid with tongs when hot**: a click opens or shuts it; it needs tongs (any `heatResistant` item in the off
  hand) only while it is too hot to touch, the same heat test as pulling a pot, and a cold furnace's lid moves
  bare-handed; without tongs on a hot lid the click is refused with a message, as a hot pull is.
- **Two pots in one hole**: one block entity for the hole (the stages, the brick type; two pots, each with its heats,
  charge and melt; one coke bed, temperature and fire; the lid; the damper; the cap). Both pots stand under one cell
  (`holeCell`): a click there goes to the pot whose mouth (`pot1`, `pot2`) is nearer the hit. A pot holds
  `PotCapacityUnits` (300 after #768, the rig's `furnace.potUnits`, which a test should hold to it): 600 a firing.
  The bed's `CokeCapacity` is drawn as `cokeLayers` layers of `cokePerLayer` (two at today's six); if #796 raises it
  for two pots, `cokePerLayer` follows.
- **Rows of up to six**, **18 blocks** long: furnaces side by side along their width (`furnace.rowAxis`, `rowMax`),
  sharing one chimney stood on any member's outlet; every other member's outlet capped; an uncapped outlet with no
  chimney on it leaks the row's draught. The flues between them are implied.
- **Draught set by the chimney's height**: counted from the row's chimney-carrying `outletCell`'s top up, in
  `ChimneyBlocks` (clay bricks, clay brick chimney, refractory bricks, stone bricks; not smex's smoke stack), the
  furnace's own stack base not counted. Six blocks (`MinChimneyHeight`) give 200 °C an hour up to 1600 °C; each
  block above six adds 40 °C an hour and 10 °C of maximum, counted up to eleven blocks.
- **The ash-pit damper**: three places, clicked at the ash mouth (`ash`), the renderer drawing the matching part:
  **open**, full draught; **half**, half the heating rate and the coke burning at 0.6; **shut**, banked, no heating,
  cooling at a quarter of the normal rate and the coke burning at 0.25.
- **No forced air**: today's air from exlib pipes (`Game/ForcedAir.cs`, `ForcedHeatingPerHour`,
  `ForcedMaxTemperature`, `AirLitresPerSecond`) goes; the furnace has no air port.
- **A renderer** on `Machines/Core` (`RigAnimation`) drawing the parts by the furnace's state: the stages fitted (the
  brickwork in its brick type's texture, the lining, the lid at depth from its eased place, 0 open, 1 shut), then the
  hole's contents (the pots, charges and melts per pot; the coke layers by `ceil(coke / cokePerLayer)`, `fire` in
  place of `coke` while lit; the damper's place; the cap), `melt` and `charge` textures set from each pot's contents,
  the glow scaled to the temperature (the model's are fixed); flames, light and sound at `mouth` while the lid is
  open, smoke at the chimney's top.
- **Old holes removed**: the `meltinghole` block, its grid recipe and the chimney row (`FurnaceRow`) are simply
  removed, with no migration: holes already in a world go.
- **Recipes and data**: the frame's grid recipe; the recipe browser's multiblock data for the new block, with its
  stages' materials; the handbook (the stages, any fired brick, the brick type rule).
- **Tests**: the rig and its reference poses replayed in C#, as the mandrel station's are; Atlas scenarios for
  placing the frame, each stage (a stack a click, the counts, one brick type and a different one refused, a type not
  in `bricks` refused), the two pots, the lid and its tongs, the damper, a row sharing a chimney with caps, and the
  draught by chimney height.
