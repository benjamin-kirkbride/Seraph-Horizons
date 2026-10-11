# Amalgam pan

Part of the Seraph Horizons mod (`../README.md`): the hand station, and tier 1, of the amalgamation stage
of ore processing (epic #684). A hand-worked **amalgamating pan** of the mid 1800s, the Washoe process's
pan scaled down to one block: a wide, shallow sheet-iron pan with a beaded rim on an oak stand, a
cast-iron step in the middle of its floor, a shallow cast-iron arch across its rim, and between them an
iron spindle carrying the **muller** (a hub and four arms, an iron **shoe** bolted under each arm's end,
dragging on the floor) and, above the arch, a **crank** with an oak handle. Gold or silver concentrate and
mercury go in; the player turns the crank and the shoes grind the concentrate into the mercury, which
takes up the free gold or silver as **amalgam**, a grey paste, to be retorted in the still (#726).

This folder holds the model's generator and its rig (`tools/`). The gameplay is #718 and is not built
yet; the model is #719. "Gameplay" below says what the rig offers it.

**A hand station.** It has no mechanical power, no power cell and no oil. The player works it by holding
right-click on it, as on the quern. The rig's θ is that work, the crank's angle: unlike the press brake,
the squaring shear and the mandrel station (whose θ is a clock that moves nothing), the pan's one motion
is continuous turning, so θ turns the muller directly, once a turn of the crank. Nothing is posed by a
work quantity W: the rig has no `work`.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: the muller is a plain `rotate`).

## The station

**Footprint.** 1 cell, the controller `[0,0,0]`: the pan is one block, with no ghost. The pan's axis,
the spindle's, is the cell's vertical centre line.

**Orientation.** The operator stands on the native north side (`operatorSide`), and at rest the crank
points north, its handle nearest them. Placed as the press brake is (`side` = the way the player
looks), that is the side the player placed it from. The pan is nearly symmetric: the arch runs west to
east, across the operator's view, and the discharge plug is on the east side of the wall.

**Anchors** (in `assets/seraphhorizons/config/amalgampan-rig.json`): `pan.pos`, the middle of the pan's
floor (`[0.5, 0.375, 0.5]`: grinding sounds and particles, around the step), and `operatorSide`
(`north`). There is no `powerCell`, `powerFace`, `infeedSide`, `outputSide` or `output` (see "Open for
the owner").

**Fitted parts and contents** (the rig's `requires`). Everything with `requires` null is the frame: the
stand, the pan with its rim, step and plug, and the arch with its boss. The rest:

| `requires` | Part | Draws |
|---|---|---|
| `muller` | `muller` | The spindle, the muller's hub and four arms, the crank's hub and arm (iron) and its handle (oak) |
| `shoes` | `shoes` | The four iron shoes under the arms' ends. Their texture code is `shoe`, so a renderer can set it to a fitted metal |
| `mercury` | `mercury` | The contents: a pool of mercury 0.4 deep on the floor |
| `amalgam` | `amalgam` | The contents: the amalgam paste, 0.8 deep |

The build stages (which item fits `muller` and `shoes`, and in what order) are not designed yet; the ids
only split the station where a stage could. The contents are not parts: gameplay draws one of them, or
neither (the pan empty). They are expressed the way the other hand stations express their work (the
press brake's `platelead` and `platecopper`), as `requires` values gameplay sets, with no driver; the
viewer shows them as a "Contents" select of three states (`states` in `site/models.json`).

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The wood is the game's debarked oak, the pan the game's riveted sheet iron
(`game:block/metal/sheet/iron1`), the step, arch, muller and crank its iron plate
(`game:block/metal/plate/iron`), and the amalgam its lead solder ingot (`game:block/metal/ingot/leadsolder`,
a dull mottled grey). The mercury's texture is the mod's own, and provisional: neither the game nor the
pack's mods have one (Expanded Matter's `em:mercuryportion` borrows the game's andesite and kimberlite
for its bucket), so `tools/mercury_texture.py` draws a plain 32 × 32 silver liquid in the style of the
game's liquids, from integer-period waves (so it tiles) and a fixed hash for its grain.
`tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/amalgampan.json` | The whole station, every moving part and both contents (113 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/amalgampan_frame.json` | The static frame only (72 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/amalgampan-rig.json` | The cell, the anchors and the part rig (5 parts). |
| `assets/seraphhorizons/textures/block/liquid/mercury.png` | The provisional mercury texture (`seraphhorizons:block/liquid/mercury`). |
| `tests/AmalgamPan/rig-reference.json` | Every part's matrix at a spread of crank angles, from the reference maths. |

### How the station works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y
up, z north to south) in voxels, measured from the cell's north-west bottom corner, so the shipped
files are the build frame divided by 16. Every round part is a polygon on the pan's 16 angles (the
round parts of the muller and crank are octagons on every other one).

**The stand** is oak: a leg at each corner (1.8 square, clear of the pan), an apron on each side and two
bearers across under the floor (their tops at y 5, the pan's underside), and low rails west and east.

**The pan** is 14 across its outside and 12.4 inside, a 16-sided sheet-iron floor (y 5..6) and wall (to
the rim at y 9.2), so 3.2 deep inside: wide and shallow. A bead (a ring 0.3 thick) runs round the rim's
outside. The wall's foot is sunk into the floor's edge, and the floor's edge sits a hair inside the
wall's outside, so no slit shows between them. Low on the wall's east face is the **discharge plug**, an
iron boss with an oak bung, for drawing the amalgam off.

**The step** is a cast-iron cone in two tiers in the middle of the floor (apothem 1.7, then 1.15, to y
7.9): the spindle's lower bearing, standing above the paste so its foot (y 7) stays out of it. **The
arch** is the upper bearing: a foot on the rim each side (over the wall's top and the bead), from each a
half arch of two iron bars bent once, rising to the crown, and there a boss (apothem 1.0, y 11..12.6)
round the spindle. The two bearings are 3.1 apart.

**The muller.** On the spindle (apothem 0.45, y 7..14.7), keyed, are the muller's hub (y 8.1..9.7, 0.2
over the step), its four arms (y 8.15..8.95, out to 5.4 from the axis, at 45° to the arch at rest),
and under each arm's end a shoe (2.4 across, 2.3..5.6 from the axis) standing on the floor. Over the
arch the crank's hub (y 13.2..14.4), its arm (out to 4.95) and the oak handle (4.4 from the axis, to y
15.9). The handle is turned by hand; everything on the spindle turns with it, once a turn.

**The contents** are flat layers on the floor, inside the wall: their edge runs 0.05 into the wall (no
gap shows at the wall) and their underside is sunk in the floor. The mercury is 0.4 deep, the amalgam
0.8; the shoes stand out of both (to y 8.15), and the spindle's foot (y 7) and the step's top (y 7.9)
are above them.

Every moving part (θ the crank's angle):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Muller: spindle, hub, four arms, crank hub and arm, oak handle (`muller`) [muller] | The operator, by the crank's handle | The shoes | `rotate` y about the pan's axis (`[0.5, 0, 0.5]`), ratio 1, on θ |
| Shoes (`shoes`) [shoes] | Ride the muller (bolted under its arms) | The contents (grinding) | none (ride) |
| Mercury (`mercury`) [mercury] | Static | — | none |
| Amalgam (`amalgam`) [amalgam] | Static | — | none |
| Frame (`frame`) | Static | — | none |

There are no gears, so no toothed wheel meshes with nothing. The spindle is carried by two bearings fixed
to the frame (the step and the arch's boss); its foot rests in the step, which holds it endwise.

### Regenerating

```sh
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py              # stdlib only; rewrites the five files
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 5 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_amalgampan.py` holds the checks; every run checks its output and exits
non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part.
- **Containment:** nothing leaves the block over a whole turn, every 5°, with each contents.
- **Textures** by role: oak stand and handle and the plug's bung; `pan` for the floor, wall and bead;
  iron for the step, the arch, the plug's boss and the muller; `shoe` for the shoes; each content its own.
- **Turning:** the pan's axis stays put, the muller turns by exactly θ, the shoes ride it, and nothing
  else moves.
- **Supports:** the spindle runs through the step and the boss (found by `supports`), at least 0.3
  inside each, the two 3 or more apart; the muller's hub just clear of the step, the crank's hub of the
  boss.
- **Shoes:** at every degree of a turn, each shoe flat on the floor and up against its arm, the arm's end
  over it, and at least 0.3 clear of the wall and of the step.
- **Contents:** each on the floor, its edge just into the wall; the mercury under the paste's level, the
  paste under the spindle's foot, the step's top and the shoes' tops.
- **Bridge:** both feet on the rim (the wall's top and the bead); each half arch from inside its foot,
  its two bars meeting at the bend, into the boss, clear of the spindle.
- **Clearances:** over a turn every 10°, with each contents, no two parts touch except the intended
  contacts listed in `ALLOWED` (the spindle in its bearings, the shoes under their arms, the contents in
  the pan and round the shoes).
- **Swept paths** (full runs): the same at every degree of a turn, with each contents (1080 poses).
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at nine poses, with each contents shown
  alone (`shown`). The model is built so that there are none before the fix (ring segments alternate
  their ends by 0.03, round strips step theirs by 0.012, layers start at heights no other face has); the
  fix only removes the faces pressed against their own part (19).
- **Files:** every texture declared; one cell, with a lid; no power cell and no work; the shipped model is
  the checked one (no move: the build frame's corner is the controller's).

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are.

### Rig schema (`amalgampan-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") without its work: nothing is added to
the shared rig maths.

- **Inputs.** θ alone, the crank's angle (radians). The muller part rotates about +y by θ (ratio 1:
  counter-clockwise seen from above as θ grows), the shoes ride it. The contents and the frame have no
  driver.
- **Keys.** `cells` (one, with its boxes and a `lid`), `operatorSide`, `pan` and `parts`. `requires`
  values: `muller`, `shoes`, `mercury`, `amalgam`, or null. The texture code a renderer may set: `shoe`
  (iron in the shape).

**In the viewer** (`site/models.json`, `amalgam-pan`): the crank is the θ slider, and Play turns it at
1.5 s a turn (about 40 turns a minute, a hand's pace) by its input speed. The muller and the shoes are
checkboxes; the contents are a "Contents" select (Empty, Mercury, Amalgam), opening on Mercury. Coloured
by texture, the mercury and the amalgam have colours of their own (`site/src/lib/model-view.ts`), apart
from the pan's iron.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `muller_*` (`spindle`,
`hub`, `arm1`..`arm4`, `crankhub`, `crank`, `handle`), `shoe_1`..`shoe_4`, `mercury_*`, `amalgam_*`, and
`fr_*` for the frame (`leg`, `apron`, `bearer`, `rail`, `floor`, `wall`, `rim`, `cone_base`, `cone_neck`,
`plug_boss`, `plug_bung`, `foot`, `arch`, `boss`). Hand edits are lost when the script runs again: port
them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it
in a client or the site's viewer.

1. **The contents do not turn or change.** A layer is drawn or not; the mercury does not turn grey as it
   is worked, and nothing shows the concentrate. A state between them would be a third layer.
2. **The shoes stand in the paste.** They pass through the layers as they turn; nothing is pushed
   aside.
3. **The handle is fixed to the crank's arm.** A real handle turns on its pin; this one turns with the
   crank.
4. **Selection follows the crank at rest.** The cell's third box is the crank and handle where they
   rest (north); as the crank turns the handle leaves it. The lid covers the top for walking.
5. **Round parts are polygons:** the pan has 16 sides, the hubs, bosses and handle 8.
6. **The mercury texture is provisional** (above), and it is the mod's first texture of its own.

## Open for the owner

1. **The bridge.** The spindle's upper bearing is a cast arch on the rim. A Washoe pan has none (its
   shaft is driven from below, through a hollow cone that carries it); a top crank needs one, or a tall
   cone carrying the spindle alone, cantilevered to the crank. The arch was kept for the two bearings.
2. **Requires ids.** `muller` takes the spindle, the hub and arms, and the crank with its handle as one
   part; `shoes` the four shoes. Whether the crank (or the handle) should be a stage of its own, and which
   items fit each, is for the build stages.
3. **The shoes' texture code** (`shoe`, iron now) lets a renderer draw a fitted metal, as the press
   brake's edges do; drop it if the shoes are always iron.
4. **The contents.** Two layers as `requires` values, drawn one at a time; heights 0.4 (mercury) and 0.8
   (amalgam). Whether a third state is wanted (concentrate added, before or while it is worked), and
   whether gameplay should ever draw both.
5. **The mercury texture**: the mod's own provisional one, or Expanded Matter's look (andesite with
   kimberlite blended over it, Lighten), which a blocktype could set on the `mercury` code with
   `blendedOverlays` to match the bucket. The amalgam's lead solder ingot texture is likewise a choice.
6. **The crank's sense** (counter-clockwise from above as θ grows) and its rest (pointing at the operator).
7. **Anchors.** Only `pan.pos` and `operatorSide`. Whether the pan takes from or gives to a container
   beside it (an `infeedSide`, `outputSide` or `output`, as the press brake has) is the gameplay's call.
8. **The lid.** A deck over the cell, as the press brake has (the mandrel station has none).
9. **The viewer's colours.** Two texture families were added so the contents read apart from the pan in
   the viewer (`mercury`, `amalgam`).

## Gameplay

Not built: #718 (gold and silver concentrate plus mercury to amalgam, lifting the × 0.7 free-metal
penalty; the amalgam retorted in the still, #726). What the rig offers it, after the hand stations'
pattern (`../PressBrake/README.md`, "Gameplay"):

- **The clock.** A renderer turns θ while the player holds right-click (the quern's pattern), at a pace
  the gameplay sets, and draws the `muller` part (and the `shoes`, riding it) by the shared rig maths
  (`Machines/Core/RigAnimation.cs`); the rig has no work, so nothing else follows the clock.
- **Parts.** Every part with a `requires`, a ride or a driver is drawn by the renderer when its value is
  fitted: `muller` and `shoes` as stages, `mercury` or `amalgam` as the contents. The frame shape is the
  block's.
- **Tests.** `tests/AmalgamPan/AmalgamPanRigTests.cs` (the shipped rig through the shared parser, its
  vocabulary, no work, and every pose of `tests/AmalgamPan/rig-reference.json` replayed through
  `Machines/Core`), `tools/tests/test_amalgampan_model.py` and `site/test/amalgampan.test.ts` (the
  generated files against their own rules and the site's rig maths).
