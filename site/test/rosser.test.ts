import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  compileGlobs,
  corners,
  flattenShape,
  multiply,
  partMatrices,
  partOf,
  rideOrder,
  toVoxels,
  tripEnd,
  trunkPathOf,
  type Mat4,
  type Pose,
  type Rig,
  type Shape,
  type Vec3,
} from "../src/lib/rig.ts";

// The rosser's shipped files, and the poses Rosser/tools/make_shape.py computes from them with
// machinegen's reference maths (trunk travel, class, presence and feed included).
// tools/tests/test_rosser_model.py replays the same file in Python, and RosserRigTests in C#.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/rosser-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/rosser.json") as Shape;
type RefPose = { theta: number; travel: number; feed: number; trunk: number; size: number; presence: number; matrices: Record<string, number[][]> };
const reference = read("tests/Rosser/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = trunkPathOf(rig)!;
const order = rideOrder(parts);

/** A column-major matrix as 3 rows of 4, the reference file's layout. */
const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, trunk: p.trunk, size: p.size, presence: p.presence, feed: p.feed });

describe("the rosser's rig against its rig-reference.json", () => {
  it("has a trunk path, poses of both classes and none, and a matrix for every shipped part", () => {
    expect(path).not.toBeNull();
    expect(tripEnd(path, 1)).toBeGreaterThan(8);
    expect(tripEnd(path, 2)).toBeGreaterThan(tripEnd(path, 1));
    expect(reference.poses.length).toBeGreaterThan(100);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
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
            if (diff > 2e-6)
              throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}, φ ${p.feed}, T ${p.trunk}, k ${p.size}, p ${p.presence}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    // The file rounds to 6 decimals.
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });
});

// make_shape.py's cell_boxes (machinegen.checks): the elements' bounding boxes clipped to a cell,
// split greedily into up to three boxes. The rosser's cells were written by it from the model at
// rest (no trunk); a cell with nothing of its own is "hollow", with no boxes. (The same as the
// mill's test in rig.test.ts.)
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

describe("the rosser's shape and cells", () => {
  const flat = flattenShape(shape.elements);
  const globs = compileGlobs(parts);
  const rest = partMatrices(parts, { theta: 0, depth: 0, lifting: 0, travel: 0, trunk: 0, size: 0, presence: 0, feed: 0 }, order, path);
  const aabbs: Box[] = flat.map((f) => {
    const cs = corners(f, multiply(toVoxels(rest[partOf(globs, f.chain)]!), f.world));
    return [[0, 1, 2].map((k) => Math.min(...cs.map((c) => c[k]!))) as Vec3, [0, 1, 2].map((k) => Math.max(...cs.map((c) => c[k]!))) as Vec3];
  });

  it("gives every element a part, and the controller is a cell", () => {
    expect(flat.every((f) => partOf(globs, f.chain) >= 0)).toBe(true);
    expect(rig.cells!.some((c) => c.pos.join(",") === "0,0,0")).toBe(true);
  });

  it("reproduces rig.json's collision boxes from the model at rest, and leaves the empty cells hollow", () => {
    const cells = rig.cells!;
    expect(cells.length).toBeGreaterThan(200);
    let worst = 0;
    for (const cell of cells) {
      const got = cellBoxes(aabbs, cell.pos) ?? [];
      const want = cell.boxes ?? [];
      expect(got.length, `cell ${cell.pos}`).toBe(want.length);
      expect(Boolean(cell.hollow), `cell ${cell.pos} hollow`).toBe(want.length === 0);
      got.forEach((b, i) => b.forEach((v, k) => (worst = Math.max(worst, Math.abs(v - want[i]![k]!)))));
    }
    // Rounded to 4 decimals, and the boxes come from the model before the z-fighting insets.
    expect(worst).toBeLessThan(4e-3);
    const listed = new Set(cells.map((c) => c.pos.join(",")));
    const lo = [0, 1, 2].map((k) => Math.min(...cells.map((c) => c.pos[k]!)) - 1);
    const hi = [0, 1, 2].map((k) => Math.max(...cells.map((c) => c.pos[k]!)) + 1);
    for (let x = lo[0]!; x <= hi[0]!; x++)
      for (let y = lo[1]!; y <= hi[1]!; y++)
        for (let z = lo[2]!; z <= hi[2]!; z++) if (!listed.has(`${x},${y},${z}`)) expect(cellBoxes(aabbs, [x, y, z]), `cell ${x},${y},${z}`).toBeNull();
  });
});
