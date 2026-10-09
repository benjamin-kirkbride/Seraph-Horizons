// The power data: the export's optional `power` section, which prepare-data copies to
// `data/<version>/power.json` as it is. The exporter (tools/recipe-export/PowerSection.cs)
// only extracts parameters from the running server; every derived figure (torque at a
// speed, peak power, equilibrium under a load, wind-averaged output) is computed in the
// app by src/lib/power.ts, so the maths lives in one language. docs/recipe-browser/power.md
// explains the models and where each number comes from.
//
// Units are the game's mechanical network units: "speed" is network speed (what the
// network's `Speed` holds), "torque" and "load" are what `GetTorque` returns and
// `GetResistance` returns. In-game kN figures (windmill block info only) are cosmetic and
// carried separately as `shownKN`.

/** Where a figure was read from, so the page can say so and a test can catch fallbacks. */
export interface PowerSource {
  /** What the figure is, e.g. "SailCenteredModifier". */
  what: string;
  /** Where it came from: "ModConfig/millwright.json", "block attributes", "code constant (BEBehaviorMPPulverizer)". */
  from: string;
  /** True when the exporter could not read the live value and used its built-in default. */
  fallback?: boolean;
}

/** torque(s) = max(0, targetSpeed − s) × torqueFactor. The game's rotor, the water wheel, the hand crank, Yang's engines. */
export interface RotorModel {
  kind: "rotor";
  targetSpeed: number;
  torqueFactor: number;
}

/**
 * A windmill: a rotor whose target speed is min(speedCap, wind × speedPerWind).
 * `turbulencePenalty`: the game halves a vanilla windmill's torque factor when another
 * windmill is within 1.5 × sails blocks (Millwright's rotors have no such check).
 */
export interface WindModel {
  kind: "wind";
  speedPerWind: number;
  speedCap: number;
  torqueFactor: number;
  turbulencePenalty: boolean;
  /** Sails fitted for this entry, and what one sail adds, so the page can offer fewer. */
  sails?: { count: number; max: number; torqueFactorPerSail: number };
}

/**
 * ppex's engine generator, a constant-power source:
 * torque(s) = budget / max(s, minSpeed) for s ≤ taperFrom, then falling linearly to 0 at shaftSpeed.
 */
export interface ConstantPowerModel {
  kind: "constantPower";
  budget: number;
  shaftSpeed: number;
  minSpeed: number;
  taperFrom: number;
}

export type ProducerModel = RotorModel | WindModel | ConstantPowerModel;

export interface Producer {
  /** Unique within the export, stable across versions: "game:windmillrotor-metal@10". */
  id: string;
  /** An item code the page links to (a block's north variant), or null. */
  item: string | null;
  /** Mod id of the mod that adds it. */
  mod: string;
  /** English name with its setup: "Windmill rotor (metal), 10 sails". */
  name: string;
  family: "wind" | "water" | "steam" | "muscle";
  model: ProducerModel;
  /** What the game's block info reads as "Sails power output", or null where it shows none. */
  shownKN: number | null;
  /** The conditions the figures assume: "wind ≥ 60%", "steam in its pressure band", "3 rapid water cells". */
  conditions: string;
  /** Running cost, if any: "2.5 satiety per second", "30 L/s of steam". */
  cost?: string;
  sources: PowerSource[];
}

export interface Consumer {
  /** Unique within the export: "game:quern", "seraphhorizons:millframe". */
  id: string;
  item: string | null;
  mod: string;
  name: string;
  /** "machine" works; "transmission" only passes power (friction); "brake" stops it. */
  category: "machine" | "transmission" | "brake";
  /** Load while working, fully oiled. For a ranged load (a blower against back-pressure), the low end. */
  load: number;
  /** The high end of a ranged load; absent when the load is fixed. */
  loadMax?: number;
  /** Load while idle, unfinished or empty, where the game sets one. */
  idleLoad?: number;
  /** Present on machines with an oil tank (seraphhorizons MachineOil): dry load = load × dryMultiplier. */
  oil?: { dryMultiplier: number; tank: number };
  /** How the load is made up: "0.05 + 0.05 per atm of back-pressure, 2 atm at most". */
  note?: string;
  sources: PowerSource[];
}

export interface WindPattern {
  code: string;
  name: string;
  strengthAvg: number;
  strengthVar: number;
  durationAvgHours: number;
  durationVarHours: number;
  /** Mean duration in hours from the declared distribution (invexp: avg + var / 4). */
  durationMeanHours: number;
  /** The duration's distribution as the config declares it, lower case: "invexp", "uniform", ... */
  durationDist: string;
  /** Share of game time the pattern is active, from the simulation (0–1). */
  share: number;
  /** True when the pattern adds the game's gust noise on top of its base strength. */
  gusts: boolean;
}

export interface Wind {
  /** The simulation: game years run, samples per game hour, RNG seed. Deterministic. */
  simulation: { years: number; samplesPerHour: number; seed: number };
  patterns: WindPattern[];
  /**
   * Wind at the rotor's height = sea-level wind × max(1, base + blocksAboveSeaLevel / divisor),
   * capped at `cap` (the game's WeatherSimulationRegion.GetWindSpeed above sea level).
   */
  altitude: { base: number; divisor: number; cap: number };
  /**
   * Sea-level wind speed distribution, share of time per bin, bins of `binWidth` from 0.
   * Negative strengths (the still pattern) are counted in the first bin. Shares sum to 1.
   */
  histogram: { binWidth: number; shares: number[] };
  /** Mean sea-level wind over the simulation. */
  mean: number;
  /** Where the pattern settings and the model's constants come from. */
  sources?: PowerSource[];
}

export interface PowerData {
  producers: Producer[];
  consumers: Consumer[];
  wind: Wind | null;
}
