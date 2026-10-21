import { describe, expect, it } from "vitest";
import { mergeVariants } from "../src/lib/entity-view.ts";
import { isKept, sourceRows } from "../src/lib/recipe-view.ts";
import { anyPackTrade, packTrade, tradeNotes, tradePrice, tradePriceText } from "../src/lib/trades.ts";

// As the exporter writes a pack trader's trades (tools/recipe-export/Items/PackTradeSources.cs).
const carpenter = {
  type: "traderSells" as const,
  from: "seraphhorizons:trader-male-carpenter-temperate",
  fromName: "Carpenter",
  quantity: { avg: 16 },
  price: 4,
};
const shelf = { entityType: "seraphhorizons:trader-carpenter", traderType: "carpenter", list: "core", priceMin: 2, priceMax: 6 };
const schematic = {
  entityType: "seraphhorizons:trader-carpenter",
  traderType: "carpenter",
  catalog: "schematics",
  priceMin: 60,
  priceMax: 60,
  standingTier: 2,
  standingTierName: "regular",
};

describe("pack trades", () => {
  it("shows the whole range, or one price when there is no spread", () => {
    expect(tradePrice({ ...carpenter, extra: shelf })).toBe("2–6");
    expect(tradePriceText({ ...carpenter, extra: shelf })).toBe("2–6 rusty gears");
    expect(tradePrice({ price: 60, extra: schematic })).toBe("60");
    expect(tradePriceText({ price: 60, extra: schematic })).toBe("60 rusty gears");
  });

  it("leaves a vanilla trader's trade as it was", () => {
    const vanilla = { price: 4, extra: { entityType: "game:trader", priceVar: 1 } };
    expect(packTrade(vanilla)).toBeUndefined();
    expect(tradePrice(vanilla)).toBe("4 ± 1");
    expect(tradePriceText(vanilla)).toBe("4 rusty gears");
    expect(tradeNotes(vanilla)).toEqual([]);
    expect(anyPackTrade([vanilla])).toBe(false);
  });

  it("says what limits a trade: the catalog, the standing, the region, rotating and rare stock", () => {
    expect(tradeNotes({ extra: schematic })).toEqual(["schematic catalog, flat price", "from standing Regular"]);
    expect(tradeNotes({ extra: { ...shelf, list: "rotating", region: "cold", rare: true, standingTier: 3, standingTierName: "trusted" } })).toEqual([
      "from standing Trusted",
      "cold regions only",
      "in rotating stock",
      "rare stock",
    ]);
    expect(tradeNotes({ extra: { ...shelf, region: "igneous", playerSupplied: true } })).toEqual([
      "igneous rock regions only",
      "once players have sold enough in the region",
    ]);
    expect(anyPackTrade([{ extra: schematic }])).toBe(true);
  });

  it("keeps an item's trades apart that differ only in what limits them", () => {
    const rows = sourceRows([
      { ...carpenter, extra: shelf },
      { ...carpenter, extra: { ...shelf, region: "cold" } },
      { ...carpenter, extra: shelf },
    ]);
    expect(rows.map((r) => r.extra?.region)).toEqual([undefined, "cold"]);
  });

  it("merges a range across variants from each trade's min and max", () => {
    const rows = mergeVariants([
      { code: "a", name: "Carpenter", sources: [{ type: "traderSells", item: 1, quantity: { avg: 16 }, price: 4, extra: shelf }] },
      { code: "b", name: "Carpenter", sources: [{ type: "traderSells", item: 1, quantity: { avg: 16 }, price: 5, extra: { ...shelf, priceMax: 9 } }] },
    ]);
    expect(rows[0]!.price).toEqual({ min: 2, max: 9 });
  });
});

describe("isKept", () => {
  it("is a grid ingredient the game does not consume and that is no tool", () => {
    expect(isKept({ extra: { consumed: false } })).toBe(true);
    expect(isKept({ isTool: true, extra: { consumed: false } })).toBe(false);
    expect(isKept({})).toBe(false);
    expect(isKept(undefined)).toBe(false);
  });
});
