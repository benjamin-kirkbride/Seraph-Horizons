<script lang="ts">
  // The multiblock pages: #/<version>/multiblocks and #/<version>/multiblock/<id>. App.svelte
  // loads this component lazily, and the page loads three.js after it.
  import type { VersionData } from "../lib/data.ts";
  import type { Meta, MultiblockIndex } from "../lib/format.ts";
  import { mbt } from "../lib/multiblock-strings.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import ModLink from "./ModLink.svelte";
  import MultiblockPage from "./MultiblockPage.svelte";

  let { data, meta, id }: { data: VersionData; meta: Meta; id: string | null } = $props();

  let index = $state<MultiblockIndex | null>(null);
  let failed = $state(false);

  $effect(() => {
    if ((meta.multiblockCount ?? 0) === 0) {
      index = { multiblocks: [] };
      return;
    }
    data.multiblocks().then(
      (i) => (index = i),
      () => (failed = true),
    );
  });

  const sorted = $derived(index ? [...index.multiblocks].sort((a, b) => a.name.localeCompare(b.name, "en")) : []);
  const entry = $derived(index && id !== null ? (index.multiblocks.find((m) => m.id === id) ?? null) : null);

  $effect(() => {
    document.title = entry ? `${entry.name} · ${mbt.heading} · ${t.siteTitle}` : `${mbt.heading} · ${t.siteTitle}`;
  });
</script>

{#if failed}
  <p role="alert">{mbt.loadFailed}</p>
{:else if !index}
  <p class="muted">{t.loading}</p>
{:else if id === null}
  <h1>{mbt.heading}</h1>
  <p>{mbt.intro}</p>
  {#if sorted.length === 0}
    <p>{mbt.none}</p>
  {:else}
    <ul class="multiblocks" data-testid="multiblock-list">
      {#each sorted as m (m.id)}
        <li data-multiblock={m.id}>
          <h2><a href={formatRoute({ view: "multiblock", version: data.id, id: m.id })}>{m.name}</a></h2>
          <p class="muted small">
            <ModLink id={m.mod} mods={meta.mods} /> · {mbt.cells(m.cells)}{#if m.sizes} · {mbt.sizes(m.sizes)}{/if}
          </p>
          <p class="muted small"><code>{m.id}</code></p>
        </li>
      {/each}
    </ul>
  {/if}
{:else if !entry}
  <h1>{t.notFound}</h1>
  <p>{mbt.unknown(id)}</p>
  <p><a href={formatRoute({ view: "multiblocks", version: data.id })}>{mbt.all}</a></p>
{:else}
  {#key `${data.id}|${entry.id}`}
    <MultiblockPage {data} {meta} {entry} />
  {/key}
{/if}

<style>
  .multiblocks {
    list-style: none;
    padding: 0;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 18rem), 1fr));
    gap: 1rem;
  }
  .multiblocks li {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.8rem 1rem;
  }
  .multiblocks h2 {
    margin: 0 0 0.4rem;
    font-size: 1.15rem;
  }
  .multiblocks p {
    margin: 0.3rem 0;
  }
  .small {
    font-size: 0.875rem;
  }
</style>
