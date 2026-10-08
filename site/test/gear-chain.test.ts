// @vitest-environment jsdom
// The gear chain's recipe shapes (#483): the pickling tub, the oiled gear's lottery and the
// gear cutter, from schema/examples/minimal.json, as helpers and as rendered cards.
import { readFileSync } from "node:fs";
import path from "node:path";
import { flushSync, mount, unmount } from "svelte";
import { afterEach, describe, expect, it } from "vitest";
import type { Recipe, RecipeExport } from "../src/lib/export.ts";
import { VersionData } from "../src/lib/data.ts";
import { cardOutputs, lotteryOutcomes, machineLines, machineRole, tubLines, tubOutputs } from "../src/lib/recipe-view.ts";
import RecipeCard from "../src/components/RecipeCard.svelte";

const minimal = JSON.parse(
  // From the working directory (site/): under jsdom, import.meta.url is not a file URL.
  readFileSync(path.resolve(process.cwd(), "../schema/examples/minimal.json"), "utf8"),
) as RecipeExport;
const byId = (id: string) => minimal.recipes.find((r) => r.id === id)!;
const tub = byId("picklingtub|seraphhorizons:gear-degreased|game:vinegarportion");
const lottery = byId("lottery|seraphhorizons:gear-oiled|0");
const cutter = byId("gearcutter|seraphhorizons:gearblank-steel|0");

describe("tub", () => {
  it("says how long, the batch, and when the acid starts eating", () => {
    expect(tubLines(tub)).toEqual([
      "Pickled clean in 24 hours of game time",
      "A batch of up to 8 gears; a finished batch uses up 1 L",
      "Take them out within 12 hours of done: after that the liquid eats one gear every 3 hours",
    ]);
  });

  it("says a brine bath rusts and gives its loss chance", () => {
    const brine: Recipe = { ...tub, tub: { kind: "rust", hours: 48, batchSize: 8, lossChance: 0.1, failure: 1 } };
    expect(tubLines(brine)).toEqual([
      "Rusts through in 2 days of game time",
      "A batch of up to 8 gears",
      "10% of the gears come out lost",
    ]);
  });

  it("heads the card with the pickled gear only, not the steel bits", () => {
    expect(tubOutputs(tub)).toEqual({ made: [0], failure: 1 });
    expect(cardOutputs(tub, 0).map((s) => s.code)).toEqual(["seraphhorizons:gear-pickled"]);
  });
});

describe("lottery", () => {
  it("lists the outcomes likeliest first with their stacks", () => {
    expect(lotteryOutcomes(lottery, 0).map((o) => [o.label, o.stacks.map((s) => s.code)])).toEqual([
      ["90%", ["game:metalbit-steel"]],
      ["10%", ["seraphhorizons:gear-steel"]],
    ]);
  });

  it("gives an outcome with no outputs no stacks", () => {
    const r: Recipe = { ...lottery, lottery: { trigger: "inventory", outcomes: [{ chance: 0.1, outputs: [0] }, { chance: 0.9, outputs: [] }] } };
    expect(lotteryOutcomes(r, 0)[0]!.stacks).toEqual([]);
  });
});

describe("machine", () => {
  it("says the turns, the work and the oil", () => {
    expect(machineLines(cutter)).toEqual([
      "Mechanical power: 144 turns of the axle a job (12 teeth, 12 turns each)",
      "Drains 10 points of oil (0.1 L) from the machine's tank of 1000 a job",
    ]);
  });

  it("says a hand machine's turns are its lever's", () => {
    const brake: Recipe = { ...cutter, machine: { power: "hand", turns: 6, kept: [1] } };
    expect(machineLines(brake)).toEqual(["By hand: 6 turns of the lever a job, a turn a second while right-click is held"]);
  });

  it("says a hand station's blows are a hammer's", () => {
    const mandrel: Recipe = { ...cutter, machine: { power: "hand", turns: 9, work: { amount: 9, unit: "blows" }, kept: [1] } };
    expect(machineLines(mandrel)).toEqual(["By hand: 9 blows a job, each a right-click with a hammer"]);
  });

  it("says a treadle machine's strokes are its treadle's", () => {
    const shear: Recipe = { ...cutter, machine: { power: "hand", turns: 1, work: { amount: 1, unit: "strokes" }, kept: [1] } };
    expect(machineLines(shear)).toEqual(["By hand: 1 stroke of the treadle a job, a stroke a second while right-click is held"]);
    const copper: Recipe = { ...cutter, machine: { power: "hand", turns: 1.5, work: { amount: 1.5, unit: "strokes" }, kept: [1] } };
    expect(machineLines(copper)).toEqual(["By hand: 1.5 strokes of the treadle a job, a stroke a second while right-click is held"]);
  });

  it("tells the consumed blank from the kept master, the worn kit, the oil and the machine", () => {
    expect(cutter.ingredients.map((_, i) => machineRole(cutter, i))).toEqual(["consumed", "kept", "wear", "oil", "station"]);
  });
});

let target: HTMLElement;
let component: ReturnType<typeof mount> | null = null;

function render(recipe: Recipe, shape: "tub" | "lottery" | "machine") {
  target = document.createElement("div");
  document.body.append(target);
  component = mount(RecipeCard, {
    target,
    props: { recipe, type: { name: shape, shape }, data: new VersionData("test"), mods: {} },
  });
  flushSync();
  return target;
}

afterEach(() => {
  if (component) unmount(component);
  component = null;
  target?.remove();
});

describe("renderers", () => {
  it("draws the tub with its roles, its time and what a lost gear becomes", () => {
    const el = render(tub, "tub");
    expect(el.querySelector("[data-shape]")!.getAttribute("data-shape")).toBe("tub");
    expect(el.querySelector("[data-tub]")!.getAttribute("data-hours")).toBe("24");
    expect([...el.querySelectorAll("[data-role]")].map((e) => e.getAttribute("data-role"))).toEqual(["batch", "liquid", "station"]);
    expect(el.querySelector("[data-failure] [data-code]")!.getAttribute("data-code")).toBe("game:metalbit-steel");
    expect(el.textContent).toContain("Pickled clean in 24 hours");
  });

  it("draws the lottery's outcomes with their chances", () => {
    const el = render(lottery, "lottery");
    const outcomes = [...el.querySelectorAll("[data-outcome]")];
    expect(outcomes.map((o) => o.getAttribute("data-chance"))).toEqual(["0.9", "0.1"]);
    expect(outcomes[1]!.querySelector("[data-code]")!.getAttribute("data-code")).toBe("seraphhorizons:gear-steel");
    expect(el.textContent).toContain("lands in a player's inventory");
  });

  it("draws the machine with the master kept and the kit's wear", () => {
    const el = render(cutter, "machine");
    const kept = el.querySelector('[data-machine-role="kept"]')!;
    expect(kept.querySelector("[data-code]")!.getAttribute("data-code")).toBe("game:gear-temporal");
    expect(kept.textContent).toContain("never consumed");
    expect(el.querySelector('[data-machine-role="wear"]')!.textContent).toContain("loses 10 durability a job with a full oil tank");
    expect(el.querySelector("[data-turns]")!.getAttribute("data-turns")).toBe("144");
  });

  it("falls back to the generic card when the block is missing", () => {
    const { machine: _, ...bare } = cutter;
    const el = render(bare as Recipe, "machine");
    expect(el.querySelector("[data-turns]")).toBeNull();
    expect(el.querySelectorAll("[data-input]")).toHaveLength(5);
  });
});
