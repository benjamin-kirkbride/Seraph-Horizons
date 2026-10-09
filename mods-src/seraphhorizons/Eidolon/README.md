# Eidolon

Part of the Seraph Horizons mod (`../README.md`): a player-built eidolon, a laborer automaton that
fells trees with a real axe it holds, hauls Logging Expanded trunks to the rosser and the bucking
sawmill, carries a Carry On container as a pack mule, lifts and carries Carry On blocks and trunks,
defends itself and stands guard. At 0 HP it slumps disabled and can be repaired; it is never killed.
It is built stage by stage in a mostly wooden gantry.

**Status: the model only.** This folder holds the shape's generator (`tools/`). There is no gameplay
yet: no entity type, no AI, no gantry. The shape and the stage map below are what those will be
built against, and the stage map and its ingredients are a **proposal for review**.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/`, which is the repository's.

## The shape

The body is the game's unused mobile eidolon, `game:shapes/entity/lore/eidolon/normal.json`
(Anego Studios' model; credited in `../CREDITS.md`), frozen into our namespace so a game update
cannot change it under us. `tools/make_shape.py` reads it from a game install, checks its sha256
against the 1.22.7 file (`VANILLA_SHA256`; a different file stops the script) and writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/entity/eidolon/eidolon.json` | The shape: vanilla's 218 elements unchanged but for the spine's 29, cut off (below), so 189, and three anchor elements, five new attachment points, 23 vanilla animations and 18 authored ones. Strict JSON. |
| `assets/seraphhorizons/config/eidolon-stages.json` | The build stages, each with its ingredients (proposal) and the element names it adds. |
| `assets/seraphhorizons/config/eidolon-rig.json` | For the model viewer: a part per build stage, matching its elements by name, needing its own `requires`, with no drivers (below). Not read by the game. |
| `../EidolonGantry/spine.json` | The spine, as vanilla has it, and the element it hangs from (`chest-inside`): the gantry generator's input. Not shipped. |

**The spine is the gantry's.** Vanilla's model has a charred-wood mast down its back, `spine1` (a
child of `chest-inside`) and everything under it: `spine1`..`3`, the pulley (`pulley-rope`,
`pulley-capL`/`R`) with its small winch handle (`winch-handle1`..`3`), the ropes (`spine-rope1`..`3`),
staples (`spine-staple1`..`8`), hooks (`spine-hook1`..`4`), platforms (`spine-platformL`/`R`) and three
steel clamps (`bar-spine1`..`3`), 29 elements. Here it belongs to the gantry: the body is clamped to it
while it is built, hanging from the gantry's chain by a ring over its top peg (`spine-hook1`), and when
the eidolon wakes it unclamps and steps off, leaving the spine hanging in the gantry. So the generator
cuts it off the shape (`SPINE_ROOT`), takes it out of every animation's keyframes (vanilla animates
`spine1` in most of them; nothing else of it), and writes it unchanged to `../EidolonGantry/spine.json`,
which the gantry hangs back on the chest block to pose it with the body (`../EidolonGantry/README.md`).

**Size and axes.** At entity size 1 the model is 60 voxels (3.75 blocks) tall and its feet are at
y 0. It faces **−x**; its right side is **−z**; the entity's position is model (8, 0, 8). Vanilla's
boss entity uses size 1. The contact poses (hands on a block or trunk, a trunk on the shoulder or in the arms) are
made for size 1 (`SIZE` in the generator); see "Open questions".

**Textures.** Nothing is copied. Vanilla's texture map has no domain, so from our namespace each
path would resolve to `seraphhorizons:`; every path is written `game:` explicit. One restyle reads
the body as player-built: the rusty iron armour (`#rusty-iron`: the hood, chest and shoulder plates,
tassets, bracers and finger plates) becomes the game's tarnished
brass, Jonas's metal (`game:block/metal/tarnished/brass`), 87 elements, and the gantry's spine's
staples, drawn from the same map. The joints and bars are already the game's tarnished steel. `make_shape.py --vanilla-look` writes vanilla's map instead; `RESTYLE` is the
one place to change it.

**Anchors.** Three invisible elements, children of `chest-inside` and built like vanilla's own `origin`
(zero size, every face disabled), carry the `Carry`, `Trunk` and `ThickTrunk` attachment points. The
animations move them, so a carried block or trunk follows a designed path between the hands (onto the
shoulder, up from and down to the ground) and moves with the torso when carried (a thick trunk only standing; walking it keeps its height
above the hips). At rest
`carry-anchor` is where the block is held in carry-idle, `trunk-anchor` where a thin trunk lies in
trunk-carry-idle, and `thick-trunk-anchor` where a thick trunk is held in trunk-thick-carry-idle,
before that pose's backward lean (which brings it to (−25.2, 24, 8)).

Thick trunks have their own anchor rather than sharing `trunk-anchor`: the two are held in different
places (on the shoulder, front to back; low in front, across), so each anchor's rest is its own idle
pose, the gameplay picks the point by the trunk's size and draws the trunk there the same way, and
neither set of animations has to move the other's anchor away from rest. It costs one joint (38).

## Attachment points

| Code | Element | Where | For |
|---|---|---|---|
| `RightHand` | `wristR` | The palm, (0.6, 0.6, 2.0) | The axe. The code the game's held-item renderer looks up (`EntityShapeRenderer`). Turned (157.5, 0, −180) so that at rest its frame is the seraph's `RightHand` at rest (its `ItemAnchor` turned −180 about y): an item's `tpHandTransform` sits in this hand as in a player's when the forearms are posed alike. |
| `LeftHand` | `wristL` | The palm, (0.6, 0.6, −0.9) | The left hand's item, the same way. |
| `Carry` | `carry-anchor` | The centre of a carried block's underside; the frame is the model's | A Carry On block or container (a block model's origin is its corner: draw it at (−0.5, 0, −0.5) block). |
| `Trunk` | `trunk-anchor` | The middle of a trunk's underside, TrunkEntities' origin (`TrunkBoxes`) | A thin Logging Expanded trunk (1×1×4). Turned 90° about y, so a trunk drawn with its length along its own z (as TrunkEntities draws it) lies front to back on the shoulder. |
| `ThickTrunk` | `thick-trunk-anchor` | The same: the middle of a thick trunk's underside | A thick trunk (2×2×5, Logging Expanded's xxl). Not turned: a trunk drawn with its length along its own z lies across the body, in front, held in both arms. |
| `ObjectR`, `ObjectL` | `wristR`, `wristL` | Vanilla's, kept | Unused by vanilla's mobile eidolon. |

Carry On's own player point is `carryon:FrontCarry` (patched onto the seraph by Carry On); the
eidolon's gameplay will draw its carried block itself at `Carry`.

## Animations

30 frames a second. In the game's format an element's offset, rotation and stretch are each
interpolated linearly between the nearest keys that set them, wrapping from the last key round to
the first; a one-shot's last key is on its last frame so nothing wraps. The new animations move only
the 35 elements vanilla's animations move (less `spine1`, which is the gantry's) plus the three anchors
(38 joints; the game's cap is 230 and its warning threshold 46).

### Kept from vanilla (unchanged but for the spine)

`stand-walk`, `stand-run`, `stand-idle1`, `stand-idle2`, `stand-alert` (walking, running, idling, a
look round: the laborer's own); `stand-punch`, `stand-kick`, `stand-slam`, `stand-slash`,
`stand-throw`, `stand-stagger` (defending itself and guarding: punch, kick, two-fisted overhead slam,
claw slash, throw, the stagger when hit); `weapon-walk`, `weapon-run`, `weapon-idle1`,
`weapon-alert`, `weapon-stab`, `weapon-kick`, `weapon-stagger` (the same with the right hand
holding something: walking and idling with the axe); `stand-inactive`, `stand-activate` (vanilla's
own dormant pose and 10-frame wake-up); `stand-die`, `weapon-die`, `toppleover` (kept, though the
laborer never dies: `slump` is its 0 HP; die pitches it face down and 29 voxels forward). Their keys for
`spine1` are taken out with the spine; nothing else in them changes.

### Dropped

| Code | Why |
|---|---|
| `stand-hurteye`, `weapon-hurteye` | The boss's eye weak point: a hand thrown over the struck eye. |
| `stand-eyesweep` | The boss's eye-beam sweep. |
| `ground-inactive`, `ground-activate`, `ground-idle`, `ground-crawl`, `ground-throw`, `ground-slash`, `ground-slam`, `ground-die` | The legless state the boss falls into after `toppleover`; the laborer never loses its legs. |

### Authored

| Code | Frames | Ends | Stopped | What it is |
|---|---|---|---|---|
| `fell` | 40 (1.33 s) | Repeat | EaseOut | A two-handed chop from over the right shoulder into a trunk in front, knees giving at the cut. **The cut lands on frame 15**, the hands at about (−13, 31, 7) and (−9, 29.5, 8.5): 1.4 blocks in front of the entity's centre at 1.9 blocks up, so with a block-long axe the trunk stands about 2 to 2.5 blocks in front. The left hand slides on the handle as it would (the hands 4.5 to 9 voxels apart). |
| `carry-idle` | 60 | Repeat | EaseOut | Standing with a block held in both arms against the chest, hands on its sides; a slow breath. |
| `carry-walk` | 30 | Repeat | EaseOut | Vanilla `stand-walk`'s legs, hips, spine and skirts with the arms holding the block; set down so the stance foot is on the ground (vanilla's walk lets it sink 2.3 voxels). |
| `lift` | 45 | **Hold** | PlayTillEnd | A block from the ground in front (its underside centre at (−9.5, 0, 8)) into carry-idle's first frame: squat, take it by the sides (grab on frame 22), stand. |
| `setdown` | 45 | Stop | PlayTillEnd | Carry-idle's first frame to the block on the ground where lift takes it from (released on frame 28), then back to rest. |
| `trunk-carry-idle` | 60 | Repeat | EaseOut | Standing with a thin trunk on the left shoulder, front to back, the left hand steadying it on its outer side in front, the head leaning away. (Not asked for; the hauler needs it to wait at an infeed.) |
| `trunk-carry-walk` | 30 | Repeat | EaseOut | Vanilla `stand-walk` with the trunk carried as in trunk-carry-idle; the right arm (the axe hand) swings as vanilla's. |
| `trunk-pickup` | 60 | **Hold** | PlayTillEnd | A thin trunk lying across in front of the feet (its underside middle at (−10.5, 0, 8)): squat, take it by the top (grab on frame 24), stand with it across the chest, turn it front to back and heave it onto the left shoulder, ending in trunk-carry-idle's first frame. |
| `trunk-setdown` | 50 | Stop | PlayTillEnd | The reverse: off the shoulder, across the chest, down where pickup takes it from (released on frame 36), then rest. |
| `trunk-thick-carry-idle` | 60 | Repeat | EaseOut | Standing with a thick trunk (2×2×5) across the body in both arms, low in front of the belly (its top at eye height), the palms on its near side 3 voxels above the bottom edge with the fists curled under it; knees bent, leaning back 8° against the weight, the head bowed to look over it; a slow breath. |
| `trunk-thick-carry-walk` | 30 | Repeat | EaseOut | Vanilla `stand-walk`'s legs, hips, spine and skirts frame for frame (as carry-walk), its chest turned 5° further back, the arms holding it as in the idle. The trunk keeps the idle's place, rising and falling 3 voxels with the hips: riding the walk's hip pitch this far in front it would swing 8 voxels a step, so the arms take the pitch. It is held far enough out (its near side 6 to 8 voxels before the belly) that the swinging knees pass behind it. |
| `trunk-thick-pickup` | 60 | **Hold** | PlayTillEnd | A thick trunk lying across in front of the feet (its underside middle at (−26, 0, 8), the near side 10 voxels past the toes): a deep squat with the knees out and the head up (bowing over it would put the head into a trunk 32 voxels high), take it by its near side low down (grab on frame 26), lift it with the legs and stand, leaning back as it comes up against the belly, ending in trunk-thick-carry-idle's first frame. |
| `trunk-thick-setdown` | 50 | Stop | PlayTillEnd | The reverse: from trunk-thick-carry-idle's first frame down into the squat, the trunk on the ground where pickup takes it from (released on frame 32), then up to rest. |
| `guard-idle` | 80 | Repeat | EaseOut | An alert stance: feet apart, knees bent, leaning in, the axe hand up and the left fist raised, the head sweeping 32° each way. |
| `hung` | 1 | Hold | EaseOut | Limp in the gantry, clamped by the back to its spine: head down, arms hanging, hands loosely curled, toes pointing down, **the lowest toe 3 voxels off the floor** (`HUNG_CLEAR`), **10 voxels behind the entity's position** (`HUNG_BACK`). For the gantry to show while building. |
| `activate` | 90 (3 s) | Stop | PlayTillEnd | The first awakening, from `hung`: the spine's clamps let go and the body lurches forward off the mast at its back (frame 3) and drops onto its feet, knees giving (8, `ACTIVATE_LAND`), sinking into them (12); it stirs (22), the hands open (32), it straightens (40), steps forward away from the spine, right foot (lifted 48, down 56) then left (lifted 64, down 72), to stand where the entity is, looks left and right (78, 84) and stands at rest (89). |
| `slump` | 50 | **Hold** | PlayTillEnd | At 0 HP: the knees give (8), it drops onto its knees (20) and sinks forward, head down and arms limp (32), with a settle (40); held on frame 49, kneeling on the ground. |
| `standup` | 60 | Stop | PlayTillEnd | From slump's last frame: straightens on its knees (14), up onto the right knee with the left foot planted (30), rising (44), standing at rest (59). Start it before stopping slump. |

The event frames (cut, grab, release) are also in `EVENTS` in the generator; the gameplay must key
off them.

**Thick trunks** are carried across the body, low, by the near side. A thick trunk's section is 32
voxels, more than half the body's height, and the arms are about 28 long: cradled on the forearms it
would sit over the head, and hugged higher against the chest it would cover the face. Front to back
it would stick 4 of its 5 blocks out past the hands, all its weight in front of them, and its 32-voxel
width would not fit between the shoulders (21 apart). Across, its weight is centred between the hands
and against the body, the arms come straight down its near side, and the backward lean and bent
knees read as weight. The cost is the face: the trunk's top is at eye height in the idle, and it
cannot be lower without the hands reaching further than the arms do or the trunk coming in closer,
where the walk's knees swing.

**Reused and considered.** The walks reuse `stand-walk` frame for frame (the generator samples it at
its keys, which reproduces it exactly) and change only the arms and head. `fell`'s two-arm raise
follows `stand-slam`'s, which lunges far too hard (hips pitched 80°, 19 voxels forward) to loop at
a trunk. `hung` and `activate` take their curled hands from `stand-inactive` and `stand-activate`,
whose 10-frame wake-up starts from a standing pose, not a hung one. `activate` is played in the gantry,
where the spine still hangs behind the body; on its own (the model viewer) there is nothing at its back
for the first frames, so the clamps let go at once (the body is moving from frame 1 and on its feet by
frame 8). It steps forward because standing at rest where it hung it would be inside the spine (the
hood touches the top clamp until 7 voxels forward): the hung pose is `HUNG_BACK` behind the entity's
position, and the steps bring it there. `stand-die` was the candidate for
`slump` but ends face down far forward, a poor pose to repair or stand up from. `weapon-walk`,
`weapon-idle1` and `weapon-run` already hold something in the right hand and serve for walking with
the axe as they are; `stand-alert` and `weapon-alert` are 80-frame one-shots (they stop), not loops, so `guard-idle`
is new.

**How the poses are made.** Each key is a hand-set body pose, then solved with `tools/kin.py`, a copy
of the game's pose maths (`ShapeElement.GetLocalTransformMatrix`, `Mat4f.RotateByXYZ`,
`Animation.GenerateFrameForElement`, read from the decompiled 1.22.7 API): the feet are turned flat
and the hips moved so the lowest sole is on the ground (or the lowest leg part, kneeling); the anchors
are set to their world place (a block or trunk on the ground stays put while the body bows over it);
and each arm (shoulder in three axes, elbow) is solved by a deterministic search for its palm to reach
its grip, starting from and preferring a hinted pose; a foot set on a place (activate's steps) is
solved the same way, thigh, knee and foot, flat, the hips moved over the planted feet and lowered until
both reach. Where the game's interpolation between two keys
would let a planted foot, a grounded object or a grip drift by more than 0.4 voxel, keys are added
between them. Elements at rest in every key are left out of an animation, so other animations blend
through them. Angles, for reading the keys: rotation z bows the hips, chest and head forward when
positive, swings an arm or thigh forward when negative and bends a knee when positive or an elbow
when negative; rotation x moves the right arm or leg out when positive, the left when negative.

### Pose checks

`make_shape.py --report` prints these from the written shape, frame by frame as the game plays it
(voxels, model size 1):

| Check | Result |
|---|---|
| Lowest sole, every frame of `carry-walk`, `trunk-carry-walk` and `trunk-thick-carry-walk` (vanilla `stand-walk` for reference) | −0.24 .. 0.41 (vanilla −2.3 .. 0.61) |
| Soles at every planted key of `fell`, `guard-idle`, `lift`, `setdown`, `trunk-pickup`, `trunk-setdown`, `trunk-thick-pickup`, `trunk-thick-setdown`, `trunk-thick-carry-idle`, `standup` | within 0.01 of the ground |
| `activate`: the lower sole at every key from the landing (8) on (one foot is up while it steps); every frame from 8 on | within 0.01; within 0.3 (the planted foot, between keys) |
| `hung`'s lowest toe | 3.0 above the floor |
| `slump`'s held frame | lowest leg part on the ground; the hood's top at 40.7 (from 60) |
| Hands to their grips, every frame they hold: block (carry, lift, setdown), trunk on the shoulder, trunk on the ground | ≤ 0.34 |
| Hands to a thick trunk's near side: carry-idle and carry-walk every frame; pickup from the grab (26) on and setdown up to the release (32) | ≤ 0.16; ≤ 0.37 |
| A block or trunk on the ground, while the body moves over it (thick: pickup 0..26, setdown 28..49) | moves ≤ 0.39 (thick ≤ 0.25) |
| A thick trunk's underside at the grab (pickup frame 26) and release (setdown frame 32) | 0.0 |
| A thick trunk against the body, every frame of its four animations, against its square collision section (`TrunkBoxes`, stricter than the rounder model; 27 points sampled on each element) | nothing inside it, the arms included down to the wrists (the hands hold it); the head and hood at least 1.6 away (closest at the grab's squat) |
| `fell` hands at the cut | within 0.3 of the targets; wind-up with the right hand at 62 (above the 60-voxel head) |
| A thin trunk on the shoulder against the head and hood | no corner of the head inside it |

## Build stages (proposal)

The gantry shows the body cumulatively, stage by stage, by element name; `eidolon-stages.json`
lists each stage's elements. Each stage claims some root elements and everything under them that no
other root claims, so every element is in exactly one stage. The spine is in none: it is the gantry's,
hanging in it by the ring over its top peg before anything is fitted. The torso comes first, the chest
block with its plates, clamped onto the spine by the spine's steel clamps, so the spine holds the body
from the first part fitted on. The
ingredients are the first proposal, matched to the model's parts; the steel comes to **20 ingots** (a
`metalplate` is 2 ingots, a `rod` 1).

**Attached, not parents first.** In the shape's hierarchy the chest block hangs off the hip block
(`origin` > `hip-inside` > `chest-inside`), so with the torso first the chest's parent arrives a stage
after it. That is harmless: the entity only exists built whole, and the gantry draws the body baked
in the `hung` pose, each element a static box at its posed place, so no element shown depends on a
hidden one for where it is drawn. What the order must not do is leave a piece floating, so the rule
(checked in the generator and the tests) is that **after every stage, the elements shown so far are
one connected piece of the hierarchy**, its links taken either way (the gantry checks the same with
the spine there from the start, the torso joined to it): each stage fits onto what is
already there (the pelvis under the chest, the legs on the hip block, the arms and head on the chest,
the cores into chest and head). Re-rooting the claims instead (the torso owning `origin` and
`hip-inside` down to the chest) would have put the hip block, a pelvis part, in the torso.

| Stage | Ingredients | Elements (claim roots; the full list is in the stages file) | What they are in the model |
|---|---|---|---|
| 1 Gantry | Wood: the gantry's own model | None | The frame it hangs in. |
| 2 Torso (18) | `game:eidolongearbox` (disabled in vanilla; to be enabled), `game:jonasparts-tank01`, `game:jonasparts-tank02`, `game:jonasparts-pumphead`, 2 `game:metalplate-steel` (4 ingots) | `chest-inside`, `collar-front`, `collar-R`, `collar-L`, `chest-plateR`, `chest-plateL`, `chest-backplate`, `chest-sideplateR`, `chest-sideplateL`, `chest-sash2`, `bar-chestR1`, `bar-chestR2`, `bar-chestL1`, `bar-chestL2`, `carry-anchor`, `trunk-anchor`, `thick-trunk-anchor` | The lattice chest block, its plates, collar and sashes (and the three invisible anchors), clamped to the gantry's spine. |
| 3 Pelvis (15) | `game:eidolongearbox`, `game:jonasframes-gearbox02`, 2 `game:metalplate-steel` (4 ingots) | `origin`, `hip-inside`, `hip-tassetR`, `hip-tassetL`, `back-tassetR`, `back-tassetL`, `waist-fauld`, `chainskirt-back1`, `chainskirt-front1`, `bar-hip` | The lattice hip block under the chest block (the gearbox), the leather fauld with its tassets, the brass hip and back tassets, the chain skirts. |
| 4 Legs (55) | 2 `game:jonasframes-joint01` (the knees), `game:jonasframes-spring01`, 2 `game:rod-steel`, 2 `game:metalplate-steel` (6 ingots) | `bar-legs`, `upperlegR`, `upperlegL` | The hip axle and both legs: wooden thighs and shins, steel knees (`kneeR`, `kneeL`), brass shin and knee plates, feet. |
| 5 Arms (77) | `game:eidolongearbox` (the shoulders), `game:jonasframes-gears01`, `game:jonasframes-gears02`, `game:jonasparts-cylinder01`, `game:jonasparts-valve01`, 2 `game:rod-steel`, `game:metalplate-steel` (4 ingots) | `bar-arms`, `upper-armR`, `upperarmL` | The shoulder axle and both arms: shoulder plates, wooden arm bones, elbows (`elbowR`, `elbowL`), bracers, wrists and hands. |
| 6 Head (21) | `game:jonasframes-oscillator01`, `game:jonasparts-cylinder02`, `game:jonasframes-gearbox01`, `game:jonasparts-connector01`, `game:metalplate-steel` (2 ingots) | `neck`, `head-inside`, `hood-back3`, `hood-back4` | The neck, the lattice head block, the wooden face and head plates, the brass hood, and the eye's bracket (`Eye-bracket`), without the eye's light. |
| 7 Mind (6) | `game:rustypart-eidolon2tr` (the Eidolon elucidatory vessel), `game:gear-temporal` (its charge, a quarter in-game year) | `brain`, `heart`, `Eye-out` | The two glowing cores, the brain in the head (the vessel) and the heart in the chest (the temporal gear), with their bars, and the eye's red light: what lights up when it wakes. |

**In the model viewer** the eidolon's page shows the stages too: a **Build state** select (torso, pelvis,
legs, arms, head, mind: fully built, where it opens) and a checkbox per stage, from
`eidolon-rig.json`, a part per stage (`rig_file()`). Its parts have no drivers, so each part's matrix is
the identity and the shape's animations move the elements within them as they do the whole body: any set
of stages plays any animation, the elements shown where the game poses them however many of the joints
above or below them are hidden (`docs/recipe-browser/models.md`, "Keyframe animations"). An element is
in the first part matching a name in its chain, so the rig lists each stage before those its elements
hang from (`rig_order()`: the legs, arms, mind and head before the torso, the torso before the pelvis),
and the generator checks every element lands in its own stage.

Some elements under the hip and chest blocks belong to other stages by design: `bar-legs` (the
legs' axle, under `hip-inside`) and `bar-arms` (the arms', under `chest-inside`) arrive with their
limbs; `neck`, `hood-back3` and `hood-back4` (under `chest-inside`) arrive with the head; `heart`
(under `chest-inside`), `brain` (under `head-inside`) and `Eye-out` (under `Eye-bracket`) arrive with
the mind; and the chest block itself (under `hip-inside`) arrives before its parent, as above.

## Regenerating

```sh
VINTAGE_STORY=$HOME/Games/vintagestory python3 mods-src/seraphhorizons/Eidolon/tools/make_shape.py          # write the four files (about 70 s)
VINTAGE_STORY=$HOME/Games/vintagestory python3 mods-src/seraphhorizons/Eidolon/tools/make_shape.py --check  # compare only
python3 mods-src/seraphhorizons/Eidolon/tools/make_shape.py --report                                         # the pose checks
```

The output is deterministic (no randomness, values rounded to 0.001). Regenerate the gantry after it
(`../EidolonGantry/README.md`): it bakes this shape and the spine. Edit the generator, not the
shape: the authored animations are in `authored()`, the geometry constants (grips, where a block or
trunk is held and set down, the cut) at the top.

## Tests

`tools/tests/test_eidolon_model.py` (stdlib unittest): the committed files are strict JSON (no
NaN, no duplicate keys) and written as the generator writes them; with `$VINTAGE_STORY` set the
generator runs and must reproduce them (and `spine.json` and `eidolon-rig.json`) exactly; element names are unique; the spine is
in neither the shape nor any animation, and is in `spine.json` whole; every texture is
`game:` and every enabled face's texture is in the map; the attachment points are on their elements;
the kept, dropped and authored codes and their lengths and end handling; every animation names only
existing elements and fields, with each offset, rotation or stretch set as a whole; the moved
elements stay vanilla's joints plus the anchors; one-shots end on their last frame and meet the
poses they lead to (`lift` → `carry-idle`, `slump` → `standup`, `hung` → `activate`, …) and the
setdowns, `standup` and `activate` end at rest; `activate` starts `HUNG_BACK` behind the entity's position,
is moving by frame 3 and has a foot on the ground every frame from the landing on; the pose checks above within 0.5 voxel (and for a
thick trunk: nothing of the body inside it, the head and hood over 1 voxel away, the trunk on the
ground at the grab and release); and the stage map covers every element exactly once, the body one piece after every stage (a stage
that would leave a piece floating is caught), the torso first, as the stages file has it; and the viewer's
rig a part per stage with no drivers, every element in its own stage's part by the viewer's rule (a
part listed too late is caught).

## Open questions

- **Size.** At size 1 the eidolon is 3.75 blocks tall (vanilla's boss size), a giant beside a
  1.85-block player, and an axe or block drawn at its true size in its hand looks small. A smaller
  entity (say 0.5, about player height) shrinks its carried things with it unless the renderer scales
  them back, and the contact poses then need remaking for that size (`SIZE`, which every block and
  trunk measure is divided by).
- **The axe's orientation in the hand** comes from the item's `tpHandTransform` in the `RightHand`
  frame, matched to the seraph's at rest; whether the head points at the trunk through the swing can
  only be seen in the game.
- **Ingredients and steel** per stage are the first proposal; the counts are to taste.
- **The restyle** is one texture (rusty iron to tarnished brass); more (the charred wood bones, the
  rusty chain skirt) is a matter of taste.
