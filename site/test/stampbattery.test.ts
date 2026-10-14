import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { cellBoxes, discoverAnchors, lidBox } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, stateGroups } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, workOf, type Mat4, type Pose, type Rig, type RigCell, type Shape } from "../src/lib/rig.ts";

// The stamp battery's shipped files, and the poses StampBattery/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads the axle's angle θ (the entry shaft) and its travel ψ, which everything behind the
// rectifier follows: the line shaft and its pulley, the camshaft, the stamps (each a sum of harmonics of the camshaft's
// turn). It has one level and no work. tools/tests/test_stampbattery_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/stampbattery-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/stampbattery.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/StampBattery/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const BATTERY = ["mortar", "camshaft", "stamps"];
const LEVEL1 = [...BATTERY, "feedbox"];
const VALUES = LEVEL1;
const PIPES = ["pipecopper", "pipelead"];

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel: number): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the stamp battery's rig against its rig-reference.json", () => {
  it("reads the axle's angle and travel, has no work, and fits its one level's parts", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: true, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(new Set(parts.map((p) => p.requires ?? null))).toEqual(new Set([null, ...VALUES, ...PIPES]));
    const byId = Object.fromEntries(parts.map((p) => [p.id, p]));
    for (const id of ["entry", "line", "frame"]) expect(byId[id]!.requires ?? null).toBeNull();
    for (const p of parts) expect(p.ride ?? null).toBeNull();
    const levels = rig.levels as Record<string, string[]>;
    expect(Object.keys(levels).filter((k) => !k.startsWith("_"))).toEqual(["1"]);
    expect(levels["1"]).toEqual(LEVEL1);
    expect(rig.feeder).toBeUndefined();
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("drops each stamp twice a camshaft turn, by its lift and no more", () => {
    const stamps = parts.filter((p) => /^stamp\d$/.test(p.id));
    expect(stamps.length).toBe(5);
    const turnsPerRev = (rig.stamps as { turnsPerRev: number }).turnsPerRev;
    const lift = (rig.stamps as { lift: number }).lift;
    for (const s of stamps) {
      const i = parts.indexOf(s);
      const ys = Array.from({ length: 721 }, (_, k) => {
        const psi = (2 * Math.PI * turnsPerRev * k) / 720;
        return partMatrices(parts, pose(psi, psi), order, null)[i]![13]!;
      });
      const lo = Math.min(...ys), hi = Math.max(...ys);
      const drops = ys.filter((y, k) => k > 0 && ys[k - 1]! >= lo + lift / 2 && y < lo + lift / 2).length;
      expect(hi - lo).toBeGreaterThan(lift * 0.98);
      expect(hi - lo).toBeLessThan(lift * 1.15);
      expect(drops).toBe(2);
    }
  });

  it("discharges through a funnel of the chute's sheet spanning the screen, with no launder or apron", () => {
    const flat = flattenShape(shape.elements);
    const names = flat.map((f) => f.element.name);
    expect(names.filter((n) => /launder|apron/.test(n))).toEqual([]);
    const funnel = flat.filter((f) => f.element.name.startsWith("mortar_funnel_"));
    expect(funnel.map((f) => f.element.name).sort()).toEqual(["mortar_funnel_floor", "mortar_funnel_floore", "mortar_funnel_floorw", "mortar_funnel_walle", "mortar_funnel_wallw"]);
    for (const f of funnel) for (const face of Object.values(f.element.faces ?? {})) expect(face.texture).toBe("#chute");
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });

  it("fills its 3 × 4 × 3 box with cells and decks every column", () => {
    const cells = rig.cells as RigCell[];
    expect(cells.length).toBe(36);
    const xs = cells.map((c) => c.pos[0]), ys = cells.map((c) => c.pos[1]), zs = cells.map((c) => c.pos[2]);
    expect([Math.min(...xs), Math.max(...xs), Math.min(...ys), Math.max(...ys), Math.min(...zs), Math.max(...zs)]).toEqual([-1, 1, 0, 3, 0, 2]);
    for (const c of cells) {
      expect(cellBoxes(c).length).toBeGreaterThan(0);
      expect(lidBox(c) !== null).toBe(c.pos[1] === 3);
    }
  });

  it("puts its anchors on the faces they name: power at the back east, water west, chute in on top, chute out in front", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "powerCell")).toMatchObject({ kind: "cell", pos: [1, 0, 2], face: "east" });
    expect(anchors.find((a) => a.key === "waterCell")).toMatchObject({ kind: "cell", pos: [-1, 2, 2], face: "west" });
    expect(anchors.find((a) => a.key === "infeedCell")).toMatchObject({ kind: "cell", pos: [0, 3, 2], face: "up" });
    expect(anchors.find((a) => a.key === "outputCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "north" });
    expect((rig.infeed as { pos: number[] }).pos).toEqual([0.5, 4, 2.5]);
    expect((rig.output as { pos: number[] }).pos).toEqual([0.5, 0.5, 0]);
  });
});

describe("the stamp battery's page", () => {
  const model = manifest.models.find((m) => m.id === "stamp-battery")!;

  it("builds the machine from a select: the frame, then its one level", () => {
    const groups = stateGroups(model.scenario!.states, [...VALUES, ...PIPES]);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([["level", VALUES], ["pipes", PIPES]]);
    const none = Object.fromEntries(VALUES.map((r) => [r, false]));
    const spec = groups[0]!.spec;
    const at = (id: string) => applyState(none, spec.options.find((o) => o.id === id)!, groups[0]!.owns);
    expect(Object.values(at("frame")).every((v) => !v)).toBe(true);
    expect(at("level1")).toEqual({ mortar: true, camshaft: true, stamps: true, feedbox: true });
    expect(spec.options.map((o) => o.id)).toEqual(["frame", "level1"]);
    expect(spec.default).toBe("level1");
  });

  it("draws the water line in one metal at a time, copper by default, once the mortar is fitted", () => {
    const groups = stateGroups(model.scenario!.states, [...VALUES, ...PIPES]);
    const pipes = groups[1]!;
    expect(pipes.spec.default).toBe("pipecopper");
    expect(pipes.spec.needs).toEqual(["mortar"]);
    const none = { pipecopper: false, pipelead: false };
    expect(applyState(none, pipes.spec.options.find((o) => o.id === "pipelead")!, pipes.owns)).toEqual({ pipecopper: false, pipelead: true });
    const flat = flattenShape(shape.elements);
    const names = (m: string) => flat.map((f) => f.element.name).filter((n) => n.startsWith(`pipe${m}_`)).map((n) => n.slice(`pipe${m}_`.length));
    expect(names("lead")).toEqual(names("copper"));
    expect(names("copper").length).toBeGreaterThan(10);
  });
});
