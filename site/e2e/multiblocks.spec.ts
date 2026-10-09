// The multiblock viewer (#/<version>/multiblocks). Written to pass with or without WebGL: the
// slice and the materials are plain page controls either way. The blast furnace is smex 0.10.1's
// blocktypes/blastfurnace/door.json: refractory bricks (number 1) and cells left empty (11).
import { V, expect, test } from "./fixtures.ts";

test("the list links each structure, the blast furnace among them", async ({ page }) => {
  await page.goto(`./#/${V}`);
  await page.getByRole("link", { name: "Multiblocks", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/multiblocks$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Multiblocks");
  const list = page.getByTestId("multiblock-list");
  await expect(list.locator('[data-multiblock="smex:smokestack"]')).toBeVisible();
  await list.locator('[data-multiblock="smex:blastfurnacedoor"]').getByRole("link", { name: /Blast Furnace Door/ }).click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/multiblock/smex:blastfurnacedoor$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(/Blast Furnace Door/);
});

test("the materials count the blocks, and a slice counts only what it shows", async ({ page }) => {
  await page.goto(`./#/${V}/multiblock/smex:blastfurnacedoor`);
  await expect(page.getByTestId("multiblock-stage")).toHaveAttribute("data-scene", /^(ready|nowebgl)$/);
  const legend = page.getByTestId("multiblock-legend");
  const bricks = legend.locator("li").filter({ has: page.getByRole("link", { name: "Refractory bricks (Tier 1)" }) });
  const count = bricks.getByTestId("multiblock-count");
  const total = Number((await count.textContent())!.replace("×", ""));
  expect(total).toBeGreaterThan(20);
  await expect(legend.locator('li[data-kind="air"]').first()).toBeVisible();

  // Only the bottom layer.
  const layer = page.locator('[data-input="cut-1"]');
  await layer.fill(await layer.getAttribute("min") as string);
  await page.locator('[data-input="only-1"]').check();
  await expect(page.getByTestId("multiblock-cut-1")).toHaveText(/^1 of \d+ only$/);
  const shown = (await count.textContent())!;
  expect(shown).toMatch(new RegExp(`^\\d+ / ${total}×$`));

  // Picking a kind in the legend presses it; again lets go.
  const button = bricks.getByTestId("multiblock-part");
  await button.click();
  await expect(button).toHaveAttribute("aria-pressed", "true");
  await button.click();
  await expect(button).toHaveAttribute("aria-pressed", "false");

  await page.getByRole("button", { name: "Show everything" }).click();
  await expect(count).toHaveText(`${total}×`);
});

test("the multiblock page fits a phone @phone", async ({ page }) => {
  await page.goto(`./#/${V}/multiblock/smex:blastfurnacedoor`);
  await expect(page.getByTestId("multiblock-stage")).toHaveAttribute("data-scene", /^(ready|nowebgl)$/);
  await expect(page.getByTestId("multiblock-legend")).toBeVisible();
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  await expect(page.getByTestId("multiblock-controls")).toBeVisible();
});

test("an unknown structure says so", async ({ page }) => {
  await page.goto(`./#/${V}/multiblock/smex:nothing`);
  await expect(page.getByText("There is no multiblock “smex:nothing” in this version.")).toBeVisible();
});
