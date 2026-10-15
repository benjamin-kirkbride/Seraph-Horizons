import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, thetaTurns, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The rolling mill's shipped files, and the poses RollingMill/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads W, its work (passes, 0..3), k, p, the
// oil and the axle's angle (k: 1 an iron bar, 2 a steel one, the same passes); the rolls turn a third of the axle, the tumbling shaft once a pass on W, and the
// tables, the transfer, the tray and the bar are gauges on W (the bar's pieces grown and shrunk by stretching
// gauges as each pass rolls them).
// tools/tests/test_rollingmill_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/rollingmill-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/rollingmill.json") as Shape;
type RefPose = { theta: number; travel: number; work: number; size: number; presence: number; oil: number; matrices: Record<string, number[][]> };
const reference = read("tests/RollingMill/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: p.work, size: p.size, presence: p.presence, feed: 0, oil: p.oil });

describe("the rolling mill's rig against its rig-reference.json", () => {
  it("measures its work in passes, has poses with the bar and without, and a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "passes", unit: "passes" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(3);
    expect(workEnd(path, 2)).toBe(3);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows W, the bar (size), its presence, the axle and the oil; no depth, lifting or feed
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: false, lifting: false, work: true, size: true, presence: true, oil: true });
    expect(rigInputs(parts).feed).toBeUndefined();
    // the rolls turn a third of the axle: the θ slider spans three turns
    expect(thetaTurns(parts)).toBe(3);
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

  it("fits the pinions, the spindles, the rolls, the catcher's gear and the tray, and an iron or a steel bar as the work", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "pinions", "spindles", "rolls", "catcher", "tray", "bariron", "barsteel"]));
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    for (const metal of ["iron", "steel"])
      for (const id of ["billet", "bar1", "bar2", "bar3"]) expect(byId[id + metal]!.requires).toBe("bar" + metal);
    expect(shape.textures?.bar).toBe("game:block/metal/ingot/iron");
    expect(shape.textures?.barsteel).toBe("game:block/metal/ingot/steel");
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
