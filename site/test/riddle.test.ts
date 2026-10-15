import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { collapsed, compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The riddle's shipped model, one block: the hand riddle on its bearers over the fines box, and the poses
// Riddle/tools/make_shape.py computes from it with machinegen's reference maths. Its rig reads W (the riddling of one
// charge), k (a charge or none), p, and ψ, the riddling clock's travel: the shake is a gauge's lobes, eased in and out
// with its window, so θ moves nothing while no charge is riddled. The layer of fines on the mesh shrinks and the fines
// in the box grow on stretching gauges; the box's fines are a contents state of their own (`fines`), whole with no
// charge on. tools/tests/test_riddle_model.py replays the same files in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type RefPose = { theta: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, work: number, size: number, presence: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work, size, presence, feed: 0, oil: 0 });

const forms = [
  { name: "riddle", ref: "rig-reference.json", requires: [null, "riddle", "charge", "fines"] },
].map((f) => {
  const rig = read(`assets/seraphhorizons/config/${f.name}-rig.json`) as Rig;
  const parts = rig.parts!;
  const path = workOf(rig)!;
  const order = rideOrder(parts);
  return {
    ...f,
    rig,
    parts,
    path,
    shape: read(`assets/seraphhorizons/shapes/block/${f.name}.json`) as Shape,
    reference: read(`tests/Riddle/${f.ref}`) as { poses: RefPose[] },
    at: (theta: number, work: number, size: number, presence = 1) => partMatrices(parts, pose(theta, work, size, presence), order, path),
    index: (id: string) => parts.findIndex((p) => p.id === id),
  };
});

describe.each(forms)("the $name rig against its reference poses", (f) => {
  it("counts its work in charges, one riddling a charge, and has a matrix for every part", () => {
    expect(f.path).toMatchObject({ kind: "work", name: "riddling", unit: "charges" });
    expect(f.rig.trunkPath).toBeUndefined();
    expect(workEnd(f.path, 1)).toBe(1);
    expect(new Set(f.reference.poses.map((p) => p.size))).toEqual(new Set([0, 1]));
    for (const p of f.reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(f.parts.map((q) => q.id).sort());
    // the viewer shows the clock (θ, for its travel ψ, so Play), W, the charge and its presence; nothing else
    expect(rigInputs(f.parts)).toMatchObject({ theta: false, travel: true, depth: false, lifting: false, work: true, size: true, presence: true });
    expect((f.rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of f.reference.poses) {
      const ms = f.at(p.theta, p.work, p.size, p.presence);
      f.parts.forEach((q, i) => {
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

  it("moves the riddle with the clock only while a charge is riddled", () => {
    for (const w of [0, 0.9, 1]) {
      const a = f.at(0, w, 1);
      const b = f.at(2.2, w, 1);
      a.forEach((mm, i) => mm.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    const none = [f.at(0, 0.5, 0, 0), f.at(1.6, 0.5, 0, 0)];
    none[0]!.forEach((mm, i) => mm.forEach((v, j) => expect(Math.abs(v - none[1]![i]![j]!)).toBeLessThan(1e-12)));
    // a quarter turn into the riddling the riddle's middle is a voxel or more along z from where it was
    const z = (theta: number) => {
      const r = rows(f.at(theta, 0.5, 1)[f.index("riddle")]!);
      return (r[2]![0]! * 0.5 + r[2]![1]! * (6.4 / 16) + r[2]![2]! * 0.5 + r[2]![3]!) * 16;
    };
    expect(Math.abs(z(Math.PI / 2) - z(0))).toBeGreaterThan(1);
  });

  it("shrinks the heap to nothing and grows the fines in the box from nothing, which stay with no charge on", () => {
    const start = f.at(0, 0, 1);
    const done = f.at(0, 1, 1);
    expect(collapsed(start[f.index("heap")]!)).toBe(false);
    expect(collapsed(done[f.index("heap")]!)).toBe(true);
    expect(collapsed(start[f.index("fines")]!)).toBe(true);
    expect(collapsed(done[f.index("fines")]!)).toBe(false);
    // no charge on: the box's fines are whole, as built (the identity)
    for (const ms of [f.at(0, 0, 0, 0), f.at(1.7, 1, 0, 1)]) {
      const m = ms[f.index("fines")]!;
      m.forEach((v, j) => expect(Math.abs(v - (j % 5 === 0 ? 1 : 0))).toBeLessThan(1e-12));
    }
  });

  it("fits its parts, the charge and its fines by class, in the ore texture code", () => {
    expect(new Set(f.parts.map((p) => p.requires ?? null))).toEqual(new Set(f.requires));
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(f.shape.elements);
    const globs = compileGlobs(f.parts);
    for (const e of flat) {
      const r = f.parts[partOf(globs, e.chain)]!.requires ?? "frame";
      for (const code of textureCodes(e.element)) (codes[r] ??= new Set()).add(code);
    }
    expect([...codes.charge!]).toEqual(["ore"]);
    expect([...codes.fines!]).toEqual(["ore"]);
    expect(codes.riddle).toEqual(new Set(["oak", "iron"]));
    // the box's planks, the bearers' oak and the corner straps' iron
    expect(codes.frame).toEqual(new Set(["oak", "planks", "iron"]));
    // #714 (2026-10-14): a charge is three lumps, riddled in twelve shakes
    const riddling = (f.rig as unknown as { riddling: { shakesPerCharge: { thin: number }; lumpsPerCharge: number } }).riddling;
    expect(riddling.shakesPerCharge).toEqual({ thin: 12 });
    expect(riddling.lumpsPerCharge).toBe(3);
  });

  it("gives every element a part and every part an element, in one block", () => {
    const flat = flattenShape(f.shape.elements);
    const globs = compileGlobs(f.parts);
    const used = new Set(flat.map((e) => partOf(globs, e.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(f.parts.length);
    expect(f.rig.cells!.map((c) => c.pos)).toEqual([[0, 0, 0]]);
  });
});
