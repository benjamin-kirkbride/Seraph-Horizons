// The ore review page's "Processing line" tab (scripts/standalone/ore-review.json): the settled
// machine design, written for players. Which machines each ore class needs, from the least that
// works to the full line, as a flow of steps with a "smelt here" off-ramp under each; one card per
// machine and hand station (what it is, what it does, why use it, what skipping it costs, where it
// goes, what it needs, what it replaces); how concentrators chain, with the locked-share penalties;
// a glossary; and the open questions. Every figure is computed here from CFG, which mirrors the
// spec (the settled machine specification: fifteen machines and the hand stations) and main's
// config/ore-processing.json after #847 and #863 (rock stacks to 16 and converts 1:1, units 3/5/7/10,
// smelting by grade 10/25/40/50 %, no chunks, no quern). Plain HTML and CSS on the site's colour
// tokens (light and dark), no script.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

export type Grade = "poor" | "medium" | "rich" | "bountiful";
export const GRADES: Grade[] = ["poor", "medium", "rich", "bountiful"];

/** The settled figures: the spec's rules and cards, and ore-processing.json on main. Starting values for playtesting. */
export const CFG = {
  /** What share of its units rock (raw, crushed, ground or roasted rock alike) gives in a furnace, by grade. */
  smeltByGrade: { poor: 0.1, medium: 0.25, rich: 0.4, bountiful: 0.5 } as Record<Grade, number>,
  /** Metal units a rock item holds, by grade, for most ores (patches/oreprocessing-ore.json). */
  units: { poor: 3, medium: 5, rich: 7, bountiful: 10 } as Record<Grade, number>,
  unitExceptions: {
    hematite: { poor: 4, medium: 6, rich: 8, bountiful: 11 },
    "silver quartz": { poor: 2, medium: 4, rich: 6, bountiful: 7 },
    cassiterite: { poor: 1, medium: 3, rich: 4, bountiful: 6 },
    "gold quartz": { poor: 1, medium: 3, rich: 4, bountiful: 6 },
  } as Record<string, Record<Grade, number>>,
  /** Every rock form stacks to 16 and converts one to one (#847, #863); concentrate holds 5 units and stacks to 128. */
  rockStack: 16,
  concentrateStack: 128,
  concentrateUnits: 5,
  unitsPerIngot: 100,
  /** The hand concentrators, which do not chain. */
  hand: { pan: 0.45, rocker: 0.55 },
  /** Each concentrating machine's single-pass recovery r of the catchable units in its feed. */
  chain: { sluice: 0.55, jig: 0.6, table: 0.7, vanner: 0.8 },
  /** Locked shares: applied once to the feed, never per pass. The rest of the feed's metal gravity cannot catch. */
  locked: { unclassified: 0.85, poorUnground: 0.4, freeUnamalgamated: 0.7 },
  /** Amalgamation plates by level: the share of the free metal caught at once, and the factor the rest passes at (in place of × 0.7). */
  plates: { 1: { caught: 0.6, pass: 0.9 }, 2: { caught: 0.65, pass: 1 } } as Record<1 | 2, { caught: number; pass: number }>,
  roasters: { firepit: 0.85, stall: 0.92, reverberatory1: 0.96, reverberatory2: 1 },
  /** The parting stations' figures (the share of the by-product won). */
  parting: { cupel: 0.85, cupellation: 0.95, parkesCupellation: 1, liquationPan: 0.85, liquation: 0.95, acidparting: 0.95 },
  /** Gold quartz's gold smelted with its silver unparted. */
  goldUnparted: 0.85,
  /** Litharge back to lead. */
  litharge: 0.95,
  mercuryReturn: 0.9,
  /** The shaft speed the spec's rates are stated at. */
  referenceSpeed: 0.6,
} as const;

export type Conc = keyof typeof CFG.chain;
export const CONCS: Conc[] = ["sluice", "jig", "table", "vanner"];

export interface Feed {
  /** Went over a screen or the riddle (default true). */
  classified?: boolean;
  /** Poor (fine-grained) ore. */
  poor?: boolean;
  /** Ground in the arrastra or the ball mill. */
  ground?: boolean;
  /** Free gold or silver (gold and silver quartz). */
  free?: boolean;
  /** Worked in the amalgam pan (the hand line); the plates have their own figures. */
  amalgamated?: boolean;
  /** How readily the mineral settles: per pass, on each r, capped at 1. */
  density?: number;
}

/** The share of the feed's units gravity can catch at all: the locked shares, applied once. */
export function catchable(f: Feed): number {
  let c = 1;
  if (f.classified === false) c *= CFG.locked.unclassified;
  if (f.poor && !f.ground) c *= CFG.locked.poorUnground;
  if (f.free && !f.amalgamated) c *= CFG.locked.freeUnamalgamated;
  return c;
}

/** Concentrators in series: 1 − Π(1 − r). A kind used twice counts once (no gain from a machine's own kind). */
export function chain(kinds: Conc[], density = 1): number {
  const used = new Set(kinds);
  let miss = 1;
  for (const k of used) miss *= 1 - Math.min(1, CFG.chain[k] * density);
  return 1 - miss;
}

/** What a chain of machines recovers of the metal in the rock: the catchable share × the chain's figure. */
export function lineRecovery(kinds: Conc[], f: Feed = {}): number {
  return catchable(f) * chain(kinds, f.density);
}

/** A hand concentrator (pan or rocker) on a feed. */
export function handRecovery(station: keyof typeof CFG.hand, f: Feed = {}): number {
  return Math.min(1, CFG.hand[station] * (f.density ?? 1)) * catchable(f);
}

/** Free gold or silver through the plates at a level, then the chain: caught + (1 − caught) × pass × chain. */
export function platesLine(level: 1 | 2, kinds: Conc[], f: Feed = {}): number {
  const p = CFG.plates[level];
  return p.caught + (1 - p.caught) * p.pass * catchable({ ...f, free: false }) * chain(kinds, f.density);
}

/** The rock's smelt share at a grade, through a roaster where the ore is a sulfide. */
export const rockSmelt = (g: Grade, roast = 1): number => CFG.smeltByGrade[g] * roast;

/** Every non-empty sub-chain of the four, by size then in line order. */
export function subchains(): Conc[][] {
  const out: Conc[][] = [];
  for (let mask = 1; mask < 1 << CONCS.length; mask++) out.push(CONCS.filter((_, i) => mask & (1 << i)));
  return out.sort((a, b) => a.length - b.length);
}

export const FULL: Conc[] = ["sluice", "jig", "table", "vanner"];
export const full = (f: Feed = {}) => lineRecovery(FULL, f);

// ---- the machines and the hand stations: one card each

export type Feeding = "continuous" | "batch" | "hand";

export interface Machine {
  id: string;
  name: string;
  /** The real machine and its date. */
  real: string;
  kind: "machine" | "hand";
  feeding: Feeding;
  levels: number;
  /** The card, in plain words. */
  what: string;
  does: string;
  why: string;
  skip: string;
  where: string;
  needs: string;
  replaces: string;
  /** The rate at the reference speed, in words. */
  rate: string;
  issues: number[];
}

const pct = (x: number, d = 1) => `${Math.round(x * 100 * 10 ** d) / 10 ** d}`;
const byGrade = (f: (g: Grade) => number, d = 1) => GRADES.map((g) => pct(f(g), d)).join(" / ");

const L = CFG.locked;
const R = CFG.chain;
const RO = CFG.roasters;
const P = CFG.parting;
const skipConc = (k: Conc) => `${pct(lineRecovery(FULL.filter((x) => x !== k)))} % instead of ${pct(full())} %`;

export const MACHINES: Machine[] = [
  {
    id: "jawbreaker",
    name: "Jaw breaker",
    real: "Blake jaw breaker under a grizzly, 1858",
    kind: "machine",
    feeding: "continuous",
    levels: 1,
    what: "A machine that breaks big rock between a fixed iron jaw and a swinging one, under a grizzly (sloping bars that let the small stuff fall straight through).",
    does: "Raw ore in; crushed ore out, small and big together, ready for the stamps.",
    why: "It keeps stamp batteries fed: 24 rock an hour, enough for four. No metal is lost.",
    skip: `Nothing is lost: the battery takes raw ore itself. Its product is not screened, so it lifts no × ${L.unclassified} on its own: sent straight to a full line it concentrates at ${pct(full({ classified: false }))} %, not ${pct(full())} %.`,
    where: "First in the line: ore from the mine in, the stamp battery after it. Optional.",
    needs: "Axle. Chute in, chute out.",
    replaces: "Spalling with a hammer, for volume.",
    rate: "24 rock an hour; one serves four batteries",
    issues: [728],
  },
  {
    id: "battery",
    name: "Stamp battery",
    real: "wet five-stamp battery with a screen, 1850s; level 2 the Challenge self-feeder, c. 1880",
    kind: "machine",
    feeding: "continuous",
    levels: 2,
    what: "Five heavy iron-shod rods (stamps) lifted by cams and dropped on the ore in an iron box, with water running through. A mesh screen in its front lets the ore out only once it is small enough. Level 2 adds a self-feeder that tips in ore as fast as the stamps can take it.",
    does: "Raw or broken ore in; crushed ore out, classified (all one size), in a stream of water.",
    why: `6 rock an hour (7 with the feeder), unattended, and its screen lifts the unclassified × ${L.unclassified}. No metal is lost.`,
    skip: `Unclassified rock gives up ${pct(1 - L.unclassified, 0)} % of its metal to the concentrators: a full line catches ${pct(full({ classified: false }))} % instead of ${pct(full())} %, a lone jig ${pct(R.jig * L.unclassified)} % instead of ${pct(R.jig)} %. The riddle classifies by hand.`,
    where: "After the jaw breaker (or fed by hand), before the plates or the concentrators. The heart of a mill.",
    needs: "Axle (rectifier), water. Chute in (or by hand), chute out.",
    replaces: "Spalling and the riddle, for volume.",
    rate: "6 rock an hour; 7 with the feeder",
    issues: [728],
  },
  {
    id: "arrastra",
    name: "Arrastra",
    real: "mule-sized arrastra, the oldest grinding mill of the Americas",
    kind: "machine",
    feeding: "batch",
    levels: 1,
    what: "A round stone-paved pit where heavy stones are dragged round and round over the ore by a turning arm.",
    does: "Crushed ore in, by the charge; ground ore out, by hand.",
    why: `It lifts the poor-ore × ${L.poorUnground}: ground poor ore concentrates whole. A small, mule-sized arrastra: a charge of 2 rock in about 7.5 hours, turned by a windmill or a hand crank as easily as by steam. No metal is lost.`,
    skip: `Poor ore that is not ground keeps ${pct(1 - L.poorUnground, 0)} % of its metal locked: a full line catches ${pct(full({ poor: true }))} % of it instead of ${pct(full())} %, a lone jig ${pct(R.jig * L.poorUnground)} % instead of ${pct(R.jig)} %.`,
    where: "After crushing (the battery, or spalling), before the concentrators; poor ore only. The player carries the charge in and out.",
    needs: "Axle, water. Batch, by hand.",
    replaces: "Replaced by the ball mill, which does the same work continuously and also screens.",
    rate: "a charge of 2 rock in 7.5 hours (0.27 an hour); about 22 to keep up with one battery",
    issues: [732],
  },
  {
    id: "ballmill",
    name: "Ball mill",
    real: "Krupp (Gruson) ball mill with screens, 1880s",
    kind: "machine",
    feeding: "continuous",
    levels: 1,
    what: "A short iron drum, turning on its side, half full of iron balls that tumble over the ore and grind it; screens round the drum let out only what is fine enough.",
    does: "Crushed ore (and the table's middlings) in; ground, screened ore out.",
    why: `It lifts the poor-ore × ${L.poorUnground} and the unclassified × ${L.unclassified}, continuously, 6 rock an hour, and it frees the locked metal in the table's middlings. No metal is lost.`,
    skip: `As for the arrastra: unground poor ore gives a full line ${pct(full({ poor: true }))} % instead of ${pct(full())} %; and the table's middlings have nowhere to go (their metal is lost with the tailings).`,
    where: "After the stamp battery and before the concentrators, in the chute line; the table's middlings chute back into its infeed. Optional for ores that are not poor.",
    needs: "Axle, water. Chute in, chute out.",
    replaces: "The arrastra.",
    rate: "6 rock an hour: one battery's poor rock, or one table's middlings with room to spare",
    issues: [732],
  },
  {
    id: "plates",
    name: "Amalgamation plates",
    real: "mercury-dressed copper plate table; level 2 mercury traps",
    kind: "machine",
    feeding: "continuous",
    levels: 2,
    what: "A sloping table of copper plates coated with mercury; the crushed ore washes over it, and grains of gold and silver stick to the mercury. Level 2 adds mercury traps at the foot, boxes that catch the amalgam and mercury the stream scours off.",
    does: "Crushed ore in; the same ore out by chute, less the free gold and silver, which build up on the plates as amalgam for the player to scrape off and take to the still.",
    why: `On gold quartz and silver quartz it catches ${pct(CFG.plates[1].caught, 0)} % of the free metal at once (${pct(CFG.plates[2].caught, 0)} % with the traps) and lifts the free-metal × ${L.freeUnamalgamated} on the rest (to × ${CFG.plates[1].pass}; to × ${CFG.plates[2].pass} with the traps). Other ores pass unchanged.`,
    skip: `Free gold and silver float past the concentrators: a full line catches ${pct(full({ free: true }))} % of gold quartz's gold, against ${pct(platesLine(1, FULL))} % with the plates and ${pct(platesLine(2, FULL))} % with the traps.`,
    where: "Between crushing and concentrating: after the battery (or the ball mill), before the sluice. Only for gold and silver quartz; optional otherwise.",
    needs: "Water, no power. Chute in, chute out; the amalgam by hand.",
    replaces: "The machine form of the amalgam pan.",
    rate: "8 rock an hour: one battery with its feeder",
    issues: [734, 718],
  },
  {
    id: "sluice",
    name: "Sluice",
    real: "riffled plank sluice",
    kind: "machine",
    feeding: "continuous",
    levels: 1,
    what: "A long wooden trough with bars (riffles) across its floor; water carries the crushed ore down it, and the heavy grains settle behind the bars.",
    does: "Crushed or ground ore in; tailings out by chute. Concentrate collects behind the riffles and is cleaned out by hand (32 fit; full, it passes everything on).",
    why: `It catches ${pct(R.sluice, 0)} % of the catchable metal, takes 16 rock an hour (two batteries' worth), and needs no power. The first concentrator most players build.`,
    skip: `The line works without it: jig, table and vanner alone catch ${skipConc("sluice")}.`,
    where: "First of the concentrators, after the plates or the battery; before the jig. Optional.",
    needs: "Water. Chute in, tailings chute out; concentrate by hand.",
    replaces: "The rocker; the jig and the rest work on its tailings.",
    rate: "16 rock an hour; one serves two batteries",
    issues: [734],
  },
  {
    id: "jig",
    name: "Jig",
    real: "Harz plunger jig, 1840s on",
    kind: "machine",
    feeding: "continuous",
    levels: 1,
    what: "A box of water with a sieve in it; a plunger beside the sieve pulses the water up and down through the ore bed, so the heavy grains sink through the sieve and the light ones wash over the top.",
    does: "Crushed or ground ore in; concentrate out of one chute, tailings out of another.",
    why: `It catches ${pct(R.jig, 0)} % of the catchable metal, 8 rock an hour, unattended.`,
    skip: `Sluice, table and vanner catch ${skipConc("jig")}.`,
    where: "After the sluice (on its tailings), before the table. Optional.",
    needs: "Axle, water. Chute in, two chutes out.",
    replaces: "None: it works beside the others, on their tailings.",
    rate: "8 rock an hour",
    issues: [734],
  },
  {
    id: "table",
    name: "Shaking table",
    real: "Wilfley table, 1896",
    kind: "machine",
    feeding: "continuous",
    levels: 1,
    what: "A wide sloping deck with ridges along it, shaken end to end with a slow push and a quick pull-back; the heavy grains walk to the end of the deck while the water washes the light ones off the side.",
    does: "Crushed or ground ore in; three chutes out: concentrate, middlings (grains with metal still locked in rock) and tailings.",
    why: `It catches ${pct(R.table, 0)} % of the catchable metal, 7 rock an hour, and it is the one machine that sends the locked metal (from poor ore not ground, or unclassified feed) out as middlings for the ball mill to free.`,
    skip: `Sluice, jig and vanner catch ${skipConc("table")}, and locked metal is lost with the tailings.`,
    where: "After the jig (on its tailings), before the vanner. Middlings to the ball mill. Optional.",
    needs: "Axle (rectifier), water. Chute in, three chutes out.",
    replaces: "None: it works beside the others.",
    rate: "7 rock an hour",
    issues: [734],
  },
  {
    id: "vanner",
    name: "Vanner",
    real: "Frue vanner, 1874",
    kind: "machine",
    feeding: "continuous",
    levels: 1,
    what: "A slowly climbing rubber belt, shaken from side to side under a spray of water; the heavy grains ride up over the top roller, the light ones wash back down.",
    does: "Crushed or ground ore in; concentrate out of one chute, tailings out of another.",
    why: `The best single concentrator: ${pct(R.vanner, 0)} % of the catchable metal. Slow (3 rock an hour): a battery needs two.`,
    skip: `Sluice, jig and table catch ${skipConc("vanner")}.`,
    where: "Last of the concentrators, on the table's tailings. Its tailings are waste. Optional.",
    needs: "Axle (ratchet), water. Chute in, two chutes out.",
    replaces: "None: it works beside the others.",
    rate: "3 rock an hour; two to a battery",
    issues: [734],
  },
  {
    id: "stall",
    name: "Stall roaster",
    real: "roasting stall, one of a row",
    kind: "machine",
    feeding: "batch",
    levels: 1,
    what: "A three-sided brick bay, open at the front, with a flue at the back to a chimney; ore is heaped in it on a bed of firewood, lit, and left to burn on its own sulfur for days. Stalls stand in rows sharing their walls.",
    does: "Raw or crushed sulfide rock in, by hand; roasted rock out, which smelts.",
    why: `A sulfide does not smelt until roasted. The stall roasts rock in bulk without machinery: a charge of 32 rock in 40 hours, at ${pct(RO.stall, 0)} % (the firepit roasts at ${pct(RO.firepit, 0)} %). Sulfur is first collected here.`,
    skip: `Unroasted sulfide rock smelts to nothing; in the firepit it roasts at ${pct(RO.firepit, 0)} % instead of ${pct(RO.stall, 0)} %.`,
    where: "Straight after the mine (or a breaker): rock in, roasted rock to the furnace. Off the concentrating line: for lump ore, or players not yet concentrating.",
    needs: "A chimney. Batch, by hand.",
    replaces: "Firepit roasting for rock. Concentrate goes to the reverberatory roaster.",
    rate: "a charge of 32 rock (two stacks) in 40 hours, 0.8 an hour; about 8 keep up with one battery",
    issues: [736],
  },
  {
    id: "reverberatory",
    name: "Reverberatory roaster",
    real: "long-bedded reverberatory; level 2 mechanical rabbles, 1890s",
    kind: "machine",
    feeding: "batch",
    levels: 2,
    what: "A long, low brick furnace with a curved roof; a fire at one end throws its flame over a flat hearth where the concentrate lies, and workers turn it with long rakes (rabbles) through side doors. Level 2 fits mechanical rabbles (water-cooled rakes on shafts through the roof, turned by the axle) and chute feeding.",
    does: "Sulfide concentrate in; roasted concentrate out, which smelts whole.",
    why: `It roasts concentrate, which the stall cannot: level 1 at ${pct(RO.reverberatory1, 0)} %, 28 concentrate an hour; level 2 at ${pct(RO.reverberatory2, 0)} %, 76 an hour, unattended.`,
    skip: `Sulfide concentrate does not smelt unroasted; in the firepit it roasts at ${pct(RO.firepit, 0)} %, one item at a time.`,
    where: "After the concentrators, before smelting. Needed for sulfide ores on a concentrating line.",
    needs: "A chimney; coal. Level 1: batch, by hand. Level 2: axle (rectifier), water, chute in, chute out, coal by chute.",
    replaces: "Firepit roasting for concentrate.",
    rate: "level 1, 28 concentrate an hour (serves about five batteries); level 2, 76 (about thirteen)",
    issues: [736],
  },
  {
    id: "cupellation",
    name: "Cupellation furnace",
    real: "English test furnace",
    kind: "machine",
    feeding: "batch",
    levels: 1,
    what: "A small furnace where air is blown across a pool of molten lead lying in a dish of bone ash (the test): the lead burns to litharge and runs off, and the silver stays.",
    does: "Silver-bearing lead in, by hand; a cake of silver, and litharge (which smelts back to lead), out.",
    why: `It parts silver from galena (3 %), argentiferous galena (38 %) and tetrahedrite (5 %), and copper from freibergite (30 %), at ${pct(P.cupellation, 0)} % (the bone-ash cupel at the forge gets ${pct(P.cupel, 0)} %); ${pct(P.parkesCupellation, 0)} % on rich lead from the Parkes kettle. 55 units of lead an hour.`,
    skip: `The by-product is lost: smelted unparted, galena's silver is gone. The bone-ash cupel by hand gets ${pct(P.cupel, 0)} %.`,
    where: "After smelting, after the Parkes kettle if there is one. Optional; for silver.",
    needs: "Blast (the axle drives its blowing cylinder), a chimney, fuel by hand. Batch.",
    replaces: "The bone-ash cupel at the forge.",
    rate: "55 units of lead an hour",
    issues: [740, 722],
  },
  {
    id: "parkes",
    name: "Parkes kettle",
    real: "Parkes desilverizing kettle with a stirrer, 1850s; stirrers 1890s",
    kind: "machine",
    feeding: "batch",
    levels: 1,
    what: "A big iron pot of molten lead over a fire, with a powered stirrer; zinc stirred into the lead takes up its silver and floats to the top as a crust that is skimmed off.",
    does: "Silver-bearing lead and zinc in, by hand; desilverized lead (plain lead) and rich lead (the crust's, carrying the silver) out, by hand.",
    why: `With the cupellation furnace after it, galena's silver comes out whole (${pct(P.parkesCupellation, 0)} %) instead of ${pct(P.cupellation, 0)} %, and most of the lead never needs cupelling. A charge of 2,400 units in 5 hours. Zinc: 1 unit to 100 of lead, ${pct(0.8, 0)} % of it back.`,
    skip: `Cupel the lead directly, at ${pct(P.cupellation, 0)} %.`,
    where: "After smelting, before the cupellation furnace. Optional.",
    needs: "Fire (fuel by hand), a chimney, an axle for the stirrer. Batch.",
    replaces: "None: it works with the cupellation furnace.",
    rate: "a charge of 2,400 units in 5 hours (480 an hour); one serves a works",
    issues: [740],
  },
  {
    id: "liquation",
    name: "Liquation furnace",
    real: "inclined-hearth liquation furnace",
    kind: "machine",
    feeding: "batch",
    levels: 1,
    what: "A small furnace with a sloping iron hearth plate; tin-lead cakes laid on it are heated just enough for the tin to melt and run down into a pot, and the lead stays behind.",
    does: "Tin-lead (from teallite and franckeite) in, by hand; tin and lead out, separately, by hand.",
    why: `It parts teallite's lead (40 %) and franckeite's (30 %) at ${pct(P.liquation, 0)} % (the clay pan by hand gets ${pct(P.liquationPan, 0)} %); 100 units an hour.`,
    skip: `The lead is lost; the clay liquation pan by hand gets ${pct(P.liquationPan, 0)} %.`,
    where: "After smelting. Optional; only for these two ores.",
    needs: "Fire (fuel by hand), a chimney. Batch.",
    replaces: "The clay liquation pan.",
    rate: "100 units an hour",
    issues: [740, 724],
  },
  {
    id: "acid",
    name: "Acid parting vessel",
    real: "nitric acid parting vessel on a sand bath, under a hood",
    kind: "machine",
    feeding: "batch",
    levels: 1,
    what: "A stoneware pot of nitric acid warmed in a tray of hot sand over a small fire, under a brick hood that carries the fumes up the chimney; the acid dissolves the silver out of gold-silver metal and leaves the gold.",
    does: "Doré (gold and silver together) in, by hand; gold and silver out, by hand.",
    why: `Gold quartz's gold smelts at ${pct(CFG.goldUnparted, 0)} % unparted; parted, it comes out whole and its 15 % silver is won, at ${pct(P.acidparting, 0)} %. 10 units an hour.`,
    skip: `Gold quartz's gold at ${pct(CFG.goldUnparted, 0)} %, its silver lost. There is no hand station for acid parting.`,
    where: "After the still (sponge) or the cupel (doré). Optional; gold quartz.",
    needs: "Water, fire (by hand), a fume hood with its chimney. Batch.",
    replaces: "None.",
    rate: "10 units an hour",
    issues: [740],
  },
  // ---- hand stations
  {
    id: "pan",
    name: "Gold pan",
    real: "vanilla",
    kind: "hand",
    feeding: "hand",
    levels: 1,
    what: "A shallow pan swirled in water to wash the light grains away.",
    does: "Gravel or crushed ore in; concentrate out.",
    why: `The first concentrator, from day one: ${pct(CFG.hand.pan, 0)} %.`,
    skip: `Smelting rock instead gives ${byGrade((g) => rockSmelt(g))} % by grade.`,
    where: "At the stream or the placer field.",
    needs: "Water. By hand.",
    replaces: "Replaced by the rocker, then the sluice.",
    rate: "one item a pan at most",
    issues: [716, 701],
  },
  {
    id: "rocker",
    name: "Rocker",
    real: "wooden cradle with a sieve",
    kind: "hand",
    feeding: "hand",
    levels: 1,
    what: "A wooden cradle with a sieve, rocked by hand while water is poured through.",
    does: "Crushed ore or gravel in; concentrate out.",
    why: `${pct(CFG.hand.rocker, 0)} % by hand, against the pan's ${pct(CFG.hand.pan, 0)} %.`,
    skip: `The pan's ${pct(CFG.hand.pan, 0)} %.`,
    where: "After spalling (and the riddle).",
    needs: "Water, poured by bucket while rocking. By hand.",
    replaces: `Replaced by the sluice (the same ${pct(R.sluice, 0)} %, unattended).`,
    rate: "by hand",
    issues: [716],
  },
  {
    id: "riddle",
    name: "Riddle",
    real: "coarse hand sieve",
    kind: "hand",
    feeding: "hand",
    levels: 1,
    what: "A coarse hand sieve.",
    does: "Crushed ore in; classified crushed ore out.",
    why: `It lifts the unclassified × ${L.unclassified}: a rocker gets ${pct(CFG.hand.rocker, 0)} % instead of ${pct(handRecovery("rocker", { classified: false }))} %.`,
    skip: `Unclassified feed concentrates at × ${L.unclassified}.`,
    where: "After spalling, before the rocker.",
    needs: "Nothing. By hand.",
    replaces: "Replaced by the stamp battery's screen and the ball mill's.",
    rate: "by hand",
    issues: [714],
  },
  {
    id: "spalling",
    name: "Spalling",
    real: "a hammer at the rock face",
    kind: "hand",
    feeding: "hand",
    levels: 1,
    what: "Breaking ore with a hammer where it lies, as miners did before stamp mills.",
    does: "Raw ore set on the ground in; crushed ore out, two blows each, one to one.",
    why: "Crushed ore is what the rocker, the riddle and roasting take; nothing is lost.",
    skip: "Raw ore only smelts by grade.",
    where: "First, at the rock face.",
    needs: "A hammer. By hand.",
    replaces: "Replaced by the jaw breaker and the stamp battery.",
    rate: "two blows an ore",
    issues: [747],
  },
  {
    id: "amalgampan",
    name: "Amalgam pan",
    real: "a pan worked with mercury, by hand or from an axle",
    kind: "hand",
    feeding: "batch",
    levels: 1,
    what: "A pan where crushed gold or silver quartz is worked with mercury and water, by hand or turned from an axle below like the quern.",
    does: "Crushed gold or silver quartz and mercury in; amalgam out (to the still).",
    why: `It catches the free gold and silver that floats past a gravity concentrator (× ${L.freeUnamalgamated}): the rocker then gets ${pct(handRecovery("rocker", { free: true, amalgamated: true }), 0)} % of gold quartz instead of ${pct(handRecovery("rocker", { free: true }))} %.`,
    skip: `Free metal at × ${L.freeUnamalgamated}.`,
    where: "After crushing; amalgam to the still.",
    needs: "Water with the charge; an axle optional. Batch.",
    replaces: "Replaced by the amalgamation plates.",
    rate: "by the charge",
    issues: [718],
  },
  {
    id: "firepit",
    name: "Firepit roasting",
    real: "the game's firepit",
    kind: "hand",
    feeding: "hand",
    levels: 1,
    what: "Roasting sulfide in the game's firepit.",
    does: "Sulfide concentrate (or raw and crushed sulfide rock) in; roasted concentrate (or roasted rock) out.",
    why: `A sulfide does not smelt unroasted. ${pct(RO.firepit, 0)} %. No sulfur.`,
    skip: "Sulfide smelts to nothing.",
    where: "Before smelting.",
    needs: "A fire. One item at a time.",
    replaces: `Replaced by the stall roaster (rock, ${pct(RO.stall, 0)} %) and the reverberatory roaster (concentrate, ${pct(RO.reverberatory1, 0)} % and ${pct(RO.reverberatory2, 0)} %).`,
    rate: "one item at a time",
    issues: [720],
  },
  {
    id: "cupel",
    name: "Bone-ash cupel",
    real: "a cupel in crucibulum's forge",
    kind: "hand",
    feeding: "batch",
    levels: 1,
    what: "A small bone-ash dish held hot in crucibulum's forge with the blast gate open.",
    does: "Galena's, tetrahedrite's or freibergite's metal (lead added where needed) in; the by-product (silver, or freibergite's copper) and litharge out.",
    why: `Wins the by-product at ${pct(P.cupel, 0)} %.`,
    skip: "The by-product is lost.",
    where: "After smelting.",
    needs: "The forge at 950 °C, its blast gate (bellows). Batch.",
    replaces: `Replaced by the cupellation furnace (${pct(P.cupellation, 0)} %; ${pct(P.parkesCupellation, 0)} % with the Parkes kettle).`,
    rate: "a charge of 200 units, a minute an ingot",
    issues: [722],
  },
  {
    id: "liquationpan",
    name: "Clay liquation pan",
    real: "a fired clay dish",
    kind: "hand",
    feeding: "batch",
    levels: 1,
    what: "A fired clay dish heated in a firepit or the forge to 240 °C.",
    does: "Teallite's or franckeite's roasted concentrate in; tin and lead out.",
    why: `Wins the lead at ${pct(P.liquationPan, 0)} %.`,
    skip: "The lead is lost (also if the pan passes 327 °C).",
    where: "After roasting.",
    needs: "A fire held between the tin and lead points. Batch.",
    replaces: `Replaced by the liquation furnace (${pct(P.liquation, 0)} %).`,
    rate: "a charge of 100 units",
    issues: [724],
  },
  {
    id: "still",
    name: "Still",
    real: "the game's boiler with a copper condenser beside it: a retort",
    kind: "hand",
    feeding: "batch",
    levels: 1,
    what: "The game's boiler with a copper condenser beside it.",
    does: `Amalgam in; sponge gold or silver, and ${pct(CFG.mercuryReturn, 0)} % of the mercury back, out. Also cinnabar to mercury.`,
    why: "Amalgam does not smelt; sponge smelts whole.",
    skip: "Amalgam is worth nothing in a furnace.",
    where: "After the amalgam pan or the plates.",
    needs: "A fire under the boiler. Batch.",
    replaces: "None.",
    rate: "2 portions of mercury a second",
    issues: [726],
  },
  {
    id: "leaching",
    name: "Barrel leaching",
    real: "a barrel and a cooking pot",
    kind: "hand",
    feeding: "batch",
    levels: 1,
    what: "Soaking raw borax, saltpeter or alum in a barrel and boiling the liquor down in a cooking pot.",
    does: "The raw mineral in; the game's borax, saltpeter or alum out.",
    why: "The raw forms do not work in recipes until leached.",
    skip: "The raw mineral stays raw.",
    where: "Off the metal line.",
    needs: "A barrel, water, a cooking pot over a fire. Batch.",
    replaces: "None.",
    rate: "by the barrel",
    issues: [742],
  },
];

export const machine = (id: string): Machine => {
  const m = MACHINES.find((x) => x.id === id);
  if (!m) throw new Error(`no machine ${id}`);
  return m;
};

// ---- which machines do I need: the lines, per ore class

export type Exit =
  /** Rock: smelts at the grade's share (× a roaster's figure for roasted sulfide rock). */
  | { kind: "grade"; factor?: number; label?: string }
  /** Concentrate or sponge: a share of the metal in the rock, cumulative to here. */
  | { kind: "share"; value: number; label?: string; alt?: { label: string; value: number }[] }
  /** Nothing smelts here. */
  | { kind: "none"; text: string }
  /** Goes on to another step, not a furnace. */
  | { kind: "on"; text: string };

export interface Step {
  /** The machine's id (a card), or none for a stage with no machine (mined, smelt). */
  m?: string;
  stage?: string;
  note?: string;
  optional?: boolean;
  exit: Exit;
}

export interface Line {
  title: string;
  steps: Step[];
}

export interface OreClass {
  id: string;
  title: string;
  ores: string;
  lede: string;
  minimal: Line[];
  full: Line;
  /** What the full line adds. */
  adds: string;
}

const MINED = (exit: Exit = { kind: "grade" }): Step => ({ stage: "Mined", note: "raw ore, the grade's units, 16 a stack", exit });
const SMELT: Step = { stage: "Smelt", note: "any furnace: crucible, crucibulum, crucible furnace, bloomery, smex", exit: { kind: "on", text: "metal" } };
const grade = (label?: string, factor?: number): Exit => ({ kind: "grade", factor, label });
const share = (value: number, label?: string, alt?: { label: string; value: number }[]): Exit => ({ kind: "share", value, label, alt });
const none = (text: string): Exit => ({ kind: "none", text });
const on = (text: string): Exit => ({ kind: "on", text });

/** The four concentrators as line steps, each exit the chain's cumulative figure on the feed. */
function concSteps(f: Feed, exitOf: (kinds: Conc[]) => Exit = (kinds) => share(lineRecovery(kinds, f), "the concentrate so far")): Step[] {
  const sofar: Conc[] = [];
  return CONCS.map((k) => {
    sofar.push(k);
    return { m: k, optional: true, note: k === "table" ? "middlings to the ball mill" : undefined, exit: exitOf([...sofar]) };
  });
}

const sulfideConc = (kinds: Conc[]): Exit => none(`sulfide concentrate does not smelt until roasted: the firepit gives ${pct(lineRecovery(kinds) * RO.firepit)} %`);

export const CLASSES: OreClass[] = [
  {
    id: "oxide",
    title: "Oxide and carbonate ores",
    ores: "Hematite, magnetite, limonite, malachite, azurite, cassiterite, chromite, ilmenite, smithsonite, hemimorphite, cerussite, vanadinite, wulfenite, rhodochrosite, uranium. Native copper and native platinum behave alike (native, but not free metal: no amalgamation).",
    lede: `The plain case: the rock smelts by grade as it is, or the concentrate smelts whole. Every step before the concentrators is lossless, and the rock still smelts at its grade's share after any of them.`,
    minimal: [
      { title: "Smelt the rock", steps: [MINED(), SMELT] },
      {
        title: "By hand",
        steps: [
          MINED(),
          { m: "spalling", exit: grade("still rock") },
          { m: "riddle", optional: true, note: `skip it: × ${L.unclassified} at the rocker`, exit: grade("still rock") },
          { m: "rocker", exit: share(handRecovery("rocker"), "the concentrate", [{ label: "unclassified", value: handRecovery("rocker", { classified: false }) }, { label: "poor, unground", value: handRecovery("rocker", { poor: true }) }]) },
          SMELT,
        ],
      },
    ],
    full: {
      title: "The full line",
      steps: [
        MINED(),
        { m: "jawbreaker", optional: true, exit: grade("still rock") },
        { m: "battery", exit: grade("still rock") },
        { m: "ballmill", optional: true, note: "poor ore, and the table's middlings", exit: grade("still rock") },
        ...concSteps({}),
        SMELT,
      ],
    },
    adds: `Volume: unattended chutes, 6 rock an hour a battery, and ${pct(full())} % instead of the rocker's ${pct(CFG.hand.rocker, 0)} %.`,
  },
  {
    id: "sulfide",
    title: "Sulfide ores",
    ores: "Galena, argentiferous galena, chalcopyrite, chalcocite, tetrahedrite, freibergite, sphalerite, pentlandite, bismuthinite, pyrite, teallite, franckeite, sperrylite.",
    lede: "Unroasted, nothing smelts: not the rock, not the concentrate. Roast the rock in a stall and smelt it by grade, or concentrate it and roast the concentrate.",
    minimal: [
      {
        title: "Roast the rock",
        steps: [
          MINED(none("unroasted sulfide rock does not smelt")),
          { m: "stall", note: "raw or crushed rock, by the charge", exit: grade(`roasted rock: the grade's share × ${pct(RO.stall, 0)} %`, RO.stall) },
          SMELT,
        ],
      },
      {
        title: "By hand",
        steps: [
          MINED(none("unroasted sulfide rock does not smelt")),
          { m: "spalling", exit: none(`still unroasted; the firepit roasts rock at ${pct(RO.firepit, 0)} %: ${byGrade((g) => rockSmelt(g, RO.firepit))} % by grade`) },
          { m: "riddle", optional: true, exit: none("still unroasted rock") },
          { m: "rocker", exit: none("sulfide concentrate does not smelt until roasted") },
          { m: "firepit", exit: share(handRecovery("rocker") * RO.firepit, `${pct(CFG.hand.rocker, 0)} % × ${pct(RO.firepit, 0)} %`) },
          SMELT,
        ],
      },
    ],
    full: {
      title: "The full line",
      steps: [
        MINED(none("unroasted sulfide rock does not smelt")),
        { m: "jawbreaker", optional: true, exit: none("rock: a stall roasts it") },
        { m: "battery", exit: none("rock: a stall roasts it") },
        { m: "ballmill", optional: true, note: "poor ore, and the table's middlings", exit: none("rock: a stall roasts it") },
        ...concSteps({}, sulfideConc),
        {
          m: "reverberatory",
          exit: share(full() * RO.reverberatory2, `level 2, ${pct(RO.reverberatory2, 0)} %`, [
            { label: `level 1, ${pct(RO.reverberatory1, 0)} %`, value: full() * RO.reverberatory1 },
            { label: `firepit, ${pct(RO.firepit, 0)} %`, value: full() * RO.firepit },
          ]),
        },
        SMELT,
      ],
    },
    adds: `The reverberatory roasts concentrate, which stalls never take: ${pct(full() * RO.reverberatory2)} % against the stall's ${byGrade((g) => rockSmelt(g, RO.stall))} % by grade. Six of these ores carry a second metal: see the by-product ores.`,
  },
  {
    id: "native",
    title: "Free gold and silver: gold quartz and silver quartz",
    ores: `Gold quartz (with 15 % silver, won only by acid parting; its gold smelts at ${pct(CFG.goldUnparted, 0)} % unparted) and silver quartz. Free metal floats past a gravity concentrator (× ${L.freeUnamalgamated}) unless mercury catches it first.`,
    lede: "Amalgamation is the step that matters: the pan by hand, the plates on a line. Amalgam does not smelt; the still turns it into sponge, which does.",
    minimal: [
      {
        title: "By hand",
        steps: [
          MINED(),
          { m: "spalling", exit: grade("still rock") },
          { m: "riddle", optional: true, exit: grade("still rock") },
          { m: "amalgampan", optional: true, note: `skip it: × ${L.freeUnamalgamated} at the rocker`, exit: on("amalgam to the still; the rest to the rocker") },
          { m: "rocker", exit: share(handRecovery("rocker", { free: true, amalgamated: true }), "with the pan", [{ label: "without the pan", value: handRecovery("rocker", { free: true }) }]) },
          { m: "still", exit: share(handRecovery("rocker", { free: true, amalgamated: true }), "sponge smelts whole") },
          SMELT,
        ],
      },
    ],
    full: {
      title: "The full line",
      steps: [
        MINED(),
        { m: "jawbreaker", optional: true, exit: grade("still rock") },
        { m: "battery", exit: grade("still rock") },
        { m: "plates", note: `level 1 keeps ${pct(CFG.plates[1].caught, 0)} % of the free metal on the plates at once, level 2 ${pct(CFG.plates[2].caught, 0)} %`, exit: on("amalgam to the still; the pulp on to the sluice") },
        ...concSteps({ free: true }, (kinds) =>
          share(platesLine(2, kinds), "plates level 2, so far", [
            { label: "plates level 1", value: platesLine(1, kinds) },
            { label: "no plates", value: lineRecovery(kinds, { free: true }) },
          ]),
        ),
        { m: "still", exit: share(platesLine(2, FULL), "sponge and concentrate together", [{ label: "plates level 1", value: platesLine(1, FULL) }]) },
        SMELT,
      ],
    },
    adds: `The plates: without them the line catches ${pct(full({ free: true }))} %; with them ${pct(platesLine(1, FULL))} %, ${pct(platesLine(2, FULL))} % with the traps. Gold quartz's silver: the acid parting vessel.`,
  },
  {
    id: "placer",
    title: "Placer gravel",
    ores: "Gravel from the placer fields: free grains already, so no crushing (placer gravel skips the stamps) and no furnace. The line puts out nuggets.",
    lede: "The same concentrators as a rock line, fed gravel. Figures are of the gravel's metal.",
    minimal: [
      { title: "Gold pan", steps: [{ stage: "Dug", note: "a gravel block", exit: on("gravel") }, { m: "pan", exit: share(CFG.hand.pan, "nuggets") }] },
      { title: "Rocker", steps: [{ stage: "Dug", note: "a gravel block", exit: on("gravel") }, { m: "rocker", exit: share(CFG.hand.rocker, "nuggets") }] },
    ],
    full: {
      title: "The full line",
      steps: [{ stage: "Dug", note: "a gravel block", exit: on("gravel, by chute") }, ...concSteps({}, (kinds) => share(lineRecovery(kinds), "nuggets so far"))],
    },
    adds: "Throughput, unattended.",
  },
];

// ---- by-product ores: parting

export interface Parted {
  ore: string;
  main: string;
  byProduct: string;
  /** Share of the main metal's recovered units. */
  share: number;
  hand: { m: string; value: number } | null;
  machine: { m: string; value: number; with?: { m: string; value: number } };
  unparted: string;
  issues: number[];
}

export const PARTED: Parted[] = [
  { ore: "Galena", main: "lead", byProduct: "silver", share: 0.03, hand: { m: "cupel", value: P.cupel }, machine: { m: "cupellation", value: P.cupellation, with: { m: "parkes", value: P.parkesCupellation } }, unparted: "lead; the silver is lost", issues: [690, 722] },
  { ore: "Argentiferous galena", main: "lead", byProduct: "silver", share: 0.38, hand: { m: "cupel", value: P.cupel }, machine: { m: "cupellation", value: P.cupellation, with: { m: "parkes", value: P.parkesCupellation } }, unparted: "lead; the silver is lost", issues: [690, 722] },
  { ore: "Tetrahedrite", main: "copper", byProduct: "silver", share: 0.05, hand: { m: "cupel", value: P.cupel }, machine: { m: "cupellation", value: P.cupellation }, unparted: "copper; the silver is lost. The cupel takes a unit of lead an ore unit", issues: [722] },
  { ore: "Freibergite", main: "silver", byProduct: "copper", share: 0.3, hand: { m: "cupel", value: P.cupel }, machine: { m: "cupellation", value: P.cupellation }, unparted: "silver; the copper is lost. Lead added as for tetrahedrite", issues: [722, 740] },
  { ore: "Teallite", main: "tin", byProduct: "lead", share: 0.4, hand: { m: "liquationpan", value: P.liquationPan }, machine: { m: "liquation", value: P.liquation }, unparted: "tin; the lead is lost", issues: [724] },
  { ore: "Franckeite", main: "tin", byProduct: "lead", share: 0.3, hand: { m: "liquationpan", value: P.liquationPan }, machine: { m: "liquation", value: P.liquation }, unparted: "tin; the lead is lost", issues: [724] },
  { ore: "Gold quartz", main: "gold", byProduct: "silver", share: 0.15, hand: null, machine: { m: "acid", value: P.acidparting }, unparted: `gold at ${pct(CFG.goldUnparted, 0)} %; the silver is lost`, issues: [740] },
];

// ---- glossary and open questions

export const GLOSSARY: [string, string][] = [
  ["Amalgam", "Gold or silver dissolved in mercury, a soft grey paste; heated in the still, the mercury boils off and leaves the metal as sponge."],
  ["Amalgamation", "Catching free gold or silver with mercury (on plates or in the amalgam pan)."],
  ["Batch", "Loaded, worked and emptied by hand, one charge at a time; no chutes."],
  ["Blast", "Air blown into a furnace by a machine (a blowing cylinder, fan or bellows) rather than drawn by the chimney."],
  ["By-product", "A second metal in an ore (galena's silver, teallite's lead), won only at a parting step; smelted plain, it is lost."],
  ["Chain", "Concentrators in series, each working on the tailings of the one before."],
  ["Charge", "One load of a batch machine."],
  ["Chute", "The game's straight chute section, copper; how continuous machines pass items on. Every arrow on a line is a chute the player lays."],
  [
    "Classified",
    `Sorted to an even grain size, by a screen (the stamp battery's, the ball mill's) or the riddle. Unclassified feed concentrates at × ${L.unclassified}.`,
  ],
  ["Concentrate", `What a concentrator keeps: the heavy, metal-bearing grains, ${CFG.concentrateUnits} units an item, ${CFG.concentrateStack} a stack. It smelts whole (a sulfide's once roasted).`],
  ["Concentrator", "A machine that separates the heavy grains from the light by water: sluice, jig, table, vanner."],
  ["Continuous", "Fed and emptied by chute while it runs, unattended."],
  ["Crushed rock", "Ore broken small (the crushed ore item); holds all its metal, one item to one raw ore."],
  ["Cupel, cupellation", "Parting silver from lead by blowing air over the molten lead on a bed of bone ash: the lead turns to litharge and runs off or soaks in, and the silver stays."],
  ["Doré", "Gold and silver together, as they come from the still or the cupel, before acid parting."],
  ["Free metal", `Gold or silver in grains of the metal itself (gold and silver quartz). Gravity catches only ${pct(L.freeUnamalgamated, 0)} % of it unless it has been amalgamated.`],
  ["Gangue", "The worthless rock round the ore mineral."],
  ["Grade", `How rich an ore item is: poor ${CFG.units.poor}, medium ${CFG.units.medium}, rich ${CFG.units.rich}, bountiful ${CFG.units.bountiful} units of metal (most ores).`],
  ["Grizzly", "Sloping iron bars over a breaker's mouth: the fines fall through, the big rock slides into the jaws."],
  ["Ground rock", "Crushed ore ground fine (the ground ore item); lifts the poor-ore penalty."],
  ["Hutch", "The bottom box of a jig, where the concentrate collects."],
  ["Liquation", "Parting two metals by heat: tin melts at 232 °C and runs off, lead (327 °C) stays."],
  ["Litharge", `Lead oxide, the lead that cupellation burns off; smelts back to lead (${pct(CFG.litharge, 0)} %).`],
  [
    "Locked share",
    "Metal gravity cannot catch, because it is still locked in the rock (poor ore unground) or in grains of the wrong size (unclassified). It leaves at the table's middlings.",
  ],
  ["Middlings", "Grains between concentrate and tailings: rock with metal still locked in it. The table sends them out on their own; the ball mill regrinds them."],
  ["Parting", "Separating a by-product metal from the main one: cupellation, liquation, acid parting."],
  ["Poor-ore penalty", `Poor (fine-grained) ore not ground concentrates at × ${L.poorUnground}.`],
  ["Pulp", "Crushed rock and water flowing together through the wet machines."],
  ["Rabble", "A rake that turns and moves the charge on a roasting hearth; by hand through a work door, or on a spindle by machine."],
  ["Rectifier", "A closed gear case that turns its output shaft one way whichever way the axle turns."],
  ["Riffle", "A bar across the floor of a sluice or a table that the heavy grains settle behind."],
  ["Roasting", "Heating a sulfide in air to burn its sulfur off; until roasted, a sulfide does not smelt."],
  ["Roasted rock, roasted concentrate", "A sulfide's rock or concentrate after roasting."],
  ["Screen", "A mesh the crushed ore passes through only once it is small enough."],
  ["Sponge", "The gold or silver left in the still when the mercury has boiled off an amalgam; smelts whole."],
  ["Stamp", "A heavy iron-shod rod lifted by a cam and dropped on the ore."],
  ["Sulfide", "An ore whose metal is bound to sulfur (galena, chalcopyrite and the rest); must be roasted."],
  ["Tailings", "What a concentrator lets go: the light grains, with whatever metal it missed. The next concentrator works on them."],
  ["Test", "The oval bone-ash dish the lead lies on in a cupellation furnace."],
  ["Tuyere", "The nozzle that blows the blast into a furnace."],
  ["Units", `The metal an item holds; ${CFG.unitsPerIngot} units make an ingot.`],
];

/** The spec's open questions that remain (its settled ones: load constant once assembled; rate in proportion to speed, uncapped; the mule-sized arrastra; no design plant). */
export const OPEN_QUESTIONS: string[] = [
  "Each machine's build stages (frame recipe, stages, materials), settled per machine with the owner while it is modelled, as the crucible furnace's were.",
  "The sluice's concentrate: held in the riffles and cleaned up by hand (recommended, the period way), or a concentrate chute like the jig's?",
  "The locked share (recommended): confirm the poor-ore and unclassified factors apply once, as units gravity cannot catch that leave at the table's middlings, rather than on every pass.",
  "The parting furnaces' items: what form each takes and gives (roasted concentrate as the hand cupel and pan take now, or silver-lead, tin-lead and doré items), settled in the behaviour issues (#740).",
  "The scale: 6 rock an hour for one battery sets every rate. The boiler does not force it: what it sets is coal an ingot (1.7 on medium ore) and water an hour. A playtest may move it; the ratios stay.",
  "The power scale: 0.1 resistance a period horsepower puts the battery (0.75), breaker (0.8) and ball mill (1.1) at two to six times the pack's other machines (bucking sawmill 0.17, draw bench 0.2 and 0.35, borer 0.3), beyond a vanilla windmill, a water wheel or the hand crank. Only steam or a Millwright double rotor runs a line. Accept?",
  "Water at a twelfth of the period's mass (recommended), so one MP pump waters a line and its boiler; at the period's mass a line wants about 95 L/s, past a Cornish engine's fluid pump.",
  "A wet machine with no water stops (recommended), or runs with a penalty as the rosser's dry bark roll does.",
  "The cupellation blast from its own blowing cylinder on an axle (as specified), or from Steelmaking Expanded's air blower on a steam engine, piped to the tuyere (the period's blast came from blowers).",
  "ppex's handbook says its engine fluid pump moves \"about 5 L/s\", twice overclocked; the code and config move 100 L/s a unit of power (20, 40, 80 on a Cornish). A pack lang fix, if wanted.",
  "A pressure valve for a Cornish engine's run (recommended): let a pressure valve be set to 8 atm, by raising BronzeBurstPressure or restoring ppex's steel pressure valve. Without it the pack has no way to guard a run at 6 to 8 atm: setup B cannot be built, and setup C holds only on an exact 48 L/s balance with the boiler fed at 1 atm. Or keep the 5 atm ceiling and teach C's balance in the handbook.",
];

// ---- rendering

const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";
const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
const issueLinks = (ns: number[]) => `<span class="ol-issues">${ns.map(issueLink).join(" ")}</span>`;
/** "#123" in prose becomes a link. */
const linked = (s: string) => esc(s).replace(/#(\d{3,4})\b/g, (_m, n: string) => issueLink(Number(n)));
const cardLink = (id: string, text: string) => `<a class="ol-mlink" href="#ol-m-${id}">${esc(text)}</a>`;
const FEEDING: Record<Feeding, string> = { continuous: "continuous", batch: "batch", hand: "by hand" };
const feedTag = (f: Feeding) => `<span class="ol-tag ol-tag-${f}">${FEEDING[f]}</span>`;

function exit(e: Exit): string {
  if (e.kind === "grade") {
    const figs = byGrade((g) => rockSmelt(g, e.factor));
    return `<div class="ol-exit ol-exit-smelt"><b>Smelt here</b> <span class="ol-exit-fig">${figs} %</span> by grade${e.label ? `<span class="ol-exit-note">${linked(e.label)}</span>` : ""}</div>`;
  }
  if (e.kind === "share") {
    const alts = e.alt?.map((a) => `${esc(a.label)} ${pct(a.value)} %`).join("; ");
    return `<div class="ol-exit ol-exit-smelt"><b>Smelt here</b> <span class="ol-exit-fig">${pct(e.value)} %</span>${e.label ? ` ${linked(e.label)}` : ""}${alts ? `<span class="ol-exit-note">${alts}</span>` : ""}</div>`;
  }
  if (e.kind === "none") return `<div class="ol-exit ol-exit-none"><b>No smelt</b> ${linked(e.text)}</div>`;
  return `<div class="ol-exit ol-exit-on"><b>On</b> ${linked(e.text)}</div>`;
}

function step(s: Step, i: number): string {
  const m = s.m ? machine(s.m) : null;
  const title = m ? cardLink(m.id, m.name) : esc(s.stage ?? "");
  const tags = `${m ? feedTag(m.feeding) : ""}${s.optional ? '<span class="ol-tag ol-tag-opt">optional</span>' : ""}`;
  return `<div class="ol-step${m?.kind === "hand" ? " ol-step-hand" : ""}"><div class="ol-step-n">${i + 1}</div><div class="ol-stage">${title}</div>${tags ? `<div class="ol-opt">${tags}</div>` : ""}${s.note ? `<div class="ol-dev">${linked(s.note)}</div>` : ""}${exit(s.exit)}</div>`;
}

const strip = (steps: Step[]) => `<div class="ol-strip">${steps.map((s, i) => step(s, i)).join('<div class="ol-arrow" aria-hidden="true"></div>')}</div>`;

export function renderClass(c: OreClass): string {
  return `<section class="ol-class" id="ol-${c.id}" aria-labelledby="ol-${c.id}-h">
<h3 id="ol-${c.id}-h">${esc(c.title)}</h3>
<p class="ol-ores">${linked(c.ores)}</p><p class="ol-lede">${linked(c.lede)}</p>
${c.minimal.map((l) => `<h4>Least that works: ${esc(l.title)}</h4>${strip(l.steps)}`).join("\n")}
<h4>${esc(c.full.title)}</h4>${strip(c.full.steps)}
<p class="ol-adds"><b>What the full line adds:</b> ${linked(c.adds)}</p>
</section>`;
}

export function renderParting(): string {
  const head = `<tr><th>Ore</th><th>Main metal</th><th>By-product</th><th>By hand</th><th>Machine</th><th>Smelted unparted</th></tr>`;
  const fig = (p: Parted, s: { m: string; value: number } | null) =>
    s ? `<span class="ol-f ol-f-best"><b>${pct(p.share * s.value, 2)} %</b> of the main metal</span><span class="ol-cnote">${cardLink(s.m, machine(s.m).name)} ${pct(s.value, 0)} % × ${pct(p.share, 0)} %</span>` : `<span class="ol-c-none">no hand station: lost</span>`;
  const rows = PARTED.map(
    (p) =>
      `<tr><th scope="row"><b>${esc(p.ore)}</b> ${issueLinks(p.issues)}</th><td>${esc(p.main)}</td><td>${esc(p.byProduct)} <b>${pct(p.share, 0)} %</b></td><td class="ol-c">${fig(p, p.hand)}</td><td class="ol-c">${fig(p, p.machine)}${p.machine.with ? `<span class="ol-cnote">with the ${cardLink(p.machine.with.m, machine(p.machine.with.m).name)} first: <b>${pct(p.share * p.machine.with.value, 2)} %</b></span>` : ""}</td><td class="ol-unparted">${linked(p.unparted)}</td></tr>`,
  ).join("\n");
  const flow: Step[] = [
    { stage: "Smelt", note: "the main metal, as above; the by-product rides in it", exit: on("silver-lead, tin-lead or doré, carried by hand") },
    { m: "parkes", optional: true, note: "silver-lead only", exit: on("rich lead to the cupellation furnace; plain lead") },
    { m: "cupellation", note: "silver-lead; or the liquation furnace for tin-lead, the acid parting vessel for doré", exit: share(P.parkesCupellation, "of the by-product, with the kettle", [{ label: "without", value: P.cupellation }, { label: "the hand stations", value: P.cupel }]) },
  ];
  return `<section class="ol-class" id="ol-parting" aria-labelledby="ol-parting-h">
<h3 id="ol-parting-h">By-product ores: the parting step</h3>
<p class="ol-ores">Six ores carry a second metal, won only at a parting step after smelting. Smelted plain, they give the main metal and the by-product is lost. The by-product is a share of the main metal's recovered units, so it rides on the line's figure above: parting never changes the main metal's recovery. Cupellation's lead soaks into the cupel as litharge, which smelts back to lead at ${pct(CFG.litharge, 0)} %.</p>
<h4>Least that works: the hand stations. The full works: one of each furnace serves a whole works</h4>
${strip(flow)}
<div class="ol-tablewrap"><table class="ol-table ol-table-parting"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> by-product units = main metal units recovered × share × the parting station's figure (ore-processing.json: ores.*.byProducts; the spec's cards 12 to 15). Worked: 100 units of lead from argentiferous galena cupelled by hand gives 100 × ${pct(0.38, 0)} % × ${pct(P.cupel, 0)} % = ${Math.round(100 * 0.38 * P.cupel * 10) / 10} units of silver; in the cupellation furnace ${Math.round(100 * 0.38 * P.cupellation * 10) / 10}; through the Parkes kettle ${Math.round(100 * 0.38 * P.parkesCupellation * 10) / 10}.</p>
</section>`;
}

export function renderCards(): string {
  const card = (m: Machine) =>
    `<article class="ol-card${m.kind === "hand" ? " ol-card-hand" : ""}" id="ol-m-${m.id}"><h4>${esc(m.name)} ${feedTag(m.feeding)}${m.levels > 1 ? `<span class="ol-tag ol-tag-lv">${m.levels} levels</span>` : ""} ${issueLinks(m.issues)}</h4><p class="ol-real">${esc(m.real)}</p>
<dl><dt>What it is</dt><dd>${linked(m.what)}</dd><dt>What it does</dt><dd>${linked(m.does)}</dd><dt>Why use it</dt><dd>${linked(m.why)}</dd><dt>If you skip it</dt><dd>${linked(m.skip)}</dd><dt>Where it goes</dt><dd>${linked(m.where)}</dd><dt>Needs</dt><dd>${linked(m.needs)}</dd><dt>Rate</dt><dd>${linked(m.rate)}</dd><dt>Replaces</dt><dd>${linked(m.replaces)}</dd></dl></article>`;
  const machines = MACHINES.filter((m) => m.kind === "machine");
  const hands = MACHINES.filter((m) => m.kind === "hand");
  return `<section class="ol-class" id="ol-machines" aria-labelledby="ol-machines-h"><h3 id="ol-machines-h">The machines: one card each</h3>
<p class="ol-ores">${machines.length} machines, each one real piece of equipment (a level is parts fitted to the same equipment). Continuous machines take a chute in and give chutes out, unattended; batch machines are loaded and emptied by hand. Rates are at the reference shaft speed ${CFG.referenceSpeed}; a faster or slower shaft runs them in proportion (the power section on the next tab).</p>
<div class="ol-cards">${machines.map(card).join("\n")}</div>
<h3 id="ol-hand-h" style="margin-top:1rem">The hand stations</h3>
<p class="ol-ores">Worked by the player, no automation; unchanged by the machine spec. They do not chain: a rocker after a pan catches nothing more.</p>
<div class="ol-cards">${hands.map(card).join("\n")}</div>
</section>`;
}

export function renderChains(): string {
  const names = (ks: Conc[]) => (ks.length === 4 ? "all four" : ks.join(" + "));
  const subs = subchains();
  const col = (n: number[]) => subs.filter((s) => n.includes(s.length)).map((s) => `<li${s.length === 4 ? ' class="ol-sub-all"' : ""}>${names(s)} <b>${pct(chain(s))} %</b></li>`).join("");
  const feeds: [string, Feed][] = [
    ["fully catchable feed (classified, ground where poor, amalgamated)", {}],
    ["unclassified (no screen, no riddle)", { classified: false }],
    ["poor ore, not ground", { poor: true }],
    ["poor, unground and unclassified", { poor: true, classified: false }],
    ["free gold or silver, not amalgamated", { free: true }],
  ];
  const lines: [string, Conc[]][] = [
    ["jig alone", ["jig"]],
    ["sluice + jig", ["sluice", "jig"]],
    ["jig + table + vanner", ["jig", "table", "vanner"]],
    ["all four", FULL],
  ];
  const head = `<tr><th>Feed</th><th>Catchable</th>${lines.map(([l]) => `<th>${esc(l)}</th>`).join("")}</tr>`;
  const rows = feeds
    .map(([label, f]) => `<tr><th scope="row">${esc(label)}</th><td class="ol-c"><b>× ${catchable(f)}</b></td>${lines.map(([, ks]) => `<td class="ol-c"><span class="ol-f ol-f-best"><b>${pct(lineRecovery(ks, f))} %</b></span></td>`).join("")}</tr>`)
    .join("\n");
  const twoVanners = 1 - (1 - R.vanner) ** 2;
  return `<section class="ol-class" id="ol-chains" aria-labelledby="ol-chains-h"><h3 id="ol-chains-h">How concentrators chain</h3>
<p class="ol-lede">Each concentrating machine works on what the one before it missed. A sluice, jig, table or vanner recovers its figure <i>r</i> of the catchable units in its feed, and its tailings carry the rest on. In series the losses multiply:</p>
<p class="ol-rule">line recovery = 1 − (1 − r₁)(1 − r₂)…(1 − rₙ)</p>
<p class="ol-lede">The order does not change the product, so any sub-chain in any order works (the period's order, by grain size, is sluice, jig, table, vanner). <b>A machine recovers nothing from its own kind's tailings</b>: what a vanner missed is what a vanner cannot catch (the tailings item records which kinds have worked it). Without that rule two vanners in series would reach ${pct(twoVanners)} % and nothing else would be built.</p>
<ul class="ol-smelts" aria-label="Single-pass figures">${CONCS.map((k) => `<li>${cardLink(k, machine(k).name)} <b>r = ${R[k]}</b></li>`).join("")}<li>by hand, no chain: gold pan <b>${pct(CFG.hand.pan, 0)} %</b>, rocker <b>${pct(CFG.hand.rocker, 0)} %</b></li></ul>
<h4>Every sub-chain, on fully catchable feed</h4>
<div class="ol-subs"><div><b>One machine</b><ul>${col([1])}</ul></div><div><b>Two</b><ul>${col([2])}</ul></div><div><b>Three or four</b><ul>${col([3, 4])}</ul></div></div>
<h4>The locked shares: applied once, not per pass</h4>
<p class="ol-lede">A feed's factors (unclassified × ${L.unclassified}, poor ore not ground × ${L.poorUnground}, free metal not amalgamated × ${L.freeUnamalgamated}) are the share of its units gravity can catch at all; the rest is <b>locked</b> (in unground rock, in grains of the wrong size, in floating gold), and no number of passes catches it. The line recovers its chain figure of the catchable share only. Applied per pass instead, a full line would wear the penalties away (${pct(1 - (1 - R.sluice * L.poorUnground) * (1 - R.jig * L.poorUnground) * (1 - R.table * L.poorUnground) * (1 - R.vanner * L.poorUnground))} % on unground poor ore) and grinding and screening would stop mattering just when the player can afford them.</p>
<div class="ol-tablewrap"><table class="ol-table"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> recovery = catchable × (1 − Π(1 − r)); catchable = ${L.unclassified} if unclassified × ${L.poorUnground} if poor and not ground × ${L.freeUnamalgamated} if free and not amalgamated. Density (cassiterite and chromite 1.05, native copper 1.1, smithsonite 0.9) stays per pass, on each r, capped at 1. The plates replace the free-metal factor with their own figures: caught + (1 − caught) × pass × chain.</p>
<p class="ol-lede"><b>The locked share leaves at the table, as middlings.</b> The ${cardLink("table", "shaking table")} sends its feed's locked units out of a middlings chute, which the player runs to the ${cardLink("ballmill", "ball mill")}'s infeed; the mill grinds and screens, so what it passes has no locked share, and it chutes back to the head of the concentrators. Without a table, the locked share leaves with the last machine's tailings, lost.</p>
</section>`;
}

export function renderGlossary(): string {
  return `<section class="ol-class" id="ol-glossary" aria-labelledby="ol-glossary-h"><h3 id="ol-glossary-h">Glossary</h3>
<dl class="ol-glossary">${GLOSSARY.map(([t, d]) => `<div><dt>${esc(t)}</dt><dd>${linked(d)}</dd></div>`).join("")}</dl></section>`;
}

/** The tab's styles; the other tab reuses them (the two sit on one page). */
export const STYLE = `<style>
.ore-line { --ol-cont: var(--series-1); --ol-batch: var(--series-3); --ol-hand: var(--series-4); --ol-line: var(--muted); }
.ore-line h2 { font-size: 1.3rem; margin: 0.2rem 0 0.4rem; }
.ore-line h3 { font-size: 1.12rem; margin: 0 0 0.3rem; }
.ore-line h4 { font-size: 0.95rem; margin: 0.9rem 0 0.35rem; }
.ore-line p { margin: 0.3rem 0; }
.ore-line ul, .ore-line ol { margin: 0.25rem 0; padding-left: 1.2rem; }
.ol-intro { max-width: 60rem; }
.ol-jump { display: flex; flex-wrap: wrap; gap: 0.3rem 0.5rem; margin: 0.6rem 0 1rem; font-size: 0.88rem; }
.ol-jump a { color: var(--link); }
.ol-rule { background: var(--surface); border-left: 3px solid var(--accent); border-radius: var(--radius); padding: 0.4rem 0.6rem; margin: 0.4rem 0 0.6rem; font-size: 0.9rem; }
.ol-smelts { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.4rem 0 0.2rem; padding: 0; list-style: none; font-size: 0.85rem; }
.ol-smelts li { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ol-smelts li b { color: var(--accent); }
.ol-smelts li.ol-s0 b { color: var(--critical); }
.ol-legend { display: flex; flex-wrap: wrap; gap: 0.35rem 0.8rem; margin: 0.4rem 0 0.6rem; font-size: 0.84rem; align-items: center; }
.ol-class { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.8rem; margin: 0 0 1rem; }
.ol-ores, .ol-lede, .ol-note, .ol-adds { font-size: 0.88rem; }
.ol-ores { color: var(--muted); }
.ol-adds { margin-top: 0.6rem; }
.ol-mlink { color: var(--link); text-decoration: none; border-bottom: 1px dotted var(--link); }
/* the step strip */
.ol-strip { display: flex; flex-wrap: wrap; align-items: stretch; gap: 0.35rem 0; margin-top: 0.4rem; }
.ol-step { position: relative; flex: 1 1 9rem; max-width: 20rem; min-width: 0; background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.55rem 0.4rem; font-size: 0.8rem; line-height: 1.3; display: flex; flex-direction: column; gap: 0.25rem; }
.ol-step-hand { border-style: dashed; }
.ol-step-n { position: absolute; top: 0.3rem; right: 0.45rem; font-size: 0.72rem; color: var(--muted); }
.ol-stage { font-weight: 700; font-size: 0.95rem; padding-right: 1rem; }
.ol-opt { display: flex; flex-wrap: wrap; gap: 0.25rem; font-size: 0.78rem; color: var(--muted); }
.ol-tag { display: inline-block; font-size: 0.66rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; border-radius: 999px; padding: 0 0.4rem; line-height: 1.5; vertical-align: 0.1rem; white-space: nowrap; }
.ol-tag-opt { background: var(--notice-bg); border: 1px solid var(--notice-border); }
.ol-tag-lv { background: var(--surface-2); border: 1px solid var(--border); }
.ol-tag-continuous { background: color-mix(in srgb, var(--ol-cont) 18%, transparent); border: 1px solid var(--ol-cont); }
.ol-tag-batch { background: color-mix(in srgb, var(--ol-batch) 18%, transparent); border: 1px solid var(--ol-batch); }
.ol-tag-hand { background: color-mix(in srgb, var(--ol-hand) 18%, transparent); border: 1px solid var(--ol-hand); }
.ol-dev { font-size: 0.78rem; color: var(--muted); }
.ol-exit { margin-top: auto; font-size: 0.78rem; border-radius: var(--radius); padding: 0.2rem 0.4rem; border: 1px solid var(--border); }
.ol-exit b { text-transform: uppercase; letter-spacing: 0.04em; font-size: 0.66rem; margin-right: 0.2rem; }
.ol-exit-fig { font-weight: 700; color: var(--accent); font-size: 0.9rem; }
.ol-exit-note { display: block; color: var(--muted); font-size: 0.72rem; }
.ol-exit-smelt { border-color: var(--accent); background: color-mix(in srgb, var(--accent) 12%, transparent); }
.ol-exit-smelt b { color: var(--accent); }
.ol-exit-none { border-style: dashed; color: var(--muted); }
.ol-exit-none b { color: var(--critical); }
.ol-exit-on { background: var(--surface-2); }
.ol-arrow { flex: none; width: 1rem; align-self: center; height: 2px; background: var(--ol-line); position: relative; }
.ol-arrow::after { content: ""; position: absolute; right: -1px; top: -4px; border: 5px solid transparent; border-left: 6px solid var(--ol-line); border-right: 0; }
.ol-issues { font-size: 0.74rem; font-weight: 400; }
.ol-issue { color: var(--link); white-space: nowrap; }
/* tables */
.ol-tablewrap { overflow-x: auto; }
.ol-table { border-collapse: collapse; width: 100%; font-size: 0.84rem; }
.ol-table th, .ol-table td { border: 1px solid var(--border); padding: 0.3rem 0.45rem; text-align: left; vertical-align: top; }
.ol-table thead th { background: var(--surface-2); font-size: 0.8rem; }
.ol-table tbody th { font-weight: 400; background: var(--bg); min-width: 8rem; }
.ol-table tbody th b { display: block; }
.ol-when { display: block; color: var(--muted); font-size: 0.76rem; font-weight: 400; }
.ol-c .ol-f { display: block; white-space: nowrap; }
.ol-c .ol-f-best b { color: var(--accent); font-size: 1rem; }
.ol-c-zero, .ol-c-none { color: var(--muted); }
.ol-cnote { display: block; color: var(--muted); font-size: 0.74rem; white-space: normal; }
.ol-unparted { font-size: 0.8rem; min-width: 11rem; }
.ol-formula, .ol-notes { font-size: 0.8rem; color: var(--muted); }
.ol-formula { margin-top: 0.5rem; }
.ol-example { margin-top: 0.6rem; font-size: 0.84rem; background: var(--surface-2); border-radius: var(--radius); padding: 0.45rem 0.6rem; }
/* cards */
.ol-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 19rem), 1fr)); gap: 0.5rem; margin: 0.5rem 0; }
.ol-card { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.5rem 0.6rem; font-size: 0.8rem; line-height: 1.35; scroll-margin-top: 3.5rem; }
.ol-card:target { border-color: var(--accent); box-shadow: 0 0 0 2px color-mix(in srgb, var(--accent) 35%, transparent); }
.ol-card-hand { border-style: dashed; }
.ol-card h4 { margin: 0; font-size: 0.98rem; display: flex; flex-wrap: wrap; gap: 0.3rem; align-items: center; }
.ol-real { color: var(--muted); font-size: 0.76rem; margin: 0.1rem 0 0.35rem !important; }
.ol-card dl { margin: 0; display: grid; grid-template-columns: max-content 1fr; gap: 0.15rem 0.5rem; }
.ol-card dt { font-weight: 700; color: var(--muted); font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.04em; padding-top: 0.1rem; }
.ol-card dd { margin: 0; }
/* chains */
.ol-subs { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 14rem), 1fr)); gap: 0.5rem; font-size: 0.84rem; }
.ol-subs > div { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.4rem 0.6rem; }
.ol-subs ul { list-style: none; padding: 0; margin: 0.2rem 0 0; }
.ol-subs li b { color: var(--accent); float: right; }
.ol-subs li.ol-sub-all { font-weight: 700; }
/* glossary */
.ol-glossary { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 22rem), 1fr)); gap: 0.3rem 1rem; margin: 0.4rem 0; font-size: 0.82rem; }
.ol-glossary dt { font-weight: 700; display: inline; }
.ol-glossary dd { display: inline; margin: 0; }
.ol-glossary dt::after { content: ". "; }
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
  .ol-card dl { grid-template-columns: 1fr; gap: 0; }
  .ol-card dt { margin-top: 0.25rem; }
}
</style>`;

export default function oreLine(): string {
  const jump = [
    ...CLASSES.map((c) => ({ id: c.id, title: c.title })),
    { id: "parting", title: "By-product ores" },
    { id: "machines", title: "The machines" },
    { id: "chains", title: "How concentrators chain" },
    { id: "glossary", title: "Glossary" },
    { id: "open", title: "Open questions" },
  ];
  const slotRock = (g: Grade) => (CFG.rockStack * CFG.units[g] * CFG.smeltByGrade[g]) / CFG.unitsPerIngot;
  const slotConc = (CFG.concentrateStack * CFG.concentrateUnits) / CFG.unitsPerIngot;
  return `<div class="ore-line">
${STYLE}
<h2>The processing line: which machines, and why</h2>
<p class="ol-intro">For each kind of ore: the least that works and the full line, as the steps from the rock to metal. Each step names its machine or hand station (its card is below: what it is, what it does, why use it, what skipping it costs) and says whether you can <b>smelt here</b> what you hold, and at what share of the metal that was in the rock. Figures are the settled machine specification's and main's <code>ore-processing.json</code> (${issueLink(684)}, ${issueLink(847)}, ${issueLink(863)}), computed on this page; every one is a starting value for playtesting.</p>
<p class="ol-rule"><b>The rule.</b> A slot holds a fixed amount of rock: ${CFG.rockStack} ore's worth, whatever has been done to it (raw, crushed, ground or roasted rock all stack to ${CFG.rockStack} and convert one to one). Grade is metal per item: ${GRADES.map((g) => `${g} ${CFG.units[g]}`).join(", ")} units for most ores. Rock smelts by grade, ${byGrade((g) => rockSmelt(g))} %, so a slot of rock is worth ${GRADES.map((g) => `${Math.round(slotRock(g) * 100) / 100}`).join(" / ")} ingots by grade however it has been crushed. Only <b>concentrating throws the rock away</b>: concentrate holds ${CFG.concentrateUnits} units an item, stacks to ${CFG.concentrateStack} and smelts whole, so its slot is worth ${slotConc} ingots at any grade. Everything before the concentrators is lossless; the concentrators are where metal is lost, and the roasters and parting stations after them.</p>
<div class="ol-legend"><b>Steps:</b> ${feedTag("continuous")} chute in, chutes out, unattended ${feedTag("batch")} loaded and emptied by hand ${feedTag("hand")} the player's own work <span class="ol-tag ol-tag-opt">optional</span> the line works without it; its card says what it costs</div>
<nav class="ol-jump" aria-label="Sections">${jump.map((c) => `<a href="#ol-${c.id}">${esc(c.title)}</a>`).join("")}</nav>
${CLASSES.map(renderClass).join("\n")}
${renderParting()}
${renderCards()}
${renderChains()}
${renderGlossary()}
<section class="ol-questions" id="ol-open" aria-labelledby="ol-open-h"><h3 id="ol-open-h">Open questions</h3><p>Settled by the owner, from the spec's list: each machine's load is constant once assembled; rate is in proportion to speed, uncapped; the arrastra is the mule-sized one (a charge of 2 rock in 7.5 hours); there is no design plant. Still open:</p><ol>${OPEN_QUESTIONS.map((q) => `<li>${linked(q)}</li>`).join("")}</ol></section>
</div>`;
}
