# Grinder

Part of the Seraph Horizons mod (`../README.md`): the ore line's **grind stage** (behaviour #732, model #733,
epic #684), one machine upgraded in place on the shared upgradeable-machine code (#711). A frame, then each
tier's parts fitted one item each in order; fitting the next tier's set replaces the previous tier's working
parts. Grinding never loses metal: it is what lifts poor ore's × 0.4.

| Tier | Fitted | Period machine | In the model |
|---|---|---|---|
| 2 | arrastra | a stone-paved pit, drag stones on sweeps, mule-driven | a paved basin on piers; a post turned through bevel gears under it sweeps four drag stones round on chain bridles |
| 3 | Chilean mill | edge runners rolling in a pan | two granite edge runners on a horizontal axle through a cross-head, rolling on a steel die in an iron pan; ploughs; a discharge screen |
| 4 | ball mill | a riveted drum on trunnions with a girth gear, middlings reground | a riveted drum on hollow trunnions in pillow blocks, a girth gear and pinion, fed through one trunnion and discharging over the other, a return funnel for middlings |

**Status: the model only.** This folder holds the generator (`tools/`). There is no block, block entity or
renderer yet; the model and its rig are what #732 will be built against. Paths here are from this folder
unless they start with `assets/` or `tests/`, which are the mod's (`mods-src/seraphhorizons/`), or `Machines/`,
this folder's neighbour. The generic half of the generator is `Machines/tools/machinegen/`, unchanged by this
machine: no new driver, every motion a `rotate` on the axle's angle or a ride.

## The machine

**Footprint, fixed at the tier 4 size from the frame on:** 3 × 2 × 3 cells (x, y, z), three by three and two
high, 18 cells. In the native frame (x west to east, y up, z north to south) the controller `[0,0,0]` is the
middle of the north side at ground level: the operator's side, the block clicked. The build frame (below) is
measured from the box's north-west bottom corner, so the shipped files are it moved one block west.

**The ore runs west to east; the drive comes in from the south.** Every anchor is at the same cell in every
tier (shipped coordinates):

| Anchor | Cell, face | What is there |
|---|---|---|
| `powerCell`, `powerFace` | `[0,0,2]`, south | The vanilla axle comes in along z at the face's centre (x 24, y 8 voxels in the build frame) onto the power shaft. |
| `infeedCell`, `infeedFace` | `[-1,1,1]`, up | The feed hopper's mouth over the west end: a chute from the previous machine (crusher or classifier), or a player at tier 2, tips ore in from above. |
| `outputCell`, `outputFace`, `output.pos` | `[1,0,1]`, east, `[2.0, 0.3125, 1.5]` | The discharge box's lip through the east face, low down: the ground ore leaves towards the next machine (the concentrator). |
| `returnCell`, `returnFace` | `[-1,1,2]`, up | Tier 4's middlings return funnel, beside the hopper at the feed end; the concentrator's middlings come back by chute from above. |

**The frame** (`requires` null) is what never moves between tiers: a timber deck over the whole footprint;
the power shaft (the vanilla axle's cross from the south face, then an octagonal iron shaft in two pillow
blocks, collars either side of the inner one) running north under the middle to just short of the centre;
the feed hopper, a timber bin between two posts at the west edge with a floor sloping down to an outlet in its
east wall; and the discharge box at the east end, open at the top, its floor sloping to the lip. The power
shaft turns with the axle in every state, the bare frame's included. Each tier brings its own structure (the
basin's piers, the pan's legs, the mill's bedplate and pedestals) and its own gearing, keyed on the power
shaft's end: the shaft's axis meets the machine's centre line at one point (x 24, y 8, z 24), the apex of every
bevel pair.

### Why this layout (the precedent for the ore machines)

This is the first of the seven upgradeable ore machines (#729 crusher, #731 classifier, #735 concentrator, ...),
so the layout is written down as a rule each can follow:

1. **Footprint the tier 4 machine's, odd across the drive.** The ball mill, its trunnions, the feed box and
   the discharge box take three blocks along the line; the round basin and pan fit inside the same three by
   three. Three cells across the power's line puts the drive on a cell's centre, where a vanilla axle meets it.
2. **The ore runs along x, west to east, by gravity: in from above at the west end, out low at the east.**
   Period mills stood in terraces down a hillside, each machine's discharge above the next one's feed; the
   hand-off chutes of #711 work the same way. The infeed is a top face (a chute or hopper drops into it, or a
   player tips ore in), the output an end face low down (a chute, a hopper or a container beyond it).
3. **The drive comes from the side, never the ore's ends.** A line shaft along the south side of a line of
   machines can drive each one without crossing a chute. The power cell is the south side's middle at ground
   level, the power shaft the frame's, and each tier's gearing goes on its end.
4. **The frame holds what never moves:** the deck, the power shaft and its bearings, the infeed (hopper) and
   the output (discharge box). Pipes, chutes and the tailings heap would join it (the grinder has none of
   them: no water is drawn and nothing is lost).
5. **Closed circuits come in beside the feed, by a second top face** (`returnCell`), into tier 4's own feed
   box, which takes the hopper's outlet too.
6. **The operator stands on the north side**, where the controller is; the drive is on the far side.

### Powered substitutes for the period drives

No animal, water wheel or engine: power comes from the axle, and every tier works whichever way it turns.

- **The arrastra was mule- or water-driven, its sweep at shoulder height.** Here the post is turned from
  below: a bevel pinion on the power shaft drives a bevel wheel on the post's iron foot (3:1), and the post is
  stepped on the deck and runs in the basin's centre stone. A pit dug into the ground cannot have its drive
  under it, so the **basin is raised on eight stone piers** over the gearing (the visible cause, open to view
  from the side). Each drag stone hangs under its arm on a **bridle of two chains**, one to each end, rather
  than a single chain pulling it from in front, so it is dragged the same either way the post turns.
- **The Chilean mill** was usually driven by bevel gearing from above or below; it is underdriven here, the
  same way as the arrastra (a 2:1 bevel pair, so it runs faster), its pan on eight iron legs.
- **The ball mill** keeps its girth gear and pinion (3:1). The axle comes in square to the drum, so a
  **mitre pair (1:1) on the power shaft's end turns a countershaft** under the drum, which carries the pinion
  under the girth gear on the drum's east head: period mills were often driven through a countershaft.

Gearing ratios are the model's (the turns the work makes per axle turn); gameplay's throughput is its own.

### Every turning part, by tier

θ is the axle's angle. A part that rides another turns with it; nothing else moves. Turns are per axle turn;
the whole machine repeats every six (the viewer's θ slider spans six turns).

| Part (rig id) [requires] | Driven by | Drives | Drivers |
|---|---|---|---|
| **Frame** | | | |
| Power shaft: the axle's cross, the iron shaft, its collars (`shaft`) [null] | The vanilla axle | The fitted tier's pinion | `rotate` z, 1 |
| Deck, pillow blocks, hopper, discharge box (`frame`) [null] | Fixed | — | none |
| **Tier 2: arrastra** | | | |
| Bevel pinion, 10 teeth, on the power shaft's end (`t2pinion`) [t2post] | The power shaft (keyed) | The post's bevel wheel | rides `shaft` |
| Post: iron gudgeon, bevel wheel (30 teeth) and hub, oak post, iron bands and cap (`t2post`) [t2post] | The pinion | The arms | `rotate` y, −1/3 |
| Sweep arms and their staples (`t2arms`) [t2arms] | The post (through it) | The chains | rides `t2post` |
| Drag stones, their eyes and chain bridles (`t2stones`) [t2stones] | The arms, by the chains | Grind on the paving | rides `t2post` |
| Footstep (`t2foot`) [t2post]; basin: piers, platform, paving, centre stone, curb, feed spout, drain (`t2basin`) [t2basin] | Fixed | — | none |
| **Tier 3: Chilean mill** | | | |
| Bevel pinion, 10 teeth (`t3pinion`) [t3shaft] | The power shaft (keyed) | The shaft's bevel wheel | rides `shaft` |
| Shaft: steel shaft, bevel wheel (20 teeth) and hub, cross-head, the runners' axle, collars, nuts, cap (`t3shaft`) [t3shaft] | The pinion | Carries the runners and the ploughs round | `rotate` y, −1/2 |
| East runner (`t3runner1`), west runner (`t3runner2`) [t3runners]: granite wheel, iron tyre, hub | Carried round by the axle, rolled by the die | Grind on the die | `rotate` x about its axle, +2/3 and −2/3 (rolling at its track, 9.6 from the centre, radius 7.2), then rides `t3shaft` |
| Ploughs: arms, hangers, steel blades (`t3scrapers`) [t3scrapers] | The cross-head | Turn the pulp back under the runners | rides `t3shaft` |
| Footstep (`t3foot`) [t3shaft]; pan: legs, bottom, die ring, boss, wall, screen, launder, feed spout (`t3pan`) [t3pan] | Fixed | — | none |
| **Tier 4: ball mill** | | | |
| Mitre pinion, 12 teeth, and hub (`t4mitre`) [t4drive] | The power shaft (keyed) | The mitre wheel | rides `shaft` |
| Countershaft: mitre wheel (12), shaft, girth pinion (10), collars (`t4counter`) [t4drive] | The mitre pinion | The girth gear | `rotate` x, −1 |
| Drum: trunnions, heads, riveted shell, manhole, girth gear (30), discharge lip (`t4drum`) [t4drum] | The girth pinion | Lifts the charge | `rotate` x, +1/3 |
| Countershaft's pillow blocks (`t4cbear`) [t4drive]; bedplate, portals, trunnion bearings (`t4bed`) [t4bed]; feed box, spout, return funnel and launder (`t4feed`) [t4feed] | Fixed | — | none |
| The charge (`t4balls`) [t4balls] | Lies in the drum's bottom, heaped up the rising side | — | none |

At one axle turn a second the arrastra's post turns 20 times a minute, the Chilean mill's shaft 30 (each
runner spinning 40 about its axle), the drum 20. Toothed wheels and what they mesh (nothing toothed meshes
nothing): each tier's pinion and its wheel; the mitre pinion and wheel; the girth pinion and gear.

### The tiers' fitted parts (provisional)

Each tier's set goes on in this order, one item each; the next tier's set replaces it (the rig's `tiers`).
The items are #732's to choose; the `requires` ids and the order are the model's proposal.

| Tier | # | `requires` | What it fits |
|---|---|---|---|
| 2 | 1 | `t2basin` | The basin: eight stone piers, a timber platform, two courses of paving, the centre stone, the curb with its drain, the feed spout from the hopper's outlet |
| 2 | 2 | `t2post` | The post with its footstep, its bevel wheel and the pinion on the power shaft |
| 2 | 3 | `t2arms` | The four sweep arms through the post |
| 2 | 4 | `t2stones` | The four drag stones and their chain bridles |
| 3 | 1 | `t3pan` | The pan: eight iron legs, the bottom, the steel die ring, the boss, the wall with its screen and outer launder, the feed spout |
| 3 | 2 | `t3shaft` | The shaft with its footstep, bevel wheel, pinion, cross-head and the runners' axle |
| 3 | 3 | `t3runners` | The two edge runners |
| 3 | 4 | `t3scrapers` | The two ploughs |
| 4 | 1 | `t4bed` | The bedplate, the two cast portals and the trunnion bearings |
| 4 | 2 | `t4drum` | The drum with its trunnions, heads, manhole and girth gear |
| 4 | 3 | `t4drive` | The mitre pair, the countershaft with its pinion and its two pillow blocks |
| 4 | 4 | `t4balls` | The charge of iron balls, in through the manhole |
| 4 | 5 | `t4feed` | The feed box at the west trunnion with its spout, the middlings return funnel and its launder |

**In the viewer** (`site/models.json`): one select, **Fitted**, steps from "The frame" through "Tier 2:
arrastra" and "Tier 3: Chilean mill" to "Tier 4: ball mill" (the default), each fitting that tier's whole set
and none of another's; the checkboxes under it show a tier's stages one at a time. θ has a slider and Play.

## Model

Everything in the model was made for this mod: no other mod's model is used. Textures are the game's,
referenced: debarked oak and oak planks, the generic planks of the vanilla axle's cross, iron plate, plain
steel sheet, riveted iron (the drum's shell), iron chain, iron mesh (the screen), and granite as cobblestone
(paving), dry stone (piers, curb), rock (drag stones, centre stone) and polished rock (runners).
`tools/make_shape.py` writes:

| File | What it holds |
|---|---|
| `assets/seraphhorizons/shapes/block/grinder.json` | The frame and every tier's parts (1021 elements), each tier's under its `requires`. |
| `assets/seraphhorizons/shapes/block/grinder_frame.json` | The static frame only (46 elements): what a block would draw. |
| `assets/seraphhorizons/config/grinder-rig.json` | Footprint, anchors, the tiers, the part rig (22 parts). |
| `tests/Grinder/rig-reference.json` | Every part's matrix at a grid of axle angles, from the reference maths. |

### Dimensions (voxels, build frame)

- **Deck** y 0..2. **Power shaft** along z at (x 24, y 8) from its north end (z 26.5) to the south face; the
  axle's cross from z 42.5; pillow blocks at z 33..35.5 and 39.5..42.
- **Hopper** x 0.5..7.5, z 17.5..30.5, y 21.4..32 (its mouth the infeed cell's top face); outlet in its east
  wall at z 22.5..25.5, y 22..24. **Discharge box** x 43..47.5, z 19..29, up to y 13; its lip out of the east
  face at y 4.4..5.
- **Tier 2:** basin centre (24, 24); piers at radius 12, y 2..12.5; platform y 12.5..14; paving to y 15.5
  (the floor); curb radius 15..16.5 up to y 21 (a basin about 2 m across); arms y 22.5..25 to radius 12.5;
  drag stones radius 5.5..14, 4.6 wide, y 15.5..19.6; the post to y 28.
- **Tier 3:** pan wall radius 15.5..16.5 up to y 21; die ring radius 6..12.8, its top at y 15.5; runners radius
  7.2 (about 0.9 m across), 4.5 wide, centred 9.6 from the axis on an axle at y 22.7.
- **Tier 4:** drum axis along x at (y 20.8, z 24); shell radius 7.9..8.5 (about 1.06 m across), x 20..33.5;
  heads to radius 9; trunnions radius 2.6..4 (the bore 2.6), x 11.5..18.5 and 35..45; girth gear pitch radius
  9.6 on the east head; countershaft along x at (y 8, z 24), x 25.6..41.6; pedestals at x 13..16.5 and 38.5..42.

### Regenerating

```sh
python3 mods-src/seraphhorizons/Grinder/tools/make_shape.py              # stdlib only; rewrites the four files
python3 mods-src/seraphhorizons/Grinder/tools/make_shape.py --out DIR    # or writes them into DIR
python3 mods-src/seraphhorizons/Grinder/tools/make_shape.py --quick      # skips the z-fighting fix and the swept paths
```

A full run takes about a minute. Output is deterministic (two runs into two folders `diff -r` clean).
`tools/validate_grinder.py` holds the checks; every run checks its output and exits non-zero if one fails.
The model holds four states never drawn together (the frame alone, and the frame with tier 2, 3 or 4), and
every check comparing parts does it a state at a time.

- **Parts:** the Euler round trip; every element in the part it was built for; no duplicate names, no empty
  part.
- **The build:** every part but the frame's in one tier's stage, needing its `requires`; no `requires` in two
  stages; at rest the frame, then each of a tier's stages in turn, joined to the ground through what is
  already there (nothing floats).
- **Textures** by role (the list in "Model" above).
- **Anchors:** the anchor cells on the footprint's outside, their faces looking out and all different; the
  power face on a side, not on the ore's ends; the axle's cross at the power face's centre; the hopper's mouth
  the infeed cell's top face and the funnel's the return cell's; the lip at the output face, `output.pos` its
  end.
- **The ore's way:** tiers 2 and 3's spouts from the hopper's outlet to inside the curb or the pan's wall;
  tier 4's feed box taking the outlet, the return launder ending over it and its spout inside the west
  trunnion's bore; the arrastra's drain, the Chilean mill's launder lip and the drum's discharge lip each over
  the discharge box's inside.
- **Supports:** the power shaft in its two pillow blocks; the post and the Chilean mill's shaft each on a
  footstep and in a neck bearing (the centre stone, the pan's boss); the countershaft in two pillow blocks; each
  trunnion through a bearing closed round it on four sides; each runner between its collar and nut; each
  pinion's hub on the power shaft's end.
- **Rolling:** each runner's contact point at its track stands still (finite differences of the posed rig),
  its tyre on the die at rest.
- **Gearing:** for each mesh the pitch points move together and the ratio is the tooth counts'; over a whole
  turn of the faster wheel, every 2.5 degrees, no tooth runs into the other wheel and the mesh is always
  engaged.
- **Containment:** nothing leaves the 3 × 2 × 3 box over the cycle.
- **Clearances:** every pair of parts that move against each other, over the six-turn cycle (every 30 degrees
  of the axle; every 5 in full runs), touches only at the intended contacts (`ALLOWED`: shafts in their
  bearings and footsteps, keyed hubs, meshing teeth, the runners on their axle and die, the charge on the
  shell).
- **No z-fighting** (full runs): no coplanar overlapping faces in any state at three axle angles, after the
  fix (`fix_coplanar`, a tier at a time with the frame).
- **Files:** every texture declared; the frame shape the frame part alone; 18 cells, no lids; the rig's
  `tiers` naming every `requires` once; the shipped model the checked one moved.

`tools/tests/test_grinder_model.py` holds the written files to these rules without running the generator, and
this README's tier table to the generator's; `site/test/grinder.test.ts` replays the reference poses and the
viewer's states.

### Rig schema (`grinder-rig.json`)

The bucking sawmill's format (`../BuckingSawmill/README.md`, "Rig schema"). The only input is θ; every driver
is a `rotate` on it, and the rest of the parts ride or stand still. Keys: `cells` (18, the boxes at rest of
every tier's parts together; no lids), `powerCell` and `powerFace`, `infeedCell` and `infeedFace`,
`outputCell`, `outputFace` and `output`, `returnCell` and `returnFace`, `tiers` (`[{tier, name, fitted}]`: each
tier's `requires` values in fitting order) and `parts`. `requires` values: `t2basin`, `t2post`, `t2arms`,
`t2stones`, `t3pan`, `t3shaft`, `t3runners`, `t3scrapers`, `t4bed`, `t4drum`, `t4drive`, `t4balls`, `t4feed`, or
null.

### Editing by hand

Element names are the rig's interface (first-match globs, a part's id and `_`): `sh_*` (the power shaft),
`fr_*` (the frame), and `t2pinion_*`, `t2post_*`, `t2arms_*`, `t2stones_*`, `t2foot_*`, `t2basin_*`,
`t3pinion_*`, `t3shaft_*`, `t3runner1_*`, `t3runner2_*`, `t3scrapers_*`, `t3foot_*`, `t3pan_*`, `t4mitre_*`,
`t4counter_*`, `t4drum_*`, `t4cbear_*`, `t4bed_*`, `t4feed_*`, `t4balls_*`. Hand edits are lost when the script
runs again: port them into `make_shape.py`, or stop regenerating.

## Open for the owner

1. **The tiers' items** and the finer build stages within each tier (the `requires` ids and their order above
   are a proposal; one item each, #711's pattern).
2. **The precedent** ("Why this layout"): flow along x by gravity, in from the top at the west and out low at
   the east; the drive from the south side's middle at ground level; the frame holding the deck, the drive,
   the infeed and the output; closed circuits by a second top face beside the feed.
3. **Power at the west instead?** With the axle coming in along the drum's axis the pinion could sit straight
   on the power shaft (no mitre pair or countershaft), at the cost of putting the drive on the ore's way in.
4. **The raised arrastra.** Underdriven, its basin stands on piers about 0.8 m up; a pit at ground level would
   need its drive overhead (a timber bridge over the basin carrying a bevel pair, the power cell a block up).
5. **Water.** No water is drawn (the issues give it to the concentrators). All three period machines ground
   wet; a ppex pipe could come in on the west face of `[-1,1,0]` to the hopper or the feed box if the grinder
   should draw some.
6. **Collision.** The cells' boxes are every tier's parts together; per-tier boxes, and whether the machine is
   walked on (lids), are #711's.
7. **The return.** The concentrator is downstream and lower, so middlings coming back by chute from above
   would need lifting (an elevator); gameplay may move them without a chute.
8. **Sizes and speeds:** the basin about 2 m across, the runners 0.9 m, the drum 1.06 m by 0.84 m (a short,
   Krupp-like drum); the post at a third of the axle, the Chilean mill's shaft at a half, the drum at a third.
9. **The charge** is drawn lying still in the drum's bottom, seen only with the drum unticked; the ore and
   pulp are not drawn at all (no work input).

## Known weak spots, and what is not checked

1. **Box teeth.** Spur teeth are radial boxes; bevel teeth are boxes along the pitch cone at mid-face size
   (they do not taper).
2. **The runners' tyres are 18-gons:** turning, their corners dip up to 0.12 voxels into the die (an intended
   contact).
3. **Rigid chains and ploughs.** The bridles do not swing and the ploughs do not ride the floor.
4. **The hopper's floor slopes one way only**, east to the outlet.
5. **Not seen in a client:** there is no block or renderer, so lighting, the textures' scale, the riveted and
   mesh textures on these faces and the machine at play distance are unchecked. In this pass it was reviewed in
   projections rendered from the written files.
