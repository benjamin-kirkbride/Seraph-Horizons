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
  ingredients: { key?: string; role?: string; isTool?: boolean }[];
  outputs: { code: string; extra?: { alternatives?: string[] } }[];
  variants: { ingredients: Stack[][]; outputs: Stack[] }[];
  grid?: { width: number; height: number; pattern: string[] };
  construction?: { stages: { ingredients: number[] }[] };
  butchery?: {
    stages: { ingredients: number[]; options?: number[][]; optional?: number[]; outputs: number[] }[];
    variants: { yields: (object | null)[] }[];
    condition?: { min: number; max: number };
  };
  transition?: { type: string };
  tub?: { lossEveryHours?: number; lossChance?: number; failure?: number };
  lottery?: { outcomes: { chance: number; outputs: number[] }[] };
  machine?: { kept?: number[]; wear?: { ingredient: number }; oil?: { ingredient: number } };
}
export interface ExportV1 {
  schemaVersion: number;
  pack: { id: string; version: string; gameVersion: string };
  mods: Record<string, unknown>;
  items: Record<string, { mod: string; value?: number; floorZero?: true; valuePerLitre?: true }>;
  recipes: Recipe[];
  recipeTypes: Record<string, { count: number }>;
  variantGroups?: Record<string, { title: string; members: string[] }>;
}

export function checkCrossReferences(doc: unknown, report: ErrorReport): void {
  const d = doc as ExportV1;
  const has = (o: object, k: string) => Object.hasOwn(o, k);

  for (const [code, item] of Object.entries(d.items)) {
    if (!has(d.mods, item.mod)) {
      report.add("item-mod", `/items/${ptr(code)}/mod`, "a key of mods", JSON.stringify(item.mod));
    }
    // The pack's item value table: gears per item (per litre with valuePerLitre), never
    // negative or not a number.
    if (item.value !== undefined && !(Number.isFinite(item.value) && item.value >= 0)) {
      report.add("item-value", `/items/${ptr(code)}/value`, "a finite number, 0 or more", String(item.value));
    }
    // valuePerLitre and floorZero qualify a value; without one they mean nothing.
    for (const flag of ["valuePerLitre", "floorZero"] as const) {
      if (item[flag] !== undefined && item.value === undefined) {
        report.add("item-value", `/items/${ptr(code)}/${flag}`, "only with a value", "no value");
      }
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
    if (r.construction) checkConstruction(r, at, report);
    if (r.butchery) checkButchery(r, at, report);
    if (r.transition) checkTransition(r, at, report);
    if (r.tub) checkTub(r, at, report);
    if (r.lottery) checkLottery(r, at, report);
    if (r.machine) checkMachine(r, at, report);
  });

  for (const [type, t] of Object.entries(d.recipeTypes)) {
    const n = perType.get(type) ?? 0;
    if (t.count !== n) {
      report.add("recipe-type-count", `/recipeTypes/${ptr(type)}/count`, `${n} (records of this type)`, String(t.count));
    }
  }

  if (d.variantGroups) checkVariantGroups(d, report);
}

// Tidy Variants groups: the site folds each into one row, so a member must be an item, and an
// item can stand in only one group. The schema already asks for two distinct members and a
// non-empty title; a title of only spaces still passes it.
function checkVariantGroups(d: ExportV1, report: ErrorReport): void {
  const groupOf = new Map<string, string>();
  for (const [id, g] of Object.entries(d.variantGroups!)) {
    const at = `/variantGroups/${ptr(id)}`;
    if (g.title.trim() === "") {
      report.add("variant-group-title", `${at}/title`, "a title that is not blank", JSON.stringify(g.title));
    }
    if (new Set(g.members).size < 2) {
      report.add("variant-group-size", `${at}/members`, "two or more distinct codes", String(new Set(g.members).size));
    }
    g.members.forEach((code, i) => {
      if (!Object.hasOwn(d.items, code)) {
        report.add("variant-group-code", `${at}/members/${i}`, "a key of items", JSON.stringify(code));
      }
      const other = groupOf.get(code);
      if (other !== undefined && other !== id) {
        report.add("variant-group-overlap", `${at}/members/${i}`, "a code in no other group",
          `${JSON.stringify(code)}, also in ${JSON.stringify(other)}`);
      } else {
        groupOf.set(code, id);
      }
    });
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

/** Each ingredient is consumed by exactly one stage. */
function checkConstruction(r: Recipe, at: string, report: ErrorReport): void {
  const stageOf = new Map<number, number>();
  r.construction!.stages.forEach((stage, s) => {
    stage.ingredients.forEach((i, k) => {
      const iat = `${at}/construction/stages/${s}/ingredients/${k}`;
      const first = stageOf.get(i);
      if (i >= r.ingredients.length) {
        report.add("construction-ingredient", iat, `an index below ${r.ingredients.length} (ingredients)`, String(i));
      } else if (first !== undefined) {
        report.add("construction-ingredient", iat, "an ingredient no other stage lists", `${i}, also in stage ${first}`);
      } else {
        stageOf.set(i, s);
      }
    });
  });
  r.ingredients.forEach((_, i) => {
    if (!stageOf.has(i)) {
      report.add("construction-ingredient", `${at}/ingredients/${i}`, "an ingredient some stage lists", "in no stage");
    }
  });
}

/**
 * Each ingredient and each output belongs to exactly one stage, `butchery.variants` is
 * aligned with `variants` and each one's yields with `outputs`, and a variant's output
 * stacks are the outputs it yields (with their alternatives), and the condition's range is
 * not upside down.
 */
function checkButchery(r: Recipe, at: string, report: ErrorReport): void {
  const b = r.butchery!;
  const claim = (kind: "ingredient" | "output", size: number) => {
    const stageOf = new Map<number, number>();
    return {
      add(i: number, s: number, iat: string) {
        const first = stageOf.get(i);
        if (i >= size) {
          report.add("butchery-stage", iat, `an index below ${size} (${kind}s)`, String(i));
        } else if (first !== undefined) {
          report.add("butchery-stage", iat, `an ${kind} no other stage lists`, `${i}, also in stage ${first}`);
        } else {
          stageOf.set(i, s);
        }
      },
      unclaimed(list: string) {
        for (let i = 0; i < size; i++) {
          if (!stageOf.has(i)) report.add("butchery-stage", `${at}/${list}/${i}`, `an ${kind} some stage lists`, "in no stage");
        }
      },
    };
  };
  const ingredients = claim("ingredient", r.ingredients.length);
  const outputs = claim("output", r.outputs.length);
  b.stages.forEach((stage, s) => {
    const sat = `${at}/butchery/stages/${s}`;
    stage.ingredients.forEach((i, k) => ingredients.add(i, s, `${sat}/ingredients/${k}`));
    (stage.options ?? []).forEach((group, g) => group.forEach((i, k) => ingredients.add(i, s, `${sat}/options/${g}/${k}`)));
    (stage.optional ?? []).forEach((i, k) => ingredients.add(i, s, `${sat}/optional/${k}`));
    stage.outputs.forEach((i, k) => outputs.add(i, s, `${sat}/outputs/${k}`));
  });
  ingredients.unclaimed("ingredients");
  outputs.unclaimed("outputs");
  if (b.condition && b.condition.min > b.condition.max) {
    report.add("butchery-condition", `${at}/butchery/condition`, "min at most max", `${b.condition.min} > ${b.condition.max}`);
  }

  if (b.variants.length !== r.variants.length) {
    report.add("butchery-variants", `${at}/butchery/variants`, `${r.variants.length} entries (one per variant)`, String(b.variants.length));
  }
  b.variants.forEach((v, j) => {
    const vat = `${at}/butchery/variants/${j}/yields`;
    if (v.yields.length !== r.outputs.length) {
      report.add("butchery-variants", vat, `${r.outputs.length} yields (one per output)`, String(v.yields.length));
      return;
    }
    const expected = r.outputs.flatMap((o, i) => (v.yields[i] === null ? [] : [o.code, ...(o.extra?.alternatives ?? [])]));
    const found = (r.variants[j]?.outputs ?? []).map((s) => s.code);
    if (JSON.stringify(found) !== JSON.stringify(expected)) {
      report.add("butchery-variants", `${at}/variants/${j}/outputs`, `the outputs it yields, ${JSON.stringify(expected)}`, JSON.stringify(found));
    }
  });
}

/**
 * One stack turns into one other: the first ingredient turns, any further ones are stations
 * it needs (role `station`), and there is one output and one variant.
 */
function checkTransition(r: Recipe, at: string, report: ErrorReport): void {
  if (r.ingredients.length === 0) {
    report.add("transition-shape", `${at}/ingredients`, "1 ingredient (what turns) and any stations", "0");
  } else if (r.ingredients[0]!.role === "station") {
    report.add("transition-shape", `${at}/ingredients/0/role`, "what turns, not a station", JSON.stringify("station"));
  }
  r.ingredients.forEach((ing, i) => {
    if (i > 0 && ing.role !== "station") {
      report.add("transition-shape", `${at}/ingredients/${i}/role`, `"station" (only the first ingredient turns)`, JSON.stringify(ing.role ?? null));
    }
  });
  if (r.outputs.length !== 1) {
    report.add("transition-shape", `${at}/outputs`, "1 output (what it becomes)", String(r.outputs.length));
  }
  if (r.variants.length !== 1) {
    report.add("transition-shape", `${at}/variants`, "1 variant", String(r.variants.length));
  }
}

/**
 * A batch, a liquid and a station: exactly one ingredient of each role, and one output
 * besides the failure, which names an output and is there whenever gears can be lost.
 */
function checkTub(r: Recipe, at: string, report: ErrorReport): void {
  const t = r.tub!;
  for (const role of ["batch", "liquid", "station"]) {
    const n = r.ingredients.filter((i) => i.role === role).length;
    if (n !== 1) report.add("tub-shape", `${at}/ingredients`, `1 ingredient with role ${JSON.stringify(role)}`, String(n));
  }
  if (t.failure !== undefined && t.failure >= r.outputs.length) {
    report.add("tub-failure", `${at}/tub/failure`, `an index below ${r.outputs.length} (outputs)`, String(t.failure));
  }
  const loses = (t.lossEveryHours ?? 0) > 0 || (t.lossChance ?? 0) > 0;
  if (loses && t.failure === undefined) {
    report.add("tub-failure", `${at}/tub`, "a failure output, since gears can be lost", "none");
  }
  const expected = t.failure === undefined ? 1 : 2;
  if (r.outputs.length !== expected) {
    report.add("tub-shape", `${at}/outputs`, `${expected} outputs (what the batch becomes${t.failure === undefined ? "" : ", what a lost gear becomes"})`, String(r.outputs.length));
  }
}

/** One ingredient; the chances add up to 1; each output belongs to exactly one outcome. */
function checkLottery(r: Recipe, at: string, report: ErrorReport): void {
  const l = r.lottery!;
  if (r.ingredients.length !== 1) {
    report.add("lottery-shape", `${at}/ingredients`, "1 ingredient (what is decided)", String(r.ingredients.length));
  }
  const total = l.outcomes.reduce((n, o) => n + o.chance, 0);
  if (Math.abs(total - 1) > 1e-6) {
    report.add("lottery-chance", `${at}/lottery/outcomes`, "chances adding up to 1", String(total));
  }
  const outcomeOf = new Map<number, number>();
  l.outcomes.forEach((o, k) => {
    o.outputs.forEach((i, m) => {
      const iat = `${at}/lottery/outcomes/${k}/outputs/${m}`;
      const first = outcomeOf.get(i);
      if (i >= r.outputs.length) {
        report.add("lottery-output", iat, `an index below ${r.outputs.length} (outputs)`, String(i));
      } else if (first !== undefined) {
        report.add("lottery-output", iat, "an output no other outcome lists", `${i}, also in outcome ${first}`);
      } else {
        outcomeOf.set(i, k);
      }
    });
  });
  r.outputs.forEach((_, i) => {
    if (!outcomeOf.has(i)) report.add("lottery-output", `${at}/outputs/${i}`, "an output some outcome lists", "in no outcome");
  });
}

/**
 * The machine is an ingredient with role station; `kept`, `wear` and `oil` name distinct
 * ingredients; the worn one is a tool and a kept one is not.
 */
function checkMachine(r: Recipe, at: string, report: ErrorReport): void {
  const m = r.machine!;
  if (!r.ingredients.some((i) => i.role === "station")) {
    report.add("machine-shape", `${at}/ingredients`, "an ingredient with role \"station\" (the machine)", "none");
  }
  const named = new Map<number, string>();
  const claim = (i: number, iat: string, what: string) => {
    const first = named.get(i);
    if (i >= r.ingredients.length) {
      report.add("machine-ingredient", iat, `an index below ${r.ingredients.length} (ingredients)`, String(i));
    } else if (first !== undefined) {
      report.add("machine-ingredient", iat, `an ingredient not already the ${first}`, String(i));
    } else {
      named.set(i, what);
    }
  };
  (m.kept ?? []).forEach((i, k) => {
    claim(i, `${at}/machine/kept/${k}`, "kept");
    if (r.ingredients[i]?.isTool) report.add("machine-ingredient", `${at}/ingredients/${i}/isTool`, "a kept part, not a tool that wears", "true");
  });
  if (m.wear) {
    claim(m.wear.ingredient, `${at}/machine/wear/ingredient`, "worn tool");
    const tool = r.ingredients[m.wear.ingredient];
    if (tool && !tool.isTool) report.add("machine-ingredient", `${at}/ingredients/${m.wear.ingredient}`, "a tool (isTool), since it wears", "not a tool");
  }
  if (m.oil) claim(m.oil.ingredient, `${at}/machine/oil/ingredient`, "oil");
}
