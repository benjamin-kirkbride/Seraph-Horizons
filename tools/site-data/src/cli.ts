// site-data: validate, migrate and assemble recipe exports for the recipe browser.

import { appendFileSync, mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { parseArgs } from "node:util";
import { assemble, AssembleError } from "./assemble.js";
import { fetchReleases, ghSource } from "./fetch.js";
import { Pipeline } from "./pipeline.js";
import { DEFAULT_SCHEMA_DIR } from "./schemas.js";
import { formatBytes, measure, summaryMarkdown } from "./size.js";

const USAGE = `usage: site-data <command> [options]

  validate <export.json>...           check exports against the schema and each other
  migrate <in.json> <out.json>        bring an export to the current schemaVersion
  fetch --repo <owner/name> --out <dir>
                                      download the export of every release (needs gh)
  assemble --releases <dir> --out <dir>
                                      write versions.json and <id>/export.json
  size <site dir> [--limit-bytes N] [--warn-at 0.7] [--fail-at 0.95]
                                      report the built site's size against the Pages limit

  --schema-dir <dir>                  schema directory (default: the repository's schema/)`;

export function main(argv: string[], env: NodeJS.ProcessEnv = process.env): number {
  const [command, ...rest] = argv;
  const { values, positionals } = parseArgs({
    args: rest,
    allowPositionals: true,
    options: {
      "schema-dir": { type: "string", default: DEFAULT_SCHEMA_DIR },
      repo: { type: "string" },
      releases: { type: "string" },
      out: { type: "string" },
      "limit-bytes": { type: "string" },
      "warn-at": { type: "string" },
      "fail-at": { type: "string" },
    },
  });
  const need = (name: string, v: string | undefined): string => {
    if (v === undefined) throw new UsageError(`${command}: --${name} is required`);
    return v;
  };

  switch (command) {
    case "validate": {
      if (positionals.length === 0) throw new UsageError("validate: give at least one file");
      const pipeline = Pipeline.fromDir(values["schema-dir"]);
      let failed = 0;
      for (const file of positionals) {
        const r = pipeline.processFile(file);
        if (r.ok) {
          console.log(`ok ${file} (schemaVersion ${r.from})`);
        } else {
          failed++;
          console.log(`FAIL ${file}`);
          for (const line of r.report.format()) console.log(`  ${line}`);
        }
      }
      return failed ? 1 : 0;
    }

    case "migrate": {
      const [input, output] = positionals;
      if (!input || !output || positionals.length !== 2) throw new UsageError("migrate: give <in> and <out>");
      const r = Pipeline.fromDir(values["schema-dir"]).processFile(input);
      if (!r.ok) {
        console.log(`FAIL ${input}`);
        for (const line of r.report.format()) console.log(`  ${line}`);
        return 1;
      }
      mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
      writeFileSync(output, JSON.stringify(r.doc));
      const steps = r.applied.map((s) => `\n  ${s.from} -> ${s.to}: ${s.description}`).join("");
      console.log(`${input}: schemaVersion ${r.from} -> ${r.doc.schemaVersion}${steps}`);
      return 0;
    }

    case "fetch": {
      fetchReleases(ghSource(need("repo", values.repo)), need("out", values.out));
      return 0;
    }

    case "assemble": {
      try {
        const v = assemble({
          releasesDir: need("releases", values.releases),
          outDir: need("out", values.out),
          pipeline: Pipeline.fromDir(values["schema-dir"]),
        });
        console.log(`default ${v.default}; versions ${v.versions.map((e) => e.id).join(", ")}`);
        return 0;
      } catch (e) {
        if (e instanceof AssembleError) {
          console.log(e.message);
          return 1;
        }
        throw e;
      }
    }

    case "size": {
      const [dir] = positionals;
      if (!dir) throw new UsageError("size: give the built site directory");
      const num = (name: string, v: string | undefined) => {
        if (v === undefined) return undefined;
        const n = Number(v);
        if (!Number.isFinite(n)) throw new UsageError(`--${name}: not a number: ${v}`);
        return n;
      };
      const opts: { limitBytes?: number; warnAt?: number; failAt?: number } = {};
      const limit = num("limit-bytes", values["limit-bytes"]);
      const warn = num("warn-at", values["warn-at"]);
      const fail = num("fail-at", values["fail-at"]);
      if (limit !== undefined) opts.limitBytes = limit;
      if (warn !== undefined) opts.warnAt = warn;
      if (fail !== undefined) opts.failAt = fail;
      const r = measure(dir, opts);
      const md = summaryMarkdown(r);
      console.log(md);
      if (env.GITHUB_STEP_SUMMARY) appendFileSync(env.GITHUB_STEP_SUMMARY, md + "\n");
      const total = `${formatBytes(r.total)} of ${formatBytes(r.limitBytes)}`;
      if (r.level === "fail") {
        console.log(`::error::The site is ${total}, above the failure threshold. See docs/recipe-browser/deploy.md.`);
        return 1;
      }
      if (r.level === "warn") {
        console.log(`::warning::The site is ${total}, above the warning threshold. See docs/recipe-browser/deploy.md.`);
      }
      return 0;
    }

    default:
      throw new UsageError(command ? `unknown command: ${command}` : "no command");
  }
}

export class UsageError extends Error {}

export function run(argv: string[]): void {
  try {
    process.exitCode = main(argv);
  } catch (e) {
    if (e instanceof UsageError || (e as { code?: string }).code?.startsWith("ERR_PARSE_ARGS")) {
      console.error(`${(e as Error).message}\n\n${USAGE}`);
      process.exitCode = 2;
    } else {
      throw e;
    }
  }
}
