import { describe, expect, it } from "vitest";
import { groundStorageLines, itemPileLines, pileable } from "../src/lib/ground-storage.ts";

describe("groundStorageLines", () => {
  it("describes a pile with its capacity, clicks, solid top and height", () => {
    expect(
      groundStorageLines({ layout: "stacking", capacity: 1024, transfer: 1, bulkTransfer: 16, solidTop: true, fullHeight: 1 }),
    ).toEqual([
      "Pile: up to 1024 per block, 1 per click, 16 per Ctrl click.",
      "A full pile can be built on, more piles included, so it grows past one block.",
      "A full pile is 1 block tall.",
    ]);
  });

  it("says when Ctrl is needed, how high piles go and a fractional height", () => {
    expect(
      groundStorageLines({ layout: "stacking", capacity: 16, transfer: 2, bulkTransfer: 8, requiresCtrl: true, solidTop: true, maxPilesHigh: 3, fullHeight: 0.5 }),
    ).toEqual([
      "Pile: up to 16 per block, 2 per click, 8 per Ctrl click.",
      "Hold Ctrl as well to put it down.",
      "A full pile can be built on, up to 3 piles high.",
      "A full pile is 0.5 blocks tall.",
    ]);
  });

  it("says a pile that cannot be built on stays one block high", () => {
    expect(groundStorageLines({ layout: "stacking", capacity: 4096, transfer: 2, bulkTransfer: 64, requiresCtrl: true })).toEqual([
      "Pile: up to 4096 per block, 2 per click, 64 per Ctrl click.",
      "Hold Ctrl as well to put it down.",
      "A pile is one block high at most.",
    ]);
  });

  it("calls messy12 a loose pile and leaves out a Ctrl click that moves no more", () => {
    expect(groundStorageLines({ layout: "messy12", capacity: 12, transfer: 1, bulkTransfer: 4 })).toEqual([
      "Loose pile: up to 12 per block, 1 per click, 4 per Ctrl click.",
    ]);
    expect(groundStorageLines({ layout: "stacking", capacity: 4, transfer: 1, bulkTransfer: 1 })).toEqual([
      "Pile: up to 4 per block, 1 per click.",
      "A pile is one block high at most.",
    ]);
  });

  it("names the placed layouts", () => {
    expect(groundStorageLines({ layout: "quadrants", capacity: 4 })).toEqual(["Placeable (quadrants): up to 4 per block."]);
    expect(groundStorageLines({ layout: "wallhalves", capacity: 2 })).toEqual(["Placeable (against a wall): up to 2 per block."]);
    expect(groundStorageLines({ layout: "singlecenter", capacity: 1 })).toEqual(["Placeable (centre): one per block."]);
  });
});

describe("itemPileLines", () => {
  it("describes a coal pile, which grows upward", () => {
    expect(itemPileLines({ capacity: 16, transfer: 2, bulkTransfer: 2 })).toEqual([
      "Pile: up to 16 per block, 2 per click.",
      "A full pile can be built on, more piles included, so it grows past one block.",
    ]);
  });
});

describe("pileable", () => {
  it("is yes for a pile and an item pile, with what one block holds", () => {
    expect(pileable({ groundStorage: { layout: "stacking", capacity: 2048, transfer: 2, bulkTransfer: 32 } })).toBe("Yes, up to 2048 per block");
    expect(pileable({ itemPile: { capacity: 16, transfer: 2, bulkTransfer: 2 } })).toBe("Yes, up to 16 per block");
  });

  it("is no for a loose heap, a placed item and an item that does not go on the ground", () => {
    expect(pileable({ groundStorage: { layout: "messy12", capacity: 12, transfer: 1, bulkTransfer: 4 } })).toBe("No");
    expect(pileable({ groundStorage: { layout: "quadrants", capacity: 4 } })).toBe("No");
    expect(pileable({})).toBe("No");
    expect(pileable(undefined)).toBe("No");
  });
});
