// Playwright's web server: builds the site, prepares the real export given in
// RECIPE_EXPORT, and serves the build output the way GitHub Pages will, under a sub-path.
//
//   /Seraph-Horizons/   the build with data and a few icons
//   /noicons/           the same build with no icons/ directory at all
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { createServer } from "node:http";
import { dirname, extname, join, normalize, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { crc32, deflateSync } from "node:zlib";
import { E2E_GROUPS, E2E_VALUES, E2E_VERSIONS, GEAR_ICON, ICON_CODE, PORT, REAL_ICONS, SUB_PATH, NO_ICONS_PATH } from "./config.ts";

const site = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const work = join(site, "e2e", ".work");
const root = join(work, "site");

const exportPath = process.env.RECIPE_EXPORT;
if (!exportPath) {
  console.error("RECIPE_EXPORT is not set: the end-to-end tests need the path of a real recipe export.");
  process.exit(1);
}

function run(args: string[]) {
  execFileSync(process.execPath, args, { cwd: site, stdio: "inherit" });
}

rmSync(work, { recursive: true, force: true });
mkdirSync(work, { recursive: true });
run([join(site, "node_modules/vite/bin/vite.js"), "build", "--outDir", root, "--emptyOutDir"]);
// The same export under two version ids, so the version switcher has somewhere to go.
const [first, ...others] = E2E_VERSIONS;
run(["--max-old-space-size=6144", "--import", "tsx", "scripts/prepare-data.ts", "--export", withValues(resolve(exportPath)), "--out", join(root, "data", first!.id)]);
for (const v of others) cpSync(join(root, "data", first!.id), join(root, "data", v.id), { recursive: true });
writeFileSync(join(root, "data", "versions.json"), JSON.stringify({ default: E2E_VERSIONS[0]!.id, versions: E2E_VERSIONS }));

// A few icons, so pages show real images for some items and placeholders for the rest:
// a solid test square, and a few of the pack's own icons.
const iconIndex: Record<string, string> = {};
function addIcon(code: string, png: Buffer) {
  const hash = createHash("sha256").update(png).digest("hex");
  mkdirSync(join(root, "icons", hash.slice(0, 2)), { recursive: true });
  writeFileSync(join(root, "icons", hash.slice(0, 2), `${hash}.png`), png);
  iconIndex[code] = hash;
}
addIcon(ICON_CODE, solidPng(8, 8, [0xb8, 0x73, 0x33]));
for (const [code, file] of Object.entries({ ...REAL_ICONS, [GEAR_ICON.code]: GEAR_ICON.file })) addIcon(code, readFileSync(join(site, "e2e", "icons", file)));
writeFileSync(join(root, "icons", "index.json"), JSON.stringify({ schemaVersion: 1, size: 64, icons: iconIndex }));

const types: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript",
  ".css": "text/css",
  ".json": "application/json",
  ".png": "image/png",
  ".svg": "image/svg+xml",
};

createServer((req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  let rel: string | null = null;
  let icons = true;
  if (url.pathname.startsWith(SUB_PATH)) rel = url.pathname.slice(SUB_PATH.length);
  else if (url.pathname.startsWith(NO_ICONS_PATH)) {
    rel = url.pathname.slice(NO_ICONS_PATH.length);
    icons = false;
  }
  if (rel === null || (!icons && rel.startsWith("icons/"))) {
    res.writeHead(404).end();
    return;
  }
  let file = normalize(join(root, decodeURIComponent(rel)));
  if (!file.startsWith(root)) {
    res.writeHead(403).end();
    return;
  }
  if (existsSync(file) && statSync(file).isDirectory()) file = join(file, "index.html");
  if (!existsSync(file)) {
    res.writeHead(404).end();
    return;
  }
  res.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream" });
  res.end(readFileSync(file));
}).listen(PORT, "127.0.0.1", () => {
  console.log(`serving ${root} at http://127.0.0.1:${PORT}${SUB_PATH}`);
});

/**
 * A copy of the export with E2E_VALUES, and made-up values when it has none of its own, and
 * with E2E_GROUPS added to its variant groups.
 */
function withValues(path: string): string {
  type Item = { value?: number; floorZero?: boolean };
  type Group = { title: string; members: string[] };
  const exp = JSON.parse(readFileSync(path, "utf8")) as { items: Record<string, Item>; variantGroups?: Record<string, Group> };
  const items = Object.entries(exp.items);
  if (!items.some(([, item]) => typeof item.value === "number")) {
    for (const [code, item] of items) {
      const h = createHash("sha256").update(code).digest().readUInt32BE(0);
      if (h % 10 !== 0) item.value = (h % 5_000_000) / 1000;
    }
  }
  for (const [code, fixed] of Object.entries(E2E_VALUES)) {
    const item = exp.items[code];
    if (!item) throw new Error(`${code} is not in the export; e2e/config.ts E2E_VALUES needs it`);
    delete item.value;
    delete item.floorZero;
    if (fixed) Object.assign(item, fixed);
  }
  // A code is in at most one group, and a group needs two members.
  const fixed = new Set([...Object.keys(E2E_VALUES), ...Object.values(E2E_GROUPS).flatMap((g) => g.members)]);
  const groups: Record<string, Group> = {};
  for (const [id, group] of Object.entries(exp.variantGroups ?? {})) {
    const members = group.members.filter((code) => !fixed.has(code));
    if (members.length >= 2) groups[id] = { ...group, members };
  }
  for (const [id, group] of Object.entries(E2E_GROUPS)) {
    for (const code of group.members) if (!exp.items[code]) throw new Error(`${code} is not in the export; e2e/config.ts E2E_GROUPS needs it`);
    groups[id] = group;
  }
  exp.variantGroups = groups;
  const out = join(work, "export-with-values.json");
  writeFileSync(out, JSON.stringify(exp));
  return out;
}

function solidPng(w: number, h: number, rgb: [number, number, number]): Buffer {
  const chunk = (type: string, body: Buffer) => {
    const len = Buffer.alloc(4);
    len.writeUInt32BE(body.length);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([Buffer.from(type), body])));
    return Buffer.concat([len, Buffer.from(type), body, crc]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr.set([8, 2, 0, 0, 0], 8);
  const row = Buffer.from([0, ...Array.from({ length: w }, () => rgb).flat()]);
  const raw = Buffer.concat(Array.from({ length: h }, () => row));
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}
