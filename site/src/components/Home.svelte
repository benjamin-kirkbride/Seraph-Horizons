<script lang="ts">
  import type { Meta } from "../lib/format.ts";
  import { formatRoute } from "../lib/route.ts";
  import { t } from "../lib/strings.ts";

  let { meta, version }: { meta: Meta; version: string } = $props();

  const types = $derived(Object.entries(meta.recipeTypes).sort((a, b) => b[1].count - a[1].count));
</script>

<h1>{t.siteTitle}</h1>
<p>{t.homeIntro(meta.itemCount, meta.recipeCount, meta.pack.version, meta.pack.gameVersion)}</p>
<p>{t.homeHint}</p>
{#if meta.entityCount > 0}
  <p><a href={formatRoute({ view: "entities", version })}>{t.entitiesHomeLink(meta.entityCount)}</a></p>
{/if}
<h2>{t.recipeTypesHeading}</h2>
<ul class="types">
  {#each types as [code, info] (code)}
    <li><a href={formatRoute({ view: "type", version, code })}>{info.name}</a> <span class="muted">{info.count.toLocaleString("en")}</span></li>
  {/each}
</ul>

<style>
  .types {
    list-style: none;
    padding: 0;
    columns: 14rem;
  }
  li {
    display: flex;
    justify-content: space-between;
    gap: 1rem;
    max-width: 14rem;
    break-inside: avoid;
  }
</style>
