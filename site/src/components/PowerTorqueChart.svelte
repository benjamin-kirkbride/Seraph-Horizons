<script lang="ts" module>
  import type { ProducerModel } from "../lib/power-data.ts";

  export interface Curve {
    id: string;
    name: string;
    /** A CSS colour (a --series token). */
    color: string;
    model: ProducerModel;
    wind: number;
  }
</script>

<script lang="ts">
  // Torque against network speed, one 2px line per producer, drawn at the width the page
  // gives it so its text stays readable on a phone. A crosshair follows the pointer (or the
  // arrow keys, once the chart has focus) and the readout lists every curve at that speed,
  // the nearest one picked out. The table above it holds the same figures.
  import { fmt, freeSpeed, niceTicks, stallTorque, torqueAt } from "../lib/power.ts";
  import { t } from "../lib/strings.ts";

  let { curves, title, desc }: { curves: Curve[]; title: string; desc: string } = $props();

  const id = $props.id();
  let width = $state(640);
  const height = $derived(width < 480 ? 260 : 340);
  const M = { left: 48, right: 12, top: 12, bottom: 40 };
  const pw = $derived(Math.max(10, width - M.left - M.right));
  const ph = $derived(height - M.top - M.bottom);

  const xTicks = $derived(niceTicks(Math.max(0.1, ...curves.map((c) => freeSpeed(c.model, c.wind))) * 1.02, width < 480 ? 4 : 6));
  const yTicks = $derived(niceTicks(Math.max(0.01, ...curves.map((c) => stallTorque(c.model, c.wind))) * 1.02, 5));
  const xMax = $derived(xTicks.at(-1)!);
  const yMax = $derived(yTicks.at(-1)!);
  const x = (s: number) => M.left + (s / xMax) * pw;
  const y = (v: number) => M.top + ph - (Math.min(v, yMax) / yMax) * ph;

  const N = 160;
  const paths = $derived(
    curves.map((c) => {
      const free = freeSpeed(c.model, c.wind);
      const end = Math.min(xMax, free);
      const pts: string[] = [];
      for (let i = 0; i <= N; i++) {
        const s = (end * i) / N;
        pts.push(`${x(s).toFixed(1)},${y(torqueAt(c.model, s, c.wind)).toFixed(1)}`);
      }
      return { ...c, d: `M${pts.join("L")}` };
    }),
  );

  // The crosshair, in speed units; null when the pointer is away and the chart unfocused.
  let at = $state<number | null>(null);
  let pointerY = $state<number | null>(null);
  const readout = $derived(
    at === null ? [] : curves.map((c) => ({ ...c, torque: torqueAt(c.model, at!, c.wind) })).sort((a, b) => b.torque - a.torque),
  );
  const nearest = $derived.by(() => {
    if (at === null || readout.length === 0) return null;
    if (pointerY === null) return readout[0]!.id;
    const py = pointerY;
    let best = readout[0]!;
    for (const r of readout) if (Math.abs(y(r.torque) - py) < Math.abs(y(best.torque) - py)) best = r;
    return best.id;
  });
  const shown = $derived(readout.filter((r) => r.torque > 0 || r.id === nearest).slice(0, 10));
  const hidden = $derived(readout.length - shown.length);

  function move(e: PointerEvent) {
    const r = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
    const px = e.clientX - r.left;
    at = Math.max(0, Math.min(xMax, ((px - M.left) / pw) * xMax));
    pointerY = e.clientY - r.top;
  }
  function key(e: KeyboardEvent) {
    const step = xMax / 50;
    if (e.key === "ArrowRight" || e.key === "ArrowLeft") {
      e.preventDefault();
      pointerY = null;
      at = Math.max(0, Math.min(xMax, (at ?? 0) + (e.key === "ArrowRight" ? step : -step)));
    } else if (e.key === "Home") at = 0;
    else if (e.key === "End") at = xMax;
    else if (e.key === "Escape") at = null;
  }
  const tipLeft = $derived(at === null ? 0 : x(at));
</script>

<figure class="chart" data-testid="torque-chart">
  <figcaption>
    <span class="title" id="{id}-t">{title}</span>
    <span class="desc muted" id="{id}-d">{desc} {t.power.chartKeys}</span>
  </figcaption>
  <div class="plot" bind:clientWidth={width}>
    {#if curves.length === 0}
      <p class="muted empty">{t.power.noneShown}</p>
    {:else}
      <!-- An image of the curves that the keyboard can also walk along; the readout is a live region. -->
      <!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions -->
      <svg
        {width}
        {height}
        role="img"
        aria-labelledby="{id}-t"
        aria-describedby="{id}-d"
        tabindex="0"
        onpointermove={move}
        onpointerleave={() => (at = null)}
        onkeydown={key}
        onblur={() => (at = null)}
      >
        {#each yTicks as v (v)}
          <line class="grid" x1={M.left} x2={M.left + pw} y1={y(v)} y2={y(v)} />
          <text class="tick" x={M.left - 6} y={y(v)} text-anchor="end" dominant-baseline="middle">{fmt(v)}</text>
        {/each}
        {#each xTicks as v (v)}
          <text class="tick" x={x(v)} y={M.top + ph + 16} text-anchor="middle">{fmt(v)}</text>
        {/each}
        <line class="axis" x1={M.left} x2={M.left + pw} y1={M.top + ph} y2={M.top + ph} />
        <text class="axis-label" x={M.left + pw} y={height - 4} text-anchor="end">{t.power.speedAxis} →</text>
        <text class="axis-label" x={4} y={M.top - 2} dominant-baseline="hanging" transform="rotate(-90 4 {M.top})" text-anchor="end">{t.power.torqueAxis} →</text>
        {#each paths as p (p.id)}
          <path class="curve" class:dim={nearest !== null && nearest !== p.id} d={p.d} style:stroke={p.color} data-curve={p.id} />
        {/each}
        {#if at !== null}
          <line class="cross" x1={x(at)} x2={x(at)} y1={M.top} y2={M.top + ph} />
          {#each readout as r (r.id)}
            {#if r.id === nearest}
              <circle class="dot" cx={x(at)} cy={y(r.torque)} r="4.5" style:fill={r.color} />
            {/if}
          {/each}
        {/if}
      </svg>
      {#if at !== null}
        <div class="tip" class:flip={tipLeft > width / 2} style:left="{tipLeft}px" role="status">
          <p class="head">{t.power.atSpeed(fmt(at))}</p>
          <ul>
            {#each shown as r (r.id)}
              <li class:near={r.id === nearest}>
                <span class="key" style:background={r.color}></span><strong>{fmt(r.torque)}</strong>
                <span class="name">{r.name}</span>
              </li>
            {/each}
          </ul>
          {#if hidden > 0}<p class="muted more">+{hidden} at 0</p>{/if}
        </div>
      {/if}
    {/if}
  </div>
</figure>

<style>
  .chart {
    margin: 1rem 0;
  }
  figcaption {
    display: flex;
    flex-direction: column;
    margin-bottom: 0.4rem;
  }
  .title {
    font-weight: 600;
  }
  .desc {
    font-size: 0.85rem;
  }
  .plot {
    position: relative;
    width: 100%;
  }
  .empty {
    padding: 2rem 0;
  }
  svg {
    display: block;
    touch-action: pan-y;
    border-radius: var(--radius);
  }
  .grid {
    stroke: var(--chart-grid);
    stroke-width: 1;
  }
  .axis {
    stroke: var(--chart-axis);
    stroke-width: 1;
  }
  .tick,
  .axis-label {
    fill: var(--muted);
    font-size: 0.75rem;
    font-variant-numeric: tabular-nums;
  }
  .curve {
    fill: none;
    stroke-width: 2;
    stroke-linejoin: round;
    stroke-linecap: round;
    transition: opacity 0.1s;
  }
  .curve.dim {
    opacity: 0.35;
  }
  .cross {
    stroke: var(--muted);
    stroke-width: 1;
  }
  .dot {
    stroke: var(--surface);
    stroke-width: 2;
  }
  .tip {
    position: absolute;
    top: 0.5rem;
    transform: translateX(0.75rem);
    max-width: min(20rem, 60%);
    padding: 0.4rem 0.6rem;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    box-shadow: 0 2px 8px rgb(0 0 0 / 0.15);
    font-size: 0.8rem;
    pointer-events: none;
    z-index: 2;
  }
  .tip.flip {
    transform: translateX(calc(-100% - 0.75rem));
  }
  .tip p {
    margin: 0;
  }
  .head {
    color: var(--muted);
  }
  .tip ul {
    list-style: none;
    margin: 0.2rem 0 0;
    padding: 0;
  }
  .tip li {
    display: flex;
    align-items: baseline;
    gap: 0.35rem;
    line-height: 1.35;
  }
  .tip li.near .name {
    font-weight: 600;
  }
  .key {
    flex: none;
    width: 0.8rem;
    height: 2px;
    align-self: center;
  }
  .tip strong {
    font-variant-numeric: tabular-nums;
  }
  .name {
    color: var(--muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
</style>
