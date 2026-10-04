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
boxes, the power cell and the face that takes the axle, the infeed end a rack must touch, the
point and side logs leave by, the saws' travel (`saw`, optional), and keys only the renderer
reads. Everything there is in the native,
south-facing frame with the controller at `[0,0,0]`. Turning it to a facing follows Immersive
Woodworking: with n the normal of the `side` variant, local (x, y, z) goes to
(x·nz + z·nx, y, −x·nx + z·nz), and the shape turns by rotateY north 180, east 90, south 0,
west 270. `Core/Rig.cs` parses the file and `Core/Footprint.cs` does the turning.

**Assembly.** Right-click the frame or any ghost holding a part, recognised by code path as
Immersive Woodworking does: two `sawmillsash`, one `sawmillcrankshaft`, one `sawmilllevers` and two
`sawmillblade-{metal}` kits of one metal. Any order, but each blade kit needs a sash without a kit.
The levers are the linkage that throws the windlass in when the saws reach the bed and out again
when they are back at the top (see **Saws** below). No carriage: in Immersive Woodworking it carries the log, which this
mill does not do, and holding one does nothing special here. Parts are used up outside creative
mode. Ctrl + right-click takes the trunk back if
there is one, otherwise the last blade kit. The other parts come back only by breaking the frame,
which drops the frame, every fitted part, the blade kits and a recoverable trunk. The rules are in
`Core/Parts.cs`.

**Power.** `BEBehaviorMillMP`, a mechanical power consumer on the power ghost, connects only
through the rig's power face, turned to the mill's facing (like Immersive Woodworking's
`BEBehaviorSawmillMP`, without its cap on the network's speed). It loads the shaft with 0.005 until
the mill is assembled, then with `Resistance`. The mill runs its saws' cycle, cuts, and pulls from a
rack only while it is assembled and the shaft turns at `MinSpeed` or faster.

**Trunks.** One at a time, held as the trunk's whole item stack, so one taken back out is unchanged.
Any `loggingmod:treetrunk-*` of any size goes in, but not a branched one (`branches` variant `yes`
or `branchCount` above 0) while Logging Expanded's `RequireBranchRemovalForProcessing` is on: the
player gets Logging Expanded's own message. By hand, right-click holding a trunk, or with an empty
hand to take the first one from the hotbar, then the backpack (as Logging Expanded's workstations
search). A trunk goes on only while the saws are at the top of their cycle, within
`SawDepth.LoadWindow` (0.08 of the travel, about half a turn either side of the top at the
default rate); otherwise the player is told to wait for the saws to come up. From a rack: an
assembled, empty, turning mill looks for a Trunk Storage Rack touching its infeed end at ground
level (either of the rack's cells), every tick its saws are at (or pass) the top and once a second
besides. The infeed is the east end in the native frame, the end opposite the axle, where a trunk
slides in lengthwise along the bed through both stations. The mill takes the rack's top trunk (the rack is last in, first out) unless that trunk is branched; then it waits. The rack
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

**Saws.** Each saw's blade lies across the trunk, held at both ends, strokes back and forth, and sinks
through it under its own weight. Their depth is one server value from 0 (at the top) to 1 (at the
bed, through the trunk), with the direction they are going (`rising`); both are saved and synced
with the block entity. Nothing holds the saws at the top: while the mill is assembled and turning at
`MinSpeed` or faster it cycles without stopping, measured from the shaft's angle like the cut.

1. **Down.** Empty, the saws sink 1/`RaiseRevolutions` of the travel per shaft turn. With a trunk
   they drop at once to where they touch it, (`topY` − trunk top) / (`topY` − `bottomY`) clamped
   to 0..1, with the trunk top at the bed's `origin` y plus its thickness (2 blocks for sizes `xl`
   and `xxl`, otherwise 1) and `topY` and `bottomY` from the rig's `saw` (3.0 and 0.5 when it has
   none). Cutting, the depth is touch + (1 − touch) × progress.
2. **At the bed** (depth 1) the bottom trip throws the windlass in. If a cut has just finished, the
   logs drop, the blades wear and the trunk is gone; turning left over in that tick is dropped.
3. **Up.** The windlass winds the saws up at 1/`RaiseRevolutions` per turn.
4. **At the top** (depth 0) the top trip throws the windlass out and the saws start down again,
   onto the next trunk if one went on. A trunk loaded on the last of the rise waits for the top.

An empty cycle takes 2 × `RaiseRevolutions` turns (12 by default). A trunk taken back out early
(less than a quarter cut) leaves the saws where they are, and they carry on down at the empty
rate. A missing or worn out blade kit, or lost power, stops the cycle where it is; it carries on
from there. The phase (`MillPhase`) is derived: `Stopped` when not assembled or not turning fast
enough, otherwise `Raising` while going up, `Cutting` while going down with a trunk, and `Sinking`
while going down empty. Wear, the stroke sound and sawdust happen only while cutting. The
arithmetic is in `Core/SawDepth.cs` (`Advance`).

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
| `RaiseRevolutions` | 6 | Shaft turns for the saws' whole travel, each way, when not cutting: up from the bed, and down when empty. An empty cycle is twice this. |
| `LogsPerStoredLog` | 2.0 | Logs out per log stored, rounded down over the trunk |
| `BladeWearPerStoredLog` | 0.25 | Durability each blade kit loses per log stored, rounded up over the trunk |
| `AutoPullFromRack` | true | Whether it takes trunks from a rack at its infeed end (the end opposite the axle) |

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

The trunk lies along x on a fixed bed at y 0.5, centred at z 1.6875 (27 voxels), so a 2×2 trunk spans z 11..43 voxels. The north side holds the saw mechanism. Trunks come in at the east end, sliding lengthwise along the bed from a rack standing there, and logs leave by the south side. Power comes in at the west end, on the shared shaft along x at y 3.5, z 1.5.

**Crankshaft and frame.**
- **Main shaft.** A wooden shaft of IW's cross profile, matching the vanilla axle that feeds it, runs from the input bearing to station 1's west post. It carries the reversing gear.
- **Crankshaft.** From station 1's west post on, the shaft is the **crankshaft**: IW's crank bar, in iron, from crank 1 straight through to crank 2 with no change of section.
  - The bar between the cranks is the crank stub's own profile (`shaft_crankbar`).
  - It runs in a bearing block hanging from the top beam at every station post.
- **Drum shaft.** It runs in a bearing block at each station post, built into the post's cap under the top beam, and in a block hanging from the west head beam beside the drum pinion.
- **West end.**
  - The west post stands on the head beam's centre line, and the beam's end rests square and flush on the post's top, as the top beams sit on the station posts.
  - The input post carries the main shaft's bearing on its top.
  - The drum shaft's west bearing hangs from the west head beam.
  - The rock shaft runs in two bearing blocks: one on an arm from the input post, one on an arm from station 1's west post.
- **Nothing fixed floats.** The generator checks that every frame element shares a face with the rest of the frame, down to the ground, and that each bearing encloses its shaft.

There are two saw stations, at x = 2 and x = 4. Each one has the same parts:

- **Posts and carriage.** Each station stands on four solid 4×4 timbers, two north of the trunk (z 7..11) and two south of it (z 44..48), 15 voxels apart.
  - Each north post has a groove down its inner face, 1.7 wide and 1.6 deep, and the carriage runs between them in those grooves.
  - The carriage is a frame of two stiles and two rails, 2.9 deep, whose opening the blade passes through. Each stile carries a 1.5×1.5 tongue that runs in its post's groove with 0.1 to spare all round.
  - The top beams (4 wide, 3.5 deep) run north–south from post to post and sit square on them.
- **Saw head.** The head hangs from two guide bars that run north from the carriage, and slides along z on them.
  - The head is IW's sash rail turned on its side, with its clamp bars gripping the blade.
  - The blade is one of IW's three, turned to cross-cut: its face points along x, its length runs along z, and the teeth point down. It is centred on the station's cutting plane and thickened from 0.2 to 0.4 voxels so it reads at this scale.
  - The blade is clamped in the head at its north end and reaches south across the whole trunk to a **tail piece**, a metal stirrup clamped over its south end. The blade is held taut between the two.
- **Stroke.** The station's crank on the crankshaft drives a connecting rod (a wooden pitman with iron eyes), which pushes a **yoke** back and forth along z.
  - The yoke is a slotted crosshead: a tall bar on the north face, sliding in fixed guides at its top and bottom.
  - A pin on the saw head rides in the yoke's vertical slot. At any height of the carriage, the yoke carries the saw through its stroke.
  - The chain is shaft → crank → rod → yoke → pin → head → blade → tail.
  - Station 2's crank runs half a turn behind station 1's.
  - The stroke is ±1.85 voxels (crank throw 1.75). It is set by the room south of the trunk: the tail has to stay in its guide block through the whole stroke.
- **South-end support.** A 3×3 **guide post** stands on the south edge, just west of the blade plane (x −3.9..−0.9 from it, z 44.5..47.5).
  - It is squared into the frame like IW's own posts. It stands centred on a sill laid between the feet of the station's two south posts, and runs straight up into a head beam laid between the station's two top beams. Both are on the south posts' line (z 44..48), and there are no braces.
  - A **guide block** slides up and down the post: a sleeve round the post, and a web from it to a slot along z in the blade plane, from z 43.1 to 47.9. The tail piece strokes in that slot, so the blade is guided in its cutting plane at both ends.
  - The slot is closed underneath. Over it are two iron straps with a window between them, through which the tail shows.
- **The guide block's lift.** The block has its own rope, wound by the same windlass, so both ends of the saw rise and sink together.
  - **Spool:** a second plain spool on the drum shaft, just west of the drum (rope at x −4.3 from the blade plane). It has the drum's rope radius, so it pays out at the same rate.
  - **Run:** the rope leaves the spool's underside, the side that pays out as the drum turns to lower the saw. It runs south at y 53.85, under the crank stub and west of the crank's throw, high above the trunk's path.
  - **Sheave:** the run passes over a plain grooved sheave (radius 1.6) hung on the guide post's west face. The sheave turns on an iron pin from the post, and an iron strap outside it carries the pin's outer end, with an iron cap back to the post above the rope.
  - **Drop:** from the sheave the rope drops plumb onto an iron eye on top of the guide block's west sleeve wall. It sits in the sheave's groove on both the incoming and the outgoing side.
  - Over the stroke the tail runs from z 43.25 to 47.75: 0.15 voxels inside each end of the slot and 0.25 clear of a 2×2 trunk's south face (43). The block's lowest point, at depth 1, is 2.4 voxels above the sill.
- **Lift.** A rope hangs from a drum on the drum shaft, plumb down to an iron eye on the carriage's top rail. Paying the rope out lowers the carriage; winding it in raises it.
  - The drum is a plain spool: a wooden core wrapped in rope, between two octagonal oak flanges.
  - The rope leaves the wrap tangentially on the drum's north side, level with the drum shaft's axis, so its top end is on the drum's rope surface.
  - The guide block's spool (see above) is the same spool, smaller, and its rope leaves its underside.

The drum shaft runs along x above the carriages, at y 3.5, z 0.70 (11.25 voxels). It is turned by IW's gear set at the west end, through a dog clutch on the crown axle. Nothing in the drive needs to know which way the shaft turns, except two one-way catches.

**The gear set is a rectifier, always running.**
- **Two loose pinions** sit on the main shaft. Each drives through its own one-way catch, a small iron pawl on its outer hub face; the two catches are of opposite hands.
- **Both pinions mesh the peg ring of IW's crown disc** (axis z, north of the shaft), on opposite sides of it.
- **Whichever way the shaft turns,** one pinion is carried with it and the other idles the opposite way, driven by the disc. So the disc always turns the same way, the raising way, whenever the shaft turns.
- **The disc's axle** runs north to IW's small crown gear, turned half round. That gear meshes the **drum pinion**, which is fixed on the drum shaft.

**One on/off clutch for the lift.**
- The crown axle is in two halves:
  - the disc's half, which always turns;
  - the small crown gear's half, which ends in a **dog hub** and is geared to the drums.
- A **dog clutch** sleeve, keyed to the disc's half, sits between them with two iron dogs on its north face.
- Slid north onto the hub, it locks the two halves together, and the disc winds the drums.
- **Bearings:** each half has a bearing of its own, and an iron pilot spigot on the hub's face runs on into the disc's half, so the inner ends carry each other.
  - The small crown gear's half runs in a block hanging from the west head beam, north of the gear.
  - The disc's half runs in a block between the clutch and the disc, on a rail from a hanger under station 1's west top beam.
- **Collars:** the two loose pinions are kept in place on the main shaft by fixed collars either side of each (and the input bearing beyond the west one).
- Left out, the drum train is free, and the drums turn back as the ropes pay out under the saws' weight.

No toothed wheel on the machine meshes nothing. The cranks are plain bars with a pin, IW's toothed main-rotor flanges are left off, and the drums and spools are plain. The toothed or pegged wheels are:

| Wheel | Meshes with |
|---|---|
| West pinion | The crown disc's peg ring (west side); its catch on the shaft when the shaft turns forwards |
| East pinion | The crown disc's peg ring (east side); its catch on the shaft when the shaft turns backwards |
| Crown disc | Both pinions |
| Small crown gear (on its half of the crown axle) | The drum pinion |
| Drum pinion (on the drum shaft) | The small crown gear |

Every rotating part, and what drives it:

| Part | Driven by | Drives |
|---|---|---|
| Main shaft and crankshaft (`shaft`) | The axle at the power face | The rods through the crank pins; whichever pinion's catch bites |
| West and east pinions (`pinion_w`, `pinion_e`) | Their one-way catches on the shaft, or the disc, whichever way the shaft turns | The crown disc |
| Crown disc and its half of the axle (`crown`) | The pinions | The dog clutch |
| Dog clutch (`dog`) | Keyed to the disc's half (turns); the rock shaft's fork (slides) | The small crown gear's half, when slid onto its hub |
| Small crown gear, its half of the axle and the dog hub (`crown_b`) | The dog clutch (raising); the drum train (cutting) | The drum pinion |
| Drum shaft, drum pinion and drums (`drum`) | The ropes paying out (sinking); the small crown gear (raising) | The carriages' ropes |
| Guide-block spools (`f<n>_spool`) | Turn with the drum (same shaft, same radius) | The guide blocks' ropes |
| Sheaves (`f<n>_sheave`) | The guide-block rope running over them | — |
| Trip rod (`trip`) | The carriage's lug: pushed down at the bed by its tappet, lifted at the top by its collar | The rock shaft's tappet arm |
| Rock shaft (`rock`) | The trip rod; held either way by its over-centre weight | The dog clutch, through the fork |

**Levers** (the "levers" part): one rigid lever and a pushrod.
- **Rock shaft.** One iron shaft runs along x low on the north side of the gear set, between bearings on the input post and on station 1's west post. Fixed on it:
  - a **tappet arm** pointing north to the top of the trip rod;
  - a **fork arm** pointing up into the groove of the dog clutch on the crown axle;
  - an **over-centre weight** on a short upright arm, which stands exactly over the shaft half way through the throw.
- **Trip rod.** A pushrod up station 1's west post, with a **tappet** under station 1's carriage lug and a **collar** above it.

In plain terms: at the bottom the carriage's lug pushes the rod down by its tappet, and at the top it lifts the rod by its collar. The rod rocks the shaft, whose fork slides the dog clutch in (at the bottom) or out (at the top). The weight tips past upright as the shaft turns, so it holds the clutch whichever way it was last thrown.

The throw is 2 voxels at the rod, 10.1° at the shaft and 1 voxel at the clutch. The weight leans 0.32 voxels south when the clutch is out and 0.32 north when it is in.

One cycle, in the terms the gameplay uses:

1. Depth 0 is the top, with the blade's cutting edge at `saw.topY` (2.5625, just above a 2-block trunk). The clutch is out.
2. The saws sink while stroking, onto a trunk if one was loaded, and through it. Depth rises to 1, where the cutting edge is at `saw.bottomY` (0.4375, below the bed top), through the gaps in the bed rails. The drums turn back as the ropes pay out; the gear set keeps turning, and the dog clutch spins clear of its hub.
3. Over the last 2 voxels of the drop (from depth 0.941) the lug pushes the trip rod down, the rock shaft turns, and the dog clutch goes in.
4. The disc now winds the drums, and the carriages and guide blocks rise, while depth falls from 1 to 0 (`lifting` = 1). Over the last 2 voxels of the rise (depth 0.059 to 0) the lug lifts the collar, and the clutch comes out. The saws start down again at once.

**Gear ratios.**
- The pinions turn 2.38 times per turn of the disc (peg rings of 6.05 and 2.54 voxels), and the small crown gear and drum pinion are 1:1.
- The drum's rope radius (2.15 voxels) is chosen so that a full raise takes 6 shaft turns, the gameplay's `RaiseRevolutions` default: the drum turns 15.83 rad over a full sink or raise.
- With that default, the clutched halves of the crown axle turn exactly together, either way the shaft turns. With another `RaiseRevolutions` the gameplay raises the saws at a different rate, and the dog clutch visibly slips against its hub.

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
  - the blades clear a 2×2 trunk at the top, and the bed rails at depth 1;
  - over 72 shaft angles, every depth, cutting and lifting, and the shaft turning either way, each tail stays inside its guide block's slot (in length, height and width) and clear of a 2×2 trunk, each guide block stays on its post, and both stay inside the declared cells;
  - the guide block's rope leaves the spool's underside and meets the sheave, and its drop stays on the guide block, at every depth; the run, the drop and the sheave clear every other moving part and the frame over the whole motion (the sheave's pin excepted);
  - the moving parts don't run into each other or the frame (pin joints excepted), and the shafts, gears, drums and ropes clear the frame except where a shaft runs in its bearings;
  - every frame element shares a face with (or overlaps) another, and the frame is one piece from the ground up, so nothing fixed floats;
  - each bearing encloses the shaft it carries, at rest and turned 45°;
  - every rotating shaft is carried by the frame: at least two fixed bearings enclose its axis along its length (main shaft 5, drum shaft 5, rock shaft 2, each sheave's pin 2), or one bearing plus the pilot spigot (each half of the crown axle); each loose pinion has a collar or bearing against both faces. A part that fails is named;
- **Linkages:**
  - each rod's ends stay on the yoke pin and the crank pin, within 0.5 voxel;
  - the head's pin stays on the yoke's slot centre line and within its length at every depth;
  - every rope leaves its spool's rope wrap at the tangent point and ends on its eye, at every depth (the carriage's rope on the carriage's eye; the guide block's rope over the sheave's groove on both sides and on the guide block's eye); the measured gaps are printed, all 0.000;
  - the trip rod reaches at depth 1 from the tappet under the carriage's lug (lug and tappet meet at y 4.5) up to the tappet arm, and its top stays pinned to the arm's tip over the whole throw (0.18 voxels);
  - the fork stays in the dog clutch's groove at every depth, going down and up, and the clutch's throw puts its face on the dog hub's;
  - the counterweight passes over the rock shaft between out and in (it leans south out, north in, and stands upright half way);
  - both trips: going down, the carriage's lug meets the tappet exactly when the trip starts to move (depth 0.941); going up, it meets the collar exactly when the trip starts back (depth 0.059); and the lug never runs into either;
  - the lever never jumps: over every depth, going down and going up, the trip rod's top stays pinned to the tappet arm;
  - the rectifier: per radian of shaft travel the west pinion turns +1, the east −1, and the disc the same way either way; in a raise the small crown gear's half turns exactly as the disc does.
- **Files:** every texture used is declared, and every file parses.

Rerun it when IW's shape changes. Placement numbers are named constants at the top of the script. The script also holds the reference implementation of the driver maths (`driver_matrix`, `part_matrix`), which the renderer and the browser viewer must match.

### How it derives from IW's sawmill

**Built from IW's elements:**

| Part | Source in IW's model | What was done to it |
|---|---|---|
| Posts | `Frame.011`'s faces | Built afresh as solid 4×4 timbers from the ground to the top beams, four per station. The north pair (z 7..11) each have a groove down the inner face for the carriage's tongues; the south pair (z 44..48) are plain. IW's slatted, split posts are no longer used. |
| Carriage | `sash_022`'s faces (IW's sash stile) | Built afresh: two 2-wide stiles with tongues for the post grooves, two 2-tall rails, all 2.9 deep, 10.5 tall; iron guide bars for the saw head and an iron lug for the trip. IW's sash is no longer used. |
| Saw head | The sash's bottom rail, clamp bars and rivets (`sash_001`–`021`) | Turned on its side: rail length → up, rail height → z, rail depth → x. |
| Blade | `saw*`: the middle one of IW's three blades (`saw_016`–`030`, `047`, `050`, `055`–`057`) | Turned 90° about x, so it cuts across the trunk with the teeth down. Stretched to 40.7 voxels long, so the teeth are spaced out, and its plate and teeth thickened from 0.2 to 0.4. IW's other two blades are left off: they are its rip saw's, one per cut between planks. |
| Cranks | `Rotor_default_4_001`–`005` | Throw shortened from 3.5 to 1.75 voxels; IW's flange disc left off; retextured iron. The bar between the two cranks (`shaft_crankbar`) is the stub's profile carried on, so the main shaft's thick section stops at the first crank. |
| Gear set | The two pinions `Rotor_default_1` and `_2`; the crown disc, axle and small crown gear of `Rotor_default_3` | Each pinion stands against the disc's peg ring as IW stands its pinions against its small crown gear. The small crown gear is turned half round at the north end of the axle, and the drum pinion is a copy of IW's east pinion on the drum shaft. |
| Rope | `spring_002` (rope texture) | Used for the ropes and the rope wound on the drums. |

**New elements**, made from IW elements' faces with their UVs cropped to length:
- the yokes and their guides;
- the connecting rods (from IW's beam);
- the carriage guide bars and the trip lug;
- the blade's tail piece (IW's lever plate) and the guide block (sleeve, cheeks, straps and foot, from IW's sash stile);
- the guide post, its sill and head beam, and the sheave's pin, strap and cap;
- the guide block's spool (rope core and oak flanges, like the drums), the sheave (two oak flanges and a core, octagonal), and the guide block's rope;
- the drums (plain octagonal spools: four rope-textured strips for the core and four oak strips per flange);
- the dog clutch and dog hub (from the main rotor's plate) and the pinions' one-way catches;
- the levers (trip rod with its tappet and collar; the rock shaft with its tappet arm, weight arm and weight, fork arm and fork), from IW's lever plate;
- the rope wraps on the drums and spools, and the iron eyes the ropes are tied to on the carriages and guide blocks;
- the posts (solid 4×4, the north ones grooved), the top beams (4×3.5), the crankshaft and drum-shaft bearings at every station post, the input post and its bearing, the west post and head beam, the drum shaft's west bearing, and the rock shaft's two bearings;
- the bed: rails with gaps at the blade planes, sleepers and legs.

**Dropped from IW's model:**
- the log carriage, the log, its springs and rope, and the bed table;
- IW's main rotor (`MainRotor_twoway`, a shaft section with toothed flanges that would mesh nothing);
- IW's lever train proper (`leveler_*`, `connector*`, the `rotor_metal*` ratchet wheel: 145 elements);
- IW's crank rod and crosshead (rebuilt), and the crank's flange disc.

Texture codes are IW's: `oak`, `metal` and `rope` (`game:item/resource/rope`). The frame shape uses only `oak` and `metal`. The blades use `metal`, which the renderer swaps for the blade kit's metal; everything else that is `metal` stays the block's iron plate.

The rule:
- **Wood:** structure (posts, beams, bearings, bed), the main shaft and drum shaft (they continue the vanilla wooden axle), the drums, gears and pinions as IW has them, the carriages, saw heads, yoke bars, rod bars and the guide block's body.
- **Iron:** pins, wearing surfaces and thin linkage. That is the crankshaft and crank bar, the rod eyes, the saw-head pins, the blade's tail piece, the dog clutch, the pinions' catches, the whole lever linkage (trip rod with its tappet and collar, rock shaft with its arms, weight and fork), the crown axle's pilot spigot, the rope eyes, the guide block's straps, the trip straps and the sheave's pin, strap and cap.

Elements retextured iron get UVs in proportion to their faces (4 texels per voxel). Elements copied unchanged from IW keep IW's texturing.

### Rig schema (`rig.json`)

Everything is in the native frame, in block units, with the controller at `[0,0,0]`.

| Field | Meaning |
|---|---|
| `cells` | Every cell the machine occupies, each with `pos` and `boxes`. `boxes` holds up to three collision/selection cuboids in cell-local 0..1 coordinates, derived from the elements in that cell with the saws at the top (depth 0, θ 0). Cells that hold nothing are omitted, so players can walk there. An empty or missing `boxes` means a full cube. |
| `powerCell`, `powerFace` | The cell that takes the axle, and the native-frame face it connects on. |
| `infeedSide`, `outputSide` | Native-frame sides: `east` (the end opposite the axle, where the rack stands and trunks slide in lengthwise) and `south` (logs drop off it). |
| `output.pos` | Where cut logs spawn: just outside the output side, at bed height. |
| `saw` | `topY` and `bottomY`: the height of the blades' cutting edge at the top (depth 0) and at the end of a cut (depth 1). The gameplay works out from them where the saw first touches a trunk of a given thickness: at depth (`topY` − (bed top + thickness)) / (`topY` − `bottomY`). |
| `trunkBed` | `origin` is the centre of the bed's top surface. A trunk is drawn centred on it, lying along `axis`. `length` is the usable bed length. |
| `parts` | An ordered list of moving parts (below). |

How `parts` works:
- **Matching.** Each part's `match` is a list of case-sensitive `*` globs over an element's name. The first part with a matching glob owns the element. The last part, `frame` with `*`, is the static frame.
- **`requires`** names the fitted item the part needs before it is drawn: `crankshaft`, `levers`, `sash1`, `sash2`, `blade1` or `blade2`, or null for always (the parser rejects anything else). In the shipped rig:
  - `crankshaft`: the shaft, cranks, rods, yokes, gear set, drum shaft and drums;
  - `sash1` and `sash2`: each station's carriage, saw head (with the blade's tail piece), guide block, rope, and the guide block's spool, rope and sheave;
  - `blade1` and `blade2`: each station's blade;
  - `levers`: the trip rod and the rock shaft.
  - `crankshaft` also covers the dog clutch and both halves of the crown axle.
- **Composition.** `drivers` apply in list order to the authored geometry, with pivots in the authored frame: M = Dₙ ⋯ D₂ · D₁. If the part has a `ride`, that part's whole matrix is then applied on top: M = M_ride · M. Parents are evaluated first whatever the file order, and cycles are rejected.
- **Inputs:**
  - θ is the signed shaft angle in radians.
  - *d* is the saw's **depth** in [0, 1]: 0 is the top, 1 is at the bed, through the trunk.
  - *L* is **lifting**: 1 while the saws are going up, otherwise 0. The renderer switches it straight from the direction: the `trip` gate is continuous where the direction changes, so nothing jumps.
  - ψ is the shaft's **travel**: the total angle it has turned through either way, in radians, never decreasing. A renderer adds the size of each turn to it. When it is not given it is |θ|.
- **Units and conventions.** Distances are blocks and angles radians. Rotations are right-handed about the positive axis. `axis` is `x`, `y` or `z`.

| Driver | Parameters | Motion |
|---|---|---|
| `rotate` | `axis`, `pivot`, `ratio` (default 1), `rectified` (false) | Rotation by `ratio`·θ about `pivot`; with `rectified: true`, by `ratio`·ψ, so it turns the same way whichever way the shaft turns. |
| `slide` | `axis`, `amplitude`, `ratio` (1), `phase` (0) | Translation along `axis` by `amplitude`·sin(`ratio`·θ + `phase`). |
| `swing` | `axis`, `pivot`, `amplitude`, `ratio` (1), `phase` (0) | Rotation by `amplitude`·sin(`ratio`·θ + `phase`) about `pivot`. |
| `feed` | `axis`, `travel` | Translation along `axis` by `travel`·*d*. |
| `step` | `motion` (`slide` or `rotate`), `axis`, `pivot` (rotate only), `amount`, `from` (0), `to` (1), `lifting` (absent, `hold`, `block` or `trip`), `top` (`trip` only, above 0) | Let e = clamp((*d* − `from`) / (`to` − `from`), 0, 1). Then `lifting: hold` makes e = max(e, *L*), `lifting: block` makes e = e·(1 − *L*), and `lifting: trip` makes e = e·(1 − *L*) + clamp(*d* / `top`, 0, 1)·*L*: thrown in over `from`..`to` going down, held while going up, and thrown back out over `top`..0 at the end of the rise. `slide` translates along `axis` by `amount`·e (blocks); `rotate` turns by `amount`·e (radians) about `pivot`. |
| `stretch` | `axis`, `anchor`, `length`, `travel` | Scales along `axis` about the plane through `anchor` normal to it, by f = (`length` + `travel`·*d*) / `length`. A coordinate *a* on that axis goes to `anchor` + f·(*a* − `anchor`); the other axes are unchanged. `length` is the signed distance from the anchor to the free end in the authored model, and `travel` is the free end's signed displacement at *d* = 1. The matrix is the identity, except that the axis' diagonal entry is f and its translation is `anchor`·(1 − f). |

How the shipped rig uses them:

| Part | Drivers |
|---|---|
| `shaft` | `rotate` x, ratio 1, about the shaft axis. Covers the main shaft, both cranks and the crank bar between them. |
| `pinion_w`, `pinion_e` | Rectified `rotate` x, ratio +1 and −1: each turns with the shaft's travel, the west one with the shaft when it turns forwards, the east one when it turns backwards. |
| `crown` | Rectified `rotate` z, ratio −0.4198 (−1/2.38): the same way whichever way the shaft turns. |
| `crown_b` | `step` `rotate` z of +15.83 rad over the whole cut: geared to the drums. |
| `dog` | A `trip` `step` `slide` z of −1/16 over depth 0.941..1 (north, onto the dog hub), out again over 0.059..0 going up, then the disc's rectified `rotate`. |
| `drum` | `step` `rotate` x of −15.83 rad (= −`SINK`/`DRUM_R`). The rope pays out as the carriage sinks and winds in as it rises. Covers the drums, the drum shaft and the drum pinion. |
| `f<n>_yoke` | `slide` z: the first harmonic of the exact slider-crank, amplitude 0.1156 (1.85 voxels), with station 2 half a turn behind. |
| `f<n>_rod` | `swing` x about the yoke pin (first harmonic of the exact rod angle, ±5.1°), riding the yoke. |
| `f<n>_carriage` | `feed` y by −`SINK` (−2.125). |
| `f<n>_saw` | The yoke's `slide`, riding the carriage. Includes the blade's tail piece. |
| `f<n>_blade` | Rides the saw head. |
| `f<n>_slider` | No drivers; rides the carriage, so the guide block sinks and rises with the saw but does not stroke. |
| `f<n>_rope` | `stretch` y about the drum's axis height, where the rope leaves the drum: from 6.9 voxels long at depth 0 to 40.9 at depth 1. |
| `f<n>_spool` | The drum's driver: `step` `rotate` x of −15.83 rad about the drum shaft. |
| `f<n>_tailrun` | No drivers: the guide block rope's run from the spool to the sheave. A rope sliding along itself shows no motion. |
| `f<n>_taildrop` | `stretch` y about the sheave's centre height: from 7.55 voxels long at depth 0 to 41.55 at depth 1. |
| `f<n>_sheave` | `step` `rotate` x of +30.9 rad (`SINK` / sheave radius) about its pin, so its rim moves with the rope. |
| `trip` | A `trip` `step` `slide` y of −2/16 over depth 0.941..1, `top` 0.059. |
| `rock` | A `trip` `step` `rotate` x of −0.176 rad (10.1°) about the rock shaft over depth 0.941..1, `top` 0.059: the trip's travel at the tappet arm's tip. |

The model is posed for θ = 0, depth 0 and going down. The exceptions are the yokes, saw heads and rods, which are authored at mid-stroke and at the rod's mean angle; their drivers put them where the cranks are.

### Rendering

`MillRenderer` (client only, made and disposed by `BEBuckingMill`) draws everything that moves. The static frame comes from the block's own JSON shape, `buckingmill_frame.json`, which holds the same elements as the `frame` part. The renderer skips that part, so the frame is never drawn twice.

The renderer:
- **Builds one mesh per moving rig part** from `buckingmill.json`, by blanking every other part's elements, as Immersive Woodworking's sawmill renderer does.
- **Draws each part** with its rig matrix (`RigParts.Matrices(θ, depth, lifting, ψ)` in `Core/RigAnimation.cs`), turned to the mill's facing.
  - A part whose `requires` is not fitted is skipped; the levers part is drawn when `HasLevers`.
  - It registers for the opaque pass and both shadow passes, and draws nothing beyond 64 blocks.
  - The rope's matrix scales along one axis, so it is not rigid.
- **Blade texture:** the blade meshes take the fitted kit's metal (`game:block/metal/ingot/{metal}`) in place of `metal`. The head's clamps keep iron.
- **The loaded trunk** is Logging Expanded's own block for that stack, so every size and wood looks right. It is laid on the bed from its tessellated bounds: turned onto x, centred on `trunkBed.origin`, its underside on the bed. It stays whole until the cut finishes and the gameplay clears it.
- **Shaft angle:** taken from the power ghost's `AngleRad`, which the network already advances smoothly on the client, and accumulated into a continuous angle.
  - The native shaft angle is `-AngleRad` facing south or west and `+AngleRad` facing north or east (`MillMotion.NativeShaftAngle`).
  - That makes the shaft turn with a vanilla axle on the power face, which draws itself as a right-handed turn of `-AngleRad` about the world axis.
- **Depth, lifting and travel:** depth is the block entity's `ClientSawDepth`, and lifting is 1 while `ClientRising`, otherwise 0. The shaft's travel ψ adds up the size of every turn of the shaft, so the rectified gears turn the same way whichever way the network drives it.
- **Sound and particles** (IW's sounds are referenced, not copied):
  - Only in `MillPhase.Cutting` it plays one of Immersive Woodworking's stroke sounds (`immersivewoodworking:sounds/saw/sawing_*`) per half turn, in IW's speed bands, and puffs sawdust along each blade's cutting edge. Sinking and raising are silent.

### Editing by hand

If you edit the shapes in VS Model Creator, keep the element names: the rig finds its parts through them.

| Prefix | Part |
|---|---|
| `f<n>_post_n_`, `f<n>_post_s_`, `f<n>_guide_` | Station *n*'s posts and yoke guides (static frame). |
| `f<n>_carriage_` | Station *n*'s carriage. |
| `f<n>_saw_` | Station *n*'s saw head, its pin and the blade's tail piece (`f<n>_saw_tail`). |
| `f<n>_blade_` | Station *n*'s blade. |
| `f<n>_slider_` | Station *n*'s guide block. |
| `f<n>_tailpost` | Station *n*'s guide post, its sill, head beam, and the sheave's pin, strap and cap (static frame). |
| `f<n>_spool_`, `f<n>_tailrope_run`, `f<n>_tailrope_drop`, `f<n>_sheave_` | Station *n*'s guide-block spool, rope run, rope drop and sheave. |
| `f<n>_yoke_` | Station *n*'s yoke. |
| `f<n>_rod_` | Station *n*'s rod. |
| `f<n>_crank_` | Station *n*'s crank (iron). |
| `shaft_crankbar` | The crankshaft between the two cranks. |
| `f<n>_rope` | Station *n*'s rope. |
| `shaft_` | The shared shaft. |
| `dog_` | The dog clutch. |
| `gear_crown_b_`, `gear_crownb_` | The small crown gear, its half of the crown axle and the dog hub. |
| `drum_pinion_` | The drum pinion (part of `drum`). |
| `gear_pinion_w_catch`, `gear_pinion_e_catch` | The pinions' one-way catches. |
| `gear_pinion_w_`, `gear_pinion_e_` | The two loose pinions. |
| `gear_crown_` | The crown disc, its axle and the small crown gear. |
| `drum` | The drum shaft and drums (`drum<n>_core`, `_wrap`, `_flange_*`). |
| `bearing<n><w\|e>`, `bearing_drum*`, `input_bearing`, `rock_bearing_*` | The shaft bearings (static frame). |
| `west_post`, `west_head_beam`, `rock_bearing_w`, `rock_bearing_e` | The west end's frame and the rock shaft's bearings (static frame). |
| `f<n>_carriage_eye`, `f<n>_slider_eye` | The iron eyes the ropes are tied to. |
| `lever_trip_`, `lever_rock_` | The levers: the trip rod (with `lever_trip_collar`) and the rock shaft (with `lever_rock_weight`). |

Everything else is the static frame.

Hand edits are lost when the script runs again. Either port them into `make_shape.py`, or stop regenerating.

The model is derived from Immersive Woodworking's sawmill model by Bobrik00 and is used with permission. It is not covered by the repository's license; see `CREDITS.md`.

## Tests

- `tests/` (`dotnet test mods-src/buckingsawmill/tests`, no game needed) compiles `Core/` and tests
  the rig parser (with its own fixture, and the shipped `rig.json`), the rotation maths, the
  assembly rules, the cut arithmetic, the saws' cycle (empty down and up, turning at both ends in
  one step, the load window, a trunk loaded at the top and on the last of the rise, a finished cut
  starting the rise, a trunk taken out early, stopping and resuming) and the config. It also tests the rig's animation: glob
  matching, each driver (including the step gates, the `trip` gate agreeing with itself where the direction changes, and the stretch), ride composition and cycles,
  the shaft's direction against a vanilla axle on every facing, every shipped part's matrix
  against `tests/rig-reference.json` (written by `tools/make_shape.py` from its reference maths,
  with a spread of shaft travels), the shipped carriages falling from `saw.topY` to
  `saw.bottomY`, the guide blocks sinking with the saws without stroking, the rectifier (for each
  shaft direction the disc turns the same way, the pinion whose catch bites turns with the shaft,
  and in a raise the small crown gear's half turns as the disc does), and the rock shaft
  throwing the dog clutch onto the dog hub, holding it while the saws rise and throwing it out at the top.
- `tools/make_shape.py` checks its own output every time it regenerates the model.
- `tests/PackTests/BuckingSawmillScenarios.cs` (Atlas) loads this build with every locked mod: the
  mod loads cleanly with its recipe, the bridge resolves against the pinned Logging Expanded,
  placing stamps the ghosts in every facing and breaking removes them and drops the parts,
  assembly follows the rules (levers included), racks feed debranched trunks only (also through a
  rack's filler cell), a powered empty mill cycles down and up without stopping and takes a trunk
  by hand only at the top, a full cut on a real mechanical network (a creative rotor) gives the
  configured logs and wears the blades (and a broken kit stops the cycle until replaced), the
  rack's next trunk waits until the saws are at the top, a trunk taken out early lets the saws
  carry on down empty, and the depth and direction survive saving. Its ModConfig is seeded from
  `tests/PackTests/fixtures/buckingsawmill`, which shortens the cut and the cycle (an empty cycle
  is two turns).

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
