import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { main } from "../src/cli.js";
import { MigrationChain, type Doc, type MigrationStep } from "../src/migrate.js";
import { steps as realSteps } from "../src/migrations/index.js";
import { Pipeline } from "../src/pipeline.js";
import { SchemaSet } from "../src/schemas.js";
import { EXAMPLE_FILE, tempDir, type Loose } from "./helpers.js";

// A made-up history: v1 has `recipes`, v2 renames it to `recipeList`, v3 adds
// `recipeCount`. The schemas are written out for v1 and v3 only, like the real
// archive, which only needs a schema per version an export can arrive in.
const schemaV1 = {
  type: "object",
  required: ["schemaVersion", "name", "recipes"],
  additionalProperties: false,
  properties: {
    schemaVersion: { const: 1 },
    name: { type: "string" },
    recipes: { type: "array", items: { type: "string" } },
  },
};
const schemaV3 = {
  type: "object",
  required: ["schemaVersion", "name", "recipeList", "recipeCount"],
  additionalProperties: false,
  properties: {
    schemaVersion: { const: 3 },
    name: { type: "string" },
    recipeList: { type: "array", items: { type: "string" } },
    recipeCount: { type: "integer" },
  },
};

function fakeSetup(overrides: { step2?: MigrationStep["up"] } = {}) {
  const calls: string[] = [];
  const step1: MigrationStep = {
    from: 1,
    to: 2,
    description: "rename recipes to recipeList",
    up: (d) => {
      calls.push("1->2");
      const { recipes, ...rest } = d;
      return { ...rest, schemaVersion: 2, recipeList: recipes };
    },
  };
  const step2: MigrationStep = {
    from: 2,
    to: 3,
    description: "add recipeCount",
    up:
      overrides.step2 ??
      ((d) => {
        calls.push("2->3");
        return { ...d, schemaVersion: 3, recipeCount: (d.recipeList as unknown[]).length };
      }),
  };
  const schemas = new SchemaSet(3, (v) => ({ 1: schemaV1, 3: schemaV3 })[v]);
  // Given out of order on purpose: the chain orders by version, not by listing.
  const pipeline = new Pipeline(schemas, new MigrationChain(3, [step2, step1]), () => {});
  return { pipeline, calls };
}

describe("migrate", () => {
  it("brings a version 1 document to version 3", () => {
    const { pipeline, calls } = fakeSetup();
    const r = pipeline.process({ schemaVersion: 1, name: "pack", recipes: ["a", "b"] });
    if (!r.ok) throw new Error(r.report.format().join("\n"));
    expect(r.doc).toEqual({ schemaVersion: 3, name: "pack", recipeList: ["a", "b"], recipeCount: 2 });
    expect(r.from).toBe(1);
    expect(calls).toEqual(["1->2", "2->3"]);
    expect(r.applied.map((s) => s.description)).toEqual(["rename recipes to recipeList", "add recipeCount"]);
  });

  it("refuses a version whose schema is not archived", () => {
    const { pipeline, calls } = fakeSetup();
    const r = pipeline.process({ schemaVersion: 2, name: "pack", recipeList: ["a"] });
    if (r.ok) throw new Error("expected a refusal");
    expect(r.report.problems.map((p) => [p.path, p.expected])).toEqual([
      ["/schemaVersion", "an archived schema for version 2"],
    ]);
    expect(calls).toEqual([]);
  });

  it("starts the chain at the document's own version", () => {
    const chain = new MigrationChain(3, [
      { from: 1, to: 2, description: "", up: () => ({ schemaVersion: 2 }) },
      { from: 2, to: 3, description: "", up: (d) => ({ ...d, schemaVersion: 3, seen: true }) },
    ]);
    const direct = chain.run({ schemaVersion: 2, name: "x" });
    expect(direct.doc).toEqual({ schemaVersion: 3, name: "x", seen: true });
    expect(direct.applied.map((s) => s.from)).toEqual([2]);
  });

  it("returns a current document unchanged", () => {
    const { pipeline, calls } = fakeSetup();
    const doc = { schemaVersion: 3, name: "pack", recipeList: ["a"], recipeCount: 1 };
    const r = pipeline.process(doc);
    if (!r.ok) throw new Error(r.report.format().join("\n"));
    expect(r.doc).toEqual({ schemaVersion: 3, name: "pack", recipeList: ["a"], recipeCount: 1 });
    expect(r.applied).toEqual([]);
    expect(calls).toEqual([]);
  });

  it.each([
    [4, "newer than this tool understands (current 3)"],
    [99, "newer than this tool understands (current 3)"],
    [0, "not a known export version (known: 1 to 3)"],
    ["1", 'schemaVersion "1" is not a known export version'],
    [1.5, "schemaVersion 1.5 is not a known export version"],
  ])("refuses schemaVersion %j", (version, message) => {
    const { pipeline, calls } = fakeSetup();
    const r = pipeline.process({ schemaVersion: version, name: "pack", recipes: [] });
    if (r.ok) throw new Error("expected a refusal");
    expect(r.report.problems).toHaveLength(1);
    expect(r.report.problems[0]!.path).toBe("/schemaVersion");
    expect(r.report.problems[0]!.found).toContain(message);
    expect(calls).toEqual([]);
  });

  it("fails when a step produces a document the current schema rejects", () => {
    const { pipeline } = fakeSetup({ step2: (d) => ({ ...d, schemaVersion: 3 }) }); // forgets recipeCount
    const r = pipeline.process({ schemaVersion: 1, name: "pack", recipes: ["a"] });
    if (r.ok) throw new Error("expected failure");
    expect(r.report.problems.map((p) => [p.kind, p.path])).toEqual([["schema:required", "/recipeCount"]]);
  });

  it("fails when a step forgets to set the new schemaVersion", () => {
    const { pipeline } = fakeSetup({ step2: (d) => ({ ...d, recipeCount: 1 }) });
    const r = pipeline.process({ schemaVersion: 1, name: "pack", recipes: ["a"] });
    if (r.ok) throw new Error("expected failure");
    expect(r.report.problems[0]!.found).toBe("migration 2 -> 3 returned schemaVersion 2");
  });

  it("validates the input against its own version's schema first", () => {
    const { pipeline, calls } = fakeSetup();
    const r = pipeline.process({ schemaVersion: 1, name: "pack", recipes: [7] });
    if (r.ok) throw new Error("expected failure");
    expect(r.report.problems.map((p) => [p.kind, p.path])).toEqual([["schema:type", "/recipes/0"]]);
    expect(calls).toEqual([]);
  });

  it("does not let a step modify its input", () => {
    const input: Doc = { schemaVersion: 1, name: "pack", recipes: ["a"] };
    const chain = new MigrationChain(2, [
      {
        from: 1,
        to: 2,
        description: "mutates",
        up: (d) => {
          (d as Loose).recipes.push("b");
          return { ...d, schemaVersion: 2 };
        },
      },
    ]);
    expect(() => chain.run(input)).toThrow(TypeError);
    expect(input.recipes).toEqual(["a"]);
  });

  it("rejects a chain with a gap, a skip or a duplicate", () => {
    const up = (d: Readonly<Doc>) => ({ ...d });
    expect(() => new MigrationChain(3, [{ from: 1, to: 3, description: "", up }])).toThrow("exactly one version");
    expect(() => new MigrationChain(2, [{ from: 2, to: 3, description: "", up }])).toThrow("beyond the current");
    expect(
      () =>
        new MigrationChain(3, [
          { from: 1, to: 2, description: "", up },
          { from: 1, to: 2, description: "", up },
        ]),
    ).toThrow("two migrations from version 1");
    // 1 -> 2 missing: version 1 is unknown, version 2 still works.
    const chain = new MigrationChain(3, [{ from: 2, to: 3, description: "", up: (d) => ({ ...d, schemaVersion: 3 }) }]);
    expect(chain.oldest).toBe(2);
    expect(() => chain.check(1)).toThrow("not a known export version (known: 2 to 3)");
  });
});

describe("migrate: the real configuration", () => {
  it("has a step and an archived schema for every version before the current one", () => {
    const pipeline = Pipeline.fromDir();
    const chain = new MigrationChain(pipeline.schemas.current, realSteps);
    expect(chain.oldest).toBe(1);
    for (let v = 1; v <= pipeline.schemas.current; v++) expect(pipeline.schemas.has(v)).toBe(true);
  });

  it("passes the current example through the command line unchanged", () => {
    const out = path.join(tempDir(), "sub", "out.json");
    const log = vi.spyOn(console, "log").mockImplementation(() => {});
    try {
      expect(main(["migrate", EXAMPLE_FILE, out])).toBe(0);
    } finally {
      log.mockRestore();
    }
    expect(JSON.parse(readFileSync(out, "utf8"))).toEqual(JSON.parse(readFileSync(EXAMPLE_FILE, "utf8")));
  });
});
