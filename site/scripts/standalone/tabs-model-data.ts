// Stands in for a checkout's src/lib/model-data.ts in the tabbed standalone page
// (scripts/standalone-tabs.ts): the model files come from the page itself, for the tab being shown.
// Only one tab's viewer is mounted at a time, and tab-viewer.ts selects it before mounting.
import type { ModelFiles } from "../../src/lib/model-data.ts";
import type { ModelIndex, PublishedModel } from "../../src/lib/model-manifest.ts";
import { tabs } from "virtual:standalone-tabs-data";

let current: string | null = null;

export function selectTab(slug: string): void {
  if (!(slug in tabs)) throw new Error(`no model tab "${slug}" in this bundle`);
  current = slug;
}

export function loadModelIndex(): Promise<ModelIndex> {
  return Promise.resolve({ models: Object.values(tabs).map((t) => t.model) });
}

export function loadModelFiles(m: PublishedModel): Promise<ModelFiles> {
  const t = current === null ? undefined : tabs[current];
  if (!t || t.model.id !== m.id) return Promise.reject(new Error(`${m.id} is not the tab being shown`));
  return Promise.resolve({ shape: t.shape, rig: t.rig, bogie: t.bogie });
}
