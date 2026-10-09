// Turns one export into the files the app loads (see format.ts). Everything that needs
// the whole data set, the reverse indexes above all, is computed here so the browser
// only ever fetches what one page shows.
import type { Recipe, RecipeExport, Shape, Source } from "./export.ts";
import {
  DATA_FORMAT,
  FLAG_BLOCK,
  FLAG_FLOOR_ZERO,
  FLAG_PER_LITRE,
  type EntityChunk,
  type EntityIndex,
  type EntityVariant,
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
  /** Each mod's ModDB asset id, by modid (assetIdsFromLock). */
  assetIds?: Record<string, number>;
}

/**
 * The ModDB asset id of every mod in a pack/lock.json. All versions are built against the
 * repo's current lock, which is fine: an asset id never changes. A lock from before
 * `packtool lock` recorded them, or anything unreadable, gives none.
 */
export function assetIdsFromLock(lock: unknown): Record<string, number> {
  const mods = (lock as { mods?: unknown } | null)?.mods;
  const ids: Record<string, number> = {};
  if (!Array.isArray(mods)) return ids;
  for (const m of mods as { id?: unknown; assetId?: unknown }[]) {
    if (typeof m?.id === "string" && Number.isSafeInteger(m.assetId) && (m.assetId as number) > 0) ids[m.id] = m.assetId as number;
  }
  return ids;
}

export interface Prepared {
  /** Path relative to the version directory, to JSON value. */
  files: Map<string, unknown>;
  meta: Meta;
}

const SHAPES: readonly Shape[] = ["grid", "voxels", "barrel", "alloy", "cooking", "construction", "butchery", "transition", "tub", "lottery", "machine", "generic"];

/** The item nearly every food perishes into. */
export const ROT = "game:rot";

/**
 * Perishing into rot: thousands of records in the pack, which would bury rot's own recipes.
 * They are listed on the page of what perishes only, and rot's page counts them
 * (`madeByElsewhere`). Perishing into anything else (wine into vinegar) is listed on both
 * pages, like any other recipe.
 */
export function listedOnSourceOnly(recipe: Recipe): boolean {
  return recipe.transition?.type === "perish" && recipe.outputs.length === 1 && recipe.outputs[0]!.code === ROT;
}

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

/**
 * The sources the site shows: the trades of a trader the pack replaces (`extra.replaced`: its
 * trader grid turns the spawners of the game's and other mods' traders into its own, so that
 * trader is never met) are left out, as the pack leaves them out of the handbook.
 */
export function shownSources(sources: readonly Source[] | undefined): Source[] {
  return (sources ?? []).filter((s) => s.extra?.replaced !== true);
}

/** The mod that owns an asset domain: the mod of that id, else one that lists the domain. */
function modOfDomain(exp: RecipeExport, domain: string): string {
  if (exp.mods[domain]) return domain;
  for (const [id, mod] of Object.entries(exp.mods)) if (mod.domains?.includes(domain)) return id;
  return domain;
}

const capitalise = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/**
 * A name for an entity type, which the game does not have: the words every variant's name
 * starts or ends with, leaving out what is in brackets ("Wolf (male)" and "Wolf pup
 * (female)" give "Wolf"; "Surface Drifter" and "Deep Drifter" give "Drifter"). Failing
 * that, the code: game:fish-saltwater is "Fish (saltwater)".
 */
export function entityTypeName(type: string, variantNames: readonly string[]): string {
  const words = [...new Set(variantNames)].map((n) => n.replace(/\s*\([^)]*\)\s*$/, "").split(/\s+/).filter(Boolean));
  if (words.length > 0) {
    const first = words[0]!;
    let pre = first.length;
    let suf = first.length;
    for (const w of words) {
      let i = 0;
      while (i < pre && i < w.length && w[i]!.toLowerCase() === first[i]!.toLowerCase()) i++;
      pre = i;
      let j = 0;
      while (j < suf && j < w.length && w[w.length - 1 - j]!.toLowerCase() === first[first.length - 1 - j]!.toLowerCase()) j++;
      suf = j;
    }
    if (pre > 0) return capitalise(first.slice(0, pre).join(" "));
    if (suf > 0) return capitalise(first.slice(first.length - suf).join(" "));
  }
  const [head, ...rest] = type.slice(type.indexOf(":") + 1).split("-");
  return capitalise(head!) + (rest.length > 0 ? ` (${rest.join(", ")})` : "");
}

/**
 * Creature and trader types, from the item sources that name them. The export has no list
 * of entities of its own, so one that gives nothing is not here. A source's
 * `extra.entityType` says which type its entity is a variant of; exports from before it was
 * written make every entity a type of its own.
 *
 * `recipes` (the enabled recipes, in index order) adds each type's butchery records, and
 * the creatures they name that give nothing else.
 */
export function entitiesFrom(
  exp: RecipeExport,
  codes: readonly string[],
  recipes: readonly Recipe[] = [],
): { index: EntityIndex; types: EntityVariant[][] } {
  const byType = new Map<string, Map<string, EntityVariant>>();
  const recipesOf = new Map<string, number[]>();
  recipes.forEach((r, ri) => {
    const b = r.butchery;
    if (!b) return;
    let variants = byType.get(b.entityType);
    if (!variants) byType.set(b.entityType, (variants = new Map()));
    for (const v of b.variants) {
      for (const e of v.entities) {
        if (!variants.has(e.code)) variants.set(e.code, { code: e.code, name: e.name || e.code, sources: [] });
      }
    }
    const list = recipesOf.get(b.entityType) ?? [];
    list.push(ri);
    recipesOf.set(b.entityType, list);
  });
  codes.forEach((code, item) => {
    for (const { from, fromName, ...rest } of shownSources(exp.items[code]!.sources)) {
      if (!ENTITY_SOURCES.has(rest.type)) continue;
      const declared = rest.extra?.entityType;
      const type = typeof declared === "string" && declared !== "" ? declared : from;
      let variants = byType.get(type);
      if (!variants) byType.set(type, (variants = new Map()));
      let v = variants.get(from);
      if (!v) variants.set(from, (v = { code: from, name: fromName || from, sources: [] }));
      let extra: Record<string, unknown> | undefined;
      if (rest.extra) {
        const { entityType: _type, ...others } = rest.extra;
        if (Object.keys(others).length > 0) extra = others;
      }
      const { extra: _extra, ...fields } = rest;
      v.sources.push({ ...fields, ...(extra ? { extra } : {}), item });
    }
  });
  const index: EntityIndex = {
    codes: [...byType.keys()].sort(compareCodes),
    names: [],
    mod: [],
    variantNames: [],
    drops: [],
    trades: [],
    recipes: [],
  };
  const types = index.codes.map((code) => {
    const variants = [...byType.get(code)!.values()].sort((a, b) => compareCodes(a.code, b.code));
    const names = variants.map((v) => v.name);
    index.names.push(entityTypeName(code, names));
    index.mod.push(modOfDomain(exp, code.slice(0, code.indexOf(":"))));
    index.variantNames.push([...new Set(names)]);
    const drops = new Set<number>();
    const trades = new Set<number>();
    for (const v of variants) for (const s of v.sources) (s.type === "entityDrop" ? drops : trades).add(s.item);
    index.drops.push(drops.size);
    index.trades.push(trades.size);
    index.recipes.push(recipesOf.get(code) ?? []);
    return variants;
  });
  return { index, types };
}

/**
 * The export's Tidy Variants groups as search.json carries them: titles, and member item
 * indices in the export's rank order, groups in id order. site-data has checked the export,
 * but an older or hand-made one may still name an unknown item or put an item in two groups;
 * such members are skipped (an item stays in its first group) and a group left with fewer
 * than two is dropped, since it groups nothing.
 */
function variantGroups(exp: RecipeExport, codes: readonly string[]): SearchFile["groups"] | undefined {
  const titles: string[] = [];
  const members: number[][] = [];
  const taken = new Set<number>();
  for (const id of Object.keys(exp.variantGroups ?? {}).sort(compareCodes)) {
    const group = exp.variantGroups![id]!;
    const items: number[] = [];
    for (const code of group.members ?? []) {
      const i = indexOfSorted(codes, code);
      if (i < 0 || taken.has(i)) continue;
      taken.add(i);
      items.push(i);
    }
    if (items.length < 2) continue;
    titles.push(group.title || exp.items[codes[items[0]!]!]!.name || id);
    members.push(items);
  }
  return titles.length > 0 ? { titles, members } : undefined;
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
  // Item values: one number (or null) per row, so the item page, search and the values page
  // read them without fetching a chunk.
  const values: (number | null)[] = [];
  const valueSwitches: Record<string, string[]> = {};
  for (const code of codes) {
    const item = exp.items[code]!;
    search.names.push(item.name || code);
    search.mod.push(modIndex.get(item.mod)!);
    search.flags.push(
      (item.handbookVisible ? FLAG_HANDBOOK : 0) | (item.kind === "block" ? FLAG_BLOCK : 0) | (item.floorZero ? FLAG_FLOOR_ZERO : 0) |
        (item.valuePerLitre ? FLAG_PER_LITRE : 0),
    );
    values.push(typeof item.value === "number" && Number.isFinite(item.value) ? item.value : null);
    if (item.valueSwitches && item.valueSwitches.length > 0) valueSwitches[String(values.length - 1)] = item.valueSwitches;
    const d: ItemDetail = {};
    if (item.description) d.description = item.description;
    if (item.attributes && Object.keys(item.attributes).length > 0) d.attributes = item.attributes;
    const sources = shownSources(item.sources);
    if (sources.length > 0) d.sources = sources;
    details.push(d);
  }

  // Disabled definitions cannot be crafted, so listing them would mislead. Sorting by
  // type first keeps each type's recipes in one run, which is all its page needs. An id
  // starts with its type, so this is the id order but for a type that is a prefix of
  // another.
  const recipes = exp.recipes
    .filter((r) => r.enabled !== false)
    .sort((a, b) => compareCodes(a.type, b.type) || compareCodes(a.id, b.id));

  const recipeTypes: Record<string, TypeInfo> = {};
  recipes.forEach((r, ri) => {
    const known = exp.recipeTypes[r.type];
    const info = (recipeTypes[r.type] ??= {
      name: known?.name ?? r.type,
      shape: known && SHAPES.includes(known.shape) ? known.shape : "generic",
      count: 0,
      start: ri,
      ...(known?.mod ? { mod: known.mod } : {}),
    });
    info.count++;
  });

  const patternCache = new Map<string, number[]>();
  recipes.forEach((recipe, ri) => {
    const used = new Set<number>();
    for (const slot of slotItems(recipe, codes, patternCache)) for (const i of slot) used.add(i);
    for (const i of used) pushTo((details[i]!.usedIn ??= {}), recipe.type, ri);
    if (listedOnSourceOnly(recipe)) {
      for (const i of outputItems(recipe, codes)) {
        const elsewhere = (details[i]!.madeByElsewhere ??= {});
        elsewhere[recipe.type] = (elsewhere[recipe.type] ?? 0) + 1;
      }
    } else {
      for (const i of outputItems(recipe, codes)) pushTo((details[i]!.madeBy ??= {}), recipe.type, ri);
    }
  });

  // A block's page says what it gives. Creatures and traders have pages of their own.
  codes.forEach((code, item) => {
    for (const { from, fromName: _name, ...rest } of exp.items[code]!.sources ?? []) {
      if (ENTITY_SOURCES.has(rest.type)) continue;
      const block = indexOfSorted(codes, from);
      if (block >= 0) (details[block]!.gives ??= []).push({ ...rest, item });
    }
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

  const entities = entitiesFrom(exp, codes, recipes);
  const entityChunks = chunkBy(entities.types, maxItems, chunkBytes);
  entityChunks.forEach((start, n) => {
    const end = entityChunks[n + 1] ?? entities.types.length;
    const chunk: EntityChunk = { start, entities: entities.types.slice(start, end) };
    files.set(`entities/${n}.json`, chunk);
  });

  const valueCount = values.filter((v) => v !== null).length;
  if (valueCount > 0) {
    search.value = values;
    if (Object.keys(valueSwitches).length > 0) search.valueSwitches = valueSwitches;
  }
  const groups = variantGroups(exp, codes);
  if (groups) search.groups = groups;

  const meta: Meta = {
    format: DATA_FORMAT,
    pack: exp.pack,
    generator: exp.generator,
    // `extra` is unbounded and the app does not read it; meta.json has to stay small.
    mods: Object.fromEntries(
      Object.entries(exp.mods).map(([id, { extra: _extra, ...mod }]) => {
        const assetId = options.assetIds?.[id];
        return [id, assetId === undefined ? mod : { ...mod, assetId }];
      }),
    ),
    recipeTypes,
    itemCount: codes.length,
    recipeCount: recipes.length,
    itemChunks,
    recipeChunks,
    entityCount: entities.index.codes.length,
    entityChunks,
    ...(valueCount > 0 ? { valueCount } : {}),
    ...(exp.power ? { power: true } : {}),
  };
  files.set("meta.json", meta);
  files.set("entities.json", entities.index);
  files.set("search.json", search);
  // The power page's data, as the export has it (power-data.ts). An export without it, such as
  // every one made before it existed, gives no power.json, which the app reads as none.
  if (exp.power) files.set("power.json", exp.power);
  return { files, meta };
}
