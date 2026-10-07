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
 * The values page's table over search.json. Each column's ascending order is computed once,
 * on first use; a query then walks that order (backwards for descending) and keeps the rows
 * whose name, code and mod contain every word typed. That is a few milliseconds for 25,000
 * rows, so the page filters as the reader types without a debounce.
 */
export class ValueTable {
  private readonly file: SearchFile;
  private readonly modNames: string[];
  private readonly names: string[];
  private readonly hay: string[];
  private readonly orders = new Map<string, number[]>();

  constructor(file: SearchFile, mods: Record<string, Pick<MetaMod, "name">>) {
    this.file = file;
    this.modNames = file.mods.map((id) => normalize(mods[id]?.name || id));
    this.names = file.names.map((n) => normalize(n));
    this.hay = file.codes.map((code, i) => {
      const modId = file.mods[file.mod[i]!] ?? "";
      return `${this.names[i]}\n${normalize(code)}\n${normalize(modId)}\n${this.modNames[file.mod[i]!] ?? ""}`;
    });
  }

  /** Number of items with a value. */
  get valued(): number {
    return this.valueOrder("asc").length;
  }

  private byName = (a: number, b: number): number => {
    const na = this.names[a]!;
    const nb = this.names[b]!;
    if (na !== nb) return na < nb ? -1 : 1;
    return a - b; // codes are sorted, so the index is the code order
  };

  /** Ascending order of every item by name, or by mod then name. */
  private order(column: "name" | "mod"): number[] {
    let o = this.orders.get(column);
    if (o) return o;
    const all = this.file.codes.map((_, i) => i);
    if (column === "name") o = all.sort(this.byName);
    else {
      o = all.sort((a, b) => {
        const ma = this.modNames[this.file.mod[a]!]!;
        const mb = this.modNames[this.file.mod[b]!]!;
        return ma < mb ? -1 : ma > mb ? 1 : this.byName(a, b);
      });
    }
    this.orders.set(column, o);
    return o;
  }

  /** The items with a value, by value; equal values in name order either way. */
  private valueOrder(dir: SortDir): number[] {
    const key = `value|${dir}`;
    let o = this.orders.get(key);
    if (o) return o;
    o = sortByValue(
      this.order("name").filter((i) => valueOf(this.file, i) !== undefined),
      this.file,
      dir,
    );
    this.orders.set(key, o);
    return o;
  }

  /** Item indices to show, in order. */
  query(q: ValueQuery): number[] {
    const tokens = tokenize(q.filter);
    const keep = (i: number) => (q.unvalued || valueOf(this.file, i) !== undefined) && tokens.every((t) => this.hay[i]!.includes(t));
    if (q.column === "value") {
      // Items without a value come last in both directions, in name order.
      const rows = this.valueOrder(q.dir).filter(keep);
      if (q.unvalued) for (const i of this.order("name")) if (valueOf(this.file, i) === undefined && keep(i)) rows.push(i);
      return rows;
    }
    const rows = this.order(q.column).filter(keep);
    return q.dir === "desc" ? rows.reverse() : rows;
  }
}
