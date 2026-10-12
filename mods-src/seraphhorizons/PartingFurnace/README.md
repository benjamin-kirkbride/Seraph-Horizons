# Parting furnace

Part of the Seraph Horizons mod (`../README.md`): the parting stage of the ore line (ore processing, #684), one
upgradeable machine (#740, on the shared level code of #711) that does every parting step: cupellation of silver
from lead, liquation of lead from tin, acid parting of silver from gold, and the Parkes process. Its model is #741.

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what those will be built against. The machine has two levels, each a set
of parts fitted into the same frame:

| Level | Tier | Fits | Recovery |
|---|---|---|---|
| 1 | water | the blast, the cupellation furnace, the liquation furnace, the acid parting vessel (with the process-water cock) | 95 % |
| 2 | mill | the Parkes kettle, its stirrer, and its chutes (chute-fed) | 100 % |

Level 2 **adds** to level 1 and takes nothing away: the Parkes process leaves a rich zinc crust whose silver is
still cupelled, and liquation and acid parting serve the other ores (#740).

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), `Machines/`, this folder's neighbour, or `site/` and `tools/tests/`, which are the
repository's. The generic half of the generator is `Machines/tools/machinegen/`, unchanged by this machine (no new
driver: every motion is an existing `rotate` or `slide` on θ).

## The machine

A small parting works of the 1800s on a brick-paved floor, one chimney stack at the back, every furnace's working
face to the front (native south, +z). Everything in the model was made for this mod: no other mod's model is used,
so the generator needs nothing from `build/mods`.

**Footprint.** Fixed at its level 2 size from the frame on: 4 cells wide (x) by 2 deep (z) by 2 high, the stack's
column 2 higher, **18 cells**. No floor is left dead at level 2: each column holds a furnace. At level 1 the kettle's
place (the front right) stands empty, as #711 expects of an early level. The controller is the cupellation
furnace's front cell at ground level (build cell (2, 0, 1), shipped `[0,0,0]`).

**Plan** (build cells (x, z), north at the top):

| | x 0 | x 1 | x 2 | x 3 |
|---|---|---|---|---|
| z 0 (back) | liquation furnace (its high end) | the chimney stack | cupellation furnace (its back, the tuyere) | the blowing engine; the power cell |
| z 1 (front) | liquation furnace's arch, spout and receiving pot | acid parting vessel under its hood; the water cell | cupellation furnace's working door, fire door, litharge pot | (level 2) the Parkes kettle; the input and output cells |

**The frame** (`requires` null; the block draws it, `partingfurnace_frame.json`, 18 elements): the brick-paved
floor over the whole footprint and the chimney stack (a base, the shaft, a fire-brick cap with its flue open,
soot-black inside). **The fires' flues run under the floor to the stack and are not drawn**; the cupellation furnace
and the acid hood stand against the stack.

**Level 1** (water: `blast`, `cupel`, `liquation`, `acid`, and the water pipe in one metal; 232 elements with the frame):

- *The blast* (`blast`). The vanilla axle comes in on the north face of the power cell, along z, and continues as
  the **engine shaft** in two cast-iron pedestal bearings. Between them a **crank** (two webs and a pin) works a
  **Scotch yoke**, which slides in two guides standing on a small cast-iron **blowing cylinder** under it; the
  yoke's piston rod goes down through a stuffing box into the cylinder. From the cylinder's valve chest the **blast
  pipe** runs round the back of the cupellation furnace to its tuyere. The shaft runs on past the south bearing to
  a short stub, where level 2's bevel pinion is keyed.
- *The cupellation furnace* (`cupel`), an English test furnace: a brick hearth with the **fireplace** sunk in its
  east end (grate bars, coke, the fire), a fire-brick **fire bridge**, and in the hearth the **test**: an oval of
  bone ash in an iron ring (open at the front, the breast), the molten lead on it. A low segmental **vault** over
  all, buckstays and tie rods, the **tuyere** through the back wall blowing across the lead, and at the front a wide
  **working door** up to the vault's springing, through which the test shows, the breast's **litharge channel**
  running out through it into the **litharge pot**. The **fire door** stands open on its hinge, so the fire shows;
  the ash-pit door under it.
- *The liquation furnace* (`liquation`): a brick base over its firebox, a cast-iron **inclined hearth plate** on
  fire-brick steps with raised rims, two **cakes** on the slope and dross at its foot, a low vault over it **open at
  the low end** (the front), so the plate and cakes show, and a **spout** from there into the **receiving pot**. Its
  fire door is on the west face.
- *The acid parting vessel* (`acid`): a brick firebox with a cast-iron **sand bath** on it, the **stoneware vessel**
  set in the sand with the aqua fortis in it, and a brick **hood** over the vessel (two piers and a top), its back
  against the stack, which takes the fumes. The front of the sand bath is open. The **process water** comes in
  from a ppex pipe on the water face (south, at the hood's front) through a **water pipe** under the hood to a
  **brass cock** over the vessel, its nozzle over the vessel's mouth.
- *The water pipe* (`pipecopper` or `pipelead`, fitted with the acid set). It is drawn as the rosser draws its drip
  pipes and as UnifiedPipes draws copper and lead ppex pipe (`../Pipes/tools/solderedpipe.py`): one straight length
  of Pipes and Power Expanded's pipe, its square tube round its bore and its texture mapping, from the water face
  along the east pier's inside to the cock, with a wiped joint of solder where it meets the cock. It has no joint at
  the face: the ppex pipe beyond brings its own half joint there. The part is drawn twice, once in each metal
  (`game:block/metal/sheet-plain/copper4` or `lead4`, the joint in `game:block/metal/ingot/leadsolder`), and the
  renderer draws the supplied metal's.

**Level 2** (mill: `kettle`, `stirrer`, `chutes`; 80 more elements, 312 drawn in all):

- *The Parkes kettle* (`kettle`): an octagonal brick **setting** over its firebox (fire and ash-pit doors on its
  front), the cast-iron kettle's **flange** round its mouth, the molten **lead** in it with cakes of **zinc crust**
  gathered round its edge, and a **skimmer** lying across the flange.
- *The stirrer* (`stirrer`): a **bevel pinion** keyed to the engine shaft's south end drives an equal **bevel
  wheel** (a mitre pair, 6 teeth each) on a vertical shaft, carried in two bosses on a cast-iron **gallows** bolted
  to the kettle's flange; two **paddles** cross on the shaft's foot, their tops breaking the lead's surface so the
  stirring shows.
- *The chutes* (`chutes`): the **infeed** takes automation, so it is **the game's chute**: a straight length of
  vanilla's closed square chute (`shapes/block/metal/chute/straight.json`: four walls 1 voxel thick round a 6 × 6
  bore, 8 across, at 4..12 in the block) in its own texture, `game:block/metal/sheet/copper1` (the pack's
  `unifiedpipes-chutesection.json` adds lead only to the chute *section* item; a placed chute is copper sheet). It
  runs in from the input face (east), where a player's chute placed in the next cell meets it wall for wall with no
  gap or step, to its open end over the kettle's mouth, where the charge drops in; a leg under it stands on the
  setting. A **siphon** over the kettle's front rim runs the desilverized lead down
  into the **output spout**, whose lip is on the output face (south).

**Anchors** (`assets/seraphhorizons/config/partingfurnace-rig.json`, shipped frame):

| Key | Value | What |
|---|---|---|
| `powerCell`, `powerFace` | `[1, 1, -1]`, north | The vanilla axle comes in along z at the cell's centre (build (x 56, y 24)) |
| `waterCell`, `waterFace` | `[-1, 1, 0]`, south | The water pipe ends on the face's centre in ppex's section, on the axis of the ppex pipe beyond (build (x 24, y 24)) |
| `inputCell`, `inputFace`, `input.pos` | `[1, 1, 0]`, east, `[2.0, 1.5, 0.5]` | Level 2: the infeed, the game's chute section centred on that face |
| `outputCell`, `outputFace`, `output.pos` | `[1, 0, 0]`, south, `[1.65, 0.35, 1.0]` | Level 2: the output spout's lip on that face |

All four cells are the machine's from the frame on, on its outside, and differ. At level 1 the player feeds and
empties the furnaces by hand (the water tier); the input and output faces are where level 2's chutes meet the
machines before and after it.

### Every moving part

θ is the axle's angle; the rig reads nothing else. Every other part (the furnaces, the engine's fixed parts, the
gallows, the chutes, the frame) stands still.

| Part (rig id) [requires] | Driven by | Drives | Driver |
|---|---|---|---|
| Engine shaft: the axle's oak continuation, the shaft, the crank's webs and pin (`shaft`) [blast] | The vanilla axle at the power face | The yoke, through the crank pin; at level 2 the bevel pinion, keyed to it | `rotate` z, ratio 1, about (x 56, y 24) |
| Scotch yoke: frame, slippers, piston rod (`yoke`) [blast] | The crank pin, running in its slot | The blowing cylinder's piston: the blast | `slide` y, amplitude 2.5 voxels, sin θ |
| Bevel pinion and its boss (`bevelpinion`) [stirrer] | Keyed to the engine shaft | The bevel wheel | `rotate` z, ratio 1 |
| Stirrer: bevel wheel, shaft, paddles (`stirrer`) [stirrer] | The bevel pinion (a mitre pair) | The lead and zinc in the kettle | `rotate` y, ratio −1, about (x 56, z 22) |

The crank pin is east of the shaft at θ 0, so its height is the shaft's plus 2.5 sin θ, and a Scotch yoke follows a
crank pin's height exactly: the `slide` driver is that motion, with no approximation. The bevel pair's pitch cones
share their apex where the axes cross, over the kettle's centre (module 1.2, 6 teeth each, pitch radius 3.6).
Toothed wheels and what they mesh: the pinion and the wheel, only.

**Either way round.** The vanilla axle may turn either way, and every motion works in both:

- *The blast.* The yoke follows the crank pin's height, r sin θ: turned backwards it runs the same stroke, up and
  down, in the other order. The blowing cylinder's valves (in its valve chest) are self-acting flap valves, opened
  and shut by the air as the piston draws and drives, not by anything on the shaft, so the cylinder blows on its
  stroke whichever way the crank turns. Nothing in the engine has a direction: no ratchet, no cam with a working
  flank, no one-way catch.
- *The stirrer.* The mitre pair turns the stirrer the other way when the shaft does. Its two paddles are flat blades
  through the shaft, symmetric about it, so they stir alike either way.
- *The rig.* Every driver reads θ alone (none reads the shaft's travel, which is how the mill tells a direction), so
  the model is posed by the angle only. The generator checks this, that the yoke's stroke is the same backwards,
  that the paddles are symmetric about the stirrer's axis, and the clearances over a turn backwards.

### Choices, and why

- **The blast is a crank, a Scotch yoke and a small blowing cylinder, off the axle.** English cupellation
  furnaces were blown by a fan or a blowing engine, so the period device is kept; powering it is the machine's power
  cell. The old line shaft on four columns is gone: the axle comes straight into a short shaft over the cylinder.
  A Scotch yoke is used because its motion is exactly the existing `slide` driver; a connecting rod's swing and
  rise are not any existing driver's. The crank sits between the two bearings (a cranked shaft), so the shaft can
  run on to the stirrer's pinion. The cylinder is drawn single-acting with a valve chest; its piston is not drawn.
- **The stirrer is powered, through a mitre pair.** Parkes's zinc was stirred in by hand at first; geared
  mechanical stirrers came in later in the century, and the mill tier is the unattended one. An equal pair keeps
  the wheel small over the kettle (its mouth shows round it) and the teeth chunky. It turns whenever the shaft does.
- **No precipitating tub.** The old model's tub of copper plates is dropped: the silver solution is drawn off and
  its silver thrown down (on copper, or as chloride with salt) as part of the vessel's cycle, which at this scale is
  not drawn as a vessel of its own. This is a simplification (see the open questions).
- **Process water: yes, for the acid vessel.** Acid parting uses water: the aqua fortis is diluted before the
  silver is precipitated, and the gold left in the vessel is washed with hot water. Nothing else uses water
  (cupellation, liquation and the Parkes kettle are dry), so there is one inlet, on the acid station, fixed from the
  frame on.
- **The acid vessel is stoneware, not cast iron.** Aqua fortis (nitric acid) eats iron; cast-iron vessels were used
  for parting with sulphuric acid. Stoneware or glass on a sand bath is the period practice for nitric parting.
- **Interiors open to view.** The cupellation furnace's working door runs up to the vault's springing and its fire
  door stands open; the liquation furnace's arch is open at its low end; the acid hood is open in front; the kettle
  is open on top. Nothing is cut away that the furnace did not have open.
- **Flues under the floor.** Every fire's flue runs under the floor to the stack (the frame's), so nothing on the
  floor moves between levels and no duct crosses a working face.
- **Collision by what is fitted.** A cell's `boxes` are the frame's (the floor, the stack); its `fitted` boxes, by
  requires value, are there only while that set is fitted (`docs/recipe-browser/models.md`, "Collision by what is
  fitted"). At the frame 11 of the 18 cells have boxes; the kettle's upper cell has only the engine shaft's end at
  level 1.

## The rig (`assets/seraphhorizons/config/partingfurnace-rig.json`)

The bucking sawmill's schema (`../BuckingSawmill/README.md`, "Rig schema") with no work and no trunk path: θ only.

- `cells`: the 18 cells. Each has `boxes` for the frame's elements and `fitted: {requires: boxes}` for each fitted
  set's, up to three boxes each, rebuilt from the shipped shape posed at rest (as the rosser's, the gear cutter's and
  the draw bench's are). No cell is hollow, and **no column has a lid** (see the open questions).
- `powerCell`, `powerFace`; `waterCell`, `waterFace`; `inputCell`, `inputFace`, `input.pos`; `outputCell`,
  `outputFace`, `output.pos` (above).
- `levels`: `{ "1": [...], "2": [...] }`, the `requires` values of each level's sets, in order. The viewer lists it as
  a key it does not draw.
- `waterPipes`: `["pipecopper", "pipelead"]`, the water pipe's parts, one per metal, fitted with the acid set: the
  supplied metal's is drawn (the rosser's drip pipes' way). Also listed as not drawn.
- `parts`, in level order, the frame last: `shaft`, `yoke`, `engine` [blast]; `cupel` [cupel]; `liquation`
  [liquation]; `acid` [acid]; `pipecopper` [pipecopper], `pipelead` [pipelead]; `kettle` [kettle]; `gallows`, `stirrer`, `bevelpinion` [stirrer]; `chutes` [chutes];
  `frame` (null). No part rides another.

## Model files

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/partingfurnace.json` | The whole machine, both levels' sets and the water pipe in both metals (320 elements). |
| `assets/seraphhorizons/shapes/block/partingfurnace_frame.json` | The frame only (18 elements): the floor and the stack. |
| `assets/seraphhorizons/config/partingfurnace-rig.json` | Cells, anchors, levels, the water pipe's parts and the part rig (14 parts). |
| `tests/PartingFurnace/rig-reference.json` | Every part's matrix at ten poses of θ, from the reference maths. |

Elements by set: frame 18; blast 63 (shaft 16, yoke 7, engine 40); cupellation furnace 72; liquation furnace 40;
acid parting 31; the water pipe 8 in each metal; kettle 22; stirrer 44 (gallows 6, stirrer 20, pinion 18); chutes 14.

Textures are the game's, referenced: brick `clay/brick/eight/running/orange1`, the floor's `red1`, fire brick
`cream1`, cast iron `metal/plate/iron`, steel `metal/sheet-plain/steel1`, oak, bone ash `stone/rock/chalk1`, lead,
tin and zinc ingots for the metals, litharge `clay/hardened/orange`, coke, charcoal and ember for the fires and the
dross, sandstone sand for the sand bath, `clay/ceramic-dark` for the stoneware, `liquid/dilutedalum` for the aqua
fortis, `metal/sheet-plain/copper4` or `lead4` (ppex's pipes', as UnifiedPipes gives them) for the water pipe, with its
joint in `metal/ingot/leadsolder`, `metal/plate/brass` for the cock, `metal/sheet/copper1` (the game's chute's) for
the infeed, and
`black` down the stack. Coloured by texture in the viewer, each has a colour of its own
(`site/src/lib/model-view.ts`).

## Regenerating

```sh
python3 mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py --quick      # skips the z-fighting fix (not for files that ship)
```

A full run takes about five seconds. Two runs write the same bytes (`tools/tests/test_partingfurnace_model.py`
checks that the generator reproduces the committed files).

## Validation (`tools/validate_partingfurnace.py`)

Every run checks its output and exits non-zero if a check fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Levels:** every `requires` value is one level's; the parts are listed level by level, the frame last.
- **Nothing floats, at any level:** the static elements of the frame, of the frame with level 1, and with levels
  1–2, are each joined to the ground (a later level's set may not hold up an earlier one's).
- **Containment:** nothing leaves the 4 × 4 × 2 box over 25 poses, and no moving part reaches a cell the model does
  not hold at rest.
- **Anchors:** the oak end meets the power face at the power cell's centre; the water pipe meets the water face at
  its cell's centre in ppex's 6 × 6 section; the infeed's end is on the input cell's face and the output
  spout's lip on the output cell's, each within its cell, their anchor points on those faces; every anchor cell is on
  the footprint's outside, and no two share a face.
- **Textures** by role (23 rules, every element covered; the water pipe in its metal, its joint in solder).
- **Gearing:** the bevel pair's axes cross at its apex, its ratio is its teeth's, and the pitch point moves alike on
  both gears at six angles either way (finite differences of the posed rig).
- **Yoke:** at 48 angles the crank pin stays on the slot's centre line and within its length; the piston rod stays
  in its gland at the top of the stroke and clear of the cylinder's foot at the bottom.
- **Supports:** the engine shaft in its two pedestal bearings and the stirrer's shaft in the gallows' two bosses
  (each round its shaft at rest and turned 45 degrees); the yoke's slippers on their guides through the stroke.
- **Flows:** the litharge channel ends over its pot, the liquation spout over its pot, the blast pipe runs from the
  valve chest into the tuyere, the infeed's open end is over the kettle's mouth, the siphon dips into it and runs out into
  the spout, and the water cock's nozzle is over the vessel's mouth.
- **Infeed:** where it meets the input face, its walls are exactly the game's straight chute's section in the
  cell (`VANILLA_CHUTE`), axis-aligned, running in from the face, in the chute's texture, so a vanilla chute in the
  next cell meets it with no gap or step. `tools/tests/test_partingfurnace_model.py` also holds `VANILLA_CHUTE` to the
  game's own `straight.json` when the game is installed (skipped otherwise).
- **Pipes:** the water pipe is four walls of ppex's section from the water face into the cock, a wiped joint where it
  meets the cock; each other metal's part is the same elements in its own texture, the joint in solder.
- **Either way round:** every driver reads θ alone; the yoke's stroke is the same backwards; the paddles are
  symmetric about the stirrer's axis; the clearances hold over 48 poses of a turn backwards.
- **Clearances:** over 99 poses through two turns (and backwards), no moving part touches anything but its intended
  contacts (`ALLOWED`: the shaft in its bearings, the pin in the slot, the rod in the cylinder and gland, the slippers
  on their guides, the pinion on the shaft, the stirrer in its bosses and the lead). The bevel teeth clear each other:
  they are 0.30 of the pitch thick, cast teeth with backlash.
- **No z-fighting** (full runs): no coplanar overlapping faces at four poses, after the fix, with the water pipe in one metal (the copies are made after the fix, so each gets the same insets, and never drawn together).
- **Files:** every texture declared; no hollow cells, no lids, every `fitted` key a requires value, no collision box
  thinner than 0.04 of a block (a part just over a cell's edge); what collides at each level is printed; the shipped
  model is the checked one moved.

## Editing by hand

Element names are the rig's interface (first-match globs): `sh_*`, `yk_*`, `en_*`, `cp_*`, `lq_*`, `ac_*`, `kt_*`,
`gl_*`, `st_*`, `bp_*`, `ch_*`, and `fr_*` for the frame. Hand edits are lost when the script runs again: port them
into `make_shape.py`, or stop regenerating.

## In the viewer

`site/models.json`, `parting-furnace`: θ's slider spans one turn (the stirrer turns with the shaft), Play turns the
axle at 1.5 s a turn (the play script has no phases: the rig reads θ alone), and a **Level** select (one group of
states) shows the frame, level 1 (water) or level 2 (mill), each with every set before it, level 2 by default, and a **Water pipe** select (Copper or Lead, the
rosser's values and labels, copper by default), which waits until the acid set is fitted. The checkboxes under each
fit or take off a set by hand. The anchors draw as the power, water, input and output cells
and the two chute ends; the collision overlay draws what each picked level collides with.

`site/test/partingfurnace.test.ts` replays the reference poses through the viewer's maths and checks the anchors,
the scenario, the level states, the fitted collision and the texture colours; `tools/tests/test_partingfurnace_model.py`
does the same in Python and checks the generator reproduces the files, the cells, the water pipe in each metal, the mitre pair, the yoke, and
that every motion works either way round.

## Open for the owner

1. **The blast.** A crank in a Scotch yoke on a small blowing cylinder, between two pedestal bearings. The
   alternative is a pair of leather bellows worked from the same shaft by cams (the older device); a fan in a casing
   would hide its motion.
2. **The stirrer** is powered through a mitre pair and turns whenever the shaft does; a clutch, or lifting it out
   for the crust to rise, would need inputs the rig does not have. Skimming is left to the hand (the skimmer is a
   prop).
3. **The precipitating tub is dropped** (above). If the owner wants the silver's precipitation seen, a small
   stoneware crock with a copper plate beside the vessel is about 12 elements.
4. **The water inlet** is on the front (south) face, at the hood: the only outer face the acid station has. A pipe
   comes to the front of the machine there.
5. **"All chute-fed" at level 2.** The kettle is on the chutes (in from the east, out by the siphon at the front);
   the crust's way to the cupellation furnace (by way of a retort, not drawn) and the other furnaces' charges have no
   launders. Whether the two chutes stand for the whole machine's input and output is the gameplay's call.
6. **Walking on top: no lids.** The tops are at many heights (vaults, the hood, the engine's guides, the kettle); a
   deck at one height would be an invisible floor over the kettle and the furnaces' fronts.
7. **The charges and fires are always shown**: the lead on the test, the cakes, the acid, the kettle's lead and
   crust, the coke and embers. They could become parts of their own, hidden when a furnace is cold, as the amalgam
   pan's contents are. The embers do not glow (the shape has no glow values).
8. **The build stages** within each level (which items fit which set, in what order) are not designed yet.
9. **The controller cell** (the cupellation furnace's front) and the anchor faces were chosen for the layout; the
   gameplay's placement rule (which way the works extend from the player) is to be set.
10. **Drawing it in the game.** 233 of the 312 elements drawn at once are fitted sets that never move; gameplay should mesh the
    fitted static sets with the frame and draw only the four moving parts in the renderer.
11. **The game's rig loader** (`Machines/Core`) does not read `fitted` yet (the viewer does).

## Known weak spots, and what is not checked

The model has been reviewed in the site's model viewer (the standalone build, WebGL in a headless browser) at
every level and at several crank angles, from the front, both angles and above. It has not been seen in the game.

1. **Box teeth.** The bevel teeth are boxes on the pitch cones, thinner than half the pitch so they never touch.
2. **The test** is an octagonal ring with its front segment left out, the bone ash and the lead two crossed
   rectangles each.
3. **The cylinder's piston and the kettle's bowl are not drawn**; the paddles are drawn breaking the lead's surface
   (a real stirrer's blades are under it).
4. **The water pipe** is ppex's pipe all the way to the cock, large next to the vessel; it shows where a ppex pipe
   meets the machine.
5. **The engine shaft's end** reaches 2.4 voxels into the kettle's upper cell, so at level 1 that cell collides
   with it alone: the stub the pinion is keyed to at level 2.
