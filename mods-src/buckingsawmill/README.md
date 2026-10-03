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

### Regenerating

```sh
python3 tools/packtool.py fetch                        # puts IW's zip in build/mods
python3 mods-src/buckingsawmill/tools/make_shape.py    # stdlib only; rewrites the three files
```

The output is deterministic. On every run the script also checks its own output, and exits non-zero if any check fails:

- every element lands in the rig part it was built for;
- nothing leaves the declared cells, at rest or anywhere in the motion;
- no trunk size, centred on the bed, touches the frame or a sash over the full stroke;
- each rod's upper end stays on its crank pin;
- every texture used is declared.

Rerun it when IW's shape changes. Placement numbers are named constants at the top of the script.

### How it derives from IW's sawmill

All coordinates are in the native south-facing frame: x is width, y is up, z is depth, and the controller cell is at the origin.

**Built from IW's elements:**
- **Saw frames:** IW's posts, caps, feet and base sills, and its sash, are copied twice, one copy per frame.
  - The sash opening is widened from 16 to 38 voxels and the sash stretched from 20 to 38 voxels tall, so a 2×2 trunk fits with stroke clearance.
  - Each copy is turned 90° about y so the trunk passes through both openings, and placed at x = 2 and x = 4 blocks.
- **Blades:** each frame keeps IW's blade set in IW's orientation, which is the same as turning it with the frame and back again. Blade normals point along x, so they cross-cut.
  - The blades are stretched to the taller sash and packed into the sash's thickness.
  - They start at the north side of the opening and feed south across the trunk as the cut progresses.
- **Drive:** both cranks (IW's `Rotor_default_4`) sit on one shared shaft along x at y 3.5, z 1.5. That is the centre of the west face of the power cell `[0,3,1]`.
  - The crank throw is shortened from 3.5 to 2.5 voxels.
  - The second crank runs half a turn behind the first.
  - IW's gear set (main rotor, two pinions and the crown disc) sits once at the west end, where the axle comes in.

**New elements**, made from IW's beam elements with their UVs cropped to length:
- bed rails and sleepers;
- top beams with shaft bearings;
- the input bearing post;
- the disc's axle post.

**Dropped from IW's model:** the carriage, the log, the carriage springs and rope, the levers and ratchet train, the bed table, and the lever posts. All of these served IW's travelling carriage, which the mill does not have (and it takes no levers part).

Texture codes are IW's: `oak` and `metal`. Both are declared in the shapes. The blades use `metal`, which the renderer swaps for the blade's metal.

### Rig schema (`rig.json`)

Everything is in the native frame, in block units, with the controller at `[0,0,0]`.

| Field | Meaning |
|---|---|
| `cells` | Every cell the machine occupies, each with `pos` and `boxes`. `boxes` holds up to three collision/selection cuboids in cell-local 0..1 coordinates, derived from the elements in that cell. Cells that hold nothing are omitted, so players can walk there. An empty or missing `boxes` means a full cube. |
| `powerCell`, `powerFace` | The cell that takes the axle, and the native-frame face it connects on. |
| `infeedSide`, `outputSide` | Native-frame sides. The rack stands on the infeed side; logs drop off the output side. |
| `output.pos` | Where cut logs spawn: just outside the output side, at bed height. |
| `saw` | Optional. `topY` and `bottomY`: the height of the blades' cutting edge when latched at the top and at the end of a cut. The gameplay's saw depth runs between them; 3.0 and 0.5 when absent. |
| `trunkBed` | `origin` is the centre of the bed's top surface. A trunk is drawn centred on it, lying along `axis`. `length` is the usable bed length. |
| `parts` | An ordered list of moving parts (below). |

How `parts` works:
- Each part's `match` is a list of case-sensitive `*` globs over an element's name. The first part with a matching glob owns the element. The last part, `frame` with `*`, is the static frame.
- `requires` names the fitted item the part needs before it is drawn: `crankshaft`, `levers`, `sash1`, `sash2`, `blade1`, `blade2`, or null for always (the parser rejects anything else).
- `drivers` apply in list order to the authored geometry, with pivots in the authored frame. If the part has a `ride`, that part's whole transform is then applied on top.
- θ is the signed shaft angle, and rotations are right-handed about the positive axis.

| Driver | Motion |
|---|---|
| `rotate` | Angle = `ratio`·θ about `pivot`. |
| `slide` | Offset = `amplitude`·sin(`ratio`·θ + `phase`). |
| `swing` | Angle = `amplitude`·sin(`ratio`·θ + `phase`) about `pivot`. |
| `feed` | Offset = `travel`·cut progress (0..1). |

The model is posed for θ = 0 and progress = 0, except that the sashes are authored at mid-stroke and their slide puts them where the cranks are.

### Rendering

`MillRenderer` (client only, made and disposed by `BEBuckingMill`) draws everything that moves. The static frame comes from the block's own JSON shape, `buckingmill_frame.json`, which holds the same elements as the `frame` part. The renderer skips that part, so the frame is never drawn twice.

The renderer:
- **Builds one mesh per moving rig part** from `buckingmill.json`, by blanking every other part's elements, as Immersive Woodworking's sawmill renderer does.
- **Draws each part** with its rig matrix (`RigParts.Matrices` in `Core/RigAnimation.cs`), turned to the mill's facing.
  - A part whose `requires` is not fitted is skipped.
  - It registers for the opaque pass and both shadow passes, and draws nothing beyond 64 blocks.
- **Blade texture:** the blade meshes take the fitted kit's metal (`game:block/metal/ingot/{metal}`) in place of `metal`. The sash's clamps keep iron.
- **The loaded trunk** is Logging Expanded's own block for that stack, so every size and wood looks right. It is laid on the bed from its tessellated bounds: turned onto x, centred on `trunkBed.origin`, its underside on the bed. It stays whole until the cut finishes and the gameplay clears it.
- **Shaft angle:** taken from the power ghost's `AngleRad`, which the network already advances smoothly on the client, and accumulated into a continuous angle.
  - The native shaft angle is `-AngleRad` facing south or west and `+AngleRad` facing north or east (`MillMotion.NativeShaftAngle`).
  - That makes the shaft turn with a vanilla axle on the power face, which draws itself as a right-handed turn of `-AngleRad` about the world axis.
- **Sound and particles:** while cutting, it plays one of Immersive Woodworking's stroke sounds (`immersivewoodworking:sounds/saw/sawing_*`, referenced, not copied) per half turn, in IW's speed bands. It also puffs sawdust particles at each blade set's leading edge.

### Editing by hand

If you edit the shapes in VS Model Creator, keep the element names: the rig finds its parts through them. Every element of frame *n* is prefixed `f<n>_`, then `frame_`, `sash_`, `rod_`, `crank_` or `blade_`. The shared parts use `shaft_`, `gear_main_`, `gear_idler_` and `gear_disc_`.

Hand edits are lost when the script runs again. Either port them into `make_shape.py`, or stop regenerating.

The model is derived from Immersive Woodworking's sawmill model by Bobrik00 and is used with permission. It is not covered by the repository's license; see `CREDITS.md`.

## Tests

- `tests/` (`dotnet test mods-src/buckingsawmill/tests`, no game needed) compiles `Core/` and tests
  the rig parser (with its own fixture, and the shipped `rig.json`), the rotation maths, the
  assembly rules, the cut arithmetic, the saw depth and raise arithmetic and the config. It also tests the rig's animation: glob
  matching, each driver, ride composition and cycles, the shaft's direction against a vanilla
  axle on every facing, and the shipped rods staying on their crank pins.
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
