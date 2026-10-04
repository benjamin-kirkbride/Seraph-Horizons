<script lang="ts">
  // One slot of a recipe. A slot that accepts several stacks cycles through them on the
  // shared clock tick its card passes in. Notes passed as children (a tool's durability, a
  // yield) stack under the name, beside the icon: after the name, they wrapped under the
  // icon whenever a longer name cycled in.
  import type { Snippet } from "svelte";
  import type { Stack } from "../lib/export.ts";
  import type { VersionData } from "../lib/data.ts";
  import { formatRoute } from "../lib/route.ts";
  import { cycleAt, stackAmount } from "../lib/recipe-view.ts";
  import { t } from "../lib/strings.ts";
  import Icon from "./Icon.svelte";

  let {
    stacks,
    tick,
    data,
    tool = false,
    showName = false,
    mark,
    children,
  }: { stacks: Stack[]; tick: number; data: VersionData; tool?: boolean; showName?: boolean; mark?: string; children?: Snippet } = $props();

  const stack = $derived(cycleAt(stacks, tick));
  const name = $derived(stack ? (stack.name ?? data.nameOf(stack.code)) : "");
  const initials = $derived(
    name
      .split(/[\s()-]+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0]!.toUpperCase())
      .join(""),
  );
  const amount = $derived(stack ? stackAmount(stack) : "");
  const label = $derived(
    stack ? [name, amount, tool ? t.tool.toLowerCase() : "", stacks.length > 1 ? `1 of ${stacks.length} accepted` : ""].filter(Boolean).join(", ") : t.nothingMatches,
  );
</script>

{#snippet slot()}
{#if stack}
  <a
    class="slot"
    class:tool
    class:named={showName}
    href={formatRoute({ view: "item", version: data.id, code: stack.code })}
    title={amount ? `${name} (${amount})` : name}
    aria-label={label}
    data-code={stack.code}
    data-amount={amount}
    data-cycling={stacks.length > 1 ? stacks.length : undefined}
  >
    <span class="frame">
      <Icon code={stack.code} label={mark ?? initials} bare />
      {#if amount}<span class="amount" aria-hidden="true">{amount}</span>{/if}
      {#if tool}<span class="toolmark" aria-hidden="true" title={t.tool}>T</span>{/if}
    </span>
    {#if showName}<span class="name">{name}</span>{/if}
  </a>
{:else}
  <span class="slot missing" aria-label={label} title={t.nothingMatches}><span class="frame">?</span></span>
{/if}
{/snippet}

{#if children}
  <span class="noted">
    {@render slot()}
    <span class="notes">{@render children()}</span>
  </span>
{:else}
  {@render slot()}
{/if}

<style>
  .slot {
    display: inline-flex;
    align-items: center;
    gap: 0.5rem;
    text-decoration: none;
    color: inherit;
    min-width: 0;
  }
  .frame {
    position: relative;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex: none;
    width: var(--slot);
    height: var(--slot);
    background: var(--icon-fill);
    border: 1px solid var(--icon-border);
  }
  /* The link colour stands out from both the light frame and the card around it. */
  .slot:hover .frame {
    border-color: var(--link);
  }
  .tool .frame {
    border-style: dashed;
    border-color: var(--accent);
  }
  .amount {
    position: absolute;
    right: 1px;
    bottom: 0;
    font-size: 0.72rem;
    font-weight: 700;
    line-height: 1;
    padding: 1px 2px;
    background: var(--surface);
    border-radius: 3px;
  }
  .toolmark {
    position: absolute;
    left: 1px;
    top: 1px;
    font-size: 0.65rem;
    font-weight: 700;
    line-height: 1;
    padding: 1px 3px;
    border-radius: 3px;
    background: var(--accent);
    color: var(--accent-text);
  }
  .name {
    overflow-wrap: anywhere;
  }
  .named:hover .name {
    text-decoration: underline;
  }
  /* The icon leaves the flow so that the name and the notes under it share one column
     beside it, whatever the name's length. */
  .noted {
    position: relative;
    display: flex;
    flex-direction: column;
    justify-content: center;
    min-height: var(--slot);
    min-width: 0;
    padding-left: calc(var(--slot) + 0.5rem);
  }
  .noted .slot {
    display: block;
  }
  .noted .frame {
    position: absolute;
    left: 0;
    top: 0;
  }
  .notes {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    font-size: 0.85rem;
    color: var(--muted);
  }
  .missing .frame {
    color: var(--icon-text);
  }
</style>
