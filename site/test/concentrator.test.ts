import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { cellBoxes, checkFittedBoxes } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, openingFitted, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { compileGlobs, fitted, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The concentrator's shipped files, and the poses Concentrator/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads θ, the axle's angle (the line shaft turns with it, the sheaves
// and the vanner's pulley ride it, the plunger, the deck and the vanner's frame slide with their sheaves, the
// countershaft turns 1:1), and ψ, its travel either way (the ratchet turns the worm shaft forward a tooth a
// stroke, so the worm and the vanner's rollers creep to the head whichever way the axle turns). Its cells
// carry per-level collision (`fitted`); its scenario has two selects, the level and the amalgamation plates,
// which wait for the jig, and the water pipes' metal. tools/tests/test_concentrator_model.py replays the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type Level = { level: number; tier: string; name: string; requires: string[] };
const rig = read("assets/seraphhorizons/config/concentrator-rig.json") as Rig & { levels: Level[] };
const shape = read("assets/seraphhorizons/shapes/block/concentrator.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Concentrator/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel = Math.abs(theta)): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the concentrator's rig against its rig-reference.json", () => {
  it("reads θ and its travel, has no work, and has a matrix for every part", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: true, depth: false, lifting: false });
    // only the ratchet's train reads the travel
    expect(parts.filter((p) => p.drivers?.some((d) => d.input === "travel")).map((p) => p.id)).toEqual(["vannerworm", "vannerhead", "vannerfoot"]);
    expect(workOf(rig)).toBeNull();
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    expect(reference.poses.some((p) => p.theta < 0)).toBe(true);
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("creeps the vanner's belt the same way whichever way the axle turns", () => {
    const i = parts.findIndex((p) => p.id === "vannerhead");
    const fwd = partMatrices(parts, pose(1.3), order, null)[i]!;
    const back = partMatrices(parts, pose(-1.3), order, null)[i]!;
    // the head roller's turn (about z) is the same; only the frame's shake, a sine, differs
    expect(Math.atan2(fwd[1]!, fwd[0]!)).toBeCloseTo(Math.atan2(back[1]!, back[0]!), 12);
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});

describe("the concentrator's levels in the viewer", () => {
  const model = manifest.models.find((m) => m.id === "concentrator")!;
  const view = buildModelView(shape, rig, model.scenario);
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(model.scenario!.states, values);
  const opening = openingFitted(values, model.scenario!.choices ?? [], groups);
  const ticked = (level: string, plates: string) => {
    const lv = groups[0]!.spec.options.find((o) => o.id === level)!;
    const pl = groups[1]!.spec.options.find((o) => o.id === plates)!;
    return applyState(applyState(opening.fitted, lv, groups[0]!.owns), pl, groups[1]!.owns);
  };
  const PIPE = "pipecopper";
  const shown = (level: string, plates: string) => {
    const t = ticked(level, plates);
    return new Set(view.parts.filter((p) => fitted(p.part.requires, t) && statesShow(p.part.requires, t, groups)).map((p) => p.part.requires ?? null));
  };

  it("has two selects, the level and the plates, opening at level 4 with the plates dressed", () => {
    expect(new Set(values)).toEqual(new Set(["sluice", "lineshaft", "jig", "table", "infeed", "vanner", "plates", "dressing", "pipecopper", "pipelead"]));
    expect(model.scenario!.choices).toEqual([{ label: "Water pipes", values: ["pipecopper", "pipelead"], default: "pipecopper" }]);
    expect(opening.fitted.pipecopper).toBe(true);
    expect(opening.fitted.pipelead).toBe(false);
    expect(groups.map((g) => g.spec.label)).toEqual(["Level", "Amalgamation plates"]);
    expect(groups[0]!.spec.options.map((o) => o.id)).toEqual(["frame", "level1", "level2", "level3", "level4"]);
    expect(opening.picked).toEqual({ level: "level4", plates: "dressed" });
    expect(checkScenario(model.scenario!, rig, view.anchors, values)).toEqual([]);
  });

  it("fits each level's set as the rig's levels say, the sluice only at level 1", () => {
    for (const lv of rig.levels) {
      const opt = groups[0]!.spec.options.find((o) => o.id === `level${lv.level}`)!;
      expect(opt.fitted).toEqual(lv.requires);
      expect(shown(`level${lv.level}`, "none")).toEqual(new Set([null, PIPE, ...lv.requires]));
    }
    expect(shown("frame", "none")).toEqual(new Set([null, PIPE]));
    expect(shown("level2", "none").has("sluice")).toBe(false);
  });

  it("draws the plates only once the jig is fitted, bare or dressed", () => {
    expect(shown("level1", "dressed").has("plates")).toBe(false);
    expect(shown("frame", "bare").has("plates")).toBe(false);
    expect(shown("level2", "bare")).toEqual(new Set([null, PIPE, "lineshaft", "jig", "plates"]));
    expect(shown("level4", "dressed")).toEqual(new Set([null, PIPE, "lineshaft", "jig", "table", "infeed", "vanner", "plates", "dressing"]));
  });

  it("collides with what is fitted: the frame's boxes always, each level's only while it is fitted", () => {
    expect(checkFittedBoxes(rig.cells, requiresValues(parts))).toEqual([]);
    const count = (set: Set<string>) => rig.cells!.reduce((n, c) => n + cellBoxes(c, set).length, 0);
    const frameOnly = count(new Set());
    const level = (id: string) => new Set(groups[0]!.spec.options.find((o) => o.id === id)!.fitted);
    expect(frameOnly).toBeGreaterThan(0);
    expect(count(level("level1"))).toBeGreaterThan(frameOnly);
    expect(count(level("level4"))).toBeGreaterThan(count(level("level3")));
    // the sluice's boxes are gone at level 2, and the vanner's absent before level 4
    const sluiceCells = rig.cells!.filter((c) => c.fitted?.sluice);
    expect(sluiceCells.length).toBeGreaterThan(0);
    for (const c of sluiceCells) expect(cellBoxes(c, level("level2")).length).toBeLessThan(cellBoxes(c, new Set(["sluice", ...level("level2")])).length);
    const vannerCells = rig.cells!.filter((c) => c.fitted?.vanner);
    for (const c of vannerCells) expect(cellBoxes(c, level("level3")).length).toBeLessThan(cellBoxes(c, level("level4")).length);
  });

  it("anchors the fixed points: one water inlet, the feed, the power, the outlets and the return water", () => {
    const keys = view.anchors.map((a) => a.key);
    for (const k of ["powerCell", "waterCell", "feedCell", "feedSpout", "waterSpout", "concentrate", "concentrateSide", "tailings", "tailingsSide", "returnWaterCell"])
      expect(keys).toContain(k);
    expect(keys.filter((k) => /^water/.test(k) && k.endsWith("Cell"))).toEqual(["waterCell"]);
  });
});
