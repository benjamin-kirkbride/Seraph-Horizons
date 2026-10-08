import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { checkModelFiles, type ManifestModel } from "../src/lib/model-manifest.ts";
import { initialFitted } from "../src/lib/model-scenario.ts";
import { rolledBy, vehicleOf, withBogies } from "../src/lib/model-vehicle.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The handcar's shipped files, and the poses Handcar/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads one input, θ, the axle's angle: the wheels turn by it,
// the countershaft by −θ/3, and the walking beam and its pitman by their linkage, written as
// harmonics of the crank's angle. tools/tests/test_handcar_model.py and the mod's C# tests replay
// the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/handcar-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/entity/handcar.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/Handcar/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: 0, work: 0, size: 0, presence: 0, feed: 0, oil: 1 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);

describe("the handcar's rig against its rig-reference.json", () => {
  it("reads θ alone, and fits one branch lever of three", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(parts.filter((p) => p.requires).map((p) => p.requires)).toEqual(["left", "straight", "right"]);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
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

  it("comes back to the same pose after a stroke, three turns of the axle", () => {
    const cycle = rig.cycle as { axleTurns: number };
    const a = partMatrices(parts, pose(0.4), order, null);
    const b = partMatrices(parts, pose(0.4 + 2 * Math.PI * cycle.axleTurns), order, null);
    a.forEach((m, i) => m.forEach((v, k) => expect(Math.abs(v - b[i]![k]!), parts[i]!.id).toBeLessThan(1e-7)));
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });
});

describe("the handcar's anchors", () => {
  it("shows the seats and the cargo where they stand, and the grips riding the beam", () => {
    const { anchors, unrecognised } = discoverAnchors(rig);
    const points = Object.fromEntries(anchors.filter((a) => a.kind === "point").map((a) => [a.key, a]));
    expect(Object.keys(points).sort()).toEqual(["cargo", "gripFrontL", "gripFrontR", "gripRearL", "gripRearR", "seatFront", "seatRear"]);
    for (const k of ["gripFrontL", "gripFrontR", "gripRearL", "gripRearR"]) expect(points[k]).toMatchObject({ part: "beam" });
    for (const k of ["seatFront", "seatRear", "cargo"]) expect(points[k]!).not.toHaveProperty("part");
    // the cycle, the riders' animations and Yang's bogies are figures, not places
    expect(unrecognised.sort()).toEqual(["bogies", "cycle", "riders"]);
  });

  it("moves each grip with the beam: the handles go up and down by the beam's swing", () => {
    const beam = parts.findIndex((p) => p.id === "beam");
    const grip = (rig.gripRearR as { pos: number[] }).pos;
    const heights = Array.from({ length: 36 }, (_, i) => apply(partMatrices(parts, pose((i / 36) * 6 * Math.PI), order, null)[beam]!, grip)[1]!);
    const travel = (Math.max(...heights) - Math.min(...heights)) * 16;
    expect(travel).toBeGreaterThan(5);
    expect(travel).toBeLessThan(9);
  });

  it("is a vehicle: its slider spans a stroke, it rolls by its wheels, and its bogies are drawn at the axles", () => {
    const model = manifest.models.find((m) => m.id === "handcar")!;
    const cycle = rig.cycle as { axleTurns: number; wheelRadius: number; distancePerCycle: number };
    expect(thetaTurns(parts)).toBe(cycle.axleTurns);
    const v = vehicleOf(model.scenario!.vehicle, rig)!;
    expect(rolledBy(2 * Math.PI * cycle.axleTurns, v)).toBeCloseTo(cycle.distancePerCycle, 5);
    // Yang's renderer puts the body at the front bogie plus its offset: the axle boxes sit on the axles
    const bogie = read("assets/seraphhorizons/shapes/entity/handcar-axlebox.json") as Shape;
    const merged = withBogies(shape, rig, bogie, v);
    const view = buildModelView(merged.shape, merged.rig, model.scenario);
    const axles = ["axle_front", "axle_rear"].map((id) => (parts.find((p) => p.id === id)!.drivers![0]!.pivot as number[])[0]!);
    const journals = view.parts[0]!.elements.map((i) => view.flat[i]!.element).filter((e) => /_journall$/.test(e.name));
    expect(journals.map((e) => (e.from[0] + e.to[0]) / 32)).toEqual(axles.map((x) => expect.closeTo(x, 3)));
    // one branch lever of three at a time, straight at first
    expect(initialFitted(view.requires.map((r) => r.value), model.scenario!.choices)).toEqual({ left: false, straight: true, right: false });
    expect(() => checkModelFiles(model, shape, rig, bogie)).not.toThrow();
  });

  it("refuses a grip that rides a part the rig does not have", () => {
    const broken = { ...rig, gripRearR: { pos: [1, 1, 1], part: "nothing" } };
    const model = { id: "handcar", title: "Handcar", description: "d", credit: "c", shape: "s.json", rig: "r.json" };
    expect(() => checkModelFiles(model, shape, broken)).toThrow(/gripRearR rides "nothing"/);
    expect(() => checkModelFiles(model, shape, rig)).not.toThrow();
  });
});
