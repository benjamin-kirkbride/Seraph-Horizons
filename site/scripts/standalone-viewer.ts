// Builds one model of site/models.json into a single self-contained HTML file: the site's own model page
// (src/components/ModelPage.svelte, the rig maths and the three.js scene) with the model's shape and rig
// built in, three.js bundled, the scripts and styles inlined. It fetches nothing, so it opens from a
// file or as a shared page, for reviewing a model before it is published. The site's own build is
// untouched: this is a separate Vite build with its own entry (scripts/standalone/).
//
//   node --import tsx scripts/standalone-viewer.ts <model id> <out.html>     (from site/)
import { copyFileSync, mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import { build, type Plugin } from "vite";
import { loadModels } from "./models.ts";

const here = dirname(fileURLToPath(import.meta.url));
const site = resolve(here, "..");
const repo = resolve(site, "..");
const VIRTUAL = "virtual:standalone-model";

/** The model's index entry, shape and rig, as one module. */
function modelModule(id: string): Plugin {
  const { index, files } = loadModels();
  const model = index.models.find((m) => m.id === id);
  if (!model) throw new Error(`no model "${id}" in site/models.json (${index.models.map((m) => m.id).join(", ")})`);
  const source = (path: string | undefined) => files.find((f) => f.path === path)?.source;
  const read = (path: string | undefined) => (path ? (JSON.parse(readFileSync(resolve(repo, path), "utf8")) as unknown) : null);
  const shape = read(source(model.shape));
  const rig = read(source(model.rig));
  return {
    name: "standalone-model",
    resolveId: (id) => (id === VIRTUAL ? `\0${VIRTUAL}` : null),
    load: (id) =>
      id === `\0${VIRTUAL}`
        ? `export const model = ${JSON.stringify(model)};\nexport const shape = JSON.parse(${JSON.stringify(JSON.stringify(shape))});\nexport const rig = JSON.parse(${JSON.stringify(JSON.stringify(rig))});\n`
        : null,
  };
}

/** The site's data module, swapped for the one that answers from the page. */
function localData(): Plugin {
  const swap = resolve(here, "standalone", "model-data.ts");
  return {
    name: "standalone-data",
    enforce: "pre",
    async resolveId(id, importer, options) {
      if (!importer || !id.endsWith("model-data.ts")) return null;
      const hit = await this.resolve(id, importer, { ...options, skipSelf: true });
      return hit && resolve(hit.id) === resolve(site, "src", "lib", "model-data.ts") ? swap : null;
    },
  };
}

/** Every script and stylesheet of the build inlined into its HTML page. */
function inlineAll(): Plugin {
  return {
    name: "standalone-inline",
    enforce: "post",
    generateBundle(_options, bundle) {
      const page = Object.values(bundle).find((f) => f.type === "asset" && f.fileName.endsWith(".html"));
      if (!page || page.type !== "asset") throw new Error("the build wrote no page");
      let html = String(page.source);
      for (const [name, file] of Object.entries(bundle)) {
        if (file === page) continue;
        const ref = new RegExp(`<script[^>]*src="[^"]*${file.fileName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}"[^>]*></script>`);
        const css = new RegExp(`<link[^>]*href="[^"]*${file.fileName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}"[^>]*>`);
        if (file.type === "chunk") {
          // the dynamic import of the scene is inlined; Vite leaves its preload list's marker behind
          const code = file.code.replace(/__VITE_PRELOAD__/g, "void 0").replace(/<\/script/gi, "<\\/script");
          if (!ref.test(html)) throw new Error(`${file.fileName} is not loaded by the page: a chunk was split off`);
          html = html.replace(ref, () => `<script type="module">\n${code}</script>`);
        } else if (file.fileName.endsWith(".css")) {
          html = html.replace(css, () => `<style>\n${String(file.source)}</style>`);
        } else {
          throw new Error(`${file.fileName}: only scripts and styles can be inlined`);
        }
        delete bundle[name];
      }
      page.source = html;
    },
  };
}

async function main() {
  const [id, out] = process.argv.slice(2);
  if (!id || !out) throw new Error("usage: standalone-viewer.ts <model id> <out.html>");
  const dir = mkdtempSync(join(tmpdir(), "standalone-viewer-"));
  try {
    await build({
      configFile: false,
      root: resolve(here, "standalone"),
      base: "./",
      logLevel: "warn",
      plugins: [svelte(), modelModule(id), localData(), inlineAll()],
      build: {
        target: "es2022",
        outDir: dir,
        emptyOutDir: true,
        assetsInlineLimit: Number.MAX_SAFE_INTEGER,
        chunkSizeWarningLimit: 20000,
        modulePreload: { polyfill: false },
        rolldownOptions: { output: { codeSplitting: false } },
      },
    });
    copyFileSync(join(dir, "index.html"), resolve(process.cwd(), out));
    console.log(`wrote ${resolve(process.cwd(), out)} (${Math.round(readFileSync(join(dir, "index.html")).length / 1024)} KiB)`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

main().catch((e: unknown) => {
  console.error((e as Error).message);
  process.exit(1);
});
