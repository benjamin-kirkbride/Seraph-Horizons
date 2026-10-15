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

The owner's rulings on #714 that the model follows:

- **Hand riddle only** (2026-10-13): the stand form is dropped. An earlier pass modelled the riddle hung on straps
  from an oak stand; it is gone, with its files.
- **The oversize is rock again** (2026-10-13): it goes back to spalling (#747) to be broken smaller.
- **A charge is 3 lumps, riddled in 12 shakes** (2026-10-14, starting values): the model's one charge, three lumps of
  oversize under one layer of fines.
- **The box is emptied before the next charge** (2026-10-14): the player takes the fines out, and gameplay refuses a
  charge while the box holds fines.
- **The oversize wears its host rock's texture** (2026-10-14), not the ore's: the texture code `rock`.
- **No rim caps** over the box's corners (2026-10-14): the corner straps alone.

The riddle is square and shallow, after vanilla's pan (two stepped tiers of oak boards, 11.5 across at the top and 2
deep), with a coarse woven mesh of iron wire. It rests by its north and south boards on two oak bearers laid across
the mouth of a plank box that catches the fines, the box's corners bound with nailed angle-iron straps. The player
lifts it just off the bearers and swirls it, to and fro and side to side. The oversize stays in the riddle, to be
taken out by hand; the fines stay in the box until the player takes them out.

**Period practice.** On the 1800s dressing floors of Cornwall, and in the American camps that copied them, ore broken
by hand was riddled over a tub or a box. The riddle was held in both hands, shaken and swirled, and the fines went
through into the tub. What stayed on the mesh, the riddlings, was picked over by hand and went back to be broken
again. The model follows that: the box is the tub, iron-bound at the corners as plank boxes and tubs were, and the
bearers are the two sticks a riddle rested on between charges. Simplified: the wire mesh is coarser and chunkier than
a real one (it must read at block scale), and its wires cross in two layers rather than going over and under.

This folder holds the model's generator and its rig (`tools/`). The gameplay is #714 and is not built yet; the model
is #715. "Gameplay" below says what the rig offers it.

**A hand station.** No mechanical power, power cell, oil or pipe. The player works it by holding right-click, as on
the quern, the squaring shear and the press brake. The rig's θ is that clock, one turn a shake. The motion is drawn
from its travel ψ, and only while a charge is being riddled.

**No automated input or output.** A hand station is loaded and unloaded by the player, by hand (the owner's ruling for
every hand station, #684): no chute, hopper or other item transfer puts crushed ore in or takes fines or oversize out.
So the rig has no `infeedSide` or `outputSide`, or any other side, face, cell or point for one. The only side is
`operatorSide`, where the operator stands. The generator fails if one appears.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the generator. The generic half of
it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one. No new driver: every
motion is a `gauge`, the shake a gauge's `lobes`, the fines stretching gauges.

## The station

**Footprint.** 1 cell, the controller `[0,0,0]`, with no ghost.

**Orientation.** The operator stands on the native north side (`operatorSide`). Placed as the other hand stations are
(`side` = the way the player looks), that is the side the player placed it from. The riddle is symmetric but for its
bearers, which run west to east, across the operator's view.

**Anchors** (`assets/seraphhorizons/config/riddle-rig.json`):

| Key | Where | For |
|---|---|---|
| `operatorSide` | `north` | Where the player stands to work it. |
| `fines.pos` | `[0.5, 0.1469, 0.5]` | On the fines in the box, where the player takes them out. |
| `charge.pos` | `[0.5, 0.4469, 0.5]` | The middle of the charge in the riddle: the riddling sound and dust, and where the oversize is taken out. |

There is no infeed side and no output side: see "No automated input or output" above.

**Fitted parts** (the rig's `requires`). The frame (`requires` null) is what the block draws.

| `requires` | Draws |
|---|---|
| (frame) | The box (plank walls and bottom), its four corner straps with their nails, and the two oak bearers across its mouth. |
| `riddle` | The riddle: two tiers of oak boards and the woven mesh of iron wires. |
| `charge` | The work, a charge (k 1): three lumps of oversize (rock) and one layer of fines over them, on the mesh. |
| `fines` | The box's contents: the fines on its bottom, a layer and a mound on it. |

**Contents** (the rig's `contents`, as the jig's): `charge` while a charge is on the riddle, and `fines` while the box
holds fines. The viewer has a "The box" select, Empty or Fines in the box, and shows the charge only with a charge
chosen (`requiresClass` `charge`: `thin`). There are no levels. These ids are a sensible split for build stages, which
are worked out with the owner later.

## Model

Everything in the model was made for this mod: no other mod's model is used (vanilla's pan was read for its
proportions only), so the generator needs nothing from `build/mods`. The materials are all the game's:

- the wood is debarked oak (`game:block/wood/debarked/oak`), with oak planks (`game:block/wood/planks/oak1`) for the
  box;
- the iron (wires, corner straps, nails) is `game:block/metal/plate/iron`;
- the layer of fines on the mesh and the fines in the box have the texture code `ore`. Its default is hematite's
  nugget texture (`game:item/resource/nugget/hematite`), because the pack's crushed ore wears its ore's nugget texture
  (`itemtypes/oreprocessing/crushedore.json`). The renderer is to set `ore` to the riddled ore's texture;
- the oversize, rock going back to spalling, has the texture code `rock`, defaulting to granite
  (`game:block/stone/rock/granite1`). The renderer is to set it to the host rock's texture.

The viewer's entry has a texture select for each: "Crushed ore" with seven ores, and "Oversize (host rock)" with nine
rocks (the game's `block/stone/rock/<rock>1`); their colours are each texture's mean, from the game's files. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/riddle.json` | The riddle, every moving part, the charge and the fines (53 elements). |
| `assets/seraphhorizons/shapes/block/riddle_frame.json` | Its static frame only: the box, its straps and the bearers (31 elements). |
| `assets/seraphhorizons/config/riddle-rig.json` | Its cell, anchors, contents, work, pace and part rig (5 parts). |
| `tests/Riddle/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north to
south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame divided by 16.
The station's middle is (8, 8).

**The box**: plank walls 1 thick and 4.4 tall, 13 square outside (the north and south walls across, the east and west
between them), and a bottom 1 thick on the ground inside them.

**The corner straps**: an angle-iron strap down each of the box's four vertical corners, 0.3 thick, from 0.3 off the
ground to 0.3 under the rim. Each is an L of two legs, one flat on each of the corner's outer faces, the north or
south leg lapping the other's edge so they meet along the corner. Each leg is 1.8 wide from the corner, past the end
of the wall it laps (1 thick) onto the other wall, so it binds both. Each leg has two nail heads (0.5 square, 0.12
proud), 0.9 and 3.5 up and 1.3 from the corner: the north and south legs' into the end walls' faces, the west and east
legs' past the end walls, into the side walls. The straps stay under the rim, 0.3 under the bearers, and 1.38 or more
from the riddle at any point of its swirl.

**The riddle** (its underside at y 5.4): a lower tier of oak boards 1 thick and 1 deep, 10 across outside (an 8 × 8
opening), and an upper tier stepped out to 11.5 across, so it is 2 deep (vanilla's pan steps 7, 9 and 11, and is 3
deep). The north and south boards run across, and the east and west fit between them.

The mesh: four iron wires 0.75 square along x, 2 apart (openings of 1.25), their underside 0.15 over the riddle's.
Over them lie four along z, crimped 0.35 into them where they cross, and every wire's ends are let 0.4 into the lower
tier's boards. The mesh's top, the charge's bed, is 1.3 over the riddle's underside.

**The bearers**: two oak bars, 1 square, laid across the box's mouth on its walls (y 4.4 to 5.4), from x 0.9 to 15.1
(their ends past the walls). Each lies under one of the riddle's lower north and south boards (z 3 to 4 and 12 to 13),
so each carries a whole board.

**The charge** rides the riddle:

- The **oversize**: three lumps of rock (`rock`), 1.2 to 1.5 across and 1.3 to 1.5 tall, turned about y. Each stands on the mesh's top
  over a wire along z, inside the riddle's top tier.
- The **layer of fines** over them: 6.8 square, 0.9 over the mesh. Its underside is settled 0.15 into the mesh,
  between the wires' tops and bottoms. The lumps stand 0.3 or more out of it.

**The fines in the box** (`fines`): a layer 9.2 square and 0.75 deep on the box's bottom and a mound 6 square and 0.6
deep on it, 1.35 deep in all, under the mesh.

As a charge is riddled its layer **shrinks** onto its underside and is gone, and the fines in the box **grow** from the
box's bottom, from nothing (stretching gauges along y, `docs/recipe-browser/models.md`, "Stretching gauges"). At W 1
the layer is gone, the fines lie in the box and the lumps, the oversize, lie on the bare mesh. With no charge on (k 0)
the fines' gauge does nothing, so the box's fines, when fitted, are drawn whole: the box keeps them after the oversize
is taken out.

**The shake.** While a charge is riddled the player lifts the riddle 0.5 off the bearers and swirls it. Turns about
axes 3 blocks above it (so it moves nearly level, tilting about 2 degrees as hands do) carry its middle 1.5 to and fro
(z) and 0.75 side to side (x), a quarter turn apart, so it goes round an ellipse once a turn of the clock. It clears
the bearers by at least 0.158, and stays inside the block.

**The cycle** (t = W, one charge; θ, the clock, poses the shake, and only inside its window):

| t | |
|---|---|
| 0.00..0.04 | The charge on the mesh as loaded; nothing in the box. |
| 0.04..0.08 | The shake eases in; the riddle is lifted off its bearers. |
| 0.10..0.76 | The layer of fines shrinks into the mesh; the fines grow in the box. |
| 0.80..0.84 | The shake eases out; the riddle is set down. |
| 0.84..1.00 | Still. Delivered at 1: the fines in the box, the oversize on the mesh. |

Every moving part, in the terms the rig uses:

- W is the riddling, k the charge, p its presence and ψ the clock's travel.
- A "shake" is a gauge of amount 0 with `lobes` {ratio 1, amplitude A} in the shaking window. It turns by
  e × A × cos(ψ + phase), where e is its engagement.
- "Shrinks" and "grows" are stretching gauges along y, with amount −1 for the charge's class.

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Riddle: boards, mesh (`riddle`) [riddle] | The player's hands | The charge on it | `gauge` slide y +0.5 (the lift); a shake about x and a shake about z, both about a point 3 blocks over the middle |
| Oversize: lumps (`oversize`) [charge] | Rides the riddle | — | none |
| Fines in the riddle (`heap`) [charge] | Rides the riddle; the shake | — | shrinks onto its underside |
| Fines in the box (`fines`) [fines] | Falling through the mesh | — | grows from the box's bottom; whole with no charge on |
| Frame (`frame`) | Static | — | none |

There are no gears, shafts or joints.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 4 s and is deterministic (two runs into two folders are byte-identical, logs included).
`tools/validate_riddle.py` holds the checks. Every run checks the output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Readable:** at most 56 elements, and no part of the riddle or the frame thinner than 0.75 (the charge's lumps and
  layer and the fines are a heap, and the corner straps and their nails iron: they are left out).
- **Frame:** joined to the ground as one piece.
- **Textures** by role: oak boards and bearers; planks for the box; iron wires, corner straps and nails; `rock` for the
  oversize; `ore` for the layer of fines and the fines in the box.
- **Mesh:** the wires along z over those along x, crimped into them; every wire's ends in the lower tier's boards; the
  openings 1.25.
- **Containment:** nothing leaves the block over the cycle and the shake's phase.
- **Charge**, through one riddling:
  - three lumps (#714);
  - as loaded at W 0: the layer whole, nothing in the box;
  - the lumps riding the riddle;
  - the layer shrinking onto its own underside in the riddle's frame, never growing, and gone by the end of its
    window;
  - the fines in the box growing from its bottom, never shrinking, from nothing to whole over their window;
  - with no charge on, the fines whole where they were built, at any W and clock;
  - the fines inside the box's walls and under its rim;
  - at W 1: the riddle at rest, the lumps on the mesh, each standing on a wire along z inside the riddle's top tier
    and 0.25 or more out of the layer, and no two lumps overlapping.
- **Clock:** it moves nothing outside the shaking window, nor with no charge.
- **Shake:** in the window the riddle goes 1.5 to and fro and 0.75 side to side.
- **Bearers:** on the box's walls, each under a whole board of the riddle; the riddle rests on both, and clears them by
  0.05 or more while shaken.
- **Corner straps**, every corner of the box (found from its walls):
  - an L of two legs, one flat on each outer face, meeting along the corner and wrapping it;
  - each leg reaching past the end of the wall it laps onto the other wall, and running most of the box's height,
    under its rim;
  - each leg nailed with two heads or more, the west and east legs' nails over the side walls and the north and south
    legs' over the end walls, so both walls of the corner are nailed;
  - 0.25 or more from the bearers, and from the riddle and its charge over the whole swept path (the grid of poses and
    the cycle at the pace).
- **Clearances:** over 249 poses (W against the shake's phase) no two parts touch, except the intended contacts in
  `ALLOWED`:
  - the riddle on its bearers;
  - the lumps on the mesh, and the layer settled into it with the lumps standing in it;
  - the fines on the box's bottom.

  The **swept paths** (full runs) check the same every 0.0025 of the cycle, at the pace.
- **No z-fighting** (full runs): no coplanar overlapping faces at nine poses of what can be seen (the charge left out
  with none on, `shown`). There are none before the fix. It only removes the faces pressed against their own part
  (37).
- **Files:**
  - every texture declared;
  - the one cell, boxed inside itself and lidded;
  - no power cell;
  - no infeed or output side, face, cell or point, only `operatorSide` (no automated input or output);
  - the contents are the charge and the fines, each a `requires` of the rig;
  - a charge is three lumps in twelve shakes, and the work has the one class;
  - the shipped model is the checked one (no move: the build frame's corner is the controller's).

The cell's box is rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the other machines'
are: one box over the box with its straps, the riddle and the charge, lidded at its top (0.5125).

### Rig schema (`riddle-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema"), as the squaring shear uses it, with nothing added
to the shared rig maths:

- a `work` quantity and gauges;
- the shake is a gauge's `lobes` (the rosser's scraper arms are the other user of them);
- the fines are stretching gauges (the mandrel station's spring is the other user of them).

Unlike the other hand stations, no driver reads θ itself (there is no ratio-0 rotate): the lobes read its travel ψ,
which is enough for the viewer to offer the clock and Play.

- **The work.** `{ "name": "riddling", "unit": "charges", "step": 0.005, "end": { "thin": 1 } }`. W is one charge's
  riddling, 0..1. There is one class: a charge.
- **Inputs.** W, k and p, and ψ.
  - k is the charge: 1 while one is on the riddle, 0 none.
  - p is its presence: 1 while a charge is on (below).
  - ψ is the clock's travel. Gameplay is to turn θ while the player holds right-click, and advance W by
    1 / `shakesPerCharge` a turn. The shake's engagement is p times its window's, so it eases in and out with the
    riddling and stops with no charge.
- **Presence.** Every gauge eases with p, so a charge's parts are right only at p 1:
  - at W 0, with p under 1, the box's fines would show part grown;
  - at W 1, as p eases out, the layer would grow back on the mesh, and the box's fines are whole either way.

  So gameplay puts a charge on at p 1 at once, and stops drawing it (k 0) when the oversize is taken out, as the
  mandrel station stops drawing its work at delivery.
- **`contents`**: `{ "charge": "charge", "fines": "fines" }`, the `requires` values for what it holds (the jig's key).
  `charge` is fitted while a charge is on; `fines` while the box holds fines, from a charge's loading until the player
  takes them out. While a charge is riddled the fines grow from nothing as it goes, so the box must be empty when a
  charge goes on: the player empties it first, and gameplay refuses a charge while the box holds fines (the owner's
  ruling).
- **`riddling`**: `lumpsPerCharge` 3 and `shakesPerCharge` `{ "thin": 12 }`, #714's starting values.
- **Keys.** `cells`, `operatorSide`, `fines`, `charge`, `contents`, `work`, `riddling` and `parts`; no infeed or
  output side, face or cell (a hand station takes no automated input or output).
  - `requires` values: `riddle`, `charge`, `fines`, or null.
  - The texture codes the renderer sets: `ore` (the fines) and `rock` (the oversize).

**In the viewer** (`site/models.json`: `riddle`; a standalone copy with `site/scripts/standalone-viewer.ts`):

- the clock is the θ slider, with its travel shown under it, and the riddling's slider reads in charges;
- the charge select is None or A charge (the site's manifest check now lets a rig with one class name only none and
  it); the charge shows only with it;
- "The box" select is Empty or Fines in the box, opening with the fines;
- Play moves W with θ at the pace (`play.turnsPerWork` `"riddling.shakesPerCharge.thin"`), and starts at 0.75 s a turn;
- coloured by texture, the "Crushed ore" select sets the fines' colour and the "Oversize (host rock)" select the
  lumps'.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order):

- `riddle_*`: `riddle_lower_*`, `riddle_upper_*`, `riddle_wirex_*`, `riddle_wirez_*`;
- the charge: `oversize_lump1`..`3`, `heap_1`;
- the box's fines: `fines_1`, `fines_2`;
- `fr_*` for the frame: `fr_box_wall_*`, `fr_box_bottom`, `fr_strap_<corner>_x`/`_z` and their `_xnail*`/`_znail*`,
  `fr_bearer_*`.

Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. It has not been looked at in the site's
viewer in a browser, or in a client.

1. **The fines shrink and grow; nothing falls.** Nothing is drawn falling through the mesh. Gameplay's dust particles
   under the riddle are to carry that.
2. **The layer shrinks flat**, not as a heap settling round the lumps.
3. **The fines in the box are mostly hidden** under the riddle. They read through the mesh, and with the riddle taken
   off.
4. **The riddle's cause is the player.** No hands are modelled: the riddle moves as held.
5. **The motion stops where it is.** If the player lets go part way through a charge, θ stops and the riddle holds
   where it was in its swirl. A renderer can ease θ to the next whole turn.
6. **Presence:** see "Presence" above. The charge's parts are right only at p 1.
7. **Linear ramps** for the layer and the fines, as the other hand stations' gauges.

## Open for the owner

1. **The oversize** stays on the mesh at the end of a charge, to be taken out by hand (an empty-handed right-click,
   say), and goes back to spalling (#714). Whether a new charge may be riddled on top of oversize left in the riddle
   is #714's.
2. **The charge** is #714's starting values: three lumps in one layer, twelve shakes. A different count changes
   `LUMPS` and `SHAKES`, and the generator's files check holds them to #714's.
3. **Sizes.**
   - The riddle: 11.5 across and 2 deep (vanilla's pan: 11 and 3), the mesh's openings 1.25 (four wires each way).
   - The box: 13 square and 4.4 tall; its corner straps 0.3 thick, each leg 1.8 wide, two nails a leg.
   - The fines in the box: 1.35 deep.
4. **Presence**: a charge on at p 1 at once and not drawn once taken out (above). The alternative is a renderer that
   eases p, which these gauges would draw wrongly.
5. **The operator's side**, north (the side the player placed it from). There is no infeed or output side: a hand
   station takes no automated input or output (the owner's ruling).

## Gameplay

Not built: #714. The rig is written for it the way the other hand stations' are (the squaring shear's `Core/` and
`Game/` are the template). The renderer and block are to:

- read the anchors, `contents`, `work`, `riddling` and `requires`, never element names;
- take the block's collision and selection boxes from the rig's `cells`;
- set the texture codes `ore` (the riddled ore's) and `rock` (the oversize's host rock);
- refuse a charge while the box holds fines;
- draw the `charge` parts only while a charge is on the station, at p 1, and the `fines` parts while the box holds
  fines;
- turn θ while held, and give the renderer ψ, its travel.
