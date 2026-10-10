// The tabbed standalone page's hash: which tab it opens (scripts/standalone/tabs-shell.ts). Browser-safe.

/** The tab a page hash ("#rocker", "rocker" or "") opens: its slug, or null when it names no tab. */
export function tabForHash(hash: string, slugs: readonly string[]): string | null {
  let want: string;
  try {
    want = decodeURIComponent(hash.replace(/^#/, ""));
  } catch {
    return null;
  }
  return slugs.includes(want) ? want : null;
}
