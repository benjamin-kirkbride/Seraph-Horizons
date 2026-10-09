<script lang="ts">
  // The wind speed distribution at one height as columns, one per histogram bin, with a
  // marker at 0.6 (a vanilla windmill's cap). Each column names its bin and share on hover;
  // the table under the chart lists them all, for the keyboard and screen readers.
  import { fmt, niceTicks, pct } from "../lib/power.ts";
  import { t } from "../lib/strings.ts";

  let { bins, title, desc, mark = 0.6 }: { bins: { from: number; to: number; share: number }[]; title: string; desc: string; mark?: number } = $props();

  const id = $props.id();
  let width = $state(640);
  const height = $derived(width < 480 ? 200 : 240);
  const M = { left: 44, right: 8, top: 10, bottom: 36 };
  const pw = $derived(Math.max(10, width - M.left - M.right));
  const ph = $derived(height - M.top - M.bottom);
  const xMax = $derived(Math.max(bins.at(-1)?.to ?? 1, mark * 1.1));
  const yTicks = $derived(niceTicks(Math.max(0.01, ...bins.map((b) => b.share)), 4));
  const yMax = $derived(yTicks.at(-1)!);
  const xTicks = $derived(niceTicks(xMax, width < 480 ? 4 : 8).filter((v) => v <= xMax + 1e-9));
  const x = (v: number) => M.left + (v / xMax) * pw;
  const y = (v: number) => M.top + ph - (v / yMax) * ph;
  const colW = $derived(Math.max(1, Math.min(24, pw / Math.max(1, bins.length) - 2)));

  let hover = $state<number | null>(null);
  const tip = $derived(hover === null ? null : bins[hover]);
</script>

<figure class="chart" data-testid="wind-histogram">
  <figcaption>
    <span class="title" id="{id}-t">{title}</span>
    <span class="desc muted">{desc}</span>
  </figcaption>
  <div class="plot" bind:clientWidth={width}>
    <svg {width} {height} role="img" aria-labelledby="{id}-t">
      {#each yTicks as v (v)}
        <line class="grid" x1={M.left} x2={M.left + pw} y1={y(v)} y2={y(v)} />
        <text class="tick" x={M.left - 6} y={y(v)} text-anchor="end" dominant-baseline="middle">{pct(v)}</text>
      {/each}
      {#each xTicks as v (v)}
        <text class="tick" x={x(v)} y={M.top + ph + 16} text-anchor="middle">{fmt(v)}</text>
      {/each}
      <text class="tick" x={M.left + pw} y={height - 4} text-anchor="end">{t.power.windAxis} →</text>
      {#each bins as b, i (i)}
        {#if b.share > 0}
          {@const h = Math.max(1, ph - (y(b.share) - M.top))}
          <g class="col" class:on={hover === i} role="presentation" onpointerenter={() => (hover = i)} onpointerleave={() => (hover = null)}>
            <rect class="hit" x={x((b.from + b.to) / 2) - Math.max(colW, 12) / 2} y={M.top} width={Math.max(colW, 12)} height={ph} />
            <path
              class="bar"
              d="M{x((b.from + b.to) / 2) - colW / 2},{M.top + ph}v{-(h - Math.min(4, h, colW / 2))}q0,{-Math.min(4, h, colW / 2)} {Math.min(4, h, colW / 2)},{-Math.min(4, h, colW / 2)}h{colW - 2 * Math.min(4, h, colW / 2)}q{Math.min(4, h, colW / 2)},0 {Math.min(4, h, colW / 2)},{Math.min(4, h, colW / 2)}v{h - Math.min(4, h, colW / 2)}z"
            />
          </g>
        {/if}
      {/each}
      <line class="axis" x1={M.left} x2={M.left + pw} y1={M.top + ph} y2={M.top + ph} />
      <line class="mark" x1={x(mark)} x2={x(mark)} y1={M.top} y2={M.top + ph} />
      <text class="tick mark-label" x={x(mark) + 4} y={M.top + 2} dominant-baseline="hanging">{fmt(mark)}</text>
    </svg>
    {#if tip}
      <div class="tip" style:left="{x((tip.from + tip.to) / 2)}px" class:flip={x(tip.from) > width / 2}>
        <strong>{pct(tip.share)}</strong> <span class="muted">{fmt(tip.from)}–{fmt(tip.to)}</span>
      </div>
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
  }
  svg {
    display: block;
  }
  .grid {
    stroke: var(--chart-grid);
  }
  .axis {
    stroke: var(--chart-axis);
  }
  .mark {
    stroke: var(--text);
    stroke-width: 1;
    stroke-dasharray: 3 3;
  }
  .tick {
    fill: var(--muted);
    font-size: 0.75rem;
    font-variant-numeric: tabular-nums;
  }
  .mark-label {
    fill: var(--text);
  }
  .hit {
    fill: transparent;
  }
  .bar {
    fill: var(--series-1);
  }
  .col.on .bar {
    fill: color-mix(in srgb, var(--series-1) 70%, var(--text));
  }
  .tip {
    position: absolute;
    top: 0.25rem;
    transform: translateX(0.6rem);
    padding: 0.2rem 0.5rem;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    font-size: 0.8rem;
    white-space: nowrap;
    pointer-events: none;
  }
  .tip.flip {
    transform: translateX(calc(-100% - 0.6rem));
  }
</style>
