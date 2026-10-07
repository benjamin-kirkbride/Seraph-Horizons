import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { RecipeExport } from "../src/lib/export.ts";
import { FLAG_FLOOR_ZERO, type Meta, type SearchFile } from "../src/lib/format.ts";
import { prepareData } from "../src/lib/prepare.ts";
import { formatGears, isFloorZero, sortByValue, valueOf, ValueTable } from "../src/lib/values.ts";

const minimal = JSON.parse(readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8")) as RecipeExport;

/** minimal.json with a value on a few items, as the exporter writes them. */
function withValues(): RecipeExport {
  const exp = structuredClone(minimal);
  const set = (code: string, fields: Partial<RecipeExport["items"][string]>) => Object.assign(exp.items[code]!, fields);
  set("game:ingot-copper", { value: 2.5 });
  set("game:ingot-tin", { value: 3.125 });
  set("game:stick", { value: 0.002, floorZero: true });
  set("game:flint", { value: 0.05 });
  set("game:saw-copper", { value: 7.5, valueSwitches: ["toolValues"] });
  set("examplemod:widget", { value: 2.5 });
  set("seraphhorizons:gearblank-steel", { value: 1234.5, switch: "gears" });
  return exp;
}

const prepared = prepareData(withValues());
const search = prepared.files.get("search.json") as SearchFile;
const at = (code: string) => search.codes.indexOf(code);

describe("prepareData with item values", () => {
  it("writes one value or null per search.json row", () => {
    expect(search.value).toHaveLength(search.codes.length);
    expect(search.value![at("game:ingot-copper")]).toBe(2.5);
    expect(search.value![at("seraphhorizons:gearblank-steel")]).toBe(1234.5);
    expect(search.value![at("game:plank-oak")]).toBeNull();
  });

  it("flags floorZero items and keeps the other flags", () => {
    expect(search.flags[at("game:stick")]! & FLAG_FLOOR_ZERO).toBe(FLAG_FLOOR_ZERO);
    expect(search.flags[at("game:ingot-copper")]! & FLAG_FLOOR_ZERO).toBe(0);
    expect(isFloorZero(search, at("game:stick"))).toBe(true);
  });

  it("keeps the switches a value depends on, by item index, for the items that have some", () => {
    expect(search.valueSwitches).toEqual({ [String(at("game:saw-copper"))]: ["toolValues"] });
  });

  it("counts the valued items in meta.json", () => {
    expect((prepared.files.get("meta.json") as Meta).valueCount).toBe(7);
  });

  it("writes no value column and no count for an export without values", () => {
    const files = prepareData(minimal).files;
    expect((files.get("search.json") as SearchFile).value).toBeUndefined();
    expect((files.get("search.json") as SearchFile).valueSwitches).toBeUndefined();
    expect((files.get("meta.json") as Meta).valueCount).toBeUndefined();
  });
});

describe("formatGears", () => {
  it("shows up to three decimals and groups thousands", () => {
    expect(formatGears(1)).toBe("1");
    expect(formatGears(0.125)).toBe("0.125");
    expect(formatGears(2.5)).toBe("2.5");
    expect(formatGears(1234.5)).toBe("1,234.5");
    expect(formatGears(0.0004)).toBe("0");
  });
});

describe("sortByValue", () => {
  const hits = ["game:plank-oak", "game:ingot-tin", "examplemod:widget", "game:stick", "game:ingot-copper"].map(at);
  const codes = (list: number[]) => list.map((i) => search.codes[i]);

  it("sorts by value, keeping the given order for equal values and putting items without one last", () => {
    expect(codes(sortByValue(hits, search, "asc"))).toEqual([
      "game:stick",
      "examplemod:widget",
      "game:ingot-copper",
      "game:ingot-tin",
      "game:plank-oak",
    ]);
    expect(codes(sortByValue(hits, search, "desc"))).toEqual([
      "game:ingot-tin",
      "examplemod:widget",
      "game:ingot-copper",
      "game:stick",
      "game:plank-oak",
    ]);
  });

  it("leaves results alone when the export has no values", () => {
    const plain = prepareData(minimal).files.get("search.json") as SearchFile;
    expect(sortByValue([3, 1, 2], plain, "desc")).toEqual([3, 1, 2]);
    expect(valueOf(plain, 1)).toBeUndefined();
  });
});

describe("ValueTable", () => {
  const table = new ValueTable(search, { game: { name: "Vintage Story" }, examplemod: { name: "Example Mod" }, seraphhorizons: { name: "Seraph Horizons" } });
  const names = (list: number[]) => list.map((i) => search.names[i]!);
  const codes = (list: number[]) => list.map((i) => search.codes[i]);
  const base = { filter: "", column: "value", dir: "desc", unvalued: false } as const;

  it("lists only valued items unless asked, by value with ties by name", () => {
    expect(table.valued).toBe(7);
    const rows = table.query(base);
    expect(rows).toHaveLength(7);
    expect(codes(rows).slice(0, 4)).toEqual(["seraphhorizons:gearblank-steel", "game:saw-copper", "game:ingot-tin", "game:ingot-copper"]);
    // Copper ingot and Widget are both 2.5: name order in both directions.
    const asc = codes(table.query({ ...base, dir: "asc" }));
    expect(asc.slice(0, 4)).toEqual(["game:stick", "game:flint", "game:ingot-copper", "examplemod:widget"]);
  });

  it("puts the items without a value last in both directions", () => {
    for (const dir of ["asc", "desc"] as const) {
      const rows = table.query({ ...base, dir, unvalued: true });
      expect(rows).toHaveLength(search.codes.length);
      expect(rows.slice(0, 7).every((i) => valueOf(search, i) !== undefined)).toBe(true);
      expect(rows.slice(7).every((i) => valueOf(search, i) === undefined)).toBe(true);
    }
  });

  it("sorts by name and by mod name, either way", () => {
    const byName = names(table.query({ ...base, column: "name", dir: "asc" }));
    expect(byName).toEqual([...byName].sort((a, b) => (a.toLowerCase() < b.toLowerCase() ? -1 : 1)));
    expect(names(table.query({ ...base, column: "name", dir: "desc" }))).toEqual([...byName].reverse());
    const byMod = codes(table.query({ ...base, column: "mod", dir: "asc" }));
    expect(byMod[0]).toBe("examplemod:widget");
    expect(byMod.at(-1)).toBe("game:ingot-tin"); // Vintage Story last, then "Tin ingot" last by name
    expect(codes(table.query({ ...base, column: "mod", dir: "desc" }))[0]).toBe("game:ingot-tin");
  });

  it("filters on every word over name, code and mod", () => {
    expect(codes(table.query({ ...base, filter: "copper" }))).toEqual(["game:saw-copper", "game:ingot-copper"]);
    expect(codes(table.query({ ...base, filter: "INGOT tin" }))).toEqual(["game:ingot-tin"]);
    expect(codes(table.query({ ...base, filter: "example mod" }))).toEqual(["examplemod:widget"]);
    expect(codes(table.query({ ...base, filter: "seraphhorizons" }))).toEqual(["seraphhorizons:gearblank-steel"]);
    expect(table.query({ ...base, filter: "nothing like this" })).toEqual([]);
  });

  it("filters and sorts 25,000 rows quickly", () => {
    const n = 25_000;
    const big: SearchFile = {
      mods: ["game", "examplemod"],
      codes: Array.from({ length: n }, (_, i) => `game:item-${String(i).padStart(5, "0")}`),
      names: Array.from({ length: n }, (_, i) => `Item ${(i * 7919) % n}`),
      mod: Array.from({ length: n }, (_, i) => i % 2),
      flags: Array.from({ length: n }, () => 1),
      value: Array.from({ length: n }, (_, i) => (i % 10 === 0 ? null : ((i * 104729) % 100_000) / 1000)),
    };
    const started = performance.now();
    const t = new ValueTable(big, {});
    for (const column of ["value", "name", "mod"] as const) t.query({ ...base, column });
    const built = performance.now() - started;
    const q0 = performance.now();
    for (const filter of ["i", "it", "ite", "item 1", "item 12"]) t.query({ ...base, filter });
    const perQuery = (performance.now() - q0) / 5;
    expect(t.query(base)).toHaveLength(22_500);
    // Generous bounds for a slow CI machine; locally this is a fraction of them.
    expect(built).toBeLessThan(2000);
    expect(perQuery).toBeLessThan(100);
  });
});
