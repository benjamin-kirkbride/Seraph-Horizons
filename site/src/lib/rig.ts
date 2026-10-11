// Shape posing and rig maths for the model viewer, as pure functions. A port of the C# in
// mods-src/seraphhorizons/Machines/Core/RigAnimation.cs and of the reference maths in
// mods-src/seraphhorizons/Machines/tools/machinegen/ (driver_matrix, part_matrix, flatten);
// test/rig.test.ts holds all three to the poses in
// mods-src/seraphhorizons/tests/BuckingSawmill/rig-reference.json, and the trunk-path inputs and
// drivers (input, gauge, roll) to mods-src/seraphhorizons/tests/Machines/driver-fixture.json;
// test/rosser.test.ts holds them to mods-src/seraphhorizons/tests/Rosser/rig-reference.json.
// The rig format is documented in mods-src/seraphhorizons/BuckingSawmill/README.md ("Rig schema"),
// the trunk-path additions in mods-src/seraphhorizons/Rosser/README.md and docs/recipe-browser/models.md.
//
// Matrices are 16 numbers in column-major order (translation in 12..14), the layout of the
// game's Mat4f and of three.js's Matrix4.elements; points transform as M·p. Shapes are in
// voxels (16 per block), rigs in blocks.

export type Vec3 = [number, number, number];
export type Mat4 = number[];
export type Axis = "x" | "y" | "z";

// ---- Vintage Story shapes

export interface ShapeFace {
  texture?: string;
  enabled?: boolean;
  [key: string]: unknown;
}

export interface ShapeElement {
  name: string;
  from: Vec3;
  to: Vec3;
  rotationOrigin?: Vec3;
  rotationX?: number;
  rotationY?: number;
  rotationZ?: number;
  /** The element's scale about its rotation origin (1 when not given); no generated shape has one. */
  scaleX?: number;
  scaleY?: number;
  scaleZ?: number;
  faces?: Partial<Record<FaceName, ShapeFace>>;
  children?: ShapeElement[];
}

export interface Shape {
  textures?: Record<string, string>;
  elements: ShapeElement[];
  /** Keyframe animations (keyframes.ts reads them). */
  animations?: unknown;
}

export const FACE_NAMES = ["north", "east", "south", "west", "up", "down"] as const;
export type FaceName = (typeof FACE_NAMES)[number];

// ---- rigs

/** What rotate, slide and swing read in place of θ: θ, ψ (the shaft's travel), φ (the feed's travel), W (the work; "trunk" is its trunk-flavoured spelling) or the oil tank's fill. */
export type DriverInput = "theta" | "travel" | "feed" | "work" | "trunk" | "oil";

/** What a stretch reads: the depth (the default) or the oil tank's fill. */
export type StretchInput = "depth" | "oil";

/**
 * A number per class, keyed by class: "thin" (1), "thick" (2), and from 3 on the class's number ("3" .. "9").
 * It names one class or more (a window's gain may name none); a class it does not name has none of it
 * (amount and amplitude 0, gain 1, a work's end 0). Class 0, none, never has any.
 */
export type PerClass = Partial<Record<string, number>>;

/** A gauge's window along the trunk path, in blocks: occupied while any part of the trunk lies over [from, to]. */
export interface GaugeWindow {
  from: number;
  to: number;
  ease: number;
  /** Per class, each 1 when not given. */
  gain?: Partial<PerClass>;
}

/** A gauge's four-lobe term: the rounded-square trunk turning under a part that rides it. */
export interface GaugeLobes {
  ratio: number;
  phase?: number;
  amplitude: PerClass;
}

export interface Driver {
  type: "rotate" | "slide" | "swing" | "feed" | "step" | "stretch" | "gauge" | "roll";
  axis: Axis;
  pivot?: Vec3;
  anchor?: Vec3;
  ratio?: number;
  rectified?: boolean;
  /** rotate, slide, swing: a DriverInput; stretch: a StretchInput. */
  input?: DriverInput | StretchInput;
  amplitude?: number;
  phase?: number;
  travel?: number;
  /** step: slide or rotate; gauge: slide, rotate or stretch. */
  motion?: "slide" | "rotate" | "stretch";
  /** A stretching gauge: the point (blocks, authored frame) the plane through which, normal to its axis, stays put. */
  origin?: Vec3;
  /** A stretching gauge: the frame its axis is in, [rx, ry, rz] degrees turned Rx·Ry·Rz (an element's rotationX/Y/Z); the authored frame without one. */
  rotation?: Vec3;
  /** A number for step; per class for gauge. */
  amount?: number | PerClass;
  from?: number;
  to?: number;
  lifting?: "hold" | "block" | "trip";
  top?: number;
  length?: number;
  mode?: "occupy" | "present";
  windows?: GaugeWindow[];
  lobes?: GaugeLobes;
  /** roll: the roller's place along the trunk path, blocks. */
  at?: number;
}

export interface RigPart {
  id: string;
  match: string[];
  requires?: string | null;
  ride?: string | null;
  drivers?: Driver[];
}

export interface RigCell {
  pos: Vec3;
  boxes?: number[][] | null;
  /** A ghost with nothing of its own to collide with (a trunk path's cell); without boxes it has none, not a full cube. */
  hollow?: boolean;
  /** On the top cell of a column: the cell-local height of the top of a collision-only box over the whole cell, 1/16 thick, so the machine's top walks as a deck. Not selectable; the viewer draws it with the collision boxes, in its own shade. */
  lid?: number;
}

/** rig.json: `parts` and `cells` are read as such; every other key is an anchor (model-anchors.ts). */
export interface Rig {
  cells?: RigCell[];
  parts?: RigPart[];
  [key: string]: unknown;
}

/** The rig's inputs: θ the signed shaft angle (radians), depth 0..1, lifting 0..1, and ψ
 * the shaft's travel, the total angle it has turned either way (|θ| when not given). A machine
 * a trunk travels through adds T, the trunk's travel along the rig's trunkPath (blocks), its
 * class (`size`: 0 none, 1 thin, 2 thick), its presence p (0..1, eased as it is loaded and taken
 * away) and φ, the feed's travel (radians: ψ counted only while the feed runs); a machine that shows
 * its oil adds `oil`, how full its tank is (0..1). Each is 0 when not given. */
export interface Pose {
  theta: number;
  depth: number;
  lifting: number;
  travel?: number;
  /** W, the work's progress in the rig's unit (`work`, or a trunk's travel on its trunkPath). */
  work?: number;
  /** The trunk-flavoured spelling of `work`, read when `work` is not given. */
  trunk?: number;
  size?: number;
  presence?: number;
  feed?: number;
  oil?: number;
}

/** The named classes after 0 (none), by index − 1: a trunk's two sizes, and the first two classes of any work. */
export const TRUNK_CLASSES = ["thin", "thick"] as const;
export type TrunkClass = (typeof TRUNK_CLASSES)[number];

/** The last class: classes 3 .. MAX_CLASS are keyed by their number, "3" .. "9" (the mandrel station's spring is 3). */
export const MAX_CLASS = 9;

/** Class k's per-class key ("thin", "thick", "3" .. "9"), or null for none or a class out of range. */
export function classKey(k: number): string | null {
  if (!Number.isInteger(k) || k < 1 || k > MAX_CLASS) return null;
  return k <= TRUNK_CLASSES.length ? TRUNK_CLASSES[k - 1]! : String(k);
}

/** The class a per-class key names (1 .. MAX_CLASS), or null for any other key ("1", "03", "10", "medium"). */
export function classOfKey(key: string): number | null {
  const named = (TRUNK_CLASSES as readonly string[]).indexOf(key);
  if (named >= 0) return named + 1;
  if (!/^[1-9][0-9]*$/.test(key)) return null;
  const k = Number(key);
  return k > TRUNK_CLASSES.length && k <= MAX_CLASS ? k : null;
}

/** Class k's value in a per-class object, `missing` when it names none for k (or k is none). */
export function perClassValue(v: PerClass | undefined, k: number, missing = 0): number {
  const key = classKey(k);
  const x = key === null || v === undefined ? undefined : v[key];
  return x === undefined ? missing : x;
}

/** Checks a per-class object (keyed by class, each a number, naming a class or more unless `mayBeEmpty`); throws naming `what`. */
export function checkPerClass(v: unknown, what: string, mayBeEmpty = false): PerClass {
  if (typeof v !== "object" || v === null || Array.isArray(v)) throw new Error(`${what} must be an object keyed by class`);
  const keys = Object.keys(v);
  for (const key of keys) {
    if (classOfKey(key) === null) throw new Error(`${what}: "${key}" is not a class (thin, thick, or 3 .. ${MAX_CLASS})`);
    if (!isNum((v as Record<string, unknown>)[key])) throw new Error(`${what}.${key} must be a number`);
  }
  if (keys.length === 0 && !mayBeEmpty) throw new Error(`${what} names no class`);
  return v as PerClass;
}

/**
 * A rig's progress, W: what its gauge windows and rolls are placed on. The rig's `work` is a
 * named quantity in its own unit with an end per class, a point on its own scale (nose = tail = W,
 * so a window is occupied while W is in it); its `trunkPath` is the trunk-flavoured case, a trunk
 * with a length travelling along a line (below). A rig has one or neither.
 */
export interface Work {
  kind: "work" | "trunk";
  /** What W counts ("teeth cut"); "trunk travel" for a trunkPath. */
  name: string;
  /** W's unit ("teeth"); "blocks" for a trunkPath. */
  unit: string;
  /** A slider's step; 1/16 for a trunkPath. */
  step: number;
  /** The leading point at W = 0: 0 for work, a trunkPath's nose0. */
  nose0: number;
  /** L_k, indexed by class 0 .. MAX_CLASS: [0, thin, thick, 0, ...] (a trunk has the two classes); 0 for work. */
  lengths: number[];
  /** Where W ends, indexed by class 0 .. MAX_CLASS ([0] 0): a work's per class (0 for a class it has no end for). */
  ends: number[];
}

/**
 * The rig's `trunkPath`: the line a trunk travels along, nose first, towards + on its axis.
 * Places along it (nose0, tailStop, windows, stations, a roll's `at`) are coordinates on that
 * axis in the rig's frame, in blocks. At travel T the nose is at nose0 + T and the tail L_k
 * behind it; T runs from 0 to end(k) = tailStop + L_k − nose0.
 */
export interface TrunkPath extends Work {
  kind: "trunk";
  origin: Vec3;
  axis: Axis;
  length: number;
  tailStop: number;
  stations: Record<string, number>;
}

const isNum = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);
/** A trunkPath's lengths: thin and thick numbers (a trunk's two sizes). */
const isTrunkLengths = (v: unknown): v is { thin: number; thick: number } =>
  typeof v === "object" && v !== null && isNum((v as Record<string, unknown>).thin) && isNum((v as Record<string, unknown>).thick);
/** An array indexed by class 0 .. MAX_CLASS from `at(k)` for the classes ([0] 0). */
const byClass = (at: (k: number) => number): number[] => Array.from({ length: MAX_CLASS + 1 }, (_, k) => (k === 0 ? 0 : at(k)));

/** The rig's trunkPath, or null when it has none; throws when it has one that is malformed. */
export function trunkPathOf(rig: Rig | null | undefined): TrunkPath | null {
  const raw = rig?.trunkPath as Record<string, unknown> | undefined;
  if (raw === undefined) return null;
  const problem = (what: string) => new Error(`trunkPath: ${what}`);
  if (typeof raw !== "object" || raw === null) throw problem("must be an object");
  const { origin, axis, length, nose0, lengths, tailStop, stations } = raw;
  if (!Array.isArray(origin) || origin.length !== 3 || !origin.every(isNum)) throw problem("origin must be 3 numbers");
  if (axis !== "x" && axis !== "y" && axis !== "z") throw problem('axis must be "x", "y" or "z"');
  if (!isNum(length) || length <= 0) throw problem("length must be a number above 0");
  if (!isNum(nose0)) throw problem("nose0 must be a number");
  if (!isNum(tailStop)) throw problem("tailStop must be a number");
  if (!isTrunkLengths(lengths) || lengths.thin <= 0 || lengths.thick <= 0) throw problem("lengths needs thin and thick above 0");
  const st: Record<string, number> = {};
  if (stations !== undefined) {
    if (typeof stations !== "object" || stations === null || Array.isArray(stations)) throw problem("stations must map names to numbers");
    for (const [k, v] of Object.entries(stations)) {
      if (!isNum(v)) throw problem(`station "${k}" must be a number`);
      st[k] = v;
    }
  }
  const ls = byClass((k) => (k === 1 ? lengths.thin : k === 2 ? lengths.thick : 0));
  return {
    kind: "trunk",
    name: "trunk travel",
    unit: "blocks",
    step: 1 / 16,
    origin: origin as Vec3,
    axis,
    length,
    nose0,
    lengths: ls,
    ends: byClass((k) => tailStop + ls[k]! - nose0),
    tailStop,
    stations: st,
  };
}

/** The rig's `work` as a Work, or null when it has none; throws when it is malformed. */
export function workQuantityOf(rig: Rig | null | undefined): Work | null {
  const raw = rig?.work as Record<string, unknown> | undefined;
  if (raw === undefined) return null;
  const problem = (what: string) => new Error(`work: ${what}`);
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) throw problem("must be an object");
  const { name, unit, step, end } = raw;
  if (typeof unit !== "string" || unit === "") throw problem("needs a unit");
  if (name !== undefined && (typeof name !== "string" || name === "")) throw problem("name must be a non-empty string");
  if (step !== undefined && !(isNum(step) && step > 0)) throw problem("step must be above 0");
  let ends: PerClass;
  try {
    ends = checkPerClass(end, "end");
  } catch (e) {
    throw problem((e as Error).message);
  }
  for (const [key, v] of Object.entries(ends)) if (!(v! > 0)) throw problem(`end.${key} must be above 0`);
  for (const key of ["nose0", "lengths", "tailStop"]) if (key in raw) throw problem(`has no ${key}: that is a trunkPath's`);
  return {
    kind: "work",
    name: (name as string | undefined) ?? unit,
    unit,
    step: (step as number | undefined) ?? 1 / 16,
    nose0: 0,
    lengths: byClass(() => 0),
    ends: byClass((k) => perClassValue(ends, k)),
  };
}

/** The rig's progress: its `work`, its `trunkPath`, or null; a rig with both is refused. */
export function workOf(rig: Rig | null | undefined): Work | null {
  if (rig?.work !== undefined && rig?.trunkPath !== undefined) throw new Error("a rig has work or a trunkPath, not both");
  return workQuantityOf(rig) ?? trunkPathOf(rig);
}

/** W at a pose: `work`, else the trunk-flavoured `trunk`, else 0. */
export function workAt(pose: Pose): number {
  return pose.work ?? pose.trunk ?? 0;
}

/** A pose's class, rounded: 1 .. MAX_CLASS, or 0 (none) for anything else (as the C# and the Python have it). */
export function classIndex(size: number | undefined): number {
  const k = Math.round(size ?? 0);
  return k >= 1 && k <= MAX_CLASS ? k : 0;
}

/** Where the nose is at travel T. */
export function noseAt(path: Work, work: number): number {
  return path.nose0 + work;
}

/** Where W ends for a class (a trunk's, the tail at tailStop); 0 for no class. */
export function workEnd(path: Work, size: number): number {
  const k = classIndex(size);
  return k === 0 ? 0 : (path.ends[k] ?? 0);
}

/** The trunk-flavoured name of workEnd. */
export const tripEnd = workEnd;

// ---- matrices

export function identity(): Mat4 {
  return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}

export function translation(x: number, y: number, z: number): Mat4 {
  const m = identity();
  m[12] = x;
  m[13] = y;
  m[14] = z;
  return m;
}

/** a·b: b applies first. */
export function multiply(a: Mat4, b: Mat4): Mat4 {
  const r = new Array<number>(16);
  for (let col = 0; col < 4; col++)
    for (let row = 0; row < 4; row++) {
      let s = 0;
      for (let k = 0; k < 4; k++) s += a[k * 4 + row]! * b[col * 4 + k]!;
      r[col * 4 + row] = s;
    }
  return r;
}

/** A right-handed rotation about a principal axis through the origin (VS's Mat4f.RotateX/Y/Z). */
export function rotation(axis: Axis, radians: number): Mat4 {
  const c = Math.cos(radians);
  const s = Math.sin(radians);
  const m = identity();
  if (axis === "x") {
    m[5] = c;
    m[9] = -s;
    m[6] = s;
    m[10] = c;
  } else if (axis === "y") {
    m[0] = c;
    m[8] = s;
    m[2] = -s;
    m[10] = c;
  } else {
    m[0] = c;
    m[4] = -s;
    m[1] = s;
    m[5] = c;
  }
  return m;
}

/** `m` applied about `pivot` instead of the origin. */
export function about(m: Mat4, pivot: Vec3): Mat4 {
  return multiply(translation(pivot[0], pivot[1], pivot[2]), multiply(m, translation(-pivot[0], -pivot[1], -pivot[2])));
}

export function apply(m: Mat4, p: Vec3): Vec3 {
  return [
    m[0]! * p[0] + m[4]! * p[1] + m[8]! * p[2] + m[12]!,
    m[1]! * p[0] + m[5]! * p[1] + m[9]! * p[2] + m[13]!,
    m[2]! * p[0] + m[6]! * p[1] + m[10]! * p[2] + m[14]!,
  ];
}

/** A matrix in blocks to the same motion in voxels: the translation times 16. */
export function toVoxels(m: Mat4): Mat4 {
  const r = m.slice();
  r[12] = m[12]! * 16;
  r[13] = m[13]! * 16;
  r[14] = m[14]! * 16;
  return r;
}

const AXIS_INDEX: Record<Axis, number> = { x: 0, y: 1, z: 2 };

function along(axis: Axis, offset: number): Mat4 {
  return translation(axis === "x" ? offset : 0, axis === "y" ? offset : 0, axis === "z" ? offset : 0);
}

// ---- shapes

/** An element's own transform: Rx·Ry·Rz (degrees), then its scale, about its rotationOrigin, the order VS applies them in (ShapeElement.GetLocalTransformMatrix). */
export function elementLocal(e: ShapeElement): Mat4 {
  const o = e.rotationOrigin ?? [0, 0, 0];
  const deg = Math.PI / 180;
  let r = multiply(rotation("x", (e.rotationX ?? 0) * deg), multiply(rotation("y", (e.rotationY ?? 0) * deg), rotation("z", (e.rotationZ ?? 0) * deg)));
  const sx = e.scaleX ?? 1;
  const sy = e.scaleY ?? 1;
  const sz = e.scaleZ ?? 1;
  if (sx !== 1 || sy !== 1 || sz !== 1) {
    const s = identity();
    s[0] = sx;
    s[5] = sy;
    s[10] = sz;
    r = multiply(r, s);
  }
  return about(r, o);
}

export interface FlatElement {
  element: ShapeElement;
  /** Position in the depth-first walk of the shape, which is also its index in flattenShape's result. */
  index: number;
  /** Element-local voxels (its `from`..`to` box) to model voxels. */
  world: Mat4;
  /** The names of its ancestors, then its own. */
  chain: string[];
}

/** Bakes VS's hierarchy, where a child's coordinates are relative to its parent's `from`. */
export function flattenShape(elements: readonly ShapeElement[]): FlatElement[] {
  const out: FlatElement[] = [];
  const walk = (els: readonly ShapeElement[], parent: Mat4, chain: string[]) => {
    for (const e of els) {
      const world = multiply(parent, elementLocal(e));
      const own = [...chain, e.name];
      out.push({ element: e, index: out.length, world, chain: own });
      if (e.children?.length) walk(e.children, multiply(world, translation(e.from[0], e.from[1], e.from[2])), own);
    }
  };
  walk(elements, identity(), []);
  return out;
}

/** The eight corners of an element's box in model voxels: corner i takes `to` on x when bit 0 is set, y bit 1, z bit 2. */
export function corners(f: FlatElement, m: Mat4 = f.world): Vec3[] {
  const { from: a, to: b } = f.element;
  const out: Vec3[] = [];
  for (let i = 0; i < 8; i++) out.push(apply(m, [(i & 1 ? b : a)[0], (i & 2 ? b : a)[1], (i & 4 ? b : a)[2]]));
  return out;
}

/** The texture codes an element's faces use, without the `#`, sorted; faces with `enabled: false` do not count. */
export function textureCodes(e: ShapeElement): string[] {
  const set = new Set<string>();
  for (const face of Object.values(e.faces ?? {})) {
    if (!face || face.enabled === false) continue;
    set.add(typeof face.texture === "string" ? face.texture.replace(/^#/, "") : "?");
  }
  return [...set].sort();
}

// ---- parts

/** A case-sensitive glob where `*` matches any run of characters. */
export function globRegExp(pattern: string): RegExp {
  return new RegExp("^" + pattern.replace(/[.+?^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*") + "$");
}

/** Compiled globs, one list per part. */
export function compileGlobs(parts: readonly RigPart[]): RegExp[][] {
  return parts.map((p) => p.match.map(globRegExp));
}

/** The first part with a glob matching any name in an element's chain, or -1. */
export function partOf(globs: readonly RegExp[][], chain: readonly string[]): number {
  for (let i = 0; i < globs.length; i++) for (const g of globs[i]!) for (const n of chain) if (g.test(n)) return i;
  return -1;
}

/** Whether a part needing `requires` is drawn: null is always, anything else when it is ticked. */
export function fitted(requires: string | null | undefined, ticked: Readonly<Record<string, boolean>>): boolean {
  return requires == null || ticked[requires] === true;
}

// ---- drivers

/** A step driver's fraction (the README's e): d's progress through [from, to], then the lifting gate. */
export function stepFraction(d: Driver, depth: number, lifting: number): number {
  const lo = d.from ?? 0;
  const hi = d.to ?? 1;
  const e = Math.min(1, Math.max(0, (depth - lo) / (hi - lo)));
  switch (d.lifting) {
    case "hold":
      return Math.max(e, lifting);
    case "block":
      return e * (1 - lifting);
    case "trip":
      return e * (1 - lifting) + ((d.top ?? 0) > 0 ? Math.min(1, Math.max(0, depth / d.top!)) : 1) * lifting;
    default:
      return e;
  }
}

function point(d: Driver, key: "pivot" | "anchor"): Vec3 {
  const p = d[key];
  if (!Array.isArray(p) || p.length !== 3) throw new Error(`a ${d.type} driver needs a ${key} of 3 numbers`);
  return p;
}

const INPUTS: readonly string[] = ["theta", "travel", "feed", "work", "trunk", "oil"];
const STRETCH_INPUTS: readonly string[] = ["depth", "oil"];

/** The input a rotate, slide or swing driver reads in place of θ; `rectified: true` is the mill's way of saying "travel". */
export function driverInput(d: Driver): Exclude<DriverInput, "trunk"> {
  if (d.input !== undefined && d.rectified !== undefined) throw new Error(`a ${d.type} driver takes input or rectified, not both`);
  if (d.input === undefined) return d.rectified ? "travel" : "theta";
  if (!INPUTS.includes(d.input)) throw new Error(`unknown driver input "${d.input}"`);
  // "trunk" is the trunk-flavoured spelling of "work"
  return (d.input === "trunk" ? "work" : d.input) as Exclude<DriverInput, "trunk">;
}

/** The input a stretch reads: the depth unless it says `input: "oil"`. */
export function stretchInput(d: Driver): StretchInput {
  if (d.rectified !== undefined) throw new Error("rectified is for rotate, slide and swing drivers");
  if (d.input === undefined) return "depth";
  if (!STRETCH_INPUTS.includes(d.input)) throw new Error(`a stretch reads depth or oil, not "${d.input}"`);
  return d.input as StretchInput;
}

function inputValue(d: Driver, pose: Pose): number {
  switch (driverInput(d)) {
    case "travel":
      return pose.travel ?? Math.abs(pose.theta);
    case "feed":
      return pose.feed ?? 0;
    case "work":
      return workAt(pose);
    case "oil":
      return pose.oil ?? 0;
    default:
      return pose.theta;
  }
}

function perClass(d: Driver, v: unknown, key: string): PerClass {
  return checkPerClass(v, `a ${d.type} driver's ${key}`);
}

function needPath(d: Driver, path: Work | null | undefined): Work {
  if (!path) throw new Error(`a ${d.type} driver needs the rig's work or trunkPath`);
  return path;
}

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));

/** A stretching gauge's factor below this is 0: the part vanishes. */
export const STRETCH_EPS = 1e-6;

/** A driver that scales its part (a stretch, a stretching gauge): what a part riding it does not inherit. */
export function isScale(d: Driver): boolean {
  return d.type === "stretch" || (d.type === "gauge" && d.motion === "stretch");
}

const isVec3 = (v: unknown): v is Vec3 => Array.isArray(v) && v.length === 3 && v.every(isNum);

/** A stretching gauge's unit axis in the authored frame: its axis of the frame `rotation` turns to (Rx·Ry·Rz, degrees). */
export function stretchDirection(d: Driver): Vec3 {
  const deg = Math.PI / 180;
  const [rx, ry, rz] = d.rotation ?? [0, 0, 0];
  const r = multiply(rotation("x", rx * deg), multiply(rotation("y", ry * deg), rotation("z", rz * deg)));
  const k = AXIS_INDEX[d.axis];
  return [r[k * 4]!, r[k * 4 + 1]!, r[k * 4 + 2]!];
}

/** A scale by f along the unit vector u about the plane through `origin` normal to it; f 0 collapses everything to `origin`. */
export function scaleAlong(u: Vec3, f: number, origin: Vec3): Mat4 {
  const m = identity();
  if (f === 0) {
    m[0] = m[5] = m[10] = 0;
    m[12] = origin[0];
    m[13] = origin[1];
    m[14] = origin[2];
    return m;
  }
  for (let i = 0; i < 3; i++) for (let j = 0; j < 3; j++) m[j * 4 + i] = (i === j ? 1 : 0) + (f - 1) * u[i]! * u[j]!;
  for (let i = 0; i < 3; i++) m[12 + i] = origin[i]! - (m[i]! * origin[0] + m[4 + i]! * origin[1] + m[8 + i]! * origin[2]);
  return m;
}

/** Whether a matrix collapses everything to a point (its linear part all 0): a part stretched to nothing, drawn as nothing. */
export function collapsed(m: Mat4): boolean {
  return [0, 1, 2, 4, 5, 6, 8, 9, 10].every((i) => m[i] === 0);
}

/**
 * A gauge's engagement e: 0 without a trunk; p when its mode is "present"; else p times
 * the most engaged window's min(1, gain · occupancy), occupancy easing in over `ease` blocks as
 * the nose arrives and out as the tail leaves. A stretching gauge sums its windows' min(1, gain ·
 * occupancy) instead, so a stretch made in steps adds up (and can pass 1).
 */
export function gaugeEngagement(d: Driver, pose: Pose, path: Work | null | undefined): number {
  const mode = d.mode ?? "occupy";
  if (mode !== "occupy" && mode !== "present") throw new Error(`unknown gauge mode "${mode}"`);
  if (mode === "occupy") {
    if (!Array.isArray(d.windows) || d.windows.length === 0) throw new Error("an occupy gauge needs windows");
    for (const w of d.windows) {
      if (!isNum(w.from) || !isNum(w.to) || !isNum(w.ease)) throw new Error("a gauge window needs from, to and ease");
      if (!(w.ease > 0)) throw new Error("a gauge window's ease must be above 0");
      if (!(w.to > w.from)) throw new Error("a gauge window's to must be above its from");
      if (w.gain !== undefined) checkPerClass(w.gain, "a gauge window's gain", true);
    }
  }
  const k = classIndex(pose.size);
  if (k === 0) return 0;
  const p = pose.presence ?? 0;
  if (mode === "present") return p;
  const pt = needPath(d, path);
  const nose = noseAt(pt, workAt(pose));
  const tail = nose - (pt.lengths[k] ?? 0);
  let best = 0;
  let total = 0;
  for (const w of d.windows!) {
    const gain = perClassValue(w.gain, k, 1);
    const occ = clamp01((nose - w.from) / w.ease) * clamp01((w.to - tail) / w.ease);
    best = Math.max(best, Math.min(1, gain * occ));
    total += Math.min(1, gain * occ);
  }
  return p * (d.motion === "stretch" ? total : best);
}

/** One driver's matrix (blocks) at a pose. Gauge and roll drivers read the rig's progress (workOf); a roll needs a trunkPath. */
export function driverMatrix(d: Driver, pose: Pose, path?: Work | null): Mat4 {
  const { depth } = pose;
  const lifting = pose.lifting ?? 0;
  switch (d.type) {
    case "rotate":
      return about(rotation(d.axis, (d.ratio ?? 1) * inputValue(d, pose)), point(d, "pivot"));
    case "swing":
      return about(rotation(d.axis, (d.amplitude ?? 0) * Math.sin((d.ratio ?? 1) * inputValue(d, pose) + (d.phase ?? 0))), point(d, "pivot"));
    case "slide":
      return along(d.axis, (d.amplitude ?? 0) * Math.sin((d.ratio ?? 1) * inputValue(d, pose) + (d.phase ?? 0)));
    case "feed":
      return along(d.axis, (d.travel ?? 0) * depth);
    case "step": {
      if (d.motion !== "slide" && d.motion !== "rotate") throw new Error('a step driver needs a motion, "slide" or "rotate"');
      const e = stepFraction(d, depth, lifting);
      const amount = (d.amount ?? 0) as number;
      return d.motion === "rotate" ? about(rotation(d.axis, amount * e), point(d, "pivot")) : along(d.axis, amount * e);
    }
    case "stretch": {
      const k = AXIS_INDEX[d.axis];
      const length = d.length ?? 0;
      if (length === 0) throw new Error("a stretch driver needs a non-zero length");
      const f = (length + (d.travel ?? 0) * (stretchInput(d) === "oil" ? (pose.oil ?? 0) : depth)) / length;
      const m = identity();
      m[k * 5] = f;
      m[12 + k] = point(d, "anchor")[k]! * (1 - f);
      return m;
    }
    case "gauge": {
      if (d.motion !== "slide" && d.motion !== "rotate" && d.motion !== "stretch")
        throw new Error('a gauge driver needs a motion, "slide", "rotate" or "stretch"');
      const amount = perClass(d, d.amount, "amount");
      if (d.motion === "stretch") {
        if (!(d.axis in AXIS_INDEX)) throw new Error(`a stretching gauge's axis "${d.axis}" is not x, y or z`);
        if (!isVec3(d.origin)) throw new Error("a stretching gauge needs an origin of 3 numbers");
        if (d.rotation !== undefined && !isVec3(d.rotation)) throw new Error("a stretching gauge's rotation must be 3 numbers (degrees)");
      }
      if (d.lobes !== undefined) {
        if (d.motion !== "rotate") throw new Error(`a ${d.motion} gauge cannot have lobes`);
        if (!isNum(d.lobes.ratio)) throw new Error("a gauge's lobes need a ratio");
        perClass(d, d.lobes.amplitude, "lobes.amplitude");
      }
      const pivot = d.motion === "rotate" ? point(d, "pivot") : null;
      const e = gaugeEngagement(d, pose, path);
      const k = classIndex(pose.size);
      let a = 0;
      if (k > 0) {
        a = perClassValue(amount, k) * e;
        if (d.lobes) a += e * perClassValue(d.lobes.amplitude, k) * Math.cos(d.lobes.ratio * (pose.travel ?? Math.abs(pose.theta)) + (d.lobes.phase ?? 0));
      }
      if (d.motion === "stretch") {
        const f = 1 + a;
        return scaleAlong(stretchDirection(d), f < STRETCH_EPS ? 0 : f, d.origin!);
      }
      return pivot ? about(rotation(d.axis, a), pivot) : along(d.axis, a);
    }
    case "roll": {
      const pivot = point(d, "pivot");
      if (!isNum(d.at)) throw new Error("a roll driver needs an at");
      if (path && path.kind !== "trunk") throw new Error("a roll driver needs the rig's trunkPath, not work");
      const k = classIndex(pose.size);
      if (k === 0) return identity();
      const pt = needPath(d, path);
      const over = Math.min(pt.lengths[k] ?? 0, Math.max(0, noseAt(pt, workAt(pose)) - d.at));
      return about(rotation(d.axis, (d.ratio ?? 1) * over), pivot);
    }
    default:
      throw new Error(`unknown driver type "${(d as Driver).type}"`);
  }
}

/** Part indices ordered so that a part comes after the part it rides; throws on an unknown ride or a cycle. */
export function rideOrder(parts: readonly RigPart[]): number[] {
  const ids = new Map<string, number>();
  parts.forEach((p, i) => {
    if (ids.has(p.id)) throw new Error(`part "${p.id}" is listed twice`);
    ids.set(p.id, i);
  });
  const state = new Array<number>(parts.length).fill(0);
  const order: number[] = [];
  const visit = (i: number) => {
    if (state[i] === 2) return;
    if (state[i] === 1) throw new Error(`part "${parts[i]!.id}" rides itself through a cycle`);
    state[i] = 1;
    const ride = parts[i]!.ride;
    if (ride != null) {
      const j = ids.get(ride);
      if (j === undefined) throw new Error(`part "${parts[i]!.id}" rides "${ride}", which is not a part`);
      visit(j);
    }
    state[i] = 2;
    order.push(i);
  };
  parts.forEach((_, i) => visit(i));
  return order;
}

/**
 * One matrix (blocks) per part, in part order: its drivers in list order, then its ride part's rigid
 * matrix, that part's without its own scales (isScale), so a part riding a stretched one moves and turns
 * with it but is not stretched. `path` is the rig's progress (workOf), for gauge and roll drivers.
 */
export function partMatrices(parts: readonly RigPart[], pose: Pose, order: readonly number[] = rideOrder(parts), path?: Work | null): Mat4[] {
  const ids = new Map(parts.map((p, i) => [p.id, i]));
  const out = new Array<Mat4>(parts.length);
  const rigid = new Array<Mat4>(parts.length);
  for (const i of order) {
    const p = parts[i]!;
    let m = identity();
    let r: Mat4 | null = (p.drivers ?? []).some(isScale) ? identity() : null;
    for (const d of p.drivers ?? []) {
      const dm = driverMatrix(d, pose, path);
      m = multiply(dm, m);
      if (r && !isScale(d)) r = multiply(dm, r);
    }
    if (p.ride != null) {
      const parent = rigid[ids.get(p.ride)!]!;
      m = multiply(parent, m);
      if (r) r = multiply(parent, r);
    }
    out[i] = m;
    rigid[i] = r ?? m;
  }
  return out;
}

/**
 * Which of the inputs the rig's drivers read, so a viewer shows a control for each and no
 * others. The trunk path's four (T, k, p, φ) are listed only when some driver reads them, so a
 * rig without them reports the four it always did.
 */
export interface RigInputs {
  theta: boolean;
  travel: boolean;
  depth: boolean;
  lifting: boolean;
  /** W, the work's progress (a trunk's travel). */
  work?: true;
  /** k, the work's class. */
  size?: true;
  /** p, the trunk's presence. */
  presence?: true;
  /** φ, the feed's travel. */
  feed?: true;
  /** How full the oil tank is. */
  oil?: true;
}

export function rigInputs(parts: readonly RigPart[]): RigInputs {
  const used: RigInputs = { theta: false, travel: false, depth: false, lifting: false };
  for (const p of parts)
    for (const d of p.drivers ?? []) {
      if (d.type === "rotate" || d.type === "slide" || d.type === "swing") {
        const input = driverInput(d);
        if (input === "theta" || input === "travel") used[input] = true;
        else used[input] = true;
      }
      else if (d.type === "gauge") {
        used.size = used.presence = true;
        if ((d.mode ?? "occupy") === "occupy") used.work = true;
        if (d.lobes) used.travel = true;
      } else if (d.type === "roll") used.size = used.work = true;

      else if (d.type === "stretch" && stretchInput(d) === "oil") used.oil = true;
      else used.depth = true;
      if (d.type === "step" && d.lifting) used.lifting = true;
    }
  return used;
}

/** The distinct `requires` values, in the order the parts first name them. */
export function requiresValues(parts: readonly RigPart[]): string[] {
  const out: string[] = [];
  for (const p of parts) if (p.requires != null && !out.includes(p.requires)) out.push(p.requires);
  return out;
}

/** The shortest signed step from angle `from` to `to` on a circle of `period` (a turn by default), in (−period/2, period/2]. */
export function wrappedDelta(from: number, to: number, period = 2 * Math.PI): number {
  let d = (to - from) % period;
  if (d > period / 2) d -= period;
  else if (d <= -period / 2) d += period;
  return d;
}

/** The most shaft turns thetaTurns looks for a cycle in; a longer one is not worth a slider. */
export const MAX_CYCLE_TURNS = 6;

/**
 * How many shaft turns it takes for every part that reads θ to come back to the same pose: the
 * least n (up to MAX_CYCLE_TURNS) for which every θ-reading rotate, slide and swing turns its
 * ratio × n whole times. 1 when none is that short: a gear train of odd teeth counts, which looks
 * the same at each turn anyway, as a tooth comes round where a tooth was. The handcar's beam
 * swings once in 3 turns of its axle (ratio −1/3), so its slider spans 3 turns.
 */
export function thetaTurns(parts: readonly RigPart[]): number {
  const ratios: number[] = [];
  for (const p of parts)
    for (const d of p.drivers ?? [])
      if ((d.type === "rotate" || d.type === "slide" || d.type === "swing") && driverInput(d) === "theta") ratios.push(d.ratio ?? 1);
  for (let n = 1; n <= MAX_CYCLE_TURNS; n++) if (ratios.every((r) => Math.abs(r * n - Math.round(r * n)) < 1e-4)) return n;
  return 1;
}
