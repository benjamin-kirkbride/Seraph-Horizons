import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { textureColour } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The Parkes kettle's shipped files, and the poses ParkesKettle/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads θ (the axle: the drive shaft and its pinion about z, the stirrer about y
// through a mitre pair, both ratio 1) and depth (the fire door, lifted open); everything else is a requires, the build's
// stages (brickwork, lining, kettle, stirrer) among them; the iron frame has none. tools/tests/test_parkeskettle_model.py
// replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/parkeskettle-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/parkeskettle.json") as Shape;
type RefPose = { theta: number; depth: number; matrices: Record<string, number[][]> };
const reference = read("tests/ParkesKettle/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, depth: number): Pose => ({ theta, depth, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the Parkes kettle's rig against its rig-reference.json", () => {
  it("reads θ and depth only, takes the axle at the back, and has a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: true, lifting: false, travel: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    expect(r.powerCell).toEqual([0, 2, 2]);
    expect(r.powerFace).toBe("south");
    for (const k of ["airCell", "waterCell", "inputCell", "outputCell", "work"]) expect(r[k]).toBeUndefined();
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

  it("turns the drive and the stirrer with θ alike, lifts only the fire door with depth, and moves nothing else", () => {
    const at = (theta: number, depth: number) => partMatrices(parts, pose(theta, depth), order);
    const idx = (id: string) => parts.findIndex((q) => q.id === id);
    const [drive, stirrer, door] = [idx("drive"), idx("stirrer"), idx("firedoor")];
    for (const theta of [0.4, -1.3, 2.9]) {
      const ms = at(theta, 0.5);
      ms.forEach((m, i) => {
        if (i !== drive && i !== stirrer && i !== door) m.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12));
      });
      // the drive about z, the stirrer about y, by the same angle (the mitre pair, 1:1)
      expect(ms[drive]![0]).toBeCloseTo(Math.cos(theta), 6);
      expect(ms[drive]![1]).toBeCloseTo(Math.sin(theta), 6);
      expect(ms[stirrer]![0]).toBeCloseTo(Math.cos(theta), 6);
      expect(ms[stirrer]![8]).toBeCloseTo(Math.sin(theta), 6);
    }
    const lift = (rig as unknown as { furnace: { fireDoorLift: number } }).furnace.fireDoorLift;
    for (const depth of [0, 0.5, 1]) expect(at(1.0, depth)[door]![13]).toBeCloseTo(lift * depth, 6);
  });

  it("shows the stages, the bath's levels, the zinc, the crust, the fire, the damper and the cap by requires, and glows only where the coal burns", () => {
    expect(requiresValues(parts)).toEqual(["brickwork", "lining", "kettle", "lead1", "lead2", "lead3", "lead4", "zinc", "crust", "fuel1", "fuel2", "fire1", "fire2", "damperopen", "damperhalf", "dampershut", "outletcap", "stirrer"]);
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const key = parts[partOf(globs, f.chain)]!.id.replace(/\d$/, "").replace(/^damper.*/, "damper");
      for (const code of textureCodes(f.element)) (codes[key] ??= new Set()).add(code);
    }
    expect([...codes.lead!]).toEqual(["lead"]);
    expect([...codes.zinc!]).toEqual(["zinc"]);
    expect([...codes.crust!]).toEqual(["crust"]);
    // the viewer tints the lead, the zinc and the crust apart
    const tint = (code: string) => textureColour(code, shape.textures?.[code] ?? null);
    expect(new Set([tint("lead"), tint("zinc"), tint("crust")]).size).toBe(3);
    expect([...codes.fuel!]).toEqual(["fuel"]);
    expect([...codes.fire!]).toEqual(["ember"]);
    expect([...codes.brickwork!]).toEqual(["brick"]);
    expect([...codes.lining!]).toEqual(["lining"]);
    expect([...codes.frame!]).toEqual(["iron"]);
    expect([...codes.kettle!]).toEqual(["iron"]);
    expect(new Set(codes.drive)).toEqual(new Set(["oak", "steel"]));
    expect(new Set(codes.stirrer)).toEqual(new Set(["iron", "steel"]));
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      const id = parts[partOf(globs, f.chain)]!.id;
      if (/^fire\d/.test(id)) expect(Math.min(...glows)).toBeGreaterThan(0);
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

describe("the Parkes kettle's scenario", () => {
  const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
  const model = manifest.models.find((m) => m.id === "parkeskettle")!;
  type Bricks = { code: string; default: string; types: Record<string, { base: string; overlays?: string[] }> };
  const bricks = (rig as unknown as { bricks: Bricks }).bricks;

  it("offers every brick type of the rig, in its order, each its colour-bearing texture, opening at the default", () => {
    const select = model.scenario!.textures!;
    expect(select).toHaveLength(1);
    expect(select[0]!.code).toBe(bricks.code);
    expect(select[0]!.default).toBe(bricks.default);
    expect(select[0]!.options.map((o) => o.id)).toEqual(Object.keys(bricks.types));
    for (const o of select[0]!.options) expect(o.path, o.id).toBe(bricks.types[o.id]!.overlays?.[0] ?? bricks.types[o.id]!.base);
  });

  it("steps the build through the rig's stages, and the kettle's contents, the fire and the damper wait for the kettle", () => {
    const groups = Object.fromEntries((model.scenario!.states as { id: string; values: string[]; needs?: string[] }[]).map((g) => [g.id, g]));
    const stages = (rig as unknown as { build: { stages: { requires: string }[] } }).build.stages.map((s) => s.requires);
    expect(groups.build!.values).toEqual(stages);
    for (const g of ["kettle", "fire", "damper"]) expect(groups[g]!.needs).toEqual(["brickwork", "lining", "kettle"]);
    expect(groups.outlet!.needs).toEqual(["brickwork", "lining"]);
  });
});
