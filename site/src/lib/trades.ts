// Trades of the pack's own traders (`extra.traderType`, docs/recipe-browser/item-data.md):
// a price range, the standing that unlocks a good and where the trader carries it. Pure, so
// the item and entity pages share it and it is tested without a DOM.
import type { PackTradeExtra } from "./export.ts";
import { formatNumber, formatQuantity } from "./recipe-view.ts";
import { t } from "./strings.ts";

interface Traded {
  price?: number;
  extra?: Record<string, unknown>;
}

/** The pack trader extras of a trade, or undefined for any other source (a vanilla trader's). */
export function packTrade(s: Traded): PackTradeExtra | undefined {
  const e = s.extra;
  if (!e || typeof e.traderType !== "string" || typeof e.priceMin !== "number" || typeof e.priceMax !== "number") return undefined;
  return e as unknown as PackTradeExtra;
}

/** "12–18" for a pack trade's range, "30" when it has one price; a vanilla trade's "4 ± 1". */
export function tradePrice(s: Traded): string {
  const p = packTrade(s);
  if (p) return p.priceMin === p.priceMax ? formatNumber(p.priceMin) : `${formatNumber(p.priceMin)}–${formatNumber(p.priceMax)}`;
  if (s.price === undefined) return "";
  return formatQuantity({ avg: s.price, var: typeof s.extra?.priceVar === "number" ? s.extra.priceVar : undefined });
}

/** The same with its unit: "12–18 rusty gears". */
export function tradePriceText(s: Traded): string {
  const p = packTrade(s);
  if (p) return t.priceRange(p.priceMin, p.priceMax);
  return s.price !== undefined ? t.price(s.price) : "";
}

/** What limits a pack trade: a catalog, the standing it takes, the region, rotating or rare stock. */
export function tradeNotes(s: Traded): string[] {
  const p = packTrade(s);
  if (!p) return [];
  const notes: string[] = [];
  if (p.catalog) notes.push(t.tradeNotes.catalog(p.catalog));
  if (p.standingTierName) notes.push(t.tradeNotes.standing(p.standingTierName));
  if (p.region) notes.push(t.tradeNotes.region(p.region));
  if (p.list === "rotating") notes.push(t.tradeNotes.rotating);
  if (p.rare) notes.push(t.tradeNotes.rare);
  if (p.playerSupplied) notes.push(t.tradeNotes.playerSupplied);
  return notes;
}

/** Whether any of the rows is a pack trade, for the note that says why its prices vary. */
export function anyPackTrade(rows: readonly Traded[]): boolean {
  return rows.some((s) => packTrade(s) !== undefined);
}
