import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The slitting mill's shipped files, and the poses SlittingMill/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ (the entry shaft and the rectifier), ψ (the rectified shaft and the clutch's cup), W (knife
// strokes since the plate went in: the crankshaft, the knife, and the great wheel, arbors and pinions stepped once a stroke by
// gauges), k (a plate) and p (the clutch in); the work grows and shrinks by stretching gauges, and each nail goes down with the
// knife's edge and falls by gauges.
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
    expect(workEnd(path, 1)).toBe(5);
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
    expect(req).toEqual(new Set([null, "rolls", "pinions", "drive", "shear", "pusher", "plate"]));
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

  it("steps the rolls while the knife is up and stands them while it is down, as the site's maths poses them", () => {
    const at = (W: number): Pose => ({ theta: 2 * Math.PI * W, depth: 0, lifting: 0, travel: 2 * Math.PI * W, work: W, size: 1, presence: 1, feed: 0, oil: 0 });
    const idx = (id: string) => parts.findIndex((q) => q.id === id);
    const turn = (W: number, id: string) => { const m = partMatrices(parts, at(W), order, path)[idx(id)]!; return Math.atan2(m[1 * 4 + 2]!, m[1 * 4 + 1]!); };
    const knifeY = (W: number) => partMatrices(parts, at(W), order, path)[idx("knife")]![13]! * 16;
    const slit = (rig as unknown as { slit: { steps: number[]; stepWidth: number; cuts: number[] } }).slit;
    const wrap = (x: number) => ((((x + Math.PI) % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI)) - Math.PI;
    for (const s of slit.steps) {
      const a = s - slit.stepWidth / 2, b = s + slit.stepWidth / 2;
      // a step: 36 degrees of the bottom arbor while the knife is high
      expect(wrap(turn(b, "rollbottom") - turn(a, "rollbottom"))).toBeCloseTo(Math.PI / 5, 4);
      expect(knifeY(a)).toBeGreaterThan(0);
      // the knife down at the next whole stroke, the rolls standing from the step's end to there
      expect(wrap(turn(Math.min(Math.ceil(s), 5), "rollbottom") - turn(b, "rollbottom"))).toBeCloseTo(0, 4);
    }
    for (const c of slit.cuts) expect(knifeY(c)).toBeCloseTo(-2, 3);
  });

  it("pushes the strips out past the cut and draws the push bar back before the hand-out", () => {
    const at = (W: number): Pose => ({ theta: 2 * Math.PI * W, depth: 0, lifting: 0, travel: 2 * Math.PI * W, work: W, size: 1, presence: 1, feed: 0, oil: 0 });
    const idx = (id: string) => parts.findIndex((q) => q.id === id);
    const z = (W: number, id: string) => partMatrices(parts, at(W), order, path)[idx(id)]![14]! * 16;
    const slit = (rig as unknown as { slit: { push: number[]; pushBack: number[]; handOut: number } }).slit;
    // the strips (both layers) go the strips' length on, all of it past the cut, while the knife is up
    for (const id of ["rodsl", "rodsu"]) expect(z(slit.push[1]!, id) - z(slit.push[0]!, id)).toBeCloseTo(4, 3);
    // the push bar is back where it started by the hand-out
    expect(z(slit.pushBack[1]!, "pusher")).toBeCloseTo(0, 3);
    expect(z(slit.handOut, "pusher")).toBeCloseTo(z(0, "pusher"), 3);
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
