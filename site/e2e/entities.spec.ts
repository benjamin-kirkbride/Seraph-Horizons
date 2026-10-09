import { V, card, expect, openItem, pause, test } from "./fixtures.ts";

test("an item's creature source opens its type's page on that variant, which links back", async ({ page }) => {
  // survival/entities/lore/drifter.json: code "drifter", variant type "normal" (Surface
  // Drifter); harvesting it gives rusty gears at avg 0.0725: BetterLoot+'s 0.01, plus its 0.25 gear
  // parts at a quarter (seraphhorizons' GearPartsRemoved).
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
  await expect(gear.getByRole("cell").nth(1)).toHaveText("0.07");
  await gear.getByRole("link", { name: "Rusty gear", exact: true }).click();
  await expect(page.locator('article[data-item="game:gear-rusty"]')).toBeVisible();
});

test("all variants of a type on one page, merged, with each variant a click away", async ({ page }) => {
  // drifter.json: six variant types; rusty gear from 0.0725 (normal) to 7 (double-headed).
  await page.goto(`./#/${V}/entity/game:drifter`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Drifter");
  const chips = page.getByTestId("variants").getByRole("link");
  await expect(chips).toHaveCount(7);
  await expect(chips.first()).toHaveAttribute("aria-current", "page");
  const gear = page.locator('[data-section="harvest"]').getByRole("row").filter({ has: page.getByRole("link", { name: "Rusty gear", exact: true }) });
  await expect(gear.getByRole("cell").nth(1)).toHaveText("0.07–7");
  await expect(gear.getByRole("cell").nth(2)).toHaveText("all");
  await chips.filter({ hasText: "Deep Drifter" }).click();
  await expect(page).toHaveURL(/variant=game%3Adrifter-deep$/);
  await expect(page.getByTestId("variant-codes")).toHaveText("game:drifter-deep");
});

test("a trader's page lists what it sells per trade", async ({ page }) => {
  // survival/config/tradelists/trader-treasurehunter.json: metal parts are sold 4 at a time. Every
  // survival/entities/humanoid/trader-*.json is code "trader". The treasure hunter is the only
  // one left: the pack's trader grid replaces the camp traders, whose trades the export marks
  // extra.replaced and the site leaves out.
  await page.goto(`./#/${V}/entity/game:trader?variant=game%3Atrader-male-treasurehunter-temperate`);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Treasure hunter trader");
  // Its six variants (two genders, three climates) trade alike, so there is nothing to pick.
  await expect(page.getByTestId("variants")).toHaveCount(0);
  const parts = page.locator('[data-section="sells"]').getByRole("row").filter({ has: page.getByRole("link", { name: "Metal parts", exact: true }) });
  await expect(parts.getByRole("cell").nth(1)).toHaveText("4");
  await expect(page.locator('[data-section="buys"]')).toBeVisible();
});

test("a trader the pack replaces is not listed", async ({ page }) => {
  // trader-commodities.json sells charcoal; the grid's camps have the pack's traders instead.
  await page.goto(`./#/${V}/entity/game:trader?variant=game%3Atrader-male-commodities-temperate`);
  await expect(page.getByTestId("variants").getByText("Commodities trader", { exact: false })).toHaveCount(0);
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

test("a butchery slot keeps its notes under its name, and its multipliers explain themselves on focus", async ({ page }) => {
  // Decompiled Butchering 1.14.3: tables give butcheringEfficiency 0.8 (primitive), 1
  // (simple) and 1.2 (advanced); EntityBehaviorHarvestable keeps animalWeight in 0.5–1.
  await page.goto(`./#/${V}/entity/game:deer?variant=game%3Adeer-whitetail-adult-male`);
  const c = page.locator('[data-section="butchery"] article').first();
  await pause(c);
  const butcher = c.locator('[data-step="butcher"]');
  // The note sits below the name, to the right of the icon, not on the icon's line or below it.
  const knife = butcher.locator("li[data-input]").filter({ hasText: "loses 8 durability" });
  const icon = (await knife.locator(".frame").boundingBox())!;
  const note = (await knife.getByText("loses 8 durability").boundingBox())!;
  const name = (await knife.locator(".name").boundingBox())!;
  expect(note.x).toBeGreaterThanOrEqual(icon.x + icon.width);
  expect(note.y).toBeGreaterThanOrEqual(name.y + name.height - 1);
  expect(note.y).toBeLessThan(icon.y + icon.height);

  const meat = butcher.locator("li[data-output]").filter({ has: page.locator('[data-code="butchering:primemeat-raw"]') });
  const station = meat.getByRole("button", { name: "times the station's yield" });
  await station.focus();
  const tip = page.getByRole("tooltip").filter({ visible: true });
  await expect(tip).toContainText("Advanced Butchering Table: ×1.2");
  await expect(tip).toContainText("Primitive Butchering Table: ×0.8");
  await expect(tip).not.toContainText("Hook");
  await page.keyboard.press("Escape");
  await expect(tip).toHaveCount(0);
  await meat.getByRole("button", { name: "times the creature's condition" }).focus();
  await expect(page.getByRole("tooltip").filter({ visible: true })).toContainText("from ×0.5 at the lowest to ×1 when well fed");

  // "Optional" heads the bucket on a line of its own.
  const optional = c.locator('[data-step="bleed"] .optional');
  const label = (await optional.getByText("Optional", { exact: true }).boundingBox())!;
  const bucket = (await optional.locator("li").first().boundingBox())!;
  expect(bucket.y).toBeGreaterThanOrEqual(label.y + label.height - 1);
});

test("a creature with butchery shows what the ground and the hook and table give, by weight, without scrolling sideways", async ({ page }) => {
  // butchercreatures/deer.json gives a whitetail adult primemeat-raw 3 ± 1 on the table and
  // nothing in the field; the field gives half the creature's redmeat. Both are food, so
  // the creature's weight (0.5 to 1) scales them.
  for (const width of [360, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`./#/${V}/entity/game:deer?variant=game%3Adeer-whitetail-adult-male`);
    const table = page.locator('[data-section="harvest"]').getByTestId("butchery-yields");
    await expect(table.getByRole("columnheader", { name: "Harvested where it lies" })).toBeVisible();
    await expect(table.getByRole("columnheader", { name: "Hook and table" })).toBeVisible();
    await expect(table.getByRole("columnheader", { name: "Low weight" })).toHaveCount(2);
    const prime = table.locator('tr[data-item="butchering:primemeat-raw"] td');
    await expect(prime.nth(1)).toContainText("none");
    await expect(prime.nth(3)).toHaveText("1.5");
    await expect(prime.nth(4)).toHaveText("3");
    const red = table.locator('tr[data-item="game:redmeat-raw"] td');
    const [low, good, full] = await Promise.all([red.nth(1).innerText(), red.nth(2).innerText(), red.nth(4).innerText()]);
    expect(Number(low) * 2).toBeCloseTo(Number(good));
    expect(Number(good) * 2).toBeCloseTo(Number(full));
    // The carcass in its states is a means, not a product.
    await expect(table.locator('tr[data-item^="butchering:deaddeer"]')).toHaveCount(0);
    await expect(page.locator('[data-section="harvest"]')).toContainText("leaves of the usual harvest (50%)");
    const sizes = await page.evaluate(() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]);
    expect(sizes[0]).toBeLessThanOrEqual(sizes[1]!);
  }
  // All variants: ranges over the creatures, still within a phone's width.
  await page.setViewportSize({ width: 360, height: 900 });
  await page.goto(`./#/${V}/entity/game:deer`);
  await expect(page.getByTestId("butchery-yields").locator('tr[data-item="game:redmeat-raw"] td').nth(4)).toHaveText(/–/);
  const sizes = await page.evaluate(() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]);
  expect(sizes[0]).toBeLessThanOrEqual(sizes[1]!);
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
