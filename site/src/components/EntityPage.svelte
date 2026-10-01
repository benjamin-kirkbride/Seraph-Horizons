<script lang="ts">
  import type { EntityIndex, EntitySource, Meta } from "../lib/format.ts";
  import type { VersionData } from "../lib/data.ts";
  import { entitySections, type Section } from "../lib/entity-view.ts";
  import { formatQuantity } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import { initials } from "../lib/icons.ts";
  import { indexOfSorted } from "../lib/wildcard.ts";
  import Icon from "./Icon.svelte";
  import ItemLink from "./ItemLink.svelte";

  let { data, meta, code }: { data: VersionData; meta: Meta; code: string } = $props();

  type Loaded =
    | { status: "loading" }
    | { status: "missing" }
    | { status: "error" }
    | { status: "ok"; name: string; mod: string; sections: Section[] };
  let page = $state<Loaded>({ status: "loading" });

  $effect(() => {
    const wanted = code;
    page = { status: "loading" };
    Promise.all([data.entities(), data.ready()])
      .then(async ([index]: [EntityIndex, void]) => {
        const i = indexOfSorted(index.codes, wanted);
        if (i < 0) return { status: "missing" } as const;
        // The data keeps item order; a reader scans by name.
        const name = (s: EntitySource) => data.ref(s.item)?.name ?? "";
        const sources = [...(await data.entity(i))].sort((a, b) => name(a).localeCompare(name(b), "en"));
        return { status: "ok", name: index.names[i]!, mod: index.mod[i]!, sections: entitySections(sources) } as const;
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

  const isTrade = (s: Section) => s.kind === "sells" || s.kind === "buys";

  function details(s: EntitySource): string {
    return [s.tool ? `${t.tool.toLowerCase()}: ${s.tool}` : "", s.note && s.note !== "Harvested" ? s.note : ""].filter(Boolean).join("; ");
  }

  function stock(s: EntitySource): string {
    const q = s.extra?.stock as { avg: number; var?: number } | undefined;
    return q && typeof q.avg === "number" ? formatQuantity(q) : "";
  }
</script>

{#if page.status === "loading"}
  <p class="muted">{t.loading}</p>
{:else if page.status === "error"}
  <p role="alert">{t.loadFailed}</p>
{:else if page.status === "missing"}
  <h1 class="code">{code}</h1>
  <p>{t.entityNotInVersion(code, data.id)}</p>
{:else}
  <article class="entity" data-entity={code}>
    <header class="head">
      <Icon {code} size={64} label={initials(page.name)} />
      <div>
        <h1>{page.name}</h1>
        <dl class="facts">
          <div><dt>{t.code}</dt><dd><code data-testid="entity-code">{code}</code></dd></div>
          <div><dt>{t.mod}</dt><dd data-testid="entity-mod">{meta.mods[page.mod]?.name ?? page.mod}</dd></div>
        </dl>
      </div>
    </header>

    {#each page.sections as section, si (si)}
      {@const heading = section.kind === "behavior" ? section.note! : t.entitySections[section.kind]}
      <section aria-labelledby="sec-{si}" data-section={section.kind}>
        <h2 id="sec-{si}">{heading}</h2>
        <div class="scroll">
          <table class="sources">
            <thead>
              {#if isTrade(section)}
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
                  {#if isTrade(section)}
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
  .scroll {
    overflow-x: auto;
  }
</style>
