// Code patterns as the game reads them: `*` matches any run of characters, a path that
// starts with `@` is a regular expression, and outputs use `{name}` for the value a named
// wildcard bound. allowedVariants and skipVariants filter on the value of the first `*`.

export function isPattern(code: string): boolean {
  return code.includes("*") || code.includes("{") || code.includes(":@");
}

export interface CompiledPattern {
  /** Literal text every match starts with, for narrowing a sorted list of codes. */
  prefix: string;
  regex: RegExp;
  /** Capture group names in order; `*` for an anonymous wildcard. */
  groups: string[];
}

const escapeRe = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

export function compilePattern(pattern: string): CompiledPattern | null {
  const colon = pattern.indexOf(":");
  const domain = pattern.slice(0, colon);
  const path = pattern.slice(colon + 1);
  if (path.startsWith("@")) {
    try {
      return { prefix: `${domain}:`, regex: new RegExp(`^${escapeRe(domain)}:(?:${path.slice(1)})$`), groups: [] };
    } catch {
      return null;
    }
  }
  const groups: string[] = [];
  // A wildcard in the domain (`*:plank-*`) accepts any mod's item. It binds nothing, so
  // it must not take the place of the first `*` that allowedVariants filters on.
  const anyDomain = domain.includes("*");
  let re = anyDomain ? `${domain.split("*").map(escapeRe).join("[^:]*")}:` : "";
  let prefix = "";
  let literal = !anyDomain;
  const parts = (anyDomain ? path : pattern).split(/(\*|\{[^}]*\})/);
  for (const part of parts) {
    if (part === "") continue;
    if (part === "*") {
      groups.push("*");
      re += "(.*)";
      literal = false;
    } else if (part.startsWith("{") && part.endsWith("}")) {
      groups.push(part.slice(1, -1));
      re += "(.*)";
      literal = false;
    } else {
      re += escapeRe(part);
      if (literal) prefix += part;
    }
  }
  return { prefix, regex: new RegExp(`^${re}$`), groups };
}

export interface VariantFilter {
  allowedVariants?: string[] | undefined;
  skipVariants?: string[] | undefined;
}

/** Whether a wildcard value passes allowedVariants and skipVariants. */
export function variantAllowed(value: string, filter: VariantFilter): boolean {
  if (filter.allowedVariants && filter.allowedVariants.length > 0 && !filter.allowedVariants.includes(value)) {
    return false;
  }
  return !(filter.skipVariants && filter.skipVariants.includes(value));
}

function lowerBound(sorted: readonly string[], key: string): number {
  let lo = 0;
  let hi = sorted.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if ((sorted[mid] as string) < key) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

/** Index of `code` in a sorted array, or -1. */
export function indexOfSorted(sorted: readonly string[], code: string): number {
  const i = lowerBound(sorted, code);
  return sorted[i] === code ? i : -1;
}

/**
 * Indices of the codes in `sorted` that match `pattern`. `constraints` limits named
 * groups (`{wood}` in an output) and the first anonymous `*` (an ingredient's own
 * allowedVariants and skipVariants).
 */
export function matchSorted(
  sorted: readonly string[],
  pattern: string,
  constraints: Record<string, VariantFilter> = {},
): number[] {
  if (!isPattern(pattern)) {
    const i = indexOfSorted(sorted, pattern);
    return i < 0 ? [] : [i];
  }
  const compiled = compilePattern(pattern);
  if (!compiled) return [];
  const out: number[] = [];
  for (let i = lowerBound(sorted, compiled.prefix); i < sorted.length; i++) {
    const code = sorted[i] as string;
    if (!code.startsWith(compiled.prefix)) break;
    const m = compiled.regex.exec(code);
    if (!m) continue;
    let ok = true;
    let firstStar = true;
    compiled.groups.forEach((g, gi) => {
      const value = m[gi + 1] ?? "";
      const filter = g === "*" ? (firstStar ? constraints["*"] : undefined) : constraints[g];
      if (g === "*") firstStar = false;
      if (filter && !variantAllowed(value, filter)) ok = false;
    });
    if (ok) out.push(i);
  }
  return out;
}
