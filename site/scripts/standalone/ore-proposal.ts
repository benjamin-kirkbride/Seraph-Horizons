// The ore review page's "Proposal: smelt by grade" tab (scripts/standalone/ore-review.json): a
// proposed rebalance of what a furnace takes, broken down so the owner can judge it. Today bulk sits
// on one item (raw ore stacks to 4) and crushed ore smelts at a flat 50 % at any grade, which opens
// escape hatches past the concentrator. The proposal has the furnace read the ore's grade, not its
// form: unconcentrated ore (raw, chunk, crushed, ground) smelts at a share set by grade, concentrate
// at 100 %, and every form still carrying its rock stacks poorly. Every figure here is computed from
// PROPOSED (next to ore-line.ts's CFG, the figures on main) so it can be checked, and each table has
// its formula under it. Plain HTML and CSS on the processing line's styles, no script.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

import { CFG, concentration, DEVICES, STYLE, TIER_LABEL, TIERS, tierConcentrate, type Feed, type Tier } from "./ore-line.ts";

export type Grade = "poor" | "medium" | "rich" | "bountiful";
export const GRADES: Grade[] = ["poor", "medium", "rich", "bountiful"];

/** Vanilla's ore-graded.json metalUnitsByType default, from poor to bountiful (and its exceptions). */
export const UNITS: Record<Grade, number> = { poor: 15, medium: 20, rich: 25, bountiful: 35 };
export const UNIT_EXCEPTIONS: Record<string, Record<Grade, number>> = {
  hematite: { poor: 20, medium: 25, rich: 30, bountiful: 40 },
  "silver quartz": { poor: 10, medium: 15, rich: 20, bountiful: 25 },
  cassiterite: { poor: 5, medium: 10, rich: 15, bountiful: 20 },
  "gold quartz": { poor: 5, medium: 10, rich: 15, bountiful: 20 },
};

/** The item each grade is mined as (OreProducts.FormOfGrade). */
export const MINED_FORM: Record<Grade, "raw" | "chunk"> = { poor: "raw", medium: "raw", rich: "chunk", bountiful: "chunk" };

/**
 * Stack sizes on main. Raw ore and chunks: OreProducts.RawStack and ChunkStack, set on vanilla's
 * game:ore-{grade}-{ore}-{rock} in code (OreProcessingItems.Apply; vanilla's ore-graded.json says 64).
 * Crushed: the pack's own game:crushed-{ore}-{grain} (assets/game/itemtypes/resource/crushedore.json).
 * Ground ore, concentrate, roasted concentrate, amalgam, sponge: their item files under
 * assets/seraphhorizons/itemtypes/oreprocessing/. Vanilla's game:crushed-{material} and Expanded
 * Matter's em:crushed-ore (expanded_matter-3.8.1.zip, assets/em/itemtypes/resource/crushed-ore.json)
 * both stack to 64, but the pack's line does not use them.
 */
export const STACK = { raw: 4, chunk: 16, crushed: 16, ground: 16, concentrate: 128, roastedconcentrate: 128, amalgam: 128, sponge: 128 } as const;
export type Form = keyof typeof STACK;
export const FORMS: Form[] = ["raw", "chunk", "crushed", "ground", "concentrate", "roastedconcentrate", "amalgam", "sponge"];
export const FORM_LABEL: Record<Form, string> = {
  raw: "Raw ore",
  chunk: "Chunk",
  crushed: "Crushed ore",
  ground: "Ground ore",
  concentrate: "Concentrate",
  roastedconcentrate: "Roasted concentrate",
  amalgam: "Amalgam",
  sponge: "Sponge",
};
/** Forms that still carry their rock: they smelt by grade under the proposal and stack poorly. */
export const ROCK_FORMS: Form[] = ["raw", "chunk", "crushed", "ground"];

/** The proposal's figures. Starting values; the sensitivity table nudges them. */
export interface Proposal {
  /** What share of its units unconcentrated ore (raw, chunk, crushed, ground) gives in a furnace, by grade. */
  byGrade: Record<Grade, number>;
  /** Concentrate, roasted concentrate and sponge keep smelting whole. */
  concentrate: number;
  /** An extra factor on unconcentrated sulfide (raw, chunk, crushed): 1 is none. Open question. */
  sulfidePenalty: number;
}
export const PROPOSED: Proposal = {
  byGrade: { poor: 0.1, medium: 0.3, rich: 0.6, bountiful: 0.75 },
  concentrate: 1,
  sulfidePenalty: 1,
};

/** Units of metal in a crushed, ground, concentrate, roasted concentrate, amalgam or sponge item. */
export const UNITS_PER_PRODUCT = CFG.unitsPerCrushed;
export const UNITS_PER_INGOT = 100;

// ---- what a furnace gives, today and proposed

/** Today's smelt share of a form (OreRecovery.SmeltShare): by form alone; grade only decides raw or chunk. */
export function currentSmelt(form: Form, sulfide = false): number {
  if (form === "amalgam") return 0;
  if (sulfide && form === "concentrate") return 0;
  return CFG.smelt[form === "sponge" ? "sponge" : form];
}

/** The proposal's smelt share: rock forms by grade (× the sulfide penalty), concentrate whole, sulfide concentrate still 0 until roasted. */
export function proposedSmelt(form: Form, grade: Grade, p: Proposal = PROPOSED, sulfide = false): number {
  if (form === "amalgam") return 0;
  if (ROCK_FORMS.includes(form)) return p.byGrade[grade] * (sulfide ? p.sulfidePenalty : 1);
  if (sulfide && form === "concentrate") return 0;
  return p.concentrate;
}

// ---- the choices a grade faces at a tier

export interface Choice {
  key: "mined" | "crushed" | "conc" | "concUnclassified";
  label: string;
  /** The figure, of the metal in the rock; for concentrate, the best device. */
  value: number;
  device?: string;
  /** For poor ore at a tier that grinds: the figure without the grinder. */
  alt?: { label: string; value: number };
}

export interface Options {
  sulfide?: boolean;
  free?: boolean;
  amalgamated?: boolean;
}

/** The best concentrate figure a tier gives a grade, with the roaster for sulfides; poor is ground where the tier grinds. */
export function bestConcentrate(tier: Tier, grade: Grade, o: Options = {}, classified = true): { device: string; value: number; unground?: number } {
  const feed: Feed = { poor: grade === "poor", classified, free: o.free, amalgamated: o.amalgamated };
  const roast = o.sulfide ? CFG.roasters[DEVICES[tier].roaster.key] : 1;
  const best = tierConcentrate(tier, feed)[0]!;
  const r = { device: best.device, value: best.value * roast } as { device: string; value: number; unground?: number };
  if (grade === "poor" && DEVICES[tier].grinds) r.unground = concentration(CFG.concentrators[DEVICES[tier].conc[0]!.key], { ...feed, ground: false }) * roast;
  return r;
}

/** Every choice at a tier for a grade, under the current figures (p null) or a proposal. */
export function choices(tier: Tier, grade: Grade, p: Proposal | null, o: Options = {}): Choice[] {
  const mined = MINED_FORM[grade];
  const smelt = (form: Form) => (p ? proposedSmelt(form, grade, p, o.sulfide) : currentSmelt(form, o.sulfide));
  const c = bestConcentrate(tier, grade, o, true);
  const u = bestConcentrate(tier, grade, o, false);
  const alt = (x: { unground?: number }) => (x.unground == null ? undefined : { label: "not ground", value: x.unground });
  return [
    { key: "mined", label: `Smelt as mined (${mined})`, value: smelt(mined) },
    { key: "crushed", label: "Crush and smelt", value: smelt("crushed") },
    { key: "conc", label: "Concentrate, classified", value: c.value, device: c.device, alt: alt(c) },
    { key: "concUnclassified", label: "Concentrate, unclassified", value: u.value, device: u.device, alt: alt(u) },
  ];
}

export function winner(cs: Choice[]): Choice {
  return cs.reduce((a, b) => (b.value > a.value + 1e-9 ? b : a));
}

/** The decision a grade faces at a tier: carry home and smelt, or concentrate at the mine. Margin in points of recovery. */
export function decision(tier: Tier, grade: Grade, p: Proposal = PROPOSED, o: Options = {}): { home: number; mine: Choice; margin: number; verdict: "mine" | "home" } {
  const cs = choices(tier, grade, p, o);
  const home = Math.max(cs[0]!.value, cs[1]!.value);
  const mine = cs[2]!;
  const margin = (mine.value - home) * 100;
  return { home, mine, margin, verdict: margin > 1e-9 ? "mine" : "home" };
}

// ---- logistics

/** Units of metal one item of a form holds, mined at a grade (concentrate and the rest hold 5 whatever the grade). */
export const unitsPerItem = (form: Form, grade: Grade) => (form === "raw" || form === "chunk" ? UNITS[grade] : UNITS_PER_PRODUCT);

/** Metal units a full slot of a form gives smelted: stack × units an item × the smelt share. */
export function unitsPerSlot(form: Form, grade: Grade, p: Proposal | null, sulfide = false): number {
  const share = p ? proposedSmelt(form, grade, p, sulfide) : currentSmelt(form, sulfide);
  return STACK[form] * unitsPerItem(form, grade) * share;
}
export const ingotsPerSlot = (form: Form, grade: Grade, p: Proposal | null, sulfide = false) => unitsPerSlot(form, grade, p, sulfide) / UNITS_PER_INGOT;

export interface Route {
  label: string;
  /** Units of metal in the rock that must be mined. */
  rock: number;
  /** Items carried home. */
  items: number;
  /** Inventory slots those items fill. */
  slots: number;
  form: Form;
  /** Of the metal in the rock, what reaches the furnace. */
  recovery: number;
}

/** Bringing `target` units of metal home from a grade: smelt as mined, carry crushed, or concentrate at each tier. */
export function routes(grade: Grade, p: Proposal | null, target = 1000, o: Options = {}): Route[] {
  const mined = MINED_FORM[grade];
  const smelt = (form: Form) => (p ? proposedSmelt(form, grade, p, o.sulfide) : currentSmelt(form, o.sulfide));
  const carry = (label: string, form: Form, recovery: number): Route => {
    if (recovery <= 0) return { label, rock: Infinity, items: Infinity, slots: Infinity, form, recovery: 0 };
    const rock = target / recovery;
    // What is carried: the rock's units in that form's items (concentrate: the recovered units, 5 each).
    const carried = form === "concentrate" || form === "roastedconcentrate" ? target : rock;
    const items = carried / unitsPerItem(form, grade);
    return { label, rock, items, slots: items / STACK[form], form, recovery };
  };
  return [
    carry(`Smelt as mined (${mined})`, mined, smelt(mined)),
    carry("Crush at the mine, carry crushed", "crushed", smelt("crushed")),
    ...TIERS.map((t) => {
      const c = bestConcentrate(t, grade, o, true);
      return carry(`Concentrate at the mine, ${TIER_LABEL[t][0].toLowerCase()} (${c.device})`, o.sulfide ? "roastedconcentrate" : "concentrate", c.value);
    }),
  ];
}

// ---- sensitivity

export interface Variant {
  label: string;
  grade: Grade;
  proposal: Proposal;
  options?: Options;
}
export const VARIANTS: Variant[] = [
  { label: "medium 20 %", grade: "medium", proposal: { ...PROPOSED, byGrade: { ...PROPOSED.byGrade, medium: 0.2 } } },
  { label: "medium 30 % (proposed)", grade: "medium", proposal: PROPOSED },
  { label: "medium 40 %", grade: "medium", proposal: { ...PROPOSED, byGrade: { ...PROPOSED.byGrade, medium: 0.4 } } },
  { label: "rich 50 %", grade: "rich", proposal: { ...PROPOSED, byGrade: { ...PROPOSED.byGrade, rich: 0.5 } } },
  { label: "rich 60 % (proposed)", grade: "rich", proposal: PROPOSED },
  { label: "rich 70 %", grade: "rich", proposal: { ...PROPOSED, byGrade: { ...PROPOSED.byGrade, rich: 0.7 } } },
  { label: "bountiful 75 % (proposed)", grade: "bountiful", proposal: PROPOSED },
  { label: "bountiful 65 %", grade: "bountiful", proposal: { ...PROPOSED, byGrade: { ...PROPOSED.byGrade, bountiful: 0.65 } } },
  { label: "rich sulfide, no penalty (proposed)", grade: "rich", proposal: PROPOSED, options: { sulfide: true } },
  { label: "rich sulfide × 0.8", grade: "rich", proposal: { ...PROPOSED, sulfidePenalty: 0.8 }, options: { sulfide: true } },
  { label: "bountiful sulfide, no penalty (proposed)", grade: "bountiful", proposal: PROPOSED, options: { sulfide: true } },
  { label: "bountiful sulfide × 0.8", grade: "bountiful", proposal: { ...PROPOSED, sulfidePenalty: 0.8 }, options: { sulfide: true } },
];

// ---- open questions and side effects

const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";

export const OPEN_QUESTIONS: string[] = [
  "Crushed and ground ore carry no grade. The pack's crushed ore is game:crushed-{ore}-{grain} (OreProducts.CrushedCode), fine from poor and coarse from medium, rich, bountiful and nuggets alike, and ground ore is seraphhorizons:groundore-{ore}. A furnace smelts by the item type's CombustibleProps (OreProcessingItems.Smelting sets one SmeltedRatio per item), so \"crushed smelts by grade\" needs either a grade variant on crushed and ground ore (four items an ore instead of two, and stacks of different grades no longer merge) or a per-stack attribute read by a Harmony patch on the furnaces. Which? And what grade does a nugget's crushed ore take (nuggets crush to one coarse crushed ore)?",
  "Do chunks keep their own figure? Today a chunk smelts at 50 % whatever its grade (rich and bountiful). Under the proposal it reads the grade: rich 60 %, bountiful 75 %, so both chunks smelt better than today, and a bountiful chunk out-smelts the hand rocker (55 %) and nearly the water jig (80 %). If chunks are meant to stay at 50 %, say so; the tables here read them by grade.",
  "Should unconcentrated sulfide take an extra penalty? With none, a rich sulfide chunk by hand (60 %) beats the whole hand sulfide line (rocker × firepit 46.8 %), and a bountiful chunk (75 %) beats the water line (jig × stall 73.6 %). The sensitivity table tries × 0.8. Unroasted sulfide concentrate stays at 0 either way.",
  "Should raw ore (poor and medium) smelt at all? Today it does not (0 %); the proposal gives it 10 % and 30 %, which also makes it fit a crucible (OreProcessingItems gives a smelting form CrucibleSize). That is how it fits the bloomery and crucibulum too.",
  "Item values: the recipe export's item-values check weighs items by what they smelt to. Raw ore gaining a smelt, chunks changing from 50 % to 60 or 75 %, and crushed ore's value depending on grade all move it; concentrate's does not.",
  "Ground ore: today it does not smelt (it goes on to the concentrator). The proposal lists it among the rock forms, so poor ground ore would smelt at 10 %. Harmless, since ground ore only comes from poor ore, but it needs the same grade carry as crushed.",
  "Free gold's unparted 0.85 and the furnace patch (the processing line's open question) is unchanged by this proposal; smelting gold quartz as mined at 60 or 75 % would still skip the silver.",
];

export const CODE_CHANGES: string[] = [
  "ore-processing.json: \"smelt\" becomes a share by grade for the rock forms, for example \"smelt\": { \"byGrade\": { \"poor\": 0.1, \"medium\": 0.3, \"rich\": 0.6, \"bountiful\": 0.75 }, \"concentrate\": 1, \"roastedconcentrate\": 1, \"sponge\": 1 } (and \"sulfidePenalty\" if wanted); the per-form raw, chunk, crushed, ground keys go.",
  "OreRecovery.SmeltShare(ore, form) gains the grade: SmeltShare(ore, form, grade), returning byGrade[grade] (× the penalty for a sulfide) for Raw, Chunk, Crushed and Ground, 0 for unroasted sulfide concentrate, and the form's share otherwise. OreProcessingItems.Apply has the grade for ore items (parts[1]) but not for crushed ore (parts are crushed, ore, grain): the grade must come from the item (a new variant) or the stack.",
  "OreProducts: stack sizes are already by form (RawStack 4, ChunkStack 16, CrushedStack 16, ConcentrateStack 128); \"bulk follows the rock\" is already the case for raw, crushed and ground. Only the chunk's 16 is in question.",
  "The processing line tab (ore-line.ts: CFG.smelt, COMMON_STOPS and the worked examples) and the README's \"Ore processing\" section follow.",
];

export const ISSUE_765 =
  "#765 (open) reworks the recovery core to three tiers and per-device lines and says: \"Keep the factors and the config file; move per-tier figures to per-device ones where they differ\". It does not mention the smelt shares. If the proposal is taken up, #765's text should add the grade-read furnace (the smelt map by grade, SmeltShare taking the grade, and the grade carried on crushed and ground ore), or it gets its own issue under #684 and #765 links it, so the two reworks of OreRecovery land together.";

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
const linked = (s: string) => esc(s).replace(/#(\d{3,4})\b/g, (_m, n: string) => issueLink(Number(n)));
const pct = (x: number) => `${Math.round(x * 1000) / 10} %`;
const pts = (x: number) => `${Math.round(Math.abs(x) * 10) / 10}`;
const num = (x: number, d = 1) => (Number.isFinite(x) ? `${Math.round(x * 10 ** d) / 10 ** d}` : "∞");
const cap = (s: string) => s[0]!.toUpperCase() + s.slice(1);
const tierHead = TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</th>`).join("");

function fig(value: number, best: boolean, device?: string, alt?: Choice["alt"]): string {
  return `<span class="ol-f${best ? " ol-f-best" : ""}"><b>${pct(value)}</b>${device ? ` ${esc(device)}` : ""}</span>${alt ? `<span class="ol-cnote">${esc(alt.label)} ${pct(alt.value)}</span>` : ""}`;
}

/** 1. Current against proposed, one table a grade. */
function renderCompare(grade: Grade): string {
  const keys = choices("hand", grade, null).map((c) => c.label);
  const head = `<tr><th>Tier</th><th>Figures</th>${keys.map((k) => `<th>${esc(k)}</th>`).join("")}<th>Winner</th></tr>`;
  const rows = TIERS.flatMap((t) => {
    const now = choices(t, grade, null);
    const prop = choices(t, grade, PROPOSED);
    const wNow = winner(now);
    const wProp = winner(prop);
    const flips = wNow.key !== wProp.key;
    const cells = (cs: Choice[], w: Choice) =>
      cs.map((c) => `<td class="ol-c${c === w ? " op-win" : ""}${c.value === 0 ? " ol-c-zero" : ""}">${fig(c.value, c === w, c.device, c.alt)}</td>`).join("");
    const short = (c: Choice) => esc(c.label.replace(/ \(.*\)/, ""));
    const tierCell = `<th scope="row" rowspan="2" class="op-tier"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</th>`;
    return [
      `<tr class="op-now">${tierCell}<td class="op-which">Now</td>${cells(now, wNow)}<td class="op-verdict">${short(wNow)}</td></tr>`,
      `<tr class="op-prop"><td class="op-which">Proposed</td>${cells(prop, wProp)}<td class="op-verdict">${short(wProp)}${flips ? ' <span class="ol-tag op-tag-flip">flips</span>' : ' <span class="ol-tag ol-tag-opt">same</span>'}</td></tr>`,
    ];
  }).join("\n");
  return `<div class="op-grade" id="op-cmp-${grade}"><h4>${cap(grade)}: ${UNITS[grade]} units an ore, mined as ${MINED_FORM[grade] === "raw" ? "raw ore (stack 4)" : "a chunk (stack 16)"}; now ${pct(currentSmelt(MINED_FORM[grade]))} as mined, proposed ${pct(PROPOSED.byGrade[grade])}</h4>
<div class="ol-tablewrap"><table class="ol-table op-table"><thead>${head}</thead><tbody>${rows}</tbody></table></div></div>`;
}

/** 2. The decision each grade faces. */
function renderDecisions(): string {
  const cards = GRADES.map((g) => {
    const lines = TIERS.map((t) => {
      const d = decision(t, g);
      const say =
        d.verdict === "mine"
          ? `<b>concentrate at the mine</b>: ${esc(d.mine.device!)} ${pct(d.mine.value)} beats smelting as is (${pct(d.home)}) by <b>${pts(d.margin)} points</b>`
          : `<b>carry home and smelt as is</b>: ${pct(d.home)} beats the ${esc(d.mine.device!)} (${pct(d.mine.value)}) by <b>${pts(d.margin)} points</b>`;
      return `<li class="op-d-${d.verdict}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}: ${say}.</li>`;
    });
    const mineTiers = TIERS.filter((t) => decision(t, g).verdict === "mine");
    const summary =
      mineTiers.length === 3 ? "Process at the mine at every tier." : mineTiers.length === 0 ? "Carry it home at every tier." : `Process at the mine at ${mineTiers.map((t) => TIER_LABEL[t][0].toLowerCase()).join(" and ")}; by ${TIERS.filter((t) => !mineTiers.includes(t)).map((t) => TIER_LABEL[t][0].toLowerCase()).join(" and ")}, carry it home.`;
    return `<div class="op-card"><b>${cap(g)}</b> <span class="ol-when">as mined ${pct(PROPOSED.byGrade[g])}</span><p class="op-summary">${summary}</p><ul>${lines.join("")}</ul></div>`;
  });
  return `<div class="op-cards">${cards.join("")}</div>
<p class="ol-formula"><b>Formula:</b> margin = best concentrate (classified, poor ground where the tier grinds) − the grade's share as mined, in points; under the proposal crushing changes nothing in the furnace, so "smelt as is" and "crush and smelt" are the same figure. Oxide ores; sulfides and free gold are below.</p>`;
}

/** 3a. Ingots a slot is worth, per form and grade, now and proposed. */
function renderSlots(): string {
  const head = `<tr><th>Form</th><th>Stack</th><th>Units an item</th>${GRADES.map((g) => `<th>${cap(g)}<span class="ol-when">now → proposed</span></th>`).join("")}</tr>`;
  const rows = FORMS.map((f) => {
    const cells = GRADES.map((g) => {
      if ((f === "raw" && MINED_FORM[g] !== "raw") || (f === "chunk" && MINED_FORM[g] !== "chunk") || (f === "ground" && g !== "poor")) return `<td class="ol-c ol-c-none">—</td>`;
      if (f === "amalgam") return `<td class="ol-c ol-c-none">retort it: ${num(ingotsPerSlot("sponge", g, PROPOSED))} as sponge</td>`;
      const now = ingotsPerSlot(f, g, null);
      const prop = ingotsPerSlot(f, g, PROPOSED);
      const same = Math.abs(now - prop) < 1e-9;
      return `<td class="ol-c${prop === 0 ? " ol-c-zero" : ""}"><span class="ol-f"><b class="${same ? "" : "op-old"}">${num(now, 2)}</b>${same ? "" : ` → <b class="op-new">${num(prop, 2)}</b>`} ingots</span><span class="ol-cnote">${num(unitsPerSlot(f, g, PROPOSED))} units</span></td>`;
    });
    const units = f === "raw" || f === "chunk" ? GRADES.filter((g) => MINED_FORM[g] === f).map((g) => UNITS[g]).join(" / ") : `${UNITS_PER_PRODUCT}`;
    return `<tr><th scope="row"><b>${esc(FORM_LABEL[f])}</b>${ROCK_FORMS.includes(f) ? '<span class="ol-when">carries its rock</span>' : ""}</th><td>${STACK[f]}</td><td>${units}</td>${cells.join("")}</tr>`;
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-slots"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> ingots a slot = stack size × metal units an item × that form's smelt share ÷ ${UNITS_PER_INGOT} units an ingot. A concentrate, roasted concentrate or sponge item holds ${UNITS_PER_PRODUCT} units whatever the ore's grade and whichever tier made it (UnitCarry rounds nothing away), so its slot is worth the same ${num(ingotsPerSlot("concentrate", "poor", null))} ingots at every grade and tier; the recovery up to that stage decides how much ore it took to fill the slot, which the next table shows. Smelt shares: now ore-processing.json's smelt (raw 0, chunk and crushed 0.5, ground 0, concentrate 1); proposed by grade ${GRADES.map((g) => `${g} ${pct(PROPOSED.byGrade[g])}`).join(", ")} for the rock forms, concentrate 1.</p>`;
}

/** 3b. Bringing 1000 units home by each route. */
function renderRoutes(): string {
  const target = 1000;
  const head = `<tr><th>Grade</th><th>Route</th><th>Ore mined<span class="ol-when">units in the rock</span></th><th>Carried as</th><th>Items</th><th>Slots<span class="ol-when">now → proposed</span></th></tr>`;
  const rows = GRADES.flatMap((g) => {
    const now = routes(g, null, target);
    const prop = routes(g, PROPOSED, target);
    return prop.map((r, i) => {
      const n = now[i]!;
      const same = Math.abs(n.slots - r.slots) < 1e-9 || (!Number.isFinite(n.slots) && !Number.isFinite(r.slots));
      return `<tr>${i === 0 ? `<th scope="row" rowspan="${prop.length}" class="op-tier"><b>${cap(g)}</b><span class="ol-when">${UNITS[g]} units an ore</span></th>` : ""}<td>${esc(r.label)}</td><td>${num(r.rock, 0)}<span class="ol-cnote">at ${pct(r.recovery)}</span></td><td>${esc(FORM_LABEL[r.form])} <span class="ol-cnote">stack ${STACK[r.form]}</span></td><td>${num(r.items, 0)}</td><td class="ol-c op-slotcell"><span class="ol-f"><b class="${same ? "" : "op-old"}">${num(n.slots)}</b>${same ? "" : ` → <b class="op-new">${num(r.slots)}</b>`}</span></td></tr>`;
    });
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-routes"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> ore mined = ${target} ÷ the route's recovery; items = the units carried ÷ units an item (rock forms carry the whole rock, concentrate only the ${target} recovered units at ${UNITS_PER_PRODUCT} each); slots = items ÷ stack. ∞: the route gives no metal (raw ore does not smelt today). Concentrate at the mine always comes home as ${num(target / UNITS_PER_PRODUCT / STACK.concentrate)} slots; the tier only changes how much rock is dug.</p>`;
}

/** 4. The three problems, before and after. */
function renderProblems(): string {
  const handPoor = bestConcentrate("hand", "poor");
  const handMed = bestConcentrate("hand", "medium");
  const handMedU = bestConcentrate("hand", "medium", {}, false);
  const handSulf = bestConcentrate("hand", "medium", { sulfide: true });
  // Worked examples in units: hematite 20 a poor ore, the default 20 a medium ore, galena 25 medium and 30 rich.
  const u = (total: number, share: number) => `${num(total * share)}`;
  const hem = 8 * UNIT_EXCEPTIONS.hematite!.poor;
  const med = 4 * UNITS.medium;
  const gal = 4 * UNITS.medium;
  const galRich = 4 * UNITS.rich;
  const line = (tier: Tier, grade: Grade, o: Options = {}) => bestConcentrate(tier, grade, o).value;
  const problems = [
    {
      title: "Poor ore is better crushed and smelted than concentrated by hand",
      before: `Crushed smelts at ${pct(currentSmelt("crushed"))}; the rocker, with no hand grinder, catches ${pct(handPoor.value)} (${CFG.concentrators.rocker} × ${CFG.fineUnground}). Crushing wins by ${pts((currentSmelt("crushed") - handPoor.value) * 100)} points, so nobody concentrates poor ore by hand.`,
      after: `Poor smelts at ${pct(PROPOSED.byGrade.poor)} in any rock form. The rocker's ${pct(handPoor.value)} now wins by ${pts((handPoor.value - PROPOSED.byGrade.poor) * 100)} points; at water the ground jig gives ${pct(bestConcentrate("water", "poor").value)}, ${pts((bestConcentrate("water", "poor").value - PROPOSED.byGrade.poor) * 100)} points over carrying it home.`,
      example: `8 poor hematite (${UNIT_EXCEPTIONS.hematite!.poor} units each, ${hem} units of iron). Now: raw ore does not smelt; spalled, ${hem} × ${pct(currentSmelt("crushed"))} = ${u(hem, currentSmelt("crushed"))} units smelted crushed against ${hem} × ${pct(handPoor.value)} = ${u(hem, handPoor.value)} concentrated by hand. Proposed: ${hem} × ${pct(PROPOSED.byGrade.poor)} = ${u(hem, PROPOSED.byGrade.poor)} units smelted in any rock form, ${u(hem, handPoor.value)} from the rocker, ${u(hem, line("water", "poor"))} from the water jig, ${u(hem, line("mill", "poor"))} from the mill table and vanner.`,
    },
    {
      title: "Medium ore in the rocker barely beats smelting it crushed",
      before: `Rocker ${pct(handMed.value)} against crushed ${pct(currentSmelt("crushed"))}: ${pts((handMed.value - currentSmelt("crushed")) * 100)} points for the work of classifying and rocking, and unclassified (${pct(handMedU.value)}) it loses.`,
      after: `Medium smelts at ${pct(PROPOSED.byGrade.medium)}. The rocker wins by ${pts((handMed.value - PROPOSED.byGrade.medium) * 100)} points classified and ${pts((handMedU.value - PROPOSED.byGrade.medium) * 100)} unclassified; the choice is clear at every tier.`,
      example: `4 medium ore of the default ${UNITS.medium} units (${med} units). Now: ${u(med, currentSmelt("crushed"))} smelted crushed against ${u(med, handMed.value)} from the rocker (${u(med, handMedU.value)} unclassified). Proposed: ${u(med, PROPOSED.byGrade.medium)} smelted as is, still ${u(med, handMed.value)} from the rocker, ${u(med, line("water", "medium"))} from the jig, ${u(med, line("mill", "medium"))} from the mill.`,
    },
    {
      title: "By hand, concentrated and roasted sulfide loses to smelting it crushed",
      before: `Rocker ${pct(CFG.concentrators.rocker)} × firepit ${pct(CFG.roasters.firepit)} = ${pct(handSulf.value)}, against crushed sulfide at ${pct(currentSmelt("crushed"))} with no roasting. The hand sulfide line only pays for the silver the cupel wins.`,
      after: `Medium sulfide as mined smelts at ${pct(PROPOSED.byGrade.medium)}: the hand line's ${pct(handSulf.value)} wins by ${pts((handSulf.value - PROPOSED.byGrade.medium) * 100)} points. But a rich sulfide chunk smelts at ${pct(PROPOSED.byGrade.rich)} and a bountiful one at ${pct(PROPOSED.byGrade.bountiful)}, both over the hand line, and the bountiful chunk is over the water line too (jig × stall ${pct(bestConcentrate("water", "bountiful", { sulfide: true }).value)}). Whether that is the intended "rich ore is worth hauling" or wants a sulfide penalty is open.`,
      example: `4 medium galena (${UNITS.medium} units each, ${gal} units of lead). Now: ${u(gal, currentSmelt("crushed"))} smelted crushed against ${u(gal, handSulf.value)} roasted from the rocker. Proposed: ${u(gal, PROPOSED.byGrade.medium)} as mined, ${u(gal, handSulf.value)} by hand, ${u(gal, line("water", "medium", { sulfide: true }))} at water, ${u(gal, line("mill", "medium", { sulfide: true }))} at the mill. 4 rich galena chunks (${UNITS.rich} units, ${galRich}): proposed ${u(galRich, PROPOSED.byGrade.rich)} as mined against ${u(galRich, line("hand", "rich", { sulfide: true }))} by hand and ${u(galRich, line("water", "rich", { sulfide: true }))} at water.`,
    },
  ];
  return `<div class="op-problems">${problems
    .map(
      (p, i) =>
        `<div class="op-problem"><b>${i + 1}. ${esc(p.title)}</b><div class="op-ba"><div class="op-before"><span class="ol-tag op-tag-now">now</span> ${linked(p.before)}</div><div class="op-after"><span class="ol-tag op-tag-prop">proposed</span> ${linked(p.after)}</div></div><div class="ol-example"><b>Worked example:</b> ${linked(p.example)}</div></div>`,
    )
    .join("")}</div>`;
}

/** 5. Sulfides and free gold under the proposal, by tier and grade. */
function renderSpecial(): string {
  const table = (o: Options, lines: { label: string; o: Options }[]) => {
    const head = `<tr><th>Grade</th><th>As mined<span class="ol-when">proposed, any rock form</span></th>${TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}<span class="ol-when">best line</span></th>`).join("")}</tr>`;
    const rows = GRADES.map((g) => {
      const home = proposedSmelt(MINED_FORM[g], g, PROPOSED, o.sulfide);
      const cells = TIERS.map((t) => {
        const figs = lines.map((l) => ({ ...bestConcentrate(t, g, { ...o, ...l.o }), label: l.label }));
        const best = figs.reduce((a, b) => (b.value > a.value ? b : a));
        const win = best.value > home + 1e-9;
        return `<td class="ol-c${win ? " op-win" : ""}">${figs.map((f) => `<span class="ol-f${f === best ? " ol-f-best" : ""}"><b>${pct(f.value)}</b> ${esc(f.device)}${f.label ? `, ${esc(f.label)}` : ""}</span>`).join("")}<span class="ol-cnote">${win ? `mine wins by ${pts((best.value - home) * 100)}` : `home wins by ${pts((home - best.value) * 100)}`}</span></td>`;
      });
      const homeWins = TIERS.every((t) => lines.every((l) => bestConcentrate(t, g, { ...o, ...l.o }).value <= home + 1e-9));
      return `<tr><th scope="row"><b>${cap(g)}</b></th><td class="ol-c${homeWins ? " op-win" : ""}"><span class="ol-f ol-f-best"><b>${pct(home)}</b></span></td>${cells.join("")}</tr>`;
    });
    return `<div class="ol-tablewrap"><table class="ol-table op-table"><thead>${head}</thead><tbody>${rows.join("\n")}</tbody></table></div>`;
  };
  return `<h4>Sulfides (galena, chalcopyrite, sphalerite…)</h4>
<p class="ol-lede">Unconcentrated sulfide smelts by grade like an oxide (no penalty in the starting figures); its concentrate still does not smelt until roasted, so the mine line is concentrate × roaster (firepit ${pct(CFG.roasters.firepit)}, stall ${pct(CFG.roasters.stall)}, reverberatory ${pct(CFG.roasters.reverberatory)}). Classified, poor ground where the tier grinds.</p>
${table({ sulfide: true }, [{ label: "", o: {} }])}
<h4>Free gold and silver (gold quartz, silver quartz)</h4>
<p class="ol-lede">Free metal floats past a gravity concentrator unless amalgamated (× ${CFG.freeUnamalgamated}); amalgamated, the sponge smelts whole after retorting. As mined, gold quartz smelts by grade like any rock (and gives no silver: the parting table on the processing line).</p>
${table({ free: true }, [{ label: "amalgamated", o: { amalgamated: true } }, { label: "not amalgamated", o: { amalgamated: false } }])}
<p class="ol-formula"><b>Formula:</b> as mined = the grade's share (× the sulfide penalty, ${PROPOSED.sulfidePenalty} here); the line = concentrator base × ${CFG.unclassified} if unclassified (not here) × ${CFG.fineUnground} if poor and not ground × ${CFG.freeUnamalgamated} if free and not amalgamated, × the roaster for sulfides. Green: the better of carrying home and processing at the mine.</p>`;
}

/** 6. Sensitivity. */
function renderSensitivity(): string {
  const head = `<tr><th>If…</th>${tierHead}</tr>`;
  const rows = VARIANTS.map((v) => {
    const cells = TIERS.map((t) => {
      const d = decision(t, v.grade, v.proposal, v.options);
      return `<td class="ol-c op-d-${d.verdict}"><span class="ol-f"><b>${d.verdict === "mine" ? "mine" : "home"}</b> by ${pts(d.margin)}</span><span class="ol-cnote">${pct(d.mine.value)} ${esc(d.mine.device!)} v ${pct(d.home)}</span></td>`;
    });
    return `<tr><th scope="row">${esc(v.label)}</th>${cells.join("")}</tr>`;
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-sens"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> as the decisions above, with one figure of PROPOSED changed. Poor and bountiful are not nudged downward or upward far: poor at 10 % loses to every concentrator even unclassified by hand (${pct(bestConcentrate("hand", "poor", {}, false).value)}), and bountiful at 75 % loses only to the water jig (${pct(CFG.concentrators.jig)}) and the mill.</p>`;
}

const OP_STYLE = `<style>
.ore-proposal .op-pills { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.3rem 0 0.6rem; padding: 0; list-style: none; font-size: 0.85rem; }
.ore-proposal .op-pills li { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ore-proposal .op-pills li b { color: var(--accent); }
.ore-proposal .op-pills li .op-old { color: var(--muted); text-decoration: line-through; font-weight: 400; }
.op-old { color: var(--muted); font-weight: 400; text-decoration: line-through; }
.op-new { color: var(--accent); }
.op-win { background: color-mix(in srgb, var(--accent) 14%, transparent); }
.op-win .ol-f-best b { color: var(--accent); }
.op-table td.ol-c .ol-f b { color: var(--text); font-size: 0.95rem; }
.op-table td.op-win .ol-f-best b { color: var(--accent); font-size: 1rem; }
.op-which { font-size: 0.78rem; color: var(--muted); white-space: nowrap; }
.op-prop .op-which { color: var(--text); font-weight: 600; }
.op-verdict { font-size: 0.8rem; min-width: 7rem; }
.op-tag-flip { background: color-mix(in srgb, var(--series-2) 18%, transparent); border: 1px solid var(--series-2); color: var(--series-2); }
.op-tag-now { background: var(--surface-2); border: 1px solid var(--border); color: var(--muted); }
.op-tag-prop { background: color-mix(in srgb, var(--accent) 14%, transparent); border: 1px solid var(--accent); color: var(--accent); }
.op-grade { margin: 0.6rem 0 0.9rem; }
.op-grade h4 { margin: 0.4rem 0 0.3rem; }
.op-tier { white-space: nowrap; }
.op-cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 17rem), 1fr)); gap: 0.5rem; margin: 0.5rem 0; }
.op-card { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.6rem; font-size: 0.84rem; }
.op-card ul { margin: 0.25rem 0 0; padding-left: 0; list-style: none; }
.op-card li { margin: 0.2rem 0; padding-left: 0.25rem; border-left: 3px solid var(--border); }
.op-card li.op-d-mine { border-left-color: var(--accent); }
.op-card li.op-d-home { border-left-color: var(--series-2); }
.op-summary { font-weight: 600; margin: 0.2rem 0 !important; }
.op-slots .ol-c .ol-f { white-space: normal; }
.op-problems { display: grid; gap: 0.5rem; }
.op-problem { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.6rem; font-size: 0.84rem; }
.op-ba { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); gap: 0.4rem; margin: 0.3rem 0; }
.op-before, .op-after { padding: 0.35rem 0.5rem; border-radius: var(--radius); border: 1px solid var(--border); }
.op-after { border-color: var(--accent); }
.op-problem .ol-example { margin-top: 0.3rem; }
.op-sens td.op-d-mine .ol-f b { color: var(--accent); }
.op-sens td.op-d-home .ol-f b { color: var(--series-2); }
.op-code li { margin: 0.25rem 0; }
.op-code code { font-size: 0.8rem; }
@media (max-width: 640px) {
  .op-verdict { min-width: 5rem; }
  .op-table { min-width: 36rem; }
  .op-slots { min-width: 40rem; }
}
</style>`;

export default function oreProposal(): string {
  const jump = [
    ["compare", "Current against proposed"],
    ["decision", "The decision each grade faces"],
    ["logistics", "Logistics"],
    ["problems", "The three problems"],
    ["special", "Sulfides and free gold"],
    ["sensitivity", "Sensitivity"],
    ["open", "Open questions and side effects"],
  ];
  const smeltPills = [
    ...GRADES.map((g) => `<li>${g} ore, any rock form <b><span class="op-old">${pct(currentSmelt(MINED_FORM[g]))} / ${pct(currentSmelt("crushed"))}</span> ${pct(PROPOSED.byGrade[g])}</b></li>`),
    `<li>concentrate, roasted concentrate, sponge <b>${pct(PROPOSED.concentrate)}</b> (unchanged)</li>`,
    `<li>sulfide concentrate, unroasted <b>0 %</b> (unchanged)</li>`,
  ];
  const stackPills = FORMS.map((f) => `<li>${esc(FORM_LABEL[f])} <b>${STACK[f]}</b>${ROCK_FORMS.includes(f) ? " (rock)" : ""}</li>`);
  return `<div class="ore-line ore-proposal">
${STYLE}
${OP_STYLE}
<h2>Proposal: the furnace reads grade, not form</h2>
<p class="ol-intro">Two goals: ore processing that is interesting, dynamic and somewhat realistic, and logistics that make processing <b>at the mine</b> necessary. Today bulk sits on one item (raw ore stacks to 4) and crushed ore smelts at a flat ${pct(CFG.smelt.crushed)} at any grade, which opens three escape hatches past the concentrator (section 4). The proposal follows the real logic, where dressing ore existed to throw the rock away before freight and smelting: <b>unconcentrated ore smelts by grade</b> (raw, chunk, crushed and ground alike; crushing is only the way into the concentrator), concentrate stays at 100 %, and <b>bulk follows the rock</b>: every form still carrying its rock stacks poorly, only concentrate stacks well. The concentrator figures and factors stay as on main (ore-line.ts's <code>CFG</code>); the proposal's figures are <code>PROPOSED</code> in <code>ore-proposal.ts</code> and every number here is computed from them. Issues: ${issueLink(684)}, ${issueLink(765)}, ${issueLink(686)}, ${issueLink(688)}.</p>
<p class="ol-intro"><b>What a furnace takes</b>, now (struck through: as mined / crushed) and proposed:</p>
<ul class="op-pills" aria-label="Smelt shares">${smeltPills.join("")}</ul>
<p class="ol-intro"><b>Stack sizes on main</b> (OreProducts.RawStack and ChunkStack set in code on vanilla's ore item; the rest in the item files; see the logistics formula):</p>
<ul class="op-pills" aria-label="Stack sizes">${stackPills.join("")}<li>metal units an ore: ${GRADES.map((g) => `${g} ${UNITS[g]}`).join(", ")} (hematite 20/25/30/40, silver quartz 10/15/20/25, cassiterite and gold quartz 5/10/15/20); concentrate and the other products ${UNITS_PER_PRODUCT}</li></ul>
<nav class="ol-jump" aria-label="Sections">${jump.map(([id, t], i) => `<a href="#op-${id}">${i + 1}. ${esc(t!)}</a>`).join("")}</nav>

<section class="ol-class" id="op-compare" aria-labelledby="op-compare-h"><h3 id="op-compare-h">1. Current against proposed: the best recovery of each choice</h3>
<p class="ol-ores">Oxide ores, of the metal in the rock. For each grade and tier: smelt as mined, crush and smelt, or concentrate and smelt (classified or not; for poor ore, ground or not where the tier has a grinder). The winning choice is shaded; the last column says whether it changes.</p>
${GRADES.map(renderCompare).join("\n")}
<p class="ol-formula"><b>Formula:</b> as mined = smelt share of the mined form (now: raw 0, chunk ${pct(CFG.smelt.chunk)}; proposed: the grade's share); crush and smelt = crushed's share (now ${pct(CFG.smelt.crushed)}; proposed: the grade's share); concentrate = base × ${CFG.unclassified} if unclassified × ${CFG.fineUnground} if poor and not ground, best device of the tier (hand rocker, water jig L2, mill table and vanner L4), density 1.</p>
</section>

<section class="ol-class" id="op-decision" aria-labelledby="op-decision-h"><h3 id="op-decision-h">2. The decision each grade faces under the proposal</h3>
<p class="ol-ores">Carry it home and smelt it as it is, or concentrate it at the mine, at which tier.</p>
${renderDecisions()}
</section>

<section class="ol-class" id="op-logistics" aria-labelledby="op-logistics-h"><h3 id="op-logistics-h">3. Logistics: what a slot is worth, and what it takes to bring 1000 units home</h3>
<h4>Ingots one inventory slot is worth, smelted at that stage</h4>
<p class="ol-ores">The density of each form: a full slot of it, put through a furnace. Where now and proposed differ the cell shows both. Amalgam does not smelt; its slot is worth what it retorts to.</p>
${renderSlots()}
<h4>Bringing ${1000} units of metal home (${1000 / UNITS_PER_INGOT} ingots)</h4>
<p class="ol-ores">By each route, from each grade: how much rock must be dug, what comes home and how many slots it fills. The gap between the rock routes and the concentrate routes is how hard the proposal pushes processing to the mine.</p>
${renderRoutes()}
</section>

<section class="ol-class" id="op-problems" aria-labelledby="op-problems-h"><h3 id="op-problems-h">4. The three problems, before and after</h3>
${renderProblems()}
</section>

<section class="ol-class" id="op-special" aria-labelledby="op-special-h"><h3 id="op-special-h">5. Sulfides and free gold under the proposal</h3>
${renderSpecial()}
</section>

<section class="ol-class" id="op-sensitivity" aria-labelledby="op-sensitivity-h"><h3 id="op-sensitivity-h">6. Sensitivity: nudging the grade figures</h3>
<p class="ol-ores">Each row changes one figure and shows which way the decision goes at each tier, and by how many points.</p>
${renderSensitivity()}
</section>

<section class="ol-questions" id="op-open" aria-labelledby="op-open-h"><h3 id="op-open-h">7. Open questions and side effects</h3>
<ul>${OPEN_QUESTIONS.map((q) => `<li>${linked(q)}</li>`).join("")}</ul>
<h4>The code that would change</h4>
<ul class="op-code">${CODE_CHANGES.map((q) => `<li>${linked(q)}</li>`).join("")}</ul>
<h4>Issue #765</h4>
<p>${linked(ISSUE_765)}</p>
</section>
</div>`;
}
