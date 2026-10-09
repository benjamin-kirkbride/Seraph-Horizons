// Fetching and caching of the prepared data. Paths are relative to the page, which is
// what lets the build run under any sub-path.
import type { Recipe } from "./export.ts";
import {
  chunkOf,
  type EntityChunk,
  type EntityIndex,
  type EntityVariant,
  type ItemChunk,
  type ItemDetail,
  type Meta,
  type MultiblockFile,
  type MultiblockIndex,
  type RecipeChunk,
  type SearchFile,
  type VersionsFile,
} from "./format.ts";
import type { PowerData } from "./power-data.ts";
import { readIconIndex, type IconIndex } from "./icons.ts";
import { ItemSearch } from "./search.ts";
import { indexOfSorted } from "./wildcard.ts";

export class LoadError extends Error {}

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new LoadError(`${url}: HTTP ${res.status}`);
  return (await res.json()) as T;
}

export function loadVersions(): Promise<VersionsFile> {
  return getJson<VersionsFile>("data/versions.json");
}

let iconIndex: Promise<IconIndex | null> | null = null;

/** The icon index, or null when the site was built without icons. */
export function loadIconIndex(): Promise<IconIndex | null> {
  iconIndex ??= fetch("icons/index.json")
    .then((res) => (res.ok ? res.json() : null))
    .then(readIconIndex)
    .catch(() => null);
  return iconIndex;
}

export interface ItemRef {
  index: number;
  code: string;
  name: string;
  mod: string;
  flags: number;
  /** Value in rusty gears, when the item has one. */
  value?: number;
}

export class VersionData {
  readonly id: string;
  private readonly base: string;
  private metaP: Promise<Meta> | null = null;
  private searchP: Promise<SearchFile> | null = null;
  private engine: ItemSearch | null = null;
  private readonly itemChunks = new Map<number, Promise<ItemChunk>>();
  private readonly recipeChunks = new Map<number, Promise<RecipeChunk>>();
  private entitiesP: Promise<EntityIndex> | null = null;
  private powerP: Promise<PowerData | null> | null = null;
  private readonly entityChunks = new Map<number, Promise<EntityChunk>>();
  private multiblocksP: Promise<MultiblockIndex> | null = null;
  private readonly multiblockFiles = new Map<number, Promise<MultiblockFile>>();
  /** Loaded search.json, for synchronous lookups once `ready` resolved. */
  index: SearchFile | null = null;

  constructor(id: string) {
    this.id = id;
    this.base = `data/${encodeURIComponent(id)}/`;
  }

  meta(): Promise<Meta> {
    this.metaP ??= getJson<Meta>(`${this.base}meta.json`);
    return this.metaP;
  }

  searchFile(): Promise<SearchFile> {
    this.searchP ??= getJson<SearchFile>(`${this.base}search.json`).then((f) => {
      this.index = f;
      return f;
    });
    return this.searchP;
  }

  async ready(): Promise<void> {
    await Promise.all([this.meta(), this.searchFile()]);
  }

  async search(query: string, limit?: number): Promise<number[]> {
    const file = await this.searchFile();
    this.engine ??= new ItemSearch(file);
    return this.engine.search(query, limit);
  }

  /** Index of `code`, or -1; needs `ready()` first. */
  indexOf(code: string): number {
    return this.index ? indexOfSorted(this.index.codes, code) : -1;
  }

  ref(index: number): ItemRef | null {
    const f = this.index;
    if (!f || index < 0 || index >= f.codes.length) return null;
    const ref: ItemRef = { index, code: f.codes[index]!, name: f.names[index]!, mod: f.mods[f.mod[index]!]!, flags: f.flags[index]! };
    const value = f.value?.[index];
    if (typeof value === "number") ref.value = value;
    return ref;
  }

  /** Display name of a code; the code itself when the export does not list it. */
  nameOf(code: string): string {
    const i = this.indexOf(code);
    return i >= 0 ? this.index!.names[i]! : code;
  }

  async item(index: number): Promise<ItemDetail> {
    const meta = await this.meta();
    const n = chunkOf(meta.itemChunks, index);
    let p = this.itemChunks.get(n);
    if (!p) {
      p = getJson<ItemChunk>(`${this.base}items/${n}.json`);
      this.itemChunks.set(n, p);
    }
    const chunk = await p;
    return chunk.items[index - chunk.start] ?? {};
  }

  /** entities.json; only the entity pages need it. */
  entities(): Promise<EntityIndex> {
    this.entitiesP ??= getJson<EntityIndex>(`${this.base}entities.json`);
    return this.entitiesP;
  }

  /** power.json; null for a version whose export had no power section (no file, or null). */
  power(): Promise<PowerData | null> {
    this.powerP ??= fetch(`${this.base}power.json`).then(async (res) => {
      if (res.status === 404) return null;
      if (!res.ok) throw new LoadError(`${this.base}power.json: HTTP ${res.status}`);
      return ((await res.json()) as PowerData | null) ?? null;
    });
    return this.powerP;
  }

  /** The variants of entity type `index` of entities.json, with what each gives. */
  async entity(index: number): Promise<EntityVariant[]> {
    const meta = await this.meta();
    const n = chunkOf(meta.entityChunks, index);
    let p = this.entityChunks.get(n);
    if (!p) {
      p = getJson<EntityChunk>(`${this.base}entities/${n}.json`);
      this.entityChunks.set(n, p);
    }
    const chunk = await p;
    return chunk.entities[index - chunk.start] ?? [];
  }

  /** multiblocks.json; only the multiblock pages need it. */
  multiblocks(): Promise<MultiblockIndex> {
    this.multiblocksP ??= getJson<MultiblockIndex>(`${this.base}multiblocks.json`);
    return this.multiblocksP;
  }

  /** One structure's file, multiblocks/<file>.json. */
  multiblock(file: number): Promise<MultiblockFile> {
    let p = this.multiblockFiles.get(file);
    if (!p) {
      p = getJson<MultiblockFile>(`${this.base}multiblocks/${file}.json`);
      this.multiblockFiles.set(file, p);
    }
    return p;
  }

  async recipes(indices: readonly number[]): Promise<Recipe[]> {
    const meta = await this.meta();
    return Promise.all(
      indices.map(async (ri) => {
        const n = chunkOf(meta.recipeChunks, ri);
        let p = this.recipeChunks.get(n);
        if (!p) {
          p = getJson<RecipeChunk>(`${this.base}recipes/${n}.json`);
          this.recipeChunks.set(n, p);
        }
        const chunk = await p;
        const r = chunk.recipes[ri - chunk.start];
        if (!r) throw new LoadError(`recipe ${ri} is missing from chunk ${n}`);
        return r;
      }),
    );
  }
}

const versions = new Map<string, VersionData>();

export function versionData(id: string): VersionData {
  let v = versions.get(id);
  if (!v) {
    v = new VersionData(id);
    versions.set(id, v);
  }
  return v;
}
