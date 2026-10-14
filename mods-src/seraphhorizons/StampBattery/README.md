# Stamp battery

Part of the Seraph Horizons mod (`../README.md`): the crush-and-classify machine of the ore line (epic #684,
behaviour #728, model #729). A **wet five-stamp gravity battery** with its mesh discharge screen, fed by chute from
above and discharging by chute in front, so it can run on a chute line with nobody at it. One machine is one piece of
equipment (#711): a timber battery frame whose footprint is fixed from the frame on, and the parts its one level fits
into it.

| Level | Fitted (`requires`) | What it is |
|---|---|---|
| (frame) | none | Two oak end frames, the guide girts, the ties and the two bearers; the rectifier's gear case at the power cell and the line shaft along the back row |
| 1 | `mortar`, `camshaft`, `stamps`, `feedbox`, and `pipecopper` or `pipelead` | The battery: the mortar with its dies, screen, shoot, the infeed chute section and the output chute (its funnel and stub); the camshaft and its belt drive; five stamps; the plank feed box under the infeed; the water line to the feed opening |

**Status: the model only.** This folder holds the generator (`tools/`); there is no block, block entity or renderer
yet (#728, #711). Paths here are from this folder unless they start with `assets/` or `tests/` (the mod's), or
`Machines/`, this folder's neighbour, whose `tools/machinegen/` is unchanged by this machine (no new driver).

It is the battery half of the stamp mill's v2 model (`729-stampmill-model-v2`), re-laid out three deep. The v2 model's
jaw breaker and grizzly are gone: the jaw breaker is its own machine (#912, model #914). A Challenge self-feeder was
drawn as a level 2 in the first v3 pass and dropped in review: it did not look as good as the battery, and the
battery runs unattended anyway, chute in and chute out.

## The machine

**Footprint.** 36 cells, 3 wide (x), 4 high (y), 3 deep (z), no hollow cell. In the shipped frame the controller is
`[0,0,0]`, the bottom middle cell of the north face, where the output chute leaves: the cells run x −1..1, y 0..3,
z 0..2. Placed like the crucible furnace, it extends away from the player: the north face (the screen and the output
chute) faces them, and the feed and the power are behind. The shafts run along x; the ore runs north, from the infeed
at the back through the mortar to the output at the front.

| Row (z) | Holds |
|---|---|
| Front (0) | The output chute: its funnel under the screen and its stub flush with the front face, on an oak block |
| Middle (1) | The mortar on its block, the five stamps in their guides, the camshaft with its cams and bull pulley |
| Back (2) | The feed box under the infeed chute, the line shaft and its pedestals, the rectifier's gear case |

**Three deep** (#729 asks for the decision). The back row carries what the battery needs behind the stamps: the line
shaft (on the power cell's axis, which must be a cell's centre) and the gear case, the bull belt's lower pulley, the
infeed chute section, a block of the game's chute standing over the feed, and the feed box under it, which needs the
back row's depth for its floor to fall 50° onto the shoot. The mortar is 11 voxels deep with the stems on its middle
line, so from its back wall to the middle row's back face there are 2.5 voxels: no room for the feed there. So:
3 × 4 × 3, as #729 proposes.

**Anchors** (`assets/seraphhorizons/config/stampbattery-rig.json`, shipped coordinates):

| Anchor | Cell, face | Point | What comes or goes there |
|---|---|---|---|
| `powerCell`, `powerFace` | `[1, 0, 2]`, east | the face's centre | The vanilla axle, into the gear case |
| `waterCell`, `waterFace`, `water` | `[-1, 2, 2]`, west | `[-1.0, 2.5, 2.5]`, the water line's inlet | Process water, from a ppex pipe on the west face, down into the feed opening |
| `infeedCell`, `infeedFace`, `infeed` | `[0, 3, 2]`, up | `[0.5, 4.0, 2.5]`, the infeed chute's mouth | Ore by the game's chute from above: a vanilla `chute-straight-ud` placed on this face carries straight on down the machine's own section into the feed box. **Hand feeding goes in at the same mouth** (#728, #711), the player standing on the deck |
| `outputCell`, `outputFace`, `output` | `[0, 0, 0]`, north | `[0.5, 0.5, 0.0]`, the output chute's mouth | The crushed, classified ore (pulp), out of the funnel along the stub: a vanilla `chute-straight-ns` placed against this face takes it on |

There is no return-water anchor (#711: the water is used up) and no separate hand-feed anchor (v2's `feed`): one
infeed, one mouth.

**Power.** The vanilla axle comes in on the east face of `[1, 0, 2]` into a cast-iron **gear case** holding a
rectifier, as the draw bench's and the gear cutter's, so the **line shaft** coming out of its west side turns one way
whichever way the axle turns, at the axle's pace. The case is closed: its gears are not drawn. The line shaft runs west
along the back row at the power cell's height, in the case's boss and two pedestals, to its pulley outside the west end
frame, which belts the **bull pulley** on the camshaft. The belt is open, so the camshaft turns the line shaft's way.

**Either way round.** The vanilla axle may turn either way, and the machine works either way. Only the entry shaft
follows the axle's angle θ; everything else is behind the rectifier and follows the axle's travel ψ, which grows
whichever way the axle turns:

| Motion | With the axle reversed |
|---|---|
| Entry shaft | Turns backwards with the axle, into the gear case |
| Line shaft and its pulley | The rectifier's other gear pair carries it: it turns as forwards |
| Camshaft, bull pulley and cams | As forwards: the cams' involutes only lift turning that way, and they always do |
| Stamps | As forwards: their lift is a function of ψ |

A check holds this: at the same ψ, every part but the entry shaft stands alike for θ and −θ.

## Model

Everything was made for this mod: no other mod's model is used, so the generator needs nothing from `build/mods`.
Textures by role: debarked oak (`game:block/wood/debarked/oak`) for the frame's timbers, the entry shaft (the vanilla
axle's continuation), the mortar block, the output chute's block and the bull pulley's rim and arms; oak planks for the
feed box; iron plate for castings (the mortar, the shoot, the gear case, the pillow blocks, the cams); plain steel sheet
for shafts and wearing parts (stems, shoes, dies); the water line in `game:block/metal/sheet-plain/copper4` or `lead4`
(with `game:block/metal/ingot/leadsolder` declared for its joints); `game:block/metal/mesh1` for the screen; the game's
chute (`game:block/metal/sheet/copper1`) for both chute sections and the output funnel; plain leather for the belt.
`tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/stampbattery.json` | The whole machine, the water line in both metals (326 elements; drawn: 40 with nothing fitted, 300 at level 1) |
| `assets/seraphhorizons/shapes/block/stampbattery_frame.json` | The frame only (34 elements). A block would draw it and the inventory show it. |
| `assets/seraphhorizons/config/stampbattery-rig.json` | Footprint (cells with boxes and lids), anchors, the stamps' figures, the level and the part rig (15 parts) |
| `tests/StampBattery/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

### How the machine works

Positions here are in the generator's **build frame**: native axes in voxels from the machine box's north-west bottom
corner, x 0..48, y 0..64, z 0..48. The shipped files are the build frame moved one block west.

**The frame.** Two oak end frames (west x 3.5..7.5, east x 40.5..44.5), each a sill under a front post (z 11..15) and a
middle post (z 30..34, behind the mortar), a back post (z 43.6..47.6) standing on the ground at the back face, a cap over
all three, and three rails between the front and middle posts: the lower guide rail, the cam rail and the upper guide
rail. Across them: the two guide girts (each two strips, the stems running in the slot between them), the front tie at
the caps, the chute tie and the back tie at the caps either side of the infeed chute's section, and two bearers
(y 44..48), one between the middle posts and one between the back posts: the feed box's back board hangs on the back
one, and an iron strap from the front one holds the water line. The gear case stands east of the back row, clear of the
east end frame's back post.

**Level 1, the battery.** Five stamps on a line along x (z 24, x 12.4 to 35.6, 5.8 apart), each a steel shoe, an
iron head, a steel stem through the two guide girts and a square iron tappet. They stand in a cast-iron **mortar box**
(x 8..40, z 18.5..29.5, its rim at y 16.5, low enough that the heads show over it) on an oak mortar block, each over a
steel die. The **screen** (mesh, x 9.5..38.5, y 11..15.2) is in the front wall over the dies. At the back, the **feed
opening** over the lip in the mortar's back wall and the iron **shoot** rising from the lip under the feed box: the ore
slides down it into the mortar.

**The output chute** takes the pulp from the screen to the front face, and is all the game's chute's sheet, 1 voxel
thick as the game's chute is:

- **The funnel**, a section of the chute flared in plan, its mouth as wide as the whole screen (x 9.5..38.5 inside, at
  the mortar's front face, z 18.5) and narrowing over 13.5 voxels to the chute's 6-voxel bore (x 21..27) at z 5. Its
  two walls stand from y 4 to 12, as the chute's walls do; its floor is level with the chute's (top at y 5); it is open
  on top, and its back is the mortar's front wall itself, from the floor up to the screen's lower edge, into which its
  walls and floor run on. So the pulp splashing out through the screen falls straight into it and runs out along its
  floor into the stub, with no apron or launder between.
- **The stub**, the game's straight chute turned to `ns`, 5 voxels long from the funnel to the front face, its profile
  at the face a vanilla chute's in the output cell, so a player's chute line carries on from it.
- **An oak block** under the joint carries both.

The **infeed chute section** is the game's straight chute turned to `ud`, a whole block of it (y 48..64) in the infeed
cell, hung between the chute tie and the back tie: ore from a player's chute line comes straight on down it and falls
out of its end into the plank **feed box**: a floor falling 50° onto the shoot's top, two sides and a back board hung
on the back bearer.

**The water line** is Pipes and Power Expanded's straight pipe (`ppex:pipe-straight-*`) in copper or lead, the metals
`UnifiedPipes` adds to ppex's pipes, drawn as `UnifiedPipes` draws them, soldered, and built by the same module as the
rosser's drip pipes (`Pipes/tools/solderedpipe.py`): ppex's tube 6 voxels across round its 4 × 4 bore, a hub where two
lengths meet. From the inlet on the west face (on the axis of the ppex pipe beyond it, `water`) it runs east between the
west end frame's middle and back posts, turns north beside the feed box, and drops behind the stamps' guides, open at
its end over the west end of the feed opening: the water goes into the mortar with the ore. Its runs are each shorter
than a block, so it has no seams and no joints but its two hubs; the inlet has none at the face (the pipe beyond brings
its own half joint). The rig has a part per metal, `pipecopper` and `pipelead`, the same elements in each metal's
texture; a renderer draws the fitted metal's.

The **camshaft** (y 34, z 18.55, north of the stems) runs in pillow blocks on the end frames' cam rails and carries ten
involute **cams**, a double cam beside each stem, lifting its tappet's north edge; and at its west end, outside the
west end frame, the oak **bull pulley** (radius 9.4), belted from the line shaft's pulley (4.7): two axle turns a
camshaft turn. An involute of a base circle lifts a flat follower at a constant rate (3.5 voxels a radian of the
camshaft) along a fixed line: each stamp rises 4 voxels, is let go as the arm's tip passes the tappet's edge, is thrown
a little higher, falls freely onto its die and rests there till the cam's other arm comes round: two drops a stamp a
camshaft turn, ten a turn in the order 1-3-5-2-4.

The stamps run continuously with the camshaft, on the axle's travel ψ. Their lift is not a sinusoid (a rise at the
cam's rate, a free fall, a rest), so each stamp is drawn as a sum of ten `slide`s, the harmonics of that lift through
its period (half a camshaft turn), smoothed by a Gaussian of 0.06 radians (the cam's pick-up and the shoe's landing on
the ore bed) so the series rests flat: between drops a shoe stands at most 0.11 voxels over its die, and through the
lift a tappet rides at most 0.08 over its cam and never in it. The cams' faces are drawn a hair (0.02) inside the true
involute for that.

**Pace** (ψ the axle's travel):

| Shaft | Turns a radian of ψ | Through |
|---|---|---|
| Line shaft and its pulley | −1 | the rectifier, 1:1 |
| Camshaft and bull pulley | −0.5 | the belt, 4.7 to 9.4 |

Every moving part (θ the axle's angle, ψ its travel):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft (`entry`) | The vanilla axle | The rectifier in the gear case | `rotate` x 1 (θ) |
| Line shaft (`line`) | The rectifier | Its pulley | `rotate` x −1 (ψ) |
| Line pulley (`linepulley`) [camshaft] | Keyed to the line shaft | The bull belt | `rotate` x −1 (ψ) |
| Camshaft, cams, hubs, bull pulley (`camshaft`) [camshaft] | The bull belt | The tappets | `rotate` x −0.5 (ψ) |
| Pillow blocks, bull belt (`camframe`) [camshaft] | Static | — | none |
| Stamps 1 to 5: shoe, head, stem, tappet (`stamp1`..`stamp5`) [stamps] | Its double cam | The ore on its die | ten `slide` y, harmonics of the camshaft's turn (ψ) |
| Mortar, dies, screen, shoot, infeed section, output funnel, stub and block (`mortar`) [mortar] | Static | — | none |
| Feed box (`feedbox`) [feedbox] | Static | — | none |
| Water line, copper (`pipecopper`) [pipecopper] or lead (`pipelead`) [pipelead] | Static, one metal drawn | — | none |
| Frame, gear case, pedestals, guides, bearers (`frame`) | Static | — | none |

There are no toothed wheels in sight: the rectifier's gears are in the closed case.

### Regenerating

```sh
python3 mods-src/seraphhorizons/StampBattery/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/StampBattery/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/StampBattery/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about half a minute, most of it the swept paths and the z-fighting fix; two runs are byte-identical.
`tools/validate_stampbattery.py` holds the checks; every run checks its output and exits non-zero if one fails. Checks
that set parts against each other run per build state: the frame; level 1.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; the
  `requires` are the one level's set and the water line's two metals.
- **Nothing floats:** the frame one piece from the ground, and each build state at rest.
- **Containment:** nothing leaves the 3 × 4 × 3 box over nine poses.
- **Anchors:** the entry shaft meets the power face at the cell's centre; every anchor's point on its cell's face, at
  the thing it names (the water line's inlet, the infeed chute's mouth with its end over the feed box and its bore
  inside the box's sides and over its floor, the output chute's mouth flush with the front face).
- **Chutes:** `VANILLA_CHUTE` is the game's straight chute (from, to, UVs, rotations, drawn faces, texture), read from
  `$VINTAGE_STORY` or `~/Games/vintagestory` when the game is there; at the infeed face the section's profile is a
  vanilla `ud` chute's in that cell, at the output face an `ns` chute's, each flush with its face, in the chute's texture.
- **Paths:** the feed box's floor falls at least 35° onto the shoot; the shoot at least 45° from under the feed box
  floor's lower end to the feed lip; the funnel's mouth spans the whole screen at the mortar's front face, under the
  screen, and narrows to the stub's bore where they meet; its floor is level with the stub's and reaches the mortar's
  front; the water line's drop is open over the feed opening.
- **Outlet clear:** at rest with everything fitted, no element of any part stands in the funnel's space (from its floor
  up to the screen's top, from the mortar's front to the stub) or in the stub's bore: the pulp's path out is open.
- **Textures** by role, every element ruled.
- **Drive:** the line shaft turns one way for either sign of the axle; at the same travel every part but the entry shaft
  stands alike for either sign of the axle; the bull belt's two rims move alike and its runs are tangent to both rims.
- **Stamps:** over 60 poses in the lifts every tappet rides its cam (never into it, at most 0.15 over); resting, each
  shoe within 0.12 of its die and never into it; two drops a stamp a camshaft turn, in the order 1-3-5-2-4.
- **Supports:** the entry shaft in the gear case, the line shaft in the case's boss and both pedestals, the camshaft in
  its two pillow blocks.
- **Clearances:** over 42 poses (each build state at rest and the battery every 4.5° of a half turn) no two parts touch
  except the intended contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the battery through a whole camshaft turn every 0.5°, everything fitted: 720 poses.
- **No z-fighting** (full runs): no coplanar overlapping face pairs at five poses of the two build states.
- **Files:** every texture declared; every cell of the box present, a lid on every column, every box inside its cell;
  the shipped model is the checked one moved.

`tools/tests/test_stampbattery_model.py` and `site/test/stampbattery.test.ts` hold the written files to these rules
without the generator's state, and replay the reference poses.

### Rig schema (`stampbattery-rig.json`)

The draw bench's schema (`../DrawBench/README.md`, "Rig schema") without a work: nothing is added to the shared rig
maths. Rotations on θ and ψ, slides on ψ.

- **Cells:** each cell's `boxes` are rebuilt from the shipped, rounded shape posed at rest, the finished machine's (the
  frame, level 1 and the copper water line); every column's top cell carries a `lid`, so the top is one deck, where the
  player stands to feed by hand at the infeed's mouth.
- **`stamps`:** `turnsPerRev` (2, axle turns a camshaft turn as drawn), `dropsPerRev` (2, a stamp), `firingOrder`
  ([1, 3, 5, 2, 4]) and `lift` (0.25 blocks).
- **`levels`:** `{ "1": [mortar, camshaft, stamps, feedbox] }`, the machine's one level.
- **The water line's metal:** `pipecopper` or `pipelead`, one of them fitted with the level (the rosser's pattern).
- **Keys.** `cells`, `powerCell`/`powerFace`, `waterCell`/`waterFace`/`water`, `infeedCell`/`infeedFace`/`infeed`,
  `outputCell`/`outputFace`/`output`, `stamps`, `levels` and `parts`.

**In the viewer** (`site/models.json`, `stamp-battery`): a **Level** select (the scenario's `states`): the frame and
level 1 (the default), each with a note; and a **Water line** select, copper (the default) or lead, which waits for the
mortar. Play turns the axle at 1.5 turns a second, a camshaft turn in 1.3 seconds (about 90 drops a minute a stamp, a
period battery's pace).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `line_*`, `linepulley*`,
`camshaft_*` (the shaft, hubs, cams `camshaft_cam<stamp><a|b><n>` and the bull pulley `camshaft_bull_*`),
`camframe_*`, `stamp1_*`..`stamp5_*`, `mortar_*` (the chute sections `mortar_chutein_*` and `mortar_chuteout_*`, the
funnel `mortar_funnel_*`, its block `mortar_chuteleg`, the shoot `mortar_shoot*`), `feedbox_*`, `pipecopper_*` and
`pipelead_*` (solderedpipe's box names: `inlet_*`, `hub1_*`, `north_*`, `hub2_*`, `drop_*`), and `fr_*` for the frame.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Choices made, and why

- **Salvaged from v2, unchanged in kind:** the frame's end frames and guide girts, the closed rectifier gear case and
  the line shaft, the belt drive and bull pulley, the mortar with its dies and screen, the camshaft with its involute
  cams, the five stamps with their harmonic drivers, the soldered water line and the vanilla chute section, and most of
  the checks (stamps, belt, rectifier, reverse, chute, textures, supports, clearances, z-fighting). Everything moved 8
  voxels back into the middle row.
- **New:** the three-row layout and its frame (a back post and the two bearers, the caps over all three rows), the
  output funnel and stub in place of v2's plank spout, the shoot, the infeed's section a whole block long over the
  feed, the plank feed box, and the path and outlet checks.
- **One level, chute in and chute out** (owner's review): the Challenge feeder of the first v3 pass is gone with
  everything that existed only for it (the tray, the hopper, the friction grip, the lever, the bumper rod, the feeding
  tappet's lug, their checks and level 2), and the plank feed box is the machine's feed.
- **The funnel instead of an apron and a launder** (owner's review): the output chute itself widens to the whole screen,
  so the pulp falls from the screen straight into it. The funnel is flat-floored, as the game's chute is, and flared in
  plan only; its back is the mortar's front wall, so it needs no back plate of its own. The output anchor stays where it
  was (`[0, 0, 0]`, north), and the stub keeps the game's chute's profile at the face.
- **Belts, not gears**, as v2: a battery's camshaft was driven by a belt from the line shaft to its bull wheel.
- **The rectifier is enclosed**, as v2: the cams lift only one way round; a drawn rectifier would be about ninety
  elements of gearing.
- **The water line drops into the feed opening**, beside the shoot.
- **The infeed chute is a whole block**, so the ore drops into the feed box, not from three voxels under the top face
  across half the machine's height.

## Known weak spots, and what is not checked

The model has not been looked at in a client yet.

1. **The stamps' lift is a smoothed series.** A shoe hovers up to 0.11 voxels over its die between drops, and the free
   fall is slightly rounded where it lands. Stamps do not turn on their stems as real ones do.
2. **Belts are drawn still.** A belt looks alike as it runs, so its runs and wraps are frame-like pieces.
3. **Box faces and chords.** Cam faces are four chords of the involute; round parts are hexagons, octagons or 12-gons.
4. **The funnel's walls are angled boxes.** Their ends run into the stub's walls and the mortar's front wall; where a
   wall meets the stub its outer face stands up to 0.3 voxels outside the stub's for its last half voxel, and its top
   is 0.02 under the stub's (so the stub keeps the game's profile exactly and nothing z-fights).
5. **No ore or water is drawn.**
6. **The rectifier's gears are not drawn** (the closed gear case).

## Open for the owner

1. **Build stages**: the frame recipe, the stages and their materials, measured from the model and settled with the
   owner (#729's Open), as the crucible furnace's were. The `requires` ids only split the machine where a stage could.
2. **Water "into the feed box"** (#728): the line drops into the feed opening beside the shoot, under the feed box's
   lower end, so the water meets the ore there. Rose's batteries had a horizontal pipe along the mortar's top with a
   cock at each stamp (Rose 1898, p. 123); that would be a ppex pipe with five outlets, which `UnifiedPipes` does not
   draw.
3. **Hand feeding at the infeed's mouth**, on the top face, 4 blocks up: the player stands on the deck (every column has
   a lid) to reach it. Gameplay could also take a click on the feed box's cells from behind.
4. **Anchors provisional pending #711**: the chute sections (the v2 branches' `VANILLA_CHUTE`, checked against the
   game's chute), the water anchor (a cell face with the soldered pipe's inlet, the rosser's pattern, no reservoir
   drawn) and hand feeding at the infeed.
5. **Size**: four high (accepted on #729) against a real battery's six; the stamps 5.8 voxels apart (about 36 cm) with
   a 4 voxel (25 cm) lift.
6. **The front row is mostly air above the funnel**: the end frames start at z 9 behind it, so a player cannot build in
   front of the camshaft. Bringing the front posts out to the front face would box the funnel in.
7. **Two bearers with little left to carry**: with the hopper gone the front bearer only holds the water line's strap
   and the back one the feed box's back board. They stay because they tie the end frames together below the caps; they
   could go if the owner prefers a plainer frame.
