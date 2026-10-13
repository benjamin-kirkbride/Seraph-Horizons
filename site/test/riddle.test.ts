import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { collapsed, compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The riddle's two shipped models, one block each: the hand riddle (on its bearers over the fines box) and the riddle
// on its stand (the same riddle hung by four straps from an oak stand over the same box), and the poses
// Riddle/tools/make_shape.py computes from them with machinegen's reference maths. Their rigs read W (the riddling of
// one charge), k (a part or a full charge), p, and ψ, the riddling clock's travel: the shake is a gauge's lobes, eased
// in and out with its window, so θ moves nothing while no charge is riddled. The heap of fines shrinks and the fines
// in the box grow on stretching gauges. tools/tests/test_riddle_model.py replays the same files in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type RefPose = { theta: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, work: number, size: number, presence: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work, size, presence, feed: 0, oil: 0 });

const forms = [
  { name: "riddle", ref: "rig-reference.json", requires: [null, "riddle", "chargesmall", "chargefull"] },
  { name: "riddlestand", ref: "stand-rig-reference.json", requires: [null, "hangers", "riddle", "chargesmall", "chargefull"] },
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
  it("counts its work in charges, one riddling a charge of either size, and has a matrix for every part", () => {
    expect(f.path).toMatchObject({ kind: "work", name: "riddling", unit: "charges" });
    expect(f.rig.trunkPath).toBeUndefined();
    expect(workEnd(f.path, 1)).toBe(1);
    expect(workEnd(f.path, 2)).toBe(1);
    expect(new Set(f.reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
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
      const a = f.at(0, w, 2);
      const b = f.at(2.2, w, 2);
      a.forEach((mm, i) => mm.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    const none = [f.at(0, 0.5, 0, 0), f.at(1.6, 0.5, 0, 0)];
    none[0]!.forEach((mm, i) => mm.forEach((v, j) => expect(Math.abs(v - none[1]![i]![j]!)).toBeLessThan(1e-12)));
    // a quarter turn into the riddling the riddle's middle is a voxel or more along z from where it was
    const z = (theta: number) => {
      const r = rows(f.at(theta, 0.5, 2)[f.index("riddle")]!);
      return (r[2]![0]! * 0.5 + r[2]![1]! * (6.4 / 16) + r[2]![2]! * 0.5 + r[2]![3]!) * 16;
    };
    expect(Math.abs(z(Math.PI / 2) - z(0))).toBeGreaterThan(1);
  });

  it("shrinks the heap to nothing and grows the fines in the box from nothing", () => {
    for (const [k, heaps, fines] of [[1, ["c1heap"], ["c1fines"]], [2, ["c2heap", "c2top"], ["c2fines1", "c2fines2"]]] as const) {
      const start = f.at(0, 0, k);
      const done = f.at(0, 1, k);
      for (const id of heaps) {
        expect(collapsed(start[f.index(id)]!)).toBe(false);
        expect(collapsed(done[f.index(id)]!)).toBe(true);
      }
      for (const id of fines) {
        expect(collapsed(start[f.index(id)]!)).toBe(true);
        expect(collapsed(done[f.index(id)]!)).toBe(false);
      }
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
    expect([...codes.chargesmall!]).toEqual(["ore"]);
    expect([...codes.chargefull!]).toEqual(["ore"]);
    expect(codes.riddle).toEqual(new Set(["oak", "iron"]));
    expect(codes.frame).toEqual(new Set(["oak", "planks"]));
    if (codes.hangers) expect(codes.hangers).toEqual(new Set(["iron"]));
    const pace = (f.rig as unknown as { riddling: { shakesPerCharge: { thin: number; thick: number } } }).riddling.shakesPerCharge;
    expect(pace.thick).toBeGreaterThan(pace.thin);
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

describe("the riddle on its stand", () => {
  const stand = forms[1]!;
  it("hangs the riddle level on its straps, which stay on their pins", () => {
    for (const theta of [0, 0.7, Math.PI / 2, 2.9, 4.4]) {
      const ms = stand.at(theta, 0.5, 2);
      const r = rows(ms[stand.index("riddle")]!);
      for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) expect(Math.abs(r[i]![j]! - (i === j ? 1 : 0))).toBeLessThan(1e-9);
      // each strap's pin into the riddle (the west strap's, at its middle) moves with the riddle
      for (const [id, z] of [["hangernorth", 3.75], ["hangersouth", 12.25]] as const) {
        const p = [1.875 / 16, 6.9 / 16, z / 16, 1];
        const onStrap = rows(ms[stand.index(id)]!).map((row) => row.reduce((s, v, c) => s + v * p[c]!, 0));
        const onRiddle = r.map((row) => row.reduce((s, v, c) => s + v * p[c]!, 0));
        onStrap.forEach((v, i) => expect(Math.abs(v - onRiddle[i]!)).toBeLessThan(1e-9));
      }
    }
  });
});
