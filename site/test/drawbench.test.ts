import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The draw bench's shipped files, and the poses DrawBench/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads W, its work (chute sections drawn), k (the hollow section's metal), p, the oil
// and the axle's angle and travel; every motion of a stroke is a gauge with one window per section.
// tools/tests/test_drawbench_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/drawbench-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/drawbench.json") as Shape;
type RefPose = { theta: number; travel: number; work: number; size: number; presence: number; oil: number; matrices: Record<string, number[][]> };
const reference = read("tests/DrawBench/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: p.travel, work: p.work, size: p.size, presence: p.presence, feed: 0, oil: p.oil });

describe("the draw bench's rig against its rig-reference.json", () => {
  it("counts its work in sections, four a hollow of either metal, and has a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "sections drawn", unit: "sections" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(4);
    expect(workEnd(path, 2)).toBe(4);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows W, the metal (size), its presence, the axle and the oil; no depth, lifting or feed
    expect(rigInputs(parts)).toMatchObject({ theta: true, travel: true, depth: false, lifting: false, work: true, size: true, presence: true, oil: true });
    expect(rigInputs(parts).feed).toBeUndefined();
    expect(new Set(reference.poses.map((p) => p.oil))).toEqual(new Set([0, 0.35, 1]));
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}, W ${p.work}, k ${p.size}, p ${p.presence}, oil ${p.oil}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("fits the parts in build order, the work by metal, and gives the die its own texture code", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "gearbox", "chain", "dog", "mandrel", "die", "billetlead", "billetcopper"]));
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    expect(byId.dog!.requires).toBe("dog");
    expect(byId.jaw!.ride).toBe("dog");
    const dieTextures = new Set<string>();
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) if (parts[partOf(globs, f.chain)]!.id === "die") for (const code of textureCodes(f.element)) dieTextures.add(code);
    expect([...dieTextures]).toEqual(["die"]);
    // copper takes twice lead's axle turns a section; the viewer's Play is geared at lead's pace
    const draw = (rig as unknown as { draw: { turnsPerSection: { thin: number; thick: number } } }).draw;
    expect(draw.turnsPerSection.thick).toBeCloseTo(2 * draw.turnsPerSection.thin, 5);
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
