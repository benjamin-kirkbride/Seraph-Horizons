// A model that is a vehicle on a track (the handcar, and the tracked vehicles after it), given as
// the scenario's `vehicle` in site/models.json: θ is its axle's angle, growing as it rolls towards
// its front, and the wheel's radius turns that into distance. With it the page shows the distance
// rolled and the speed in blocks a second, Play starts at the vehicle's speed, a stretch of track
// under it scrolls by the distance rolled (the vehicle stays put), and its bogies (a shape of their
// own, which the renderer draws along the car) are added to the model. Pure, so the page, the scene
// and the tests read the same thing. docs/recipe-browser/models.md describes the format.
import type { Bounds } from "./model-anchors.ts";
import { rigNumber } from "./model-scenario.ts";
import { rigInputs, type Axis, type Rig, type RigPart, type Shape, type ShapeElement, type Vec3 } from "./rig.ts";

/** Where the vehicle's front faces in its model frame: the axis it runs along, and which way. */
export type Front = "-x" | "+x" | "-z" | "+z";

export interface TrackSpec {
  /** Rail centre to rail centre, blocks. */
  gauge: number;
  /** The track's centre line across the axis it runs along (z for a car along x), blocks. */
  centre: number;
  /** The rails' top, blocks (default 0). */
  top?: number;
  /** Sleeper centre to sleeper centre along the track, blocks (default 0.5). */
  sleeperSpacing?: number;
  /** A sleeper's length across the track, blocks (default the gauge and 0.75). */
  sleeperLength?: number;
}

export interface BogieSpec {
  /** Each bogie's place along the axis, blocks: numbers or rig paths ("bogies.front"). */
  at: (number | string)[];
  /** Added to every place: Yang's body offset ("bogies.bodyOffsetForward"); default 0. */
  offset?: number | string;
}

export interface VehicleSpec {
  /** The wheels' radius, blocks: a number or a rig path ("cycle.wheelRadius"). */
  wheelRadius: number | string;
  front: Front;
  /** What one cycle of the rig's θ is called ("stroke"), for the readout. */
  cycle?: string;
  /** Play's starting speed, blocks a second; it overrides play.secondsPerTurn. */
  speed?: number;
  track?: TrackSpec;
  /** Drawn from the manifest's `bogie` shape, one copy at each place. */
  bogies?: BogieSpec;
}

/** A vehicle with its rig paths read. */
export interface Vehicle {
  wheelRadius: number;
  axis: Axis;
  /** +1 when the front faces +axis, −1 when it faces −axis. */
  frontSign: 1 | -1;
  cycle: string | null;
  speed: number | null;
  track: TrackSpec | null;
  /** Each bogie's place along the axis, blocks. */
  bogies: { name: string; at: number }[];
}

export const FRONTS: readonly Front[] = ["-x", "+x", "-z", "+z"];
/** The bogies' part, added ahead of the rig's own so that its glob claims their elements first. */
export const BOGIE_PART = "bogies";
const BOGIE_PREFIX = "bogie_";
/** The overlay the track is drawn in (no rig key can be named so). */
export const TRACK_OVERLAY = "vehicle:track";

/** The vehicle a scenario describes, with its rig paths read; null without one. Throws on a path the rig lacks. */
export function vehicleOf(spec: VehicleSpec | undefined, rig: Rig): Vehicle | null {
  if (!spec) return null;
  const offset = spec.bogies?.offset !== undefined ? rigNumber(rig, spec.bogies.offset) : 0;
  return {
    wheelRadius: rigNumber(rig, spec.wheelRadius),
    axis: spec.front[1] as Axis,
    frontSign: spec.front[0] === "-" ? -1 : 1,
    cycle: spec.cycle ?? null,
    speed: spec.speed ?? null,
    track: spec.track ?? null,
    bogies: (spec.bogies?.at ?? []).map((ref, i) => ({
      // A rig path names its bogie by its last key ("bogies.front" is front); a number by its place.
      name: typeof ref === "string" ? ref.split(".").pop()! : String(i + 1),
      at: offset + rigNumber(rig, ref),
    })),
  };
}

/** Problems with a scenario's vehicle against its rig and the manifest's bogie shape, as messages. */
export function checkVehicle(spec: VehicleSpec, rig: Rig, hasBogieShape: boolean): string[] {
  const out: string[] = [];
  if (!FRONTS.includes(spec.front)) out.push(`vehicle.front must be one of ${FRONTS.join(", ")}`);
  if (!rigInputs(rig.parts ?? []).theta) out.push("vehicle needs a rig whose drivers read θ, the axle's angle");
  const positive = (what: string, v: unknown) => {
    if (typeof v !== "number" || !(v > 0) || !Number.isFinite(v)) out.push(`vehicle.${what} must be above 0`);
  };
  try {
    positive("wheelRadius", rigNumber(rig, spec.wheelRadius));
  } catch (e) {
    out.push(`vehicle.wheelRadius: ${(e as Error).message}`);
  }
  if (spec.speed !== undefined) positive("speed", spec.speed);
  if (spec.track) {
    positive("track.gauge", spec.track.gauge);
    if (typeof spec.track.centre !== "number") out.push("vehicle.track.centre must be a number");
    if (spec.track.top !== undefined && typeof spec.track.top !== "number") out.push("vehicle.track.top must be a number");
    if (spec.track.sleeperSpacing !== undefined) positive("track.sleeperSpacing", spec.track.sleeperSpacing);
    if (spec.track.sleeperLength !== undefined) positive("track.sleeperLength", spec.track.sleeperLength);
  }
  if (spec.bogies) {
    if (!hasBogieShape) out.push("vehicle.bogies needs the model's bogie shape");
    if (!Array.isArray(spec.bogies.at) || spec.bogies.at.length === 0) out.push("vehicle.bogies.at needs at least one place");
    for (const ref of [...(spec.bogies.at ?? []), ...(spec.bogies.offset !== undefined ? [spec.bogies.offset] : [])])
      try {
        rigNumber(rig, ref);
      } catch (e) {
        out.push(`vehicle.bogies: ${(e as Error).message}`);
      }
  } else if (hasBogieShape) out.push("the model's bogie shape needs vehicle.bogies to place it");
  return out;
}

/**
 * The model with its bogies: a copy of the bogie shape at each place along the axis (its elements
 * named bogie_<name>_<element>, moved there; children are in their parent's frame), the textures
 * merged (the body's win), and the rig given a static part, `bogies`, ahead of its own.
 */
export function withBogies(shape: Shape, rig: Rig, bogie: Shape | null, vehicle: Vehicle | null): { shape: Shape; rig: Rig } {
  if (!bogie || !vehicle || vehicle.bogies.length === 0) return { shape, rig };
  const k = vehicle.axis === "x" ? 0 : 2;
  const moved = (e: ShapeElement, by: number, name: string): ShapeElement => {
    const shift = (v: Vec3): Vec3 => v.map((c, i) => (i === k ? c + by : c)) as Vec3;
    return { ...e, name, from: shift(e.from), to: shift(e.to), ...(e.rotationOrigin ? { rotationOrigin: shift(e.rotationOrigin) } : {}) };
  };
  const elements = [...shape.elements];
  for (const b of vehicle.bogies) for (const e of bogie.elements) elements.push(moved(e, b.at * 16, `${BOGIE_PREFIX}${b.name}_${e.name}`));
  const part: RigPart = { id: BOGIE_PART, match: [`${BOGIE_PREFIX}*`], requires: null, drivers: [] };
  return { shape: { ...shape, textures: { ...bogie.textures, ...shape.textures }, elements }, rig: { ...rig, parts: [part, ...(rig.parts ?? [])] } };
}

/** How far the vehicle has rolled for a change of θ: towards its front when positive, blocks. */
export function rolledBy(dTheta: number, vehicle: Vehicle): number {
  return dTheta * vehicle.wheelRadius;
}

/** Axle turns a second for a speed in blocks a second. */
export function turnsPerSecondAt(blocksPerSecond: number, vehicle: Vehicle): number {
  return blocksPerSecond / (2 * Math.PI * vehicle.wheelRadius);
}

/** Blocks a second at an axle speed in turns a second. */
export function blocksPerSecondAt(turnsPerSecond: number, vehicle: Vehicle): number {
  return turnsPerSecond * 2 * Math.PI * vehicle.wheelRadius;
}

export interface TrackBox {
  centre: Vec3;
  size: Vec3;
}

/** The track as boxes, in blocks: two rails and the sleepers, which scroll along the track by `scroll`. */
export interface TrackLayout {
  rails: TrackBox[];
  sleepers: TrackBox[];
  axis: Axis;
  spacing: number;
}

const RAIL_WIDTH = 0.09;
const RAIL_HEIGHT = 0.11;
const SLEEPER_WIDTH = 0.16;
const SLEEPER_HEIGHT = 0.08;
/** Track beyond the model at each end, blocks. */
const TRACK_MARGIN = 5;

/**
 * The track under the model: rails a gauge apart along the vehicle's axis, from end to end of the
 * model's bounds and TRACK_MARGIN beyond, with sleepers under them every spacing, one more at each
 * end so that scrolling them by up to a spacing either way leaves no gap.
 */
export function trackLayout(track: TrackSpec, vehicle: Vehicle, bounds: Bounds): TrackLayout {
  const k = vehicle.axis === "x" ? 0 : 2;
  const across = k === 0 ? 2 : 0;
  const top = track.top ?? 0;
  const spacing = track.sleeperSpacing ?? 0.5;
  const sleeperLength = track.sleeperLength ?? track.gauge + 0.75;
  const lo = bounds.lo[k]! - TRACK_MARGIN;
  const hi = bounds.hi[k]! + TRACK_MARGIN;
  const box = (along: number, length: number, acrossAt: number, width: number, y: number, height: number): TrackBox => {
    const centre: Vec3 = [0, y, 0];
    const size: Vec3 = [0, height, 0];
    centre[k] = along;
    size[k] = length;
    centre[across] = acrossAt;
    size[across] = width;
    return { centre, size };
  };
  const rails = [-1, 1].map((side) => box((lo + hi) / 2, hi - lo, track.centre + (side * track.gauge) / 2, RAIL_WIDTH, top - RAIL_HEIGHT / 2, RAIL_HEIGHT));
  const sleepers: TrackBox[] = [];
  const first = Math.floor(lo / spacing) - 1;
  for (let i = first; i * spacing <= hi + spacing; i++)
    sleepers.push(box(i * spacing, SLEEPER_WIDTH, track.centre, sleeperLength, top - RAIL_HEIGHT - SLEEPER_HEIGHT / 2, SLEEPER_HEIGHT));
  return { rails, sleepers, axis: vehicle.axis, spacing };
}

/**
 * How far the sleepers are shifted along the axis, in [0, spacing) towards the vehicle's rear (or
 * (−spacing, 0] when the rear is −axis): as the vehicle rolls forward the track goes by backwards.
 */
export function trackScroll(rolled: number, spacing: number, vehicle: Vehicle): number {
  const m = ((rolled % spacing) + spacing) % spacing;
  return -vehicle.frontSign * m;
}
