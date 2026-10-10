import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import oreLine, { CHAINS, CHARTS, TIERS } from "../scripts/standalone/ore-line.ts";
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
  it("draws every step in all three tiers, each with its issue", () => {
    for (const c of CHARTS) {
      for (const r of c.rows) {
        expect(Boolean(r.span) !== Boolean(r.lanes), `${c.id} ${r.stage}`).toBe(true);
        if (r.lanes) for (const t of TIERS) expect(r.lanes[t].length, `${c.id} ${r.stage} ${t}`).toBeGreaterThan(0);
        const boxes = r.span ? [r.span] : TIERS.flatMap((t) => r.lanes![t]);
        for (const b of boxes) expect(b.issues.length, `${c.id} ${r.stage} ${b.name}`).toBeGreaterThan(0);
      }
    }
    for (const c of CHAINS) for (const s of c.steps) expect(s.issues.length, `${c.id} ${s.name}`).toBeGreaterThan(0);
  });

  it("renders one section per chart, its issues linked", () => {
    const html = oreLine();
    for (const c of [...CHARTS, ...CHAINS]) expect(html).toContain(`id="ol-${c.id}"`);
    expect(html).toContain('href="https://github.com/benjamin-kirkbride/Seraph-Horizons/issues/747"');
    expect(html).not.toMatch(/<script/i);
  });
});
