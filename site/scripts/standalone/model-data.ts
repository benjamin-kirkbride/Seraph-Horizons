// Stands in for src/lib/model-data.ts in the standalone viewer (scripts/standalone-viewer.ts): the
// model's index entry, shape and rig come from the page itself, not from the network.
import type { ModelIndex, PublishedModel } from "../../src/lib/model-manifest.ts";
import type { Rig, Shape } from "../../src/lib/rig.ts";
import { model, rig, shape } from "virtual:standalone-model";

export function loadModelIndex(): Promise<ModelIndex> {
  return Promise.resolve({ models: [model] });
}

export function loadModelFiles(m: PublishedModel): Promise<{ shape: Shape; rig: Rig | null }> {
  if (m.id !== model.id) return Promise.reject(new Error(`${m.id} is not built into this page`));
  return Promise.resolve({ shape, rig });
}
