import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { applyState, needsMet, openingFitted, stateGroups } from "../src/lib/model-scenario.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, rideOrder, rigInputs, textureCodes, workOf, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The amalgamation plates' shipped files (AmalgamationPlates/tools/make_shape.py). The plates take no power and
// nothing in them moves: the rig has no driver, so it reads no input and every part's matrix is the identity.
// Its parts are what gameplay fits or draws: the chute sections, the plates and their mercury dressing, the
// amalgam in three states (one drawn at a time), the level-2 trap and the spray pipe in copper or lead.
// tools/tests/test_amalgamationplates_model.py checks the same files against the generator's rules.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/amalgamationplates-rig.json") as Rig & Record<string, unknown>;
const shape = read("assets/seraphhorizons/shapes/block/amalgamationplates.json") as Shape;
const pan = read("assets/seraphhorizons/shapes/block/amalgampan.json") as Shape;
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const parts = rig.parts!;

const pose: Pose = { theta: 1.3, depth: 0, lifting: 0, travel: 1.3, work: 0, size: 0, presence: 0, feed: 0, oil: 0 };
const IDENTITY = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

describe("the amalgamation plates' rig", () => {
  it("reads no input, has no work and no power cell, and every part stands still", () => {
    expect(rigInputs(parts)).toEqual({ theta: false, travel: false, depth: false, lifting: false });
    expect(workOf(rig)).toBeNull();
    expect(rig.powerCell).toBeUndefined();
    expect(parts.map((p) => [p.id, p.requires ?? null, p.ride ?? null, p.drivers?.length ?? 0])).toEqual([
      ["amalgam1", "amalgam1", null, 0],
      ["amalgam2", "amalgam2", null, 0],
      ["amalgam3", "amalgam3", null, 0],
      ["dressing", "dressing", null, 0],
      ["plates", "plates", null, 0],
      ["trap", "trap", null, 0],
      ["chutes", "chutes", null, 0],
      ["pipecopper", "pipecopper", null, 0],
      ["pipelead", "pipelead", null, 0],
      ["frame", null, null, 0],
    ]);
    for (const m of partMatrices(parts, pose, rideOrder(parts), null)) expect(Array.from(m).map((v) => Math.round(v * 1e12) / 1e12)).toEqual(IDENTITY);
  });

  it("gives every element a part and every part an element; the amalgam is the amalgam pan's", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const c of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(c);
    for (const a of ["amalgam1", "amalgam2", "amalgam3"]) expect([...codes[a]!]).toEqual(["amalgam"]);
    expect([...codes.dressing!]).toEqual(["mercury"]);
    expect([...codes.plates!]).toEqual(["copper"]);
    expect([...codes.chutes!]).toEqual(["chute"]);
    expect([...codes.trap!].sort()).toEqual(["iron", "mercury"]);
    expect([...codes.pipecopper!].sort()).toEqual(["pipecopper", "solder"]);
    expect([...codes.pipelead!].sort()).toEqual(["pipelead", "solder"]);
    expect(shape.textures!.amalgam).toBe(pan.textures!.amalgam);
    expect(shape.textures!.mercury).toBe(pan.textures!.mercury);
  });

  it("marks the infeed in the head cell's top face, the water on its west face and the outlet in the foot's end face", () => {
    const { anchors, unrecognised } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "feedCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "up" });
    expect(anchors.find((a) => a.key === "waterCell")).toMatchObject({ kind: "cell", pos: [0, 0, 0], face: "west" });
    expect(anchors.find((a) => a.key === "outputCell")).toMatchObject({ kind: "cell", pos: [0, 0, 2], face: "south" });
    for (const key of ["feedSpout", "spray", "amalgam", "trapWell"]) expect(anchors.find((a) => a.key === key)).toMatchObject({ kind: "point" });
    expect(unrecognised.sort()).toEqual(["amalgamStates", "levels", "pipes"]);
  });
});

describe("the amalgamation plates' page", () => {
  const model = manifest.models.find((m) => m.id === "amalgamation-plates")!;
  const values = ["chutes", "plates", "dressing", "amalgam1", "amalgam2", "amalgam3", "trap", "pipecopper", "pipelead"];
  const groups = stateGroups(model.scenario!.states, values);
  const choices = model.scenario!.choices!;

  it("builds by level, draws one amalgam state at a time on dressed plates, and one pipe metal", () => {
    expect(groups.map((g) => [g.id, g.owns])).toEqual([
      ["level", ["plates", "dressing", "trap"]],
      ["amalgam", ["amalgam1", "amalgam2", "amalgam3"]],
    ]);
    const { fitted, picked } = openingFitted(values, choices, groups);
    expect(picked).toEqual({ level: "level2", amalgam: "building" });
    expect(fitted).toEqual({ chutes: true, plates: true, dressing: true, amalgam1: false, amalgam2: true, amalgam3: false, trap: true, pipecopper: true, pipelead: false });
    const [level, amalgam] = groups;
    const at = (g: typeof level, id: string, from = fitted) => applyState(from, g!.spec.options.find((o) => o.id === id)!, g!.owns, choices);
    expect(at(level, "level1")).toMatchObject({ plates: true, dressing: true, trap: false });
    expect(at(amalgam, "thick")).toMatchObject({ amalgam1: false, amalgam2: false, amalgam3: true });
    expect(needsMet(amalgam!, at(level, "bare"))).toBe(false);
    expect(needsMet(amalgam!, at(level, "level1"))).toBe(true);
  });
});
