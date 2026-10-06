import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { cellBoxes, discoverAnchors, footprintBounds, humanize, sideArrow, type Anchor } from "../src/lib/model-anchors.ts";
import { checkManifest, publishModels, type ManifestModel } from "../src/lib/model-manifest.ts";
import {
  advance,
  checkScenario,
  classShows,
  contactDepth,
  defaultInputSpeed,
  enterPhase,
  feedAdvance,
  feedBlocksPerRadian,
  INPUT_SPEED_MAX,
  playTrip,
  propBox,
  rigNumber,
  startPhase,
  type Motion,
  type PlayContext,
  type Scenario,
} from "../src/lib/model-scenario.ts";
import { STATIC_COLOUR, buildModelView, elementDetails, modelBounds, textureColour } from "../src/lib/model-view.ts";
import { partMatrices, trunkPathOf, tripEnd, workEnd, workOf, type Rig, type Shape } from "../src/lib/rig.ts";

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
    expect(textureColour("glass", "game:block/glass/plain")).toBe("#cfe3ea");
    expect(textureColour("oil", "game:block/liquid/honey")).toBe("#c8902a");
    expect(textureColour("coil", null)).not.toBe("#c8902a");
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

// A machine a trunk travels through (the rosser, build/rosser/design.md §2): a small rig with the
// rosser's trunk path in shipped blocks, a part on each kind of new driver, hollow cells and the
// stations, and the scenario site/models.json will give the rosser (w1c-report.md).
describe("a trunk travelling through a machine", () => {
  const rig: Rig = {
    cells: [
      { pos: [0, 0, 0], boxes: [] },
      { pos: [-1, 1, 0], hollow: true },
      { pos: [-2, 1, 0], hollow: true, boxes: [[0, 0, 0, 1, 0.25, 1]] },
    ],
    powerCell: [-9, 3, -2],
    powerFace: "north",
    infeedSide: "west",
    outputSide: "east",
    chute: { pos: [-6.5, 0.25, 3.05] },
    chuteSide: "south",
    trunkPath: {
      origin: [-7, 1.6875, 0.6875],
      axis: "x",
      length: 16,
      nose0: -9.875,
      lengths: { thin: 4, thick: 5 },
      tailStop: -4.25,
      radius: { thin: [0.46875, 0.5625], thick: [0.940625, 1.15625] },
      stations: { treadle: -10.875, breaker: -8.875, drip: -7.875, ring: -7.3125 },
    },
    feed: { blocksPerRadian: 0.1 },
    parts: [
      { id: "main", match: ["main_*"], requires: "shaft", drivers: [{ type: "rotate", axis: "x", pivot: [-7, 3.5, 0.84], ratio: 2.382, input: "travel" }] },
      { id: "cross_in", match: ["cross_*"], requires: "rollsin", drivers: [{ type: "rotate", axis: "z", pivot: [-10.25, 3.03, 0], ratio: 0.238, input: "feed" }] },
      {
        id: "toparm_in",
        match: ["toparm_*"],
        requires: "rollsin",
        drivers: [{ type: "gauge", motion: "rotate", axis: "z", pivot: [-10.25, 3.03, 0], amount: { thin: 0.1, thick: 0.5 }, windows: [{ from: -9.5, to: -8.875, ease: 0.25 }] }],
      },
      { id: "cradle_in", match: ["cradle_*"], requires: null, drivers: [{ type: "gauge", motion: "slide", axis: "y", mode: "present", amount: { thin: 0, thick: -0.46875 } }] },
      { id: "botroll_in", match: ["botroll_*"], requires: "rollsin", ride: "cradle_in", drivers: [{ type: "roll", axis: "z", pivot: [-9.25, 0.97, 0.6875], at: -9.5, ratio: -4 }] },
      { id: "frame", match: ["*"], requires: null },
    ],
  };
  const el = (name: string, x: number) => ({ name, from: [x, 0, 0] as [number, number, number], to: [x + 4, 4, 4] as [number, number, number] });
  const shape: Shape = { elements: [el("main_shaft", -112), el("cross_in", -160), el("toparm_in", -160), el("cradle_in", -150), el("botroll_in", -150), el("post", 0)] };
  const scenario: Scenario = {
    inputs: {
      theta: { label: "Axle angle" },
      reverse: { label: "Axle turns backwards" },
      trunk: { label: "Trunk travel", hint: "0 waiting on the infeed bed; the end, delivered on the outfeed bed" },
      presence: { label: "Trunk presence", hint: "Eases in as a trunk is loaded and out as it is taken away" },
    },
    requires: { shaft: "Input shaft", rollsin: "Infeed rolls" },
    prop: {
      label: "Trunk",
      on: "trunkPath",
      placement: "axis",
      moves: "trunk",
      default: "thick",
      colour: "#8b5a2b",
      options: [
        { id: "thin", label: "Thin (xs to lg), shown 1×1×4", size: [4, 1, 1], class: "thin" },
        { id: "thick", label: "Thick (xl, xxl), shown 2×2×5", size: [5, 2, 2], class: "thick" },
      ],
    },
    play: {
      secondsPerTurn: 1,
      phases: [
        { id: "load", label: "Loaded on the infeed bed", wait: true, seconds: 1, prop: "load", next: "feed", nextWithoutProp: "idle", startIf: { travelling: false } },
        { id: "feed", label: "Feeding through the ring", input: "trunk", to: "end", turns: 40, next: "delivered", startIf: { travelling: true } },
        { id: "delivered", label: "Delivered on the outfeed bed", wait: true, seconds: 1.5, next: "clear" },
        { id: "clear", label: "Taken away", wait: true, seconds: 1, prop: "clear", next: "load" },
        { id: "idle", label: "Turning empty", wait: true, turns: 2, prop: "clear", next: "load" },
      ],
    },
  };
  const play = scenario.play!;
  const path = trunkPathOf(rig)!;
  const { anchors, unrecognised } = discoverAnchors(rig);
  const line = anchors.find((a): a is Extract<Anchor, { kind: "line" }> => a.kind === "line" && a.key === "trunkPath")!;
  const [thin, thick] = scenario.prop!.options as [NonNullable<Scenario["prop"]>["options"][number], NonNullable<Scenario["prop"]>["options"][number]];

  it("publishes, its drivers evaluating on the trunk path", () => {
    const model = { id: "rosser", title: "Rosser", description: "d", credit: "c", shape: "s.json", rig: "r.json", scenario };
    const { index } = publishModels({ models: [model] }, (p) => (p === "s.json" ? shape : rig));
    expect(index.models[0]).toMatchObject({ id: "rosser", parts: 6, elements: 6 });
    const noPath = { ...rig, trunkPath: undefined };
    expect(() => publishModels({ models: [{ ...model, scenario: undefined }] }, (p) => (p === "s.json" ? shape : noPath))).toThrow(/needs the rig's work or trunkPath/);
  });

  it("draws the trunk path with its stations, and the hollow cells with no box of their own", () => {
    expect(anchors.map((a) => [a.kind, a.key])).toEqual([
      ["cell", "powerCell"],
      ["side", "infeedSide"],
      ["side", "outputSide"],
      ["point", "chute"],
      ["side", "chuteSide"],
      ["line", "trunkPath"],
    ]);
    // feed is a number for the game, not a place
    expect(unrecognised).toEqual(["feed"]);
    expect(line.marks!.map((m) => [m.label, m.at])).toEqual([
      ["Treadle", -10.875],
      ["Breaker", -8.875],
      ["Drip", -7.875],
      ["Ring", -7.3125],
    ]);
    expect(rig.cells!.map((c) => cellBoxes(c).length)).toEqual([1, 0, 1]);
    expect(footprintBounds(rig.cells)).toEqual({ lo: [-2, 0, 0], hi: [1, 2, 1] });
  });

  it("is sound against the rig, and names what is wrong with a broken one", () => {
    expect(checkScenario(scenario, rig, anchors, ["shaft", "rollsin"])).toEqual([]);
    const bad: Scenario = {
      prop: { ...scenario.prop!, placement: "middle" as "axis", options: [{ id: "thin", label: "t", size: [3, 1, 1], class: "thin" }, { id: "x", label: "x", size: [5, 2, 2] }] },
      play: {
        presenceRate: 0,
        phases: [
          { id: "a", label: "a", input: "trunk", to: "contact", turns: 1, next: "b" },
          { id: "b", label: "b", wait: true, to: 1, seconds: 1, next: "a" },
        ],
      },
    };
    const problems = checkScenario(bad, rig, anchors, []).join("\n");
    for (const re of [/prop.placement/, /"thin" is 3 long, but trunkPath.lengths.thin is 4/, /"x" needs a class/, /presenceRate/, /"a": a trunk phase's to/, /"b" waits/])
      expect(problems).toMatch(re);
    // a trunk that travels needs a trunk path, and a trunk phase a trunk that travels
    const noPath = { ...rig, trunkPath: undefined };
    expect(checkScenario(scenario, noPath, discoverAnchors(noPath).anchors, []).join("\n")).toMatch(/needs the rig's trunkPath/);
    const still: Scenario = { ...scenario, prop: { ...scenario.prop!, moves: undefined } };
    expect(checkScenario(still, rig, anchors, []).join("\n")).toMatch(/"feed" moves the trunk/);
  });

  it("puts the trunk on the path's axis, its nose at nose0 + T", () => {
    expect(path.nose0).toBe(-9.875);
    expect(tripEnd(path, 2)).toBeCloseTo(170 / 16);
    expect(tripEnd(path, 1)).toBeCloseTo(154 / 16);
    const at0 = propBox(thick, line, { placement: "axis", nose: path.nose0 });
    expect(at0).toEqual({ centre: [-9.875 - 2.5, 1.6875, 0.6875], size: [5, 2, 2] });
    const delivered = propBox(thin, line, { placement: "axis", nose: path.nose0 + tripEnd(path, 1) });
    // delivered, the tail is at tailStop
    expect(delivered.centre[0] - delivered.size[0] / 2).toBeCloseTo(path.tailStop);
    // without a placement, the underside is on the line, as the mill's bed
    expect(propBox(thick, line).centre).toEqual([-7, 1.6875 + 1, 0.6875]);
  });

  const rest: Motion = { theta: 0, travel: 0, depth: 0, lifting: 0, phase: null, phaseFrom: 0, propOn: false, work: 0, size: 0, presence: 0, feed: 0 };
  const ctx = (option: typeof thin | null): PlayContext => ({
    play, direction: 1, propChosen: option !== null, contact: 0, trip: playTrip(scenario.prop, option, path), blocksPerRadian: feedBlocksPerRadian(rig),
  });

  it("starts by loading a trunk, or carries on with one part-way through", () => {
    expect(startPhase(play, rest, true, ctx(thick).trip)).toBe("load");
    expect(startPhase(play, { ...rest, work: 3 }, true, ctx(thick).trip)).toBe("feed");
    expect(startPhase(play, { ...rest, work: tripEnd(path, 2) }, true, ctx(thick).trip)).toBe("load");
    expect(startPhase(play, { ...rest, work: 3 }, false, ctx(null).trip)).toBe("load");
    expect(playTrip(undefined, thick, path)).toBeUndefined();
    expect(playTrip(scenario.prop, null, path)).toEqual({ size: 0, end: 0 });
  });

  /** Runs Play in small steps and records each phase change with the motion at the end of that step. */
  function run(c: PlayContext, seconds: number, from: Motion = rest) {
    let m = enterPhase(c, from, startPhase(c.play, from, c.propChosen, c.trip)!);
    const seen: Motion[] = [m];
    for (let t = 0; t < seconds; t += 1 / 60) {
      m = advance(c, m, 1 / 60);
      if (m.phase !== seen.at(-1)!.phase) seen.push(m);
    }
    return { m, seen };
  }

  it("loads a trunk, feeds it through to the end in its turns, waits, takes it away and loads the next", () => {
    const c = ctx(thick);
    const { seen } = run(c, 1 + 40 + 1.5 + 1 + 0.2);
    expect(seen.map((m) => m.phase)).toEqual(["load", "feed", "delivered", "clear", "load"]);
    const [load, feed, delivered, clear, again] = seen as [Motion, Motion, Motion, Motion, Motion];
    expect(load).toMatchObject({ propOn: true, work: 0, size: 2 });
    // eased in while it waits on the infeed bed
    expect(feed.presence).toBe(1);
    expect(feed.work).toBeLessThan(0.05);
    expect(delivered.work).toBeCloseTo(tripEnd(path, 2), 9);
    // φ followed T through the feed's blocks per radian, as in the game, not the 40 turns of the shaft
    expect(delivered.feed!).toBeCloseTo(tripEnd(path, 2) / 0.1, 6);
    expect(clear.propOn).toBe(false);
    // taken away: it eases out, the class held until it has, and the next goes on at the start
    expect(again).toMatchObject({ propOn: true, work: 0, size: 2 });
    expect(again.feed).toBe(delivered.feed);
  });

  it("feeds at an even pace, the rig's parts following T, φ and p", () => {
    const c = ctx(thin);
    const { m } = run(c, 1 + 20);
    expect(m.phase).toBe("feed");
    expect(m.work).toBeCloseTo(tripEnd(path, 1) / 2, 1);
    expect(m.feed!).toBeCloseTo(m.work! / 0.1, 6);
    const view = buildModelView(shape, rig, scenario);
    expect(view.inputs).toEqual({ theta: false, travel: true, depth: false, lifting: false, work: true, size: true, presence: true, feed: true });
    expect(view.path).toEqual(path);
    const ms = partMatrices(rig.parts!, { theta: m.theta, depth: 0, lifting: 0, travel: m.travel, work: m.work, size: m.size, presence: m.presence, feed: m.feed }, view.order, view.path);
    const cradle = rig.parts!.findIndex((p) => p.id === "cradle_in");
    // a thin trunk leaves the cradle up; the cross shaft has turned with the feed
    expect(ms[cradle]![13]).toBe(0);
    expect(ms[1]).not.toEqual(partMatrices(rig.parts!, { theta: 0, depth: 0, lifting: 0 }, view.order, view.path)[1]);
  });

  it("eases presence in and out at its rate, and holds the class until it is out", () => {
    const c = ctx(thick);
    let m = enterPhase(c, rest, "load");
    m = advance(c, m, 1 / 24);
    expect(m.presence).toBeCloseTo(0.5);
    m = advance(c, m, 1 / 24);
    expect(m.presence).toBe(1);
    m = enterPhase(c, { ...m, work: 3 }, "clear");
    m = advance(c, m, 1 / 24);
    expect(m).toMatchObject({ propOn: false, size: 2, work: 3 });
    expect(m.presence).toBeCloseTo(0.5);
    m = advance(c, m, 1 / 24);
    expect(m).toMatchObject({ presence: 0, size: 0 });
  });

  it("turns empty without a trunk, and moves nothing", () => {
    const c = ctx(null);
    const { seen, m } = run(c, 1 + 2 * 1 + 0.5);
    expect(seen.map((x) => x.phase)).toEqual(["load", "idle", "load"]);
    expect(m).toMatchObject({ propOn: false, work: 0, size: 0, presence: 0, feed: 0 });
  });
});

describe("the feed's travel", () => {
  it("follows the trunk through the rig's blocks per radian, forward only, as the game's FeedAdvance", () => {
    expect(feedAdvance(1, 1.5, 0.1)).toBeCloseTo(5, 9);
    expect(feedAdvance(1.5, 1, 0.1)).toBe(0);
    expect(feedAdvance(0, 1, undefined)).toBe(0);
    expect(feedAdvance(0, 1, 0)).toBe(0);
    expect(feedBlocksPerRadian({ feed: { blocksPerRadian: 0.1 } })).toBe(0.1);
    expect(feedBlocksPerRadian({ feed: 3 })).toBeUndefined();
    expect(feedBlocksPerRadian(null)).toBeUndefined();
  });
});

// A rig whose work is not a trunk (the gear cutter: W counts teeth cut, twelve axle turns a tooth):
// the rig's `work` names W, its unit, step and end per class; the scenario labels it, ties a
// master's set-up to its class, names the classes, and Play moves W with the shaft.
describe("a rig whose work is a quantity, geared to the shaft", () => {
  const cutter = manifest.models.find((m) => m.id === "gear-cutter")!;
  const cutterRig = readRepoJson(cutter.rig!) as Rig;
  const s = cutter.scenario!;
  const work = workOf(cutterRig)!;
  const { anchors: cutterAnchors, unrecognised } = discoverAnchors(cutterRig);
  const requires = [...new Set(cutterRig.parts!.map((p) => p.requires).filter((r): r is string => !!r))];

  it("reads the rig's work: teeth cut, 0 to 12 or 20, and draws nothing for it", () => {
    expect(work).toMatchObject({ kind: "work", name: "teeth cut", unit: "teeth", ends: [0, 12, 20] });
    expect(cutterRig.trunkPath).toBeUndefined();
    expect([0, 1, 2].map((k) => workEnd(work, k))).toEqual([0, 12, 20]);
    expect(cutterAnchors.map((a) => a.key)).not.toContain("work");
    expect(unrecognised).not.toContain("work");
    const view = buildModelView(readRepoJson(cutter.shape) as Shape, cutterRig, s);
    expect(view.inputs).toMatchObject({ work: true, size: true, presence: true, oil: true });
    expect(view.path).toEqual(work);
  });

  it("is sound against the rig, and names what is wrong with a broken one", () => {
    expect(checkScenario(s, cutterRig, cutterAnchors, requires)).toEqual([]);
    const bad: Scenario = {
      inputs: { work: { label: "a" }, trunk: { label: "b" }, size: { names: ["a", "b"] as unknown as [string, string, string] } },
      requiresClass: { nothing: "thin", master: "medium" as "thin" },
      play: { turnsPerWork: "cut.nowhere", secondsPerTurn: 0, phases: [] },
    };
    const problems = checkScenario(bad, cutterRig, cutterAnchors, requires).join("\n");
    for (const re of [
      /label the work as work or trunk, not both/,
      /inputs.size.names/,
      /requiresClass "nothing" names no part/,
      /requiresClass "master": class must be/,
      /play.turnsPerWork: rig has no number at "cut.nowhere"/,
      /play.secondsPerTurn must be above 0/,
    ])
      expect(problems).toMatch(re);
    // a rig with both progress declarations is refused
    expect(checkScenario(s, { ...cutterRig, trunkPath: { origin: [0, 0, 0], axis: "x", length: 1, nose0: 0, lengths: { thin: 1, thick: 1 }, tailStop: 1 } }, cutterAnchors, requires).join("\n")).toMatch(/not both/);
  });

  it("shows a class's own set-up only while that class is chosen", () => {
    expect([0, 1, 2].map((k) => classShows("master", k, s))).toEqual([false, true, false]);
    expect([0, 1, 2].map((k) => classShows("blanklarge", k, s))).toEqual([false, false, true]);
    expect([0, 1, 2].map((k) => classShows("head", k, s))).toEqual([true, true, true]);
    expect(classShows("cover", 0, s)).toBe(true);
    expect(classShows(null, 2, s)).toBe(true);
    expect(classShows("master", 2, undefined)).toBe(true);
  });

  it("moves W with the shaft while playing, either way it turns, and stops at the job's end", () => {
    const turns = rigNumber(cutterRig, s.play!.turnsPerWork!);
    expect(turns).toBe(12);
    const start: Motion = { theta: 0, travel: 0, depth: 0, lifting: 0, phase: null, phaseFrom: 0, propOn: false, work: 0, size: 1, presence: 1, feed: 0, oil: 1 };
    const c = (direction: number): PlayContext => ({ play: s.play, direction, propChosen: false, contact: 0, geared: { turns, end: 12 }, turnsPerSecond: 2 });
    // six axle turns at 2 RPS is 3 s: half a tooth, forward or backward
    for (const dir of [1, -1]) {
      let m = start;
      for (let i = 0; i < 60; i++) m = advance(c(dir), m, 0.05);
      expect(m.work).toBeCloseTo(0.5, 9);
      expect(m.travel).toBeCloseTo(6 * 2 * Math.PI, 9);
      expect(m.oil).toBe(1);
    }
    // it stops at the end, and the shaft turns on
    let m: Motion = { ...start, work: 11.99 };
    m = advance(c(1), m, 3);
    expect(m.work).toBe(12);
    // without gearing, Play moves only the shaft
    expect(advance({ play: s.play, direction: 1, propChosen: false, contact: 0, turnsPerSecond: 1 }, start, 1).work).toBe(0);
  });
});

describe("the input speed", () => {
  const millPlay = mill.scenario!.play!;
  const start: Motion = { theta: 0, travel: 0, depth: 0, lifting: 0, phase: null, phaseFrom: 0, propOn: false };

  it("turns the shaft that many revolutions a second, and 0 stops everything", () => {
    for (const rps of [0.5, 1, 2.5, INPUT_SPEED_MAX]) {
      const m = advance({ play: undefined, direction: 1, propChosen: false, contact: 0, turnsPerSecond: rps }, start, 0.2);
      expect(m.travel).toBeCloseTo(2 * Math.PI * rps * 0.2, 9);
    }
    const stopped = advance({ play: millPlay, direction: 1, propChosen: true, contact: 0.3, turnsPerSecond: 0 }, { ...start, phase: millPlay.phases[0]!.id }, 1);
    expect(stopped).toEqual({ ...start, phase: millPlay.phases[0]!.id });
    // without a speed, the script's own secondsPerTurn (the old fixed timing)
    const legacy = advance({ play: { secondsPerTurn: 2, phases: [] }, direction: 1, propChosen: false, contact: 0 }, start, 1);
    expect(legacy.travel).toBeCloseTo(Math.PI, 9);
  });

  it("starts at 1 RPS, or the script's secondsPerTurn as turns a second, within 0 to 5", () => {
    expect(INPUT_SPEED_MAX).toBe(5);
    expect(defaultInputSpeed(undefined)).toBe(1);
    expect(defaultInputSpeed({ phases: [] })).toBe(1);
    expect(defaultInputSpeed({ secondsPerTurn: 1.25, phases: [] })).toBe(0.8);
    expect(defaultInputSpeed({ secondsPerTurn: 0.1, phases: [] })).toBe(5);
  });
});
