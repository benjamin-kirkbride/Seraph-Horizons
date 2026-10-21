# Cupellation furnace: the model

Part of the Seraph Horizons mod's ore processing (epic #684): the generated model of the **cupellation furnace**
(model #741, behaviour #740), one machine of the settled lineup, placed as a multiblock like the pack's other
machines. The line, the glossary and the period sources are in #939; the power and water budget in #940.

**Status: in play.** This folder holds the model's generator and its checks (`tools/`), and the gameplay (#740:
`Core/`, `Game/`; the mod's README, "Ore processing: the cupellation furnace"). It follows the crucible furnace's conventions (`../CrucibleFurnace/README.md`): any fired
brick, tier-3 refractory lining 3 thick on every hot face, an ash mouth with a three-place damper, an outlet with the
chimney's anchor, iron binding, and a staged build. The generic half of the generator is `Machines/tools/machinegen/`,
unchanged by this machine.

Paths here are from this folder unless they start with `assets/`, which is the mod's (`mods-src/seraphhorizons/`),
or `Machines/`, this folder's neighbour.

## The furnace

An **English cupellation (test) furnace** of the nineteenth century [Percy 1870, *The Metallurgy of Lead*, the
English refining furnace; Hofman 1904, *The Metallurgy of Lead*, ch. XI]: air blown across a pool of molten lead lying
in a dish of bone ash (the **test**) burns the lead to **litharge**, which runs off over the test's front (the
**breast**), and the silver stays. The model is one such furnace in brickwork **three blocks wide (x), two high and
two deep**, with its **stack in a column behind the west end** (the back row's west cell, two high), bound with angle
iron:

- **The test and the fireplace side by side.** West, the **chamber**: 22.5 wide and 16 deep, its floor (the hearth)
  at 10.5, its walls rising to the springing at 19, under a **stepped vault** (three courses stepping in, to its crown
  at 23.5) that runs on over the bridge and the fireplace. On the hearth stands the **test**: an oval of bone ash 18 by
  12.8 inside an iron ring 0.9 thick and 3.7 high (seven sides of an octagon inscribed in the oval), its dish's floor at
  13, a rim of bone ash round it to 14, and at the front, where the ring is left out, the **breast**, bone ash 2.5
  thick, with the **gate** cut in it, 1.8 wide, from the dish's floor up: the litharge's way out. The ring's rim and
  the breast's top are level with the working door's sill (14.2). The test is the furnace's **wearing part** (below,
  "The test, a wearing part"): the ring is a fitting and stays; the bone ash in it is renewed. East, behind a **fire
  bridge** (3 thick, to 17, 2.8 over the test's rim), the **fireplace**: coal on a grate of four firebars (front to
  back, on two iron bearers built into the bridge and the east wall) over an ash pit (the cave), which the air enters at
  the **ash mouth** in the front face under an iron **damper** (open, half, shut, as the crucible furnace's).
- **The flame** crosses the bridge and the test and leaves at the chamber's west end by the **flue opening** in the
  back wall (4.5 by 4.5), along the **back flue** into the stack column and up its **stack** (8 by 8) to the
  **outlet** on the top. **The model stops at the outlet**: the player stands the chimney on it, or every furnace but one
  in a row sharing a chimney is **capped** with a flat iron plate flush in the curb's seat.
- **The blast comes by pipe** (the owner's ruling on #740): Steelmaking Expanded's engine air blower, on a Pipes and
  Power Expanded steam engine, puts air into the pipe network, and a pipe laid to the **socket** in the middle of the
  back wall's face (the blast cell's face: one fixed connector, drawing "Air" at 10 L/s and working at 1.5 atm or more,
  as smex's Bessemer converter's intake port) feeds an iron **wind box** set in the wall, from which the **tuyere** (an iron
  pipe 2.2 across) runs through the back wall on the test's middle line and blows across the lead towards the breast,
  2.9 over the rim, its mouth just behind the ring's back side. See "The blast", below.
- **The litharge's way.** From the gate an iron **gutter** runs out through the working door's sill, in a groove,
  falling 0.4 over its 7.6, and off its end into an iron **pot** standing in a **bay** of the front face under the
  door; the bay is 12 wide and 3 deep, iron-jambed, with a niche at its back for the pot.
- **The working door** is at the back of the bay, over the breast, its sill level with the test's rim: the lead is
  charged and the silver cake taken out through it, and a spent test broken out and a new one beaten in. The **fire
  door** (an iron door, shut, in a cast frame flush in the face) is over the grate, above the damper: **coal by hand**
  (the ruling: 0.7 coal an hour), with no automated fuel anchor.
- **The binding is angle iron** (the owner's rule for furnace brickwork): an **angle up every outside vertical
  corner** (five: the front two, the back's east corner, the stack column's two back corners), one leg on each face,
  its inside corner bedded against the brick's corner, so it locks the brick in on both faces, from the ground to just
  under the top; and an **angle band round the top**, wrapping every top edge, its face legs sitting on the uprights'
  heads. The binding is **one frame**: the bands along x (front, back, the stack column's back) run through the
  corners and the bands along z butt against them, top leg to top leg and face leg to face leg; at the L's **inside
  corner** the back band's top leg reaches 2 on over the top so the stack column's east band runs into its side, the
  two top legs one piece of frame. No two members overlap. Legs 1 thick, the uprights' 2.5 wide and the band's 2
  (clear of the outlet's curb), all set flush in rebates in the brick, so nothing leaves the footprint.

**Footprint and cells.** **14 cells**: the front two rows' twelve (3 × 2 × 2) and the stack column behind their west
end (`[-1,0,2]`, `[-1,1,2]`). The back row's middle and east columns are not the furnace's: the blast's pipe comes in
there. None is hollow; every top cell has a `lid` (a deck) at the top. The cell over the outlet, `[-1,2,2]`, is the
chimney's. **The controller is the front middle cell at ground level**, `[0,0,0]`, the litharge bay's. Placed like
the crucible furnace, it extends away from the player along their line of sight: the working face (the bay, the fire
door, the damper) faces them, the stack and the blast inlet face away.

**Why this footprint.** As small as the test, the fireplace, the walls and the flue allow:

- **Three wide**: 8 of brick west, the chamber's 22.5 (the test 18.4 across its ring, and room round it), the bridge's
  3, the fireplace's 6.5 (a grate about 1.3 ft by 3 ft), 8 of brick east: 48.
- **Two deep, the stack a block behind**: the front wall's 8 (the bay and the working door through it), the chamber's
  16 (the test 13.8 deep across its ring and breast), the back wall's 8 (the tuyere, the wind box and the flue opening
  through it). The stack (8 square in 3 of lining) needs a cell of its own; the spec puts the flue and the outlet in the
  back row, so it stands behind the chamber's west end, where the flue leaves. With the blowing engine gone (#740's
  ruling) the back row's other two cells hold nothing: the footprint shrank from 18 cells to 14.
- **Two high**: the ash pit, the grate and the test's hearth near the ground, the chamber and its vault to 23.5, and
  the lining and the brick over it to the top.

**Rows.** Furnaces stand side by side along x sharing their side walls: the west face (the front two rows and the
stack column) and the east face (the front two rows) are plain brick but for the angles' legs and the top band at
their edges, flush; in a row the neighbours' legs meet at the shared plane. Behind a row the stack columns stand at
each furnace's west end, and between them, behind each furnace's middle and east cells, the blast pipe comes in to the
socket: a row's blast main can run behind the stacks and branch forward to each furnace. The flues between them are
implied, as the crucible furnace's.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `blastCell`, `blastFace` | `[0,1,1]`, `south` | The blast inlet: the back wall's face of the middle top cell, the socket at its middle; the pipe is laid in the cell beyond, `[0,1,2]`, which is not the furnace's |
| `outletCell`, `outletFace` | `[-1,1,2]`, `up` | The stack's top cell, the stack column's; the chimney (or the cap) on its top, the chimney from `[-1,2,2]` up |
| `chimney` | origin `[-0.5,5,2.5]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `ash`, `ashSide` | `[1.2969,0.1875,0]`, `north` | The middle of the ash mouth in the front face: the damper, worked from in front |
| `fireDoor`, `fireDoorSide` | `[1.2969,0.875,0]`, `north` | The fire door's middle: coal by hand |
| `door`, `doorSide` | `[0.2031,1.0375,0]`, `north` | The working door's middle at the bay's face: the lead charged, the cake taken out |
| `dish` | `[0.2031,0.8125,1]` | The middle of the test's dish: where the lead and the cake lie |
| `litharge`, `lithargeSide` | `[0.2031,0.4375,0.1688]`, `north` | The litharge pot's mouth: the litharge taken out by hand |
| `tuyere` | `[0.2031,1.0688,1.4375]` | The tuyere's mouth in the chamber: the blast's sound and particles |
| `build` | | The build's stages and their materials (below); not drawn |
| `bricks` | | The fired brick types and their textures (the crucible furnace's); not drawn |
| `furnace` | | `minChimney` 6, `rowMax` 5, `rowAxis` `x`, `blastGas` `Air`, `blastRate` 10 (L/s), `blastPressure` 1.5 (atm, the least it works at); not drawn |

**Batch, no chutes** (#740, rule 2): the lead in, the silver and the litharge out, all by hand; no chute anchor, and
no fuel anchor: coal goes in by hand at the fire door.
**No mechanical power**: no `powerCell` (the validator fails if one appears). **Services**: blast (by pipe), chimney,
fuel by hand. The anchors are this model's; #711 (shared machine code) has not landed, so they are provisional where
it may settle them otherwise.

## The blast

**How Steelmaking Expanded delivers its blast** (smex 0.10.1, read from its assets and its DLL, disassembled with
`ikdasm`):

- **The engine air blower** (`smex:engineairblower`, classes `BlockEngineAirBlower` and
  `BlockEntityEngineAirBlower`) is a sub-machine of ppex's steam engine (`BlockEngineSubmachine`). Driven by the engine,
  each tick it **puts gas `"Air"` into the pipe network** connected at its output face
  (`PipeNetwork.TryProduceGas`, at `AirBlowerOutputPerSecond`, 300 L/s in the pack's defaults, scaled by the engine's
  inlet pressure and `SteamEngineEfficiency`). The block is an exlib network connector (`INetworkConnector`,
  `NetworkType` `"pipe"`), with **one connector face**, its `LeftFace` (west turned by its `side` variant):
  `HasConnectorAt(face)` is true there alone. It is not a face adjacency to the furnace: the air goes into the pipes.
- **The pipes are ppex's** (exlib's `PipeNetwork`): the air travels in any pipe of the network as gas.
- **Smex's own blast furnace takes its air at its tuyeres**: `smex:blastfurnace-tuyere-*` (`BlockTuyere`, a ppex
  `BlockPipe` subclass, a refractory brick block with one pipe connection by its `orientation` variant, s/n/w/e) stands
  in the furnace's wall at positions fixed in its multiblock structure (`ScanForOutlets`), and the furnace draws
  `TuyereIntakeVolume` (20 L) of air from the network there (`MachinePorts.NetworkAt<PipeNetwork>`, `IPipeNode.TryConsume`,
  `GasLine.Pressure`). So a smex blast furnace is fed by pipes laid from the blower to its tuyere blocks.
- **The ruling for this furnace** (#740): a fixed connector on the back face drawing `Air` from the network at
  **10 L/s**, working only at **1.5 atm** or more, as smex's Bessemer converter's intake port (the rig's
  `furnace.blastGas`, `blastRate`, `blastPressure`). The furnace's place on the line is a small user of a blower's
  300 L/s: one blower serves a row.

**This furnace's inlet** matches that: a pipe connection at a face of the furnace, the **blast cell's south face**
(`blastCell` `[0,1,1]`, `blastFace` `south`: the back wall's face of the middle top cell). The model has its own
geometry for the inlet only: an iron **wind box** set in the back wall, its face plate set back 2.5 in a **pocket** in
the wall's face (9 square, its four sides lined with iron flush with the face, so neither brick nor lining shows in
it), and the **socket**, a short **pipe stub** flanged to the plate and standing in the pocket: ppex's pipe section
(a square tube 6 across, its block's 5..11, 1-voxel walls round a 4 × 4 bore), in copper, its mouth at the blast face
with half a soldered joint round it, exactly as UnifiedPipes draws a copper or lead pipe's end
(`Pipes/tools/solderedpipe.py`: a bead of the game's lead solder 0.75 long and 0.2 proud). So the pipe the player lays
in the cell behind meets the stub as one ppex pipe meets the next, and nothing stands proud of the wall. The flange
is copper too, with four iron bolt heads; the stub is a fitting (the frame's plate is iron only). The box's front,
inside the wall (in front of the lining behind the chamber), takes the tuyere's back end. No element of smex's or ppex's models is used. Gameplay (#740) makes that face a pipe connector of exlib's `"pipe"`
network and takes `Air` from the network there, as smex's blast furnace does at its tuyeres (the rig's
`furnace.blastGas`).

### Nothing moves

The furnace has no moving part: the rig's parts have no drivers and read no input. What flows:

| Flow | From | To |
|---|---|---|
| The blast | Steelmaking Expanded's engine air blower, by pipe to the socket | the wind box, the tuyere, across the lead towards the breast |
| The draught | the chimney on the outlet, natural draught | in at the ash mouth under the damper, up through the grate and the fire, over the bridge and the test, out at the flue opening, along the back flue, up the stack |
| The litharge (`stream`, `litharge`) | the blast, driving it to the front | out through the gate, along the gutter, into the pot |

Everything that shows or not is a `requires`, gameplay's state: the stages, the coal and the fire, the lead and the
cake, the running litharge and the litharge in the pot, the damper's place, the cap.

**Particles.** Lit, the fired machines' flames at `fireDoor` and the row's chimney smoke. While the blast works the
charge (the litharge running, `stream`), white litharge fume rises off the `dish` and the litharge drips glowing into
the pot at `litharge`.

## The build (a proposal)

The crucible furnace's pattern: an iron frame by grid recipe (placing it puts the controller and its ghosts down), then
stages, each a `requires` value, in order; the last, the test, is the wearing part (the owner's ruling, below). The
brickwork's and the lining's counts are measured from the model; the test's bone meal too, at the bone-ash cupel's
rate. The frame's recipe and the fittings' materials are proposals to settle with the owner, as the crucible furnace's
were.

| | Stage | `requires` | What it puts in | Materials | Gear |
|---|---|---|---|---|---|
| Frame | **Iron frame**: grid recipe, hammer | none | The binding (5 angle uprights, the angle band round the top), the grate's bearers and firebars, the ash mouth's lintel, the damper's guides, the fire door's frame, the bay's and the working door's jambs and lintels and the niche's lintel, the tuyere with its wind box (its face plate set back in an iron-lined pocket); 6 short posts hidden in the brickwork | 3 iron plates, 4 iron rods, 12 iron nails and strips | 49.46 |
| 1 | **Brickwork** | `brickwork` | The base, the walls, the stack column: 8.26 blocks | **17 fired bricks**, any one type | 5.78 (brown 2.98) |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on every hot face (the chamber's walls, floor and vault, the bridge, the fireplace from its bearers up, the flue opening, the back flue and the stack) and the outlet's curb: 2.96 blocks | **12 tier-3 refractory bricks, 5 fire clay** | 10.11 |
| 3 | **Fittings** | `fittings` (and the damper's place) | The fire door, the gutter, the litharge pot, the test's iron ring (standing empty on the hearth), the blast stub (copper, flanged and soldered); the damper | 3 iron plates, 1 copper plate | 27.76 |
| 4 | **The test** (wearing part) | `test` | The bone ash beaten into the ring: 487 cubic voxels | 48 bone meal (at the cupel's 4 for 41 cubic voxels); renewed every 15 charges for the same | 2.50 |
| | *Outlet cap* | `outletcap` | Not a stage: by hand, any time after the lining | 1 iron plate | 7.71 |

A furnace costs **95.60** in gear (92.80 in brown brick), the blower, the engine and the pipes aside. Values from
`assets/seraphhorizons/config/item-values.json`. The damper is drawn once the fittings are in; the test's contents,
the fire and the litharge once the test is; the cap once the lining (its curb) is.

### The test, a wearing part

The owner's ruling (#740): the test is not a build stage but a **wearing part**, as the cylinder borer's cutter head
is: fitted last, after the fittings, and renewed when spent. The route's figure is **a new test every 15 charges**,
about **3,000 units of lead** a test (200 a charge); each costs the same **48 bone meal**. In the period the test soaked
up litharge as it worked and was renewed every few days.

- **The ring is a fitting, the test its bone ash.** A whole test (18.4 across its ring) is wider than the working door
  (12), so it could not be lifted out through it; instead the iron ring is fitted with the fittings and stays on the
  hearth, and the spent bone ash is broken out of it and new bone ash beaten in, through the working door. So the
  ring's iron plate is in the fittings (3 iron plates), and a test is bone meal alone. With the test out (the
  fittings stage, or a spent test broken out) the ring stands empty on the hearth.
- **The rig**: the test is the last stage, `requires` `test`, with `wearing`: `charges` 15, `lead` 3000 and `renew`
  (what a new test takes, its own materials). Gameplay counts the charges, takes the spent test out (the furnace stops
  until a new one is in) and draws the bone ash again when one is beaten in.
- **Nothing waits on the test but its contents**: the damper and the fire door work without it; the lead, the cake and
  the running litharge need it, so the viewer's work states wait for the test.

## Model

Everything in the model was made for this mod: no other mod's model is used. **No new texture**: every texture is
the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/cupellationfurnace.json` | The whole furnace, every part (551 elements) |
| `assets/seraphhorizons/shapes/block/cupellationfurnace_frame.json` | The iron frame only (91 elements): what the placed frame draws |
| `assets/seraphhorizons/config/cupellationfurnace-rig.json` | Cells, anchors, the build and the brick types, and the part rig (15 parts, no drivers) |

| Code | Texture | On |
|---|---|---|
| `brick` | The furnace's brick type's block texture (the renderer's, from `bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork: world-mapped |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining, the curb: world-mapped |
| `iron` | `game:block/metal/plate/iron` | The frame (the angle binding, the grate, the lintels, jambs and guides, the fire door's frame, the tuyere and the wind box), the fittings (the test's ring among them), the damper, the cap |
| `boneash` | `game:item/resource/crushed/lime` (the fired cupel's) | The test's bone ash |
| `lead` | `game:block/metal/ingot/lead` | The molten lead; glow 200 |
| `silver` | `game:block/metal/ingot/silver` | The silver cake; glow 255 |
| `litharge` | `game:item/resource/crushed/limonite` (the litharge item's base) | The running litharge (glow 160) and the litharge in the pot |
| `coal` | `game:block/coal/bituminous` | The coal on the grate |
| `ember` | `game:block/coal/ember` | The same coal burning, and the cinders; glow 200 |
| `copper` | `game:block/metal/sheet-plain/copper4` (UnifiedPipes' copper pipe's) | The blast stub and its flange |
| `solder` | `game:block/metal/ingot/leadsolder` (UnifiedPipes' joints') | The half joint round the stub's mouth |

### Regenerating

```sh
python3 mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py             # the assets
python3 mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py --out DIR   # all three into DIR
python3 mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py --quick     # skips the z-fighting fix
```

A full run takes about a minute and is deterministic (two runs into two folders are byte-identical). It exits non-zero
if a check fails (`tools/validate_cupellationfurnace.py`):

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **The stages:** at each stage (the iron frame alone, with the brickwork, the lining, the fittings, the test) nothing
  floats; the iron frame is iron only, with no thin post taller than half the furnace; its ties are hidden in the
  brickwork.
- **Textures by role**, as the table above puts them.
- **Containment:** nothing leaves the furnace's 14 cells; nothing over the top, nothing in the back row's middle and
  east columns.
- **A row:** the side faces plain brickwork and binding (10240 points), nothing else at either side plane, the binding
  flush; two furnaces side by side touch only at their walls.
- **The binding is angle iron** *(review)*: an upright angle at each of the 5 outside vertical corners, its legs 1
  thick and 2 to 3 wide, flush with both faces of its corner and meeting at it, from the ground to the band, and an
  angle band wrapping each of the 6 top edges; every leg's inside face bedded on the brick of its face (or on the band
  over an upright's head), 1305 points; no flat strap or kerb left.
- **The binding is one frame** *(review)*: its 11 members (5 uprights, 6 bands) butt and never run into each other;
  every band's two ends meet another band or an upright face to face (a touch along an edge is not a meeting); every
  upright stands on the ground and carries the band on its head; each band's two legs meet; and the members, joined
  where they meet, are one connected frame (16 joints).
- **Thick walls:** at least `MIN_WALL` (8) of brick, or iron set in it, round the chamber and the fireplace, west,
  east, behind (the wind box aside), and in front clear of the openings.
- **The lining:** at least `LINING_T` (3) of the lining's brick behind every hot face (3441 points), the cave and the
  ash mouth the brickwork's.
- **The stages' counts:** the brick boxes' volumes add up to the lattice's; the counts at the game's rates; the test's
  bone ash measured from its elements (on a quarter-voxel grid) within 3 % of its count.
- **The test, the wearing part** *(review)*: the last stage, after the fittings, its `wearing` 15 charges and about
  3,000 lead, renewed for its own materials, bone meal alone; the test the bone ash alone, its ring a fitting of
  seven sides standing on the hearth; nothing of another part in the test's place; the test's contents never shown
  without it.
- **The test:** on the hearth, clear of the walls, under the working door, whose sill is level with its rim; the ring on
  seven sides; the gate as wide as the gutter; the lead and the cake in the dish; the tuyere on the test's middle line,
  its mouth just behind the ring, between the rim and the springing.
- **The litharge's way:** the gutter falls from the gate's floor at the breast's face, bedded in its groove; the stream
  falls from its end inside the pot's walls onto the litharge, its fall clear of the furnace.
- **The blast's way** *(review)*: a stub of ppex's pipe (6 across, a 4 × 4 bore) at the middle of the blast face, its
  mouth at the face with half a soldered joint (as UnifiedPipes' pipe ends), nothing proud of the face; flanged to the
  wind box's face plate, bedded on it, the plate set back at the pocket's back under all of it; the pocket's four sides
  iron from the plate to the face, no brick or lining in the pocket and no lining behind the box; the bore clear from
  the mouth into the box; the blast face the back wall's, looking onto a cell that is not the furnace's; the wind box
  closed; the tuyere from its front through the wall into the chamber.
- **Clearances:** in 14 scenes (every state of the test and the fire, every place of the damper with the cap on, each
  stage before the furnace is complete) no two parts overlap, but the gutter bedded in its groove.
- **The damper** in its recess between its guides in all three places; shut over the mouth, half over its upper half,
  open clear of it.
- **The outlet:** open to the sky through the stack; the cap flush in the curb's seat over the stack's mouth; the curb
  the lining's brick.
- **The draught:** from outside the ash mouth through the cave, up every gap of the grate, over the bridge and the test,
  out at the flue opening, along the back flue and up the stack: 152 points clear of the furnace built; the stack's mouth
  clear but for the cap; every firebar on both bearers, the bearers built into the walls.
- **No z-fighting** in 9 scenes (the stages among them), after the fix (141 faces pressed against their own part
  removed).
- **Files:** every texture the game's (with its files at hand) and declared; glow on the burning coal, the lead, the
  cake and the running litharge only; the frame's shape the iron frame alone; 14 cells with boxes, decks on the 7 top
  cells; no power cell; the blast cell a cell of the furnace facing out of it; the outlet under the chimney's cell, the
  chimney six blocks from it up; the ash mouth, the fire door and the working door on the front face; the shipped files
  are the checked model.

`tools/tests/test_cupellationfurnace_model.py` holds the written files to these without the generator (the
vocabulary, nothing moving, the 14 cells rebuilt from the shape, the blast inlet and the socket, the angle binding and
that it is one frame, the
stages' counts, the frame, the brick types, the anchors, the side faces, the textures and glow, and
`site/models.json`'s states, the test the wearing part); `site/test/cupellationfurnace.test.ts` holds the rig in the
viewer's maths (no input, the blast cell and its figures, the test the last stage and wearing, every part still, the
binding one frame of butted members).

### Rig schema (`cupellationfurnace-rig.json`)

The bucking sawmill's format with nothing added to the shared rig maths:

- **Inputs:** none. **Drivers:** none.
- **`requires`** values: the stages, `brickwork`, `lining`, `fittings` and `test` (the wearing part); `coal`, `fire`, `lead`, `silver`,
  `stream`, `litharge`; `damperopen`, `damperhalf`, `dampershut` (one at a time); `outletcap`. The iron frame has none.
- **Keys:** `cells`, `blastCell`/`blastFace`, `outletCell`/`outletFace`, `ash`/`ashSide`, `fireDoor`/`fireDoorSide`,
  `door`/`doorSide`, `dish`, `litharge`/`lithargeSide`, `tuyere`, `chimney`, `build`, `bricks`, `furnace`, `parts`.
- **`build`**: as the crucible furnace's: `frame` (`recipe`, `tool`, `ingredients`), `stages` in order (`requires`,
  `materials`, `volumeBlocks` where measured, `perClick`, the brickwork's `oneType`, the fittings' `also`: the damper's
  places, the test's `wearing`: `charges`, `lead`, `renew`), `outletCap`.

**In the viewer** (`site/models.json`, `cupellation-furnace`): no slider, nothing turns. Four selects of states:
**Build** (the iron frame, 1: brickwork, 2: lining, 3: fittings, the ring empty, 4: the test, complete), **The test
and the fire** (cold, coal on the grate, lit, lead melted with the blast on, cupelling with the litharge running off,
brightened, the cake and the litharge taken out; it waits for the test), **Ash-pit damper** (waits for the fittings)
and **Outlet**
(waits for the lining). A **Brick** select offers the nine fired brick types, as the crucible furnace's. The `blast`
anchor overlay shades the blast face.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork
(`bk_brick_NNN`), `ln_*` the lining (`ln_brick_NNN`), `ts_*` the test's bone ash (`ts_ash_*`, `ts_rim<k>`,
`ts_breast_foot`, `_w`, `_e`), `ft_*` the fittings (`ft_firedoor*`, `ft_gutter_*`, `ft_pot_*`, `ft_ring<k>`), `coal_*`, `fire_*`,
`lead_*`, `silver_*`, `stream_*`, `litharge_*`, `damperopen_*`, `damperhalf_*`, `dampershut_*`, `outletcap_*`; and
`fr_*` the iron frame (`fr_angle_<corner>_a|b<n>`, `fr_band_<edge>_top|face<n>`, `fr_bearer<n>`, `fr_bar<n>`,
`fr_lintel`, `fr_guide_*`, `fr_firedoor_*`, `fr_jamb_*`, `fr_baylintel_*`, `fr_nichelintel`, `fr_tuyere_*`,
`fr_windbox_*`, `fr_tie_*`). Hand edits are lost when the script runs again.

## Salvaged, and new

From `741-parting-furnace-model-v2`: the test (`build_cupel`'s ring of chord segments round an oval, the bone ash,
the breast and the litharge channel and pot), rebuilt at full size, with the helpers `disc`, `obox`, `segment_y` and
`channel`. The first pass also took the branch's blowing engine (shaft, Scotch yoke, cylinder); the owner's ruling on
#740 (the blast from smex's air blower) removed it in the second. The branch's furnace (2 to 2.5 times too small), its
shared stack, floor and per-cell `fitted` collision are not used. The body (the brick lattice, the lining's zones, the
binding, the ties, the damper, the outlet and its cap, the stages, the rig's `build`, `bricks` and the cells) is new on
`../CrucibleFurnace/tools/make_shape.py`'s skeleton, its helpers copied, not shared.

## Review passes

1. **First pass**: the furnace with its own blowing engine on the back row's top (18 cells, an axle power cell).
2. **The blast by pipe** (#740 ruling): the blowing engine and the power cell removed; the blast inlet (`blastCell`,
   `blastFace`) at the middle of the back wall's face, a socket in a wind box feeding the tuyere, matching how smex's
   engine air blower delivers air (into the pipe network) and smex's blast furnace takes it (at pipe-connected tuyere
   blocks); the footprint shrunk to 14 cells, the back row's middle and east columns left free for the pipe.
3. **Angle iron** (the owner's rule for furnace brickwork): the flat straps and kerbs became angle uprights wrapping
   the outside corners and an angle band round the top edges, checked bedded on both faces.
4. **One frame** (owner's note: the back band's top leg did not meet the stack column's east band at the L's inside
   corner, the two touching only along an edge, and at the outside corners the bands and the uprights' heads ran into
   each other): the bands along x run through the corners, those along z butt against them, the back band reaches over
   the inside corner to meet the stack column's band, and the uprights stop under the band and carry it. A new check
   (and model test) holds the binding to one connected frame of butted members, every band end meeting another member.
5. **The test a wearing part** (owner's rulings on #740, 2026-10-14): the test moved from the third stage to the last,
   after the fittings, renewed every 15 charges (about 3,000 lead) for 48 bone meal (`wearing`, as the cylinder borer's
   cutter head); its ring moved into the fittings (a whole test does not pass the working door, so the ring stays and
   the bone ash is renewed in it; the ring's two sides by the breast no longer run on into it). The test still stands
   on the hearth, its rim level with the working door's sill. The blast's figures (10 L/s of `Air`, 1.5 atm) in the
   rig's `furnace`; fuel by hand (0.7 coal an hour), no fuel anchor. No geometry moved but the ring's two ends; no
   anchor changed. A new check (and model and site tests) holds the test to the wearing part.
6. **A recessed stub** (owner's note): the wind box's face plate set back 2.5 in an iron-lined pocket in the back
   wall's face, and a copper stub of ppex's pipe flanged to it, its mouth at the blast face with half a soldered joint
   as UnifiedPipes' copper and lead pipe ends, so a ppex pipe in the cell behind meets it; nothing proud of the face.
   The wind box is shallower (z 27 to 29.5, in front of the lining). The anchor is unchanged; the check of the blast's
   way now holds the stub, its joint, its flange, the pocket's iron and the bore.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has looked at it in a client.

1. **The test stands on the hearth** (the owner's ruling), 3.7 high, rather than set into it with its rim flush, as the
   English test was (held up from below on iron bars in an opening of the hearth, and so changed from below). Its rim
   is level with the working door's sill.
2. **The vault is stepped** (three courses), as the brick lattice is made of square boxes.
3. **Whole-cell boxes**; the decks make the top one floor.
4. **The glow is fixed**: 200 on the fire and the lead, 255 on the cake, 160 on the running litharge.
5. **The lead pool is drawn full** whatever is on the test; a stretch on the work could lower it (gameplay's call).
6. **The stub is a shape**: that a ppex pipe connects to the blast face is gameplay's (#740), not the model's.

## Open for the owner

Settled on #740 (2026-10-14): the blast (a fixed connector on the back face, `Air` at 10 L/s, 1.5 atm), fuel by hand
(0.7 coal an hour), the test a wearing part renewed every 15 charges for 48 bone meal, and the test standing on the
hearth with its rim level with the working door's sill. Still open:

1. **The ring a fitting, the test relined in place** (this pass's reading of the ruling): a whole test, ring and all,
   is wider than the working door, so the model keeps the ring on the hearth and renews only the bone ash. If the test
   is to be a whole item swapped as the cutter head is, the working door (and the bay) must widen to about 20, or the
   test go in from below, as the English test did.
2. **The stub's metal**: copper, soldered, as the owner asked it to match UnifiedPipes' copper and lead joints. A
   player may lay any ppex pipe to it (iron and steel are banded, not soldered); gameplay may tint it to the pipe met.
3. **Power moves to the steam engine**: #740's and #940's figures for this furnace (resistance 0.2, 0.04 empty, on the
   works shaft) no longer apply: the furnace draws no mechanical power; the blast's cost is the steam engine's steam.
4. **The footprint is 14 cells, an L** (the stack column behind the west end), shrunk from 18 with the engine gone.
5. **The build stages**: the frame's recipe (now with the angle binding and the wind box) and the fittings' materials
   are proposals; the brickwork's, the lining's and the bone ash are measured.
6. **The items** (#740's open item): what goes in (silver-lead, or the roasted concentrate the hand cupel takes) and
   comes out (a silver cake, litharge, the item the pot gives) are gameplay's; the model draws a lead pool, a cake and
   litharge.
7. **Shared anchors pending #711**: the blast inlet and the outlet follow the crucible furnace and smex's interface;
   #711 may settle the batch machines' hand anchors (`door`, `fireDoor`, `litharge`, `dish`) differently.
