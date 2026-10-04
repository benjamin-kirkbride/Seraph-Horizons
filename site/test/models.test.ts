import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { cellBoxes, discoverAnchors, footprintBounds, humanize, sideArrow, type Anchor } from "../src/lib/model-anchors.ts";
import { checkManifest, publishModels, type ManifestModel } from "../src/lib/model-manifest.ts";
import { advance, checkScenario, contactDepth, enterPhase, propBox, rigNumber, startPhase, type Motion, type PlayContext, type Scenario } from "../src/lib/model-scenario.ts";
import { STATIC_COLOUR, buildModelView, elementDetails, modelBounds, textureColour } from "../src/lib/model-view.ts";
import type { Rig, Shape } from "../src/lib/rig.ts";

const repo = (path: string) => fileURLToPath(new URL(`../../${path}`, import.meta.url));
const readRepoJson = (path: string) => {
  if (!existsSync(repo(path))) throw new Error(`${path} does not exist`);
  return JSON.parse(readFileSync(repo(path), "utf8")) as unknown;
};
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const mill = manifest.models.find((m) => m.id === "bucking-sawmill")!;
const millRig = readRepoJson(mill.rig!) as Rig;
const millShape = readRepoJson(mill.shape) as Shape;

describe("site/models.json", () => {
  it("publishes every model from files that exist and check out", () => {
    const { index, files } = publishModels(manifest, readRepoJson);
    expect(index.models.map((m) => m.id)).toEqual(manifest.models.map((m) => m.id));
    for (const f of files) expect(existsSync(repo(f.source)), f.source).toBe(true);
    const m = index.models.find((x) => x.id === "bucking-sawmill")!;
    expect(m.shape).toBe("models/bucking-sawmill/shape.json");
    expect(m.rig).toBe("models/bucking-sawmill/rig.json");
    expect(m.source.shape).toBe(mill.shape);
    expect(m.elements).toBe(millShape.elements.length);
    expect(m.parts).toBe(millRig.parts!.length);
    expect(m.credit).toMatch(/Bobrik00/);
    expect(m.credit).toMatch(/permission/);
  });

  it("rejects a broken manifest with every problem named", () => {
    expect(() => checkManifest({})).toThrow(/"models" array/);
    const bad = {
      models: [
        { id: "A b", title: "", description: "d", credit: "c", shape: "../outside.json" },
        { id: "x", title: "t", description: "d", credit: "c", shape: "a.json", scenario: {} },
        { id: "x", title: "t", description: "d", credit: "c", shape: "a.json", creditUrl: "http://insecure" },
      ],
    };
    let message = "";
    try {
      checkManifest(bad);
    } catch (e) {
      message = (e as Error).message;
    }
    expect(message).toMatch(/models\[0\].*id must be/);
    expect(message).toMatch(/title is required/);
    expect(message).toMatch(/shape must be a \.json path inside the repository/);
    expect(message).toMatch(/a scenario needs a rig/);
    expect(message).toMatch(/id is used twice/);
    expect(message).toMatch(/creditUrl must be an https URL/);
  });

  it("refuses files the viewer could not read", () => {
    const model = { id: "m", title: "t", description: "d", credit: "c", shape: "s.json", rig: "r.json" };
    const files: Record<string, unknown> = {
      "s.json": { elements: [{ name: "a", from: [0, 0, 0], to: [1, 1, 1] }] },
      "r.json": { parts: [{ id: "a", match: ["*"], drivers: [{ type: "spin", axis: "x" }] }] },
    };
    expect(() => publishModels({ models: [model] }, (p) => files[p])).toThrow(/unknown driver type "spin"/);
    files["r.json"] = { parts: [{ id: "a", match: ["*"], ride: "a" }] };
    expect(() => publishModels({ models: [model] }, (p) => files[p])).toThrow(/cycle/);
    files["s.json"] = { nope: [] };
    expect(() => publishModels({ models: [model] }, (p) => files[p])).toThrow(/no "elements" array/);
  });

  it("publishes a model with a shape alone", () => {
    const { index, files } = publishModels(
      { models: [{ id: "box", title: "Box", description: "d", credit: "c", shape: "s.json" }] },
      () => ({ elements: [{ name: "a", from: [0, 0, 0], to: [16, 16, 16] }] }),
    );
    expect(files).toEqual([{ path: "models/box/shape.json", source: "s.json" }]);
    expect(index.models[0]).toMatchObject({ id: "box", shape: "models/box/shape.json", elements: 1, parts: 0 });
    expect(index.models[0]!.rig).toBeUndefined();
  });
});

describe("anchors", () => {
  const { anchors, unrecognised } = discoverAnchors(millRig);
  const byKey = new Map(anchors.map((a) => [a.key, a]));

  it("finds every anchor of the bucking sawmill's rig by its shape", () => {
    expect(anchors.map((a) => [a.kind, a.key])).toEqual([
      ["cell", "powerCell"],
      ["side", "infeedSide"],
      ["side", "outputSide"],
      ["point", "output"],
      ["line", "trunkBed"],
      ["level", "saw.topY"],
      ["level", "saw.bottomY"],
    ]);
    expect(unrecognised).toEqual([]);
    expect(byKey.get("powerCell")).toMatchObject({ face: "west", label: "Power cell (west face)" });
    expect(byKey.get("output")).toMatchObject({ side: "east", label: "Output" });
    expect(byKey.get("infeedSide")!.label).toBe("Infeed (west)");
  });

  it("reports keys it cannot draw, and skips comments", () => {
    const r = discoverAnchors({ _comment: "x", cells: [], parts: [], strange: 5, hopperCell: [1, 2, 3], spoutSide: "up", spoutFace: "nowhere" });
    expect(r.anchors.map((a) => a.key)).toEqual(["hopperCell", "spoutSide"]);
    expect(r.unrecognised).toEqual(["strange", "spoutFace"]);
    expect(humanize("trunkBed")).toBe("Trunk bed");
    expect(humanize("power_cell")).toBe("Power cell");
  });

  it("measures the footprint and turns cell boxes into blocks", () => {
    expect(footprintBounds(millRig.cells)).toEqual({ lo: [-5, 0, -1], hi: [1, 4, 2] });
    expect(footprintBounds([])).toBeNull();
    expect(cellBoxes({ pos: [2, 0, -1], boxes: [[0, 0, 0.5, 1, 0.25, 1]] })).toEqual([{ lo: [2, 0, -0.5], hi: [3, 0.25, 0] }]);
    expect(cellBoxes({ pos: [1, 1, 1] })).toEqual([{ lo: [1, 1, 1], hi: [2, 2, 2] }]);
  });

  it("puts a side's arrow outside that side, on a bed running that way", () => {
    const bounds = footprintBounds(millRig.cells)!;
    const west = sideArrow("west", bounds, anchors);
    expect(west.dir).toEqual([1, -0, -0]);
    expect(west.from[0]).toBeCloseTo(-5 - 1.3);
    expect(west.from[1]).toBeCloseTo(0.5 + 0.5);
    expect(west.from[2]).toBeCloseTo(0.6875);
    const north = sideArrow("north", bounds, []);
    expect(north.from).toEqual([-2, 0.5, -1 - 1.3]);
  });
});

describe("scenario", () => {
  const scenario = mill.scenario!;
  const play = scenario.play!;
  const { anchors } = discoverAnchors(millRig);
  const bed = anchors.find((a): a is Extract<Anchor, { kind: "line" }> => a.kind === "line" && a.key === "trunkBed")!;
  const thick = scenario.prop!.options.find((o) => o.id === "thick")!;

  it("is sound against the rig", () => {
    expect(checkScenario(scenario, millRig, anchors, ["crankshaft", "sash1", "blade", "sash2", "levers"])).toEqual([]);
  });

  it("names what is wrong with a broken one", () => {
    const bad: Scenario = {
      requires: { nothing: "Nothing" },
      prop: { label: "p", on: "nowhere", options: [{ id: "a", label: "a", size: [1, 1, 0] }], default: "b" },
      play: { phases: [{ id: "x", label: "x", to: "contact", next: "y" }] },
    };
    const problems = checkScenario(bad, millRig, anchors, ["crankshaft"]).join("\n");
    for (const re of [/requires label "nothing"/, /prop.on "nowhere"/, /three positive numbers/, /prop.default "b"/, /goes to "y"/, /turns or seconds/, /needs an edge/])
      expect(problems).toMatch(re);
  });

  it("lays a prop on the bed and finds where the saw meets it", () => {
    // the mill shows every trunk as one of two models, as the game does: thin 1×1×4, thick 2×2×5
    expect(scenario.prop!.options.map((o) => [o.id, o.size])).toEqual([["thin", [4, 1, 1]], ["thick", [5, 2, 2]]]);
    const b = propBox(thick, bed);
    // 5 long on x, 2 wide on z, 2 high, underside on the bed
    expect(b.size).toEqual([5, 2, 2]);
    expect(b.centre).toEqual([bed.origin[0], bed.origin[1] + 1, bed.origin[2]]);
    expect(rigNumber(millRig, "saw.topY")).toBe(2.5625);
    expect(() => rigNumber(millRig, "saw.nope")).toThrow(/saw.nope/);
    const top = bed.origin[1] + 2;
    expect(contactDepth(play, millRig, top)).toBeCloseTo((2.5625 - top) / (2.5625 - 0.4375));
    expect(contactDepth(play, millRig, 99)).toBe(0);
  });

  const rest: Motion = { theta: 0, travel: 0, depth: 0, lifting: 0, phase: null, phaseFrom: 0, propOn: false };
  const ctx = (propChosen: boolean, direction = 1): PlayContext => ({
    play,
    direction,
    propChosen,
    contact: propChosen ? contactDepth(play, millRig, bed.origin[1] + 2) : 0,
  });

  it("starts where the pose is: going up, onto a trunk at the top, or sinking", () => {
    expect(startPhase(play, { depth: 0.5, lifting: 1 }, true)).toBe("raising");
    expect(startPhase(play, { depth: 0, lifting: 0 }, true)).toBe("drop");
    expect(startPhase(play, { depth: 0.5, lifting: 0 }, true)).toBe("sinking");
    expect(startPhase(play, { depth: 0, lifting: 0 }, false)).toBe("sinking");
    expect(startPhase(undefined, { depth: 0, lifting: 0 }, false)).toBeNull();
  });

  /** Runs Play in small steps and records each phase change, with the depth at the end of that step. */
  function run(c: PlayContext, seconds: number, from: Motion = rest) {
    let m = enterPhase(c, from, startPhase(c.play, from, c.propChosen)!);
    const seen: [string, number, boolean][] = [[m.phase!, m.depth, m.propOn]];
    for (let t = 0; t < seconds; t += 1 / 60) {
      m = advance(c, m, 1 / 60);
      if (m.phase !== seen.at(-1)![0]) seen.push([m.phase!, m.depth, m.propOn]);
    }
    return { m, seen };
  }

  it("runs the cycle with a trunk: drop onto it, cut through, wind up, take the next", () => {
    const c = ctx(true);
    // 0.4 s to drop, 8 turns to cut, 6 to wind up, at 1.2 s a turn; stop just after the next drop starts
    const { seen } = run(c, 0.4 + 1.2 * (8 + 6) + 0.2);
    expect(seen.map((s) => s[0])).toEqual(["drop", "cutting", "raising", "drop"]);
    expect(seen[0]![2]).toBe(true); // the trunk goes on with the drop
    expect(seen[1]![1]).toBeCloseTo(c.contact, 2); // the cut starts where the saw meets the trunk
    expect(seen[2]![1]).toBeCloseTo(1, 2); // and winds up from the bed
    expect(seen[2]![2]).toBe(false); // the cut trunk is gone
    expect(seen[3]![1]).toBeCloseTo(0, 2);
  });

  it("runs the cycle empty, down and up, and times a raise by turns", () => {
    const c = ctx(false);
    const { m, seen } = run(c, 1.2 * 6 * 2 + 0.5);
    expect(seen.map((s) => s[0])).toEqual(["sinking", "raising", "sinking"]);
    // 6 turns down and 6 up at 1.2 s a turn
    const { m: half } = run(c, 1.2 * 3);
    expect(half.phase).toBe("sinking");
    expect(half.depth).toBeCloseTo(0.5, 1);
    expect(m.lifting).toBe(0);
  });

  it("turns the shaft either way, its travel always growing", () => {
    const back = advance(ctx(false, -1), { ...rest, theta: 1 }, 0.3);
    expect(back.theta).toBeCloseTo(1 - (0.3 * 2 * Math.PI) / 1.2 + 2 * Math.PI); // kept in 0..2π
    expect(back.travel).toBeCloseTo((0.3 * 2 * Math.PI) / 1.2);
    // without a script Play only turns the shaft
    const plain = advance({ play: undefined, direction: 1, propChosen: false, contact: 0 }, { ...rest, depth: 0.3 }, 0.6);
    expect(plain.depth).toBe(0.3);
    expect(plain.theta).toBeCloseTo(Math.PI);
  });
});

describe("model view", () => {
  const view = buildModelView(millShape, millRig, mill.scenario);

  it("groups every element into the rig's parts, with the static frame in its own colour", () => {
    expect(view.parts.map((p) => p.id)).toEqual(millRig.parts!.map((p) => p.id));
    expect(view.parts.reduce((n, p) => n + p.elements.length, 0)).toBe(millShape.elements.length);
    const frame = view.parts.find((p) => p.id === "frame")!;
    expect(frame.moving).toBe(false);
    expect(frame.colour).toBe(STATIC_COLOUR);
    const moving = view.parts.filter((p) => p.moving).map((p) => p.colour);
    expect(new Set(moving).size).toBe(moving.length);
    // f1_blade has no drivers of its own but rides the saw head, so it moves
    expect(view.parts.find((p) => p.id === "f1_blade")!.moving).toBe(true);
  });

  it("generates the controls from the rig: its inputs and requires values, labelled by the scenario", () => {
    expect(view.inputs).toEqual({ theta: true, travel: true, depth: true, lifting: true });
    expect(view.requires).toEqual([
      { value: "crankshaft", label: "Crankshaft" },
      { value: "sash1", label: "Sash 1" },
      { value: "blade", label: "Blade kit" },
      { value: "sash2", label: "Sash 2" },
      { value: "levers", label: "Levers" },
    ]);
    expect(view.textures.map((t) => t.code).sort()).toEqual(["metal", "oak", "rope"]);
    expect(view.textures.find((t) => t.code === "oak")!.path).toBe("game:block/wood/debarked/oak");
  });

  const box: Shape = {
    textures: { plank: "game:block/wood/planks/oak" },
    elements: [
      { name: "base", from: [0, 0, 0], to: [16, 8, 16], faces: { up: { texture: "#plank" } } },
      { name: "lid", from: [0, 8, 0], to: [16, 10, 16], faces: { up: { texture: "#glass" } } },
    ],
  };

  it("works with a shape alone: one static part, textures, no inputs", () => {
    const v = buildModelView(box, null);
    expect(v.hasRig).toBe(false);
    expect(v.parts.map((p) => [p.id, p.elements])).toEqual([["model", [0, 1]]]);
    expect(v.inputs).toEqual({ theta: false, travel: false, depth: false, lifting: false });
    expect(v.requires).toEqual([]);
    expect(v.anchors).toEqual([]);
    expect(v.bounds).toEqual({ lo: [0, 0, 0], hi: [1, 1, 1] });
    expect(v.textures.map((t) => t.code)).toEqual(["glass", "plank"]);
    expect(textureColour("plank", "game:block/wood/planks/oak")).toBe("#b88a58");
  });

  it("puts elements no part claims in an unmatched group", () => {
    const v = buildModelView(box, { parts: [{ id: "lid", match: ["lid"], drivers: [{ type: "feed", axis: "y", travel: 1 }] }] });
    expect(v.parts.map((p) => [p.id, p.elements])).toEqual([
      ["lid", [1]],
      ["unmatched", [0]],
    ]);
  });

  it("describes an element for the pinned panel", () => {
    const i = view.flat.findIndex((f) => f.element.rotationY);
    const rows = elementDetails(view, i);
    expect(rows.map((r) => r.label)).toEqual(expect.arrayContaining(["from", "to", "rotation", "origin"]));
    expect(modelBounds(view.flat).lo[0]).toBeLessThanOrEqual(-5);
  });
});
