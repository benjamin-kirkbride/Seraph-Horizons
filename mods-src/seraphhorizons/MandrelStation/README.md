# Mandrel forging station

Part of the Seraph Horizons mod (`../README.md`): a rung of the pipe ladder (unified pipes, `../Pipes/`).
A smith's **mandrel station** of the 1700s hammers one lead or copper **hollow section** (vanilla's
chute section, named the tube blank in game, `game:chutesection-lead` or `-copper`: an 8 × 8 square box, 8 long, 1-voxel walls) down
onto an iron mandrel into a pipe-sized tube, which comes off as two **pipe sections**
(`seraphhorizons:pipesection-{lead,copper}`: a square tube 6 across, 1-voxel walls, 8 long, half a
block). One hollow makes two sections; a section with solder makes a straight pipe.

**Its second job winds a coil spring** on the same mandrel: one hot **steel rod** (`game:rod-steel`) is
pulled round the fixed mandrel by hand, with tongs, into one **spring** (`seraphhorizons:spring-steel`), a
square coil of six turns that follows the square mandrel. The draw bench's follower spring is this spring
(`../DrawBench/README.md`). The model of that job is in "The spring job"; what gameplay has to build for it
is in "Gameplay: the spring job (to build)".

This folder holds the model's generator and its rig (`tools/`); the gameplay is built against the rig
("Gameplay" below).

**A hand station.** It has no mechanical power, no power cell and no oil. The blows are the player's
right-clicks with a hammer, as at the anvil; no hammer is modelled, since the player holds it. The rig's
θ is the hammer's clock: every motion is posed by the forging W, and θ moves nothing. A spring is wound
with right-click held, as the press brake is worked; θ is then that clock, and moves nothing either.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines. This one
added `machinegen/coil.py` (the square coil spring, which the draw bench draws too), and no new driver
(every motion is a `gauge`, and the spring's pieces ride each other). It needed the rig maths' classes past
thick: the spring is a job of its own, class 3 (`docs/recipe-browser/models.md`, "Classes").

## The station

**Footprint.** 2 cells: 1 wide (x), 1 high (y), 2 long (z). In the native, south-facing frame with
the controller at `[0,0,0]`: x 0, y 0, z 0..1. The controller is the stump, `[0,0,0]`, the block the
player clicks. Placed like the press brake and the draw bench, the station extends away from the
player along their line of sight, the stump nearest them and the mandrel pointing away: the block's
`side` is the way they look. A chest of hollows can stand beside the stump (native west, `infeedSide`);
when a hollow is done, gameplay delivers its two sections beyond the tip (native south, `outputSide`),
into a container standing there, or onto the ground at `output.pos`. The model draws nothing coming off.

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
| The spring's sides (`sp1u`..`sp6e`: turn 1..6, face `u` top, `w` west, `d` bottom, `e` east) | the smith's pull on the rod (not modelled), round the mandrel's edge | each, in its twenty-fourth of the work, swings from along the mandrel's edge across its face into the coil, about that edge, and stays; each rides the one before it |
| The rod's next piece (the side after the one being bent: the follower) | the end of the side being bent | stays along the mandrel, its start on that end, round a quarter circle to the next edge; comes out of the head as it goes |
| The head (the last side, `sp6e`, a hair bigger) | the follower | runs on along the edge ahead of the follower, carrying the sides still to come inside it, out of sight, and the rod's far end; is itself bent last |
| The rod's far end (`sptail`) | the head | runs back into the head over the first 22 sides: the straight rod shortens |

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

**How it is wound.** No driver bends a part, and the rod a coil this long would take (110 voxels) does not fit
the station's two blocks, so the rod is drawn as the coil's sides and a short straight end, and wound a side
at a time:

- At W 0 the rod lies straight along the mandrel's top east edge, from the shoulder to just short of the nose
  (z 7.13..22.22, 15.1 long). Its first piece is the coil's first side (`sp1u`); then the **head** (`sp6e`,
  the coil's last side, 0.015 bigger all round), with every other side inside it, out of sight; then the
  rod's **far end** (`sptail`).
- In its twenty-fourth of the work, side j **swings** about the mandrel's edge it lies on, in the plane of
  the next face, from along the edge across the face into its place in the coil (a `gauge` rotate about the
  face's normal through its start, 90° less the lean), and stays there.
- Meanwhile the side after it (the **follower**, side j + 1) stays along the mandrel with its start on the
  swinging side's end: it rides side j and turns back by the same angle about that end (a second `gauge`),
  so it is carried round a quarter circle in the face's plane to the next edge, still straight along the
  mandrel. The head runs on ahead of it along the edge (four straight `gauge` slides, fitted to the quarter
  circle within 0.08), so the follower comes out of the head as it goes, and is out by the end of the
  quarter: it is the next to be bent.
- So the straight rod goes round the mandrel a face at a time, along its edges, as the coil grows from the
  shoulder towards the tip; the far end runs back into the head over the first 22 sides, so the rod
  shortens (15.1 long as the first side is bent, 9.9 as the 23rd is, 5.3 as the last), and at W 1 nothing
  of it is left: the spring is wound, on the mandrel. As the coil nears the tip the straight rod reaches
  past it, up to 6.7 beyond the nose (z 30.2 at most), the smith's end of it, inside the station's two
  blocks.

Every side rides the one before it (`sp1w` rides `sp1u`, ..., `sp6e` rides `sp6d`; `sptail` rides `sp6e`):
a chain 25 deep, which the shared rig maths composes as it does any ride. Each side's own drivers, in order:
its run ahead (sides 3 on, in the side before the one before it), its swing, and its turn back (sides 2 on, in
the side before it). The class is the job: every spring amount names class 3 alone, so the spring moves
only for a spring job and stands still for the hollows' 1 and 2 (whose parts stand still for 3).

**Hot.** The rod is wound hot, and drawn as the game draws hot metal: its texture is the plain steel sheet
(code `spring`) and it glows by its temperature, the vanilla way, which a renderer does with the standard
shader's glow (see "Gameplay: the spring job"). The rig names the parts drawn hot (`wind.hot`: `rodsteel`);
the viewer shows them glowing at a temperature slider (1000 °C, orange, at first). The shapes carry no glow
of their own: a part glows only while gameplay says the rod is hot.
The sides alternate full and 0.015 thinner on each face (and the head is 0.015 bigger), so no two
neighbours' faces share a plane while one swings past the other.

The winding (t = W, one rod; Q = 1/24, side j from 0; the rig holds none of these times):

| t | |
|---|---|
| 0 | the rod lies straight along the mandrel's top east edge from the shoulder, 15.1 long (p eases in) |
| j Q .. (j + 1) Q | side j swings across its face into the coil; the follower (side j + 1) goes round a quarter circle to the next edge and comes out of the head, which runs on along the edge; the far end runs back into the head |
| 0 .. 22 Q | the far end runs back into the head, evenly |
| 22 Q .. 23 Q | the head is the follower, the far end inside it |
| 23 Q .. 1 | the head, the last side, swings into the coil |
| 1 | the spring is wound: six turns on the mandrel from the shoulder; gameplay delivers it and stops drawing it; nothing slides or drops |

### Regenerating

```sh
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py             # the assets, the reference poses and the item
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py --out DIR   # all of them into DIR (the item into DIR/item/)
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py --quick     # skips the z-fighting fix and the swept paths
```

A full run takes about 2 min 15 s (the spring's swept paths and its z-fighting poses; `--quick` about 30 s) and
is deterministic (two runs into two folders are byte-identical). It exits non-zero if a check fails
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
- **Spring, wound** (W 1): every side where `machinegen.coil` puts it (within 2e-5: the rig's numbers are
  rounded to 6 places), on the mandrel's faces, the coil between the shoulder and the nose (z 7.10..22.19),
  the rod's far end inside the last side.
- **Spring, at W 0:** one straight rod along the mandrel's top east edge, unbroken, from 0.13 off the
  shoulder, 15.1 long.
- **Spring, winding** (every 1/96 of W): one wire, each side's centre line starting where the one before it
  ends (within 1e-4; the rounding adds up along the chain of 25 rides), the follower along the mandrel's edge,
  the head on the follower and coaxial with it, the far end along the head; the sides still to come, and the
  far end once run back, inside the head; no two pieces that are not joined overlapping; the straight rod
  never longer as one side is bent than as the one before it was. The class is the job: the spring moves for
  class 3 alone (with none, 1 or 2 its parts are the identity), and the hollows never for 3.
- **Clearances:** no two parts overlap at any pose every 0.02 of the forging or 0.01 of the winding (the
  work's own rings, walls and corner bars, and the spring's own pieces, excepted), and the **swept paths**:
  the hollow stretching along the mandrel every 0.0025, the rod going round it and the coil growing every
  0.001.
- **No z-fighting** at 51 poses after the fix: twelve of the forging, rest to delivered (21 faces pressed
  against their own part removed, the rest stepped in by 0.015; the fix runs up to 40 rounds for the stacks
  of rings), and 39 of the winding (W 0, a quarter into every side's swing, halfway into every third, three
  quarters into five, and wound), of what can be seen: the sides hidden in the head are left out. The
  spring needs no step: its sides alternate in size.
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
  the k gameplay gives it), `turnsPerRod` (4: the pace, turns of the held right-click's clock, a turn a
  second), `spring` (`seraphhorizons:spring-steel`, what a rod makes), `springsPerRod` (1) and `hot`
  (`["rodsteel"]`: the requires values whose parts are drawn hot). Scalars, not per class: a spring has one
  class.
- **The work's end** names the three classes, each a job of 1: `{ "thin": 1, "thick": 1, "3": 1 }`.
- **Anchors**: `output.pos` (on the ground beyond the tip, where gameplay drops the two sections or the
  spring), `strike.pos` (the top of the box's middle, where the hammer lands: sparks and the blow's sound),
  `infeedSide` west, `outputSide` south.
- **The spring's parts** (`requires` `rodsteel`): `sp1u` .. `sp6e`, each riding the one before it, and
  `sptail` riding `sp6e`; every driver a `gauge` (rotations about the mandrel's edges, slides along it) with
  windows in twenty-fourths of W, its amount for class `"3"` alone.

**Keys.** `cells`, `infeedSide`, `outputSide`, `output`, `strike`, `work`, `forge`, `wind` and `parts`.
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
turns there; 2.25 RPS shows it at the game's four seconds.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `mandrel_*`; per metal
(`l`, `c`) and ring (`1` at the shoulder .. `8`; `1`..`4` are the near section, `5`..`8` the far one) the
walls `l1u_*`, `l1d_*`, `l1e_*`, `l1w_*` and the corner bars `l1ue_*`, `l1uw_*`, `l1de_*`, `l1dw_*`; the
spring's sides `sp1u_*` .. `sp6e_*` (turn 1..6, face `u`, `w`, `d`, `e` in winding order) and the rod's far
end `sptail_*`; and `fr_*` for the frame. The spring's elements are written lying along the mandrel's top east
edge (their place at W 0, coincident in the head but for the first), not where they end up: the rig swings
them into the coil. Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

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
10. **The rod is drawn as the spring's wire**, 0.6 square, not as the game's rod (2 × 2 × 10, whose metal the
    six turns hold), and it is not as long as the coil takes (110): it is 15.1 at W 0 and shortens by its far
    end running back into the head, not by what the coil uses.
11. **A side swings, it does not wrap.** Each side swings rigidly about the mandrel's edge from along it to
    across the face; a real rod wraps progressively and bends at the corner. The bend is a hard corner where
    two sides' bars overlap, and the rod is fed along the mandrel's edge rather than pulled off tangentially
    (a straight end pulled round the top would rise into the block above).
12. **The straight rod reaches past the tip** in the last turns, up to 6.7 beyond the nose, unsupported (the
    smith holds it): the head always runs a side ahead of the follower.
13. **Sizes that keep faces apart.** Every other side is 0.03 thinner (0.57) and its inner face 0.015 off the
    mandrel; the last side, which carries the sides still to come hidden inside it, is 0.03 bigger (0.63) and
    0.03 longer, and sits 0.015 into the mandrel. The first side starts 0.1 off the shoulder, so its corner
    clears the collar mid-swing.
14. **Hidden sides.** The sides still to come lie coincident inside the head; the game draws them there,
    unseen. Rounding the rig's numbers to 6 places leaves the wound sides up to 2e-5 off the coil and the
    joints up to 1e-4 apart.
15. **The head runs ahead in four straight ramps a side**, fitted to the quarter circle the follower goes
    round within 0.074, so the follower comes out of it slightly unevenly.
16. **Linear ramps, a side at a time.** Each side swings at an even rate in its twenty-fourth of the work and
    the next starts as it stops: the rod goes round without a pause, but turns sharply at each edge.
17. **p unwinds it.** Every gauge eases back with p, so a spring drawn while p eases out after delivery
    would unwind back to a rod: the renderer must stop drawing the work at delivery, as for the hollows.

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
`forge.sections[k]` (`seraphhorizons:pipesection-{metal}`) go into a container in
`MandrelStationRig.OutputNeighbour()` (the cell beyond the tip, native south), else drop just past it at
`OutputDrop()`, pushed outward, as the press brake's angle does. Each blow syncs W and the blow count.
Ctrl + right-click takes a hollow back only before the first blow (the work, not a part); after it, it
stays (an error says so). **Infeed:** a hand station takes nothing by itself. A blow on a bare
mandrel takes one hollow from a container in a cell beside the station (`InfeedNeighbours`, native
west: beside the stump or beside the tip's cell) and loads it; that click is not a blow. **Hotbar
refill:** with no hollow from the infeed, a hammer click on a bare mandrel while right-click is held since
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

## Gameplay: the spring job (to build)

The model and rig of the spring job are done ("The spring job", "Rig schema"); none of its gameplay is.
This is the contract for it, everything read from the rig (`wind`, `requires`, the anchors), never element
names. Settled with the owner: steel rods only, one rod a spring, wound on the mandrel already fitted, by hand
with tongs, right-click held, about 4 s; heat by the anvil's rules.

- **The vocabulary first.** The shipped rig now uses `requires` `rodsteel`, which `MandrelRequires.KnownRequires`
  (`Core/Forging.cs`) does not know: `RigParts.Parse` refuses the rig, so the game's reader and
  `MandrelStationRigTests` fail until it is added. Add it (with a `MandrelRequires.Rod` or the like), and read
  `wind` in `MandrelStationRig` (`rod`, `class`, `turnsPerRod`, `spring`, `springsPerRod`, `hot`). The work's
  end now names class 3 too (`work.Ends[3]` 1); the reader's check of `Ends[1]` and `Ends[2]` stands. The
  shared rig code takes classes past thick already (`Machines/Core/RigClasses.cs`): nothing there to change.
- **The item.** `seraphhorizons:spring-steel`, a new item type (gameplay's), shape
  `seraphhorizons:item/spring` (`assets/seraphhorizons/shapes/item/spring.json`: the wound coil along z, 5.3
  × 5.3 × 15.1, centred on the item box's floor, texture code `spring`, the game's plain steel sheet). Its
  GUI, hand and ground transforms are the item type's.
- **The work.** A steel rod (`wind.rod`, `game:rod-steel`; no other metal) goes on a fitted, bare mandrel by
  right-click, as a hollow does, and only when the spring item exists. It must be hot enough to work by the
  anvil's rule, vanilla's `ItemIngot.CanWork`: its temperature (`CollectibleObject.GetTemperature`) at least
  the item's `attributes.workableTemperature`, else half its melting point; the steel rod has no attribute
  and melts at 1502 °C (`combustibleProps`), so it works from **751 °C**. A cold rod is refused with a
  message. The rod keeps its stack on the station (its temperature with it, cooling as the game cools any
  stack); should it fall below 751 °C part-way, winding stops until it is reheated or taken back (the
  anvil's way: it will not work cold). Steel rods are also mandrels: a station with no mandrel takes a steel
  rod as its mandrel, as now; only on a fitted, bare mandrel is it a spring job (see "Open for the owner").
- **Its class.** A spring job is k = `wind.class`, **3**, for the rig and the clock: a job of its own, as each
  hollow's metal is (1 lead, 2 copper). The spring's parts move for class 3 alone and the hollows' never for it,
  so the clock's k says which job is on; gameplay keeps the job and its class together, and the clock must
  pass 3 through (today's `MandrelStationClock` keeps only 1 and 2: `HollowOn = hollowClass is 1 or 2 ...`).
- **Winding.** Right-click held on the station with a rod on (tongs in hand, if the owner wants them) turns
  the held clock θ a turn a second, as the press brake's lever (`Folding.LeverTurnsPerSecond`), and W advances
  by `1 / wind.turnsPerRod` a turn: 4 turns, 4 s, a twenty-fourth of W (a side of the coil) every 1/6 s.
  Follow the press brake's `HeldWorkFollower` for the client's W, so the rod goes round smoothly. The model
  moves only with W; θ moves nothing.
- **Drawn.** The `rodsteel` parts (every part with that `requires`: `sp1u` .. `sp6e`, `sptail`) are drawn only
  while a spring job is on and p > 0, as `ShowsHollow` draws a hollow's; never the hollows' parts during a
  spring job, nor the spring's during a hollow's. They ride each other: the shared `RigAnimation` composes the
  chain (25 deep) as it does any ride. Their texture code is `spring`, declared on the block
  (`blocktypes/mandrelstation/frame.json`).
- **Drawn hot, the vanilla way.** The parts whose `requires` is in `wind.hot` (`rodsteel`) glow by the rod's
  temperature exactly as vanilla's renderers draw hot work (`AnvilPartRenderer`, `AnvilWorkItemRenderer`,
  `IngotMoldRenderer`, `ToolMoldRenderer`, in VSSurvivalMod 1.22.7; verified from their IL):
  `MandrelStationRenderer` already draws each part with the standard shader (`rapi.PreparedStandardShader`),
  so, for each hot part, before `MachineMeshes.Draw`:
  ```csharp
  int temp = (int)rod.Collectible.GetTemperature(world, rod);              // the rod's stack on the station
  float[] glow = ColorUtil.GetIncandescenceColorAsColor4f(temp);           // 0 below 500 °C
  int extraGlow = GameMath.Clamp((temp - 550) / 2, 0, 255);
  prog.RgbaGlowIn = new Vec4f(glow[0], glow[1], glow[2], extraGlow / 255f);
  prog.ExtraGlow = extraGlow;
  ```
  and `prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0); prog.ExtraGlow = 0;` for every other part (the frame, the
  mandrel, a hollow). The shader then mixes the part's lit colour towards that colour by extraGlow / 255 ×
  its alpha (`standard.fsh`): dull red at the working 751 °C (extraGlow 100), orange at 1000 °C (225), yellow
  past 1060 °C (255) and paler above 1100 °C; nothing below 525 °C. The client needs the rod's temperature:
  sync the stack (its attributes carry it) or the temperature with the job. The shadow passes need nothing.
- **The finished spring is hot too.** At delivery the spring takes the rod's temperature, the vanilla way:
  `spring.Collectible.SetTemperature(world, springStack, rod.Collectible.GetTemperature(world, rod))` (the
  stack's `temperature` attribute tree, which the game then cools and draws glowing in hand, on the ground
  and in slots). The spring's item type should carry steel's `combustibleProps` (`meltingPoint` 1502,
  `meltingDuration` 30, `smeltedStack` `game:ingot-steel`, `smeltedRatio` 1, as the rod's), so it melts back
  and cools as steel does.
- **Delivery.** At W = 1 the rod is used up and `wind.springsPerRod` (1) `wind.spring` goes into a container
  in `OutputNeighbour()`, else drops at `OutputDrop()`, as the sections do; then the station clears. The
  spring is wound on the mandrel at W 1, and nothing slides off: the renderer stops drawing the work at
  delivery (W held at 1, k held, p easing out, the parts not drawn: every gauge eases back with p, so a
  spring drawn then would unwind into a rod).
- **Taking back, breaking, saving.** As a hollow: Ctrl + right-click takes the rod back before W moves; a
  station broken mid-winding loses the part-wound rod, and one with an unwound rod drops it; a save keeps the
  job, W and the rod's stack (its temperature).
- **Infeed.** None suggested: a rod must come hot from the forge, so a container's cold rods are no use.
- **Sounds and particles** (gameplay's choice): none are in the rig. `strike.pos` is the hollow's; a
  creak of the rod at each side, or a hiss of the hot rod, would be at the coil's growing end.
- **Export and handbook.** A `machine` record like the hollows' (`mandrelstation|game:rod-steel|0`): the rod
  in, the mandrel kept, the frame as the station, one spring out, `power` `hand`, `turns` `wind.turnsPerRod`.
  The handbook's work and ladder sections should name the spring and the draw bench's use of it.
- **The draw bench** now needs one spring in its fourth stage (`../DrawBench/README.md`, "Stages").

## Open for the owner

1. **The bench's spring is bigger than the item.** The coil wound on the station's 4 × 4 mandrel has a 4 bore;
   the draw bench's spring goes round its 5.9 mandrel bar, so the bench draws the same coil (six turns, 0.6
   wire, steel) with a 6.0 bore. Keep it, or change the bench (a 4 × 4 spindle for the spring behind the
   follower) or wind springs on a bigger arbor?
2. **How the rod is wound.** It is fed along the mandrel's edge and bent across one face at a time, the rest
   following round the edges, rather than pulled off tangentially: a tangential end pulled over the top face
   would rise past the station's block (the mandrel's top is 1.4 below it). Acceptable?
3. **A steel rod is a mandrel too.** The suggested rule: a bare station fits it as the mandrel; a fitted,
   bare mandrel takes it as a spring job. Or keep steel out of the mandrels?
4. **Tongs, and a rod cooling mid-job.** Should winding need tongs in hand? The suggestion is that a rod
   cooling below 751 °C part-way stops the winding until it is reheated (or taken back), as the anvil does.
5. **Pace.** 4 s for any player and any tongs; nothing like the hammer's tier speeds it.
6. **The work's unit** still reads "hollows" (in the rig and the viewer) for a spring too.
7. **The rod past the tip.** In the last turns the straight rod reaches up to 6.7 voxels beyond the nose.
8. **Free length.** The free spring is 15.1 long (pitch 2.4, to fit the station's mandrel); at the end of a
   draw bench job its spring is stretched to 15.7 between the nut and the follower, 4 % past free.
