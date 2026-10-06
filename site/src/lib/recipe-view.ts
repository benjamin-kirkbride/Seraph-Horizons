// Pure layout and formatting for the recipe renderers, kept out of the components so
// it can be tested without a DOM.
import type { ButcheryStage, Hours, Ingredient, Recipe, Source, Stack, Transition, Yield } from "./export.ts";
import type { GivenItem, TypeInfo } from "./format.ts";
import { t } from "./strings.ts";

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

/** A source, or one turned around (GivenItem). */
type How = Source | GivenItem;

const isPanned = (s: How) => s.type === "other" && s.note === PANNED;

/**
 * The chance that one pan of the block gives the item. A pan gives at most one item: the
 * game rolls the block's drops in a random order and stops at the first hit, so the
 * exporter's `extra.chancePerPan` is below the declared chance in `quantity.avg`, which
 * is the fallback for an export without it.
 */
export function panChance(s: How): number | undefined {
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
function mergePanned<T extends How>(sources: readonly T[], pair: (s: T) => unknown): T[] {
  const merged = new Map<string, T>();
  return sources.flatMap((s) => {
    if (!isPanned(s)) return [s];
    const { chancePerPan: _chance, ...extra } = s.extra ?? {};
    const key = JSON.stringify([pair(s), extra, s.tool, s.price]);
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
  for (const s of mergePanned(sources, (s) => s.from)) {
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

/** One row of what a block gives. */
export type GiveRow = GivenItem & {
  /** Panning only: the chance that one pan gives the item. */
  chance?: number;
};

/**
 * What a block gives, for its page: rows that read the same once, and what panning gives
 * last, likeliest first.
 */
export function giveRows(gives: readonly GivenItem[]): GiveRow[] {
  const seen = new Set<string>();
  const rows: GiveRow[] = [];
  for (const g of mergePanned(gives, (g) => g.item)) {
    const key = JSON.stringify(g);
    if (seen.has(key)) continue;
    seen.add(key);
    const chance = isPanned(g) ? panChance(g) : undefined;
    rows.push(chance === undefined ? g : { ...g, chance });
  }
  const panned = rows.filter(isPanned).sort((a, b) => (b.chance ?? 0) - (a.chance ?? 0));
  return [...rows.filter((r) => !isPanned(r)), ...panned];
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
 * The stations the stage giving a butchery output can use, by name with their loot
 * multiplier, highest first: skinning names the hooks, butchering the tables. Blocks of
 * one name and multiplier (a hook in each metal) are one entry.
 */
export function butcheryStations(recipe: Recipe, output: number, nameOf: (code: string) => string): { name: string; multiplier: number }[] {
  const stage = recipe.butchery?.stages.find((s) => s.outputs.includes(output));
  if (!stage) return [];
  const seen = new Set<string>();
  const out: { name: string; multiplier: number }[] = [];
  for (const i of [...stage.ingredients, ...(stage.options ?? []).flat()]) {
    for (const [code, multiplier] of Object.entries((recipe.ingredients[i]?.extra?.efficiency as Record<string, number> | undefined) ?? {})) {
      const name = nameOf(code);
      const key = `${name}|${multiplier}`;
      if (seen.has(key)) continue;
      seen.add(key);
      out.push({ name, multiplier });
    }
  }
  return out.sort((a, b) => b.multiplier - a.multiplier);
}

/**
 * What heads a recipe card: its outputs. A butchery record gives a dozen things over
 * several stages, so its card is headed by the carcass its first stage gives.
 */
export function cardOutputs(recipe: Recipe, variant: number): Stack[] {
  const first = recipe.butchery?.stages[0];
  if (first) return first.outputs.flatMap((o) => butcheryOutput(recipe, variant, o)?.stacks.slice(0, 1) ?? []);
  // A tub's lost gears are not what it makes; the renderer shows them.
  const failure = recipe.tub?.failure;
  if (failure !== undefined) return variantOutputs(recipe, variant).filter((_, i) => i !== failure);
  return variantOutputs(recipe, variant);
}

/** Recipes per page of a recipe type's page: four rows at the widest. */
export const TYPE_PAGE_SIZE = 24;

/**
 * Page `page` of a recipe type's page, clamped to the pages there are: the recipe indices
 * it shows and the position of its first recipe within the type.
 */
export function typePage(info: Pick<TypeInfo, "start" | "count">, page = 1, size = TYPE_PAGE_SIZE): { page: number; pages: number; first: number; indices: number[] } {
  const pages = Math.max(1, Math.ceil(info.count / size));
  const at = Math.min(Math.max(1, Math.floor(page)), pages);
  const first = (at - 1) * size;
  const n = Math.max(0, Math.min(size, info.count - first));
  return { page: at, pages, first, indices: Array.from({ length: n }, (_, i) => info.start + first + i) };
}

/**
 * The page numbers to link to from page `page` of `pages`: the first, the last and those
 * within `around` of it, with null for each gap. A gap of one page shows that page instead.
 */
export function pageLinks(page: number, pages: number, around = 2): (number | null)[] {
  const keep = new Set([1, pages]);
  for (let p = page - around; p <= page + around; p++) if (p >= 1 && p <= pages) keep.add(p);
  const sorted = [...keep].sort((a, b) => a - b);
  const out: (number | null)[] = [];
  sorted.forEach((p, i) => {
    const prev = sorted[i - 1];
    if (prev !== undefined && p - prev === 2) out.push(prev + 1);
    else if (prev !== undefined && p - prev > 2) out.push(null);
    out.push(p);
  });
  return out;
}

/**
 * When a transition starts and when it is done, in in-game hours, each as the lowest and
 * highest a stack can draw: a stack picks its own hours within average ± spread.
 */
export function transitionWindow(tr: Transition): { starts: [number, number]; takes: [number, number]; done: [number, number] } {
  const lo = (h: Hours) => Math.max(0, h.avg - Math.abs(h.var ?? 0));
  const hi = (h: Hours) => Math.max(0, h.avg + Math.abs(h.var ?? 0));
  const f = tr.freshHours;
  const d = tr.transitionHours;
  return { starts: [lo(f), hi(f)], takes: [lo(d), hi(d)], done: [lo(f) + lo(d), hi(f) + hi(d)] };
}

/** In-game time: minutes below an hour, hours below two days, then days. */
export function formatHours(h: number): string {
  const tenth = (x: number) => formatNumber(Math.round(x * 10) / 10);
  if (h < 1) {
    const m = Math.max(1, Math.round(h * 60));
    return `${m} minute${m === 1 ? "" : "s"}`;
  }
  if (h < 48) {
    const v = tenth(h);
    return `${v} hour${v === "1" ? "" : "s"}`;
  }
  return `${tenth(h / 24)} days`;
}

/** "2 days", or "2 days to 3 days" when stacks differ. */
export function formatHoursRange([lo, hi]: [number, number]): string {
  const a = formatHours(lo);
  const b = formatHours(hi);
  return a === b ? a : `${a} to ${b}`;
}

/** What a tub does to a batch, as sentences: how long, the batch, and how gears are lost. */
export function tubLines(recipe: Recipe): string[] {
  const tub = recipe.tub;
  if (!tub) return [];
  const lines = [t.tubDone(tub.kind, formatHours(tub.hours)), t.tubBatch(tub.batchSize, tub.litresPerBatch)];
  if (tub.lossEveryHours !== undefined && tub.lossEveryHours > 0) {
    lines.push(t.tubEats(formatHours(tub.graceHours ?? 0), formatHours(tub.lossEveryHours)));
  }
  if (tub.lossChance !== undefined && tub.lossChance > 0) lines.push(t.tubLossChance(formatChance(tub.lossChance)));
  return lines;
}

/** The outputs of a tub other than what a lost gear becomes, and the failure's index or -1. */
export function tubOutputs(recipe: Recipe): { made: number[]; failure: number } {
  const failure = recipe.tub?.failure ?? -1;
  return { made: recipe.outputs.map((_, i) => i).filter((i) => i !== failure), failure };
}

/** A lottery's outcomes, likeliest first: each chance as text and the stacks it gives in `variant`. */
export function lotteryOutcomes(recipe: Recipe, variant: number): { chance: number; label: string; stacks: Stack[] }[] {
  const given = recipe.variants[variant]?.outputs ?? [];
  return (recipe.lottery?.outcomes ?? [])
    .map((o) => ({
      chance: o.chance,
      label: formatChance(o.chance),
      stacks: o.outputs.flatMap((i) => (given[i] ? [given[i]!] : [])),
    }))
    .sort((a, b) => b.chance - a.chance);
}

/** A machine job's facts, as sentences: the power and turns, then the oil it drains. */
export function machineLines(recipe: Recipe): string[] {
  const m = recipe.machine;
  if (!m) return [];
  const work = m.work ? { amount: formatNumber(m.work.amount), unit: m.work.unit, per: m.work.turnsPerUnit } : undefined;
  const lines = [t.machineTurns(m.power, formatNumber(m.turns), work)];
  if (m.oil) lines.push(t.machineOil(formatNumber(m.oil.points), m.oil.tank, formatNumber(m.oil.points / 100)));
  return lines;
}

/** What one ingredient is to a machine: kept, the worn tool, the oil, the machine itself, or consumed. */
export function machineRole(recipe: Recipe, i: number): "kept" | "wear" | "oil" | "station" | "consumed" {
  const m = recipe.machine;
  if (m?.kept?.includes(i)) return "kept";
  if (m?.wear?.ingredient === i) return "wear";
  if (m?.oil?.ingredient === i) return "oil";
  if (recipe.ingredients[i]?.role === "station") return "station";
  return "consumed";
}
