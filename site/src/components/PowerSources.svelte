<script lang="ts">
  // A section's "where these numbers come from": each figure's source, under the producer
  // or machine it belongs to, with the exporter's fallbacks to its own defaults flagged.
  import type { PowerSource } from "../lib/power-data.ts";
  import { t } from "../lib/strings.ts";

  let { entries }: { entries: { name: string; sources: PowerSource[] }[] } = $props();

  const listed = $derived(entries.filter((e) => e.sources.length > 0));
  const count = $derived(listed.reduce((n, e) => n + e.sources.length, 0));
  const fallbacks = $derived(listed.reduce((n, e) => n + e.sources.filter((s) => s.fallback).length, 0));
</script>

{#if count > 0}
  <details class="sources" data-testid="sources">
    <summary>{t.power.sourcesSummary(count, fallbacks)}</summary>
    <dl>
      {#each listed as e, i (i)}
        <dt>{e.name}</dt>
        {#each e.sources as s, j (j)}
          <dd>
            {s.what}: <span class="muted">{s.from}</span>
            {#if s.fallback}<span class="fallback" title={t.power.fallbackHint}>{t.power.fallback}</span>{/if}
          </dd>
        {/each}
      {/each}
    </dl>
  </details>
{/if}

<style>
  .sources {
    margin: 0.75rem 0;
    font-size: 0.85rem;
  }
  summary {
    cursor: pointer;
    color: var(--muted);
  }
  dl {
    margin: 0.4rem 0 0;
    columns: 2 22rem;
    column-gap: 2rem;
  }
  dt {
    font-weight: 600;
    break-after: avoid;
    margin-top: 0.3rem;
  }
  dd {
    margin: 0 0 0 1rem;
    overflow-wrap: anywhere;
  }
  .fallback {
    display: inline-block;
    margin-left: 0.3rem;
    padding: 0 0.3rem;
    border: 1px solid var(--critical);
    border-radius: var(--radius);
    font-size: 0.75rem;
  }
</style>
