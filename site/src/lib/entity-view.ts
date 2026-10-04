// Pure grouping for the entity pages, kept out of the components so it can be tested
// without a DOM.
import type { ButcheryStep, Recipe } from "./export.ts";
import type { EntitySource, EntityVariant } from "./format.ts";
import { efficiencyRange, formatNumber } from "./recipe-view.ts";

export type SectionKind = "drops" | "harvest" | "behavior" | "sells" | "buys";

/** What a section row needs to be placed. */
export interface Placed {
  type: EntitySource["type"];
  note?: string;
}

export interface Section<T extends Placed> {
  kind: SectionKind;
  /** The behavior's note, for kind "behavior". */
  note?: string;
  rows: T[];
}

const ORDER: readonly SectionKind[] = ["drops", "harvest", "behavior", "sells", "buys"];

function kindOf(s: Placed): SectionKind {
  if (s.type === "traderSells") return "sells";
  if (s.type === "traderBuys") return "buys";
  if (s.note === undefined) return "drops";
  return s.note === "Harvested" ? "harvest" : "behavior";
}

/**
 * Rows split the way a player meets them: dropped on death, harvested from the body,
 * other behaviors (one section per note), then trades. Rows keep their order.
 */
export function entitySections<T extends Placed>(rows: readonly T[]): Section<T>[] {
  const sections = new Map<string, Section<T>>();
  for (const s of rows) {
    const kind = kindOf(s);
    const key = kind === "behavior" ? `behavior|${s.note}` : kind;
    let section = sections.get(key);
    if (!section) {
      section = { kind, rows: [] };
      if (kind === "behavior") section.note = s.note;
      sections.set(key, section);
    }
    section.rows.push(s);
  }
  return [...sections.values()].sort((a, b) => ORDER.indexOf(a.kind) - ORDER.indexOf(b.kind));
}

/** Variants that give exactly the same, shown as one. */
export interface VariantGroup {
  codes: string[];
  /** Distinct names, in variant order. */
  names: string[];
  sources: EntitySource[];
}

export function variantGroups(variants: readonly EntityVariant[]): VariantGroup[] {
  const groups = new Map<string, VariantGroup>();
  for (const v of variants) {
    const key = JSON.stringify(v.sources);
    let g = groups.get(key);
    if (!g) groups.set(key, (g = { codes: [], names: [], sources: v.sources }));
    g.codes.push(v.code);
    if (!g.names.includes(v.name)) g.names.push(v.name);
  }
  return [...groups.values()];
}

/**
 * A short label for a group: names that differ only in brackets are folded, so
 * "Agriculture trader (cold)" and "Agriculture trader (desert)" read
 * "Agriculture trader (cold, desert)".
 */
export function groupLabel(names: readonly string[]): string {
  const byBase = new Map<string, string[]>();
  for (const n of names) {
    const m = /^(.*?)\s*\(([^)]*)\)\s*$/.exec(n);
    const base = m ? m[1]! : n;
    const list = byBase.get(base) ?? [];
    if (m && !list.includes(m[2]!)) list.push(m[2]!);
    byBase.set(base, list);
  }
  return [...byBase].map(([base, inner]) => (inner.length > 0 ? `${base} (${inner.join(", ")})` : base)).join(" / ");
}

export interface Range {
  min: number;
  max: number;
  /** Kept only while every variant agrees on the average. */
  var?: number;
}

/** One item across all variants of a type. */
export interface MergedRow extends Placed {
  item: number;
  tool?: string;
  quantity?: Range;
  price?: Range;
  /** How many variants give it this way. */
  variants: number;
}

function widen(range: Range | undefined, avg: number, v: number | undefined, first: boolean): Range {
  if (!range) return first ? { min: avg, max: avg, ...(v ? { var: v } : {}) } : { min: avg, max: avg };
  const out: Range = { min: Math.min(range.min, avg), max: Math.max(range.max, avg) };
  if (range.var !== undefined && range.min === avg && range.max === avg && range.var === v) out.var = v;
  return out;
}

/** Every item the variants give, each way once, with the spread of quantities and prices. */
export function mergeVariants(variants: readonly EntityVariant[]): MergedRow[] {
  const rows = new Map<string, MergedRow>();
  for (const v of variants) {
    const seen = new Set<string>();
    for (const s of v.sources) {
      const key = JSON.stringify([s.type, s.note ?? null, s.item]);
      let row = rows.get(key);
      const first = !row;
      if (!row) {
        row = { type: s.type, item: s.item, variants: 0 };
        if (s.note !== undefined) row.note = s.note;
        if (s.tool) row.tool = s.tool;
        rows.set(key, row);
      }
      if (s.quantity) row.quantity = widen(row.quantity, s.quantity.avg, s.quantity.var, first);
      if (s.price !== undefined) row.price = widen(row.price, s.price, undefined, first);
      if (!seen.has(key)) {
        seen.add(key);
        row.variants++;
      }
    }
  }
  return [...rows.values()];
}

/** "3", "7 ± 2.5" or "1–3". */
export function formatSpread(r: Range | undefined): string {
  if (!r) return "";
  if (r.min !== r.max) return `${formatNumber(r.min)}–${formatNumber(r.max)}`;
  return r.var ? `${formatNumber(r.min)} ± ${formatNumber(r.var)}` : formatNumber(r.min);
}

/** One item in the butchery yield table. */
export interface ButcheryYieldRow {
  code: string;
  /** A liquid (blood), in litres. */
  litres: boolean;
  /** Harvested where the body lies, one range per condition; null when that way gives none. */
  ground: Range[] | null;
  /** Taken through the hook and the table, at a station yield of ×1; null likewise. */
  full: Range[] | null;
}

export interface ButcheryYields {
  /** In the order the records give the items. */
  rows: ButcheryYieldRow[];
  /** The lowest and highest condition; only 1 when the export does not give the range. */
  conditions: number[];
  /** The share of the drops field harvesting leaves, when the records say. */
  groundShare?: number;
  /** The loot multiplier range of each stage's stations (the hooks', the tables'). */
  stations: { step: ButcheryStep; min: number; max: number }[];
}

/** What one variant gives of one item one way, split by whether condition scales it. */
interface Sum {
  plain: number;
  plainVar: number;
  scaled: number;
  scaledVar: number;
}

/**
 * What the butchery variants picked give, item by item: harvested where the body lies, and
 * taken through all the other stages, each at the lowest and the highest condition. Several
 * variants (a chip that covers more than one, or a whole type) give ranges of averages as
 * `mergeVariants` does, the spread kept only while they agree. The carcass in its states is
 * a means, not a product, and is left out.
 */
export function butcheryYields(picked: readonly { recipe: Recipe; variants: readonly number[] }[]): ButcheryYields {
  let condition: { min: number; max: number } | undefined;
  let groundShare: number | undefined;
  const stations = new Map<ButcheryStep, { step: ButcheryStep; min: number; max: number }>();
  for (const { recipe } of picked) {
    const c = recipe.butchery?.condition;
    if (c) condition = condition ? { min: Math.min(condition.min, c.min), max: Math.max(condition.max, c.max) } : { ...c };
    for (const stage of recipe.butchery?.stages ?? []) {
      if (stage.step === "harvest") groundShare ??= stage.multiplier;
      for (const i of [...stage.ingredients, ...(stage.options ?? []).flat()]) {
        const r = efficiencyRange(recipe.ingredients[i]);
        if (!r) continue;
        const s = stations.get(stage.step);
        stations.set(stage.step, { step: stage.step, min: Math.min(s?.min ?? r.min, r.min), max: Math.max(s?.max ?? r.max, r.max) });
      }
    }
  }
  const conditions = !condition ? [1] : condition.min === condition.max ? [condition.max] : [condition.min, condition.max];

  const rows = new Map<string, ButcheryYieldRow>();
  for (const { recipe, variants } of picked) {
    const b = recipe.butchery;
    if (!b) continue;
    const carcasses = new Set(recipe.ingredients.filter((ing) => ing.role === "carcass").map((ing) => ing.code));
    for (const v of variants) {
      const sums = new Map<string, { ground?: Sum; full?: Sum }>();
      for (const stage of b.stages) {
        const way = stage.step === "harvest" ? "ground" : "full";
        for (const o of stage.outputs) {
          const out = recipe.outputs[o];
          const y = b.variants[v]?.yields[o];
          if (!out || !y || carcasses.has(out.code)) continue;
          // A liquid's yield counts portions; its output says how many litres that many are.
          const unit = out.litres !== undefined && out.quantity ? out.litres / out.quantity : 1;
          const scaled = ((out.extra?.scaledBy as string[] | undefined) ?? []).includes("condition");
          let item = sums.get(out.code);
          if (!item) sums.set(out.code, (item = {}));
          const sum = (item[way] ??= { plain: 0, plainVar: 0, scaled: 0, scaledVar: 0 });
          sum[scaled ? "scaled" : "plain"] += y.avg * unit;
          sum[scaled ? "scaledVar" : "plainVar"] += (y.var ?? 0) * unit;
          let row = rows.get(out.code);
          if (!row) rows.set(out.code, (row = { code: out.code, litres: false, ground: null, full: null }));
          if (out.litres !== undefined) row.litres = true;
        }
      }
      for (const [code, item] of sums) {
        const row = rows.get(code)!;
        for (const way of ["ground", "full"] as const) {
          const sum = item[way];
          if (!sum) continue;
          const before = row[way];
          row[way] = conditions.map((c, k) => {
            const spread = round(sum.plainVar + sum.scaledVar * c);
            return widen(before?.[k], round(sum.plain + sum.scaled * c), spread || undefined, !before);
          });
        }
      }
    }
  }
  return { rows: [...rows.values()], conditions, ...(groundShare !== undefined ? { groundShare } : {}), stations: [...stations.values()] };
}

/**
 * True when butchery records cover every one of `codes`, so that their yields can stand in
 * for what those creatures give when harvested: a creature the records miss would be lost.
 */
export function butcheryCovers(recipes: readonly Recipe[], codes: readonly string[]): boolean {
  const covered = new Set(recipes.flatMap((r) => (r.butchery?.variants ?? []).flatMap((v) => v.entities.map((e) => e.code))));
  return covered.size > 0 && codes.every((c) => covered.has(c));
}

/** Sums of decimals ("0.1 + 0.2") read as the numbers they stand for. */
function round(n: number): number {
  return Math.round(n * 1e6) / 1e6;
}
