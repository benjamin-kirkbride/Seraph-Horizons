<script lang="ts">
  import { iconPath } from "../lib/icons.ts";
  import { icons } from "../lib/state.svelte.ts";

  let { code, size = 32, label = "" }: { code: string; size?: number; label?: string } = $props();

  const src = $derived(iconPath(icons.index, code));
  let failed = $state<string | null>(null);
</script>

{#if src && failed !== src}
  <img
    class="icon"
    {src}
    alt=""
    width={size}
    height={size}
    loading="lazy"
    data-icon={code}
    onerror={() => (failed = src)}
  />
{:else}
  <!-- Without icons a slot still needs something to tell its neighbours apart. -->
  <span class="icon placeholder" style:width="{size}px" style:height="{size}px" style:font-size="{Math.round(size * 0.4)}px" data-icon-placeholder={code} aria-hidden="true"
    >{label}</span
  >
{/if}

<style>
  .icon {
    display: inline-block;
    flex: none;
    image-rendering: auto;
  }
  .placeholder {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border-radius: 4px;
    background: var(--surface-2);
    border: 1px solid var(--border);
    color: var(--muted);
    font-weight: 600;
    line-height: 1;
    overflow: hidden;
  }
</style>
