// Stands in for src/lib/model-data.ts in the standalone viewer (scripts/standalone-viewer.ts): the
// model's index entry, shape, rig and bogie shape come from the page itself, not from the network.
import type { ModelFiles } from "../../src/lib/model-data.ts";
import type { ModelIndex, PublishedModel } from "../../src/lib/model-manifest.ts";
import { bogie, model, rig, shape } from "virtual:standalone-model";

export function loadModelIndex(): Promise<ModelIndex> {
  return Promise.resolve({ models: [model] });
}

export function loadModelFiles(m: PublishedModel): Promise<ModelFiles> {
  if (m.id !== model.id) return Promise.reject(new Error(`${m.id} is not built into this page`));
  return Promise.resolve({ shape, rig, bogie });
}
