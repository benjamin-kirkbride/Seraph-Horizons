import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { compileGlobs, flattenShape, partMatrices, partOf, rigInputs, rideOrder, textureCodes, workEnd, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The mandrel forging station's shipped files, and the poses MandrelStation/tools/make_shape.py computes
// from them with machinegen's reference maths. Its rig reads W (the forging of one hollow: blows struck
// over blows needed), k (the hollow's metal), p and θ (the hammer's clock, carried by a ratio-0 rotate so
// the viewer offers Play); every motion is a gauge. tools/tests/test_mandrelstation_model.py replays the
// same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/mandrelstation-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/mandrelstation.json") as Shape;
type RefPose = { theta: number; work: number; size: number; presence: number; matrices: Record<string, number[][]> };
const reference = read("tests/MandrelStation/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const path = workOf(rig)!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (p: RefPose): Pose => ({ theta: p.theta, depth: 0, lifting: 0, travel: Math.abs(p.theta), work: p.work, size: p.size, presence: p.presence, feed: 0, oil: 0 });

describe("the mandrel station's rig against its rig-reference.json", () => {
  it("counts its work in hollows, one forging a hollow of either metal, and has a matrix for every part", () => {
    expect(path).toMatchObject({ kind: "work", name: "blows", unit: "hollows" });
    expect(rig.trunkPath).toBeUndefined();
    expect(workEnd(path, 1)).toBe(1);
    expect(workEnd(path, 2)).toBe(1);
    // the spring is its own class, 3, with a job of 1 too
    expect(workEnd(path, 3)).toBe(1);
    expect(new Set(reference.poses.map((p) => p.size))).toEqual(new Set([0, 1, 2, 3]));
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows θ (so Play), W, the metal and its presence; no depth, lifting, feed or oil
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: false, lifting: false, work: true, size: true, presence: true });
    expect(rigInputs(parts).oil).toBeUndefined();
    expect(rigInputs(parts).feed).toBeUndefined();
    // a hand station: no power cell
    expect((rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, W ${p.work}, k ${p.size}, p ${p.presence}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("moves nothing with θ: the hammer's clock", () => {
    const at = (theta: number) => partMatrices(parts, { theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0.3, size: 2, presence: 1, feed: 0, oil: 0 }, order, path);
    const a = at(0);
    const b = at(5.1);
    a.forEach((m, i) => m.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
  });

  it("fits the mandrel, the work by metal or the spring's rod, and gives the mandrel and the spring their own texture codes", () => {
    const req = new Set(parts.map((p) => p.requires ?? null));
    expect(req).toEqual(new Set([null, "mandrel", "hollowlead", "hollowcopper", "rodsteel"]));
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const r = parts[partOf(globs, f.chain)]!.requires ?? "frame";
      for (const code of textureCodes(f.element)) (codes[r] ??= new Set()).add(code);
    }
    expect([...codes.mandrel!]).toEqual(["mandrel"]);
    expect([...codes.hollowlead!]).toEqual(["lead"]);
    expect([...codes.hollowcopper!]).toEqual(["copper"]);
    expect([...codes.rodsteel!]).toEqual(["spring"]);
    expect(new Set(codes.frame!)).toEqual(new Set(["oak", "iron"]));
    // the spring: one steel rod wound into one spring: its 24 sides in winding order, then the straight rod, a stretch a
    // side (each a wire's width shorter), each on its own (no rides), moved by gauges alone
    const wind = (rig as unknown as { wind: { rod: string; spring: string; springsPerRod: number; class: number } }).wind;
    expect(wind).toMatchObject({ rod: "game:rod-steel", spring: "seraphhorizons:spring-steel", springsPerRod: 1, class: 3, hot: ["rodsteel"] });
    const spring = parts.filter((p) => p.requires === "rodsteel");
    const sides = [1, 2, 3, 4, 5, 6].flatMap((t) => ["u", "w", "d", "e"].map((f) => `sp${t}${f}`));
    expect(spring.slice(0, 24).map((p) => p.id)).toEqual(sides);
    expect(spring.slice(24).map((p) => p.id)).toEqual(Array.from({ length: 24 }, (_, j) => `spr${j}`));
    spring.forEach((p) => expect(p.ride ?? null).toBeNull());
    spring.forEach((p) => (p.drivers ?? []).forEach((d) => expect(d.type).toBe("gauge")));
    const forge = (rig as unknown as { forge: { blowsPerHollow: { thin: number; thick: number }; sectionsPerHollow: number; sections: Record<string, string>; hollows: Record<string, string> } }).forge;
    expect(forge.blowsPerHollow.thick).toBeGreaterThan(forge.blowsPerHollow.thin);
    expect(forge.sectionsPerHollow).toBe(2);
    expect(forge.hollows).toEqual({ thin: "game:chutesection-lead", thick: "game:chutesection-copper" });
    expect(forge.sections).toEqual({ thin: "seraphhorizons:pipesection-lead", thick: "seraphhorizons:pipesection-copper" });
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});
