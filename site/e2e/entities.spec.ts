import { V, expect, openItem, test } from "./fixtures.ts";

const DRIFTER = "game:drifter-normal";
const TRADER = "game:trader-male-commodities-temperate";

test("an item's creature source links to the creature's page, which links back", async ({ page }) => {
  // survival/entities/lore/drifter.json: harvesting a surface drifter gives a rusty gear
  // at avg 0.01.
  await openItem(page, "game:gear-rusty");
  await page.locator(`a[data-entity-link="${DRIFTER}"]`).click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/entity/${DRIFTER}$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Surface Drifter");
  await expect(page.getByTestId("entity-code")).toHaveText(DRIFTER);
  const harvest = page.locator('[data-section="harvest"]');
  await expect(harvest.getByRole("heading")).toHaveText("Harvested from the body");
  const gear = harvest.getByRole("row").filter({ has: page.getByRole("link", { name: "Rusty gear", exact: true }) });
  await expect(gear.getByRole("cell").nth(1)).toHaveText("0.01");
  await gear.getByRole("link", { name: "Rusty gear", exact: true }).click();
  await expect(page.locator('article[data-item="game:gear-rusty"]')).toBeVisible();
});

test("a trader's page lists what it sells, per trade", async ({ page }) => {
  // survival/config/tradelists/trader-commodities.json: charcoal is sold 8 at a time.
  await page.goto(`./#/${V}/entity/${TRADER}`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Commodities trader (temperate)");
  const sells = page.locator('[data-section="sells"]');
  const charcoal = sells.getByRole("row").filter({ has: page.getByRole("link", { name: "Charcoal", exact: true }) });
  await expect(charcoal.getByRole("cell").nth(1)).toHaveText("8");
  await expect(page.locator('[data-section="buys"]')).toBeVisible();
});

test("the list has every creature and trader, and the filter narrows it", async ({ page }) => {
  await page.goto(`./#/${V}`);
  await page.getByRole("link", { name: "Creatures and traders", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Creatures and traders");
  const creatures = page.getByTestId("creatures");
  await expect(creatures.locator(`a[data-code="${DRIFTER}"]`)).toBeVisible();
  await expect(page.getByTestId("traders").locator(`a[data-code="${TRADER}"]`)).toBeVisible();
  // survival/entities/animal/mammal/wolf-adult.json is harvestable.
  await expect(creatures.locator('a[data-code="game:wolf-eurasian-adult-male"]')).toBeVisible();

  await page.getByLabel("Filter").fill("drifter");
  await expect(creatures.locator(`a[data-code="${DRIFTER}"]`)).toBeVisible();
  await expect(creatures.locator('a[data-code="game:wolf-eurasian-adult-male"]')).toHaveCount(0);
  await expect(page.getByTestId("traders")).toHaveCount(0);
});

test("an unknown entity says so", async ({ page }) => {
  await page.goto(`./#/${V}/entity/game:no-such-creature`);
  await expect(page.getByText(`game:no-such-creature gives nothing in version ${V}, or does not exist there.`)).toBeVisible();
});
