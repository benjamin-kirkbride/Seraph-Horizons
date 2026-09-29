import { describe, expect, it } from "vitest";
import type { SearchFile } from "../src/lib/format.ts";
import { ItemSearch } from "../src/lib/search.ts";

const items: [string, string, number][] = [
  ["game:ingot-copper", "Copper ingot", 1],
  ["game:ingot-tin", "Tin ingot", 1],
  ["game:ingot-tinbronze", "Tin bronze ingot", 1],
  ["game:ingotmold-burned", "Ingot mold", 1],
  ["game:metalbit-copper", "Copper bits", 1],
  ["game:copperingotcast-hidden", "Copper ingot cast", 0],
  ["game:saw-copper", "Copper saw", 1],
  ["game:plank-oak", "Board (Oak)", 1],
  ["game:ladder-wood-oak-north", "Oak ladder", 1],
  ["game:ladder-stick-north", "Crude ladder", 1],
  ["game:scythe-copper", "Copper Scythe", 1],
  ["game:cafe", "Café table", 1],
  ["game:rabbit", "Rabbit", 1],
  ["game:sawdust", "Sawdust", 1],
];
const file: SearchFile = {
  mods: ["game"],
  codes: items.map((i) => i[0]),
  names: items.map((i) => i[1]),
  mod: items.map(() => 0),
  flags: items.map((i) => i[2]),
};
const engine = new ItemSearch(file);
const codes = (q: string) => engine.search(q).map((i) => file.codes[i]);

describe("ItemSearch", () => {
  it("ranks Copper ingot first for `ingot cop`", () => {
    expect(codes("ingot cop")[0]).toBe("game:ingot-copper");
  });

  it("ignores word order and case", () => {
    expect(codes("COPPER INGOT")[0]).toBe("game:ingot-copper");
    expect(codes("ingot copper")[0]).toBe("game:ingot-copper");
  });

  it("requires every word to match somewhere", () => {
    expect(codes("tin ingot")).toEqual(["game:ingot-tin", "game:ingot-tinbronze"]);
    expect(codes("copper zzz")).toEqual([]);
  });

  it("puts a shorter name ahead when both match equally", () => {
    // "Copper ingot" and "Copper ingot cast" both match; the extra word costs.
    const hits = codes("copper ingot");
    expect(hits.indexOf("game:ingot-copper")).toBeLessThan(hits.indexOf("game:copperingotcast-hidden"));
  });

  it("prefers a name with fewer words beyond the query, even when it is longer", () => {
    const f: SearchFile = { mods: ["game"], codes: ["game:a", "game:b"], names: ["Oak log pile", "Oak logpiles"], mod: [0, 0], flags: [1, 1] };
    const s = new ItemSearch(f);
    expect(s.search("oak").map((i) => f.codes[i])).toEqual(["game:b", "game:a"]);
  });

  it("finds by code, with or without the domain", () => {
    expect(codes("game:saw-copper")[0]).toBe("game:saw-copper");
    expect(codes("ladder-stick")[0]).toBe("game:ladder-stick-north");
  });

  it("ranks a whole word over the start of one, and the start of a word over its inside", () => {
    // Shorter names would win the tie-break, so only the word scoring puts these first.
    expect(codes("saw")[0]).toBe("game:saw-copper");
    expect(codes("bit")).toEqual(["game:metalbit-copper", "game:rabbit"]);
  });

  it("matches the inside of a word when nothing better is there", () => {
    expect(codes("adder")).toEqual(expect.arrayContaining(["game:ladder-wood-oak-north", "game:ladder-stick-north"]));
  });

  it("folds accents", () => {
    expect(codes("cafe")).toEqual(["game:cafe"]);
  });

  it("returns nothing for an empty query and respects the limit", () => {
    expect(codes("   ")).toEqual([]);
    expect(engine.search("copper", 2)).toHaveLength(2);
  });
});
