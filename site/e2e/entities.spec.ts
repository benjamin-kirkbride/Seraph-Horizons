import { V, card, expect, openItem, pause, test } from "./fixtures.ts";

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

test("a creature's page shows its butchery stage by stage, for the variant picked", async ({ page }) => {
  // butchering/patches/entities/deer.json: a whitetail adult is picked up as
  // deaddeer-male-adultlarge-1-dead; butchering/itemtypes/butchercreatures/deer.json gives that
  // carcass butcheringRewards primemeat-raw (3 ± 1) and a medium workload (2 hours on the hook,
  // knife -8 durability, cleaver -4, from the decompiled mod).
  await page.goto(`./#/${V}/entity/game:deer?variant=game%3Adeer-whitetail-adult-male`);
  const section = page.locator('[data-section="butchery"]');
  await expect(section.getByRole("heading", { level: 2 })).toHaveText("Butchery");
  const c = section.locator('article[data-recipe-id="butchery|game:deer|butchering:deaddeer-male-adultlarge-1-dead"]');
  await expect(c).toHaveCount(1);
  // Other carcasses of the type (fawns, red brockets) are not the whitetail's.
  await expect(section.locator("article")).toHaveCount(1);
  await pause(c);
  await expect(c).toHaveAttribute("data-shape", "butchery");
  await expect(c.locator("[data-entities]")).toContainText("Whitetail deer (male)");
  await expect(c.locator("[data-step]")).toHaveCount(5);
  await expect(c.locator('[data-step="pickUp"] [data-code="butchering:deaddeer-male-adultlarge-1-dead"]')).toBeVisible();
  await expect(c.locator('[data-step="skin"] [data-code="game:hide-raw-large"]')).toBeVisible();
  await expect(c.locator('[data-step="bleed"]')).toContainText("Takes 2 in-game hours");
  await expect(c.locator('[data-step="bleed"] [data-code="butchering:bloodportion"]')).toHaveAttribute("data-amount", "4 L");
  const butcher = c.locator('[data-step="butcher"]');
  await expect(butcher).toContainText("loses 8 durability");
  await expect(butcher).toContainText("loses 4 durability");
  await expect(butcher.locator('li[data-yield="3 ± 1"] [data-code="butchering:primemeat-raw"]')).toBeVisible();
  await expect(c.locator('[data-step="harvest"] [data-multiplier="0.5"]')).toContainText("50%");

  // The creature link on the card leads back to the variant.
  await c.locator('[data-entities] a[data-entity="game:deer-whitetail-adult-male"]').click();
  await expect(page).toHaveURL(/variant=game%3Adeer-whitetail-adult-male$/);
});

test("butchery products and carcasses link to the butchery that makes and uses them", async ({ page }) => {
  await openItem(page, "butchering:primemeat-raw");
  const made = await card(page, "madeBy", "butchery|game:deer|butchering:deaddeer-male-adultlarge-1-dead");
  await expect(made).toHaveAttribute("data-shape", "butchery");
  await openItem(page, "butchering:deaddeer-male-adultlarge-1-bledout");
  await expect(await card(page, "usedIn", "butchery|game:deer|butchering:deaddeer-male-adultlarge-1-dead")).toBeVisible();
  await expect(await card(page, "madeBy", "butchery|game:deer|butchering:deaddeer-male-adultlarge-1-dead")).toBeVisible();
});

test("an unknown entity says so", async ({ page }) => {
  await page.goto(`./#/${V}/entity/game:no-such-creature`);
  await expect(page.getByText(`game:no-such-creature gives nothing in version ${V}, or does not exist there.`)).toBeVisible();
});
