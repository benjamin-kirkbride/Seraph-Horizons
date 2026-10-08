# Squaring shear

Part of the Seraph Horizons mod (`../README.md`): the first rung of the pipe ladder (unified pipes,
`../Pipes/`). A tinsmith's **foot-treadle squaring shear** of the early-to-mid 1800s, framed in oak with
iron blades, an iron back gauge and an iron hold-down, cuts one lead or copper **plate**
(`game:metalplate-lead` or `-copper`, 8 × 8) once, across its middle, into two **half plates**
(`seraphhorizons:halfplate-{lead,copper}`, 8 × 4 each). The press brake (`../PressBrake/`) folds a half
plate into an angle. The plate goes on the table against the back gauge, the **hold-down** comes down on
it, the **treadle** pulls the **crosshead** and its upper blade down through it past the lower blade, the
blade and the hold-down go back up and the two halves are drawn off across the table towards the
operator.

This folder holds the model's generator and its rig (`tools/`); the gameplay is built against the rig
("Gameplay" below).

**A hand machine.** It has no mechanical power, no power cell and no oil. The player works it by
holding right-click on it, as on the quern. The rig's θ is that work, the treadle's hold-to-work clock:
every motion is posed by the cut cycle W, and θ moves nothing.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: every motion is a `gauge`, and the links ride the crosshead).

## The machine

**Footprint.** 2 cells: 1 wide (x), 1 high (y), 2 long (z). In the native, south-facing frame with
the controller at `[0,0,0]`: x 0, y 0, z 0..1. The controller is the table end, `[0,0,0]`, the block
the player clicks. Placed like the press brake, the shear extends away from the player along their line
of sight, the table end nearest them: the block's `side` is the way they look. The operator stands at
the table with a foot on the treadle; the half plates come off towards them (native north), and a chest
of plates can stand behind the gauge (native south).

**The cut** (the lower blade's back face, the line the upper blade passes) runs along x at y 9, z 16
voxels (in blocks 0.5625, 1): the table's top, on the cells' boundary.

**The half plate** (the item's shape follows these exact dimensions): 8 voxels along the blade, 4 across
it, 1 thick. As delivered on the table (build frame): the near half x 4..12, y 9..10, z 4..8; the far
half x 4..12, y 9..10, z 8..12. Two of them are the plate they were cut from.

**Anchors** (in `assets/seraphhorizons/config/squaringshear-rig.json`): `infeedSide` south and
`outputSide` north; `output.pos` (over the table where the halves lie at W = 1), `plate.pos` (the middle
of the plate as laid: loading sounds) and `edge.pos` (the middle of the cut: the cut's sound and dust).
There is no `powerCell` or `powerFace`.

**Fitted parts** (the rig's `requires`). The frame item carries everything with `requires` null: the
oak end cheeks (legs, rails, the housings with their caps), the table, the gauge rails and the
stretcher, the iron bosses and pins the treadle turns on, the crosshead with its iron shoes, the iron
links, and the treadle (its oak arms and foot bar, its iron knuckles and pins). The **build order** is
the frame, then `blade` and `gauge`:

| Order | `requires` | Item | Draws | Taken back |
|---|---|---|---|---|
| 1 | `blade` | `game:metalplate-iron` or `-steel` | The upper blade on the crosshead and the lower blade in the table's rebate. Their texture code is `blade`, which the renderer sets to the plate's metal | Only by breaking the frame |
| 2 | `gauge` | `game:rod-iron`, `-meteoriciron` or `-steel` | The back gauge (two arms and the stop across them) and the hold-down bar. Their texture code is `gauge`, which the renderer sets to the rod's metal | Only by breaking the frame |
| — | `platelead` | The work: a lead plate on the shear (k = 1) | The plate, as laid and as its two halves are drawn off (`lf`, `lb`) | The material being worked, not a part |
| — | `platecopper` | The work: a copper plate (k = 2) | The same in copper (`cf`, `cb`) | as above |

Why these items: a shear's blades are ground from plate, and its gauge arms, stop and hold-down are
square bar, which the game's rod stands for. The treadle is the frame's: no game item reads as one.
Until the blades are fitted, the table's far edge shows its rebate and the crosshead's front is bare.
The viewer shows `platelead` only with lead chosen and `platecopper` only with copper (`requiresClass`).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The sheet wears the game's plain lead and copper sheet
(`game:block/metal/sheet-plain/lead1`, `copper1`), the wood debarked oak and the iron
`game:block/metal/plate/iron`. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/squaringshear.json` | The whole machine, every moving part (61 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/squaringshear_frame.json` | The static frame only (33 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/squaringshear-rig.json` | Footprint, anchors, the work, the cut's constants and the part rig (13 parts). |
| `tests/SquaringShear/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y
up, z north to south) in voxels, measured from the machine box's north-west bottom corner, which is the
controller's corner, so the shipped files are the build frame divided by 16.

**The frame.** Two oak end cheeks (x 0..1.2 and 14.8..16), each two legs (z 3..5 and 29.4..31.4), a top
rail under the table's ends and a low rail. On each cheek's top rail stand the housings: three oak posts
(z 12.4..13.3, 15.65..16.75, 18.85..20) under a cap, making a slot for the hold-down's end and one for
the crosshead's. The oak table (y 7..9) spans the cheeks from z 3 to the cut, its far edge rebated for
the lower blade (z 15..16, y 8..9). Behind the cut two oak gauge rails cross the frame (z 20.5..22 and
26.5..28) and a stretcher ties the back legs. On each back leg's inner face is an iron boss carrying the
treadle's pivot pin (y 2.5, z 30.4).

**The crosshead** is oak (x 1.2..14.8, y 12.6..15.4 at rest, z 16.8..18.8) with iron shoes on its ends,
which run in the housings' back slots. The upper blade is bolted to its front face, 0.76 thick, its edge
0.8 over the sheet at rest. Its underside stays over the far half through the stroke.

**The treadle** is two oak arms under the table, from the pivot at the back to the oak foot bar at the
front (y 6, z 1.5 at rest), with iron knuckles round the pivot pins. An iron link hangs from each end of
the crosshead's underside to a pin in the arm under it, its eye slotted for the pin's small swing. The
treadle turns 9.5° down on the stroke, which brings the pins, the links and the crosshead 2.1 down: the
upper blade's edge from 0.8 over the sheet to 0.3 past the lower blade's edge, 0.04 behind its back
face. The foot comes down to 0.5 over the floor. The linear gauges keep the pin and the link's eye
within 0.002 of each other.

**The hold-down** is a bar across the table just in front of the cut (z 13.4..15.55), its ends in the
housings' front slots. With no plate on, it lies on the table. As a plate goes on, it stands 1 voxel
(`LIFT`) over the sheet, and it comes down onto the sheet to clamp it.

**The plate as laid.** The plate goes on as the game's plate item reads, 8 × 8: x 4..12, z 12..20, on
the table and over the lower blade, its far edge against the gauge's stop. The near half (z 12..16) lies
on the table, the far half (z 16..20) on the gauge arms. The two halves are one plate until the cut.

**The cycle** (t = W, one plate; θ, the treadle's work, moves nothing):

| t | What moves |
|---|---|
| 0.00..0.06 | The plate lies on the table (p eases in; the hold-down rises off the table with it). |
| 0.06..0.16 | The hold-down comes down on the plate. |
| 0.20..0.40 | The treadle goes down and the crosshead with it: the upper blade comes down through the plate, the cut. |
| 0.40..0.60 | The treadle comes back up, and the blade with it. |
| 0.66..0.76 | The hold-down rises 1 off the sheet. |
| 0.80..0.96 | Both halves slide 8 north, under the raised hold-down and blade, onto the table. |
| 0.96..1.00 | They lie there: delivered at 1. |

The crosshead's stroke is one gauge window, down and back: its middle (0.40, the blade at the bottom)
is the cut.

Every moving part (W the cut cycle, k the metal: 1 lead, 2 copper, p its presence):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Crosshead: oak, shoes (`crosshead`) | The links | The upper blade | `gauge` slide y −2.1, one window |
| Upper blade (`upperblade`) [blade] | Rides the crosshead | The sheet (cut) | none |
| Treadle: arms, foot, knuckles, pins (`treadle`) | The operator's foot | The links | `gauge` rotate x about the pivot by −9.5°, the same window; `rotate` x ratio 0 on θ (θ's carrier; moves nothing) |
| Links (`linkw`, `linke`) | The treadle's pins | The crosshead | Ride the crosshead |
| Hold-down (`holddown`) [gauge] | The frame | The sheet (clamped) | `gauge` slide y +2 (`present`); `gauge` slide y −1 in the clamp window |
| Back gauge: arms, stop (`gauge`) [gauge] | Fixed | — | none |
| Lower blade (`lowerblade`) [blade] | Fixed | — | none |
| Near and far halves (`lf`, `lb`) [platelead], (`cf`, `cb`) [platecopper] | The operator's pull | — | the slide off (slide z −8) |
| Frame (`frame`) | Static | — | none |

There are no gears, so no toothed wheel meshes with nothing. Every shaft-like part is carried: the
treadle's pivot pins in their bosses, the links' eyes on the treadle's pins, and the crosshead's and the
hold-down's ends in the housings' slots.

### Regenerating

```sh
python3 mods-src/seraphhorizons/SquaringShear/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/SquaringShear/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/SquaringShear/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 4 s. `tools/validate_squaringshear.py` holds the checks. Every run checks its
output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part.
- **Containment:** nothing leaves the 1 × 1 × 2 box over the whole cycle of both metals, the treadle's
  foot pressed included.
- **Textures** by role: oak timber, the crosshead, the treadle's arms and foot; iron bosses, pins,
  shoes, knuckles and links; `blade` for the blades, `gauge` for the gauge and the hold-down; lead and
  copper sheet.
- **Sheet:** the plate lies as laid (both halves exactly where they were put) until the halves are
  drawn off; at W = 1 both metals' halves lie side by side on the table, each 8 × 1 × 4, the near one
  not overhanging the table's front.
- **Cut:** the upper blade's edge at least 0.5 over the sheet at rest and while the halves come off,
  past the lower blade's edge at the bottom of the stroke, and behind the lower blade's back face by
  0.04 throughout; the treadle's foot goes down with it.
- **Clamp:** the hold-down lies on the table with no plate, on the sheet through the stroke, and at
  least `LIFT` over it while the halves are drawn off.
- **Links:** each link's slotted eye holds the treadle's pin at every pose of the cycle (within 0.2 up
  and down, inside the slot along z).
- **Supports:** each pivot pin in its boss; the treadle's knuckles on their pins at rest and pressed;
  the crosshead's and the hold-down's ends in their slots through the cycle.
- **Treadle:** θ moves nothing.
- **Clearances:** over 103 poses no two parts touch except the intended contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the same, every 0.0025 of the cycle for both metals (802 poses): the
  treadle and the crosshead through the stroke, the halves' slide off.
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at nine poses over the cycle of both
  metals, of what can be seen (another metal's sheet is left out, `shown`).
- **Files:** every texture declared; lids over both columns; no power cell; the shipped model is the
  checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are.

### Rig schema (`squaringshear-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema"), with nothing added to the shared
rig maths: a `work` quantity, gauges, and one θ-driven rotate of ratio 0.

- **The work.** `{ "name": "cut", "unit": "plates", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one plate's cut cycle, 0..1: one cut.
- **Inputs.** W, k and p, and θ. k is the plate's metal (1 lead, 2 copper, 0 none). p is its presence,
  eased in as the plate goes on (the hold-down rises off the table with it) and out after delivery, with
  k held while p eases out. The gameplay advances W while the player holds right-click on a shear with a
  plate, at `1 / strokesPerPlate[k]` per stroke of its treadle clock θ. It delivers
  `cut.halfPlatesPerPlate` (2) `cut.halfPlates[k]` at W = 1, then clears (p eases out with W held at 1)
  and starts the next plate at W = 0. θ is that clock and poses nothing.
- **`cut`**: `strokesPerPlate` (lead 1, copper 1.5: the pace; copper, stiffer, half as long again),
  `plates` (each class's work), `halfPlates` (what a plate makes) and `halfPlatesPerPlate` (2).

**Keys.** `cells`, `infeedSide`, `outputSide`, `output`, `plate`, `edge`, `work`, `cut` and `parts`.
`requires` values: `blade`, `gauge`, `platelead`, `platecopper`, or null. The texture codes the
renderer sets: `blade` (the fitted plate's metal) and `gauge` (the rod's metal).

**In the viewer** (`site/models.json`, the site's model page): the treadle's work is the θ slider, and
it moves nothing. The cut cycle's slider reads in plates and stops at 1. The size select names the
plates ("Lead", "Copper"), and each metal's sheet shows only with its own metal (`requiresClass`). Play
moves W with θ at lead's pace (`play.turnsPerWork` `"cut.strokesPerPlate.thin"`); in the game copper's
is half as much again.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `crosshead_*`,
`upperblade_*`, `treadle_*`, `linkw_*`, `linke_*`, `holddown_*`, `gauge_*`, `lowerblade_*`, `lf_*`,
`lb_*`, `cf_*`, `cb_*`, and `fr_*` for the frame. Hand edits are lost when the script runs again: port
them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it
in a client or the site's viewer.

1. **The cut is an overlap.** The upper blade comes down into the far half where it lies; the far half
   is not pushed down or bent by it, and the plate is two halves from the start, drawn as one plate.
2. **The halves come back towards the operator.** On a real shear the far piece falls behind the
   blades. Here both are drawn north under the raised blade, so both come off at the output face (the
   press brake's arrangement: infeed south, output north).
3. **Linear ramps.** The gauges move in straight ramps: the treadle, the crosshead and the hold-down
   start and stop at full speed. The links' eyes are slotted for what the straight ramps leave between
   the treadle's arc and the crosshead's slide (0.002).
4. **No return spring.** The treadle and the crosshead come back up by the gauge; nothing draws a
   spring or a counterweight.
5. **θ moves nothing.** A treadle on θ could not hold still between strokes, nor follow the cycle: Play
   advances θ continuously. θ is carried by a ratio-0 rotate so that the viewer offers Play.
6. **The hold-down is not linked to the treadle.** It comes down and goes up by its own gauge, before
   and after the stroke.
7. **Lead and copper look the same** but for their texture and pace.

## Gameplay

The rules are game-independent in `Core/` (`SquaringShearParts.cs`, `Cutting.cs`,
`SquaringShearConfig.cs`, `SquaringShearRig.cs`, and `SquaringShearView.cs`: the renderer's view of the
shear and its clock); the game side is `Game/` (`SquaringShearSystem`, the controller
`BlockSquaringShear` and `BESquaringShear`, the ghost `BlockSquaringShearGhost` with
`BESquaringShearGhost`, and `SquaringShearRenderer`), copied from the press brake's
(`../PressBrake/README.md`, "Gameplay"). The mod's README, "Squaring shear", is the player-facing
summary; the decisions are here. Everything is read from the rig through its anchors, `requires` and the
stable part id `crosshead`, never element names.

**Blocks and item.** `seraphhorizons:squaringshear-frame-{side}`
(`assets/seraphhorizons/blocktypes/squaringshear/frame.json`, drawing `squaringshear_frame.json`; its
textures are every texture of both shapes, `blade` and `gauge` included) and `squaringshear-ghost` for
the far cell. Placement (`side` = the player's look, `SquaringShearRig.PlacedSide`, so the shear runs
away from them with the table end nearest), the ghost, its repair, breaking through it and the boxes
(selection from the rig's cells, collision with their lids) are the press brake's. The half plate,
`seraphhorizons:halfplate-{copper,lead}` (`assets/seraphhorizons/itemtypes/halfplate.json`,
`shapes/item/halfplate.json`: one 8 × 1 × 4 box in the game's plate texture), stacks to 16, stands on
the ground and on shelves, and melts back into one ingot (a plate melts into two). It is the shear's
own: `SquaringShearSystem.TypeAssets` lists its type file, so the switch takes it out with the shear,
and no recipe makes one. The frame recipe (`assets/seraphhorizons/recipes/grid/squaringshear.json`):
three oak planks, two oak logs, four nails and strips of iron, meteoric iron or steel, and a hammer, as
the press brake's, in another pattern (`PPP,NHN,L_L`, the brake's `NHN,PPP,L_L`). It takes no plate and
no rod: the frame's own iron (the bosses, pins, links, shoes and knuckles) is the nails and strips, and
the blades and the gauge are the stages.

**Stages** (`SquaringShearParts`), one item a stage, in `SquaringShearStage` order, the next missing
stage the only one a click fills (`OutOfOrder`, `AlreadyFitted`, `NotAPart` otherwise):

| # | `requires` | Item (verified in game 1.22.7's `survival/itemtypes`: `resource/metalplate.json`, `part/rod.json`) |
|---|---|---|
| 1 | `blade` | `game:metalplate-iron` or `-steel` |
| 2 | `gauge` | `game:rod-iron`, `-meteoriciron` or `-steel` |

**Take-back**, as the press brake's: the parts come back only by breaking (only a consumable may be
taken out of a built machine, and the shear has none; Ctrl + right-click once took the last stage back
out, and no longer does). Ctrl takes the plate off while it is still whole (W = 0); a plate being cut
stays (an error says so). With no plate on, a Ctrl click is an ordinary click. Breaking drops every part
and a whole plate (a half-cut one is lost). The
creative shortcut (Ctrl in creative mode on an incomplete shear) fits each stage's first code. A save
restores the stages as a run from the first.

**Hold to work** (the quern's pattern, as the press brake's): the block forwards the interaction's
start, every step, the stop and the cancel to the block entity (the ghost forwards them too). A click
with a part fits it; with a plate on an empty table, loads it (and, held on, the steps that follow work
it); anything else (an empty hand, a tool, a plate on a loaded table) on a complete shear is a work
click, Shift excepted so a held block can still be placed against the shear. The server keeps the
players working the treadle in `TreadleHolds`, refreshed by each step and dropped on the stop, or after
`Cutting.HoldTimeoutMs` (600) without a step. Its 50 ms tick cuts while anyone holds: θ, the treadle
clock, turns `Cutting.StrokesPerSecond` (1) a second, and `BESquaringShear.Cut(radians)` advances W by
`PlatesFor(radians, StrokesPerPlate(k))` (the Atlas scenarios call it to finish a plate). Whether anyone
holds is synced (`held`), and the client's own player counts as holding for 250 ms after its last step,
so the renderer answers at once. No animation of its own: the game's held-interaction pose is used.

**The cut** (`Cutting`, `CutJob`). The work is a plate, `game:metalplate-lead` (k 1) or `-copper` (k 2),
the rig's `cut.plates`, cut once across its middle; what comes off is `cut.halfPlates[k]`
(`seraphhorizons:halfplate-{metal}`), `cut.halfPlatesPerPlate` (2) of it. Nothing else goes on: a half
plate, an angle, an ingot or another metal's plate is refused. A plate goes on only on a complete shear,
one at a time, and only when its half plate exists (the shear's own item, so it is missing only when
its type file is: then a plate is refused with a message, as the press brake refuses one with
`UnifiedPipes`' angle missing). With the `PressBrake` switch off the shear still cuts: the half plates
then have no use but melting. At W = 1 the plate is used up and the half plates go into a container in
`SquaringShearRig.OutputNeighbour()` (the cell beyond `output.pos` across the output face, native
north), else drop at `OutputDrop()`, pushed outward. The cut (`game:sounds/block/heavymetal-hit2`) is
heard at the bottom of the stroke, the middle of the `crosshead` part's one gauge window
(`SquaringShearRig.Strokes`, `CutMoments`: 0.40 as generated). The server syncs W every 0.02 plate.
**Infeed:** a hand machine takes nothing by itself. A work click on an empty table, or a step held on
past a finished plate, takes one plate from a container in the cell beyond the far end
(`InfeedNeighbours`, native south); after a plate is done the held steps wait 0.6 s (`ClearMs`) before
the next, so the model eases the hold-down back first, and the hold goes on through that wait while the
infeed has a plate.

**Settings** (`SquaringShearSettings`): `StrokesPerPlateLead` 1 and `StrokesPerPlateCopper` 1.5, the
rig's `cut.strokesPerPlate` (`SquaringShearRigTests.The_default_pace_is_the_rigs` holds them together);
the server sends them to clients in the block entity's tree. The blades do not wear, so there is no
durability setting, no tool in the export and no oil.

**Renderer** (`SquaringShearRenderer`, through `ISquaringShearView`: facing, fitted parts, the blades'
and gauge's metals, the plate's class and the server's W, and whether anyone holds). Every rig part with
a `requires`, a ride or a driver, from `squaringshear.json`, drawn when its stage is fitted, with the
texture codes `blade` and `gauge` set to the fitted plate's and rod's metals
(`MachineMeshes.MetalTexture`, one mesh set per pair of metals). The sheet's parts (`platelead`,
`platecopper`) are drawn only while that metal's plate is on the table: once the halves are delivered
they drop as items, and the sheet is not drawn while p eases out, though the hold-down eases back with
it. `SquaringShearClock` turns θ while held, and shows W through `Machines/Core/HeldWorkFollower`, as the
press brake's: predicted at the plate's pace every frame while held and eased toward the server's W
(carried forward at that pace for up to 0.25 s since it last changed), never snapped to it and, while
held, never run backward; with no plate on, W is held at 1 while p eases out over 0.4 s and k is held;
the next plate starts from the server's W. While the blade moves (`IsCutting(W)`) and the
treadle is held, metal dust at `edge.pos`; while worked, the frame creaks
(`game:sounds/block/woodcreak_1..4`).

**Why the follower** (the cut stuttered while held, as if at a few frames a second). The server cuts
on its 50 ms tick, which fires only on its own frames, so W moves in uneven steps, synced each step
and late by the trip to the client. A lead plate is one stroke and a stroke a second, so a tick moves
W by 0.05, and 0.066 or more on a tick that runs late. The clock used to advance W at the pace and snap
it to the server's W whenever it fell behind it or ran more than 0.06 ahead: a band no wider than one
tick's work, so it was thrown out at nearly every packet and the crosshead moved in the server's steps,
jumping back and forward. (The press brake, slower at 1.5 and 2.25 lever turns a half plate, had some
slack and only jittered now and then.) The follower keeps W moving every frame and only steers it
toward the server (`tests/Machines/HeldWorkFollowerTests.cs` plays the server's steps against a 60 fps
client, and shows the old band snapping the shear).

**Handbook.** Three sections on the frame (`attributes.handbook.extraSections`): assembly, cutting
plates, and half plates and pipe (the ladder: two half plates a plate, and nothing else makes one; the
press brake folds a half plate into an angle; two angles and solder make a tube blank; tube blanks make
pipe sections on the mandrel station or the draw bench). The half plate has its own section. With the
switch off, `SquaringShearSystem.UnlinkText` strips links to the shear and to the half plate from the
mod's own text.

**Export.** `tools/recipe-export/Recipes/SquaringShearExport.cs` and `RecipeSection.SquaringShear.cs`
write one `machine` record per metal (`squaringshear|game:metalplate-{metal}|0`): the plate consumed,
the blades and gauge kept, the frame as the station, two half plates out; `power` `hand`, `turns` the
treadle strokes a plate, `work` the same in `strokes` (the site says "By hand: 1 stroke of the treadle a
job"), no `wear` and no `oil`. Type `squaringshear`, owned by `SquaringShear`
(`Core/SwitchOwnership.cs`); the half plate's code and the frame's grid recipe are owned through
`SwitchRegistry` from `SquaringShearSystem`'s asset lists.

**Tests.** `tests/SquaringShear/SquaringShearGameplayTests.cs` (stages, saves, plates by
metal, load rules, two half plates a plate, W only while held and done at 1, cuts crossed, treadle
holds, the clock, settings, placing) and `SquaringShearRigTests.cs` (the shipped rig through the shared
parser and `SquaringShearRig`, its anchors, its one stroke, the pace held to the settings, the reader's
refusals, and every pose of `tests/SquaringShear/rig-reference.json` replayed through `Machines/Core`);
`tools/tests/test_squaringshear_model.py` and `site/test/squaringshear.test.ts` (the generated files
against their own rules and the site's rig maths); `tests/PackTests/SquaringShearScenarios.cs`,
`RecipeExportSquaringShearScenarios.cs` and
`SwitchesOffScenarios.Squaring_shear_off_there_is_no_squaring_shear_and_no_half_plate` (Atlas:
`FullyQualifiedName~Squaring_shear`); `tests/Machines/HeldWorkFollowerTests.cs` (the clock's W while
held).
