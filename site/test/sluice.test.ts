import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { needsMet, openingFitted, applyState, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workOf, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The sluice's shipped files (Sluice/tools/make_shape.py). Nothing in it moves: it takes no power, and
// its parts are fitted or drawn by state, with no driver, so it has no reference poses; every part's
// matrix is the identity at any input. The concentrate is three requires values, one drawn at a time, a
// "Concentrate" select on its page. tools/tests/test_sluice_model.py checks the same files' geometry.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/sluice-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/sluice.json") as Shape;
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const REQUIRES = ["riffles", "concentrate1", "concentrate2", "concentrate3", "infeedchute", "tailingschute", "pipecopper", "pipelead"];

const pose = (theta: number): Pose => ({ theta, depth: 0.5, lifting: 1, travel: Math.abs(theta) + 3, work: 2, size: 1, presence: 1, feed: 1, oil: 0.5 });

describe("the sluice's rig", () => {
  it("reads no input, has no work and no power cell, and fits the riffles, the chutes, the water inlet and the concentrate", () => {
    expect(rigInputs(parts)).toEqual({ theta: false, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(rig.powerCell).toBeUndefined();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null, p.drivers?.length ?? 0])).toEqual([
      ["riffles", "riffles", null, 0],
      ["concentrate1", "concentrate1", null, 0],
      ["concentrate2", "concentrate2", null, 0],
      ["concentrate3", "concentrate3", null, 0],
      ["infeedchute", "infeedchute", null, 0],
      ["tailingschute", "tailingschute", null, 0],
      ["pipecopper", "pipecopper", null, 0],
      ["pipelead", "pipelead", null, 0],
      ["frame", null, null, 0],
    ]);
    expect((rig.fill as { requires: string[] }).requires).toEqual(["concentrate1", "concentrate2", "concentrate3"]);
  });

  it("poses every part where it was authored, whatever the inputs", () => {
    for (const theta of [0, 1.3, -7.9]) {
      const ms = partMatrices(parts, pose(theta), rideOrder(parts), null);
      for (const m of ms) expect(m.map((v, k) => v - (k % 5 === 0 ? 1 : 0)).every((v) => Math.abs(v) < 1e-12)).toBe(true);
    }
  });

  it("gives every element a part and every part an element; the concentrate, the chutes and the pipes their own textures", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const c of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(c);
    for (const k of [1, 2, 3]) expect([...codes[`concentrate${k}`]!]).toEqual(["concentrate"]);
    expect([...codes.infeedchute!]).toEqual(["chute"]);
    expect([...codes.tailingschute!]).toEqual(["chute"]);
    expect([...codes.pipecopper!]).toEqual(["pipecopper"]);
    expect([...codes.pipelead!]).toEqual(["pipelead"]);
    expect([...codes.riffles!].sort()).toEqual(["iron", "oak"]);
    expect([...codes.frame!].sort()).toEqual(["oak", "planks"]);
    expect(shape.textures!.chute).toBe("game:block/metal/sheet/copper1");
  });

  it("marks the infeed at the head's top, the water at the head's end face and the tailings at the tail's", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "infeedCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "up" });
    expect(anchors.find((a) => a.key === "waterCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "north" });
    expect(anchors.find((a) => a.key === "tailingsCell")).toMatchObject({ kind: "cell", pos: [0, 0, 2], face: "south" });
    for (const key of ["feedSpout", "waterSpout", "concentrate"]) expect(anchors.find((a) => a.key === key)).toMatchObject({ kind: "point" });
    expect((rig.cells as { pos: number[] }[]).map((c) => c.pos)).toEqual([[0, 0, 0], [0, 0, 1], [0, 0, 2]]);
  });
});

describe("the sluice's page", () => {
  const model = manifest.models.find((m) => m.id === "sluice")!;
  const scenario = model.scenario!;

  it("shows one state of the concentrate at a time, from a select that waits on the riffles", () => {
    const groups = stateGroups(scenario.states, REQUIRES);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([["concentrate", ["concentrate1", "concentrate2", "concentrate3"]]]);
    const spec = groups[0]!.spec;
    expect(spec.options.map((o) => o.id)).toEqual(["empty", "third", "twothirds", "full"]);
    const all = Object.fromEntries(REQUIRES.map((r) => [r, true]));
    const at = (id: string) => applyState(all, spec.options.find((o) => o.id === id)!, groups[0]!.owns);
    expect(at("empty")).toMatchObject({ concentrate1: false, concentrate2: false, concentrate3: false, riffles: true });
    expect(at("third")).toMatchObject({ concentrate1: true, concentrate2: false, concentrate3: false });
    expect(at("full")).toMatchObject({ concentrate1: false, concentrate2: false, concentrate3: true });
    const { fitted } = openingFitted(REQUIRES, scenario.choices ?? [], groups);
    expect(fitted).toMatchObject({ concentrate2: true, concentrate1: false, concentrate3: false, pipecopper: true, pipelead: false });
    // without the riffles the concentrate waits, and is not drawn
    expect(needsMet(groups[0]!, { ...fitted, riffles: false })).toBe(false);
    expect(statesShow("concentrate2", { ...fitted, riffles: false }, groups)).toBe(false);
    expect(statesShow("concentrate2", fitted, groups)).toBe(true);
  });

  it("draws the water inlet in one metal at a time", () => {
    expect(scenario.choices).toEqual([{ label: "Water inlet", values: ["pipecopper", "pipelead"], default: "pipecopper" }]);
  });
});
