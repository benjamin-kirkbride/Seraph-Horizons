import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import { animatedWorlds, jointDeltas, pieceMatrix, poseAt } from "../src/lib/keyframes.ts";
import {
  applyState,
  checkScenario,
  initialFitted,
  needsMet,
  openingFitted,
  stateGroups,
  stateOf,
  statesShow,
  type FittedStates,
  type RequiresChoice,
  type Scenario,
  type StateGroup,
} from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { apply, corners, fitted, multiply, partMatrices, translation, type Mat4, type Rig, type Shape, type Vec3 } from "../src/lib/rig.ts";

const rig: Rig = { cells: [{ pos: [0, 0, 0] }], parts: [] };
const lever: RequiresChoice = { label: "Lever", values: ["left", "right"], default: "right" };
const requires = ["frame", "body", "head", "left", "right"];
const states: FittedStates = {
  label: "Build",
  default: "built",
  options: [
    { id: "empty", label: "Empty", fitted: [] },
    { id: "body", label: "Body", fitted: ["frame", "body"] },
    { id: "built", label: "Built", fitted: ["frame", "body", "head", "left"] },
    { id: "gone", label: "Gone", fitted: [], hint: "Left empty again." },
  ],
};

describe("the scenario's states of fitted parts", () => {
  it("fits a state's values and takes every other off, leaving a choice it does not name as it is", () => {
    const start = initialFitted(requires, [lever]);
    expect(applyState(start, states.options[1]!, requires, [lever])).toEqual({ frame: true, body: true, head: false, left: false, right: true });
    expect(applyState(start, states.options[2]!, requires, [lever])).toEqual({ frame: true, body: true, head: true, left: true, right: false });
    expect(applyState(start, states.options[0]!, requires)).toEqual({ frame: false, body: false, head: false, left: false, right: false });
  });

  it("names the state the parts are in, keeping the one picked when two fit the same parts", () => {
    const empty = applyState(initialFitted(requires, [lever]), states.options[3]!, requires, [lever]);
    expect(stateOf(empty, states, "gone", requires, [lever])).toBe("gone");
    expect(stateOf(empty, states, null, requires, [lever])).toBe("empty");
    expect(stateOf(empty, states, "built", requires, [lever])).toBe("empty");
    // a part ticked by hand: no state
    expect(stateOf({ ...empty, head: true }, states, "gone", requires, [lever])).toBeNull();
    expect(stateOf(empty, undefined, "gone", requires)).toBeNull();
  });

  it("is checked against the rig's requires values and choices", () => {
    expect(checkScenario({ choices: [lever], states }, rig, [], requires)).toEqual([]);
    const bad: Scenario = {
      choices: [lever],
      states: {
        label: "",
        default: "nowhere",
        options: [
          { id: "a", label: "A", fitted: ["frame", "wings"] },
          { id: "a", label: "", fitted: ["left", "right"], hint: "" },
          { id: "", label: "B", fitted: undefined as unknown as string[] },
        ],
      },
    };
    expect(checkScenario(bad, rig, [], requires)).toEqual([
      "states need a label",
      'state "a": "wings" is no part\'s requires value',
      'state "a" is there twice',
      'state "a" needs a label',
      'state "a": its hint must be text',
      'state "a" fits more than one value of choice "Lever"',
      "a state needs an id",
      'state "" needs a fitted list (empty for nothing fitted)',
      'states: default "nowhere" is not one of the states',
    ]);
    expect(checkScenario({ states: { label: "One", options: [{ id: "x", label: "X", fitted: [] }] } }, rig, [], [])).toEqual([
      "states need two options or more",
    ]);
  });
});

describe("independent groups of states", () => {
  const values = ["frame", "chain", "spine", "torso", "head", "left", "right"];
  const groups: FittedStates[] = [
    {
      id: "gantry",
      label: "Gantry",
      default: "hung",
      values: ["frame", "chain", "spine"],
      options: [
        { id: "bare", label: "Bare", fitted: [] },
        { id: "framed", label: "Framed", fitted: ["frame", "chain"] },
        { id: "hung", label: "Hung", fitted: ["frame", "chain", "spine"] },
      ],
    },
    {
      id: "body",
      label: "Body",
      default: "built",
      values: ["torso", "head"],
      needs: ["spine"],
      options: [
        { id: "none", label: "None", fitted: [] },
        { id: "torso", label: "Torso", fitted: ["torso"] },
        { id: "built", label: "Built", fitted: ["torso", "head"] },
        { id: "gone", label: "Gone", fitted: [] },
      ],
    },
  ];
  const lever: RequiresChoice = { label: "Lever", values: ["left", "right"] };
  const gs = stateGroups(groups, values);
  const [gantry, body] = gs as [StateGroup, StateGroup];

  it("reads a lone group as owning every value, and a list's groups as owning their values", () => {
    expect(stateGroups(states, requires)).toEqual([{ id: "states", spec: states, owns: requires }]);
    expect(gs.map((g) => [g.id, g.owns])).toEqual([
      ["gantry", ["frame", "chain", "spine"]],
      ["body", ["torso", "head"]],
    ]);
    expect(stateGroups(undefined, values)).toEqual([]);
  });

  it("opens each group at its default, the values no group owns fitted", () => {
    const { fitted: f, picked } = openingFitted(values, [lever], gs);
    expect(f).toEqual({ frame: true, chain: true, spine: true, torso: true, head: true, left: true, right: false });
    expect(picked).toEqual({ gantry: "hung", body: "built" });
  });

  it("fits a group's state over its own values only, leaving the other group alone", () => {
    const start = openingFitted(values, [lever], gs).fitted;
    const torso = applyState(start, body.spec.options[1]!, body.owns, [lever]);
    expect(torso).toEqual({ ...start, head: false });
    const framed = applyState(start, gantry.spec.options[1]!, gantry.owns, [lever]);
    expect(framed).toEqual({ ...start, spine: false });
    // each group names its own state; ticking a box by hand leaves only that box's group
    expect(stateOf(framed, gantry.spec, null, gantry.owns)).toBe("framed");
    expect(stateOf(framed, body.spec, "built", body.owns)).toBe("built");
    const byHand = { ...framed, head: false };
    expect(stateOf(byHand, body.spec, "built", body.owns)).toBe("torso");
    expect(stateOf({ ...framed, torso: false }, body.spec, "built", body.owns)).toBeNull();
    expect(stateOf({ ...framed, torso: false }, gantry.spec, "framed", gantry.owns)).toBe("framed");
    // two states fitting the same parts: the one picked is kept
    const gone = applyState(start, body.spec.options[3]!, body.owns);
    expect(stateOf(gone, body.spec, "gone", body.owns)).toBe("gone");
    expect(stateOf(gone, body.spec, null, body.owns)).toBe("none");
  });

  it("does not draw a group's values while it waits on its needs, whatever their boxes say", () => {
    const start = openingFitted(values, [lever], gs).fitted;
    const framed = applyState(start, gantry.spec.options[1]!, gantry.owns);
    expect(needsMet(body, start)).toBe(true);
    expect(needsMet(body, framed)).toBe(false);
    expect(needsMet(gantry, framed)).toBe(true);
    expect(statesShow("torso", framed, gs)).toBe(false);
    expect(statesShow("head", framed, gs)).toBe(false);
    expect(statesShow("chain", framed, gs)).toBe(true);
    expect(statesShow("left", framed, gs)).toBe(true);
    expect(statesShow(null, framed, gs)).toBe(true);
    // the body's state is kept while it waits: fitting the spine again shows it as it was
    expect(framed.torso && framed.head).toBe(true);
    expect(statesShow("torso", { ...framed, spine: true }, gs)).toBe(true);
  });

  it("is checked: values in one group each, options naming only their group's, needs and defaults valid", () => {
    expect(checkScenario({ choices: [lever], states: groups }, rig, [], values)).toEqual([]);
    const bad: FittedStates[] = [
      {
        id: "a",
        label: "A",
        default: "x",
        values: ["frame", "chain", "wings"],
        needs: ["chain", "nothing"],
        options: [
          { id: "x", label: "X", fitted: ["frame", "torso"] },
          { id: "y", label: "Y", fitted: ["chain"] },
        ],
      },
      {
        id: "a",
        label: "",
        default: "z",
        values: ["chain", "torso"],
        needsHint: "Fit it",
        options: [{ id: "x", label: "X", fitted: [] }],
      },
      { label: "No id", options: [] } as unknown as FittedStates,
    ];
    expect(checkScenario({ states: bad }, rig, [], values)).toEqual([
      'state group "a": "wings" is no part\'s requires value',
      'state group "a": state "x": "torso" is not one of the group\'s values',
      'state group "a" is there twice',
      'state group "a": "chain" is group "a"\'s too',
      'state group "a": needs a label',
      'state group "a": needs two options or more',
      'state group "a": default "z" is not one of its states',
      "a state group needs an id",
      "a state group with no id: needs its values, the requires values its states fit or take off",
      "a state group with no id: needs two options or more",
      'state group "a": it needs "chain", one of its own values',
      'state group "a": it needs "nothing", which is no part\'s requires value',
      'state group "a": needsHint must be text, with needs',
    ]);
    expect(checkScenario({ states: [] }, rig, [], values)).toEqual(["states: a list needs one group or more"]);
    expect(checkScenario({ states: { ...states, needs: ["frame"] } }, rig, [], requires)).toEqual(["states: values, needs and needsHint are for a list of groups"]);
  });
});

const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
const read = (path: string) => JSON.parse(readFileSync(new URL(`../../${path}`, import.meta.url), "utf8")) as unknown;

describe("the eidolon gantry's build states", () => {
  const gantry = manifest.models.find((m) => m.id === "eidolon-gantry")!;
  const view = buildModelView(read(gantry.shape) as Shape, read(gantry.rig!) as Rig, gantry.scenario);
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(gantry.scenario!.states, values);
  const group = (id: string) => groups.find((g) => g.id === id)!;
  const opening = openingFitted(values, [], groups);
  // The parts drawn after picking these states (group id: state id) from the page's opening.
  const shown = (picks: Record<string, string>) => {
    let ticked = opening.fitted;
    for (const [g, id] of Object.entries(picks)) ticked = applyState(ticked, group(g).spec.options.find((o) => o.id === id)!, group(g).owns);
    return view.parts.filter((p) => fitted(p.part.requires, ticked) && statesShow(p.part.requires, ticked, groups)).map((p) => p.part.id);
  };

  const winch = ["axles", "crankshaft", "gears", "drum", "strapping", "ratchet", "crank", "chain"];
  const body = ["torso", "pelvis", "legs", "arms", "head", "mind"];

  it("has two selects, the gantry's and the eidolon's, opening both fully built", () => {
    expect(groups.map((g) => [g.id, g.spec.label, g.spec.default])).toEqual([
      ["gantry", "Gantry", "spine"],
      ["eidolon", "Eidolon", "built"],
    ]);
    expect(group("gantry").owns).toEqual([...winch, "spine"]);
    expect(group("eidolon").owns).toEqual(body);
    expect(values).toEqual([...winch, "spine", ...body]);
    expect(Object.values(opening.fitted).every(Boolean)).toBe(true);
    expect(opening.picked).toEqual({ gantry: "spine", eidolon: "built" });
    expect(checkScenario(gantry.scenario!, read(gantry.rig!) as Rig, view.anchors, values)).toEqual([]);
  });

  it("steps the gantry from the bare frame through each winch stage to the spine on the chain", () => {
    const st = group("gantry").spec;
    expect(st.options.map((o) => o.id)).toEqual(["frame", ...winch, "spine"]);
    // the frame alone, then each stage added to the ones before
    expect(st.options.map((o) => o.fitted)).toEqual(st.options.map((_, i) => [...winch, "spine"].slice(0, i)));
  });

  it("steps the eidolon from none through each body stage to fully built, then departed", () => {
    const st = group("eidolon").spec;
    expect(st.options.map((o) => o.id)).toEqual(["none", ...body.slice(0, -1), "built", "departed"]);
    expect(st.options.slice(0, -1).map((o) => o.fitted)).toEqual(st.options.slice(0, -1).map((_, i) => body.slice(0, i)));
    expect(st.options.at(-1)!.fitted).toEqual([]);
    expect(st.needs).toEqual(["chain", "spine"]);
  });

  it("shows the bare frame, then the winch stage by stage, with no body until the spine hangs", () => {
    expect(shown({ gantry: "frame" })).toEqual(["frame"]);
    expect(shown({ gantry: "axles" })).toEqual(expect.arrayContaining(["frame", "layshaft", "drumshaft"]));
    expect(shown({ gantry: "axles" })).not.toContain("laygears");
    expect(shown({ gantry: "gears" })).toEqual(expect.arrayContaining(["crank", "cranklantern", "laygears", "drumwheel"]));
    expect(shown({ gantry: "gears" })).not.toContain("drum");
    expect(shown({ gantry: "chain" })).toEqual(expect.arrayContaining(["ring", "hook", "fall", "lead", "coil"]));
    // the eidolon's select still says fully built, but nothing of the body is drawn without the spine
    for (const g of ["frame", "gears", "chain"]) {
      expect(needsMet(group("eidolon"), applyState(opening.fitted, group("gantry").spec.options.find((o) => o.id === g)!, group("gantry").owns))).toBe(false);
      for (const stage of [...body, "spine"]) expect(shown({ gantry: g })).not.toContain(stage);
    }
    expect(shown({ gantry: "spine" })).toEqual(expect.arrayContaining(["spine", ...body]));
  });

  it("builds the body on the spine stage by stage, and leaves the spine hanging empty once departed", () => {
    expect(shown({ eidolon: "torso" })).toEqual(expect.arrayContaining(["spine", "torso"]));
    expect(shown({ eidolon: "torso" })).not.toContain("pelvis");
    const departed = shown({ eidolon: "departed" });
    expect(departed).toEqual(expect.arrayContaining(["spine", "ring", "hook", "frame", "crank", "drum", "sheave"]));
    for (const stage of body) expect(departed).not.toContain(stage);
    expect(departed).toEqual(shown({ eidolon: "none" }));
    // the eidolon's pick is kept while the gantry is taken back and built up again
    expect(shown({ eidolon: "legs", gantry: "frame" })).toEqual(["frame"]);
    expect(shown({ gantry: "frame", eidolon: "legs" })).toEqual(["frame"]);
    expect(shown({ eidolon: "legs" })).toEqual(expect.arrayContaining(["torso", "pelvis", "legs"]));
  });
});

describe("the eidolon's page: its stages with any animation", () => {
  const model = manifest.models.find((m) => m.id === "eidolon")!;
  const shape = read(model.shape) as Shape;
  const rigFile = read(model.rig!) as Rig;
  const view = buildModelView(shape, rigFile, model.scenario);
  const stages = (read("mods-src/seraphhorizons/assets/seraphhorizons/config/eidolon-stages.json") as { stages: { code: string; elements: string[] }[] }).stages;
  const body = ["torso", "pelvis", "legs", "arms", "head", "mind"];
  const values = view.requires.map((r) => r.value);
  const groups = stateGroups(model.scenario!.states, values);
  const partId = (ei: number) => view.parts[view.elementPart[ei]!]!.id;
  const byName = new Map(view.flat.map((f) => [f.element.name, f.index]));

  it("has a part per stage, every element in its own stage's, none moving", () => {
    expect(view.parts.map((p) => p.id).sort()).toEqual([...body].sort());
    for (const st of stages) for (const n of st.elements) expect(partId(byName.get(n)!), n).toBe(st.code);
    expect(view.parts.every((p) => !p.moving)).toBe(true);
    // a static rig colours each stage its own
    expect(new Set(view.parts.map((p) => p.colour)).size).toBe(body.length);
    const matrices = partMatrices(view.parts.map((p) => p.part), { theta: 1, depth: 0.5, lifting: 1 }, view.order);
    for (const m of matrices) expect(m).toEqual([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
  });

  it("has a build-state select from the torso to fully built, opening fully built", () => {
    expect(groups.map((g) => [g.spec.label, g.owns, g.spec.default])).toEqual([["Build state", body, "built"]]);
    expect(groups[0]!.spec.options.map((o) => o.fitted)).toEqual(body.map((_, i) => body.slice(0, i + 1)));
    expect(checkScenario(model.scenario!, rigFile, view.anchors, values)).toEqual([]);
    expect(Object.values(openingFitted(values, [], groups).fitted).every(Boolean)).toBe(true);
  });

  // Where the scene draws a shown element's corners (blocks): its piece's matrix, the part's times its
  // joint's motion, on its rest corners; and where the game puts them, posing the whole body.
  const drawn = (ei: number, deltas: Map<number, Mat4>) => {
    const j = view.animation.joints[ei]!;
    const m = pieceMatrix(identity, j >= 0 ? deltas.get(j) : undefined);
    return corners(view.flat[ei]!).map((c) => apply(m, [c[0] / 16, c[1] / 16, c[2] / 16]));
  };
  const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  const game = (worlds: Mat4[], ei: number) => {
    const e = view.flat[ei]!.element;
    const m = multiply(worlds[ei]!, translation(-e.from[0], -e.from[1], -e.from[2]));
    return corners(view.flat[ei]!, m).map((c) => [c[0] / 16, c[1] / 16, c[2] / 16] as Vec3);
  };

  const cases: [string, string[]][] = [
    ["the head alone: its joints hang from the hidden chest and hip blocks", ["head"]],
    ["the torso alone: the hip block it hangs from hidden, the head and arms on it hidden", ["torso"]],
    ["the legs and the mind: two pieces, each moved by hidden joints", ["legs", "mind"]],
    ["everything but the torso", ["pelvis", "legs", "arms", "head", "mind"]],
  ];

  for (const code of ["fell", "activate", "stand-walk", "trunk-thick-pickup"]) {
    it(`draws every shown element where the game poses it, in ${code}, whatever stages are shown`, () => {
      const anim = view.animation.animations.find((a) => a.code === code)!;
      expect(anim).toBeDefined();
      for (const frame of [0, Math.floor(anim.frames / 3), Math.floor((2 * anim.frames) / 3) + 0.5]) {
        const deltas = jointDeltas(view.animation, view.flat, anim, frame);
        const worlds = animatedWorlds(view.flat, view.animation.parents, poseAt(anim, frame), anim.version);
        for (const [, shownStages] of cases) {
          const ticked = Object.fromEntries(body.map((b) => [b, shownStages.includes(b)]));
          const visible = view.parts.map((p) => fitted(p.part.requires, ticked) && statesShow(p.part.requires, ticked, groups));
          let checked = 0;
          view.flat.forEach((f, ei) => {
            if (!visible[view.elementPart[ei]!]) return;
            const got = drawn(ei, deltas);
            const want = game(worlds, ei);
            for (let k = 0; k < 8; k++) for (let a = 0; a < 3; a++) expect(got[k]![a]!).toBeCloseTo(want[k]![a]!, 6);
            checked++;
          });
          expect(checked).toBeGreaterThan(0);
        }
      }
    });
  }

  it("exercises both: a shown element moved by a joint in a hidden stage, and a shown joint over hidden elements", () => {
    const parents = view.animation.parents;
    const joints = view.animation.joints;
    // the head alone: its elements move with joints whose ancestors (the chest, the hips) are hidden
    const head = view.flat.filter((f) => partId(f.index) === "head");
    const hiddenAbove = head.filter((f) => {
      for (let p = parents[f.index]!; p >= 0; p = parents[p]!) if (partId(p) !== "head" && joints.includes(p)) return true;
      return false;
    });
    expect(hiddenAbove.length).toBeGreaterThan(0);
    // some shown element's own joint is an element of a hidden stage
    const borrowed = view.flat.filter((f) => joints[f.index]! >= 0 && partId(joints[f.index]!) !== partId(f.index));
    expect(borrowed.length).toBeGreaterThan(0);
    // the torso alone: the chest is a joint, and elements moved by it are in the hidden head or arms
    const chest = byName.get("chest-inside")!;
    expect(view.animation.jointList).toContain(chest);
    const under = view.flat.filter((f) => {
      for (let p = parents[f.index]!; p >= 0; p = parents[p]!) if (p === chest) return true;
      return false;
    });
    expect(under.some((f) => partId(f.index) !== "torso")).toBe(true);
  });
});
