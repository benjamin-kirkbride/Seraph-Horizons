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
- **The body hangs in the middle of cell (2, 0, 2)**: the entity's position, model (8, 0, 8), is the
  build frame's (40, 0, 40). Round it there is 1.28 blocks to the front posts, 1.13 and 1.14 to the
  side posts and 1.44 to the winch at the back, to walk round it and fit parts.
- **The winch is at the back**: two oak cheeks bolted to the back posts carry an iron axle in iron
  bearing plates, with an oak drum (iron hoops, the chain coiled at its middle) and an iron crank with an
  oak handle on the right (north) side, worked from inside the gantry. The chain leaves the drum's top
  and runs up and forward, tangent to both, onto one oak sheave hung in iron hangers under the hoist
  beam, and drops from it straight down to a swivel eye over the body.
- **What holds the body**: an iron ring from the eye over the top peg of the body's own spine
  (`spine-hook1`, the spine's top hook between the shoulder blades), its bottom bar under the peg. The
  torso is the first stage (`../Eidolon/README.md`, "Build stages"), so the peg is there from the first
  part fitted on: the ring is fitted with the torso (the rig's `torso` value) and holds the body
  through every stage after it, each fitting onto what already hangs. (While the pelvis came first the
  gantry also had a rope sling from the eye to two eyebolts on the waist, for the stages before the
  spine; with the torso first nothing needs it, and it is gone.)

The body's pose is `hung` (limp, head down, the lowest toe 3 voxels off the floor). Searching the posed
body for a way to hang it showed why the spine's peg: no straight line reaches the hip block from above
without passing through the heart, the hood or the arms, which hang tight against the waist.

## The body: baked, not animated

The gantry's shape carries the eidolon's elements **baked into the hung pose**: each element is posed
by `../Eidolon/tools/kin.py` (the game's pose maths) at `hung`'s one frame and written as a plain
static element, its hierarchy flattened, renamed `b_<stage>_<name>` by its build stage
(`assets/seraphhorizons/config/eidolon-stages.json`). Elements with no drawn face (`origin` and the
invisible anchors) are left out; disabled faces and wind data are dropped; glow is kept (the mind's
cores glow).

Why baked, for the in-game renderer: a block draws static elements in the chunk mesh, or a renderer
tessellates them once per stage into a mesh it draws with one matrix (the hook's let-down), as the
machines' moving parts are drawn. Carrying the `hung` animation instead would need the entity
animator on a block (the joint shader pass for over 200 elements, the animation run every frame) to show a
pose that never changes, and stage toggles would have to hide joints' elements in the animator. The
cost of baking is a copy: the gantry's shape must be regenerated when `eidolon.json` changes, and
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
| `winch` (drum, axle, hoops, coil, crank) | | `rotate` θ at ratio 0 (the crank's clock; moves nothing), `step` rotate by depth: the drum pays out the drop |
| `sheave` | | `step` rotate by depth, the same chain |
| `lead` (the drum-to-sheave chain) | | none: its ends do not move |
| `fall` (the hanging chain) | | `stretch` by depth about the sheave: it lengthens by the drop |
| `hook` (the swivel eye) | | `feed`: down by the drop at depth 1 |
| `ring` | `torso` | ride `hook` |
| `torso`, `pelvis`, `legs`, `arms`, `head`, `mind` | the same | ride `hook`: the body comes down with the eye |
| `frame` | | |

**depth** is the winch let down, 0..1: 0 hung, 1 the lowest toe on the floor (`winch.drop`, 3 voxels).
The cells' boxes are built from the gantry's own parts at rest (frame, winch, sheave); the body's
collision and selection are gameplay's (it changes stage by stage). Cells with none of the gantry are
`hollow`.

Anchors: `body` (the entity's position), `hang` (where the ring bears on the peg), `fit` (the chest's
middle, where parts are fitted), `exit` with `exitSide: "west"`, and `winchCell` (the crank's cell).

## Regenerating

```sh
python3 mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py                 # write the three files
python3 mods-src/seraphhorizons/EidolonGantry/tools/make_shape.py --out "$TMPDIR/g"   # elsewhere
```

It reads `eidolon.json` and `eidolon-stages.json` from the repository (no game needed), takes about
2 seconds, and writes `assets/seraphhorizons/shapes/block/eidolongantry.json` (everything),
`eidolongantry_frame.json` (the frame part only) and the rig. Output is deterministic. Regenerate after
every change to the eidolon's shape or stages.

## Validation (`tools/validate_eidolongantry.py`)

Every run: elements in their parts, Euler round trip; the baked body against kin's hung pose; every
drawn eidolon element in the gantry once, in its stage, the body one piece after every stage (the
eidolon's rule; the chest is shown before the hip block it hangs from in the hierarchy, which baking
makes harmless), the spine's peg in the first stage and the ring fitted with it; the lowest toe
at 3 voxels at depth 0 and on the floor at depth 1; everything in the machine box; the body touching
nothing of the gantry (but the ring's bottom bar under the peg) at depth 0, 0.5 and 1, the ring clear
of the rest of the body; the peg inside the ring;
the lead chain tangent to drum and sheave, the fall on the sheave's tangent and on the eye at every
depth, drum and sheave turning with the chain; the frame one piece from the ground, the axle in both
cheeks and the sheave's pin in both hangers; the eidolon standing where it hung (vanilla's rest pose)
walking out west touching nothing; at least a block to walk past the body on every side; textures by
role; no z-fighting among the gantry's own faces (after `fix_coplanar`). `tools/tests/test_eidolongantry_model.py`
holds the written files to these without the generator's state.

## Open questions

- **Size.** 6×5×5.5 blocks is set by the body at size 1 (3.75 blocks, arms hanging 2 blocks wide) and a
  block of room round it; a smaller eidolon would shrink it.
- **Wood and look.** Debarked oak and iron plate throughout; whether the gantry should take the wood it
  is built from (a texture code per wood) is open.
- **Hand-off at activation.** `activate` starts from `hung` with the toe 3 voxels up and lets the body
  down itself (frames 26 to 44). The let-down here is the gantry's own (for docking, or to show the
  winch working); gameplay must choose whether the entity takes over at depth 0 (and the chain pays
  out unseen) or the hung pose is made to start lower.
- **The hang's balance.** The peg is behind the body's centre of mass, so a real body hung there would
  swing back; the `hung` pose leans forward. A second line to the shoulders, or a hung pose that leans
  less, would make it hang true.
