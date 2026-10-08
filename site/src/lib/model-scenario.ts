// A model's optional scenario: what the generic viewer cannot know from the rig alone, given
// as data in site/models.json. Labels for the rig's inputs and `requires` values, a prop (a
// box of a chosen size laid on one of the rig's line anchors, such as a trunk on a bed, or
// travelling along the rig's trunk path), and a play script: the phases the inputs run
// through when the reader presses Play. Without one, Play only turns the shaft (at the
// reader's input speed), and moves the rig's work with it when the script gears it.
// docs/recipe-browser/models.md describes the format.
import type { Anchor } from "./model-anchors.ts";
import type { VehicleSpec } from "./model-vehicle.ts";
import { TRUNK_CLASSES, workEnd, workOf, type Rig, type TrunkClass, type Vec3, type Work } from "./rig.ts";

export interface InputLabel {
  label?: string;
  hint?: string;
}

/** The size input's labels, and a name for each class: none, thin, thick. */
export interface SizeInput extends InputLabel {
  names?: [string, string, string];
}

export interface PropOption {
  id: string;
  label: string;
  /** Length along the line's axis, width across it, height; blocks. */
  size: [number, number, number];
  /** The trunk class this option is (the rig's size input), for a prop that moves with the trunk. */
  class?: TrunkClass;
}

export interface PropSpec {
  label: string;
  /** The rig key of a line anchor ({ origin, axis, length }) the prop lies on. */
  on: string;
  options: PropOption[];
  /** An option id, or "none" (the default). */
  default?: string;
  colour?: string;
  /** "underside" (the default): its underside on the line, as a trunk on a bed; "axis": centred on the line, as a trunk on a ring's axis. */
  placement?: "underside" | "axis";
  /** "trunk": the prop is the trunk the rig's trunkPath carries; its nose is at trunkPath.nose0 + T, and its option is the class. */
  moves?: "trunk";
}

export interface PhaseStart {
  lifting?: number;
  prop?: boolean;
  depthAtMost?: number;
  /** A trunk is chosen and part-way through its trip: 0 < T < its end. */
  travelling?: boolean;
}

export interface PlayPhase {
  id: string;
  label: string;
  /** The lifting input during the phase (default 0). */
  lifting?: number;
  /** Which input the phase moves: depth (the default) or W, the work ("trunk", a trunk's travel, is the same). */
  input?: "depth" | "work" | "trunk";
  /**
   * Where the phase runs to. Depth: 0..1, or "contact", where the edge meets the prop's top.
   * Trunk: T in blocks, or "end", the chosen class's end of trip. Not used by a wait.
   */
  to?: number | "contact" | "end";
  /** The phase moves nothing and lasts its turns or seconds (a pause). */
  wait?: boolean;
  /** How long it takes from where the phase starts: shaft turns, or seconds. */
  turns?: number;
  seconds?: number;
  /** At the phase's start: put the chosen prop on (a trunk goes on at T = 0), or take it off. */
  prop?: "load" | "clear";
  next: string;
  /** The next phase when no prop is chosen (default: next). */
  nextWithoutProp?: string;
  /** Play starts in the first phase whose conditions hold; a phase without startIf is never chosen to start. */
  startIf?: PhaseStart;
}

export interface PlaySpec {
  /** The input speed slider's starting point, as seconds per shaft turn (default 1 turn a second). */
  secondsPerTurn?: number;
  /**
   * Shaft turns per unit of W, as a number or a rig path ("cut.turnsPerTooth"): with it, Play also
   * moves the rig's work forward as the shaft turns (either way), up to the class's end, whenever no
   * script phase runs it. For a machine whose work is geared to the shaft.
   */
  turnsPerWork?: number | string;
  /** For "contact": the height of the working edge at depth 0 and at depth 1, as numbers or rig paths ("saw.topY"). */
  edge?: { top: number | string; bottom: number | string };
  /** How fast the trunk's presence eases in and out, per second (default 12, the game's). */
  presenceRate?: number;
  phases: PlayPhase[];
}

export interface Scenario {
  inputs?: {
    theta?: InputLabel;
    depth?: InputLabel;
    lifting?: InputLabel;
    reverse?: InputLabel;
    /** W's label: `work` for any rig; `trunk` is read too, its trunk-flavoured name. */
    work?: InputLabel;
    trunk?: InputLabel;
    size?: SizeInput;
    presence?: InputLabel;
    feed?: InputLabel;
    oil?: InputLabel;
  };
  requires?: Record<string, string>;
  /** Requires values that belong to one class only (one master's set-up, say): shown only while that class is chosen. */
  requiresClass?: Record<string, TrunkClass>;
  /** Requires values of which exactly one is fitted at a time (the handcar's branch lever): a select, not checkboxes. */
  choices?: RequiresChoice[];
  prop?: PropSpec;
  play?: PlaySpec;
  /** The model is a vehicle on a track (model-vehicle.ts). */
  vehicle?: VehicleSpec;
}

export interface RequiresChoice {
  label: string;
  /** Requires values, at least two; each in one choice only. */
  values: string[];
  /** The value fitted at first (default the first). */
  default?: string;
}

/** What is fitted at first: every requires value, except that of each choice only its default. */
export function initialFitted(requires: readonly string[], choices: readonly RequiresChoice[] = []): Record<string, boolean> {
  const fitted = Object.fromEntries(requires.map((r) => [r, true]));
  for (const c of choices) for (const v of c.values) fitted[v] = v === (c.default ?? c.values[0]);
  return fitted;
}

/** Fits `value` and no other of its choice's. */
export function pickChoice(fitted: Readonly<Record<string, boolean>>, choice: RequiresChoice, value: string): Record<string, boolean> {
  return { ...fitted, ...Object.fromEntries(choice.values.map((v) => [v, v === value])) };
}

/** The choice a requires value belongs to, if any. */
export function choiceOf(choices: readonly RequiresChoice[] | undefined, value: string): RequiresChoice | null {
  return choices?.find((c) => c.values.includes(value)) ?? null;
}

export const DEFAULT_SECONDS_PER_TURN = 1.2;
/** The input speed slider: revolutions of the shaft per second of real time. */
export const INPUT_SPEED_MAX = 5;
export const DEFAULT_INPUT_SPEED = 1;

/**
 * The input speed Play starts at: `turnsPerSecond` when given (a vehicle's speed, as axle turns),
 * else the script's secondsPerTurn as turns a second, else 1; within 0..INPUT_SPEED_MAX.
 */
export function defaultInputSpeed(play: PlaySpec | undefined, turnsPerSecond?: number | null): number {
  const spt = play?.secondsPerTurn;
  const rps = turnsPerSecond != null && turnsPerSecond > 0 ? turnsPerSecond : spt !== undefined && spt > 0 ? 1 / spt : DEFAULT_INPUT_SPEED;
  return Math.min(INPUT_SPEED_MAX, Math.max(0, Math.round(rps * 100) / 100));
}

/** Whether a phase moves the work (W; "trunk" is its trunk-flavoured spelling). */
export function movesWork(p: PlayPhase): boolean {
  return p.input === "work" || p.input === "trunk";
}
export const DEFAULT_PRESENCE_RATE = 12;

/** A number from the rig by dotted path, or the number itself. */
export function rigNumber(rig: Rig, ref: number | string): number {
  if (typeof ref === "number") return ref;
  let v: unknown = rig;
  for (const k of ref.split(".")) v = typeof v === "object" && v !== null ? (v as Record<string, unknown>)[k] : undefined;
  if (typeof v !== "number" || !Number.isFinite(v)) throw new Error(`rig has no number at "${ref}"`);
  return v;
}

/** An option's class index for the rig's size input: 0 (none, or no class), 1 thin, 2 thick. */
export function optionClass(option: PropOption | null | undefined): 0 | 1 | 2 {
  const i = option?.class ? TRUNK_CLASSES.indexOf(option.class) : -1;
  return i < 0 ? 0 : ((i + 1) as 1 | 2);
}

/**
 * Where the prop sits, in blocks. Along the line it is centred on the line's origin or, given
 * the nose's place on the line's axis (a prop that moves with the trunk), ends there. Across, it
 * lies with its underside on the line or, with `placement: "axis"`, centred on it.
 */
export function propBox(
  option: PropOption,
  line: Extract<Anchor, { kind: "line" }>,
  place: { placement?: PropSpec["placement"]; nose?: number } = {},
): { centre: Vec3; size: Vec3 } {
  const [length, width, height] = option.size;
  const o = line.origin;
  const size: Vec3 = line.axis === "x" ? [length, height, width] : line.axis === "z" ? [width, height, length] : [width, length, width];
  const centre: Vec3 = place.placement === "axis" ? [o[0], o[1], o[2]] : [o[0], o[1] + size[1] / 2, o[2]];
  if (place.nose !== undefined) centre[{ x: 0, y: 1, z: 2 }[line.axis]] = place.nose - length / 2;
  return { centre, size };
}

/** The depth at which the edge, moving from edge.top at depth 0 to edge.bottom at depth 1, reaches `height`, clamped to 0..1. */
/**
 * How much φ, the feed's travel, grows when T goes from `last` to `next`: ΔT over the rig's
 * `feed.blocksPerRadian`, and nothing when T goes back (a new trunk). The feed is geared and never
 * slips, so its rolls move exactly with the trunk; this is the game's RosserVisuals.FeedAdvance.
 */
export function feedAdvance(last: number, next: number, blocksPerRadian: number | undefined): number {
  return blocksPerRadian !== undefined && blocksPerRadian > 0 && next > last ? (next - last) / blocksPerRadian : 0;
}

/** The rig's `feed.blocksPerRadian` (the trunk's travel per radian of the feed), if it has one. */
export function feedBlocksPerRadian(rig: Rig | null | undefined): number | undefined {
  const feed = rig?.["feed"];
  const b = feed && typeof feed === "object" ? (feed as Record<string, unknown>)["blocksPerRadian"] : undefined;
  return typeof b === "number" && b > 0 ? b : undefined;
}

export function contactDepth(play: PlaySpec, rig: Rig, height: number): number {
  if (!play.edge) return 0;
  const top = rigNumber(rig, play.edge.top);
  const bottom = rigNumber(rig, play.edge.bottom);
  if (top === bottom) return 0;
  return Math.min(1, Math.max(0, (top - height) / (top - bottom)));
}

export interface Motion {
  theta: number;
  travel: number;
  depth: number;
  lifting: number;
  /** The running phase's id, or null when posed by hand. */
  phase: string | null;
  /** The phase's input (depth, or W) when the phase started. */
  phaseFrom: number;
  /** Whether the prop is on (Play loads and clears it). */
  propOn: boolean;
  /** W, the rig's work in its unit (a trunk's travel along its trunkPath, blocks); 0 when not given. */
  work?: number;
  /** k, the class shown: 0 none, 1 thin, 2 thick. Held after a trunk goes until presence is back to 0. */
  size?: number;
  /** p, the work's presence, 0..1. */
  presence?: number;
  /** φ, the feed's travel, radians: it follows T (feedAdvance), never going back. */
  feed?: number;
  /** How full the oil tank is, 0..1 (set by hand). */
  oil?: number;

  /** How long a wait phase has run, in its own unit (turns or seconds). */
  phaseTime?: number;
}

/** The trip Play runs when the prop is the rig's trunk: the chosen option's class and that class's end of trip. */
export interface PlayTrip {
  /** 0 when no prop is chosen. */
  size: number;
  /** W at the end of the trip (0 without a class). */
  end: number;
}

/** Whether a part with this requires value shows for class `size`: always, unless the scenario ties the value to another class. */
export function classShows(requires: string | null | undefined, size: number, scenario?: Scenario): boolean {
  const cls = requires ? scenario?.requiresClass?.[requires] : undefined;
  if (cls === undefined) return true;
  return size === TRUNK_CLASSES.indexOf(cls) + 1;
}

/** The trip for a chosen option on a trunk path; undefined when the prop does not move with the trunk. */
export function playTrip(prop: PropSpec | undefined, option: PropOption | null, path: Work | null): PlayTrip | undefined {
  if (!prop || prop.moves !== "trunk" || !path) return undefined;
  const size = optionClass(option);
  return { size, end: workEnd(path, size) };
}

/** The phase Play starts in from a pose; null when there is no script. `trip` decides `travelling`. */
export function startPhase(
  play: PlaySpec | undefined,
  m: Pick<Motion, "depth" | "lifting"> & Partial<Pick<Motion, "work">>,
  propChosen: boolean,
  trip?: PlayTrip,
): string | null {
  if (!play || play.phases.length === 0) return null;
  for (const p of play.phases) {
    const s = p.startIf;
    if (!s) continue;
    if (s.lifting !== undefined && (m.lifting > 0.5 ? 1 : 0) !== s.lifting) continue;
    if (s.prop !== undefined && s.prop !== propChosen) continue;
    if (s.depthAtMost !== undefined && m.depth > s.depthAtMost) continue;
    if (s.travelling !== undefined) {
      const t = m.work ?? 0;
      const travelling = propChosen && trip !== undefined && trip.size > 0 && t > 0 && t < trip.end;
      if (travelling !== s.travelling) continue;
    }
    return p.id;
  }
  return play.phases[0]!.id;
}

export interface PlayContext {
  play: PlaySpec | undefined;
  /** +1 or −1: which way the shaft turns. */
  direction: number;
  propChosen: boolean;
  /** The contact depth for the chosen prop (contactDepth), used by phases that run to "contact". */
  contact: number;
  /** Set when the prop is the rig's trunk: Play then loads it at W = 0, runs work phases to its end, eases presence and counts φ. */
  trip?: PlayTrip;
  /** The rig's feed.blocksPerRadian: φ grows by ΔT over it (feedAdvance). Without it φ stays put. */
  blocksPerRadian?: number;
  /** Set from play.turnsPerWork: W moves forward by one per `turns` shaft turns, up to `end`, whenever no phase runs. */
  geared?: { turns: number; end: number };
  /** The reader's input speed, shaft turns a second (0 stops Play); without it, the script's secondsPerTurn. */
  turnsPerSecond?: number;
}

/** Enters a phase: sets lifting and the prop, and remembers where it started. */
export function enterPhase(ctx: PlayContext, m: Motion, id: string): Motion {
  const p = ctx.play?.phases.find((q) => q.id === id);
  if (!p) return { ...m, phase: null };
  const next: Motion = {
    ...m,
    phase: id,
    lifting: p.lifting ?? 0,
    propOn: p.prop === "load" ? ctx.propChosen : p.prop === "clear" ? false : m.propOn,
  };
  // A trunk goes on at the start of its trip, as the chosen class; one taken away keeps its class until it has eased out.
  if (ctx.trip && p.prop === "load" && ctx.propChosen) {
    next.work = 0;
    next.size = ctx.trip.size;
  }
  next.phaseFrom = movesWork(p) ? (next.work ?? 0) : next.depth;
  if (p.wait) next.phaseTime = 0;
  return next;
}

/** Advances the motion by `dt` seconds of Play: the shaft turns, and the script, if any, moves depth or W from phase to phase. */
export function advance(ctx: PlayContext, m: Motion, dt: number): Motion {
  const next = run(ctx, m, dt);
  if (!ctx.trip) return next;
  // Presence eases towards whether the trunk is on; the class is held until it is back to 0.
  const rate = ctx.play?.presenceRate ?? DEFAULT_PRESENCE_RATE;
  const target = next.propOn ? 1 : 0;
  const p = next.presence ?? 0;
  const presence = p < target ? Math.min(target, p + rate * dt) : Math.max(target, p - rate * dt);
  return { ...next, presence, size: presence <= 0 && !next.propOn ? 0 : next.size };
}

function run(ctx: PlayContext, m: Motion, dt: number): Motion {
  // At an input speed of 0 everything stands, waits included.
  if (ctx.turnsPerSecond !== undefined && !(ctx.turnsPerSecond > 0)) return m;
  const secondsPerTurn = ctx.turnsPerSecond !== undefined ? 1 / ctx.turnsPerSecond : (ctx.play?.secondsPerTurn ?? DEFAULT_SECONDS_PER_TURN);
  const dTheta = (ctx.direction * dt * 2 * Math.PI) / secondsPerTurn;
  let turns = Math.abs(dTheta) / (2 * Math.PI);
  let seconds = dt;
  // θ is not wrapped: a part that turns at a fraction of the shaft's speed (the handcar's beam, a
  // third) would jump back at each turn. The page shows it within the rig's cycle (thetaTurns).
  let next: Motion = { ...m, theta: m.theta + dTheta, travel: m.travel + Math.abs(dTheta) };
  if (ctx.geared && next.phase === null && ctx.geared.turns > 0) {
    const w = next.work ?? 0;
    if (w < ctx.geared.end) next.work = Math.min(ctx.geared.end, w + turns / ctx.geared.turns);
  }
  if (!ctx.play || next.phase === null) return next;
  // A phase that is already at its target ends at once; the bound stops a script of such phases looping.
  for (let guard = 0; guard <= ctx.play.phases.length; guard++) {
    const p = ctx.play.phases.find((q) => q.id === next.phase);
    if (!p) return { ...next, phase: null };
    const budget = p.turns ? turns : seconds;
    let used: number;
    if (p.wait) {
      const remaining = Math.max(0, (p.turns ?? p.seconds ?? 0) - (next.phaseTime ?? 0));
      if (budget < remaining) return { ...next, phaseTime: (next.phaseTime ?? 0) + budget };
      used = remaining;
    } else {
      const work = movesWork(p);
      const value = work ? (next.work ?? 0) : next.depth;
      const target = p.to === "contact" ? ctx.contact : p.to === "end" ? (ctx.trip?.end ?? value) : (p.to ?? value);
      const span = Math.abs(target - next.phaseFrom);
      const remaining = Math.abs(target - value);
      // The input's change per turn or per second, over the phase's whole span.
      const rate = p.turns ? span / p.turns : p.seconds ? span / p.seconds : Infinity;
      const step = rate === Infinity ? Infinity : rate * budget;
      // φ follows W, as the game's geared feed: it grows by the trunk's advance over blocksPerRadian.
      const feed = (to: number) => (work ? { feed: (next.feed ?? 0) + feedAdvance(value, to, ctx.blocksPerRadian) } : {});
      if (step < remaining) {
        const moved = value + Math.sign(target - value) * step;
        return { ...next, ...(work ? { work: moved } : { depth: moved }), ...feed(moved) };
      }
      // Reached the target: spend what is left of this frame in the next phase.
      used = rate === Infinity || rate === 0 ? 0 : remaining / rate;
      next = { ...next, ...(work ? { work: target } : { depth: target }), ...feed(target) };
    }
    if (p.turns) {
      turns = Math.max(0, turns - used);
      seconds = turns * secondsPerTurn;
    } else {
      seconds = Math.max(0, seconds - used);
      turns = seconds / secondsPerTurn;
    }
    next = enterPhase(ctx, next, ctx.propChosen ? p.next : (p.nextWithoutProp ?? p.next));
    if (turns <= 0 && seconds <= 0) return next;
  }
  return next;
}

/** Problems with a scenario against its rig's anchors, as messages; empty when it is sound. */
export function checkScenario(s: Scenario, rig: Rig | null, anchors: readonly Anchor[], requires: readonly string[]): string[] {
  const out: string[] = [];
  for (const key of Object.keys(s.requires ?? {})) if (!requires.includes(key)) out.push(`requires label "${key}" names no part's requires value`);
  for (const [key, cls] of Object.entries(s.requiresClass ?? {})) {
    if (!requires.includes(key)) out.push(`requiresClass "${key}" names no part's requires value`);
    if (!(TRUNK_CLASSES as readonly string[]).includes(cls)) out.push(`requiresClass "${key}": class must be "thin" or "thick"`);
  }
  const chosen = new Set<string>();
  for (const c of s.choices ?? []) {
    if (typeof c.label !== "string" || c.label === "") out.push("a choice needs a label");
    if (!Array.isArray(c.values) || c.values.length < 2) out.push(`choice "${c.label}" needs two values or more`);
    for (const v of c.values ?? []) {
      if (!requires.includes(v)) out.push(`choice "${c.label}": "${v}" is no part's requires value`);
      if (chosen.has(v)) out.push(`choice "${c.label}": "${v}" is in another choice too`);
      if (s.requiresClass?.[v] !== undefined) out.push(`choice "${c.label}": "${v}" belongs to a class (requiresClass), so it cannot be chosen`);
      chosen.add(v);
    }
    if (c.default !== undefined && !c.values?.includes(c.default)) out.push(`choice "${c.label}": default "${c.default}" is not one of its values`);
  }
  let path: Work | null = null;
  try {
    path = workOf(rig);
  } catch (e) {
    out.push((e as Error).message);
  }
  if (s.inputs?.work && s.inputs?.trunk) out.push("inputs: label the work as work or trunk, not both");
  const names = s.inputs?.size?.names;
  if (names !== undefined && (!Array.isArray(names) || names.length !== 3 || !names.every((n) => typeof n === "string" && n !== "")))
    out.push("inputs.size.names needs three names: none, thin, thick");
  const moving = s.prop?.moves === "trunk";
  if (s.prop) {
    const line = anchors.find((a) => a.kind === "line" && a.key === s.prop!.on);
    if (!line) out.push(`prop.on "${s.prop.on}" is not a line anchor ({ origin, axis, length }) of the rig`);
    if (s.prop.placement !== undefined && s.prop.placement !== "underside" && s.prop.placement !== "axis") out.push(`prop.placement must be "underside" or "axis"`);
    if (s.prop.moves !== undefined && s.prop.moves !== "trunk") out.push(`prop.moves must be "trunk"`);
    if (moving && path?.kind !== "trunk") out.push(`prop.moves "trunk" needs the rig's trunkPath`);
    const ids = new Set<string>();
    for (const o of s.prop.options ?? []) {
      if (ids.has(o.id) || o.id === "none") out.push(`prop option "${o.id}" is listed twice or is reserved`);
      ids.add(o.id);
      if (!Array.isArray(o.size) || o.size.length !== 3 || !o.size.every((n) => typeof n === "number" && n > 0))
        out.push(`prop option "${o.id}" needs a size of three positive numbers`);
      if (o.class !== undefined && !(TRUNK_CLASSES as readonly string[]).includes(o.class)) out.push(`prop option "${o.id}": class must be "thin" or "thick"`);
      if (moving && o.class === undefined) out.push(`prop option "${o.id}" needs a class, since the prop moves with the trunk`);
      // The box must be the trunk the path's ends are measured for.
      const k = optionClass(o);
      if (moving && path && k > 0 && Array.isArray(o.size) && o.size[0] !== path.lengths[k])
        out.push(`prop option "${o.id}" is ${o.size[0]} long, but trunkPath.lengths.${o.class} is ${path.lengths[k]}`);
    }
    if (s.prop.default !== undefined && s.prop.default !== "none" && !ids.has(s.prop.default)) out.push(`prop.default "${s.prop.default}" is not an option`);
  }
  if (s.play) {
    if (!rig) out.push("play needs a rig");
    if (s.play.secondsPerTurn !== undefined && !(s.play.secondsPerTurn > 0)) out.push("play.secondsPerTurn must be above 0");
    if (s.play.turnsPerWork !== undefined) {
      if (!path) out.push("play.turnsPerWork needs the rig's work or trunkPath");
      if (s.prop?.moves === "trunk") out.push("play.turnsPerWork is for a rig whose work is geared to the shaft, not one with a travelling trunk prop");
      if (rig)
        try {
          if (!(rigNumber(rig, s.play.turnsPerWork) > 0)) out.push("play.turnsPerWork must be above 0");
        } catch (e) {
          out.push(`play.turnsPerWork: ${(e as Error).message}`);
        }
    }

    const ids = new Set(s.play.phases.map((p) => p.id));
    if (ids.size !== s.play.phases.length) out.push("play phase ids must be unique");
    if (s.play.presenceRate !== undefined && !(s.play.presenceRate > 0)) out.push("play.presenceRate must be above 0");
    for (const p of s.play.phases) {
      for (const n of [p.next, p.nextWithoutProp]) if (n !== undefined && !ids.has(n)) out.push(`play phase "${p.id}" goes to "${n}", which is not a phase`);
      if (p.input !== undefined && p.input !== "depth" && !movesWork(p)) out.push(`play phase "${p.id}": input must be "depth", "work" or "trunk"`);
      if (p.wait) {
        if (p.to !== undefined || p.input !== undefined) out.push(`play phase "${p.id}" waits, so it takes no to or input`);
      } else if (movesWork(p)) {
        if (p.to !== "end" && (typeof p.to !== "number" || p.to < 0)) out.push(`play phase "${p.id}": a trunk phase's to must be W (0 or more) or "end"`);
        if (!moving) out.push(`play phase "${p.id}" moves the trunk, which needs a prop that moves with it (prop.moves "trunk")`);
      } else if (p.to !== "contact" && (typeof p.to !== "number" || p.to < 0 || p.to > 1)) out.push(`play phase "${p.id}": to must be 0..1 or "contact"`);
      if (!(p.turns! > 0) && !(p.seconds! > 0)) out.push(`play phase "${p.id}" needs turns or seconds above 0`);
      if (p.to === "contact" && !s.prop) out.push(`play phase "${p.id}" runs to "contact", which needs a prop`);
      if (p.startIf?.travelling !== undefined && !moving) out.push(`play phase "${p.id}": startIf.travelling needs a prop that moves with the trunk`);
    }
    if (s.play.phases.some((p) => p.to === "contact" && !p.wait && !movesWork(p))) {
      if (!s.play.edge) out.push(`play needs an edge for "contact"`);
      else if (rig)
        for (const ref of [s.play.edge.top, s.play.edge.bottom])
          try {
            rigNumber(rig, ref);
          } catch (e) {
            out.push(`play.edge: ${(e as Error).message}`);
          }
    }
  }
  return out;
}
