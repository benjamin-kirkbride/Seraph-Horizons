// The ore review page's "Smelting, stacks and logistics" tab (scripts/standalone/ore-review.json):
// the settled design for what a furnace takes and what that does to bulk, broken down so it can be
// checked. Merged on main (#847, #863): every rock form (raw, crushed, ground, roasted rock) is one
// item to one raw ore, carries the ore's grade and units (3 / 5 / 7 / 10 for most ores), stacks to
// 16 and smelts at a share set by grade (10 / 25 / 40 / 50 %); there are no chunks and the quern
// takes no ore; concentrate holds 5 units, stacks to 128 and smelts whole. The decision each grade
// faces (carry it home, or concentrate at the mine, and on which line), what a slot is worth, what
// it takes to bring 1000 units home, the transport audit (ore-transport.ts) and the power and water
// budget (ore-power.ts). Every figure is computed from ore-line.ts's CFG and each table has its
// formula under it. Plain HTML and CSS on the processing line's styles, no script.
//
// The default export is the tab's HTML fragment (scripts/standalone-tabs.ts includes it).

import { CFG, full, FULL, GRADES, handRecovery, lineRecovery, platesLine, rockSmelt, STYLE, type Grade } from "./ore-line.ts";
import { renderPower } from "./ore-power.ts";
import { renderTransport } from "./ore-transport.ts";

export type { Grade };
export { GRADES };

/** Metal units a rock item holds by grade: most ores, and the exceptions. */
export const UNITS = CFG.units;
export const UNIT_EXCEPTIONS = CFG.unitExceptions;

export type Form = "raw" | "crushed" | "ground" | "roastedrock" | "concentrate" | "roastedconcentrate" | "amalgam" | "sponge";
export const FORMS: Form[] = ["raw", "crushed", "ground", "roastedrock", "concentrate", "roastedconcentrate", "amalgam", "sponge"];
export const FORM_LABEL: Record<Form, string> = {
  raw: "Raw ore",
  crushed: "Crushed ore",
  ground: "Ground ore",
  roastedrock: "Roasted rock",
  concentrate: "Concentrate",
  roastedconcentrate: "Roasted concentrate",
  amalgam: "Amalgam",
  sponge: "Sponge",
};
/** Forms that still carry their rock: one item to one raw ore, the ore's grade and units, stack 16, smelt by grade. */
export const ROCK_FORMS: Form[] = ["raw", "crushed", "ground", "roastedrock"];
export const isRock = (f: Form): boolean => ROCK_FORMS.includes(f);

/** The settled design's figures, from CFG. */
export interface Design {
  byGrade: Record<Grade, number>;
  concentrate: number;
  stack: Record<Form, number>;
  /** The roast a rock form gets: the firepit's (the hand roast takes raw and crushed sulfide rock), or the stall's. */
  rockRoast: number;
}
export const SETTLED: Design = {
  byGrade: CFG.smeltByGrade,
  concentrate: 1,
  stack: {
    raw: CFG.rockStack,
    crushed: CFG.rockStack,
    ground: CFG.rockStack,
    roastedrock: CFG.rockStack,
    concentrate: CFG.concentrateStack,
    roastedconcentrate: CFG.concentrateStack,
    amalgam: CFG.concentrateStack,
    sponge: CFG.concentrateStack,
  },
  rockRoast: CFG.roasters.firepit,
};
export const STACK = SETTLED.stack;
export const UNITS_PER_PRODUCT = CFG.concentrateUnits;
export const UNITS_PER_INGOT = CFG.unitsPerIngot;

// ---- what a furnace gives

/** The smelt share of a form at a grade: rock by grade (a sulfide's 0 unroasted, roasted rock × the roast), concentrate whole, unroasted sulfide concentrate 0. */
export function settledSmelt(form: Form, grade: Grade, d: Design = SETTLED, sulfide = false, roast = d.rockRoast): number {
  if (form === "amalgam") return 0;
  if (form === "roastedrock") return d.byGrade[grade] * roast;
  if (isRock(form)) return sulfide ? 0 : d.byGrade[grade];
  if (sulfide && form === "concentrate") return 0;
  return d.concentrate;
}

/** The best a rock form smelts at for a grade, carried home: the grade's share, or for a sulfide the roasted rock's. */
export const homeSmelt = (grade: Grade, d: Design = SETTLED, sulfide = false, roast = d.rockRoast) => Math.max(settledSmelt("raw", grade, d, sulfide), settledSmelt("roastedrock", grade, d, sulfide, roast));

// ---- the lines a grade can be put through, and the decision each grade faces

export interface LineOption {
  id: string;
  label: string;
  /** By hand, or machines. */
  hand: boolean;
  /** Of the metal in the rock (oxide feed, classified, ground where poor). */
  recovery: (poor: boolean) => number;
}

export const LINES: LineOption[] = [
  { id: "rocker", label: "Rocker, by hand", hand: true, recovery: (poor) => handRecovery("rocker", { poor }) },
  { id: "sluice", label: "Sluice alone", hand: false, recovery: () => lineRecovery(["sluice"]) },
  { id: "sluicejig", label: "Sluice + jig", hand: false, recovery: () => lineRecovery(["sluice", "jig"]) },
  { id: "full", label: "Full line", hand: false, recovery: () => full() },
];

export const lineOption = (id: string): LineOption => {
  const l = LINES.find((x) => x.id === id);
  if (!l) throw new Error(`no line ${id}`);
  return l;
};

/** The best concentrate figure a line gives a grade (poor is unground by hand: there is no hand grinder). */
export const bestConcentrate = (line: string, grade: Grade): number => lineOption(line).recovery(grade === "poor");

export type Verdict = "mine" | "home" | "tie";

/** Carry home and smelt, or concentrate at the mine on a line. Margin in points of recovery. */
export function decision(line: string, grade: Grade, o: { sulfide?: boolean } = {}): { home: number; mine: number; margin: number; verdict: Verdict } {
  const home = homeSmelt(grade, SETTLED, o.sulfide, o.sulfide ? CFG.roasters.stall : 1);
  const l = lineOption(line);
  const roast = o.sulfide ? (l.hand ? CFG.roasters.firepit : CFG.roasters.reverberatory2) : 1;
  const mine = bestConcentrate(line, grade) * roast;
  const margin = (mine - home) * 100;
  return { home, mine, margin, verdict: Math.abs(margin) < 1e-9 ? "tie" : margin > 0 ? "mine" : "home" };
}

// ---- logistics

/** Units of metal one item of a form holds, mined at a grade: rock forms carry the ore's units, products 5. */
export const unitsPerItem = (form: Form, grade: Grade): number => (isRock(form) ? UNITS[grade] : UNITS_PER_PRODUCT);
export const stackOf = (form: Form): number => STACK[form];

/** Metal units a full slot of a form gives smelted: stack × units an item × the smelt share. */
export function unitsPerSlot(form: Form, grade: Grade, sulfide = false): number {
  return stackOf(form) * unitsPerItem(form, grade) * settledSmelt(form, grade, SETTLED, sulfide);
}
export const ingotsPerSlot = (form: Form, grade: Grade, sulfide = false) => unitsPerSlot(form, grade, sulfide) / UNITS_PER_INGOT;

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

/** Bringing `target` units of metal home from a grade: as rock, or as concentrate from each line. */
export function routes(grade: Grade, target = 1000): Route[] {
  const carry = (label: string, form: Form, recovery: number): Route => {
    if (recovery <= 0) return { label, rock: Infinity, items: Infinity, slots: Infinity, form, recovery: 0 };
    const rock = target / recovery;
    const carried = isRock(form) ? rock : target;
    const items = carried / unitsPerItem(form, grade);
    return { label, rock, items, slots: items / stackOf(form), form, recovery };
  };
  return [
    carry("Carry the rock (raw, crushed or ground)", "raw", settledSmelt("raw", grade)),
    ...LINES.map((l) => carry(`Concentrate at the mine: ${l.label.toLowerCase()}`, "concentrate", l.recovery(grade === "poor"))),
  ];
}

// ---- rendering

const REPO = "https://github.com/benjamin-kirkbride/Seraph-Horizons";
const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const issueLink = (n: number) => `<a class="ol-issue" href="${REPO}/issues/${n}" target="_blank" rel="noopener noreferrer">#${n}</a>`;
const pct = (x: number, d = 1) => `${Math.round(x * 100 * 10 ** d) / 10 ** d} %`;
const pts = (x: number) => `${Math.round(Math.abs(x) * 10) / 10}`;
const num = (x: number, d = 1) => (Number.isFinite(x) ? `${Math.round(x * 10 ** d) / 10 ** d}` : "∞");
const cap = (s: string) => s[0]!.toUpperCase() + s.slice(1);

/** 1. The decision each grade faces: oxide ores. */
function renderDecisions(): string {
  const head = `<tr><th>Grade</th><th>As mined<span class="ol-when">raw, crushed or ground alike</span></th>${LINES.map((l) => `<th>${esc(l.label)}<span class="ol-when">${l.hand ? "poor stays unground" : "poor ground in the ball mill"}</span></th>`).join("")}</tr>`;
  const rows = GRADES.map((g) => {
    const cells = LINES.map((l) => {
      const d = decision(l.id, g);
      return `<td class="ol-c op-d-${d.verdict}${d.verdict === "mine" ? " op-win" : ""}"><span class="ol-f ol-f-best"><b>${pct(d.mine)}</b></span><span class="ol-cnote">${d.verdict === "tie" ? "a dead heat" : d.verdict === "mine" ? `mine wins by ${pts(d.margin)}` : `home wins by ${pts(-d.margin)}`}</span></td>`;
    });
    return `<tr><th scope="row"><b>${cap(g)}</b><span class="ol-when">${UNITS[g]} units an ore</span></th><td class="ol-c"><span class="ol-f ol-f-best"><b>${pct(rockSmelt(g))}</b></span></td>${cells.join("")}</tr>`;
  }).join("\n");
  const b = decision("rocker", "bountiful");
  return `<div class="ol-tablewrap"><table class="ol-table op-table"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> margin = the line's recovery (classified; poor ore ground where the line has a ball mill, never by hand: the quern takes no ore, so the rocker gets ${pct(handRecovery("rocker", { poor: true }))} % of poor ore) − the grade's share as mined, in points. Crushing and grinding change nothing in the furnace, so "smelt as mined", "crush and smelt" and "grind and smelt" are one figure. Bountiful by hand is ${b.verdict === "tie" ? `a dead heat: the rocker's ${pct(CFG.hand.rocker, 0)} % equals bountiful's share, so the rocker only pays unclassified, never` : b.verdict === "mine" ? `the rocker's by ${pts(b.margin)} points` : `carried home, by ${pts(-b.margin)} points`}; at the sluice and beyond every grade is concentrated at the mine.</p>`;
}

/** 2. Sulfides and free gold. */
function renderSpecial(): string {
  const sulfHead = `<tr><th>Grade</th><th>Roasted rock, carried home<span class="ol-when">stall ${pct(CFG.roasters.stall, 0)} · firepit ${pct(CFG.roasters.firepit, 0)}</span></th>${LINES.map((l) => `<th>${esc(l.label)}<span class="ol-when">${l.hand ? `× firepit ${pct(CFG.roasters.firepit, 0)}` : `× reverberatory level 2 ${pct(CFG.roasters.reverberatory2, 0)}`}</span></th>`).join("")}</tr>`;
  const sulfRows = GRADES.map((g) => {
    const cells = LINES.map((l) => {
      const d = decision(l.id, g, { sulfide: true });
      return `<td class="ol-c op-d-${d.verdict}${d.verdict === "mine" ? " op-win" : ""}"><span class="ol-f ol-f-best"><b>${pct(d.mine)}</b></span><span class="ol-cnote">${d.verdict === "tie" ? "a dead heat" : d.verdict === "mine" ? `mine wins by ${pts(d.margin)}` : `home wins by ${pts(-d.margin)}`}</span></td>`;
    });
    return `<tr><th scope="row"><b>${cap(g)}</b></th><td class="ol-c"><span class="ol-f ol-f-best"><b>${pct(rockSmelt(g, CFG.roasters.stall))}</b></span><span class="ol-cnote">firepit ${pct(rockSmelt(g, CFG.roasters.firepit))}; unroasted 0 %</span></td>${cells.join("")}</tr>`;
  }).join("\n");
  const goldHead = `<tr><th>Grade</th><th>As mined</th><th>Rocker, by hand<span class="ol-when">with the amalgam pan · without</span></th><th>Full line<span class="ol-when">plates level 2 · level 1 · no plates</span></th></tr>`;
  const goldRows = GRADES.map((g) => {
    const poor = g === "poor";
    const withPan = handRecovery("rocker", { free: true, amalgamated: true, poor });
    const without = handRecovery("rocker", { free: true, poor });
    return `<tr><th scope="row"><b>${cap(g)}</b></th><td class="ol-c"><span class="ol-f ol-f-best"><b>${pct(rockSmelt(g))}</b></span></td><td class="ol-c${withPan > rockSmelt(g) ? " op-win" : ""}"><span class="ol-f ol-f-best"><b>${pct(withPan)}</b></span><span class="ol-cnote">without ${pct(without)}</span></td><td class="ol-c op-win"><span class="ol-f ol-f-best"><b>${pct(platesLine(2, FULL))}</b></span><span class="ol-cnote">level 1 ${pct(platesLine(1, FULL))}; no plates ${pct(lineRecovery(FULL, { free: true }))}</span></td></tr>`;
  }).join("\n");
  return `<h4>Sulfides (galena, chalcopyrite, sphalerite…)</h4>
<p class="ol-lede">Unroasted sulfide rock does not smelt, and neither does unroasted sulfide concentrate. Carried home, the rock is roasted in a stall (${pct(CFG.roasters.stall, 0)}) or the firepit (${pct(CFG.roasters.firepit, 0)}) and smelts at its grade's share times that. At the mine, the hand line's concentrate is roasted in the firepit and a machine line's in the reverberatory roaster (level 2, ${pct(CFG.roasters.reverberatory2, 0)}; level 1 ${pct(CFG.roasters.reverberatory1, 0)}).</p>
<div class="ol-tablewrap"><table class="ol-table op-table"><thead>${sulfHead}</thead><tbody>${sulfRows}</tbody></table></div>
<h4>Free gold and silver (gold quartz, silver quartz)</h4>
<p class="ol-lede">Free metal floats past a gravity concentrator unless amalgamated (× ${CFG.locked.freeUnamalgamated}); amalgamated, the sponge smelts whole after retorting. As mined, gold quartz smelts by grade like any rock (its gold at ${pct(CFG.goldUnparted, 0)} unparted, and no silver: the by-product ores on the processing line). The plates' figures do not depend on grade; poor quartz by hand is unground.</p>
<div class="ol-tablewrap"><table class="ol-table op-table"><thead>${goldHead}</thead><tbody>${goldRows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> carried home = the grade's share (a sulfide: × the roaster's figure, 0 unroasted); the line = the chain's recovery of the catchable share (${CFG.locked.poorUnground} if poor and not ground, ${CFG.locked.freeUnamalgamated} if free and not amalgamated), × the roaster for sulfides; the plates: caught + (1 − caught) × pass × chain. Green: the better of carrying home and processing at the mine.</p>`;
}

/** 3a. Ingots a slot is worth, per form and grade. */
function renderSlots(): string {
  const head = `<tr><th>Form</th><th>Stack</th><th>Units an item</th>${GRADES.map((g) => `<th>${cap(g)}<span class="ol-when">${UNITS[g]} units</span></th>`).join("")}</tr>`;
  const rows = FORMS.map((f) => {
    const cells = GRADES.map((g) => {
      if (f === "amalgam") return `<td class="ol-c ol-c-none">retort it: ${num(ingotsPerSlot("sponge", g), 2)} as sponge</td>`;
      const sulfide = f === "roastedrock";
      const v = ingotsPerSlot(f, g, sulfide);
      return `<td class="ol-c${v === 0 ? " ol-c-zero" : ""}"><span class="ol-f"><b>${num(v, 2)}</b> ingots</span><span class="ol-cnote">${num(unitsPerSlot(f, g, sulfide))} units</span></td>`;
    });
    const note = f === "roastedrock" ? '<span class="ol-when">sulfide, firepit; carries its rock</span>' : isRock(f) ? '<span class="ol-when">carries its rock</span>' : "";
    return `<tr><th scope="row"><b>${esc(FORM_LABEL[f])}</b>${note}</th><td class="ol-c"><b>${stackOf(f)}</b></td><td class="ol-c">${isRock(f) ? GRADES.map((g) => UNITS[g]).join(" / ") : UNITS_PER_PRODUCT}</td>${cells.join("")}</tr>`;
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-slots"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> ingots a slot = stack size × metal units an item × that form's smelt share ÷ ${UNITS_PER_INGOT} units an ingot. Every rock form is one item to one raw ore with the ore's units and stacks to ${STACK.raw}, so a slot of crushed or ground ore is worth exactly a slot of raw ore (${GRADES.map((g) => `${g} ${num(ingotsPerSlot("raw", g), 2)}`).join(", ")} ingots): crushing and grinding change nothing in bulk. Roasted rock is sulfide ore roasted in the firepit: its grade's share × ${SETTLED.rockRoast} (in a stall × ${CFG.roasters.stall}). A concentrate, roasted concentrate or sponge item holds ${UNITS_PER_PRODUCT} units whatever the ore's grade and whichever line made it, so its slot is worth the same ${num(ingotsPerSlot("concentrate", "poor"))} ingots everywhere; concentrating is the one step where bulk drops, and the recovery up to it decides how much ore it took to fill the slot, which the next table shows. Most ores' units; hematite (${GRADES.map((g) => UNIT_EXCEPTIONS.hematite![g]).join(" / ")}) is worth a little more a slot, silver quartz (${GRADES.map((g) => UNIT_EXCEPTIONS["silver quartz"]![g]).join(" / ")}), cassiterite and gold quartz (${GRADES.map((g) => UNIT_EXCEPTIONS.cassiterite![g]).join(" / ")}) less.</p>`;
}

/** 3b. Bringing 1000 units home by each route. */
function renderRoutes(): string {
  const target = 1000;
  const head = `<tr><th>Grade</th><th>Route</th><th>Ore mined<span class="ol-when">units in the rock</span></th><th>Carried as</th><th>Items</th><th>Slots</th></tr>`;
  const rows = GRADES.flatMap((g) => {
    const rs = routes(g, target);
    return rs.map(
      (r, i) =>
        `<tr>${i === 0 ? `<th scope="row" rowspan="${rs.length}" class="op-tier"><b>${cap(g)}</b><span class="ol-when">${UNITS[g]} units an ore</span></th>` : ""}<td>${esc(r.label)}</td><td>${num(r.rock, 0)}<span class="ol-cnote">at ${pct(r.recovery)}</span></td><td>${esc(FORM_LABEL[r.form])} <span class="ol-cnote">stack ${stackOf(r.form)}</span></td><td class="ol-c"><b>${num(r.items, 0)}</b></td><td class="ol-c op-slotcell"><b>${num(r.slots)}</b></td></tr>`,
    );
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-routes"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> ore mined = ${target} ÷ the route's recovery; items = the units carried ÷ units an item (rock forms carry the whole rock, one item an ore mined; concentrate only the ${target} recovered units at ${UNITS_PER_PRODUCT} each); slots = items ÷ stack. Carrying crushed or ground is the same haul as carrying raw, so the only reason to crush at the mine is to feed a concentrator. Concentrate always comes home as ${num(target / UNITS_PER_PRODUCT / STACK.concentrate, 2)} slots; the line only changes how much rock is dug. Most ores' units.</p>`;
}

const OP_STYLE = `<style>
.ore-proposal .op-pills { display: flex; flex-wrap: wrap; gap: 0.35rem; margin: 0.3rem 0 0.6rem; padding: 0; list-style: none; font-size: 0.85rem; }
.ore-proposal .op-pills li { background: var(--surface); border: 1px solid var(--border); border-radius: 999px; padding: 0.1rem 0.6rem; }
.ore-proposal .op-pills li b { color: var(--accent); }
.ore-proposal .op-settled { margin: 0.3rem 0 0.6rem; padding-left: 1.2rem; font-size: 0.9rem; }
.ore-proposal .op-settled li { margin: 0.15rem 0; }
.op-win { background: color-mix(in srgb, var(--accent) 14%, transparent); }
.op-win .ol-f-best b { color: var(--accent); }
.op-table td.ol-c .ol-f b { color: var(--text); font-size: 0.95rem; }
.op-table td.op-win .ol-f-best b { color: var(--accent); font-size: 1rem; }
.op-table td.op-d-home .ol-f-best b { color: var(--series-2); }
.op-tier { white-space: nowrap; }
.op-slots .ol-c .ol-f { white-space: normal; }
@media (max-width: 640px) {
  .op-table { min-width: 36rem; }
  .op-slots { min-width: 44rem; }
}
</style>`;

export default function oreProposal(): string {
  const jump = [
    ["decision", "The decision each grade faces"],
    ["special", "Sulfides and free gold"],
    ["logistics", "Logistics: slots and hauls"],
    ["transport", "Transport: can rock be hauled home?"],
    ["power", "Power and water"],
  ];
  const S = SETTLED;
  const settled = [
    `<b>Rock forms are the pack's own items</b>: raw ore, crushed ore, ground ore and roasted rock (raw or crushed sulfide ore roasted in the firepit or a stall). Each stacks to <b>${S.stack.raw}</b> and converts <b>1 : 1</b>, one raw ore to one crushed to one ground, carrying the ore's grade and units unchanged: crushing and grinding are lossless and never change bulk (${issueLink(847)}, ${issueLink(863)}).`,
    `<b>No chunks.</b> Rich and bountiful drop raw ore at ${S.stack.raw} a stack like every grade.`,
    `<b>Rock smelts by grade</b>, raw, crushed or ground alike: ${GRADES.map((g) => `${g} <b>${pct(S.byGrade[g], 0)}</b>`).join(", ")}.`,
    `<b>Units an ore</b>: ${GRADES.map((g) => `${g} ${UNITS[g]}`).join(", ")} for most ores (hematite ${GRADES.map((g) => UNIT_EXCEPTIONS.hematite![g]).join("/")}, silver quartz ${GRADES.map((g) => UNIT_EXCEPTIONS["silver quartz"]![g]).join("/")}, cassiterite and gold quartz ${GRADES.map((g) => UNIT_EXCEPTIONS.cassiterite![g]).join("/")}).`,
    `<b>Sulfides.</b> Unroasted sulfide rock does not smelt, nor unroasted sulfide concentrate. Roasted rock smelts at its grade's share × the roaster (firepit ${S.rockRoast}, stall ${CFG.roasters.stall}); roasted concentrate smelts whole (reverberatory ${pct(CFG.roasters.reverberatory1, 0)} and ${pct(CFG.roasters.reverberatory2, 0)}, firepit ${pct(CFG.roasters.firepit, 0)}).`,
    `<b>Concentrate</b>: ${UNITS_PER_PRODUCT} units an item, <b>${S.stack.concentrate}</b> a stack (${num(ingotsPerSlot("concentrate", "poor"))} ingots a slot at any grade), smelting at ${pct(S.concentrate, 0)}. Concentrating is the one step where bulk drops. Roasted concentrate, amalgam and sponge stay at ${S.stack.concentrate}.`,
    `<b>The quern takes no ore.</b> There is no hand grinding; grinding starts with the arrastra and the ball mill. Poor ore by hand stays at ${pct(handRecovery("rocker", { poor: true }))}.`,
    `<b>Spalling</b> with a hammer is the hand crush.`,
    `<b>Yang's vehicles</b> run at half their top speed. The advanced locomotive still breaking the bulk lever is accepted as the late-game reward.`,
  ];
  const smeltPills = [
    ...GRADES.map((g) => `<li>${g} rock, any form <b>${pct(S.byGrade[g], 0)}</b></li>`),
    `<li>roasted sulfide rock, firepit <b>${GRADES.map((g) => pct(settledSmelt("roastedrock", g, S, true), 1)).join(" / ")}</b>; stall <b>${GRADES.map((g) => pct(settledSmelt("roastedrock", g, S, true, CFG.roasters.stall), 1)).join(" / ")}</b></li>`,
    `<li>concentrate, roasted concentrate, sponge <b>${pct(S.concentrate, 0)}</b></li>`,
    `<li>sulfide rock or concentrate, unroasted <b>0 %</b></li>`,
  ];
  return `<div class="ore-line ore-proposal">
${STYLE}
${OP_STYLE}
<h2>Smelting, stacks and logistics</h2>
<p class="ol-intro">Two goals: ore processing that is interesting, dynamic and somewhat realistic, and logistics that make processing <b>at the mine</b> necessary. The design follows the real logic, where dressing ore existed to throw the rock away before freight and smelting. Merged on main: ${issueLink(847)} (rock smelts by grade, no chunks) and ${issueLink(863)} (rock stacks to 16, units 3/5/7/10); the machines are the settled specification on the processing line tab. Issues: ${issueLink(684)}, ${issueLink(765)}.</p>
<p class="ol-rule"><b>The rule behind it:</b> a slot holds a fixed amount of rock (${S.stack.raw} ore's worth, whatever has been done to it), and grade is metal per item. So what a slot of rock is worth is set by the ore's grade alone, and only throwing the rock away, concentrating, makes a slot worth more.</p>
<ol class="op-settled" aria-label="The settled design">${settled.map((s) => `<li>${s}</li>`).join("")}</ol>
<p class="ol-intro"><b>What a furnace takes</b> (share of the item's units):</p>
<ul class="op-pills" aria-label="Smelt shares">${smeltPills.join("")}</ul>
<p class="ol-intro"><b>Stack sizes</b>: rock forms <b>${S.stack.raw}</b>; concentrate, roasted concentrate, amalgam and sponge <b>${S.stack.concentrate}</b>. Every number here is computed from ore-line.ts's <code>CFG</code> (main's <code>ore-processing.json</code> and the machine spec).</p>
<nav class="ol-jump" aria-label="Sections">${jump.map(([id, t], i) => `<a href="#op-${id}">${i + 1}. ${esc(t!)}</a>`).join("")}</nav>

<section class="ol-class" id="op-decision" aria-labelledby="op-decision-h"><h3 id="op-decision-h">1. The decision each grade faces</h3>
<p class="ol-ores">Oxide ores, of the metal in the rock: carry it home and smelt it as it is, or concentrate it at the mine, and on which line. The winning side is shaded.</p>
${renderDecisions()}
</section>

<section class="ol-class" id="op-special" aria-labelledby="op-special-h"><h3 id="op-special-h">2. Sulfides and free gold</h3>
${renderSpecial()}
</section>

<section class="ol-class" id="op-logistics" aria-labelledby="op-logistics-h"><h3 id="op-logistics-h">3. Logistics: what a slot is worth, and what it takes to bring 1000 units home</h3>
<h4>Ingots one inventory slot is worth, smelted at that stage</h4>
<p class="ol-ores">The density of each form: a full slot of it, put through a furnace. Amalgam does not smelt; its slot is worth what it retorts to.</p>
${renderSlots()}
<h4>Bringing ${1000} units of metal home (${1000 / UNITS_PER_INGOT} ingots)</h4>
<p class="ol-ores">By each route, from each grade: how much rock must be dug, what comes home and how many slots it fills. The gap between the rock route and the concentrate routes is how hard the design pushes processing to the mine.</p>
${renderRoutes()}
</section>

<section class="ol-class" id="op-transport" aria-labelledby="op-transport-h"><h3 id="op-transport-h">4. Transport: can rock be hauled home?</h3>
${renderTransport()}
</section>

<section class="ol-class" id="op-power" aria-labelledby="op-power-h"><h3 id="op-power-h">5. Power and water</h3>
${renderPower()}
</section>
</div>`;
}
