<script lang="ts">
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { cycleAt, gridCells, slotStacks, variantOutputs } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const cells = $derived(gridCells(recipe));
  const width = $derived(recipe.grid?.width ?? 3);
  const slots = $derived(recipe.ingredients.map((ing, i) => ({ ing, i })));
</script>

<div class="layout">
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
              mark={recipe.ingredients[slot]?.key}
            />
          {:else}
            <span class="visually-hidden">{t.row(r + 1, c + 1)}: {t.empty}</span>
          {/if}
        </div>
      {/each}
    {/each}
  </div>
  <span class="arrow" aria-hidden="true">→</span>
  <div class="outputs" role="group" aria-label={t.output}>
    {#each variantOutputs(recipe, variant) as out, i (i)}
      <div class="output" data-output={i}>
        <Slot stacks={[out]} {tick} {data} showName />
      </div>
    {/each}
  </div>
</div>
{#if recipe.grid?.shapeless}
  <p class="note" data-shapeless>{t.shapeless}</p>
{/if}
<!-- The key doubles as the placeholder letter, so the grid reads without icons. -->
<ul class="note legend">
  {#each slots as { ing, i } (i)}
    {@const stacks = slotStacks(recipe, variant, i)}
    {@const shown = cycleAt(stacks, tick)}
    <li data-key={ing.key}>
      {#if ing.key}<span class="key">{ing.key}</span>{/if}
      {shown ? (shown.name ?? data.nameOf(shown.code)) : ing.code}{#if ing.isTool}, {t.toolNote(ing.toolDurabilityCost)}{/if}{#if ing.returned}, {t.returns(
          ing.returned.name ?? data.nameOf(ing.returned.code),
        )}{/if}
    </li>
  {/each}
</ul>

<style>
  .layout {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
  }
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
  .arrow {
    font-size: 1.5rem;
    color: var(--muted);
  }
  .outputs {
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
    min-width: 0;
  }
  .key {
    display: inline-block;
    min-width: 1.2em;
    font-weight: 600;
    color: var(--text);
  }
  .note {
    margin: 0.5rem 0 0;
    padding-left: 0;
    list-style: none;
    font-size: 0.9rem;
    color: var(--muted);
  }
</style>
