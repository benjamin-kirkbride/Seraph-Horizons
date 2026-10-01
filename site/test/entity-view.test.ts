import { describe, expect, it } from "vitest";
import { entitySections, formatSpread, groupLabel, mergeVariants, variantGroups } from "../src/lib/entity-view.ts";
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
