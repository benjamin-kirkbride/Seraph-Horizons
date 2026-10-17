import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { discoverAnchors } from "../src/lib/model-anchors.ts";
import { checkScenario, pickedTextures, stateGroups, type Scenario } from "../src/lib/model-scenario.ts";
import { buildModelView, withPickedTextures } from "../src/lib/model-view.ts";
import { compileGlobs, flattenShape, partMatrices, partOf, requiresValues, rigInputs, rideOrder, textureCodes, thetaTurns, workOf, type Mat4, type Pose, type Rig, type Shape } from "../src/lib/rig.ts";

// The rocker's shipped files, and the poses Rocker/tools/make_shape.py computes from them with machinegen's
// reference maths. Its rig reads θ alone, the rocking (the hold-to-work clock): the cradle swings about its
// rocking axis and slides as its rockers roll, the load rides it, the water rides it but swings back so it
// stays level, and nothing stands still: the rockers roll on the block below. It takes water by bucket only: no water cell, no spout. A hand
// station, it has no build stages: its only requires are the three states. tools/tests/test_rocker_model.py
// replays the same file in Python.
const MOD = "../../mods-src/seraphhorizons/";
const read = (path: string) => JSON.parse(readFileSync(new URL(MOD + path, import.meta.url), "utf8")) as unknown;
const rig = read("assets/seraphhorizons/config/rocker-rig.json") as Rig;
const shape = read("assets/seraphhorizons/shapes/block/rocker.json") as Shape;
type RefPose = { theta: number; matrices: Record<string, number[][]> };
const reference = read("tests/Rocker/rig-reference.json") as { poses: RefPose[] };
const manifest = JSON.parse(readFileSync(new URL("../models.json", import.meta.url), "utf8")) as { models: { id: string; scenario?: Scenario }[] };
const parts = rig.parts!;
const order = rideOrder(parts);

const rows = (m: Mat4) => [0, 1, 2].map((r) => [0, 1, 2, 3].map((c) => m[c * 4 + r]!));
const at = (theta: number): Pose => ({ theta, depth: 0, lifting: 0, travel: Math.abs(theta), work: 0, size: 0, presence: 0, feed: 0, oil: 0 });

describe("the rocker's rig against its rig-reference.json", () => {
  it("reads θ alone, one rock a turn, and has a matrix for every part", () => {
    expect(workOf(rig)).toBeNull();
    for (const p of reference.poses) expect(Object.keys(p.matrices).sort()).toEqual(parts.map((q) => q.id).sort());
    // the viewer shows θ (so Play) and nothing else; a hand station: no power cell
    expect(rigInputs(parts)).toEqual({ theta: true, travel: false, depth: false, lifting: false });
    expect(thetaTurns(parts)).toBe(1);
    expect((rig as unknown as Record<string, unknown>).powerCell).toBeUndefined();
  });

  it("matches every part's matrix at every reference pose", () => {
    let worst = 0;
    for (const p of reference.poses) {
      const ms = partMatrices(parts, at(p.theta), order, null);
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
    expect(worst).toBeLessThanOrEqual(1e-6 + 1e-12);
  });

  it("rocks the load with the cradle and keeps the water level, with nothing standing still", () => {
    const ms = partMatrices(parts, at(1.1), order, null);
    const cradle = ms[parts.findIndex((p) => p.id === "cradle")]!;
    parts.forEach((p, i) => {
      if (p.ride === "cradle" && p.id !== "water") ms[i]!.forEach((v, j) => expect(Math.abs(v - cradle[j]!)).toBeLessThan(1e-12));
      if (p.ride == null && p.id !== "cradle") ms[i]!.forEach((v, j) => expect(Math.abs(v - (j % 5 === 0 ? 1 : 0))).toBeLessThan(1e-12));
    });
    expect(parts.filter((p) => p.ride == null).map((p) => p.id)).toEqual(["cradle"]);
    // the water: no turn at all, column-major 3x3 the identity, though the cradle under it leans
    const water = ms[parts.findIndex((p) => p.id === "water")]!;
    for (const j of [0, 1, 2, 4, 5, 6, 8, 9, 10]) expect(Math.abs(water[j]! - (j % 5 === 0 ? 1 : 0))).toBeLessThan(1e-12);
    expect(Math.abs(cradle[6]!)).toBeGreaterThan(0.1);
  });

  it("gives every element a part and every part an element, each in its own textures", () => {
    const flat = flattenShape(shape.elements);
    const globs = compileGlobs(parts);
    const used = new Set(flat.map((f) => partOf(globs, f.chain)));
    expect(used.has(-1)).toBe(false);
    expect(used.size).toBe(parts.length);
    const codes: Record<string, Set<string>> = {};
    for (const f of flat) for (const code of textureCodes(f.element)) (codes[parts[partOf(globs, f.chain)]!.id] ??= new Set()).add(code);
    expect([...codes.cradle!].sort()).toEqual(["canvas", "oak", "riddle"]);
    expect([...codes.water!]).toEqual(["water"]);
    expect([...codes.charge!]).toEqual(["charge"]);
    expect([...codes.concentrate!]).toEqual(["concentrate"]);
  });
});

describe("the rocker's anchors and scenario", () => {
  it("names the operator's side, the tailings beyond the foot and the points that ride the cradle, and no transfer side or cell", () => {
    const { anchors, unrecognised } = discoverAnchors(rig);
    expect(anchors.find((a) => a.key === "operatorSide")).toMatchObject({ kind: "side", side: "north" });
    expect(unrecognised).toEqual(["rock"]);
    expect(anchors.filter((a) => a.kind === "cell")).toEqual([]);
    // a hand station: no side but the operator's, for no chute or hopper takes from it or feeds it
    expect(anchors.filter((a) => a.kind === "side").map((a) => a.key)).toEqual(["operatorSide"]);
    expect(anchors.find((a) => a.key === "tailings")).toMatchObject({ kind: "point", pos: [-0.5, 0, 0.5] });
    for (const key of ["pour", "outflow", "concentrate"]) expect(anchors.find((a) => a.key === key)).toMatchObject({ kind: "point", part: "cradle" });
    expect(anchors.find((a) => a.key === "spout")).toBeUndefined();
  });

  it("has a water state (wet, opened on, as Play rocks it) and a load state, and fits the rig", () => {
    const scenario = manifest.models.find((m) => m.id === "rocker")!.scenario!;
    const requires = requiresValues(parts);
    expect(checkScenario(scenario, rig, discoverAnchors(rig).anchors, requires)).toEqual([]);
    const groups = stateGroups(scenario.states, requires);
    expect(groups.map((g) => [g.id, g.owns])).toEqual([
      ["water", ["water"]],
      ["load", ["charge", "concentrate"]],
    ]);
    const water = groups[0]!.spec as { default?: string; options: { id: string; fitted: string[] }[] };
    expect(water.options.map((o) => [o.id, o.fitted])).toEqual([
      ["dry", []],
      ["wet", ["water"]],
    ]);
    expect(water.default).toBe("wet");
    // no build stages: every requires value is a state's
    expect(requires.filter((r) => !groups.some((g) => g.owns.includes(r)))).toEqual([]);
  });
});

describe("the rocker's Charge and Concentrate selects against the pack's items", () => {
  // The renderer sets the charge's and the concentrate's texture codes to the material's; the viewer shows a few
  // ores in their colours. Each option is an ore of the pack's crushed ore (medium grade and up) or concentrate
  // item, with the texture that item takes; the placer option is Wilderlands Panning's rich gravel, whose
  // texture is its rock's gravel with a few ore specks over it.
  const scenario = manifest.models.find((m) => m.id === "rocker")!.scenario!;
  const select = (code: string) => scenario.textures!.find((t) => t.code === code)!;
  const itemType = (name: string) => {
    const text = readFileSync(new URL(MOD + `assets/seraphhorizons/itemtypes/oreprocessing/${name}.json`, import.meta.url), "utf8");
    return JSON.parse(text.replace(/^\s*\/\/.*$/gm, "")) as {
      variantgroups: { code: string; states: string[] }[];
      textureByType: Record<string, { base: string }>;
    };
  };
  // The texture an item's code takes: the first textureByType key it matches, as the game resolves *ByType.
  const textureOf = (type: ReturnType<typeof itemType>, code: string, ore: string) => {
    const key = Object.keys(type.textureByType).find((k) => new RegExp(`^${k.replace(/[.+?^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*")}$`).test(code))!;
    return type.textureByType[key]!.base.replace("{ore}", ore);
  };

  it("has the two selects, each for a code the shape's faces use, opening at the shape's own texture", () => {
    const requires = requiresValues(parts);
    const codes = [...new Set(flattenShape(shape.elements).flatMap((f) => textureCodes(f.element)))];
    expect(checkScenario(scenario, rig, discoverAnchors(rig).anchors, requires, codes)).toEqual([]);
    expect(scenario.textures!.map((t) => t.code)).toEqual(["charge", "concentrate"]);
    for (const t of scenario.textures!) expect(shape.textures?.[t.code], t.code).toBe(t.options.find((o) => o.id === t.default)!.path);
  });

  it("offers ores of the pack's crushed ore and concentrate, each in the texture its item takes", () => {
    const crushed = itemType("crushedore");
    const concentrate = itemType("concentrate");
    const ores = (t: ReturnType<typeof itemType>) => t.variantgroups.find((g) => g.code === "ore")!.states;
    for (const o of select("charge").options.filter((q) => q.id !== "placer")) {
      expect(ores(crushed)).toContain(o.id);
      expect(o.path, o.id).toBe(textureOf(crushed, `crushedore-${o.id}-medium`, o.id));
    }
    for (const o of select("concentrate").options) {
      expect(ores(concentrate)).toContain(o.id);
      expect(o.path, o.id).toBe(textureOf(concentrate, `concentrate-${o.id}`, o.id));
    }
    // every concentrate is one a charge offered gives; placer gravel's gold is the gold quartz's concentrate
    expect(select("concentrate").options.map((o) => o.id)).toEqual(select("charge").options.filter((o) => o.id !== "placer").map((o) => o.id));
    expect(select("charge").options.find((o) => o.id === "placer")!.path).toBe("game:block/stone/gravel/granite");
  });

  it("draws the charge and the concentrate in the picked ore's colour", () => {
    const view = buildModelView(shape, rig, scenario);
    for (const code of ["charge", "concentrate"]) {
      const s = select(code);
      const opening = view.textures.find((t) => t.code === code)!;
      expect(opening.colour).toBe(s.options.find((o) => o.id === s.default)!.colour);
      const picked = withPickedTextures(view.textures, pickedTextures(scenario.textures, { [code]: "malachite" })).find((t) => t.code === code)!;
      expect(picked).toMatchObject({ path: "game:item/resource/nugget/malachite", colour: s.options.find((o) => o.id === "malachite")!.colour });
    }
  });
});
