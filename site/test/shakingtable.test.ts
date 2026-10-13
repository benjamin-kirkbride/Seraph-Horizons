import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { choiceOf, initialFitted } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The shaking table's shipped files, and the poses ShakingTable/tools/make_shape.py computes from them with
// machinegen's reference maths. Its rig reads θ, the axle's angle (the axle's cross into the rectifier case),
// and ψ, the axle's travel either way: past the rectifier the head shaft turns 3ψ, and the deck strokes by
// three harmonics of a slow-forward, quick-return stroke on it (three slides along z), the pull rod riding
// it. tools/tests/test_shakingtable_model.py and the mod's C# tests replay the same file.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/shakingtable-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/shakingtable.json") as Shape;
type RefPose = { theta: number; travel: number; matrices: Record<string, number[][]> };
const reference = read("tests/ShakingTable/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const order = rideOrder(parts);
const index = (id: string) => parts.findIndex((p) => p.id === id);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const pose = (theta: number, travel = Math.abs(theta)): Pose => ({ theta, depth: 0, lifting: 0, travel, work: 0, size: 0, presence: 0, feed: 0, oil: 0 });
const matrix = (id: string, theta: number, travel?: number) => partMatrices(parts, pose(theta, travel), order, null)[index(id)]!;
// the deck's offset along z (blocks): its matrix's translation
const deckZ = (travel: number) => matrix("deck", travel, travel)[14]!;
const angleX = (m: Mat4) => Math.atan2(m[6]!, m[5]!);

describe("the shaking table's rig against its rig-reference.json", () => {
  it("reads θ and ψ, has no work, and fits the deck, the head, the rectifier, the chutes and a water line", () => {
    expect(rigInputs(parts)).toEqual({ theta: true, travel: true, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(thetaTurns(parts)).toBe(1);
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null])).toEqual([
      ["entry", "rectifier", null],
      ["headshaft", "head", null],
      ["deck", "deck", null],
      ["pullrod", "head", "deck"],
      ["rectifier", "rectifier", null],
      ["head", "head", null],
      ["chutes", "chutes", null],
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
            if (diff > 2e-6) throw new Error(`${q.id} at θ ${p.theta}, ψ ${p.travel}: [${r}][${c}] is ${got[r]![c]}, want ${want[r]![c]}`);
          }
      });
    }
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("turns the head shaft three times the axle's travel and strokes the deck the same whichever way the axle turns", () => {
    for (const theta of [0.4, 1.3, 2.9, 7.7]) {
      expect(matrix("headshaft", -theta)).toEqual(matrix("headshaft", theta));
      expect(matrix("deck", -theta)).toEqual(matrix("deck", theta));
      expect(matrix("pullrod", theta)).toEqual(matrix("deck", theta));
      expect(Math.cos(angleX(matrix("headshaft", theta)) - 3 * theta)).toBeCloseTo(1, 9);
      // the axle's cross follows the axle, either way
      expect(Math.cos(angleX(matrix("entry", -theta)) + theta)).toBeCloseTo(1, 9);
    }
  });

  it("strokes the deck a slow push south and a quick pull back, once a turn of the head shaft", () => {
    const period = (2 * Math.PI) / 3;
    const n = 720;
    const zs = Array.from({ length: n + 1 }, (_, i) => deckZ((period * i) / n));
    const steps = zs.slice(1).map((z, i) => z - zs[i]!);
    const forward = steps.filter((s) => s > 0).length / n;
    let changes = 0;
    for (let i = 0; i < n; i++) if (steps[i]! > 0 !== steps[(i + n - 1) % n]! > 0) changes++;
    const stroke = Math.max(...zs) - Math.min(...zs);
    expect(zs[n]!).toBeCloseTo(zs[0]!, 9);
    expect(stroke).toBeCloseTo((rig.stroke as { length: number }).length, 3);
    expect(forward).toBeGreaterThan(0.6);
    expect(forward).toBeLessThan(0.8);
    expect(changes).toBe(2);
    expect(-Math.min(...steps)).toBeGreaterThan(2 * Math.max(...steps));
    expect((rig.stroke as { strokesPerTurn: number; forward: string })).toMatchObject({ strokesPerTurn: 3, forward: "south" });
  });

  it("gives every element a part and every part an element", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
  });

  it("marks the power, water and chute anchors, each a cell and a face", () => {
    const { anchors, unrecognised } = discoverAnchors(rig);
    const cell = (key: string) => anchors.find((a) => a.key === key);
    expect(cell("powerCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "west" });
    expect(cell("infeedCell")).toMatchObject({ kind: "cell", pos: [0, 0, 1], face: "up" });
    expect(cell("waterCell")).toMatchObject({ kind: "cell", pos: [0, 0, 2], face: "up" });
    expect(cell("concentrateCell")).toMatchObject({ kind: "cell", pos: [0, 0, 2], face: "south" });
    expect(cell("middlingsCell")).toMatchObject({ kind: "cell", pos: [1, 0, 2], face: "south" });
    expect(cell("tailingsCell")).toMatchObject({ kind: "cell", pos: [1, 0, 0], face: "east" });
    expect(unrecognised).toEqual(["stroke"]);
  });
});

describe("the shaking table's page", () => {
  const model = manifest.models.find((m) => m.id === "shaking-table")!;

  it("draws one water line, copper or lead, from a select", () => {
    const choices = model.scenario!.choices!;
    const fitted = initialFitted(["deck", "head", "rectifier", "chutes", "pipecopper", "pipelead"], choices);
    expect(fitted).toMatchObject({ deck: true, head: true, rectifier: true, chutes: true, pipecopper: true, pipelead: false });
    expect(choiceOf(choices, "pipelead")?.label).toBe("Water line");
  });
});
