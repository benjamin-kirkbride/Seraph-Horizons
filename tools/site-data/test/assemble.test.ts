import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { assemble, AssembleError, versionId, type ReleaseEntry } from "../src/assemble.js";
import { fetchReleases, type ReleaseSource } from "../src/fetch.js";
import { Pipeline } from "../src/pipeline.js";
import { compareVersions, parseTag } from "../src/semver.js";
import { example, tempDir } from "./helpers.js";

const pipeline = Pipeline.fromDir();

interface FakeRelease {
  tag: string;
  commit: string;
  /** pack version and game version of the export, or null for a release without one */
  export: [string, string] | null;
  breakIt?: boolean;
}

/** Writes a releases directory as `fetch` would, and returns it. */
function releasesDir(releases: FakeRelease[]): string {
  const dir = tempDir();
  const entries: ReleaseEntry[] = releases.map((r, i) => {
    if (!r.export) return { tag: r.tag, commit: r.commit, export: null };
    const doc = example();
    doc.pack.version = r.export[0];
    doc.pack.gameVersion = r.export[1];
    if (r.breakIt) doc.recipeTypes.grid.count = 7;
    const rel = `r${i}/seraphhorizons_${r.export[0]}_recipes.json`;
    mkdirSync(path.join(dir, `r${i}`));
    writeFileSync(path.join(dir, rel), JSON.stringify(doc, null, 1));
    return { tag: r.tag, commit: r.commit, export: rel };
  });
  writeFileSync(path.join(dir, "releases.json"), JSON.stringify({ releases: entries }));
  return dir;
}

function run(releases: FakeRelease[]) {
  const src = releasesDir(releases);
  const out = path.join(tempDir(), "data");
  const log: string[] = [];
  const versions = assemble({ releasesDir: src, outDir: out, pipeline, log: (l) => log.push(l) });
  return { src, out, log, versions };
}

// Listed in the order `gh release list` might give them (newest first by date),
// which is not version order: v0.9.1 is a patch released after v0.10.0.
const FIVE: FakeRelease[] = [
  { tag: "next", commit: "b0ec8cd1234567890abcdef1234567890abcdef1", export: ["0.10.1", "1.22.8"] },
  { tag: "v0.9.1", commit: "9910000000000000000000000000000000000000", export: ["0.9.1", "1.22.5"] },
  { tag: "v1.0.0-rc.1", commit: "1001000000000000000000000000000000000000", export: ["1.0.0-rc.1", "1.22.7"] },
  { tag: "v1.0.0", commit: "1000000000000000000000000000000000000000", export: ["1.0.0", "1.22.7"] },
  { tag: "v0.10.0", commit: "0100000000000000000000000000000000000000", export: ["0.10.0", "1.22.6"] },
];

describe("assemble", () => {
  it("writes versions.json with main first, then releases newest first", () => {
    const { out, versions } = run(FIVE);
    const expected = {
      default: "v1.0.0",
      versions: [
        { id: "main", label: "main (b0ec8cd)", packVersion: "0.10.1", gameVersion: "1.22.8", commit: "b0ec8cd1234567890abcdef1234567890abcdef1" },
        { id: "v1.0.0", label: "1.0.0", packVersion: "1.0.0", gameVersion: "1.22.7", commit: "1000000000000000000000000000000000000000" },
        { id: "v1.0.0-rc.1", label: "1.0.0-rc.1", packVersion: "1.0.0-rc.1", gameVersion: "1.22.7", commit: "1001000000000000000000000000000000000000" },
        { id: "v0.10.0", label: "0.10.0", packVersion: "0.10.0", gameVersion: "1.22.6", commit: "0100000000000000000000000000000000000000" },
        { id: "v0.9.1", label: "0.9.1", packVersion: "0.9.1", gameVersion: "1.22.5", commit: "9910000000000000000000000000000000000000" },
      ],
    };
    expect(versions).toEqual(expected);
    expect(JSON.parse(readFileSync(path.join(out, "versions.json"), "utf8"))).toEqual(expected);
    expect(readdirSync(out).sort()).toEqual(["main", "v0.10.0", "v0.9.1", "v1.0.0", "v1.0.0-rc.1", "versions.json"]);
  });

  it("copies each export under its id", () => {
    const { src, out } = run(FIVE);
    expect(readFileSync(path.join(out, "v0.9.1", "export.json"), "utf8")).toBe(
      readFileSync(path.join(src, "r1", "seraphhorizons_0.9.1_recipes.json"), "utf8"),
    );
    expect(JSON.parse(readFileSync(path.join(out, "main", "export.json"), "utf8")).pack.version).toBe("0.10.1");
  });

  it("defaults to a release candidate when it is the newest release", () => {
    const { versions } = run([
      { tag: "v0.9.0", commit: "a".repeat(40), export: ["0.9.0", "1.22.7"] },
      { tag: "v1.0.0-rc.1", commit: "b".repeat(40), export: ["1.0.0-rc.1", "1.22.7"] },
    ]);
    expect(versions.default).toBe("v1.0.0-rc.1");
  });

  it("skips a release without an export, with a note", () => {
    const { versions, log, out } = run([
      { tag: "v0.2.0", commit: "c".repeat(40), export: ["0.2.0", "1.22.7"] },
      { tag: "v0.1.0", commit: "d".repeat(40), export: null },
    ]);
    expect(versions.versions.map((v) => v.id)).toEqual(["v0.2.0"]);
    expect(versions.default).toBe("v0.2.0");
    expect(log).toContain("skip v0.1.0: no recipe export attached");
    expect(existsSync(path.join(out, "v0.1.0"))).toBe(false);
  });

  it("defaults to main when no versioned release has an export", () => {
    const { versions } = run([
      { tag: "next", commit: "e".repeat(40), export: ["0.1.0", "1.22.7"] },
      { tag: "v0.1.0", commit: "d".repeat(40), export: null },
    ]);
    expect(versions).toEqual({
      default: "main",
      versions: [{ id: "main", label: "main (eeeeeee)", packVersion: "0.1.0", gameVersion: "1.22.7", commit: "e".repeat(40) }],
    });
  });

  it("fails on an invalid export and names the release and the path", () => {
    const src = releasesDir([
      { tag: "next", commit: "e".repeat(40), export: ["0.1.0", "1.22.7"] },
      { tag: "v0.1.0", commit: "d".repeat(40), export: ["0.1.0", "1.22.7"], breakIt: true },
    ]);
    const out = path.join(tempDir(), "data");
    let error: unknown;
    try {
      assemble({ releasesDir: src, outDir: out, pipeline, log: () => {} });
    } catch (e) {
      error = e;
    }
    expect(error).toBeInstanceOf(AssembleError);
    expect((error as Error).message).toBe(
      [
        "invalid recipe exports:",
        "v0.1.0 (r1/seraphhorizons_0.1.0_recipes.json):",
        "  /recipeTypes/grid/count: expected 1 (records of this type), found 7 [recipe-type-count]",
        "  1 error",
      ].join("\n"),
    );
    expect(existsSync(path.join(out, "versions.json"))).toBe(false);
  });

  it("fails when there is nothing to publish", () => {
    const src = releasesDir([{ tag: "v0.1.0", commit: "d".repeat(40), export: null }]);
    expect(() => assemble({ releasesDir: src, outDir: path.join(tempDir(), "o"), pipeline, log: () => {} })).toThrow(
      "no release has a recipe export",
    );
  });

  it("only writes ids that are plain directory names", () => {
    const { out, log, versions } = run([
      { tag: "../evil", commit: "1".repeat(40), export: ["0.0.1", "1.22.7"] },
      { tag: "v1.0.0/../../x", commit: "2".repeat(40), export: ["0.0.2", "1.22.7"] },
      { tag: "v1.0.0+build.5", commit: "3".repeat(40), export: ["0.0.3", "1.22.7"] },
      { tag: "feature-x", commit: "4".repeat(40), export: ["0.0.4", "1.22.7"] },
      { tag: "v1.0", commit: "5".repeat(40), export: ["0.0.5", "1.22.7"] },
      { tag: "v0.3.0", commit: "6".repeat(40), export: ["0.3.0", "1.22.7"] },
    ]);
    expect(versions.versions.map((v) => v.id)).toEqual(["v0.3.0"]);
    expect(readdirSync(out).sort()).toEqual(["v0.3.0", "versions.json"]);
    expect(readdirSync(path.dirname(out))).toEqual(["data"]);
    expect(log.filter((l) => l.startsWith("skip"))).toHaveLength(5);
  });

  it("refuses to write into a directory that is not empty", () => {
    const src = releasesDir(FIVE);
    const out = tempDir();
    writeFileSync(path.join(out, "stale.json"), "{}");
    expect(() => assemble({ releasesDir: src, outDir: out, pipeline, log: () => {} })).toThrow("is not empty");
  });
});

describe("version ids and order", () => {
  it.each([
    ["next", "main"],
    ["v0.1.0", "v0.1.0"],
    ["v1.0.0-rc.1", "v1.0.0-rc.1"],
    ["v1.0.0-alpha.beta-2", "v1.0.0-alpha.beta-2"],
    ["main", undefined],
    // Mod releases (mod-release.yml, and next.yml's seraphhorizons-next) carry no recipe export and are not site versions.
    ["allowedvariantsfix-v1.0.0", undefined],
    ["seraphhorizons-next", undefined],
    ["0.1.0", undefined],
    ["v01.0.0", undefined],
    ["v1.0.0+meta", undefined],
    ["v1.0.0-", undefined],
    ["v1.0.0-rc..1", undefined],
    ["../v1.0.0", undefined],
    ["v1.0.0/x", undefined],
  ])("%s -> %s", (tag, id) => {
    expect(versionId(tag)).toBe(id);
  });

  // Each list is oldest to newest (semver 2.0.0 section 11).
  it.each([
    [["v0.9.0", "v0.10.0"]],
    [["v0.10.0", "v1.0.0-rc.1", "v1.0.0"]],
    [["v1.0.0-alpha", "v1.0.0-alpha.1", "v1.0.0-alpha.beta", "v1.0.0-beta", "v1.0.0-beta.2", "v1.0.0-beta.11", "v1.0.0-rc.1", "v1.0.0"]],
    [["v1.9.9", "v1.10.0", "v2.0.0"]],
  ])("orders %j", (tags) => {
    for (let i = 0; i + 1 < tags.length; i++) {
      const a = parseTag(tags[i]!)!;
      const b = parseTag(tags[i + 1]!)!;
      expect(compareVersions(a, b), `${tags[i]} < ${tags[i + 1]}`).toBe(-1);
      expect(compareVersions(b, a), `${tags[i + 1]} > ${tags[i]}`).toBe(1);
    }
    expect(compareVersions(parseTag("v1.2.3")!, parseTag("v1.2.3")!)).toBe(0);
  });
});

describe("fetch", () => {
  it("downloads each export into a directory named by id and writes the manifest", () => {
    const downloads: [string, string, string][] = [];
    const out = tempDir();
    const source: ReleaseSource = {
      tags: () => ["next", "v0.2.0", "v0.1.0", "weird/tag"],
      assets: (tag) =>
        ({
          next: ["seraphhorizons_0.2.0.cairn", "seraphhorizons_0.2.0_recipes.json", "SHA256SUMS"],
          "v0.2.0": ["seraphhorizons_0.2.0_recipes.json"],
          "v0.1.0": ["seraphhorizons_0.1.0.cairn"],
        })[tag] ?? [],
      commit: (tag) => `sha-of-${tag}`,
      download: (tag, asset, dir) => {
        downloads.push([tag, asset, path.relative(out, dir)]);
      },
    };
    const releases = fetchReleases(source, out, () => {});
    const expected = [
      { tag: "next", commit: "sha-of-next", export: "main/seraphhorizons_0.2.0_recipes.json" },
      { tag: "v0.2.0", commit: "sha-of-v0.2.0", export: "v0.2.0/seraphhorizons_0.2.0_recipes.json" },
      { tag: "v0.1.0", commit: "sha-of-v0.1.0", export: null },
    ];
    expect(releases).toEqual(expected);
    expect(JSON.parse(readFileSync(path.join(out, "releases.json"), "utf8"))).toEqual({ releases: expected });
    expect(downloads).toEqual([
      ["next", "seraphhorizons_0.2.0_recipes.json", "main"],
      ["v0.2.0", "seraphhorizons_0.2.0_recipes.json", "v0.2.0"],
    ]);
  });

  it("refuses a release with two exports", () => {
    const source: ReleaseSource = {
      tags: () => ["v0.1.0"],
      assets: () => ["a_0.1.0_recipes.json", "b_0.1.0_recipes.json"],
      commit: () => "x",
      download: () => {},
    };
    expect(() => fetchReleases(source, tempDir(), () => {})).toThrow("more than one");
  });
});
