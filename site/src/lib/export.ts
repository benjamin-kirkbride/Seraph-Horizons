// Types for schema/recipe-export.schema.json (schemaVersion 1). The schema is the
// contract; these mirror it and are only as strict as the app needs.

import type { PowerData } from "./power-data.ts";

export type Kind = "item" | "block";
export type Shape = "grid" | "voxels" | "barrel" | "alloy" | "cooking" | "construction" | "butchery" | "transition" | "tub" | "lottery" | "machine" | "generic";

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
  /** Nutrients added to farmland, in percent. */
  fertilizer?: { n: number; p: number; k: number };
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
  /** Item values (the pack's price table): gears per item, with rusty gear = 1, or per litre with `valuePerLitre`. */
  value?: number;
  /** Worth under a gear per full stack: traders treat it as worthless. */
  floorZero?: boolean;
  /** `value` is in gears per litre, not per item: a liquid the table prices by the litre. */
  valuePerLitre?: boolean;
  /** Config switches the value depends on; the value is the default config's. */
  valueSwitches?: string[];
  /** The config switch that adds the item. */
  switch?: string;
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
  /** The config switch of the pack's own mod that adds the recipe. */
  switch?: string;
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
  transition?: Transition;
  tub?: Tub;
  lottery?: Lottery;
  machine?: Machine;
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

/** In-game hours: the average and the spread each stack draws its own value from. */
export interface Hours {
  avg: number;
  var?: number;
}

/**
 * A stack that turns into another after some time: the recipe's first ingredient into its
 * one output, whose quantity is stacks out per stack in. By itself (the collectible's
 * transitionableProps), or on a station that further ingredients name (role `station`).
 */
export interface Transition {
  /** The engine's EnumTransitionType in lower case (perish, dry, cure, ...), or a mod's own process (smoke). */
  type: string;
  /** Hours before it starts. */
  freshHours: Hours;
  /** Hours it then takes. */
  transitionHours: Hours;
}

/**
 * A batch of gears in a liquid in a vessel (ingredient roles batch, liquid, station) until it
 * turns into the first output; with `failure`, gears can be lost to that output.
 */
export interface Tub {
  /** pickle: an acid leaves the metal clean. passivate: nitric acid leaves it passive. (rust: brine, in older exports.) */
  kind: string;
  hours: number;
  batchSize: number;
  /** Litres of the liquid a finished batch uses up. */
  litresPerBatch?: number;
  /** Hours past done before the liquid starts on the batch. */
  graceHours?: number;
  /** After the grace, one gear is lost every this many hours. */
  lossEveryHours?: number;
  /** Each gear's chance to come out as the failure at done. */
  lossChance?: number;
  /** Index into `outputs` of what a lost gear becomes. */
  failure?: number;
}

/** The one ingredient is decided item by item into one of the outcomes; the chances add up to 1. */
export interface Lottery {
  /** inventory: when it lands in a player's inventory. */
  trigger: string;
  outcomes: { chance: number; outputs: number[] }[];
}

/**
 * A powered machine (the ingredient with role station) that makes the outputs a job at a
 * time; `kept` ingredients are fitted and never consumed.
 */
export interface Machine {
  power: string;
  /** Input shaft turns per job. */
  turns: number;
  work?: { amount: number; unit: string; turnsPerUnit?: number };
  kept?: number[];
  wear?: { ingredient: number; rule: "fixed" | "dividedByOilFill" };
  /** Points drained from a tank of `tank` per job, 100 to the litre. */
  oil?: { ingredient: number; points: number; tank?: number };
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
  /**
   * Tidy Variants' groups, by group id: the variants the pack shows as one creative-menu
   * tile and one handbook group. Absent without the pack's own mod or with Tidy Variants off.
   */
  variantGroups?: Record<string, VariantGroup>;
  /** Mechanical power producers, consumers and the wind (power-data.ts). Absent in older exports. */
  power?: PowerData;
  /** Structures built block by block (docs/recipe-browser/multiblocks.md); absent from older exports. */
  multiblocks?: Multiblocks;
  extra?: Record<string, unknown>;
}

export interface Multiblocks {
  structures: Multiblock[];
  /** How each block a part is drawn with looks, by code. */
  shapes: Record<string, BlockShape>;
}

export interface Multiblock {
  /** `<domain>:<first code part>` of the block that checks the layout, unique. */
  id: string;
  /** The block that checks the layout, at [0, 0, 0]. */
  code: string;
  name: string;
  mod: string;
  /** Every block carrying this layout. */
  codes: string[];
  parts: MultiblockPart[];
  /** One layout, or one per size. */
  sizes: MultiblockSize[];
  sizeLabel?: string;
  defaultSize?: number;
}

export interface MultiblockSize {
  label?: string;
  /** [x, y, z, part], blocks from the checking block: x east, y up, z south. */
  cells: [number, number, number, number][];
}

export interface MultiblockPart {
  /** The layout's code pattern: a game wildcard, or a regular expression after `@`. */
  pattern: string;
  /** The cell may be empty; with no `block`, it must be. */
  air?: true;
  /** The block the cell is drawn with, a key of `shapes`. */
  block?: string;
  name?: string;
  /** Blocks that fit, at most 24. */
  accepts?: string[];
  acceptsMore?: number;
}

export interface BlockShape {
  draw: "cube" | "none" | "shape";
  shapes?: CompositeShape[];
}

export interface CompositeShape {
  elements: ExportShapeElement[];
  rotateX?: number;
  rotateY?: number;
  rotateZ?: number;
  /** Blocks. */
  offset?: [number, number, number];
  scale?: number;
}

/** A shape file's element as the export has it: voxels, children relative to the parent's `from`. */
export interface ExportShapeElement {
  name: string;
  from: [number, number, number];
  to: [number, number, number];
  rotationOrigin?: [number, number, number];
  rotationX?: number;
  rotationY?: number;
  rotationZ?: number;
  /** The faces drawn, when not all six. */
  faces?: ("north" | "east" | "south" | "west" | "up" | "down")[];
  children?: ExportShapeElement[];
}

export interface VariantGroup {
  /** The group's English title, as the game shows it. */
  title: string;
  /** Two or more item codes, best representative first. A code is in at most one group. */
  members: string[];
}
