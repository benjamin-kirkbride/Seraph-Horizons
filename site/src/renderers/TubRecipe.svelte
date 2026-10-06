<script lang="ts">
  // A batch of gears left in a liquid in a vessel (the pickling tub): what goes in, how long
  // it takes, and how gears are lost when the batch is left too long.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { slotStacks, tubLines, tubOutputs, variantOutputs } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const lines = $derived(tubLines(recipe));
  const failure = $derived(tubOutputs(recipe).failure);
  const lost = $derived(failure >= 0 ? variantOutputs(recipe, variant)[failure] : undefined);
</script>

<div class="layout">
  <ul aria-label={t.ingredients}>
    {#each recipe.ingredients as ing, i (i)}
      <li data-input={i} data-role={ing.role}>
        <Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} showName><span>({t.tubRoles[ing.role ?? ""] ?? ing.role})</span></Slot>
      </li>
    {/each}
  </ul>
  <div class="facts" data-tub={recipe.tub?.kind} data-hours={recipe.tub?.hours}>
    {#each lines as line, i (i)}<p class={i === 0 ? "" : "muted"}>{line}</p>{/each}
    {#if lost}
      <p class="lost" data-failure={failure}>
        <span class="muted">{t.tubLost}</span>
        <Slot stacks={[lost]} {tick} {data} showName />
      </p>
    {/if}
  </div>
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
  .facts {
    font-size: 0.9rem;
    min-width: 0;
  }
  .facts p {
    margin: 0 0 0.2rem;
  }
  .lost {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.4rem;
  }
</style>
