<script lang="ts">
  import type { Meta } from "../lib/format.ts";
  import { FLAG_HANDBOOK } from "../lib/format.ts";
  import type { ItemRef, VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import Icon from "./Icon.svelte";
  import ModLink from "./ModLink.svelte";

  const LIMIT = 100;

  let { data, meta, query }: { data: VersionData; meta: Meta; query: string } = $props();

  let results = $state<ItemRef[] | null>(null);
  let failed = $state(false);

  $effect(() => {
    const q = query;
    data.search(q, LIMIT + 1).then(
      (hits) => {
        if (q === query) results = hits.map((i) => data.ref(i)!).filter(Boolean);
      },
      () => (failed = true),
    );
  });
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
  <p role="status" class="muted">{t.resultsFor(query, Math.min(results.length, LIMIT), results.length > LIMIT)}</p>
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
        <span class="mod muted"><ModLink id={r.mod} mods={meta.mods} /></span>
      </li>
    {/each}
  </ol>
{/if}

<style>
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
