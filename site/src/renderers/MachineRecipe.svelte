<script lang="ts">
  // One job of a powered machine (the gear cutter): what it consumes, the parts fitted to it
  // and kept, the tool that wears, the oil it drains, and the turns a job takes.
  import type { Recipe } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { machineLines, machineRole, slotStacks } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Slot from "../components/Slot.svelte";

  let { recipe, variant, tick, data }: { recipe: Recipe; variant: number; tick: number; data: VersionData } = $props();

  const lines = $derived(machineLines(recipe));
  const note = (i: number): string => {
    const role = machineRole(recipe, i);
    if (role === "wear") return t.machineWear(recipe.ingredients[i]?.toolDurabilityCost, recipe.machine?.wear?.rule);
    return t.machineNotes[role] ?? "";
  };
</script>

<div class="layout">
  <ul aria-label={t.ingredients}>
    {#each recipe.ingredients as ing, i (i)}
      {@const n = note(i)}
      <li data-input={i} data-machine-role={machineRole(recipe, i)}>
        {#if n}
          <Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} tool={ing.isTool ?? false} showName><span>({n})</span></Slot>
        {:else}
          <Slot stacks={slotStacks(recipe, variant, i)} {tick} {data} tool={ing.isTool ?? false} showName />
        {/if}
      </li>
    {/each}
  </ul>
  <div class="facts" data-turns={recipe.machine?.turns}>
    {#each lines as line, i (i)}<p class={i === 0 ? "" : "muted"}>{line}</p>{/each}
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
</style>
