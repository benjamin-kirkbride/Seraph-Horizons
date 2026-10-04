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

The rig format is the bucking sawmill's (`mods-src/buckingsawmill/README.md`, "Rig schema").
Its drivers read four inputs, and the viewer shows a control only for those some driver reads:

| Input | Read by | Control |
|---|---|---|
| θ, the shaft angle | `rotate`, `slide`, `swing` | Slider, 0 to 359° |
| ψ, the shaft's travel | `rotate` with `rectified` | Added up from every change of θ, shown under the slider |
| depth, 0..1 | `feed`, `step`, `stretch` | Slider |
| lifting, 0 or 1 | `step` with a `lifting` gate | Checkbox |

When θ or ψ is read there is also a Play button and a "turn backwards" toggle. Play turns the
shaft; with a play script (below) it also runs the inputs through the model's cycle.

### Overlays

Anchors are recognised by their shape, so a new rig gets overlays without code changes
(`site/src/lib/model-anchors.ts`):

| In the rig | Drawn as |
|---|---|
| `cells` | Block cells (outlined, the origin cell `[0,0,0]` in orange, a floor grid) and collision boxes |
| `"<name>Cell": [x, y, z]` | That cell outlined; with `"<name>Face": "<side>"`, the face shaded and an arrow into it |
| `"<name>Side": "<side>"` | An arrow into that side of the footprint, on a line anchor running that way if there is one |
| `"<name>": { "pos": [x, y, z] }` | A point; with `"<name>Side"`, an arrow out that way |
| `"<name>": { "origin", "axis", "length" }` | A line along the axis, centred on origin |
| `"<name>": { "<x>Y": number, ... }` | A level line across the footprint at each such height |

Keys starting with `_` are comments. Any other key is listed under the overlays as not drawn.

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
      "shape": "mods-src/buckingsawmill/assets/buckingsawmill/shapes/block/buckingmill.json",
      "rig": "mods-src/buckingsawmill/assets/buckingsawmill/config/rig.json",
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

- `inputs`: labels for the controls, `{ "theta" | "depth" | "lifting" | "reverse": { "label", "hint" } }`.
- `requires`: a label for each `requires` value, e.g. `{ "sash1": "Sash 1" }`. Unlabelled values show as they are.
- `prop`: a box laid on one of the rig's line anchors, such as a trunk on the bed.
  - `label`; `on`, the anchor's key; `colour`; `default`, an option id or `"none"`.
  - `options`: `{ "id", "label", "size": [length along the line, width, height] }`, in blocks. The box is centred on the line's origin with its underside on it.
- `play`: the cycle Play runs.
  - `secondsPerTurn` (default 1.2) sets the shaft's speed.
  - `edge`, `{ "top", "bottom" }`: the height of the working edge at depth 0 and at depth 1, as numbers or rig paths such as `"saw.topY"`. A phase can then run to `"contact"`, the depth where the edge meets the prop's top.
  - `phases`, each `{ "id", "label", "to", "turns" or "seconds", "lifting", "prop", "next", "nextWithoutProp", "startIf" }`:
    - `to` is a depth (0..1) or `"contact"`, and `turns` (of the shaft) or `seconds` is how long the phase takes to get there from where it starts. `lifting` (0 or 1) holds during the phase.
    - `prop` is `"load"` (the chosen prop goes on as the phase starts) or `"clear"` (it comes off).
    - At its target the phase goes to `next`, or to `nextWithoutProp` when the reader chose no prop.
    - Play starts in the first phase whose `startIf` holds (`lifting`, `prop`: whether one is chosen, `depthAtMost`); a phase with no `startIf` is never chosen to start.
  - A prop chosen while Play runs goes on at the next `load`.

The bucking sawmill's script is the gameplay's cycle: wind up 6 turns with the lift clutch in,
then drop onto the trunk if one is loaded and cut down through it in 8 turns (in the game a cut
takes the logs times `RevolutionsPerStoredLog`), or sink empty to the bed in 6 turns; then wind
up again.

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
| `site/src/lib/rig.ts` | Shape flattening (VS's rotation order and child frames) and the rig maths: globs, drivers, ride order, part matrices. Pure. |
| `site/src/lib/model-anchors.ts` | Anchor discovery, footprint, side arrows. |
| `site/src/lib/model-scenario.ts` | Scenario types, props, contact depth and the play state machine. |
| `site/src/lib/model-view.ts` | What the page shows for a shape and rig: parts, textures, colours, controls. |
| `site/src/lib/model-manifest.ts` | Manifest and model checks, what the build publishes. |
| `site/src/components/ModelsRoute.svelte`, `ModelPage.svelte` | The index and the model page. |
| `site/src/viewer/model-scene.ts` | The three.js scene. |

`site/test/rig.test.ts` holds the TypeScript to `mods-src/buckingsawmill/tests/rig-reference.json`,
the part matrices `tools/make_shape.py` computes with its reference maths at a spread of poses,
which the mod's C# tests also check against: the Python, the C# and the site agree to the file's
six decimals. It also flattens and poses the shipped shape at rest and recomputes every cell's
collision boxes the way `make_shape.py` does, which must give back `rig.json`'s `cells`.
`site/test/models.test.ts` covers the manifest, anchors, the play script and the view;
`site/e2e/models.spec.ts` the pages in a browser, with or without WebGL.

Without WebGL (or when three.js fails to load) the stage says so, and the controls, legend and
element list still work. When a model's files fail to load, the page says so instead of the
stage.
