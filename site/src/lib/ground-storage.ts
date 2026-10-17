// The item page's "Pileable" fact and "Ground storage" lines, from the export's
// attributes.groundStorage and attributes.itemPile (docs/recipe-browser/item-data.md, "Ground storage").
import type { GroundStorage, ItemAttributes, ItemPile } from "./export.ts";
import { formatNumber } from "./recipe-view.ts";
import { t } from "./strings.ts";

/**
 * Whether the item piles on the ground, for every item page: a pile is a `stacking` ground
 * storage or an item pile (coal); a loose heap, the placed layouts and an item that does not go
 * on the ground are not.
 */
export function pileable(attrs: ItemAttributes | undefined): string {
  const capacity = attrs?.groundStorage?.layout === "stacking" ? attrs.groundStorage.capacity : attrs?.itemPile?.capacity;
  return capacity === undefined ? t.pileableNo : t.pileableYes(formatNumber(capacity));
}

/**
 * "Pile: up to 64 per block, 1 per click, 4 per Ctrl click.", then a line for each thing
 * that applies (Ctrl to place, how high the pile goes, the full pile's height).
 */
export function groundStorageLines(g: GroundStorage): string[] {
  const n = formatNumber;
  const kind = t.groundKinds[g.layout];
  const parts = [t.groundCapacity(n(g.capacity))];
  if (g.transfer !== undefined) parts.push(t.groundPerClick(n(g.transfer)));
  if (g.bulkTransfer !== undefined && g.bulkTransfer !== g.transfer) parts.push(t.groundPerBulkClick(n(g.bulkTransfer)));
  const head = kind ?? `${t.groundPlaced} (${t.groundLayouts[g.layout] ?? g.layout})`;
  const lines = [`${head}: ${parts.join(", ")}.`];
  if (g.requiresCtrl) lines.push(t.groundNeedsCtrl);
  if (g.solidTop) lines.push(t.groundSolidTop(g.maxPilesHigh !== undefined ? n(g.maxPilesHigh) : undefined));
  else if (g.layout === "stacking") lines.push(t.groundOneHigh);
  if (g.fullHeight !== undefined) lines.push(t.groundFullHeight(n(g.fullHeight)));
  return lines;
}

/** An item pile (coal): its own pile block, Shift to put it down, the next pile on top when full. */
export function itemPileLines(p: ItemPile): string[] {
  const n = formatNumber;
  const parts = [t.groundCapacity(n(p.capacity)), t.groundPerClick(n(p.transfer))];
  if (p.bulkTransfer !== p.transfer) parts.push(t.groundPerBulkClick(n(p.bulkTransfer)));
  return [`${t.groundKinds.stacking}: ${parts.join(", ")}.`, t.groundSolidTop(undefined)];
}
