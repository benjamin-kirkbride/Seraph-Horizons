import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, openingFitted, stateGroups } from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { fitted, partMatrices, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The roaster's shipped files, and the poses Roaster/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone (the line shaft, the rabbles through their crown wheels, the feeder's
// crank and yoke); its scenario's states are the frame and tiers 2 to 4, each tier's parts in place of the
// last's. tools/tests/test_roaster_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/roaster-rig.json") as Rig & { tiers: Record<string, string[]> };
const shape = read("assets/seraphhorizons/shapes/block/roaster.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Roaster/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "roaster")!;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the roaster's rig against its rig-reference.json", () => {
  it("reads θ alone, over a cycle of three turns, and has a matrix for every part", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, travel: false, depth: false, lifting: false });
    expect(thetaTurns(parts)).toBe(3);
    expect(rig.work).toBeUndefined();
    expect(rig.trunkPath).toBeUndefined();
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta), order, null);
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
});

describe("the roaster's tiers", () => {
  const view = buildModelView(shape, rig, model.scenario);
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(model.scenario!.states, values);
  const opening = openingFitted(values, [], groups);
  const shown = (id: string) => {
    const g = groups[0]!;
    const ticked = applyState(opening.fitted, g.spec.options.find((o) => o.id === id)!, g.owns);
    return view.parts.filter((p) => fitted(p.part.requires, ticked)).map((p) => p.part.id);
  };

  it("lists the requires values tier by tier and opens at tier 4; the scenario fits the rig", () => {
    expect(values).toEqual(["stalls", "stallflue", "stallbin", "heaps", "hearth", "walls", "arch", "ironwork", "fire", "drive", "rabbles", "charger", "hopper", "feeder"]);
    expect(groups.map((g) => [g.spec.label, g.spec.default])).toEqual([["Tier", "tier4"]]);
    expect(opening.picked).toEqual({ states: "tier4" });
    expect(checkScenario(model.scenario!, rig, view.anchors, values)).toEqual([]);
  });

  it("names the same sets as the rig's tiers", () => {
    const options = Object.fromEntries(groups[0]!.spec.options.map((o) => [o.id, o.fitted]));
    expect(Object.keys(options)).toEqual(["frame", "tier2", "tier3", "tier4"]);
    expect(options.frame).toEqual([]);
    for (const k of ["2", "3", "4"]) expect(options[`tier${k}`]).toEqual(rig.tiers[k]);
  });

  it("shows the frame alone, then the stalls, then the furnace with its charging box, then with its hopper and feeder", () => {
    expect(shown("frame")).toEqual(["frame"]);
    expect(shown("tier2")).toEqual(["stalls", "stallflue", "stallbin", "heaps", "frame"]);
    const furnace = ["hearth", "walls", "arch", "ironwork", "fire", "lineshaft", "pedestals", "rabble1", "rabble2", "rabblemounts"];
    expect(shown("tier3")).toEqual([...furnace, "charger", "frame"]);
    expect(shown("tier4")).toEqual([...furnace, "hopper", "crank", "yoke", "frame"]);
  });

  it("draws the power cell, the infeed and the output as the frame's anchors", () => {
    const keys = view.anchors.map((a) => a.key);
    for (const k of ["powerCell", "infeedCell", "outputCell", "infeed", "output", "sulfur", "smoke"]) expect(keys).toContain(k);
    const cell = (k: string) => view.anchors.find((a) => a.key === k) as { pos: number[]; face?: string };
    expect([cell("powerCell").pos, cell("powerCell").face]).toEqual([[0, 2, 0], "north"]);
    expect([cell("infeedCell").pos, cell("infeedCell").face]).toEqual([[0, 2, 4], "up"]);
    expect([cell("outputCell").pos, cell("outputCell").face]).toEqual([[1, 0, 2], "east"]);
  });
});
