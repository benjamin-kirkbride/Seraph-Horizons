# Mandrel forging station

Part of the Seraph Horizons mod (`../README.md`): a rung of the pipe ladder (unified pipes, `../Pipes/`).
A smith's **mandrel station** of the 1700s hammers one lead or copper **hollow section** (vanilla's
chute section, `game:chutesection-lead` or `-copper`: an 8 × 8 square box, 8 long, 1-voxel walls) down
onto an iron mandrel into a pipe-sized tube, which comes off as two **pipe sections**
(`seraphhorizons:pipesection-{lead,copper}`: a square tube 6 across, 1-voxel walls, 8 long, half a
block). One hollow makes two sections; a section with solder makes a straight pipe.

This folder holds the model's generator and its rig (`tools/`); the gameplay is built against the rig
("Gameplay" below).

**A hand station.** It has no mechanical power, no power cell and no oil. The blows are the player's
right-clicks with a hammer, as at the anvil; no hammer is modelled, since the player holds it. The rig's
θ is the hammer's clock: every motion is posed by the forging W, and θ moves nothing.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's
generator; the generic half of it is `Machines/tools/machinegen/`, shared with the other machines and
unchanged by this one (no new driver: every motion is a `gauge`).

## The station

**Footprint.** 2 cells: 1 wide (x), 1 high (y), 2 long (z). In the native, south-facing frame with
the controller at `[0,0,0]`: x 0, y 0, z 0..1. The controller is the stump, `[0,0,0]`, the block the
player clicks. Placed like the press brake and the draw bench, the station extends away from the
player along their line of sight, the stump nearest them and the mandrel pointing away: the block's
`side` is the way they look. A chest of hollows can stand beside the stump (native west, `infeedSide`);
when a hollow is done, gameplay delivers its two sections beyond the tip (native south, `outputSide`),
into a container standing there, or onto the ground at `output.pos`. The model draws nothing coming off.

**Why 1 × 1 × 2.** The finished tube is 16 long (two sections) on a mandrel that also needs a root
held in a bracket: the hollow starts against the shoulder at z 7 and the finished tube ends at z 23, half a
voxel short of the tip at 23.5. `output.pos` is on the ground beyond the tip (z 28), where the sections are
dropped.

**Parts, by build order.** The block itself (`seraphhorizons:mandrelstation-frame-{side}`, its recipe
gameplay's) is the frame: the oak stump with its iron hoop, the iron bracket and the swage. Then one
item:

| `requires` | Item | Draws |
|---|---|---|
| `mandrel` | `game:rod-iron`, `-meteoriciron` or `-steel` | the mandrel: root in the bracket, collar (the shoulder), square body, tapered nose |
| `hollowlead` | the work, a lead hollow (k 1) | the hollow and the two lead sections it becomes |
| `hollowcopper` | the work, a copper hollow (k 2) | the same in copper |

Why one stage: the bracket and the swage are forged with the station, as the brake's gallows are; the
mandrel is a bar, the game's bar is the rod, and it is the part a smith would take out and swap.

## Model

`assets/seraphhorizons/shapes/block/mandrelstation.json` (the whole station, every moving part) and
`mandrelstation_frame.json` (the frame only: the block draws it, the inventory shows it), the rig
`assets/seraphhorizons/config/mandrelstation-rig.json` and the reference poses
`tests/MandrelStation/rig-reference.json`. All four are written by `tools/make_shape.py`; nothing is
taken from another mod's model.

### How the station works

| Part | Driven by | Drives |
|---|---|---|
| The stump (oak, iron hoop) | fixed | carries the bracket and the swage |
| The bracket (base plate; two bands, each a saddle, two cheeks and a cap) | fixed to the stump | holds the mandrel's root at two places, against the cantilever |
| The mandrel (`mandrel`) | fixed in the bracket | takes the blows: the hollow is hammered onto it |
| The swage (a block with a square groove) | fixed | none: a tool beside the mandrel, not animated |
| The hollow (eight rings `1`..`8`: four walls and four corner bars each) | the blows, the mandrel under them | closes from 8 to 6 across onto the mandrel and stretches from 8 to 16 long, evenly, in one motion over the whole work; at W 1 it stays on the mandrel (rings 1..4 and 5..8 are the two sections) |

The forging (t = W, one hollow; the rig holds none of these times):

| t | |
|---|---|
| 0.00..0.02 | the hollow lies on the mandrel against the shoulder (p eases in) |
| 0 | the hollow lies on the mandrel against the shoulder, 8 × 8, 8 long (p eases in) |
| 0..1 | forged: it closes from 8 to 6 across onto the mandrel and stretches from 8 to 16 long, evenly along its whole length, together, in proportion to W |
| 1 | one 6 × 6 tube, 16 long, its far end at the tip, the part line between rings 4 and 5 showing; it stays on the mandrel. Gameplay delivers the two sections and stops drawing the work; nothing slides or drops |

**How it closes and stretches at once.** No rig driver scales a part, so the hollow is drawn as eight
short rings (2.1 long) along the mandrel, each four walls 6 wide and four 1 × 1 corner bars. At rest the
rings overlap, filling the hollow's 8; over the forging every ring's walls slide in by 1 and its corner
bars diagonally by 1 on both axes (so the cross-section goes from 8 across to 6, its bore from 6 to the
mandrel's 4), while each ring slides along the axis from its place at rest to its place in the forged
tube: the first not at all, the last 8, the rest in proportion to their place in their section. All of
it is one gauge window, linear in W, so the tube closes and lengthens together and evenly. The forged
tube is two sections of four rings each, the rings in a section overlapping a little (0.13) and the two
sections meeting end to end at the part line (where a ring's side walls and corner bars stand back at
their ends, a hairline shows there at W 1). At every tenth of W the work is checked to be one
continuous square tube of even cross-section, with no gap between rings; the volumes where rings, walls
and corner bars overlap are never seen.

**It hangs on the mandrel.** A loose box on a bar hangs from it: at every W the bore's ceiling (the top
wall's underside) rests on the bar's top face, and the gap is below the bar, never round it. At rest the
6 bore on the 4 bar puts the box's centre 1 below the mandrel's axis (y 12.6; the box's bottom 0.6 over
the stump). As it closes, the top wall stays on the bar, the side walls come in by 1 and the bottom wall
comes up by 2, so the section's centre rises by 1 with the forging, and the finished tube's bore closes
round the bar. Checked at every tenth of W: the lowest ceiling of the ring stack on the bar's top within
0.01 (each ring's within the z-fighting fix's steps, 0.09 at most), and no element of the work into the
bar by more than 0.01.

**Faces that share a plane.** The overlapping rings' outer faces lie in one plane all the time, so the
z-fighting fix steps them in by 0.015 each, up to 0.105 deep for a stack of eight; no end faces share a
plane (a ring's side walls stand back 0.03 at its ends and its corner bars 0.06), so the joints between
rings are not opened by the fix.

### Regenerating

```sh
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/MandrelStation/tools/make_shape.py --quick     # skips the z-fighting fix and the swept paths
```

A full run takes about 20 s and is deterministic (two runs into two folders are byte-identical, logs
included). It exits non-zero if a check fails (`tools/validate_mandrelstation.py`):

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **Frame:** joined to the ground as one piece (the stump, hoop, bracket and swage).
- **Textures by role:** the stump oak; hoop, bracket and swage iron; the mandrel `mandrel`; each metal's
  work its own sheet.
- **Containment:** nothing leaves the 1 × 1 × 2 box over the whole forging, either metal, every 0.01.
- **Forging:** at every tenth of W, one continuous square tube of even cross-section (every sample of its
  walls covered, nothing in its bore or outside it, all along it, but for 0.1 either side of the part
  line), (8 − 2W) across and (8 + 8W) long, with no gap between rings; at W 1 closed onto the mandrel and
  still on it (its far end short of the tip), its two sections (rings 1..4, 5..8) each 6 × 6 × 8 and end to
  end; `output.pos` beyond the tip. The tolerance is 0.15 voxels, the fix's deepest step and a margin.
- **Hang:** at every tenth of W the bore's ceiling on the bar's top (the stack's lowest within 0.01, every
  ring's within 0.15), nothing of the work into the bar by more than 0.01.
- **Mandrel:** through both bands of the bracket, each band's saddle, cheeks and cap bearing on it; the
  hollow against the shoulder; θ moves no part.
- **Clearances:** no two parts overlap at any pose every 0.02 of the forging (the work's own rings,
  walls and corner bars excepted), and the **swept paths** every 0.0025: the hollow stretching along the
  mandrel.
- **No z-fighting** at twelve poses, rest to delivered, after the fix (21 faces pressed against their own
  part removed, the rest stepped in by 0.015; the fix runs up to 40 rounds for the stacks of rings).
- **Files:** every texture code declared; no `lid` on any cell (a hand station is not walked on); no
  power cell; the shipped files are the
  checked model (no move: the build frame's corner is the controller's).

### Rig schema (`mandrelstation-rig.json`)

The gear cutter's schema (`../GearCutter/README.md`, "Rig schema") as the press brake uses it, with
nothing added to the shared rig maths: a `work` quantity, gauges, and one θ-driven rotate of ratio 0.

- **The work.** `{ "name": "blows", "unit": "hollows", "step": 0.005, "end": { "thin": 1, "thick": 1 } }`.
  W is one hollow's forging, 0..1: blows struck over blows needed.
- **Inputs.** W, k and p, and θ. k is the hollow's metal (1 lead, 2 copper, 0 none). p is its presence,
  eased in as the hollow goes on and out after delivery, with k held while p eases out. Gameplay advances
  W by `1 / blowsPerHollow[k]` a blow, delivers two `forge.sections[k]` at W = 1, then clears. As with
  the press brake, every gauge eases back as p eases out, so a renderer stops drawing the work once its
  sections are delivered.
- **`forge`**: `blowsPerHollow` (lead 6, copper 9: the pace), `hollows` (each class's work), `sections`
  (what a hollow makes) and `sectionsPerHollow` (2).
- **Anchors**: `output.pos` (on the ground beyond the tip, where gameplay drops the two sections), `strike.pos` (the top of the box's
  middle, where the hammer lands: sparks and the blow's sound), `infeedSide` west, `outputSide` south.

**Keys.** `cells`, `infeedSide`, `outputSide`, `output`, `strike`, `work`, `forge` and `parts`.
`requires` values: `mandrel`, `hollowlead`, `hollowcopper`, or null. The texture code the renderer sets:
`mandrel` (the fitted rod's metal; iron plate in the shape).

**In the viewer** (`site/models.json`, `mandrel-station`): the θ slider is the hammer's blows and moves
nothing; the forging's slider reads in hollows; the size select names the hollows ("Lead", "Copper"), and
each metal's work shows only with its own metal (`requiresClass`). Play moves W with θ at lead's pace, a
turn a blow (`play.turnsPerWork` `"forge.blowsPerHollow.thin"`).

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `mandrel_*`; per metal
(`l`, `c`) and ring (`1` at the shoulder .. `8`; `1`..`4` are the near section, `5`..`8` the far one) the
walls `l1u_*`, `l1d_*`, `l1e_*`, `l1w_*` and the corner bars `l1ue_*`, `l1uw_*`, `l1de_*`, `l1dw_*`; and
`fr_*` for the frame. Hand edits are lost when the
script runs again: port them into `make_shape.py`, or stop regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files and in the site's own viewer
(a standalone copy). No one has yet looked at it in a client.

1. **The mandrel is cantilevered** 17.3 voxels past the bracket (10 past the stump): a smith slips the
   hollow on and the tube off over its free end. The bracket holds it at two bands 3.6 apart.
2. **Ring joints.** The hollow is eight rings, so the game will show seams where their textures meet
   (like rings of blows), and the stretch is carried by the rings sliding apart, not by the texture
   stretching. Through the open end the overlapping rings' ends can be seen inside the bore.
3. **Stepped faces.** The overlapping rings' outer faces are stepped in by up to 0.105 voxels (the
   z-fighting fix); a ring's side walls and corner bars stand back 0.03 and 0.06 at its ends.
4. **No slide-on, no slide-off.** The hollow appears on the mandrel as p eases in; at W 1 the finished tube
   stays on the mandrel and gameplay drops the two sections as items at `output.pos` and stops drawing it.
   Nothing is seen coming off. (Every gauge eases back as p eases out, so the work must not be drawn then.)
5. **Blows.** The forging is linear in W, so each blow (1/6 of a lead hollow, 1/9 of a copper one) closes and
   stretches it by the same amount; the renderer eases W between blows.
6. **Linear ramps**, as the press brake's: the motion starts and stops at full speed.
7. **The swage is decoration**: nothing is swaged in it.
8. **Lead and copper look the same** but for their texture.
9. **No lids.** The cells have their boxes and no collision-only deck: a player cannot walk across the top.

## Gameplay

The rules are game-independent in `Core/` (`Forging.cs`: the `requires` vocabulary, the mandrel, the
hollows by metal and the job; `MandrelStationConfig.cs`; `MandrelStationRig.cs`; and
`MandrelStationView.cs`: the renderer's view of the station and its clock); the game side is `Game/`
(`MandrelStationSystem`, the controller `BlockMandrelStation` and `BEMandrelStation`, the ghost
`BlockMandrelStationGhost` with `BEMandrelStationGhost`, and `MandrelStationRenderer`), copied from the
press brake's (`../PressBrake/README.md`, "Gameplay") with its held lever replaced by blows. The mod's
README, "Mandrel forging station", is the player-facing summary; the decisions are here. Everything is
read from the rig through its anchors, `forge` and `requires`, never element names.

**Blocks.** `seraphhorizons:mandrelstation-frame-{side}` (`assets/seraphhorizons/blocktypes/mandrelstation/frame.json`,
drawing `mandrelstation_frame.json`; its textures are every texture of both shapes, `mandrel` included)
and `mandrelstation-ghost` for the far cell. The contract writes the block as `mandrelstation-{side}`;
gameplay names it `-frame-{side}`, as the press brake's and the draw bench's are, since what is placed is
the frame and the mandrel is fitted to it. Placement (`side` = the player's look,
`MandrelStationRig.PlacedSide`, the stump nearest them and the mandrel pointing away), the ghost, its
repair, breaking through it and the boxes (selection from the rig's cells, collision with their lids)
are the press brake's. The frame recipe (`assets/seraphhorizons/recipes/grid/mandrelstation.json`): an
oak log stood on end for the stump, an iron or steel plate (the bracket's base plate and the swage), two
nails and strips of iron, meteoric iron or steel (the hoop and the strap bands) and a hammer. It takes
no rod: the mandrel is the one fitted part.

**The mandrel** (`MandrelPart`): `game:rod-iron`, `-meteoriciron` or `-steel` (verified in game 1.22.7's
`survival/itemtypes/part/rod.json`), fitted by right-click on the stump or the ghost; a second is
refused with a message, and a rod of another metal is the item's own business. Ctrl + right-click takes
it back while nothing is on it (the contract has it back only by breaking; the brief asked for this, as
on the press brake). The creative shortcut (Ctrl in creative mode with no mandrel) fits an iron one free.
Breaking drops the frame, the mandrel and a hollow not yet struck (a struck one is lost).

**Blows** (the anvil's pattern: `BlockEntityAnvil` takes a hammer's hits). A hollow section, the game's
`chutesection-lead` (k 1) or `-copper` (k 2), the rig's `forge.hollows`, goes on a bare mandrel by
right-click; nothing else does (an angle, a pipe section, an ingot, a plate: `Forging.ClassOfHollow`),
and only when its pipe section exists (`UnifiedPipes`' item; with that switch off a hollow is refused
with a message). Then **each right-click with any of the game's hammers** (`game:hammer-*`) on the
station is a blow, no faster than one each `Forging.BlowIntervalMs` (300) on a station, so right-click
held does not hammer faster than a smith swings. A blow advances W by (the hammer's tool tier /
`BaseHammerTier`) / `BlowsPerHollow` of the hollow's metal (`Forging.WorkPerBlow`, `ForgeJob.Strike`):
`BlowsPerHollow` is the count for the base hammer, the copper one, and the tier is read from the held
hammer's item (`CollectibleObject.ToolTier`, the game's `tooltierbytype`: copper, gold and silver 2, the
three bronzes 3, iron and meteoric iron 4, steel 5) at each blow; a hammer with no tier counts as the
base, and one blow never advances more than half a hollow (`Forging.MaxWorkPerBlow`). So lead takes 6
blows of a copper hammer, 4 of a bronze one, 3 of an iron or steel one; copper 9, 6, 5 (iron) and 4
(steel). The blow that brings W to 1 finishes it (the overshoot is dropped). A blow plays the anvil's `game:sounds/effect/anvilhit` and throws a few
small glowing sparks at `strike.pos`, and costs the hammer `HammerWearPerBlow` durability (not in
creative mode), one a blow whatever the hammer. At W = 1 the hollow is used up and `forge.sectionsPerHollow` (2)
`forge.sections[k]` (`seraphhorizons:pipesection-{metal}`) go into a container in
`MandrelStationRig.OutputNeighbour()` (the cell beyond the tip, native south), else drop just past it at
`OutputDrop()`, pushed outward, as the press brake's angle does. Each blow syncs W and the blow count.
Ctrl + right-click takes a hollow back only before the first blow; after it, it stays (an error says
so), and so does the mandrel. **Infeed:** a hand station takes nothing by itself. A blow on a bare
mandrel takes one hollow from a container in a cell beside the station (`InfeedNeighbours`, native
west: beside the stump or beside the tip's cell) and loads it; that click is not a blow. A save keeps
the mandrel's code, the hollow, its class, the blows and W.

**Settings** (`MandrelStationSettings`): `BlowsPerHollowLead` 6 and `BlowsPerHollowCopper` 9, the rig's
`forge.blowsPerHollow` (`MandrelStationRigTests.The_default_pace_is_the_rigs` holds them together),
counted for a hammer of `BaseHammerTier` 2 (the game's copper hammer's tier); the block info shows the
blows struck and W as a percentage, since the blows left depend on the hammer.
`HammerWearPerBlow` 1, as a blow on the anvil costs a hammer one. Nothing of the station wears, so there
is no oil and no tool of its own.

**Renderer** (`MandrelStationRenderer`, through `IMandrelStationView`: facing, the mandrel fitted and its
metal, the hollow's class, the server's W and blows). Every rig part with a `requires`, a ride or a
driver, from `mandrelstation.json`, drawn when fitted (the mandrel), with the texture code `mandrel` set
to the fitted rod's metal (`MachineMeshes.MetalTexture`, one mesh set a metal). The hollow's parts
(`hollowlead`, `hollowcopper`) are drawn only while that metal's hollow is on: once the sections are
delivered they are items, and the work is not drawn while p eases out (the contract's "Changes").
`MandrelStationClock` eases W and θ (2π a blow) to the server's after each blow, exponentially, settling
in about a tenth of a second (weak spot 5: a blow can end part way through a window); with no hollow on,
W is held at 1 while p eases out over 0.4 s and k is held; the next hollow starts from the server's W.

**Handbook.** Three sections on the frame (`attributes.handbook.extraSections`): assembly, forging pipe
sections, and the ladder (two angles and solder make a hollow; a hollow on the mandrel gives two pipe
sections, or four on the draw bench; a pipe section and solder make a pipe). With the switch off,
`MandrelStationSystem.UnlinkText` strips the mod's own links to the station (`handbook://block-seraphhorizons:mandrelstation*`
and the other features' `handbooksearch://mandrel station`).

**Export.** `tools/recipe-export/Recipes/MandrelStationExport.cs` and `RecipeSection.MandrelStation.cs`
write one `machine` record per metal (`mandrelstation|game:chutesection-{metal}|0`): the hollow
consumed, the mandrel kept, the hammer as a tool worn `HammerWearPerBlow` × blows a job (`wear`
`fixed`, so the site shows it as the worn tool, not consumed), the frame as the station, two pipe
sections out; `power` `hand`, `turns` the base (copper) hammer's blows and `work` that many `blows` (the
site says "By hand: 6 blows a job, each a right-click with a hammer"), the definition's hammer
`game:hammer-copper`; a better hammer's fewer blows are the handbook's (the schema has no note on a
recipe); no `oil`. Type `mandrelstation`, owned by
`MandrelStation` (`Core/SwitchOwnership.cs`).

**Tests.** `tests/MandrelStation/MandrelStationGameplayTests.cs` (the mandrel and its take-back, saves,
hollows by metal and what is refused, hammers, blows and the last one, copper's nine, the blow interval,
the clock, settings, and the reader on a rig written to the contract with its refusals) and
`MandrelStationRigTests.cs` (the shipped rig through the shared parser and `MandrelStationRig`, its
anchors, the pace held to the settings, the reader's refusals, and every pose of
`tests/MandrelStation/rig-reference.json` replayed through `Machines/Core`);
`tests/PackTests/MandrelStationScenarios.cs`, `RecipeExportMandrelStationScenarios.cs` and
`SwitchesOffScenarios.Mandrel_station_off_there_is_no_mandrel_station` (Atlas:
`FullyQualifiedName~Mandrel_station`).

**Not done.** No hand animation of its own: the click plays the game's default use pose. No schematic
for the frame (`MachineSchematics`).
