<script lang="ts">
  // A block built in place (the water wheel, ppex's engines): placed, then completed stage
  // by stage with right clicks that each take stacks from the hotbar.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { constructionStages, constructionTotals, slotStacks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const stages = $derived(constructionStages(recipe));
  const totals = $derived(constructionTotals(recipe, variant));
</script>

<p class="note">{t.buildInPlace}</p>
<ol class="stages" aria-label={t.stages}>
  {#each stages as stage, s (s)}
    <li data-stage={s}>
      <span class="step">{t.stage(s + 1)}</span>
      {#if stage.ingredients.length > 0}
        <ul aria-label={t.ingredients}>
          {#each stage.ingredients as i (i)}
            <li data-input={i}><Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} showName /></li>
          {/each}
        </ul>
      {:else}
        <span class="muted">{s === 0 && !stage.action ? t.placed : t.rightClick(stage.action ?? t.construct)}</span>
      {/if}
      {#if stage.ingredients.length > 0 && stage.action}
        <span class="muted">{t.rightClick(stage.action)}</span>
      {/if}
    </li>
  {/each}
</ol>
{#if totals.length > 0}
  <div class="totals">
    <strong>{t.totalNeeded}</strong>
    <ul data-totals>
      {#each totals as stacks, i (i)}
        <li><Slot {stacks} {tick} {data} showName /></li>
      {/each}
    </ul>
  </div>
{/if}

<style>
  .note {
    margin: 0 0 0.5rem;
    font-size: 0.9rem;
    color: var(--muted);
  }
  ol,
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    min-width: 0;
  }
  /* The label sits above the stage's items: beside them it lined up differently with each
     name's length, and wrapped under long ones. */
  .stages > li {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 0.3rem;
    padding: 0.35rem 0;
    border-top: 1px solid var(--border);
  }
  .stages > li:first-child {
    border-top: none;
  }
  .step {
    font-size: 0.85rem;
    font-weight: 600;
  }
  .stages ul,
  .totals ul {
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
  }
  .totals {
    margin-top: 0.6rem;
  }
  .totals strong {
    font-size: 0.9rem;
  }
  .totals ul {
    margin-top: 0.3rem;
  }
</style>
