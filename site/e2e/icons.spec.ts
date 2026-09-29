// What the browser actually paints behind item icons, in both colour schemes. Colours are
// read with getComputedStyle and measured with the WCAG formulas; the thresholds are
// written here by hand, not read from the stylesheet.
import type { Locator, Page } from "@playwright/test";
import { contrastRatio, parseColor, relativeLuminance, type Rgb } from "../src/lib/contrast.ts";
import { ICON_CODE, REAL_ICONS } from "./config.ts";
import { V, card, expect, openItem, search, test } from "./fixtures.ts";

const SCHEMES = ["dark", "light"] as const;

// Half the pack's 26,700 icons have a mean luminance under 0.09 (measured over icons/
// when this test was written). A background of at least 0.4 gives such an icon a contrast
// of 3:1 or more; the old dark theme's slot background was 0.025.
const BACKGROUND_FLOOR = 0.4;
// WCAG 1.4.3, normal-size text.
const TEXT_CONTRAST = 4.5;
// WCAG 1.4.11: graphical objects need 3:1 against what is next to them.
const ICON_CONTRAST = 3;

/** The colour painted behind `el`: its own background or the first ancestor's that shows. */
async function paintedBackground(el: Locator): Promise<Rgb> {
  const layers = await el.evaluate((node) => {
    const out: string[] = [];
    for (let e: Element | null = node; e; e = e.parentElement) out.push(getComputedStyle(e).backgroundColor);
    return out;
  });
  // Composite from the element down until an opaque layer; the page is opaque at the root.
  const stack: { rgb: Rgb; alpha: number }[] = [];
  for (const text of layers) {
    const c = parseColor(text);
    if (!c) throw new Error(`unreadable background colour ${text}`);
    if (c.alpha === 0) continue;
    stack.push(c);
    if (c.alpha === 1) break;
  }
  if (stack.length === 0 || stack.at(-1)!.alpha !== 1) throw new Error(`no opaque background behind the element: ${layers.join(" | ")}`);
  let rgb = stack.pop()!.rgb;
  for (const c of stack.reverse()) rgb = rgb.map((v, i) => c.rgb[i]! * c.alpha + v * (1 - c.alpha)) as Rgb;
  return rgb;
}

async function textColor(el: Locator): Promise<Rgb> {
  const c = parseColor(await el.evaluate((node) => getComputedStyle(node).color));
  expect(c?.alpha).toBe(1);
  return c!.rgb;
}

async function expectLightBehind(el: Locator, what: string) {
  await expect(el).toBeVisible();
  const lum = relativeLuminance(await paintedBackground(el));
  expect(lum, `luminance of the background behind ${what}`).toBeGreaterThan(BACKGROUND_FLOOR);
}

async function expectReadable(el: Locator, what: string) {
  await expect(el).toBeVisible();
  const ratio = contrastRatio(relativeLuminance(await textColor(el)), relativeLuminance(await paintedBackground(el)));
  expect(ratio, `contrast of ${what}`).toBeGreaterThanOrEqual(TEXT_CONTRAST);
}

/** Mean relative luminance of the icon's opaque pixels, drawn from the loaded image. */
async function iconLuminance(img: Locator): Promise<number> {
  await img.scrollIntoViewIfNeeded();
  await expect.poll(() => img.evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0)).toBe(true);
  const px = await img.evaluate((i: HTMLImageElement) => {
    const canvas = document.createElement("canvas");
    canvas.width = i.naturalWidth;
    canvas.height = i.naturalHeight;
    const ctx = canvas.getContext("2d")!;
    ctx.drawImage(i, 0, 0);
    return Array.from(ctx.getImageData(0, 0, canvas.width, canvas.height).data);
  });
  let sum = 0;
  let n = 0;
  for (let k = 0; k < px.length; k += 4) {
    // Mostly transparent pixels are edge antialiasing; the background shows through them.
    if (px[k + 3]! < 128) continue;
    sum += relativeLuminance([px[k]!, px[k + 1]!, px[k + 2]!]);
    n++;
  }
  expect(n, "opaque pixels in the icon").toBeGreaterThan(100);
  return sum / n;
}

async function steamEngineRecipe(page: Page): Promise<Locator> {
  await openItem(page, "yangtransport:steamengine-standard-north");
  return card(page, "madeBy", "recipes/grid/steamengines.json");
}

for (const colorScheme of SCHEMES) {
  test.describe(`${colorScheme} theme`, () => {
    test.beforeEach(async ({ page }) => {
      await page.emulateMedia({ colorScheme });
    });

    test("icons sit on a light background wherever they are shown", async ({ page }) => {
      // Grid slot and recipe output: grid/steamengines.json asks for iron rods.
      const c = await steamEngineRecipe(page);
      await expectLightBehind(c.locator('.cell [data-code="game:rod-iron"] img[data-icon]').first(), "a grid slot icon");
      await expectLightBehind(c.locator('[data-output="0"] img[data-icon]'), "a recipe output icon");
      // Item page header.
      await expectLightBehind(page.locator('header img[data-icon="yangtransport:steamengine-standard-north"]'), "the item page icon");
      // Search results.
      await page.goto(`./#/${V}`);
      const results = await search(page, "iron rod");
      await expectLightBehind(results.and(page.locator('[data-code="game:rod-iron"]')).locator(".icon"), "a search result icon");
      // Item links: copper ingot lists what smelts into it.
      await openItem(page, ICON_CODE);
      await expectLightBehind(page.locator(".item-link .icon").first(), "an item link icon");
    });

    test("text drawn over icons stays readable", async ({ page }) => {
      // Iron rod is used in grid recipes with stack amounts, tools and items without icons.
      await openItem(page, "game:rod-iron");
      const used = page.locator('[data-group="usedIn"]');
      await expectReadable(used.locator(".frame .amount").first(), "an amount badge");
      await expectReadable(used.locator(".frame .toolmark").first(), "a tool mark");
      await expectReadable(used.locator(".frame [data-icon-placeholder]").first(), "a slot's placeholder letters");
      // Placeholders outside slots: the item page header and item links.
      await openItem(page, "game:stick");
      await expectReadable(page.locator('header [data-icon-placeholder="game:stick"]'), "the header placeholder");
      await openItem(page, ICON_CODE);
      await expectReadable(page.locator(".item-link [data-icon-placeholder]").first(), "an item link placeholder");
    });

    test("dark items stand out from their slot @phone", async ({ page }) => {
      const c = await steamEngineRecipe(page);
      for (const code of Object.keys(REAL_ICONS)) {
        const img = c.locator(`[data-code="${code}"] img[data-icon]`).first();
        const icon = await iconLuminance(img);
        const bg = relativeLuminance(await paintedBackground(img));
        const ratio = contrastRatio(icon, bg);
        console.log(`${colorScheme} ${code}: icon ${icon.toFixed(4)}, background ${bg.toFixed(4)}, contrast ${ratio.toFixed(2)}`);
        expect.soft(ratio, `contrast of ${code} against its slot`).toBeGreaterThanOrEqual(ICON_CONTRAST);
      }
    });
  });
}
