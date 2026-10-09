// The item page's "Ground storage" lines, from the export's attributes.groundStorage
// (docs/recipe-browser/item-data.md, "Ground storage").
import type { GroundStorage } from "./export.ts";
import { formatNumber } from "./recipe-view.ts";
import { t } from "./strings.ts";

/**
 * "Pile: up to 64 per block, 1 per click, 4 per Ctrl click.", then a line for each thing
 * that applies (Ctrl to place, building on top, the full pile's height).
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
  if (g.fullHeight !== undefined) lines.push(t.groundFullHeight(n(g.fullHeight)));
  return lines;
}
