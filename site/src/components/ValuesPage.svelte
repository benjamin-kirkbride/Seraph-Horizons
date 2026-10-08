<script lang="ts">
  // #/<version>/values: the pack's price table, every item with a value. It reads only
  // search.json, which the app has already loaded, and shows PAGE_SIZE rows at a time: the
  // filter and the sort run over all 25,000 or so items (ValueTable), the DOM holds a page.
  // Variants Tidy Variants groups into one tile are one row when they share a price, as are
  // look-alikes such as a block's orientations, and the row opens to list them. The filters
  // and the order are in the address (ValuesView), so a link keeps them; a change replaces
  // the history entry, as the search page's order does.
  import { untrack } from "svelte";
  import { SvelteSet } from "svelte/reactivity";
  import type { Meta, SearchFile } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatRoute, type ValuesSort, type ValuesView } from "../lib/route.ts";
  import { pageLinks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import {
    FLAG_FILTERS,
    isFloorZero,
    isPerLitre,
    itemCount,
    VALUE_KINDS,
    ValueTable,
    type FlagFilter,
    type SortDir,
    type ValueColumn,
    type ValueKind,
    type ValueRow,
  } from "../lib/values.ts";
  import GearValue from "./GearValue.svelte";
  import Icon from "./Icon.svelte";
  import ModLink from "./ModLink.svelte";

  const PAGE_SIZE = 100;
  const COLUMNS: readonly ValueColumn[] = ["name", "mod", "value"];

  let { data, meta, view }: { data: VersionData; meta: Meta; view: ValuesView } = $props();

  let table = $state.raw<ValueTable | null>(null);
  let file = $state.raw<SearchFile | null>(null);
  let failed = $state(false);
  // The box's text. Each keystroke writes the address, and its hashchange comes back a moment
  // later, maybe after the next keystroke; an address the box wrote itself is not read back,
  // so a late one never undoes typing. Any other (Back, a link) replaces the text.
  let filter = $state("");
  let pending: string[] = [];
  $effect.pre(() => {
    const q = view.q ?? "";
    untrack(() => {
      const at = pending.indexOf(q);
      if (at >= 0) pending = pending.slice(at + 1);
      else {
        pending = [];
        filter = q;
      }
    });
  });
  function type(text: string) {
    filter = text;
    pending.push(text);
    update({ q: text || undefined });
  }
  const column = $derived((view.sort ?? "value-desc").split("-")[0] as ValueColumn);
  const dir = $derived((view.sort ?? "value-desc").split("-")[1] as SortDir);
  const unvalued = $derived(view.unvalued === true);
  const kind = $derived<ValueKind>(view.kind ?? "all");
  const worthless = $derived<FlagFilter>(view.worthless ?? "any");
  const unlisted = $derived<FlagFilter>(view.unlisted ?? "any");
  let page = $state(1);
  /** Open group rows, by their first item. */
  const open = new SvelteSet<number>();

  $effect(() => {
    data.searchFile().then(
      (f) => {
        file = f;
        table = new ValueTable(f, meta.mods);
      },
      () => (failed = true),
    );
  });

  const rows = $derived(table ? table.query({ filter, column, dir, unvalued, kind, worthless, unlisted }) : []);
  const filtered = $derived(kind !== "all" || worthless !== "any" || unlisted !== "any");

  /** Writes a change to the address (defaults left out) and goes back to the first page. */
  function update(next: Partial<ValuesView>) {
    // The box may be ahead of the address while the reader types.
    const v = { ...view, q: filter || undefined, ...next };
    page = 1;
    location.replace(
      formatRoute({
        view: "values",
        version: data.id,
        ...(v.q ? { q: v.q } : {}),
        ...(v.sort && v.sort !== ("value-desc" as string) ? { sort: v.sort } : {}),
        ...(v.unvalued ? { unvalued: true } : {}),
        ...(v.kind ? { kind: v.kind } : {}),
        ...(v.worthless ? { worthless: v.worthless } : {}),
        ...(v.unlisted ? { unlisted: v.unlisted } : {}),
      }),
    );
  }
  const flagValue = (f: string) => (f === "only" || f === "hide" ? f : undefined);
  const pages = $derived(Math.max(1, Math.ceil(rows.length / PAGE_SIZE)));
  const shown = $derived(rows.slice((Math.min(page, pages) - 1) * PAGE_SIZE, Math.min(page, pages) * PAGE_SIZE));

  // Values read best from the top down; names and mods from A.
  function sortOn(c: ValueColumn) {
    const next = `${c}-${c === column ? (dir === "asc" ? "desc" : "asc") : c === "value" ? "desc" : "asc"}`;
    update({ sort: next === "value-desc" ? undefined : (next as ValuesSort) });
  }

  const ariaSort = (c: ValueColumn) => (c === column ? (dir === "asc" ? "ascending" : "descending") : "none");
  const switchesOf = (i: number) => file?.valueSwitches?.[String(i)];
  const toggle = (row: ValueRow) => {
    const key = row.items[0]!;
    if (open.has(key)) open.delete(key);
    else open.add(key);
  };
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
  {#if table.grouped}<p class="muted">{t.valuesGroupedNote}</p>{/if}
  <div class="controls">
    <label class="filter">
      <span>{t.valuesFilter}</span>
      <input
        type="search"
        value={filter}
        oninput={(e) => type(e.currentTarget.value)}
        placeholder={t.valuesFilterPlaceholder}
        autocomplete="off"
        spellcheck="false"
        data-testid="values-filter"
      />
    </label>
    <label class="check">
      <input type="checkbox" checked={unvalued} onchange={(e) => update({ unvalued: e.currentTarget.checked || undefined })} data-testid="values-unvalued" />
      <span>{t.valuesUnvalued}</span>
    </label>
    <fieldset class="kinds" data-testid="values-kind">
      <legend class="visually-hidden">{t.valuesKind}</legend>
      {#each VALUE_KINDS as k (k)}
        <label class:on={kind === k}>
          <input
            type="radio"
            name="values-kind"
            value={k}
            checked={kind === k}
            onchange={() => update({ kind: k === "all" ? undefined : k })}
          />{t.valuesKinds[k]}
        </label>
      {/each}
    </fieldset>
    <label class="flag">
      <span>{t.valuesWorthless}</span>
      <select value={worthless} onchange={(e) => update({ worthless: flagValue(e.currentTarget.value) })} data-testid="values-worthless">
        {#each FLAG_FILTERS as f (f)}<option value={f}>{t.valuesFlagFilters[f]}</option>{/each}
      </select>
    </label>
    <label class="flag">
      <span>{t.valuesUnlisted}</span>
      <select value={unlisted} onchange={(e) => update({ unlisted: flagValue(e.currentTarget.value) })} data-testid="values-unlisted">
        {#each FLAG_FILTERS as f (f)}<option value={f}>{t.valuesFlagFilters[f]}</option>{/each}
      </select>
    </label>
    <p class="muted count" role="status" data-testid="values-count">
      {table.grouped ? t.valuesRowCount(rows.length, itemCount(rows)) : t.valuesCount(rows.length)}
    </p>
  </div>

  {#if rows.length === 0}
    <p role="status">{filter.trim() ? t.valuesNoMatch(filter) : filtered ? t.valuesNoMatchFilters : t.valuesNoMatch(filter)}</p>
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
          {#each shown as row (row.items[0])}
            {@const i = row.items[0]!}
            {@const code = file.codes[i]!}
            {@const label = table.label(row)}
            {@const value = table.value(row)}
            {@const switches = switchesOf(i)}
            {@const isOpen = open.has(i)}
            <tr
              data-code={row.group === undefined ? code : undefined}
              data-group={row.group === undefined ? undefined : label}
              data-items={row.items.length > 1 ? row.items.length : undefined}
            >
              <td class="name">
                <div class="item">
                  {#if row.group === undefined}
                    <a class="item" href={formatRoute({ view: "item", version: data.id, code })}>
                      <Icon {code} size={24} label={initials(label)} />
                      <span class="text"><span class="label">{label}</span> <code class="muted">{code}</code></span>
                    </a>
                  {:else}
                    <Icon {code} size={24} label={initials(label)} />
                    <span class="text"><span class="label group-title">{label}</span></span>
                  {/if}
                  {#if row.items.length > 1}
                    <button
                      type="button"
                      class="variants"
                      aria-expanded={isOpen}
                      aria-controls={isOpen ? `variants-${i}` : undefined}
                      title={t.valuesVariantsHint(
                        row.items.map((m) => file!.codes[m]!),
                        row.group !== undefined,
                      )}
                      onclick={() => toggle(row)}
                      data-testid="values-variants"
                    >
                      {t.valuesVariants(row.items.length, row.groupSize ?? row.items.length)}<span class="arrow" aria-hidden="true">{isOpen ? "▾" : "▸"}</span>
                    </button>
                  {/if}
                </div>
                {#if isOpen}
                  <ul class="members" id="variants-{i}" data-testid="values-group-members">
                    {#each row.items as m (m)}
                      {@const mcode = file.codes[m]!}
                      <li data-code={mcode}>
                        <a class="item" href={formatRoute({ view: "item", version: data.id, code: mcode })}>
                          <Icon code={mcode} size={20} label={initials(file.names[m]!)} />
                          <span class="text"><span class="member">{file.names[m]}</span> <code class="muted">{mcode}</code></span>
                        </a>
                      </li>
                    {/each}
                  </ul>
                {/if}
              </td>
              <td class="mod muted">
                {#each table.modsOf(row) as id, n (id)}{#if n > 0}, {/if}<ModLink {id} mods={meta.mods} />{/each}
              </td>
              <td class="value">
                {#if value !== undefined}
                  <GearValue {value} floorZero={isFloorZero(file, i)} perLitre={isPerLitre(file, i)} title={switches ? t.valueSwitchesHint(switches) : undefined} />
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
  .flag {
    display: flex;
    align-items: center;
    gap: 0.4rem;
  }
  .flag select {
    padding: 0.3rem 0.4rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  /* A segmented control: radios in one bordered strip, the chosen one in the accent colour. */
  .kinds {
    display: inline-flex;
    margin: 0;
    padding: 0;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    overflow: hidden;
  }
  .kinds label {
    position: relative;
    padding: 0.3rem 0.7rem;
    background: var(--surface-2);
    cursor: pointer;
  }
  .kinds label + label {
    border-left: 1px solid var(--border);
  }
  .kinds label.on {
    background: var(--accent);
    color: var(--accent-text);
  }
  .kinds input {
    position: absolute;
    opacity: 0;
    inset: 0;
    margin: 0;
    cursor: pointer;
  }
  .kinds label:has(:focus-visible) {
    outline: 2px solid var(--focus);
    outline-offset: -2px;
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
  /* An open row's list makes it tall; its mod and value stay by its title. */
  tr[data-items] td {
    vertical-align: top;
  }
  tr[data-items] td.mod,
  tr[data-items] td.value {
    padding-top: 0.45rem;
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
  .label,
  .member {
    text-decoration: underline;
  }
  .group-title {
    text-decoration: none;
  }
  .variants {
    margin-left: 0.5rem;
    font: inherit;
    font-size: 0.8rem;
    white-space: nowrap;
    color: inherit;
    background: var(--surface-2);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.05rem 0.45rem;
    cursor: pointer;
  }
  .variants .arrow {
    margin-left: 0.3rem;
    width: auto;
  }
  .members {
    list-style: none;
    margin: 0.25rem 0 0.35rem 2rem;
    padding: 0;
    display: grid;
    gap: 0.15rem;
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
