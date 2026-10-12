# Amalgam pan

Part of the Seraph Horizons mod (`../README.md`): the hand station of the amalgamation stage of ore
processing (epic #684), used until the concentrator's jig takes the amalgamation plates (#734). An
**amalgamating pan** of the mid 1800s, the Washoe process's pan scaled down to one block, worked as the
game's **quern** is, both ways: **by hand**, the player holding right-click on it, or **by a vanilla
axle under it**, with no player, as the Washoe pan was driven from below. A wide, shallow sheet-iron pan
with a beaded rim on an oak stand, a hollow cast-iron cone in the middle of its floor, and an iron
**spindle** carrying the **muller** (a hub and four arms, an iron **shoe** bolted under each arm's end,
dragging on the floor) over the cone. The spindle comes down through the cone and a cast-iron
**footstep** bearing on an oak bridge-tree to the block's bottom face, where it ends in the vanilla axle's
oak cross profile, the **coupling**: an axle in the block below turns it. Gold or silver concentrate and
mercury go in; the muller's shoes grind the concentrate into the mercury, which takes up the free gold or
silver as **amalgam**, a grey paste, to be retorted in the still (#726).

This folder holds the model's generator and its rig (`tools/`). The gameplay is #718 and is not built
yet; the model is #719. "Gameplay" below says what the rig offers it.

**Worked as the quern is.** The game's quern (`game:quern-*`, `BlockQuern` with an `MPConsumer` entity
behaviour) takes mechanical power at its up or down face (`BlockQuern.HasMechPowerConnectorAt`) and,
powered, draws its top stone at the network's angle (`QuernTopRenderer`: `AngleRad` from the consumer),
so the stone turns once a turn of the axle; by hand it turns the stone 40° a second while the player holds
right-click. The pan does the same with its muller: under power it turns 1:1 with the axle, which comes
up into its down face; by hand the player holds right-click on the pan and the muller turns, with no
crank or handle to see. There is no oil and no pipe, and one level: the hand tier's. The rig's θ is the
spindle's angle, the axle's or the hand's clock; nothing is posed by a work quantity W: the rig has no
`work`.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: the muller is a plain `rotate`).

## The station

**Footprint.** 1 cell, the controller `[0,0,0]`: the pan is one block, with no ghost. The pan's axis,
the spindle's, is the cell's vertical centre line.

**Power.** `powerCell` `[0,0,0]`, `powerFace` down: a vanilla axle standing in the block below
(`woodenaxle-ud`) comes up into the pan's down face on the pan's axis, as an axle comes into the quern's.
The pan's own cell is its power cell (a one-block station needs no power ghost); its block carries the
mechanical-power consumer, as the quern's does.

**Orientation.** The operator stands on the native north side (`operatorSide`), where they hold
right-click; the pan is nearly symmetric, and the discharge plug is on the east side of the wall. Placed
as the press brake is (`side` = the way the player looks).

**Anchors** (in `assets/seraphhorizons/config/amalgampan-rig.json`): `powerCell` and `powerFace`; `pan.pos`,
the middle of the pan's floor (`[0.5, 0.375, 0.5]`: grinding sounds and particles, around the cone);
`operatorSide` (`north`). There is no `infeedSide`, `outputSide` or `output` (see "Open for the owner").

**Parts and contents** (the rig's `requires`). The station has no build stages: the frame and the muller
with its spindle, shoes and coupling are all one block, always there (`requires` null). Only the
contents are `requires` values:

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
nothing from `build/mods`. The wood is the game's debarked oak (the coupling too: a shaft that continues
a vanilla wooden axle stays wood), the pan the game's riveted sheet iron (`game:block/metal/sheet/iron1`),
the cone, the footstep and the muller its iron plate (`game:block/metal/plate/iron`), and the amalgam its
lead solder ingot (`game:block/metal/ingot/leadsolder`, a dull mottled grey). The mercury's texture is
the mod's own, and provisional: neither the game nor the pack's mods have one (Expanded Matter's
`em:mercuryportion` borrows the game's andesite and kimberlite for its bucket), so
`tools/mercury_texture.py` draws a 32 × 32 liquid silver in the style of the game's liquids: broad, soft
swells of light and shade, as a mirror-bright liquid shows its surroundings, and one soft glint, from a
few low waves of whole periods (so it tiles) and a fixed hash for a faint grain. (Its first version, fine
diagonal ripples with a grid of bright dots, read as quilted cloth.) `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/amalgampan.json` | The whole station, the muller and both contents (101 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/amalgampan_frame.json` | The static frame only (67 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/amalgampan-rig.json` | The cell, the power cell and face, the anchors and the part rig (4 parts). |
| `assets/seraphhorizons/textures/block/liquid/mercury.png` | The provisional mercury texture (`seraphhorizons:block/liquid/mercury`). |
| `tests/AmalgamPan/rig-reference.json` | Every part's matrix at a spread of shaft angles, from the reference maths. |

Elements by part: the muller 18, each content 8, the frame 67; drawn empty 85, with either content 93.

### How the station works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y
up, z north to south) in voxels, measured from the cell's north-west bottom corner, so the shipped
files are the build frame divided by 16. Every round part about the pan's axis is a polygon on the pan's
16 angles (the hubs, the spindle and the footstep are octagons).

**The stand** is oak: a leg at each corner (1.8 square, clear of the pan), an apron on each side and two
bearers across under the floor (their tops at y 5, the pan's underside), low rails west and east (y
1.6..2.8), and between them, under the pan's axis, the **bridge-tree** that carries the footstep. It is
as high as the footstep and the coupling under it need: the pan stands on it at y 5.

**The pan** is 14 across its outside and 12.4 inside, a 16-sided sheet-iron floor (y 5..6) and wall (to
the rim at y 9.2), so 3.2 deep inside: wide and shallow. A bead (a ring 0.3 thick) runs round the rim's
outside. The wall's foot is sunk into the floor's edge, and the floor's edge sits a hair inside the
wall's outside, so no slit shows between them. Low on the wall's east face is the **discharge plug**, an
iron boss with an oak bung, for drawing the amalgam off.

**The cone** is a hollow cast-iron cone in two tiers in the middle of the floor (apothem 1.7, then 1.15,
to y 7.9), standing above the paste: the spindle runs up through it, and its neck is the spindle's
upper bearing, as in the Washoe pan.

**The spindle** (iron, apothem 0.5, y 1..10.1) carries the muller over the cone and comes down through
the cone, the floor and the **footstep** (an iron bearing, apothem 1.1, y 2.8..3.6, on the bridge-tree),
then through the bridge-tree to the **coupling**: the vanilla axle's cross profile, two oak bars 3 by 1.2
crossed, from the block's bottom face (y 0) to y 1.4, 0.2 under the bridge-tree, the spindle's iron end
set 0.4 into it. An axle in the block below meets it at the bottom face. The spindle's two bearings, the
footstep and the cone's neck, are 3.1 apart; the axle below holds it endwise, as the quern's does its
stone.

**The muller.** Keyed on the spindle over the cone are the muller's hub (y 8.1..9.7, 0.2 over the cone),
its four arms (y 8.15..8.95, out to 5.4 from the axis, at 45° to the cell's axes at rest), and under
each arm's end a shoe (2.4 across, 2.3..5.6 from the axis) standing on the floor.

**The contents** are flat layers on the floor, inside the wall: their edge runs 0.05 into the wall (no
gap shows at the wall) and their underside is sunk in the floor. The mercury is 0.4 deep, the amalgam
0.8; the shoes stand out of both (to y 8.15), and the cone's top (y 7.9) is above them.

Every moving part (θ the spindle's angle):

| Part (rig id) | Driven by | Drives | Drivers |
|---|---|---|---|
| Muller: spindle, coupling, hub, four arms, four shoes (`muller`) | The axle below, by the coupling; or the player, by right-click held | The contents (grinding) | `rotate` y about the pan's axis (`[0.5, 0, 0.5]`), ratio 1, on θ |
| Mercury (`mercury`) [mercury] | Static | — | none |
| Amalgam (`amalgam`) [amalgam] | Static | — | none |
| Frame (`frame`) | Static | — | none |

There are no gears: the spindle turns with the axle, once a turn. By hand nothing is drawn turning but
the muller, as on the quern, where the player's hand turns the stone with nothing to show for it.

**Period.** The Washoe pan's shaft came up through the hollow cone from gearing under the pan, driven by
a line shaft; here the vanilla axle stands in for that shaft and its gearing, as on every machine in the
pack. The muller hangs on the spindle's top by its hub, with no separate driver or adjusting screw.

### Regenerating

```sh
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py              # stdlib only; rewrites the five files
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 6 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_amalgampan.py` holds the checks; every run checks its output and exits
non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part; no `requires` but the contents (no build stages).
- **Containment:** nothing leaves the block over a whole turn, every 5°, with each contents.
- **Textures** by role: oak stand, bridge-tree, coupling and the plug's bung; `pan` for the floor, wall and
  bead; iron for the cone, the footstep, the plug's boss and the muller; each content its own.
- **Turning:** the pan's axis stays put; the muller turns by exactly θ (ratio 1); nothing else moves.
- **Supports:** the spindle runs through the footstep and the cone's neck (found by `supports`), at least
  0.3 inside each, at rest and turned an eighth and a quarter, the two 3 or more apart; the muller's hub
  just clear of the cone.
- **Coupling:** the power cell is `[0,0,0]` and the power face down; the spindle's foot is the axle's oak
  cross profile (3 by 1.2), centred on the pan's axis, from the block's bottom face up past the spindle's
  iron end, 0.1 or more under the bridge-tree.
- **Shoes:** at every degree of a turn, each shoe flat on the floor and up against its arm, the arm's end
  over it, and at least 0.3 clear of the wall and of the cone.
- **Contents:** each on the floor, its edge just into the wall; the mercury under the paste's level, the
  paste under the cone's top and the shoes' tops.
- **Clearances:** over a turn every 10°, with each contents, no two parts touch except the intended
  contacts listed in `ALLOWED` (the spindle down through the footstep and the bridge-tree and up through
  the floor and the cone, the contents in the pan, round the shoes and round the spindle in the cone).
- **Swept paths** (full runs): the same at every degree of a turn, with each contents (1080 poses).
- **Nothing floats:** every frame element joined to the ground.
- **No z-fighting** (full runs): no coplanar overlapping faces at nine poses, with each contents shown
  alone (`shown`). Before the fix there are 34 pairs at each pose (32 of the floor's strips against the
  wall's foot, and the coupling's two bars where they cross at the bottom face); the fix removes the faces
  pressed against their own part (20) and insets what is left (one bar's foot by 0.015); none remain.
- **Files:** every texture declared; one cell, with a lid; the power cell and face; no work; the shipped
  model is the checked one (no move: the build frame's corner is the controller's).

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are: one box over the whole station, to the spindle's top (0.63), with the lid at that
height.

### Rig schema (`amalgampan-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") without its work, plus a power cell
and face as the powered machines have: nothing is added to the shared rig maths.

- **Inputs.** θ alone, the spindle's angle (radians). The muller part rotates about +y by θ (ratio 1:
  counter-clockwise seen from above as θ grows). The contents and the frame have no driver.
- **Keys.** `cells` (one, with its box and a `lid`), `powerCell` and `powerFace`, `operatorSide`, `pan`
  and `parts`. `requires` values: `mercury`, `amalgam`, or null. No texture code is for a renderer to
  set.

**In the viewer** (`site/models.json`, `amalgam-pan`): θ is the "Muller turned" slider over one turn, and
Play turns it at 1.5 s a turn (40 turns a minute) by its input speed. The power cell is drawn with an
arrow up into its down face. The contents are a "Contents" select (Empty, Mercury, Amalgam), opening on
Mercury. Coloured by texture, the mercury and the amalgam have colours of their own
(`site/src/lib/model-view.ts`: a light silver and a warm grey), apart from the pan's iron.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `muller_*` (`spindle`,
`couplinga`, `couplingb`, `hub`, `arm1`..`arm4`, `shoe1`..`shoe4`), `mercury_*`, `amalgam_*`, and `fr_*`
for the frame (`leg`, `apron`, `bearer`, `rail`, `bridgetree`, `floor`, `wall`, `rim`, `cone_base`,
`cone_neck`, `plug_boss`, `plug_bung`, `footstep`). Hand edits are lost when the script runs again: port
them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (the standalone build), at every contents state
and through a turn, from several angles. No one has yet looked at it in a client.

1. **The contents do not turn or change.** A layer is drawn or not; the mercury does not turn grey as it
   is worked, and nothing shows the concentrate. A state between them would be a third layer.
2. **The shoes stand in the paste.** They pass through the layers as they turn; nothing is pushed aside.
3. **Collision is one box.** The space under the pan is solid to walk into; selection takes a click
   anywhere on the block.
4. **Round parts are polygons:** the pan has 16 sides, the hubs, spindle and footstep 8.
5. **The mercury texture is provisional** (above), and it is the mod's first texture of its own.

## Open for the owner

1. **The contents.** Two layers as `requires` values, drawn one at a time; heights 0.4 (mercury) and 0.8
   (amalgam). Whether a third state is wanted (concentrate added, before or while it is worked), and
   whether gameplay should ever draw both.
2. **The mercury texture**: the mod's own provisional one (reworked), or Expanded Matter's look (andesite
   with kimberlite blended over it, Lighten), which a blocktype could set on the `mercury` code with
   `blendedOverlays` to match the bucket. The amalgam's lead solder ingot texture is likewise a choice.
3. **Power from below only.** The quern takes an axle at its top or its bottom; the pan takes one only
   from below (its top is the open pan).
4. **Anchors.** Whether the pan takes from or gives to a container beside it (an `infeedSide`,
   `outputSide` or `output`, as the press brake has) is the gameplay's call.
5. **The lid.** A deck over the cell at the spindle's top, as the press brake has.
6. **The recipe.** The comment on #718 fits the muller and the shoes as stages and makes "the bearing
   cone, the arch and the rivets" of nails and strips; the muller is now part of the block and there is
   no arch, so the block's recipe takes their iron if gameplay wants them paid for.

## Gameplay

Not built: #718 (gold and silver concentrate plus mercury to amalgam, lifting the × 0.7 free-metal
penalty; the amalgam retorted in the still, #726). What the rig offers it, after the quern's pattern and
the powered machines' rig keys:

- **Power.** The block carries a mechanical-power consumer whose only connector is the down face
  (`powerFace`), as the quern's `MPConsumer` with `HasMechPowerConnectorAt` up or down. Powered, θ is the
  network's angle about the vertical axle, 1:1 (the quern's top takes `AngleRad` as it is). The shared
  `MillMotion.NativeShaftAngle` takes horizontal input shafts only (x or z); the pan's renderer needs the
  vertical case, the sign at which a vanilla `-ud` axle draws itself, which the gameplay adds.
- **By hand.** While the player holds right-click (the quern's `OnBlockInteractStep`), the renderer
  advances θ at a pace the gameplay sets (the quern's stone turns 40° a second); nothing else is drawn.
- **Parts.** The renderer always draws the muller (it has no `requires`), and draws `mercury` or
  `amalgam` as the contents gameplay sets. The frame shape is the block's.
- **Tests.** `tests/AmalgamPan/AmalgamPanRigTests.cs` (the shipped rig through the shared parser, its
  vocabulary, the power cell and face, no work, and every pose of `tests/AmalgamPan/rig-reference.json`
  replayed through `Machines/Core`), `tools/tests/test_amalgampan_model.py` and
  `site/test/amalgampan.test.ts` (the generated files against their own rules and the site's rig maths).
