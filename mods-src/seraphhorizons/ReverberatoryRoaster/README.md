# Reverberatory roaster: the model

Part of the Seraph Horizons mod (`../README.md`): the ore line's roaster of concentrate (epic #684, behaviour #736),
the generated model (#737). It roasts sulfide concentrate into roasted concentrate: level 1 batch, rabbled by hand
(96 %); level 2 continuous, with water-cooled mechanical rabbles (100 %). **There is no gameplay yet**: no block, block
entity or renderer reads these files; the rig is written as the contract the gameplay will be built against.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, `Pipes/` and `CrucibleFurnace/`, this folder's neighbours. The generic
half of the generator is `Machines/tools/machinegen/`, unchanged by this machine: no new driver (every motion is a
`rotate` on θ or on ψ, the axle's travel) and no change to the package. The water's pipes are drawn by
`Pipes/tools/solderedpipe.py`, as the rosser's are.

## The furnace

A **long-bedded reverberatory roaster** [Peters 1895, ch. VII–VIII; Hofman 1904, figs. 96–143]: a level hearth under a
low arch, the fireplace at the north end behind a fire bridge, the flame drawn along the hearth to the flue at the
south end. It is **one real furnace with two levels**, a level being only parts fitted to that same furnace (#711): the
footprint, the brick and the arch are the same at both, and level 2 adds its parts, taking the place of level 1's in
three places only (the issue's "in place of"). It follows the **crucible furnace's conventions** (`CrucibleFurnace/README.md`):
the model stops at an **outlet** on the top; the **brickwork takes any fired brick** and the **hot faces are lined
with tier-3 refractory brick 3 voxels thick**, the arch being that refractory; natural draught through an **ash mouth
with a three-place damper**; iron binding on the faces that are not shared; **build stages**. The binding is **angle
iron** (the owner's rule for every furnace): see "The binding", below.

**Footprint: 3 wide (x) × 2 high × 5 long (z), 30 cells, and the drive's row on the roof's middle** (5 cells,
x 0, y 2, z 0..4, fixed from the frame on, as on the branch): 35 cells. In the native, south-facing frame with the
controller at `[0,0,0]`: x −1..1, y 0..1, z 0..4, plus x 0, y 2, z 0..4. The **controller** is the middle of the fire end
at ground level, the ash mouth's and the fire door's cell. Placed like the crucible furnace, it runs away from the
player along their line of sight: the fire end (north) faces them, the flue end (south) is behind.

**Why 3 × 2 × 5.**

- **Three wide**: a hearth 2 blocks across (32 voxels) between an 8-thick shared west wall and a 6-thick east wall with
  its ironwork outside (to 48).
- **Two high**: the hearth on its bed over the discharge tunnel (the output chute's 8 × 8 bore at y 4..12, the hearth's
  floor at 14), the arch springing at 22 and its crown's top at 31, just under the top (32); the fire end's grate and
  ash pit under the same springing.
- **Five long**: the fire end block (the fireplace and the coal well over it, 16), the arch over the bridge and a hearth
  two rabbles long (48), the flue end block (the hearth's last 10, the cavity over it, the uptake and the outlet, 16).
  The outlet is over the flue end, as the issue asks.

**Rows.** The work doors are on the **east** long side only and the fire door at the fire end, so two furnaces stand
**back to back, sharing their west walls**, the second turned half round (its fire end at the other end); the flue
between them is implied, one outlet carries the chimney and the other is capped. The west face is plain brick over its
whole area, nothing of the furnace at its plane (no buckstay, nut or door): the arch's west foot is built into the
shared wall, which rises to the top the whole length, and the tie rods end on anchor plates set in its top. The rig's
`furnace` has `rowMax` 2, `rowAxis` `x` and `rowFacing` `opposite`.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `powerCell`, `powerFace` | `[0,2,0]`, north | The vanilla axle comes in along z at the cell's centre (y 2.5) into the gear case. Level 2 |
| `infeedCell`, `infeedFace` | `[0,2,4]`, up | The hopper's mouth (the cell's top): charged by hand at level 1; at level 2 a section of the vanilla chute hangs in it |
| `outputCell`, `outputFace` | `[1,0,2]`, east | The discharge tunnel's mouth in the footing's east face: the discharge door (level 1), a section of the vanilla chute flush with the face (level 2) |
| `fuelCell`, `fuelFace` | `[-1,1,0]`, up | The coal bunker's chute (level 2): a section of the vanilla chute from the cell's top into the bunker's west end |
| `waterCell`, `waterFace` | `[0,2,2]`, up | The water (level 2): a ppex pipe from above onto the manifold, at the block's 5..11 as ppex lays a pipe. Provisional pending #711 |
| `outletCell`, `outletFace` | `[-1,1,4]`, up | The outlet, in the flue end's **west** corner: beside the drive's row, never under it or under the infeed (the branch's clash) |
| `chimney` | origin `[-0.3125,5,4.4375]`, y, 6 | The shortest chimney (the crucible furnace's six blocks, provisional), from the outlet up; its first cell `[-1,2,4]` is never the furnace's |
| `infeed`, `output`, `fuel`, `water` | points | The middles of those faces |
| `charge`, `discharge` | points | Where the feed chute drops the charge on the hearth; the discharge hole by the bridge |
| `ash` + `ashSide`, `stoke` + `stokeSide` | north | The ash mouth (the damper) and the fire door (level 1's stoking), on the controller's face |
| `workDoor1`, `workDoor2` + their sides | east | The work doors, at the rabbles |
| `build`, `bricks`, `furnace`, `gearing` | | The build (below), the brick types (the crucible furnace's), the row, the drawn gearing: not drawn |

## The levels

| | Fitted (`requires`) | What it is |
|---|---|---|
| Level 1: batch, by hand | `hopper`, `firedoor`, `handfiring`, `doorsopen`, `dischargeshut`; the damper (`damperopen`, `damperhalf` or `dampershut`) | The hopper on the roof, charged by hand, its neck and feed chute dropping the charge through the flue end's roof onto the hearth; the fire door over a **flat grate** (`handfiring`: 14 bars on the bearers, and the coal well's cover); the work doors on the east raised in their guides, so the charge is rabbled through them by hand; the discharge door shut over the tunnel's mouth, opened to draw the roasted charge. No power, no water |
| Level 2: continuous | `hopper`, `firedoor`, `doorsshut`, `dischargeopen`, `drive`, `rabbles`, `feeder`, `stoker`, `output`; the damper; the pipes (`pipecopper` or `pipelead`) | Adds the drive (gear case with its rectifier, line shaft, bevel pinions, pedestals), the water-cooled rabbles with their swivels, the feed roll and the infeed's chute section, the **stoker** (the coal bunker in the fire end's well, its throat and the **step grate**, in place of the flat grate and the cover), the output's chute section and spout, and the water's pipes. The work doors stay, **shut**; the discharge door is slid clear of the output chute. The hopper and the fire door stay |
| Contents | `charge` (both), `fireflat` (level 1), `firestep` (level 2) | The bed of concentrate on the hearth; the fire on the flat grate; the fire on the step grate with the coal in the throat and the bunker. Gameplay's state, not fittings |

The rig's `build.levels` lists each level's `fits`, its `also` (the damper's places; the pipes' metals) and level 2's
`replaces` (`handfiring` → `stoker`, `doorsopen` → `doorsshut`, `dischargeshut` → `dischargeopen`). This is not the
branch's `levels` retire scheme: nothing of level 1's is pulled down but those three, each a part level 2 has its own of.

## The build (provisional)

Settled with the owner later (#737, "Build stages"); the rig's `build` carries `"provisional": true`. Proposed, on the
crucible furnace's pattern:

| | Stage | `requires` | What it puts in | Materials (measured) |
|---|---|---|---|---|
| Frame | **Iron frame**, a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (`frame`) | The fire end's and the flue end's corner angles, knees and angle bands, the ash mouth's lintel and the damper's guides, standing on their own (the end blocks' east corner angles on a short post each, the lintel on two, hidden in the brickwork) | not settled |
| 1 | **Brickwork** | `brickwork` (and `frames`) | The base, the shared wall, the outer walls, the footing, the end blocks: 13.19 blocks; with it, at no cost of its own, the iron set in as it is laid (the fire door's lintel, the flat grate's bearers, the work doors' frames and guides, the discharge door's rails) | **27 fired bricks**, any one type |
| 2 | **Lining** | `lining` | Tier-3 refractory 3 thick on every hot face: 4.07 blocks | **17 tier-3 refractory bricks, 7 fire clay** |
| 3 | **Arch** | `arch` (and `ironwork`) | The arch's 9 strips and the east skewback, tier-3 refractory: 2.08 blocks; with it the angle buckstays, the skewback bar (the east side's length, end to end), the tie rods and their anchors | **9 tier-3 refractory bricks, 4 fire clay** (the ironwork not settled) |
| | Level 1, level 2 | as above | | not settled |
| | *Outlet cap* | `outletcap` | Not a stage: on the furnace of a pair that does not carry the chimney | 1 iron plate |

The counts are measured from the model as the crucible furnace's are: the brick is a lattice (every void's faces, the
lining's zones' and the blocks' boundaries) merged into world-mapped boxes, the brickwork's and the lining's apart, at 2
fired bricks or 4 refractory bricks a block, rounded up, fire clay one per 2.5 refractory bricks.

## The binding

Every piece of iron that binds the brickwork is **angle iron**, an L section 0.75 thick with legs 2 wide (`ANGLE_T`,
`ANGLE_L`), wrapping the brick's corner: one leg on each face, its inside corner bedded against the brick's corner, so it
holds the brick in on both faces (the owner's rule for every furnace). The end faces are the footprint's faces and the west
face is the shared wall's, so nothing may stand proud of them: the angles there are **set into the brick, flush** with
both faces of their corner.

- **Corner angles** (`fr_angle_<n|s><w|e|f>_face`, `_side`; the iron frame): at each vertical corner of the fire end's and
  the flue end's faces, full height at the shared wall's corners, from the footing up at the end blocks' east corners (each
  on a short post hidden in the footing, `fr_tie_angle_*`), and to the footing's top at its east corners.
- **Knees** (`fr_knee_<n|s>_face`, `_top`; the iron frame): where the footing steps out from under the end block's east
  corner, the footing's corner angle and the end block's are one stepped member. A knee plate in the end face, 2 × 2,
  set in flush, fills the step's inside corner under the end block's angle, so the end-face legs run through the step
  without a break (footing angle, knee, end block angle, ground to top); a short leg in the footing's top wraps the
  ledge's edge from the end block's face to the footing angle's side leg, closing the footing angle's top.
- **Angle bands** (`fr_band_<n|s>_face`, `_top`; the iron frame): along the top edge of each end face between its corner
  angles, wrapping that edge (a leg down the face, a leg on the top), their ends meeting the corner angles: the face
  leg butts the corner angles' end-face legs and the top leg runs on to their side legs, so each top corner is iron to
  the corner.
- **Buckstays** (`iw_stay<i>_flange`, `_web`; the ironwork, with the arch): the east wall between the end blocks has no
  vertical corner, so each buckstay is an angle standing on the face, its flange bedded against the wall and the skewback
  bar, its web standing out from the flange's south edge to the footprint's face; the tie rod passes through the flange
  and its nut bears on it, against the web.
- **Skewback bar** (`iw_skewbar`; the ironwork): behind the buckstays, where the arch's east skewback bears on it, and on
  through the end blocks' east faces, set in flush, to the east corner angles' side legs at both ends. It ties the fire
  end's and the flue end's binding together along the east side; the tie rods tie the buckstays across to the shared
  wall. The binding is one frame, not two end frames and loose ironwork.

A check holds it (`check_binding`): each corner angle's two legs are 0.75 × 2 in the two faces of its corner, flush, with
brick behind each leg and the face running on in brick past it, nothing proud of either face, the legs meeting at the
corner; each band's legs in the end face and the top, bedded the same way, its ends on the corner angles; each buckstay's
flange on the east face, bedded against the wall and the skewback bar, its web at the flange's edge, its nut on the flange.
A second check holds the joins (`check_frame_connected`): on each end face every member (corner angles' legs, knee,
band's legs) meets another over an area, not an edge, and they form one group; the corner chains run without a gap from
the ground to the top (the shared wall's angle; the footing's angle, the knee, the end block's angle); the knee meets
both angles, the bands' legs both corners' legs; and both end faces' binding with the ironwork (buckstays, skewback bar,
rods, anchors, nuts) is one group, the skewback bar meeting both east corner angles. Removing a knee, shortening the
band's top leg or the skewback bar each fails it.

## How it works

Positions in this section are in the generator's **build frame**: the native axes in voxels from the machine box's
north-west bottom corner; the controller is the build frame's cell (1, 0, 0), so the shipped files are the build frame
moved one block west and divided by 16.

**The structure.** The shared west wall, x 0..8, plain brick to the top (y 32) the whole length. The east wall, x 40..46,
to the springing (22) under the arch and to the top at the end blocks, on a **footing** x 40..48 to the hearth's level
(14). Between them (x 8..40): the **ash pit** (y 1..10) and the **firebox** (z 6..20) under the fire end block's roof
(22); the **bridge** (z 20..25, to y 20); the **hearth** (z 25..74, its floor at 14) under the **arch** (z 16..64,
segmental, rise 5, 4 thick, nine strips of refractory, springing at 22 from the east skewback and the shared wall,
crown 27..31); the flue end's **cavity** (z 64..74, to y 27) under the flue end block's flat roof; the **flue end wall**
(z 74..80). The **uptake** (x 8..14, z 68..74) rises from the cavity's south-west corner through the roof to the
**outlet**: a curb of the lining's brick, flush, round a seat for the cap. The **discharge hole** (x 30..36, z 37..43,
in the first rabble's sweep) drops through the hearth's bed into the **tunnel** (y 4..12, z 36..44) that runs east to the
footing's face.

**The fire end.** On the controller's face: the **ash mouth** (x 18..30, y 1..6.5) into the ash pit, an iron **damper**
sliding in a recess over it (open, half, shut: the crucible furnace's), and above it the **fire door** (x 19..29, y
14..20), shut in its recess, over the flat grate (level 1: 14 bars on two bearers built into the side walls, the fire
bed on them). In the fire end block's top, the **coal well** (x 3..41, z 3..13, y 25..31.5) over a **throat** through the
firebox's roof (x 9..39, z 7..12): covered flush by three iron plates at level 1; at level 2 the **bunker**'s iron liner,
an iron throat hanging to y 20 over the top tread, and the **step grate** (four treads from z 6 to the bridge, tops
18.5, 16.5, 14.5, 12.5, the air rising between them), the coal sliding down the steps as it burns. The fuel's chute
section drops into the bunker's west end from the fuel cell's top, hung from a strap on the liner. The gear case stands
on the well's rims, bridging it.

**The drive** (level 2), as the branch's: the oak entry (the vanilla axle's cross) from the power face into a closed
cast-iron **gear case** (x 18..30, y 33..46, z 2..12) whose **rectifier** (two pairs of gears on one-way catches, one
through an idler; not drawn) turns the iron **line shaft** (24, 40) one way whichever way the axle turns. The shaft runs
in the case's south boss, two **pedestals** on the arch's crown (z 24..27, 46.6..49.2) and the hopper neck's end walls.
Each rabble (spindles at z 39 and 60) is driven by a **bevel pair**, an 8-tooth pinion on the shaft on the north rim of a
16-tooth wheel on the spindle (module 0.7), their pitch cones' apex where the axes meet: the rabbles turn at −1/2 of the
axle.

**The rabbles** (level 2). Each **hollow spindle** runs from the hearth up through a **gland** in the crown strip and its
**swivel** on a bracket on the crown (the spindle's upper bearing, and the water's way in), the wheel's hub resting on
the swivel. In the hearth it carries a hub with two arms, three ploughs on each set 40° across it; the sweeps reach
12.44 from each spindle, so the second rabble's takes in the charge under the feed chute, the first's the discharge
hole, and the two overlap between (a quarter turn apart, the arms never meet: the swept paths).

**The feed.** The **hopper** (level 1, x 16.5..31.5, y 42..48, z 66..78) on its **neck** (x 20..28, y 33..42) on a flange
on the flue end block's top; the iron **feed chute** from the neck down through the roof into the cavity (y 26.5..35).
At level 1 a charge thrown in falls through to the hearth's far end. At level 2 a **fluted feed roll** keyed on the line
shaft's end turns in the neck under the hopper's slot and meters the charge down the chute, and a **section of the
vanilla chute** hangs in the hopper's mouth from two straps on its rim.

**The output** (level 2): a **section of the vanilla chute** along x in the tunnel's mouth (x 40..48), flush with the
face, and an iron **spout** from under the discharge hole down onto its floor, so the roasted charge falling through
slides into the chute. The discharge door (a plate sliding along z on rails in its recess in the footing's face) is
slid clear of it; at level 1 it is shut over the mouth, and the player opens it to draw the charge.

**The water** (level 2): the **manifold**, a cube of ppex's pipe (UnifiedPipes' soldered pipe, 6 across) under the water
cell's top, open up to the player's pipe and plugged below; from its side a **small-bore pipe** runs east, down the drive
row's east side clear of the wheels (x 31.4), and in under each wheel to its swivel's port, soldered at each bend. The
same pipes in copper (`pipecopper`) or lead (`pipelead`), one drawn. The water is used up: no return anchor (rule 4).

Every moving part (θ the axle angle, ψ its travel; none rides another):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry: the oak shaft into the gear case (`entry`) [drive] | The vanilla axle at the power face | The rectifier in the gear case | `rotate` z, ratio 1 (θ), about (24, 40) |
| Line shaft: iron shaft, bevel pinions (`lineshaft`) [drive] | The rectifier, 1:1 | The bevel wheels; the feed roll | `rotate` z, ratio 1 (ψ) |
| Rabble 1, rabble 2: spindle, bevel wheel, hub, arms, ploughs (`rabble1`, `rabble2`) [rabbles] | Its bevel wheel, from the pinion on its north rim, 8:16 | The charge on the hearth | `rotate` y, ratio −1/2 (ψ), about its spindle |
| Feed roll: body and flutes (`feedroll`) [feeder] | Keyed on the line shaft's end | The charge from the hopper | `rotate` z, ratio 1 (ψ) |
| Everything else | Static | — | none |

**Turning either way.** Only the entry turns back with the axle (θ); everything beyond the rectifier reads ψ, which only
grows, so the line shaft, the rabbles and the roll turn forward either way and the ploughs still work the charge towards
the discharge. Level 1 has nothing that moves.

**The draught** (natural, both levels): in at the ash mouth, up through the flat grate's gaps (1.17) or between the
step grate's treads, over the bridge, along the hearth under the arch, into the cavity and up the uptake to the outlet.

## Model

Everything was made for this mod; no other mod's model is used, and the generator needs nothing from `build/mods`.
**No new texture**: every texture is the game's.

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/reverberatory.json` | The whole furnace, both levels' parts (772 elements: 435 built, 489 at level 1 roasting, 726 at level 2 running) |
| `assets/seraphhorizons/shapes/block/reverberatory_frame.json` | The iron frame only (43 elements) |
| `assets/seraphhorizons/config/reverberatory-rig.json` | Cells, anchors, the build, the bricks, the row, the gearing and the part rig (33 parts) |
| `tests/ReverberatoryRoaster/rig-reference.json` | Every part's matrix at a spread of axle angles, from the reference maths |

| Code | Texture | On |
|---|---|---|
| `brick` | The furnace's brick type's block texture, set by the renderer (`bricks`, the crucible furnace's types); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork, world-mapped |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining, the arch and its skewback, world-mapped (the arch's rotated strips proportionally) |
| `iron` | `game:block/metal/plate/iron` | The frame, the iron set in, the ironwork, the doors, the damper, the hopper, the drive, the rabbles, the stoker, the cap |
| `oak` | `game:block/wood/debarked/oak` | The entry |
| `chute` | `game:block/metal/sheet/copper1` | The vanilla chute's sections |
| `pipecopper`, `pipelead`, `solder` | UnifiedPipes' pipe textures and the solder (`Pipes/tools/solderedpipe.py`) | The water's pipes |
| `ore` | `game:block/pile/oldore` | The charge |
| `coal` | `game:block/coal/bituminous` | The coal in the bunker and the throat |
| `ember` | `game:block/coal/ember`, glow 200 | The burning beds |

### Regenerating

```sh
python3 mods-src/seraphhorizons/ReverberatoryRoaster/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/ReverberatoryRoaster/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/ReverberatoryRoaster/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

A full run takes about 70 s and is deterministic (two runs are byte-identical). `tools/validate_reverberatory.py` holds
the checks, and the run exits non-zero if one fails. Checks that look at parts together look at one **scene** at a time
(what is fitted: the build's state, the level's, the damper's place, the pipes' metal, the cap), never both levels at once:

- **Parts:** the Euler round trip; every element in its part; no duplicate names, no empty part; the elements drawn at
  each level's state.
- **Textures** by role (brick, lining, iron, chute, oak, the pipes and solder, ore, ember, coal), every element.
- **The stages and the levels:** nothing floats at each stage (the frame alone, with the brickwork, the lining, the
  arch) and at each level's state; the frame iron only, no thin post standing alone, its ties hidden in the brickwork;
  level 2 keeps level 1's fittings but the three it replaces; every `requires` value is a stage's, a level's, a content
  or the cap.
- **Containment:** every element, at every sampled angle, inside the footprint's 35 cells; the chimney's cell free.
- **The shared wall** (the owner's rule): its face plain brick at every half voxel (10240 points), nothing else at its
  plane; the doors and the buckstays on the east; two furnaces back to back (the second turned half round) touch nowhere
  but at their shared walls, at rest and turning.
- **The binding** (the owner's rule: angle iron): as "The binding" above.
- **The lining:** every hot face (the firebox and its roof, the bridge, the hearth's bed and walls, the cavity, the
  uptake, the feed hole, the throat) lined at least 3 deep or to where the brick ends (4576 points); the ash pit, the
  ash mouth and the tunnel the brickwork's; the arch refractory.
- **The build's counts:** the brick boxes' volumes are the lattice's, and give the rig's quantities.
- **The charge's path:** the feed drops in the second rabble's sweep, the sweeps meet, the discharge hole is in the
  first's, past the bridge, over the tunnel; the sweeps clear the bridge, the flue end wall and the side walls by a voxel.
- **The firing:** level 1's bars on both bearers, the fire door over them, the ash mouth under them, the well's cover
  flush; level 2's treads stepping down to the bridge, the throat's foot over the top tread, the bunker under the fuel
  cell; the two grates never drawn together; the fire end face open only at the fire door and the ash mouth.
- **The doors:** the work doors on the east only; open, the plates clear of the openings and sight lines from outside
  them to the hearth's middle line clear but for the charge; shut, the plates over the openings; the shared wall
  unbroken; the discharge door over the tunnel's mouth shut, clear of it open.
- **Supports:** the line shaft in the gear case's boss, two pedestals and the neck's end walls; the entry in the north
  boss; each spindle in its gland and its swivel, its wheel's hub on the swivel.
- **Gearing:** each bevel pair's pitch cones share the apex where the axes meet; the contact point moves alike on both
  either way round; tooth and gap within a tenth of a tooth; the box teeth run at most 0.3 into each other (0.287).
- **The feed roll:** keyed on the shaft, in the neck under the slot, over the feed chute; the chute through the roof.
- **Turning either way:** the line shaft, the rabbles and the roll posed alike for the axle turned either way; only the
  entry turns back; nothing moves at level 1.
- **The chutes** (the owner's rule): the infeed's and the fuel's sections up and down, the output's along x, each the
  vanilla chute's walls in its cell and texture up to its anchor's face, checked against the game's own
  `shapes/block/metal/chute/straight.json` when the game is at hand.
- **The water:** the manifold ppex's pipe at the water cell's top face, open up; the small-bore pipes join it to both
  swivels' ports, every piece touching the next; copper and lead the same boxes; drawn only with the rabbles.
- **The damper:** open, half and shut in its recess between its guides over the ash mouth.
- **The outlet** (review of the branch: it clashed with the infeed and the drive's row): beside the drive's row, not
  under it or the infeed, under the chimney's cell; its bore clear from the cavity to the sky at every angle but for the
  cap; the cap flush in the curb's seat; the curb the lining's brick.
- **The draught:** points from outside the ash mouth to the sky over the outlet clear, at each level.
- **Clearances** at 24 angles and **swept paths** every 5° over the rabbles' two turns (full runs): every moving part
  against everything drawn at level 2 (intended contacts in `ALLOWED`).
- **No z-fighting** (full runs) over ten poses (level 2 at four angles, level 1, the damper's places with the cap on, lead
  pipes, the frame, brickwork and lining stages).
- **Files:** every texture declared and a file of the game's; the bricks the crucible furnace's types; glow on the
  burning beds only; the frame shape iron only; boxes in every cell and a deck over every column; the anchor cells are
  cells, the chimney's is not, the chimney's line stands on the outlet; no air port, no return; the shipped model is the
  checked one moved.

`tools/tests/test_reverberatory_model.py` and `site/test/reverberatory.test.ts` hold the written files to the rig's
maths (the reference poses), the cells, the build and the levels against the viewer's states, the measured counts, the
bricks and the anchors.

### Rig schema (`reverberatory-rig.json`)

The gear cutter's and the branch's schema: no work quantity; the entry reads θ, every part beyond the rectifier
`"input": "travel"`, ψ.

- `cells` (35), each `pos` and `boxes` (the finished machine at level 2, at rest), and a deck (`lid`) on every column's
  top cell. A level 1 or part-built furnace's collision is gameplay's.
- `powerCell`/`powerFace`, `infeedCell`/`infeedFace`, `outputCell`/`outputFace`, `fuelCell`/`fuelFace`,
  `waterCell`/`waterFace`, `outletCell`/`outletFace`; the points; the `chimney` line.
- `build`: `provisional`, `frame` (ingredients not settled), `stages` (`requires`, `volumeBlocks`, `materials`,
  `perClick`, `oneType`, `alsoParts`), `levels` (`fits`, `also`, level 2's `replaces`), `outletCap`.
- `bricks`: the crucible furnace's. `furnace`: `minChimney`, `rowMax`, `rowAxis`, `rowFacing`. `gearing`: as drawn.
- `parts`, in the build's order: `brickwork`, `frames`, `lining`, `arch`, `ironwork`, level 1's, the damper's places,
  level 2's, the pipes, the contents, the cap, and `frame` (no requires).

**In the viewer** (`site/models.json`, `reverberatory`): the θ slider turns the axle and ψ follows it, "Axle turns
backwards" turns only the entry back; Play turns the axle. Five selects of states: **Build** (provisional: the frame, 1
brickwork, 2 lining, 3 arch and ironwork), **Level** (built and nothing fitted, level 1 cold and roasting, level 2 cold
and running; it waits for the furnace to be built), **Ash-mouth damper** (open, half, shut), **Water pipes** (copper,
lead; it waits for level 2's rabbles) and **Outlet** (open, capped). A **Brick** select gives the crucible furnace's nine
brick types. The page opens built, at level 2 running, damper open, copper pipes, outlet open. Level 1's open work doors
show the hearth from the east.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`fm_*` the iron set into it, `ln_*` the lining (`ln_brick_NNN`), `arch_*` (`arch_v<i>_<j>`, `arch_skew*`), `iw_*` the
ironwork, `hopper_*`, `firedoor_*`, `hf_*` level 1's firing, `doorsopen_*`, `doorsshut_*`, `dischargeshut_*`,
`dischargeopen_*`, `damper<place>_*`, `entry_*`, `gearcase_*`, `lineshaft_*`, `pedestal*`, `rabble1_*`, `rabble2_*`,
`rabblemount*` (gland, base, swivel, port), `feedroll_*`, `feeder_*`, `stoker_*`, `output_*`, `pipecopper_*`,
`pipelead_*`, `charge_*`, `fireflat_*`, `firestep_*`, `outletcap_*`, and `fr_*` the iron frame. Hand edits are lost when
the script runs again: port them into `make_shape.py`, or stop regenerating.

## Salvaged from the branch, and what is new

From `737-roaster-model-v2`'s level 2 (evaluation `evals/737-roaster.md`), copied nearly as they were: the drive
(entry, gear case with its rectifier, line shaft, bevel pinions, pedestals), the rabbles and the `bevel` helper, the feed
roll, the chute sections and `CHUTE_UD_WALLS`, the segmental arch (now nine strips, not seven), and their checks (supports,
gearing, feed roll, reverse, chutes, the charge's path, the sight lines, clearances and swept paths). Adapted: the walls,
the ironwork, the firebox, the plinth and pit (now the brickwork's base and the discharge tunnel). From the crucible
furnace: the brick lattice and its world-mapped boxes, the lining's zones, the any-fired-brick `bricks`, the damper, the
outlet's curb and cap, the build's counts, and the lining, sides, damper, outlet and draught checks. Discarded: the
stalls, the trestle, the flue chamber and stack, the sulfur door, `RETIRED` and the `levels` retire scheme, and the
per-cell `fitted` collision (`9fa58a5`, never ported).

New: the 5-long layout with flat-topped end blocks, the outlet in the flue end's west corner, the shared west wall, the
work doors on the east only, level 1's fire door, flat grate and well cover, the coal well and stoker at level 2, the ash
mouth's damper, the discharge door and the output chute with its spout, the water's manifold, small-bore pipes and the
swivels, the hopper's neck kept at level 1, and the build's states.

**Provisional pending #711** (shared machine code): the chute anchors (`infeedCell`, `outputCell`, `fuelCell`, drawn as
the vanilla chute's sections against the game's file), the water's `waterCell` (a ppex pipe onto the manifold from
above) and the reservoir it fills, and the furnace's row (`furnace`).

## Choices, and why

- **Flat-topped end blocks with the arch between.** The fire end needs a roof over the firebox at level 1 and a coal
  bunker over it at level 2; the flue end needs an uptake to an outlet on the top that the drive's row does not cover. A
  solid block at each end carries both, its top flush, as the crucible furnace's top is; the arch spans the 3 blocks
  between, where the rabbles' spindles pass through its crown.
- **The outlet in the flue end's west corner** (the branch's outlet sat in the infeed's cell, under the drive's row):
  the hopper keeps the infeed cell over the hearth's far end, where the second rabble can reach the charge, and the uptake
  rises from the cavity's south-west corner, out of every rabble's sweep, to an outlet beside the hopper.
- **The shared wall to the top, the arch's foot built into it.** Two furnaces back to back take each other's thrust
  through it (a battery's parting wall), so it needs no buckstays; its face is plain brick, flush, and the tie rods end
  on anchor plates set in its top.
- **A coal well with a cover at level 1.** The fire end block is the same at both levels (the footprint and the brick
  are fixed from the frame on), so the bunker's place is built with it: a well in its top over a throat through the
  firebox's roof, closed flush by iron plates at level 1, where the fire is stoked through the fire door; at level 2 the
  bunker's liner, throat and the step grate go in, the cover and the flat grate come out (the issue's "in place of").
- **The hopper kept at both levels, on a neck.** Level 1 is charged through the hopper's mouth (the issue), so the
  hopper, its neck and the feed chute are level 1's; at level 2 the feed roll turns in that neck, its end walls the line
  shaft's last bearings.
- **Small-bore pipes from a ppex manifold to the swivels.** ppex's pipe is 6 voxels across; between the crown and the
  wheels there are 3, and the line shaft runs over the spindles. So ppex's pipe ends in a manifold under the water cell's top (a cube of
  it, as UnifiedPipes draws a joint) and small pipes in the same metal carry the water to a swivel on each spindle,
  between the crown and the wheel: the spindle's upper bearing, with a port on its side.
- **The output by a spout into the vanilla section.** The discharge hole must be over the hearth (inside the walls) and
  the vanilla section flush with the face, so the section fills the tunnel's mouth and an iron spout carries the charge
  from under the hole down onto its floor.
- **The discharge door slides along z.** It cannot swing out past the footprint, nor slide up into work door 1's frame
  above it; it slides on rails in a recess in the footing's face.
- **Nine voussoirs** (the branch's weak spot: seven): the crown strip still takes the glands and the pedestals' bases.

## Open for the owner

1. **The outlet in the flue end's west corner** (beside the hopper), or elsewhere: the middle column's top is the drive's
   row and the infeed's.
2. **The coal well and its cover at level 1**: a well built into the fire end block with its throat through the
   firebox's roof, covered by three iron plates until level 2. The alternative is a firebox roof with no hole at level 1
   and the bunker's throat cut through at level 2 (brick pulled down at an upgrade).
3. **Two in a row, back to back** (`rowMax` 2, `rowFacing` opposite): the doors on one side only allow no more.
4. **Water to the swivels by small-bore pipe** from a ppex manifold, not ppex's own pipe (which cannot reach them); and
   where the water anchor is (the drive row's top over the first rabble, so the player's pipe comes from above).
5. **The build**: the frame's recipe, the stages and their order (the frame, brickwork, lining, arch with ironwork as
   drawn), the ironwork's and the levels' materials; the brick counts are measured (27 fired bricks; 17 + 9 refractory
   bricks, 7 + 4 fire clay).
6. **The chimney's minimum**: the crucible furnace's six blocks, taken as they are.
7. **The sulfur** (0.7 of a sulfide's sulfur through the flue, #736): nothing is drawn for it; an anchor at the outlet
   or the uptake if gameplay needs one.
8. **Readability:** level 1's open work doors show the hearth from the east; level 2's are shut (the issue), so its
   rabbles are seen only from above with the arch hidden, or through level 1's doors.
9. **Rabble count**: two, as the branch's; real furnaces had more.

## Known weak spots, and what is not checked

Reviewed in projections rendered from the written files; no one has looked at it in a client or in the viewer with WebGL.

1. **Box teeth**: the bevel teeth run up to 0.29 into each other in mesh (the gear cutter's limit 0.3).
2. **The arch is nine strips**: its outside is a polygon; the strips' feet run into the shared wall and the skewback.
3. **Static contents**: the charge, the fire and the coal are parts, not work.
4. **The feed roll and the rectifier are hidden** in the neck and the gear case.
5. **Lining on an outside face**: the flue end block's north face, just above the arch's haunches, shows the cavity
   roof's lining edge (y 27..30).
6. **Collision is the finished machine's** at level 2, with the doors shut: level 1's and a part-built furnace's are
   gameplay's.
7. **The tie rods are straight over the crown**, standing clear of the arch's haunches.
