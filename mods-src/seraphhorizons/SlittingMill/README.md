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
knife, is left whole: the **strips**. The machine's **push bar** pushes them out past the knife (its fingers
run in the rods' own channels between the collars), and they fall into the nail box with the nails. The bar
is driven by the feed train: a **push sector** on the top arbor, two runs of teeth, turns the **push
shaft** through two of the feed steps, and the shaft's pinion drives a **rack** on the bar, first up behind
the plate's tail and then through the nip with the strips; the shaft's drum winds a **return weight** up as
it goes, and once the sector's run has left the shaft's gear the weight runs the bar back to its stop.

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
standard, the feed table and its guides, the nail box, the push shaft's post and bracket, the bar's stop,
the rack's hold-down, and the entry shaft):

| `requires` | Draws |
|---|---|
| `rolls` | The slitters: both arbors, each with five steel cutters and five spacers, their necks and collars |
| `pinions` | The two equal spur gears (16 teeth, module 0.375) on the arbors' west necks |
| `drive` | The rectifier (the entry's two pinions, the two loose wheels with their catches, the idler), the rectified shaft and its gear, the clutch gear with the cup, the cone and its fork, the crankshaft with its mutilated gear, the great wheel |
| `shear` | The crank disc and its pin, the knife (blade, crosshead, ram and yoke) and the bed knife |
| `pusher` | The push bar (its head between the table's guides, eight fingers, one a rod at its layer, an upright and the rack over the stand) and its drive: the push sector on the top arbor, the push shaft with its gear, pinion and drum, the return weight and its chain |
| `plate` | The work: the plate, its rods (the strips) and its nails, all wearing the texture code `metal` |

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. Textures are the game's, referenced: debarked oak, iron plate, plain steel sheet, brass
sheet for the chocks, cupronickel sheet for the cone clutch (the draw bench's), and the work's `metal`
(`game:block/metal/ingot/iron` in the shape; a renderer would set it to the plate's metal, as the draw bench
sets `die`). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/slittingmill.json` | The whole machine, every moving part and the work (738 elements). |
| `assets/seraphhorizons/shapes/block/slittingmill_frame.json` | The static frame only (72 elements). |
| `assets/seraphhorizons/config/slittingmill-rig.json` | Footprint, anchors, the work, the slitting's figures (`slit`) and the part rig (31 parts). |
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
| Push sector on the top arbor (two runs of five teeth on a 40-tooth pitch, module 0.3) → push gear on the push shaft | 40 : 8 while a run is round | the push shaft +180° in step 3, +90° in the first half of step 4 |
| Push pinion on the push shaft (module 1/π) → the bar's rack (pitch 1) | 16 teeth | the bar 8 south in step 3, 4 more in step 4 |
| Push shaft's drum (radius 0.8) → the return weight's chain | | the weight up 3.77; down again, turning the shaft back, once the run has let go |

At the drawn pace, one axle turn a stroke (`slit.turnsPerStroke`), the cone and the cup turn together; the
cone's smooth faces are where the axle's half meets the half the work drives, so a gameplay pace other than the
drawn one, or the cup's phase, shows nowhere (the draw bench's arrangement). The crankshaft is at the same angle
at every whole stroke. A step turns the cutters' rims (radius 3.18) a nail's length, 2 voxels; a job's five steps
turn the arbors half a turn, which every pattern on them, on the pinions and on the wheel repeats in, so the next
plate's W starting again at 0 moves nothing. Toothed wheels and what they mesh (nothing toothed meshes nothing):
A1 ↔ W1; A2 ↔ idler ↔ W2; the rectified shaft's gear ↔ the clutch gear; the mutilated gear ↔ the great wheel
(while its sector is round); the two pinions; the push sector ↔ the push gear (while a run is round); the push
pinion ↔ the bar's rack. The collars, the cup and the cone, the crank disc, the drum and the hubs are plain.

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

**The push bar and its drive.** The bar is a head that slides on the feed table between its guides, eight fingers
forward from it (each the section of the rod it pushes, a hair smaller, at that rod's layer: the lower ones run in
grooves in the table, all of them through the slitters' channels), and an upright from the head, beside the
pinion, to the **rack**, a bar along the feed (x 12.4..14.3, y 19.8..20.5) whose teeth hang down onto the **push
pinion**. At rest its head lies against an iron stop on the table and its fingers' front against the plate's tail
as laid (z 12). The **push shaft** (y 16.82, z 13.9) runs over the table's north half from its post at the west
end, through a bracket on the west housing's front post, to the pinion under the rack; the rack runs under a hold-
down on an arm from the west cap, beside the pinion, and over the top roll and the knife's crosshead. The shaft's
**push gear** (8 teeth) at the west end meets the **push sector** on the top arbor's west neck: a plate wheel cut
back clear of the gear's teeth, with two runs of five teeth on the pitch of a 40, half a turn apart (the arbors
turn half a turn a job, so the next plate meets the other run). The sector steps with the top arbor; a run is
round for the fourth feed step (3.40..3.60) and the first half of the fifth (4.40..4.50), and turns the gear five
times its own turn: half a turn, then a quarter. The pinion's pitch radius is 2.55, so the bar goes 8 south in the
fourth step, catching up with the plate's tail (which goes 2) and meeting it at the nip as the step ends, stands
with it while the knife comes down for the second nails, and goes 4 more in the fifth step's first half with the
strips before it, their tails then at the cut. Out of the run, the rest of the sector is a plain plate clear of
the gear's teeth, and the gear stands. The shaft's **drum** (radius 0.8) at its west end winds the **return
weight**'s chain up as the shaft turns (the weight rises 3.77). Once the run's last tooth has left the gear (just
after the push, 0.1 clear by 4.51) nothing holds the shaft: the weight runs it back (4.54..4.80, the arbor
finishing its step meanwhile with the sector's teeth at least 0.48 clear of the gear's), the pinion running the
bar back with it, until the bar's head is against its stop again. The rack is in mesh with the pinion all the way,
and every motion of the bar has its cause: the sector through the gear going, the weight through the drum coming
back.

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
| 3.4..3.6 | Step 3: the second nails come through; the plate's tail goes into the nip. A run of the push sector is round: the push bar is driven 8 south behind the plate's tail and meets it at the nip as the step ends. |
| 3.70..3.91 | The second nails are pressed through and fall, the upper layer then the lower; the bar stands at the nip. |
| 4.40..4.50 | Step 4's first half, the knife up: the sector drives the bar 4 on, pushing the strips out their length, all of each wholly past the cut. |
| 4.50..4.54 | The run's last tooth leaves the push gear (0.1 clear of it by 4.51), the arbors still finishing step 4; the bar stands at the cut. |
| 4.50..4.72 | Past the bed knife, the strips fall into the box, onto the second nails and the box's shelf, ahead of the knife coming down. |
| 4.54..4.80 | The return weight runs the push shaft back, and the bar back through the slitters' channels and along the table's grooves to its stop. |
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
| Fork, shifter rod and knob (`clutchfork`) [drive] | The game: in as a plate goes on, out after the hand-out (with the plate's placing, the only cause from outside the machine) | The cone | `gauge` slide x −0.5 (`present`) |
| Crankshaft and mutilated gear (`crankshaft`) [drive] | The cone | The great wheel (in its sector); the crank | `rotate` x 2π (W) |
| Crank disc and pin (`crank`) [shear] | Keyed on the crankshaft | The yoke | `rotate` x 2π (W) |
| Knife: blade, crosshead, ram, yoke (`knife`) [shear] | The crank pin in the yoke's slot | The nails (the cut) | `slide` y, amplitude 2, ratio 2π, phase −π/2 (W) |
| Bed knife (`bedknife`) [shear] | Fixed | — | none |
| Great wheel (`wheel`) [drive] | The mutilated gear | The top arbor (keyed) | the step: `rotate` x −π/5 a stroke (W), `gauge` rotate +π/5 W (scaled by p), and a `gauge` rotate −π/5 in each step's window |
| Top arbor (`rolltop`) [rolls], top pinion (`piniontop`) [pinions] | The great wheel | The bottom pinion; the plate; the push sector | as the great wheel, about the top arbor |
| Bottom pinion (`pinionbottom`) [pinions], bottom arbor (`rollbottom`) [rolls] | The top pinion | The plate | the same, the other way, about the bottom arbor |
| Plate (`plate`) [plate] | The slitters | — | stretching `gauge` z about the nip, a quarter of it in each of steps 0..3 |
| Rods, the strips (`rodsl`, `rodsu`: the lower and the upper layer) [plate] | The slitters; then the push bar's fingers; they fall once past the bed knife | — | stretching `gauge` z from the nip, 0.01 → 4 long, half in each of steps 0 and 1; `gauge` slide z +4 over the push; `gauge` slide y into the box over the fall; another stretch to 0 over 4.96..5 |
| Push sector (`pushsector`) [pusher] | Keyed on the top arbor | The push gear, while a run is round | as the top arbor |
| Push shaft: gear, pinion, drum (`pushshaft`) [pusher] | The push sector, going; the return weight, coming back | The bar's rack; the weight's chain | `gauge` rotate x +π over step 3, +π/2 over 4.40..4.50, −3π/2 over 4.54..4.80 (a job's net turn nothing) |
| Push bar: head, fingers, upright, rack (`pusher`) [pusher] | The push pinion, through its rack | The strips' tails | `gauge` slide z +8 over step 3, +4 over 4.40..4.50, −12 over 4.54..4.80: the pinion's pitch radius times its turn |
| Return weight (`pushweight`) [pusher] | The drum, through its chain, going up; its own weight coming down | The push shaft, through the chain and drum, coming back | `gauge` slides y: the drum's radius times its turn |
| Its chain (`pushchain`) [pusher] | The drum, winding it on and off | — | stretching `gauge` y from the drum's side, its windows adding up to the drum's turn |
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

A full run takes about three minutes. `tools/validate_slittingmill.py` holds the checks; every run
checks its output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part, no part the renderer would never draw.
- **Containment:** nothing leaves the 2 × 2 × 2 box at 16 idle angles and over the job both ways.
- **Textures** by role: oak timber, the box and the axle's continuation; iron castings, wheels, the knife's
  crosshead, ram and yoke, the fork, the push bar, its sector, drum, weight and chain; steel collars, arbors, blades,
  pins, shafts, gears and pinions; a cupronickel cone clutch; brass chocks; the work `metal`.
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
- **Push bar** *(review)*: every 0.005 of a stroke, both ways: at rest against the plate's tail as laid until the
  catch-up; in the catch-up step never ahead of the plate's tail, meeting it at the nip as the step ends; standing
  there until the push; its fingers' front on the strips' tails all through the push; standing at the cut until the
  return, and at rest again after it; it never reaches past the cut (so never into the knife's path); at rest at the
  start and the end (p easing there moves nothing); the strips' tails wholly past the cut when pushed, the knife's edge
  at least 0.05 over them throughout the push (it is 0.99); the strips fall only once the push is done.
- **Push drive** *(review)*: the push sector and the push gear at their pitch radii; every 0.0025 of a stroke over the
  job, the push shaft turns forward only at the sector's ratio with a tooth of the run within 0.05 of a gear tooth (it is
  within 0.012), and back only with the sector's teeth more than 0.1 clear of the gear's (they are at least 0.48 clear); no
  tooth of the sector or its plate runs into the gear's; the bar goes the pinion's pitch radius times its turn, with a
  pinion tooth within 0.05 of a rack tooth wherever it moves (0.012), no tooth of either into the other; the weight
  goes the drum's radius times its turn, and its chain reaches from the drum's side to the weight's top all the way.
- **Causes** *(review)*: every moving part has a mechanical cause traceable to the axle or to the work's own motion
  through drawn parts (`causes()` in `make_shape.py`: part, cause, how). Over the axle turning either way with no
  plate, the job forward every 0.0025 and backward every 0.01, and p easing in and out at its ends, every change in a
  part's pose is accounted for by one of its causes, each link held: keyed parts turn with their shafts, meshing gears
  roll their pitch circles, the catches and the clutch carry their shafts, a sector turns its gear only with a tooth in
  contact (the great wheel's too), the rack goes with its pinion with a tooth in contact, the weight and its chain go
  with the drum, and the weight runs the push shaft back only with the sector's teeth clear of its gear. The work's
  links (fed by the rolls, pushed by the fingers, pressed by the knife, falling once let go) are held by the work,
  push bar and parting checks, which must pass. Every moving part reaches the axle through its causes; the only
  causes from outside are the clutch fork (the game's) and the plate's placing (the player's).
- **Removal** *(review)*: every 0.0025 of a stroke, both ways, any piece of the work that gets smaller does so only as
  the plate going into the nip (its front stays there) or at the hand-out, lying wholly inside the nail box and clear of
  the slitters, the knife, the bed knife, the push bar and every part of the frame but the box: nothing shrinks or
  vanishes inside the machine.
- **Knife:** the crank pin's centre in the middle of the yoke's slot and its ends inside it all round; the edge
  under the lower layer at the bottom, over the upper layer at the top, the crosshead never down to the rods; the
  knife at the bottom at both cuts; the blade behind the bed knife; the crosshead's ends in the housings' slots.
- **Supports:** the entry shaft in its inner post's bearing and the outer standard, the rectified shaft in its
  pedestal and the standard, the crankshaft the same, each arbor in its two chocks, the push shaft in its post's
  bearing and its bracket (inside each by a margin, at rest and a quarter stroke on); the idler on its stud; each
  screw through its cap onto its top chock; the fork's rod through its guide on the standard.
- **Clearances:** over 221 poses (16 idle angles, the job every 0.05 forward and 0.1 backward, every nail's pressing
  and fall in 13 steps, the clutch out) no two parts touch except the intended contacts in `ALLOWED` (shafts in
  bearings, keyed, feathered and loose parts, gears in mesh, the cone in its cup, the fork's rod in its guide, the
  crank pin in the yoke, the work on the table, in the slitters, on the bed knife and in the box, the push bar's fingers
  in the slitters' channels against the work's tails, the push sector on its neck, the push shaft in its bearings, the
  push drive's meshes, the chain on its drum and its weight). The knife and the work are not in it: the blade never
  touches a rod, a strip or a nail.
- **Swept paths** (full runs): the same every 0.01 stroke forward, 0.02 backward, 36 idle angles and every nail's
  pressing and fall in 41 steps (988 poses).
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at nineteen poses, of what can be seen (`shown`:
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
  (5), `catchUp`, `push` and `pushBack` (W over which the push sector drives the push bar up to the plate's tail,
  then on with the strips past the cut, and over which the return weight runs it back), `rods` (8),
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
`plate_*`, `rodsl_*`, `rodsu_*`, `pushsector_*`, `pushshaft_*`, `pusher_*`, `pushweight_*`, `pushchain_*`, `nail1l_*`,
`nail1u_*`, `nail2l_*`, `nail2u_*`, and `fr_*` for the frame. Hand edits are lost
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
every rod, from the nip to the cut, stays there unless something pushes it out or pulls it out. The second pass pushed
it with a push bar worked by hand. The third review would not have an operator: the bar must be the machine's, with a
visible cause for all of its motion. This pass drives it from the feed train:

- **The push bar**: a head that runs between the feed table's side guides, eight fingers forward from it, each the
  section of the rod it pushes (a hair smaller) at that rod's layer (the lower layer's in four grooves in the table, the
  upper layer's above it), and an upright to a rack over the stand. As laid, the plate's tail lies against the fingers'
  ends; the head lies against its stop.
- **The strips must cross the knife's plane in one knife-up window**, or the knife would come down on them and cut them
  into nails: 4 in less than 0.39 of a stroke, while the rolls carry the work 2 a step. So the bar cannot simply go at
  the rolls' pace. It is driven at four times their pace, 8 a step, by a sector on the top arbor turning a gear five
  times as fast, whose shaft's pinion drives the rack; and only while one of the sector's two runs is round. It stands
  through the first three steps, catches up with the plate's tail in the fourth (it goes 8 while the tail goes 2, and
  meets it at the nip as the step ends), stands at the nip while the knife cuts the second nails, and in the first half
  of the fifth pushes the strips their length, so that their tails are at the cut, flush with the bed knife's back face,
  as a rod's front is before it is fed. The fingers stop there and never reach the knife's path. Off the bed knife, the
  strips fall into the nail box, onto the second nails and a shelf in the box beyond them, ahead of the knife coming down.
- **The push ends half a step early** so the run's last tooth can leave the push gear while the arbor still turns: a
  gear held by a tooth in its gap cannot be turned back. Once the tooth is clear the gear, its shaft and the bar are free.
- **The return weight runs it back.** The push shaft's drum wound the weight's chain up as it turned; now the weight
  comes down, turning the shaft back, and the pinion runs the bar back through the slitters' channels and along the
  table's grooves to its stop, before the job ends, so it is where it started for the next plate. The next plate meets
  the sector's other run, half a turn on, in the same place.
- **Only then** are the nails and the strips taken off, from the box.

## Open for the owner

1. **The strips' way out** is the machine's push bar (above): a rack and pinion off the feed train, a sector on the top
   arbor turning the push shaft through the catch-up and the push, and a return weight on the shaft's drum. The
   alternatives considered: a crank or cam lever off the crankshaft (it turns every stroke, so it would push every
   stroke; once a job needs a counting shaft, and a lever's arc cannot be drawn exactly with the existing drivers);
   pushing at the rolls' pace (the strips would straddle the knife's plane as it comes down and be cut into nails);
   driving the return as well as the push from the arbors (they turn only in the five steps, and the push takes the
   last, so the return would need a longer job of ten strokes, the arbors' whole turn); or no push at all (the rolls
   cannot carry a tail past the nip, so there would be no strips). The bar now stands through the first three steps
   instead of following the plate, and the job is still five strokes.
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
10. **The `requires` ids** (`rolls`, `pinions`, `drive`, `shear`, `pusher`; the push drive is under `pusher`) are provisional; the stages and their items are
   still to be decided with the owner.
11. **Renderer rule (later):** the work should be drawn only while a plate is on, not while p eases out after the
    hand-out (as the squaring shear does), or the plate would grow back on the table as p falls.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files; the standalone viewer was built
but not seen with WebGL. No one has looked at it in a client.

1. **The mutilated gear's box teeth overlap the wheel's** by up to 0.06 as they drive it, and by 0.04 as a step
   starts and ends, the wheel standing: in contact all through the step, as the cause check asks, but not involutes.
2. **No locking arc.** A real mutilated gear holds the driven wheel between steps with a locking rim; here nothing
   holds the great wheel but the friction of the arbors in their chocks under the screws. Between the catch-up and the
   push the return weight pulls on the train through the push drive and that friction is all that holds it; a locking
   rim, or a pawl on the great wheel, would make it positive.
3. **The rods jump layers at the nip.** The plate is fed at the pass line and each rod is a straight box in its
   layer, 0.68 above or below, from the nip on; nothing draws the bend between.
4. **Growth by stretching.** The rods and nails grow by scaling slivers; their textures are mapped for their full
   length, so a growing piece's texture is squeezed until it is full grown.
5. **The nails fall at a steady speed**, straight down, and lie in two neat layers in the box; the length of rod
   pressed down with a nail is the nail itself (no burr or kerf).
6. **Box teeth, 12-gon cutters**; the cutters' corners dip up to 0.12 into the rods as they turn.
7. **The catches** are drawn as a lug on each loose wheel's hub, not as a ratchet and pawl.
8. **The push bar outruns the rolls** in the catch-up and the push: it goes four times their pace, so the strips slide
   between the collars' faces as it pushes them. Its fingers are the rods' section a hair smaller, and run in the
   channels the rods leave, against the rods' tails.
9. **The strips land flat** on the second nails and the box's shelf, all eight side by side, and fall at a steady speed.
10. **The plate's front enters the cutters' bite** before the nip (the cutters reach above and below the pass line
   there): the plate is drawn flat through them.
11. **The push drive's teeth** are boxes like every gear's here, narrowed to 0.85 of half the pitch for backlash: a
   driving tooth is within 0.012 of the tooth it drives throughout, but no flank is an involute. The sector's two runs
   of five teeth stand on a plain plate, and the push gear has eight teeth.
12. **The return weight** is a plain iron block on a chain drawn as one bar (no links), wound straight on the drum; it
   comes down at a steady speed, and nothing draws the bar's head bumping its stop.
