# Mandrel forging station

Part of the Seraph Horizons mod (`../README.md`): a rung of the pipe ladder (unified pipes, `../Pipes/`).
A smith's **mandrel station** of the 1700s hammers one lead or copper **hollow section** (vanilla's
chute section, named the tube blank in game, `game:chutesection-lead` or `-copper`: an 8 × 8 square box, 8 long, 1-voxel walls) down
onto an iron mandrel into a pipe-sized tube, which comes off as two **pipe sections**
(`seraphhorizons:pipesection-{lead,copper}`: a square tube 6 across, 1-voxel walls, 8 long, half a
block). One hollow makes two sections; a section with solder makes a straight pipe.

**Its second job winds a coil spring** on the same mandrel: one hot **steel rod** (`game:rod-steel`) is
laid along the fixed mandrel and wound round it from the shoulder by hand, with tongs, into one **spring**
(`seraphhorizons:spring-steel`), a square coil of six turns that follows the square mandrel. The draw bench's follower spring is this spring
(`../DrawBench/README.md`). The model of that job is in "The spring job"; its gameplay
is in "Gameplay: the spring job".

This folder holds the model's generator and its rig (`tools/`); the gameplay is built against the rig
("Gameplay" below).

**A hand station.** It has no mechanical power, no power cell and no oil. The blows are the player's
right-clicks with a hammer, as at the anvil; no hammer is modelled, since the player holds it. The rig's
θ is the hammer's clock: every motion is posed by the forging W, and θ moves nothing. A spring is wound
with right-click held, as the press brake is worked; θ is then that clock, and moves nothing either.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines. This one
added `machinegen/coil.py` (the square coil spring, which the draw bench draws too), and is the first to use the
rig's stretching gauge (every motion is a `gauge`; each side grows and the rod shrinks by one). It needed the rig maths' classes past
thick: the spring is a job of its own, class 3 (`docs/recipe-browser/models.md`, "Classes").

## The station

**Footprint.** 2 cells: 1 wide (x), 1 high (y), 2 long (z). In the native, south-facing frame with
the controller at `[0,0,0]`: x 0, y 0, z 0..1. The controller is the stump, `[0,0,0]`, the block the
player clicks. Placed like the press brake and the draw bench, the station extends away from the
player along their line of sight, the stump nearest them and the mandrel pointing away: the block's
`side` is the way they look. When a hollow is done, gameplay drops its two sections beyond the tip
(native south, `outputSide`), onto the ground at `output.pos`. The model draws nothing coming off.

**Why 1 × 1 × 2.** The finished tube is 16 long (two sections) on a mandrel that also needs a root
held in a bracket: the hollow starts against the shoulder at z 7 and the finished tube ends at z 23, half a
voxel short of the tip at 23.5. `output.pos` is on the ground beyond the tip (z 28), where the sections are
dropped.

**Parts, by build order.** The block itself (`seraphhorizons:mandrelstation-frame-{side}`, its recipe
gameplay's) is the frame: the oak stump with its iron hoop, the iron bracket and the swage. Then one
item:

| `requires` | Item | Draws |
|---|---|---|
| `mandrel` | `game:rod-iron`, `-meteoriciron` or `-steel` | the mandrel: root in the bracket, collar (the shoulder), square body, tapered nose |
| `hollowlead` | the work, a lead hollow (k 1) | the hollow and the two lead sections it becomes |
| `hollowcopper` | the work, a copper hollow (k 2) | the same in copper |
| `rodsteel` | the work, a hot steel rod (k 3) | the rod and the spring it is wound into, drawn hot |

Why one stage: the bracket and the swage are forged with the station, as the brake's gallows are; the
mandrel is a bar, the game's bar is the rod, and it is the part a smith would take out and swap.

## Model

`assets/seraphhorizons/shapes/block/mandrelstation.json` (the whole station, every moving part) and
`mandrelstation_frame.json` (the frame only: the block draws it, the inventory shows it), the rig
`assets/seraphhorizons/config/mandrelstation-rig.json`, the reference poses
`tests/MandrelStation/rig-reference.json` and the spring item's shape
`assets/seraphhorizons/shapes/item/spring.json` (the coil as the station leaves it wound). All five are
written by `tools/make_shape.py`; nothing is taken from another mod's model. The coil's geometry is
`Machines/tools/machinegen/coil.py`, which the draw bench's follower spring is drawn by too.

### How the station works

| Part | Driven by | Drives |
|---|---|---|
| The stump (oak, iron hoop) | fixed | carries the bracket and the swage |
| The bracket (base plate; two bands, each a saddle, two cheeks and a cap) | fixed to the stump | holds the mandrel's root at two places, against the cantilever |
| The mandrel (`mandrel`) | fixed in the bracket | takes the blows: the hollow is hammered onto it |
| The swage (a block with a square groove) | fixed | none: a tool beside the mandrel, not animated |
| The hollow (eight rings `1`..`8`: four walls and four corner bars each) | the blows, the mandrel under them | closes from 8 to 6 across onto the mandrel and stretches from 8 to 16 long, evenly, in one motion over the whole work; at W 1 it stays on the mandrel (rings 1..4 and 5..8 are the two sections) |
| The spring's sides (`sp1u`..`sp6e`: turn 1..6, face `u` top, `w` west, `d` bottom, `e` east) | the smith (with tongs, not modelled), round the mandrel's corners | each one bar growing from the corner where the side before ends, along its own line across the face (a stretch about its corner), its leading end inside the rod's end; whole from then on |
| The straight rod (`spr`) | the smith | lies along the mandrel on the coil's corner line, the coil's length, its far end never moving; shrinks into the coil a wire's width a side from its shoulder end (a stretch about its far face) while it moves across each face with the growing side's leading end: round the mandrel a face a side; gone as the last side ends |

The forging (t = W, one hollow; the rig holds none of these times):

| t | |
|---|---|
| 0 | the hollow lies on the mandrel against the shoulder, 8 × 8, 8 long (p eases in) |
| 0..1 | forged: it closes from 8 to 6 across onto the mandrel and stretches from 8 to 16 long, evenly along its whole length, together, in proportion to W |
| 1 | one 6 × 6 tube, 16 long, its far end at the tip, the part line between rings 4 and 5 showing; it stays on the mandrel. Gameplay delivers the two sections and stops drawing the work; nothing slides or drops |

**How it closes and stretches at once.** No rig driver scales a part, so the hollow is drawn as eight
short rings (2.1 long) along the mandrel, each four walls 6 wide and four 1 × 1 corner bars. At rest the
rings overlap, filling the hollow's 8; over the forging every ring's walls slide in by 1 and its corner
bars diagonally by 1 on both axes (so the cross-section goes from 8 across to 6, its bore from 6 to the
mandrel's 4), while each ring slides along the axis from its place at rest to its place in the forged
tube: the first not at all, the last 8, the rest in proportion to their place in their section. All of
it is one gauge window, linear in W, so the tube closes and lengthens together and evenly. The forged
tube is two sections of four rings each, the rings in a section overlapping a little (0.13) and the two
sections meeting end to end at the part line (where a ring's side walls and corner bars stand back at
their ends, a hairline shows there at W 1). At every tenth of W the work is checked to be one
continuous square tube of even cross-section, with no gap between rings; the volumes where rings, walls
and corner bars overlap are never seen.

**One sheet down the tube.** The rings' faces along the length (the walls' and corner bars' sides, top
and bottom, inside and out) take their UVs from one sheet of the metal's texture running the length of
the work: each ring's continues where the one before it ends, and across the section each face takes
the part of the sheet where it stands, so walls and corner bars meet without a seam (the end faces keep
their own). A ring is rigid and slides, so the sheet can run on exactly from ring to ring at one W only;
elsewhere two rings' textures are off by 4 texture units a voxel times how far their spacing has moved
since. It is mapped by **W 0.5** (`W_BAND`), the rings' places halfway through the stretch, not by the
rest or W 1: the offset at either end is then half what a mapping by one end leaves at the other, 2.25
texture units (about a pixel of the 32-pixel sheet) between neighbouring rings at rest and at W 1, and
none at W 0.5. Mapped by the rest it would be none at rest and 4.5 at W 1. So no seams show by design;
the one line along the work is the part line between the two sections.

**It hangs on the mandrel.** A loose box on a bar hangs from it: at every W the bore's ceiling (the top
wall's underside) rests on the bar's top face, and the gap is below the bar, never round it. At rest the
6 bore on the 4 bar puts the box's centre 1 below the mandrel's axis (y 12.6; the box's bottom 0.6 over
the stump). As it closes, the top wall stays on the bar, the side walls come in by 1 and the bottom wall
comes up by 2, so the section's centre rises by 1 with the forging, and the finished tube's bore closes
round the bar. Checked at every tenth of W: the lowest ceiling of the ring stack on the bar's top within
0.01 (each ring's within the z-fighting fix's steps, 0.09 at most), and no element of the work into the
bar by more than 0.01.

**Faces that share a plane.** The overlapping rings' outer faces lie in one plane all the time, so the
z-fighting fix steps them in by 0.015 each, up to 0.105 deep for a stack of eight; no end faces share a
plane (a ring's side walls stand back 0.03 at its ends and its corner bars 0.06), so the joints between
rings are not opened by the fix.

### The spring job

**The spring.** A square coil (`Machines/tools/machinegen/coil.py`, `Coil`) round the 4 × 4 mandrel: six
turns of square wire 0.6 across, wound free at a pitch of 2.4, a right-hand helix (seen from the shoulder its
corners go top east, top west, bottom west, bottom east). It is 24 straight sides, four a turn, each running
along one face from corner to corner of the coil's centre line (a square 4.6 across) and rising 0.6, so it
leans 7.4°; each is the centre line's length (4.64) and the wire's width long, so neighbouring sides fill the
corner between them. Outside it is 5.2 square (the collar's size) and 15.1 long, from the shoulder (its first
side 0.1 off it) to 0.1 short of the nose. Six turns of 0.6 wire are 40 voxels of steel, as the game's rod
(2 × 2 × 10) is. The spring item's shape is this coil as the station leaves it (`spring.json`, 5.3 × 5.3 ×
15.1, along z, centred on the item box's floor, at scale 1).

**How it is wound.** The mandrel stands still, so the rod is wound round it the way the owner pictured it: laid
straight along the mandrel, from the shoulder to near the coil's far end, the finished spring's length, and wound up
from its shoulder end, turn by turn, the straight part used up as the coils form; each side grows from the corner
where the side before ends, along its own line across the face, as wire laid down, and the rod shrinks into it. The
two motions are the rig's stretching gauge (`docs/recipe-browser/models.md`, "Stretching gauges": a part scaled along
one of its own axes about a plane, by the work), which this model is the first to use; everything else is slides.
The wire's real length is not kept (the coil is 111 voxels of wire, the straight rod 14.4): the rod loses only a
wire's width (0.6) to each side. The plan is `make_shape.py`'s `plan()`; W is cut into 24 equal **slots**, one a
side, and 25 parts move:

- **A side** (`sp1u` .. `sp6e`) is one bar, written where the coil puts it, with one stretch along its own axis
  about its start corner: at nothing (and not drawn) until its slot, grown over the slot at one speed, whole from
  then on. So it grows from the corner where the side before ends, along its own line, its leading end moving
  steadily across the face; its back end (half a wire behind the corner, the joint) grows with it, inside the end of
  the side before, and its leading end is inside the rod's end throughout. The first side has no side before it, so it
  grows about its start face instead, forward only: that face is the wire's free end, uncovered where it lies, half a
  wire east of the first corner, as the rod moves on from it.
- **The rod** (`spr`) is one bar, 0.615 square, written as it lies at W 0: along the mandrel on the coil's first
  corner line, from 0.35 before the coil's first corner to 0.35 before its last (14.4, the coil's length). It has a
  stretch along the mandrel about its far end face, a wire's width a side over the whole job (so it is gone as the
  last side ends), then 24 slides, one a slot, across the face that slot's side lies on: so its shoulder end rides
  with the growing side's leading end, 0.35 before it along the mandrel, the bend of the wire being where the side
  enters the rod's end, and the rod goes round the mandrel a face a side, never stopping, its far end never moving.

So the wire visibly goes from the rod into the coil at the moving bend, the coil grows from the shoulder a pitch a
turn, and the rod is used up into it from the shoulder end until nothing of it is left. No instants, nothing waiting
inside the mandrel, nothing swapped: 25 parts, 49 gauges.

At W 0 the rod alone can be seen, along the mandrel's top east edge from the shoulder (0.08 off the collar) to 0.35
before the coil's last corner, 15.0 long with its ends; every side is at nothing. Each part's drivers are its moves in
order, every amount for class 3 alone; none rides another.

**Hot.** The rod is wound hot, and drawn as the game draws hot metal: its texture is the plain steel sheet
(code `spring`) and it glows by its temperature, the vanilla way, which a renderer does with the standard
shader's glow ("Gameplay: the spring job"). The rig names the parts drawn hot (`wind.hot`: `rodsteel`);
the viewer shows them glowing at a temperature slider (1000 °C, orange, at first). The shapes carry no glow
of their own: a part glows only while gameplay says the rod is hot.

**Sizes that keep faces apart.** The top and bottom sides are 0.6 across, the west and east sides 0.57, the straight
rod 0.615: no two pieces that meet share a face's plane, and a side's leading end lies inside the rod's end as it
grows. A side reaches half a wire past its corners. The rod's centre line runs on the coil's corner line, so its inner
faces stand 0.0075 into the mandrel's body (unseen; not in a plane with its faces).

The winding (t = W, one rod; 24 equal slots of 1/24; the rig's windows hold the exact times):

| t | |
|---|---|
| 0 | the rod lies straight along the mandrel's top east edge, from the shoulder to near the coil's far end (p 1: the sides are at nothing only with p 1) |
| 0 .. 1/6 | turn 1: the top side grows west across the top from the top east corner while the rod moves across the top with its end, shrinking (0 .. 1/24); the west side grows down the west face (.. 2/24); the bottom side east under the bottom (.. 3/24); the east side up the east face to the top east edge (.. 4/24) |
| 1/6 .. 5/6 | turns 2 to 5, the same, 1/6 each |
| 5/6 .. 1 | turn 6, the same; the rod's last wire's width shrinks to nothing as the last side ends |
| 1 | the spring is wound: six turns on the mandrel from the shoulder, the rod gone; gameplay delivers it and stops drawing it; nothing slides or drops |

### Regenerating

```sh
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py             # the assets, the reference poses and the item
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py --out DIR   # all of them into DIR (the item into DIR/item/)
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py --quick     # skips the z-fighting fix and the swept paths
```

A full run takes about 1.5 min (the spring's swept paths and its z-fighting poses; `--quick`, which still holds the
spring to its plan every 1/1000 of W, about 15 s) and
(`tools/validate_mandrelstation.py`):

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **Frame:** joined to the ground as one piece (the stump, hoop, bracket and swage).
- **Textures by role:** the stump oak; hoop, bracket and swage iron; the mandrel `mandrel`; each metal's
  work its own sheet; the spring `spring`.
- **Banding:** the work's faces along its length one sheet: at W 0.5 no offset between neighbouring rings
  or within a ring; at rest and at W 1 the largest offset between neighbouring rings reported (2.51
  texture units, at the part line; 2.25 within a section) and under a voxel's worth (4).
- **Containment:** nothing leaves the 1 × 1 × 2 box over the whole forging, either metal, every 0.01, or
  over the winding every 0.0025.
- **Forging:** at every tenth of W, one continuous square tube of even cross-section (every sample of its
  walls covered, nothing in its bore or outside it, all along it, but for 0.1 either side of the part
  line), (8 − 2W) across and (8 + 8W) long, with no gap between rings; at W 1 closed onto the mandrel and
  still on it (its far end short of the tip), its two sections (rings 1..4, 5..8) each 6 × 6 × 8 and end to
  end; `output.pos` beyond the tip. The tolerance is 0.15 voxels, the fix's deepest step and a margin.
- **Hang:** at every tenth of W the bore's ceiling on the bar's top (the stack's lowest within 0.01, every
  ring's within 0.15), nothing of the work into the bar by more than 0.01.
- **Mandrel:** through both bands of the bracket, each band's saddle, cheeks and cap bearing on it; the
  hollow against the shoulder; θ moves no part.
- **Spring, wound** (W 1): every side where `machinegen.coil` puts it (within 1e-5: the rig's numbers are rounded
  to 6 places), on the mandrel's faces, the coil between the shoulder and the nose (z 7.10..22.19); the rod gone.
- **Spring, at W 0:** the straight rod alone, along the mandrel's top east edge (on the coil's first corner line,
  along the mandrel exactly) from the shoulder (within 0.2 of the collar) to 0.35 before the coil's last corner, the
  coil's length; every side stretched to nothing.
- **Spring, winding** (every 1/1000 of W): the sides before the slot whole, those after it at nothing, the slot's
  own the coil's bar scaled about its start corner (the first side about its start face) along its own axis by how
  far the slot has got (within 0.002), its back end inside the end of the side before (within 0.02, the sizes' steps)
  and its leading end inside the rod's end (within 0.002; in the last slot it may run on past the rod's far face on
  the rod's line, as the rod shrinks away); the rod along the mandrel, its far face fixed, its shoulder end 0.35
  before the growing side's leading end along the mandrel and on that end's line across the face (within 0.002); no
  two pieces overlapping but those that meet (a side and the next; the rod and the side it lays, or the side before
  while the rod is within 0.7 of their corner).
- **Spring, steps** (the plan's 24 slots): a slot grows its side and moves the rod, and nothing else. The class is
  the job: the spring moves for class 3 alone (with none, 1 or 2 its parts are the identity), and the hollows never
  for 3.
- **Clearances:** no two parts overlap at any pose every 0.02 of the forging or 0.01 of the winding (the
  work's own rings, walls and corner bars, and the spring's own pieces, excepted; the spring's pieces out of
  sight left out), and the **swept paths**: the hollow stretching along the mandrel every 0.0025, the rod
  going round it and the coil growing every 0.001.
- **No z-fighting** at 54 poses after the fix: twelve of the forging, rest to delivered (21 faces pressed
  against their own part removed, the rest stepped in by 0.015; the fix runs up to 40 rounds for the stacks
  of rings), and 42 of the winding (W 0, halfway into each of the plan's 24 slots, early and late in
  every third, and wound), of what can be seen: a side stretched to nothing has no faces. The spring needs no
  step: its pieces' sizes keep their faces apart.
  step: its pieces' sizes keep their faces apart.
- **Item:** the spring item is the wound coil's 24 sides, each textured, centred on the item box's floor
  inside its 16 voxels.
- **Files:** every texture code declared; no `lid` on any cell (a hand station is not walked on); no
  power cell; the shipped files are the
  checked model (no move: the build frame's corner is the controller's). The cells' boxes are the frame,
  the mandrel and the hollows at rest, as before: the spring's rod is left out of them.

### Rig schema (`mandrelstation-rig.json`)

The gear cutter's schema (`../GearCutter/README.md`, "Rig schema") as the press brake uses it, with
nothing added to the shared rig maths: a `work` quantity, gauges, rides, and one θ-driven rotate of ratio 0.

- **The work.** `{ "name": "blows", "unit": "hollows", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one hollow's forging, 0..1: blows struck over blows needed; or one spring's winding, 0..1 (the unit
  still reads "hollows").
- **Inputs.** W, k and p, and θ. k is the job: the hollow's metal (1 lead, 2 copper), 3 a spring, 0 none. p is its presence,
  eased in as the hollow goes on and out after delivery, with k held while p eases out. Gameplay advances
  W by `1 / blowsPerHollow[k]` a blow, delivers two `forge.sections[k]` at W = 1, then clears. As with
  the press brake, every gauge eases back as p eases out, so a renderer stops drawing the work once its
  sections are delivered.
- **`forge`**: `blowsPerHollow` (lead 9, copper 14: the pace), `hollows` (each class's work), `sections`
  (what a hollow makes) and `sectionsPerHollow` (2).
- **`wind`**, the spring job: `rod` (`game:rod-steel`, its work), `class` (3: the spring job's own class,
  the k gameplay gives it), `turnsPerRod` (8: the pace, turns of the held right-click's clock, a turn a
  second), `spring` (`seraphhorizons:spring-steel`, what a rod makes), `springsPerRod` (1) and `hot`
  (`["rodsteel"]`: the requires values whose parts are drawn hot). Scalars, not per class: a spring has one
  class.
- **The work's end** names the three classes, each a job of 1: `{ "thin": 1, "thick": 1, "3": 1 }`.
- **Anchors**: `output.pos` (on the ground beyond the tip, where gameplay drops the two sections or the
  spring), `strike.pos` (the top of the box's middle, where the hammer lands: sparks and the blow's sound),
  `outputSide` south.
- **The spring's parts** (`requires` `rodsteel`): the sides `sp1u` .. `sp6e` and the straight rod `spr`, 25 in all,
  none riding another; every driver a `gauge`. A side has one, a `stretch` along its own axis (`rotation` its
  element's, `origin` its start corner, the first side's its start face) with `amount` −1 for class `"3"` and one
  window ending at its slot's end, so
  it is at nothing until its slot, grows over it and stays whole. The rod has a `stretch` along z about its far end
  face (`amount` −1, one window over the whole job: a wire's width a side), then 24 slides, one a slot, across the
  face that slot's side lies on; the stretch first, so the slides move the shrunk bar
  point, or a slide), in the order the piece makes its moves, each in its own window
  (one rising over a step of the plan, or a millionth of W wide for an instant, and open to the end), its
  amount for class `"3"` alone.

**Keys.** `cells`, `outputSide`, `output`, `strike`, `work`, `forge`, `wind` and `parts`.
`requires` values: `mandrel`, `hollowlead`, `hollowcopper`, `rodsteel`, or null. The texture code the renderer
sets: `mandrel` (the fitted rod's metal; iron plate in the shape). The spring's elements wear `spring`
(`game:block/metal/sheet-plain/steel1`, declared on the block too), and glow by the renderer's shader when
hot (`wind.hot`), not by any texture of their own.

**In the viewer** (`site/models.json`, `mandrel-station`): the θ slider is the hammer's blows (or the held
click) and moves nothing; the work's slider reads in hollows; the select **Work on the mandrel** is the job,
each its own class ("Lead hollow" 1, "Copper hollow" 2, "Steel rod: a spring" 3), and each job's work shows
only with its class (`requiresClass`). A **Rod temperature** slider (the scenario's `heat`, 0 to 1500 °C,
opening at 1000) draws the rod and its spring glowing by vanilla's rule, as the game will. Play moves W with θ
at lead's pace, a turn a blow (`play.turnsPerWork` `"forge.blowsPerHollow.thin"`), so a spring takes nine
turns there; 1.125 RPS shows it at the game's eight seconds.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `mandrel_*`; per metal
(`l`, `c`) and ring (`1` at the shoulder .. `8`; `1`..`4` are the near section, `5`..`8` the far one) the
walls `l1u_*`, `l1d_*`, `l1e_*`, `l1w_*` and the corner bars `l1ue_*`, `l1uw_*`, `l1de_*`, `l1dw_*`; the
spring's sides `sp1u_*` .. `sp6e_*` (turn 1..6, face `u`, `w`, `d`, `e` in winding order) and the straight rod
`spr_*`; and `fr_*` for the frame. The spring's elements are written whole: each side where the coil puts it (its
stretch has it at nothing until its slot), and the rod as it lies at W 0, along the mandrel's top east edge.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's own viewer
(a standalone copy). No one has yet looked at it in a client.

1. **The mandrel is cantilevered** 17.3 voxels past the bracket (10 past the stump): a smith slips the
   hollow on and the tube off over its free end. The bracket holds it at two bands 3.6 apart.
2. **Ring joints.** The hollow is eight rings, banded as one sheet ("One sheet down the tube"), so no
   seams are drawn between them: only the part line between the two sections shows. The sheet runs on
   exactly at W 0.5; towards rest and W 1 neighbouring rings' textures drift apart by up to about a pixel,
   and the stretch is carried by the rings sliding apart, not by the texture stretching. Through the open end the overlapping rings' ends can be seen inside the bore.
3. **Stepped faces.** The overlapping rings' outer faces are stepped in by up to 0.105 voxels (the
   z-fighting fix); a ring's side walls and corner bars stand back 0.03 and 0.06 at its ends.
4. **No slide-on, no slide-off.** The hollow appears on the mandrel as p eases in; at W 1 the finished tube
   stays on the mandrel and gameplay drops the two sections as items at `output.pos` and stops drawing it.
   Nothing is seen coming off. (Every gauge eases back as p eases out, so the work must not be drawn then.)
5. **Blows.** The forging is linear in W, so each blow (1/9 of a lead hollow, 1/14 of a copper one) closes and
   stretches it by the same amount; the renderer eases W between blows.
6. **Linear ramps**, as the press brake's: the motion starts and stops at full speed.
7. **The swage is decoration**: nothing is swaged in it.
8. **Lead and copper look the same** but for their texture.
9. **No lids.** The cells have their boxes and no collision-only deck: a player cannot walk across the top.
10. **The wire's length is not kept** (the owner's choice). The straight rod is 14.4 long on its centre line and the
    coil 111 voxels of wire: each side grows from its corner by its own stretch, and the rod loses only a wire's
    width to it. The rod is drawn as the spring's wire (0.615 square), not as the game's rod (2 × 2 × 10, whose metal
    the six turns hold).
11. **The bend is the rod's end.** The wire's 90° bend at the growing end is where the growing side's leading end
    enters the rod's end: the rod is 0.015 wider than the wire, so the bend is a 0.0075 step, and the rod's end face
    stands 0.05 before the side's near face along the mandrel. No block, no tongs (the smith's grip is not modelled).
12. **Stretched, not cut.** A side's bar grows by a scale along its own axis about its start corner, so its texture
    (the plain steel sheet) is stretched along it as it grows, and its back end, half a wire behind the corner, grows
    with it inside the end of the side before (the first side grows about its start face, the wire's free end, which
    comes out from under the rod's end as the rod moves on). The ends of a side are 0.6 or 0.57 and the side before's
    bar the other, so a growing side's back end stands 0.015 out of the side before on two faces for the first 6 % of
    its slot (a sliver on the joint's shadowed faces).
13. **Hard corners.** Each side meets its corner square: a hard corner where two bars overlap, where a real rod
    bends round a radius.
14. **The rod's shoulder end** stands 0.35 before the growing side's leading end along the mandrel (0.05 past the
    side's near face), the same the whole job, so it covers the first side's tilted start as it begins to grow. In the
    last slot the rod's last wire's width shrinks away 0.35 short of the coil's last corner, so the last side's end
    runs on past the rod's far face, on its line, while the rod vanishes: the wire's end comes out as the rod is used
    up. A rod long enough to hold it would leave a stub at the end.
15. **Sizes that keep faces apart.** The west and east sides are 0.57 (their inner face 0.015 off the mandrel), the
    rod 0.615 (its inner faces 0.0075 inside the mandrel's body, as its centre line is the coil's). The item is the
    wound coil as before (its last side 0.57 like every east side). Rounding the rig's numbers to 6 places leaves the
    grown sides up to 1e-5 off the coil.
16. **Linear ramps.** Each side grows at one speed through its slot, the rod moves and shrinks at one speed through
    the job, and the next slot starts as the last ends: the coil's growing end never slows, and turns each corner
    square.
17. **p unwinds it.** Every gauge eases back with p, so a spring drawn while p eases out after delivery
    would come apart: the renderer must stop drawing the work at delivery, as for the hollows.

## Gameplay

The rules are game-independent in `Core/` (`Forging.cs`: the `requires` vocabulary, the mandrel, the
hollows by metal and the job; `MandrelStationConfig.cs`; `MandrelStationRig.cs`; and
`MandrelStationView.cs`: the renderer's view of the station and its clock); the game side is `Game/`
(`MandrelStationSystem`, the controller `BlockMandrelStation` and `BEMandrelStation`, the ghost
`BlockMandrelStationGhost` with `BEMandrelStationGhost`, and `MandrelStationRenderer`), copied from the
press brake's (`../PressBrake/README.md`, "Gameplay") with its held lever replaced by blows. The mod's
README, "Mandrel forging station", is the player-facing summary; the decisions are here. Everything is
read from the rig through its anchors, `forge` and `requires`, never element names.

**Blocks.** `seraphhorizons:mandrelstation-frame-{side}` (`assets/seraphhorizons/blocktypes/mandrelstation/frame.json`,
drawing `mandrelstation_frame.json`; its textures are every texture of both shapes, `mandrel` included)
and `mandrelstation-ghost` for the far cell. The contract writes the block as `mandrelstation-{side}`;
gameplay names it `-frame-{side}`, as the press brake's and the draw bench's are, since what is placed is
the frame and the mandrel is fitted to it. Placement (`side` = the player's look,
`MandrelStationRig.PlacedSide`, the stump nearest them and the mandrel pointing away), the ghost, its
repair, breaking through it and the boxes (selection from the rig's cells, collision with their lids)
are the press brake's. The frame recipe (`assets/seraphhorizons/recipes/grid/mandrelstation.json`): an
oak log stood on end for the stump, an iron or steel plate (the bracket's base plate and the swage), two
nails and strips of iron, meteoric iron or steel (the hoop and the strap bands) and a hammer. It takes
no rod: the mandrel is the one fitted part.

**The mandrel** (`MandrelPart`): `game:rod-iron`, `-meteoriciron` or `-steel` (verified in game 1.22.7's
`survival/itemtypes/part/rod.json`), fitted by right-click on the stump or the ghost; a second is
refused with a message, and a rod of another metal is the item's own business. Once fitted it stays in:
only breaking the station gives it back, as the contract has it. A machine's parts come out by hand only
when they are consumables (the draw bench's die), and the mandrel station has none, so there is no
take-out path, no help line for one and no lang string. The creative shortcut (Ctrl in creative mode with no mandrel) fits an iron one free.
Breaking drops the frame, the mandrel and a hollow not yet struck (a struck one is lost).

**Blows** (the anvil's pattern: `BlockEntityAnvil` takes a hammer's hits). A hollow section, the game's
`chutesection-lead` (k 1) or `-copper` (k 2), the rig's `forge.hollows`, goes on a bare mandrel by
right-click; nothing else does (an angle, a pipe section, an ingot, a plate: `Forging.ClassOfHollow`),
and only when its pipe section exists (`UnifiedPipes`' item; with that switch off a hollow is refused
with a message). Then **each right-click with any of the game's hammers** (`game:hammer-*`) on the
station is a blow, no faster than one each `Forging.BlowIntervalMs` (300) on a station, so right-click
held does not hammer faster than a smith swings. A blow advances W by (the hammer's tool tier /
`BaseHammerTier`) / `BlowsPerHollow` of the hollow's metal (`Forging.WorkPerBlow`, `ForgeJob.Strike`):
`BlowsPerHollow` is the count for the base hammer, the copper one, and the tier is read from the held
hammer's item (`CollectibleObject.ToolTier`, the game's `tooltierbytype`: copper, gold and silver 2, the
three bronzes 3, iron and meteoric iron 4, steel 5) at each blow; a hammer with no tier counts as the
base, and one blow never advances more than half a hollow (`Forging.MaxWorkPerBlow`). So lead takes 9
blows of a copper hammer, 6 of a bronze one, 5 of an iron one and 4 of a steel one; copper 14, 10, 7
(iron) and 6 (steel). The blow that brings W to 1 finishes it (the overshoot is dropped). A blow plays the anvil's `game:sounds/effect/anvilhit` and throws a few
small glowing sparks at `strike.pos`, and costs the hammer `HammerWearPerBlow` durability (not in
creative mode), one a blow whatever the hammer. At W = 1 the hollow is used up and `forge.sectionsPerHollow` (2)
`forge.sections[k]` (`seraphhorizons:pipesection-{metal}`) drop just past the tip (into
`MandrelStationRig.OutputNeighbour()`, native south) at `OutputDrop()`, pushed outward, as the press brake's angle does. Each blow syncs W and the blow count.
Ctrl + right-click takes a hollow back only before the first blow (the work, not a part); after it, it
stays (an error says so). The station never takes from or puts into a chest or other container
beside it. **Hotbar refill:** a hammer click on a bare mandrel while right-click is held since
a hollow was finished (the same player's hammer clicks on the station no more than
`Forging.HeldClickGapMs`, 750, apart: the client repeats a held click every 250 ms) puts another of the
same item on from the first of the player's hotbar slots holding one (`Forging.RefillSlot`: slots 0..9,
not the skill slot or the off hand, never the backpack; a hollow of the other metal is left), as a
right-click with it in hand would (`TryLoadHollow`: the slot's stack less one, the hollow's sound, the
station synced). It waits for `Forging.BlowIntervalMs` since the last blow, as a blow would, so the
finished tube is seen delivered first; that click is not a blow either, and the next is struck as usual.
So hammering carries on without letting go. A save keeps
the mandrel's code, the hollow, its class, the blows and W.

**Settings** (`MandrelStationSettings`): `BlowsPerHollowLead` 9 and `BlowsPerHollowCopper` 14, the rig's
`forge.blowsPerHollow` (`MandrelStationRigTests.The_default_pace_is_the_rigs` holds them together),
counted for a hammer of `BaseHammerTier` 2 (the game's copper hammer's tier); the block info shows the
blows struck and W as a percentage, since the blows left depend on the hammer.
`HammerWearPerBlow` 1, as a blow on the anvil costs a hammer one. Nothing of the station wears, so there
is no oil and no tool of its own.

**Renderer** (`MandrelStationRenderer`, through `IMandrelStationView`: facing, the mandrel fitted and its
metal, the hollow's class, the server's W and blows). Every rig part with a `requires`, a ride or a
driver, from `mandrelstation.json`, drawn when fitted (the mandrel), with the texture code `mandrel` set
to the fitted rod's metal (`MachineMeshes.MetalTexture`, one mesh set a metal). The hollow's parts
(`hollowlead`, `hollowcopper`) are drawn only while that metal's hollow is on: once the sections are
delivered they are items, and the work is not drawn while p eases out (the contract's "Changes").
`MandrelStationClock` eases W and θ (2π a blow) to the server's after each blow, exponentially, settling
in about a tenth of a second (weak spot 5: a blow can end part way through a window); with no hollow on,
W is held at 1 while p eases out over 0.4 s and k is held; the next hollow starts from the server's W.

**Handbook.** Three sections on the frame (`attributes.handbook.extraSections`): assembly, forging pipe
sections, and the ladder (two angles and solder make a hollow; a hollow on the mandrel gives two pipe
sections, or four on the draw bench; a pipe section and solder make a pipe). With the switch off,
`MandrelStationSystem.UnlinkText` strips the mod's own links to the station (`handbook://block-seraphhorizons:mandrelstation*`
and the other features' `handbooksearch://mandrel station`).

**Export.** `tools/recipe-export/Recipes/MandrelStationExport.cs` and `RecipeSection.MandrelStation.cs`
write one `machine` record per metal (`mandrelstation|game:chutesection-{metal}|0`): the hollow
consumed, the mandrel kept, the hammer as a tool worn `HammerWearPerBlow` × blows a job (`wear`
`fixed`, so the site shows it as the worn tool, not consumed), the frame as the station, two pipe
sections out; `power` `hand`, `turns` the base (copper) hammer's blows and `work` that many `blows` (the
site says "By hand: 9 blows a job, each a right-click with a hammer"), the definition's hammer
`game:hammer-copper`; a better hammer's fewer blows are the handbook's (the schema has no note on a
recipe); no `oil`. Type `mandrelstation`, owned by
`MandrelStation` (`Core/SwitchOwnership.cs`).

**Tests.** `tests/MandrelStation/MandrelStationGameplayTests.cs` (the mandrel, kept until broken, saves,
hollows by metal and what is refused, hammers, blows and the last one, copper's fourteen, the blow interval, right-click held and the
hotbar slot a refill comes from,
the clock, settings, and the reader on a rig written to the contract with its refusals) and
`MandrelStationRigTests.cs` (the shipped rig through the shared parser and `MandrelStationRig`, its
anchors, the pace held to the settings, the reader's refusals, and every pose of
`tests/MandrelStation/rig-reference.json` replayed through `Machines/Core`);
`tests/PackTests/MandrelStationScenarios.cs`, `RecipeExportMandrelStationScenarios.cs` and
`SwitchesOffScenarios.Mandrel_station_off_there_is_no_mandrel_station` (Atlas:
`FullyQualifiedName~Mandrel_station`).

**Not done.** No hand animation of its own: the click plays the game's default use pose. No schematic
for the frame (`MachineSchematics`).

## Gameplay: the spring job

Built to the contract the model left (settled with the owner: steel rods only, one rod a spring, wound on the
mandrel already fitted, by hand with tongs, right-click held, about 8 s (4 at first); heat by the anvil's rules). Everything
is read from the rig (`wind`, `requires`, the anchors), never element names. The rules are in `Core/Winding.cs`
(the verdict for a rod, the heat rule, the glow, `WindJob`, `WindHolds`) and `Core/MandrelStationRig.cs`
(`WindSpec`, the rig's `wind`); the game side is `BEMandrelStation` ("Winding"), with the held interaction in
`BlockMandrelStation` and its ghost. Switch: `MandrelSprings` (with `MandrelStation`).

- **The vocabulary.** `MandrelRequires.RodSteel` (`rodsteel`) is known, and `MandrelRequires.Work(k)` gives a
  job's work: the hollows for 1 and 2, the rod for 3. The reader takes `wind` (optional: a rig without one has
  no spring job) and refuses one whose rod, class, spring or count are not `Winding`'s, a pace of 0, or a `hot`
  value that is not a `requires`; with `wind`, `work.end."3"` must be 1.
- **The item.** `seraphhorizons:spring-steel` (`assets/seraphhorizons/itemtypes/spring.json`), shape
  `seraphhorizons:item/spring`, texture the game's plain steel sheet, steel's `combustibleProps` (1502 °C, 30,
  one steel ingot, as the rod), in the general and items tabs, its own handbook section. It exists only with
  `MandrelSprings` and `MandrelStation` on (`MandrelStationSystem.SpringTypeAssets`, disabled on the server
  before the game loads it; `SwitchRegistry` owns it by `MandrelSprings`).
- **A rod on the station** (`Winding.ForRod`). A mandrel rod on a station with no mandrel is fitted as the
  mandrel, steel too, hot or cold. On a fitted, bare mandrel only `game:rod-steel` goes on as work, and only at
  working temperature (`Winding.Workable`: the stack's `workableTemperature` attribute, else half its melting
  point, `GetMeltingPoint`; the steel rod has no attribute, so 751 °C); a cold one is refused with a message
  giving its temperature and the one it needs; with work on, an occupied message; with springs off, or an iron
  rod, the mandrel-fitted message as before. One rod from the held stack goes on, its stack kept (and so its
  temperature, which the game's temperature attributes go on cooling).
- **Its class.** A rod on is k = 3 (`wind.class`) for the rig and the clock; `MandrelStationClock` carries 1, 2
  and 3, and the view reads `JobClass`, `JobWork`, `Held`, `TurnsPerRod` and `RodTemperature`.
- **Winding.** With a rod on, any right-click on the station or its ghost (Shift aside) starts winding, held as
  the press brake's lever is: `OnBlockInteractStep` keeps the player in `WindHolds` while they hold, and the
  server's 50 ms tick winds by `Winding.RadiansPerSecond` × dt while anyone does, W advancing by a turn over
  `TurnsPerRod` (8, the rig's `wind.turnsPerRod`, held together by a test): eight seconds a rod. The player needs
  **tongs** in either hand (any item whose tool is `Tongs`: the game's `game:tongs` and metal tongs; the game
  keeps them in the off hand), none in creative mode; without, an error and no hold. W syncs every 0.02;
  the client follows the held clock with `HeldWorkFollower` (the press brake's), θ a turn a second, moving
  nothing.
- **Heat.** Each tick the server reads the rod's temperature; below working heat W stops (the winder is told
  once, with the temperature) until the rod is taken back and reheated. **Reheating:** Ctrl + right-click takes
  the rod back at any time while it is on, straight, its stack and temperature as they are (the winding so far
  is lost, the metal is not), into the player's inventory; it is reheated on a forge (`RodsOnForge`, the mod's
  README) and put on again, from W 0.
- **Drawn.** The `rodsteel` parts are drawn only while a rod is on and p is 1 (`MandrelStationClock.ShowsWork`:
  for class 3, p jumps to 1 as the rod goes on); never the hollows' during a spring, nor the spring's during a
  hollow. At delivery the job is off at once and the parts are not drawn while p eases out.
- **Drawn hot.** `MandrelStationRenderer` sets, for each part whose `requires` is in `wind.hot`, the standard
  shader's `RgbaGlowIn` from `ColorUtil.GetIncandescenceColorAsColor4f(temp)` with alpha extraGlow / 255 and
  `ExtraGlow` = `Winding.ExtraGlow(temp)` = Clamp((temp − 550) / 2, 0, 255), and zero glow for every other part
  (and after the loop), the rod's temperature read on the client from the synced stack each frame.
- **Delivery.** At W 1 the rod is used up and one spring, at the rod's temperature (`SetTemperature`), drops at
  `OutputDrop()`, as the sections do; the station clears. The
  last winder's tongs (the hand holding them) pay `TongsWearPerRod` (1), not in creative mode.
- **Breaking, saving.** Breaking gives back a rod not yet wound (W 0); a part-wound one is lost. A save keeps the
  rod's stack (its temperature attributes), W and, for the client, whether someone winds and the server's pace.
- **Infeed.** None: a rod must come hot from the forge. Nor a hotbar refill.
- **Sounds.** The ingot's sound as a rod goes on, comes off and as a spring drops; no other.
- **Export.** `MandrelStationExport.ReadWind` and `RecipeSection.SpringRecord`: `mandrelstation|game:rod-steel|0`,
  the rod in, the mandrel kept, the tongs (`game:tongs`, every tongs as the variant) worn `TongsWearPerRod` a
  spring (`wear` `fixed`), the frame as the station, one spring out; `power` `hand`, `turns` `TurnsPerRod`,
  `work` that many `seconds` (the site says "By hand, with tongs: 4 seconds of winding a job"); the heat, which
  the schema has no field for, in `requirements` ("The steel rod at working temperature, 751 °C or more") and
  `extra.workableTemperature`. Owned by `MandrelStation` (the type); the spring by `MandrelSprings`.
- **Handbook.** A "Winding springs" section on the frame (dropped from the frame with springs off), the ladder
  section names the spring and the draw bench's use of it, and the spring's own page.
- **The draw bench** takes one spring in its mandrel stage, with its rods (`../DrawBench/README.md`).
- **Tests.** `tests/MandrelStation/MandrelStationGameplayTests.cs` (the verdict for a rod, the heat rule, the glow,
  `WindJob`, `WindHolds`, the clock's class 3 at p 1, the reader's `wind` and its refusals),
  `MandrelStationRigTests.cs` (the shipped rig's spring job, its pace the settings'); Atlas
  `tests/PackTests/MandrelSpringScenarios.cs` (the spring and forgeable rods load; a rod on the forge heats; a
  cold rod refused, a hot one wound with tongs from the stump and the ghost into a hot spring and the tongs worn;
  a cooled rod stopped, taken back and put on again; breaking; a steel rod as the mandrel on a bare station),
  `RecipeExportMandrelStationScenarios.Mandrel_station_winds_a_steel_rod_into_a_spring_record` and
  `SwitchesOffScenarios.Mandrel_springs_off_there_is_no_spring`.

## Open for the owner

1. **The bench's spring is bigger than the item.** The coil wound on the station's 4 × 4 mandrel has a 4 bore;
   the draw bench's spring goes round its 5.9 mandrel bar, so the bench draws the same coil (six turns, 0.6
   wire, steel) with a 6.0 bore. Keep it, or change the bench (a 4 × 4 spindle for the spring behind the
   follower) or wind springs on a bigger arbor?
2. **How the rod is wound.** As you asked: laid straight along the mandrel's top east edge from the shoulder, the
   coil's length, and wound from its shoulder end, each side growing from its corner along its own line across the
   face, as wire laid down, while the rod moves across that face with the growing end (its shoulder end at the bend)
   and shrinks into the coil, gone as the last side ends; nothing swinging, popping or fat at the bend. A side's
   growth and the rod's shrinking are the new stretching gauge (`docs/recipe-browser/models.md`, "Stretching gauges"),
   with its one artefact: the sides are drawn whole at p 0, so the spring's parts must only be drawn at p 1 (the
   gameplay contract below). Acceptable?
3. **A steel rod is a mandrel too.** Settled: a bare station fits it as the mandrel; a fitted, bare mandrel
   takes a hot one as a spring job and refuses a cold one (built).
4. **Tongs, and a rod cooling mid-job.** Settled: winding needs tongs in either hand; a rod cooling below 751 °C
   stops until it is taken back (Ctrl + right-click, any time) and reheated (built).
5. **Pace.** 4 s for any player and any tongs; nothing like the hammer's tier speeds it (built so).
6. **The work's unit** still reads "hollows" (in the rig and the viewer) for a spring too.
7. **The rod's length.** The straight rod is the coil's length (14.4 on its centre line, from 0.35 before the coil's
   first corner to 0.35 before its last), shrinking a wire's width a side, so it is gone as the last side ends. A real
   rod of that length would make a fifth of the coil.
8. **Free length.** The free spring is 15.1 long (pitch 2.4, to fit the station's mandrel); at the end of a
   draw bench job its spring is stretched to 15.7 between the nut and the follower, 4 % past free.
