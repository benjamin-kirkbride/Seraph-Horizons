<script lang="ts">
  import type { VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import { initials } from "../lib/icons.ts";
  import Icon from "./Icon.svelte";

  let { code, data, label }: { code: string; data: VersionData; label?: string } = $props();
</script>

<a class="item-link" href={formatRoute({ view: "item", version: data.id, code })}>
  <Icon {code} size={24} label={initials(label ?? data.nameOf(code))} />
  <span class="name">{label ?? data.nameOf(code)}</span>
</a>

<style>
  .item-link {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    /* On the link itself the underline would also run through an icon's placeholder letters. */
    text-decoration: none;
  }
  .name {
    text-decoration: underline;
  }
</style>
