# Riddle

Part of the Seraph Horizons mod (`../README.md`): the classify stage of ore processing (epic #684), at the
hand tier and tier 1. A miner's **riddle** of the 1800s, a round sieve with a bent-wood rim and a coarse
woven mesh of iron wire, classifies crushed ore: the fines fall through and the oversize is left on the
mesh. Classified feed loses the unclassified penalty (× 0.85) at the concentrator (#714). Two models share
the riddle, element for element:

- **the hand riddle** (`riddle`), one block: the riddle laid on two oak bearers across a cooper's low tub
  that catches the fines. The player shakes it;
- **the riddle on its stand** (`riddlestand`), tier 1, one block: the same tub under a light oak stand, the
  same riddle hung from the stand's side rails by two iron hangers, and a hand lever that swings it to and
  fro through an iron link, so a full charge (a stack, #714) is riddled without being held.

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
writes both models, as they will be one feature with one switch. The riddle is built once
(`build_riddle`) and placed in each model, and the generator fails unless the stand's riddle is the hand
riddle's, every element the same box raised 1.3 voxels (`check_same_riddle`), so "visibly the same part" is
held by a check, not by care. Two folders would have had two copies of the riddle to keep in step, or one
generator importing the other's.

## The models

**Footprint.** Each is 1 cell, x 0, y 0, z 0, the controller `[0,0,0]`: the block the player clicks. The
operator stands to the native north (the block's `side`, as the other hand stations are placed, would be the
way the player looks); a container of crushed ore can stand to the east (`infeedSide`), and the fines go out
to the south (`outputSide`). Why 1 × 1: both are a tub with a riddle over it, and the stand's lever and link
fit beside the tub inside the same block (below).

**Anchors** (`assets/seraphhorizons/config/riddle-rig.json`, `riddlestand-rig.json`): `infeedSide` east,
`outputSide` south, `output.pos` (on the fines heaped in the tub at W 1: where gameplay takes them from) and
`charge.pos` (the middle of the charge in the riddle: the riddling sound, dust falling). No `powerCell`.

**Fitted parts** (the rigs' `requires`). The frame (`requires` null) is what the block draws.

| Model | `requires` | Draws |
|---|---|---|
| hand | (frame) | the tub (16 oak staves, a bottom set in above the chime, two iron hoops) and the two oak bearers across its mouth |
| hand | `riddle` | the riddle: the bent-wood rim, the iron band inside it, the woven mesh of iron wires |
| stand | (frame) | the oak stand (four legs, top rails all round, low stretchers all round), the iron plates and pins the hangers and the lever turn on |
| stand | `tub` | the tub, as the hand riddle's |
| stand | `riddle` | the riddle, the hand riddle's |
| stand | `hangers` | the two iron hangers, the riddle's iron trunnion lugs and pins, the link's pin on the west hanger |
| stand | `lever` | the hand lever (oak, an iron knuckle at its pivot, the pin the link hangs on) and the iron link |
| both | `chargesmall` | the work: a part charge (k 1), its oversize bed, its one layer of fines and those fines in the tub |
| both | `chargefull` | the work: a full charge (k 2), its bed, its two layers of fines and those fines in the tub |

These are a sensible split for the build stages, which are worked out with the owner later; nothing here is a
recipe or an item. The viewer shows `chargesmall` only with a part charge chosen and `chargefull` only with a
full one (`requiresClass`).

## Model

Everything in the models was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. The wood is the game's debarked oak (`game:block/wood/debarked/oak`) and its oak planks
(`game:block/wood/planks/oak1`, the tub), the iron `game:block/metal/plate/iron`, and the charge has the
texture code `ore` (`game:block/stone/gravel/granite` in the shape), which the renderer is to set to the
crushed ore's texture. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/riddle.json` | The hand riddle, every part (134 elements). |
| `assets/seraphhorizons/shapes/block/riddle_frame.json` | Its static frame only, the tub and the bearers (54 elements). |
| `assets/seraphhorizons/shapes/block/riddlestand.json` | The riddle on its stand, every part (169 elements). |
| `assets/seraphhorizons/shapes/block/riddlestand_frame.json` | The stand's static frame only (21 elements). |
| `assets/seraphhorizons/config/riddle-rig.json` | The hand riddle's cell, anchors, work, pace and part rig (10 parts). |
| `assets/seraphhorizons/config/riddlestand-rig.json` | The stand's (15 parts). |
| `tests/Riddle/rig-reference.json` | Every hand riddle part's matrix at a grid of poses, from the reference maths. |
| `tests/Riddle/stand-rig-reference.json` | The stand's. |

### How it works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z
north to south) in voxels from the cell's north-west bottom corner, so the shipped files are the build frame
divided by 16.

**The tub** (both models): 16 oak staves round the axis x 8, z 8, their outer faces 5.75 from it, 0.7
thick, 5.5 tall; a bottom 1 thick set in them at y 0.6..1.6 above the chime; two iron hoops at y 0.9..1.4 and
4.1..4.6, 0.15 proud of the staves.

**The riddle** (both models, its rim's bottom edge at y0): a bent-wood rim of 16 segments, outer faces 5.5
from the axis (11 across), 0.45 thick, 2.4 deep; an iron band inside it at y0 + 0.45..1.15 (radius 4.75 to
5.1, into the rim) that holds the mesh; the mesh, square iron wires 0.25 thick at a pitch of 1.25 (openings
of 1), eight along x under eight along z, crossing on them, their ends in the band. The mesh's top, the
charge's bed, is at y0 + 1.05. y0 is 6.3 on the bearers and 7.6 hung on the stand.

**The charge** rides the riddle: an **oversize bed** on the mesh (radius 3.7, 0.7 thick) and over it one
layer of fines (a part charge: radius 2.4, 0.5) or two (a full charge: radius 2.8, 0.5, and a top of
radius 1.7, 0.36, heaped 0.21 over the rim). Each is a round slab of four strips. As the charge is riddled
the fines sink, the top into the layer under it and that into the bed, each hidden inside the one under it by
at least 0.034 (no driver scales a part, so a layer cannot shrink: it sinks out of sight), and **the fines in
the tub** rise out of its bottom, where they were hidden, layer by layer onto it (a full charge's second
layer onto its first). At W 1 the bed is left in the riddle and the fines lie in the tub, 1.35 deep.

**The hand riddle.** The riddle rests on the two bearers (oak, 0.9 × 0.8, across the tub's mouth at
z 4.7 and 11.3), its rim crossing both. While a charge is riddled the player lifts it 0.4 off them and shakes
it: turns about axes 2 blocks above it (so it moves nearly level, tilting about 2 degrees as hands do) carry
its middle 1.2 to and fro (z) and 0.6 side to side (x), a quarter turn apart, so it is swirled round an
ellipse once a turn of the clock. It clears the bearers by at least 0.118 throughout.

**The stand.** Four oak legs 1.5 square at the corners, top rails all round (y 14.6..16) and low stretchers
all round (y 1.4..2.6). Each side rail carries an iron pin at y 15.25 over the riddle's middle (z 8), in
the rail and an iron plate on its inner face; on it hangs an iron hanger (a strap, 0.4 × 0.8), down to a
trunnion pin at y 10, through an iron lug on the riddle's rim at its east and west points. The trunnions are
at the rim's top, so the riddle hangs level under them, 2.1 over the tub. The hand lever, oak, stands on the
operator's right (west, x 1.6..2.2), pivoted at y 2 in the west stretcher (an iron plate on it, an iron
knuckle on the lever), its grip at y 15.4. An iron link runs from a pin on the lever 9.3 over its pivot
(z 4) to a pin on the west hanger 3.95 under the hanger's pivot. Worked to and fro, the lever turns 6.97
degrees each way and the link carries the west hanger 16.6 degrees; the hangers swing together (one part:
they turn about one line), the riddle goes 1.5 to and fro on them and stays level. The link is drawn
parallel to itself, carried by the hanger's pin; the lever's pin moves 0.233 up and down against it at the
ends of the swing (the two pins swing on arcs about pivots above and below), which the link's eye at the
lever, a slot, takes, and 0.005 along it.

**The cycle** (t = W, one charge; θ, the clock, poses the shake, and only inside its window):

| t | What moves |
|---|---|
| 0.00..0.04 | The charge goes on (p eases in): the heap on the mesh, the tub's fines hidden in its bottom. |
| 0.04..0.08 | The shake eases in (and the hand riddle is lifted off the bearers). |
| 0.10..0.38 | A full charge's top sinks into its lower layer; its first fines rise onto the tub's bottom. |
| 0.42..0.76 | Its lower layer sinks into the bed; the second fines rise onto the first. |
| 0.10..0.76 | (A part charge: its one layer sinks into the bed; its fines rise onto the tub's bottom.) |
| 0.80..0.84 | The shake eases out (the hand riddle is set down on the bearers). |
| 0.84..1.00 | Still: the bed in the riddle, the fines in the tub. Delivered at 1. |

Every moving part (W the riddling, k the charge, p its presence, ψ the clock's travel; "shake" is a gauge of
amount 0 with `lobes` {ratio 1, amplitude A}, window 0.04..0.84, so it turns by e × A × cos(ψ + phase), e
its engagement):

| Model | Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|---|
| hand | Riddle: rim, band, mesh (`riddle`) [riddle] | The player's hands | The charge riding it | `gauge` slide y +0.4 (the lift), shake about x and shake about z, both about a point 2 blocks over it |
| stand | Hangers, the link's pin (`hangers`) [hangers] | The link (west hanger) | The riddle, the link | shake about x through the pivots, A 0.2898 |
| stand | Riddle (`riddle`) [riddle] | The hangers, on its trunnions | The charge riding it | rides `hangers`; shake about x through the trunnions, A −0.2898 (so it stays level) |
| stand | Lugs, trunnion pins (`lugs`) [hangers] | Fixed to the riddle | — | rides `riddle` |
| stand | Lever: bar, knuckle, pin (`lever`) [lever] | The player's hand | The link | shake about x through its pivot, A −0.1217 |
| stand | Link (`link`) [lever] | The lever's pin (in its slot) | The west hanger | rides `hangers`; shake about x through its pin on the hanger, A −0.2898 (parallel to itself) |
| both | Oversize bed (`c1base`, `c2base`) [charge…] | Rides the riddle | — | none |
| both | Fines in the riddle (`c1top`, `c2mid`, `c2top` riding `c2mid`) [charge…] | The shake | — | `gauge` slide y down into the layer under it |
| both | Fines in the tub (`c1fines`, `c2fines1`, `c2fines2` riding `c2fines1`) [charge…] | Falling through the mesh | — | `gauge` slide y up out of the tub's bottom |
| stand | Tub (`tub`) [tub] | Static | — | none |
| both | Frame (`frame`) | Static | — | none |

There are no gears. Every pin is carried: the hangers' pivot pins in the side rails and their iron plates, the
lever's pivot pin in the west stretcher and its plate, the trunnion pins in the lugs and the hangers' eyes,
the link's pins in the hanger and the lever.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py              # stdlib only; rewrites the eight files
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Riddle/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 30 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_riddle.py` holds the checks; every run checks both models and exits non-zero if
one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part.
- **Frame:** joined to the ground as one piece.
- **Textures** by role: oak timber, bearers, rim and lever; planks for the tub's staves and bottom; iron hoops,
  band, wires, hangers, lugs, link, plates, pins and knuckle; `ore` for the charge.
- **Containment:** nothing leaves the block over the cycle of both charges and the shake's phase.
- **Charge:** as loaded at W 0, the tub's fines hidden in its bottom (the second layer in the first); at W 1
  every layer of fines sunk inside the bed (a full charge's top first inside its lower layer), the bed not
  moved in the riddle, the fines on the tub's bottom and the second layer on the first, clear of the staves.
  Every hidden layer is inside what hides it by at least `HIDE_MARGIN` (0.02), measured in the riddle's frame.
- **Shake:** the clock moves nothing at W 0, 0.02, 0.86, 0.93 and 1 (nor with no charge); in the window the
  riddle goes 1.2 to and fro and 0.6 side to side by hand, 1.5 to and fro on the stand.
- **Hand:** the riddle rests on both bearers, its rim across them, and clears them by 0.05 or more while shaken.
- **Stand:** the riddle level (no turn at all) through the swing; the trunnions in the hangers' eyes and the
  link on the hanger's pin (exactly); the lever's pin in the link's slot (0.33 of room up and down, 0.03
  along); every pivot pin carried by its rail or stretcher and its plate; every eye round its pin by at least
  the pin's radius through the swing.
- **Clearances:** over 233 poses (W against the shake's phase, both charges) no two parts touch except the
  intended contacts in `ALLOWED`; **swept paths** (full runs) the same every 0.0025 of the cycle at the pace.
- **No z-fighting** (full runs): no coplanar overlapping faces at ten poses of what can be seen (the other
  charge's parts left out, `shown`).
- **The same riddle:** the stand's riddle is the hand riddle's raised, every element.
- **Files:** every texture declared; one cell with its lid; no power cell; the shipped models are the checked
  ones (no move: the build frame's corner is the controller's).

The cells' boxes are rebuilt from the shipped, rounded shapes posed at rest by the shipped rigs, as the other
machines' are.

### Rig schema (`riddle-rig.json`, `riddlestand-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") as the squaring shear uses it, with nothing
added to the shared rig maths: a `work` quantity and gauges, the shake a gauge's `lobes` (the rosser's
scraper arms are the other user of them). Unlike the other hand stations, no driver reads θ itself (there is
no ratio-0 rotate): the lobes read its travel ψ, which is enough for the viewer to offer the clock and Play.

- **The work.** `{ "name": "riddling", "unit": "charges", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one charge's riddling, 0..1.
- **Inputs.** W, k and p, and ψ. k is the charge (1 a part charge, 2 a full charge, 0 none): the renderer is
  to choose it by how much is in the riddle, as the mill shows two trunk sizes. p is its presence, eased in
  as it goes on and out after delivery, k held while p eases out. ψ is the clock's travel: gameplay is to
  turn θ while the player holds right-click and advance W by 1 / `shakesPerCharge[k]` a turn. The shake's
  engagement is p times its window's, so it eases in and out with the riddling and stops with no charge.
- **`riddling`**: `shakesPerCharge` (hand: 12 a part charge, 20 a full one; stand: 20 and 32), the pace.
  Provisional: #714 sets it.

**Keys.** `cells`, `infeedSide`, `outputSide`, `output`, `charge`, `work`, `riddling` and `parts`.
`requires` values: hand `riddle`, `chargesmall`, `chargefull` or null; stand also `tub`, `hangers`, `lever`.
The texture code the renderer sets: `ore`.

**In the viewer** (`site/models.json`, `riddle` and `riddle-stand`; a standalone copy each with
`site/scripts/standalone-viewer.ts`): the clock is the θ slider (its travel shown under it); the riddling's
slider reads in charges; the charge select names the sizes ("Part charge", "Full charge"), and each
size's charge and fines show only with it. Play moves W with θ at a part charge's pace
(`play.turnsPerWork` `"riddling.shakesPerCharge.thin"`).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `riddle_*` (`riddle_rim_*`,
`riddle_band_*`, `riddle_wirex_*`, `riddle_wirez_*`), the charge's `c1base_*`, `c1top_*`, `c2base_*`, `c2mid_*`,
`c2top_*`, `c1fines_*`, `c2fines1_*`, `c2fines2_*`; on the stand `hanger_*`, `lug_*`, `lever_*`, `link_*`,
`tub_*`; and `fr_*` for each frame (the hand riddle's tub is `fr_tub_*`). Hand edits are lost when the script
runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The models have been reviewed in projections rendered from the written files. No one has yet looked at them
in a client, and the standalone viewers were built but not seen with WebGL here.

1. **The fines sink rather than fall.** No driver scales a part, so the heap cannot shrink: each layer of
   fines sinks into the one under it, and the fines in the tub rise out of its bottom. Nothing is drawn
   falling through the mesh; gameplay's dust particles under the riddle are to carry that.
2. **The oversize bed is large.** It must be thicker than any layer hidden in it, so it is 0.7 thick and
   most of the mesh's width; what is left at W 1 reads as a thick bed of oversize, not a few lumps.
3. **The oversize is not tipped off.** It stays in the riddle until the charge is delivered (the gameplay's
   to drop or discard). See "Open for the owner".
4. **The hand riddle's cause is the player.** No hands are modelled (as the mandrel station's hammer is not):
   the riddle moves as held.
5. **The shake stops where it is.** If the player lets go part way through a charge, θ stops and the riddle
   holds where it was in its swing, off its rest; a renderer can ease θ to the next whole turn.
6. **The link's slot.** The lever's pin rides 0.233 up and down in the link's eye; the slot is not drawn as a
   hole (the eye is a solid block the pin passes through).
7. **Linear ramps** for the fines' sinking and rising, as the other hand stations' gauges.
8. **A full charge looks the same on both models,** though the stand's is "a stack".

## Open for the owner

1. **The stand's mechanism.** A hand lever and a link to one hanger, as the brief suggested, so the swing has
   a visible cause and the riddle need not be held. Alternatives: a handle bar fixed to the riddle's rim
   (the operator pushes the hung riddle directly, as the hanging sieves in De Re Metallica were worked: fewer
   parts, and the same handle could tip it), or four hangers (a parallelogram) instead of two trunnions.
2. **Tipping the oversize off one side.** Not built. A riddle tipped about its own axis empties within its own
   width, which is over the tub, so tipping the oversize clear needs the riddle moved beyond the tub first:
   a 1 × 2 stand with longer hangers (2 blocks high) to swing it out over an oversize box, or a riddle hinged
   at its far edge that flips over into a box in the second cell (the riddle would have to be about 9 across
   to flip inside one block's height). Either is possible with the existing drivers; say which, if any.
3. **A hopper** over the stand for "a stack at a time": left out (a stack is a full riddle here).
4. **Oversize at all?** If #714 makes the riddle a plain conversion (crushed to classified, no oversize item),
   the bed could be fines too and sink away entirely, but a slab cannot hide without something to hide in;
   the bed is the simplest honest remainder.
5. **The two charge sizes** (a part charge and a full one) for k, and their paces (12/20 shakes by hand,
   20/32 on the stand): provisional, gameplay's to set. Should the stand's full charge be heaped higher?
6. **Sizes.** The riddle is 11 across (vanilla's sieve is 14; a real miner's riddle is about 8), the tub
   11.5, the stand 16 square and a block high, its riddle 2.1 over the tub.
7. **Sides.** Infeed east and output south are placeholders; the operator to the north as the other hand
   stations.

## Gameplay

Not built: #714. The rigs are written for it the way the other hand stations' are (the squaring shear's
`Core/` and `Game/` are the template): read the anchors, `work`, `riddling.shakesPerCharge` and `requires`,
never element names; set the texture code `ore`; draw the `chargesmall`/`chargefull` parts only while that
charge is in the riddle; turn θ while held and give the renderer ψ, its travel.
