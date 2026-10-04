import { defineConfig } from "vite";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import { modelsPlugin } from "./scripts/models.ts";

// A relative base lets the same build run at any sub-path (GitHub Pages serves it under
// /Seraph-Horizons/). Routing is in the hash, so a reload on a deep link never reaches
// the server as a path it does not have. modelsPlugin publishes the model viewer's files
// (site/models.json) from where they live in the repository.
export default defineConfig({
  base: "./",
  plugins: [svelte(), modelsPlugin()],
  build: {
    target: "es2022",
    outDir: "dist",
    // three.js (about 570 kB) is its own chunk, loaded only by the model viewer (#/models).
    chunkSizeWarningLimit: 650,
  },
});
