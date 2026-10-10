import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The crucible furnace's shipped files, and the poses CrucibleFurnace/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads depth (the covers, only one off at a time: 0 hole 1's off, lying on
// the top, 0.5 both seated, 1 hole 2's off; they are the only parts that move) and θ (the melter's clock, carried by a
// ratio-0 rotate so the viewer offers Play); everything else is a requires per hole. tools/tests/test_cruciblefurnace_model.py replays the same file in
// Python.
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

describe("the crucible furnace's rig against its rig-reference.json", () => {
  it("reads θ and depth only, has no power cell, and a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: true, lifting: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    expect(r.powerCell).toBeUndefined();
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

  it("moves nothing with θ, and only the covers with depth, one at a time: a block between the top and the seat", () => {
    const at = (theta: number, depth: number) => partMatrices(parts, pose(theta, depth), order);
    for (const depth of [0.3, 0.8]) {
      const a = at(0, depth);
      const b = at(4.2, depth);
      a.forEach((m, i) => m.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    // hole 1's cover is authored off, lying on the top behind its hole, hole 2's seated: at 0 hole 1 is open, at 0.5
    // both are shut (hole 1's a block forward and its thickness, 2.5 voxels, down into the seat), at 1 hole 2 is open
    // (its cover a block back and its thickness up, on the top); nothing else ever moves
    const shift = (depth: number, id: string) => {
      const m = at(0, depth)[parts.findIndex((q) => q.id === id)]!;
      return [m[12]!, m[13]!, m[14]!].map((v) => Math.round(v * 1e9) / 1e9 + 0);
    };
    const t = 2.5 / 16;
    const places: [number, number[], number[]][] = [
      [0, [0, 0, 0], [0, 0, 0]],
      [0.5, [0, -t, -1], [0, 0, 0]],
      [1, [0, -t, -1], [0, t, 1]],
    ];
    for (const [depth, one, two] of places) {
      expect(shift(depth, "h1cover")).toEqual(one);
      expect(shift(depth, "h2cover")).toEqual(two);
      for (const q of parts) if (!q.id.endsWith("cover")) expect(shift(depth, q.id)).toEqual([0, 0, 0]);
    }
    // carried half a voxel over the floor, each in its own half, the other seated meanwhile
    expect(shift(0.25, "h1cover")[1]).toBe(0.5 / 16);
    expect(shift(0.25, "h2cover")).toEqual([0, 0, 0]);
    expect(shift(0.75, "h2cover")[1]).toBe(3 / 16);
    expect(shift(0.75, "h1cover")).toEqual([0, -t, -1]);
  });

  it("shows each hole's contents by requires, with their own texture codes, and glows only where it burns", () => {
    const req = requiresValues(parts);
    const want = [1, 2].flatMap((i) => ["pot", "charge", "melt", "coke1", "coke2", "coke3", "fire1", "fire2", "fire3"].map((w) => `h${i}${w}`));
    expect(req).toEqual(want);
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const p = parts[partOf(globs, f.chain)]!;
      const key = p.requires ? p.requires.replace(/^h\d/, "").replace(/\d$/, "") : p.id.replace(/^h\d/, "");
      for (const code of textureCodes(f.element)) (codes[key] ??= new Set()).add(code);
    }
    expect([...codes.pot!]).toEqual(["clay"]);
    expect([...codes.coke!]).toEqual(["coke"]);
    expect([...codes.fire!]).toEqual(["ember"]);
    expect([...codes.melt!]).toEqual(["melt"]);
    expect([...codes.charge!]).toEqual(["charge"]);
    expect(new Set(codes.cover)).toEqual(new Set(["iron", "brick"]));
    expect(new Set(codes.frame)).toEqual(new Set(["brick", "iron", "clay"]));
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      const id = parts[partOf(globs, f.chain)]!.id;
      if (/^h\d(fire|melt)/.test(id)) expect(Math.min(...glows)).toBeGreaterThan(0);
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
