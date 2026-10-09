# Eidolon gantry

Part of the Seraph Horizons mod (`../README.md`): the frame the player-built eidolon (`../Eidolon/README.md`)
is assembled in, stage by stage, and its dock afterwards, for repair and recharge.

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity
or renderer yet; the model and its rig are what those will be built against.

Paths here are from this folder unless they start with `assets/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/`, which is the repository's.

## The gantry

An open oak frame of 6×6 timbers, **6 blocks deep (x) by 5 wide (z) by 5.5 high** (cells 6×6×5, the top
row half used): four corner posts, side head beams over them, front, back and hoist beams between, sills
on the sides and back, side rails at 2.5 blocks (a player walks under them), knee braces at every top
corner, iron fish plates and angle brackets at the post tops and trenail heads on the beams and rails.

- **The front is west (−x), the way the body faces, and open** from the ground to the front beam
  (5.1 blocks) across 4.25 blocks between the front posts: there is no front sill, and the front knees
  are short, over the top corners only. The eidolon walks straight out of it when it wakes.
- **The body hangs round the middle of cell (2, 0, 2)**, the eidolon's `hung` pose 10 voxels
  (`HUNG_BACK`) behind the entity's position, model (8, 0, 8), which is the build frame's (30, 0, 40):
  where it stands once it has woken and stepped off the spine. Round the hung body there is 1.28 blocks
  to the front posts, 1.13 and 1.14 to the side posts and 1.44 to the winch at the back, to walk round
  it and fit parts.
- **The winch is at the back**: two oak cheeks bolted to the back posts carry an iron axle in iron
  bearing plates, with an oak drum (iron hoops, the chain coiled at its middle). **The crank is outside
  the frame**: on the left (south) the axle runs on past its cheek, through an iron pillow block bolted
  to the back left post's front face, and out past the post's outer face (z 80) by 5 voxels, where an
  iron collar keeps it from sliding in and an iron crank (radius 6.5 voxels) with an oak handle pointing
  outwards is turned by a player standing south of the gantry, about 1.5 blocks up. A full turn clears
  the frame (checked every 10°): the crank turns 2 voxels outside the post. Drum, axle and crank are one
  part and turn together. The chain leaves the drum's top
  and runs up and forward, tangent to both, onto one oak sheave hung in iron hangers under the hoist
  beam, and drops from it straight down to a swivel eye over the body.
- **The spine is the gantry's, and holds the body**: vanilla's eidolon has a charred-wood mast down
  its back (`spine1` and its 28 children: the mast in three lengths, a pulley with a small winch handle,
  ropes, staples, hooks, two little platforms and three steel clamps). Here it is cut off the eidolon
  (`../Eidolon/README.md`, "The spine is the gantry's") and hangs in the gantry from the start, empty, in
  `hung`'s pose: an iron ring from the eye over its top peg (`spine-hook1`), the ring's bottom bar under
  the peg. The torso, the first stage, is clamped onto it: the spine's steel clamps (`bar-spine1` and
  `bar-spine2`) run forward through the chest's backplate, and every stage after fits onto what already
  hangs. When the eidolon wakes (its `activate`) the clamps let go, it drops onto its feet, steps forward
  off the spine and walks out of the front, and the spine is left hanging empty, as before the build.

The body's pose is `hung` (limp, head down, the lowest toe 3 voxels off the floor), and the spine is
baked with it where vanilla has it on the chest (tilted with the chest's lean). Searching the posed
body for a way to hang it showed why the spine's peg: no straight line reaches the hip block from above
without passing through the heart, the hood or the arms, which hang tight against the waist.

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
| `winch` (drum, axle, hoops, coil, collar, crank, handle) | | `rotate` θ at ratio 0 (the crank's clock; moves nothing), `step` rotate by depth: the drum pays out the drop, drum, axle and crank turning together, 0.75 rad (43°) over the let-down (the drum's radius is 4 voxels, the drop 3) |
| `sheave` | | `step` rotate by depth, the same chain |
| `lead` (the drum-to-sheave chain) | | none: its ends do not move |
| `fall` (the hanging chain) | | `stretch` by depth about the sheave: it lengthens by the drop |
| `hook` (the swivel eye) | | `feed`: down by the drop at depth 1 |
| `ring`, `spine` | | ride `hook`: the gantry's, there from the start |
| `torso`, `pelvis`, `legs`, `arms`, `head`, `mind` | the same | ride `hook`: the body comes down with the eye and the spine |
| `frame` | | |

**Before the build and after the awakening** the gantry looks the same: the spine hanging empty from
the ring. In the model viewer that is every stage unticked (the torso's label says so). There is no
`departed` value: a `requires` can only add a part, not hide others, and the empty spine is what the
stage-1 gantry already shows; gameplay draws the body's stages only while the eidolon is in it.

**depth** is the winch let down, 0..1: 0 hung, 1 the lowest toe on the floor (`winch.drop`, 3 voxels).
The cells are the 6×6×5 machine box and one more outside it, **the crank's cell `[5, 1, 5]`**, south
of the back left post, a block up (the axle's end, the collar, the crank and its handle): 181 in all. The
cells' boxes are built from the gantry's own parts at rest (frame, winch, sheave; the crank's cell gets
the crank pointing up); the body's and the spine's collision and selection are gameplay's (they hang and
move with the winch). Cells with none of the gantry are `hollow`.

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
takes about 4 seconds, and writes `assets/seraphhorizons/shapes/block/eidolongantry.json` (everything),
`eidolongantry_frame.json` (the frame part only) and the rig. Output is deterministic. Regenerate after
every change to the eidolon's shape or stages.

## Validation (`tools/validate_eidolongantry.py`)

Every run: elements in their parts, Euler round trip; the baked body and spine against kin's hung pose;
every drawn eidolon element in the gantry once, in its stage, and the spine's in the spine part; the
spine and ring needing nothing; the spine and body one piece after every stage, the spine there from
the start (the eidolon's rule; the chest is shown before the hip block it hangs from in the hierarchy,
which baking makes harmless), and the torso touching the spine (its clamps); the lowest toe at 3 voxels
at depth 0 and on the floor at depth 1; everything in the machine box or the crank's cell; a full turn
of the winch clearing the frame, the crank in its cell; the body touching nothing of the gantry but the
spine, and the spine nothing but the ring's bottom bar under its peg, at depth 0, 0.5 and 1; the
eidolon's `activate`, frame by frame where it hung, touching nothing of the gantry and clear of the
spine by frame 56 (it is from 51); the peg inside the ring;
the lead chain tangent to drum and sheave, the fall on the sheave's tangent and on the eye at every
depth, drum and sheave turning with the chain; the frame one piece from the ground, the axle in both
cheeks and the pillow block and out of the frame, and the sheave's pin in both hangers; the eidolon
standing where it woke (vanilla's rest pose) walking out west touching nothing, the empty spine included; at least a block to walk past the body on every side; textures by
role; no z-fighting among the gantry's own faces (after `fix_coplanar`). `tools/tests/test_eidolongantry_model.py`
holds the written files to these without the generator's state.

## Open questions

- **Size.** 6×5×5.5 blocks is set by the body at size 1 (3.75 blocks, arms hanging 2 blocks wide) and a
  block of room round it; a smaller eidolon would shrink it.
- **Wood and look.** Debarked oak and iron plate throughout; whether the gantry should take the wood it
  is built from (a texture code per wood) is open.
- **Hand-off at activation.** `activate` starts from `hung` at depth 0, the toe 3 voxels up: the
  clamps let go and the body drops onto its feet (frames 1 to 8) and steps off the spine. Gameplay should
  wind the winch up to depth 0 before waking it; the let-down here is for docking, or to show the winch
  working.
- **The empty spine's lean.** Baked in `hung`'s pose, the spine keeps the chest's lean (about 18°) when
  the body has gone; a mast hung by its top peg would swing plumb. Hanging it plumb when empty would need
  a second baked copy (or a renderer turning it), and is left for now.
- **The crank's gearing.** The crank is on the drum's axle, so the let-down turns it only 43°. A geared
  winch (a pinion on the crank's shaft driving a gear on the drum) would turn it several times.
- **The hang's balance.** The peg is behind the body's centre of mass, so a real body hung there would
  swing back; the `hung` pose leans forward. A second line to the shoulders, or a hung pose that leans
  less, would make it hang true.
