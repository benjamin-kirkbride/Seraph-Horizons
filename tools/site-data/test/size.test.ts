import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it, vi } from "vitest";
import { main } from "../src/cli.js";
import { measure } from "../src/size.js";
import { tempDir } from "./helpers.js";

/** A built site of exactly `extra` + 700 bytes: main 300, v0.1.0 200, icons 100, app 100 + extra. */
function site(extra = 0): string {
  const dir = tempDir();
  const file = (rel: string, bytes: number) => {
    mkdirSync(path.dirname(path.join(dir, rel)), { recursive: true });
    writeFileSync(path.join(dir, rel), "x".repeat(bytes));
  };
  file("data/main/export.json", 250);
  file("data/main/items/a.json", 50);
  file("data/v0.1.0/export.json", 200);
  file("icons/ab/ab12.png", 60);
  file("icons/index.json", 40);
  file("index.html", 100 + extra);
  return dir;
}

const limits = { limitBytes: 1000, warnAt: 0.7, failAt: 0.95 };

describe("size", () => {
  it("reports each version, icons and the rest", () => {
    const r = measure(site(), limits);
    expect(r.rows).toEqual([
      { name: "data/main", bytes: 300 },
      { name: "data/v0.1.0", bytes: 200 },
      { name: "icons", bytes: 100 },
      { name: "app and other files", bytes: 100 },
    ]);
    expect(r.total).toBe(700);
  });

  it.each([
    [0, 700, "ok"], // exactly at the warning threshold
    [1, 701, "warn"],
    [250, 950, "warn"], // exactly at the failure threshold
    [251, 951, "fail"],
  ])("with %i extra bytes (%i total) is %s", (extra, total, level) => {
    const r = measure(site(extra), limits);
    expect(r.total).toBe(total);
    expect(r.level).toBe(level);
  });

  it("uses 70% and 95% of 1 GB by default", () => {
    const r = measure(site());
    expect([r.limitBytes, r.warnBytes, r.failBytes]).toEqual([1_000_000_000, 700_000_000, 950_000_000]);
  });

  it("rejects thresholds in the wrong order", () => {
    expect(() => measure(site(), { warnAt: 0.9, failAt: 0.8 })).toThrow("thresholds");
  });

  it("warns, fails and writes the step summary from the command line", () => {
    const summary = path.join(tempDir(), "summary.md");
    const lines: string[] = [];
    const log = vi.spyOn(console, "log").mockImplementation((l: string) => void lines.push(l));
    const args = (dir: string) => ["size", dir, "--limit-bytes", "1000", "--warn-at", "0.7", "--fail-at", "0.95"];
    try {
      expect(main(args(site(0)), { GITHUB_STEP_SUMMARY: summary })).toBe(0);
      expect(lines.some((l) => l.startsWith("::"))).toBe(false);

      expect(main(args(site(1)), { GITHUB_STEP_SUMMARY: summary })).toBe(0);
      expect(lines.filter((l) => l.startsWith("::warning::"))).toHaveLength(1);

      expect(main(args(site(251)), { GITHUB_STEP_SUMMARY: summary })).toBe(1);
      expect(lines.filter((l) => l.startsWith("::error::"))).toHaveLength(1);
    } finally {
      log.mockRestore();
    }
    const md = readFileSync(summary, "utf8");
    expect(md.match(/### Site size/g)).toHaveLength(3);
    expect(md).toContain("| data/main | 300 B | 30.0% |");
    expect(md).toContain("| **total** | **951 B** | **95.1%** |");
  });
});
