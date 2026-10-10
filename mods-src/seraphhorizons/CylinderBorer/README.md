# Cylinder borer

Part of the Seraph Horizons mod (`../README.md`): the steam engine's cylinder, bored true (#639). A
mechanically powered **cylinder boring mill** after John Wilkinson's of 1774-75, the machine whose bore, Watt
wrote, did not err "the thickness of an old shilling" in a 50-inch cylinder.

What it bores is **the cylinder of Pipes and Power Expanded's engines**: the barrel of the Watt and Cornish engine
block (ppex's `Root/Cylinder`, the engine block's own shape, `selectiveElements` in its blocktype). Both engines
draw the same barrel, 10 voxels across with an 8 bore, 16 long, a band 12 across near its top and two steam ports on
one side; they differ in metal: ppex draws the Watt's iron (`sheet-plain/iron5`) and the Cornish's steel
(`sheet-plain/steel5`). So this machine bores **one cylinder in two metals**, and the rig's two classes are the
metal, not a size. The generator reads both engine shapes from `build/mods/ppex_*.zip` on every run, measures the
barrel, and fails if it no longer matches the cylinder modelled here.

The cast **cylinder**, square outside as ppex draws it and with a round bore, lies on its side in a timber
**cradle**: on two flat **bearers** between their cleats, held down by two **chains**. A slender iron **boring bar** runs right through it, carried in a **bearing at each end**
(Wilkinson's improvement on Smeaton's cantilevered borer, which wandered), and is turned from the vanilla axle by a
**pinion and a great wheel**, 1:4: a slow, heavy cut. A **cutter head** keyed on the bar by a feather (it turns
with the bar and slides along it) carries four **cutters held by wedges**; it is drawn through the bore by a
**control rod** from a **crosshead** that runs on the ways of a timber **bench** at the far end, and the crosshead
is pulled by a **rope** wound on a **drum** that a falling **weight** turns: a **weighted feed**, as Wilkinson's
was. As the cutters cross the bore, its cast skin gives way to the bright bored face.

This pass is the model only, finished (every part fitted): the generator and its validation (`tools/`), the
shapes, the rig and its reference poses, and the site viewer's entry. There is no gameplay yet: no blocks, items,
recipes or C#. The build stages are to be settled with the owner ("Open for the owner").

**The period machine and this one.** In the reference drawing (Wrexham Heritage Services' simplified drawing of
the 1775 machine, used as reference only) a water wheel turns the bar through gears at the wall, and the far end's
frame carries a hand lever for moving the head. Here, as on every machine in the pack, the power comes from a
vanilla axle at a power cell, and the pinion and great wheel on their timber portal stand for the drawing's "gears
to control the speed of the boring machine shaft" at the wall. The machine runs unattended, so the head is fed by
a weight, and the period handle is on the crosshead, where it pushes the head back for the next cylinder (see "The
feed").

**A MachineOil machine.** The two bar bearings each carry a sight-feed cup whose level is the rig's `oil` input,
the fill (0..1) of the machine's MachineOil tank: one tank, two faces. Cast iron was bored dry; the oil is the
bearings'. The cups have no fill face or anchor of their own.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator; the
generic half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one
(no new driver: every motion is an existing one).

## The machine

**Footprint.** 8 cells: 1 wide (x), 2 high (y), 4 long (z), the long axis being the bar's. In the native frame
with the controller at `[0,0,0]`: x 0, y 0..1, z 0..3. The controller is the bench end at ground level,
`[0,0,0]`, the block the player clicks: the operator's end, with the feed's drum, its weight and the far bearing's
oil cup. The machine runs south from it: the bench (z 0..2), the cradle with the cylinder (z 2), the drive (z 3).
Placed as the draw bench is (the block's `side` the way the player looks, the bench end nearest them), the axle
comes in at the far end at ground level, the cylinder is fed from the player's left (native east) and comes off to
their right (native west). Placement is gameplay's to settle.

**The work: ppex's engine cylinder.** Measured from ppex 0.7.1's `shapes/engine/watt.json` and `cornish.json`
(`Root/Cylinder`: the walls Cube21..24, the band Cube50..53, the ports Cube8 and Cube6, and the piston, `Root/Piston`'s
Cube74), and built to those figures (voxels):

| | ppex (both engines) | This model |
|---|---|---|
| Outside | 10 × 10 (square, walls 1 thick) | 10 × 10, square: four slabs as ppex's |
| Bore | 8 × 8 square (the piston is 8 × 8) | round: a 12-gon 8 across the flats, concentric with the bar, bright once bored; cast skin 7.2 across before |
| Length | 16 (y 10..26) | 16 (z 32..48, lying along the bar) |
| Band | 12 × 12, 2 long, at 11..13 from the bottom | 12 × 12 square, at 11..13 from its bottom end |
| Steam ports | 2 × 2 on the south face at 2..4 and 12..14 from the bottom | 2 × 2 bosses on top at 2..4 and 12..14, 2.5 proud |
| Covers | a top cover with the stuffing box, a bottom plate, inside the barrel | none: a bored casting is open at both ends; the covers come with the engine |
| Metal | Watt `game:block/metal/sheet-plain/iron5`, Cornish `.../steel5` | the same textures, as `castiron` (k = 1) and `caststeel` (k = 2) |

The cylinder lies with its bottom end north (towards the bench) and its top end, with the band, at the drive end;
its ports face up, clear of the bearers and the chains. Outside it is ppex's square barrel (the owner's ruling: it
should look like the engine's); inside, the bore is round, as a bored bore is. The square tube's four slabs give the
bore its top, bottom and side flats (their inner faces, 4 from the axis); eight staves within the square give its
other eight flats; four fillets close the square's inner corners behind them, so the section is solid from the bore
to the outside (a check samples it).

**Power.** The vanilla axle comes in on the south face of `[0,0,3]` (the drive cell, at ground level), along z at
the cell's centre (x 8, y 8 voxels), into the entry shaft, which runs under the bar: its pinion meshes the great
wheel on the bar (axis x 8, y 16) 8 above it, the pitch radii 1.6 and 6.4.

**Anchors** (in `assets/seraphhorizons/config/cylinderborer-rig.json`): `powerCell` and `powerFace`; `infeedSide`
east (a chest or hopper beside the cradle feeds cylinder castings) and `outputSide` west with `output.pos` (the west
face beside the cradle, low: a bored cylinder comes off there, or into a container beyond it); `chips.pos` (the
bore's mouth at the drive end, at the rough bore's bottom: chips and dust) and `drip.pos` (the far cup's feed pipe,
where it enters the bearing). No oil anchor: the oil is MachineOil's. The rig's `bore.box` gives the casting's
bounds: the cells' boxes leave the work out (see "Rig schema").

**Fitted parts** (the rig's `requires`, **provisional**: the stages are the owner's to settle). The frame carries
everything with `requires` null: the sills and cross sills, the bench with its ways, the bench end's posts and top
beam, the drum shaft's bearings, the far bearing's pedestal, the drive portal, the entry shaft with its pinion and
its bearings, the cradle's bearers and the chains' staples, and the two oil cups.

| `requires` | Item (provisional; #639's first list) | Draws |
|---|---|---|
| `bearings` | Two bar bearings (iron, smithed) | The bar's bearing blocks, on the far pedestal and the drive portal's transom |
| `bar` | The boring bar (iron rod work) | The bar, its feather, its two collars and the great wheel keyed on its drive end |
| `feed` | The feed (#639's third item) | The crosshead with its shoes, push handle, control rod and yoke; the drum shaft with its two drums; the weight; the two ropes |
| `cutterhead` | The cutter head with its cutters, the wearing kit (as the gear cutter's `gearcutterkit`) | The head (body, hubs, the yoke's groove) with its four cutters and their wedges |
| `blankiron`, `blanksteel` | The work: the iron or the steel casting | The casting, its cast skin and its two chains |

**Iron and steel: how the metal is shown.** The two castings are two sets of parts with the same geometry and
different texture codes: `blankiron` (its outside `castiron`, the game's `block/metal/sheet-plain/iron5`, ppex's Watt
cylinder's texture) and `blanksteel` (`caststeel`, `block/metal/sheet-plain/steel5`, the Cornish's). The bored face
(`bored`, `sheet-plain/steel1`) and the cast skin (`rough`, `corroded/rusty1`) are the same in both. The viewer shows
`blankiron` only with iron chosen and `blanksteel` only with steel (`requiresClass`, as the gear cutter shows each
master's blank and the draw bench each metal's hollow). In the game, a renderer would draw the set of the class k
of the casting in the cradle, k read from the blank's item code against the rig's `bore.blanks` (thin: the iron
blank, thick: the steel one), and the frame block's `textures` must declare `castiron` and `caststeel`. (The other
clean way, one set of parts with one texture code the renderer sets per metal, as the draw bench's die code `die`,
would halve the parts but not show the metal in the viewer.)

## Model

Everything in the model was made for this mod: no element of ppex's or any other mod's model is used, and ppex's
shapes are read only to measure them. Textures are the game's, referenced: oak (`game:block/wood/debarked/oak`), iron
(`game:block/metal/plate/iron`), steel (`sheet-plain/steel1`), the castings as above, rope, brass, the glass in the
Transparent render pass, and the oil (`game:block/liquid/honey`). A future frame block's `textures` must name every
code of `cylinderborer.json`, as the draw bench's does. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/cylinderborer.json` | The whole machine, every moving part and both castings (646 elements). |
| `assets/seraphhorizons/shapes/block/cylinderborer_frame.json` | The static frame only (79 elements). |
| `assets/seraphhorizons/config/cylinderborer-rig.json` | Footprint, anchors, the work, the bore's figures and the part rig (64 parts: 40 quarters of cast skin, 10 rope pieces). |
| `tests/CylinderBorer/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north
to south) in voxels, measured from the machine box's north-west bottom corner. The controller is the build frame's
own corner cell, so the shipped files are the build frame divided by 16.

**The frame.** Two oak sills run the machine's length (x 0.5..3.5 and 12.5..15.5, y 0..3), joined by seven oak cross
sills between them, each carrying something: the far bearing's pedestal, the bench's two pairs of legs, the two
bearers, and the entry shaft's two bearing posts. At the north end, the **bench end**: two oak posts (z 1..4) up to a
top beam (y 23..25.5), the drum shaft in iron bearings on the posts, the far bar bearing on its pedestal between them,
and a cross beam carrying the bench's rails. The **bench**, the drawing's "sturdy frame": two oak rails (x 3.5..5.5 and
10.5..12.5, y 7.5..10) with iron ways on them, from the bench end (z 4) to the cradle (z 31.8), on legs at their middle
and their near end. At the south end, the **drive portal**: two oak posts on the sills and a transom across them
(y 11.6..14.2) carrying the drive bearing, the entry shaft passing under it: the drawing's wall.

**The drive.** The entry shaft (the vanilla axle's oak cross profile in its outer bearing at the face, then a round
iron shaft) runs in two iron bearings on short oak posts, either side of its pinion (8 teeth, module 0.4). The pinion
meshes the **great wheel** (32 teeth: a cast spoked wheel, its rim, six spokes and hub) keyed on the bar's drive end,
north of the portal: the bar turns a quarter of the axle, the other way.

**The bar** (an iron octagon 2.4 across, z 0.3..58.4) runs from beyond the far bearing (z 0.8..3.8), where two collars
hold it endwise, through the bench, the crosshead's portal, the cylinder and the head, to the drive bearing (z 56..58.6)
on the portal beyond the great wheel. A steel **feather** along its top flat (z 4.9..52.4) keys the head to it.

**The cutter head** (cast iron, a 12-gon 6.4 across, z 48.4..50.6 at rest) slides on the bar's feather. Ahead of it
(north) a hub carries the **yoke's groove** between two lips; a short hub behind. Four **cutters** stand in radial slots
at its leading face, a quarter turn apart: each an iron holder with a steel bit, its tip 0.009 inside the bore, held by a
steel **wedge** beside it standing a little proud of the face.

**The feed.** The **crosshead** is an oak portal on iron shoes that slide on the bench's ways: a base across the ways,
two uprights either side of the bar, a top rail over it. An iron boss on its base holds the **control rod**, which runs
south under the bar, through the bore, to the **yoke**: a steel eye round the head's groove. The crosshead's **rope** is
tied to an eye on the top rail's north face and runs level, north, to the top of the **big drum** (radius 1.6) on the
drum shaft at the bench end, onto which it winds. The **small drum** (radius 0.8) on the same shaft pays out the
**weight's** rope from its north side: the weight hangs between the far bearing and the east post. So the weight,
falling, turns the drum, the big drum winds the crosshead north, and the rod draws the head through the bore, twice as
far as the weight falls (9 voxels a bore). The feed is not geared to the bar: the cutters bear on the cut as hard as
the weight pulls, and the head advances as fast as they take metal, which is the gameplay's pace
(`bore.turnsPerBlock`, drawn nowhere). **The handle** (an oak grip on two iron arms on the crosshead's north face) is
how the head is pushed back south for the next cylinder, pulling the rope off the big drum and winding the weight back
up; by the end of a bore the crosshead has come up near the bench end, where the operator stands. In the rig the head
comes back with the casting's presence p as it is cleared.

The crosshead's rope is drawn in pieces: a fixed one from the drum to the crosshead's nearest reach, and seven the
crosshead takes up into its top rail as it comes (each rides with it from when its north face reaches the piece). The
weight's rope is a fixed piece above the weight's eye and four pieces that start inside the weight, under its cap, and
each rides down with it until it reaches its place in the rope.

**The cradle.** The casting is square, so it sits flat: two oak **bearers** across the cradle (z 33..35.4 and
39.2..41.6, under the barrel between its ends and its band, x 1.5..14.5, on their cross sill and the long sills either
side), their tops level with the casting's flat bottom, 5 under the bar's axis, so the bore lies concentric with the
bar; an oak **cleat** on each bearer either side of the casting's foot keeps it from shifting sideways. Two **chains**
(z 37 and 46.9, clear of the ports and the band) go up its sides, over its top, 0.4 off its faces and bending at its
corners, and down to iron **staples** on the sills.

**The bore.** The casting's cast skin (the cored hole, 0.4 thick inside the round bored face) is drawn in five slices
along the bore, each in four quarters of three staves of the bore's 12-gon (the top, the west side, the bottom, the east
side). A quarter stays
where it was cast until the cutters reach its slice; while the cutters cross the slice (from their leading face reaching
its drive-end edge to their trailing face leaving its far edge) it sinks 0.7 radially into the wall, and from then on it
lies wholly inside the wall, so the bright bored face shows. W runs from 0 (the cutters 0.4 outside the drive-end face)
to 18/16 blocks (their trailing face 0.6 past the far end): every slice is cut by then.

Every moving part (θ the axle angle, W the head's travel in blocks, k the casting's metal: 1 iron, 2 steel, p its
presence, oil the tank's fill):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft and pinion (`entry`) | The vanilla axle | The great wheel | `rotate` z 1 (θ) |
| Bar, feather, collars, great wheel (`bar`) [bar] | The pinion, 8:32 | The head, by its feather | `rotate` z −1/4 (θ) |
| Cutter head with its cutters and wedges (`head`) [cutterhead] | The bar's feather (it turns); the yoke (it travels) | Cuts the skin | `rotate` z −1/4 (θ); `gauge` slide z −W |
| Crosshead, shoes, handle, control rod, yoke (`feed`) [feed] | The rope (forward); the handle (back, as the cylinder is cleared) | The head, by the yoke in its groove | `gauge` slide z −W |
| Drum shaft, big drum, small drum (`drum`) [feed] | The weight's rope, on the small drum | The crosshead's rope, on the big drum | `gauge` rotate x −10 rad a block (the travel over the big drum's radius) |
| Weight (`weight`) [feed] | Its own weight | The small drum | `gauge` slide y −W/2 |
| Crosshead's rope pieces (`xrope1`..`xrope7`) [feed] | Taken up into the crosshead's top rail as it comes | (none) | `gauge` slide z, each with the crosshead from when it reaches the piece |
| Weight's rope pieces (`wrope2`..`wrope4`) [feed] | Ride down inside the weight to their places | (none) | `gauge` slide y |
| The ropes' fixed pieces (`feedropes`) [feed] | (fixed) | (none) | none |
| Oil in the cups (`oillevel`) | The machine's oil tank (MachineOil) | (shows it) | `stretch` y, 0.05 → 1.34 tall (oil) |
| Cast skin quarters (`si1t`..`si5r`, `ss1t`..`ss5r`) [blankiron, blanksteel] | The cutters, crossing their slice | (none) | `gauge` slide 0.7 outward, over the cutters' crossing |
| Castings (`blankiron`, `blanksteel`), their chains (`chainiron`, `chainsteel`) [blankiron, blanksteel] | Fixed in the cradle | (none) | none |
| Bar bearings (`bearings`) [bearings] | Fixed on their pedestal and transom | Carry the bar | none |
| Frame (`frame`) | Static | (none) | none |

**The gear train and the feed.**

| Stage | Teeth / radii | Ratio |
|---|---|---|
| Pinion (entry shaft) to great wheel (bar) | 8 : 32, module 0.4, pitch radii 1.6 and 6.4, centres 8 apart | the bar −1/4 of the axle |
| Weight's rope on the small drum (r 0.8), crosshead's rope on the big drum (r 1.6) | 1 : 2 | the head travels twice the weight's fall |

Toothed wheels and what they mesh (nothing toothed meshes nothing): the pinion and the great wheel. The drums, the
collars, the hubs and the cutter head are plain.

### Regenerating

```sh
python3 tools/packtool.py fetch                                                  # the generator measures ppex's engines in build/mods
python3 mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py              # rewrites the four files
python3 mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

The generator is stdlib only and reads `build/mods/ppex_*.zip` (it stops, saying so, if the zip is not there; the
tests below read only the shipped files, so CI needs no zip). A full run takes about 40 seconds, most of it the swept
paths and the z-fighting fix. `tools/validate_cylinderborer.py` holds the checks; every run checks its output and
exits non-zero if one fails:

- **ppex's cylinder:** both engine shapes' barrels, measured from the zip, are the figures this model is built to
  (outside 10, bore 8, length 16, the band 12 across at 11..13, the ports 2 square at 2..4 and 12..14, the piston 8),
  square as ppex draws them, and the same in both engines; and each casting as built has those figures: square outside,
  10 × 10 round the bar's axis, 16 long, its band 12 × 12 square at 11..13, its ports in their places; its bore round
  and clear of the wall (no corner, edge middle or face centre of a wall element inside the 12-gon); and its section
  solid from the bore to the square outside (every point of a 0.1 grid between them inside a wall element: the staves
  and fillets leave no gap at the square's inner corners). If ppex changes its engine cylinder, the run fails.
- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; no
  part but the frame that a renderer would never draw (one with no `requires`, ride or driver).
- **Containment:** nothing leaves the 1 × 2 × 4 box over 21 poses of both metals.
- **Anchors:** the entry shaft meets the south power face at the power cell's centre; the output on the west face beside
  the cylinder, the infeed east, every anchor inside the machine.
- **Textures** by role: oak timber, the axle's continuation, the bearers, the drums and the crosshead; iron castings,
  bearings, gears, the bar, the head, the crosshead's shoes, the weight and the chains; steel bits, wedges, the feather
  and the yoke; each casting in its metal's texture (ppex's iron5 or steel5), the rough skin; rope; a glass and brass
  oiler with oil in it. Every bright face is a flat of the bore, facing the bar 4 from its axis, and each casting has
  twelve of them a twelfth of a turn apart: the bore is round.
- **Gearing:** the pinion's and the great wheel's centres are the sum of their pitch radii apart, and the arcs they roll
  are equal and opposite (finite differences of the posed rig), either way round; no pinion tooth runs more than 0.12
  into a wheel tooth over 40 angles; the head turns with the bar.
- **Cradle:** each casting concentric with the bar, its flat bottom on both bearers' tops (exactly) with the bearers
  under its whole width, its foot between the cleats; the chains 0.4 off its sides and top, down to their staples; the
  cutters 0.009 inside the bore.
- **Feed:** the head, the yoke, the rod and the crosshead travel together, W blocks; the yoke in the head's groove; the
  big drum's turn times its radius is the travel and the weight falls the small drum's share; the crosshead's rope whole
  from the big drum's top (level, tangent to it) to the crosshead, every piece taken up inside its top rail; the weight's
  rope whole from the small drum to the weight's cap, the rest inside the weight; the head between the bar's bearings
  over its whole travel.
- **Bore:** every skin quarter in place until the cutters reach its slice, and from when they have passed it wholly
  inside the wall (every corner, edge middle and face centre of it inside a wall stave); the cut within the head's
  travel, all of it done by the end.
- **Supports:** the bar in two bearings (the far one on its pedestal, the drive one on the portal's transom, the entry
  shaft passing 2.1 under it), the entry shaft in two, the drum shaft in two; the control rod held in the crosshead's boss
  and the yoke at both ends of the travel; the bench's rails on the far beam and two legs each.
- **Oiler:** each cup's level 0.05 tall at oil 0, 1.34 at 1 and linear between, inside the glass; a pane over each side,
  every pane in the Transparent pass (`checks.sight_glass`); the drip anchor at the far cup's pipe.
- **Nothing floats:** every frame element joined to the ground.
- **Clearances:** over 38 poses (both metals, each skin slice's window, the loading) no two parts touch except the
  intended contacts listed in `ALLOWED`; the cutters touch only the skin slice they are crossing.
- **Clearing** *(the cylinder taken away)*: at the end of a bore, with the work no longer drawn, the head, the crosshead,
  the drum and the weight ease back with p touching nothing.
- **Swept paths** (full runs): the head, its cutters, the rod and the crosshead every 1/64 block of the travel, both
  metals, five bar angles (its feather all round): 730 poses.
- **No z-fighting** (full runs): no coplanar overlapping faces at five poses of both metals, of what can be seen: the
  other metal's casting, the skin sunk into the wall and the rope inside the crosshead or the weight are left out of
  both the fix and the check (`shown`).
- **Files:** every texture declared; lids over every column; the shipped model is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the other machines'
are, leaving out the work (both castings, their skins and chains): a loaded cylinder's box is the rig's `bore.box`.

### Rig schema (`cylinderborer-rig.json`)

The draw bench's schema (`../DrawBench/README.md`, "Rig schema"), with nothing added to the shared rig maths: a `work`
quantity, windowed gauges, rotations on θ, and a `stretch` on the oil.

- **The work.** `{ "name": "cutter travel", "unit": "blocks", "step": 0.015625, "end": { "thin": 1.125, "thick": 1.125 } }`.
  W is the cutter head's travel through the bore: 0 with its cutters 0.4 outside the drive-end face, 1.125 with them 0.6
  past the far end, the same for both metals. Every motion that follows the head is a `gauge` with one window from 0,
  its ease the end and its amount the end's worth of motion; the skin's windows are the cutters' crossing of each slice.
- **Inputs.** θ (the axle; the bar turns −θ/4, so the viewer's θ slider spans four turns); W, k and p; the oil. k is the
  casting's metal (1 iron, 2 steel, 0 none); p its presence, eased as it is loaded and cleared, k held while p eases out.
  The gameplay advances W with the shaft at `1 / bore.turnsPerBlock[k]` a turn from 0 when a casting goes in, holds it
  at the end when it is bored, clears it (p eases out: the head comes back) and starts the next at W = 0. The head turns
  with θ either way; the weighted feed does not care which way the bar turns. ψ (travel) is read by nothing.

**Keys.** `cells`, `powerCell` and `powerFace`, `infeedSide`, `outputSide`, `output`, `chips`, `drip`, `work`, `bore`
(`turnsPerBlock`, `cylinder`, `blanks`, `cylinders`, `box`) and `parts`. `requires` values: `bearings`, `bar`, `feed`,
`cutterhead`, `blankiron`, `blanksteel`, or null. `bore.turnsPerBlock` (`{ "thin": 64, "thick": 96 }`, provisional: steel
bores slower) is the gameplay's pace: the feed is weighted, so no ratio between the axle and W is drawn and any pace
shows true. `bore.cylinder` is the casting's figures (outside, bore, length), ppex's. `bore.blanks` and `bore.cylinders`
are each metal's work in and out (provisional item codes). `bore.box` is the casting's bounds in blocks from the
controller, `[x0, y0, z0, x1, y1, z1]`, for its collision box while it is loaded.

**In the viewer** (`site/models.json`, the site's model page; `cylinder-borer`): the work's slider reads in blocks and
stops at 1.125, the size select names the metals ("Iron (a Watt engine's)", "Steel (a Cornish engine's)"), each metal's
casting shows only with it (`requiresClass`), the oil has a slider, and Play turns the axle at the reader's input speed
(starting at 4 a second) and moves W with it at iron's pace (`bore.turnsPerBlock.thin`). A standalone copy for review:
`cd site && node --import tsx scripts/standalone-viewer.ts cylinder-borer ../build/cylinderborer-viewer.html`.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`; `bar_*` and `wheel_*`
(`bar`); `brg_*` (`bearings`); `head_*` (the cutters are `head_cut1_*`..`head_cut4_*`); `feed_*`; `drum_*`; `weight_*`;
`xr1_*`..`xr7_*` (`xrope1`..`xrope7`); `wr2_*`..`wr4_*` (`wrope2`..`wrope4`); `ropes_*` (`feedropes`); `oillevel_*`;
`bli_*` (`blankiron`), `chi*` (`chainiron`), `bls_*` (`blanksteel`), `chs*` (`chainsteel`); `si1t_*`..`si5r_*` and
`ss1t_*`..`ss5r_*` (the skin's quarters: slice 1 at the drive end, t top, l west, b bottom, r east); and `fr_*` for the
frame (the bearers and their cleats `fr_bed*`, the oil cups `fr_cupfar_*` and `fr_cupdrive_*`). Hand edits are lost when the script
runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and side by side with ppex's engine
cylinder. No one has yet looked at it in a client; there is no renderer yet.

1. **Box teeth.** The pinion's and the wheel's teeth are boxes; they mesh by their pitch circles, with up to 0.12 of
   overlap allowed where a box cannot follow a tooth form.
2. **The skin sinks in quarters.** A slice's four quarters sink radially into the wall over the cutters' crossing, a
   coarse stand-in for the cut; while a slice is being crossed, it is part sunk all along. The skin stops 0.03 short of
   the casting's end faces, a lip seen at rest.
3. **One traverse a cylinder.** The cutters take the cast skin to the bore in one pass; there is no separate finishing
   pass (see "Open for the owner").
4. **The bar turns either way** with the axle, the cutters with it; a cutter cuts one way only, so with the axle
   reversed they would rub, as the gear cutter's cutter would. The weighted feed is indifferent.
5. **Ropes in pieces.** Pieces of the crosshead's rope are taken up into its top rail and the weight's rope is paid out
   of the weight, rather than winding round the drums; the drums' wraps are drawn still.
6. **The head comes back with p.** As a bored cylinder is cleared, the head, the crosshead, the drum and the weight go
   back with p (the handle pushing, the weight wound up) over the gameplay's ease, a fraction of a second; a renderer
   should stop drawing the casting when it is delivered (its skin would come back with p too).
7. **The bore's corners.** The round bore's eight diagonal flats are staves within the square, a little wider than a
   flat so they overlap at its corners; seen end-on the casting's face is the square ring with a 12-gon hole, its
   inner pieces meeting in steps a hundredth of a voxel deep where the z-fighting fix set their ends apart.
8. **Tight clearances in one block's width:** the weight hangs 0.15 from the far bearing, the two drums' flanges stand
   0.25 apart, the crosshead's boss 0.28 under the bar's feather; all checked, none generous.
9. **The lids.** Every column's top cell gets a lid at the highest top in the top layer, the bench end's top beam (25.5
   voxels): over the cradle an invisible deck 2 voxels above the casting's ports.

## Open for the owner

Decided provisionally, to be settled:

1. **The stages.** The `requires` above are #639's first list (bearings, bar, feed, cutter head), with the great wheel on
   the bar and the pinion with the frame; their order and what each costs are the owner's. The frame's own gearing (the
   pinion; the wheel if it moves to the frame) would follow #473's precedent of stainless gears in a machine's frame, or
   be cast iron as Wilkinson's were.
2. **The feed: weighted, not geared.** Chosen because it is Wilkinson's own, has a visible cause both ways (the weight
   forward, the handle back), needs no rectifier (the bar may turn either way: the weight pulls regardless) and no ratio
   between the axle and the head's travel, so the gameplay's pace can be anything and shows true. It bears on #639's
   ordering question: **the feed takes no gear at all** (a rope, two drums and a weight), so nothing in it needs the gear
   cutter, and the borer can come before it, as historically. The alternatives were a rack or a screw geared off the bar
   (a toothed feed, from the gear cutter or smithed, a rectifier so the head never backs, and a drawn pace the gameplay
   must match) and Wilkinson's internal rod in a hollow bar (no room for the rod's travel in four blocks).
3. **One cylinder, two metals.** ppex's two engines share one barrel, so there is one geometry; the classes are iron
   (the Watt's) and steel (the Cornish's, which ppex draws in steel; #639 asks whether the Cornish should move to iron).
   If both engines take one iron cylinder, the classes collapse to one and the steel set goes.
4. **Square outside, round bore** (settled by the owner): the casting is ppex's square barrel outside, its bore round
   inside, sitting flat on bearers with cleats rather than in V-blocks.
5. **The casting's details.** Its band and its two south-face ports are ppex's, the ports' bosses 2.5 proud (ppex's
   pipes stand 3 and 4 off); ppex's third port (Cube28, north face near the top) and its covers are left off.
6. **Passes.** The model bores in one traverse, W the head's travel in blocks, rather than #639's `TurnsPerPass` ×
   passes. Several passes (a coarse cut, then a finishing cut) would need the head brought back between them; the model
   could show that as a stroke and a return per pass (the draw bench's windows), the handle winding it back.
7. **The pace.** `bore.turnsPerBlock` 64 for iron and 96 for steel (72 and 108 axle turns a cylinder) are placeholders
   for the gameplay's settings; cutter wear per cylinder is gameplay's.
8. **Item codes** in `bore.blanks` and `bore.cylinders` are #639's proposals with `-iron` / `-steel`.
9. **Placement and anchors:** the controller at the bench end (the operator's end), the power at the drive end at
   ground level (the axle along the machine), the infeed east and the output west beside the cradle, the chips at the
   bore's drive end (a fixed point; it could ride the head instead).
10. **The oil** is the bearings' (two cups, one tank): a dry machine should mean more load (the mill's dry rule) rather
    than more cutter wear (the gear cutter's).
11. **The handle** is on the crosshead, which by the end of a bore stands near the bench end, rather than on the bench's
    end as in the drawing.

## Tests

- `tools/tests/test_cylinderborer_model.py` (no game, no fetched mods, so CI runs it): the shipped rig parses with the
  shared maths and uses the `requires` vocabulary, every non-frame part drawable; every element has a part and every
  part an element; every face's texture declared (and, with `VINTAGE_STORY` set, in the game); every third reference
  pose is the rig's own maths; the cells are rebuilt from the shipped shape; the work is the head's travel (18/16
  blocks), steel's pace slower than iron's; the casting is ppex's cylinder (10 across, 8 bore, 16 long), each metal in
  ppex's texture; the head and the feed travel together and come back with p; the drum and the weight follow the head;
  the bar and the head turn a quarter of the axle; every skin quarter sunk by the end; the casting concentric with the
  bar; 8 cells, lids, the power face, the anchors; the oil level follows `oil`.
- `site/test/cylinderborer.test.ts` replays every reference pose through the site's `rig.ts`, checks the inputs the
  viewer offers (four θ turns), the `requires` vocabulary, the castings' textures and every element's part;
  `site/test/models.test.ts` checks the scenario against the rig.
- `make_shape.py` checks its own output, ppex's cylinder included, every time it regenerates the model.
- No C# yet: the rig tests against the shared `RigAnimation` (as `DrawBenchRigTests`) come with the gameplay.
