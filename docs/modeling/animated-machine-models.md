# Animated machine models

How to build an animated multiblock machine model for a mod in this repository, from another mod's
model or from scratch. The bucking sawmill (`mods-src/buckingsawmill/`) is the worked example
throughout. Its [README](../../mods-src/buckingsawmill/README.md) documents the finished machine in
detail; this guide is about the process, and the mistakes worth not repeating.

## When to use this approach

Use it for any machine that has moving parts tied to gameplay state: shafts turned by mechanical
power, saws that sink with a cut, levers thrown at the ends of a cycle. It produces:

- **A generator script**, [`tools/make_shape.py`](../../mods-src/buckingsawmill/tools/make_shape.py).
  It is stdlib-only Python, reads the source mod's shape from `build/mods/` and writes everything below.
- **Two shape files**: the whole machine, and the static frame only (the block draws the frame; a
  renderer draws everything that moves).
- **A rig file**, `assets/<modid>/config/rig.json`. It holds the footprint (cells with collision
  boxes), the anchor points gameplay needs (power cell and face, infeed and output), and the moving
  parts. Each part has the element-name globs it owns and the drivers that pose it.
- **A reference of poses**, [`tests/rig-reference.json`](../../mods-src/buckingsawmill/tests/rig-reference.json):
  every part's matrix at a grid of inputs, from the script's own maths. It keeps every other
  implementation honest.
- **A renderer driven by the rig** (`MillRenderer.cs`, with the maths in
  [`Core/RigAnimation.cs`](../../mods-src/buckingsawmill/Core/RigAnimation.cs)), so gameplay code never
  knows element names.
- **A browser viewer** for review without the game (see [Reviewing without the game](#reviewing-without-the-game)).

**Why a generator, not hand-editing in VS Model Creator.** The model is reproducible from the
source mod's shape and a list of named constants, so it can be regenerated when the source mod
changes. It is validated on every run, which is what makes dozens of review passes safe. Its
output is deterministic, so diffs show real changes.

If someone does edit the output by hand, those edits are lost on the next run, and they must keep:
- element names, because the rig finds its parts by name prefix;
- the split between the full shape and the frame-only shape;
- the cell boxes in `rig.json`, if anything moved across a cell boundary.

Port hand edits back into the script, or stop regenerating.

## Before modelling

### Permission and credit

Parts taken from another mod's model belong to that mod's author. For the sawmill:
- Bobrik00 gave permission to use and modify Immersive Woodworking's sawmill model.
- By the end, about a third of the elements were still Immersive Woodworking's (the gears, saw
  blades, saw heads and cranks); the rest had been rebuilt. Credit what is actually taken, and
  re-check the wording when the model has changed a lot.
- [`CREDITS.md`](../../mods-src/buckingsawmill/CREDITS.md) names those parts, says they are the
  author's, used with permission, and outside the repository's licence. The csproj copies it into the release zip,
  next to `modinfo.json`.
- The root `LICENSE` names the shapes folder as an exception to the Apache licence.

Do all three before any derived model ships.

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
- **Files:** every texture code is declared, and every written file parses.
- **Shifted output equals checked model:** every element posed by the shipped rig lands where the
  checked one does, moved.
- **Determinism:** not a self-check, so run it by hand (see the checklist).

## The rig

The schema is in the mod README's "Rig schema" section. In short:
- `cells` (with up to three boxes each), `powerCell`, `powerFace`, `infeedSide`, `outputSide`,
  `output.pos`, `trunkBed` and `saw` are for gameplay;
- `parts` is an ordered list of `{id, match, requires, ride, drivers}` for the renderer.

**Inputs.** Every driver is a function of four numbers:
- θ, the signed shaft angle;
- *d*, the saw depth, 0..1;
- *L*, lifting, 1 while the saws go up;
- ψ, the shaft's total travel either way.

A part's matrix is its drivers composed in order, then its `ride` parent's matrix.

**Drivers:**
- `rotate` (optionally `rectified`, using ψ);
- `slide` and `swing` (sinusoidal in θ);
- `feed` (linear in *d*);
- `step` (a ramp over a depth window, with lift gates `hold`, `block` and `trip`);
- `stretch` (ropes).

Extend this sparingly. Every new driver or gate must be added in three places and to the reference
poses. Prefer a mechanism that the existing drivers can express. The `trip` gate was added only
because a lever thrown at both ends of a continuous cycle could not be expressed otherwise; its
two halves agree where the direction changes, so nothing jumps.

**The contract with gameplay.** The block entity implements a small read-only interface
(`IMillVisualState`): facing, fitted parts, blade metal, trunk, phase, client-side progress and
depth, direction, shaft angle and speed. The renderer polls it and nothing else. A part's
`requires` (`crankshaft`, `levers`, `sash1`, ...) hides it until that item is fitted. Gameplay
reads only the rig's anchors, never element names. This contract let the model and the gameplay
be rebuilt separately many times.

**Keeping the implementations in step.** The driver maths exists three times:
- the Python reference in the generator;
- C# in `Core/RigAnimation.cs`, tested against `tests/rig-reference.json` by `RigAnimationTests`;
- the site viewer's TypeScript, which should be tested against the same file.

Regenerate the reference whenever the rig changes, and never edit it by hand. A mismatch there is
always a real bug.

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
   game before release.

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
4. Write the generator: constants, one builder per piece, rig parts, reference maths, writers,
   validation, the origin shift.
5. Add a check for anything you would otherwise only eyeball, especially paths, supports, joints and contacts.
6. Generate, then check determinism:
   ```sh
   python3 mods-src/buckingsawmill/tools/make_shape.py --out "$TMPDIR/a"
   python3 mods-src/buckingsawmill/tools/make_shape.py --out "$TMPDIR/b"
   diff -r "$TMPDIR/a" "$TMPDIR/b"
   python3 mods-src/buckingsawmill/tools/make_shape.py      # writes the assets and tests/rig-reference.json
   ```
7. Implement the rig maths in C# `Core/` with tests against the reference poses; build the renderer
   on the visual-state interface; keep gameplay on the rig's anchors.
8. Render projections and review them; add the model to the site viewer and review there.
9. Run the full checks before every commit:
   ```sh
   VINTAGE_STORY=$HOME/Games/vintagestory dotnet build mods-src/buckingsawmill -c Release --no-incremental
   dotnet test mods-src/buckingsawmill/tests
   python3 -m unittest discover -s tools/tests
   mkdir -p build/atlas-tmp
   TMPDIR=$PWD/build/atlas-tmp VINTAGE_STORY=$HOME/Games/vintagestory dotnet test tests/PackTests \
     --filter "FullyQualifiedName~BuckingSawmill|FullyQualifiedName~PackLoadScenarios"
   rm -rf build/atlas-tmp
   ```
10. Update the mod README (mechanism, rig schema, driver table, validation list, element prefixes).
11. Before release, play it in the game: lighting, textures, z-fighting, sounds, item forms.
