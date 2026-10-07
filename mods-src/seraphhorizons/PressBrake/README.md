# Press brake

Part of the Seraph Horizons mod (`../README.md`): the brake rung of the pipe ladder (unified pipes,
`../Pipes/`). A hand-worked **leaf brake** (a cornice brake) of the early-to-mid 1800s, in oak with iron
wearing edges and iron clamping screws, folds one lead or copper **plate** (`game:metalplate-lead` or
`-copper`) into two **open chute sections** (`seraphhorizons:chutesectionopen-{lead,copper}`), which are
soldered shut on the grid into the game's chute sections. The plate goes on in two halves lying end to
end along the folding edge, and both halves are folded together, twice. The **clamping bar** is screwed
down on the sheet and the **leaf** swings up and folds the near flange up. The bar is screwed up, the
sheet is pulled one panel towards the operator, the bar goes down again and the leaf folds the middle
panel up. The first flange swings back over the bar's low nose, and the sheet is a U round the bar, open
towards the far end. The bar is screwed up and the two U sections are slid off it onto the leaf.

This folder holds the model's generator and its rig (`tools/`); the gameplay is built against the rig
("Gameplay" below).

**A hand machine.** It has no mechanical power, no power cell and no oil. The player works it by
holding right-click on it, as on the quern. The rig's θ is that work, the lever's hold-to-work clock:
every motion is posed by the fold cycle W, and θ moves nothing.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: every motion is a `gauge`).

## The machine

**Footprint.** 2 cells: 1 wide (x), 1 high (y), 2 long (z). In the native, south-facing frame with
the controller at `[0,0,0]`: x 0, y 0, z 0..1. The controller is the leaf end, `[0,0,0]`, the block
the player clicks. Placed like the draw bench, the brake extends away from the player along their line
of sight, the leaf end nearest them: the block's `side` is the way they look. The operator stands at
the leaf; the U sections come off towards them (native north), and a chest of plates can stand behind
the far end of the bed (native south).

**The folding edge** (the leaf's hinge axis) runs along x at y 4.6, z 10 voxels (in blocks 0.2875,
0.625): the bed's top and its iron edge. A panel of the sheet is 8 voxels (the game's chute section is 8
across); the sheet is 1 thick, as the open section item's walls are. Each half is 6.45 long (x 1.45..7.9 and 8.1..14.55), so each section is
6.45 long with an 8 × 8 U.

**Anchors** (in `assets/seraphhorizons/config/pressbrake-rig.json`): `infeedSide` south and `outputSide`
north; `output.pos` (over the leaf at the near end, where the two U sections lie at W = 1), `plate.pos`
(the middle of the laid plate on the bed: loading sounds) and `edge.pos` (the middle of the folding edge:
the bend's sound and dust). There is no `powerCell` or `powerFace`.

**Fitted parts** (the rig's `requires`). The frame item carries everything with `requires` null: the
oak end frames (legs and rails), the bed, the iron hinge bearings and pins, the iron gallows with their
nuts, the folding leaf with its knuckles and its bail handle (the lever: two iron arms and an oak hand
bar), and the clamping bar with its screw cups. The **build order** is the frame, then `screws` and
`edge`:

| Order | `requires` | Item | Draws | Taken back |
|---|---|---|---|---|
| 1 | `screws` | `game:rod-iron`, `-meteoriciron` or `-steel` | The two clamp screws with their tommy bars. Their texture code is `screw`, which the renderer sets to the rods' metal | Only by breaking the frame |
| 2 | `edge` | `game:metalplate-iron` or `-steel` | The iron wearing edges: the bed's folding edge, the leaf's edge, the bar's nose. Their texture code is `edge`, which the renderer sets to the plate's metal | Only by breaking the frame |
| — | `platelead` | The work: a lead plate on the brake (k = 1) | The two halves of the sheet, each in three panels, folding into two U sections | The material being worked, not a part |
| — | `platecopper` | The work: a copper plate (k = 2) | The same in copper | as above |

Why these items: the game has no screw, so the screws are turned rods, and the wearing edges are strips
of plate. No game item reads as a brake's lever (the crowbar is a tool the player would lose), so the
lever is the frame's. Until the edges are fitted, the bed's front, the leaf's heel and the bar's foot
show their rebates. The viewer shows `platelead` only with lead chosen and `platecopper` only with copper
(`requiresClass`).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The sheet wears the game's plain lead and copper sheet
(`game:block/metal/sheet-plain/lead1`, `copper1`), the wood debarked oak and the iron
`game:block/metal/plate/iron`. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/pressbrake.json` | The whole machine, every moving part (82 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/pressbrake_frame.json` | The static frame only (37 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/pressbrake-rig.json` | Footprint, anchors, the work, the fold's constants and the part rig (15 parts). |
| `tests/PressBrake/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y
up, z north to south) in voxels, measured from the machine box's north-west bottom corner, which is the
controller's corner, so the shipped files are the build frame divided by 16.

**The frame.** Two oak end frames (x 0..1 and 15..16), each two legs, a top rail and a low rail, run
from z 9 to 31.6. Between them is the oak bed (y 1.6..4.6) from the folding edge at z 10 to the far end,
its front piece rebated for the iron folding edge (z 10..11, y 3.6..4.6). An oak stretcher ties the legs
at the far end. On each end frame's front is an iron hinge bearing (y 3.6..5.6, z 9..10.45) carrying the
leaf's pin, and over each end of the clamping bar is an iron gallows: two uprights either side of the
bar's end, a bridge across them, and in it the screw's nut.

**The leaf** is oak, 8 wide (z 2..10) and 2 deep under its face, its face flush with the bed. Its iron
edge lies in a rebate along the hinge, and iron knuckles and straps at its ends turn on the pins. Its
bail handle is two iron arms from under the leaf to an oak hand bar 9.2 from the hinge, 20° below the
horizontal at rest. At copper's throw the hand bar is near the top of the block.

**The clamping bar** is oak (y 1 to 3.5 over its foot, z 11.95..16.4) on an iron nose (z 11.45..12.35, 1
tall). The nose is set back from the edge, and the oak further still, so a flange over-bent to 100°
clears them. Each end carries an iron cup for the screw's tip. With no plate on, the bar lies on the
bed. As a plate goes on, it stands 1 voxel (`LIFT`) over the sheet, lifted by the screws.

**The screws** turn in their nuts with a pitch of 0.25 voxels a turn, right-handed. A screw's tip
stays in its cup, and the bar rises and falls with the screws. To clamp, the screws are turned 4 turns
down and the bar comes 1 down onto the sheet. Copper is screwed a quarter turn harder, and its tip
presses 1/16 voxel into the cup.

**The cycle** (t = W, one plate; θ, the lever's work, moves nothing):

| t | What moves |
|---|---|
| 0.00..0.04 | The plate lies on: flange A over the leaf, panels M and B on the bed (p eases in; the bar stands over it). |
| 0.04..0.12 | The screws turn down; the bar comes down on M. |
| 0.13..0.25 | The leaf swings up 95° (lead) or 100° (copper), carrying flange A up against the bar's nose. |
| 0.25..0.37 | The leaf falls back; A springs back with it to 90° and stays there. |
| 0.38..0.46 | The screws turn up; the bar rises 1 off the sheet. |
| 0.46..0.54 | The sheet is pulled 8 north: M lies on the leaf, A stands at the leaf's front edge, B is under the bar. |
| 0.54..0.62 | The bar comes down on B. |
| 0.63..0.75 | The leaf swings M up; A swings back over the bar's nose and lies over B (the bar inside the U). |
| 0.75..0.87 | The leaf falls back; M springs back to 90°. |
| 0.88..0.96 | The bar rises. |
| 0.96..1.00 | The two U sections slide 8 north, off the bar onto the leaf: delivered at 1. |

The leaf's throw is the rig's `fold.throwDegrees`. A panel the leaf carries follows it exactly, by the
same gauge, until the leaf falls back to 90°. From there the spring-back gauge, whose window is copper's
and whose lead gain matches lead's fall, holds it at 90°.

Every moving part (W the fold cycle, k the metal: 1 lead, 2 copper, p its presence):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Leaf: oak, knuckles, straps (`leaf`) | The operator, by its handle | The panel it carries | `gauge` rotate x about the hinge by the throw, one window per fold |
| Leaf's iron edge (`leafedge`) [edge] | Rides the leaf | — | none |
| Bail handle: arms, hand bar (`lever`) | Rides the leaf | The leaf | `rotate` x ratio 0 on θ (θ's carrier; moves nothing) |
| Clamping bar, cups (`bar`) | The screws | The sheet (clamped) | `gauge` slide y +2 (`present`); `gauge` slide y −1 in each clamp window |
| Bar's iron nose (`baredge`) [edge] | Rides the bar | — | none |
| Clamp screws (`screww`, `screwe`) [screws] | The operator, by the tommy bars | The bar | `gauge` rotate y about its own axis and slide y, as its thread: +8 turns, +2 (`present`); −4 turns, −1 a clamp (copper −4¼ turns, −1.0625) |
| Bed's iron edge (`bededge`) [edge] | Fixed | — | none |
| Sheet panels A, M, B (`la`, `lm`, `lb`) [platelead], (`ca`, `cm`, `cb`) [platecopper] | The leaf; the operator's pull | — | A: the leaf's fold 1 and its spring-back, the pull (slide z −8), fold 2 and its spring-back, the slide off (slide z −8). M: the pull, fold 2 and its spring-back, the slide off. B: the pull, the slide off |
| Frame (`frame`) | Static | — | none |

There are no gears, so no toothed wheel meshes with nothing. Every shaft-like part is carried: the
leaf's pins in the bearings, the screws in their nuts, and the bar's ends between the gallows' uprights.

### Regenerating

```sh
python3 mods-src/seraphhorizons/PressBrake/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/PressBrake/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/PressBrake/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 5 s. `tools/validate_pressbrake.py` holds the checks. Every run checks its output
and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part.
- **Containment:** nothing leaves the 1 × 1 × 2 box over the whole cycle of both metals, the hand bar
  at copper's throw included.
- **Textures** by role: oak timber, the leaf and the hand bar; iron bearings, pins, gallows, nuts,
  knuckles, straps, arms and cups; `edge` for the wearing edges, `screw` for the screws; lead and copper
  sheet.
- **Sheet:** the panel the leaf carries lies on the leaf's face (within 1e-4) through each fold, until
  the leaf is back at 90°. From there it holds at 90°, off the leaf. Each fold reaches its metal's throw,
  and at W = 1 both metals' U sections lie on the leaf, the B, M and A panels where an 8 × 8 U puts them.
- **Clamp:** the bar lies on the bed with no plate, on the sheet at every point of both folds, and at
  least `LIFT` over it while the sheet is pulled and slid off. Each screw's tip is in its cup, exactly
  for lead and within a quarter turn's 1/16 for copper. Every screw's rise is its turn times the pitch.
- **Supports:** each pin in its bearing; the knuckles on their pins at rest and at the throw; each screw
  through its nut's four bars, at rest, raised and clamped; the bar's ends between the uprights.
- **Lever:** θ moves nothing.
- **Clearances:** over 103 poses no two parts touch except the intended contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the same, every 0.0025 of the cycle for both metals (802 poses): the
  leaf and its handle through both throws, the flange over the bar, the sheet's pull and its slide off.
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at eight poses over the cycle of both
  metals, of what can be seen (another metal's sheet is left out, `shown`).
- **Files:** every texture declared; lids over both columns; no power cell; the shipped model is the
  checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are.

### Rig schema (`pressbrake-rig.json`)

The gear cutter's schema (`../GearCutter/README.md`, "Rig schema"), with nothing added to the shared rig
maths: a `work` quantity, gauges, and one θ-driven rotate of ratio 0.

- **The work.** `{ "name": "fold", "unit": "plates", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one plate's fold cycle, 0..1.
- **Inputs.** W, k and p, and θ. k is the plate's metal (1 lead, 2 copper, 0 none). p is its presence,
  eased in as the plate goes on (the bar and the screws rise off the bed with it) and out after
  delivery, with k held while p eases out. The gameplay advances W while the player holds right-click on
  a brake with a plate, at `1 / leverTurnsPerPlate[k]` per turn of its lever clock θ. It delivers two
  `fold.sections[k]` at W = 1, then clears (p eases out with W held at 1) and starts the next plate at
  W = 0. θ is that clock and poses nothing.
- **`fold`**: `leverTurnsPerPlate` (lead 6, copper 9: the pace), `throwDegrees` (95, 100: as drawn),
  `plates` (each class's work), `sections` (what a plate makes) and `sectionsPerPlate` (2).

**Keys.** `cells`, `infeedSide`, `outputSide`, `output`, `plate`, `edge`, `work`, `fold` and `parts`.
`requires` values: `screws`, `edge`, `platelead`, `platecopper`, or null. The texture codes the
renderer sets: `edge` (the fitted plate's metal) and `screw` (the rods' metal).

**In the viewer** (`site/models.json`, the site's model page): the lever's work is the θ slider, and it
moves nothing. The fold cycle's slider reads in plates and stops at 1. The size select names the plates
("Lead", "Copper"), and each metal's sheet shows only with its own metal (`requiresClass`). Play moves W
with θ at lead's pace (`play.turnsPerWork` `"fold.leverTurnsPerPlate.thin"`); in the game copper's is
half as much again.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `leaf_*`, `leafedge_*`,
`lever_*`, `bar_*`, `baredge_*`, `screww_*`, `screwe_*`, `bededge_*`, `la_*`, `lm_*`, `lb_*`, `ca_*`,
`cm_*`, `cb_*` (each panel `_1` and `_2`, the two halves), and `fr_*` for the frame. Hand edits are lost
when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's own viewer
(a standalone copy, `site/scripts/standalone-viewer.ts`). No one has yet looked at it in a client.

1. **No shear.** The plate goes on already in two halves. A shear blade along the bed's centre line
   would cross the clamping bar, and an 8 × 8 U's sheet (24 deep) does not fit behind the bar to be cut
   there.
2. **No turn.** The second fold is made by pulling the sheet one panel forward, the first flange
   swinging back over the bar's low nose, as a channel is made on a cornice brake. The U comes out open
   towards the far end, round the bar, and is slid off forwards. A bar lifted clear of an upright U
   would need 8 voxels of screw travel, which a 1-high machine cannot hold.
3. **The open section here has no lips, and is 6.45 long.** `seraphhorizons:chutesectionopen`'s item
   shape (8 long, lips folded in from both walls) cannot be made here: two 8-long sections fill the whole
   16-voxel edge, leaving no room for bearings, gallows or screws, and lips close the section round the
   clamping bar, which could then come out only through the 2-voxel slot between them. The brake draws an
   8 × 8 U with 1-voxel walls, 6.45 long; the item is to follow it (the contract's Changes).
4. **Square bends.** Each bend is two panels meeting at the corner, overlapping by the sheet's
   thickness. There is no bend radius.
5. **Linear ramps.** The gauges move in straight ramps: the leaf, the bar and the sheet start and stop at
   full speed.
6. **θ moves nothing.** A leaf on θ could not hold still between the folds, nor follow the sheet: Play
   advances θ continuously. θ is carried by a ratio-0 rotate so that the viewer offers Play.
7. **Delivery.** At W = 1 the sections lie on the leaf; as p eases out, every gauge eases back, so a
   renderer should stop drawing the sheet once the sections are delivered.
8. **Lead and copper look the same** but for their texture, the copper throw (100°) and the screws'
   extra quarter turn.

## Gameplay

The rules are game-independent in `Core/` (`PressBrakeParts.cs`, `Folding.cs`, `PressBrakeConfig.cs`,
`PressBrakeRig.cs`, and `PressBrakeView.cs`: the renderer's view of the brake and its clock); the game
side is `Game/` (`PressBrakeSystem`, the controller `BlockPressBrake` and `BEPressBrake`, the ghost
`BlockPressBrakeGhost` with `BEPressBrakeGhost`, and `PressBrakeRenderer`), copied from the draw bench's
(`../DrawBench/README.md`, "Gameplay") without its power ghost, its mechanical power behaviour and its
oil. The mod's README, "Press brake", is the player-facing summary; the decisions are here. Everything
is read from the rig through its anchors, `requires` and the stable part id `leaf`, never element names.

**Blocks.** `seraphhorizons:pressbrake-frame-{side}` (`assets/seraphhorizons/blocktypes/pressbrake/frame.json`,
drawing `pressbrake_frame.json`; its textures are every texture of both shapes, `edge` and `screw`
included) and `pressbrake-ghost` for the far cell. Placement (`side` = the player's look,
`PressBrakeRig.PlacedSide`, so the brake runs away from them with the leaf end nearest), the ghost, its
repair, breaking through it and the boxes (selection from the rig's cells, collision with their lids)
are the draw bench's. The frame recipe (`assets/seraphhorizons/recipes/grid/pressbrake.json`): three oak
planks, two oak logs, four nails and strips of iron, meteoric iron or steel, and a hammer. It takes no
plate and no rod: the contract puts the frame's own iron (gallows, nuts, hinge pins, bail arms) in the
frame, and the screws and edges are the stages, so the nails and strips stand for the frame's iron and
the rods and plate are fitted.

**Stages** (`PressBrakeParts`), one item a stage, in `PressBrakeStage` order, the next missing stage the
only one a click fills (`OutOfOrder`, `AlreadyFitted`, `NotAPart` otherwise):

| # | `requires` | Item (verified in game 1.22.7's `survival/itemtypes`: `part/rod.json`, `resource/metalplate.json`) |
|---|---|---|
| 1 | `screws` | `game:rod-iron`, `-meteoriciron` or `-steel` |
| 2 | `edge` | `game:metalplate-iron` or `-steel` |

**Take-back.** The contract has the parts back only by breaking; the gameplay brief asked for Ctrl +
right-click to take them back while no plate is on, and that is what is built: Ctrl takes the last
stage fitted (the edges, then the screws), and, with a plate on, the plate itself while it is still flat
(W = 0); a plate being folded stays (an error says so), and so do the parts. Breaking drops every part
and a flat plate (a half-folded one is lost). The creative shortcut (Ctrl in creative mode on an
incomplete brake) fits each stage's first code. A save restores the stages as a run from the first.

**Hold to work** (the quern's pattern, `BlockQuern`/`BlockEntityQuern`): the block forwards the
interaction's start, every step, the stop and the cancel to the block entity (the ghost forwards them
too). A click with a part fits it; with a plate on an empty bed, loads it (and, held on, the steps that
follow work it); anything else (an empty hand, a tool, a plate on a loaded bed) on a complete brake is a
work click, Shift excepted so a held block can still be placed against the brake. The server keeps the
players working the lever in `LeverHolds`, refreshed by each step and dropped on the stop, or after
`Folding.HoldTimeoutMs` (600) without a step, as the quern forgets a grinder after a second. Its 50 ms
tick folds while anyone holds: θ, the lever clock, turns `Folding.LeverTurnsPerSecond` (1) a second,
and `BEPressBrake.Fold(radians)` advances W by `PlatesFor(radians, LeverTurnsPerPlate(k))` (the Atlas
scenarios call it to finish a plate). Whether anyone holds is synced (`held`), and the client's own
player counts as holding for 250 ms after its last step, so the renderer answers at once. No hand
animation of its own: the game's held-interaction pose is used, as the quern's.

**The fold** (`Folding`, `FoldJob`). The work is a plate, `game:metalplate-lead` (k 1) or `-copper`
(k 2), the rig's `fold.plates`; what comes off is `fold.sections[k]`, `fold.sectionsPerPlate` (2) of
them. A plate goes on only on a complete brake, one at a time, and only when its section exists (it is
`UnifiedPipes`' item; with that switch off a plate is refused with a message). At W = 1 the plate is
used up and the two sections go into a container in `PressBrakeRig.OutputNeighbour()` (the cell beyond
`output.pos` across the output face, native north), else drop at `OutputDrop()`, pushed outward, as the
draw bench's. The bend (`game:sounds/block/heavymetal-hit`) is heard at the middle of each fold, the
`leaf` part's gauge windows (`PressBrakeRig.Folds`, `FoldMoments`: 0.25 and 0.75 as generated). The
server syncs W every 0.02 plate. **Infeed:** a hand machine takes nothing by itself. A work click on an
empty bed, or a step held on past a finished plate, takes one plate from a container in the cell beyond
the far end (`InfeedNeighbours`, native south); after a plate is done the held steps wait 0.6 s
(`ClearMs`) before the next, so the model eases the bar back first, and the hold goes on through that
wait while the infeed has a plate.

**Settings** (`PressBrakeSettings`): `LeverTurnsPerPlateLead` 6 and `LeverTurnsPerPlateCopper` 9, the
rig's `fold.leverTurnsPerPlate` (`PressBrakeRigTests.The_default_pace_is_the_rigs` holds them together);
the server sends them to clients in the block entity's tree. The edges do not wear (the contract gives
them none), so there is no durability setting, no tool in the export and no oil.

**Renderer** (`PressBrakeRenderer`, through `IPressBrakeView`: facing, fitted parts, the screws' and
edges' metals, the plate's class and the server's W, and whether anyone holds). Every rig part with a
`requires`, a ride or a driver, from `pressbrake.json`, drawn when its stage is fitted, with the texture
codes `screw` and `edge` set to the fitted rods' and plates' metals (`MachineMeshes.MetalTexture`, one
mesh set per pair of metals). The sheet's parts (`platelead`, `platecopper`) are drawn only while that
metal's plate is on the bed: once the sections are delivered they drop as items, and the sheet is not
drawn while p eases out (weak spot 7 above), though the bar and screws ease back with it.
`PressBrakeClock` turns θ and advances W at the server's pace while held, never behind the server's W
and at most 0.06 ahead of it; with no plate on, W is held at 1 while p eases out over 0.4 s and k is
held; the next plate starts from the server's W. While the leaf swings (`IsFolding(W)`) and the lever is
held, metal dust at `edge.pos`; while worked, the frame creaks (`game:sounds/block/woodcreak_1..4`).

**Handbook.** Three sections on the frame (`attributes.handbook.extraSections`): assembly, folding
plates, and sections and pipe (the ladder: two open sections a plate, closed with solder, then pipe or
chute). With the switch off, `PressBrakeSystem.UnlinkText` strips links to the brake from the mod's own
text.

**Export.** `tools/recipe-export/Recipes/PressBrakeExport.cs` and `RecipeSection.PressBrake.cs` write
one `machine` record per metal (`pressbrake|game:metalplate-{metal}|0`): the plate consumed, the screws
and edges kept, the frame as the station, two open sections out; `power` `hand` (the schema's `power`
is a free string; its description now names `hand`, and the site says "By hand: 6 turns of the lever a
job"), `turns` the lever turns a plate, no `wear` and no `oil`. Type `pressbrake`, owned by `PressBrake`
(`Core/SwitchOwnership.cs`).

**Tests.** `tests/PressBrake/PressBrakeGameplayTests.cs` (stages, take-back, saves, plates by metal,
load rules, W only while held and done at 1, folds crossed, lever holds, the clock, settings, placing)
and `PressBrakeRigTests.cs` (the shipped rig through the shared parser and `PressBrakeRig`, its anchors,
the pace held to the settings, the reader's refusals, and every pose of `tests/PressBrake/rig-reference.json`
replayed through `Machines/Core`); `tests/PackTests/PressBrakeScenarios.cs`,
`RecipeExportPressBrakeScenarios.cs` and `SwitchesOffScenarios.Press_brake_off_there_is_no_press_brake`
(Atlas: `FullyQualifiedName~Press_brake`).
