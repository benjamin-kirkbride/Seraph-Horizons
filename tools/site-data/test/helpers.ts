import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const REPO = fileURLToPath(new URL("../../../", import.meta.url));
export const EXAMPLE_FILE = path.join(REPO, "schema/examples/minimal.json");

// Loosely typed so tests can break documents in any way they like.
export type Loose = any;

/** A fresh copy of schema/examples/minimal.json. */
export function example(): Loose {
  return JSON.parse(readFileSync(EXAMPLE_FILE, "utf8"));
}

export function tempDir(): string {
  return mkdtempSync(path.join(tmpdir(), "site-data-test-"));
}
