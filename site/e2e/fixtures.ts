import { test as base, expect, type Locator, type Page } from "@playwright/test";
import { E2E_VERSIONS } from "./config.ts";

export const V = E2E_VERSIONS[0]!.id;

export interface Problems {
  /** Console errors the test expects, matched against "<text> @ <url>". */
  allow: RegExp[];
}

// Every test fails on an uncaught page error or a console error it did not expect.
export const test = base.extend<{ problems: Problems }>({
  problems: [
    async ({ page }, use) => {
      const seen: string[] = [];
      page.on("pageerror", (e) => seen.push(`pageerror: ${e.message}`));
      page.on("console", (m) => {
        if (m.type() === "error") seen.push(`console: ${m.text()} @ ${m.location().url}`);
      });
      const problems: Problems = { allow: [] };
      await use(problems);
      expect(seen.filter((s) => !problems.allow.some((re) => re.test(s)))).toEqual([]);
    },
    { auto: true },
  ],
});
export { expect };

export async function openItem(page: Page, code: string) {
  await page.goto(`./#/${V}/item/${code}`);
  await expect(page.locator(`article[data-item="${code}"]`)).toBeVisible();
}

/** Types into the search box and returns the result links. */
export async function search(page: Page, query: string): Promise<Locator> {
  const box = page.getByLabel("Search items");
  await expect(box).toBeEnabled();
  await box.fill(query);
  const results = page.getByTestId("results").locator("a");
  await expect(results.first()).toBeVisible();
  return results;
}

/**
 * The card of a recipe defined in `sourceFile` in the made-by or used-in section,
 * pressing "Show more" until it is loaded.
 */
export async function card(page: Page, group: "madeBy" | "usedIn", sourceFile: string, extra = ""): Promise<Locator> {
  const section = page.locator(`[data-group="${group}"]`);
  const found = section.locator(`article[data-recipe-id*="${sourceFile}"]${extra}`).first();
  // Stop on the site's end-of-list mark, not on a missing button: a page can finish loading
  // between two checks, so seeing neither "Show more" nor "Loading…" proves nothing.
  const incomplete = page.locator(`[data-group="${group}"]:not([data-complete])`);
  await expect(section.first()).toBeVisible();
  for (let i = 0; i < 500; i++) {
    if (await found.isVisible()) return found;
    const more = section.getByRole("button", { name: /^Show \d+ more$/ });
    if ((await more.count()) > 0) await more.first().click();
    else if ((await incomplete.count()) > 0) await page.waitForTimeout(100);
    else break;
  }
  await expect(found).toBeVisible();
  return found;
}

/** Pauses a cycling card so its assertions do not race the clock. */
export async function pause(c: Locator) {
  const btn = c.locator("button[data-pause]");
  if ((await btn.count()) > 0 && (await btn.getAttribute("aria-pressed")) === "false") await btn.click();
}

/** Filled voxel cells of a card as "row,col" strings. */
export async function filledCells(c: Locator): Promise<string[]> {
  return c.locator('.voxels [data-filled="1"]').evaluateAll((els) =>
    els.map((e) => `${e.getAttribute("data-row")},${e.getAttribute("data-col")}`),
  );
}

/** "row,col" of every `#` in a pattern copied from an asset file. */
export function cellsOf(rows: string[]): string[] {
  return rows.flatMap((row, r) => [...row].flatMap((ch, c) => (ch === "#" ? [`${r},${c}`] : [])));
}

/** The item code shown in grid cell (row, col), or null when the cell is empty. */
export async function gridCell(c: Locator, row: number, col: number): Promise<string | null> {
  const cell = c.locator(`.cell[data-row="${row}"][data-col="${col}"]`);
  await expect(cell).toHaveCount(1);
  const slot = cell.locator("[data-code]");
  return (await slot.count()) === 0 ? null : slot.getAttribute("data-code");
}
