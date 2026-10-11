<script lang="ts">
  // The eidolon's fitting sheet: #/<version>/eidolon. The fittings, stats and bill are the version's
  // export (eidolon.json, eidolon-fittings.ts); the model is the model viewer's eidolon (its shape,
  // rig and animations, as main has them), drawn by the viewer's scene in the game's textures the
  // export carries. App.svelte loads this component lazily, and it loads three.js after it.
  import { onDestroy } from "svelte";
  import type { VersionData } from "../lib/data.ts";
  import {
    bill,
    change,
    defaultPick,
    isAgedWood,
    layerEffect,
    layerHasStats,
    material,
    materialLabel,
    round,
    sashParts,
    statsFor,
    textureImages,
    type EidolonData,
    type EidolonLayer,
    type FittingPick,
  } from "../lib/eidolon-fittings.ts";
  import { et } from "../lib/eidolon-strings.ts";
  import { advanceFrame, animationOptions, animationSpeed, jointDeltas, loopsByDefault } from "../lib/keyframes.ts";
  import { loadModelFiles, loadModelIndex } from "../lib/model-data.ts";
  import type { PublishedModel } from "../lib/model-manifest.ts";
  import { buildModelView, textureColour, type ModelView } from "../lib/model-view.ts";
  import { formatRoute } from "../lib/route.ts";
  import { identity } from "../lib/rig.ts";
  import { t } from "../lib/strings.ts";
  import type { ModelScene, SceneColours } from "../viewer/model-scene.ts";
  import { webglAvailable } from "../viewer/webgl.ts";
  import ItemLink from "./ItemLink.svelte";

  /** The model viewer's eidolon (site/models.json). */
  const MODEL_ID = "eidolon";

  let { data }: { data: VersionData } = $props();

  // ---- data
  let fittings = $state<EidolonData | null | undefined>(undefined);
  let failed = $state(false);
  let model = $state<PublishedModel | null>(null);
  let view = $state<ModelView | null>(null);
  let modelFailed = $state(false);
  let pick = $state<FittingPick>({});

  $effect(() => {
    data.eidolon().then(
      (d) => {
        fittings = d;
        if (d) pick = defaultPick(d);
      },
      () => (failed = true),
    );
    // The fitting sheet's own data first: the stats and the bill do not wait for the model.
    data.ready().catch(() => {});
  });

  $effect(() => {
    loadModelIndex()
      .then(async (index) => {
        const m = index.models.find((x) => x.id === MODEL_ID);
        if (!m) throw new Error(`no model ${MODEL_ID}`);
        const files = await loadModelFiles(m);
        model = m;
        view = buildModelView(files.shape, files.rig, m.scenario);
        selectAnimation("");
      })
      .catch(() => (modelFailed = true));
  });

  $effect(() => {
    document.title = `${et.heading} · ${t.siteTitle}`;
  });

  const stats = $derived(fittings ? statsFor(fittings, pick) : null);
  const baseline = $derived(fittings ? statsFor(fittings, defaultPick(fittings)) : null);
  const images = $derived(fittings ? textureImages(fittings, pick) : new Map<string, string>());
  const rows = $derived(fittings ? bill(fittings, pick, { torso: et.torso, mind: et.mind }) : []);
  const pieces = $derived(new Map((view?.textures ?? []).map((tx) => [tx.code, tx.elements.length])));
  const textured = $derived(images.size > 0);

  function choose(layer: string, id: string) {
    pick = { ...pick, [layer]: id };
  }

  function effectOf(l: EidolonLayer): string {
    if (!fittings || !layerHasStats(l.id)) return et.looksOnly;
    const e = layerEffect(fittings, l.id, pick[l.id] ?? "");
    return e.length > 0 ? e.join(" · ") : et.baseline;
  }

  /** A swatch's image: the material's texture, or a colour square when the export has none. */
  function swatch(layer: string, id: string): { src: string } | { colour: string } {
    const path = fittings ? material(fittings, layer, id)?.texture : null;
    const uri = path ? fittings?.textures[path] : undefined;
    return uri ? { src: uri } : { colour: textureColour(layer, path ?? id) };
  }

  /** Sash: the kind picked (leather or cloth), and its colours. */
  const sashKinds = $derived.by(() => {
    const l = fittings?.layers.find((x) => x.id === "sash");
    const kinds: string[] = [];
    for (const m of l?.materials ?? []) {
      const k = sashParts(m.id).kind;
      if (!kinds.includes(k)) kinds.push(k);
    }
    return kinds;
  });
  const sashKind = $derived(sashParts(pick.sash ?? "").kind);
  function chooseSashKind(kind: string) {
    const l = fittings?.layers.find((x) => x.id === "sash");
    if (!l) return;
    const colour = sashParts(pick.sash ?? "").colour;
    const same = l.materials.find((m) => m.id === `${kind}-${colour}`);
    const first = l.materials.find((m) => sashParts(m.id).kind === kind);
    const next = same ?? first;
    if (next) choose("sash", next.id);
  }

  function delta(value: number, against: number | undefined): { text: string; cls: string } {
    if (against === undefined) return { text: "", cls: "" };
    const r = change(value, against);
    if (r === 0) return { text: et.same, cls: "" };
    return { text: et.versus(Math.abs(r * 100).toFixed(0), r > 0), cls: r > 0 ? "up" : "down" };
  }
  const num = (v: number, places: number) => round(v, places);

  // ---- animations (keyframes.ts), as the model page plays them
  let animCode = $state("");
  let animFrame = $state(0);
  let animPlaying = $state(false);
  let animSpeed = $state(1);
  let animLoop = $state(true);
  const animations = $derived(view?.animation.animations ?? []);
  const currentAnim = $derived(animations.find((a) => a.code === animCode) ?? null);
  const animGroups = $derived(animationOptions(animations, model?.scenario?.animations));
  const jointMatrices = $derived(view && currentAnim ? jointDeltas(view.animation, view.flat, currentAnim, animFrame) : null);
  // The eidolon's rig is its build stages, parts that never move: the whole body, at rest.
  const matrices = $derived(view ? view.parts.map(() => identity()) : []);
  const visible = $derived(view ? view.parts.map(() => true) : []);

  function selectAnimation(code: string) {
    const a = animations.find((x) => x.code === code) ?? null;
    animCode = a ? a.code : "";
    animFrame = 0;
    if (!a) return stopAnimation();
    animLoop = loopsByDefault(a);
    animSpeed = animationSpeed(model?.scenario?.animations, a.code);
    startAnimation();
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
  function startAnimation() {
    if (!currentAnim) return;
    if (!animLoop && animFrame >= currentAnim.frames - 1) animFrame = 0;
    cancelAnimationFrame(animRaf);
    animPlaying = true;
    animLast = null;
    animRaf = requestAnimationFrame(animTick);
  }
  function stopAnimation() {
    animPlaying = false;
    cancelAnimationFrame(animRaf);
    animLast = null;
  }
  onDestroy(() => cancelAnimationFrame(animRaf));

  // ---- the 3D scene (src/viewer/model-scene.ts), textured
  let canvas = $state<HTMLCanvasElement>();
  let labelLayer = $state<HTMLElement>();
  let probe = $state<HTMLElement>();
  let scene = $state<ModelScene | null>(null);
  let sceneState = $state<"loading" | "ready" | "nowebgl" | "failed">("loading");
  const reducedMotion = typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
  let turning = $state(!reducedMotion);
  let lit = $state<string | null>(null);

  function readColours(): SceneColours {
    const el = probe?.querySelector<HTMLElement>('[data-token="stage"]');
    const background = el ? getComputedStyle(el).color || "#e4e1d8" : "#e4e1d8";
    return { background, edge: "#000000", grid: "#888888", highlight: "#d9480f" };
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
          made = new mod.ModelScene(c, layer, v, readColours());
          made.setEdges(false);
          made.colourBy("texture");
          made.setView("front");
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
  $effect(() => scene?.setTextures(images));
  $effect(() => scene?.dim(lit));
  $effect(() => scene?.setAutoRotate(turning));
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
</script>

<div class="probe" bind:this={probe} aria-hidden="true"><span data-token="stage"></span></div>

{#if failed}
  <p role="alert">{et.loadFailed}</p>
{:else if fittings === undefined}
  <p class="muted">{t.loading}</p>
{:else if fittings === null}
  <h1>{et.heading}</h1>
  <p>{et.none}</p>
{:else}
  <div class="sheet" data-testid="eidolon-fittings">
    <header class="head">
      <div class="eyebrow">{et.eyebrow}</div>
      <h1>{et.heading}</h1>
      <p class="lede">{et.lede}</p>
      <p class="small"><a href={formatRoute({ view: "model", id: MODEL_ID })}>{et.modelPage}</a>{#if fittings.item}{" · "}<ItemLink code={fittings.item} {data} />{/if}</p>
    </header>

    <div class="main">
      <div class="stagebox">
        <div class="stage" data-testid="eidolon-stage" data-scene={modelFailed ? "failed" : sceneState}>
          {#if modelFailed}
            <p class="fallback" role="alert">{et.modelFailed}</p>
          {:else}
            <canvas bind:this={canvas} aria-label={et.stageLabel} data-testid="eidolon-canvas"></canvas>
            <div class="labels" bind:this={labelLayer}></div>
            {#if sceneState === "loading"}
              <p class="fallback muted">{et.loadingViewer}</p>
            {:else if sceneState === "nowebgl"}
              <p class="fallback" role="status" data-testid="eidolon-webgl-missing">{et.noWebgl}</p>
            {:else if sceneState === "failed"}
              <p class="fallback" role="alert">{et.viewerFailed}</p>
            {/if}
          {/if}
        </div>
        <div class="viewbar">
          <span>{textured ? et.viewHint : et.viewHintColours}</span>
          <button type="button" aria-pressed={turning} onclick={() => (turning = !turning)} data-input="turning">{turning ? et.turning : et.still}</button>
        </div>
        {#if animations.length > 0}
          <div class="viewbar anim" data-testid="eidolon-animation">
            <label>
              <span>{et.animation}</span>
              <select value={animCode} onchange={(e) => selectAnimation(e.currentTarget.value)} data-input="animation">
                <option value="">{et.animationNone}</option>
                {#each animGroups as g, gi (gi)}
                  <optgroup label={g.label ?? et.animationOther}>
                    {#each g.options as o (o.code)}<option value={o.code}>{o.label}</option>{/each}
                  </optgroup>
                {/each}
              </select>
            </label>
            {#if currentAnim}
              <button type="button" aria-pressed={animPlaying} onclick={() => (animPlaying ? stopAnimation() : startAnimation())} data-input="anim-play">{animPlaying ? et.pause : et.play}</button>
            {/if}
          </div>
        {/if}
      </div>

      <div class="panel">
        <section class="stats" aria-live="polite" data-testid="eidolon-stats">
          <h2>{et.stats}</h2>
          {#if stats}
            {@const b = baseline}
            <div class="statgrid">
              {#each [
                { k: "hp", label: et.statNames.hp, v: num(stats.maxHealth, 0), d: delta(stats.maxHealth, b?.maxHealth) },
                { k: "walk", label: et.statNames.walk, v: stats.walkSpeed.toFixed(4), d: delta(stats.walkSpeed, b?.walkSpeed) },
                { k: "run", label: et.statNames.run, v: stats.runSpeed.toFixed(4), d: delta(stats.runSpeed, b?.runSpeed) },
                { k: "punch", label: et.statNames.punch, v: num(stats.defenceDamage, 1), d: delta(stats.defenceDamage, b?.defenceDamage) },
                { k: "slam", label: et.statNames.slam, v: num(stats.slamDamage, 1), d: delta(stats.slamDamage, b?.slamDamage) },
              ] as s (s.k)}
                <div class="stat" data-stat={s.k}>
                  <span class="k">{s.label}</span>
                  <span class="v">{s.v}</span>
                  <span class="d {s.d.cls}">{s.d.text}</span>
                </div>
              {/each}
              <div class="stat" data-stat="armour">
                <span class="k">{et.statNames.armour}</span>
                {#if stats.armour}
                  <span class="v">{et.armourTier(stats.armour.protectionTier)}</span>
                  <span class="d">{et.armourDetail(num(stats.armour.relativeProtection * 100, 1), num(stats.armour.flatDamageReduction, 2))}</span>
                {:else}
                  <span class="v">{et.noArmour}</span>
                {/if}
              </div>
            </div>
          {:else}
            <p class="note">{et.noStats}</p>
          {/if}
          <p class="note">{et.statNote(num(fittings.settings.baseHealth, 1), num(fittings.settings.allSteelHealth, 0))}</p>
        </section>

        {#each fittings.layers as l (l.id)}
          {@const picked = pick[l.id]}
          <section
            class="fit"
            data-layer={l.id}
            role="group"
            aria-label={l.name}
            onmouseenter={() => (lit = l.id)}
            onmouseleave={() => (lit = null)}
            onfocusin={() => (lit = l.id)}
            onfocusout={() => (lit = null)}
          >
            <div class="fit-head">
              <h2>{l.name}</h2>
              <span class="effect" data-testid="effect">{effectOf(l)}</span>
            </div>
            <div class="parts muted">{et.parts[l.id] ?? ""}{#if pieces.get(l.id)}{" · "}{et.pieces(pieces.get(l.id)!)}{/if}</div>
            {#if l.id === "sash"}
              <div class="seg" role="group" aria-label={l.name}>
                {#each sashKinds as k (k)}
                  <button type="button" aria-pressed={sashKind === k} onclick={() => chooseSashKind(k)}>{et.sashKinds[k] ?? k}</button>
                {/each}
              </div>
              <div class="swatches">
                {#each l.materials.filter((m) => sashParts(m.id).kind === sashKind) as m (m.id)}
                  {@render sw(l.id, m.id)}
                {/each}
              </div>
            {:else if l.id === "bones"}
              <div class="swatches">
                {#each l.materials.filter((m) => !isAgedWood(m.id)) as m (m.id)}
                  {@render sw(l.id, m.id)}
                {/each}
              </div>
              {#if l.materials.some((m) => isAgedWood(m.id))}
                <div class="subhead">{et.salvaged}</div>
                <div class="swatches">
                  {#each l.materials.filter((m) => isAgedWood(m.id)) as m (m.id)}
                    {@render sw(l.id, m.id)}
                  {/each}
                </div>
              {/if}
            {:else}
              <div class="seg" role="group" aria-label={l.name}>
                {#each l.materials as m (m.id)}
                  <button type="button" aria-pressed={picked === m.id} onclick={() => choose(l.id, m.id)}>{materialLabel(l.id, m.id)}</button>
                {/each}
              </div>
              <div class="swatches">
                {#each l.materials as m (m.id)}
                  {@render sw(l.id, m.id)}
                {/each}
              </div>
              {#if l.id === "skirt" && picked === "stainlesssteel"}
                <p class="note">{et.stainlessChain}</p>
              {/if}
            {/if}
          </section>
        {/each}
      </div>
    </div>

    <section class="bill" data-testid="eidolon-bill">
      <h2>{et.bill}</h2>
      <p class="note">{et.billNote}</p>
      <div class="billwrap">
        <table>
          <thead><tr><th scope="col">{et.stage}</th><th scope="col">{et.ingredients}</th></tr></thead>
          <tbody>
            {#each rows as r (r.id)}
              <tr data-stage={r.id}>
                <th scope="row">{r.name}</th>
                <td>
                  <ul class="items">
                    {#each r.items as it, i (i)}
                      <li class="it" class:chg={it.chosen} data-code={it.stack.code}><span class="count">{it.stack.count}×</span> <ItemLink code={it.stack.code} {data} /></li>
                    {/each}
                  </ul>
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    </section>
  </div>
{/if}

{#snippet sw(layer: string, id: string)}
  {@const img = swatch(layer, id)}
  {@const label = materialLabel(layer, id)}
  <button type="button" class="sw" aria-pressed={pick[layer] === id} onclick={() => choose(layer, id)} data-material={id} title={label}>
    {#if "src" in img}
      <img alt="" src={img.src} width="48" height="48" />
    {:else}
      <span class="chip" style:background={img.colour}></span>
    {/if}
    <span class="name">{label}</span>
  </button>
{/snippet}

<style>
  .probe {
    display: none;
  }
  .probe [data-token="stage"] {
    color: var(--model-stage);
  }
  .sheet {
    display: grid;
    gap: 1.6rem;
  }
  .head {
    display: grid;
    gap: 0.35rem;
  }
  .eyebrow {
    font-family: ui-monospace, monospace;
    font-size: 0.75rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--accent);
  }
  h1 {
    margin: 0;
    line-height: 1.05;
    text-wrap: balance;
  }
  .lede {
    color: var(--muted);
    max-width: 68ch;
    margin: 0;
  }
  .small {
    font-size: 0.9rem;
    margin: 0;
  }
  .main {
    display: grid;
    grid-template-columns: minmax(0, 5fr) minmax(0, 6fr);
    gap: 1.6rem;
    align-items: start;
  }
  .stagebox {
    position: sticky;
    top: 0.75rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
    background: var(--model-stage);
  }
  .stage {
    position: relative;
    aspect-ratio: 4 / 5;
    max-height: 72vh;
    width: 100%;
  }
  .stage canvas {
    display: block;
    width: 100%;
    height: 100%;
    cursor: grab;
    touch-action: none;
  }
  .labels {
    position: absolute;
    inset: 0;
    pointer-events: none;
  }
  .fallback {
    position: absolute;
    inset: auto 1rem 1rem;
    margin: 0;
    padding: 0.5rem 0.7rem;
    background: var(--surface);
    border-radius: var(--radius);
  }
  .viewbar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem 1rem;
    padding: 0.55rem 0.8rem;
    border-top: 1px solid var(--border);
    font-size: 0.82rem;
    color: var(--muted);
    background: var(--surface);
  }
  .viewbar.anim label {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    flex: 1 1 14rem;
    min-width: 0;
  }
  .viewbar select {
    flex: 1;
    min-width: 0;
    padding: 0.2rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
    color: var(--text);
  }
  .viewbar button,
  .seg button {
    font: inherit;
    color: var(--text);
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 4px;
    padding: 0.15rem 0.7rem;
    cursor: pointer;
  }
  .panel {
    display: grid;
    gap: 1.3rem;
    min-width: 0;
  }
  .stats {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 1rem 1.1rem;
    display: grid;
    gap: 0.75rem;
  }
  h2 {
    margin: 0;
    font-size: 1.25rem;
  }
  .statgrid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(7.4rem, 1fr));
    gap: 0.6rem 1.1rem;
  }
  .stat {
    display: grid;
    gap: 0.1rem;
    align-content: start;
  }
  .stat .k {
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.07em;
    color: var(--muted);
  }
  .stat .v {
    font-family: ui-monospace, monospace;
    font-size: 1.55rem;
    font-variant-numeric: tabular-nums;
    line-height: 1.1;
  }
  .stat .d {
    font-family: ui-monospace, monospace;
    font-size: 0.75rem;
    color: var(--muted);
  }
  .stat .d.up {
    color: light-dark(#2f7d4f, #6cc391);
  }
  .stat .d.down {
    color: light-dark(#b2452f, #ec8a72);
  }
  .note {
    font-size: 0.8rem;
    color: var(--muted);
    margin: 0;
  }
  .fit {
    display: grid;
    gap: 0.6rem;
    padding-bottom: 1.1rem;
    border-bottom: 1px solid var(--border);
  }
  .fit:last-child {
    border-bottom: 0;
  }
  .fit-head {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    justify-content: space-between;
    gap: 0.25rem 0.75rem;
  }
  .parts {
    font-size: 0.8rem;
  }
  .effect {
    font-family: ui-monospace, monospace;
    font-size: 0.75rem;
    color: var(--accent);
  }
  .seg {
    display: inline-flex;
    flex-wrap: wrap;
    width: fit-content;
    max-width: 100%;
  }
  .seg button {
    border-radius: 0;
    padding: 0.3rem 0.9rem;
  }
  .seg button + button {
    border-left: 0;
  }
  .seg button:first-child {
    border-radius: 5px 0 0 5px;
  }
  .seg button:last-child {
    border-radius: 0 5px 5px 0;
  }
  .seg button[aria-pressed="true"],
  .viewbar button[aria-pressed="true"] {
    background: var(--surface-2);
    font-weight: 600;
    box-shadow: inset 0 -2px 0 var(--accent);
  }
  .swatches {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }
  .sw {
    display: grid;
    justify-items: center;
    gap: 0.25rem;
    background: none;
    border: 0;
    padding: 0;
    cursor: pointer;
    color: var(--text);
    font: inherit;
    width: 3.9rem;
  }
  .sw img,
  .sw .chip {
    display: block;
    width: 48px;
    height: 48px;
    image-rendering: pixelated;
    border-radius: 4px;
    border: 2px solid transparent;
    box-shadow: 0 0 0 1px var(--border);
  }
  .sw .name {
    font-size: 0.72rem;
    line-height: 1.2;
    text-align: center;
    color: var(--muted);
  }
  .sw[aria-pressed="true"] img,
  .sw[aria-pressed="true"] .chip {
    border-color: var(--accent);
    box-shadow: 0 0 0 1px var(--accent);
  }
  .sw[aria-pressed="true"] .name {
    color: var(--text);
    font-weight: 600;
  }
  .sw:focus-visible,
  .seg button:focus-visible,
  .viewbar button:focus-visible {
    outline: 2px solid var(--focus);
    outline-offset: 2px;
  }
  .subhead {
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.07em;
    color: var(--muted);
  }
  .bill {
    display: grid;
    gap: 0.75rem;
  }
  .billwrap {
    overflow-x: auto;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--surface);
  }
  table {
    border-collapse: collapse;
    width: 100%;
    min-width: 36rem;
    font-size: 0.88rem;
  }
  th,
  td {
    text-align: left;
    padding: 0.5rem 0.75rem;
    border-bottom: 1px solid var(--border);
    vertical-align: top;
  }
  tbody th {
    white-space: nowrap;
  }
  tr:last-child th,
  tr:last-child td {
    border-bottom: 0;
  }
  .items {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-wrap: wrap;
    gap: 0.3rem 0.4rem;
  }
  .it {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    padding: 0.1rem 0.45rem;
    border-radius: 4px;
    background: var(--bg);
  }
  .it.chg {
    background: var(--surface-2);
    box-shadow: inset 0 0 0 1px var(--accent);
  }
  .count {
    font-family: ui-monospace, monospace;
    font-size: 0.8rem;
  }
  @media (max-width: 860px) {
    .main {
      grid-template-columns: 1fr;
    }
    .stagebox {
      position: static;
    }
  }
</style>
