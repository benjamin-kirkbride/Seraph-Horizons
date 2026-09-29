// Client-side item search. A linear scan with a cheap substring pre-check is fast enough
// for tens of thousands of items and needs no index beyond search.json itself.
import { FLAG_HANDBOOK, type SearchFile } from "./format.ts";

export function normalize(text: string): string {
  return text
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase();
}

export function tokenize(text: string): string[] {
  return normalize(text)
    .split(/[^\p{L}\p{N}]+/u)
    .filter((t) => t.length > 0);
}

interface Prepared {
  name: string;
  nameWords: string[];
  code: string;
  codeWords: string[];
  /** name and code together, for the substring pre-check. */
  hay: string;
}

export class ItemSearch {
  private readonly prepared: Prepared[];
  private readonly flags: readonly number[];

  constructor(file: SearchFile) {
    this.flags = file.flags;
    this.prepared = file.codes.map((code, i) => {
      const name = normalize(file.names[i] ?? code);
      const lc = normalize(code);
      return { name, nameWords: tokenize(name), code: lc, codeWords: tokenize(lc), hay: `${name}\n${lc}` };
    });
  }

  /** Item indices matching `query`, best first. */
  search(query: string, limit = 100): number[] {
    const tokens = tokenize(query);
    if (tokens.length === 0) return [];
    const whole = normalize(query).trim();
    const scored: { i: number; score: number }[] = [];
    this.prepared.forEach((p, i) => {
      for (const t of tokens) if (!p.hay.includes(t)) return;
      const score = scoreItem(p, tokens, whole, ((this.flags[i] ?? 0) & FLAG_HANDBOOK) !== 0);
      if (score !== null) scored.push({ i, score });
    });
    scored.sort(
      (a, b) =>
        b.score - a.score ||
        this.prepared[a.i]!.name.length - this.prepared[b.i]!.name.length ||
        (this.prepared[a.i]!.code < this.prepared[b.i]!.code ? -1 : 1),
    );
    return scored.slice(0, limit).map((s) => s.i);
  }
}

function tokenScore(t: string, words: readonly string[], whole: string, exact: number, prefix: number, inner: number): number {
  let best = 0;
  for (const w of words) {
    if (w === t) return exact;
    if (w.startsWith(t)) best = Math.max(best, prefix);
  }
  if (best === 0 && whole.includes(t)) best = inner;
  return best;
}

function scoreItem(p: Prepared, tokens: readonly string[], whole: string, handbook: boolean): number | null {
  let score = 0;
  const usedWords = new Set<number>();
  for (const t of tokens) {
    const byName = tokenScore(t, p.nameWords, p.name, 10, 7, 3);
    const byCode = tokenScore(t, p.codeWords, p.code, 6, 5, 2);
    if (byName === 0 && byCode === 0) return null;
    score += Math.max(byName, byCode);
    const w = p.nameWords.findIndex((word) => word.startsWith(t));
    if (w >= 0) usedWords.add(w);
  }
  if (whole === p.name) score += 30;
  else if (p.name.startsWith(whole)) score += 10;
  if (whole === p.code || p.code.endsWith(`:${whole}`)) score += 40;
  if (handbook) score += 2;
  // Prefer names that are mostly made of what was typed: "Copper ingot" over
  // "Copper ingot mold" for "ingot cop".
  score -= Math.max(0, p.nameWords.length - usedWords.size);
  return score;
}
