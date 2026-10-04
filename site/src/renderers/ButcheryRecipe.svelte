<script lang="ts">
  // A creature's butchery with the Butchering mod: picked up whole, skinned on a hook, bled
  // out, butchered on a table; or harvested where it lies for less. Each stage lists what it
  // needs (the carcass, the station, the tool) and what it gives in this variant.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { butcheryEntities, butcheryOutput, butcheryStages, butcheryStations, efficiencyRange, formatNumber, formatQuantity, slotStacks } from "../lib/recipe-view.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import Hint from "../components/Hint.svelte";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const stages = $derived(butcheryStages(recipe));
  const entities = $derived(butcheryEntities(recipe, variant));
  const entityType = $derived(recipe.butchery?.entityType ?? "");
  const uid = $props.id();

  /** Names of the ingredients an output needs, e.g. the bucket that catches the blood. */
  function needs(output: number): string {
    const list = (recipe.outputs[output]?.extra?.needs as number[] | undefined) ?? [];
    return list.map((i) => data.nameOf(slotStacks(recipe, variant, i)[0]?.code ?? "")).join(", ");
  }

  function scaledBy(output: number): string[] {
    return (recipe.outputs[output]?.extra?.scaledBy as string[] | undefined) ?? [];
  }

  /** What each multiplier an output is scaled by can be: the stations of its stage, the creature's condition. */
  function scaleHint(output: number, by: string): { text: string; lines?: string[] } {
    if (by === "efficiency") {
      const stations = butcheryStations(recipe, output, (code) => data.nameOf(code));
      return { text: t.butcheryStationHint, lines: stations.map((s) => t.butcheryStationYield(s.name, formatNumber(s.multiplier))) };
    }
    const c = recipe.butchery?.condition;
    return { text: t.butcheryConditionHint(c ? { min: formatNumber(c.min), max: formatNumber(c.max) } : null) };
  }

  function efficiency(i: number): string {
    const r = efficiencyRange(recipe.ingredients[i]);
    return r ? t.butcheryEfficiency(formatNumber(r.min), formatNumber(r.max)) : "";
  }
</script>

{#snippet slot(i: number)}
  {@const ing = recipe.ingredients[i]}
  <li data-input={i}>
    <Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} tool={ing?.isTool} showName>
      {#if ing?.isTool && ing.toolDurabilityCost}<span>{t.toolNote(ing.toolDurabilityCost)}</span>{/if}
      {#if efficiency(i)}<span>{efficiency(i)}</span>{/if}
    </Slot>
  </li>
{/snippet}

<p class="for" data-entities>
  <span class="muted">{t.butcheryFor}</span>
  {#each entities as e, i (e.code)}{#if i > 0}, {/if}<a href={formatRoute({ view: "entity", version: data.id, code: entityType, variant: e.code })} data-entity={e.code}>{e.name}</a>{/each}
</p>
<ol class="stages" aria-label={t.stages}>
  {#each stages as stage, s (s)}
    <li data-stage={s} data-step={stage.step}>
      <strong class="step">{t.butcherySteps[stage.step] ?? stage.step}</strong>
      {#if stage.ingredients.length > 0 || (stage.options?.length ?? 0) > 0}
        <ul class="needs" aria-label={t.ingredients}>
          {#each stage.ingredients as i (i)}{@render slot(i)}{/each}
          {#each stage.options ?? [] as group, g (g)}
            {#if g > 0}<li class="or muted">{t.butcheryOr}</li>{/if}
            {#each group as i (i)}{@render slot(i)}{/each}
          {/each}
        </ul>
      {/if}
      {#if (stage.optional?.length ?? 0) > 0}
        <div class="optional">
          <span class="muted" id="{uid}-optional-{s}">{t.butcheryOptional}</span>
          <ul class="needs" aria-labelledby="{uid}-optional-{s}">{#each stage.optional ?? [] as i (i)}{@render slot(i)}{/each}</ul>
        </div>
      {/if}
      {#if stage.hours !== undefined}<p class="muted note">{t.butcheryHours(stage.hours)}</p>{/if}
      {#if stage.multiplier !== undefined}<p class="muted note" data-multiplier={stage.multiplier}>{t.butcheryHarvestCut(`${formatNumber(stage.multiplier * 100)}%`)}</p>{/if}
      <ul class="gives" aria-label={t.butcheryGives}>
        {#each stage.outputs as o (o)}
          {@const out = butcheryOutput(recipe, variant, o)}
          {#if out}
            <li data-output={o} data-yield={formatQuantity(out.yield)}>
              <Slot stacks={out.stacks} {tick} {data} showName>
                {#if out.yield.var}<span>{formatQuantity(out.yield)}</span>{/if}
                {#if needs(o)}<span>{t.butcheryNeeds(needs(o))}</span>{/if}
                {#if scaledBy(o).length > 0}
                  <span class="scaled">
                    {#each scaledBy(o) as by, k (by)}
                      {@const hint = scaleHint(o, by)}
                      {#if k > 0}, {/if}<Hint text={hint.text} lines={hint.lines}>{t.butcheryScaled[by as keyof typeof t.butcheryScaled] ?? by}</Hint>
                    {/each}
                  </span>
                {/if}
              </Slot>
            </li>
          {/if}
        {/each}
      </ul>
    </li>
  {/each}
</ol>
<p class="muted note">{t.butcheryNote}</p>

<style>
  .for {
    margin: 0 0 0.5rem;
    font-size: 0.9rem;
  }
  ol,
  ul {
    list-style: none;
    margin: 0;
    padding: 0;
    min-width: 0;
  }
  .stages > li {
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
    padding: 0.5rem 0;
    border-top: 1px solid var(--border);
  }
  .stages > li:first-child {
    border-top: none;
  }
  .stages > li[data-step="harvest"] {
    border-top-style: dashed;
  }
  .step {
    font-size: 0.9rem;
  }
  .needs,
  .gives {
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
  }
  /* Each slot holds its own notes under its name (Slot), so a row never wraps. */
  .needs li,
  .gives li {
    display: flex;
    min-width: 0;
  }
  .gives {
    padding-left: 0.75rem;
    border-left: 2px solid var(--border);
  }
  /* The label sits above the optional slots: beside them it shared a line with each name
     cycling through, and wrapped differently with each. */
  .optional {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }
  .note {
    margin: 0;
    font-size: 0.85rem;
  }
  .muted {
    font-size: 0.85rem;
  }
</style>
