import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, needsMet, openingFitted, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { textureColour } from "../src/lib/model-view.ts";
import { compileGlobs, fitted, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The arrastra's shipped files, and the poses Arrastra/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone (the axle's angle): the power shaft turns with it, the post at a third of it
// through the bevel pair, the arms and stones ride the post; everything else stands still. The machine is built in
// stages by requires on a placed frame, and the page fits them from a Build select; the water pipe waits on the pit,
// the pit's contents on the whole build. tools/tests/test_arrastra_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type Build = { stages: { requires: string }[] };
const rig = read("assets/seraphhorizons/config/arrastra-rig.json") as Rig & Record<string, unknown> & { build: Build };
const shape = read("assets/seraphhorizons/shapes/block/arrastra.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Arrastra/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "arrastra")!;
const parts = rig.parts!;
const order = rideOrder(parts);
const STAGES = ["pit", "post", "arms", "stones", "hopper"];

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel = Math.abs(theta)): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the arrastra's rig against its rig-reference.json", () => {
  it("reads the axle's angle and nothing else, over a three-turn cycle", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(thetaTurns(parts)).toBe(3);
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["shaft", null, null],
      ["pinion", "post", "shaft"],
      ["post", "post", null],
      ["arms", "arms", "post"],
      ["stones", "stones", "post"],
      ["step", "post", null],
      ["pit", "pit", null],
      ["hopper", "hopper", null],
      ["charge", "charge", null],
      ["paste", "paste", null],
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

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });

  it("lists the build's stages in order, each a requires value, and draws the contents apart from the pit's stone", () => {
    expect(rig.build.stages.map((s) => s.requires)).toEqual(STAGES);
    for (const s of STAGES) expect(requiresValues(parts)).toContain(s);
    const stone = textureColour("cobble", shape.textures!.cobble!);
    const charge = textureColour("charge", shape.textures!.charge!);
    const paste = textureColour("paste", shape.textures!.paste!);
    expect(new Set([stone, charge, paste]).size).toBe(3);
  });
});

describe("the arrastra's anchors: batch, by hand", () => {
  it("has the power and the water at the back, and the two hand actions at the front, over the controller", () => {
    const keys = discoverAnchors(rig).anchors.map((a) => a.key);
    expect(keys).toEqual(["powerCell", "waterCell", "loadCell", "drawCell"]);
    expect([rig.powerCell, rig.powerFace, rig.waterCell, rig.waterFace]).toEqual([[0, 1, 1], "south", [0, 0, 1], "south"]);
    expect([rig.loadCell, rig.loadFace, rig.drawCell, rig.drawFace]).toEqual([[0, 1, 0], "up", [0, 0, 0], "north"]);
    expect(Object.keys(rig).filter((k) => /infeed|output|chute|return/i.test(k))).toEqual([]);
    expect(Object.keys(shape.textures!).filter((k) => /chute/.test(k + shape.textures![k]))).toEqual([]);
    // the ground layer's six cells and the middle column's upper two: nothing of the machine is over the pit's corners
    expect(rig.cells!.map((c) => c.pos.join(",")).sort()).toEqual(
      ["-1,0,0", "-1,0,1", "0,0,0", "0,0,1", "1,0,0", "1,0,1", "0,1,0", "0,1,1"].sort(),
    );
    expect(rig.cells!.every((c) => (c.boxes ?? []).length > 0 && !c.hollow && c.lid === undefined)).toBe(true);
  });
});

describe("the arrastra's page", () => {
  const requires = requiresValues(parts);
  const groups = stateGroups(model.scenario!.states, requires);
  const build = groups[0]!;

  it("is a valid scenario with three selects: the build, the water pipe and what is in the pit", () => {
    expect(checkScenario(model.scenario!, rig, discoverAnchors(rig).anchors, requires)).toEqual([]);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([
      ["build", STAGES],
      ["pipe", ["pipecopper", "pipelead"]],
      ["contents", ["charge", "paste"]],
    ]);
  });

  it("builds stage by stage, in the rig's order, the frame alone first and the whole machine last", () => {
    const options = build.spec.options;
    expect(options.map((o) => o.fitted)).toEqual(STAGES.map((_, i) => STAGES.slice(0, i)).concat([STAGES]));
    expect(build.spec.default).toBe(options.at(-1)!.id);
    const { fitted: opening, picked } = openingFitted(requires, [], groups);
    expect(picked).toEqual({ build: "complete", pipe: "pipecopper", contents: "charge" });
    // the frame alone: the head frame and the turning power shaft; the pipe waits on the pit, the contents on the build
    const bare = applyState(opening, options[0]!, build.owns);
    const shown = parts.filter((p) => fitted(p.requires, bare) && statesShow(p.requires, bare, groups)).map((p) => p.id);
    expect(shown).toEqual(["shaft", "frame"]);
    expect(needsMet(groups[1]!, bare)).toBe(false);
    const pit = applyState(opening, options[1]!, build.owns);
    expect(parts.filter((p) => fitted(p.requires, pit) && statesShow(p.requires, pit, groups)).map((p) => p.id)).toEqual(["shaft", "pit", "pipecopper", "frame"]);
    expect(needsMet(groups[2]!, pit)).toBe(false);
    const all = parts.filter((p) => fitted(p.requires, opening) && statesShow(p.requires, opening, groups)).map((p) => p.id);
    expect(all).toEqual(parts.map((p) => p.id).filter((id) => id !== "paste" && id !== "pipelead"));
  });
});
