import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, openingFitted, stateGroups } from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { fitted, partMatrices, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The roaster's shipped files, and the poses Roaster/tools/make_shape.py computes from them with machinegen's
// reference maths. Its entry shaft reads θ, and everything beyond the gear case's rectifier ψ, the axle's travel (the
// line shaft and the feed roll on its end, the rabbles through their bevel pairs); its scenario's states are the frame and levels 1 and 2, level 2 keeping level 1's hopper and pulling
// down the rest. tools/tests/test_roaster_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/roaster-rig.json") as Rig & { levels: Record<string, string[]>; contents: Record<string, string[]> };
const shape = read("assets/seraphhorizons/shapes/block/roaster.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Roaster/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "roaster")!;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the roaster's rig against its rig-reference.json", () => {
  it("reads θ at the entry and ψ beyond the rectifier, and has a matrix for every part", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, travel: true, depth: false, lifting: false });
    expect(thetaTurns(parts)).toBe(1);
    expect(rig.work).toBeUndefined();
    expect(rig.trunkPath).toBeUndefined();
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("turns the rabbles and the feed roll forward whichever way the axle turns", () => {
    const rectified = ["lineshaft", "rabble1", "rabble2", "feedroll"];
    for (const th of [0.6, 2.4, 9]) {
      const fwd = partMatrices(parts, { ...pose(th), travel: th }, order, null);
      const back = partMatrices(parts, { ...pose(-th), travel: th }, order, null);
      parts.forEach((q, i) => {
        const same = fwd[i]!.every((v, k) => Math.abs(v - back[i]![k]!) < 1e-9);
        expect([q.id, same]).toEqual([q.id, q.id === "entry" ? false : (q.drivers ?? []).length === 0 || rectified.includes(q.id)]);
      });
    }
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, pose(p.theta), order, null);
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
});

describe("the roaster's levels", () => {
  const view = buildModelView(shape, rig, model.scenario);
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(model.scenario!.states, values);
  const opening = openingFitted(values, [], groups);
  const ticked = (id: string) => {
    const g = groups[0]!;
    return applyState(opening.fitted, g.spec.options.find((o) => o.id === id)!, g.owns);
  };
  const shown = (id: string) => {
    const t = ticked(id);
    return view.parts.filter((p) => fitted(p.part.requires, t)).map((p) => p.part.id);
  };

  it("lists the requires values level by level and opens at level 2; the scenario fits the rig", () => {
    expect(values).toEqual(["stalls", "stallflue", "trestle", "heaps", "hopper", "hearth", "walls", "arch", "ironwork", "firebox", "drive", "rabbles", "feeder", "charge", "fire"]);
    expect(groups.map((g) => [g.spec.label, g.spec.default])).toEqual([["Level", "level2"]]);
    expect(opening.picked).toEqual({ states: "level2" });
    expect(checkScenario(model.scenario!, rig, view.anchors, values)).toEqual([]);
  });

  it("names the rig's levels with what each shows while roasting", () => {
    const options = Object.fromEntries(groups[0]!.spec.options.map((o) => [o.id, o.fitted]));
    expect(Object.keys(options)).toEqual(["frame", "level1", "level2"]);
    expect(options.frame).toEqual([]);
    for (const k of ["1", "2"]) expect(options[`level${k}`]).toEqual([...rig.levels[k]!, ...rig.contents[k]!]);
  });

  it("shows the frame alone, then the stalls under the hopper, then the furnace under the same hopper", () => {
    expect(shown("frame")).toEqual(["frame"]);
    expect(shown("level1")).toEqual(["stalls", "stallflue", "trestle", "heaps", "hopper", "frame"]);
    expect(shown("level2")).toEqual(["hopper", "hearth", "walls", "arch", "ironwork", "firebox", "entry", "gearcase", "lineshaft", "pedestals", "rabble1", "rabble2", "rabblemounts", "feedroll", "feeder", "charge", "fire", "frame"]);
  });

  it("collides with what each level fits: the frame's boxes always, a part's only while it is fitted", () => {
    const keys = new Set(rig.cells!.flatMap((c) => Object.keys(c.fitted ?? {})));
    expect([...keys].every((k) => values.includes(k))).toBe(true);
    // the middle stall's floor (build frame x 21, y 9, z 34: the shipped cell [0, 0, 2]) is open with level 1's parts
    // (its heaps aside, which are its contents), and inside the furnace's hearth at level 2
    const cell = rig.cells!.find((c) => c.pos.join() === "0,0,2")!;
    const boxesAt = (on: Readonly<Record<string, boolean>>) => [...(cell.boxes ?? []), ...Object.entries(cell.fitted ?? {}).filter(([k]) => on[k] === true).flatMap(([, b]) => b)];
    const at = [(21 - 16) / 16, 9 / 16, (34 - 32) / 16];
    const inside = (bs: number[][]) => bs.some((b) => at.every((v, k) => b[k]! < v && v < b[k + 3]!));
    const parts1 = Object.fromEntries(rig.levels["1"]!.map((v) => [v, true]));
    expect(inside(boxesAt(parts1))).toBe(false);
    expect(inside(boxesAt(ticked("level1")))).toBe(true);
    expect(inside(boxesAt(ticked("level2")))).toBe(true);
    expect(Object.keys(cell.fitted ?? {})).toEqual(expect.arrayContaining(["stalls", "hearth"]));
  });

  it("draws the power, infeed, output and fuel cells as the frame's anchors", () => {
    const keys = view.anchors.map((a) => a.key);
    for (const k of ["powerCell", "infeedCell", "outputCell", "fuelCell", "infeed", "output", "fuel", "sulfur", "smoke"]) expect(keys).toContain(k);
    const cell = (k: string) => view.anchors.find((a) => a.key === k) as { pos: number[]; face?: string };
    expect([cell("powerCell").pos, cell("powerCell").face]).toEqual([[0, 2, 0], "north"]);
    expect([cell("infeedCell").pos, cell("infeedCell").face]).toEqual([[0, 2, 4], "up"]);
    expect([cell("outputCell").pos, cell("outputCell").face]).toEqual([[1, 0, 2], "east"]);
    expect([cell("fuelCell").pos, cell("fuelCell").face]).toEqual([[-1, 1, 0], "up"]);
  });
});
