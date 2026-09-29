// Downloads the recipe export of every release with the gh CLI and writes the
// releases directory `assemble` reads. The only part that touches the network.

import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { versionId, type ReleaseEntry } from "./assemble.js";

export interface ReleaseSource {
  /** Tags of the published (not draft) releases. */
  tags(): string[];
  assets(tag: string): string[];
  commit(tag: string): string;
  download(tag: string, asset: string, dir: string): void;
}

export const EXPORT_SUFFIX = "_recipes.json";

export function ghSource(repo: string): ReleaseSource {
  const gh = (...args: string[]) => execFileSync("gh", args, { encoding: "utf8", maxBuffer: 64 << 20 }).trim();
  return {
    tags: () =>
      gh("release", "list", "--repo", repo, "--limit", "1000", "--json", "tagName,isDraft",
        "--jq", ".[] | select(.isDraft | not) | .tagName").split("\n").filter(Boolean),
    assets: (tag) =>
      gh("release", "view", tag, "--repo", repo, "--json", "assets", "--jq", ".assets[].name").split("\n").filter(Boolean),
    commit: (tag) => gh("api", `repos/${repo}/commits/${encodeURIComponent(tag)}`, "--jq", ".sha"),
    download: (tag, asset, dir) => {
      gh("release", "download", tag, "--repo", repo, "--pattern", asset, "--dir", dir, "--clobber");
    },
  };
}

export function fetchReleases(source: ReleaseSource, outDir: string, log: (l: string) => void = console.log): ReleaseEntry[] {
  mkdirSync(outDir, { recursive: true });
  const releases: ReleaseEntry[] = [];
  for (const tag of source.tags()) {
    const id = versionId(tag);
    if (id === undefined) {
      log(`skip ${tag}: not a release tag`);
      continue;
    }
    const exports = source.assets(tag).filter((a) => a.endsWith(EXPORT_SUFFIX));
    if (exports.length > 1) throw new Error(`${tag}: more than one *${EXPORT_SUFFIX} asset: ${exports.join(", ")}`);
    const commit = source.commit(tag);
    const asset = exports[0];
    if (asset === undefined) {
      releases.push({ tag, commit, export: null });
      continue;
    }
    // Stored under the site id, which is a safe directory name; the tag might not be.
    source.download(tag, asset, path.join(outDir, id));
    releases.push({ tag, commit, export: `${id}/${asset}` });
    log(`${tag}: ${asset}`);
  }
  writeFileSync(path.join(outDir, "releases.json"), JSON.stringify({ releases }, null, 2) + "\n");
  return releases;
}
