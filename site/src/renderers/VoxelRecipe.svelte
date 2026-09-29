<script lang="ts">
  // Smithing, knapping and clayforming: the pattern the player works, one layer at a time.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { slotStacks, variantOutputs, voxelLayers } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const layers = $derived(voxelLayers(recipe));
  let chosen = $state(0);
  const layer = $derived(layers[Math.min(chosen, layers.length - 1)] ?? []);
  const width = $derived(layer[0]?.length ?? 0);
  const filled = $derived(layer.reduce((n, row) => n + row.filter(Boolean).length, 0));
  // Keep the whole pattern within a phone's width.
  const cell = $derived(Math.max(8, Math.min(18, Math.floor(300 / Math.max(width, 1)))));
</script>

<div class="layout">
  <div class="inputs" role="group" aria-label={t.ingredients}>
    {#each recipe.ingredients as ing, i (i)}
      <Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} tool={ing.isTool ?? false} showName />
    {/each}
  </div>
  <div class="pattern">
    {#if layers.length > 1}
      <div class="layers" role="group" aria-label={t.layer}>
        {#each layers as _, li (li)}
          <button type="button" aria-pressed={li === chosen} onclick={() => (chosen = li)} aria-label={t.layerOf(li + 1, layers.length)}
            >{li + 1}</button
          >
        {/each}
      </div>
    {/if}
    <div
      class="voxels"
      role="img"
      aria-label="{t.voxelGrid(width, layer.length)}, {t.filledCells(filled)}{layers.length > 1 ? `, ${t.layerOf(chosen + 1, layers.length)}` : ''}"
      style:grid-template-columns="repeat({width}, {cell}px)"
      data-layer={chosen}
    >
      {#each layer as row, r (r)}
        {#each row as on, c (c)}
          <span class="v" class:on style:width="{cell}px" style:height="{cell}px" data-row={r} data-col={c} data-filled={on ? "1" : "0"}></span>
        {/each}
      {/each}
    </div>
  </div>
  <span class="arrow" aria-hidden="true">→</span>
  <div class="outputs" role="group" aria-label={t.output}>
    {#each variantOutputs(recipe, variant) as out, i (i)}
      <div data-output={i}><Slot stacks={[out]} {tick} {data} showName /></div>
    {/each}
  </div>
</div>

<style>
  .layout {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
  }
  .inputs,
  .outputs {
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
    min-width: 0;
  }
  .layers {
    display: flex;
    gap: 0.25rem;
    margin-bottom: 0.4rem;
    flex-wrap: wrap;
  }
  .layers button[aria-pressed="true"] {
    background: var(--accent);
    color: var(--accent-text);
    border-color: var(--accent);
  }
  .voxels {
    display: grid;
    gap: 1px;
    background: var(--border);
    border: 1px solid var(--border);
    width: max-content;
  }
  .v {
    background: var(--cell-empty);
  }
  .v.on {
    background: var(--filled);
  }
  .arrow {
    font-size: 1.5rem;
    color: var(--muted);
  }
</style>
