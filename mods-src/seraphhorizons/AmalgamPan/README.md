# Amalgam pan

Part of the Seraph Horizons mod (`../README.md`): the hand station of the amalgamation stage of ore
processing (epic #684), used until the concentrator's jig takes the amalgamation plates (#734). A
hand-worked **amalgamating pan** of the mid 1800s, the Washoe process's pan scaled down to one block and
driven from below as the Washoe pan was: a wide, shallow sheet-iron pan with a beaded rim on an oak stand,
a hollow cast-iron cone in the middle of its floor, and an iron **spindle** that rises from a footstep
under the pan, through the cone, to the **muller** (a hub and four arms, an iron **shoe** bolted under each
arm's end, dragging on the floor). Under the pan a **crown wheel** on the spindle is turned by a
**pinion** on a horizontal **crankshaft**, which runs out to a **crank** with an oak handle on the
operator's side. Gold or silver concentrate and mercury go in; the player turns the crank and the shoes
grind the concentrate into the mercury, which takes up the free gold or silver as **amalgam**, a grey
paste, to be retorted in the still (#726).

This folder holds the model's generator and its rig (`tools/`). The gameplay is #718 and is not built
yet; the model is #719. "Gameplay" below says what the rig offers it.

**A hand station.** It has no mechanical power, no power cell, no pipe and no oil, and one level: the
hand tier's. The player works it by holding right-click on it, as on the quern. The rig's θ is that work,
the crank's angle: unlike the press brake, the squaring shear and the mandrel station (whose θ is a clock
that moves nothing), the pan's motion is continuous turning, so θ turns the crank directly and the muller
through the gears. Nothing is posed by a work quantity W: the rig has no `work`.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: the crank and the muller are plain `rotate`s).

## The station

**Footprint.** 1 cell, the controller `[0,0,0]`: the pan is one block, with no ghost. The pan's axis,
the spindle's, is the cell's vertical centre line; the crankshaft runs along the cell's north-south
centre line, under the pan.

**Orientation.** The operator stands on the native north side (`operatorSide`), where the crank turns,
its handle standing out to the block's north face. Placed as the press brake is (`side` = the way the
player looks), that is the side the player placed it from. At rest the crank points west, level. The
discharge plug is on the east side of the wall. In the viewer the crank is seen from the Opposite view
(north-east) or by orbiting to the north; the default (Angled, from the south-west) shows the pan.

**Anchors** (in `assets/seraphhorizons/config/amalgampan-rig.json`): `pan.pos`, the middle of the pan's
floor (`[0.5, 0.625, 0.5]`: grinding sounds and particles, around the cone), and `operatorSide`
(`north`). There is no `powerCell`, `powerFace`, `infeedSide`, `outputSide` or `output` (see "Open for
the owner").

**Parts and contents** (the rig's `requires`). The station has no build stages: the frame, the crank and
the muller with its shoes are all one block, always there (`requires` null), made whole by the block's
recipe. Only the contents are `requires` values:

| `requires` | Part | Draws |
|---|---|---|
| `mercury` | `mercury` | The contents: a pool of mercury 0.4 deep on the floor |
| `amalgam` | `amalgam` | The contents: the amalgam paste, 0.8 deep |

The contents are not parts: gameplay draws one of them, or neither (the pan empty). They are expressed
the way the other hand stations express their work (the press brake's `platelead` and `platecopper`), as
`requires` values gameplay sets, with no driver; the viewer shows them as a "Contents" select of three
states (`states` in `site/models.json`).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The wood is the game's debarked oak, the pan the game's riveted sheet iron
(`game:block/metal/sheet/iron1`), the cone, the bearings, the gears, the muller and the crank its iron
plate (`game:block/metal/plate/iron`), and the amalgam its lead solder ingot
(`game:block/metal/ingot/leadsolder`, a dull mottled grey). The mercury's texture is the mod's own, and
provisional: neither the game nor the pack's mods have one (Expanded Matter's `em:mercuryportion`
borrows the game's andesite and kimberlite for its bucket), so `tools/mercury_texture.py` draws a 32 × 32
liquid silver in the style of the game's liquids: broad, soft swells of light and shade, as a
mirror-bright liquid shows its surroundings, and one soft glint, from a few low waves of whole periods
(so it tiles) and a fixed hash for a faint grain. (Its first version, fine diagonal ripples with a grid
of bright dots, read as quilted cloth.) `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/amalgampan.json` | The whole station, every moving part and both contents (150 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/amalgampan_frame.json` | The static frame only (78 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/amalgampan-rig.json` | The cell, the anchors and the part rig (5 parts). |
| `assets/seraphhorizons/textures/block/liquid/mercury.png` | The provisional mercury texture (`seraphhorizons:block/liquid/mercury`). |
| `tests/AmalgamPan/rig-reference.json` | Every part's matrix at a spread of crank angles, from the reference maths. |

Elements by part: the crank 23, the muller 33, each content 8, the frame 78; drawn empty 134, with
either content 142.

### How the station works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y
up, z north to south) in voxels, measured from the cell's north-west bottom corner, so the shipped
files are the build frame divided by 16. Every round part about the pan's axis is a polygon on the pan's
16 angles (the hubs, shafts and bosses are octagons).

**The stand** is oak: a leg at each corner (1.8 square, clear of the pan), aprons on the west, east and
south sides and two bearers across under the floor (their tops at y 9, the pan's underside), low rails
west and east, and two sills across between the rails that carry the drive. The north side has no
apron: the crank turns there. The stand is 9 high, so the crank under the pan can be turned through a
circle of 3.6.

**The pan** is 14 across its outside and 12.4 inside, a 16-sided sheet-iron floor (y 9..10) and wall (to
the rim at y 13.2), so 3.2 deep inside: wide and shallow. A bead (a ring 0.3 thick) runs round the rim's
outside. The wall's foot is sunk into the floor's edge, and the floor's edge sits a hair inside the
wall's outside, so no slit shows between them. Low on the wall's east face is the **discharge plug**, an
iron boss with an oak bung, for drawing the amalgam off.

**The cone** is a hollow cast-iron cone in two tiers in the middle of the floor (apothem 1.7, then 1.15,
to y 11.9), standing above the paste: the spindle runs up through it, and its neck is the spindle's
upper bearing, as in the Washoe pan.

**The drive**, all under the pan:

- **The spindle** (apothem 0.5, y 3..14.1) stands in a cast-iron **footstep** (apothem 1.0) on a base
  plate on the south sill, rises through the floor and the cone, and carries the muller over the cone.
  Its two bearings, the footstep and the cone's neck, are 7.1 apart.
- **The crown wheel**, keyed on the spindle under the pan: a web of the pan's 16 sides (radius 3.5,
  y 7.25..7.95) and 9 teeth standing straight down from its rim (radially 2.5..3.5, down to y 6.0; its
  pitch plane at y 6.5). A crown wheel is the bevel wheel whose pitch cone is flat, so its teeth are
  straight.
- **The pinion**, a spur pinion of 6 teeth (pitch radius 2.0, hub 1.25) on the crankshaft, under the
  crown's north side; its top tooth sits in a gap of the crown at rest. Both have a circular pitch of
  2.09; the pinion's teeth are 0.8 wide and the crown's 0.75, and each mate's tips clear the other's
  roots by 0.15 or more. The pinion turns the crown 2 turns for every 3 of the crank: geared down 3:2.
- **The crankshaft** (apothem 0.5, along z at y 4.5, the line that meets the spindle's) runs in two
  pedestal bearings 1.8 apart either side of the pinion: the outer one on the north sill, the inner one
  cast with the footstep. The crank's eye is keyed on its north end, 0.2 before the outer bearing.
- **The crank**: an iron arm (4.1 from the shaft, z 2.0..2.8) on the eye, and an oak handle (apothem
  0.55) 3.6 from the shaft, standing out north to z 0.3, 0.3 inside the block's north face. Over a turn
  it passes 0.35 above the ground and 0.35 under the pan.

**The muller.** Keyed on the spindle over the cone are the muller's hub (y 12.1..13.7, 0.2 over the
cone), its four arms (y 12.15..12.95, out to 5.4 from the axis, at 45° to the cell's axes at rest), and
under each arm's end a shoe (2.4 across, 2.3..5.6 from the axis) standing on the floor.

**The contents** are flat layers on the floor, inside the wall: their edge runs 0.05 into the wall (no
gap shows at the wall) and their underside is sunk in the floor. The mercury is 0.4 deep, the amalgam
0.8; the shoes stand out of both (to y 12.15), and the cone's top (y 11.9) is above them.

Every moving part (θ the crank's angle):

| Part (rig id) | Driven by | Drives | Drivers |
|---|---|---|---|
| Crank: crankshaft, pinion, eye, arm, oak handle (`crank`) | The operator, by the crank's handle | The crown wheel, by the pinion | `rotate` z about the crankshaft (`[0.5, 0.28125, 0.5]`), ratio 1, on θ |
| Muller: spindle, crown wheel, hub, four arms, four shoes (`muller`) | The pinion, by the crown wheel | The contents (grinding) | `rotate` y about the pan's axis (`[0.5, 0, 0.5]`), ratio 2/3 (6 teeth to 9), on θ |
| Mercury (`mercury`) [mercury] | Static | — | none |
| Amalgam (`amalgam`) [amalgam] | Static | — | none |
| Frame (`frame`) | Static | — | none |

The only toothed wheels are the pinion and the crown, and they mesh. Each shaft is carried by two
bearings fixed to the frame (the spindle by the footstep and the cone's neck, the crankshaft by its two
pedestals); the spindle's foot rests in the footstep, which holds it endwise.

**Period.** The Washoe pan's shaft came up through the hollow cone from bevel gearing under the pan,
driven by a line shaft; here a hand crank turns that shaft. Two simplifications: the bevel pair is drawn
as a crown wheel and spur pinion (straight teeth, which read at block scale; a pair of cone bevels with
leaning teeth was tried and read as loose blocks), and the muller hangs on the spindle's top by its hub,
with no separate driver or adjusting screw. Hand-cranked pans were small test or assay pans; the gearing
down is ours.

### Regenerating

```sh
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py              # stdlib only; rewrites the five files
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 50 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_amalgampan.py` holds the checks; every run checks its output and exits
non-zero if one fails. The crank's **cycle** is 3 turns (the muller's 2), and the sweeps cover it:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part; no `requires` but the contents (no build stages).
- **Containment:** nothing leaves the block over the cycle, every 5°, with each contents.
- **Textures** by role: oak stand, handle and the plug's bung; `pan` for the floor, wall and bead; iron
  for the cone, the bearings and the plug's boss, the muller and the crank; each content its own.
- **Turning:** both axes stay put; the crank turns by exactly θ, the muller by 2/3 θ; nothing else moves.
- **Supports:** the spindle runs through the footstep and the cone's neck, the crankshaft through its two
  bearings (found by `supports`), each at least 0.3 inside, at rest and turned an eighth and a quarter;
  the spindle's bearings 3 or more apart, the crankshaft's 1.5; the muller's hub just clear of the cone,
  the crank's eye of the outer bearing.
- **Gears:** the tooth counts give the rig's ratio and the pitches agree; the pinion's pitch circle meets
  the crown's pitch plane and its face lies in the crown's teeth's band; each mate's tips clear the
  other's roots by 0.1 or more; at rest the pinion's top tooth sits in the middle of a gap of the crown.
  That the teeth pass each other is the swept paths'.
- **Crank:** over a turn, at least 0.3 above the ground and 0.2 under the pan; the handle out to the
  operator's side (0.1..0.5 from the north face); the crank north of the outer bearing.
- **Shoes:** at every degree of the muller's turn, each shoe flat on the floor and up against its arm,
  the arm's end over it, and at least 0.3 clear of the wall and of the cone.
- **Contents:** each on the floor, its edge just into the wall; the mercury under the paste's level, the
  paste under the cone's top and the shoes' tops.
- **Clearances:** over the cycle every 10°, with each contents, no two parts touch except the intended
  contacts listed in `ALLOWED` (the spindle in its footstep and up through the floor and the cone, the
  crankshaft in its bearings, the contents in the pan, round the shoes and round the spindle in the cone).
- **Swept paths** (full runs): the same at every degree of the cycle, with each contents (3240 poses);
  this is what holds the teeth clear of each other as they mesh.
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at nine poses, with each contents shown
  alone (`shown`). Before the fix there are 43 pairs at each pose (the floor's strips against the wall's
  foot, the crank's eye on its shaft's end, the bearings' bosses and pedestals); the fix removes the faces
  pressed against their own part (23) and insets what is left; none remain.
- **Files:** every texture declared; one cell, with a lid; no power cell and no work; the shipped model is
  the checked one (no move: the build frame's corner is the controller's).

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are: one box over the whole station, to the spindle's top (0.88), with the lid at that
height.

### Rig schema (`amalgampan-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") without its work: nothing is added to
the shared rig maths.

- **Inputs.** θ alone, the crank's angle (radians). The crank part rotates about +z by θ (ratio 1:
  counter-clockwise seen from the south as θ grows), the muller about +y by 2/3 θ (counter-clockwise
  seen from above). The contents and the frame have no driver.
- **Keys.** `cells` (one, with its box and a `lid`), `operatorSide`, `pan` and `parts`. `requires`
  values: `mercury`, `amalgam`, or null. No texture code is for a renderer to set.

**In the viewer** (`site/models.json`, `amalgam-pan`): the crank is the θ slider, over the cycle's three
turns, and Play turns it at 1.5 s a turn (40 turns a minute, a hand's pace) by its input speed. The
contents are a "Contents" select (Empty, Mercury, Amalgam), opening on Mercury. Coloured by texture, the
mercury and the amalgam have colours of their own (`site/src/lib/model-view.ts`: a light silver and a
warm grey), apart from the pan's iron.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `crank_*` (`shaft`,
`pinion_body`, `piniontooth1`..`6`, `eye`, `arm`, `handle`), `muller_*` (`spindle`, `crown_web`,
`crowntooth1`..`9`, `hub`, `arm1`..`arm4`, `shoe1`..`shoe4`), `mercury_*`, `amalgam_*`, and `fr_*`
for the frame (`leg`, `apron`, `bearer`, `rail`, `sill`, `floor`, `wall`, `rim`, `cone_base`,
`cone_neck`, `plug_boss`, `plug_bung`, `stepbox`, `footstep`, `pedestal`, `bearing`). Hand edits are lost
when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (the standalone build), at every contents state
and through the crank's turn, from several angles. No one has yet looked at it in a client.

1. **The crank is low.** It turns under the pan's overhang, its handle 0.35 to 8.6 voxels off the ground;
   from a standing player's eye the pan's lip hides the top of its circle.
2. **The contents do not turn or change.** A layer is drawn or not; the mercury does not turn grey as it
   is worked, and nothing shows the concentrate. A state between them would be a third layer.
3. **The shoes stand in the paste.** They pass through the layers as they turn; nothing is pushed aside.
4. **The handle is fixed to the crank's arm.** A real handle turns on its pin; this one, an octagon,
   turns with the crank and reads the same.
5. **Collision is one box.** The space under the pan is solid to walk into; selection takes a click
   anywhere on the block.
6. **Round parts are polygons:** the pan and the crown's web have 16 sides, the hubs, shafts and handle 8.
7. **The mercury texture is provisional** (above), and it is the mod's first texture of its own.

## Open for the owner

1. **The drive.** A crank on a crankshaft under the pan, a spur pinion and a crown wheel into the
   spindle, from below as the Washoe pan was. Whether a true cone bevel pair is wanted instead (it reads
   worse at block scale), and whether the crank should be somewhere else.
2. **The ratio.** Geared down 3:2 (6 teeth to 9): the muller turns twice for three turns of the crank, so
   gameplay's crank pace is the muller's × 1.5. 1:1 would need teeth too thin to read or gears too big to
   fit.
3. **The crank's place and size.** Under the pan on the operator's (north) side, the stand raised to 9 so
   its circle is 3.6; the handle stands to 0.3 inside the north face (nothing leaves the block). The
   crank's sense (counter-clockwise from the south as θ grows) and its rest (pointing west, level).
4. **The contents.** Two layers as `requires` values, drawn one at a time; heights 0.4 (mercury) and 0.8
   (amalgam). Whether a third state is wanted (concentrate added, before or while it is worked), and
   whether gameplay should ever draw both.
5. **The mercury texture**: the mod's own provisional one (reworked), or Expanded Matter's look (andesite
   with kimberlite blended over it, Lighten), which a blocktype could set on the `mercury` code with
   `blendedOverlays` to match the bucket. The amalgam's lead solder ingot texture is likewise a choice.
6. **Anchors.** Only `pan.pos` and `operatorSide`. Whether the pan takes from or gives to a container
   beside it (an `infeedSide`, `outputSide` or `output`, as the press brake has) is the gameplay's call.
7. **The lid.** A deck over the cell at the spindle's top, as the press brake has (the mandrel station
   has none).
8. **The recipe.** The comment on #718 fits the muller and the shoes as stages and makes "the bearing
   cone, the arch and the rivets" of nails and strips; the muller and shoes are now part of the block,
   and there is no arch, so the block's recipe takes their iron (a rod for the spindle and crankshaft,
   plate for the shoes and gears) if gameplay wants them paid for.

## Gameplay

Not built: #718 (gold and silver concentrate plus mercury to amalgam, lifting the × 0.7 free-metal
penalty; the amalgam retorted in the still, #726). What the rig offers it, after the hand stations'
pattern (`../PressBrake/README.md`, "Gameplay"):

- **The clock.** A renderer turns θ while the player holds right-click (the quern's pattern), at a pace
  the gameplay sets, and draws the `crank` and `muller` parts by the shared rig maths
  (`Machines/Core/RigAnimation.cs`); the rig has no work, so nothing else follows the clock.
- **Parts.** The renderer always draws the crank and the muller (they have no `requires`), and draws
  `mercury` or `amalgam` as the contents gameplay sets. The frame shape is the block's.
- **Tests.** `tests/AmalgamPan/AmalgamPanRigTests.cs` (the shipped rig through the shared parser, its
  vocabulary, no work, and every pose of `tests/AmalgamPan/rig-reference.json` replayed through
  `Machines/Core`), `tools/tests/test_amalgampan_model.py` and `site/test/amalgampan.test.ts` (the
  generated files against their own rules and the site's rig maths).
