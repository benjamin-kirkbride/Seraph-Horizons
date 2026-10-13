// The ore review page's "Deposits" tab (scripts/standalone/ore-review.json): how much metal a deposit of
// iron, copper, tin, zinc or nickel holds as the pack now generates it (#863: graded ore's veins four
// times Interesting Ore Gen's volume to hold the same ingots at units of 3 / 5 / 7 / 10 by grade), how
// its blocks split by grade, and how many ingots each processing level wins from it, against what a
// player needs. The deposits are measured (tools/ore-survey on main, SURVEY in ore-deposits-data.ts):
// one row each, its blocks per ore and grade. Everything else is computed here from those rows and
// ore-line.ts's CFG, so a change to the figures there carries through. Plain HTML and CSS on the
// processing line's styles, no script.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

import { CFG, FULL, GRADES, handRecovery, lineRecovery, STYLE, type Conc, type Grade } from "./ore-line.ts";
import { DEPOSITS, SURVEY } from "./ore-deposits-data.ts";

export { DEPOSITS, SURVEY };
export type Metal = "iron" | "copper" | "tin" | "zinc" | "nickel";
export const METALS: Metal[] = ["iron", "copper", "tin", "zinc", "nickel"];

/** Where a deposit came from: an ore cell's vein (one per metal per 5 km cell), a hydrothermal district's vein, or neither (a stray vein). */
export type Source = "cell" | "district" | "other";

/** A measured deposit: its blocks per ore, poor / medium / rich / bountiful. */
export interface Deposit {
  seed: number;
  metal: Metal;
  source: Source;
  /** Shallowest ore below the worldgen surface, in blocks (negative: in a cliff face). */
  depth: number;
  ores: Record<string, number[]>;
}

export interface OreInfo {
  metal: Metal;
  sulfide: boolean;
  /** Settling density (config/ore-processing.json), on each concentrator's figure. */
  density: number;
  name: string;
}

/** The five metals' ores (Ore/Core/OreMetals.cs) with their class and density (config/ore-processing.json). */
export const ORES: Record<string, OreInfo> = {
  hematite: { metal: "iron", sulfide: false, density: 1, name: "hematite" },
  magnetite: { metal: "iron", sulfide: false, density: 1, name: "magnetite" },
  limonite: { metal: "iron", sulfide: false, density: 1, name: "limonite" },
  pyrite: { metal: "iron", sulfide: true, density: 1, name: "pyrite" },
  nativecopper: { metal: "copper", sulfide: false, density: 1.1, name: "native copper" },
  malachite: { metal: "copper", sulfide: false, density: 1, name: "malachite" },
  azurite: { metal: "copper", sulfide: false, density: 1, name: "azurite" },
  chalcopyrite: { metal: "copper", sulfide: true, density: 1, name: "chalcopyrite" },
  chalcocite: { metal: "copper", sulfide: true, density: 1, name: "chalcocite" },
  tetrahedrite: { metal: "copper", sulfide: true, density: 1, name: "tetrahedrite" },
  cassiterite: { metal: "tin", sulfide: false, density: 1.05, name: "cassiterite" },
  teallite: { metal: "tin", sulfide: true, density: 1, name: "teallite" },
  franckeite: { metal: "tin", sulfide: true, density: 1, name: "franckeite" },
  sphalerite: { metal: "zinc", sulfide: true, density: 1, name: "sphalerite" },
  smithsonite: { metal: "zinc", sulfide: false, density: 0.9, name: "smithsonite" },
  hemimorphite: { metal: "zinc", sulfide: false, density: 1, name: "hemimorphite" },
  pentlandite: { metal: "nickel", sulfide: true, density: 1, name: "pentlandite" },
};

/** What a mined ore block drops (its drops are vanilla's): 1.25 raw ore, and 0.01 crystallised ore holding twice the units. */
export const DROPS = { raw: 1.25, crystallised: 0.01 };
export const ITEMS_PER_BLOCK = DROPS.raw + DROPS.crystallised;
/** A block's metal units over one raw ore's. */
export const UNITS_PER_BLOCK = DROPS.raw + 2 * DROPS.crystallised;

/** Units a raw ore item holds at a grade (CFG's units and exceptions). */
export const oreUnits = (ore: string, g: Grade): number => (CFG.unitExceptions[ore] ?? CFG.units)[g];

export type Level = "ground" | "mined" | "minedAll" | "hand" | "water" | "full";
/** The levels the tables show, poorest first. */
export const LEVELS: Level[] = ["ground", "mined", "hand", "water", "full"];
export const LEVEL_LABEL: Record<Level, string> = {
  ground: "In the ground",
  mined: "Smelted as mined",
  minedAll: "Every grade smelted as mined",
  hand: "By hand",
  water: "Water level",
  full: "Full line",
};
export const LEVEL_NOTE: Record<Level, string> = {
  ground: "every block's units",
  mined: "rich and bountiful only",
  minedAll: "every grade",
  hand: "spall, riddle, rocker",
  water: "sluice + jig, arrastra",
  full: "4 concentrators, ball mill",
};

/** The water level's concentrators (the full line's are ore-line.ts's FULL). */
export const WATER: Conc[] = ["sluice", "jig"];

/**
 * The share of a block's units a level wins, by ore and grade.
 * - mined: rock smelted by its grade, only the rich and bountiful blocks (poor and medium are left in
 *   the dump: the best case); a sulfide's as roasted rock from a stall.
 * - minedAll: every grade smelted as mined, likewise.
 * - hand: spalled, riddled (classified), the rocker; poor ore stays unground (no hand grinder); a
 *   sulfide's concentrate roasted in the firepit.
 * - water: the sluice and the jig on ground, classified feed; a sulfide's concentrate in the stall.
 * - full: the four concentrators on ground, classified feed; the reverberatory (level 2).
 */
export function share(level: Level, ore: string, g: Grade): number {
  const o = ORES[ore];
  if (!o) throw new Error(`unknown ore ${ore}`);
  const feed = { poor: g === "poor", density: o.density };
  switch (level) {
    case "ground":
      return 1;
    case "mined":
      return g === "rich" || g === "bountiful" ? share("minedAll", ore, g) : 0;
    case "minedAll":
      return CFG.smeltByGrade[g] * (o.sulfide ? CFG.roasters.stall : 1);
    case "hand":
      return handRecovery("rocker", feed) * (o.sulfide ? CFG.roasters.firepit : 1);
    case "water":
      return lineRecovery(WATER, { ...feed, ground: true }) * (o.sulfide ? CFG.roasters.stall : 1);
    case "full":
      return lineRecovery(FULL, { ...feed, ground: true }) * (o.sulfide ? CFG.roasters.reverberatory2 : 1);
  }
}

export interface Tally {
  blocks: number;
  /** Shares of the blocks. */
  grades: Record<Grade, number>;
  ingots: Record<Level, number>;
  /** Rock items mined: raw and crystallised ore. */
  rockItems: number;
}

const ALL_LEVELS: Level[] = ["ground", "mined", "minedAll", "hand", "water", "full"];

/** A deposit's blocks, grade split and ingots at every level. */
export function tally(d: Pick<Deposit, "ores">): Tally {
  const grades = { poor: 0, medium: 0, rich: 0, bountiful: 0 } as Record<Grade, number>;
  const ingots = { ground: 0, mined: 0, minedAll: 0, hand: 0, water: 0, full: 0 } as Record<Level, number>;
  let blocks = 0;
  for (const [ore, counts] of Object.entries(d.ores)) {
    GRADES.forEach((g, i) => {
      const n = counts[i] ?? 0;
      blocks += n;
      grades[g] += n;
      const units = n * UNITS_PER_BLOCK * oreUnits(ore, g);
      for (const l of ALL_LEVELS) ingots[l] += (units * share(l, ore, g)) / CFG.unitsPerIngot;
    });
  }
  for (const g of GRADES) grades[g] = blocks ? grades[g] / blocks : 0;
  return { blocks, grades, ingots, rockItems: blocks * ITEMS_PER_BLOCK };
}

/** Slots one deposit fills: all its rock at 16 a stack, against the full line's concentrate at 128. */
export const rockSlots = (t: Tally) => t.rockItems / CFG.rockStack;
export const concentrateSlots = (t: Tally, level: Level = "full") => (t.ingots[level] * CFG.unitsPerIngot) / CFG.concentrateUnits / CFG.concentrateStack;

/** Linear-interpolated quantile. */
export function quantile(xs: number[], p: number): number {
  const s = [...xs].sort((a, b) => a - b);
  if (!s.length) return NaN;
  const k = (s.length - 1) * p;
  const lo = Math.floor(k);
  const hi = Math.ceil(k);
  return s[lo]! + (s[hi]! - s[lo]!) * (k - lo);
}

/** Worst, typical and best over a set: the 10th, 50th and 90th percentiles. */
export interface Spread {
  lo: number;
  mid: number;
  hi: number;
  n: number;
}
export const spread = (xs: number[]): Spread => ({ lo: quantile(xs, 0.1), mid: quantile(xs, 0.5), hi: quantile(xs, 0.9), n: xs.length });

export const depositsOf = (metal: Metal, source?: Source): Deposit[] => DEPOSITS.filter((d) => d.metal === metal && (!source || d.source === source));

/** Blocks of each grade over a set of deposits, as shares. */
export function pooledGrades(ds: Pick<Deposit, "ores">[]): Record<Grade, number> {
  const out = { poor: 0, medium: 0, rich: 0, bountiful: 0 } as Record<Grade, number>;
  let all = 0;
  for (const d of ds)
    for (const c of Object.values(d.ores))
      GRADES.forEach((g, i) => {
        out[g] += c[i] ?? 0;
        all += c[i] ?? 0;
      });
  for (const g of GRADES) out[g] = all ? out[g] / all : 0;
  return out;
}

/** A deposit's grade, if one grade holds at least `at` of its blocks (IOG draws one grade per vein). */
export function mainGrade(t: Tally, at = 0.8): Grade | null {
  return GRADES.find((g) => t.grades[g] >= at) ?? null;
}

/**
 * Interesting Ore Gen's grade draw for a vein (TiltedDiscDepositGenerator): u² × the ore's most grades,
 * clamped to the grades the host rock allows. With four grades: poor 1/2, medium √2/2 − 1/2, rich
 * √3/2 − √2/2, bountiful 1 − √3/2.
 */
export function veinGradeOdds(gradesInRock: number, maxGrades = 4): number[] {
  const out = new Array<number>(gradesInRock).fill(0);
  for (let i = 0; i < maxGrades; i++) out[Math.min(gradesInRock - 1, i)]! += Math.sqrt((i + 1) / maxGrades) - Math.sqrt(i / maxGrades);
  return out;
}

// ---- what a player needs (the pack's recipes: casting, and smithing at 42 voxels an ingot)

/** One player's tool kit, a tool head each, all one ingot (pickaxe 24 voxels, axe 37, shovel 36, hoe 35, hammer 40, saw blade 22, chisel 16, knife 17, scythe 22, shears 37, prospecting pick 16, tongs 30, spear 32). */
export const KIT = 13;
/** The anvil: cast from 9 ingots (copper, bronze), or smithed in two parts of 196 and 210 voxels, 5 ingots each (iron, which does not cast). */
export const ANVIL = { cast: 9, smithed: 10 };
/** The helve hammer's head, 78 voxels. */
export const HELVE = 2;
/** Tin bronze is 88–92 % copper: at 90 %. Brass 60–70 % copper (zinc 35 %), cupronickel 65–75 % (nickel 30 %), at the middle. */
export const ALLOY = { tinInBronze: 0.1, zincInBrass: 0.35, nickelInCupronickel: 0.3 };
/** The epic's typical deposit, for a group of about four through an age (config/ore-sizes.json, targetIngots). */
export const TARGET: Record<Metal, { small: number; typical: number; large: number }> = {
  iron: { small: 150, typical: 400, large: 1000 },
  copper: { small: 150, typical: 400, large: 1000 },
  tin: { small: 60, typical: 150, large: 400 },
  zinc: { small: 60, typical: 150, large: 400 },
  nickel: { small: 60, typical: 150, large: 400 },
};

export interface Need {
  metal: Metal;
  what: string;
  ingots: number;
  how: string;
}
export const NEEDS: Need[] = [
  { metal: "copper", what: "Copper age, one player", ingots: KIT + ANVIL.cast, how: `${KIT} tool heads + a cast anvil (${ANVIL.cast})` },
  { metal: "copper", what: "Bronze age, one player: its copper", ingots: (KIT + ANVIL.cast) * (1 - ALLOY.tinInBronze), how: `${KIT + ANVIL.cast} tin bronze at ${100 - ALLOY.tinInBronze * 100} % copper` },
  { metal: "tin", what: "Bronze age, one player", ingots: (KIT + ANVIL.cast) * ALLOY.tinInBronze, how: `${KIT + ANVIL.cast} tin bronze at ${ALLOY.tinInBronze * 100} % tin` },
  { metal: "iron", what: "Iron age, one player", ingots: KIT + ANVIL.smithed + HELVE, how: `${KIT} tool heads + a smithed anvil (${ANVIL.smithed}) + a helve hammer head (${HELVE})` },
  { metal: "zinc", what: "Ten brass ingots", ingots: 10 * ALLOY.zincInBrass, how: `brass at ${ALLOY.zincInBrass * 100} % zinc` },
  { metal: "nickel", what: "Ten cupronickel ingots", ingots: 10 * ALLOY.nickelInCupronickel, how: `cupronickel at ${ALLOY.nickelInCupronickel * 100} % nickel` },
];

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
const cap = (s: string) => s[0]!.toUpperCase() + s.slice(1);
const pct = (x: number) => `${Math.round(x * 100)} %`;

/** A figure to two significant figures, thousands grouped: 1,234 → 1,200; 87.4 → 87; 3.44 → 3.4. */
export function sig2(x: number): string {
  if (!Number.isFinite(x)) return "–";
  if (x === 0) return "0";
  const step = 10 ** (Math.floor(Math.log10(Math.abs(x))) - 1);
  const v = Math.round(x / step) * step;
  return Math.abs(v) >= 100 ? Math.round(v).toLocaleString("en-US") : String(Math.round(v * 10) / 10);
}

/** The typical in bold, worst – best under it. */
const sp = (s: Spread) =>
  s.n === 0 ? `<span class="od-none">none</span>` : s.n === 1 ? `<b>${sig2(s.mid)}</b><span class="ol-cnote">one deposit</span>` : `<b>${sig2(s.mid)}</b><span class="ol-cnote od-range">${sig2(s.lo)} – ${sig2(s.hi)}</span>`;

function gradeBar(g: Record<Grade, number>, label: string): string {
  const parts = GRADES.filter((k) => g[k] >= 0.005)
    .map((k) => `<span class="od-g od-g-${k}" style="flex-grow:${g[k].toFixed(3)}" title="${cap(k)} ${pct(g[k])}">${g[k] >= 0.12 ? pct(g[k]) : ""}</span>`)
    .join("");
  return `<span class="od-bar" role="img" aria-label="${esc(label)}: ${GRADES.map((k) => `${k} ${pct(g[k])}`).join(", ")}">${parts}</span>`;
}

const GRADE_KEY = `<ul class="od-key" aria-label="Grades">${GRADES.map((g) => `<li><span class="od-g od-g-${g}"></span>${g}</li>`).join("")}</ul>`;

const SOURCE_LABEL: Record<Source, string> = { cell: "Ore cell veins", district: "District veins", other: "Stray veins" };
const SOURCE_NOTE: Record<Source, string> = {
  cell: "one per metal per 5 km cell",
  district: "hydrothermal districts, up to 8 per metal each",
  other: "neither a cell's nor near a district",
};

const oreNames = (ds: Deposit[]) => {
  const blocks = new Map<string, number>();
  for (const d of ds) for (const [o, c] of Object.entries(d.ores)) blocks.set(o, (blocks.get(o) ?? 0) + c.reduce((a, b) => a + b, 0));
  const all = [...blocks.values()].reduce((a, b) => a + b, 0);
  return [...blocks.entries()]
    .sort((a, b) => b[1] - a[1])
    .map(([o, n]) => `${ORES[o]?.name ?? o}${ORES[o]?.sulfide ? " (sulfide)" : ""} ${pct(n / all)}`)
    .join(", ");
};

/** 1. The summary: per metal and source, worst · typical · best. */
function renderSummary(): string {
  const head = `<tr><th>Metal<span class="ol-when">deposits in ${SURVEY.areaKm2} km²</span></th><th>Ore blocks</th><th>Grades<span class="ol-when">all blocks</span></th>${LEVELS.map((l) => `<th>${LEVEL_LABEL[l]}<span class="ol-when">${LEVEL_NOTE[l]}</span></th>`).join("")}</tr>`;
  const rows = METALS.flatMap((m) => {
    const groups = (["cell", "district"] as Source[]).map((s) => ({ s, ds: depositsOf(m, s) })).filter((g) => g.ds.length);
    if (!groups.length) return [`<tr><th scope="row"><b>${cap(m)}</b></th><td class="ol-c" colspan="${2 + LEVELS.length}"><span class="od-none">no deposit measured</span>: nickel comes only from mafic districts' veins (no ore cell); see its section.</td></tr>`];
    return groups.map(({ s, ds }, i) => {
      const ts = ds.map(tally);
      const cells = LEVELS.map((l) => `<td class="ol-c od-num${l === "ground" ? " od-ground" : ""}">${sp(spread(ts.map((t) => t.ingots[l])))}</td>`).join("");
      return `<tr${i === 0 ? ' class="od-first"' : ""}><th scope="row"><b>${cap(m)}</b><span class="ol-when">${SOURCE_LABEL[s].toLowerCase()}: ${ds.length}</span></th><td class="ol-c od-num">${sp(spread(ts.map((t) => t.blocks)))}</td><td class="ol-c">${gradeBar(pooledGrades(ds), `${m} ${s}`)}</td>${cells}</tr>`;
    });
  }).join("\n");
  return `${GRADE_KEY}<div class="ol-tablewrap"><table class="ol-table od-table od-summary"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Read:</b> the <b>typical</b> figure is the median, and worst – best under it the 10th and 90th percentiles over that metal's measured deposits (one figure where only one was measured), each column on its own, so the worst ingots are the smallest, poorest deposits and the best the largest, richest. Ingots = blocks × ${UNITS_PER_BLOCK} (${DROPS.raw} raw ore and ${DROPS.crystallised} crystallised ore at twice the units) × the ore's units at the block's grade × the level's share ÷ ${CFG.unitsPerIngot}.</p>`;
}

/** 2. Per metal: where its deposits are, their grades and every deposit's figures. */
function renderMetal(m: Metal): string {
  const ds = depositsOf(m);
  const id = `od-${m}`;
  if (!ds.length) return renderNickel();
  const rows = [...ds]
    .map((d) => ({ d, t: tally(d) }))
    .sort((a, b) => (a.d.source === b.d.source ? b.t.ingots.ground - a.t.ingots.ground : a.d.source.localeCompare(b.d.source)))
    .map(({ d, t }) => {
      const main = mainGrade(t);
      return `<tr><th scope="row">${SOURCE_LABEL[d.source].replace(" veins", "")}<span class="ol-when">seed ${d.seed}; ${d.depth <= 6 ? "at the surface" : `${d.depth} m down`}</span></th><td class="ol-c">${esc(Object.keys(d.ores).map((o) => ORES[o]?.name ?? o).join(", "))}</td><td class="ol-c od-num">${sig2(t.blocks)}</td><td class="ol-c">${gradeBar(t.grades, `deposit`)}<span class="ol-cnote">${main ? `all but ${pct(1 - t.grades[main])} ${main}` : "mixed"}</span></td>${LEVELS.map((l) => `<td class="ol-c od-num${l === "ground" ? " od-ground" : ""}">${sig2(t.ingots[l])}</td>`).join("")}<td class="ol-c od-num">${sig2(rockSlots(t))}<span class="ol-cnote">→ ${sig2(concentrateSlots(t))}</span></td></tr>`;
    })
    .join("\n");
  const head = `<tr><th>Deposit</th><th>Ores</th><th>Blocks</th><th>Grades</th>${LEVELS.map((l) => `<th>${LEVEL_LABEL[l]}</th>`).join("")}<th>Slots<span class="ol-when">rock → concentrate</span></th></tr>`;
  const cell = depositsOf(m, "cell");
  const cellNote = cell.length
    ? `<b>${cell.length}</b> ore cell veins, ${SURVEY.cellDepositsInWindow[m]} cells' deposits fell in the windows: one a 5 km cell, so the nearest is typically 2–3.5 km from any point and 3.4 km from the next one (see method).`
    : "no ore cell vein in the windows.";
  return `<section class="ol-class" id="${id}" aria-labelledby="${id}-h"><h3 id="${id}-h">${cap(m)}</h3>
<p class="ol-ores"><b>Ores measured:</b> ${oreNames(ds)}. ${cellNote} ${METAL_NOTE[m]}</p>
<div class="ol-tablewrap"><table class="ol-table od-table od-each"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
</section>`;
}

/** Notes per metal; the figures they quote are computed. */
const METAL_NOTE: Record<Metal, string> = {
  iron: "Hematite seams (granite, peridotite, limestone, phyllite) are most cell veins, limonite tubes (shale, chert) and magnetite the rest; pyrite, a sulfide, comes from mafic districts. Hematite's units (4 / 6 / 8 / 11) make it worth a little more a block.",
  copper: "Native copper chimneys are most cell veins, and their core runs rich: native copper's density (1.1) lifts the hand and water figures. Chalcopyrite, a sulfide, needs roasting and is poorer.",
  tin: "Cassiterite only: units 1 / 3 / 4 / 6, the poorest per block of any ore, so tin needs about twice the blocks per ingot of copper. Granite districts' tin veins are sized to the targets but can run far over them (method: district oversizing).",
  zinc: "Sphalerite only, a sulfide: nothing smelts until roasted. Smithsonite and hemimorphite are rare tries (one in six chunks) and none reached a deposit here. Most zinc is district ore, in granite and felsic districts.",
  nickel: "",
};

/** Nickel: no measured deposit; what the rules give. */
function renderNickel(): string {
  const odds = veinGradeOdds(4);
  return `<section class="ol-class" id="od-nickel" aria-labelledby="od-nickel-h"><h3 id="od-nickel-h">Nickel</h3>
<p class="ol-ores"><b>Not measured:</b> no nickel ore reached a deposit in the ${SURVEY.areaKm2} km² surveyed. Nickel has <b>no ore cell</b> (the cell rule lists bismuth, borax, coal, copper, iron, lead, platinum, rhodochrosite, tin and zinc), and its only vein, Interesting Ore Gen's pentlandite chimney, has 0 tries a chunk. It comes from <b>mafic districts</b> (one district per 7 km tile at 40 %, about one per 122 km², and a mafic one a share of those), each keeping <b>8</b> pentlandite veins of 27–47.</p>
<p class="ol-ores"><b>Derived:</b> each kept vein is cut to a band of heights drawn for a size log-linear through ${TARGET.nickel.small} / ${TARGET.nickel.typical} / ${TARGET.nickel.large} ingots at the 10th / 50th / 90th percentile (config/ore-districts.json, DistrictVeins.cs). District grades are the poorest nine blocks in ten away from a fault's rich bands, so take about 90 % poor. Pentlandite is a sulfide (default units 3 / 5 / 7 / 10): at that split a typical ${TARGET.nickel.typical}-ingot vein gives about <b>${sig2(DERIVED_NICKEL.mined)}</b> ingots smelted as mined, <b>${sig2(DERIVED_NICKEL.hand)}</b> by hand, <b>${sig2(DERIVED_NICKEL.water)}</b> at water and <b>${sig2(DERIVED_NICKEL.full)}</b> on a full line, from about <b>${sig2(DERIVED_NICKEL.blocks)}</b> blocks. A cell vein's grade odds, for comparison (IOG draws one grade a vein): ${GRADES.map((g, i) => `${g} ${pct(odds[i]!)}`).join(", ")}.</p>
</section>`;
}

/** A pentlandite vein of the typical size at district grades (90 % poor, 10 % medium): derived, not measured. */
export const DERIVED_NICKEL = (() => {
  const unit = tally({ ores: { pentlandite: [0.9, 0.1, 0, 0] } });
  const k = TARGET.nickel.typical / unit.ingots.ground;
  return { blocks: k, mined: unit.ingots.mined * k, hand: unit.ingots.hand * k, water: unit.ingots.water * k, full: unit.ingots.full * k };
})();

/** 3. Effort: rock against concentrate for a typical deposit. */
function renderEffort(): string {
  const head = `<tr><th>Metal</th><th>Rock mined<span class="ol-when">items, typical cell vein</span></th><th>Rock slots<span class="ol-when">at ${CFG.rockStack}</span></th><th>Concentrate slots<span class="ol-when">at ${CFG.concentrateStack}, full line</span></th><th>Ratio</th><th>Ingots a rock slot<span class="ol-when">full line</span></th></tr>`;
  const rows = METALS.filter((m) => depositsOf(m, "cell").length)
    .map((m) => {
      const ts = depositsOf(m, "cell").map(tally);
      const items = spread(ts.map((t) => t.rockItems));
      const rock = spread(ts.map(rockSlots));
      const conc = spread(ts.map((t) => concentrateSlots(t)));
      const per = spread(ts.map((t) => t.ingots.full / rockSlots(t)));
      return `<tr><th scope="row"><b>${cap(m)}</b></th><td class="ol-c od-num">${sp(items)}</td><td class="ol-c od-num">${sp(rock)}</td><td class="ol-c od-num">${sp(conc)}</td><td class="ol-c od-num"><b>${sig2(rock.mid / conc.mid)}×</b></td><td class="ol-c od-num">${sp(per)}</td></tr>`;
    })
    .join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table od-table"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> rock items = blocks × ${ITEMS_PER_BLOCK}; rock slots = items ÷ ${CFG.rockStack}; concentrate slots = the full line's units ÷ ${CFG.concentrateUnits} a concentrate ÷ ${CFG.concentrateStack}. A player's inventory and hotbar are about 40 slots, so a typical deposit is many trips of rock, and one or two of concentrate. Tin's rock is the bulkiest per ingot (cassiterite's units are the lowest).</p>`;
}

/** 4. Need: what a player uses against what a deposit gives. */
function renderNeed(): string {
  const typical = (m: Metal, l: "mined" | "hand" | "water" | "full") =>
    m === "nickel" ? DERIVED_NICKEL[l] : spread(depositsOf(m, "cell").map((d) => tally(d).ingots[l])).mid;
  const head = `<tr><th>Need</th><th>Metal</th><th>Ingots</th>${(["mined", "hand", "water", "full"] as const).map((l) => `<th>Typical deposit ${LEVEL_LABEL[l].toLowerCase()}<span class="ol-when">covers it × times</span></th>`).join("")}</tr>`;
  const rows = NEEDS.map((n) => {
    const cells = (["mined", "hand", "water", "full"] as const).map((l) => `<td class="ol-c od-num">${sig2(typical(n.metal, l) / n.ingots)}<span class="ol-cnote">${sig2(typical(n.metal, l))} ingots</span></td>`).join("");
    return `<tr><th scope="row"><b>${esc(n.what)}</b><span class="ol-when">${esc(n.how)}</span></th><td>${n.metal}</td><td class="ol-c od-num"><b>${sig2(n.ingots)}</b></td>${cells}</tr>`;
  }).join("\n");
  const targets = METALS.map((m) => `${m} ${TARGET[m].typical}`).join(", ");
  return `<div class="ol-tablewrap"><table class="ol-table od-table"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>From the recipes:</b> every tool head is one ingot (its voxels ≤ 42); a cast anvil ${ANVIL.cast}, a smithed one ${ANVIL.smithed} (iron does not cast); a helve hammer head ${HELVE}; a plate 2, a rod 1, a cylinder blank 6, a large gear blank 2, two gear blanks or two pipe sections an ingot. Armour, plates for machines and the pack's pipe network come on top and are not counted. The design's yardstick: a typical deposit holds what a <b>group of about four</b> needs through an age (${targets} ingots in the ground; config/ore-sizes.json). Nickel's typical is derived (its section).</p>`;
}

const OD_STYLE = `<style>
.ore-deposits { --od-g0: color-mix(in srgb, var(--accent) 18%, var(--surface-2)); --od-g1: color-mix(in srgb, var(--accent) 45%, var(--surface-2)); --od-g2: color-mix(in srgb, var(--accent) 72%, var(--surface-2)); --od-g3: var(--accent); }
.od-key { display: flex; flex-wrap: wrap; gap: 0.2rem 0.8rem; list-style: none; padding: 0 !important; margin: 0.3rem 0 !important; font-size: 0.8rem; }
.od-key li { display: flex; align-items: center; gap: 0.3rem; }
.od-key .od-g { width: 0.9rem; height: 0.9rem; border-radius: 2px; display: inline-block; }
.od-bar { display: flex; gap: 1px; min-width: 6.5rem; height: 1.1rem; border-radius: 3px; overflow: hidden; border: 1px solid var(--border); background: var(--bg); }
.od-g { display: flex; align-items: center; justify-content: center; font-size: 0.68rem; line-height: 1; white-space: nowrap; overflow: hidden; }
.od-g-poor { background: var(--od-g0); color: var(--text); }
.od-g-medium { background: var(--od-g1); color: var(--text); }
.od-g-rich { background: var(--od-g2); color: var(--bg); }
.od-g-bountiful { background: var(--od-g3); color: var(--bg); }
.od-table td.od-num { white-space: nowrap; font-variant-numeric: tabular-nums; }
.od-table td.od-ground { background: var(--surface-2); }
.od-none { color: var(--muted); font-style: italic; }
.od-pills { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.3rem 0 0.6rem; padding: 0 !important; list-style: none; font-size: 0.85rem; }
.od-pills li { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.od-pills li b { color: var(--accent); }
.od-method li { margin: 0.2rem 0; font-size: 0.86rem; }
.od-tag { font-size: 0.7rem; border: 1px solid var(--border); border-radius: 999px; padding: 0 0.4rem; margin-right: 0.3rem; white-space: nowrap; }
.od-tag-m { border-color: var(--accent); color: var(--accent); }
.od-range { white-space: nowrap; }
.od-summary tr.od-first th, .od-summary tr.od-first td { border-top-width: 2px; }
@media (max-width: 640px) {
  .od-summary { min-width: 40rem; }
  .od-each { min-width: 46rem; }
  .od-table { min-width: 32rem; }
}
</style>`;

const MEASURED = `<span class="od-tag od-tag-m">measured</span>`;
const DERIVED = `<span class="od-tag">derived</span>`;

function renderMethod(): string {
  const items = [
    `${MEASURED} <b>The survey.</b> <code>tools/ore-survey</code> on main at <code>${esc(SURVEY.commit)}</code> (the pack's mods at their pins, the pack's own mod built from that commit), seeds ${SURVEY.seeds.join(", ")}, each a ${SURVEY.chunks} × ${SURVEY.chunks} chunk square at the map centre (${sig2(SURVEY.areaKm2 / SURVEY.seeds.length)} km², ${SURVEY.areaKm2} km² in all). Every ore block was counted, by ore, grade and 8-block cell.`,
    `${MEASURED} <b>A deposit</b> is the survey's: blocks of one ore touching (8-block cells, 26 neighbours) are a fragment; fragments of 100 blocks or more of one metal, any ore, within 150 m are one deposit. A deposit is a <b>cell vein</b> when the server log's "deposit placed" line for that metal lies within 250 m of it, a <b>district vein</b> when a district that carries the metal was built within 5 km, else stray.`,
    `${MEASURED} <b>Drops</b>: an ore block drops ${DROPS.raw} raw ore and ${DROPS.crystallised} crystallised ore (the survey's dump of the live blocks); each raw ore holds its grade's units (CFG), crystallised ore twice them.`,
    `${DERIVED} <b>Recovery</b> is each block's units × the level's share from ore-line.ts (the settled design): as mined by grade (sulfides as stall-roasted rock), only rich and bountiful blocks for the best case; by hand the rocker on spalled, riddled ore (${pct(CFG.hand.rocker)}, poor × ${CFG.locked.poorUnground} unground), sulfide concentrate × the firepit ${CFG.roasters.firepit}; at water the sluice and jig chained (${pct(lineRecovery(WATER))}) on ground ore, sulfides × the stall ${CFG.roasters.stall}; on the full line all four (${pct(lineRecovery(FULL))}), the reverberatory. Density (native copper 1.1, cassiterite 1.05, smithsonite 0.9) acts on each concentrator, capped at 1. The shipped config differs a little (a tier's single concentrator: jig 80 % at water against the chain's 82 %): about 2.5 % fewer ingots at water.`,
    `${DERIVED} <b>Grades</b> come from Interesting Ore Gen's generator: one grade a vein, drawn as u² × the ore's grade count (poor half the time, then medium 21 %, rich 16 %, bountiful 13 %), clamped to what the host rock allows (hematite in phyllite stops at medium); native copper and cassiterite chimneys put their core at rich (90 % of core blocks); a seam's sub-deposits draw their own. So a cell vein is nearly one grade, and which one is luck. District ore is the poorest grade nine blocks in ten, apart from a fault's rich bands.`,
    `${DERIVED} <b>Spacing</b>: one cell vein per metal per 5 km cell, at a spot at least 500 m inside it, so (simulated over a grid of cells) the next deposit of a metal is 2.6 / 3.4 / 4.7 km away (10th / 50th / 90th) and a random point is 0.9 / 2.1 / 3.3 km from the nearest. A cell can run out of spots and have none (zinc twice and iron once in these windows).`,
    `<b>Caveats.</b> Small samples: a cell's deposit per metal per window, so a handful of cell veins per metal, and the windows cover the cells around the map centre only. <b>Nickel</b> has no deposit here because none of the windows reached a mafic district's pentlandite: a sampling limit, not an absence. The needs are recipe sums for a tool kit, not a full playthrough.`,
    `<b>District oversizing.</b> Granite districts' tin and zinc veins measured up to ${sig2(Math.max(...depositsOf("tin", "district").map((d) => tally(d).ingots.ground)))} tin ingots against the 400-ingot "large" (seed 202: two cassiterite veins of 440,000 and 480,000 blocks, cut by the window's edge, so larger still), and zinc veins up to ${sig2(Math.max(...depositsOf("zinc", "district").map((d) => tally(d).ingots.ground)))} ingots. The likely cause is in DistrictVeinSizes: the granite district's fault rock is granite itself (<code>hostMaterialCode: rock-granite</code>), so nothing cuts an ore shoot to the fault's width as the estimate assumes; it fills its whole ellipse, and #863's widening multiplies both its half strike and half across by 4, sixteen times the area against the four the estimate counts (about 10,000–17,000 ore blocks a layer measured against about 1,100 estimated). A vein whose wanted band is taller than the room under sea level also keeps its whole column (y 0–144 measured), still widened. Worth its own issue.`,
  ];
  return `<ul class="od-method">${items.map((i) => `<li>${i}</li>`).join("")}</ul>`;
}

export default function oreDeposits(): string {
  const jump = [
    ["summary", "Summary"],
    ...METALS.map((m) => [m, cap(m)]),
    ["effort", "Effort: rock against concentrate"],
    ["need", "Against need"],
    ["method", "Method and caveats"],
  ];
  const cellAll = DEPOSITS.filter((d) => d.source === "cell").map(tally);
  const oneGrade = cellAll.filter((t) => mainGrade(t)).length;
  const pills = [
    `<li>ore block → <b>${DROPS.raw}</b> raw ore + ${DROPS.crystallised} crystallised</li>`,
    `<li>units a raw ore <b>${GRADES.map((g) => CFG.units[g]).join(" / ")}</b> (hematite ${GRADES.map((g) => CFG.unitExceptions.hematite![g]).join(" / ")}, cassiterite ${GRADES.map((g) => CFG.unitExceptions.cassiterite![g]).join(" / ")})</li>`,
    `<li>deposits <b>4×</b> IOG's volume</li>`,
    `<li>ingot <b>${CFG.unitsPerIngot}</b> units</li>`,
    `<li><b>${oneGrade} of ${cellAll.length}</b> cell veins are ≥ 80 % one grade</li>`,
  ];
  return `<div class="ore-line ore-deposits">
${STYLE}
${OD_STYLE}
<h2>Deposits</h2>
<p class="ol-intro">How much metal a deposit of iron, copper, tin, zinc or nickel holds as the pack now generates it, how its blocks split by grade, and what each processing level wins from it. Since ${issueLink(847)} and ${issueLink(863)} a raw ore holds about a quarter of vanilla's units and deposits are four times the blocks, so a deposit holds the ingots it did and the processing line decides how many of them come home. Deposit sizes and grades are <b>measured</b> on main; recoveries are <b>derived</b> from those blocks and the processing line's figures.</p>
<ul class="od-pills" aria-label="The rules">${pills.join("")}</ul>
<p class="ol-rule"><b>In short:</b> a cell vein holds one to a few hundred ingots in the ground, and it is nearly all one grade, so it is rich or it is not. Smelted as mined, a rich or bountiful vein gives two fifths to a half of its metal and a poor or medium one next to nothing worth carrying. By hand a deposit yields a quarter (poor) to three fifths (rich), at water about four fifths, on a full line all of it; its rock fills hundreds of slots against tens of concentrate.</p>
<nav class="ol-jump" aria-label="Sections">${jump.map(([id, t]) => `<a href="#od-${id}">${esc(t!)}</a>`).join("")}</nav>

<section class="ol-class" id="od-summary" aria-labelledby="od-summary-h"><h3 id="od-summary-h">Summary: typical, and worst – best</h3>
<p class="ol-ores">Ingots a deposit gives at each level.</p>
${renderSummary()}
</section>

${METALS.map(renderMetal).join("\n")}

<section class="ol-class" id="od-effort" aria-labelledby="od-effort-h"><h3 id="od-effort-h">Effort: rock against concentrate</h3>
<p class="ol-ores">All of a cell vein's ore, mined, against the concentrate it makes on a full line.</p>
${renderEffort()}
</section>

<section class="ol-class" id="od-need" aria-labelledby="od-need-h"><h3 id="od-need-h">Against need</h3>
<p class="ol-ores">How many times a typical cell vein covers a need at each level.</p>
${renderNeed()}
</section>

<section class="ol-class" id="od-method" aria-labelledby="od-method-h"><h3 id="od-method-h">Method and caveats</h3>
${renderMethod()}
</section>
</div>`;
}
