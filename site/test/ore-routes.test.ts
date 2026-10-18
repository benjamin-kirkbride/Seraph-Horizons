import { describe, expect, it } from "vitest";
import type { RecipeExport } from "../src/lib/export.ts";
import { metalOf, oreRoutesData } from "../src/lib/ore-prepare.ts";
import {
  bestRoute,
  chainOf,
  fmtPct,
  fmtUnits,
  glance,
  HAND_STEPS,
  oreChoices,
  oreRoutes,
  placerRoutes,
  stepItem,
  type OreProcessingExport,
  type OreSpec,
  type RouteNode,
} from "../src/lib/ore-routes.ts";
import { formatRoute, parseRoute } from "../src/lib/route.ts";

// The figures of mods-src/seraphhorizons/assets/seraphhorizons/config/ore-processing.json. The expected
// numbers below are worked by hand from Ore/Core/OreRecovery.cs's rules (and agree with the ore routes
// design page the owner reviewed, which used 4 / 7 / 9 / 13 units for most ores).
const DATA: OreProcessingExport = {
  concentrateUnits: 5,
  handStations: { pan: 0.45, rocker: 0.55, amalgampan: 0.45, firepit: 0.85, cupel: 0.85, liquationpan: 0.85 },
  unclassified: 0.85,
  fineUnground: 0.4,
  freeUnamalgamated: 0.7,
  machines: {
    amalgamationplates: { recovery: 0.65 },
    sluice: { recovery: 0.55 },
    jig: { recovery: 0.6 },
    table: { recovery: 0.7 },
    vanner: { recovery: 0.8 },
    stallroaster: { recovery: 0.92 },
    reverberatory: { recovery: 1 },
    cupellationfurnace: { recovery: 0.95 },
    parkeskettle: { recovery: 1 },
    liquationfurnace: { recovery: 0.95 },
    acidparting: { recovery: 0.95 },
  },
  smeltByGrade: { poor: 0.1, medium: 0.25, rich: 0.4, bountiful: 0.5 },
  smelt: { concentrate: 1, roastedconcentrate: 1, sponge: 1 },
  ores: [],
};

const ore = (o: Partial<OreSpec> & { ore: string }): OreSpec => ({
  name: o.ore,
  metal: "copper",
  class: "oxide",
  density: 1,
  free: false,
  unparted: 1,
  units: [4, 7, 9, 13],
  byProducts: [],
  ...o,
});
const MALACHITE = ore({ ore: "malachite" });
const CHALCOPYRITE = ore({ ore: "chalcopyrite", class: "sulfide" });
const GALENA = ore({ ore: "galena", metal: "lead", class: "sulfide", byProducts: [{ metal: "silver", share: 0.03, partedBy: "cupellation" }] });
const GOLD = ore({ ore: "quartz_nativegold", metal: "gold", class: "native", free: true, unparted: 0.85, units: [2, 4, 5, 8], byProducts: [{ metal: "silver", share: 0.15, partedBy: "acidparting" }] });

/** The node at the end of a path of step names (variants after a colon), walking from the top. */
function at(root: RouteNode, ...path: string[]): RouteNode {
  let n = root;
  for (const p of path) {
    const [step, variant] = p.split(":");
    const next = n.children.find((c) => c.step === step && (variant === undefined ? c.variant === undefined : c.variant === variant));
    if (!next) throw new Error(`no ${p} under ${n.step}: ${n.children.map((c) => c.step + (c.variant ? `:${c.variant}` : "")).join(", ")}`);
    n = next;
  }
  return n;
}

describe("ore routes for an oxide", () => {
  const poor = oreRoutes(DATA, MALACHITE, "poor");
  const medium = oreRoutes(DATA, MALACHITE, "medium");

  it("smelts rock by its grade and grinds only poor ore", () => {
    expect(poor.units).toBeCloseTo(0.4);
    expect(medium.units).toBeCloseTo(1.75);
    expect(at(poor, "spalling", "riddle", "arrastra").units).toBeCloseTo(0.4);
    expect(() => at(medium, "spalling", "riddle", "arrastra")).toThrow();
    expect(() => at(medium, "jawbreaker", "stampbattery", "ballmill")).toThrow();
  });

  it("applies the locked shares to the rocker: unsorted × 0.85, poor unground × 0.4", () => {
    expect(at(poor, "spalling", "riddle", "rocker").units).toBeCloseTo(4 * 0.55 * 0.4);
    expect(at(poor, "spalling", "riddle", "arrastra", "rocker:ground").units).toBeCloseTo(2.2);
    expect(at(medium, "spalling", "rocker:unriddled").units).toBeCloseTo(7 * 0.55 * 0.85);
  });

  it("chains the concentrators on each other's tailings, the vanner recovering the rest on the whole route", () => {
    const line = ["jawbreaker", "stampbattery", "sluice", "jig", "table", "vanner"];
    expect(at(medium, ...line.slice(0, 3)).units).toBeCloseTo(3.85);
    expect(at(medium, ...line.slice(0, 4)).units).toBeCloseTo(5.74);
    expect(at(medium, ...line.slice(0, 5)).units).toBeCloseTo(6.622);
    const vanner = at(medium, ...line);
    expect(vanner.units).toBeCloseTo(7);
    expect(vanner.final).toBe(true);
    // Unscreened: locked × 0.85, and no last step that makes it whole.
    expect(at(medium, "jawbreaker", "line:unscreened").units).toBeCloseTo(7 * 0.85 * (1 - 0.45 * 0.4 * 0.3 * 0.2));
    expect(at(poor, "jawbreaker", "line:unscreened").units).toBeCloseTo(1.3453, 4);
  });

  it("finds the best route, and the best by hand", () => {
    expect(bestRoute(medium).map((n) => n.step)).toEqual(["mined", "jawbreaker", "stampbattery", "sluice", "jig", "table", "vanner"]);
    expect(bestRoute(medium, HAND_STEPS).map((n) => n.step)).toEqual(["mined", "spalling", "riddle", "rocker"]);
  });

  it("caps a dense ore's concentrator share at 1", () => {
    const dense = oreRoutes(DATA, { ...MALACHITE, density: 2 }, "medium");
    expect(at(dense, "jawbreaker", "stampbattery", "sluice").units).toBeCloseTo(7);
  });
});

describe("ore routes for a sulfide", () => {
  const medium = oreRoutes(DATA, CHALCOPYRITE, "medium");

  it("smelts to nothing until roasted", () => {
    expect(medium.units).toBe(0);
    const rocker = at(medium, "spalling", "riddle", "rocker");
    expect(rocker.units).toBe(0);
    expect(rocker.holds).toBeCloseTo(3.85);
    expect(at(medium, "spalling", "riddle", "rocker", "firepit").units).toBeCloseTo(3.2725);
    expect(at(medium, "spalling", "firepit:rock").units).toBeCloseTo(7 * 0.25 * 0.85);
    expect(at(medium, "stallroaster").units).toBeCloseTo(1.61);
  });

  it("makes the reverberatory its last step", () => {
    const vanner = at(medium, "jawbreaker", "stampbattery", "sluice", "jig", "table", "vanner");
    expect(vanner.final).toBeUndefined();
    expect(vanner.holds).toBeCloseTo(6.9244);
    expect(at(vanner, "reverberatory").units).toBeCloseTo(7);
    expect(at(vanner, "reverberatory").final).toBe(true);
    expect(at(vanner, "firepit").units).toBeCloseTo(5.8857, 4);
  });
});

describe("ore routes for an ore with a second metal", () => {
  it("parts lead bullion by cupel, furnace and Parkes kettle, the kettle's line the last step", () => {
    const t = oreRoutes(DATA, GALENA, "medium");
    const cupel = at(t, "spalling", "riddle", "rocker", "firepit", "bullion", "cupel");
    expect(cupel.units).toBeCloseTo(3.2725);
    expect(cupel.byProduct!.units).toBeCloseTo(3.2725 * 0.03 * 0.85);
    expect(at(t, "spalling", "riddle", "rocker", "firepit", "bullion").byProduct!.units).toBe(0);
    const bullion = at(t, "jawbreaker", "stampbattery", "sluice", "jig", "table", "vanner", "reverberatory", "bullion");
    expect(at(bullion, "cupellationfurnace").byProduct!.units).toBeCloseTo(6.9244 * 0.03 * 0.95, 4);
    const rich = at(bullion, "parkeskettle", "cupellationfurnace:richlead");
    expect(rich.units).toBeCloseTo(7);
    expect(rich.byProduct!.units).toBeCloseTo(0.21);
    expect(rich.final).toBe(true);
    // Off the whole route the kettle is not the last step: its own figure, nothing made whole.
    const stall = at(t, "stallroaster", "bullion", "parkeskettle", "cupellationfurnace:richlead");
    expect(stall.units).toBeCloseTo(1.61);
    expect(stall.final).toBeUndefined();
  });

  it("amalgamates free gold, and acid parting makes the whole route whole", () => {
    const t = oreRoutes(DATA, GOLD, "medium");
    expect(t.units).toBeCloseTo(4 * 0.25 * 0.85);
    const plates = at(t, "jawbreaker", "stampbattery", "plates");
    expect(plates.units).toBeCloseTo(4 * 0.65 * 0.85);
    expect(at(plates, "sluice").units).toBeCloseTo((2.6 + 1.4 * 0.55) * 0.85);
    const acid = at(plates, "sluice", "jig", "table", "vanner", "bullion", "acidparting");
    expect(acid.units).toBeCloseTo(4);
    expect(acid.byProduct!.units).toBeCloseTo(0.6);
    expect(acid.final).toBe(true);
    expect(at(t, "jawbreaker", "stampbattery", "line:noplates").units).toBeCloseTo(4 * 0.7 * 0.9892 * 0.85, 4);
    // By hand: the amalgam pan catches 45 % as amalgam, the rocker 55 % of the rest; no acid parting by hand.
    const rest = at(t, "spalling", "riddle", "amalgampan", "rocker:rest");
    expect(rest.units).toBeCloseTo((1.8 + 2.2 * 0.55) * 0.85);
    expect(rest.children).toEqual([]);
  });
});

describe("placer routes", () => {
  const gravel = { feed: "game:richgravel-granite", kind: "gravel" as const, rocks: ["granite"], contents: [{ metal: "tin", units: 2 }, { metal: "gold", units: 1 }], output: "nuggets" };

  it("pans, rocks or washes a block through the four concentrators", () => {
    const t = placerRoutes(DATA, gravel);
    expect(t.units).toBeNull();
    expect(at(t, "pan").units).toBeCloseTo(1.35);
    expect(at(t, "rocker").units).toBeCloseTo(1.65);
    expect(at(t, "sluice", "jig").units).toBeCloseTo(3 * 0.82);
    expect(at(t, "sluice", "jig", "table", "vanner").units).toBe(3);
    expect(() => at(placerRoutes(DATA, { ...gravel, kind: "bogiron" }), "pan")).toThrow();
  });
});

describe("reading the routes", () => {
  it("formats units and shares", () => {
    expect(fmtUnits(3.2725)).toBe("3.27");
    expect(fmtUnits(7)).toBe("7");
    expect(fmtPct(1.75, 7)).toBe("25 %");
    expect(fmtPct(6.9244, 7)).toBe("98.9 %");
    expect(fmtPct(6.9999, 7)).toBe("99.9 %");
    expect(fmtPct(1, 0)).toBe("–");
  });

  it("draws a run of single next steps as one chain", () => {
    const t = oreRoutes(DATA, MALACHITE, "medium");
    const { chain, choices } = chainOf(at(t, "jawbreaker", "stampbattery"));
    expect(chain.map((n) => n.step)).toEqual(["stampbattery", "sluice", "jig", "table", "vanner"]);
    expect(choices).toEqual([]);
    expect(chainOf(t).choices.map((n) => n.step)).toEqual(["spalling", "jawbreaker"]);
  });

  it("orders the picker by group, then name, bog iron last", () => {
    const data = {
      ...DATA,
      ores: [GOLD, GALENA, MALACHITE],
      placers: [
        { feed: "game:stone-bogiron", kind: "bogiron" as const, rocks: [], contents: [{ metal: "iron", units: 14 }], output: "seraphhorizons:concentrate-limonite" },
        { feed: "game:richgravel-granite", kind: "gravel" as const, rocks: ["granite"], contents: [{ metal: "tin", units: 1 }], output: "nuggets" },
      ],
    };
    expect(oreChoices(data, (c) => c.id).map((c) => c.id)).toEqual(["malachite", "galena", "quartz_nativegold", "richgravel-granite", "stone-bogiron"]);
    const g = glance(data, oreChoices(data, (c) => c.id)[1]!, "medium");
    expect(g).toMatchObject({ contained: 7, raw: 0 });
    expect(g.best).toBeCloseTo(7);
    expect(g.byProduct).toBeCloseTo(0.21);
  });

  it("links each step to its machine, and bullion to the ore's own", () => {
    expect(stepItem("sluice")).toBe("seraphhorizons:sluice-north");
    expect(stepItem("bullion", GALENA)).toBe("seraphhorizons:bullion-galena-lead");
    expect(stepItem("spalling")).toBeUndefined();
  });
});

describe("ores.json from an export", () => {
  const smelting = (code: string, input: number, quantity = 1) => ({ smelting: { inputQuantity: input, output: { code, kind: "item", quantity } } });
  const exp = {
    oreProcessing: { ...DATA, ores: [MALACHITE, CHALCOPYRITE, ore({ ore: "rhodochrosite" })] },
    items: {
      "seraphhorizons:concentrate-malachite": { kind: "item", name: "Malachite concentrate", attributes: smelting("game:ingot-copper", 20) },
      "seraphhorizons:concentrate-chalcopyrite": { kind: "item", name: "c", attributes: smelting("seraphhorizons:roastedconcentrate-chalcopyrite", 1) },
      "seraphhorizons:roastedconcentrate-chalcopyrite": { kind: "item", name: "r", attributes: smelting("game:ingot-copper", 20) },
      "game:nugget-nativegold": { kind: "item", name: "n", attributes: smelting("game:ingot-gold", 20) },
      "game:nugget-cassiterite": { kind: "item", name: "n", attributes: smelting("game:ingot-tin", 20) },
      "game:flint": { kind: "item", name: "f", attributes: smelting("game:calcinedflint", 1) },
      "seraphhorizons:concentrate-limonite": { kind: "item", name: "l", attributes: smelting("game:ironbloom", 20) },
    },
    recipes: [
      { type: "sluice", ingredients: [{ code: "game:richgravel-granite" }], outputs: [{ code: "game:nugget-nativegold", quantity: 0.11 }, { code: "game:flint", quantity: 0.5 }], extra: { recovery: 0.55 } },
      { type: "sluice", ingredients: [{ code: "game:richgravel-basalt" }], outputs: [{ code: "game:nugget-nativegold", quantity: 0.11 }, { code: "game:flint", quantity: 0.4 }], extra: { recovery: 0.55 } },
      { type: "sluice", ingredients: [{ code: "game:richgravel-chalk" }], outputs: [{ code: "game:nugget-cassiterite", quantity: 0.11 }], extra: { recovery: 0.55 } },
      { type: "sluice", ingredients: [{ code: "game:stone-bogiron" }], outputs: [{ code: "seraphhorizons:concentrate-limonite", quantity: 1.54 }], extra: { recovery: 0.55 } },
      { type: "sluice", ingredients: [{ code: "seraphhorizons:crushedore-malachite-medium" }], outputs: [{ code: "seraphhorizons:concentrate-malachite", quantity: 0.4675 }], extra: { recovery: 0.55 } },
    ],
  } as unknown as RecipeExport;

  it("keeps the ores that smelt, with their product", () => {
    const d = oreRoutesData(exp)!;
    expect(d.ores.map((o) => [o.ore, o.product])).toEqual([
      ["malachite", "game:ingot-copper"],
      ["chalcopyrite", "game:ingot-copper"],
    ]);
    expect(d.concentrateUnits).toBe(5);
  });

  it("reads the placer feeds off the sluice, gravels that hold the same as one, metal only", () => {
    const d = oreRoutesData(exp)!;
    expect(d.placers.map((p) => [p.feed, p.kind, p.rocks, p.contents, p.output])).toEqual([
      ["game:richgravel-chalk", "gravel", ["chalk"], [{ metal: "tin", units: 1 }], "nuggets"],
      ["game:richgravel-granite", "gravel", ["granite", "basalt"], [{ metal: "gold", units: 1 }], "nuggets"],
      ["game:stone-bogiron", "bogiron", [], [{ metal: "iron", units: 14 }], "seraphhorizons:concentrate-limonite"],
    ]);
  });

  it("gives nothing without the section", () => {
    expect(oreRoutesData({ ...exp, oreProcessing: undefined } as RecipeExport)).toBeNull();
  });

  it("names the metal an item smelts to", () => {
    expect(metalOf("game:ingot-copper")).toBe("copper");
    expect(metalOf("game:ironbloom")).toBe("iron");
  });
});

describe("the ore routes page's address", () => {
  it("keeps the ore and a grade other than medium", () => {
    expect(parseRoute("#/next/ores")).toEqual({ view: "ores", version: "next" });
    expect(parseRoute("#/next/ores?ore=galena&grade=poor")).toEqual({ view: "ores", version: "next", ore: "galena", grade: "poor" });
    expect(parseRoute("#/next/ores?grade=medium")).toEqual({ view: "ores", version: "next" });
    expect(parseRoute("#/next/ores?grade=huge")).toEqual({ view: "ores", version: "next" });
    expect(formatRoute({ view: "ores", version: "next", ore: "richgravel-granite", grade: "rich" })).toBe("#/next/ores?ore=richgravel-granite&grade=rich");
    expect(formatRoute({ view: "ores", version: "next", grade: "medium" })).toBe("#/next/ores");
  });
});
