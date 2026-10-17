import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, needsMet, openingFitted, stateGroups, statesShow } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workOf, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The sluice's shipped files (Sluice/tools/make_shape.py). Nothing in it moves: it takes no power, and
// its parts are fitted or drawn while it runs, with no driver, so it has no reference poses; every part's
// matrix is the identity at any input. Its concentrate leaves by chute, continuously: drawn off through a
// slot behind each riffle into a launder under the floor, which pours down the concentrate chute standing
// upright in the third cell, out of its bottom face; the tailings run on to the fourth cell's south face.
// tools/tests/test_sluice_model.py checks the same files' geometry.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/sluice-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/sluice.json") as Shape;
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;
const REQUIRES = ["riffles", "concentrate", "infeedchute", "tailingschute", "concentratechute", "pipecopper", "pipelead"];

const pose = (theta: number): Pose => ({ theta, depth: 0.5, lifting: 1, travel: Math.abs(theta) + 3, work: 2, size: 1, presence: 1, feed: 1, oil: 0.5 });

describe("the sluice's rig", () => {
  it("reads no input, has no work and no power cell, and fits the riffles, three chutes, the water inlet and the concentrate", () => {
    expect(rigInputs(parts)).toEqual({ theta: false, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(rig.powerCell).toBeUndefined();
    expect(rig.fill).toBeUndefined();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null, p.drivers?.length ?? 0])).toEqual([
      ["riffles", "riffles", null, 0],
      ["concentrate", "concentrate", null, 0],
      ["infeedchute", "infeedchute", null, 0],
      ["tailingschute", "tailingschute", null, 0],
      ["concentratechute", "concentratechute", null, 0],
      ["pipecopper", "pipecopper", null, 0],
      ["pipelead", "pipelead", null, 0],
      ["frame", null, null, 0],
    ]);
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
    expect([...codes.concentrate!]).toEqual(["concentrate"]);
    for (const c of ["infeedchute", "tailingschute", "concentratechute"]) expect([...codes[c]!]).toEqual(["chute"]);
    expect([...codes.pipecopper!]).toEqual(["pipecopper"]);
    expect([...codes.pipelead!]).toEqual(["pipelead"]);
    expect([...codes.riffles!].sort()).toEqual(["iron", "oak"]);
    expect([...codes.frame!].sort()).toEqual(["oak", "planks"]);
    expect(shape.textures!.chute).toBe("game:block/metal/sheet/copper1");
  });

  it("marks the infeed at the head's top, the water at its end face, and the two outputs from different cells and faces", () => {
    const { anchors } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "infeedCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "up" });
    expect(anchors.find((a) => a.key === "waterCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "north" });
    expect(anchors.find((a) => a.key === "tailingsCell")).toMatchObject({ kind: "cell", pos: [0, 0, 3], face: "south" });
    expect(anchors.find((a) => a.key === "concentrateCell")).toMatchObject({ kind: "cell", pos: [0, 0, 2], face: "down" });
    for (const key of ["feedSpout", "waterSpout", "concentrateSpout"]) expect(anchors.find((a) => a.key === key)).toMatchObject({ kind: "point" });
    expect(anchors.some((a) => /clean/i.test(a.key))).toBe(false);
    expect((rig.cells as { pos: number[] }[]).map((c) => c.pos)).toEqual([[0, 0, 0], [0, 0, 1], [0, 0, 2], [0, 0, 3]]);
  });
});

describe("the sluice's page", () => {
  const model = manifest.models.find((m) => m.id === "sluice")!;
  const scenario = model.scenario!;

  it("shows the concentrate while it runs, from a select that waits on the riffles", () => {
    const groups = stateGroups(scenario.states, REQUIRES);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([["running", ["concentrate"]]]);
    const spec = groups[0]!.spec;
    expect(spec.options.map((o) => o.id)).toEqual(["stopped", "running"]);
    const all = Object.fromEntries(REQUIRES.map((r) => [r, true]));
    const at = (id: string) => applyState(all, spec.options.find((o) => o.id === id)!, groups[0]!.owns);
    expect(at("stopped")).toMatchObject({ concentrate: false, riffles: true, concentratechute: true });
    expect(at("running")).toMatchObject({ concentrate: true });
    const { fitted } = openingFitted(REQUIRES, scenario.choices ?? [], groups);
    expect(fitted).toMatchObject({ concentrate: true, concentratechute: true, tailingschute: true, pipecopper: true, pipelead: false });
    // without the riffles the concentrate waits, and is not drawn
    expect(needsMet(groups[0]!, { ...fitted, riffles: false })).toBe(false);
    expect(statesShow("concentrate", { ...fitted, riffles: false }, groups)).toBe(false);
    expect(statesShow("concentrate", fitted, groups)).toBe(true);
    expect(model.description).toMatch(/concentrate chute/);
    expect(model.description).toMatch(/bottom face/);
    expect(model.description).not.toMatch(/cleans them up/);
  });

  it("draws the water inlet in one metal at a time", () => {
    expect(scenario.choices).toEqual([{ label: "Water inlet", values: ["pipecopper", "pipelead"], default: "pipecopper" }]);
  });
});
