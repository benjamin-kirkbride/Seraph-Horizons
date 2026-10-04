// Types for schema/recipe-export.schema.json (schemaVersion 1). The schema is the
// contract; these mirror it and are only as strict as the app needs.

export type Kind = "item" | "block";
export type Shape = "grid" | "voxels" | "barrel" | "alloy" | "cooking" | "construction" | "butchery" | "generic";

export interface Mod {
  name: string;
  version: string;
  authors?: string[];
  website?: string;
  description?: string;
  side?: string;
  domains?: string[];
  extra?: Record<string, unknown>;
}

export interface Stack {
  code: string;
  kind: Kind;
  quantity: number;
  litres?: number;
  attributes?: Record<string, unknown>;
  name?: string;
}

export interface ItemAttributes {
  maxStackSize?: number;
  durability?: number;
  tool?: string;
  toolTier?: number;
  requiredMiningTier?: number;
  attackPower?: number;
  materialDensity?: number;
  nutrition?: { category: string; satiety: number; health?: number };
  burn?: { temperature?: number; durationSeconds?: number };
  smelting?: {
    meltingPoint?: number;
    durationSeconds?: number;
    inputQuantity?: number;
    requiresContainer?: boolean;
    method?: string;
    output?: Stack;
  };
  storageFlags?: string[];
  extra?: Record<string, unknown>;
}

export type SourceType = "blockDrop" | "entityDrop" | "traderSells" | "traderBuys" | "other";

export interface Source {
  type: SourceType;
  from: string;
  fromName?: string;
  quantity?: { avg: number; var?: number };
  tool?: string;
  price?: number;
  note?: string;
  extra?: Record<string, unknown>;
}

export interface Item {
  kind: Kind;
  name: string;
  mod: string;
  handbookVisible: boolean;
  description?: string;
  attributes?: ItemAttributes;
  sources?: Source[];
  extra?: Record<string, unknown>;
}

export interface Ingredient {
  key?: string;
  code: string;
  kind: Kind;
  quantity: number;
  litres?: number;
  attributes?: Record<string, unknown>;
  wildcardName?: string;
  allowedVariants?: string[];
  skipVariants?: string[];
  isTool?: boolean;
  toolDurabilityCost?: number;
  returned?: Stack;
  role?: string;
  minQuantity?: number;
  maxQuantity?: number;
  minRatio?: number;
  maxRatio?: number;
  extra?: Record<string, unknown>;
}

export interface Output {
  code: string;
  kind: Kind;
  quantity: number;
  litres?: number;
  attributes?: Record<string, unknown>;
  extra?: Record<string, unknown>;
}

export interface Variant {
  bindings?: Record<string, string>;
  ingredients: Stack[][];
  outputs: Stack[];
}

export interface Grid {
  width: number;
  height: number;
  shapeless: boolean;
  pattern: string[];
  copyAttributesFrom?: string;
}

export interface Recipe {
  id: string;
  type: string;
  mod: string;
  source?: string;
  enabled?: boolean;
  ingredients: Ingredient[];
  outputs: Output[];
  variants: Variant[];
  grid?: Grid;
  voxels?: string[][];
  barrel?: { sealHours?: number };
  alloy?: Record<string, never>;
  cooking?: { code?: string; cooksInto?: Stack; dirtyPot?: boolean };
  construction?: { stages: { ingredients: number[]; action?: string }[] };
  butchery?: Butchery;
  requirements?: string[];
  extra?: Record<string, unknown>;
}

export type ButcheryStep = "pickUp" | "skin" | "bleed" | "butcher" | "harvest";

export interface ButcheryStage {
  step: ButcheryStep;
  /** Ingredient indices the stage needs. */
  ingredients: number[];
  /** Ingredient index groups; the stage needs one of them as well. */
  options?: number[][];
  /** Ingredient indices the stage does without; an output that needs one says so in `extra.needs`. */
  optional?: number[];
  /** Output indices. */
  outputs: number[];
  hours?: number;
  multiplier?: number;
}

export interface Yield {
  avg: number;
  var?: number;
}

/** What one creature gives with the Butchering mod, stage by stage. */
export interface Butchery {
  entityType: string;
  workload?: string;
  /** The creature's lowest and highest condition (animalWeight); missing in older exports. */
  condition?: { min: number; max: number };
  stages: ButcheryStage[];
  /** Aligned with the recipe's variants; each one's yields with its outputs. */
  variants: { entities: { code: string; name?: string }[]; yields: (Yield | null)[] }[];
}

export interface RecipeType {
  name: string;
  count: number;
  shape: Shape;
  registry?: string;
  mod?: string;
}

export interface Guide {
  code: string;
  title: string;
  text: string;
  mod?: string;
}

export interface RecipeExport {
  schemaVersion: 1;
  generator: { name: string; version: string };
  pack: { id: string; version: string; gameVersion: string };
  mods: Record<string, Mod>;
  items: Record<string, Item>;
  recipes: Recipe[];
  recipeTypes: Record<string, RecipeType>;
  guides: Guide[];
  extra?: Record<string, unknown>;
}
