import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { checkScenario, classShows, extraGlow, hotGlow, incandescence, progressClasses, sizeNames, type Scenario } from "../src/lib/model-scenario.ts";
import { workOf, type Rig } from "../src/lib/rig.ts";

// Classes past thick in the viewer (the mandrel station's spring is class 3: a job of its own, not the
// lead hollow's), and parts drawn hot as the game draws hot metal.
const repo = (path: string) => JSON.parse(readFileSync(new URL(`../../${path}`, import.meta.url), "utf8")) as unknown;
const manifest = repo("site/models.json") as { models: { id: string; rig?: string; scenario?: Scenario }[] };
const station = manifest.models.find((m) => m.id === "mandrel-station")!;
const rig = repo(station.rig!) as Rig;
const scenario = station.scenario!;
const requires = [...new Set(rig.parts!.map((p) => p.requires).filter((r): r is string => !!r))];
const { anchors } = discoverAnchors(rig);

describe("a rig with a class past thick", () => {
  it("offers each class its work has, and ties each job to its own", () => {
    const path = workOf(rig);
    expect(progressClasses(path)).toEqual([1, 2, 3]);
    expect(sizeNames(scenario.inputs?.size?.names, ["None", "Thin", "Thick"], path)).toEqual(["None", "Lead hollow", "Copper hollow", "Steel rod: a spring"]);
    // without names, the defaults and "Class 3"
    expect(sizeNames(undefined, ["None", "Thin", "Thick"], path)).toEqual(["None", "Thin", "Thick", "Class 3"]);
    expect(sizeNames(undefined, ["None", "Thin", "Thick"], null)).toEqual(["None", "Thin", "Thick"]);
    // the spring shows with its class alone, each hollow with its metal's
    expect([0, 1, 2, 3].map((k) => classShows("rodsteel", k, scenario))).toEqual([false, false, false, true]);
    expect([0, 1, 2, 3].map((k) => classShows("hollowlead", k, scenario))).toEqual([false, true, false, false]);
    expect(checkScenario(scenario, rig, anchors, requires)).toEqual([]);
  });

  it("refuses names short of the rig's classes, a class that is no class, and one the work has no end for", () => {
    const bad = (s: Partial<Scenario>) => checkScenario({ ...scenario, ...s }, rig, anchors, requires);
    expect(bad({ inputs: { ...scenario.inputs, size: { names: ["None", "Lead", "Copper"] } } })).toContainEqual(expect.stringContaining("inputs.size.names"));
    expect(bad({ requiresClass: { ...scenario.requiresClass, rodsteel: "medium" } })).toContainEqual(expect.stringContaining("class must be"));
    expect(bad({ requiresClass: { ...scenario.requiresClass, rodsteel: "4" } })).toContainEqual(expect.stringContaining("no end for class 4"));
    expect(bad({ heat: { requires: ["nothing"], default: 1000 } })).toContainEqual(expect.stringContaining("heat.requires"));
    expect(bad({ heat: { requires: ["rodsteel"], default: 2000, max: 1500 } })).toContainEqual(expect.stringContaining("heat.default"));
  });
});

describe("a rig whose work has one class", () => {
  const mill = manifest.models.find((m) => m.id === "slitting-mill")!;
  const millRig = repo(mill.rig!) as Rig;
  const millScenario = mill.scenario!;
  const millRequires = [...new Set(millRig.parts!.map((p) => p.requires).filter((r): r is string => !!r))];
  const millAnchors = discoverAnchors(millRig).anchors;

  it("offers none and that class only (the slitting mill's plate), and ties the work to it", () => {
    const path = workOf(millRig);
    expect(progressClasses(path)).toEqual([1]);
    expect(sizeNames(millScenario.inputs?.size?.names, ["None", "Thin", "Thick"], path)).toEqual(["None", "Plate"]);
    expect(sizeNames(undefined, ["None", "Thin", "Thick"], path)).toEqual(["None", "Thin"]);
    expect([0, 1, 2].map((k) => classShows("plate", k, millScenario))).toEqual([false, true, false]);
    expect(checkScenario(millScenario, millRig, millAnchors, millRequires)).toEqual([]);
    // a name short of that class, or a class it has no work for, is refused
    const bad = (s: Partial<Scenario>) => checkScenario({ ...millScenario, ...s }, millRig, millAnchors, millRequires);
    expect(bad({ inputs: { ...millScenario.inputs, size: { names: ["None"] } } })).toContainEqual(expect.stringContaining("inputs.size.names"));
    expect(bad({ requiresClass: { plate: "thick" } })).toContainEqual(expect.stringContaining("no end for class thick"));
  });
});

describe("hot metal, as the game draws it", () => {
  it("is vanilla's incandescence colour (ColorUtil.GetIncandescenceColorAsColor4f)", () => {
    expect(incandescence(20)).toEqual([0, 0, 0, 0]);
    expect(incandescence(499)).toEqual([0, 0, 0, 0]);
    expect(incandescence(500)).toEqual([0, 0, 0, 0]);
    expect(incandescence(751)).toEqual([251 / 400, 0, 0, 1]);
    expect(incandescence(1000)).toEqual([1, 0.5, 0, 1]);
    expect(incandescence(1200)).toEqual([1, 1, 0.5, 1]);
    // whole degrees, as the game casts the temperature to an int
    expect(incandescence(1000.9)).toEqual(incandescence(1000));
  });

  it("glows as the anvil's and the molds' renderers have it: extraGlow = clamp((t - 550) / 2, 0, 255)", () => {
    expect([20, 549, 551, 751, 1000, 1060, 1061, 1500].map(extraGlow)).toEqual([0, 0, 0, 100, 225, 255, 255, 255]);
    // the standard shader mixes the lit colour towards the glow by extraGlow / 255 times its alpha
    expect(hotGlow(20).mix).toBe(0);
    expect(hotGlow(751)).toEqual({ rgb: [251 / 400, 0, 0], mix: 100 / 255 });
    expect(hotGlow(1000)).toEqual({ rgb: [1, 0.5, 0], mix: 225 / 255 });
  });

  it("is the station's rod's, whose page opens it orange-hot", () => {
    expect(scenario.heat?.requires).toEqual(["rodsteel"]);
    expect(scenario.heat?.default).toBe(1000);
    const g = hotGlow(scenario.heat!.default);
    expect(g.rgb[0]).toBe(1);
    expect(g.rgb[1]).toBeGreaterThan(0.2);
    expect(g.rgb[2]).toBe(0);
    // the rig names the same parts hot
    expect((rig as unknown as { wind: { hot: string[] } }).wind.hot).toEqual(scenario.heat?.requires);
  });
});
