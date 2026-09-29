import type { Mod } from "./export.ts";

/** The base game's own mods have no ModDB page. */
const BASE_GAME = new Set(["game", "survival", "creative"]);

/** Where the credits page links a mod: its own website, else its ModDB page. */
export function modLink(id: string, mod: Pick<Mod, "website">): string {
  const site = mod.website?.trim();
  if (site && /^https?:\/\//i.test(site)) return site;
  if (BASE_GAME.has(id)) return "https://www.vintagestory.at/";
  return `https://mods.vintagestory.at/${encodeURIComponent(id)}`;
}

export interface CreditRow {
  id: string;
  name: string;
  version: string;
  authors: string[];
  link: string;
}

/** Every mod in the data, base game first, then by name. */
export function creditRows(mods: Record<string, Mod>): CreditRow[] {
  return Object.entries(mods)
    .map(([id, m]) => ({ id, name: m.name || id, version: m.version, authors: m.authors ?? [], link: modLink(id, m) }))
    .sort((a, b) => Number(BASE_GAME.has(b.id)) - Number(BASE_GAME.has(a.id)) || a.name.localeCompare(b.name, "en"));
}
