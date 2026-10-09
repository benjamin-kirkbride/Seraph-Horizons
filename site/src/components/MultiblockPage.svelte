<script lang="ts">
  // One multiblock: the 3D view (multiblock-scene.ts), its slice and size controls, and the
  // materials it takes, which are also the legend. docs/recipe-browser/multiblocks.md.
  import type { VersionData } from "../lib/data.ts";
  import type { Meta, MultiblockEntry, MultiblockFile } from "../lib/format.ts";
  import { mbt } from "../lib/multiblock-strings.ts";
  import {
    buildMultiblockView,
    fullSlice,
    shownCells,
    shownCounts,
    type MultiblockView,
    type Slice,
    type ViewPart,
  } from "../lib/multiblock-view.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import type { MultiblockScene, SceneColours, ViewName } from "../viewer/multiblock-scene.ts";
  import { webglAvailable } from "../viewer/webgl.ts";
  import ModLink from "./ModLink.svelte";

  let { data, meta, entry }: { data: VersionData; meta: Meta; entry: MultiblockEntry } = $props();

  let file = $state<MultiblockFile | null>(null);
  let failed = $state(false);
  let indexReady = $state(false);
  let size = $state(0);

  $effect(() => {
    data.multiblock(entry.file).then(
      (f) => {
        file = f;
        size = f.structure.defaultSize ?? 0;
      },
      () => (failed = true),
    );
    data.ready().then(
      () => (indexReady = true),
      () => {},
    );
  });

  const view = $derived<MultiblockView | null>(file ? buildMultiblockView(file.structure, file.shapes, size) : null);
  const structure = $derived(file?.structure ?? null);

  // ---- slice: one cut per axis; a new size starts uncut
  let slice = $state<Slice>([
    { at: 0, only: false },
    { at: 0, only: false },
    { at: 0, only: false },
  ]);
  $effect(() => {
    if (view) slice = fullSlice(view);
  });
  const shown = $derived(view ? shownCells(view, slice) : []);
  const counts = $derived(view ? shownCounts(view, shown) : []);
  const cut = $derived(view ? slice.some((c, k) => c.only || c.at < view.max[k]!) : false);

  let outlines = $state(true);
  let edges = $state(true);
  let highlighted = $state<number | null>(null);
  let picked = $state<{ part: number; pos: [number, number, number] } | null>(null);

  // Blocks first, most first; then what is left empty or fills itself.
  const legend = $derived(
    view
      ? [...view.parts]
          .filter((p) => p.count > 0)
          .sort((a, b) => (a.kind === "block" ? 0 : 1) - (b.kind === "block" ? 0 : 1) || b.count - a.count || a.label.localeCompare(b.label))
      : [],
  );

  function kindNote(p: ViewPart): string | null {
    if (p.kind === "air") return p.part.block !== undefined ? mbt.emptyOr(p.label) : mbt.mustBeEmpty;
    if (p.kind === "filler") return mbt.filler;
    return null;
  }

  function itemHref(code: string | undefined): string | null {
    if (!code || !indexReady || data.indexOf(code) < 0) return null;
    return formatRoute({ view: "item", version: data.id, code });
  }

  function toggle(part: number) {
    highlighted = highlighted === part ? null : part;
  }

  // ---- the 3D scene
  let canvas = $state<HTMLCanvasElement>();
  let probe = $state<HTMLElement>();
  let scene = $state<MultiblockScene | null>(null);
  let sceneState = $state<"loading" | "ready" | "nowebgl" | "failed">("loading");

  function readColours(): SceneColours {
    const get = (name: string, fallback: string) => {
      const el = probe?.querySelector<HTMLElement>(`[data-token="${name}"]`);
      return el ? getComputedStyle(el).color || fallback : fallback;
    };
    return { background: get("stage", "#e4e1d8"), edge: get("edge", "#2a2722"), grid: get("grid", "#8f897c") };
  }

  $effect(() => {
    const v = view;
    const c = canvas;
    if (!v || !c) return;
    if (!webglAvailable()) {
      sceneState = "nowebgl";
      return;
    }
    let gone = false;
    let made: MultiblockScene | null = null;
    import("../viewer/multiblock-scene.ts").then(
      (mod) => {
        if (gone) return;
        try {
          made = new mod.MultiblockScene(c, v, readColours());
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

  $effect(() => scene?.setShown(shown));
  $effect(() => scene?.setGhosts(outlines));
  $effect(() => scene?.setEdges(edges));
  $effect(() => scene?.highlight(highlighted));

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

  // A click (not the end of an orbit drag) picks the kind of block under the pointer.
  let down: { x: number; y: number } | null = null;
  function onPointerDown(e: PointerEvent) {
    down = { x: e.clientX, y: e.clientY };
  }
  function onPointerUp(e: PointerEvent) {
    if (!down || !scene) return;
    const moved = Math.hypot(e.clientX - down.x, e.clientY - down.y);
    down = null;
    if (moved > 5) return;
    const hit = scene.pick(e.clientX, e.clientY);
    picked = hit ? { part: hit.part, pos: [hit.pos[0], hit.pos[1], hit.pos[2]] } : null;
    highlighted = hit ? hit.part : null;
  }
  function onKey(e: KeyboardEvent) {
    if (e.key === "Escape") {
      highlighted = null;
      picked = null;
    }
  }

  const viewNames: ViewName[] = ["angled", "opposite", "front", "side", "top"];
  // Layer (y) first: it is the cut a builder wants most.
  const axisOrder = [1, 0, 2] as const;
</script>

<svelte:window onkeydown={onKey} />

<nav class="crumbs" aria-label="Breadcrumb"><a href={formatRoute({ view: "multiblocks", version: data.id })}>{mbt.heading}</a> › {entry.name}</nav>
<h1>{entry.name}</h1>

<!-- Colours for three.js, read from the theme's tokens (the model viewer's). -->
<div class="probe" aria-hidden="true" bind:this={probe}>
  <span data-token="stage"></span><span data-token="edge"></span><span data-token="grid"></span>
</div>

{#if failed}
  <p role="alert">{mbt.loadFailed}</p>
{:else if !view || !structure}
  <p class="muted">{t.loading}</p>
{:else}
  <p class="meta">
    {mbt.from} <ModLink id={structure.mod} mods={meta.mods} />.
    {mbt.checkedBy}
    {#if itemHref(structure.code)}<a href={itemHref(structure.code)}><code>{structure.code}</code></a>{:else}<code>{structure.code}</code>{/if}
    {#if structure.codes.length > 1}<span class="muted">{mbt.variants(structure.codes.length)}</span>{/if}.
  </p>

  <div class="bench">
    <div class="stage-col">
      <div class="stage" data-scene={sceneState} data-testid="multiblock-stage">
        <canvas bind:this={canvas} aria-label={mbt.stageLabel(entry.name)} data-testid="multiblock-canvas" onpointerdown={onPointerDown} onpointerup={onPointerUp}></canvas>
        {#if sceneState === "ready"}
          <div class="views" role="group" aria-label={mbt.views}>
            {#each viewNames as v (v)}
              <button type="button" onclick={() => scene?.setView(v)}>{mbt.viewNames[v]}</button>
            {/each}
          </div>
        {:else if sceneState === "nowebgl"}
          <p class="fallback" role="status" data-testid="multiblock-webgl-missing">{mbt.noWebgl}</p>
        {:else if sceneState === "failed"}
          <p class="fallback" role="alert">{mbt.viewerFailed}</p>
        {:else}
          <p class="fallback muted">{mbt.loadingViewer}</p>
        {/if}
      </div>
      <p class="pick muted" aria-live="polite" data-testid="multiblock-pick">
        {#if picked}
          {mbt.picked(view.parts[picked.part]!.label, picked.pos[0], picked.pos[1], picked.pos[2])}
        {:else}
          {mbt.frame}
        {/if}
      </p>
    </div>

    <aside class="controls" data-testid="multiblock-controls">
      {#if structure.sizes.length > 1}
        <label class="stack">
          <span>{structure.sizeLabel ?? mbt.size}</span>
          <select value={String(size)} onchange={(e) => (size = +e.currentTarget.value)} data-input="size">
            {#each structure.sizes as s, i (i)}<option value={String(i)}>{s.label ?? String(i + 1)}</option>{/each}
          </select>
        </label>
      {/if}

      <fieldset>
        <legend>{mbt.slice}</legend>
        {#each axisOrder as k (k)}
          {@const lo = view.min[k]!}
          {@const hi = view.max[k]!}
          {@const c = slice[k]!}
          <div class="slider">
            <label class="row" for="cut-{k}"
              ><span>{mbt.axes[k]}</span><output data-testid="multiblock-cut-{k}"
                >{c.only ? mbt.only(c.at - lo + 1, hi - lo + 1) : mbt.upTo(c.at - lo + 1, hi - lo + 1)}</output
              ></label
            >
            <input
              id="cut-{k}"
              type="range"
              min={lo}
              max={hi}
              step="1"
              value={c.at}
              disabled={hi === lo}
              oninput={(e) => (slice[k] = { ...c, at: +e.currentTarget.value })}
              data-input="cut-{k}"
            />
            <label class="check"
              ><input type="checkbox" checked={c.only} onchange={(e) => (slice[k] = { ...c, only: e.currentTarget.checked })} data-input="only-{k}" />
              {mbt.onlyThis}</label
            >
          </div>
        {/each}
        <button type="button" class="reset" disabled={!cut} onclick={() => (slice = fullSlice(view))}>{mbt.resetSlice}</button>
      </fieldset>

      <fieldset>
        <legend>{mbt.display}</legend>
        <label class="check"><input type="checkbox" bind:checked={outlines} data-input="outlines" /> {mbt.outlines}</label>
        <label class="check"><input type="checkbox" bind:checked={edges} data-input="edges" /> {mbt.edges}</label>
      </fieldset>
    </aside>
  </div>

  <section class="materials" aria-labelledby="materials-h">
    <h2 id="materials-h">{mbt.materials}</h2>
    <p class="muted small">{mbt.materialsHint}</p>
    <ul class="legend" data-testid="multiblock-legend">
      {#each legend as p (p.index)}
        {@const note = kindNote(p)}
        {@const href = itemHref(p.part.block)}
        <li class:on={highlighted === p.index} class:dim={highlighted !== null && highlighted !== p.index} data-part={p.index} data-kind={p.kind}>
          <button type="button" class="swatch-btn" aria-pressed={highlighted === p.index} onclick={() => toggle(p.index)} data-testid="multiblock-part">
            <span class="swatch" class:hollow={p.kind !== "block"} style:--swatch={p.colour}></span>
            <span class="count" data-testid="multiblock-count">{mbt.shownOf(counts[p.index] ?? 0, p.count)}×</span>
          </button>
          <div class="what">
            <span class="name">{#if href && p.kind === "block"}<a {href}>{p.label}</a>{:else if note}{note}{:else}{p.label}{/if}</span>
            <details>
              <summary class="muted small">{mbt.pattern}: <code>{p.part.pattern}</code></summary>
              {#if p.part.accepts && p.part.accepts.length > 0}
                <p class="small">
                  {mbt.accepts}:
                  {#each p.part.accepts as code, i (code)}{#if i > 0}, {/if}{#if itemHref(code)}<a href={itemHref(code)}><code>{code}</code></a>{:else}<code>{code}</code>{/if}{/each}{#if p.part.acceptsMore}, {mbt.more(p.part.acceptsMore)}{/if}
                </p>
              {/if}
            </details>
          </div>
        </li>
      {/each}
    </ul>
  </section>
{/if}

<style>
  .crumbs {
    font-size: 0.9rem;
    margin-bottom: 0.4rem;
  }
  h1 {
    margin-top: 0;
  }
  .meta {
    overflow-wrap: anywhere;
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
  .bench {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 18rem;
    gap: 1rem;
    align-items: start;
  }
  @media (max-width: 52rem) {
    .bench {
      grid-template-columns: minmax(0, 1fr);
    }
  }
  .stage-col {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }
  .stage {
    position: relative;
    height: min(70vh, 40rem);
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
  .pick {
    margin: 0;
    font-size: 0.875rem;
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
    gap: 0.6rem;
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
    font-size: 0.875rem;
    cursor: pointer;
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
  .reset {
    align-self: flex-start;
    font-size: 0.85rem;
  }
  .materials h2 {
    margin-bottom: 0.2rem;
  }
  .small {
    font-size: 0.85rem;
  }
  .legend {
    list-style: none;
    margin: 0.5rem 0 0;
    padding: 0;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
    gap: 0.4rem 1rem;
  }
  .legend li {
    display: flex;
    gap: 0.6rem;
    align-items: flex-start;
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--surface);
    min-width: 0;
  }
  .legend li.on {
    border-color: var(--accent);
    box-shadow: 0 0 0 1px var(--accent);
  }
  .legend li.dim {
    opacity: 0.55;
  }
  .swatch-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.2rem 0.4rem;
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
  .swatch {
    width: 1.1rem;
    height: 1.1rem;
    border-radius: 3px;
    background: var(--swatch);
    border: 1px solid rgb(0 0 0 / 0.35);
  }
  .swatch.hollow {
    background: transparent;
    border: 2px dashed var(--swatch);
  }
  .what {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 0.1rem;
  }
  .name {
    font-weight: 600;
    overflow-wrap: anywhere;
  }
  details p {
    margin: 0.2rem 0 0;
    overflow-wrap: anywhere;
  }
  summary {
    cursor: pointer;
    overflow-wrap: anywhere;
  }
</style>
