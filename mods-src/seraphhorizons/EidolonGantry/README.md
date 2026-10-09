# Eidolon gantry

Part of the Seraph Horizons mod (`../README.md`): the frame the player-built eidolon (`../Eidolon/README.md`)
is assembled in, stage by stage, and its dock afterwards, for repair and recharge.

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity
or renderer yet; the model and its rig are what those will be built against.

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
  (`../Eidolon/README.md`, "The spine is the gantry's") and is hung on the chain once the winch is built,
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
| `wood` | Side grain: the frame's timbers, knees and cheeks, the drum, the crank's handle, the sheave | `game:block/wood/debarked/oak` | `game:block/wood/debarked/{wood}` |
| `wood-end` | End grain, the rings: the two ends of every timber, knee and cheek, of the drum and the handle (UVs the middle of the texture) | `game:block/wood/treetrunk/debarked/oak` | `game:block/wood/treetrunk/debarked/{wood}` |
| `mechanics` | What is fitted from vanilla's mechanical power blocks, drawn as the game draws them whatever their wood: the wooden shafts (wooden axles), the lanterns' discs and staves and the wheels' rims, arms and cogs (spur gears) | `game:block/wood/planks/generic` | the same: not wood-typed |
| `iron`, `chain` | Plates, brackets, pegs, bearings, hangers, the sheave's pin, hoops, collars, gudgeons, the crank shaft, ratchet, pawl and its pin and bracket, crank web, ring, eye; the chains | iron plate, iron chain | the same: not wood-typed |

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

## The build (proposal)

**A proposal for the gameplay to come**: there is no block yet. The frame is crafted and placed whole;
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
| Schematic | `seraphhorizons:schematic-eidolon` (sold by a trader; the item does not exist yet), not consumed | 1 |
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
is any wood (the frame's would match).

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
| — | `spine` | To be decided (open question) | The spine, hung on the ring by its top peg: what the torso is clamped to | — |

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
- **Drum, strapping, ratchet, crank, chain.** Planks of a wood for the drum and the sheave; one nails and
  strips a band, as the woodworking machines pay for their iron; a plate for the ratchet and pawl (the
  pawl's pin and bracket from the offcut); a rod for the crank (the web forged from it; the handle is
  wood); the game's chain, which the draw bench also takes.

**Every step can be built** (the validator's `check_build`): at rest, the frame and then each winch stage in
turn are joined to the ground through what is already there and themselves, nothing floating, and no part
comes before what it is fitted onto (`NEEDS`: a gear its shaft, the drum its shaft, a band what it binds,
the ratchet and crank the crank shaft, the crank the ratchet, the chain the drum and the sheave, the spine
the ring, the body the spine). The body's stages then fit onto the spine as before.

## The body: baked, not animated

The gantry's shape carries the eidolon's elements **baked into the hung pose**: each element is posed
by `../Eidolon/tools/kin.py` (the game's pose maths) at `hung`'s one frame and written as a plain
static element, its hierarchy flattened, the body's renamed `b_<stage>_<name>` by its build stage
(`assets/seraphhorizons/config/eidolon-stages.json`) and the spine's `sp_<name>`: the spine
(`spine.json`, written from vanilla by the eidolon's generator) is hung back on the chest block to be
posed with it. Elements with no drawn face (`origin` and the
invisible anchors) are left out; disabled faces and wind data are dropped; glow is kept (the mind's
cores glow).

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
build names: its **Build state** select (the scenario's `states`, `docs/recipe-browser/models.md`)
steps from "The frame" through each winch stage, "The spine on the chain" and each stage of the body to
"Mind: fully built", where the page opens, and has "Departed: awake and gone", which fits the winch and
the spine and none of the body.

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
counts the model's: the frame's timbers, the axles' lengths, the bands); the spine and body one piece
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
role (the wood-variant codes on the frame, drum, sheave and handle, `wood-end` only on wood, `mechanics` on the axles and gears); no z-fighting among the gantry's own faces (after `fix_coplanar`); and in the written rig, the
crank's cell in the footprint, hollow, with no boxes. `tools/tests/test_eidolongantry_model.py`
holds the written files to these without the generator's state, and this README's stage table to the
generator's.

## Open questions

- **The body's stages are not one item each.** The eidolon's stages (`../Eidolon/README.md`, "Build
  stages") each list several ingredients (the torso a gearbox, two tanks, a pump head and two steel
  plates), which does not follow the machines' pattern of one plain item a stage, a click taking all of
  it. Fitted that way they would need splitting into one-item stages (the torso's gearbox, then its tanks,
  ...), each its own `requires` with what it draws, or a different interaction for the body. The body's
  stages are unchanged here.
- **The spine's item.** The spine stage has no item yet: vanilla's mast is charred wood with three steel
  clamps, a pulley and ropes, none of which the game has as a plain item. It could take support beams and a
  steel plate as two stages, or come with the chain stage (the chain hanging it as it goes on).
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
