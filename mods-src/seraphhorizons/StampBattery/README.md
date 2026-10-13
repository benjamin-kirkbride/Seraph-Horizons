# Stamp battery

Part of the Seraph Horizons mod (`../README.md`): the crush-and-classify machine of the ore line (epic #684,
behaviour #728, model #729). A **wet five-stamp gravity battery** with its mesh discharge screen, and at level 2 the
**Challenge self-feeder**. One machine is one piece of equipment (#711): a timber battery frame whose footprint is
fixed from the frame on, and the parts each level fits into it.

| Level | Fitted (`requires`) | What it is |
|---|---|---|
| (frame) | none | Two oak end frames, the guide girts, the ties and the hopper's two bearers; the rectifier's gear case at the power cell and the line shaft along the back row |
| 1 | `mortar`, `camshaft`, `stamps`, `feedbox`, and `pipecopper` or `pipelead` | The battery: the mortar with its dies, screen, apron, launder, shoot and both chute sections; the camshaft and its belt drive; five stamps; the plank feed box under the infeed; the water line to the feed opening |
| 2 | `mortar`, `camshaft`, `stamps`, `feeder` (and the water line) | The Challenge feeder **in the feed box's place**: fitting it takes the feed box off |

**Status: the model only.** This folder holds the generator (`tools/`); there is no block, block entity or renderer
yet (#728, #711). Paths here are from this folder unless they start with `assets/` or `tests/` (the mod's), or
`Machines/`, this folder's neighbour, whose `tools/machinegen/` is unchanged by this machine (no new driver).

It is the battery half of the stamp mill's v2 model (`729-stampmill-model-v2`), re-laid out three deep, with the
Challenge feeder added as level 2. The v2 model's jaw breaker and grizzly are gone: the jaw breaker is its own machine
(#912, model #914).

## The machine

**Footprint.** 36 cells, 3 wide (x), 4 high (y), 3 deep (z), fixed for every level, no hollow cell. In the shipped
frame the controller is `[0,0,0]`, the bottom middle cell of the north face, where the output chute leaves: the cells
run x −1..1, y 0..3, z 0..2. Placed like the crucible furnace, it extends away from the player: the north face (the
screen, the apron, the launder and the output) faces them, and the feed and the power are behind. The shafts run along
x; the ore runs north, from the infeed at the back through the mortar to the output at the front.

| Row (z) | Holds |
|---|---|
| Front (0) | The apron, the launder on its two legs, and the output chute section flush with the front face |
| Middle (1) | The mortar on its block, the five stamps in their guides, the camshaft with its cams and bull pulley |
| Back (2) | The feed box (level 1) or the Challenge feeder (level 2) under the infeed chute, the line shaft and its pedestals, the rectifier's gear case |

**Three deep, not two** (#729 asks for the decision). Two deep would need the feeder in the middle row's back, behind
the mortar. The mortar is 11 voxels deep with the stems on its middle line (z 24 of the middle row's 16..32), so from
the back wall to the back face there are 2.5 voxels: no room for a tray at all, let alone one with its hopper, the
pinion under it and the bumper rod beside it (the tray here is 12 voxels across). Moving the mortar forward into the front row instead would push the apron and launder out of the footprint.
The back row also carries what the battery needs behind the stamps whatever the level: the line shaft (on the
power cell's axis, which must be a cell's centre) and the gear case, the bull belt's lower pulley, and the infeed chute
section, a block of the game's chute standing over the feed. So: 3 × 4 × 3, as #729 proposes.

**Anchors** (`assets/seraphhorizons/config/stampbattery-rig.json`, shipped coordinates), the same at every level:

| Anchor | Cell, face | Point | What comes or goes there |
|---|---|---|---|
| `powerCell`, `powerFace` | `[1, 0, 2]`, east | the face's centre | The vanilla axle, into the gear case |
| `waterCell`, `waterFace`, `water` | `[-1, 2, 2]`, west | `[-1.0, 2.5, 2.5]`, the water line's inlet | Process water, from a ppex pipe on the west face, down into the feed opening (level 1 on) |
| `infeedCell`, `infeedFace`, `infeed` | `[0, 3, 2]`, up | `[0.5, 4.0, 2.5]`, the infeed chute's mouth | Ore by the game's chute from above, at both levels: a vanilla `chute-straight-ud` placed on this face carries straight on down the machine's own section into the feed box (level 1) or the feeder's hopper (level 2). **Hand feeding goes in at the same mouth** (#728, #711), the player standing on the deck |
| `outputCell`, `outputFace`, `output` | `[0, 0, 0]`, north | `[0.5, 0.5, 0.0]`, the output chute's mouth | The crushed, classified ore (pulp) off the launder: a vanilla `chute-straight-ns` placed against this face takes it on |

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
| The feeder (bumper rod, lever, grip, pinion, tray) | As forwards: they follow stamp 3's drop, a function of ψ |

A check holds this: at the same ψ, every part but the entry shaft stands alike for θ and −θ.

## Model

Everything was made for this mod: no other mod's model is used, so the generator needs nothing from `build/mods`.
Textures by role: debarked oak (`game:block/wood/debarked/oak`) for the frame's timbers, the entry shaft (the vanilla
axle's continuation), the mortar block, the launder's legs and the bull pulley's rim and arms; oak planks for the
launder and the feed box; iron plate for castings (the mortar, the shoot, the gear case, the pillow blocks, the cams,
the feeder's straps, brackets, tray, pinion, drum, lever and rod fittings); the game's riveted sheet iron
(`game:block/metal/sheet/iron1`) for the feeder's sheet-iron hopper; plain steel sheet for shafts and wearing parts
(stems, shoes, dies, the tray's spindle, the pinion's shaft, the lever's pin, the bumper rod); the water line in
`game:block/metal/sheet-plain/copper4` or `lead4` (with `game:block/metal/ingot/leadsolder` declared for its joints);
`game:block/metal/mesh1` for the screen; the game's chute (`game:block/metal/sheet/copper1`) for both chute sections;
plain leather for the belt. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/stampbattery.json` | The whole machine, every level's parts and the water line in both metals (418 elements; drawn: 40 with nothing fitted, 303 at level 1, 388 at level 2) |
| `assets/seraphhorizons/shapes/block/stampbattery_frame.json` | The frame only (34 elements). A block would draw it and the inventory show it. |
| `assets/seraphhorizons/config/stampbattery-rig.json` | Footprint (cells with boxes and lids), anchors, the stamps' and the feeder's figures, the levels and the part rig (21 parts) |
| `tests/StampBattery/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths |

### How the machine works

Positions here are in the generator's **build frame**: native axes in voxels from the machine box's north-west bottom
corner, x 0..48, y 0..64, z 0..48. The shipped files are the build frame moved one block west.

**The frame.** Two oak end frames (west x 3.5..7.5, east x 40.5..44.5), each a sill under a front post (z 11..15) and a
middle post (z 30..34, behind the mortar), a back post (z 43.6..47.6) standing on the ground at the back face, a cap over
all three, and three rails between the front and middle posts: the lower guide rail, the cam rail and the upper guide
rail. Across them: the two guide girts (each two strips, the stems running in the slot between them), the front tie at
the caps, the chute tie and the back tie at the caps either side of the infeed chute's section, and the two bearers
the hopper hangs between (y 44..48), one between the middle posts and one between the back posts. An iron strap from
the front bearer holds the water line. The gear case stands east of the back row, clear of the east end frame's back
post.

**Level 1, the battery.** Five stamps on a line along x (z 24, x 12.4 to 35.6, 5.8 apart), each a steel shoe, an
iron head, a steel stem through the two guide girts and a square iron tappet. They stand in a cast-iron **mortar box**
(x 8..40, z 18.5..29.5, its rim at y 16.5, low enough that the heads show over it) on an oak mortar block, each over a
steel die. The **screen** (mesh) is in the front wall over the dies; the crushed ore splashes through it onto the iron
**apron** and runs down into the plank **launder** along the front, whose floor stands a little over the bore of the
**output chute section** in its front board's middle: the game's straight chute, turned to `ns`, 6 voxels long from the
launder to the front face. At the back, the **feed opening** over the lip in the mortar's back wall and the iron
**shoot** rising from the lip under the feed: the ore slides down it into the mortar.

The **infeed chute section** is the game's straight chute turned to `ud`, a whole block of it (y 48..64) in the infeed
cell, hung between the chute tie and the back tie: ore from a player's chute line comes straight on down it and falls
out of its end into the feed box or the hopper below. At level 1 the plank **feed box** catches it: a floor falling 50°
onto the shoot's top, two sides and a back board hung on the back bearer.

**The water line** is Pipes and Power Expanded's straight pipe (`ppex:pipe-straight-*`) in copper or lead, the metals
`UnifiedPipes` adds to ppex's pipes, drawn as `UnifiedPipes` draws them, soldered, and built by the same module as the
rosser's drip pipes (`Pipes/tools/solderedpipe.py`): ppex's tube 6 voxels across round its 4 × 4 bore, a hub where two
lengths meet. From the inlet on the west face (on the axis of the ppex pipe beyond it, `water`) it runs east between the
west end frame's middle and back posts, turns north beside the feed box (or the hopper), and drops behind the stamps'
guides, open at its end over the west end of the feed opening: the water goes into the mortar with the ore at either
level. Its runs are each shorter than a block, so it has no seams and no joints but its two hubs; the inlet has none at
the face (the pipe beyond brings its own half joint). The rig has a part per metal, `pipecopper` and `pipelead`, the
same elements in each metal's texture; a renderer draws the fitted metal's. It is level 1's and stays at level 2.

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

**Level 2, the Challenge feeder.** Hendy's Challenge feeder, "the best machine devised, and the most largely used"
(Rose 1898, p. 121), as Rose describes it (pp. 122–123): "the tray, A, below the sheet-iron hopper, B, is revolved in a
horizontal plane by means of a gear-wheel below it ... and this gears with teeth set in the bottom of the tray ... The
gear-wheel is set in motion by a friction grip, D, placed on the outside of the frame, and actuated through the lever,
E, by the bumper-rod, G, against which the tappet of the stamp strikes. At each partial rotation a given quantity of ore
is scraped off by the stationary wings or side plates, H, resting on the tray." Every feeder of the kind is "a
pyramidal hopper with inclined floor, connected with the feed-opening of the mortar-box by a spout or shoot" (p. 121),
and the feeding tappet is "keyed" "near the top of a stamp-stem (usually the middle one in the battery)" (p. 122). The
model, its parts by Rose's letters:

- **B, the hopper**: sheet iron, its mouth (x 17..31, z 34..43.6) under the infeed chute's end at y 48, hung between
  the two bearers, its floor inclined 61° down and forward to a throat over the tray's middle.
- **A, the tray**: a cast-iron plate 12 across (y 22..23) on a vertical steel spindle behind stamp 3, the spindle
  standing in a step bearing on a bracket slung under the hopper by two straps; sixteen teeth set in its bottom.
- **The gear-wheel**: a six-tooth pinion under the tray's east side, meshing those teeth (the tray turns 6/16 of the
  pinion's turn), on a shaft along x in two bearings (a pedestal on the bracket, a boss on the east strap).
- **D, the friction grip**: a plain drum on the same shaft outside the east strap, and a shoe on the lever bearing on its
  rim. It takes the drum round as the lever goes down and slips back over it as the lever comes up, so the shaft, the
  pinion and the tray turn one way only, a step a strike, and stand between.
- **E, the lever**: loose on the shaft beside the drum, its arm running north over the mortar's back wall to a cross pin,
  its tail south to a counterweight that holds the rod up on its stop.
- **G, the bumper rod**: hung on E's cross pin (between stamps 3 and 4, behind their stems), up through an iron guide on
  the upper guide girts' back strip, a stop collar under the guide, its head under the feeding tappet.
- **F, the feeding tappet**: a collar near the top of stamp 3's stem, above the upper guide girts, with a lug reaching
  back over G's head. When stamp 3 lands, the lug pushes G down 1 voxel (Rose's feeder strikes only when the mortar runs
  low; the model draws the dies bare, so it strikes at every drop).
- **H, the wings**: two iron plates resting on the tray's front, from the throat's front corners out over its edge,
  scraping the ore off it onto the shoot.

So the feeder needs no drive of its own (#729): stamp 3's fall works it. Each strike turns the grip 6.41°, the tray
2.40° (`feeder.trayDegreesPerStrike`): two strikes a camshaft turn, a tray turn in 150 strikes.

The motions are on the existing drivers. G's drop is the stamp's own drawn lift pushing it (in contact while the stamp is
less than the push over its die), fitted by 24 `slide`s of the camshaft's harmonics, smoothed by 0.03 radians and
lowered so it is never over its stop or into the lug: resting, the lug stands at most 0.034 over G's head, and G at
most 0.042 under its stop while the stamp is high. E's turn is the arcsine of G's drop over its arm, 24 `swing`s; its
pin and G's eye stay within 0.055 of each other. The grip's turn is the one-way sum of E's down strokes: a steady
`rotate` on the axle's travel plus its remainder's 24 harmonics as `swing`s about the same axis, smoothed by 0.04
radians (the grip taking hold and letting go); the tray has the same terms times the gearing's ratio, so the pinion and
the tray stay in mesh at every pose. Each part is drawn at its mean pose, so the rig's terms add up to the motion.

**Pace** (ψ the axle's travel):

| Shaft | Turns a radian of ψ | Through |
|---|---|---|
| Line shaft and its pulley | −1 | the rectifier, 1:1 |
| Camshaft and bull pulley | −0.5 | the belt, 4.7 to 9.4 |
| Feeder: grip, shaft and pinion | 6.41° a strike, 2 strikes a camshaft turn | the friction grip, one way |
| Feeder: tray | 2.40° a strike | the pinion's 6 teeth on the tray's 16 |

Every moving part (θ the axle's angle, ψ its travel):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft (`entry`) | The vanilla axle | The rectifier in the gear case | `rotate` x 1 (θ) |
| Line shaft (`line`) | The rectifier | Its pulley | `rotate` x −1 (ψ) |
| Line pulley (`linepulley`) [camshaft] | Keyed to the line shaft | The bull belt | `rotate` x −1 (ψ) |
| Camshaft, cams, hubs, bull pulley (`camshaft`) [camshaft] | The bull belt | The tappets | `rotate` x −0.5 (ψ) |
| Pillow blocks, bull belt (`camframe`) [camshaft] | Static | — | none |
| Stamps 1 to 5: shoe, head, stem, tappet (`stamp1`..`stamp5`) [stamps] | Its double cam | The ore on its die; stamp 3 its feeding tappet | ten `slide` y, harmonics of the camshaft's turn (ψ) |
| Mortar, dies, screen, apron, launder, shoot, both chute sections (`mortar`) [mortar] | Static | — | none |
| Feed box (`feedbox`) [feedbox] | Static | — | none |
| Feeding tappet, F (`feedtappet`) [feeder] | Keyed to stamp 3's stem (rides `stamp3`) | G's head, as the stamp lands | none |
| Bumper rod, G (`bumper`) [feeder] | The feeding tappet down, the counterweight up | E's cross pin | 24 `slide` y (ψ) |
| Lever E, its shoe and counterweight (`lever`) [feeder] | G | The grip's drum, going down | 24 `swing` x about the shaft (ψ) |
| Grip's drum, shaft and pinion (`feedshaft`) [feeder] | E's shoe, one way | The tray's teeth | `rotate` x and 24 `swing` x (ψ) |
| Tray A, its teeth and spindle (`tray`) [feeder] | The pinion | The ore under the wings | `rotate` y and 24 `swing` y (ψ) |
| Hopper, straps, bracket, bearings, wings, G's guide (`feeder`) [feeder] | Static | — | none |
| Water line, copper (`pipecopper`) [pipecopper] or lead (`pipelead`) [pipelead] | Static, one metal drawn | — | none |
| Frame, gear case, pedestals, guides, bearers (`frame`) | Static | — | none |

The only toothed wheels are the pinion and the tray's teeth, which mesh; the rectifier's gears are in the closed case.

### Regenerating

```sh
python3 mods-src/seraphhorizons/StampBattery/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/StampBattery/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/StampBattery/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes under two minutes, most of it the swept paths and the z-fighting fix; two runs are byte-identical.
`tools/validate_stampbattery.py` holds the checks; every run checks its output and exits non-zero if one fails. Checks
that set parts against each other run per build state: the frame; level 1; level 2.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; the
  `requires` are the levels' sets, level 2 keeping level 1's battery with the feeder for the feed box.
- **Nothing floats:** the frame one piece from the ground, and each build state at rest.
- **Containment:** nothing leaves the 3 × 4 × 3 box over nine poses.
- **Anchors:** the entry shaft meets the power face at the cell's centre; every anchor's point on its cell's face, at
  the thing it names (the water line's inlet, the infeed chute's mouth with its end over the hopper's mouth and the feed
  box, the output chute's mouth flush with the front face).
- **Chutes:** `VANILLA_CHUTE` is the game's straight chute (from, to, UVs, rotations, drawn faces, texture), read from
  `$VINTAGE_STORY` or `~/Games/vintagestory` when the game is there; at the infeed face the section's profile is a
  vanilla `ud` chute's in that cell, at the output face an `ns` chute's, each flush with its face, in the chute's texture.
- **Paths:** the feed box's floor falls at least 35° onto the shoot, the hopper's floor at least 45° to its throat over
  the tray, the shoot at least 45° from under the tray's front edge to the feed lip; the apron starts under the screen
  and ends over the launder, whose floor stands over the output chute's; the water line's drop is open over the feed
  opening.
- **Textures** by role, every element ruled.
- **Drive:** the line shaft turns one way for either sign of the axle; at the same travel every part but the entry shaft
  stands alike for either sign of the axle; the bull belt's two rims move alike and its runs are tangent to both rims.
- **Stamps:** over 60 poses in the lifts every tappet rides its cam (never into it, at most 0.15 over); resting, each
  shoe within 0.12 of its die and never into it; two drops a stamp a camshaft turn, in the order 1-3-5-2-4.
- **Feeder:** over five camshaft turns every 0.2°: the feeding tappet's lug never in G's head and on it within 0.06
  while the stamp rests; G never over its stop and on it within 0.06 while the stamp is high; G's eye on E's pin within
  0.08; E's shoe on the drum's rim; the pinion and the tray in their gearing's ratio at every pose; the grip turning one
  way (back under 5 % of a step a strike) and all but standing (under 10 %) while the stamp is high.
- **Supports:** the entry shaft in the gear case, the line shaft in the case's boss and both pedestals, the camshaft in
  its two pillow blocks, the pinion's shaft in its pedestal and the strap's boss, the tray's spindle in its step bearing,
  the bumper rod in its guide (and on E's pin at its foot).
- **Clearances:** over 103 poses (each build state at rest, the battery every 4.5° of a half turn, the feeder every 15°
  of five camshaft turns) no two parts touch except the intended contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the battery through a whole camshaft turn every 0.5°, and the feeder through five
  camshaft turns every 2°, everything fitted: 1620 poses.
- **No z-fighting** (full runs): no coplanar overlapping faces at six poses of the three build states.
- **Files:** every texture declared; every cell of the box present, a lid on every column, every box inside its cell;
  the shipped model is the checked one moved.

`tools/tests/test_stampbattery_model.py` and `site/test/stampbattery.test.ts` hold the written files to these rules
without the generator's state, and replay the reference poses.

### Rig schema (`stampbattery-rig.json`)

The draw bench's schema (`../DrawBench/README.md`, "Rig schema") without a work: nothing is added to the shared rig
maths. Rotations on θ and ψ, swings and slides on ψ, one part riding another (`feedtappet` on `stamp3`).

- **Cells:** each cell's `boxes` are rebuilt from the shipped, rounded shape posed at rest, the finished machine's (the
  frame, level 2 and the copper water line); every column's top cell carries a `lid`, so the top is one deck, where the
  player stands to feed by hand at the infeed's mouth.
- **`stamps`:** `turnsPerRev` (2, axle turns a camshaft turn as drawn), `dropsPerRev` (2, a stamp), `firingOrder`
  ([1, 3, 5, 2, 4]) and `lift` (0.25 blocks).
- **`feeder`:** `stamp` (3, the one with the feeding tappet), `strikesPerRev` (2 a camshaft turn), `push` (0.0625 blocks,
  G's drop at a strike) and `trayDegreesPerStrike` (2.40).
- **`levels`:** `{ "1": [mortar, camshaft, stamps, feedbox], "2": [mortar, camshaft, stamps, feeder] }`.
- **The water line's metal:** `pipecopper` or `pipelead`, one of them fitted from level 1 on (the rosser's pattern).
- **Keys.** `cells`, `powerCell`/`powerFace`, `waterCell`/`waterFace`/`water`, `infeedCell`/`infeedFace`/`infeed`,
  `outputCell`/`outputFace`/`output`, `stamps`, `feeder`, `levels` and `parts`.

**In the viewer** (`site/models.json`, `stamp-battery`): a **Level** select (the scenario's `states`): the frame, level
1 (the feed box) and level 2 (the feeder, the default), each with a note; and a **Water line** select, copper (the
default) or lead, which waits for the mortar. Play turns the axle at 1.5 turns a second, a camshaft turn in 1.3
seconds (about 90 drops a minute a stamp, a period battery's pace).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `line_*`, `linepulley*`,
`camshaft_*` (the shaft, hubs, cams `camshaft_cam<stamp><a|b><n>` and the bull pulley `camshaft_bull_*`),
`camframe_*`, `stamp1_*`..`stamp5_*`, `mortar_*` (the chute sections `mortar_chutein_*` and `mortar_chuteout_*`, the
shoot `mortar_shoot*`, the launder `mortar_launder_*`), `feedbox_*`, `feedtappet_*`, `bumper_*`, `lever_*`,
`feedshaft_*`, `tray_*`, `feeder_*` (`feeder_hopper_*`, `feeder_strap_*`, `feeder_wing_*`, ...), `pipecopper_*` and
`pipelead_*` (solderedpipe's box names: `inlet_*`, `hub1_*`, `north_*`, `hub2_*`, `drop_*`), and `fr_*` for the frame.
Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Choices made, and why

- **Salvaged from v2, unchanged in kind:** the frame's end frames and guide girts, the closed rectifier gear case and
  the line shaft, the belt drive and bull pulley, the mortar with its dies, screen and apron, the camshaft with its
  involute cams, the five stamps with their harmonic drivers, the soldered water line and the vanilla chute section, and
  most of the checks (stamps, belt, rectifier, reverse, chute, textures, supports, clearances, z-fighting). Everything
  moved 8 voxels back into the middle row.
- **New:** the three-row layout and its frame (a back post and the hopper's bearers, the caps over all three rows),
  the output chute section in place of v2's plank spout, the launder raised over it on two legs, the shoot, the
  infeed's section a whole block long over the feed, the plank feed box of level 1, the Challenge feeder, and the
  feeder's checks.
- **Belts, not gears**, as v2: a battery's camshaft was driven by a belt from the line shaft to its bull wheel.
- **The rectifier is enclosed**, as v2: the cams lift only one way round; a drawn rectifier would be about ninety
  elements of gearing.
- **The feeder's grip is Rose's friction grip.** #729 names "its ratchet and bevel": the tray's bevel is the pinion and
  the teeth in its bottom, and the ratchet is drawn as the friction grip Rose describes, a toothless ratchet: a plain
  drum and the lever's shoe on it. A toothed ratchet and pawl would need the pawl to rise over each tooth as the lever
  comes back, a part of its own on harmonics with a sharp drop; the grip slips without showing it.
- **The bumper rod stands between stamps 3 and 4.** Behind stamp 3 itself it would pass through the ore falling off the
  tray onto the shoot, so it hangs beside that stream and the feeding tappet's lug reaches it (4.5 voxels from the stem),
  and E's cross pin carries its foot over the mortar's back wall to the lever outside the east strap.
- **The feeder replaces the feed box.** Both sit under the infeed's end and deliver onto the shoot; a hopper cannot sit
  over a tray where the feed box's floor is. So the levels' sets are states, not nested (`levels` in the rig), and the
  feed box comes off when the feeder goes on.
- **The water line drops into the feed opening**, beside the shoot, so it serves both levels unchanged.
- **The infeed chute is a whole block**, so the ore drops into the hopper's mouth, not from three voxels under the top
  face across half the machine's height.

## Known weak spots, and what is not checked

The model has not been looked at in a client or in the site's viewer by the owner yet.

1. **The stamps' lift is a smoothed series.** A shoe hovers up to 0.11 voxels over its die between drops, and the free
   fall is slightly rounded where it lands. Stamps do not turn on their stems as real ones do.
2. **The feeder's motions are series too.** G leads the lug by a hair as it lands (the smoothing) and stands up to 0.04
   off its stop; the grip creeps by under 3 % of a step while the stamp is high, and the tray with it.
3. **The pinion and the tray's teeth are boxes**, not involutes or bevels; they clear each other at every sampled pose of
   a whole tooth's turn, with little backlash to spare.
4. **The feeder strikes at every drop**, as if the mortar were always running low: nothing in the rig says how full it
   is. A renderer could hold the feeder still (by holding its parts' travel) while the mortar is full.
5. **Belts are drawn still.** A belt looks alike as it runs, so its runs and wraps are frame-like pieces.
6. **Box faces and chords.** Cam faces are four chords of the involute; round parts are hexagons, octagons or 12-gons.
7. **No ore or water is drawn.**
8. **The rectifier's gears are not drawn** (the closed gear case).

## Open for the owner

1. **Build stages**: the frame recipe, the stages and their materials, measured from the model and settled with the
   owner (#729's Open), as the crucible furnace's were. The `requires` ids only split the machine where a stage could.
2. **The feed box comes off at level 2.** Fitting the feeder takes the feed box's place (gameplay would hand the feed
   box back). Should level 1's feed box stay a separate item, or be part of the mortar set?
3. **The ratchet as a friction grip** (above), Rose's own mechanism; a toothed ratchet and pawl can replace it.
4. **The feeder's size: 89 elements**, against #729's "about 25 to 40": the tray's 16 teeth and the pinion's 6 (they
   must mesh), a 12-sided drum (so the shoe sits on its rim at every turn), and hexagonal shafts. Fewer teeth (8 and 3)
   and plainer round parts would bring it to about 55.
5. **Water "into the feed box"** (#728): the line drops into the feed opening beside the shoot, under the feed box's
   lower end at level 1, so the water meets the ore there at both levels. Rose's batteries had a horizontal pipe along
   the mortar's top with a cock at each stamp (p. 123); that would be a ppex pipe with five outlets, which `UnifiedPipes`
   does not draw.
6. **Hand feeding at the infeed's mouth**, on the top face, 4 blocks up: the player stands on the deck (every column has
   a lid) to reach it. Gameplay could also take a click on the hopper's or the feed box's cells from behind.
7. **Collision boxes are the finished machine's** (level 2): the cells' boxes do not change with the level, as #711
   fixes the footprint. At level 1 the feeder's cells collide as if it were fitted (it stands where the feed box does).
8. **Anchors provisional pending #711**: the chute sections (the v2 branches' `VANILLA_CHUTE`, checked against the
   game's chute), the water anchor (a cell face with the soldered pipe's inlet, the rosser's pattern, no reservoir
   drawn) and hand feeding at the infeed.
9. **Size**: four high (accepted on #729) against a real battery's six; the stamps 5.8 voxels apart (about 36 cm) with
   a 4 voxel (25 cm) lift.
10. **The front row is mostly air above the launder**: the end frames start at z 9 behind it, so a player cannot build
    in front of the camshaft. Bringing the front posts out to the front face would box the launder in.
