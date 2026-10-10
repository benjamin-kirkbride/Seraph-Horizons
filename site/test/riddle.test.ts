import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The riddle's two shipped models (the hand riddle and the riddle on its stand), and the poses
// Riddle/tools/make_shape.py computes from them with machinegen's reference maths. Each rig reads W (the
// riddling of one charge), k (a part or a full charge), p, and ψ, the riddling clock's travel: the shake is a
// gauge's lobes, eased in and out with its window, so θ moves nothing while no charge is riddled.
// tools/tests/test_riddle_model.py replays the same files in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type RefPose = { theta: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };

const models = [
  { name: "hand riddle", rig: "riddle-rig.json", shape: "riddle.json", reference: "rig-reference.json", requires: [null, "riddle", "chargesmall", "chargefull"] },
  { name: "riddle on its stand", rig: "riddlestand-rig.json", shape: "riddlestand.json", reference: "stand-rig-reference.json", requires: [null, "tub", "riddle", "hangers", "lever", "chargesmall", "chargefull"] },
].map((m) => ({
  ...m,
  rigFile: read(`assets/seraphhorizons/config/${m.rig}`) as Rig,
  shapeFile: read(`assets/seraphhorizons/shapes/block/${m.shape}`) as Shape,
  ref: read(`tests/Riddle/${m.reference}`) as { poses: RefPose[] },
}));

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, work: number, size: number, presence: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work, size, presence, feed: 0, oil: 0 });

for (const m of models) {
  const rig = m.rigFile;
  const parts = rig.parts!;
  const path = workOf(rig)!;
  const order = rideOrder(parts);
  const at = (theta: number, work: number, size: number, presence = 1) => partMatrices(parts, pose(theta, work, size, presence), order, path);
  const riddle = parts.findIndex((p) => p.id === "riddle");

  describe(`the ${m.name}'s rig against its reference poses`, () => {
    it("counts its work in charges, one riddling a charge of either size, and has a matrix for every part", () => {
      expect(path).toMatchObject({ kind: "work", name: "riddling", unit: "charges" });
      expect(rig.trunkPath).toBeUndefined();
      expect(workEnd(path, 1)).toBe(1);
      expect(workEnd(path, 2)).toBe(1);
      expect(new Set(m.ref.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
      for (const p of m.ref.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
      // the viewer shows the clock (θ, for its travel ψ, so Play), W, the charge and its presence; nothing else
      expect(rigInputs(parts)).toMatchObject({ theta: false, travel: true, depth: false, lifting: false, work: true, size: true, presence: true });
      expect((rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
    });

    it("matches every part's matrix at every reference pose", () => {
      let worst = 0;
      for (const p of m.ref.poses) {
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
      // a quarter turn into the riddling the riddle is a block's sixteenth or more along z from where it was
      const z = (theta: number) => rows(at(theta, 0.5, 2)[riddle]!)[2]![3]! * 16;
      expect(Math.abs(z(Math.PI / 2) - z(0))).toBeGreaterThan(1);
    });

    it("fits its parts, the charge and its fines by class, in the ore texture code", () => {
      expect(new Set(parts.map((p) => p.requires ?? null))).toEqual(new Set(m.requires));
      const codes: Record<string, Set<string>> = {};
      const flat = flattenShape(m.shapeFile.elements);
      const globs = compileGlobs(parts);
      for (const f of flat) {
        const r = parts[partOf(globs, f.chain)]!.requires ?? "frame";
        for (const code of textureCodes(f.element)) (codes[r] ??= new Set()).add(code);
      }
      expect([...codes.chargesmall!]).toEqual(["ore"]);
      expect([...codes.chargefull!]).toEqual(["ore"]);
      expect(codes.riddle).toEqual(new Set(["oak", "iron"]));
      const pace = (rig as unknown as { riddling: { shakesPerCharge: { thin: number; thick: number } } }).riddling.shakesPerCharge;
      expect(pace.thick).toBeGreaterThan(pace.thin);
    });

    it("gives every element a part and every part an element", () => {
      const flat = flattenShape(m.shapeFile.elements);
      const globs = compileGlobs(parts);
      const used = new Set(flat.map((f) => partOf(globs, f.chain)));
      expect(used.has(-1)).toBe(false);
      expect(used.size).toBe(parts.length);
    });
  });
}

describe("the riddle on its stand", () => {
  const stand = models[1]!;
  const parts = stand.rigFile.parts!;
  const path = workOf(stand.rigFile)!;
  const order = rideOrder(parts);
  it("hangs level on its hangers as the lever swings it", () => {
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    expect(byId.riddle!.ride).toBe("hangers");
    expect(byId.link!.ride).toBe("hangers");
    const riddle = parts.findIndex((p) => p.id === "riddle");
    for (const theta of [0.4, 1.6, 3.1, 4.7])
      for (const work of [0.1, 0.5]) {
        const r = rows(partMatrices(parts, pose(theta, work, 2, 1), order, path)[riddle]!);
        for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) expect(Math.abs(r[i]![j]! - (i === j ? 1 : 0))).toBeLessThan(1e-9);
      }
  });
});
