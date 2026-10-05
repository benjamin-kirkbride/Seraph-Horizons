// A model's optional scenario: what the generic viewer cannot know from the rig alone, given
// as data in site/models.json. Labels for the rig's inputs and `requires` values, a prop (a
// box of a chosen size laid on one of the rig's line anchors, such as a trunk on a bed, or
// travelling along the rig's trunk path), and a play script: the phases the inputs run
// through when the reader presses Play. Without one, Play only turns the shaft.
// docs/recipe-browser/models.md describes the format.
import type { Anchor } from "./model-anchors.ts";
import { TRUNK_CLASSES, trunkPathOf, tripEnd, type Rig, type TrunkClass, type TrunkPath, type Vec3 } from "./rig.ts";

export interface InputLabel {
  label?: string;
  hint?: string;
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
  /** Which input the phase moves: depth (the default) or T, the trunk's travel. */
  input?: "depth" | "trunk";
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
  secondsPerTurn?: number;
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
    trunk?: InputLabel;
    size?: InputLabel;
    presence?: InputLabel;
    feed?: InputLabel;
  };
  requires?: Record<string, string>;
  prop?: PropSpec;
  play?: PlaySpec;
}

export const DEFAULT_SECONDS_PER_TURN = 1.2;
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
  /** The phase's input (depth, or T) when the phase started. */
  phaseFrom: number;
  /** Whether the prop is on (Play loads and clears it). */
  propOn: boolean;
  /** T, the trunk's travel along the rig's trunkPath, blocks (0 when not given). */
  trunk?: number;
  /** k, the class shown: 0 none, 1 thin, 2 thick. Held after the trunk goes until presence is back to 0. */
  size?: number;
  /** p, the trunk's presence, 0..1. */
  presence?: number;
  /** φ, the feed's travel, radians: it follows T (feedAdvance), never going back. */
  feed?: number;
  /** How long a wait phase has run, in its own unit (turns or seconds). */
  phaseTime?: number;
}

/** The trip Play runs when the prop is the rig's trunk: the chosen option's class and that class's end of trip. */
export interface PlayTrip {
  /** 0 when no prop is chosen. */
  size: number;
  /** T at the end of the trip (0 without a class). */
  end: number;
}

/** The trip for a chosen option on a trunk path; undefined when the prop does not move with the trunk. */
export function playTrip(prop: PropSpec | undefined, option: PropOption | null, path: TrunkPath | null): PlayTrip | undefined {
  if (!prop || prop.moves !== "trunk" || !path) return undefined;
  const size = optionClass(option);
  return { size, end: tripEnd(path, size) };
}

/** The phase Play starts in from a pose; null when there is no script. `trip` decides `travelling`. */
export function startPhase(
  play: PlaySpec | undefined,
  m: Pick<Motion, "depth" | "lifting"> & Partial<Pick<Motion, "trunk">>,
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
      const t = m.trunk ?? 0;
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
  /** Set when the prop is the rig's trunk: Play then loads it at T = 0, runs trunk phases to its end, eases presence and counts φ. */
  trip?: PlayTrip;
  /** The rig's feed.blocksPerRadian: φ grows by ΔT over it (feedAdvance). Without it φ stays put. */
  blocksPerRadian?: number;
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
    next.trunk = 0;
    next.size = ctx.trip.size;
  }
  next.phaseFrom = p.input === "trunk" ? (next.trunk ?? 0) : next.depth;
  if (p.wait) next.phaseTime = 0;
  return next;
}

/** Advances the motion by `dt` seconds of Play: the shaft turns, and the script, if any, moves depth or T from phase to phase. */
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
  const secondsPerTurn = ctx.play?.secondsPerTurn ?? DEFAULT_SECONDS_PER_TURN;
  const dTheta = (ctx.direction * dt * 2 * Math.PI) / secondsPerTurn;
  let turns = Math.abs(dTheta) / (2 * Math.PI);
  let seconds = dt;
  let next: Motion = { ...m, theta: wrapAngle(m.theta + dTheta), travel: m.travel + Math.abs(dTheta) };
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
      const trunk = p.input === "trunk";
      const value = trunk ? (next.trunk ?? 0) : next.depth;
      const target = p.to === "contact" ? ctx.contact : p.to === "end" ? (ctx.trip?.end ?? value) : (p.to ?? value);
      const span = Math.abs(target - next.phaseFrom);
      const remaining = Math.abs(target - value);
      // The input's change per turn or per second, over the phase's whole span.
      const rate = p.turns ? span / p.turns : p.seconds ? span / p.seconds : Infinity;
      const step = rate === Infinity ? Infinity : rate * budget;
      // φ follows T, as the game's geared feed: it grows by the trunk's advance over blocksPerRadian.
      const feed = (to: number) => (trunk ? { feed: (next.feed ?? 0) + feedAdvance(value, to, ctx.blocksPerRadian) } : {});
      if (step < remaining) {
        const moved = value + Math.sign(target - value) * step;
        return { ...next, ...(trunk ? { trunk: moved } : { depth: moved }), ...feed(moved) };
      }
      // Reached the target: spend what is left of this frame in the next phase.
      used = rate === Infinity || rate === 0 ? 0 : remaining / rate;
      next = { ...next, ...(trunk ? { trunk: target } : { depth: target }), ...feed(target) };
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

function wrapAngle(a: number): number {
  const t = 2 * Math.PI;
  return ((a % t) + t) % t;
}

/** Problems with a scenario against its rig's anchors, as messages; empty when it is sound. */
export function checkScenario(s: Scenario, rig: Rig | null, anchors: readonly Anchor[], requires: readonly string[]): string[] {
  const out: string[] = [];
  for (const key of Object.keys(s.requires ?? {})) if (!requires.includes(key)) out.push(`requires label "${key}" names no part's requires value`);
  let path: TrunkPath | null = null;
  try {
    path = trunkPathOf(rig);
  } catch (e) {
    out.push((e as Error).message);
  }
  const moving = s.prop?.moves === "trunk";
  if (s.prop) {
    const line = anchors.find((a) => a.kind === "line" && a.key === s.prop!.on);
    if (!line) out.push(`prop.on "${s.prop.on}" is not a line anchor ({ origin, axis, length }) of the rig`);
    if (s.prop.placement !== undefined && s.prop.placement !== "underside" && s.prop.placement !== "axis") out.push(`prop.placement must be "underside" or "axis"`);
    if (s.prop.moves !== undefined && s.prop.moves !== "trunk") out.push(`prop.moves must be "trunk"`);
    if (moving && !path) out.push(`prop.moves "trunk" needs the rig's trunkPath`);
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
    const ids = new Set(s.play.phases.map((p) => p.id));
    if (ids.size !== s.play.phases.length) out.push("play phase ids must be unique");
    if (s.play.presenceRate !== undefined && !(s.play.presenceRate > 0)) out.push("play.presenceRate must be above 0");
    for (const p of s.play.phases) {
      for (const n of [p.next, p.nextWithoutProp]) if (n !== undefined && !ids.has(n)) out.push(`play phase "${p.id}" goes to "${n}", which is not a phase`);
      if (p.input !== undefined && p.input !== "depth" && p.input !== "trunk") out.push(`play phase "${p.id}": input must be "depth" or "trunk"`);
      if (p.wait) {
        if (p.to !== undefined || p.input !== undefined) out.push(`play phase "${p.id}" waits, so it takes no to or input`);
      } else if (p.input === "trunk") {
        if (p.to !== "end" && (typeof p.to !== "number" || p.to < 0)) out.push(`play phase "${p.id}": a trunk phase's to must be T (0 or more) or "end"`);
        if (!moving) out.push(`play phase "${p.id}" moves the trunk, which needs a prop that moves with it (prop.moves "trunk")`);
      } else if (p.to !== "contact" && (typeof p.to !== "number" || p.to < 0 || p.to > 1)) out.push(`play phase "${p.id}": to must be 0..1 or "contact"`);
      if (!(p.turns! > 0) && !(p.seconds! > 0)) out.push(`play phase "${p.id}" needs turns or seconds above 0`);
      if (p.to === "contact" && !s.prop) out.push(`play phase "${p.id}" runs to "contact", which needs a prop`);
      if (p.startIf?.travelling !== undefined && !moving) out.push(`play phase "${p.id}": startIf.travelling needs a prop that moves with the trunk`);
    }
    if (s.play.phases.some((p) => p.to === "contact" && !p.wait && p.input !== "trunk")) {
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
