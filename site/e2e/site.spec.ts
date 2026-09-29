import { readFileSync } from "node:fs";
import { E2E_VERSIONS, ICON_CODE, NO_ICONS_PATH, PORT } from "./config.ts";
import { V, expect, openItem, test } from "./fixtures.ts";

test("a deep link survives a reload under the sub-path", async ({ page }) => {
  await page.goto(`./#/${V}/item/game:ingot-tinbronze`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Tin bronze ingot");
  await page.reload();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Tin bronze ingot");
  expect(new URL(page.url()).pathname).toBe("/Seraph-Horizons/");
});

test("the bare address opens the default version", async ({ page }) => {
  await page.goto("./");
  await expect(page).toHaveURL(new RegExp(`#/${V}$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Seraph Horizons recipes");
});

test("switching version keeps the item and puts the version in the address", async ({ page }) => {
  const other = E2E_VERSIONS[1]!;
  await openItem(page, "game:ingot-tinbronze");
  await page.getByLabel("Pack version").selectOption(other.id);
  await expect(page).toHaveURL(new RegExp(`#/${other.id.replace(/\./g, "\\.")}/item/game:ingot-tinbronze$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Tin bronze ingot");
  await expect(page.getByLabel("Pack version")).toHaveValue(other.id);
});

test("an unknown item and an unknown version say so", async ({ page }) => {
  await page.goto(`./#/${V}/item/game:no-such-thing`);
  await expect(page.getByText(`game:no-such-thing is not in version ${V}.`)).toBeVisible();
  await page.goto(`./#/v999/item/game:stick`);
  await expect(page.getByText("There is no version “v999”.")).toBeVisible();
});

test("the unofficial notice, credits and removal contact are present", async ({ page }) => {
  const exp = JSON.parse(readFileSync(process.env.RECIPE_EXPORT!, "utf8")) as { mods: Record<string, { website?: string }> };
  await page.goto("./");
  const notice = page.getByTestId("unofficial-notice");
  await expect(notice).toBeVisible();
  await expect(notice).toContainText("Unofficial");
  await expect(notice).toContainText("Not affiliated with or endorsed by Anego Studios");

  await page.getByRole("link", { name: /^Credits/ }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Credits");
  const listed = page.getByTestId("credits").locator("li");
  await expect(listed).toHaveCount(Object.keys(exp.mods).length);
  // A mod from pack/pack.toml, linked to its own site or its ModDB page.
  const ef = page.locator('[data-mod="expandedfoods"] a');
  const efSite = exp.mods.expandedfoods?.website;
  await expect(ef).toHaveAttribute("href", efSite && /^https?:/.test(efSite) ? efSite : "https://mods.vintagestory.at/expandedfoods");
  for (const a of await page.getByTestId("credits").locator("a").all()) {
    expect(await a.getAttribute("href")).toMatch(/^https:\/\/[^\s]+$/);
  }

  const removal = page.getByTestId("removal-link");
  await expect(removal).toHaveAttribute("href", "https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/new");
  await expect(page.locator('a[href^="mailto:"]')).toHaveCount(0);
  expect(await page.content()).not.toMatch(/[\w.+-]+@[\w-]+\.[a-z]{2,}/i);
});

test("an item with an icon shows it and others get a placeholder", async ({ page }) => {
  await openItem(page, ICON_CODE);
  const img = page.locator(`header img[data-icon="${ICON_CODE}"]`);
  await expect(img).toBeVisible();
  expect(await img.evaluate((el: HTMLImageElement) => el.naturalWidth)).toBe(8);
  expect(await img.getAttribute("src")).toMatch(/^icons\/[0-9a-f]{2}\/[0-9a-f]{64}\.png$/);
  await openItem(page, "game:stick");
  await expect(page.locator('header [data-icon-placeholder="game:stick"]')).toBeVisible();
});

test("without an icon index every item gets a placeholder and nothing else fails", async ({ page, problems }) => {
  // The only expected error is the browser reporting the missing index itself.
  problems.allow.push(/Failed to load resource.*404.* @ .*\/noicons\/icons\/index\.json$/);
  const base = `http://127.0.0.1:${PORT}${NO_ICONS_PATH}`;
  await page.goto(`${base}#/${V}/item/${ICON_CODE}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Copper ingot");
  await expect(page.locator(`header [data-icon-placeholder="${ICON_CODE}"]`)).toBeVisible();
  await expect(page.locator("img[data-icon]")).toHaveCount(0);
  // Recipe slots too.
  await expect(page.locator("[data-group] [data-icon-placeholder]").first()).toBeVisible();
});

test("an item page fits a phone screen @phone", async ({ page }) => {
  await openItem(page, "game:stick");
  await expect(page.locator("[data-group] article").first()).toBeVisible();
  const [scroll, client] = await page.evaluate(() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]);
  expect(scroll).toBeLessThanOrEqual(client);
  await expect(page.getByTestId("unofficial-notice")).toBeVisible();
});
