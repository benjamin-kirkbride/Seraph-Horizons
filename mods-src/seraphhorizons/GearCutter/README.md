# Gear cutter

Part of the Seraph Horizons mod (`../README.md`): the end-game machine of the gears epic (#484, the
machine is #480). A mechanically powered generating gear cutter on a knee-and-column bench frame turns
stainless gear blanks into stainless gears. A **temporal gear master** on the arbor rolls on a fixed rack as
the table feeds, so the blank beside it rolls under a cutter that is one tooth of the same rack, and
the cutter generates the master's tooth form in the blank (Bilgram 1884, Fellows 1896). A looted
**Jonas sub-assembly** is the dividing head: a planetary that indexes the arbor one tooth per pass. A
**sight-feed oiler** over the cutter, its reservoir the visible face of the machine's oil and a Jonas
**injection valve** spraying the cutter's teeth (#481).

This folder holds the model's generator and its rig (`tools/`), and the gameplay built against the
rig (`Core/`, `Game/`, "Gameplay" below; switch `GearCutter`, settings `GearCutterSettings`).

**A MachineOil machine (#488, #481).** The oiler's level is the rig's `oil` input, the fill (0..1)
of the cutter's MachineOil tank, passed as `RigInput.Oil` (C#) and `oil` (the viewer and the Python
maths). The oiler is only the tank's face: it has no fill face or anchor of its own and no pipe.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the mill and the rosser.

## The machine

**Footprint.** 8 cells: 2 deep (x), 2 high (y), 2 long (z), the long axis being the table's travel;
inside #480's "about 3×2×2". In the native, south-facing frame with the controller at `[0,0,0]`:
x −1..0 (the column at the back, west; the operator's side east), y 0..1, z 0..1. The controller is
the north cell of the operator's side at ground level, `[0,0,0]`, the block the player clicks. Placed
with the mill's rule (`Footprint.PlacedFacing`), the machine extends away from the player along their
line of sight, so the operator's side faces them.

**What shows and what is enclosed.** As a period machine shows them: the cone pulleys and the belt
off the axle, the cutter spindle and the cutter, the table with its feed post and the cam drums under
the knee, the dividing head with the Jonas housing and the master, the index's pusher, lever and pawls,
and the oiler. The feed's power train is enclosed: the **rectifier** (its two pairs of gears and the
idler) runs inside the hollow cast column, behind a removable door in the column's north side, and
the **clutch, worm and worm wheel** run in a cast **gearbox** on the bed at the north end, under a
removable top (slotted for the clutch lever's handle). The door and the top are one rig part,
`cover`; the enclosed parts are modelled and checked like every other, and the viewer shows them
with the cover hidden.

**Sizes.** The gears are the game's sizes, measured element by element (a shape's bounding box takes
in rotated pins and mover elements, and is too thick). The vanilla gear (`shapes/item/gear*`) and the
temporal gear (`shapes/item/gear-temporal.json`) are about 6.7 voxels across, read as 12 tips, and
their bodies are 1.4 thick; the temporal gear's twelve 0.2-square nubs stand out to 2.0 overall. The
large temporal gear (`shapes/block/machine/jonas/tempgear.json`) is about 10 across and 2.0 thick (its
rim 2.0, its teeth 1.9). One module serves both: **module 0.5**, 12 teeth at pitch radius 3 (7 across
the tips) and 20 at pitch radius 5 (11 across; the large temporal gear's own 11 chunky teeth cannot
share a module with 12 small ones). Thicknesses: the small blank and the small master **1.4** (the
master with the twelve nubs through it, 2.0 over them), the large master and the large blank **2.0**
(a blank is as thick as the gear it becomes).

**Power.** The vanilla axle comes in on the west face of `[-1,0,0]` (the back, north, bottom cell),
along x at the cell's centre, into the column. The cutter turns with the axle either way; the feed goes
through a rectifier, so the cut always advances (see the drive below).

**Anchors** (in `assets/seraphhorizons/config/gearcutter-rig.json`): `powerCell` and `powerFace`;
`infeedSide` north (a chest or hopper there feeds blanks) and `outputSide` south with `output.pos`
(where a finished gear drops); `chips.pos` (the cutter, for chips and sparks) and `drip.pos` (the mouth
of the injection valve's nozzle, just over the cutter's teeth, where its spray starts). There is no
oil anchor: the oil is MachineOil's.

**Fitted parts** (the rig's `requires`; the frame item carries everything with `requires` null: the
bed, column, gearbox casing, overarm, rack, oiler, knee with its drip tray, table, sliders, the
camshaft with its worm wheel, the dividing head's pedestal and arbor, the feed rectifier and the
lower cone). The **build order** (#480): the frame, then `spindle`, `feedscrew`, `camfeed`,
`camindex`, `liftcam`, `index`, `oiler`, `head`, `cutter`, and a master (`master` or `masterlarge`):

| Order | `requires` | Item (#480) | How it is made | Draws | Taken back |
|---|---|---|---|---|---|
| 1 | `spindle` | Cutter spindle with cone pulley (new item, steel) | Grid: 2 steel rods, 2 steel plates, metal parts (`game:metal-parts`: the keys and collars) | The head shaft with the upper cone, the belt, the mitre bevels, the spindle | Only by breaking the frame |
| 2 | `feedscrew` | Feed screw (new item, steel) | Smithing: 1 steel ingot | The worm and the clutch sleeve (in the gearbox) | Only by breaking the frame |
| 3 | `camfeed` | `game:jonasframes-gearbox02` (Jonas "Eccentric gearbox"), looted or converted | One eccentric gearbox | The feed cam drum and its groove | Only by breaking the frame |
| 4 | `camindex` | `game:jonasframes-gearbox02` (Jonas "Eccentric gearbox"), looted or converted | One eccentric gearbox | The index cam drum and its groove | Only by breaking the frame |
| 5 | `liftcam` | Lift cam (new item, steel) | Smithing: 1 steel ingot | The lift cam on its hub | Only by breaking the frame |
| 6 | `index` | Index (new item: lever, pawls, shield) | Grid: 2 steel rods, 2 steel plates, metal parts (`game:metal-parts`: the pawls, their pins and the lever's roller) | The shield, the lever with its roller, the pawl and the check pawl | Only by breaking the frame |
| 7 | `oiler` | `game:jonasparts-valve01` (Jonas "Alternate injection valve"), looted | Looted (Jonas) | The injection valve: its body, the feed from the reservoir, the nozzle over the cutter, and the plunger with its marble handle | Only by breaking the frame |
| 8 | `head` | Dividing head: `game:jonasframes-gears02` or `gears01` | Looted (Jonas) | The Jonas planetary: the housing (the ring, with the rim ratchet), the planets, the carrier, the sun with its detent disc, and the detent plunger | Only by breaking the frame |
| 9 | `cutter` | Cutter kit (its own issue), the wearing part | Its own issue | The formed cutter | When worn out it is spent; unworn, Ctrl + right-click |
| 10 | `master` | `game:gear-temporal`, the master for a 12-tooth gear | The game's | The temporal gear master | Ctrl + right-click after the cutter kit; breaking the frame drops it |
| 10 | `masterlarge` | `game:largegear-temporal`, the master for a 20-tooth gear | The game's | The large temporal gear master | Ctrl + right-click after the cutter kit; breaking the frame drops it |
| — | `blanksmall`, `blanklarge` | The stainless gear blank, the two-ingot large blank | Their own issues | The blank and its gap fills | The material being worked, not a part |
| — | `cover` | None: part of the frame as built | — | The gearbox's top and the column's door | Proposal: shown always in the game (a renderer may hide it to show the works); the viewer's checkbox hides it |

Two cam drums, two eccentric gearboxes, so a half-built cutter can show one drum; the lift cam is a
forged steel part of its own. The camshaft and its worm wheel are the frame's. The oiler's reservoir
(the cup, its oil and the bracket) is the frame's too: it is filled whether or not the valve is fitted. How the sliders' followers sit while their drum is absent is the
renderer's to decide; the viewer hides each drum with its stage.

The masters (#480, "The temporal gear as master", as settled): **one master station** takes either
master, one at a time, and the master fitted decides which blank the cutter accepts; changing over
means swapping the master. A master is an ordinary recoverable part like the mill's sash: it never
wears and is never consumed, Ctrl + right-click takes it back once the cutter kit is out, and breaking
the frame drops whatever is fitted. The gameplay follows the "Taken back" column, with one addition:
Ctrl + right-click takes a blank off the arbor between the kit and the master ("Gameplay").

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs
nothing from `build/mods`. The masters wear the game's temporal gear texture
(`game:item/resource/temporalgear`, referenced, not copied); their shapes are this generator's own,
drawn after the vanilla items' look (the small one's crossed bars, the large one's octagonal rim and
cross web, a gold boss). The Jonas head is drawn in the vanilla Jonas items' palette (cupronickel,
steel, gold) but is its own geometry, as is the injection valve (cupronickel and the game's
`block/stone/rock/whitemarble2`, after the Jonas valve item). What is made from the game's metal
parts is drawn in the same cupronickel (`game:block/metal/sheet/cupronickel1`): the index's pawl and
check pawl with their pins and the lever's roller, and the spindle's two collars. The spindle's keys
are not drawn (no element stands for them), so only its collars show the metal parts. The oiler's glass is
`game:block/glass/plain` and its oil `game:block/liquid/honey`, all referenced. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/gearcutter.json` | The whole machine, every moving part and the cover (1466 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/gearcutter_frame.json` | The static frame only (102 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/gearcutter-rig.json` | Footprint, anchors, the work, the cut's constants and the part rig (75 parts, 32 of them gap fills). |
| `tests/GearCutter/rig-reference.json` | Every part's matrix at a grid of poses, from the reference maths. |
| `assets/seraphhorizons/shapes/item/gearcutter/{spindle,feedscrew,liftcam,index,kit}.json` | The forged parts' item shapes: each the elements of the rig parts its stage draws, as they are at rest (the spindle's head shaft and spindle without the belt; the worm and clutch; the lift cam; the lever, pawls and shield; the cutter), centred on the item box's floor and scaled to fit 16 voxels (`ITEM_SCALE`: the spindle 0.74, the rest 1). The item types (`itemtypes/gearcutter/*.json`) draw them with the machine's textures. |

### How the machine works

Positions in this section are in the generator's **build frame**: the native axes (x west to east, y up,
z north to south) in voxels, measured from the machine box's north-west bottom corner. The shipped
files are the same model moved so the controller cell, build cell (1, 0, 0), is `[0,0,0]`:
shipped (blocks) = build / 16 − (1, 0, 0).

**The frame.** Oak sills on the floor, flush with the outline of everything on them (x 2..31, z 0.2..31), an iron bed on them (with pits under the worm wheel and the
lift cam), a hollow cast-iron column along the whole back (x 5..11, z 0.2..31) with an overarm on its
top (y 29.5..32, z 22..31) that reaches forward over the work and ends in a cross-head (z 6..22). The
column holds the rectifier; its north side is the cover's door. The **gearbox** stands on the bed at
the north end (x 11..30.7, y 2.5..11.8, z 1..5.75): walls on three sides, the column's front wall on the
fourth, the cover's top over it. The cross-head carries the cutter spindle's three bearings, the oiler
on its front face and, hung over the master, the **rack**: a fixed bar with 11 teeth of module 0.5 on
its underside (z 7.9..23.6), pitch line at y 24.8. The **knee** (z 6..31) slides up and down the
column's face on gibs; the **table** slides along z on the knee's ways and carries the dividing head.
The knee's low back part, under the master and blank, carries the **drip tray**.

**The arbor and the pass.** The arbor runs along x, through two bearings in the table's pedestal
(y 20.3 authored, z 12 with the table at rest). It carries, from the back: the master (centred on
x 14.4; 13.7..15.1 small, its nubs 13.4..15.4, and 13.4..15.4 large), the blank (centred on x 19.9;
19.2..20.6 small, 18.9..20.9 large; a thin gear is held by hubs between the collars), the pedestal, and
the Jonas head at
its front end, towards the operator. The master and the blank are the same gear and their gaps are at
the same angles. The rack's pitch line and the cutter's pitch line are one height. With the knee up,
the master's top meshes the rack and the blank's top is under the cutter (z 15.75). As the table
feeds +z, the master rolls on the rack (the arbor turns −travel / r, so its top pitch point stands
still against the rack), the blank rolls under the cutter in the same way, and the cutter, whose edge
is a rack tooth, generates the flanks of one gap: the tooth form is the master's. A pass is 7.5 voxels
of table travel: 143° of roll for the small gear and 86° for the large, enough that the cutter is
clear of the blank's tip circle at both ends. The table runs back the same way (a finishing pass in the
same gap), the knee drops 1.5, the master leaves the rack and the cutter the blank, and the Jonas head
indexes the arbor one tooth while it is free. A 12-tooth gear off a 20° rack is undercut at the root
(fewer than 17 teeth); the small gears' box teeth are drawn 0.1 thinner instead.

**The blank and the cutter.** A blank is drawn as the gear it becomes plus one fill per gap (`gsNN`,
`glNN`, two boxes each, `_lo` and `_hi`), and until the cutter reaches a gap its fill makes the rim
there plain disc: the fill runs from 0.15 inside the root to 0.02 past the tip, and each box is as
wide as it can be without its inner corners passing the middle of the teeth either side, so
neighbouring fills meet there and hide the teeth's flank edges. Seen face on, depths in from the
blank's faces: the teeth 0.025 (their outer boxes 0.045), the fills 0 (`_lo`) and 0.025 proud
(`_hi`), and the body, a polygon just inside the root (inradius root − 0.1), its strips 0.05 to 0.175
proud (small) or 0.225 (large). Faces that overlap facing the same way are never closer than
`ZF_GAP`, 0.025: the depth buffer cannot part faces a hundredth of a voxel apart at a few blocks, and
the blanks z-fought when their round bodies' strips were stepped 0.012. The masters, the hubs and the
cutter's body are stepped the same way, and what runs into a body (the hubs, the master's bars and
boss) runs 0.3 into it rather than ending at its face. A fill sinks 1.425
(addendum + dedendum + 0.3) radially through its pass, which puts it wholly inside the body,
inside its inradius and behind its faces: the gap opens and the fill is gone, with nothing to
see on the face. The cutter is an octagonal body 1.2 thick reaching r 2.06, just inside the radius
at which it would meet the blank's tip circle (3.2 − 1.125), and three bands of 12 teeth, each
overlapping the next and the body: r 2.8..3.2 at the rack tooth's tip thickness (0.33), 2.25..2.85 at
its thickness 0.35 up (0.585), and 1.91..2.3 at its pitch-line thickness (0.785), 0.1 inside the
flanks there because the blank's box teeth are not the generated form.

**The two masters, one station.** Both masters sit at the one station on the arbor. The large one's
pitch circle is 2 bigger, so for it the knee is set 2 lower: its **elevating screw** (lead 1, two
turns) stands on the lift cam's roller and through the knee's screw web, and turning it sets the knee
(and the index pusher's pad, which is set on its post the same way) for the master fitted. One rack and
one cutter height serve both: the rack is "adjustable" in the sense #480 asked for by the knee's
height, not by a second rack. The shield over the housing's rim ratchet is swung in for the large
master, so the same lever stroke drives the housing three rim teeth instead of five (an 18° step of
the arbor instead of 30°).

**The oiler.** The reservoir is the frame's: a sight-feed glass cup (1.6 × 1.4 × 1.5) between a brass
base and cap, a needle valve through the cap with a brass knob, on a brass bracket bolted to the
cross-head's front (east) face at the cutter's z; the bracket runs down past the base to carry the valve.
The oil in the cup is one element, `oillevel_oil`, authored 0.05 tall (empty) and stretched up by the
rig's `oil` input to 1.2 when full. The glass is four panes in the Transparent render pass (their
elements' `renderPass` 3, machinegen's `TRANSPARENT`; the block's own pass is opaque, which draws the
glass's faint texture solid and hid the level from every side but an open one), so the level shows from
every side.

The **injection valve** is the oiler's stage (`oiler`, the Jonas `jonasparts-valve01`), drawn after the
game's item (its cupronickel body, end bosses, side stem and white marble handle; its own geometry): a
cupronickel body (1.4 × 1.4 × 2) on the bracket under the reservoir, fed from the reservoir's base into
its top, a nozzle pipe west in under the cross-head ending in a downturned tip whose mouth is 0.6 over
the cutter's teeth (x 23.0, y 28.65), where the `drip` anchor is, and on the operator's side the plunger,
a stem with a marble handle across it, that pumps in and out 0.3 once a tooth (a `slide` on the work).
While the machine cuts with oil in the tank, the gameplay sprays oil from the nozzle's mouth (particles
at the `drip` anchor). The **drip tray** on the knee (5.8 × 19.6, with a rim) lies under either blank
all through the pass.

**The cycle, per tooth** (t is the fraction of a tooth; the camshaft turns once per tooth):

| t | What moves |
|---|---|
| 0.00..0.10 | The lift cam raises the knee 1.5: the master into the rack, the blank up to the cutter. |
| 0.09..0.10 | The detent rides out of the sun's notch. |
| 0.10..0.38 | The feed drum carries the table 7.5 voxels south: the master rolls, the cutter generates the gap; the sun floats at three times the arbor's roll. |
| 0.38..0.40 | Dwell at the end of the pass. |
| 0.40..0.68 | The table returns, rolling back (a finishing pass); the sun comes back to its notch. |
| 0.68..0.69 | The detent drops into the sun's notch. |
| 0.68..0.78 | The knee drops 1.5: the master leaves the rack, the cutter the blank. |
| 0.78..0.80 | The index pusher closes its gap to the lever's roller. |
| 0.80..0.89 | The pusher turns the lever 50°: 5° free, then the pawl drives the housing 45° (small, five rim teeth) or, past the shield, 27° (large, three); the sun is held, so the arbor turns two thirds of it: 30° or 18°, one tooth. |
| 0.89..1.00 | The lever springs back on the pusher, the pawl lifted over the rim's teeth; the pusher opens its gap. |

**The drive** (θ the axle's angle, ψ its travel either way, W the teeth cut):

- *Cone and belt off the axle.* The entry shaft (oak, the vanilla axle's cross profile, as far as the
  rectifier; a round steel shaft inside the column) carries the lower cone (radii 3.8, 3.2, 2.6) behind
  the column. An open belt runs up the back to the upper cone (2.6, 3.2, 3.8) on the head shaft (z 28),
  which runs through the column's top and along under the overarm to a 1:1 mitre pair that turns the
  cutter spindle (along z). The belt is on the first steps: the cutter turns 1.4615 times per axle
  turn. It turns with the axle, either way, as #480 has it.
- *The feed's rectifier, in the column.* The entry shaft also carries A1 (15 teeth) and A2 (12),
  module 0.3. A1 meshes B1 (15) directly, A2 meshes B2 (12) through an idler (12), held on a stud from
  the column's front wall: B1 turns −θ, B2 +θ, both loose on the feed shaft beside it (north, z 3.6),
  each on a one-way catch. Whichever turns forward carries the feed shaft, so the feed shaft turns
  forward at ψ, 1:1, whichever way the axle turns.
- *The clutch and the feed screw, in the gearbox.* The feed shaft leaves the column through its front
  wall into the gearbox. A dog clutch keyed to it engages the worm (the feed screw), loose on the same
  shaft, while a cut is running; its lever's handle comes up through the cover's slot, thrown in as
  the master and blank go on and out when the count is done. The single-start worm (module 0.5) turns
  the camshaft's 12-tooth wheel: the camshaft turns once per 12 axle turns, **one turn per tooth**.
- *The cams.* The camshaft runs along z in the bed under the knee, from the gearbox (where its wheel
  is) south: the index drum, the feed drum (barrel cams) and, at the south end, the lift cam (the
  elevating screw's roller rides it). Each drum's groove moves a slider in guides on its top: the feed
  slider's arm carries a post that runs between two guide bars on the table's front edge, so the table
  follows it along z and slides up and down the post with the knee; the index slider's arm carries the
  pusher's post at the front.

Every moving part (input θ the axle angle, ψ its travel, W the teeth cut, k the master: 1 the temporal
gear, 2 the large one, p its presence, oil the tank's fill):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Entry shaft, lower cone, A1, A2 (`entry`) | The vanilla axle | The belt; B1; the idler | `rotate` x 1 (θ) |
| B1 with its catch (`rectb1`), in the column | A1 | The feed shaft when the axle turns − | `rotate` x −1 (θ) |
| Idler (`idler`), in the column | A2 | B2 | `rotate` x −1 (θ) |
| B2 with its catch (`rectb2`), in the column | The idler | The feed shaft when the axle turns + | `rotate` x 1 (θ) |
| Feed shaft (`feedshaft`) | Whichever catch bites | The clutch sleeve | `rotate` x 1 (ψ) |
| Clutch sleeve (`clutch`) [feedscrew], in the gearbox | The feed shaft (keyed); its lever's fork | The worm, while in | `rotate` x 1 (ψ); `gauge` slide x 0.25, in from W 0 to the last tooth |
| Clutch lever (`clutchlever`) | The operator (in), the count (out) | The sleeve | `gauge` rotate z, the clutch's window |
| Worm (`worm`) [feedscrew], in the gearbox | The clutch | The camshaft's wheel | `rotate` x 12·2π per tooth (W) |
| Camshaft and worm wheel (`camshaft`) | The worm | The drums and the lift cam, keyed on it | `rotate` z 2π per tooth (W) |
| Feed cam drum and groove (`camfeed`) [camfeed] | Keyed to the camshaft | The feed slider | `rotate` z 2π per tooth (W), as the camshaft |
| Index cam drum and groove (`camindex`) [camindex] | Keyed to the camshaft | The index slider | `rotate` z 2π per tooth (W), as the camshaft |
| Lift cam and hub (`liftcam`) [liftcam] | Keyed to the camshaft | The elevating screw's roller | `rotate` z 2π per tooth (W), as the camshaft |
| Feed slider, arm and post (`feedslider`) | The feed drum's groove | The table | `gauge` slide z 7.5, a window per tooth |
| Index slider, arm and post (`indexslider`) | The index drum's groove | The pusher | two `gauge` slides z: the gap (0.3), the push (4.73) |
| Pusher pad (`pusher`) | Rides the index slider; set on its post for the master | The lever's roller | `gauge` slide y −2 for the large master (`present`) |
| Knee with the drip tray (`knee`) | The lift cam through the screw; the screw's setting | The table | `gauge` slide y 1.5, a window per tooth; `gauge` slide y −2 for the large master (`present`) |
| Elevating screw (`screw`) | The lift cam (its roller); turned to set the knee | The knee | `gauge` slide y 1.5 (with the knee's lift); `gauge` rotate y 2 turns for the large master (`present`) |
| Table, pedestal, guide bars, posts (`table`) | The feed post; rides the knee | Everything on it | `gauge` slide z 7.5, a window per tooth; rides `knee` |
| Arbor, nut, collars (`arbor`) | The master on the rack (the roll); the Jonas carrier (the index) | The master, the blank, the carrier | `gauge` rotate x: the roll (−2.5 or −1.5 rad); 20 `gauge` rotations, one per tooth, the index (−30° or −18°); rides `table` |
| Master (`master`) [master], large master (`masterlarge`) [masterlarge] | Keyed to the arbor | Rolls on the rack | rides `arbor` |
| Blanks (`blanksmall`, `blanklarge`) | Keyed to the arbor | (are cut) | rides `arbor` |
| Gap fills (`gs01`..`gs12`, `gl01`..`gl20`) | The cutter takes them | (none) | two `gauge` slides, radial inward, during their own pass; ride `arbor` |
| Jonas carrier (`jcarrier`) [head] | The arbor (it is the carrier) | The planets' pins | rides `arbor` |
| Planets (`jplanet1`..`3`) [head] | The sun, the ring, the carrier | (each other) | `gauge` rotate x about each pin: −4 × the roll; 20 index steps of +2 × the arbor's; ride `arbor` |
| Housing: ring, rim ratchet, drum, bands (`jring`) [head] | The pawl, at the index | The planets | 20 `gauge` rotations, 1.5 × the arbor's step; rides `table` |
| Sun, its shaft and detent disc (`sun`) [head] | The planets, while the master rolls | (floats) | `gauge` rotate x 3 × the roll; rides `table` |
| Detent plunger (`detent`) [head] | The sun's disc | Holds the sun at the index | `gauge` slide z 0.35 out while the sun turns; rides `table` |
| Shield (`shield`) [index] | Set for the master | Holds the pawl off the rim for the first 23° of its stroke (large) | `gauge` rotate x 60° for the large master (`present`) |
| Lever with roller and tail (`lever`) [index] | The pusher (out), its spring (back), its stop | The pawl | `gauge` rotate x −50°, a window per tooth; rides `table` |
| Pawl (`pawl`) [index] | The lever; lifted on the return (and by the shield, large) | The housing's rim | `gauge` rotate about its pin; rides `lever` |
| Check pawl (`checkpawl`) [index] | The rim's teeth | Holds the housing against turning back | `gauge` rotate about its pin, lifted during each step; rides `table` |
| Head shaft, upper cone, bevel (`headshaft`) [spindle] | The belt | The spindle | `rotate` x 1.4615 (θ) |
| Belt (`belt`) [spindle] | The lower cone | The upper cone | none (drawn still) |
| Spindle, bevel, collars (`spindle`) [spindle] | The head shaft | The cutter | `rotate` z −1.4615 (θ) |
| Cutter (`cutter`) [cutter] | The spindle | (cuts) | `rotate` z −1.4615 (θ) |
| Oil in the cup (`oillevel`) | The machine's oil tank (MachineOil, #488) | (shows it) | `stretch` y, 0.05 → 1.2 tall (oil) |
| Injection valve: body, bosses, feed, nozzle (`valve`) [oiler] | Fixed on the oiler's bracket | Sprays the cutter's teeth | none |
| Valve plunger: stem and marble handle (`valveplunger`) [oiler] | The work, once a tooth | (pumps) | `slide` x ±0.15, 2π per tooth (W) |
| Gearbox top and column door (`cover`) [cover] | Fixed; removable | Encloses the rectifier, clutch, worm and wheel | none |
| Frame with the gearbox casing and the oiler's reservoir (`frame`) | Static | — | none |

**The Jonas planetary.** Sun 10, planets 5 (three, at 120°), ring 20 inside the housing, module 0.3
(the housing 9 across, its rim ratchet 10); the carrier is the arbor. Ring, carrier and sun obey
(ring − carrier) × 20 = −(sun − carrier) × 10. **At the index** the detent holds the sun and the pawl
drives the housing, so the carrier (the arbor) turns 20 / 30 = two thirds of the housing: 45° of
housing (five of its 40 rim teeth) is 30° of arbor, one small tooth; 27° (three) is 18°, one large
tooth. **While the master rolls**, the check pawl holds the housing and the detent is out, so the sun
floats at three times the arbor's roll (up to 430° and back for the small gear) and the master can
turn the arbor; the sun comes back to its notch at the end of the return and the detent drops in.
Without that the dividing head would fight the rack. A gear's 12 or 20 indexes turn the housing 1.5
turns, which leaves it half a turn on; every pattern on it (6 spokes, 8 rivets, 16-sided drum, 20 and
40 teeth) repeats in 180°, so a new blank starts without a visible jump.

**The gear train.**

| Stage | Teeth / radii | Ratio |
|---|---|---|
| Lower cone to upper cone, belt on step 1 | r 3.8 → 2.6 | head shaft 1.4615 θ |
| Mitre bevels, head shaft to spindle | 10 : 10 | spindle and cutter −1.4615 θ |
| A1 → B1 (direct), in the column | 15 : 15, module 0.3 | B1 −θ |
| A2 → idler → B2, in the column | 12 : 12 : 12, module 0.3 | B2 +θ |
| Catches → feed shaft | | ψ (forward either way) |
| Feed shaft → clutch → worm (single start, lead π × 0.5) → wheel, in the gearbox | 1 : 12, module 0.5 | camshaft 1/12 per axle turn: **12 axle turns per tooth**, **144 a small gear, 240 a large one** |
| Camshaft → feed drum → table | | 7.5 voxels a pass |
| Master on the rack | small 12 T r 3; large 20 T r 5; rack module 0.5 | 143° / 86° of roll a pass |
| Index drum → pusher → lever → pawl → housing's rim ratchet | 40 teeth; 5 a stroke small, 3 large | housing −45° / −27° |
| Housing → planets → carrier (sun held) | 20 / 5 / 10, module 0.3 | arbor 2/3 of the housing: −30° / −18° a tooth |

Toothed wheels and what they mesh (nothing toothed meshes nothing): A1 ↔ B1; A2 ↔ idler ↔ B2; worm ↔
wheel; the mitre pair; sun ↔ planets ↔ ring; master ↔ rack; the cutter (a rack tooth) generates the
blank; the rim ratchet ↔ pawl and check pawl. The cones, the clutch's dogs, the lift cam, the drums,
the handwheel and the detent disc are plain.

The rig's `cut` holds `turnsPerTooth` (12) and the two masters' item codes; the teeth per gear are the
work's `end`. The gameplay's pace (#480: `TurnsPerTooth` × teeth shaft turns) should take its default
from it, or a test should hold the two together, as the rosser's `feed.gear` is held to its pace.

### Regenerating

```sh
python3 mods-src/seraphhorizons/GearCutter/tools/make_shape.py              # stdlib only; rewrites the files above
python3 mods-src/seraphhorizons/GearCutter/tools/make_shape.py --out DIR    # or writes them into DIR (the items into DIR/item)
python3 mods-src/seraphhorizons/GearCutter/tools/make_shape.py --quick      # skips the z-fighting fix and checks, the teeth depths and the cutter against the fills
```

A full run takes a few minutes, most of it the z-fighting fix. `tools/validate_gearcutter.py` holds the
checks; every run checks its output and exits non-zero if one fails. The enclosed parts are checked
like the rest:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no
  empty part.
- **Containment:** nothing leaves the 2 × 2 × 2 box over 29 poses of both masters.
- **Anchors:** the entry shaft meets the power face at the power cell's centre.
- **Textures** by role: oak sills and the axle's continuation; iron castings; stainless gears, shafts and
  wearing parts (the rim ratchet among them); temporal masters; cupronickel and gold Jonas head;
  cupronickel pawls, pins, roller and spindle collars (metal parts); a leather belt; a glass and brass oiler with oil in it.
- **Gearing:** every meshing pair's centre distance is the sum (or, internal, the difference) of the
  pitch radii, and its contact point moves alike on both wheels (finite differences of the posed rig),
  for the axle either way: the rectifier's three meshes, the mitre pair, the belt's rims, the worm (its
  lead the wheel's pitch; 12 axle turns a tooth), the clutch's halves turning together while engaged,
  and the sun, planets and ring in the index (sun held), the roll and the return (housing held), for
  both masters. The rectifier: for either sign of θ exactly one loose gear turns with the feed shaft,
  forward.
- **Generation:** for both masters, the master's and the blank's top pitch points stand still as the
  table moves (no slip on the rack, the blank rolling under the cutter as the master under the rack).
- **Teeth** (full runs): every mesh's worst box-into-box overlap over a tooth's cycle: master on the
  rack 0.1 (measured 0.082 small, 0.061 large), the cutter into a blank's teeth 0 (it only takes
  fills) and into any fill but the one it is cutting 0 (the fills reach past the tip circle and over
  the teeth's flanks), the rectifier 0.2 (0.095), the worm, the mitres and the planetary 0.3.
- **Index:** after every whole tooth the arbor and the housing stand on their steps and the sun at
  home; at the end of a gear the arbor stands as at the start and the housing half a turn on, which
  looks the same, so a new blank (W back to 0) starts without a jump.
- **Fills:** each gap's fill is under the cutter at the middle of its own pass and sunk below the root
  after it, for both blanks. Mid-gear (a third of the gaps cut), seen along the arbor: the face inside
  the root and every uncut gap's sector out to the tip covered (no crack), a fill's face in front at
  every gap and flank edge, and the middle of every cut gap open from root to tip; a sunk fill inside
  the body's inradius and behind its faces.
- **Cams:** both sliders' pins in their grooves at every 1/100 of a tooth (never into a rail); the
  screw's roller on the lift cam (an exact roller envelope) within 0.1; the pusher on the lever's
  roller through the push and the return (within 0.03), clear of it otherwise; the pawl 5° short of a
  rim face at rest and at the face mid-push; the housing turning exactly 5 rim teeth an index (small)
  and 3 (large); with the shield in, the pawl riding on it (tip at 5.09, over the rim's tips) until it
  drops; the detent never running into the sun's disc and in its notch whenever the sun is held.
- **Supports:** every shaft in two bearings or more (entry 2, feed 4 with the gearbox's end wall, head
  shaft 4, camshaft 5, spindle 3, arbor 2), the idler on its stud from the column's front wall, the sun
  shaft through the housing's eye and piloted in the arbor's end.
- **Oiler:** the oil's height is 0.05 at oil 0, 1.2 at 1 and linear between, inside the glass and under
  the cap; a pane over each side of the oil, every pane in the Transparent pass (`checks.sight_glass`); the cup's z span holds the cutter's plane; the valve's feed reaches the reservoir's base; its
  nozzle's mouth is 0.2 to 0.8 over the cutter's teeth (0.62; the nozzle never closer than 0.25) with the
  drip anchor at its mouth; the plunger pumps 0.3 once a tooth; the tray is under either blank all
  through the pass.
- **Nothing floats:** every frame element joined to the ground, without the cover.
- **Outline:** the sills are the base's plan (x 2..31, z 0.2..31): no frame or cover element whose
  bottom is below the knee projects beyond them, each sill's outer face is the body's extreme face on
  its side, and the east and west sills run the full length to the north and south sills' outer faces.
- **Cover seams:**
 every edge of every cover panel meets the frame or another panel (points along the
  edge, at mid-thickness and on both faces, within 0.06 of a frame or cover element), apart from the
  gearbox top's slot for the clutch handle.

- **No z-fighting** (full runs): no coplanar overlapping faces at five poses of both masters.
- **No close faces on the work** (full runs): at the start, middle and end of a gear for both masters, no
  two faces of the blanks, their fills, the masters or the cutter (or a face beside one of theirs) that
  face the same way and overlap where both can be seen are closer than `ZF_GAP` (0.025); a point of a
  face is out of sight when another element holds the point a gap in front of it, as a sunk fill is
  inside the body.
- **Items:** each forged part's item shape inside the 16-voxel item box at its `ITEM_SCALE`, centred on
  its floor, every element with a textured face, the scale no smaller than it needs.
- **Clearances:** over 52 poses (both masters, every phase of the cycle, the large master to its 20th
  tooth) no two parts touch except the intended contacts listed in `ALLOWED` (shafts through the
  column's walls and the gearbox's walls among them).
- **Files:** every texture declared; lids over every column; the shipped model is the checked one moved.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, as the
rosser's are.

### Rig schema (`gearcutter-rig.json`)

The rosser's schema (`../Rosser/README.md`, "Rig schema"), with these differences. Two things were
added to the shared rig maths for this machine, in `Machines/tools/machinegen/rigmath.py`,
`Machines/Core/RigAnimation.cs` and `Machines/Core/WorkProgress.cs`, `site/src/lib/rig.ts` and the
driver fixture (`Machines/tools/make_fixture.py`, regenerated; the mill's and the rosser's files
regenerate unchanged):

- **The work.** A rig's progress, W, is generic: a rig declares `work`, a named quantity in its own
  unit with a step and an end per class (the rosser's travelling stock is the other way to declare it,
  described in its README). This rig's `work` is `{ "name": "teeth cut", "unit": "teeth", "step":
  0.005, "end": { "thin": 12, "thick": 20 } }`. A work quantity is a point on its own scale: a gauge
  window is occupied while W is in it, eased in over `ease` at its start and out at its end, so the
  windows are written in teeth with nothing behind W. Drivers read W as `"input": "work"`. The details
  are in `docs/recipe-browser/models.md`, "The work and its drivers".
- **The `oil` input** (0..1, default 0), read by `"input": "oil"` on `rotate`, `slide` and `swing`, and
  by a `stretch` with `"input": "oil"` (a stretch's input is `"depth"` by default).

**Inputs.** θ and ψ as the mill's; W, k and p, used for the cut; and the oil:

- W counts **teeth cut**: W = 3.4 is 0.4 of the way through the fourth tooth's cycle. The gameplay
  advances W with the shaft at `1 / turnsPerTooth` per turn (12 axle turns a tooth), from 0 when a blank
  goes on, and holds it at the gear's count (12 or 20, the work's end for the master fitted) when the
  gear is done and while no blank is on (then the clutch is out and every part stands as at rest).
- k is **the master fitted**: 1 the temporal gear, 2 the large temporal gear, 0 none. p is its
  presence, eased as the master is fitted or taken off; k is held while p eases out.
- `oil` is the MachineOil tank's fill, 0..1: the cup's level. It moves nothing else.
- Every cam-driven motion is a `gauge` with one window per tooth (per master: a window carries a gain
  of 0 for the other master); the index is a stack of 20 `gauge` rotations that rise once and never
  fall; the knee's and the pusher's setting and the shield's are `present` gauges with amount 0 for the
  small master. Gauge windows are in teeth and are not shifted with the model.

**Keys.** `cells`, `powerCell` and `powerFace`, `infeedSide`, `outputSide`, `output`, `chips`, `drip`,
`work`, `cut` (`turnsPerTooth`, `masters`) and `parts`. `requires` values: `spindle`, `feedscrew`,
`camfeed`, `camindex`, `liftcam`, `index`, `oiler`, `head`, `cutter`, `master`, `masterlarge`,
`blanksmall`, `blanklarge`, `cover`, or null. A

renderer shows a blank's parts while that blank is on the arbor (gameplay reports it as fitted), and
one master's parts while it is fitted.

**In the viewer** (`site/models.json`, the site's model page): the work's slider reads in teeth (the
rig's unit and step) and stops at 12 or 20 for the master chosen (the work's end), the size select
names the masters (`inputs.size.names`), each master and blank shows only with its own master
(`requiresClass`), the cover is a fitted-parts checkbox, the oil has a slider, and Play turns the axle
at the reader's input speed (0 to 5 RPS) and moves W with it at 12 turns a tooth
(`play.turnsPerWork` `"cut.turnsPerTooth"`). See `docs/recipe-browser/models.md`.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `entry_*`, `rectb1_*`,
`idler_*`, `rectb2_*`, `feedshaft_*`, `clutch_*`, `clutchlever_*`, `worm_*`, `cam_shaft*` and `cam_wheel*` (camshaft), `cam_fdrum*` and `cam_fgroove*` (camfeed), `cam_idrum*` and `cam_igroove*` (camindex), `cam_lift*` (liftcam), `feedslider_*`,
`indexslider_*`, `pusher_*`, `knee_*`, `screw_*`, `table_*`, `arbor_*`, `master_*`, `mastl_*`,
`blanks_*`, `blankl_*`, `gs01_*`..`gs12_*`, `gl01_*`..`gl20_*`, `jcarrier_*`, `jplanet1_*`..`jplanet3_*`,
`jring_*`, `sun_*`, `shield_*`, `lever_*`, `pawl_*`, `checkpawl_*`, `detent_*`, `headshaft_*`,
`belt_*`, `spindle_*`, `cutter_*`, `oillevel_*`, `valve_*` and `valveplunger_*` (the injection valve),
`cover_*`, and `fr_*` for the frame (the oiler's glass, cap and bracket are `fr_oiler_*`, the gearbox's
walls `fr_case_*`). Hand edits are lost when the
script runs again: port them into `make_shape.py`, or stop regenerating.

## Gameplay

The rules are game-independent in `Core/` (`GearCutterParts.cs`, `GearCut.cs`, `GearCutterConfig.cs`,
`GearCutterRig.cs`); the game side is `Game/` (`GearCutterSystem`, the controller `BlockGearCutter` and
`BEGearCutter`, the ghosts `BlockGearCutterGhost` and `BlockGearCutterGhostPower` with
`BEGearCutterGhost`, the load `BEBehaviorGearCutterMP`, and `GearCutterRenderer`), as the rosser's are.
The mod's README, "Gear cutter", is the player-facing summary; the decisions are here.

**Blocks.** `seraphhorizons:gearcutter-frame-{side}` (`blocktypes/gearcutter/frame.json`, drawing
`gearcutter_frame.json`, its textures every texture of both shapes, since the renderer draws the parts
with the block's texture source), `gearcutter-ghost` and `gearcutter-ghostpower-{side}`. Placement,
ghosts, ghost repair, breaking through a ghost and the boxes (selection from the rig's cells, collision
with their lids) are the rosser's, without the trunk. The frame recipe (`recipes/grid/gearcutter.json`):
8 steel ingots (4 in each ingot slot, steel only), 4 planks, 32 nails and strips of iron, meteoric
iron or steel, 3 stainless gears (`seraphhorizons:gear-stainless`, in the bottom middle slot, for the gearing
the frame carries: the feed rectifier's train and the camshaft's worm wheel; every other machine in the
pack pays stainless gears for its gears, #473, and the first come from reclaiming rusty gears) and a hammer. The pack's other machine frames cost iron, but this is the end-game
machine of the gears epic, and a cast bed and column are a lot of metal. The cost is in the recipe
itself (the cutter does not need Immersive Woodworking, so it is not in
`patches/woodworking-machine-costs.json`).

**Stages** (`GearCutterParts`). One item a stage, in `GearCutterStage` order, the next missing stage
the only one a click fills (an item a later stage takes is `OutOfOrder`, one whose stages are all in
`AlreadyFitted`, a kit with no durability `KitSpent`). The two cam drums each take one
`game:jonasframes-gearbox02`. The head takes either Jonas gear assembly. The kit keeps its durability
left and full (`GetRemainingDurability`, `GetMaxDurability`; 500 in `itemtypes/gearcutter/kit.json`). The
master is `master` for the temporal gear and `masterlarge` for the large one; `Fitted` draws only the
fitted one. The creative shortcut (`CreativeUpgrades.Applies`, Ctrl in creative mode on an incomplete
cutter) fits each stage's first code, always on (the rosser's follows `UnifiedWoodworking`; this
machine has nothing to do with woodworking). Take-back (`TakeBack`): the kit, else a blank on the
arbor, else the master (only once the kit is out); a kit comes back with a `durability` attribute when
worn. Restoring a save keeps the stages up to the head as a run from the first and the kit and the
master only with the head, as the rules could have fitted them.

**The cut** (`GearCut`, `CutJob`). A blank goes on only on a complete cutter, one at a time, of the
master's size. W runs from 0 to 12 or 20 by `TeethFor(radians, TurnsPerTooth)` while
`GearCut.Running` (complete, a blank on, the shaft at `MinSpeed`). `BEGearCutter.Cut(radians)` is
the step (the server's 50 ms tick calls it with the shaft's advance; the Atlas scenarios call it to
finish a gear without minutes of turning). A finished gear goes into a container in
`GearCutterRig.OutputNeighbour()` (the cell beyond `output.pos` across the output face), else drops at
`OutputDrop()` (`output.pos` moved 0.15 beyond that face, pushed outward). The infeed is every cell
just beyond the infeed face, both levels (`InfeedNeighbours`); the first container slot holding a
blank of the master's size gives one, on the slow tick and after each gear, while the shaft turns.

**Oil and wear** (`GearCutterWear`). `Oil.NewOwn(api, OilMachine.GearCutter)`: the tank, pouring,
saving, syncing and smoke are MachineOil's. `BEBehaviorGearCutterMP.GetResistance` is `Resistance`
whatever the tank holds (the mill's and the rosser's multiply while dry). When a gear finishes:
fill = points / tank before this gear's drain; wear = ceil(`CutterWearPerGear` × teeth / 12 / fill),
at most what is left, all of it at fill 0; then the drain (`DrainPerJob`, doubled for a large gear);
then `WearKit`, which at 0 removes the kit and plays the tool-break sound. The gear is delivered
either way. `Oil.Info` takes `dryLoad: false`, so the cutter's info has no "takes 3× the power"
line; it adds the wear multiplier (or "the next gear breaks the kit") and the kit's gears left at the
current fill (`GearsLeft`).

**Renderer.** Every rig part with a `requires`, a ride or a driver, from `gearcutter.json`
(`MachineMeshes.PartMeshes`), drawn when `BEGearCutter.Fitted(requires)`: the parts' rule, `cover`
always, `blanksmall` / `blanklarge` while that blank is on. Inputs: θ and ψ from the power ghost's
angle about native x (`MillMotion.NativeShaftAngle(.., Axis.X)`); W advanced per frame by the shaft
while running, never behind the server's W and at most 0.3 tooth ahead of it, at the master's end
with no blank on (so the clutch is out and the index stands at rest); k the master's class, held while
p eases out over 0.4 s; `Oil` the tank's fill (1 with MachineOil off). Particles while running: steel
chips and now and then a spark at `chips.pos`; with oil in the tank, a downward oil spray at
`drip.pos`.

The part meshes come from one tessellation of the shape, not one per part (that froze the client
for a moment on placing a cutter: 75 clones and tessellations of 1466 elements): every element's
`JointId` is set to its part + 1, `TesselateShapeWithJointIds` writes it per vertex into
`CustomInts`, and the faces are copied out by that tag (`MachineMeshes.PartMeshes`,
`Machines/Core/PartSplit.cs`), with a logged fallback to the per-part way if the tags don't check
out. The uploaded meshes are cached for the session (`MachinePartMeshes`) and shared by every cutter,
so a second one or a chunk reload builds nothing; they are disposed when the client leaves the world,
never by a renderer.

**Settings** (`GearCutterSettings`): `TurnsPerTooth` 12, `CutterWearPerGear` 10, `Resistance` 0.2 and
`MinSpeed` 0.05 (the rosser's), each falling back to its default with a warning when out of range;
the oil's are `MachineOilSettings.GearCutter` (tank 1000, 10 a gear). The client uses the server's
`MinSpeed`, `TurnsPerTooth` and `CutterWearPerGear` (synced in the tree).

**Switch.** `GearCutter` (default on): off, the server marks the block types, the item types
(`itemtypes/gearcutter/*.json`) and the recipe files (`recipes/grid/gearcutter.json`,
`recipes/smithing/gearcutter.json`) disabled before the game loads them. The classes are registered
either way, as the rosser's are.

**Open questions, decided the simplest way.** The blank can be taken back by Ctrl + right-click
(after the kit, before the master), its cut lost, since otherwise only breaking the frame would free
it to change the master; the kit's wear rounds up per gear; a cutter with no MachineOil tank wears at its base; the
infeed only feeds while the shaft turns; and the frame has no schematic yet.

## Tests

- `tools/tests/test_gearcutter_model.py` (no game, no fetched mods, so CI runs it): the shipped rig
  parses with the shared maths and uses the `requires` vocabulary; every element has a part and every
  part an element; every third reference pose is the rig's own maths; the cells are rebuilt from the
  shipped shape; the work counts teeth to 12 and 20 and is the rig's only progress; the gears are the game's
  sizes; the index lands on whole teeth for both masters with the housing at 1.5 times the arbor; the
  knee drops 2 for the large master; the oil level follows `oil` from empty to full; 8 cells, lids, the
  power face and the anchors (and no oil anchor).
- `tools/tests/test_machinegen.py` holds the work in the reference maths: `progress_of`, a work
  quantity's windows and ends, the old input name as the same input, a roll refused on work.
- `site/test/gearcutter.test.ts` replays every reference pose (oil included) through the site's
  `rig.ts` and checks the inputs the viewer will offer; `site/test/models.test.ts` checks the
  scenario against the rig and the viewer's work end, class filter, geared Play and input speed.
- `tests/GearCutter/GearCutterRigTests.cs` (part of `dotnet test mods-src/seraphhorizons/tests`) parses
  the shipped rig with the shared `RigParts` and `RigProgress` (the renderer's maths) and replays every
  reference pose (θ and ψ alone, then the cycle's edges in three teeth of each master, W at 0 and its
  end, p at 1 and 0.4, the oil at 0, 0.35 and 1), so Python, C# and TypeScript are held to one another;
  the driver fixture (`tests/Machines/driver-fixture.json`) holds the `oil` input and the work quantity
  in all three.
- `make_shape.py` checks its own output every time it regenerates the model.
- `tests/GearCutter/GearCutterGameplayTests.cs` (the same project, no game): the build order and what
  each stage takes, out-of-order and repeated parts refused, the two cam drums, either Jonas head,
  the masters' classes and what is drawn, take-back order, drops and saves; the cut's turns per tooth,
  progress by angle and the teeth by master; the kit's wear over the fill, the large gear's 20/12,
  the empty tank taking the whole kit; the settings, and the shipped rig's anchors, neighbours and
  `cut.turnsPerTooth` against the gameplay's default.
- `tests/PackTests/GearCutterScenarios.cs` (Atlas, the shared world) and `SwitchesOffScenarios`:
  see the mod's README, "Tests".

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's own
viewer. The gameplay is tested headless (Atlas): no one has yet looked at the renderer's output in a
client, the particles' placing included.

1. **Box teeth.** Every tooth is one or two boxes, so meshing teeth overlap a little where real tooth
   forms would not (the limits above). The cutter is a body and three stepped bands inside the rack
   tooth's profile, and the blank's teeth are boxes (thinner for 12 teeth, for the undercut), so the
   "generated" form is drawn, not computed.
2. **Linear ramps.** The gauges move in straight ramps, so the table, the knee and the lever start and
   stop sharply; the grooves and the lift cam are drawn from the same ramps, so the followers stay on
   them (to 0.13 and 0.1), but the motion is not the smooth cam motion a real machine has.
3. **The pusher and the lever.** The pusher moves in a straight line and the roller on an arc; they
   stay within 0.03 through the push.
4. **Fills sink radially.** A gap's fill sinks into the blank evenly through its pass; a real cut
   proceeds as the cutter rolls through the gap. Sunk fills overlap each other inside the body. An
   uncut rim is plain disc only to within 0.07 of the faces: the fills stand up to 0.025 proud, the
   teeth 0.025 and 0.045 in, and a narrow strip of tooth face shows between neighbouring fills near the tip,
   where a box fill cannot follow the tooth's taper.
5. **The clutch's dog faces.** The sleeve turns with ψ and the worm with W; they agree only while the
   gameplay's pace is the drawn 12 turns a tooth. A changed `TurnsPerTooth` would show at the dogs.
6. **Master changes.** While p eases in or out, the index stack and the knee's setting move with p, so
   fitting or taking off a master at W = 12 or 20 turns the arbor a full turn in about 0.1 s. Resetting
   W to 0 when the master changes avoids it.
7. **Which member is held in the roll** is drawn, not forced: the check pawl holds the housing one way
   and the lever's spring the other, and the sun floats. The housing ends a gear half a turn on.
8. **The cutter turns with the axle**, either way; a formed cutter cuts one way only. #480 wanted the
   spindle on θ; a reversed axle would rub. The feed is rectified, so the count always advances.
9. **The oiler's level is a box**, not a meniscus, seen through four panes of glass.
10. **The masters and blanks overhang** the pedestal on the arbor's back end, the master 6 to 10 voxels
    west of its bearings, as a gear on a mandrel would be; nothing steadies the arbor's end.
11. **The cover** is one part (the gearbox's top and the column's door together), drawn with the
    moving parts, not in the frame's static shape; whether the game shows it always or lets it come
    off is gameplay's to decide. The camshaft's lift cam, at the south end, and the cam drums are not
    enclosed.
