# Stall roaster: the model

Part of the Seraph Horizons mod's ore processing (epic #684): the generated model of the **stall roaster** (#926), one
roasting stall, as one machine. Its behaviour is #925 (and #938 for native sulfur); the line, the glossary and the
period sources are in #939, the power and water budget in #940.

**Status: in the game** (#925). This folder's `tools/` holds the model's generator and its checks; `Core/` and `Game/`
hold the stall's gameplay, on the shared ore machine code (`Machines/`): the mod README's "Ore processing: the stall
roaster" has the rules, and "Wired into the game", below, what reads what.

Paths here are from this folder unless they start with `assets/` or `tests/`, which are the mod's
(`mods-src/seraphhorizons/`), or `Machines/` and `CrucibleFurnace/`, this folder's neighbours. The model follows the
crucible furnace's conventions (`../CrucibleFurnace/README.md`): any fired brick, tier-3 lining 3 voxels thick on every
hot face, iron binding (here angle iron wrapping every corner, by the owner's later rule), flush side walls for a row, an outlet with a curb and a cap,
a minimum chimney, a staged build measured from the model. The generic half of the generator is
`Machines/tools/machinegen/`, unchanged: no new driver and no change to the package.

## The stall

A **roasting stall** of the late nineteenth century (Peters 1895, ch. VI, "Stall-roasting"; Hofman 1904, figs.
281–284, the Přibram stalls): a three-sided bay of brickwork, open at the front, with a flue at the back to a chimney.
Sulfide rock is heaped in it on a bed of firewood, kindled, and left to roast for days on its own sulfur; the front is
walled up with loose brick, which sets the draught; the roasted rock is raked out through the front. Stalls stood in
rows sharing their walls, every flue to one stack. The model is one stall, **two blocks wide (x), two deep (z) and two
high**:

- **The bay** is the front row: 20 voxels wide between its side walls, the whole front row deep, open at the front
  and to the sky. Its floor (on a base of brickwork 2 thick) and the inner faces of its side walls and its back wall
  are **lined** with tier-3 refractory brick, 3 thick; the floor is at 5. Each side wall is 6 thick, 3 of brickwork
  outside 3 of lining, so two stalls side by side put 12 voxels of wall between their bays.
- **The back wall** is the back row, holding the flue: a **flue mouth** at the foot of the bay's back face, centred
  (8 wide, 5 high), leads through the lining into a **back flue** low in the wall, which runs west under the **stack**;
  the stack rises in the west back cell to the **outlet** on the top. All of it is lined, 3 thick; 3 of brickwork stand
  behind it on the back face. The draught goes in at the front, back through the charge, down into the mouth (the
  charge covers it), along the back flue and up the stack: the period stall's downdraught through its heap.
- **The outlet** has a **curb**, the stack's lining's brick, flush with the top, with a **seat** half a voxel down in
  it: a ledge 3 wide round the stack's mouth on the west, east and back, the stack's own front face on the front. The
  seat runs out to the side and back bands' top legs, so the cap's west and back edges meet the frame's iron with no
  strip of brick between them (a rim there would have been 2 wide). **The model stops at the outlet**: the player
  stands the chimney on it, or a flat iron **cap** (12 by 9) set flush in the seat, as on every stall in a row but the
  one carrying the chimney.
- **The iron binding is one frame of angle iron** (the owner's rules for brick structures): every binder is an L, 1
  voxel thick, that **wraps a corner** of the brickwork, one leg on each of the corner's two faces, its inside corner
  bedded against the brick's corner, so it holds the brick in on both faces. Each is set into the brick, its outside
  flush with the faces. Eleven angles:
  - up each of the **four outer vertical corners** and each **jamb** of the opening, legs 3 wide. A side wall's front
    face is only 6 wide, too narrow for brick between two angles, so the corner angle's and the jamb angle's front
    legs **meet in the middle of it**: the wall's end is cased in iron. The jamb's other leg is on the bay's side face,
    above the floor;
  - a **sill angle** wrapping the floor's front edge between the jambs, its front leg running **down to the ground**
    (5 high; the base under the floor is too low for brick under a leg) and meeting the jambs' front legs;
  - an **angle band** round **each of the four top edges**, its leg on the face 2.5 deep and its top leg 3 wide. The
    vertical angles' face legs stand up to the bands' face legs and their side legs up under the bands' top legs, so
    each upright meets the bands over it; the side bands run between the front and back bands' top legs. The front
    band is a tie across the top of the opening, where there is no brick.

  **No sliver of brick** shows anywhere between two irons, or between iron and an opening: every piece of brick
  showing is at least 3 voxels across both ways (the walls' own layers are 3 thick). The front face is all iron but
  the opening; the side faces are brick inside a border of iron (the corner angles' side legs, 2 wide, and the side
  band's leg), all flush; the back face is brick between the corner angles' legs and under the back band.
- **The charge**: a **bed of firewood** (four logs across the bay, five sticks along it), the rock on it in **four
  courses of eight** (32, two stacks, #925), each course across the bay from wall to wall, stepped back at the front, the
  top one mounded in from the walls, lumps of rock lying on each course's top. Cold, burning (the ember texture,
  glowing) or roasted (red-brown: the sulfide burnt to oxide). The bed burns as the **kindling**, glowing, under the
  cold charge.
- **The sulfur** collects in **pools in the roasted heap's crust**, on its top course: the sulfur driven off the
  sulfide condenses in the cooler top of the heap and is taken out by hand when the stall is cold.
- **The front** has **three places** (#925): **open** (charging, kindling, raking out); **half walled**, three courses
  of loose fired brick; **walled up**, six courses, to half a voxel under the front band. The loose brick is dry-laid in running
  bond in the bricks' own size (8 long, 4 high, 4 deep: the brick texture's), whole bricks and halves, each set back a
  little more or less so the wall reads as loose, and the bottom course leaves **two draught holes** at the floor. The
  walling is the stall's own brick type, each brick's faces one brick of the texture, its mortar cropped.
- **Stalls stand in a row**, up to six side by side along their width (x), sharing walls: the side faces are flush,
  brickwork in a border of the corner angles' and the side band's legs, flush, and nothing beyond the footprint, so
  two stalls' side walls meet (iron to iron round the border) and touch nowhere else. A row of six is **12 blocks** long. The flues between them are implied.

**Footprint and cells.** The stall's eight cells, every one with boxes from the built stall (the iron, the brickwork,
the lining), a **deck** (`lid`) at the top of each top cell, so a player walks over the open bay and does not fall
into it. Nothing moves, so no cell is hollow. The cell over the outlet, `[0,2,1]`, is the chimney's; it, its
neighbours along the row and the cells over the bay are not the stall's, and nothing of it reaches them. The
**controller is the front cell at ground level under the outlet's column**, `[0,0,0]` (a stall two wide has no middle
cell): the stall extends one block east (+x) and one back (+z) from it.

**Why 2 × 2 × 2** (#926):

- **Two wide**: a charge between walls. The bay's 20 voxels are five loose bricks' length in a course, between side
  walls of 6 (3 brick, 3 lining).
- **Two deep**: the bay (the front row) and the back wall with its flue and outlet (the back row). The chimney stands on
  the back wall's top, over the back row, never over the bay.
- **Two high**: the heap (a bed to 9, four courses to 26.5, lumps to about 27.7) and the front's walling (six courses,
  5 to 29) under the walls' top at 32 and the front band's face leg at 29.5.

| Anchor (rig key) | Shipped | What |
|---|---|---|
| `outletCell`, `outletFace` | `[0,1,1]`, `up` | The stack's top cell, in the back row of the controller's column; the chimney (or the cap) on its top, the chimney from `[0,2,1]` up |
| `front`, `frontSide` | `[1,1.1562,0]`, `north` | The middle of the opening in the front face: where the bay is charged, walled up and raked out |
| `heap` | `[1,1.6562,0.75]` | The middle of the full charge's top: its smoke, glow and sound |
| `chimney` | origin `[0.5625,5,1.4375]`, `y`, 6 | The shortest chimney, six blocks counted from the outlet up (y 2 to 8), drawn as a line over the stack |
| `build` | | The build's stages, the front's walling and the cap ("The build", below); not drawn |
| `bricks` | | The fired brick types and their textures (the crucible furnace's; its README's "The brick"); not drawn |
| `furnace` | | `charge` 32, `courses` 4, `perCourse` 8, `minChimney` 6, `rowMax` 6, `rowAxis` `x`; not drawn |

No `powerCell` (no power, #925: resistance 0), no air port, **no chute anchor** (batch, loaded and emptied by hand,
rule 2): the validator fails if any appears.

## The build

**Proposed, to settle with the owner** (#926: "Build stages to settle with the owner"). On the crucible furnace's
pattern, two stages after the frame, each a `requires` value of its part, and the viewer's **Build** select steps
through them:

| | Stage | `requires` | What it puts in | Materials |
|---|---|---|---|---|
| Frame | **Iron frame**: a grid recipe with a hammer; placing it puts down the controller and its ghosts | none (the frame part) | The angles: four up the outer corners, two up the jambs, the sill angle, the four angle bands; one frame, every member meeting the next, standing on its own | 2 iron plates, 2 iron rods, 6 iron nails and strips (a proposal, not measured) |
| 1 | **Brickwork** | `brickwork` | The base, the side walls' outer halves, the back wall: 3.03 blocks of brick | **7 fired bricks**, any one type (`game:burnedbrick-*`) |
| 2 | **Lining** | `lining` | Tier-3 refractory brick 3 thick on the hot faces (the bay's floor, its side and back faces, the flue mouth, the back flue, the stack), the outlet's curb: 2.16 blocks | **9 tier-3 refractory bricks**, **4 fire clay** |
| | *The front's walling* | `fronthalf`, `frontwalled` | Not a stage: the loose brick, one fired brick of the stall's own type a brick drawn, kept by the stall once given | 18 fired bricks (9 for half walled) |
| | *Outlet cap* | `outletcap` | Not a stage: put on or taken off by hand, any time after the lining | 1 iron plate |

**The counts are measured from the model**, as the crucible furnace's are: the generator builds the brick as a
lattice (every void's faces, the lining's zones' and the blocks' boundaries along each axis) and merges it into boxes,
the brickwork's and the lining's apart; the stages' volumes are those boxes' volumes added up, at the game's rates, 2
fired bricks a block and 4 refractory bricks a block, rounded up, fire clay one per 2.5 refractory bricks. The bay's
contents, the walling and the cap are drawn once the lining is in.

### No power, nothing moves

The stall is fired by its own sulfur on **natural draught**: no mechanical power (no `powerCell` or `powerFace`) and no
air port. Unlike the crucible furnace (whose lid swings), **nothing in the stall moves**: no part has a driver, the rig
reads no input, and the viewer shows no Play. Every part is a `requires`, gameplay's state. The reference poses
(`tests/StallRoaster/rig-reference.json`) are the identity at every pose; they are written so the stall's files have the
templates' shape and the tests replay them.

### The chimney

Counted **from the outlet up**, as the crucible furnace's: at least **six shaft blocks** (`furnace.minChimney`, the
crucible furnace's `MinChimneyHeight`; provisional for the stall) stood on the outlet, topped with one chimney top. The
viewer's `chimney` line draws the six from the outlet (y 2 to 8) over the stack.

## Model

Everything in the model was made for this mod: no other mod's model is used, so the generator needs nothing from
`build/mods`. **No new texture**: every texture is the game's. `tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/stallroaster.json` | The whole stall, every part (247 elements) |
| `assets/seraphhorizons/shapes/block/stallroaster_frame.json` | The iron frame only (44 elements): what the placed frame draws |
| `assets/seraphhorizons/config/stallroaster-rig.json` | Cells, anchors, the build and the brick types, and the part rig (21 parts) |
| `tests/StallRoaster/rig-reference.json` | Every part's matrix at three poses (all the identity) |

| Code | Texture | On |
|---|---|---|
| `brick` | The stall's brick type's block texture, set by the renderer (`bricks`); the shape's own is `game:block/clay/brick/four/running/red1` | The brickwork (world-mapped, so the boxes read as one wall) and the front's loose walling (one brick of the texture a face) |
| `lining` | `game:block/clay/refractory/tier3/front1` | The lining and the curb: world-mapped |
| `iron` | `game:block/metal/plate/iron` | The iron frame, the cap |
| `wood` | `game:block/wood/firepit/log` (the firepit's logs) | The bed of firewood |
| `ore` | `game:item/resource/crushed/galena`; the renderer may set it to the charge's rock | The charge's courses, cold |
| `ember` | `game:block/coal/ember` | The bed burning (glow 200) and the charge burning (glow 120) |
| `roasted` | `game:item/resource/crushed/hematite` (the sulfide roasted to oxide); the renderer may set it | The charge's courses, roasted |
| `sulfur` | `game:item/resource/crushed/sulfur` | The sulfur in the crust |

### How the stall works

Nothing turns or slides; every row is a state gameplay fits.

| Part (rig id) | Driven by | Drives |
|---|---|---|
| The iron frame (`frame`, no requires) | fixed: placed as the machine | angle iron at every corner: holds the brick in on both faces of each; the jamb angles and the sill guard the opening's brick from the rakes and the walling; the front band ties the side walls together across the opening |
| The brickwork (`brickwork`) | stage 1 | the base, the side walls' outer halves, the back wall; brick on the side faces for a row, in a border of flush angle iron |
| The lining (`lining`) | stage 2 | the hot faces: the bay's floor and inner faces, the flue mouth, the back flue and the stack; the outlet's curb |
| The bed (`wood`) | laid by hand through the open front | carries the charge; kindled, it lights it |
| The bed burning (`kindling`) | lit | lights the charge's bottom course; burns away |
| The charge's courses (`ore1`..`ore4`) | charged by hand, eight rock a course: gameplay shows `ceil(rock / 8)` | covers the flue mouth: the draught is drawn through it |
| The charge burning (`fire1`..`fire4`) | kindled from the bed, or lit from above (native sulfur, `fire4` alone) | burns on its own sulfur, roasting itself |
| The charge roasted (`roasted1`..`roasted4`) | the burn finished | raked out through the open front, a course at a time |
| The sulfur (`sulfur`) | the burn: sulfur driven off condenses in the cooler crust | taken out by hand when cold |
| The front's walling (`fronthalf`, `frontwalled`) | walled up by hand: one place at a time, none when open | sets the draught: open full, half, walled up only through the bottom course's two holes and the loose joints |
| The cap (`outletcap`) | fitted on every stall in a row but the one carrying the chimney | closes the outlet |
| The draught (no part) | the chimney on the outlet, natural draught only | in at the front, back through the charge, into the flue mouth, along the back flue, up the stack and the chimney |

### Regenerating

```sh
python3 mods-src/seraphhorizons/StallRoaster/tools/make_shape.py             # the assets and the reference poses
python3 mods-src/seraphhorizons/StallRoaster/tools/make_shape.py --out DIR   # all four into DIR
python3 mods-src/seraphhorizons/StallRoaster/tools/make_shape.py --quick     # skips the z-fighting fix and check
```

A full run takes about 20 seconds and is deterministic (two runs into two folders are byte-identical, logs included).
It exits non-zero if a check fails (`tools/validate_stallroaster.py`). The brick types and every texture are checked
against the game's files when they are at hand (`$VINTAGE_STORY`, else `~/Games/vintagestory`); without them that check
says it was skipped.

- **Parts:** every element in its intended part; no part without elements; **no part moves** (no driver, no ride);
  Euler angles round-trip.
- **The stages:** at each stage (the iron frame alone, with the brickwork, with the lining) nothing floats: every
  element drawn is joined to the ground. The iron frame is iron only and has no thin post taller than half the stall.
- **Angle iron:** every element of the iron frame is a leg of one of the eleven angles; each is an L of two legs 1
  thick and as wide as `ANGLES` says, meeting at a corner of the brickwork, each leg's outside flush with its face (open
  beyond it) and its inside against brick over its whole length and width (but where the front band crosses the
  opening, or where another angle's leg lies behind it at a joint), its inside corner bedded against the brick's corner.
  **One frame:** the vertical angles stand on the ground and their tops meet a band; every band and the sill meet
  other angles at both their ends (sharing a face). A flat bar, an angle off its corner, or a member stopping short of
  the next fails it. The Python test also checks the frame's elements make one piece.
- **No slivers** (owner's rule): at the brickwork stage and the stall complete, every piece of brick that shows (the
  brickwork's or the lining's, open to the air) is at least `MIN_BRICK` (3, the walls' own layers) across both ways on
  its face, measured between whatever bounds it there: iron, the other brick, an opening or an edge (about 50,000 face
  cells of half a voxel). The seat's sides (half a voxel deep, under the cap's edges) are the seat's. Legs of 2.5
  (the last pass's), a vertical angle's side leg stopping under its band's face leg, or the sill's front leg stopping
  above the ground each fail it.
- **Textures by role:** brick, lining, iron, wood, ore, ember, roasted, sulfur where the table above puts them.
- **Containment:** nothing leaves the eight cells, nothing stands over the top.
- **A flat top:** every half voxel of the top, but the bay and the outlet's seat, is brick or flush iron at the top.
- **A row:** each side face is brickwork, or a corner angle's or a side band's leg flush in it, over its whole area (8192 points over
  the two); nothing else reaches either side plane; two stalls side by side touch nowhere but at their shared walls.
- **The walls:** the bay centred left to right, the outlet's seat in the outlet's cell; each side wall solid for 6 out from the
  bay at every half voxel from the floor to the top; the bay's back face solid for 3 back but at the flue mouth; the
  back face solid for 6 in from it everywhere under the outlet's seat.
- **The lining:** behind every hot face (the bay's floor, sides and back, every face of the flue mouth, the back flue
  and the stack; 2512 points, walked out every quarter) the brick is the lining's for at least 3, or to where the brick
  ends, and no hot face is the brickwork's.
- **The stages' counts:** the brick boxes' volumes add up to the lattice's, the brickwork's and the lining's apart, and
  give the rig's quantities at the game's rates; the front's walling is a brick a brick drawn.
- **The charge:** the bed on the floor between the walls, behind the walling's place, clear of the back wall; the
  courses stacked from the bed, wall to wall (the top one in from them), stepping back at the front behind the walling,
  against the back wall, under the walls' top; 32 rock, 8 a course; the burning and roasted twins and the burning bed
  the same boxes; the sulfur on the top course's crust.
- **The front:** each place's bricks in the opening between the jambs, on the floor and the sill or on bricks of the
  course below, three or six courses, under the front's angle band, two draught holes in the bottom course, no two bricks
  overlapping.
- **The outlet:** the cap flush in its seat over the stack's mouth; the seat's ledge the lining's brick; the cap's
  edges against something solid all round: the bands' iron on the west and back, brick on the others.
- **The draught:** sampled points from in front through the walling's draught holes, back through the bay, into the
  flue mouth, along the back flue and up the stack to the outlet lie in no brick, iron or walling; the three passages
  run into each other; the stack's mouth is clear of everything but the cap.
- **Clearances:** in twelve scenes (every state of the charge walled up and capped, two half walled, the two stages
  before the stall is built), no two parts overlap (a lump on a course inside the course over it excepted).
- **No z-fighting** at 14 poses after the fix (62 faces pressed against their own part removed).
- **Files:** every texture code declared; glow on the burning bed and the burning charge only; the frame's shape the
  iron frame alone; no power cell, air port or chute; the eight cells with boxes, decks on the four top ones; the outlet
  under the chimney's cell, in the back row of the controller's column; the chimney's line six blocks from the outlet
  up, over it; the front's anchor in the opening's middle on the front face; the heap's over the bay; the `furnace`
  block the model's; the shipped files are the checked model.

`tools/tests/test_stallroaster_model.py` holds the written files to these without the generator (the vocabulary,
nothing moving, the cells rebuilt from the shipped shape, the counts measured again from the brick, the brick types and
textures against the game's files, the charge and the walling in the bay, the side faces for a row, the anchors) and
`site/models.json`'s states to the generator's; `site/test/stallroaster.test.ts` replays the reference poses in the
viewer's maths and checks the page's scenario and selects.

### Rig schema (`stallroaster-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"), the crucible furnace's keys, nothing added
to the shared rig maths.

- **Inputs.** None: no driver reads any.
- **`requires`** values: the stages, `brickwork` and `lining`; `wood`, `kindling`; `ore1..4`, `fire1..4`,
  `roasted1..4` (one set at a time for a course: cold, burning or roasted); `sulfur`; `fronthalf`, `frontwalled` (one
  at a time, none when open); `outletcap`. The iron frame has none.
- **Keys:** `cells`, `outletCell`/`outletFace`, `front`/`frontSide`, `heap`, `chimney`, `build`, `bricks`, `furnace`
  and `parts`.
- **`build`**: `frame` (`recipe` grid, `tool` hammer, `ingredients`), `stages` in order (`requires`, `materials`,
  `volumeBlocks`, `perClick` `stack`, the brickwork's `oneType`), `front` (`places`, `sameType`, `materials` for walled
  up, `half` the half-walled count), `outletCap`.
- **`bricks`**: the crucible furnace's, the same nine types (`code` `brick`, `item` `game:burnedbrick-{type}`, `default`
  `red`).

**In the viewer** (`site/models.json`, `stall-roaster`): four selects of states: **Build** (the iron frame, 1:
brickwork, 2: lining, complete), **The charge** (swept and empty, the bed laid, charged, kindled, roasting, roasted with
the sulfur, half raked out, native sulfur lit from above), **The front** (open, half walled, walled up) and **Outlet**
(open or capped); the last three wait for the stall to be built. The page opens built, charged, half walled, the outlet
open. A **Brick** select (the crucible furnace's nine types and colours) and a **Charge rock** select (galena,
sphalerite, native sulfur rock) set the `brick` and `ore` codes' colours. The viewer's texture families gain entries
for the ember, roasted rock, sulfur and sulfide codes (`site/src/lib/model-view.ts`), appended, so no earlier match
changes.

### Editing by hand

Element names are the rig's interface (first-match globs, in the rig's order): `bk_*` the brickwork (`bk_brick_NNN`),
`ln_*` the lining (`ln_brick_NNN`), `wood_*` and `kindling_*` (`log1..4`, `stick1..5`), `ore<j>_*`, `fire<j>_*`,
`roasted<j>_*` (`course`, `lump1..6`), `sulfur_*` (`pool1`, `pool2`), `fronthalf_*` and `frontwalled_*`
(`brick<course><n>`), `outletcap_*`; and `fr_*` the iron frame : each angle's legs `<angle>_a<n>` and `<angle>_b<n>`,
the angles `fr_corner_{nw,ne,sw,se}`, `fr_jamb_{w,e}`, `fr_sill`, `fr_band_{n,s,w,e}`). Hand edits are lost when the script runs again: port
them into `make_shape.py`, or stop regenerating.

## Salvaged from v2

From `737-roaster-model-v2` (`Roaster/tools/make_shape.py`, the evaluation's advice): **`build_heaps`**, a bed of
firewood under the ore heaped in courses (here four logs across and five sticks along, four courses stepped back at
the front, lumps on each), and **the flue mouth cut at the foot of the back wall** (`FLUE_MOUTH`; here centred, through
the lining into a back flue). Everything else was rebuilt on the crucible furnace's generator: the brick lattice, the
lining zones, `world_skin`, the stages and their counts, `bricks`, the outlet's curb and cap, the `chimney` line, the
`furnace` block, the three-place front on the damper's pattern (one part per place), and most of the checks. Not taken:
v2's three stalls, rubble walls, collecting flue, header, flue chamber, stack, trestle and trough, and the per-cell
`fitted` collision (`9fa58a5`).

## Provisional

- **Services** (#711 has not landed): the stall has none but the outlet and its chimney, on the crucible furnace's
  pattern (`outletCell`/`outletFace`, `chimney`); batch equipment has no chute anchor by rule 2. The `front` and `heap`
  points are this model's own, for gameplay's interactions, particles and sounds.
- `minChimney` 6 is the crucible furnace's; `rowMax` 6 is #926's.
- The frame's recipe is a proposal, not measured.

## Known weak spots, and what is not checked

The model has been reviewed in projections rendered from the written files. No one has looked at it in a client or in
the WebGL viewer (the sandbox's headless browser had no WebGL).

1. **Texture seams.** The brick is 104 boxes (54 of brickwork, 50 of lining), world-mapped as the crucible furnace's.
2. **The loose bricks' UVs** each crop one brick of the running-bond texture (its mortar left out); how they read in
   the game, and with every brick type's overlay, is not seen.
3. **The charge is flat courses** with lumps, not a sloped heap; a part charge shows whole courses.
4. **The glow is fixed**: 200 on the burning bed, 120 on the burning charge (scaling it is a renderer's).
5. **Whole-cell decks**: a player walks over the open bay at the walls' top; clicking into the bay is gameplay's.

## Open for the owner

1. **An open top.** The stall is roofless, as the period's open stalls (and v2's) were; the heap shows over its walls.
   A covered (vaulted) stall is the other period form; it would put a roof over the front row and the chimney could
   then stand over the bay. #925's "three-sided brick bay" reads as open; say if it should be covered.
2. **The outlet's column.** Two wide has no middle cell, so the stack and the outlet are in the west back cell and the
   controller in front of it; the flue mouth stays centred in the bay and a back flue carries the draught west to the
   stack. Mirror it, or move the mouth under the stack, if preferred.
3. **Where the sulfur is taken.** Drawn as pools in the roasted heap's crust (the period's heap and stall roasting of
   pyritic ore, where sulfur condensed in the cool top). #925 says "sulfur from the flue as now"; the alternative is a
   clean-out door on the back flue, drawn on the back face.
4. **Native sulfur (#938).** The courses' `ore` code is meant to be set by the renderer to the charge's rock (the
   viewer's Charge rock select shows native sulfur rock), and the calcarone's firing from above is `fire4` over cold
   courses. The spent rock left after melting would need its own texture on the `roasted` code; the molten sulfur's
   run-off (the calcarone's tapped floor) is not modelled.
5. **The front's walling and its cost.** Loose bricks of the stall's own brick type, counted a brick a brick drawn
   (18 walled up, 9 half); at the brick block's rate (2 a block) the whole wall would be one brick. Not a stage: kept by
   the stall once given, as the cap is put on by hand. Settle the cost, and whether the third place needs draught
   holes or the loose joints alone.
6. **The build stages.** Proposed: the iron frame (2 iron plates, 2 rods, 6 nails and strips, not measured), then the
   brickwork (7 fired bricks) and the lining (9 tier-3 refractory bricks, 4 fire clay). There is no third stage: after
   the lining nothing is fitted but the walling and the cap.
7. **The iron on the front and the sides.** No brick may show narrower than 3 between irons, and a side wall's front
   face is 6 wide, so the corner and jamb angles' front legs (3 each) meet and the whole front face is iron but the
   opening (the sill's leg too runs to the ground). The other way out, legs narrow enough to leave 3 of brick between
   them, would make them 1.5 wide: flat bars, not angles. Each side face has a border of iron (the corner angles'
   side legs and the new side band), so in a row two stalls meet iron to iron round it. The sliver minimum is 3, not
   half a brick's length (4): the walls' brick and lining layers are themselves 3 thick, so 4 could not be met.
8. **The texture families** appended to the viewer (`ember`, roasted rock, sulfur, sulfide) also recolour the crucible
   furnace's burning coke in the viewer, from a hashed blue to ember orange.

## Wired into the game

#925 wired the model in on the shared ore machine code (the mod README's "Ore machines: the shared game half").
What gameplay reads, and where:

- **The iron frame** is the controller block, `seraphhorizons:stallroaster-{side}` (the item is `-north`), class
  `seraphhorizons.OreMachine`, drawing `stallroaster_frame.json`; its grid recipe is the rig's `build.frame`
  (`recipes/grid/stallroaster.json`). Placed, it stamps the shared `oremachine-cell` into the other seven cells, their
  boxes and decks the rig's.
- **The stages** are `config/builds/stallroaster.json`, the rig's `build.stages` (brickwork, lining) with the same
  counts; the brickwork is a `oneType` material listing the rig's nine `bricks` types, so the first brick sets the
  stall's type. A test holds the build to the rig.
- **The brick** is the rig's `bricks`: the stall's `PartTextures` composes the type's `base` and `overlays` into the
  block atlas, as the crucible furnace's renderer does, and sets the shape's `brick` code to it.
- **The bay's contents** are drawn by the stall's state (`BEStallRoaster.ShowsPart`, the frame block's
  `extraRequires`): `wood` with the bed laid and not lit, `kindling` the first tenth of the burn, `ore1..4`,
  `fire1..4` or `roasted1..4` by `ceil(rock / furnace.perCourse)`, `sulfur` while the crust holds any, `fronthalf` or
  `frontwalled` by the front's place (`build.front`: 9 bricks, 18), `outletcap` capped (`build.outletCap`).
- **The charge** is `machines.stallroaster` in ore-processing.json (32 rock, 40 hours, 92 %, a third of the sulfur),
  held to the rig's `furnace.charge` by a test. The burn is the shared fire (`OreMachineFire`).
- **The row** is the crucible furnace's (`FurnaceRow`, `ChimneyColumn`): along native x, the stall's width (2)
  apart, up to `furnace.rowMax`, the chimney from `outletCell` up, at least `furnace.minChimney`.
- **Tests**: `tests/StallRoaster/StallRoasterTests.cs` (the rules, the rig's gameplay half, the build against the
  rig, the figures); `tests/PackTests/StallRoasterScenarios.cs` places, builds, charges, burns and empties it.
