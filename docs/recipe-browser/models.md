# Model viewer

The site's model viewer (`#/models`) shows the machine models of the pack's own mods: the
game's shape file, posed by the mod's rig, in the browser. It was first a throwaway page for
reviewing the bucking sawmill while its model was being generated, and nearly every flaw in
that model was found by looking at it, so it is kept for every model made this way.

How such a model is made is in [Animated machine models](../modeling/animated-machine-models.md).

The viewer has nothing to do with the recipe exports. Its pages have no version in the
address (`#/models`, `#/models/<id>`), they load nothing under `data/`, and they work when no
recipe data is published. A version can therefore never be called `models`.

## What it shows

- **The model**: every element of the shape as a box, coloured by rig part or by texture code,
  with optional box edges. Drag to orbit, right-drag or two fingers to pan, scroll or pinch to
  zoom; buttons give angled, opposite, front (from the south), side (from the west) and top
  views.
- **Picking**: hovering outlines an element and names it; a click or tap pins its details below
  the stage (name with its parents', part, `requires`, texture codes, `from`, `to`, rotations
  and their origin), with buttons to copy the element's name and its part id. Esc or a click on
  empty space clears it. The legend lists every element of each part (or texture), and picking
  one there does the same, which is also how it works without WebGL.
- **Controls generated from the rig**: a slider or toggle for each input the rig's drivers read
  (below), a checkbox per distinct `requires` value, and an overlay checkbox per anchor.
- **The credit line** from the manifest, always shown above the stage.

### Inputs

The rig format is the bucking sawmill's (`mods-src/seraphhorizons/BuckingSawmill/README.md`, "Rig schema"),
plus a machine's work (below): a trunk travelling through the rosser, the gear cutter's teeth cut. Its
drivers read up to nine inputs, and the viewer shows a control only for those some driver reads:

| Input | Read by | Control |
|---|---|---|
| θ, the shaft angle | `rotate`, `slide`, `swing` | Slider, 0 to 359° |
| ψ, the shaft's travel | `rotate`, `slide`, `swing` with `rectified` or `"input": "travel"`; a `gauge`'s `lobes` | Added up from every change of θ, shown under the slider |
| depth, 0..1 | `feed`, `step`, `stretch` (unless its `input` is `"oil"`) | Slider |
| lifting, 0 or 1 | `step` with a `lifting` gate | Checkbox |
| W, the work, in the rig's unit (a trunk's travel in blocks; teeth cut) | `"input": "work"` (or `"trunk"`), an `occupy` `gauge`, `roll` | Slider in the work's unit and step, 0 to its end for the class shown, labelled with the work's name |
| k, the work's class: 0 none, 1 thin, 2 thick | `gauge`, `roll` | The prop's choice when the prop moves with the trunk (below), else a select |
| p, the work's presence, 0..1 | `gauge` | Slider; posed by hand it is 1 with a class chosen, and Play eases a trunk's |
| φ, the feed's travel | `"input": "feed"` | Follows W: grows by ΔW over the rig's `feed.blocksPerRadian`, never going back; shown under the slider |
| oil, how full the machine's oil tank is, 0..1 | `"input": "oil"` on `rotate`, `slide`, `swing` or `stretch` | Slider, starting full (the gear cutter's sight-feed cup) |

When θ, ψ or φ is read there is also a Play button, a "turn backwards" toggle and an **input speed**
slider, 0 to 5 RPS (shaft revolutions per second of real time; 0 stops Play, waits included). Play
turns the shaft at that speed; with a play script (below) it also runs the inputs through the model's
cycle, a phase's `turns` at that speed (its `seconds` are real time). The slider starts at 1 RPS, or
at the script's `secondsPerTurn` as turns a second.

φ is worked out as the game does: the rosser's feed is geared and never slips, so φ grows by the
trunk's advance over the rig's `feed.blocksPerRadian` and never goes back (`feedAdvance` in
`site/src/lib/model-scenario.ts`, the game's `RosserVisuals.FeedAdvance`). Its feed rolls move
exactly with the trunk, in Play and when W is dragged by hand; turning θ alone does not turn them.
A rig without `feed.blocksPerRadian` leaves φ at 0. The reference poses (`site/test/rosser.test.ts`)
give φ explicitly.

#### The work and its drivers

A rig whose drivers read W, k or p declares its **work**, the machine's progress, in one of two ways (a
rig with both is refused; `workOf` in `site/src/lib/rig.ts`, `progress_of` in `rigmath.py`,
`RigProgress.Of` in `Machines/Core/WorkProgress.cs`):

- `work`: `{ "name", "unit", "step", "end": { "thin", "thick" } }`, a named quantity in its own unit
  (the gear cutter's `{ "name": "teeth cut", "unit": "teeth", "step": 0.005, "end": { "thin": 12,
  "thick": 20 } }`). `unit` and `end` (both above 0) are required; `name` defaults to the unit, `step`
  (the slider's) to 1/16. W runs from 0 to `end[k]`. It is a point on its own scale: a window is
  occupied while W is in it (nose = tail = W), so windows are written in the work's unit. A trunkPath's
  keys (`nose0`, `lengths`, `tailStop`) are refused here, and a `roll` needs a trunkPath.
- `trunkPath`, the trunk-flavoured case: `{ "origin", "axis", "length", "nose0", "lengths": { "thin",
  "thick" }, "tailStop", "stations": { "<name>": x, ... } }`, in blocks, a trunk with a length travelling
  along a line. Its name is "trunk travel", its unit blocks and its step 1/16. The trunk travels nose
  first towards + on the axis; places along it (`nose0`, `tailStop`, windows, stations, a roll's `at`)
  are coordinates on that axis. At travel W the nose is at `nose0` + W and the tail L_k behind it (L =
  0, `lengths.thin`, `lengths.thick`), and the trip ends at W = `tailStop` + L_k − `nose0`. The mill's
  and the rosser's rigs are written this way, unchanged.

The drivers (`site/src/lib/rig.ts`; the Python reference is `Machines/tools/machinegen/rigmath.py`):

- `"input"` on `rotate`, `slide` and `swing`: `"theta"` (the default), `"travel"`, `"feed"`,
  `"work"` (W; `"trunk"` is the same, its trunk-flavoured spelling) or `"oil"`, read in place of θ.
  `"rectified": true` is the mill's way of writing `"travel"`; a driver with both keys is refused. On a
  `stretch`, `"input"` is `"depth"` (the default) or `"oil"`: it scales by (length + travel × that
  input) / length, so a liquid's level can follow the oil tank.
- `gauge`, a motion set by the work at a place: `motion` (`slide` or `rotate`, the latter with a
  `pivot`), `axis`, `amount: { "thin", "thick" }`, `mode` (`occupy`, the default, or `present`),
  `windows: [{ "from", "to", "ease", "gain": { "thin", "thick" } }]` (for `occupy`; a gain left out
  is 1) and optional `lobes: { "ratio", "phase", "amplitude": { "thin", "thick" } }` (rotate only).
  Its engagement e is 0 without a class, p when `present`, else p times the most engaged window's
  min(1, gain × occupancy), occupancy easing in over `ease` (in the work's unit) as the nose arrives
  and out as the tail leaves. It moves by amount[k] × e, plus e × amplitude[k] × cos(ratio × ψ + phase).
- `roll`, an idle roller the trunk turns: `axis`, `pivot`, `at`, `ratio` (radians per block); it
  turns by ratio × clamp(nose − `at`, 0, L_k), and not at all without a trunk.

### Overlays

Anchors are recognised by their shape, so a new rig gets overlays without code changes
(`site/src/lib/model-anchors.ts`):

| In the rig | Drawn as |
|---|---|
| `cells` | Block cells (outlined, the origin cell `[0,0,0]` in orange, a floor grid) and collision boxes. A cell with `"hollow": true` and no `boxes` has none (it is solid only where the trunk is, which the game adds); any other cell without boxes is a full cube. A cell's `lid` (a collision-only deck over the whole cell, 1/16 thick, on the top cell of a column: every column of the mill and the gear cutter, the rosser's station only) is drawn with the collision boxes in its own shade, violet, so the deck the game adds reads apart from the boxes under it |
| `"<name>Cell": [x, y, z]` | That cell outlined; with `"<name>Face": "<side>"`, the face shaded and an arrow into it |
| `"<name>Side": "<side>"` | An arrow into that side of the footprint, on a line anchor running that way if there is one |
| `"<name>": { "pos": [x, y, z] }` | A point; with `"<name>Side"`, an arrow out that way |
| `"<name>": { "origin", "axis", "length" }` | A line along the axis, centred on origin; with `"stations": { "<name>": x }`, a labelled mark at each place along the axis |
| `"<name>": { "<x>Y": number, ... }` | A level line across the footprint at each such height |

Keys starting with `_` are comments. Any other key is listed under the overlays as not drawn
(the rosser's `feed`, its gearing figures, is one: the viewer reads its `blocksPerRadian` for φ
but draws nothing for it).

## The manifest: `site/models.json`

```json
{
  "models": [
    {
      "id": "bucking-sawmill",
      "title": "Bucking sawmill",
      "description": "One or two sentences for the index.",
      "credit": "Who made the model and on what terms; shown on the model's page, always.",
      "creditUrl": "https://github.com/.../CREDITS.md",
      "shape": "mods-src/seraphhorizons/assets/seraphhorizons/shapes/block/buckingmill.json",
      "rig": "mods-src/seraphhorizons/assets/seraphhorizons/config/buckingmill-rig.json",
      "scenario": { }
    }
  ]
}
```

| Field | |
|---|---|
| `id` | The address, `#/models/<id>`: lower-case letters, digits and dashes. |
| `title`, `description`, `credit` | Required. Write the credit from the mod's `CREDITS.md`. |
| `creditUrl` | Optional, https: a link after the credit. |
| `shape` | Required: the shape file, as a path from the repository's root. Strict JSON (a generated shape is; a hand-edited one with comments or trailing commas is not read). |
| `rig` | Optional: the rig. Without one the model is shown still, coloured by texture, as one part. |
| `scenario` | Optional, needs a rig: what the viewer cannot know from the rig (below). |

### Scenario

Everything specific to one machine lives here, as data; the viewer has no machine-specific code
(`site/src/lib/model-scenario.ts`).

- `inputs`: labels for the controls, `{ "theta" | "depth" | "lifting" | "reverse" | "work" | "size" | "presence" | "feed" | "oil": { "label", "hint" } }`.
  The work's label may be written `trunk` instead (the mill's and the rosser's are), not both; the
  work's unit, step and ends are the rig's (`work`, or the trunkPath's), not the scenario's. `size`
  takes `names`: three names for none, thin and thick, for the select.
- `requires`: a label for each `requires` value, e.g. `{ "sash1": "Sash 1" }`. Unlabelled values show as they are.
- `requiresClass`: `{ "<requires value>": "thin" | "thick" }`, parts that belong to one class's set-up
  (the gear cutter's master and blank for each size): shown only while that class is chosen, and not
  with none, whatever their checkbox says, as the game only ever has one fitted.
- `prop`: a box laid on one of the rig's line anchors, such as a trunk on the bed.
  - `label`; `on`, the anchor's key; `colour`; `default`, an option id or `"none"`.
  - `options`: `{ "id", "label", "size": [length along the line, width, height], "class" }`, in blocks. The box is centred on the line's origin with its underside on it.
  - `placement`: `"underside"` (the default) or `"axis"`, the box centred on the line, as a trunk on a ring's axis.
  - `moves`: `"trunk"` makes the prop the trunk of the rig's `trunkPath`: its nose is at `nose0` + T, it
    moves as T does, and each option's `class` (`"thin"` or `"thick"`) is what the rig's k reads. Each
    option then needs a class, and its length must be that class's `trunkPath.lengths`. Posed by hand,
    choosing an option sets k and p = 1 (none: k = 0, p = 0).
- `play`: the cycle Play runs.
  - `secondsPerTurn` (above 0) is where the input speed slider starts, as seconds per turn; without it
    the slider starts at 1 RPS. (It used to be Play's fixed speed; the reader's slider now sets it.)
  - `turnsPerWork`, a number or a rig path such as `"cut.turnsPerTooth"`: shaft turns per unit of W,
    for a rig whose work is geared to the shaft (no trunk prop). While no phase runs it, Play moves W
    forward by that rate whichever way the shaft turns, up to the class's end, and holds it there;
    with no class chosen W stays put. The gear cutter's play is just this, with no phases.

  - `edge`, `{ "top", "bottom" }`: the height of the working edge at depth 0 and at depth 1, as numbers or rig paths such as `"saw.topY"`. A phase can then run to `"contact"`, the depth where the edge meets the prop's top.
  - `presenceRate` (default 12 a second, the game's) is how fast p eases in and out.
  - `phases`, each `{ "id", "label", "input", "to", "wait", "turns" or "seconds", "lifting", "prop", "next", "nextWithoutProp", "startIf" }`:
    - `input` is what the phase moves: `"depth"` (the default) or `"work"` (W; `"trunk"` is the same;
      needs a prop that moves with the trunk).

    - `to` is a depth (0..1) or `"contact"`; for a trunk phase, T in blocks or `"end"`, the end of the
      chosen class's trip. `turns` (of the shaft) or `seconds` is how long the phase takes to get there
      from where it starts. `lifting` (0 or 1) holds during the phase.
    - `wait: true` moves nothing (no `input` or `to`) and lasts its `turns` or `seconds`: a pause.
    - `prop` is `"load"` (the chosen prop goes on as the phase starts) or `"clear"` (it comes off).
      A trunk that moves goes on at T = 0, as the chosen class.
    - At its target (or the end of its wait) the phase goes to `next`, or to `nextWithoutProp` when the reader chose no prop.
    - Play starts in the first phase whose `startIf` holds (`lifting`, `prop`: whether one is chosen,
      `depthAtMost`, `travelling`: whether a trunk is chosen and part-way through, 0 < T < its end); a
      phase with no `startIf` is never chosen to start.
  - With a trunk that moves, Play also eases p towards 1 while the trunk is on and 0 once it is
    cleared, keeps k until p is back to 0 (so the parts ease back rather than jump, as the game's
    renderer does), and turns φ with the trunk as it moves (ΔT over `feed.blocksPerRadian`).
  - A prop chosen while Play runs goes on at the next `load`.

The bucking sawmill's script is the gameplay's cycle: wind up 6 turns with the lift clutch in,
then drop onto the trunk if one is loaded and cut down through it in 8 turns (in the game a cut
takes the logs times `RevolutionsPerStoredLog`, over the blade kit's speed), or sink empty to the
bed in 6 turns; then wind up again. Its prop has the two trunks the game shows, whatever a trunk's
own size: thin (1×1×4, Logging Expanded's `lg` model) and thick (2×2×5, its `xxl`). Its `requires`
are the parts the gameplay fits, the one `blade` kit standing for both saws' blades.

A machine the trunk travels through scripts the trip instead: load the trunk on the infeed bed (a
wait while p eases in), feed it to `"end"` in its turns, a pause delivered on the outfeed bed, take
it away (a wait while p eases out), and load the next; with no trunk chosen, turn empty.

```json
"phases": [
  { "id": "load", "label": "Loaded on the infeed bed", "wait": true, "seconds": 1, "prop": "load", "next": "feed", "nextWithoutProp": "idle", "startIf": { "travelling": false } },
  { "id": "feed", "label": "Feeding through the ring", "input": "trunk", "to": "end", "turns": 40, "next": "delivered", "startIf": { "travelling": true } },
  { "id": "delivered", "label": "Delivered on the outfeed bed", "wait": true, "seconds": 1.5, "next": "clear" },
  { "id": "clear", "label": "Taken away", "wait": true, "seconds": 1, "prop": "clear", "next": "load" },
  { "id": "idle", "label": "Turning empty", "wait": true, "turns": 2, "prop": "clear", "next": "load" }
]
```

## The data step

`site/scripts/models.ts` is a Vite plugin (`vite.config.ts`). It reads the manifest, checks it
and every file it names (each driver must evaluate, rides must not loop, the scenario must fit
the rig: `site/src/lib/model-manifest.ts`), and publishes

```
models/index.json              the manifest as the app reads it, with element and part counts
models/<id>/shape.json         copied from the shape's path
models/<id>/rig.json           copied from the rig's path
```

- `npm --prefix site run build` writes them into `dist/`, so `pages.yml` (which runs the
  build) and the end-to-end server (`e2e/serve.ts`) get them with no step of their own. A
  broken manifest or model fails the build.
- `npm --prefix site run dev` serves them from the repository on every request and reloads the
  page when one of them changes, so regenerating a model shows up at once.

Nothing is copied into the repository: the files under `mods-src/` are the only copy in git.
The site deployed by `pages.yml` shows the models as they are on main when it runs (after a
release or `next`), not as of any release.

The model page is a lazily loaded chunk, and three.js (about 570 kB, 140 kB gzipped) another
one loaded only when a model is opened, so the recipe browser's first load does not carry
either. A model's files are fetched when its page opens.

## Adding a model

1. Generate or write the model's shape (and rig, if it moves) under `mods-src/<modid>/`.
   [Animated machine models](../modeling/animated-machine-models.md) is the guide to making
   one this way: generating the shape, writing the rig, and reviewing it in this viewer.
2. Add an entry to `site/models.json`.
3. Check it: `npm --prefix site test` (the manifest test publishes every model) and
   `npm --prefix site run dev`, then open `http://localhost:5173/#/models/<id>`.

Nothing else: no component, route or workflow changes.

## Code and tests

| File | |
|---|---|
| `site/src/lib/rig.ts` | Shape flattening (VS's rotation order and child frames) and the rig maths: globs, drivers (with the trunk path's), ride order, part matrices. Pure. |
| `site/src/lib/model-anchors.ts` | Anchor discovery, footprint, side arrows. |
| `site/src/lib/model-scenario.ts` | Scenario types, props, contact depth and the play state machine. |
| `site/src/lib/model-view.ts` | What the page shows for a shape and rig: parts, textures, colours, controls. |
| `site/src/lib/model-manifest.ts` | Manifest and model checks, what the build publishes. |
| `site/src/components/ModelsRoute.svelte`, `ModelPage.svelte` | The index and the model page. |
| `site/src/viewer/model-scene.ts` | The three.js scene. |

`site/test/rig.test.ts` replays `mods-src/seraphhorizons/tests/Machines/driver-fixture.json`, every driver
(old and new) alone and composed in a small rig, plus the drivers every parser must refuse, as
`Machines/tools/make_fixture.py` computes them with the reference maths; the C# replays the same file.
It also holds the TypeScript to `mods-src/seraphhorizons/tests/BuckingSawmill/rig-reference.json`,
the part matrices `tools/make_shape.py` computes with its reference maths at a spread of poses,
which the mod's C# tests also check against: the Python, the C# and the site agree to the file's
six decimals. It also flattens and poses the shipped shape at rest and recomputes every cell's
collision boxes the way `make_shape.py` does, which must give back the rig file's `cells`.
`site/test/rosser.test.ts` does the same for the rosser: every pose of
`mods-src/seraphhorizons/tests/Rosser/rig-reference.json` (with T, k, p and φ), and every cell's boxes
rebuilt from the shipped shape, hollow cells with none.
`site/test/models.test.ts` covers the manifest, anchors, the play script and the view, the mill's
and a trunk travelling through a machine (a small rig on the rosser's trunk path);
`site/e2e/models.spec.ts` the pages in a browser, with or without WebGL.

Without WebGL (or when three.js fails to load) the stage says so, and the controls, legend and
element list still work. When a model's files fail to load, the page says so instead of the
stage.
