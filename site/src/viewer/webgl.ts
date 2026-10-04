// Kept apart from model-scene.ts so the page can check for WebGL without loading three.js.

/** Whether this browser can give a canvas a WebGL context at all. */
export function webglAvailable(): boolean {
  try {
    const c = document.createElement("canvas");
    return !!(c.getContext("webgl2") ?? c.getContext("webgl"));
  } catch {
    return false;
  }
}
