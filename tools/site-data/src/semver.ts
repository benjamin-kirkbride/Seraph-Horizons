// Just enough semver (2.0.0, section 11) to order release tags. Build metadata is
// not accepted: `+` does not belong in a directory name.

const TAG = /^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?$/;

export interface Version {
  major: number;
  minor: number;
  patch: number;
  pre: string[];
}

/** Parses a release tag such as v1.2.3 or v1.0.0-rc.1; undefined for anything else. */
export function parseTag(tag: string): Version | undefined {
  const m = TAG.exec(tag);
  if (!m) return undefined;
  return { major: Number(m[1]), minor: Number(m[2]), patch: Number(m[3]), pre: m[4] ? m[4].split(".") : [] };
}

/** Negative when a is older than b, positive when newer, 0 when equal. */
export function compareVersions(a: Version, b: Version): number {
  const core = a.major - b.major || a.minor - b.minor || a.patch - b.patch;
  if (core !== 0) return Math.sign(core);
  // A pre-release sorts before its release.
  if (a.pre.length === 0 || b.pre.length === 0) return Math.sign(b.pre.length - a.pre.length);
  for (let i = 0; i < Math.min(a.pre.length, b.pre.length); i++) {
    const c = compareIdentifier(a.pre[i]!, b.pre[i]!);
    if (c !== 0) return c;
  }
  return Math.sign(a.pre.length - b.pre.length);
}

function compareIdentifier(a: string, b: string): number {
  const an = /^\d+$/.test(a);
  const bn = /^\d+$/.test(b);
  if (an && bn) return Math.sign(Number(a) - Number(b));
  if (an !== bn) return an ? -1 : 1;
  return a < b ? -1 : a > b ? 1 : 0;
}
