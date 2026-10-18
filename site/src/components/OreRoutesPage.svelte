<script lang="ts">
  // #/<version>/ores: every way to turn each ore into metal, from ores.json (the export's
  // oreProcessing section and the placer feeds prepare-data adds). Every figure is worked out by
  // src/lib/ore-routes.ts. docs/recipe-browser/site.md describes the page.
  import type { Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import {
    GRADES,
    HAND_STEPS,
    bestRoute,
    chainOf,
    choiceRoutes,
    fmtPct,
    fmtUnits,
    glance,
    oreChoices,
    stepItem,
    type Grade,
    type OreChoice,
    type OreGroup,
    type OreRoutesData,
    type RouteNode,
  } from "../lib/ore-routes.ts";
  import { formatRoute, type OreGrade } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import ItemLink from "./ItemLink.svelte";

  let { data, meta, ore: oreId, grade: gradeParam }: { data: VersionData; meta: Meta; ore?: string; grade?: OreGrade } = $props();

  let ores = $state.raw<OreRoutesData | null>(null);
  let loaded = $state(false);
  let failed = $state(false);

  $effect(() => {
    if (!meta.ores) {
      ores = null;
      loaded = true;
      return;
    }
    data.ores().then(
      (o) => {
        ores = o;
        loaded = true;
      },
      () => (failed = true),
    );
  });

  const T = t.ores;
  const GROUPS: readonly OreGroup[] = ["oxide", "sulfide", "free", "placer"];

  const rockName = (rock: string) => {
    const name = data.nameOf(`game:rock-${rock}`);
    return name.startsWith("game:") ? rock : name.replace(/\s+rock$/i, "");
  };
  function nameOf(c: OreChoice): string {
    if (c.ore) return c.ore.name;
    const p = c.placer!;
    if (p.kind === "bogiron") return data.nameOf(p.feed);
    return p.rocks.length > 2 ? T.gravelOthers(p.rocks.length) : T.gravel(p.rocks.map(rockName).join(", "));
  }
  const optionName = (c: OreChoice) => (c.ore?.byProducts[0] ? T.withSecond(c.ore.name, c.ore.byProducts[0].metal) : nameOf(c));

  const choices = $derived(ores ? oreChoices(ores, nameOf) : []);
  const grade = $derived<Grade>(gradeParam ?? "medium");
  const choice = $derived(choices.find((c) => c.id === oreId) ?? choices.find((c) => c.id === "malachite") ?? choices[0]);
  const routes = $derived(ores && choice ? choiceRoutes(ores, choice, grade) : null);
  const best = $derived(routes ? bestRoute(routes.tree) : []);
  const bestSet = $derived(new Set(best));
  const hand = $derived(routes ? bestRoute(routes.tree, HAND_STEPS) : []);
  const metal = $derived(choice?.ore?.metal ?? "");

  const go = (ore: string | undefined, g: Grade) =>
    location.replace(formatRoute({ view: "ores", version: data.id, ...(ore ? { ore } : {}), ...(g !== "medium" ? { grade: g } : {}) }));
  const link = (ore: string) => formatRoute({ view: "ores", version: data.id, ore, ...(grade !== "medium" ? { grade } : {}) });

  const stepName = (n: RouteNode) => T.steps[n.step].name;
  const pathText = (path: RouteNode[]) =>
    path
      .filter((n) => n.step !== "mined" && n.step !== "dug")
      .map(stepName)
      .join(" → ");

  const containedText = $derived.by(() => {
    if (!choice || !routes) return "";
    if (choice.placer?.kind === "gravel")
      return T.unitsPlacer(fmtUnits(routes.contained), choice.placer.contents.map((c) => `${c.metal} ${fmtUnits(c.units)}`).join(", "));
    if (choice.placer) return T.unitsBogIron(fmtUnits(routes.contained), choice.placer.contents[0]?.metal ?? "iron");
    return T.units(fmtUnits(routes.contained), metal);
  });

  // The glance tables, at the page's grade.
  const oreRows = $derived(ores ? choices.filter((c) => c.ore).map((c) => ({ c, g: glance(ores!, c, grade) })) : []);
  const placerRows = $derived(
    ores
      ? choices
          .filter((c) => c.placer)
          .map((c) => {
            const { tree, contained } = choiceRoutes(ores!, c, grade);
            const at = (step: string) => tree.children.find((n) => n.step === step)?.units ?? null;
            return { c, contained, pan: at("pan"), rocker: at("rocker") };
          })
      : [],
  );

  const resultOf = (n: RouteNode, contained: number) => {
    if (n.units === null) return n.step === "dug" ? T.gravelNoSmelt : null;
    if (n.holds !== undefined) return T.smeltNothing(fmtUnits(n.holds));
    if (n.step === "mined" && choice?.ore?.class === "sulfide") return T.smeltRaw;
    const m = choice?.placer ? (choice.placer.kind === "gravel" ? "metal" : (choice.placer.contents[0]?.metal ?? "")) : metal;
    return T.smeltNow(fmtUnits(n.units), m, fmtPct(n.units, contained));
  };
</script>

{#snippet step(n: RouteNode, contained: number)}
  {@const item = stepItem(n.step, choice?.ore)}
  {@const isHand = HAND_STEPS.has(n.step) && n.step !== "mined" && n.step !== "dug" && n.step !== "bullion"}
  {@const isMachine = !HAND_STEPS.has(n.step) && n.step !== "line"}
  {@const result = resultOf(n, contained)}
  <div class="step" class:best={bestSet.has(n)} class:hand={isHand} class:machine={isMachine || n.step === "line"} data-step={n.step}>
    <div class="title">
      {#if item && data.indexOf(item) >= 0}<ItemLink code={item} {data} label={stepName(n)} />{:else}<span class="name">{stepName(n)}</span>{/if}
      {#if n.variant}<span class="variant">{T.variants[n.variant]}</span>{/if}
      {#if isHand}<span class="tag tag-hand">{T.hand}</span>{:else if isMachine}<span class="tag tag-machine">{T.machine}</span>{/if}
      {#if bestSet.has(n) && best.at(-1) === n}<span class="tag tag-best">{T.bestTag}</span>{/if}
    </div>
    {#if T.steps[n.step].what && !(n.step === "mined" && choice?.ore?.class === "sulfide")}<p class="what">{T.steps[n.step].what}</p>{/if}
    {#if result}
      <p class="result" class:zero={n.units === 0 || n.holds !== undefined}>
        {result}
        {#if n.byProduct}
          <br /><span class="second">{n.byProduct.units > 0 ? T.plusSecond(fmtUnits(n.byProduct.units), n.byProduct.metal) : T.secondLost(n.byProduct.metal)}</span>
        {/if}
      </p>
    {/if}
    {#if n.final}<p class="final">{T.finalTag}</p>{/if}
  </div>
{/snippet}

{#snippet branch(n: RouteNode, contained: number)}
  {@const { chain, choices: next } = chainOf(n)}
  <ol class="chain">
    {#each chain as s, i (i)}
      <li>{@render step(s, contained)}</li>
    {/each}
  </ol>
  {#if next.length > 0}
    <ul class="choices">
      {#each next as c, i (i)}
        <li>
          {#if i > 0}<span class="or" aria-hidden="true">{T.or}</span>{/if}
          {@render branch(c, contained)}
        </li>
      {/each}
    </ul>
  {/if}
{/snippet}

<h1>{T.heading}</h1>

{#if failed}
  <p role="alert">{t.loadFailed}</p>
{:else if !loaded}
  <p class="muted">{t.loading}</p>
{:else if !ores || choices.length === 0 || !choice || !routes}
  <p data-testid="ores-none">{T.none}</p>
{:else}
  <p class="lead">{T.intro}</p>

  <details class="glossary" data-testid="ores-glossary">
    <summary>{T.glossary}</summary>
    <p class="muted note">{T.glossaryNote}</p>
    <dl>
      {#each T.glossaryTerms as g (g.term)}
        <dt>{g.term}</dt>
        <dd>{g.text}</dd>
      {/each}
    </dl>
  </details>

  <section aria-labelledby="ores-routes">
    <h2 id="ores-routes">{T.sections.routes}</h2>
    <div class="controls">
      <label class="select">
        <span>{T.ore}</span>
        <select value={choice.id} onchange={(e) => go(e.currentTarget.value, grade)} data-testid="ore-picker">
          {#each GROUPS as g (g)}
            {#if choices.some((c) => c.group === g)}
              <optgroup label={T.groups[g]}>
                {#each choices.filter((c) => c.group === g) as c (c.id)}
                  <option value={c.id}>{optionName(c)}</option>
                {/each}
              </optgroup>
            {/if}
          {/each}
        </select>
      </label>
      <label class="select">
        <span>{T.grade}</span>
        <select value={grade} disabled={!!choice.placer} title={choice.placer ? T.gradeNone : undefined} onchange={(e) => go(choice.id, e.currentTarget.value as Grade)} data-testid="grade-picker">
          {#each GRADES as g (g)}
            <option value={g}>{T.grades[g]}</option>
          {/each}
        </select>
      </label>
    </div>

    <p>{containedText}</p>
    <dl class="summary" data-testid="ores-summary">
      <dt>{T.best}</dt>
      <dd>
        {#if best.length > 0 && (best.at(-1)!.units ?? 0) > 0}
          {T.bestLine(fmtUnits(best.at(-1)!.units!), fmtPct(best.at(-1)!.units!, routes.contained), pathText(best))}
          {#if (best.at(-1)!.byProduct?.units ?? 0) > 0}{T.secondMetal(fmtUnits(best.at(-1)!.byProduct!.units), best.at(-1)!.byProduct!.metal)}{/if}
        {:else}{T.bestNone}{/if}
      </dd>
      <dt>{T.byHand}</dt>
      <dd>
        {#if hand.length > 0 && (hand.at(-1)!.units ?? 0) > 0}
          {T.bestLine(fmtUnits(hand.at(-1)!.units!), fmtPct(hand.at(-1)!.units!, routes.contained), pathText(hand))}
          {#if (hand.at(-1)!.byProduct?.units ?? 0) > 0}{T.secondMetal(fmtUnits(hand.at(-1)!.byProduct!.units), hand.at(-1)!.byProduct!.metal)}{/if}
        {:else}{T.bestNone}{/if}
      </dd>
    </dl>
    <p class="muted note">{T.legend}</p>
    <div class="tree" role="group" aria-label={T.treeLabel(nameOf(choice), choice.placer ? "" : T.grades[grade].toLowerCase())} data-testid="ore-tree">
      {@render branch(routes.tree, routes.contained)}
    </div>
  </section>

  <section aria-labelledby="ores-glance">
    <h2 id="ores-glance">{T.sections.glance}</h2>
    <p class="muted note">{T.glanceNote(T.grades[grade].toLowerCase())}</p>
    <div class="scroll">
      <table data-testid="ores-glance">
        <thead>
          <tr>
            <th>{T.columns.ore}</th>
            <th>{T.columns.metal}</th>
            <th class="num">{T.columns.raw}</th>
            <th class="num">{T.columns.hand}</th>
            <th class="num">{T.columns.best}</th>
          </tr>
        </thead>
        {#each GROUPS.filter((g) => g !== "placer") as g (g)}
          <tbody>
            <tr class="group"><th colspan="5" scope="colgroup">{T.groups[g]}</th></tr>
            {#each oreRows.filter((r) => r.c.group === g) as r (r.c.id)}
              <tr class:current={r.c === choice}>
                <th scope="row"><a href={link(r.c.id)} title={T.show(r.c.ore!.name)}>{r.c.ore!.name}</a></th>
                <td>
                  {r.c.ore!.metal}{#if r.c.ore!.byProducts[0]}<span class="muted">, {r.c.ore!.byProducts[0].metal}</span>{/if}
                </td>
                <td class="num">{T.units2(fmtUnits(r.g.raw), fmtPct(r.g.raw, r.g.contained))}</td>
                <td class="num">{T.units2(fmtUnits(r.g.hand), fmtPct(r.g.hand, r.g.contained))}</td>
                <td class="num">
                  {T.units2(fmtUnits(r.g.best), fmtPct(r.g.best, r.g.contained))}{#if r.g.byProduct > 0}<br /><span class="muted">{T.plusSecond(fmtUnits(r.g.byProduct), r.c.ore!.byProducts[0]!.metal)}</span>{/if}
                </td>
              </tr>
            {/each}
          </tbody>
        {/each}
      </table>
    </div>
    {#if placerRows.length > 0}
      <h3>{T.groups.placer}</h3>
      <p class="muted note">{T.glanceNotePlacer}</p>
      <div class="scroll">
        <table data-testid="placer-glance">
          <thead>
            <tr>
              <th>{T.columns.feed}</th>
              <th class="num">{T.columns.holds}</th>
              <th class="num">{T.columns.pan}</th>
              <th class="num">{T.columns.rocker}</th>
              <th class="num">{T.columns.line}</th>
            </tr>
          </thead>
          <tbody>
            {#each placerRows as r (r.c.id)}
              <tr class:current={r.c === choice}>
                <th scope="row"><a href={link(r.c.id)}>{nameOf(r.c)}</a></th>
                <td class="num">{fmtUnits(r.contained)}</td>
                <td class="num">{r.pan === null ? "–" : fmtUnits(r.pan)}</td>
                <td class="num">{r.rocker === null ? "–" : fmtUnits(r.rocker)}</td>
                <td class="num">{fmtUnits(r.contained)}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  </section>

  <section aria-labelledby="ores-kinds">
    <h2 id="ores-kinds">{T.sections.kinds}</h2>
    {#each T.kinds as k (k.title)}
      <h3>{k.title}</h3>
      <p>{k.text}</p>
    {/each}
  </section>
{/if}

<style>
  h2 {
    font-size: 1.3rem;
    margin: 2rem 0 0.5rem;
    padding-bottom: 0.2rem;
    border-bottom: 1px solid var(--border);
  }
  h3 {
    font-size: 1.05rem;
    margin: 1.5rem 0 0.3rem;
  }
  .lead {
    max-width: 48rem;
  }
  .note {
    font-size: 0.85rem;
  }
  .glossary {
    margin: 1rem 0;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--surface);
    padding: 0.4rem 0.8rem;
  }
  .glossary summary {
    cursor: pointer;
    font-weight: 600;
  }
  .glossary dl {
    display: grid;
    grid-template-columns: minmax(9rem, max-content) 1fr;
    gap: 0.4rem 1rem;
    margin: 0.5rem 0;
    font-size: 0.9rem;
  }
  .glossary dt {
    font-weight: 600;
  }
  .glossary dd {
    margin: 0;
  }
  @media (max-width: 40rem) {
    .glossary dl {
      grid-template-columns: 1fr;
      gap: 0.1rem;
    }
    .glossary dd {
      margin-bottom: 0.5rem;
    }
  }
  .controls {
    display: flex;
    flex-wrap: wrap;
    align-items: flex-end;
    gap: 0.75rem 1.5rem;
    margin: 1rem 0 0.5rem;
  }
  .select {
    display: flex;
    flex-direction: column;
    gap: 0.1rem;
    font-size: 0.9rem;
    max-width: 100%;
  }
  select {
    padding: 0.25rem;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    background: var(--bg);
    max-width: 100%;
  }
  .summary {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: 0.2rem 1rem;
    margin: 0.5rem 0;
  }
  .summary dt {
    font-weight: 600;
  }
  .summary dd {
    margin: 0;
  }
  @media (max-width: 40rem) {
    .summary {
      grid-template-columns: 1fr;
    }
    .summary dd {
      margin-bottom: 0.4rem;
    }
  }
  /* The tree: a run of steps one under another, and at a choice an indented list of the next steps. */
  .tree {
    margin: 0.5rem 0 1rem;
  }
  .chain,
  .choices {
    list-style: none;
    margin: 0;
    padding: 0;
  }
  .chain > li + li::before {
    content: "↓";
    display: block;
    color: var(--muted);
    line-height: 1.1;
    padding-left: 1rem;
  }
  .choices {
    margin: 0.3rem 0 0.3rem 0.9rem;
    padding-left: 0.9rem;
    border-left: 2px solid var(--border);
  }
  .choices > li + li {
    margin-top: 0.4rem;
  }
  .or {
    display: block;
    color: var(--muted);
    font-size: 0.8rem;
    font-style: italic;
    margin-bottom: 0.2rem;
  }
  @media (max-width: 40rem) {
    .choices {
      margin-left: 0.3rem;
      padding-left: 0.6rem;
    }
  }
  .step {
    max-width: 34rem;
    background: var(--surface);
    border: 1px solid var(--border);
    border-left-width: 4px;
    border-radius: var(--radius);
    padding: 0.35rem 0.6rem;
    font-size: 0.9rem;
  }
  .step.hand {
    border-left-color: var(--series-4);
  }
  .step.machine {
    border-left-color: var(--series-1);
  }
  .step.best {
    outline: 2px solid var(--accent);
    outline-offset: 1px;
  }
  .title {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.2rem 0.5rem;
  }
  .name {
    font-weight: 600;
  }
  .title :global(.item-link .name) {
    font-weight: 600;
  }
  .variant {
    color: var(--muted);
    font-size: 0.85rem;
  }
  .tag {
    font-size: 0.72rem;
    border-radius: var(--radius);
    padding: 0 0.35rem;
    border: 1px solid var(--border);
    white-space: nowrap;
  }
  .tag-best {
    background: var(--accent);
    color: var(--accent-text);
    border-color: var(--accent);
  }
  .what {
    margin: 0.15rem 0 0;
    color: var(--muted);
    font-size: 0.85rem;
  }
  .result {
    margin: 0.25rem 0 0;
    font-variant-numeric: tabular-nums;
  }
  .result.zero {
    color: var(--muted);
  }
  .second {
    font-size: 0.85rem;
  }
  .final {
    margin: 0.2rem 0 0;
    font-size: 0.8rem;
    color: var(--accent);
    font-weight: 600;
  }
  .scroll {
    overflow-x: auto;
    max-width: 100%;
  }
  table {
    font-size: 0.9rem;
  }
  thead th {
    border-bottom: 1px solid var(--border);
    vertical-align: bottom;
  }
  tr.group th {
    padding-top: 0.8rem;
    color: var(--muted);
    font-weight: 600;
  }
  tbody tr:not(.group):hover {
    background: var(--surface-2);
  }
  tr.current {
    background: var(--surface-2);
  }
  .num {
    text-align: right;
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
  }
</style>
