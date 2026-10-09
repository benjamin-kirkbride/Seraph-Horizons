import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, thetaTurns, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The cylinder borer's shipped files, and the poses CylinderBorer/tools/make_shape.py computes from them
// with machinegen's reference maths. Its rig reads W, its work (the cutter head's travel, in blocks), k (the
// cylinder: small or large), p, the oil and the axle's angle; the bar turns a quarter of the axle, and the
// feed, the drum, the weight, the ropes and the bore's skin are gauges on the head's travel.
// tools/tests/test_cylinderborer_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/cylinderborer-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/cylinderborer.json") as Shape;
type RefPose = { theta: number; travel: number; work: number; size: number; presence: number; oil: number; matrices: Record<string, number[][]> };
const reference = read("tests/CylinderBorer/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: p.work, size: p.size, presence: p.presence, feed: 0, oil: p.oil });

describe("the cylinder borer's rig against its rig-reference.json", () => {
  it("measures its work as the head's travel in blocks, has poses of both cylinders and none, and a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "cutter travel", unit: "blocks" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(17 / 16);
    expect(workEnd(path, 2)).toBe(21 / 16);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows W, the cylinder (size), its presence, the axle and the oil; no depth, lifting or feed
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: false, lifting: false, work: true, size: true, presence: true, oil: true });
    expect(rigInputs(parts).feed).toBeUndefined();
    // the bar turns a quarter of the axle: the θ slider spans four turns
    expect(thetaTurns(parts)).toBe(4);
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, W ${p.work}, k ${p.size}, p ${p.presence}, oil ${p.oil}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("fits the bearings, the bar, the feed and the cutter head, and each cylinder's set by its size", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "bearings", "bar", "feed", "cutterhead", "blanksmall", "blanklarge"]));
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    for (const c of ["cutter1", "cutter2", "cutter3", "cutter4"]) expect(byId[c]!.ride).toBe("head");
    expect(byId.chainsmall!.requires).toBe("blanksmall");
    expect(byId.chainlarge!.requires).toBe("blanklarge");
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
