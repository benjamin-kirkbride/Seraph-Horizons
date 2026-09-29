<script lang="ts">
  import type { VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import Icon from "./Icon.svelte";

  let { code, data, label }: { code: string; data: VersionData; label?: string } = $props();
</script>

<a class="item-link" href={formatRoute({ view: "item", version: data.id, code })}>
  <Icon {code} size={24} />
  <span>{label ?? data.nameOf(code)}</span>
</a>

<style>
  .item-link {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
  }
</style>
