import { writeFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { main } from "../src/cli.js";
import { checkCrossReferences } from "../src/checks.js";
import { Pipeline } from "../src/pipeline.js";
import { ErrorReport } from "../src/report.js";
import { example, EXAMPLE_FILE, tempDir, type Loose } from "./helpers.js";

const pipeline = Pipeline.fromDir();

/** [kind, path] of every problem, or [] when the document is valid. */
function problems(doc: Loose): [string, string][] {
  const r = pipeline.process(doc);
  return r.ok ? [] : r.report.problems.map((p) => [p.kind, p.path]);
}

function only(doc: Loose) {
  const r = pipeline.process(doc);
  if (r.ok) throw new Error("expected the document to fail");
  expect(r.report.total).toBe(1);
  return r.report.problems[0]!;
}

describe("validate: the example", () => {
  it("passes", () => {
    expect(problems(example())).toEqual([]);
  });
});

describe("validate: cross-references", () => {
  it("rejects a recipe whose type is not in recipeTypes", () => {
    const d = example();
    delete d.recipeTypes.barrel;
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["recipe-type", "/recipes/1/type", '"barrel"']);
  });

  it("rejects a recipe type whose count is wrong", () => {
    const d = example();
    d.recipeTypes.grid.count = 2;
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["recipe-type-count", "/recipeTypes/grid/count", "2"]);
    expect(p.expected).toContain("1");
  });

  it("rejects a recipe type listed with records that do not exist", () => {
    const d = example();
    d.recipeTypes.smithing = { name: "Smithing", count: 3, shape: "voxels" };
    expect(problems(d)).toEqual([["recipe-type-count", "/recipeTypes/smithing/count"]]);
  });

  it("rejects a recipe from an unknown mod", () => {
    const d = example();
    d.recipes[6].mod = "othermod";
    expect(problems(d)).toEqual([["recipe-mod", "/recipes/6/mod"]]);
  });

  it("rejects an item from an unknown mod", () => {
    const d = example();
    d.items["examplemod:widget"].mod = "nomod";
    expect(problems(d)).toEqual([["item-mod", "/items/examplemod:widget/mod"]]);
  });

  it("takes item values, switches and recipe switches, and rejects a negative value", () => {
    const d = example();
    expect(d.items["seraphhorizons:gear-steel"].valueSwitches).toEqual(["GearBlanks", "GearCutter"]);
    d.items["examplemod:widget"].value = -1;
    expect(problems(d).length).toBeGreaterThan(0);
    d.items["examplemod:widget"].value = 0;
    d.items["examplemod:widget"].switch = "not a switch";
    expect(problems(d).length).toBeGreaterThan(0);
  });

  it("rejects an item value that is not a finite number", () => {
    const report = new ErrorReport();
    const d = example();
    d.items["examplemod:widget"].value = Number.NaN;
    checkCrossReferences(d, report);
    expect(report.problems.map((p) => [p.kind, p.path])).toEqual([["item-value", "/items/examplemod:widget/value"]]);
  });

  it("accepts an export without variantGroups", () => {
    const d = example();
    delete d.variantGroups;
    expect(problems(d)).toEqual([]);
  });

  it("rejects a variant group member that is not an item", () => {
    const d = example();
    d.variantGroups["auto:game:plank"].members[1] = "game:plank-pine";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "variant-group-code",
      "/variantGroups/auto:game:plank/members/1",
      '"game:plank-pine"',
    ]);
  });

  it("rejects a code in two variant groups", () => {
    const d = example();
    d.variantGroups["auto:game:supportbeam"].members.push("game:plank-oak");
    const p = only(d);
    expect([p.kind, p.path]).toEqual(["variant-group-overlap", "/variantGroups/auto:game:supportbeam/members/2"]);
    expect(p.found).toContain("auto:game:plank");
  });

  it("rejects a blank variant group title", () => {
    const d = example();
    d.variantGroups["auto:game:plank"].title = "  ";
    expect(problems(d)).toEqual([["variant-group-title", "/variantGroups/auto:game:plank/title"]]);
  });

  it("rejects a variant group of one distinct code", () => {
    const d = example();
    d.variantGroups["auto:game:plank"].members = ["game:plank-oak", "game:plank-oak"];
    // The schema's uniqueItems stops this first; the check stands on its own too.
    expect(problems(d).map(([kind]) => kind)).not.toContain("variant-group-size");
    const report = new ErrorReport();
    checkCrossReferences(d, report);
    expect(report.problems.map((p) => [p.kind, p.path])).toEqual([["variant-group-size", "/variantGroups/auto:game:plank/members"]]);
  });

  it("rejects a variant ingredient code that is not an item", () => {
    const d = example();
    d.recipes[8].variants[1].ingredients[0][0].code = "game:plank-pine";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "variant-code",
      "/recipes/8/variants/1/ingredients/0/0/code",
      '"game:plank-pine"',
    ]);
  });

  it("rejects a variant output code that is not an item", () => {
    const d = example();
    d.recipes[0].variants[0].outputs[0].code = "game:ingot-bismuthbronze";
    expect(problems(d)).toEqual([["variant-code", "/recipes/0/variants/0/outputs/0/code"]]);
  });

  it("rejects duplicate recipe ids", () => {
    const d = example();
    d.recipes[1].id = "alloy|game:recipes/alloy/tinbronze.json|0";
    const p = only(d);
    expect([p.kind, p.path]).toEqual(["recipe-id-duplicate", "/recipes/1/id"]);
    expect(p.found).toContain("/recipes/0");
  });

  it("rejects unsorted recipe ids", () => {
    const d = example();
    [d.recipes[8], d.recipes[9]] = [d.recipes[9], d.recipes[8]];
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["recipe-id-order", "/recipes/9/id", '"grid|game:recipes/grid/ladder.json|0"']);
  });

  it("sorts ids by code unit, not by locale", () => {
    // "Z" (0x5A) sorts before "a" (0x61) ordinally, after it in a locale-aware sort.
    const d = example();
    d.recipes[0].id = "Zalloy|x|0";
    d.recipes[1].id = "abarrel|x|0";
    expect(problems(d)).toEqual([]);
    d.recipes[0].id = "zalloy|x|0";
    d.recipes[1].id = "Abarrel|x|0";
    expect(problems(d)).toEqual([["recipe-id-order", "/recipes/1/id"]]);
  });

  it("rejects a grid pattern row that is too short", () => {
    const d = example();
    d.recipes[8].grid.pattern[2] = "S_";
    const p = only(d);
    expect([p.kind, p.path]).toEqual(["grid-width", "/recipes/8/grid/pattern/2"]);
    expect(p.expected).toContain("3");
  });

  it("rejects a grid pattern with too few rows", () => {
    const d = example();
    d.recipes[8].grid.pattern.pop();
    expect(problems(d)).toEqual([["grid-height", "/recipes/8/grid/pattern"]]);
  });

  it("rejects a grid pattern key with no ingredient", () => {
    const d = example();
    d.recipes[8].grid.pattern[1] = "SXS";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["grid-key", "/recipes/8/grid/pattern/1", '"X" at column 1']);
  });

  it("rejects a construction stage index past the ingredients", () => {
    const d = example();
    d.recipes[4].construction.stages[3].ingredients = [3];
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["construction-ingredient", "/recipes/4/construction/stages/3/ingredients/0", "3"]);
  });

  it("rejects a construction ingredient two stages consume", () => {
    const d = example();
    d.recipes[4].construction.stages[3].ingredients = [0];
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "construction-ingredient",
      "/recipes/4/construction/stages/3/ingredients/0",
      "0, also in stage 1",
    ]);
  });

  it("rejects a construction ingredient no stage consumes", () => {
    const d = example();
    d.recipes[4].construction.stages[2].ingredients = [1];
    expect(problems(d)).toEqual([["construction-ingredient", "/recipes/4/ingredients/2"]]);
  });

  // recipes[2] is the hare's butchery.
  it("rejects a butchery ingredient two stages list, counting options and optional ones", () => {
    const d = example();
    d.recipes[2].butchery.stages[4].ingredients = [7];
    expect(problems(d)).toEqual([
      ["butchery-stage", "/recipes/2/butchery/stages/4/ingredients/0"],
      ["butchery-stage", "/recipes/2/ingredients/9"],
    ]);
    const p = pipeline.process(d);
    expect(p.ok ? "" : p.report.problems[0]!.found).toBe("7, also in stage 3");
  });

  it("rejects a butchery output no stage gives, and an index past the outputs", () => {
    const d = example();
    d.recipes[2].butchery.stages[2].outputs = [3, 9];
    expect(problems(d)).toEqual([
      ["butchery-stage", "/recipes/2/butchery/stages/2/outputs/1"],
      ["butchery-stage", "/recipes/2/outputs/4"],
    ]);
  });

  it("rejects butchery yields that do not line up with the outputs or the variant's stacks", () => {
    const d = example();
    d.recipes[2].butchery.variants[1].yields.pop();
    expect(problems(d)).toEqual([["butchery-variants", "/recipes/2/butchery/variants/1/yields"]]);

    const e = example();
    e.recipes[2].butchery.variants[1].yields[6] = { avg: 1 };
    const p = only(e);
    expect([p.kind, p.path]).toEqual(["butchery-variants", "/recipes/2/variants/1/outputs"]);
    expect(p.expected).toContain("game:fat");
  });

  it("rejects a creature condition range whose lowest is above its highest", () => {
    const d = example();
    d.recipes[2].butchery.condition = { min: 1, max: 0.5 };
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["butchery-condition", "/recipes/2/butchery/condition", "1 > 0.5"]);
    delete d.recipes[2].butchery.condition;
    expect(problems(d)).toEqual([]);
  });

  it("rejects a butchery block with one entry too few for the variants", () => {
    const d = example();
    d.recipes[2].butchery.variants.pop();
    expect(problems(d)).toEqual([["butchery-variants", "/recipes/2/butchery/variants"]]);
  });

  it("rejects a butchery stage step it does not know", () => {
    const d = example();
    d.recipes[2].butchery.stages[0].step = "dance";
    expect(problems(d).map(([kind]) => kind)).toContain("schema:enum");
  });

  // recipes[5] is wet sinew curing into dry sinew.
  it("rejects a transition with a second output or variant", () => {
    const d = example();
    d.recipes[5].outputs.push({ code: "game:fat", kind: "item", quantity: 1 });
    d.recipes[5].variants.push(structuredClone(d.recipes[5].variants[0]));
    expect(problems(d)).toEqual([
      ["transition-shape", "/recipes/5/outputs"],
      ["transition-shape", "/recipes/5/variants"],
    ]);
  });

  // recipes[12] is prime meat on the smoking rack: the meat, then the rack as a station.
  it("rejects a transition whose station has no station role, or that starts with a station", () => {
    const d = example();
    delete d.recipes[12].ingredients[1].role;
    expect(problems(d)).toEqual([["transition-shape", "/recipes/12/ingredients/1/role"]]);

    const e = example();
    e.recipes[12].ingredients[0].role = "station";
    expect(problems(e)).toEqual([["transition-shape", "/recipes/12/ingredients/0/role"]]);
  });

  it("rejects transition hours without an average", () => {
    const d = example();
    d.recipes[5].transition.transitionHours = { var: 2 };
    expect(problems(d)).toEqual([["schema:required", "/recipes/5/transition/transitionHours/avg"]]);
  });

  // recipes[11] is degreased gears pickling in vinegar, recipes[10] the oiled gear's lottery,
  // recipes[7] a gear cut on the gear cutter.
  it("rejects a tub whose failure is missing, past the outputs, or whose roles are not one each", () => {
    const d = example();
    delete d.recipes[11].tub.failure;
    expect(problems(d)).toEqual([
      ["tub-failure", "/recipes/11/tub"],
      ["tub-shape", "/recipes/11/outputs"],
    ]);

    const e = example();
    e.recipes[11].tub.failure = 2;
    expect(problems(e)).toEqual([["tub-failure", "/recipes/11/tub/failure"]]);

    const f = example();
    f.recipes[11].ingredients[2].role = "liquid";
    expect(problems(f)).toEqual([
      ["tub-shape", "/recipes/11/ingredients"],
      ["tub-shape", "/recipes/11/ingredients"],
    ]);
  });

  it("rejects tub hours of zero and an unknown tub property", () => {
    const d = example();
    d.recipes[11].tub.hours = 0;
    d.recipes[11].tub.colour = "green";
    expect(problems(d).map(([kind]) => kind).sort()).toEqual(["schema:additionalProperties", "schema:exclusiveMinimum"]);
  });

  it("rejects lottery chances that do not add up to 1", () => {
    const d = example();
    d.recipes[10].lottery.outcomes[1].chance = 0.8;
    expect(problems(d)).toEqual([["lottery-chance", "/recipes/10/lottery/outcomes"]]);
  });

  it("rejects a lottery output in two outcomes or in none, and an index past the outputs", () => {
    const d = example();
    d.recipes[10].lottery.outcomes[1].outputs = [0, 2];
    expect(problems(d)).toEqual([
      ["lottery-output", "/recipes/10/lottery/outcomes/1/outputs/0"],
      ["lottery-output", "/recipes/10/lottery/outcomes/1/outputs/1"],
      ["lottery-output", "/recipes/10/outputs/1"],
    ]);
  });

  it("rejects a machine whose kept part is a tool, whose worn part is not, or that names one ingredient twice", () => {
    const d = example();
    d.recipes[7].machine.kept = [2];
    expect(problems(d)).toEqual([
      ["machine-ingredient", "/recipes/7/ingredients/2/isTool"],
      ["machine-ingredient", "/recipes/7/machine/wear/ingredient"],
    ]);

    const e = example();
    delete e.recipes[7].ingredients[2].isTool;
    expect(problems(e)).toEqual([["machine-ingredient", "/recipes/7/ingredients/2"]]);

    const f = example();
    f.recipes[7].machine.oil.ingredient = 9;
    expect(problems(f)).toEqual([["machine-ingredient", "/recipes/7/machine/oil/ingredient"]]);
  });

  it("rejects a machine with no station and a wear rule it does not know", () => {
    const d = example();
    delete d.recipes[7].ingredients[4].role;
    expect(problems(d)).toEqual([["machine-shape", "/recipes/7/ingredients"]]);

    const e = example();
    e.recipes[7].machine.wear.rule = "sometimes";
    expect(problems(e)).toEqual([["schema:enum", "/recipes/7/machine/wear/rule"]]);
  });

  it("rejects a variant with too few ingredient lists", () => {
    const d = example();
    d.recipes[0].variants[0].ingredients.pop();
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["variant-ingredients", "/recipes/0/variants/0/ingredients", "1"]);
    expect(p.expected).toContain("2");
  });
});

describe("validate: schema", () => {
  it("rejects a missing schemaVersion", () => {
    const d = example();
    delete d.schemaVersion;
    expect(problems(d)).toEqual([["schema:required", "/schemaVersion"]]);
  });

  it("rejects a missing required section", () => {
    const d = example();
    delete d.guides;
    expect(problems(d)).toEqual([["schema:required", "/guides"]]);
  });

  it("rejects an unknown top-level property", () => {
    const d = example();
    d.recipeCount = 5;
    expect(problems(d)).toEqual([["schema:additionalProperties", "/recipeCount"]]);
  });

  it("rejects a stack code without a domain", () => {
    const d = example();
    d.recipes[8].variants[0].outputs[0].code = "ladder-wood-north";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "schema:pattern",
      "/recipes/8/variants/0/outputs/0/code",
      '"ladder-wood-north"',
    ]);
  });

  it("rejects an item key without a domain", () => {
    const d = example();
    d.items.widget = d.items["examplemod:widget"];
    delete d.items["examplemod:widget"];
    expect(problems(d)).toEqual([["schema:propertyName-pattern", "/items/widget"]]);
  });
});

describe("validate: report", () => {
  it("caps each kind and still counts every error", () => {
    const d = example();
    // 25 unknown ingredient codes: one variant slot with 25 stacks.
    d.recipes[6].variants[0].ingredients[0] = Array.from({ length: 25 }, (_, i) => ({
      code: `game:nothing-${i}`,
      kind: "item",
      quantity: 1,
    }));
    const r = pipeline.process(d);
    if (r.ok) throw new Error("expected failure");
    expect(r.report.total).toBe(25);
    expect(r.report.problems).toHaveLength(20);
    const lines = r.report.format();
    expect(lines[0]).toBe(
      '/recipes/6/variants/0/ingredients/0/0/code: expected a key of items, found "game:nothing-0" [variant-code]',
    );
    expect(lines.slice(-2)).toEqual(["  ... and 5 more [variant-code] errors", "25 errors"]);
  });

  it("exits non-zero from the command line when any file fails", () => {
    const dir = tempDir();
    const bad = example();
    bad.recipeTypes.alloy.count = 0;
    const badFile = path.join(dir, "bad.json");
    writeFileSync(badFile, JSON.stringify(bad));
    const log = vi.spyOn(console, "log").mockImplementation(() => {});
    try {
      expect(main(["validate", EXAMPLE_FILE])).toBe(0);
      expect(main(["validate", EXAMPLE_FILE, badFile])).toBe(1);
      expect(log).toHaveBeenCalledWith(`FAIL ${badFile}`);
      expect(log).toHaveBeenCalledWith(
        "  /recipeTypes/alloy/count: expected 1 (records of this type), found 0 [recipe-type-count]",
      );
    } finally {
      log.mockRestore();
    }
  });

  it("reports a file that is not JSON", () => {
    const dir = tempDir();
    const file = path.join(dir, "broken.json");
    writeFileSync(file, '{"schemaVersion": 1,');
    const r = pipeline.processFile(file);
    expect(r.ok).toBe(false);
  });
});
