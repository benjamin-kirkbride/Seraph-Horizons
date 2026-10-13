import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { textureColour } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The cupellation furnace's shipped files, and the poses CupellationFurnace/tools/make_shape.py computes from them
// with machinegen's reference maths. Its rig reads θ alone, the axle's angle: the engine shaft turns with it (ratio 1
// about z) and the Scotch yoke slides along x with the crank pin; everything else is a requires (the build's stages,
// the test's and the fire's state, the damper's places, the outlet's cap). tools/tests/test_cupellationfurnace_model.py
// replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/cupellationfurnace-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/cupellationfurnace.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/CupellationFurnace/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the cupellation furnace's rig against its rig-reference.json", () => {
  it("reads θ only, takes the axle at a power cell, and has a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: false, lifting: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    expect(r.powerCell).toEqual([1, 1, 2]);
    expect(r.powerFace).toBe("south");
    expect(r.work).toBeUndefined();
    expect(reference.poses.length).toBeGreaterThan(10);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta), order);
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

  it("moves only the shaft and the yoke, the yoke exactly with the crank pin (a Scotch yoke)", () => {
    const shaft = parts.findIndex((q) => q.id === "shaft");
    const yoke = parts.findIndex((q) => q.id === "yoke");
    const pivot = (parts[shaft]!.drivers![0] as { pivot: number[] }).pivot;
    const throwR = (parts[yoke]!.drivers![0] as { amplitude: number }).amplitude;
    for (const theta of [0, 0.7, 1.9, Math.PI, 4.4, -2.2, 9.1]) {
      const ms = partMatrices(parts, pose(theta), order);
      ms.forEach((m, i) => {
        if (i !== shaft && i !== yoke) m.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12));
      });
      // the crank pin, under the shaft at θ 0, carried round by the shaft; the yoke's slot centre carried by the yoke
      const pin = apply(ms[shaft]!, [pivot[0]!, pivot[1]! - throwR, 2.5]);
      const slot = apply(ms[yoke]!, [pivot[0]!, pivot[1]!, 2.5]);
      expect(Math.abs(pin[0]! - slot[0]!)).toBeLessThan(1e-9);
      expect(Math.abs(slot[1]! - pivot[1]!)).toBeLessThan(1e-12);
      expect(Math.abs(pin[1]! - pivot[1]!)).toBeLessThanOrEqual(throwR + 1e-12);
    }
  });

  it("shows the stages, the test's and the fire's state, the damper and the cap by requires, with their own texture codes, and glows only where it burns", () => {
    expect(requiresValues(parts)).toEqual(["brickwork", "lining", "test", "engine", "fittings", "coal", "fire", "lead", "silver", "stream", "litharge", "damperopen", "damperhalf", "dampershut", "outletcap"]);
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
    expect(new Set(codes.test)).toEqual(new Set(["iron", "boneash"]));
    expect(new Set(codes.engine)).toEqual(new Set(["iron", "leather"]));
    expect(new Set(codes.shaft)).toEqual(new Set(["oak", "steel", "iron"]));
    expect(new Set(codes.yoke)).toEqual(new Set(["iron", "steel"]));
    expect([...codes.fittings!]).toEqual(["iron"]);
    expect([...codes.coal!]).toEqual(["coal"]);
    expect([...codes.fire!]).toEqual(["ember"]);
    expect([...codes.lead!]).toEqual(["lead"]);
    expect([...codes.silver!]).toEqual(["silver"]);
    expect([...codes.stream!]).toEqual(["litharge"]);
    expect([...codes.litharge!]).toEqual(["litharge"]);
    expect([...codes.damper!]).toEqual(["iron"]);
    expect([...codes.outletcap!]).toEqual(["iron"]);
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      if (glows.length === 0) continue;
      const id = parts[partOf(globs, f.chain)]!.id;
      if (["fire", "lead", "silver", "stream"].includes(id)) expect(Math.min(...glows)).toBeGreaterThan(0);
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

  it("draws the test, the lead, the cake, the litharge and the fire in colours of their own", () => {
    const tex = shape.textures!;
    const colours = ["lead", "silver", "litharge", "boneash", "coal", "ember"].map((c) => textureColour(c, tex[c]!));
    expect(new Set(colours).size).toBe(colours.length);
    for (const c of colours) expect(c).not.toBe(textureColour("iron", tex.iron!));
  });
});

describe("the cupellation furnace's Brick select against the rig's bricks", () => {
  type Bricks = { code: string; default: string; types: Record<string, { base: string; overlays?: string[] }> };
  const bricks = (rig as unknown as { bricks: Bricks }).bricks;
  const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
  const select = manifest.models.find((m) => m.id === "cupellation-furnace")!.scenario!.textures!;

  it("offers every brick type of the rig, in its order, its overlay over the cream base, else its base", () => {
    expect(select).toHaveLength(1);
    const brick = select[0]!;
    expect(brick.code).toBe(bricks.code);
    expect(brick.default).toBe(bricks.default);
    expect(brick.options.map((o) => o.id)).toEqual(Object.keys(bricks.types));
    for (const o of brick.options) expect(o.path, o.id).toBe(bricks.types[o.id]!.overlays?.[0] ?? bricks.types[o.id]!.base);
    expect(shape.textures?.[bricks.code]).toBe(brick.options.find((o) => o.id === bricks.default)!.path);
  });
});
