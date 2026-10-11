import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import oreLine, { CFG, CHAINS, CLASSES, concentration, OPEN_QUESTIONS, PARTED, TIERS, tierConcentrate } from "../scripts/standalone/ore-line.ts";
import oreProposal, { bestConcentrate, choices, decision, homeSmelt, ingotsPerSlot, OPEN_QUESTIONS as PROPOSAL_QUESTIONS, routes, SETTLED, settledSmelt, STACK, winner } from "../scripts/standalone/ore-proposal.ts";
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
    expect(spec.tabs[1]).toMatchObject({ kind: "html", label: "Proposal: smelt by grade", slug: "proposal-smelt-by-grade" });
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
  it("computes the issues' worked examples from the config", () => {
    // Poor hematite: hand 22 % (rocker, unground), water 80 % (jig, ground), mill 99 % (#765, #685).
    expect(tierConcentrate("hand", { poor: true })[0]).toEqual({ device: "rocker", value: 0.55 * 0.4 });
    expect(tierConcentrate("water", { poor: true })[0]).toEqual({ device: "jig L2", value: 0.8 });
    expect(tierConcentrate("mill", { poor: true })[0]).toEqual({ device: "table + vanner L4", value: 0.99 });
    // Factors multiply and the result is capped (#685).
    expect(concentration(0.55, { classified: false })).toBeCloseTo(0.4675);
    expect(concentration(0.99, { free: true })).toBeCloseTo(0.693);
    expect(concentration(0.99, { free: true, amalgamated: true, density: 1.1 })).toBe(1);
    // What smelts (ore-processing.json, #720).
    expect(CFG.smelt).toMatchObject({ raw: 0, chunk: 0.5, crushed: 0.5, ground: 0, concentrate: 1, roastedconcentrate: 1 });
  });

  it("gives every class a strip with a smelt-now exit under each step, each step with its issues", () => {
    for (const c of CLASSES) {
      expect(c.steps.length, c.id).toBeGreaterThanOrEqual(4);
      for (const s of c.steps) {
        expect(Boolean(s.span) !== Boolean(s.devices), `${c.id} ${s.stage}`).toBe(true);
        expect(s.issues.length, `${c.id} ${s.stage}`).toBeGreaterThan(0);
        expect(["smelt", "none", "on"]).toContain(s.exit.kind);
      }
      expect(c.steps.some((s) => s.exit.kind === "smelt"), c.id).toBe(true);
      for (const stop of c.stops) for (const t of TIERS) expect(stop.cells[t], `${c.id} ${stop.item} ${t}`).toBeDefined();
      expect(c.formula.length, c.id).toBeGreaterThan(20);
      expect(c.example.lines.length, c.id).toBeGreaterThan(0);
    }
    for (const p of PARTED) expect(p.issues.length, p.ore).toBeGreaterThan(0);
    for (const c of CHAINS) expect(c.issues.length, c.id).toBeGreaterThan(0);
  });

  it("renders one section per class, the parting table, the open questions, its issues linked, and no script", () => {
    const html = oreLine();
    for (const c of CLASSES) expect(html).toContain(`id="ol-${c.id}"`);
    for (const id of ["parting", "nonmetals", "open"]) expect(html).toContain(`id="ol-${id}"`);
    expect(html).toContain('href="https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/747"');
    expect(html).toContain("Smelt now");
    // The cumulative figures a reader checks first.
    expect(html).toContain("<b>22 %</b> rocker");
    expect(html).toContain("<b>99 %</b> table + vanner L4");
    expect(html).toContain("<b>46.8 %</b> rocker");
    expect(html).toContain("× firepit 85 %");
    for (const q of OPEN_QUESTIONS) expect(html).toContain(q.slice(0, 20).replace(/&/g, "&amp;"));
    expect(html).not.toMatch(/<script/i);
  });
});

describe("the ore review's smelt-by-grade proposal", () => {
  it("smelts rock forms by grade and concentrate whole; sulfide rock only roasted, sulfide concentrate only roasted", () => {
    expect(SETTLED.byGrade).toEqual({ poor: 0.1, medium: 0.25, rich: 0.4, bountiful: 0.55 });
    expect(settledSmelt("raw", "poor")).toBe(0.1);
    expect(settledSmelt("crushed", "medium")).toBe(0.25);
    expect(settledSmelt("ground", "rich")).toBe(0.4);
    expect(settledSmelt("raw", "bountiful")).toBe(0.55);
    expect(settledSmelt("concentrate", "poor")).toBe(1);
    // Sulfides: rock 0 unroasted, roasted rock the grade's share × the firepit; concentrate 0 until roasted.
    expect(settledSmelt("raw", "rich", SETTLED, true)).toBe(0);
    expect(settledSmelt("crushed", "rich", SETTLED, true)).toBe(0);
    expect(settledSmelt("roastedrock", "rich", SETTLED, true)).toBeCloseTo(0.34);
    expect(settledSmelt("roastedrock", "bountiful", SETTLED, true)).toBeCloseTo(0.4675);
    expect(homeSmelt("medium", SETTLED, true)).toBeCloseTo(0.2125);
    expect(settledSmelt("concentrate", "rich", SETTLED, true)).toBe(0);
    expect(settledSmelt("roastedconcentrate", "rich", SETTLED, true)).toBe(1);
  });

  it("flips the three escape hatches, and finds bountiful by hand a dead heat with the rocker", () => {
    // Poor by hand: crushed 50 % beat the rocker's 22 %; now the rocker wins (22 % against 10 %).
    expect(winner(choices("hand", "poor", null)).key).toBe("treated");
    expect(winner(choices("hand", "poor", SETTLED))).toMatchObject({ key: "conc", device: "rocker" });
    expect(decision("hand", "poor")).toMatchObject({ verdict: "mine" });
    expect(decision("hand", "poor").margin).toBeCloseTo(12);
    // Medium by hand: rocker 55 % against 25 %, 30 points; rich 15.
    expect(decision("hand", "medium").margin).toBeCloseTo(30);
    expect(decision("hand", "rich")).toMatchObject({ verdict: "mine" });
    expect(decision("hand", "rich").margin).toBeCloseTo(15);
    // Bountiful by hand: 55 % as mined against the rocker's 55 %, a tie; at water the jig wins by 25.
    expect(decision("hand", "bountiful")).toMatchObject({ verdict: "tie" });
    expect(decision("hand", "bountiful").margin).toBeCloseTo(0);
    expect(decision("water", "bountiful")).toMatchObject({ verdict: "mine" });
    expect(decision("water", "bountiful").margin).toBeCloseTo(25);
    expect(decision("mill", "bountiful").margin).toBeCloseTo(44);
    // Crushing is 1:1: the crushed figure is the grade's share, the same as mined.
    expect(choices("hand", "rich", SETTLED).map((c) => c.value).slice(0, 2)).toEqual([0.4, 0.4]);
    // Sulfides: the hand line (rocker × firepit 46.75 %) beats medium roasted rock (21.25 %) by 25.5;
    // bountiful roasted rock (46.75 %) is the same figure; the water line (jig × stall 73.6 %) beats it by 26.85.
    expect(bestConcentrate("hand", "medium", { sulfide: true }).value).toBeCloseTo(0.4675);
    expect(decision("hand", "medium", SETTLED, { sulfide: true })).toMatchObject({ verdict: "mine" });
    expect(decision("hand", "medium", SETTLED, { sulfide: true }).margin).toBeCloseTo(25.5);
    expect(decision("hand", "bountiful", SETTLED, { sulfide: true })).toMatchObject({ verdict: "tie" });
    expect(decision("water", "bountiful", SETTLED, { sulfide: true }).margin).toBeCloseTo(26.85);
    // Poor at a tier that grinds: the figure without the grinder is carried along; by hand there is no grinder.
    expect(bestConcentrate("water", "poor")).toEqual({ device: "jig L2", value: 0.8, unground: 0.8 * 0.4 });
    expect(bestConcentrate("hand", "poor")).toEqual({ device: "rocker", value: 0.55 * 0.4 });
  });

  it("gives the slot densities and the trips home: a slot of crushed or ground equals a slot of raw", () => {
    expect(STACK).toEqual({ raw: 4, crushed: 4, ground: 4, roastedrock: 4, concentrate: 128, roastedconcentrate: 128, amalgam: 128, sponge: 128 });
    // A slot of raw poor ore: 4 × 15 × 10 % = 6 units; a slot of concentrate: 128 × 5 = 640 units.
    expect(ingotsPerSlot("raw", "poor", null)).toBe(0);
    expect(ingotsPerSlot("raw", "poor", SETTLED)).toBeCloseTo(0.06);
    expect(ingotsPerSlot("raw", "medium", SETTLED)).toBeCloseTo(0.2);
    expect(ingotsPerSlot("raw", "rich", SETTLED)).toBeCloseTo(0.4);
    expect(ingotsPerSlot("raw", "bountiful", SETTLED)).toBeCloseTo(0.77);
    for (const g of ["poor", "medium", "rich", "bountiful"] as const) {
      expect(ingotsPerSlot("crushed", g, SETTLED)).toBeCloseTo(ingotsPerSlot("raw", g, SETTLED));
      expect(ingotsPerSlot("ground", g, SETTLED)).toBeCloseTo(ingotsPerSlot("raw", g, SETTLED));
    }
    expect(ingotsPerSlot("roastedrock", "bountiful", SETTLED, true)).toBeCloseTo(0.77 * 0.85);
    // Today: a bountiful chunk slot 16 × 35 × 50 % = 2.8; crushed 16 × 5 × 50 % = 0.4 at any grade.
    expect(ingotsPerSlot("chunk", "bountiful", null)).toBeCloseTo(2.8);
    expect(ingotsPerSlot("chunk", "bountiful", SETTLED)).toBe(0);
    expect(ingotsPerSlot("crushed", "medium", null)).toBeCloseTo(0.4);
    expect(ingotsPerSlot("concentrate", "poor", SETTLED)).toBeCloseTo(6.4);
    // 1000 units from poor ore: as mined 10 000 units of rock = 667 raw ore = 167 slots, crushed the same; concentrate 1.6 slots whatever the tier.
    const r = routes("poor", SETTLED);
    expect(r[0]).toMatchObject({ form: "raw", recovery: 0.1 });
    expect(r[0]!.rock).toBeCloseTo(10000);
    expect(r[0]!.slots).toBeCloseTo(10000 / 15 / 4);
    expect(r[1]!.slots).toBeCloseTo(r[0]!.slots);
    expect(r[2]!.slots).toBeCloseTo(1.5625);
    expect(r[4]!.slots).toBeCloseTo(1.5625);
    expect(r[4]!.rock).toBeCloseTo(1000 / 0.99);
    expect(routes("poor", null)[0]!.slots).toBe(Infinity);
    expect(routes("rich", null)[0]).toMatchObject({ form: "chunk", recovery: 0.5 });
    expect(routes("rich", SETTLED)[0]).toMatchObject({ form: "raw", recovery: 0.4 });
  });

  it("renders every section, the key figures and no script", () => {
    const html = oreProposal();
    for (const id of ["compare", "decision", "logistics", "transport", "problems", "special", "nudge", "open"]) expect(html).toContain(`id="op-${id}"`);
    expect(html).toContain("rocker 55 % beats smelting as is (25 %) by <b>30 points</b>");
    expect(html).toContain("a dead heat");
    expect(html).toContain("6.4</b> ingots");
    expect(html).toContain("flips");
    expect(html).not.toContain("sulfidePenalty");
    expect(html).not.toContain("sulfide penalty");
    expect(html).not.toContain("ChunkStack 16");
    for (const q of PROPOSAL_QUESTIONS) expect(html).toContain(q.slice(0, 20).replace(/&/g, "&amp;"));
    expect(html).not.toMatch(/<script/i);
  });
});

describe("the proposal's transport audit", () => {
  it("sizes the loads: 10 ingots of medium rock is 50 slots, of concentrate 1.56 at any grade", () => {
    expect(PLAYER_SLOTS).toBe(66);
    expect(haulSlots("raw", "medium")).toBeCloseTo(1000 / 0.25 / 20 / 4);
    expect(haulSlots("raw", "poor")).toBeCloseTo(1000 / 0.1 / 15 / 4);
    expect(haulSlots("raw", "rich")).toBeCloseTo(1000 / 0.4 / 25 / 4);
    expect(haulSlots("raw", "bountiful")).toBeCloseTo(1000 / 0.55 / 35 / 4);
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
    // the proposal halves every one of Yang's vehicles
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
    // A medium batch is 500 slots; the advanced train's 514 just takes it in one.
    expect(haulSlots("raw", "medium", BATCH)).toBeCloseTo(500);
    expect(trips(haulSlots("raw", "medium", BATCH), foot.slots.raw)).toBe(8);
    expect(trips(haulSlots("raw", "poor", BATCH), foot.slots.raw)).toBe(26);
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
