import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The gear cutter's shipped files, and the poses GearCutter/tools/make_shape.py computes from them
// with machinegen's reference maths. Its rig reads W, its work (teeth cut), k (the master fitted), p,
// the oil and the axle's angle and travel; every cam-driven part is a gauge with one window per tooth.
// tools/tests/test_gearcutter_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/gearcutter-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/gearcutter.json") as Shape;
type RefPose = { theta: number; travel: number; work: number; size: number; presence: number; oil: number; matrices: Record<string, number[][]> };
const reference = read("tests/GearCutter/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: p.work, size: p.size, presence: p.presence, feed: 0, oil: p.oil });

describe("the gear cutter's rig against its rig-reference.json", () => {
  it("counts its work in teeth, 12 or 20, has poses of both masters and none, and a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "teeth cut", unit: "teeth" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(12);
    expect(workEnd(path, 2)).toBe(20);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows T, the master (size), its presence, the axle and the oil; no depth, lifting or feed
    expect(rigInputs(parts)).toMatchObject({ theta: true, travel: true, depth: false, lifting: false, work: true, size: true, presence: true, oil: true });
    expect(new Set(reference.poses.map((p) => p.oil))).toEqual(new Set([0, 0.35, 1]));
    expect(rigInputs(parts).feed).toBeUndefined();
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}, W ${p.work}, k ${p.size}, p ${p.presence}, oil ${p.oil}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("makes each cam drum and the lift cam its own stage, turning with the shaft", () => {
    const req = Object.fromEntries(parts.map((p) => [p.id, p.requires ?? null]));
    expect([req.camshaft, req.camfeed, req.camindex, req.liftcam]).toEqual([null, "camfeed", "camindex", "liftcam"]);
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    for (const id of ["camfeed", "camindex", "liftcam"]) expect(byId[id]!.drivers, id).toEqual(byId.camshaft!.drivers);
  });

  it("fits the injection valve as the oiler's stage, the reservoir staying with the frame", () => {
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    expect([byId.valve!.requires, byId.valveplunger!.requires, byId.oillevel!.requires ?? null]).toEqual(["oiler", "oiler", null]);
    expect(byId.valveplunger!.drivers![0]).toMatchObject({ type: "slide", input: "work" });
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
