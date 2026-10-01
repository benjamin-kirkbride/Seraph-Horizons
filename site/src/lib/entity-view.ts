// Pure grouping for the entity pages, kept out of the components so it can be tested
// without a DOM.
import type { EntitySource, EntityVariant } from "./format.ts";
import { formatNumber } from "./recipe-view.ts";

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
