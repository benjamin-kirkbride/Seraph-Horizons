// npm run prepare-data -- --export <export.json> --out <dir>
//
// Reads one export (already validated and migrated to the current schemaVersion by
// tools/site-data) and writes the files the app loads for that version into <dir>.
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { parseArgs } from "node:util";
import type { RecipeExport } from "../src/lib/export.ts";
import { prepareData } from "../src/lib/prepare.ts";

function fail(message: string): never {
  console.error(`prepare-data: ${message}`);
  process.exit(1);
}

const { values } = parseArgs({
  options: {
    export: { type: "string" },
    out: { type: "string" },
  },
});
if (!values.export || !values.out) fail("usage: prepare-data --export <export.json> --out <dir>");

// npm runs scripts from the package directory; paths given on the command line are
// relative to where the user typed them.
const cwd = process.env.INIT_CWD ?? process.cwd();
const exportPath = resolve(cwd, values.export);
const outDir = resolve(cwd, values.out);

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

const { files, meta } = prepareData(data);

// Stale chunks from an earlier, larger export would otherwise linger in the build.
rmSync(join(outDir, "items"), { recursive: true, force: true });
rmSync(join(outDir, "recipes"), { recursive: true, force: true });
let bytes = 0;
for (const [path, value] of files) {
  const target = join(outDir, path);
  mkdirSync(dirname(target), { recursive: true });
  const text = JSON.stringify(value);
  bytes += text.length;
  writeFileSync(target, text);
}
console.log(
  `prepare-data: ${meta.itemCount} items, ${meta.recipeCount} recipes, ${files.size} files, ` +
    `${(bytes / 1e6).toFixed(1)} MB in ${((Date.now() - started) / 1000).toFixed(1)} s -> ${outDir}`,
);
