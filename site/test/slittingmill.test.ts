import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The slitting mill's shipped files, and the poses SlittingMill/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ (the entry shaft, the knife's crank and the rectifier's loose wheels), ψ (the rectified
// shaft, the great wheel, the arbors and the pinions), W (knife strokes since the plate went in), k (a plate) and p; the work
// grows and shrinks by stretching gauges and the cut nails fall by gauges.
// tools/tests/test_slittingmill_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/slittingmill-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/slittingmill.json") as Shape;
type RefPose = { theta: number; travel: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };
const reference = read("tests/SlittingMill/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: p.work, size: p.size, presence: p.presence, feed: 0, oil: 0 });

describe("the slitting mill's rig against its rig-reference.json", () => {
  it("counts its work in knife strokes to the hand-out, has poses with a plate and none, and a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "knife strokes", unit: "strokes" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(4.5);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows θ (so Play), W, the plate and its presence; no depth, lifting, feed or oil
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: false, lifting: false, work: true, size: true, presence: true });
    expect(rigInputs(parts).oil).toBeUndefined();
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, W ${p.work}, k ${p.size}, p ${p.presence}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("fits the slitters, the pinions, the drive and the shear, and draws the work in the plate's metal", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "rolls", "pinions", "drive", "shear", "plate"]));
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const r = parts[partOf(globs, f.chain)]!.requires ?? "frame";
      for (const code of textureCodes(f.element)) (codes[r] ??= new Set()).add(code);
    }
    expect([...codes.plate!]).toEqual(["metal"]);
    for (const [r, cs] of Object.entries(codes)) if (r !== "plate") expect(cs.has("metal")).toBe(false);
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
