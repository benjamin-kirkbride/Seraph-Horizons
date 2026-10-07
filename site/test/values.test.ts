import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { RecipeExport } from "../src/lib/export.ts";
import { FLAG_FLOOR_ZERO, type Meta, type SearchFile } from "../src/lib/format.ts";
import { prepareData } from "../src/lib/prepare.ts";
import { codeBase, formatGears, isFloorZero, itemCount, sortByValue, valueOf, ValueTable, type ValueQuery, type ValueRow } from "../src/lib/values.ts";

/** minimal.json without the value fields and groups it shows off, so each test sets its own. */
const minimal = withoutValues(
  JSON.parse(readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8")) as RecipeExport,
);

function withoutValues(exp: RecipeExport): RecipeExport {
  delete exp.variantGroups;
  for (const item of Object.values(exp.items)) {
    delete item.value;
    delete item.floorZero;
    delete item.valueSwitches;
  }
  return exp;
}

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
  // Without groups every row is one item, but for the two clay gear blank molds (below).
  const query = (q: ValueQuery) => table.query(q).map((r) => r.items[0]!);
  const variantsOf = (code: string) => table.rows.find((r) => search.codes[r.items[0]!] === code)!.items.map((i) => search.codes[i]);

  it("has a row per item without groups, but for look-alikes", () => {
    // The black and blue clay gear blank molds are both "Gear blank mold", unvalued, of one base.
    expect(table.grouped).toBe(true);
    expect(table.rows).toHaveLength(search.codes.length - 1);
    expect(variantsOf("seraphhorizons:toolmold-black-fired-gearblank")).toEqual([
      "seraphhorizons:toolmold-black-fired-gearblank",
      "seraphhorizons:toolmold-blue-fired-gearblank",
    ]);
  });

  it("lists only valued items unless asked, by value with ties by name", () => {
    expect(table.valued).toBe(7);
    const rows = query(base);
    expect(rows).toHaveLength(7);
    expect(codes(rows).slice(0, 4)).toEqual(["seraphhorizons:gearblank-steel", "game:saw-copper", "game:ingot-tin", "game:ingot-copper"]);
    // Copper ingot and Widget are both 2.5: name order in both directions.
    const asc = codes(query({ ...base, dir: "asc" }));
    expect(asc.slice(0, 4)).toEqual(["game:stick", "game:flint", "game:ingot-copper", "examplemod:widget"]);
  });

  it("puts the items without a value last in both directions", () => {
    for (const dir of ["asc", "desc"] as const) {
      const rows = query({ ...base, dir, unvalued: true });
      expect(rows).toHaveLength(search.codes.length - 1);
      expect(itemCount(table.query({ ...base, dir, unvalued: true }))).toBe(search.codes.length);
      expect(rows.slice(0, 7).every((i) => valueOf(search, i) !== undefined)).toBe(true);
      expect(rows.slice(7).every((i) => valueOf(search, i) === undefined)).toBe(true);
    }
  });

  it("sorts by name and by mod name, either way", () => {
    const byName = names(query({ ...base, column: "name", dir: "asc" }));
    expect(byName).toEqual([...byName].sort((a, b) => (a.toLowerCase() < b.toLowerCase() ? -1 : 1)));
    expect(names(query({ ...base, column: "name", dir: "desc" }))).toEqual([...byName].reverse());
    const byMod = codes(query({ ...base, column: "mod", dir: "asc" }));
    expect(byMod[0]).toBe("examplemod:widget");
    expect(byMod.at(-1)).toBe("game:ingot-tin"); // Vintage Story last, then "Tin ingot" last by name
    expect(codes(query({ ...base, column: "mod", dir: "desc" }))[0]).toBe("game:ingot-tin");
  });

  it("filters on every word over name, code and mod", () => {
    expect(codes(query({ ...base, filter: "copper" }))).toEqual(["game:saw-copper", "game:ingot-copper"]);
    expect(codes(query({ ...base, filter: "INGOT tin" }))).toEqual(["game:ingot-tin"]);
    expect(codes(query({ ...base, filter: "example mod" }))).toEqual(["examplemod:widget"]);
    expect(codes(query({ ...base, filter: "seraphhorizons" }))).toEqual(["seraphhorizons:gearblank-steel"]);
    expect(query({ ...base, filter: "nothing like this" })).toEqual([]);
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
    expect(t.grouped).toBe(false); // every name is its own
    // Generous bounds for a slow CI machine; locally this is a fraction of them.
    expect(built).toBeLessThan(2000);
    expect(perQuery).toBeLessThan(100);

    // The same with every item in a group of five, two of which share a price.
    const grouped: SearchFile = {
      ...big,
      value: big.value!.map((v, i) => (i % 5 < 2 ? 1 : v)),
      groups: {
        titles: Array.from({ length: n / 5 }, (_, g) => `Group ${g}`),
        members: Array.from({ length: n / 5 }, (_, g) => [0, 1, 2, 3, 4].map((k) => g * 5 + k)),
      },
    };
    const g0 = performance.now();
    const gt = new ValueTable(grouped, {});
    for (const column of ["value", "name", "mod"] as const) gt.query({ ...base, column });
    const gBuilt = performance.now() - g0;
    const gq0 = performance.now();
    for (const filter of ["i", "it", "ite", "item 1", "group 12"]) gt.query({ ...base, filter });
    expect(gt.rows).toHaveLength(n - n / 5);
    expect(gBuilt).toBeLessThan(2000);
    expect((performance.now() - gq0) / 5).toBeLessThan(100);
  });
});

describe("ValueTable with variant groups", () => {
  const exp = withValues();
  const set = (code: string, fields: Partial<RecipeExport["items"][string]>) => Object.assign(exp.items[code]!, fields);
  set("game:ingot-tinbronze", { value: 2.5 });
  set("game:ingot-steel", { value: 2.5 });
  set("game:plank-oak", { value: 0.5 });
  set("game:plank-birch", { value: 0.5 });
  set("game:hide-raw-small", { value: 1 });
  set("game:hide-soaked-small", { value: 1, floorZero: true });
  set("game:knifeblade-flint", { value: 7.5, valueSwitches: ["toolValues"] });
  set("game:knife-generic-flint", { value: 7.5 });
  exp.variantGroups = {
    // Tin is dearer than the rest, so the group splits: three ingots and the widget share 2.5.
    "auto:game:ingot": {
      title: "Ingot",
      members: ["game:ingot-tin", "game:ingot-steel", "game:ingot-copper", "examplemod:widget", "game:ingot-tinbronze"],
    },
    "auto:game:plank": { title: "Plank", members: ["game:plank-oak", "game:plank-birch"] },
    // No value, so one row, shown with the unvalued items.
    "auto:game:supportbeam": { title: "Support beam", members: ["game:supportbeam-oak", "game:supportbeam-birch"] },
    // floorZero and the switches tell prices apart as much as the value does.
    "auto:game:hide": { title: "Hide", members: ["game:hide-raw-small", "game:hide-soaked-small"] },
    "auto:game:knife": { title: "Knife", members: ["game:knifeblade-flint", "game:knife-generic-flint"] },
  };
  const s = prepareData(exp).files.get("search.json") as SearchFile;
  const table = new ValueTable(s, { game: { name: "Vintage Story" }, examplemod: { name: "Example Mod" }, seraphhorizons: { name: "Seraph Horizons" } });
  const base = { filter: "", column: "value", dir: "desc", unvalued: false } as const;
  const show = (rows: ValueRow[]) => rows.map((r) => (r.group === undefined ? s.codes[r.items[0]!]! : `${table.label(r)} ${r.items.length}/${r.groupSize}`));
  const codes = (row: ValueRow) => row.items.map((i) => s.codes[i]);

  it("makes one row of a group's members that share a price, in rank order", () => {
    expect(table.grouped).toBe(true);
    const ingots = table.rows.find((r) => r.group !== undefined && table.label(r) === "Ingot")!;
    expect(codes(ingots)).toEqual(["game:ingot-steel", "game:ingot-copper", "examplemod:widget", "game:ingot-tinbronze"]);
    expect(ingots.groupSize).toBe(5);
    expect(table.value(ingots)).toBe(2.5);
    expect(table.modsOf(ingots)).toEqual(["examplemod", "game"]);
  });

  it("leaves a member alone at its own price, and splits members whose floorZero or switches differ", () => {
    const rows = show(table.query({ ...base, unvalued: true }));
    expect(rows).toContain("game:ingot-tin");
    expect(rows).toEqual(expect.arrayContaining(["game:hide-raw-small", "game:hide-soaked-small", "game:knifeblade-flint", "game:knife-generic-flint"]));
    expect(rows.filter((r) => r.startsWith("Hide") || r.startsWith("Knife"))).toEqual([]);
  });

  it("lists every item once, a group's unvalued members as one row with the unvalued items", () => {
    const all = table.query({ ...base, unvalued: true });
    expect(itemCount(all)).toBe(s.codes.length);
    // Four ingots and widget in one row, two planks, two beams, and the two gear blank molds folded.
    expect(all).toHaveLength(s.codes.length - 3 - 1 - 1 - 1);
    expect(show(all)).toContain("Support beam 2/2");
    expect(show(table.query(base))).not.toContain("Support beam 2/2");
    expect(itemCount(table.query(base))).toBe(table.valued);
  });

  it("sorts a group row by its title and its value, ties by name", () => {
    const byValue = show(table.query(base));
    // Copper's 2.5 group, by name: Ingot before Plank's 0.5 and after tin's 3.125.
    expect(byValue.indexOf("game:ingot-tin")).toBeLessThan(byValue.indexOf("Ingot 4/5"));
    expect(byValue.indexOf("Ingot 4/5")).toBeLessThan(byValue.indexOf("Plank 2/2"));
    const byName = table.query({ ...base, column: "name", dir: "asc" }).map((r) => table.label(r));
    expect(byName).toEqual([...byName].sort((a, b) => (a.toLowerCase() < b.toLowerCase() ? -1 : 1)));
    expect(byName).toContain("Ingot");
  });

  it("sorts a group row with items of several mods by their names joined", () => {
    const byMod = show(table.query({ ...base, column: "mod", dir: "asc" }));
    // "example mod, vintage story" sorts after "example mod" and before "seraph horizons".
    expect(byMod.slice(0, 1)).toEqual(["Ingot 4/5"]);
  });

  it("matches a group row on its title or on any one member's name, code or mod", () => {
    expect(show(table.query({ ...base, filter: "plank" }))).toEqual(["Plank 2/2"]);
    expect(show(table.query({ ...base, filter: "birch" }))).toEqual(["Plank 2/2"]);
    expect(show(table.query({ ...base, filter: "plank birch" }))).toEqual(["Plank 2/2"]);
    expect(show(table.query({ ...base, filter: "example mod" }))).toEqual(["Ingot 4/5"]);
    expect(show(table.query({ ...base, filter: "tinbronze" }))).toEqual(["Ingot 4/5"]);
    // One member has to hold every word: no ingot is both steel and copper.
    expect(show(table.query({ ...base, filter: "steel copper ingot" }))).toEqual([]);
  });
});

describe("ValueTable look-alike rows", () => {
  // Two mods each have a "Gearbox"; one mod's gearbox comes in five orientations, one of
  // them (south) priced differently, and another of its blocks shares the name and value.
  const items: [code: string, name: string, mod: number, value: number | null][] = [
    ["mpegearbox:gearbox14-down", "Gearbox", 1, 3],
    ["mpegearbox:gearbox14-east", "Gearbox", 1, 3],
    ["mpegearbox:gearbox14-north", "Gearbox", 1, 3],
    ["mpegearbox:gearbox14-south", "Gearbox", 1, 4],
    ["mpegearbox:gearbox14-up", "Gearbox", 1, 3],
    ["mpegearbox:gearboxcrate-north", "Gearbox", 1, 3],
    ["othermod:gearbox-east", "Gearbox", 2, 3],
    ["othermod:gearbox-west", "Gearbox", 2, 3],
    ["othermod:rock-granite-east", "Rock", 2, null],
    ["othermod:rock-granite-west", "Rock", 2, null],
    ["othermod:rock-basalt-east", "Rock", 2, null],
    ["othermod:clutter-aquatic", "othermod:clutter-", 2, null],
    ["othermod:clutter-devastation", "othermod:clutter-", 2, null],
  ];
  items.sort((a, b) => (a[0] < b[0] ? -1 : 1)); // search.json's codes are sorted
  const file: SearchFile = {
    mods: ["game", "mpegearbox", "othermod"],
    codes: items.map((x) => x[0]),
    names: items.map((x) => x[1]),
    mod: items.map((x) => x[2]),
    flags: items.map(() => 1),
    value: items.map((x) => x[3]),
  };
  const table = new ValueTable(file, { othermod: { name: "The Other Mod" } });
  const base: ValueQuery = { filter: "", column: "name", dir: "asc", unvalued: false };
  const rows = (q: Partial<ValueQuery> = {}, t = table, f = file) => t.query({ ...base, ...q }).map((r) => [f.codes[r.items[0]!], r.items.length] as const);
  const codesOf = (row: ValueRow, f = file) => row.items.map((i) => f.codes[i]);

  it("folds a block's orientations into one row, under its first code, with the rest counted", () => {
    expect(codeBase("mpegearbox:gearbox14-north")).toBe("mpegearbox:gearbox14");
    expect(codeBase("game:stick")).toBe("game:stick");
    expect(codeBase("gearbox14-north")).toBe("gearbox14");
    expect(codeBase("my-mod:gearbox14-north")).toBe("my-mod:gearbox14");
    expect(rows()).toEqual([
      ["mpegearbox:gearbox14-down", 4],
      ["mpegearbox:gearbox14-south", 1], // another value
      ["mpegearbox:gearboxcrate-north", 1], // another code base
      ["othermod:gearbox-east", 2], // another mod
    ]);
    const down = table.rows.find((r) => file.codes[r.items[0]!] === "mpegearbox:gearbox14-down")!;
    expect(codesOf(down)).toEqual(["mpegearbox:gearbox14-down", "mpegearbox:gearbox14-east", "mpegearbox:gearbox14-north", "mpegearbox:gearbox14-up"]);
    expect(down.group).toBeUndefined();
    expect(table.label(down)).toBe("Gearbox");
  });

  it("counts valued items, and sorts the rows", () => {
    expect(table.valued).toBe(8);
    expect(rows({ column: "value", dir: "desc" }).map(([code]) => code)).toEqual([
      "mpegearbox:gearbox14-south",
      "mpegearbox:gearbox14-down",
      "mpegearbox:gearboxcrate-north",
      "othermod:gearbox-east",
    ]);
    expect(rows({ column: "mod", dir: "desc" })[0]).toEqual(["othermod:gearbox-east", 2]);
  });

  it("finds a row by any of its codes", () => {
    expect(rows({ filter: "gearbox14-east" })).toEqual([["mpegearbox:gearbox14-down", 4]]);
    expect(rows({ filter: "othermod west" })).toEqual([["othermod:gearbox-east", 2]]);
    expect(rows({ filter: "other mod west" })).toEqual([["othermod:gearbox-east", 2]]);
  });

  it("folds the items without a value the same way", () => {
    expect(rows({ unvalued: true, filter: "rock" })).toEqual([["othermod:rock-basalt-east", 3]]);
    for (const dir of ["asc", "desc"] as const) {
      const all = rows({ column: "value", dir, unvalued: true });
      expect(all).toHaveLength(7);
      expect(all.at(-1)).toEqual(["othermod:rock-basalt-east", 3]);
    }
  });

  it("never folds items whose name is still a lang key", () => {
    expect(rows({ unvalued: true, filter: "clutter" })).toEqual([
      ["othermod:clutter-aquatic", 1],
      ["othermod:clutter-devastation", 1],
    ]);
  });

  it("keeps apart items whose rows would show something else", () => {
    const west = file.codes.indexOf("othermod:gearbox-west");
    const floor: SearchFile = { ...file, flags: file.flags.map((f, i) => (i === west ? f | FLAG_FLOOR_ZERO : f)) };
    expect(rows({ filter: "othermod gearbox" }, new ValueTable(floor, {}), floor)).toEqual([
      ["othermod:gearbox-east", 1],
      ["othermod:gearbox-west", 1],
    ]);
    const switched: SearchFile = { ...file, valueSwitches: { [String(west)]: ["gears"] } };
    expect(rows({ filter: "othermod gearbox" }, new ValueTable(switched, {}), switched)).toHaveLength(2);
  });

  it("joins a group member's look-alikes to its group row: the orientations the game hides", () => {
    // Tidy Variants shows one gearbox orientation (north) and groups it with the crate.
    const at = (code: string) => file.codes.indexOf(code);
    const grouped: SearchFile = {
      ...file,
      groups: { titles: ["Gearbox"], members: [[at("mpegearbox:gearbox14-north"), at("mpegearbox:gearboxcrate-north")]] },
    };
    const t = new ValueTable(grouped, {});
    const row = t.rows.find((r) => r.group === 0)!;
    // The ranked members first, then the hidden orientations in code order; south, at another price, stays apart.
    expect(codesOf(row, grouped)).toEqual([
      "mpegearbox:gearbox14-north",
      "mpegearbox:gearboxcrate-north",
      "mpegearbox:gearbox14-down",
      "mpegearbox:gearbox14-east",
      "mpegearbox:gearbox14-up",
    ]);
    expect(row.groupSize).toBe(5);
    expect(rows({}, t, grouped)).toEqual([
      ["mpegearbox:gearbox14-north", 5],
      ["mpegearbox:gearbox14-south", 1],
      ["othermod:gearbox-east", 2],
    ]);
  });
});
