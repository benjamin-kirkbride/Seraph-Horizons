<script lang="ts">
  // The model viewer's pages: #/models and #/models/<id>. App.svelte loads this component
  // lazily, and it needs no recipe data.
  import { loadModelIndex } from "../lib/model-data.ts";
  import type { ModelIndex } from "../lib/model-manifest.ts";
  import { formatRoute } from "../lib/route.ts";
  import { mt } from "../lib/model-strings.ts";
  import { t } from "../lib/strings.ts";
  import ModelPage from "./ModelPage.svelte";

  let { id }: { id: string | null } = $props();

  let index = $state<ModelIndex | null>(null);
  let failed = $state(false);

  $effect(() => {
    loadModelIndex().then(
      (i) => (index = i),
      () => (failed = true),
    );
  });

  const model = $derived(index && id !== null ? (index.models.find((m) => m.id === id) ?? null) : null);

  $effect(() => {
    document.title = model ? `${model.title} · ${mt.heading} · ${t.siteTitle}` : `${mt.heading} · ${t.siteTitle}`;
  });
</script>

{#if failed}
  <p role="alert">{mt.loadFailed}</p>
{:else if !index}
  <p class="muted">{t.loading}</p>
{:else if id === null}
  <h1>{mt.heading}</h1>
  <p>{mt.intro}</p>
  {#if index.models.length === 0}
    <p>{mt.none}</p>
  {:else}
    <ul class="models" data-testid="model-list">
      {#each index.models as m (m.id)}
        <li data-model={m.id}>
          <h2><a href={formatRoute({ view: "model", id: m.id })}>{m.title}</a></h2>
          <p class="description">{m.description}</p>
          <p class="muted small">{mt.counts(m.elements, m.parts)}</p>
          <p class="muted small">{m.credit}</p>
        </li>
      {/each}
    </ul>
  {/if}
{:else if !model}
  <h1>{t.notFound}</h1>
  <p>{mt.unknown(id)}</p>
  <p><a href={formatRoute({ view: "models" })}>{mt.allModels}</a></p>
{:else}
  {#key model.id}
    <ModelPage {model} />
  {/key}
{/if}

<style>
  .models {
    list-style: none;
    padding: 0;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 20rem), 1fr));
    gap: 1rem;
  }
  .models li {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.8rem 1rem;
  }
  .models h2 {
    margin: 0 0 0.4rem;
    font-size: 1.2rem;
  }
  .models p {
    margin: 0.3rem 0;
  }
  /* Manifest text holds file paths, which have nowhere to break on a phone. */
  .description {
    overflow-wrap: anywhere;
  }
  .small {
    font-size: 0.875rem;
  }
</style>
