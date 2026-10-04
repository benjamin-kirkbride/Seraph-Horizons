<script lang="ts">
  import type { EntityIndex, EntityVariant, Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { entitySections, formatSpread, groupLabel, mergeVariants, variantGroups, type MergedRow, type Placed, type VariantGroup } from "../lib/entity-view.ts";
  import { butcheryVariantsFor, formatQuantity } from "../lib/recipe-view.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import { indexOfSorted } from "../lib/wildcard.ts";
  import type { Recipe } from "../lib/export.ts";
  import Icon from "./Icon.svelte";
  import ItemLink from "./ItemLink.svelte";
  import RecipeCard from "./RecipeCard.svelte";

  let { data, meta, code, variant }: { data: VersionData; meta: Meta; code: string; variant?: string } = $props();

  type Loaded =
    | { status: "loading" }
    | { status: "missing" }
    | { status: "error" }
    | {
        status: "ok";
        name: string;
        mod: string;
        variants: EntityVariant[];
        groups: VariantGroup[];
        merged: MergedRow[];
        butchery: Recipe[];
      };
  let page = $state<Loaded>({ status: "loading" });

  $effect(() => {
    const wanted = code;
    page = { status: "loading" };
    Promise.all([data.entities(), data.ready()])
      .then(async ([index]: [EntityIndex, void]) => {
        const i = indexOfSorted(index.codes, wanted);
        if (i < 0) return { status: "missing" } as const;
        // The data keeps item order; a reader scans by name.
        const name = (item: number) => data.ref(item)?.name ?? "";
        const byName = <T extends { item: number }>(rows: T[]) => [...rows].sort((a, b) => name(a.item).localeCompare(name(b.item), "en"));
        const variants = (await data.entity(i)).map((v) => ({ ...v, sources: byName(v.sources) }));
        // Older data has no recipe list per type.
        const butchery = await data.recipes(index.recipes?.[i] ?? []);
        return {
          status: "ok",
          name: index.names[i]!,
          mod: index.mod[i]!,
          variants,
          groups: variantGroups(variants),
          merged: byName(mergeVariants(variants)),
          butchery,
        } as const;
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
    if (page.status === "ok") document.title = `${page.name} · ${t.siteTitle}`;
  });

  /** The group the address picks; null shows every variant merged. */
  const selected = $derived(page.status === "ok" && variant ? (page.groups.find((g) => g.codes.includes(variant!)) ?? null) : null);

  /** Butchery records of the variants shown, each with the variants it shows (none: all). */
  const butchery = $derived.by(() => {
    if (page.status !== "ok") return [];
    const codes = selected !== null || page.groups.length === 1 ? (selected ?? page.groups[0]!).codes : null;
    return page.butchery
      .map((recipe) => ({ recipe, only: codes ? butcheryVariantsFor(recipe, codes) : [] }))
      .filter((b) => codes === null || b.only.length > 0);
  });

  const href = (v?: string) => formatRoute({ view: "entity", version: data.id, code, ...(v ? { variant: v } : {}) });
  const isTrade = (kind: string) => kind === "sells" || kind === "buys";

  function details(s: { tool?: string; note?: string }): string {
    return [s.tool ? `${t.tool.toLowerCase()}: ${s.tool}` : "", s.note && s.note !== "Harvested" ? s.note : ""].filter(Boolean).join("; ");
  }

  function stock(s: { extra?: Record<string, unknown> }): string {
    const q = s.extra?.stock as { avg: number; var?: number } | undefined;
    return q && typeof q.avg === "number" ? formatQuantity(q) : "";
  }
</script>

{#snippet heading(kind: string, note: string | undefined, id: string)}
  <h2 {id}>{kind === "behavior" ? note : t.entitySections[kind as keyof typeof t.entitySections]}</h2>
{/snippet}

{#if page.status === "loading"}
  <p class="muted">{t.loading}</p>
{:else if page.status === "error"}
  <p role="alert">{t.loadFailed}</p>
{:else if page.status === "missing"}
  <h1 class="code">{code}</h1>
  <p>{t.entityNotInVersion(code, data.id)}</p>
{:else}
  {@const total = page.variants.length}
  <article class="entity" data-entity={code}>
    <header class="head">
      <Icon {code} size={64} label={initials(page.name)} />
      <div>
        <h1>{page.name}</h1>
        <dl class="facts">
          <div><dt>{t.code}</dt><dd><code data-testid="entity-code">{code}</code></dd></div>
          <div><dt>{t.mod}</dt><dd data-testid="entity-mod">{meta.mods[page.mod]?.name ?? page.mod}</dd></div>
          {#if total > 1}<div><dt>{t.variantsLabel}</dt><dd>{total}</dd></div>{/if}
        </dl>
      </div>
    </header>

    {#if page.groups.length > 1}
      <nav aria-label={t.variantsLabel} class="variants" data-testid="variants">
        <a href={href()} aria-current={selected === null ? "page" : undefined}>{t.allVariants}</a>
        {#each page.groups as g (g.codes[0])}
          <a href={href(g.codes[0])} aria-current={selected === g ? "page" : undefined} title={g.codes.join("\n")} data-variant={g.codes[0]}>
            {groupLabel(g.names)}
          </a>
        {/each}
      </nav>
    {/if}

    {#if selected !== null || page.groups.length === 1}
      {@const group = selected ?? page.groups[0]!}
      {#if page.groups.length > 1}
        <p class="muted" data-testid="variant-codes">{group.codes.join(", ")}</p>
      {/if}
      {#each entitySections(group.sources) as section, si (si)}
        <section aria-labelledby="sec-{si}" data-section={section.kind}>
          {@render heading(section.kind, section.note, `sec-${si}`)}
          <div class="scroll">
            <table>
              <thead>
                {#if isTrade(section.kind)}
                  <tr><th scope="col">{t.item}</th><th scope="col">{t.perTrade}</th><th scope="col">{t.priceHeading}</th><th scope="col">{t.stock}</th></tr>
                {:else}
                  <tr><th scope="col">{t.item}</th><th scope="col">{t.quantity}</th><th scope="col"></th></tr>
                {/if}
              </thead>
              <tbody>
                {#each section.rows as s, i (i)}
                  {@const ref = data.ref(s.item)}
                  <tr>
                    <td>{#if ref}<ItemLink code={ref.code} {data} />{/if}</td>
                    <td>{formatQuantity(s.quantity)}</td>
                    {#if isTrade(section.kind)}
                      <td>{s.price !== undefined ? formatQuantity({ avg: s.price, var: s.extra?.priceVar as number | undefined }) : ""}</td>
                      <td>{stock(s)}</td>
                    {:else}
                      <td>{details(s)}</td>
                    {/if}
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </section>
      {/each}
    {:else}
      <p class="muted">{t.allVariantsHint(total)}</p>
      {#each entitySections<MergedRow & Placed>(page.merged) as section, si (si)}
        <section aria-labelledby="sec-{si}" data-section={section.kind}>
          {@render heading(section.kind, section.note, `sec-${si}`)}
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t.item}</th>
                  <th scope="col">{isTrade(section.kind) ? t.perTrade : t.quantity}</th>
                  {#if isTrade(section.kind)}<th scope="col">{t.priceHeading}</th>{/if}
                  <th scope="col">{t.variantsLabel}</th>
                  {#if !isTrade(section.kind)}<th scope="col"></th>{/if}
                </tr>
              </thead>
              <tbody>
                {#each section.rows as s, i (i)}
                  {@const ref = data.ref(s.item)}
                  <tr>
                    <td>{#if ref}<ItemLink code={ref.code} {data} />{/if}</td>
                    <td>{formatSpread(s.quantity)}</td>
                    {#if isTrade(section.kind)}<td>{formatSpread(s.price)}</td>{/if}
                    <td>{s.variants === total ? t.everyVariant : t.someVariants(s.variants, total)}</td>
                    {#if !isTrade(section.kind)}<td>{details(s)}</td>{/if}
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </section>
      {/each}
    {/if}

    {#if butchery.length > 0}
      <section aria-labelledby="sec-butchery" data-section="butchery">
        <h2 id="sec-butchery">{t.butcheryHeading}</h2>
        <p class="muted">{t.butcheryOnEntity}</p>
        <div class="cards">
          {#each butchery as b (b.recipe.id)}
            <RecipeCard
              recipe={b.recipe}
              type={meta.recipeTypes[b.recipe.type] ?? { name: t.butcheryHeading, shape: "butchery", count: 0 }}
              {data}
              only={b.only}
              modName={meta.mods[b.recipe.mod]?.name ?? b.recipe.mod}
            />
          {/each}
        </div>
      </section>
    {/if}
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
  .variants {
    display: flex;
    flex-wrap: wrap;
    gap: 0.4rem;
    margin: 1rem 0 0.5rem;
  }
  .variants a {
    padding: 0.2rem 0.6rem;
    border: 1px solid var(--border);
    border-radius: 999px;
    text-decoration: none;
    font-size: 0.9rem;
  }
  .variants a[aria-current="page"] {
    background: var(--surface-2);
    border-color: var(--link);
    font-weight: 600;
  }
  .scroll {
    overflow-x: auto;
  }
  .cards {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr));
    gap: 0.75rem;
  }
</style>
