// The "Power and water" section of the smelting, stacks and logistics tab (ore-proposal.ts renders
// it), from the spec's "Power and water budget". How mechanical power works in the game (a load is a
// constant resistance; a source's torque falls with speed; the shaft settles where they balance;
// work is in turns, so rate is in proportion to speed, with a minimum speed and no cap); the three
// example setups A, B and C with the line's speed and rate under each; the hard limits (stall,
// minimum speed, water); each machine's load and water; and fuel per ingot. There is no design
// plant: any power runs a line at its own pace. Every figure is computed here from POWER and DRAW,
// which mirror the spec (its figures are from the decompiled game, ppex and Millwright).

import { CFG, full, machine } from "./ore-line.ts";

// ---- the game's mechanical power, as the spec confirms it in the code

export interface Engine {
  name: string;
  /** Steam drawn, L/s, whatever the load. */
  steam: number;
  /** ppex's power figure. */
  power: number;
  /** MpPowerBudget = power × 2: torque = B / max(s, 0.25). */
  B: number;
  /** ShaftSpeed = (0.5 + power) × π / 5: torque tapers to 0 between ⅔ S and S. */
  S: number;
  cutIn: number;
  burst: number;
  /** Condensate out, L/s. */
  condensate: number;
}

const engine = (name: string, steam: number, power: number, cutIn: number, burst: number, condensate: number): Engine => ({
  name,
  steam,
  power,
  B: power * 2,
  S: ((0.5 + power) * Math.PI) / 5,
  cutIn,
  burst,
  condensate,
});

export const POWER = {
  /** The shaft speed the spec's rates are stated at. */
  referenceSpeed: CFG.referenceSpeed,
  /** Speed 1 is 5 rad/s: 95.5 turns an in-game hour (2 real minutes). */
  turnsPerHourPerSpeed: 95.5,
  hourSeconds: 120,
  /** A machine stops under this (MinSpeed, a config value). */
  minSpeed: 0.05,
  /** Below this speed an engine's torque is floored: B / 0.25, so it stalls past a load of 4 B. */
  engineFloor: 0.25,
  engines: {
    watt: engine("Watt", 30, 0.3, 2, 4, 1.0),
    low: engine("Cornish low", 8, 0.2, 5, 8, 0.3),
    normal: engine("Cornish normal", 16, 0.4, 6, 8, 0.6),
    high: engine("Cornish high", 32, 0.8, 7, 8, 1.2),
  },
  boilers: {
    lancashire: { name: "Lancashire boiler", steam: 48, cap: 12, coalPerDay: 12 },
    cornish: { name: "Cornish boiler", steam: 32, cap: 5, coalPerDay: 12 },
  },
  /** Water to steam: 16 L of steam a litre; a boiler takes steam drawn ÷ 16. */
  steamExpansion: 16,
  /** ppex's mechanical pump: 20 L/s a unit of shaft speed, at a fixed load, delivering at 1.5 atm. */
  mpPump: { litresPerSpeed: 20, load: 0.125, atm: 1.5 },
  /** Rotors: torque = (c − s) × F, so one against a load R settles at s = c − R / F and stalls past c × F. */
  rotors: {
    windmill: { name: "Vanilla windmill, 5 metal sails, full wind", c: 0.6, F: (5 / 4) * 1.25 },
    millwright: { name: "Millwright double rotor of 5, wind 0.24", c: Math.min(0.6 * 4, 0.24 * 4), F: (5 * 4) / 4 },
    waterwheel: { name: "Water wheel, flow 1", c: 0.3, F: 1 },
    handcrank: { name: "Hand crank, one player", c: 0.5, F: 0.5 },
    handcrank4: { name: "Hand crank, four players", c: 0.5, F: 2 },
  },
  /** A line at full rate takes a battery's 6 rock an hour, 24 hours a day. */
  rockPerDay: 6 * 24,
} as const;

/** A load's share of one battery's line, and its figures (the spec's "Per machine" table). */
export interface Draw {
  id: string;
  /** Resistance working and empty. */
  load: number;
  empty: number;
  /** Litres a rock (or a concentrate, or a 10 units). */
  water: number;
  waterUnit: "rock" | "concentrate" | "10 units";
  /** Items an hour at the reference speed, or null for a batch machine with a charge time. */
  rate: number | null;
  rateNote: string;
  /** How many of it one battery's line has (a quarter: it serves four). */
  onLine: number;
  /** Works shaft, not the line's. */
  works?: boolean;
}

export const DRAW: Draw[] = [
  { id: "jawbreaker", load: 0.8, empty: 0.16, water: 0, waterUnit: "rock", rate: 24, rateNote: "24 rock/h", onLine: 0.25 },
  { id: "battery", load: 0.75, empty: 0.15, water: 40, waterUnit: "rock", rate: 6, rateNote: "6 rock/h; 7 with the feeder", onLine: 1 },
  { id: "arrastra", load: 0.1, empty: 0.02, water: 10, waterUnit: "rock", rate: 2 / 7.5, rateNote: "2 rock in 7.5 h", onLine: 0 },
  { id: "ballmill", load: 1.1, empty: 0.22, water: 25, waterUnit: "rock", rate: 6, rateNote: "6 rock/h", onLine: 1 },
  { id: "plates", load: 0, empty: 0, water: 5, waterUnit: "rock", rate: 8, rateNote: "8 rock/h", onLine: 1 },
  { id: "sluice", load: 0, empty: 0, water: 10, waterUnit: "rock", rate: 16, rateNote: "16 rock/h", onLine: 0.5 },
  { id: "jig", load: 0.1, empty: 0.02, water: 70, waterUnit: "rock", rate: 8, rateNote: "8 rock/h", onLine: 1 },
  { id: "table", load: 0.1, empty: 0.02, water: 20, waterUnit: "rock", rate: 7, rateNote: "7 rock/h", onLine: 1 },
  { id: "vanner", load: 0.08, empty: 0.016, water: 15, waterUnit: "rock", rate: 3, rateNote: "3 rock/h", onLine: 2 },
  { id: "stall", load: 0, empty: 0, water: 0, waterUnit: "rock", rate: 0.8, rateNote: "32 rock in 40 h", onLine: 0 },
  { id: "reverberatory", load: 0.5, empty: 0.1, water: 2, waterUnit: "concentrate", rate: 76, rateNote: "level 2, 76 concentrate/h (level 1 by hand, 28)", onLine: 0, works: true },
  { id: "cupellation", load: 0.2, empty: 0.04, water: 0, waterUnit: "rock", rate: null, rateNote: "55 units/h", onLine: 0, works: true },
  { id: "parkes", load: 0.2, empty: 0.04, water: 0, waterUnit: "rock", rate: null, rateNote: "2,400 units in 5 h", onLine: 0, works: true },
  { id: "liquation", load: 0, empty: 0, water: 0, waterUnit: "rock", rate: null, rateNote: "100 units/h", onLine: 0 },
  { id: "acid", load: 0, empty: 0, water: 1, waterUnit: "10 units", rate: null, rateNote: "10 units/h", onLine: 0 },
];

export const draw = (id: string): Draw => {
  const d = DRAW.find((x) => x.id === id);
  if (!d) throw new Error(`no draw ${id}`);
  return d;
};

/** Litres a second a machine takes at its rate at the reference speed. */
export const waterAtRate = (d: Draw): number => (d.rate == null ? 0 : (d.water * d.rate) / POWER.hourSeconds);

/**
 * One battery's line: the breaker's quarter (working a quarter of the time, empty the rest: it
 * averages 0.32), the battery, the plates, the sluice's half, the jig, the table, two vanners, the
 * ball mill (empty on middlings, working on poor ore) and the MP pump.
 */
export function lineLoad(poor = false): number {
  let r = POWER.mpPump.load;
  for (const d of DRAW) {
    if (d.onLine === 0) continue;
    if (d.id === "ballmill" && !poor) r += d.empty;
    else if (d.onLine < 1) r += d.onLine * d.load + (1 - d.onLine) * d.empty;
    else r += d.onLine * d.load;
  }
  return r;
}

/** Litres of water a rock passing down one battery's line takes (every machine it goes through; the ball mill on poor ore). */
export function lineWaterPerRock(poor = false): number {
  let w = 0;
  for (const d of DRAW) {
    if (d.onLine === 0 || d.waterUnit !== "rock") continue;
    if (d.id === "ballmill" && !poor) continue;
    w += d.water;
  }
  return w;
}

/** The works' machines (the reverberatory at level 2, the cupellation blast, the Parkes stirrer), busy. */
export const worksLoad = (): number => DRAW.filter((d) => d.works).reduce((a, d) => a + d.load, 0);

/** An engine's torque at a shaft speed. */
export function engineTorque(e: Engine, s: number): number {
  const taper = Math.max(0, Math.min(1, (e.S - s) / (e.S / 3)));
  return (e.B / Math.max(s, POWER.engineFloor)) * taper;
}

/** Where a shaft settles under a constant load, driven by these engines together: 0 if they stall. */
export function engineSpeed(engines: Engine[], load: number): number {
  const torque = (s: number) => engines.reduce((a, e) => a + engineTorque(e, s), 0);
  if (torque(1e-6) < load) return 0;
  let lo = 1e-6;
  let hi = Math.max(...engines.map((e) => e.S));
  for (let i = 0; i < 60; i++) {
    const mid = (lo + hi) / 2;
    if (torque(mid) >= load) lo = mid;
    else hi = mid;
  }
  return lo;
}

/** Where a rotor (windmill, water wheel, hand crank) settles under a load: c − R / F, 0 past its stall. */
export const rotorSpeed = (r: { c: number; F: number }, load: number): number => Math.max(0, r.c - load / r.F);

/** The rate a shaft speed gives, as a share of the spec's (uncapped); 0 under the minimum speed. */
export const rateAt = (s: number): number => (s < POWER.minSpeed ? 0 : s / POWER.referenceSpeed);

/** The MP pump's delivery on a shaft. */
export const pumped = (s: number): number => POWER.mpPump.litresPerSpeed * s;

export interface Setup {
  id: "A" | "B" | "C" | "N3";
  name: string;
  plant: string;
  boiler: keyof typeof POWER.boilers;
  /** The engines on the line's shaft. */
  line: Engine[];
  /** The engines elsewhere (a works shaft or a fluid pump). */
  spare: Engine[];
  /** Extra steam from feed water over 1 atm, L/s. */
  boost: number;
  note: string;
}

const E = POWER.engines;
export const SETUPS: Setup[] = [
  {
    id: "A",
    name: "A, early",
    plant: "Cornish boiler, Watt engine and generator, bronze relief valve at 3.5 atm venting to the air",
    boiler: "cornish",
    line: [E.watt],
    spare: [],
    boost: 1,
    note: "Holds: the valve spills 2 to 3 L/s. The one plant a Cornish boiler can run.",
  },
  {
    id: "B",
    name: "B, mixed",
    plant: "Lancashire boiler, Cornish normal and Watt on one shaft",
    boiler: "lancashire",
    line: [E.normal, E.watt],
    spare: [],
    boost: 1.5,
    note: "Cannot be built in the pack as it stands: a Watt and a Cornish engine cannot share a steam run, and no valve guards a run at 6 to 8 atm (the Watt bursts in about 3 minutes, the normal in 5, the boiler in 6). With an 8 atm valve (open question) it is two runs. Costs C's coal for 0.7 of its power.",
  },
  {
    id: "C",
    name: "C, full",
    plant: "Lancashire boiler, Cornish high and generator on the line's shaft, Cornish normal on a works shaft (or its fluid pump)",
    boiler: "lancashire",
    line: [E.high],
    spare: [E.normal],
    boost: 0,
    note: "Parks at the high's cut-in, 7.0 to 7.3 atm: holds while the boiler is fed at 1 atm (through a bronze valve set at 1, so no boost; fed at 1.5 atm it gains 1.5 L/s and bursts both engines at 14 minutes). The steam run is steel. An exact 48 of 48 balance: with either engine broken the other bursts in about two minutes.",
  },
  {
    id: "N3",
    name: "Three normals",
    plant: "Lancashire boiler, three Cornish normals on one shaft",
    boiler: "lancashire",
    line: [E.normal, E.normal, E.normal],
    spare: [],
    boost: 0,
    note: "Holds (parks at 6). The normal's cap is 0.57, so the line turns slower than under C: C without the spare.",
  },
];

export interface SetupResult {
  speed: number;
  rate: number;
  rockPerHour: number;
  pumped: number;
  lineWater: number;
  boilerWater: number;
  margin: number;
  stalls: boolean;
}

/** The line under a setup: speed, rate, and the water balance with one MP pump on the line's shaft. */
export function run(setup: Setup, poor = false): SetupResult {
  const load = lineLoad(poor);
  const speed = engineSpeed(setup.line, load);
  const rate = rateAt(speed);
  const rockPerHour = 6 * rate;
  const lineWater = (lineWaterPerRock(poor) * rockPerHour) / POWER.hourSeconds;
  const boilerWater = POWER.boilers[setup.boiler].steam / POWER.steamExpansion;
  const p = pumped(speed);
  return { speed, rate, rockPerHour, pumped: p, lineWater, boilerWater, margin: p - lineWater - boilerWater, stalls: speed === 0 };
}

/** Steam drawn by every engine of a setup against the boiler's make (with the boost). */
export const steamBalance = (s: Setup) => ({ made: POWER.boilers[s.boiler].steam + s.boost, used: [...s.line, ...s.spare].reduce((a, e) => a + e.steam, 0) });

/** Coal an ingot and ingots a day for a line at a rate, on an ore with `units` a rock, through the full line. */
export function fuel(rate: number, units: number): { ingotsPerDay: number; coalPerIngot: number } {
  const ingotsPerDay = (POWER.rockPerDay * rate * units * full()) / CFG.unitsPerIngot;
  return { ingotsPerDay, coalPerIngot: ingotsPerDay > 0 ? POWER.boilers.lancashire.coalPerDay / ingotsPerDay : Infinity };
}

// ---- rendering

const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]!);
const num = (x: number, d = 1) => (Number.isFinite(x) ? `${Math.round(x * 10 ** d) / 10 ** d}` : "∞");
const pct = (x: number, d = 0) => `${Math.round(x * 100 * 10 ** d) / 10 ** d} %`;
const cardLink = (id: string) => `<a class="ol-mlink" href="#ol-m-${id}">${esc(machine(id).name)}</a>`;

function renderSetups(): string {
  const head = `<tr><th>Setup</th><th>Steam<span class="ol-when">made (+ boost) / used, L/s</span></th><th>Power<span class="ol-when">on the line (works)</span></th><th>Line's shaft<span class="ol-when">not poor · poor ore (ball mill working)</span></th><th>Rate<span class="ol-when">rock an hour</span></th><th>Water, one MP pump<span class="ol-when">pumped against line + boiler</span></th></tr>`;
  const rows = SETUPS.map((s) => {
    const n = run(s, false);
    const p = run(s, true);
    const st = steamBalance(s);
    const cell = (r: SetupResult) => (r.stalls ? `<b class="op-stall">stalls</b>` : `<b>${num(r.speed, 2)}</b> (${pct(r.rate)})`);
    const water = (r: SetupResult) => (r.stalls ? "—" : `${num(r.pumped)} against ${num(r.lineWater)} + ${num(r.boilerWater)}: <b class="${r.margin < 0 ? "op-stall" : ""}">${r.margin < 0 ? `short ${num(-r.margin)}` : `margin ${num(r.margin)}`}</b>`);
    return `<tr class="op-setup-${s.id}"><th scope="row"><b>${esc(s.name)}</b><span class="ol-when">${esc(s.plant)}</span></th><td class="ol-c"><span class="ol-f"><b>${st.made}</b> / ${st.used}</span><span class="ol-cnote">${POWER.boilers[s.boiler].name}, ${POWER.boilers[s.boiler].coalPerDay} coal a day</span></td><td class="ol-c"><span class="ol-f"><b>${num(s.line.reduce((a, e) => a + e.power, 0), 1)}</b>${s.spare.length ? ` (${num(s.spare.reduce((a, e) => a + e.power, 0), 1)})` : ""}</span><span class="ol-cnote">B ${s.line.map((e) => e.B).join(" + ")}</span></td><td class="ol-c"><span class="ol-f">${cell(n)} · ${cell(p)}</span></td><td class="ol-c"><span class="ol-f"><b>${n.stalls ? "—" : num(n.rockPerHour)}</b> · ${p.stalls ? "—" : num(p.rockPerHour)}</span></td><td class="ol-c"><span class="ol-f">${water(n)}</span><span class="ol-cnote">poor: ${water(p)}</span></td></tr>`;
  }).join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-setups"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<ul class="ol-notes">${SETUPS.map((s) => `<li><b>${esc(s.name)}:</b> ${esc(s.note)}</li>`).join("")}</ul>
<p class="ol-formula"><b>Formula:</b> an engine's torque = B ÷ max(speed, ${POWER.engineFloor}), tapering to nothing between ⅔ S and S (B = 2 × power; S = (0.5 + power) × π ÷ 5: Watt ${num(E.watt.S, 2)}, low ${num(E.low.S, 2)}, normal ${num(E.normal.S, 2)}, high ${num(E.high.S, 2)}); engines on one shaft add their torques; the shaft settles where the torque equals the line's load, ${num(lineLoad(), 2)} on ore that is not poor and ${num(lineLoad(true), 2)} on poor ore with the ball mill working (the per-machine table). Rate = speed ÷ ${POWER.referenceSpeed}, uncapped; rock an hour = 6 × rate. Water: the MP pump on the line's shaft gives ${POWER.mpPump.litresPerSpeed} × speed L/s; the line takes ${lineWaterPerRock()} L a rock (${lineWaterPerRock(true)} on poor ore) at its rate; the boiler takes its steam ÷ ${POWER.steamExpansion} (Lancashire ${num(POWER.boilers.lancashire.steam / POWER.steamExpansion)}, Cornish ${num(POWER.boilers.cornish.steam / POWER.steamExpansion)} L/s), the engines' condensate (up to ${num(E.high.condensate + E.normal.condensate)} L/s on C) a bonus that cannot be counted on. Where C is short on poor ore, the spec's fix is a second MP pump on the works shaft feeding the boiler alone.</p>`;
}

function renderSources(): string {
  const loads: [string, number][] = [
    ["a jig, table or arrastra (0.1)", 0.1],
    ["the MP pump (0.125)", POWER.mpPump.load],
    ["the battery and its breaker share (1.07)", draw("battery").load + draw("jawbreaker").load * 0.25],
    ["the ball mill (1.1)", draw("ballmill").load],
    [`one battery's line (${num(lineLoad(), 2)})`, lineLoad()],
  ];
  const head = `<tr><th>Source</th><th>Stalls past</th>${loads.map(([l]) => `<th>${esc(l)}</th>`).join("")}</tr>`;
  const cell = (s: number) => (s < POWER.minSpeed ? `<b class="op-stall">${s === 0 ? "stalls" : "under the minimum"}</b>` : `<b>${num(s, 2)}</b> (${pct(rateAt(s))})`);
  const rows = [
    ...Object.values(POWER.rotors).map((r) => `<tr><th scope="row"><b>${esc(r.name)}</b><span class="ol-when">torque (${num(r.c, 2)} − speed) × ${num(r.F, 2)}</span></th><td class="ol-c"><b>${num(r.c * r.F, 2)}</b></td>${loads.map(([, R]) => `<td class="ol-c">${cell(rotorSpeed(r, R))}</td>`).join("")}</tr>`),
    ...Object.values(E).map((e) => `<tr><th scope="row"><b>${esc(e.name)}</b><span class="ol-when">${e.steam} L/s of steam, power ${e.power}, cap ${num(e.S, 2)}</span></th><td class="ol-c"><b>${num(4 * e.B, 1)}</b></td>${loads.map(([, R]) => `<td class="ol-c">${cell(engineSpeed([e], R))}</td>`).join("")}</tr>`),
  ].join("\n");
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-sources"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> a rotor (windmill, water wheel, hand crank) gives torque (c − speed) × F and settles at c − load ÷ F, stalling past c × F: the vanilla windmill c = min(0.6, wind), F = sails ÷ 4 (× 1.25 metal); Millwright's c = min(0.6 × m, wind × m), F = sails × m ÷ 4, m = 2 for the pack's sail modifiers × 2 for a double rotor; the water wheel c = min(0.3, flow), F = flow; the hand crank c = 0.5, F = 0.5 a player. An engine stalls past 4 × B. The percentage is the rate against the spec's (speed ÷ ${POWER.referenceSpeed}); a machine stops under ${POWER.minSpeed}.</p>`;
}

function renderDraw(): string {
  const head = `<tr><th>Machine</th><th>Load<span class="ol-when">working / empty</span></th><th>Power at ${POWER.referenceSpeed}<span class="ol-when">load × speed</span></th><th>Water</th><th>Water at its rate<span class="ol-when">L/s</span></th><th>Rate</th><th>On one battery's line</th></tr>`;
  const rows = DRAW.map((d) => {
    const on = d.works ? "works shaft" : d.onLine === 0 ? "—" : d.onLine === 1 ? "1" : d.onLine < 1 ? `${d.onLine} of one` : `${d.onLine}`;
    return `<tr><th scope="row"><b>${cardLink(d.id)}</b></th><td class="ol-c"><span class="ol-f"><b>${d.load}</b>${d.load ? ` / ${d.empty}` : ""}</span></td><td class="ol-c">${num(d.load * POWER.referenceSpeed, 2)}</td><td class="ol-c">${d.water ? `${d.water} L a ${d.waterUnit}` : "0"}</td><td class="ol-c">${d.water && d.rate != null ? num(waterAtRate(d), 2) : d.water ? "under 0.01" : "0"}</td><td>${esc(d.rateNote)}</td><td>${on}</td></tr>`;
  }).join("\n");
  const pump = `<tr><th scope="row"><b>MP pump (ppex)</b></th><td class="ol-c"><b>${POWER.mpPump.load}</b> fixed</td><td class="ol-c">${num(POWER.mpPump.load * POWER.referenceSpeed, 3)}</td><td class="ol-c">gives ${POWER.mpPump.litresPerSpeed} × speed L/s</td><td class="ol-c">${num(pumped(POWER.referenceSpeed))}</td><td>at ${POWER.mpPump.atm} atm</td><td>1</td></tr>`;
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-draw"><thead>${head}</thead><tbody>${rows}\n${pump}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> load = period horsepower × 0.1 (one battery's line about 18 HP; a Cornish high's steady load about 29 HP), drawn in full once assembled, working or idle, as every vanilla consumer and the pack's machines do (empty, a fifth; the ball mill on middlings is counted at it, and the breaker, working a quarter of the time, at a quarter of its load and three quarters of its empty figure: 0.32); 0.005 unassembled, × 3 dry of oil. Water = the period's tons a ton of ore at about 8.5 L a rock each (a twelfth of the period's mass, so one MP pump waters a line and its boiler), spent per item from a reservoir filled by ppex pipe; with it dry the machine stops. Water at rate = litres a rock × rock an hour ÷ ${POWER.hourSeconds} s. One battery's line: ${num(lineLoad(), 2)} (${num(lineLoad(true), 2)} with the ball mill grinding poor ore); the works' three machines ${num(worksLoad(), 1)} busy, on C's normal at ${num(engineSpeed([E.normal], worksLoad()), 2)} (${pct(rateAt(engineSpeed([E.normal], worksLoad())))}).</p>`;
}

function renderFuel(): string {
  const grades = Object.entries(CFG.units) as [string, number][];
  const head = `<tr><th>Ore (most ores' units)</th>${SETUPS.map((s) => `<th>${esc(s.name)}<span class="ol-when">coal an ingot (ingots a day)</span></th>`).join("")}</tr>`;
  const rows = grades
    .map(([g, u]) => {
      const poor = g === "poor";
      return `<tr><th scope="row"><b>${esc(g)}</b> (${u})${poor ? '<span class="ol-when">ball mill working</span>' : ""}</th>${SETUPS.map((s) => {
        const r = run(s, poor);
        if (r.stalls) return `<td class="ol-c"><b class="op-stall">stalls</b></td>`;
        const f = fuel(r.rate, u);
        return `<td class="ol-c${s.id === "C" ? " op-win" : ""}"><span class="ol-f"><b>${num(f.coalPerIngot, 2)}</b> (${num(f.ingotsPerDay)})</span></td>`;
      }).join("")}</tr>`;
    })
    .join("\n");
  const rawMedium = (POWER.rockPerDay * CFG.units.medium * CFG.smeltByGrade.medium) / CFG.unitsPerIngot;
  const c = fuel(run(SETUPS[2]!).rate, CFG.units.medium);
  return `<div class="ol-tablewrap"><table class="ol-table op-table op-fuel"><thead>${head}</thead><tbody>${rows}</tbody></table></div>
<p class="ol-formula"><b>Formula:</b> every boiler burns ${POWER.boilers.lancashire.coalPerDay} coal an in-game day whatever it drives (the coal pile under it burns one item every 2 hours); a line at full rate takes ${POWER.rockPerDay} rock a day. Ingots a day = ${POWER.rockPerDay} × rate × units a rock × ${pct(full(), 1)} (the full line) ÷ ${CFG.unitsPerIngot}; coal an ingot = ${POWER.boilers.lancashire.coalPerDay} ÷ that, before smelting fuel (sulfides add the roaster's coal, a sixth of the concentrate's mass at level 2). Smelted raw, the same ${POWER.rockPerDay} medium rock a day gives ${pct(CFG.smeltByGrade.medium)}: ${num(rawMedium)} ingots; C's line gives ${num(c.ingotsPerDay / rawMedium)} times the metal for its ${POWER.boilers.lancashire.coalPerDay} coal. The spec's own table states C at the reference rate (medium 1.7 coal an ingot, 7.1 a day); this one runs it at the speed its shaft settles at, ${num(run(SETUPS[2]!).speed, 2)}.</p>`;
}

export const OP_POWER_STYLE = `<style>
.op-stall { color: var(--critical); }
.op-setups { min-width: 52rem; }
.op-sources { min-width: 44rem; }
.op-draw { min-width: 46rem; }
.op-fuel { min-width: 34rem; }
.op-how { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr)); gap: 0.5rem; margin: 0.5rem 0; }
.op-how > div { background: var(--bg); border: 1px solid var(--border); border-radius: var(--radius); padding: 0.45rem 0.6rem; font-size: 0.84rem; }
.op-how b.op-h { display: block; margin-bottom: 0.15rem; }
.op-limits { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr)); gap: 0.5rem; margin: 0.5rem 0; }
.op-limits > div { background: var(--bg); border: 1px solid var(--critical); border-radius: var(--radius); padding: 0.45rem 0.6rem; font-size: 0.84rem; }
</style>`;

/** The section's body (ore-proposal.ts wraps it in its section). */
export function renderPower(): string {
  const C = run(SETUPS[2]!);
  const A = run(SETUPS[0]!);
  const jig = rotorSpeed(POWER.rotors.handcrank, 0.1);
  const wind = rotorSpeed(POWER.rotors.windmill, 0.1);
  return `${OP_POWER_STYLE}
<p class="ol-lede"><b>There is no design plant.</b> Every machine works off whatever drives it, at the speed that source holds under the load: a weaker plant runs the same line slower, a stronger one faster, and only three things stop a line rather than slow it (the hard limits). The spec's rates are at the reference speed ${POWER.referenceSpeed}; the setups below are examples of what a plant gives one battery's line.</p>
<h4>How mechanical power works</h4>
<div class="op-how">
<div><b class="op-h">A load is a constant resistance.</b> Each machine draws its full figure once assembled, working or idle, as every vanilla consumer does (a quern 0.1 loaded or empty); 0.005 unassembled and × 3 dry of oil. A machine may set its load by its job (the draw bench by its metal), never by whether it is busy. One battery's line is ${num(lineLoad(), 2)}, ${num(lineLoad(true), 2)} with the ball mill grinding poor ore.</div>
<div><b class="op-h">A source's torque falls with speed.</b> Ten times a second the game sums every source's torque and every load's resistance and moves the shaft toward where they balance: up by at most 0.05 a step, down without limit, never below 10⁻⁶. A rotor's torque is (c − speed) × F; an engine's B ÷ speed, tapering to nothing at its cap. There is no inertia and no stored energy.</div>
<div><b class="op-h">Rate is in proportion to speed, uncapped.</b> Work is counted in shaft turns (speed 1 is ${POWER.turnsPerHourPerSpeed} turns an in-game hour), so energy an item is the same at any speed and a faster shaft works faster: a battery's 6 rock an hour at ${POWER.referenceSpeed} is ${num((POWER.turnsPerHourPerSpeed * POWER.referenceSpeed) / 6)} turns a rock. Feed supply limits the machines downstream.</div>
<div><b class="op-h">Minimum speed.</b> Under ${POWER.minSpeed} a machine stops ("no power" in its block info), as the pack's machines do. Power = resistance × speed is the unit here; the game itself only balances torque against resistance and counts turns.</div>
</div>
<h4>The setups, as examples</h4>
<p class="ol-ores">What one battery's line does under each plant, with one MP pump on the line's shaft. Loads are the line's; speeds are where the shaft settles; rate is speed ÷ ${POWER.referenceSpeed}. Coal is ${POWER.boilers.lancashire.coalPerDay} a day in every case.</p>
${renderSetups()}
<h4>The hard limits</h4>
<div class="op-limits">
<div><b>Stall.</b> The load exceeds the most torque the source can give at zero speed (an engine 4 × B: Watt ${num(4 * E.watt.B)}, Cornish high ${num(4 * E.high.B)}; a windmill sails × wind ÷ 4; a hand crank 0.25 a player), and the shaft runs down to 0. Setup A stalls on poor ore with the ball mill working (${num(lineLoad(true), 2)} against ${num(4 * E.watt.B)}).</div>
<div><b>Minimum speed.</b> A machine stops under ${POWER.minSpeed}. A hand crank on the whole line settles at ${num(rotorSpeed(POWER.rotors.handcrank4, lineLoad()), 2)} with four players: ${rotorSpeed(POWER.rotors.handcrank4, lineLoad()) < POWER.minSpeed ? "stopped" : `${pct(rateAt(rotorSpeed(POWER.rotors.handcrank4, lineLoad())))} of the rate`}.</div>
<div><b>Water.</b> A wet machine with its reservoir dry stops. A pump on the line's own shaft slows with the line, so water an item holds at any speed; a pump on another shaft or a fixed-rate one (hand pump, fluid pump) does not, and a line running faster than it outpaces it. A short main starves the boiler first.</div>
</div>
<h4>Hand, wind and water, and the steam engines alone</h4>
<p class="ol-ores">Where each source holds a load, and the rate that gives. A hand crank turns a jig at ${num(jig, 2)} for one player (${pct(rateAt(jig))}); a vanilla windmill turns a mule-sized arrastra at ${num(wind, 2)} (${pct(rateAt(wind))}); only steam or a Millwright double rotor runs a line.</p>
${renderSources()}
<h4>Each machine's load and water</h4>
${renderDraw()}
<h4>Fuel against metal</h4>
<p class="ol-ores">Coal an ingot by setup, before smelting fuel, with the ingots a day in brackets. Under C the line at ${num(C.speed, 2)} gives medium ore ${num(fuel(C.rate, CFG.units.medium).ingotsPerDay)} ingots a day; under A ${num(fuel(A.rate, CFG.units.medium).ingotsPerDay)}.</p>
${renderFuel()}`;
}
