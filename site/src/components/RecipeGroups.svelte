<script lang="ts">
  // Recipes grouped by type. Each group fetches its recipes a page at a time, so an item
  // used by a thousand recipes does not download all of them at once.
  import type { Recipe } from "../lib/export.ts";
  import type { Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import type { Focus } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import RecipeCard from "./RecipeCard.svelte";

  const PAGE = 8;

  let {
    groups,
    meta,
    data,
    focus,
    kind,
  }: { groups: Record<string, number[]>; meta: Meta; data: VersionData; focus: Focus; kind: "madeBy" | "usedIn" } = $props();

  const order = $derived(
    Object.keys(groups).sort((a, b) => (meta.recipeTypes[a]?.name ?? a).localeCompare(meta.recipeTypes[b]?.name ?? b, "en")),
  );

  let shown = $state<Record<string, number>>({});
  let loaded = $state<Record<string, Recipe[]>>({});
  let failed = $state<Record<string, boolean>>({});

  // Plain objects on purpose: they track requests in flight and must not re-run the effect.
  const requested: Record<string, number> = {};
  const queue: Record<string, Promise<void>> = {};

  $effect(() => {
    for (const type of order) {
      const want = Math.min(shown[type] ?? PAGE, groups[type]!.length);
      const have = requested[type] ?? 0;
      if (want <= have) continue;
      requested[type] = want;
      const ids = groups[type]!.slice(have, want);
      // Pages append in the order they were asked for.
      queue[type] = (queue[type] ?? Promise.resolve())
        .then(() => data.recipes(ids))
        .then((recipes) => {
          loaded[type] = [...(loaded[type] ?? []), ...recipes];
        })
        .catch(() => {
          failed[type] = true;
        });
    }
  });
</script>

{#each order as type (type)}
  {@const info = meta.recipeTypes[type] ?? { name: type, shape: "generic" as const, count: 0 }}
  {@const total = groups[type]!.length}
  {@const recipes = loaded[type] ?? []}
  <!-- data-complete: every page of this type is loaded. The end-to-end tests wait on it. -->
  <section class="group" data-group={kind} data-type={type} data-complete={recipes.length >= total || undefined} aria-labelledby="{kind}-{type}">
    <h3 id="{kind}-{type}">{info.name} <span class="muted">({total})</span></h3>
    <div class="cards">
      {#each recipes as recipe (recipe.id)}
        <RecipeCard {recipe} type={info} {data} {focus} mods={meta.mods} />
      {/each}
    </div>
    {#if failed[type]}
      <p role="alert">{t.loadFailed}</p>
    {:else if recipes.length < Math.min(shown[type] ?? PAGE, total)}
      <p class="muted">{t.loading}</p>
    {:else if recipes.length < total}
      <button type="button" onclick={() => (shown[type] = recipes.length + PAGE * 2)}>{t.showMore(Math.min(PAGE * 2, total - recipes.length))}</button>
    {/if}
  </section>
{/each}

<style>
  .group {
    margin-bottom: 1.5rem;
  }
  h3 {
    font-size: 1.05rem;
    margin: 0 0 0.5rem;
  }
  .cards {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
    gap: 0.75rem;
    margin-bottom: 0.5rem;
  }
</style>
