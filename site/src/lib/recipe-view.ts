// Pure layout and formatting for the recipe renderers, kept out of the components so
// it can be tested without a DOM.
import type { ButcheryStage, Ingredient, Recipe, Source, Stack, Yield } from "./export.ts";

/** Grid cells, row by row: the index of the ingredient in each cell, or null when empty. */
export function gridCells(recipe: Recipe): (number | null)[][] {
  const grid = recipe.grid;
  if (!grid) return [];
  const byKey = new Map<string, number>();
  recipe.ingredients.forEach((ing, i) => {
    if (ing.key !== undefined) byKey.set(ing.key, i);
  });
  const rows: (number | null)[][] = [];
  for (let r = 0; r < grid.height; r++) {
    const line = grid.pattern[r] ?? "";
    const row: (number | null)[] = [];
    for (let c = 0; c < grid.width; c++) {
      const ch = line[c];
      row.push(ch === undefined || ch === "_" || ch === " " ? null : (byKey.get(ch) ?? null));
    }
    rows.push(row);
  }
  return rows;
}

/** Voxel layers as booleans, bottom layer first; rows as the data gives them. */
export function voxelLayers(recipe: Recipe): boolean[][][] {
  return (recipe.voxels ?? []).map((layer) => layer.map((row) => [...row].map((ch) => ch === "#")));
}

export function formatNumber(n: number): string {
  return Number.isInteger(n) ? String(n) : String(Math.round(n * 100) / 100);
}

/** A drop or trade quantity: "2", or "7 ± 2.5" when it varies. */
export function formatQuantity(q: { avg: number; var?: number } | undefined): string {
  if (!q) return "";
  return q.var ? `${formatNumber(q.avg)} ± ${formatNumber(q.var)}` : formatNumber(q.avg);
}

/** "88–92%" for an alloy share given as fractions. */
export function formatRatio(min: number | undefined, max: number | undefined): string {
  const pct = (x: number) => formatNumber(Math.round(x * 1000) / 10);
  if (min === undefined && max === undefined) return "";
  if (min === undefined) return `up to ${pct(max!)}%`;
  if (max === undefined || min === max) return `${pct(min)}%`;
  return `${pct(min)}–${pct(max)}%`;
}

/** Amount shown on a slot: litres for liquids, otherwise a count above one. */
export function stackAmount(stack: Pick<Stack, "quantity" | "litres">): string {
  if (stack.litres !== undefined) return `${formatNumber(stack.litres)} L`;
  return stack.quantity !== 1 ? `×${formatNumber(stack.quantity)}` : "";
}

export function formatRange(min: number | undefined, max: number | undefined): string {
  if (min === undefined && max === undefined) return "";
  if (min === max || max === undefined) return formatNumber(min ?? 0);
  return `${formatNumber(min ?? 0)}–${formatNumber(max)}`;
}

export type Focus = { code: string; as: "ingredient" | "output" | "entity" } | null;

/**
 * Variant indices to cycle through. On an item's page a recipe shows the variants that
 * involve that item, as the in-game handbook does; when none name it (a pattern the
 * exporter did not resolve), all of them.
 */
export function focusVariants(recipe: Recipe, focus: Focus): number[] {
  const all = recipe.variants.map((_, i) => i);
  if (!focus) return all;
  const hits = all.filter((i) => {
    const v = recipe.variants[i]!;
    if (focus.as === "entity") return recipe.butchery?.variants[i]?.entities.some((e) => e.code === focus.code) ?? false;
    return focus.as === "output"
      ? v.outputs.some((s) => s.code === focus.code)
      : v.ingredients.some((slot) => slot.some((s) => s.code === focus.code));
  });
  return hits.length > 0 ? hits : all;
}

/** The stack a cycling slot shows at `tick`. */
export function cycleAt<T>(list: readonly T[], tick: number): T | undefined {
  if (list.length === 0) return undefined;
  return list[((tick % list.length) + list.length) % list.length];
}

/** Stacks accepted by one ingredient slot in a variant, falling back to the definition. */
export function slotStacks(recipe: Recipe, variant: number, slot: number): Stack[] {
  const stacks = recipe.variants[variant]?.ingredients[slot];
  const ing = recipe.ingredients[slot];
  if (stacks && stacks.length > 0) {
    // Litres are a property of the slot; a resolved stack may leave them out.
    const litres = ing?.litres;
    return litres === undefined ? stacks : stacks.map((s) => (s.litres === undefined ? { ...s, litres } : s));
  }
  if (!ing) return [];
  const s: Stack = { code: ing.code, kind: ing.kind, quantity: ing.quantity };
  if (ing.litres !== undefined) s.litres = ing.litres;
  return [s];
}

export function variantOutputs(recipe: Recipe, variant: number): Stack[] {
  const outs = recipe.variants[variant]?.outputs;
  if (outs && outs.length > 0) return outs;
  return recipe.outputs.map((o) => {
    const s: Stack = { code: o.code, kind: o.kind, quantity: o.quantity };
    if (o.litres !== undefined) s.litres = o.litres;
    return s;
  });
}

/** A source row, standing for every block in `from` (first) and `alsoFrom`. */
export interface SourceRow extends Source {
  /** The sources of the other variants of the block that the row stands for, when there are any. */
  alsoFrom?: Source[];
  /** Panning only: the lowest and highest chance per pan of the blocks in the row. */
  chance?: { min: number; max: number };
}

/** The `note` of a panning source. */
export const PANNED = "Panned";

const isPanned = (s: Source) => s.type === "other" && s.note === PANNED;

/**
 * The chance that one pan of the block gives the item. A pan gives at most one item: the
 * game rolls the block's drops in a random order and stops at the first hit, so the
 * exporter's `extra.chancePerPan` is below the declared chance in `quantity.avg`, which
 * is the fallback for an export without it.
 */
export function panChance(s: Source): number | undefined {
  const c = s.extra?.chancePerPan;
  return typeof c === "number" ? c : s.quantity?.avg;
}

/** "game:richgravel" for game:richgravel-granite: the block type, without its variant. */
function blockType(code: string): string {
  const dash = code.indexOf("-", code.indexOf(":") + 1);
  return dash < 0 ? code : code.slice(0, dash);
}

/**
 * A block's panning list may hold the item twice (Wilderlands Panning keeps vanilla's stone
 * in the gravel list and adds its own). Each entry rolls, so the block's chances add up.
 */
function mergePanned(sources: readonly Source[]): Source[] {
  const merged = new Map<string, Source>();
  return sources.flatMap((s) => {
    if (!isPanned(s)) return [s];
    const { chancePerPan: _chance, ...extra } = s.extra ?? {};
    const key = JSON.stringify([s.from, extra, s.tool, s.price]);
    const first = merged.get(key);
    if (!first) {
      const copy = { ...s };
      merged.set(key, copy);
      return [copy];
    }
    const chance = (panChance(first) ?? 0) + (panChance(s) ?? 0);
    first.extra = { ...first.extra, chancePerPan: chance };
    return [];
  });
}

/**
 * Sources without the rows that would read the same. A block's four orientations each
 * drop the item, and all four are called "Aged torch holder": one row. Sources of type
 * `other` (panning, harvesting) also fold the variants of one block type into one row
 * when everything else is equal. Panning folds them even when the chances differ, and the
 * row has their range: flint pans from 38 gravels and from 29 rich gravels whose chance
 * depends on the rock, and that is two rows, not dozens.
 */
export function sourceRows(sources: readonly Source[]): SourceRow[] {
  const rows = new Map<string, SourceRow>();
  const names = new Map<SourceRow, Set<string>>();
  for (const s of mergePanned(sources)) {
    const name = s.fromName ?? s.from;
    let key: string;
    if (isPanned(s)) {
      const { chancePerPan: _chance, ...extra } = s.extra ?? {};
      key = JSON.stringify([s.type, s.note, blockType(s.from), extra, s.tool, s.price]);
    } else {
      const where = s.type === "other" ? [blockType(s.from), s.extra ?? null] : name;
      key = JSON.stringify([s.type, where, s.quantity?.avg, s.quantity?.var, s.tool, s.price, s.note]);
    }
    const chance = isPanned(s) ? panChance(s) : undefined;
    let row = rows.get(key);
    if (!row) {
      rows.set(key, (row = { ...s }));
      names.set(row, new Set([name]));
      if (chance !== undefined) row.chance = { min: chance, max: chance };
      continue;
    }
    const seen = names.get(row)!;
    if (seen.has(name)) continue;
    seen.add(name);
    (row.alsoFrom ??= []).push(s);
    if (chance !== undefined) {
      row.chance = row.chance ? { min: Math.min(row.chance.min, chance), max: Math.max(row.chance.max, chance) } : { min: chance, max: chance };
    }
  }
  return [...rows.values()];
}

/** A chance as a percentage: "30%", "1.25%", "0.06%", or a range "4.9–6.98%". */
export function formatChance(min: number | undefined, max: number = min ?? 0): string {
  if (min === undefined) return "";
  const pct = (x: number) => {
    const p = x * 100;
    // Two decimals hide the rare drops, which are the ones worth knowing.
    return p >= 1 ? formatNumber(p) : String(Number(p.toPrecision(2)));
  };
  return min === max ? `${pct(min)}%` : `${pct(min)}–${pct(max)}%`;
}

/** Stages of a block built in place, in build order; the first is the block as placed. */
export function constructionStages(recipe: Recipe): { ingredients: number[]; action?: string }[] {
  return recipe.construction?.stages ?? [];
}

/**
 * What a whole build consumes: slots accepting the same stacks are added up, in order of
 * first use. The stacks carry the summed quantity.
 */
export function constructionTotals(recipe: Recipe, variant: number): Stack[][] {
  const totals = new Map<string, Stack[]>();
  recipe.ingredients.forEach((ing, i) => {
    const stacks = slotStacks(recipe, variant, i);
    if (stacks.length === 0) return;
    const key = JSON.stringify(stacks.map((s) => [s.code, s.litres ?? null]));
    const seen = totals.get(key);
    if (seen) {
      totals.set(key, seen.map((s) => ({ ...s, quantity: s.quantity + ing.quantity })));
    } else {
      totals.set(key, stacks.map((s) => ({ ...s, quantity: ing.quantity })));
    }
  });
  return [...totals.values()];
}

/** Stages of a creature's butchery in the order a player goes through them; `harvest` is the alternative to the rest. */
export function butcheryStages(recipe: Recipe): ButcheryStage[] {
  return recipe.butchery?.stages ?? [];
}

/**
 * One output of a butchery variant: its stacks (a carcass can come in several coats) and
 * its yield. Null when the variant does not give it.
 */
export function butcheryOutput(recipe: Recipe, variant: number, output: number): { stacks: Stack[]; yield: Yield } | null {
  const y = recipe.butchery?.variants[variant]?.yields[output];
  const o = recipe.outputs[output];
  if (!y || !o) return null;
  const codes = [o.code, ...((o.extra?.alternatives as string[] | undefined) ?? [])];
  return {
    stacks: codes.map((code) => ({ code, kind: o.kind, quantity: y.avg, ...(o.litres !== undefined ? { litres: o.litres } : {}) })),
    yield: y,
  };
}

/** The creatures a butchery variant covers. */
export function butcheryEntities(recipe: Recipe, variant: number): { code: string; name: string }[] {
  return (recipe.butchery?.variants[variant]?.entities ?? []).map((e) => ({ code: e.code, name: e.name || e.code }));
}

/** Indices of the butchery variants that cover one of `codes`, for a creature page showing some variants. */
export function butcheryVariantsFor(recipe: Recipe, codes: readonly string[]): number[] {
  return (recipe.butchery?.variants ?? []).flatMap((v, i) => (v.entities.some((e) => codes.includes(e.code)) ? [i] : []));
}

/** The lowest and highest loot multiplier of a station slot, from its `extra.efficiency`. */
export function efficiencyRange(ing: Ingredient | undefined): { min: number; max: number } | null {
  const values = Object.values((ing?.extra?.efficiency as Record<string, number> | undefined) ?? {});
  if (values.length === 0) return null;
  return { min: Math.min(...values), max: Math.max(...values) };
}

/**
 * What heads a recipe card: its outputs. A butchery record gives a dozen things over
 * several stages, so its card is headed by the carcass its first stage gives.
 */
export function cardOutputs(recipe: Recipe, variant: number): Stack[] {
  const first = recipe.butchery?.stages[0];
  if (first) return first.outputs.flatMap((o) => butcheryOutput(recipe, variant, o)?.stacks.slice(0, 1) ?? []);
  return variantOutputs(recipe, variant);
}
