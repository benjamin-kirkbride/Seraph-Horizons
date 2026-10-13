import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The riddle's shipped model (one block: the riddle on its bearers over the fines box), and the poses
// Riddle/tools/make_shape.py computes from it with machinegen's reference maths. Its rig reads W (the
// riddling of one charge), k (a part or a full charge), p, and ψ, the riddling clock's travel: the shake is a
// gauge's lobes, eased in and out with its window, so θ moves nothing while no charge is riddled.
// tools/tests/test_riddle_model.py replays the same files in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type RefPose = { theta: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };

const rig = read("assets/seraphhorizons/config/riddle-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/riddle.json") as Shape;
const ref = read("tests/Riddle/rig-reference.json") as { poses: RefPose[] };

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, work: number, size: number, presence: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work, size, presence, feed: 0, oil: 0 });

const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);
const at = (theta: number, work: number, size: number, presence = 1) => partMatrices(parts, pose(theta, work, size, presence), order, path);
const riddle = parts.findIndex((p) => p.id === "riddle");

describe("the riddle's rig against its reference poses", () => {
  it("counts its work in charges, one riddling a charge of either size, and has a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "riddling", unit: "charges" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(1);
    expect(workEnd(path, 2)).toBe(1);
    expect(new Set(ref.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of ref.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows the clock (θ, for its travel ψ, so Play), W, the charge and its presence; nothing else
    expect(rigInputs(parts)).toMatchObject({ theta: false, travel: true, depth: false, lifting: false, work: true, size: true, presence: true });
    expect((rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of ref.poses) {
      const ms = at(p.theta, p.work, p.size, p.presence);
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

  it("shakes the riddle with the clock only while a charge is riddled", () => {
    for (const w of [0, 0.9, 1]) {
      const a = at(0, w, 2);
      const b = at(2.2, w, 2);
      a.forEach((mm, i) => mm.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    const none = [at(0, 0.5, 0, 0), at(1.6, 0.5, 0, 0)];
    none[0]!.forEach((mm, i) => mm.forEach((v, j) => expect(Math.abs(v - none[1]![i]![j]!)).toBeLessThan(1e-12)));
    // a quarter turn into the riddling the riddle's middle is a voxel or more along z from where it was
    const z = (theta: number) => {
      const r = rows(at(theta, 0.5, 2)[riddle]!);
      return (r[2]![0]! * 0.5 + r[2]![1]! * (6.4 / 16) + r[2]![2]! * 0.5 + r[2]![3]!) * 16;
    };
    expect(Math.abs(z(Math.PI / 2) - z(0))).toBeGreaterThan(1);
  });

  it("fits its parts, the charge and its fines by class, in the ore texture code", () => {
    expect(new Set(parts.map((p) => p.requires ?? null))).toEqual(new Set([null, "riddle", "chargesmall", "chargefull"]));
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const r = parts[partOf(globs, f.chain)]!.requires ?? "frame";
      for (const code of textureCodes(f.element)) (codes[r] ??= new Set()).add(code);
    }
    expect([...codes.chargesmall!]).toEqual(["ore"]);
    expect([...codes.chargefull!]).toEqual(["ore"]);
    expect(codes.riddle).toEqual(new Set(["oak", "iron"]));
    expect(codes.frame).toEqual(new Set(["oak", "planks"]));
    const pace = (rig as unknown as { riddling: { shakesPerCharge: { thin: number; thick: number } } }).riddling.shakesPerCharge;
    expect(pace.thick).toBeGreaterThan(pace.thin);
  });

  it("gives every element a part and every part an element, in one block", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    expect(rig.cells!.map((c) => c.pos)).toEqual([[0, 0, 0]]);
  });
});
