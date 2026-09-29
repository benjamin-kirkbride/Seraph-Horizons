<script lang="ts">
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { slotStacks } from "../lib/recipe-view.ts";
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
  <p class="seal" data-seal-hours={hours}>{hours > 0 ? t.sealFor(hours) : t.noSeal}</p>
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
    margin: 0;
    font-size: 0.9rem;
    color: var(--muted);
  }
</style>
