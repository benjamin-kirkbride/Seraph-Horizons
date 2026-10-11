// The eidolon's fitting sheet (#/<version>/eidolon): the export's `eidolon` section
// (docs/recipe-browser/schema.md, "The eidolon's fittings"), and what the page derives from a choice
// of material per layer: the stats (looked up in the export's builds, which the mod's own
// EidolonStats computed), the textures the model wears, each layer's effect and the bill. Pure.

export interface EidolonStack {
  code: string;
  count: number;
}

export interface EidolonMaterial {
  /** As the mod stores it: a wood, a metal code (stainlesssteel), or leather-<colour> / cloth-<colour>. */
  id: string;
  /** domain:path under textures/, no extension; null when the mod names none. */
  texture: string | null;
  /** What a layer of it takes, its Jonas parts aside. */
  items: EidolonStack[];
}

export interface EidolonLayer {
  /** The layer and its texture code in the eidolon's shape. */
  id: string;
  name: string;
  /** An eidolon from before fittings: the shape's default look. */
  default: string | null;
  materials: EidolonMaterial[];
  /** Taken whatever the material, given back when it is stripped. */
  parts: EidolonStack[];
}

export interface PlateArmour {
  protectionTier: number;
  relativeProtection: number;
  flatDamageReduction: number;
}

export interface EidolonSettings {
  allSteelHealth: number;
  baseHealth: number;
  plateHealth: Record<string, number>;
  jointHealth: Record<string, number>;
  skirtHealth: Record<string, number>;
  plateDamage: Record<string, number>;
  plateMoveSpeed: Record<string, number>;
  plateArmour: Record<string, PlateArmour | null>;
  armourLoss: Record<string, number | boolean>;
  walkSpeed: number;
  runSpeed: number;
  defenceDamage: number;
  slamDamage: number;
}

export interface EidolonBuildStats {
  joints: string;
  plates: string;
  skirt: string;
  maxHealth: number;
  walkSpeed: number;
  runSpeed: number;
  defenceDamage: number;
  slamDamage: number;
  armour: PlateArmour | null;
}

/** The export's eidolon section. */
export interface EidolonExport {
  mod: string;
  item: string | null;
  torso: { item: string | null; recipe: EidolonStack[] };
  mind: EidolonStack[];
  layers: EidolonLayer[];
  settings: EidolonSettings;
  builds: EidolonBuildStats[];
  /** The shape's texture codes and their paths. */
  shapeTextures: Record<string, string>;
}

/** eidolon.json: the export's section with the images of its textures, which prepare-data takes
 * from the repository's texture store (textures/, docs/recipe-browser/icons.md). */
export interface EidolonData extends EidolonExport {
  /** Texture path → PNG data URI, for each the store has. */
  textures: Record<string, string>;
}

/** Every texture path the section names: its layers' materials' and its shape's own, sorted.
 * The same set tools/textures.py imports. */
export function eidolonTexturePaths(e: EidolonExport): string[] {
  const paths = new Set(Object.values(e.shapeTextures));
  for (const l of e.layers) for (const m of l.materials) if (m.texture) paths.add(m.texture);
  return [...paths].sort();
}

/** A material chosen for each layer, by layer id. */
export type FittingPick = Record<string, string>;

/** Each layer at its default (the all-steel build an old eidolon loads as), else its first material. */
export function defaultPick(d: EidolonData): FittingPick {
  const pick: FittingPick = {};
  for (const l of d.layers) {
    const m = l.materials.find((x) => x.id === l.default) ?? l.materials[0];
    if (m) pick[l.id] = m.id;
  }
  return pick;
}

export function material(d: EidolonData, layer: string, id: string | undefined): EidolonMaterial | null {
  return d.layers.find((l) => l.id === layer)?.materials.find((m) => m.id === id) ?? null;
}

const METALS: Record<string, string> = { iron: "Iron", steel: "Steel", stainlesssteel: "Stainless", brass: "Brass" };
const WORDS: Record<string, string> = { baldcypress: "bald cypress", purpleheart: "purple heart", veryaged: "very aged" };

const cap = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/** A sash material's kind and colour: leather-red → leather, red. */
export function sashParts(id: string): { kind: string; colour: string } {
  const dash = id.indexOf("-");
  return dash < 0 ? { kind: id, colour: "" } : { kind: id.slice(0, dash), colour: id.slice(dash + 1) };
}

/** What a material is called on the page: "Bald cypress", "Stainless", "Red". */
export function materialLabel(layer: string, id: string): string {
  if (layer === "sash") return cap(sashParts(id).colour);
  return METALS[id] ?? cap(WORDS[id] ?? id);
}

/** Whether a wood is a salvaged beam's (aged, very aged), listed apart. */
export function isAgedWood(id: string): boolean {
  return /aged$/.test(id);
}

/** The stats of the picked build, as the export lists them; null when it does not (an older export, a pick it lacks). */
export function statsFor(d: EidolonData, pick: FittingPick): EidolonBuildStats | null {
  return d.builds.find((b) => b.joints === pick.joints && b.plates === pick.plates && b.skirt === pick.skirt) ?? null;
}

/** How a layer's material changes the stats, against iron, from the settings: e.g. ["HP ×1.5"]. Bones and sash: none. */
export function layerEffect(d: EidolonData, layer: string, id: string): string[] {
  const s = d.settings;
  const x = (label: string, table: Record<string, number> | undefined) => {
    const v = table?.[id];
    return v !== undefined && v !== 1 ? [`${label} ×${round(v, 3)}`] : [];
  };
  switch (layer) {
    case "joints":
      return x("HP", s.jointHealth);
    case "plates":
      return [...x("HP", s.plateHealth), ...x("damage", s.plateDamage), ...x("speed", s.plateMoveSpeed)];
    case "skirt":
      return x("HP", s.skirtHealth);
    default:
      return [];
  }
}

/** Whether a layer changes any stat at all (bones and sash are looks only). */
export function layerHasStats(layer: string): boolean {
  return layer === "joints" || layer === "plates" || layer === "skirt";
}

export function round(v: number, places: number): string {
  const f = 10 ** places;
  return String(Math.round(v * f) / f);
}

/** Each texture code of the shape and the path it draws with for the pick: the layers' materials over the shape's own. */
export function texturePaths(d: EidolonData, pick: FittingPick): Map<string, string> {
  const out = new Map(Object.entries(d.shapeTextures));
  for (const l of d.layers) {
    const path = material(d, l.id, pick[l.id])?.texture;
    if (path) out.set(l.id, path);
  }
  return out;
}

/** The images the model wears for the pick, by texture code: those the export carries. */
export function textureImages(d: EidolonData, pick: FittingPick): Map<string, string> {
  const out = new Map<string, string>();
  for (const [code, path] of texturePaths(d, pick)) {
    const uri = d.textures[path];
    if (uri) out.set(code, uri);
  }
  return out;
}

export interface BillRow {
  /** torso, a layer id, or mind. */
  id: string;
  name: string;
  /** The material's items (which follow the pick) first, then the parts. */
  items: { stack: EidolonStack; chosen: boolean }[];
}

/** The bill stage by stage: the torso's grid recipe, each layer in the picked material with its parts, the mind. */
export function bill(d: EidolonData, pick: FittingPick, names: { torso: string; mind: string }): BillRow[] {
  const rows: BillRow[] = [{ id: "torso", name: names.torso, items: d.torso.recipe.map((stack) => ({ stack, chosen: false })) }];
  for (const l of d.layers) {
    const m = material(d, l.id, pick[l.id]);
    rows.push({
      id: l.id,
      name: l.name,
      items: [...(m?.items ?? []).map((stack) => ({ stack, chosen: true })), ...l.parts.map((stack) => ({ stack, chosen: false }))],
    });
  }
  rows.push({ id: "mind", name: names.mind, items: d.mind.map((stack) => ({ stack, chosen: false })) });
  return rows;
}

/** A stat against the default build's: the share it is above or below, or 0 when the same (within half a thousandth). */
export function change(value: number, against: number): number {
  if (against === 0) return 0;
  const r = value / against - 1;
  return Math.abs(r) < 0.0005 ? 0 : r;
}
