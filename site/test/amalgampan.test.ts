import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, stateGroups } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The amalgam pan's shipped files, and the poses AmalgamPan/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads one input, θ, the crank's angle: the muller (spindle, hub,
// arms, crank) turns by it about the pan's axis and the shoes ride it; the contents (mercury, amalgam) and
// the frame stand still. tools/tests/test_amalgampan_model.py and the mod's C# tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/amalgampan-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/amalgampan.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/AmalgamPan/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);

describe("the amalgam pan's rig against its rig-reference.json", () => {
  it("reads θ alone, has no work and no power cell, and fits the muller, its shoes and two contents", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(rig.powerCell).toBeUndefined();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["muller", "muller", null],
      ["shoes", "shoes", "muller"],
      ["mercury", "mercury", null],
      ["amalgam", "amalgam", null],
      ["frame", null, null],
    ]);
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

  it("turns the muller and its shoes once a turn of the crank about the pan's axis, and nothing else", () => {
    const i = (id: string) => parts.findIndex((p) => p.id === id);
    for (const theta of [0.4, 2.2, -1.3, 7.9]) {
      const ms = partMatrices(parts, pose(theta), order, null);
      const m = ms[i("muller")]!;
      // the axis stays put, and a point a block out along x turns by θ about +y
      expect(apply(m, [0.5, 0.6, 0.5]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([0.5, 0.6, 0.5]);
      const p = apply(m, [1.5, 0.6, 0.5]);
      expect(p[0]! - 0.5).toBeCloseTo(Math.cos(theta), 9);
      expect(p[2]! - 0.5).toBeCloseTo(-Math.sin(theta), 9);
      expect(ms[i("shoes")]).toEqual(m);
      for (const still of ["mercury", "amalgam", "frame"]) expect(ms[i(still)]!.map((v, k) => v - (k % 5 === 0 ? 1 : 0)).every((v) => Math.abs(v) < 1e-12)).toBe(true);
    }
  });

  it("gives every element a part and every part an element, the contents their own texture codes", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const c of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(c);
    expect([...codes.mercury!]).toEqual(["mercury"]);
    expect([...codes.amalgam!]).toEqual(["amalgam"]);
    expect([...codes.shoes!]).toEqual(["shoe"]);
    expect([...codes.muller!].sort()).toEqual(["iron", "oak"]);
  });

  it("marks the operator's side and the pan's floor", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "operatorSide")).toMatchObject({ kind: "side", side: "north" });
    expect(anchors.find((a) => a.key === "pan")).toMatchObject({ kind: "point", pos: [0.5, 0.375, 0.5] });
  });
});

describe("the amalgam pan's page", () => {
  const model = manifest.models.find((m) => m.id === "amalgam-pan")!;

  it("shows one of its contents at a time, from a select, and fits the muller by checkbox", () => {
    const groups = stateGroups(model.scenario!.states, ["muller", "shoes", "mercury", "amalgam"]);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([["contents", ["mercury", "amalgam"]]]);
    const all = { muller: true, shoes: true, mercury: true, amalgam: true };
    const spec = groups[0]!.spec;
    const at = (id: string) => applyState(all, spec.options.find((o) => o.id === id)!, groups[0]!.owns);
    expect(at("empty")).toEqual({ muller: true, shoes: true, mercury: false, amalgam: false });
    expect(at("mercury")).toEqual({ muller: true, shoes: true, mercury: true, amalgam: false });
    expect(at("amalgam")).toEqual({ muller: true, shoes: true, mercury: false, amalgam: true });
    expect(spec.default).toBe("mercury");
  });
});
