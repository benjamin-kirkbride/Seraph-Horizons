// Collects validation errors and prints them readably. A broken 100 MB export can
// produce hundreds of thousands of identical errors, so each kind is capped.

export interface Problem {
  kind: string;
  path: string;
  expected: string;
  found: string;
}

export class ErrorReport {
  readonly problems: Problem[] = [];
  private readonly counts = new Map<string, number>();

  constructor(readonly capPerKind = 20) {}

  add(kind: string, path: string, expected: string, found: string): void {
    const n = (this.counts.get(kind) ?? 0) + 1;
    this.counts.set(kind, n);
    if (n <= this.capPerKind) this.problems.push({ kind, path, expected, found });
  }

  get total(): number {
    let t = 0;
    for (const n of this.counts.values()) t += n;
    return t;
  }

  get ok(): boolean {
    return this.total === 0;
  }

  countOf(kind: string): number {
    return this.counts.get(kind) ?? 0;
  }

  format(): string[] {
    const lines: string[] = [];
    for (const [kind, n] of this.counts) {
      for (const p of this.problems) {
        if (p.kind === kind) lines.push(`${p.path || "/"}: expected ${p.expected}, found ${p.found} [${kind}]`);
      }
      if (n > this.capPerKind) lines.push(`  ... and ${n - this.capPerKind} more [${kind}] errors`);
    }
    lines.push(`${this.total} error${this.total === 1 ? "" : "s"}`);
    return lines;
  }
}

/** A short description of a value, without serialising anything big. */
export function describe(value: unknown): string {
  if (value === undefined) return "nothing";
  if (value === null) return "null";
  if (Array.isArray(value)) return `an array of ${value.length}`;
  if (typeof value === "object") return `an object with ${Object.keys(value).length} keys`;
  const s = JSON.stringify(value);
  return s.length > 80 ? `${s.slice(0, 77)}..."` : s;
}

/** Escapes one JSON Pointer segment (RFC 6901). */
export function ptr(segment: string | number): string {
  return String(segment).replaceAll("~", "~0").replaceAll("/", "~1");
}
