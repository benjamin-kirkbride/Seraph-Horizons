<script lang="ts">
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { cycleAt, gridCells, slotStacks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const cells = $derived(gridCells(recipe));
  const width = $derived(recipe.grid?.width ?? 3);
  const notes = $derived(recipe.ingredients.map((ing, i) => ({ ing, i })).filter(({ ing }) => ing.isTool || ing.returned));
</script>

<div class="grid" style:grid-template-columns="repeat({width}, var(--slot))" role="group" aria-label={t.ingredients}>
  {#each cells as row, r (r)}
    {#each row as slot, c (c)}
      <div class="cell" data-row={r} data-col={c} data-empty={slot === null ? "" : undefined}>
        {#if slot !== null}
          <Slot
            stacks={slotStacks(recipe, variant, slot)}
            {tick}
            {data}
            tool={recipe.ingredients[slot]?.isTool ?? false}
          />
        {:else}
          <span class="visually-hidden">{t.row(r + 1, c + 1)}: {t.empty}</span>
        {/if}
      </div>
    {/each}
  {/each}
</div>
{#if recipe.grid?.shapeless}
  <p class="note" data-shapeless>{t.shapeless}</p>
{/if}
<!-- Tool wear and returned items don't show on the grid, so only those ingredients get a note. -->
{#each notes as { ing, i } (i)}
  {@const shown = cycleAt(slotStacks(recipe, variant, i), tick)}
  <p class="note" data-ingredient={i}>
    {shown ? (shown.name ?? data.nameOf(shown.code)) : ing.code}{#if ing.isTool}, {t.toolNote(ing.toolDurabilityCost)}{/if}{#if ing.returned}, {t.returns(
        ing.returned.name ?? data.nameOf(ing.returned.code),
      )}{/if}
  </p>
{/each}

<style>
  .grid {
    display: grid;
    gap: 3px;
  }
  .cell {
    width: var(--slot);
    height: var(--slot);
    border-radius: 4px;
    background: var(--cell-empty);
  }
  .note {
    margin: 0.5rem 0 0;
    font-size: 0.9rem;
    color: var(--muted);
  }
</style>
