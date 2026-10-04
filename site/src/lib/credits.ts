import type { Mod } from "./export.ts";

/** The base game's own mods have no ModDB page. */
const BASE_GAME = new Set(["game", "survival", "creative"]);

/** The CI-only recipe exporter (tools/recipe-export) loads with the pack but is never published. */
const EXPORTER = "seraphexport";

/**
 * Where every mention of a mod links: its ModDB page, the game's site for the base game,
 * or nothing for the exporter. The ModDB serves a mod at its URL alias, which is the modid
 * for most mods but not all; see docs/recipe-browser/site.md.
 */
export function modLink(id: string): string | null {
  if (BASE_GAME.has(id)) return "https://www.vintagestory.at/";
  if (id === EXPORTER) return null;
  return `https://mods.vintagestory.at/${encodeURIComponent(id)}`;
}

/** The mod's own website, when it gives an http(s) one that is not already its link. */
export function modWebsite(id: string, mod: Pick<Mod, "website">): string | null {
  const site = mod.website?.trim();
  return site && /^https?:\/\//i.test(site) && site !== modLink(id) ? site : null;
}

export interface CreditRow {
  id: string;
  name: string;
  version: string;
  authors: string[];
  link: string | null;
  website: string | null;
}

/** Every mod in the data, base game first, then by name. */
export function creditRows(mods: Record<string, Mod>): CreditRow[] {
  return Object.entries(mods)
    .map(([id, m]) => ({
      id,
      name: m.name || id,
      version: m.version,
      authors: m.authors ?? [],
      link: modLink(id),
      website: modWebsite(id, m),
    }))
    .sort((a, b) => Number(BASE_GAME.has(b.id)) - Number(BASE_GAME.has(a.id)) || a.name.localeCompare(b.name, "en"));
}
