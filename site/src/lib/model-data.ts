// Fetching the model viewer's files. Paths are relative to the page, like the recipe data,
// and nothing here depends on a recipe export being published.
import { MODEL_INDEX, type ModelIndex, type PublishedModel } from "./model-manifest.ts";
import type { Rig, Shape } from "./rig.ts";

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
  return (await res.json()) as T;
}

let index: Promise<ModelIndex> | null = null;

export function loadModelIndex(): Promise<ModelIndex> {
  index ??= getJson<ModelIndex>(MODEL_INDEX).catch((e: unknown) => {
    index = null;
    throw e;
  });
  return index;
}

export async function loadModelFiles(model: PublishedModel): Promise<{ shape: Shape; rig: Rig | null }> {
  const [shape, rig] = await Promise.all([getJson<Shape>(model.shape), model.rig ? getJson<Rig>(model.rig) : Promise.resolve(null)]);
  if (!Array.isArray(shape.elements)) throw new Error(`${model.shape}: no elements`);
  return { shape, rig };
}
