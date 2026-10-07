<script lang="ts">
  // #/<version>/values: the pack's price table, every item with a value. It reads only
  // search.json, which the app has already loaded, and shows PAGE_SIZE rows at a time: the
  // filter and the sort run over all 25,000 or so rows (ValueTable), the DOM holds a page.
  import type { Meta, SearchFile } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import { pageLinks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import { isFloorZero, valueOf, ValueTable, type SortDir, type ValueColumn } from "../lib/values.ts";
  import GearValue from "./GearValue.svelte";
  import Icon from "./Icon.svelte";
  import ModLink from "./ModLink.svelte";

  const PAGE_SIZE = 100;
  const COLUMNS: readonly ValueColumn[] = ["name", "mod", "value"];

  let { data, meta }: { data: VersionData; meta: Meta } = $props();

  let table = $state.raw<ValueTable | null>(null);
  let file = $state.raw<SearchFile | null>(null);
  let failed = $state(false);
  let filter = $state("");
  let column = $state<ValueColumn>("value");
  let dir = $state<SortDir>("desc");
  let unvalued = $state(false);
  let page = $state(1);

  $effect(() => {
    data.searchFile().then(
      (f) => {
        file = f;
        table = new ValueTable(f, meta.mods);
      },
      () => (failed = true),
    );
  });

  const rows = $derived(table ? table.query({ filter, column, dir, unvalued }) : []);
  const pages = $derived(Math.max(1, Math.ceil(rows.length / PAGE_SIZE)));
  const shown = $derived(rows.slice((Math.min(page, pages) - 1) * PAGE_SIZE, Math.min(page, pages) * PAGE_SIZE));

  // Values read best from the top down; names and mods from A.
  function sortOn(c: ValueColumn) {
    if (c === column) dir = dir === "asc" ? "desc" : "asc";
    else {
      column = c;
      dir = c === "value" ? "desc" : "asc";
    }
    page = 1;
  }

  const ariaSort = (c: ValueColumn) => (c === column ? (dir === "asc" ? "ascending" : "descending") : "none");
  const switchesOf = (i: number) => file?.valueSwitches?.[String(i)];
</script>

{#snippet pager(where: string)}
  {#if pages > 1}
    <nav class="pager" aria-label="{t.pagesLabel} ({where})">
      <button type="button" disabled={page <= 1} onclick={() => (page = Math.max(1, page - 1))}>{t.previousPage}</button>
      {#each pageLinks(Math.min(page, pages), pages) as p, i (i)}
        {#if p === null}<span aria-hidden="true">…</span>
        {:else if p === Math.min(page, pages)}<span class="current" aria-current="page">{p}</span>
        {:else}<button type="button" class="link" onclick={() => (page = p)}>{p}</button>{/if}
      {/each}
      <button type="button" disabled={page >= pages} onclick={() => (page = Math.min(pages, page + 1))}>{t.nextPage}</button>
    </nav>
  {/if}
{/snippet}

<h1>{t.valuesHeading}</h1>
<p>{t.valuesIntro}</p>

{#if failed}
  <p role="alert">{t.loadFailed}</p>
{:else if !table || !file}
  <p class="muted">{t.loading}</p>
{:else if table.valued === 0}
  <p>{t.valuesNone}</p>
{:else}
  <div class="controls">
    <label class="filter">
      <span>{t.valuesFilter}</span>
      <input
        type="search"
        bind:value={filter}
        oninput={() => (page = 1)}
        placeholder={t.valuesFilterPlaceholder}
        autocomplete="off"
        spellcheck="false"
        data-testid="values-filter"
      />
    </label>
    <label class="check">
      <input type="checkbox" bind:checked={unvalued} onchange={() => (page = 1)} />
      <span>{t.valuesUnvalued}</span>
    </label>
    <p class="muted count" role="status" data-testid="values-count">{t.valuesCount(rows.length)}</p>
  </div>

  {#if rows.length === 0}
    <p role="status">{t.valuesNoMatch(filter)}</p>
  {:else}
    {@render pager("top")}
    <div class="scroll">
      <table class="values" data-testid="values" data-sort="{column}-{dir}" data-page={Math.min(page, pages)}>
        <thead>
          <tr>
            {#each COLUMNS as c (c)}
              <th scope="col" class={c} aria-sort={ariaSort(c)}>
                <button type="button" onclick={() => sortOn(c)} title={t.sortBy(t.valuesColumns[c])} data-column={c}>
                  {t.valuesColumns[c]}<span class="arrow" aria-hidden="true">{c === column ? (dir === "asc" ? "▲" : "▼") : ""}</span>
                </button>
              </th>
            {/each}
          </tr>
        </thead>
        <tbody>
          {#each shown as i (i)}
            {@const code = file.codes[i]!}
            {@const name = file.names[i]!}
            {@const value = valueOf(file, i)}
            {@const switches = switchesOf(i)}
            <tr data-code={code}>
              <td class="name">
                <a class="item" href={formatRoute({ view: "item", version: data.id, code })}>
                  <Icon {code} size={24} label={initials(name)} />
                  <span class="text"><span class="label">{name}</span> <code class="muted">{code}</code></span>
                </a>
              </td>
              <td class="mod muted"><ModLink id={file.mods[file.mod[i]!]!} mods={meta.mods} /></td>
              <td class="value">
                {#if value !== undefined}
                  <GearValue {value} floorZero={isFloorZero(file, i)} title={switches ? t.valueSwitchesHint(switches) : undefined} />
                {:else}
                  <span class="muted">–</span>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
    {@render pager("bottom")}
  {/if}
{/if}

<style>
  h1 {
    margin: 0 0 0.25rem;
    font-size: 1.6rem;
  }
  .controls {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.5rem 1.25rem;
    margin: 0.75rem 0;
  }
  .filter {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    flex: 0 1 24rem;
  }
  .filter input {
    flex: 1;
    min-width: 0;
    padding: 0.35rem 0.6rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  .check {
    display: flex;
    align-items: center;
    gap: 0.4rem;
  }
  .count {
    margin: 0;
  }
  .scroll {
    overflow-x: auto;
  }
  .values {
    width: 100%;
    border-collapse: collapse;
  }
  th {
    text-align: left;
    border-bottom: 1px solid var(--border);
    padding: 0;
  }
  th.value,
  td.value {
    text-align: right;
  }
  th button {
    font: inherit;
    font-weight: 700;
    color: inherit;
    background: none;
    border: none;
    padding: 0.4rem 0.5rem;
    cursor: pointer;
    width: 100%;
    text-align: inherit;
  }
  .arrow {
    display: inline-block;
    width: 1em;
    margin-left: 0.25rem;
    font-size: 0.75em;
  }
  td {
    padding: 0.2rem 0.5rem;
    border-bottom: 1px solid var(--border);
    vertical-align: middle;
  }
  tbody tr:hover {
    background: var(--surface-2);
  }
  .item {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    text-decoration: none;
  }
  .text {
    min-width: 0;
    overflow-wrap: anywhere;
  }
  .label {
    text-decoration: underline;
  }
  .text code {
    font-size: 0.8rem;
  }
  .mod {
    font-size: 0.85rem;
    overflow-wrap: break-word;
  }
  /* On a phone the code would take more room than the name; the filter still matches it. */
  @media (max-width: 40rem) {
    .text code {
      display: none;
    }
    td {
      padding: 0.2rem 0.25rem;
    }
  }
  .pager {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.25rem 0.75rem;
    margin: 0.5rem 0;
  }
  .pager .link {
    font: inherit;
    color: var(--link);
    background: none;
    border: none;
    padding: 0;
    text-decoration: underline;
    cursor: pointer;
  }
  .current {
    font-weight: 700;
  }
</style>
