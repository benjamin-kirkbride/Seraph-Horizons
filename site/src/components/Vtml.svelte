<script lang="ts">
  // Builds DOM from the parsed VTML tree. Only elements named here can come out; the
  // data never reaches innerHTML. Font colours are not applied: the game picks them for
  // its dark tooltips and many are unreadable on a light page.
  import type { VNode } from "../lib/vtml.ts";
  import Vtml from "./Vtml.svelte";

  let { nodes, itemHref }: { nodes: VNode[]; itemHref: (code: string) => string } = $props();
</script>

{#each nodes as n, i (i)}
  {#if n.t === "text"}{n.text}{:else if n.t === "br"}<br />{:else if n.t === "b"}<strong><Vtml nodes={n.children} {itemHref} /></strong
    >{:else if n.t === "i"}<em><Vtml nodes={n.children} {itemHref} /></em>{:else if n.t === "link"}<a
      href={n.href}
      rel="noopener noreferrer nofollow"
      target="_blank"><Vtml nodes={n.children} {itemHref} /></a
    >{:else if n.t === "item"}<a href={itemHref(n.code)}><Vtml nodes={n.children} {itemHref} /></a>{:else if n.t === "color"}<span
      data-color={n.color}><Vtml nodes={n.children} {itemHref} /></span
    >{/if}
{/each}
