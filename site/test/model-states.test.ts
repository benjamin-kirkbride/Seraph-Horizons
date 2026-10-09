import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { ManifestModel } from "../src/lib/model-manifest.ts";
import {
  applyState,
  checkScenario,
  initialFitted,
  stateOf,
  type FittedStates,
  type RequiresChoice,
  type Scenario,
} from "../src/lib/model-scenario.ts";
import { buildModelView } from "../src/lib/model-view.ts";
import { fitted, type Rig, type Shape } from "../src/lib/rig.ts";

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

describe("the eidolon gantry's build states", () => {
  const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: ManifestModel[] };
  const gantry = manifest.models.find((m) => m.id === "eidolon-gantry")!;
  const read = (path: string) => JSON.parse(readFileSync(new URL(`../../${path}`, import.meta.url), "utf8")) as unknown;
  const view = buildModelView(read(gantry.shape) as Shape, read(gantry.rig!) as Rig, gantry.scenario);
  const values = view.requires.map((r) => r.value);
  const shown = (id: string) => {
    const state = gantry.scenario!.states!.options.find((o) => o.id === id)!;
    const ticked = applyState(initialFitted(values), state, values);
    return view.parts.filter((p) => fitted(p.part.requires, ticked)).map((p) => p.part.id);
  };

  const winch = ["axles", "crankshaft", "gears", "drum", "strapping", "ratchet", "crank", "chain"];
  const body = ["torso", "pelvis", "legs", "arms", "head", "mind"];

  it("steps through the build, one stage at a time, opening fully built", () => {
    const st = gantry.scenario!.states!;
    expect(st.default).toBe("built");
    expect(st.options.map((o) => o.id)).toEqual(["frame", ...winch, "spine", ...body.slice(0, -1), "built", "departed"]);
    // the frame alone, then each stage added to the ones before, fully built last before "departed"
    expect(st.options.slice(0, -1).map((o) => o.fitted.length)).toEqual(st.options.slice(0, -1).map((_, i) => i));
    expect(st.options.at(-2)!.fitted).toEqual(values);
    expect(values).toEqual([...winch, "spine", ...body]);
  });

  it("shows the bare frame, then the winch stage by stage", () => {
    expect(shown("frame")).toEqual(["frame"]);
    expect(shown("axles")).toEqual(expect.arrayContaining(["frame", "layshaft", "drumshaft"]));
    expect(shown("axles")).not.toContain("laygears");
    expect(shown("gears")).toEqual(expect.arrayContaining(["crank", "cranklantern", "laygears", "drumwheel"]));
    expect(shown("gears")).not.toContain("drum");
    expect(shown("chain")).toEqual(expect.arrayContaining(["ring", "hook", "fall", "lead", "coil"]));
    expect(shown("chain")).not.toContain("spine");
  });

  it("shows the spine hanging empty from the ring once the eidolon has departed", () => {
    const departed = shown("departed");
    expect(departed).toEqual(expect.arrayContaining(["spine", "ring", "hook", "frame", "crank", "drum", "sheave"]));
    for (const stage of body) expect(departed).not.toContain(stage);
    expect(departed).toEqual(shown("spine"));
    expect(shown("built")).toEqual(expect.arrayContaining(body));
  });
});
