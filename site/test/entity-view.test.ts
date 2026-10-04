import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { butcheryCovers, butcheryYields, entitySections, formatSpread, groupLabel, mergeVariants, variantGroups } from "../src/lib/entity-view.ts";
import type { Recipe, RecipeExport } from "../src/lib/export.ts";
import type { EntitySource, EntityVariant } from "../src/lib/format.ts";

describe("entitySections", () => {
  it("splits drops, harvests, behaviors and trades, in that order, keeping row order", () => {
    const rows: EntitySource[] = [
      { item: 3, type: "traderSells", quantity: { avg: 1 }, price: 4 },
      { item: 0, type: "entityDrop", quantity: { avg: 2 }, note: "Harvested" },
      { item: 1, type: "entityDrop", quantity: { avg: 1 } },
      { item: 2, type: "entityDrop", quantity: { avg: 1 }, note: "Behavior milkable" },
      { item: 4, type: "entityDrop", quantity: { avg: 1 }, note: "Harvested" },
      { item: 5, type: "traderBuys", quantity: { avg: 1 }, price: 1 },
    ];
    expect(entitySections(rows).map((s) => [s.kind, s.note, s.rows.map((r) => r.item)])).toEqual([
      ["drops", undefined, [1]],
      ["harvest", undefined, [0, 4]],
      ["behavior", "Behavior milkable", [2]],
      ["sells", undefined, [3]],
      ["buys", undefined, [5]],
    ]);
  });

  it("gives nothing for nothing", () => {
    expect(entitySections([])).toEqual([]);
  });
});

const v = (code: string, name: string, sources: EntitySource[]): EntityVariant => ({ code, name, sources });

describe("variantGroups and groupLabel", () => {
  it("shows variants that give the same as one, folding what differs in brackets", () => {
    const same: EntitySource[] = [{ item: 0, type: "traderSells", quantity: { avg: 8 }, price: 2 }];
    const groups = variantGroups([
      v("game:trader-female-agriculture-cold", "Agriculture trader (cold)", same),
      v("game:trader-male-agriculture-cold", "Agriculture trader (cold)", same),
      v("game:trader-male-agriculture-desert", "Agriculture trader (desert)", same),
      v("game:trader-male-commodities-cold", "Commodities trader (cold)", [{ item: 1, type: "traderSells", quantity: { avg: 2 } }]),
    ]);
    expect(groups.map((g) => [g.codes.length, groupLabel(g.names)])).toEqual([
      [3, "Agriculture trader (cold, desert)"],
      [1, "Commodities trader (cold)"],
    ]);
    expect(groupLabel(["Surface Drifter"])).toBe("Surface Drifter");
  });
});

describe("mergeVariants and formatSpread", () => {
  const merged = mergeVariants([
    v("a", "A", [
      { item: 0, type: "entityDrop", quantity: { avg: 5, var: 1 }, note: "Harvested" },
      { item: 1, type: "entityDrop", quantity: { avg: 2, var: 1 }, note: "Harvested" },
    ]),
    v("b", "B", [
      { item: 0, type: "entityDrop", quantity: { avg: 3 }, note: "Harvested" },
      { item: 1, type: "entityDrop", quantity: { avg: 2, var: 1 }, note: "Harvested" },
      { item: 1, type: "entityDrop", quantity: { avg: 1 } },
    ]),
  ]);

  it("keeps one row per item and way of getting it, with the spread and how many variants give it", () => {
    expect(merged.map((r) => [r.item, r.note, formatSpread(r.quantity), r.variants])).toEqual([
      [0, "Harvested", "3–5", 2],
      [1, "Harvested", "2 ± 1", 2],
      [1, undefined, "1", 1],
    ]);
  });
});

describe("butcheryYields", () => {
  const minimal = JSON.parse(readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8")) as RecipeExport;
  // Outputs: 0 dead, 1 skinned, 2 hide (hook), 3 bled out, 4 blood (bucket), 5 bushmeat and
  // 6 fat (table, by condition), 7 hide and 8 bushmeat (field harvest, the meat by condition).
  const hare = minimal.recipes.find((r) => r.type === "butchery")!;

  it("gives each product harvested where it lies and through the hook and table, at the lowest and highest condition", () => {
    const y = butcheryYields([{ recipe: hare, variants: [0] }]);
    expect(y.conditions).toEqual([0.5, 1]);
    expect(y.groundShare).toBe(0.5);
    expect(y.stations).toEqual([
      { step: "skin", min: 1, max: 1 },
      { step: "butcher", min: 1, max: 1 },
    ]);
    // The carcass in its three states is not a product; blood is in litres.
    const same = (n: number) => [{ min: n, max: n }, { min: n, max: n }];
    expect(y.rows).toEqual([
      { code: "game:hide-raw-small", litres: false, ground: same(0.5), full: same(1) },
      { code: "butchering:bloodportion", litres: true, ground: null, full: same(0.2) },
      {
        code: "game:bushmeat-raw",
        litres: false,
        ground: [{ min: 0.75, max: 0.75, var: 0.25 }, { min: 1.5, max: 1.5, var: 0.5 }],
        full: [{ min: 1.5, max: 1.5, var: 0.5 }, { min: 3, max: 3, var: 1 }],
      },
      { code: "game:fat", litres: false, ground: null, full: [{ min: 0.5, max: 0.5 }, { min: 1, max: 1 }] },
    ]);
  });

  it("gives ranges of averages over several variants, counting for an item only the variants that give it", () => {
    const y = butcheryYields([{ recipe: hare, variants: [0, 1] }]);
    const meat = y.rows.find((r) => r.code === "game:bushmeat-raw")!;
    expect(meat.full).toEqual([{ min: 1, max: 1.5 }, { min: 2, max: 3 }]);
    expect(meat.ground).toEqual([{ min: 0.5, max: 0.75 }, { min: 1, max: 1.5 }]);
    expect(y.rows.find((r) => r.code === "game:fat")!.full).toEqual([{ min: 0.5, max: 0.5 }, { min: 1, max: 1 }]);
  });

  it("adds up what the hook and the table give of one item, scaling only the part condition scales", () => {
    const r: Recipe = structuredClone(hare);
    r.outputs[2] = { ...r.outputs[2]!, code: "game:fat" };
    expect(butcheryYields([{ recipe: r, variants: [0] }]).rows.find((x) => x.code === "game:fat")!.full).toEqual([
      { min: 1.5, max: 1.5 },
      { min: 2, max: 2 },
    ]);
  });

  it("gives one column per way for an export without the condition range", () => {
    const r: Recipe = structuredClone(hare);
    delete r.butchery!.condition;
    const y = butcheryYields([{ recipe: r, variants: [0] }]);
    expect(y.conditions).toEqual([1]);
    expect(y.rows.find((x) => x.code === "game:bushmeat-raw")!.full).toEqual([{ min: 3, max: 3, var: 1 }]);
  });

  it("stands in for the harvest only when the records cover every creature", () => {
    expect(butcheryCovers([hare], ["game:hare-european-adult-male", "game:hare-european-adult-female"])).toBe(true);
    expect(butcheryCovers([hare], ["game:hare-european-adult-male", "game:hare-arctic-adult-male"])).toBe(false);
    expect(butcheryCovers([], [])).toBe(false);
  });
});
