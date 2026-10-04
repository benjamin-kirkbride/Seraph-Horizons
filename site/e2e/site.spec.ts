import { readFileSync } from "node:fs";
import { E2E_VERSIONS, ICON_CODE, NO_ICONS_PATH, PORT } from "./config.ts";
import type { Locator } from "@playwright/test";
import { V, card, expect, openItem, search, test } from "./fixtures.ts";

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

test("panning sources show the chance per pan and fold a block's rock variants", async ({ page }) => {
  // survival/blocktypes/wood/pan.json: bony soil pans to bone at chance 0.3 among its other
  // drops; a pan gives at most one, so the chance of bone from one pan is lower.
  await openItem(page, "game:bone");
  const sources = page.locator("table.sources tbody > tr");
  const bony = sources.filter({ has: page.getByRole("link", { name: "Bony soil", exact: true }) });
  await expect(bony.locator("td").first()).toHaveText("Panning");
  await expect(bony.locator(".hint")).toHaveText(/^\d+(\.\d+)?%$/);
  await expect(bony.locator(".hint")).not.toHaveText("30%");

  // The same file pans flint from every rock's gravel at one chance: one row, not one per rock.
  await openItem(page, "game:flint");
  // Folded behind "and N more", so found by address rather than by its (hidden) name.
  const gravel = sources.filter({ has: page.locator('a[href$="/item/game:gravel-granite"]') });
  await expect(gravel).toHaveCount(1);
  await expect(gravel.locator("td").first()).toHaveText("Panning");
  await gravel.getByText(/^and \d+ more$/).click();
  await expect(gravel.getByRole("link", { name: "Granite gravel", exact: true })).toBeVisible();

  // The gravel's own page says the same from its side.
  await openItem(page, "game:gravel-granite");
  const flint = page.locator("table.gives tbody > tr").filter({ has: page.locator('a[href$="/item/game:flint"]') });
  await expect(flint).toHaveCount(1);
  await expect(flint.locator("td").first()).toHaveText("When panned");
  await expect(flint.locator(".hint")).toHaveText(/^\d+(\.\d+)?%$/);
});

// pack/lock.json's asset ids, which prepare-data puts on the mods by default.
const SHOW_MOD = "https://mods.vintagestory.at/show/mod/";
const lock = JSON.parse(readFileSync(new URL("../../pack/lock.json", import.meta.url), "utf8")) as { mods: { id: string; assetId: number }[] };
const ASSET = Object.fromEntries(lock.mods.map((m) => [m.id, m.assetId]));

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
  // A mod from pack/pack.toml, linked to its ModDB page by the asset id in pack/lock.json;
  // its own website, if any, comes second.
  const modLink = (id: string) => page.locator(`li[data-mod="${id}"] a[data-link="mod"]`);
  await expect(modLink("expandedfoods")).toHaveAttribute("href", `${SHOW_MOD}${ASSET.expandedfoods}`);
  const efSite = exp.mods.expandedfoods?.website?.trim();
  await expect(page.locator('li[data-mod="expandedfoods"] a[data-link="website"]')).toHaveCount(efSite && /^https?:/.test(efSite) ? 1 : 0);
  // ModDB aliases that are not the modid: /moreroads is a 404, /scaffolding another mod.
  await expect(modLink("moreroads")).toHaveAttribute("href", `${SHOW_MOD}${ASSET.moreroads}`);
  await expect(modLink("scaffolding")).toHaveAttribute("href", `${SHOW_MOD}${ASSET.scaffolding}`);
  await expect(page.locator('li[data-mod="game"] a')).toHaveAttribute("href", "https://www.vintagestory.at/");
  // The CI-only exporter is in the export but not on the ModDB.
  await expect(page.locator('li[data-mod="seraphexport"]')).toHaveCount(exp.mods.seraphexport ? 1 : 0);
  await expect(page.locator('li[data-mod="seraphexport"] a')).toHaveCount(0);
  for (const a of await page.getByTestId("credits").locator("a").all()) {
    expect(await a.getAttribute("href")).toMatch(/^https:\/\/[^\s]+$/);
  }

  const removal = page.getByTestId("removal-link");
  await expect(removal).toHaveAttribute("href", "https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/new");
  await expect(page.locator('a[href^="mailto:"]')).toHaveCount(0);
  expect(await page.content()).not.toMatch(/[\w.+-]+@[\w-]+\.[a-z]{2,}/i);
});

test("every mod a page names links to its ModDB page, never from inside another link", async ({ page }) => {
  const ef = `${SHOW_MOD}${ASSET.expandedfoods}`;
  const external = async (a: Locator, href: string) => {
    await expect(a).toHaveAttribute("href", href);
    await expect(a).toHaveAttribute("target", "_blank");
    await expect(a).toHaveAttribute("rel", "noopener noreferrer");
  };
  // expandedfoods:recipes/grid/agedmeat.json makes aged red meat.
  await openItem(page, "expandedfoods:agedmeat-redmeat-cut");
  await external(page.getByTestId("item-mod").getByRole("link", { name: "Expanded Foods" }), ef);
  await card(page, "madeBy", "expandedfoods:recipes/grid/agedmeat.json");
  await external(page.getByTestId("recipe-mod").getByRole("link").first(), ef);
  await openItem(page, "game:stick");
  await external(page.getByTestId("item-mod").getByRole("link"), "https://www.vintagestory.at/");

  await page.goto("./");
  await search(page, "aged red meat");
  const row = page.getByTestId("results").locator("li", { has: page.locator('a[data-code="expandedfoods:agedmeat-redmeat-cut"]') });
  await external(row.locator('a[data-mod="expandedfoods"]'), ef);
  await expect(page.getByTestId("results").locator("a a")).toHaveCount(0);

  await page.goto(`./#/${V}/entity/game:wolf`);
  await external(page.getByTestId("entity-mod").getByRole("link"), "https://www.vintagestory.at/");
  await page.getByRole("link", { name: "Creatures and traders", exact: true }).click();
  await external(page.getByTestId("creatures").locator("li", { has: page.locator('a[data-code="game:wolf"]') }).locator("a[data-mod]"), "https://www.vintagestory.at/");
  await expect(page.locator("a a")).toHaveCount(0);
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

test("the theme picker overrides the system theme, survives a reload and keeps the icon tile", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await openItem(page, ICON_CODE);
  const picker = page.getByLabel("Theme");
  await expect(picker).toHaveValue("system");
  const bodyBg = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor);
  const tileBg = () => page.locator(`header img[data-icon="${ICON_CODE}"]`).evaluate((el) => getComputedStyle(el).backgroundColor);
  const tile = await tileBg();
  expect(await bodyBg()).toBe("rgb(246, 244, 239)");

  await picker.selectOption("dark");
  expect(await bodyBg()).toBe("rgb(27, 26, 24)");
  expect(await tileBg()).toBe(tile);
  await page.reload();
  await expect(page.getByLabel("Theme")).toHaveValue("dark");
  expect(await bodyBg()).toBe("rgb(27, 26, 24)");

  // Back to the system theme, which is still light.
  await page.getByLabel("Theme").selectOption("system");
  expect(await bodyBg()).toBe("rgb(246, 244, 239)");
  await page.emulateMedia({ colorScheme: "dark" });
  expect(await bodyBg()).toBe("rgb(27, 26, 24)");
});
