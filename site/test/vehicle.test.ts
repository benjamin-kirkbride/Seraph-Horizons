import { describe, expect, it } from "vitest";
import { checkManifest, checkModelFiles, type ManifestModel } from "../src/lib/model-manifest.ts";
import { checkScenario, choiceOf, defaultInputSpeed, initialFitted, pickChoice, type RequiresChoice, type Scenario } from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import {
  BOGIE_PART,
  blocksPerSecondAt,
  checkVehicle,
  rolledBy,
  trackLayout,
  trackScroll,
  turnsPerSecondAt,
  vehicleOf,
  withBogies,
  type VehicleSpec,
} from "../src/lib/model-vehicle.ts";
import { MAX_CYCLE_TURNS, thetaTurns, wrappedDelta, type Rig, type RigPart, type Shape } from "../src/lib/rig.ts";

// A small vehicle: a body along x, front towards −x, its axle turning with θ and a beam rocking once in
// two turns; a bogie shape of one box, placed at two places read from the rig.
const box = (name: string, from: [number, number, number], to: [number, number, number]) => ({ name, from, to, faces: { up: { texture: "#wood" } } });
const shape: Shape = { textures: { wood: "game:block/wood" }, elements: [box("axle", [0, 0, 0], [4, 4, 4]), box("beam", [0, 8, 0], [16, 9, 2]), box("body", [-8, 4, 0], [40, 6, 32])] };
const bogie: Shape = { textures: { wood: "ignored", iron: "game:block/iron" }, elements: [box("journal", [6, 3, 0], [10, 7, 2])] };
const parts: RigPart[] = [
  { id: "axle", match: ["axle"], drivers: [{ type: "rotate", axis: "z", pivot: [0.125, 0.125, 0.125], ratio: 1 }] },
  { id: "beam", match: ["beam"], drivers: [{ type: "swing", axis: "z", pivot: [0.5, 0.5, 0], amplitude: 0.3, ratio: 0.5 }] },
  { id: "lever_l", match: ["lever_l"], requires: "left", drivers: [] },
  { id: "lever_r", match: ["lever_r"], requires: "right", drivers: [] },
  { id: "frame", match: ["*"], drivers: [] },
];
const rig: Rig = { parts, wheel: { radius: 0.25 }, bogies: { offset: -0.5, front: 0, rear: 2 } } as Rig;
const spec: VehicleSpec = {
  wheelRadius: "wheel.radius",
  front: "-x",
  cycle: "stroke",
  speed: 3,
  track: { gauge: 1.5, centre: 1 },
  bogies: { at: ["bogies.front", "bogies.rear"], offset: "bogies.offset" },
};

describe("θ's cycle", () => {
  it("is the least whole turns that bring every θ-reading part round", () => {
    expect(thetaTurns(parts)).toBe(2);
    expect(thetaTurns([{ id: "a", match: ["*"], drivers: [{ type: "swing", axis: "z", ratio: -1 / 3 }] }])).toBe(3);
    expect(thetaTurns([{ id: "a", match: ["*"], drivers: [{ type: "rotate", axis: "z", ratio: -0.33333333 }] }])).toBe(3);
    // ψ and the depth are not θ
    expect(thetaTurns([{ id: "a", match: ["*"], drivers: [{ type: "rotate", axis: "z", ratio: 0.5, rectified: true }] }])).toBe(1);
  });

  it("is one turn when no short cycle exists (a gear train of odd teeth looks the same at each turn)", () => {
    expect(thetaTurns([{ id: "a", match: ["*"], drivers: [{ type: "rotate", axis: "z", ratio: 19 / 13 }] }])).toBe(1);
    expect(MAX_CYCLE_TURNS).toBeLessThan(13);
  });

  it("is stepped round the short way, on a circle of the cycle", () => {
    const period = 4 * Math.PI;
    expect(wrappedDelta(0.1, period - 0.1, period)).toBeCloseTo(-0.2);
    expect(wrappedDelta(1, 1.5 * Math.PI, period)).toBeCloseTo(1.5 * Math.PI - 1); // over half a turn, under half the cycle
    expect(wrappedDelta(1, 3 * Math.PI, period)).toBeCloseTo(-Math.PI - 1);
    expect(wrappedDelta(9 * Math.PI, Math.PI, period)).toBeCloseTo(0);
  });
});

describe("a vehicle", () => {
  const v = vehicleOf(spec, rig)!;

  it("reads its rig paths", () => {
    expect(v).toMatchObject({ wheelRadius: 0.25, axis: "x", frontSign: -1, cycle: "stroke", speed: 3 });
    expect(v.bogies).toEqual([
      { name: "front", at: -0.5 },
      { name: "rear", at: 1.5 },
    ]);
    expect(vehicleOf(undefined, rig)).toBeNull();
    expect(() => vehicleOf({ ...spec, wheelRadius: "wheel.diameter" }, rig)).toThrow(/wheel.diameter/);
  });

  it("rolls by θ times the wheel's radius, and turns its speed into axle turns", () => {
    expect(rolledBy(2 * Math.PI, v)).toBeCloseTo(Math.PI / 2);
    expect(rolledBy(-Math.PI, v)).toBeCloseTo(-Math.PI / 4);
    expect(turnsPerSecondAt(blocksPerSecondAt(1.7, v), v)).toBeCloseTo(1.7);
    expect(defaultInputSpeed({ secondsPerTurn: 0.5, phases: [] }, turnsPerSecondAt(3, v))).toBeCloseTo(3 / (Math.PI / 2), 2);
    expect(defaultInputSpeed({ secondsPerTurn: 0.5, phases: [] }, null)).toBe(2);
  });

  it("adds a copy of the bogie shape at each place, in a static part of its own ahead of the rig's", () => {
    const merged = withBogies(shape, rig, bogie, v);
    const added = merged.shape.elements.slice(shape.elements.length);
    expect(added.map((e) => e.name)).toEqual(["bogie_front_journal", "bogie_rear_journal"]);
    expect(added.map((e) => [e.from[0], e.to[0]])).toEqual([
      [6 - 8, 10 - 8],
      [6 + 24, 10 + 24],
    ]);
    expect(added[0]!.from.slice(1)).toEqual([3, 0]);
    // the body's textures win
    expect(merged.shape.textures).toEqual({ wood: "game:block/wood", iron: "game:block/iron" });
    const view = buildModelView(merged.shape, merged.rig);
    expect(view.parts[0]!.id).toBe(BOGIE_PART);
    expect(view.parts[0]!.elements.map((i) => view.flat[i]!.element.name)).toEqual(["bogie_front_journal", "bogie_rear_journal"]);
    expect(view.parts[0]!.moving).toBe(false);
    expect(view.parts.find((p) => p.id === "frame")!.elements.map((i) => view.flat[i]!.element.name)).toEqual(["body"]);
    // nothing to add without a bogie shape or places
    expect(withBogies(shape, rig, null, v).shape).toBe(shape);
    expect(withBogies(shape, rig, bogie, { ...v, bogies: [] }).rig).toBe(rig);
  });

  it("lays a track under it a gauge apart, with sleepers that scroll backwards as it rolls forwards", () => {
    const bounds = { lo: [-1, 0, 0] as [number, number, number], hi: [3, 1, 2] as [number, number, number] };
    const t = trackLayout(spec.track!, v, bounds);
    expect(t.rails.map((r) => r.centre[2])).toEqual([0.25, 1.75]);
    for (const r of t.rails) {
      expect(r.centre[1] + r.size[1] / 2).toBeCloseTo(0); // the rails' top
      expect(r.centre[0] - r.size[0] / 2).toBe(-6);
      expect(r.centre[0] + r.size[0] / 2).toBe(8);
    }
    const xs = t.sleepers.map((s) => s.centre[0]);
    expect(Math.min(...xs)).toBeLessThan(-6);
    expect(Math.max(...xs)).toBeGreaterThan(8);
    expect(xs[1]! - xs[0]!).toBeCloseTo(0.5);
    expect(t.sleepers[0]!.size[2]).toBeCloseTo(2.25);
    expect(t.sleepers[0]!.centre[1]).toBeLessThan(0);
    // the front is −x, so the track goes by towards +x as it rolls forwards, and back the other way
    expect(trackScroll(0.2, 0.5, v)).toBeCloseTo(0.2);
    expect(trackScroll(1.2, 0.5, v)).toBeCloseTo(0.2);
    expect(trackScroll(-0.2, 0.5, v)).toBeCloseTo(0.3);
    expect(trackScroll(0.2, 0.5, { ...v, frontSign: 1 })).toBeCloseTo(-0.2);
    // along z, the track runs along z
    const alongZ = trackLayout(spec.track!, { ...v, axis: "z" }, bounds);
    expect(alongZ.rails.map((r) => r.centre[0])).toEqual([0.25, 1.75]);
    expect(alongZ.axis).toBe("z");
  });

  it("is checked against its rig and the manifest's bogie shape", () => {
    expect(checkVehicle(spec, rig, true)).toEqual([]);
    expect(checkVehicle(spec, rig, false)).toEqual(["vehicle.bogies needs the model's bogie shape"]);
    expect(checkVehicle({ ...spec, bogies: undefined }, rig, true)).toEqual(["the model's bogie shape needs vehicle.bogies to place it"]);
    const bad = checkVehicle({ ...spec, front: "up" as never, wheelRadius: -1, speed: 0, track: { gauge: 0, centre: 1 }, bogies: { at: ["bogies.middle"] } }, rig, true);
    expect(bad).toEqual([
      "vehicle.front must be one of -x, +x, -z, +z",
      "vehicle.wheelRadius must be above 0",
      "vehicle.speed must be above 0",
      "vehicle.track.gauge must be above 0",
      'vehicle.bogies: rig has no number at "bogies.middle"',
    ]);
    expect(checkVehicle(spec, { ...rig, parts: [{ id: "frame", match: ["*"], drivers: [] }] }, true)).toContain("vehicle needs a rig whose drivers read θ, the axle's angle");
  });

  it("is published with its bogie shape, which no body element may be named like", () => {
    const model: ManifestModel = { id: "car", title: "Car", description: "d", credit: "c", shape: "s.json", rig: "r.json", bogie: "b.json", scenario: { vehicle: spec } };
    expect(checkManifest({ models: [model] }).models).toHaveLength(1);
    expect(() => checkManifest({ models: [{ ...model, scenario: {} }] })).toThrow(/a bogie shape needs the scenario's vehicle/);
    expect(checkModelFiles(model, shape, rig, bogie)).toEqual({ elements: 3, parts: parts.length });
    const clash = { ...shape, elements: [...shape.elements, box("bogie_spare", [0, 0, 0], [1, 1, 1])] };
    expect(() => checkModelFiles(model, clash, rig, bogie)).toThrow(/bogie_spare would be taken for the bogies/);
    expect(() => checkModelFiles(model, shape, rig, { elements: "none" })).toThrow(/b.json has no "elements" array/);
  });
});

describe("a choice of requires values", () => {
  const lever: RequiresChoice = { label: "Lever", values: ["left", "right"], default: "right" };

  it("fits only its default at first, and one value at a time", () => {
    expect(initialFitted(["left", "right", "chest"], [lever])).toEqual({ left: false, right: true, chest: true });
    expect(initialFitted(["left", "right"], [{ ...lever, default: undefined }])).toEqual({ left: true, right: false });
    expect(pickChoice({ left: false, right: true, chest: false }, lever, "left")).toEqual({ left: true, right: false, chest: false });
    expect(choiceOf([lever], "right")).toBe(lever);
    expect(choiceOf([lever], "chest")).toBeNull();
  });

  it("is checked against the rig's requires values", () => {
    const s: Scenario = { choices: [lever, { label: "Again", values: ["right", "up"], default: "down" }, { label: "One", values: ["left"] }], requiresClass: { left: "thin" } };
    expect(checkScenario(s, rig, [], ["left", "right"])).toEqual([
      'choice "Lever": "left" belongs to a class (requiresClass), so it cannot be chosen',
      'choice "Again": "right" is in another choice too',
      'choice "Again": "up" is no part\'s requires value',
      'choice "Again": default "down" is not one of its values',
      'choice "One" needs two values or more',
      'choice "One": "left" is in another choice too',
      'choice "One": "left" belongs to a class (requiresClass), so it cannot be chosen',
    ]);
  });
});
