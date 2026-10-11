// The ore review page's "Processing line" tab (scripts/standalone/ore-review.json). Per ore class: a
// strip of the steps from mined ore to metal, each with its hand, water and mill device and a "smelt
// now" off-ramp, then a table of what stopping at each step and smelting gives, cumulative, per tier
// and (where it matters) per grade. Figures are computed here from the recovery core's constants
// (CFG, copied from mods-src/seraphhorizons/assets/seraphhorizons/config/ore-processing.json and
// Ore/Core/OreRecovery.cs on main, PR #756), under the three tiers of #765 and the machine levels of
// #711; where an issue and the code differ, the cell says so and the question is listed at the end.
// Plain HTML and CSS on the site's colour tokens (light and dark), no script.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

export type Tier = "hand" | "water" | "mill";
export const TIERS: Tier[] = ["hand", "water", "mill"];
export const TIER_LABEL: Record<Tier, [string, string]> = {
  hand: ["Hand", "stations the player works"],
  water: ["Water", "machines on ppex water, fed by the player"],
  mill: ["Mill", "continuous, chute-fed, unattended"],
};

/** The recovery core's figures (ore-processing.json on main). Starting values for playtesting. */
export const CFG = {
  concentrators: { pan: 0.45, rocker: 0.55, sluice: 0.65, jig: 0.8, table: 0.92, tableandvanner: 0.99 },
  unclassified: 0.85,
  fineUnground: 0.4,
  freeUnamalgamated: 0.7,
  roasters: { firepit: 0.85, stall: 0.92, reverberatory: 1.0 },
  /** What share of its units each form gives in a furnace (OreRecovery.SmeltShare). Sulfide concentrate gives 0 until roasted (#720). */
  smelt: { raw: 0, chunk: 0.5, crushed: 0.5, ground: 0, concentrate: 1, roastedconcentrate: 1, sponge: 1 },
  /** Per method and tier (#722, #724, #740); null: no station at that tier. The config still keys these by the old five tiers (open question). */
  parting: {
    cupellation: { hand: 0.85, water: 0.95, mill: 1 },
    liquation: { hand: 0.85, water: 0.95, mill: 1 },
    acidparting: { hand: null, water: 0.95, mill: 1 },
  } as Record<string, Record<Tier, number | null>>,
  /** Litharge back to lead: 21 litharge an ingot (OreProducts.LithargeRate). */
  litharge: 0.95,
  mercuryReturn: 0.9,
  unitsPerCrushed: 5,
} as const;

type ConcKey = keyof typeof CFG.concentrators;
type RoastKey = keyof typeof CFG.roasters;

/** Each tier's concentrators, best first, and its roaster. */
export const DEVICES: Record<Tier, { conc: { name: string; key: ConcKey }[]; roaster: { name: string; key: RoastKey }; grinds: boolean }> = {
  hand: { conc: [{ name: "rocker", key: "rocker" }, { name: "gold pan", key: "pan" }], roaster: { name: "firepit", key: "firepit" }, grinds: false },
  water: { conc: [{ name: "jig L2", key: "jig" }, { name: "sluice L1", key: "sluice" }], roaster: { name: "stall L1", key: "stall" }, grinds: true },
  mill: { conc: [{ name: "table + vanner L4", key: "tableandvanner" }, { name: "table L3", key: "table" }], roaster: { name: "reverberatory L2", key: "reverberatory" }, grinds: true },
};

export interface Feed {
  /** Poor ore (fine-grained). */
  poor?: boolean;
  ground?: boolean;
  classified?: boolean;
  /** Free gold or silver (gold and silver quartz, placer gold). */
  free?: boolean;
  amalgamated?: boolean;
  density?: number;
}

/** OreRecovery.Concentration: base × 0.85 unclassified × 0.4 poor-unground × 0.7 free-unamalgamated × density, capped at 1. */
export function concentration(base: number, f: Feed): number {
  let r = base * (f.density ?? 1);
  if (f.classified === false) r *= CFG.unclassified;
  if (f.poor && !f.ground) r *= CFG.fineUnground;
  if (f.free && !f.amalgamated) r *= CFG.freeUnamalgamated;
  return Math.min(1, r);
}

/** A tier's concentrate figures for a feed, best device first; poor feed is ground where the tier has a grinder. */
export function tierConcentrate(tier: Tier, f: Feed): { device: string; value: number }[] {
  const d = DEVICES[tier];
  return d.conc.map((c) => ({ device: c.name, value: concentration(CFG.concentrators[c.key], { ...f, ground: f.ground ?? (f.poor ? d.grinds : false) }) }));
}

const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";

// ---- the line, per ore class

export type Exit =
  /** Stop here and smelt: what you get, of what is in the item. */
  | { kind: "smelt"; text: string }
  /** Nothing to smelt here. */
  | { kind: "none"; text: string }
  /** Goes on to another step, not a furnace. */
  | { kind: "on"; text: string };

export interface Step {
  stage: string;
  /** The step is optional: what skipping it costs. */
  optional?: string;
  /** The step is required: why. */
  required?: string;
  /** One device at every tier. */
  span?: string;
  /** The device per tier; "" for none. */
  devices?: Record<Tier, string>;
  exit: Exit;
  issues: number[];
}

/** A table cell: figures (best device first), or a word. */
export type Cell = { figures: { device: string; value: number }[]; unit?: "×"; note?: string } | { text: string; kind: "zero" | "none" | "open" };

export interface Stop {
  /** The item you hold. */
  item: string;
  /** Which grades, or what else, the row is for. */
  when?: string;
  cells: Record<Tier, Cell>;
}

export interface OreClass {
  id: string;
  title: string;
  ores: string;
  lede?: string;
  steps: Step[];
  /** The table's title. */
  stops: Stop[];
  /** Under the table: the formula and the footnotes. */
  formula: string;
  notes: string[];
  example: { title: string; lines: string[] };
}

const pct = (x: number) => `${Math.round(x * 1000) / 10}`;
const units = (x: number) => `${Math.round(x * 10) / 10}`;

const zero = (text = "does not smelt"): Cell => ({ text, kind: "zero" });
const none = (text: string): Cell => ({ text, kind: "none" });
const open = (text: string): Cell => ({ text, kind: "open" });
const all = (c: Cell): Record<Tier, Cell> => ({ hand: c, water: c, mill: c });
const share = (v: number): Cell => ({ figures: [{ device: "", value: v }] });
const figs = (f: (t: Tier) => { device: string; value: number }[], note?: (t: Tier) => string | undefined): Record<Tier, Cell> =>
  Object.fromEntries(TIERS.map((t) => [t, { figures: f(t), note: note?.(t) }])) as Record<Tier, Cell>;

/** The steps every ore takes from the rock to crushed, classified feed. */
const minedStep = (exit: Exit): Step => ({
  stage: "Mined",
  span: "Raw ore (poor, medium: stack 4) or chunk (rich, bountiful: stack 16): vanilla's ore item by grade",
  exit,
  issues: [686],
});
const MINED: Step = minedStep({ kind: "smelt", text: `chunk ${pct(CFG.smelt.chunk)} %, raw ore ${pct(CFG.smelt.raw)} %` });
const CRUSH: Step = {
  stage: "Crush",
  required: "no loss: the ore's units ÷ 5 crushed items. Poor comes out fine, the rest coarse.",
  devices: { hand: "spalling: ore on the ground, hammer blows", water: "pulverizer (patched), or stamp mill L1", mill: "stamp mill L2: rock breaker, chute-fed" },
  exit: { kind: "smelt", text: `${pct(CFG.smelt.crushed)} %` },
  issues: [747, 687, 728],
};
const CLASSIFY: Step = {
  stage: "Classify",
  optional: `skip it: × ${CFG.unclassified} at the concentrator. No effect on smelting crushed ore.`,
  devices: { hand: "riddle", water: "riddle, or the stamp mill L1 screen", mill: "stamp mill L2 screen" },
  exit: { kind: "smelt", text: `${pct(CFG.smelt.crushed)} %, still crushed ore` },
  issues: [714, 728],
};
const GRIND: Step = {
  stage: "Grind",
  optional: `poor ore only. Skip it: × ${CFG.fineUnground} at the concentrator. No hand grinder.`,
  devices: { hand: "", water: "grinder L1: arrastra", mill: "grinder L2: ball mill, chute-fed" },
  exit: { kind: "none", text: "ground ore does not smelt: concentrate it" },
  issues: [732],
};
const concentrateStep = (exit: Exit): Step => ({
  stage: "Concentrate",
  required: "the lossy step: the device's base × the feed's factors × the ore's density.",
  devices: {
    hand: `gold pan ${pct(CFG.concentrators.pan)} %, rocker ${pct(CFG.concentrators.rocker)} % (a bucket poured while rocking)`,
    water: `sluice L1 ${pct(CFG.concentrators.sluice)} % (fed and emptied by hand), jig L2 ${pct(CFG.concentrators.jig)} % (hopper in)`,
    mill: `table L3 ${pct(CFG.concentrators.table)} %, table and vanner L4 ${pct(CFG.concentrators.tableandvanner)} %`,
  },
  exit,
  issues: [716, 734],
});
const CONCENTRATE = concentrateStep({ kind: "smelt", text: "100 % of the concentrate: the table has the cumulative figure" });
const SMELT: Step = {
  stage: "Smelt",
  span: "Any furnace: firepit crucible, crucibulum, crucible furnace, bloomery, smex",
  exit: { kind: "on", text: "metal" },
  issues: [688],
};

/** The stops shared by every class up to the concentrator. */
const COMMON_STOPS: Stop[] = [
  { item: "Raw ore", when: "poor, medium", cells: all(zero()) },
  { item: "Chunk", when: "rich, bountiful", cells: all(share(CFG.smelt.chunk)) },
  { item: "Crushed ore", when: "any grade, classified or not", cells: all(share(CFG.smelt.crushed)) },
  { item: "Ground ore", when: "poor", cells: { hand: none("no hand grinder"), water: zero(), mill: zero() } },
];

const POOR_NOTE = (t: Tier) => (t === "hand" ? `no grinder: × ${CFG.fineUnground}` : "ground");

const FORMULA = `concentrate = base × ${CFG.unclassified} if unclassified × ${CFG.fineUnground} if poor and not ground × density, capped at 100 %; crushed ore and chunks smelt at ${pct(CFG.smelt.crushed)} % of their units, raw and ground ore at 0 (ore-processing.json: concentrators, smelt; OreRecovery.Concentration, SmeltShare).`;
const UNCLASSIFIED_NOTE = `Every concentrate figure is × ${CFG.unclassified} if the feed skipped the riddle or screen: rocker ${pct(CFG.concentrators.rocker * CFG.unclassified)} %, jig ${pct(CFG.concentrators.jig * CFG.unclassified)} %, table and vanner ${pct(CFG.concentrators.tableandvanner * CFG.unclassified)} %.`;
const POOR_UNGROUND_NOTE = `Poor at the water and mill tiers assumes the grinder; without it × ${CFG.fineUnground} (jig ${pct(CFG.concentrators.jig * CFG.fineUnground)} %, table and vanner ${pct(CFG.concentrators.tableandvanner * CFG.fineUnground)} %).`;
const DENSITY_NOTE = "Density: cassiterite and chromite × 1.05, native copper × 1.1, smithsonite × 0.9, on the concentrate figures (capped at 100 %).";

// Worked examples, in units, from the grade's units (ore-graded.json metalUnitsByType).
const hematitePoor = { count: 8, each: 20 };
const galenaMedium = { count: 4, each: 25 };
const silverMedium = { count: 10, each: 15 };
const conc = (tier: Tier, f: Feed) => tierConcentrate(tier, f)[0]!;
const roast = (tier: Tier) => CFG.roasters[DEVICES[tier].roaster.key];

export const CLASSES: OreClass[] = [
  {
    id: "oxide",
    title: "Oxide and carbonate ores",
    ores:
      "Hematite, magnetite, limonite, malachite, azurite, cassiterite, chromite, ilmenite, smithsonite, hemimorphite, cerussite, vanadinite, wulfenite, uraninite, rhodochrosite. Native copper and native platinum take this line too (native, but not free metal: no amalgamation).",
    steps: [MINED, CRUSH, CLASSIFY, GRIND, CONCENTRATE, SMELT],
    stops: [
      ...COMMON_STOPS,
      { item: "Concentrate", when: "medium, rich, bountiful", cells: figs((t) => tierConcentrate(t, {})) },
      { item: "Concentrate", when: "poor", cells: figs((t) => tierConcentrate(t, { poor: true }), POOR_NOTE) },
    ],
    formula: FORMULA,
    notes: [UNCLASSIFIED_NOTE, POOR_UNGROUND_NOTE, DENSITY_NOTE],
    example: {
      title: `${hematitePoor.count} poor hematite raw ore, ${hematitePoor.each} units each: ${hematitePoor.count * hematitePoor.each} units of iron in the rock`,
      lines: [
        `Smelt now: nothing (raw ore). Spall it: ${(hematitePoor.count * hematitePoor.each) / CFG.unitsPerCrushed} crushed ore, which smelt to ${units(hematitePoor.count * hematitePoor.each * CFG.smelt.crushed)} units.`,
        ...TIERS.map((t) => {
          const c = conc(t, { poor: true });
          return `${TIER_LABEL[t][0]}, concentrated (${c.device}${t === "hand" ? ", not ground" : ", ground"}): ${pct(c.value)} % = ${units(hematitePoor.count * hematitePoor.each * c.value)} units.`;
        }),
        `So by hand, poor ore is worth more smelted as crushed ore (${pct(CFG.smelt.crushed)} %) than concentrated (${pct(conc("hand", { poor: true }).value)} %); medium ore in the rocker (${pct(CFG.concentrators.rocker)} %) only just beats the crushed smelt, and loses to it unclassified (${pct(CFG.concentrators.rocker * CFG.unclassified)} %).`,
      ],
    },
  },
  {
    id: "sulfide",
    title: "Sulfide ores",
    ores: "Galena, argentiferous galena, chalcopyrite, chalcocite, sphalerite, pentlandite, bismuthinite, pyrite, sperrylite, tetrahedrite, freibergite, teallite, franckeite.",
    lede: "As an oxide to concentrate, then the concentrate must be roasted before it smelts. Crushed sulfide ore and sulfide chunks still smelt at a half, so the only way to skip roasting is to stop before concentrating.",
    steps: [
      MINED,
      CRUSH,
      CLASSIFY,
      GRIND,
      concentrateStep({ kind: "none", text: "sulfide concentrate does not smelt until roasted" }),
      {
        stage: "Roast",
        required: "unroasted sulfide concentrate does not smelt (its one combustible property roasts it). Sulfur comes off from the stall on.",
        devices: {
          hand: `firepit ${pct(CFG.roasters.firepit)} %, any fuel, no sulfur`,
          water: `roaster L1: stall ${pct(CFG.roasters.stall)} %, sulfur`,
          mill: `roaster L2: reverberatory ${pct(CFG.roasters.reverberatory)} %, chute-fed`,
        },
        exit: { kind: "smelt", text: "100 % of the roasted concentrate" },
        issues: [720, 736],
      },
      SMELT,
    ],
    stops: [
      ...COMMON_STOPS,
      { item: "Concentrate", when: "unroasted, any grade", cells: all(zero("roast it first")) },
      {
        item: "Roasted concentrate",
        when: "medium, rich, bountiful",
        cells: figs(
          (t) => tierConcentrate(t, {}).map((c) => ({ device: c.device, value: c.value * roast(t) })),
          (t) => `× ${DEVICES[t].roaster.name} ${pct(roast(t))} %`,
        ),
      },
      {
        item: "Roasted concentrate",
        when: "poor",
        cells: figs(
          (t) => tierConcentrate(t, { poor: true }).map((c) => ({ device: c.device, value: c.value * roast(t) })),
          (t) => `× ${DEVICES[t].roaster.name} ${pct(roast(t))} %; ${POOR_NOTE(t)}`,
        ),
      },
    ],
    formula: `roasted concentrate = concentrate figure × roaster (firepit ${CFG.roasters.firepit}, stall ${CFG.roasters.stall}, reverberatory ${CFG.roasters.reverberatory}); ${FORMULA}`,
    notes: [
      `#688 (closed) still reads "unroasted sulfide concentrate 60 %"; #720 (newer, closed) replaced that with "does not smelt at all", and the code agrees (OreRecovery.SmeltShare returns 0 for sulfide concentrate). This page follows the code.`,
      UNCLASSIFIED_NOTE,
      POOR_UNGROUND_NOTE,
      "Galena, argentiferous galena, tetrahedrite, freibergite, teallite and franckeite carry a second metal: see the parting table below. Smelted as here they give only the main metal.",
    ],
    example: {
      title: `${galenaMedium.count} medium galena raw ore, ${galenaMedium.each} units each: ${galenaMedium.count * galenaMedium.each} units of lead (and ${pct(0.03)} % of that in silver, won only by cupellation)`,
      lines: [
        `Spall: ${(galenaMedium.count * galenaMedium.each) / CFG.unitsPerCrushed} crushed ore, which smelt to ${units(galenaMedium.count * galenaMedium.each * CFG.smelt.crushed)} units of lead with no roasting.`,
        ...TIERS.map((t) => {
          const c = conc(t, {});
          return `${TIER_LABEL[t][0]} (${c.device} × ${DEVICES[t].roaster.name}): ${pct(c.value)} % × ${pct(roast(t))} % = ${pct(c.value * roast(t))} % = ${units(galenaMedium.count * galenaMedium.each * c.value * roast(t))} units.`;
        }),
        `So by hand, concentrating and roasting a sulfide (${pct(conc("hand", {}).value * roast("hand"))} %) gives less lead than smelting the crushed ore (${pct(CFG.smelt.crushed)} %); the hand sulfide line pays only for the silver the cupel wins from the roasted concentrate.`,
      ],
    },
  },
  {
    id: "native",
    title: "Native gold and silver: gold quartz and silver quartz",
    ores: "Free metal, which floats past a gravity concentrator unless it is amalgamated. Gold quartz also carries 15 % silver, won only by acid parting (parting table). Mercury comes from cinnabar retorted in the same still.",
    steps: [
      MINED,
      CRUSH,
      CLASSIFY,
      GRIND,
      concentrateStep({ kind: "smelt", text: `100 % of the concentrate, but the concentrate caught only × ${CFG.freeUnamalgamated} if not amalgamated` }),
      {
        stage: "Amalgamate",
        optional: `skip it: × ${CFG.freeUnamalgamated} at the concentrator (the free metal floats past). Amalgamation counts when done at the concentrator's tier.`,
        devices: {
          hand: "amalgam pan: concentrate and mercury",
          water: "amalgam pan with the sluice; amalgamation plates fitted to the jig",
          mill: "amalgamation plates on the table",
        },
        exit: { kind: "on", text: "amalgam: retort it, it does not smelt" },
        issues: [718, 734],
      },
      {
        stage: "Retort",
        span: `The still (cooking pot and condenser) at every tier: ${pct(CFG.mercuryReturn)} % of the mercury back, the metal left as sponge`,
        exit: { kind: "smelt", text: "sponge: 100 %" },
        issues: [726],
      },
      SMELT,
    ],
    stops: [
      ...COMMON_STOPS,
      { item: "Concentrate, not amalgamated", when: "medium, rich, bountiful", cells: figs((t) => tierConcentrate(t, { free: true })) },
      { item: "Concentrate, not amalgamated", when: "poor", cells: figs((t) => tierConcentrate(t, { free: true, poor: true }), POOR_NOTE) },
      { item: "Amalgam", cells: all(none("does not smelt: retort it")) },
      { item: "Sponge, retorted", when: "medium, rich, bountiful, amalgamated", cells: figs((t) => tierConcentrate(t, { free: true, amalgamated: true })) },
      { item: "Sponge, retorted", when: "poor, amalgamated", cells: figs((t) => tierConcentrate(t, { free: true, poor: true, amalgamated: true }), POOR_NOTE) },
    ],
    formula: `not amalgamated: × ${CFG.freeUnamalgamated} (freeUnamalgamated); amalgamated and retorted: the concentrate figure in full (the sponge smelts at 100 %); ${FORMULA}`,
    notes: [
      UNCLASSIFIED_NOTE,
      POOR_UNGROUND_NOTE,
      `Gold quartz's gold when its silver is not parted: the config's "unparted" 0.85 and #684 say gold at 85 %, but on main only the cupel and liquation pan apply it (OreRecovery.Smelted); the furnace patch (OreProcessingItems) smelts every gold quartz form at the plain share, so a furnace gives 100 % gold today. Open question.`,
    ],
    example: {
      title: `${silverMedium.count} medium silver quartz raw ore, ${silverMedium.each} units each: ${silverMedium.count * silverMedium.each} units of silver`,
      lines: [
        `Spall: ${(silverMedium.count * silverMedium.each) / CFG.unitsPerCrushed} crushed ore, which smelt to ${units(silverMedium.count * silverMedium.each * CFG.smelt.crushed)} units.`,
        ...TIERS.map((t) => {
          const raw = conc(t, { free: true });
          const am = conc(t, { free: true, amalgamated: true });
          return `${TIER_LABEL[t][0]} (${am.device}): not amalgamated ${pct(raw.value)} % = ${units(silverMedium.count * silverMedium.each * raw.value)} units; amalgamated and retorted ${pct(am.value)} % = ${units(silverMedium.count * silverMedium.each * am.value)} units.`;
        }),
      ],
    },
  },
  {
    id: "placer",
    title: "Placer gravel",
    ores: "Wilderlands' rich gravel and the pack's placer fields: free grains already, 1.1 to 4.1 units a block by rock (the pan's yield ÷ 0.45).",
    lede: "No crushing or grinding, and no furnace: the line puts out nuggets (each metal in the gravel's proportions) and the pan's stone, flint, quartz and gems. Yields are relative to the gold pan, which gives at most one item a pan.",
    steps: [
      { stage: "Dug", span: "A gravel block", exit: { kind: "smelt", text: "pan it now: 1× (one item a pan at most)" }, issues: [701] },
      {
        stage: "Classify",
        optional: "the issues do not say whether gravel pays the × 0.85 unclassified, nor name a hand classifier for it.",
        devices: { hand: "", water: "stamp mill L1 screen only (no stamps)", mill: "stamp mill L2 screen only" },
        exit: { kind: "on", text: "classified gravel" },
        issues: [701, 728],
      },
      {
        stage: "Concentrate",
        required: "yield relative to the gold pan, no one-item cap.",
        devices: { hand: "gold pan 1×, rocker 1.2×", water: "sluice L1 1.4×, jig L2 (not given)", mill: "table L3 2.0×, table and vanner L4 2.2×" },
        exit: { kind: "on", text: "nuggets, and placer gold to the amalgamator" },
        issues: [701, 734],
      },
      {
        stage: "Amalgamate",
        optional: `gold only, as for gold quartz: × ${CFG.freeUnamalgamated} without.`,
        devices: { hand: "amalgam pan", water: "amalgam pan; plates from the jig", mill: "plates" },
        exit: { kind: "on", text: "amalgam to the still, then sponge" },
        issues: [718, 734],
      },
    ],
    stops: [
      {
        item: "Gravel, panned or washed",
        when: "yield relative to the gold pan",
        cells: {
          hand: { figures: [{ device: "rocker", value: 1.2 }, { device: "gold pan", value: 1 }], unit: "×" },
          water: { figures: [{ device: "sluice L1", value: 1.4 }], unit: "×", note: "jig L2: not given (open)" },
          mill: { figures: [{ device: "table + vanner L4", value: 2.2 }, { device: "table L3", value: 2.0 }], unit: "×" },
        },
      },
    ],
    formula: "nuggets = the gravel's units × the device's multiple ÷ (the pan's 1×); placer gold × 0.7 if not amalgamated (#701, #685).",
    notes: ["The jig's multiple is not in #701 (80 % ÷ 45 % would be 1.8×): open question.", "Re-panning tailings is no exploit: wpanning trims plain gravel and layered sand to stone, flint, quartz and 0.1 % gold (#694)."],
    example: {
      title: "One block of gravel holding 4 units of gold",
      lines: [
        "The gravel's content is the pan's yield ÷ 0.45, so the pan wins 0.45 × 4 = 1.8 units over its 8 pans (1×). Rocker 1.2× = 2.2 units; sluice 1.4× = 2.5; table 2.0× = 3.6; table and vanner 2.2× = 4.0, nearly all of it. Gold: × 0.7 of that unless amalgamated.",
      ],
    },
  },
];

// ---- the by-products: parting

export interface Parted {
  ore: string;
  main: string;
  byProduct: string;
  /** Share of the main metal's recovered units. */
  share: number;
  method: "cupellation" | "liquation" | "acidparting";
  /** What a plain smelt gives without parting. */
  unparted: string;
  open?: string;
  issues: number[];
}

export const PARTED: Parted[] = [
  { ore: "Galena", main: "lead", byProduct: "silver", share: 0.03, method: "cupellation", unparted: "lead; the silver is lost", issues: [690, 722] },
  { ore: "Argentiferous galena", main: "lead", byProduct: "silver", share: 0.38, method: "cupellation", unparted: "lead; the silver is lost", issues: [690, 722] },
  { ore: "Tetrahedrite", main: "copper", byProduct: "silver", share: 0.05, method: "cupellation", unparted: "copper; the silver is lost. The cupel charge takes 1 unit of lead per ore unit", issues: [722] },
  {
    ore: "Freibergite",
    main: "silver",
    byProduct: "copper",
    share: 0.3,
    method: "cupellation",
    unparted: "silver; the copper is lost. Lead added as for tetrahedrite",
    open: "how the copper comes out of the cupel (with the litharge, as its own nugget share) is to settle when built (#722)",
    issues: [722, 740],
  },
  { ore: "Teallite", main: "tin", byProduct: "lead", share: 0.4, method: "liquation", unparted: "tin; the lead is lost (also if the pan passes 327 °C)", issues: [724] },
  { ore: "Franckeite", main: "tin", byProduct: "lead", share: 0.3, method: "liquation", unparted: "tin; the lead is lost", issues: [724] },
  {
    ore: "Gold quartz",
    main: "gold",
    byProduct: "silver",
    share: 0.15,
    method: "acidparting",
    unparted: "gold at 85 % (config); the silver is lost",
    open: "the furnace patch on main applies no 0.85: see the native line's notes",
    issues: [740],
  },
];

export const METHODS: Record<Parted["method"], { name: string; devices: Record<Tier, string>; issues: number[] }> = {
  cupellation: {
    name: "Cupellation",
    devices: { hand: "bone-ash cupel in crucibulum's forge, blast gate open, single use", water: "parting furnace L1: cupellation furnace", mill: "parting furnace L2: Parkes kettle (zinc, mostly returned)" },
    issues: [722, 740],
  },
  liquation: {
    name: "Liquation",
    devices: { hand: "clay liquation pan in a firepit or forge, held between 240 and 327 °C", water: "parting furnace L1: liquation furnace", mill: "parting furnace L2, chute-fed" },
    issues: [724, 740],
  },
  acidparting: {
    name: "Acid parting",
    devices: { hand: "", water: "parting furnace L1: acid parting vessel (aqua fortis from saltpeter and sulfuric acid; sulfur from the stall roaster)", mill: "parting furnace L2, chute-fed" },
    issues: [740, 742, 736],
  },
};

// ---- coal and non-metals

export interface Chain {
  id: string;
  title: string;
  steps: string[];
  note?: string;
  issues: number[];
}

export const CHAINS: Chain[] = [
  { id: "coal", title: "Coal", steps: ["Raw coal (stack 4)", "Concentrator, water or mill level: washes it", "Washed coal: less ash, more heat an item"], note: "No hand form is named, and whether coal is crushed first is not said (#734).", issues: [686, 734] },
  { id: "leach", title: "Borax, saltpeter, alum", steps: ["Raw mineral (stack 4)", "Barrel: water and mineral to a crude liquor, hours; the rock is lost", "Cooking pot on a firepit: evaporates", "Today's powdered borax, saltpeter, powdered alum"], note: "Hand only; no station.", issues: [742] },
  { id: "sulfur", title: "Sulfur", steps: ["Sulfur-bearing rock", "Sulfur kiln (sketched only)", "Sulfur; also off roasting sulfides at the stall and reverberatory, amount open"], issues: [744, 736] },
  { id: "mercury", title: "Mercury", steps: ["Cinnabar, powder or crushed", "The still: cooking pot on a firepit, condenser beside it", "Mercury (Expanded Matter's liquid; its cooking recipe removed)"], issues: [726] },
];

export const OPEN_QUESTIONS: string[] = [
  "Gold quartz unparted: ore-processing.json and #684 give gold at 85 % when its silver is not parted, but main's furnace patch (OreProcessingItems) smelts gold quartz concentrate, chunks and sponge at the plain share; only the cupel and liquation pan (OreRecovery.Smelted) apply the 0.85. Is 85 % meant in every furnace?",
  "Parting at the water tier: #740 gives the parting furnace L1 95 % and L2 100 %, and the hand stations 85 % (#722, #724). ore-processing.json still keys parting by the old five tiers (hand 85, tier 1 85, tier 2 95, tier 3 95, tier 4 100); #765 is to rework it. The parting table follows #740.",
  "Sulfur from roasting: how much a roasted sulfide concentrate gives at the stall and reverberatory roasters (open in #684 and #736), and the sulfur kiln (#744) is only sketched.",
  "Placer gravel: the jig's yield relative to the pan is not given in #701 (rocker 1.2×, sluice 1.4×, table 2.0×, table and vanner 2.2×). Does gravel pay the × 0.85 unclassified factor, and is there a hand classifier for it (the riddle is for crushed ore, #714)?",
  "Freibergite's copper: \"comes out with the lead's litharge as its own nugget share (settle when built)\" (#722). As what item, and does the Parkes kettle give it the same way?",
  "The liquation pan's output form (tin out and lead left in the pan, or a pan with lead residue) is to settle when built (#724).",
  "Gold pan on crushed ore: #716 puts the vanilla gold pan (45 %) before the rocker as a hand concentrator for crushed ore, but vanilla's pan works gravel and sand blocks. Is a pan for crushed ore part of #716?",
  "Coal: does it need crushing before washing, and does the hand tier wash it at all (#734 names only the concentrator's water and mill levels)?",
];

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
const issueLinks = (ns: number[]) => `<span class="ol-issues">${ns.map(issueLink).join(" ")}</span>`;
/** "#123" in prose becomes a link. */
const linked = (s: string) => esc(s).replace(/#(\d{3,4})\b/g, (_m, n: string) => issueLink(Number(n)));


function exit(e: Exit): string {
  const label = e.kind === "smelt" ? "Smelt now" : e.kind === "none" ? "No smelt" : "On";
  return `<div class="ol-exit ol-exit-${e.kind}"><b>${label}</b> ${linked(e.text)}</div>`;
}

function step(s: Step, i: number): string {
  const tag = s.optional
    ? `<span class="ol-tag ol-tag-opt">optional</span> <span class="ol-cost">${linked(s.optional)}</span>`
    : s.required
      ? `<span class="ol-cost">${linked(s.required)}</span>`
      : "";
  const body = s.span
    ? `<div class="ol-dev ol-dev-span">${linked(s.span)}</div>`
    : TIERS.map((t) => `<div class="ol-dev ol-dev-${t}"><span class="ol-dot ol-dot-${t}" title="${TIER_LABEL[t][0]}"></span>${s.devices![t] ? linked(s.devices![t]) : "<i>none</i>"}</div>`).join("");
  return `<div class="ol-step"><div class="ol-step-n">${i + 1}</div><div class="ol-stage">${esc(s.stage)}</div>${tag ? `<div class="ol-opt">${tag}</div>` : ""}${body}${exit(s.exit)}${issueLinks(s.issues)}</div>`;
}

function cell(c: Cell): string {
  if ("text" in c) {
    if (c.kind === "zero") return `<td class="ol-c ol-c-zero"><span class="ol-f ol-f-best"><b>0 %</b></span><span class="ol-cnote">${linked(c.text)}</span></td>`;
    return `<td class="ol-c ol-c-${c.kind}">${linked(c.text)}${c.kind === "open" ? ' <span class="ol-tag ol-tag-open">open</span>' : ""}</td>`;
  }
  const f = c.figures.map(
    (x, i) => `<span class="ol-f${i === 0 ? " ol-f-best" : ""}"><b>${c.unit === "×" ? `${x.value}×` : `${pct(x.value)} %`}</b>${x.device ? ` ${esc(x.device)}` : ""}</span>`,
  );
  return `<td class="ol-c">${f.join("")}${c.note ? `<span class="ol-cnote">${linked(c.note)}</span>` : ""}</td>`;
}

function stopsTable(c: OreClass): string {
  const head = `<tr><th class="ol-th-item">Stop and smelt as…</th>${TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</th>`).join("")}</tr>`;
  const rows = c.stops.map((s) => `<tr><th scope="row"><b>${esc(s.item)}</b>${s.when ? `<span class="ol-when">${linked(s.when)}</span>` : ""}</th>${TIERS.map((t) => cell(s.cells[t])).join("")}</tr>`).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table"><thead>${head}</thead><tbody>${rows}</tbody></table></div>`;
}

export function renderClass(c: OreClass): string {
  return `<section class="ol-class" id="ol-${c.id}" aria-labelledby="ol-${c.id}-h">
<h3 id="ol-${c.id}-h">${esc(c.title)}</h3>
<p class="ol-ores">${linked(c.ores)}</p>${c.lede ? `<p class="ol-lede">${linked(c.lede)}</p>` : ""}
<div class="ol-strip">${c.steps.map((s, i) => step(s, i)).join('<div class="ol-arrow" aria-hidden="true"></div>')}</div>
<h4>Stop here and smelt: cumulative recovery of the metal in the rock</h4>
${stopsTable(c)}
<p class="ol-formula"><b>Formula:</b> ${linked(c.formula)}</p>
<ul class="ol-notes">${c.notes.map((n) => `<li>${linked(n)}</li>`).join("")}</ul>
<div class="ol-example"><b>Worked example: ${linked(c.example.title)}</b><ul>${c.example.lines.map((l) => `<li>${linked(l)}</li>`).join("")}</ul></div>
</section>`;
}

export function renderParting(): string {
  const methods = Object.entries(METHODS)
    .map(
      ([key, m]) =>
        `<div class="ol-method"><b>${esc(m.name)}</b> ${issueLinks(m.issues)}${TIERS.map((t) => `<div class="ol-dev ol-dev-${t}"><span class="ol-dot ol-dot-${t}"></span>${m.devices[t] ? linked(m.devices[t]) : "<i>none</i>"} <b class="ol-fig">${CFG.parting[key]![t] == null ? "—" : `${pct(CFG.parting[key]![t]!)} %`}</b></div>`).join("")}</div>`,
    )
    .join("");
  const head = `<tr><th>Ore</th><th>Main metal</th><th>By-product</th><th>Parted by</th>${TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</th>`).join("")}<th>Smelted unparted</th></tr>`;
  const rows = PARTED.map((p) => {
    const cells = TIERS.map((t) => {
      const r = CFG.parting[p.method]![t];
      if (r == null) return `<td class="ol-c ol-c-none">no station: lost</td>`;
      return `<td class="ol-c"><span class="ol-f ol-f-best"><b>${pct(p.share * r)} %</b> of the main metal</span><span class="ol-cnote">${pct(p.share)} % × ${pct(r)} %</span></td>`;
    }).join("");
    return `<tr><th scope="row"><b>${esc(p.ore)}</b> ${issueLinks(p.issues)}</th><td>${esc(p.main)}</td><td>${esc(p.byProduct)} <b>${pct(p.share)} %</b></td><td>${esc(METHODS[p.method].name)}</td>${cells}<td class="ol-unparted">${linked(p.unparted)}${p.open ? ` <span class="ol-tag ol-tag-open">open</span> <span class="ol-cnote">${linked(p.open)}</span>` : ""}</td></tr>`;
  }).join("\n");
  return `<section class="ol-class" id="ol-parting" aria-labelledby="ol-parting-h">
<h3 id="ol-parting-h">By-products: the parting step</h3>
<p class="ol-ores">Six ores carry a second metal, won only at a parting step after roasting (or, for gold quartz, after retorting). Smelted in a furnace instead, they give the main metal and the by-product is lost. The by-product is a share of the main metal's recovered units, so it rides on the line's figure above: parting never changes the main metal's recovery.</p>
<div class="ol-methods">${methods}</div>
<div class="ol-tablewrap"><table class="ol-table ol-table-parting"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> by-product units = main metal units recovered × share × the parting station's figure (ore-processing.json: ores.*.byProducts, parting; OreRecovery.Smelted). Cupellation's lead soaks into the cupel as litharge, which smelts back to lead at ${pct(CFG.litharge)} % (21 litharge an ingot, OreProducts.LithargeRate).</p>
<div class="ol-example"><b>Worked example: 100 units of lead recovered from argentiferous galena, cupelled</b><ul><li>Hand cupel: 100 lead as litharge (${units(100 * CFG.litharge)} back as lead) and 100 × 0.38 × 0.85 = ${units(100 * 0.38 * 0.85)} units of silver. Parting furnace L1: ${units(100 * 0.38 * 0.95)}; Parkes kettle: ${units(100 * 0.38)}. Smelted instead: 100 lead, no silver.</li></ul></div>
</section>`;
}

export function renderChains(): string {
  return `<section class="ol-class" id="ol-nonmetals" aria-labelledby="ol-nonmetals-h"><h3 id="ol-nonmetals-h">Coal and non-metals</h3>
<div class="ol-chains">${CHAINS.map(
    (c) =>
      `<div class="ol-chain" id="ol-${c.id}"><b>${esc(c.title)}</b> ${issueLinks(c.issues)}<ol>${c.steps.map((s) => `<li>${linked(s)}</li>`).join("")}</ol>${c.note ? `<p class="ol-note">${linked(c.note)}</p>` : ""}</div>`,
  ).join("")}</div></section>`;
}

/** The tab's styles; ore-proposal.ts reuses them (the two tabs sit on one page). */
export const STYLE = `<style>
.ore-line { --ol-hand: var(--series-4); --ol-water: var(--series-1); --ol-mill: var(--series-3); --ol-line: var(--muted); }
.ore-line h2 { font-size: 1.3rem; margin: 0.2rem 0 0.4rem; }
.ore-line h3 { font-size: 1.12rem; margin: 0 0 0.3rem; }
.ore-line h4 { font-size: 0.95rem; margin: 1rem 0 0.35rem; }
.ore-line p { margin: 0.3rem 0; }
.ore-line ul, .ore-line ol { margin: 0.25rem 0; padding-left: 1.2rem; }
.ol-intro { max-width: 56rem; }
.ol-jump { display: flex; flex-wrap: wrap; gap: 0.3rem 0.5rem; margin: 0.6rem 0 1rem; font-size: 0.88rem; }
.ol-jump a { color: var(--link); }
.ol-legend { display: grid; grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); gap: 0.5rem; margin: 0.6rem 0; }
.ol-legend > div { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.6rem; font-size: 0.86rem; }
.ol-legend .ol-l-tier { border-top: 4px solid var(--ol-c); }
.ol-l-hand { --ol-c: var(--ol-hand); } .ol-l-water { --ol-c: var(--ol-water); } .ol-l-mill { --ol-c: var(--ol-mill); }
.ol-legend .ol-exit { margin: 0.25rem 0 0; }
.ol-smelts { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.4rem 0 0.2rem; padding: 0; list-style: none; font-size: 0.85rem; }
.ol-smelts li { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ol-smelts li b { color: var(--accent); }
.ol-smelts li.ol-s0 b { color: var(--critical); }
.ol-factors { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.3rem 0 0.8rem; padding: 0; list-style: none; font-size: 0.85rem; }
.ol-factors li { background: var(--notice-bg); border: 1px solid var(--notice-border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ol-class { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.8rem; margin: 0 0 1rem; }
.ol-ores, .ol-lede, .ol-note { font-size: 0.88rem; color: var(--muted); }
.ol-lede { color: var(--text); }
/* the step strip */
.ol-strip { display: flex; flex-wrap: wrap; align-items: stretch; gap: 0.35rem 0; margin-top: 0.7rem; }
.ol-step { position: relative; flex: 1 1 10rem; max-width: 24rem; min-width: 0; background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.55rem 0.4rem; font-size: 0.82rem; line-height: 1.3; display: flex; flex-direction: column; gap: 0.25rem; }
.ol-step-n { position: absolute; top: 0.3rem; right: 0.45rem; font-size: 0.72rem; color: var(--muted); }
.ol-stage { font-weight: 700; font-size: 0.95rem; }
.ol-opt { font-size: 0.78rem; color: var(--muted); }
.ol-tag { display: inline-block; font-size: 0.68rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; border-radius: 999px; padding: 0 0.4rem; line-height: 1.4; vertical-align: 0.05rem; }
.ol-tag-opt { background: var(--notice-bg); border: 1px solid var(--notice-border); }
.ol-tag-open { background: var(--notice-bg); border: 1px solid var(--critical); color: var(--critical); }
.ol-dev { display: flex; gap: 0.35rem; align-items: baseline; font-size: 0.8rem; }
.ol-dev-span { color: var(--text); }
.ol-dev i { color: var(--muted); }
.ol-dot { flex: none; display: inline-block; width: 0.65rem; height: 0.65rem; border-radius: 2px; background: var(--ol-c); vertical-align: -0.05rem; margin-right: 0.3rem; }
.ol-dot-hand { --ol-c: var(--ol-hand); } .ol-dot-water { --ol-c: var(--ol-water); } .ol-dot-mill { --ol-c: var(--ol-mill); }
.ol-exit { margin-top: auto; font-size: 0.78rem; border-radius: var(--radius); padding: 0.2rem 0.4rem; border: 1px solid var(--border); }
.ol-exit b { text-transform: uppercase; letter-spacing: 0.04em; font-size: 0.68rem; margin-right: 0.2rem; }
.ol-exit-smelt { border-color: var(--accent); background: color-mix(in srgb, var(--accent) 12%, transparent); }
.ol-exit-smelt b { color: var(--accent); }
.ol-exit-none { border-style: dashed; color: var(--muted); }
.ol-exit-none b { color: var(--critical); }
.ol-exit-on { background: var(--surface-2); }
.ol-arrow { flex: none; width: 1.1rem; align-self: center; height: 2px; background: var(--ol-line); position: relative; }
.ol-arrow::after { content: ""; position: absolute; right: -1px; top: -4px; border: 5px solid transparent; border-left: 6px solid var(--ol-line); border-right: 0; }
.ol-issues { font-size: 0.74rem; }
.ol-issue { color: var(--link); white-space: nowrap; }
/* the stops table */
.ol-tablewrap { overflow-x: auto; }
.ol-table { border-collapse: collapse; width: 100%; font-size: 0.84rem; }
.ol-table th, .ol-table td { border: 1px solid var(--border); padding: 0.3rem 0.45rem; text-align: left; vertical-align: top; }
.ol-table thead th { background: var(--surface-2); font-size: 0.8rem; }
.ol-table tbody th { font-weight: 400; background: var(--bg); min-width: 8rem; }
.ol-table tbody th b { display: block; }
.ol-when { display: block; color: var(--muted); font-size: 0.76rem; }
.ol-c .ol-f { display: block; white-space: nowrap; }
.ol-c .ol-f b { font-weight: 700; }
.ol-c .ol-f-best b { color: var(--accent); font-size: 1rem; }
.ol-c-zero, .ol-c-none { color: var(--muted); }
.ol-c-zero .ol-f-best b { color: var(--critical); }
.ol-cnote { display: block; color: var(--muted); font-size: 0.74rem; white-space: normal; }
.ol-unparted { font-size: 0.8rem; min-width: 11rem; }
.ol-formula, .ol-notes { font-size: 0.8rem; color: var(--muted); }
.ol-formula { margin-top: 0.5rem; }
.ol-notes li { margin: 0.15rem 0; }
.ol-example { margin-top: 0.6rem; font-size: 0.84rem; background: var(--surface-2); border-radius: var(--radius); padding: 0.45rem 0.6rem; }
.ol-example ul { margin: 0.2rem 0 0; }
/* parting */
.ol-methods { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr)); gap: 0.5rem; margin: 0.6rem 0; }
.ol-method { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.4rem 0.55rem; font-size: 0.82rem; display: flex; flex-direction: column; gap: 0.2rem; }
.ol-method .ol-fig { color: var(--accent); white-space: nowrap; }
/* non-metals */
.ol-chains { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr)); gap: 0.5rem; }
.ol-chain { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.4rem 0.55rem; font-size: 0.84rem; }
.ol-chain ol { margin: 0.2rem 0 0; }
.ol-questions { font-size: 0.88rem; background: var(--notice-bg); border: 1px solid var(--notice-border); border-radius: var(--radius); padding: 0.5rem 0.75rem; }
.ol-questions li { margin: 0.3rem 0; }
@media (max-width: 640px) {
  .ol-class { padding: 0.6rem; }
  .ol-strip { flex-direction: column; gap: 0.2rem; }
  .ol-step { flex-basis: auto; max-width: none; }
  .ol-arrow { width: 2px; height: 0.9rem; align-self: center; }
  .ol-arrow::after { right: -4px; top: auto; bottom: -1px; border: 5px solid transparent; border-top: 6px solid var(--ol-line); border-bottom: 0; }
  .ol-table { font-size: 0.76rem; }
  .ol-table th, .ol-table td { padding: 0.25rem 0.3rem; }
  .ol-table tbody th { min-width: 5.5rem; }
  .ol-c .ol-f { white-space: normal; }
  .ol-c .ol-f-best b { font-size: 0.9rem; }
  .ol-table-parting { min-width: 44rem; }
}
</style>`;

export default function oreLine(): string {
  const jump = [...CLASSES.map((c) => ({ id: c.id, title: c.title })), { id: "parting", title: "By-products: parting" }, { id: "nonmetals", title: "Coal and non-metals" }, { id: "open", title: "Open questions" }];
  const smelts: [string, number][] = [
    ["raw ore", CFG.smelt.raw],
    ["chunk", CFG.smelt.chunk],
    ["crushed ore", CFG.smelt.crushed],
    ["ground ore", CFG.smelt.ground],
    ["concentrate", CFG.smelt.concentrate],
    ["sulfide concentrate, unroasted", 0],
    ["roasted concentrate", CFG.smelt.roastedconcentrate],
    ["sponge (retorted amalgam)", CFG.smelt.sponge],
  ];
  return `<div class="ore-line">
${STYLE}
<h2>The processing line, by ore class</h2>
<p class="ol-intro">For each ore class: the steps from the rock to metal, with each tier's device and, under every step, whether you can <b>stop there and smelt</b> what you hold. The table under each strip gives the cumulative recovery of the metal that was in the rock for every stopping point, per tier and per grade where grade matters. All figures come from the recovery core on main (${issueLink(756)}: <code>ore-processing.json</code>, <code>OreRecovery.cs</code>) under the three tiers of ${issueLink(765)} and the machine levels of ${issueLink(711)}; they are starting values for playtesting. Issue numbers link the design; where the design and the code disagree, the cell says <span class="ol-tag ol-tag-open">open</span> and the question is at the <a href="#ol-open">bottom</a>.</p>
<div class="ol-legend">
${TIERS.map((t) => `<div class="ol-l-tier ol-l-${t}"><b><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</b>: ${TIER_LABEL[t][1]}.${t === "hand" ? "" : " L1, L2… are the machine's levels, fitted in place."}</div>`).join("\n")}
<div><b>Under each step</b>: <div class="ol-exit ol-exit-smelt"><b>Smelt now</b> what a furnace gives of what is in the item</div><div class="ol-exit ol-exit-none"><b>No smelt</b> the item does not smelt; go on</div></div>
</div>
<p class="ol-intro" style="margin-top:0.6rem"><b>What a furnace takes</b> (share of the item's units; ${issueLink(686)}, ${issueLink(688)}, ${issueLink(720)}):</p>
<ul class="ol-smelts" aria-label="Smelt shares">${smelts.map(([n, v]) => `<li class="${v === 0 ? "ol-s0" : ""}">${esc(n)} <b>${pct(v)} %</b></li>`).join("")}<li>amalgam <b>retort</b></li></ul>
<p class="ol-intro"><b>Factors at the concentrator</b> (the only lossy step before roasting; crushing and grinding never lose metal):</p>
<ul class="ol-factors" aria-label="Recovery factors">
<li>not classified × ${CFG.unclassified}</li><li>poor ore not ground × ${CFG.fineUnground}</li><li>free gold or silver not amalgamated × ${CFG.freeUnamalgamated}</li><li>density × 0.9 to × 1.1 per ore</li><li>then × the roaster for sulfides: ${pct(CFG.roasters.firepit)} / ${pct(CFG.roasters.stall)} / ${pct(CFG.roasters.reverberatory)} %</li>
</ul>
<nav class="ol-jump" aria-label="Sections">${jump.map((c) => `<a href="#ol-${c.id}">${esc(c.title)}</a>`).join("")}</nav>
${CLASSES.map(renderClass).join("\n")}
${renderParting()}
${renderChains()}
<section class="ol-questions" id="ol-open" aria-labelledby="ol-open-h"><h3 id="ol-open-h">Open questions</h3><ul>${OPEN_QUESTIONS.map((q) => `<li>${linked(q)}</li>`).join("")}</ul></section>
</div>`;
}
