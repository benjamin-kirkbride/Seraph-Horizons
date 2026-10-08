<script lang="ts">
  // A value in rusty gears: the gear's icon, then the number. The word is for screen readers
  // only. A value under a gear per full stack (floorZero) is dimmed, with the reason on hover.
  // A liquid's value is per litre: "/ L" follows the number, and the hidden word and the hover
  // say "rusty gears per litre".
  import { formatGears, GEAR } from "../lib/values.ts";
  import { t } from "../lib/strings.ts";
  import Icon from "./Icon.svelte";

  let {
    value,
    floorZero = false,
    perLitre = false,
    size = 20,
    title,
  }: { value: number; floorZero?: boolean; perLitre?: boolean; size?: number; title?: string } = $props();

  const hint = $derived(
    [perLitre ? t.perLitreHint : "", floorZero ? t.floorZeroHint : "", title ?? ""].filter(Boolean).join(" ") || undefined,
  );
</script>

<span
  class="gears"
  class:floor={floorZero}
  title={hint}
  data-value={value}
  data-floor-zero={floorZero ? "" : undefined}
  data-per-litre={perLitre ? "" : undefined}
  ><Icon code={GEAR} {size} label="RG" /><span class="n">{formatGears(value)}</span
  >{#if perLitre}<span class="unit" aria-hidden="true">{t.perLitre}</span>{/if}<span class="visually-hidden"
    >{` ${perLitre ? t.gearsPerLitre : t.gears}`}</span
  ></span
>

<style>
  .gears {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    white-space: nowrap;
    font-variant-numeric: tabular-nums;
  }
  .floor .n,
  .unit {
    color: var(--muted);
  }
</style>
