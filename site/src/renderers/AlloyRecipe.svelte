<script lang="ts">
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatRatio, slotStacks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();
</script>

<div class="layout">
  <table>
    <thead>
      <tr><th scope="col">{t.ingredients}</th><th scope="col">{t.ratio}</th></tr>
    </thead>
    <tbody>
      {#each recipe.ingredients as ing, i (i)}
        <tr data-input={i}>
          <td><Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} showName /></td>
          <td class="ratio" data-ratio>{formatRatio(ing.minRatio, ing.maxRatio)}</td>
        </tr>
      {/each}
    </tbody>
  </table>
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
  .ratio {
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
</style>
