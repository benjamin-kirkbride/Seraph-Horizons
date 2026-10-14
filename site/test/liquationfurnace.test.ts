import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { pickedTextures } from "../src/lib/model-scenario.ts";
import { buildModelView, withPickedTextures } from "../src/lib/model-view.ts";
import { compileGlobs, corners, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The liquation furnace's shipped files, and the poses LiquationFurnace/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads depth (the fire door: 0 open, slid up its guides, 1 shut; the door is
// the only part that moves) and θ (the stoker's clock, carried by a ratio-0 rotate so the viewer offers Play);
// everything else is a requires, the build's stages (brickwork, lining, fittings) among them; the iron frame has none.
// tools/tests/test_liquationfurnace_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/liquationfurnace-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/liquationfurnace.json") as Shape;
type RefPose = { theta: number; depth: number; matrices: Record<string, number[][]> };
const reference = read("tests/LiquationFurnace/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, depth: number): Pose => ({ theta, depth, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the liquation furnace's rig against its rig-reference.json", () => {
  it("reads θ and depth only, has no power cell and no air port, and a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, depth: true, lifting: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    expect(r.powerCell).toBeUndefined();
    expect(r.airCell).toBeUndefined();
    expect(r.work).toBeUndefined();
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

  it("moves nothing with θ, and only the fire door with depth: slid down its travel from open to shut", () => {
    const at = (theta: number, depth: number) => partMatrices(parts, pose(theta, depth), order);
    for (const depth of [0.3, 0.8]) {
      const a = at(0, depth);
      const b = at(4.2, depth);
      a.forEach((m, i) => m.forEach((v, j) => expect(Math.abs(v - b[i]![j]!)).toBeLessThan(1e-12)));
    }
    const door = parts.findIndex((q) => q.id === "door");
    const travel = (rig as unknown as { furnace: { doorTravel: number } }).furnace.doorTravel;
    for (const depth of [0, 0.5, 1]) {
      const ms = at(0, depth);
      ms.forEach((m, i) => {
        if (i !== door) m.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12));
      });
      // a slide down y by travel × depth: the matrix's y translation, no turn
      expect(ms[door]![13]).toBeCloseTo(-travel * depth, 9);
      expect(ms[door]![0]).toBeCloseTo(1, 12);
      expect(ms[door]![5]).toBeCloseTo(1, 12);
    }
  });

  it("shows the build's stages, the charge, the fire, the damper and the cap by requires, with their own texture codes, and glows only where it burns", () => {
    const req = requiresValues(parts);
    expect(req).toEqual(["brickwork", "lining", "fittings", "cakes", "tinrun", "tin", "lead", "fuel1", "fuel2", "fire1", "fire2", "damperopen", "damperhalf", "dampershut", "outletcap"]);
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const key = parts[partOf(globs, f.chain)]!.id.replace(/\d$/, "").replace(/^damper.*/, "damper");
      for (const code of textureCodes(f.element)) (codes[key] ??= new Set()).add(code);
    }
    expect([...codes.cakes!]).toEqual(["cake"]);
    expect([...codes.tinrun!]).toEqual(["tin"]);
    expect([...codes.tin!]).toEqual(["tin"]);
    expect([...codes.lead!]).toEqual(["lead"]);
    expect([...codes.fuel!]).toEqual(["fuel"]);
    expect([...codes.fire!]).toEqual(["ember"]);
    for (const p of ["plate", "pot", "door", "damper", "outletcap", "frame"]) expect([...codes[p]!], p).toEqual(["iron"]);
    expect([...codes.brickwork!]).toEqual(["brick"]);
    expect([...codes.lining!]).toEqual(["lining"]);
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      if (glows.length === 0) continue;
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

describe("the liquation furnace's Brick select against the rig's bricks", () => {
  type Bricks = { code: string; default: string; types: Record<string, { base: string; overlays?: string[] }> };
  const bricks = (rig as unknown as { bricks: Bricks }).bricks;
  const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
  const select = manifest.models.find((m) => m.id === "liquation-furnace")!.scenario!.textures!;

  it("offers every brick type of the rig, in its order, for its code, opening at its default", () => {
    expect(select).toHaveLength(1);
    const brick = select[0]!;
    expect(brick.code).toBe(bricks.code);
    expect(brick.default).toBe(bricks.default);
    expect(brick.options.map((o) => o.id)).toEqual(Object.keys(bricks.types));
  });

  it("names each type's colour-bearing texture, and the crucible furnace's colours", () => {
    for (const o of select[0]!.options) {
      const t = bricks.types[o.id]!;
      expect(o.path, o.id).toBe(t.overlays?.[0] ?? t.base);
    }
    const def = select[0]!.options.find((o) => o.id === bricks.default)!;
    expect(shape.textures?.[bricks.code]).toBe(def.path);
    const crucible = manifest.models.find((m) => m.id === "crucible-furnace")!.scenario!.textures![0]!;
    expect(select[0]!.options).toEqual(crucible.options);
  });

  it("draws the brickwork in the picked type's colour", () => {
    const view = buildModelView(shape, rig, { textures: select });
    const opening = view.textures.find((t) => t.code === "brick")!;
    expect(opening.colour).toBe(select[0]!.options.find((o) => o.id === "red")!.colour);
    const black = withPickedTextures(view.textures, pickedTextures(select, { brick: "black" })).find((t) => t.code === "brick")!;
    expect(black).toMatchObject({ path: "game:block/clay/brick/four/running/black1", colour: "#494339", elements: opening.elements });
  });
});

describe("the liquation furnace's outlet", () => {
  it("stands the chimney's line on the middle of the outlet's cell, over a square flue open in the shape", () => {
    const r = rig as unknown as { outletCell: number[]; chimney: { origin: number[]; axis: string; length: number } };
    const [ox, oy, oz] = r.outletCell as [number, number, number];
    expect(r.chimney.origin).toEqual([ox + 0.5, oy + 1 + r.chimney.length / 2, oz + 0.5]);
    // the flue under the line: open over the 4 by 4 square centred on it, closed just
    // outside it (the cap aside); sampled off the brick boxes' faces, where a point is inside neither box
    const boxes = flattenShape(shape.elements)
      .filter((f) => !f.element.name.startsWith("outletcap"))
      .map((f) => {
        const cs = corners(f);
        return [0, 1, 2].map((k) => [Math.min(...cs.map((c) => c[k]!)), Math.max(...cs.map((c) => c[k]!))]);
      });
    const filled = (p: number[]) => boxes.some((b) => [0, 1, 2].every((k) => b[k]![0]! < p[k]! && p[k]! < b[k]![1]!));
    const cx = (ox + 0.5) * 16, cz = (oz + 0.5) * 16;
    for (const y of [31.25, 27.25]) {
      for (const t of [-1.75, -0.75, 0.75, 1.75]) {
        for (const p of [[cx + t, y, cz - 1.75], [cx + t, y, cz + 1.75], [cx - 1.75, y, cz + t], [cx + 1.75, y, cz + t]]) expect(filled(p), String(p)).toBe(false);
        for (const p of [[cx + t, y, cz - 2.25], [cx + t, y, cz + 2.25], [cx - 2.25, y, cz + t], [cx + 2.25, y, cz + t]]) expect(filled(p), String(p)).toBe(true);
      }
    }
  });
});
