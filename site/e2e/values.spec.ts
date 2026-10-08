// Item values. The test server writes E2E_VALUES over the export's (and gives an export
// without values made-up ones on nine items in ten), so these numbers hold for any export.
// It adds E2E_GROUPS to the export's variant groups the same way.
import { V, expect, openItem, search, test } from "./fixtures.ts";

test("an item's header shows its value after the gear icon, or that it has none", async ({ page }) => {
  await openItem(page, "game:ingot-copper");
  const value = page.getByTestId("item-value");
  await expect(value).toHaveText("2.5 rusty gears");
  // The real gear icon, then the number, and no word on screen.
  await expect(value.locator('img[data-icon="game:gear-rusty"]')).toBeVisible();
  await expect(value.locator(".n")).toHaveText("2.5");

  await openItem(page, "game:stick");
  await expect(page.getByTestId("item-value").locator("[data-floor-zero]")).toHaveAttribute("title", /worthless/);

  await openItem(page, "game:rot");
  await expect(page.getByTestId("item-value")).toHaveText("No trade value");
});

test("a liquid's value is per litre wherever it is shown", async ({ page }) => {
  await openItem(page, "game:ciderportion-apple");
  const value = page.getByTestId("item-value").locator("[data-per-litre]");
  await expect(value.locator(".n")).toHaveText("4");
  await expect(value.locator(".unit")).toHaveText("/ L");
  await expect(value.locator(".visually-hidden")).toHaveText("rusty gears per litre");
  await expect(value).toHaveAttribute("title", /per litre/);
  // Copper is per item.
  await openItem(page, "game:ingot-copper");
  await expect(page.getByTestId("item-value").locator("[data-per-litre]")).toHaveCount(0);

  await page.goto(`./#/${V}`);
  await search(page, "apple cider");
  const row = page.getByTestId("results").locator("li", { has: page.locator('a[data-code="game:ciderportion-apple"]') });
  await expect(row.getByTestId("result-value").locator(".unit")).toHaveText("/ L");

  await page.goto(`./#/${V}/values`);
  await expect(page.getByText(/Liquids are priced per litre/)).toBeVisible();
  await page.getByTestId("values-filter").fill("ciderportion-apple");
  await expect(page.getByTestId("values").locator('tr[data-code="game:ciderportion-apple"] [data-per-litre] .unit')).toHaveText("/ L");
});

test("search results carry their value and sort by it", async ({ page }) => {
  await page.goto(`./#/${V}`);
  const results = await search(page, "copper ingot");
  await expect(results.first()).toHaveAttribute("data-code", "game:ingot-copper");
  // Hundreds of items match "copper", so sorting by value reorders a full page.
  const row = page.getByTestId("results").locator("li", { has: page.locator('a[data-code="game:ingot-copper"]') });
  await expect(row.getByTestId("result-value")).toHaveText("2.5 rusty gears");
  await search(page, "copper");
  await expect(page.getByRole("status")).toHaveText(/results for “copper”$/);

  const values = async () =>
    (await page.getByTestId("results").locator("[data-value]").evaluateAll((els) => els.map((e) => Number(e.getAttribute("data-value")))));
  for (const [sort, cmp] of [
    ["value-desc", (a: number, b: number) => b - a],
    ["value-asc", (a: number, b: number) => a - b],
  ] as const) {
    await page.getByTestId("search-sort").selectOption(sort);
    await expect(page).toHaveURL(new RegExp(`sort=${sort}$`));
    await expect(page.getByTestId("search-sort")).toHaveValue(sort);
    // The results re-render when the sorted list arrives.
    await expect.poll(async () => {
      const got = await values();
      return got.length > 5 && got.every((v, i) => i === 0 || cmp(got[i - 1]!, v) <= 0);
    }).toBe(true);
  }
  // A new query keeps the order.
  await page.getByLabel("Search items").fill("gear");
  await expect(page).toHaveURL(/q=gear&sort=value-asc$/);
});

test("the values page lists every valued item, sorts on each column and filters", async ({ page }) => {
  await page.goto(`./#/${V}`);
  await page.getByRole("banner").getByRole("link", { name: "Values", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/values$`));
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Item values");

  const table = page.getByTestId("values");
  const rows = table.locator("tbody tr");
  await expect(rows).toHaveCount(100);
  // Grouped variants make fewer rows than items: "1,234 rows, 25,112 items".
  const [rowCount, itemCount] = (await page.getByTestId("values-count").textContent())!.match(/\d[\d,]*/g)!.map((n) => Number(n.replace(/,/g, "")));
  expect(rowCount).toBeGreaterThan(1000);
  expect(itemCount).toBeGreaterThan(rowCount!);

  const column = (n: number) => rows.evaluateAll((trs, n) => trs.map((tr) => tr.children[n]!.textContent!.trim()), n);
  const numbers = async () =>
    table.locator("tbody [data-value]").evaluateAll((els) => els.map((e) => Number(e.getAttribute("data-value"))));

  // Highest value first by default; a click on the header turns it round.
  await expect(table).toHaveAttribute("data-sort", "value-desc");
  let got = await numbers();
  expect(got).toEqual([...got].sort((a, b) => b - a));
  await page.getByRole("button", { name: "Value" }).click();
  await expect(table).toHaveAttribute("data-sort", "value-asc");
  await expect(table.locator("th.value")).toHaveAttribute("aria-sort", "ascending");
  got = await numbers();
  expect(got).toEqual([...got].sort((a, b) => a - b));

  const collate = (list: string[], dir: 1 | -1) => [...list].sort((a, b) => dir * (a.toLowerCase() < b.toLowerCase() ? -1 : a.toLowerCase() > b.toLowerCase() ? 1 : 0));
  await page.getByRole("button", { name: "Item" }).click();
  await expect(table).toHaveAttribute("data-sort", "name-asc");
  const names = await rows.locator("td.name .label").allTextContents();
  expect(names).toEqual(collate(names, 1));
  await page.getByRole("button", { name: "Mod" }).click();
  await page.getByRole("button", { name: "Mod" }).click();
  await expect(table).toHaveAttribute("data-sort", "mod-desc");
  const mods = await column(1);
  expect(mods).toEqual(collate(mods, -1));

  // The pager walks the whole table.
  await page.getByRole("navigation", { name: "Pages (top)" }).getByRole("button", { name: "Next" }).click();
  await expect(table).toHaveAttribute("data-page", "2");

  // The filter matches name, code and mod, and goes back to the first page.
  await page.getByTestId("values-filter").fill("copper ingot");
  await expect(table).toHaveAttribute("data-page", "1");
  const copper = table.locator('tr[data-code="game:ingot-copper"]');
  await expect(copper.locator("td.value")).toHaveText("2.5 rusty gears");
  await copper.locator("a.item").click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/item/game:ingot-copper$`));

  // A block's orientations are one row, under the first code, found by any of them.
  await page.goBack();
  await page.getByTestId("values-filter").fill("chest-south");
  await expect(rows).toHaveCount(1);
  const chest = table.locator('tr[data-code="game:chest-east"]');
  await expect(chest.locator("td.name .label")).toHaveText("Wooden chest");
  await expect(chest.getByTestId("values-variants")).toHaveText(/^4 variants/);
  await expect(chest.getByTestId("values-variants")).toHaveAttribute("title", /game:chest-west$/);
  await expect(copper).toHaveCount(0);
});

test("the values page can list the items without a value, last whichever way it sorts", async ({ page }) => {
  await page.goto(`./#/${V}/values`);
  // The number of rows, the first of the count line's two numbers.
  const count = async () => Number((await page.getByTestId("values-count").textContent())!.match(/\d[\d,]*/)![0].replace(/,/g, ""));
  await expect(page.getByTestId("values").locator("tbody tr")).toHaveCount(100);
  const valued = await count();
  await page.getByLabel("Include items with no value").check();
  await expect.poll(count).toBeGreaterThan(valued);
  const pages = Math.ceil((await count()) / 100);
  // Highest first by default; one click on the header gives lowest first, another the default again.
  for (const sort of ["value-asc", "value-desc"]) {
    await page.getByRole("button", { name: "Value" }).click();
    await expect(page.getByTestId("values")).toHaveAttribute("data-sort", sort);
    const nav = page.getByRole("navigation", { name: "Pages (top)" });
    await nav.getByRole("button", { name: String(pages), exact: true }).click();
    await expect(page.getByTestId("values")).toHaveAttribute("data-page", String(pages));
    const cells = await page.getByTestId("values").locator("tbody td.value").allTextContents();
    expect(cells.at(-1)!.trim()).toBe("–");
    await nav.getByRole("button", { name: "1", exact: true }).click();
  }
});

test("the values page shows a group's variants that share a price as one row, which opens to list them", async ({ page }) => {
  await page.goto(`./#/${V}/values`);
  const table = page.getByTestId("values");
  await page.getByTestId("values-filter").fill("E2E ingots");
  // Tin, zinc and bismuth share 4 gears; lead, at 6, is a row of its own and does not carry the title.
  const group = table.locator('tr[data-group="E2E ingots"]');
  await expect(table.locator("tbody tr")).toHaveCount(1);
  await expect(page.getByTestId("values-count")).toHaveText("1 row, 3 items");
  await expect(group).toHaveAttribute("data-items", "3");
  await expect(group.locator("td.name .label")).toHaveText("E2E ingots");
  await expect(group.locator("td.value")).toHaveText("4 rusty gears");
  // All four ingots come from one mod (survival, in the vanilla game), named once.
  await expect(group.locator("td.mod [data-mod]")).toHaveCount(1);
  // The best-ranked member's icon (here its placeholder: the server has no icon for it) stands for the row.
  await expect(group.locator('td.name [data-icon-placeholder="game:ingot-tin"]')).toBeVisible();

  const toggle = group.getByTestId("values-variants");
  await expect(toggle).toHaveText(/^3 of 4 variants/);
  await expect(toggle).toHaveAttribute("aria-expanded", "false");
  await expect(group.getByTestId("values-group-members")).toHaveCount(0);
  await toggle.click();
  await expect(toggle).toHaveAttribute("aria-expanded", "true");
  const members = group.getByTestId("values-group-members").locator("li");
  // In rank order, each with its name and code.
  await expect(members).toHaveCount(3);
  expect(await members.evaluateAll((lis) => lis.map((li) => li.getAttribute("data-code")))).toEqual(["game:ingot-tin", "game:ingot-zinc", "game:ingot-bismuth"]);
  await expect(members.first().locator("code")).toHaveText("game:ingot-tin");

  // A member's name or code finds its group's row; lead's own row is found the same way.
  await page.getByTestId("values-filter").fill("ingot zinc");
  await expect(table.locator("tbody tr")).toHaveCount(1);
  await expect(group).toBeVisible();
  await page.getByTestId("values-filter").fill("game:ingot-lead");
  await expect(table.locator('tr[data-code="game:ingot-lead"] td.value')).toHaveText("6 rusty gears");

  // A group whose variants all share a price says so without "of".
  await page.getByTestId("values-filter").fill("E2E planks");
  await expect(table.locator('tr[data-group="E2E planks"]').getByTestId("values-variants")).toHaveText(/^3 variants/);

  // The members link to their pages.
  await page.getByTestId("values-filter").fill("E2E ingots");
  await members.nth(1).locator("a.item").click();
  await expect(page).toHaveURL(new RegExp(`#/${V}/item/game:ingot-zinc$`));
});

test("the values page filters by kind, worthless and handbook, and the address keeps it", async ({ page }) => {
  await page.goto(`./#/${V}/values`);
  const table = page.getByTestId("values");
  const rows = table.locator("tbody tr");
  await expect(rows).toHaveCount(100);

  // Liquids are the per-litre rows; the radio group works from the keyboard as well.
  await page.getByTestId("values-kind").getByLabel("Liquids").check();
  await expect(page).toHaveURL(/values\?kind=liquids$/);
  await page.getByTestId("values-filter").fill("ciderportion-apple");
  await expect(table.locator('tr[data-code="game:ciderportion-apple"] [data-per-litre]')).toHaveCount(1);
  await page.getByTestId("values-filter").fill("");
  const perLitre = await rows.locator("td.value [data-value]").evaluateAll((els) => els.map((e) => e.hasAttribute("data-per-litre")));
  expect(perLitre.length).toBeGreaterThan(0);
  expect(perLitre.every(Boolean)).toBe(true);

  await page.getByTestId("values-kind").getByLabel("Blocks").focus();
  await page.keyboard.press("ArrowRight");
  await expect(page).toHaveURL(/kind=liquids/);
  await page.keyboard.press("ArrowLeft");
  await expect(page).toHaveURL(/kind=blocks/);
  await expect(table.locator("tbody [data-per-litre]")).toHaveCount(0);

  // Worthless rows alone: every value dimmed. Copper is an item, not a block.
  await page.getByTestId("values-kind").getByLabel("Items").check();
  await page.getByTestId("values-worthless").selectOption("only");
  await expect(page).toHaveURL(/kind=items&worthless=only$/);
  const floor = await rows.locator("td.value [data-value]").evaluateAll((els) => els.map((e) => e.hasAttribute("data-floor-zero")));
  expect(floor.length).toBeGreaterThan(0);
  expect(floor.every(Boolean)).toBe(true);
  await page.getByTestId("values-worthless").selectOption("hide");
  await expect(table.locator('tr[data-code="game:stick"]')).toHaveCount(0);
  await page.getByTestId("values-filter").fill("stick");
  await expect(table.locator('tr[data-code="game:stick"]')).toHaveCount(0);

  // A reload keeps every filter.
  await page.reload();
  await expect(page.getByTestId("values-filter")).toHaveValue("stick");
  await expect(page.getByTestId("values-kind").getByLabel("Items")).toBeChecked();
  await expect(page.getByTestId("values-worthless")).toHaveValue("hide");

  // Not in handbook, alone.
  await page.getByTestId("values-filter").fill("");
  await page.getByTestId("values-worthless").selectOption("any");
  await page.getByTestId("values-kind").getByLabel("All").check();
  await page.getByTestId("values-unlisted").selectOption("only");
  await expect(page).toHaveURL(/values\?unlisted=only$/);
  await expect(rows.first()).toBeVisible();
});
