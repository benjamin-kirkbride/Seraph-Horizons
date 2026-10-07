// Item values: the pack's price table, every item in rusty gears (rusty gear = 1). They
// ride in search.json (`value`, one number or null per item, and FLAG_FLOOR_ZERO), so the
// item page, the search results and the values page (#/<version>/values) need no other file.
// See docs/recipe-browser/site.md.
import { FLAG_FLOOR_ZERO, type MetaMod, type SearchFile } from "./format.ts";
import { normalize, tokenize } from "./search.ts";

/** The item every value is counted in. */
export const GEAR = "game:gear-rusty";

/** "1,234.5": a value has at most three decimals. */
export function formatGears(value: number): string {
  return value.toLocaleString("en", { maximumFractionDigits: 3 });
}

/** Item `index`'s value, or undefined when it has none (or the export has no values). */
export function valueOf(file: SearchFile, index: number): number | undefined {
  const v = file.value?.[index];
  return typeof v === "number" ? v : undefined;
}

export function isFloorZero(file: SearchFile, index: number): boolean {
  return ((file.flags[index] ?? 0) & FLAG_FLOOR_ZERO) !== 0;
}

export type SortDir = "asc" | "desc";

/**
 * Orders item indices by value, items without one last in either direction; equal values
 * keep their order (the search ranking, or the name order on the values page).
 */
export function sortByValue(indices: readonly number[], file: SearchFile, dir: SortDir): number[] {
  const sign = dir === "asc" ? 1 : -1;
  return indices
    .map((i, pos) => ({ i, pos, v: valueOf(file, i) }))
    .sort((a, b) => {
      if (a.v === undefined || b.v === undefined) return (a.v === undefined ? 1 : 0) - (b.v === undefined ? 1 : 0) || a.pos - b.pos;
      return sign * (a.v - b.v) || a.pos - b.pos;
    })
    .map((x) => x.i);
}

export type ValueColumn = "name" | "mod" | "value";

export interface ValueQuery {
  filter: string;
  column: ValueColumn;
  dir: SortDir;
  /** Also list items with no value (they sort last by value). */
  unvalued: boolean;
}

/**
 * One row of the values page: a single item, or the variants of one Tidy Variants group
 * that share a price, which the game shows as one tile and the page as one row.
 */
export interface ValueRow {
  /** Item indices, best representative first. One item, or two or more of `group`. */
  items: number[];
  /** Index of the row's group in search.json's `groups`, for a row of grouped variants. */
  group?: number;
  /** How many variants the group has in all, at any price. */
  groupSize?: number;
}

/**
 * What makes two variants one row: the same value, floorZero and config switches. Variants
 * without a value share one key, so a group's unvalued members are one row too.
 */
function priceKey(file: SearchFile, i: number): string {
  const v = valueOf(file, i);
  if (v === undefined) return "none";
  return `${v}|${isFloorZero(file, i) ? 1 : 0}|${(file.valueSwitches?.[String(i)] ?? []).join(",")}`;
}

/** Splits each group by price into rows; every item outside a row of two or more is a row of its own. */
export function valueRows(file: SearchFile): ValueRow[] {
  const rows: ValueRow[] = [];
  const grouped = new Uint8Array(file.codes.length);
  file.groups?.members.forEach((members, group) => {
    const byPrice = new Map<string, number[]>();
    for (const i of members) {
      const key = priceKey(file, i);
      const part = byPrice.get(key);
      if (part) part.push(i);
      else byPrice.set(key, [i]);
    }
    for (const items of byPrice.values()) {
      if (items.length < 2) continue;
      rows.push({ items, group, groupSize: members.length });
      for (const i of items) grouped[i] = 1;
    }
  });
  file.codes.forEach((_, i) => {
    if (!grouped[i]) rows.push({ items: [i] });
  });
  return rows;
}

/**
 * The values page's table over search.json. The rows (valueRows) and each column's ascending
 * order are computed once, on first use; a query then walks that order (backwards for
 * descending) and keeps the rows that match every word typed. That is a few milliseconds for
 * 25,000 rows, so the page filters as the reader types without a debounce.
 */
export class ValueTable {
  private readonly file: SearchFile;
  private readonly mods: Record<string, Pick<MetaMod, "name">>;
  readonly rows: ValueRow[];
  /** Each row's sort name: the group's title or the item's name, normalized. */
  private readonly names: string[];
  /** Each row's mod names, normalized and joined as the page shows them. */
  private readonly modKeys: string[];
  /**
   * What the filter searches, per row: each member's name, code, mod id and mod name, after
   * the group's title for a group row. A row matches when one of them has every word.
   */
  private readonly hay: string[][];
  private readonly orders = new Map<string, number[]>();
  /** Number of items with a value. */
  readonly valued: number;

  constructor(file: SearchFile, mods: Record<string, Pick<MetaMod, "name">>) {
    this.file = file;
    this.mods = mods;
    this.rows = valueRows(file);
    const modNames = file.mods.map((id) => normalize(mods[id]?.name || id));
    const itemHay = (i: number) => {
      const modId = file.mods[file.mod[i]!] ?? "";
      return `${normalize(file.names[i]!)}\n${normalize(file.codes[i]!)}\n${normalize(modId)}\n${modNames[file.mod[i]!] ?? ""}`;
    };
    this.names = this.rows.map((r) => normalize(this.label(r)));
    this.modKeys = this.rows.map((r) => normalize(this.modsOf(r).map((id) => mods[id]?.name || id).join(", ")));
    this.hay = this.rows.map((r) => {
      if (r.group === undefined) return [itemHay(r.items[0]!)];
      const title = normalize(file.groups!.titles[r.group]!);
      return r.items.map((i) => `${title}\n${itemHay(i)}`);
    });
    this.valued = file.value ? file.value.filter((v) => typeof v === "number").length : 0;
  }

  /** Whether any row holds more than one item. */
  get grouped(): boolean {
    return this.rows.length < this.file.codes.length;
  }

  /** A row's label: its group's title, or its item's name. */
  label(row: ValueRow): string {
    return row.group === undefined ? this.file.names[row.items[0]!]! : this.file.groups!.titles[row.group]!;
  }

  /** The row's value: every item of a row has the same one. */
  value(row: ValueRow): number | undefined {
    return valueOf(this.file, row.items[0]!);
  }

  /** The distinct mod ids of a row's items, by mod name. */
  modsOf(row: ValueRow): string[] {
    const ids = [...new Set(row.items.map((i) => this.file.mods[this.file.mod[i]!]!))];
    if (ids.length > 1) {
      const name = (id: string) => normalize(this.mods[id]?.name || id);
      ids.sort((a, b) => (name(a) < name(b) ? -1 : name(a) > name(b) ? 1 : 0));
    }
    return ids;
  }

  private byName = (a: number, b: number): number => {
    const na = this.names[a]!;
    const nb = this.names[b]!;
    if (na !== nb) return na < nb ? -1 : 1;
    // Codes are sorted, so the first item's index is the code order.
    return this.rows[a]!.items[0]! - this.rows[b]!.items[0]!;
  };

  /** Ascending order of every row by name, or by mod then name. */
  private order(column: "name" | "mod"): number[] {
    let o = this.orders.get(column);
    if (o) return o;
    const all = this.rows.map((_, r) => r);
    if (column === "name") o = all.sort(this.byName);
    else {
      o = all.sort((a, b) => {
        const ma = this.modKeys[a]!;
        const mb = this.modKeys[b]!;
        return ma < mb ? -1 : ma > mb ? 1 : this.byName(a, b);
      });
    }
    this.orders.set(column, o);
    return o;
  }

  /** The rows with a value, by value; equal values in name order either way. */
  private valueOrder(dir: SortDir): number[] {
    const key = `value|${dir}`;
    let o = this.orders.get(key);
    if (o) return o;
    const sign = dir === "asc" ? 1 : -1;
    const values = this.rows.map((r) => this.value(r));
    // The sort is stable, so ties keep the name order.
    o = this.order("name")
      .filter((r) => values[r] !== undefined)
      .sort((a, b) => sign * (values[a]! - values[b]!));
    this.orders.set(key, o);
    return o;
  }

  /** The rows to show, in order. */
  query(q: ValueQuery): ValueRow[] {
    const tokens = tokenize(q.filter);
    const keep = (r: number) =>
      (q.unvalued || this.value(this.rows[r]!) !== undefined) &&
      (tokens.length === 0 || this.hay[r]!.some((h) => tokens.every((t) => h.includes(t))));
    let rows: number[];
    if (q.column === "value") {
      // Rows without a value come last in both directions, in name order.
      rows = this.valueOrder(q.dir).filter(keep);
      if (q.unvalued) for (const r of this.order("name")) if (this.value(this.rows[r]!) === undefined && keep(r)) rows.push(r);
    } else {
      rows = this.order(q.column).filter(keep);
      if (q.dir === "desc") rows.reverse();
    }
    return rows.map((r) => this.rows[r]!);
  }
}

/** How many items the rows hold. */
export function itemCount(rows: readonly ValueRow[]): number {
  let n = 0;
  for (const r of rows) n += r.items.length;
  return n;
}
