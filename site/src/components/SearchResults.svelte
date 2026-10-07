<script lang="ts">
  import type { Meta } from "../lib/format.ts";
  import { FLAG_HANDBOOK } from "../lib/format.ts";
  import type { ItemRef, VersionData } from "../lib/data.ts";
  import { formatRoute, type SearchSort } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import { isFloorZero, sortByValue } from "../lib/values.ts";
  import GearValue from "./GearValue.svelte";
  import Icon from "./Icon.svelte";
  import ModLink from "./ModLink.svelte";

  const LIMIT = 100;

  let { data, meta, query, sort }: { data: VersionData; meta: Meta; query: string; sort?: SearchSort } = $props();

  let results = $state<ItemRef[] | null>(null);
  let failed = $state(false);
  const hasValues = $derived((meta.valueCount ?? 0) > 0);

  $effect(() => {
    const q = query;
    const order = sort;
    // By value, every match is a candidate, not just the best hundred.
    data.search(q, order ? Infinity : LIMIT + 1).then(
      (hits) => {
        if (q !== query || order !== sort) return;
        if (order && data.index) hits = sortByValue(hits, data.index, order === "value-asc" ? "asc" : "desc").slice(0, LIMIT + 1);
        results = hits.map((i) => data.ref(i)!).filter(Boolean);
      },
      () => (failed = true),
    );
  });

  function reorder(e: Event) {
    const value = (e.currentTarget as HTMLSelectElement).value;
    const next = value === "value-asc" || value === "value-desc" ? value : undefined;
    // The order is part of the address; replacing the entry keeps Back for the previous page.
    location.replace(formatRoute({ view: "search", version: data.id, query, ...(next ? { sort: next } : {}) }));
  }
</script>

<h1 class="visually-hidden">{t.searchLabel}</h1>
{#if failed}
  <p role="alert">{t.loadFailed}</p>
{:else if results === null}
  <p class="muted">{t.loading}</p>
{:else if query.trim() === ""}
  <p class="muted">{t.homeHint}</p>
{:else if results.length === 0}
  <p role="status">{t.noResults(query)}</p>
{:else}
  <div class="bar">
    <p role="status" class="muted">{t.resultsFor(query, Math.min(results.length, LIMIT), results.length > LIMIT)}</p>
    {#if hasValues}
      <label class="order">
        <span>{t.sortLabel}</span>
        <select value={sort ?? "best"} onchange={reorder} data-testid="search-sort">
          {#each Object.entries(t.searchSorts) as [key, label] (key)}
            <option value={key}>{label}</option>
          {/each}
        </select>
      </label>
    {/if}
  </div>
  <ol class="results" data-testid="results">
    {#each results.slice(0, LIMIT) as r (r.index)}
      <li>
        <a class="hit" href={formatRoute({ view: "item", version: data.id, code: r.code })} data-code={r.code}>
          <Icon code={r.code} size={32} label={initials(r.name)} />
          <span class="text">
            <span class="name">{r.name}</span>
            <span class="meta">
              <code>{r.code}</code>
              {#if !(r.flags & FLAG_HANDBOOK)}<span class="muted">· {t.hiddenInHandbook}</span>{/if}
            </span>
          </span>
        </a>
        {#if hasValues}
          <span class="value" data-testid="result-value">
            {#if r.value !== undefined}<GearValue value={r.value} floorZero={isFloorZero(data.index!, r.index)} />{/if}
          </span>
        {/if}
        <span class="mod muted"><ModLink id={r.mod} mods={meta.mods} /></span>
      </li>
    {/each}
  </ol>
{/if}

<style>
  .bar {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem 1rem;
    margin: 0 0 0.5rem;
  }
  .bar p {
    margin: 0;
  }
  .order {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    font-size: 0.9rem;
  }
  .order select {
    padding: 0.25rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  .value {
    flex: none;
    min-width: 4.5rem;
    text-align: right;
  }
  /* On a phone the value goes under the name, so the name keeps the row's width. */
  @media (max-width: 40rem) {
    li {
      flex-wrap: wrap;
    }
    .value {
      order: 3;
      flex-basis: 100%;
      min-width: 0;
      text-align: left;
      padding: 0 0.5rem 0.4rem 3.25rem;
    }
  }
  .results {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  /* The mod link sits beside the row's link, not inside it: links can't nest. */
  li {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    border-radius: var(--radius);
  }
  li:hover,
  li:has(:focus-visible) {
    background: var(--surface-2);
  }
  .hit {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.4rem 0.5rem;
    border-radius: var(--radius);
    text-decoration: none;
    color: inherit;
  }
  .mod {
    max-width: 40%;
    padding-right: 0.5rem;
    font-size: 0.85rem;
    text-align: right;
    overflow-wrap: anywhere;
  }
  .text {
    display: flex;
    flex-direction: column;
    min-width: 0;
  }
  .name {
    font-weight: 600;
    color: var(--link);
  }
  .meta {
    font-size: 0.85rem;
  }
</style>
