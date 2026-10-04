<script lang="ts">
  // Text with an explanation, dotted like an abbreviation. The explanation shows while the
  // pointer is over it, it has keyboard focus or it is tapped; Escape hides it, and a screen
  // reader reads it as the text's description. A title attribute only ever showed to a mouse.
  import type { Snippet } from "svelte";

  let { text, lines = [], children }: { text: string; lines?: string[]; children: Snippet } = $props();

  const id = $props.id();
  let open = $state(false);
  let anchor = $state<HTMLElement>();
  let tip = $state<HTMLElement>();
  let left = $state(0);
  let top = $state(0);

  // Fixed to the window, so a table that scrolls sideways does not clip it, and kept
  // within the window's width.
  $effect(() => {
    if (!open || !anchor || !tip) return;
    const place = () => {
      const a = anchor!.getBoundingClientRect();
      const width = document.documentElement.clientWidth;
      left = Math.max(8, Math.min(a.left, width - 8 - tip!.offsetWidth));
      top = a.bottom + 4;
    };
    place();
    window.addEventListener("scroll", place, true);
    window.addEventListener("resize", place);
    return () => {
      window.removeEventListener("scroll", place, true);
      window.removeEventListener("resize", place);
    };
  });
</script>

<span class="wrap" role="presentation" onmouseenter={() => (open = true)} onmouseleave={() => (open = false)}>
  <button
    type="button"
    class="hint"
    aria-describedby={id}
    bind:this={anchor}
    onclick={() => (open = true)}
    onfocus={() => (open = true)}
    onblur={() => (open = false)}
    onkeydown={(e) => {
      if (e.key === "Escape") open = false;
    }}>{@render children()}</button
  ><span class="tip" role="tooltip" {id} hidden={!open} bind:this={tip} style:left="{left}px" style:top="{top}px"
    >{text}{#if lines.length > 0}<span class="lines">{#each lines as line, i (i)}<span>{line}</span>{/each}</span>{/if}</span
  >
</span>

<style>
  /* A button only so that it takes focus; it reads as the text it is. */
  .hint {
    display: inline;
    padding: 0;
    border: 0;
    border-radius: 0;
    background: none;
    font: inherit;
    color: inherit;
    text-align: inherit;
    text-decoration: underline dotted;
    cursor: help;
  }
  .hint:focus-visible {
    outline: 2px solid var(--link);
    outline-offset: 1px;
  }
  .tip {
    position: fixed;
    z-index: 10;
    display: block;
    width: max-content;
    max-width: min(20rem, calc(100vw - 16px));
    padding: 0.4rem 0.55rem;
    background: var(--surface);
    color: var(--text);
    border: 1px solid var(--border);
    border-radius: 4px;
    box-shadow: 0 2px 8px rgb(0 0 0 / 0.2);
    font-size: 0.85rem;
    font-weight: normal;
    line-height: 1.35;
    text-align: left;
    white-space: normal;
  }
  .tip[hidden] {
    display: none;
  }
  .lines {
    display: flex;
    flex-direction: column;
    margin-top: 0.25rem;
  }
</style>
