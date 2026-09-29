// Relative luminance and contrast ratio as WCAG 2.x defines them
// (https://www.w3.org/TR/WCAG22/#dfn-relative-luminance). The end-to-end tests use these
// to check what the browser actually paints behind icons.

export type Rgb = [number, number, number];

/** One sRGB channel, 0-255, to linear light. */
function linear(channel: number): number {
  const c = channel / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

export function relativeLuminance([r, g, b]: Rgb): number {
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

/** Contrast ratio of two luminances, from 1 to 21 whichever order they come in. */
export function contrastRatio(a: number, b: number): number {
  const [hi, lo] = a > b ? [a, b] : [b, a];
  return (hi + 0.05) / (lo + 0.05);
}

/**
 * Reads "#rrggbb", or "rgb(...)"/"rgba(...)" as getComputedStyle returns them. Returns
 * the colour and its alpha, or null for anything else.
 */
export function parseColor(text: string): { rgb: Rgb; alpha: number } | null {
  const s = text.trim().toLowerCase();
  const hex = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/.exec(s);
  if (hex) return { rgb: [parseInt(hex[1]!, 16), parseInt(hex[2]!, 16), parseInt(hex[3]!, 16)], alpha: 1 };
  const fn = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)$/.exec(s);
  if (!fn) return null;
  let alpha = 1;
  if (fn[4] !== undefined) alpha = fn[4].endsWith("%") ? parseFloat(fn[4]) / 100 : parseFloat(fn[4]);
  return { rgb: [Number(fn[1]), Number(fn[2]), Number(fn[3])], alpha };
}
