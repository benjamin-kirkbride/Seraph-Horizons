# Arrastra

Part of the Seraph Horizons mod (`../README.md`): the ore line's **small grinder** (behaviour #732, model #733,
epic #684). A mule-sized arrastra: a stone-paved pit where four heavy stones are dragged round and round over the
ore by a turning post's arms. Batch, worked by hand: the player tips a charge of crushed rock straight into the pit
and pours a bucket of water a rock after it, the stones grind it to a paste, and the player draws it out. It lifts poor ore's × 0.4
and loses no metal. One machine with one level, built in stages on its frame (the crucible furnace's pattern); its
continuous successor is the ball mill (#915, its own machine).

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or renderer
yet; the model and its rig are what #732 will be built against. Paths here are from this folder unless they start
with `assets/` or `tests/`, which are the mod's (`mods-src/seraphhorizons/`), or `Machines/`, this folder's
neighbour. The generic half of the generator is `Machines/tools/machinegen/`, unchanged by this machine:
no new driver, every motion a `rotate` on the axle's angle or a ride.

It is salvaged from `733-grinder-model-v2` (the grinder, which held this arrastra as its level 1 and a ball mill as
its level 2): its frame and level 1, copied into a new generator and changed to #733's spec. What changed is listed
under [From the grinder](#from-the-grinder).

## The machine

**Footprint: 8 cells.** The box is 3 × 2 × 2 (x, y, z): three wide, two high, two deep. In the native frame (x
west to east, y up, z north to south) the ground layer is all six cells (the pit, two blocks across outside its
curb, centred on the middle column), and the upper layer is the middle column only: the head frame's front over the
pit's front and the drive's over its back. The four upper cells over the pit's corners hold nothing of the machine,
so they are not the machine's: a player may build there, and arrastras stand side by side along x with nothing
between them; over the front two the pit lies open to tip the charge into. The controller `[0,0,0]` is the middle
of the front (north) row at ground level,
where the draw gate is: the player works the machine from its front, and the axle comes in at its back. The build frame (below) is measured from the box's north-west bottom corner, so the shipped files are it
moved one block west.

**Batch, by hand** (#711, rule 2): no chute, no load anchor and no output anchor. Nothing automated may feed it (the
owner's rule for batch hand machines), so nothing in the model reads as a mouth a chute could meet: no hopper, no
spout, no load anchor (the validator fails on any). **Loaded and watered by hand**: the player tips the charge
straight into the open pit over its low curb, and pours the bucket of water in after it (the owner's ruling,
[below](#water-by-the-bucket)); neither has an anchor. Only the draw is a hand action with an anchor.

| Anchor | Cell, face (shipped) | What is there |
|---|---|---|
| `drawCell`, `drawFace` | `[0,0,0]`, north | **Draw**, a hand action: the gate in the curb at the pit's front, at the controller, between the head frame's front posts. The player draws the ground charge out over its threshold. |
| `powerCell`, `powerFace` | `[0,1,1]`, south | The vanilla axle comes in along z at the upper back face's centre (x 24, y 24 voxels in the build frame) onto the power shaft. High, because the arrastra is driven from overhead. |

**The frame** (`requires` null) is what is placed, and turns from the start:

- the **head frame** over the pit: four oak posts set in the curb at the front and back, two stringers along z on
  them, knee braces, and two transoms between the stringers (T1 over the pit's centre, T2 over the power shaft);
- the **power shaft**: the vanilla axle's cross at the back face, then an octagonal iron shaft forward to its end
  (z 20.6), carried in two iron drop hangers under T2, collars either side of the first; it turns with the axle in
  every state, the bare frame's included.

**The stages**, each one `requires` value, fitted in this order (the rig's `build`; [below](#the-builds-stages-provisional)):

- **pit**: the pit at ground level: a granite centre stone a little proud, cobbled paving laid on the ground in rows,
  a course of edging stones, and a drystone curb two blocks across outside, sixteen stones, less the one at the
  front, left out for the **draw gate** (a granite
  threshold level with the edging, two oak jambs across the curb at the opening's sides, and between them a plank
  gate standing in their grooves to above the curb, an iron grip on its outer face);
- **post**: the iron footstep on the centre stone, the top bearing hung under T1, the oak post between them on its
  iron gudgeons, banded either side of the arms and capped, with its 18-tooth bevel wheel and hub under the power
  shaft's end; and the 6-tooth bevel pinion keyed on that end. The post turns at a third of the axle;
- **arms**: four oak sweep arms through the post, an iron staple under each;
- **stones**: four basalt drag stones on the paving, each on a bridle of two chains from its arm's staple to the
  eyes on its back, one fore and one aft. With them the machine is complete.

**No hopper.** The charge goes straight into the pit by hand, over the curb: a low rim (6 voxels), with nothing of
the machine over the pit outside the middle column, so the pit lies open from above either side of the head frame
(the sweep's arms pass there). Nothing in the model is a mouth a chute could meet.

### Water by the bucket

The arrastra is watered by hand, not by pipe (the owner's ruling). The period's water went in with the charge: it
was damped to a paste and a little more added as needed [Louis 1902, p. 292], barrels a charge, not a flow. #732's
own figure is 10 L a rock, and a charge is 2 rock: a vanilla bucket (10 L) a rock. So the player pours a bucket of
water a rock straight into the pit after the charge, over the curb, as the charge went in. Neither the charge nor
the water has an anchor: anchors are where a service or a chute meets a machine, and nothing automated feeds the
arrastra. Nothing of a water inlet is drawn: no pipe, no cut-down curb stone, no water anchor.

**The contents** (`charge`, `paste`), one drawn at a time, are the batch's, not stages: the charge as tipped in
(crushed rock, damped from the bucket) and the same ground to a paste. Each is a level annulus bedded on the paving,
from clear of the footstep to the curb's inner face; the drag stones stand out of it. Their textures are stand-ins,
the game's granite gravel and sand: a renderer may set either code to the charge's own ore.

### Why this layout

1. **A pit at ground level, so the drive is overhead.** An arrastra was a pit dug into the ground and paved.
   Water-driven arrastras took their power from the wheel's shaft through gearing above the sweep, the post held
   at the top in a frame over the pit; here the vanilla axle stands in for that shaft. It comes in high at the
   back, and a bevel pinion on its end turns a bevel wheel on the post under it.
2. **The player at the front, the drive at the back.** The draw gate is at the controller; the charge and the
   water go in over the curb by the open front corners either side of the head frame. The axle comes in high at the
   back. Nothing is on the east or west sides, so arrastras stand in a row along x, and each still has its front
   corners to tip from.
3. **Two blocks across, the smallest that reads as an arrastra with four stones**, the head frame and the drive
   over the pit's middle rather than in rows of their own.
4. **Tipped straight into the pit, no hopper** (the owner's rule): nothing automated feeds a batch hand machine, so
   the model has no mouth flush with its top, no spout and no load anchor, nothing a chute could meet. The pit is
   the mouth: its curb is a low rim (6 voxels, checked at most half a block), and nothing fixed stands over the pit
   outside the middle column (checked). Period arrastras were charged the same way, by the shovel and the barrow
   over the curb.
5. **The water by the bucket, with the charge** (the owner's ruling): the charge is damped, then ground to a paste
   [Louis 1902, p. 292], a bucket a rock poured into the pit. Nothing is drawn for it, and there is no water anchor.

### Powered substitute for the mule

No mule, water wheel or engine: power comes from the axle (the owner's rule for every machine). The arrastra was
mule-driven, a sweep on its post; the water-driven ones had the drive overhead, as here. The 3:1 bevel pair
stands in for the wheel's own gearing. Gearing ratios are the model's (the turns the post makes per axle turn);
gameplay's rate is its own (#732: a charge of 2 rock in 7.5 hours at the reference speed 0.6). At that speed the
axle turns 0.48 a second and the post 9.6 a minute, against the period's 12 for a 12 ft power arrastra
[Louis 1902, p. 294]; the viewer's Play starts at it.

### Either way round

The axle may turn either way, and nothing in the arrastra depends on which (#733: the sweep is symmetric, no
rectifier):

- **The gearing** has no catch, ratchet or one-way part: the bevel pair has symmetric teeth, and every motion is
  a plain `rotate` that runs backwards with θ.
- **The drag stones** hang on bridles of two chains, one fore and one aft of each stone, so the same bridle drags
  it whichever way the arms go; a period stone on one leading chain would trail the wrong way in reverse.
- **The contents** are drawn still and level, which reads right whichever way the stones go.

### Every turning part

θ is the axle's angle. A part that rides another turns with it; nothing else moves. Turns are per axle turn; the
whole machine repeats every three (the viewer's θ slider spans three turns).

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Power shaft: the axle's cross, the iron shaft, its collars (`shaft`) [null] | The vanilla axle | The bevel pinion | `rotate` z, 1 |
| Bevel pinion, 6 teeth, and hub on the power shaft's end (`pinion`) [post] | The power shaft (keyed) | The post's bevel wheel | rides `shaft` |
| Post: gudgeons, oak post, bands and cap, bevel wheel (18 teeth) and hub (`post`) [post] | The pinion | The arms | `rotate` y, +1/3 |
| Sweep arms and their staples (`arms`) [arms] | The post (through it) | The chains | rides `post` |
| Drag stones, their eyes and chain bridles (`stones`) [stones] | The arms, by the chains | Grind on the paving | rides `post` |
| Footstep and top bearing (`step`) [post] | Fixed | — | none |
| Head frame and hangers (`frame`) [null]; the pit and the draw gate (`pit`) [pit] | Fixed | — | none |
| The charge (`charge`) [charge] or the paste (`paste`) [paste] | Lies in the pit, drawn still and level | — | none |

Toothed wheels and what they mesh (nothing toothed meshes nothing): the pinion and the post's bevel wheel.

### The build's stages (provisional)

The frame (the head frame and the power shaft) is placed as the machine; then each stage goes on, one `requires`
value, in this order (the rig's `build.stages`). The order is the mechanism's: the post stands on the pit's centre
stone and in the frame's top bearing, the arms go through the post, and the stones hang from the arms, which
completes the machine. There is no hopper stage: the charge is tipped straight into the pit. The frame's recipe
and each stage's items and materials are open (#733, "Build stages").

| # | `requires` | What it fits |
|---|---|---|
| 1 | `pit` | The centre stone, the paving, the edging, the curb, and the draw gate |
| 2 | `post` | The footstep, the top bearing, the post with its bevel wheel, and the bevel pinion on the power shaft |
| 3 | `arms` | The four sweep arms through the post, with their staples |
| 4 | `stones` | The four drag stones and their chain bridles: the machine complete |

**In the viewer** (`site/models.json`): two selects. **Build** steps from "The frame (placed)" through each stage
to "4: The drag stones: complete" (the default). **In the pit** shows it empty, charged (the default) or ground to a paste; it waits on the whole build. θ has
a slider and Play.

### Collision

The rig's cells are the whole machine built (the frame and every stage; not the contents), as the
crucible furnace's are: each cell up to three boxes, a turning part's swept over the whole cycle, so the sweep's arms
and stones fill the pit's cells up to the arms' height and nobody stands in their way. Every cell holds part of the
machine (none hollow). No lids: whether the top is walked on is open.

## Model

Everything in the model was made for this mod: no other mod's model is used. Textures are the game's, referenced:
debarked oak (the timbers, the gate's jambs) and oak planks (the gate), the generic planks of the vanilla
axle's cross, iron plate (castings and fittings), plain steel sheet (the shafts and the pinion), iron chain, and
granite as cobblestone (the
paving and its edging), dry stone (the curb) and rock (the centre stone, the gate's threshold); the drag stones are
basalt, a hard dark rock as they were chosen, which also reads apart from the paving. The contents are the game's
granite gravel and sand. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/arrastra.json` | The frame, every stage and both contents (213 elements), each under its `requires`. |
| `assets/seraphhorizons/shapes/block/arrastra_frame.json` | The static frame only (18 elements): what a block would draw. |
| `assets/seraphhorizons/config/arrastra-rig.json` | Footprint, anchors, the build's stages, the part rig (10 parts). |
| `tests/Arrastra/rig-reference.json` | Every part's matrix at a grid of axle angles, from the reference maths. |

Elements per part: frame 18, shaft 14, pit 49, step 4, post 54, pinion 14, arms 8, stones 20, the charge 16, the
paste 16.

### Dimensions (voxels, build frame)

- **Pit**: centre (24, 16). Paving y 0..1.5 in nine rows; edging radius 12..13.5, top 1.8; centre stone radius 3.2,
  top 2.0; curb radius 13.5..16, y 0..6 (two blocks across outside), sixteen stones less the gate's.
- **Draw gate**, in the front stone's place: x 20.82..27.18 (the stone's width), z 0..2.5 (across the curb);
  threshold y 0..1.8; jambs 0.9 thick, to y 7.2; the gate z 0.8..1.7, y 1.8..6.4, its ends 0.3 into the jambs; the grip
  x 22.8..25.2, y 4.6..5.6, 0.5 proud.
- **Head frame**: posts 3 × 2.8 at x 17..20 and 28..31, z 0..2.8 and 29.2..32, up to y 27; stringers y 27..31;
  T1 z 14..18, T2 z 24.8..30.4.
- **Power shaft** along z at (x 24, y 24), from z 20.6 to the back face; the axle's cross from z 30.6; hangers
  at z 25.4..27.4 and 28.4..30.
- **Post and sweep**: post radius 2 from y 3.6 to 24.4 on an iron step on the centre stone, its top gudgeon in a
  bearing y 24.8..26.4 under T1; bevel wheel radius 7.5 (18 teeth, module 0.83) under the pinion radius 2.5 (6
  teeth), apex (24, 24, 16); arms y 8.5..11.5 to radius 12; drag stones radius 4.6..11.5, 4 wide, y 1.5..4.5, on
  chains 1 voxel thick to staples under the arms.
- **Contents**: an annulus radius 4.4..13.45, bedded from y 1.2; the charge's top 3.0, the paste's 2.7.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Arrastra/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Arrastra/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Arrastra/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

A full run takes about 15 seconds. Output is deterministic (two runs into two folders `diff -r` clean).
`tools/validate_arrastra.py` holds the checks; every run checks its output and exits non-zero if one fails. The
model holds the whole machine built with each content in turn (the two are never drawn together); a state with
fewer stages draws a subset, so what holds for the whole holds for it.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part. **Chunky:** no element is a rod thinner than 0.9 voxels both ways (a plate may be thin).
- **The build:** the frame's parts need nothing, every other part its stage's `requires` (the contents
  their own); no `requires` in two stages; the rig's `build` lists the stages in order; at rest the frame, then each
  stage in turn, joined to the ground through what is already there (nothing floats), each content once the machine
  is built.
- **Textures** by role (the list in "Model" above).
- **Anchors:** the anchor faces on the footprint's outside, looking out, all different, and no anchor in the power
  cell; the draw gate at the controller, on the front; the axle's cross at the power face's centre; no load, feed,
  chute, pour or water anchor (`FORBIDDEN_ANCHOR`, nor a `LOAD_CELL` in the generator) and no hopper, spout or chute
  element; the gate in the draw cell, square to its face, the pit's frontmost, between the front posts.
- **The pit:** the paving on the ground with its top at 1.5 and nothing else under it inside the curb (no piers);
  the curb on the ground, every stone to its top (none cut down) but the gate's, left out; the drag
  stones on the paving at rest and inside the curb over the cycle.
- **The charge's way:** in by hand over a rim: the curb's top at most `TIP_RIM_MAX` (half a block), and nothing
  fixed over the pit above it outside the middle column (the pit open from above; only the sweep passes there); each
  content bedded on the paving, inside the curb, clear of the footstep, under the stones' tops; the gate's threshold
  under the contents' tops (they are drawn out over it) and the gate above them, its front the draw face.
- **Supports:** the power shaft in its two hangers; the post on its step and in its top bearing; the pinion's hub
  on the power shaft's end.
- **Footprint:** nothing at any pose reaches the four dropped cells (each element's box against the cell's).
- **Containment:** nothing leaves the box over the cycle.
- **Gearing:** the pitch points move together and the ratio is the tooth counts'; over a whole turn of the pinion,
  every 2.5 degrees, no tooth runs into the wheel and the mesh is always engaged.
- **Gaps:** the arms and stones clear of the curb and the gate, at least 0.3 apart over the cycle.
- **Clearances:** every pair of parts that move against each other, over the three-turn cycle (every 15 degrees;
  every 5 in full runs), touches only at the intended contacts (`ALLOWED`: the shafts in their bearings and step,
  the keyed hub, the meshing teeth, the stones on the paving and through the contents).
- **No z-fighting** (full runs): no coplanar overlapping faces with either content at three axle angles, after the
  fix (`fix_coplanar`, a content at a time with the whole machine).
- **Files:** every texture declared; the frame shape the frame part alone; 8 cells, each with boxes inside it, none
  hollow, no lids; of the anchors only the power and the draw (no key matching `FORBIDDEN_ANCHOR`), no hopper, spout
  or chute element, and no chute, pipe or solder texture; the rig's `build` the generator's stages; the shipped
  model the checked one moved.

`tools/tests/test_arrastra_model.py` holds the written files to these rules without running the generator, and this
README's stage table to the generator's; `site/test/arrastra.test.ts` replays the reference poses and the viewer's
states.

### Rig schema (`arrastra-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"). The only input is θ; every driver is a
`rotate` on it, and the rest of the parts ride or stand still. Keys: `cells` (8, with `boxes`; no lids, none
hollow), `powerCell` and `powerFace`, `drawCell` and `drawFace` (the draw, a hand action; the crucible furnace's
`holeCell` is the pattern), `build`
(`{stages: [{requires}]}`, the crucible furnace's key, the stages in order, no materials yet) and `parts`.
`requires` values: `pit`, `post`, `arms`, `stones` (the stages), `charge`, `paste` (the contents), or null. No
`loadCell` and no `waterCell`: the arrastra is loaded and watered by hand, straight into the pit, and the validator
fails on any load, feed, chute, pour or water key (`FORBIDDEN_ANCHOR`).

### Editing by hand

Element names are the rig's interface (first-match globs, a part's id and `_`): `sh_*` (the power shaft), `fr_*`
(the frame), and `pinion_*`, `post_*`, `arms_*`, `stones_*`, `step_*`, `pit_*`, `charge_*`, `paste_*`.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or
stop regenerating.

## From the grinder

Copied from `733-grinder-model-v2` (its `Grinder/tools/make_shape.py` and `validate_grinder.py`): the vector and box
helpers, the bevel pair (`bevel`, `bevel_geometry`, `bevel_pitch_point`), the pit (`build_pit`), the head frame
(`build_head`, `hanger`), the power shaft (`build_power`), level 1's arrastra (`build_l1`), and the checks (gearing, supports, clearances,
gaps, containment, cells, z-fighting, the stage-floating rule). Changed to #733 and #732:

- **No overflow notch, launder or output**: batch, emptied by hand. The notch's curb stone is now the draw gate's
  place, moved to the front, at the controller.
- **No chute and no hopper**: the ball mill's chute head (`build_l2_chute`, `VANILLA_CHUTE`, the chute-profile check)
  is gone, and so are the grinder's hopper and its spout (`build_hopper`): the hopper's mouth was the machine's top
  face, which read as a place a chute could feed, and nothing automated feeds the arrastra (the owner's rule). The
  charge is tipped straight into the pit.
- **No levels**: the ball mill's parts and `LEVELS` are gone; level 1's parts are the build's stages, renamed off
  `l1*` (`l1post` → `post`, `pinion`, `step`; `l1arms` → `arms`; `l1stones` → `stones`).
- **No water inlet**: the grinder's soldered ppex inlet (`build_inlet`, `pipe_copies`) is gone, and with it the water
  anchor. The arrastra is watered by hand, a bucket a rock into the pit (the owner's ruling, [Water by the
  bucket](#water-by-the-bucket)).
- **Only the draw is an anchor**: `drawCell` (the gate). The grinder's infeed face, for a while `loadCell`, went with
  the hopper.
- **8 cells, not 10**: the grinder's upper back corners held the ball mill's girth gear; the arrastra has nothing
  there.
- **The contents are new**: the charge and the paste in the pit (the grinder drew none).
- The grinder's per-`requires` `fitted` collision (`9fa58a5`, never on main, not read by the game) is not used.

## Open for the owner

1. **8 cells, not #733's 10.** #733 keeps the grinder's 10 cells, but with the ball mill gone nothing of the
   arrastra reaches the two upper cells over the pit's back corners (the head frame and the drive are all in the
   middle column), so they are dropped as the front corners were. Ten is `EMPTY_CELLS` less two entries;
   the two cells would then be hollow, holding nothing.
2. **Loading with no anchor.** The charge and its bucket of water go straight into the pit by hand, over the low
   curb (6 voxels), from the open front corners or a side; gameplay decides what is clicked to load (any pit cell or
   the curb, with the charge or the bucket in hand). The model only draws the pit open for it. The draw could be on
   a side instead (east or west, any curb stone); the front keeps both sides free for a row of arrastras.
3. **The hopper is gone** (the owner's rule: nothing automated feeds a batch hand machine, and its mouth flush with
   the machine's top read as a place a chute could meet). The machine's top is now the head frame's stringers, at
   y 31, and the upper front cell holds only the head frame's front. #732's "the hopper's rim" becomes the pit's
   rim, the curb.
4. **The water by bucket** (the owner's ruling): poured straight into the pit after the charge, a bucket a rock,
   with no anchor; the model draws nothing for it.
5. **The stages and their order** (pit, post, arms, stones on a placed head frame and shaft) are a proposal; the
   frame's recipe and each stage's items and materials are open (#733).
6. **Sizes and speeds:** the pit two blocks across outside (period arrastras were 3 to 6 m, mule ones about the
   smaller end); four stones; the post at a third of the axle (9.6 turns a minute at the reference speed).
7. **The contents** are drawn still, level, in stand-in textures (granite gravel and sand); the stones do not
   visibly work them. The gate is drawn shut only: gameplay's draw is the lifting.
8. **Collision** fills the pit's cells up to the arms with the sweep's swept boxes; no lids.
9. **Provisional pending #711:** the name `drawCell` for the draw (and whether a wet batch machine watered by
   bucket needs a reservoir of its own, #711's rule for piped machines).

## Known weak spots, and what is not checked

1. **Box teeth.** Bevel teeth are boxes along the pitch cone at mid-face size (they do not taper), the wheel's
   lengthened into its body so they meet its back.
2. **Rigid chains.** The bridles do not swing.
3. **Overlaps in the masonry:** the paving's rows end under the edging course; the head frame's posts are set into
   the curb's stones; the gate's jambs into the stones either side of its opening. None of these faces share a plane (the z-fighting check
   holds), but they are boxes through boxes.
4. **Not seen in a client:** there is no block or renderer, so lighting, the textures' scale and the machine at
   play distance are unchecked. It was reviewed in the site's model viewer.
