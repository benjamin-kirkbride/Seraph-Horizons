<script lang="ts">
  import type { Meta } from "../lib/format.ts";
  import { t } from "../lib/strings.ts";

  let { meta }: { meta: Meta } = $props();

  const types = $derived(Object.entries(meta.recipeTypes).sort((a, b) => b[1].count - a[1].count));
</script>

<h1>{t.siteTitle}</h1>
<p>{t.homeIntro(meta.itemCount, meta.recipeCount, meta.pack.version, meta.pack.gameVersion)}</p>
<p>{t.homeHint}</p>
<h2>{t.recipeTypesHeading}</h2>
<ul class="types">
  {#each types as [code, info] (code)}
    <li><span>{info.name}</span> <span class="muted">{info.count.toLocaleString("en")}</span></li>
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
