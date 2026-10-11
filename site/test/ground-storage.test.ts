import { describe, expect, it } from "vitest";
import { groundStorageLines } from "../src/lib/ground-storage.ts";

describe("groundStorageLines", () => {
  it("describes a pile with its capacity, clicks, solid top and height", () => {
    expect(
      groundStorageLines({ layout: "stacking", capacity: 64, transfer: 1, bulkTransfer: 4, solidTop: true, fullHeight: 1 }),
    ).toEqual(["Pile: up to 64 per block, 1 per click, 4 per Ctrl click.", "A full pile can be built on.", "A full pile is 1 block tall."]);
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

  it("calls messy12 a loose pile and leaves out a Ctrl click that moves no more", () => {
    expect(groundStorageLines({ layout: "messy12", capacity: 12, transfer: 1, bulkTransfer: 4 })).toEqual([
      "Loose pile: up to 12 per block, 1 per click, 4 per Ctrl click.",
    ]);
    expect(groundStorageLines({ layout: "stacking", capacity: 4, transfer: 1, bulkTransfer: 1 })).toEqual(["Pile: up to 4 per block, 1 per click."]);
  });

  it("names the placed layouts", () => {
    expect(groundStorageLines({ layout: "quadrants", capacity: 4 })).toEqual(["Placeable (quadrants): up to 4 per block."]);
    expect(groundStorageLines({ layout: "wallhalves", capacity: 2 })).toEqual(["Placeable (against a wall): up to 2 per block."]);
    expect(groundStorageLines({ layout: "singlecenter", capacity: 1 })).toEqual(["Placeable (centre): one per block."]);
  });
});
