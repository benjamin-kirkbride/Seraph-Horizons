# Amalgam pan

Part of the Seraph Horizons mod (`../README.md`): the hand station of the amalgamation stage of ore
processing (epic #684); its machine form is the amalgamation plates (#917), a separate machine. An
**amalgamating pan** of the mid 1800s, the Washoe process's pan scaled down to one block, worked as the
game's **quern** is, both ways: **by hand**, the player holding right-click on it, or **by a vanilla
axle under it**, with no player, as the Washoe pan was driven from below. A wide, shallow sheet-iron pan
with a beaded rim on an oak stand and a hollow cast-iron cone in the middle of its floor, and the two
parts fitted to it by hand (#718), in order: the **muller**, an iron **spindle** (the fitted rod) carrying
a hub and four arms over the cone, which comes down through the cone and a cast-iron **footstep** bearing
on an oak bridge-tree to the block's bottom face and ends there in the vanilla axle's oak cross profile,
the **coupling**, so an axle in the block below turns it; then its **shoes**, an iron or steel plate
bolted under each arm's end, dragging on the floor. Crushed gold or silver quartz, water and mercury go
in; the shoes work them into a pulp, and the mercury takes up the free gold or silver as **amalgam**, a
grey paste, to be retorted in the still (#726).

This folder holds the model's generator and its rig (`tools/`), and the gameplay (#718): its rules in
`Core/`, the game side in `Game/`, described in the mod's README, "Ore processing: the amalgam pan". The
model is #719. "Gameplay" below says what the rig offers it and how the gameplay takes it.

**Worked as the quern is.** The game's quern (`game:quern-*`, `BlockQuern` with an `MPConsumer` entity
behaviour) takes mechanical power at its up or down face (`BlockQuern.HasMechPowerConnectorAt`) and,
powered, draws its top stone at the network's angle (`QuernTopRenderer`: `AngleRad` from the consumer),
so the stone turns once a turn of the axle; by hand it turns the stone 40° a second while the player holds
right-click. The pan does the same with its muller: under power it turns 1:1 with the axle, which comes
up into its down face; by hand the player holds right-click on the pan and the muller turns, with no
crank or handle to see. There is no oil and no pipe. The rig's θ is
the spindle's angle, the axle's or the hand's clock; nothing is posed by a work quantity W: the rig has
no `work`.

**Loaded and emptied by hand only.** The pan takes no automated input or output: no chute, hopper or
chest feeds it or takes from it (the 10-13 spec on #718, and epic #684's rule for hand stations). The
player pours in the mercury, the crushed quartz and the water (from a bucket in hand, as at the rocker)
and takes the amalgam out by hand; only the power is automatic, from the axle below. The rig has no
infeed or output anchor (`infeedSide`, `outputSide`, `output`), only `operatorSide`, and a check holds it
so. Nothing here waits on #711's shared anchors: the power cell is the vanilla axle's, and there is no
chute, hopper or water anchor to settle.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: the muller is a plain `rotate`, the shoes and the water film ride it).

## The station

**Footprint.** 1 cell, the controller `[0,0,0]`: the pan is one block, with no ghost. The pan's axis,
the spindle's, is the cell's vertical centre line.

**Power.** `powerCell` `[0,0,0]`, `powerFace` down: a vanilla axle standing in the block below
(`woodenaxle-ud`) comes up into the pan's down face on the pan's axis, as an axle comes into the quern's.
The pan's own cell is its power cell (a one-block station needs no power ghost); its block carries the
mechanical-power consumer, as the quern's does. Power comes from below only: the pan's top is open.

**Orientation.** The operator stands on the native north side (`operatorSide`), where they hold
right-click and load and empty the pan; the pan is nearly symmetric, and the discharge plug is on the east
side of the wall. Placed as the press brake is (`side` = the way the player looks).

**Anchors** (in `assets/seraphhorizons/config/amalgampan-rig.json`): `powerCell` and `powerFace`; `pan.pos`,
the middle of the pan's floor (`[0.5, 0.375, 0.5]`: grinding sounds and particles, around the cone);
`operatorSide` (`north`). Nothing for item transfer (above).

**Fitted parts and contents** (the rig's `requires`). The frame item carries everything with `requires`
null: the oak stand with its bridge-tree, the sheet-iron pan with its bead and discharge plug, the cast-iron
cone in its floor and the footstep under it. The **build order** is #718's: the frame (a grid recipe),
then `muller` and `shoes`, one right-click with the item each:

| Order | `requires` | Item | Draws | Taken back |
|---|---|---|---|---|
| 1 | `muller` | `game:rod-iron`, `-meteoriciron` or `-steel` | The muller: the rod is the spindle (as the mandrel station's rod is its mandrel), and the hub, the four arms and the oak coupling at its foot come with it, as #718 has the arms come with it | Only by breaking the pan |
| 2 | `shoes` | `game:metalplate-iron` or `-steel` | The four shoes under the arms' ends, riding the muller. Their texture code is `shoe`, which the renderer sets to the plate's metal | Only by breaking the pan: they don't wear |
| — | `mercury` | The contents | The mercury poured in: a pool 0.4 deep on the floor | The batch, not a part |
| — | `slurry` | The contents | The pan charged and working: crushed gold or silver quartz and water with the mercury worked through it, a grey-brown pulp 1.6 deep, the shoes mostly in it and the cone's foot under it, so a loaded pan reads from across the room | as above |
| — | `water` | The contents | A thin film of water standing over the charge (drawn with `slurry`), 0.15 over the pulp, parted round each shoe and swept round with the muller (below, "The water film") | as above |
| — | `amalgam` | The contents | The worked charge: the amalgam paste, 0.8 deep | as above |

Until the muller is fitted, nothing turns: the cone and the footstep stand empty, and an axle below meets
nothing. With the muller and no shoes, the arms turn clear of the floor and of every content. The contents
are drawn one at a time, or none (the pan empty), the water film only over the charge, as `requires`
values gameplay sets, with no driver, the
way the other hand stations express their work (the press brake's `platelead` and `platecopper`).

**Why the coupling comes with the rod.** It turns with the spindle (it is the spindle's foot), so it
cannot be the frame's; and a coupling with no spindle above it would have nothing to hang from. #718's
table had the crank and its oak handle come with the rod; the pan driven from below has neither, and the
coupling, the oak that meets the axle, takes their place in the stage.

**No arch.** #718's recipe makes "the bearing cone, the arch and the rivets" of the nails and strips. The
arch belonged to v1's top-driven pan: its crank sat over the pan on the spindle's top, so the spindle needed
an upper bearing over the rim. Driven from below, as the Washoe pan was, the spindle is carried by two
bearings fixed to the frame, 3.1 apart: the footstep on the bridge-tree under the pan and the cone's neck in
it, with the muller over the neck, as the Washoe pan's hollow cone carried its shaft and driver. An arch
would be a third bearing in line (the shaft over-constrained), drive nothing, and stand over the pan where
the player pours in and scoops out. So it is not built, and the nails and strips stand for the iron the
frame does have: the cone, the footstep, the discharge plug's boss and the pan's rivets. The recipe need not
change; only #718's wording does ("Open for the owner").

**The lid.** The cell's collision box covers the station to the spindle's top (0.63), and the rig puts the
collision-only `lid` (`with_lids`) at that height over the whole cell, as the press brake's: the player
walks over the pan as over a slab, and cannot step down into the mercury. It stays out of the selection
boxes. No cover is drawn over the pan: a Washoe pan's wooden cover kept the steam's heat in, and this pan
is not heated; a cover would hide the work and be in the way of loading by hand.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The wood is the game's debarked oak (the coupling too: a shaft that continues
a vanilla wooden axle stays wood), the pan the game's riveted sheet iron (`game:block/metal/sheet/iron1`,
whichever plates made it), the cone, the footstep and the muller its iron plate (`game:block/metal/plate/iron`),
the shoes the same iron plate under their own code `shoe` (the renderer sets it to the fitted plate's metal:
`game:block/metal/plate/iron` or `game:block/metal/plate/steel`), and the amalgam the lead solder ingot
(`game:block/metal/ingot/leadsolder`, a dull mottled grey).

**The mercury is drawn as Expanded Matter draws it.** Expanded Matter's `em:mercuryportion` (3.8.1,
`assets/em/itemtypes/liquid/mercury.json`) uses no texture of its own: it is the game's
`block/stone/rock/andesite2` with the game's `block/stone/rock/kimberlite1` blended over it, Lighten, a
speckled blue-grey. The pan uses the same two game textures, so its mercury is the bucket's, and nothing of
Expanded Matter's is copied or shipped: the pack's mod references the game's own assets, as every model
here does, and needs no credit or licence from Expanded Matter (GPL-3.0) for it; the README names it as
the look's source. A shape file can only name one texture per code, so the shape names the base
(`game:block/stone/rock/andesite2`) and the blocktype adds the overlay (see "Gameplay"). Baking the blend
into a PNG of our own was not done: that would be redistributing a modified copy of the game's art.

**The charge's slurry** is the mod's own texture, `seraphhorizons:block/liquid/slurry`, drawn by
`tools/slurry_texture.py` to sit beside that mercury: 32 × 32 with the same fine per-pixel grain as the
game's rock textures, a grey-brown base, the soft swirls of a stirred pulp (low waves of whole periods,
so it tiles) and a scatter of small beads of mercury showing through, from a fixed hash, so every run
writes the same pixels. It is the whole charge in one layer: a Washoe pan's charge was a thick pulp of
crushed ore and water, the mercury worked through it, not ore settled under clear water, so the water is
in the pulp; the owner's 2026-10-14 ruling adds a film of water over it as a state of its own, `water`
(below). The film is the game's **water portion** (`game:block/liquid/waterportion`), the texture its
barrels and buckets draw water in, as the jig's and the rocker's water are.

`tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/amalgampan.json` | The whole station, both fitted parts, the three contents and the water film (143 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/amalgampan_frame.json` | The static frame only (67 elements), the shape of the block `seraphhorizons:amalgampan-frame-{side}` (#718's recipe output), as `pressbrake_frame.json` is `pressbrake-frame-{side}`'s. The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/amalgampan-rig.json` | The cell, the power cell and face, the anchors and the part rig (7 parts). |
| `assets/seraphhorizons/textures/block/liquid/slurry.png` | The charge's slurry texture (`seraphhorizons:block/liquid/slurry`). |
| `tests/AmalgamPan/rig-reference.json` | Every part's matrix at a spread of shaft angles, from the reference maths. |

Elements by part: the muller 14, the shoes 4, each content 8, the water film 34, the frame 67; drawn
complete and empty 85, with any content 93, charged under water 127.

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
to y 7.9), standing above the pulp: the spindle runs up through it, and its neck is the spindle's
upper bearing, as in the Washoe pan.

**The muller** (stage 1). **The spindle** (iron, apothem 0.5, y 1..10.1) carries the muller over the cone
and comes down through the cone, the floor and the **footstep** (an iron bearing, apothem 1.1, y 2.8..3.6,
on the bridge-tree), then through the bridge-tree to the **coupling**: the vanilla axle's cross profile,
two oak bars 3 by 1.2 crossed, from the block's bottom face (y 0) to y 1.4, 0.2 under the bridge-tree, the
spindle's iron end set 0.4 into it. An axle in the block below meets it at the bottom face. The spindle's
two bearings, the footstep and the cone's neck, are 3.1 apart; the axle below holds it endwise, as the
quern's does its stone. Keyed on the spindle over the cone are the muller's hub (y 8.1..9.7, 0.2 over the
cone) and its four arms (y 8.15..8.95, out to 5.4 from the axis, at 45° to the cell's axes at rest).

**The shoes** (stage 2): under each arm's end a shoe (2.4 across, 2.3..5.6 from the axis) standing on the
floor, its top against the arm's underside, riding the muller.

**The contents** are flat layers on the floor, inside the wall: their edge runs 0.05 into the wall (no
gap shows at the wall) and their underside is sunk in the floor. The mercury is 0.4 deep, the amalgam
0.8 and the charge's pulp 1.6, over the cone's foot and most of the shoes; the shoes stand out of all
three (to y 8.15), and the cone's top (y 7.9) is above them.

**The water film** (`water`, over the charge) is a sheet from y 7.32..7.4 (in the pulp, under every strip
of its top) to 7.67..7.75, up to 0.15 over the charge and under the cone's top and the arms (y 8.1). A still sheet over
the floor could not keep clear of the shoes, which stand on the floor up through the charge into the arms,
and putting it over the arms would drown them and the hub. So the film **rides the muller**: it is cut
away round each shoe, 0.1 clear on every side and end (`SLOT_MARGIN`), and turns with the muller, as the
muller sweeps the pulp and its water round the pan; the slot is the shoe's wake, a line of pulp showing
along each side. Clear of the shoes by construction, it never meets them at any angle. Drawn in the
muller's frame with the shoes on the axes, it is 34 boxes: an arm of film
along each shoe's line inside its slot (to the open square, 1.4 across, left round the spindle inside the
cone's neck) and outside it (to the wall), small pieces by the core, and between each two shoes a quadrant
stepped out in six bands whose corners all lie between 6.4 and 6.9 from the axis. That ring is inside the
wall at every angle (its inside is 6.2 to 6.32 out, its outside 7), so the turning film's stepped edge
is always hidden in the wall and no gap opens at the wall; its middle is hidden in the cone's neck, clear
of the spindle. Each box is grown 0.005 every way across (`WATER_LAP`), so neighbours lap by 0.01: the
shape file's turned boxes, each rounded about its own centre, would otherwise meet with hairline cracks
(up to 0.0001), and the stepped bands with T-junctions, through which the pulp could sparkle. Each box's
top and underside sit 0.0025 under the last's (`WATER_STEP`, as the round parts' strips step), so no two
laps share a plane. The slots are cut 0.005 wider to keep the 0.1 clear of the shoes after the lap.

Every moving part (θ the spindle's angle):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Muller: spindle, coupling, hub, four arms (`muller`) [muller] | The axle below, by the coupling; or the player, by right-click held | The shoes | `rotate` y about the pan's axis (`[0.5, 0, 0.5]`), ratio 1, on θ |
| Shoes (`shoes`) [shoes] | Ride the muller (bolted under its arms) | The contents (grinding) | none (ride) |
| Mercury (`mercury`) [mercury] | Static | — | none |
| Charge (`slurry`) [slurry] | Static | — | none |
| Water film (`water`) [water] | Rides the muller (parted round the shoes) | — | none (ride) |
| Amalgam (`amalgam`) [amalgam] | Static | — | none |
| Frame (`frame`) | Static | — | none |

There are no gears: the spindle turns with the axle, once a turn. By hand nothing is drawn turning but
the muller and its shoes, as on the quern, where the player's hand turns the stone with nothing to show
for it.

**Period.** The Washoe pan's shaft came up through the hollow cone from gearing under the pan, driven by
a line shaft; here the vanilla axle stands in for that shaft and its gearing, as on every machine in the
pack. The muller hangs on the spindle's top by its hub, with no separate driver or adjusting screw. Its
shoes were replaceable iron castings bolted under the muller; here they are plates, and they don't wear.
A charged Washoe pan was a pulp of crushed ore, water and mercury, kept moving by the muller; the charge
state is drawn so. The water film over it stands for the free water a pan was run with when the pulp was
thinned, swept round with the muller and parting at the shoes.

### Regenerating

```sh
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py              # stdlib only; rewrites the five files
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/AmalgamPan/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 20 s and is deterministic (two runs into two folders are byte-identical, logs
included). `tools/validate_amalgampan.py` holds the checks; every run checks its output and exits
non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part; every `requires` in the pan's vocabulary (the two stages, the three contents, `water`, or null).
- **Stages:** the fitted stages in #718's order, the muller then the shoes; the muller carries the one
  `rotate` and rides nothing; the shoes ride the muller with no driver of their own; each content a still
  layer of its own `requires`; the water film its own `requires`, riding the muller with no driver; the
  `shoe` texture code on the shoes alone, and the shoes wearing nothing
  else, so setting it to the plate's metal changes the shoes and nothing more.
- **Containment:** nothing leaves the block over a whole turn, every 5°, with each contents.
- **Textures** by role: oak stand, bridge-tree, coupling and the plug's bung; `pan` for the floor, wall and
  bead; iron for the cone, the footstep, the plug's boss and the muller; `shoe` for the shoes; each content
  its own.
- **Turning:** the pan's axis stays put; the muller turns by exactly θ (ratio 1); the shoes' and the water
  film's matrices are the muller's at every θ; nothing else moves.
- **Supports:** the spindle runs through the footstep and the cone's neck (found by `supports`), at least
  0.3 inside each, at rest and turned an eighth and a quarter, the two 3 or more apart; the muller's hub
  just clear of the cone.
- **Coupling:** the power cell is `[0,0,0]` and the power face down; the spindle's foot is the axle's oak
  cross profile (3 by 1.2), centred on the pan's axis, from the block's bottom face up past the spindle's
  iron end, 0.1 or more under the bridge-tree.
- **Shoes:** at every degree of a turn, each shoe flat on the floor and up against its arm, the arm's end
  over it, and at least 0.3 clear of the wall and of the cone.
- **Contents:** each on the floor, its edge just into the wall; the mercury under the paste's level, the
  paste under the charge's, the charge under the cone's top and the shoes' tops (the arms' undersides, so
  the arms clear every content with the shoes off too).
- **Water film:** under the cone's top and the arms, 0.05 or more over the charge's top and its underside
  in the charge; its boxes side by side, never overlapping; every box at least `SLOT_MARGIN` (0.1) clear
  of every shoe, at five angles; its edge out past the wall's inside all round (a ray every 1.5°) and inside
  the wall's outside; and, at five angles, every point of the open pan at its height (inside the wall,
  outside the cone's neck, on a 0.2 grid) under the film or in a shoe's slot, so the charge shows nowhere
  but round the shoes.
- **Clearances:** over a turn every 10°, with each contents, no two parts touch except the intended
  contacts listed in `ALLOWED` (the spindle down through the footstep and the bridge-tree and up through
  the floor and the cone, the contents in the pan, round the shoes and round the spindle in the cone; the
  water film's edge in the wall, its middle in the cone's neck and its underside in the charge, but never
  the shoes or the muller). A
  stage left off only takes parts away, so the complete pan's clearances hold for every stage.
- **Swept paths** (full runs): the same at every degree of a turn, with each contents (1800 poses).
- **Nothing floats:** every frame element joined to the ground (the frame stands alone, before any stage).
- **No z-fighting** (full runs): no coplanar overlapping faces at fifteen poses, with each contents shown
  alone (`shown`) and the charge under its water. Before the fix there are 34 pairs at each pose, the water
  adding none (32 of the floor's strips against the
  wall's foot, and the coupling's two bars where they cross at the bottom face); the fix removes the faces
  pressed against their own part (20: never a shoe's or an arm's, which are different parts, so the arms'
  undersides are drawn with the shoes off) and insets what is left (one coupling bar's two ends by 0.015);
  none remain.
- **Files:** every texture declared; one cell, with a lid; the power cell and face; no work; no anchor
  for item transfer (nothing ending in `Side` but `operatorSide`, nothing named `infeed`, `output` or
  `outfeed`); the shipped model is the checked one (no move: the build frame's corner is the controller's).

The cell's boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
other machines' are: one box over the whole station, to the spindle's top (0.63), with the lid at that
height.

### Rig schema (`amalgampan-rig.json`)

The press brake's schema (`../PressBrake/README.md`, "Rig schema") without its work or its infeed and
output, plus a power cell and face as the powered machines have: nothing is added to the shared rig maths.

- **Inputs.** θ alone, the spindle's angle (radians). The muller part rotates about +y by θ (ratio 1:
  counter-clockwise seen from above as θ grows); the shoes and the water film ride it. The contents and the frame have no
  driver.
- **Keys.** `cells` (one, with its box and a `lid`), `powerCell` and `powerFace`, `operatorSide`, `pan`
  and `parts`. `requires` values: `muller`, `shoes`, `mercury`, `slurry`, `water`, `amalgam`, or null. The texture
  code a renderer sets: `shoe` (the fitted plate's metal); the blocktype sets the mercury's overlay (see
  "Gameplay").

**In the viewer** (`site/models.json`, `amalgam-pan`): θ is the "Muller turned" slider over one turn, and
Play turns it at 1.5 s a turn (40 turns a minute) by its input speed. The power cell is drawn with an
arrow up into its down face. A **Build** select steps through the frame, "1: Muller (a rod)" and "2:
Shoes (a plate): complete" (opening complete; each hint names its items); a **Contents** select (Empty, Mercury, Charged (slurry), Charged, water over it, Amalgam, each with a
hint), opening on Charged, water over it, waits on the built pan (`needs` the muller and the shoes); a **Shoes** texture
select draws them in iron or steel plate, each its texture's mean colour from the game's files (iron
`#695d56`, steel `#807f7c`). Coloured by texture, the contents have colours of their own apart from the
pan's iron (`site/src/lib/model-view.ts`): the mercury the mean of Expanded Matter's blend (a dark
blue-grey), the slurry the mean of its texture (grey-brown), the amalgam a warm grey, and the water the rocker's
water blue (`#6f9fcf`, the same line the rocker's branch adds).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `muller_*` (`spindle`,
`couplinga`, `couplingb`, `hub`, `arm1`..`arm4`), `shoe_1`..`shoe_4`, `mercury_*`, `slurry_*`,
`water_*` (`ins`, `inn`, `ine`, `inw`, `outs`, `outn`, `oute`, `outw`, `coree`, `corew`, `se1`..`sw6`),
`amalgam_*`, and `fr_*` for the frame (`leg`, `apron`, `bearer`, `rail`, `bridgetree`, `floor`, `wall`,
`rim`, `cone_base`, `cone_neck`, `plug_boss`, `plug_bung`, `footstep`). Hand edits are lost when the
script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's model
viewer (the standalone build), at every stage and contents state and through a turn, from several angles.
No one has yet looked at it in a client.

1. **The contents do not turn or change.** A layer is drawn or not; nothing shows the charge turning to
   amalgam but the change of state.
2. **The shoes stand in the layers.** They pass through the pulp as they turn; nothing is pushed aside.
   The water film turns with them rather than standing still, so its texture turns with the muller too.
3. **Collision is one box.** The space under the pan is solid to walk into; selection takes a click
   anywhere on the block. The box is the complete pan's at every stage.
4. **Round parts are polygons:** the pan has 16 sides, the hubs, spindle and footstep 8.
5. **The mercury is the bucket's look only with the blocktype.** The shape alone names plain andesite (the
   overlay is the blocktype's); the viewer draws the blend's mean colour.
6. **The shoes are iron in the shape.** Steel shows only where the renderer sets `shoe` (and in the viewer's
   Shoes select).

## Open for the owner

1. **The arch.** Not built (above, "No arch"): driven from below, the spindle has its two bearings and an
   arch would carry nothing. Either amend #718's recipe note to "the bearing cone, the footstep and the
   rivets" (the ingredients unchanged: two nails and strips), or have the arch back as a bridge across the
   rim with a third bearing at the spindle's top, which is redundant and stands where the player loads.
   The recommendation is the first.
2. **The lid.** The rig's collision deck over the cell at the spindle's top (0.63) is kept, as the press
   brake has one, so nobody steps into the mercury; no cover is drawn over the pan (it is not heated). Drop
   the deck if the pan should be walked into like the mandrel station, or ask for a cover if wanted.
3. **The muller's stage takes the coupling.** #718's table has the rod bring "the arms, crank and oak
   handle"; driven from below there is no crank or handle, and the oak coupling at the spindle's foot comes
   with the rod in their place. The table's wording wants updating to match.
4. **Concentrate or crushed quartz.** #718's body still says "gold and silver concentrate plus mercury";
   its 10-13 player card says crushed gold or silver quartz, with water, and so do the model, the viewer
   and this README. The body's first lines may want the card's words.
5. **The water film turns with the muller.** Ruled in (2026-10-14) as a film over the charge, clear of the
   shoes' swept path or riding if the geometry needs it: it needs it, since the shoes stand on the floor
   up through any still sheet over the charge. It is drawn riding the muller, slotted round the shoes
   (above, "The water film"). Accepted as built (2026-10-14). In the viewer it is its own Contents option,
   "Charged, water over it", which the Contents select opens on.
6. **Contents wait for a built pan** in the viewer (the Contents select `needs` the muller and the
   shoes), as the crucible furnace's pots wait for its build. Whether gameplay refuses mercury in a pan
   with no muller is #718's call.
7. **The pan's sheet is always iron**, whether the frame was made from iron or steel plates (#718's `P`);
   only the shoes follow their plate's metal.

## Gameplay

Built (#718; the mod's README, "Ore processing: the amalgam pan"), as below: the mercury refused until
the pan is built, the muller's coupling a connector that only the built pan's work loads, the hand's
pace 40 turns a minute. The rig's `buildStages` (the muller, then the shoes) are what the shared build
file, `config/builds/amalgampan.json`, is checked against. Originally: #718 (crushed gold or silver quartz, water and mercury in, amalgam out, lifting the × 0.7
free-metal penalty; the amalgam retorted in the still, #726). What the rig offers it, after the quern's
pattern, the hand stations' fitted stages (`../PressBrake/README.md`, "Stages") and the powered machines'
rig keys:

- **No automated input or output.** The pan must refuse insertion and extraction by hoppers, chutes and
  any other item transfer, both ways: the player pours in the mercury, the crushed quartz and the water
  and takes the amalgam out by hand. Only the power is automatic.
- **Blocks and recipe.** `seraphhorizons:amalgampan-frame-{side}` draws `amalgampan_frame.json`, rotated
  per side as the press brake's frame is; its textures are every texture of both shapes, `shoe` and the
  mercury's composite included. The frame's grid recipe is #718's (`PNP,BHB`: two iron or steel plates,
  two nails and strips of iron, meteoric iron or steel, two oak planks, a hammer).
- **Stages**, one item a click, in order, the next missing stage the only one a click fills (the press
  brake's `OutOfOrder`, `AlreadyFitted`, `NotAPart`): 1 `muller` (`game:rod-iron`, `-meteoriciron`,
  `-steel`), 2 `shoes` (`game:metalplate-iron`, `-steel`). They come back only by breaking the pan; the
  only consumable is the mercury.
- **Power.** The block carries a mechanical-power consumer whose only connector is the down face
  (`powerFace`), as the quern's `MPConsumer` with `HasMechPowerConnectorAt` (the quern's is up or down);
  until the muller is fitted the axle below meets nothing (no connector, or a connector that turns
  nothing: gameplay's choice). Powered, θ is the network's angle about the vertical axle, 1:1 (the quern's
  top takes `AngleRad` as it is). The shared `MillMotion.NativeShaftAngle` takes horizontal input shafts
  only (x or z); the pan's renderer needs the vertical case, the sign at which a vanilla `-ud` axle draws
  itself, which the gameplay adds.
- **By hand.** While the player holds right-click (the quern's `OnBlockInteractStep`), the renderer
  advances θ at a pace the gameplay sets (the quern's stone turns 40° a second); nothing else is drawn.
- **Parts and contents.** The renderer draws the `muller` part when the muller is fitted and the `shoes`
  (riding it) when the shoes are, by the shared rig maths (`Machines/Core/RigAnimation.cs`), and one of
  `mercury`, `slurry` (the pan charged and working) and `amalgam` as the batch stands, or none, and
  `water` (riding the muller) over `slurry` while the charge has its water. The frame
  shape is the block's.
- **Textures.** The renderer sets the `shoe` code to the fitted plate's metal (`game:block/metal/plate/iron`
  or `game:block/metal/plate/steel`), as the press brake's renderer sets `edge`. The blocktype gives the
  `mercury` code Expanded Matter's composite, so the pool is the bucket's mercury:
  `"mercury": { "base": "game:block/stone/rock/andesite2", "blendedOverlays": [ { "base": "game:block/stone/rock/kimberlite1", "blendMode": "Lighten" } ] }`;
  `slurry`, `water` and `amalgam` are the shape's own paths.
- **Tests.** `tests/AmalgamPan/AmalgamPanRigTests.cs` (the shipped rig through the shared parser, its
  vocabulary, the stages and the shoes' ride, the power cell and face, no work, no item transfer anchor,
  and every pose of `tests/AmalgamPan/rig-reference.json` replayed through `Machines/Core`),
  `tools/tests/test_amalgampan_model.py` and `site/test/amalgampan.test.ts` (the generated files against
  their own rules and the site's rig maths, and the viewer's Build, Contents and Shoes selects).
