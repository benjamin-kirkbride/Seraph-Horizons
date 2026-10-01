import { V, expect, openItem, test } from "./fixtures.ts";

test("an item's creature source opens its type's page on that variant, which links back", async ({ page }) => {
  // survival/entities/lore/drifter.json: code "drifter", variant type "normal" (Surface
  // Drifter); harvesting it gives a rusty gear at avg 0.01.
  await openItem(page, "game:gear-rusty");
  await page.locator('a[data-entity-link="game:drifter-normal"]').click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/entity/game:drifter\\?variant=game%3Adrifter-normal$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Drifter");
  await expect(page.getByTestId("entity-code")).toHaveText("game:drifter");
  const picked = page.getByTestId("variants").locator('[aria-current="page"]');
  await expect(picked).toHaveText("Surface Drifter");
  const harvest = page.locator('[data-section="harvest"]');
  await expect(harvest.getByRole("heading")).toHaveText("Harvested from the body");
  const gear = harvest.getByRole("row").filter({ has: page.getByRole("link", { name: "Rusty gear", exact: true }) });
  await expect(gear.getByRole("cell").nth(1)).toHaveText("0.01");
  await gear.getByRole("link", { name: "Rusty gear", exact: true }).click();
  await expect(page.locator('article[data-item="game:gear-rusty"]')).toBeVisible();
});

test("all variants of a type on one page, merged, with each variant a click away", async ({ page }) => {
  // drifter.json: six variant types; rusty gear from 0.01 (normal) to 7 (double-headed).
  await page.goto(`./#/${V}/entity/game:drifter`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Drifter");
  const chips = page.getByTestId("variants").getByRole("link");
  await expect(chips).toHaveCount(7);
  await expect(chips.first()).toHaveAttribute("aria-current", "page");
  const gear = page.locator('[data-section="harvest"]').getByRole("row").filter({ has: page.getByRole("link", { name: "Rusty gear", exact: true }) });
  await expect(gear.getByRole("cell").nth(1)).toHaveText("0.01–7");
  await expect(gear.getByRole("cell").nth(2)).toHaveText("all");
  await chips.filter({ hasText: "Deep Drifter" }).click();
  await expect(page).toHaveURL(/variant=game%3Adrifter-deep$/);
  await expect(page.getByTestId("variant-codes")).toHaveText("game:drifter-deep");
});

test("traders of one kind share a variant, and it lists what they sell per trade", async ({ page }) => {
  // survival/config/tradelists/trader-commodities.json: charcoal is sold 8 at a time. Every
  // survival/entities/humanoid/trader-*.json is code "trader".
  await page.goto(`./#/${V}/entity/game:trader?variant=game%3Atrader-male-commodities-temperate`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Trader");
  await expect(page.getByTestId("variants").locator('[aria-current="page"]')).toHaveText("Commodities trader (cold, desert, temperate)");
  const charcoal = page.locator('[data-section="sells"]').getByRole("row").filter({ has: page.getByRole("link", { name: "Charcoal", exact: true }) });
  await expect(charcoal.getByRole("cell").nth(1)).toHaveText("8");
  await expect(page.locator('[data-section="buys"]')).toBeVisible();
});

test("the list has every type once, and the filter matches variant names", async ({ page }) => {
  await page.goto(`./#/${V}`);
  await page.getByRole("link", { name: "Creatures and traders", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Creatures and traders");
  const creatures = page.getByTestId("creatures");
  await expect(creatures.locator('a[data-code="game:drifter"]')).toBeVisible();
  await expect(creatures.locator('a[data-code="game:drifter-normal"]')).toHaveCount(0);
  await expect(page.getByTestId("traders").locator('a[data-code="game:trader"]')).toBeVisible();

  // survival/entities/animal/mammal/wolf-adult.json is code "wolf".
  await page.getByLabel("Filter").fill("surface drifter");
  await expect(creatures.locator('a[data-code="game:drifter"]')).toBeVisible();
  await expect(creatures.locator('a[data-code="game:wolf"]')).toHaveCount(0);
  await expect(page.getByTestId("traders")).toHaveCount(0);
});

test("an unknown entity says so", async ({ page }) => {
  await page.goto(`./#/${V}/entity/game:no-such-creature`);
  await expect(page.getByText(`game:no-such-creature gives nothing in version ${V}, or does not exist there.`)).toBeVisible();
});
