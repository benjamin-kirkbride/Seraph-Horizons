import { describe, expect, it } from "vitest";
import { entitySections } from "../src/lib/entity-view.ts";
import type { EntitySource } from "../src/lib/format.ts";

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
