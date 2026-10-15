# Parkes kettle: the model

Part of the Seraph Horizons mod's ore processing (epic #684): the generated model of the **Parkes kettle** (#928), the
desilverizing kettle of the parting stage, whose behaviour is #927. The line, the glossary and the period sources are
in #939, the power and water budget in #940.

**Status: model only, second review pass** (v3). This folder's `tools/` holds the model's generator and its checks; there is
no gameplay yet (#927). The model follows the crucible furnace's conventions (`../CrucibleFurnace/README.md`; its
generator is this one's template) and salvages the stirrer's mechanism from the parting furnace's v2 branch
(`741-parting-furnace-model-v2`, see "Salvage"). The generic half of the generator is `Machines/tools/machinegen/`,
unchanged by this model: no new driver and no change to the package.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour.

## The kettle

A cast-iron kettle of silver-lead in a brick setting over its own fire. Zinc stirred into the molten lead takes up
its silver and rises as a crust that is skimmed off by hand; the desilverized lead under it is ladled out by hand
(#927: "Batch. Lead and zinc in by hand; the crust skimmed by hand; the lead out by hand"). The model is one such
kettle, built as one machine: a block of brickwork **three blocks wide (x), three deep (z) and two high**, bound with
**angle iron**, any one type of fired brick with **tier-3 refractory lining 3 voxels thick** on every hot face, as the
crucible furnace's.

- **The binding** is angle iron, the owner's rule for every brick structure: an L section that wraps the brickwork's
  corner, so it locks the brick in on both faces. At each of the four vertical corners an **angle** runs from the
  ground to the floor, one leg on the front (or back) face and one on the side face, each 1 thick and 2.5 from the
  corner, its inside corner bedded against the brick's outside corner (the brick is notched for it, so its legs lie
  flush with the faces). Along the top edge of every face (front, back and both sides) an **angle band** wraps that edge, one leg
  down the face and one on the top (flush with the floor), and every band end lands on an upright's head, so each
  corner is a closed joint: the front's and the back's face legs butt the corner angles' face legs and their top legs
  run on over the corner to the angles' side legs; the side bands' face legs butt the angles' side legs and their top
  legs the front's and back's top legs. The top of each corner is iron all over, 2.5 by 2.5, and the binding is one
  connected frame (four uprights and a ring round the top). The side bands lie flush, like the angles' side legs, so a
  row's neighbours still meet face to face. No flat strap is left.

- **The kettle** hangs by its flange in the setting, **centred left to right in the front two rows** (its axis at
  x 24, z 20 in the build frame): a hemispherical bowl 24 voxels across inside (a faceted hemisphere: three bands of
  eight flat facets and a flat bottom, cast iron 1 thick) and an octagonal **flange 28 across** round its rim. The
  flange lies on the lining's top courses round the bowl (**the seat**) and is the floor: its top stands 0.15 over
  the setting's top (bedded in the seat's brick, it would z-fight the brick under its rim if flush). The bowl's
  inside, from its bottom (y 19.9) to its rim (y 31), holds 3,312 units at the crucible furnace's 1.1 cubic voxels a
  unit; **a charge of 2,400 units** (#927) stands 2.2 voxels under the rim.
- **The fire** burns under the kettle on a **grate** of iron firebars (ten rows, two lengths a row, on three
  bearers built into the walls) over an **ash pit**. The setting's inside is a square shaft round the bowl from the
  grate up to the seat: the flame rises round the kettle in it and leaves near the top of its back wall by a **flue
  opening**, along a **back flue** to a **stack** in the back row's east corner, and out at the **outlet** on the top
  there. The model stops at the outlet: the player stands the chimney on it, or a flat iron **cap** set flush in its
  curb, as on every kettle in a row but the one carrying the chimney.
- **The front**, the stoker's: a **fire door** (an iron plate lifting in iron guides in a recess, over the fire
  door's opening above the grate, where coal goes in by hand) and under it the **ash mouth**, where the air comes in,
  with an iron **damper** sliding sideways along iron rails across it in three places: **open** (slid east, clear of
  the mouth), **half** (over its east half) and **shut** (over all of it). **Fuel** (#927's ruling): coal, **6 to a
  2,400-unit charge**; the grate holds **8**, in two layers of four (`coalPerCharge`, `fuelLayers`, `fuelPerLayer`). Both are set in a cast-iron front built into
  the brick behind the recesses. Natural draught only.
- **The stirrer's gallows** stands over the kettle: two cast-iron **plate standards** on feet on the floor, west and
  east of the kettle, joined by three **beams** across it. The lower and the upper beams carry the stirrer's vertical
  shaft in bosses; the middle beam carries the drive shaft's front end. The stirrer **hangs** from the upper boss by a
  collar; at its foot two **flat paddles** cross in the bath, in two tiers that follow the bowl (5.6 and 8.6 from the
  axis).
- **The drive.** The axle comes in **at the back, at the gallows' height** (the power cell is the back middle cell
  over the setting, its face south; the axle's line is x 24, y 40, the cell's face centre). Its oak continuation (the
  vanilla axle's cross profile) meets the face; a steel **drive shaft** runs forward along z in two bearings (a
  **pedestal** on the back row and the gallows' middle beam), located by collars either side of the pedestal, to a
  **bevel pinion** that meshes with a **mitre wheel** on the stirrer's shaft under it: 8 teeth each, module 1.0
  (the owner's ruling, 2026-10-14, so the pair reads as machined; v2's were 6 at 1.2), so the stirrer turns at the
  axle's speed, ratio 1 as before. The lower beam sits 0.3 lower than in the first pass so the wheel's back clears
  its boss; the wheel's tooth tips reach 0.35 into the hollow cell over the kettle's mouth (the machine's own cell). **No rectifier:** flat paddles stir alike either way (#927, rule 3).
- **Kettles stand in a row**, side by side along their width (x), sharing walls: the side faces are flush, brickwork
  but for the corner angles' side legs set flush in them, with nothing beyond the footprint, so two settings' side
  walls meet (their corner angles and side bands back to back) and touch nowhere else, the stirrers turning. A row shares one chimney; the flues between the settings are
  implied, not modelled. A row's axles come from a line shaft along x behind it, at 2.5 blocks up, an angled gear
  turning into each kettle's power face.

**Footprint and cells.** The setting's **18 cells**, and over it **four cells** for the gallows and the drive (the
middle row's three, where the standards, the beams and the gears stand, and the back middle one, the power cell, with
the pedestal), and **three hollow cells** over the kettle's mouth (the front row's: the machine's, with no boxes of
their own, so nothing is built over the kettle where the lead and zinc go in and the crust and lead come out, and the
flange's proud top is in its own cells): **25 cells**. The cell over the outlet, the back east one, is the
chimney's, and the back west one is free: neither is the machine's. The **controller is the front middle cell at
ground level**, `[0,0,0]`, the fire's front, by the crucible furnace's convention; the setting extends a block to each
side (x) and two back (z) from it, away from the player.

**Why 3 × 2 × 3 (and a third layer over it).**

- **Three wide:** the kettle about two blocks across (#928) in a setting three wide: the flange 28 across with 10 of
  brick each side, the gallows' standards beside it.
- **Three deep:** the kettle in the front two rows, its flange's back edge clear of the chimney's cell behind it (the
  outlet's cell's top is the chimney's, a full block standing over the back east corner), and the back row for the
  back flue, the stack and the outlet, and the drive's pedestal. The front wall is 6.5 thick (5.5 behind the doors'
  recesses).
- **Two high:** the setting, with the flange at the floor (the crucible furnace's word for a setting's top): the ash
  pit (6), the grate (3), the fire under the bowl (about 9) and the bowl (12). A kettle two blocks across with a fire
  under it does not fit in one block.
- **Over it:** the gallows, as the crucible furnace's lid stands open in the cells over its top: four cells, and the
  three hollow ones over the kettle's mouth.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `powerCell`, `powerFace` | `[0,2,2]`, `south` | The axle: the back middle cell over the setting, at the gallows' height; its face centre is the drive's line |
| `kettleCell`, `kettleFace` | `[0,1,1]`, `up` | The cell under the kettle's mouth's middle, worked from its top: lead and zinc in, the crust skimmed, the lead ladled out |
| `mouth` | `[0.5,2,1.25]` | The middle of the kettle's mouth at the floor: the bath's sound and fumes |
| `fireDoor`, `fireDoorSide` | `[0.5,0.875,0]`, `north` | The middle of the fire door in the controller's front face: coal in, by hand |
| `ash`, `ashSide` | `[0.5,0.2188,0]`, `north` | The middle of the ash mouth in the controller's front face: the damper |
| `outletCell`, `outletFace` | `[1,1,2]`, `up` | The stack's top cell, in the back row's east corner; the chimney (or the cap) on its top |
| `chimney` | origin `[1.4375,5,2.4375]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `build` | | The build's stages and their materials ("The build", below); not drawn |
| `bricks` | | The fired brick types and their textures, the crucible furnace's ("The brick", below); not drawn |
| `furnace` | | `chargeUnits` 2400, `leadLevels` 4, `fuelLayers` 2, `fuelPerLayer` 4, `coalPerCharge` 6, `minChimney` 6, `rowMax` 5, `rowAxis` `x`, `stirRatio` 1, `fireDoorLift` 0.45; not drawn |

No air port, no water and **no chute anchor**: the kettle is batch, loaded and emptied by hand (#928, rule 2).

**Decks, boxes.** Each of the nine top cells of the setting has a `lid` (a collision deck) at the floor: a player
walks over the kettle's mouth and cannot fall in, as over the crucible furnace's hole. The cells' boxes are built from
the machine as built (the iron, the brick, the kettle, the fire door shut, the gallows, the drive and the stirrer at
rest); the three cells over the kettle's mouth are hollow.

## The build (proposed)

The issue leaves the build to be settled with the owner while the kettle is modelled (#928, "Open"). As a starting
point, the crucible furnace's pattern: the iron frame, then four stages, each a `requires` value (the rig's `build`, in
order), and the viewer's **Build** select steps through them:

| | Stage | `requires` | What it puts in | Materials |
|---|---|---|---|---|
| Frame | **Iron frame**: a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (the frame part) | The binding's corner angles and top angle bands, the grate's bearers and firebars on three short posts (hidden in the brickwork), the cast-iron front with the damper's rails and the fire door's guides | 2 iron plates, 4 iron rods, 10 iron nails and strips (the crucible furnace's, unchanged: to settle) |
| 1 | **Brickwork** | `brickwork` | The base, the ash pit and the ash mouth, the outer walls: 9.51 blocks | **20 fired bricks**, any one type (`game:burnedbrick-*`) |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on the hot faces (round the fire and the kettle, the seat the flange lies on, the flue, the stack and the outlet's curb): 2.97 blocks | **12 tier-3 refractory bricks**, **5 fire clay** |
| 3 | **Kettle and doors** | `kettle` (and the `firedoor` part, and the damper's places) | The cast-iron kettle let down into its seat; the fire door; the damper | 6 iron plates (a proposal: the kettle's iron measures 0.39 blocks) |
| 4 | **Stirrer** | `stirrer` (and the `gallows` and `drive` parts) | The gallows, the pedestal, the drive shaft and its pinion, the stirrer with its mitre wheel and paddles | 4 iron plates, 4 iron rods, 4 iron nails and strips (a proposal: the gallows measure 0.31 blocks of iron, the drive and the stirrer 0.15) |
| | *Outlet cap* | `outletcap` | Not a stage: put on (or taken off) by hand, any time after the lining | 1 iron plate |

**The counts of brick are measured from the model**, as the crucible furnace's: the generator builds the brick as a
lattice and merges it into boxes, the brickwork's and the lining's apart; at 2 fired bricks a block and 4 refractory
bricks a block, rounded up, fire clay one per 2.5 refractory bricks. The kettle's contents, the fire and the damper
are drawn only once the kettle stage is in, the cap only once the lining (its curb) is.

### The brick

**Any fired brick**, exactly as the crucible furnace's (`../CrucibleFurnace/README.md`, "The brick"): the first brick
put in sets the setting's type, and the renderer sets the shape's `brick` code to that type's block texture from the
rig's `bricks`, which are the crucible furnace's (a test holds them equal). In the viewer, a **Brick** select.

### The chimney

Counted **from the outlet up**, as the crucible furnace's: at least six shaft blocks (`furnace.minChimney`) stood on
the outlet, topped by one chimney top. The viewer's `chimney` line draws the six from the outlet (y 2 to 8).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. **No new texture**: every texture is the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/parkeskettle.json` | The whole machine, every part |
| `assets/seraphhorizons/shapes/block/parkeskettle_frame.json` | The iron frame only (55 elements): what the placed frame draws, before any stage |
| `assets/seraphhorizons/config/parkeskettle-rig.json` | Cells, anchors, the build and the brick types, and the part rig (22 parts) |
| `tests/ParkesKettle/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

**Textures** (all the game's):

| Code | Texture | On |
|---|---|---|
| `brick` | The setting's brick type's block texture, set by the renderer (`bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork: world-mapped, so the many boxes read as one wall |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining, the seat, the outlet's curb: world-mapped |
| `iron` | `game:block/metal/plate/iron` | The kettle (cast iron), the iron frame, the gallows and its pedestal, the fire door, the damper, the cap, the paddles |
| `steel` | `game:block/metal/sheet-plain/steel1` (the draw bench's) | The drive shaft, its collars and hub, the pinion; the stirrer's shaft, collar and hub, the mitre wheel |
| `oak` | `game:block/wood/debarked/oak` | The axle's oak continuation at the power face |
| `lead` | `game:block/metal/ingot/lead` | The bath, at its four levels |
| `zinc` | `game:block/metal/ingot/zinc` | The zinc stirred in |
| `crust` | `game:block/metal/ingot/zinc` | The crust (zinc, carrying the silver, and lead) |
| `fuel` | `game:block/coal/bituminous` | The coal on the grate |
| `ember` | `game:block/coal/ember` | The same coal, burning; its faces glow 200 |

### How the kettle works

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The iron frame (`frame`, no requires) | fixed: placed as the machine | carries the grate; its angle-iron binding locks the brick in at its corners and top edges; its front carries the doors' rails and guides |
| The brickwork (`brickwork`), the lining (`lining`) | stages 1 and 2 | the setting; the lining's top courses are the seat the flange lies on |
| The kettle (`kettle`) | stage 3: let down into its seat | holds the bath |
| The fire door (`firedoor`, with the kettle) | depth: the stoker, lifting it in its guides (0 shut, 1 open, 7.2 voxels up) | lets coal in onto the grate |
| The damper (`damperopen`, `damperhalf`, `dampershut`, with the kettle) | the stoker: one of its three places on its rails | sets the draught at the ash mouth |
| The coal (`fuel1`, `fuel2`) and the same burning (`fire1`, `fire2`) | gameplay's coal on the grate, `ceil(coal / fuelPerLayer)` layers | heats the bowl from under it |
| The axle (no part: the vanilla axle at `powerCell`'s south face) | the player's power | the drive shaft |
| The drive (`drive`, with the stirrer): the oak continuation, the drive shaft, its collars, the bevel pinion | θ, about z (ratio 1), in the pedestal's bearing and the gallows' middle boss | the mitre wheel |
| The stirrer (`stirrer`): its shaft, the mitre wheel, the collar, the two flat paddles | the pinion through the mitre pair: θ about y (ratio 1), hanging from the upper boss by its collar, turning in the lower boss | stirs the bath; flat paddles stir alike either way, so no rectifier |
| The gallows (`gallows`, with the stirrer): the standards, the three beams and their bosses, the pedestal | fixed, on the floor | carries the stirrer and the drive |
| The bath (`lead1`..`lead4`) | gameplay's charge: `ceil(fill × leadLevels)`, each a quarter of a charge by volume | the stirrer's load follows it (#927): the paddles' foot reaches a quarter-full bath, their top breaks a full one |
| The zinc (`zinc`), the crust (`crust`) | the zinc put in by hand and stirred; the crust risen, skimmed by hand | (drawn on the full bath) |
| The cap (`outletcap`) | fitted on every kettle in a row but the one carrying the chimney | closes the outlet |
| The draught (no part) | the chimney on the outlet, natural draught only | in at the ash mouth, up through the grate and the fire, round the bowl, out at the flue opening, along the back flue, up the stack and the chimney |

**The bath's levels.** A charge is 2,400 units (#927), drawn at the crucible furnace's 1.1 cubic voxels a unit: the
bowl's inside, measured from its facets' inner faces, holds a quarter (600 units) to y 23.60, a half to 25.64, three
quarters to 27.28 and a charge to 28.82, 2.2 under the rim. Each level is a surface layer 0.5 thick, a hundredth into
the wall. The zinc floats on the full bath beyond the paddles' reach, half sunk; the crust (0.7 thick, with lumps)
covers it.

### Regenerating

```sh
python3 mods-src/seraphhorizons/ParkesKettle/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/ParkesKettle/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/ParkesKettle/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run is deterministic (two runs into two folders are byte-identical). It exits non-zero if a check fails
(`tools/validate_parkeskettle.py`). The brick types are checked against the game's files when they are at hand
(`$VINTAGE_STORY`, else `~/Games/vintagestory`). The checks:

- **Parts:** every element in its intended part; no part without elements; the moving parts are the drive, the
  stirrer and the fire door; Euler angles round-trip.
- **The stages:** at each stage (the iron frame alone, with the brickwork, the lining, the kettle and its doors, the
  stirrer) nothing floats: every element drawn is joined to the ground (the gears' teeth run into their bodies, the
  stirrer's collar sits on its boss). The iron frame is iron only, with no thin post over half the setting; its ties
  are hidden in the brickwork.
- **Textures by role:** brick, lining, iron, steel, oak, lead, zinc, crust, fuel and ember where the table puts them.
- **Containment:** over a turn of the axle either way and the fire door's whole lift, nothing leaves the setting and
  the cells over it that are the machine's (tested through the volume of every turned element), so nothing reaches
  the chimney's cell or the free cell beside it; over the floor stand only the gallows, the drive, the stirrer and the
  flange's 0.15.
- **A flat top:** every half voxel of the top, but the kettle's mouth and the outlet's cap seat, is brick whose top is
  the floor, or the flange.
- **A row:** each side face is flush, brickwork over its whole area but for the binding's flush side legs (the corner angles' and the side
  bands'), nothing else
  reaches either side plane in any pose, and two settings side by side touch nowhere but at their shared walls, the
  stirrers turning.
- **The binding is angle iron** (the owner's rule): no flat strap or band; at each vertical corner an angle from the
  ground to the floor whose section is iron along both legs (1 thick, 2.5 from the corner) and brick beyond; the brick
  behind each leg's inside face and at its inside corner, sampled up its height, so both legs bear on the corner's two
  brick faces; along the top edge of every face an angle band wrapping the edge from corner to corner, the brick behind
  both its legs and at its inside corner.
- **The binding is one closed frame** (the owner's rule that members meet): every end face of every band leg lies
  on iron over its whole section, and at each end of each band at least one leg lands on an upright; the top of each
  corner is iron all over, leg by leg; and the binding's pieces, joined wherever two touch over an area, are one
  connected frame. (Run on the previous geometry, whose top legs stopped short of the corners, this check fails.)
- **Centred, walls:** the kettle, the ash mouth, the fire door and its recess, the setting's inside and the iron front
  are centred left to right; the brick round the setting's inside is at least `MIN_WALL` (5.5) to the faces.
- **The lining:** behind every hot face (the setting's inside from the grate to the seat, the flue opening, the back
  flue and the stack) the brick is the lining's for at least 3, or to where the brick ends; the ash pit's and the ash
  mouth's faces are the brickwork's.
- **The stages' counts:** the brick boxes' volumes add up to the lattice's and give the rig's quantities at the game's
  rates; the stages are the build's and fit their parts.
- **The kettle:** the bowl closed (10,800 rays from the rim's centre, every 1.5 degrees down and 2 round, all meet an
  element of it); each level of the bath holds its quarter of a charge, measured from the facets' inner faces, within
  2 %, the full bath at least a voxel under the rim; the bath, the zinc and the crust inside the bowl; the flange from
  the rim to 0.15 over the floor, lying on the seat's brick all round (72 points under its edge); the seat's opening
  (a stepped octagon, as the lattice is square to the axes) holds the bowl's top and stays inside the flange; the bowl
  off the setting's walls and over the coal; the flange clear of the chimney's cell.
- **Gearing:** the mitre pair's axes cross at its apex, the pitch radii are in the teeth's ratio, and the pitch point
  moves alike on both (finite differences of the posed rig, both ways round): no slip. Each gear has its count of
  teeth, and posed every 7.5 degrees either way a tooth of each stands half a pitch from the other's nearest at the
  pitch point: the teeth interleave rather than meet tip to tip (a wheel set tooth to tooth fails it, 22.5 degrees off).
- **Fuel:** the rig's `furnace` keys give #927's ruling: 6 coal to a 2,400-unit charge, the grate holding 8.
- **Supports:** the drive shaft in two bearings (the pedestal's, the middle boss), the stirrer's shaft in two (the
  lower and the upper bosses), each enclosing it at rest and turned 45 degrees; the stirrer hanging from the upper boss
  by its collar; the oak continuation meeting the power face at its centre.
- **Reverse:** every driver reads θ alone (nothing reads the shaft's travel); the paddles symmetric about the shaft;
  the clearances hold turning backwards.
- **Paddles:** they reach a voxel into the quarter-full bath and break the full one's surface; the zinc floats beyond
  their reach.
- **The doors:** the fire door shut over its opening, lifted clear of it, between its guides in its recess over its
  whole lift; the damper in each place on its rails in its recess, covering the whole ash mouth, its east half, or
  none of it.
- **The outlet:** the cap flush in the curb's seat over the stack's mouth; the curb the lining's brick; the outlet in
  the cell under the chimney's.
- **Draught:** sampled points from the ash mouth through the ash pit, up every gap of the grate, round the bowl at the
  setting's corners, out at the flue opening, along the back flue and up the stack to the outlet, in no element of the
  brick, the frame or the kettle; the stack clear in every pose but for the cap; every firebar on a bearer at each end,
  the bearers built into the walls.
- **Clearances:** in 13 scenes at rest no two parts overlap (intended contacts aside: the shafts in their bearings and
  beams, the paddles in the bath and the crust, the flange bedded in its seat, the bath's edge in the bowl's wall, the
  zinc half sunk, a lump of coal in the layer over it); the drive and the stirrer turned every 7.5 degrees either way
  touch nothing else (the mitre pair's teeth among them); the fire door lifted every 0.02 touches nothing.
- **No z-fighting** at 39 poses (13 scenes, three poses each) after the fix.
- **Any fired brick** and **files**: the crucible furnace's brick checks; every texture code declared; glow on the
  burning coal only; the frame's shape the iron frame alone; no air, water or chute anchor; the hollow cells the
  kettle mouth's; decks on the setting's top cells; the power cell on the back face; the outlet under the chimney's
  cell, the chimney six from it up; the fire door and the ash mouth on the controller's front face; the shipped files
  are the checked model.

`tools/tests/test_parkeskettle_model.py` holds the written files to these without the generator (the rig's parts and
requires, the reference poses, the cells rebuilt from the shipped shape, θ turning the drive and the stirrer alike and
depth lifting only the fire door, each level of the bath, the paddles, the flange, the anchors, the side faces, the
stages' counts measured from the shape's brick, the brick types the crucible furnace's) and `site/models.json`'s
states and Play to the generator's; `site/test/parkeskettle.test.ts` replays the reference poses in the viewer's maths.

### Rig schema (`parkeskettle-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema") with nothing added to the shared rig
maths: two inputs, and parts gated by `requires`.

- **Inputs.** θ, the axle's angle about native z (the power face is south); depth, the fire door (0 shut, 1 open). No
  work, class, presence, feed or oil.
- **Drivers.** The drive: a `rotate` about z through (x 24, y 40), ratio 1. The stirrer: a `rotate` about y through
  its axis, ratio 1 (the wheel under the apex, the pinion behind it: the same sign as θ). The fire door: a `step` slide
  up y over depth 0..1 by `furnace.fireDoorLift`. Every other part has none.
- **`requires`** values: the stages, `brickwork`, `lining`, `kettle` (with the `firedoor` part) and `stirrer` (with the
  `gallows` and `drive` parts); `lead1`..`lead4`, `zinc`, `crust`, `fuel1`, `fuel2`, `fire1`, `fire2`, `damperopen`,
  `damperhalf`, `dampershut` (one at a time) and `outletcap`; the iron frame has none.
- **Keys:** `cells`, `powerCell`/`powerFace`, `kettleCell`/`kettleFace`, `mouth`, `fireDoor`/`fireDoorSide`,
  `ash`/`ashSide`, `outletCell`/`outletFace`, `chimney`, `build`, `bricks`, `furnace` and `parts`.
- **`build`**: the crucible furnace's: `frame` (`recipe`, `tool`, `ingredients`), `stages` in order (`requires`,
  `materials`, the brickwork's and the lining's `volumeBlocks` and `perClick`, the brickwork's `oneType`, the kettle's
  `alsoParts` (the fire door) and `also` (the damper's places), the stirrer's `alsoParts` (the gallows and the drive)),
  `outletCap`.

**In the viewer** (`site/models.json`, `parkeskettle`): the "Axle" slider is θ (Play turns it, at 2 s a turn, about
the axle's 0.48 turns a second at the reference speed 0.6), "Turn backwards" reverses it, and the "Fire door" slider
is depth; Play also lifts and lets down the fire door (stirring 4 s, lifted in 1 s, stoking 1.5 s, let down in
0.8 s). Five selects of states: **Build (proposed stages)** (the iron frame, 1: brickwork, 2: lining, 3: kettle and
doors, 4: stirrer, complete; each with its materials), **The kettle** (empty, a quarter, half, three quarters, full,
zinc stirred in, the crust risen; it waits for the kettle stage), **The fire** (cold, coal laid, lit, burnt low),
**Ash-pit damper** (open, half, shut) and **Outlet** (open, or capped; it waits for the lining). The page opens with
the kettle built, the zinc stirred in, the fire lit, the damper open and the outlet open, and a **Brick** select.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`ln_*` the lining (`ln_brick_NNN`), `kt_*` the kettle (`kt_wall<band><n>`, `kt_bottom_*`, `kt_flange<n>`), `fd_*` the
fire door (`fd_plate`, `fd_handle`), `lead<n>_*`, `zinc_*`, `crust_*` (`cake`, `lump<n>`), `fuel<j>_*` and `fire<j>_*`
(`bed`, `lump<n>`, the burning bottom layer's `cinder<n>`), `damperopen_*`, `damperhalf_*`, `dampershut_*` (`plate`,
`knob`), `outletcap_*`, `gl_*` the gallows (`gl_foot_*`, `gl_standard_*`, `gl_lower*`, `gl_middle*`, `gl_upper*`,
`gl_boss_*`, `gl_ped_*`), `dr_*` the drive (`dr_oaka`, `dr_oakb`, `dr_shaft_*`, `dr_collar_*`, `dr_pinion_*`,
`dr_hub_*`), `st_*` the stirrer (`st_shaft_*`, `st_collar_*`, `st_hub_*`, `st_wheel_*`, `st_paddle<tier><n>`); and
`fr_*` the iron frame (`fr_bearer`, `fr_bar`, `fr_front_*`, `fr_rail_*`, `fr_guide_*`, `fr_angle_<corner>_a`/`_b` (the corner angles' face and side legs), `fr_band_<face>_a`/`_b` (the top bands' face and top legs, `<face>` n, s, w or e),
`fr_tie_*`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Salvage

From `origin/741-parting-furnace-model-v2` (its evaluation is the v3 brief's `evals/741-parting.md`), copied into this
generator and adapted, never rebased:

- **Kept:** the mitre pair's maths and helpers (`bevel`, `bevel_cone`, `disc`: stub teeth
  with backlash, a body of two tiers; v2's module 1.2 and 6 teeth each became module 1.0 and 8 in pass 2), its phasing (the pinion's tooth 1 into the wheel's gap at θ 0), the
  paddles' symmetry, and the gearing (pitch point), supports and reverse checks.
- **Adapted:** the gallows (v2's two standards on the flange, north and south, with two crossbeams along z, become two
  plate standards on the floor, west and east, with three beams across x and bosses); the pinion (v2's came off the
  blowing engine's shaft: now its own drive shaft from the power face, in a pedestal bearing and the gallows' middle
  boss); the stirrer (its shaft hung by a collar, a hub under the wheel, two tiers of paddles that follow the bowl); the
  gears' teeth now run into their bodies, so nothing floats.
- **Rebuilt:** the kettle (v2's was a brick setting 13.6 across with a flange ring and a disc of lead) and its whole
  setting, on the crucible furnace's generator (`world_skin`, the brick lattice, lining zones, the binding (made angle iron), ties, the cap,
  the bricks, the build, the shipped cells and most of its checks).
- **Dropped:** v2's chutes (the infeed, the siphon, the outfeed): batch, rule 2; its skimmer lying on the flange; its
  shared floor, stack and engine shaft.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files only. No one has looked at it in a client.

1. **The kettle is 28 across at its flange**, not quite two blocks: its back edge must clear the chimney's cell behind
   it, and the front wall keeps 6.5 of brick (5.5 behind the doors' recesses).
2. **The flange stands 0.15 proud of the floor.** Bedded flush in the seat's brick, its top would share a plane with
   the brick's under its outer rim.
3. **The seat's opening is a stepped octagon** (the brick lattice is square to the axes): hidden under the flange, but
   its steps make many small boxes in the top courses round the kettle.
4. **The bowl is faceted** (24 flat facets and a flat bottom), its facets wider at their tops than their bottoms, the
   extra lying inside their neighbours.
5. **The bath is drawn at four levels**, each a surface layer: a part-filled kettle between them is drawn at the next.
   The zinc and the crust are drawn on the full bath only.
6. **No glow on the bath**: molten lead at its working heat barely shows; the renderer may set it.
7. **The gears' teeth are boxes**: 8 stub teeth each, straight-sided, as every generated gear here.
8. **Texture seams**, as the crucible furnace's: the brick is world-mapped boxes; that it runs on across them is not
   yet seen in the game.
9. **The site viewer's colours** (the families in `site/src/lib/model-view.ts`): the lead bath keeps the metal grey;
   the zinc (pale blue-white, `#c2d0d8`) and the crust (a dull buff, `#a8946c`) have their own tints, entries added
   ahead of the metal family (the crust first, as its texture path names zinc too). No other model's colours change.

## Provisional, pending #711

The shared machine code (#711) has not landed. The power anchor is the existing machines' (`powerCell`/`powerFace`,
the power ghost taking a vanilla axle); the kettle has no chute or water anchor, as batch equipment; the furnace
anchors (`outletCell`/`outletFace`, `chimney`, `ash`/`ashSide`) and the `build`, `bricks` and `furnace` keys are the
crucible furnace's, with `fireDoor`/`fireDoorSide` and `kettleCell`/`kettleFace` new. The stirrer's load following
the charge (0.04 empty to 0.2 full, #927) is gameplay's; the model only shows the bath's level.

## Settled by the owner (2026-10-14)

- **"Two high"**: two blocks of setting, the flange at its top (the floor), with the gallows in a third layer: 25
  cells, three of them hollow over the kettle's mouth.
- **The axle at the back, at the gallows' height**, and **the outlet in the back row's east corner**.
- **Fuel** (#927): 6 coal to a 2,400-unit charge; the fire holds 8.
- **The gears**: 8 teeth each, module 1.0, ratio 1.
- **Viewer colours**: the zinc and the crust have their own tints; the lead keeps the metal grey.

## Open for the owner

1. **Build stages**: the frame recipe (the crucible furnace's copied), the kettle stage's and the stirrer stage's
   materials (proposed: 6 iron plates; 4 iron plates, 4 iron rods, 4 iron nails and strips), and whether the fire door
   and the damper come with the kettle (#928, "Open").
2. **The kettle's size**: 28 across at the flange (1.75 blocks), set by the chimney's cell behind it. A full two
   blocks would need the setting four deep, or the outlet elsewhere.
3. **The kettle worked from the top**: its mouth is two blocks up, worked from the deck or from a step in front, as the
   crucible furnace's hole.
4. **The hollow cells over the kettle's mouth**, or none: they keep the space over the kettle free (the mitre wheel's
   tooth tips reach 0.35 into the middle one).
5. **The parting furnaces' items** (silver-lead in, desilverized and rich lead out) are #927's and #740's open
   question, not the model's: the model draws one bath texture.
6. **The Faber du Faur retort** is not modelled (#927: not a level; the zinc return is a rule).
