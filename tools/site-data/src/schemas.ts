// One JSON Schema per export schemaVersion. The current one is
// schema/recipe-export.schema.json; older ones are archived as
// schema/archive/recipe-export.v<N>.schema.json when the version is bumped
// (docs/recipe-browser/deploy.md).

import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { Ajv2020, type ErrorObject, type ValidateFunction } from "ajv/dist/2020.js";
import { describe, ptr, type ErrorReport } from "./report.js";

/** schema/ at the repository root, from both src/ and dist/. */
export const DEFAULT_SCHEMA_DIR = fileURLToPath(new URL("../../../schema/", import.meta.url));

export class SchemaSet {
  private readonly compiled = new Map<number, ValidateFunction>();

  /**
   * @param current the schemaVersion this tool reads and writes
   * @param load returns the schema for a version, or undefined when there is none
   */
  constructor(
    readonly current: number,
    private readonly load: (version: number) => object | undefined,
  ) {}

  static fromDir(dir: string = DEFAULT_SCHEMA_DIR): SchemaSet {
    const currentFile = path.join(dir, "recipe-export.schema.json");
    const current = JSON.parse(readFileSync(currentFile, "utf8")) as {
      properties?: { schemaVersion?: { const?: unknown } };
    };
    const version = current.properties?.schemaVersion?.const;
    if (typeof version !== "number" || !Number.isInteger(version)) {
      throw new Error(`${currentFile}: properties.schemaVersion.const must be an integer`);
    }
    return new SchemaSet(version, (v) => {
      if (v === version) return current;
      const archived = path.join(dir, "archive", `recipe-export.v${v}.schema.json`);
      return existsSync(archived) ? (JSON.parse(readFileSync(archived, "utf8")) as object) : undefined;
    });
  }

  has(version: number): boolean {
    return this.compiled.has(version) || this.load(version) !== undefined;
  }

  /** Validates `doc` against the schema of `version`, adding problems to `report`. */
  validate(version: number, doc: unknown, report: ErrorReport): void {
    const fn = this.validator(version);
    if (fn(doc)) return;
    for (const e of fn.errors ?? []) addAjvError(e, report);
  }

  private validator(version: number): ValidateFunction {
    let fn = this.compiled.get(version);
    if (!fn) {
      const schema = this.load(version);
      if (!schema) throw new Error(`no schema for schemaVersion ${version}`);
      // A fresh Ajv per version: the archived schemas share one $id.
      const ajv = new Ajv2020({ allErrors: true, strict: true, verbose: true });
      fn = ajv.compile(schema);
      this.compiled.set(version, fn);
    }
    return fn;
  }
}

function addAjvError(e: ErrorObject, report: ErrorReport): void {
  const kind = `schema:${e.keyword}`;
  const p = e.params as Record<string, unknown>;
  switch (e.keyword) {
    case "propertyNames":
      // Ajv also reports the failing name's own error, which carries the detail.
      return;
    case "required":
      report.add(kind, `${e.instancePath}/${ptr(String(p.missingProperty))}`, "a required property", "nothing");
      return;
    case "additionalProperties":
      report.add(kind, `${e.instancePath}/${ptr(String(p.additionalProperty))}`, "no such property (not in the schema)", "one");
      return;
  }
  if (e.propertyName !== undefined) {
    // A failing key of an object, e.g. an item code without a domain.
    report.add(`schema:propertyName-${e.keyword}`, `${e.instancePath}/${ptr(e.propertyName)}`,
      `a key that ${ajvMessage(e)}`, JSON.stringify(e.propertyName));
    return;
  }
  report.add(kind, e.instancePath, ajvMessage(e), describe(e.data));
}

function ajvMessage(e: ErrorObject): string {
  const p = e.params as Record<string, unknown>;
  switch (e.keyword) {
    case "const":
      return `the value ${JSON.stringify(p.allowedValue)}`;
    case "enum":
      return `one of ${JSON.stringify(p.allowedValues)}`;
    case "pattern":
      return `matches ${String(p.pattern)}`;
    default:
      return (e.message ?? e.keyword).replace(/^must /, "to ");
  }
}
