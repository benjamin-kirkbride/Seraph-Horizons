// GitHub Pages refuses sites over about 1 GB, and the site keeps every release, so
// the built site's size is reported on every deploy with a warning well before the
// limit.

import { readdirSync, statSync } from "node:fs";
import path from "node:path";

export const DEFAULT_LIMIT_BYTES = 1_000_000_000;
export const DEFAULT_WARN_AT = 0.7;
export const DEFAULT_FAIL_AT = 0.95;

export interface SizeRow {
  name: string;
  bytes: number;
}

export interface SizeReport {
  rows: SizeRow[];
  total: number;
  level: "ok" | "warn" | "fail";
  warnBytes: number;
  failBytes: number;
  limitBytes: number;
}

export function dirSize(dir: string): number {
  let total = 0;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) total += dirSize(p);
    else if (e.isFile()) total += statSync(p).size;
  }
  return total;
}

/**
 * Sizes the built site: one row per version under data/, one for icons/, one for
 * everything else. Warns above `warnAt` of the limit and fails above `failAt`;
 * exactly at a threshold is still below it.
 */
export function measure(
  siteDir: string,
  opts: { limitBytes?: number; warnAt?: number; failAt?: number } = {},
): SizeReport {
  const limitBytes = opts.limitBytes ?? DEFAULT_LIMIT_BYTES;
  const warnAt = opts.warnAt ?? DEFAULT_WARN_AT;
  const failAt = opts.failAt ?? DEFAULT_FAIL_AT;
  if (!(warnAt > 0 && warnAt <= failAt && failAt <= 1)) {
    throw new Error(`thresholds must satisfy 0 < warn (${warnAt}) <= fail (${failAt}) <= 1`);
  }

  const rows: SizeRow[] = [];
  const dataDir = path.join(siteDir, "data");
  let counted = 0;
  const entries = (dir: string) => {
    try {
      return readdirSync(dir, { withFileTypes: true });
    } catch {
      return [];
    }
  };
  for (const e of entries(dataDir).filter((e) => e.isDirectory()).sort((a, b) => (a.name < b.name ? -1 : 1))) {
    const bytes = dirSize(path.join(dataDir, e.name));
    rows.push({ name: `data/${e.name}`, bytes });
    counted += bytes;
  }
  if (entries(path.join(siteDir, "icons")).length > 0) {
    const bytes = dirSize(path.join(siteDir, "icons"));
    rows.push({ name: "icons", bytes });
    counted += bytes;
  }
  const total = dirSize(siteDir);
  rows.push({ name: "app and other files", bytes: total - counted });

  const warnBytes = Math.floor(limitBytes * warnAt);
  const failBytes = Math.floor(limitBytes * failAt);
  const level = total > failBytes ? "fail" : total > warnBytes ? "warn" : "ok";
  return { rows, total, level, warnBytes, failBytes, limitBytes };
}

export function formatBytes(n: number): string {
  if (n < 1000) return `${n} B`;
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)} kB`;
  return `${(n / 1_000_000).toFixed(1)} MB`;
}

export function summaryMarkdown(r: SizeReport): string {
  const pct = (n: number) => `${((100 * n) / r.limitBytes).toFixed(1)}%`;
  return [
    "### Site size",
    "",
    "| part | size | share of limit |",
    "|---|---:|---:|",
    ...r.rows.map((row) => `| ${row.name} | ${formatBytes(row.bytes)} | ${pct(row.bytes)} |`),
    `| **total** | **${formatBytes(r.total)}** | **${pct(r.total)}** |`,
    "",
    `Limit ${formatBytes(r.limitBytes)}; warning above ${formatBytes(r.warnBytes)}, failure above ${formatBytes(r.failBytes)}.`,
    "",
  ].join("\n");
}
