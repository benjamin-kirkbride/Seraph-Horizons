import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, checkScenario, openingFitted, pickedTextures, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { buildModelView, withPickedTextures } from "../src/lib/model-view.ts";
import { fitted, partMatrices, rideOrder, rigInputs, thetaTurns, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The reverberatory roaster's shipped files, and the poses ReverberatoryRoaster/tools/make_shape.py computes from them
// with machinegen's reference maths. Its entry shaft reads θ and everything beyond the gear case's rectifier ψ, the
// axle's travel (the line shaft, the feed roll on its end, the rabbles through their bevel pairs); everything else is a
// requires: the structure's stages, the two levels' fittings, the contents, the damper's places, the pipes' metal and
// the outlet's cap. tools/tests/test_reverberatory_model.py replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
type Build = { stages: { requires: string }[]; levels: { level: number; fits: string[]; replaces?: Record<string, string>; also?: string[] }[] };
type Bricks = { code: string; default: string; types: Record<string, { base: string; overlays?: string[] }> };
const rig = read("assets/seraphhorizons/config/reverberatory-rig.json") as Rig & { build: Build; bricks: Bricks };
const shape = read("assets/seraphhorizons/shapes/block/reverberatory.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/ReverberatoryRoaster/rig-reference.json") as { poses: RefPose[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const model = manifest.models.find((m) => m.id === "reverberatory")!;

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const MOVING = ["entry", "lineshaft", "rabble1", "rabble2", "feedroll"];

describe("the reverberatory roaster's rig against its rig-reference.json", () => {
  it("reads θ at the entry and ψ beyond the rectifier, and has a matrix for every part at every pose", () => {
    expect(rigInputs(parts)).toMatchObject({ theta: true, travel: true, depth: false, lifting: false });
    for (const k of ["work", "size", "presence", "feed", "oil"] as const) expect(rigInputs(parts)[k]).toBeFalsy();
    expect(thetaTurns(parts)).toBe(1);
    expect(rig.work).toBeUndefined();
    expect(rig.trunkPath).toBeUndefined();
    expect(reference.poses.length).toBeGreaterThan(10);
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    expect(parts.filter((q) => (q.drivers ?? []).length > 0).map((q) => q.id)).toEqual(MOVING);
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

  it("turns the rabbles, the line shaft and the feed roll forward whichever way the axle turns; only the entry turns back", () => {
    for (const th of [0.6, 2.4, 9]) {
      const fwd = partMatrices(parts, { ...pose(th), travel: th }, order, null);
      const back = partMatrices(parts, { ...pose(-th), travel: th }, order, null);
      parts.forEach((q, i) => {
        const same = fwd[i]!.every((v, k) => Math.abs(v - back[i]![k]!) < 1e-9);
        expect([q.id, same]).toEqual([q.id, q.id !== "entry"]);
      });
    }
  });
});

describe("the reverberatory roaster's scenario", () => {
  const view = buildModelView(shape, rig, model.scenario);
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(model.scenario!.states, values);
  const opening = openingFitted(values, [], groups);
  const group = (id: string) => groups.find((g) => g.id === id)!;
  const pick = (start: Record<string, boolean>, gid: string, sid: string) => {
    const g = group(gid);
    return applyState(start, g.spec.options.find((o) => o.id === sid)!, g.owns);
  };
  const shown = (f: Record<string, boolean>) =>
    view.parts.filter((p) => fitted(p.part.requires, f) && statesShow(p.part.requires, f, groups)).map((p) => p.part.id);

  it("fits the rig: every requires value in one group, opening built at level 2 running, damper open, copper pipes, outlet open", () => {
    expect(checkScenario(model.scenario!, rig, view.anchors, values, view.textures.map((t) => t.code))).toEqual([]);
    expect(groups.map((g) => g.id)).toEqual(["build", "level", "damper", "water", "outlet"]);
    expect(groups.flatMap((g) => g.owns).sort()).toEqual([...values].sort());
    expect(opening.picked).toEqual({ build: "arch", level: "level2", damper: "open", water: "copper", outlet: "open" });
  });

  it("follows the rig's build: the stages, and each level's fittings with level 2's in place of level 1's", () => {
    expect(rig.build.stages.map((s) => s.requires)).toEqual(group("build").owns);
    const options = Object.fromEntries(group("level").spec.options.map((o) => [o.id, o.fitted]));
    const [l1, l2] = rig.build.levels;
    expect(options.level1cold).toEqual(l1!.fits);
    expect(options.level2cold).toEqual(l2!.fits);
    expect(options.level1).toEqual([...l1!.fits, "charge", "fireflat"]);
    expect(options.level2).toEqual([...l2!.fits, "charge", "firestep"]);
    for (const [was, now] of Object.entries(l2!.replaces ?? {})) {
      expect(l1!.fits).toContain(was);
      expect(l2!.fits).not.toContain(was);
      expect(l2!.fits).toContain(now);
    }
  });

  it("shows only what each state fits: the frame alone, level 1 with no moving part, level 2 without level 1's grate and cover", () => {
    const frame = pick(opening.fitted, "build", "frame");
    expect(shown(frame)).toEqual(["frame"]);
    const l1 = pick(opening.fitted, "level", "level1");
    const s1 = shown(l1);
    expect(s1.filter((p) => MOVING.includes(p))).toEqual([]);
    expect(s1).toEqual(expect.arrayContaining(["handfiring", "doorsopen", "dischargeshut", "fireflat", "hopper", "firedoor", "damperopen"]));
    expect(s1).not.toContain("pipecopper");
    const s2 = shown(opening.fitted);
    expect(s2).toEqual(expect.arrayContaining([...MOVING, "stoker", "doorsshut", "dischargeopen", "output", "firestep", "pipecopper"]));
    for (const p of ["handfiring", "doorsopen", "dischargeshut", "fireflat", "pipelead", "outletcap"]) expect(s2).not.toContain(p);
    const lead = pick(opening.fitted, "water", "lead");
    expect(shown(lead)).toContain("pipelead");
    expect(shown(lead)).not.toContain("pipecopper");
  });

  it("offers every brick type of the rig, in its order, the brickwork drawn in the picked one", () => {
    const select = model.scenario!.textures!;
    expect(select).toHaveLength(1);
    const brick = select[0]!;
    expect([brick.code, brick.default]).toEqual([rig.bricks.code, rig.bricks.default]);
    expect(brick.options.map((o) => o.id)).toEqual(Object.keys(rig.bricks.types));
    for (const o of brick.options) expect(o.path).toBe(rig.bricks.types[o.id]!.overlays?.[0] ?? rig.bricks.types[o.id]!.base);
    expect(shape.textures?.[rig.bricks.code]).toBe(brick.options.find((o) => o.id === rig.bricks.default)!.path);
    const black = withPickedTextures(view.textures, pickedTextures(select, { brick: "black" })).find((t) => t.code === "brick")!;
    expect(black.colour).toBe("#494339");
  });

  it("draws its anchors: power, infeed, output, fuel, water and the outlet with the chimney's line on it", () => {
    const keys = view.anchors.map((a) => a.key);
    for (const k of ["powerCell", "infeedCell", "outputCell", "fuelCell", "waterCell", "outletCell", "chimney", "ash", "stoke", "workDoor1", "workDoor2"])
      expect(keys).toContain(k);
    const r = rig as unknown as Record<string, unknown>;
    expect([r.powerCell, r.powerFace]).toEqual([[0, 2, 0], "north"]);
    expect([r.infeedCell, r.infeedFace]).toEqual([[0, 2, 4], "up"]);
    expect([r.outputCell, r.outputFace]).toEqual([[1, 0, 2], "east"]);
    expect([r.fuelCell, r.fuelFace]).toEqual([[-1, 1, 0], "up"]);
    expect([r.waterCell, r.waterFace]).toEqual([[0, 2, 2], "up"]);
    expect([r.outletCell, r.outletFace]).toEqual([[-1, 1, 4], "up"]);
    // the outlet is not under the drive's row (whose cells are x 0, y 2) nor the infeed's column
    const cells = rig.cells!.map((c) => c.pos.join());
    expect(cells).not.toContain("-1,2,4");
    expect(cells).toContain("0,2,4");
  });

  it("binds its brick in one connected frame: corner angles, knees at the footing's step, bands and the ironwork", () => {
    type Box = [number[], number[]];
    const boxes = new Map<string, Box>();
    for (const e of shape.elements)
      if (/^(fr_angle_|fr_knee_|fr_band_|iw_)/.test(e.name)) boxes.set(e.name, [e.from, e.to]);
    for (const f of ["n", "s"]) for (const leg of ["face", "top"]) expect(boxes.has(`fr_knee_${f}_${leg}`)).toBe(true);
    // two members meet over an area: apart along no axis, overlapping along at least two
    const touch = (a: Box, b: Box) => {
      const gap = [0, 1, 2].map((k) => Math.max(a[0][k]!, b[0][k]!) - Math.min(a[1][k]!, b[1][k]!));
      return gap.every((g) => g <= 1e-6) && gap.filter((g) => g < -1e-6).length >= 2;
    };
    const names = [...boxes.keys()];
    const seen = new Set<string>();
    const todo = [names[0]!];
    while (todo.length) {
      const n = todo.pop()!;
      if (seen.has(n)) continue;
      seen.add(n);
      for (const o of names) if (!seen.has(o) && touch(boxes.get(n)!, boxes.get(o)!)) todo.push(o);
    }
    expect(names.filter((n) => !seen.has(n))).toEqual([]);
  });
});
