import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The crusher's shipped files, and the poses Crusher/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads the axle's angle and travel (the power train, the jaw crusher and the rolls), and
// W, k and p for the stamp battery's load (camshaft turns; the stamps are gauges, a ramp each lift and drop).
// tools/tests/test_crusher_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/crusher-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/crusher.json") as Shape;
type RefPose = { theta: number; travel: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };
const reference = read("tests/Crusher/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: p.work, size: p.size, presence: p.presence, feed: 0, oil: 0 });

describe("the crusher's rig against its rig-reference.json", () => {
  it("counts a load in camshaft turns and has a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "load crushed", unit: "camshaft turns" });
    expect(workEnd(path, 1)).toBe(4);
    expect(workEnd(path, 2)).toBe(4);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows the axle, W, the load and its presence; no depth, lifting, feed or oil
    expect(rigInputs(parts)).toMatchObject({ theta: true, travel: true, depth: false, lifting: false, work: true, size: true, presence: true });
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p), order, path);
      parts.forEach((q, i) => {
        const got = rows(ms[i]!);
        const want = p.matrices[q.id]!;
        for (let r = 0; r < 3; r++)
          for (let c = 0; c < 4; c++) {
            const diff = Math.abs(got[r]![c]! - want[r]![c]!);
            worst = Math.max(worst, diff);
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}, W ${p.work}, k ${p.size}, p ${p.presence}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("fits each tier's set, the frame's parts with no requires", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "mortar", "camshaft", "stamps", "jaw", "rolls"]));
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    for (const id of ["entry", "rectb1", "idler", "rectb2", "line", "frame"]) expect(byId[id]!.requires ?? null).toBeNull();
    const tiers = (rig as unknown as { tiers: Record<string, string[]> }).tiers;
    expect([tiers["2"], tiers["3"], tiers["4"]]).toEqual([["mortar", "camshaft", "stamps"], ["jaw"], ["jaw", "rolls"]]);
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
