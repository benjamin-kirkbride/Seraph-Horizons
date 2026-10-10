# Eidolon gantry

Part of the Seraph Horizons mod (`../README.md`): the frame the player-built eidolon (`../Eidolon/README.md`)
is assembled in, stage by stage, and its dock afterwards, for repair and recharge.

**Status: the gantry and its winch are built** (#671): the block, its ghost cells, the frame's recipe,
the nine winch stages and the renderer are in `Core/` and `Game/` ("Gameplay", below, and the mod's
README, "Eidolon"). The body's stages on the spine and the waking are built too (#672:
`Game/BEBehaviorEidolonBody.cs`, `Core/BodyParts.cs`; the mod's README, "The body and waking it"). This
folder also holds the model's generator (`tools/`).

Paths here are from this folder unless they start with `assets/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/`, which is the repository's.

## The gantry

An open frame of 6×6 timbers in **the wood it is built from** (below, "Wood"), **6 blocks deep (x) by 5
wide (z) by 5.5 high** (cells 6×6×5, the top row half used): four corner posts, side head beams over them, front, back and hoist beams between, sills
on the sides and back, side rails at 2.5 blocks (a player walks under them), knee braces at every top
corner, iron fish plates and angle brackets at the post tops and trenail heads on the beams and rails.

- **The front is west (−x), the way the body faces, and open** from the ground to the front beam
  (5.1 blocks) across 4.25 blocks between the front posts: there is no front sill, and the front knees
  are short, over the top corners only. The eidolon walks straight out of it when it wakes.
- **The body hangs round the middle of cell (2, 0, 2)**, the eidolon's `hung` pose 10 voxels
  (`HUNG_BACK`) behind the entity's position, model (8, 0, 8), which is the build frame's (30, 0, 40):
  where it stands once it has woken and stepped off the spine. Round the hung body there is 1.28 blocks
  to the front posts, 1.13 and 1.14 to the side posts and 1.07 to the winch's cheeks at the back, to walk round
  it and fit parts. The winch's wheels stand in the back left corner (z 61 to 68, from x 64 back), so the
  left walkway ends at them: the space behind the body is reached from the right.
- **The winch is at the back, and geared in wood: the eidolon is heavy** (about 2,500 kg). It is a crab
  winch: two tall cheeks bolted to the back posts carry three shafts in iron bearing plates, the crank shaft
  at the bottom (iron, only in the left cheek), the layshaft above it and the drum's shaft at the top (both
  wooden: vanilla's wooden axles, below), with a wooden drum (iron hoops, the chain coiled at its middle).
  It is fitted onto the built frame stage by stage ("The build", below). The gearing is a mill's: two
  stages of module 1, each a **6-stave lantern pinion driving a 30-cog wheel**, sit at the left (south) end
  between the drum and the left cheek: stage 1, against the cheek, the crank shaft's lantern and the
  layshaft's wheel; stage 2, against the drum, the layshaft's lantern and the drum's wheel.
  - The gears are fitted from **vanilla's spur gears** (`game:spurgear-s`, below) and drawn in their generic
    plank texture. A **lantern** is six round staves (octagons, 1.2 across) on a 3-voxel radius between two
    board discs 8.4 across and 1 thick, each bound with an iron hoop, 3 voxels apart: the wheel's cogs run
    between them.
  - A **cog wheel** is 2 blocks across over its cogs (pitch radius 15, tips at 16) and 2.4 thick: a rim of 20
    felloes (from radius 11 to the cogs' roots at 13.8), four **clasp arms** crossing in a square round the
    shaft's flats (the shaft's octagon is turned to them), and 30 cogs pegged into the rim, each 1.4 wide to
    the pitch circle and tapered to 1 at its tip, the pitch 3.14.
  - That is **25 to 1**: the 3-voxel let-down turns the drum 43° and the crank 2.98 turns. With the crank's
    6.5-voxel radius and the drum's 4, the handle sees a 40.6th of the load: about **600 N** for 2,500 kg
    before friction, some 800 N with the wooden gearing's and the bearings' (about three quarters getting
    through), heavy work for one and steady work for two. The gearing is the model's, not gameplay's. 25 to 1
    is as much as two such stages fit: a 36-cog wheel (6 to 1) would reach the back knees.
  - The pitch circles touch (centre distances 18), and each stage is phased to sit meshed at rest (a stave on
    the line of centres, the wheel's gap there). Through a full turn of the crank no stave runs into a cog, and
    at every moment a stave of each lantern is inside its wheel's tips within 0.3 voxels of a cog: the
    lantern always drives.
  - The crank's axis is the middle of its cell, (88, 24); the wheels stand as far back as their cogs clear
    the box (x 95.7), so the layshaft is at (79.7, 40.0), up and forward of the crank, and the drum straight
    above it at (79.7, 58.0), 18 apart. The drum's wheel clears the back left knee by 2 voxels; the cheeks
    reach forward to x 74 to carry the shafts, and up to the drum's bearing (y 18 to 60.5), under the back
    knees.
  - **The shafts.** The crank shaft is iron (apothem 1): it is the fast shaft with the least torque (a
    25th of the drum's), and it carries the iron ratchet and crank out of the frame through the pillow
    block beside the post, where a wooden shaft thick enough would not fit. The layshaft and the drum's shaft
    are **vanilla's wooden axles** (`game:woodenaxle-ud`), laid end to end one a block as the game lays them,
    four each, and drawn as the game draws one: the axle's cross of two 4 × 2 boards
    (`shapes/block/wood/mechanics/axle.json`) in its generic plank texture. The drum's shaft has the axle's
    section full size; the layshaft's is 0.7 of it (2.8 × 1.4), because the drum wheel's cogs reach to 2
    voxels from its axis inside its lantern and the full cross's corners reach 2.24. Each runs on **iron
    gudgeons** (apothem 0.8) driven into its ends, running in the cheeks' bearings, with an **iron collar**
    round each end so the gudgeon does not split it. The drum's shaft ends past its wheel: its gudgeon runs on
    through stage 1, where the layshaft's wheel passes 1.1 voxels from it (a wooden shaft there would be hit).
  - **The ratchet and pawl stay iron** (below): the pawl holds the whole hanging load on one small tooth of
    the crank shaft's ratchet, which in wood would shear; iron ratchets and pawls were usual on wooden winches.
- **The crank is outside the frame**: the crank shaft runs on past the left cheek, through an iron pillow
  block bolted to the back left post's front face, and out past the post's outer face (z 80) by 5 voxels,
  where an iron crank (radius 6.5 voxels) with a wooden handle pointing outwards is turned by a player
  standing south of the gantry, about 1.5 blocks up. A full turn clears the frame (checked every 5°, the
  layshaft and drum turning with it): the crank turns 2 voxels outside the post.
- **A ratchet and pawl hold the load.** Between the post's outer face and the crank's web, an 8-tooth iron
  ratchet (tips at 4 voxels, roots at 2.8) is keyed on the crank shaft; each tooth's radial face is the side
  letting down turns into. An iron pawl hangs from a pin on an iron bracket bolted to the post's outer face,
  forward of the shaft, and lies over the ratchet's top with its nose down in a gap, 0.15 from the face
  behind it: the load cannot run back, and it falls in by its own weight. Turning to wind up rides the
  teeth under it. To let down it is thrown off: the rig lifts it 0.5 rad over the first 0.3% of the let-down
  and drops it back at hung. The chain leaves the drum's top
  and runs up and forward, tangent to both, onto one wooden sheave turning on an iron pin held in iron hangers
  under the hoist beam, and drops from it straight down to a swivel eye over the body.
- **The spine is the gantry's, and holds the body**: vanilla's eidolon has a charred-wood mast down
  its back (`spine1` and its 28 children: the mast in three lengths, a pulley with a small winch handle,
  ropes, staples, hooks, two little platforms and three steel clamps). Here it is cut off the eidolon
  (`../Eidolon/README.md`, "The spine is the gantry's"), made from three support beams of the gantry's
  wood, its mast and platforms drawn in that wood ("Wood", below), and is hung on the chain once the winch is built,
  empty, in `hung`'s pose: an iron ring from the eye over its top peg (`spine-hook1`), the ring's bottom bar
  under the peg. The torso, the first stage, is clamped onto it: the spine's steel clamps (`bar-spine1` and
  `bar-spine2`) run forward through the chest's backplate, and every stage after fits onto what already
  hangs. When the eidolon wakes (its `activate`) the clamps let go, it drops onto its feet, steps forward
  off the spine and walks out of the front, and the spine is left hanging empty, as before the build.

The body's pose is `hung` (limp, head down, the lowest toe 3 voxels off the floor), and the spine is
baked with it where vanilla has it on the chest (tilted with the chest's lean). Searching the posed
body for a way to hang it showed why the spine's peg: no straight line reaches the hip block from above
without passing through the heart, the hood or the arms, which hang tight against the waist.

## Wood

**The gantry takes the wood it is built from.** Every wooden face is one of two texture codes a blockType
maps per wood; the shape's own map points them at oak, so the model renders as oak where nothing maps them:

| Code | What | The shape's map | A wood-typed blockType's |
|---|---|---|---|
| `wood` | Side grain: the frame's timbers, knees and cheeks, the drum, the crank's handle, the sheave; the spine's mast and its two platforms | `game:block/wood/debarked/oak` | `game:block/wood/debarked/{wood}` |
| `wood-end` | End grain, the rings: the two ends of every timber, knee and cheek, of the drum and the handle, and of each of the mast's three lengths and the platforms (UVs the middle of the texture) | `game:block/wood/treetrunk/debarked/oak` | `game:block/wood/treetrunk/debarked/{wood}` |
| `mechanics` | What is fitted from vanilla's mechanical power blocks, drawn as the game draws them whatever their wood: the wooden shafts (wooden axles), the lanterns' discs and staves and the wheels' rims, arms and cogs (spur gears) | `game:block/wood/planks/generic` | the same: not wood-typed |
| `iron`, `chain` | Plates, brackets, pegs, bearings, hangers, the sheave's pin, hoops, collars, gudgeons, the crank shaft, ratchet, pawl and its pin and bracket, crank web, ring, eye; the chains | iron plate, iron chain | the same: not wood-typed |
| The eidolon's | The body, and the rest of the spine as vanilla draws it: its steel clamps (`steel`), its staples (`rusty-iron`, restyled to tarnished brass with the body), its ropes (`reedrope`), and the small pulley and its winch handle (`charred`); its four hooks are drawn in the gantry's `iron` | the eidolon's map | the same: not wood-typed |

The blockType, when it is written, takes a `wood` variant group from the game's wood properties
(`{ code: "wood", loadFromProperties: "block/wood" }`: birch, oak, maple, pine, acacia, kapok, baldcypress,
larch, redwood, ebony, walnut, purpleheart, each with both textures), as vanilla's support beam does
(`textures: { wood: { base: "block/wood/debarked/{wood}" } }`), and maps both codes:

```json5
textures: {
  "wood":     { base: "game:block/wood/debarked/{wood}" },
  "wood-end": { base: "game:block/wood/treetrunk/debarked/{wood}" }
}
```

A renderer drawing the moving parts (the winch, the sheave) tessellates them with the same map. The model
viewer colours by texture code, not by the textures, so it cannot show another wood: its Texture colouring
shows which faces are `wood`, `wood-end` and iron.

**The spine is in the gantry's wood too.** Vanilla's mast is charred wood (`#charred`); the spine stage makes it
from support beams of the gantry's wood, so the generator (`spine_wood()`) redraws the charred faces of the mast's
three lengths (`spine1`..`3`, `SPINE_TIMBERS`) and of the two platforms cut from the same beams in `wood`, and
the two faces across each one's long axis in `wood-end`, as the frame's timbers are drawn. Its four hooks
(`spine-hook1`..`4`, `SPINE_IRON`) are iron, the frame's `iron` plate: the top one carries the body on the ring.
The pulley's caps and its winch handle are small vanilla fittings, not the mast's timber, and keep vanilla's
charred texture, as the clamps, staples and ropes keep theirs. The body's own charred parts (its wooden bones) are the eidolon's
and are unchanged.

## The build

The frame is crafted and placed whole;
the winch is fitted onto the placed frame stage by stage, as the pack's other multiblock machines are
(`../DrawBench/README.md`, "Stages"; `../GearCutter/README.md`; `../PressBrake/README.md`): the stages go
in a fixed order, a right-click on the frame holding the next stage's item takes the stage's count of it
from the held stack in one click (fewer held is refused, and nothing is taken), an item of a later stage
is refused as out of order, and breaking the frame returns everything that went in. No new crafted part
items: every stage takes one plain item the game already has. Then the spine is hung on the chain and
the body is built on the spine (`../Eidolon/README.md`, "Build stages"). The generator holds the stages
(`winch_stages()`) and the recipe (`frame_recipe()`); the validator and the test hold this README to
them.

**Frame and winch.** The frame is what stands and holds: the posts, head beams, front and back beams, the
hoist beam, sills, side rails, knee braces and the two cheeks (the winch's side frames), and the iron
that fixes them and that the turning parts drop into: fish plates, angle brackets, trenail heads, the
cheeks' bearing plates and the pillow block, the sheave's hangers and the sheave's pin (fixed: the
sheave turns on it). The winch is everything that turns, binds what turns, or hangs: the shafts and their
gudgeons, the gears, drum and sheave, the hoops and collars, the ratchet with its pawl (and the pawl's pin
and bracket, which come with it), the crank, and the chain, its swivel eye and the ring. So the frame
alone (`eidolongantry_frame.json`, the part with no `requires`) is the bare gantry with empty bearings.

**The frame's recipe** (grid), gated by the eidolon schematic, which is not used up. The frame takes the
wood of its beams (`seraphhorizons:eidolongantry-{wood}`, "Wood" above).

| Slot | Item | Count |
|---|---|---|
| Schematic | `seraphhorizons:schematic-eidolon`, sold by the curio dealer, not consumed: added by its gate (`config/schematic-gates.json`, machine `eidolon`), not written in the recipe | 1 |
| Beams | 24 × `game:supportbeam-{wood}`, all of one wood (birch, oak, maple, pine, acacia, kapok, baldcypress, larch, redwood, ebony, walnut, purpleheart) | 24 |
| Nails and strips | `game:metalnailsandstrips-{metal}`: iron, meteoric iron or steel | 16 |
| Tools | A hammer and a saw (`tool-hammer`, `tool-saw`), worn as the game's mechanical power recipes wear them | — |

The beams are the model's timbers, one support beam each (`frame_timbers()`): 4 posts, 2 side head
beams, the front, back and hoist beams, 3 sills, 2 side rails, 8 knee braces and 2 cheeks, 24. (A
support beam is 4 × 4; the frame's timbers are 6 × 6 and up to six blocks long, so a beam stands for a
piece, not for its wood.) The 16 nails and strips pay for the frame's iron: 20 straps, plates and
bearings (4 fish plates, 8 angle brackets, 5 bearing plates and the pillow block, 2 hangers), the
sheave's pin and 12 trenail heads.

**The winch's stages**, in order. Each is one rig `requires`, so the viewer shows the bare frame and then
each stage on top of the ones before. `{metal}` is iron, meteoric iron or steel, as the pack's
woodworking machines take them (`assets/seraphhorizons/patches/woodworking-machine-costs.json`); `{wood}`
is the gantry's own wood: the drum's planks and the spine's beams are drawn in it, so the gameplay takes
no other (the eidolon epic's decision, #668).

| # | `requires` | Item | In the model | Taken back |
|---|---|---|---|---|
| 1 | `axles` | 8 × `game:woodenaxle-ud` | The layshaft and the drum's shaft, four axles each (one a block: 3.6 and 3.3 blocks), with the iron gudgeons driven into their ends, set in the cheeks' bearings | Only by breaking the frame |
| 2 | `crankshaft` | 1 × `game:rod-{metal}` | The iron crank shaft, in the left cheek's bearing and the pillow block and out past the post | Only by breaking the frame |
| 3 | `gears` | 4 × `game:spurgear-s` | The two lanterns (on the crank shaft and the layshaft) and the two 30-cog wheels (on the layshaft and the drum's shaft): discs, staves, rims, clasp arms, cogs | Only by breaking the frame |
| 4 | `drum` | 10 × `game:plank-{wood}` | The drum (one plank a stave of its eight) and the sheave's hub and flanges (two), on its pin | Only by breaking the frame |
| 5 | `strapping` | 10 × `game:metalnailsandstrips-{metal}` | One a band: the lanterns' four disc hoops, the drum's two hoops, the wooden shafts' four collars | Only by breaking the frame |
| 6 | `ratchet` | 1 × `game:metalplate-{metal}` | The 8-tooth ratchet keyed on the crank shaft, the pawl, its pin and its bracket on the post | Only by breaking the frame |
| 7 | `crank` | 1 × `game:rod-{metal}` | The crank's web on the shaft's end (outside the ratchet, so after it) and its wooden handle | Only by breaking the frame |
| 8 | `chain` | 4 × `game:metalchain-{metal}` | The chain: its turn on the drum, the lead to the sheave, the fall, the swivel eye and the ring (its working length, drum to eye with the drop, is 3.5 blocks: a chain a block) | Only by breaking the frame |
| 9 | `spine` | 3 × `game:supportbeam-{wood}` | The spine, a beam for each of the mast's three lengths (`SPINE_TIMBERS`), drawn in that wood, with its platforms, vanilla's pulley, ropes, staples, hooks and steel clamps; hung on the ring by its top peg: what the torso is clamped to | Only by breaking the frame |

**Why these items.**

- **Axles.** The wooden shafts are vanilla's axle, so they take it, and look it: its cross section and
  texture, four a shaft as vanilla lays axles end to end. **The crank shaft stays iron** and is its own
  stage, before the gears (its lantern goes on it): it passes the back left post 2 voxels from its face in
  the pillow block, where the axle's 4-voxel cross would not fit, and inside the frame it runs only from its
  lantern to the cheek (6 voxels, all inside the lantern), so a wooden axle with an iron end would be an
  iron shaft with a lantern's width of wood on it. The gudgeons go in with the axles (they are what a shaft
  turns on: without them it would lie loose between the cheeks); their iron is the frame recipe's (an open
  question below).
- **Gears.** Vanilla's mechanical power has three wooden gears. The angled gears (`angledgears`: a lantern
  of eight pegs and a peg wheel) and the large gear (`largegear3`: a 3-block wheel with pegs on its face)
  both mesh at right angles, turning a shaft's power round a corner; the **spur gear** (`spurgear-s`: radial
  peg teeth between two plank platters, a block across) is the one that meshes shaft to parallel shaft, as
  this train does. So the stage takes four spur gears, one for each of the train's gears, and the model
  echoes them: the lanterns are the spur gear's two platters with pegs between, the wheels its radial cogs,
  in its texture. The model keeps its own sizes (the wheels are 2 blocks across, the spur gear 1.25) and
  its mill pattern (a lantern of six staves into a 30-cog wheel), which the gearing checks hold.
- **Spine.** Vanilla's mast is three lengths of timber (2 × 2 voxels, 16, 16 and 11 long) pegged end to end, so
  the stage takes a support beam for each (`spine_stage()` counts `SPINE_TIMBERS`), of the gantry's wood
  (`{wood}`, the gantry's own): the mast is drawn in it. Its small fittings (the steel clamps, staples,
  hooks, pulley, handle and ropes) are not paid for, as the frame's trenail heads are not: one item a stage. A
  steel plate for the clamps would be a second stage of a part the player hangs as one.
- **Drum, strapping, ratchet, crank, chain.** Planks of a wood for the drum and the sheave; one nails and
  strips a band, as the woodworking machines pay for their iron; a plate for the ratchet and pawl (the
  pawl's pin and bracket from the offcut); a rod for the crank (the web forged from it; the handle is
  wood); the game's chain, which the draw bench also takes.

**Every step can be built** (the validator's `check_build`): at rest, the frame and then each winch stage in
turn are joined to the ground through what is already there and themselves, nothing floating, and no part
comes before what it is fitted onto (`NEEDS`: a gear its shaft, the drum its shaft, a band what it binds,
the ratchet and crank the crank shaft, the crank the ratchet, the chain the drum and the sheave, the spine
the ring, the body the spine). The body's stages then fit onto the spine as before.

## Gameplay (`Core/`, `Game/`)

What the gameplay holds to here, beyond the mod's README ("Eidolon"):

- **Placing.** `GantryRig.PlacedSide`: the open front (native west) faces the player, and the front's
  middle cell (`exit`'s, `[0, 0, 2]`, `GantryRig.PlaceCell`) is the block they click; the controller,
  `[0, 0, 0]`, is two cells to its side. Every cell must be free. Hollow cells get a ghost with no boxes.
- **Stages.** `GantryParts`: the table above, in order, by full code, a stage's whole count from one
  stack; breaking returns every fitted code and count (`Returns`). `GantryRequires.KnownRequires` is the
  rig's vocabulary: the nine stages and the body's six (`BodyStages`), which `GantryParts` never claims.
  A test holds `GantryParts` to this README's stage table, as `tools/tests/test_eidolongantry_model.py`
  holds the generator to it.
- **Drawing.** `EidolonGantryRenderer` draws every part with a `requires`, a ride or a driver (the frame
  is the block's own shape) when its stage is fitted, posed by `BEEidolonGantry.WinchDepth` (eased at
  1/8 a second). The rig's `crank` part's `rotate` driver runs at ratio 0, so θ is fed 0.
- **The body's seam.** `IEidolonGantryExtension` (`Game/IEidolonGantryExtension.cs`): a block entity
  behavior of the controller, asked after the winch for clicks, the creative shortcut, which body stages
  are drawn, drops and help. `BEEidolonGantry.WinchComplete` is the spine fitted; `WorldPoint` turns the
  rig's anchors (`body`, `hang`, `fit`, `exit`) to the world; ghosts implement `IMachineGhost`.
- **The body.** `BEBehaviorEidolonBody`, the one extension: six stages after the spine (`BodyParts`,
  `BodyBill`), drawn when each is complete; the mind spawns the eidolon at `body`, facing `exitSide`,
  sends it 3 blocks past `exit` and clears the body. `BEEidolonGantry.DockAt`/`Docks`: whether a point
  is in the dock (1.25 blocks round `body`).
- **Hand-off at activation** (below, "Open questions"): the eidolon is spawned standing at `body`
  whatever the winch's depth (nothing drives the depth yet), and `activate` plays from `hung`.

## The body: baked, not animated

The gantry's shape carries the eidolon's elements **baked into the hung pose**: each element is posed
by `../Eidolon/tools/kin.py` (the game's pose maths) at `hung`'s one frame and written as a plain
static element, its hierarchy flattened, the body's renamed `b_<stage>_<name>` by its build stage
(`assets/seraphhorizons/config/eidolon-stages.json`) and the spine's `sp_<name>`: the spine
(`spine.json`, written from vanilla by the eidolon's generator) is hung back on the chest block to be
posed with it. Elements with no drawn face (`origin` and the
invisible anchors) are left out; disabled faces and wind data are dropped; glow is kept (the brain,
the vessel the head brings, glows in the hung head; the mind's heart and eye are never drawn here).

Why baked, for the in-game renderer: a block draws static elements in the chunk mesh, or a renderer
tessellates them once per stage into a mesh it draws with one matrix (the hook's let-down), as the
machines' moving parts are drawn. Carrying the `hung` animation instead would need the entity
animator on a block (the joint shader pass for over 200 elements, the animation run every frame) to show a
pose that never changes, and stage toggles would have to hide joints' elements in the animator. The
cost of baking is a copy: the gantry's shape must be regenerated when `eidolon.json` or `spine.json` changes, and
`tools/tests/test_eidolongantry_model.py` fails until it is (it runs the generator and compares).

The checks that the baked pose is the animated one: the generator compares every baked element's
corners with kin's pose, and the written shape, flattened as the game reads it (machinegen's
`flatten`, a separate implementation of the hierarchy), with the same (worst 8e-5 voxels, the
rounding).

## The rig (`assets/seraphhorizons/config/eidolongantry-rig.json`)

The bucking sawmill's rig format (`../BuckingSawmill/README.md`, "Rig schema"). Native frame, block
units, controller cell `[0,0,0]` the foot of the front right (north-west) post.

| Part | Requires | Drivers |
|---|---|---|
| `crank` (`ck_shaft*`: the iron crank shaft) | `crankshaft` | `rotate` θ at ratio 0 (the crank's clock; moves nothing), `step` rotate by depth about the crank's axis: +18.75 rad (2.98 turns, 25 × the drum) over the let-down |
| `cranklantern` (its lantern's discs and staves), `crankhoops` (the lantern's hoops), `ratchet`, `crankarm` (the web and handle) | `gears`, `strapping`, `ratchet`, `crank` | ride `crank` |
| `layshaft` (`ls_shaft*`, `ls_gudgeon*`: the axles and gudgeons) | `axles` | `step` rotate by depth: −3.75 rad (5 × the drum, the other way: it meshes with both) |
| `laygears` (its wheel and lantern), `laystraps` (its collars and the lantern's hoops) | `gears`, `strapping` | ride `layshaft` |
| `drumshaft` (`dr_shaft*`, `dr_gudgeon*`) | `axles` | `step` rotate by depth: +0.75 rad (43°): the drum pays out the drop (its radius is 4 voxels, the drop 3) |
| `drumwheel`, `drum` (the barrel), `drumstraps` (its hoops and the shaft's collars), `coil` (the chain on it) | `gears`, `drum`, `strapping`, `chain` | ride `drumshaft` |
| `pawl` (`pw_*`) | `ratchet` | `step` rotate by depth about its pin, 0.5 rad over depth 0..0.003: thrown off the ratchet as the let-down starts |
| `pawlmount` (`pm_*`: its pin and bracket) | `ratchet` | none |
| `sheave` (`sv_*`: the hub and flanges; its pin is the frame's) | `drum` | `step` rotate by depth, the same chain |
| `lead` (the drum-to-sheave chain) | `chain` | none: its ends do not move |
| `fall` (the hanging chain) | `chain` | `stretch` by depth about the sheave: it lengthens by the drop |
| `hook` (the swivel eye) | `chain` | `feed`: down by the drop at depth 1 |
| `ring` | `chain` | rides `hook` |
| `spine` | `spine` | rides `hook` |
| `torso`, `pelvis`, `legs`, `arms`, `head`, `mind` | the same | ride `hook`: the body comes down with the eye and the spine |
| `frame` | | |

What is fitted onto a shaft in a later stage rides the shaft's part, so it turns with it whatever is fitted.
**Before the body and after the awakening** the gantry looks the same: the winch built and the spine
hanging empty from the ring. There is no `departed` value in the rig: a `requires` can only add a part,
and gameplay draws the body's stages only while the eidolon is in it. The model viewer's entry gives the
build names in two selects (the scenario's `states`, two groups, `docs/recipe-browser/models.md`): **Gantry**
steps from "The frame" through each winch stage to "The spine on the chain", and **Eidolon** from "None"
through each stage of the body to "Mind: fully built", then "Departed: awake and gone", none of the body.
The page opens with both fully built. The body hangs on the spine, so the Eidolon select waits, disabled with
a note and the body not drawn, until the chain and the spine are fitted.

**depth** is the winch let down, 0..1: 0 hung, 1 the lowest toe on the floor (`winch.drop`, 3 voxels).
The cells are the 6×6×5 machine box and one more outside it, **the crank's cell `[5, 1, 5]`**, south
of the back left post, a block up (the crank shaft's end, the ratchet and pawl, the crank and its handle): 181
in all. The crank's cell is in the footprint so that nothing is placed where the crank turns, but it is
**`hollow`, with no boxes**: nothing in it to collide with or select. The other cells' boxes are built from
the gantry's own parts at rest, every stage of the winch fitted (the frame, the shafts and all on them, the
pawl and its mount, the sheave); the body's and
the spine's collision and selection are gameplay's (they hang and move with the winch). Cells with none of
the gantry are `hollow`: 88 of the 181. The wooden train is bigger than the iron one was and stands further
forward, so nine cells that were hollow have boxes now: (4, 1, 0) (the right cheek, forward to x 74),
(4, 2..3, 1..3) and (4, 4, 3) (the layshaft, the drum and its shaft, now at x 79.7) and (3, 3, 3) (the drum
wheel's front cogs, to x 63.7).

Anchors: `body` (the entity's position, where it stands once awake), `hang` (where the ring bears on the
spine's peg), `fit` (the chest's middle, where parts are fitted), `exit` with `exitSide: "west"`, and
`crankCell` with `crankFace: "south"` (the crank's cell, its south face shaded with an arrow into it:
where the player stands to turn it; the viewer's `Side` anchor would put its arrow at the middle of the
footprint's south side, away from the crank). `winchCell` is gone.

## Regenerating

```sh
python3 mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py                 # write the three files
python3 mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py --out "$TMPDIR/g"   # elsewhere
```

It reads `eidolon.json`, `eidolon-stages.json` and `spine.json` from the repository (no game needed),
takes about 20 seconds, and writes `assets/seraphhorizons/shapes/block/eidolongantry.json` (everything),
`eidolongantry_frame.json` (the frame part only) and the rig. Output is deterministic. Regenerate after
every change to the eidolon's shape or stages.

## Validation (`tools/validate_eidolongantry.py`)

Every run: elements in their parts, Euler round trip; the baked body and spine against kin's hung pose;
every drawn eidolon element in the gantry once, in its stage, and the spine's in the spine part; the
ring with the chain and the spine with its own stage, both on the hook; the build ("The build": every
part but the frame in one stage, the stages in order, nothing before what it is fitted onto, each winch
stage at rest joined to the ground through what is there, the spine resting on the ring; the stages'
counts the model's: the frame's timbers, the axles' lengths, the bands, the spine's beams its mast's lengths); the spine and body one piece
after every stage (the eidolon's rule; the chest is shown before the hip block it hangs from in the hierarchy,
which baking makes harmless), and the torso touching the spine (its clamps); the lowest toe at 3 voxels
at depth 0 and on the floor at depth 1; everything in the machine box or the crank's cell; a full turn
of the crank (the layshaft and drum turning by their stave and cog counts) clearing the frame, the pawl's mount and
the thrown-off pawl, nothing of one shaft touching another's (the axles and gudgeons included) but a lantern's staves
and its wheel's cogs, the crank,
ratchet and pawl outside the frame in their cell; the gearing (pitch radii the module's, centre distances
their sums, the rig's turns the stave and cog counts', each meshing pair the other way, the drum paying out
the drop, the staves among the cogs at rest, and through a full turn of the crank the staves at most 0.25
into the cogs and engaged: at every step a stave of each lantern inside its wheel's tips within 0.4 of a
cog); the ratchet (the pawl's nose in a gap at hung, the
ratchet turned the let-down way running into it, and no contact as it is thrown off and after); the
body touching nothing of the gantry but the spine, and the spine nothing but the ring's bottom bar under its peg, at depth 0, 0.5 and 1; the
eidolon's `activate`, frame by frame where it hung, touching nothing of the gantry and clear of the
spine by frame 56 (it is from 51); the peg inside the ring;
the lead chain tangent to drum and sheave, the fall on the sheave's tangent and on the eye at every
depth, drum and sheave turning with the chain; the frame one piece from the ground, the layshaft and
the drum's shaft on their gudgeons in both cheeks' bearings, each gudgeon driven into its shaft, the crank
shaft in the left's and the pillow block and out of the frame, the pawl on its pin, the sheave's pin in both hangers and the sheave on it; the eidolon
standing where it woke (vanilla's rest pose) walking out west touching nothing, the empty spine included; at least a block to walk past the body on every side; textures by
role (the wood-variant codes on the frame, drum, sheave and handle, `wood-end` only on wood, `mechanics` on the axles and gears; the spine's mast and platforms in the wood codes and nothing else of it); no z-fighting among the gantry's own faces (after `fix_coplanar`); and in the written rig, the
crank's cell in the footprint, hollow, with no boxes. `tools/tests/test_eidolongantry_model.py`
holds the written files to these without the generator's state, and this README's stage table to the
generator's.

## Open questions

- **The gudgeons' iron** is not paid by the stage that fits them (the axles stage takes wooden axles): it
  is counted in the frame recipe's nails and strips. A rod in the axles stage would break one item a stage.
- **Counts by piece, not by wood.** A support beam a timber, a wooden axle a block of shaft, a spur gear
  a gear and a plank a drum stave count pieces, not material: the timbers are 6 × 6 against the beam's
  4 × 4, the wheels 2 blocks across against the spur gear's 1.25.
- **Size.** 6×5×5.5 blocks is set by the body at size 1 (3.75 blocks, arms hanging 2 blocks wide) and a
  block of room round it; a smaller eidolon would shrink it.
- **The winch's effort.** About 600 N at the handle before friction, 800 N with it: heavy for one player.
  A second crank on the north end of the crank shaft (a second pillow block and a crank cell on the north
  side) would make it two players' work, as real crab winches had; a third stage would not fit the corner.
- **Hand-off at activation.** `activate` starts from `hung` at depth 0, the toe 3 voxels up: the
  clamps let go and the body drops onto its feet (frames 1 to 8) and steps off the spine. Gameplay should
  wind the winch up to depth 0 before waking it; the let-down here is for docking, or to show the winch
  working.
- **The empty spine's lean.** Baked in `hung`'s pose, the spine keeps the chest's lean (about 18°) when
  the body has gone; a mast hung by its top peg would swing plumb. Hanging it plumb when empty would need
  a second baked copy (or a renderer turning it), and is left for now.
- **The hang's balance.** The peg is behind the body's centre of mass, so a real body hung there would
  swing back; the `hung` pose leans forward. A second line to the shoulders, or a hung pose that leans
  less, would make it hang true.
