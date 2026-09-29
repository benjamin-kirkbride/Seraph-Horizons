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

/** data/<version>/meta.json: small, loaded first. */
export interface Meta {
  format: number;
  pack: { id: string; version: string; gameVersion: string };
  generator: { name: string; version: string };
  mods: Record<string, Mod>;
  recipeTypes: Record<string, TypeInfo>;
  itemCount: number;
  recipeCount: number;
  /** First item index of each item chunk, ascending. Chunk n is items/<n>.json. */
  itemChunks: number[];
  /** First recipe index of each recipe chunk, ascending. Chunk n is recipes/<n>.json. */
  recipeChunks: number[];
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
