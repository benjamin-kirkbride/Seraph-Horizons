<script lang="ts">
  // Every recipe of one type, a page at a time. A type's recipes are one run of indices
  // (TypeInfo.start), so a page fetches only the one or two chunks that hold it, even
  // for the crafting grid's thousands.
  import type { Recipe } from "../lib/export.ts";
  import type { Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { pageLinks, typePage } from "../lib/recipe-view.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import ModLink from "./ModLink.svelte";
  import RecipeCard from "./RecipeCard.svelte";

  let { data, meta, code, page = 1 }: { data: VersionData; meta: Meta; code: string; page?: number } = $props();

  // Own keys only, so that a code such as "constructor" is not found on Object.
  const info = $derived(Object.hasOwn(meta.recipeTypes, code) ? meta.recipeTypes[code]! : null);
  const view = $derived(info ? typePage(info, page) : null);

  let loaded = $state<{ page: number; recipes: Recipe[] } | null>(null);
  let failed = $state<number | null>(null);

  $effect(() => {
    const v = view;
    if (!v) return;
    // The cards look names up in search.json, so wait for it too.
    Promise.all([data.recipes(v.indices), data.ready()]).then(
      ([recipes]) => {
        if (view?.page === v.page) loaded = { page: v.page, recipes };
      },
      () => {
        if (view?.page === v.page) failed = v.page;
      },
    );
  });

  $effect(() => {
    document.title = info ? `${info.name} · ${t.siteTitle}` : t.siteTitle;
  });

  const pageHref = (p: number) => formatRoute({ view: "type", version: data.id, code, page: p });
</script>

{#snippet pager(v: NonNullable<typeof view>, where: string)}
  {#if v.pages > 1}
    <nav class="pager" aria-label={t.pagesLabel} data-testid="pager-{where}">
      {#if v.page > 1}<a href={pageHref(v.page - 1)} rel="prev">‹ {t.previousPage}</a>{/if}
      {#each pageLinks(v.page, v.pages) as p, i (i)}
        {#if p === null}
          <span class="muted">…</span>
        {:else if p === v.page}
          <span class="current" aria-current="page" aria-label={t.pageOf(p, v.pages)}>{p}</span>
        {:else}
          <a href={pageHref(p)} aria-label={t.pageOf(p, v.pages)}>{p}</a>
        {/if}
      {/each}
      {#if v.page < v.pages}<a href={pageHref(v.page + 1)} rel="next">{t.nextPage} ›</a>{/if}
    </nav>
  {/if}
{/snippet}

{#if !info || !view}
  <h1 class="code">{code}</h1>
  <p>{t.typeNotInVersion(code, data.id)}</p>
{:else}
  {@const recipes = loaded?.page === view.page ? loaded.recipes : null}
  <article class="type" data-type={code}>
    <h1>{info.name}</h1>
    <dl class="facts">
      <div><dt>{t.code}</dt><dd><code data-testid="type-code">{code}</code></dd></div>
      {#if info.mod}<div><dt>{t.mod}</dt><dd data-testid="type-mod"><ModLink id={info.mod} mods={meta.mods} /></dd></div>{/if}
      <div><dt>{t.recipesLabel}</dt><dd data-testid="type-count">{t.recipeCount(info.count)}</dd></div>
    </dl>

    {@render pager(view, "top")}
    {#if view.pages > 1}
      <p class="muted" role="status">{t.typeShowing(view.first + 1, view.first + view.indices.length, info.count)}</p>
    {/if}

    {#if failed === view.page && !recipes}
      <p role="alert">{t.loadFailed}</p>
    {:else if !recipes}
      <p class="muted">{t.loading}</p>
    {:else}
      <!-- data-page: which page the cards are from. The end-to-end tests wait on it. -->
      <div class="cards" data-page={view.page}>
        {#each recipes as recipe (recipe.id)}
          <RecipeCard {recipe} type={info} {data} mods={meta.mods} />
        {/each}
      </div>
      {@render pager(view, "bottom")}
    {/if}
  </article>
{/if}

<style>
  h1 {
    margin: 0 0 0.25rem;
    font-size: 1.6rem;
  }
  .facts {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem 1.25rem;
    margin: 0 0 1rem;
  }
  .facts div {
    display: flex;
    gap: 0.4rem;
  }
  dt {
    color: var(--muted);
  }
  dd {
    margin: 0;
  }
  .cards {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
    gap: 0.75rem;
    margin-bottom: 0.75rem;
  }
  .pager {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.25rem 0.75rem;
    margin: 0.5rem 0;
  }
  .current {
    font-weight: 700;
  }
</style>
