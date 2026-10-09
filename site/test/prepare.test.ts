import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Recipe, RecipeExport } from "../src/lib/export.ts";
import type { EntityChunk, EntityIndex, ItemChunk, Meta, RecipeChunk, SearchFile } from "../src/lib/format.ts";
import { assetIdsFromLock, entityTypeName, prepareData } from "../src/lib/prepare.ts";

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

  it("carries ground storage into the item chunks", () => {
    expect(r.detail("game:ingot-copper").attributes?.groundStorage).toEqual({
      layout: "stacking",
      capacity: 64,
      transfer: 1,
      bulkTransfer: 4,
      solidTop: true,
      fullHeight: 1,
    });
    expect(r.detail("game:saw-copper").attributes?.groundStorage).toEqual({ layout: "wallhalves", capacity: 2 });
    expect(r.detail("game:stick").attributes?.groundStorage).toBeUndefined();
  });

  it("lists birch plank as used by the ladder recipe although the recipe says game:plank-*", () => {
    expect(r.ids(r.detail("game:plank-birch").usedIn)).toEqual({
      construction: ["construction|game:waterwheel-3m-north|2"],
      grid: ["grid|game:recipes/grid/ladder.json|0"],
    });
  });

  it("indexes a block built in place as made by its stages, which use what they consume", () => {
    const id = "construction|game:waterwheel-3m-north|2";
    expect(r.ids(r.detail("game:waterwheel-3m-north").madeBy)).toEqual({ construction: [id] });
    expect(r.ids(r.detail("game:supportbeam-oak").usedIn)).toEqual({ construction: [id] });
    expect(r.ids(r.detail("game:resin").usedIn)).toEqual({ construction: [id] });
    expect(r.meta.recipeTypes.construction).toEqual({ name: "Built in place", shape: "construction", count: 1, start: 4, mod: "survival" });
  });

  it("indexes a creature's butchery as making every stage's output and using every carcass, station and tool", () => {
    const id = "butchery|game:hare|butchering:deadhare-male-european-1-dead";
    for (const code of ["game:bushmeat-raw", "game:fat", "butchering:bloodportion", "butchering:deadhare-male-european-1-bledout"]) {
      expect(r.ids(r.detail(code).madeBy).butchery).toEqual([id]);
    }
    expect(r.ids(r.detail("game:hide-raw-small").madeBy)).toEqual({ butchery: [id] });
    for (const code of ["butchering:deadhare-male-european-1-skinned", "butchering:butchertable-simple-north", "game:cleaver-copper", "game:woodbucket"]) {
      expect(r.ids(r.detail(code).usedIn)).toEqual({ butchery: [id] });
    }
    expect(r.meta.recipeTypes.butchery).toEqual({ name: "Butchery", shape: "butchery", count: 1, start: 2, mod: "butchering" });
  });

  it("gives the creature type its butchery, and the creatures the record names that give nothing else", () => {
    const index = prepareData(minimal).files.get("entities.json") as EntityIndex;
    const hare = index.codes.indexOf("game:hare");
    expect(hare).toBeGreaterThanOrEqual(0);
    expect(index.recipes[hare]!.map((ri) => r.recipe(ri).id)).toEqual(["butchery|game:hare|butchering:deadhare-male-european-1-dead"]);
    expect(index.variantNames[hare]).toEqual(["European hare (female)", "European hare (male)"]);
    expect(index.drops[hare]).toBe(0);
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
    expect(r.search.codes.slice(0, 3)).toEqual(["butchering:bloodportion", "butchering:butcherhook-copper-north", "butchering:butchertable-simple-north"]);
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
    expect(r.meta.recipeTypes["examplemod:press"]).toEqual({ name: "Press", shape: "generic", count: 1, start: 6, mod: "examplemod" });
    expect(r.meta.itemCount).toBe(50);
    expect(r.meta.recipeCount).toBe(13);
  });

  it("indexes a transition as made by on the result's page and used in on the source's", () => {
    const id = "curing|butchering:sinew-wet|0";
    expect(r.ids(r.detail("butchering:sinew-dry").madeBy)).toEqual({ curing: [id] });
    expect(r.ids(r.detail("butchering:sinew-wet").usedIn)).toEqual({ curing: [id] });
    expect(r.detail("butchering:sinew-wet").madeBy).toBeUndefined();
    expect(r.meta.recipeTypes.curing).toEqual({ name: "Curing", shape: "transition", count: 1, start: 5, mod: "game" });
  });

  it("indexes the smoking rack as a use of the rack and of the raw meat, and as a source of the smoked meat", () => {
    const id = "smoking|butchering:primemeat-raw|0";
    expect(r.ids(r.detail("butchering:smoked-none-primemeat").madeBy)).toEqual({ smoking: [id] });
    expect(r.ids(r.detail("butchering:primemeat-raw").usedIn)).toEqual({ smoking: [id] });
    expect(r.ids(r.detail("butchering:smokingrack-copper-north").usedIn)).toEqual({ smoking: [id] });
    expect(r.meta.recipeTypes.smoking).toEqual({ name: "Smoking rack", shape: "transition", count: 1, start: 12, mod: "butchering" });
  });
});

describe("perishing", () => {
  const stack = (code: string, quantity = 1) => ({ code, kind: "item" as const, quantity });
  const perish = (from: string, to: string): Recipe => ({
    id: `perishing|${from}|0`,
    type: "perishing",
    mod: "game",
    ingredients: [stack(from)],
    outputs: [stack(to)],
    variants: [{ ingredients: [[stack(from)]], outputs: [stack(to)] }],
    transition: { type: "perish", freshHours: { avg: 36 }, transitionHours: { avg: 24 } },
  });
  const exp: RecipeExport = {
    ...exportWith(["game:redmeat-raw", "game:fish-raw", "game:rot", "game:wine", "game:vinegar"], [
      perish("game:fish-raw", "game:rot"),
      perish("game:redmeat-raw", "game:rot"),
      perish("game:wine", "game:vinegar"),
    ]),
    recipeTypes: { perishing: { name: "Perishing", count: 3, shape: "transition" } },
  };
  const r = reader(prepareData(exp).files);

  it("lists perishing into rot on the food's page only, and counts it on rot's", () => {
    expect(r.ids(r.detail("game:redmeat-raw").usedIn)).toEqual({ perishing: ["perishing|game:redmeat-raw|0"] });
    expect(r.detail("game:rot").madeBy).toBeUndefined();
    expect(r.detail("game:rot").madeByElsewhere).toEqual({ perishing: 2 });
  });

  it("lists perishing into anything else on both pages", () => {
    expect(r.ids(r.detail("game:vinegar").madeBy)).toEqual({ perishing: ["perishing|game:wine|0"] });
    expect(r.ids(r.detail("game:wine").usedIn)).toEqual({ perishing: ["perishing|game:wine|0"] });
    expect(r.detail("game:vinegar").madeByElsewhere).toBeUndefined();
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

  it("keeps each type's recipes in one run that starts where the meta says", () => {
    const base = { mod: "game", ingredients: [], outputs: [], variants: [] };
    // By id alone "grid2|…" would sort between "grid|a|0" and "grid|z|0", since `2` < `|`.
    const exp = exportWith([], [
      { ...base, id: "grid|z|0", type: "grid" },
      { ...base, id: "grid2|m|0", type: "grid2" },
      { ...base, id: "barrel|m|0", type: "barrel" },
      { ...base, id: "grid|a|0", type: "grid" },
    ]);
    exp.recipeTypes.grid2 = { name: "Other grid", count: 1, shape: "grid" };
    exp.recipeTypes.barrel = { name: "Barrel", count: 1, shape: "barrel" };
    const r = reader(prepareData(exp).files);
    const run = (type: string) => {
      const { start, count } = r.meta.recipeTypes[type]!;
      return Array.from({ length: count }, (_, i) => r.recipe(start + i).id);
    };
    expect(run("barrel")).toEqual(["barrel|m|0"]);
    expect(run("grid")).toEqual(["grid|a|0", "grid|z|0"]);
    expect(run("grid2")).toEqual(["grid2|m|0"]);
  });

  it("gives every type of minimal.json exactly its own recipes", () => {
    const r = reader(prepareData(minimal).files);
    for (const [type, { start, count }] of Object.entries(r.meta.recipeTypes)) {
      for (let i = 0; i < count; i++) expect(r.recipe(start + i).type).toBe(type);
    }
    expect(Object.values(r.meta.recipeTypes).reduce((n, t) => n + t.count, 0)).toBe(r.meta.recipeCount);
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

  it("turns block sources around into what the block gives, and leaves entities to their pages", () => {
    const exp = exportWith(["game:flint", "game:gravel-granite"], []);
    exp.items["game:flint"]!.sources = [
      { type: "other", from: "game:gravel-granite", fromName: "Granite gravel", quantity: { avg: 0.075 }, note: "Panned", extra: { chancePerPan: 0.05 } },
      { type: "blockDrop", from: "game:looseflints", fromName: "Loose flint", quantity: { avg: 1 } },
      { type: "entityDrop", from: "game:gravel-granite", quantity: { avg: 1 } },
    ];
    const r = reader(prepareData(exp).files);
    const flint = r.search.codes.indexOf("game:flint");
    expect(r.detail("game:gravel-granite").gives).toEqual([
      { type: "other", quantity: { avg: 0.075 }, note: "Panned", extra: { chancePerPan: 0.05 }, item: flint },
    ]);
    expect(r.detail("game:flint").gives).toBeUndefined();
  });

  it("leaves out the trades of a trader the pack replaces, on the item and as an entity", () => {
    const exp = exportWith(["game:fruit-blueberry"], []);
    const trade = (from: string, fromName: string, replaced: boolean) => ({
      type: "traderSells" as const,
      from,
      fromName,
      quantity: { avg: 8 },
      price: 1,
      extra: { entityType: "game:trader", ...(replaced ? { replaced: true } : {}) },
    });
    exp.items["game:fruit-blueberry"]!.sources = [
      trade("game:trader-male-agriculture-temperate", "Agriculture trader (temperate)", true),
      trade("game:trader-male-treasurehunter-temperate", "Treasure hunter trader (temperate)", false),
    ];
    const prepared = prepareData(exp);
    const r = reader(prepared.files);
    expect(r.detail("game:fruit-blueberry").sources!.map((s) => s.from)).toEqual(["game:trader-male-treasurehunter-temperate"]);
    const index = prepared.files.get("entities.json") as EntityIndex;
    expect(index.codes).toEqual(["game:trader"]);
    expect(index.variantNames).toEqual([["Treasure hunter trader (temperate)"]]);
  });
});

describe("entities, from the item sources that name them", () => {
  const exp = exportWith(["game:fat", "game:gear-rusty", "game:hide-raw-large", "game:stick"], []);
  exp.mods.wool = { name: "Wool", version: "1", domains: ["woolfleece"] };
  const wolf = (from: string, fromName: string) => ({ from, fromName, extra: { entityType: "game:wolf" } });
  exp.items["game:fat"]!.sources = [
    { type: "entityDrop", ...wolf("game:wolf-male", "Wolf (male)"), quantity: { avg: 5, var: 1 }, note: "Harvested" },
    { type: "entityDrop", ...wolf("game:wolf-pup-male", "Wolf pup (male)"), quantity: { avg: 1 }, note: "Harvested" },
  ];
  exp.items["game:hide-raw-large"]!.sources = [
    { type: "entityDrop", ...wolf("game:wolf-male", "Wolf (male)"), quantity: { avg: 1 }, note: "Harvested" },
    { type: "entityDrop", from: "woolfleece:sheep", fromName: "Sheep", quantity: { avg: 1 } },
  ];
  exp.items["game:gear-rusty"]!.sources = [
    { type: "blockDrop", from: "game:loosegears-1", fromName: "Loose rusty gears", quantity: { avg: 1 } },
    { type: "entityDrop", from: "game:drifter-normal", fromName: "Surface Drifter", quantity: { avg: 0.01 }, note: "Harvested", extra: { entityType: "game:drifter" } },
    {
      type: "traderBuys",
      from: "game:trader-agriculture",
      fromName: "Agriculture trader",
      quantity: { avg: 1 },
      price: 2,
      extra: { entityType: "game:trader", stock: { avg: 4 } },
    },
  ];
  const { files, meta } = prepareData(exp, { maxItemsPerChunk: 2 });
  const index = files.get("entities.json") as EntityIndex;
  const search = files.get("search.json") as SearchFile;
  const variants = (code: string) => {
    const i = index.codes.indexOf(code);
    const chunk = [...files.entries()]
      .filter(([p]) => p.startsWith("entities/"))
      .map(([, c]) => c as EntityChunk)
      .find((c) => i >= c.start && i < c.start + c.entities.length)!;
    return chunk.entities[i - chunk.start]!.map((v) => ({
      ...v,
      sources: v.sources.map(({ item, ...rest }) => ({ item: search.codes[item], ...rest })),
    }));
  };

  it("lists each type once, sorted by code, and leaves blocks out", () => {
    expect(index.codes).toEqual(["game:drifter", "game:trader", "game:wolf", "woolfleece:sheep"]);
    expect(index.names).toEqual(["Surface Drifter", "Agriculture trader", "Wolf", "Sheep"]);
    expect(index.variantNames).toEqual([["Surface Drifter"], ["Agriculture trader"], ["Wolf (male)", "Wolf pup (male)"], ["Sheep"]]);
    expect(index.drops).toEqual([1, 0, 2, 1]);
    expect(index.trades).toEqual([0, 1, 0, 0]);
    expect(index.recipes).toEqual([[], [], [], []]);
    expect(meta.entityCount).toBe(4);
    expect(meta.entityChunks).toEqual([0, 2]);
  });

  it("makes an entity without a declared type a type of its own, and credits a domain to the mod that lists it", () => {
    expect(variants("woolfleece:sheep").map((v) => v.code)).toEqual(["woolfleece:sheep"]);
    expect(index.mod).toEqual(["game", "game", "game", "wool"]);
  });

  it("turns each source around into what each variant gives, in item order, without the type", () => {
    expect(variants("game:wolf")).toEqual([
      {
        code: "game:wolf-male",
        name: "Wolf (male)",
        sources: [
          { item: "game:fat", type: "entityDrop", quantity: { avg: 5, var: 1 }, note: "Harvested" },
          { item: "game:hide-raw-large", type: "entityDrop", quantity: { avg: 1 }, note: "Harvested" },
        ],
      },
      { code: "game:wolf-pup-male", name: "Wolf pup (male)", sources: [{ item: "game:fat", type: "entityDrop", quantity: { avg: 1 }, note: "Harvested" }] },
    ]);
    expect(variants("game:trader")[0]!.sources).toEqual([
      { item: "game:gear-rusty", type: "traderBuys", quantity: { avg: 1 }, price: 2, extra: { stock: { avg: 4 } } },
    ]);
  });
});

describe("ModDB asset ids", () => {
  it("reads them from a lock, and nothing from an old or broken one", () => {
    const lock = { lockVersion: 1, mods: [{ id: "examplemod", assetId: 42 }, { id: "old" }, { id: "bad", assetId: "7" }, null] };
    expect(assetIdsFromLock(lock)).toEqual({ examplemod: 42 });
    expect(assetIdsFromLock({ lockVersion: 1, mods: [{ id: "examplemod", version: "1.0.0" }] })).toEqual({});
    expect(assetIdsFromLock(null)).toEqual({});
    expect(assetIdsFromLock("<html>")).toEqual({});
  });

  it("puts each locked mod's id on it in meta.json and leaves the rest alone", () => {
    const meta = prepareData(minimal, { assetIds: { examplemod: 42, notexported: 9 } }).meta;
    expect(meta.mods.examplemod!.assetId).toBe(42);
    expect(meta.mods.game).toEqual({ name: "Essentials", version: "1.22.7", authors: ["Tyron"] });
    expect(meta.mods.notexported).toBeUndefined();
    expect(prepareData(minimal).meta.mods.examplemod!.assetId).toBeUndefined();
  });
});

describe("entityTypeName", () => {
  it("takes the words every variant name starts or ends with", () => {
    expect(entityTypeName("game:wolf", ["Wolf (male)", "Wolf (female)", "Wolf pup (male)"])).toBe("Wolf");
    expect(entityTypeName("game:drifter", ["Surface Drifter", "Deep Drifter", "Double-headed Drifter"])).toBe("Drifter");
    expect(entityTypeName("game:trader", ["Commodities trader (cold)", "Agriculture trader (temperate)"])).toBe("Trader");
  });

  it("falls back to the code when the names share nothing", () => {
    expect(entityTypeName("game:fish-saltwater", ["Arapaima", "Barracuda"])).toBe("Fish (saltwater)");
    expect(entityTypeName("game:goat", [])).toBe("Goat");
  });
});

describe("variant groups", () => {
  const exp = structuredClone(minimal);
  exp.variantGroups = {
    "auto:game:plank": { title: "Plank", members: ["game:plank-oak", "game:plank-birch"] },
    "auto:game:ingot": { title: "Ingot", members: ["game:ingot-tin", "game:ingot-copper", "game:ingot-tinbronze"] },
    // site-data rejects these, but an older export may still have them: an unknown member,
    // a member already in another group, a group left with one item.
    "auto:game:supportbeam": { title: "Support beam", members: ["game:supportbeam-oak", "game:nosuchitem", "game:ingot-tin"] },
  };
  const search = prepareData(exp).files.get("search.json") as SearchFile;
  const at = (code: string) => search.codes.indexOf(code);

  it("writes each group's title and members as item indices, in rank order, groups by id", () => {
    expect(search.groups).toEqual({
      titles: ["Ingot", "Plank"],
      members: [
        [at("game:ingot-tin"), at("game:ingot-copper"), at("game:ingot-tinbronze")],
        [at("game:plank-oak"), at("game:plank-birch")],
      ],
    });
  });

  it("writes no groups for an export without them", () => {
    const plain = structuredClone(minimal);
    delete plain.variantGroups;
    expect((prepareData(plain).files.get("search.json") as SearchFile).groups).toBeUndefined();
  });
});

describe("power.json", () => {
  it("is the export's power section as it is", () => {
    const files = prepareData(minimal).files;
    expect(files.get("power.json")).toEqual(minimal.power);
    expect((files.get("meta.json") as Meta).power).toBe(true);
    expect(minimal.power?.producers.length).toBeGreaterThan(0);
  });

  it("is not written for an export without a power section", () => {
    const { power: _power, ...older } = minimal;
    const files = prepareData(older as RecipeExport).files;
    expect(files.has("power.json")).toBe(false);
    expect("power" in (files.get("meta.json") as Meta)).toBe(false);
  });
});
