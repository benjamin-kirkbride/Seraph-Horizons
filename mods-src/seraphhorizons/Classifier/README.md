# Classifier

Part of the Seraph Horizons mod (`../README.md`): the **classify** stage of the ore mill line (epic #684),
one machine upgraded in place (#711). Hand and tier 1 use the riddle (#714), a separate station; this is the
machine for tiers 2 to 4. Its gameplay is #730; this model is #731.

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what those will be built against (as the eidolon gantry's were).
Nothing in C# was needed for the model: the site's viewer reads the shape and the rig, and the reference
poses are there for the gameplay's tests to replay when it comes.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `tools/tests/` and `site/`, which are the repository's. `tools/` is the
model's generator; the generic half is `../Machines/tools/machinegen/`, unchanged by this machine (no new
driver).

## The machine

**Footprint.** 24 cells: 4 long (x, the way the material flows), 3 high (y), 2 wide (z), fixed at tier 4's
size from the frame on, so nothing moves as it is upgraded. In the native frame with the controller at
`[0,0,0]`: x 0..3, y 0..2, z 0..1. The controller is the feed end's north-west bottom cell. The material
flows east: the feed comes in high at the west end, and everything leaves low. (Which way the machine
extends from the player who places it is the gameplay's call.)

**Power** comes in on the north face of `[3,1,0]`: a vanilla axle along z at the cell's centre (x 56, y 24
voxels), into the **entry shaft**, which crosses the machine in two pillow blocks on the side girts. It is
the frame's, the same at every tier, and both tiers' drives take their motion from it: tier 2's eccentrics
and tier 3's bevel pinion are keyed on it. Either way round works: the screen shakes and the trommel screens
whichever way they turn, so there is no rectifier.

**The ports**, fixed at every tier (the rig's `<port>Cell`, `<port>Face` and a point where material crosses
the face, in blocks):

| Port | Cell, face | Point | Tiers | What crosses |
|---|---|---|---|---|
| Power | `[3,1,0]`, north | (3.5, 1.5, 0) | all | The vanilla axle |
| Feed inlet | `[0,2,0]`, west | (0, 2.6766, 0.8438) | all | Crushed ore, high at the west end: a chute from upstream (the crusher) comes in here |
| Fines (undersize) | `[1,0,0]`, north | (1.375, 0.1625, 0) | all | What went through the screen, the trommel's drum (tier 3) or its outer jacket (tier 4): out of the fines bin's spout, low on the north side |
| Oversize | `[3,0,0]`, east | (4, 0.1187, 0.75) | all | What rode over the screen or out of the drum's open low end: down a spout, out of the east end, low |
| Middlings | `[2,0,1]`, south | (2.5312, 0.1125, 2) | 4 | Tier 4's third product, what passed the drum but not the outer jacket: off the jacket's low end, down the middlings spout, out of the south side, low (meant for the grinder) |

The feed is on the top row and the outlets on the bottom one; no two ports share a face, and none is the
power's. The middlings port is on the south side at the discharge end because that is where the jacket's
lip drops them: between the fines bin's east end (x 38) and the discharge spout's back (x 44), a spout runs
straight down south from under it, the shortest steep path to a face. It is unused before tier 4.

**The tiers.** Each tier fits a set of parts (the rig's `requires` values and its `tiers` key); fitting a
tier's set replaces the last tier's working parts (#711), and the frame alone is the bare machine with its
shaft turning:

| Tier | Fitted | What it is | Notes |
|---|---|---|---|
| 2 | `grizzly`, `screen`, `eccentric` | Grizzly and screen | Two products, batch-ish: the feed in at the inlet, the fines and the oversize collect at their outlets |
| 3 | `trommel`, `bevel`, `discharge` | Trommel | Two products, continuous: the outlets hand off by chute to the next machine |
| 4 | `trommel`, `bevel`, `discharge`, `jacket`, `middlings` | Compound trommel | Three products: tier 3's trommel and spout kept, an outer jacket and a middlings spout added |

The fitting order inside a tier (what each item is and when it goes on) is not designed here; the owner sets it.

### The choices, and why

- **Tier 2's screen is shaken by eccentrics on the entry shaft.** A period shaking screen was reciprocated by
  an eccentric or a cam from a countershaft; ours is powered, so its shake must come from the power input
  with a visible cause. An eccentric on each end of the entry shaft drives a connecting rod to a pin on each
  side of the screen's tail: one stroke an axle turn, 2.5 voxels long (the eccentrics' throw twice). The
  screen hangs on four iron hangers of one length, so it moves without turning, on their arc. A cam and
  bumper (a bumping screen) was the other choice; the eccentric was taken because its motion is continuous
  and every part of it is pinned, which the checks can hold.
- **Tier 3's trommel is driven by a bevel pair on its shaft.** Period trommels on a central shaft and spiders
  were turned by bevel gearing on the shaft from a countershaft; girth gears and friction rollers belong to
  later, shaftless drums. The trommel's shaft is set so its axis crosses the entry shaft's (the pair's apex),
  so the pinion (8 teeth) is keyed on the entry shaft itself and the wheel (24) on the trommel's shaft: no
  countershaft, and the trommel turns a third of a turn an axle turn (about 20 turns a minute at an axle's
  turn a second, a trommel's pace). The drive is at the low (discharge) end, which keeps the feed end open for
  the feed chute, and the trommel's east bearing hangs from a top cross beam between the drum and the wheel.
- **The trommel is cylindrical on a shaft inclined 1 in 12**, as the brief has it. The rig turns parts only
  about x, y or z, so its turn about its own inclined axis is written as three drivers: a fixed turn levelling
  it (a `swing` with ratio 0, a constant, as the handcar writes its constant `slide`s), the `rotate` about x,
  and the fixed turn back. No driver is new, and the site's viewer and the game's rig maths already compose
  them. (A conical drum on a level shaft, the Harz pattern, would have needed only the `rotate`; it is the
  other period choice.)
- **Tier 4 is a compound (double) trommel**, so the top tier sorts three sizes where the lower tiers sort two.
  The drum is wrapped in a concentric outer jacket of finer mesh, carried on iron spacers from the drum's
  middle and band rings and closed at its high end by a head ring against the drum's feed ring: it turns with
  the drum (it rides it in the rig, no new driver). As period compound trommels were, the jacket is shorter
  than the drum (29.6 voxels against 35): the drum's oversize runs off its own open low end, past the jacket,
  as at tier 3; what the drum lets through but the jacket does not (the middlings) runs along the jacket's
  bottom, over its blank band and its lip, off its low end; what the jacket lets through (the fines) falls to
  the bin. The jacket's rings are 9.4 in radius, as large as the frame's north mid post (z 4) and the bin
  allow: the bin's rim came down a voxel to 14.5 for it.
- **The grizzly is replaced at tier 3, not kept as a scalper.** #711 has a tier's set replace the last
  tier's working parts, and the grizzly stands where the trommel's feed chute does. Keeping it ahead of the
  trommel would be realistic: see "Open for the owner".
- **The feed chute runs into the drum beside its shaft.** The shaft runs through the feed end to its west
  bearing, so a chute down the middle would drop the feed onto the shaft. The chute's throat is south of the
  shaft and its head, under the inlet's whole width, is a funnel plate that falls south into it; the feed
  falls past the shaft onto the drum's bottom.

## How it works

Positions here are in the generator's **build frame**: the native axes in voxels from the machine box's
north-west bottom corner, which is the controller cell's corner, so the shipped files are the build frame
divided by 16.

**The frame** (no `requires`): oak posts at x 0, 30 and 60 on both sides, on sills; side girts at y 18..21.5;
top rails, end ties and three top cross beams (x 13, 38, 46: the grizzly's straps and the screen's head
hangers; the screen's tail hangers; the trommel's east bearing); knee braces in the side frames; a west tie
under the **feed inlet chute** (planks, x 0..5, its lip at y 39.9, an iron lip bearer under it); the **fines
bin** (plank sides on the sills, x 6..38, rim at y 14.5, a V floor falling 36 degrees from both ends to its
middle and a spout out of the north side); the **entry shaft** in its two pillow blocks.

**Tier 2: grizzly and screen.**

- The **grizzly**: four iron bars, 1.1 voxels wide with 1.2 between them, at 40 degrees, 13 long, their heads
  hooked under the inlet's lip bearer, their feet on an iron bearer hung by two straps from the first top
  cross beam. The feed slides down the bars: the fines drop between them onto the screen's blank feed plate,
  the lumps run off the foot onto the screen.
- The **screen**: a tray 12 wide, its deck of wire cloth (the punched or woven screen) between plank side
  boards, a blank feed plate at its head under the grizzly and a blank tail plate, iron cross bars under it,
  falling 6 degrees to the east. Its wire cloth ends at x 36.5, so at every point of its stroke it is over the
  bin. It hangs on four iron **hangers**, 13 long, from brackets under the top cross beams; its stroke is 2.5
  voxels, the hangers swinging 5.5 degrees either way. What goes through the deck falls into the fines bin;
  what rides over it goes off the tail lip into the **tail spout** (planks, falling 39 degrees to the oversize
  outlet).
- The **eccentrics** and their **rods**: a sheave on each end of the entry shaft (throw 1.25), a strap round
  it and a bar to an eye on the screen's tail pin on that side (13.1 long).

**Tier 3: trommel.**

- The **trommel**: a drum 15 voxels across and 35 long, its axis falling 1 in 12 from (x 10.2, y 27.8) at the
  feed end to (45.0, 24.9) at the discharge, through the entry shaft's axis at (56, 24). Its jacket is wire
  cloth in two lengths between iron rings (a feed ring with the feed opening, 6.4 in radius; a middle ring; a
  band ring where the cloth ends at 37; a lip ring at the low end, which the oversize rides over), with a blank
  band before the lip, on two spiders (a hub and six arms) on a steel shaft. The shaft runs in a pillow block
  at the feed end, on a pedestal on a cross timber across the girts, and in one at the discharge end, hung from
  the third top cross beam.
- The **feed chute** (sheet iron): its head under the inlet's lip, a funnel plate falling 60 degrees down the
  chute and 15 across, south into a throat 2.2 wide that runs beside the shaft into the feed opening.
- The **bevel pair**: the pinion of 8 on the entry shaft, the wheel of 24 on the trommel's shaft past its
  east bearing, module 0.5, their pitch cones meeting at the apex.
- What goes through the drum falls into the fines bin; the oversize goes over the lip into the **discharge
  spout** (planks, 32 degrees, to the oversize outlet).

**Tier 4: compound trommel.** The same drum, bevel pair and discharge spout, and:

- The **outer jacket**: fine mesh (`game:block/metal/mesh5`, finer than the drum's `mesh2`) 17.9 across, from
  the drum's feed ring to 5.4 voxels short of its lip, in two lengths and a blank band before its own lip ring;
  a head ring closing its high end against the drum's feed ring; a middle ring outside the mesh (no lip in the
  annulus); six iron spacers at each of the drum's middle and band rings, out to the jacket. The annulus
  between the drum's rings and the jacket's mesh is 0.7 voxels clear (1.15 from the drum's cloth).
- The **middlings spout** (planks, falling 28 degrees south): its back board north of where the middlings fall,
  between the bin's east end and the discharge spout, down to the middlings outlet on the south face.
- So three products: the **oversize** off the drum's open low end down the discharge spout (east), the
  **middlings** off the jacket's low end down the middlings spout (south), and the **fines** through the
  jacket into the bin (north).

### Every moving part

θ is the axle's angle. Every motion is a function of θ alone; the linkage's are Fourier series (six
harmonics and a constant, as the handcar's pitman is written), fitted to the exact four-bar motion by the
generator, which holds every pin to within 3e-5 voxels.

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft (`entry`) | The vanilla axle | The eccentrics; the bevel pinion | `rotate` z, ratio 1 |
| Eccentric sheaves, north and south (`eccn`, `eccs`) [eccentric] | Keyed on the entry shaft | The rods' straps | ride `entry` |
| Connecting rods (`rodn`, `rods`) [eccentric] | The eccentrics (their straps) | The screen's tail pins | `swing` z about the small end's pin (7 terms), then the screen's `slide`s |
| Screen (`screen`) [screen] | The rods | — (carries the ore) | `slide` x (7 terms), `slide` y (the hangers' arc, 7 terms) |
| Hangers (`hanger1`, `hanger2` at the head, `hanger3`, `hanger4` at the tail; odd north) [screen] | The screen | — | `swing` z about the top pin (7 terms) |
| Hanger brackets (`brackets`) [screen] | Fixed | — | none |
| Tail spout (`tailspout`) [screen] | Fixed | — | none |
| Grizzly (`grizzly`) [grizzly] | Fixed | — | none |
| Bevel pinion (`pinion`) [bevel] | Keyed on the entry shaft | The bevel wheel | ride `entry` |
| Trommel: drum, spiders, shaft (`drum`) [trommel] | The bevel wheel | The outer jacket (tier 4) | `swing` z ratio 0 (the tilt, levelled), `rotate` x ratio −1/3 about the apex, `swing` z ratio 0 (tilted back) |
| Bevel wheel (`wheel`) [bevel] | The pinion | The trommel's shaft | ride `drum` |
| Outer jacket, its rings and spacers (`jacket`) [jacket] | The drum (on its spacers) | — (screens the middlings from the fines) | ride `drum` |
| Bearings, timber, hanger, feed chute (`mount`) [trommel] | Fixed | — | none |
| Discharge spout (`discharge`) [discharge] | Fixed | — | none |
| Middlings spout (`middlings`) [middlings] | Fixed | — | none |
| Frame (`frame`) | Fixed | — | none |

Toothed wheels and what they mesh: the bevel pinion and the bevel wheel, nothing else.

## Model

Everything in the model was made for this mod; no other mod's model is used. It wears the game's textures:
debarked oak for the frame's timbers and the axle's continuation (`oak`), oak planks for the chutes, the bin and
the screen's sides (`planks`), the iron plate for castings, bars, rings, spiders, spacers and fittings (`iron`),
plain steel for the shafts and gears (`steel`), iron sheet for the blank plates and the feed chute (`sheet`), the
game's wire mesh `game:block/metal/mesh2` for the screen's deck and the drum (`mesh`), and its finer
`game:block/metal/mesh5` for the outer jacket (`finemesh`). Both meshes are textures with holes: whether the
renderer draws them see-through is for the game to show. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/classifier.json` | The whole machine, every tier's parts (489 elements). Tier 2's grizzly and screen and tier 3's trommel stand in the same space: the rig says which are fitted. |
| `assets/seraphhorizons/shapes/block/classifier_frame.json` | The static frame only (52 elements): what a block would draw. |
| `assets/seraphhorizons/config/classifier-rig.json` | Footprint, power and ports, the tiers' sets, the trommel's figures and the part rig (21 parts). |
| `tests/Classifier/rig-reference.json` | Every part's matrix at 15 axle angles, from the reference maths. |

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, from the frame
and the finished machine's parts (tier 4), with a lid over every column; no cell is hollow.

### Rig schema (`classifier-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"), with no work, no trunk path and
no oil: θ is the only input.

- `cells`, `powerCell` and `powerFace`.
- The ports: `feedCell`/`feedFace`, `finesCell`/`finesFace`, `oversizeCell`/`oversizeFace` and
  `middlingsCell`/`middlingsFace`, and `feed`, `fines`, `oversize` and `middlings` as `{ "pos": [x, y, z] }`,
  where material crosses each face (the inlet's middle; each outlet chute's floor at its face, a little above
  it).
- `tiers`: `{ "2": [...], "3": [...], "4": [...] }`, the `requires` values each tier fits.
- `trommel`: `turnsPerAxleTurn` (1/3, the bevel pair's 8:24; tier 4's jacket turns with the drum) and `slope`
  (1/12). A gameplay pace that counts the trommel's turns should take it from here, or a test should hold the
  two together.
- `parts`: `{id, match, requires, ride, drivers}`, first match wins, `frame` last.

**In the viewer** (`site/models.json`, id `classifier`): a select of the build state ("The frame" and one
state per tier, opening on tier 4) over a checkbox per `requires` value; the axle's angle, with Play turning
it at a turn a second. The θ slider spans three turns, the trommel's cycle.

## Regenerating

```sh
python3 mods-src/seraphhorizons/Classifier/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Classifier/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Classifier/tools/make_shape.py --quick      # skips the z-fighting fix and check (not for files that ship)
```

A full run takes about fifteen seconds. Output is deterministic: two runs into two folders `diff -r` clean.

## Validation (`tools/validate_classifier.py`)

Every run checks its output and exits non-zero if a check fails. A pose is an axle angle and a state (the frame,
or a tier's set fitted): tier 2's and tier 3's parts share space, so every check between parts runs per state.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part; the `requires` values exactly the tiers' sets.
- **Nothing floats:** the frame one piece from the ground, and each tier's state at rest (the frame and that
  tier's parts) one piece with it.
- **Containment:** nothing leaves the 4 × 3 × 2 box, in any state, over the trommel's cycle.
- **Power and ports:** the entry shaft meets the power face at the cell's centre; each port's point on its
  cell's face, on the outside of the footprint, the feed on the top row and the outlets on the bottom, no two
  on one face; each outlet chute's floor (the fines spout, both oversize spouts, the middlings spout) and the
  inlet's floor under its port's point.
- **Textures** by role (oak timbers, plank chutes, iron fittings, steel shafts and gears, sheet-iron blank
  plates, wire-cloth screens, the finer jacket), and every element under a rule.
- **The bevel pair:** the pitch cones share their cone distance, the radii are the teeth's ratio, and the
  pitch point moves alike on both gears at 13 angles (finite differences of the posed rig); the trommel turns
  about its own inclined axis (a point on the axis stays put) and three axle turns turn it once, the other way.
- **The compound trommel:** the jacket turns with the drum (its matrix the drum's at every angle); it is
  concentric with the drum (its mesh and band 8.9 to 9.2 from the axis), its mesh at least 0.5 clear of the
  drum's rings; it is at least 4 voxels shorter than the drum at the low end; and its middlings fall between
  the bin's end and the discharge spout's back.
- **The linkage:** over a turn, each rod's big end on its eccentric's centre and its small end on the screen's
  pin, each hanger's top on its bracket's pin and its foot on the screen's; the screen moves without turning;
  its stroke the eccentric's throw twice.
- **Supports:** the entry shaft in its two pillow blocks; the trommel's shaft, looked at level, in its two.
- **Where material falls** (rays straight down, at several angles as the screen and the drum move): off the
  inlet's lip onto the grizzly's bars (tier 2) or into the feed chute (tiers 3 and 4); between the grizzly's
  bars and off its foot onto the screen; through the screen's deck and the drum into the fines bin; off the
  screen's tail into its spout; off the feed chute's lip into the drum (clear of its shaft); off the drum's lip
  into the discharge spout (tiers 3 and 4); and at tier 4 the three products: what passes the drum onto the
  outer jacket, the fines through the jacket into the bin, the middlings off the jacket's lip into the
  middlings spout.
- **Clearances:** over each state's cycle (114 poses) no two parts touch except the intended contacts
  (`ALLOWED`: shafts in bearings, fittings on their shafts, the bevel pair, the jacket's head ring and spacers on
  the drum's rings, the rods' straps and eyes, the hangers' pins, fixed parts on the frame).
- **No z-fighting** (full runs): no coplanar overlapping faces among what one state shows, at seven poses.
- **Files:** every texture declared; a lid over every column; the shipped model is the checked one moved.

`tools/tests/test_classifier_model.py` holds the written files to these without the generator's state (and
runs the generator to see it reproduces them); `site/test/classifier.test.ts` replays the reference poses in
the site's rig maths and checks the viewer's states against the rig's tiers.

## Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `eccn_*`, `eccs_*`,
`rodn_*`, `rods_*`, `screen_*`, `hanger1_*`..`hanger4_*`, `brackets_*`, `tailspout_*`, `grizzly_*`,
`pinion_*`, `drum_*`, `wheel_*`, `mount_*`, `discharge_*`, `jacket_*`, `middlings_*`, and `fr_*` for the frame.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Open for the owner

1. **The grizzly at tiers 3 and 4.** Replaced now (#711's rule). Kept as a scalper ahead of the trommel it would
   need its own oversize path past the drum and the feed chute under its bars; the feed chute's head is where it
   stands.
2. **Water.** No water inlet is modelled. A trommel washed by a spray pipe along the inside of its top (and a
   water port, which would be a fifth fixed face) is the period wet-screening practice; whether the classifier
   draws water at any tier is undecided in the epic.
3. **The fitting order inside each tier**, and what each set costs: the `requires` ids are one per set
   (`grizzly`, `screen`, `eccentric`; `trommel`, `bevel`, `discharge`; `jacket`, `middlings`), not a build order.
4. **Where the middlings go.** The model gives them an outlet (south, at the discharge end), meant for the
   grinder; the line has no rule for a third product yet, and whether tier 2 or 3 should leave the middlings in
   the fines (as they do now) or in the oversize is the gameplay's.
5. **The middlings spout falls 28 degrees**, the most the space between the jacket and the floor allows (the
   oversize spouts fall 32 and 39). Fine middlings slide on it; wet, they would anyway.
6. **One shaking screen under the grizzly** stands for tier 2's "screens". A second deck (a finer screen
   under the first) would give tier 2 middlings too.
7. **Tailings.** The classifier makes none: its products all go on. If the epic's "every stage after crushing
   leaves tailings" covers classifying, a tailings face is missing.
8. **Collision boxes at every tier are the finished machine's** (tier 4): at tier 2 the trommel's space is
   solid. Per-tier boxes would need the rig to carry a set per tier.
9. **The trommel's shape**: cylindrical on an inclined shaft (the brief), against a conical drum on a level
   shaft (the Harz pattern, one plain `rotate`). The east bearing hangs from a top beam; a pedestal there would
   stand in the oversize's path.
10. **The power input**: the north face at the discharge end, a block and a half up, chosen so the bevel's apex
    is the entry shaft's own axis.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's viewer (a
standalone copy). No one has looked at it in a client.

1. **Box teeth.** The bevel teeth are boxes along their pitch cones' generators; meshing is checked at the pitch
   point, not tooth by tooth.
2. **Plates as boxes.** The bin's V floor, the spouts and the feed chute are flat boards; the spouts narrow in a
   step, and the feed chute's funnel plate is a rectangle in a plane that falls two ways, so its far edge is
   skewed a voxel in plan.
3. **A narrow annulus.** The compound trommel's middlings run in an annulus 1.15 voxels deep between the drum's
   cloth and the jacket; the jacket could not be larger in this frame without moving the bin or the posts.
4. **The screen's arc** is on its hangers (it rises 0.06 voxels at the ends of its stroke), drawn exactly by the
   series; the deck's blank plates are the period feed and tail plates, not modelled holes.
5. **Material is not drawn.** The ports, the falls and the clear paths are checked; no ore is shown moving.
6. **What only the game shows:** lighting, the mesh textures' holes in the renderer, z-fighting at distance,
   sounds, item forms, placement in four facings.
