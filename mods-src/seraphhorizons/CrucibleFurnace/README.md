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
is one such hole, as one block of tier-3 refractory brickwork **two blocks wide (x), two deep (z) and two high**,
bound with iron on its front and back. **Its top is flat**, the melting floor, broken only by the lid's seat, the
hinge pit behind it and the outlet.

- **The hole** is 24 voxels wide and 11.6 deep inside, lined with the firebrick, across the front row. It holds
  **two pots side by side**, each on a **fireclay stand** on the **grate** (six rows of iron firebars, two lengths a
  row, lying on three iron bearers that run front to back and are built into the front and back walls), packed
  round in **one bed of coke** up to their shoulders: 1.7 of coke between a belly and a side wall, 3.4 between the
  two, 1.5 before and behind.
- **Each pot** is a fireclay crucible, an octagonal barrel 8.6 across at the belly and 12 tall from its narrow foot,
  a little larger than the game's `meltingpot` block (8 by 11), as it holds 300 units ("A full pot", below): 600 a
  firing, teemed from both into one mould for #639's cylinder blank.
- **The lid** is firebrick in an iron frame, 25 by 12.6 and 2 thick, flush with the floor when shut, with a pocket
  near its front edge and an iron handle bar across the pocket, under its top. It is **hinged at the back**, on an
  iron pin through its back bar (0.4 above its underside and 0.4 in front of its back edge), the pin's ends in iron
  brackets set in the side walls; behind the hole a **hinge pit** gives its heel room as it swings. It swings up 91°
  and **rests just past upright** against an iron **stop** along the pit's back edge, its weight 0.7 behind the pin
  (past 84° it falls back onto the stop of itself). Open, it stands in the two cells over the hole, out of the
  chimney's cell behind.
- **The ash pit** (the cave) runs under the grate, 8 voxels high, open at the front through the **ash mouth** (12
  wide, 6 high, an iron lintel over it): natural draught only, the air comes in there. Over the mouth the **damper**,
  an iron plate with a knob, slides in a shallow recess in the front face between iron guides, in three places:
  **open** (clear of the mouth), **half** (over its upper half) and **shut** (over all of it). There is no air port.
- **The flue.** Near the top of the hole's back wall, above the coke and under the lid, a **flue opening** leads into
  a flue in the back row and up a **stack** to an **outlet** on the top over the east back cell: a square mouth in an
  iron curb, flush. **The model stops at the outlet**: the player stands the chimney on it ("The chimney", below).
  The outlet has **two states**: open, with the chimney on it, or **capped** with a flat iron plate set flush in the
  curb's rim, as every other furnace in a row sharing one chimney is.
- **Furnaces stand in a row**, up to six side by side along their width (x), sharing walls: the side faces are flush,
  plain brick with nothing beyond the footprint (the binding is on the front and back only), so two furnaces' 4-voxel
  side walls meet. The flues under the floor between them are implied, not modelled.

**Footprint and cells.** The brickwork's 8 cells, and the 2 cells over the hole (`[0,2,0]`, `[1,2,0]`), **hollow**:
the lid stands there open, so nothing may be built there. The cell over the outlet, `[1,2,1]`, is the chimney's and
the one beside it, `[0,2,1]`, is free: neither is the furnace's, and nothing of it reaches them at any point of the
lid's swing. The **controller is the front west cell at ground level**, `[0,0,0]`; the furnace extends east (x) and
south (z) from it. Placed like the press brake and the mandrel station, it extends away from the player along their
line of sight: the hole, the ash mouth and the damper face them, the stack and the outlet are behind.

**Why 2 × 2 × 2.** As small as the hole, the pots, the lid's swing, the ash mouth and the outlet allow:

- **Two wide**: two pots side by side need a hole 24 wide, and with its side walls that is two blocks. A row of six
  is twelve blocks long.
- **Two deep**: the hole and its front and back walls fill the front row, and the lid must open towards the back (the
  front is the melter's, the sides are the neighbours'). The outlet cannot stand beside the hole (no room in the
  width), so it goes in the back row, behind the lid's swing; the lid, hinged low in its back edge, stands open
  within the front row's air (to z 15.5), clear of the chimney on the outlet. A furnace one block deep would have
  nowhere for the outlet, and one three wide with the outlet beside the hole would make a row of six 18 blocks long.
- **Two high**: the ash pit and the grate under a hole 18 deep from the grate to the lid; the period's cellar is
  folded into the block. Set into a floor with a step down in front of the ash mouth, it is the period's shop;
  stood on the ground, the hole is worked from a step.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `pot1Cell`, `pot1Face` | `[0,1,0]`, `up` | The cell over pot 1 (west), worked from its top |
| `pot2Cell`, `pot2Face` | `[1,1,0]`, `up` | The cell over pot 2 (east) |
| `outletCell`, `outletFace` | `[1,1,1]`, `up` | The stack's base; the chimney (or the cap) on its top, the chimney from `[1,2,1]` up |
| `ash`, `ashSide` | `[1,0.25,0]`, `north` | The middle of the ash mouth in the front face: the damper, worked from in front |
| `mouth` | `[1,2,0.4875]` | The middle of the hole's mouth at the floor: flames, the glow's light and the fire's sound while the lid is open |
| `hinge` | origin `[1,1.9,0.8562]`, `x` | The lid's pin, drawn as a line |
| `chimney` | origin `[1.5,5,1.5]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `furnace` | | `pots` 2, `potUnits` 300, `cokeLayers` 3, `cokePerLayer` 2, `minChimney` 6, `rowMax` 6, `rowAxis` `x`, `lidOpenDegrees` 91; not drawn |

**Decks, boxes.** Each of the four top cells has a `lid` (a collision deck) at the floor: a player walks over the
open hole and cannot fall in. The cells' boxes are built from the brickwork, its iron and the lid shut; the box
splitter cannot leave a hole open in three boxes, so every cell's box is the whole cell. The open lid has no box in
the rig's cells: it moves, so its collision is gameplay's.

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
| `assets/seraphhorizons/shapes/block/cruciblefurnace.json` | The whole furnace, every part (376 elements) |
| `assets/seraphhorizons/shapes/block/cruciblefurnace_frame.json` | The static frame only (152 elements): the brickwork, its iron (the grate, the lintel and the damper's guides, the hinge's pin, brackets and stop, the curb, the binding) and the stands. A block draws it |
| `assets/seraphhorizons/config/cruciblefurnace-rig.json` | Cells, anchors and the part rig (18 parts) |
| `tests/CrucibleFurnace/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

**Textures** (all the game's; the shape names them, a blocktype may map them):

| Code | Texture | On |
|---|---|---|
| `brick` | `game:block/clay/refractory/tier3/front1` (the old melting hole's) | The brickwork and the lid's infill: world-mapped, so the many boxes read as one wall |
| `iron` | `game:block/metal/plate/iron` (the other machines' iron) | Firebars, bearers, the lintel and the damper's guides, the hinge's pin, brackets and stop, the curb, the binding, the lid's frame and handle, the damper, the cap |
| `clay` | `game:block/clay/hardened/fire` (the fired pot's) | The pots and their stands |
| `coke` | `game:block/coal/coke` | The coke |
| `ember` | `game:block/coal/ember` | The same coke, burning; its faces glow 200 (the game's lit firepit's embers glow 160) |
| `melt` | `game:block/metal/ingot/stainlesssteel` | The melts; glow 255. The renderer may set a pot's metal |
| `charge` | `game:block/metal/ingot/iron` | The charges' pieces; the renderer may set it |

### How the furnace works

Nothing turns. The lid is the only part that moves; everything else that shows or not is a `requires`, gameplay's
state.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The brickwork, its iron binding (`frame`) | fixed | carries everything; its voids are the hole, the seat, the hinge pit, the cave, the ash mouth and the damper's recess, the flue opening, the back flue and the stack; its top is flat, its sides plain |
| The bearers and firebars (`frame`) | fixed: the bearers built into the front and back walls, the bars lying across them | carry the stands and the coke; the draught comes up between the bars |
| The stands (`frame`) | fixed, on the grate | carry the pots |
| The hinge: pin, brackets, stop (`frame`) | fixed: the pin in brackets set in the side walls, the stop along the pit's back edge | carry the lid at its back; the stop holds it open |
| The lid (`lid`, no requires) | depth: turned about the pin from open (0, 91° up, resting on the stop) to shut (1, flush in its seat). The melter, with tongs while it is too hot to touch | shuts the hole: the fire then draws only up through the grate, out at the flue opening |
| The pots (`pot1`, `pot2`) | put in, pulled: fitted or not | hold the charges, then the melts |
| The charges (`charge1`, `charge2`) | charged | fill each pot to 24.5 (a full pot, 300 units) with pieces standing up to 27.9, over the rim |
| The melts (`melt1`, `melt2`) | the charge melted | each pot's molten surface at 24.5, a full pot of 300 units, 1.5 under the rim, glowing |
| The coke, three layers (`coke1`..`3`) | the coke in the hole: two coke a layer, from the grate up | one bed packed round both pots to their shoulders, lumps of coke on each layer's top |
| The coke burning (`fire1`..`3`) | lit: the same layers in their burning texture, glowing; the bottom one also drops cinders on the cave's floor under the pots | the glow: from above when the lid is open, at the ash mouth and up through the bars when it is shut |
| The damper (`damperopen`, `damperhalf`, `dampershut`) | the cellar lad: one of its three places | sets the draught (#796: open full, half at half the heating, shut banked) |
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

A full run takes about 15 s and is deterministic (two runs into two folders are byte-identical, logs included). It
exits non-zero if a check fails (`tools/validate_cruciblefurnace.py`). Those marked *(review)* came from the owner's
review:

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **Frame:** joined to the ground as one piece (152 elements).
- **Textures by role:** brick, iron, clay, coke, ember, melt and charge where the table above puts them.
- **Containment:** nothing leaves the brickwork and the two cells over the hole over the lid's whole swing (every
  0.01 of depth), so nothing reaches the chimney's cell or the one beside it; with the lid shut nothing stands over
  the floor, and with it open only the lid.
- **A flat top** *(review)*: every half voxel of the top, but the seat, the hinge pit and the outlet's mouth, is brick
  or flush iron whose top is the floor (2296 points); nothing of the frame above it.
- **A row** *(review)*: each side face is plain brick over its whole area (8192 points over the two), nothing but
  brick reaches either side plane at any depth, and two furnaces side by side touch nowhere but at their shared
  walls over the lid's whole swing.
- **The lid** *(review)*: shut, flush in its seat, on the seat's ledge at its front and sides and on the pin at its
  back (the pin in its back bar at every depth, its ends in the brackets); open, 91° about the pin, its weight behind
  the pin, its back face on the stop's edge (to 1e-4); θ moves nothing, depth only the lid.
- **The pots:** each on its stand on the grate (its foot covering the stand), its mouth under the shut lid, at least
  1.5 of coke round it, to the walls and between the two; the charges and the melts inside them, the charges'
  pieces under the lid.
- **A full pot** *(review)*: each pot's inside from its floor to its melt's top, measured from its walls' elements
  (the octagon inside their inner faces at each height, every 0.01), holds `POT_UNITS` (300) at `VOXELS_PER_UNIT`,
  within 2 %; the melt at least a voxel under the rim.
- **Coke:** the layers stack from the grate to the shoulders, two coke a layer (six in all); at each layer's top the
  coke meets both pots with no gap; every layer whole across (sampled on a grid: each point coke or a pot's place);
  its top, lumps and all, under the flue opening; the burning layers the same boxes as the cold ones.
- **The damper** *(review)*: in each place a plate in its recess behind the front face, between its guides; shut it
  covers the whole ash mouth, half its upper half (the mouth open below its middle), open none of it.
- **The outlet** *(review)*: open, clear to the sky through the stack; the cap flush with the floor in the curb's
  rim, on its ring, over the stack's mouth.
- **Draught:** sampled points from outside the ash mouth, through the cave, up through every gap of the grate, out at
  the flue opening, along the back flue and up the stack to the outlet, in no element of the frame; the stack and the
  outlet's mouth clear of every element at every depth but the cap; every firebar on a bearer at each end, the
  bearers built into the walls; the bars' gaps at least 0.8.
- **Clearances:** in each of ten scenes (the seven states of the pots, the damper's three places with the cap on), no
  two parts overlap with the lid open (lumps of a coke layer inside the layer over them, and the pin in the lid's back
  bar, excepted), and the lid, swung every 0.02 of depth, touches nothing.
- **No z-fighting** at 21 poses (seven scenes, three depths) with only what each shows, after the fix (221 faces
  pressed against their own part removed, the rest stepped in by 0.015).
- **Files:** every texture code declared; glow on the burning coke and the melts only; no power cell and no air port;
  the hollow cells the lid's; decks on the top cells at the floor; the outlet under the chimney's cell, which is not
  the furnace's, nor the one beside it; the chimney's line six blocks from the outlet up, its top 8 over the ground;
  the ash mouth on the front face; the hinge's line the pin's; the shipped files are the checked model.

`tools/tests/test_cruciblefurnace_model.py` holds the written files to these without the generator (the lid shut
and open, the melts' level for 300 units, the side faces for a row, the chimney's six from the outlet), and
`site/models.json`'s states, choice and Play to the generator's; `site/test/cruciblefurnace.test.ts` replays the
reference poses in the viewer's maths.

### Rig schema (`cruciblefurnace-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema") with nothing added to the shared rig
maths: two inputs, and parts gated by `requires`.

- **Inputs.** depth, the lid (0 open, 1 shut); θ, the melter's clock, read at ratio 0. No work, class, presence, feed
  or oil.
- **Drivers.** The lid: a `rotate` of ratio 0 (θ), then a `step` rotation about x through the pin, −91° over depth
  0..1 (from open to shut). Every other part has none.
- **`requires`** values: `pot1`, `pot2`, `charge1`, `charge2`, `melt1`, `melt2`, `coke1..3`, `fire1..3`,
  `damperopen`, `damperhalf`, `dampershut` (one at a time) and `outletcap`; the lid and the frame have none.
- **Keys:** `cells`, `pot1Cell`/`pot1Face`, `pot2Cell`/`pot2Face`, `outletCell`/`outletFace`, `ash`/`ashSide`,
  `mouth`, `hinge`, `chimney`, `furnace` and `parts`.

**In the viewer** (`site/models.json`, `crucible-furnace`): the "Lid" slider is depth, and Play opens and shuts it
(open 2 s, shut in 1.4 s, firing 3 s, open in 1.4 s). Two selects of states: **The pots and the fire** (cold and
empty, two pots on their stands, charged with the coke packed round, lit, melted, melted with the coke burnt low, the
pots pulled with the fire still lit) and **Outlet** (open, the chimney standing on it, or capped); and a choice,
**Ash-pit damper** (open, half, shut). The page opens with the lid open, the pots melted, the damper open and the
outlet open.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `lid_*` (`frame_*`, `fill_*`,
`pocket`, `handle`), `pot1_*` and `pot2_*` (`foot`, `lower`, `belly`, `upper`), `charge1_*`, `charge2_*`, `melt1_*`,
`melt2_*`, `coke<j>_*` and `fire<j>_*` (`n`, `s`, `w`, `m`, `e`, the corner fills `ne1`..`sw2`, the lumps, the
burning bottom layer's cinders), `damperopen_*`, `damperhalf_*`, `dampershut_*` (`plate`, `knob`), `outletcap_*`;
and `fr_*` for the frame (`fr_brick_NNN`, `fr_bearer`, `fr_bar`, `fr_lintel`, `fr_guide`, `fr_pin`, `fr_bracket`,
`fr_stop`, `fr_curb`, `fr_rim`, `fr_strap`, `fr_kerb`, `fr_stand`). Hand edits are lost when the script runs again:
port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a standalone
copy). No one has looked at it in a client.

1. **Texture seams.** The brickwork is 98 boxes, world-mapped by the mandrel station's reading of the game's face
   directions; that the brick runs on across them is not yet seen in the game.
2. **The glow is fixed**: 200 on the burning coke, 255 on the melts, whatever the temperature (scaling it is the
   renderer's, below).
3. **The coke is flat-topped** but for its lumps.
4. **The lid's swing** is checked against the frame and the hole's contents; a player standing where it swings is
   gameplay's.
5. **Whole-cell boxes**: the cells' boxes are full cubes, and the decks make the top one floor: no falling into the
   hole, and no clicking a single part inside it.
6. **A pot is drawn full**, 300 units, whatever is in it; the pot in the furnace is a little larger than the game's
   `meltingpot` block (8.6 by 12 against 8 by 11).
7. **The front wall is thin at the top**: 1.5 voxels in front of the lid's seat (the lid, the pots and the lid's
   swing fill the front row's depth).

## Open for the owner

The model is settled. Still open:

1. **The build stages**, to be worked out next.

## Wiring it into the game

The game-side rules are #796 ("crucible furnace: one two-pot hole per furnace, natural draught by chimney height,
shared chimneys, ash-pit damper"). A follow-up, after the build stages. It will take:

- **A ghost-cell machine**: a controller `seraphhorizons:cruciblefurnace-frame-{side}` at the front west cell drawing
  `cruciblefurnace_frame.json` (its textures every code of both shapes), a ghost for the other seven brickwork cells
  and one for the two hollow cells over the hole, placed from the rig's cells by the player's look
  (`Footprint.PlacedFacing`), as the press brake and the mandrel station are; selection boxes from the cells,
  collision with their decks, and the open lid's collision in the hollow cells while it is open.
- **The lid with tongs when hot**: a click opens or shuts it; it needs tongs (any `heatResistant` item in the off
  hand) only while it is too hot to touch, the same heat test as pulling a pot, and a cold furnace's lid moves
  bare-handed; without tongs on a hot lid the click is refused with a message, as a hot pull is.
- **Two pots in one hole**: one block entity for the hole (two pots, each with its heats, charge and melt; one coke
  bed, temperature and fire; the lid; the damper; the cap), a click routed to a pot by its cell (`pot1Cell`,
  `pot2Cell`). A pot holds `PotCapacityUnits` (300 after #768, the rig's `furnace.potUnits`, which a test should
  hold to it): 600 a firing. The bed's `CokeCapacity` is drawn as `cokeLayers` layers of `cokePerLayer` (two at
  today's six); if #796 raises it for two pots, `cokePerLayer` follows.
- **Rows of up to six**: furnaces side by side along their width (`furnace.rowAxis`, `rowMax`), sharing one chimney
  stood on any member's outlet; every other member's outlet capped (`outletcap`, set on the outlet's top by a click
  with an iron plate, say); an uncapped outlet with no chimney on it leaks the row's draught. The flues between
  them are implied.
- **Draught set by the chimney's height**: counted from the row's chimney-carrying `outletCell`'s top up, in
  `ChimneyBlocks` (clay bricks, clay brick chimney, refractory bricks, stone bricks; not smex's smoke stack), the
  furnace's own stack base not counted. Six blocks (`MinChimneyHeight`) give 200 °C an hour up to 1600 °C; each
  block above six adds 40 °C an hour and 10 °C of maximum, counted up to eleven blocks.
- **The ash-pit damper**: three places, clicked at the ash mouth (`ash`), the renderer drawing the matching part:
  **open**, full draught; **half**, half the heating rate and the coke burning at 0.6; **shut**, banked, no heating,
  cooling at a quarter of the normal rate and the coke burning at 0.25.
- **No forced air**: today's air from exlib pipes (`Game/ForcedAir.cs`, `ForcedHeatingPerHour`,
  `ForcedMaxTemperature`, `AirLitresPerSecond`) goes; the furnace has no air port.
- **A renderer** on `Machines/Core` (`RigAnimation`) drawing the lid at depth from its eased place (0 open, 1 shut),
  and the parts by the hole's state (the pots, charges and melts per pot; the coke layers by `ceil(coke /
  cokePerLayer)`, `fire` in place of `coke` while lit; the damper's place; the cap), `melt` and `charge` textures set
  from each pot's contents, the glow scaled to the temperature (the model's are fixed); flames, light and sound at
  `mouth` while the lid is open, smoke at the chimney's top.
- **Old holes removed**: the `meltinghole` block, its grid recipe and the chimney row (`FurnaceRow`) are simply
  removed, with no migration: holes already in a world go.
- **Recipes and data**: the frame's recipe (refractory bricks and iron) and its stages; the recipe browser's
  multiblock data for the new block; the handbook.
- **Tests**: the rig and its reference poses replayed in C#, as the mandrel station's are; Atlas scenarios for
  placement, the two pots, the lid and its tongs, the damper, a row sharing a chimney with caps, and the draught by
  chimney height.
