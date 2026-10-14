import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { textureColour } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The cupellation furnace's shipped files (CupellationFurnace/tools/make_shape.py). Nothing in it moves: its blast
// comes by pipe from Steelmaking Expanded's engine air blower to the socket at blastCell's blastFace, so its rig reads
// no input and every part is a requires (the build's stages, the test's and the fire's state, the damper's places, the
// outlet's cap). tools/tests/test_cupellationfurnace_model.py holds the same files in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/cupellationfurnace-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/cupellationfurnace.json") as Shape;
const parts = rig.parts!;
const order = rideOrder(parts);

const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the cupellation furnace's rig", () => {
  it("reads no input, takes no mechanical power, and takes its blast by pipe at the back wall's middle", () => {
    expect(Object.values(rigInputs(parts)).some(Boolean)).toBe(false);
    const r = rig as unknown as Record<string, unknown>;
    expect(r.powerCell).toBeUndefined();
    expect(r.powerFace).toBeUndefined();
    expect(r.blastCell).toEqual([0, 1, 1]);
    expect(r.blastFace).toBe("south");
    expect((r.furnace as { blastGas: string }).blastGas).toBe("Air");
    const cells = (rig.cells ?? []).map((c) => c.pos.join(","));
    expect(cells).toHaveLength(14);
    expect(cells).toContain("0,1,1");
    expect(cells).not.toContain("0,1,2");
  });

  it("moves nothing", () => {
    for (const theta of [0, 1.3, -4.4]) partMatrices(parts, pose(theta), order).forEach((m) => m.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12)));
  });

  it("shows the stages, the test's and the fire's state, the damper and the cap by requires, with their own texture codes, and glows only where it burns", () => {
    expect(requiresValues(parts)).toEqual(["brickwork", "lining", "test", "fittings", "coal", "fire", "lead", "silver", "stream", "litharge", "damperopen", "damperhalf", "dampershut", "outletcap"]);
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

  it("binds the brickwork with one frame of angle iron, its members butted face to face, the L's inside corner joined", () => {
    type Box = { lo: number[]; hi: number[] };
    const members = new Map<string, Box[]>();
    for (const f of flattenShape(shape.elements)) {
      const e = f.element as { name: string; from: number[]; to: number[]; rotationX?: number; rotationY?: number; rotationZ?: number };
      if (!/^fr_(angle|band)_/.test(e.name)) continue;
      expect(f.chain).toHaveLength(1);
      expect([e.rotationX ?? 0, e.rotationY ?? 0, e.rotationZ ?? 0]).toEqual([0, 0, 0]);
      const key = e.name.split("_").slice(0, 3).join("_");
      members.set(key, [...(members.get(key) ?? []), { lo: e.from, hi: e.to }]);
    }
    const span = (a: Box, b: Box, q: number) => Math.min(a.hi[q]!, b.hi[q]!) - Math.max(a.lo[q]!, b.lo[q]!);
    const touch = (a: Box, b: Box) =>
      [0, 1, 2].some((k) => Math.min(Math.abs(a.hi[k]! - b.lo[k]!), Math.abs(a.lo[k]! - b.hi[k]!)) < 0.06 && [0, 1, 2].every((q) => q === k || span(a, b, q) >= 0.3));
    const names = [...members.keys()].sort();
    expect(names.filter((n) => n.startsWith("fr_angle_"))).toHaveLength(5);
    expect(names.filter((n) => n.startsWith("fr_band_"))).toHaveLength(6);
    const links = new Map(names.map((n) => [n, new Set<string>()]));
    for (const m of names)
      for (const n of names) {
        if (m >= n) continue;
        const pairs = members.get(m)!.flatMap((a) => members.get(n)!.map((b) => [a, b] as const));
        expect(pairs.some(([a, b]) => [0, 1, 2].every((q) => span(a, b, q) > 0.1)), `${m} runs into ${n}`).toBe(false);
        if (pairs.some(([a, b]) => touch(a, b))) {
          links.get(m)!.add(n);
          links.get(n)!.add(m);
        }
      }
    expect(links.get("fr_band_s")!.has("fr_band_m")).toBe(true);
    for (const n of names) if (n.startsWith("fr_angle_")) expect([...links.get(n)!].some((o) => o.startsWith("fr_band_")), n).toBe(true);
    const seen = new Set<string>();
    const todo = [names[0]!];
    while (todo.length) {
      const n = todo.pop()!;
      if (seen.has(n)) continue;
      seen.add(n);
      todo.push(...links.get(n)!);
    }
    expect([...seen].sort()).toEqual(names);
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
