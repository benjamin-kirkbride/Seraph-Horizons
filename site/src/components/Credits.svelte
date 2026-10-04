<script lang="ts">
  import type { Meta } from "../lib/format.ts";
  import { creditRows } from "../lib/credits.ts";
  import { NEW_ISSUE_URL, REPO_URL, t } from "../lib/strings.ts";

  let { meta }: { meta: Meta } = $props();

  const rows = $derived(creditRows(meta.mods));
</script>

<h1>{t.credits}</h1>
<p>{t.creditsIntro}</p>
<ul class="mods" data-testid="credits">
  {#each rows as m (m.id)}
    <li data-mod={m.id}>
      {#if m.link}<a href={m.link} rel="noopener noreferrer" target="_blank" data-link="mod">{m.name}</a>{:else}{m.name}{/if}
      <span class="muted">{m.version}</span>
      <span class="authors">{m.authors.length > 0 ? `${t.by} ${m.authors.join(", ")}` : t.unknownAuthors}</span>
      {#if m.website}<a class="website" href={m.website} rel="noopener noreferrer" target="_blank" data-link="website">{t.website}</a>{/if}
    </li>
  {/each}
</ul>

<h2 id="about">{t.about}</h2>
<p>{t.aboutText}</p>

<h2 id="removal">{t.removal}</h2>
<p>{t.removalText} <a href={NEW_ISSUE_URL} rel="noopener noreferrer" target="_blank" data-testid="removal-link">{t.removalLink}</a>.</p>
<p><a href={REPO_URL} rel="noopener noreferrer" target="_blank">{t.sourceCode}</a></p>

<style>
  .mods {
    padding-left: 1.2rem;
  }
  .mods li {
    margin-bottom: 0.2rem;
  }
  .authors {
    margin-left: 0.3rem;
  }
  .website {
    margin-left: 0.3rem;
    font-size: 0.85rem;
  }
</style>
