import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Recipe, RecipeExport } from "../src/lib/export.ts";
import {
  butcheryEntities,
  butcheryOutput,
  butcheryStages,
  butcheryVariantsFor,
  cardOutputs,
  constructionStages,
  constructionTotals,
  cycleAt,
  efficiencyRange,
  distinctSources,
  focusVariants,
  formatRange,
  formatRatio,
  gridCells,
  slotStacks,
  stackAmount,
  voxelLayers,
} from "../src/lib/recipe-view.ts";

const minimal = JSON.parse(
  readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8"),
) as RecipeExport;
const byId = (id: string) => minimal.recipes.find((r) => r.id === id)!;
const ladder = byId("grid|game:recipes/grid/ladder.json|0");
const waterwheel = byId("construction|game:waterwheel-3m-north|2");
const hare = byId("butchery|game:hare|butchering:deadhare-male-european-1-dead");

describe("gridCells", () => {
  it("puts the saw in row 0 column 1 and leaves row 2 column 1 empty for SWS/SPS/S_S", () => {
    // Ingredient order in the ladder recipe: 0 = P (plank), 1 = S (stick), 2 = W (saw).
    expect(gridCells(ladder)).toEqual([
      [1, 2, 1],
      [1, 0, 1],
      [1, null, 1],
    ]);
  });

  it("pads short rows and ignores keys no ingredient has", () => {
    const r: Recipe = { ...ladder, grid: { width: 3, height: 2, shapeless: false, pattern: ["SX", "W"] } };
    expect(gridCells(r)).toEqual([
      [1, null, null],
      [2, null, null],
    ]);
  });
});

describe("voxelLayers", () => {
  it("reads # as filled, bottom layer first", () => {
    const r = { ...ladder, voxels: [["#_", "_#"], ["__", "#_"]] } as Recipe;
    expect(voxelLayers(r)).toEqual([
      [
        [true, false],
        [false, true],
      ],
      [
        [false, false],
        [true, false],
      ],
    ]);
  });
});

describe("formatting", () => {
  it("writes alloy shares as percentages", () => {
    expect(formatRatio(0.88, 0.92)).toBe("88–92%");
    expect(formatRatio(0.08, 0.12)).toBe("8–12%");
    expect(formatRatio(0.5, 0.5)).toBe("50%");
    expect(formatRatio(0.333, 0.667)).toBe("33.3–66.7%");
  });

  it("shows litres for liquids and a count only above one", () => {
    expect(stackAmount({ quantity: 200, litres: 2 })).toBe("2 L");
    expect(stackAmount({ quantity: 3 })).toBe("×3");
    expect(stackAmount({ quantity: 1 })).toBe("");
  });

  it("writes cooking ranges", () => {
    expect(formatRange(0, 1)).toBe("0–1");
    expect(formatRange(2, 2)).toBe("2");
  });
});

describe("variants and cycling", () => {
  it("shows only the birch variant on the birch plank's page", () => {
    expect(focusVariants(ladder, { code: "game:plank-birch", as: "ingredient" })).toEqual([1]);
    // The stick is in both, so both cycle.
    expect(focusVariants(ladder, { code: "game:stick", as: "ingredient" })).toEqual([0, 1]);
    // Nothing names this code: fall back to all.
    expect(focusVariants(ladder, { code: "game:plank-ebony", as: "ingredient" })).toEqual([0, 1]);
  });

  it("cycles with wrap-around, also for negative steps", () => {
    expect(cycleAt(["a", "b", "c"], 4)).toBe("b");
    expect(cycleAt(["a", "b", "c"], -1)).toBe("c");
    expect(cycleAt([], 3)).toBeUndefined();
  });

  it("takes a slot's stacks from the variant, with the slot's litres", () => {
    const barrel = byId("barrel|game:recipes/barrel/hide.json|0");
    const noLitres: Recipe = {
      ...barrel,
      variants: [{ ingredients: [[{ code: "game:waterportion", kind: "item", quantity: 200 }], []], outputs: [] }],
    };
    expect(slotStacks(noLitres, 0, 0)).toEqual([{ code: "game:waterportion", kind: "item", quantity: 200, litres: 2 }]);
    // An empty slot in the variant falls back to the definition.
    expect(slotStacks(noLitres, 0, 1)).toEqual([{ code: "game:hide-raw-small", kind: "item", quantity: 1 }]);
  });
});

describe("distinctSources", () => {
  const drop = (from: string, fromName: string, avg = 1) => ({ type: "blockDrop" as const, from, fromName, quantity: { avg } });

  it("keeps one row for the orientations of a block, which share a name", () => {
    const rows = distinctSources([
      drop("game:torchholder-aged-filled-north", "Aged torch holder"),
      drop("game:torchholder-aged-filled-east", "Aged torch holder"),
      drop("game:torchholder-brass-filled-north", "Brass torch holder"),
      drop("game:torchholder-aged-filled-south", "Aged torch holder"),
    ]);
    expect(rows.map((r) => r.from)).toEqual(["game:torchholder-aged-filled-north", "game:torchholder-brass-filled-north"]);
  });

  it("keeps rows that differ in quantity, kind or tool", () => {
    const rows = distinctSources([
      drop("game:a-north", "A"),
      drop("game:a-east", "A", 2),
      { ...drop("game:a-south", "A"), tool: "knife" },
      { ...drop("game:a-west", "A"), type: "traderSells" as const },
    ]);
    expect(rows).toHaveLength(4);
  });
});

describe("construction", () => {
  it("lists the stages in build order, the placed block first", () => {
    expect(constructionStages(waterwheel)).toEqual([
      { ingredients: [] },
      { ingredients: [0] },
      { ingredients: [1, 2] },
      { ingredients: [], action: "Launch" },
    ]);
    expect(constructionStages(ladder)).toEqual([]);
  });

  it("totals what the variant consumes, slot by slot", () => {
    // Variant 1 binds wood to oak.
    expect(constructionTotals(waterwheel, 1)).toEqual([
      [{ code: "game:supportbeam-oak", kind: "block", quantity: 16 }],
      [{ code: "game:plank-oak", kind: "item", quantity: 48 }],
      [{ code: "game:resin", kind: "item", quantity: 4 }],
    ]);
  });

  it("adds up slots that accept the same stacks", () => {
    const resin = waterwheel.ingredients[2]!;
    const r: Recipe = {
      ...waterwheel,
      ingredients: [...waterwheel.ingredients, { ...resin, quantity: 6 }],
      variants: waterwheel.variants.map((v) => ({ ...v, ingredients: [...v.ingredients, v.ingredients[2]!] })),
      construction: { stages: [{ ingredients: [] }, { ingredients: [0] }, { ingredients: [1, 2] }, { ingredients: [3] }] },
    };
    expect(constructionTotals(r, 0).map((slot) => slot.map((s) => [s.code, s.quantity]))).toEqual([
      [["game:supportbeam-birch", 16]],
      [["game:plank-birch", 48]],
      [["game:resin", 10]],
    ]);
  });
});

describe("butchery", () => {
  it("lists the stages in order, with the field harvest last", () => {
    expect(butcheryStages(hare).map((s) => s.step)).toEqual(["pickUp", "skin", "bleed", "butcher", "harvest"]);
    expect(butcheryStages(ladder)).toEqual([]);
  });

  it("gives each output's stacks and yield for one variant, and null where the variant gives nothing", () => {
    expect(butcheryOutput(hare, 0, 5)).toEqual({
      stacks: [{ code: "game:bushmeat-raw", kind: "item", quantity: 3 }],
      yield: { avg: 3, var: 1 },
    });
    expect(butcheryOutput(hare, 1, 6)).toBeNull();
    expect(butcheryOutput(hare, 0, 4)!.stacks).toEqual([{ code: "butchering:bloodportion", kind: "item", quantity: 20, litres: 0.2 }]);
  });

  it("cycles a carcass through its other coats", () => {
    const r: Recipe = { ...hare, outputs: hare.outputs.map((o, i) => (i === 0 ? { ...o, extra: { alternatives: ["butchering:deadhare-male-european-2-dead"] } } : o)) };
    expect(butcheryOutput(r, 0, 0)!.stacks.map((s) => s.code)).toEqual([
      "butchering:deadhare-male-european-1-dead",
      "butchering:deadhare-male-european-2-dead",
    ]);
  });

  it("heads the card with the carcass rather than every output", () => {
    expect(cardOutputs(hare, 0)).toEqual([{ code: "butchering:deadhare-male-european-1-dead", kind: "item", quantity: 1 }]);
    expect(cardOutputs(ladder, 0).map((s) => s.code)).toEqual(["game:ladder-wood-north"]);
  });

  it("names the creatures of a variant and finds the variants of some creatures", () => {
    expect(butcheryEntities(hare, 1)).toEqual([{ code: "game:hare-european-adult-female", name: "European hare (female)" }]);
    expect(butcheryVariantsFor(hare, ["game:hare-european-adult-female", "game:wolf-male"])).toEqual([1]);
    expect(focusVariants(hare, { code: "game:hare-european-adult-male", as: "entity" })).toEqual([0]);
    expect(focusVariants(hare, { code: "game:fat", as: "output" })).toEqual([0]);
  });

  it("reads a station's yield range from its efficiency per block", () => {
    expect(efficiencyRange({ ...hare.ingredients[6]!, extra: { efficiency: { a: 0.8, b: 1.2, c: 1 } } })).toEqual({ min: 0.8, max: 1.2 });
    expect(efficiencyRange(hare.ingredients[2])).toBeNull();
  });
});
