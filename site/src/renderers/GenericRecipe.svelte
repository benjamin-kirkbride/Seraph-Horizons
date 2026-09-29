<script lang="ts">
  // For recipe types the site has no layout for, mods' own machines above all.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { slotStacks, variantOutputs } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();
</script>

<div class="layout">
  <ul aria-label={t.ingredients}>
    {#each recipe.ingredients as ing, i (i)}
      <li data-input={i}>
        <Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} tool={ing.isTool ?? false} showName />
        {#if ing.role}<span class="muted">({ing.role})</span>{/if}
      </li>
    {/each}
  </ul>
  <span class="arrow" aria-hidden="true">→</span>
  <ul aria-label={t.output}>
    {#each variantOutputs(recipe, variant) as out, i (i)}
      <li data-output={i}><Slot stacks={[out]} {tick} {data} showName /></li>
    {/each}
  </ul>
</div>

<style>
  .layout {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
  }
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
    min-width: 0;
  }
  li {
    display: flex;
    align-items: center;
    gap: 0.4rem;
  }
  .arrow {
    font-size: 1.5rem;
    color: var(--muted);
  }
</style>
