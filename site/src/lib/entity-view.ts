// Pure grouping for the entity pages, kept out of the components so it can be tested
// without a DOM.
import type { EntitySource } from "./format.ts";

export type SectionKind = "drops" | "harvest" | "behavior" | "sells" | "buys";

export interface Section {
  kind: SectionKind;
  /** The behavior's note, for kind "behavior". */
  note?: string;
  rows: EntitySource[];
}

const ORDER: readonly SectionKind[] = ["drops", "harvest", "behavior", "sells", "buys"];

function kindOf(s: EntitySource): SectionKind {
  if (s.type === "traderSells") return "sells";
  if (s.type === "traderBuys") return "buys";
  if (s.note === undefined) return "drops";
  return s.note === "Harvested" ? "harvest" : "behavior";
}

/**
 * What an entity gives, split the way a player meets it: dropped on death, harvested
 * from the carcass, other behaviors (one section per note), then trades. Rows keep their
 * order.
 */
export function entitySections(sources: readonly EntitySource[]): Section[] {
  const sections = new Map<string, Section>();
  for (const s of sources) {
    const kind = kindOf(s);
    const key = kind === "behavior" ? `behavior|${s.note}` : kind;
    let section = sections.get(key);
    if (!section) {
      section = { kind, rows: [] };
      if (kind === "behavior") section.note = s.note;
      sections.set(key, section);
    }
    section.rows.push(s);
  }
  return [...sections.values()].sort((a, b) => ORDER.indexOf(a.kind) - ORDER.indexOf(b.kind));
}
