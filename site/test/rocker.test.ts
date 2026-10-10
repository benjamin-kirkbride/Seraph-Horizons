import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { checkScenario, stateGroups, type Scenario } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rigInputs, rideOrder, textureCodes, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The rocker's shipped files, and the poses Rocker/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone, the rocking (the hold-to-work clock): the cradle swings about its
// rocking axis and slides as its rockers roll, everything else that rocks rides it, and the sills stand still.
// It takes water by bucket only: no water cell, no spout. tools/tests/test_rocker_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/rocker-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/rocker.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/Rocker/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: { id: string; scenario?: Scenario }[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const at = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the rocker's rig against its rig-reference.json", () => {
  it("reads θ alone, one rock a turn, and has a matrix for every part", () => {
    expect(workOf(rig)).toBeNull();
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows θ (so Play) and nothing else; a hand station: no power cell
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(thetaTurns(parts)).toBe(1);
    expect((rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, at(p.theta), order, null);
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

  it("rocks everything that rides the cradle with it and leaves the sills still", () => {
    const ms = partMatrices(parts, at(1.1), order, null);
    const cradle = ms[parts.findIndex((p) => p.id === "cradle")]!;
    parts.forEach((p, i) => {
      if (p.ride === "cradle") ms[i]!.forEach((v, j) => expect(Math.abs(v - cradle[j]!)).toBeLessThan(1e-12));
      if (p.ride == null && p.id !== "cradle") ms[i]!.forEach((v, j) => expect(Math.abs(v - (j % 5 === 0 ? 1 : 0))).toBeLessThan(1e-12));
    });
    expect(parts.filter((p) => p.ride == null).map((p) => p.id)).toEqual(["cradle", "frame"]);
  });

  it("gives every element a part and every part an element, each in its own textures", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const code of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(code);
    expect([...codes.riddle!]).toEqual(["riddle"]);
    expect([...codes.water!]).toEqual(["water"]);
    expect([...codes.charge!]).toEqual(["charge"]);
    expect([...codes.concentrate!]).toEqual(["concentrate"]);
  });
});

describe("the rocker's anchors and scenario", () => {
  it("names the tailings beyond the foot and the points that ride the cradle, and no water cell", () => {
    const { anchors, unrecognised } = discoverAnchors(rig);
    expect(unrecognised).toEqual(["rock"]);
    expect(anchors.filter((a) => a.kind === "cell")).toEqual([]);
    expect(anchors.find((a) => a.key === "tailings")).toMatchObject({ kind: "point", pos: [-0.5, 0, 0.5], side: "west" });
    for (const key of ["hopper", "outflow", "concentrate"]) expect(anchors.find((a) => a.key === key)).toMatchObject({ kind: "point", part: "cradle" });
    expect(anchors.find((a) => a.key === "spout")).toBeUndefined();
  });

  it("has a water state (wet, opened on, as Play rocks it) and a load state, and fits the rig", () => {
    const scenario = manifest.models.find((m) => m.id === "rocker")!.scenario!;
    const requires = requiresValues(parts);
    expect(checkScenario(scenario, rig, discoverAnchors(rig).anchors, requires)).toEqual([]);
    const groups = stateGroups(scenario.states, requires);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([
      ["water", ["water"]],
      ["load", ["charge", "concentrate"]],
    ]);
    const water = groups[0]!.spec as { default?: string; options: { id: string; fitted: string[] }[] };
    expect(water.options.map((o) => [o.id, o.fitted])).toEqual([
      ["dry", []],
      ["wet", ["water"]],
    ]);
    expect(water.default).toBe("wet");
    // the fitted parts stay checkboxes of their own, outside the states
    expect(requires.filter((r) => !groups.some((g) => g.owns.includes(r)))).toEqual(["riffles", "apron", "hopper", "riddle"]);
  });
});
