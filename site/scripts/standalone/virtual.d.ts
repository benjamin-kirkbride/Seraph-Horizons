// The standalone viewer's built-in model (scripts/standalone-viewer.ts writes the module).
declare module "virtual:standalone-model" {
  import type { PublishedModel } from "../../src/lib/model-manifest.ts";
  import type { Rig, Shape } from "../../src/lib/rig.ts";
  export const model: PublishedModel;
  export const shape: Shape;
  export const rig: Rig | null;
}
