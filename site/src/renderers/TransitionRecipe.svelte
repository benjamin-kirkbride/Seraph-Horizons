<script lang="ts">
  // A stack that turns into another after some time: what turns, the station it needs if
  // any (a smoking rack), and when.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatHoursRange, formatNumber, slotStacks, transitionWindow } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Hint from "../components/Hint.svelte";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const tr = $derived(recipe.transition!);
  const when = $derived(transitionWindow(tr));
  const verb = $derived(t.transitionVerbs[tr.type] ?? t.transitionVerb);
  const stations = $derived(recipe.ingredients.map((_, i) => i).filter((i) => i > 0));
  // Fresh hours first, then the transition: worth spelling out only when both take time.
  const steps = $derived(when.starts[1] > 0 && when.takes[1] > 0);
  const ratio = $derived(recipe.outputs[0]?.quantity ?? 1);
</script>

<div class="layout">
  <ul aria-label={t.ingredients}>
    <li data-input="0"><Slot stacks={slotStacks(recipe, variant, 0)} {tick} {data} showName /></li>
    {#each stations as i (i)}
      <li data-input={i} data-station><Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} showName><span>({t.transitionOn})</span></Slot></li>
    {/each}
  </ul>
  <div class="time" data-transition={tr.type} data-done-hours={when.done[0]}>
    <p>
      {#if stations.length > 0}
        {t.transitionDone(verb, formatHoursRange(when.done))}
      {:else}
        <Hint text={t.transitionRate}>{t.transitionDone(verb, formatHoursRange(when.done))}</Hint>
      {/if}
    </p>
    {#if steps}
      <p class="muted">{t.transitionSteps(formatHoursRange(when.starts), formatHoursRange(when.takes))}</p>
    {/if}
    {#if ratio !== 1}<p class="muted" data-ratio>{t.transitionRatio(formatNumber(ratio))}</p>{/if}
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
  .time {
    font-size: 0.9rem;
  }
  .time p {
    margin: 0;
  }
</style>
