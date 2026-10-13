import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import oreLine, { catchable, CFG, chain, CLASSES, full, FULL, GLOSSARY, handRecovery, lineRecovery, MACHINES, machine, OPEN_QUESTIONS, PARTED, platesLine, rockSmelt, subchains } from "../scripts/standalone/ore-line.ts";
import { DRAW, engineSpeed, engineTorque, fuel, lineLoad, lineWaterPerRock, POWER, rateAt, rotorSpeed, run, SETUPS, steamBalance, worksLoad } from "../scripts/standalone/ore-power.ts";
import oreDeposits, { DEPOSITS, depositsOf, LEVELS, ORES, oreUnits, quantile, rockSlots, share, sig2, tally, UNITS_PER_BLOCK, veinGradeOdds } from "../scripts/standalone/ore-deposits.ts";
import oreProposal, { bestConcentrate, decision, homeSmelt, ingotsPerSlot, LINES, routes, SETTLED, settledSmelt, STACK, UNITS } from "../scripts/standalone/ore-proposal.ts";
import { BATCH, gaitSpeed, GAITS, handcarTopSpeed, haulHours, haulSlots, NOTES, PLAYER, PLAYER_SLOTS, RAILS, railCost, steamTopSpeed, steamTopSpeedUnscaled, transports, trips, YANG, YANG_SPEED_SCALE } from "../scripts/standalone/ore-transport.ts";
import { tabForHash } from "../scripts/standalone/tabs-hash.ts";
import { expandPath, groupBy, parseSpec, slugify } from "../scripts/standalone/tabs-spec.ts";
import { loadModels, REPO } from "../scripts/models.ts";

const env = { HOME: "/home/me", TMPDIR: "/tmp/x" };

describe("the tabbed standalone page's spec", () => {
  it("expands ~, $VAR and ${VAR}, and resolves relative paths from the spec's folder", () => {
    expect(expandPath("~/a", "/spec", env)).toBe("/home/me/a");
    expect(expandPath("$TMPDIR/m-rocker", "/spec", env)).toBe("/tmp/x/m-rocker");
    expect(expandPath("${TMPDIR}/m", "/spec", env)).toBe("/tmp/x/m");
    expect(expandPath("ore-line.ts", "/spec/dir", env)).toBe("/spec/dir/ore-line.ts");
    expect(() => expandPath("$NOPE/a", "/spec", env)).toThrow(/NOPE is not set/);
  });

  it("slugs labels and refuses a slug used twice", () => {
    expect(slugify("Parting furnace")).toBe("parting-furnace");
    expect(slugify("Processing line")).toBe("processing-line");
    const spec = parseSpec(
      {
        title: "T",
        tabs: [
          { kind: "html", label: "Processing line", file: "a.html" },
          { kind: "model", label: "Rocker", repo: "$TMPDIR/m", id: "rocker", note: "Branch x" },
        ],
      },
      "/spec",
      env,
    );
    expect(spec.viewer).toBe("own");
    expect(spec.tabs).toEqual([
      { kind: "html", label: "Processing line", slug: "processing-line", file: "/spec/a.html" },
      { kind: "model", label: "Rocker", slug: "rocker", repo: "/tmp/x/m", id: "rocker", note: "Branch x" },
    ]);
    const twice = { title: "T", tabs: [1, 2].map(() => ({ kind: "model", label: "Rocker", repo: "/r", id: "rocker" })) };
    expect(() => parseSpec(twice, "/spec", env)).toThrow(/used twice/);
    expect(() => parseSpec({ title: "T", tabs: [{ kind: "pdf", label: "x" }] }, "/", env)).toThrow(/kind/);
    expect(() => parseSpec({ title: "T", viewer: "theirs", tabs: [] }, "/", env)).toThrow(/viewer/);
  });

  it("parses the ore review's spec", () => {
    const file = fileURLToPath(new URL("../scripts/standalone/ore-review.json", import.meta.url));
    const spec = parseSpec(JSON.parse(readFileSync(file, "utf8")), "/spec", env);
    expect(spec.tabs[0]?.kind).toBe("html");
    expect(spec.tabs[1]).toMatchObject({ kind: "html", label: "Smelting, stacks and logistics", slug: "smelting-stacks-and-logistics" });
    expect(spec.tabs.filter((t) => t.kind === "model").length).toBeGreaterThanOrEqual(8);
  });

  it("opens the tab the hash names, and nothing for an unknown hash", () => {
    const slugs = ["processing-line", "rocker"];
    expect(tabForHash("#rocker", slugs)).toBe("rocker");
    expect(tabForHash("", slugs)).toBeNull();
    expect(tabForHash("#/models", slugs)).toBeNull();
    expect(tabForHash("#%E0%A4%A", slugs)).toBeNull();
  });

  it("groups in first-seen order", () => {
    expect([...groupBy(["a1", "b1", "a2"], (s) => s[0]!).entries()]).toEqual([
      ["a", ["a1", "a2"]],
      ["b", ["b1"]],
    ]);
  });

  it("reads models from a checkout's root", () => {
    const here = loadModels();
    expect(loadModels(REPO).index.models.map((m) => m.id)).toEqual(here.index.models.map((m) => m.id));
  });
});

describe("the ore review's processing line", () => {
  it("mirrors the merged figures: smelt by grade, units 3/5/7/10, stacks of 16, no chunks", () => {
    expect(CFG.smeltByGrade).toEqual({ poor: 0.1, medium: 0.25, rich: 0.4, bountiful: 0.5 });
    expect(CFG.units).toEqual({ poor: 3, medium: 5, rich: 7, bountiful: 10 });
    expect(CFG.rockStack).toBe(16);
    expect(CFG.concentrateStack).toBe(128);
    expect(rockSmelt("medium", CFG.roasters.stall)).toBeCloseTo(0.23);
  });

  it("chains the concentrators: 1 − Π(1 − r), with no gain from a machine's own kind", () => {
    // The spec's sub-chain table.
    expect(chain(["sluice"])).toBeCloseTo(0.55);
    expect(chain(["sluice", "jig"])).toBeCloseTo(0.82);
    expect(chain(["sluice", "table"])).toBeCloseTo(0.865);
    expect(chain(["jig", "table"])).toBeCloseTo(0.88);
    expect(chain(["sluice", "vanner"])).toBeCloseTo(0.91);
    expect(chain(["jig", "vanner"])).toBeCloseTo(0.92);
    expect(chain(["table", "vanner"])).toBeCloseTo(0.94);
    expect(chain(["sluice", "jig", "table"])).toBeCloseTo(0.946);
    expect(chain(["sluice", "jig", "vanner"])).toBeCloseTo(0.964);
    expect(chain(["sluice", "table", "vanner"])).toBeCloseTo(0.973);
    expect(chain(["jig", "table", "vanner"])).toBeCloseTo(0.976);
    expect(chain(FULL)).toBeCloseTo(0.989);
    expect(full()).toBeCloseTo(0.989);
    // Two vanners are one vanner.
    expect(chain(["vanner", "vanner"])).toBeCloseTo(0.8);
    expect(subchains()).toHaveLength(15);
    // Density per pass, capped.
    expect(chain(["vanner"], 1.1)).toBeCloseTo(0.88);
    expect(chain(["sluice", "jig", "table", "vanner"], 2)).toBe(1);
  });

  it("applies the locked shares once: unground poor 39.6 %, unclassified 84.1 %, both 33.6 %, a lone jig 24 %", () => {
    expect(catchable({})).toBe(1);
    expect(catchable({ poor: true })).toBe(0.4);
    expect(catchable({ poor: true, ground: true })).toBe(1);
    expect(catchable({ classified: false })).toBe(0.85);
    expect(full({ poor: true })).toBeCloseTo(0.3956);
    expect(full({ classified: false })).toBeCloseTo(0.84065);
    expect(full({ poor: true, classified: false })).toBeCloseTo(0.33626);
    expect(lineRecovery(["jig"], { poor: true })).toBeCloseTo(0.24);
    expect(lineRecovery(["jig"], { classified: false })).toBeCloseTo(0.51);
    // Free metal: × 0.7 without the plates; the plates' own figures with them.
    expect(full({ free: true })).toBeCloseTo(0.6923);
    expect(platesLine(1, FULL)).toBeCloseTo(0.956, 3);
    expect(platesLine(2, FULL)).toBeCloseTo(0.996, 3);
    // By hand: the rocker, the amalgam pan, the riddle.
    expect(handRecovery("rocker")).toBe(0.55);
    expect(handRecovery("rocker", { classified: false })).toBeCloseTo(0.4675);
    expect(handRecovery("rocker", { poor: true })).toBeCloseTo(0.22);
    expect(handRecovery("rocker", { free: true })).toBeCloseTo(0.385);
    expect(handRecovery("rocker", { free: true, amalgamated: true })).toBeCloseTo(0.55);
  });

  it("has a card for every machine and hand station, and every line step points at one", () => {
    expect(MACHINES.filter((m) => m.kind === "machine")).toHaveLength(15);
    expect(MACHINES.filter((m) => m.kind === "hand").map((m) => m.id)).toEqual(["pan", "rocker", "riddle", "spalling", "amalgampan", "firepit", "cupel", "liquationpan", "still", "leaching"]);
    for (const m of MACHINES) {
      for (const k of ["what", "does", "why", "skip", "where", "needs", "replaces", "rate"] as const) expect(m[k].length, `${m.id} ${k}`).toBeGreaterThan(3);
      expect(m.issues.length, m.id).toBeGreaterThan(0);
    }
    for (const c of CLASSES) {
      const lines = [...c.minimal, c.full];
      for (const l of lines) {
        expect(l.steps.length, `${c.id} ${l.title}`).toBeGreaterThanOrEqual(2);
        for (const s of l.steps) {
          if (s.m) expect(() => machine(s.m!), `${c.id} ${s.m}`).not.toThrow();
          else expect(s.stage, c.id).toBeTruthy();
        }
      }
      expect(c.full.steps.some((s) => s.exit.kind === "share"), c.id).toBe(true);
    }
    for (const p of PARTED) expect(p.issues.length, p.ore).toBeGreaterThan(0);
    expect(GLOSSARY.length).toBeGreaterThan(30);
    expect(OPEN_QUESTIONS.length).toBe(11);
  });

  it("renders the classes, the parting table, the cards, the chains, the glossary and the open questions, with no script", () => {
    const html = oreLine();
    for (const c of CLASSES) expect(html).toContain(`id="ol-${c.id}"`);
    for (const id of ["parting", "machines", "chains", "glossary", "open"]) expect(html).toContain(`id="ol-${id}"`);
    for (const m of MACHINES) expect(html).toContain(`id="ol-m-${m.id}"`);
    expect(html).toContain('href="#ol-m-jig"');
    expect(html).toContain('href="https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/747"');
    expect(html).toContain("Smelt here");
    // The figures a reader checks first.
    expect(html).toContain('<span class="ol-exit-fig">10 / 25 / 40 / 50 %</span>');
    expect(html).toContain('<span class="ol-exit-fig">9.2 / 23 / 36.8 / 46 %</span>');
    expect(html).toContain('<span class="ol-exit-fig">98.9 %</span>');
    expect(html).toContain('<span class="ol-exit-fig">46.8 %</span>');
    expect(html).toContain('<span class="ol-exit-fig">99.6 %</span>');
    expect(html).toContain("all four <b>98.9 %</b>");
    expect(html).toContain("<b>39.6 %</b>");
    expect(html).toContain("<b>84.1 %</b>");
    for (const q of OPEN_QUESTIONS) expect(html).toContain(q.slice(0, 20).replace(/&/g, "&amp;"));
    expect(html).not.toMatch(/<script/i);
    expect(html).not.toContain("chunk");
    expect(html).not.toContain("quern takes");
  });
});

describe("the ore review's smelting, stacks and logistics", () => {
  it("smelts rock forms by grade and concentrate whole; sulfide rock only roasted, sulfide concentrate only roasted", () => {
    expect(SETTLED.byGrade).toEqual({ poor: 0.1, medium: 0.25, rich: 0.4, bountiful: 0.5 });
    expect(settledSmelt("raw", "poor")).toBe(0.1);
    expect(settledSmelt("crushed", "medium")).toBe(0.25);
    expect(settledSmelt("ground", "rich")).toBe(0.4);
    expect(settledSmelt("raw", "bountiful")).toBe(0.5);
    expect(settledSmelt("concentrate", "poor")).toBe(1);
    expect(settledSmelt("raw", "rich", SETTLED, true)).toBe(0);
    expect(settledSmelt("crushed", "rich", SETTLED, true)).toBe(0);
    expect(settledSmelt("roastedrock", "rich", SETTLED, true)).toBeCloseTo(0.34);
    expect(settledSmelt("roastedrock", "bountiful", SETTLED, true)).toBeCloseTo(0.425);
    expect(settledSmelt("roastedrock", "medium", SETTLED, true, CFG.roasters.stall)).toBeCloseTo(0.23);
    expect(homeSmelt("medium", SETTLED, true)).toBeCloseTo(0.2125);
    expect(settledSmelt("concentrate", "rich", SETTLED, true)).toBe(0);
    expect(settledSmelt("roastedconcentrate", "rich", SETTLED, true)).toBe(1);
  });

  it("decides each grade: the rocker wins at every grade but bountiful, where it is a dead heat with 50 %; every line beats carrying home", () => {
    expect(LINES.map((l) => l.id)).toEqual(["rocker", "sluice", "sluicejig", "full"]);
    expect(bestConcentrate("rocker", "poor")).toBeCloseTo(0.22);
    expect(bestConcentrate("full", "poor")).toBeCloseTo(0.989);
    expect(decision("rocker", "poor")).toMatchObject({ verdict: "mine" });
    expect(decision("rocker", "poor").margin).toBeCloseTo(12);
    expect(decision("rocker", "medium").margin).toBeCloseTo(30);
    expect(decision("rocker", "rich").margin).toBeCloseTo(15);
    expect(decision("rocker", "bountiful").margin).toBeCloseTo(5);
    expect(decision("sluice", "bountiful")).toMatchObject({ verdict: "mine" });
    expect(decision("sluicejig", "bountiful").margin).toBeCloseTo(32);
    expect(decision("full", "bountiful").margin).toBeCloseTo(48.92);
    // Sulfides: home is the stall's roasted rock (medium 23 %); the hand line rocker × firepit 46.75 %; the full line × reverberatory 98.9 %.
    expect(decision("rocker", "medium", { sulfide: true }).home).toBeCloseTo(0.23);
    expect(decision("rocker", "medium", { sulfide: true }).mine).toBeCloseTo(0.4675);
    expect(decision("rocker", "medium", { sulfide: true }).margin).toBeCloseTo(23.75);
    expect(decision("rocker", "bountiful", { sulfide: true })).toMatchObject({ verdict: "mine" });
    expect(decision("full", "bountiful", { sulfide: true }).margin).toBeCloseTo(52.92);
  });

  it("gives the slot densities and the trips home: a slot of crushed or ground equals a slot of raw", () => {
    expect(STACK).toEqual({ raw: 16, crushed: 16, ground: 16, roastedrock: 16, concentrate: 128, roastedconcentrate: 128, amalgam: 128, sponge: 128 });
    expect(UNITS).toEqual({ poor: 3, medium: 5, rich: 7, bountiful: 10 });
    // A slot of raw poor ore: 16 × 3 × 10 % = 4.8 units; a slot of concentrate: 128 × 5 = 640 units.
    expect(ingotsPerSlot("raw", "poor")).toBeCloseTo(0.048);
    expect(ingotsPerSlot("raw", "medium")).toBeCloseTo(0.2);
    expect(ingotsPerSlot("raw", "rich")).toBeCloseTo(0.448);
    expect(ingotsPerSlot("raw", "bountiful")).toBeCloseTo(0.8);
    for (const g of ["poor", "medium", "rich", "bountiful"] as const) {
      expect(ingotsPerSlot("crushed", g)).toBeCloseTo(ingotsPerSlot("raw", g));
      expect(ingotsPerSlot("ground", g)).toBeCloseTo(ingotsPerSlot("raw", g));
    }
    expect(ingotsPerSlot("roastedrock", "bountiful", true)).toBeCloseTo(0.8 * 0.85);
    expect(ingotsPerSlot("concentrate", "poor")).toBeCloseTo(6.4);
    // 1000 units from poor ore: as rock 10 000 units = 3333 ore = 208 slots; concentrate 1.56 slots whatever the line.
    const r = routes("poor");
    expect(r[0]).toMatchObject({ form: "raw", recovery: 0.1 });
    expect(r[0]!.rock).toBeCloseTo(10000);
    expect(r[0]!.slots).toBeCloseTo(10000 / 3 / 16);
    expect(r[1]!.slots).toBeCloseTo(1.5625);
    expect(r[4]!.slots).toBeCloseTo(1.5625);
    expect(r[4]!.rock).toBeCloseTo(1000 / 0.9892);
    expect(routes("rich")[0]).toMatchObject({ form: "raw", recovery: 0.4 });
  });

  it("renders every section, the key figures and no script", () => {
    const html = oreProposal();
    for (const id of ["decision", "special", "logistics", "transport", "power"]) expect(html).toContain(`id="op-${id}"`);
    expect(html).toContain("Smelting, stacks and logistics");
    expect(html).toContain("mine wins by 30");
    expect(html).toContain("6.4</b> ingots");
    expect(html).toContain("Power and water");
    expect(html).not.toContain("Chunk");
    expect(html).not.toContain("a chunk");
    expect(html).not.toContain("Proposal");
    expect(html).not.toMatch(/<script/i);
  });
});

describe("the power and water budget", () => {
  it("sums one battery's line to 1.78, 2.66 on poor ore, and the works to 0.9", () => {
    expect(lineLoad()).toBeCloseTo(1.775);
    expect(lineLoad(true)).toBeCloseTo(2.655);
    expect(worksLoad()).toBeCloseTo(0.9);
    expect(lineWaterPerRock()).toBe(160);
    expect(lineWaterPerRock(true)).toBe(185);
    expect(DRAW.find((d) => d.id === "jig")!.water).toBe(70);
    for (const d of DRAW) expect(() => machine(d.id), d.id).not.toThrow();
  });

  it("settles the shaft where the engines' torque meets the load: C 0.63, B 0.44, A 0.34 and stalling on poor ore", () => {
    const E = POWER.engines;
    expect(E.high.B).toBeCloseTo(1.6);
    expect(E.high.S).toBeCloseTo(0.82, 2);
    expect(E.watt.S).toBeCloseTo(0.5, 2);
    expect(engineTorque(E.high, 0.1)).toBeCloseTo(6.4);
    expect(engineTorque(E.high, E.high.S)).toBe(0);
    expect(engineSpeed([E.high], lineLoad())).toBeCloseTo(0.63, 2);
    expect(engineSpeed([E.high], lineLoad(true))).toBeCloseTo(0.56, 2);
    expect(engineSpeed([E.normal, E.watt], lineLoad())).toBeCloseTo(0.44, 2);
    expect(engineSpeed([E.normal, E.watt], lineLoad(true))).toBeCloseTo(0.4, 2);
    expect(engineSpeed([E.watt], lineLoad())).toBeCloseTo(0.34, 2);
    expect(engineSpeed([E.watt], lineLoad(true))).toBe(0);
    expect(engineSpeed([E.normal, E.normal, E.normal], lineLoad())).toBeCloseTo(0.5, 2);
    expect(engineSpeed([E.normal], worksLoad())).toBeCloseTo(0.47, 2);
    // Rotors: a hand crank on a jig 0.3, a windmill on an arrastra 0.54, a water wheel on the pump 0.175; Millwright's line at 0.6 from wind 0.24.
    expect(rotorSpeed(POWER.rotors.handcrank, 0.1)).toBeCloseTo(0.3);
    expect(rotorSpeed(POWER.rotors.windmill, 0.1)).toBeCloseTo(0.536, 2);
    expect(rotorSpeed(POWER.rotors.waterwheel, POWER.mpPump.load)).toBeCloseTo(0.175);
    expect(rotorSpeed(POWER.rotors.millwright, lineLoad())).toBeCloseTo(0.6, 1);
    expect(rotorSpeed(POWER.rotors.windmill, lineLoad(true))).toBe(0);
    expect(rateAt(0.63)).toBeCloseTo(1.05);
    expect(rateAt(0.04)).toBe(0);
  });

  it("costs the setups in water and coal", () => {
    const [A, B, C] = SETUPS;
    expect(steamBalance(A!)).toEqual({ made: 33, used: 30 });
    expect(steamBalance(C!)).toEqual({ made: 48, used: 48 });
    expect(steamBalance(B!)).toEqual({ made: 49.5, used: 46 });
    const c = run(C!);
    expect(c.rockPerHour).toBeCloseTo(6.3, 1);
    expect(c.boilerWater).toBe(3);
    expect(c.pumped).toBeCloseTo(12.5, 1);
    expect(c.margin).toBeGreaterThan(0);
    const cPoor = run(C!, true);
    expect(cPoor.margin).toBeLessThan(0);
    expect(cPoor.margin).toBeCloseTo(-0.4, 1);
    expect(run(A!).boilerWater).toBe(2);
    expect(run(A!, true).stalls).toBe(true);
    // Coal an ingot: A on medium 3.0 (4.0 ingots a day); C on poor 3.0 (4.0).
    expect(fuel(run(A!).rate, 5).coalPerIngot).toBeCloseTo(3.0, 1);
    expect(fuel(run(A!).rate, 5).ingotsPerDay).toBeCloseTo(4.0, 1);
    expect(fuel(cPoor.rate, 3).coalPerIngot).toBeCloseTo(3.0, 1);
    expect(fuel(run(C!).rate, 10).ingotsPerDay).toBeCloseTo(14.9, 1);
    expect(fuel(0, 5).coalPerIngot).toBe(Infinity);
    const html = oreProposal();
    expect(html).toContain("How mechanical power works");
    expect(html).toContain("The hard limits");
    expect(html).toContain("Fuel against metal");
    expect(html).toContain("stalls");
  });
});

describe("the transport audit", () => {
  it("sizes the loads: 10 ingots of medium rock is 50 slots, of concentrate 1.56 at any grade", () => {
    expect(PLAYER_SLOTS).toBe(66);
    expect(haulSlots("raw", "medium")).toBeCloseTo(1000 / 0.25 / 5 / 16);
    expect(haulSlots("raw", "medium")).toBeCloseTo(50);
    expect(haulSlots("raw", "poor")).toBeCloseTo(1000 / 0.1 / 3 / 16);
    expect(haulSlots("raw", "rich")).toBeCloseTo(1000 / 0.4 / 7 / 16);
    expect(haulSlots("raw", "bountiful")).toBeCloseTo(1000 / 0.5 / 10 / 16);
    expect(haulSlots("conc", "poor")).toBeCloseTo(1.5625);
    expect(haulSlots("conc", "bountiful")).toBeCloseTo(1.5625);
    expect(trips(41.7, 66)).toBe(1);
    expect(trips(66, 66)).toBe(1);
    expect(trips(66.1, 66)).toBe(2);
  });

  it("computes the vehicles' speeds from their files' figures", () => {
    // Handcar: one rider 4.2 less 0.5 for the crate; with two boxcars it falls to the 0.8 floor.
    expect(handcarTopSpeed(1)).toBeCloseTo(3.7);
    expect(handcarTopSpeed(9)).toBe(0.8);
    // Yang: power × max °C ÷ 100 − weight, capped by the track.
    expect(steamTopSpeedUnscaled(YANG.locoStandard, 16, 32)).toBe(18);
    expect(steamTopSpeedUnscaled(YANG.locoAdvanced, 28, 32)).toBe(32);
    expect(steamTopSpeedUnscaled(YANG.engineCartStandard, 12, YANG.track.wagon.metal)).toBe(14);
    // the pack halves every one of Yang's vehicles
    expect(YANG_SPEED_SCALE).toBe(0.5);
    expect(steamTopSpeed(YANG.locoStandard, 16, 32)).toBe(9);
    expect(steamTopSpeed(YANG.locoAdvanced, 28, 32)).toBe(16);
    expect(steamTopSpeed(YANG.engineCartStandard, 12, YANG.track.wagon.metal)).toBe(7);
    const ts = transports();
    expect(ts.find((t) => t.id === "carryon")!.slots).toEqual({ raw: 66 + 48, conc: 66 + 25 + 16 });
    expect(ts.find((t) => t.id === "sgadvanced")!.slots.raw).toBe(66 + 7 * 64);
    for (const t of ts) for (const n of t.notes) expect(NOTES[n], `${t.id} note ${n}`).toBeDefined();
  });

  it("turns a gait's movespeed into blocks a second as the engine's ground physics does", () => {
    // 135.7 blocks a second per unit of walk vector, as measured in Atlas (135.0 at the server's 1/30 s).
    expect(gaitSpeed(1)).toBeCloseTo(135.71, 1);
    expect(PLAYER.walk).toBeCloseTo(3.39, 2);
    expect(gaitSpeed(GAITS.elk.sprint)).toBeCloseTo(10.86, 2);
    expect(gaitSpeed(GAITS.elk.trot)).toBeCloseTo(4.89, 2);
    expect(gaitSpeed(GAITS.cartWide.sprint)).toBeCloseTo(5.43, 2);
    expect(gaitSpeed(GAITS.cartNarrow.sprint)).toBeCloseTo(6.11, 2);
    const ts = transports();
    for (const t of ts) expect(t.speed, t.id).not.toBeNull();
    expect(ts.find((t) => t.id === "elk")!.speed).toBe(gaitSpeed(0.08));
  });

  it("costs track at one block a piece: a kilometre of metal is 36 iron rods and 72 planks", () => {
    const [stdWood, stdMetal, wagonWood, wagonMetal] = RAILS;
    expect(railCost(stdWood, 1000)).toEqual({ crafts: 21, binder: 126, planks: 252 });
    expect(railCost(stdMetal, 1000)).toEqual({ crafts: 6, binder: 36, planks: 72 });
    expect(railCost(stdMetal, 2000)).toEqual({ crafts: 11, binder: 66, planks: 132 });
    expect(railCost(stdMetal, 5000)).toEqual({ crafts: 27, binder: 162, planks: 324 });
    expect(railCost(stdWood, 5000)).toEqual({ crafts: 105, binder: 630, planks: 1260 });
    expect(railCost(wagonWood, 1000)).toEqual({ crafts: 32, binder: 128, planks: 256 });
    expect(railCost(wagonMetal, 1000)).toEqual({ crafts: 8, binder: 32, planks: 64 });
  });

  it("finds walking a batch of medium rock 8 trips and the advanced train 1, and at half speed only the advanced train breaking the lever", () => {
    const ts = transports();
    const foot = ts.find((t) => t.id === "foot")!;
    const adv = ts.find((t) => t.id === "sgadvanced")!;
    // A medium batch is 500 slots (16 × 5 units is 80 a slot, as 4 × 20 was); the advanced train's 514 just takes it in one. Poor is 2083.
    expect(haulSlots("raw", "medium", BATCH)).toBeCloseTo(500);
    expect(trips(haulSlots("raw", "medium", BATCH), foot.slots.raw)).toBe(8);
    expect(trips(haulSlots("raw", "poor", BATCH), foot.slots.raw)).toBe(32);
    expect(trips(haulSlots("raw", "medium", BATCH), adv.slots.raw)).toBe(1);
    // 8 round trips of 4 km at a sprint.
    expect(haulHours(foot, "raw", "medium", 2000, BATCH)).toBeCloseTo((8 * 4000) / PLAYER.sprint / 3600);
    // The wide cart: 4 trips of the batch at its sprint; the elk 6 at its gallop.
    expect(haulHours(ts.find((t) => t.id === "cartwide")!, "raw", "medium", 2000, BATCH)).toBeCloseTo((4 * 4000) / gaitSpeed(0.04) / 3600);
    expect(haulHours(ts.find((t) => t.id === "elk")!, "raw", "medium", 2000, BATCH)).toBeCloseTo((6 * 4000) / gaitSpeed(0.08) / 3600);
    expect(ts.filter((t) => t.verdict === "breaks").map((t) => t.id)).toEqual(["sgadvanced"]);
    const html = oreProposal();
    expect(html).toContain("4. Transport: can rock be hauled home?");
    expect(html).toContain('id="ot-n10"');
    expect(html).toContain('id="ot-n18"');
    expect(html).toContain("Laying track");
    expect(html).not.toContain("speed unknown");
  });
});

describe("the ore review's deposits tab", () => {
  it("values a block by its drops and the ore's units", () => {
    expect(UNITS_PER_BLOCK).toBeCloseTo(1.27);
    expect(oreUnits("hematite", "poor")).toBe(4);
    expect(oreUnits("cassiterite", "bountiful")).toBe(6);
    expect(oreUnits("sphalerite", "rich")).toBe(7);
    // 100 blocks of medium malachite: 100 × 1.27 × 5 units = 6.35 ingots in the ground.
    const t = tally({ ores: { malachite: [0, 100, 0, 0] } });
    expect(t.ingots.ground).toBeCloseTo(6.35);
    expect(t.ingots.mined).toBe(0);
    expect(t.ingots.minedAll).toBeCloseTo(6.35 * CFG.smeltByGrade.medium);
    expect(t.ingots.hand).toBeCloseTo(6.35 * CFG.hand.rocker);
    expect(t.ingots.full).toBeCloseTo(6.35 * full());
    expect(rockSlots(t)).toBeCloseTo((100 * 1.26) / 16);
  });

  it("follows the processing line's rules for poor ore and sulfides", () => {
    expect(share("hand", "malachite", "poor")).toBeCloseTo(CFG.hand.rocker * CFG.locked.poorUnground);
    expect(share("water", "malachite", "poor")).toBeCloseTo(lineRecovery(["sluice", "jig"]));
    expect(share("hand", "sphalerite", "medium")).toBeCloseTo(CFG.hand.rocker * CFG.roasters.firepit);
    expect(share("mined", "sphalerite", "rich")).toBeCloseTo(CFG.smeltByGrade.rich * CFG.roasters.stall);
    expect(share("full", "nativecopper", "medium")).toBeLessThanOrEqual(1);
    for (const l of LEVELS) for (const g of ["poor", "medium", "rich", "bountiful"] as const) expect(share(l, "hematite", g)).toBeLessThanOrEqual(1);
  });

  it("draws IOG's vein grades as u² and clamps them to the rock", () => {
    const four = veinGradeOdds(4);
    expect(four[0]).toBeCloseTo(0.5);
    expect(four.reduce((a, b) => a + b, 0)).toBeCloseTo(1);
    expect(veinGradeOdds(2)).toEqual([0.5, expect.closeTo(0.5)]);
  });

  it("has measured deposits for four metals and renders every section", () => {
    for (const m of ["iron", "copper", "tin", "zinc"] as const) expect(depositsOf(m, "cell").length).toBeGreaterThan(0);
    expect(DEPOSITS.every((d) => Object.keys(d.ores).every((o) => ORES[o]?.metal === d.metal))).toBe(true);
    expect(quantile([1, 2, 3, 4, 5], 0.5)).toBe(3);
    expect(sig2(1234)).toBe("1,200");
    expect(sig2(3.44)).toBe("3.4");
    const html = oreDeposits();
    for (const id of ["od-summary", "od-iron", "od-copper", "od-tin", "od-zinc", "od-nickel", "od-effort", "od-need", "od-method"]) expect(html).toContain(`id="${id}"`);
    expect(html).not.toMatch(/NaN|undefined/);
  });

  it("is listed after the smelting tab", () => {
    const file = fileURLToPath(new URL("../scripts/standalone/ore-review.json", import.meta.url));
    const spec = parseSpec(JSON.parse(readFileSync(file, "utf8")), "/spec", env);
    expect(spec.tabs[2]).toMatchObject({ kind: "html", label: "Deposits", slug: "deposits" });
  });
});
