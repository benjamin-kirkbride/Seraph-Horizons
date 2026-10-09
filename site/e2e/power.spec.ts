// The power page against the real export's power section. Producers are found by their
// parameters rather than by id, so a renamed entry still finds the vanilla metal windmill
// with its full set of sails and Millwright's double windmill. The reference figures are in
// docs/recipe-browser/power.md; the wind averages depend on the simulated histogram, so they
// are only checked to within 10%.
import { readFileSync } from "node:fs";
import type { Page } from "@playwright/test";
import type { PowerData, Producer } from "../src/lib/power-data.ts";
import { V, expect, test } from "./fixtures.ts";

const power = (JSON.parse(readFileSync(process.env.RECIPE_EXPORT!, "utf8")) as { power?: PowerData | null }).power ?? null;

/** The vanilla metal windmill with every sail fitted: speed 1 per wind, capped at 0.6. */
function vanillaWindmill(): Producer {
  const p = power!.producers.find(
    (p) => p.id.startsWith("game:") && p.model.kind === "wind" && /metal/i.test(p.id) && (!p.model.sails || p.model.sails.count === p.model.sails.max),
  );
  expect(p, "the export has the vanilla metal windmill at full sails").toBeTruthy();
  return p!;
}

function doubleWindmill(): Producer {
  const p = power!.producers.find((p) => p.id.startsWith("millwright:") && p.model.kind === "wind" && /double/i.test(`${p.id} ${p.name}`));
  expect(p, "the export has Millwright's double windmill").toBeTruthy();
  return p!;
}

/** An id quoted for an attribute selector. */
const q = (id: string) => id.replace(/["\\]/g, "\\$&");

const num = async (page: Page, sel: string) => Number((await page.locator(sel).textContent())!.replace(/[^0-9.]/g, ""));

async function openPower(page: Page) {
  await page.goto(`./#/${V}`);
  await page.getByRole("link", { name: "Power", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/power$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Mechanical power");
  await expect(page.getByTestId("power-consumers")).toBeVisible();
}

test.beforeAll(() => {
  expect(power, "RECIPE_EXPORT has a power section").not.toBeNull();
});

test("the power page lists producers with the reference figures", async ({ page }) => {
  await openPower(page);
  const vanilla = vanillaWindmill();
  const row = (id: string, col: string) => page.locator(`[data-testid="producers"] tr[data-producer="${q(id)}"] [data-col="${col}"]`);
  // speed 1 per wind capped at 0.6, torque factor 3.125: stall 1.875, peak 3.125 × 0.6² / 4.
  await expect(row(vanilla.id, "free")).toHaveText("0.6");
  await expect(row(vanilla.id, "stall")).toHaveText("1.88");
  await expect(row(vanilla.id, "peak")).toHaveText("0.281");
  const double = doubleWindmill();
  await expect(row(double.id, "stall")).toHaveText("12");
  await expect(row(double.id, "peak")).toHaveText("7.2");
  await expect(page.getByTestId("producers").locator("tbody tr")).toHaveCount(power!.producers.length);
  await expect(page.getByTestId("torque-chart").locator("path.curve")).toHaveCount(power!.producers.length);
  await expect(page.getByTestId("reference-load")).toContainText("0.125");
  // No figure fell back to the exporter's defaults.
  await expect(page.locator(".fallback")).toHaveCount(0);
});

test("wind averages match the simulation, at sea level and 50 blocks up", async ({ page }) => {
  test.skip(!power?.wind, "the export has no wind simulation");
  await openPower(page);
  const avg = (p: Producer) => `[data-testid="wind-averages"] tr[data-producer="${q(p.id)}"] [data-col="avg-peak"]`;
  const vanilla = vanillaWindmill();
  const double = doubleWindmill();
  // About 28.5% of the time at or above 0.6 at sea level.
  await expect(page.getByTestId("wind-stats")).toContainText(/at least 0\.6 for (2[6-9]|3[01])%/);
  expect(await num(page, avg(vanilla))).toBeGreaterThan(0.13 * 0.9);
  expect(await num(page, avg(vanilla))).toBeLessThan(0.13 * 1.1);
  expect(await num(page, avg(double))).toBeGreaterThan(3.41 * 0.9);
  expect(await num(page, avg(double))).toBeLessThan(3.41 * 1.1);

  const slider = page.getByTestId("power-wind").getByRole("slider");
  await slider.fill("50");
  await expect(page.getByTestId("height")).toHaveText("50 blocks");
  await expect(page.getByTestId("wind-histogram")).toContainText("Wind speed at 50 blocks");
  expect(await num(page, avg(vanilla))).toBeGreaterThan(0.17 * 0.9);
  expect(await num(page, avg(vanilla))).toBeLessThan(0.17 * 1.1);
  expect(await num(page, avg(double))).toBeGreaterThan(4.23 * 0.9);
  expect(await num(page, avg(double))).toBeLessThan(4.23 * 1.1);
});

test("the load explorer settles each producer under four helve hammers", async ({ page }) => {
  await openPower(page);
  await page.getByRole("button", { name: "4 helve hammers" }).click();
  await expect(page.getByTestId("total-load")).toHaveText("Total load 0.5");
  // 0.6 − 0.5 / 3.125 at full wind.
  const vanilla = vanillaWindmill();
  await expect(page.locator(`[data-testid="explorer"] tr[data-producer="${q(vanilla.id)}"] [data-col="speed"]`)).toHaveText("0.44");
  await expect(page.getByTestId("explorer-chart")).toBeVisible();
  await page.getByRole("button", { name: "Add a machine" }).click();
  await expect(page.getByTestId("picks").locator("tbody tr")).toHaveCount(2);
  await page.getByRole("button", { name: "Clear" }).click();
  await expect(page.getByTestId("total-load")).toHaveText("Total load 0");
});

test("the torque chart reads out every curve under the pointer and the keyboard", async ({ page }) => {
  await openPower(page);
  const svg = page.getByTestId("torque-chart").locator("svg");
  await svg.focus();
  await page.keyboard.press("ArrowRight");
  await expect(page.getByTestId("torque-chart").getByRole("status")).toContainText("At speed");
  await page.keyboard.press("Escape");
  await expect(page.getByTestId("torque-chart").getByRole("status")).toHaveCount(0);
  // Hiding the wind family takes its curves off the chart.
  const winds = power!.producers.filter((p) => p.family === "wind").length;
  await page.getByTestId("power-producers").getByRole("checkbox", { name: "Wind" }).uncheck();
  await expect(page.getByTestId("torque-chart").locator("path.curve")).toHaveCount(power!.producers.length - winds);
});

test("the power page fits a phone @phone", async ({ page }) => {
  await openPower(page);
  await page.getByRole("button", { name: "4 helve hammers" }).click();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
});
