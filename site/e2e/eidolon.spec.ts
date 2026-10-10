// The eidolon's fitting sheet (#/<version>/eidolon) against the real export's eidolon section.
// The figures are the export's own (its builds, computed by the mod's EidolonStats), so the tests
// read them from the file and check that the page shows them. Written to pass with or without WebGL.
import { readFileSync } from "node:fs";
import type { EidolonData } from "../src/lib/eidolon-fittings.ts";
import { V, expect, openItem, test } from "./fixtures.ts";

const eidolon = (JSON.parse(readFileSync(process.env.RECIPE_EXPORT!, "utf8")) as { eidolon?: EidolonData }).eidolon ?? null;

const build = (joints: string, plates: string, skirt: string) => eidolon!.builds.find((b) => b.joints === joints && b.plates === plates && b.skirt === skirt)!;

test.beforeEach(() => {
  test.skip(eidolon === null, "the export has no eidolon section");
});

async function openSheet(page: import("@playwright/test").Page) {
  await page.goto(`./#/${V}/eidolon`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Eidolon Fitting Sheet");
  await expect(page.getByTestId("eidolon-stage")).toHaveAttribute("data-scene", /^(ready|nowebgl)$/);
}

test("the eidolon's item page links to its fitting sheet, which opens all steel", async ({ page }) => {
  await openItem(page, eidolon!.item!);
  await page.getByTestId("eidolon-link").getByRole("link").click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/eidolon$`));
  await expect(page.getByTestId("eidolon-stage")).toHaveAttribute("data-scene", /^(ready|nowebgl)$/);
  const steel = build("steel", "steel", "steel");
  await expect(page.locator('[data-stat="hp"] .v')).toHaveText(String(Math.round(steel.maxHealth)));
  await expect(page.locator('[data-stat="hp"] .d')).toHaveText("same as all steel");
  await expect(page.locator('[data-stat="armour"] .v')).toHaveText(`tier ${steel.armour!.protectionTier}`);
  // Every layer, in build order, with its default picked.
  await expect(page.locator(".fit")).toHaveCount(eidolon!.layers.length);
  for (const l of eidolon!.layers) await expect(page.locator(`.fit[data-layer="${l.id}"] .sw[aria-pressed="true"]`)).toHaveAttribute("data-material", l.default!);
});

test("brass plates change the stats, the effect and the bill", async ({ page }) => {
  await openSheet(page);
  const plates = page.locator('.fit[data-layer="plates"]');
  await plates.getByRole("button", { name: "Brass", exact: true }).first().click();
  const brass = build("steel", "brass", "steel");
  await expect(page.locator('[data-stat="hp"] .v')).toHaveText(String(Math.round(brass.maxHealth)));
  await expect(page.locator('[data-stat="walk"] .v')).toHaveText(brass.walkSpeed.toFixed(4));
  await expect(page.locator('[data-stat="walk"] .d')).toHaveClass(/up/);
  await expect(plates.getByTestId("effect")).toContainText("damage ×");
  const row = page.locator('[data-stage="plates"]');
  await expect(row.locator('.it.chg[data-code="game:metalplate-brass"]')).toBeVisible();
  // The joints' Jonas parts stay in their row whatever the material.
  const joints = eidolon!.layers.find((l) => l.id === "joints")!;
  for (const p of joints.parts) await expect(page.locator(`[data-stage="joints"] .it[data-code="${p.code}"]`)).toHaveCount(1);
});

test("the sash switches between leather and cloth, keeping the colour where it can", async ({ page }) => {
  await openSheet(page);
  const sash = page.locator('.fit[data-layer="sash"]');
  await sash.getByRole("button", { name: "Cloth", exact: true }).click();
  await expect(sash.locator('.sw[aria-pressed="true"]')).toHaveAttribute("data-material", "cloth-plain");
  await expect(page.locator('[data-stage="sash"] .it.chg[data-code="game:cloth-plain"]')).toBeVisible();
});

test("an animation plays on the model", async ({ page }) => {
  await openSheet(page);
  const anim = page.getByTestId("eidolon-animation");
  await anim.locator("select").selectOption("fell");
  await expect(anim.getByRole("button", { name: "Pause" })).toHaveAttribute("aria-pressed", "true");
  await anim.getByRole("button", { name: "Pause" }).click();
  await expect(anim.getByRole("button", { name: "Play" })).toHaveAttribute("aria-pressed", "false");
});

test("the eidolon's model page links to the fitting sheet", async ({ page }) => {
  await page.goto("./#/models/eidolon");
  await page.getByTestId("model-link").getByRole("link").click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/eidolon$`));
  await expect(page.getByTestId("eidolon-fittings")).toBeVisible();
});

test("the fitting sheet fits a phone @phone", async ({ page }) => {
  await openSheet(page);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
  await expect(page.getByTestId("eidolon-bill")).toBeVisible();
});
