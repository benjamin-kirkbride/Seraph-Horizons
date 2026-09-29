import { defineConfig } from "vite";
import { svelte } from "@sveltejs/vite-plugin-svelte";

// A relative base lets the same build run at any sub-path (GitHub Pages serves it under
// /Seraph-Horizons/). Routing is in the hash, so a reload on a deep link never reaches
// the server as a path it does not have.
export default defineConfig({
  base: "./",
  plugins: [svelte()],
  build: {
    target: "es2022",
    outDir: "dist",
  },
});
