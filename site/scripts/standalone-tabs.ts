// Builds several models, and HTML tabs, into one self-contained HTML page with tabs: the tabbed sibling
// of standalone-viewer.ts, for reviewing models being reworked on different branches side by side.
// The spec (scripts/standalone/tabs-spec.ts) lists the tabs; each model tab is read from its own
// checkout (site/models.json, the shape and the rig) and drawn by that checkout's own model page
// (its site/src), bundled with three.js; tabs whose site/src are the same share a bundle. Only the
// shown tab's viewer is mounted (scripts/standalone/tabs-shell.ts), and the hash remembers the tab.
// It fetches nothing, so it opens from a file or as a shared page. A model tab that cannot be built
// is left out and named on the page and in the output; the rest still build.
//
//   node --import tsx scripts/standalone-tabs.ts <spec.json> <out.html>     (from site/)
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import { build, type Plugin, type Rollup } from "vite";
import type { ModelIndex, PublishedFile, PublishedModel } from "../src/lib/model-manifest.ts";
import { THEMES } from "../src/lib/theme.ts";
import { manifestOf, readJsonAt, REPO } from "./models.ts";
import { escapeHtml, groupBy, parseSpec, type HtmlTab, type ModelTab } from "./standalone/tabs-spec.ts";

const here = dirname(fileURLToPath(import.meta.url));
const site = resolve(here, "..");
const standalone = resolve(here, "standalone");
const DATA = "virtual:standalone-tabs-data";
const PAGE = "virtual:standalone-tabs-page";

interface BuiltModel {
  tab: ModelTab;
  model: PublishedModel;
  shape: unknown;
  rig: unknown;
  bogie: unknown;
  /** The checkout whose site/src draws it. */
  codeRoot: string;
  provenance: string;
}

interface Skipped {
  label: string;
  reason: string;
}

/** A checkout's model, checked by that checkout's own manifest code, as the site's build would. */
async function loadModel(tab: ModelTab, codeRoot: string): Promise<Omit<BuiltModel, "provenance">> {
  const manifestCode = resolve(codeRoot, "site", "src", "lib", "model-manifest.ts");
  const { publishModels } = (await import(pathToFileURL(manifestCode).href)) as {
    publishModels: (raw: unknown, read: (p: string) => unknown) => { index: ModelIndex; files: PublishedFile[] };
  };
  let raw: { models?: { id?: unknown }[] };
  try {
    raw = JSON.parse(readFileSync(manifestOf(tab.repo), "utf8")) as typeof raw;
  } catch (e) {
    throw new Error(`no readable site/models.json in ${tab.repo}: ${(e as Error).message}`);
  }
  const ids = (raw.models ?? []).map((m) => String(m.id));
  if (!ids.includes(tab.id)) throw new Error(`no model "${tab.id}" in ${tab.repo}/site/models.json (${ids.join(", ")})`);
  // Only this tab's model is checked: another model broken on that branch is not this tab's problem.
  const read = readJsonAt(tab.repo);
  const { index, files } = publishModels({ ...raw, models: (raw.models ?? []).filter((m) => m.id === tab.id) }, read);
  const model = index.models[0]!;
  const file = (path: string | undefined) => {
    const source = files.find((f) => f.path === path)?.source;
    return source ? read(source) : null;
  };
  return { tab, model, shape: file(model.shape), rig: file(model.rig), bogie: file(model.bogie), codeRoot };
}

/** Which commit a checkout is at, for the tab's note. */
function provenance(root: string): string {
  const git = (...args: string[]) => {
    try {
      return execFileSync("git", ["-C", root, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
    } catch {
      return "";
    }
  };
  // The commit, not a branch name: a detached checkout's commit is often the tip of several branches.
  const at = git("log", "-1", "--format=%h (%cs): %s");
  return at || root;
}

/** The content of a checkout's site/src: tabs drawn by the same code share a bundle. */
function codeHash(codeRoot: string): string {
  const src = resolve(codeRoot, "site", "src");
  const hash = createHash("sha256");
  const files = (readdirSync(src, { recursive: true }) as string[]).filter((f) => statSync(join(src, f)).isFile()).sort();
  for (const f of files) hash.update(`${f}\0`).update(readFileSync(join(src, f))).update("\0");
  return hash.digest("hex").slice(0, 10);
}

/** A bundle's virtual modules: its tabs' data and its checkout's model page. */
function bundleModules(group: string, codeSrc: string, models: BuiltModel[]): Plugin {
  const tabs = Object.fromEntries(models.map((m) => [m.tab.slug, { model: m.model, shape: m.shape, rig: m.rig, bogie: m.bogie }]));
  return {
    name: "standalone-tabs-modules",
    enforce: "pre",
    resolveId: (id) => (id === DATA ? `\0${DATA}` : id === PAGE ? resolve(codeSrc, "components", "ModelPage.svelte") : null),
    load: (id) =>
      id === `\0${DATA}` ? `export const group = ${JSON.stringify(group)};\nexport const tabs = JSON.parse(${JSON.stringify(JSON.stringify(tabs))});\n` : null,
  };
}

/** The checkout's data module, swapped for the one that answers from the page. */
function localData(codeSrc: string): Plugin {
  const swap = resolve(standalone, "tabs-model-data.ts");
  const original = resolve(codeSrc, "lib", "model-data.ts");
  return {
    name: "standalone-tabs-data",
    enforce: "pre",
    async resolveId(id, importer, options) {
      if (!importer || !id.endsWith("model-data.ts")) return null;
      const hit = await this.resolve(id, importer, { ...options, skipSelf: true });
      return hit && resolve(hit.id) === original ? swap : null;
    },
  };
}

/** One entry built into a single classic script (an IIFE), its styles injected by its own code. */
async function bundle(entry: string, plugins: Plugin[]): Promise<string> {
  const result = (await build({
    configFile: false,
    root: standalone,
    logLevel: "warn",
    define: { "process.env.NODE_ENV": JSON.stringify("production") },
    plugins: [svelte({ emitCss: false }), ...plugins],
    build: {
      write: false,
      target: "es2022",
      minify: true,
      chunkSizeWarningLimit: 20000,
      lib: { entry, formats: ["iife"], name: "standaloneTabs" },
      rolldownOptions: { output: { codeSplitting: false } },
    },
  })) as Rollup.RollupOutput | Rollup.RollupOutput[];
  const outputs = (Array.isArray(result) ? result : [result]).flatMap((r) => r.output);
  const chunks = outputs.filter((o): o is Rollup.OutputChunk => o.type === "chunk");
  if (chunks.length !== 1) throw new Error(`${relative(standalone, entry)} built into ${chunks.length} scripts, not one`);
  const extra = outputs.filter((o) => o.type === "asset");
  if (extra.length > 0) throw new Error(`${relative(standalone, entry)} built files that cannot be inlined: ${extra.map((o) => o.fileName).join(", ")}`);
  return chunks[0]!.code;
}

/** Script text safe inside <script>…</script>. */
const scriptSafe = (code: string) => code.replace(/<\/script/gi, "<\\/script").replace(/<!--/g, "<\\!--");

async function htmlTab(tab: HtmlTab): Promise<string> {
  if (/\.[cm]?[jt]s$/.test(tab.file)) {
    const mod = (await import(pathToFileURL(tab.file).href)) as { default?: unknown };
    const v = typeof mod.default === "function" ? await (mod.default as () => unknown)() : mod.default;
    if (typeof v !== "string") throw new Error(`${tab.file}: its default export is not an HTML string or a function returning one`);
    return v;
  }
  return readFileSync(tab.file, "utf8");
}

async function main() {
  const [specPath, out] = process.argv.slice(2);
  if (!specPath || !out) throw new Error("usage: standalone-tabs.ts <spec.json> <out.html>");
  const specFile = resolve(process.cwd(), specPath);
  const spec = parseSpec(JSON.parse(readFileSync(specFile, "utf8")), dirname(specFile));
  const skipped: Skipped[] = [];

  // Every model tab's data, from its own checkout.
  const built: BuiltModel[] = [];
  for (const tab of spec.tabs) {
    if (tab.kind !== "model") continue;
    const codeRoot = spec.viewer === "own" ? tab.repo : REPO;
    try {
      built.push({ ...(await loadModel(tab, codeRoot)), provenance: provenance(tab.repo) });
    } catch (e) {
      skipped.push({ label: tab.label, reason: (e as Error).message });
    }
  }

  // One bundle per distinct viewer code. Each checkout's site/src is built from a copy inside this site,
  // so its imports of svelte and three, and its TypeScript settings, are this site's: a fresh worktree
  // of a branch has no node_modules of its own.
  const groups = groupBy(built, (m) => codeHash(m.codeRoot));
  const scripts: string[] = [];
  const groupOf = new Map<string, string>();
  const work = mkdtempSync(resolve(site, ".standalone-tabs-"));
  try {
    for (const [hash, models] of groups) {
      const group = `v${hash}`;
      const codeSrc = resolve(work, group, "src");
      cpSync(resolve(models[0]!.codeRoot, "site", "src"), codeSrc, { recursive: true });
      try {
        scripts.push(await bundle(resolve(standalone, "tab-viewer.ts"), [bundleModules(group, codeSrc, models), localData(codeSrc)]));
        for (const m of models) groupOf.set(m.tab.slug, group);
        console.log(`bundle ${group}: ${models.map((m) => m.tab.label).join(", ")} (viewer code of ${models[0]!.codeRoot})`);
      } catch (e) {
        for (const m of models) skipped.push({ label: m.tab.label, reason: `its viewer code (${models[0]!.codeRoot}) did not build: ${(e as Error).message}` });
      }
    }
  } finally {
    rmSync(work, { recursive: true, force: true });
  }
  const shell = await bundle(resolve(standalone, "tabs-shell.ts"), []);

  // The page.
  const tabs = spec.tabs.filter((t) => t.kind === "html" || groupOf.has(t.slug));
  if (tabs.length === 0) throw new Error(`no tab could be built:\n  ${skipped.map((s) => `${s.label}: ${s.reason}`).join("\n  ")}`);
  const modelOf = new Map(built.map((m) => [m.tab.slug, m]));
  const panels: string[] = [];
  for (const [i, tab] of tabs.entries()) {
    const head = `<section class="tab-panel" id="panel-${tab.slug}" role="tabpanel" aria-labelledby="tab-${tab.slug}"${i === 0 ? "" : " hidden"}`;
    if (tab.kind === "html") {
      panels.push(`${head}>\n${await htmlTab(tab)}\n</section>`);
    } else {
      const m = modelOf.get(tab.slug)!;
      const source = `Model <code>${escapeHtml(tab.id)}</code> at commit ${escapeHtml(m.provenance)}${tab.note ? `. ${escapeHtml(tab.note)}` : ""}`;
      panels.push(
        `${head} data-group="${groupOf.get(tab.slug)}">\n<p class="muted tab-source">${source}</p>\n<div class="model-mount"><noscript>The model viewer needs JavaScript.</noscript></div>\n</section>`,
      );
    }
  }
  const bar = tabs
    .map(
      (t, i) =>
        `<a role="tab" id="tab-${t.slug}" data-slug="${t.slug}" href="#${t.slug}" aria-controls="panel-${t.slug}" aria-selected="${i === 0}"${i === 0 ? "" : ' tabindex="-1"'}>${escapeHtml(t.label)}</a>`,
    )
    .join("\n");
  const notice =
    skipped.length === 0
      ? ""
      : `<div class="tab-notice"><strong>Not built:</strong><ul>${skipped.map((s) => `<li>${escapeHtml(s.label)}: ${escapeHtml(s.reason)}</li>`).join("")}</ul></div>`;
  const themes = THEMES.map((t) => `<option value="${t}">${t[0]!.toUpperCase()}${t.slice(1)}</option>`).join("");
  const css = [readFileSync(resolve(REPO, "site", "src", "app.css"), "utf8"), readFileSync(resolve(standalone, "tabs.css"), "utf8")];
  const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<meta name="color-scheme" content="light dark" />
<title>${escapeHtml(spec.title)}</title>
${css.map((c) => `<style>\n${c}</style>`).join("\n")}
</head>
<body>
<div class="tabs-page">
<header class="tabs-head">
<h1>${escapeHtml(spec.title)}</h1>
<label class="tabs-theme">Theme <select id="theme">${themes}</select></label>
</header>
<nav class="tab-bar" role="tablist" aria-label="Tabs">
${bar}
</nav>
${notice}
${panels.join("\n")}
</div>
${[...scripts, shell].map((s) => `<script>\n${scriptSafe(s)}</script>`).join("\n")}
</body>
</html>
`;
  const target = resolve(process.cwd(), out);
  writeFileSync(target, html);
  console.log(`wrote ${target} (${Math.round(html.length / 1024)} KiB): ${tabs.map((t) => t.label).join(", ")}`);
  for (const s of skipped) console.warn(`skipped ${s.label}: ${s.reason}`);
}

main().catch((e: unknown) => {
  console.error((e as Error).message);
  process.exit(1);
});
