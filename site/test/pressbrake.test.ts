import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The press brake's shipped files, and the poses PressBrake/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads W (the fold cycle of one half plate: one fold, one angle), k (its metal), p and θ
// (the lever's work, carried by a ratio-0 rotate so the viewer offers Play); every motion is a gauge.
// tools/tests/test_pressbrake_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/pressbrake-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/pressbrake.json") as Shape;
type RefPose = { theta: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };
const reference = read("tests/PressBrake/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: Math.abs(p.theta), work: p.work, size: p.size, presence: p.presence, feed: 0, oil: 0 });

describe("the press brake's rig against its rig-reference.json", () => {
  it("counts its work in plates, one fold cycle a plate of either metal, and has a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "fold", unit: "plates" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(1);
    expect(workEnd(path, 2)).toBe(1);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows θ (so Play), W, the metal and its presence; no depth, lifting, feed or oil
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: false, lifting: false, work: true, size: true, presence: true });
    expect(rigInputs(parts).oil).toBeUndefined();
    expect(rigInputs(parts).feed).toBeUndefined();
    // a hand machine: no power cell
    expect((rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
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

  it("moves nothing with θ: the lever's clock", () => {
    const at = (theta: number) => partMatrices(parts, { theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0.3, size: 2, presence: 1, feed: 0, oil: 0 }, order, path);
    const a = at(0);
    const b = at(5.1);
    a.forEach((m, i) => m.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
  });

  it("fits the parts in build order, the sheet by metal, and gives the edges their own texture code and the screws (metal parts) cupronickel", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "screws", "edge", "platelead", "platecopper"]));
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    expect(byId.lever!.ride).toBe("leaf");
    expect(byId.leafedge!.ride).toBe("leaf");
    expect(byId.baredge!.ride).toBe("bar");
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const r = parts[partOf(globs, f.chain)]!.requires ?? "frame";
      for (const code of textureCodes(f.element)) (codes[r] ??= new Set()).add(code);
    }
    expect([...codes.edge!]).toEqual(["edge"]);
    expect([...codes.screws!]).toEqual(["cupronickel"]);
    expect([...codes.platelead!]).toEqual(["lead"]);
    expect([...codes.platecopper!]).toEqual(["copper"]);
    const fold = (rig as unknown as { fold: { leverTurnsPerPlate: { thin: number; thick: number }; anglesPerPlate: number; plates: Record<string, string>; angles: Record<string, string> } }).fold;
    expect(fold.leverTurnsPerPlate).toEqual({ thin: 1.5, thick: 2.25 });
    expect(fold.plates).toEqual({ thin: "seraphhorizons:halfplate-lead", thick: "seraphhorizons:halfplate-copper" });
    expect(fold.anglesPerPlate).toBe(1);
    expect(fold.angles).toEqual({ thin: "seraphhorizons:angle-lead", thick: "seraphhorizons:angle-copper" });
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
