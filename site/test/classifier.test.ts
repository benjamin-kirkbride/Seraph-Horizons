import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, openingFitted, stateGroups, type FittedStates, type StatesSpec } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The classifier's shipped files, and the poses Classifier/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads θ alone (the entry shaft; the screen's linkage as Fourier
// series; the trommel turned about its inclined axis between two ratio-0 swings, tier 4's outer jacket riding it); its scenario has one
// select of build states, the frame and a state per tier, which must fit the rig's `tiers`.
// tools/tests/test_classifier_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/classifier-rig.json") as Rig & { tiers: Record<string, string[]> };
const shape = read("assets/seraphhorizons/shapes/block/classifier.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Classifier/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "classifier")!;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel });

describe("the classifier's rig against its rig-reference.json", () => {
  it("reads the axle's angle and nothing else, and has a matrix for every part", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p), order);
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

  it("shows the power and the four ports as cells with their faces", () => {
    const { anchors } = discoverAnchors(rig);
    const cells = Object.fromEntries(anchors.filter((a) => a.kind === "cell").map((a) => [a.key, a.kind === "cell" ? [a.pos, a.face] : null]));
    expect(cells).toEqual({
      powerCell: [[3, 1, 0], "north"],
      feedCell: [[0, 2, 0], "west"],
      finesCell: [[1, 0, 0], "north"],
      oversizeCell: [[3, 0, 0], "east"],
      middlingsCell: [[2, 0, 1], "south"],
    });
    expect(anchors.filter((a) => a.kind === "point").map((a) => a.key).sort()).toEqual(["feed", "fines", "middlings", "oversize"]);
  });
});

describe("the classifier's build states in the viewer", () => {
  const requires = [...new Set(parts.map((p) => p.requires).filter((r): r is string => typeof r === "string"))];
  const states = model.scenario!.states as StatesSpec;
  const groups = stateGroups(states, requires);
  const group = (Array.isArray(states) ? states[0] : states) as FittedStates & { values?: string[] };

  it("has the frame and one state per tier, each fitting exactly that tier's set, and opens on tier 4", () => {
    expect(groups).toHaveLength(1);
    expect(group.options.map((o) => o.id)).toEqual(["frame", "tier2", "tier3", "tier4"]);
    expect(group.options[0]!.fitted).toEqual([]);
    for (const tier of ["2", "3", "4"]) expect(group.options.find((o) => o.id === `tier${tier}`)!.fitted).toEqual(rig.tiers[tier]);
    const { fitted, picked } = openingFitted(requires, [], groups);
    expect(picked).toEqual({ tier: "tier4" });
    expect(Object.keys(fitted).filter((k) => fitted[k]).sort()).toEqual([...rig.tiers["4"]!].sort());
  });

  it("takes the last tier's working parts off when the next tier's are fitted", () => {
    const all = Object.fromEntries(requires.map((r) => [r, true]));
    const tier2 = applyState(all, group.options.find((o) => o.id === "tier2")!, groups[0]!.owns);
    expect(Object.keys(tier2).filter((k) => tier2[k]).sort()).toEqual(["eccentric", "grizzly", "screen"]);
    const tier3 = applyState(tier2, group.options.find((o) => o.id === "tier3")!, groups[0]!.owns);
    expect(Object.keys(tier3).filter((k) => tier3[k]).sort()).toEqual(["bevel", "discharge", "trommel"]);
    // tier 4 keeps tier 3's trommel and spout and adds the outer jacket and the middlings spout
    const tier4 = applyState(tier3, group.options.find((o) => o.id === "tier4")!, groups[0]!.owns);
    expect(Object.keys(tier4).filter((k) => tier4[k]).sort()).toEqual(["bevel", "discharge", "jacket", "middlings", "trommel"]);
  });

  it("carries the jacket on the drum", () => {
    const jacket = parts.find((p) => p.id === "jacket")!;
    expect([jacket.requires, jacket.ride, jacket.drivers]).toEqual(["jacket", "drum", []]);
  });
});
