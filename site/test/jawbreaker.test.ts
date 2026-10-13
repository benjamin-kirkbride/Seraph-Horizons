import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { cellBoxes, discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, thetaTurns, workOf, type Mat4, type Pose, type Rig, type RigCell, type Shape } from "../src/lib/rig.ts";

// The jaw breaker's shipped files, and the poses JawBreaker/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads one input, θ, the axle's angle: the eccentric shaft turns with it, once a turn, and
// the Blake linkage (pitman, toggles, swinging jaw, rods, springs) follows as harmonics of it, the same either way
// round. It has no work. tools/tests/test_jawbreaker_model.py and the mod's C# tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/jawbreaker-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/jawbreaker.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/JawBreaker/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const STAGES = ["jaws", "eccentric", "toggles", "grizzly"];

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);
const index = (id: string) => parts.findIndex((p) => p.id === id);

describe("the jaw breaker's rig against its rig-reference.json", () => {
  it("reads θ alone, has no work, and fits its four stages to the frame", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(thetaTurns(parts)).toBe(1);
    expect(requiresValues(parts).sort()).toEqual([...STAGES].sort());
    expect(rig.stages).toEqual(STAGES);
    expect(parts.at(-1)).toMatchObject({ id: "frame", requires: null });
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta), order, null);
      parts.forEach((q, i) => {
        const got = rows(ms[i]!);
        const want = p.matrices[q.id]!;
        for (let r = 0; r < 3; r++)
          for (let c = 0; c < 4; c++) {
            const diff = Math.abs(got[r]![c]! - want[r]![c]!);
            worst = Math.max(worst, diff);
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("turns the eccentric shaft with the axle on the power cell's centre line, and rocks the jaw once a turn", () => {
    for (const theta of [0.4, 2.2, -1.3, 7.9]) {
      const m = partMatrices(parts, pose(theta), order, null)[index("eshaft")]!;
      expect(apply(m, [1, 1.5, 1.5]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([1, 1.5, 1.5]);
      const p = apply(m, [1, 2.5, 1.5]);
      expect(p[1]! - 1.5).toBeCloseTo(Math.cos(theta), 9);
      expect(p[2]! - 1.5).toBeCloseTo(Math.sin(theta), 9);
    }
    // a point on the swinging jaw's face low down: back where it was after a turn, and the same either way round
    const jaw = index("swingjaw");
    const at = (theta: number) => apply(partMatrices(parts, pose(theta), order, null)[jaw]!, [0.6875, 1.1, 0.87]);
    for (const theta of [0.3, 1.7, 4.4]) {
      const a = at(theta), b = at(theta + 2 * Math.PI), c = at(-theta), d = at(2 * Math.PI - theta);
      for (let k = 0; k < 3; k++) {
        expect(b[k]!).toBeCloseTo(a[k]!, 9);
        expect(c[k]!).toBeCloseTo(d[k]!, 9);
      }
    }
    const zs = Array.from({ length: 72 }, (_, k) => at((2 * Math.PI * k) / 72)[2]!);
    expect((Math.max(...zs) - Math.min(...zs)) * 16).toBeGreaterThan(0.5);
  });

  it("gives every element a part and every part an element, the chutes the game's chute texture", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    expect(shape.textures!.chute).toBe("game:block/metal/sheet/copper1");
  });

  it("puts the power, the infeed and the output on their cells' faces, both chutes in one column", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "powerCell")).toMatchObject({ kind: "cell", pos: [1, 1, 1], face: "east" });
    expect(anchors.find((a) => a.key === "infeedCell")).toMatchObject({ kind: "cell", pos: [0, 2, 0], face: "up" });
    expect(anchors.find((a) => a.key === "outputCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "north" });
    expect(anchors.find((a) => a.key === "infeed")).toMatchObject({ kind: "point", pos: [0.5, 3, 0.5] });
    expect(anchors.find((a) => a.key === "output")).toMatchObject({ kind: "point", pos: [0.5, 0.5, 0] });
  });

  it("collides with its frame in all twelve cells, with no lids", () => {
    const cells = rig.cells as RigCell[];
    expect(cells.length).toBe(12);
    for (const c of cells) {
      expect(c.lid).toBeUndefined();
      expect(cellBoxes(c).length).toBeGreaterThan(0);
    }
  });
});

describe("the jaw breaker's page", () => {
  const model = manifest.models.find((m) => m.id === "jaw-breaker")!;

  it("labels its stages and turns at a steady pace", () => {
    expect(Object.keys(model.scenario!.requires!).sort()).toEqual([...STAGES].sort());
    expect(model.scenario!.play!.secondsPerTurn).toBeGreaterThan(0);
    expect(model.shape).toBe("mods-src/seraphhorizons/assets/seraphhorizons/shapes/block/jawbreaker.json");
  });
});
