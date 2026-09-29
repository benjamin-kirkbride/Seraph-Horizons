// icons/index.json maps item codes to the sha256 of their PNG; the file itself is
// icons/<first two hex digits>/<hash>.png. See docs/recipe-browser/README.md.

export interface IconIndex {
  schemaVersion: number;
  size: number;
  icons: Record<string, string>;
}

/** Path of the icon for `code`, relative to the site root, or null for a placeholder. */
export function iconPath(index: IconIndex | null | undefined, code: string): string | null {
  const hash = index?.icons?.[code];
  // Anything that is not a plain hex digest would let the index point outside icons/.
  if (typeof hash !== "string" || !/^[0-9a-f]{16,128}$/i.test(hash)) return null;
  const h = hash.toLowerCase();
  return `icons/${h.slice(0, 2)}/${h}.png`;
}

/** Accepts a parsed index.json, or null when it is missing or not in a known shape. */
export function readIconIndex(value: unknown): IconIndex | null {
  if (typeof value !== "object" || value === null) return null;
  const v = value as Partial<IconIndex>;
  if (v.schemaVersion !== 1 || typeof v.icons !== "object" || v.icons === null) return null;
  return { schemaVersion: 1, size: typeof v.size === "number" ? v.size : 64, icons: v.icons };
}
