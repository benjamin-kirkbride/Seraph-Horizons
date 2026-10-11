# Eidolon

Part of the Seraph Horizons mod (`../README.md`): a player-built eidolon, a laborer automaton that
fells trees with a real axe it holds, mines out a marked area bare-handed, hauls Logging Expanded
trunks to the rosser and the bucking sawmill, carries a Carry On container as a pack mule, lifts and carries Carry On blocks and trunks,
defends itself and stands guard. At 0 HP it slumps disabled and can be repaired; it is never killed.
It is built in a mostly wooden gantry: a crafted torso, then five layers of material, each the builder's
choice of wood, metal, leather or cloth, which set its look and its stats and can be stripped and refitted.
It never heals by itself (only repair restores health), and creatures leave it be while it is down.

**Status: the model and the entity.** This folder holds the shape's generator (`tools/`) and the
entity (#673): its type, ownership, charge, slumping and a first pathfinder for its size, in `Core/`
and `Game/`, described in the mod's README ("Eidolon"), with the seams the gantry, upkeep, command tool
and jobs plug into; the body stages and the waking (#672: the gantry's `BEBehaviorEidolonBody`,
`../EidolonGantry/`), oil and repair (#674), and the command tool's binding, mode wheel and marking,
following, staying and self-defence (#675) are built too, and hauling trunks to the rosser and the
bucking mill (#678), mining a marked area (`Game/MineOrder.cs`, `Core/EidolonMining.cs`, its slam
the shape's vanilla `stand-slam`), climbing steps of two (`Core/EidolonClimb.cs`, the `climb` animation)
and keeping its chunks loaded while it works (`Game/EidolonChunkSystem.cs`, `Core/EidolonChunks.cs`).
The other jobs are not built yet.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/`, which is the repository's.

## The shape

The body is the game's unused mobile eidolon, `game:shapes/entity/lore/eidolon/normal.json`
(Anego Studios' model; credited in `../CREDITS.md`), frozen into our namespace so a game update
cannot change it under us. `tools/make_shape.py` reads it from a game install, checks its sha256
against the 1.22.7 file (`VANILLA_SHA256`; a different file stops the script) and writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/entity/eidolon/eidolon.json` | The shape: vanilla's 218 elements unchanged but for the spine's 29, cut off (below), so 189, and three anchor elements, five new attachment points, 23 vanilla animations and 22 authored ones. Its texture codes are the layers of material; strict JSON. |
| `assets/seraphhorizons/config/eidolon-stages.json` | The build stages, each with its ingredients, a layer's material and how its count was measured from the model, and the element names it adds. |
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

**Textures, by layer.** Nothing is copied. Vanilla's texture map has no domain, so from our namespace
each path would resolve to `seraphhorizons:`; every path is written `game:` explicit. The body is built in
layers of material (below, "Build stages"), each the builder's choice, so its texture codes are the
layers: each of vanilla's aged codes is renamed for the layer it is, and the map's paths are the
**default build**, all steel: steel plate and joints, debarked oak bones, plain leather, steel chain.
The entity draws each eidolon with its own layers' paths over these (a per-entity texture override from
its attributes), and the gantry the hung body the same way. Every texture is 32×32, so the UVs fit any
of them unchanged.

| Code | Vanilla | Default path | What wears it |
|---|---|---|---|
| `bones` | `charred`: `wood/charred` | `wood/debarked/oak` | the wooden bones (thighs, shins, feet, arm bones and forearms, the hip and shoulder axles), the face and head plates, and the upper-arm blocks (26 elements; the bones also take the head's lattice block and the vessel) |
| `joints` | `steel`: `tarnished/steel` (rust red) | `metal/plate/steel` | the joints (neck, knees, elbows, ankles, wrists, palms, fingers, thumbs, the eye's bracket) and the bars and clamps (`bar-*`) (57, and the heart's two bars, the mind's) |
| `plates` | `rusty-iron`: `tarnished/rusty-iron` | `metal/plate/steel` | the hood, chest, shoulder, arm, shin and knee plates, the tassets, bracers, finger and palm plates, the foot plates and their nails (87) |
| `sash` | `poor`: `leather/poor` | `leather/plain` | the chest sashes and the fauld with its waist tassets (5) |
| `skirt` | `chain-rust`: `tarnished/chain-rust` | `armor-generic/chain-steel` | the chain skirt (5) |
| `glow` | `rustyglow`: `machine/statictranslocator/rustyglow` | the same | the vessel (`brain`) and the heart |

`lattice` (the inner frames), `fire-red` (the eye) and `reedrope` (the spine's ropes) keep vanilla's codes
and paths. Brass is no longer the plates' default: it is one of their choices. A layer's paths are
`block/wood/debarked/{wood}`, `block/metal/plate/{metal}` (`stainlesssteel` for stainless),
`block/leather/{colour}` or `block/cloth/basic/{colour}` (plain cloth is `normal`) and
`block/metal/armor-generic/chain-{metal}` (the pack ships a stainless chain texture, which the game lacks).

Six elements are **recoded** to another layer than their vanilla code's (`RECODE`), so that no layer
hangs off a later one in the hierarchy (the body stays one piece after every stage, below): the
upper-arm blocks (`upper-armR`, `upperarmL`, steel) carry the arms' wooden bones, so they are bones, under
the shoulder plates; the waist tassets (`waist-tassetR`, `-L`, rusty iron) hang off the fauld, so they are
its tassets, leather or cloth like it; the foot nails (`footR-nail`, `footL-nail`, steel) sit in the foot
plates, so they are the plates'. `make_shape.py --vanilla-look` writes vanilla's codes and map instead
(the stages are the same); `CODES`, `TEXTURES` and `RECODE` are the one place to change them. The gantry's
block type (`blocktypes/eidolongantry/frame.json`) repeats the map for the body built in it, and
`../EidolonGantry/spine.json` is recoded alike (`CODES`; the gantry draws the spine in codes of its own).

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
eidolon draws its carried block itself at `Carry`, and its carried trunk at `Trunk` or `ThickTrunk`, in
its own renderer (`Eidolon/Game/EidolonShapeRenderer.cs`, through `EidolonAttachmentRender`, which any
point can use); the axe at `RightHand` is the game's held-item drawing.

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
| `dock` | 60 (2 s) | **Hold** | PlayTillEnd | Docking onto a gantry's spine (a dock order, to have its layers stripped and refitted; the crank wakes it again), `activate` in reverse: from rest at the entity's position it glances back over its left shoulder (6), steps back, left foot (lifted 14, down 22) then right (lifted 30, down 38), onto where activate lands, and sinks bowed into its knees with its back against the spine (`DOCK_CLAMP`); **the clamps close on frame 46** and lift it off its feet (52, activate's frame 3), and it hangs limp, held on frame 59, `hung`'s pose. |
| `slump` | 50 | **Hold** | PlayTillEnd | At 0 HP: the knees give (8), it drops onto its knees (20) and sinks forward, head down and arms limp (32), with a settle (40); held on frame 49, kneeling on the ground. |
| `standup` | 60 | Stop | PlayTillEnd | From slump's last frame: straightens on its knees (14), up onto the right knee with the left foot planted (30), rising (44), standing at rest (59). Start it before stopping slump. |
| `strike-jab` | 24 (0.8 s) | Stop | PlayTillEnd | A straight right on the move, **the upper body only** (`STRIKE_ELEMENTS`: chest, head, arms, hands): from a guard (fists up before the chest) the right fist is drawn back as the chest turns away (4), then driven out as it turns in and leans after it, the left pulled back; **it lands on frame 9**, the fist 1.9 blocks in front of the centre (`STRIKE_FIST_JAB`), then back to the guard. |
| `strike-hammer` | 30 (1 s) | Stop | PlayTillEnd | A two-fisted hammer blow on the move, the upper body only: both fists raised over the head as the chest leans back (7), then brought down together as it bends forward over them; **it lands on frame 14**, the fists 1.4 blocks in front and a block up (`STRIKE_FIST_HAMMER`; the arms reach no lower with the hips the walk's), then back to the guard. |
| `climb` | 60 (2 s) | Stop | PlayTillEnd | Up a step of two, posed against the entity's scripted move (below, "Climbing"): a crouch and a look up, the arms coming up out to the sides (4) and over the ledge (8); **the hands on the ledge's top on frame 12** (`CLIMB_GRAB`), at (−11.3, 35.4, 1) and (−11.3, 35.4, 15): 19 voxels in front of the entity's centre, a block and a fifth, 3 past the ledge's face, the palms' middles 3.5 above its top at 32 (`CLIMB_HANDS`, `CLIMB_PALM`, the hand's thickness); it pulls itself up as the entity rises, bowing further over its hands (to 45°) with the legs tucked behind it, clear of the face (16 to 28), lets go and brings the right knee up, the foot tucked under it over the edge (30, 32), **the right foot on the step on frame 36** (`CLIMB_TOP`, where it stands at rest on the top node) with the left leg hanging, stands up on it as the entity moves forward, the left foot up beside the edge (44), over it (48) and down beside the right (**standing on frame 52**, `CLIMB_STAND`), then at rest (59). |

The event frames (cut, grab, release, hit, the climb's grab, top and stand) are also in `EVENTS` in the
generator; the gameplay must key off them (the strikes' in `Eidolon/Core/EidolonStrikes.cs`, the climb's in
`Eidolon/Core/EidolonClimb.cs`, which `tools/tests/test_eidolon_model.py` holds to these and to how far the
fists get).

**Strikes on the move.** Vanilla's punch, kick and slam move the whole body, the legs planted, so
played while it walks they stop it in its tracks (or slide it). The two strikes move only the upper
body, and the entity type (`entities/eidolon.json`) gives them weight 10 on exactly those elements and
0.001 elsewhere (the game's own player does it so: an element every playing animation weights 0 is
not drawn right), so the game plays them over `stand-walk` or `stand-run`, whose legs, hips and skirts
go on, or over the idle when it stands. Both start and end on the same guard, so the blend in and out
is short. The game weights each element by name, not by its parents, so the list names every element
they move.

**Climbing.** The game lifts a creature one block (its step height); a step of two is climbed, the entity
moved by script as `climb` plays (the mod's README, "Steps and drops"): at the foot node until frame 12,
straight up two blocks (32 voxels) by frame 36, then forward a block onto the top node by frame 52, each at
an even pace, up then forward so its box is always where the pathfinder found it free (`climb_moved`). The
ledge's face is a block in front of the foot node (a box 1.7 wide on a block corner just clears it) and its
top two blocks up: at the start, model x −8 and y 32. The poses are made against that motion
(`climb_local`): what holds the ledge or stands on the step is put at its place in the world less how far
the entity has moved by that key, with keys every 2 to 4 frames, so the hands stay on the ledge as the body
rises past them and the right foot on the step as the entity moves forward over it. The body is 60 voxels
tall and its hips 18 up, so a step of two is chest high: a mantle, not a step. The arms come up out to the
sides first, since swung straight forward they would sweep through the ledge's face (the upper arm alone is
20 voxels, the face 13.5 in front of the shoulder); the knee comes over only once the hips are above the
top. Held objects are not posed: a carried block or trunk rides on the chest while it climbs.

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
whose 10-frame wake-up starts from a standing pose, not a hung one; `dock` is `activate`'s keys in
reverse (its steps, its landing and its lurch off the spine), without the stirring, so it backs onto the
spine the way it came off. `activate` is played in the gantry,
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
| `dock`: the lower sole at every key up to the clamps (46); every frame up to 46 | within 0.01; within 0.3 |
| `hung`'s lowest toe, and `dock`'s last frame | 3.0 above the floor |
| `slump`'s held frame | lowest leg part on the ground; the hood's top at 40.7 (from 60) |
| Hands to their grips, every frame they hold: block (carry, lift, setdown), trunk on the shoulder, trunk on the ground | ≤ 0.34 |
| Hands to a thick trunk's near side: carry-idle and carry-walk every frame; pickup from the grab (26) on and setdown up to the release (32) | ≤ 0.16; ≤ 0.37 |
| A block or trunk on the ground, while the body moves over it (thick: pickup 0..26, setdown 28..49) | moves ≤ 0.39 (thick ≤ 0.25) |
| A thick trunk's underside at the grab (pickup frame 26) and release (setdown frame 32) | 0.0 |
| A thick trunk against the body, every frame of its four animations, against its square collision section (`TrunkBoxes`, stricter than the rounder model; 27 points sampled on each element) | nothing inside it, the arms included down to the wrists (the hands hold it); the head and hood at least 1.6 away (closest at the grab's squat) |
| `fell` hands at the cut | within 0.3 of the targets; wind-up with the right hand at 62 (above the 60-voxel head) |
| A thin trunk on the shoulder against the head and hood | no corner of the head inside it |
| `climb`, against the entity's move (`climb_moved`): the hands to their place on the ledge, frames 12..28 | ≤ 0.41 |
| `climb`: the body inside the ledge or under the floor below it, every frame (27 points sampled on each element); the hands inside the ledge | ≤ 0.09; ≤ 0.15 |
| `climb`: the soles, frames 0..12 and 52..59; the right foot's place in the world, frames 36..59 | within 0.07 of the ground; moves ≤ 0.05 |

## Build stages

The gantry shows the body cumulatively, stage by stage, by element name; `eidolon-stages.json` lists each
stage's elements, written by the generator (`stage_map()`), so every element is in exactly one stage. The
spine is in none: it is the gantry's, hanging in it by the ring over its top peg before anything is fitted.
How the stages are fitted and stripped is gameplay (the mod's README, "Eidolon").

1. **Torso** (`torso`): the crafted `seraphhorizons:eidolontorso` (the Jonas pump head, tanks 1 and 2, 4
   metal parts and 3 stainless gears, its `recipe`), placed onto the spine, clamped by the spine's steel
   clamps. It is the trunk's frame: the **lattice chest and hip blocks** (`chest-inside`, `hip-inside`),
   with the faceless root (`origin`) and the three anchors. The hip block is the torso's, not the bones',
   because the chest hangs from it in the hierarchy and the bones hang from both: with the hip in the bones
   the viewer's rig could not order its parts (below).
2. **The five layers**, in order, **each the elements of its texture code** (`bones`, `joints`, `plates`,
   `sash`, `skirt`; the table above), after the recodes. An element whose faces were in several codes would
   go to the code over most of its drawn faces' area, a tie to the earlier layer (`main_code`; today every
   element has one code). Two other codes go with the bones: the head's **lattice** block (`head-inside`,
   the head's frame) and the vessel (`brain`, **glow**), the Jonas frames and the relic the bones take.
3. **Mind** (`mind`): the heart (glow) and the eye's light (`Eye-out`, **fire-red**) and what hangs from
   them (the heart's two bars, steel but the heart's), as before: what lights when it wakes. A temporal
   gear, its first charge.

**One piece after every stage.** The rule (checked in the generator, the tests and the gantry, there with
the spine from the start, the torso clamped to it) is that **after every stage, the elements shown so far
are one connected piece of the hierarchy**, its links taken either way: each layer fits onto what is
already there, the bones on the torso's blocks, the joints on the bones, the plates on the joints and
bones, the sash on the plates (the left chest sash hangs from the left chest plate), the skirt on the hip
block. The recodes are what keeps it so; without them the arm bones would hang off joints not yet fitted.
The gantry draws the body baked in the `hung` pose, each element a static box at its posed place, so no
element shown depends on a hidden one for where it is drawn.

**Counts are scaled to the model** (`layer_bill()`; each layer's `measure` in the stages file). One
measure for every layer: the **face area** its own elements show, half each box's surface (xy + yz + zx, in
model voxels squared), so a sheet counts the one side it covers and not its edges twice, and a layer
costs about what it looks like. Volume was the alternative, but it weighs the thick wooden bones and
blocks against thin sheets of plate and leather by a thickness the item does not have. Only a layer's
own material counts: its elements wearing its code (not the bones' lattice frame or vessel, Jonas items
of their own). **Metal sets the scale**: today's bill, 13 plates, 8 rods and 36 nails and strips, is 43
ingots (a plate 2, a rod 1, nails and strips 4 to the ingot), and it buys the plates' and joints' area,
4973.1, so **an ingot covers 115.65**. A plate, or a chain (both two ingots on the anvil), covers two
ingots' area, **231.31, about a block's face**, and a leather or a cloth is counted as a sheet the same. A
support beam is the game's own beam, 16 × 4 × 4, **144**. Counts round to the nearest whole item, nails
and strips to whole ingots (fours). The bones take at least 30 beams, three times the model's wood
(`BONES_FLOOR`), and the sash and the skirt at least 4 (`SMALL_LAYER_FLOOR`): the owner's calls.

| Layer | Its area | Per item | Exact | Count (default build) |
|---|---|---|---|---|
| Bones | 1439.4 (the wood) | 144 a beam | 10.0 | **30** `game:supportbeam-{wood}` (oak), 10 raised to the floor of 30 |
| Joints: bars and clamps (`bar-*`) | 411.0 | 115.65 a rod | 3.55 | **4** `game:rod-{metal}` (steel) |
| Joints: the rest | 531.3 | 28.9 a nail and strip | 18.4 | **20** `game:metalnailsandstrips-{metal}` |
| Plates | 4030.8 | 231.31 a plate | 17.4 | **17** `game:metalplate-{metal}` |
| Sash and fauld | 527.8 | 231.31 a sheet | 2.3 | **2** `game:leather-normal-{colour}` or `game:cloth-{colour}` (plain leather) |
| Chain skirt | 434.0 | 231.31 a chain | 1.9 | **2** `game:metalchain-{metal}` |

The metal still comes to 43 ingots (34 + 4 + 5) but moves to the plates, which are most of what the body
shows: 17 plates (13 before), 4 rods (8), 20 nails and strips (36).

| Stage | Ingredients (`{token}`: the builder's choice) | Elements | What they are in the model |
|---|---|---|---|
| 1 Gantry | Wood: the gantry's own model | None | The frame it hangs in. |
| 2 Torso (6) | `seraphhorizons:eidolontorso`, crafted from `game:jonasparts-pumphead`, `game:jonasparts-tank01`, `game:jonasparts-tank02`, 4 `game:metal-parts`, 3 `seraphhorizons:gear-stainless` | `origin`, `hip-inside`, `chest-inside`, the three anchors | The lattice chest and hip blocks, placed onto the spine. |
| 3 Bones (28) | 30 `game:supportbeam-{wood}`; `game:jonasframes-gearbox01`, `game:jonasframes-gearbox02`, `game:jonasframes-oscillator01`, `game:jonasparts-cylinder02`, `game:jonasparts-connector01`, `game:rustypart-eidolon2tr` (the Eidolon elucidatory vessel, its brain: one per player who hurt the archives' eidolon) | the `bones` code, `head-inside`, `brain` | The wooden skeleton, face and head plates, the head's lattice block and the vessel in it. |
| 4 Joints, bars and clamps (57) | 4 `game:rod-{metal}`, 20 `game:metalnailsandstrips-{metal}`; 2 `game:jonasframes-joint01` (the knees), 2 `game:jonasframes-spring01`, `game:jonasframes-gears01`, `game:jonasframes-gears02`, `game:jonasparts-cylinder01`, `game:jonasparts-valve01`, 9 `seraphhorizons:gear-stainless`, 12 `game:metal-parts` | the `joints` code | The moving mechanism: neck, knees, elbows, ankles, hands, the eye's bracket, and the bars and clamps. |
| 5 Plates (87) | 17 `game:metalplate-{metal}` | the `plates` code | The armour: hood, chest, shoulders, arms, legs, feet, tassets, bracers. |
| 6 Sash and fauld (5) | 4 `game:leather-normal-{colour}` or `game:cloth-{colour}` | the `sash` code | The chest sashes, the fauld and its waist tassets. |
| 7 Chain skirt (5) | 4 `game:metalchain-{metal}` | the `skirt` code | The chain skirt, front and back. |
| 8 Mind (4) | `game:gear-temporal` (its first charge, a quarter in-game year) | `heart`, `Eye-out` and the heart's bars | The heart in the chest and the eye's red light: what lights when it wakes. |

The Jonas items are spread over the torso, bones and joints (16 in all, the 14 kinds once, the knee joint
and spring twice), with 16 `game:metal-parts` and 12 stainless gears (`seraphhorizons:gear-stainless`; no
large gears), the vessel and a temporal gear. The game's `eidolongearbox` (disabled in vanilla) is not
used. The gantry's `BodyBill` (`../EidolonGantry/Core/BodyParts.cs`) is the default build's, held to the
stages file by a test.

**In the model viewer** the eidolon's page shows the stages too: a **Build state** select (torso, bones,
joints, plates, sash, skirt, mind: fully built, where it opens) and a checkbox per stage, from
`eidolon-rig.json`, a part per stage (`rig_file()`). Its parts have no drivers, so each part's matrix is
the identity and the shape's animations move the elements within them as they do the whole body: any set
of stages plays any animation, the elements shown where the game poses them however many of the joints
above or below them are hidden (`docs/recipe-browser/models.md`, "Keyframe animations"). An element is
in the first part matching a name in its chain, so the rig lists each stage before those its elements
hang from (`rig_order()`: the sash, plates, skirt and mind, then the joints, the bones and the torso last),
and the generator checks every element lands in its own stage; a cycle (a stage both above and below
another) cannot be ordered and stops the generator, which is why the hip block is the torso's.

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
ground at the grab and release); `dock` starts at rest, has a foot on the ground every frame until the clamps and ends as `hung`;
`climb` starts and ends at rest, its hands two blocks up past the ledge's face at the grab and the climb's pose
checks above within their bounds, and its frames and the entity's move are `Core/EidolonClimb.cs`'s; the texture
codes are the layers (the default build's paths, every layer worn, the recodes, the spine recoded alike, an
element of several codes going by the most area); and the stage map covers every element exactly once, the
body one piece after every stage (a stage that would leave a piece floating is caught), each layer the
elements of its code, the torso and mind as above, the ingredients the default build's totals (43 ingots of
steel, 12 stainless gears, 16 metal parts, the Jonas items) and each count the model's area over an item's,
as the stages file has it; and the viewer's
rig a part per stage with no drivers, every element in its own stage's part by the viewer's rule (a
part listed too late is caught).

## Fittings

The body is customizable in five layers of material (the fittings brief; the rules are
`Core/EidolonFittings.cs`, the figures `Core/EidolonStats.cs`, the settings in `Core/EidolonConfig.cs`,
tests in `tests/Eidolon/EidolonFittingsTests.cs`).

| Layer | Texture code | Materials | Items | Texture |
|---|---|---|---|---|
| Bones | `bones` | any wood but `rotten`, `veryrotten` | `game:supportbeam-{wood}` (a block) | `game:block/wood/debarked/{wood}` |
| Joints | `joints` | `iron`, `steel`, `stainlesssteel` | `game:rod-{metal}`, `game:metalnailsandstrips-{metal}` | `game:block/metal/plate/{metal}` |
| Plates | `plates` | those and `brass` | `game:metalplate-{metal}` | `game:block/metal/plate/{metal}` |
| Sash | `sash` | `leather-{colour}` (the game's 11), `cloth-{colour}` (its 12 but mordant) | `game:leather-normal-{colour}`, `game:cloth-{colour}` | `game:block/leather/{colour}`, `game:block/cloth/basic/{colour}` (plain cloth `normal`) |
| Skirt | `skirt` | `iron`, `steel`, `stainlesssteel` | `game:metalchain-{metal}` | `game:block/metal/armor-generic/chain-{metal}`; stainless the pack's `seraphhorizons:block/metal/armor-generic/chain-stainlesssteel` |

A material is stored by its short code (the table's), the metals by the game's metal codes. The Jonas
parts go with the torso (a grid recipe: pumphead, tank01, tank02, 4 metal parts, 3 stainless gears), the
bones (gearbox01, gearbox02, oscillator01, cylinder02, connector01 and the elucidatory vessel) and the
joints (2 joint01, 2 spring01, gears01, gears02, cylinder01, valve01, 9 stainless gears, 12 metal parts);
a layer's bill (`EidolonFittings.Bill`) is its material then its Jonas parts, and stripping gives it all
back. **The material counts** (`FittingCounts`) are the model's ("Build stages", above, its table; the
stages file's `measure`), kept in that one place and held to the stages file by
`tests/EidolonGantry/EidolonBodyTests.cs`.

`EidolonBuild` is an eidolon's record: each layer's material or none, always a prefix of the build order
(`With` fits only the next layer, `Stripped` takes off only the outermost, `CanStrip` only at full
health). Stored under the attribute `eidolonFittings`, a tree of strings keyed by texture code; no tree at
all reads as `EidolonBuild.OldBuild` (oak, steel, steel, `leather-plain`, steel), a broken one as the
layers before the first bad one. `Textures()` gives the per-entity texture override by texture code.

**The armour maths** (`EidolonArmour`) is the game's for a player in a full suit of one armour
(`ModSystemWearableStats.handleDamaged`, `VSSurvivalMod.dll` 1.22.7; the figures from
`itemtypes/wearable/seraph/armor.json`): only blunt, piercing and slashing attacks, never internal or
suicide damage; for each tier up to the attack's, the flat reduction loses 0.1 (0.2 past the armour's
tier) and the relative protection is multiplied by 1 − 0.03 (1 − 0.15 past it), the past-tier losses
halved for plate (`highDamageTierResistant`); then damage = max(0, damage − flat) × (1 − max(0, relative)).
The game picks a random armour slot, which for a full suit of one metal changes nothing, and wears the
piece; the eidolon's plates do not wear.

### On the woken eidolon

`EntityLaborEidolon.Build` reads and writes the record in its watched attributes (`ReadBuild` and
`WriteBuild` do the same on any tree, e.g. for the gantry's hung body); writing it any way applies at once.

- **Look.** A layer that is off (stripped in a gantry's dock, below) is not drawn: at tessellation the
  dock behaviour leaves its elements out of the shape (`willDeleteElements`, as the game hides a worn
  item's elements; the elements from `config/eidolon-stages.json`, `Core/EidolonLayerElements.cs`). The
  game deletes an element with its children, and no element hangs from one of a later stage (a test walks
  the shape), so a layer takes with it only those after it, already off, and with the joints the mind's
  eye light, which hangs on the eye's bracket. `EidolonShapeRenderer` is the texture source the shape is tessellated with (the game's
  `EntityShapeRenderer` tessellates with `TexSource = this`, its virtual indexer). Before each
  tessellation, on the main thread, it looks up each worn layer's texture (`EidolonBuild.Textures`) in the
  entity texture atlas, inserting it the first time (`GetOrInsertTexture` with a `CompositeTexture`), and
  its indexer returns those for the five layer codes and the shape's own for the rest. A change re-tessellates:
  the entity's modified-listener on `eidolonFittings` marks the shape modified on the client, and the
  renderer also compares the record once a second (a `RemoveAttribute` fires no listener).
- **Figures** (server; `ApplyFittings`, run from that listener, at `Initialize` and in every `Check`,
  doing nothing when the record has not changed): `EidolonStats.Of(build, settings)` as `FittedStats`.
  The health behaviour's `BaseMaxHealth` becomes `MaxHealth` and the health keeps its share of the most
  (`EidolonStats.KeepShare`), so an old eidolon at 300 stays 300. The navigator walks and runs at
  `FittedStats`' speeds (the traverser's speed, for paths and steering alike), the defence task strikes for
  its `DefenceDamage` and `SlamDamage` (`AiTaskEidolonDefend.StrikeDamage`), and the armour is a
  delegate on the health behaviour's `onDamaged`, as the game's worn armour is on a player: before the
  health is taken, `EidolonArmour.Applies` on the hit's type and source, then `Reduce` at its
  `DamageTier`. A blow the armour takes whole still provokes the defence (the task AI hears every hit).
  At 0 HP it slumps as before; armour changes only how fast it gets there.
- **Info.** The server writes the figures it worked out under `seraphhorizons:eidolonStats` (blows,
  slams, the move speed's multiplier, the armour), which the info text shows under a "Fittings:" line
  naming each layer by the game's material and colour names (`Core/EidolonFittingText.cs`).
- **`/sh eidolon spawn [player] [layer=material ...]`** spawns the old build with the named layers
  changed, `fit layer=material ...` changes the nearest one's (`Core/EidolonBuildSpec.cs`: `bones=pine
  plates=brass sash=cloth-red`, `stainless` for `stainlesssteel`, `all=` the three metal layers), and
  `info` shows the build and figures.

Tests: `tests/Eidolon/EidolonWornFittingsTests.cs`, and the Atlas class `EidolonFittedScenarios` (a
brass, an iron and a stainless build's health, walking and running speed as the traverser has them and
blows; a wolf struck for the brass build's damage; each plate metal against blows of four tiers, as the
maths says, internal harm and fire passing; no record is the old all-steel eidolon at 300; a changed
record, through the property or straight into the attribute, changes the figures at once and keeps the
share of health, and taking it away goes back to the old build).

## Docking

A woken eidolon goes back onto a gantry's spine to have its layers stripped and refitted (the gantry's
side: `../EidolonGantry/README.md`, "Gameplay"; the player's: the mod's README, "Docking, stripping and
refitting"). Rules in `Core/EidolonDocking.cs`, the entity's side in `Game/EntityBehaviorEidolonDock.cs`
and `Game/DockOrder.cs`.

- **The order.** The command tool's `dock` mode (wheel place 90, `game:textures/icons/worldmap/home.svg`)
  marks a block, optionally (`EidolonCommandMode.MarkOptional`): a gantry's cell names that gantry, the air
  the nearest whose spine is free within `EidolonDocking.SearchBlocks` (24), found among the loaded chunks'
  block entities; one another eidolon is on its way to (`DockOrder.Claimed`) is passed over. The `dock`
  order (`x`, `y`, `z` the gantry's controller; `then` the order it was doing, unless that was a `goto` or
  a dock) walks to the rig's `body` point by the wide pathfinder; within `ArriveBlocks` (0.5) it is set on
  the point facing out of the open front (`BEEidolonGantry.OutwardYaw`) and `EntityBehaviorEidolonDock.Begin`
  starts `dock`. With the spine taken it waits before the gantry; with the gantry gone it says so.
- **The clamp.** `dock` is 60 frames, its clamps closing on frame 46 (`EVENTS["dock"]["clamp"]` in
  `tools/make_shape.py`; `EidolonDocking.ClampFrame` and `DockFrames`, held to them by
  `tools/tests/test_eidolon_model.py`): at 46/30 s the gantry
  takes hold (`BEBehaviorEidolonBody.TakeHold`), the phase goes from `docking` to `docked`, and at 2 s
  `hung` starts (one frame, held) as `dock` stops, the same pose. A spine taken by then (or the gantry
  gone) and it stands down and goes back to its order. Loaded mid-dock, it goes straight to the clamp.
- **Held.** The entity's watched tree `seraphhorizons:dock` (the gantry's position, the point, the yaw,
  the phase, `then`) is the entity's half; the gantry keeps the entity id. While docking or docked
  `CanWork` is false (no task starts, the order task ends), its upkeeps' `running` is false (the charge
  does not drain), the pose is not updated (it hangs rather than slumps; at 0 HP it slumps when woken),
  it does not push other eidolons off (they push themselves off it), and every server tick it is set back
  on the point with no motion, its yaw and body yaw outward, its controls stopped: nothing pushes it off.
  `hung` is started again after a load. The two halves check each other every 2 s: docked with its
  gantry's chunk in and the gantry gone or holding another, it lets go (`Release`: `activate`, no order);
  the gantry lets an eidolon go that is loaded but not docked on it, or missing from a loaded chunk three
  checks running.
- **Stripping** (Ctrl+right-click, any hand, by its owner and their company; the machines' take-back key). `EidolonDocking.CanStrip`: only
  docked, only at full health (`EidolonBuild.CanStrip`), only with a layer on; outermost first
  (`EidolonBuild.NextToStrip`). Pieces of a layer part fitted come back before any layer. The layer's
  whole bill goes to the player (`TryGiveItemStack`, the rest dropped at their feet), the gantry's stage is
  emptied (`BodyParts.Strip`) and the entity's record loses it: the listener on `eidolonFittings` refreshes
  its figures (its most health keeps its share, so a whole one stays whole) and its look.
- **Refitting.** Docked, the gantry's `BodyParts` is the eidolon's build (`BodyParts.Docked`: torso, the
  layers it wears whole, mind), so its own fitting code (first piece fixes the material, out of order and
  wrong material refused) fits a stripped layer; a right-click on the eidolon with an item the next layer
  takes (or of its family, to be refused with why) goes to the gantry's `OnGantryInteract`, as does one on
  the gantry. A layer completed is written to the eidolon at once. A repair item on a damaged eidolon is
  the repair's, not the fitting's.
- **Waking.** The crank (`OnCrank`) with all five layers on (`EidolonDocking.CanWake`) clears the dock,
  plays `activate` and gives a `goto` 3 blocks out of the front whose `then` is the order it was given
  while docked (orders are taken then, and wait), else the one it docked from;
  the gantry's spine is bare again (loose pieces given back at the front).

Tests: `tests/Eidolon/EidolonDockingTests.cs` (timing, the verdicts, the docked body, refitting in another
material, loose pieces, saving, which elements a layer that is off leaves out and that none takes an
earlier layer's) and `tests/PackTests/EidolonDockScenarios.cs` (Atlas, the mod's README lists them).

## Open questions

- **Size.** At size 1 the eidolon is 3.75 blocks tall (vanilla's boss size), a giant beside a
  1.85-block player, and an axe or block drawn at its true size in its hand looks small. A smaller
  entity (say 0.5, about player height) shrinks its carried things with it unless the renderer scales
  them back, and the contact poses then need remaking for that size (`SIZE`, which every block and
  trunk measure is divided by).
- **The axe's orientation in the hand** comes from the item's `tpHandTransform` in the `RightHand`
  frame, matched to the seraph's at rest; whether the head points at the trunk through the swing can
  only be seen in the game.
- **The layers' textures** have been seen only in the model viewer, not in the game's lighting.
- **Docking's hand-off.** The eidolon is set on the `body` point when it arrives within half a block of
  it, so it may slide that far before `dock` plays; and it docks wherever the winch stands (nothing
  drives the winch's depth yet), as it wakes.
