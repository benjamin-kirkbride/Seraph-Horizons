// What the browser actually paints behind item icons, in both colour schemes. The tile is
// a gradient, so the tests screenshot it with its contents hidden and read the pixels.
// Expected colours and thresholds are written here by hand, not read from the stylesheet.
import type { Locator, Page } from "@playwright/test";
import { contrastRatio, parseColor, relativeLuminance, type Rgb } from "../src/lib/contrast.ts";
import { ICON_CODE, REAL_ICONS } from "./config.ts";
import { V, card, expect, openItem, search, test } from "./fixtures.ts";
import { decodePng, pixelAt, type Pixels } from "./png.ts";

const SCHEMES = ["dark", "light"] as const;

// The game's item slot, sampled by hand from a player's screenshot of an in-game tooltip
// (the "Broken chicken egg" header, a slot about 90 px wide). Near the centre it reads
// #f1d3b4 to #f4d7b7; about 4% in from a corner, #c09f82 to #c6a587.
const GAME_CENTRE: [Rgb, Rgb] = [
  [0xf1, 0xd3, 0xb4],
  [0xf4, 0xd7, 0xb7],
];
const GAME_CORNER: [Rgb, Rgb] = [
  [0xc0, 0x9f, 0x82],
  [0xc6, 0xa5, 0x87],
];
// Per channel, beyond the ranges above. The screenshot is small and lossy (flat areas vary
// by 1-3 levels), its brightest point is #fbdebe, 7 levels above the centre samples, and a
// 24 px tile can only be sampled at whole pixels, which moves the corner sample by up to 2%
// of the tile (about 4 levels there). Both old flat tiles miss a range by 39 levels or more.
const TOLERANCE = 10;
// WCAG 1.4.3, normal-size text.
const TEXT_CONTRAST = 4.5;
// WCAG 1.4.11: graphical objects need 3:1 against what is next to them.
const ICON_CONTRAST = 3;

const PROBE_CSS = `
  [data-probe], [data-probe] * { color: transparent !important; text-shadow: none !important; }
  [data-probe="tile"] > * { visibility: hidden !important; }
  img[data-probe="tile"] { object-position: -10000px 0 !important; }
`;

interface Shot {
  /** Size of the element's box, in whole CSS pixels. */
  w: number;
  h: number;
  /** The pixel at (x, y) of the element's box. */
  at(x: number, y: number): Rgb;
}

/** Screenshots `el` in CSS pixels with `mode` applied: "tile" hides everything drawn over its background, "text" only its text. */
async function shoot(el: Locator, mode: "tile" | "text"): Promise<Shot> {
  await el.evaluate(
    (node, [mode, css]) => {
      // Wholly on screen, since the screenshot shows what is painted there.
      node.scrollIntoView({ block: "center" });
      if (!document.getElementById("probe-css")) {
        const style = document.createElement("style");
        style.id = "probe-css";
        style.textContent = css;
        document.head.append(style);
      }
      node.setAttribute("data-probe", mode);
    },
    [mode, PROBE_CSS] as const,
  );
  try {
    const box = (await el.boundingBox())!;
    const img = decodePng(await el.screenshot({ scale: "css", animations: "disabled" }));
    // A box at a fractional position is painted from the nearest whole pixel, while the
    // screenshot starts at the whole pixel below it.
    const ox = Math.round(box.x) - Math.floor(box.x);
    const oy = Math.round(box.y) - Math.floor(box.y);
    const w = Math.min(Math.round(box.width), img.width - ox);
    const h = Math.min(Math.round(box.height), img.height - oy);
    return { w, h, at: (x, y) => rgbOf(img, ox + Math.floor(x), oy + Math.floor(y)) };
  } finally {
    await el.evaluate((node) => node.removeAttribute("data-probe"));
  }
}

const rgbOf = (img: Pixels, x: number, y: number): Rgb => pixelAt(img, x, y).slice(0, 3) as Rgb;
const hex = (c: Rgb) => "#" + c.map((v) => v.toString(16).padStart(2, "0")).join("");

function expectNear(c: Rgb, [lo, hi]: [Rgb, Rgb], what: string) {
  const off = c.map((v, i) => Math.max(lo[i]! - v, v - hi[i]!, 0));
  expect(Math.max(...off), `${what} is ${hex(c)}, expected ${hex(lo)} to ${hex(hi)} within ${TOLERANCE}`).toBeLessThanOrEqual(TOLERANCE);
}

/** Checks the painted tile against the game's slot and returns its pixels. */
async function expectGameSlot(el: Locator, size: number, what: string) {
  await expect(el).toBeVisible();
  const box = await el.boundingBox();
  expect([box?.width, box?.height], `size of ${what}`).toEqual([size, size]);
  const { at } = await shoot(el, "tile");
  const centre = at(size / 2, size / 2);
  expectNear(centre, GAME_CENTRE, `the centre of ${what}`);
  // About 5% in from each corner, and never on the 1 px outline.
  const k = Math.max(1, Math.round(0.05 * size - 0.5));
  const corners = [at(k, k), at(size - 1 - k, size - 1 - k)];
  console.log(`${what}: centre ${hex(centre)}, corners ${corners.map(hex).join(" ")}`);
  for (const c of corners) expectNear(c, GAME_CORNER, `a corner of ${what}`);
  // In the samples above the centre is 0.28 to 0.34 lighter in luminance than the corner;
  // the tile must fall off at least half as much.
  for (const c of corners) {
    expect(relativeLuminance(centre) - relativeLuminance(c), `falloff from centre to corner of ${what}`).toBeGreaterThan(0.17);
  }
}

async function textColor(el: Locator): Promise<Rgb> {
  const c = parseColor(await el.evaluate((node) => getComputedStyle(node).color));
  expect(c?.alpha).toBe(1);
  return c!.rgb;
}

/** Text over the tile must reach 4.5:1 against the darkest and the lightest pixel behind its glyphs. */
async function expectReadable(el: Locator, what: string) {
  await expect(el).toBeVisible();
  const fg = relativeLuminance(await textColor(el));
  const shot = await shoot(el, "text");
  // The box the text itself occupies, relative to the element.
  const box = await el.evaluate((node) => {
    const range = document.createRange();
    range.selectNodeContents(node);
    const t = range.getBoundingClientRect();
    const e = node.getBoundingClientRect();
    return { x: t.left - e.left, y: t.top - e.top, w: t.width, h: t.height, r: parseFloat(getComputedStyle(node).borderTopLeftRadius) || 0 };
  });
  expect(box.w * box.h, `text area of ${what}`).toBeGreaterThan(0);
  // A badge's text box reaches into its rounded corners, where the tile shows through
  // antialiased pixels that no glyph covers.
  const r = Math.ceil(box.r);
  const inCorner = (x: number, y: number) => (x < r || x >= shot.w - r) && (y < r || y >= shot.h - r);
  let lo = Infinity;
  let hi = -Infinity;
  for (let y = Math.max(0, Math.floor(box.y)); y < Math.min(shot.h, Math.ceil(box.y + box.h)); y++) {
    for (let x = Math.max(0, Math.floor(box.x)); x < Math.min(shot.w, Math.ceil(box.x + box.w)); x++) {
      if (inCorner(x, y)) continue;
      const l = relativeLuminance(shot.at(x, y));
      lo = Math.min(lo, l);
      hi = Math.max(hi, l);
    }
  }
  console.log(`${what}: ${contrastRatio(fg, lo).toFixed(2)} on the darkest, ${contrastRatio(fg, hi).toFixed(2)} on the lightest`);
  expect(contrastRatio(fg, lo), `contrast of ${what} on the darkest pixel behind it`).toBeGreaterThanOrEqual(TEXT_CONTRAST);
  expect(contrastRatio(fg, hi), `contrast of ${what} on the lightest pixel behind it`).toBeGreaterThanOrEqual(TEXT_CONTRAST);
}

/** Mean relative luminance of the tile inside its 1 px outline. */
function meanLuminance(shot: Shot): number {
  let sum = 0;
  let n = 0;
  for (let y = 1; y < shot.h - 1; y++) {
    for (let x = 1; x < shot.w - 1; x++) {
      sum += relativeLuminance(shot.at(x, y));
      n++;
    }
  }
  return sum / n;
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

    test("icons sit on the game's item slot wherever they are shown", async ({ page }) => {
      // Recipe slots are 44 px (--slot). grid/steamengines.json asks for iron rods.
      const c = await steamEngineRecipe(page);
      await expectGameSlot(c.locator('.cell [data-code="game:rod-iron"] .frame').first(), 44, "a grid slot");
      await expectGameSlot(c.locator('[data-output="0"] .frame'), 44, "a recipe output");
      await expectGameSlot(page.locator('header img[data-icon="yangtransport:steamengine-standard-north"]'), 64, "the item page icon");
      await page.goto(`./#/${V}`);
      const results = await search(page, "iron rod");
      await expectGameSlot(results.and(page.locator('[data-code="game:rod-iron"]')).locator(".icon"), 32, "a search result icon");
      // Item links: copper ingot lists what smelts into it.
      await openItem(page, ICON_CODE);
      await expectGameSlot(page.locator(".item-link .icon").first(), 24, "an item link icon");
      // A placeholder paints the same tile as an image.
      await openItem(page, "game:stick");
      await expectGameSlot(page.locator('header [data-icon-placeholder="game:stick"]'), 64, "the item page placeholder");
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
        const slot = c.locator(`[data-code="${code}"]`).first();
        const icon = await iconLuminance(slot.locator("img[data-icon]"));
        const bg = meanLuminance(await shoot(slot.locator(".frame"), "tile"));
        const ratio = contrastRatio(icon, bg);
        console.log(`${colorScheme} ${code}: icon ${icon.toFixed(4)}, slot mean ${bg.toFixed(4)}, contrast ${ratio.toFixed(2)}`);
        expect.soft(ratio, `contrast of ${code} against its slot`).toBeGreaterThanOrEqual(ICON_CONTRAST);
      }
    });
  });
}
