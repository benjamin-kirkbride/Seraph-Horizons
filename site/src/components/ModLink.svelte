<script lang="ts">
  // A mod's name, linked to its ModDB page (see modLink) in a new tab.
  import type { Mod } from "../lib/export.ts";
  import { modLink } from "../lib/credits.ts";

  let { id, mods }: { id: string; mods: Record<string, Pick<Mod, "name">> } = $props();

  const href = $derived(modLink(id));
  const name = $derived(mods[id]?.name || id);
</script>

{#if href}<a class="mod-link" {href} rel="noopener noreferrer" target="_blank" data-mod={id}>{name}</a>{:else}<span data-mod={id}>{name}</span>{/if}
