// The model viewer's data step, as a Vite plugin (vite.config.ts): reads site/models.json and
// publishes models/index.json plus each model's shape and rig, read from their place in the
// repository (mods-src/...), so the site never holds a second copy in git.
//
//   vite build (npm run build, pages.yml, e2e/serve.ts)   writes them into the build output
//   vite (npm run dev)                                     serves them from the repository on
//                                                          every request, and reloads the page
//                                                          when one of them changes
//
// A broken manifest or model file fails the build with the problem. The checks themselves are
// in src/lib/model-manifest.ts.
import { readFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import type { Plugin } from "vite";
import { MODEL_INDEX, publishModels, type ModelIndex, type PublishedFile } from "../src/lib/model-manifest.ts";

const site = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repo = resolve(site, "..");
export const MANIFEST = resolve(site, "models.json");

function repoFile(path: string): string {
  const full = resolve(repo, path);
  if (relative(repo, full).startsWith("..")) throw new Error(`${path} is outside the repository`);
  return full;
}

function readJson(path: string): unknown {
  const full = repoFile(path);
  let text: string;
  try {
    text = readFileSync(full, "utf8");
  } catch {
    throw new Error(`${path} does not exist (named in site/models.json)`);
  }
  try {
    return JSON.parse(text);
  } catch (e) {
    throw new Error(`${path} is not JSON: ${(e as Error).message}`);
  }
}

/** Reads and checks the manifest and every file it names. */
export function loadModels(): { index: ModelIndex; files: PublishedFile[] } {
  return publishModels(JSON.parse(readFileSync(MANIFEST, "utf8")), readJson);
}

export function modelsPlugin(): Plugin {
  return {
    name: "seraph-models",
    configureServer(server) {
      const watched = new Set<string>([MANIFEST]);
      const watch = (files: PublishedFile[]) => {
        for (const f of files) {
          const full = repoFile(f.source);
          if (!watched.has(full)) {
            watched.add(full);
            server.watcher.add(full);
          }
        }
      };
      server.watcher.add(MANIFEST);
      server.watcher.on("change", (file) => {
        if (watched.has(resolve(file))) server.ws.send({ type: "full-reload" });
      });
      server.middlewares.use((req, res, next) => {
        const path = decodeURIComponent(new URL(req.url ?? "/", "http://localhost").pathname).replace(/^\/+/, "");
        if (!path.startsWith("models/")) return next();
        try {
          const { index, files } = loadModels();
          watch(files);
          let body: string | Buffer | null = null;
          if (path === MODEL_INDEX) body = JSON.stringify(index);
          else {
            const f = files.find((x) => x.path === path);
            if (f) body = readFileSync(repoFile(f.source));
          }
          if (body === null) {
            res.statusCode = 404;
            res.end();
            return;
          }
          res.setHeader("content-type", "application/json");
          res.setHeader("cache-control", "no-store");
          res.end(body);
        } catch (e) {
          server.config.logger.error(`models: ${(e as Error).message}`);
          res.statusCode = 500;
          res.setHeader("content-type", "text/plain; charset=utf-8");
          res.end((e as Error).message);
        }
      });
    },
    generateBundle() {
      const { index, files } = loadModels();
      for (const f of files) {
        this.addWatchFile(repoFile(f.source));
        this.emitFile({ type: "asset", fileName: f.path, source: readFileSync(repoFile(f.source)) });
      }
      this.addWatchFile(MANIFEST);
      this.emitFile({ type: "asset", fileName: MODEL_INDEX, source: JSON.stringify(index) });
      this.info(`models: ${index.models.map((m) => m.id).join(", ") || "none"} (${files.length} files)`);
    },
  };
}
