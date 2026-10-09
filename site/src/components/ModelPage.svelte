<script lang="ts">
  // One model: the 3D stage (three.js, loaded on demand), controls generated from the rig, the
  // pinned element's details and the parts legend with every element listed. The controls and
  // the list work without WebGL. The maths is in src/lib/ (rig.ts, model-view.ts,
  // model-scenario.ts); the scene in src/viewer/model-scene.ts.
  import { onDestroy } from "svelte";
  import { advanceFrame, animationOptions, animationSpeed, FRAMES_PER_SECOND, jointDeltas, loopsByDefault } from "../lib/keyframes.ts";
  import { loadModelFiles } from "../lib/model-data.ts";
  import type { PublishedModel } from "../lib/model-manifest.ts";
  import {
    advance,
    applyState,
    classShows,
    choiceOf,
    contactDepth,
    defaultInputSpeed,
    enterPhase,
    feedAdvance,
    feedBlocksPerRadian,
    initialFitted,
    INPUT_SPEED_MAX,
    optionClass,
    pickChoice,
    playTrip,
    propBox,
    rigNumber,
    startPhase,
    stateOf,
    type Motion,
    type PlayContext,
  } from "../lib/model-scenario.ts";
  import {
    BOGIE_PART,
    blocksPerSecondAt,
    rolledBy,
    TRACK_OVERLAY,
    trackLayout,
    trackScroll,
    turnsPerSecondAt,
    vehicleOf,
    withBogies,
    type Vehicle,
  } from "../lib/model-vehicle.ts";
  import { buildModelView, elementDetails, type ModelView } from "../lib/model-view.ts";
  import { formatRoute } from "../lib/route.ts";
  import { classIndex, fitted, partMatrices, thetaTurns, workEnd, wrappedDelta, type Rig } from "../lib/rig.ts";
  import { mt } from "../lib/model-strings.ts";
  import { REPO_URL, t } from "../lib/strings.ts";
  import type { ColourMode, ModelScene, SceneColours, ViewName } from "../viewer/model-scene.ts";
  import { webglAvailable } from "../viewer/webgl.ts";

  let { model }: { model: PublishedModel } = $props();
  const s = mt;

  // ---- data
  let view = $state<ModelView | null>(null);
  let rig = $state<Rig | null>(null);
  let vehicle = $state<Vehicle | null>(null);
  let failed = $state<string | null>(null);

  $effect(() => {
    const m = model;
    loadModelFiles(m).then(
      (files) => {
        try {
          // A vehicle's bogies are its own shape, added to the model where the renderer draws them.
          const v = files.rig ? vehicleOf(m.scenario?.vehicle, files.rig) : null;
          const merged = files.rig ? withBogies(files.shape, files.rig, files.bogie, v) : { shape: files.shape, rig: null };
          view = buildModelView(merged.shape, merged.rig, m.scenario);
          rig = merged.rig;
          vehicle = v;
          const values = view.requires.map((r) => r.value);
          fittedState = initialFitted(values, m.scenario?.choices);
          const opening = m.scenario?.states?.options.find((o) => o.id === m.scenario?.states?.default);
          if (opening) fittedState = applyState(fittedState, opening, values, m.scenario?.choices);
          pickedState = opening?.id ?? null;
          overlays = {
            cells: true,
            collision: false,
            ...(v?.track ? { [TRACK_OVERLAY]: true } : {}),
            ...Object.fromEntries(view.anchors.map((a) => [a.key, true])),
          };
          colourMode = view.hasRig ? "part" : "texture";
          propChoice = m.scenario?.prop?.default ?? "none";
          inputSpeed = defaultInputSpeed(m.scenario?.play, v?.speed ? turnsPerSecondAt(v.speed, v) : null);
          choose();
          selectAnimation(m.scenario?.animations?.default?.toLowerCase() ?? "");
        } catch (e) {
          failed = (e as Error).message;
        }
      },
      (e: unknown) => (failed = (e as Error).message),
    );
  });

  const scenario = $derived(model.scenario);
  const play = $derived(scenario?.play);
  const propSpec = $derived(scenario?.prop);
  const propLine = $derived(view && propSpec ? (view.anchors.find((a) => a.kind === "line" && a.key === propSpec.on) ?? null) : null);

  // ---- state
  // The oil tank starts full: a machine is shown as it runs.
  let motion = $state<Motion>({ theta: 0, travel: 0, depth: 0, lifting: 0, phase: null, phaseFrom: 0, propOn: false, work: 0, size: 0, presence: 0, feed: 0, oil: 1 });
  // Play's input speed: shaft turns a second of real time, 0 to INPUT_SPEED_MAX (0 stops it).
  let inputSpeed = $state(1);
  let reverse = $state(false);
  let playing = $state(false);
  let fittedState = $state<Record<string, boolean>>({});
  // The state last picked from the scenario's states: two states may fit the same parts (built empty, and left empty).
  let pickedState = $state<string | null>(null);
  let overlays = $state<Record<string, boolean>>({});
  let edges = $state(true);
  let colourMode = $state<ColourMode>("part");
  let propChoice = $state("none");
  let picked = $state<number | null>(null);
  let hovered = $state<number | null>(null);
  let tip = $state<{ x: number; y: number } | null>(null);
  let openGroups = $state<Record<string, boolean>>({});

  const propOption = $derived(propSpec?.options.find((o) => o.id === propChoice) ?? null);
  // A prop that is the trunk of the rig's trunk path: it travels with W, and its option is the class.
  const trunkProp = $derived(propSpec?.moves === "trunk" && view?.path?.kind === "trunk" ? view.path : null);
  const tripLength = $derived(view?.path ? workEnd(view.path, motion.size ?? 0) : 0);
  // The work's labels: the scenario's, else the trunk's for a trunkPath, else the rig's own name for its work.
  const workIsTrunk = $derived(view?.path?.kind === "trunk");
  const workLabel = $derived(scenario?.inputs?.work ?? scenario?.inputs?.trunk);
  const workName = $derived(view?.path ? (workIsTrunk ? s.trunkTravel : s.workOf(view.path.name)) : "");
  const sizeNames = $derived(scenario?.inputs?.size?.names ?? s.sizeNames);
  // Shaft turns per unit of W when Play gears the work to the shaft (play.turnsPerWork); null otherwise.
  const gearedTurns = $derived.by(() => {
    const ref = play?.turnsPerWork;
    if (ref === undefined || !rig || !view?.path) return null;
    try {
      return rigNumber(rig, ref);
    } catch {
      return null;
    }
  });
  const matrices = $derived(
    view
      ? partMatrices(
          view.parts.map((p) => p.part),
          {
            theta: motion.theta,
            depth: motion.depth,
            lifting: motion.lifting,
            travel: motion.travel,
            work: motion.work,
            size: motion.size,
            presence: motion.presence,
            feed: motion.feed,
            oil: motion.oil,
          },
          view.order,
          view.path,
        )
      : [],
  );
  const visible = $derived(view ? view.parts.map((p) => fitted(p.part.requires, fittedState) && classShows(p.part.requires, classIndex(motion.size), scenario)) : []);
  // While a cycle runs (or is paused part-way) it decides whether the prop is on; posed by hand, the choice does.
  const propShown = $derived(motion.phase !== null ? motion.propOn : propOption !== null);
  const box = $derived(
    propShown && propOption && propLine && propLine.kind === "line"
      ? propBox(propOption, propLine, { placement: propSpec?.placement, ...(trunkProp ? { nose: trunkProp.nose0 + (motion.work ?? 0) } : {}) })
      : null,
  );
  // θ runs on unwrapped; its slider spans the rig's cycle, the turns it takes every part reading θ to
  // come round (the handcar's beam rocks once in three turns of its axle).
  const cycleTurns = $derived(view ? thetaTurns(view.parts.map((p) => p.part)) : 1);
  const cycleDeg = $derived(Math.round((((motion.theta * 180) / Math.PI) % (360 * cycleTurns)) + 360 * cycleTurns) % (360 * cycleTurns));
  const thetaDeg = $derived(cycleDeg % 360);
  const thetaNote = $derived(
    cycleTurns === 1 ? "" : vehicle?.cycle ? s.ofCycle(Math.floor((cycleDeg / (360 * cycleTurns)) * 100), vehicle.cycle) : s.turnOf(Math.floor(cycleDeg / 360) + 1, cycleTurns),
  );
  // A vehicle: how far it has rolled (towards its front when positive), blocks.
  let rolled = $state(0);
  const track = $derived(vehicle?.track && view ? trackLayout(vehicle.track, vehicle, view.bounds) : null);
  const phaseLabel = $derived(motion.phase ? (play?.phases.find((p) => p.id === motion.phase)?.label ?? motion.phase) : null);
  const status = $derived(playing ? (phaseLabel ?? (vehicle ? s.rolling : s.playHintTurn)) : phaseLabel ? s.paused(phaseLabel.toLowerCase()) : s.posedByHand);

  // ---- the shape's own keyframe animations (keyframes.ts): one at a time, posed at a frame
  let animCode = $state("");
  let animFrame = $state(0);
  let animPlaying = $state(false);
  let animSpeed = $state(1);
  let animLoop = $state(true);
  const animations = $derived(view?.animation.animations ?? []);
  const currentAnim = $derived(animations.find((a) => a.code === animCode) ?? null);
  const animGroups = $derived(animationOptions(animations, scenario?.animations));
  // Each joint's motion at the frame; null at rest. The rig's part matrices pose the parts around them.
  const jointMatrices = $derived(view && currentAnim ? jointDeltas(view.animation, view.flat, currentAnim, animFrame) : null);

  // ---- the 3D scene
  let canvas = $state<HTMLCanvasElement>();
  let labelLayer = $state<HTMLElement>();
  let probe = $state<HTMLElement>();
  let scene = $state<ModelScene | null>(null);
  let sceneState = $state<"loading" | "ready" | "nowebgl" | "failed">("loading");

  function readColours(): SceneColours {
    const get = (name: string, fallback: string) => {
      const el = probe?.querySelector<HTMLElement>(`[data-token="${name}"]`);
      return el ? getComputedStyle(el).color || fallback : fallback;
    };
    return { background: get("stage", "#e4e1d8"), edge: get("edge", "#2a2722"), grid: get("grid", "#8f897c"), highlight: get("highlight", "#d9480f") };
  }

  $effect(() => {
    const v = view;
    const c = canvas;
    const layer = labelLayer;
    if (!v || !c || !layer) return;
    if (!webglAvailable()) {
      sceneState = "nowebgl";
      return;
    }
    let gone = false;
    let made: ModelScene | null = null;
    import("../viewer/model-scene.ts").then(
      (mod) => {
        if (gone) return;
        try {
          made = new mod.ModelScene(c, layer, v, readColours(), propSpec?.colour, track);
          scene = made;
          sceneState = "ready";
        } catch {
          sceneState = "nowebgl";
        }
      },
      () => {
        if (!gone) sceneState = "failed";
      },
    );
    return () => {
      gone = true;
      made?.dispose();
      scene = null;
    };
  });

  $effect(() => scene?.pose(matrices, visible, jointMatrices));
  $effect(() => scene?.colourBy(colourMode));
  $effect(() => scene?.setEdges(edges));
  $effect(() => scene?.setProp(box));
  $effect(() => {
    if (vehicle && track) scene?.setTrackScroll(trackScroll(rolled, track.spacing, vehicle));
  });
  $effect(() => scene?.highlight(picked, hovered));
  $effect(() => {
    const sc = scene;
    if (!sc) return;
    for (const [key, on] of Object.entries(overlays)) sc.setOverlay(key, on);
  });

  // The theme can change under the page: the header's picker sets data-theme, the system its scheme.
  $effect(() => {
    const sc = scene;
    if (!sc) return;
    const update = () => sc.setColours(readColours());
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    const mq = matchMedia("(prefers-color-scheme: dark)");
    mq.addEventListener("change", update);
    return () => {
      observer.disconnect();
      mq.removeEventListener("change", update);
    };
  });

  // ---- pointer: click or tap pins, hover previews (mouse only)
  let down: { x: number; y: number } | null = null;
  let hoverAt: { x: number; y: number } | null = null;
  let hoverFrame = 0;

  function onPointerDown(e: PointerEvent) {
    down = { x: e.clientX, y: e.clientY };
  }
  function onPointerUp(e: PointerEvent) {
    // A click, not the end of an orbit drag: pin what is under the pointer; empty space clears.
    if (down && scene && Math.hypot(e.clientX - down.x, e.clientY - down.y) < 5) picked = scene.pick(e.clientX, e.clientY);
    down = null;
  }
  function onPointerMove(e: PointerEvent) {
    if (e.pointerType !== "mouse" || e.buttons & 7 || !scene) return;
    hoverAt = { x: e.clientX, y: e.clientY };
    if (hoverFrame) return;
    hoverFrame = requestAnimationFrame(() => {
      hoverFrame = 0;
      if (!hoverAt || !scene || !canvas) return;
      hovered = scene.pick(hoverAt.x, hoverAt.y);
      const r = canvas.getBoundingClientRect();
      tip = hovered === null ? null : { x: hoverAt.x - r.left, y: hoverAt.y - r.top };
    });
  }
  function onPointerLeave() {
    hoverAt = null;
    hovered = null;
    tip = null;
  }
  function onKey(e: KeyboardEvent) {
    if (e.key === "Escape") {
      picked = null;
      hovered = null;
      tip = null;
    }
  }

  // ---- motion
  function byHand() {
    playing = false;
    motion.phase = null;
  }
  /** Sets θ to a place in the rig's cycle, the shortest way round it from where it is. */
  function setTheta(deg: number) {
    const turned = wrappedDelta(motion.theta, (deg * Math.PI) / 180, 2 * Math.PI * cycleTurns);
    motion.travel += Math.abs(turned);
    motion.theta += turned;
    if (vehicle) rolled += rolledBy(turned, vehicle);
  }
  /** Posed by hand, the prop's choice is the trunk's class, fully present, kept within its trip. */
  function choose() {
    if (motion.phase !== null || propSpec?.moves !== "trunk") return;
    const size = optionClass(propOption);
    motion.size = size;
    motion.presence = size > 0 ? 1 : 0;
    if (view?.path) motion.work = Math.min(motion.work ?? 0, workEnd(view.path, size));
  }
  /** A size chosen without a prop (a rig that reads the class, with no trunk prop in its scenario). */
  function setSize(size: number) {
    byHand();
    motion.size = size;
    motion.presence = size > 0 ? 1 : 0;
    if (view?.path) motion.work = Math.min(motion.work ?? 0, workEnd(view.path, size));
  }
  function playContext(): PlayContext {
    const chosen = propOption !== null;
    const b = propOption && propLine && propLine.kind === "line" ? propBox(propOption, propLine) : null;
    const top = b ? b.centre[1] + b.size[1] / 2 : 0;
    const trip = playTrip(propSpec, propOption, view?.path ?? null);
    return {
      play,
      direction: reverse ? -1 : 1,
      propChosen: chosen,
      contact: play && rig && b ? contactDepth(play, rig, top) : motion.depth,
      ...(trip ? { trip } : {}),
      ...(feedBlocksPerRadian(rig) !== undefined ? { blocksPerRadian: feedBlocksPerRadian(rig) } : {}),
      ...(gearedTurns !== null && classIndex(motion.size) > 0 ? { geared: { turns: gearedTurns, end: tripLength } } : {}),
      turnsPerSecond: inputSpeed,
    };
  }
  let frame = 0;
  let last: number | null = null;
  function tick(time: number) {
    frame = requestAnimationFrame(tick);
    const dt = last === null ? 0 : Math.min(0.1, (time - last) / 1000);
    last = time;
    const next = advance(playContext(), motion, dt);
    if (vehicle) rolled += rolledBy(next.theta - motion.theta, vehicle);
    motion = next;
  }
  function togglePlay() {
    if (playing) {
      playing = false;
      cancelAnimationFrame(frame);
      last = null;
      return;
    }
    if (motion.phase === null) {
      const ctx = playContext();
      // Posed by hand, a chosen trunk is on: Play carries on from where it is.
      if (ctx.trip) motion.propOn = ctx.propChosen;
      const first = startPhase(play, motion, ctx.propChosen, ctx.trip);
      if (first) motion = enterPhase(ctx, motion, first);
    }
    playing = true;
    last = null;
    frame = requestAnimationFrame(tick);
  }
  /** Selects an animation by code ("" for none), at its first frame, looping as the game does and at its speed. */
  function selectAnimation(code: string) {
    const a = animations.find((x) => x.code === code) ?? null;
    animCode = a ? a.code : "";
    animFrame = 0;
    if (!a) stopAnimation();
    else {
      animLoop = loopsByDefault(a);
      animSpeed = animationSpeed(scenario?.animations, a.code);
    }
  }
  let animRaf = 0;
  let animLast: number | null = null;
  function animTick(time: number) {
    animRaf = requestAnimationFrame(animTick);
    const dt = animLast === null ? 0 : Math.min(0.1, (time - animLast) / 1000);
    animLast = time;
    if (!currentAnim) return stopAnimation();
    const next = advanceFrame(currentAnim, animFrame, dt, animSpeed, animLoop);
    animFrame = next.frame;
    if (next.ended) stopAnimation();
  }
  function stopAnimation() {
    animPlaying = false;
    cancelAnimationFrame(animRaf);
    animLast = null;
  }
  function toggleAnimation() {
    if (animPlaying || !currentAnim) return stopAnimation();
    // Played to its end without looping: Play starts it again.
    if (!animLoop && animFrame >= currentAnim.frames - 1) animFrame = 0;
    animPlaying = true;
    animLast = null;
    animRaf = requestAnimationFrame(animTick);
  }
  onDestroy(() => {
    cancelAnimationFrame(animRaf);
    cancelAnimationFrame(frame);
    cancelAnimationFrame(hoverFrame);
  });

  // ---- pinned details
  const pickedPart = $derived(view && picked !== null ? view.parts[view.elementPart[picked]!]! : null);
  const requiresLabel = (value: string) => {
    const label = view?.requires.find((r) => r.value === value)?.label ?? value;
    const choice = choiceOf(scenario?.choices, value);
    return choice ? `${choice.label}: ${label}` : label;
  };
  // The scenario's state the fitted parts are in, or null when they were set by hand.
  const statesSpec = $derived(scenario?.states);
  const currentState = $derived(
    view && statesSpec ? stateOf(fittedState, statesSpec, pickedState, view.requires.map((r) => r.value), scenario?.choices) : null,
  );
  const currentStateHint = $derived(statesSpec?.options.find((o) => o.id === currentState)?.hint ?? null);
  function pickState(id: string) {
    const state = statesSpec?.options.find((o) => o.id === id);
    if (!view || !state) return;
    fittedState = applyState(fittedState, state, view.requires.map((r) => r.value), scenario?.choices);
    pickedState = id;
  }
  // The requires values that are not in a choice: a checkbox each.
  const freeRequires = $derived(view ? view.requires.filter((r) => !choiceOf(scenario?.choices, r.value)) : []);
  let copyState = $state<{ which: string; text: string } | null>(null);
  let copyTimer: ReturnType<typeof setTimeout> | undefined;
  let nameEl = $state<HTMLElement>();
  let partEl = $state<HTMLElement>();

  function say(which: string, text: string) {
    copyState = { which, text };
    clearTimeout(copyTimer);
    copyTimer = setTimeout(() => (copyState = null), 1400);
  }
  // The Clipboard API, else a hidden textarea and execCommand, else select the text so it can be copied by hand.
  async function copy(which: string, text: string, el: HTMLElement | undefined) {
    try {
      await navigator.clipboard.writeText(text);
      say(which, s.copied);
      return;
    } catch {
      // fall through
    }
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed";
    ta.style.opacity = "0";
    document.body.appendChild(ta);
    ta.select();
    let ok = false;
    try {
      ok = document.execCommand("copy");
    } catch {
      ok = false;
    }
    ta.remove();
    if (ok) return say(which, s.copied);
    if (el) {
      const range = document.createRange();
      range.selectNodeContents(el);
      getSelection()?.removeAllRanges();
      getSelection()?.addRange(range);
    }
    say(which, s.copySelect);
  }

  // ---- legend
  const groups = $derived(
    !view
      ? []
      : colourMode === "part"
        ? view.parts.map((p, i) => ({
            key: `part:${p.id}`,
            colour: p.colour,
            name: p.id,
            note: [
              p.id === "unmatched" ? s.unmatched : vehicle && p.id === BOGIE_PART ? s.bogiesNote : p.part.requires ? s.needs(requiresLabel(p.part.requires)) : s.always,
              p.part.ride ? s.rides(p.part.ride) : "",
            ]
              .filter(Boolean)
              .join(" · "),
            elements: p.elements,
            on: visible[i] ?? true,
          }))
        : view.textures.map((tx) => ({ key: `tex:${tx.code}`, colour: tx.colour, name: `#${tx.code}`, note: tx.path ?? "", elements: tx.elements, on: true })),
  );
  const viewNames = ["angled", "opposite", "front", "side", "top", "reset"] as const;
  const sourceUrl = (path: string) => `${REPO_URL}/blob/main/${path}`;
</script>

<svelte:window onkeydown={onKey} />

<nav class="crumbs" aria-label="Breadcrumb"><a href={formatRoute({ view: "models" })}>{mt.heading}</a> › {model.title}</nav>
<h1>{model.title}</h1>
<p class="description">{model.description}</p>
<p class="credit" data-testid="model-credit">
  <strong>{s.credit}:</strong>
  {model.credit}
  {#if model.creditUrl}<a href={model.creditUrl} rel="noopener noreferrer" target="_blank">{s.creditMore}</a>{/if}
</p>

<!-- Colours for three.js, read from the theme's tokens. -->
<div class="probe" aria-hidden="true" bind:this={probe}>
  <span data-token="stage"></span><span data-token="edge"></span><span data-token="grid"></span><span data-token="highlight"></span>
</div>

{#if failed}
  <p role="alert" data-testid="model-load-failed">{s.filesFailed} <span class="muted">{failed}</span></p>
{:else if !view}
  <p class="muted">{t.loading}</p>
{:else}
  <div class="bench">
    <div class="stage-col">
      <div class="stage" data-scene={sceneState} data-testid="model-stage">
        <canvas
          bind:this={canvas}
          aria-label={s.stageLabel(model.title)}
          data-testid="model-canvas"
          onpointerdown={onPointerDown}
          onpointerup={onPointerUp}
          onpointermove={onPointerMove}
          onpointerleave={onPointerLeave}
        ></canvas>
        <div class="labels" bind:this={labelLayer}></div>
        {#if sceneState === "ready"}
          <div class="views" role="group" aria-label={s.views}>
            {#each viewNames as v (v)}
              <button type="button" onclick={() => scene?.setView(v as ViewName | "reset")}>{s.viewNames[v]}</button>
            {/each}
          </div>
        {:else if sceneState === "nowebgl"}
          <p class="fallback" role="status" data-testid="model-webgl-missing">{s.noWebgl}</p>
        {:else if sceneState === "failed"}
          <p class="fallback" role="alert">{s.viewerFailed}</p>
        {:else}
          <p class="fallback muted">{s.loadingViewer}</p>
        {/if}
        {#if tip && hovered !== null}
          <div class="tip" style:left="{tip.x}px" style:top="{tip.y}px">{view.flat[hovered]!.element.name} · {view.parts[view.elementPart[hovered]!]!.id}</div>
        {/if}
      </div>

      <div class="pick" aria-live="polite" data-testid="model-pick">
        {#if picked !== null && pickedPart}
          {@const f = view.flat[picked]!}
          <div class="pick-head">
            <span class="name code" bind:this={nameEl} data-testid="model-pick-name">{f.chain.join(" › ")}</span>
            <span class="btns">
              <button type="button" onclick={() => copy("name", f.element.name, nameEl)}>{copyState?.which === "name" ? copyState.text : s.copyName}</button>
              <button type="button" onclick={() => copy("part", pickedPart.id, partEl)}>{copyState?.which === "part" ? copyState.text : s.copyPart}</button>
              <button type="button" onclick={() => (picked = null)}>{s.clear}</button>
            </span>
          </div>
          <dl>
            <dt>{s.part}</dt>
            <dd>
              <span class="code" bind:this={partEl}>{pickedPart.id}</span>
              {#if pickedPart.part.requires}<span class="muted">({s.needs(requiresLabel(pickedPart.part.requires))})</span>{/if}
            </dd>
            <dt>{s.texture}</dt>
            <dd class="code">{view.elementTextures[picked]!.map((c) => `#${c}`).join(", ") || "–"}</dd>
            {#each elementDetails(view, picked) as row (row.label)}
              <dt>{row.label}</dt>
              <dd class="code">{row.value}</dd>
            {/each}
          </dl>
        {:else}
          <span class="muted">{s.pickHint}</span>
        {/if}
      </div>
    </div>

    <aside class="controls" data-testid="model-controls">
      {#if animations.length > 0}
        <fieldset data-testid="model-animation">
          <legend>{s.animation}</legend>
          <select aria-label={s.animation} value={animCode} onchange={(e) => selectAnimation(e.currentTarget.value)} data-input="animation">
            <option value="">{s.animationNone}</option>
            {#each animGroups as g, gi (gi)}
              {#if g.label === null && animGroups.length === 1}
                {#each g.options as o (o.code)}<option value={o.code}>{o.label}</option>{/each}
              {:else}
                <optgroup label={g.label ?? s.animationOther}>
                  {#each g.options as o (o.code)}<option value={o.code}>{o.label}</option>{/each}
                </optgroup>
              {/if}
            {/each}
          </select>
          {#if currentAnim}
            <label class="slider">
              <span class="row"><span>{s.animationFrame}</span><output data-testid="model-anim-frame">{s.frameOf(Math.floor(animFrame), currentAnim.frames - 1)}</output></span>
              <input
                type="range"
                min="0"
                max={currentAnim.frames - 1}
                step="1"
                value={Math.floor(animFrame)}
                oninput={(e) => {
                  stopAnimation();
                  animFrame = +e.currentTarget.value;
                }}
                data-input="anim-frame"
              />
            </label>
            <div class="row play-row">
              <button type="button" class="play" aria-pressed={animPlaying} onclick={toggleAnimation} data-input="anim-play">{animPlaying ? s.pause : s.play}</button>
              <label class="check"><input type="checkbox" bind:checked={animLoop} data-input="anim-loop" /> {s.animationLoop}</label>
            </div>
            <label class="slider">
              <span class="row"><span>{s.animationSpeed}</span><output data-testid="model-anim-speed">{s.animationSpeedOf(animSpeed)}</output></span>
              <input type="range" min="0" max="4" step="0.05" bind:value={animSpeed} data-input="anim-speed" />
            </label>
            <span class="muted small">{s.animationLength(currentAnim.frames, currentAnim.frames / FRAMES_PER_SECOND)}. {s.animationEnds[currentAnim.onAnimationEnd] ?? ""}</span>
          {/if}
          <span class="muted small">{s.animationHint}</span>
        </fieldset>
      {/if}
      {#if view.inputs.theta || view.inputs.travel || view.inputs.depth || view.inputs.lifting || view.inputs.work || view.inputs.size || view.inputs.presence || view.inputs.feed || view.inputs.oil}
        <fieldset>
          <legend>{s.motion}</legend>
          {#if view.inputs.theta || view.inputs.travel}
            <label class="slider">
              <span class="row"
                ><span>{scenario?.inputs?.theta?.label ?? s.shaftAngle}</span><output data-testid="model-theta">{thetaDeg}°{#if thetaNote}<span class="muted">, {thetaNote}</span>{/if}</output></span
              >
              <input type="range" min="0" max={360 * cycleTurns - 1} step="1" value={cycleDeg} oninput={(e) => setTheta(+e.currentTarget.value)} data-input="theta" />
              {#if scenario?.inputs?.theta?.hint}<span class="muted small">{scenario.inputs.theta.hint}</span>{/if}
              {#if vehicle}
                <span class="muted small" data-testid="model-rolled">{s.rolled(rolled)}{cycleTurns > 1 && vehicle.cycle ? ` · ${s.cycleDistance(vehicle.cycle, cycleTurns, 2 * Math.PI * vehicle.wheelRadius * cycleTurns)}` : ""}</span>
              {/if}
              {#if view.inputs.travel}<span class="muted small">{s.shaftTravel(Math.round((motion.travel * 180) / Math.PI))}</span>{/if}
              {#if view.inputs.feed}<span class="muted small" data-testid="model-feed">{s.feedTravel(Math.round(((motion.feed ?? 0) * 180) / Math.PI))}</span>{/if}
            </label>
          {/if}
          {#if view.inputs.size && !trunkProp}
            <label class="stack">
              <span>{scenario?.inputs?.size?.label ?? (workIsTrunk ? s.trunkSize : s.workClass)}</span>
              <select value={String(classIndex(motion.size))} onchange={(e) => setSize(+e.currentTarget.value)} data-input="size">
                {#each sizeNames as name, i (i)}<option value={String(i)}>{name}</option>{/each}
              </select>
            </label>
          {/if}
          {#if view.inputs.work && view.path}
            <label class="slider">
              <span class="row"
                ><span>{workLabel?.label ?? workName}</span><output data-testid="model-work"
                  >{s.travelOf(motion.work ?? 0, tripLength, view.path.unit)}</output
                ></span
              >
              <input
                type="range"
                min="0"
                max={tripLength}
                step={view.path.step}
                value={motion.work ?? 0}
                disabled={tripLength <= 0}
                oninput={(e) => {
                  byHand();
                  const to = +e.currentTarget.value;
                  // the feed rolls turn with the trunk, as in the game (forward only)
                  motion.feed = (motion.feed ?? 0) + feedAdvance(motion.work ?? 0, to, feedBlocksPerRadian(rig));
                  motion.work = to;
                }}
                data-input="work"
              />
              {#if workLabel?.hint}<span class="muted small">{workLabel.hint}</span>{/if}
            </label>
          {/if}
          {#if view.inputs.presence}
            <label class="slider">
              <span class="row"><span>{scenario?.inputs?.presence?.label ?? (workIsTrunk ? s.presence : s.workPresence)}</span><output>{(motion.presence ?? 0).toFixed(2)}</output></span>
              <input
                type="range"
                min="0"
                max="1"
                step="0.01"
                value={motion.presence ?? 0}
                disabled={classIndex(motion.size) === 0}
                oninput={(e) => {
                  byHand();
                  motion.presence = +e.currentTarget.value;
                }}
                data-input="presence"
              />
              {#if scenario?.inputs?.presence?.hint}<span class="muted small">{scenario.inputs.presence.hint}</span>{/if}
            </label>
          {/if}
          {#if view.inputs.depth}
            <label class="slider">
              <span class="row"><span>{scenario?.inputs?.depth?.label ?? s.depth}</span><output>{motion.depth.toFixed(2)}</output></span>
              <input
                type="range"
                min="0"
                max="1"
                step="0.01"
                value={motion.depth}
                oninput={(e) => {
                  byHand();
                  motion.depth = +e.currentTarget.value;
                }}
                data-input="depth"
              />
              {#if scenario?.inputs?.depth?.hint}<span class="muted small">{scenario.inputs.depth.hint}</span>{/if}
            </label>
          {/if}
          {#if view.inputs.oil}
            <label class="slider">
              <span class="row"><span>{scenario?.inputs?.oil?.label ?? s.oil}</span><output>{(motion.oil ?? 0).toFixed(2)}</output></span>
              <input
                type="range"
                min="0"
                max="1"
                step="0.01"
                value={motion.oil ?? 0}
                oninput={(e) => {
                  motion.oil = +e.currentTarget.value;
                }}
                data-input="oil"
              />
              {#if scenario?.inputs?.oil?.hint}<span class="muted small">{scenario.inputs.oil.hint}</span>{/if}
            </label>
          {/if}
          {#if view.inputs.lifting}
            <label class="check">
              <input
                type="checkbox"
                checked={motion.lifting > 0.5}
                onchange={(e) => {
                  byHand();
                  motion.lifting = e.currentTarget.checked ? 1 : 0;
                }}
                data-input="lifting"
              />
              {scenario?.inputs?.lifting?.label ?? s.lifting}
            </label>
          {/if}
          {#if view.inputs.theta || view.inputs.travel || view.inputs.feed}
            <label class="check"><input type="checkbox" bind:checked={reverse} data-input="reverse" /> {scenario?.inputs?.reverse?.label ?? s.reverse}</label>
            <label class="slider">
              <span class="row"><span>{s.inputSpeed}</span><output data-testid="model-speed">{s.rps(inputSpeed)}{#if vehicle}<span class="muted">, {s.blocksPerSecond(blocksPerSecondAt(inputSpeed, vehicle))}</span>{/if}</output></span>
              <input type="range" min="0" max={INPUT_SPEED_MAX} step="0.05" bind:value={inputSpeed} data-input="speed" />
            </label>
            <div class="row play-row">
              <button type="button" class="play" aria-pressed={playing} onclick={togglePlay}>{playing ? s.pause : s.play}</button>
              <span class="muted small" role="status" data-testid="model-status">{status}</span>
            </div>
            <span class="muted small">{play?.phases.length ? s.playHintScript : gearedTurns !== null ? s.playHintGeared : vehicle ? s.playHintRoll : s.playHintTurn}</span>

          {/if}
        </fieldset>
      {/if}

      {#if view.requires.length > 0}
        <fieldset>
          <legend>{s.fitted}</legend>
          {#if statesSpec}
            <label class="stack">
              <span>{statesSpec.label}</span>
              <select value={currentState ?? ""} onchange={(e) => pickState(e.currentTarget.value)} data-input="state">
                {#if currentState === null}<option value="" disabled>{s.stateByHand}</option>{/if}
                {#each statesSpec.options as o (o.id)}<option value={o.id}>{o.label}</option>{/each}
              </select>
              {#if currentStateHint}<span class="muted small" data-testid="model-state-hint">{currentStateHint}</span>{/if}
            </label>
          {/if}
          {#each scenario?.choices ?? [] as c (c.label)}
            <label class="stack">
              <span>{c.label}</span>
              <select
                value={c.values.find((v) => fittedState[v]) ?? ""}
                onchange={(e) => (fittedState = pickChoice(fittedState, c, e.currentTarget.value))}
                data-choice={c.label}
              >
                {#each c.values as v (v)}<option value={v}>{view.requires.find((r) => r.value === v)?.label ?? v}</option>{/each}
              </select>
            </label>
          {/each}
          {#if freeRequires.length > 0}
            <div class="checks">
              {#each freeRequires as r (r.value)}
                <label class="check"><input type="checkbox" bind:checked={fittedState[r.value]} data-requires={r.value} /> {r.label}</label>
              {/each}
            </div>
          {/if}
        </fieldset>
      {/if}

      {#if propSpec && propLine}
        <fieldset>
          <legend>{s.prop}</legend>
          <label class="stack">
            <span>{propSpec.label}</span>
            <select
              value={propChoice}
              onchange={(e) => {
                propChoice = e.currentTarget.value;
                choose();
              }}
              data-input="prop"
            >
              <option value="none">{s.propNone}</option>
              {#each propSpec.options as o (o.id)}<option value={o.id}>{o.label}</option>{/each}
            </select>
          </label>
        </fieldset>
      {/if}

      <fieldset>
        <legend>{s.overlays}</legend>
        <div class="checks">
          <label class="check"><input type="checkbox" bind:checked={overlays.cells} data-overlay="cells" /> {s.overlayCells}</label>
          {#if view.cells.length > 0}
            <label class="check"><input type="checkbox" bind:checked={overlays.collision} data-overlay="collision" /> {s.overlayCollision}</label>
          {/if}
          <label class="check"><input type="checkbox" bind:checked={edges} data-overlay="edges" /> {s.overlayEdges}</label>
          {#if track}
            <label class="check"><input type="checkbox" bind:checked={overlays[TRACK_OVERLAY]} data-overlay="track" /> {s.overlayTrack}</label>
          {/if}
          {#each view.anchors as a (a.key)}
            <label class="check"><input type="checkbox" bind:checked={overlays[a.key]} data-overlay={a.key} /> {a.label}</label>
          {/each}
        </div>
        {#if track}<p class="muted small">{s.trackNote}</p>{/if}
        {#if view.unrecognised.length > 0}<p class="muted small">{s.unrecognised(view.unrecognised.join(", "))}</p>{/if}
      </fieldset>

      <fieldset>
        <legend>{s.colour}</legend>
        <div class="seg" role="group" aria-label={s.colour}>
          {#if view.hasRig}
            <button type="button" aria-pressed={colourMode === "part"} onclick={() => (colourMode = "part")}>{s.colourByPart}</button>
          {/if}
          <button type="button" aria-pressed={colourMode === "texture"} onclick={() => (colourMode = "texture")}>{s.colourByTexture}</button>
        </div>
        <ul class="legend" data-testid="model-legend" aria-label={colourMode === "part" ? s.parts : s.textures}>
          {#each groups as g (g.key)}
            <li class:off={!g.on}>
              <details bind:open={openGroups[g.key]}>
                <summary>
                  <i class="swatch" style:background={g.colour}></i>
                  <span class="code gname">{g.name}</span>
                  <span class="muted note">{g.note}</span>
                  <span class="count">{g.elements.length}</span>
                </summary>
                {#if openGroups[g.key]}
                  <ul class="elements" aria-label={s.elementsOf(g.elements.length)}>
                    {#each g.elements as ei (ei)}
                      <li>
                        <button type="button" class="code" aria-pressed={picked === ei} data-element={view.flat[ei]!.element.name} onclick={() => (picked = ei)}>
                          {view.flat[ei]!.element.name}
                        </button>
                      </li>
                    {/each}
                  </ul>
                {/if}
              </details>
            </li>
          {/each}
        </ul>
      </fieldset>

      <p class="muted small">
        {s.source}:
        <a href={sourceUrl(model.source.shape)} rel="noopener noreferrer" target="_blank">{s.shapeFile}</a>{#if model.source.rig}, <a href={sourceUrl(model.source.rig)} rel="noopener noreferrer" target="_blank">{s.rigFile}</a>{/if}{#if model.source.bogie}, <a href={sourceUrl(model.source.bogie)} rel="noopener noreferrer" target="_blank">{s.bogieFile}</a>{/if}
      </p>
    </aside>
  </div>
{/if}

<style>
  .crumbs {
    font-size: 0.9rem;
    color: var(--muted);
  }
  h1 {
    margin: 0.3rem 0 0.5rem;
  }
  /* Manifest text holds file paths, which have nowhere to break on a phone. */
  .description,
  .credit {
    overflow-wrap: anywhere;
  }
  .credit {
    padding: 0.5rem 0.75rem;
    background: var(--notice-bg);
    border: 1px solid var(--notice-border);
    border-radius: var(--radius);
    font-size: 0.9rem;
  }
  .probe {
    display: none;
  }
  .probe [data-token="stage"] {
    color: var(--model-stage);
  }
  .probe [data-token="edge"] {
    color: var(--model-edge);
  }
  .probe [data-token="grid"] {
    color: var(--model-grid);
  }
  .probe [data-token="highlight"] {
    color: var(--model-highlight);
  }
  .bench {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 20rem;
    gap: 1rem;
    align-items: start;
  }
  .stage-col {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 0.6rem;
  }
  .stage {
    position: relative;
    height: min(72vh, 44rem);
    min-height: 300px;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
    background: var(--model-stage);
  }
  canvas {
    display: block;
    width: 100%;
    height: 100%;
    touch-action: none;
  }
  .views {
    position: absolute;
    top: 0.5rem;
    left: 0.5rem;
    right: 0.5rem;
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem;
  }
  .views button {
    background: var(--surface);
    font-size: 0.85rem;
  }
  .fallback {
    position: absolute;
    inset: 0;
    margin: 0;
    padding: 1.5rem;
    display: grid;
    place-items: center;
    text-align: center;
  }
  .labels {
    position: absolute;
    inset: 0;
    pointer-events: none;
    overflow: hidden;
  }
  .labels :global(span) {
    position: absolute;
    transform: translate(-50%, -130%);
    white-space: nowrap;
    font-size: 0.72rem;
    line-height: 1;
    background: var(--surface);
    color: var(--text);
    border: 1px solid var(--border);
    border-radius: 3px;
    padding: 0.2rem 0.35rem;
  }
  .tip {
    position: absolute;
    pointer-events: none;
    transform: translate(12px, 12px);
    max-width: 22rem;
    font-family: ui-monospace, "SFMono-Regular", Menlo, Consolas, monospace;
    font-size: 0.75rem;
    overflow-wrap: anywhere;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 3px;
    padding: 0.15rem 0.4rem;
  }
  .pick {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.5rem 0.75rem;
    font-size: 0.9rem;
    min-height: 3rem;
  }
  .pick-head {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem 0.75rem;
  }
  .name {
    font-weight: 600;
  }
  .btns {
    display: inline-flex;
    flex-wrap: wrap;
    gap: 0.3rem;
    margin-left: auto;
  }
  .btns button {
    font-size: 0.85rem;
  }
  dl {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 0.1rem 0.75rem;
    margin: 0.5rem 0 0;
  }
  dt {
    color: var(--muted);
  }
  dd {
    margin: 0;
    min-width: 0;
  }
  .controls {
    min-width: 0;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.75rem;
    display: flex;
    flex-direction: column;
    gap: 1rem;
  }
  fieldset {
    border: 0;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 0.45rem;
    min-width: 0;
  }
  legend {
    font-size: 0.8rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
    padding: 0;
    margin-bottom: 0.3rem;
  }
  .row {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 0.5rem;
  }
  .slider {
    display: flex;
    flex-direction: column;
    gap: 0.15rem;
    font-size: 0.9rem;
  }
  .slider output {
    font-variant-numeric: tabular-nums;
  }
  input[type="range"] {
    width: 100%;
    accent-color: var(--accent);
  }
  input[type="checkbox"] {
    accent-color: var(--accent);
    margin: 0;
  }
  .check {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    font-size: 0.9rem;
    cursor: pointer;
  }
  .checks {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(8.5rem, 1fr));
    gap: 0.3rem 0.75rem;
  }
  .stack {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    font-size: 0.9rem;
  }
  select {
    padding: 0.25rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  .play-row {
    justify-content: flex-start;
  }
  .play {
    background: var(--accent);
    color: var(--accent-text);
    border-color: var(--accent);
    min-width: 5rem;
  }
  .seg {
    display: inline-flex;
    align-self: flex-start;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
  }
  .seg button {
    border: 0;
    border-radius: 0;
    background: transparent;
    font-size: 0.85rem;
    padding: 0.25rem 0.7rem;
  }
  .seg button[aria-pressed="true"] {
    background: var(--accent);
    color: var(--accent-text);
  }
  .legend {
    list-style: none;
    margin: 0;
    padding: 0;
    font-size: 0.85rem;
  }
  .legend > li {
    border-bottom: 1px solid var(--border);
  }
  .legend > li:last-child {
    border-bottom: 0;
  }
  .legend > li.off summary {
    opacity: 0.5;
  }
  summary {
    display: grid;
    grid-template-columns: auto auto minmax(0, 1fr) auto;
    align-items: center;
    gap: 0.5rem;
    padding: 0.2rem 0;
    cursor: pointer;
    list-style: none;
  }
  summary::-webkit-details-marker {
    display: none;
  }
  .swatch {
    display: block;
    width: 0.75rem;
    height: 0.75rem;
    border-radius: 2px;
    border: 1px solid var(--border);
  }
  .note {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .count {
    font-variant-numeric: tabular-nums;
  }
  .elements {
    list-style: none;
    margin: 0 0 0.4rem;
    padding: 0 0 0 1.25rem;
    max-height: 14rem;
    overflow-y: auto;
  }
  .elements button {
    background: none;
    border: 0;
    padding: 0.05rem 0.2rem;
    text-align: left;
    color: var(--link);
    font-size: 0.8rem;
  }
  .elements button[aria-pressed="true"] {
    background: var(--surface-2);
    color: var(--text);
  }
  .small {
    font-size: 0.85rem;
  }
  @media (max-width: 860px) {
    .bench {
      grid-template-columns: minmax(0, 1fr);
    }
    .stage {
      height: 60vh;
    }
  }
</style>
