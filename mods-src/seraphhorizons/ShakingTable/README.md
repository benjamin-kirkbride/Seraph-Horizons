# Shaking table

Part of the Seraph Horizons mod (`../README.md`): the third concentrator of the ore line (epic #684), a
**Wilfley table** of about 1900. A wide deck with riffles along it, tilted a little across its width, is shaken
along its length by a **differential head**, a slow push forward and a quick pull back: the heavy grains walk
along the riffles to the far end while the wash water carries the light ones down across the deck to its low
side. Crushed or ground ore comes in by chute; three chutes take out the **concentrate** (off the far end), the
**middlings** (off the far end's lower corner: grains with metal still locked in rock, for the ball mill) and the
**tailings** (off the low side).

This folder holds the model's generator and its rig (`tools/`) and the gameplay (#921, `Core/` and `Game/`, on
the ore machines' shared game code; the mod's README, "Shaking table"); the model is #922. The line, the glossary and the period sources are #939, the power and water budget #940, the
shared machine code (chutes, the water reservoir) #711.

**Salvaged** from `735-concentrator-model-v2` (its wash house's table, its evaluation in the v3 brief): the box,
disc, slope and launder helpers, the deck's boards, joists, riffles, feed box, water box, lip and `tilt`, the
joists' slides (`joist_under`), the vanilla chute's profile and its check against the game's file
(`VANILLA_CHUTE`, `read_vanilla_chute`), the soldered water line (`pipe_boxes`, `build_pipes`, `pipe_copies`)
and the validator's structure (parts, textures by role, flows by ray-drop, clearances with intended contacts,
z-fighting). **New**: the drive (the axle into a closed rectifier case, the head in its own closed box, the
flywheel and pull rod), the stroke as harmonics, the footprint and the layout round it, the middlings launder
and splitter, the three output chutes and the infeed and water inlet from above, the upper side's posts, and
the stroke's, rectifier's and anchors' checks.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. The generic half of the generator is
`Machines/tools/machinegen/`, shared with the other machines and **unchanged** by this one: no new driver (the
stroke is three plain `slide` drivers on the axle's travel).

## The machine

**Footprint.** 2 wide (x) × 1 high × 3 long (z), six cells, all solid. The deck runs along z, its **head end**
north (the front, the player's side, where the controller is) and its **far end** south; its **upper edge** is
west, its **low side** east. The controller `[0,0,0]` is the head end's upper corner, where the axle comes in and
the rectifier case stands. Each dimension is what has to fit: the head end's drive (the rectifier case, the head
box, the flywheel and pedestal) takes the first 14 voxels of length, the deck 28 (with its stroke), the launders
across the far end the last 5.5; across, the deck takes 25 and the tailings launder along the low side 4. One
high holds the deck at a working height (its top 9.5 at the upper edge) over launders that reach the chutes'
floors (y 5), with the water line over the deck flush with the top.

**Anchors** (`assets/seraphhorizons/config/shakingtable-rig.json`), each a cell and the face a neighbouring
block meets:

| Anchor | Cell | Face | What meets it |
|---|---|---|---|
| `power` | `[0,0,0]` | west | The vanilla axle, along x at the cell's centre (the axle's oak cross starts flush with the face) |
| `infeed` | `[0,0,1]` | up | A vanilla chute coming down, onto the infeed chute section, into the feed box |
| `water` | `[0,0,2]` | up | A ppex pipe coming down, onto the water line's elbow (open at the top, flush with the face) |
| `concentrate` | `[0,0,2]` | south | A vanilla chute, on the concentrate launder's chute section |
| `middlings` | `[1,0,2]` | south | A vanilla chute, on the middlings launder's |
| `tailings` | `[1,0,0]` | east | A vanilla chute, on the tailings launder's |

No two want the same neighbouring block, and none is inside the footprint (checked). There is no return-water
anchor (#711, rule 4: the water is used up). Each chute section is the game's straight chute exactly (its walls,
an 8 × 8 tube 1 thick, a 6 × 6 bore, centred in the cell's face, in `game:block/metal/sheet/copper1`), flush with
its face, so a chute placed beyond meets it with no step. Where the tailings chute is not chuted on, #921
discards the tailings beside it as sand: the neighbouring block of the tailings face.

**Fitted parts** (the rig's `requires`). Everything with `requires` null is the frame: the stand (sills, legs,
slide plates, rails, the tie), the head's bed sills, the three launders, the splitter and their blocks, and the
upper side's posts, rail and straps. The rest:

| `requires` | Parts | Draws |
|---|---|---|
| `deck` | `deck` | The deck: its boards, the far end's lip, two joists, the head end's bearer and the cast bracket under it, eight riffles, the feed box and the water box |
| `rectifier` | `rectifier`, `entry` | The rectifier's closed gear case (joint flanges, bosses, cover, filler); the axle's oak cross into it |
| `head` | `head`, `headshaft`, `pullrod` | The differential head's closed box with its bosses, the pull rod's guide, the cover with the stroke-adjusting screw and handwheel; the outer pedestal; the head shaft with its coupling, flywheel and two collars; the pull rod and its nuts |
| `chutes` | `chutes` | The four chute sections: the infeed and the three outputs |
| `pipecopper`, `pipelead` | `pipecopper`, `pipelead` | The water line, soldered, in copper or in lead (the same elements; the supplied metal's drawn; in the viewer the Build stage select's two services options) |

**Build stages** (as settled by the owner, 2026-10-15). The frame is placed (no `requires`); each stage after it
is one `requires` group, fitted in order, and the machine doesn't run until it is complete. The generator's
`BUILD_STAGES` is this list, written into the rig as `buildStages`; its checked states (`STATES`) are the frame
and each stage on the ones before it, named for the stage. Materials are the settled figures (one rate: 128 vox
of iron or steel an ingot, a plate 2 ingots; rods counted, then plates, then nails and strips; boards in planks at 128 vox, timbers in support beams at 256;
plus 1 iron nails and strips per 4 planks' worth of timber (a support beam 2), for the fastenings).

| # | Stage | `requires` | Why here | Materials |
|---|---|---|---|---|
| F | Frame (placed) | — | The ground | 4 iron nails and strips, 9 plank |
| 1 | Deck | `deck` | The joists rest on the frame's slide plates | 4 iron nails and strips, 10 plank |
| 2 | Rectifier | `rectifier` | The case stands on the frame's bed sills; the axle's cross enters its west boss | 3 `seraphhorizons:gear-stainless` (the closed rectifier's gears, not drawn), 2 iron plate, 1 iron nails and strips, 1 plank |
| 3 | Differential head | `head` | The head shaft's first bearing is the rectifier's east boss, and its pull rod pins into the deck's bracket, so both are in already | 1 `seraphhorizons:spring-steel` (the return spring), 2 iron plate, 5 iron nails and strips, 2 steel rod |
| 4 | Services | `chutes`, `pipecopper` / `pipelead` | Last, as on every machine: the four chute sections and the water line (copper or lead, a choice) | 4 chute section, 1 `seraphhorizons:pipesection-{copper,lead}` |

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. The timber is the game's debarked oak, the boards its oak planks, the castings and straps its iron
plate (`game:block/metal/plate/iron`), the shafts and rod its plain steel sheet, the chutes its copper chute sheet,
the water line UnifiedPipes' copper or lead pipe texture with lead-solder joints. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/shakingtable.json` | The whole machine, every fitted part, both water lines (281 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/shakingtable_frame.json` | The frame only (45 elements): the block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/shakingtable-rig.json` | The cells, the anchors, the stroke's figures and the part rig (10 parts). |
| `tests/ShakingTable/rig-reference.json` | Every part's matrix at a spread of poses (θ, ψ), from the reference maths. |

### How the machine works

Positions here are in the generator's **build frame**: the native axes (x west to east, y up, z north to south)
in voxels, from the machine box's north-west bottom corner, which is the controller's; the shipped files are the
build frame divided by 16.

**The drive.** The vanilla axle comes in at the west face along x at (y 8, z 8), the power cell's centre, and
continues as an oak cross in the game's axle profile (two 4 × 2 bars) into the west boss of the **rectifier**: a
closed cast-iron gear case (x 2.6..8.2) on two oak bed sills, with a joint flange at the shaft's height, a bolted
cover and a filler. Inside (not drawn) a pinion and wheel each way, one through an idler, on one-way catches: its
output shaft, the **head shaft**, leaves the east boss on the same line and turns one way, three times as fast as
the axle, whichever way the axle turns (#922 rule: "reversed, the differential head's quick stroke would be
forward"). The head shaft (steel, through a flanged coupling) runs through the **head box** (x 12..19), carries a
six-spoked cast-iron **flywheel** past it and ends in the outer **pedestal**, located endwise by two collars
either side of it. It is carried by four bearings: the case's east boss, the head box's two bosses and the
pedestal.

**The head.** A closed cast-iron box: inside it (not drawn) the eccentric on the head shaft, its pitman and the
toggles that turn each turn into a slow push and a quick pull, and the return spring; on its cover the
stroke-adjusting screw with a small handwheel. The **pull rod** (steel, along z at x 15.4, y 5.4) comes out of a
guide boss low on the box's south face and is pinned, between two nuts, in a cast bracket under the deck's head
end, on a cross bearer between the joists: it moves with the deck (`ride`).

**The deck** (x 3..27.8, z 14.5..42.5), planked, on two oak joists along its length, is built level and tilted
2.5° about its upper edge, falling east 1.1 across its width. The joists run on four iron slide plates on oak
legs, on two sills with a tie between them, a rail along each pair of legs. On the deck: eight oak **riffles**
along it (x 13..27, every 2), each tapering from 1.0 high at the head end to 0.35 and ending on a diagonal, the
upper ones shortest, so the smooth corner at the upper side of the far end is where the concentrate band, rising
out of the riffles, is washed clean on its way to the far end (v2 had the diagonal the other way; see "Open");
the **feed box** at the head end's upper corner under the infeed chute, its deck side a low weir the pulp spills
over; the **water box** along the upper edge under the water line's nozzles, a tall outer wall and a low weir;
the **lip** past the far end, over the end launders.

**The stroke.** The deck moves along z only, one stroke a turn of the head shaft (three a turn of the axle): a
slow push south (forward, towards the concentrate end) for 69 % of the stroke, then a quick pull back north,
2.9 times as fast at its fastest, its speed changing sign exactly twice a stroke. It is drawn as the first three
harmonics of a sawtooth (the push its ramp) smoothed by Lanczos' sigma factors so it has no ripple, scaled to a
1.2-voxel stroke (±0.6 about the deck as built; the period's was ½ to 1 inch, drawn larger to be seen), each
harmonic a `slide` along z on the axle's travel ψ:

| n | Amplitude (voxels) | Ratio (on ψ) | Phase |
|---|---|---|---|
| 1 | 0.5084 | 3 | 0 |
| 2 | 0.1797 | 6 | π |
| 3 | 0.0565 | 9 | 0 |

**The launders**, of oak planks on oak blocks: the **tailings launder** along the low side (x 26.8..30.6) catches
what leaves the deck's low edge and falls north to its outlet in its east wall at the head end, onto the
tailings chute; across the far end, under the lip, the **concentrate launder** (west of the splitter at x 19)
falls west to its outlet in its south wall, and the **middlings launder** (east of it, under the far corner)
falls east to its outlet. Each launder's floor meets its chute's floor (y 5.0) at the outlet, a hair above it.
The **splitter** is a board between the two end launders, standing on the ridge where their floors meet, its top
under the lip.

**The water line** is Pipes and Power Expanded's pipe drawn soldered (`Pipes/tools/solderedpipe.py`, as the
rosser's drip pipes): an elbow under the water face's centre, open up (the ppex pipe above meets it there) and
north; a run north over the water box at y 10..16, its end plugged with solder; four nozzles under the run's
upper-edge side, dripping into the water box. The upper side's three oak **posts** and the rail along their tops
carry the infeed chute and the water line over the moving deck by iron straps.

Every moving part (θ the axle's angle, ψ its travel either way, both radians):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Axle's oak cross (`entry`) [rectifier] | The vanilla axle | The rectifier's input (inside the case) | `rotate` x about (y 8, z 8), ratio 1, on θ |
| Head shaft: rod, coupling, flywheel, collars (`headshaft`) [head] | The rectifier's output (inside the case) | The head's eccentric and toggles (inside the box) | `rotate` x about (y 8, z 8), ratio 3, on ψ |
| Deck: boards, joists, bearer, bracket, riffles, boxes, lip (`deck`) [deck] | The pull rod | — | three `slide` z on ψ (the table above) |
| Pull rod and nuts (`pullrod`) [head] | The head's toggles (inside the box) | The deck (pinned in its bracket) | none: rides `deck` |
| Rectifier case, head box and pedestal, chutes, water lines, frame | Static | — | none |

There are no gears drawn, so no toothed wheel meshes with nothing. The rectifier's and the head's works are
inside closed castings, as #922 asks; what shows is their effect: the cross turning either way, the flywheel
always the same way, the deck's uneven stroke.

### Regenerating

```sh
python3 mods-src/seraphhorizons/ShakingTable/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/ShakingTable/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/ShakingTable/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths (not for files that ship)
```

A full run takes about 10 s and is deterministic (two runs into two folders are byte-identical, logs included).
`tools/validate_shakingtable.py` holds the checks; every run checks its output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part.
- **Requires and states:** the vocabulary (the frame's, `deck`, `rectifier`, `head`, `chutes`, the two water
  lines); the lead line is the copper line's elements; the pull rod rides the deck only where the deck is fitted.
- **Stages:** every `requires` in one stage, services (the chutes and both water lines) last; the rig's
  `buildStages` the same; the states the frame and each stage on the ones before it; each stage's parts touching
  what came before them. The head after the rectifier and the deck: on the head's own bosses and pedestal its
  shaft has three bearings, and the rectifier's east boss makes the four it needs.
- **Textures by role:** oak timber and the axle's cross; planks for the deck, its boxes, the launders and the
  splitter; iron for the castings, the flywheel, the slides, straps and bracket; steel for the shafts, the rod,
  the coupling's bolts, the collars and the screw; `chute` (the game's chute texture) for the chutes; each water
  line in its metal and solder.
- **Thickness:** nothing under 0.6 across but bolt heads and the handwheel's rim and spokes.
- **Nothing floats:** at rest, the frame alone and each state's parts with it, one piece from the ground.
- **Containment:** nothing leaves the 2 × 1 × 3 box over a stroke either way.
- **Anchors:** the axle's cross from the power face at the cell's centre in the game's axle profile; the water
  line's elbow open on the water face's centre, ppex's 6 × 6, up to the face; every chute the game's section at its
  cell's face, flush with it; `VANILLA_CHUTE` the game's own file (when the game is here); one neighbouring block
  each, none inside the footprint.
- **Flows** (ray-drops over a stroke): the infeed chute's bore onto the deck inside the feed box; each nozzle onto
  the deck inside the water box; the deck's low edge into the tailings launder; the lip into the concentrate
  launder west of the splitter and the middlings launder east of it; each launder's floor falling to its outlet,
  meeting its chute's floor there, and its mouth dropping into the chute.
- **The stroke:** 1.2 voxels; one a turn of the head shaft; forward (south) 60 to 80 % of the stroke; its speed
  changing sign twice; the return at least twice as fast as the push; the rig's slides against the harmonics.
- **The rectifier:** for ±θ the head shaft and the deck are the same; the head shaft turns 3ψ; the cross follows
  θ either way; the pull rod moves with the deck.
- **Supports:** the head shaft in four bearings (found by `supports`), the cross in the case's west boss; the
  collars 0.05 either side of the pedestal; the joists on their slides through a stroke; the pull rod through its
  guide, into the head box and through the bracket through a stroke.
- **Clearances:** no two parts closer than 0.02 at 16 poses a stroke in each state and backwards, but for the
  listed contacts (the shafts in their bosses, the rod in its guide, box and bracket); **swept paths**: the moving
  parts against everything every 1.25° of the axle over a stroke.
- **No z-fighting** at rest and part way through a stroke, finished, frame only and deck only.
- **Files:** every texture declared; lids over every column; no anchor's cell hollow.

### Rig schema (`shakingtable-rig.json`)

The shared rig format (the bucking sawmill's and rosser's READMEs, `docs/recipe-browser/models.md`). This rig adds
no driver and no input: θ and ψ only, no work.

| Key | |
|---|---|
| `cells` | The six cells, each with up to three boxes from the finished machine at rest (copper line), and a lid. |
| `powerCell`, `powerFace` | The axle's cell and face. |
| `infeedCell`, `infeedFace` | The infeed chute's. |
| `waterCell`, `waterFace` | The water line's inlet. |
| `concentrateCell`/`Face`, `middlingsCell`/`Face`, `tailingsCell`/`Face` | The three output chutes'. |
| `stroke` | `strokesPerTurn` (3: the deck's strokes and the head shaft's turns a turn of the axle, either way), `length` (blocks), `forward` (`south`, the slow push's way). Not drawn by the viewer. |
| `buildStages` | `[{ stage, id, name, adds }]`: each build stage after the placed frame, in order, and the `requires` it fits (services: `chutes` and the two water lines, one of which is drawn). |
| `parts` | The ten parts above, in first-match order. |

### Editing by hand

Don't: regenerate. Element names are the rig's interface (`entry_*`, `headshaft_*`, `deck_*`, `pullrod_*`,
`rc_*`, `hd_*`, `ch_*`, `pipecopper_*`, `pipelead_*`, `fr_*`).

## Provisional

- The rig's `stroke` object: figures for the site, not read by the game (the deck's 3 strokes a turn are
  `ShakingTabling.StrokesPerTurn`; the work is counted in axle turns, as every ore machine's).

## Known weak spots, and what is not checked

- The rectifier's and the head's works are not drawn (closed castings), so nothing checks them: the 3:1 step-up
  and the toggles are figures in the rig, not geometry.
- The stroke is drawn about 1.2 voxels, larger than the period's ½ to 1 inch at this scale, so it can be seen.
- The riffles taper by being turned about x, their feet sunk in the deck; the deck's boards are not split along
  the riffles.
- The water box's end walls are low, as its weir is: the water line passes over them.
- Nothing has been seen in the game: lighting, texture stretch, flicker at distance, item forms.

## Open for the owner

1. **The riffles' diagonal**: drawn with the upper riffles shortest, leaving a smooth cleaning corner at the upper
   far corner (my reading of the Wilfley deck's plan, not checked against a source); the v2 branch had the upper
   ones longest. Which?
2. **The rectifier's step-up**: 3:1 (three strokes a turn of the axle: 86 a minute at the reference speed 0.6,
   144 at speed 1; the period's were about 250). Higher looks busier; 1:1 would be a slow 29 a minute.
3. **The stroke's size and shape**: 1.2 voxels, a push for 69 % of the stroke and a return 2.9 times as fast,
   three harmonics. More harmonics give a sharper return.
4. **The head's internals**: the box is closed, as #922 asks. A cut-away or inspection cover could show the
   toggles if wanted (they would need their own motion, likely a new driver: not added).
5. **The feed and water boxes' size**: each sits under an 8 × 8 chute or a 6-wide pipe centred in its cell, so
   they are wider than a period table's narrow boxes (the feed box 8 across a 25-wide deck).
6. **The infeed and the water come from above**, the only faces a chute or pipe can deliver onto the deck from at
   one block high; the tailings chute is at the head end of the low side (its launder falls north).
7. **The upper side's posts and rail** carrying the infeed chute and the water line over the moving deck are this
   model's own; a period table hung them from the mill's timbers.
8. **The deck's proportions**: 25 wide by 28 long (about 1.1 : 1), against the period's 6 by 16 ft; the head end
   and the launders take the rest of the 2 × 3 footprint.
9. **Where the v2 branch differed from #922** (the issue followed): v2 shook the deck with a sinusoid from an
    eccentric and yoke on the wash house's line shaft, with no rectifier (now a differential head behind a
    rectifier, three harmonics of a quick-return stroke); its deck stood at y 16.8 in a two-high house (now one
    high, the deck's top at 9.5); it was fed by the jig's weir spout and emptied into the house's launders (now a
    chute in, three chutes out); it had no middlings (now its own launder and chute); its riffles' diagonal ran
    the other way (item 2).
