# Bucking Sawmill

A client and server code mod adding the bucking sawmill: a mechanically powered pair of drag saws
that cross-cut Logging Expanded (`loggingmod`) tree trunks into logs. It is built in the world from a
frame and Immersive Woodworking's (`immersivewoodworking`) sawmill parts, and is modelled on how
Immersive Woodworking's own sawmill works, without referencing either mod at build time.

`"side": "Universal"`, required on the client: the mill is a set of blocks with their own classes.
It depends on the game, `immersivewoodworking` and `loggingmod`.

## The machine

**Blocks.** The controller is `buckingsawmill:buckingmill-frame-{side}` (horizontally orientable,
item form `-north`). Placing it needs room for every cell of the rig and stamps an invisible ghost
into each other cell: `buckingmill-ghost`, and `buckingmill-ghostpower-{side}` in the cell that
takes the axle. Ghosts store the controller's position and pass interaction, breaking, the
pick-block stack, particles, name, info and help to it, as Immersive Woodworking's sawmill ghosts
do. The controller re-stamps missing ghosts shortly after it loads. Breaking the frame or any ghost
breaks the whole mill.

**Rig.** The footprint and anchor points are data, in `assets/buckingsawmill/config/rig.json`
(written by the model's tooling in `tools/`): every occupied cell with its collision and selection
boxes, the power cell and the face that takes the axle, the infeed side a rack must touch, the
point and side logs leave by, the saws' travel (`saw`, optional), and keys only the renderer
reads. Everything there is in the native,
south-facing frame with the controller at `[0,0,0]`. Turning it to a facing follows Immersive
Woodworking: with n the normal of the `side` variant, local (x, y, z) goes to
(x·nz + z·nx, y, −x·nx + z·nz), and the shape turns by rotateY north 180, east 90, south 0,
west 270. `Core/Rig.cs` parses the file and `Core/Footprint.cs` does the turning.

**Assembly.** Right-click the frame or any ghost holding a part, recognised by code path as
Immersive Woodworking does: two `sawmillsash`, one `sawmillcrankshaft`, one `sawmilllevers` and two
`sawmillblade-{metal}` kits of one metal. Any order, but each blade kit needs a sash without a kit.
The levers are the linkage that trips the windlass when the saws bottom out (see **Saws**
below). No carriage: in Immersive Woodworking it carries the log, which this
mill does not do, and holding one does nothing special here. Parts are used up outside creative
mode. Ctrl + right-click takes the trunk back if
there is one, otherwise the last blade kit. The other parts come back only by breaking the frame,
which drops the frame, every fitted part, the blade kits and a recoverable trunk. The rules are in
`Core/Parts.cs`.

**Power.** `BEBehaviorMillMP`, a mechanical power consumer on the power ghost, connects only
through the rig's power face, turned to the mill's facing (like Immersive Woodworking's
`BEBehaviorSawmillMP`, without its cap on the network's speed). It loads the shaft with 0.005 until
the mill is assembled, then with `Resistance`. The mill cuts, winds its saws up, and pulls from a
rack, only while the shaft turns at `MinSpeed` or faster.

**Trunks.** One at a time, held as the trunk's whole item stack, so one taken back out is unchanged.
Any `loggingmod:treetrunk-*` of any size goes in, but not a branched one (`branches` variant `yes`
or `branchCount` above 0) while Logging Expanded's `RequireBranchRemovalForProcessing` is on: the
player gets Logging Expanded's own message. By hand, right-click holding a trunk, or with an empty
hand to take the first one from the hotbar, then the backpack (as Logging Expanded's workstations
search). Neither works while the saws are still rising after a cut (the player is told so). From
a rack: about once a second an assembled, empty, turning mill with its saws up looks for a Trunk
Storage Rack touching its infeed side at ground level (either of the rack's cells), and takes its
top trunk (the rack is last in, first out) unless that trunk is branched; then it waits. The rack
is looked up afresh every time.

**Cutting.** Progress runs from 0 to 1 per trunk, advanced by how far the shaft turned (its angle,
as Immersive Woodworking's sawmill does, not the time). A trunk takes `RevolutionsPerStoredLog`
turns per log stored in it. When it is through, the mill drops
floor(stored logs × `LogsPerStoredLog`) of the log Logging Expanded makes from that wood
(`TreeManager.GetPlacedLogCode`) at the rig's output point, and each blade kit loses
ceil(stored logs × `BladeWearPerStoredLog`) durability. A kit worn to 0 breaks with the tool-break
sound, and the mill stops until a new one is fitted. The trunk can be taken back only while less
than a quarter is cut. Its resin and branch data are lost, as on Logging Expanded's sawhorses.
The server does all of it; the arithmetic is in `Core/Cutting.cs`.

**Saws.** Each blade set lies across the trunk, strokes back and forth, and sinks through it under
its own weight. Their depth is one server value from 0 (latched at the top) to 1 (at the bed,
through the trunk), saved and synced with the block entity. Loading a trunk drops them at once to
where they touch it: (`topY` − trunk top) / (`topY` − `bottomY`), clamped to 0..1, with the trunk
top at the bed's `origin` y plus its thickness (2 blocks for sizes `xl` and `xxl`, otherwise 1)
and `topY` and `bottomY` from the rig's `saw` (3.0 and 0.5 when it has none). While cutting the
depth is touch + (1 − touch) × progress. When the cut is through, or a trunk is taken back out,
the saws are left where they are and the mill raises them: the bottomed-out saws trip the levers,
which engage a windlass that winds them up on ropes to the latch. The depth falls by
1/`RaiseRevolutions` per shaft revolution, measured from the shaft's angle like the cut, while the
mill is assembled and turning at `MinSpeed` or faster; a missing or worn out blade kit, or lost
power, pauses it. At 0 the mill is idle and takes the next trunk. The phase (`MillPhase`) is
derived: a trunk means Cutting, else a depth above 0 means Raising, else Idle. The arithmetic is in
`Core/SawDepth.cs`.

**Logging Expanded** is read through `LoggingBridge.cs`, by reflection: the rack's block entity
(`LoggingMod.BETrunkStorage`: `GetStoredTrunks`, `PopTrunk`), `LoggingMod.TreeManager.Instance`
(`GetPlacedLogCode`) and `LoggingMod.LoggingConfig.Current` (`RequireBranchRemovalForProcessing`).
The log count and wood come from the trunk stack's own attributes (`slots` → `"0"`, a stack of the
log block whose size is the count). If a member is missing the mod logs one warning and the mill
takes and cuts no trunks.

**Rendering.** The controller draws the static frame through its JSON shape,
`shapes/block/buckingmill_frame.json`. The block entity implements `IMillVisualState` (facing,
fitted parts, blade metal, trunk, phase, client-side progress and saw depth, shaft angle and
speed), which `MillRenderer` polls to draw the moving parts and the trunk (see [Model](#model)).

## Config

`ModConfig/buckingsawmill.json`. Values out of range fall back to the default with a warning.

| Setting | Default | |
|---|---|---|
| `Resistance` | 0.17 | Load of the assembled mill on its shaft (twice Immersive Woodworking's sawmill) |
| `MinSpeed` | 0.05 | Shaft speed below which it neither cuts nor pulls from a rack |
| `RevolutionsPerStoredLog` | 8 | Shaft turns per log stored in a trunk |
| `RaiseRevolutions` | 6 | Shaft turns to wind the saws from the bed back up to the latch after a cut |
| `LogsPerStoredLog` | 2.0 | Logs out per log stored, rounded down over the trunk |
| `BladeWearPerStoredLog` | 0.25 | Durability each blade kit loses per log stored, rounded up over the trunk |
| `AutoPullFromRack` | true | Whether it takes trunks from a rack at its infeed side |

## Crafting

The frame: two Immersive Woodworking sawmill frames and four support beams, with a hammer
(`assets/buckingsawmill/recipes/grid/buckingmill.json`). The parts are Immersive Woodworking's
own.

## Model

The machine's model is an edited copy of the sawmill from Immersive Woodworking by Bobrik00, shipped with his permission (`CREDITS.md`). It is generated, not drawn: `tools/make_shape.py` reads IW's `shapes/block/sawmill/sawmill.json` from `build/mods/immersivewoodworking_*.zip` and writes three files:

| File | What it holds |
|---|---|
| `assets/buckingsawmill/shapes/block/buckingmill.json` | The whole machine, including every moving part. The block entity splits it into parts by element name. |
| `assets/buckingsawmill/shapes/block/buckingmill_frame.json` | The static frame only. The block draws it and the inventory shows it. |
| `assets/buckingsawmill/config/rig.json` | Footprint, anchors and the part rig: the contract between the model and the code. |

### How the machine works

The mill is a pair of **drag saws with a windlass lift**. All positions are in the native, south-facing frame: x is width (west to east), y is up, z is depth (north to south), and the controller cell is at the origin.

The trunk lies along x on a fixed bed at y 0.5, centred at z 1.75. The north side holds the saw mechanism, and the rack stands there. Logs leave by the south side. Power comes in at the west end, on the shared shaft along x at y 3.5, z 1.5.

There are two saw stations, at x = 2 and x = 4. Each one has the same parts:

- **Posts and carriage.** Two IW posts stand on the north side of the trunk with their slots facing each other. The carriage slides up and down in the slots. It is IW's sash, squeezed into a short frame whose opening the blades pass through.
- **Saw head.** The head hangs from two guide bars that run north from the carriage, and slides along z on them.
  - The head is IW's sash rail turned on its side, with its clamp bars gripping the blades.
  - The blades are IW's set turned to cross-cut: their faces point along x, their length runs along z, and the teeth point down.
  - They are clamped at the north end only and reach south across the whole trunk. The free tip runs in its own kerf, as on a real drag saw.
- **Stroke.** IW's crank on the shared shaft drives a connecting rod, which pushes a **yoke** back and forth along z.
  - The yoke is a slotted crosshead: a tall bar on the north face, sliding in fixed guides at its top and bottom.
  - A pin on the saw head rides in the yoke's vertical slot. At any height of the carriage, the yoke carries the saw through its stroke.
  - The chain is shaft → crank → rod → yoke → pin → head → blades.
  - Station 2's crank runs half a turn behind station 1's.
- **Lift.** A rope hangs from a drum on the drum shaft, straight down to the carriage's top rail. Paying the rope out lowers the carriage; winding it in raises it. The drum is a plain spool: an octagonal core of wound rope between two octagonal oak flanges.

The drum shaft runs along x above the carriages, at y 3.5, z 0.77. It is turned by IW's gear set at the west end. The gear set is a reversing gear, laid out as IW meshes it:

- **Two loose pinions** sit on the main shaft. A sliding **clutch sleeve**, keyed to the shaft, sits between them. Shifting it west locks the west pinion to the shaft, shifting it east locks the east one; in the middle, neither pinion is driven.
- **Both pinions mesh the peg ring of IW's crown disc** (axis z, north of the shaft), on opposite sides of it, so they always turn opposite ways.
- **The disc's axle** runs north to IW's small crown gear, turned half round, which meshes a pinion on the drum shaft. A disc turning about z can't drive a shaft along x without this one right-angle pair.
- **The result:** because the pinions turn opposite ways, one of them always winds the drums in, whichever way the shaft turns. The shifter puts the sleeve on that one: the west pinion when the shaft turns forwards (θ increasing), the east one when it turns backwards. With the sleeve in the middle the drums run free.

No toothed wheel on the machine meshes nothing. The cranks are plain bars with a pin, IW's toothed main-rotor flanges are left off, and the drums are plain spools. The toothed or pegged wheels are:

| Wheel | Meshes with |
|---|---|
| West pinion | The crown disc's peg ring (west side); the sleeve when the shaft turns forwards |
| East pinion | The crown disc's peg ring (east side); the sleeve when the shaft turns backwards |
| Crown disc | Both pinions; the latch pawl drops between its pegs |
| Small crown gear (on the disc's axle) | The drum pinion |
| Drum pinion (on the drum shaft) | The small crown gear |

**Levers** (the "levers" part):

- **Trip.** At the bottom of a cut, a lug on station 1's carriage pushes down the tappet of a **trip rod** that runs down station 1's west post.
- **Shifter.** The trip rod turns a **bell crank**, whose pin frees the **link** through a slot in its end (lost motion), and the link turns a **rock shaft** along z. The rock shaft's fork sits in the sleeve's groove and throws the sleeve onto the winding pinion, which starts the lift. Which way it throws follows the shaft's direction (the rig's *D* input); the slot lets the link go either way while the bell crank, pushed down by the carriage, always turns the same way.
- **Holding the clutch.** The sleeve stays engaged for the whole lift; the rig shows this with its `lifting` input.
- **Latch.** A **pawl** drops between the top two pegs of the crown disc, which doubles as the windlass's ratchet wheel. While winding it clicks over them, and at the top it holds the windlass, and so both carriages, up.
- **Release.** When the next cut starts, the pawl swings clear and the carriages sink under their own weight while the saws stroke.

One cut, in the terms the gameplay uses:

1. Depth 0 means latched at the top, with the blades' cutting edge at `saw.topY` (2.5625, just above a 2-block trunk).
2. The latch lets go and the saws sink while stroking. Depth rises to 1, where the cutting edge is at `saw.bottomY` (0.4375, below the bed top), through the gaps in the bed rails.
3. Near the bottom (from depth 0.941, the last 2 voxels of the drop) the carriage pushes the trip down and the clutch engages.
4. The shaft winds the carriages back up while depth falls from 1 to 0 (`lifting` = 1). The latch catches at the top and the clutch is released.

The pinions turn 2.38 times per turn of the disc (peg rings of 6.05 and 2.54 voxels), and the second crown gear and drum pinion are 1:1. The drum's rope radius (2.15 voxels) is chosen so that a full raise takes 6 shaft turns, the gameplay's `RaiseRevolutions` default: the drum turns 15.83 rad over a full sink or raise. With that default the engaged pinion turns exactly with the shaft during a raise, either way round. With another `RaiseRevolutions` the gameplay raises the saws at a different rate from the gears, and the engaged pinion visibly slips against the shaft.

### Regenerating

```sh
python3 tools/packtool.py fetch                                    # puts IW's zip in build/mods
python3 mods-src/buckingsawmill/tools/make_shape.py                # stdlib only; rewrites the three files
python3 mods-src/buckingsawmill/tools/make_shape.py --out DIR      # or writes them into DIR instead
```

It also writes `tests/rig-reference.json`: every part's matrix at a grid of poses, from the script's reference maths. The unit tests check `Core/RigAnimation.cs` against it, so the C# and the Python cannot drift apart.

The output is deterministic. On every run the script also checks its own output, and exits non-zero if any check fails:

- **Parts and cells:**
  - every element lands in the rig part it was built for, and the count per part is printed;
  - nothing leaves the declared cells at rest, or the machine box anywhere in the motion.
- **Clearances** (sampled over 8 shaft angles × 5 depths × cutting and lifting, the shaft turning either way):
  - no trunk size, centred on the bed, touches any part but the blades;
  - the blades clear a 2×2 trunk while latched, and the bed rails at depth 1;
  - the moving parts don't run into each other or the frame (pin joints excepted).
- **Linkages:**
  - each rod's ends stay on the yoke pin and the crank pin, within 0.5 voxel;
  - the head's pin stays on the yoke's slot centre line and within its length at every depth;
  - each rope's ends meet its drum and its carriage's top rail at every depth;
  - each lever joint stays made over the trip's whole throw, the shaft turning either way, and the bell crank's pin stays in the link's slot;
  - the carriage's lug meets the tappet exactly when the trip starts to move;
  - the shifter puts the sleeve on the west pinion's face when the shaft turns forwards and on the east one's when it turns backwards;
  - in a raise the engaged pinion turns exactly with the shaft (both ways) and the drum winds the ropes in.
- **Files:** every texture used is declared, and every file parses.

Rerun it when IW's shape changes. Placement numbers are named constants at the top of the script. The script also holds the reference implementation of the driver maths (`driver_matrix`, `part_matrix`), which the renderer and the browser viewer must match.

### How it derives from IW's sawmill

**Built from IW's elements:**

| Part | Source in IW's model | What was done to it |
|---|---|---|
| Posts | `Frame` posts, slats, caps, feet and sills | Unturned, the opening squeezed from 16 to 15 voxels and the depth from 6 to 3. Four per station: the north pair guides the carriage; the south pair is the same turned half round, without the sills, and only carries the top beams. |
| Carriage | `sash*`, without its crosshead bars and lever bracket | Unturned, squeezed to 12.5 voxels tall, so its rails sit below and above the blades. |
| Saw head | The sash's bottom rail, clamp bars and rivets (`sash_001`–`021`) | Turned on its side: rail length → up, rail height → z, rail depth → x. |
| Blades | `saw*` | Turned 90° about x, so they cut across the trunk with the teeth down. Stretched to 39 voxels long, so the teeth are spaced out, and packed 0.8 voxels apart. |
| Cranks | `Rotor_default_4_001`–`005` | Throw shortened from 3.5 to 3 voxels; IW's flange disc left off. |
| Gear set | The two pinions `Rotor_default_1` and `_2`; the crown disc, axle and small crown gear of `Rotor_default_3` | Each pinion stands against the disc's peg ring as IW stands its pinions against its small crown gear. The small crown gear is turned half round at the north end of the axle, and the drum pinion is a copy of IW's east pinion on the drum shaft. |
| Rope | `spring_002` (rope texture) | Used for the ropes and the rope wound on the drums. |

**New elements**, made from IW elements' faces with their UVs cropped to length:
- the yokes and their guides;
- the connecting rods (from IW's beam);
- the carriage guide bars and the trip lug;
- the drums (plain octagonal spools: four rope-textured strips for the core and four oak strips per flange);
- the clutch sleeve (from the main rotor's plate);
- the levers (trip rod, tappet, bell crank, link, rock shaft and fork, pawl), from IW's sash stile and lever plate;
- the top beams, shaft bearings, input post, west post and head beam, and the pawl bracket;
- the bed: rails with gaps at the blade planes, sleepers and legs.

**Dropped from IW's model:**
- the log carriage, the log, its springs and rope, and the bed table;
- IW's main rotor (`MainRotor_twoway`, a shaft section with toothed flanges that would mesh nothing);
- IW's lever train proper (`leveler_*`, `connector*`, the `rotor_metal*` ratchet wheel), because the crown disc's pegs serve as the ratchet;
- IW's crank rod and crosshead (rebuilt), and the crank's flange disc.

Texture codes are IW's: `oak`, `metal` and `rope` (`game:item/resource/rope`). The frame shape uses only `oak` and `metal`. The blades use `metal`, which the renderer swaps for the blade's metal. The head's clamp bars stay iron.

### Rig schema (`rig.json`)

Everything is in the native frame, in block units, with the controller at `[0,0,0]`.

| Field | Meaning |
|---|---|
| `cells` | Every cell the machine occupies, each with `pos` and `boxes`. `boxes` holds up to three collision/selection cuboids in cell-local 0..1 coordinates, derived from the elements in that cell with the saws latched (depth 0, θ 0). Cells that hold nothing are omitted, so players can walk there. An empty or missing `boxes` means a full cube. |
| `powerCell`, `powerFace` | The cell that takes the axle, and the native-frame face it connects on. |
| `infeedSide`, `outputSide` | Native-frame sides. The rack stands on the infeed side; logs drop off the output side. |
| `output.pos` | Where cut logs spawn: just outside the output side, at bed height. |
| `saw` | `topY` and `bottomY`: the height of the blades' cutting edge when latched (depth 0) and at the end of a cut (depth 1). The gameplay works out from them where the saw first touches a trunk of a given thickness: at depth (`topY` − (bed top + thickness)) / (`topY` − `bottomY`). |
| `trunkBed` | `origin` is the centre of the bed's top surface. A trunk is drawn centred on it, lying along `axis`. `length` is the usable bed length. |
| `parts` | An ordered list of moving parts (below). |

How `parts` works:
- **Matching.** Each part's `match` is a list of case-sensitive `*` globs over an element's name. The first part with a matching glob owns the element. The last part, `frame` with `*`, is the static frame.
- **`requires`** names the fitted item the part needs before it is drawn: `crankshaft`, `levers`, `sash1`, `sash2`, `blade1` or `blade2`, or null for always (the parser rejects anything else). In the shipped rig:
  - `crankshaft`: the shaft, cranks, rods, yokes, gear set, drum shaft and drums;
  - `sash1` and `sash2`: each station's carriage, saw head and rope;
  - `blade1` and `blade2`: each station's blades;
  - `levers`: the trip, bell crank, link, rock shaft and pawl.
- **Composition.** `drivers` apply in list order to the authored geometry, with pivots in the authored frame: M = Dₙ ⋯ D₂ · D₁. If the part has a `ride`, that part's whole matrix is then applied on top: M = M_ride · M. Parents are evaluated first whatever the file order, and cycles are rejected.
- **Inputs:**
  - θ is the signed shaft angle in radians.
  - *d* is the saw's **depth** in [0, 1]: 0 is latched at the top, 1 is at the bed, through the trunk.
  - *L* is **lifting**: 1 while the saws are being wound back up, otherwise 0. A renderer may ease it over a fraction of a second.
  - *D* is the shaft's **direction**: +1 while it turns forwards (θ increasing), −1 backwards. A renderer may ease it between.
- **Units and conventions.** Distances are blocks and angles radians. Rotations are right-handed about the positive axis. `axis` is `x`, `y` or `z`.

| Driver | Parameters | Motion |
|---|---|---|
| `rotate` | `axis`, `pivot`, `ratio` (default 1) | Rotation by `ratio`·θ about `pivot`. |
| `slide` | `axis`, `amplitude`, `ratio` (1), `phase` (0) | Translation along `axis` by `amplitude`·sin(`ratio`·θ + `phase`). |
| `swing` | `axis`, `pivot`, `amplitude`, `ratio` (1), `phase` (0) | Rotation by `amplitude`·sin(`ratio`·θ + `phase`) about `pivot`. |
| `feed` | `axis`, `travel` | Translation along `axis` by `travel`·*d*. |
| `step` | `motion` (`slide` or `rotate`), `axis`, `pivot` (rotate only), `amount`, `from` (0), `to` (1), `lifting` (absent, `hold` or `block`), `reversible` (false) | Let e = clamp((*d* − `from`) / (`to` − `from`), 0, 1). Then `lifting: hold` makes e = max(e, *L*), and `lifting: block` makes e = e·(1 − *L*). With `reversible: true`, e is then multiplied by *D*. `slide` translates along `axis` by `amount`·e (blocks); `rotate` turns by `amount`·e (radians) about `pivot`. |
| `stretch` | `axis`, `anchor`, `length`, `travel` | Scales along `axis` about the plane through `anchor` normal to it, by f = (`length` + `travel`·*d*) / `length`. A coordinate *a* on that axis goes to `anchor` + f·(*a* − `anchor`); the other axes are unchanged. `length` is the signed distance from the anchor to the free end in the authored model, and `travel` is the free end's signed displacement at *d* = 1. The matrix is the identity, except that the axis' diagonal entry is f and its translation is `anchor`·(1 − f). |

How the shipped rig uses them:

| Part | Drivers |
|---|---|
| `shaft` | `rotate` x, ratio 1, about the shaft axis. Covers both cranks and IW's main rotor. |
| `clutch` | `rotate` with the shaft, then a held, reversible `step` `slide` x of −1.52/16 over depth 0.941..1: west forwards, east backwards. |
| `pinion_w`, `pinion_e` | `step` `rotate` x of ∓37.70 rad (6 turns) over the whole cut. The disc drives them in opposite directions. |
| `crown` | `step` `rotate` z of +15.83 rad. |
| `drum` | `step` `rotate` x of −15.83 rad (= −`SINK`/`DRUM_R`). The rope pays out as the carriage sinks. |
| `f<n>_yoke` | `slide` z: the first harmonic of the exact slider-crank, amplitude 0.2022, with station 2 half a turn behind. |
| `f<n>_rod` | `swing` x about the yoke pin (first harmonic of the exact rod angle, ±8.9°), riding the yoke. |
| `f<n>_carriage` | `feed` y by −`SINK` (−2.125). |
| `f<n>_saw` | The yoke's `slide`, riding the carriage. |
| `f<n>_blade` | Rides the saw head. |
| `f<n>_rope` | `stretch` y about the drum's underside: from 5.35 voxels long at depth 0 to 39.35 at depth 1. |
| `trip`, `bell`, `link`, `rock` | Held `step`s over depth 0.941..1, sized from the lever geometry so the joints stay made. `link` and `rock` are reversible. |
| `latch` | `step` `rotate` y of 0.3 rad over depth 0..0.005, blocked while lifting, so it clicks over the pegs during the lift. |

The model is posed for θ = 0, depth 0 and not lifting. The exceptions are the yokes, saw heads and rods, which are authored at mid-stroke and at the rod's mean angle; their drivers put them where the cranks are.

### Rendering

`MillRenderer` (client only, made and disposed by `BEBuckingMill`) draws everything that moves. The static frame comes from the block's own JSON shape, `buckingmill_frame.json`, which holds the same elements as the `frame` part. The renderer skips that part, so the frame is never drawn twice.

The renderer:
- **Builds one mesh per moving rig part** from `buckingmill.json`, by blanking every other part's elements, as Immersive Woodworking's sawmill renderer does.
- **Draws each part** with its rig matrix (`RigParts.Matrices(θ, depth, lifting, direction)` in `Core/RigAnimation.cs`), turned to the mill's facing.
  - A part whose `requires` is not fitted is skipped; the levers part is drawn when `HasLevers`.
  - It registers for the opaque pass and both shadow passes, and draws nothing beyond 64 blocks.
  - The rope's matrix scales along one axis, so it is not rigid.
- **Blade texture:** the blade meshes take the fitted kit's metal (`game:block/metal/ingot/{metal}`) in place of `metal`. The head's clamps keep iron.
- **The loaded trunk** is Logging Expanded's own block for that stack, so every size and wood looks right. It is laid on the bed from its tessellated bounds: turned onto x, centred on `trunkBed.origin`, its underside on the bed. It stays whole until the cut finishes and the gameplay clears it.
- **Shaft angle:** taken from the power ghost's `AngleRad`, which the network already advances smoothly on the client, and accumulated into a continuous angle.
  - The native shaft angle is `-AngleRad` facing south or west and `+AngleRad` facing north or east (`MillMotion.NativeShaftAngle`).
  - That makes the shaft turn with a vanilla axle on the power face, which draws itself as a right-handed turn of `-AngleRad` about the world axis.
- **Depth, lifting and direction:** depth is the block entity's `ClientSawDepth`. Lifting eases towards 1 while `Phase` is `Raising` and back to 0 otherwise (6 per second). Direction eases towards the sign of the shaft's last turn, so the clutch sleeve crosses to the other pinion if the network reverses.
- **Sound and particles** (IW's sounds are referenced, not copied):
  - In `MillPhase.Cutting` it plays one of Immersive Woodworking's stroke sounds (`immersivewoodworking:sounds/saw/sawing_*`) per half turn, in IW's speed bands, and puffs sawdust along each blade set's cutting edge.
  - In `Raising` it plays IW's `sounds/saw/metal_click`, quietly, each time the latch passes one of the crown disc's 12 pegs.

### Editing by hand

If you edit the shapes in VS Model Creator, keep the element names: the rig finds its parts through them.

| Prefix | Part |
|---|---|
| `f<n>_post_n_`, `f<n>_post_s_`, `f<n>_guide_` | Station *n*'s posts and yoke guides (static frame). |
| `f<n>_carriage_` | Station *n*'s carriage. |
| `f<n>_saw_` | Station *n*'s saw head. |
| `f<n>_blade_` | Station *n*'s blades. |
| `f<n>_yoke_` | Station *n*'s yoke. |
| `f<n>_rod_` | Station *n*'s rod. |
| `f<n>_crank_` | Station *n*'s crank. |
| `f<n>_rope` | Station *n*'s rope. |
| `shaft_` | The shared shaft. |
| `gear_clutch_` | The clutch sleeve. |
| `gear_pinion_w_`, `gear_pinion_e_` | The two loose pinions. |
| `gear_crown_` | The crown disc, its axle and the small crown gear. |
| `drum` | The drum shaft, drums and drum pinion. |
| `lever_trip_`, `lever_bell_`, `lever_link`, `lever_rock_`, `lever_latch_` | The levers. |

Everything else is the static frame.

Hand edits are lost when the script runs again. Either port them into `make_shape.py`, or stop regenerating.

The model is derived from Immersive Woodworking's sawmill model by Bobrik00 and is used with permission. It is not covered by the repository's license; see `CREDITS.md`.

## Tests

- `tests/` (`dotnet test mods-src/buckingsawmill/tests`, no game needed) compiles `Core/` and tests
  the rig parser (with its own fixture, and the shipped `rig.json`), the rotation maths, the
  assembly rules, the cut arithmetic, the saw depth and raise arithmetic and the config. It also tests the rig's animation: glob
  matching, each driver (including the step gates and the stretch), ride composition and cycles,
  the shaft's direction against a vanilla axle on every facing, every shipped part's matrix
  against `tests/rig-reference.json` (written by `tools/make_shape.py` from its reference maths,
  with the shaft turning both ways), the shipped carriages falling from `saw.topY` to
  `saw.bottomY`, and, for each shaft direction, the clutch engaging the pinion that turns with
  the shaft while the drum winds the ropes in.
- `tools/make_shape.py` checks its own output every time it regenerates the model.
- `tests/PackTests/BuckingSawmillScenarios.cs` (Atlas) loads this build with every locked mod: the
  mod loads cleanly with its recipe, the bridge resolves against the pinned Logging Expanded,
  placing stamps the ghosts in every facing and breaking removes them and drops the parts,
  assembly follows the rules (levers included), racks feed debranched trunks only (also through a
  rack's filler cell), a full cut on a real mechanical network (a creative rotor) gives the
  configured logs and wears the blades, the rack's next trunk waits until the saws have risen,
  taking a trunk out early raises the saws from where they were, and the depth survives saving. Its
  ModConfig is seeded from `tests/PackTests/fixtures/buckingsawmill`, which shortens the cut and
  the raise.

The test project loads this directory's build as a mod and leaves out a pinned copy from the
ModDB (`buckingsawmill_*.zip` in `build/mods`).

## Releasing

The same as `mods-src/allowedvariantsfix/README.md`: bump the version in `modinfo.json` and
`BuckingSawmill.csproj`, merge, tag `buckingsawmill-v<version>` on main, upload the zip from the
GitHub Release to the ModDB (keep the file name), then pin it in `pack/pack.toml` (the first
release adds the entry, `side = "universal"`) and run `packtool lock`.

For a local build: `dotnet build mods-src/buckingsawmill -c Release` (needs `VINTAGE_STORY`) writes
`build/buckingsawmill_<version>.zip`: the DLL, `modinfo.json` and `CREDITS.md` at the top level, and
`assets/`.
