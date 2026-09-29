<script lang="ts">
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatRange, slotStacks, variantOutputs } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const cooksInto = $derived(recipe.cooking?.cooksInto);
</script>

<div class="layout">
  <table>
    <thead>
      <tr><th scope="col">{t.ingredients}</th><th scope="col">{t.amount}</th></tr>
    </thead>
    <tbody>
      {#each recipe.ingredients as ing, i (i)}
        <tr data-input={i}>
          <td><Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} showName /></td>
          <td class="range" data-range>{formatRange(ing.minQuantity, ing.maxQuantity)}</td>
        </tr>
      {/each}
    </tbody>
  </table>
  <span class="arrow" aria-hidden="true">→</span>
  <div class="outputs">
    <div role="group" aria-label={t.output}>
      {#each variantOutputs(recipe, variant) as out, i (i)}
        <div data-output={i}><Slot stacks={[out]} {tick} {data} showName /></div>
      {/each}
    </div>
    {#if cooksInto}
      <div data-cooks-into>
        <span class="muted">{t.cooksInto}</span>
        <Slot stacks={[cooksInto]} {tick} {data} showName />
      </div>
    {/if}
    {#if recipe.cooking?.dirtyPot}<p class="muted">{t.dirtyPot}</p>{/if}
  </div>
</div>

<style>
  .layout {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
  }
  td {
    vertical-align: middle;
  }
  .range {
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
  .outputs {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }
  .outputs p {
    margin: 0;
  }
  .arrow {
    font-size: 1.5rem;
    color: var(--muted);
  }
</style>
