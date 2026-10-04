import { writeFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { main } from "../src/cli.js";
import { Pipeline } from "../src/pipeline.js";
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
    d.recipes[4].mod = "othermod";
    expect(problems(d)).toEqual([["recipe-mod", "/recipes/4/mod"]]);
  });

  it("rejects an item from an unknown mod", () => {
    const d = example();
    d.items["examplemod:widget"].mod = "nomod";
    expect(problems(d)).toEqual([["item-mod", "/items/examplemod:widget/mod"]]);
  });

  it("rejects a variant ingredient code that is not an item", () => {
    const d = example();
    d.recipes[5].variants[1].ingredients[0][0].code = "game:plank-pine";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "variant-code",
      "/recipes/5/variants/1/ingredients/0/0/code",
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
    [d.recipes[5], d.recipes[6]] = [d.recipes[6], d.recipes[5]];
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["recipe-id-order", "/recipes/6/id", '"grid|game:recipes/grid/ladder.json|0"']);
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
    d.recipes[5].grid.pattern[2] = "S_";
    const p = only(d);
    expect([p.kind, p.path]).toEqual(["grid-width", "/recipes/5/grid/pattern/2"]);
    expect(p.expected).toContain("3");
  });

  it("rejects a grid pattern with too few rows", () => {
    const d = example();
    d.recipes[5].grid.pattern.pop();
    expect(problems(d)).toEqual([["grid-height", "/recipes/5/grid/pattern"]]);
  });

  it("rejects a grid pattern key with no ingredient", () => {
    const d = example();
    d.recipes[5].grid.pattern[1] = "SXS";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["grid-key", "/recipes/5/grid/pattern/1", '"X" at column 1']);
  });

  it("rejects a construction stage index past the ingredients", () => {
    const d = example();
    d.recipes[3].construction.stages[3].ingredients = [3];
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual(["construction-ingredient", "/recipes/3/construction/stages/3/ingredients/0", "3"]);
  });

  it("rejects a construction ingredient two stages consume", () => {
    const d = example();
    d.recipes[3].construction.stages[3].ingredients = [0];
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "construction-ingredient",
      "/recipes/3/construction/stages/3/ingredients/0",
      "0, also in stage 1",
    ]);
  });

  it("rejects a construction ingredient no stage consumes", () => {
    const d = example();
    d.recipes[3].construction.stages[2].ingredients = [1];
    expect(problems(d)).toEqual([["construction-ingredient", "/recipes/3/ingredients/2"]]);
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
    d.recipes[5].variants[0].outputs[0].code = "ladder-wood-north";
    const p = only(d);
    expect([p.kind, p.path, p.found]).toEqual([
      "schema:pattern",
      "/recipes/5/variants/0/outputs/0/code",
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
    d.recipes[4].variants[0].ingredients[0] = Array.from({ length: 25 }, (_, i) => ({
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
      '/recipes/4/variants/0/ingredients/0/0/code: expected a key of items, found "game:nothing-0" [variant-code]',
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
