import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Manifest } from "../src/lib/model-manifest.ts";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { checkScenario, initialFitted } from "../src/lib/model-scenario.ts";
import { compileGlobs, fitted, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The ball mill's shipped files, and the poses BallMill/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone (the axle's angle): the pinion turns with it, the drum (and the screens and
// the girth wheel riding it) a third of it the other way; everything else stands still. Every fitted part has its
// own requires; the water pipe is drawn in the metal chosen. tools/tests/test_ballmill_model.py and the mod's C#
// tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/ballmill-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/ballmill.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/BallMill/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as Manifest;
const model = manifest.models.find((m) => m.id === "ball-mill")!;
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel = Math.abs(theta)): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);

describe("the ball mill's rig against its rig-reference.json", () => {
  it("reads the axle's angle and nothing else, over a three-turn cycle", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(thetaTurns(parts)).toBe(3);
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["pinion", "pinion", null],
      ["drum", "drum", null],
      ["girth", "girth", "drum"],
      ["screens", "screens", "drum"],
      ["balls", "balls", null],
      ["feedbox", "feedbox", null],
      ["hopper", "hopper", null],
      ["pipecopper", "pipecopper", null],
      ["pipelead", "pipelead", null],
      ["frame", null, null],
    ]);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta, p.travel), order, null);
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

  it("turns the drum a third of the axle the other way about its own axis, and the pinion with the axle about its shaft", () => {
    const i = (id: string) => parts.findIndex((p) => p.id === id);
    const drumAxis = [0, 20 / 16, 1];
    for (const theta of [0.4, 2.2, -1.3, 7.9]) {
      const ms = partMatrices(parts, pose(theta), order, null);
      const drum = ms[i("drum")]!;
      // its axis stays put, and a point a block above it turns by -θ/3 about +x
      expect(apply(drum, drumAxis).map((v) => Math.round(v * 1e9) / 1e9)).toEqual(drumAxis);
      const p = apply(drum, [0, drumAxis[1]! + 1, 1]);
      expect(p[1]! - drumAxis[1]!).toBeCloseTo(Math.cos(-theta / 3), 5);
      expect(p[2]! - 1).toBeCloseTo(Math.sin(-theta / 3), 5);
      expect(ms[i("girth")]).toEqual(drum);
      expect(ms[i("screens")]).toEqual(drum);
      const pinion = ms[i("pinion")]!;
      expect(apply(pinion, [1, 0.5, 0.5]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([1, 0.5, 0.5]);
      for (const still of ["balls", "feedbox", "hopper", "pipecopper", "pipelead", "frame"])
        expect(ms[i(still)]!.every((v, k) => Math.abs(v - (k % 5 === 0 ? 1 : 0)) < 1e-12)).toBe(true);
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

describe("the ball mill's page", () => {
  const requires = requiresValues(parts);

  it("passes the scenario's checks and draws one water pipe, copper at first", () => {
    expect(checkScenario(model.scenario!, rig, discoverAnchors(rig).anchors, requires)).toEqual([]);
    const opening = initialFitted(requires, model.scenario!.choices);
    const shown = parts.filter((p) => fitted(p.requires, opening)).map((p) => p.id);
    expect(shown).toEqual(["pinion", "drum", "girth", "screens", "balls", "feedbox", "hopper", "pipecopper", "frame"]);
    for (const r of requires) expect(model.scenario!.requires![r]).toBeTruthy();
  });

  it("puts its anchors on the footprint: the drive low at the east end, ore and water in at the west, the pulp out low at the back", () => {
    const keys = discoverAnchors(rig).anchors.map((a) => a.key);
    expect(keys).toEqual(["powerCell", "infeedCell", "waterCell", "outputCell", "output"]);
    expect([rig.powerCell, rig.powerFace, rig.infeedCell, rig.infeedFace, rig.waterCell, rig.waterFace, rig.outputCell, rig.outputFace]).toEqual([
      [1, 0, 0], "east", [-1, 1, 0], "west", [-1, 1, 1], "west", [0, 0, 1], "south",
    ]);
    expect(Object.keys(rig).filter((k) => k.startsWith("return"))).toEqual([]);
    expect(rig.cells!.length).toBe(12);
    expect(rig.cells!.every((c) => (c.boxes ?? []).length > 0)).toBe(true);
    expect(rig.cells!.filter((c) => c.lid !== undefined).map((c) => c.pos[1])).toEqual([1, 1, 1, 1, 1, 1]);
  });
});
