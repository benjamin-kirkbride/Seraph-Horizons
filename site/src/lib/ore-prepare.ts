// ores.json (the ore routes page, ore-routes.ts) from an export: its `oreProcessing` section, with
// each ore's smelted product and the placer feeds, which the section does not list, read off the
// sluice's records.
import type { Item, RecipeExport } from "./export.ts";
import type { OreRoutesData, OreSpec, Placer } from "./ore-routes.ts";

type Smelting = { inputQuantity?: number; output?: { code?: string; quantity?: number } };
const smeltingOf = (item: Item | undefined): Smelting | undefined => (item?.attributes as { smelting?: Smelting } | undefined)?.smelting;

/** A smelted metal: an ingot, or a bloom (iron goes to the bloomery). */
const METAL = /^[^:]+:(ingot-[a-z0-9]+|[a-z0-9]+bloom)$/;

/** The metal an item smelts to:`game:ingot-copper` is copper, `game:ironbloom` iron. */
export function metalOf(code: string): string {
  const path = code.slice(code.indexOf(":") + 1);
  return path.replace(/^ingot-/, "").replace(/bloom$/, "");
}

/**
 * The page's data, or null when the export has no ore processing. An ore is kept when something
 * made of it smelts (its concentrate, or a sulfide's roasted concentrate), and gets that smelt's
 * `product`. A placer feed is any sluice ingredient that is not the pack's crushed or ground ore:
 * what a block holds of each metal is the record's output over the sluice's recovery, in the
 * units its output smelts to; rich gravels that hold the same are one feed.
 */
export function oreRoutesData(exp: RecipeExport): OreRoutesData | null {
  const section = exp.oreProcessing;
  if (!section) return null;
  const ores: OreSpec[] = [];
  for (const ore of section.ores) {
    const conc = smeltingOf(exp.items[`seraphhorizons:concentrate-${ore.ore}`]);
    const roasted = smeltingOf(exp.items[`seraphhorizons:roastedconcentrate-${ore.ore}`]);
    const product = (ore.class === "sulfide" ? roasted : conc)?.output?.code;
    if (!product) continue;
    ores.push({ ...ore, product });
  }

  const byContents = new Map<string, Placer>();
  for (const r of exp.recipes) {
    if (r.type !== "sluice" || r.enabled === false) continue;
    const feed = r.ingredients[0]?.code;
    if (!feed || feed.startsWith("seraphhorizons:")) continue;
    const recovery = Number((r.extra as { recovery?: unknown } | undefined)?.recovery);
    if (!(recovery > 0)) continue;
    const contents = new Map<string, number>();
    let output = "";
    for (const o of r.outputs) {
      const s = smeltingOf(exp.items[o.code]);
      // Metal only: flint and quartz wash out too, and smelt to calcined flint and glass.
      if (!s?.output?.code || !s.inputQuantity || !METAL.test(s.output.code)) continue;
      const units = (o.quantity * 100 * (s.output.quantity ?? 1)) / s.inputQuantity / recovery;
      const metal = metalOf(s.output.code);
      contents.set(metal, (contents.get(metal) ?? 0) + units);
      output = output === "" || output === o.code ? o.code : "nuggets";
    }
    if (contents.size === 0) continue;
    const kind = feed.startsWith("game:richgravel-") ? "gravel" : "bogiron";
    const list = [...contents].map(([metal, units]) => ({ metal, units: Math.round(units * 1e4) / 1e4 })).sort((a, b) => b.units - a.units || a.metal.localeCompare(b.metal));
    const key = `${kind}|${JSON.stringify(list)}`;
    const rock = kind === "gravel" ? feed.slice("game:richgravel-".length) : null;
    const known = byContents.get(key);
    if (known) {
      if (rock) known.rocks.push(rock);
      continue;
    }
    byContents.set(key, { feed, kind, rocks: rock ? [rock] : [], contents: list, output: kind === "gravel" ? "nuggets" : output });
  }
  const placers = [...byContents.values()].sort((a, b) => (a.kind === b.kind ? a.rocks.length - b.rocks.length || a.feed.localeCompare(b.feed) : a.kind === "gravel" ? -1 : 1));
  const { ores: _ores, ...figures } = section;
  return { ...figures, ores, placers };
}
