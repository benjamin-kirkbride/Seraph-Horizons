# Jaw breaker

Part of the Seraph Horizons mod (`../README.md`): the first machine of the ore line (epic #684), a **Blake jaw
breaker under a grizzly**, one real piece of equipment. Ore comes in by the game's chute onto the
grizzly's head and runs down its sloping bars: the fines fall between them, the oversize slides off their foot into
the breaker's mouth. The breaker crushes it between a fixed iron jaw and a swinging one; its product and the fines
meet in a small iron hopper under it and run out through the output chute, low on the front face. It is dry, and it
takes mechanical power from a vanilla axle at one side, coupled straight onto the eccentric shaft: a Blake works
either way round, so there is no rectifier.

This folder holds the model's generator and its rig (`tools/`). The gameplay is #912 and is not built yet; the model
is #914. "Gameplay" below says what the rig offers it.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour. `tools/` is the model's generator; the generic
half of it is `Machines/tools/machinegen/`, shared with the other machines and unchanged by this one (no new driver:
the linkage is the shared `swing` and `slide` drivers, fitted harmonics of the axle's angle).

**Where it comes from.** The breaker comes from the stamp mill v2 (`origin/729-stampmill-model-v2`, its
`StampMill/tools/make_shape.py`) moved out into a machine of its own, as #914 asks: the Blake linkage built in its own
frame (`build_jaw_frame_local`, `build_eshaft_local`, `build_linkage_local`, `build_jaw`), its exact solve (`jaw_at`,
`linkage`, `solve3`) and its fit to harmonic drivers (`fit`, `harmonics`), the flywheels (now one, 2 blocks across), the buffers'
shares (now the springs'), `build_grizzly` (`griz_top`, `griz_under`, `throat_under`), `build_chute` with `VANILLA_CHUTE`, and the
validator's breaker checks. What changed is listed under "From the stamp mill v2" below.

## The machine

**Footprint.** A 2 wide × 3 high × 2 deep box (x west to east, y up, z away from the player; the controller at the
front), and three more cells behind it in the west column, `[0,0,2]`, `[0,1,2]`, `[0,2,2]`, for the flywheel's back
third: 15 cells. Each dimension is what has to fit:

- **2 wide:** the cheeks with the flywheel outside the west one (x 0.4..18.8), and the drive end: the eccentric shaft
  runs on east to the power face through an outboard pedestal.
- **The flywheel's cells:** one flywheel 2 blocks across on the eccentric shaft, whose axis is the power cell's centre
  line (y 24, z 24). Upright it fits (y 8..40, over the ground and under the infeed), but it reaches z 8..40, half a
  block behind the box. The shaft cannot move without moving the axle (or adding a gear pair), and a pit gains
  nothing (the wheel already clears the ground by 8), so the footprint grows by the three cells the wheel's rim sweeps
  through, and only those: the east column's back stays free.
- **3 high:** the output chute low on the front face and the hopper over it (y 4..14.5), the breaker on its stand's
  cross sills (from y 14.5, the eccentric shaft at y 24 on the power cell's centre line, the jaws to y 31), the
  grizzly falling 40° to the mouth (its bars' top from y 45.1 at its head to 36 at its foot), and the infeed's chute
  section in the top face (y 45..48).
- **2 deep:** the breaker's frame (front wall to back wall, z 7.95..31.23: 1.45 blocks) and the grizzly's run in front
  of it (its head at z 2.4, its foot over the mouth at z 13.2).

The controller `[0,0,0]` is the west column's front bottom cell, where the output chute leaves; the build frame's
corner is the controller's, so the shipped files are the build frame as it is.

**Both chutes in one column.** With no middle column, the infeed and the output are both in the **west column**
(x = 0), in its front row: the infeed on the top face of `[0,2,0]`, the output on the north face of `[0,0,0]`. A
vanilla chute section is centred in its cell's face (4..12), so the breaker's chamber (x 5..17) is set as far west as the
flywheel outside the west cheek allows: the infeed's bore (x 5..11) lands wholly between the grizzly's curbs (x 5..17),
and the hopper takes the discharge's east half west into the output's trough. The east column carries the drive end.

**Anchors** (in `assets/seraphhorizons/config/jawbreaker-rig.json`), provisional until the shared machine code (#711)
settles how chutes attach:

| Key | Value | What it is |
|---|---|---|
| `powerCell`, `powerFace` | `[1, 1, 1]`, east | The vanilla axle, along x at (y 24, z 24), onto the eccentric shaft's oak stub |
| `infeedCell`, `infeedFace`, `infeed.pos` | `[0, 2, 0]`, up, `[0.5, 3.0, 0.5]` | The infeed: a vanilla `chute-straight-ud` placed on this face carries straight on into the machine's own section |
| `outputCell`, `outputFace`, `output.pos` | `[0, 0, 0]`, north, `[0.5, 0.5, 0.0]` | The output: a vanilla `chute-straight-ns` against this face carries on from the machine's section |

**Build stages** (as settled by the owner, 2026-10-15). The frame is placed, with no `requires`: the timber stand
(four posts, the side and cross sills, the head beam and the top tie), the breaker's cast frame (the cheeks with their
bearings and the rods' abutment lugs, the front wall, the back wall with the back toggle's seat), the outboard
pedestal and the hopper. Then five stages, fitted in order, each one `requires` group; the third takes two clicks, the
toggles and rods and then the springs (the draw bench's mandrel-and-spring pattern). The services come last, as on
every machine: the chute sections are their own part (`chutes`), no longer the frame's. The rig's `buildStages` lists
them (`stage`, `id`, `name`, `adds`: the `requires` values it fits, in click order).

| # | Stage (`id`) | `requires` (parts) | Draws | Materials |
|---|---|---|---|---|
| F | Frame (placed) | none (`frame`) | The stand, the cast frame, the outboard pedestal, the hopper | 9 iron plate, 2 iron nails and strips, 1 steel nails and strips, 11 support beam |
| 1 | Jaws (`jaws`) | `jaws` (`fixedjaw`, `swingjaw`) | The fixed jaw's steel plate, the hinge pin, and the swinging jaw hung on it | 2 iron plate, 1 iron nails and strips, 1 steel rod, 1 steel plate, 2 steel nails and strips |
| 2 | Eccentric shaft and flywheel (`eccentric`) | `eccentric` (`eshaft`, `pitman`) | The eccentric shaft with its eccentric, the flywheel, the collar, the coupling and the axle's stub; the pitman hung on the eccentric | 4 iron plate, 5 iron nails and strips, 2 steel rod, 1 steel plate, 2 steel nails and strips, 1 plank |
| 3 | Toggles, rods and springs (`toggles`) | `toggles` (`backtoggle`, `fronttoggle`, `rods`), then `springs` (`spring1`..`spring4`) | The two toggles and the two tension rods with their nuts and pins; then a steel coil spring on each rod | 2 steel rod (the rods); then 2 spring (`seraphhorizons:spring-steel`) |
| 4 | Grizzly (`grizzly`) | `grizzly` (`grizzly`) | Five bars, two side curbs, the head block, the cross bar on its two brackets, the throat plate and its lip | 5 iron rod |
| 5 | Services (`services`) | `chutes` (`chutes`) | The two chute sections: the infeed in the top face, the output low on the front face | 2 chute section |
| Wear | Jaw faces | (in `jaws`) | The fixed plate and the swinging jaw's face: a wearing part, renewed whole (never turned round), not a stage | 2 steel plate |

The materials are the owner's figures (one rate: 128 vox of iron or steel an ingot, a plate 2 ingots, counted rods,
then whole plates, then nails and strips; boards in planks at 128 vox, timbers in support beams at 256); the items' exact codes are gameplay's (#912).
Fitted out of order, the swinging jaw would rock with nothing driving it: gameplay should hold the shaft still until the
machine is complete. The grizzly lies over the mouth, so it goes in after the linkage.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. The chute sections are the game's own straight chute's walls (`shapes/block/metal/chute/straight.json`),
held as data. Textures are the game's: debarked oak for the stand's timbers and the axle's stub, iron plate for
castings and the grizzly, riveted sheet iron for the hopper, plain steel sheet for shafts, pins and wearing parts (the
jaws' faces, the toggles, the rods, the springs), and the chute's copper sheet for the chute sections. `tools/make_shape.py`
writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/jawbreaker.json` | The whole machine, every stage's parts (188 elements). The renderer splits it into parts by element name. |
| `assets/seraphhorizons/shapes/block/jawbreaker_frame.json` | The static frame only (36 elements). The block draws it and the inventory shows it. |
| `assets/seraphhorizons/config/jawbreaker-rig.json` | The cells, the anchors, `breaker`, `buildStages` and the part rig (14 parts). |
| `tests/JawBreaker/rig-reference.json` | Every part's matrix at 25 axle angles, from the reference maths. |

### How the machine works

Positions in this section are in the generator's build frame: the native axes (x west to east, y up, z north to
south) in voxels from the controller cell's north-west bottom corner, so the shipped files are the build frame
divided by 16. The breaker is built in its own frame, y as here and u along the ore's flow from the back wall to the
fixed jaw, and mirrored in, z = 41.35 − u, so its fixed jaw faces the front and the pitman's foot lies under the
power cell's centre line.

**The stand** (oak) has four posts, 3.5 square, outside the hopper and the chute: two tall front posts (z 0.4..3.9,
to the top) and two short back posts (z 28.1..31.6). Side sills run along z between them (y 9.5..12); two cross sills
lie on the side sills (y 12..14.5), one under the breaker's front wall and fixed jaw (z 7.95..11.35), one under its
back wall. Between the front posts, the head beam (y 37.4..40) carries the grizzly's head block, and the top tie
(y 45.5..48) has the infeed's section bolted against its back face.

**The breaker's cast frame** stands on the cross sills: two cheeks (x 3.2..5 and 17..18.8), each a base flange, a front
plate beside the chamber to the top (y 31), a post carrying the eccentric shaft's bearing (with a boss outside the
cheek) and, at its top, the hinge pin's bearing, and a low back part at the back wall; the front wall behind the fixed
jaw; the back wall with the back toggle's steel seat. The chamber between the cheeks is x 5..17, open at the bottom
between the walls, where the product falls.

**The drive.** The vanilla axle meets the eccentric shaft's oak stub (the axle's own cross profile) at the east face;
an iron coupling joins the stub to the steel shaft, which runs west through an iron pillow block on an oak post
(the outboard pedestal, x 25..28.2), a steel collar against the east bearing's boss, both cheeks' bearings, and the
**flywheel** outside the west cheek, the side away from the axle: a period Blake's, 2 blocks across, with a heavy cast
rim (2.8 deep, 2.4 wide, 24 segments), six straight arms and an octagonal hub keyed to the shaft by a gib-headed key
whose head stands out of the hub's face. The hub against the west boss and the collar against the east one hold the
shaft endwise. On the shaft between the cheeks is the eccentric, its centre 0.8 off the shaft's.

The stand's west side is cut back to clear the wheel: the cross sills end at x 3, inside it; the back west post stands
at x 3..6.5, inside it; and the west side sill runs only from the front post to the front cross sill, where the rim
passes over it (its lowest there y 14.2).

**The linkage** (Blake's): the pitman hangs from the eccentric by a strap, its foot (y 16.2) between the inner ends of
two toggles: the back toggle (5 long) seated on the back wall, the front toggle (7.5) seated on the swinging jaw's
back. Both sag 1.3 below their seats. As the eccentric lifts the pitman, the toggles straighten and push the swinging
jaw's lower end towards the fixed jaw; as it lowers it, two tension rods, pinned to lugs low on the jaw's back and
running back through abutment lugs on the cheeks, pull the jaw back, a steel coil spring on each between its abutment
and its nut: a square coil of four turns of 0.35 wire (`machinegen.coil`, the shape of the pack's own
`seraphhorizons:spring-steel`, which the mandrel station winds), 3.1 long. The swinging jaw hangs from the hinge pin over its top (y 30.7); its steel face and the fixed
jaw's are 2.5 apart at the bottom as designed and 7 at the top (the mouth). Through a turn the gap at the bottom runs
2.26..2.94 (a throw of 0.68), the jaw closing once a turn; the toggles never pass straight.

The linkage is solved exactly at 128 angles of the eccentric (`jaw_at`: the pitman's, the back toggle's and the front
toggle's lengths, by Newton's method) and each part's motion is fitted to four harmonics of the axle's angle
(`fit`, `harmonics`): the pitman a swing about its mean foot and slides in y and z, the back toggle a swing about its
seat, the front toggle a swing about its mean seat and slides, the swinging jaw a swing about the hinge, the rods
slides, each turn of the springs a share of the rods' travel (by where it lies between the abutment and the nut),
so the coil closes up evenly as the nut comes in. Every pin stays joined within 4.3e-5 voxels.

**The grizzly**: five iron bars (1.2 wide, 1.4 apart) falling 40° from a head block on the head beam (z 2.4) to their
foot over the mouth (z 13.2, just behind the fixed jaw's face at 11.35), where they rest on a cross bar on two
brackets on the cheeks' tops; a curb along each side (its inner faces x 5 and 17). Under the bars, over the front
wall, an iron **throat plate** falls 40° forward and sheds the fines that land there past the front wall's face (to
z 6.95), a lip under its back end standing on the front wall.

**The hopper** (sheet iron) hangs under the front cross sill, z 3..16.4: a trough carrying the output section's bore
back under the breaker (its floor, its west wall the section's, a low lip on its east side), and east of it a floor
falling 44.5° west from past the chamber's east side (x 17.6) to the lip, with a strip along its front and back
edges and plates closing the back. The fines falling in front of the front wall (through the bars and off the throat
plate) and the breaker's product (between the jaws, behind the front cross sill) all fall into it and run out through
the output section.

**The chute sections** are the game's straight chute's four walls, cut to 3 voxels along the chute and turned as the
blocktype turns it (`ud` for the infeed, `ns` for the output): 8 across, walls 1 thick round a 6 × 6 bore, centred in
the cell's face and flush with it, in `game:block/metal/sheet/copper1`.

Every moving part (θ the axle's angle):

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| Eccentric shaft, eccentric, flywheel (rim, arms, hub, key), collar, coupling, axle stub (`eshaft`) [eccentric] | The vanilla axle at the power face | The pitman | `rotate` x about (1.5, 1.5), ratio 1 |
| Pitman (`pitman`) [eccentric] | The eccentric | The toggles' inner ends | `swing` x ×4 about its mean foot, `slide` y ×4, `slide` z ×4 |
| Back toggle (`backtoggle`) [toggles] | The pitman's foot | — (its seat on the back wall) | `swing` x ×4 about its seat |
| Front toggle (`fronttoggle`) [toggles] | The pitman's foot | The swinging jaw | `swing` x ×4 about its mean seat, `slide` y ×4, `slide` z ×4 |
| Swinging jaw (`swingjaw`) [jaws] | The front toggle; the rods pull it back | The ore | `swing` x ×4 about the hinge pin |
| Tension rods, nuts, pins (`rods`) [toggles] | The swinging jaw | The springs | `slide` y ×4, `slide` z ×4 |
| Coil springs, one part a turn (`spring1`..`spring4`, `spring1` at the nut) [toggles] | The nuts | — (they bear on the abutment lugs) | `slide` y ×4, `slide` z ×4, each turn its share of the nuts' travel |
| Fixed jaw's plate, hinge pin (`fixedjaw`) [jaws] | Static | — | none |
| Grizzly (`grizzly`) [grizzly] | Static | — | none |
| Frame (`frame`) | Static | — | none |

There are no gears. Every shaft is carried by bearings fixed to the frame: the eccentric shaft by both cheeks and the
pillow block, the hinge pin by both cheeks' hinge bearings, the rods by the jaw's lugs and the abutments.

### Regenerating

```sh
python3 mods-src/seraphhorizons/JawBreaker/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/JawBreaker/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/JawBreaker/tools/make_shape.py --quick      # skips the z-fighting fix, the swept paths (not for files that ship)
```

A full run takes about 9 s and is deterministic (two runs into two folders are byte-identical, logs included).
`tools/validate_jawbreaker.py` holds the checks; every run checks its output and exits non-zero if one fails:

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty part; the
  requires are the frame's none and the stages' fits; the build is the owner's order (jaws; eccentric; toggles, then
  springs; grizzly; the services last, the chute sections alone in them); every driver reads θ alone (no travel: either way round).
- **Nothing floats:** the frame is one piece from the ground, and so is each build state at rest.
- **Containment:** nothing reaches into a cell that is not the machine's (the box and the flywheel's three cells) over a
  turn.
- **Anchors:** the axle's stub reaches the power face on the power cell's centre line; the infeed and output anchors
  on their cells' faces, at their sections' mouths; both chutes in one column.
- **Paths:** the infeed's stream lands between the grizzly's curbs, over its bars; the bars fall at least 40° and
  stand at least 1 apart, their foot over the mouth and their span within the chamber; the throat plate falls at least
  38° and ends past the front wall's face; the fines and the discharge fall into the hopper; its floor falls at least
  40° to the trough, whose floor carries on the output section's; the front cross sill lets the fines fall in front of
  it and the discharge behind it.
- **Chutes:** `VANILLA_CHUTE` is the game's straight chute (from, to, UVs, rotations, drawn faces, texture), read from
  the game when it is installed (`VINTAGE_STORY` or `~/Games/vintagestory`); each section's profile at its face is a
  vanilla chute's in its cell, flush, 3 long, in the chute's texture.
- **Textures** by role (five rules; every element has one).
- **Jaw:** through a turn (72 steps) the strap on the eccentric, the foot on both toggles and where the solved linkage
  puts it, the toggles on their seats, the rods on the jaw's lugs, all within 1e-3; the toggles never pass straight
  (sag at least 0.3); the gap at the bottom never under 1; the eccentric shaft turns with the axle, once a turn
  either way.
- **Springs:** through a turn, each spring's end turns stay on its nut and its abutment lug (within 0.15 along the
  rod; 0.08 in practice) and its turns never close up (at least 0.1 apart; 0.27 in practice).
- **Supports:** the eccentric shaft in three bearings (the cheeks' posts and bosses, the pillow block), the hinge pin in
  two; the flywheel's hub and the collar on the bosses' outer faces.
- **Flywheel:** one, 2 blocks across (30..34 voxels), outside the west cheek, the side away from the axle; its rim's
  whole sweep inside the machine's cells and 8 over the ground; its arms and key there. Its clearances to the frame
  (the cheek's boss and the hinge pin's end, which its arms pass; the sills and the back post) are the clearance and
  swept-path checks'.
- **Clearances:** at rest in every build state and through a turn every 15°, no two parts touch except the intended
  contacts listed in `ALLOWED`.
- **Swept paths** (full runs): the moving parts through a turn every 2°.
- **No z-fighting** (full runs): no coplanar overlapping faces at nine poses, every build state shown alone. 29 faces
  pressed against their own part are removed; the rest are inset (`fix_coplanar`).
- **Files:** every texture declared; fifteen cells, none hollow, no lids; the shipped model is the checked one.

The cells' boxes are rebuilt from the shipped, rounded shape posed at rest by the shipped rig, everything fitted, as
the other machines' are.

### Rig schema (`jawbreaker-rig.json`)

The shared schema (the bucking sawmill's README, "Rig schema"; `docs/recipe-browser/models.md`) with nothing added to
the rig maths.

- **Inputs.** θ alone, the axle's angle (radians). The eccentric shaft rotates about +x by θ (ratio 1, about the power
  cell's centre line); every other moving part is a sum of `swing` and `slide` harmonics of θ. Nothing reads ψ, the
  axle's travel: the Blake linkage is symmetric, so running backwards it passes the same poses in reverse.
- **Keys.** `cells` (fifteen, each with its boxes; no `lid`), `powerCell`, `powerFace`, `infeedCell`, `infeedFace`,
  `infeed` (`pos`), `outputCell`, `outputFace`, `output` (`pos`), `breaker` (`turnsPerBite`: axle turns a turn of
  the eccentric shaft, 1), `buildStages` (the stages in order, each the requires values it adds) and `parts`. `requires` values: `jaws`,
  `eccentric`, `toggles`, `springs`, `grizzly`, `chutes`, or null.

**In the viewer** (`site/models.json`, `jaw-breaker`): the axle angle is the θ slider, and Play turns it at 2 s a turn
(about the reference speed 0.6, 0.48 turns a second), by its input speed; "turn backwards" runs it the other way. The
Build stage select has the frame and the five stages, each with everything before it, opening on the finished machine.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `eshaft_*` (`shaft`, `eccentric`,
`flyw_*`, `flye_*`, `coupling`, `stuba`, `stubb`), `pitman_*` (`strap`, `body`, `foot`), `backtoggle_plate`,
`fronttoggle_plate`, `swingjaw_*` (`face`, `slab`, `boss`, `web`, `seat`, `lugw`, `luge`), `rods_*` (`w`, `e`, `nutw`,
`nute`, `pinw`, `pine`), `spring1_*`..`spring4_*` (the rod, `w` or `e`, then the side's face `u`, `w`, `d`, `e`),
`fixedjaw_*` (`plate`, `hingepin`), `grizzly_*` (`head`, `bar1`..`bar5`,
`curbw`, `curbe`, `bracketw`, `brackete`, `crossbar`, `throat`, `lip`), `chutein_*` and `chuteout_*` for the services' chute sections (the
walls: `bottom`, `top`, ...), and `fr_*` for the frame (`post_*`, `sill_*`, `cross_*`, `beam_head`, `tie_top`, `cheek_*`,
`backwall`, `backseat`, `frontwall`, `ped_post`, `ped_block`, `hopper_*`). Hand edits are lost when the script runs again: port them into `make_shape.py`, or stop
regenerating.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has yet looked at it in a client.

1. **The springs close up by sliding.** Each turn slides its share of the nut's travel, so the coil shortens
   evenly, but each turn keeps its own pitch: where one turn's last side meets the next turn's first, the corner
   opens or overlaps by up to a few hundredths of a voxel.
2. **The rods' pins turn with the rods.** The pin through the jaw's lug follows the rods' slides, not the jaw's swing;
   the lug and the pin stay together within 3.5e-5, but the pin does not rotate in it.
3. **Round parts are polygons:** the flywheel's rim has 24 sides, the shafts and hubs 8, the pins and rods 6.
4. **The flywheel's cells hold its rest pose's boxes.** Its rim is a full ring, so they cover its sweep but for the
   arms, which turn.
5. **Ore is not drawn.** Nothing shows rock on the grizzly, in the jaws or in the hopper.
6. **The chute sections' UV turn** follows machinegen's `rotate` (right-handed); their walls' positions are checked
   against a vanilla chute, but which way their copper's texture runs on each face has not been compared with the
   game's tessellation of a turned chute.
7. **Collision is everything fitted.** The cells' boxes are the finished machine's; a frame without its stages has the
   same boxes.

## Open for the owner

1. **Which column the chutes are in.** Both are in the west column, front row (rule: no middle column). The chamber is
   set as far west as the flywheel allows (x 5..17), so the infeed's bore lands inside the grizzly's curbs and the
   hopper moves only the discharge's east half; the east column carries the drive end. The other way (a centred
   breaker) needs the infeed's stream turned 8 voxels sideways at the grizzly's head.
2. **Direct drive or a gear pair.** The axle is coupled straight onto the eccentric shaft (#914 allows either), so the
   shaft's axis is the power cell's centre line, y 24: the eccentric sits at mid-height of the frame and the pitman is
   short (7.8). A gear pair would let the eccentric sit at the frame's top, as a Blake's does, and could step the speed
   up: a period Blake ran at about 250 turns a minute; this one bites once an axle turn (about 29 a minute at 0.6).
3. **The power side** is east (the outboard pedestal and coupling in the east column); west would put the axle on the
   west face of `[0,1,1]` right outside the flywheel's hub, with no pedestal, and the wheel would move east.
4. **The flywheel's three cells.** The wheel (2 blocks across, on the axle's centre line at z 24) reaches half a block
   behind the 2-deep box, so the footprint adds `[0,0,2]`, `[0,1,2]`, `[0,2,2]` (15 cells, an L in plan) rather than a
   third full row (18 cells, the east three empty). The other ways: a smaller wheel (at most 16 across fits the box), or
   a gear pair from the axle to a shaft further forward (which the breaker's frame in front of the shaft does not allow
   without moving the grizzly over it).
5. **Build stages: settled** (2026-10-15; "Build stages" above). Still open: the frame's grid recipe, and the items'
   exact codes (#912).
6. **The chute sections are the services'**, the last stage, so a chute can be laid to the machine only once it is
   finished.
7. **No lids.** A deck at the top of the columns would be an invisible floor over the grizzly and the drive end.
8. **The rods' springs bear on lugs inside the frame** (on the cheeks' posts), not behind the back wall as in the v2
   model: that kept the breaker 1.45 blocks deep (#914 guessed about 1.3) with the grizzly in front of it.
9. **The front posts run the full three blocks** to carry the grizzly's head beam and the infeed's tie; the outboard
   pedestal is a lone oak post.
10. **Anchors are provisional** pending #711: the infeed and output as `<name>Cell`/`<name>Face` plus a `pos`, as the v2
   branches have them.
11. **Rig extras** for gameplay: `breaker.turnsPerBite` (1) and `buildStages`; drop them if #912 does not read them.

## From the stamp mill v2

What #914 asked to change, and how, against `origin/729-stampmill-model-v2`:

- **Its own orientation and stand:** no longer mirrored and raised onto the battery's frame at z 26..64; mirrored once
  (z = 41.35 − u) so the fixed jaw faces the machine's front, on a stand of its own.
- **The grizzly's head on its own beam**, between two tall front posts, in place of the battery's back tie.
- **The axle onto the eccentric shaft directly**, in place of the belt from the line shaft; no flywheel is a pulley.
- **One flywheel, 2 blocks across** (the owner's review), on the side away from the axle, in place of the v2's two
  (8 in radius, one outside each cheek). With no rectifier in front of it, the rig reads θ, not ψ.
- **The discharge into a hopper and the output chute**, in place of the battery's feed box; no shared gear case.
- **Steel springs for the india-rubber buffers:** nothing in the pack makes rubber; the pack's own mod makes
  `seraphhorizons:spring-steel` (wound at the mandrel station), and later Blake breakers had springs there. The
  lugs moved forward a voxel (z 23.2..24.2) to give the coil 3.1 voxels.
- **A compact Blake:** toggles 7.5 and 5 (were 10 and 10), the eccentric's throw 0.8 (1.2), the jaws 2.5 apart at the
  bottom and 7 at the top (3 and 9.5), the cheeks 1.8 thick (3), so the frame fits 2 deep with the grizzly in front of it.
- **Kept:** the exact solve and the four-harmonic fit, the slab, seats, lugs and web of the swinging jaw, the strap,
  the buffers' shares (for the springs' turns), the grizzly's bars, cross bar, brackets, throat plate and lip, `VANILLA_CHUTE` and its check
  against the game's file, and the validator's breaker checks (pins within 1e-3 through a turn, toggles never
  straight, the gap never under 1), its clearance and swept-path checks and its z-fighting fix.

## Gameplay

Not built: #912 (raw ore in, crushed ore out, 24 rock an hour at 0.6, its load rising with the rock it bites up to
0.8, 0.16 empty). What the rig offers it, after the other machines' pattern:

- **The clock.** A renderer turns θ with the axle's angle (no rectifier: θ itself, either way) and draws the parts by
  the shared rig maths (`Machines/Core/RigAnimation.cs`); the rig has no work, so nothing follows a job.
- **Parts.** Every part with a `requires` is drawn by the renderer once that stage is fitted; the frame shape is the
  block's.
- **Anchors.** The power cell and face for the mechanical network, the infeed and output cells and faces for chutes.
- **Tests.** `tests/JawBreaker/JawBreakerRigTests.cs` (the shipped rig through the shared parser, its vocabulary, no
  work, its anchors, and every pose of `tests/JawBreaker/rig-reference.json` replayed through `Machines/Core`),
  `tools/tests/test_jawbreaker_model.py` and `site/test/jawbreaker.test.ts` (the generated files against their own
  rules and the site's rig maths).
