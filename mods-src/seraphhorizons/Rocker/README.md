# Rocker

Part of the Seraph Horizons mod (`../README.md`): the hand tier of the ore-processing chain's
**concentrate** stage (the epic, #684), the hand concentrator after the gold pan. A **rocker** (a cradle)
of the 1850s gold rush: an open-topped oak box on two curved rockers, its floor falling gently to its
open foot over three riffles; on its head a **hopper** (the riddle box) whose bottom is an iron **riddle
plate**; under that an inclined canvas **apron** on a frame that catches what falls through the riddle and
carries it back to the head of the box; an upright **handle** on its side, by which it is rocked. Crushed
ore or placer gravel goes into the hopper and water over it; the fines wash through the riddle, down the
apron and back down the floor, the riffles catch the heavy concentrate, and the water leaves at the foot
with the tailings.

This folder holds the model's generator and its rig (`tools/`); it is the model issue, #717. The
gameplay (#716: crushed ore or placer gravel to concentrate at 55 %, tailings by #694) is not built yet:
"For the gameplay" below is what the model offers it.

**A hand station, watered by bucket.** It has no mechanical power, no power cell and no oil, and it takes
no water by pipe or from anything else. The player works it by holding right-click on it with a full
bucket in hand: it rocks, and the bucket is slowly poured into the hopper as it does. The rig's θ is that
work, the hold-to-work clock, and unlike on the press brake, the squaring shear and the mandrel station
(whose θ moves nothing) it is the motion itself: one turn of θ is one rock, over to the south and back
over to the north. There is no work quantity: the rig reads θ alone.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator;
the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by
this one (no new driver: the rock is a `swing` and a `slide`, both on θ).

## The station

**Footprint.** One cell, the controller `[0,0,0]`. The period cradle is about a metre long and half a
metre wide (40 inches by 16 to 20), which is a block at the game's scale: the box here is 14.8 voxels
(0.93 m) long and 8 (0.5 m) wide, its sides 5 voxels (0.3 m) over the floor, and a block holds it with
its rock (10 degrees each way, the box shifting 1.75 voxels as its rockers roll) and its handle. A second
cell would only add length the device does not have, and a cheap hand station should take one block.
The only neighbour it needs is the tailings' cell on the west.

**Orientation.** In the native, south-facing frame, the player who placed it stands on the north side
looking south (the block's `side` is the way they look, as for the press brake): the box runs across
their view, along x, its **head** (the hopper) east, to their left, and its open **foot** west, to their
right. The **handle** is on the north side, the operator's, beside the hopper they pour into; the
**tailings** leave over the foot into the cell west of it.

**Anchors** (in `assets/seraphhorizons/config/rocker-rig.json`, blocks):

| Key | Where | For |
|---|---|---|
| `hopper` (`part` `cradle`) | `[0.6882, 0.653, 0.5]` | The middle of the riddle plate's top, riding the cradle: where a charge goes in and the bucket is poured (sounds, the pour's particles). |
| `outflow` (`part` `cradle`) | `[0.0346, 0.2812, 0.5]` | The middle of the floor's lip at the open foot, riding the cradle: where water and tailings leave (splash particles). |
| `concentrate` (`part` `cradle`) | `[0.4513, 0.3286, 0.5]` | Behind the second riffle, riding the cradle: the concentrate (cleanup). |
| `tailings`, `tailingsSide` | `[-0.5, 0, 0.5]`, `west` | On the ground in the cell west of the rocker, beyond the open foot: where the tailings land (vanilla layered gravel or sand, #694). |
| `rock` | `degrees` 10, `radius` 0.625 | The rock as drawn (not an anchor; the viewer lists it as not drawn). |

There is no `powerCell` or `powerFace`, no water cell or face (nothing connects to it), and no
`infeedSide` or `outputSide`: a charge and the water go in by hand at the top, and where the concentrate
goes is gameplay's (Open for the owner).

**Parts** (the rig's `requires`). The finished rocker, every part fitted; the build stages are to be worked
out with the owner, so these are ids for them, not an order. With `requires` null, always drawn: the
cradle (the box: floor, sides, head board; the two rockers), the handle, and the frame (the two sills).
Then:

| `requires` | Draws |
|---|---|
| `riffles` | The three oak riffles across the floor |
| `apron` | The apron: two oak rails against the sides, the canvas between them, an oak bar under each end |
| `hopper` | The riddle box's four oak walls, and its cleats over the box's sides |
| `riddle` | The iron riddle plate, the hopper's bottom (texture code `riddle`) |
| `water` | State: water washing through, while it is rocked and the bucket poured |
| `charge` | State: a charge heaped on the riddle plate (texture code `charge`) |
| `concentrate` | State: the heavy sand caught behind the riffles (texture code `concentrate`) |

The three states are drawn like the work on the press brake (its `platelead`): they are `requires` values
the renderer fits from the block entity's state, not items. In the viewer they are two selects, **Water**
(Dry: at rest; Wet: rocked while water is poured) and **Load** (empty; a charge in the hopper; concentrate
behind the riffles; both). Water opens Wet, since the water is only there while it is worked and Play rocks
it (a play script cannot change a state); pick Dry to see it at rest. Load opens empty.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The wood is debarked oak (`game:block/wood/debarked/oak`), the iron
`game:block/metal/plate/iron`, the riddle plate the game's rusty iron mesh (`game:block/metal/mesh4`, a
cut-out texture: the apron shows through it), the apron's canvas `game:block/linen`, the water
`game:block/liquid/water` (as the pickling tub's), a charge granite gravel
(`game:block/stone/gravel/granite`) and the concentrate basalt sand (`game:block/stone/sand/basalt`), both
for the renderer to replace with the material's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/rocker.json` | The whole rocker, every part and state (57 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/rocker_frame.json` | The static frame only: the two sills (2 elements). The block draws it; everything else rocks. |
| `assets/seraphhorizons/config/rocker-rig.json` | The cell, the anchors, the rock's figures and the part rig (10 parts). |
| `tests/Rocker/rig-reference.json` | Every part's matrix at 14 values of θ, from the reference maths. |

### How it works

Positions here are in the generator's **build frame**: the native axes (x west to east, y up, z north to
south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame divided
by 16.

**The sills and the rockers.** Two oak sills lie on the ground across the box (z 2.5..13.5, 1.25 high),
under the rockers at x 2.6 (the foot's) and 12.2 (the head's). Each rocker is an oak top bar under the
floor and a running face of six chords inscribed in a circle of radius 10 about the **rocking axis** (y
11.25, z 8, along x), one corner at the bottom resting on the sill. The rockers stand upright under the
tilted box, so the head's is the taller.

**The cradle.** The box is built level and tilted 3.5 degrees about z, so its floor falls to the foot: an
oak floor (0.75 thick) between two oak sides (0.6 thick, 5 over the floor) and a head board, x 0.6..15.4,
z 4..12; the foot is open. Three oak riffles (0.6 by 0.6) cross the floor at x 3.0, 6.2 and 9.6 (level
frame): two in the open, the third under the apron. The apron's rails lie against the sides from under the
hopper's foot wall (x 7.5, its canvas top 0.3 under the riddle plate) down to x 14.2, 1.5 over the floor
and 0.6 short of the head board, 25.5 degrees: it catches all that falls through the riddle and drops it on
the floor's head, where the water carries it down over the riffles. The hopper stands on the sides over the
head half (x 7.4..15.4, its walls 2.5 high), located by four cleats over the sides' outer faces; its iron
riddle plate lies between its walls on the sides' tops, open to the sky so the bucket can be poured on it.
The handle is an oak stick against the north side and the hopper's north wall, x 10.4..11.4, up to y 14.5
(a standing player's hand), held by two iron bands; it is built upright and rides the cradle, so it leans
with the rock.

**The rock.** θ is the hold-to-work clock, one rock a turn. The cradle swings about the rocking axis by 10
sin θ degrees and slides south by the rockers' radius times that angle, so the rockers roll on the sills
without slipping: the running faces' centre stays on its line at y 11.25 and moves 1.75 each way along z,
and each rocker's lowest point stays on its sill. It leans south at θ = π/2 and north (towards the
operator) at 3π/2, and is level at 0 and π. Everything that rocks rides the cradle; only the sills stand
still.

**The water.** Only from the bucket in the player's hand, poured slowly into the hopper while it is rocked;
nothing connects to the rocker and nothing stands beside it. Neither the bucket nor the pour is modelled
(the owner's ruling: the water state is only the water in and on the rocker). While it is worked, water
stands 0.5 deep on the riddle plate, runs down the apron and off its low end, runs down the floor as a
sheet with a pool behind each riffle, and falls off the open foot as a curtain. The water's elements are in
the Transparent render pass (`renderPass` 3, as the gear cutter's sight glass), and ride the cradle, so
they tilt with it.

Every moving part (θ the rock):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Cradle: floor, sides, head board, rockers (`cradle`) | The handle | Everything that rides it | `swing` x about the rocking axis, amplitude 10°, ratio 1 on θ; `slide` z, amplitude 0.109 blocks (the radius times the swing's angle), ratio 1 on θ |
| Handle: stick, bands (`handle`) | The operator's hand | The cradle | Rides the cradle |
| Riffles (`riffles`) [riffles] | Fixed to the floor | — | Ride the cradle |
| Apron (`apron`) [apron] | Fixed to the sides | — | Rides the cradle |
| Hopper (`hopper`) [hopper], riddle plate (`riddle`) [riddle] | Sitting on the sides | — | Ride the cradle |
| Water (`water`) [water], charge (`charge`) [charge], concentrate (`concentrate`) [concentrate] | In the cradle | — | Ride the cradle |
| Sills (`frame`) | Fixed | — | none |

There are no gears or shafts. The rock's visible cause is the operator's hand on the handle, which is
fixed to the box; the box rolls on its rockers, which roll on the sills.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py --quick      # skips the z-fighting fix and the swept rock (not for files that ship)
```

A full run takes about 2 s. `tools/validate_rocker.py` holds the checks. Every run checks its output and
exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part; everything that rocks rides the cradle, and the sills have no ride and no driver.
- **Nothing floats:** the sills on the ground; everything that rocks joined to the rockers' running faces
  at rest.
- **Textures** by role: oak timber, iron bands, the canvas, the riddle plate's mesh, water, charge and
  concentrate; every water element in the Transparent pass.
- **Containment:** nothing leaves the cell over the rock (every 5 degrees of θ).
- **Rock:** level at 0 and π, 10 degrees south at π/2 and north at 3π/2; every rocking part moves exactly as
  the cradle, and the sills stand still.
- **Rolling:** the running faces' corners lie on their circle; the rocking axis stays at its height and moves
  the radius times the angle along z; each rocker's lowest point stays on its sill, never below its top and
  never more than a chord's sagitta (0.042) above it, over the sill's length (every degree of θ).
- **Path:** the apron starts under the hopper's foot wall, clear of the plate, falls towards the head and
  ends short of the head board and over the floor; the riffles stand on the floor's run.
- **Handle:** at a hand's height, against the north side.
- **Foot:** open over the floor's lip; the tailings land west of it, on the ground.
- **Water by bucket only:** the rig declares no water (or any other) cell or face, no spout and no part for a
  pipe or a stream; the sills are the only fixed part and stand no higher than their tops; the hopper is open
  to the sky, nothing of the rocker over its riddle plate at any point of the rock (every 10 degrees); the
  points that ride name a part the rig has.
- **Clearances:** every pair of parts at rest and at both ends of the rock, except the intended contacts in
  `ALLOWED`; and (full runs) what rocks against the sills, every 2 degrees of θ.
- **No z-fighting** (full runs): no coplanar overlapping faces at seven poses of the rock.
- **Files:** every texture declared; a lid over the cell; no power cell, and no water cell or face; the
  water still in the Transparent pass in the file; the shipped model is the checked one.

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are: one box over most of the cell, and a lid at its top.

### Rig schema (`rocker-rig.json`)

The shared rig format (the bucking sawmill's README, "Rig schema"), with nothing added to the shared
maths: two θ drivers on the cradle and rides.

- **Inputs.** θ alone: no `work`, no `trunkPath`, no class or presence. θ advances while the player holds
  right-click with a full bucket and is that clock: one turn, one rock.
- **`cells`**: one cell `[0,0,0]` with its boxes and a `lid`.
- **Anchors**: `hopper`, `outflow`, `concentrate`, `tailings`, `tailingsSide`, as above. A point with `part`
  rides that part (the handcar's grips do the same): gameplay poses it by the part's matrix.
- **`rock`**: `degrees` (10), the swing's amplitude, and `radius` (0.625 blocks), the rockers' running faces,
  as drawn; the slide's amplitude is their product in radians. Level at θ = 0 and π.

**Keys.** `cells`, `hopper`, `outflow`, `concentrate`, `tailings`, `tailingsSide`, `rock` and `parts`.
`requires` values: `riffles`, `apron`, `hopper`, `riddle`, `water`, `charge`, `concentrate`, or null. The
texture codes a renderer sets: `charge` and `concentrate` (the material's).

**In the viewer** (`site/models.json`, `rocker`): θ is the "Rocked" slider and Play rocks it at one rock a
second, wet; the fitted parts are checkboxes, and the states two selects (**Water**, **Load**) with their
own checkboxes.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `cradle_*`, `handle_*`,
`riffle_*`, `apron_*`, `hopper_*`, `riddle_*`, `water_*`, `charge_*`, `conc_*`, and `fr_*` for the sills.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## For the gameplay (#716)

What the model assumes, for whoever builds the block entity and the renderer:

- **The block and the item.** The block draws `rocker_frame.json`, which is only the sills; the renderer draws
  every rig part with a `requires`, a ride or a driver (as the other machines' renderers do), the cradle
  included. The item form (inventory, hand, ground) needs the whole rocker, dry and empty: `rocker.json`
  with the states' elements left out (`water_*`, `charge_*`, `conc_*`), or a third shape the generator could
  write.
- **Working it.** Hold right-click with a full bucket (the quern's and the press brake's hold pattern): θ
  advances, a turn a rock, and the bucket empties slowly into the hopper. When the hold ends, run θ on to the
  next multiple of π so the cradle comes to rest level. The rock is the only motion, so the renderer needs θ
  and the fitted parts and states, nothing else.
- **Water.** Draw `water` while it is worked (rocked, the bucket pouring). The pour itself is the held
  bucket's: particles from the player's bucket to the `hopper` point if anything; nothing in the model.
- **The load.** Draw `charge` while a charge is in the hopper and `concentrate` while concentrate lies behind
  the riffles, with their texture codes set to the material's.
- **Tailings** land at `tailings.pos`, the cell west of the rocker, on the ground (#694's layered gravel or
  sand), and splash at `outflow`.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in
a client.

1. **The riddle plate is a mesh.** The period plate was a sheet punched with holes about a centimetre
   across; the game has no punched-plate texture, so it wears the rusty iron mesh, cut out, which reads as a
   screen and shows the apron through it. Whether the cut-out draws in the renderer's pass is unseen.
2. **The water is the game's grey water texture, untinted,** and drawn in the Transparent pass on a moving
   part; neither has been seen in a client. The water tilts with the cradle (a film on the floor, not a
   level surface), and the curtain off the foot rocks with the box.
3. **Polygonal rockers.** The running faces are six chords: the box bobs by up to 0.042 voxels as a corner
   passes the sill.
4. **Collision is the rest pose's**: one box over the cell; the rock carries the box a little outside it.
5. **Straight sides.** The period box was often wider at the top than at the floor; this one's sides are
   upright.

## Open for the owner

1. **One cell**, not 1 × 2 (above). A second cell would make room for a longer box; the period device does
   not need it.
2. **The orientation:** the operator (and the handle) north, the head east and the foot west. Mirrored
   (head west) is one constant each.
3. **The riddle plate's texture:** the mesh (above), or plain iron plate.
4. **The rock:** 10 degrees each way, one rock a turn of θ; rolling, so the box shifts 1.75 voxels each way.
5. **The `requires` ids:** `riffles`, `apron`, `hopper`, `riddle`, with the cradle, the handle and the sills
   always drawn; the build stages and their items are still to be worked out.
6. **The states:** `water` (asked for), and `charge` and `concentrate`, added so the model can show #716's
   work; drop them if the renderer will not draw the load.
7. **Where the concentrate goes** (cleaned up by hand from the riffles, or into a container beside it): the
   rig has only the `concentrate` point, and no `outputSide`.
