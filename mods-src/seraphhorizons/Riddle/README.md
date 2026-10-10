# Riddle

Part of the Seraph Horizons mod (`../README.md`): the classify stage of ore processing (epic #684), at the
hand tier and tier 1. A miner's **riddle** of the 1800s classifies crushed ore: the fines fall through its
mesh and the oversize is left on it. Classified feed loses the unclassified penalty (× 0.85) at the
concentrator (#714). The riddle is square and shallow, after vanilla's pan (two stepped tiers of oak
boards, 11.5 across at the top and 2 deep), with a coarse woven mesh of iron wire. Two models share it,
element for element:

- **the hand riddle** (`riddle`), one block: the riddle laid on two oak bearers across a square plank box
  with iron corners that catches the fines. The player shakes it;
- **the riddle on its stand** (`riddlestand`), tier 1, one block wide and two long: the same box under a
  light oak stand (the fines box) and a low box of the same make in the second block (the oversize box). The
  riddle hangs at its north end from two iron hangers and rests near its south end on an oak roller. A long
  hand lever, rising above the block, swings the hangers through an iron link: worked to and fro it riddles
  a full charge (a stack, #714) without the riddle being held; pulled right back it swings the riddle out
  over the roller, which tips it into the second block, and the oversize goes off its far end into the
  oversize box.

This folder holds the models' generator and their rigs (`tools/`). The behaviour is #714 and is not built
yet; nothing here is C#. Tiers 2 to 4 of classifying (the grizzly and screens, the trommel) are a separate
machine (#730) and are not designed for here.

**Hand stations.** Neither model has mechanical power, a power cell or oil. The player works both by
holding right-click, as on the quern, the squaring shear and the press brake. The rigs' θ is that clock, one
turn a shake; the shake is drawn from its travel ψ, and only while a charge is being riddled.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the generator; the generic
half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one (no new
driver: every motion is a `gauge`, the shake a gauge's `lobes`).

**One folder for both.** The hand riddle and the stand are one station at two tiers (#714 is one issue: "at
tier 1 the same station on a stand takes a stack at a time"), so they are one folder with one generator that
writes both models, as they will be one feature with one switch. The riddle is built once (`build_riddle`),
and the generator fails unless the stand's riddle is the hand riddle's, every element the same box moved
(0, 0.3, 2.25) (`check_same_riddle`). The boxes are one builder too (`build_box`): the hand riddle's box is
the stand's fines box, and the oversize box is the same make, lower.

## The models

**The hand riddle.** One cell, `[0,0,0]`, the controller. The operator stands to the native north; a
container of crushed ore can stand to the east (`infeedSide`), and the fines go out to the south
(`outputSide`). Anchors: `output.pos` (on the fines heaped in the box at W 1) and `charge.pos` (the middle of
the charge in the riddle: the riddling sound, dust falling).

**The stand.** Two cells, 1 wide (x), 1 high (y), 2 long (z): `[0,0,0]` and `[0,0,1]`. The controller is
`[0,0,0]`, the fines end, nearest the player who placed it (the block's `side` would be the way they look, as
the other hand stations are placed): the fines box, the riddle at rest, the hangers and the lever. `[0,0,1]`
holds the oversize box; the roller is on the middle legs, at the cells' boundary. Infeed east (beside the
fines box), fines out west (`outputSide`, beside the fines box: the lever is inside the stand), oversize out
south (`oversizeSide`, beyond the oversize box). Anchors: `output.pos` in `[0,0,0]` on the fines,
`oversize.pos` in `[0,0,1]` on the oversize laid in its box, `charge.pos`.

**The lever has no boxes above the block.** The lever's handle rises 5.26 voxels over the stand's top
(its grip at y 21.25, where a standing player's hand is). Collision and selection come from the rig's `cells`
(as on the other machines, whose blocks build their boxes from the rig), and the generator writes boxes only
for the footprint's cells, each clipped to its cell (`shipped_cells`): nothing outside the footprint has a
box, so the handle above the block is drawn but can be neither walked into nor clicked. The blocktype (#714)
must take its boxes from the rig and never from the shape. The lever is drawn by the renderer, as every
moving part is, never by the block's frame shape. Two checks hold this: containment allows only the parts a
model names (`Model.above`: the stand's lever) past the footprint, and only through its top; and the files
check that the cells are exactly the footprint's with every box inside its cell.

**Fitted parts** (the rigs' `requires`). The frame (`requires` null) is what the block draws.

| Model | `requires` | Draws |
|---|---|---|
| hand | (frame) | the box (plank walls, a bottom, iron corners) and the two oak bearers across its mouth |
| hand | `riddle` | the riddle: two tiers of oak boards and the woven mesh of iron wires |
| stand | (frame) | the oak stand (six legs, top rails all round, low stretchers all round), the oak roller in iron bearings on the middle legs, the iron plates and pins the hangers and the lever turn on |
| stand | `boxes` | the fines box (the hand riddle's) and the oversize box |
| stand | `riddle` | the riddle, the hand riddle's |
| stand | `hangers` | the two iron hangers, the riddle's iron lugs and pins at its north end, the link's pin on the west hanger |
| stand | `lever` | the hand lever (oak, an iron strap at its pivot, the pin the link hangs on) and the iron link |
| both | `chargesmall` | the work: a part charge (k 1), its oversize bed, its one layer of fines and those fines in the box |
| both | `chargefull` | the work: a full charge (k 2), its bed, its two layers of fines and those fines in the box |

These are a sensible split for the build stages, which are worked out with the owner later; nothing here is a
recipe or an item. The viewer shows `chargesmall` only with a part charge chosen and `chargefull` only with a
full one (`requiresClass`).

## Model

Everything in the models was made for this mod: no other mod's model is used (vanilla's pan was read for its
proportions only), so the generator needs nothing from `build/mods`. The wood is the game's debarked oak
(`game:block/wood/debarked/oak`) and its oak planks (`game:block/wood/planks/oak1`, the boxes), the iron
`game:block/metal/plate/iron`, and the charge has the texture code `ore` (`game:block/stone/gravel/granite`
in the shape), which the renderer is to set to the crushed ore's texture. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/riddle.json` | The hand riddle, every part (47 elements). |
| `assets/seraphhorizons/shapes/block/riddle_frame.json` | Its static frame only, the box and the bearers (15 elements). |
| `assets/seraphhorizons/shapes/block/riddlestand.json` | The riddle on its stand, every part (104 elements). |
| `assets/seraphhorizons/shapes/block/riddlestand_frame.json` | The stand's static frame only (31 elements). |
| `assets/seraphhorizons/config/riddle-rig.json` | The hand riddle's cell, anchors, work, pace and part rig (10 parts). |
| `assets/seraphhorizons/config/riddlestand-rig.json` | The stand's (15 parts). |
| `tests/Riddle/rig-reference.json` | Every hand riddle part's matrix at a grid of poses, from the reference maths. |
| `tests/Riddle/stand-rig-reference.json` | The stand's. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z
north to south) in voxels from the controller cell's north-west bottom corner, so the shipped files are the
build frame divided by 16.

**The riddle** (both models, its underside at y0, its middle at (cx, cz)): a lower tier of oak boards 1
thick and 1 deep, 10 across outside (an 8 × 8 opening), and an upper tier stepped out to 11.5 across, so
it is 2 deep (vanilla's pan steps 7, 9 and 11, 3 deep); the north and south boards run across, the east and
west fit between them. The mesh: square iron wires 0.25 thick a voxel apart (openings of 0.75), eight along
x under eight along z, crossing on them, their ends let 0.4 into the lower tier's boards. Its top, the
charge's bed, is 0.7 over the underside.

**The boxes**: plank walls 0.75 thick (the north and south across, the east and west between), a bottom 1
thick on the ground inside them, and an iron angle down each corner (a plate on each face). The fines box
is 13 square and 4.4 tall (both models); the stand's oversize box is 13 × 12.5 and 2.5 tall.

**The charge** rides the riddle: an **oversize bed** on the mesh (6 square, 0.7 thick) and over it one
layer of fines (a part charge: 4 square, 0.5) or two (a full charge: 4.6 square, 0.5, and a top 2.8
square, 0.36, heaped 0.26 over the rim). As the charge is riddled the fines sink, the top into the layer
under it and that into the bed, each hidden inside the one under it by at least 0.07 (no driver scales a
part, so a layer cannot shrink: it sinks out of sight), and **the fines in the box** rise out of its bottom,
where they were hidden, layer by layer onto it. At W 1 the fines lie in the fines box, 1.35 deep.

**The hand riddle.** The riddle rests on the two bearers (oak, 0.9 × 0.8, across the box's mouth at
z 4.5 and 11.5, under the riddle's east and west boards). While a charge is riddled the player lifts it 0.4
off them and shakes it: turns about axes 2 blocks above it (so it moves nearly level, tilting about 2
degrees as hands do) carry its middle 1.2 to and fro (z) and 0.6 side to side (x), a quarter turn apart,
so it is swirled round an ellipse once a turn of the clock. It clears the bearers by at least 0.116. The
oversize bed is left in the riddle at W 1.

**The stand.** Oak legs 1.2 square at the four corners and at z 14.15..15.35 (the middle legs), top rails
all round (y 14.6..16), low stretchers all round (y 1.4..2.6). The riddle (middle z 10.25, underside
y 5.5) hangs at its north end: an iron lug on each of its upper tier's east and west faces at z 5, a pin at
its rim's top (y 7.5) through the lug and the eye of an iron hanger (0.4 × 0.8), which hangs 7.75 from a
pin through the side rail and an iron plate on its inner face (y 15.25). Near its south end the riddle's
lower tier rests on the **roller**, oak, radius 0.5, across the stand at z 14.75 (y 5), its ends in iron
bearings on the middle legs, just over the fines box's south wall.

The **hand lever** stands on the operator's right (west), pivoted on the west rail at z 8 by a pin through
the rail and an iron plate: an oak bar with an iron strap at the pivot, its arm 3 below the pivot carrying a
pin, its handle rising to y 21.25, 5.25 over the block. An iron link joins that pin to a pin 3 below the
west hanger's pivot. Lever and hanger turn about pivots at one height with arms of one length, so lever,
link and hanger are a parallelogram: the lever turns exactly as the hangers do, the link stays parallel to
itself, and no slot is needed (the pass-1 design's link had one). Pulling the handle north swings the
riddle south.

- **Riddling.** Worked to and fro, the lever swings the hangers 11.2 degrees each way: the riddle goes 1.5
  to and fro, sliding on the roller, and rocks a little on it (its far end down by up to 1.0 degree as its
  north end rises at either end of the swing, twice a shake). The rock is fitted to the roller's contact
  (`tilt`, `riddling_tilt`: a term in the swing and one in its square, the latter a gauge's lobes of ratio 2).
- **The tip.** Pulled right back, the lever swings the hangers 60 degrees south. The riddle's north end
  rises (to y 11.4) and comes south, and the riddle slides out over the roller and tips about it, its far
  end down 43.8 degrees and out over the second block (z 19.65). The tip is the roller's doing: hung at
  one end and resting on the roller, the riddle's angle follows from where its north pin is (`tilt` solves
  the underside tangent to the roller). The swing out is drawn in ten straight steps, the tip fitted at each.
- **The oversize** is lifted off the tipped mesh (clear of the rim), carried straight off the riddle's far
  end to over the oversize box (away from the tipped rim all the way), levelled there and laid on the box's
  floor, where it stays while the lever is pushed back and the riddle swings back to rest. It is the one
  part that leaves the riddle, so its rig writes the riddle's motion out in full (its swing out kept) rather
  than riding it, and the hidden layers of fines ride it.

**The cycles** (t = W, one charge; θ, the clock, poses the shake, and only inside its window):

| Hand riddle t | |
|---|---|
| 0.00..0.04 | The charge goes on (p eases in): the heap on the mesh, the box's fines hidden in its bottom. |
| 0.04..0.08 | The riddle is lifted off the bearers and the shake eases in. |
| 0.10..0.38 | A full charge's top sinks into its lower layer; its first fines rise onto the box's bottom. |
| 0.42..0.76 | Its lower layer sinks into the bed; the second fines rise onto the first. (A part charge: its one layer sinks over 0.10..0.76.) |
| 0.80..0.84 | The shake eases out and the riddle is set down. |
| 0.84..1.00 | Still. Delivered at 1, the bed in the riddle. |

| Stand t | |
|---|---|
| 0.00..0.04 | The charge goes on. |
| 0.04..0.66 | Riddled: the shake eases in over 0.04..0.08 and out over 0.62..0.66; a full charge's top sinks over 0.10..0.34 and its lower layer over 0.38..0.60, the fines rising with them (a part charge over 0.10..0.60). |
| 0.68..0.80 | The lever pulled right back: the hangers swing out 60 degrees, the riddle tips 43.8 over the roller. |
| 0.80..0.88 | The oversize lifted off (0.80..0.82), carried over the oversize box (0.82..0.85), levelled (0.85..0.865), laid on its floor (0.865..0.88). |
| 0.88..1.00 | The lever pushed back: the riddle swings back and rights itself on the roller, at rest at 1. Delivered at 1. |

Every moving part (W the riddling, k the charge, p its presence, ψ the clock's travel; "shake" is a gauge of
amount 0 with `lobes` {ratio 1, amplitude A}, in the shaking window, so it turns by e × A × cos(ψ + phase), e
its engagement; "swing out" is ten gauges, one a step of the swing):

| Model | Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|---|
| hand | Riddle: boards, mesh (`riddle`) [riddle] | The player's hands | The charge on it | `gauge` slide y +0.4 (the lift), shake about x and shake about z, both about a point 2 blocks over it |
| stand | Hangers, the link's pin (`hangers`) [hangers] | The link (west hanger) | The riddle, the link | shake about x through the pivots, A −0.1948; swing out, −60 degrees in all |
| stand | Riddle (`riddle`) [riddle] | The hangers, at its north pins; the roller under it | The charge on it | rides `hangers`; about its north pin: shake A +0.1971 (back as far as the hangers turn, plus the rock's swing term) and the rock's square term (lobes ratio 2); swing out, +60 degrees back plus its tip, 43.8 in all |
| stand | Lugs, north pins (`lugs`) [hangers] | Fixed to the riddle | The hangers' eyes | rides `riddle` |
| stand | Lever: bar, strap, pin (`lever`) [lever] | The player's hand | The link | as the hangers, about its own pivot |
| stand | Link (`link`) [lever] | The lever's pin | The west hanger | rides `hangers`; turned back about its pin on the hanger (parallel to itself) |
| hand | Oversize bed (`c1base`, `c2base`) [charge…] | Rides the riddle | — | none |
| stand | Oversize bed (`c1base`, `c2base`) [charge…] | The riddle, then tipped off | — | the riddle's whole motion written out, its swing out kept; lifted, carried, levelled and laid in the oversize box |
| both | Fines in the riddle (`c1top`; `c2mid`, `c2top` riding it) [charge…] | Ride the bed; the shake | — | `gauge` slide y down into the layer under it |
| both | Fines in the box (`c1fines`, `c2fines1`, `c2fines2` riding it) [charge…] | Falling through the mesh | — | `gauge` slide y up out of the box's bottom |
| stand | Boxes (`boxes`) [boxes] | Static | — | none |
| both | Frame (`frame`) | Static | — | none |

There are no gears. Every pin is carried: the hangers' and the lever's pivot pins in the side rails and their
iron plates, the roller in the middle legs and their iron bearings, the north pins in the lugs and the
hangers' eyes, the link's pins in the hanger and the lever.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py              # stdlib only; rewrites the eight files
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 20 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_riddle.py` holds the checks; every run checks both models and exits non-zero if
one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part.
- **Frame:** joined to the ground as one piece.
- **Textures** by role: oak boards, bearers, stand, roller and lever; planks for the boxes' walls and bottoms;
  iron corners, wires, hangers, lugs, link, plates, bearings, pins and strap; `ore` for the charge.
- **Containment:** nothing leaves the footprint over the cycle of both charges and the shake's phase, but the
  parts a model lets rise above it (the stand's lever, up to 5.26 over the top), and those only through its
  top; the lever must rise above the block.
- **Charge:** as loaded at W 0, the box's fines hidden in its bottom (the second layer in the first); the bed
  moving with the riddle exactly while it is riddled; at W 1 every layer of fines sunk inside the bed (a full
  charge's top first inside its lower layer), measured in the bed's frame, the fines on the box's bottom and
  the second layer on the first. Every hidden layer is inside what hides it by at least `HIDE_MARGIN` (0.05).
- **Shake:** the clock moves nothing at W 0, 0.02, 0.9 and 1 and just past the shaking window (nor with no
  charge); in the window the riddle goes 1.2 to and fro and 0.6 side to side by hand, 1.5 to and fro on the
  stand.
- **Hand:** the riddle rests on the bearers, and clears them by 0.05 or more while shaken.
- **Stand:** the riddle's north end on the hangers' pins and the link on both its pins, exactly (the
  parallelogram); the riddle's underside on the roller through the riddling, the swing out and back (off it
  by −0.043 to 0.009: the rock and the steps are fitted, not solved); swung out, the riddle tipped 35
  degrees or more (43.8) with its far end over the second block (past z 17; 19.65), and back at rest at
  W 1; the oversize laid flat on the oversize box's floor, inside its walls; every pivot pin carried by its
  rail and plate, the roller by the middle legs and their bearings; every eye round its pin by at least the
  pin's radius through the swing.
- **Clearances:** over 249 (hand) and 393 (stand) poses (W against the shake's phase, both charges) no two
  parts touch except the intended contacts in `ALLOWED` (the riddle on the roller is one); **swept paths**
  (full runs) the same every 0.0025 of the cycle at the pace: the riddle's tip past the fines box's wall,
  the oversize's way off past the rim and over the oversize box's wall.
- **No z-fighting** (full runs): no coplanar overlapping faces at 10 (hand) and 16 (stand) poses of what can
  be seen (the other charge's parts left out, `shown`).
- **The same riddle:** the stand's riddle is the hand riddle's moved, every element.
- **Files:** every texture declared; the cells exactly the footprint's, every box inside its cell, a lid on
  every column; no power cell; the shipped models are the checked ones (no move: the build frame's corner
  is the controller's).

The cells' boxes are rebuilt from the shipped, rounded shapes posed at rest by the shipped rigs, as the other
machines' are. The stand's two cells are each one full box (its frame fills them), lidded at 1.

### Rig schema (`riddle-rig.json`, `riddlestand-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") as the squaring shear uses it, with nothing
added to the shared rig maths: a `work` quantity and gauges, the shake a gauge's `lobes` (the rosser's
scraper arms are the other user of them). Unlike the other hand stations, no driver reads θ itself (there is
no ratio-0 rotate): the lobes read its travel ψ, which is enough for the viewer to offer the clock and Play.

- **The work.** `{ "name": "riddling", "unit": "charges", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one charge's riddling, 0..1 (on the stand, its tipping off too).
- **Inputs.** W, k and p, and ψ. k is the charge (1 a part charge, 2 a full charge, 0 none): the renderer is
  to choose it by how much is in the riddle, as the mill shows two trunk sizes. p is its presence, eased in
  as it goes on and out after delivery, k held while p eases out. ψ is the clock's travel: gameplay is to
  turn θ while the player holds right-click and advance W by 1 / `shakesPerCharge[k]` a turn. The shake's
  engagement is p times its window's, so it eases in and out with the riddling and stops with no charge.
- **`riddling`**: `shakesPerCharge` (hand: 12 a part charge, 20 a full one; stand: 20 and 32), the pace.
  Provisional: #714 sets it.
- **The stand's anchors** add `oversizeSide` (south) and `oversize.pos` (in the second cell, on the oversize
  laid in its box).

**Keys.** `cells`, `infeedSide`, `outputSide`, `output`, `charge` (the stand also `oversizeSide`,
`oversize`), `work`, `riddling` and `parts`. `requires` values: hand `riddle`, `chargesmall`, `chargefull` or
null; stand also `boxes`, `hangers`, `lever`. The texture code the renderer sets: `ore`.

**In the viewer** (`site/models.json`, `riddle` and `riddle-stand`; a standalone copy each with
`site/scripts/standalone-viewer.ts`): the clock is the θ slider (its travel shown under it); the riddling's
slider reads in charges (on the stand it runs through the tip and back); the charge select names the sizes
("Part charge", "Full charge"), and each size's charge and fines show only with it. Play moves W with θ at
a part charge's pace (`play.turnsPerWork` `"riddling.shakesPerCharge.thin"`).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `riddle_*`
(`riddle_lower_*`, `riddle_upper_*`, `riddle_wirex_*`, `riddle_wirez_*`), the charge's `c1base_*`, `c1top_*`,
`c2base_*`, `c2mid_*`, `c2top_*`, `c1fines_*`, `c2fines1_*`, `c2fines2_*`; on the stand `hanger_*`, `lug_*`,
`lever_*`, `link_*`, `box_*` and `obox_*` (the `boxes` part); and `fr_*` for each frame (the hand riddle's box
is `fr_box_*`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

## Known weak spots, and what is not checked

The models have been reviewed in projections rendered from the written files. No one has yet looked at them
in a client, and the standalone viewers were built but not seen with WebGL here.

1. **The fines sink rather than fall.** No driver scales a part, so the heap cannot shrink: each layer of
   fines sinks into the one under it, and the fines in the box rise out of its bottom. Nothing is drawn
   falling through the mesh; gameplay's dust particles under the riddle are to carry that.
2. **The oversize bed is large** (6 of the 8 square opening), as it must be thicker than any layer hidden in
   it: what is left reads as a bed of oversize, not a few lumps.
3. **The oversize's way off is stylized.** It lifts off the tipped mesh, moves straight to over the box,
   levels and drops, a slab throughout; it does not tumble or pour.
4. **The riddle rides the roller within −0.043 to 0.009.** The rock while riddling is a fit (a term and its
   square), eased in linearly where the true rock eases in with the square of the swing, and the swing out
   is ten straight steps; the riddle dips into the roller by up to 0.043 at the start of the riddling.
5. **The hand riddle's cause is the player.** No hands are modelled: the riddle moves as held.
6. **The shake stops where it is.** If the player lets go part way through a charge, θ stops and the riddle
   holds where it was in its swing; a renderer can ease θ to the next whole turn.
7. **The tip is part of the work, not the clock.** In the game the swing out and back happen as W runs
   through 0.68..1 at the riddling's pace; nothing draws the player pulling the lever harder.
8. **Linear ramps** for the fines and the swing out, as the other hand stations' gauges.
9. **A full charge looks the same on both models,** though the stand's is "a stack".

## Open for the owner

1. **The stand's mechanism.** A riddle hung at one end and resting on a roller, so the long lever both
   riddles it (small swings) and tips it into the second block (pulled right back), the roller doing the
   tipping; lever, link and hanger a parallelogram. Alternatives weighed: tipping about trunnions empties
   over the fines box; a riddle that flips over a hinge into the second block needs two blocks of height;
   four hangers forming a tipping four-bar barely carry the riddle past the first block.
2. **The oversize's path** (weak spot 3), and whether the riddle should tip further (43.8 degrees now; more
   needs a longer swing or a higher pivot, and the north end then rises over the block).
3. **A hopper** over the stand for "a stack at a time": left out (a stack is a full riddle here).
4. **Oversize at all?** If #714 makes the riddle a plain conversion (crushed to classified, no oversize item),
   the oversize box and the tip could go and the stand return to one block.
5. **The two charge sizes** (a part charge and a full one) for k, and their paces (12/20 shakes by hand,
   20/32 on the stand): provisional, gameplay's to set. Should the stand's full charge be heaped higher?
6. **Sizes.** The riddle 11.5 across and 2 deep (vanilla's pan: 11 and 3), the boxes 13 square, the stand
   16 × 32 and a block high, the lever's grip 5.25 over it.
7. **Sides.** Infeed east, fines out west (stand) or south (hand), oversize out south: placeholders.

## Gameplay

Not built: #714. The rigs are written for it the way the other hand stations' are (the squaring shear's
`Core/` and `Game/` are the template): read the anchors, `work`, `riddling.shakesPerCharge` and `requires`,
never element names; take the blocks' collision and selection boxes from the rig's `cells` (never from the
shape, so the stand's lever has no box above the block); set the texture code `ore`; draw the
`chargesmall`/`chargefull` parts only while that charge is on the station; turn θ while held and give the
renderer ψ, its travel.
