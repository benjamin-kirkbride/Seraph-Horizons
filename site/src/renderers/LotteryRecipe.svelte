<script lang="ts">
  // An item decided by chance, one at a time (the oiled gear): what it is, when it is
  // decided, and each outcome with its chance.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { lotteryOutcomes, slotStacks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const outcomes = $derived(lotteryOutcomes(recipe, variant));
</script>

<div class="layout">
  <div data-input="0"><Slot stacks={slotStacks(recipe, variant, 0)} {tick} {data} showName /></div>
  <div class="outcomes">
    <p class="muted">{t.lotteryTrigger(recipe.lottery?.trigger ?? "")}</p>
    <ul aria-label={t.lotteryOutcomes}>
      {#each outcomes as o, i (i)}
        <li data-outcome={i} data-chance={o.chance}>
          <span class="chance">{o.label}</span>
          {#if o.stacks.length > 0}
            {#each o.stacks as s, j (j)}<Slot stacks={[s]} {tick} {data} showName />{/each}
          {:else}
            <span class="muted">{t.lotteryNothing}</span>
          {/if}
        </li>
      {/each}
    </ul>
  </div>
</div>

<style>
  .layout {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
  }
  .outcomes {
    font-size: 0.9rem;
    min-width: 0;
  }
  .outcomes p {
    margin: 0 0 0.3rem;
  }
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
  }
  li {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.5rem;
  }
  .chance {
    min-width: 3rem;
    font-variant-numeric: tabular-nums;
    font-weight: 600;
  }
</style>
