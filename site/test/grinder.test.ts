import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Manifest } from "../src/lib/model-manifest.ts";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { applyState, checkScenario, openingFitted, stateGroups, type FittedStates } from "../src/lib/model-scenario.ts";
import { compileGlobs, fitted, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The grinder's shipped files, and the poses Grinder/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone (the axle's angle); every part turns with it at its gearing's ratio or
// stands still. The model holds every tier's parts; the manifest's states fit the frame alone or one tier.
// tools/tests/test_grinder_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type Tier = { tier: number; name: string; fitted: string[] };
const rig = read("assets/seraphhorizons/config/grinder-rig.json") as Rig & { tiers: Tier[] };
const shape = read("assets/seraphhorizons/shapes/block/grinder.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Grinder/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as Manifest;
const model = manifest.models.find((m) => m.id === "grinder")!;
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel = Math.abs(theta)): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the grinder's rig against its rig-reference.json", () => {
  it("reads the axle's angle and nothing else, over a six-turn cycle", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(rig.work).toBeUndefined();
    expect(rig.trunkPath).toBeUndefined();
    expect(thetaTurns(parts)).toBe(6);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta, p.travel), order, null);
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

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});

describe("the grinder's tiers and the viewer's states", () => {
  const requires = requiresValues(parts);
  const states = model.scenario!.states as FittedStates;

  it("lists each tier's fitted parts once, in tier order, and every requires value is a tier's", () => {
    expect(rig.tiers.map((t) => t.tier)).toEqual([2, 3, 4]);
    const all = rig.tiers.flatMap((t) => t.fitted);
    expect(new Set(all).size).toBe(all.length);
    expect(new Set(all)).toEqual(new Set(requires));
    for (const t of rig.tiers) for (const r of t.fitted) expect(r.startsWith(`t${t.tier}`)).toBe(true);
  });

  it("has a state for the frame alone and one for each tier, fitting exactly that tier's set", () => {
    expect(states.options.map((o) => o.id)).toEqual(["frame", "t2", "t3", "t4"]);
    expect(states.options[0]!.fitted).toEqual([]);
    rig.tiers.forEach((t, i) => expect([...states.options[i + 1]!.fitted].sort()).toEqual([...t.fitted].sort()));
    expect(checkScenario(model.scenario!, rig, discoverAnchors(rig).anchors, requires)).toEqual([]);
  });

  it("draws in each state the frame's parts and only that tier's", () => {
    const groups = stateGroups(states, requires);
    const { fitted: opening, picked } = openingFitted(requires, [], groups);
    expect(picked).toEqual({ states: "t4" });
    for (const [i, option] of states.options.entries()) {
      const ticked = applyState(opening, option, groups[0]!.owns);
      const shown = parts.filter((p) => fitted(p.requires, ticked)).map((p) => p.id);
      const tier = i === 0 ? null : `t${i + 1}`;
      expect(shown.filter((id) => !id.startsWith("t")).sort()).toEqual(["frame", "shaft"]);
      expect(shown.every((id) => !id.startsWith("t") || id.startsWith(tier ?? "-"))).toBe(true);
      if (tier) expect(shown.filter((id) => id.startsWith(tier)).length).toBe(parts.filter((p) => p.id.startsWith(tier)).length);
    }
  });

  it("puts its anchors on the footprint: the power face on the south side, the ore in at the west and out at the east", () => {
    const keys = discoverAnchors(rig).anchors.map((a) => a.key);
    expect(keys).toEqual(expect.arrayContaining(["powerCell", "infeedCell", "outputCell", "returnCell", "output"]));
    const r = rig as unknown as Record<string, unknown>;
    expect([r.powerFace, r.infeedFace, r.outputFace, r.returnFace]).toEqual(["south", "up", "east", "up"]);
    expect(rig.cells!.length).toBe(18);
    expect(rig.cells!.some((c) => c.lid !== undefined)).toBe(false);
  });
});
