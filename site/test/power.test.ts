import { describe, expect, it } from "vitest";
import {
  altitudeMultiplier,
  consumerLoad,
  equilibriumSpeed,
  fmt,
  freeSpeed,
  niceTicks,
  patternDuration,
  peakPower,
  referenceLoad,
  shareAtLeast,
  stallTorque,
  stalls,
  torqueAt,
  totalLoad,
  windAtHeight,
  windAverages,
  windDistribution,
  windHistogram,
  windUnderLoad,
  withSails,
} from "../src/lib/power.ts";
import type { ConstantPowerModel, RotorModel, WindModel } from "../src/lib/power-data.ts";
import { POWER, WIND } from "./fixtures/power.ts";

/** Within `rel` of `want` (1% by default), the tolerance of the hand-worked figures. */
function near(got: number, want: number, rel = 0.01) {
  expect(Math.abs(got - want)).toBeLessThanOrEqual(Math.abs(want) * rel + 1e-9);
}

const rotor = (targetSpeed: number, torqueFactor: number): RotorModel => ({ kind: "rotor", targetSpeed, torqueFactor });
const wind = (speedPerWind: number, speedCap: number, torqueFactor: number): WindModel => ({
  kind: "wind",
  speedPerWind,
  speedCap,
  torqueFactor,
  turbulencePenalty: false,
});
const engine = (budget: number, shaftSpeed: number): ConstantPowerModel => ({
  kind: "constantPower",
  budget,
  shaftSpeed,
  minSpeed: 0.25,
  taperFrom: (shaftSpeed * 2) / 3,
});

const handCrank = rotor(0.5, 0.5);
const waterWheel = rotor(0.3, 7.24);
const metalWindmill = wind(1, 0.6, 3.125);
const doubleWindmill = wind(4, 2.4, 5);
const watt = engine(0.6, (0.8 * Math.PI) / 5);
const cornish = engine(1.6, (1.3 * Math.PI) / 5);
const yang = rotor(0.6, 1.1);

describe("torque curves", () => {
  it("a rotor gives torque in proportion to how far it is below its target", () => {
    expect(torqueAt(handCrank, 0)).toBe(0.25);
    expect(torqueAt(handCrank, 0.25)).toBe(0.125);
    expect(torqueAt(handCrank, 0.5)).toBe(0);
    expect(torqueAt(handCrank, 0.7)).toBe(0);
  });

  it("a windmill aims for wind × speed per wind, capped", () => {
    expect(freeSpeed(metalWindmill, 0.3)).toBeCloseTo(0.3);
    expect(freeSpeed(metalWindmill, 1)).toBeCloseTo(0.6);
    expect(freeSpeed(doubleWindmill, 0.5)).toBeCloseTo(2);
    expect(torqueAt(metalWindmill, 0, 0)).toBe(0);
  });

  it("a constant-power engine gives budget / speed, floored at its minimum speed, then tapers to its shaft speed", () => {
    expect(torqueAt(watt, 0)).toBeCloseTo(2.4);
    expect(torqueAt(watt, 0.25)).toBeCloseTo(2.4);
    expect(torqueAt(watt, 0.3)).toBeCloseTo(2);
    expect(torqueAt(watt, watt.taperFrom)).toBeCloseTo(0.6 / watt.taperFrom);
    const mid = (watt.taperFrom + watt.shaftSpeed) / 2;
    expect(torqueAt(watt, mid)).toBeCloseTo((0.6 / mid) * 0.5);
    expect(torqueAt(watt, watt.shaftSpeed)).toBe(0);
    expect(torqueAt(watt, 1)).toBe(0);
  });
});

describe("reference figures", () => {
  it("hand crank", () => {
    near(freeSpeed(handCrank), 0.5);
    near(stallTorque(handCrank), 0.25);
    near(peakPower(handCrank).power, 0.03125);
    near(peakPower(handCrank).speed, 0.25);
  });

  it("water wheel, and six helve hammers on it", () => {
    near(stallTorque(waterWheel), 2.17);
    near(peakPower(waterWheel).power, 0.163);
    near(equilibriumSpeed(waterWheel, 6 * 0.125), 0.196);
  });

  it("vanilla metal windmill at full wind", () => {
    near(freeSpeed(metalWindmill), 0.6);
    near(stallTorque(metalWindmill), 1.875);
    near(peakPower(metalWindmill).power, 0.28125);
    near(equilibriumSpeed(metalWindmill, 0.5), 0.44);
  });

  it("Millwright double windmill", () => {
    near(stallTorque(doubleWindmill), 12);
    near(peakPower(doubleWindmill).power, 7.2);
    near(equilibriumSpeed(doubleWindmill, 2), 2);
  });

  it("Watt engine", () => {
    near(stallTorque(watt), 2.4);
    near(peakPower(watt).power, 0.6);
    near(freeSpeed(watt), 0.5027);
    near(equilibriumSpeed(watt, 1.5), 0.354);
    // Under a light load the engine runs in its taper: 0.6 / s × (0.5027 − s) / 0.1676 = 0.25.
    near(equilibriumSpeed(watt, 0.25), 0.4699);
  });

  it("Cornish engine (high)", () => {
    near(stallTorque(cornish), 6.4);
    near(peakPower(cornish).power, 1.6);
    near(freeSpeed(cornish), 0.817);
    near(equilibriumSpeed(cornish, 1.5), 0.651);
  });

  it("Yang's standard engine", () => {
    near(stallTorque(yang), 0.66);
    near(peakPower(yang).power, 0.099);
    near(equilibriumSpeed(yang, 0.5), 0.145);
  });
});

describe("equilibrium", () => {
  it("is 0 when the load is more than the stall torque, and the free speed with no load", () => {
    expect(equilibriumSpeed(handCrank, 0.3)).toBe(0);
    expect(stalls(handCrank, 0.3)).toBe(true);
    expect(stalls(handCrank, 0.2)).toBe(false);
    expect(equilibriumSpeed(handCrank, 0)).toBe(0.5);
  });

  it("adds the torques of producers on one shaft", () => {
    const shaft = { models: [handCrank, waterWheel] };
    // Below 0.3 both push: 0.5(0.5 − s) + 7.24(0.3 − s) = 1 → s = (0.25 + 2.172 − 1) / 7.74.
    near(equilibriumSpeed(shaft, 1), (0.25 + 2.172 - 1) / 7.74);
    // A small load is carried past the wheel's target by the crank alone.
    near(equilibriumSpeed(shaft, 0.05), 0.4);
    near(peakPower(shaft).power, Math.max(...[0.1, 0.15, 0.2].map((s) => s * (0.5 * (0.5 - s) + 7.24 * (0.3 - s)))), 0.02);
    expect(stalls(shaft, 2.5)).toBe(true);
  });
});

describe("sails", () => {
  it("fewer sails take what each sail adds off the torque factor", () => {
    const m = POWER.producers[2]!.model as WindModel;
    expect(withSails(m, 10)).toBe(m);
    near(withSails(m, 6).torqueFactor, 3.125 - 4 * 0.3125);
    expect(withSails(m, 6).sails!.count).toBe(6);
    expect(withSails(m, 40).torqueFactor).toBe(3.125);
    expect(withSails(m, -1).torqueFactor).toBe(0);
  });
});

describe("wind", () => {
  it("strengthens with height above sea level, never below 1 and capped", () => {
    expect(altitudeMultiplier(WIND, 0)).toBe(1);
    expect(altitudeMultiplier(WIND, -20)).toBe(1);
    expect(altitudeMultiplier(WIND, 5)).toBe(1);
    near(altitudeMultiplier(WIND, 50), 1.4);
    near(windAtHeight(WIND, 0.5, 50), 0.7);
    expect(windAtHeight(WIND, 0.5, -10)).toBe(0.5);
    expect(windAtHeight(WIND, 1.4, 150)).toBe(1.5);
  });

  it("spreads the sea-level histogram to a height at bin centres", () => {
    const sea = windDistribution(WIND, 0);
    expect(sea).toHaveLength(10);
    near(sea[2]!.wind, 0.25);
    near(shareAtLeast(sea, 0.6), 0.25);
    near(shareAtLeast(sea, 0.4), 0.75);
    const high = windDistribution(WIND, 50);
    near(high[4]!.wind, 0.63);
    near(shareAtLeast(high, 0.6), 0.75);
    const bins = windHistogram(WIND, 50);
    expect(bins.reduce((s, b) => s + b.share, 0)).toBeCloseTo(1);
    // 0.25 × 1.4 = 0.35 lands in the 0.3–0.4 bin, 0.95 × 1.4 = 1.33 in 1.3–1.4.
    expect(bins[3]!.share).toBeCloseTo(0.25);
    expect(bins.at(-1)!.from).toBeCloseTo(1.3);
  });

  it("gives a pattern's duration as the mean and range of the game's invexp draw", () => {
    const p = { ...WIND.patterns[0]!, durationAvgHours: 6, durationVarHours: 48, durationMeanHours: 18, durationDist: "invexp" };
    expect(patternDuration(p)).toEqual({ mean: 18, min: 6, max: 54 });
    expect(patternDuration({ ...p, durationDist: "stronginvexp", durationMeanHours: 12 })).toEqual({ mean: 12, min: 6, max: 54 });
    expect(patternDuration({ ...p, durationDist: "uniform", durationMeanHours: 6 })).toEqual({ mean: 6, min: 0, max: 54 });
    expect(patternDuration({ ...p, durationDist: "dirac", durationMeanHours: 6 })).toEqual({ mean: 6, min: 6, max: 6 });
  });

  it("averages a windmill's peak power and free speed over time", () => {
    // Peak power at wind w is TF × min(cap, w × spw)² / 4.
    const peak = (m: WindModel, w: number) => (m.torqueFactor * Math.min(m.speedCap, w * m.speedPerWind) ** 2) / 4;
    const avg = (m: WindModel, ws: number[]) => 0.25 * peak(m, ws[0]!) + 0.5 * peak(m, ws[1]!) + 0.25 * peak(m, ws[2]!);
    const sea = windAverages(metalWindmill, windDistribution(WIND, 0));
    near(sea.peakPower, avg(metalWindmill, [0.25, 0.45, 0.95]));
    near(sea.ofFull, sea.peakPower / 0.28125);
    near(sea.freeSpeed, 0.25 * 0.25 + 0.5 * 0.45 + 0.25 * 0.6);
    const high = windAverages(doubleWindmill, windDistribution(WIND, 50));
    near(high.peakPower, avg(doubleWindmill, [0.35, 0.63, 1.33]));
    expect(high.peakPower).toBeGreaterThan(windAverages(doubleWindmill, windDistribution(WIND, 0)).peakPower);
  });

  it("averages speed under a load, stalled time counting as 0", () => {
    // Stall torque at wind w is 3.125 × min(0.6, w): 0.78 at 0.25, 1.41 at 0.45, 1.875 at 0.95.
    const r = windUnderLoad(metalWindmill, 1, windDistribution(WIND, 0));
    near(r.stalled, 0.25);
    near(r.speed, 0.5 * (0.45 - 1 / 3.125) + 0.25 * (0.6 - 1 / 3.125));
  });
});

describe("consumers", () => {
  const [hammer, quern, mill, blower] = POWER.consumers;

  it("count a ranged load at its high end, and a dry machine at its multiplier", () => {
    expect(consumerLoad(blower!, false)).toBe(0.15);
    expect(consumerLoad(blower!, false, "low")).toBe(0.05);
    expect(consumerLoad(mill!, false)).toBe(0.4);
    expect(consumerLoad(mill!, true)).toBe(0.8);
    expect(consumerLoad(quern!, true)).toBe(0.085);
  });

  it("add up by count, ignoring unknown ids", () => {
    near(
      totalLoad(POWER.consumers, [{ id: "game:helvehammer", count: 4 }, { id: "seraphhorizons:millframe", count: 1 }, { id: "nope", count: 3 }], true),
      4 * 0.125 + 0.8,
    );
    expect(referenceLoad(POWER.consumers)).toBe(hammer);
    expect(referenceLoad([])).toBeNull();
    const toggle = { ...hammer!, id: "game:woodentoggle", name: "Helve hammer toggle" };
    expect(referenceLoad([quern!, toggle])).toBe(toggle);
  });
});

describe("formatting", () => {
  it("picks clean ticks", () => {
    expect(niceTicks(2.4)).toEqual([0, 0.5, 1, 1.5, 2, 2.5]);
    expect(niceTicks(12)).toEqual([0, 2.5, 5, 7.5, 10, 12.5]);
    expect(niceTicks(0)).toEqual([0, 1]);
  });

  it("rounds to three significant digits", () => {
    expect(fmt(0.28125)).toBe("0.281");
    expect(fmt(2.4)).toBe("2.4");
    expect(fmt(0)).toBe("0");
    expect(fmt(12345)).toBe("12,345");
  });
});
