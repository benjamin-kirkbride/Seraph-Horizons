<script lang="ts">
  import type { EntityIndex, Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import Icon from "./Icon.svelte";
  import ModLink from "./ModLink.svelte";

  let { data, meta }: { data: VersionData; meta: Meta } = $props();

  interface Row {
    code: string;
    name: string;
    mod: string;
    variants: string[];
    drops: number;
    trades: number;
  }

  let rows = $state<{ creatures: Row[]; traders: Row[] } | null>(null);
  let failed = $state(false);
  let filter = $state("");

  $effect(() => {
    data.entities().then(
      (index: EntityIndex) => {
        const list: Row[] = index.codes.map((code, i) => ({
          code,
          name: index.names[i]!,
          mod: index.mod[i]!,
          variants: index.variantNames[i]!,
          drops: index.drops[i]!,
          trades: index.trades[i]!,
        }));
        const byName = (a: Row, b: Row) => a.name.localeCompare(b.name, "en") || (a.code < b.code ? -1 : 1);
        // A trader is listed with the traders even if it drops something too.
        rows = {
          creatures: list.filter((r) => r.trades === 0).sort(byName),
          traders: list.filter((r) => r.trades > 0).sort(byName),
        };
      },
      () => (failed = true),
    );
  });

  const needle = $derived(filter.trim().toLowerCase());
  const matches = (r: Row) =>
    needle === "" || r.code.includes(needle) || [r.name, ...r.variants].some((n) => n.toLowerCase().includes(needle));
  const shown = $derived(rows ? { creatures: rows.creatures.filter(matches), traders: rows.traders.filter(matches) } : null);
</script>

<h1>{t.entitiesHeading}</h1>
<p>{t.entitiesIntro}</p>

{#if failed}
  <p role="alert">{t.loadFailed}</p>
{:else if !shown}
  <p class="muted">{t.loading}</p>
{:else}
  <label class="filter">
    <span>{t.entitiesFilter}</span>
    <input type="search" bind:value={filter} placeholder={t.entitiesFilterPlaceholder} autocomplete="off" spellcheck="false" />
  </label>
  {#if shown.creatures.length === 0 && shown.traders.length === 0}
    <p role="status">{t.entitiesNone(filter)}</p>
  {/if}
  {#each [[t.creatures, shown.creatures, "creatures"], [t.traders, shown.traders, "traders"]] as const as [heading, list, id] (id)}
    {#if list.length > 0}
      <section aria-labelledby="{id}-h">
        <h2 id="{id}-h">{heading} <span class="muted count">{list.length}</span></h2>
        <ol class="results" data-testid={id}>
          {#each list as r (r.code)}
            <li>
              <a class="hit" href={formatRoute({ view: "entity", version: data.id, code: r.code })} data-code={r.code}>
                <Icon code={r.code} size={32} label={initials(r.name)} />
                <span class="text">
                  <span class="name">{r.name}</span>
                  <span class="meta">
                    <code>{r.code}</code>
                    <span class="muted">· {t.entityCounts(r.variants.length, r.drops, r.trades)}</span>
                  </span>
                </span>
              </a>
              <span class="mod muted"><ModLink id={r.mod} mods={meta.mods} /></span>
            </li>
          {/each}
        </ol>
      </section>
    {/if}
  {/each}
{/if}

<style>
  h2 {
    font-size: 1.2rem;
    margin: 1.5rem 0 0.5rem;
    padding-bottom: 0.2rem;
    border-bottom: 1px solid var(--border);
  }
  .count {
    font-size: 0.9rem;
    font-weight: normal;
  }
  .filter {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    max-width: 24rem;
  }
  .filter input {
    flex: 1;
    min-width: 0;
    padding: 0.35rem 0.6rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
  }
  .results {
    list-style: none;
    padding: 0;
    margin: 0;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
    gap: 2px 1rem;
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
    overflow-wrap: anywhere;
  }
</style>
