// The ore routes page's maths (#/<version>/ores). The export's `oreProcessing` section carries the
// figures ore processing works by (tools/recipe-export/OreProcessingSection.cs); prepare-data adds
// the placer feeds it reads off the machine records and writes both to ores.json. Every figure on
// the page is worked out here, following the mod's own rules in
// mods-src/seraphhorizons/Ore/Core/OreRecovery.cs: concentrators that each work what the one before
// missed, the locked shares applied once, the last step of an ore's whole route recovering what is
// left, roasting and parting. docs/recipe-browser/site.md describes the page.

export type Grade = "poor" | "medium" | "rich" | "bountiful";
export const GRADES: readonly Grade[] = ["poor", "medium", "rich", "bountiful"];

export type OreClass = "oxide" | "sulfide" | "native" | "placer";
export type PartingMethod = "cupellation" | "liquation" | "acidparting";

export interface OreByProduct {
  metal: string;
  /** Units of it per unit of the main metal. */
  share: number;
  partedBy: PartingMethod;
}

export interface OreSpec {
  ore: string;
  name: string;
  metal: string;
  class: OreClass;
  density: number;
  free: boolean;
  unparted: number;
  /** Metal units a raw ore item holds, by grade (GRADES' order). */
  units: number[];
  byProducts: OreByProduct[];
  /** What its concentrate (a sulfide's roasted concentrate) smelts to: an ingot, a bloom or bullion. Added by prepare-data. */
  product?: string;
}

/** The export's `oreProcessing` section (schema/recipe-export.schema.json). */
export interface OreProcessingExport {
  concentrateUnits: number;
  handStations: Record<string, number>;
  unclassified: number;
  fineUnground: number;
  freeUnamalgamated: number;
  machines: Record<string, { recovery: number }>;
  smeltByGrade: Record<string, number>;
  smelt: Record<string, number>;
  ores: OreSpec[];
}

/** A placer feed, washed rather than crushed: rich gravel (the rocks whose gravel holds the same) or bog iron. */
export interface Placer {
  /** The feed's code: game:richgravel-<rock> (the first of its rocks) or game:stone-bogiron. */
  feed: string;
  kind: "gravel" | "bogiron";
  /** The rocks whose rich gravel holds these contents (gravel only), by code. */
  rocks: string[];
  /** Metal units a block holds, per metal, most first. */
  contents: { metal: string; units: number }[];
  /** What it washes out as: nuggets, or the code of the concentrate. */
  output: string;
}

/** ores.json: the section, with the placer feeds prepare-data read off the sluice's records. */
export interface OreRoutesData extends OreProcessingExport {
  placers: Placer[];
}

// ---- The stream (OreRecovery's OreStream and its locked shares) ----

type Concentrator = "sluice" | "jig" | "table" | "vanner";
const CONCENTRATORS: readonly Concentrator[] = ["sluice", "jig", "table", "vanner"];

interface Stream {
  units: number;
  /** The units gravity can't catch, once the first concentrator has split the stream. */
  locked: number | null;
  classified: boolean;
  ground: boolean;
  amalgamated: boolean;
  worked: readonly Concentrator[];
}

type Lifted = "classified" | "ground" | "amalgamated";

const raw = (units: number): Stream => ({ units, locked: null, classified: false, ground: false, amalgamated: false, worked: [] });

/** One machine's or hand station's figures on one ore at one grade. */
export class Recovery {
  readonly data: OreProcessingExport;
  readonly ore: OreSpec;
  readonly grade: Grade | null;

  constructor(data: OreProcessingExport, ore: OreSpec, grade: Grade | null) {
    this.data = data;
    this.ore = ore;
    this.grade = grade;
  }

  machine(m: string): number {
    return this.data.machines[m]?.recovery ?? 0;
  }

  hand(h: string): number {
    return this.data.handStations[h] ?? 0;
  }

  /** The share of a stream gravity can catch at all (LockFactor). Placer: 1. */
  lockFactor(s: Pick<Stream, "classified" | "ground" | "amalgamated">): number {
    if (this.ore.class === "placer") return 1;
    let f = 1;
    if (this.grade === "poor" && !s.ground) f *= this.data.fineUnground;
    if (!s.classified) f *= this.data.unclassified;
    if (this.ore.free && !s.amalgamated) f *= this.data.freeUnamalgamated;
    return f;
  }

  lockedOf(s: Stream): number {
    return s.locked ?? s.units * (1 - this.lockFactor(s));
  }

  /** Screening, grinding or amalgamating (Lift): the flag set, and a split stream's locked units freed in proportion. */
  lift(s: Stream, added: Lifted): Stream {
    const next = { ...s, [added]: true };
    if (s.locked === null || s[added]) return next;
    const before = 1 - this.lockFactor(s);
    const after = 1 - this.lockFactor(next);
    const now = before > 0 ? (s.locked * after) / before : 0;
    return { ...next, locked: now, worked: s.locked - now > 1e-12 ? [] : s.worked };
  }

  passShare(m: Concentrator): number {
    return Math.min(1, Math.max(0, this.machine(m) * this.ore.density));
  }

  /** Classified, ground if poor, amalgamated if free, and worked by the sluice, jig and table (OnWholeRoute). */
  onWholeRoute(s: Stream): boolean {
    return (
      this.lockFactor(s) >= 1 - 1e-12 &&
      this.lockedOf(s) <= 1e-9 &&
      ["sluice", "jig", "table"].every((m) => s.worked.includes(m as Concentrator)) &&
      !s.worked.includes("vanner")
    );
  }

  /** One concentrator's pass (Concentrate): what it catches, what goes on, and whether its concentrate is the whole route's. */
  concentrate(m: Concentrator, feed: Stream): { caught: number; tailings: Stream; whole: boolean } {
    let locked = this.lockedOf(feed);
    if (feed.worked.includes(m)) return { caught: 0, tailings: { ...feed, locked }, whole: false };
    const whole = m === "vanner" && this.onWholeRoute(feed);
    const finalHere = whole && this.finalStep() === "vanner";
    const caught = Math.max(0, feed.units - locked) * (finalHere ? 1 : this.passShare(m));
    let left = feed.units - caught;
    // The table puts the locked units out as middlings (back to the ball mill); they leave this stream.
    if (m === "table" && locked > 1e-12) {
      left -= locked;
      locked = 0;
    }
    return { caught, tailings: { ...feed, units: Math.max(0, left), locked, worked: [...feed.worked, m] }, whole: whole && !finalHere };
  }

  /** A hand concentrator (the gold pan, the rocker): its share of the catchable units; what it misses is spent. */
  handConcentrate(h: "pan" | "rocker", feed: Stream): number {
    const catchable = Math.max(0, feed.units - this.lockedOf(feed));
    return catchable * Math.min(1, Math.max(0, this.hand(h) * this.ore.density));
  }

  /** Amalgamation (the amalgam pan, the plates): free metal caught as amalgam, the rest on, amalgamated. */
  amalgamate(figure: number, feed: Stream): { caught: number; rest: Stream } {
    const lifted = this.lift(feed, "amalgamated");
    if (!this.ore.free) return { caught: 0, rest: lifted };
    const caught = Math.max(0, lifted.units - this.lockedOf(lifted)) * Math.min(1, Math.max(0, figure));
    return { caught, rest: { ...lifted, units: lifted.units - caught } };
  }

  /** The last step of the ore's ideal route, which recovers whatever is left (FinalStep). */
  finalStep(): string {
    const bp = this.ore.byProducts[0];
    if (bp)
      return bp.partedBy === "cupellation"
        ? this.ore.metal === "lead"
          ? "parkeskettle"
          : "cupellationfurnace"
        : bp.partedBy === "liquation"
          ? "liquationfurnace"
          : "acidparting";
    return this.ore.class === "sulfide" ? "reverberatory" : "vanner";
  }

  /** What a later final step multiplies a whole-route concentrate by to make it whole (WholeRestore). */
  wholeRestore(): number {
    const chain = 1 - CONCENTRATORS.reduce((left, m) => left * (1 - this.passShare(m)), 1);
    return chain > 0 ? 1 / chain : 1;
  }

  roaster(r: Roaster): number {
    return r === "firepit" ? this.hand("firepit") : this.machine(r === "stall" ? "stallroaster" : "reverberatory");
  }

  /** The share roasting keeps (Roasting); the reverberatory makes a whole-route concentrate whole when it is the last step. */
  roasting(r: Roaster, whole = false): number {
    const share = this.roaster(r);
    return whole && this.wholeAt(r) ? share * this.wholeRestore() : share;
  }

  /** Whether roasting in `r` is the ore's last step, which makes whole-route concentrate whole. */
  wholeAt(r: Roaster): boolean {
    return r === "reverberatory" && this.finalStep() === "reverberatory";
  }

  gradeShare(): number {
    return this.grade ? (this.data.smeltByGrade[this.grade] ?? 0) : 0;
  }

  /**
   * What smelting (and parting at `station`) gives from concentrate holding `units` of the main metal
   * (Smelted): unparted, the main metal × the ore's unparted share and the by-product lost; parted,
   * all the main metal and the by-product at its share × the station's recovery; at the ore's last
   * step, the `whole` units of them that came off the whole route are made whole, and the
   * by-product comes out whole with them. (Amalgam the plates caught is not marked whole: they
   * caught all of theirs.)
   */
  smelted(units: number, station: PartingStation | null, whole = 0): { main: number; byProduct: { metal: string; units: number } | null } {
    const bp = this.ore.byProducts[0];
    if (!station || !bp || bp.partedBy !== partingMethod(station)) return { main: units * this.ore.unparted, byProduct: bp ? { metal: bp.metal, units: 0 } : null };
    const final = whole > 0 && station !== "cupel" && station !== "liquationpan" && this.finalStep() === station;
    const main = final ? units + whole * (this.wholeRestore() - 1) : units;
    const p = final ? 1 : station === "cupel" || station === "liquationpan" ? this.hand(station) : this.machine(station);
    return { main, byProduct: { metal: bp.metal, units: main * bp.share * p } };
  }
}

export type Roaster = "firepit" | "stall" | "reverberatory";
export type PartingStation = "cupel" | "liquationpan" | "cupellationfurnace" | "parkeskettle" | "liquationfurnace" | "acidparting";

export function partingMethod(s: PartingStation): PartingMethod {
  return s === "cupel" || s === "cupellationfurnace" || s === "parkeskettle" ? "cupellation" : s === "liquationpan" || s === "liquationfurnace" ? "liquation" : "acidparting";
}

// ---- The route tree ----

/** A step on a route. Its name, what it does and where it links are the page's (strings.ts, STEP_ITEMS). */
export type StepId =
  | "mined"
  | "dug"
  | "spalling"
  | "riddle"
  | "arrastra"
  | "pan"
  | "rocker"
  | "amalgampan"
  | "jawbreaker"
  | "stampbattery"
  | "ballmill"
  | "plates"
  | "sluice"
  | "jig"
  | "table"
  | "vanner"
  | "line"
  | "firepit"
  | "stallroaster"
  | "reverberatory"
  | "bullion"
  | PartingStation;

/** Which way a step is taken, where its name alone doesn't say: rock or concentrate, unscreened, ... */
export type StepVariant = "rock" | "unriddled" | "ground" | "rest" | "unscreened" | "noplates" | "richlead";

export interface RouteNode {
  step: StepId;
  variant?: StepVariant;
  /** Units of the main metal one raw ore item (a block of placer) gives smelted after this step; null where nothing is smelted here. */
  units: number | null;
  /** Units an unroasted sulfide product holds, which smelts to nothing until roasted. */
  holds?: number;
  /** The by-product, where the ore has one: what this step gives of it (0 unparted). */
  byProduct?: { metal: string; units: number };
  /** The last step of the ore's whole route, which recovers whatever is left. */
  final?: boolean;
  children: RouteNode[];
}

/** The steps worked by hand: a route made only of these needs no machine. */
export const HAND_STEPS: ReadonlySet<StepId> = new Set<StepId>(["mined", "dug", "spalling", "riddle", "pan", "rocker", "amalgampan", "firepit", "bullion", "cupel", "liquationpan"]);

const node = (step: StepId, units: number | null, children: RouteNode[] = [], more: Partial<RouteNode> = {}): RouteNode => ({ step, units, children, ...more });

/** Every route one raw ore item of `ore` at `grade` can take, from the mine to metal. */
export function oreRoutes(data: OreProcessingExport, ore: OreSpec, grade: Grade): RouteNode {
  const r = new Recovery(data, ore, grade);
  const U = ore.units[GRADES.indexOf(grade)] ?? 0;
  const sulfide = ore.class === "sulfide";
  const poor = grade === "poor";
  // Rock smelts by its grade, unparted (gold quartz's gold × 0.85).
  const rockUnits = sulfide ? 0 : r.smelted(U * r.gradeShare(), null).main;
  const rock = (step: StepId, children: RouteNode[], variant?: StepVariant): RouteNode => node(step, rockUnits, children, variant ? { variant } : {});
  const conc = data.smelt["concentrate"] ?? 1;

  // Bullion and its parting, by hand (the cupel, the liquation pan) or by machine; `whole` of the
  // units came off the whole route, which the ore's last step makes whole.
  const parting = (main: number, how: "hand" | "machine", whole = 0): RouteNode[] => {
    const bp = ore.byProducts[0];
    if (!bp) return [];
    const at = (station: PartingStation): RouteNode => {
      const s = r.smelted(main, station, whole);
      return node(station, s.main, [], { byProduct: s.byProduct!, ...(whole > 0 && r.finalStep() === station ? { final: true } : {}) });
    };
    const unparted = r.smelted(main, null);
    let children: RouteNode[];
    if (how === "hand") children = bp.partedBy === "cupellation" ? [at("cupel")] : bp.partedBy === "liquation" ? [at("liquationpan")] : [];
    else if (bp.partedBy === "cupellation")
      // Lead with silver: the cupellation furnace on the bullion, or the Parkes kettle's rich lead
      // (the kettle's figure: it takes the silver whole into the rich lead the furnace then cupels).
      children = ore.metal === "lead" ? [at("cupellationfurnace"), node("parkeskettle", null, [{ ...at("parkeskettle"), step: "cupellationfurnace", variant: "richlead" }])] : [at("cupellationfurnace")];
    else children = [at(bp.partedBy === "liquation" ? "liquationfurnace" : "acidparting")];
    return [node("bullion", unparted.main, children, { byProduct: unparted.byProduct! })];
  };

  // Concentrate holding `units` of the main metal (`whole` of them off the whole route): smelted
  // whole, or for a sulfide roasted first.
  const concentrate = (step: StepId, units: number, how: "hand" | "machine", whole = 0, more: Partial<RouteNode> = {}, roast = true): RouteNode => {
    if (!sulfide) {
      // Acid parting has no hand form: gold quartz washed by hand smelts unparted.
      const kids = how === "machine" || ore.byProducts[0]?.partedBy !== "acidparting" ? parting(units * conc, how, whole * conc) : [];
      return node(step, r.smelted(units * conc, null).main, kids, { ...more, ...(ore.byProducts[0] ? { byProduct: { metal: ore.byProducts[0].metal, units: 0 } } : {}) });
    }
    const roasts: RouteNode[] = [];
    if (roast && how === "machine") {
      const share = r.roasting("reverberatory");
      const final = whole > 0 && r.wholeAt("reverberatory");
      const reverb = units * share + (final ? whole * share * (r.wholeRestore() - 1) : 0);
      roasts.push(node("reverberatory", r.smelted(reverb, null).main, parting(reverb, "machine", final ? 0 : whole * share), final ? { final: true } : {}));
      roasts.push(node("firepit", units * r.roasting("firepit")));
    } else if (roast) {
      const roasted = units * r.roasting("firepit");
      roasts.push(node("firepit", r.smelted(roasted, null).main, parting(roasted, "hand")));
    }
    return node(step, 0, roasts, { holds: units, ...more });
  };

  // By hand: the rocker, and for free gold or silver the amalgam pan before it.
  const sponge = data.smelt["sponge"] ?? 1;
  const byHand = (s: Stream, variant?: StepVariant, amalgamPan = false): RouteNode[] => {
    const out: RouteNode[] = [concentrate("rocker", r.handConcentrate("rocker", s), "hand", 0, variant ? { variant } : {})];
    if (amalgamPan && ore.free) {
      const pass = r.amalgamate(r.hand("amalgampan"), s);
      const total = pass.caught * sponge + r.handConcentrate("rocker", pass.rest);
      out.push(node("amalgampan", r.smelted(pass.caught * sponge, null).main, [concentrate("rocker", total, "hand", 0, { variant: "rest" })]));
    }
    return out;
  };

  // The four concentrators in a row, each on the one before's tailings; `before` is what the plates caught.
  const line = (s: Stream, before = 0): RouteNode => {
    let feed = s;
    let caught = 0;
    const passes: { m: Concentrator; caught: number; whole: boolean; final: boolean }[] = [];
    for (const m of CONCENTRATORS) {
      const finalHere = m === "vanner" && r.onWholeRoute(feed) && r.finalStep() === "vanner";
      const pass = r.concentrate(m, feed);
      caught += pass.caught;
      feed = pass.tailings;
      passes.push({ m, caught, whole: pass.whole, final: finalHere });
    }
    let tail: RouteNode | null = null;
    for (let i = passes.length - 1; i >= 0; i--) {
      const p = passes[i]!;
      const kids: RouteNode[] = tail ? [tail] : [];
      if (p.m === "vanner") tail = concentrate("vanner", before + p.caught, "machine", p.whole ? p.caught : 0, p.final ? { final: true } : {});
      else tail = { ...concentrate(p.m, before + p.caught, "machine", 0, {}, false), children: kids };
    }
    return tail!;
  };

  // All four concentrators as one step, for a stream that skipped a lift (no screen, no plates).
  const lineAsOne = (s: Stream, variant: StepVariant): RouteNode => {
    let feed = s;
    let caught = 0;
    for (const m of CONCENTRATORS) {
      const pass = r.concentrate(m, feed);
      caught += pass.caught;
      feed = pass.tailings;
    }
    return concentrate("line", caught, "machine", 0, { variant }, false);
  };

  const mined = raw(U);
  const riddled = r.lift(mined, "classified");

  // By hand: spalling, the riddle, the rocker (the arrastra grinds poor ore); a sulfide's rock roasted in a firepit.
  const riddleKids = [...byHand(riddled, undefined, true)];
  if (poor) riddleKids.push(rock("arrastra", byHand(r.lift(riddled, "ground"), "ground")));
  const spallingKids = [rock("riddle", riddleKids), ...byHand(mined, "unriddled")];
  if (sulfide) {
    const roasted = U * r.gradeShare() * r.roasting("firepit");
    spallingKids.push(node("firepit", roasted, parting(roasted, "hand"), { variant: "rock" }));
  }

  // By machine: the stamp battery screens (the ball mill grinds poor ore), the plates amalgamate free metal, the four concentrators.
  let screened = r.lift(mined, "classified");
  if (poor) screened = r.lift(screened, "ground");
  const lineKids: RouteNode[] = [];
  if (ore.free) {
    const pass = r.amalgamate(r.machine("amalgamationplates"), screened);
    const amalgam = pass.caught * sponge;
    lineKids.push(node("plates", r.smelted(amalgam, null).main, [line(pass.rest, amalgam)]));
    lineKids.push(lineAsOne(screened, "noplates"));
  } else lineKids.push(line(screened));
  const battery = rock("stampbattery", poor ? [rock("ballmill", lineKids)] : lineKids);

  const minedKids = [rock("spalling", spallingKids)];
  if (sulfide) {
    const roasted = U * r.gradeShare() * r.roasting("stall");
    minedKids.push(node("stallroaster", roasted, parting(roasted, "machine")));
  }
  minedKids.push(rock("jawbreaker", [battery, lineAsOne(mined, "unscreened")]));
  return rock("mined", minedKids);
}

/** Every route a block of placer takes: panned, rocked, or through the four concentrators (each on the one before's tailings). */
export function placerRoutes(data: OreProcessingExport, placer: Placer): RouteNode {
  const total = placer.contents.reduce((a, c) => a + c.units, 0);
  const share = (m: string) => Math.min(1, Math.max(0, data.machines[m]?.recovery ?? 0));
  let left = 1;
  const cumulative = CONCENTRATORS.map((m) => {
    left *= 1 - share(m);
    return total * (1 - left);
  });
  let tail: RouteNode | null = null;
  for (let i = CONCENTRATORS.length - 1; i >= 0; i--) {
    const m = CONCENTRATORS[i]!;
    // The vanner is placer's last step: it recovers whatever is left.
    tail = m === "vanner" ? node(m, total, [], { final: true }) : node(m, cumulative[i]!, tail ? [tail] : []);
  }
  const kids: RouteNode[] = [];
  if (placer.kind === "gravel") kids.push(node("pan", total * (data.handStations["pan"] ?? 0)));
  kids.push(node("rocker", total * (data.handStations["rocker"] ?? 0)), tail!);
  return node("dug", null, kids);
}

// ---- Reading a tree ----

/** What a node gives in all, main metal and by-product, for comparing routes. */
export const nodeTotal = (n: RouteNode): number => (n.units ?? 0) + (n.byProduct?.units ?? 0);

/**
 * The best route: the node that gives the most in all, the deeper one on a tie (the step that
 * finishes the job), with the steps from the top to it. `only` limits it to routes of those steps.
 */
export function bestRoute(root: RouteNode, only?: ReadonlySet<StepId>): RouteNode[] {
  let best: RouteNode[] = [];
  let bestTotal = -1;
  const walk = (n: RouteNode, path: RouteNode[]) => {
    if (only && !only.has(n.step)) return;
    const here = [...path, n];
    const total = nodeTotal(n);
    if (n.units !== null && (total > bestTotal + 1e-9 || (Math.abs(total - bestTotal) <= 1e-9 && here.length > best.length))) {
      best = here;
      bestTotal = total;
    }
    for (const c of n.children) walk(c, here);
  };
  walk(root, []);
  return best;
}

/** The metal units a raw ore item (a block of placer) holds. */
export function contained(ore: OreSpec, grade: Grade): number {
  return ore.units[GRADES.indexOf(grade)] ?? 0;
}

/** The ore's groups on the page: what decides its route. */
export type OreGroup = "oxide" | "sulfide" | "free" | "placer";

export function oreGroup(ore: OreSpec): OreGroup {
  return ore.free ? "free" : ore.class === "sulfide" ? "sulfide" : "oxide";
}

// ---- The page's choices and numbers ----

/** The item a step's name links to: its machine's or station's block, the pan, the bullion. */
const STEP_ITEMS: Partial<Record<StepId, string>> = {
  riddle: "seraphhorizons:riddle-frame-north",
  arrastra: "seraphhorizons:arrastra-north",
  pan: "game:pan-wooden",
  rocker: "seraphhorizons:rocker-north",
  amalgampan: "seraphhorizons:amalgampan-frame-north",
  jawbreaker: "seraphhorizons:jawbreaker-north",
  stampbattery: "seraphhorizons:stampbattery-north",
  ballmill: "seraphhorizons:ballmill-north",
  plates: "seraphhorizons:amalgamationplates-north",
  sluice: "seraphhorizons:sluice-north",
  jig: "seraphhorizons:jig-north",
  table: "seraphhorizons:shakingtable-north",
  vanner: "seraphhorizons:vanner-north",
  firepit: "game:firepit-cold",
  stallroaster: "seraphhorizons:stallroaster-north",
  reverberatory: "seraphhorizons:reverberatory-north",
  cupel: "seraphhorizons:cupel-fired",
  liquationpan: "seraphhorizons:liquationpan-fired",
  cupellationfurnace: "seraphhorizons:cupellationfurnace-north",
  parkeskettle: "seraphhorizons:parkeskettle-north",
  liquationfurnace: "seraphhorizons:liquationfurnace-north",
  acidparting: "seraphhorizons:acidparting-north",
};

/** The item a step links to, if any: the bullion is the ore's own (seraphhorizons:bullion-<ore>-<metal>). */
export function stepItem(step: StepId, ore?: OreSpec): string | undefined {
  if (step === "bullion") return ore ? `seraphhorizons:bullion-${ore.ore}-${ore.metal}` : undefined;
  return STEP_ITEMS[step];
}

/** One entry of the page's ore picker: an ore, or a placer feed. */
export interface OreChoice {
  /** The address's `ore`: the ore's code, or the placer feed's path (richgravel-granite, stone-bogiron). */
  id: string;
  group: OreGroup;
  ore?: OreSpec;
  placer?: Placer;
}

/** The picker's entries, by group in the page's order, then by name (bog iron after the gravels). */
export function oreChoices(data: OreRoutesData, nameOf: (choice: OreChoice) => string): OreChoice[] {
  const order: OreGroup[] = ["oxide", "sulfide", "free", "placer"];
  const all: OreChoice[] = [
    ...data.ores.map((ore) => ({ id: ore.ore, group: oreGroup(ore), ore })),
    ...data.placers.map((placer) => ({ id: placer.feed.slice(placer.feed.indexOf(":") + 1), group: "placer" as const, placer })),
  ];
  const bog = (c: OreChoice) => (c.placer?.kind === "bogiron" ? 1 : 0);
  return all.sort((a, b) => order.indexOf(a.group) - order.indexOf(b.group) || bog(a) - bog(b) || nameOf(a).localeCompare(nameOf(b), "en"));
}

/** The routes of a choice at a grade (placer has no grade), with what one item or block holds. */
export function choiceRoutes(data: OreProcessingExport, choice: OreChoice, grade: Grade): { tree: RouteNode; contained: number } {
  if (choice.placer) return { tree: placerRoutes(data, choice.placer), contained: choice.placer.contents.reduce((a, c) => a + c.units, 0) };
  return { tree: oreRoutes(data, choice.ore!, grade), contained: contained(choice.ore!, grade) };
}

/** Units as the page shows them: at most two decimals, no trailing zeros. */
export function fmtUnits(x: number): string {
  const r = Math.round(x * 100) / 100;
  return (Object.is(r, -0) ? 0 : r).toLocaleString("en", { maximumFractionDigits: 2 });
}

/** A share of `whole` as a percentage: one decimal at most, and just under all never rounds up to 100 %. */
export function fmtPct(part: number, whole: number): string {
  if (!(whole > 0)) return "–";
  const p = (100 * part) / whole;
  const r = Math.round(p * 10) / 10;
  const shown = r >= 100 && p < 100 - 1e-9 ? 99.9 : r;
  return `${shown.toLocaleString("en", { maximumFractionDigits: 1 })} %`;
}

/**
 * A tree as the page draws it: a run of steps that follow one another (each with one next
 * step) is one column; where a step has several next steps, they are a list of choices.
 */
export function chainOf(n: RouteNode): { chain: RouteNode[]; choices: RouteNode[] } {
  const chain = [n];
  let at = n;
  while (at.children.length === 1) {
    at = at.children[0]!;
    chain.push(at);
  }
  return { chain, choices: at.children };
}

/** One ore at a glance: raw, the best by hand, the best of all, as units at `grade`. */
export function glance(data: OreProcessingExport, choice: OreChoice, grade: Grade): { contained: number; raw: number; hand: number; best: number; byProduct: number } {
  const { tree, contained } = choiceRoutes(data, choice, grade);
  const hand = bestRoute(tree, HAND_STEPS).at(-1);
  const best = bestRoute(tree).at(-1);
  return { contained, raw: tree.units ?? 0, hand: hand?.units ?? 0, best: best?.units ?? 0, byProduct: best?.byProduct?.units ?? 0 };
}
