<script lang="ts">
  import type { ItemAttributes } from "../lib/export.ts";
  import type { ItemDetail, Meta } from "../lib/format.ts";
  import type { ItemRef, VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import { distinctSources, formatNumber, formatQuantity } from "../lib/recipe-view.ts";
  import { parseVtml } from "../lib/vtml.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import Icon from "./Icon.svelte";
  import ItemLink from "./ItemLink.svelte";
  import RecipeGroups from "./RecipeGroups.svelte";
  import Vtml from "./Vtml.svelte";

  let { data, meta, code }: { data: VersionData; meta: Meta; code: string } = $props();

  type Loaded = { status: "loading" } | { status: "missing" } | { status: "error" } | { status: "ok"; ref: ItemRef; detail: ItemDetail };
  let page = $state<Loaded>({ status: "loading" });

  $effect(() => {
    const wanted = code;
    page = { status: "loading" };
    data
      .ready()
      .then(async () => {
        const index = data.indexOf(wanted);
        if (index < 0) return { status: "missing" } as const;
        const detail = await data.item(index);
        return { status: "ok", ref: data.ref(index)!, detail } as const;
      })
      .then(
        (result) => {
          if (wanted === code) page = result;
        },
        () => {
          if (wanted === code) page = { status: "error" };
        },
      );
  });

  $effect(() => {
    if (page.status === "ok") document.title = `${page.ref.name} · ${t.siteTitle}`;
  });

  const itemHref = (c: string) => formatRoute({ view: "item", version: data.id, code: c });

  // An optional third entry is a tooltip explaining the value.
  function attributeRows(a: ItemAttributes): [string, string, string?][] {
    const rows: [string, string, string?][] = [];
    const n = (x: number | undefined) => (x === undefined ? "" : formatNumber(x));
    if (a.maxStackSize !== undefined) rows.push([t.attr.maxStackSize, n(a.maxStackSize)]);
    if (a.durability !== undefined) rows.push([t.attr.durability, n(a.durability)]);
    if (a.tool) rows.push([t.attr.tool, a.tool]);
    if (a.toolTier !== undefined) rows.push([t.attr.toolTier, n(a.toolTier)]);
    if (a.requiredMiningTier !== undefined) rows.push([t.attr.requiredMiningTier, n(a.requiredMiningTier)]);
    if (a.attackPower !== undefined) rows.push([t.attr.attackPower, n(a.attackPower)]);
    if (a.materialDensity !== undefined) rows.push([t.attr.materialDensity, n(a.materialDensity), t.densityHint(a.materialDensity)]);
    if (a.nutrition) {
      const parts = [a.nutrition.category, `satiety ${n(a.nutrition.satiety)}`];
      if (a.nutrition.health) parts.push(`health ${n(a.nutrition.health)}`);
      rows.push([t.attr.nutrition, parts.join(", ")]);
    }
    if (a.burn) {
      const parts = [];
      if (a.burn.temperature !== undefined) parts.push(`${n(a.burn.temperature)} °C`);
      if (a.burn.durationSeconds !== undefined) parts.push(`for ${n(a.burn.durationSeconds)} s`);
      rows.push([t.attr.burn, parts.join(" ")]);
    }
    if (a.smelting?.meltingPoint !== undefined) {
      rows.push([t.attr.smelting, `${n(a.smelting.meltingPoint)} °C${a.smelting.durationSeconds ? `, ${n(a.smelting.durationSeconds)} s` : ""}`]);
    }
    if (a.storageFlags && a.storageFlags.length > 0) rows.push([t.attr.storageFlags, a.storageFlags.join(", ")]);
    return rows;
  }

  // Creatures and traders have pages of their own; blocks are items.
  const entitySource = (type: string) => type === "entityDrop" || type === "traderSells" || type === "traderBuys";
</script>

{#if page.status === "loading"}
  <p class="muted">{t.loading}</p>
{:else if page.status === "error"}
  <p role="alert">{t.loadFailed}</p>
{:else if page.status === "missing"}
  <h1 class="code">{code}</h1>
  <p>{t.itemNotInVersion(code, data.id)}</p>
{:else}
  {@const { ref, detail } = page}
  {@const mod = meta.mods[ref.mod]}
  <article class="item" data-item={ref.code}>
    <header class="head">
      <Icon code={ref.code} size={64} label={initials(ref.name)} />
      <div>
        <h1>{ref.name}</h1>
        <dl class="facts">
          <div><dt>{t.code}</dt><dd><code data-testid="item-code">{ref.code}</code></dd></div>
          <div><dt>{t.mod}</dt><dd data-testid="item-mod">{mod?.name ?? ref.mod}</dd></div>
        </dl>
      </div>
    </header>

    {#if detail.description}
      <section aria-labelledby="desc-h">
        <h2 id="desc-h" class="visually-hidden">{t.description}</h2>
        <p class="description" data-testid="description"><Vtml nodes={parseVtml(detail.description)} {itemHref} /></p>
      </section>
    {/if}

    {#if detail.attributes || detail.smeltsInto !== undefined}
      {@const rows = attributeRows(detail.attributes ?? {})}
      <section aria-labelledby="attr-h">
        <h2 id="attr-h">{t.attributes}</h2>
        <table class="attrs">
          <tbody>
            {#each rows as [k, v, hint] (k)}
              <tr><th scope="row">{k}</th><td>{#if hint}<span class="hint" title={hint}>{v}</span>{:else}{v}{/if}</td></tr>
            {/each}
            {#if detail.smeltsInto !== undefined}
              {@const target = data.ref(detail.smeltsInto)}
              {#if target}<tr><th scope="row">{t.smeltsInto}</th><td><ItemLink code={target.code} {data} /></td></tr>{/if}
            {/if}
          </tbody>
        </table>
      </section>
    {/if}

    {#if detail.sources && detail.sources.length > 0}
      <section aria-labelledby="src-h">
        <h2 id="src-h">{t.sources}</h2>
        <div class="scroll">
          <table class="sources">
            <thead>
              <tr><th scope="col">{t.kind}</th><th scope="col">{t.from}</th><th scope="col">{t.quantity}</th><th scope="col"></th></tr>
            </thead>
            <tbody>
              {#each distinctSources(detail.sources) as s, i (i)}
                <tr>
                  <td>{t.sourceKinds[s.type] ?? s.type}</td>
                  <td>
                    {#if entitySource(s.type)}
                      <a href={formatRoute({ view: "entity", version: data.id, code: s.from })} data-entity-link={s.from}>{s.fromName ?? s.from}</a>
                    {:else if data.indexOf(s.from) >= 0}<ItemLink code={s.from} {data} label={s.fromName} />{:else}{s.fromName ?? s.from}{/if}
                  </td>
                  <td>{formatQuantity(s.quantity)}</td>
                  <td>
                    {[s.tool ? `${t.tool.toLowerCase()}: ${s.tool}` : "", s.price !== undefined ? t.price(s.price) : "", s.note ?? ""]
                      .filter(Boolean)
                      .join("; ")}
                  </td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      </section>
    {/if}

    {#if detail.smeltedFrom && detail.smeltedFrom.length > 0}
      <section aria-labelledby="smelt-h">
        <h2 id="smelt-h">{t.smeltedFrom}</h2>
        <ul class="links">
          {#each detail.smeltedFrom as i (i)}
            {@const r = data.ref(i)}
            {#if r}<li><ItemLink code={r.code} {data} /></li>{/if}
          {/each}
        </ul>
      </section>
    {/if}

    <section aria-labelledby="made-h" data-testid="made-by">
      <h2 id="made-h">{t.madeBy}</h2>
      {#if detail.madeBy}
        <RecipeGroups groups={detail.madeBy} {meta} {data} kind="madeBy" focus={{ code: ref.code, as: "output" }} />
      {:else}
        <p class="muted">{t.noRecipesMake}</p>
      {/if}
    </section>

    <section aria-labelledby="used-h" data-testid="used-in">
      <h2 id="used-h">{t.usedIn}</h2>
      {#if detail.usedIn}
        <RecipeGroups groups={detail.usedIn} {meta} {data} kind="usedIn" focus={{ code: ref.code, as: "ingredient" }} />
      {:else}
        <p class="muted">{t.noRecipesUse}</p>
      {/if}
    </section>
  </article>
{/if}

<style>
  .head {
    display: flex;
    gap: 1rem;
    align-items: flex-start;
  }
  h1 {
    margin: 0 0 0.25rem;
    font-size: 1.6rem;
  }
  h2 {
    font-size: 1.2rem;
    margin: 1.5rem 0 0.5rem;
    padding-bottom: 0.2rem;
    border-bottom: 1px solid var(--border);
  }
  .facts {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem 1.25rem;
    margin: 0;
  }
  .facts div {
    display: flex;
    gap: 0.4rem;
  }
  dt {
    color: var(--muted);
  }
  dd {
    margin: 0;
  }
  .description {
    white-space: pre-line;
    max-width: 48rem;
  }
  .hint {
    text-decoration: underline dotted;
    cursor: help;
  }
  .scroll {
    overflow-x: auto;
  }
  .links {
    list-style: none;
    padding: 0;
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem 1rem;
  }
</style>
