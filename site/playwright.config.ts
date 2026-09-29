import { defineConfig, devices } from "@playwright/test";
import { PORT, SUB_PATH } from "./e2e/config.ts";

// By decision these tests only run against a real export; there is no fixture to fall
// back on, so a missing export is an error rather than a skip.
if (!process.env.RECIPE_EXPORT) {
  throw new Error(
    "RECIPE_EXPORT is not set. The end-to-end tests run against a real recipe export: " +
      "RECIPE_EXPORT=/path/to/recipes.json npm --prefix site run e2e",
  );
}

export default defineConfig({
  testDir: "e2e",
  outputDir: "e2e/.work/results",
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]] : "list",
  use: {
    baseURL: `http://127.0.0.1:${PORT}${SUB_PATH}`,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    { name: "phone", use: { ...devices["Pixel 7"] }, grep: /@phone/ },
  ],
  webServer: {
    command: "node --import tsx e2e/serve.ts",
    url: `http://127.0.0.1:${PORT}${SUB_PATH}data/versions.json`,
    // Preparing a full export takes a while.
    timeout: 15 * 60_000,
    reuseExistingServer: false,
    stdout: "pipe",
    stderr: "pipe",
  },
});
