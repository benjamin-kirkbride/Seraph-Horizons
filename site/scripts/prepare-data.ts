// npm run prepare-data -- --export <export.json> --out <dir> [--lock <lock.json>] [--textures <dir>]
//
// Reads one export (already validated and migrated to the current schemaVersion by
// tools/site-data) and writes the files the app loads for that version into <dir>. The
// mods' ModDB asset ids come from --lock, by default the repo's pack/lock.json. The images of the
// textures the export's eidolon section names come from the texture store --textures, by default
// the repo's textures/ (tools/textures.py, docs/recipe-browser/icons.md); one the store lacks, or
// holds only as a Git LFS pointer, is left out with a warning and the page draws it as a colour.
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { parseArgs } from "node:util";
import type { RecipeExport } from "../src/lib/export.ts";
import { eidolonTexturePaths } from "../src/lib/eidolon-fittings.ts";
import { assetIdsFromLock, prepareData } from "../src/lib/prepare.ts";

function fail(message: string): never {
  console.error(`prepare-data: ${message}`);
  process.exit(1);
}

const { values } = parseArgs({
  options: {
    export: { type: "string" },
    out: { type: "string" },
    lock: { type: "string" },
    textures: { type: "string" },
  },
});
if (!values.export || !values.out) fail("usage: prepare-data --export <export.json> --out <dir> [--lock <lock.json>] [--textures <dir>]");

// npm runs scripts from the package directory; paths given on the command line are
// relative to where the user typed them.
const cwd = process.env.INIT_CWD ?? process.cwd();
const exportPath = resolve(cwd, values.export);
const outDir = resolve(cwd, values.out);
const lockPath = values.lock ? resolve(cwd, values.lock) : resolve(import.meta.dirname, "../../pack/lock.json");
const texturesDir = values.textures ? resolve(cwd, values.textures) : resolve(import.meta.dirname, "../../textures");

const started = Date.now();
let data: RecipeExport;
try {
  data = JSON.parse(readFileSync(exportPath, "utf8")) as RecipeExport;
} catch (e) {
  fail(`cannot read ${exportPath}: ${(e as Error).message}`);
}
if (data.schemaVersion !== 1) {
  fail(`${exportPath} has schemaVersion ${String(data.schemaVersion)}; this site reads 1. Migrate it with tools/site-data first.`);
}

// Without asset ids the site still builds; it just leaves mod names unlinked.
let assetIds: Record<string, number> = {};
if (existsSync(lockPath)) {
  try {
    assetIds = assetIdsFromLock(JSON.parse(readFileSync(lockPath, "utf8")));
  } catch (e) {
    console.warn(`prepare-data: cannot read ${lockPath}, so no ModDB links: ${(e as Error).message}`);
  }
  if (Object.keys(assetIds).length === 0) console.warn(`prepare-data: ${lockPath} has no ModDB asset ids`);
} else if (values.lock) {
  fail(`${lockPath} does not exist`);
} else {
  console.warn(`prepare-data: no ${lockPath}, so no ModDB links`);
}

const textures = data.eidolon ? readTextures(eidolonTexturePaths(data.eidolon)) : {};

/** The store's image of each path it has, as a PNG data URI; warns about the rest. */
function readTextures(paths: string[]): Record<string, string> {
  const out: Record<string, string> = {};
  let index: Record<string, string> = {};
  const indexPath = join(texturesDir, "index.json");
  if (existsSync(indexPath)) {
    try {
      index = (JSON.parse(readFileSync(indexPath, "utf8")) as { textures: Record<string, string> }).textures;
    } catch (e) {
      console.warn(`prepare-data: cannot read ${indexPath}, so the eidolon is drawn in colours: ${(e as Error).message}`);
    }
  } else if (values.textures) {
    fail(`${indexPath} does not exist`);
  }
  const missing: string[] = [];
  const pointers: string[] = [];
  for (const path of paths) {
    const digest = index[path];
    const file = digest ? join(texturesDir, digest.slice(0, 2), `${digest}.png`) : null;
    if (!file || !existsSync(file)) {
      missing.push(path);
      continue;
    }
    const bytes = readFileSync(file);
    // A checkout without `git lfs pull` has pointer files, not images.
    if (bytes.subarray(0, 40).toString("latin1").startsWith("version https://git-lfs")) pointers.push(path);
    else out[path] = `data:image/png;base64,${bytes.toString("base64")}`;
  }
  if (missing.length > 0) {
    console.warn(`prepare-data: ${missing.length} of ${paths.length} eidolon textures are not in ${texturesDir}, so the page draws them as colours: ${missing.join(", ")}`);
  }
  if (pointers.length > 0) {
    console.warn(`prepare-data: ${pointers.length} eidolon textures in ${texturesDir} are Git LFS pointers (run git lfs pull), so the page draws them as colours`);
  }
  return out;
}

const { files, meta } = prepareData(data, { assetIds, textures });

// Stale chunks from an earlier, larger export would otherwise linger in the build.
rmSync(join(outDir, "items"), { recursive: true, force: true });
rmSync(join(outDir, "recipes"), { recursive: true, force: true });
rmSync(join(outDir, "entities"), { recursive: true, force: true });
// An earlier export's power.json would otherwise outlive one without a power section.
rmSync(join(outDir, "power.json"), { force: true });
rmSync(join(outDir, "eidolon.json"), { force: true });
rmSync(join(outDir, "ores.json"), { force: true });
rmSync(join(outDir, "multiblocks"), { recursive: true, force: true });
rmSync(join(outDir, "multiblocks.json"), { force: true });
let bytes = 0;
for (const [path, value] of files) {
  const target = join(outDir, path);
  mkdirSync(dirname(target), { recursive: true });
  const text = JSON.stringify(value);
  bytes += text.length;
  writeFileSync(target, text);
}
console.log(
  `prepare-data: ${meta.itemCount} items, ${meta.recipeCount} recipes, ${meta.entityCount} entities, ` +
    `${Object.values(meta.mods).filter((m) => m.assetId !== undefined).length}/${Object.keys(meta.mods).length} mods on the ModDB, ${files.size} files, ` +
    `${(bytes / 1e6).toFixed(1)} MB in ${((Date.now() - started) / 1000).toFixed(1)} s -> ${outDir}`,
);
