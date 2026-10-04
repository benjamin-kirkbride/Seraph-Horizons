import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  apply,
  compileGlobs,
  corners,
  driverMatrix,
  flattenShape,
  globRegExp,
  multiply,
  partMatrices,
  partOf,
  requiresValues,
  rideOrder,
  rigInputs,
  stepFraction,
  textureCodes,
  toVoxels,
  wrappedDelta,
  type Driver,
  type Mat4,
  type Rig,
  type RigPart,
  type Shape,
  type Vec3,
} from "../src/lib/rig.ts";

// The bucking sawmill's shipped files, and the poses BuckingSawmill/tools/make_shape.py computes from them with
// its reference maths. The mod's C# tests (RigAnimationTests) check against the same file, so
// the Python, the C# and the site are held to the same numbers.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/buckingmill-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/buckingmill.json") as Shape;
const reference = read("tests/BuckingSawmill/rig-reference.json") as {
  poses: { theta: number; depth: number; lifting: number; travel: number; matrices: Record<string, number[][]> }[];
};
const parts = rig.parts!;

/** A column-major matrix as 3 rows of 4, the reference file's layout. */
const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));

describe("rig maths against rig-reference.json", () => {
  it("has poses and a matrix for every shipped part", () => {
    expect(reference.poses.length).toBeGreaterThan(20);
    for (const pose of reference.poses) expect(Object.keys(pose.matrices).sort()).toEqual(parts.map((p) => p.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const pose of reference.poses) {
      const ms = partMatrices(parts, pose);
      parts.forEach((p, i) => {
        const got = rows(ms[i]!);
        const want = pose.matrices[p.id]!;
        for (let r = 0; r < 3; r++)
          for (let c = 0; c < 4; c++) {
            const diff = Math.abs(got[r]![c]! - want[r]![c]!);
            worst = Math.max(worst, diff);
            if (diff > 2e-6)
              throw new Error(`${p.id} at θ ${pose.theta}, depth ${pose.depth}, lifting ${pose.lifting}, travel ${pose.travel}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    // The file rounds to 6 decimals.
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });
});

// make_shape.py's cell_boxes: the elements' bounding boxes clipped to a cell, split greedily into
// up to three boxes. rig.json's cells were written by it from the model posed at rest, so
// flattening and posing the shipped shape here must give them back.
type Box = [Vec3, Vec3];
function cellBoxes(boxes: Box[], cell: Vec3): number[][] | null {
  const lo = cell.map((v) => v * 16) as Vec3;
  const clipped: Box[] = [];
  for (const [a, b] of boxes) {
    const ca = [0, 1, 2].map((k) => Math.max(a[k]!, lo[k]!)) as Vec3;
    const cb = [0, 1, 2].map((k) => Math.min(b[k]!, lo[k]! + 16)) as Vec3;
    if ([0, 1, 2].every((k) => cb[k]! - ca[k]! > 0.01)) clipped.push([ca, cb]);
  }
  if (clipped.length === 0) return null;
  const bound = (bs: Box[]): Box => [
    [0, 1, 2].map((k) => Math.min(...bs.map((x) => x[0][k]!))) as Vec3,
    [0, 1, 2].map((k) => Math.max(...bs.map((x) => x[1][k]!))) as Vec3,
  ];
  const vol = (b: Box) => (b[1][0] - b[0][0]) * (b[1][1] - b[0][1]) * (b[1][2] - b[0][2]);
  const groups: Box[][] = [clipped];
  while (groups.length < 3) {
    let best: { gain: number; gi: number; a: Box[]; b: Box[] } | null = null;
    groups.forEach((g, gi) => {
      if (g.length < 2) return;
      const whole = vol(bound(g));
      for (let axis = 0; axis < 3; axis++) {
        // Python's sort is stable, as is Array.prototype.sort.
        const srt = [...g].sort((x, y) => x[0][axis]! + x[1][axis]! - (y[0][axis]! + y[1][axis]!));
        for (let i = 1; i < srt.length; i++) {
          const a = srt.slice(0, i);
          const b = srt.slice(i);
          const gain = whole - vol(bound(a)) - vol(bound(b));
          if (best === null || gain > best.gain) best = { gain, gi, a, b };
        }
      }
    });
    if (best === null || (best as { gain: number }).gain < 0.08 * 16 ** 3) break;
    const { gi, a, b } = best as { gi: number; a: Box[]; b: Box[] };
    groups.splice(gi, 1, a, b);
  }
  const r4 = (x: number) => Math.round(x * 1e4) / 1e4;
  return groups
    .map((g) => {
      const [a, b] = bound(g);
      return [...[0, 1, 2].map((k) => r4((a[k]! - lo[k]!) / 16)), ...[0, 1, 2].map((k) => r4((b[k]! - lo[k]!) / 16))];
    })
    .sort((x, y) => {
      for (let k = 0; k < 6; k++) if (x[k] !== y[k]) return x[k]! - y[k]!;
      return 0;
    });
}

describe("shape posing", () => {
  const flat = flattenShape(shape.elements);
  const globs = compileGlobs(parts);
  const rest = partMatrices(parts, { theta: 0, depth: 0, lifting: 0, travel: 0 });
  const aabbs: Box[] = flat.map((f) => {
    const cs = corners(f, multiply(toVoxels(rest[partOf(globs, f.chain)]!), f.world));
    return [[0, 1, 2].map((k) => Math.min(...cs.map((c) => c[k]!))) as Vec3, [0, 1, 2].map((k) => Math.max(...cs.map((c) => c[k]!))) as Vec3];
  });

  it("gives every element of the shipped model a part", () => {
    expect(flat.length).toBe(shape.elements.length);
    expect(flat.every((f) => partOf(globs, f.chain) >= 0)).toBe(true);
  });

  it("reproduces rig.json's collision boxes from the model at rest", () => {
    const cells = rig.cells!;
    expect(cells.length).toBeGreaterThan(10);
    let worst = 0;
    for (const cell of cells) {
      const got = cellBoxes(aabbs, cell.pos) ?? [];
      const want = cell.boxes ?? [];
      expect(got.length, `cell ${cell.pos}`).toBe(want.length);
      got.forEach((b, i) => b.forEach((v, k) => (worst = Math.max(worst, Math.abs(v - want[i]![k]!)))));
    }
    // The shape's own numbers are rounded to 4 decimals, so the 4th decimal of a box may differ; and
    // the boxes come from the model before make_shape.py moved z-fighting faces in (COPLANAR_INSET,
    // 0.015 voxels a step, at most 4 steps deep), so a box edge may be up to 0.06 voxels out.
    expect(worst).toBeLessThan(4e-3);
    // And no cell left out of the rig holds anything.
    const listed = new Set(cells.map((c) => c.pos.join(",")));
    const lo = [0, 1, 2].map((k) => Math.min(...cells.map((c) => c.pos[k]!)) - 1);
    const hi = [0, 1, 2].map((k) => Math.max(...cells.map((c) => c.pos[k]!)) + 1);
    for (let x = lo[0]!; x <= hi[0]!; x++)
      for (let y = lo[1]!; y <= hi[1]!; y++)
        for (let z = lo[2]!; z <= hi[2]!; z++) if (!listed.has(`${x},${y},${z}`)) expect(cellBoxes(aabbs, [x, y, z]), `cell ${x},${y},${z}`).toBeNull();
  });

  it("puts a child relative to its parent's from and rotates about the origin, x then y then z", () => {
    const flatTree = flattenShape([
      {
        name: "parent",
        from: [2, 0, 0],
        to: [4, 2, 2],
        rotationOrigin: [2, 0, 0],
        rotationY: 90,
        children: [{ name: "child", from: [0, 2, 0], to: [1, 3, 1] }],
      },
    ]);
    expect(flatTree.map((f) => f.chain)).toEqual([["parent"], ["parent", "child"]]);
    const near = (a: Vec3, b: Vec3) => a.forEach((v, k) => expect(v).toBeCloseTo(b[k]!, 9));
    // A right-handed quarter turn about +y takes +x to −z.
    near(apply(flatTree[0]!.world, [4, 0, 0]), [2, 0, -2]);
    // The child's origin is the parent's `from`, turned with it.
    near(apply(flatTree[1]!.world, [0, 2, 0]), [2, 2, 0]);
    near(apply(flatTree[1]!.world, [1, 2, 0]), [2, 2, -1]);
  });

  it("lists the textures an element's drawn faces use", () => {
    expect(textureCodes({ name: "a", from: [0, 0, 0], to: [1, 1, 1], faces: { up: { texture: "#oak" }, down: { texture: "#metal" }, north: { texture: "#oak", enabled: false } } })).toEqual(["metal", "oak"]);
    expect(textureCodes({ name: "b", from: [0, 0, 0], to: [1, 1, 1], faces: { east: { texture: "#rope" }, west: { texture: "#oak", enabled: false } } })).toEqual(["rope"]);
  });
});

describe("rig parts", () => {
  it("matches globs case-sensitively over the whole name", () => {
    expect(globRegExp("f1_saw_*").test("f1_saw_head")).toBe(true);
    expect(globRegExp("f1_saw_*").test("F1_saw_head")).toBe(false);
    expect(globRegExp("drum*").test("xdrum")).toBe(false);
    expect(globRegExp("a.b").test("axb")).toBe(false);
  });

  it("gives an element the first part matching any name in its chain", () => {
    const ps: RigPart[] = [
      { id: "a", match: ["arm_*"] },
      { id: "b", match: ["base*"] },
      { id: "rest", match: ["*"] },
    ];
    const globs = compileGlobs(ps);
    expect(partOf(globs, ["base", "arm_1"])).toBe(0);
    expect(partOf(globs, ["base", "bolt"])).toBe(1);
    expect(partOf(globs, ["lid"])).toBe(2);
    expect(partOf(compileGlobs(ps.slice(0, 2)), ["lid"])).toBe(-1);
  });

  it("evaluates a ride's parent first whatever the order, and rejects cycles and unknown parts", () => {
    const ps: RigPart[] = [
      { id: "rider", match: ["r"], ride: "carrier", drivers: [{ type: "slide", axis: "x", amplitude: 1, phase: Math.PI / 2 }] },
      { id: "carrier", match: ["c"], drivers: [{ type: "feed", axis: "y", travel: -2 }] },
    ];
    expect(rideOrder(ps)).toEqual([1, 0]);
    const [rider] = partMatrices(ps, { theta: 0, depth: 0.5, lifting: 0 });
    expect(apply(rider!, [0, 0, 0])).toEqual([1, -1, 0]);
    expect(() => rideOrder([{ id: "a", match: ["*"], ride: "b" }, { id: "b", match: ["*"], ride: "a" }])).toThrow(/cycle/);
    expect(() => rideOrder([{ id: "a", match: ["*"], ride: "nope" }])).toThrow(/not a part/);
    expect(() => rideOrder([{ id: "a", match: ["*"] }, { id: "a", match: ["*"] }])).toThrow(/twice/);
  });

  it("reads which inputs the drivers use and the requires values in order", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: true, depth: true, lifting: true });
    expect(rigInputs([{ id: "a", match: ["*"], drivers: [{ type: "rotate", axis: "y", pivot: [0, 0, 0] }] }])).toEqual({
      theta: true,
      travel: false,
      depth: false,
      lifting: false,
    });
    expect(rigInputs([])).toEqual({ theta: false, travel: false, depth: false, lifting: false });
    expect(requiresValues(parts)).toEqual(["crankshaft", "sash1", "blade", "sash2", "levers"]);
  });
});

describe("drivers", () => {
  const step = (lifting: Driver["lifting"]): Driver => ({ type: "step", motion: "slide", axis: "y", amount: 1, from: 0.5, to: 1, lifting, top: 0.1 });

  it("gates a step by lifting", () => {
    expect(stepFraction(step(undefined), 0.75, 1)).toBeCloseTo(0.5);
    expect(stepFraction(step("hold"), 0.6, 1)).toBe(1);
    expect(stepFraction(step("block"), 0.9, 1)).toBe(0);
    // trip: thrown going down, held going up until the top, and continuous where the direction changes
    expect(stepFraction(step("trip"), 1, 0)).toBe(1);
    expect(stepFraction(step("trip"), 1, 1)).toBe(1);
    expect(stepFraction(step("trip"), 0.05, 1)).toBeCloseTo(0.5);
    expect(stepFraction(step("trip"), 0, 1)).toBe(0);
    expect(stepFraction(step("trip"), 0, 0)).toBe(0);
  });

  it("turns rectified rotations by the travel, the same way either way the shaft turns", () => {
    const d: Driver = { type: "rotate", axis: "z", pivot: [0, 0, 0], ratio: 1, rectified: true };
    const a = driverMatrix(d, { theta: 1, depth: 0, lifting: 0 });
    const b = driverMatrix(d, { theta: -1, depth: 0, lifting: 0 });
    expect(a).toEqual(b);
    expect(apply(driverMatrix(d, { theta: -1, depth: 0, lifting: 0, travel: Math.PI / 2 }), [1, 0, 0]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([0, 1, 0]);
  });

  it("stretches along an axis about the anchor's plane", () => {
    const d: Driver = { type: "stretch", axis: "y", anchor: [0, 3, 0], length: -1, travel: -1 };
    const m = driverMatrix(d, { theta: 0, depth: 1, lifting: 0 });
    // the free end at y 2 goes to y 1, the anchor stays, x and z are untouched
    expect(apply(m, [5, 2, 7])).toEqual([5, 1, 7]);
    expect(apply(m, [5, 3, 7])).toEqual([5, 3, 7]);
    expect(() => driverMatrix({ ...d, length: 0 }, { theta: 0, depth: 1, lifting: 0 })).toThrow(/non-zero/);
  });

  it("takes the short way round between two angles", () => {
    expect(wrappedDelta(0.1, 2 * Math.PI - 0.1)).toBeCloseTo(-0.2);
    expect(wrappedDelta(3, -3)).toBeCloseTo(2 * Math.PI - 6);
  });
});
