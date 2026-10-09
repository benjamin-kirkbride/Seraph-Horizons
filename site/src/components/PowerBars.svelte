<script lang="ts" module>
  export interface Bar {
    key: string;
    label: string;
    /** A second, quieter line under the label ("full wind"). */
    sub?: string;
    value: number;
    /** End of a lighter extension: the high end of a ranged value. */
    range?: number;
    /** End of a hatched extension: the value when a machine runs dry. */
    dry?: number;
    /** A CSS colour (a --series token). */
    color: string;
    /** The value as text, at the bar's tip. */
    text: string;
    /** What a screen reader hears instead of `text`, when the tip is a shortened form. */
    spoken?: string;
    /** A status shown at the baseline instead of a bar ("Stalls"). */
    status?: string;
    /** Dimmer, for a second bar of the same producer. */
    light?: boolean;
  }
  export interface BarGroup {
    name?: string;
    bars: Bar[];
  }
  export interface LegendKey {
    label: string;
    color: string;
    kind?: "solid" | "light" | "hatch";
  }
</script>

<script lang="ts">
  // A horizontal bar chart in plain HTML: rows wrap and reflow on a phone, the value is
  // written at each bar's tip so nothing hides behind a hover, and a screen reader reads
  // each row as "label: value". Bars grow from one baseline with a rounded data end.
  import { fmt, niceTicks } from "../lib/power.ts";

  let {
    title,
    desc,
    axis,
    groups,
    legend = [],
    testid,
  }: { title: string; desc?: string; axis: string; groups: BarGroup[]; legend?: LegendKey[]; testid?: string } = $props();

  const id = $props.id();
  const top = $derived(Math.max(0, ...groups.flatMap((g) => g.bars.map((b) => Math.max(b.value, b.range ?? 0, b.dry ?? 0)))));
  const ticks = $derived(niceTicks(top, 4));
  const max = $derived(ticks.at(-1)! || 1);
  const pos = (v: number) => `${(Math.max(0, v) / max) * 100}%`;
  const ext = (from: number, to: number) => `calc(${((to - from) / max) * 100}% - 2px)`;
  const end = (b: Bar) => Math.max(b.value, b.range ?? 0, b.dry ?? 0);
</script>

<figure class="bars" aria-labelledby="{id}-t" data-testid={testid}>
  <figcaption>
    <span class="title" id="{id}-t">{title}</span>
    {#if desc}<span class="desc muted">{desc}</span>{/if}
  </figcaption>
  {#if legend.length > 0}
    <ul class="legend" aria-label="Legend">
      {#each legend as k (k.label)}
        <li><span class="swatch {k.kind ?? 'solid'}" style:--c={k.color}></span>{k.label}</li>
      {/each}
    </ul>
  {/if}
  {#each groups as g, gi (gi)}
    {#if g.name}<p class="group">{g.name}</p>{/if}
    <ul class="rows" aria-label={g.name ?? title}>
      {#each g.bars as b (b.key)}
        <li class="row" class:light={b.light} aria-label="{b.label}{b.sub ? ` (${b.sub})` : ''}: {b.status ?? b.spoken ?? b.text}" data-key={b.key}>
          <span class="label" aria-hidden="true">{b.label}{#if b.sub}<span class="sub muted">{b.sub}</span>{/if}</span>
          <span class="track" aria-hidden="true">
            {#each ticks as tk (tk)}<span class="grid" style:left={pos(tk)}></span>{/each}
            {#if b.status}
              <span class="status"><span class="icon">⚠</span>{b.status}</span>
            {:else}
              <span class="bar" class:open={b.range !== undefined || b.dry !== undefined} style:width={pos(b.value)} style:--c={b.color}></span>
              {#if b.range !== undefined && b.range > b.value}
                <span class="bar ext light-ext" style:left="calc({pos(b.value)} + 2px)" style:width={ext(b.value, b.range)} style:--c={b.color}></span>
              {/if}
              {#if b.dry !== undefined && b.dry > Math.max(b.value, b.range ?? 0)}
                {@const from = Math.max(b.value, b.range ?? 0)}
                <span class="bar ext hatch" style:left="calc({pos(from)} + 2px)" style:width={ext(from, b.dry)} style:--c={b.color}></span>
              {/if}
              <span class="value" style:left="calc({pos(end(b))} + 0.35rem)">{b.text}</span>
            {/if}
          </span>
        </li>
      {/each}
    </ul>
  {/each}
  <div class="axis" aria-hidden="true">
    <span class="label">{axis}</span>
    <span class="track">
      {#each ticks as tk (tk)}<span class="tick" style:left={pos(tk)}>{fmt(tk)}</span>{/each}
    </span>
  </div>
</figure>

<style>
  .bars {
    margin: 1rem 0;
    container-type: inline-size;
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
  .legend {
    list-style: none;
    display: flex;
    flex-wrap: wrap;
    gap: 0.2rem 1rem;
    padding: 0;
    margin: 0 0 0.4rem;
    font-size: 0.85rem;
  }
  .legend li {
    display: flex;
    align-items: center;
    gap: 0.35rem;
  }
  .swatch {
    width: 0.9rem;
    height: 0.6rem;
    border-radius: 2px;
    background: var(--c);
  }
  .swatch.light,
  .bar.light-ext {
    background: color-mix(in srgb, var(--c) 40%, var(--bg));
  }
  .swatch.hatch,
  .bar.hatch {
    background: repeating-linear-gradient(45deg, var(--c) 0 2px, color-mix(in srgb, var(--c) 25%, var(--bg)) 2px 5px);
  }
  .group {
    margin: 0.6rem 0 0.1rem;
    font-size: 0.85rem;
    font-weight: 600;
  }
  .rows {
    list-style: none;
    margin: 0;
    padding: 0;
  }
  .row,
  .axis {
    display: grid;
    grid-template-columns: minmax(0, 14rem) minmax(0, 1fr);
    gap: 0 0.75rem;
    align-items: center;
  }
  .row {
    min-height: 1.6rem;
  }
  .label {
    font-size: 0.85rem;
    line-height: 1.2;
    text-align: right;
    overflow-wrap: anywhere;
  }
  .sub {
    display: block;
    font-size: 0.75rem;
  }
  /* The scale ends 4.5rem short of the track, so the value at a full bar's tip still fits. */
  .track {
    position: relative;
    height: 1.4rem;
    margin-right: 4.5rem;
  }
  .grid {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 1px;
    background: var(--chart-grid);
  }
  .bar {
    position: absolute;
    left: 0;
    top: 0.2rem;
    height: 1rem;
    background: var(--c);
    border-radius: 0 4px 4px 0;
  }
  .bar.open {
    border-radius: 0;
  }
  .ext {
    border-radius: 0 4px 4px 0;
  }
  .row.light .bar {
    background: color-mix(in srgb, var(--c) 55%, var(--bg));
  }
  .value,
  .status {
    position: absolute;
    top: 0;
    white-space: nowrap;
    font-size: 0.8rem;
    line-height: 1.4rem;
    font-variant-numeric: tabular-nums;
  }
  .status {
    left: 0.35rem;
    font-weight: 600;
  }
  .icon {
    color: var(--critical);
    margin-right: 0.25rem;
  }
  .axis {
    border-top: 1px solid var(--chart-axis);
    margin-top: 0.2rem;
    font-size: 0.75rem;
    color: var(--muted);
  }
  .axis .track {
    height: 1.2rem;
  }
  .tick {
    position: absolute;
    transform: translateX(-50%);
    font-variant-numeric: tabular-nums;
  }
  .tick:first-child {
    transform: none;
  }
  @container (max-width: 34rem) {
    .row,
    .axis {
      grid-template-columns: minmax(0, 1fr);
    }
    .label {
      text-align: left;
      margin-top: 0.3rem;
    }
    .axis > .label {
      display: none;
    }
  }
</style>
