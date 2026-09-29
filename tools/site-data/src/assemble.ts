// Turns the downloaded exports of every release into the site's data directory:
// <out>/versions.json and <out>/<id>/export.json (docs/recipe-browser/README.md).
//
// Input is a releases directory as `fetch` writes it:
//   <releases>/releases.json  {"releases": [{"tag", "commit", "export": "<path>" | null}]}
//   <releases>/<path>         the release's *_recipes.json asset
// Keeping the download separate keeps this part testable without the network.

import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import type { Pipeline } from "./pipeline.js";
import { compareVersions, parseTag, type Version } from "./semver.js";

export interface ReleaseEntry {
  tag: string;
  commit: string;
  /** Path of the export relative to the releases directory; null when the release has none. */
  export: string | null;
}

export interface VersionEntry {
  id: string;
  label: string;
  packVersion: string;
  gameVersion: string;
  commit: string;
}

export interface VersionsJson {
  default: string;
  versions: VersionEntry[];
}

/** The rolling release built from main. */
export const NEXT_TAG = "next";
export const MAIN_ID = "main";

export class AssembleError extends Error {}

/**
 * The site id of a release tag: `main` for `next`, the tag itself for a semver
 * tag (v1.2.3, v1.0.0-rc.1), undefined for anything else. Every id returned is a
 * plain directory name.
 */
export function versionId(tag: string): string | undefined {
  if (tag === NEXT_TAG) return MAIN_ID;
  return parseTag(tag) ? tag : undefined;
}

export function assemble(opts: {
  releasesDir: string;
  outDir: string;
  pipeline: Pipeline;
  log?: (line: string) => void;
}): VersionsJson {
  const log = opts.log ?? ((line: string) => console.log(line));
  const manifest = JSON.parse(readFileSync(path.join(opts.releasesDir, "releases.json"), "utf8")) as {
    releases: ReleaseEntry[];
  };

  if (existsSync(opts.outDir) && readdirSync(opts.outDir).length > 0) {
    throw new AssembleError(`${opts.outDir} is not empty`);
  }
  mkdirSync(opts.outDir, { recursive: true });

  const found: { entry: VersionEntry; version: Version | undefined }[] = [];
  const failures: string[] = [];
  const ids = new Set<string>();

  for (const r of manifest.releases) {
    const id = versionId(r.tag);
    if (id === undefined) {
      log(`skip ${r.tag}: not a release tag (vX.Y.Z or ${NEXT_TAG})`);
      continue;
    }
    if (r.export === null) {
      log(`skip ${r.tag}: no recipe export attached`);
      continue;
    }
    if (ids.has(id)) throw new AssembleError(`${r.tag}: listed twice`);
    ids.add(id);

    const file = path.join(opts.releasesDir, r.export);
    const result = opts.pipeline.processFile(file);
    if (!result.ok) {
      failures.push(`${r.tag} (${r.export}):`, ...result.report.format().map((l) => `  ${l}`));
      continue;
    }

    const dir = path.join(opts.outDir, id);
    mkdirSync(dir);
    const out = path.join(dir, "export.json");
    // Unmigrated exports are copied byte for byte; no need to re-serialise 100 MB.
    if (result.applied.length === 0) copyFileSync(file, out);
    else writeFileSync(out, JSON.stringify(result.doc));

    const pack = result.doc.pack as { version: string; gameVersion: string };
    const version = parseTag(r.tag);
    found.push({
      version,
      entry: {
        id,
        label: id === MAIN_ID ? `main (${r.commit.slice(0, 7)})` : r.tag.slice(1),
        packVersion: pack.version,
        gameVersion: pack.gameVersion,
        commit: r.commit,
      },
    });
    const migrated = result.applied.length ? `, migrated from schemaVersion ${result.from}` : "";
    log(`${r.tag} -> ${id}${migrated}`);
  }

  if (failures.length > 0) {
    throw new AssembleError(["invalid recipe exports:", ...failures].join("\n"));
  }
  if (found.length === 0) throw new AssembleError("no release has a recipe export");

  // main first, then releases newest first.
  found.sort((a, b) => {
    if (!a.version || !b.version) return a.version ? 1 : b.version ? -1 : 0;
    return compareVersions(b.version, a.version);
  });
  const versions = found.map((f) => f.entry);
  const newestRelease = found.find((f) => f.version !== undefined);
  const result: VersionsJson = { default: newestRelease ? newestRelease.entry.id : MAIN_ID, versions };
  writeFileSync(path.join(opts.outDir, "versions.json"), JSON.stringify(result, null, 2) + "\n");
  return result;
}
