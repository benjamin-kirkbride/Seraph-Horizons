# Cylinder borer

Part of the Seraph Horizons mod (`../README.md`): the steam engine's cylinder, bored true (#639). A
mechanically powered **cylinder boring mill** after John Wilkinson's of 1774-75, the machine whose bore, Watt
wrote, did not err "the thickness of an old shilling" in a 50-inch cylinder. A cast **cylinder** lies fixed in a
timber **cradle**, bedded in two **V-blocks** and held down by **chains**; a stiff iron **boring bar** runs right
through it, carried in a **bearing at each end** (Wilkinson's improvement on Smeaton's cantilevered borer, which
wandered), and is turned from the vanilla axle by a **pinion and a great wheel**, 1:4: a slow, heavy cut. A
**cutter head** keyed on the bar by a feather (it turns with the bar and slides along it) carries four
**cutters set by wedges**; it is drawn through the bore by a **control rod** from a **crosshead** that runs on
the ways of a long timber **bench** at the far end, and the crosshead is pulled by a **rope** wound on a
**drum** that a falling **weight** turns: a **weighted feed**, as Wilkinson's was. As the cutters cross the bore,
its cast skin gives way to the bright bored face. Two cylinders: a **small** one (a Watt engine's) and a
**large** one (a Cornish engine's, a wider bore and a heavier wall), for which the V-blocks are wound down on
**screw jacks** and the cutters set out, so either lies concentric with the bar.

This pass is the model only, finished (every part fitted): the generator and its validation (`tools/`), the
shapes, the rig and its reference poses, and the site viewer's entry. There is no gameplay yet: no blocks,
items, recipes or C#. The build stages are to be settled with the owner ("Open for the owner").

**The period machine and this one.** In the reference drawing (Wrexham Heritage Services' simplified drawing of
the 1775 machine, used as reference only) a water wheel turns the bar through gears at the wall, and the far
end's frame carries a hand lever for moving the head. Here, as on every machine in the pack, the power comes
from a vanilla axle at a power cell, and the pinion and great wheel stand for the drawing's "gears to control
the speed of the boring machine shaft". The machine runs unattended, so the head is fed by a weight, and the
period handle is on the crosshead, where it pushes the head back for the next cylinder (see "The feed").

**A MachineOil machine.** The two bar bearings each carry a sight-feed cup whose level is the rig's `oil` input,
the fill (0..1) of the machine's MachineOil tank: one tank, two faces. Cast iron was bored dry; the oil is the
bearings'. The cups have no fill face or anchor of their own.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator; the
generic half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one
(no new driver: every motion is an existing one).

## The machine

**Footprint.** 16 cells: 2 wide (x), 2 high (y), 4 long (z), the long axis being the bar's. In the native frame
with the controller at `[0,0,0]`: x 0..1, y 0..1, z 0..3. The controller is the bench end's west cell at ground
level, `[0,0,0]`, the block the player clicks: the operator's end, with the feed's drum, its weight and the far
bearing's oil cup. The machine runs south from it: the bench (z 0..2), the cradle with the cylinder (z 2..3), the
drive (z 3). Placed as the draw bench is (the block's `side` the way the player looks, the bench end nearest
them), the axle comes in at the far end, the cylinder is fed from the player's left (native east) and comes off
to their right (native west). Placement is gameplay's to settle.

**The work** (voxels; the castings are 12-gons with a flat at the top and the bottom):

| Cylinder (k) | Bore (across the flats) | Cast skin (the cored hole) | Outside | Flanges | Length | In the cradle |
|---|---|---|---|---|---|---|
| Small, a Watt engine's (1, thin) | 11 | 10 (0.5 deep) | 15 | 17.4 | 15 | z 36.6..51.6 |
| Large, a Cornish engine's (2, thick) | 13 | 12 (0.5 deep) | 19 | 21.4 | 19 | z 32.6..51.6 |

Both share their drive-end face (z 51.6), where the head starts. The provisional item codes (#639) are in the
rig's `bore` (`blanks`, `cylinders`); the items do not exist yet.

**Power.** The vanilla axle comes in on the south face of `[0,1,3]` (the upper west cell at the drive end), along
z at the cell's centre (x 8, y 24 voxels), into the entry shaft. The power cell is in the upper layer because the
pinion on the entry shaft meshes the great wheel on the bar (axis x 16, y 18): their centres are 10 apart (a 6-8-10
triangle), the pitch radii 2 and 8.

**Anchors** (in `assets/seraphhorizons/config/cylinderborer-rig.json`): `powerCell` and `powerFace`; `infeedSide`
east (a chest or hopper beside the cradle feeds cylinder blanks) and `outputSide` west with `output.pos` (the west
face beside the cradle, low: a bored cylinder comes off there, or into a container beyond it); `chips.pos` (the
bore's mouth at the drive end, at the rough bore's bottom: chips and dust) and `drip.pos` (the far cup's feed pipe,
where it enters the bearing). No oil anchor: the oil is MachineOil's. The rig's `bore.box` gives each cylinder's
bounds: the cells' boxes leave the work out (see "Rig schema").

**Fitted parts** (the rig's `requires`, **provisional**: the stages are the owner's to settle). The frame carries
everything with `requires` null: the sills and cross sills, the bench with its ways, the bench end's posts and top
beam, the drum shaft's bearings, the entry shaft with its pinion and its bearings, the bar's pedestals, the
cradle's V-blocks, jacks and hook bars, and the two oil cups.

| `requires` | Item (provisional; #639's first list) | Draws |
|---|---|---|
| `bearings` | Two bar bearings (iron, smithed) | The bar's bearing blocks on their pedestals |
| `bar` | The boring bar (iron rod work) | The bar, its feather, its two collars and the great wheel keyed on its drive end |
| `feed` | The feed (#639's third item) | The crosshead with its shoes, push handle, control rod and yoke; the drum shaft with its two drums; the weight; the two ropes |
| `cutterhead` | The cutter head with its cutters, the wearing kit (as the gear cutter's `gearcutterkit`) | The head (body, hubs, the yoke's groove, four wedges) and its four cutters |
| `blanksmall`, `blanklarge` | The work: the small or the large cylinder | The casting, its cast skin and its two chains |

The viewer shows `blanksmall` only with the small cylinder chosen and `blanklarge` only with the large one
(`requiresClass`); a renderer shows one of them while a cylinder of that size is in the cradle.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. Textures are the game's, referenced: oak (`game:block/wood/debarked/oak`), iron
(`game:block/metal/plate/iron`), steel (`sheet-plain/steel1`), the casting (`game:block/metal/corroded/normal1`),
its cast skin (`corroded/rusty1`) and its bored face (`sheet-plain/steel1`, as `bored`), brass, rope, the glass in
the Transparent render pass, and the oil (`game:block/liquid/honey`). A future frame block's `textures` must name
every code of `cylinderborer.json`, as the draw bench's does (a renderer draws the parts with the block's texture
source). `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/cylinderborer.json` | The whole machine, every moving part and both cylinders (714 elements). |
| `assets/seraphhorizons/shapes/block/cylinderborer_frame.json` | The static frame only (84 elements). |
| `assets/seraphhorizons/config/cylinderborer-rig.json` | Footprint, anchors, the work, the bore's figures and the part rig (73 parts: 40 quarters of cast skin, 10 rope pieces). |
| `tests/CylinderBorer/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up, z north
to south) in voxels, measured from the machine box's north-west bottom corner. The controller is the build frame's
own corner cell, so the shipped files are the build frame divided by 16.

**The frame.** Two oak sills run the machine's length (x 0.5..4 and 28..31.5, y 0..3), joined by eight oak cross
sills between them, each carrying something: the far bearing's pedestal, the bench's two pairs of legs, the two
V-blocks' jacks, the chains' hook bars, and the entry shaft's two pedestals (the bar's drive pedestal shares the last).
At the north end, the **bench end**: two oak posts (z 1..4.5) up to a top beam (y 27..29.5), the drum shaft in iron
bearings on the posts, the far bar bearing on its pedestal between them, and a cross beam carrying the bench's rails.
The **bench**, the drawing's "sturdy frame": two oak rails (x 5..8 and 24..27, y 8..11) with iron ways on them, from the
bench end (z 4.5) to the cradle (z 32.4), on legs at their middle and their near end.

**The drive.** The entry shaft (the vanilla axle's oak cross profile into its outer bearing, then a round iron shaft)
runs in two iron bearings on oak pedestals, either side of its pinion (8 teeth, module 0.5). The pinion meshes the
**great wheel** (32 teeth: a cast spoked wheel, its rim, six spokes and hub) keyed on the bar's drive end: the bar turns
a quarter of the axle, the other way.

**The bar** (an iron octagon 4 across, z 0.3..63.5) runs from beyond the far bearing (z 1..4.4), where two collars hold it
endwise, through the bench, the crosshead's portal, the cylinder and the head, to the drive bearing (z 61.2..63.8) beyond
the great wheel. A steel **feather** along its top flat (z 6..57.4) keys the head to it.

**The cutter head** (cast iron, a 12-gon 9 across, z 52..54.8 at rest) slides on the bar's feather. Ahead of it (north)
a hub carries the **yoke's groove** between two lips; a short hub behind. Four **cutters** stand in radial slots at its
leading face, a quarter turn apart: each an iron holder with a steel bit, its tip 0.005 inside the small bore, held by a
steel **wedge** beside it. For the large cylinder the cutters are set out 1 voxel (to 0.008 inside its bore): the
operator's setting, eased in with the cylinder (`present`).

**The feed.** The **crosshead** is an oak portal on iron shoes that slide on the bench's ways: a base across the ways,
two uprights either side of the bar, a top rail over it. An iron boss on its base holds the **control rod**, which runs
south under the bar, through the bore, to the **yoke**: a steel eye round the head's groove. The crosshead's **rope** is
tied to an eye on the top rail's north face and runs level, north, to the top of the **big drum** (radius 2) on the drum
shaft at the bench end, onto which it winds. The **small drum** (radius 1) on the same shaft pays out the **weight's**
rope from its north side: the weight hangs between the far bearing and the east post. So the weight, falling, turns
the drum, the big drum winds the crosshead north, and the rod draws the head through the bore, twice as far as the
weight falls (10.5 voxels in a large bore). The feed is not geared to the bar: the cutters bear on the cut as hard as
the weight pulls, and the head advances as fast as they take metal, which is the gameplay's pace (`bore.turnsPerBlock`,
drawn nowhere). **The handle** (an oak grip on two iron arms on the crosshead's north face) is how the head is pushed
back south for the next cylinder, pulling the rope off the big drum and winding the weight back up; by the end of a
bore the crosshead has come up to the bench end, so the handle is where the operator stands. In the rig the head comes
back with the cylinder's presence p as it is cleared.

The crosshead's rope is drawn in pieces: a fixed one from the drum to the crosshead's nearest reach, and seven the
crosshead takes up into its top rail as it comes (each rides with it from when its north face reaches the piece). The
weight's rope is a fixed piece above the weight's eye and four pieces that start inside the weight, under its cap, and
each rides down with it until it reaches its place in the rope.

**The cradle.** Two oak **V-blocks** (z 38.6..41 and 47.4..49.8), their faces rising 30 degrees, bed each cylinder on the
two flats either side of its bottom one; for a cylinder of outside apothem R concentric with the bar, the V's bottom is
R / cos 30 below the bar's axis. The V-blocks stand on four iron **screw jacks** (lead 1), two each: authored for the
small cylinder, they are wound down 2.31 for the large one, each nut turning 2.31 turns and running down its screw, so
both lie concentric with the bar. Two **chains** (z 43 and 45.4) go over each cylinder's upper flats, 0.42 off them, and
down its sides to the **hook bars**: iron bars across the cradle on three legs each, which either cylinder's chains hook
over.

**The bore.** Each cylinder's cast skin (the cored hole, 0.5 thick inside the bored face) is drawn in five slices along
the bore, each in four quarters of three staves (the top, the west side, the bottom, the east side). A quarter stays where
it was cast until the cutters reach its slice; while the cutters cross the slice (from their leading face reaching its
drive-end edge to their trailing face leaving its far edge) it sinks 0.8 radially into the wall, and from then on it lies
wholly inside the wall, so the bright bored face shows. W runs from 0 (the cutters 0.4 outside the drive-end face) to
17/16 (small) or 21/16 (large) blocks, when their trailing face is 0.4 past the far end: every slice is cut by then.

Every moving part (θ the axle angle, W the head's travel in blocks, k the cylinder: 1 small, 2 large, p its presence,
oil the tank's fill):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft and pinion (`entry`) | The vanilla axle | The great wheel | `rotate` z 1 (θ) |
| Bar, feather, collars, great wheel (`bar`) [bar] | The pinion, 8:32 | The head, by its feather | `rotate` z −1/4 (θ) |
| Cutter head: body, hubs, groove, wedges (`head`) [cutterhead] | The bar's feather (it turns); the yoke (it travels) | The cutters | `rotate` z −1/4 (θ); `gauge` slide z −W |
| Cutters (`cutter1`..`cutter4`) [cutterhead] | Ride the head; set out by their wedges for the large bore | Cut the skin | `gauge` slide 1 voxel outward for the large cylinder (`present`); ride `head` |
| Crosshead, shoes, handle, control rod, yoke (`feed`) [feed] | The rope (forward); the handle (back, as the cylinder is cleared) | The head, by the yoke in its groove | `gauge` slide z −W |
| Drum shaft, big drum, small drum (`drum`) [feed] | The weight's rope, on the small drum | The crosshead's rope, on the big drum | `gauge` rotate x −8 rad a block (the travel over the big drum's radius) |
| Weight (`weight`) [feed] | Its own weight | The small drum | `gauge` slide y −W/2 |
| Crosshead's rope pieces (`xrope1`..`xrope7`) [feed] | Taken up into the crosshead's top rail as it comes | (none) | `gauge` slide z, each with the crosshead from when it reaches the piece |
| Weight's rope pieces (`wrope2`..`wrope4`) [feed] | Ride down inside the weight to their places | (none) | `gauge` slide y |
| The ropes' fixed pieces (`feedropes`) [feed] | (fixed) | (none) | none |
| V-blocks (`saddles`) | The jacks' nuts | The cylinder | `gauge` slide y −2.31 for the large cylinder (`present`) |
| Jack nuts with tommy bars (`jack1`..`jack4`) | Turned for the cylinder (the operator) | The V-blocks | `gauge` rotate y −2.31 turns and slide y −2.31 for the large cylinder (`present`) |
| Oil in the cups (`oillevel`) | The machine's oil tank (MachineOil) | (shows it) | `stretch` y, 0.05 → 1.44 tall (oil) |
| Cast skin quarters (`ss1t`..`ss5r`, `sl1t`..`sl5r`) [blanksmall, blanklarge] | The cutters, crossing their slice | (none) | `gauge` slide 0.8 outward, over the cutters' crossing |
| Cylinders (`blanksmall`, `blanklarge`), their chains (`chainsmall`, `chainlarge`) [blanksmall, blanklarge] | Fixed in the cradle | (none) | none |
| Bar bearings (`bearings`) [bearings] | Fixed on their pedestals | Carry the bar | none |
| Frame (`frame`) | Static | (none) | none |

**The gear train and the feed.**

| Stage | Teeth / radii | Ratio |
|---|---|---|
| Pinion (entry shaft) to great wheel (bar) | 8 : 32, module 0.5, pitch radii 2 and 8, centres 10 apart | the bar −1/4 of the axle |
| Weight's rope on the small drum (r 1), crosshead's rope on the big drum (r 2) | 1 : 2 | the head travels twice the weight's fall |

Toothed wheels and what they mesh (nothing toothed meshes nothing): the pinion and the great wheel. The drums, the
collars, the hubs, the jack nuts and the cutter head are plain.

### Regenerating

```sh
python3 mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/CylinderBorer/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 40 seconds, most of it the swept paths and the z-fighting fix.
`tools/validate_cylinderborer.py` holds the checks; every run checks its output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; no
  part but the frame that a renderer would never draw (one with no `requires`, ride or driver).
- **Containment:** nothing leaves the 2 × 2 × 4 box over 21 poses of both cylinders.
- **Anchors:** the entry shaft meets the south power face at the power cell's centre; the output on the west face beside
  the cradle, the infeed east, every anchor inside the machine.
- **Textures** by role: oak timber, the axle's continuation, the V-blocks, the drums and the crosshead; iron castings,
  bearings, gears, the bar, the head, the crosshead's shoes, the weight and the chains; steel bits, wedges, the feather
  and the yoke; the casting outside with its bored face towards the bar, the rough skin; rope; a glass and brass oiler
  with oil in it.
- **Gearing:** the pinion's and the great wheel's centres are the sum of their pitch radii apart, and the arcs they roll
  are equal and opposite (finite differences of the posed rig), either way round; no pinion tooth runs more than 0.12
  into a wheel tooth over 40 angles; the head turns with the bar.
- **Cradle:** each cylinder concentric with the bar, its two bedding flats on the V-blocks' faces as set for it (within
  1e-5); the cutters set 0.005 (small) and 0.008 (large) inside its bore; the chains on its upper flats, down to their
  hook bars; each jack's nut turning its drop over the lead, under the V-block for both cylinders, on its beam for the
  large one.
- **Feed:** the head, the yoke, the rod and the crosshead travel together, W blocks; the yoke in the head's groove; the
  big drum's turn times its radius is the travel and the weight falls the small drum's share; the crosshead's rope whole
  from the big drum's top (level, tangent to it) to the crosshead, every piece taken up inside its top rail; the weight's
  rope whole from the small drum to the weight's cap, the rest inside the weight; the head between the bar's bearings
  over its whole travel.
- **Bore:** every skin quarter in place until the cutters reach its slice, and from when they have passed it wholly inside
  the wall (every corner, edge middle and face centre of it inside a wall stave or a flange); the cut within the head's
  travel, all of it done by the end.
- **Supports:** the bar in two bearings (the bearings on their pedestals), the entry shaft in two, the drum shaft in two;
  the control rod held in the crosshead's boss and the yoke at both ends of the travel; the bench's rails on the far beam
  and two legs each.
- **Oiler:** each cup's level 0.05 tall at oil 0, 1.44 at 1 and linear between, inside the glass; a pane over each side,
  every pane in the Transparent pass (`checks.sight_glass`); the drip anchor at the far cup's pipe.
- **Nothing floats:** every frame element joined to the ground.
- **Clearances:** over 38 poses (both cylinders, each skin slice's window, the loading) no two parts touch except the
  intended contacts listed in `ALLOWED`; the cutters touch only the skin slice they are crossing.
- **Clearing** *(the cylinder taken away)*: at the end of a bore, with the work no longer drawn, the head, the feed, the
  V-blocks and the cutters ease back with p touching nothing.
- **Swept paths** (full runs): the head, its cutters, the rod and the crosshead every 1/64 block of the travel, both
  cylinders, five bar angles (its feather all round): 770 poses.
- **No z-fighting** (full runs): no coplanar overlapping faces at five poses of both cylinders, of what can be seen:
  the other cylinder's set, the skin sunk into the wall and the rope inside the crosshead or the weight are left out
  of both the fix and the check (`shown`).
- **Files:** every texture declared; lids over every column; the shipped model is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the other machines'
are, leaving out the work (both cylinders, their skins and chains): a loaded cylinder's box is the rig's `bore.box`.

### Rig schema (`cylinderborer-rig.json`)

The draw bench's schema (`../DrawBench/README.md`, "Rig schema"), with nothing added to the shared rig maths: a `work`
quantity, gauges (windowed and `present`), rotations on θ, and a `stretch` on the oil.

- **The work.** `{ "name": "cutter travel", "unit": "blocks", "step": 0.015625, "end": { "thin": 1.0625, "thick": 1.3125 } }`.
  W is the cutter head's travel through the bore: 0 with its cutters 0.4 outside the drive-end face, the end with them
  0.4 past the far end. Every motion that follows the head is a `gauge` with one window, from 0, its ease the large
  bore's end (1.3125) and its amount that end's worth of motion, so it moves the same per block for both cylinders and
  the small one's job ends part way along it; the skin's windows are the cutters' crossing of each slice.
- **Inputs.** θ (the axle; the bar turns −θ/4, so the viewer's θ slider spans four turns); W, k and p; the oil. k is
  the cylinder (1 small, 2 large, 0 none); p its presence, eased as it is loaded and cleared, k held while p eases out.
  The gameplay advances W with the shaft at `1 / bore.turnsPerBlock` a turn from 0 when a blank goes on, holds it at the
  cylinder's end when it is bored, clears it (p eases out: the head comes back) and starts the next at W = 0. The head
  turns with θ either way; the weighted feed does not care which way the bar turns. ψ (travel) is read by nothing.
- `present` gauges set the V-blocks, the jacks and the cutters for the large cylinder; their amount for the small one is 0.

**Keys.** `cells`, `powerCell` and `powerFace`, `infeedSide`, `outputSide`, `output`, `chips`, `drip`, `work`, `bore`
(`turnsPerBlock`, `blanks`, `cylinders`, `box`) and `parts`. `requires` values: `bearings`, `bar`, `feed`,
`cutterhead`, `blanksmall`, `blanklarge`, or null. `bore.turnsPerBlock` (64, provisional) is the gameplay's pace: the
feed is weighted, so no ratio between the axle and W is drawn and any pace shows true. `bore.box` is each cylinder's
bounds in blocks from the controller, `[x0, y0, z0, x1, y1, z1]`, for its collision box while it is loaded.

**In the viewer** (`site/models.json`, the site's model page; `cylinder-borer`): the work's slider reads in blocks and
stops at 1.0625 or 1.3125 for the cylinder chosen, the size select names the cylinders, each cylinder's set shows only
with its own size (`requiresClass`), the oil has a slider, and Play turns the axle at the reader's input speed (starting
at 4 a second) and moves W with it at `bore.turnsPerBlock`. A standalone copy for review:
`cd site && node --import tsx scripts/standalone-viewer.ts cylinder-borer ../build/cylinderborer-viewer.html`.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`; `bar_*` and `wheel_*`
(`bar`); `brg_*` (`bearings`); `head_*`; `cut1_*`..`cut4_*` (`cutter1`..`cutter4`); `feed_*`; `drum_*`; `weight_*`;
`xr1_*`..`xr7_*` (`xrope1`..`xrope7`); `wr2_*`..`wr4_*` (`wrope2`..`wrope4`); `ropes_*` (`feedropes`); `saddle*`
(`saddles`); `jack1_*`..`jack4_*`; `oillevel_*`; `bls_*` (`blanksmall`), `chs*` (`chainsmall`), `bll_*`
(`blanklarge`), `chl*` (`chainlarge`); `ss1t_*`..`ss5r_*` and `sl1t_*`..`sl5r_*` (the skin's quarters: slice 1 at the
drive end, t top, l west, b bottom, r east); and `fr_*` for the frame (the oil cups `fr_cupfar_*` and
`fr_cupdrive_*`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in a client;
there is no renderer yet.

1. **Box teeth.** The pinion's and the wheel's teeth are boxes; they mesh by their pitch circles, with up to 0.12 of
   overlap allowed where a box cannot follow a tooth form.
2. **The skin sinks in quarters.** A slice's four quarters sink radially into the wall over the cutters' crossing, a
   coarse stand-in for the cut; while a slice is being crossed, it is part sunk all along. The skin stops 0.03 short of
   the cylinder's end faces, a lip seen at rest.
3. **One traverse a cylinder.** The cutters take the cast skin to the bore in one pass; there is no separate finishing
   pass (see "Open for the owner").
4. **The bar turns either way** with the axle, the cutters with it; a cutter cuts one way only, so with the axle
   reversed they would rub, as the gear cutter's cutter would. The weighted feed is indifferent.
5. **Ropes in pieces.** Pieces of the crosshead's rope are taken up into its top rail and the weight's rope is paid out
   of the weight, rather than winding round the drums; the drums' wraps are drawn still.
6. **The head comes back with p.** As a bored cylinder is cleared, the head, the crosshead, the drum and the weight go
   back with p (the handle pushing, the weight wound up) over the gameplay's ease, a fraction of a second; a renderer
   should stop drawing the cylinder when it is delivered (its skin would come back with p too).
7. **The V-blocks are boxes:** each face a rotated box through a base, and the large cylinder's bedding flats overhang
   the faces' outer ends a little.
8. **The lids.** Every column's top cell gets a lid at the highest top in the top layer, the bench end's top beam (29.5
   voxels): over the cradle that is an invisible deck about 5 voxels above the large cylinder.

## Open for the owner

Decided provisionally, to be settled:

1. **The stages.** The `requires` above are #639's first list (bearings, bar, feed, cutter head), with the great wheel on
   the bar and the pinion with the frame; their order and what each costs are the owner's. The frame's own gearing (the
   pinion; the wheel if it moves to the frame) would follow #473's precedent of stainless gears in a machine's frame,
   or be cast iron as Wilkinson's were.
2. **The feed: weighted, not geared.** Chosen because it is Wilkinson's own, has a visible cause both ways (the weight
   forward, the handle back), needs no rectifier (the bar may turn either way: the weight pulls regardless) and no
   ratio between the axle and the head's travel, so the gameplay's pace can be anything and shows true. It bears on
   #639's ordering question: **the feed takes no gear at all** (a rope, two drums and a weight), so nothing in it needs
   the gear cutter, and the borer can come before it, as historically. The alternatives were a rack or a screw geared off
   the bar (a toothed feed, from the gear cutter or smithed, a rectifier so the head never backs, and a drawn pace the
   gameplay must match) and Wilkinson's internal rod in a hollow bar (no room for the rod's travel in four blocks).
3. **Passes.** The model bores in one traverse, W the head's travel in blocks, rather than #639's `TurnsPerPass` ×
   passes. Several passes (a coarse cut, then a finishing cut) would need the head brought back between them; the model
   could show that as a stroke and a return per pass (the draw bench's windows), the handle winding it back, if passes
   are wanted.
4. **Sizes.** Two, as the table above: the large bore 2 wider than the small, its wall 1 thicker, 4 longer; both bored by
   one cutter head with its cutters set out 1 for the large one, and bedded by one pair of V-blocks wound down 2.31. The
   figures are the model's, not the engines' (ppex's engine cylinders are not measured here).
5. **The pace.** `bore.turnsPerBlock` 64 (68 axle turns a small cylinder, 84 a large one) is a placeholder for the
   gameplay's setting; cutter wear per cylinder (or per block) is gameplay's.
6. **Item codes** in `bore.blanks` and `bore.cylinders` are #639's proposals with `-small` / `-large`.
7. **Placement and anchors:** the controller at the bench end (the operator's end), the power at the far end's upper
   west cell (the axle one block up), the infeed east and the output west beside the cradle, the chips at the bore's
   drive end (a fixed point; it could ride the head instead).
8. **The oil** is the bearings' (two cups, one tank): a dry machine should mean more load (the mill's dry rule) rather
   than more cutter wear (the gear cutter's).
9. **The handle** is on the crosshead, which by the end of a bore stands at the bench end, rather than on the bench's end
   as in the drawing.
10. **The lids** (weak spot 8): kept on every column; the deck could be lowered over the cradle by lidding only some
    columns, as the rosser does.

## Tests

- `tools/tests/test_cylinderborer_model.py` (no game, no fetched mods, so CI runs it): the shipped rig parses with the
  shared maths and uses the `requires` vocabulary, every non-frame part drawable; every element has a part and every
  part an element; every face's texture declared (and, with `VINTAGE_STORY` set, in the game); every third reference
  pose is the rig's own maths; the cells are rebuilt from the shipped shape; the work is the head's travel (17/16,
  21/16 blocks); the head and the feed travel together and come back with p; the drum and the weight follow the head;
  the bar and the head turn a quarter of the axle; every skin quarter sunk by the end; both cylinders concentric with
  the bar, the V-blocks wound down and the cutters set out for the large one; 16 cells, lids, the power face, the
  anchors; the oil level follows `oil`.
- `site/test/cylinderborer.test.ts` replays every reference pose through the site's `rig.ts`, checks the inputs the
  viewer offers (four θ turns), the `requires` vocabulary and every element's part; `site/test/models.test.ts` checks
  the scenario against the rig.
- `make_shape.py` checks its own output every time it regenerates the model.
- No C# yet: the rig tests against the shared `RigAnimation` (as `DrawBenchRigTests`) come with the gameplay.
