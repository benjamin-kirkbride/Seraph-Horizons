import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The acid parting vessel's shipped files, and the poses AcidParting/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads depth (the sliding fire door: 0 open, 1 shut; the door is the only part
// that moves) and θ (the stoker's clock, carried by a ratio-0 rotate so the viewer offers Play); everything else is a
// requires: the build's stages (brickwork, lining, fittings), the water line in copper or lead and its cock's two
// places, the damper's three, the fire, the vessel's contents and the outlet's cap; the iron frame has none.
// tools/tests/test_acidparting_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/acidparting-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/acidparting.json") as Shape;
type RefPose = { theta: number; depth: number; matrices: Record<string, number[][]> };
const reference = read("tests/AcidParting/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "acid-parting")!;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, depth: number): Pose => ({ theta, depth, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the acid parting vessel's rig against its rig-reference.json", () => {
  it("reads θ and depth only, has no power cell, no air port and no chute, and a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: true, lifting: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    for (const key of ["powerCell", "airCell", "inputCell", "outputCell", "work"]) expect(r[key]).toBeUndefined();
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

  it("moves nothing with θ, and only the fire door with depth: slid straight down from open to shut", () => {
    const at = (theta: number, depth: number) => partMatrices(parts, pose(theta, depth), order);
    for (const depth of [0.3, 0.8]) {
      const a = at(0, depth);
      const b = at(4.2, depth);
      a.forEach((m, i) => m.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    const door = parts.findIndex((q) => q.id === "door");
    const shut = at(0, 1)[door]![13]!;
    expect(shut).toBeLessThan(0);
    for (const depth of [0, 0.5, 1]) {
      const ms = at(0, depth);
      ms.forEach((m, i) => {
        if (i !== door) m.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12));
      });
      expect(ms[door]![13]).toBeCloseTo(shut * depth, 9);
      expect(ms[door]![12]).toBeCloseTo(0, 12);
      expect(ms[door]![14]).toBeCloseTo(0, 12);
    }
  });

  it("shows the stages, the water line, the damper, the fire, the contents and the cap by requires, with their own texture codes, and glows only where it burns", () => {
    expect(requiresValues(parts)).toEqual(["brickwork", "lining", "fittings", "damperopen", "damperhalf", "dampershut", "pipecopper", "pipelead", "cockshut", "cockopen", "fuel", "fire", "charge", "acid", "solution", "gold", "wash", "outletcap"]);
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const key = parts[partOf(globs, f.chain)]!.id.replace(/^damper.*/, "damper");
      for (const code of textureCodes(f.element)) (codes[key] ??= new Set()).add(code);
    }
    expect([...codes.brickwork!]).toEqual(["brick"]);
    expect([...codes.lining!]).toEqual(["lining"]);
    expect([...codes.frame!]).toEqual(["iron"]);
    expect([...codes.hoodiron!]).toEqual(["iron"]);
    expect(new Set(codes.bath)).toEqual(new Set(["iron", "sand"]));
    expect([...codes.vessel!]).toEqual(["porcelain"]);
    expect([...codes.door!]).toEqual(["iron"]);
    expect([...codes.damper!]).toEqual(["iron"]);
    expect(new Set(codes.pipecopper)).toEqual(new Set(["pipecopper", "solder", "brass"]));
    expect(new Set(codes.pipelead)).toEqual(new Set(["pipelead", "solder", "brass"]));
    expect([...codes.cockshut!]).toEqual(["brass"]);
    expect(new Set(codes.cockopen)).toEqual(new Set(["brass", "water"]));
    expect([...codes.fuel!]).toEqual(["fuel"]);
    expect([...codes.fire!]).toEqual(["ember"]);
    expect([...codes.charge!]).toEqual(["dore"]);
    expect([...codes.acid!]).toEqual(["acid"]);
    expect([...codes.solution!]).toEqual(["solution"]);
    expect([...codes.gold!]).toEqual(["gold"]);
    expect([...codes.wash!]).toEqual(["water"]);
    expect([...codes.outletcap!]).toEqual(["iron"]);
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      if (glows.length === 0) continue;
      const id = parts[partOf(globs, f.chain)]!.id;
      if (id === "fire") expect(Math.min(...glows)).toBeGreaterThan(0);
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

describe("the acid parting vessel's page in site/models.json", () => {
  type Bricks = { code: string; default: string; types: Record<string, { base: string; overlays?: string[] }> };
  const bricks = (rig as unknown as { bricks: Bricks }).bricks;
  const scenario = model.scenario!;

  it("offers every brick type of the rig, in its order, its colour-bearing texture each, opening at its default", () => {
    const brick = scenario.textures![0]!;
    expect(brick.code).toBe(bricks.code);
    expect(brick.default).toBe(bricks.default);
    expect(brick.options.map((o) => o.id)).toEqual(Object.keys(bricks.types));
    for (const o of brick.options) expect(o.path, o.id).toBe(bricks.types[o.id]!.overlays?.[0] ?? bricks.types[o.id]!.base);
    expect(shape.textures?.[bricks.code]).toBe(brick.options.find((o) => o.id === bricks.default)!.path);
  });

  it("labels every requires value, and its Build states are the rig's stages in order", () => {
    expect(Object.keys(scenario.requires!).sort()).toEqual(requiresValues(parts).slice().sort());
    type Build = { stages: { requires: string }[] };
    const stages = (rig as unknown as { build: Build }).build.stages.map((s) => s.requires);
    const groups = scenario.states as unknown as { id: string; values: string[] }[];
    expect(groups.find((g) => g.id === "build")!.values).toEqual(stages);
  });
});
