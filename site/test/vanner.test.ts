import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, needsMet, openingFitted, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rideOrder, rigInputs, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The vanner's shipped files, and the poses Vanner/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ, the axle (the countershaft turns with it; the shaking frame slides across,
// E sin θ, and its uprights swing with it), and ψ, the axle's travel either way (the worm and the rollers it
// creeps turn by it, one way only). tools/tests/test_vanner_model.py and the mod's C# tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/vanner-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/vanner.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/Vanner/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel: number): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);
const index = (id: string) => parts.findIndex((p) => p.id === id);

describe("the vanner's rig against its rig-reference.json", () => {
  it("reads θ and the travel, has no work, and fits its parts in order", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: true, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["countershaft", "countershaft", null],
      ["shaker", "shaker", null],
      ["uprightw", "shaker", null],
      ["uprighte", "shaker", null],
      ["seam1", "belt", "shaker"],
      ["seam2", "belt", "shaker"],
      ["belt", "belt", "shaker"],
      ["head", "rollers", "shaker"],
      ["foot", "rollers", "shaker"],
      ["worm", "creep", "shaker"],
      ["pawl", "creep", null],
      ["chutes", "chutes", null],
      ["feedbox", "feedbox", null],
      ["pipecopper", "pipecopper", null],
      ["pipelead", "pipelead", null],
      ["frame", null, null],
    ]);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    expect(reference.poses.length).toBeGreaterThanOrEqual(20);
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

  it("shakes the frame across with the axle, alike either way, and creeps the belt to the head on the travel", () => {
    const e = 0.5 / 16;
    for (const theta of [0.4, -1.3, Math.PI / 2, 4.1]) {
      const ms = partMatrices(parts, pose(theta, Math.abs(theta)), order, null);
      expect(ms[index("shaker")]![12]).toBeCloseTo(e * Math.sin(theta), 9);
      // the countershaft turns once a turn about its axis (x 1.5, y 0.5, along z)
      const p = apply(ms[index("countershaft")]!, [1.5, 0.5 - e, 1.4]);
      expect(p[0]! - 1.5).toBeCloseTo(e * Math.sin(theta), 9);
    }
    // the head's top moves north (−z), to the head, for any travel, whichever way θ went
    const top = [0.6, (9.3 + 2.0) / 16, 7 / 16];
    for (const [theta, travel] of [[0.5, 0.5], [-0.5, 0.5], [3.0, 30.0], [-3.0, 30.0]] as const) {
      const ms = partMatrices(parts, pose(theta, travel), order, null);
      const shaker = ms[index("shaker")]!;
      const moved = apply(ms[index("head")]!, top);
      expect(moved[2]!).toBeLessThan(top[2]!);
      expect(moved[0]! - top[0]!).toBeCloseTo(shaker[12]!, 9);
    }
  });

  it("carries the belt's laced seams round its loop at the head drum's surface speed, the top run to the head", () => {
    // the belt's inner face rides the drums (radius 2) without slipping: it travels |head ratio| x 2 voxels a radian
    // of travel, round a loop of two runs and two half turns; each seam (its own part) goes round it with the
    // travel, shaking with the frame, and is back where it started once round
    const head = parts[index("head")]!.drivers![0] as unknown as { ratio: number };
    const speed = Math.abs(head.ratio) * 2 / 16;                      // blocks a radian of travel
    const drive = rig.drive as { beltPerTurn: number; beltLength: number };
    expect(drive.beltPerTurn).toBeCloseTo(speed * 2 * Math.PI, 6);
    const cycle = drive.beltLength / speed;
    const flat = flattenShape(shape.elements);
    for (const id of ["seam1", "seam2"]) {
      const lap = flat.find((f) => f.element.name === `belt_${id}`)!;
      const a = lap.element.from, b = lap.element.to;
      const mid = apply(lap.world, [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2]).map((x) => x / 16);
      const at = (theta: number, travel: number) => {
        const ms = partMatrices(parts, pose(theta, travel), order, null);
        return { p: apply(ms[index(id)]!, mid), shift: ms[index("shaker")]![12]! };
      };
      // through a turn of the loop: never off the loop's band (between the drums' axes' line less 3 and more 3
      // voxels), shaking with the frame, and its z sweeping from the head roller's front to the foot roller's back
      let zmin = Infinity, zmax = -Infinity;
      for (let k = 0; k <= 96; k++) {
        const { p, shift } = at(0.9 * k, (cycle * k) / 96);
        expect(p[0]! - mid[0]!).toBeCloseTo(shift, 9);
        zmin = Math.min(zmin, p[2]!);
        zmax = Math.max(zmax, p[2]!);
        const axisY = (9.3 + ((p[2]! * 16 - 7) * (8.1 - 9.3)) / 31) / 16;   // the line of the drums' axes under it
        const r = Math.hypot(p[1]! - axisY, p[2]! < 7 / 16 ? p[2]! - 7 / 16 : p[2]! > 38 / 16 ? p[2]! - 38 / 16 : 0);
        expect(r * 16).toBeGreaterThan(2.3);
        expect(r * 16).toBeLessThan(2.7);
      }
      expect(zmin * 16).toBeLessThan(5.0);
      expect(zmax * 16).toBeGreaterThan(40.0);
      // the seam at rest is on the top run; a little travel on, it is nearer the head (−z) by the drum's surface
      const { p: p0 } = at(0, 0), { p: p1 } = at(0, 0.03 / speed);
      expect(p1[2]!).toBeLessThan(p0[2]!);
      expect(Math.hypot(p1[1]! - p0[1]!, p1[2]! - p0[2]!)).toBeCloseTo(0.03, 3);
      const back = at(0, cycle).p;
      for (let c = 0; c < 3; c++) expect(back[c]!).toBeCloseTo(mid[c]!, 4);
    }
  });

  it("advances the worm's helical thread under the wheel as the travel turns it, the way the wheel's bottom runs", () => {
    // 49 bars, a 16th of a turn apart (3 turns), and 12 teeth cut across the rim in three slices each
    const flat = flattenShape(shape.elements);
    const named = (rx: RegExp) => flat.filter((f) => rx.test(f.element.name));
    const bars = named(/^worm_thread\d+$/);
    expect(bars.length).toBe(49);
    expect(named(/^head_wheel_tooth\d+_[wce]$/).length).toBe(36);
    const centre = (f: (typeof flat)[number], m: Mat4) => {
      const a = f.element.from, b = f.element.to;
      const v = apply(f.world, [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2]);   // voxels
      return apply(m, v.map((x) => x / 16));
    };
    const lead = (Math.PI * 0.6) / 16;   // the lead in blocks
    const step = (12 * 2 * Math.PI) / 16; // the travel that turns the worm a 16th of a turn
    let last: number | null = null;
    for (let k = 0; k <= 6; k++) {
      const ms = partMatrices(parts, pose(0, step * k), order, null);
      // the bar on top of the worm under the wheel (within half a lead of the wheel's axis, z 7/16)
      const top = bars.map((f) => centre(f, ms[index("worm")]!)).filter((c) => Math.abs(c[2]! - 7 / 16) <= lead / 2 + 1e-4)
        .reduce((a, b) => (b[1]! > a[1]! ? b : a));
      if (last !== null) {
        const moved = top[2]! - last;
        const d = moved < -lead / 4 ? moved + lead : moved;   // past the window's end, the next turn's bar takes over
        expect(d).toBeCloseTo(lead / 16, 3);                   // to the south (+z), as the wheel's bottom teeth go
      }
      last = top[2]!;
    }
  });

  it("gives every element a part and every part an element, the water line twice over", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const count = (id: string) => flat.filter((f) => partOf(globs, f.chain) === index(id)).length;
    expect(count("pipecopper")).toBe(count("pipelead"));
  });

  it("marks the axle, the water, the infeed and the two output chutes, each on its own cell's face", () => {
    const { anchors } = discoverAnchors(rig);
    const face = (key: string) => anchors.find((a) => a.key === key);
    expect(face("powerCell")).toMatchObject({ kind: "cell", pos: [1, 0, 2], face: "south" });
    expect(face("waterCell")).toMatchObject({ kind: "cell", pos: [1, 0, 0], face: "up" });
    expect(face("feedCell")).toMatchObject({ kind: "cell", pos: [0, 0, 1], face: "up" });
    expect(face("concentrateCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "north" });
    expect(face("tailingsCell")).toMatchObject({ kind: "cell", pos: [0, 0, 2], face: "south" });
  });
});

describe("the vanner's page", () => {
  const model = manifest.models.find((m) => m.id === "vanner")!;

  const groups = stateGroups(model.scenario!.states, requiresValues(parts));
  const stage = groups.find((g) => g.id === "stage")!;
  const water = groups.find((g) => g.id === "water")!;

  it("builds in the settled stages from the placed frame, services last, one state a stage", () => {
    expect(checkScenario(model.scenario!, rig, discoverAnchors(rig).anchors, requiresValues(parts))).toEqual([]);
    const adds = (rig.buildStages as { id: string; adds: string[] }[]).map((s) => [s.id, s.adds]);
    expect(adds).toEqual([
      ["countershaft", ["countershaft"]],
      ["shaker", ["shaker"]],
      ["rollers", ["rollers", "creep"]],
      ["belt", ["belt"]],
      ["services", ["feedbox", "chutes", "pipecopper", "pipelead"]],
    ]);
    // each state is the frame and the stages up to it, the water line in the services' own select
    const opts = stage.spec.options;
    expect(opts.map((o) => o.id)).toEqual(["frame", ...adds.map(([id]) => id)]);
    let want: string[] = [];
    opts.slice(1).forEach((o, i) => {
      want = [...want, ...(adds[i]![1] as string[]).filter((r) => !r.startsWith("pipe"))];
      expect(o.fitted).toEqual(want);
    });
    expect(opts[0]!.fitted).toEqual([]);
  });

  it("opens finished, the water line in copper, and draws no water line before the services", () => {
    const { fitted } = openingFitted(requiresValues(parts), model.scenario!.choices, groups);
    expect(fitted).toEqual({ countershaft: true, shaker: true, rollers: true, belt: true, creep: true, feedbox: true, chutes: true, pipecopper: true, pipelead: false });
    expect(needsMet(water, fitted)).toBe(true);
    const belt = applyState(fitted, stage.spec.options.find((o) => o.id === "belt")!, stage.owns);
    expect(needsMet(water, belt)).toBe(false);
    expect(statesShow("pipecopper", belt, groups)).toBe(false);
    const lead = applyState(fitted, water.spec.options.find((o) => o.id === "lead")!, water.owns);
    expect([lead.pipecopper, lead.pipelead]).toEqual([false, true]);
  });
});
