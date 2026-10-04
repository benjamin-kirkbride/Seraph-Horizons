// Shape posing and rig maths for the model viewer, as pure functions. A port of the C# in
// mods-src/seraphhorizons/BuckingSawmill/Core/RigAnimation.cs and of the reference in that mod's
// tools/make_shape.py (driver_matrix, part_matrix, flatten); test/rig.test.ts holds all three
// to the poses in mods-src/seraphhorizons/tests/BuckingSawmill/rig-reference.json. The rig format is
// documented in mods-src/seraphhorizons/BuckingSawmill/README.md ("Rig schema").
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
  faces?: Partial<Record<FaceName, ShapeFace>>;
  children?: ShapeElement[];
}

export interface Shape {
  textures?: Record<string, string>;
  elements: ShapeElement[];
}

export const FACE_NAMES = ["north", "east", "south", "west", "up", "down"] as const;
export type FaceName = (typeof FACE_NAMES)[number];

// ---- rigs

export interface Driver {
  type: "rotate" | "slide" | "swing" | "feed" | "step" | "stretch";
  axis: Axis;
  pivot?: Vec3;
  anchor?: Vec3;
  ratio?: number;
  rectified?: boolean;
  amplitude?: number;
  phase?: number;
  travel?: number;
  motion?: "slide" | "rotate";
  amount?: number;
  from?: number;
  to?: number;
  lifting?: "hold" | "block" | "trip";
  top?: number;
  length?: number;
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
}

/** rig.json: `parts` and `cells` are read as such; every other key is an anchor (model-anchors.ts). */
export interface Rig {
  cells?: RigCell[];
  parts?: RigPart[];
  [key: string]: unknown;
}

/** The rig's inputs: θ the signed shaft angle (radians), depth 0..1, lifting 0..1, and ψ
 * the shaft's travel, the total angle it has turned either way (|θ| when not given). */
export interface Pose {
  theta: number;
  depth: number;
  lifting: number;
  travel?: number;
}

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

/** An element's own transform: Rx·Ry·Rz (degrees) about its rotationOrigin, the order VS applies them in. */
export function elementLocal(e: ShapeElement): Mat4 {
  const o = e.rotationOrigin ?? [0, 0, 0];
  const deg = Math.PI / 180;
  const r = multiply(rotation("x", (e.rotationX ?? 0) * deg), multiply(rotation("y", (e.rotationY ?? 0) * deg), rotation("z", (e.rotationZ ?? 0) * deg)));
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

/** One driver's matrix (blocks) at a pose. */
export function driverMatrix(d: Driver, pose: Pose): Mat4 {
  const { theta, depth } = pose;
  const lifting = pose.lifting ?? 0;
  const travel = pose.travel ?? Math.abs(theta);
  switch (d.type) {
    case "rotate":
      return about(rotation(d.axis, (d.ratio ?? 1) * (d.rectified ? travel : theta)), point(d, "pivot"));
    case "swing":
      return about(rotation(d.axis, (d.amplitude ?? 0) * Math.sin((d.ratio ?? 1) * theta + (d.phase ?? 0))), point(d, "pivot"));
    case "slide":
      return along(d.axis, (d.amplitude ?? 0) * Math.sin((d.ratio ?? 1) * theta + (d.phase ?? 0)));
    case "feed":
      return along(d.axis, (d.travel ?? 0) * depth);
    case "step": {
      const e = stepFraction(d, depth, lifting);
      return d.motion === "rotate" ? about(rotation(d.axis, (d.amount ?? 0) * e), point(d, "pivot")) : along(d.axis, (d.amount ?? 0) * e);
    }
    case "stretch": {
      const k = AXIS_INDEX[d.axis];
      const length = d.length ?? 0;
      if (length === 0) throw new Error("a stretch driver needs a non-zero length");
      const f = (length + (d.travel ?? 0) * depth) / length;
      const m = identity();
      m[k * 5] = f;
      m[12 + k] = point(d, "anchor")[k]! * (1 - f);
      return m;
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

/** One matrix (blocks) per part, in part order: its drivers in list order, then its ride part's whole matrix. */
export function partMatrices(parts: readonly RigPart[], pose: Pose, order: readonly number[] = rideOrder(parts)): Mat4[] {
  const ids = new Map(parts.map((p, i) => [p.id, i]));
  const out = new Array<Mat4>(parts.length);
  for (const i of order) {
    const p = parts[i]!;
    let m = identity();
    for (const d of p.drivers ?? []) m = multiply(driverMatrix(d, pose), m);
    if (p.ride != null) m = multiply(out[ids.get(p.ride)!]!, m);
    out[i] = m;
  }
  return out;
}

/** Which of the inputs the rig's drivers read, so a viewer shows a control for each and no others. */
export interface RigInputs {
  theta: boolean;
  travel: boolean;
  depth: boolean;
  lifting: boolean;
}

export function rigInputs(parts: readonly RigPart[]): RigInputs {
  const used: RigInputs = { theta: false, travel: false, depth: false, lifting: false };
  for (const p of parts)
    for (const d of p.drivers ?? []) {
      if (d.type === "rotate") used[d.rectified ? "travel" : "theta"] = true;
      else if (d.type === "slide" || d.type === "swing") used.theta = true;
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

/** The shortest signed step from angle `from` to `to`, in (−π, π]. */
export function wrappedDelta(from: number, to: number): number {
  let d = (to - from) % (2 * Math.PI);
  if (d > Math.PI) d -= 2 * Math.PI;
  else if (d <= -Math.PI) d += 2 * Math.PI;
  return d;
}
