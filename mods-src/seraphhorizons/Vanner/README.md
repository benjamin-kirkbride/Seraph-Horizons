# Vanner

Part of the Seraph Horizons mod (`../README.md`): the last of the ore line's concentrators (epic #684), a
**Frue vanner** (1874). An endless rubber belt, four feet wide, runs over a head roller and a foot roller
twelve feet apart, falling gently to the foot, on a frame that is shaken from side to side; the belt creeps
slowly up to the head under a spray of wash water. Pulp fed onto it a little below the head spreads over
the shaking belt: the heavy grains settle on it and ride up over the head roller into the **concentrate
box**, the light ones are washed back down and off the foot into the **tailings box**. Each box ends in a
vanilla chute section, and the pulp comes in by one down the feed box.

This folder holds the model's generator and its rig (`tools/`). The gameplay is #923 and is not built yet;
the model is #924. "Gameplay" below says what the rig offers it.

**Driven like every machine.** A vanilla axle at the back turns the countershaft directly (the v2 wash
house's line shaft, pulley and drive belt are gone). The shake comes from two eccentrics, which shake the
frame the same whichever way the axle turns; the creep comes through a ratchet, which turns the belt one
way, to the head, whichever way the axle turns. No rectifier is needed, no new driver was added, and
`Machines/tools/machinegen/` is unchanged.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` and `Pipes/`, this folder's neighbours.

## The machine

**Footprint.** 2 wide, 1 high, 3 long (6 cells), the issue's guide, each dimension set by what has to fit:

- **Long (z, 3 blocks, 48 voxels):** the belt, 31 between the rollers' centres (12 ft at about 2.75 voxels a
  foot, the scale of the belt's width), the concentrate box and its chute section in front of the head
  roller, and the tailings box and its chute section behind the foot roller.
- **Wide (x, 2 blocks, 32):** the belt, 11 wide (4 ft), between its two rails on the west column; the
  countershaft, its eccentrics, yokes and bearings along the east column. The countershaft is on the east
  column's centre line, where the axle comes in, which leaves a strip about 5 wide free east of it.
- **High (y, 1 block):** the belt's top is at 11.7 at the head, 10.5 at the foot; the feed box and the spray
  pipe over it reach 15.8 and 14.6; the water inlet and the infeed chute section are open on the top face.

**Orientation.** The player stands at the native north side (the front, low z) and the machine runs away from
them, south: the head of the belt, the concentrate box and its chute at the front, the foot and the tailings at
the back. The **controller** is the front west cell, `[0,0,0]`, under the head of the belt, nearest the
player; the build frame's corner is the controller's, so the shipped files need no shift.

**Anchors** (`assets/seraphhorizons/config/vanner-rig.json`); every cell is different:

| Anchor | Cell, face | What meets it |
|---|---|---|
| `powerCell`, `powerFace` | `[1,0,2]`, south | A vanilla axle, north to south, on the countershaft's line (x 24, y 8): the countershaft's oak cross is the axle's profile, flush with the face |
| `waterCell`, `waterFace` | `[1,0,0]`, up | A ppex pipe coming down: the water line's first bend is open on the face, ppex's 6 x 6 tube at its centre |
| `feedCell`, `feedFace` | `[0,0,1]`, up | A vanilla chute coming down: the infeed chute section, upright, flush with the top face |
| `concentrateCell`, `concentrateFace` | `[0,0,0]`, north | The concentrate chute section, along z, flush with the front face |
| `tailingsCell`, `tailingsFace` | `[0,0,2]`, south | The tailings chute section, along z, flush with the back face |

The three chute sections are vanilla's straight chute in copper, exactly: an 8 x 8 tube, 1 thick, centred in
the cell's face (`VANILLA_CHUTE`, held to the game's `shapes/block/metal/chute/straight.json` whenever the game
is installed). The chute anchors' names and the water cell's place are **provisional, pending #711**
(the shared machine code); the `pipes` key (the metals' parts) and `drive` (below) are for gameplay too.

**Fitted parts** (the rig's `requires`). Everything with `requires` null is the frame: the sills, the
uprights' foot pins in their cheeks, the countershaft's pedestals and bearings, the feed box's posts and
brackets, the spray pipe's post and bracket, the saddle under the water line's bend, the concentrate and
tailings boxes with their chute sections. The rest:

| `requires` | Parts | Draws |
|---|---|---|
| `countershaft` | `countershaft` | The axle's oak cross, the steel shaft, two collars, the two eccentric sheaves |
| `shaker` | `shaker`, `uprightw`, `uprighte` | The shaking frame: two oak rails, three ties, the rollers' bearing blocks, the two yokes and their arms, the worm shaft's bearings and hangers, the rails' clevises and pins; and the six oak uprights it stands on |
| `rollers` | `head`, `foot` | The head roller with its journal, collars and worm wheel; the foot roller |
| `belt` | `belt` | The belt round both rollers, and its two raised edges |
| `creep` | `worm`, `pawl` | The worm shaft: its rod, collars, the worm (core and thread rings) and the ratchet; the fixed pawl on its post |
| `feedbox` | `feedbox` | The feed box, its weir and apron, and the infeed chute section |
| `pipecopper`, `pipelead` | one each | The water line in copper or in lead: the same elements, one drawn |

The build stages (which items fit what, in what order) are not designed yet: the ids split the machine where a
stage could, in the order the floating check builds it up (README "Open for the owner").

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing
from `build/mods`. Timber is the game's debarked oak and its oak planks; castings and straps its iron plate;
shafts, sheaves, the worm, its wheel and the ratchet its plain steel sheet; the belt its black leather (the
game has no rubber); the chute sections vanilla's chute texture (`game:block/metal/sheet/copper1`); the water
line UnifiedPipes' copper or lead pipe with solder joints (`Pipes/tools/solderedpipe.py`). `tools/make_shape.py`
writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/vanner.json` | The whole machine, both water lines (400 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/vanner_frame.json` | The frame only (87 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/vanner-rig.json` | The cells, the anchors and the part rig (13 parts). |
| `tests/Vanner/rig-reference.json` | Every part's matrix at 21 poses of θ and ψ, from the reference maths. |

### How the machine works

Positions are in the generator's **build frame**: the native axes (x west to east, y up, z north to south) in
voxels from the machine box's north-west bottom corner, so the shipped files are the build frame divided by 16.

**The frame.** Two oak sills along z under the rails' uprights, and a third under the countershaft's three
pedestals, each with an iron bearing block (2.6 square) at y 8. The feed box stands on four oak posts (two each
side, clear of the shaking frame) with iron brackets under its floor; a fifth post on the west carries the
spray pipe's capped end. An iron saddle on the front bearing's cap carries the water line's second bend.

**The countershaft** runs along z on the east column's centre line (x 24, y 8) from the power face (z 48) to
z 15.6: the vanilla axle's oak cross (a 4 x 2 and a 2 x 4 bar) for its last 2.6, then a steel shaft through the
three bearings, located by a collar each side of the middle one. On it, keyed, are two eccentric sheaves (z 22.5
and 36), each 0.5 off the shaft's centre (under it at rest).

**The shaking frame.** Two oak rails along z (x 2.5..4 and 16..17.5, y 7..8.5) tied at both ends beyond the belt
and once inside its loop, at the first sheave. Each eccentric runs in an iron yoke, an upright slot whose west
and east bars sit on the sheave's flats' circle and whose top and bottom bars clear its swing: the yoke, and the
frame through an iron arm to the east rail, follow the sheave's centre across, x = 0.5 sin θ, while the sheave
swings up and down in the slot. The frame stands on **six oak uprights**, three under each rail, each pinned at
its foot in iron cheeks on the sill (y 1.9) and at its head in an iron clevis under its rail (y 6.3): the period
vanner's flexible uprights, drawn as rigid posts pinned top and bottom, so they lean 6.5° each way as the frame
is shaken and carry it, and hold it along its length. Their heads move on an arc, so they drop 0.03 at the ends
of a stroke against the rail's straight slide.

**The belt and the rollers.** The head roller (axis y 9.3, z 7) and the foot roller (y 8.1, z 38) are oak drums,
2 in radius, a little wider than the belt, on steel journals in iron bearing blocks on the rails (the foot's set
into them), with a collar outside each. The belt, 0.4 thick, runs round both, its top run on their upper
tangent, falling 1.2 in 31 (1 in 26) to the foot; two raised edges, 0.6 high, run along the top run inside its
sides to keep the water on it.

**The creep.** Under the head roller's east journal, on two iron bearings hung from the east rail, is the worm
shaft (along z, at x 19.6, y 4.3), riding the shaking frame. On it are a **ratchet** of 12 saw teeth, pitch
radius 1.91 so a tooth's pitch is the shake's stroke (1.0), and a single-start **worm** under the head roller's
12-tooth **worm wheel** (pitch radius 3.6; the worm's 1.4; module 0.6). A **pawl**, fixed on a short oak post from
the ground, lies just under the ratchet's bottom teeth (0.05 clear of their reach) through the whole shake: as
the frame moves east the bottom tooth catches on it and the ratchet turns a tooth; as the frame moves back the
tooth slips over it. So the ratchet turns a tooth a stroke, one way, whichever way the axle turns, the worm turns
with it, and the wheel turns a tooth for every turn of the worm: the head roller draws the belt's top run up to
the head by about 0.1 voxel a turn of the axle (0.4 in a stroke at the model's scale; the period's belt crept
0.2 to 0.8 in a stroke). The worm and its wheel both ride the frame, so they never move on each other. The foot
roller turns with the belt.

**The feed box** spans the belt below the head (z 19.4..28.6, its floor at y 12.2..12.7, over the belt's
edges): the infeed chute section comes down into it from the feed cell's top face, its west and south walls'
inner faces on the chute's outer ones. Its north wall is a weir (to y 13.6), and an apron slopes from the weir's
top down to just over the belt (its lip at z 17.4, inside the belt's edges as they shake): the pulp spills over
the weir and onto the belt about 10 voxels (3.7 ft) below the head roller's axis.

**The water line** is ppex's pipe drawn soldered (one part per metal): from the water face it comes down into a
bend (x 24, y 13, z 8), turns south into a second bend on the saddle (z 14.5), turns west to its end at x 19, and
there reduces, through a plug and a ring of solder, to the **spray pipe**: a smaller soldered tube (2.4 across)
across the belt at y 13.4, capped with solder at its west end on its post's bracket, with five nozzles under it
over the belt, 7.5 below the head roller's axis. ppex's own pipe (6 across) cannot cross a one-high belt.

**The boxes and the chutes.** The **concentrate box** lies under the head roller (z 1.8..8.8), its plank floor's
top at y 5.2 (just over the chute's bore, 5.0) on two oak bearers, its walls to 6.5, under the belt's wrap round
the head (6.9) and its bottom run. A spout through its north wall rests on the concentrate chute section's
bottom wall, inside its bore. The **tailings box** lies behind the foot roller (z 39.8..44), its north wall low
(6.3) under the belt's wrap, and its spout runs through its south wall into the tailings chute section. The
concentrate leaves the belt as it rounds the head; the tailings run off the belt as it rounds the foot.

Every moving part (θ the axle's angle, ψ its travel either way):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Countershaft (`countershaft`) [countershaft] | The axle, at the power face | The two eccentric sheaves | `rotate` z about (1.5, 0.5), ratio 1, on θ |
| Shaking frame (`shaker`) [shaker] | The sheaves, in its yokes | The rollers, the belt, the worm shaft, the uprights' heads | `slide` x, amplitude 0.5 voxel (1/32 block), on θ: x = 0.5 sin θ |
| Uprights (`uprightw`, `uprighte`) [shaker] | The shaking frame, at their head pins | — (they carry it) | `swing` z about each side's foot pins (y 1.9), amplitude −asin(0.5/4.4) (6.5°), on θ |
| Belt (`belt`) [belt] | The head roller | The foot roller | none; rides `shaker` (drawn still: a plain belt moving along its length looks the same) |
| Head roller (`head`) [rollers] | The worm, by its wheel | The belt | `rotate` x about (y 9.3, z 7), ratio −1/144, on ψ; rides `shaker` |
| Foot roller (`foot`) [rollers] | The belt | — | `rotate` x about (y 8.1, z 38), ratio −1/144, on ψ; rides `shaker` |
| Worm shaft: rod, worm, ratchet (`worm`) [creep] | The fixed pawl, a tooth a stroke | The head roller's worm wheel | `rotate` z about (19.6, 4.3), ratio −1/12, on ψ; rides `shaker` |
| Pawl (`pawl`) [creep] | Static | The ratchet | none |
| Feed box, water lines, frame | Static | — | none |

There are two toothed wheels and each meshes: the worm wheel with the worm, the ratchet with the pawl.
Every shaft runs in two bearings or more: the countershaft in three on the frame, the worm shaft in two and each
roller's journal in two on the shaking frame.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Vanner/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Vanner/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Vanner/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 18 s (4 s quick) and is deterministic (two runs into two folders are byte-identical,
logs included). `tools/validate_vanner.py` holds the checks; every run checks its output and exits non-zero if
one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part; the requires vocabulary; the water line's metals the same elements; a part that rides another fitted only
  with it.
- **Textures** by role: oak for timber, the uprights, the rollers' drums, the pawl's post and the axle's cross;
  planks for the boxes and the feed box; iron for castings and straps; steel for shafts, sheaves, pins, the worm,
  the wheel and the ratchet; rubber for the belt; the chute texture for the chute sections; each water line in its
  metal, solder only on the reducer and the cap.
- **Thickness:** nothing thinner than 0.6 across but the belt, teeth, thread rings and solder.
- **Floating** at every build state (the frame, then each part added in turn, then the water line): one piece
  from the ground.
- **Containment:** nothing leaves the machine box over a turn.
- **Anchors:** the countershaft's oak cross on the power face's centre in the axle's 4 x 4 profile; the water
  line's inlet open on the water face's centre in ppex's 6 x 6 tube; each chute section exactly vanilla's (its four
  walls tile the 8 x 8 ring, 1 thick, centred in the face, flush with it, in the chute's texture), and
  `VANILLA_CHUTE` the game's own file where the game is installed; the feed box round the infeed's foot; the five
  anchor cells different.
- **Drops** (a ray down from each lip): the infeed into the feed box; the pulp off the apron and the wash water from
  each nozzle onto the belt at seven points of the shake; the concentrate off the head into the concentrate box and
  the tailings off the foot into the tailings box, across the belt; each box's spout into its chute section's bore.
- **Yokes:** each sheave's centre on its yoke's slot line through a turn, the bars on its flats' circle, its swing
  clear of the top and bottom bars by 0.3 or more.
- **Drive:** the countershaft with the axle, the frame 0.5 sin θ; the ratchet's tooth the stroke; the fixed pawl
  just under its teeth's reach and under the bottom tooth through the shake; the worm on the wheel's pitch circle, in
  its plane; the ratchet a tooth a turn, the wheel a tooth a worm turn, the foot with the head; the belt's top run
  moving to the head for θ either way.
- **Supports:** the countershaft in its three bearings, the worm shaft and each roller's journal in two.
- **Uprights:** each leaning about its foot pin, its head within the arc's drop (0.03) of the rail's pin through a
  turn, clear of the rail above it.
- **Clearances:** no two parts touch over 36 poses (turns either way, the creep run on) except the intended
  contacts listed in `ALLOWED` (shafts in their bearings, sheaves in their yokes, the worm in its wheel, the belt on
  its rollers, the uprights on their pins, the foot's journals through the rails).
- **Swept paths** (full runs): the same for the moving parts every 3.75° of a turn, and with the creep run on.
- **No z-fighting** (full runs): no coplanar overlapping faces at six poses after the fix (`fix_coplanar`), which
  removes the faces pressed against their own part and insets the rest.
- **Files:** every texture declared; a lid over every column; no anchor cell hollow; the shipped model is the
  checked one (no move: the build frame's corner is the controller's).

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig (the second water
line left out, as it is the first's), as the other machines' are.

### Rig schema (`vanner-rig.json`)

The bucking sawmill's schema (`../BuckingSawmill/README.md`, "Rig schema") with the rosser's pipe parts; nothing
is added to the shared rig maths.

- **Inputs.** θ, the axle's angle (radians), and ψ, its travel either way (`"input": "travel"`). The
  countershaft rotates about z by θ; the shaking frame slides along x by 1/32 sin θ; the uprights swing about z by
  −0.1139 sin θ about their feet; the worm shaft (ratio −1/12), the head and the foot rollers (−1/144) rotate by ψ
  and ride the shaking frame. No work, class or presence.
- **Keys.** `cells` (six, each with its boxes and a `lid`), `powerCell`/`powerFace`, `waterCell`/`waterFace`,
  `feedCell`/`feedFace`, `concentrateCell`/`concentrateFace`, `tailingsCell`/`tailingsFace`, `pipes` (`requires`
  per metal), `drive` and `parts`. `requires` values: `countershaft`, `shaker`, `rollers`, `belt`, `creep`,
  `feedbox`, `pipecopper`, `pipelead`, or null.
- **`drive`**: `strokesPerTurn` (1: one stroke across and back a turn), `shake` (the stroke, blocks), `beltPerTurn`
  (blocks the belt creeps to the head a turn, either way), `ratchet` (`teeth`, `teethPerTurn`) and `worm`
  (`starts`, `wheelTeeth`): figures a renderer or sound can use; the rig's drivers already pose them.

**In the viewer** (`site/models.json`, `vanner`): the axle is the θ slider and Play turns it at two turns a
second by its input speed (the shake reads as a shake; the game turns about 0.5 a second at the reference speed
0.6); ψ is added up under it, so the rollers creep on in Play and when θ is dragged either way. Every requires
value is a checkbox, and the water line is a "Water line" select, copper or lead.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `cs_*` (`entrya`, `entryb`,
`rod`, `collar1`/`2`, `sheave1`/`2`), `sk_*` (`rail_w`/`e`, `tie1`..`3`, `headbearing_*`, `footbearing_*`,
`yoke1`/`2_*`, `arm1`/`2`, `wbearing1`/`2`, `whanger1`/`2`, `up<side><n>_cheek*`/`_pin`), `upw_*` and `upe_*` (the
uprights), `belt_*` (`runa`, `runb`, `wrap*`, `edge1`/`2`), `head_*` (`drum`, `journal`, `collar*`, `wheel_*`),
`foot_*`, `worm_*` (`rod`, `collar*`, `core`, `thread1`..`4`, `ratchet_*`), `pawl_*` (`post`, `arm`), `fb_*`
(`floor`, `w`, `e`, `s`, `weir`, `apron`, `chute_*`), `pipecopper_*` and `pipelead_*`, and `fr_*` for the frame
(`sill_*`, `up<side><n>_*`, `pedestal*`, `bearing*`, `saddle`, `post_*`, `bracket_*`, `spraypost`,
`spraybracket`, `cb_*`, `tb_*`, `chute_conc_*`, `chute_tail_*`). Hand edits are lost when the script runs again:
port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in a
client or the site's viewer.

1. **The belt is drawn still**, and its rollers turn under it; in the game a renderer could scroll its texture by
   `beltPerTurn`. The creep is a tenth of a voxel a turn, so this shows only over many turns.
2. **The ratchet turns evenly** with ψ rather than a tooth at a time.
3. **The uprights are rigid posts on pins**, not the period's bending springs; their heads drift 0.03 off the
   rail's pins at the ends of a stroke.
4. **The foot roller's journals pass through the rails** where its bearing blocks are set into them.
5. **No pulp, water or concentrate is drawn**: on the belt, in the boxes, from the nozzles.
6. **No tank dip, tightener or idlers.** The period belt's return run dipped through a tank of water under the
   machine, where the concentrate washed off, over tightener rollers, and small idlers carried its top run; here
   the belt runs round its two rollers only, and the concentrate drops into the box under the head.
7. **Round parts are polygons:** the drums and sheaves 12-sided, shafts and pins 8-sided.

## Open for the owner

1. **Build stages.** The `requires` ids are a proposal (countershaft; shaking frame with its uprights; rollers;
   belt; creep; feed box; water line), in the order the floating check builds the machine up; the frame recipe,
   the stages and their materials are to be settled, as the crucible furnace's were.
2. **The ratchet's place.** #923 says "the ratchet on it stays" (on the countershaft). v2 put the worm shaft in
   line with the countershaft beyond its end, with the pawl on the shaking frame, and its worm wheel slid along the
   fixed worm by the whole shake. Here the worm shaft with the ratchet rides the shaking frame and the pawl is fixed,
   so the worm and wheel never move on each other; the countershaft carries only the eccentrics. Either is a
   ratchet driven by the shake.
3. **Uprights in place of v2's slides** (its open point 13): the period's flexible uprights, as pinned posts (see
   weak spot 3). Six, three a side.
4. **Two eccentrics**, where the Frue had three cranks on its crankshaft (v2 had two).
5. **The shake is drawn large** so it reads: a 1-voxel stroke (about 4 in at the belt's scale; the period's was
   about 1 in, 180 to 200 a minute). The creep per stroke is the period's.
6. **The water line reduces to a smaller spray pipe.** ppex's 6-wide pipe cannot cross a one-high belt, so it comes
   down from the water cell's top face beside the belt and reduces, through a soldered plug, to a 2.4-wide soldered
   spray pipe with five nozzles. A ppex-size spray pipe would need a second block of height.
7. **The anchors are provisional (#711):** their names (`feedCell`, `concentrateCell`, `tailingsCell` with
   their faces, as the v2 branches named the infeed) and their faces: the concentrate out of the front, the
   tailings and the power at the back side by side, the infeed and the water from above. The water face is the
   top of the east front cell, the one place a 6-wide pipe reaches the spray's height.
8. **The concentrate box under the head**, not a tank the belt dips through (weak spot 6); its floor sits just
   over the chute's bore so its spout reaches into the chute.
9. **The controller cell** is the front west cell, under the head; which way the machine extends from the player
   (east, as here, or mirrored) is the gameplay's placement rule.
10. **The belt's texture:** black leather, as the game has no rubber.
11. **The free strip** east of the countershaft (about 5 wide): the footprint's width is the belt's and the
    countershaft's; nothing is put there.
12. **Play's speed** in the viewer: two turns a second.

## Gameplay

Not built: #923 (crushed or ground ore in by chute, concentrate and tailings out by chute, 80 % of the catchable
metal at 3 rock an hour; resistance 0.08, 0.016 empty; 15 L of water a rock). What the rig offers it:

- **The drive.** A renderer turns θ with the axle's angle and ψ with its travel (the shared `ShaftClock`), and
  draws every part by the shared rig maths (`Machines/Core/RigAnimation.cs`): the countershaft, the shake and the
  uprights by θ, the creep by ψ, so the belt goes to the head whichever way the axle turns. `drive` gives the
  stroke and the belt's creep per turn for sounds and a scrolling belt texture.
- **Parts.** Every part with a `requires` is drawn when its value is fitted, the water line in the supplied metal
  (`pipes.requires`); the frame shape is the block's.
- **Anchors.** The power, water, infeed and the two output chutes, each a cell and a face (above).
- **Tests.** `tests/Vanner/VannerRigTests.cs` (the shipped rig through the shared parser, its vocabulary, no work,
  its anchors, and every pose of `tests/Vanner/rig-reference.json` replayed through `Machines/Core`),
  `tools/tests/test_vanner_model.py` and `site/test/vanner.test.ts` (the generated files against their own rules
  and the site's rig maths).
