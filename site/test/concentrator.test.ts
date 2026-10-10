import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, openingFitted, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { compileGlobs, fitted, flattenShape, partMatrices, partOf, rideOrder, rigInputs, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The concentrator's shipped files, and the poses Concentrator/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads θ (the axle: the entry shaft and the rectifier's gears) and ψ,
// the axle's travel either way (the main shaft and everything belted from it, which turn one way whichever
// way the axle does); no work, no depth. tools/tests/test_concentrator_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/concentrator-rig.json") as Rig & { tiers: { tier: number; name: string; requires: string[] }[] };
const shape = read("assets/seraphhorizons/shapes/block/concentrator.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Concentrator/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel: number): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the concentrator's rig against its rig-reference.json", () => {
  it("reads the axle's angle and its travel, nothing else, and has a matrix for every part", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: true, depth: false, lifting: false });
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    expect(reference.poses.some((p) => p.theta < 0)).toBe(true);
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("turns everything after the rectifier the same way whichever way the axle turns", () => {
    const fwd = partMatrices(parts, pose(1.3, 1.3), order, null);
    const back = partMatrices(parts, pose(-1.3, 1.3), order, null);
    const after = new Set(["entry", "rectb1", "idler", "rectb2"]);
    parts.forEach((q, i) => {
      if (after.has(q.id)) return;
      fwd[i]!.forEach((v, j) => expect(Math.abs(v - back[i]![j]!)).toBeLessThan(1e-12));
    });
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});

describe("the concentrator's tiers in the viewer", () => {
  const model = manifest.models.find((m) => m.id === "concentrator")!;
  const view = buildModelView(shape, rig, model.scenario);
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(model.scenario!.states, values);
  const opening = openingFitted(values, [], groups);
  const shown = (id: string) => {
    const state = groups[0]!.spec.options.find((o) => o.id === id)!;
    const ticked = applyState(opening.fitted, state, groups[0]!.owns);
    return view.parts.filter((p) => fitted(p.part.requires, ticked) && statesShow(p.part.requires, ticked, groups)).map((p) => p.part.requires ?? null);
  };

  it("has one select, the tier, from the frame alone to tier 4 with its dressed plates, opening there", () => {
    expect(values).toEqual(["longtom", "jig", "table", "vanner", "plates", "dressing"]);
    expect(groups).toHaveLength(1);
    expect(groups[0]!.spec.label).toBe("Tier");
    expect(groups[0]!.spec.options.map((o) => o.id)).toEqual(["frame", "tier1", "tier2", "tier2plates", "tier3", "tier3plates", "tier4", "tier4plates"]);
    expect(opening.picked).toEqual({ states: "tier4plates" });
    expect(checkScenario(model.scenario!, rig, view.anchors, values)).toEqual([]);
  });

  it("fits each tier's set alone, tier 4 the table's and the vanner's, as the rig's tiers say", () => {
    for (const t of rig.tiers) {
      // the plain tier states fit the tier's set alone; the plates come with their own states
      const opt = groups[0]!.spec.options.find((o) => o.id === `tier${t.tier}`)!;
      expect(opt.fitted).toEqual(t.requires);
      expect(new Set(shown(`tier${t.tier}`))).toEqual(new Set([null, ...t.requires]));
    }
    expect(new Set(shown("frame"))).toEqual(new Set([null]));
  });

  it("fits the amalgamation plates on a tier from 2, bare at tier 2 and dressed at tiers 3 and 4", () => {
    expect(new Set(shown("tier2plates"))).toEqual(new Set([null, "jig", "plates"]));
    expect(new Set(shown("tier3plates"))).toEqual(new Set([null, "table", "plates", "dressing"]));
    expect(new Set(shown("tier4plates"))).toEqual(new Set([null, "table", "vanner", "plates", "dressing"]));
    expect(new Set(shown("tier1"))).not.toContain("plates");
  });

  it("anchors the fixed points: the power, water and feed cells, the spouts and the outlets", () => {
    const keys = view.anchors.map((a) => a.key);
    for (const k of ["powerCell", "waterCell", "feedCell", "feedSpout", "waterSpout", "concentrate", "concentrateSide", "tailings", "tailingsSide"])
      expect(keys).toContain(k);
  });
});
