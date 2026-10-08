# Draw bench

Part of the Seraph Horizons mod (`../README.md`): the drawn rung of the pipe ladder (unified pipes,
`../Pipes/`). A mechanically powered **chain draw bench**, as used for lead and copper pipe from the
1790s to the 1880s, turns one **hollow section** of lead or copper (the game's chute section, named the tube blank in game,
`game:chutesection-{lead,copper}`, the 8 × 8 × 8 hollow box) into four seamless **pipe sections**
(`seraphhorizons:pipesection-{lead,copper}`: a square tube the size of ppex's pipe, 6 voxels across,
half a block long). The hollow goes on the bench threaded on a square **mandrel** bar and held against
the **die** by a spring follower. Each stroke draws a quarter of it through the square die, over the
mandrel's plug, into one section: the **dog** (drawing tongs on a portal that runs on two ways, one
either side of the section) grips the section's point at the die's mouth, and an endless **chain**
beside the bed, shackled to the dog, hauls it along. The chain's drive sprocket is turned from the
vanilla axle through a **rectifier**, a **cone friction clutch**, a two-speed **change gear** (lead
fast, copper at half the speed) and a **final drive**. The operator's **start lever** throws the clutch
in and shuts the jaws on the point; when the section's tail leaves the die the jaws spring open, the
section drops into the **trough** under the bed and slides north down it to queue behind the others,
and at the end of its travel the dog's lug knocks the clutch out. A **counterweight**, lifted during the
draw by a rope on a barrel geared to the return sprocket, falls and hauls the chain, and the dog, back
to the die.

This folder holds the model's generator and its rig (`tools/`), and the gameplay built against the rig
("Gameplay" below).

**A MachineOil machine.** The oiler's level is the rig's `oil` input, the fill (0..1) of the bench's
MachineOil tank: an oiler on the die stock drips onto the hollow just behind the die stock. It is only
the tank's face: it has no fill face or anchor of its own.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the mill, the rosser and
the gear cutter, and unchanged by this machine (no new driver: every motion is an existing one).

## The machine

**Footprint.** 4 cells: 1 wide (x), 1 high (y), 4 long (z), the long axis being the draw. In the native,
south-facing frame with the controller at `[0,0,0]`: x 0, y 0, z 0..3. The controller is the die end,
`[0,0,0]`, the block the player clicks. Placed, the bench extends away from the player along their line
of sight, the die end nearest them: the block's `side` is the way they look, since the bench runs along
native south (not the mills' `Footprint.PlacedFacing`, which sends native west along the look; see
"Gameplay"). Seen by the player who placed it, the axle comes in at the far end on their right (native
west), finished sections come out on their left (native east), and a chest of hollow sections stands in
front of the die end (native north).

**The draw line** runs along z at x 5, y 11.5 voxels (in blocks 0.3125, 0.719): the section's axis.

**The work, exactly** (voxels; the recipes' item shapes match these):

| Piece | Outside | Wall | Bore | Length | On the bench |
|---|---|---|---|---|---|
| Hollow section (`game:chutesection-*`, in) | 8 × 8 square | 1 | 6 × 6 | 8 | On the mandrel bar (5.9 square), z 10.4..18.4, against the die stock's back face |
| Pipe section (`seraphhorizons:pipesection-*`, out) | 6 × 6 square (0.375 block; ppex's pipe, its collision box 0.3125..0.6875) | 0.3 | 5.4 × 5.4 (the plug's 5.38) | 8 (0.5 block) | Axis along z; drawn out of the die's mouth, then queued in the trough |

A quarter of the hollow (2 long) is the metal of a section.

**Power.** The vanilla axle comes in on the west face of `[0,0,3]` (the drive head's cell), along x at
the cell's centre (y 8, z 56 voxels), into the head. Whichever way the axle turns, the rectifier turns
the clutch's cup the draw's way; the draw itself is always forward.

**Anchors** (in `assets/seraphhorizons/config/drawbench-rig.json`): `powerCell` and `powerFace`;
`infeedSide` north (a chest or hopper beyond the die end's north face feeds hollow sections) and
`outputSide` east with `output.pos` (`[0.9994, 0.2329, 0.26]`: the east face of the die end's cell
`[0,0,0]`, level with the first section in the trough, at the middle of its length; a finished section
leaves the east face there); `die.pos` (the die's mouth on the draw line, where the section emerges: dust
and lubricant smoke) and `drip.pos` (`[0.3125, 0.9719, 1.1281]`: the oiler's spout over the hollow, just
behind the die stock). There is no oil anchor: the oil is MachineOil's.

**Fitted parts** (the rig's `requires`; the frame item carries everything with `requires` null: the
oak beams and the iron ways, the trestles, the die stock and the tail stock, the trough and its rails,
the drive head's cheeks and bearings, the entry shaft and the rectifier, the start lever, the clutch rod
and its bell crank, the change gear's selector, the return shaft, the barrel shaft with its barrel, the
counterweight and its rope, and the oiler). The **build order**: the frame, then `gearbox`, `chain`,
`dog`, `mandrel` and `die`:

| Order | `requires` | Item | Draws | Taken back |
|---|---|---|---|---|
| 1 | `gearbox` | `game:jonasframes-gearbox01` (Jonas sub-assembly, "Consumately crafted gearbox"), looted or converted | The cone clutch's cup (cupronickel, the Jonas palette) and cone, the sleeve with the sliding change-gear cluster, the drive shaft with its two change wheels and the final drive's pinion | Only by breaking the frame |
| 2 | `chain` | Two of `game:metalchain-iron`, `-meteoriciron` or `-steel` (the game's chain) | The endless drawing chain and its two sprockets: the drive sprocket on its own shaft with the final drive's wheel, and the return sprocket on the return shaft | Only by breaking the frame |
| 3 | `dog` | `game:bracket-heavy-iron`, `-meteoriciron` or `-steel` (the game's heavy bracket) | The dog: its portal (shoes on both ways, cheeks, crossbar and top plate over the section), the fixed jaw, the moving jaw with its tail and knuckle and its leaf spring, the bracket and shank with two shackle pins, the knock-off lug | Only by breaking the frame |
| 4 | `mandrel` | Two of `game:rod-iron`, `-meteoriciron` or `-steel` | The square mandrel bar with its plug and nut, the follower and its spring | Only by breaking the frame |
| 5 | `die` | New: `seraphhorizons:drawdie-iron` or `drawdie-steel` (iron: lead only; steel: lead and copper), the wearing part | The die plate with its square eye in the die stock's mouth. Its elements use the texture code `die`, which the renderer sets to the fitted die's metal | When worn out it is spent; unworn, Ctrl + right-click |
| — | `billetlead` | The work: a lead hollow section on the bench (k = 1) | The lead hollow (in quarters) and the four lead pipe sections drawn from it | The material being worked, not a part |
| — | `billetcopper` | The work: a copper hollow section (k = 2) | The same in copper | as above |

There is no cover: the gearing is open, as a period bench's. The viewer shows `billetlead` only with
lead chosen and `billetcopper` only with copper (`requiresClass`); a renderer shows one of them while a
job of that metal is on the bench, from loading until it is cleared.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The hollow wears the chute section's own sheet (`game:block/metal/sheet/lead1`,
`copper1`, as the game's item has it), the pipe sections the plain sheet
(`game:block/metal/sheet-plain/lead1`, `copper1`), the rope `game:item/resource/rope`, the oiler's glass
`game:block/glass/plain` (four panes in the Transparent render pass, `renderPass` 3, so the oil shows from
every side; the block's own pass is opaque and drew the glass solid) and its oil `game:block/liquid/honey`,
all referenced. The hollow's quarters and the boxes of each quarter's walls wear one sheet: each face's UVs
are its place on the whole hollow (`sheet_uv`, from the game's own face mapping, `FACE_SHEET`), so the
texture runs along the hollow and round its corners unbroken and the seams between quarters do not show
(each piece starting the texture afresh drew it in 2-voxel bands). The drawn sections and the frame keep
a texture per face from its corner. The frame block's `textures` (`assets/seraphhorizons/blocktypes/drawbench/frame.json`) must
name every code of `drawbench.json`: the renderer draws the moving parts with the block's texture source,
and a code the block lacks renders white (`tools/tests/test_drawbench_model.py` holds them together). `tools/make_shape.py`
writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/drawbench.json` | The whole machine, every moving part (739 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/drawbench_frame.json` | The static frame only (67 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/drawbench-rig.json` | Footprint, anchors, the work, the draw's constants and the part rig (84 parts: 20 chain links of their own, 24 pieces of work). |
| `tests/DrawBench/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y
up, z north to south) in voxels, measured from the machine box's north-west bottom corner. The
controller is the build frame's own corner cell, so the shipped files are the build frame divided by 16.

**The bed.** Two oak beams run the bed's length (y 1..7.2), at x 0.2..1.6 and 8.4..9.8, with an iron way
on each (to y 8) from the die stock on: the dog's portal runs on them, either side of the section. Oak
ties join them at two trestles (the tail end and before the head). Between and under them is the
**trough**: two iron rails (x 2.3..2.9 and 7.1..7.7) whose top runs down to the north (y 0.6 + 0.03 z)
to a stop at the tail end. The **tail stock** (z 0.3..1.8) holds the mandrel bar; the **die stock** (z
18.4..23.4, x 0.6..9.4, up to y 16, its keel down between the ways to y 7.4) is a cast-iron block on
feet on the beams, with the die plate (z 23.4..24.2) in its mouth. The **drive head** (z 41..58.6) is an
iron bed plate with two cast cheeks and bearings on posts.

**The work.** The square mandrel bar runs from the tail stock along the draw line to its plug in the
die's throat (z 22.8..24). Over it: the follower's spring (six coils from the tail stock), the follower
(z 9.8..10.4) and the hollow section, its bore on the bar, to the die stock's back face (z 10.4..18.4).
As a section is drawn, the quarter of the hollow at the die goes into the die stock (where it is hidden)
and the follower pushes the rest up behind it: the hollow shortens by a quarter a stroke. It is drawn as
four pieces end to end (`lslug1`..`4`), one a quarter, under one sheet of texture. A section is drawn as two segments, each hidden in
the die stock until the section's growing length brings it out of the die's mouth (z 24.2). Before its
stroke, each section's point (2 voxels) is through the die, in the dog's open jaws.

**The stroke.** The dog's jaws shut on the point (the start lever's finger presses the moving jaw's
knuckle) and the dog travels 8 voxels south: 6 draw the section (its tail leaves the die), 2 more carry
the dog on. When the pull goes, the leaf spring opens the jaw; over the last 1.6 the dog's lug (an arm out
east from the dog's shank, down to an eye round the clutch rod) carries the rod's collar south and the
clutch goes out. The free section tips to the trough's slope and drops about 7 between the ways onto the
rails, then slides north down them to its place in the queue: the first 0.02 from the stop, the others
end to end behind it 0.024 apart along the rails (their centres at z 4.16, 12.18, 20.2 and 28.22). The counterweight then
falls and the dog runs back to the die, and after a short pause the next section's point comes through
(see below).

**The cycle, per section** (t is the fraction of a section's cycle, W = m + t for the m-th section):

| t | What moves |
|---|---|
| −0.08..0.00 | The next section's point comes through the die (the first as the hollow is loaded). |
| 0.000..0.075 | The start lever turns 32°: its arm pulls the clutch rod north 1.6 (through the bell crank the cone goes 0.6 into the cup) and its finger pushes the jaw's knuckle south: the jaws shut on the point. |
| 0.075..0.450 | The chain hauls the dog 8 south; the section comes out of the die at the dog's pace; the hollow's quarter at the die goes in and the follower pushes the rest up; the weight rises 2.43. |
| 0.356 | The section's tail leaves the die. |
| 0.356..0.431 | The jaws spring open. |
| 0.375..0.450 | The lug carries the clutch rod's collar 1.6 south: the clutch goes out, the start lever comes back. |
| 0.431..0.471 | The section drops into the trough, tipping to its slope. |
| 0.471..0.530 | It slides north down the rails to its place in the queue. |
| 0.530..0.905 | The weight falls 2.43 and hauls the chain, and the dog, back to the die (the clutch's sleeve and change gears turn back with the chain; the cup turns on, out of engagement). |
| 0.905..1.000 | A short pause; the next point comes through the die from 0.92. |

The draw takes 0.375 of the cycle (`SPAN` in `make_shape.py`) and the return as long; the start and the
jaws' opening are as long as the knock-off (0.075, a gauge's two eases being equal).

**The drive** (θ the axle's angle, ψ its travel either way, W the sections drawn, k the metal):

- *The rectifier.* The entry shaft (oak, the vanilla axle's cross profile, then a round steel shaft)
  carries A1 (12 teeth) and A2 (10), module 0.3. A1 meshes B1 (24) directly; A2 meshes B2 (20) through an
  idler (10) on a stud from the east cheek. B1 and B2 are loose on the rectified shaft beside it, each
  with a one-way catch; whichever turns the draw's way carries the shaft, so the rectified shaft turns
  forward at ψ / 2 whichever way the axle turns.
- *The cone clutch.* The cup is keyed to the rectified shaft (it turns with it always). The cone slides
  on the sleeve, which is loose on the shaft; the bell crank's fork in the cone's groove throws it 0.6
  into the cup. The cone's faces are smooth, and that is where the axle-driven half meets the half the
  work drives: at the drawn pace the two turn together (a check holds this for both metals), and a
  gameplay pace other than the drawn one shows nowhere.
- *The change gear.* The cluster slides on the sleeve: A (16 teeth) and B (10), module 0.25. On the drive
  shaft, A' (24) and B' (30). For lead the cluster stands west, A in mesh with A' (the sleeve turns 1.5
  times the drive shaft); for copper the selector slides it 3.4 east, B in mesh with B' (3 times), so a
  copper section takes twice the axle's turns. The pair not in use stands clear.
- *The final drive.* A wheel (16 teeth, module 0.4) on the drive shaft's west end meshes a pinion of 8 on
  the sprocket shaft (y 4, z 44.4), which runs east under the head, in the west cheek and a bearing on a
  post, to the drive sprocket outside the east beam: 2:1 up, the sprocket turning twice the drive shaft,
  against it. (The pair's centre distance is the old 12:12's, so nothing else moved.)
- *The chain.* The drive sprocket (10 teeth, the pins on a circle of 2.6) and the return sprocket (on the
  return shaft, under the die stock's east side, z 20) carry an endless chain of 37 links (pitch 1.607)
  at x 10.8, outside the east beam, both ends shackled to the dog's shank 4.93 apart. The return shaft's
  gear turns the barrel shaft over it 1:1; the counterweight's rope is wound on the barrel (radius 0.75),
  the weight hanging east of the chain.

The axle turns **2.06 times a lead section** and **4.12 times a copper section** at the drawn gearing
(the rig's `draw.turnsPerSection`): four sections, a hollow, in 8.2 turns (lead) or 16.5 (copper). The
stroke takes 0.375 of a section's cycle and the return as long; the pause is under a tenth. (It was 7.72
and 15.45 with a 1:1 final drive and a stroke of 0.2 of the cycle, half of it a dwell: the regearing
halves the turns a stroke, the shorter dwell takes the rest, 3.75 times as fast.)

Every moving part (θ the axle angle, ψ its travel, W sections drawn, k the metal: 1 lead, 2 copper, p
its presence, oil the tank's fill). A gauge's windows are one per section:

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft, A1, A2 (`entry`) | The vanilla axle | B1; the idler | `rotate` x 1 (θ) |
| B1 with its catch (`rectb1`) | A1 | The rectified shaft when the axle turns + | `rotate` x −1/2 (θ) |
| Idler (`idler`) | A2 | B2 | `rotate` x −1 (θ) |
| B2 with its catch (`rectb2`) | The idler | The rectified shaft when the axle turns − | `rotate` x 1/2 (θ) |
| Rectified shaft (`rectshaft`) | Whichever catch bites | The cup | `rotate` x 1/2 (ψ) |
| Cup (`cup`) [gearbox] | Keyed to the rectified shaft | The cone, while in | `rotate` x 1/2 (ψ) |
| Cone (`cone`) [gearbox] | The cup (friction); the crank's fork | The sleeve (a feather) | the sleeve's `gauge`; `gauge` slide x 0.6, in at the start, out at the knock-off |
| Sleeve (`sleeve`) [gearbox] | The cone | The cluster | `gauge` rotate x 1.5 or 3 × the drive shaft's turn, out in the draw and back in the return |
| Change-gear cluster (`cluster`) [gearbox] | The sleeve (a feather); the selector's fork | A' (lead) or B' (copper) | the sleeve's `gauge`; `gauge` slide x 3.4 for copper (`present`) |
| Drive shaft, A', B', the final drive's wheel (`driveshaft`) [gearbox] | The cluster | The final drive's pinion | `gauge` rotate x −1.62 rad (half the sprocket's turn), the draw and back |
| Sprocket shaft, the final drive's pinion, the drive sprocket (`drivesprocket`) [chain] | The final drive | The chain | `gauge` rotate x 3.24 rad (8 / the chain's radius) |
| Chain's links on the top run (`chaintop`), on the bottom run (`chainbottom`) [chain] | The dog's shank / the loop | — | `gauge` slide z ±8, the draw and back |
| Chain links that go round a sprocket in a stroke (`ch04`..`ch13`, `ch24`..`ch33`) [chain] | The loop | — | one `gauge` per piece of the loop crossed: slide along a run, rotate round a sprocket's centre |
| Return sprocket (`returnsprocket`) [chain] | The chain | The return shaft | as the return shaft |
| Return shaft with its gear (`returnshaft`) | The return sprocket | The barrel shaft | `gauge` rotate x 3.24 rad, the draw and back |
| Barrel shaft, gear, barrel (`barrel`) | The return shaft (1:1); the weight's rope in the return | The rope | `gauge` rotate x −3.24 rad |
| Counterweight (`weight`) | Lifted by the rope in the draw; falls in the return | The barrel, hence the chain and the dog | `gauge` slide y 2.43 |
| Rope pieces (`rope1`..`rope5`); the rope's top (`ropetop`) | The weight (each piece is taken up into it as it rises) | — | `gauge` slide y; `ropetop` none |
| Dog (`dog`) [dog] | The chain | The jaws, the clutch rod's collar | `gauge` slide z 8, the draw and back |
| Moving jaw (`jaw`) [dog] | The start lever's finger (shut), its leaf spring (open) | The section's point | `gauge` rotate y about its pin, shut at the start and open as the section's tail leaves the die; rides `dog` |
| Start lever: rock shaft, finger, rod arm, handle (`startlever`) | The operator (in); the clutch rod (back) | The clutch rod; the jaw's knuckle | `gauge` rotate x 32°, in at the start, back at the knock-off |
| Clutch rod, collar, stop (`clutchrod`) | The start lever (in); the dog's lug (out) | The bell crank | `gauge` slide z −1.6 |
| Bell crank and fork (`crank`) | The clutch rod | The cone | `gauge` rotate y 15.7° |
| Change-gear selector (`selector`) | Set for the metal (the operator) | The cluster's fork | `gauge` slide x 3.4 for copper (`present`) |
| Mandrel bar, plug, nut (`mandrel`) [mandrel] | Fixed | — | none |
| Follower (`follower`) [mandrel] | Its spring | The hollow | `gauge` slide z 2 a stroke, over the section's draw (four, open to the end) |
| Spring coils (`spring1`..`spring6`) [mandrel] | The follower | — | `gauge` slide z, each its share of the follower's travel |
| Die (`die`) [die] | Fixed | — | none |
| The hollow's quarters (`lslug1`..`4`) [billetlead], (`cslug1`..`4`) [billetcopper] | The follower; the die | — | `gauge` slide z 2 a stroke, one stroke more each quarter, into the die stock |
| Section segments (`lsect1a`..`lsect4b`) [billetlead], (`csect1a`..`csect4b`) [billetcopper]: section 1..4, segment a, b | The dog (drawn out of the die); the trough | — | point: `gauge` slide z 2 before the stroke; `gauge` slide z with the dog from when the segment reaches the mouth; the drop: `gauge` rotate x to the trough's slope and slide y; then slides z and y north down the rails to its slot |
| Oil in the cup (`oillevel`) | The machine's oil tank (MachineOil) | (shows it) | `stretch` y, 0.05 → 1.15 tall (oil) |
| Frame (`frame`) | Static | — | none |

The rig's `draw` holds `turnsPerSection` (per metal), `sectionsPerHollow` (4; each a
`seraphhorizons:pipesection` of the hollow's metal) and the hollow each metal's work is (`hollows`:
`game:chutesection-lead`, `game:chutesection-copper`). The gameplay's pace should take its default from
`turnsPerSection`, or a test should hold the two together, as the gear cutter's `cut.turnsPerTooth` and the
rosser's `feed.gear` are.

Toothed wheels and what they mesh (nothing toothed meshes nothing): A1 ↔ B1; A2 ↔ idler ↔ B2; A ↔ A'
(lead) or B ↔ B' (copper), the other pair standing clear as a sliding change gear's does; the final
drive's wheel ↔ its pinion; the return gear ↔ the barrel's gear; both sprockets ↔ the chain. The cup, the
cone, the barrel and the hubs are plain.

### Regenerating

```sh
python3 mods-src/seraphhorizons/DrawBench/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/DrawBench/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/DrawBench/tools/make_shape.py --quick      # skips the z-fighting fix (not for files that ship)
```

A full run takes about a minute, most of it the swept paths and the z-fighting fix. `tools/validate_drawbench.py` holds the checks; every run checks its output
and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part.
- **Containment:** nothing leaves the 1 × 1 × 4 box over 73 poses of both metals.
- **Anchors:** the entry shaft meets the power face at the power cell's centre.
- **Textures** by role: oak timber and the axle's continuation; iron castings, ways, rails, weight, the
  dog's body and the chain; steel shafts, gears, the mandrel, the jaws and the rods; a cupronickel clutch;
  the die's own code; lead and copper work; rope; a glass and brass oiler with oil in it.
- **Gearing:** every meshing pair's centre distance is the sum of the pitch radii, and the arcs rolled on
  the two pitch circles are equal (finite differences of the posed rig): the rectifier's three meshes, both
  change-gear pairs (each with its own metal), the final drive, the return gears. The rectifier: for either
  sign of the axle exactly one loose wheel turns with the rectified shaft, the draw's way. The clutch: the
  cone turns with the cup through the draw, at each metal's drawn pace.
- **Chain:** every link on the chain's line, along it, as far round as the dog has gone, at 15 poses; both
  sprockets turning with the chain.
- **Work:** the section being drawn whole from the die's mouth to its point in the jaws, the rest of it in
  the die stock; the hollow's quarters end to end from the follower to the die stock; the spring's coils
  sharing the follower's travel; the four sections in the trough, each in its slot, a face flat on the
  rails and touching them, the first 0.02 from the stop and the others end to end behind it.
- **Bore:** between strokes the die's bore is empty, and the face seen in it (the frontmost hidden
  segment's) stands at least 0.01 clear of whatever is behind it.
- **Weight:** it rises as the barrel winds its rope, and the rope reaches from the barrel to the weight's
  cap at every pose, without a gap.
- **Controls:** the start lever's finger on the jaw's knuckle at rest and pushing it shut (within 0.035);
  the lug meeting the collar exactly as the rod starts out and carrying it (0.80 short just before); the
  rod's stop on its guide at rest; the bell crank's fork carrying the cone (within 0.003); the jaws open at
  rest, shut on the point from the start to the section's release, open after.
- **Supports:** every shaft in two bearings or more (entry 2, rectified 3, drive 2, sprocket 2, return 2,
  barrel 2, the start lever's rock shaft 2), the idler on its stud, the clutch rod through its guide (its
  north end pinned to the start lever), the selector rod through one guide (two for copper) with its fork
  in the cluster's groove as a pilot.
- **Oiler:** the oil's height 0.05 at oil 0, 1.15 at 1 and linear between, inside the glass; a pane over
  each side of the oil, every pane in the Transparent pass (`checks.sight_glass`); the spout's
  mouth 0.05 over the hollow, 0.35 behind the die stock, the drip anchor there.
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at five poses of both metals, of what can
  be seen: another metal's work, the work hidden in the die stock and the rope taken up into the weight
  are left out of both the fix and the check (`shown`).
- **Swept paths** (full runs): the dog through its whole stroke and back, finely, and the section through
  its drop and slide, every 0.005 of a cycle, for both metals and the first and last section (532 poses).
- **Clearances:** over 211 poses (both metals, every phase of the cycle, all four sections) no two parts
  touch except the intended contacts listed in `ALLOWED`.
- **Files:** every texture declared; lids over every column; the shipped model is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
rosser's and the gear cutter's are.

### Rig schema (`drawbench-rig.json`)

The gear cutter's schema (`../GearCutter/README.md`, "Rig schema"), with nothing added to the shared rig
maths: a `work` quantity, gauges, rotations on θ and ψ, and a `stretch` on the oil.

- **The work.** `{ "name": "sections drawn", "unit": "sections", "step": 0.005, "end": { "thin": 4, "thick": 4 } }`.
  W counts sections drawn: one unit is one stroke's whole cycle, and W = 1.5 is halfway through the second
  section's. The job ends at 4 for both metals (one hollow, four sections).
- **Inputs.** θ and ψ as the mill's; W, k and p; and the oil. k is the hollow's metal (1 lead, 2 copper,
  0 none); p its presence, eased as the hollow is loaded and cleared, k held while p eases out. The
  gameplay advances W with the shaft at `1 / turnsPerSection[k]` per turn from 0 when a hollow goes on, and
  holds it at 4 when the job is done; it delivers the sections (each drops into the trough at W = m +
  0.43..0.53), clears the bench (p eases out with W held at 4: the follower and its spring go back) and
  starts the next hollow at W = 0.
- Every motion of a stroke is a `gauge` with one window per section, rising over the draw and falling over the
  return (the return is as long as the draw, so a window's two eases are equal); parts that go one way
  through a job (the hollow's quarters, the sections, the follower and the spring) have windows open to the
  end of the job (`to` 1000) and come back with p. The change gear's setting is a `present` gauge.

**Keys.** `cells`, `powerCell` and `powerFace`, `infeedSide`, `outputSide`, `output`, `die`, `drip`,
`work`, `draw` (`turnsPerSection`, `sectionsPerHollow`, `hollows`) and `parts`. `requires` values:
`gearbox`, `chain`, `dog`, `mandrel`, `die`, `billetlead`, `billetcopper`, or null. The die's texture code
is `die`.

**In the viewer** (`site/models.json`, the site's model page): the work's slider reads in sections (the rig's
unit and step) and stops at 4, the size select names the metals ("Lead", "Copper"), each metal's work shows
only with its own metal (`requiresClass`), the oil has a slider, and Play turns the axle at the reader's
input speed and moves W with it at lead's pace (`play.turnsPerWork` `"draw.turnsPerSection.thin"`; in the game
copper's is twice that, which the viewer does not show but the cone's smooth faces would hide).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `rectb1_*`,
`idler_*`, `rectb2_*`, `rectshaft_*`, `cup_*`, `cone_*`, `sleeve_*`, `cluster_*`, `driveshaft_*`,
`selector_*`, `startlever_*`, `clutchrod_*`, `crank_*`, `drivesprocket_*`, `returnsprocket_*`,
`chaintop_*`, `chainbot_*` (the part `chainbottom`), `ch04_*`..`ch13_*`, `ch24_*`..`ch33_*`,
`returnshaft_*`, `barrel_*`, `weight_*`, `rope1_*`..`rope5_*`, `dog_*`, `jaw_*`, `mandrel_*`,
`follower_*`, `spring1_*`..`spring6_*`, `die_*`, `lslug1_*`..`lslug4_*`, `lsect1a_*`..`lsect4b_*`,
`cslug1_*`..`cslug4_*`, `csect1a_*`..`csect4b_*`, `oillevel_*`, `ropetop_*`, and `fr_*` for the frame
(the oiler's glass, base, cap and spout are `fr_oiler_*`, the head's cheeks `fr_cheek_w` and
`fr_cheek_e`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's own viewer
(a standalone copy, `site/scripts/standalone-viewer.ts`). No one has yet looked at it in a client.

1. **A section is drawn in two segments.** Each comes out of the die stock as the section's length reaches
   it; the segments are a hair smaller one after another (0.012 voxels a segment) so that hidden ones never
   share a face. The hollow enters the die stock a quarter at a time, whole, rather than thinning into the
   die, and nothing is drawn between its 8-wide box and the 6-wide tube.
2. **The bench is wider than its draw.** The 8-wide hollow and the dog's portal round it fill the cell's
   width between the ways; the chain, the weight and the controls stand outside the east beam, in the same
   one-block cell, so the bench is crowded on that side.
3. **Linear ramps.** The gauges move in straight ramps: the dog starts and stops at full speed, the jaws and
   the start lever snap over 0.075 of a cycle, the section drops and slides in straight lines.
4. **Box teeth and links.** The gears' teeth, the sprockets' teeth and the chain's links are boxes; the
   links are drawn round a sprocket at the chain's kinematic radius, so their pins sit on the pitch circle
   only at a link's ends.
5. **Contacts made of arcs.** The start lever's finger pushes the jaw's knuckle along an arc and slides
   down it (within 0.035 along the push); the bell crank's fork slides 0.08 round the cone's groove as it
   throws it.
6. **The return is as fast as the draw.** A window's rise and fall have one ease, so the weight hauls the dog
   back at the draw's speed, and the start lever and the jaws' opening take as long as the knock-off. The
   bench draws for 0.375 of a section's cycle and pauses for under a tenth of it.
7. **The jaws open by a spring** when the section's tail leaves the die (the pull goes): drawn, not forced.
8. **The change gear's pair not in use** stands clear (a sliding gear's idle pair), and the selector's
   setting is the operator's, eased in with the hollow's presence.
9. **Lead and copper look the same** but for their texture: the hollow and the sections are one geometry.

## Gameplay

The rules are game-independent in `Core/` (`DrawBenchParts.cs`, `Drawing.cs`, `DrawBenchConfig.cs`,
`DrawBenchRig.cs`, and `DrawBenchView.cs`: the renderer's view of the bench and its clock); the game side
is `Game/` (`DrawBenchSystem`, the controller `BlockDrawBench` and `BEDrawBench`, the ghosts
`BlockDrawBenchGhost` and `BlockDrawBenchGhostPower` with `BEDrawBenchGhost`, the load
`BEBehaviorDrawBenchMP`, and `DrawBenchRenderer`), copied from the gear cutter's
(`../GearCutter/README.md`, "Gameplay"). The mod's README, "Draw bench", is the player-facing summary;
the decisions are here. Everything is read from the rig through its anchors and `requires`, never
element names.

**Blocks.** `seraphhorizons:drawbench-frame-{side}` (`assets/seraphhorizons/blocktypes/drawbench/frame.json`,
drawing `drawbench_frame.json`; its textures are every texture of both shapes, `die` included, since the
renderer draws the parts with the block's texture source), `drawbench-ghost` and
`drawbench-ghostpower-{side}`: placement, ghosts, ghost repair, breaking through a ghost and the boxes
(selection from the rig's cells, collision with their lids) are the gear cutter's. **Placing** differs:
the bench runs along native south, so the `side` placed is the way the player looks
(`DrawBenchRig.PlacedSide`), and the bench runs away from them with the die end, the block they clicked,
nearest. (`Footprint.PlacedFacing`, the mills' rule, sends native west along the look, which would lay
the bench across it.) The axle then comes in at the far end on the player's right, the pipe sections come
off to their left, and a chest of hollow sections stands in front of the die end. The frame recipe
(`assets/seraphhorizons/recipes/grid/drawbench.json`): two metal plates (die stock and the drive head's bed
plate, in one slot) and four rods (the ways, two a side) of iron, meteoric iron or steel, 12 nails and
strips of the same metals, two steel gears (`seraphhorizons:gear-steel`, for the gearing the frame carries:
the rectifier's A1, A2, B1, B2 and idler, the return shaft's gear and the barrel gear; every other machine
in the pack pays steel gears for its gears, #473), two oak logs (the sills), oak planks (the bed) and a
hammer: about 11 ingots of iron (a plate 2, a rod 1, 12 nails and strips 3), under the steel gear cutter's
frame (8 steel ingots, 32 nails and strips and 3 steel gears).

**Stages** (`DrawBenchParts`), in `DrawBenchStage` order, the next missing stage the only one a click
fills (a later stage's item is `OutOfOrder`, one whose stage is in `AlreadyFitted`, a die with no
durability `DieSpent`). A stage takes `Needed` of its item, all from the held stack in one click (fewer
held is `TooFew`, with a message, and nothing is taken): two chains (the endless chain runs round both
sprockets the bench's length), two rods (the mandrel bar, and the follower's spindle), one of the rest.
Breaking returns as many as went in; a save keeps one code a stage, as before. With the frame (about 11
iron ingots) and the die (2), the parts bring the bench to about 21 ingots of iron and a Jonas gearbox:
the chains 4, the bracket 2, the rods 2.

| # | `requires` | Item (verified in game 1.22.7's `survival/itemtypes`) |
|---|---|---|
| 1 | `gearbox` | `game:jonasframes-gearbox01` |
| 2 | `chain` | `game:metalchain-iron`, `-meteoriciron` or `-steel`, two |
| 3 | `dog` | `game:bracket-heavy-iron`, `-meteoriciron` or `-steel` |
| 4 | `mandrel` | `game:rod-iron`, `-meteoriciron` or `-steel`, two |
| 5 | `die` | `seraphhorizons:drawdie-iron` or `-steel` (`assets/seraphhorizons/itemtypes/drawbench/drawdie.json`, shape `assets/seraphhorizons/shapes/item/drawdie.json`; smithed from two ingots of its metal, 80 voxels (two thick rings and a stepped shoulder), named `drawdie` so the helve hammer leaves it alone) |

The die keeps its durability left and full (`GetRemainingDurability`, `GetMaxDurability`); the die
item's durability is `DieDurability`, set on the server in `AssetsFinalize` before the types go to
clients (the file's 100 is the default). Ctrl + right-click takes the die back, with a `durability`
attribute when worn, and only while no hollow is on the bench (`CanTakeDie`; with one on, an error); the
other stages come back only by breaking the frame, which drops every part, the die with its wear, and the
hollow if no pipe section has come off it yet (once one has, the rest of it is lost). The creative shortcut
(Ctrl in creative mode on an incomplete bench) fits each stage's first code, the steel die last. A save
restores the stages as a run from the first; a die with nothing left is dropped.

**The draw** (`Drawing`, `DrawJob`). The work is a hollow section, the game's chute section
(`game:chutesection-lead` or `-copper`), threaded on the mandrel, and what comes off is four pipe
sections of its metal, `seraphhorizons:pipesection-lead` or `-copper` (the owner's final chain v2,
2026-10-07: two angles and solder make a hollow section on the grid; a hollow gives two pipe sections on
the mandrel station or four here; a pipe section and solder make a pipe; earlier passes that day had an
ingot in and chute sections out, and before that a forged section in and pipes out). A hollow goes on
only on a complete bench, one at a time, and only of a metal the die draws (`DieMetals`: iron `lead`;
steel `lead`, `copper`; a server can change them), and only when its pipe section exists in the game.
The lead hollow is `UnifiedPipes`' state on the game's item (`patches/unifiedpipes-chutesection.json`);
with that switch off there is none to put on. An ingot, an angle or a pipe section is never taken, by
hand or from the infeed (`ClassOfHollow` is 0 for each: the click is the item's own business). Class k
is 1 for a lead hollow, 2 for a copper one (the rig's `draw.hollows`; `draw.sectionsPerHollow` is 4;
the reader takes only these keys, and refuses a rig without them). W runs from 0 to 4 by
`SectionsFor(radians, TurnsPerSection(k))` while `Drawing.Running` (complete, a hollow on, the shaft at
`MinSpeed`); `BEDrawBench.Draw(radians)` is the step (the server's 50 ms tick calls it with the shaft's
advance; the Atlas scenarios call it to finish a hollow). Each time W crosses 1, 2, 3 and 4 one pipe
section goes into a container in `DrawBenchRig.OutputNeighbour()` (the cell beyond `output.pos` across the
output face: native east of the die end's cell, where the model's trough brings them), else drops at
`OutputDrop()`, pushed outward, as the gear cutter's gears do; the tank drains `DrainPerJob` (2) a pipe
section, 8 a hollow. (The model drops each section into its trough from W = m + 0.43; gameplay delivers at
the whole number, a stroke's return and dwell later, so a section is never handed out before its cycle
is over, and the count is the job's own `SectionsDone`.) At W = 4 the hollow is used up and the die wears
`DieWearPerHollow` (1), whatever the oil (the export's `wear.rule` `fixed`): at 0 it is gone with the
tool-break sound and the bench stops until a new die is fitted. The bench then clears, and the infeed
(the cell in front of the die end, `InfeedNeighbours`, a container slot holding a hollow the die draws)
waits 0.6 s before the next hollow, so the model eases the last one out first; it feeds on the slow
tick, while the shaft turns. The server syncs W every 0.05 section and at every section.

**Oil and load.** `Oil.NewOwn(api, OilMachine.DrawBench)`: the tank (1000), pouring, saving, syncing and the
smoke are MachineOil's, with the standard dry rule: `BEBehaviorDrawBenchMP.GetResistance` is
`ResistanceLead` (0.2, empty or drawing lead) or `ResistanceCopper` (0.35) through `Oil.Asked`, three
times while dry; an unassembled frame puts 0.005. The machine oil page lists the bench and its drain in
passages of its own, which `DrawBenchSystem.LangEdits` drops when the switch is off; the gear cutter's
edits touch other passages, so the page reads right in every on/off combination of the two.

**Renderer** (`DrawBenchRenderer`, through `IDrawBenchView`: facing, fitted parts, die metal, job class and
the server's W, running, shaft angle and speed, oil fill, turns a section). Every rig part with a `requires`, a
ride or a driver, from `drawbench.json`, drawn when its stage is fitted; the die's parts are meshed per die
metal with `die` set to `game:block/metal/ingot/{metal}`. `DrawBenchClock` holds the renderer's W, k and p:
W advances with the shaft while running, never behind the server's W and at most 0.1 section ahead of it;
with no hollow on, W is held at 4 and k while p eases out over 0.4 s (`billetlead` and `billetcopper` are
drawn while their metal is the clock's class and p > 0), and a new hollow starts from the server's W. θ and ψ come
from the power ghost's angle about native x (`MillMotion.NativeShaftAngle(.., Axis.X)`). While drawing:
metal dust (lead grey, copper red) and, oiled, a wisp of burning lubricant at `die.pos`, drips from the
oiler at `drip.pos`, and the gears' sound every 1.8 s; a dry bench smokes at the die.

**Settings** (`DrawBenchSettings`): `DieDurability` 100 (hollows a die draws), `DieWearPerHollow` 1, `TurnsPerSectionLead` 2.06 and
`TurnsPerSectionCopper` 4.12 (the rig's `draw.turnsPerSection`, held to it within 0.01 by
`DrawBenchRigTests.The_default_pace_is_the_rigs`), `ResistanceLead` 0.2, `ResistanceCopper` 0.35, `MinSpeed`
0.05 and `DieMetals`; each falls back to its default with a warning when out of range, and `DieMetals`
keeps only lead and copper and both dies. The oil's are `MachineOilSettings.DrawBench` (tank 1000, 2 a
pipe section). The client uses the server's `MinSpeed`, turns a section and the fitted die's metals (synced in
the tree).

**Switch.** `DrawBench` (default on): off, the server marks the block types, the die's item type and the
recipe files (`recipes/grid/drawbench.json`, `recipes/smithing/drawbench.json`) disabled before the game
loads them, and both sides strip links to the bench and the dies from the mod's own text. The classes are
registered either way. Switch ownership: the type files and recipe files, and the export's `drawbench`
recipe type (`SwitchOwnership.HandListed`).

**Export.** `tools/recipe-export/Recipes/DrawBenchExport.cs` and `RecipeSection.DrawBench.cs`: one `machine`
record per metal (`drawbench|game:chutesection-{metal}|0`): the hollow section, the four kept stages
(alternatives as variant stacks, the chain's and the mandrel's two each), the dies that draw the metal as a tool losing `DieWearPerHollow` a
hollow (`rule` `fixed`), the oil for four pipe sections, the frame as the station; four
`seraphhorizons:pipesection-{metal}` out (work unit `sections`, end 4); `turns` and
`work.turnsPerUnit` from the settings.

**Open questions, decided the simplest way.** The die wears when the hollow is done (a spent die still
finishes its last hollow); the hollow cannot be taken back once it is on (a half-drawn hollow is lost
when the frame is broken); meteoric iron makes no die (the smithing recipe allows iron and steel only; any steel
ingot, blister route included, makes a steel die); the frame has no schematic gate; the item-values entries
for the frame and the dies come from the next `itemvalues.py build`.

**Tests.** `tests/DrawBench/DrawBenchGameplayTests.cs` (no game): the build order and what each stage takes,
order and repeats refused, the die's metal, durability, take-back and wear, drops and saves, the creative
order, the draw's arithmetic (a section at each whole W to 4, big steps, the end held), hollows in and
ingots, angles and pipe sections never, the die gating, a die's 100 hollows, the renderer's clock, the settings and their sanitising, and the rig reader on a hand-trimmed
rig (`tests/DrawBench/fixtures/drawbench-rig-test.json`). `tests/DrawBench/DrawBenchRigTests.cs` parses the
shipped rig with the shared `RigParts` and `RigProgress` and the gameplay's reader, checks the
vocabulary both ways, replays every reference pose of `tests/DrawBench/rig-reference.json`, and holds the
settings' pace to `draw.turnsPerSection` (each test skips, saying why, while its file is not generated).
`tests/PackTests/DrawBenchScenarios.cs` (Atlas, the shared world), the off check in
`SwitchesOffScenarios` and the export record in `RecipeExportDrawBenchScenarios.cs`: see the mod's README,
"Tests".
