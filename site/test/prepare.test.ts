import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Recipe, RecipeExport } from "../src/lib/export.ts";
import type { ItemChunk, Meta, RecipeChunk, SearchFile } from "../src/lib/format.ts";
import { prepareData } from "../src/lib/prepare.ts";

const minimal = JSON.parse(
  readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8"),
) as RecipeExport;

/** Reads back what the app would: an item's detail by code, recipes by index. */
function reader(files: Map<string, unknown>) {
  const meta = files.get("meta.json") as Meta;
  const search = files.get("search.json") as SearchFile;
  const chunks = [...files.entries()].filter(([p]) => p.startsWith("items/")).map(([, c]) => c as ItemChunk);
  const recipeChunks = [...files.entries()].filter(([p]) => p.startsWith("recipes/")).map(([, c]) => c as RecipeChunk);
  const detail = (code: string) => {
    const i = search.codes.indexOf(code);
    if (i < 0) throw new Error(`${code} not in search.json`);
    const chunk = chunks.find((c) => i >= c.start && i < c.start + c.items.length)!;
    return chunk.items[i - chunk.start]!;
  };
  const recipe = (ri: number) => {
    const chunk = recipeChunks.find((c) => ri >= c.start && ri < c.start + c.recipes.length)!;
    return chunk.recipes[ri - chunk.start]!;
  };
  const ids = (groups: Record<string, number[]> | undefined) =>
    Object.fromEntries(Object.entries(groups ?? {}).map(([type, list]) => [type, list.map((ri) => recipe(ri).id)]));
  return { meta, search, detail, recipe, ids };
}

describe("prepareData on schema/examples/minimal.json", () => {
  const r = reader(prepareData(minimal).files);

  it("lists birch plank as used by the ladder recipe although the recipe says game:plank-*", () => {
    expect(r.ids(r.detail("game:plank-birch").usedIn)).toEqual({ grid: ["grid|game:recipes/grid/ladder.json|0"] });
  });

  it("lists the copper saw as used by the ladder recipe, as a tool", () => {
    expect(r.ids(r.detail("game:saw-copper").usedIn)).toEqual({ grid: ["grid|game:recipes/grid/ladder.json|0"] });
  });

  it("indexes copper ingot under both recipe types that consume it", () => {
    expect(r.ids(r.detail("game:ingot-copper").usedIn)).toEqual({
      alloy: ["alloy|game:recipes/alloy/tinbronze.json|0"],
      "examplemod:press": ["examplemod:press|examplemod:recipes/press/widget.json|0"],
    });
  });

  it("builds made-by from outputs", () => {
    expect(r.ids(r.detail("game:ladder-wood-north").madeBy)).toEqual({ grid: ["grid|game:recipes/grid/ladder.json|0"] });
    expect(r.ids(r.detail("game:ingot-tinbronze").madeBy)).toEqual({ alloy: ["alloy|game:recipes/alloy/tinbronze.json|0"] });
    expect(r.detail("game:ingot-tinbronze").usedIn).toBeUndefined();
    expect(r.detail("game:ladder-wood-north").usedIn).toBeUndefined();
  });

  it("writes items sorted by code with names, mods and flags", () => {
    expect(r.search.codes.slice(0, 3)).toEqual(["examplemod:widget", "game:flint", "game:hide-raw-small"]);
    const widget = r.search.codes.indexOf("examplemod:widget");
    expect(r.search.names[widget]).toBe("Widget");
    expect(r.search.mods[r.search.mod[widget]!]).toBe("examplemod");
    // Not handbook-visible, an item: no flags.
    expect(r.search.flags[widget]).toBe(0);
    const ladder = r.search.codes.indexOf("game:ladder-wood-north");
    expect(r.search.flags[ladder]).toBe(3);
  });

  it("keeps item details and gives unknown types a generic shape from recipeTypes", () => {
    expect(r.detail("game:stick").sources).toEqual([
      { type: "blockDrop", from: "game:leavesbranchy-grown-oak", fromName: "Branchy oak leaves", quantity: { avg: 0.8, var: 0 } },
    ]);
    expect(r.detail("game:ingot-copper").description).toBe("A bar of copper.");
    expect(r.meta.recipeTypes["examplemod:press"]).toEqual({ name: "Press", shape: "generic", count: 1, mod: "examplemod" });
    expect(r.meta.itemCount).toBe(14);
    expect(r.meta.recipeCount).toBe(5);
  });
});

// Small hand-written exports for the cases minimal.json does not cover.
function exportWith(items: string[], recipes: Recipe[]): RecipeExport {
  return {
    schemaVersion: 1,
    generator: { name: "test", version: "0" },
    pack: { id: "p", version: "0", gameVersion: "1.22.7" },
    mods: { game: { name: "Essentials", version: "1" } },
    items: Object.fromEntries(items.map((c) => [c, { kind: "item", name: c, mod: "game", handbookVisible: true }])),
    recipes,
    recipeTypes: { grid: { name: "Crafting grid", count: recipes.length, shape: "grid" } },
    guides: [],
  };
}

const planks = ["game:plank-oak", "game:plank-birch", "game:plank-ebony", "game:plank-rottenebony", "game:plankpile"];

describe("wildcards in the reverse indexes", () => {
  it("matches a wildcard ingredient against every item even when the variants list none of them", () => {
    const exp = exportWith([...planks, "game:table"], [
      {
        id: "grid|t|0",
        type: "grid",
        mod: "game",
        ingredients: [{ code: "game:plank-*", kind: "item", quantity: 1, skipVariants: ["rottenebony"] }],
        outputs: [{ code: "game:table", kind: "item", quantity: 1 }],
        variants: [],
      },
    ]);
    const r = reader(prepareData(exp).files);
    expect(r.ids(r.detail("game:plank-ebony").usedIn)).toEqual({ grid: ["grid|t|0"] });
    expect(r.ids(r.detail("game:plank-oak").usedIn)).toEqual({ grid: ["grid|t|0"] });
    // skipVariants holds, and `plank-*` does not match `plankpile`.
    expect(r.detail("game:plank-rottenebony").usedIn).toBeUndefined();
    expect(r.detail("game:plankpile").usedIn).toBeUndefined();
  });

  // survival/recipes/grid/fishinggear.json: the line is `{line}`, which as a pattern
  // matches every code of the game; the game accepts flax twine and rope.
  it("takes the variants, not the pattern, when a recipe has variants", () => {
    const stack = (code: string) => ({ code, kind: "item" as const, quantity: 1 });
    const exp = exportWith(
      ["game:stick", "game:flaxtwine", "game:rope", "game:torch-basic-lit-up", "game:fishingpole-simple-wood"],
      [
        {
          id: "grid|game:recipes/grid/fishinggear.json|0",
          type: "grid",
          mod: "game",
          ingredients: [stack("game:stick"), stack("game:{line}")],
          outputs: [stack("game:fishingpole-simple-wood")],
          variants: [
            { bindings: { line: "flaxtwine" }, ingredients: [[stack("game:stick")], [stack("game:flaxtwine")]], outputs: [stack("game:fishingpole-simple-wood")] },
            { bindings: { line: "rope" }, ingredients: [[stack("game:stick")], [stack("game:rope")]], outputs: [stack("game:fishingpole-simple-wood")] },
          ],
        },
      ],
    );
    const r = reader(prepareData(exp).files);
    const pole = { grid: ["grid|game:recipes/grid/fishinggear.json|0"] };
    expect(r.ids(r.detail("game:rope").usedIn)).toEqual(pole);
    expect(r.ids(r.detail("game:flaxtwine").usedIn)).toEqual(pole);
    expect(r.ids(r.detail("game:stick").usedIn)).toEqual(pole);
    expect(r.detail("game:torch-basic-lit-up").usedIn).toBeUndefined();
    expect(r.detail("game:fishingpole-simple-wood").usedIn).toBeUndefined();
  });

  it("honours allowedVariants", () => {
    const exp = exportWith(planks, [
      {
        id: "grid|t|0",
        type: "grid",
        mod: "game",
        ingredients: [{ code: "game:plank-*", kind: "item", quantity: 1, allowedVariants: ["birch"] }],
        outputs: [],
        variants: [],
      },
    ]);
    const r = reader(prepareData(exp).files);
    expect(r.ids(r.detail("game:plank-birch").usedIn)).toEqual({ grid: ["grid|t|0"] });
    expect(r.detail("game:plank-oak").usedIn).toBeUndefined();
  });

  it("resolves a {name} output only to values the named ingredient allows when there are no variants", () => {
    const exp = exportWith(
      [...planks, "game:ladder-oak", "game:ladder-birch", "game:ladder-ebony"],
      [
        {
          id: "grid|t|0",
          type: "grid",
          mod: "game",
          ingredients: [{ code: "game:plank-*", kind: "item", quantity: 1, wildcardName: "wood", allowedVariants: ["oak", "birch"] }],
          outputs: [{ code: "game:ladder-{wood}", kind: "item", quantity: 3 }],
          variants: [],
        },
      ],
    );
    const r = reader(prepareData(exp).files);
    expect(r.ids(r.detail("game:ladder-oak").madeBy)).toEqual({ grid: ["grid|t|0"] });
    expect(r.ids(r.detail("game:ladder-birch").madeBy)).toEqual({ grid: ["grid|t|0"] });
    expect(r.detail("game:ladder-ebony").madeBy).toBeUndefined();
  });

  it("drops disabled recipes and counts types from what is left", () => {
    const base = { type: "grid", mod: "game", outputs: [], variants: [] };
    const exp = exportWith(["game:a"], [
      { ...base, id: "grid|x|0", ingredients: [{ code: "game:a", kind: "item", quantity: 1 }], enabled: false },
      { ...base, id: "grid|x|1", ingredients: [{ code: "game:a", kind: "item", quantity: 1 }] },
    ]);
    const r = reader(prepareData(exp).files);
    expect(r.ids(r.detail("game:a").usedIn)).toEqual({ grid: ["grid|x|1"] });
    expect(r.meta.recipeTypes.grid!.count).toBe(1);
  });

  it("splits items and recipes into chunks whose starts the meta lists", () => {
    const codes = Array.from({ length: 25 }, (_, i) => `game:item-${String(i).padStart(2, "0")}`);
    const recipes: Recipe[] = codes.map((c, i) => ({
      id: `grid|r|${String(i).padStart(2, "0")}`,
      type: "grid",
      mod: "game",
      ingredients: [{ code: c, kind: "item", quantity: 1 }],
      outputs: [],
      variants: [],
    }));
    const { files, meta } = prepareData(exportWith(codes, recipes), { maxItemsPerChunk: 10, maxRecipesPerChunk: 7 });
    expect(meta.itemChunks).toEqual([0, 10, 20]);
    expect(meta.recipeChunks).toEqual([0, 7, 14, 21]);
    expect(files.has("items/2.json")).toBe(true);
    expect(files.has("recipes/3.json")).toBe(true);
    const r = reader(files);
    expect(r.ids(r.detail("game:item-23").usedIn)).toEqual({ grid: ["grid|r|23"] });
  });

  it("links smelting both ways", () => {
    const exp = exportWith(["game:nugget-copper", "game:ingot-copper"], []);
    exp.items["game:nugget-copper"]!.attributes = {
      smelting: { meltingPoint: 1084, output: { code: "game:ingot-copper", kind: "item", quantity: 1 } },
    };
    const r = reader(prepareData(exp).files);
    const ingot = r.search.codes.indexOf("game:ingot-copper");
    const nugget = r.search.codes.indexOf("game:nugget-copper");
    expect(r.detail("game:ingot-copper").smeltedFrom).toEqual([nugget]);
    expect(r.detail("game:nugget-copper").smeltsInto).toBe(ingot);
  });
});
