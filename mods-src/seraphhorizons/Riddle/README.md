# Riddle

Part of the Seraph Horizons mod (`../README.md`): the hand station of the classify stage of ore processing (epic
#684). A miner's **riddle** of the 1800s, a coarse hand sieve, classifies crushed ore: the fines fall through its mesh
and the oversize is left on it. Riddled feed is classified, so it loses the **unclassified × 0.85** at the
concentrating step after it (a rocker gets 55 % instead of 46.8 %). It comes after spalling (#747) and before the
rocker (#716); its machine successors are the stamp battery's screen (#728) and the ball mill's (#915), separate
machines with their own models.

The player card (#714, the handbook's text):

> **What it is.** A coarse hand sieve: the **riddle** (#714).
> **What it does.** Crushed ore in; classified crushed ore out.
> **Why use it.** It lifts the **unclassified × 0.85**: a rocker gets 55 % instead of 46.8 %.
> **If you skip it.** Unclassified feed concentrates at × 0.85.
> **Where it goes.** After spalling, before the rocker. **Needs.** Nothing. By hand.
> **Replaced by.** The stamp battery's screen and the ball mill's.

The riddle is square and shallow, after vanilla's pan (two stepped tiers of oak boards, 11.5 across at the top and 2
deep), with a coarse woven mesh of iron wire, over a plank box that catches the fines. #715 asks for it in two forms,
and both are modelled, one block each, sharing the riddle and the box element for element:

- **The hand riddle** (`riddle`): the riddle rests by its north and south boards on two oak bearers laid across the
  box's mouth. The player lifts it just off them and swirls it, to and fro and side to side.
- **The riddle on its stand** (`riddlestand`): the same riddle hung by four iron straps from a light oak stand over
  the same box, and worked by hand like the hand riddle: the player swings it to and fro by an oak grip on its north
  side, while the straps carry its weight.

The oversize stays in the riddle, to be taken out by hand.

**Period practice.** On the 1800s dressing floors of Cornwall, and in the American camps that copied them, ore broken
by hand was riddled over a tub or a box. The riddle was held in both hands, shaken and swirled, and the fines went
through into the tub. What stayed on the mesh, the riddlings, was picked over by hand and went back to be broken
again. The hand riddle follows that: the box is the tub, and the bearers are the two sticks a riddle rested on between
charges. The stand form hangs the same riddle from a frame, so it is swung rather than held. That frame is the pack's
own design, not taken from a source cited in #939. Simplified: the wire mesh is coarser and chunkier than a real one
(it must read at block scale), and its wires cross in two layers rather than going over and under.

This folder holds the models' generator and their rigs (`tools/`). The gameplay is #714 and is not built yet; the
model is #715. "Gameplay" below says what the rigs offer it.

**A hand station.** Neither form has mechanical power, a power cell, oil or a pipe. The player works either one by
holding right-click, as on the quern, the squaring shear and the press brake. The rigs' θ is that clock, one turn a
shake (or a swing). The motion is drawn from its travel ψ, and only while a charge is being riddled.

**No automated input or output.** A hand station is loaded and unloaded by the player, by hand (the owner's ruling for
every hand station, #684): no chute, hopper or other item transfer puts crushed ore in or takes fines or oversize out.
So neither rig has an `infeedSide` or an `outputSide`, or any other side, face, cell or point for one. The only side
is `operatorSide`, where the operator stands. The generator fails if one appears.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the generator. The generic half of
it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one. No new driver: every
motion is a `gauge`, the shake and the swing a gauge's `lobes`, the fines stretching gauges.

## The stations

**Footprint.** Each form is 1 cell, the controller `[0,0,0]`, with no ghost.

**Orientation.** The operator stands on the native north side (`operatorSide`). Placed as the other hand stations are
(`side` = the way the player looks), that is the side the player placed it from. The hand riddle is symmetric but for
its bearers, which run west to east, across the operator's view. The stand's frames stand on the west and east sides,
the grip is on the north, the operator's side, and the riddle swings toward and away from the operator.

**Anchors** (the same in `assets/seraphhorizons/config/riddle-rig.json` and `riddlestand-rig.json`):

| Key | Where | For |
|---|---|---|
| `operatorSide` | `north` | Where the player stands to work it. |
| `fines.pos` | `[0.5, 0.1469, 0.5]` | On the fines in the box at W 1, where the player takes them out. |
| `charge.pos` | `[0.5, 0.45, 0.5]` | The middle of the charge in the riddle: the riddling sound and dust, and where the oversize is taken out. |

There is no infeed side and no output side: see "No automated input or output" above.

**Fitted parts** (the rigs' `requires`). The frame (`requires` null) is what the block draws.

| Form | `requires` | Draws |
|---|---|---|
| hand | (frame) | The box (plank walls and bottom) and the two oak bearers across its mouth. |
| stand | (frame) | The box, and the stand: on each side a sill on the ground, a post on it, a head on the post and two knee braces from post to head, with two ties between the heads at their ends. |
| both | `riddle` | The riddle: two tiers of oak boards and the woven mesh of iron wires. On the stand also its grip (two iron posts and an oak bar on the north board) and the four pins the straps hang it by. |
| stand | `hangers` | The four iron straps, each with its pin's square head where it hangs from its head. |
| both | `chargesmall` | The work: a part charge (k 1): three lumps of oversize, one layer of fines over them, and those fines in the box. |
| both | `chargefull` | The work: a full charge (k 2): four lumps, two layers of fines, and those fines in the box. |

There are no levels. These ids are a sensible split for build stages, which are worked out with the owner later. The
viewer shows `chargesmall` only with a part charge chosen and `chargefull` only with a full one (`requiresClass`).

## Model

Everything in the models was made for this mod: no other mod's model is used (vanilla's pan was read for its
proportions only), so the generator needs nothing from `build/mods`. The materials are all the game's:

- the wood is debarked oak (`game:block/wood/debarked/oak`), with oak planks (`game:block/wood/planks/oak1`) for the
  box;
- the iron is `game:block/metal/plate/iron`;
- the charge has the texture code `ore`. Its default is hematite's nugget texture (`game:item/resource/nugget/hematite`),
  because the pack's crushed ore wears its ore's nugget texture (`itemtypes/oreprocessing/crushedore.json`). The
  renderer is to set `ore` to the riddled ore's texture.

The viewer's entries have a texture select for `ore` with seven ores; their colours are each nugget texture's mean,
from the game's files. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/riddle.json` | The hand riddle, every moving part and both charges (36 elements). |
| `assets/seraphhorizons/shapes/block/riddle_frame.json` | Its static frame only: the box and the bearers (7 elements). |
| `assets/seraphhorizons/shapes/block/riddlestand.json` | The riddle on its stand, every part (61 elements). |
| `assets/seraphhorizons/shapes/block/riddlestand_frame.json` | Its static frame only: the box and the stand (17 elements). |
| `assets/seraphhorizons/config/riddle-rig.json` | The hand riddle's cell, anchors, work, pace and part rig (10 parts). |
| `assets/seraphhorizons/config/riddlestand-rig.json` | The stand's (12 parts). |
| `tests/Riddle/rig-reference.json` | Every hand riddle part's matrix at a grid of poses, from the reference maths. |
| `tests/Riddle/stand-rig-reference.json` | The stand's. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north to
south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame divided by 16.
The station's middle is (8, 8).

**The box** (both forms): plank walls 1 thick and 4.4 tall, 13 square outside (the north and south walls across, the
east and west between them), and a bottom 1 thick on the ground inside them.

**The riddle** (both forms, its underside at y 5.4): a lower tier of oak boards 1 thick and 1 deep, 10 across outside
(an 8 × 8 opening), and an upper tier stepped out to 11.5 across, so it is 2 deep (vanilla's pan steps 7, 9 and 11,
and is 3 deep). The north and south boards run across, and the east and west fit between them.

The mesh: four iron wires 0.75 square along x, 2 apart (openings of 1.25), their underside 0.15 over the riddle's.
Over them lie four along z, crimped 0.35 into them where they cross, and every wire's ends are let 0.4 into the lower
tier's boards. The mesh's top, the charge's bed, is 1.3 over the riddle's underside.

**The hand riddle's bearers**: two oak bars, 1 square, laid across the box's mouth on its walls (y 4.4 to 5.4), from
x 0.9 to 15.1 (their ends past the walls). Each lies under one of the riddle's lower north and south boards (z 3 to 4
and 12 to 13), so each carries a whole board.

**The stand**: oak timbers 1.5 square, outside the box's walls on the west and east, in the block's edge.

- On each side, a sill on the ground (z 1 to 15), a post in its middle up to y 14.5, and a head on the post along the
  block's top (z 1 to 15).
- Two knee braces on each side, 1 across, at 45 degrees from the post (3 under the head) to the head (3 out from the
  post), housed at both ends.
- Two ties between the heads at their north and south ends (z 1 to 2 and 14 to 15), a hair inside the heads' height.

The riddle hangs at the hand riddle's height, its underside 1 over the box's walls.

**The straps**: four iron straps, 0.75 thick and 1 wide, filling the gap between each head's inner face and the
riddle's side. They hang from pins through the heads (y 15.25) to pins into the riddle's upper side boards (y 6.9,
8.35 below), at z 3.75 and 12.25. The north pair are one part and the south pair another. The riddle carries the four pins
into it, their heads on the straps' outer faces, and its **grip**: two iron posts 1.5 tall on its north board, 4.5
apart, carrying an oak bar 1 square and 6 long.

**The charge** rides the riddle:

- The **oversize**: three lumps (a part charge) or four (a full charge), 1.2 to 1.5 across and 1.3 to 1.5 tall,
  turned about y. Each stands on the mesh's top over a wire along z, inside the riddle's top tier.
- The **heap of fines** over them: one layer (a part charge: 6.8 square, 0.9 over the mesh) or two (a full charge:
  7.2 square, 1.0 over the mesh, and a top 4.8 square and 0.7 thick, heaped 1.0 over the rim). Its underside is
  settled 0.15 into the mesh, between the wires' tops and bottoms. The lumps stand 0.3 or more out of it.

As the charge is riddled the heap **shrinks** onto its underside, layer by layer (a full charge's top first), and is
gone. **The fines in the box** (in the box, not the riddle) **grow** from its bottom, layer by layer, each from
nothing (a part charge's one layer 8 square and 0.75 deep; a full charge's two, 9.2 square and 0.75, then 6 square and
0.6 on it). These are stretching gauges along y (`docs/recipe-browser/models.md`, "Stretching gauges"). At W 1 the
heap is gone, the fines lie in the box (1.35 deep for a full charge), and the lumps, the oversize, lie on the bare mesh.

**The hand riddle's shake.** While a charge is riddled the player lifts the riddle 0.5 off the bearers and swirls it.
Turns about axes 3 blocks above it (so it moves nearly level, tilting about 2 degrees as hands do) carry its middle
1.5 to and fro (z) and 0.75 side to side (x), a quarter turn apart, so it goes round an ellipse once a turn of the
clock. It clears the bearers by at least 0.166, and stays inside the block.

**The stand's swing.** While a charge is riddled the straps swing about their pins in the heads, 10.35 degrees each
way, so the riddle goes 1.5 to and fro (z), once a turn of the clock. The riddle rides the north straps and turns back
about its own pin in them by as much, so it never tilts: it moves as the fourth side of a parallelogram, level, on an
arc that lifts it 0.136 at the ends of its swing. The south straps turn with the same angle, so their pins stay in the
riddle's. It clears the box's walls by 1 throughout.

**The cycle** (t = W, one charge; θ, the clock, poses the shake or the swing, and only inside its window):

| t | |
|---|---|
| 0.00..0.04 | The charge on the mesh as loaded; nothing in the box. |
| 0.04..0.08 | The shake (or the swing) eases in; the hand riddle is lifted off its bearers. |
| 0.10..0.38 | A full charge's top shrinks onto its lower layer; its first layer of fines grows in the box. |
| 0.42..0.76 | Its lower layer shrinks into the mesh; the second layer of fines grows on the first. (A part charge: its one layer shrinks, and its fines grow, over 0.10..0.76.) |
| 0.80..0.84 | The shake eases out; the hand riddle is set down. |
| 0.84..1.00 | Still. Delivered at 1: the fines in the box, the oversize on the mesh. |

Every moving part, in the terms the rigs use:

- W is the riddling, k the charge, p its presence and ψ the clock's travel.
- A "shake" or "swing" is a gauge of amount 0 with `lobes` {ratio 1, amplitude A} in the shaking window. It turns by
  e × A × cos(ψ + phase), where e is its engagement.
- "Shrinks" and "grows" are stretching gauges along y, with amount −1 for their class.

| Form | Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|---|
| hand | Riddle: boards, mesh (`riddle`) [riddle] | The player's hands | The charge on it | `gauge` slide y +0.5 (the lift); a shake about x and a shake about z, both about a point 3 blocks over the middle |
| stand | North straps (`hangernorth`) [hangers] | The riddle, swung by its grip | — | a swing about x through their pins in the heads |
| stand | South straps (`hangersouth`) [hangers] | The riddle, swung by its grip | — | a swing about x through their pins in the heads, the same angle |
| stand | Riddle: boards, mesh, grip, pins (`riddle`) [riddle] | The player's hand on the grip | The straps; the charge on it | rides `hangernorth`; a swing about x through its pin in them, the same angle back |
| both | Oversize: lumps (`c1over`, `c2over`) [charge…] | Rides the riddle | — | none |
| both | Fines in the riddle (`c1heap`; `c2heap`, `c2top`) [charge…] | Ride the riddle; the shake | — | shrinks onto its underside |
| both | Fines in the box (`c1fines`; `c2fines1`, `c2fines2`) [charge…] | Falling through the mesh | — | grows from its underside |
| both | Frame (`frame`) | Static | — | none |

There are no gears or shafts. The straps' pins are the stand's only joints, and the checks hold them pinned.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py              # stdlib only; rewrites the eight files
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 10 s and is deterministic (two runs into two folders are byte-identical, logs included).
`tools/validate_riddle.py` holds the checks. Every run checks both forms' output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Readable:** at most 40 elements (the hand riddle) or 64 (the stand), and no part of the riddle, the stand or the
  frame thinner than 0.75 (the charge's lumps and layers are a heap and are left out).
- **Frame:** joined to the ground as one piece.
- **Textures** by role: oak boards, bearers, stand timbers and grip bar; planks for the box; iron wires, straps, pins
  and grip posts; `ore` for the charge.
- **Mesh:** the wires along z over those along x, crimped into them; every wire's ends in the lower tier's boards; the
  openings 1.25.
- **Containment:** nothing leaves the block over the cycle of both charges and the shake's phase.
- **Charge**, for each class, through one riddling:
  - as loaded at W 0: the heap whole, nothing in the box;
  - the lumps riding the riddle;
  - every layer of the heap shrinking onto its own underside in the riddle's frame, never growing, and gone by the
    end of its window (a full charge's top before its lower layer starts);
  - every layer of fines in the box growing from its underside, never shrinking, from nothing to whole over its
    window;
  - at W 1: the riddle at rest, the lumps on the mesh, each standing on a wire along z inside the riddle's top tier
    and 0.25 or more out of the heap, and no two lumps overlapping.
- **Clock:** it moves nothing outside the shaking window, nor with no charge.
- **Shake** (the hand riddle): in the window the riddle goes 1.5 to and fro and 0.75 side to side.
- **Bearers** (the hand riddle): on the box's walls, each under a whole board of the riddle; the riddle rests on both,
  and clears them by 0.05 or more while shaken.
- **Swing** (the stand), over the grid of poses and the cycle:
  - the riddle never turns (it hangs level);
  - it goes 1.5 to and fro and never sideways;
  - it rises no more than its straps allow, and never drops below its rest;
  - every strap's top pin stays put and its bottom pin moves with the riddle's (to 1e-6).
- **Stand:**
  - each strap lies between its head's inner face and the riddle's side, its top pin in the head, its bottom pin's
    head on the riddle's upper side board;
  - each post stands on its sill under its head;
  - each brace runs from inside its post to inside its head;
  - the riddle hangs at least 0.5 over the box's walls.
- **Same riddle:** the stand's riddle is the hand riddle's, element for element, up to the z-fighting fix's insets; it
  only adds its grip and pins.
- **Clearances:** over 249 poses (W against the shake's phase, both charges) no two parts touch, except the intended
  contacts in `ALLOWED`:
  - the hand riddle on its bearers;
  - the lumps on the mesh, and the heap settled into it with the lumps standing in it;
  - the heap's top on its lower layer;
  - the fines on the box's bottom, and the second layer on the first.

  The **swept paths** (full runs) check the same every 0.0025 of the cycle, at the pace.
- **No z-fighting** (full runs): no coplanar overlapping faces at ten poses of what can be seen (the other charge's
  parts left out, `shown`). Neither form has any before the fix. It only removes the faces pressed against their own
  part (16 on the hand riddle, 32 on the stand).
- **Files:**
  - every texture declared;
  - the one cell, boxed inside itself and lidded;
  - no power cell;
  - no infeed or output side, face, cell or point, only `operatorSide` (no automated input or output);
  - the shipped model is the checked one (no move: the build frame's corner is the controller's).

Each cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the other machines'
are:

- the hand riddle: one box over the box, the riddle and a full charge's heap, lidded at its top (0.525);
- the stand: one box over the whole block, lidded at the stand's top.

### Rig schema (`riddle-rig.json`, `riddlestand-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema"), as the squaring shear uses it, with nothing added
to the shared rig maths:

- a `work` quantity and gauges;
- the shake and the swing are a gauge's `lobes` (the rosser's scraper arms are the other user of them);
- the fines are stretching gauges (the mandrel station's spring is the other user of them).

Unlike the other hand stations, no driver reads θ itself (there is no ratio-0 rotate): the lobes read its travel ψ,
which is enough for the viewer to offer the clock and Play.

- **The work.** `{ "name": "riddling", "unit": "charges", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one charge's riddling, 0..1.
- **Inputs.** W, k and p, and ψ.
  - k is the charge (1 a part charge, 2 a full charge, 0 none). The renderer is to choose it by how much is in the
    riddle, as the mill shows two trunk sizes.
  - p is its presence: 1 while a charge is on (below).
  - ψ is the clock's travel. Gameplay is to turn θ while the player holds right-click, and advance W by
    1 / `shakesPerCharge[k]` a turn. The shake's engagement is p times its window's, so it eases in and out with the
    riddling and stops with no charge.
- **Presence.** Every gauge eases with p, so a charge's parts are right only at p 1:
  - at W 0, with p under 1, the box's fines would show part grown;
  - at W 1, as p eases out, the heap would grow back.

  So gameplay puts a charge on at p 1 at once, and stops drawing its parts when the oversize is taken out, as the
  mandrel station stops drawing its work at delivery.
- **`riddling`**: `shakesPerCharge` (12 a part charge, 20 a full one), the pace, the same on both forms. Provisional:
  #714 sets it.
- **Keys.** `cells`, `operatorSide`, `fines`, `charge`, `work`, `riddling` and `parts`; no infeed or output side, face
  or cell (a hand station takes no automated input or output).
  - `requires` values: `riddle`, `chargesmall`, `chargefull`, with `hangers` on the stand, or null.
  - The texture code the renderer sets: `ore`.

**In the viewer** (`site/models.json`: `riddle` and `riddle-stand`; a standalone copy of each with
`site/scripts/standalone-viewer.ts`):

- the clock is the θ slider, with its travel shown under it, and the riddling's slider reads in charges;
- the charge select names the sizes ("Part charge", "Full charge"), and each size's charge and fines show only with
  it;
- Play moves W with θ at a part charge's pace (`play.turnsPerWork` `"riddling.shakesPerCharge.thin"`), and starts at
  0.75 s a turn;
- coloured by texture, the "Crushed ore" select sets the charge's colour.

### Editing by hand

Element names are the rigs' interface (first-match globs, in the rigs' order):

- `riddle_*`: `riddle_lower_*`, `riddle_upper_*`, `riddle_wirex_*`, `riddle_wirez_*`, and on the stand
  `riddle_grip_*` and `riddle_pin_*`;
- `hanger_n_*` and `hanger_s_*`: the straps and their pins' heads;
- the charge: `c1over_*` and `c2over_*` (`_lump1`..), `c1heap_*`, `c2heap_*`, `c2top_*`, `c1fines_*`, `c2fines1_*`
  and `c2fines2_*`;
- `fr_*` for the frame: `fr_box_wall_*`, `fr_box_bottom`, `fr_bearer_*`, and on the stand `fr_sill_*`, `fr_post_*`,
  `fr_head_*`, `fr_brace_*` and `fr_tie_*`.

Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The models have been reviewed in projections rendered from the written files. They have not been looked at in the
site's viewer in a browser, or in a client.

1. **The fines shrink and grow; nothing falls.** Nothing is drawn falling through the mesh. Gameplay's dust particles
   under the riddle are to carry that.
2. **The heap shrinks as flat layers**, top first, not as a heap settling round the lumps.
3. **The fines in the box are mostly hidden** under the riddle. They read through the mesh, and with the riddle taken
   off.
4. **The riddle's cause is the player.** No hands are modelled: the hand riddle moves as held, and the stand's riddle
   as pushed by its grip.
5. **The motion stops where it is.** If the player lets go part way through a charge, θ stops and the riddle holds
   where it was in its swirl or swing. A renderer can ease θ to the next whole turn, where the stand's riddle hangs
   straight.
6. **Presence:** see "Presence" above. The charge's parts are right only at p 1.
7. **Linear ramps** for the heap and the fines, as the other hand stations' gauges.
8. **The stand's pins are square heads**, and the straps flat bars: nothing round turns.

## Open for the owner

1. **The stand form.** #715 asks for "a stand variant, worked by hand like the sieve", and this pass builds one: the
   same riddle hung on straps from an oak stand over the same box, one block like the hand riddle, swung by hand.
   The previous pass had dropped its stand (a 1 × 2 lever-tipped "tier 1" form), since tiers are gone. To settle:
   - **Keep both forms, or one?** If one, which. If both, are they two blocks, or one block whose stand is fitted to
     the hand riddle's box?
   - **What the stand gives in play over the hand riddle**: a bigger charge, a faster pace, or less hunger (#714).
   - **Is a second form needed at all?** "Stand variant" could also be read as the hand riddle itself: the riddle set
     on a stand (its box and bearers) and worked by hand.
2. **The stand's design.**
   - A to-and-fro swing only, 1.5 at the riddle (the straps 10.35 degrees), with no side to side.
   - Straps from posts in the middle of each side, braced, tied at the top ends; the grip on the north board.
   - The whole block is its selection box.
3. **The oversize.** It stays on the mesh at the end of a charge, to be taken out by hand (an empty-handed
   right-click, say), as the riddlings of a hand riddle were picked over and sent back to be broken again. Nothing
   tips it anywhere. These are #714's to decide:
   - what it gives: the crushed ore back, to spall again, or a separate oversize item;
   - whether a new charge may be riddled on top of oversize left in the riddle.
4. **The fines in the box** are drawn with the charge, so once the oversize is taken out the box looks empty again.
   If the box should show fines while it holds them, they need a `requires` of their own (`fines`), which gameplay
   would set.
5. **The charge sizes and the pace**: a part charge and a full one for k (three lumps and one layer; four and two),
   12 and 20 shakes, the same on the stand. Provisional, gameplay's to set.
6. **Sizes.**
   - The riddle: 11.5 across and 2 deep (vanilla's pan: 11 and 3), the mesh's openings 1.25 (four wires each way).
   - The box: 13 square and 4.4 tall.
   - The stand's timbers: 1.5 square.
7. **The charge's texture**: the code `ore`, defaulting to hematite's nugget texture as the pack's crushed ore wears
   it. The renderer sets it.
8. **Presence**: a charge on at p 1 at once and not drawn once taken out (above). The alternative is a renderer that
   eases p, which these gauges would draw wrongly.
9. **The operator's side**, north (the side the player placed it from). There is no infeed or output side: a hand
   station takes no automated input or output (the owner's ruling).
10. **The box's iron corners**, dropped in the previous pass: say if they should come back as something chunkier.

## Gameplay

Not built: #714. The rigs are written for it the way the other hand stations' are (the squaring shear's `Core/` and
`Game/` are the template). The renderer and block are to:

- read the anchors, `work`, `riddling.shakesPerCharge` and `requires`, never element names;
- take the block's collision and selection boxes from the rig's `cells`;
- set the texture code `ore`;
- draw the `chargesmall` or `chargefull` parts only while that charge is on the station, at p 1;
- turn θ while held, and give the renderer ψ, its travel.

The block's frame shape is each form's `_frame` shape.

- **Hand loading only.** The player puts the charge in, and takes the fines (at `fines.pos`) and the oversize (at
  `charge.pos`) out, by hand. The block must **refuse hopper and chute insertion and extraction**: no item-transfer
  hook, no inventory exposed to a hopper or chute on any face, and nothing pushed out to a neighbour. This holds for
  every hand station.
- **Tests.**
  - `tests/Riddle/RiddleRigTests.cs`: both shipped rigs through the shared parser, their vocabulary and work, and
    every pose of both reference files replayed through `Machines/Core`, the stretching gauges included.
  - `tools/tests/test_riddle_model.py` and `site/test/riddle.test.ts`: the generated files of both forms against
    their own rules and the site's rig maths.
