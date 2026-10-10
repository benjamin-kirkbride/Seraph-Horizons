import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The crucible furnace's shipped files, and the poses CrucibleFurnace/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads depth (the hinged lid: 0 open, resting just past upright, 1 shut; the
// lid is the only part that moves) and θ (the melter's clock, carried by a ratio-0 rotate so the viewer offers Play);
// everything else is a requires. tools/tests/test_cruciblefurnace_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/cruciblefurnace-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/cruciblefurnace.json") as Shape;
type RefPose = { theta: number; depth: number; matrices: Record<string, number[][]> };
const reference = read("tests/CrucibleFurnace/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, depth: number): Pose => ({ theta, depth, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the crucible furnace's rig against its rig-reference.json", () => {
  it("reads θ and depth only, has no power cell and no air port, and a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: true, lifting: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    expect(r.powerCell).toBeUndefined();
    expect(r.airCell).toBeUndefined();
    expect(r.work).toBeUndefined();
    expect(reference.poses.length).toBeGreaterThan(10);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta, p.depth), order);
      parts.forEach((q, i) => {
        const got = rows(ms[i]!);
        const want = p.matrices[q.id]!;
        for (let r = 0; r < 3; r++)
          for (let c = 0; c < 4; c++) {
            const diff = Math.abs(got[r]![c]! - want[r]![c]!);
            worst = Math.max(worst, diff);
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, depth ${p.depth}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("moves nothing with θ, and only the lid with depth: turned down about its top back edge from open to shut", () => {
    const at = (theta: number, depth: number) => partMatrices(parts, pose(theta, depth), order);
    for (const depth of [0.3, 0.8]) {
      const a = at(0, depth);
      const b = at(4.2, depth);
      a.forEach((m, i) => m.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    const lid = parts.findIndex((q) => q.id === "lid");
    const open = (rig as unknown as { furnace: { lidOpenDegrees: number } }).furnace.lidOpenDegrees;
    for (const depth of [0, 0.5, 1]) {
      const ms = at(0, depth);
      ms.forEach((m, i) => {
        if (i !== lid) m.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12));
      });
      // a turn about x by −open × depth (its amount written to six places): the matrix reads its cos and sin
      const turn = (-open * depth * Math.PI) / 180;
      expect(ms[lid]![5]).toBeCloseTo(Math.cos(turn), 5);
      expect(ms[lid]![6]).toBeCloseTo(Math.sin(turn), 5);
      expect(ms[lid]![0]).toBeCloseTo(1, 12);
    }
  });

  it("shows the pots, the coke, the damper and the cap by requires, with their own texture codes, and glows only where it burns", () => {
    const req = requiresValues(parts);
    expect(req).toEqual(["pot1", "pot2", "charge1", "charge2", "melt1", "melt2", "coke1", "coke2", "coke3", "fire1", "fire2", "fire3", "damperopen", "damperhalf", "dampershut", "outletcap"]);
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const key = parts[partOf(globs, f.chain)]!.id.replace(/\d$/, "").replace(/^damper.*/, "damper");
      for (const code of textureCodes(f.element)) (codes[key] ??= new Set()).add(code);
    }
    expect([...codes.pot!]).toEqual(["clay"]);
    expect([...codes.coke!]).toEqual(["coke"]);
    expect([...codes.fire!]).toEqual(["ember"]);
    expect([...codes.melt!]).toEqual(["melt"]);
    expect([...codes.charge!]).toEqual(["charge"]);
    expect([...codes.damper!]).toEqual(["iron"]);
    expect([...codes.outletcap!]).toEqual(["iron"]);
    expect(new Set(codes.lid)).toEqual(new Set(["iron", "brick"]));
    expect(new Set(codes.frame)).toEqual(new Set(["brick", "iron", "clay"]));
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      const id = parts[partOf(globs, f.chain)]!.id;
      if (/^(fire|melt)\d/.test(id)) expect(Math.min(...glows)).toBeGreaterThan(0);
      else expect(Math.max(...glows)).toBe(0);
    }
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
