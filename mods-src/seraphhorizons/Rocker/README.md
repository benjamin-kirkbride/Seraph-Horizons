# Rocker

Part of the Seraph Horizons mod (`../README.md`): a **hand** station of the ore-processing chain (the epic,
#684), the hand concentrator after the gold pan (vanilla's, 45 %) at 55 %. It is one of the three hand
stations with a model, with the riddle (#715) and the amalgam pan (`../AmalgamPan/`); the water and mill
tiers' concentrator is a separate machine (#735). A **rocker** (a cradle) of the 1850s gold rush: an
open-topped oak box on two curved rockers, its floor falling gently to its open foot over two riffles; on
its head a **hopper** (the riddle box) whose bottom is an iron **riddle plate**; under that an inclined
canvas **apron** on a frame that catches what falls through the riddle and carries it back to the head of
the box; an upright **handle** on its side, by which it is rocked. Crushed ore or placer gravel goes into
the hopper and water over it; the fines wash through the riddle, down the apron and back down the floor,
the riffles catch the heavy concentrate, and the water leaves at the foot with the tailings.

This folder holds the model's generator and its rig (`tools/`); it is the model issue, #717. The
gameplay (#716: crushed ore or placer gravel to concentrate at 55 %, tailings by #694) is not built yet:
"For the gameplay" below is what the model offers it.

**A hand station, watered by bucket.** It has no mechanical power, no power cell and no oil, and it takes
no water by pipe or from anything else. The player works it by holding right-click on it with a full
bucket in hand: it rocks, and the bucket is slowly poured into the hopper as it does. The rig's θ is that
work, the hold-to-work clock, and unlike on the press brake, the squaring shear and the mandrel station
(whose θ moves nothing) it is the motion itself: one turn of θ is one rock, over to the south and back
over to the north. There is no work quantity: the rig reads θ alone.

**One item, no build stages.** Levels (parts fitted in place, a tier at a time) are for the five
upgradeable machines; a hand station is cheap and separate. The rocker is placed whole, as one item, and
its only `requires` values are what it holds: water, a charge and concentrate.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator;
the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by
this one (no new driver: the rock is a `swing` and a `slide`, and the water's level a second `swing`, all
on θ).

## The station

**Footprint.** One cell, the controller `[0,0,0]`. The period cradle is about a metre long and half a
metre wide (40 inches by 16 to 20), which is a block at the game's scale: the box here is 14.2 voxels
(0.89 m) long and 8 (0.5 m) wide, its sides 4.5 voxels (0.28 m) over the floor, and a block holds it with
its rock (10 degrees each way, the box shifting 1.75 voxels as its rockers roll) and its handle. A second
cell would only add length the device does not have, and a cheap hand station should take one block.
The only neighbour it needs is the tailings' cell on the west.

**Orientation.** In the native, south-facing frame, the player who placed it stands on the north side
(`operatorSide`) looking south (the block's `side` is the way they look, as for the press brake and the
amalgam pan): the box runs across their view, along x, its **head** (the hopper) east, to their left, and
its open **foot** west, to their right. The **handle** is on the north side, the operator's, beside the
hopper they pour into; the **tailings** leave over the foot into the cell west of it.

**Anchors** (in `assets/seraphhorizons/config/rocker-rig.json`, blocks):

| Key | Where | For |
|---|---|---|
| `operatorSide` | `north` | Where the player stands to rock it, at the handle. |
| `hopper` (`part` `cradle`) | `[0.6846, 0.7006, 0.5]` | The middle of the riddle plate's top, riding the cradle: where a charge goes in and the bucket is poured (sounds, the pour's particles). |
| `outflow` (`part` `cradle`) | `[0.0693, 0.3186, 0.5]` | The middle of the floor's lip at the open foot, riding the cradle: where water and tailings leave (splash particles). |
| `concentrate` (`part` `cradle`) | `[0.4158, 0.3867, 0.5]` | On the concentrate behind the upper riffle, riding the cradle: where the player takes the concentrate out. |
| `tailings`, `tailingsSide` | `[-0.5, 0, 0.5]`, `west` | On the ground in the cell west of the rocker, beyond the open foot: where the tailings land (vanilla layered gravel or sand, #694). |
| `rock` | `degrees` 10, `radius` 0.625 | The rock as drawn (not an anchor; the viewer lists it as not drawn). |

There is no `powerCell` or `powerFace`, no water cell or face (nothing connects to it), and no
`infeedSide` or `outputSide`: a charge and the water go in by hand at the top, and the concentrate comes
out by hand at the riffles.

**Parts** (the rig's parts). With `requires` null, always drawn: the **cradle** (the box: floor, sides,
head board; the two rockers; the riffles; the apron; the hopper and its riddle plate; the handle), and the
**frame** (the two sills). Then the three states:

| `requires` | Part | Draws |
|---|---|---|
| `water` | `water` | Water washing through, while it is rocked and the bucket poured. |
| `charge` | `charge` | A charge heaped on the riddle plate (texture code `charge`). |
| `concentrate` | `concentrate` | The heavy sand caught behind the riffles (texture code `concentrate`). |

The states are drawn like the work on the press brake (its `platelead`) and the amalgam pan's contents:
`requires` values the renderer fits from the block entity's state, not items. In the viewer they are two
selects, **Water** (Dry: at rest; Wet: rocked while water is poured) and **Load** (empty; a charge in the
hopper; concentrate behind the riffles; both). Water opens Wet, since the water is only there while it is
worked and Play rocks it (a play script cannot change a state); pick Dry to see it at rest. Load opens
empty.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The wood is debarked oak (`game:block/wood/debarked/oak`), the riddle plate
plain iron plate (`game:block/metal/plate/iron`, texture code `riddle`), the apron's canvas
`game:block/linen`, the water `game:block/liquid/water` (as the pickling tub's), a charge granite gravel
(`game:block/stone/gravel/granite`) and the concentrate basalt sand (`game:block/stone/sand/basalt`), both
for the renderer to replace with the material's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/rocker.json` | The whole rocker, every part and state (32 elements: the cradle 25, water 1, charge 2, concentrate 2, the sills 2). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/rocker_frame.json` | The static frame only: the two sills (2 elements). The block draws it; everything else rocks. |
| `assets/seraphhorizons/shapes/block/rocker_item.json` | The rocker at rest, dry and empty: the cradle and the sills (27 elements), for the item in hand, on the ground and in the handbook. |
| `assets/seraphhorizons/config/rocker-rig.json` | The cell, the anchors, the rock's figures and the part rig (5 parts). |
| `tests/Rocker/rig-reference.json` | Every part's matrix at 14 values of θ, from the reference maths. |

### How it works

Positions here are in the generator's **build frame**: the native axes (x west to east, y up, z north to
south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame divided
by 16. The parts are chunky on purpose, to read at block scale: none is much under a voxel thick (the
apron's hidden rails are 0.8 square, the concentrate 0.75 deep).

**The sills and the rockers.** Two oak sills lie on the ground across the box (z 2.5..13.5, 1.25 high),
under the rockers at x 2.6 (the foot's) and 12.2 (the head's). Each rocker is an oak top bar under the
floor and a running face of four chords inscribed in a circle of radius 10 about the **rocking axis** (y
11.25, z 8, along x), one corner at the bottom resting on the sill. The rockers stand upright under the
tilted box, so the head's is the taller.

**The cradle.** The box is built level and tilted 3.5 degrees about z, so its floor falls to the foot: an
oak floor 1.5 thick between two oak sides a voxel thick (4.5 over the floor) and a head board, x 1.2..15.4,
z 4..12; the foot is open. Two oak riffles, a voxel wide and 1.5 high, cross the floor in the open part of
the box, at x 2.6 and 5.2 (level frame). The apron, a canvas a voxel thick from side to side on two oak
rails under its edges, falls 20 degrees from under the hopper's foot wall (x 8, its top 0.3 under the
riddle plate) to x 13.4, 2.2 over the floor and a voxel short of the head board: it catches all that falls
through the riddle and drops it on the floor's head, where the water carries it down over the riffles. The
hopper stands on the sides over the head half (x 7.4..15.4, its walls a voxel thick and 2.5 high, flush
with the box's sides); its iron riddle plate, a voxel thick, lies between its walls at their foot, open to
the sky so the bucket can be poured on it. The handle is an oak stick a voxel square against the north
side and the hopper's north wall, x 10.4..11.4, up to y 14.5 (a standing player's hand); it is built
upright and rides the cradle, so it leans with the rock.

**The rock.** θ is the hold-to-work clock, one rock a turn. The cradle swings about the rocking axis by 10
sin θ degrees and slides south by the rockers' radius times that angle, so the rockers roll on the sills
without slipping: the running faces' centre stays on its line at y 11.25 and moves 1.75 each way along z,
and each rocker's lowest point stays on its sill. It leans south at θ = π/2 and north (towards the
operator) at 3π/2, and is level at 0 and π. Everything that rocks rides the cradle; only the sills stand
still.

**The water.** Only from the bucket in the player's hand, poured slowly into the hopper while it is rocked;
nothing connects to the rocker and nothing stands beside it. Only the water in the rocker is modelled (the
owner's ruling): not the bucket or the pour, and not the water leaving over the foot, which is the
outflow's particles if anything. The water passes the riddle plate and the apron at once, so none stands
there; while it is worked it lies a voxel deep over the whole floor, from the head board to the lip of the
open foot, round the riffles. It is one element, a sheet built with the box (falling to the foot with the
floor), its underside 0.75 down in the floor and its edges 0.45 into the sides and the head board. Its
part rides the cradle and swings back by the same angle about the sheet's middle (`swing`, amplitude
−10°), so the two turns cancel: the water is carried as the middle of the floor is, and stays **level**
from side to side while the box and the riffles tip under it. It is deeper on the low side (0.4 to 1.6
over the floor at the sides), and the riffles go under at the low side's edge. The floor is 1.5 thick so
the sheet's underside never shows: it stays inside the floor, 0.13 under its top and 0.16 over its
underside at worst, and its edges stay 0.17 to 0.62 into the sides, over the whole rock. The water's
element is in the Transparent render pass (`renderPass` 3, as the gear cutter's sight glass).

Every moving part (θ the rock):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Cradle: box, rockers, riffles, apron, hopper, riddle plate, handle (`cradle`) | The operator's hand on the handle | Everything that rides it | `swing` x about the rocking axis, amplitude 10°, ratio 1 on θ; `slide` z, amplitude 0.109 blocks (the radius times the swing's angle), ratio 1 on θ |
| Water (`water`) [water] | Carried in the cradle | — | `swing` x about the sheet's middle, amplitude −10°, ratio 1 on θ, then rides the cradle: level, carried |
| Charge (`charge`) [charge], concentrate (`concentrate`) [concentrate] | In the cradle | — | Ride the cradle |
| Sills (`frame`) | Fixed | — | none |

There are no gears or shafts. The rock's visible cause is the operator's hand on the handle, which is
fixed to the box; the box rolls on its rockers, which roll on the sills.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py              # stdlib only; rewrites the five files
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py --quick      # skips the z-fighting fix and the swept rock (not for files that ship)
```

A full run takes about 2 s. `tools/validate_rocker.py` holds the checks. Every run checks its output and
exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part; everything that rocks rides the cradle, the charge and concentrate with no drivers of their own and
  the water with its one swing back; the sills have no ride and no driver.
- **Nothing floats:** the sills on the ground; everything that rocks joined to the rockers' running faces
  at rest.
- **Textures** by role: oak timber, the canvas on the apron only, the riddle plate's iron, water, charge and
  concentrate; the water in the Transparent pass.
- **Containment:** nothing leaves the cell over the rock (every 5 degrees of θ).
- **Rock:** level at 0 and π, 10 degrees south at π/2 and north at 3π/2; the charge and concentrate move
  exactly as the cradle, the water never turns and is carried as its pivot on the cradle is (the pivot the
  generator's), and the sills stand still.
- **Rolling:** the running faces' corners lie on their circle; the rocking axis stays at its height and moves
  the radius times the angle along z; each rocker's lowest point stays on its sill, never below its top and
  never more than a chord's sagitta (0.095) above it, over the sill's length (every degree of θ).
- **Path:** the apron starts under the hopper's foot wall, clear of the plate, falls towards the head and
  ends short of the head board and over the floor; the riffles stand on the floor's run.
- **Handle:** at a hand's height, against the north side.
- **Foot:** open over the floor's lip; the tailings land west of it, on the ground.
- **Water by bucket only:** the rig declares no water (or any other) cell or face, no spout and no part for a
  pipe or a stream; the sills are the only fixed part and stand no higher than their tops; the hopper is open
  to the sky, nothing of the rocker over its riddle plate at any point of the rock (every 10 degrees); the
  points that ride name a part the rig has.
- **Level water** (every 5 degrees of θ, read in the box's own level frame): the sheet's underside inside
  the floor, never showing above it or below it; its edges inside the sides, its head end inside the head
  board and its foot end just inside the lip, never in front of the floor's end; and water at least 0.3
  deep over the floor at the high side.
- **Clearances:** every pair of parts at rest and at both ends of the rock, except the intended contacts in
  `ALLOWED` (the water against the floor, the sides, the head board, the riffles and the apron's low end; the
  load against what it lies on); and (full runs) what rocks against the sills, every 2 degrees of θ.
- **No z-fighting** (full runs): no coplanar overlapping faces at seven poses of the rock.
- **Files:** every texture declared; the frame shape is the sills and the item shape everything but the
  states; a lid over the cell; the operator on the north; no power cell, and no water cell or face; the
  water still in the Transparent pass in the file; the shipped model is the checked one.

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are, from the station only (the cradle and the sills, not the water or the load): one box
over the box, the hopper and the handle, one over each sill and its rocker, and a lid at the top.

### Rig schema (`rocker-rig.json`)

The shared rig format (the bucking sawmill's README, "Rig schema"), with nothing added to the shared
maths: two θ drivers on the cradle, one on the water, and rides.

- **Inputs.** θ alone: no `work`, no `trunkPath`, no class or presence. θ advances while the player holds
  right-click with a full bucket and is that clock: one turn, one rock.
- **`cells`**: one cell `[0,0,0]` with its boxes and a `lid`.
- **Anchors**: `operatorSide`, `hopper`, `outflow`, `concentrate`, `tailings`, `tailingsSide`, as above. A
  point with `part` rides that part (the handcar's grips do the same): gameplay poses it by the part's
  matrix.
- **`rock`**: `degrees` (10), the swing's amplitude, and `radius` (0.625 blocks), the rockers' running faces,
  as drawn; the slide's amplitude is their product in radians. Level at θ = 0 and π.

**Keys.** `cells`, `operatorSide`, `hopper`, `outflow`, `concentrate`, `tailings`, `tailingsSide`, `rock` and
`parts`. `requires` values: `water`, `charge`, `concentrate`, or null. The texture codes a renderer sets:
`charge` and `concentrate` (the material's).

**In the viewer** (`site/models.json`, `rocker`): θ is the "Rocked" slider and Play rocks it at one rock a
second, wet; the states are two selects (**Water**, **Load**) with their own checkboxes.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `cradle_*` (the box, the
rockers, `cradle_riffle1` and `2`, `cradle_apron_*`, `cradle_hopper_wall_*`, `cradle_riddle`,
`cradle_handle`), `water_*`, `charge_*`, `conc_*`, and `fr_*` for the sills. Hand edits are lost when the
script runs again: port them into `make_shape.py`, or stop regenerating.

## For the gameplay (#716)

What the model assumes, for whoever builds the block entity and the renderer:

- **The block and the item.** The block draws `rocker_frame.json`, which is only the sills; the renderer draws
  every rig part with a `requires`, a ride or a driver (as the other machines' renderers do), the cradle
  included. The item (inventory, hand, ground, handbook) is `rocker_item.json`, the whole rocker dry and empty.
- **Working it.** Hold right-click with a full bucket (the quern's and the press brake's hold pattern): θ
  advances, a turn a rock, and the bucket empties slowly into the hopper. When the hold ends, run θ on to the
  next multiple of π so the cradle comes to rest level. The rock is the only motion, so the renderer needs θ
  and the states, nothing else.
- **Water.** Draw `water` while it is worked (rocked, the bucket pouring). The pour itself is the held
  bucket's: particles from the player's bucket to the `hopper` point if anything; the water leaving over the
  foot is splash particles at `outflow`; nothing in the model.
- **The load.** Draw `charge` while a charge is in the hopper and `concentrate` while concentrate lies behind
  the riffles, with their texture codes set to the material's.
- **The concentrate** comes out by hand, as the period cleanup did: the player takes it from the riffles at
  the `concentrate` point (right-click with an empty hand, say). There is no `outputSide`.
- **Tailings** land at `tailings.pos`, the cell west of the rocker, on the ground (#694's layered gravel or
  sand), and splash at `outflow`.

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (every state, several points of the rock, from the
default camera and four others). No one has yet looked at it in a client.

1. **The riddle plate has no holes.** The period plate was sheet iron punched with holes about a
   centimetre across, below a voxel; it wears the game's plain iron plate. The game's rusty iron mesh
   (`game:block/metal/mesh4`, a cut-out texture) was the first pass's choice, but it reads as a window
   screen, shows the apron through it, and whether a cut-out draws in a renderer's pass is unseen.
2. **The water is the game's water texture, untinted,** drawn in the Transparent pass on a moving part;
   neither has been seen in a client. It is a rigid sheet kept level, not a fluid: its depth over the floor
   swings from 0.4 to 1.6 at the sides, and its foot end is a flat face just inside the lip. Nothing falls
   off the foot.
3. **Polygonal rockers.** The running faces are four chords: the box bobs by up to 0.095 voxels as a corner
   passes the sill.
4. **Collision is the rest pose's**: the rock carries the box a little outside its boxes.
5. **Simplified from the period device:** straight sides (period boxes were often wider at the top), two
   riffles in the open (some rockers had a third under the apron), the hopper flush with the box's sides,
   and timbers thicker than the period's inch boards, so they read at block scale.

## Defaults for the owner to overturn

Each of these was open after the first pass; this pass settled it with the default below.

1. **One cell**, not 1 × 2 (above). A second cell would make room for a longer box; the period device does
   not need it.
2. **The orientation:** the operator (and the handle) north, the head east and the foot west. Mirrored
   (head west) is one constant each.
3. **The riddle plate's texture:** plain iron plate (above). The mesh is one constant away.
4. **The rock:** 10 degrees each way, one rock a turn of θ; rolling, so the box shifts 1.75 voxels each way.
5. **The `requires` ids:** only the three states. A hand station is placed whole, so the first pass's
   placeholder build ids (`riffles`, `apron`, `hopper`, `riddle`) are gone and their parts are the cradle's.
6. **The states:** `water`, `charge` and `concentrate` are kept, so the model shows #716's work; drop the
   last two if the renderer will not draw the load.
7. **Where the concentrate goes:** taken out by hand at the riffles (the `concentrate` point); no
   `outputSide`, no container beside it.
