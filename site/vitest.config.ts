import { defineConfig } from "vitest/config";
import { svelte } from "@sveltejs/vite-plugin-svelte";

export default defineConfig({
  plugins: [svelte()],
  resolve: {
    // Svelte's server build cannot mount components; component tests need the browser one.
    conditions: ["browser"],
  },
  test: {
    include: ["test/**/*.test.ts"],
    environment: "node",
  },
});
