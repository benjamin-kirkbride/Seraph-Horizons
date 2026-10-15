# Slitting mill

Part of the Seraph Horizons mod (`../README.md`). A mechanically powered **slitting mill**, the nail-rod
mill of the 17th to 19th centuries, with a powered **cut-off shear** behind its stand: it takes one of the
game's metal **plates** (`game:metalplate-{metal}`) and makes the game's **nails and strips**
(`game:metalnailsandstrips-{metal}`). The plate is offered on the feed table to the **slitters**, two
arbors of steel collars in cast-iron housings, the cutters of the top arbor running in the gaps of the
bottom one; the arbors are paired by two equal spur gears, the **pinions**, and turn the opposite ways, so
the plate is drawn in and comes out cut lengthwise into eight square **rods** (nail rods), in two layers
as a slitter leaves them. Behind the stand a **knife**, hung in slots in the housings' back posts and
worked by a scotch yoke from a crank on the entry shaft, comes down once a turn of the axle just past a
fixed **bed knife** and crops a nail's length off the rods' ends; the pieces fall into the **nail box**.
When the plate's tail leaves the slitters nothing pushes the rods on, so their last length, from the
slitters to the knife, is left whole: the **strips**.

This pass is the model only: its generator, its validation and its rig (`tools/`), with the site's viewer
entry. There is no block, recipe, gameplay or export record yet, and no build stages; the `requires` ids
below are provisional (see "Open for the owner").

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator
is `Machines/tools/machinegen/`, unchanged by this machine (no new driver: every motion is a `rotate`, a
`slide` or a `gauge`, the work's growth a stretching gauge).

## The period machine and this one

A period slitting mill heated a bar, flattened it in a pair of plain flatting rolls and passed it through
the slitters, the strip coming out as several square rods; the rods were cut to length and headed into
nails elsewhere, by hand or later by nail machines. The mill was driven by a water wheel. Here:

- **The work is already a plate**, so there are no flatting rolls (nor a pinch roll: the slitters grip the
  plate, as they gripped the flattened bar). Iron side guides on the feed table keep it square to them.
- **The nails are made at the exit, by power**: a guillotine cut-off shear off the same axle, so one
  machine gives both, nails and strips (see "The nail mechanism" for why this one).
- **The power is an axle**, at a power cell, as on every machine in the pack: no water wheel. The roll
  pairing pinions stay, the stand's signature; the drive comes to them through a rectifier and a great
  wheel.

## The machine

**Footprint.** 8 cells: 2 wide (x), 2 high (y), 2 long (z), the long axis the plate's way. In the native
frame with the controller at `[0,0,0]`: x 0..1, y 0..1, z 0..1. The controller is the feed table's west
half at ground level, `[0,0,0]`, nearest the player; the plate travels south (+z) through the stand to the
knife and the nail box at the far end. Placed as the draw bench is (the block's `side` the way the player
looks), the axle comes in at the far end, a block up, on the player's left (native east).

**Anchors** (in `assets/seraphhorizons/config/slittingmill-rig.json`):

| Key | Where | For |
|---|---|---|
| `powerCell`, `powerFace` | `[1,1,1]`, `east`: the entry shaft's axis at the cell's centre (x 32, y 24, z 24 voxels) | the vanilla axle |
| `infeedSide` | `north` | the feed table's end, where the plate goes on |
| `outputSide`, `output.pos` | `south`, `[1.0, 0.3125, 1.9994]`: the nail box's south face, at its top | the nails and strips |
| `plate.pos` | the plate's middle on the table | loading sounds |
| `nip.pos` | the nip, on the pass line between the arbors | the slitting's sound and dust |
| `knife.pos` | the cut, on the pass line at the knife's face | the cut's sound |

**The work, exactly** (voxels). The plate is drawn as the squaring shear lays it, 8 × 8 and 1 thick (the
game's plate item is 9 × 9). It is slit into eight rods 1 × 1; each rod gives two nails 2 long and a strip
4 long (the distance from the nip to the knife).

**Fitted parts** (the rig's `requires`; the frame carries everything with `requires` null: the oak sills,
the housings with their brass chocks and screws, the pedestals and the outer standard, the feed table and
its guides, the nail box, and the entry shaft):

| `requires` | Draws |
|---|---|
| `rolls` | The slitters: both arbors, each with five steel cutters and five spacers, their necks and collars |
| `pinions` | The two equal spur gears (16 teeth, module 0.4) on the arbors' west necks |
| `drive` | The great wheel (29) on the top arbor's east neck, the rectified shaft with its pinion (8), the entry's two pinions (8 and 6), the rectifier's two loose wheels (24 and 18) with their catches and the idler (8) |
| `shear` | The crank disc and its pin, the knife (blade, crosshead, ram and yoke) and the bed knife |
| `plate` | The work: the plate, its rods (the strips) and its nails, all wearing the texture code `metal` |

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. Textures are the game's, referenced: debarked oak, iron plate, plain steel sheet, brass
sheet for the chocks, and the work's `metal` (`game:block/metal/ingot/iron` in the shape; a renderer would
set it to the plate's metal, as the draw bench sets `die`). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/slittingmill.json` | The whole machine, every moving part and the work (487 elements). |
| `assets/seraphhorizons/shapes/block/slittingmill_frame.json` | The static frame only (51 elements). |
| `assets/seraphhorizons/config/slittingmill-rig.json` | Footprint, anchors, the work, the slitting's figures (`slit`) and the part rig (21 parts). |
| `tests/SlittingMill/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions are in the generator's build frame (native axes, voxels, from the machine box's north-west bottom
corner, which is the controller's).

**The stand.** Two cast-iron housings (x 8.5..11 and 21..23.5) stand on oak sills, each a base, a front and a
back post and a cap round a window, with a brass chock top and bottom carrying the arbors' necks. A screw
through each cap bears on the top chock (its square head and capstan bar on top): the screws set the gap.
The arbors' axes are at y 6.8 and 13.2, z 20 (the nip), 6.4 apart: the pinions' pitch circles.

**The slitters.** Each arbor carries ten collars, x 11..21, one voxel each: cutters (radius 3.46) alternating
with spacers (1.7), the top arbor's cutters over the bottom one's spacers. The cutters overlap by 0.52. Rod i
(x 12 + i, i 0..7) runs under a top cutter (the even rods: the lower layer, its top on the top cutters' rims,
y 8.74..9.74) or over a bottom cutter (the odd: the upper layer, y 10.26..11.26), 0.1 clear of the other
arbor's spacer. The plate is fed at the pass line, y 9.5..10.5.

**The drive** (θ the axle's angle, ψ its travel either way):

| Stage | Teeth | Ratio |
|---|---|---|
| Entry shaft → A1 → W1 (direct), module 0.4 | 8 : 24 | W1 −θ/3 |
| Entry shaft → A2 → idler → W2, module 0.4 | 6 : 8 : 18 | W2 +θ/3 |
| W1, W2 loose on the rectified shaft, each on a one-way catch | | the shaft +ψ/3, forward either way |
| Rectified shaft's pinion P2 → great wheel on the top arbor, module 0.5 | 8 : 29 | top arbor −0.09195 ψ |
| Top pinion → bottom pinion, module 0.4 | 16 : 16 | bottom arbor +0.09195 ψ |

The cutters' rims (radius `R_CUT` = 3.4616, derived from the gearing) carry the plate 2 voxels, a nail's
length, a turn of the axle, south, whichever way it turns. Toothed wheels and what they mesh (nothing toothed
meshes nothing): A1 ↔ W1; A2 ↔ idler ↔ W2; P2 ↔ the great wheel; the two pinions. The collars, the crank
disc and the hubs are plain.

**The cut-off shear.** The crank disc is overhung on the entry shaft's west end (x 17.2..18.4); its pin (throw
1.75) runs in the slot of the knife's yoke, a scotch yoke, so the knife moves straight up and down, −1.75 cos θ,
a stroke of 3.5: at the bottom at every whole turn of the axle (the edge 0.29 under the lower layer, past the
bed knife's top), 0.69 over the upper layer at the half turn. The knife's blade (steel, 3.4 deep) hangs under
its crosshead, whose ends run in slots in the housings' back posts; a ram joins the crosshead to the yoke. The
blade passes 0.03 behind the bed knife's back face (z 24, the cut).

**The work** (W in knife strokes, one a turn of the axle):

| W | What happens |
|---|---|
| 0 | The plate lies on the table, its front at the nip (p eases in). |
| 0..4 | The plate goes into the nip, 2 a stroke; its tail leaves the nip at 4. |
| 0..2 | The rods grow out of the nip, in their two layers, and reach the knife's face at 2. |
| 2..3 | The first nail of every rod comes past the knife's face. |
| 3 | The knife at the bottom parts it; it falls into the box over 3..3.3. |
| 3..4 | The second nail comes through; parted at 4, it falls onto the first over 4..4.3. |
| 4..4.5 | The rods, no longer pushed, lie from the nip to the knife: the strips. |
| 4.45..4.5 | The nails and the strips are taken off (the hand-out, the job's end; the knife at the top). |

Every moving part:

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft (`entry`) | The vanilla axle | A1, A2, the crank (keyed) | `rotate` x 1 (θ) |
| A1, A2 (`entrygears`) [drive] | The entry shaft | W1; the idler | `rotate` x 1 (θ) |
| W1 with its catch (`rectw1`) [drive] | A1 | The rectified shaft, when the axle turns − | `rotate` x −1/3 (θ) |
| Idler (`idler`) [drive], on its stud | A2 | W2 | `rotate` x −3/4 (θ) |
| W2 with its catch (`rectw2`) [drive] | The idler | The rectified shaft, when the axle turns + | `rotate` x 1/3 (θ) |
| Rectified shaft and P2 (`rectshaft`) [drive] | Whichever catch bites | The great wheel | `rotate` x 1/3 (ψ) |
| Great wheel (`wheel`) [drive] | P2 | The top arbor (keyed) | `rotate` x −0.09195 (ψ) |
| Top arbor (`rolltop`) [rolls] | The great wheel | The top pinion; the plate | `rotate` x −0.09195 (ψ) |
| Top pinion (`piniontop`) [pinions] | Keyed on the top arbor | The bottom pinion | `rotate` x −0.09195 (ψ) |
| Bottom pinion (`pinionbottom`) [pinions] | The top pinion | The bottom arbor (keyed) | `rotate` x 0.09195 (ψ) |
| Bottom arbor (`rollbottom`) [rolls] | The bottom pinion | The plate | `rotate` x 0.09195 (ψ) |
| Crank disc and pin (`crank`) [shear] | The entry shaft (keyed) | The yoke | `rotate` x 1 (θ) |
| Knife: blade, crosshead, ram, yoke (`knife`) [shear] | The crank pin in the yoke's slot | The rods (the cut) | `slide` y, amplitude 1.75, phase −π/2 (θ) |
| Bed knife (`bedknife`) [shear] | Fixed | — | none |
| Plate (`plate`) [plate] | The slitters | — | stretching `gauge` z about the nip, 1 → 0 over W 0..4 |
| Rods, the strips (`rods`) [plate] | The slitters | — | stretching `gauge` z from the nip, 0.01 → 4 long over W 0..2; another to 0 over 4.45..4.5 |
| Nails (`nail1l`, `nail1u`, `nail2l`, `nail2u`: nail 1 or 2, lower or upper layer) [plate] | The rods (fed), the knife (parted) | — | stretching `gauge` z, 0.006 → 2 long over the stroke before its cut; `gauge` slide z to the knife's face as it starts; `gauge` slide y down into the box over 0.3 after the cut; a stretch to 0 at the hand-out |
| Frame (`frame`) | Static | — | none |

The work's pieces are authored as slivers (the rods 0.01 long at the nip, the nails 0.006 long inside the rods'
first voxel) and grown by stretching gauges, because a gauge is the identity at presence 0: at the moment a
plate goes on, only the plate shows, and no rod or nail flashes up while p eases in.

### Regenerating

```sh
python3 mods-src/seraphhorizons/SlittingMill/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/SlittingMill/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/SlittingMill/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 50 s. `tools/validate_slittingmill.py` holds the checks; every run checks its output and
exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part, no part the renderer would never draw.
- **Containment:** nothing leaves the 2 × 2 × 2 box at 16 idle angles and over the job both ways.
- **Textures** by role: oak timber, the box and the axle's continuation; iron castings, the knife's crosshead,
  ram and yoke and the wheels; steel collars, arbors, blades, pins and pinions; brass chocks; the work `metal`.
- **Anchors:** the entry shaft meets the power face at the power cell's centre; the output on the box's south
  face, the infeed the table's north end; the nip and knife anchors at the nip and the cut.
- **Gearing:** every meshing pair's centre distance is the sum of the pitch radii, and the arcs rolled on the
  two pitch circles are equal (finite differences of the posed rig, the axle either way); no tooth of a pair
  runs into the other's over 48 angles. The rectifier: whichever way the axle turns, exactly one loose wheel
  turns with the rectified shaft, and the shaft forward.
- **Feed:** both arbors' rims carry the plate south 2 a turn at the nip, either way.
- **Slitters:** the cutters overlap; each layer of rods lies on its cutters' rims and 0.1 clear of the other
  arbor's spacers; every rod runs under the cutter of its layer.
- **Work:** the plate's length and front, the rods' front, each nail from the knife's face as it grows, a nail
  long at its cut, lying in the box (or on the first nail) after its fall; nothing left at the hand-out; no
  sliver of a rod or nail visible at presence 0 or 0.5; every 0.05 of the job, both ways round.
- **Knife:** the crank pin's centre in the middle of the yoke's slot and its ends inside it all round; the edge
  under the lower layer at the bottom, over the upper layer at the top, the crosshead never down to the rods;
  the knife at the bottom at both cuts, either way round; the blade behind the bed knife; the crosshead's ends
  in the housings' slots all round.
- **Supports:** the entry shaft in its pedestal bearing and the outer standard, the rectified shaft the same,
  each arbor in its two chocks (inside each by a margin, at rest and turned 45°); the idler on its stud; each
  screw through its cap onto its top chock.
- **Clearances:** over 153 poses (16 idle angles, the job every 0.05 forward and 0.1 backward) no two parts touch
  except the intended contacts in `ALLOWED` (shafts in bearings, keyed parts, gears in mesh, the crank pin in the
  yoke, the work on the table, in the slitters, on the bed knife, under the knife, in the box).
- **Swept paths** (full runs): the same every 0.01 stroke forward, 0.02 backward, and 36 idle angles (749 poses).
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at eleven poses, of what can be seen (`shown`: the
  work's slivers and the work with no plate on are left out).
- **Files:** every texture declared; lids over every column, no hollow cell; the power cell; the shipped model
  is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, leaving out the
work, as the other machines' are.

### Rig schema (`slittingmill-rig.json`)

The cylinder borer's schema (`../CylinderBorer/README.md`), with nothing added to the shared rig maths.

- **The work.** `{ "name": "knife strokes", "unit": "strokes", "step": 0.005, "end": { "thin": 4.5 } }`: one
  class, a plate, whatever its metal (the metal is the texture code `metal`, not a class). W counts knife
  strokes since the plate went in.
- **Inputs.** θ and ψ; W, k (1 with a plate on) and p.
- **`slit`**: `turnsPerStroke` (1: the crank is on the entry shaft), `cuts` (3, 4: W at which the knife parts a
  nail off every rod), `handOut` (4.5), `rods` (8), `nailsPerRod` (2), `plate` and `output` (the item codes,
  `{metal}` the plate's).
- **The phase.** The knife is at the bottom at every whole turn of θ and the cuts are at whole strokes, so a
  plate's W must start at 0 at a whole turn of θ and move a stroke a turn (|Δθ| / 2π), either way. Gameplay can
  read θ from the network (as the other machines' renderers do) and start a plate's W at the next whole turn.

**Keys.** `cells`, `powerCell`, `powerFace`, `infeedSide`, `outputSide`, `output`, `plate`, `nip`, `knife`,
`work`, `slit` and `parts`. `requires` values: `rolls`, `pinions`, `drive`, `shear`, `plate`, or null.

**In the viewer** (`site/models.json`): the axle's slider (Play turns it and moves W with it, a stroke a turn,
from 0); the knife strokes' slider stops at 4.5; the size select names "None" and "Plate" (the viewer's size
select now offers one class for a work quantity of one class: `topClass` in `site/src/lib/model-scenario.ts`);
the plate's metal is a texture select over eleven metals' ingot textures; the work shows only with a plate
(`requiresClass`). Start Play at θ 0 and W 0 to see the knife come down as each nail is parted.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `entrygears_*`,
`crank_*`, `knife_*`, `bedknife_*`, `rectw1_*`, `idler_*`, `rectw2_*`, `rectshaft_*`, `wheel_*`, `rolltop_*`,
`rollbottom_*`, `piniontop_*`, `pinionbottom_*`, `plate_*`, `rods_*`, `nail1l_*`, `nail1u_*`, `nail2l_*`,
`nail2u_*`, and `fr_*` for the frame. Hand edits are lost when the script runs again: port them into
`make_shape.py`, or stop regenerating.

## The nail mechanism

A crank-driven guillotine on the entry shaft, through a scotch yoke, cutting against a fixed bed knife just
behind the stand. Why this one:

- **A visible cause, one turn one stroke.** The crank is keyed on the axle's own shaft, so the knife strokes
  once a turn whichever way the axle turns, and the yoke gives it a plain straight stroke that the `slide`
  driver draws exactly (a connecting rod's motion is not a sine, so the drivers would only approximate it).
- **No clutch, no extra gearing.** The rolls feed a nail's length a turn through the drive's gearing, so the knife
  needs no train of its own; the rectifier only has to make the rolls feed forward.
- **Strips and nails from one cause.** The nails are what passes the knife; the strips are the rods' last length,
  which nothing pushes past it once the plate's tail has left the slitters. The split is the nip-to-knife distance.
- **Readable.** One reciprocating part over the exit, against the stand, where the rods come out.

What it costs: the phase contract with gameplay above, and the cut is a flying cut (see the weak spots).

## Open for the owner

1. **The nail mechanism** is my pick (above). Alternatives: a W-driven cam knife behind a dog clutch (the gear
   cutter's way; it keeps the knife in step with any pace, but adds a clutch and its lever), a rotary shear
   (two knife drums), or a header after the cut (a ram that upsets a head on each nail; the nails here are
   headless cut lengths).
2. **Nails and strips** are split by geometry: two 2-voxel nails and one 4-voxel strip a rod, eight rods a plate.
   The game's item is one stack of nails and strips; how many a plate makes is gameplay's.
3. **The pace is fixed by the knife**: a stroke a turn, 4.5 axle turns a plate, every metal alike (a metal can
   cost more load, not more turns), and gameplay must start a plate at a whole turn of θ.
4. **The rectifier** (five toothed wheels on the east side) is there so the rolls feed whichever way the axle
   turns. If the mill may simply refuse a reversed axle, the drive shrinks to a pinion and the great wheel.
5. **Footprint and power:** 2 × 2 × 2, the axle a block up at the far end's east face (the crank had to be over
   the knife). The upper cells hold only the screws, the drive's top and the knife's yoke.
6. **One class**: a plate of any metal, the metal a texture code. To keep the viewer's select to "None" and
   "Plate" I changed the viewer to offer one class for a work of one class (`topClass`, with a test and the docs);
   two classes (say soft and hard metals) would avoid that change but double the work's parts.
7. **The plate** is drawn 8 × 8, as the squaring shear lays it; the game's plate item is 9 × 9.
8. **No flatting or pinch rolls**; the slitters take the plate straight from the table.
9. **The `requires` ids** (`rolls`, `pinions`, `drive`, `shear`) are provisional; the stages and their items are
   still to be decided with the owner.
10. **Renderer rule (later):** the work should be drawn only while a plate is on, not while p eases out after the
    hand-out (as the squaring shear does), or the plate would grow back on the table as p falls.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files; the standalone viewer was built
but not seen with WebGL. No one has looked at it in a client.

1. **A flying cut.** The rods move on while the knife is down: about a third of a stroke either side of each whole
   turn the knife's blade stands in the rods' ends (the first nail's front pushes into it as it rises, the parted
   nail falls through it). The clearance checks allow the knife and the nails to touch.
2. **The rods jump layers at the nip.** The plate is fed at the pass line and each rod is a straight box in its
   layer, 0.76 above or below, from the nip on; nothing draws the bend between.
3. **Growth by stretching.** The rods and nails grow by scaling slivers; their textures are mapped for their full
   length, so a growing piece's texture is squeezed until it is full grown.
4. **The nails fall at a steady speed**, straight down, and lie in two neat layers in the box.
5. **Box teeth, 12-gon cutters**; the cutters' corners dip up to 0.12 into the rods as they turn.
6. **The catches** are drawn as a lug on each loose wheel's hub, not as a ratchet and pawl.
7. **The plate's front enters the cutters' bite** before the nip (the cutters reach above and below the pass line
   there): the plate is drawn flat through them.
