// The files prepare-data writes under data/<version id>/ and the app reads. See
// docs/recipe-browser/site.md. Bump DATA_FORMAT when this changes incompatibly; the app
// and the data are always built together, so there is no migration.
import type { ItemAttributes, Mod, Recipe, Shape, Source } from "./export.ts";

export const DATA_FORMAT = 1;

/** data/versions.json, written by tools/site-data. */
export interface VersionsFile {
  default: string;
  versions: VersionEntry[];
}

export interface VersionEntry {
  id: string;
  label: string;
  packVersion?: string;
  gameVersion?: string;
  commit?: string;
}

/** A mod as meta.json has it: the export's, without `extra`. */
export interface MetaMod extends Omit<Mod, "extra"> {
  /** Its ModDB asset id, from the repo's pack/lock.json when the pack locks it. */
  assetId?: number;
}

/** data/<version>/meta.json: small, loaded first. */
export interface Meta {
  format: number;
  pack: { id: string; version: string; gameVersion: string };
  generator: { name: string; version: string };
  mods: Record<string, MetaMod>;
  recipeTypes: Record<string, TypeInfo>;
  itemCount: number;
  recipeCount: number;
  /** First item index of each item chunk, ascending. Chunk n is items/<n>.json. */
  itemChunks: number[];
  /** First recipe index of each recipe chunk, ascending. Chunk n is recipes/<n>.json. */
  recipeChunks: number[];
  /** Number of entities in entities.json. */
  entityCount: number;
  /** First entity index of each entity chunk, ascending. Chunk n is entities/<n>.json. */
  entityChunks: number[];
}

export interface TypeInfo {
  name: string;
  shape: Shape;
  count: number;
  mod?: string;
}

/**
 * data/<version>/search.json: every item, column-wise, sorted by code. An item's index
 * in these arrays is its id everywhere else in the data.
 */
export interface SearchFile {
  mods: string[];
  codes: string[];
  names: string[];
  /** Index into `mods`. */
  mod: number[];
  /** Bit flags, see FLAG_*. */
  flags: number[];
}

export const FLAG_HANDBOOK = 1;
export const FLAG_BLOCK = 2;

/** data/<version>/items/<n>.json */
export interface ItemChunk {
  start: number;
  items: ItemDetail[];
}

export interface ItemDetail {
  description?: string;
  attributes?: ItemAttributes;
  sources?: Source[];
  /** Recipe type code to recipe indices whose outputs include this item. */
  madeBy?: Record<string, number[]>;
  /** Recipe type code to recipe indices with an ingredient slot that accepts this item. */
  usedIn?: Record<string, number[]>;
  /** Item indices whose smelting output is this item. */
  smeltedFrom?: number[];
  /** Item index of this item's smelting output, when that item is in the export. */
  smeltsInto?: number;
}

/**
 * data/<version>/entities.json: every creature and trader type that some item names as a
 * source, column-wise and sorted by code. A type is an entity type file of the game
 * (game:wolf); its variants (game:wolf-eurasian-adult-male) share one page. A type's index
 * here is its id in the entity chunks.
 */
export interface EntityIndex {
  codes: string[];
  names: string[];
  /** Mod id, from the code's domain. */
  mod: string[];
  /** Names of each type's variants, for filtering the list. */
  variantNames: string[][];
  /** Distinct items each type drops on death or gives when harvested. */
  drops: number[];
  /** Distinct items each type sells or buys. */
  trades: number[];
  /** Recipe indices about each type's creatures: their butchery. */
  recipes: number[][];
}

/** One item an entity gives: an item's source, turned around. */
export type EntitySource = Omit<Source, "from" | "fromName"> & {
  /** Item index. */
  item: number;
};

export interface EntityVariant {
  code: string;
  name: string;
  sources: EntitySource[];
}

/** data/<version>/entities/<n>.json: each type's variants, sorted by code. */
export interface EntityChunk {
  start: number;
  entities: EntityVariant[][];
}

/** data/<version>/recipes/<n>.json */
export interface RecipeChunk {
  start: number;
  recipes: Recipe[];
}

/** Finds the chunk that holds `index`, given the ascending chunk starts. */
export function chunkOf(starts: readonly number[], index: number): number {
  let lo = 0;
  let hi = starts.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if ((starts[mid] ?? Infinity) <= index) lo = mid;
    else hi = mid - 1;
  }
  return lo;
}
