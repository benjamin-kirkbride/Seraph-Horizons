import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import oreLine, { CFG, CHAINS, CLASSES, concentration, OPEN_QUESTIONS, PARTED, TIERS, tierConcentrate } from "../scripts/standalone/ore-line.ts";
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
