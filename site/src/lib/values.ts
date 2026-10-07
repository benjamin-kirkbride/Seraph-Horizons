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
 * The part of an item code its variants share: the domain and the path up to the first "-"
 * (`mpegearbox:gearbox14` for `mpegearbox:gearbox14-north`). Where the variant parts sit
 * after it varies (a door's stone is in the middle of its code), so only the first part is a
 * safe base.
 */
export function codeBase(code: string): string {
  const dash = code.indexOf("-", code.indexOf(":") + 1);
  return dash < 0 ? code : code.slice(0, dash);
}

/**
 * The values page's table over search.json. Items the reader can't tell apart are one row:
 * a block's orientations and states (`gearbox14-north`, `-east`, ...) have the same name,
 * mod, value and code base (`codeBase`), so they make one group, shown as its first code
 * with the rest counted (`variantsOf`). Floor-zero and the switches a value depends on must
 * match too, since the row shows them. The groups are made once, at construction.
 *
 * Each column's ascending order of rows is computed once, on first use; a query then walks
 * that order (backwards for descending) and keeps the rows whose name, any of its codes, or
 * mod contain every word typed. That is a few milliseconds for 25,000 items, so the page
 * filters as the reader types without a debounce. A row is its group's first item index.
 */
export class ValueTable {
  private readonly file: SearchFile;
  private readonly modNames: string[];
  private readonly names: string[];
  /** Each row's first item index, in code order. */
  private readonly heads: number[] = [];
  /** The item indices of each row of more than one, by its first. */
  private readonly members = new Map<number, number[]>();
  /** What the filter searches, by a row's first item index. */
  private readonly hay = new Map<number, string>();
  private readonly orders = new Map<string, number[]>();

  constructor(file: SearchFile, mods: Record<string, Pick<MetaMod, "name">>) {
    this.file = file;
    this.modNames = file.mods.map((id) => normalize(mods[id]?.name || id));
    this.names = file.names.map((n) => normalize(n));
    const groups = new Map<string, number[]>();
    file.codes.forEach((code, i) => {
      const key = [
        file.names[i],
        file.mod[i],
        valueOf(file, i) ?? "",
        isFloorZero(file, i) ? 1 : 0,
        file.valueSwitches?.[String(i)]?.join(",") ?? "",
        codeBase(code),
      ].join("\u0000");
      const group = groups.get(key);
      if (group) group.push(i);
      else groups.set(key, [i]);
    });
    // The codes are sorted, so a group's first index is its lowest code, and the map keeps
    // the groups in the order of their first index.
    for (const group of groups.values()) {
      const head = group[0]!;
      this.heads.push(head);
      if (group.length > 1) this.members.set(head, group);
      const modId = file.mods[file.mod[head]!] ?? "";
      const codes = group.map((i) => normalize(file.codes[i]!)).join("\n");
      this.hay.set(head, `${this.names[head]}\n${codes}\n${normalize(modId)}\n${this.modNames[file.mod[head]!] ?? ""}`);
    }
  }

  /** Number of rows with a value. */
  get valued(): number {
    return this.valueOrder("asc").length;
  }

  /** The item indices a row stands for, in code order, the row's own first. */
  variantsOf(head: number): readonly number[] {
    return this.members.get(head) ?? [head];
  }

  private byName = (a: number, b: number): number => {
    const na = this.names[a]!;
    const nb = this.names[b]!;
    if (na !== nb) return na < nb ? -1 : 1;
    return a - b; // codes are sorted, so the index is the code order
  };

  /** Ascending order of every row by name, or by mod then name. */
  private order(column: "name" | "mod"): number[] {
    let o = this.orders.get(column);
    if (o) return o;
    const all = [...this.heads];
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

  /** The rows with a value, by value; equal values in name order either way. */
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

  /** The rows to show, in order, each as its first item index. */
  query(q: ValueQuery): number[] {
    const tokens = tokenize(q.filter);
    const keep = (i: number) => (q.unvalued || valueOf(this.file, i) !== undefined) && tokens.every((t) => this.hay.get(i)!.includes(t));
    if (q.column === "value") {
      // Rows without a value come last in both directions, in name order.
      const rows = this.valueOrder(q.dir).filter(keep);
      if (q.unvalued) for (const i of this.order("name")) if (valueOf(this.file, i) === undefined && keep(i)) rows.push(i);
      return rows;
    }
    const rows = this.order(q.column).filter(keep);
    return q.dir === "desc" ? rows.reverse() : rows;
  }
}
