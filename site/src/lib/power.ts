// The power page's maths: every figure derived from the export's power section
// (power-data.ts), so the exporter only reads parameters and the formulas live here.
// Units are the network's: speed is network speed, torque and load what GetTorque and
// GetResistance return, power their product. docs/recipe-browser/power.md has the models.
import type { ConstantPowerModel, Consumer, Producer, ProducerModel, Wind, WindModel, WindPattern } from "./power-data.ts";

/** Full wind, the game's 1.0 strength: what a windmill's figures assume unless told otherwise. */
export const FULL_WIND = 1;

/** A windmill's torque factor with `sails` fitted instead of the entry's own count. */
export function withSails(model: WindModel, sails: number): WindModel {
  if (!model.sails || sails === model.sails.count) return model;
  const n = Math.max(0, Math.min(model.sails.max, sails));
  return {
    ...model,
    torqueFactor: Math.max(0, model.torqueFactor + (n - model.sails.count) * model.sails.torqueFactorPerSail),
    sails: { ...model.sails, count: n },
  };
}

/** The speed a windmill's rotor aims for in `wind`. */
export function windTarget(model: WindModel, wind: number): number {
  return Math.max(0, Math.min(model.speedCap, wind * model.speedPerWind));
}

function constantPowerTorque(m: ConstantPowerModel, s: number): number {
  if (s >= m.shaftSpeed) return 0;
  const t = m.budget / Math.max(s, m.minSpeed);
  if (s <= m.taperFrom) return t;
  return (t * (m.shaftSpeed - s)) / (m.shaftSpeed - m.taperFrom);
}

/** Torque a producer gives at network speed `speed`; `wind` only matters to a windmill. */
export function torqueAt(model: ProducerModel, speed: number, wind: number = FULL_WIND): number {
  const s = Math.max(0, speed);
  switch (model.kind) {
    case "rotor":
      return Math.max(0, model.targetSpeed - s) * model.torqueFactor;
    case "wind":
      return Math.max(0, windTarget(model, wind) - s) * model.torqueFactor;
    case "constantPower":
      return constantPowerTorque(model, s);
  }
}

/** Speed with no load: where the torque reaches 0. */
export function freeSpeed(model: ProducerModel, wind: number = FULL_WIND): number {
  switch (model.kind) {
    case "rotor":
      return model.torqueFactor > 0 ? Math.max(0, model.targetSpeed) : 0;
    case "wind":
      return model.torqueFactor > 0 ? windTarget(model, wind) : 0;
    case "constantPower":
      return model.budget > 0 ? model.shaftSpeed : 0;
  }
}

export function stallTorque(model: ProducerModel, wind: number = FULL_WIND): number {
  return torqueAt(model, 0, wind);
}

/** Several producers on one shaft: one speed, torques added. */
export interface Shaft {
  models: readonly ProducerModel[];
  wind?: number;
}

const shaftTorque = (sh: Shaft, s: number) => sh.models.reduce((sum, m) => sum + torqueAt(m, s, sh.wind), 0);
const shaftFree = (sh: Shaft) => sh.models.reduce((max, m) => Math.max(max, freeSpeed(m, sh.wind)), 0);
const asShaft = (m: ProducerModel | Shaft, wind?: number): Shaft => ("models" in m ? m : { models: [m], wind });

/** Torque × speed at its highest, and the speed it is reached at. */
export function peakPower(model: ProducerModel | Shaft, wind?: number): { power: number; speed: number } {
  const sh = asShaft(model, wind);
  const free = shaftFree(sh);
  if (free <= 0) return { power: 0, speed: 0 };
  // A rotor's peak is at half its target; in general the curve has plateaus and kinks, so
  // sample it and then refine around the best sample.
  const N = 400;
  let best = 0;
  let bestS = 0;
  for (let i = 0; i <= N; i++) {
    const s = (free * i) / N;
    const p = shaftTorque(sh, s) * s;
    if (p > best) {
      best = p;
      bestS = s;
    }
  }
  let lo = Math.max(0, bestS - free / N);
  let hi = Math.min(free, bestS + free / N);
  for (let i = 0; i < 60; i++) {
    const a = lo + (hi - lo) / 3;
    const b = hi - (hi - lo) / 3;
    if (shaftTorque(sh, a) * a < shaftTorque(sh, b) * b) lo = a;
    else hi = b;
  }
  const s = (lo + hi) / 2;
  const p = shaftTorque(sh, s) * s;
  return p > best ? { power: p, speed: s } : { power: best, speed: bestS };
}

/**
 * The speed a shaft settles at under a total load: the largest speed where the torque
 * still meets it. 0 when the stall torque is below the load (the network stalls).
 * Every model's torque falls with speed, so a bisection finds it.
 */
export function equilibriumSpeed(model: ProducerModel | Shaft, load: number, wind?: number): number {
  const sh = asShaft(model, wind);
  const free = shaftFree(sh);
  if (free <= 0) return 0;
  if (load <= 0) return free;
  if (shaftTorque(sh, 0) < load) return 0;
  let lo = 0;
  let hi = free;
  for (let i = 0; i < 60; i++) {
    const mid = (lo + hi) / 2;
    if (shaftTorque(sh, mid) >= load) lo = mid;
    else hi = mid;
  }
  return lo;
}

/** True when a load is more than the shaft can turn at all. */
export function stalls(model: ProducerModel | Shaft, load: number, wind?: number): boolean {
  const sh = asShaft(model, wind);
  return load > 0 && shaftTorque(sh, 0) < load;
}

// ---------------------------------------------------------------------------------------
// Wind

/** The game's altitude multiplier above sea level, never below 1. */
export function altitudeMultiplier(wind: Wind, blocksAboveSea: number): number {
  if (blocksAboveSea <= 0) return 1;
  return Math.max(1, wind.altitude.base + blocksAboveSea / wind.altitude.divisor);
}

/**
 * Wind at a rotor `blocksAboveSea` above sea level, given the sea-level wind. At or below
 * sea level this is the sea-level wind (the game weakens it further below sea level; the
 * page does not model that).
 */
export function windAtHeight(wind: Wind, seaLevelWind: number, blocksAboveSea: number): number {
  if (blocksAboveSea <= 0) return seaLevelWind;
  return Math.min(wind.altitude.cap, seaLevelWind * altitudeMultiplier(wind, blocksAboveSea));
}

export interface WindSample {
  wind: number;
  share: number;
}

/** The wind at a height as (wind, share of time) pairs, one per sea-level histogram bin, at bin centres. */
export function windDistribution(wind: Wind, blocksAboveSea: number): WindSample[] {
  const w = wind.histogram.binWidth;
  return wind.histogram.shares.map((share, i) => ({ wind: windAtHeight(wind, (i + 0.5) * w, blocksAboveSea), share }));
}

/** The distribution regrouped into bins of the histogram's width, for drawing; a spike at the cap stays in its bin. */
export function windHistogram(wind: Wind, blocksAboveSea: number): { from: number; to: number; share: number }[] {
  const w = wind.histogram.binWidth;
  const dist = windDistribution(wind, blocksAboveSea);
  const top = dist.reduce((m, d) => (d.share > 0 ? Math.max(m, d.wind) : m), 0);
  const n = Math.max(1, Math.floor(top / w + 1e-9) + 1);
  const bins = Array.from({ length: n }, (_, i) => ({ from: i * w, to: (i + 1) * w, share: 0 }));
  for (const d of dist) bins[Math.min(n - 1, Math.floor(d.wind / w + 1e-9))]!.share += d.share;
  return bins;
}

/** Share of time the wind at a height is at least `x`. */
export function shareAtLeast(dist: readonly WindSample[], x: number): number {
  return dist.reduce((sum, d) => (d.wind >= x - 1e-9 ? sum + d.share : sum), 0);
}

export function meanWind(dist: readonly WindSample[]): number {
  return dist.reduce((sum, d) => sum + d.wind * d.share, 0);
}

/**
 * How long a wind pattern lasts: the exporter's mean, and the range its distribution can
 * draw. The game's NatFloat: "invexp" and its stronger forms add var × a product of uniform
 * draws to avg, so they run from avg to avg + var (mean avg + var / 4 for invexp); "dirac" is
 * avg alone; the rest (uniform, triangle, the gaussians) spread avg ± var.
 */
export function patternDuration(p: WindPattern): { mean: number; min: number; max: number } {
  const avg = p.durationAvgHours;
  const v = Math.abs(p.durationVarHours);
  const dist = p.durationDist.toLowerCase();
  const [min, max] = dist === "dirac" || v === 0 ? [avg, avg] : dist.endsWith("invexp") ? [avg, avg + v] : [Math.max(0, avg - v), avg + v];
  return { mean: p.durationMeanHours, min, max };
}

export interface WindAverages {
  /** Peak power averaged over time. */
  peakPower: number;
  /** That as a share of the peak power at full wind. */
  ofFull: number;
  /** Free speed averaged over time. */
  freeSpeed: number;
}

/** A windmill's output averaged over the wind at a height. */
export function windAverages(model: WindModel, dist: readonly WindSample[]): WindAverages {
  let peak = 0;
  let free = 0;
  for (const d of dist) {
    peak += d.share * peakPower(model, d.wind).power;
    free += d.share * freeSpeed(model, d.wind);
  }
  const full = peakPower(model, FULL_WIND).power;
  return { peakPower: peak, ofFull: full > 0 ? peak / full : 0, freeSpeed: free };
}

/** A windmill under a load, averaged over the wind: speed (counting stalled time as 0) and share of time stalled. */
export function windUnderLoad(model: WindModel, load: number, dist: readonly WindSample[]): { speed: number; stalled: number } {
  let speed = 0;
  let stalled = 0;
  for (const d of dist) {
    speed += d.share * equilibriumSpeed(model, load, d.wind);
    if (stalls(model, load, d.wind)) stalled += d.share;
  }
  return { speed, stalled };
}

// ---------------------------------------------------------------------------------------
// Producers and consumers as the page shows them

export interface ProducerFigures {
  free: number;
  stall: number;
  peak: number;
}

export function producerFigures(model: ProducerModel, wind: number = FULL_WIND): ProducerFigures {
  return { free: freeSpeed(model, wind), stall: stallTorque(model, wind), peak: peakPower(model, wind).power };
}

/** A consumer's load as the explorer counts it: working, oiled or dry. Ranged loads count their high end. */
export function consumerLoad(c: Consumer, dry: boolean, end: "low" | "high" = "high"): number {
  const base = end === "high" && c.loadMax !== undefined ? c.loadMax : c.load;
  return dry && c.oil ? base * c.oil.dryMultiplier : base;
}

export interface LoadPick {
  id: string;
  count: number;
}

/** Total load of the picked consumers; unknown ids count nothing. */
export function totalLoad(consumers: readonly Consumer[], picks: readonly LoadPick[], dry: boolean): number {
  const byId = new Map(consumers.map((c) => [c.id, c]));
  return picks.reduce((sum, p) => {
    const c = byId.get(p.id);
    return c ? sum + consumerLoad(c, dry) * Math.max(0, p.count) : sum;
  }, 0);
}

/**
 * The helve hammer's load, the page's reference unit of work, when the export has it. The
 * game's block that takes the load is the toggle (`game:woodentoggle`, "Helve hammer toggle").
 */
export function referenceLoad(consumers: readonly Consumer[]): Consumer | null {
  return consumers.find((c) => c.category === "machine" && (/helvehammer|:woodentoggle$/.test(c.id) || /helve/i.test(c.name))) ?? null;
}

export const isWind = (p: Producer): p is Producer & { model: WindModel } => p.model.kind === "wind";

/** Clean axis ticks from 0 to at least `max`: 1, 2 or 5 times a power of ten. */
export function niceTicks(max: number, target = 5): number[] {
  if (!(max > 0)) return [0, 1];
  const raw = max / target;
  const mag = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map((k) => k * mag).find((s) => max / s <= target) ?? 10 * mag;
  const ticks: number[] = [];
  for (let v = 0; v < max + step * 0.999; v += step) ticks.push(Number(v.toPrecision(12)));
  return ticks;
}

/** A figure with three significant digits, trailing zeros dropped; "0" for zero. */
export function fmt(n: number, digits = 3): string {
  if (!Number.isFinite(n)) return "–";
  if (n === 0) return "0";
  const a = Math.abs(n);
  if (a >= 10 ** digits) return Math.round(n).toLocaleString("en");
  return String(Number(n.toPrecision(digits)));
}

export function pct(share: number): string {
  const v = share * 100;
  return `${v >= 10 || v === 0 ? Math.round(v) : Number(v.toPrecision(2))}%`;
}
