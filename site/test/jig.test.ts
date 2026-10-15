import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, stateGroups } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The jig's shipped files, and the poses Jig/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads one input, θ, the axle's angle: the shaft turns with it and the eccentric
// sheave rides it; the plunger slides 0.5 voxel sin θ, the water's surfaces and the ore bed answer it; the
// standards, the sieve, the chutes, the inlet and the hutch stand still. tools/tests/test_jig_model.py and
// the mod's C# tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/jig-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/jig.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/Jig/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const apply = (m: Mat4, p: readonly number[]) => [0, 1, 2].map((r) => m[r]! * p[0]! + m[4 + r]! * p[1]! + m[8 + r]! * p[2]! + m[12 + r]!);
const isIdentity = (m: Mat4) => m.every((v, k) => Math.abs(v - (k % 5 === 0 ? 1 : 0)) < 1e-12);

describe("the jig's rig against its rig-reference.json", () => {
  it("reads θ alone, has no work, and fits its parts, its water inlet in either metal and its contents", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["shaft", "drive", null],
      ["sheave", "plunger", "shaft"],
      ["plunger", "plunger", null],
      ["watersieve", "water", null],
      ["waterplunger", "water", null],
      ["bed", "bed", null],
      ["stand", "drive", null],
      ["sieve", "sieve", null],
      ["infeed", "infeed", null],
      ["outlets", "outlets", null],
      ["pipecopper", "pipecopper", null],
      ["pipelead", "pipelead", null],
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
    expect(reference.poses.length).toBeGreaterThanOrEqual(10);
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("turns the shaft with the axle and strokes the plunger once a turn, the same either way", () => {
    const i = (id: string) => parts.findIndex((p) => p.id === id);
    for (const theta of [0.4, 1.5707963, -2.2, 4.0, 9.1]) {
      const ms = partMatrices(parts, pose(theta), order, null);
      // the shaft's axis (y 1.5, z 1.5 blocks) stays put, a point off it turns by θ about +x
      const s = ms[i("shaft")]!;
      expect(apply(s, [0.5, 1.5, 1.5]).map((v) => Math.round(v * 1e9) / 1e9)).toEqual([0.5, 1.5, 1.5]);
      const p = apply(s, [0.5, 1.5, 2.5]);
      expect(p[1]! - 1.5).toBeCloseTo(-Math.sin(theta), 9);
      expect(p[2]! - 1.5).toBeCloseTo(Math.cos(theta), 9);
      expect(ms[i("sheave")]).toEqual(s);
      // the plunger rises and falls by the sheave's throw, 0.5 voxel, and nothing turns it
      const pl = ms[i("plunger")]!;
      expect(pl[13]).toBeCloseTo((0.5 / 16) * Math.sin(theta), 9);
      expect([pl[12], pl[14]].map((v) => Math.abs(v!))).toEqual([0, 0]);
      const back = partMatrices(parts, pose(-theta), order, null)[i("plunger")]!;
      expect(back[13]).toBeCloseTo(-pl[13]!, 12);
      // the sieve's water rises as the plunger falls, the plunger's falls with it, the bed lifts with the pulse
      expect(Math.sign(ms[i("watersieve")]![13]!)).toBe(-Math.sign(pl[13]!));
      expect(Math.sign(ms[i("waterplunger")]![13]!)).toBe(Math.sign(pl[13]!));
      expect(Math.sign(ms[i("bed")]![13]!)).toBe(-Math.sign(pl[13]!));
      for (const still of ["stand", "sieve", "infeed", "outlets", "pipecopper", "pipelead", "frame"]) expect(isIdentity(ms[i(still)]!)).toBe(true);
    }
  });

  it("gives every element a part and every part an element; the inlet's metals their own codes", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const c of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(c);
    expect([...codes.pipecopper!]).toEqual(["pipecopper"]);
    expect([...codes.pipelead!]).toEqual(["pipelead"]);
    expect([...codes.watersieve!]).toEqual(["water"]);
    expect([...codes.bed!]).toEqual(["bed"]);
    expect([...codes.outlets!].sort()).toEqual(["chute", "iron", "oak"]);
    expect(shape.textures!.chute).toBe("game:block/metal/sheet/copper1");
    // the hutch's corner irons: two legs of angle iron on each of its four corners, in the frame, standing still
    const legs = flat.filter((f) => /^hutch_strap_(nw|ne|sw|se)_[xz]$/.test(f.element.name ?? ""));
    expect(legs.length).toBe(8);
    for (const f of legs) {
      expect(parts[partOf(globs, f.chain)]!.id).toBe("frame");
      expect([...textureCodes(f.element)]).toEqual(["iron"]);
    }
  });

  it("puts each service on a face of its own", () => {
    const { anchors } = discoverAnchors(rig);
    const cells = anchors.filter((a) => a.kind === "cell").map((a) => (a.kind === "cell" ? [a.key, a.pos, a.face] : null));
    expect(cells).toEqual([
      ["powerCell", [0, 1, 1], "west"],
      ["infeedCell", [0, 1, 0], "up"],
      ["tailingsCell", [0, 1, 0], "west"],
      ["concentrateCell", [0, 0, 0], "north"],
      ["waterCell", [0, 0, 1], "south"],
    ]);
    expect(anchors.filter((a) => a.kind === "level").map((a) => a.key).sort()).toEqual(["levels.rimY", "levels.sieveY", "levels.weirY"].sort());
  });
});

describe("the jig's page", () => {
  const model = manifest.models.find((m) => m.id === "jig")!;

  it("shows its contents from a select, its inlet in one metal, and fits its parts by checkbox", () => {
    const values = ["sieve", "drive", "plunger", "infeed", "outlets", "pipecopper", "pipelead", "water", "bed"];
    const groups = stateGroups(model.scenario!.states, values);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([["contents", ["water", "bed"]]]);
    const all = Object.fromEntries(values.map((v) => [v, true]));
    const spec = groups[0]!.spec;
    const at = (id: string) => applyState(all, spec.options.find((o) => o.id === id)!, groups[0]!.owns);
    expect(at("dry")).toMatchObject({ water: false, bed: false, plunger: true });
    expect(at("water")).toMatchObject({ water: true, bed: false });
    expect(at("working")).toMatchObject({ water: true, bed: true });
    expect(spec.default).toBe("working");
    expect(model.scenario!.choices).toEqual([{ label: "Water inlet", values: ["pipecopper", "pipelead"], default: "pipecopper" }]);
  });
});
