import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Recipe, RecipeExport } from "../src/lib/export.ts";
import {
  butcheryEntities,
  butcheryOutput,
  butcheryStations,
  butcheryStages,
  butcheryVariantsFor,
  cardOutputs,
  constructionStages,
  constructionTotals,
  cycleAt,
  efficiencyRange,
  focusVariants,
  formatChance,
  panChance,
  formatRange,
  formatRatio,
  gridCells,
  pageLinks,
  slotStacks,
  giveRows,
  sourceRows,
  stackAmount,
  typePage,
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

describe("sourceRows", () => {
  const drop = (from: string, fromName: string, avg = 1) => ({ type: "blockDrop" as const, from, fromName, quantity: { avg } });

  it("keeps one row for the orientations of a block, which share a name", () => {
    const rows = sourceRows([
      drop("game:torchholder-aged-filled-north", "Aged torch holder"),
      drop("game:torchholder-aged-filled-east", "Aged torch holder"),
      drop("game:torchholder-brass-filled-north", "Brass torch holder"),
      drop("game:torchholder-aged-filled-south", "Aged torch holder"),
    ]);
    expect(rows.map((r) => r.from)).toEqual(["game:torchholder-aged-filled-north", "game:torchholder-brass-filled-north"]);
  });

  it("keeps rows that differ in quantity, kind or tool", () => {
    const rows = sourceRows([
      drop("game:a-north", "A"),
      drop("game:a-east", "A", 2),
      { ...drop("game:a-south", "A"), tool: "knife" },
      { ...drop("game:a-west", "A"), type: "traderSells" as const },
    ]);
    expect(rows).toHaveLength(4);
  });

  it("lists what a block gives: drops first, then panning by chance, a doubled item added up", () => {
    const panned = (item: number, chancePerPan: number) => ({ type: "other" as const, quantity: { avg: 0.2 }, note: "Panned", extra: { chancePerPan }, item });
    const broken = { type: "blockDrop" as const, quantity: { avg: 1 }, item: 9 };
    const rows = giveRows([panned(1, 0.01), panned(2, 0.15), broken, panned(2, 0.24), broken, panned(3, 0.05)]);
    expect(rows.map((r) => [r.item, r.chance])).toEqual([
      [9, undefined],
      [2, 0.39],
      [3, 0.05],
      [1, 0.01],
    ]);
  });

  // As the exporter writes panning: one source per pannable block, the declared chance as
  // quantity and the real chance of one pan in extra (values from the 1.22.7 export).
  const pan = (rock: string, chancePerPan: number, block = "gravel", extra: Record<string, unknown> = {}) => ({
    type: "other" as const,
    from: `game:${block}-${rock}`,
    fromName: `${rock} ${block}`,
    quantity: { avg: 0.075 },
    note: "Panned",
    extra: { chancePerPan, ...extra },
  });

  it("folds the variants of one pannable block into one row with the range of their chances", () => {
    const rows = sourceRows([
      pan("andesite", 0.05358),
      pan("basalt", 0.05358),
      pan("andesite", 0.05892, "richgravel"),
      pan("chalk", 0.049, "richgravel"),
      pan("granite", 0.06975, "richgravel"),
      pan("andesite", 0.05358, "sandwavy"),
    ]);
    expect(rows.map((r) => [r.from, r.chance, (r.alsoFrom ?? []).map((a) => a.from)])).toEqual([
      ["game:gravel-andesite", { min: 0.05358, max: 0.05358 }, ["game:gravel-basalt"]],
      ["game:richgravel-andesite", { min: 0.049, max: 0.06975 }, ["game:richgravel-chalk", "game:richgravel-granite"]],
      ["game:sandwavy-andesite", { min: 0.05358, max: 0.05358 }, []],
    ]);
    expect(rows[1]!.alsoFrom![0]!.extra).toEqual({ chancePerPan: 0.049 });
  });

  it("adds up the chances of a block whose list holds the item twice", () => {
    const rows = sourceRows([pan("granite", 0.1), pan("granite", 0.15), pan("basalt", 0.25)]);
    expect(rows).toHaveLength(1);
    expect(rows[0]!.chance).toEqual({ min: 0.25, max: 0.25 });
    expect(rows[0]!.alsoFrom!.map((a) => a.from)).toEqual(["game:gravel-basalt"]);
  });

  it("keeps panned rows apart when the rest of their extra differs", () => {
    const gear = { stat: "rustyGearDropRate" };
    const rows = sourceRows([pan("andesite", 0.0001382, "gravel", gear), pan("basalt", 0.0001382, "gravel", gear), pan("chalk", 0.0001382)]);
    expect(rows.map((r) => r.alsoFrom?.length ?? 0)).toEqual([1, 0]);
  });

  it("takes the chance per pan from extra, and the declared chance from an export without it", () => {
    expect(panChance({ type: "other", from: "game:bonysoil", quantity: { avg: 0.3 }, note: "Panned", extra: { chancePerPan: 0.2206 } })).toBe(0.2206);
    expect(panChance({ type: "other", from: "game:bonysoil", quantity: { avg: 0.3 }, note: "Panned" })).toBe(0.3);
    expect(sourceRows([{ type: "other", from: "game:bonysoil", quantity: { avg: 0.3 }, note: "Panned" }])[0]!.chance).toEqual({ min: 0.3, max: 0.3 });
  });

  it("folds harvested block variants only when the quantities agree, and not block drops", () => {
    const harvest = (code: string, avg = 1) => ({ type: "other" as const, from: code, fromName: code, quantity: { avg }, note: "Harvested" });
    expect(sourceRows([harvest("game:bush-a"), harvest("game:bush-b")])).toHaveLength(1);
    expect(sourceRows([harvest("game:bush-a"), harvest("game:bush-b", 2)])).toHaveLength(2);
    expect(sourceRows([harvest("game:bush-a")])[0]!.chance).toBeUndefined();
    expect(sourceRows([drop("game:ore-granite", "Granite ore"), drop("game:ore-basalt", "Basalt ore")])).toHaveLength(2);
  });

  it("lists a folded variant once per name, like the orientations of a block", () => {
    const rows = sourceRows([pan("andesite", 0.05), { ...pan("andesite", 0.05), from: "game:gravel-andesite-free" }, pan("basalt", 0.05)]);
    expect(rows).toHaveLength(1);
    expect(rows[0]!.alsoFrom!.map((a) => a.from)).toEqual(["game:gravel-basalt"]);
  });
});

describe("formatChance", () => {
  it("shows a chance as a percentage, keeping two significant digits below 1%", () => {
    expect(formatChance(0.2206)).toBe("22.06%");
    expect(formatChance(0.0125)).toBe("1.25%");
    expect(formatChance(0.0006912)).toBe("0.069%");
    expect(formatChance(0.0001382)).toBe("0.014%");
    expect(formatChance(1)).toBe("100%");
    expect(formatChance(undefined)).toBe("");
  });

  it("shows a range when the blocks of a row differ", () => {
    expect(formatChance(0.049, 0.06975)).toBe("4.9–6.98%");
    expect(formatChance(0.05358, 0.05358)).toBe("5.36%");
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

  it("lists the stations of the stage that gives an output, one per name and yield, highest first", () => {
    const r: Recipe = structuredClone(hare);
    r.ingredients[1]!.extra = { efficiency: { "x:hook-copper": 1, "x:hook-iron": 1.2, "x:hook-tin": 1, "x:hook-flint": 0.8 } };
    const names: Record<string, string> = { "x:hook-copper": "Hook", "x:hook-iron": "Advanced hook", "x:hook-tin": "Hook", "x:hook-flint": "Primitive hook" };
    expect(butcheryStations(r, 2, (c) => names[c] ?? c)).toEqual([
      { name: "Advanced hook", multiplier: 1.2 },
      { name: "Hook", multiplier: 1 },
      { name: "Primitive hook", multiplier: 0.8 },
    ]);
    // The table's stage: its own station, not the hook.
    expect(butcheryStations(r, 5, (c) => c)).toEqual([{ name: "butchering:butchertable-simple-north", multiplier: 1 }]);
    expect(butcheryStations(r, 8, (c) => c)).toEqual([]);
  });
});

describe("typePage", () => {
  const grid = { start: 100, count: 50 };

  it("gives a page's recipe indices from the type's start", () => {
    expect(typePage(grid, 1, 24)).toEqual({ page: 1, pages: 3, first: 0, indices: Array.from({ length: 24 }, (_, i) => 100 + i) });
    expect(typePage(grid, 3, 24)).toEqual({ page: 3, pages: 3, first: 48, indices: [148, 149] });
  });

  it("clamps a page out of range", () => {
    expect(typePage(grid, 99, 24).page).toBe(3);
    expect(typePage(grid, 0, 24).page).toBe(1);
    expect(typePage({ start: 0, count: 0 }, 1, 24)).toEqual({ page: 1, pages: 1, first: 0, indices: [] });
  });
});

describe("pageLinks", () => {
  it("links the ends and the pages near the current one, with a gap marker between", () => {
    expect(pageLinks(1, 1)).toEqual([1]);
    expect(pageLinks(1, 5)).toEqual([1, 2, 3, 4, 5]);
    expect(pageLinks(10, 192)).toEqual([1, null, 8, 9, 10, 11, 12, null, 192]);
    expect(pageLinks(192, 192)).toEqual([1, null, 190, 191, 192]);
  });

  it("shows a single missing page instead of a gap", () => {
    expect(pageLinks(5, 9)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9]);
  });
});
