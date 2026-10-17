# Reverberatory roaster: the model

Part of the Seraph Horizons mod (`../README.md`): the ore line's roaster of concentrate (epic #684, behaviour #736),
the generated model (#737). It roasts sulfide concentrate into roasted concentrate continuously, with water-cooled
mechanical rabbles: 100 %, 76 concentrate an hour; the firepit roasts concentrate at 85 % until the player builds
it. **Status: in the game** (#736). This folder's `tools/` holds the model's generator and its checks; `Core/` and
`Game/` hold the furnace's gameplay, on the shared ore machine code (`Machines/`): the mod README's "Ore processing:
the reverberatory roaster" has the rules. The build's materials past the masonry, left open below, are set in
`assets/seraphhorizons/config/builds/reverberatory.json`; the frame's recipe in `recipes/grid/reverberatory.json`.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, `Pipes/` and `CrucibleFurnace/`, this folder's neighbours. The generic
half of the generator is `Machines/tools/machinegen/`, unchanged by this machine: no new driver (every motion is a
`rotate` on θ or on ψ, the axle's travel) and no change to the package. The water's pipes are drawn by
`Pipes/tools/solderedpipe.py`, as the rosser's are.

## The furnace

A **long-bedded reverberatory roaster** [Peters 1895, ch. VII–VIII; Hofman 1904, figs. 96–143]: a level hearth under a
low arch, the fireplace at the north end behind a fire bridge, the flame drawn along the hearth to the flue at the
south end. It is **the mechanical furnace**: two water-cooled rabbles on spindles through the arch, turned from a line
shaft on the roof, built in stages from the iron frame to its services. It follows the **crucible furnace's conventions** (`CrucibleFurnace/README.md`):
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
| `powerCell`, `powerFace` | `[0,2,0]`, north | The vanilla axle comes in along z at the cell's centre (y 2.5) into the gear case |
| `infeedCell`, `infeedFace` | `[0,2,4]`, up | The hopper's mouth (the cell's top): a section of the vanilla chute hangs in it |
| `outputCell`, `outputFace` | `[1,0,2]`, east | The discharge tunnel's mouth in the footing's east face: a section of the vanilla chute flush with the face |
| `fuelCell`, `fuelFace` | `[-1,1,0]`, up | The coal bunker's chute: a section of the vanilla chute from the cell's top into the bunker's west end |
| `waterCell`, `waterFace` | `[0,2,1]`, east | The water: a ppex pipe from the side (the owner's ruling), along the roof beside the drive's row, into the manifold at the face's 5..11 as ppex lays a pipe; between pedestal 1 and the first wheel, the shortest way to the swivels clear of the doors, the outlet, the power and the frame. Provisional pending #711 |
| `outletCell`, `outletFace` | `[-1,1,4]`, up | The outlet, in the flue end's **west** corner: beside the drive's row, never under it or under the infeed (the branch's clash) |
| `chimney` | origin `[-0.3125,5,4.4375]`, y, 6 | The shortest chimney (the crucible furnace's six blocks, provisional), from the outlet up; its first cell `[-1,2,4]` is never the furnace's |
| `infeed`, `output`, `fuel`, `water` | points | The middles of those faces |
| `charge`, `discharge` | points | Where the feed chute drops the charge on the hearth; the discharge hole by the bridge |
| `ash` + `ashSide`, `fireDoor` + `fireDoorSide` | north | The ash mouth (the damper) and the fire door (opened to clear clinker), on the controller's face |
| `workDoor1`, `workDoor2` + their sides | east | The work doors, at the rabbles, shut (kept for clearing accretions; door 1's plate with the peep hole) |
| `build`, `bricks`, `furnace`, `gearing` | | The build (below), the brick types (the crucible furnace's), the row, the drawn gearing: not drawn |

## The build (provisional)

Settled with the owner later (#737, "Build stages"); the rig's `build` carries `"provisional": true`. **Six stages**
(the owner's ruling): the iron frame; the masonry; the hopper and doors; the drive and rabbles; the feeder, stoker and
output; and last the **services**, the chute sections and the water's pipes (the owner's ruling for every machine). Each
stage after the frame fits its `requires` ids in order, each standing on what came before it (the drive's line shaft
runs in the hopper's neck, so the hopper goes in first; the feed roll on the line shaft's end). The rig's
`build.stages` lists each stage's `id` and its `fits`: each fit's `requires`, the parts fitted with it (`alsoParts`),
its states (`also`, one of them fitted) and its materials, the masonry's measured, the rest not settled (`null`).

| | Stage (`id`) | Fits (`requires`) | What it puts in | Materials (measured) |
|---|---|---|---|---|
| 1 | **Iron frame**, a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (`frame`) | The box frame (every upright, the knees, every band, the skewback angle), the ash mouth's lintel, the damper's guides and sill, standing on their own (the end blocks' east corner angles on a short post each, the lintel on two, hidden in the brickwork) | not settled |
| 2 | **Masonry** (`masonry`) | `brickwork` (and `frames`); `lining`; `arch` (and `ironwork`) | **Brickwork**: The base, the shared wall, the outer walls, the footing, the end blocks: 12.92 blocks; with it, at no cost of its own, the iron set in as it is laid (the fire door's lintel and dead plate, the work doors' frames and guides, the discharge door's rail). **Lining**: Tier-3 refractory 3 thick on every hot face: 4.14 blocks. **Arch**: The arch's 9 strips and the east skewback, tier-3 refractory: 2.02 blocks; with it the angle buckstays, the tie rods and their anchors. | **26 fired bricks**, any one type; **17 tier-3 refractory bricks, 7 fire clay**; **9 tier-3 refractory bricks, 4 fire clay** (the ironwork not settled) |
| 3 | **Hopper and doors** (`hopperdoors`) | `hopper`; `doors` (`firedoor`, `doors`, `discharge`; also `damperopen`, `damperhalf` or `dampershut`) | **Hopper**: The hopper on the flue end's roof, its neck on its base plate, the iron feed chute through the roof. **Doors and damper**: The fire door shut, the work doors shut (door 1's peep hole), the discharge door slid clear of the tunnel's mouth, the ash mouth's damper in one of its places. | not settled |
| 4 | **Drive and rabbles** (`drive`) | `drive` (`entry`, `gearcase`, `lineshaft`, `pedestals`); `rabbles` (`rabble1`, `rabble2`, `rabblemounts`) | **Drive**: The gear case with its rectifier on the coal well's rims, the entry, the line shaft with its bevel pinions, the pedestals. **Rabbles**: The two rabbles, their bevel wheels, glands, swivels and ports. | not settled |
| 5 | **Feeder, stoker and output** (`feeding`) | `feeder` (`feedroll`); `stoker`; `output` | **Feeder**: The fluted feed roll on the line shaft's end, in the hopper's neck. **Stoker**: The coal bunker's liner, hung by its rim flush in the well's seat; the throat; the step grate. **Output**: The spout from under the discharge hole down the tunnel. | not settled |
| 6 | **Services** (`services`) | `services` (also `pipecopper` or `pipelead`) | **Services**: The vanilla chute's sections (the infeed's in the hopper's mouth, the fuel's into the bunker, the output's in the tunnel's mouth) with their straps; the water's manifold, connector, reducer and small pipes in one metal. | not settled |
| | *Contents* | `charge`, `fire` | Not a stage: gameplay's state once the furnace is finished (the bed on the hearth; the fire on the step grate, the coal in the throat and the bunker) | |
| | *Outlet cap* | `outletcap` | Not a stage: on the furnace of a pair that does not carry the chimney | 1 iron plate |

The counts are measured from the model as the crucible furnace's are: the brick is a lattice (every void's faces, the
lining's zones' and the blocks' boundaries) merged into world-mapped boxes, the brickwork's and the lining's apart, at 2
fired bricks or 4 refractory bricks a block, rounded up, fire clay one per 2.5 refractory bricks.

## The binding

Every piece of iron that binds the brickwork is **angle iron**, an L section 0.75 thick with legs 2 wide (`ANGLE_T`,
`ANGLE_L`), wrapping the brick's corner: one leg on each face, its inside corner bedded against the brick's corner, so it
holds the brick in on both faces (the owner's rule for every furnace). It is one **closed box frame** following the
brickwork's outline (`binding_members` in `make_shape.py`): an angle up every outside vertical corner, an angle band along
every top edge, each meeting the next at its ends, so the iron reads as a box round the whole furnace, not end frames
and pieces. The end faces are the footprint's and the west face is the shared wall's, so nothing may stand proud of
them: every leg is **set into the brick, flush**.

- **Uprights** (`fr_angle_<n|s><w|e|f|i>_face`, `_side`): at the shared wall's corners, full height; at the end blocks'
  east corners, from the footing up (each on a short post hidden in the footing, `fr_tie_angle_*`); at the footing's east
  corners, to its top; and at each end block's **inner** east corner (`i`), where the east wall drops to the skewback
  angle under the arch's bay, standing on it.
- **Knees** (`fr_knee_<n|s>_face`): where the footing steps out from under the end block's east corner, a plate in the
  end face fills the step's inside corner, so the end-face legs run without a break from the ground to the top.
- **Bands** round every top edge: the end faces' (`fr_band_<n|s>`), the shared wall's outer one (`fr_band_w`, flush in
  its face), each end block's east one (`fr_band_<n|s>e`), the footing's (`fr_band_f`, the length of the east side: the
  buckstays, the work doors' guides and the discharge door's recess stand under or on it), and round the arch's bay:
  the shared wall's inner one (`fr_band_wi`, the tie rods passing through it), each end block's inner one
  (`fr_band_<n|s>i`, its face leg reaching down to the arch in steps following the strips, hidden behind the arch's end,
  so no brick shows between the band and the arch; over the flue end's cavity no lower than its roof) and the
  **skewback angle** (`fr_skew`): its face leg the skewback's back, the east side's length, on through the end blocks to
  their corner angles; its top leg the skewback's top to the arch's foot (the skewback now stops at the foot).
- **Buckstays** (`iw_stay<i>_flange`, `_web`; the ironwork, with the arch): each an angle standing on the footing band,
  its flange against the wall and the skewback angle, its web standing out from the flange's south edge to the
  footprint's face; the tie rod passes through the flange and its nut bears on it. The second and third stand against
  the work doors' north guides (no strip of wall between). The **tie rods** run over the crown from them through the
  west inner band to **anchor plates** in the shared wall's top, bearing against the west band's top leg.

The frame is the build's first stage, so the box and the skewback angle stand on their own before any brick (the
uprights on the ground or the posts, the inner uprights on the skewback angle, the bands between them).

**No slivers of brick** (the owner's rule): no piece of the brickwork or the lining that shows from outside is narrower
than 3 voxels (`MIN_BRICK`) either way. For it the well's seat and the bunker's rim in it run out to the bands; the outlet's curb and
the feed hole's top course are the lining's out to the bands; the hopper's neck stands on a base plate from the inner
band to the south band, over the feed hole's lining; the damper's recess runs from the ground (an iron sill under the
damper) to the fire door's recess, and the damper's open place is at the recess's top; the fire door's plate fills its
recess and its opening has an iron dead plate through the brickwork to the lining; the discharge door's recess is the
mouth's length twice, its plate the mouth's length, its low rail at the tunnel's floor and its top edge in a slot behind
the footing band's face leg; the coal well's floor beside the throat is the lining's.

Checks hold it. `check_binding`: every leg is its box, `ANGLE_T` thick across its face, flush in the face's plane with no
brick proud of it, bedded behind (brick, the arch, or the iron it joins; the footing band's face leg is the discharge
recess's lip there), each angle's legs meeting; the buckstays as above. `check_box_frame` reads the outline afresh (the
height of the brick, the arch and the frame over every voxel column, the openings in the top filled) and requires: every
top edge (a step of 3 or more) wrapped by iron in its face just under the edge and in its top just in from it, all along
it; every outside vertical corner (every voxel up to the bands) with iron in both its faces beside it; every band's and
upright's ends meeting another member over an area (an upright's foot may stand on the ground); the frame one connected
group, and one with the ironwork. `check_slivers` measures every showing face of brick (built, and finished) along the
face to the iron, the other brick, an edge or a corner (a recess's reveal and a seat's rebate do not count). Dropping
the west band, an inner upright, the footing band or the west inner band, shortening a band's leg or the skewback's top
leg, or putting the well's seat or the buckstays back where they were, each fails.

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
sliding in a recess over it from the ground, on an iron sill, between its guides (open, half, shut: the crucible
furnace's), and above it the **fire door** (x 19..29, y 14..20), its plate shut filling its recess (opened to clear
clinker off the grate), an iron dead plate on its sill. In the fire end block's top, the **coal well** (x 3..41, z 3..13,
y 25..31.5) over a **throat** through the firebox's roof (x 9..39, z 7..12): in it the **bunker**'s iron liner, hung by its
**rim**, flush in the seat round the well's mouth out to the bands, an iron throat hanging to y 20 over the top tread, and the **step grate** (four treads from z 6 to the bridge, tops
18.5, 16.5, 14.5, 12.5, the air rising between them), the coal sliding down the steps as it burns. The fuel's chute
section drops into the bunker's west end from the fuel cell's top, hung from a strap on the liner. The gear case stands
on the well's rims, bridging it.

**The drive**, as the branch's: the oak entry (the vanilla axle's cross) from the power face into a closed
cast-iron **gear case** (x 18..30, y 33..46, z 2..12) whose **rectifier** (two pairs of gears on one-way catches, one
through an idler; not drawn) turns the iron **line shaft** (24, 40) one way whichever way the axle turns. The shaft runs
in the case's south boss, two **pedestals** on the arch's crown (z 24..27, 46.6..49.2) and the hopper neck's end walls.
Each rabble (spindles at z 39 and 60) is driven by a **bevel pair**, an 8-tooth pinion on the shaft on the north rim of a
16-tooth wheel on the spindle (module 0.7), their pitch cones' apex where the axes meet: the rabbles turn at −1/2 of the
axle.

**The rabbles.** Each **hollow spindle** runs from the hearth up through a **gland** in the crown strip and its
**swivel** on a bracket on the crown (the spindle's upper bearing, and the water's way in), the wheel's hub resting on
the swivel. In the hearth it carries a hub with two arms, three ploughs on each set 40° across it; the sweeps reach
12.44 from each spindle, so the second rabble's takes in the charge under the feed chute, the first's the discharge
hole, and the two overlap between (a quarter turn apart, the arms never meet: the swept paths).

**The feed.** The **hopper** (x 16.5..31.5, y 42..48, z 66..78) on its **neck** (x 20..28, y 33..42) on a base plate on
the flue end block's top, from the inner band to the south band; the iron **feed chute** from the neck down through the
roof into the cavity (y 26.5..35). A **fluted feed roll** keyed on the line shaft's end turns in the neck under the
hopper's slot and meters the charge down the chute, to the hearth's far end; a **section of the vanilla chute** hangs in
the hopper's mouth from two straps on its rim (the services').

**The output**: a **section of the vanilla chute** along x in the tunnel's mouth (x 40..48), flush with the face (the
services'), and an iron **spout** from under the discharge hole down onto its floor, so the roasted charge falling through
slides into the chute. The discharge door (a plate sliding along z in its recess in the footing's face, on a low rail at
the tunnel's floor, its top edge in a slot behind the footing band's face leg) is slid clear of it, into the recess's
other half; it would be slid back over the mouth with the chute off, to clear the tunnel.

**The water** (the services'): the **manifold**, a cube of ppex's pipe (UnifiedPipes' soldered pipe, 6 across) under the water
cell's east face (x 26..32, y 37..43, z 21..27), open east to the player's pipe and south, plugged west; south from it a
full-size **connector** of the same pipe (z 27..30.5), and at its end the **vanner's soldered reducer** (`Vanner/tools/make_shape.py`): the connector's end
plugged round the small pipe's bore by four plates inside its walls, a ring of solder round the small pipe where it
leaves, the small pipe off the connector's middle (x 30) as the vanner's is. The **small pipe** is the vanner's spray
pipe's size, a soldered tube 2.4 across round a 1.6 bore: on south to z 33, down just short of the first wheel, along
under the wheels (y 33.4) to a tee at the first swivel and a bend at the second, and in to each swivel's **port**, a
soldered cube at each bend and the tee and a ring of solder at each port. The same pipes in copper (`pipecopper`) or lead (`pipelead`), one drawn. The water is used up: no return
anchor (rule 4).

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
the discharge. Nothing moves before the drive is fitted.

**The draught** (natural): in at the ash mouth, up between the step grate's treads, over the bridge, along the hearth under the arch, into the cavity and up the uptake to the outlet.

## Model

Everything was made for this mod; no other mod's model is used, and the generator needs nothing from `build/mods`.
**No new texture**: every texture is the game's.

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/reverberatory.json` | The whole furnace, every stage's parts and every state's (944 elements: 548 built to the arch, 885 finished and running) |
| `assets/seraphhorizons/shapes/block/reverberatory_frame.json` | The iron frame only (131 elements) |
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

A full run takes up to about 2 minutes and is deterministic (two runs are byte-identical). `tools/validate_reverberatory.py` holds
the checks, and the run exits non-zero if one fails. Checks that look at parts together look at one **scene** at a time
(what is fitted: the build's stage, the furnace cold or running, the damper's place, the pipes' metal, the cap):

- **Parts:** the Euler round trip; every element in its part; no duplicate names, no empty part; the elements drawn
  built, finished cold and running.
- **Textures** by role (brick, lining, iron, chute, oak, the pipes and solder, ore, ember, coal), every element.
- **The stages:** nothing floats at each of the six stages, after each fit within a stage (the brickwork alone, with
  the lining, ...) and running; the frame iron only, no thin post standing alone, its ties hidden in the brickwork;
  each stage's fits touch what the stages and fits before them put in; every `requires` value is a fit's, a state's
  (the damper's places, the pipes' metals), a content or the cap.
- **Containment:** every element, at every sampled angle, inside the footprint's 35 cells; the chimney's cell free.
- **The shared wall** (the owner's rule): its face plain brick at every half voxel (10240 points), nothing else at its
  plane; the doors and the buckstays on the east; two furnaces back to back (the second turned half round) touch nowhere
  but at their shared walls, at rest and turning.
- **The binding, the box frame and slivers** (the owner's rules): as "The binding" above (`check_binding`,
  `check_box_frame`, `check_slivers`).
- **The lining:** every hot face (the firebox and its roof, the bridge, the hearth's bed and walls, the cavity, the
  uptake, the feed hole, the throat) lined at least 3 deep or to where the brick ends (4572 points); the ash pit, the
  ash mouth and the tunnel the brickwork's; the arch refractory.
- **The build:** the brick boxes' volumes are the lattice's, and give the rig's quantities; the stages are the
  structure's, the fittings' and the services', in order, the services last; every stage's
  `alsoParts` are fitted with it.
- **The charge's path:** the feed drops in the second rabble's sweep, the sweeps meet, the discharge hole is in the
  first's, past the bridge, over the tunnel; the sweeps clear the bridge, the flue end wall and the side walls by a voxel.
- **The firing:** the treads stepping down to the bridge, the fire door over them and the ash mouth under them, the
  throat's foot over the top tread, the bunker under the fuel cell, its rim flush in the well's seat; the fire end face
  open only at the fire door and the ash mouth.
- **The doors:** the work doors on the east only, shut, the plates over the openings but for door 1's peep hole; the
  shared wall unbroken; the discharge door clear of the tunnel's mouth, in its recess.
- **The peep hole** (the owner's ruling: the doors are shut, so the rabbles showed only from above): inside door 1's
  opening, so through it only the opening and the hearth show; no shutter; three level sight lines and one looking down
  through it cross nothing of the finished furnace but the charge and meet the rabble at every angle, its arms crossing
  the downward one (34 of 144 angles).
- **Supports:** the line shaft in the gear case's boss, two pedestals and the neck's end walls; the entry in the north
  boss; each spindle in its gland and its swivel, its wheel's hub on the swivel.
- **Gearing:** each bevel pair's pitch cones share the apex where the axes meet; the contact point moves alike on both
  either way round; tooth and gap within a tenth of a tooth; the box teeth run at most 0.3 into each other (0.287).
- **The feed roll:** keyed on the shaft, in the neck under the slot, over the feed chute; the chute through the roof.
- **Turning either way:** the line shaft, the rabbles and the roll posed alike for the axle turned either way; only the
  entry turns back; nothing moves before the drive is fitted.
- **The chutes** (the owner's rule): the infeed's and the fuel's sections up and down, the output's along x, each the
  vanilla chute's walls in its cell and texture up to its anchor's face, checked against the game's own
  `shapes/block/metal/chute/straight.json` when the game is at hand.
- **The water:** the water face a side face of the drive's row (the owner's ruling), the manifold ppex's pipe against
  it at its 5..11, open to it, the player's pipe's way beyond it clear; the connector ppex's pipe out of its
  side, its end plugged round the small pipe's open bore, the reducer's ring of solder round the small pipe at the plug
  (the owner's ruling: the vanner's reducer); every run of the small pipe 2.4 across; the pipes join the manifold to both
  swivels' ports, every piece touching the next; copper and lead the same boxes; drawn only with the services.
- **The damper:** open, half and shut in its recess between its guides over the ash mouth.
- **The outlet** (review of the branch: it clashed with the infeed and the drive's row): beside the drive's row, not
  under it or the infeed, under the chimney's cell; its bore clear from the cavity to the sky at every angle but for the
  cap; the cap flush in the curb's seat; the curb the lining's brick.
- **The draught:** points from outside the ash mouth, between the treads, to the sky over the outlet clear.
- **Clearances** at 24 angles and **swept paths** every 5° over the rabbles' two turns (full runs): every moving part
  against everything drawn finished and running (intended contacts in `ALLOWED`).
- **No z-fighting** (full runs) over 11 poses (running at four angles, cold with the damper's other places and the cap
  on, lead pipes, and the five stages before the last).
- **Files:** every texture declared and a file of the game's; the bricks the crucible furnace's types; glow on the
  burning beds only; the frame shape iron only; boxes in every cell and a deck over every column; the anchor cells are
  cells, the chimney's is not, the chimney's line stands on the outlet; no air port, no return; the shipped model is the
  checked one moved.

`tools/tests/test_reverberatory_model.py` and `site/test/reverberatory.test.ts` hold the written files to the rig's
maths (the reference poses), the cells, the build's stages against the viewer's states, the measured counts, the
bricks and the anchors.

### Rig schema (`reverberatory-rig.json`)

The gear cutter's and the branch's schema: no work quantity; the entry reads θ, every part beyond the rectifier
`"input": "travel"`, ψ.

- `cells` (35), each `pos` and `boxes` (the finished machine, at rest), and a deck (`lid`) on every column's top cell.
  A part-built furnace's collision is gameplay's.
- `powerCell`/`powerFace`, `infeedCell`/`infeedFace`, `outputCell`/`outputFace`, `fuelCell`/`fuelFace`,
  `waterCell`/`waterFace`, `outletCell`/`outletFace`; the points; the `chimney` line.
- `build`: `provisional`, `frame` (ingredients not settled), `stages` (`id`, `fits`: each `requires`,
  `volumeBlocks`, `materials`, `perClick`, `oneType`, `alsoParts`, `also`; past the masonry `materials` null),
  `outletCap`.
- `bricks`: the crucible furnace's. `furnace`: `minChimney`, `rowMax`, `rowAxis`, `rowFacing`. `gearing`: as drawn.
- `parts`, in the build's order: `brickwork`, `frames`, `lining`, `arch`, `ironwork`, `hopper`, the doors' (`firedoor`,
  `doors`, `discharge`), the damper's places, the drive's, the rabbles', `feedroll`, `stoker`, `output`, `services`, the
  pipes, the contents (`charge`, `fire`), the cap, and `frame` (no requires).

**In the viewer** (`site/models.json`, `reverberatory`): the θ slider turns the axle and ψ follows it, "Axle turns
backwards" turns only the entry back; Play turns the axle. Five selects of states: **Build** (provisional: the six
stages, 1 the frame to 6 services), **Furnace** (cold, running; it waits for the services), **Ash-mouth
damper** (open, half, shut; it waits for the doors), **Water pipes** (copper, lead; it waits for the services) and
**Outlet** (open, capped). A **Brick** select gives the crucible furnace's nine brick types. The page opens finished
and running, damper open, copper pipes, outlet open. Door 1's peep hole shows the first rabble turning.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`fm_*` the iron set into it, `ln_*` the lining (`ln_brick_NNN`), `arch_*` (`arch_v<i>_<j>`, `arch_skew*`), `iw_*` the
ironwork, `hopper_*`, `firedoor_*`, `doors_*` (the work doors), `discharge_*`, `damper<place>_*`, `entry_*`,
`gearcase_*`, `lineshaft_*`, `pedestal*`, `rabble1_*`, `rabble2_*`, `rabblemount*` (gland, base, swivel, port),
`feedroll_*`, `stoker_*`, `output_*`, `services_*` (the chute sections and their straps), `pipecopper_*`, `pipelead_*`,
`charge_*`, `fire_*`, `outletcap_*`, and `fr_*` the iron frame. Hand edits are lost when
the script runs again: port them into `make_shape.py`, or stop regenerating.

## Salvaged from the branch, and what is new

From `737-roaster-model-v2`'s mechanical furnace (evaluation `evals/737-roaster.md`), copied nearly as they were: the drive
(entry, gear case with its rectifier, line shaft, bevel pinions, pedestals), the rabbles and the `bevel` helper, the feed
roll, the chute sections and `CHUTE_UD_WALLS`, the segmental arch (now nine strips, not seven), and their checks (supports,
gearing, feed roll, reverse, chutes, the charge's path, the sight lines, clearances and swept paths). Adapted: the walls,
the ironwork, the firebox, the plinth and pit (now the brickwork's base and the discharge tunnel). From the crucible
furnace: the brick lattice and its world-mapped boxes, the lining's zones, the any-fired-brick `bricks`, the damper, the
outlet's curb and cap, the build's counts, and the lining, sides, damper, outlet and draught checks. Discarded: the
stalls, the trestle, the flue chamber and stack, the sulfur door, `RETIRED` and its retire scheme, and the
per-cell `fitted` collision (`9fa58a5`, never ported).

New: the 5-long layout with flat-topped end blocks, the outlet in the flue end's west corner, the shared west wall, the
work doors on the east only, the fire door, the coal well and stoker with the bunker's rim in the well's seat, the ash
mouth's damper, the discharge door and the output chute with its spout, the water's manifold, connector, the vanner's
soldered reducer and small pipe, and the swivels, door 1's peep hole, the hopper on its neck, and the build's stages
with the services last.

**Provisional pending #711** (shared machine code): the chute anchors (`infeedCell`, `outputCell`, `fuelCell`, drawn as
the vanilla chute's sections against the game's file), the water's `waterCell` (a ppex pipe into the manifold from
the side) and the reservoir it fills, and the furnace's row (`furnace`).

## Choices, and why

- **Flat-topped end blocks with the arch between.** The fire end needs a roof over the firebox with a coal bunker in
  it; the flue end needs an uptake to an outlet on the top that the drive's row does not cover. A
  solid block at each end carries both, its top flush, as the crucible furnace's top is; the arch spans the 3 blocks
  between, where the rabbles' spindles pass through its crown.
- **The outlet in the flue end's west corner** (the branch's outlet sat in the infeed's cell, under the drive's row):
  the hopper keeps the infeed cell over the hearth's far end, where the second rabble can reach the charge, and the uptake
  rises from the cavity's south-west corner, out of every rabble's sweep, to an outlet beside the hopper.
- **The shared wall to the top, the arch's foot built into it.** Two furnaces back to back take each other's thrust
  through it (a battery's parting wall), so it needs no buckstays; its face is plain brick, flush, and the tie rods end
  on anchor plates set in its top.
- **The coal bunker in a well, hung by its rim.** The bunker is part of the furnace: a well in the fire end block's top
  over a throat through the firebox's roof, its iron liner hung by a rim flush in the seat round the well's mouth, the
  seat out to the bands so no strip of brick shows between them.
- **The hopper on a neck.** The feed roll turns in the neck, its end walls the line shaft's last bearings, so the hopper
  goes in before the drive.
- **The doors kept, shut.** Real mechanical furnaces kept their work doors for clearing accretions off the hearth, and
  a fire door for clearing clinker; here they are drawn shut. The discharge door stays slid clear of the output chute.
- **The services last** (the owner's ruling for every machine): the vanilla chute's sections and the water's pipes are
  the last stage, after every fitting they serve.
- **A small pipe from a ppex manifold to the swivels, by the vanner's reducer** (the owner's ruling). ppex's pipe is 6
  voxels across; between the crown and the wheels there are 3, and the line shaft runs over the spindles. So ppex's pipe
  ends in a manifold against the water cell's east face (a cube of it, as UnifiedPipes draws a joint) and a short full-size
  connector, narrowed as the vanner's is (a soldered plug in its end, a ring of solder) to the vanner's 2.4 small pipe in
  the same metal, which carries the water to a swivel on each spindle, between the crown and the wheel: the spindle's
  upper bearing, with a port on its side.
- **The water face on the drive row's east side, at cell `[0,2,1]`** (the owner's ruling: from the side, not above).
  The only cell of the row whose face's 5..11 (y 37..43) misses both wheels and the pinions: at `[0,2,2]` and `[0,2,3]`
  the manifold would stand in the wheels' teeth. It sits between pedestal 1 and the first wheel, over the east wall,
  clear of the doors (below), the outlet (the flue end's west corner), the power (the fire end's north face) and the
  frame (its top bands at y 32); from there the small pipe drops just short of the first wheel and runs under both.
- **A peep hole in work door 1** (the owner's ruling). The doors are shut, so the rabbles showed only from above; a 3.5
  by 3.4 hole low in door 1's plate, over the opening, shows the first rabble's spindle and hub and its arms going by.
  It has no shutter.
- **The output by a spout into the vanilla section.** The discharge hole must be over the hearth (inside the walls) and
  the vanilla section flush with the face, so the section fills the tunnel's mouth and an iron spout carries the charge
  from under the hole down onto its floor.
- **The discharge door slides along z.** It cannot swing out past the footprint, nor slide up into work door 1's frame
  above it; it slides in a recess in the footing's face under the footing band, the band's face leg its lip.
- **Nine voussoirs** (the branch's weak spot: seven): the crown strip still takes the glands and the pedestals' bases.

## Open for the owner

Settled by the owner (2026-10-14), not to be changed: the water face on the drive row's east side (the player's pipe
from the side); door 1's peep hole only; the outlet in the flue end's west corner; two in a row at most, back to back;
two rabbles; the flue's sulfur lost (no sulfur anchor); the water by the vanner's reducer to a small pipe; a peep hole
in a shut door; the closed box frame as drawn, and the bare refractory on the arch's top and the flue end's top. Settled
2026-10-15: the mechanical furnace only, built in stages, the doors kept shut, the services the last stage. Still open:

1. **The build**: the frame's recipe, the six stages as ruled (frame; masonry; hopper and doors; drive and rabbles;
   feeder, stoker and output; services), the ironwork's and the fittings' materials; the brick counts are measured (26 fired bricks; 17 + 9 refractory bricks, 7 + 4 fire clay).
2. **The chimney's minimum**: the crucible furnace's six blocks, taken as they are.

## Known weak spots, and what is not checked

Reviewed in projections rendered from the written files; no one has looked at it in a client or in the viewer with WebGL.

1. **Box teeth**: the bevel teeth run up to 0.29 into each other in mesh (the gear cutter's limit 0.3).
2. **The arch is nine strips**: its outside is a polygon; the strips' feet run into the shared wall and the skewback.
3. **Static contents**: the charge, the fire and the coal are parts, not work.
4. **The feed roll and the rectifier are hidden** in the neck and the gear case.
5. **The flue end's cavity at the arch's haunches**: the cavity's roof (27) is higher than the arch's extrados at its
   two feet (26.7 at x 8..9.2 and 38.8..40), so a sliver of the cavity shows between the arch's end and the flue end
   block's inner band there (under a quarter voxel); the band's face leg stops at the roof, not to hang in the cavity.
6. **Collision is the finished machine's**, with the doors shut: a part-built furnace's is gameplay's.
7. **The tie rods are straight over the crown**, standing clear of the arch's haunches.
