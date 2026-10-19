# Rocker

Part of the Seraph Horizons mod (`../README.md`): a **hand station** of the ore-processing chain (the epic,
#684), the hand concentrator after the gold pan (vanilla's, 45 %) at 55 %. The epic has no global tiers: there
are hand stations, worked by the player, and machines, each one real piece of equipment, and nothing in
between. The rocker is one of the three hand stations with a model, with the riddle (#715) and the amalgam pan
(`../AmalgamPan/`); what replaces it is a separate machine, the sluice (#734, model #735), which gets the same
55 % unattended. A **rocker** (a cradle) of the 1850s gold rush: an open-topped oak box on two curved
rockers, its floor falling gently to its open foot over two riffles; on its head a **hopper** (the riddle box)
whose bottom is an iron **riddle plate** punched with holes; under that an inclined canvas **apron** on a frame
that catches what falls through the riddle and carries it back to the head of the box; an upright **handle**
on its side, by which it is rocked. Crushed ore or placer gravel goes into the hopper and water over it; the
fines wash through the riddle's holes, down the apron and back down the floor, the riffles catch the heavy
concentrate, and the water leaves at the foot with the tailings.

This folder holds the model's generator and its rig (`tools/`), the model issue, #717, and the gameplay
(#716: crushed ore or placer gravel to concentrate at 55 %, tailings by #694) in `Core/` and `Game/`,
described in the mod's README ("Ore processing: the rocker"). "For the gameplay" below is what the model
offers it.

**A hand station, watered by bucket.** It has no mechanical power, no power cell and no oil, and it takes
no water by pipe or from anything else. The player works it by holding right-click on it with a full
bucket in hand: it rocks, and the bucket is slowly poured into the hopper as it does. The rig's θ is that
work, the hold-to-work clock, and unlike on the press brake, the squaring shear and the mandrel station
(whose θ moves nothing) it is the motion itself: one turn of θ is one rock, over to the south and back
over to the north. There is no work quantity: the rig reads θ alone.

**One item, no build stages.** A hand station has no build stages. The rocker is placed whole, as one
item, and its only `requires` values are what it holds: water, a charge and concentrate.

**By hand only: no automated input or output** (the owner's ruling for every hand station). No chute,
hopper or other item transfer puts anything into the rocker or takes anything out of it: the player tips a
charge into its hopper and pours the bucket, and takes the concentrate out of the riffles, by hand. The rig
names no infeed, outfeed or output side, cell or face; the only side is the operator's. The tailings are
not an output either: they fall to the ground in the cell west of the foot, at their point.

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
| `pour` (`part` `cradle`) | `[0.6846, 0.6225, 0.5]` | The middle of the riddle plate's top, riding the cradle: where the player tips a charge in and pours the bucket (sounds, the pour's particles). Named so it is not read as a place for a vanilla hopper: nothing feeds it. |
| `outflow` (`part` `cradle`) | `[0.0693, 0.2404, 0.5]` | The middle of the floor's lip at the open foot, riding the cradle: where water and tailings leave (splash particles). |
| `concentrate` (`part` `cradle`) | `[0.4158, 0.3086, 0.5]` | On the concentrate behind the upper riffle, riding the cradle: where the player takes the concentrate out. |
| `tailings` | `[-0.5, 0, 0.5]` | On the ground in the cell west of the rocker, beyond the open foot: where the tailings fall (one layer of vanilla layered gravel a rock, in the ore's host rock, #694). A point on the ground, not a side for a chute or a container. Provisional until #694's shared tailings code (the heap beside a machine) lands. |
| `rock` | `degrees` 10, `radius` 0.625 | The rock as drawn (not an anchor; the viewer lists it as not drawn). |

There is no `powerCell` or `powerFace`, no water cell or face (nothing connects to it), and no
`infeedSide`, `outputSide` or other transfer side, cell or face: a charge and the water go in by hand at
the top, and the concentrate comes out by hand at the riffles. None of #711's service anchors (chutes, the
water reservoir) apply to a hand station, so none of the rocker's anchors wait on it; only the tailings'
point waits on #694. The first pass's `tailingsSide` is gone too: the tailings' point says where they fall,
and a side would read as a face to take them from.

**Parts** (the rig's parts). With `requires` null, always drawn: the **cradle** (the box: floor, sides,
head board; the two rockers; the riffles; the apron; the hopper and its riddle plate; the handle). There is
no frame: the rockers stand on the ground, the top of the block below, and nothing of the rocker stands
still. Then the three states:

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
nothing from `build/mods`. The wood is debarked oak (`game:block/wood/debarked/oak`), the riddle plate iron
plate (`game:block/metal/plate/iron`, texture code `riddle`), the apron's canvas `game:block/linen`, and the
water vanilla's **water portion** (`game:block/liquid/waterportion`), the texture its barrels and buckets
show water in: it is blue in itself, where the world's water texture (`game:block/liquid/water`, the first
passes' choice) is grey until the climate's water map tints it, which nothing does for a part a renderer
draws. A charge and the concentrate wear the material's texture in the game (the renderer sets their codes);
the shape's own are the viewer's defaults, native gold's nugget texture for both
(`game:item/resource/nugget/nativegold`), which the pack's crushed gold quartz wears, and its gold concentrate
with crushed galena multiplied over it. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/rocker.json` | The whole rocker, every part and state (53 elements: the cradle 48, water 1, charge 2, concentrate 2). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/rocker_item.json` | The rocker at rest, dry and empty: the cradle (48 elements), for the item in hand, on the ground and in the handbook. |
| `assets/seraphhorizons/config/rocker-rig.json` | The cell, the anchors, the rock's figures and the part rig (4 parts). |
| `tests/Rocker/rig-reference.json` | Every part's matrix at 14 values of θ, from the reference maths. |

There is no frame shape (`rocker_frame.json`, which the other machines' blocks draw): with the sills gone,
nothing of the rocker stands still, so a frame shape would be empty.

### How it works

Positions here are in the generator's **build frame**: the native axes (x west to east, y up, z north to
south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame divided
by 16. The parts are chunky on purpose, to read at block scale: none is much under a voxel thick (the
apron's hidden rails are 0.8 square, the concentrate 0.75 deep, the riddle plate's margins half a voxel wide).

**The rockers.** Two oak rockers cross the box at x 2.6 (the foot's) and 12.2 (the head's) and stand on
the ground, the top of the block below (y 0): there are no sills. Each is an oak top bar under the floor and
a running face of eight chords inscribed in a circle of radius 10 about the **rocking axis** (y 10, z 8,
along x), one corner at the bottom resting on the ground. The rockers stand upright under the tilted box,
so the head's is the taller.

**The cradle.** The box is built level and tilted 3.5 degrees about z, so its floor falls to the foot: an
oak floor 1.5 thick between two oak sides a voxel thick (4.5 over the floor) and a head board, x 1.2..15.4,
z 4..12; the foot is open. Two oak riffles, a voxel wide and 1.5 high, cross the floor in the open part of
the box, at x 2.6 and 5.2 (level frame). The apron, a canvas a voxel thick from side to side on two oak
rails under its edges, falls 20 degrees from under the hopper's foot wall (x 8, its top 0.3 under the
riddle plate) to x 13.4, 2.2 over the floor and a voxel short of the head board: it catches what falls
through the riddle and drops it on the floor's head, where the water carries it down over the riffles. The
hopper stands on the sides over the head half (x 7.4..15.4, its walls a voxel thick and 2.5 high, flush
with the box's sides). Its **riddle plate**, iron a voxel thick between its walls at their foot, open to the
sky so the bucket can be poured on it, is punched with nine square holes a voxel across, three each way, half
a voxel of plate against the walls and a voxel between holes: sixteen boxes of plate, the fewest that leave
nine holes open (strips the plate's length, and the short pieces between them in each row of holes). Through
the holes the apron shows below; six of the nine lie over the apron, and the three nearest the head half over
its low end, so what falls through their head halves lands straight on the floor's head. The handle is an oak
stick a voxel square against the north side and the hopper's north wall, x 10.4..11.4, up to y 13.25 (a
standing player's hand); it is built upright and rides the cradle, so it leans with the rock.

**The rock.** θ is the hold-to-work clock, one rock a turn. The cradle swings about the rocking axis by 10
sin θ degrees and slides south by the rockers' radius times that angle, so the rockers roll on the ground
without slipping: the running faces' centre stays on its line at y 10 and moves 1.75 each way along z, and
each rocker's lowest point stays on the ground, at most 0.024 voxels over it as a chord's middle passes (a
chord's sagitta; four chords left 0.095). It leans south at θ = π/2 and north (towards the operator) at 3π/2,
and is level at 0 and π. Everything rides the cradle; nothing stands still.

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
element is in the Transparent render pass (`renderPass` 3, as the gear cutter's sight glass), for a renderer
that draws it in the game's transparent stage; drawn opaque, as the other machines' renderers draw their
parts, the water portion's texture still reads as water, as in a barrel.

Every moving part (θ the rock):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Cradle: box, rockers, riffles, apron, hopper, riddle plate, handle (`cradle`) | The operator's hand on the handle | Everything that rides it | `swing` x about the rocking axis, amplitude 10°, ratio 1 on θ; `slide` z, amplitude 0.109 blocks (the radius times the swing's angle), ratio 1 on θ |
| Water (`water`) [water] | Carried in the cradle | — | `swing` x about the sheet's middle, amplitude −10°, ratio 1 on θ, then rides the cradle: level, carried |
| Charge (`charge`) [charge], concentrate (`concentrate`) [concentrate] | In the cradle | — | Ride the cradle |

There are no gears or shafts. The rock's visible cause is the operator's hand on the handle, which is
fixed to the box; the box rolls on its rockers, which roll on the ground.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Rocker/tools/make_shape.py --quick      # skips the z-fighting fix and the ground check (not for files that ship)
```

A full run takes about 2 s. `tools/validate_rocker.py` holds the checks. Every run checks its output and
exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part; everything that rocks rides the cradle, the charge and concentrate with no drivers of their own and
  the water with its one swing back; no part stands still.
- **Nothing floats:** everything joined to the rockers' running faces, which stand on the ground, at rest.
- **Textures** by role: oak timber, the canvas on the apron only, the riddle plate's iron on its pieces only,
  water, charge and concentrate; the water in the Transparent pass.
- **Containment:** nothing leaves the cell over the rock (every 5 degrees of θ).
- **Rock:** level at 0 and π, 10 degrees south at π/2 and north at 3π/2; the charge and concentrate move
  exactly as the cradle, the water never turns and is carried as its pivot on the cradle is (the pivot the
  generator's).
- **Rolling:** the running faces' corners lie on their circle; the rocking axis stays at its height and moves
  the radius times the angle along z; each rocker's lowest point stays on the ground, never below it and
  never more than a chord's sagitta (0.024) above it, inside the cell (every degree of θ).
- **Path:** the apron starts under the hopper's foot wall, clear of the plate, falls towards the head and
  ends short of the head board and over the floor; the riffles stand on the floor's run.
- **Riddle:** the plate is punched as built: over its whole area, every quarter voxel at half its thickness,
  a point is iron unless it is in one of the nine holes, and nothing of the rocker is in a hole; whatever
  falls through a hole (straight down at rest, from each hole's middle and four corners) first meets the
  apron's canvas or the floor, never a rail, a riffle or a wall.
- **Handle:** at a hand's height, against the north side.
- **Foot:** open over the floor's lip; the tailings land west of it, on the ground.
- **Water by bucket only:** the rig declares no water (or any other) cell or face, no spout and no part for a
  pipe or a stream; nothing of it is fixed, so nothing stands beside it; the hopper is open
  to the sky, nothing of the rocker over its riddle plate at any point of the rock (every 10 degrees); the
  points that ride name a part the rig has.
- **Level water** (every 5 degrees of θ, read in the box's own level frame): the sheet's underside inside
  the floor, never showing above it or below it; its edges inside the sides, its head end inside the head
  board and its foot end just inside the lip, never in front of the floor's end; and water at least 0.3
  deep over the floor at the high side.
- **Clearances:** every pair of parts at rest and at both ends of the rock, except the intended contacts in
  `ALLOWED` (the water against the floor, the sides, the head board, the riffles and the apron's low end; the
  load against what it lies on).
- **Ground** (full runs, every 2 degrees of θ): nothing goes below the ground, and nothing but the rockers
  comes within a voxel of it.
- **No z-fighting** (full runs): no coplanar overlapping face pairs at seven poses of the rock.
- **Files:** every texture declared; the item shape everything but the states; a lid over the cell; the
  operator on the north; no power cell, and no water cell or face; **by hand only**: no side but the
  operator's, no cell or face, and no key beyond the rocker's own (`RIG_KEYS`), so nothing for a chute or a
  hopper; the water still in the Transparent pass in the file; the shipped model is the checked one.

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are, from the station only (the cradle, not the water or the load): one box from the
ground to the handle's top over the box, the hopper, the handle and the rockers (splitting it saves too
little), and a lid at its top.

### Rig schema (`rocker-rig.json`)

The shared rig format (the bucking sawmill's README, "Rig schema"), with nothing added to the shared
maths: two θ drivers on the cradle, one on the water, and rides.

- **Inputs.** θ alone: no `work`, no `trunkPath`, no class or presence. θ advances while the player holds
  right-click with a full bucket and is that clock: one turn, one rock.
- **`cells`**: one cell `[0,0,0]` with its boxes and a `lid`.
- **Anchors**: `operatorSide`, `pour`, `outflow`, `concentrate`, `tailings`, as above; nothing for automated
  input or output. A point with `part` rides that part (the handcar's grips do the same): gameplay poses it
  by the part's matrix.
- **`rock`**: `degrees` (10), the swing's amplitude, and `radius` (0.625 blocks), the rockers' running faces,
  as drawn; the slide's amplitude is their product in radians. Level at θ = 0 and π.

**Keys.** `cells`, `operatorSide`, `pour`, `outflow`, `concentrate`, `tailings`, `rock` and `parts`. `requires`
values: `water`, `charge`, `concentrate`, or null. The texture codes a renderer sets: `charge` and
`concentrate` (the material's).

**In the viewer** (`site/models.json`, `rocker`): θ is the "Rocked" slider and Play rocks it at one rock a
second, wet; the states are two selects (**Water**, **Load**) with their own checkboxes. Two texture selects
show a charge and the concentrate as the game would, in the material's colour (the viewer draws flat colours):
**Charge** (crushed gold quartz, cassiterite, galena, malachite or magnetite, or placer gravel: Wilderlands
Panning's rich gravel, here in granite) and **Concentrate** (the same five ores' concentrate), opening at gold
for both. Each option names the texture the pack's item takes (`crushedore-{ore}-medium`, `concentrate-{ore}`:
the ore's nugget texture, the concentrate's with crushed galena multiplied over it); its colour is that
texture's mean, by alpha, from the game's 1.22.7 files (the concentrate's after the multiply).
`site/test/rocker.test.ts` holds the options to the pack's item types.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `cradle_*` (the box, the
rockers and their `_facet1`..`8`, `cradle_riffle1` and `2`, `cradle_apron_*`, `cradle_hopper_wall_*`, the
riddle plate's `cradle_riddle_strip1`..`4` and `cradle_riddle_web{row}_{1..4}`, `cradle_handle`), `water_*`,
`charge_*` and `conc_*`. Hand edits are lost when the script runs again: port them into `make_shape.py`, or
stop regenerating.

## For the gameplay (#716)

What the model assumes, for whoever builds the block entity and the renderer:

- **The block and the item.** Nothing of the rocker stands still, so there is no frame shape: the block
  draws nothing of its own in the chunk mesh (an empty shape), and the renderer draws every rig part, the
  cradle included (as the other machines' renderers draw their moving parts), at rest when it is not worked.
  The item (inventory, hand, ground, handbook) is `rocker_item.json`, the whole rocker dry and empty.
- **The ground.** The rockers stand on the block below, so the rocker needs a solid block under it to be
  placed (as a chest does), and breaks or drops if that block goes.
- **Working it.** Hold right-click with a full bucket (the quern's and the press brake's hold pattern): θ
  advances, a turn a rock, and the bucket empties slowly into the hopper. When the hold ends, run θ on to the
  next multiple of π so the cradle comes to rest level. The rock is the only motion, so the renderer needs θ
  and the states, nothing else.
- **Water.** Draw `water` while it is worked (rocked, the bucket pouring). The pour itself is the held
  bucket's: particles from the player's bucket to the `pour` point if anything; the water leaving over the
  foot is splash particles at `outflow`; nothing in the model. The water's element alone is in the
  Transparent pass: a renderer drawing in the opaque stage, as the other machines' do, draws it opaque in the
  water portion's texture; one that wants it see-through draws the water part in the transparent stage.
- **The load.** Draw `charge` while a charge is in the hopper and `concentrate` while concentrate lies behind
  the riffles, with their texture codes set to the material's: the charge's item's texture (a crushed ore's
  is the ore's nugget texture; placer gravel's its rock's gravel) and the concentrate item's (the ore's
  nugget texture with crushed galena multiplied over it, `itemtypes/oreprocessing/concentrate.json`). These
  are item textures, so the renderer's texture source must find them (the pickling tub's
  `ContainerTextureSource` over an item stack is one way); the shape's own `game:item/resource/nugget/nativegold`
  is a placeholder.
- **The concentrate** comes out by hand, as the period cleanup did: the player takes it from the riffles at
  the `concentrate` point (right-click with an empty hand, say). There is no `outputSide`.
- **No automated input or output.** Refuse hopper and chute insertion and extraction: the block entity
  offers no inventory to item transfer (vanilla hoppers and chutes, and any mod's), so nothing can put a
  charge in or take concentrate out but the player's hand. Its slots, if it keeps the charge and the
  concentrate in an inventory, are internal. (On the shared machine base the block entity is a
  `BlockEntityContainer`, but its inventory has no slots: the state is fields of its own.)
- **Tailings** fall to the ground at `tailings.pos`, the cell west of the rocker (one layer of vanilla
  layered gravel a rock, in the ore's host rock, #694), and splash at `outflow`; they are placed in the world,
  never handed to a container or a chute.

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (every state, several points of the rock, from the
default camera and four others). No one has yet looked at it in a client.

1. **The riddle plate's holes are coarse.** The period plate was sheet iron punched with holes about a
   centimetre across, many to a row; at block scale it has nine, a voxel (6 cm) across, three each way, so they
   read as holes. The game's rusty iron mesh (`game:block/metal/mesh4`, a cut-out texture) was the first pass's
   choice, but it reads as a window screen, and whether a cut-out draws in a renderer's pass is unseen. A charge
   heaped on the plate covers most of the holes, as it would.
2. **The water is a rigid sheet kept level, not a fluid,** on a moving part: its depth over the floor swings
   from 0.4 to 1.6 at the sides, and its foot end is a flat face just inside the lip. Nothing falls off the
   foot. It now wears the water portion's texture, blue in itself, so it reads as water drawn opaque or not;
   neither has been seen in a client, and the Transparent pass only shows if the renderer draws the water in
   the transparent stage.
3. **Polygonal rockers.** The running faces are eight chords: a rocker's lowest point lifts up to 0.024 voxels
   off the ground as a chord's middle passes (four chords lifted it 0.095). Inscribed chords never go into
   the ground; more would cost two elements a chord for less than a fiftieth of a voxel.
4. **Collision is the rest pose's**: the rock carries the box a little outside its boxes.
5. **Simplified from the period device:** straight sides (period boxes were often wider at the top), two
   riffles in the open (some rockers had a third under the apron), the hopper flush with the box's sides,
   and timbers thicker than the period's inch boards, so they read at block scale.
6. **The viewer's ore colours are flat means.** The Charge and Concentrate selects draw each texture's mean
   colour; the concentrates, crushed galena multiplied over a nugget texture, come out very dark (cassiterite's
   nearly black), as the items are in the game.

## Open for the owner

Defaults this model keeps, each unconfirmed; overturn any of them:

1. **One cell**, not 1 × 2 (above). A second cell would make room for a longer box; the period device does
   not need it.
2. **The orientation:** the operator (and the handle) north, the head east and the foot west (the tailings
   west of the foot). Mirrored (head west) is one constant each.
3. **The rock:** 10 degrees each way, one rock a turn of θ; rolling, so the box shifts 1.75 voxels each way.
4. **The riddle plate:** iron plate punched with nine holes a voxel across (3 × 3), sixteen elements; the
   plain plate of the last pass, or the mesh texture, is one constant away.
5. **The rockers:** eight chords each (sixteen elements for both), the rock's lift off the ground 0.024
   voxels; four would save eight elements and lift 0.095. The model is 53 elements, from the last pass's 30.
6. **The water's texture:** the water portion's (`game:block/liquid/waterportion`), not the world's water
   texture, which would need the climate's tint.
7. **The `requires` ids:** only the three states. A hand station is placed whole, so the first pass's
   placeholder build ids (`riffles`, `apron`, `hopper`, `riddle`) are gone and their parts are the cradle's.
8. **The states:** `water`, `charge` and `concentrate` are kept, so the model shows #716's work; drop the
   last two if the renderer will not draw the load.
9. **The viewer's materials:** gold for the charge and the concentrate when the page opens, from five ores
   and placer gravel; the shape's own placeholder textures are the same gold, where they were granite gravel
   and basalt sand.
10. **Where the concentrate goes:** taken out by hand at the riffles (the `concentrate` point); no
    `outputSide`, no container beside it. This one is now the owner's ruling for every hand station: no
    automated input or output.
