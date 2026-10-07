# Animated machine models

How to build an animated multiblock machine model for a mod in this repository, from another mod's
model or from scratch. The bucking sawmill (`mods-src/seraphhorizons/BuckingSawmill/`, part of the Seraph Horizons mod) is the worked example
throughout. Its [README](../../mods-src/seraphhorizons/BuckingSawmill/README.md) documents the finished machine in
detail; this guide is about the process, and the mistakes worth not repeating. The rosser
(`mods-src/seraphhorizons/Rosser/`, [README](../../mods-src/seraphhorizons/Rosser/README.md)) is the
second machine built this way, and the first to share the mill's code: what it added to the rig and
to the process is in [A second machine: the rosser](#a-second-machine-the-rosser).
[Model pitfalls](pitfalls.md) is the short list of things that went wrong, to read before starting
and before calling a model done.

## When to use this approach

Use it for any machine that has moving parts tied to gameplay state: shafts turned by mechanical
power, saws that sink with a cut, levers thrown at the ends of a cycle. It produces:

- **A generator script**, [`tools/make_shape.py`](../../mods-src/seraphhorizons/BuckingSawmill/tools/make_shape.py).
  It is stdlib-only Python, reads the source mod's shape from `build/mods/` and writes everything below.
- **Two shape files**: the whole machine, and the static frame only (the block draws the frame; a
  renderer draws everything that moves).
- **A rig file**, `assets/<modid>/config/<machine>-rig.json` (the mill's is `buckingmill-rig.json`). It holds the footprint (cells with collision
  boxes), the anchor points gameplay needs (power cell and face, infeed and output), and the moving
  parts. Each part has the element-name globs it owns and the drivers that pose it.
- **A reference of poses**, [`tests/BuckingSawmill/rig-reference.json`](../../mods-src/seraphhorizons/tests/BuckingSawmill/rig-reference.json):
  every part's matrix at a grid of inputs, from the script's own maths. It keeps every other
  implementation honest.
- **A renderer driven by the rig** (`MillRenderer.cs`, with the maths in
  [`Machines/Core/RigAnimation.cs`](../../mods-src/seraphhorizons/Machines/Core/RigAnimation.cs), shared by every
  machine), so gameplay code never knows element names.
- **A browser viewer** for review without the game (see [Reviewing without the game](#reviewing-without-the-game)).

**Why a generator, not hand-editing in VS Model Creator.** The model is reproducible from the
source mod's shape and a list of named constants, so it can be regenerated when the source mod
changes. It is validated on every run, which is what makes dozens of review passes safe. Its
output is deterministic, so diffs show real changes.

If someone does edit the output by hand, those edits are lost on the next run, and they must keep:
- element names, because the rig finds its parts by name prefix;
- the split between the full shape and the frame-only shape;
- the cell boxes in the rig file, if anything moved across a cell boundary.

Port hand edits back into the script, or stop regenerating.

## Before modelling

### Permission and credit

Parts taken from another mod's model belong to that mod's author. For the sawmill:
- Bobrik00 gave permission to use and modify Immersive Woodworking's sawmill model.
- By the end, about a third of the elements were still Immersive Woodworking's (the gears, saw
  blades, saw heads and cranks); the rest had been rebuilt. Credit what is actually taken, and
  re-check the wording when the model has changed a lot.
- [`CREDITS.md`](../../mods-src/seraphhorizons/CREDITS.md) names those parts, says they are the
  author's, used with permission, and outside the repository's licence. The csproj copies it into the release zip,
  next to `modinfo.json`.
- The root `LICENSE` names the shapes folder as an exception to the Apache licence.

Do all three before any derived model ships. Permission is given for a use: a second model that takes
parts of the same source needs the author's word for that model too. The rosser takes the crown disc,
two pinions and a tooth from the same IW model; its credit names them without claiming permission
until the author confirms it, and `CREDITS.md` carries a note to the owner saying so.

### Find out how the source machine works

Read the source mod's code before touching its model. Its renderer decides which elements move,
and how, by matching element names.

1. Unpack its zip and disassemble the DLL:
   ```sh
   unzip -o build/mods/immersivewoodworking_*.zip '*.dll' -d "$TMPDIR/iw"
   ikdasm "$TMPDIR/iw/ImmersiveWoodworking.dll" > "$TMPDIR/iw/iw.il"
   ```
   `ikdasm` gives IL and is installed here. `ilspycmd`, if you install it, gives C#.
2. Look for:
   - the string literals its renderer matches against element names (`ldstr` in the IL);
   - the footprint (a static array of local cells) and the power cell;
   - how cells are turned to a facing (IW: world = (x·nz + z·nx, y, −x·nx + z·nz));
   - the part items and how they are recognised (code paths);
   - the mechanical power behaviour, its resistance and any speed cap;
   - the blocktype JSON: variants, `rotateY` per side, ghost blocks.
3. Write findings to a file, marking each claim VERIFIED (read in the IL or JSON) or INFERRED.
   Later passes will lean on it.

### Shape format facts that matter

- Coordinates are voxels, 16 per block. An element is a box `from`..`to`, with
  `rotationX/Y/Z` in degrees about `rotationOrigin`. The rotations are applied as Rx·Ry·Rz,
  right-handed.
- `children` coordinates are relative to the parent's `from`. Flatten the hierarchy into world
  boxes before doing anything else (`flatten` in the generator).
- Faces name a texture code (`#oak`); the code maps to a texture in the shape's `textures`, and the
  blocktype's `textures` override it. Write explicit UVs with `autoUv: false`, scaled to each face,
  or retextured boxes stretch. Some mods use numeric codes (IW's `#0`); map them to real names.
- The game ignores keys it does not know, so `_comment` entries are safe.
- A blocktype turns the whole shape per variant with `rotateY` about the block's centre. Your
  footprint rotation must agree with it, as must every point gameplay computes (output position,
  sound and particle points).
- Item forms (GUI, hand, ground) use the transforms in the blocktype JSON. A large multiblock
  needs an `origin` near its centre and a small scale.

## Design the mechanism before the geometry

Every moving part needs a visible cause. Before drawing anything, write a table of every rotating
or sliding part, what drives it and what it drives (the README's "Every rotating part" table is
the model). If a row has no believable "driven by", the mechanism is wrong, not the geometry.

Then decide, and check against each other:

- **Footprint:** which cells, and which cell is the controller. Make the controller the block the
  player clicks: the sawmill's is the middle of the near end.
- **Orientation at placement:** which way the machine extends from the player
  (`Footprint.PlacedFacing`; `BlockBuckingMill.TryPlaceBlock` sets the variant itself). Note that
  `Block.SuggestedHVOrientation` returns the direction the player is looking, not the direction
  back toward them.
- **Material in and out:** the sawmill takes trunks in at one end and drops logs at the other.
  Material sliding straight through needs a clear path for its whole length, plus room for a feeding
  block (a Logging Expanded rack) beside the machine.
- **Power:** where the axle connects. The power cell and the feeding block must not want the same cell.

Walk the material's path through every post, bearing and bracket before building. The sawmill's
first west end had an input post in the trunk's path; it became a portal.

### Rules from review

The owner's review notes on the sawmill reduce to these rules. Each has a reason.

- **No toothed wheel that meshes with nothing.** Gears read as a promise of a drive; an idle one
  reads as a mistake. Leave decorative toothed flanges off, or make the wheel plain.
- **One tool per job.** A cross-cut uses one blade per saw. The source rip saw's extra blades were dropped.
- **Thin parts held at both ends.** A long blade cantilevered from one end looks wrong. Each blade
  runs in a guide block on a post at its far end.
- **Timbers thick enough to believe.** Make them solid, not bundles of slats. Model grooves as real
  notches in a solid post, not as gaps between pieces. The sawmill's posts are 4×4, its beams 4×3.5.
- **Every fixed element joined to the frame.** Centre beams on posts and house their ends. Drop
  knees and braces that join nothing.
- **Every shaft carried by bearings fixed to the frame.** That means two bearings, or one plus a
  pilot into a part that is carried. Locate loose wheels on a shaft with collars on both faces.
- **Ropes tangent to their spools, ending on eyes.** A rope must leave the spool's wrap at the
  tangent point and be tied to something.
- **Iron for pins and wearing surfaces, wood for structure.** Shafts that continue a vanilla wooden
  axle stay wood. The README's texture rule lists every case.
- **Linkages as few rigid pieces as possible.** Each throw needs a visible cause, both ways. The
  sawmill's clutch is one rock shaft (tappet arm, fork, over-centre weight) and one pushrod. The
  carriage pushes the rod down at the bottom and lifts it by a collar at the top.
- **Nothing whose state needs information no part could carry.** The rig once had a "reversible"
  driver that moved differently depending on which way the shaft turned. No real part knows that,
  so it was replaced by a rectifier: two pinions on one-way catches, so the gear set turns the same
  way either way. Likewise a latch that only gameplay could release was removed, and the cycle now
  runs non-stop.
- **Clear paths for whatever slides through.** Check this with a swept volume, not by eye.
- **No two drawn faces in one plane.** Faces of different elements that share a plane and overlap
  z-fight in the game: they flicker as the camera moves. Boxes built to overlap (an octagon from
  four rotated strips, a cross-profile shaft from two bars, a collar flush with a bearing) do it at
  every shared end. Remove a face that is pressed flat against its neighbour (it cannot be seen),
  and move the smaller of two visible ones in by a hundredth or so of a voxel.

## The generator

### Organisation

- **Named constants at the top**, in voxels, each with a comment: station positions, post spans,
  bed height, saw travel, gear radii, lever geometry. Every placement is derived from them;
  nothing is a bare number in a builder.
- **One function per piece or transformation:** `build_posts`, `build_saw`, `build_gearbox`,
  `build_levers`, `build_frame`, `build_bed`. The helpers are `from_template` (a box with a source
  element's faces and cropped UVs), `beam` (split into block-length segments so UVs don't stretch),
  `strut` (a bar between two points), `octagon` (round parts), plus `rotate`, `translate`, `remap`
  and `spread`.
- **Build in one frame, shift at the end.** Everything is built and validated in a "build frame"
  measured from the machine box's corner. `shipped()` then moves the elements, rig pivots and cells
  so the controller cell is `[0,0,0]`, and `check_shipped()` proves the move changed nothing else.
  Changing the controller cell then costs one constant.
- **Rig parts defined next to the geometry** (`rig_parts()`). The driver maths is written once in
  Python (`driver_matrix`, `part_matrix`) as the reference.
- **Compact, deterministic output:** one element per line, values rounded to 4 places, a fixed
  key order. Two runs must be byte-identical.
- **The machine-free half is shared.** `mods-src/seraphhorizons/Machines/tools/machinegen/` (stdlib
  only) holds what no machine owns: `geometry` (`El`, `flatten`, the matrix helpers, `from_template`,
  `beam`, `strut`, `octagon`, `metal`, `rotate`, `translate`, `remap`, `spread`, `rename`),
  `rigmath` (the reference driver maths, every driver's formula in its docstring), `checks`
  (`obb_overlap`, `coplanar_faces` and `fix_coplanar`, `cell_boxes`, `frame_floating`, `supports`,
  `euler_round_trip`) and `output` (the writers and the origin shift). A machine's script adds
  `Machines/tools` to `sys.path` and imports it; it keeps its constants, builders, `rig_parts`,
  `validate`, poses and `shipped` to itself. When the mill's generator moved onto it, the gate was
  that the mill regenerated byte-identical, its validation log included; hold any change to the
  package to the same gate for every machine that uses it. `tools/tests/test_machinegen.py` tests it
  without the game or the fetched mods, so CI runs it.

### Naming

Give elements prefixes that say which part they belong to, and make the rig's `match` globs
first-match and unambiguous: `f1_saw_*`, `f1_blade_*`, `gear_pinion_w_*`, `lever_rock_*`, `drum*`.
The generator checks that every element lands in the part it was built for. A frame element whose
name happens to match a moving part's glob is the usual cause of a failure there.

### Validation on every run

The script exits non-zero if any check fails. The sawmill's checks form a reusable catalogue.
Those marked *(review)* were added because a review found a fault the existing checks had missed.
That is the pattern to keep: **every review finding becomes a check**, so it cannot come back.

- **Parts:** every element is in its intended part; counts are printed per part.
- **Containment:** nothing leaves the declared cells at rest, or the machine box over the whole motion.
- **Clearances:** moving parts against each other and the frame, over sampled shaft angles,
  depths, both directions and both lift states. Intended contacts (a shaft in its bearing, a pin in
  its eye) are listed as exceptions.
- **Material envelopes:** every trunk size on the bed touches nothing but the blades.
- **Swept paths** *(review)*: the largest trunk sliding in from beyond one end, and a section
  sliding out beyond the other, touch nothing with the saws at the top.
- **Nothing floats** *(review)*: every frame element shares a face with, or overlaps, another, and
  the frame is one connected piece from the ground.
- **Bearings enclose their shafts** *(review)*, checked at rest and turned 45 degrees.
- **Every shaft carried** *(review)*: two fixed bearings along its axis, or one plus a pilot. Each
  loose wheel has a collar or bearing on both faces. Failures are named.
- **Ropes** *(review)*: each rope leaves its spool's wrap at the tangent point and ends on its eye,
  at every depth.
- **Pinned joints:** each rod's ends stay on their pins, and the pushrod stays pinned to its arm,
  over the whole throw.
- **Linkage contacts** *(review)*:
  - the carriage's lug meets the tappet, or the collar, exactly when the trip starts to move;
  - the fork stays in the clutch's groove;
  - the over-centre weight passes over its shaft between its two rests.
- **Gearing:** in a raise, the clutched halves of the axle turn exactly together; the rectifier
  turns the disc the same way for either shaft direction.
- **Anchors:** the feeding block's cells and the axle's cell differ.
- **No z-fighting** *(review)*: no two drawn faces of different elements lie in one plane, facing the
  same way, and overlap, in world space after rotations, at rest and mid-cut. The generator
  removes the faces pressed against their own part's elements and insets the smaller face of each
  remaining pair (`fix_coplanar`) before checking; the failures name both elements.
- **Files:** every texture code is declared, and every written file parses.
- **Shifted output equals checked model:** every element posed by the shipped rig lands where the
  checked one does, moved.
- **Determinism:** not a self-check, so run it by hand (see the checklist).

## The rig

The schema is in the mill README's "Rig schema" section, what a trunk-path machine adds in the
rosser README's, and the generic work in `docs/recipe-browser/models.md` ("The work and its
drivers"). In short:
- `cells` (with up to three boxes each, and a collision-only `lid` on a column's top cell: every
  column's for the mill and the gear cutter, the station's only for the rosser),
  `powerCell`, `powerFace`, `infeedSide`, `outputSide`,
  `output.pos`, `trunkBed` and `saw` are for gameplay;
- `parts` is an ordered list of `{id, match, requires, ride, drivers}` for the renderer.

**Inputs.** The mill's drivers are functions of four numbers:
- θ, the signed shaft angle;
- *d*, the saw depth, 0..1;
- *L*, lifting, 1 while the saws go up;
- ψ, the shaft's total travel either way.

A machine with work in progress adds four, all opt-in, so a rig that uses none of them poses exactly
as before:
- W, the machine's **work**: how far its job has got, in a unit the rig declares. A rig declares it
  as `work` (`{name, unit, step, end: {thin, thick}}`, a point on its own scale: the gear cutter's teeth
  cut, 0..12 or 0..20) or, the trunk-flavoured case, as a `trunkPath` (a trunk travelling along a line,
  in blocks: the nose at `nose0` + W and the tail L_k behind it; the rosser's), never both. `"trunk"`
  is W's trunk-flavoured spelling as a driver input and in a pose;
- k, the work's class: 0 none, 1 thin, 2 thick (a trunk's size; the gear cutter's master);
- p, its presence, 0..1, eased in as a trunk is loaded (or a master fitted) and out as it is taken away
  (k is held while p eases out, so parts ease back rather than jump);
- φ, the feed's travel, an angle that only grows;
- and, separately, `oil`, 0..1, how full the machine's oil tank is (the gear cutter's sight-feed cup).

A part's matrix is its drivers composed in order, then its `ride` parent's matrix.

**Drivers:**
- `rotate` (optionally `rectified`, using ψ);
- `slide` and `swing` (sinusoidal in θ);
- `feed` (linear in *d*);
- `step` (a ramp over a depth window, with lift gates `hold`, `block` and `trip`);
- `stretch` (ropes; with `"input": "oil"`, a liquid's level);
- `"input"` on `rotate`, `slide` and `swing`: θ (the default), ψ (`"travel"`, which is what
  `rectified` means), φ (`"feed"`), W (`"work"`, or `"trunk"`) or the oil in place of θ;
- `gauge`, a motion set by the work at a place: a slide or rotation by `amount[k]` times an
  engagement that is p when `present`, or p times how far the work covers the most engaged of its
  `windows`, easing in as the nose arrives and out as the tail leaves (for a plain work quantity nose
  and tail are both W); optional `lobes` add a term in cos(ratio × ψ + phase), so a part can follow a
  turning, non-round section;
- `roll`, an idle roller a trunk turns while it is over it (a trunkPath only).


The rosser's README ("Rig schema") has the exact formulas and parse rules;
`Machines/tools/machinegen/rigmath.py` is the reference.

Extend this sparingly. Every new driver or gate must be added in three places and to the reference
poses. Prefer a mechanism that the existing drivers can express. The `trip` gate was added only
because a lever thrown at both ends of a continuous cycle could not be expressed otherwise; its
two halves agree where the direction changes, so nothing jumps. `gauge` stands for contact and
weight, and `roll` for rolling contact, so neither needs information no part could carry; the
rosser needed them because its parts move with where the trunk is, which no shaft angle says.

**The contract with gameplay.** The block entity implements a small read-only interface
(`IMillVisualState`): facing, fitted parts, blade metal, trunk, phase, client-side progress and
depth, direction, shaft angle and speed. The renderer polls it and nothing else. A part's
`requires` (`crankshaft`, `levers`, `sash1`, ...) hides it until that item is fitted. Gameplay
reads only the rig's anchors, never element names. This contract let the model and the gameplay
be rebuilt separately many times. The rosser's is `IRosserVisualState` (fitted parts, heads' metal,
trunk and class, client-side travel, state, running, wet, shaft angle and speed).

**Keeping the implementations in step.** The driver maths exists three times:
- the Python reference, `Machines/tools/machinegen/rigmath.py`;
- C# in `Machines/Core/RigAnimation.cs`;
- the site viewer's TypeScript, `site/src/lib/rig.ts`.

Two kinds of file hold them together:
- **The driver fixture**, `mods-src/seraphhorizons/tests/Machines/driver-fixture.json`, written by
  `Machines/tools/make_fixture.py` from the reference maths: every driver and key alone over a sweep of
  inputs (every window edge and mid-ramp, by nose and by tail, each class, several presences), a
  small composed rig with ride chains, and a list of drivers every parser must refuse. Its numbers are
  dyadic, so edge cases are exact in every language. The C# (`DriverFixtureTests`), the TypeScript
  (`site/test/rig.test.ts`) and the Python (`tools/tests/test_machinegen.py`, which also fails when the
  file is not what the maths writes) all replay it, to its tolerance of 1e-6. A new driver goes into
  the fixture first; the three implementations are then written against it, before any machine uses
  it.
- **Each machine's reference poses**, every shipped part's matrix at a grid of inputs:
  `tests/BuckingSawmill/rig-reference.json`, which `RigAnimationTests` and `site/test/rig.test.ts`
  replay, and `tests/Rosser/rig-reference.json`, which `RosserRigTests`, `site/test/rosser.test.ts`
  and `tools/tests/test_rosser_model.py` replay.

Regenerate a reference whenever its rig changes, and never edit it or the fixture by hand. A mismatch
there is always a real bug.

## A second machine: the rosser

The rosser (`mods-src/seraphhorizons/Rosser/`) is a ring debarker that draws a Logging Expanded trunk
lengthwise through a spinning cutter ring. Its README documents the machine; this is what building it
added to the approach.

**Share the maths, copy the game classes.** Before the rosser, the mill's rig maths, footprint, trunk
box and Logging Expanded bridge were moved to `Machines/` (C#) and the generic half of its generator
to `Machines/tools/machinegen/` (Python), both with `git mv` and namespace fixes only. Three gates
held the shipped mill while that happened: its unit tests with its reference poses, its Atlas
scenarios, and a byte-identical regeneration of its model. The game classes (renderer, ghosts, block
entity) were copied and adapted instead, as they are client-heavy and untested by Atlas; unifying
them is a follow-up once both have been seen in the game. A second taker or feeder is an interface
in the shared code (`ITrunkFeeder`, `IMachineGhost`), not a reference from one machine to the other's
block entity.

**A material that moves through the machine is an input, not a prop.** The trunk's travel T is saved
and synced by the server, advanced on the client with the shaft between syncs, and interpolated per
frame, as the mill's saw depth is. The rig gets a `trunkPath` (where the trunk starts, where it stops,
its shown lengths and the stations along it), and every part that moves with the trunk is a `gauge`
over windows on that path. The trunk's boxes follow T in 1/16-block steps, and cells over its path
that hold no element are `hollow`: ghosts, so nothing is built in the trunk's way, solid only where
the trunk is.

**Measure the material, then design for both sizes.** Logging Expanded's trunks are rounded squares,
not circles, and come in two shown sizes. A concentric ring needs both on one axis, which no fixed
support gives, so the rosser has a weighing cradle (counterweighted saddles and bottom rolls that a
thick trunk weighs down). The generator reads the trunk models from Logging Expanded's zip, measures
their flats and corners, and fails if they move.

**Fit every contact to the real contact.** Each gauge's windows (a rise as the nose arrives and a
drop behind the tail, per class) were fitted so the part bears on the trunk without passing through
it, and the swept-path check walks each class's real model every 1/16 block of the trip against
every part. The scraper arms follow the rounded section with harmonics of the ring's turn fitted to
their real geometry.

**One definition for a value gameplay and the model share.** The feed's pace is gameplay's (a constant
per class, derived in `RosserPace` from settings), and the model draws it as gears (`feed.gear` in the
rig). The feed input φ is defined by the trunk (it grows by ΔT / `feed.blocksPerRadian`), so the rolls
turn exactly with the trunk whatever the settings; the drawn gear ratio is a separate number, and a
unit test holds it to the gameplay's pace at the defaults, within 3 %. What cannot be drawn (the heads'
tier factor, a changed setting) is put where no teeth show it: the selector's plain dog faces.

**Rebuild the cells' boxes from the shipped files.** Several of the rosser's dense cells sit near ties
of the greedy box splitter, so a rounding difference of 1e-4 changed one cell's split between the
generator and the site. Its boxes are rebuilt from the shipped, rounded shape posed by the shipped
rig, and a Python test and the site's test hold every reader to them.

**Keep checks fast to iterate.** A full rosser run takes about 90 s; `--quick` skips the z-fighting
fix, the arms, the clearances and the swept paths for layout passes. The full run is still the gate.

## Reviewing without the game

1. **Projections from the written files.** Render orthographic and isometric projections of the
   shipped shape posed by the shipped rig, at rest, mid-cut and at the bottom, and look at them.
   Clip to a region and zoom for joints. Colour by part to check the rig, and by texture code to
   check the wood and iron rule. Render from the files on disk, not from the generator's internal
   state, so you review what ships. A small throwaway script that flattens elements, applies
   `part_matrix` and draws filled polygons with Pillow is enough.
2. **The browser viewer.** An interactive viewer of shape and rig is part of the GitHub Pages site,
   in `site/`, documented under [`docs/recipe-browser/`](../recipe-browser/). Add the new model to
   it and use it for review passes with the owner, who can then name elements and poses in their
   notes.
3. **What only the game shows:** lighting and shading, texture stretch on retextured faces,
   z-fighting between close faces, sounds and particles, item transforms in the GUI, hand and
   ground, and how the machine reads at play distance. At the time of writing, nothing client-side
   of the sawmill had been run in the game. Atlas scenarios exercise placement, ghosts, power,
   loading, cutting and the rack on a headless server, but not the renderer. Plan a session in the
   game before release. The same holds for the rosser: its renderer, trunk segments, particles and
   sounds were written without a client run.

## Working with agents on this

What worked:
- **Research agents first,** each reverse-engineering one thing (the source mod's sawmill, Logging
  Expanded's rack and trunks) and saving a report to a file that later passes cite.
- **One agent owning the generator and model at a time.** Gameplay was built against the rig's
  data contract, so the two sides could move independently.
- **Review notes sent one at a time** and folded into a pass. Each pass ended with the full checks,
  a regenerated model, renders, an updated README and a short report.
- **The generator's validation is what made long iteration safe.** Many passes in, a new joint
  or a moved post still could not silently break a rope or a bearing.
- **For the rosser, waves of agents with one owner per file.** The shared refactor, the
  `machinegen` move and the site's maths came first, in parallel, held together by the driver
  fixture, which was delivered before anything else; the gameplay rules (C# `Core/`) were built
  against a hand-written test rig until the generator shipped a provisional one with final part ids,
  requires and anchors. Rig keys written down as a contract between agents (what each key means and
  which rules the parser enforces) let the model and the gameplay change separately.

What did not:
- Hand-maintained duplicates drifted. A viewer's maths once disagreed with the generator until a
  script compared every element's posed corners against the Python reference.
- `dotnet build` without `--no-incremental` can skip repacking changed assets into the zip.
- Atlas runs from the wrong directory left large scratch worlds inside the mod. Run from the repo
  root, as `CLAUDE.md` says.
- Reports that claimed more than was checked. If a property matters, write a check for it before
  writing it down; this is how a counterweight described as over-centre turned out not to be.

## Checklist

1. Get the original author's permission. Add `CREDITS.md` (shipped in the zip) and a scope note in `LICENSE`.
2. `python3 tools/packtool.py fetch`, then unpack and disassemble the source mod. Save a VERIFIED/INFERRED report.
3. Write the moving-parts table (part, driven by, drives) and settle the footprint, controller,
   placement orientation, material path, feeding block and power cell. Check them against each other.
4. Write the generator on `Machines/tools/machinegen/`: constants, one builder per piece, rig parts,
   validation, the origin shift. A new driver goes into `machinegen/rigmath.py` and the driver
   fixture first (`python3 mods-src/seraphhorizons/Machines/tools/make_fixture.py`), then into the C#
   and the TypeScript, which replay it.
5. Add a check for anything you would otherwise only eyeball, especially paths, supports, joints and contacts,
   and for coplanar overlapping faces (z-fighting), which show only in the game or the viewer.
6. Generate, then check determinism:
   ```sh
   python3 mods-src/seraphhorizons/BuckingSawmill/tools/make_shape.py --out "$TMPDIR/a"
   python3 mods-src/seraphhorizons/BuckingSawmill/tools/make_shape.py --out "$TMPDIR/b"
   diff -r "$TMPDIR/a" "$TMPDIR/b"
   python3 mods-src/seraphhorizons/BuckingSawmill/tools/make_shape.py      # writes the assets and the reference poses
   ```
   The rosser's is `mods-src/seraphhorizons/Rosser/tools/make_shape.py`, the same way. After any
   change to `machinegen`, regenerate every machine into `$TMPDIR` and `diff -r` against the
   committed files: a machine you did not mean to change must come out byte-identical.
7. Use the shared rig maths (`Machines/Core/`), with tests against the reference poses; build the
   renderer on the visual-state interface; keep gameplay on the rig's anchors.
8. Render projections and review them; add the model to the site viewer and review there.
9. Run the full checks before every commit:
   ```sh
   VINTAGE_STORY=$HOME/Games/vintagestory dotnet build mods-src/seraphhorizons -c Release --no-incremental
   dotnet test mods-src/seraphhorizons/tests
   python3 -m unittest discover -s tools/tests
   npm --prefix site run check && npm --prefix site test
   mkdir -p build/atlas-tmp
   TMPDIR=$PWD/build/atlas-tmp VINTAGE_STORY=$HOME/Games/vintagestory dotnet test tests/PackTests \
     --filter "FullyQualifiedName~PackTests.WoodworkingScenarios.|FullyQualifiedName~SharedWorldScenarios.Locked_mod|FullyQualifiedName~SharedWorldScenarios.Server_boots"
   rm -rf build/atlas-tmp
   ```
10. Update the mod README (mechanism, rig schema, driver table, validation list, element prefixes).
11. Before release, play it in the game: lighting, textures, z-fighting, sounds, item forms.
