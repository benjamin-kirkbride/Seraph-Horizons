# Slitting mill

Part of the Seraph Horizons mod (`../README.md`). A mechanically powered **slitting mill**, the nail-rod
mill of the 17th to 19th centuries, with a powered **cut-off shear** behind its stand: it takes one of the
game's metal **plates** (`game:metalplate-{metal}`) and makes the game's **nails and strips**
(`game:metalnailsandstrips-{metal}`). The plate is offered on the feed table to the **slitters**, two
arbors of steel collars in cast-iron housings, the cutters of the top arbor running in the gaps of the
bottom one; the arbors are paired by two equal spur gears, the **pinions**, and turn the opposite ways, so
the plate is drawn in and comes out cut lengthwise into eight square **rods** (nail rods), in two layers
as a slitter leaves them. Behind the stand a **knife**, hung in slots in the housings' back posts and
worked by a scotch yoke from a **crankshaft**, comes down once a stroke just past a fixed **bed knife**:
its edge presses a nail's length of every rod down until it parts, and the **nails** fall into the **nail
box**. The feed is **intermittent**: a **mutilated gear** on the crankshaft steps the **great wheel** on
the top arbor while the knife is up, and the rolls and the plate stand while it is down. When the plate's
tail leaves the slitters the rolls push the rods no further, so their last length, from the slitters to the
knife, is left whole: the **strips**. The operator's **push bar**, which has followed the plate's tail along
the feed table, pushes them out past the knife (its fingers run in the rods' own channels between the
collars), and they fall into the nail box with the nails.

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
nails elsewhere, by hand or later by nail machines, which fed their stock intermittently past a
reciprocating cutter. The mill was driven by a water wheel. Here:

- **The work is already a plate**, so there are no flatting rolls (nor a pinch roll: the slitters grip the
  plate, as they gripped the flattened bar). Iron side guides on the feed table keep it square to them.
- **The nails are made at the exit, by power**, with a nail machine's intermittent feed: a guillotine
  cut-off shear and the slitters on one train, so one machine gives both, nails and strips (see "The cut and
  the feed").
- **The power is an axle**, at a power cell, as on every machine in the pack: no water wheel. The roll
  pairing pinions stay, the stand's signature; the drive comes to them through a rectifier, a cone clutch,
  the crankshaft's mutilated gear and the great wheel.

## The machine

**Footprint.** 8 cells: 2 wide (x), 2 high (y), 2 long (z), the long axis the plate's way. In the native
frame with the controller at `[0,0,0]`: x 0..1, y 0..1, z 0..1. The controller is the feed table's west
half at ground level, `[0,0,0]`, nearest the player; the plate travels south (+z) through the stand to the
knife and the nail box at the far end. Placed as the draw bench is (the block's `side` the way the player
looks), the axle comes in at the near end, a block up, on the player's left (native east).

**Anchors** (in `assets/seraphhorizons/config/slittingmill-rig.json`):

| Key | Where | For |
|---|---|---|
| `powerCell`, `powerFace` | `[1,1,0]`, `east`: the entry shaft's axis at the cell's centre (x 32, y 24, z 8 voxels) | the vanilla axle |
| `infeedSide` | `north` | the feed table's end, where the plate goes on |
| `outputSide`, `output.pos` | `south`, `[1.0, 0.3125, 1.9994]`: the nail box's south face, at its top | the nails and strips |
| `plate.pos` | the plate's middle on the table | loading sounds |
| `nip.pos` | the nip, on the pass line between the arbors | the slitting's sound and dust |
| `knife.pos` | the cut, on the pass line at the bed knife's back face | the cut's sound |

**The work, exactly** (voxels). The plate is drawn as the squaring shear lays it, 8 × 8 and 1 thick (the
game's plate item is 9 × 9). It is slit into eight rods 1 × 1; each rod gives two nails 2 long and a strip
4 long (the distance from the nip to the cut).

**Fitted parts** (the rig's `requires`; the frame carries everything with `requires` null: the oak sills,
the housings with their brass chocks and screws, the pedestals, the entry's inner post and the outer
standard, the feed table and its guides, the nail box, and the entry shaft):

| `requires` | Draws |
|---|---|
| `rolls` | The slitters: both arbors, each with five steel cutters and five spacers, their necks and collars |
| `pinions` | The two equal spur gears (16 teeth, module 0.375) on the arbors' west necks |
| `drive` | The rectifier (the entry's two pinions, the two loose wheels with their catches, the idler), the rectified shaft and its gear, the clutch gear with the cup, the cone and its fork, the crankshaft with its mutilated gear, the great wheel |
| `shear` | The crank disc and its pin, the knife (blade, crosshead, ram and yoke) and the bed knife |
| `pusher` | The push bar: its head between the table's guides, eight fingers (one a rod, at its layer), a handle back to the table's north end |
| `plate` | The work: the plate, its rods (the strips) and its nails, all wearing the texture code `metal` |

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. Textures are the game's, referenced: debarked oak, iron plate, plain steel sheet, brass
sheet for the chocks, cupronickel sheet for the cone clutch (the draw bench's), and the work's `metal`
(`game:block/metal/ingot/iron` in the shape; a renderer would set it to the plate's metal, as the draw bench
sets `die`). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/slittingmill.json` | The whole machine, every moving part and the work (635 elements). |
| `assets/seraphhorizons/shapes/block/slittingmill_frame.json` | The static frame only (65 elements). |
| `assets/seraphhorizons/config/slittingmill-rig.json` | Footprint, anchors, the work, the slitting's figures (`slit`) and the part rig (27 parts). |
| `tests/SlittingMill/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions are in the generator's build frame (native axes, voxels, from the machine box's north-west bottom
corner, which is the controller's).

**The stand.** Two cast-iron housings (x 8.5..11 and 21..23.5) stand on oak sills, each a base, a front and a
back post and a cap round a window, with a brass chock top and bottom carrying the arbors' necks (the bottom
one on the base). A screw through each cap bears on the top chock (its square head and capstan bar on top):
the screws set the gap. The arbors' axes are at y 7 and 13, z 20 (the nip), 6 apart: the pinions' pitch circles.

**The slitters.** Each arbor carries ten collars, x 11..21, one voxel each: cutters (radius 3.18) alternating
with spacers (1.6), the top arbor's cutters over the bottom one's spacers. The cutters overlap by 0.37. Rod i
(x 12 + i, i 0..7) runs under a top cutter (the even rods: the lower layer, its top on the top cutters' rims,
y 8.82..9.82) or over a bottom cutter (the odd: the upper layer, y 10.18..11.18), 0.085 clear of the other
arbor's spacer. The plate is fed at the pass line, y 9.5..10.5.

**The drive** (θ the axle's angle, ψ its travel either way, W the knife strokes):

| Stage | Teeth | Turns |
|---|---|---|
| Entry shaft → A1 → W1 (direct), module 0.4 | 16 : 16 | W1 −θ |
| Entry shaft → A2 → idler → W2, module 0.4 | 12 : 8 : 12 | W2 +θ |
| W1, W2 loose on the rectified shaft, each on a one-way catch | | the shaft −ψ, the same way either way |
| Rectified shaft's gear → clutch gear, loose on the crankshaft, with the cup, module 0.4 | 25 : 25 | the cup +ψ |
| Cup → cone (smooth faces; thrown in by its fork while a plate is on), feathered on the crankshaft | | the crankshaft one turn a stroke, +2πW |
| Crankshaft's mutilated gear (3 teeth on a 26-tooth pitch, module 0.3) → great wheel (50) on the top arbor | 5 teeth a step | the top arbor −36° a step, standing between |
| Top pinion → bottom pinion, module 0.375 | 16 : 16 | the bottom arbor +36° a step |

At the drawn pace, one axle turn a stroke (`slit.turnsPerStroke`), the cone and the cup turn together; the
cone's smooth faces are where the axle's half meets the half the work drives, so a gameplay pace other than the
drawn one, or the cup's phase, shows nowhere (the draw bench's arrangement). The crankshaft is at the same angle
at every whole stroke. A step turns the cutters' rims (radius 3.18) a nail's length, 2 voxels; a job's five steps
turn the arbors half a turn, which every pattern on them, on the pinions and on the wheel repeats in, so the next
plate's W starting again at 0 moves nothing. Toothed wheels and what they mesh (nothing toothed meshes nothing):
A1 ↔ W1; A2 ↔ idler ↔ W2; the rectified shaft's gear ↔ the clutch gear; the mutilated gear ↔ the great wheel
(while its sector is round); the two pinions. The collars, the cup and the cone, the crank disc and the hubs are
plain.

**The mutilated gear.** Three box teeth on the pitch of a 26-tooth gear, on a plain disc cut back below the great
wheel's teeth. A tooth is inside the wheel's teeth for about 1.8 pitches either side of the line of centres, so
the wheel turns, at the gears' ratio, for five pitches of the crankshaft (3/16 of a stroke, centred half a stroke
after each cut): five of its teeth. Before and after, no tooth of the sector reaches it, and it stands.

**The cut-off shear.** The crank disc is overhung on the crankshaft's west end (x 17.2..18.4); its pin (throw 2)
runs in the slot of the knife's yoke, a scotch yoke, so the knife moves straight up and down, −2 cos 2πW, a
stroke of 4: at the bottom at every whole stroke (the edge 0.29 under the lower layer, past the bed knife's top),
1.34 over the upper layer at the half. The blade (steel, 3.4 deep) hangs under its crosshead, whose ends run in
slots in the housings' back posts; a ram joins the crosshead to the yoke. The blade passes 0.03 behind the bed
knife's back face (z 24, the cut). The cut is a shear: the bed knife holds the rod, the blade's edge comes down on
the length past it and presses it down with it, and the length parts when the edge has gone through it.

**The cut and the feed** (W in knife strokes; the knife at the bottom at whole strokes, up at half strokes):

| W | What happens |
|---|---|
| 0 | The plate lies on the table, its front at the nip (p eases in; the fork throws the cone in). The knife is down. |
| 0.40..0.60, and so every stroke | A feed step, the knife up: the mutilated gear turns the great wheel five teeth, the rolls carry the work 2. |
| 0.4..1.6 | Steps 0 and 1: the rods grow out of the nip, in their two layers, and reach the cut. |
| 2.4..2.6 | Step 2: the first nail of every rod comes past the cut. |
| 2.70..2.78 | The knife's edge comes down on the upper layer's nails, presses each down its thickness: through at 2.7783, and they fall. |
| 2.81..2.91 | The same for the lower layer: through at 2.9148, and they fall into the box. |
| 3 | The knife at the bottom. |
| 3.4..3.6 | Step 3: the second nails come through; the plate's tail leaves the nip. |
| 3.70..3.91 | The second nails are pressed through and fall, the upper layer then the lower. |
| 4.33..4.67 | The knife up: the push bar pushes the strips out their length, all of each wholly past the cut (step 4 turns the rolls with them). |
| 4.67..4.89 | Past the bed knife, the strips fall into the box, onto the second nails and the box's shelf, ahead of the knife coming down. |
| 4.69..4.95 | The push bar is drawn back through the slitters' channels and along the table's grooves to where it started. |
| 4.96..5 | The nails and the strips are taken off the box (the hand-out, the job's end: a whole stroke, the knife down). |

While the blade's edge is anywhere below the upper layer's top (from 0.30 of a stroke before a whole stroke to
0.30 after it) nothing of the work moves along the feed; the steps happen with the edge at least 1.0 over the rods.

Every moving part:

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft (`entry`) | The vanilla axle | A1, A2 (keyed) | `rotate` x 1 (θ) |
| A1, A2 (`entrygears`) [drive] | The entry shaft | W1; the idler | `rotate` x 1 (θ) |
| W1 with its catch (`rectw1`) [drive] | A1 | The rectified shaft, when the axle turns + | `rotate` x −1 (θ) |
| Idler (`idler`) [drive], on its stud | A2 | W2 | `rotate` x −3/2 (θ) |
| W2 with its catch (`rectw2`) [drive] | The idler | The rectified shaft, when the axle turns − | `rotate` x 1 (θ) |
| Rectified shaft and its gear (`rectshaft`) [drive] | Whichever catch bites | The clutch gear | `rotate` x −1 (ψ) |
| Clutch gear and cup (`clutchgear`) [drive], loose on the crankshaft | The rectified shaft's gear | The cone, while it is in | `rotate` x 1 (ψ) |
| Cone (`cone`) [drive], feathered on the crankshaft | The cup (friction); its fork | The crankshaft | `gauge` slide x −0.5 (`present`); `rotate` x 2π (W) |
| Fork, shifter rod and knob (`clutchfork`) [drive] | The operator: in as a plate goes on, out after the hand-out | The cone | `gauge` slide x −0.5 (`present`) |
| Crankshaft and mutilated gear (`crankshaft`) [drive] | The cone | The great wheel (in its sector); the crank | `rotate` x 2π (W) |
| Crank disc and pin (`crank`) [shear] | Keyed on the crankshaft | The yoke | `rotate` x 2π (W) |
| Knife: blade, crosshead, ram, yoke (`knife`) [shear] | The crank pin in the yoke's slot | The nails (the cut) | `slide` y, amplitude 2, ratio 2π, phase −π/2 (W) |
| Bed knife (`bedknife`) [shear] | Fixed | — | none |
| Great wheel (`wheel`) [drive] | The mutilated gear | The top arbor (keyed) | the step: `rotate` x −π/5 a stroke (W), `gauge` rotate +π/5 W (scaled by p), and a `gauge` rotate −π/5 in each step's window |
| Top arbor (`rolltop`) [rolls], top pinion (`piniontop`) [pinions] | The great wheel | The bottom pinion; the plate | as the great wheel, about the top arbor |
| Bottom pinion (`pinionbottom`) [pinions], bottom arbor (`rollbottom`) [rolls] | The top pinion | The plate | the same, the other way, about the bottom arbor |
| Plate (`plate`) [plate] | The slitters | — | stretching `gauge` z about the nip, a quarter of it in each of steps 0..3 |
| Rods, the strips (`rodsl`, `rodsu`: the lower and the upper layer) [plate] | The slitters; then the push bar | — | stretching `gauge` z from the nip, 0.01 → 4 long, half in each of steps 0 and 1; `gauge` slide z +4 over the push; `gauge` slide y into the box over the fall; another stretch to 0 over 4.96..5 |
| Push bar: head, fingers, handle (`pusher`) [pusher] | The operator, by its handle | The plate's tail, then the strips' | four `gauge` slides z, 2 in each of steps 0..3; `gauge` slide z +4 over the push; `gauge` slide z −12 over 4.69..4.95 (a job's net motion nothing) |
| Nails (`nail1l`, `nail1u`, `nail2l`, `nail2u`: nail 1 or 2, lower or upper layer) [plate] | The rods (fed), the knife's edge (pressed, parted) | — | stretching `gauge` z, 0.006 → 2 long in their step; `gauge` slide z to the cut as it starts; six `gauge` slides y, −1/6 each, between the W at which the edge reaches each sixth of the nail's depth; `gauge` slide y down into the box over 0.3 from the parting; a stretch to 0 at the hand-out |
| Frame (`frame`) | Static | — | none |

The stepping parts' three kinds of driver add up to the steps (a `gauge` rotate per step) while the plate is on,
and are written so that at a whole stroke, where the steps and the straight term agree, p easing in or out turns
nothing: without that the rolls would wind back half a turn as the plate is taken away. The work's pieces are
authored as slivers (the rods 0.01 long at the nip, the nails 0.006 long inside the rods' first voxel) and grown by
stretching gauges, because a gauge is the identity at presence 0: as a plate goes on, only the plate shows.

### Regenerating

```sh
python3 mods-src/seraphhorizons/SlittingMill/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/SlittingMill/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/SlittingMill/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about two and a quarter minutes. `tools/validate_slittingmill.py` holds the checks; every run
checks its output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part, no part the renderer would never draw.
- **Containment:** nothing leaves the 2 × 2 × 2 box at 16 idle angles and over the job both ways.
- **Textures** by role: oak timber, the box and the axle's continuation; iron castings, wheels, the knife's
  crosshead, ram and yoke and the fork; steel collars, arbors, blades, pins, shafts and pinions; a cupronickel
  cone clutch; brass chocks; the work `metal`.
- **Anchors:** the entry shaft meets the power face at the power cell's centre; the output on the box's south
  face, the infeed the table's north end; the nip and knife anchors at the nip and the cut.
- **Gearing:** the rectifier's three meshes, the clutch gears and the pinions: centres at the sum of the pitch
  radii, the arcs rolled on the pitch circles equal (finite differences of the posed rig, the axle either way, the
  pinions over the job), no tooth of a pair running into the other's. The rectifier: whichever way the axle turns,
  exactly one loose wheel turns with the rectified shaft, always the same way.
- **Feed train:** the mutilated gear and the great wheel at their pitch radii; the wheel standing (within the
  rig's rounding) outside the steps and turning at the gears' ratio in them; the sector's teeth never in the
  wheel's over the job (every 0.0025 of a stroke); a step whole teeth of the wheel and as long as the sector takes
  to pass them; a job's half turn of the arbors repeating every pattern on them, the pinions and the wheel; the
  crankshaft back where it started at the job's end; p easing at the start and the end turning nothing.
- **Clutch:** the cone turns with the cup at the drawn pace, either way; 0.35 into the cup with a plate on, 0.15
  clear with none; the fork on the cone's neck.
- **Feed:** each arbor's rim at the nip carries the work south at the work's pace (2 a step) and stands between.
- **Slitters:** the cutters overlap; each layer of rods on its cutters' rims and 0.085 clear of the other
  arbor's spacers; every rod under the cutter of its layer.
- **Work:** the plate's length and front, the rods' front, each nail from the cut as it grows; nothing of a nail
  before its step; nothing left at the hand-out; no sliver of a rod or nail visible at presence 0 or 0.5; every
  0.025 of the job, both ways round.
- **Dwell** *(review)*: every 0.0025 of a stroke, both ways, while the blade's edge is below the upper layer's top
  nothing of the work moves along the feed; in the steps the edge is at least 0.3 over the rods (it is 1.0).
- **Parting** *(review)*: each nail of both layers, both nails: before the edge reaches it, it lies in its layer;
  from then its top follows the edge within 0.03 (it is 0.004); it falls free within a frame (1/60 of a stroke, an
  axle turn a second) of the edge going through its rod (it is within 0.002).
- **Push bar** *(review)*: its fingers' front against the plate's tail in every step and then against the strips'
  tails as it pushes them; it never reaches past the cut (so never into the knife's path); at rest against the plate's
  tail as laid at the start and again at the end (p easing there moves nothing); the strips' tails wholly past the cut
  when pushed, the knife's edge at least 0.3 over them throughout the push.
- **Removal** *(review)*: every 0.0025 of a stroke, both ways, any piece of the work that gets smaller does so only as
  the plate going into the nip (its front stays there) or at the hand-out, lying wholly inside the nail box and clear of
  the slitters, the knife, the bed knife, the push bar and every part of the frame but the box: nothing shrinks or
  vanishes inside the machine.
- **Knife:** the crank pin's centre in the middle of the yoke's slot and its ends inside it all round; the edge
  under the lower layer at the bottom, over the upper layer at the top, the crosshead never down to the rods; the
  knife at the bottom at both cuts; the blade behind the bed knife; the crosshead's ends in the housings' slots.
- **Supports:** the entry shaft in its inner post's bearing and the outer standard, the rectified shaft in its
  pedestal and the standard, the crankshaft the same, each arbor in its two chocks (inside each by a margin, at
  rest and a quarter stroke on); the idler on its stud; each screw through its cap onto its top chock; the fork's
  rod through its guide on the standard.
- **Clearances:** over 221 poses (16 idle angles, the job every 0.05 forward and 0.1 backward, every nail's pressing
  and fall in 13 steps, the clutch out) no two parts touch except the intended contacts in `ALLOWED` (shafts in
  bearings, keyed, feathered and loose parts, gears in mesh, the cone in its cup, the fork's rod in its guide, the
  crank pin in the yoke, the work on the table, in the slitters, on the bed knife and in the box, the push bar's fingers
  in the slitters' channels against the work's tails). The knife and the work are not in it: the blade never touches a
  rod, a strip or a nail.
- **Swept paths** (full runs): the same every 0.01 stroke forward, 0.02 backward, 36 idle angles and every nail's
  pressing and fall in 41 steps (988 poses).
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at seventeen poses, of what can be seen (`shown`:
  the work's slivers and the work with no plate on are left out).
- **Files:** every texture declared; lids over every column, no hollow cell; the power cell; the shipped model
  is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, leaving out the
work, as the other machines' are.

### Rig schema (`slittingmill-rig.json`)

The cylinder borer's schema (`../CylinderBorer/README.md`), with nothing added to the shared rig maths.

- **The work.** `{ "name": "knife strokes", "unit": "strokes", "step": 0.005, "end": { "thin": 5 } }`: one
  class, a plate, whatever its metal (the metal is the texture code `metal`, not a class). W counts knife strokes
  since the plate went in.
- **Inputs.** θ and ψ (the entry, the rectifier, the clutch gear and cup); W (everything behind the clutch), k (1
  with a plate on) and p (the clutch in).
- **`slit`**: `turnsPerStroke` (1: the drawn pace; the gameplay's pace should default to it, or a test hold the two
  together, as the gear cutter's `cut.turnsPerTooth` is), `steps` (W at each feed step's middle) and `stepWidth`
  (how long each lasts), `cuts` (3, 4: the whole strokes by which a nail has been parted off every rod), `handOut`
  (5), `push` and `pushBack` (W over which the push bar pushes the strips out and is drawn back), `rods` (8),
  `nailsPerRod` (2), `plate` and `output` (the item codes, `{metal}` the plate's).
- **No phase to keep.** Everything that cuts and feeds is a function of W; the axle's θ and ψ turn only what is
  before the clutch. Gameplay advances W with the shaft at its own pace and starts each plate at 0.

**Keys.** `cells`, `powerCell`, `powerFace`, `infeedSide`, `outputSide`, `output`, `plate`, `nip`, `knife`,
`work`, `slit` and `parts`. `requires` values: `rolls`, `pinions`, `drive`, `shear`, `pusher`, `plate`, or null.

**In the viewer** (`site/models.json`): the axle's slider (Play turns it and moves W with it, a stroke a turn);
the knife strokes' slider stops at 5; the size select names "None" and "Plate" (the viewer's size select offers
one class for a work quantity of one class: `topClass` in `site/src/lib/model-scenario.ts`); the plate's metal is
a texture select over eleven metals' ingot textures; the work shows only with a plate (`requiresClass`); the
presence slider throws the clutch.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `entrygears_*`,
`rectw1_*`, `idler_*`, `rectw2_*`, `rectshaft_*`, `clutchgear_*`, `cone_*`, `clutchfork_*`, `crankshaft_*`,
`crank_*`, `knife_*`, `bedknife_*`, `wheel_*`, `rolltop_*`, `rollbottom_*`, `piniontop_*`, `pinionbottom_*`,
`plate_*`, `rodsl_*`, `rodsu_*`, `pusher_*`, `nail1l_*`, `nail1u_*`, `nail2l_*`, `nail2u_*`, and `fr_*` for the frame. Hand edits are lost
when the script runs again: port them into `make_shape.py`, or stop regenerating.

## The cut and the feed

The first pass had the knife's crank on the axle's shaft and the rolls turning all the time: the rods fed
continuously, so for about a third of each stroke the lowered blade stood in rods that kept moving, and the nails
parted at whole strokes rather than when the blade went through them. The owner's review asked for the blade's
passage and the parting to be one instant, with no rod moving through a lowered blade. This pass:

- **Intermittent feed** (the owner's first choice, and how nail machines fed). The rolls dwell while the knife is
  down and advance while it is up. Their cause is a mutilated gear on the knife's crankshaft, three teeth driving
  the great wheel five teeth once a stroke, so the dwell is drawn, not just timed. Rotations and slides on θ are
  straight or sinusoidal and cannot stand still for a while, so the stepping is written in W, as gauges, and
  the whole cut and feed train is driven from W.
- **A cone clutch** joins the axle's half to it, as the draw bench's does: the rectified shaft turns the cup, the
  cone is feathered on the crankshaft and thrown in while a plate is on. Its smooth faces hide the difference between
  the drawn pace and gameplay's, so there is no longer a phase for gameplay to keep (the first pass needed each plate
  started at a whole turn of θ).
- **A sheared parting.** The bed knife holds the rod; the blade's edge comes down on the length past the cut and
  presses it down with it, its top following the edge through the rod's thickness in six straight pieces (within
  0.004), and it falls at the instant the edge is through. The upper layer parts first, then the lower.

The alternatives are kept below.

## The strips' way out

The second review found the strips taken off while they still lay in the slitters. Whatever the split, once the plate's
tail has gone through the nip the rolls push the rods no further, so with the knife behind the stand the last length of
every rod, from the nip to the cut, stays there unless something pushes it out or pulls it out. This pass pushes it:

- **The push bar** is the operator's follower: a head that runs between the feed table's side guides, a handle back to
  the table's north end, and eight fingers forward from it, each the section of the rod it pushes (a hair smaller) at that
  rod's layer. The lower layer's fingers run in four grooves in the table, under the plate; the upper layer's above the
  table. As laid, the plate's tail lies against the fingers' ends.
- **It follows the plate** a step at a time as the rolls draw it in, keeping it against them. When the plate's tail has gone
  through the nip, the fingers are at the rods' tails, in the rods' own channels between the collars.
- **It pushes the strips out** in one stroke's knife-up window (4.33..4.67, the edge at least 0.3 over the rods): the strips'
  length, so that their tails are at the cut, flush with the bed knife's back face, as a rod's front is before it is fed;
  the fingers stop there and never reach the knife's path. Off the bed knife, the strips fall into the nail box, onto the
  second nails and a shelf in the box beyond them, ahead of the knife coming down.
- **It is drawn back** while the knife comes down and before the job ends, so it is where it started for the next plate.
- **Only then** are the nails and the strips taken off, from the box.

## Open for the owner

1. **The strips' way out** is the operator's push bar (above): a hand-worked follower, its cause the operator, as the
   clutch's fork and the cylinder borer's crosshead handle are. Alternatives: a weighted follower (a weight on a cord,
   which needs room for the cord and its pulley that the table and the stand do not leave); take-off tongs behind the
   knife (they cannot grip the strips' ends, flush with the bed knife); a knife held clear while delivery rolls behind it
   take the strips (the crank cannot be stopped without a latch on the knife, and such rolls would take the nails too); or
   a split with no strips (the bar's fingers pushing each rod's last length on a nail at a time).
2. **The feed and cut mechanism** is intermittent feed by a mutilated gear behind a cone clutch (above). The
   alternatives considered: a rotary or flying shear (knife drums turning with the rods) on the old continuous
   feed, which needs no dwell but cuts the two layers of rods at different speeds across its arc; a short-dwell
   guillotine (a cam drawing a quick drop), which the existing `slide` driver can only draw as a sum of sines and
   which still meets moving rods; or a ratchet feed worked by the crank, which is the period's way but whose wheel
   follows a pawl's sine, which no driver draws stepwise.
3. **Nails and strips** are split by geometry: two 2-voxel nails and one 4-voxel strip a rod, eight rods a plate.
   The game's item is one stack of nails and strips; how many a plate makes is gameplay's. The nails are headless
   cut lengths; a header ram after the cut is another option.
4. **The pace** is drawn at one axle turn a stroke, five a plate; gameplay may set its own, and only the clutch's
   smooth faces would see the difference. The knife and the rolls stand when no plate is on (the clutch out).
5. **The rectifier** (five toothed wheels) is there so the clutch's cup always turns the same way. If the mill may
   refuse a reversed axle, the cup could go on the entry shaft.
6. **Footprint and power:** 2 × 2 × 2, the axle a block up at the near end's east face. The drive on the east side
   (entry, rectifier, clutch, crankshaft) is busier than the first pass's; the upper cells hold the drive's top,
   the screws and the knife's yoke.
7. **One class**: a plate of any metal, the metal a texture code. To keep the viewer's select to "None" and
   "Plate" I changed the viewer to offer one class for a work of one class (`topClass`, with a test and the docs);
   two classes (say soft and hard metals) would avoid that change but double the work's parts.
8. **The plate** is drawn 8 × 8, as the squaring shear lays it; the game's plate item is 9 × 9.
9. **No flatting or pinch rolls**; the slitters take the plate straight from the table.
10. **The `requires` ids** (`rolls`, `pinions`, `drive`, `shear`, `pusher`) are provisional; the stages and their items are
   still to be decided with the owner.
11. **Renderer rule (later):** the work should be drawn only while a plate is on, not while p eases out after the
    hand-out (as the squaring shear does), or the plate would grow back on the table as p falls.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files; the standalone viewer was built
but not seen with WebGL. No one has looked at it in a client.

1. **The wheel turns a hair before and after its teeth are driven.** It turns for five pitches of the crankshaft,
   while some tooth of the sector is inside its teeth; the first and last of those pitches have a tooth entering or
   leaving, not pushing.
2. **No locking arc.** A real mutilated gear holds the driven wheel between steps with a locking rim; here nothing
   holds the great wheel but the pinions' load.
3. **The rods jump layers at the nip.** The plate is fed at the pass line and each rod is a straight box in its
   layer, 0.68 above or below, from the nip on; nothing draws the bend between.
4. **Growth by stretching.** The rods and nails grow by scaling slivers; their textures are mapped for their full
   length, so a growing piece's texture is squeezed until it is full grown.
5. **The nails fall at a steady speed**, straight down, and lie in two neat layers in the box; the length of rod
   pressed down with a nail is the nail itself (no burr or kerf).
6. **Box teeth, 12-gon cutters**; the cutters' corners dip up to 0.12 into the rods as they turn.
7. **The catches** are drawn as a lug on each loose wheel's hub, not as a ratchet and pawl.
8. **The push bar outruns the rolls** in the last step: it pushes the strips their whole length while the rolls turn a
   nail's length, so the strips slide between the collars' faces. Its fingers are the rods' section a hair smaller, and
   run in the channels the rods leave, against the rods' tails; nothing draws the operator.
9. **The strips land flat** on the second nails and the box's shelf, all eight side by side, and fall at a steady speed.
10. **The plate's front enters the cutters' bite** before the nip (the cutters reach above and below the pass line
   there): the plate is drawn flat through them.
