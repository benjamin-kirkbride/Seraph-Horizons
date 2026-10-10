# Riddle

Part of the Seraph Horizons mod (`../README.md`): the hand station of the classify stage of ore processing
(epic #684). A miner's **riddle** of the 1800s classifies crushed ore: the fines fall through its mesh and the
oversize is left on it. Classified feed loses the unclassified penalty (× 0.85) at the concentrator (#714),
and the riddle also classifies for the pulverizer and the sluice at the water tier, until the stamp mill's
discharge screen (#728) does it. It is a hand station only, one block: there is no stand and no other form of
it.

The riddle is square and shallow, after vanilla's pan (two stepped tiers of oak boards, 11.5 across at the top
and 2 deep), with a coarse woven mesh of iron wire. It rests by its north and south boards on two oak bearers
laid across the mouth of a plank box, which catches the fines. The player lifts it just off the bearers and
swirls it; the oversize stays in the riddle, to be taken out by hand.

**Period practice.** On the 1800s dressing floors (Cornwall, and the American camps that copied them) ore
broken by hand was riddled over a tub or a box: the riddle held in both hands, shaken and swirled, the fines
through into the tub. What stayed on the mesh, the riddlings, was picked over by hand and went back to be
broken again. The model follows that: the box is the tub, and the bearers are the two sticks a riddle was
rested on between charges. Simplified: the wire mesh is coarser and chunkier than a real one (it must read at
block scale), and its wires cross in two layers rather than going over and under.

This folder holds the model's generator and its rig (`tools/`). The gameplay is #714 and is not built yet;
the model is #715. "Gameplay" below says what the rig offers it.

**A hand station.** It has no mechanical power, no power cell, no oil and no pipe. The player works it by
holding right-click, as on the quern, the squaring shear and the press brake. The rig's θ is that clock, one
turn a shake; the shake is drawn from its travel ψ, and only while a charge is being riddled.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the generator; the generic
half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one (no new
driver: every motion is a `gauge`, the shake a gauge's `lobes`).

## The station

**Footprint.** 1 cell, the controller `[0,0,0]`, with no ghost.

**Orientation.** The operator stands on the native north side (`operatorSide`). Placed as the other hand
stations are (`side` = the way the player looks), that is the side the player placed it from. The station is
symmetric but for the bearers, which run west to east, across the operator's view.

**Anchors** (in `assets/seraphhorizons/config/riddle-rig.json`): `operatorSide` (`north`), `infeedSide`
(`east`: a container of crushed ore beside it), `outputSide` (`south`: where the fines would go out),
`output.pos` (on the fines heaped in the box at W 1) and `charge.pos` (the middle of the charge in the riddle:
the riddling sound and dust, and where the oversize is taken from).

**Fitted parts** (the rig's `requires`). The frame (`requires` null) is what the block draws: the box and the
bearers.

| `requires` | Draws |
|---|---|
| `riddle` | the riddle: two tiers of oak boards and the woven mesh of iron wires |
| `chargesmall` | the work: a part charge (k 1), its oversize (a bed and two lumps), its one layer of fines and those fines in the box |
| `chargefull` | the work: a full charge (k 2), its oversize, its two layers of fines and those fines in the box |

There are no levels: the riddle is a hand station, the same at every tier it serves. The viewer shows
`chargesmall` only with a part charge chosen and `chargefull` only with a full one (`requiresClass`).

## Model

Everything in the model was made for this mod: no other mod's model is used (vanilla's pan was read for its
proportions only), so the generator needs nothing from `build/mods`. The wood is the game's debarked oak
(`game:block/wood/debarked/oak`) and its oak planks (`game:block/wood/planks/oak1`, the box), the iron
`game:block/metal/plate/iron`, and the charge has the texture code `ore` (`game:block/stone/gravel/granite`
in the shape), which the renderer is to set to the crushed ore's texture. The site's viewer draws `ore` in an
ore colour of its own (`site/src/lib/model-view.ts`), apart from the iron mesh under it. `tools/make_shape.py`
writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/riddle.json` | The whole station, every moving part and both charges (35 elements). |
| `assets/seraphhorizons/shapes/block/riddle_frame.json` | Its static frame only, the box and the bearers (7 elements). |
| `assets/seraphhorizons/config/riddle-rig.json` | The cell, the anchors, the work, the pace and the part rig (10 parts). |
| `tests/Riddle/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z
north to south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame
divided by 16. The station's middle is (8, 8).

**The box**: plank walls 1 thick and 4.4 tall, 13 square outside (the north and south walls across, the east
and west between them), and a bottom 1 thick on the ground inside them.

**The bearers**: two oak bars, 1 square, laid across the box's mouth on its walls (y 4.4 to 5.4), from x 0.9
to 15.1 (their ends past the walls), each under one of the riddle's lower north and south boards (z 3 to 4 and
12 to 13), so each carries a whole board.

**The riddle** (its underside at y 5.4, on the bearers): a lower tier of oak boards 1 thick and 1 deep, 10
across outside (an 8 × 8 opening), and an upper tier stepped out to 11.5 across, so it is 2 deep (vanilla's
pan steps 7, 9 and 11, 3 deep). The north and south boards run across, the east and west fit between them. The
mesh: four iron wires 0.75 square along x, 2 apart (openings of 1.25), their underside 0.15 over the riddle's,
and over them four along z, crimped 0.35 into them where they cross; every wire's ends are let 0.4 into the
lower tier's boards. The mesh's top, the charge's bed, is 1.3 over the riddle's underside.

**The charge** rides the riddle: the **oversize**, a bed on the mesh (5 square, 0.8 thick) with two lumps
standing out of it (1.6 and 1.35 across, 1.3 and 1.1 tall, turned 25 and −35 degrees, their feet 0.3 in the
bed), and over the bed one layer of fines (a part charge: 4.4 square, 0.55) or two (a full charge: 4.6 square,
0.55, and a top 2.8 square, 0.4, heaped 1.05 over the rim). The lumps show through the fines from the start.
As the charge is riddled the fines sink, the top into the layer under it and that into the bed, each hidden
inside the one under it by at least 0.075 (no driver scales a part, so a layer cannot shrink: it sinks out of
sight), and **the fines in the box** rise out of its bottom, where they were hidden, layer by layer onto it.
At W 1 the fines lie in the box, 1.35 deep, and the oversize, the bed and its lumps, lies on the mesh.

**The shake.** While a charge is riddled the player lifts the riddle 0.5 off the bearers and swirls it: turns
about axes 3 blocks above it (so it moves nearly level, tilting about 2 degrees as hands do) carry its middle
1.5 to and fro (z) and 0.75 side to side (x), a quarter turn apart, so it goes round an ellipse once a turn of
the clock. It clears the bearers by at least 0.166, and stays inside the block.

**The cycle** (t = W, one charge; θ, the clock, poses the shake, and only inside its window):

| t | |
|---|---|
| 0.00..0.04 | The charge on the mesh as loaded, the box's fines hidden in its bottom. |
| 0.04..0.08 | The riddle is lifted off the bearers and the shake eases in. |
| 0.10..0.38 | A full charge's top sinks into its lower layer; its first fines rise onto the box's bottom. |
| 0.42..0.76 | Its lower layer sinks into the bed; the second fines rise onto the first. (A part charge: its one layer sinks over 0.10..0.76.) |
| 0.80..0.84 | The shake eases out and the riddle is set down. |
| 0.84..1.00 | Still. Delivered at 1: the fines in the box, the oversize in the riddle. |

Every moving part (W the riddling, k the charge, p its presence, ψ the clock's travel; "shake" is a gauge of
amount 0 with `lobes` {ratio 1, amplitude A}, in the shaking window, so it turns by e × A × cos(ψ + phase), e
its engagement):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Riddle: boards, mesh (`riddle`) [riddle] | The player's hands | The charge on it | `gauge` slide y +0.5 (the lift); shake about x and shake about z, both about a point 3 blocks over the middle |
| Oversize: bed and lumps (`c1base`, `c2base`) [charge…] | Rides the riddle | — | none |
| Fines in the riddle (`c1top`; `c2mid`, `c2top` riding it) [charge…] | Ride the bed; the shake | — | `gauge` slide y down into the layer under it |
| Fines in the box (`c1fines`; `c2fines1`, `c2fines2` riding it) [charge…] | Falling through the mesh | — | `gauge` slide y up out of the box's bottom |
| Frame: box, bearers (`frame`) | Static | — | none |

There are no gears, pins or shafts.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 2 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_riddle.py` holds the checks; every run checks its output and exits non-zero if one
fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part.
- **Readable:** at most 36 elements, and no part of the riddle or the frame thinner than 0.75 (the charge's
  layers are steps of a heap and are left out).
- **Frame:** joined to the ground as one piece.
- **Textures** by role: oak boards and bearers; planks for the box; iron wires; `ore` for the charge.
- **Mesh:** the wires along z over those along x, crimped into them; every wire's ends in the lower tier's
  boards; the openings 1.25.
- **Containment:** nothing leaves the block over the cycle of both charges and the shake's phase.
- **Charge:** as loaded at W 0, the box's fines hidden in its bottom (the second layer in the first); the bed
  moving with the riddle throughout; at W 1 every layer of fines sunk inside the bed (a full charge's top
  first inside its lower layer), measured in the bed's frame, the fines on the box's bottom and the second
  layer on the first, and the oversize on the mesh with the riddle set down. Every hidden layer is inside what
  hides it by at least `HIDE_MARGIN` (0.05). The lumps stand on the bed, their feet in it, and out of it by
  0.75 or more.
- **Shake:** the clock moves nothing at W 0, 0.02, 0.85, 0.9 and 1 (nor with no charge); in the window the
  riddle goes 1.5 to and fro and 0.75 side to side.
- **Bearers:** on the box's walls, each under a whole board of the riddle; the riddle rests on both and clears
  them by 0.05 or more while shaken.
- **Clearances:** over 249 poses (W against the shake's phase, both charges) no two parts touch except the
  intended contacts in `ALLOWED` (the riddle on the bearers, the bed on the mesh, the fines in and on the box's
  bottom, each layer sinking into the one under it); **swept paths** (full runs) the same every 0.0025 of the
  cycle at the pace.
- **No z-fighting** (full runs): no coplanar overlapping faces at ten poses of what can be seen (the other
  charge's parts left out, `shown`). There are none before the fix; it only removes the 16 faces pressed
  against their own part.
- **Files:** every texture declared; the one cell, boxed inside itself and lidded; no power cell; the shipped
  model is the checked one (no move: the build frame's corner is the controller's).

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the other
machines' are: one box over the box, the riddle and a full charge's heap, with a lid at its top.

### Rig schema (`riddle-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") as the squaring shear uses it, with nothing
added to the shared rig maths: a `work` quantity and gauges, the shake a gauge's `lobes` (the rosser's
scraper arms are the other user of them). Unlike the other hand stations, no driver reads θ itself (there is
no ratio-0 rotate): the lobes read its travel ψ, which is enough for the viewer to offer the clock and Play.

- **The work.** `{ "name": "riddling", "unit": "charges", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one charge's riddling, 0..1.
- **Inputs.** W, k and p, and ψ. k is the charge (1 a part charge, 2 a full charge, 0 none): the renderer is
  to choose it by how much is in the riddle, as the mill shows two trunk sizes. p is its presence, eased in as
  it goes on and out as the oversize is taken away, k held while p eases out. ψ is the clock's travel:
  gameplay is to turn θ while the player holds right-click and advance W by 1 / `shakesPerCharge[k]` a turn.
  The shake's engagement is p times its window's, so it eases in and out with the riddling and stops with no
  charge.
- **`riddling`**: `shakesPerCharge` (12 a part charge, 20 a full one), the pace. Provisional: #714 sets it.
- **Keys.** `cells`, `operatorSide`, `infeedSide`, `outputSide`, `output`, `charge`, `work`, `riddling` and
  `parts`. `requires` values: `riddle`, `chargesmall`, `chargefull` or null. The texture code the renderer
  sets: `ore`.

**In the viewer** (`site/models.json`, `riddle`; a standalone copy with `site/scripts/standalone-viewer.ts`):
the clock is the θ slider (its travel shown under it); the riddling's slider reads in charges; the charge
select names the sizes ("Part charge", "Full charge"), and each size's charge and fines show only with it.
Play moves W with θ at a part charge's pace (`play.turnsPerWork` `"riddling.shakesPerCharge.thin"`).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `riddle_*` (`riddle_lower_*`,
`riddle_upper_*`, `riddle_wirex_*`, `riddle_wirez_*`), the charge's `c1base_*` and `c2base_*` (`_bed`,
`_lump1`, `_lump2`), `c1top_*`, `c2mid_*`, `c2top_*`, `c1fines_*`, `c2fines1_*`, `c2fines2_*`, and `fr_*`
for the frame (`fr_box_wall_*`, `fr_box_bottom`, `fr_bearer_*`). Hand edits are lost when the script runs
again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (WebGL) at every charge and through the cycle, and in
projections. No one has yet looked at it in a client.

1. **The fines sink rather than fall.** No driver scales a part, so the heap cannot shrink: each layer of
   fines sinks into the one under it, and the fines in the box rise out of its bottom. Nothing is drawn
   falling through the mesh; gameplay's dust particles under the riddle are to carry that.
2. **The oversize is a bed with two lumps.** The bed must be thicker than any layer hidden in it, so it is a
   bed of oversize, not a few loose lumps; the lumps on it are what read as rough ore.
3. **The fines in the box are mostly hidden** under the riddle; they read through the mesh, past the bed, and
   with the riddle taken off.
4. **The riddle's cause is the player.** No hands are modelled: the riddle moves as held.
5. **The shake stops where it is.** If the player lets go part way through a charge, θ stops and the riddle
   holds where it was in its swirl; a renderer can ease θ to the next whole turn.
6. **Linear ramps** for the fines, as the other hand stations' gauges.

## Open for the owner

1. **The oversize.** It stays in the riddle at the end of a charge, to be taken out by hand (an empty-handed
   right-click, say), as the riddlings of a hand riddle were picked over and sent back to be broken again;
   nothing tips it anywhere. What it gives (the crushed ore back, to spall again, or a separate oversize item),
   and whether a charge may be riddled on top of oversize left in the riddle, are #714's.
2. **The fines in the box** are drawn with the charge: once the oversize is taken out (the charge's parts no
   longer drawn) the box looks empty again. If the box should show fines while it holds them, they need a
   `requires` of their own (`fines`), which gameplay would set.
3. **The charge sizes and the pace**: a part charge and a full one for k, 12 and 20 shakes. Provisional,
   gameplay's to set.
4. **Sizes.** The riddle 11.5 across and 2 deep (vanilla's pan: 11 and 3), the box 13 square and 4.4 tall, the
   mesh's openings 1.25 (four wires each way).
5. **Sides.** Operator north, infeed east, fines out south: placeholders, as on the amalgam pan.
6. **What this pass changed from the previous one**: the stand (the second, lever-tipped form) and everything
   for it are gone; the mesh is four chunky wires each way in place of eight thin ones; the box's thin iron
   corner plates are gone and its walls are 1 thick; the bearers are 1 square and lie under the riddle's north
   and south boards, a whole board each; the oversize has lumps; the swirl is a little larger (1.5 and 0.75,
   lifted 0.5). Say if the iron corners should come back as something chunkier.

## Gameplay

Not built: #714. The rig is written for it the way the other hand stations' are (the squaring shear's `Core/`
and `Game/` are the template): read the anchors, `work`, `riddling.shakesPerCharge` and `requires`, never
element names; take the block's collision and selection boxes from the rig's `cells`; set the texture code
`ore`; draw the `chargesmall`/`chargefull` parts only while that charge is on the station; turn θ while held
and give the renderer ψ, its travel.

- **Tests.** `tests/Riddle/RiddleRigTests.cs` (the shipped rig through the shared parser, its vocabulary and
  work, and every pose of `tests/Riddle/rig-reference.json` replayed through `Machines/Core`),
  `tools/tests/test_riddle_model.py` and `site/test/riddle.test.ts` (the generated files against their own
  rules and the site's rig maths).
