import { describe, expect, it } from "vitest";
import { matchSorted, variantAllowed } from "../src/lib/wildcard.ts";

const codes = [
  "game:ingot-copper",
  "game:ingot-gold",
  "game:ingotmold-burned",
  "game:plank-birch",
  "game:plank-oak",
  "game:planks-oak-hor",
  "mymod:plank-oak",
].sort();
const pick = (pattern: string, c = {}) => matchSorted(codes, pattern, c).map((i) => codes[i]);

describe("matchSorted", () => {
  it("matches `*` within the domain it names", () => {
    expect(pick("game:plank-*")).toEqual(["game:plank-birch", "game:plank-oak"]);
  });

  it("matches a `*` domain against every mod", () => {
    expect(pick("*:plank-*")).toEqual(["game:plank-birch", "game:plank-oak", "mymod:plank-oak"]);
    expect(pick("*:plank-oak")).toEqual(["game:plank-oak", "mymod:plank-oak"]);
  });

  it("filters variants on the path's `*`, not on a `*` domain", () => {
    expect(pick("*:plank-*", { "*": { allowedVariants: ["oak"] } })).toEqual(["game:plank-oak", "mymod:plank-oak"]);
  });

  it("matches `*` in the middle and at the start of the path", () => {
    expect(pick("game:*-oak")).toEqual(["game:plank-oak"]);
    expect(pick("game:planks-*-hor")).toEqual(["game:planks-oak-hor"]);
  });

  it("returns a plain code only when it exists", () => {
    expect(pick("game:ingot-gold")).toEqual(["game:ingot-gold"]);
    expect(pick("game:ingot-silver")).toEqual([]);
  });

  it("applies allowed and skipped variants to the first `*`", () => {
    expect(pick("game:ingot-*", { "*": { allowedVariants: ["gold", "iron"] } })).toEqual(["game:ingot-gold"]);
    expect(pick("game:ingot-*", { "*": { skipVariants: ["gold"] } })).toEqual(["game:ingot-copper"]);
  });

  it("reads a path starting with @ as a regular expression", () => {
    expect(pick("game:@plank-(oak|birch)")).toEqual(["game:plank-birch", "game:plank-oak"]);
    expect(pick("game:@(")).toEqual([]);
  });

  it("treats {name} as a wildcard constrained by its own filter", () => {
    expect(pick("game:plank-{wood}", { wood: { allowedVariants: ["oak"] } })).toEqual(["game:plank-oak"]);
  });
});

describe("variantAllowed", () => {
  it("allows everything when there is no filter", () => {
    expect(variantAllowed("oak", {})).toBe(true);
    expect(variantAllowed("oak", { allowedVariants: [] })).toBe(true);
  });
  it("skip wins over allow", () => {
    expect(variantAllowed("oak", { allowedVariants: ["oak"], skipVariants: ["oak"] })).toBe(false);
  });
});
