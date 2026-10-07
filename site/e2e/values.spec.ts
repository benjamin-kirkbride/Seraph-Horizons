// Item values. The test server writes E2E_VALUES over the export's (and gives an export
// without values made-up ones on nine items in ten), so these numbers hold for any export.
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
  const count = Number((await page.getByTestId("values-count").textContent())!.replace(/\D/g, ""));
  expect(count).toBeGreaterThan(1000);

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
  await expect(chest.getByTestId("values-variants")).toHaveText("4 variants");
  await expect(chest.getByTestId("values-variants")).toHaveAttribute("title", /game:chest-west$/);
  await expect(copper).toHaveCount(0);
});

test("the values page can list the items without a value, last whichever way it sorts", async ({ page }) => {
  await page.goto(`./#/${V}/values`);
  const count = async () => Number((await page.getByTestId("values-count").textContent())!.replace(/\D/g, ""));
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
