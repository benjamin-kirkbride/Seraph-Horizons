import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, stateGroups } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The amalgam pan's shipped files, and the poses AmalgamPan/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads one input, θ, the crank's angle: the crank (shaft, pinion,
// arm and handle) turns by it about its shaft, and the pinion turns the crown wheel, and the spindle and
// muller with it, by 2/3 of it about the pan's axis; the contents (mercury, amalgam) and the frame stand
// still. tools/tests/test_amalgampan_model.py and the mod's C# tests replay the same file.
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
  it("reads θ alone, has no work and no power cell, and fits only its contents", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(rig.powerCell).toBeUndefined();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["crank", null, null],
      ["muller", null, null],
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

  it("turns the crank once a turn about its shaft and the muller 2/3 as fast about the pan's axis, and nothing else", () => {
    const i = (id: string) => parts.findIndex((p) => p.id === id);
    const shaftY = (rig.parts!.find((p) => p.id === "crank")!.drivers![0] as { pivot: number[] }).pivot[1]!;
    for (const theta of [0.4, 2.2, -1.3, 7.9]) {
      const ms = partMatrices(parts, pose(theta), order, null);
      // the muller: the pan's axis stays put, and a point a block out along x turns by 2θ/3 about +y
      const m = ms[i("muller")]!;
      expect(apply(m, [0.5, 0.6, 0.5]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([0.5, 0.6, 0.5]);
      const p = apply(m, [1.5, 0.6, 0.5]);
      expect(p[0]! - 0.5).toBeCloseTo(Math.cos((2 * theta) / 3), 9);
      expect(p[2]! - 0.5).toBeCloseTo(-Math.sin((2 * theta) / 3), 9);
      // the crank: its shaft's axis stays put, and a point a block east of it turns by θ about +z
      const c = ms[i("crank")]!;
      expect(apply(c, [0.5, shaftY, 0.2]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([0.5, shaftY, 0.2]);
      const q = apply(c, [1.5, shaftY, 0.2]);
      expect(q[0]! - 0.5).toBeCloseTo(Math.cos(theta), 9);
      expect(q[1]! - shaftY).toBeCloseTo(Math.sin(theta), 9);
      for (const still of ["mercury", "amalgam", "frame"]) expect(ms[i(still)]!.map((v, k) => v - (k % 5 === 0 ? 1 : 0)).every((v) => Math.abs(v) < 1e-12)).toBe(true);
    }
    // the viewer's θ slider spans three turns of the crank, two of the muller
    expect(thetaTurns(parts)).toBe(3);
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
    expect([...codes.muller!]).toEqual(["iron"]);
    expect([...codes.crank!].sort()).toEqual(["iron", "oak"]);
  });

  it("marks the operator's side and the pan's floor", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "operatorSide")).toMatchObject({ kind: "side", side: "north" });
    expect(anchors.find((a) => a.key === "pan")).toMatchObject({ kind: "point", pos: [0.5, 0.625, 0.5] });
  });
});

describe("the amalgam pan's page", () => {
  const model = manifest.models.find((m) => m.id === "amalgam-pan")!;

  it("shows one of its contents at a time, from a select", () => {
    const groups = stateGroups(model.scenario!.states, ["mercury", "amalgam"]);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([["contents", ["mercury", "amalgam"]]]);
    const all = { mercury: true, amalgam: true };
    const spec = groups[0]!.spec;
    const at = (id: string) => applyState(all, spec.options.find((o) => o.id === id)!, groups[0]!.owns);
    expect(at("empty")).toEqual({ mercury: false, amalgam: false });
    expect(at("mercury")).toEqual({ mercury: true, amalgam: false });
    expect(at("amalgam")).toEqual({ mercury: false, amalgam: true });
    expect(spec.default).toBe("mercury");
  });
});
