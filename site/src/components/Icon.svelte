<script lang="ts">
  import { iconPath } from "../lib/icons.ts";
  import { icons } from "../lib/state.svelte.ts";

  // A recipe slot draws its own frame, so the icon inside it goes bare.
  let { code, size = 32, label = "", bare = false }: { code: string; size?: number; label?: string; bare?: boolean } = $props();

  const src = $derived(iconPath(icons.index, code));
  let failed = $state<string | null>(null);
</script>

{#if src && failed !== src}
  <img
    class="icon"
    class:bare
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
  <span
    class="icon placeholder"
    class:bare
    style:width="{size}px"
    style:height="{size}px"
    style:font-size="{Math.round(size * 0.4)}px"
    data-icon-placeholder={code}
    aria-hidden="true">{label}</span
  >
{/if}

<style>
  .icon {
    display: inline-block;
    flex: none;
    image-rendering: auto;
    border-radius: 4px;
    background: var(--icon-bg);
    /* A shadow rather than a border, so the image keeps its full size. */
    box-shadow:
      inset 0 0 0 1px var(--icon-border),
      var(--icon-inset);
  }
  .icon.bare {
    background: none;
    box-shadow: none;
  }
  .placeholder {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    color: var(--icon-text);
    font-weight: 600;
    line-height: 1;
    overflow: hidden;
  }
</style>
