// Brings an export of an older schemaVersion up to the current one, one version at
// a time. Each step is a pure function from version N to N+1; the chain refuses
// anything it has no path for rather than guessing.

export type Doc = Record<string, unknown>;

export interface MigrationStep {
  from: number;
  to: number;
  /** One line for the log, e.g. "split recipe outputs into stacks". */
  description: string;
  /** Must not modify `doc`: it is frozen, and a step that writes to it throws. */
  up(doc: Readonly<Doc>): Doc;
}

export class MigrationError extends Error {}

export class MigrationChain {
  private readonly byFrom = new Map<number, MigrationStep>();

  constructor(readonly current: number, steps: readonly MigrationStep[]) {
    for (const s of steps) {
      if (s.to !== s.from + 1) throw new Error(`migration ${s.from} -> ${s.to}: a step must go up exactly one version`);
      if (s.to > current) throw new Error(`migration ${s.from} -> ${s.to}: beyond the current version ${current}`);
      if (this.byFrom.has(s.from)) throw new Error(`two migrations from version ${s.from}`);
      this.byFrom.set(s.from, s);
    }
  }

  /** The oldest version the chain can bring to `current` without a gap. */
  get oldest(): number {
    let v = this.current;
    while (this.byFrom.has(v - 1)) v--;
    return v;
  }

  /** Throws a MigrationError when `version` cannot be migrated. */
  check(version: unknown): number {
    if (typeof version !== "number" || !Number.isInteger(version)) {
      throw new MigrationError(`schemaVersion ${JSON.stringify(version)} is not a known export version`);
    }
    if (version > this.current) {
      throw new MigrationError(
        `schemaVersion ${version} is newer than this tool understands (current ${this.current}); update tools/site-data`,
      );
    }
    if (version < this.oldest) {
      throw new MigrationError(
        `schemaVersion ${version} is not a known export version (known: ${this.oldest} to ${this.current})`,
      );
    }
    return version;
  }

  /** Returns the migrated document and the steps applied, in order. */
  run(doc: Doc): { doc: Doc; applied: MigrationStep[] } {
    let v = this.check(doc.schemaVersion);
    const applied: MigrationStep[] = [];
    let cur = doc;
    while (v < this.current) {
      const step = this.byFrom.get(v)!;
      const next = step.up(deepFreeze(cur));
      if (next.schemaVersion !== step.to) {
        throw new MigrationError(
          `migration ${step.from} -> ${step.to} returned schemaVersion ${JSON.stringify(next.schemaVersion)}`,
        );
      }
      applied.push(step);
      cur = next;
      v = step.to;
    }
    return { doc: cur, applied };
  }
}

function deepFreeze<T>(value: T): T {
  if (value !== null && typeof value === "object" && !Object.isFrozen(value)) {
    Object.freeze(value);
    for (const v of Object.values(value)) deepFreeze(v);
  }
  return value;
}
