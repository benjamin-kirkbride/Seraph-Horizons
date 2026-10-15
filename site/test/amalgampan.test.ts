import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, needsMet, openingFitted, pickedTextures, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { buildModelView, withPickedTextures } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The amalgam pan's shipped files, and the poses AmalgamPan/tools/make_shape.py computes from them with
// machinegen's reference maths. Worked as the quern is, by hand or by a vanilla axle from below, its rig
// reads one input, θ, the spindle's angle: the muller (spindle, coupling, hub, arms) turns by it about the
// pan's axis, once a turn, and its shoes ride it, as does the water film over the charge; the other contents
// (mercury, the charge's slurry, amalgam) and the frame stand still. #718's fitted stages are the muller (a rod), then the shoes (a plate, drawn in its
// metal through the `shoe` code). tools/tests/test_amalgampan_model.py and the mod's C# tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/amalgampan-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/amalgampan.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/AmalgamPan/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);

describe("the amalgam pan's rig against its rig-reference.json", () => {
  it("reads θ alone, has no work, takes its axle from below at its own cell, and fits the muller, its shoes and its contents", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(rig.powerCell).toEqual([0, 0, 0]);
    expect(rig.powerFace).toBe("down");
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["muller", "muller", null],
      ["shoes", "shoes", "muller"],
      ["mercury", "mercury", null],
      ["slurry", "slurry", null],
      ["water", "water", "muller"],
      ["amalgam", "amalgam", null],
      ["frame", null, null],
    ]);
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

  it("turns the muller and its shoes with the axle, once a turn about the pan's axis, and nothing else", () => {
    const i = (id: string) => parts.findIndex((p) => p.id === id);
    for (const theta of [0.4, 2.2, -1.3, 7.9]) {
      const ms = partMatrices(parts, pose(theta), order, null);
      // the pan's axis stays put, and a point a block out along x turns by θ about +y
      const m = ms[i("muller")]!;
      expect(apply(m, [0.5, 0.6, 0.5]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([0.5, 0.6, 0.5]);
      const p = apply(m, [1.5, 0.6, 0.5]);
      expect(p[0]! - 0.5).toBeCloseTo(Math.cos(theta), 9);
      expect(p[2]! - 0.5).toBeCloseTo(-Math.sin(theta), 9);
      expect(ms[i("shoes")]).toEqual(m);
      // the water film over the charge turns with them, parted round each shoe
      expect(ms[i("water")]).toEqual(m);
      for (const still of ["mercury", "slurry", "amalgam", "frame"]) expect(ms[i(still)]!.map((v, k) => v - (k % 5 === 0 ? 1 : 0)).every((v) => Math.abs(v) < 1e-12)).toBe(true);
    }
    // the viewer's θ slider spans one turn
    expect(thetaTurns(parts)).toBe(1);
  });

  it("gives every element a part and every part an element, the shoes and the contents their own texture codes", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const c of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(c);
    expect([...codes.mercury!]).toEqual(["mercury"]);
    expect([...codes.slurry!]).toEqual(["slurry"]);
    expect([...codes.amalgam!]).toEqual(["amalgam"]);
    expect([...codes.water!]).toEqual(["water"]);
    expect(shape.textures?.water).toBe("game:block/liquid/waterportion");
    // the shoes alone wear `shoe`, which the renderer sets to the fitted plate's metal
    expect([...codes.shoes!]).toEqual(["shoe"]);
    for (const [id, cs] of Object.entries(codes)) if (id !== "shoes") expect(cs.has("shoe"), id).toBe(false);
    // iron, and the coupling at the spindle's foot the vanilla axle's oak
    expect([...codes.muller!].sort()).toEqual(["iron", "oak"]);
  });

  it("marks the axle's way in from below, the operator's side and the pan's floor", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "powerCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "down" });
    expect(anchors.find((a) => a.key === "operatorSide")).toMatchObject({ kind: "side", side: "north" });
    expect(anchors.find((a) => a.key === "pan")).toMatchObject({ kind: "point", pos: [0.5, 0.375, 0.5] });
  });
});

describe("the amalgam pan's page", () => {
  const model = manifest.models.find((m) => m.id === "amalgam-pan")!;
  const requires = ["muller", "shoes", "mercury", "slurry", "water", "amalgam"];
  const groups = stateGroups(model.scenario!.states, requires);
  const group = (id: string) => groups.find((g) => g.id === id)!;

  it("builds it in #718's order: the frame, the muller, then the shoes", () => {
    expect(groups.map((g) => [g.id, g.owns])).toEqual([
      ["build", ["muller", "shoes"]],
      ["contents", ["mercury", "slurry", "water", "amalgam"]],
    ]);
    const build = group("build");
    expect(build.spec.options.map((o) => [o.id, o.fitted])).toEqual([
      ["frame", []],
      ["muller", ["muller"]],
      ["complete", ["muller", "shoes"]],
    ]);
    for (const o of build.spec.options) expect(o.hint).toBeTruthy();
    expect(build.spec.default).toBe("complete");
  });

  it("shows its contents once it is built: empty, mercury, charged, charged under water, amalgam", () => {
    const contents = group("contents");
    const all = { muller: true, shoes: true, mercury: true, slurry: true, water: true, amalgam: true };
    expect(contents.spec.options.map((o) => o.id)).toEqual(["empty", "mercury", "charged", "watered", "amalgam"]);
    for (const o of contents.spec.options) expect(o.hint).toBeTruthy();
    const at = (id: string) => applyState(all, contents.spec.options.find((o) => o.id === id)!, contents.owns);
    expect(at("empty")).toMatchObject({ mercury: false, slurry: false, water: false, amalgam: false });
    expect(at("mercury")).toMatchObject({ mercury: true, slurry: false, water: false, amalgam: false });
    expect(at("charged")).toMatchObject({ muller: true, shoes: true, mercury: false, slurry: true, water: false, amalgam: false });
    expect(at("watered")).toMatchObject({ muller: true, shoes: true, mercury: false, slurry: true, water: true, amalgam: false });
    expect(at("amalgam")).toMatchObject({ mercury: false, slurry: false, water: false, amalgam: true });
    expect(contents.spec.default).toBe("watered");
    // it opens built and charged, water over the charge; with the shoes off the contents wait
    const { fitted } = openingFitted(requires, [], groups);
    expect(fitted).toEqual({ muller: true, shoes: true, mercury: false, slurry: true, water: true, amalgam: false });
    const bare = applyState(fitted, group("build").spec.options.find((o) => o.id === "muller")!, group("build").owns);
    expect(needsMet(contents, bare)).toBe(false);
    expect(statesShow("slurry", bare, groups)).toBe(false);
    expect(statesShow("slurry", fitted, groups)).toBe(true);
  });

  it("draws the shoes in the fitted plate's metal, iron or steel, opening on iron", () => {
    const select = model.scenario!.textures!;
    expect(select.map((s) => [s.code, s.default, s.options.map((o) => [o.id, o.path])])).toEqual([
      ["shoe", "iron", [["iron", "game:block/metal/plate/iron"], ["steel", "game:block/metal/plate/steel"]]],
    ]);
    // the shape's own shoe texture is the default plate's
    expect(shape.textures?.shoe).toBe("game:block/metal/plate/iron");
    const view = buildModelView(shape, rig, model.scenario);
    const opening = view.textures.find((t) => t.code === "shoe")!;
    expect(opening.colour).toBe("#695d56");
    const steel = withPickedTextures(view.textures, pickedTextures(select, { shoe: "steel" })).find((t) => t.code === "shoe")!;
    expect(steel).toMatchObject({ path: "game:block/metal/plate/steel", colour: "#807f7c", elements: opening.elements });
  });
});
