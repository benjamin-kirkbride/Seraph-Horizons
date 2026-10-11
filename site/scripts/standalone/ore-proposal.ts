// The ore review page's "Proposal: smelt by grade" tab (scripts/standalone/ore-review.json): the
// design the owner has settled for what a furnace takes, broken down so it can be checked. Today
// bulk sits on one item (raw ore stacks to 4), rich and bountiful are mined as chunks (stack 16),
// and crushed ore smelts at a flat 50 % at any grade, which opens escape hatches past the
// concentrator. The settled design has the furnace read the ore's grade, not its form: every rock
// form (raw, crushed, ground, roasted rock) is one item to one raw ore, carries the ore's grade and
// units, stacks to 4 and smelts at a share set by grade; concentrate holds 5 units, stacks to 128
// and smelts whole. Every figure here is computed from SETTLED (next to ore-line.ts's CFG, the
// figures on main) and each table has its formula under it. Plain HTML and CSS on the processing
// line's styles, no script.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

import { CFG, concentration, DEVICES, STYLE, TIER_LABEL, TIERS, tierConcentrate, type Feed, type Tier } from "./ore-line.ts";
import { renderTransport } from "./ore-transport.ts";

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

/** The forms of the settled design. Chunks are gone: `CurrentForm` adds them for today's figures. */
export type Form = "raw" | "crushed" | "ground" | "roastedrock" | "concentrate" | "roastedconcentrate" | "amalgam" | "sponge";
export type CurrentForm = Form | "chunk";
export const FORMS: Form[] = ["raw", "crushed", "ground", "roastedrock", "concentrate", "roastedconcentrate", "amalgam", "sponge"];
export const FORM_LABEL: Record<CurrentForm, string> = {
  raw: "Raw ore",
  chunk: "Chunk",
  crushed: "Crushed ore",
  ground: "Ground ore",
  roastedrock: "Roasted rock",
  concentrate: "Concentrate",
  roastedconcentrate: "Roasted concentrate",
  amalgam: "Amalgam",
  sponge: "Sponge",
};
/** Forms that still carry their rock: one item to one raw ore, the ore's grade and units, stack 4, smelt by grade. */
export const ROCK_FORMS: Form[] = ["raw", "crushed", "ground", "roastedrock"];
export const isRock = (f: CurrentForm): boolean => f === "chunk" || (ROCK_FORMS as CurrentForm[]).includes(f);

/** The settled design's figures. */
export interface Design {
  /** What share of its units a rock form (raw, crushed, ground; roasted rock × the firepit) gives in a furnace, by grade. */
  byGrade: Record<Grade, number>;
  /** Concentrate, roasted concentrate and sponge smelt whole. */
  concentrate: number;
  /** Stack sizes: every rock form 4, every product 128. */
  stack: Record<Form, number>;
  /** The roast a rock form gets: the firepit's figure (the hand roast also takes raw and crushed sulfide ore). */
  rockRoast: number;
}
export const SETTLED: Design = {
  byGrade: { poor: 0.1, medium: 0.25, rich: 0.4, bountiful: 0.55 },
  concentrate: 1,
  stack: { raw: 4, crushed: 4, ground: 4, roastedrock: 4, concentrate: 128, roastedconcentrate: 128, amalgam: 128, sponge: 128 },
  rockRoast: CFG.roasters.firepit,
};
/** The settled stacks, for the modules that read them by name. */
export const STACK = SETTLED.stack;

/**
 * Today (main). Raw ore and chunks: OreProducts.RawStack 4 and ChunkStack 16, set in code on
 * vanilla's game:ore-{grade}-{ore}-{rock} (OreProcessingItems.Apply); rich and bountiful are mined
 * as chunks (OreProducts.FormOfGrade). Crushed: the pack's game:crushed-{ore}-{grain}, 5 units an
 * item and no grade; ground ore the same (seraphhorizons:groundore-{ore}). Smelt shares by form
 * (ore-processing.json's smelt): raw 0, chunk and crushed 0.5, ground 0, concentrate 1. There is no
 * roasted rock today.
 */
export const CURRENT = {
  minedForm: { poor: "raw", medium: "raw", rich: "chunk", bountiful: "chunk" } as Record<Grade, "raw" | "chunk">,
  stack: { raw: 4, chunk: 16, crushed: 16, ground: 16, concentrate: 128, roastedconcentrate: 128, amalgam: 128, sponge: 128 } as Partial<Record<CurrentForm, number>>,
} as const;

/** Units of metal in a concentrate, roasted concentrate, amalgam or sponge item (and in today's crushed and ground ore). */
export const UNITS_PER_PRODUCT = CFG.unitsPerCrushed;
export const UNITS_PER_INGOT = 100;

// ---- what a furnace gives, today and settled

/** Today's smelt share of a form (OreRecovery.SmeltShare): by form alone; grade only decides raw or chunk. */
export function currentSmelt(form: CurrentForm, sulfide = false): number {
  if (form === "amalgam" || form === "roastedrock") return 0;
  if (sulfide && form === "concentrate") return 0;
  return CFG.smelt[form === "sponge" ? "sponge" : form];
}

/**
 * The settled smelt share: rock forms by grade (unroasted sulfide rock 0, roasted rock the grade's
 * share × the firepit), concentrate whole, unroasted sulfide concentrate 0.
 */
export function settledSmelt(form: Form, grade: Grade, d: Design = SETTLED, sulfide = false): number {
  if (form === "amalgam") return 0;
  if (form === "roastedrock") return d.byGrade[grade] * d.rockRoast;
  if (ROCK_FORMS.includes(form)) return sulfide ? 0 : d.byGrade[grade];
  if (sulfide && form === "concentrate") return 0;
  return d.concentrate;
}

/** The best a rock form smelts at for a grade, carried home: the grade's share, or for a sulfide the roasted rock's. */
export const homeSmelt = (grade: Grade, d: Design = SETTLED, sulfide = false) => Math.max(settledSmelt("raw", grade, d, sulfide), settledSmelt("roastedrock", grade, d, sulfide));

// ---- the choices a grade faces at a tier

export interface Choice {
  key: "mined" | "treated" | "conc" | "concUnclassified";
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

/** The best concentrate figure a tier gives a grade, with the roaster for sulfides; poor is ground where the tier grinds (no hand grinder: the quern takes no ore). */
export function bestConcentrate(tier: Tier, grade: Grade, o: Options = {}, classified = true): { device: string; value: number; unground?: number } {
  const feed: Feed = { poor: grade === "poor", classified, free: o.free, amalgamated: o.amalgamated };
  const roast = o.sulfide ? CFG.roasters[DEVICES[tier].roaster.key] : 1;
  const best = tierConcentrate(tier, feed)[0]!;
  const r = { device: best.device, value: best.value * roast } as { device: string; value: number; unground?: number };
  if (grade === "poor" && DEVICES[tier].grinds) r.unground = concentration(CFG.concentrators[DEVICES[tier].conc[0]!.key], { ...feed, ground: false }) * roast;
  return r;
}

/** Every choice at a tier for a grade, under today's figures (d null) or a design. */
export function choices(tier: Tier, grade: Grade, d: Design | null, o: Options = {}): Choice[] {
  const c = bestConcentrate(tier, grade, o, true);
  const u = bestConcentrate(tier, grade, o, false);
  const alt = (x: { unground?: number }) => (x.unground == null ? undefined : { label: "not ground", value: x.unground });
  const treated: Choice = d
    ? o.sulfide
      ? { key: "treated", label: "Roast in the firepit and smelt", value: settledSmelt("roastedrock", grade, d, true) }
      : { key: "treated", label: "Crush and smelt", value: settledSmelt("crushed", grade, d) }
    : { key: "treated", label: "Crush and smelt", value: currentSmelt("crushed", o.sulfide) };
  const mined: Choice = d
    ? { key: "mined", label: "Smelt as mined (raw)", value: settledSmelt("raw", grade, d, o.sulfide) }
    : { key: "mined", label: `Smelt as mined (${CURRENT.minedForm[grade]})`, value: currentSmelt(CURRENT.minedForm[grade], o.sulfide) };
  return [
    mined,
    treated,
    { key: "conc", label: "Concentrate, classified", value: c.value, device: c.device, alt: alt(c) },
    { key: "concUnclassified", label: "Concentrate, unclassified", value: u.value, device: u.device, alt: alt(u) },
  ];
}

export function winner(cs: Choice[]): Choice {
  return cs.reduce((a, b) => (b.value > a.value + 1e-9 ? b : a));
}

export type Verdict = "mine" | "home" | "tie";

/** The decision a grade faces at a tier: carry home and smelt, or concentrate at the mine. Margin in points of recovery. */
export function decision(tier: Tier, grade: Grade, d: Design = SETTLED, o: Options = {}): { home: number; mine: Choice; margin: number; verdict: Verdict } {
  const cs = choices(tier, grade, d, o);
  const home = Math.max(cs[0]!.value, cs[1]!.value);
  const mine = cs[2]!;
  const margin = (mine.value - home) * 100;
  return { home, mine, margin, verdict: Math.abs(margin) < 1e-9 ? "tie" : margin > 0 ? "mine" : "home" };
}

// ---- logistics

/** Units of metal one item of a form holds, mined at a grade. Settled: rock forms carry the ore's units; today crushed and ground carry 5. */
export function unitsPerItem(form: CurrentForm, grade: Grade, d: Design | null): number {
  if (form === "raw" || form === "chunk") return UNITS[grade];
  if (d && isRock(form)) return UNITS[grade];
  return UNITS_PER_PRODUCT;
}

/** The stack a form has, today (d null) or settled; undefined where the form does not exist today. */
export const stackOf = (form: CurrentForm, d: Design | null): number | undefined => (d ? (form === "chunk" ? undefined : d.stack[form]) : CURRENT.stack[form]);

/** Metal units a full slot of a form gives smelted: stack × units an item × the smelt share. */
export function unitsPerSlot(form: CurrentForm, grade: Grade, d: Design | null, sulfide = false): number {
  const share = d ? (form === "chunk" ? 0 : settledSmelt(form, grade, d, sulfide)) : currentSmelt(form, sulfide);
  return (stackOf(form, d) ?? 0) * unitsPerItem(form, grade, d) * share;
}
export const ingotsPerSlot = (form: CurrentForm, grade: Grade, d: Design | null, sulfide = false) => unitsPerSlot(form, grade, d, sulfide) / UNITS_PER_INGOT;

export interface Route {
  label: string;
  /** Units of metal in the rock that must be mined. */
  rock: number;
  /** Items carried home. */
  items: number;
  /** Inventory slots those items fill. */
  slots: number;
  form: CurrentForm;
  /** Of the metal in the rock, what reaches the furnace. */
  recovery: number;
}

/** Bringing `target` units of metal home from a grade: smelt as mined, carry crushed, or concentrate at each tier. */
export function routes(grade: Grade, d: Design | null, target = 1000, o: Options = {}): Route[] {
  const mined: CurrentForm = d ? "raw" : CURRENT.minedForm[grade];
  const smelt = (form: CurrentForm) => (d ? (form === "chunk" ? 0 : settledSmelt(form, grade, d, o.sulfide)) : currentSmelt(form, o.sulfide));
  const carry = (label: string, form: CurrentForm, recovery: number): Route => {
    const stack = stackOf(form, d);
    if (recovery <= 0 || !stack) return { label, rock: Infinity, items: Infinity, slots: Infinity, form, recovery: 0 };
    const rock = target / recovery;
    // What is carried: the rock's units in that form's items (concentrate: the recovered units, 5 each).
    const carried = form === "concentrate" || form === "roastedconcentrate" ? target : rock;
    const items = carried / unitsPerItem(form, grade, d);
    return { label, rock, items, slots: items / stack, form, recovery };
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

// ---- nudging the ladder

export interface Variant {
  label: string;
  grade: Grade;
  design: Design;
}
const nudge = (grade: Grade, share: number): Variant => ({
  label: `${grade} ${Math.round(share * 100)} %${share === SETTLED.byGrade[grade] ? " (settled)" : ""}`,
  grade,
  design: { ...SETTLED, byGrade: { ...SETTLED.byGrade, [grade]: share } },
});
export const VARIANTS: Variant[] = GRADES.flatMap((g) => [-0.05, 0, 0.05].map((dx) => nudge(g, Math.round((SETTLED.byGrade[g] + dx) * 100) / 100)));

// ---- open questions and side effects

const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";

export const OPEN_QUESTIONS: string[] = [
  "Liquation and acid parting at the mill tier (#740): the processing line's parting table still keys the figures by the old five tiers, and which station parts at the mill is not settled.",
  "Sulfur: roasting sulfide rock in the firepit, like roasting concentrate, could give sulfur. Does roasted rock yield it, at what rate, and does the hand roast's rate differ from the stall's and the reverberatory's?",
  "Per-item machine times: a crushed or ground item now carries 15 to 35 units instead of 5, so a crusher, grinder or concentrator that is timed per item does three to seven times the work in the same seconds. Each machine's seconds an item (and its hopper and chute throughput) needs retuning to the units it moves, not the items.",
  "Nuggets: a nugget crushes to one crushed ore today. Under the settled design crushed ore carries a grade, so a nugget's crushed ore needs one (medium, as its units suggest?).",
];

export const CODE_CHANGES: string[] = [
  `ore-processing.json: "smelt" becomes a share by grade for the rock forms, "smelt": { "byGrade": { "poor": ${SETTLED.byGrade.poor}, "medium": ${SETTLED.byGrade.medium}, "rich": ${SETTLED.byGrade.rich}, "bountiful": ${SETTLED.byGrade.bountiful} }, "concentrate": 1, "roastedconcentrate": 1, "sponge": 1 }; the per-form raw, chunk, crushed, ground keys go. Roasted rock smelts at its grade's share × the firepit's roaster figure; unroasted sulfide rock and concentrate at 0.`,
  "OreRecovery.SmeltShare(ore, form) gains the grade: SmeltShare(ore, form, grade), returning byGrade[grade] for Raw, Crushed and Ground (0 for a sulfide), byGrade[grade] × roasters.firepit for RoastedRock, 0 for unroasted sulfide concentrate, and the form's share otherwise. The processing line tab (ore-line.ts's CFG.smelt, COMMON_STOPS and the worked examples) and the README's \"Ore processing\" section follow.",
  "Our own graded crushed and ground ore: seraphhorizons:crushedore-{grade}-{ore} and groundore-{grade}-{ore} (four an ore, with roastedrock-{grade}-{ore} for the sulfides), each carrying the raw ore's units and CombustibleProps from OreProcessingItems, stack 4. Vanilla's game:crushed-{ore}-{grain} and every recipe that takes ore or crushed ore are owned and patched to the pack's items; Expanded Matter's em:crushed-ore stays out of the line.",
  "OreProducts: ChunkStack and FormOfGrade go (rich and bountiful drop raw ore at RawStack 4 like every grade); CrushedStack 16 becomes 4; a GroundStack and RoastedRockStack of 4; ConcentrateStack 128 stays, as do roasted concentrate, amalgam and sponge. CrushedCode and the grain variant go with the graded item. UnitCarry: crushing and grinding carry the item's units and grade through 1:1; only the concentrator's output is in 5-unit items.",
  "Roasting rock in the firepit: the hand roast's recipe (the firepit) takes raw or crushed sulfide ore as well as concentrate and gives roasted rock of the same grade; the stall and the reverberatory roast concentrate only.",
  "Quern: remove its ore recipes (crushed ore to ground ore by hand). Grinding starts at the water tier (arrastra, then ball mill); poor ore by hand stays unground (× 0.4). Spalling with a hammer stays the hand crush.",
  "Machine times: every machine timed per item (crusher, arrastra, ball mill, the concentrators, the roasters' rock input) is retuned to the units an item carries (15 to 35 instead of 5), or timed per unit. Hopper and chute throughput that counted items is revisited with it.",
  "Item values: the recipe export's item-values check weighs items by what they smelt to. Raw ore gaining a smelt, chunks going, and crushed and ground ore's value following their grade all move it; concentrate's does not.",
  "Yang's Transport Tycoon: every one of its vehicles at half its top speed (a JSON patch halving config/trackmaterialspeeds.json's caps and the engines' power in enginecart.json and sglocomotive.json, guarded like the pack's other patches on other mods). The advanced locomotive still breaking the bulk lever is accepted as the late-game reward; the transport section's figures include the halving.",
];

export const ISSUE_765 =
  "#765 (open) reworks the recovery core to three tiers and per-device lines and says: \"Keep the factors and the config file; move per-tier figures to per-device ones where they differ\". It does not mention the smelt shares. The settled design's grade-read furnace (the smelt map by grade, SmeltShare taking the grade, the graded rock items and the chunk's removal) should be added to #765's text, or get its own issue under #684 that #765 links, so the two reworks of OreRecovery land together.";

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
const linked = (s: string) => esc(s).replace(/#(\d{3,4})\b/g, (_m, n: string) => issueLink(Number(n)));
const pct = (x: number) => `${Math.round(x * 1000) / 10} %`;
const pts = (x: number) => `${Math.round(Math.abs(x) * 10) / 10}`;
const num = (x: number, d = 1) => (Number.isFinite(x) ? `${Math.round(x * 10 ** d) / 10 ** d}` : "∞");
const cap = (s: string) => s[0]!.toUpperCase() + s.slice(1);
const tierHead = TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</th>`).join("");
const VERDICT_WORD: Record<Verdict, string> = { mine: "mine", home: "home", tie: "tie" };

function fig(value: number, best: boolean, device?: string, alt?: Choice["alt"]): string {
  return `<span class="ol-f${best ? " ol-f-best" : ""}"><b>${pct(value)}</b>${device ? ` ${esc(device)}` : ""}</span>${alt ? `<span class="ol-cnote">${esc(alt.label)} ${pct(alt.value)}</span>` : ""}`;
}

/** 1. Current against settled, one table a grade. */
function renderCompare(grade: Grade): string {
  const keys = ["Smelt as mined", "Crush and smelt", "Concentrate, classified", "Concentrate, unclassified"];
  const head = `<tr><th>Tier</th><th>Figures</th>${keys.map((k) => `<th>${esc(k)}</th>`).join("")}<th>Winner</th></tr>`;
  const rows = TIERS.flatMap((t) => {
    const now = choices(t, grade, null);
    const set = choices(t, grade, SETTLED);
    const wNow = winner(now);
    const wSet = winner(set);
    const flips = wNow.key !== wSet.key;
    const cells = (cs: Choice[], w: Choice) =>
      cs.map((c) => `<td class="ol-c${c === w ? " op-win" : ""}${c.value === 0 ? " ol-c-zero" : ""}">${fig(c.value, c === w, c.device, c.alt)}</td>`).join("");
    const short = (c: Choice) => esc(c.label.replace(/ \(.*\)/, ""));
    const tierCell = `<th scope="row" rowspan="2" class="op-tier"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}</th>`;
    const tie = decision(t, grade).verdict === "tie";
    return [
      `<tr class="op-now">${tierCell}<td class="op-which">Now</td>${cells(now, wNow)}<td class="op-verdict">${short(wNow)}</td></tr>`,
      `<tr class="op-prop"><td class="op-which">Settled</td>${cells(set, wSet)}<td class="op-verdict">${short(wSet)}${tie ? ' <span class="ol-tag op-tag-flip">tie</span>' : flips ? ' <span class="ol-tag op-tag-flip">flips</span>' : ' <span class="ol-tag ol-tag-opt">same</span>'}</td></tr>`,
    ];
  }).join("\n");
  const minedNow = CURRENT.minedForm[grade];
  return `<div class="op-grade" id="op-cmp-${grade}"><h4>${cap(grade)}: ${UNITS[grade]} units an ore; now mined as ${minedNow === "raw" ? "raw ore (stack 4)" : "a chunk (stack 16)"} at ${pct(currentSmelt(minedNow))}, settled raw ore (stack ${SETTLED.stack.raw}) at ${pct(SETTLED.byGrade[grade])}</h4>
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
          : d.verdict === "tie"
            ? `<b>a dead heat</b>: the ${esc(d.mine.device!)} (${pct(d.mine.value)}) and smelting as is (${pct(d.home)}) are the same figure, so the rocker only pays unclassified, never`
            : `<b>carry home and smelt as is</b>: ${pct(d.home)} beats the ${esc(d.mine.device!)} (${pct(d.mine.value)}) by <b>${pts(d.margin)} points</b>`;
      return `<li class="op-d-${d.verdict}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}: ${say}.</li>`;
    });
    const mineTiers = TIERS.filter((t) => decision(t, g).verdict === "mine");
    const rest = TIERS.filter((t) => !mineTiers.includes(t));
    const summary =
      mineTiers.length === 3
        ? "Process at the mine at every tier."
        : mineTiers.length === 0
          ? "Carry it home at every tier."
          : `Process at the mine at ${mineTiers.map((t) => TIER_LABEL[t][0].toLowerCase()).join(" and ")}; by ${rest.map((t) => TIER_LABEL[t][0].toLowerCase()).join(" and ")}, ${rest.every((t) => decision(t, g).verdict === "tie") ? "it is even" : "carry it home"}.`;
    return `<div class="op-card"><b>${cap(g)}</b> <span class="ol-when">as mined ${pct(SETTLED.byGrade[g])}</span><p class="op-summary">${summary}</p><ul>${lines.join("")}</ul></div>`;
  });
  return `<div class="op-cards">${cards.join("")}</div>
<p class="ol-formula"><b>Formula:</b> margin = best concentrate (classified, poor ground where the tier grinds: never by hand, the quern takes no ore) − the grade's share as mined, in points; crushing changes nothing in the furnace, so "smelt as is" and "crush and smelt" are the same figure. Oxide ores; sulfides and free gold are below.</p>`;
}

/** 3a. Ingots a slot is worth, per form and grade, now and settled. */
function renderSlots(): string {
  const forms: CurrentForm[] = ["raw", "chunk", "crushed", "ground", "roastedrock", "concentrate", "roastedconcentrate", "amalgam", "sponge"];
  const head = `<tr><th>Form</th><th>Stack<span class="ol-when">now → settled</span></th><th>Units an item<span class="ol-when">now → settled</span></th>${GRADES.map((g) => `<th>${cap(g)}<span class="ol-when">now → settled</span></th>`).join("")}</tr>`;
  const arrow = (a: string, b: string) => (a === b ? `<b>${a}</b>` : `<b class="op-old">${a}</b> → <b class="op-new">${b}</b>`);
  const rows = forms
    .map((f) => {
      const nowStack = stackOf(f, null);
      const setStack = stackOf(f, SETTLED);
      const cells = GRADES.map((g) => {
        if (f === "amalgam") return `<td class="ol-c ol-c-none">retort it: ${num(ingotsPerSlot("sponge", g, SETTLED))} as sponge</td>`;
        const nowExists = nowStack != null && !((f === "raw" || f === "chunk") && CURRENT.minedForm[g] !== f) && !(f === "ground" && g !== "poor");
        const now = nowExists ? ingotsPerSlot(f, g, null) : null;
        if (setStack == null) return `<td class="ol-c ol-c-none"><span class="ol-f"><b class="op-old">${num(now ?? 0, 2)}</b> → gone</span></td>`;
        const set = f === "roastedrock" ? ingotsPerSlot(f, g, SETTLED, true) : ingotsPerSlot(f, g, SETTLED);
        const same = now != null && Math.abs(now - set) < 1e-9;
        return `<td class="ol-c${set === 0 ? " ol-c-zero" : ""}"><span class="ol-f">${now == null ? `<b class="op-new">${num(set, 2)}</b>` : same ? `<b>${num(set, 2)}</b>` : `<b class="op-old">${num(now, 2)}</b> → <b class="op-new">${num(set, 2)}</b>`} ingots</span><span class="ol-cnote">${num(f === "roastedrock" ? unitsPerSlot(f, g, SETTLED, true) : unitsPerSlot(f, g, SETTLED))} units${now == null && nowStack == null ? ", new" : now == null ? ", not today" : ""}</span></td>`;
      });
      const unitsNow = f === "raw" || f === "chunk" ? GRADES.filter((g) => CURRENT.minedForm[g] === f).map((g) => UNITS[g]).join(" / ") : nowStack == null ? "—" : `${UNITS_PER_PRODUCT}`;
      const unitsSet = setStack == null ? "—" : isRock(f) ? GRADES.map((g) => UNITS[g]).join(" / ") : `${UNITS_PER_PRODUCT}`;
      const note = f === "roastedrock" ? '<span class="ol-when">sulfide, firepit; carries its rock</span>' : isRock(f) ? '<span class="ol-when">carries its rock</span>' : "";
      return `<tr><th scope="row"><b>${esc(FORM_LABEL[f])}</b>${note}</th><td>${arrow(nowStack == null ? "—" : `${nowStack}`, setStack == null ? "—" : `${setStack}`)}</td><td>${arrow(unitsNow, unitsSet)}</td>${cells.join("")}</tr>`;
    })
    .join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-slots"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> ingots a slot = stack size × metal units an item × that form's smelt share ÷ ${UNITS_PER_INGOT} units an ingot. Settled, every rock form is one item to one raw ore with the ore's units and stacks to ${SETTLED.stack.raw}, so a slot of crushed or ground ore is worth exactly a slot of raw ore (poor ${num(ingotsPerSlot("raw", "poor", SETTLED), 2)}, medium ${num(ingotsPerSlot("raw", "medium", SETTLED), 2)}, rich ${num(ingotsPerSlot("raw", "rich", SETTLED), 2)}, bountiful ${num(ingotsPerSlot("raw", "bountiful", SETTLED), 2)} ingots): crushing and grinding change nothing in bulk, and the chunk's 16 and today's crushed ore at 16 × 5 units are gone. Roasted rock is sulfide ore roasted in the firepit: its grade's share × ${SETTLED.rockRoast}. A concentrate, roasted concentrate or sponge item holds ${UNITS_PER_PRODUCT} units whatever the ore's grade and whichever tier made it, so its slot is worth the same ${num(ingotsPerSlot("concentrate", "poor", SETTLED))} ingots everywhere; concentrating is the one step where bulk drops, and the recovery up to it decides how much ore it took to fill the slot, which the next table shows. Smelt shares: now ore-processing.json's smelt (raw 0, chunk and crushed ${CFG.smelt.crushed}, ground 0, concentrate 1); settled by grade ${GRADES.map((g) => `${g} ${pct(SETTLED.byGrade[g])}`).join(", ")} for the rock forms, concentrate 1.</p>`;
}

/** 3b. Bringing 1000 units home by each route. */
function renderRoutes(): string {
  const target = 1000;
  const head = `<tr><th>Grade</th><th>Route</th><th>Ore mined<span class="ol-when">units in the rock</span></th><th>Carried as</th><th>Items<span class="ol-when">now → settled</span></th><th>Slots<span class="ol-when">now → settled</span></th></tr>`;
  const pair = (a: number, b: number, d = 1) => {
    const same = Math.abs(a - b) < 1e-9 || (!Number.isFinite(a) && !Number.isFinite(b));
    return `<span class="ol-f"><b class="${same ? "" : "op-old"}">${num(a, d)}</b>${same ? "" : ` → <b class="op-new">${num(b, d)}</b>`}</span>`;
  };
  const rows = GRADES.flatMap((g) => {
    const now = routes(g, null, target);
    const set = routes(g, SETTLED, target);
    return set.map((r, i) => {
      const n = now[i]!;
      return `<tr>${i === 0 ? `<th scope="row" rowspan="${set.length}" class="op-tier"><b>${cap(g)}</b><span class="ol-when">${UNITS[g]} units an ore</span></th>` : ""}<td>${esc(r.label)}</td><td>${num(r.rock, 0)}<span class="ol-cnote">at ${pct(r.recovery)}${Math.abs(n.recovery - r.recovery) > 1e-9 ? `, now ${pct(n.recovery)}` : ""}</span></td><td>${esc(FORM_LABEL[r.form])} <span class="ol-cnote">stack ${stackOf(r.form, SETTLED)}${n.form !== r.form ? `, now ${esc(FORM_LABEL[n.form].toLowerCase())} ${stackOf(n.form, null)}` : stackOf(n.form, null) !== stackOf(r.form, SETTLED) ? `, now ${stackOf(n.form, null)}` : ""}</span></td><td class="ol-c">${pair(n.items, r.items, 0)}</td><td class="ol-c op-slotcell">${pair(n.slots, r.slots)}</td></tr>`;
    });
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-routes"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> ore mined = ${target} ÷ the route's recovery; items = the units carried ÷ units an item (rock forms carry the whole rock, one item an ore mined; concentrate only the ${target} recovered units at ${UNITS_PER_PRODUCT} each); slots = items ÷ stack. ∞: the route gives no metal today (raw ore does not smelt). Carrying crushed is the same haul as carrying raw: the settled design makes crushing lossless and bulk-neutral, so the only reason to crush at the mine is to feed the concentrator. Concentrate at the mine always comes home as ${num(target / UNITS_PER_PRODUCT / SETTLED.stack.concentrate)} slots; the tier only changes how much rock is dug.</p>`;
}

/** 5. The three problems, before and after. */
function renderProblems(): string {
  const handPoor = bestConcentrate("hand", "poor");
  const handMed = bestConcentrate("hand", "medium");
  const handMedU = bestConcentrate("hand", "medium", {}, false);
  const handSulf = bestConcentrate("hand", "medium", { sulfide: true });
  const waterSulf = bestConcentrate("water", "bountiful", { sulfide: true });
  // Worked examples in units: hematite 20 a poor ore, the default 20 a medium ore, galena 25 medium and 35 bountiful.
  const u = (total: number, share: number) => `${num(total * share)}`;
  const hem = 8 * UNIT_EXCEPTIONS.hematite!.poor;
  const med = 4 * UNITS.medium;
  const gal = 4 * UNITS.medium;
  const galB = 4 * UNITS.bountiful;
  const line = (tier: Tier, grade: Grade, o: Options = {}) => bestConcentrate(tier, grade, o).value;
  const S = SETTLED.byGrade;
  const roasted = (g: Grade) => settledSmelt("roastedrock", g, SETTLED, true);
  const bSulf = decision("hand", "bountiful", SETTLED, { sulfide: true });
  const problems = [
    {
      title: "Poor ore is better crushed and smelted than concentrated by hand",
      before: `Crushed smelts at ${pct(currentSmelt("crushed"))}; the rocker, with no hand grinder, catches ${pct(handPoor.value)} (${CFG.concentrators.rocker} × ${CFG.fineUnground}). Crushing wins by ${pts((currentSmelt("crushed") - handPoor.value) * 100)} points, so nobody concentrates poor ore by hand.`,
      after: `Poor smelts at ${pct(S.poor)} in any rock form. The rocker's ${pct(handPoor.value)} wins by ${pts((handPoor.value - S.poor) * 100)} points (there is still no hand grinder: the quern takes no ore); at water the ground jig gives ${pct(bestConcentrate("water", "poor").value)}, ${pts((bestConcentrate("water", "poor").value - S.poor) * 100)} points over carrying it home.`,
      example: `8 poor hematite (${UNIT_EXCEPTIONS.hematite!.poor} units each, ${hem} units of iron). Now: raw ore does not smelt; spalled, ${hem} × ${pct(currentSmelt("crushed"))} = ${u(hem, currentSmelt("crushed"))} units smelted crushed against ${hem} × ${pct(handPoor.value)} = ${u(hem, handPoor.value)} concentrated by hand. Settled: ${hem} × ${pct(S.poor)} = ${u(hem, S.poor)} units smelted in any rock form, ${u(hem, handPoor.value)} from the rocker, ${u(hem, line("water", "poor"))} from the water jig, ${u(hem, line("mill", "poor"))} from the mill table and vanner.`,
    },
    {
      title: "Medium ore in the rocker barely beats smelting it crushed",
      before: `Rocker ${pct(handMed.value)} against crushed ${pct(currentSmelt("crushed"))}: ${pts((handMed.value - currentSmelt("crushed")) * 100)} points for the work of classifying and rocking, and unclassified (${pct(handMedU.value)}) it loses.`,
      after: `Medium smelts at ${pct(S.medium)}. The rocker wins by ${pts((handMed.value - S.medium) * 100)} points classified and ${pts((handMedU.value - S.medium) * 100)} unclassified; the choice is clear at every tier.`,
      example: `4 medium ore of the default ${UNITS.medium} units (${med} units). Now: ${u(med, currentSmelt("crushed"))} smelted crushed against ${u(med, handMed.value)} from the rocker (${u(med, handMedU.value)} unclassified). Settled: ${u(med, S.medium)} smelted as is, still ${u(med, handMed.value)} from the rocker, ${u(med, line("water", "medium"))} from the jig, ${u(med, line("mill", "medium"))} from the mill.`,
    },
    {
      title: "By hand, concentrated and roasted sulfide loses to smelting it crushed",
      before: `Rocker ${pct(CFG.concentrators.rocker)} × firepit ${pct(CFG.roasters.firepit)} = ${pct(handSulf.value)}, against crushed sulfide at ${pct(currentSmelt("crushed"))} with no roasting. The hand sulfide line only pays for the silver the cupel wins.`,
      after: `Unroasted sulfide rock does not smelt. Roasted in the firepit it smelts at its grade's share × ${SETTLED.rockRoast}: medium ${pct(roasted("medium"))}, so the hand line's ${pct(handSulf.value)} wins by ${pts((handSulf.value - roasted("medium")) * 100)} points. Bountiful roasted rock (${pct(roasted("bountiful"))}) is ${bSulf.verdict === "tie" ? "exactly the hand line's figure (the same firepit factor on both sides)" : `${pts(bSulf.margin)} points ${bSulf.verdict === "mine" ? "under" : "over"} the hand line`}; the water line (jig × stall ${pct(waterSulf.value)}) beats it by ${pts((waterSulf.value - roasted("bountiful")) * 100)}.`,
      example: `4 medium galena (${UNITS.medium} units each, ${gal} units of lead). Now: ${u(gal, currentSmelt("crushed"))} smelted crushed against ${u(gal, handSulf.value)} roasted from the rocker. Settled: 0 as mined, ${u(gal, roasted("medium"))} roasted in the firepit, ${u(gal, handSulf.value)} by the hand line, ${u(gal, line("water", "medium", { sulfide: true }))} at water, ${u(gal, line("mill", "medium", { sulfide: true }))} at the mill. 4 bountiful galena (${UNITS.bountiful} units, ${galB}): ${u(galB, roasted("bountiful"))} roasted as rock against ${u(galB, line("hand", "bountiful", { sulfide: true }))} by the hand line and ${u(galB, line("water", "bountiful", { sulfide: true }))} at water.`,
    },
  ];
  return `<div class="op-problems">${problems
    .map(
      (p, i) =>
        `<div class="op-problem"><b>${i + 1}. ${esc(p.title)}</b><div class="op-ba"><div class="op-before"><span class="ol-tag op-tag-now">now</span> ${linked(p.before)}</div><div class="op-after"><span class="ol-tag op-tag-prop">settled</span> ${linked(p.after)}</div></div><div class="ol-example"><b>Worked example:</b> ${linked(p.example)}</div></div>`,
    )
    .join("")}</div>`;
}

/** 6. Sulfides and free gold under the settled design, by tier and grade. */
function renderSpecial(): string {
  const table = (o: Options, homeLabel: string, lines: { label: string; o: Options }[]) => {
    const head = `<tr><th>Grade</th><th>${homeLabel}</th>${TIERS.map((t) => `<th class="ol-th-${t}"><span class="ol-dot ol-dot-${t}"></span>${TIER_LABEL[t][0]}<span class="ol-when">best line</span></th>`).join("")}</tr>`;
    const rows = GRADES.map((g) => {
      const home = homeSmelt(g, SETTLED, o.sulfide);
      const cells = TIERS.map((t) => {
        const figs = lines.map((l) => ({ ...bestConcentrate(t, g, { ...o, ...l.o }), label: l.label }));
        const best = figs.reduce((a, b) => (b.value > a.value ? b : a));
        const diff = (best.value - home) * 100;
        const win = diff > 1e-9;
        const tie = Math.abs(diff) < 1e-9;
        return `<td class="ol-c${win ? " op-win" : ""}">${figs.map((f) => `<span class="ol-f${f === best ? " ol-f-best" : ""}"><b>${pct(f.value)}</b> ${esc(f.device)}${f.label ? `, ${esc(f.label)}` : ""}</span>`).join("")}<span class="ol-cnote">${tie ? "a dead heat" : win ? `mine wins by ${pts(diff)}` : `home wins by ${pts(-diff)}`}</span></td>`;
      });
      const homeWins = TIERS.every((t) => lines.every((l) => bestConcentrate(t, g, { ...o, ...l.o }).value <= home + 1e-9));
      return `<tr><th scope="row"><b>${cap(g)}</b></th><td class="ol-c${homeWins ? " op-win" : ""}"><span class="ol-f ol-f-best"><b>${pct(home)}</b></span>${o.sulfide ? `<span class="ol-cnote">unroasted ${pct(settledSmelt("raw", g, SETTLED, true))}</span>` : ""}</td>${cells.join("")}</tr>`;
    });
    return `<div class="ol-tablewrap"><table class="ol-table op-table"><thead>${head}</thead><tbody>${rows.join("\n")}</tbody></table></div>`;
  };
  return `<h4>Sulfides (galena, chalcopyrite, sphalerite…)</h4>
<p class="ol-lede">Unroasted sulfide rock does not smelt, and neither does unroasted sulfide concentrate. The hand roast (the firepit, ${pct(CFG.roasters.firepit)}) takes raw or crushed sulfide ore and gives roasted rock, which smelts at its grade's share × ${SETTLED.rockRoast}; it roasts concentrate too. The mine line is concentrate × the tier's roaster (firepit ${pct(CFG.roasters.firepit)}, stall ${pct(CFG.roasters.stall)}, reverberatory ${pct(CFG.roasters.reverberatory)}); roasted concentrate smelts at 100 %. Classified, poor ground where the tier grinds.</p>
${table({ sulfide: true }, `Roasted rock, carried home<span class="ol-when">grade share × ${SETTLED.rockRoast}</span>`, [{ label: "", o: {} }])}
<h4>Free gold and silver (gold quartz, silver quartz)</h4>
<p class="ol-lede">Free metal floats past a gravity concentrator unless amalgamated (× ${CFG.freeUnamalgamated}); amalgamated, the sponge smelts whole after retorting. As mined, gold quartz smelts by grade like any rock (and gives no silver: the parting table on the processing line).</p>
${table({ free: true }, 'As mined<span class="ol-when">any rock form</span>', [{ label: "amalgamated", o: { amalgamated: true } }, { label: "not amalgamated", o: { amalgamated: false } }])}
<p class="ol-formula"><b>Formula:</b> carried home = the grade's share (a sulfide: × ${SETTLED.rockRoast} for the firepit, 0 unroasted); the line = concentrator base × ${CFG.unclassified} if unclassified (not here) × ${CFG.fineUnground} if poor and not ground × ${CFG.freeUnamalgamated} if free and not amalgamated, × the roaster for sulfides. Green: the better of carrying home and processing at the mine. Bountiful by hand is the same figure on both sides, with or without the roast, because the rocker's ${pct(CFG.concentrators.rocker)} equals bountiful's share and the firepit multiplies both.</p>`;
}

/** 7. How the grade ladder could be nudged. */
function renderNudges(): string {
  const head = `<tr><th>If…</th>${tierHead}</tr>`;
  const rows = VARIANTS.map((v) => {
    const cells = TIERS.map((t) => {
      const d = decision(t, v.grade, v.design);
      return `<td class="ol-c op-d-${d.verdict}"><span class="ol-f"><b>${VERDICT_WORD[d.verdict]}</b>${d.verdict === "tie" ? "" : ` by ${pts(d.margin)}`}</span><span class="ol-cnote">${pct(d.mine.value)} ${esc(d.mine.device!)} v ${pct(d.home)}</span></td>`;
    });
    return `<tr${v.label.endsWith("(settled)") ? ' class="op-prop"' : ""}><th scope="row">${esc(v.label)}</th>${cells.join("")}</tr>`;
  }).join("\n");
  const b = SETTLED.byGrade.bountiful;
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-sens"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> as the decisions above, with one grade's share moved five points either way. The ladder's one edge is bountiful at ${pct(b)} against the rocker's ${pct(CFG.concentrators.rocker)}: five points lower and the rocker pays by hand, five higher and bountiful is carried home by hand (and still concentrated at water, jig ${pct(CFG.concentrators.jig)}). Poor at ${pct(SETTLED.byGrade.poor)} loses to every concentrator even unclassified by hand (${pct(bestConcentrate("hand", "poor", {}, false).value)}).</p>`;
}

const OP_STYLE = `<style>
.ore-proposal .op-pills { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.3rem 0 0.6rem; padding: 0; list-style: none; font-size: 0.85rem; }
.ore-proposal .op-pills li { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ore-proposal .op-pills li b { color: var(--accent); }
.ore-proposal .op-pills li .op-old { color: var(--muted); text-decoration: line-through; font-weight: 400; }
.ore-proposal .op-settled { margin: 0.3rem 0 0.6rem; padding-left: 1.2rem; font-size: 0.9rem; }
.ore-proposal .op-settled li { margin: 0.15rem 0; }
.ore-proposal .op-rule { background: var(--surface); border-left: 3px solid var(--accent); border-radius: var(--radius); padding: 0.4rem 0.6rem; margin: 0.4rem 0 0.6rem; font-size: 0.9rem; }
.op-old { color: var(--muted); font-weight: 400; text-decoration: line-through; }
.op-new { color: var(--accent); }
.op-win { background: color-mix(in srgb, var(--accent) 14%, transparent); }
.op-win .ol-f-best b { color: var(--accent); }
.op-table td.ol-c .ol-f b { color: var(--text); font-size: 0.95rem; }
.op-table td.op-win .ol-f-best b { color: var(--accent); font-size: 1rem; }
.op-which { font-size: 0.78rem; color: var(--muted); white-space: nowrap; }
.op-prop .op-which { color: var(--text); font-weight: 600; }
.op-sens tr.op-prop th { color: var(--accent); }
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
.op-card li.op-d-tie { border-left-color: var(--muted); }
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
.op-sens td.op-d-tie .ol-f b { color: var(--muted); }
.op-code li { margin: 0.25rem 0; }
.op-code code { font-size: 0.8rem; }
@media (max-width: 640px) {
  .op-verdict { min-width: 5rem; }
  .op-table { min-width: 36rem; }
  .op-slots { min-width: 44rem; }
}
</style>`;

export default function oreProposal(): string {
  const jump = [
    ["compare", "Current against settled"],
    ["decision", "The decision each grade faces"],
    ["logistics", "Logistics"],
    ["transport", "Transport: can rock be hauled home?"],
    ["problems", "The three problems"],
    ["special", "Sulfides and free gold"],
    ["nudge", "Nudging the ladder"],
    ["open", "Open questions and the code"],
  ];
  const S = SETTLED;
  const settled = [
    `<b>Rock forms are the pack's own items</b>: raw ore, crushed ore, ground ore and roasted rock (raw or crushed sulfide ore roasted in the firepit). Each stacks to <b>${S.stack.raw}</b> and converts <b>1 : 1</b>, one raw ore to one crushed to one ground, carrying the ore's grade and units unchanged: crushing and grinding are lossless and never change bulk. Vanilla's crushed-{ore}-{grain} and every recipe that takes ore are owned and patched.`,
    `<b>No chunks.</b> Rich and bountiful drop raw ore at ${S.stack.raw} a stack like every grade.`,
    `<b>Rock smelts by grade</b>, raw, crushed or ground alike: ${GRADES.map((g) => `${g} <b>${pct(S.byGrade[g])}</b>`).join(", ")}.`,
    `<b>Sulfides.</b> Unroasted sulfide rock does not smelt, nor unroasted sulfide concentrate. The hand roast (firepit, ${pct(CFG.roasters.firepit)}) also takes raw or crushed sulfide ore, giving roasted rock that smelts at its grade's share × ${S.rockRoast}. Roasted concentrate smelts at 100 %; the stall (${pct(CFG.roasters.stall)}) and the reverberatory (${pct(CFG.roasters.reverberatory)}) roast concentrate.`,
    `<b>Concentrate</b>: ${UNITS_PER_PRODUCT} units an item, <b>${S.stack.concentrate}</b> a stack (${num(ingotsPerSlot("concentrate", "poor", S))} ingots a slot at any grade), smelting at ${pct(S.concentrate)}. Concentrating is the one step where bulk drops. Roasted concentrate, amalgam and sponge stay at ${S.stack.concentrate}.`,
    `<b>The concentrator figures and factors are unchanged</b>: pan ${pct(CFG.concentrators.pan)}, rocker ${pct(CFG.concentrators.rocker)}, sluice ${pct(CFG.concentrators.sluice)}, jig ${pct(CFG.concentrators.jig)}, table ${pct(CFG.concentrators.table)}, table and vanner ${pct(CFG.concentrators.tableandvanner)}; unclassified × ${CFG.unclassified}, poor not ground × ${CFG.fineUnground}, free metal not amalgamated × ${CFG.freeUnamalgamated}.`,
    `<b>The quern takes no ore.</b> There is no hand grinding; grinding starts at the water tier (arrastra, then ball mill). Poor ore by hand stays at ${pct(bestConcentrate("hand", "poor").value)}.`,
    `<b>Spalling</b> with a hammer is the hand crush, unchanged.`,
    `<b>Yang's vehicles</b> run at half their top speed. The advanced locomotive still breaking the bulk lever is accepted as the late-game reward.`,
  ];
  const smeltPills = [
    ...GRADES.map((g) => `<li>${g} ore, any rock form <b><span class="op-old">${pct(currentSmelt(CURRENT.minedForm[g]))} / ${pct(currentSmelt("chunk"))}</span> ${pct(S.byGrade[g])}</b></li>`),
    `<li>roasted sulfide rock <b>${GRADES.map((g) => pct(settledSmelt("roastedrock", g, S, true))).join(" / ")}</b> (new; unroasted 0 %)</li>`,
    `<li>concentrate, roasted concentrate, sponge <b>${pct(S.concentrate)}</b> (unchanged)</li>`,
    `<li>sulfide concentrate, unroasted <b>0 %</b> (unchanged)</li>`,
  ];
  const stackPills = [
    `<li>raw, crushed, ground, roasted rock <b>${S.stack.raw}</b> (rock; now raw 4, crushed and ground ${CURRENT.stack.crushed})</li>`,
    `<li>chunk <b><span class="op-old">${CURRENT.stack.chunk}</span> gone</b></li>`,
    `<li>concentrate, roasted concentrate, amalgam, sponge <b>${S.stack.concentrate}</b></li>`,
  ];
  return `<div class="ore-line ore-proposal">
${STYLE}
${OP_STYLE}
<h2>Settled: the furnace reads grade, not form</h2>
<p class="ol-intro">Two goals: ore processing that is interesting, dynamic and somewhat realistic, and logistics that make processing <b>at the mine</b> necessary. Today bulk sits on one item (raw ore stacks to 4, chunks and crushed ore to 16) and crushed ore smelts at a flat ${pct(CFG.smelt.crushed)} at any grade, which opens three escape hatches past the concentrator (section 5). The design the owner has settled follows the real logic, where dressing ore existed to throw the rock away before freight and smelting. Issues: ${issueLink(684)}, ${issueLink(765)}, ${issueLink(686)}, ${issueLink(688)}.</p>
<p class="op-rule"><b>The rule behind it:</b> a slot holds a fixed amount of rock (${S.stack.raw} ore's worth, whatever has been done to it), and grade is metal per item. So what a slot of rock is worth is set by the ore's grade alone, and only throwing the rock away, concentrating, makes a slot worth more.</p>
<ol class="op-settled" aria-label="The settled design">${settled.map((s) => `<li>${s}</li>`).join("")}</ol>
<p class="ol-intro"><b>What a furnace takes</b>, now (struck through: as mined / chunk or crushed) and settled:</p>
<ul class="op-pills" aria-label="Smelt shares">${smeltPills.join("")}</ul>
<p class="ol-intro"><b>Stack sizes</b> (settled; see the logistics formula):</p>
<ul class="op-pills" aria-label="Stack sizes">${stackPills.join("")}<li>metal units an ore, carried by every rock form: ${GRADES.map((g) => `${g} ${UNITS[g]}`).join(", ")} (hematite 20/25/30/40, silver quartz 10/15/20/25, cassiterite and gold quartz 5/10/15/20); concentrate and the other products ${UNITS_PER_PRODUCT}</li></ul>
<p class="ol-intro">The concentrator figures are ore-line.ts's <code>CFG</code> (main); the settled figures are <code>SETTLED</code> in <code>ore-proposal.ts</code>, and every number here is computed from them.</p>
<nav class="ol-jump" aria-label="Sections">${jump.map(([id, t], i) => `<a href="#op-${id}">${i + 1}. ${esc(t!)}</a>`).join("")}</nav>

<section class="ol-class" id="op-compare" aria-labelledby="op-compare-h"><h3 id="op-compare-h">1. Current against settled: the best recovery of each choice</h3>
<p class="ol-ores">Oxide ores, of the metal in the rock. For each grade and tier: smelt as mined, crush and smelt, or concentrate and smelt (classified or not; for poor ore, ground or not where the tier has a grinder). The winning choice is shaded; the last column says whether it changes.</p>
${GRADES.map(renderCompare).join("\n")}
<p class="ol-formula"><b>Formula:</b> as mined = smelt share of the mined form (now: raw 0, chunk ${pct(CFG.smelt.chunk)}; settled: the grade's share); crush and smelt = crushed's share (now ${pct(CFG.smelt.crushed)}; settled: the grade's share, crushing is 1 : 1); concentrate = base × ${CFG.unclassified} if unclassified × ${CFG.fineUnground} if poor and not ground, best device of the tier (hand rocker, water jig L2, mill table and vanner L4), density 1.</p>
</section>

<section class="ol-class" id="op-decision" aria-labelledby="op-decision-h"><h3 id="op-decision-h">2. The decision each grade faces</h3>
<p class="ol-ores">Carry it home and smelt it as it is, or concentrate it at the mine, at which tier.</p>
${renderDecisions()}
</section>

<section class="ol-class" id="op-logistics" aria-labelledby="op-logistics-h"><h3 id="op-logistics-h">3. Logistics: what a slot is worth, and what it takes to bring 1000 units home</h3>
<h4>Ingots one inventory slot is worth, smelted at that stage</h4>
<p class="ol-ores">The density of each form: a full slot of it, put through a furnace. Where now and settled differ the cell shows both. Amalgam does not smelt; its slot is worth what it retorts to.</p>
${renderSlots()}
<h4>Bringing ${1000} units of metal home (${1000 / UNITS_PER_INGOT} ingots)</h4>
<p class="ol-ores">By each route, from each grade: how much rock must be dug, what comes home and how many slots it fills. The gap between the rock routes and the concentrate routes is how hard the design pushes processing to the mine.</p>
${renderRoutes()}
</section>

<section class="ol-class" id="op-transport" aria-labelledby="op-transport-h"><h3 id="op-transport-h">4. Transport: can rock be hauled home?</h3>
${renderTransport()}
</section>

<section class="ol-class" id="op-problems" aria-labelledby="op-problems-h"><h3 id="op-problems-h">5. The three problems, before and after</h3>
${renderProblems()}
</section>

<section class="ol-class" id="op-special" aria-labelledby="op-special-h"><h3 id="op-special-h">6. Sulfides and free gold</h3>
${renderSpecial()}
</section>

<section class="ol-class" id="op-nudge" aria-labelledby="op-nudge-h"><h3 id="op-nudge-h">7. How the grade ladder could be nudged</h3>
<p class="ol-ores">The shares are settled; this is only where each one sits against the concentrators. Each row moves one grade's share five points and shows which way the decision goes at each tier, and by how many points.</p>
${renderNudges()}
</section>

<section class="ol-questions" id="op-open" aria-labelledby="op-open-h"><h3 id="op-open-h">8. Open questions and the code</h3>
<ul>${OPEN_QUESTIONS.map((q) => `<li>${linked(q)}</li>`).join("")}</ul>
<h4>The implementation</h4>
<ul class="op-code">${CODE_CHANGES.map((q) => `<li>${linked(q)}</li>`).join("")}</ul>
<h4>Issue #765</h4>
<p>${linked(ISSUE_765)}</p>
</section>
</div>`;
}
