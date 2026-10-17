# Acid parting vessel: the model

Part of the Seraph Horizons mod's ore processing (epic #684): the generated model of the acid parting vessel (#932),
the machine of #931, which takes dore (gold and silver together) in by hand and gives the gold and the silver out by
hand, batch. The line, the glossary and the period sources are #939; the power and water budget #940.

**Status: in the game** (#931). This folder's `tools/` holds the model's generator and its checks; `Core/` and `Game/`
hold the vessel's gameplay, on the shared ore machine code (`Machines/`): the mod README's "Ore processing: the acid
parting vessel" has the rules, and "Wired into the game", below, what reads what. The build stages are the owner's
(2026-10-15, below).

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` and `CrucibleFurnace/`, this folder's neighbours. The generic half of the
generator is `Machines/tools/machinegen/`, unchanged: no new driver and no change to the package.

## The vessel

Nitric acid parting as the mints and refineries did it in the late nineteenth century (Rose 1898, pp. 398-400): the
granulated alloy in "cylindrical vessels of earthenware, porcelain, or platinum", set in a cast-iron sand bath over a
small fire, under a brick hood drawing to the chimney. The silver-bearing acid is drawn off into a separate tub, where
copper plates throw the silver down as a grey sludge, which is collected, washed and melted. The gold left in the vessel
is washed with water. The Philadelphia Mint's porcelain pots held 2,080 oz, twelve to a bath.

The model is **one vessel and its tub**, in one block of brickwork **one block wide (x), two deep (z) and two high**,
bound at its corners and all four top edges with angle iron, one closed frame. Its front faces the player.

- **The fire**, under the vessel in the front row's lower cell.
  - A grate of four iron **firebars** across two iron **bearers** that run front to back, their ends built into the
    walls.
  - Under it the **ash pit**, reached through the **ash mouth** (5 wide, 2.5 high, an iron lintel over it).
  - Over it the **fire chamber**, 7 wide, 8 deep and 6 high, its roof the sand bath's iron bottom.
  - The fuel goes in through the **fire door**, an opening 4 wide and 3 high over the ash mouth.
  - Both openings run through a **front wall 8 deep**: the cold front of the bench, where the tub stands, is over it.
  - Both are in **one recess** in the front face, centred. Its iron **guides** carry the **damper** over the ash mouth
    (open, half, shut: the crucible furnace's three places) and the **fire door**, an iron plate that **slides up**
    open and down shut. Natural draught only.
- **The sand bath**: a cast-iron tray 10 by 11, let into the masonry over the fire.
  - Its bottom is the fire chamber's roof, bearing 1.5 on the chamber's walls all round.
  - Its rim is flush with the **bench**: the brick round it, a block up, which is the hood's floor.
  - Its sand stands half a voxel under the rim, round the vessel and half a voxel under it.
- **The vessel**: fired clay, the game's crucible (Rose's earthenware; the period's mints used porcelain), an
  octagonal barrel 7 across and 9.25 tall, with a flat bottom and a flared lip, set 1.25
  into the sand, centred on the bath and the fire.
  - The dore, the acid and the wash water go in at its mouth, and the parted gold comes out there.
  - Its contents are parts (below): the granulated dore, the acid, the silver solution, the parted gold, the wash water.
- **The precipitating tub** (#931's ruling) stands on the **cold front of the bench**, in front of the bath (1 voxel
  clear of it) and over the front wall, under the hood's open front.
  - A wooden vat of oak boards, 10 wide, 5 deep and 5 tall.
  - A **copper rod** rests across its two side rims, and **three copper plates** hang from it into the **liquor**: the
    solution drawn off.
  - The silver comes down on the plates and settles on the tub's bottom as a grey **sludge**.
  - The sludge is **the silver's hand-draw point** (`tubCell`, `tubFace`, `tub`). The gold stays in the vessel.
- **The siphon**, a lead pipe from the vessel to the tub.
  - Its leg dips into the vessel over the gold.
  - Its run lies on the vessel's lip and goes forward over the tub.
  - Its outlet leg dips into the tub's liquor, under the vessel's liquid surface, so it runs and draws the solution off.
- **The hood**: two brick **piers** (the 2.5-thick side walls) carry the **roof** and the **breast** over the open
  front.
  - The breast stands on an iron **lintel**, set a voxel back under the brick face.
  - The opening is 11 wide and 12 high, from the bench to the breast, so the vessel can be reached over the tub and the
    siphon passes over the tub.
  - The hood's back wall stands behind the bath, with a **flue** opening near its top (6 wide, 5 high) into the stack.
  - The hood is the brickwork's, not lined: it carries the acid's fumes, not the fire's heat.
- **Water: by bucket.** There is no pipe (the owner's ruling): #931's draw is 1 L for every 10 units, a bucket an
  ingot's worth. The wash water is poured into the vessel from a bucket, a hand action at the vessel, as the arrastra
  has. There is no water anchor; the wash water is drawn as contents.
- **The flue and the stack.**
  - The fire's flue leaves the fire chamber at the top of its back wall (4 wide, 3.5 high).
  - It runs back into a **stack** 6 square, centred in the back cell under the chimney's line, from the flue's floor up to an **outlet** on the
    top. The hood's flue joins it there too. All of it is lined.
  - The outlet's **curb** is the lining's brick, flush: a rim round a seat half a voxel deep. The **cap** is a flat iron
    plate set flush in the seat.
  - **The model stops at the outlet.** The player stands the chimney on it, at least six blocks counted from the outlet
    up (the crucible furnace's `MinChimneyHeight`), or caps it, as every vessel but one in a row sharing a chimney is.
- **The binding is angle iron** (the owner's rule for furnace-like brickwork).
  - Each of the four vertical corners has an L section 1 voxel thick, the block's full height, legs 2.5. One leg lies
    on the front or back face and one on the side face, its inside corner bedded against the brick's outside corner,
    so it locks the brick in on both faces.
  - Along all four top edges, an angle wraps the edge (a leg 3 down the face, a leg 2 on the top): a ring joining the
    corner angles. Each face leg runs between the corner angles' legs on its face and meets them end to end. The
    front's and the back's top legs run the block's width between the corner angles' side legs, which close their
    ends; the sides' top legs run between the front's and the back's. So the top is iron 2 wide all round, every
    corner is a closed joint with no brick between members, and the binding is one connected frame (owner's review:
    the top angles had stopped 1.5 short of the corner angles' side legs, and the front and the back were two pieces).
  - The brick is cut back by a leg's thickness where the iron lies, so the angles' outer faces are flush with the
    block's.
- **Vessels stand in a row** along their width (x), sharing walls.
  - The side faces are flush, plain brickwork, but for the corner angles' side legs and the side's top angle, flush
    with them.
  - Two vessels side by side meet only at their shared walls: brick against brick, angle against angle.
  - The flues between them are implied, not modelled.

**Not a furnace.** The issue puts it under the crucible furnace's conventions. It takes from `CrucibleFurnace/` only
what this vessel needs:
- **The fire:** a grate on bearers built into the walls, the ash mouth with its three-place damper, natural draught.
- **The outlet**, with its curb, its cap, and the chimney counted from it.
- **Brick where it is brick**, in its two roles: any fired brick for the brickwork, and tier-3 refractory lining 3
  voxels thick on the hot faces (the fire chamber, its flue, the stack and the curb).
- The staged build, with the brick counted from the model.
- The contents drawn as parts.
- The crucible's generator and checks: `world_skin`, the brick lattice, `fix_coplanar`, and the stage, lining and row
  checks.

It leaves out what belongs to a melting hole: the lid and its hinge, the pots, the coke bed, the hollow cells and the
decks over an open hole. The hood, the bench and the tub are not lined.

**Footprint and cells.** Four cells, none hollow (nothing moves outside the front cell's recess): the front row (the
fire, the bath, the vessel, the tub, the hood) and the back row (the stack).
- The **controller is the front cell at ground level**, `[0,0,0]`, the fire door's and the ash mouth's, so the build
  frame is the shipped frame.
- The cell over the outlet, `[0,2,1]`, is the chimney's, not the machine's.
- Placed like the crucible furnace, it extends away from the player along their line of sight.
- **The tub cost no growth.** The fire, the bath and the vessel moved back 4 voxels (the front wall grew from 4 to 8),
  and the tub took the front of the bench. The stack is 6 square, centred in the back cell under the chimney's line;
  the hood's chamber ends a voxel short of the stack's lining, so its back wall is brickwork in front of the lining.

**Why 1 × 2 × 2.** As small as the fire, the bath, the vessel, the tub, the hood and a lined stack allow:

- **One wide**:
  - A fire chamber 7 wide with walls of 4.5 (a half-brick skin of 1.5 outside 3 of lining) is 16.
  - The bath (10 wide) bears 1.5 on its walls, and the vessel (7 across) stands in it with a voxel of sand each side.
  - The tub is the bath's width.
  - The hood's piers (2.5) leave an opening 11 wide.
- **Two deep**:
  - The front row is the tub (5 deep) on the cold front of the bench, then the bath and the vessel over the fire.
  - The fire chamber's 8 deep sits behind a front wall of 8; its back wall runs into the back row.
  - The stack, 6 square with 3 of lining each side and brick behind, is the back row's rest, with the outlet on its top:
    "the outlet behind".
- **Two high**:
  - The ash pit, the grate and the fire chamber reach 13; the bath's tray 16 (the bench).
  - The vessel reaches 23.5, the hood's roof 32.
  - The bench is a block up, a working height, and the tub stands on it.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `vesselCell`, `vesselFace` | `[0,1,0]`, `north` | The hood's cell, the vessel worked through its open front: the dore, the acid and the wash water's bucket in, the gold out |
| `vessel` | `[0.5,1.4688,0.75]` | The middle of the vessel's mouth |
| `tubCell`, `tubFace` | `[0,1,0]`, `north` | The same cell and face: the precipitating tub at its front, the silver's hand-draw point |
| `tub` | `[0.5,1.3125,0.1875]` | The middle of the tub's mouth: the sludge drawn, the copper put in. A click in the hood's cell is the nearer of `vessel` and `tub`'s (the crucible furnace's two pots' rule) |
| `firedoor`, `firedoorSide` | `[0.5,0.625,0]`, `north` | The middle of the fire door's opening in the controller's front face: the fuel in |
| `ash`, `ashSide` | `[0.5,0.1406,0]`, `north` | The middle of the ash mouth in the controller's front face: the damper, the ash out |
| `outletCell`, `outletFace` | `[0,1,1]`, `up` | The stack's head, in the back row; the chimney (or the cap) on its top, the chimney from `[0,2,1]` up |
| `chimney` | origin `[0.5,5,1.5]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `build` | | The build's stages and their materials (below); not drawn |
| `bricks` | | The fired brick types and their textures (the crucible furnace's table); not drawn |
| `parting` | | `minChimney` 6, `rowMax` 6, `rowAxis` `x`; not drawn |

No water anchor (watered by bucket, at the vessel), no chute anchor (batch: rule 2) and no return-water anchor. The
validator fails if any appears. **Provisional pending #711:** the outlet and chimney anchors, which follow the crucible
furnace's.

**Boxes.** The cells' boxes are built from the vessel as built: the frame, the brick, the hood's lintel, the bath, the
vessel, the tub and the fire door shut. The splitter makes every cell one whole box. Both top cells have a `lid` (a
collision deck) at the top, so the roof is walked on whole.

## The build

The owner's stages (2026-10-15), each one `requires` value fitting its parts (the rig's `build`, in order), the
viewer's **Build** select stepping through them, a state a stage. The brickwork's and the lining's counts are
**measured from the model**: the brick lattice's boxes at the game's rates (2 fired bricks a block, 4 refractory bricks
a block, fire clay one per 2.5 of those), rounded up. The iron is the settled rate over the iron measured in the model
(128 voxels an ingot, a plate 2, rods counted, the rest nails and strips at 4 an ingot), plus 1 iron nails and strips
per 4 planks' worth of timber (a support beam 2), for the fastenings. Services last, as on every machine; nothing runs until they are in. "~" marks a choice of item.

| | Stage | `requires` | What it puts in | Materials |
|---|---|---|---|---|
| F | **Iron frame**: a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (the frame part) | The binding's eight angles (four corner, four top); the grate's bearers and firebars; the ash mouth's and the fire door's lintels and the recess's guides; standing on their own, on 6 short posts hidden in the brickwork | 2 iron rods, 2 iron plates, 6 iron nails and strips |
| 1 | **Brickwork** | `brickwork` (and the `hoodiron` part) | The base, the ash pit, the walls, the bench, the hood on its piers and the stack's outer walls: 1.69 blocks; with it, at no cost of its own, the hood's lintel | **4 fired bricks**, any one type |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on the hot faces (the fire chamber, its flue, the stack) and the outlet's curb: 0.90 blocks | **4 tier-3 refractory bricks**, **2 fire clay** |
| 3 | **Sand bath and doors** | `sandbath` (the `bath` and `door` parts; the damper in one of its places) | The cast-iron sand bath with its sand, its bottom bearing on the lined chamber's walls; the fire door in the recess's guides; the ash-pit damper | ~1 sand (`game:sand-*`), 5 iron nails and strips |
| 4 | **Vessel and tub** | `vesseltub` (the `vessel` and `tub` parts) | The parting vessel set in the bath's sand; the precipitating tub on the cold front of the bench | **1 fired crucible** (`game:crucible-*-fired`, any colour: the vessel), 1 iron nails and strips, 1 oak plank (the tub) |
| 5 | **Services** | `services` (the `siphon` part) | The lead siphon, hung on the vessel's lip with a leg in the vessel and one in the tub: it waits for both | 1 lead pipe section (`seraphhorizons:pipesection-lead`) |
| | *Copper plates* | `copper` | Contents, not a stage: hung in the tub by hand, worn away as the silver comes down | copper plates (gameplay's) |
| | *Outlet cap* | `outletcap` | Not a stage: put on (or taken off) by hand, any time after the lining | 1 iron plate |

The contents and the fire wait for the services; the damper comes with the sand bath and the fire door. The siphon is
lead only (the acid would eat copper), so services has no copper-or-lead choice.

**The ties.** So that nothing floats before the brick is in, the least iron joins each piece of the frame to the
ground. All of it is in the brickwork (voids are cut for it), so it is hidden once the brick is in:
- A post under each end of the ash mouth's lintel, behind the recess. The guides stand against both lintels' faces and
  carry the fire door's lintel.
- A post under each end of each bearer, in the front and back walls, half a voxel clear of the ash pit and the mouth.

The angles stand on the ground on their own. The hood's lintel comes with the brickwork that carries it, as the
crucible furnace's hinge does, so the frame has no tall thin post: its tallest thin piece is a guide, 14.75.

## No power, no forced air

Fired by hand on natural draught: no mechanical power (no `powerCell` or `powerFace`, no power ghost) and no air port.
The validator fails if either appears. As in the crucible furnace, the rig's θ is a clock that moves nothing: a
`rotate` of ratio 0 on the fire door carries it, so the viewer offers Play, which opens and shuts the door.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. **No new texture**: every texture is the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/acidparting.json` | The whole vessel, every part (266 elements) |
| `assets/seraphhorizons/shapes/block/acidparting_frame.json` | The iron frame only (36 elements): what the placed frame draws, before any stage |
| `assets/seraphhorizons/config/acidparting-rig.json` | Cells, anchors, the build and the brick types, and the part rig (23 parts) |
| `tests/AcidParting/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

**Textures** (all the game's):

| Code | Texture | On |
|---|---|---|
| `brick` | The brick type's block texture, set by the renderer (`bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork, world-mapped |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining and the curb, world-mapped |
| `iron` | `game:block/metal/plate/iron` | The frame and its angles, the hood's lintel, the sand bath's tray, the fire door, the damper, the cap |
| `sand` | `game:block/stone/sand/sandstone` | The sand bath's sand |
| `ceramic` | `game:block/clay/hardened/blue` | The vessel: fired clay, as the fired crucible it is built from |
| `oak` | `game:block/wood/planks/oak1` | The precipitating tub |
| `lead` | `game:block/metal/sheet-plain/lead4` | The siphon |
| `copper` | `game:block/metal/plate/copper` | The copper plates and their rod |
| `silver` | `game:block/metal/ingot/silver` | The silver sludge |
| `fuel` / `ember` | `game:block/coal/charcoal` / `game:block/coal/ember` | The fire laid / burning; burning, its faces glow 200 (the crucible's coke) |
| `dore` | `game:block/metal/ingot/electrum` | The granulated dore |
| `gold` | `game:block/metal/ingot/gold` | The parted gold |
| `acid` | `game:block/liquid/brine` | Aqua fortis, pale yellow |
| `solution` | `game:block/liquid/dye/woad` | The acid with the silver (and the dore's copper) in it, blue: in the vessel, and drawn off in the tub (`liquor`) |
| `water` | `game:block/liquid/waterportion` | The wash water |

### How it works

Nothing turns. The fire door is the only part that moves; everything else that shows or not is a `requires`,
gameplay's state.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The iron frame (`frame`, no requires) | fixed: placed as the machine | carries the grate and the recess's guides; its angles lock the brick in at the corners and the top edges; its ties hold it up before the brick is in |
| The brickwork (`brickwork`) and the hood's lintel (`hoodiron`) | stage 1 | the base, the ash pit, the walls, the bench, the hood and its breast, the stack's outer walls; plain brick on the side faces for a row |
| The lining (`lining`) | stage 2 | the hot faces: the fire chamber, its flue, the stack; the outlet's curb |
| The bearers and firebars (`frame`) | fixed: the bearers built into the front and back walls, the bars across them | carry the fuel; the draught comes up between the bars |
| The sand bath (`bath`) | stage 3, `sandbath` | its bottom is the fire chamber's roof: it carries the sand, which carries and warms the vessel |
| The vessel (`vessel`) | stage 4, `vesseltub`: a fired crucible | holds the charge, the acid, the gold, the wash water |
| The precipitating tub (`tub`) | stage 4, `vesseltub` | holds the liquor, the copper and the sludge, off the heat |
| The siphon (`siphon`) | stage 5, `services`: hung over the vessel's lip, so after the vessel and the tub | draws the solution off the vessel into the tub, its outlet under the vessel's surface |
| The fire door (`door`) | stage 3, `sandbath`; then depth: slid down its guides from open (0, the rest pose) to shut (1), by the stoker's hand on its handle | shuts the fire door's opening: the fire then draws only up through the grate |
| The damper (`damperopen`, `damperhalf`, `dampershut`) | fitted with stage 3; then the stoker: one of its three places | sets the draught |
| The fire (`fuel`, `fire`) | laid by hand through the fire door; lit | the bed on the grate; burning, it glows and drops cinders into the ash pit |
| The vessel's contents (`charge`, `acid`, `solution`, `gold`, `wash`) | gameplay's charge: the dore put in, the acid poured, the silver dissolved, the solution drawn off and the gold washed (from a bucket), the gold taken out | drawn at fixed levels: the acid two-thirds full, the wash water a voxel under it |
| The tub's contents (`copper`, `liquor`, `silver`) | the copper hung by hand; the solution drawn off; the silver precipitated; the sludge drawn by hand | the copper throws the silver down onto the tub's bottom |
| The cap (`outletcap`) | fitted on every vessel in a row but the one carrying the chimney | closes the outlet |
| The draught (no part) | the chimney on the outlet, natural draught only | the fire's: in at the ash mouth, up through the grate, out at the flue into the stack; the hood's: in at its open front over the tub, over the vessel, out at its flue into the stack |

### Regenerating

```sh
python3 mods-src/seraphhorizons/AcidParting/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/AcidParting/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/AcidParting/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run takes about half a minute and is deterministic: two runs into two folders are byte-identical, and the logs
differ only in the paths they name. It exits non-zero if a check fails (`tools/validate_acidparting.py`). The brick
types and every texture are checked against the game's files when they are at hand (`$VINTAGE_STORY`, else
`~/Games/vintagestory`); without them that check says it was skipped.

- **Parts:** every element is in its intended part; no part is without elements; Euler angles round-trip.
- **The stages:** at each stage (the iron frame alone; with the brickwork and the hood's lintel; with the lining; with
  the sand bath; with the vessel and the tub; with the services, the siphon on the vessel's lip) nothing floats: every
  element drawn is joined to the ground.
  - The frame is iron only, and no thin post in it stands taller than half the vessel.
  - The hood's lintel comes with the brickwork.
  - The ties are hidden in the brickwork once it is in, none in the lining's place.
- **Textures by role:** every element's texture is the one its role names.
- **Containment:** nothing leaves the four cells over the door's whole travel (every 0.01), so nothing reaches the
  chimney's cell.
- **A flat, whole top:** every half voxel of the top is brick, the curb or flush iron (the angles' tops), but for the
  outlet's seat.
- **A row:** each side face is plain brickwork over its whole area (8192 points) but for the corner angles' side legs
  and the side's top angle.
  Nothing else reaches either side plane at any depth, and two vessels side by side touch nowhere but at their shared
  walls.
- **The binding** *(owner's rule)*: eight angles, each two legs 1 thick meeting in the corner, each leg's outer face
  flush with one face of the brick corner it wraps. Points behind every leg's inner face are brick or iron set into it
  (at least nine in ten brick). The corner angles stand the full height, and no flat strap or band is left.
  - **Joints** *(owner's review)*: each top angle's two ends meet an upright (share a face with the corner angle at
    that end), and each leg's end face is closed: points just past it, every half voxel over it, are all in the
    binding's iron, so no brick shows between members at a corner.
  - **One frame:** the angles, joined by shared faces, are one connected piece.
- **Centred, walls:** the fire chamber, the ash mouth, the fire door, the recess, the bath, the vessel, the tub, the
  hood, the stack and the curb are centred left to right. The brick beside the fire chamber, sampled every half voxel
  from the grate to its top, runs at least `MIN_WALL` (4.5) to each side face and `FRONT_WALL` (7) to the recess.
- **The lining:** behind every hot face (the fire chamber's walls from the bearers up, the fire's flue, the stack; 834
  points, walked out every quarter) the brick is the lining's for at least `LINING_T` (3), or to where the brick ends.
  The ash pit's, the ash mouth's and the hood's faces are the brickwork's.
- **The stages' counts:** the brick boxes' volumes add up to the lattice's, the brickwork's and the lining's apart, and
  give the rig's quantities at the game's rates, rounded up. The stages are the owner's order, one `requires` value
  each fitting exactly its parts: the bath and the door, then the vessel (from a fired crucible) and the tub, then the
  siphon; there is no water line.
- **The fire door:** at every depth it is in its recess between its guides; shut, it covers its opening with 0.5 round;
  open, it is clear above it. It is the only part that moves, and θ moves nothing.
- **The damper:** in each place it is a plate in the recess between the guides. Shut it covers the whole ash mouth, at
  half its upper half, open none of it. It never reaches the fire door.
- **The fire:**
  - The bed lies on the grate to the fire door's sill, wall to wall, its lumps under the flue's top and the door's.
  - The burning bed is the same boxes, its cinders on the ash pit's floor.
  - Every firebar lies on both bearers, the bearers are built into the walls, and the bars' gaps (1.07) are wide
    enough for the draught.
- **The sand bath:**
  - Its bottom is the fire chamber's roof, bearing on the lining all round by at least a voxel (1.5).
  - Its rim is flush with the bench.
  - The sand fills round the vessel with no gap (sampled on three levels).
  - The vessel is set in the sand, under the hood, its mouth in its cell.
- **The contents:** inside the vessel, within its inner apothem, over its floor and under its lip. The acid and the
  solution are at one level, the wash water under them.
- **The tub** *(#931 ruling)*:
  - Where `TUB` puts it, on the brickwork's bench, in front of the bath and the fire (nothing hot under it), under the
    hood's open front, its mouth in its cell.
  - The siphon's leg is inside the vessel, over the gold and the charge, dipping under the acid's surface; its run lies
    on the vessel's lip; its outlet leg is inside the tub, under the liquor's surface and the vessel's (so it runs),
    over the sludge. It touches no part of the hood.
  - The tub's liquor stands lower than the vessel's.
  - The copper rod rests on the tub's two side rims; each plate hangs from it into the liquor, inside the tub, clear of
    the sludge.
  - The sludge lies on the tub's floor, inside it, and the liquor is the tub's inside at its level.
- **The outlet:** the stack's mouth (6) and the curb (10) are square and centred on the chimney's line, which runs
  through the middle of the chimney's cell `[0,2,1]` (the owner's note on the liquation furnace). Open, it is clear to
  the sky through the stack; the cap is flush in the curb's seat over the stack's
  mouth; the curb is the lining's brick, rim and ledge.
- **Draught:** sampled points lie in no element built in, the tub and its siphon among them:
  - along the fire's way (in at the ash mouth, up through every gap of the grate, over the fuel, out at its flue, up
    the stack);
  - along the hood's (in at its front over the tub, under the roof, out at its flue).

  The flues and the stack run into each other, and the stack and the outlet's mouth are clear at every depth but for
  the cap.
- **Clearances:** no two parts overlap in each of 27 scenes: every state of the vessel, of the tub (with the vessel
  parting and washing), of the fire and of the damper; the cap; and the five stages before the build is complete.
  The only overlaps allowed are the siphon's legs through a liquid's surface and the plates through the tub's liquor.
  The door, slid every 0.02 of depth, touches nothing.
- **No z-fighting** at 33 poses (eleven scenes, three depths) with only what each shows, after the fix (117 faces pressed
  against their own part removed).
- **Files:**
  - Every texture code is declared; only the burning fuel glows; the frame's shape is the iron frame alone.
  - There is no power cell, no air port, no chute or return-water anchor, and no water anchor or water line.
  - The four cells all have boxes, with decks on the top two.
  - The outlet is on the back row's top, under the chimney's cell, which is not the machine's; the chimney's line runs
    six blocks from the outlet up.
  - The vessel's cell and the tub's are worked from the front, each mouth in it, the tub in front of the vessel and
    under its mouth.
  - The fire door and the ash mouth are on the controller's front face, the ash mouth under the door.
  - The shipped files are the checked model.

`tools/tests/test_acidparting_model.py` holds the written files to these without the generator:
- the rig's vocabulary, the reference poses, and the cells rebuilt from the shape;
- the textures are the game's and only the fire glows;
- the door alone moves, in its recess;
- the tub stands in front of the vessel with the siphon between them, there is no water line, and the anchors are
  where the contract puts them;
- the angles wrap the corners, the top angles close every corner in one connected frame, and the side faces are plain
  brick for a row;
- the chimney's line is through the middle of its cell, the stack's mouth and the curb square and centred on it;
- the stages are the owner's, in order, each fitting its parts; their counts come from the shape's brick, and the
  brick types are the game's;
- `site/models.json`'s states and Play match the generator's.

`site/test/acidparting.test.ts` replays the reference poses in the viewer's maths and holds the page's Brick select
and Build states to the rig.

### Rig schema (`acidparting-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"), as the crucible furnace uses it, with
nothing added to the shared rig maths: two inputs, and parts gated by `requires`.

- **Inputs.** depth, the fire door (0 open, 1 shut); θ, the stoker's clock, read at ratio 0. No work, class, presence,
  feed or oil.
- **Drivers.** The fire door has a `rotate` of ratio 0 (θ), then a `step` slide along y, −3.75 voxels over depth 0..1
  (from open to shut). Every other part has none.
- **`requires`** values:
  - the stages, in order: `brickwork` (the `brickwork` and `hoodiron` parts), `lining`, `sandbath` (the `bath` and
    `door` parts), `vesseltub` (the `vessel` and `tub` parts) and `services` (the `siphon` part);
  - `damperopen`, `damperhalf`, `dampershut` (one at a time);
  - `fuel`, `fire`;
  - the vessel's `charge`, `acid`, `solution`, `gold`, `wash`;
  - the tub's `copper`, `liquor`, `silver`;
  - `outletcap`.

  The iron frame has none.
- **Keys:** `cells`, `vesselCell`/`vesselFace`, `vessel`, `tubCell`/`tubFace`, `tub`, `firedoor`/`firedoorSide`,
  `ash`/`ashSide`, `outletCell`/`outletFace`, `chimney`, `build`, `bricks`, `parting` and `parts`.
- **`build`**:
  - `frame` (`recipe` grid, `tool` hammer, `ingredients`);
  - `stages` in order: each with `requires` and `materials` as `{ code, quantity }`; the brickwork's and the lining's
    `volumeBlocks` and `perClick` `stack`; the brickwork's `oneType` and `alsoParts` (the hood's lintel); the later
    stages' `parts`, and the sand bath's `also` (the damper's places);
  - `outletCap`.
- **`bricks`**: the crucible furnace's: `code` (`brick`), `item` (`game:burnedbrick-{type}`), `default` (`red`), and
  `types`, each with its `block`, `base` and, for the coloured courses, `overlays`.

**In the viewer** (`site/models.json`, `acid-parting`):
- The "Fire door" slider is depth. Play opens and shuts it: open 2 s, shut in 1 s, firing 3 s, open in 1 s.
- Six selects of states, the vessel's, the tub's and the fire's waiting for the vessel to be finished (the services):
  - **Build:** the iron frame, 1: brickwork, 2: lining, 3: sand bath and doors, 4: vessel and tub, 5: services
    (complete), each with its materials; the page opens complete.
  - **The vessel:** empty, charged, the acid poured on, parting, washing, parted.
  - **The tub:** empty, copper hung, the solution drawn off, precipitating, the sludge to draw.
  - **The fire:** none, laid, burning.
  - **Ash-pit damper:** open, half, shut; it waits for the sand bath and doors.
  - **Outlet:** open or capped; it waits for the lining.
- A **Brick** select offers the rig's nine brick types, as the crucible furnace's does.
- The viewer draws flat colours, and the codes the colour table does not name (the liquids, the dore) take hashed
  colours. No shared colour entry was added (one agent owns that table).

### Editing by hand

Element names are the rig's interface: first-match globs, in the rig's order.

| Glob | Part | Elements |
|---|---|---|
| `bk_*` | brickwork | `bk_brick_NNN` |
| `hi_*` | hood's lintel | |
| `ln_*` | lining | `ln_brick_NNN` |
| `bath_*` | sand bath | `tray`, `rim_*`, `sand_*` |
| `vessel_*` | vessel | `bottom`, `wall`, `lip` |
| `tub_*` | tub | `floor`, `n`, `s`, `w`, `e` |
| `siphon_*` | siphon | `vesselleg`, `run`, `tubleg` |
| `door_*` | fire door | `plate`, `handle` |
| `damperopen_*`, `damperhalf_*`, `dampershut_*` | damper | `plate`, `knob` |
| `fuel_*`, `fire_*` | fire | `bed`, `lump*`, burning's `cinder*` |
| `charge_*` | charge | `layer`, `grain*` |
| `acid_*`, `solution_*`, `wash_*` | liquids | `surface` |
| `gold_*` | gold | `cake`, `lump*` |
| `copper_*` | copper | `rod`, `plate*` |
| `liquor_*` | liquor | `surface` |
| `silver_*` | silver | `sludge`, `lump*` |
| `outletcap_*` | cap | |
| `fr_*` | iron frame | `fr_angle_<n\|s><w\|e>_<a\|b>`, the corner angles' two legs; `fr_band_<n\|s>_<a\|b>`, the top angles'; `fr_bearer`, `fr_bar`, `fr_lintel_ash`, `fr_lintel_door`, `fr_guide`, `fr_tie_*` |

Hand edits are lost when the script runs again.

## Salvaged, and what is new

From `origin/741-parting-furnace-model-v2` (evaluated in `evals/741-parting.md`):
- `build_acid`'s layout: a firebox, a cast-iron tray of sand on it with its rims, the vessel set in the sand with its
  rim and the acid, a hood on two piers. Rebuilt at this footprint's scale.
- Its flow check (`check_flows`), which became the siphon's.

The v2 hood leaned on a shared stack; this one has its own flue, stack and outlet, as the issue asks. The v2 water
line (`build_pipes`, `pipe_copies`) was taken at first and then removed by the bucket ruling. The v2 frame (the paved
floor, the shared stack) and everything of the other three furnaces are not used.

From `CrucibleFurnace/tools/`, copied, not imported, as the brief asks:
- The box helpers: `skin`, `world_skin`, `wbox`, `oct_disc`, `oct_slab`, `oct_tube`, `lump`, `bar`, `split_blocks`.
- The ties.
- The brick lattice and its merge: `brick_voids`, `lining_zones`, `brick_lattice`, `build_brick`.
- `build_damper`, `build_cap`.
- The counts: `build_volumes`, `build_counts`, `build_json`, `bricks_json`.
- `make_rig`'s shape, `shipped`, `shipped_cells`, `check_shipped`, `fix_coplanar`, the glow value.
- The validator's catalogue: stages, textures by role, sides, walls, lining, counts, damper, outlet, draught,
  clearances, z-fighting, bricks against the game's files.

New:
- The fire door sliding in the recess with the damper.
- The sand bath let into the bench.
- The fired clay vessel and its contents.
- The precipitating tub with its siphon, copper and sludge.
- The hood with its lintel and flue, and the fire's own flue.
- The angle-iron binding.
- The checks for all of these.

## Known weak spots, and what is not checked

Reviewed in projections rendered from the written files and in the viewer; not in a client.

1. **Thin side walls.** The fire chamber's side walls are 4.5 (a half-brick skin of 1.5 outside the lining), against
   the crucible furnace's 10: the price of one block's width. In a row two skins meet, 9 between fires.
2. **A deep stoke hole.** The fire door and the ash mouth run 8 deep through the front wall, which carries the tub.
3. **The lining shows in the front recess** between the damper and the fire door.
4. **One texture a brick type**, world-mapped across 128 boxes, as the crucible furnace's; the seams are not seen in
   the game.
5. **Contents at fixed levels**: the acid two-thirds full, the gold a fixed cake, the sludge a fixed layer, whatever
   the charge.
6. **The glow is fixed**: 200 on the burning fuel, whatever the temperature.
7. **Whole-cell boxes**: every cell's box is the whole cell, so the hood's opening is not open to collision.
8. **The siphon is always in place** once fitted, its leg in the vessel. The period put it in to draw off.

## Open for the owner

1. **Build stages**: settled (2026-10-15), above. The copper plates are contents, put in by hand; their item and how
   fast they wear are gameplay's. The sand bath's cast-iron tray is paid as nails and strips at the settled rate, though
   the period cast it.
2. **#931's open items** reach the model:
   - A dore item, or the still's sponge and the cupel's bead as they are? The charge is drawn as granulated dore
     (`electrum`).
   - Is the acid consumed? It is drawn as contents, not an item; a stoneware acid jar could be a fitting if it is.
3. **The tub's place.** It is on the cold front of the bench, inside the footprint. The fire, the bath and the vessel
   moved back 4 voxels for it, so the front wall is 8 deep. Growing the footprint was the other way: a second column
   beside the vessel, 4 cells more.
4. **The fire door slides** in guides rather than swinging on hinges: a hinged door would swing out into the stoker's
   cell, outside the footprint. It shares one recess with the damper.
5. **The three-place damper** is the crucible furnace's convention; a period sand-bath fire set its draught with the
   ash-pit door. Kept, as the issue's conventions ask; easy to drop.
6. **The hood** is not lined (it carries fumes, not heat) and not acid-proofed (period hoods were sometimes
   lead-lined). Its opening is 12 high so the vessel is reached over the tub; the breast is 4.
7. **The angles at a row's inner corners**: every vessel's four corners and top edges are bound, so in a row the
   angles of neighbours (the corner angles' side legs and the sides' top angles) stand back to back at their shared
   walls.
12. **Top angles on the sides.** To make the binding one connected frame, as asked, the side faces' top edges now carry
    an angle too (flush, so rows still tile). Without them the front and the back would be two separate U-frames. No
    bottom angles: the corner angles stand on the ground, and the ground is the frame's foot.
8. **One vessel a unit** (the Mint's baths held twelve): more vessels are more units in a row.
9. **The minimum chimney** is the crucible furnace's six blocks.
10. **The gold is drawn as gold** (the period's parted gold is a brown spongy mass), the sludge as silver and the fuel
    as charcoal.
11. **The static and moving split** when it is drawn in the game: the fire door is the only moving part.

## Wired into the game

#931 wired the model in on the shared ore machine code (the mod README's "Ore machines: the shared game half"). What
gameplay reads, and where:

- **The iron frame** is the controller block, `seraphhorizons:acidparting-{side}` (the item is `-north`), class
  `seraphhorizons.OreMachine`, drawing `acidparting_frame.json`; its grid recipe is the rig's `build.frame`
  (`recipes/grid/acidparting.json`). Placed, it stamps the shared `oremachine-cell` into the other three cells.
- **The stages** are `config/builds/acidparting.json`, the rig's `build.stages` with the same counts; the brickwork
  is a `oneType` material listing the rig's nine `bricks` types. The copper plates are the build's wearing part
  (`copper`, a `game:metalplate-copper`, 680 units of silver), drawn by its `requires`. A test holds the build to the
  rig and to the stage table above.
- **The brick** is the rig's `bricks`, composed into the block atlas as the crucible furnace's and the stall roaster's.
- **The contents** are drawn by the vessel's state (`BEAcidParting.ShowsPart`, the frame block's `extraRequires`):
  the damper's place with the sand bath; `fuel` laid and not lit, `fire` lit; `charge` with dore in and not started,
  `acid` with acid in and no charge parting; while parting `solution`, `gold`, the tub's `liquor` and `silver`; parted,
  `gold` and `wash` while the reservoir holds water; `silver` while the tub holds bits; `outletcap` capped.
- **Clicks.** An item goes where it belongs (dore and liquids into the vessel, fuel at the fire door, the copper
  plate into the tub, an iron plate on the outlet's top). An empty hand is placed by the rig's points: nearer `tub`
  than `vessel` in the hood's cell draws the silver, else at the vessel takes the gold; nearer `ash` than `firedoor`
  on the controller moves the damper.
- **The row** is the crucible furnace's (`FurnaceRow`, `ChimneyColumn`): along native x, the vessel's width (1)
  apart, up to `parting.rowMax`, the chimney from `outletCell` up, at least `parting.minChimney`.
- **Not drawn:** the fire door's slide (`depth`): it stays at its rest pose, open.
- **Tests**: `tests/AcidParting/AcidPartingTests.cs` (the rules, the rig's gameplay half, the build against the rig,
  the figures); `tests/PackTests/AcidPartingScenarios.cs` places, builds, charges, parts and empties it.
