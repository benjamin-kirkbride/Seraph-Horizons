// Validate, migrate, validate again: the path every export takes before the site
// reads it.

import { readFileSync } from "node:fs";
import { checkCrossReferences } from "./checks.js";
import { MigrationChain, MigrationError, type Doc, type MigrationStep } from "./migrate.js";
import { steps as realSteps } from "./migrations/index.js";
import { ErrorReport } from "./report.js";
import { DEFAULT_SCHEMA_DIR, SchemaSet } from "./schemas.js";

export type Result =
  | { ok: true; doc: Doc; from: number; applied: MigrationStep[] }
  | { ok: false; report: ErrorReport };

export class Pipeline {
  constructor(
    readonly schemas: SchemaSet,
    readonly chain: MigrationChain,
    readonly crossChecks: (doc: unknown, report: ErrorReport) => void = checkCrossReferences,
  ) {
    if (chain.current !== schemas.current) {
      throw new Error(`migrations end at version ${chain.current} but the current schema is version ${schemas.current}`);
    }
  }

  static fromDir(schemaDir: string = DEFAULT_SCHEMA_DIR): Pipeline {
    const schemas = SchemaSet.fromDir(schemaDir);
    return new Pipeline(schemas, new MigrationChain(schemas.current, realSteps));
  }

  process(doc: unknown): Result {
    const report = new ErrorReport();
    const fail = (path: string, expected: string, found: string): Result => {
      report.add("version", path, expected, found);
      return { ok: false, report };
    };
    if (doc === null || typeof doc !== "object" || Array.isArray(doc)) {
      return fail("", "a JSON object", Array.isArray(doc) ? "an array" : JSON.stringify(doc));
    }
    const d = doc as Doc;
    if (!Object.hasOwn(d, "schemaVersion")) {
      report.add("schema:required", "/schemaVersion", "a required property", "nothing");
      return { ok: false, report };
    }

    let from: number;
    try {
      from = this.chain.check(d.schemaVersion);
    } catch (e) {
      if (e instanceof MigrationError) return fail("/schemaVersion", "a known export version", e.message);
      throw e;
    }
    if (!this.schemas.has(from)) {
      return fail("/schemaVersion", `an archived schema for version ${from}`, "none in schema/archive/");
    }

    this.schemas.validate(from, d, report);
    if (!report.ok) return { ok: false, report };

    let migrated: { doc: Doc; applied: MigrationStep[] };
    try {
      migrated = this.chain.run(d);
    } catch (e) {
      if (e instanceof MigrationError) return fail("/schemaVersion", "a successful migration", e.message);
      throw e;
    }

    if (migrated.applied.length > 0) {
      this.schemas.validate(this.schemas.current, migrated.doc, report);
      if (!report.ok) return { ok: false, report };
    }
    this.crossChecks(migrated.doc, report);
    if (!report.ok) return { ok: false, report };
    return { ok: true, doc: migrated.doc, from, applied: migrated.applied };
  }

  processFile(file: string): Result {
    let doc: unknown;
    try {
      doc = JSON.parse(readFileSync(file, "utf8"));
    } catch (e) {
      const report = new ErrorReport();
      report.add("json", "", "a readable JSON file", (e as Error).message);
      return { ok: false, report };
    }
    return this.process(doc);
  }
}
