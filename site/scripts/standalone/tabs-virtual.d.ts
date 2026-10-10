// The tabbed standalone page's built-in modules (scripts/standalone-tabs.ts writes them per bundle).
declare module "virtual:standalone-tabs-data" {
  import type { PublishedModel } from "../../src/lib/model-manifest.ts";
  import type { Rig, Shape } from "../../src/lib/rig.ts";
  /** The bundle's name in the page's registry (globalThis.__standaloneTabs). */
  export const group: string;
  /** Each model tab of the bundle by its slug. */
  export const tabs: Record<string, { model: PublishedModel; shape: Shape; rig: Rig | null; bogie: Shape | null }>;
}

declare module "virtual:standalone-tabs-page" {
  // The model page of the checkout whose viewer code draws the bundle's tabs.
  import ModelPage from "../../src/components/ModelPage.svelte";
  export default ModelPage;
}
