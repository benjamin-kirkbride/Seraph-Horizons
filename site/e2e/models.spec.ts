// The model viewer (#/models). Written to pass whether or not the browser has WebGL: the
// controls and the element list work without it, and the stage then says why it is empty.
import { readFileSync } from "node:fs";
import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures.ts";

const read = (path: string) => JSON.parse(readFileSync(new URL(`../../${path}`, import.meta.url), "utf8")) as unknown;
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as {
  models: { id: string; title: string; shape: string; rig?: string }[];
};
const mill = manifest.models.find((m) => m.id === "bucking-sawmill")!;
const rig = read(mill.rig!) as { parts: { id: string; requires?: string | null }[] };
const requires = [...new Set(rig.parts.map((p) => p.requires).filter((r): r is string => typeof r === "string"))];

async function openMill(page: Page) {
  await page.goto("./#/models/bucking-sawmill");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Bucking sawmill");
  // The stage settles one way or the other: drawn, or saying WebGL is missing.
  await expect(page.getByTestId("model-stage")).toHaveAttribute("data-scene", /^(ready|nowebgl)$/);
}

test("the models index lists every model and links to it from the header", async ({ page }) => {
  await page.goto("./");
  await page.getByRole("link", { name: "Models", exact: true }).click();
  await expect(page).toHaveURL(/#\/models$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Models");
  await expect(page.getByTestId("model-list").locator("li")).toHaveCount(manifest.models.length);
  await page.locator('[data-model="bucking-sawmill"]').getByRole("link", { name: "Bucking sawmill" }).click();
  await expect(page).toHaveURL(/#\/models\/bucking-sawmill$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Bucking sawmill");
});

test("the model page shows its credit and generates its controls from the rig", async ({ page }) => {
  await openMill(page);
  const credit = page.getByTestId("model-credit");
  await expect(credit).toContainText("Bobrik00");
  await expect(credit).toContainText("permission");
  const controls = page.getByTestId("model-controls");
  for (const input of ["theta", "depth", "lifting", "reverse"]) await expect(controls.locator(`[data-input="${input}"]`)).toHaveCount(1);
  // One checkbox per distinct requires value, all fitted to start with.
  await expect(controls.locator("[data-requires]")).toHaveCount(requires.length);
  for (const r of requires) await expect(controls.locator(`[data-requires="${r}"]`)).toBeChecked();
  // One overlay per anchor the rig declares, besides cells, collision boxes and edges.
  for (const key of ["cells", "collision", "edges", "powerCell", "infeedSide", "outputSide", "output", "trunkBed", "saw.topY", "saw.bottomY"])
    await expect(controls.locator(`[data-overlay="${key}"]`)).toHaveCount(1);
  // The legend lists every part.
  await expect(page.getByTestId("model-legend").locator(":scope > li")).toHaveCount(rig.parts.length);
});

test("picking an element from the list pins its name and part, and Escape clears it", async ({ page }) => {
  await openMill(page);
  const legend = page.getByTestId("model-legend");
  await legend.locator("summary", { hasText: "f1_blade" }).click();
  const first = legend.locator("button[data-element]").first();
  const name = (await first.getAttribute("data-element"))!;
  await first.click();
  const pick = page.getByTestId("model-pick");
  await expect(page.getByTestId("model-pick-name")).toHaveText(name);
  await expect(pick).toContainText("f1_blade");
  await expect(pick.getByRole("button", { name: "Copy name" })).toBeVisible();
  await expect(pick.getByRole("button", { name: "Copy part id" })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByTestId("model-pick-name")).toHaveCount(0);
});

test("with WebGL, clicking the model pins the element under the pointer", async ({ page }) => {
  await openMill(page);
  const stage = page.getByTestId("model-stage");
  test.skip((await stage.getAttribute("data-scene")) !== "ready", "this browser has no WebGL; the list test covers picking");
  const box = (await page.getByTestId("model-canvas").boundingBox())!;
  const name = page.getByTestId("model-pick-name");
  // Somewhere in the middle of the stage is part of the machine.
  for (let fy = 0.4; fy <= 0.8 && (await name.count()) === 0; fy += 0.05)
    for (let fx = 0.3; fx <= 0.7 && (await name.count()) === 0; fx += 0.05) await page.mouse.click(box.x + box.width * fx, box.y + box.height * fy);
  await expect(name).not.toHaveText("");
  // A click on empty space (the stage's top right, below the camera buttons) clears it.
  await page.mouse.click(box.x + box.width - 10, box.y + 60);
  await expect(name).toHaveCount(0);
});

test("taking a part off greys its legend rows, and Play runs the cycle", async ({ page }) => {
  await openMill(page);
  const controls = page.getByTestId("model-controls");
  await controls.locator('[data-requires="blade1"]').uncheck();
  await expect(page.getByTestId("model-legend").locator(":scope > li.off")).toHaveCount(rig.parts.filter((p) => p.requires === "blade1").length);
  const theta = controls.locator('[data-input="theta"]');
  const before = await theta.inputValue();
  await controls.getByRole("button", { name: "Play" }).click();
  await expect(page.getByTestId("model-status")).toHaveText(/^(Dropping onto the trunk|Cutting)$/);
  await expect(theta).not.toHaveValue(before);
  await controls.getByRole("button", { name: "Pause" }).click();
  await expect(page.getByTestId("model-status")).toHaveText(/^Paused: /);
});

test("the model page needs no recipe data", async ({ page, problems }) => {
  problems.allow.push(/Failed to load resource.*404.* @ .*\/data\/versions\.json$/);
  await page.route("**/data/versions.json", (route) => route.fulfill({ status: 404, body: "" }));
  await openMill(page);
  await expect(page.getByTestId("model-controls")).toBeVisible();
});

test("a model whose files do not load says so", async ({ page, problems }) => {
  problems.allow.push(/Failed to load resource.*500.* @ .*\/models\/bucking-sawmill\/shape\.json$/);
  await page.route("**/models/bucking-sawmill/shape.json", (route) => route.fulfill({ status: 500, body: "" }));
  await page.goto("./#/models/bucking-sawmill");
  await expect(page.getByTestId("model-load-failed")).toContainText("Could not load this model's files.");
});

test("an unknown model says so", async ({ page }) => {
  await page.goto("./#/models/no-such-model");
  await expect(page.getByText("There is no model “no-such-model”.")).toBeVisible();
});

test("the model page fits a phone @phone", async ({ page }) => {
  await openMill(page);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  await expect(page.getByTestId("model-controls")).toBeVisible();
});
