import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, initialFitted, stateGroups, type FittedStates, type Scenario } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The parting furnace's shipped files, and the poses PartingFurnace/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads theta alone: the line shaft and the bevel pinion turn with it, the
// blowing tub's yoke slides with the crank, the stirrer turns at half the shaft's speed; every other part stands
// still and is fitted by tier. tools/tests/test_partingfurnace_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/partingfurnace-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/partingfurnace.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/PartingFurnace/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "parting-furnace")!;
const parts = rig.parts!;
const order = rideOrder(parts);
const tiers = (rig as unknown as { tiers: Record<string, string[]> }).tiers;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: { theta: number; travel: number }): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the parting furnace's rig against its rig-reference.json", () => {
  it("reads theta alone, over a two-turn cycle (the stirrer turns once in two), and has a matrix for every part", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(thetaTurns(parts)).toBe(2);
    expect(new Set(reference.poses.map((p) => Math.sign(p.theta)))).toEqual(new Set([-1, 0, 1]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p), order, null);
      parts.forEach((q, i) => {
        const got = rows(ms[i]!);
        const want = p.matrices[q.id]!;
        for (let r = 0; r < 3; r++)
          for (let c = 0; c < 4; c++) {
            const diff = Math.abs(got[r]![c]! - want[r]![c]!);
            worst = Math.max(worst, diff);
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("gives every element a part and every part an element, and fits its sets tier by tier", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const byTier = Object.keys(tiers).sort().flatMap((k) => tiers[k]!);
    const seen: string[] = [];
    for (const p of parts) if (p.requires && !seen.includes(p.requires)) seen.push(p.requires);
    expect(seen).toEqual(byTier);
    for (const [k, values] of Object.entries(tiers)) for (const v of values) expect(v.startsWith(`t${k}`)).toBe(true);
    expect(parts.at(-1)).toMatchObject({ id: "frame", requires: null });
  });
});

describe("the parting furnace in the viewer", () => {
  const requires = [...new Set(parts.map((p) => p.requires).filter((r): r is string => !!r))];
  const scenario = model.scenario as Scenario;

  it("draws its power cell, its chutes' cells and their ends, and lists its tiers as not drawn", () => {
    const { anchors, unrecognised } = discoverAnchors(rig);
    expect(anchors.map((a) => [a.kind, a.key])).toEqual([
      ["cell", "powerCell"],
      ["cell", "inputCell"],
      ["cell", "outputCell"],
      ["point", "input"],
      ["point", "output"],
    ]);
    expect(unrecognised).toEqual(["tiers"]);
    expect(checkScenario(scenario, rig, anchors, requires)).toEqual([]);
  });

  it("builds up tier by tier: the frame, then each tier with all before it", () => {
    const states = scenario.states as FittedStates;
    const groups = stateGroups(states, requires);
    expect(groups).toHaveLength(1);
    let want: string[] = [];
    const cumulative = [[] as string[]];
    for (const k of Object.keys(tiers).sort()) cumulative.push((want = [...want, ...tiers[k]!]));
    expect(states.options.map((o) => o.fitted)).toEqual(cumulative);
    expect(states.default).toBe("tier4");
    const start = initialFitted(requires);
    const tier3 = applyState(start, states.options.find((o) => o.id === "tier3")!, requires);
    expect(Object.entries(tier3).filter(([, on]) => on).map(([r]) => r)).toEqual([...tiers["2"]!, ...tiers["3"]!]);
  });
});
