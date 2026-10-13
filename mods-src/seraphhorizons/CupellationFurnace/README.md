# Cupellation furnace: the model

Part of the Seraph Horizons mod's ore processing (epic #684): the generated model of the **cupellation furnace**
(model #741, behaviour #740), one machine of the settled lineup, placed as a multiblock like the pack's other
machines. The line, the glossary and the period sources are in #939; the power and water budget in #940.

**Status: the model only, first pass for review.** This folder holds the model's generator and its checks
(`tools/`); there is no gameplay yet (#740). It follows the crucible furnace's conventions
(`../CrucibleFurnace/README.md`): any fired brick, tier-3 refractory lining 3 thick on every hot face, an ash mouth
with a three-place damper, an outlet with the chimney's anchor, iron binding on the faces not shared, and a staged
build. The generic half of the generator is `Machines/tools/machinegen/`, unchanged by this machine: no new driver.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/`, this folder's neighbour.

## The furnace

An **English cupellation (test) furnace** of the nineteenth century [Percy 1870, *The Metallurgy of Lead*, the
English refining furnace; Hofman 1904, *The Metallurgy of Lead*, ch. XI]: air blown across a pool of molten lead lying
in a dish of bone ash (the **test**) burns the lead to **litharge**, which runs off over the test's front (the
**breast**), and the silver stays. The model is one such furnace in a block of brickwork **three blocks wide (x),
three deep (z) and two high**, bound with iron on its front and back:

- **The test and the fireplace side by side in the front two rows** (the spec's layout). West, the **chamber**: 22.5
  wide and 16 deep, its floor (the hearth) at 10.5, its walls rising to the springing at 19, under a **stepped vault**
  (three courses stepping in, to its crown at 23.5) that runs on over the bridge and the fireplace. On the hearth stands
  the **test**: an oval of bone ash 18 by 12.8 inside an iron ring 0.9 thick and 3.7 high (seven sides of an octagon
  inscribed in the oval), its dish's floor at 13, a rim of bone ash round it to 14, and at the front, where the ring is
  left out, the **breast**, bone ash 2.5 thick, with the **gate** cut in it, 1.8 wide, from the dish's floor up: the
  litharge's way out. East, behind a **fire bridge** (3 thick, to 17, 2.8 over the test's rim), the **fireplace**: coal
  on a grate of four firebars (front to back, on two iron bearers built into the bridge and the east wall) over an ash
  pit (the cave), which the air enters at the **ash mouth** in the front face under an iron **damper** (open, half,
  shut, as the crucible furnace's).
- **The flame** crosses the bridge and the test and leaves at the chamber's west end by the **flue opening** in the
  back wall (4.5 by 4.5), along the **back flue** into the back row's west column and up its **stack** (8 by 8) to the
  **outlet** on the top. **The model stops at the outlet**: the player stands the chimney on it, or every furnace but one
  in a row sharing a chimney is **capped** with a flat iron plate flush in the curb's seat.
- **The blast.** Through the back wall, on the test's middle line, the **tuyere** (an iron pipe 2.2 across) blows
  across the lead towards the breast, 2.9 over the rim, its mouth just behind the ring's back side; it drives the
  litharge to the front, out through the gate.
- **The litharge's way.** From the gate an iron **gutter** runs out through the working door's sill, in a groove,
  falling 0.4 over its 7.6, and off its end into an iron **pot** standing in a **bay** of the front face under the
  door; the bay is 12 wide and 3 deep, iron-jambed, with a niche at its back for the pot.
- **The working door** is at the back of the bay, over the breast, its sill level with the test's rim: the lead is
  charged and the silver cake taken out through it. The **fire door** (an iron door, shut, in a cast frame flush in the
  face) is over the grate, above the damper.
- **The blowing engine stands on the back row's top**, on an iron bedplate over the back row's brick base (the spec:
  "the blowing cylinder on the back row's top"). See "The blowing engine", below.

**Footprint and cells.** All 18 cells of the 3 × 2 × 3 box are the furnace's, none hollow; every top cell has a
`lid` (a deck) at the top, so a player walks over the brick and over the engine. The cell over the outlet,
`[-1,2,2]`, is the chimney's. **The controller is the front middle cell at ground level**, `[0,0,0]`, the litharge
bay's (the templates' convention). Placed like the crucible furnace, it extends away from the player along their line of
sight: the working face (the bay, the fire door, the damper) faces them, the stack and the engine are behind.

**Why 3 × 2 × 3.** As small as the test, the fireplace, the walls and the engine allow:

- **Three wide**: 8 of brick west, the chamber's 22.5 (the test 18.4 across its ring, and room round it), the bridge's
  3, the fireplace's 6.5 (a grate about 1.3 ft by 3 ft), 8 of brick east: 48.
- **Three deep**: the front wall's 8 (the bay and the working door through it), the chamber's 16 (the test 13.8 deep
  across its ring and breast), the back wall's 8 (the tuyere and the flue opening through it): the front two rows; the
  back row for the flue, the stack and the outlet (the west column) and the engine (on the base under the middle and
  east columns).
- **Two high**: the ash pit, the grate and the test's hearth near the ground, the chamber and its vault to 23.5, and
  the lining and the brick over it to the top; the engine 16 high on the back row's base.

**Rows.** Furnaces stand side by side along x sharing their side walls: the west face is plain brick over its whole
area, the east face wherever the furnace reaches it (the front two rows and the back row's base). The engine's bay
over the base is open to the east and nothing in it reaches the side plane, so in a row the next furnace's stack
column closes it. The flues between them are implied, as the crucible furnace's.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `powerCell`, `powerFace` | `[1,1,2]`, `south` | The back row's east top cell: the vanilla axle comes in along z at its centre, onto the engine shaft's oak end |
| `outletCell`, `outletFace` | `[-1,1,2]`, `up` | The stack's top cell, in the back row's west column; the chimney (or the cap) on its top, the chimney from `[-1,2,2]` up |
| `chimney` | origin `[-0.5,5,2.5]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line |
| `ash`, `ashSide` | `[1.2969,0.1875,0]`, `north` | The middle of the ash mouth in the front face: the damper, worked from in front |
| `fireDoor`, `fireDoorSide` | `[1.2969,0.875,0]`, `north` | The fire door's middle: coal by hand |
| `door`, `doorSide` | `[0.2031,1.0375,0]`, `north` | The working door's middle at the bay's face: the lead charged, the cake taken out |
| `dish` | `[0.2031,0.8125,1]` | The middle of the test's dish: where the lead and the cake lie |
| `litharge`, `lithargeSide` | `[0.2031,0.4375,0.1688]`, `north` | The litharge pot's mouth: the litharge taken out by hand |
| `tuyere` | `[0.2031,1.0688,1.4375]` | The tuyere's mouth in the chamber: the blast's sound and particles |
| `build` | | The build's stages and their materials (below); not drawn |
| `bricks` | | The fired brick types and their textures (the crucible furnace's); not drawn |
| `furnace` | | `minChimney` 6, `rowMax` 5, `rowAxis` `x`, `doubleActing` true; not drawn |

**Batch, no chutes** (#740, rule 2): the lead in, the silver and the litharge out, all by hand; no chute anchor.
**Services**: blast (the axle), chimney, fuel by hand. The anchors are this model's; #711 (shared machine code) has
not landed, so they are provisional where it may settle them otherwise.

## The blowing engine

The furnace's own blowing cylinder, worked off the vanilla axle (#740: "Blast: the furnace's own blowing cylinder on
the axle (the branch's crank and Scotch yoke)"). It stands on the back row's top, on a cast-iron **bedplate** over the
base, under the middle and east columns:

- **The shaft** continues the vanilla axle: its oak end (the axle's cross profile) at the power face, then a steel
  shaft along z in **two pedestal bearings**; between them a **crank** (two webs and a pin, 3 throw) with the pin under
  the shaft at θ 0.
- **The Scotch yoke**: a slotted iron frame between the webs, the crank pin running up and down its slot, so it slides
  east and west exactly with the pin (x = r sin θ: the rig's `slide`); a steel slipper on its top and bottom runs under a
  top guide and on a rail on the bedplate. The top guide stands on two standards, the west one forked round the piston
  rod.
- **The blowing cylinder**, horizontal along x in the yoke's plane: a barrel 9 across and 11 long between flanges, with
  covers, on two saddles; the **piston rod** runs from the yoke through the gland on its east cover to the piston (hidden
  in the barrel). It is **double-acting**: a **valve chest** at each end, on its north side, with **flap valves**
  (leather flaps on a grid) in its top and north faces, so it draws and blows on both strokes. A Scotch yoke and a
  double-acting cylinder work the same whichever way the axle turns: **no rectifier** (#740).
- **The blast main** runs along the base under the chests to an **elbow** at the test's middle line, and through the
  back wall's face into the tuyere.

Why horizontal: the axle meets the power face at the top cell's centre (y 24), 8 over the back row's base, and the
engine must stay in the 16 voxels of the back row's top cells. A vertical cylinder under the crank, as the parting
furnace's v2 branch had it standing on the floor, needs about 29 voxels of height; turned on its side, in line with the
yoke, it fits with the crank between its bearings and the guides clear of the webs. "Open for the owner" has it.

### Every moving part

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The shaft, its oak end, the webs and the crank pin (`shaft`) | the vanilla axle at the power face (θ, ratio 1 about z) | the yoke, through the pin in its slot |
| The yoke, its slippers and the piston rod (`yoke`) | the crank pin (a `slide` along x, r sin θ: an exact Scotch yoke) | the piston, in the blowing cylinder |
| The piston (hidden in the barrel, on the rod) | the yoke | the air: drawn in through the flap valves at one end and blown out at the other, both strokes |
| The blast (no part) | the cylinder, through the valve chests' discharge, the main and the elbow | the tuyere, across the lead towards the breast |
| The draught (no part) | the chimney on the outlet, natural draught | in at the ash mouth under the damper, up through the grate and the fire, over the bridge and the test, out at the flue opening, along the back flue, up the stack |
| The litharge (`stream`, `litharge`) | the blast, driving it to the front | out through the gate, along the gutter, into the pot |

Everything else that shows or not is a `requires`, gameplay's state: the stages, the coal and the fire, the lead and
the cake, the running litharge and the litharge in the pot, the damper's place, the cap.

## The build (a proposal)

The crucible furnace's pattern: an iron frame by grid recipe (placing it puts the controller and its ghosts down), then
stages, each a `requires` value, in order. The brickwork's and the lining's counts are measured from the model; the
test's bone meal too, at the bone-ash cupel's rate. The frame's recipe and the engine's and the fittings' materials are
proposals to settle with the owner, as the crucible furnace's were.

| | Stage | `requires` | What it puts in | Materials | Gear |
|---|---|---|---|---|---|
| Frame | **Iron frame**: grid recipe, hammer | none | The binding (front and back straps and bands), the grate's bearers and firebars, the ash mouth's lintel, the damper's guides, the fire door's frame, the bay's and the working door's jambs and lintels and the niche's lintel, the tuyere; 7 short posts hidden in the brickwork | 2 iron plates, 4 iron rods, 12 iron nails and strips | 41.75 |
| 1 | **Brickwork** | `brickwork` | The base, the walls, the back row's west column and its base under the engine: 10.59 blocks | **22 fired bricks**, any one type | 7.48 (brown 3.85) |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on every hot face (the chamber's walls, floor and vault, the bridge, the fireplace from its bearers up, the flue opening, the back flue and the stack) and the outlet's curb: 2.97 blocks | **12 tier-3 refractory bricks, 5 fire clay** | 10.11 |
| 3 | **The test** | `test` | The ring and its bone ash: 487 cubic voxels of bone ash | 48 bone meal (at the cupel's 4 for 41 cubic voxels), 1 iron plate | 10.21 |
| 4 | **The blowing engine** | `engine` (and the `shaft` and `yoke` parts) | The bedplate, pedestals, guides, cylinder, valve chests, main and elbow; the shaft and the yoke | 4 iron plates, 2 iron rods, 1 leather (the flaps) | 38.98 |
| 5 | **Fittings** | `fittings` (and the damper's place) | The fire door, the gutter, the litharge pot; the damper | 2 iron plates | 15.42 |
| | *Outlet cap* | `outletcap` | Not a stage: by hand, any time after the lining | 1 iron plate | 7.71 |

A furnace costs **123.95** in gear (120.32 in brown brick). Values from `assets/seraphhorizons/config/item-values.json`.
The test's contents, the fire, the litharge and the damper are drawn once the fittings are in, the cap once the lining
(its curb) is.

## Model

Everything in the model was made for this mod: no other mod's model is used. **No new texture**: every texture is
the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/cupellationfurnace.json` | The whole furnace, every part (544 elements) |
| `assets/seraphhorizons/shapes/block/cupellationfurnace_frame.json` | The iron frame only (46 elements): what the placed frame draws |
| `assets/seraphhorizons/config/cupellationfurnace-rig.json` | Cells, anchors, the build and the brick types, and the part rig (18 parts) |
| `tests/CupellationFurnace/rig-reference.json` | Every part's matrix at 13 values of θ, from the reference maths |

| Code | Texture | On |
|---|---|---|
| `brick` | The furnace's brick type's block texture (the renderer's, from `bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork: world-mapped |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining, the curb: world-mapped |
| `iron` | `game:block/metal/plate/iron` | The frame, the ring, the engine's castings, the webs and the yoke's frame, the fittings, the damper, the cap |
| `steel` | `game:block/metal/sheet-plain/steel1` | The shaft, the crank pin, the piston rod, the slippers |
| `oak` | `game:block/wood/debarked/oak` | The shaft's oak end, continuing the vanilla axle |
| `boneash` | `game:item/resource/crushed/lime` (the fired cupel's) | The test's bone ash |
| `lead` | `game:block/metal/ingot/lead` | The molten lead; glow 200 |
| `silver` | `game:block/metal/ingot/silver` | The silver cake; glow 255 |
| `litharge` | `game:item/resource/crushed/limonite` (the litharge item's base) | The running litharge (glow 160) and the litharge in the pot |
| `leather` | `game:block/leather/plain` | The blowing cylinder's flap valves |
| `coal` | `game:block/coal/bituminous` | The coal on the grate |
| `ember` | `game:block/coal/ember` | The same coal burning, and the cinders; glow 200 |

### Regenerating

```sh
python3 mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/CupellationFurnace/tools/make_shape.py --quick     # skips the z-fighting fix and the fine sweeps
```

A full run takes about 75 seconds and is deterministic (two runs into two folders are byte-identical). It exits non-zero
if a check fails (`tools/validate_cupellationfurnace.py`):

- **Parts:** every element in its intended part; no part without elements; Euler angles round-trip.
- **The stages:** at each stage (the iron frame alone, with the brickwork, the lining, the test, the engine, the
  fittings) nothing floats; the iron frame is iron only, with no thin post taller than half the furnace; its ties are
  hidden in the brickwork.
- **Textures by role**, as the table above puts them.
- **Containment:** nothing leaves the 18 cells over a whole turn of the shaft (every 7.5 degrees); nothing over the top.
- **A row:** the west face plain brickwork over its whole area, the east face wherever the furnace reaches it, nothing
  but the brickwork at either side plane at any angle; two furnaces side by side touch only at their walls.
- **Thick walls:** at least `MIN_WALL` (8) of brick, or iron set in it, round the chamber and the fireplace, west,
  east, behind, and in front clear of the openings.
- **The lining:** at least `LINING_T` (3) of the lining's brick behind every hot face (3441 points), the cave and the
  ash mouth the brickwork's.
- **The stages' counts:** the brick boxes' volumes add up to the lattice's; the counts at the game's rates; the test's
  bone ash measured from its elements (on a quarter-voxel grid) within 3 % of its count.
- **The test:** on the hearth, clear of the walls, under the working door, whose sill is level with its rim; the ring on
  seven sides; the gate as wide as the gutter; the lead and the cake in the dish; the tuyere on the test's middle line,
  its mouth just behind the ring, between the rim and the springing.
- **The litharge's way:** the gutter falls from the gate's floor at the breast's face, bedded in its groove; the stream
  falls from its end inside the pot's walls onto the litharge, its fall clear of the furnace.
- **The blast's way:** the chests stand on the main, the main runs into the elbow, the blast pipe to the back wall's
  face where the tuyere's back end is, the tuyere into the chamber.
- **The engine:** the shaft at the power cell's centre with its oak end at the power face, carried by its two pedestal
  bearings (enclosing it at rest and at 45 degrees); the crank pin on the slot's middle line at every angle (an exact
  Scotch yoke) and within its length; the slippers on the rail and under the top guide over the stroke, clear of the
  standards; the rod through the gland, its end inside the barrel, at both ends of the stroke.
- **Clearances:** in 15 scenes (every state of the test and the fire, every place of the damper with the cap on, each
  stage before the furnace is complete) no two parts overlap at rest, and the shaft and the yoke, turned through 48
  angles, touch nothing but their intended contacts (the shaft in its bearings, the rod in the gland and the cylinder,
  the slippers on their guides, the gutter bedded in its groove).
- **The damper** in its recess between its guides in all three places; shut over the mouth, half over its upper half,
  open clear of it.
- **The outlet:** open to the sky through the stack; the cap flush in the curb's seat over the stack's mouth; the curb
  the lining's brick.
- **The draught:** from outside the ash mouth through the cave, up every gap of the grate, over the bridge and the test,
  out at the flue opening, along the back flue and up the stack: 152 points clear of the furnace built; the stack's mouth
  clear at every angle but for the cap; every firebar on both bearers, the bearers built into the walls.
- **No z-fighting** at 20 poses (eleven scenes, the stages among them, two angles), after the fix (122 faces pressed
  against their own part removed).
- **Files:** every texture the game's (with its files at hand) and declared; glow on the burning coal, the lead, the
  cake and the running litharge only; the frame's shape the iron frame alone; 18 cells with boxes, decks on the top
  cells; the power cell a cell of the furnace facing out of it; the outlet under the chimney's cell, the chimney six
  blocks from it up; the ash mouth, the fire door and the working door on the front face; the shipped files are the
  checked model.

`tools/tests/test_cupellationfurnace_model.py` holds the written files to these without the generator (the
vocabulary, the reference poses, the yoke following the pin, the cells rebuilt from the shape, the stages' counts, the
frame, the brick types, the anchors, the side faces, the textures and glow, and `site/models.json`'s states);
`site/test/cupellationfurnace.test.ts` replays the reference poses in the viewer's maths.

### Rig schema (`cupellationfurnace-rig.json`)

The bucking sawmill's format with nothing added to the shared rig maths:

- **Inputs.** θ, the axle's angle. No depth, work, class, presence, feed or oil.
- **Drivers.** The shaft: a `rotate` about z through the shaft's axis, ratio 1. The yoke: a `slide` along x, amplitude
  the crank's throw (3/16), ratio 1, phase 0. Every other part has none.
- **`requires`** values: the stages, `brickwork`, `lining`, `test`, `engine` (the `engine`, `shaft` and `yoke`
  parts) and `fittings`; `coal`, `fire`, `lead`, `silver`, `stream`, `litharge`; `damperopen`, `damperhalf`,
  `dampershut` (one at a time); `outletcap`. The iron frame has none.
- **Keys:** `cells`, `powerCell`/`powerFace`, `outletCell`/`outletFace`, `ash`/`ashSide`, `fireDoor`/`fireDoorSide`,
  `door`/`doorSide`, `dish`, `litharge`/`lithargeSide`, `tuyere`, `chimney`, `build`, `bricks`, `furnace`, `parts`.
- **`build`**: as the crucible furnace's: `frame` (`recipe`, `tool`, `ingredients`), `stages` in order (`requires`,
  `materials`, `volumeBlocks` where measured, `perClick`, the brickwork's `oneType`, the engine's `alsoParts`, the
  fittings' `also`: the damper's places), `outletCap`.

**In the viewer** (`site/models.json`, `cupellation-furnace`): the θ slider is the axle; Play turns it at 2.1 s a turn
(the reference speed 0.6). Four selects of states: **Build** (the iron frame, 1: brickwork, 2: lining, 3: the test, 4:
the blowing engine, 5: fittings, complete), **The test and the fire** (cold, coal on the grate, lit, lead melted with
the blast on, cupelling with the litharge running off, brightened, the cake and the litharge taken out; it waits for the
fittings), **Ash-pit damper** (waits for the fittings) and **Outlet** (waits for the lining). A **Brick** select offers
the nine fired brick types, as the crucible furnace's.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork
(`bk_brick_NNN`), `ln_*` the lining (`ln_brick_NNN`), `ts_*` the test (`ts_ring<k>`, `ts_ash_*`, `ts_rim<k>`,
`ts_breast_foot`, `_w`, `_e`), `en_*` the engine's fixed parts (`en_bed*`, `en_ped<n|s>*`, `en_rail`, `en_standard*`,
`en_topguide`, `en_cyl_*`, `en_flange*`, `en_cover*`, `en_gland_*`, `en_cradle<n>`, `en_chest<n>*`, `en_elbow`,
`en_main_*`, `en_blastpipe_*`), `sh_*` the shaft (`sh_oaka`, `sh_oakb`, `sh_shaftn_*`, `sh_shafts_*`, `sh_webn`,
`sh_webs`, `sh_pin_*`), `yk_*` the yoke (`yk_top`, `yk_bottom`, `yk_sidew`, `yk_sidee`, `yk_slippertop`,
`yk_slipperbottom`, `yk_rod`), `ft_*` the fittings (`ft_firedoor*`, `ft_gutter_*`, `ft_pot_*`), `coal_*`, `fire_*`,
`lead_*`, `silver_*`, `stream_*`, `litharge_*`, `damperopen_*`, `damperhalf_*`, `dampershut_*`, `outletcap_*`; and
`fr_*` the iron frame (`fr_strap_*`, `fr_kerb_*`, `fr_bearer<n>`, `fr_bar<n>`, `fr_lintel`, `fr_guide_*`,
`fr_firedoor_*`, `fr_jamb_*`, `fr_baylintel_*`, `fr_nichelintel`, `fr_tuyere_*`, `fr_tie_*`). Hand edits are lost
when the script runs again.

## Salvaged, and new

From `741-parting-furnace-model-v2` (mechanisms only, rebuilt at full size): the blowing engine (`build_shaft`,
`build_yoke`, `guide_x`, `build_engine`: the shaft in two pedestal bearings, the double-web crank, the Scotch yoke with
its slippers and guides, the cylinder with flanges, cover, gland and valve chest, the blast pipe) and the test
(`build_cupel`'s ring of chord segments round an oval, the bone ash, the breast and the litharge channel and pot), and
the yoke and support checks with the intended contacts. Turned on its side for the back row's top (the yoke slides
along x, not y), double-acting (a chest at each end), the crank pin under the shaft so the slide's phase is exactly 0.
The branch's furnace (2 to 2.5 times too small), its shared stack, floor and per-cell `fitted` collision are not used.
The body (the brick lattice, the lining's zones, the binding, the ties, the damper, the outlet and its cap, the stages,
the rig's `build`, `bricks` and the cells) is new on `../CrucibleFurnace/tools/make_shape.py`'s skeleton, its helpers
copied, not shared.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has looked at it in a client.

1. **The test stands on the hearth**, 3.7 high, rather than set into it with its rim flush, as the English test was
   (held up from below on iron bars in an opening of the hearth). Flush, the rectangular well round an oval test leaves
   gaps the lattice cannot fill; standing on the floor it reads the same from the working door.
2. **The vault is stepped** (three courses), as the brick lattice is made of square boxes.
3. **Whole-cell boxes** but for the engine's cells; the decks make the top one floor over the engine. The shaft and the
   yoke have no boxes of their own (the engine's fixed parts' boxes enclose them).
4. **The glow is fixed**: 200 on the fire and the lead, 255 on the cake, 160 on the running litharge.
5. **The piston and the valves' discharge side are not drawn**: they are inside the cylinder and the chests.
6. **The lead pool is drawn full** whatever is on the test; a stretch on the work could lower it (gameplay's call).
7. **The engine's bay is open to the east**: the row's next furnace closes it; the last in a row shows the engine from
   the side.

## Open for the owner

1. **The blast** (#740's open question): the furnace's own blowing cylinder on the axle, as specified and modelled,
   or Steelmaking Expanded's air blower on a steam engine piped to the tuyere (the period's blast came from steam jets,
   Root or Baker blowers or a fan). Swapping is the engine stage and the power anchor; the tuyere stays.
2. **The cylinder horizontal**: the spec's "blowing cylinder on the back row's top" with the axle at the top cell's
   centre leaves 16 voxels of height; the v2 branch's vertical cylinder under its crank needs about 29. Horizontal,
   double-acting, in line with the yoke. A vertical alternative would need the back row's base lower or the cylinder
   over the crank.
3. **The engine's place**: on the back row's middle and east top cells, the stack in its west column; the axle at the
   back of the east cell (a row's line shaft runs behind it, an angled gear to each furnace). The side face over the
   engine is open (above).
4. **The build stages** (#741's open item): the frame's recipe and the test's, the engine's and the fittings' materials
   are proposals; the brickwork's, the lining's and the bone ash are measured.
5. **The test as a stage or a wearing part**: in the period it was renewed every few days (it soaks up litharge). As
   modelled it is the third stage; it could instead be fitted and replaced like the cylinder borer's cutter head.
6. **The items** (#740's open item): what goes in (silver-lead, or the roasted concentrate the hand cupel takes) and
   comes out (a silver cake, litharge, the item the pot gives) are gameplay's; the model draws a lead pool, a cake and
   litharge.
7. **The test on the hearth, not flush** (weak spot 1).
8. **Shared anchors pending #711**: the power cell and face and the outlet follow the crucible furnace and the other
   machines; #711 may settle the batch machines' hand anchors (`door`, `fireDoor`, `litharge`, `dish`) differently.
