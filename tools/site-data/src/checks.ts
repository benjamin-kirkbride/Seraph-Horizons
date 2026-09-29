// What the JSON Schema cannot express: cross-references between the sections of a
// current-version export. Runs only on a document that already passed the schema,
// so the shapes below can be trusted.

import { ptr, type ErrorReport } from "./report.js";

interface Stack {
  code: string;
}
interface Recipe {
  id: string;
  type: string;
  mod: string;
  ingredients: { key?: string }[];
  variants: { ingredients: Stack[][]; outputs: Stack[] }[];
  grid?: { width: number; height: number; pattern: string[] };
}
export interface ExportV1 {
  schemaVersion: number;
  pack: { id: string; version: string; gameVersion: string };
  mods: Record<string, unknown>;
  items: Record<string, { mod: string }>;
  recipes: Recipe[];
  recipeTypes: Record<string, { count: number }>;
}

export function checkCrossReferences(doc: unknown, report: ErrorReport): void {
  const d = doc as ExportV1;
  const has = (o: object, k: string) => Object.hasOwn(o, k);

  for (const [code, item] of Object.entries(d.items)) {
    if (!has(d.mods, item.mod)) {
      report.add("item-mod", `/items/${ptr(code)}/mod`, "a key of mods", JSON.stringify(item.mod));
    }
  }

  const perType = new Map<string, number>();
  const firstIndex = new Map<string, number>();
  let prev: string | undefined;

  d.recipes.forEach((r, i) => {
    const at = `/recipes/${i}`;
    perType.set(r.type, (perType.get(r.type) ?? 0) + 1);
    if (!has(d.recipeTypes, r.type)) {
      report.add("recipe-type", `${at}/type`, "a key of recipeTypes", JSON.stringify(r.type));
    }
    if (!has(d.mods, r.mod)) {
      report.add("recipe-mod", `${at}/mod`, "a key of mods", JSON.stringify(r.mod));
    }

    const seen = firstIndex.get(r.id);
    if (seen !== undefined) {
      report.add("recipe-id-duplicate", `${at}/id`, "a unique id", `${JSON.stringify(r.id)}, also at /recipes/${seen}`);
    } else {
      firstIndex.set(r.id, i);
    }
    // Ordinal order (UTF-16 code units), like C#'s StringComparer.Ordinal. Equal
    // neighbours are duplicates, reported above.
    if (prev !== undefined && r.id < prev) {
      report.add("recipe-id-order", `${at}/id`, `an id sorted after ${JSON.stringify(prev)}`, JSON.stringify(r.id));
    }
    prev = r.id;

    r.variants.forEach((v, j) => {
      const vat = `${at}/variants/${j}`;
      if (v.ingredients.length !== r.ingredients.length) {
        report.add("variant-ingredients", `${vat}/ingredients`,
          `${r.ingredients.length} ingredient lists (one per recipe ingredient)`, String(v.ingredients.length));
      }
      v.ingredients.forEach((slot, k) => {
        slot.forEach((s, l) => {
          if (!has(d.items, s.code)) {
            report.add("variant-code", `${vat}/ingredients/${k}/${l}/code`, "a key of items", JSON.stringify(s.code));
          }
        });
      });
      v.outputs.forEach((s, k) => {
        if (!has(d.items, s.code)) {
          report.add("variant-code", `${vat}/outputs/${k}/code`, "a key of items", JSON.stringify(s.code));
        }
      });
    });

    if (r.grid) checkGrid(r, at, report);
  });

  for (const [type, t] of Object.entries(d.recipeTypes)) {
    const n = perType.get(type) ?? 0;
    if (t.count !== n) {
      report.add("recipe-type-count", `/recipeTypes/${ptr(type)}/count`, `${n} (records of this type)`, String(t.count));
    }
  }
}

function checkGrid(r: Recipe, at: string, report: ErrorReport): void {
  const g = r.grid!;
  const keys = new Set(r.ingredients.flatMap((ing) => (ing.key === undefined ? [] : [ing.key])));
  if (g.pattern.length !== g.height) {
    report.add("grid-height", `${at}/grid/pattern`, `${g.height} rows (height)`, String(g.pattern.length));
  }
  g.pattern.forEach((row, y) => {
    const rowAt = `${at}/grid/pattern/${y}`;
    if (row.length !== g.width) {
      report.add("grid-width", rowAt, `${g.width} characters (width)`, `${row.length} in ${JSON.stringify(row)}`);
    }
    for (const [x, c] of [...row].entries()) {
      if (c !== "_" && !keys.has(c)) {
        report.add("grid-key", rowAt, `"_" or an ingredient key (${[...keys].join("")})`,
          `${JSON.stringify(c)} at column ${x}`);
      }
    }
  });
}
