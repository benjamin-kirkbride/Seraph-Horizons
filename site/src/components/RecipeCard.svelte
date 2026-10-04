<script lang="ts">
  // One recipe definition. A recipe with several resolved variants cycles through them
  // like the handbook; the controls let a reader step or stop it, and a reader who asked
  // for reduced motion starts with it stopped.
  import type { Recipe } from "../lib/export.ts";
  import type { MetaMod, TypeInfo } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { cardOutputs, focusVariants, type Focus } from "../lib/recipe-view.ts";
  import { clock, prefersReducedMotion } from "../lib/state.svelte.ts";
  import { t } from "../lib/strings.ts";
  import GridRecipe from "../renderers/GridRecipe.svelte";
  import VoxelRecipe from "../renderers/VoxelRecipe.svelte";
  import BarrelRecipe from "../renderers/BarrelRecipe.svelte";
  import AlloyRecipe from "../renderers/AlloyRecipe.svelte";
  import CookingRecipe from "../renderers/CookingRecipe.svelte";
  import ConstructionRecipe from "../renderers/ConstructionRecipe.svelte";
  import ButcheryRecipe from "../renderers/ButcheryRecipe.svelte";
  import GenericRecipe from "../renderers/GenericRecipe.svelte";
  import ModLink from "./ModLink.svelte";
  import Slot from "./Slot.svelte";

  let {
    recipe,
    type,
    data,
    focus = null,
    only,
    mods,
  }: { recipe: Recipe; type: TypeInfo; data: VersionData; focus?: Focus; mods: Record<string, MetaMod>; only?: number[] } = $props();

  // `only` picks the variants outright: a creature page showing some of its creatures.
  const variants = $derived(only && only.length > 0 ? only : focusVariants(recipe, focus));
  let paused = $state(prefersReducedMotion());
  let offset = $state(0);
  let frozen = $state(0);
  const step = $derived(paused ? frozen : clock.tick + offset);
  const n = $derived(variants.length);
  const variant = $derived(variants[((step % Math.max(n, 1)) + Math.max(n, 1)) % Math.max(n, 1)] ?? 0);
  const position = $derived(variants.indexOf(variant) + 1);
  // Slots with several accepted stacks cycle too, so they also need a pause button.
  const cycles = $derived(n > 1 || variants.some((v) => (recipe.variants[v]?.ingredients ?? []).some((slot) => slot.length > 1)));
  const bindings = $derived(Object.entries(recipe.variants[variant]?.bindings ?? {}));

  const outputs = $derived(cardOutputs(recipe, variant));
  const outputNames = $derived(outputs.map((o) => o.name ?? data.nameOf(o.code)).join(", "));

  function move(by: number) {
    if (paused) frozen += by;
    else offset += by;
  }
  function togglePause() {
    if (paused) offset = frozen - clock.tick;
    else frozen = clock.tick + offset;
    paused = !paused;
  }

  const renderers = {
    grid: GridRecipe,
    voxels: VoxelRecipe,
    barrel: BarrelRecipe,
    alloy: AlloyRecipe,
    cooking: CookingRecipe,
    construction: ConstructionRecipe,
    butchery: ButcheryRecipe,
    generic: GenericRecipe,
  };
  // The type block a shape relies on can be missing in a malformed record; the generic
  // card still shows its ingredients and outputs.
  const Renderer = $derived.by(() => {
    const shape = type.shape;
    if (shape === "grid" && !recipe.grid) return GenericRecipe;
    if (shape === "voxels" && !recipe.voxels) return GenericRecipe;
    if (shape === "construction" && !recipe.construction) return GenericRecipe;
    if (shape === "butchery" && !recipe.butchery) return GenericRecipe;
    return renderers[shape] ?? GenericRecipe;
  });
</script>

<article class="card" aria-label="{type.name}: {outputNames}" data-recipe-id={recipe.id} data-shape={type.shape} data-variant={variant}>
  <!-- What the recipe makes heads the card; the renderer below shows what goes in. -->
  <h4 class="outputs">
    {#each outputs as out, i (i)}
      <span data-output={i}><Slot stacks={[out]} tick={step} {data} showName /></span>
    {/each}
  </h4>
  <!-- The group heading already names the type, so the header only holds what differs. -->
  {#if recipe.mod !== "game" || bindings.length > 0 || cycles}
  <header>
    <div class="title">
      {#if recipe.mod !== "game"}<span class="muted" data-testid="recipe-mod"><ModLink id={recipe.mod} {mods} /></span>{/if}
      {#if bindings.length > 0}
        <span class="bindings">{#each bindings as [k, v] (k)}<span class="binding">{k}: {v}</span>{/each}</span>
      {/if}
    </div>
    {#if cycles}
      <div class="controls" role="group" aria-label={n > 1 ? t.variantOf(position, n) : t.pause}>
        {#if n > 1}
          <button type="button" onclick={() => move(-1)} aria-label={t.previousVariant}>‹</button>
          <span class="count" aria-live={paused ? "polite" : "off"} data-position={position}>{position}/{n}</span>
          <button type="button" onclick={() => move(1)} aria-label={t.nextVariant}>›</button>
        {/if}
        <button type="button" onclick={togglePause} aria-pressed={paused} data-pause>{paused ? t.play : t.pause}</button>
      </div>
    {/if}
  </header>
  {/if}
  <Renderer {recipe} {variant} tick={step} {data} />
  {#if recipe.requirements && recipe.requirements.length > 0}
    <div class="requirements">
      <strong>{t.requirements}:</strong>
      <ul>
        {#each recipe.requirements as req, i (i)}<li>{req}</li>{/each}
      </ul>
    </div>
  {/if}
  {#if recipe.source}<p class="source muted">{t.source} <code>{recipe.source}</code></p>{/if}
</article>

<style>
  .card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.75rem;
    min-width: 0;
  }
  .outputs {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem 1rem;
    margin: 0 0 0.6rem;
    font-size: 1.05rem;
    font-weight: 600;
  }
  header {
    display: flex;
    flex-wrap: wrap;
    justify-content: space-between;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 0.6rem;
  }
  .binding {
    font-size: 0.8rem;
    background: var(--surface-2);
    border-radius: 3px;
    padding: 0 0.3rem;
    margin-right: 0.25rem;
  }
  .controls {
    display: flex;
    align-items: center;
    gap: 0.25rem;
  }
  .controls button {
    min-width: 2rem;
  }
  .count {
    font-variant-numeric: tabular-nums;
    min-width: 3.2rem;
    text-align: center;
    font-size: 0.9rem;
  }
  .requirements {
    margin-top: 0.6rem;
    font-size: 0.9rem;
  }
  .requirements ul {
    margin: 0.2rem 0 0;
    padding-left: 1.2rem;
  }
  .source {
    margin: 0.5rem 0 0;
    font-size: 0.8rem;
  }
</style>
