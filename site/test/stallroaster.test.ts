import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { checkScenario, pickedTextures, type Scenario } from "../src/lib/model-scenario.ts";
import { buildModelView, withPickedTextures } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, textureCodes, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The stall roaster's shipped files, and the poses StallRoaster/tools/make_shape.py computes from them with machinegen's
// reference maths. Nothing in the stall moves: every part is a requires (the build's stages, brickwork and lining; the
// bed, the charge's courses cold, burning or roasted, the sulfur; the front's walling, half or whole; the outlet's
// cap), the iron frame has none, and every matrix is the identity. tools/tests/test_stallroaster_model.py replays the
// same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/stallroaster-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/stallroaster.json") as Shape;
type RefPose = { theta: number; depth: number; matrices: Record<string, number[][]> };
const reference = read("tests/StallRoaster/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "stall-roaster")!;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, depth: number): Pose => ({ theta, depth, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the stall roaster's rig against its rig-reference.json", () => {
  it("reads no input, has no power cell, air port or chute, and a matrix for every part at every pose", () => {
    for (const k of ["theta", "depth", "lifting", "work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    const r = rig as unknown as Record<string, unknown>;
    for (const key of ["powerCell", "powerFace", "airCell", "infeedCell", "outputCell", "work", "trunkPath"]) expect(r[key]).toBeUndefined();
    expect(reference.poses.length).toBeGreaterThanOrEqual(3);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose, and every one is the identity", () => {
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta, p.depth), order);
      parts.forEach((q, i) => {
        expect(rows(ms[i]!)).toEqual(p.matrices[q.id]);
        ms[i]!.forEach((v, j) => expect(Math.abs(v - IDENTITY[j]!)).toBeLessThan(1e-12));
      });
    }
  });

  it("shows the build's stages, the charge, the walling and the cap by requires, with their own texture codes, and glows only where it burns", () => {
    expect(requiresValues(parts)).toEqual([
      "brickwork", "lining", "wood", "kindling", "ore1", "ore2", "ore3", "ore4", "fire1", "fire2", "fire3", "fire4",
      "roasted1", "roasted2", "roasted3", "roasted4", "sulfur", "fronthalf", "frontwalled", "outletcap",
    ]);
    const codes: Record<string, Set<string>> = {};
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    for (const f of flat) {
      const key = parts[partOf(globs, f.chain)]!.id.replace(/\d$/, "").replace(/^front.*/, "front");
      for (const code of textureCodes(f.element)) (codes[key] ??= new Set()).add(code);
    }
    expect(Object.fromEntries(Object.entries(codes).map(([k, v]) => [k, [...v].sort()]))).toEqual({
      brickwork: ["brick"], lining: ["lining"], frame: ["iron"], wood: ["wood"], kindling: ["ember"], ore: ["ore"],
      fire: ["ember"], roasted: ["roasted"], sulfur: ["sulfur"], front: ["brick"], outletcap: ["iron"],
    });
    for (const f of flat) {
      const glows = Object.values(f.element.faces ?? {}).map((face) => (face as { glow?: number }).glow ?? 0);
      const id = parts[partOf(globs, f.chain)]!.id;
      if (id === "kindling" || /^fire\d$/.test(id)) expect(Math.min(...glows)).toBeGreaterThan(0);
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

describe("the stall roaster's page", () => {
  type Bricks = { code: string; default: string; types: Record<string, { base: string; overlays?: string[] }> };
  const bricks = (rig as unknown as { bricks: Bricks }).bricks;
  const scenario = model.scenario as Scenario;

  it("is sound against the rig: four groups of states, each waiting for the stall to be built but the build", () => {
    expect(checkScenario(scenario, rig, discoverAnchors(rig).anchors, requiresValues(parts), Object.keys(shape.textures ?? {}))).toEqual([]);
    const groups = scenario.states as { id: string; needs?: string[] }[];
    expect(groups.map((g) => g.id)).toEqual(["build", "heap", "front", "outlet"]);
    for (const g of groups.slice(1)) expect(g.needs).toEqual(["brickwork", "lining"]);
  });

  it("offers every brick type of the rig, in its order, opening at its default, and the charge's rock", () => {
    const [brick, ore] = scenario.textures!;
    expect(brick!.code).toBe(bricks.code);
    expect(brick!.default).toBe(bricks.default);
    expect(brick!.options.map((o) => o.id)).toEqual(Object.keys(bricks.types));
    for (const o of brick!.options) expect(o.path, o.id).toBe(bricks.types[o.id]!.overlays?.[0] ?? bricks.types[o.id]!.base);
    expect(ore!.code).toBe("ore");
    expect(ore!.options.find((o) => o.id === ore!.default)!.path).toBe(shape.textures!.ore);
    const view = buildModelView(shape, rig, scenario);
    const sulfur = withPickedTextures(view.textures, pickedTextures(scenario.textures!, { ore: "sulfur" })).find((t) => t.code === "ore")!;
    expect(sulfur).toMatchObject({ path: "game:item/resource/crushed/sulfur", colour: "#f2e0a4" });
  });
});
