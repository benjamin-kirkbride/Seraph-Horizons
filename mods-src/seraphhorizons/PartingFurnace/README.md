# Parting furnace

Part of the Seraph Horizons mod (`../README.md`): the parting stage of the ore line (ore processing, #684), one
upgradeable machine (#740, on the shared tier code of #711) that does every parting step: cupellation of silver
from lead, liquation of lead from tin, acid parting of silver from gold, and the Parkes process. Its model is
#741.

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what those will be built against. This first pass is the finished tier
4 machine with every set fitted; the build stages within a tier are to be worked out with the owner.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), `Machines/`, this folder's neighbour, or `site/` and `tools/tests/`, which are the
repository's. The generic half of the generator is `Machines/tools/machinegen/`, unchanged by this machine (no new
driver: every motion is an existing `rotate` or `slide` on θ).

## The machine

A small parting works of the 1800s on a brick-paved floor under one chimney stack. Everything in the model was
made for this mod: no other mod's model is used, so the generator needs nothing from `build/mods`.

**Footprint.** Fixed at its tier 4 size from the frame on: 6 cells wide (x) by 4 deep (z) by 3 high, the stack two
more (77.5 voxels in all), 53 cells of that 6 × 5 × 4 box. The works' front, with the fire doors, the breast
and the kettle's door, faces native south (+z); the stack stands at the back. The controller is the middle of the
front row at ground level (build cell (2, 0, 3), shipped `[0,0,0]`). Early tiers look sparse inside it, as #711
asks: nothing moves as the machine is upgraded.

**Plan** (build cells (x, z), north at the top):

| | x 0 | x 1–2 | x 3 | x 4–5 |
|---|---|---|---|---|
| z 0 | blast main | cupellation furnace (back) | the stack | kettle's back; the input chute (x 5) |
| z 1 | blowing tub, under the crank | cupellation furnace | acid parting vessel, its hood against the stack | the Parkes kettle under the stirrer |
| z 2 | liquation furnace (its high end) | the breast and its litharge pot; working floor | precipitating tub | the kettle's front, the siphon, the output chute's head |
| z 3 | liquation furnace, its arch and receiving pot | working floor | working floor | the output chute (x 5) |

**The frame** (`requires` null; the block draws it, `partingfurnace_frame.json`, 83 elements): the brick-paved
floor over the whole footprint, the chimney stack (a plinth, two courses of shaft, iron bands, a fire-brick
corbelled cap with its flue open and soot-black inside) and four cast-iron columns under the line shaft, each
with a plummer block. **The fires' flues run under the floor to the stack and are not drawn**: every furnace
stands on the floor over its own flue, so no duct moves or is added as the machine is upgraded.

**Tier 2** (`t2blast`, `t2cupel`, `t2liquation`):

- *The blowing engine* (`t2blast`). The line shaft comes in from the vanilla axle on the east face and runs the
  works' length on the four columns to an **overhung crank** at its west end. The crank pin runs in the slot of a
  **Scotch yoke** riding in guides on a cast-iron **double-acting blowing tub**; the yoke's piston rod goes down
  through a stuffing box into the tub. Valve chests on the tub's west side; the **blast main** runs north from it,
  along the back with a blast valve, and in through the cupellation furnace's back wall.
- *The cupellation furnace* (`t2cupel`), an English test furnace: a brick hearth block with the **fireplace** sunk
  in its west end (grate bars, coke, the fire), a fire-brick fire bridge, and in the hearth the **test**: an oval of
  bone ash in an iron ring with the molten lead on it. A low segmental **vault** over all, iron **buckstays and tie
  rods**, the **tuyere** through the back wall blowing across the lead to the front, and there the **breast**: the
  ring is open at the front and an iron channel runs the litharge out through the working opening into the
  **litharge pot**. The fire door stands open on its hinges, so the fire shows; the ash-pit door under it.
- *The liquation furnace* (`t2liquation`): a brick base over its firebox, a cast-iron **inclined hearth plate**
  with raised rims on fire-brick steps, the **cakes** sitting on the slope and dross at its foot, a low vault over
  it open at the low end, and a **spout** from there into the **receiving pot**. It stands on the west front corner,
  its arch and pot facing the front and its firebox door the working floor.

**Tier 3** (`t3acid`, `t3precip`):

- *The acid parting vessel* (`t3acid`): a **stoneware vessel** with the aqua fortis in it, set in a cast-iron **sand
  bath** on a brick firebox between two brick piers, under a brick **hood** built against the stack, which takes
  the fumes through its back wall; an iron apron along the hood's front.
- *The precipitating tub* (`t3precip`): an oak tub in two iron hoops, the copper nitrate solution in it, two oak
  bars across its top each with two **copper plates** hung in the solution, on which the silver comes down.

**Tier 4** (`t4kettle`, `t4stirrer`, `t4chutes`):

- *The Parkes kettle* (`t4kettle`): a round brick **setting** over its firebox (fire and ash-pit doors on its front),
  the cast-iron **kettle**'s rim and flange showing above it, the molten lead in it with the **zinc crust** rising,
  and a perforated **skimmer** lying across the flange.
- *The stirrer* (`t4stirrer`): a **bevel pinion** keyed to the line shaft over the kettle drives a **bevel wheel** on
  the top of a vertical steel shaft, carried in two bosses on a cast-iron **gallows** bolted to the kettle's flange;
  two paddles cross on the shaft's foot, in the lead under its surface.
- *The chutes* (`t4chutes`): the **input chute** comes in through the back (its mouth on the input cell's north
  face) and drops the charge into the kettle; a **siphon** over the kettle's front rim runs the lead into a head box
  and the **output chute** out to the output cell's south face. Iron legs carry both.

**Anchors** (`assets/seraphhorizons/config/partingfurnace-rig.json`, shipped frame): `powerCell` `[3, 2, -2]`,
`powerFace` east (the axle comes in along x at the cell's centre, y 40, z 24 voxels in the build frame);
`inputCell` `[3, 1, -3]`, `inputFace` north, with `input.pos` `[3.1875, 1.9112, -3.0]` (the input chute's mouth on
that face); `outputCell` `[3, 0, 0]`, `outputFace` south, with `output.pos` `[3.3438, 0.45, 1.0]` (the output chute's
end on that face). All three cells are the machine's from the frame on.

### Every moving part

θ is the axle's angle; the rig reads nothing else. Every other part (the furnaces, the tub, the gallows, the
chutes, the frame) stands still.

| Part (rig id) [requires] | Driven by | Drives | Driver |
|---|---|---|---|
| Line shaft: the axle's oak continuation, coupling, shaft, collars, the overhung crank's disc, boss, pin and nut (`lineshaft`) [t2blast] | The vanilla axle at the east face | The yoke, through the crank pin; from tier 4 the bevel pinion, keyed to it | `rotate` x, ratio 1, about (y 40, z 24) |
| Scotch yoke: frame, slippers, piston rod and piston (`yoke`) [t2blast] | The crank pin, running in its slot | The tub's piston: the blast | `slide` y, amplitude 3.5 voxels, sin θ |
| Bevel pinion, 12 teeth (`bevelpinion`) [t4stirrer] | Keyed to the line shaft | The bevel wheel | `rotate` x, ratio 1 |
| Stirrer: bevel wheel (24 teeth), hub, shaft, collar, paddles (`stirrer`) [t4stirrer] | The bevel pinion | The lead and zinc in the kettle | `rotate` y, ratio ½, about (x 79.1, z 24) |

The crank pin is north of the shaft at θ 0, so its height is the shaft's plus 3.5 sin θ, and a Scotch yoke follows
a crank pin's height exactly: the `slide` driver is that motion, with no approximation. The bevel pair's pitch
cones share their apex where the axes cross, under the line shaft at the stirrer's axis (module 0.5, pitch radii 3
and 6). Toothed wheels and what they mesh: the pinion and the wheel, only.

### Choices, and why

- **The blast is a blowing tub.** English cupellation furnaces were blown from a fan or a blowing engine, so
  powering the blast changes nothing in the period device. A double-acting cylinder shows its motion (the yoke
  and the rod), where a fan's would be inside a casing. It is worked by a **Scotch yoke** rather than a connecting
  rod and crosshead because the yoke's motion is exactly the existing `slide` driver; a connecting rod's swing and
  rise are not any existing driver's, and a new one is not called for.
- **The crank is overhung at the shaft's end**, so the yoke stands clear of the shaft: a yoke round a through
  shaft would need a window the pin crosses. That is why the axle comes in at the east end and the tub stands at
  the west.
- **The stirrer is powered.** Parkes's zinc was stirred in by hand at first; mechanical stirrers came in later in
  the century. Here it is a vertical shaft geared off the line shaft through a bevel pair, its cause in sight. It
  turns whenever the shaft does (no clutch, no lifting gear): see the open questions. Skimming stays a hand job,
  shown by the skimmer.
- **The acid vessel is stoneware, not cast iron.** Aqua fortis (nitric acid) eats iron; cast-iron vessels were
  used for parting with sulphuric acid. Stoneware or glass on a sand bath is the period practice for nitric parting.
- **The kettle and the tub stand under the line shaft**, the kettle's axis where the bevel pair's axes cross, so
  nothing but the pinion is needed to drive the stirrer.
- **The fires' flues are under the floor** (the frame's), so nothing on the floor moves between tiers and no duct
  crosses the working floor; the acid hood alone vents straight into the stack, against which it is built.
- **The liquation furnace is turned to the front corner**, so the cupellation furnace's breast and fire door, the
  litharge pot and the liquation furnace's own door all face open floor.

## The rig (`assets/seraphhorizons/config/partingfurnace-rig.json`)

The bucking sawmill's schema (`../BuckingSawmill/README.md`, "Rig schema") with no work and no trunk path: θ only.

- `cells`: the 53 cells, each with up to three boxes rebuilt from the shipped shape posed at rest (as the rosser's,
  the gear cutter's and the draw bench's are). No cell is hollow, and **no column has a lid** (see the open
  questions).
- `powerCell`, `powerFace`; `inputCell`, `inputFace`, `input.pos`; `outputCell`, `outputFace`, `output.pos` (above).
- `tiers`: `{ "2": [...], "3": [...], "4": [...] }`, the `requires` values of each tier's sets, in order. The viewer
  lists it as a key it does not draw.
- `parts`, in tier order, the frame last: `lineshaft`, `yoke`, `tub` [t2blast]; `cupel` [t2cupel]; `liquation`
  [t2liquation]; `acid` [t3acid]; `precip` [t3precip]; `kettle` [t4kettle]; `gallows`, `stirrer`, `bevelpinion`
  [t4stirrer]; `chutes` [t4chutes]; `frame` (null). No part rides another.

`requires` values are one per fitted set, prefixed with their tier (`t2blast` ... `t4chutes`).

## Model files

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/partingfurnace.json` | The whole machine, every tier's sets (759 elements). |
| `assets/seraphhorizons/shapes/block/partingfurnace_frame.json` | The frame only (83 elements): the floor, the stack, the columns. |
| `assets/seraphhorizons/config/partingfurnace-rig.json` | Cells, anchors, tiers and the part rig (13 parts). |
| `tests/PartingFurnace/rig-reference.json` | Every part's matrix at ten poses of θ, from the reference maths. |

Textures are the game's, referenced: brick `clay/brick/eight/running/orange1`, the floor's `red1`, fire brick
`cream1`, cast iron `metal/plate/iron`, steel `metal/sheet-plain/steel1`, oak, oak planks, bone ash
`stone/rock/chalk1`, lead, tin and zinc ingots for the metals, litharge `clay/hardened/orange`, coke and ember for the
fire, sandstone sand for the sand bath, `clay/ceramic-dark` for the stoneware, `liquid/dilutedalum` for the aqua
fortis, `liquid/dye/blue` for the copper nitrate, copper sheet for the plates and `black` down the stack.

## Regenerating

```sh
python3 mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/PartingFurnace/tools/make_shape.py --quick      # skips the z-fighting fix (not for files that ship)
```

A full run takes about ten seconds. Two runs write the same bytes (`tools/tests/test_partingfurnace_model.py`
checks that the generator reproduces the committed files).

## Validation (`tools/validate_partingfurnace.py`)

Every run checks its output and exits non-zero if a check fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Tiers:** every `requires` value is one tier's and named for it; the parts are listed tier by tier, the frame last.
- **Nothing floats, at any tier:** the static elements of the frame, then of the frame with tier 2, with tiers 2–3
  and with tiers 2–4, are each joined to the ground (a later tier's set may not hold up an earlier one's).
- **Containment:** nothing leaves the 6 × 5 × 4 box over 25 poses, and no moving part reaches a cell the model does
  not hold at rest.
- **Anchors:** the line shaft's oak end meets the power face at the power cell's centre; the input chute's mouth
  is on the input cell's north face, the output chute's end on the output cell's south face, their anchor points
  with them.
- **Textures** by role (22 rules, every element covered).
- **Gearing:** the bevel pair's axes cross at its apex, its ratio is its teeth's, and the pitch point moves alike
  on both gears at six angles either way (finite differences of the posed rig).
- **Yoke:** at 48 angles the crank pin stays on the slot's centre line and within its length.
- **Supports:** the line shaft in its four plummer blocks and the stirrer's shaft in the gallows' two bosses (each
  round its shaft at rest and turned 45 degrees); the yoke's slippers on their guides through the stroke.
- **Flows:** the litharge channel ends over its pot, the liquation spout over its pot, the blast main reaches the
  tuyere, the input chute ends over the kettle's lead, the siphon dips into it and runs out into the head box.
- **Clearances:** over 99 poses through the two-turn cycle (and backwards), no moving part touches anything but
  its intended contacts (`ALLOWED`: shafts in bearings, the pin in the slot, the rod in the tub, the slippers in
  their guides, the pinion on the shaft, the stirrer in its bosses and the lead). The bevel teeth clear each other:
  they are 0.36 of the pitch thick, cast teeth with backlash.
- **No z-fighting** (full runs): no coplanar overlapping faces at four poses, after the fix.
- **Files:** every texture declared; no hollow cells, no lids; the shipped model is the checked one moved.

## Editing by hand

Element names are the rig's interface (first-match globs): `ls_*`, `yk_*`, `tb_*`, `cp_*`, `lq_*`, `ac_*`,
`pr_*`, `kt_*`, `gl_*`, `st_*`, `bp_*`, `ch_*`, and `fr_*` for the frame. Hand edits are lost when the script runs
again: port them into `make_shape.py`, or stop regenerating.

## In the viewer

`site/models.json`, `parting-furnace`: θ's slider spans the rig's two-turn cycle (the stirrer turns once in two),
Play turns the axle at 1.5 s a turn (the play script has no phases: the rig reads θ alone), and a **Tier** select
(one group of states) shows the frame, tier 2, tier 3 or tier 4, each with every set before it, tier 4 by default.
The checkboxes under it fit or take off each set by hand. The anchors draw as the power, input and output cells and
the two chute ends.

`site/test/partingfurnace.test.ts` replays the reference poses through the viewer's maths and checks the anchors,
the scenario and the tier states; `tools/tests/test_partingfurnace_model.py` does the same in Python and checks the
generator reproduces the files, the cells, the ratios and the yoke.

## Open for the owner

1. **Does tier 4 keep the tier 2 and 3 furnaces?** The model keeps them: the Parkes process still needs the rich
   lead cupelled, and liquation and acid parting serve other ores. #711's rule that the next tier's set replaces
   the last one's working parts would take them off; the states would then need a different tier 4.
2. **The powered mechanisms.** A blowing tub on a Scotch yoke for the blast (a fan, or lever bellows on a crank, are
   the alternatives); a powered stirrer on a bevel pair where Parkes stirred by hand. The stirrer turns whenever the
   shaft does; a clutch, or lifting the stirrer out for the crust to rise, would need inputs the rig does not have.
   Skimming is left to the hand (the skimmer is a prop).
3. **Stoneware or cast iron for the acid vessel** (stoneware chosen, for nitric acid; cast iron would suit sulphuric).
4. **"Everything chute-fed" at tier 4.** Only the kettle is on the chutes (in at the back, out by the siphon at the
   front); the other furnaces have no visible launders to the input or the output. Whether the two chutes stand
   for the whole machine's input and output, or launders between the units are wanted, is open.
5. **The footprint.** 6 × 4 leaves a large open working floor in front of the cupellation furnace (cells (1–3, 2–3)).
   A tighter footprint would need the furnaces rearranged.
6. **Walking on top: no lids.** The tops are at many heights (open floor, pots, vaults, the hood, the kettle); a
   deck at one height per layer would be an invisible floor over the working floor and the kettle. Which columns
   get one is the gameplay's call.
7. **Drawing it in the game.** 502 of the 759 elements are fitted sets that never move. Drawn by a renderer every
   frame they would cost far more than the frame does; gameplay should mesh the fitted static sets into the
   block's chunk mesh with the frame, and draw only the four moving parts (`lineshaft`, `yoke`, `bevelpinion`,
   `stirrer`) in the renderer.
8. **The charges and fires are always shown**: the lead on the test, the cakes, the acid, the solution, the kettle's
   lead and crust, the coke and embers. No input says when a furnace is working or empty; they could become parts
   of their own, hidden when idle. The embers do not glow (the shape has no glow values).
9. **The cupellation furnace's fire door stands open** (to show the fire); the other fire doors are shut.
10. **The build stages** within each tier (which items fit which set, in what order) are not designed yet.
11. **The controller cell** (the middle of the front row) and the faces of the power, input and output cells were
    chosen for the layout; the gameplay's placement rule (which way the works extend from the player) is to be set.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has looked at it in a client
or in the site's viewer with WebGL yet (the standalone copy was checked to load and to show its controls).

1. **Box teeth.** The bevel teeth are boxes on the pitch cones, thinner than half the pitch so they never touch; the
   bodies step under the root cones.
2. **The test is three rectangles** (an oval of bone ash and of lead), in a ring of 14 straight segments with two
   left out at the front for the breast.
3. **The stirrer's paddles are under the opaque lead** and cannot be seen; the bowl of the kettle below the lead is
   not modelled (the setting hides it).
4. **The piston is drawn inside the tub**, unseen; the tub's valves are boxes.
5. **The furnaces' insides are not modelled** beyond what the doors and openings show: the cupellation furnace's
   fireplace and test, the liquation hearth through its arch.
