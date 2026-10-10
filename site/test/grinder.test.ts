import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { Manifest } from "../src/lib/model-manifest.ts";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { applyState, checkScenario, openingFitted, stateGroups, type FittedStates } from "../src/lib/model-scenario.ts";
import { compileGlobs, fitted, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The grinder's shipped files, and the poses Grinder/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone (the axle's angle); every part turns with it at its gearing's ratio or
// stands still. The model holds both levels' parts; the manifest's states fit the frame alone or one level, level 2
// having retired level 1's arrastra. tools/tests/test_grinder_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type Level = { level: number; tier: string; name: string; fitted: string[]; retires?: string[] };
const rig = read("assets/seraphhorizons/config/grinder-rig.json") as Rig & { levels: Level[] };
const shape = read("assets/seraphhorizons/shapes/block/grinder.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Grinder/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as Manifest;
const model = manifest.models.find((m) => m.id === "grinder")!;
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel = Math.abs(theta)): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the grinder's rig against its rig-reference.json", () => {
  it("reads the axle's angle and nothing else, over a three-turn cycle", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(rig.work).toBeUndefined();
    expect(rig.trunkPath).toBeUndefined();
    expect(thetaTurns(parts)).toBe(3);
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

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});

describe("the grinder's levels and the viewer's states", () => {
  const requires = requiresValues(parts);
  const groups = model.scenario!.states as FittedStates[];
  const level = groups[0]!;

  it("lists each level's fitted parts once, water then mill, and level 2 retires level 1's", () => {
    expect(rig.levels.map((l) => [l.level, l.tier])).toEqual([[1, "water"], [2, "mill"]]);
    const all = rig.levels.flatMap((l) => l.fitted);
    expect(new Set(all).size).toBe(all.length);
    expect(new Set(all)).toEqual(new Set(requires));
    for (const l of rig.levels) for (const r of l.fitted) expect(r.startsWith(`l${l.level}`)).toBe(true);
    expect(rig.levels[1]!.retires).toEqual(rig.levels[0]!.fitted);
  });

  it("has a state for the frame alone and one for each level, fitting exactly that level's set", () => {
    expect(groups.length).toBe(1);
    expect(level.options.map((o) => o.id)).toEqual(["frame", "l1", "l2"]);
    expect(level.options[0]!.fitted).toEqual([]);
    rig.levels.forEach((l, i) => expect([...level.options[i + 1]!.fitted].sort()).toEqual([...l.fitted].sort()));
    expect(checkScenario(model.scenario!, rig, discoverAnchors(rig).anchors, requires)).toEqual([]);
  });

  it("draws in each state the frame's parts and only that level's", () => {
    const sg = stateGroups(model.scenario!.states!, requires);
    const { fitted: opening, picked } = openingFitted(requires, [], sg);
    expect(picked).toEqual({ level: "l1" });
    for (const [i, option] of level.options.entries()) {
      const ticked = applyState(opening, option, sg[0]!.owns);
      const shown = parts.filter((p) => fitted(p.requires, ticked)).map((p) => p.id);
      const lv = i === 0 ? null : `l${i}`;
      expect(shown.filter((id) => !/^l\d/.test(id)).sort()).toEqual(["frame", "shaft"]);
      expect(shown.every((id) => !/^l\d/.test(id) || id.startsWith(lv ?? "-"))).toBe(true);
      if (lv) expect(shown.filter((id) => id.startsWith(lv)).length).toBe(parts.filter((p) => p.id.startsWith(lv)).length);
    }
  });

  it("puts its anchors on the footprint: the power high on the south, ore and water in by the hopper, out low on the east", () => {
    const keys = discoverAnchors(rig).anchors.map((a) => a.key);
    expect(keys).toEqual(["powerCell", "infeedCell", "waterCell", "outputCell", "output"]);
    const r = rig as unknown as Record<string, unknown>;
    expect([r.powerCell, r.powerFace, r.infeedFace, r.waterFace, r.outputFace]).toEqual([[0, 1, 1], "south", "up", "north", "east"]);
    expect(Object.keys(r).filter((k) => k.startsWith("return"))).toEqual([]);
    expect(rig.cells!.length).toBe(10);
    expect(rig.cells!.some((c) => c.pos[1] === 1 && c.pos[2] === 0 && c.pos[0] !== 0)).toBe(false);
    expect(rig.cells!.some((c) => c.lid !== undefined)).toBe(false);
  });

  it("collides with the frame and what is fitted, never one level's parts in another's", () => {
    for (const c of rig.cells!) for (const k of Object.keys(c.fitted ?? {})) expect(requires).toContain(k);
    const keys = new Set(rig.cells!.flatMap((c) => Object.keys(c.fitted ?? {})));
    expect(keys.has("l1stones") && keys.has("l2drum")).toBe(true);
    expect(rig.cells!.filter((c) => c.pos[1] === 0).every((c) => (c.boxes ?? []).length > 0)).toBe(true);
  });
});
