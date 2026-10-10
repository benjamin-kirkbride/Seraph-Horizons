# Stamp mill

Part of the Seraph Horizons mod (`../README.md`): the crush-and-classify machine of the ore line (epic #684,
behaviour #728, model #729). One machine **upgraded in place** (#711): a timber battery frame whose footprint is its
mill level's from the start, and the parts each level fits into it. A level adds parts and takes none off.

| Level | Tier | Fitted (`requires`) | What it adds |
|---|---|---|---|
| (frame) | | none | The battery's end frames, guide girts and ties; the gear case at the power cell and the line shaft; the water inlet's flange |
| 1 | water | `mortar`, `camshaft`, `stamps` | A five-stamp battery, wet: the mortar with its mesh discharge screen, apron and launder; the feed box the player shovels ore into; the water pipe; the camshaft and its belt drive; five stamps |
| 2 | mill | adds `jaw`, `grizzly` | A Blake jaw rock breaker behind the battery, on its own stand, belted from the same line shaft; a grizzly over its mouth, chute-fed from the top |

**Status: the model only.** This folder holds the generator (`tools/`); there is no block, block entity or renderer
yet (#728, #711). Paths here are from this folder unless they start with `assets/` or `tests/` (the mod's), or
`Machines/`, this folder's neighbour, whose `tools/machinegen/` is unchanged by this machine (no new driver).

It replaces the first pass's "crusher" (#729's old branch). From that model it reuses the stamp battery (the mortar
with its screen, the involute cams beside the stems, the stamps and their firing order) and the Blake jaw crusher
with its exactly solved linkage; the frame, the power train, the rolls tier, the clutch and its hand lever are gone.

## The machine

**Footprint.** 48 cells, 3 wide (x), 4 high (y), 4 deep (z), fixed for every level. In the shipped frame the
controller is `[0,0,0]`, the bottom middle cell of the north face, over the launder's spout: the cells run x −1..1,
y 0..3, z 0..3. The battery fills the north half (z 0..1), the breaker the south half (z 2..3). The shafts run along x;
the ore runs north, from the breaker (south) into the battery and out of its screen (north).

**Anchors** (`assets/seraphhorizons/config/stampmill-rig.json`, shipped coordinates), the same at every level:

| Anchor | Cell, face | Point | What comes or goes there |
|---|---|---|---|
| `powerCell`, `powerFace` | `[1, 0, 2]`, east | the face's centre | The vanilla axle, into the gear case |
| `waterCell`, `waterFace`, `water` | `[1, 1, 1]`, east | `[2.0, 1.5, 1.5]`, the flange | Process water, by ppex pipe, down into the feed box (level 1 on) |
| `feedCell`, `feedFace`, `feed` | `[0, 1, 1]`, up | `[0.5, 2.0, 1.5]`, over the feed box | Ore the player shovels in (level 1); at level 2 the grizzly and the breaker feed it |
| `infeedCell`, `infeedFace`, `infeed` | `[0, 3, 1]`, up | `[0.5, 4.0, 1.3125]`, over the grizzly's head | Ore by chute from above (level 2) |
| `outputCell`, `outputFace`, `output` | `[0, 0, 0]`, north | `[0.5, 0.2875, 0.0]`, the spout's lip | The crushed, classified ore (pulp) out of the launder |

**Power.** The vanilla axle comes in on the east face of `[1, 0, 2]` into a cast-iron **gear case** holding a
rectifier, as the draw bench's and the gear cutter's, so the **line shaft** coming out of its west side turns one way
whichever way the axle turns, at the axle's pace. The case is closed: its gears are not drawn. The line shaft runs west
along the back of the machine at the power cell's height, in the case's boss and two pedestals. At its west end a pulley
belts the **bull pulley** on the camshaft (level 1); near its east end a second pulley belts the breaker's
pulley-flywheel (level 2). Both belts are open, so every shaft turns the line shaft's way.

## Model

Everything was made for this mod: no other mod's model is used, so the generator needs nothing from `build/mods`.
Textures by role: debarked oak (`game:block/wood/debarked/oak`) for the frame's timbers, the breaker's stand, the entry
shaft (the vanilla axle's continuation) and the bull pulley's rim and arms; oak planks for the launder and the feed box;
iron plate for castings (the mortar, the gear case, the pillow blocks, the cams, the breaker's frame, the grizzly);
plain steel sheet for shafts and wearing parts (stems, shoes, dies, the jaw's faces, toggles, rods); plain iron sheet
for the water pipe; `game:block/metal/mesh1` for the screen; plain leather for the belts; black leather for the
breaker's india-rubber buffers. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/stampmill.json` | The whole machine, every level's parts (451 elements: 39 drawn with nothing fitted, 274 at level 1, 451 at level 2) |
| `assets/seraphhorizons/shapes/block/stampmill_frame.json` | The frame only (33 elements). A block would draw it and the inventory show it. |
| `assets/seraphhorizons/config/stampmill-rig.json` | Footprint (cells with the frame's boxes and each level's fitted boxes), anchors, the battery's and the breaker's figures, the levels and the part rig (23 parts) |
| `tests/StampMill/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

### How the machine works

Positions here are in the generator's **build frame**: native axes in voxels from the machine box's north-west bottom
corner, x 0..48, y 0..64, z 0..64. The shipped files are the build frame moved one block west.

**The frame.** Two oak end frames (west x 3.5..7.5, east x 40.5..44.5), each a sill, a front and a back post, a cap
and three rails between the posts: the lower guide rail, the cam rail and the upper guide rail. Across them, the two
guide girts (each two strips, the stems running in the slot between them), a front tie at the caps and a back tie
under them, which carries the grizzly's head at level 2.

**Level 1, the battery.** Five stamps on a line along x (z 16, x 12.4 to 35.6, 5.8 apart), each a steel shoe, an
iron head, a steel stem through the two guide girts and a square iron tappet. They stand in a cast-iron **mortar
box** (x 8..40, z 10.5..21.5, its rim at y 16.5, low enough that the heads show over it) on an oak mortar block, each
over a steel die. The **screen** (mesh) is in the front wall over the dies; the crushed ore splashes through it onto
the iron **apron** and runs down into the plank **launder** along the front, whose spout leaves through the north
face. At the back the **feed box**: a plank floor falling 40° from under the breaker's discharge to the feed lip in the
mortar's back wall, with low sides; the player shovels ore into it, and the **water pipe** from the east face's
flange runs over its east side and turns down into it, so the ore goes into the mortar wet.

The **camshaft** (y 34, z 10.5, north of the stems) runs in pillow blocks on the end frames' cam rails and carries ten
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
involute for that. No new driver was needed.

**Level 2, the breaker and the grizzly.** The Blake jaw crusher is the first pass's, in its own frame (the flow from the
back wall to the fixed jaw), mirrored so its fixed jaw faces the battery and raised onto an oak **stand** (two sills on
four posts) behind the battery. Between two cast-iron cheeks: the steel **fixed jaw** at the north and the **swinging
jaw** hung from a hinge pin at its top; the **eccentric shaft** in the cheeks' bearings works the **pitman**, whose foot
straightens the **back toggle** (seated on the back wall) and the **front toggle** (on the swinging jaw's back), pushing
the jaw's lower end towards the fixed jaw; two **tension rods** from the jaw's lugs through the back wall, with
india-rubber **buffers** under their nuts, pull it back. The jaws' gap at the bottom runs 2.67..3.58 voxels. A flywheel
at each end of the eccentric shaft; the east one is the belt pulley (radius 8), belted from the line shaft's second
pulley (6): the eccentric turns three quarters of the axle's pace. Its product falls out of the gap onto the feed
box's floor, which is the chute down into the mortar.

The **grizzly**: five iron bars falling 42° from a head block on the back tie (under the infeed) to their foot over the
breaker's mouth, where they rest on a cross bar on two brackets on the cheeks. Ore from the chute lands on the head;
the fines fall between the bars into the feed box, and the oversize slides off the bars' foot into the breaker. Under
the bars, over the breaker's front wall, a **throat plate** falling 40° north sheds the fines that land there past the
wall's face into the feed box.

The linkage is solved exactly (Newton's method, 128 angles of the eccentric) and each body drawn at its mean pose; the
rig moves it by up to four harmonics of its exact motion in the eccentric's angle, mirrored into the build frame (a
turn about x and a slide along z change sign). A check holds every pin of the posed rig to the exact linkage through a
turn, within 1e-4 voxels.

**Pace** (ψ the axle's travel):

| Shaft | Turns a radian of ψ | Through |
|---|---|---|
| Line shaft and its two pulleys | −1 | the rectifier, 1:1 |
| Camshaft and bull pulley | −0.5 | the belt, 4.7 to 9.4 |
| Eccentric shaft and flywheels | −0.75 | the belt, 6 to 8 |

Every moving part (θ the axle's angle, ψ its travel):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft (`entry`) | The vanilla axle | The rectifier in the gear case | `rotate` x 1 (θ) |
| Line shaft (`line`) | The rectifier | Its pulleys | `rotate` x −1 (ψ) |
| Line pulley for the battery (`linepulley`) [camshaft] | Keyed to the line shaft | The bull belt | `rotate` x −1 (ψ) |
| Camshaft, cams, hubs, bull pulley (`camshaft`) [camshaft] | The bull belt | The tappets | `rotate` x −0.5 (ψ) |
| Pillow blocks, bull belt (`camframe`) [camshaft] | Static | — | none |
| Stamps 1 to 5: shoe, head, stem, tappet (`stamp1`..`stamp5`) [stamps] | Its double cam | The ore on its die | ten `slide` y, harmonics of the camshaft's turn (ψ) |
| Mortar, dies, screen, apron, launder, feed box, water pipe (`mortar`) [mortar] | Static | — | none |
| Line pulley for the breaker (`jawpulley`) [jaw] | Keyed to the line shaft | The breaker's belt | `rotate` x −1 (ψ) |
| Eccentric shaft, eccentric, both flywheels (`eshaft`) [jaw] | The breaker's belt | The pitman | `rotate` x −0.75 (ψ) |
| Pitman (`pitman`) [jaw] | The eccentric | The toggles' inner ends | `swing` x ×4, `slide` y ×4, `slide` z ×4 (ψ) |
| Back toggle (`backtoggle`) [jaw] | The pitman's foot | — (its seat on the back wall) | `swing` x ×4 about its seat |
| Front toggle (`fronttoggle`) [jaw] | The pitman's foot | The swinging jaw | `swing` x ×4, `slide` y ×4, `slide` z ×4 |
| Swinging jaw (`swingjaw`) [jaw] | The front toggle | The ore | `swing` x ×4 about its hinge pin |
| Tension rods, nuts, pins (`rods`) [jaw] | The swinging jaw | The buffers | `slide` y ×4, `slide` z ×4 |
| Rubber buffers (`buffer1`..`buffer2`) [jaw] | The nuts | — | `slide` z ×4, each its share of the nuts' travel |
| Breaker's cheeks, walls, fixed jaw, hinge pin, stand, belt (`jawframe`) [jaw] | Static | — | none |
| Grizzly bars, head, cross bar, brackets, throat plate (`grizzly`) [grizzly] | Static | — | none |
| Frame, gear case, pedestals, guides, water flange (`frame`) | Static | — | none |

There are no gears outside the closed gear case, so nothing toothed meshes with nothing.

### Regenerating

```sh
python3 mods-src/seraphhorizons/StampMill/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/StampMill/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/StampMill/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about a minute, most of it the swept paths and the z-fighting fix; two runs are byte-identical.
`tools/validate_stampmill.py` holds the checks; every run checks its output and exits non-zero if one fails. Checks
that set parts against each other run per build state: the frame; level 1; level 2 (everything).

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; the
  `requires` are the levels' sets, level 2 keeping level 1's.
- **Nothing floats:** the frame one piece from the ground, and each build state at rest.
- **Containment:** nothing leaves the 3 × 4 × 4 box over nine poses.
- **Anchors:** the entry shaft meets the power face at the cell's centre; every anchor's point on its cell's face, at
  the thing it names (the flange, the feed box, the grizzly's head, the spout).
- **Paths:** the grizzly's bars fall at least 40° and stand at least 1 apart, their foot over the breaker's mouth; the
  throat plate falls at least 38° and ends past the front wall's face; the breaker's discharge falls on the feed
  box's floor, which falls at least 35° to the feed lip; the apron starts under the screen and ends over the launder.
- **Textures** by role, every element ruled.
- **Drive:** the line shaft turns one way for either sign of the axle; each belt's two rims move alike and its runs are
  tangent to both rims.
- **Stamps:** over 60 poses in the lifts every tappet rides its cam (never into it, at most 0.15 over); resting, each
  shoe within 0.12 of its die and never into it; two drops a stamp a camshaft turn, in the order 1-3-5-2-4.
- **Breaker:** through a turn of the eccentric (72 steps) the pitman's strap on the eccentric, its foot on both
  toggles' ends and where the solved linkage puts it, the back toggle on its seat, the front toggle on the jaw's seat,
  the rods on the jaw's lugs, all within 1e-3 (in practice 4e-5); the toggles never pass straight; the gap at the
  bottom never under 1.
- **Supports:** the entry shaft in the gear case, the line shaft in the case's boss and both pedestals, the camshaft in
  its two pillow blocks, the eccentric shaft in the cheeks' two bearings, the hinge pin in its two bearings.
- **Clearances:** over 67 poses (each build state at rest, the battery every 4.5° of a half turn, the breaker every 15°
  of the eccentric) no two parts touch except the intended contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the battery through a whole camshaft turn every 0.5°, and the breaker every 2° of the
  eccentric, everything fitted: 900 poses.
- **No z-fighting** (full runs): no coplanar overlapping faces at five poses of the three build states.
- **Files:** every texture declared; lids over the battery's columns only; every fitted key a `requires` value and
  every box inside its cell; the shipped model is the checked one moved.

`tools/tests/test_stampmill_model.py` and `site/test/stampmill.test.ts` hold the written files to these rules without
the generator's state, and replay the reference poses.

### Rig schema (`stampmill-rig.json`)

The draw bench's schema (`../DrawBench/README.md`, "Rig schema") without a work: nothing is added to the shared rig
maths. Rotations on θ and ψ, swings and slides on ψ.

- **Cells** (`docs/recipe-browser/models.md`, "Collision by what is fitted"): each cell's `boxes` are the frame's,
  there at every level; its `fitted` boxes, by `requires` value, are there only while that value is fitted. Both are
  rebuilt from the shipped, rounded shape posed at rest. A cell with neither is `hollow`. The battery's top cells carry a
  `lid` (a deck); the breaker's do not. The game's rig loader does not read `fitted` yet.
- **`stamps`:** `turnsPerRev` (2, axle turns a camshaft turn as drawn), `dropsPerRev` (2, a stamp), `firingOrder`
  ([1, 3, 5, 2, 4]) and `lift` (0.25 blocks).
- **`jaw`:** `turnsPerStroke` (1.333, axle turns a turn of the eccentric).
- **`levels`:** `{ "1": [mortar, camshaft, stamps], "2": [those, jaw, grizzly] }`.
- **Keys.** `cells`, `powerCell`/`powerFace`, `waterCell`/`waterFace`/`water`, `feedCell`/`feedFace`/`feed`,
  `infeedCell`/`infeedFace`/`infeed`, `outputCell`/`outputFace`/`output`, `stamps`, `jaw`, `levels` and `parts`.

**In the viewer** (`site/models.json`, `stamp-mill`): a **Level** select (the scenario's `states`): the frame, level
1 and level 2 (the default), each with a note. Play turns the axle at 1.5 turns a second, a camshaft turn in 1.3
seconds (about 90 drops a minute a stamp, a period battery's pace).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `line_*`, `linepulley*`,
`camshaft_*` (the shaft, hubs, cams `camshaft_cam<stamp><a|b><n>` and the bull pulley `camshaft_bull_*`),
`camframe_*`, `stamp1_*`..`stamp5_*`, `mortar_*`, `jawpulley*`, `eshaft_*`, `pitman_*`, `backtoggle_*`,
`fronttoggle_*`, `swingjaw_*`, `rods_*`, `buffer1_*`..`buffer2_*`, `jawframe_*`, `grizzly_*`, and `fr_*` for the frame.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Choices made, and why

- **Belts, not gears.** A stamp battery's camshaft was driven by a belt from the line shaft to a large pulley on the
  camshaft (the bull wheel), and a Blake breaker's by a belt to one of its two flywheels, which was the pulley. Belts
  also let the camshaft and the eccentric shaft sit where the machine needs them rather than on a pinion's pitch
  circle, and need far fewer elements than toothed wheels.
- **The rectifier is enclosed.** The cams lift only one way round, so the line shaft must turn one way whichever way the
  axle turns. The draw bench's rectifier (two pairs of gears on one-way catches) would be about ninety elements of
  gearing; here it is a closed cast-iron gear case at the power cell, its gears not drawn.
- **No clutch.** The camshaft is keyed to its pulley and turns whenever the axle does; the stamps run on the axle's
  travel, not on a work quantity, so they never stand still with the shaft turning.
- **The breaker is behind the battery, its fixed jaw towards it.** The ore runs north through the machine: chute,
  grizzly, breaker, feed box, mortar, screen, launder. Mirroring the old breaker puts its discharge just behind the
  feed box, so the feed box's floor is a short, steep chute (40°) into the mortar, and the same floor is where the
  player shovels ore at level 1.
- **The grizzly sorts before the breaker.** That is the period arrangement: ore is dumped on a grizzly, the fines go
  through to the bins, the oversize to the rock breaker. The oversize that the battery's screen holds back stays in
  the mortar and is stamped again: a stamp battery classifies its own product. See the first open question.
- **A low mortar.** Its rim is at the heads' tops, so the heads show as they rise and fall.
- **The stamps as harmonics.** See "How the machine works". The alternative, the old model's ramps on a work quantity,
  needs a clutch to stop the camshaft between loads.

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (the standalone build) at each level and through its motion. No
one has yet looked at it in a client.

1. **The stamps' lift is a smoothed series.** A shoe hovers up to 0.11 voxels over its die between drops, and the
   free fall is slightly rounded where it lands. Stamps do not turn on their stems as real ones do.
2. **Belts are drawn still.** A belt looks alike as it runs, so its runs and wraps are frame-like pieces; nothing moves
   along it.
3. **Box faces and chords.** Cam faces are four chords of the involute; round parts are octagons or hexagons.
4. **The buffers do not yield.** They slide with the rods' nuts, each its share; nothing compresses them.
5. **No ore or water is drawn.**
6. **The rectifier's gears are not drawn** (the closed gear case).

## Open for the owner

1. **The oversize "return".** The grizzly sends oversize to the breaker before it is broken, and the battery's screen
   keeps its own oversize in the mortar; nothing goes back up. A true loop (a screen after the breaker returning its
   oversize to the breaker's mouth) would need a bucket elevator, since the breaker's mouth is above its discharge. Is
   the grizzly enough, or should there be an elevator (an enclosed wooden leg with a head pulley belted from the
   eccentric shaft)?
2. **Water power at level 1.** The earlier audit suggested "water power"; here level 1 takes process water by pipe
   (wet stamping) and mechanical power from the axle, like every other machine.
3. **The fitting order within each level** (three `requires` at level 1, two at level 2) and the items each takes.
4. **The closed gear case** for the rectifier, rather than drawn gears.
5. **The feed at level 2.** The player can still shovel into the feed box from above, between the battery and the
   breaker; the chute's infeed is the grizzly's head on the top face. Should the feed box take ore by hand at level 2?
6. **Size.** The battery stands four blocks tall; a real five-stamp battery is about six. The stamps are 5.8 voxels
   apart (about 36 cm), with a 4 voxel (25 cm) lift.
7. **Lids.** The battery's columns have a deck at the top; the breaker's do not (a lid there would be an invisible
   floor four blocks up over an empty stand at level 1).
