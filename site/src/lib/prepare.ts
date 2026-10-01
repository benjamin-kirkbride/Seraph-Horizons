// Turns one export into the files the app loads (see format.ts). Everything that needs
// the whole data set, the reverse indexes above all, is computed here so the browser
// only ever fetches what one page shows.
import type { Recipe, RecipeExport, Shape } from "./export.ts";
import {
  DATA_FORMAT,
  FLAG_BLOCK,
  type EntityChunk,
  type EntityIndex,
  type EntitySource,
  FLAG_HANDBOOK,
  type ItemChunk,
  type ItemDetail,
  type Meta,
  type RecipeChunk,
  type SearchFile,
  type TypeInfo,
} from "./format.ts";
import { indexOfSorted, isPattern, matchSorted, type VariantFilter } from "./wildcard.ts";

export interface PrepareOptions {
  /** Soft cap on the serialised size of one chunk file, in characters. */
  chunkBytes?: number;
  maxItemsPerChunk?: number;
  maxRecipesPerChunk?: number;
}

export interface Prepared {
  /** Path relative to the version directory, to JSON value. */
  files: Map<string, unknown>;
  meta: Meta;
}

const SHAPES: readonly Shape[] = ["grid", "voxels", "barrel", "alloy", "cooking", "construction", "generic"];

export function compareCodes(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Item indices each ingredient slot of `recipe` accepts, in slot order. */
export function slotItems(recipe: Recipe, codes: readonly string[], cache: Map<string, number[]> = new Map()): number[][] {
  return recipe.ingredients.map((ing, slot) => {
    const found = new Set<number>();
    for (const v of recipe.variants) {
      for (const stack of v.ingredients[slot] ?? []) {
        const i = indexOfSorted(codes, stack.code);
        if (i >= 0) found.add(i);
      }
    }
    // With variants present, they are the resolved truth. A pattern can be far wider than
    // what the game accepts: `game:{line}` is twine or rope, not every item of the game.
    if (recipe.variants.length > 0) return [...found].sort((a, b) => a - b);
    if (isPattern(ing.code)) {
      const filter: VariantFilter = { allowedVariants: ing.allowedVariants, skipVariants: ing.skipVariants };
      const key = `${ing.code}|${(ing.allowedVariants ?? []).join(",")}|${(ing.skipVariants ?? []).join(",")}`;
      let hits = cache.get(key);
      if (!hits) {
        hits = matchSorted(codes, ing.code, { "*": filter });
        cache.set(key, hits);
      }
      for (const i of hits) found.add(i);
    } else {
      const i = indexOfSorted(codes, ing.code);
      if (i >= 0) found.add(i);
    }
    return [...found].sort((a, b) => a - b);
  });
}

/** Item indices `recipe` can produce. */
export function outputItems(recipe: Recipe, codes: readonly string[]): number[] {
  const found = new Set<number>();
  const add = (code: string | undefined) => {
    if (!code) return;
    const i = indexOfSorted(codes, code);
    if (i >= 0) found.add(i);
  };
  for (const v of recipe.variants) for (const s of v.outputs) add(s.code);
  // A `{name}` output only takes values its named ingredient allows.
  const constraints: Record<string, VariantFilter> = {};
  for (const ing of recipe.ingredients) {
    if (ing.wildcardName) {
      constraints[ing.wildcardName] = { allowedVariants: ing.allowedVariants, skipVariants: ing.skipVariants };
    }
  }
  for (const out of recipe.outputs) {
    if (isPattern(out.code)) {
      // With variants present, they are the resolved truth; matching `{wood}` against
      // every code would also claim items no ingredient can bind.
      if (recipe.variants.length === 0) for (const i of matchSorted(codes, out.code, constraints)) found.add(i);
    } else add(out.code);
  }
  add(recipe.cooking?.cooksInto?.code);
  return [...found].sort((a, b) => a - b);
}

function chunkBy<T>(list: readonly T[], maxCount: number, maxBytes: number): number[] {
  const starts: number[] = [];
  let count = 0;
  let bytes = 0;
  list.forEach((entry, i) => {
    const size = JSON.stringify(entry ?? null).length;
    if (i === 0 || count >= maxCount || (count > 0 && bytes + size > maxBytes)) {
      starts.push(i);
      count = 0;
      bytes = 0;
    }
    count++;
    bytes += size;
  });
  return starts;
}

function pushTo(map: Record<string, number[]>, key: string, value: number) {
  (map[key] ??= []).push(value);
}

const ENTITY_SOURCES = new Set(["entityDrop", "traderSells", "traderBuys"]);

/** The mod that owns an asset domain: the mod of that id, else one that lists the domain. */
function modOfDomain(exp: RecipeExport, domain: string): string {
  if (exp.mods[domain]) return domain;
  for (const [id, mod] of Object.entries(exp.mods)) if (mod.domains?.includes(domain)) return id;
  return domain;
}

/**
 * Creatures and traders, from the item sources that name them. The export has no list of
 * entities of its own, so one that gives nothing is not here.
 */
export function entitiesFrom(exp: RecipeExport, codes: readonly string[]): { index: EntityIndex; sources: EntitySource[][] } {
  const byCode = new Map<string, { name?: string; sources: EntitySource[] }>();
  codes.forEach((code, item) => {
    for (const { from, fromName, ...rest } of exp.items[code]!.sources ?? []) {
      if (!ENTITY_SOURCES.has(rest.type)) continue;
      let e = byCode.get(from);
      if (!e) byCode.set(from, (e = { sources: [] }));
      e.name ??= fromName;
      e.sources.push({ ...rest, item });
    }
  });
  const index: EntityIndex = { codes: [...byCode.keys()].sort(compareCodes), names: [], mod: [], drops: [], trades: [] };
  const sources = index.codes.map((code) => {
    const e = byCode.get(code)!;
    index.names.push(e.name || code);
    index.mod.push(modOfDomain(exp, code.slice(0, code.indexOf(":"))));
    const drops = e.sources.filter((s) => s.type === "entityDrop").length;
    index.drops.push(drops);
    index.trades.push(e.sources.length - drops);
    return e.sources;
  });
  return { index, sources };
}

export function prepareData(exp: RecipeExport, options: PrepareOptions = {}): Prepared {
  const chunkBytes = options.chunkBytes ?? 256_000;
  const maxItems = options.maxItemsPerChunk ?? 400;
  const maxRecipes = options.maxRecipesPerChunk ?? 60;

  const codes = Object.keys(exp.items).sort(compareCodes);
  const modIds = Object.keys(exp.mods).sort(compareCodes);
  const seenMods = new Set(modIds);
  for (const code of codes) {
    const m = exp.items[code]!.mod;
    if (!seenMods.has(m)) {
      seenMods.add(m);
      modIds.push(m);
    }
  }
  const modIndex = new Map(modIds.map((m, i) => [m, i]));

  const search: SearchFile = { mods: modIds, codes, names: [], mod: [], flags: [] };
  const details: ItemDetail[] = [];
  for (const code of codes) {
    const item = exp.items[code]!;
    search.names.push(item.name || code);
    search.mod.push(modIndex.get(item.mod)!);
    search.flags.push((item.handbookVisible ? FLAG_HANDBOOK : 0) | (item.kind === "block" ? FLAG_BLOCK : 0));
    const d: ItemDetail = {};
    if (item.description) d.description = item.description;
    if (item.attributes && Object.keys(item.attributes).length > 0) d.attributes = item.attributes;
    if (item.sources && item.sources.length > 0) d.sources = item.sources;
    details.push(d);
  }

  // Disabled definitions cannot be crafted, so listing them would mislead.
  const recipes = exp.recipes.filter((r) => r.enabled !== false).sort((a, b) => compareCodes(a.id, b.id));

  const recipeTypes: Record<string, TypeInfo> = {};
  for (const r of recipes) {
    const known = exp.recipeTypes[r.type];
    const info = (recipeTypes[r.type] ??= {
      name: known?.name ?? r.type,
      shape: known && SHAPES.includes(known.shape) ? known.shape : "generic",
      count: 0,
      ...(known?.mod ? { mod: known.mod } : {}),
    });
    info.count++;
  }

  const patternCache = new Map<string, number[]>();
  recipes.forEach((recipe, ri) => {
    const used = new Set<number>();
    for (const slot of slotItems(recipe, codes, patternCache)) for (const i of slot) used.add(i);
    for (const i of used) pushTo((details[i]!.usedIn ??= {}), recipe.type, ri);
    for (const i of outputItems(recipe, codes)) pushTo((details[i]!.madeBy ??= {}), recipe.type, ri);
  });

  codes.forEach((code, i) => {
    const out = exp.items[code]!.attributes?.smelting?.output?.code;
    if (!out) return;
    const target = indexOfSorted(codes, out);
    if (target < 0) return;
    details[i]!.smeltsInto = target;
    (details[target]!.smeltedFrom ??= []).push(i);
  });

  const files = new Map<string, unknown>();
  const itemChunks = chunkBy(details, maxItems, chunkBytes);
  itemChunks.forEach((start, n) => {
    const end = itemChunks[n + 1] ?? details.length;
    const chunk: ItemChunk = { start, items: details.slice(start, end) };
    files.set(`items/${n}.json`, chunk);
  });
  const recipeChunks = chunkBy(recipes, maxRecipes, chunkBytes);
  recipeChunks.forEach((start, n) => {
    const end = recipeChunks[n + 1] ?? recipes.length;
    const chunk: RecipeChunk = { start, recipes: recipes.slice(start, end) };
    files.set(`recipes/${n}.json`, chunk);
  });

  const entities = entitiesFrom(exp, codes);
  const entityChunks = chunkBy(entities.sources, maxItems, chunkBytes);
  entityChunks.forEach((start, n) => {
    const end = entityChunks[n + 1] ?? entities.sources.length;
    const chunk: EntityChunk = { start, entities: entities.sources.slice(start, end) };
    files.set(`entities/${n}.json`, chunk);
  });

  const meta: Meta = {
    format: DATA_FORMAT,
    pack: exp.pack,
    generator: exp.generator,
    // `extra` is unbounded and the app does not read it; meta.json has to stay small.
    mods: Object.fromEntries(Object.entries(exp.mods).map(([id, { extra: _extra, ...mod }]) => [id, mod])),
    recipeTypes,
    itemCount: codes.length,
    recipeCount: recipes.length,
    itemChunks,
    recipeChunks,
    entityCount: entities.index.codes.length,
    entityChunks,
  };
  files.set("meta.json", meta);
  files.set("entities.json", entities.index);
  files.set("search.json", search);
  return { files, meta };
}
