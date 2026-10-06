// The model viewer's manifest, site/models.json, and what the build publishes from it. The
// manifest names each model's shape and rig by their path in the repository (their source of
// truth, under mods-src/); scripts/models.ts copies them into the site at build time and serves
// them in dev, so the site never keeps a second copy in git. See docs/recipe-browser/models.md.
import { discoverAnchors } from "./model-anchors.ts";
import { checkScenario, type Scenario } from "./model-scenario.ts";
import { compileGlobs, driverMatrix, flattenShape, requiresValues, rideOrder, workOf, type Pose, type Rig, type Shape } from "./rig.ts";

// Poses every driver is evaluated at: each class, part-way through its work.
const CHECK_POSES: Pose[] = [1, 2].map((size) => ({ theta: 0.3, depth: 0.5, lifting: 0.5, travel: 0.3, work: 1, size, presence: 0.5, feed: 0.3, oil: 0.5 }));

export interface ManifestModel {
  /** The URL slug: #/models/<id>. */
  id: string;
  title: string;
  description: string;
  /** Shown on the model's page, always: who made the model and on what terms. */
  credit: string;
  creditUrl?: string;
  /** Repository paths, from the repository's root. */
  shape: string;
  rig?: string;
  scenario?: Scenario;
}

export interface Manifest {
  models: ManifestModel[];
}

/** One model as models/index.json lists it for the app. */
export interface PublishedModel {
  id: string;
  title: string;
  description: string;
  credit: string;
  creditUrl?: string;
  /** Site paths, relative to the page. */
  shape: string;
  rig?: string;
  /** The repository paths they were copied from, for links to the source. */
  source: { shape: string; rig?: string };
  scenario?: Scenario;
  elements: number;
  parts: number;
}

export interface ModelIndex {
  models: PublishedModel[];
}

export const MODELS_DIR = "models";
export const MODEL_INDEX = `${MODELS_DIR}/index.json`;

const ID = /^[a-z0-9][a-z0-9-]*$/;
const REPO_PATH = /^(?!\/)(?!.*(^|\/)\.\.?(\/|$))[\w./-]+\.json$/;

/** Throws an Error listing every problem with a manifest, or returns it typed. */
export function checkManifest(raw: unknown): Manifest {
  const problems: string[] = [];
  const m = raw as Manifest;
  if (typeof raw !== "object" || raw === null || !Array.isArray(m.models)) throw new Error('the manifest needs a "models" array');
  const ids = new Set<string>();
  m.models.forEach((model, i) => {
    const at = `models[${i}]${typeof model?.id === "string" ? ` (${model.id})` : ""}`;
    if (typeof model !== "object" || model === null) {
      problems.push(`${at} is not an object`);
      return;
    }
    if (typeof model.id !== "string" || !ID.test(model.id)) problems.push(`${at}: id must be lower-case letters, digits and dashes`);
    else if (ids.has(model.id)) problems.push(`${at}: id is used twice`);
    else ids.add(model.id);
    for (const key of ["title", "description", "credit"] as const)
      if (typeof model[key] !== "string" || model[key].trim() === "") problems.push(`${at}: ${key} is required`);
    if (model.creditUrl !== undefined && !/^https:\/\/\S+$/.test(model.creditUrl)) problems.push(`${at}: creditUrl must be an https URL`);
    if (typeof model.shape !== "string" || !REPO_PATH.test(model.shape)) problems.push(`${at}: shape must be a .json path inside the repository`);
    if (model.rig !== undefined && (typeof model.rig !== "string" || !REPO_PATH.test(model.rig))) problems.push(`${at}: rig must be a .json path inside the repository`);
    if (model.scenario !== undefined && model.rig === undefined) problems.push(`${at}: a scenario needs a rig`);
  });
  if (problems.length > 0) throw new Error(`site/models.json:\n  ${problems.join("\n  ")}`);
  return m;
}

/** Checks a shape and rig the way the viewer will read them, and returns the counts for the index. */
export function checkModelFiles(model: ManifestModel, shape: unknown, rig: unknown): { elements: number; parts: number } {
  const problems: string[] = [];
  const s = shape as Shape;
  if (typeof shape !== "object" || shape === null || !Array.isArray(s.elements)) throw new Error(`${model.id}: ${model.shape} has no "elements" array`);
  const flat = flattenShape(s.elements);
  for (const f of flat) {
    const e = f.element;
    if (typeof e.name !== "string" || !Array.isArray(e.from) || !Array.isArray(e.to)) problems.push(`element ${f.index} needs a name, from and to`);
  }
  let parts = 0;
  if (rig !== undefined) {
    const r = rig as Rig;
    if (typeof rig !== "object" || rig === null) throw new Error(`${model.id}: ${model.rig} is not an object`);
    const ps = r.parts ?? [];
    if (!Array.isArray(ps)) problems.push(`${model.rig}: parts must be an array`);
    else {
      parts = ps.length;
      try {
        rideOrder(ps);
        compileGlobs(ps);
        // Every driver must evaluate: unknown types, missing pivots, a malformed work or trunkPath, and
        // gauges and rolls without one throw here.
        const path = workOf(r);
        for (const p of ps) for (const d of p.drivers ?? []) for (const pose of CHECK_POSES) driverMatrix(d, pose, path);
      } catch (e) {
        problems.push(`${model.rig}: ${(e as Error).message}`);
      }
      if (model.scenario) {
        const { anchors } = discoverAnchors(r);
        problems.push(...checkScenario(model.scenario, r, anchors, requiresValues(ps)).map((p) => `scenario: ${p}`));
      }
    }
  }
  if (problems.length > 0) throw new Error(`${model.id}:\n  ${problems.join("\n  ")}`);
  return { elements: flat.length, parts };
}

export interface PublishedFile {
  /** Site path, relative to the page. */
  path: string;
  /** Repository path. */
  source: string;
}

/**
 * What the build publishes for a manifest: models/index.json and, per model,
 * models/<id>/shape.json and models/<id>/rig.json copied from `source`. `readJson` reads a
 * repository path and throws when it is missing or not JSON.
 */
export function publishModels(raw: unknown, readJson: (repoPath: string) => unknown): { index: ModelIndex; files: PublishedFile[] } {
  const manifest = checkManifest(raw);
  const files: PublishedFile[] = [];
  const models = manifest.models.map((m): PublishedModel => {
    const shapePath = `${MODELS_DIR}/${m.id}/shape.json`;
    const rigPath = m.rig ? `${MODELS_DIR}/${m.id}/rig.json` : undefined;
    const counts = checkModelFiles(m, readJson(m.shape), m.rig ? readJson(m.rig) : undefined);
    files.push({ path: shapePath, source: m.shape });
    if (m.rig && rigPath) files.push({ path: rigPath, source: m.rig });
    return {
      id: m.id,
      title: m.title,
      description: m.description,
      credit: m.credit,
      ...(m.creditUrl ? { creditUrl: m.creditUrl } : {}),
      shape: shapePath,
      ...(rigPath ? { rig: rigPath } : {}),
      source: { shape: m.shape, ...(m.rig ? { rig: m.rig } : {}) },
      ...(m.scenario ? { scenario: m.scenario } : {}),
      ...counts,
    };
  });
  return { index: { models }, files };
}
