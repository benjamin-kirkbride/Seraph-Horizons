import type { MetaMod } from "./format.ts";

/** The base game's own mods have no ModDB page. */
const BASE_GAME = new Set(["game", "survival", "creative"]);

/**
 * Where every mention of a mod links: its ModDB page, or the game's site for the base game.
 * The page is found by the asset id that pack/lock.json records, since a mod's /<alias> on
 * the ModDB is often not its modid. A mod without one (the CI-only exporter, or one only
 * in an old release and since dropped) gets no link: guessing /<modid> can 404 or, worse,
 * open another mod's page.
 */
export function modLink(id: string, mod: Pick<MetaMod, "assetId"> | undefined): string | null {
  if (BASE_GAME.has(id)) return "https://www.vintagestory.at/";
  return mod?.assetId ? `https://mods.vintagestory.at/show/mod/${mod.assetId}` : null;
}

/** The mod's own website, when it gives an http(s) one that is not already its link. */
export function modWebsite(id: string, mod: Pick<MetaMod, "website" | "assetId">): string | null {
  const site = mod.website?.trim();
  return site && /^https?:\/\//i.test(site) && site !== modLink(id, mod) ? site : null;
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
export function creditRows(mods: Record<string, MetaMod>): CreditRow[] {
  return Object.entries(mods)
    .map(([id, m]) => ({
      id,
      name: m.name || id,
      version: m.version,
      authors: m.authors ?? [],
      link: modLink(id, m),
      website: modWebsite(id, m),
    }))
    .sort((a, b) => Number(BASE_GAME.has(b.id)) - Number(BASE_GAME.has(a.id)) || a.name.localeCompare(b.name, "en"));
}
