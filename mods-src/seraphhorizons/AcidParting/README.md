# Acid parting vessel: the model

Part of the Seraph Horizons mod's ore processing (epic #684): the generated model of the acid parting vessel (#932),
the machine of #931, which takes dore (gold and silver together) in by hand and gives the gold and the silver out by
hand, batch. The line, the glossary and the period sources are #939; the power and water budget #940.

**Status: a model only, v3, for review.** This folder's `tools/` holds the generator and its checks. Nothing in the
game reads the model yet: no blocktype, no block entity, no renderer. The build stages are a proposal (below), to be
settled with the owner.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, `Pipes/` and `CrucibleFurnace/`, this folder's neighbours. The generic
half of the generator is `Machines/tools/machinegen/`, unchanged: no new driver and no change to the package. The water
line is built by `Pipes/tools/solderedpipe.py`, as the rosser's drip pipes are, also unchanged.

## The vessel

Nitric acid parting as the mints and refineries did it in the late nineteenth century (Rose 1898, pp. 398-400: the
granulated alloy in "cylindrical vessels of earthenware, porcelain, or platinum", set in a cast-iron sand bath over a
small fire, under a brick hood drawing to the chimney; the gold left behind washed with water; the Philadelphia Mint's
porcelain pots held 2,080 oz, twelve to a bath). The model is **one vessel**, as one block of brickwork **one block
wide (x), two deep (z) and two high**, bound with iron on its front and back. Its front faces the player.

- **The fire**, in the front row's lower cell: a grate of four iron **firebars** across two iron **bearers** that run
  front to back, their ends built into the front and back walls; under it the **ash pit**, open at the front through
  the **ash mouth** (5 wide, 2.5 high, an iron lintel over it); over it the **fire chamber**, 7 wide, 8 deep and 6
  high, its roof the sand bath's iron bottom. The fuel goes in through the **fire door** (an opening 4 wide and 3
  high over the ash mouth). Both are in **one recess** in the front face, centred, whose iron **guides** carry the
  **damper** over the ash mouth (open, half, shut: the crucible furnace's three places) and the **fire door** over its
  opening, an iron plate that **slides up** open and down shut. Natural draught only.
- **The sand bath**: a cast-iron tray 10 by 12, let into the masonry over the fire, its bottom the fire chamber's roof,
  bearing 1.5 on the chamber's walls all round, its rim flush with the **bench** (the brick round it, 1 block up, the
  hood's floor). Its sand stands half a voxel under the rim, round the vessel and half a voxel under it.
- **The vessel**: porcelain, an octagonal barrel 7 across and 9.25 tall with a flat bottom and a flared lip, set 1.25
  into the sand, centred left to right and under the water face's centre. Its contents are parts (below): the
  granulated dore, the acid, the silver solution, the parted gold, the wash water.
- **The hood**: two brick **piers** (2.5 thick, the side walls) carry the **roof** and, over the hood's open front, the
  **breast**, on an iron **lintel** set half a voxel back under the brick face. The opening is 11 wide and 12 high,
  from the bench to the breast, so that the cock over the vessel shows from a stoker's eye height. The hood's back wall
  stands behind the bath, a **flue** opening near its top (6 wide, 5 high) into the stack. The hood is the brickwork's,
  not lined: it carries the acid's fumes, not the fire's heat.
- **The water line**: one ppex straight pipe's worth of UnifiedPipes' soldered pipe, copper or lead, from the **water
  face** (the top of the front row, under the player's ppex pipe) down through the hood's roof, a wiped joint of solder
  where it meets a **brass cock** over the vessel's mouth, its nozzle 1 voxel over the lip. The cock is a plug cock: its
  key's lever lies across the flow (forward) shut, along it (down) open, and open the water runs from the nozzle into
  the vessel. Rule 4: no return water, so no return anchor.
- **The flue and the stack**: the fire's flue leaves the fire chamber at the top of its back wall (4 wide, 3.5 high)
  and runs back into a **stack** 6 by 6, centred in the back row, from the flue's floor up to an **outlet** on the top,
  where the hood's flue joins it too. All of it is lined. The outlet's **curb** is the lining's brick, flush, a rim
  round a seat half a voxel deep; the **cap** is a flat iron plate set flush in the seat. **The model stops at the
  outlet**: the player stands the chimney on it, at least six blocks counted from the outlet up (the crucible
  furnace's `MinChimneyHeight`), or caps it, as every vessel but one in a row sharing a chimney is.
- **Vessels stand in a row** along their width (x), sharing walls: the side faces are flush, plain brickwork over their
  whole area, nothing beyond the footprint, so two vessels side by side touch only at their shared walls. The flues
  between them are implied, not modelled.

**Not a furnace.** The issue puts it under the crucible furnace's conventions, and it takes from `CrucibleFurnace/`
only what this vessel needs: **the fire** (a grate on bearers built into the walls, the ash mouth and its three-place
damper, natural draught), **the outlet** with its curb, cap and the chimney counted from it, **brick where it is brick**
in its two roles (any fired brick for the brickwork, tier-3 refractory lining 3 voxels thick on the hot faces: the fire
chamber, its flue, the stack and the curb), the iron binding on the front and back, the staged build with the brick
counted from the model, the contents drawn as parts, and the crucible's generator and checks (`world_skin`, the brick
lattice, `fix_coplanar`, the stage and lining and row checks). It leaves out what belongs to a melting hole: the lid
and its hinge, the pots, the coke bed, the hollow cells and the decks over an open hole. The hood and the bench are
plain brickwork.

**Footprint and cells.** Four cells, none hollow (nothing moves outside the front cell's recess): the front row (the
fire, the bath, the vessel, the hood) and the back row (the stack). The **controller is the front cell at ground
level**, `[0,0,0]`, the fire door's and the ash mouth's, so the build frame is the shipped frame. The cell over the
outlet, `[0,2,1]`, is the chimney's, and the cell over the water face, `[0,2,0]`, the player's ppex pipe's: neither is
the machine's. Placed like the crucible furnace, it extends away from the player along their line of sight.

**Why 1 × 2 × 2.** As small as the fire, the bath, the vessel, the hood and a lined stack allow:

- **One wide**: a fire chamber 7 wide with walls of 4.5 (a half-brick skin of 1.5 outside 3 of lining) is 16; the bath
  (10 wide) bears 1.5 on its walls, and the vessel (7 across) stands in it with a voxel of sand each side; the hood's
  piers (2.5) leave an opening 11 wide.
- **Two deep**: the front row is the fire, the bath, the vessel and the hood (a front wall of 4, the chamber's 8 and a
  back wall of 4, the vessel centred over it); the stack, 6 square with 3 of lining round it and brick outside, does not
  fit behind them in the same row, so it is the back row, centred, with the outlet on its top: "the outlet behind".
- **Two high**: the ash pit, the grate and the fire chamber (to 13), the bath's tray (to 16, the bench), the vessel (to
  23.5), the cock over it and the hood's roof (to 32). The bench is a block up, a working height.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `vesselCell`, `vesselFace` | `[0,1,0]`, `north` | The hood's cell, the vessel worked through its open front: the dore in, the gold and the silver out |
| `vessel` | `[0.5,1.4688,0.5]` | The middle of the vessel's mouth: the charge in, the gold out, the fumes rising |
| `firedoor`, `firedoorSide` | `[0.5,0.625,0]`, `north` | The middle of the fire door's opening in the controller's front face: the fuel in |
| `ash`, `ashSide` | `[0.5,0.1406,0]`, `north` | The middle of the ash mouth in the controller's front face: the damper, the ash out |
| `cock` | `[0.5,1.5312,0.5]` | The nozzle's tip: where the water runs out, over the vessel's mouth |
| `waterCell`, `waterFace` | `[0,1,0]`, `up` | The water face: the top of the front row, at the cell's centre, where the player's ppex pipe in `[0,2,0]` meets the water line |
| `outletCell`, `outletFace` | `[0,1,1]`, `up` | The stack's head, in the back row; the chimney (or the cap) on its top, the chimney from `[0,2,1]` up |
| `chimney` | origin `[0.5,5,1.4375]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `build` | | The build's stages and their materials (below); not drawn |
| `bricks` | | The fired brick types and their textures (the crucible furnace's table); not drawn |
| `parting` | | `minChimney` 6, `rowMax` 6, `rowAxis` `x`, `pipeMetals` copper and lead; not drawn |

**Provisional pending #711** (the shared machine code has not landed): the water face and the reservoir behind it (the
rosser's pattern, 20 L, up to 10 L/s, not drawn), and the outlet and chimney anchors, which follow the crucible
furnace's. Batch equipment: no chute anchor (rule 2), and the validator fails if one appears.

**Boxes.** The cells' boxes are built from the vessel as built (the frame, the brick, the hood's lintel, the bath, the
vessel and the fire door shut); the splitter makes every cell one whole box. Both top cells have a `lid` (a collision
deck) at the top: the roof is walked on whole.

## The build (a proposal)

Stages as the crucible furnace's, each a `requires` value of its parts but the frame's (the rig's `build`, in order),
the viewer's **Build** select stepping through them. The brickwork's and the lining's counts are **measured from the
model** (the brick lattice's boxes at the game's rates, 2 fired bricks a block, 4 refractory bricks a block, fire clay
one per 2.5 of those, rounded up); everything else is a first proposal for the owner.

| | Stage | `requires` | What it puts in | Materials |
|---|---|---|---|---|
| Frame | **Iron frame**: a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (the frame part) | The binding's straps and top bands, the grate's bearers and firebars, the ash mouth's and the fire door's lintels and the recess's guides, standing on their own on 6 short posts hidden in the brickwork | 1 iron plate, 2 iron rods, 6 iron nails and strips (proposal) |
| 1 | **Brickwork** | `brickwork` (and the `hoodiron` part) | The base, the ash pit, the walls, the bench, the hood on its piers, the stack's outer walls: 1.89 blocks; with it, at no cost of its own, the hood's lintel | **4 fired bricks**, any one type |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on the hot faces (the fire chamber, its flue, the stack) and the outlet's curb: 0.96 blocks | **4 tier-3 refractory bricks**, **2 fire clay** |
| 3 | **Fittings** | `fittings` (the `bath`, `vessel` and `door` parts; the damper in one of its places) | The sand bath with its sand, the vessel, the fire door, the damper | 3 iron plates, 2 sand (proposal); the vessel's item is open |
| | *Water line* | `pipecopper` or `pipelead` (the cock in one of its places) | One ppex straight pipe through the roof, the brass cock | `ppex:pipe-straight-ud-{copper,lead}`, 1 brass plate (proposal) |
| | *Outlet cap* | `outletcap` | Not a stage: put on (or taken off) by hand, any time after the lining | 1 iron plate |

**The ties.** So that nothing floats before the brick is in, the least iron joins each piece of the frame to the
ground, all of it in the brickwork (voids are cut for it), so it is hidden once the brick is in: a post under each end
of the ash mouth's lintel, behind the recess (the guides stand against both lintels' faces and carry the fire door's),
and a post under each end of each bearer, in the front and back walls, half a voxel clear of the ash pit and the mouth.
The hood's lintel comes with the brickwork that carries it, as the crucible furnace's hinge does, so the frame has no
tall thin post (its tallest thin piece is a guide, 14.75).

## No power, no forced air

Fired by hand on natural draught: no mechanical power (no `powerCell` or `powerFace`, no power ghost) and no air port;
the validator fails if either appears. As in the crucible furnace, the rig's θ is a clock that moves nothing: a
`rotate` of ratio 0 on the fire door carries it, so the viewer offers Play, which opens and shuts the door.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. **No new texture**: every texture is the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/acidparting.json` | The whole vessel, every part (264 elements) |
| `assets/seraphhorizons/shapes/block/acidparting_frame.json` | The iron frame only (26 elements): what the placed frame draws, before any stage |
| `assets/seraphhorizons/config/acidparting-rig.json` | Cells, anchors, the build and the brick types, and the part rig (22 parts) |
| `tests/AcidParting/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

**Textures** (all the game's):

| Code | Texture | On |
|---|---|---|
| `brick` | The brick type's block texture, set by the renderer (`bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork, world-mapped |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining and the curb, world-mapped |
| `iron` | `game:block/metal/plate/iron` | The frame, the hood's lintel, the sand bath's tray, the fire door, the damper, the cap |
| `brass` | `game:block/metal/plate/brass` | The cock, its key and lever |
| `sand` | `game:block/stone/sand/sandstone` | The sand bath's sand |
| `porcelain` | `game:block/clay/flintporcelain/fired` | The vessel |
| `fuel` / `ember` | `game:block/coal/charcoal` / `game:block/coal/ember` | The fire laid / burning; burning, its faces glow 200 (the crucible's coke) |
| `dore` | `game:block/metal/ingot/electrum` | The granulated dore |
| `gold` | `game:block/metal/ingot/gold` | The parted gold |
| `acid` | `game:block/liquid/brine` | Aqua fortis, pale yellow |
| `solution` | `game:block/liquid/dye/woad` | The acid with the silver (and the dore's copper) in it: blue |
| `water` | `game:block/liquid/waterportion` | The wash water and the stream from the open cock |
| `pipecopper`, `pipelead`, `solder` | `game:block/metal/sheet-plain/{copper,lead}4`, `game:block/metal/ingot/leadsolder` | The water line, as UnifiedPipes gives ppex's pipe, its joint in solder |

### How it works

Nothing turns. The fire door is the only part that moves; everything else that shows or not is a `requires`,
gameplay's state.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The iron frame (`frame`, no requires) | fixed: placed as the machine | carries the grate and the recess's guides; its binding holds the brick; its ties hold it up before the brick is in |
| The brickwork (`brickwork`) and the hood's lintel (`hoodiron`) | stage 1 | the base, the ash pit, the walls, the bench, the hood and its breast, the stack's outer walls; plain brick on the side faces for a row |
| The lining (`lining`) | stage 2 | the hot faces: the fire chamber, its flue, the stack; the outlet's curb |
| The bearers and firebars (`frame`) | fixed: the bearers built into the front and back walls, the bars across them | carry the fuel; the draught comes up between the bars |
| The sand bath (`bath`) | stage 3 | its bottom is the fire chamber's roof: it carries the sand, which carries and warms the vessel |
| The vessel (`vessel`) | stage 3 | holds the charge, the acid, the gold |
| The fire door (`door`) | stage 3; then depth: slid down its guides from open (0, the rest pose) to shut (1), by the stoker's hand on its handle | shuts the fire door's opening: the fire then draws only up through the grate |
| The damper (`damperopen`, `damperhalf`, `dampershut`) | fitted with stage 3; then the stoker: one of its three places | sets the draught |
| The water line (`pipecopper` or `pipelead`) | fitted by hand: the pipe's metal | carries the water from the player's pipe to the cock |
| The cock's key (`cockshut`, `cockopen`) | gameplay: open while the vessel is washed | open, the water runs from the nozzle into the vessel |
| The fire (`fuel`, `fire`) | laid by hand through the fire door; lit | the bed on the grate; burning, it glows and drops cinders into the ash pit |
| The contents (`charge`, `acid`, `solution`, `gold`, `wash`) | gameplay's charge: the dore put in, the acid poured, the silver dissolved, the solution drawn off and the gold washed, the gold taken out | drawn at fixed levels: the acid two-thirds full, the wash water a voxel under it |
| The cap (`outletcap`) | fitted on every vessel in a row but the one carrying the chimney | closes the outlet |
| The draught (no part) | the chimney on the outlet, natural draught only | the fire's: in at the ash mouth, up through the grate, out at the flue into the stack; the hood's: in at its open front, over the vessel, out at its flue into the stack |

### Regenerating

```sh
python3 mods-src/seraphhorizons/AcidParting/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/AcidParting/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/AcidParting/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run takes about half a minute and is deterministic (two runs into two folders are byte-identical; the logs
differ only in the paths they name). It exits non-zero if a check fails (`tools/validate_acidparting.py`). The brick
types and every texture are checked against the game's files when they are at hand (`$VINTAGE_STORY`, else
`~/Games/vintagestory`); without them that check says it was skipped.

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **The stages:** at each stage (the iron frame alone, with the brickwork and the hood's lintel, with the lining, with
  the fittings) nothing floats: every element drawn is joined to the ground. The frame is iron only and no thin post
  in it stands taller than half the vessel; the hood's lintel comes with the brickwork; the ties are hidden in the
  brickwork once it is in (points just off every face of every tie lie in brick or other iron), none in the lining's
  place.
- **Textures by role:** every element's texture is the one its role names.
- **Containment:** nothing leaves the four cells over the door's whole travel (every 0.01), so nothing reaches the
  chimney's cell or the water pipe's.
- **A flat top:** every half voxel of the top is brick, the curb, flush iron or the pipe's end flush at the water face,
  but for the outlet's seat.
- **A row:** each side face is plain brickwork over its whole area (8192 points), nothing else reaches either side
  plane at any depth, and two vessels side by side touch nowhere but at their shared walls.
- **Centred, walls:** the fire chamber, the ash mouth, the fire door, the recess, the bath, the vessel, the hood, the
  pipe, the stack and the curb are centred left to right; the brick beside the fire chamber, sampled every half voxel
  from the grate to its top, runs at least `MIN_WALL` (4.5) to each side face and `FRONT_WALL` (3) to the recess.
- **The lining:** behind every hot face (the fire chamber's walls from the bearers up, the fire's flue, the stack; 882
  points, walked out every quarter) the brick is the lining's for at least `LINING_T` (3), or to where the brick ends;
  the ash pit's, the ash mouth's and the hood's faces are the brickwork's.
- **The stages' counts:** the brick boxes' volumes add up to the lattice's, the brickwork's and the lining's apart, and
  give the rig's quantities at the game's rates, rounded up; the proposals are the generator's constants.
- **The fire door:** at every depth in its recess between its guides; shut it covers its opening with 0.5 round, open
  it is clear above it; the only part that moves; θ moves nothing.
- **The damper:** in each place a plate in the recess between the guides; shut it covers the whole ash mouth, half its
  upper half, open none of it; never reaches the fire door.
- **The fire:** the bed on the grate to the fire door's sill, wall to wall, its lumps under the flue's top and the
  door's; the burning bed the same boxes, its cinders on the ash pit's floor; every firebar on both bearers, the
  bearers built into the walls, the bars' gaps (1.07) wide enough for the draught.
- **The sand bath:** its bottom the fire chamber's roof, bearing on the lining all round by at least a voxel (1.5),
  its rim flush with the bench; sand round the vessel with no gap (sampled on three levels); the vessel set in the
  sand, under the hood, its mouth in its cell.
- **The contents:** inside the vessel, within its inner apothem, over its floor and under its lip; the acid and the
  solution at one level, the wash water under them.
- **The water line:** ppex's section (6 across, four walls) from the water face at the cell's centre through the
  roof (its way through the roof the pipe's own) to the brass cock, a wiped joint there; lead the copper part's
  elements in lead; the nozzle over the vessel's mouth, inside it, over the lip; the key's boss on the cock's body,
  the lever across the flow shut and along it open, clear of the vessel; open, the stream runs from the nozzle's tip
  into the vessel's water.
- **The outlet:** open, clear to the sky through the stack; the cap flush in the curb's seat over the stack's mouth;
  the curb the lining's brick, rim and ledge.
- **Draught:** sampled points along the fire's way (in at the ash mouth, up through every gap of the grate, over the
  fuel, out at its flue, up the stack) and the hood's (in at its front, under the roof, out at its flue) lie in no
  element built in; the flues and the stack run into each other; the stack and the outlet's mouth clear at every depth
  but for the cap.
- **Clearances:** in each of sixteen scenes (every state of the vessel, of the fire and of the damper, both metals of
  the water line, both places of the cock, the cap, and the three stages before the build is complete), no two parts
  overlap, and the door, slid every 0.02 of depth, touches nothing.
- **No z-fighting** at 27 poses (nine scenes, three depths) with only what each shows, after the fix (80 faces pressed
  against their own part removed).
- **Files:** every texture code declared; glow on the burning fuel only; the frame's shape the iron frame alone; no
  power cell, no air port, no chute or return-water anchor; the four cells with boxes, decks on the top two; the
  outlet on the back row's top under the chimney's cell, which is not the machine's; the chimney's line six blocks
  from the outlet up; the water face the top of the front row under the pipe's cell, which is not the machine's; the
  vessel's cell worked from the front, its mouth in it, the cock's tip over it; the fire door and the ash mouth on the
  controller's front face, the ash mouth under the door; the shipped files are the checked model.

`tools/tests/test_acidparting_model.py` holds the written files to these without the generator (the rig's vocabulary,
the reference poses, the cells rebuilt from the shape, the textures the game's, only the fire glowing, the door alone
moving in its recess, the water line at the water face and in both metals, the anchors, the side faces, the stages'
counts from the shape's brick, the brick types, and `site/models.json`'s states and Play against the generator's);
`site/test/acidparting.test.ts` replays the reference poses in the viewer's maths and holds the page's Brick select and
Build states to the rig.

### Rig schema (`acidparting-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"), as the crucible furnace uses it, with
nothing added to the shared rig maths: two inputs, and parts gated by `requires`.

- **Inputs.** depth, the fire door (0 open, 1 shut); θ, the stoker's clock, read at ratio 0. No work, class, presence,
  feed or oil.
- **Drivers.** The fire door: a `rotate` of ratio 0 (θ), then a `step` slide along y, −3.75 voxels over depth 0..1
  (from open to shut). Every other part has none.
- **`requires`** values: the stages, `brickwork` (the `brickwork` and `hoodiron` parts), `lining` and `fittings` (the
  `bath`, `vessel` and `door` parts); `damperopen`, `damperhalf`, `dampershut` (one at a time); `pipecopper`,
  `pipelead` (one at a time); `cockshut`, `cockopen` (one at a time); `fuel`, `fire`; `charge`, `acid`, `solution`,
  `gold`, `wash`; `outletcap`. The iron frame has none.
- **Keys:** `cells`, `vesselCell`/`vesselFace`, `vessel`, `firedoor`/`firedoorSide`, `ash`/`ashSide`, `cock`,
  `waterCell`/`waterFace`, `outletCell`/`outletFace`, `chimney`, `build`, `bricks`, `parting` and `parts`.
- **`build`**: `frame` (`recipe` grid, `tool` hammer, `ingredients`), `stages` in order (`requires`, `materials` as
  `{ code, quantity }`, the brickwork's and the lining's `volumeBlocks` and `perClick` `stack`, the brickwork's
  `oneType` and `alsoParts` (the hood's lintel), the fittings' `parts` and `also` (the damper's places)), `waterLine`
  (`parts` by metal, `also` the cock's places, `materials`), `outletCap`.
- **`bricks`**: the crucible furnace's: `code` (`brick`), `item` (`game:burnedbrick-{type}`), `default` (`red`), and
  `types`, each with its `block`, `base` and, for the coloured courses, `overlays`.

**In the viewer** (`site/models.json`, `acid-parting`): the "Fire door" slider is depth, and Play opens and shuts it
(open 2 s, shut in 1 s, firing 3 s, open in 1 s). Six selects of states: **Build** (the iron frame, 1: brickwork, 2:
lining, 3: fittings, complete, each with its materials, the page opening complete), **Water line** (not fitted, copper
or lead, the cock shut or open), **The vessel** (empty, charged, the acid poured on, parting, washing, parted), **The
fire** (none, laid, burning), **Ash-pit damper** (open, half, shut) and **Outlet** (open or capped). The first four after
Build wait for the vessel to be built, the outlet for the lining. A **Brick** select offers the rig's nine brick types,
as the crucible furnace's does. The viewer draws flat colours: the codes the colour table does not name (the liquids,
the dore) take hashed colours; no shared colour entry was added (one agent owns that table).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`hi_*` the hood's lintel, `ln_*` the lining (`ln_brick_NNN`), `bath_*` (`tray`, `rim_*`, `sand_*`), `vessel_*`
(`bottom`, `wall`, `lip`), `door_*` (`plate`, `handle`), `damperopen_*`, `damperhalf_*`, `dampershut_*` (`plate`,
`knob`), `pipecopper_*` and `pipelead_*` (`inlet_*`, `joint_cock*`, `cock_body`, `cock_nozzle`), `cockshut_*` and
`cockopen_*` (`boss`, `lever`, open's `stream`), `fuel_*` and `fire_*` (`bed`, `lump*`, burning's `cinder*`),
`charge_*` (`layer`, `grain*`), `acid_*`, `solution_*`, `wash_*` (`surface`), `gold_*` (`cake`, `lump*`),
`outletcap_*`; and `fr_*` the iron frame (`fr_bearer`, `fr_bar`, `fr_lintel_ash`, `fr_lintel_door`, `fr_guide`,
`fr_strap`, `fr_kerb`, `fr_tie_*`). Hand edits are lost when the script runs again.

## Salvaged, and what is new

From `origin/741-parting-furnace-model-v2` (evaluated in `evals/741-parting.md`): `build_acid`'s layout (a firebox, a
cast-iron tray of sand on it with its rims, the vessel set in the sand with its rim and the acid, a hood on two piers,
a brass cock with a nozzle over the vessel), rebuilt at this footprint's scale, and `pipe_boxes`, `build_pipes` and
`pipe_copies` (the soldered water line in each metal), turned to run down from the top; from its validator, the water
line's checks (`check_pipes`, the water half of `check_anchors`) and the cock's flow (`check_flows`). The v2 hood leaned
on a shared stack: this one has its own flue, stack and outlet, as the issue asks. The v2 frame (the paved floor, the
shared stack) and everything of the other three furnaces are not used.

From `CrucibleFurnace/tools/` (copied, not imported, as the brief asks): the box helpers (`skin`, `world_skin`,
`wbox`, `oct_disc`, `oct_slab`, `oct_tube`, `lump`, `bar`, `split_blocks`), the binding and ties, the brick lattice and
its merge (`brick_voids`, `lining_zones`, `brick_lattice`, `build_brick`), `build_damper`, `build_cap`, the counts
(`build_volumes`, `build_counts`, `build_json`, `bricks_json`), `make_rig`'s shape, `shipped`, `shipped_cells`,
`check_shipped`, `fix_coplanar`, the glow value, and the validator's catalogue (stages, textures by role, sides,
walls, lining, counts, damper, outlet, draught, clearances, z-fighting, bricks against the game's files).

New: the fire door sliding in the recess with the damper, the sand bath let into the bench, the porcelain vessel and
its contents, the hood with its lintel and flue, the water line's vertical run and the plug cock with its two places,
the fire's own flue, and the checks for them.

## Known weak spots, and what is not checked

Reviewed in projections rendered from the written files only; not yet in the viewer with the owner, and not in a
client.

1. **Thin walls.** The fire chamber's side walls are 4.5 (a half-brick skin of 1.5 outside the lining), the front 3
   behind the recess, against the crucible furnace's 10: the price of one block's width. In a row two skins meet, 9
   between fires.
2. **The lining shows in the front recess** between the damper and the fire door, and round the door's opening: the
   front wall behind the recess is all lining.
3. **One texture a brick type**, world-mapped across 128 boxes, as the crucible furnace's; seams not seen in the game.
4. **Contents at fixed levels**: the acid two-thirds full and the gold a fixed cake whatever the charge.
5. **The glow is fixed**: 200 on the burning fuel, whatever the temperature.
6. **Whole-cell boxes**: every cell's box is the whole cell, so the hood's opening is not open to collision.
7. **The pipe is ppex's section** (6 across) all the way to the cock, large over a vessel 7 across; most of it is in
   the roof.

## Open for the owner

1. **Build stages** (open on #932): the frame's recipe, the fittings' and the water line's materials are proposals; the
   vessel's own item is open (the game has no porcelain vessel; a fired clay crucible or jug could stand in); whether
   the water line is a stage or a fitting, as here.
2. **Where the silver comes out.** #931 has the silver out by hand, but no precipitating tub is drawn (v2's open item
   #3, still live): the solution is drawn off and the silver thrown down off the model. A small vessel beside it does
   not fit one block wide.
3. **#931's open items** reach the model: a dore item, or the still's sponge and the cupel's bead as they are (the
   charge is drawn as granulated dore, `electrum`); whether the acid is consumed (it is drawn as contents, not an item;
   a stoneware acid jar could be a fitting if it is).
4. **The water face is the top of the front row** (`up`), under the player's pipe: the sides are the row's, the front
   is the stoker's (a pipe in front of the hood would stand in the stoker's way at head height) and the back is the
   stack. v2 had it on the front at the hood. Provisional pending #711.
5. **The fire door slides** in guides rather than swinging on hinges: a hinged door would swing out into the stoker's
   cell, outside the footprint. It shares one recess with the damper.
6. **The three-place damper** is the crucible furnace's convention; a period sand-bath fire set its draught with the
   ash-pit door. Kept, as the issue's conventions ask; easy to drop.
7. **The hood is not lined** (fumes, not heat) and not acid-proofed (period hoods were sometimes lead-lined); its
   opening is 12 high so the cock shows, the breast 4.
8. **One vessel a unit** (the Mint's baths held twelve): more vessels are more units in a row.
9. **The minimum chimney** is the crucible furnace's six blocks.
10. **The gold is drawn as gold** (the period's parted gold is a brown spongy mass) and the fuel as charcoal.
11. **The static and moving split** when it is drawn in the game: the fire door is the only moving part.
