<script lang="ts">
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { slotStacks, variantOutputs } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const hours = $derived(recipe.barrel?.sealHours ?? 0);
</script>

<div class="layout">
  <ul class="inputs" aria-label={t.ingredients}>
    {#each recipe.ingredients as _, i (i)}
      <li data-input={i}><Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} showName /></li>
    {/each}
  </ul>
  <div class="seal">
    <span class="arrow" aria-hidden="true">→</span>
    <span data-seal-hours={hours}>{hours > 0 ? t.sealFor(hours) : t.noSeal}</span>
  </div>
  <ul class="outputs" aria-label={t.output}>
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
  .seal {
    display: flex;
    flex-direction: column;
    align-items: center;
    font-size: 0.9rem;
    color: var(--muted);
    max-width: 10rem;
    text-align: center;
  }
  .arrow {
    font-size: 1.5rem;
  }
</style>
