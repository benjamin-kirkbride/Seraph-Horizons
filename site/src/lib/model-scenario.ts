// A model's optional scenario: what the generic viewer cannot know from the rig alone, given
// as data in site/models.json. Labels for the rig's inputs and `requires` values, a prop (a
// box of a chosen size laid on one of the rig's line anchors, such as a trunk on a bed), and a
// play script: the phases the inputs run through when the reader presses Play. Without one,
// Play only turns the shaft. docs/recipe-browser/models.md describes the format.
import type { Anchor } from "./model-anchors.ts";
import type { Rig, Vec3 } from "./rig.ts";

export interface InputLabel {
  label?: string;
  hint?: string;
}

export interface PropOption {
  id: string;
  label: string;
  /** Length along the line's axis, width across it, height; blocks. */
  size: [number, number, number];
}

export interface PropSpec {
  label: string;
  /** The rig key of a line anchor ({ origin, axis, length }) the prop lies on, centred and with its underside on origin. */
  on: string;
  options: PropOption[];
  /** An option id, or "none" (the default). */
  default?: string;
  colour?: string;
}

export interface PhaseStart {
  lifting?: number;
  prop?: boolean;
  depthAtMost?: number;
}

export interface PlayPhase {
  id: string;
  label: string;
  /** The lifting input during the phase (default 0). */
  lifting?: number;
  /** The depth the phase runs to: a number, or "contact", where the edge meets the prop's top. */
  to: number | "contact";
  /** How long it takes from where the phase starts: shaft turns, or seconds. */
  turns?: number;
  seconds?: number;
  /** At the phase's start: put the chosen prop on, or take it off. */
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
  phases: PlayPhase[];
}

export interface Scenario {
  inputs?: { theta?: InputLabel; depth?: InputLabel; lifting?: InputLabel; reverse?: InputLabel };
  requires?: Record<string, string>;
  prop?: PropSpec;
  play?: PlaySpec;
}

export const DEFAULT_SECONDS_PER_TURN = 1.2;

/** A number from the rig by dotted path, or the number itself. */
export function rigNumber(rig: Rig, ref: number | string): number {
  if (typeof ref === "number") return ref;
  let v: unknown = rig;
  for (const k of ref.split(".")) v = typeof v === "object" && v !== null ? (v as Record<string, unknown>)[k] : undefined;
  if (typeof v !== "number" || !Number.isFinite(v)) throw new Error(`rig has no number at "${ref}"`);
  return v;
}

/** Where the prop sits: centred on the line's origin, along its axis, its underside on it; blocks. */
export function propBox(option: PropOption, line: Extract<Anchor, { kind: "line" }>): { centre: Vec3; size: Vec3 } {
  const [length, width, height] = option.size;
  const o = line.origin;
  const size: Vec3 = line.axis === "x" ? [length, height, width] : line.axis === "z" ? [width, height, length] : [width, length, width];
  return { centre: [o[0], o[1] + size[1] / 2, o[2]], size };
}

/** The depth at which the edge, moving from edge.top at depth 0 to edge.bottom at depth 1, reaches `height`, clamped to 0..1. */
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
  /** Depth when the phase started. */
  phaseFrom: number;
  /** Whether the prop is on (Play loads and clears it). */
  propOn: boolean;
}

/** The phase Play starts in from a pose; null when there is no script. */
export function startPhase(play: PlaySpec | undefined, m: Pick<Motion, "depth" | "lifting">, propChosen: boolean): string | null {
  if (!play || play.phases.length === 0) return null;
  for (const p of play.phases) {
    const s = p.startIf;
    if (!s) continue;
    if (s.lifting !== undefined && (m.lifting > 0.5 ? 1 : 0) !== s.lifting) continue;
    if (s.prop !== undefined && s.prop !== propChosen) continue;
    if (s.depthAtMost !== undefined && m.depth > s.depthAtMost) continue;
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
}

/** Enters a phase: sets lifting and the prop, and remembers where it started. */
export function enterPhase(ctx: PlayContext, m: Motion, id: string): Motion {
  const p = ctx.play?.phases.find((q) => q.id === id);
  if (!p) return { ...m, phase: null };
  return {
    ...m,
    phase: id,
    phaseFrom: m.depth,
    lifting: p.lifting ?? 0,
    propOn: p.prop === "load" ? ctx.propChosen : p.prop === "clear" ? false : m.propOn,
  };
}

/** Advances the motion by `dt` seconds of Play: the shaft turns, and the script, if any, moves depth from phase to phase. */
export function advance(ctx: PlayContext, m: Motion, dt: number): Motion {
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
    const target = p.to === "contact" ? ctx.contact : p.to;
    const span = Math.abs(target - next.phaseFrom);
    const remaining = Math.abs(target - next.depth);
    // Depth per turn or per second, over the phase's whole span.
    const rate = p.turns ? span / p.turns : p.seconds ? span / p.seconds : Infinity;
    const budget = p.turns ? turns : seconds;
    const step = rate === Infinity ? Infinity : rate * budget;
    if (step < remaining) {
      next = { ...next, depth: next.depth + Math.sign(target - next.depth) * step };
      return next;
    }
    // Reached the target: spend what is left of this frame in the next phase.
    const used = rate === Infinity || rate === 0 ? 0 : remaining / rate;
    if (p.turns) {
      turns = Math.max(0, turns - used);
      seconds = turns * secondsPerTurn;
    } else {
      seconds = Math.max(0, seconds - used);
      turns = seconds / secondsPerTurn;
    }
    next = enterPhase(ctx, { ...next, depth: target }, ctx.propChosen ? p.next : (p.nextWithoutProp ?? p.next));
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
  if (s.prop) {
    const line = anchors.find((a) => a.kind === "line" && a.key === s.prop!.on);
    if (!line) out.push(`prop.on "${s.prop.on}" is not a line anchor ({ origin, axis, length }) of the rig`);
    const ids = new Set<string>();
    for (const o of s.prop.options ?? []) {
      if (ids.has(o.id) || o.id === "none") out.push(`prop option "${o.id}" is listed twice or is reserved`);
      ids.add(o.id);
      if (!Array.isArray(o.size) || o.size.length !== 3 || !o.size.every((n) => typeof n === "number" && n > 0))
        out.push(`prop option "${o.id}" needs a size of three positive numbers`);
    }
    if (s.prop.default !== undefined && s.prop.default !== "none" && !ids.has(s.prop.default)) out.push(`prop.default "${s.prop.default}" is not an option`);
  }
  if (s.play) {
    if (!rig) out.push("play needs a rig");
    const ids = new Set(s.play.phases.map((p) => p.id));
    if (ids.size !== s.play.phases.length) out.push("play phase ids must be unique");
    for (const p of s.play.phases) {
      for (const n of [p.next, p.nextWithoutProp]) if (n !== undefined && !ids.has(n)) out.push(`play phase "${p.id}" goes to "${n}", which is not a phase`);
      if (p.to !== "contact" && (typeof p.to !== "number" || p.to < 0 || p.to > 1)) out.push(`play phase "${p.id}": to must be 0..1 or "contact"`);
      if (!(p.turns! > 0) && !(p.seconds! > 0)) out.push(`play phase "${p.id}" needs turns or seconds above 0`);
      if (p.to === "contact" && !s.prop) out.push(`play phase "${p.id}" runs to "contact", which needs a prop`);
    }
    if (s.play.phases.some((p) => p.to === "contact")) {
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
