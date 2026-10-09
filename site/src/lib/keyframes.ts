// A shape's own keyframe animations (its `animations`), posed as the game poses them, as pure
// functions. A port of the game's client animator (VintagestoryAPI.dll, 1.22.7, decompiled):
//
// - Animation.GenerateAllFrames resolves every keyframe into a whole pose: for each element and
//   each of its three channels (offset, rotation, stretch) on its own, the nearest keyframes
//   before and after that set the channel, wrapping round the end, are interpolated at that
//   keyframe's frame (`resolveAnimation`). It also lists, for every whole frame, the resolved
//   keyframes either side of it (getLeftRightResolvedFrame; `leftRight`).
// - ClientAnimator.calculateMatrices and ElementPose.Add interpolate between those two resolved
//   keyframes at the running frame, rotShortestDistance turning the short way round
//   (`poseAt`). An animation that holds its end (onAnimationEnd "Hold") stops interpolating on
//   its last frame.
// - ShapeElement.GetLocalTransformMatrix places each element in its parent's frame with the
//   pose's offset, rotation and stretch (`localMatrix`), and the hierarchy is walked from the
//   root (`animatedWorlds`). What the game's shader applies to a vertex is the matrix of its
//   joint, the nearest element at or above it that some animation names, times the inverse of
//   that joint's rest matrix (`jointDeltas`), so elements move in groups of a joint (`jointOf`).
// - RunningAnimation.Progress advances the frame by 30 frames a second times the animation's
//   speed (`advanceFrame`).
//
// test/keyframes.test.ts holds these to hand-worked poses of test/fixtures/keyframes.json.
// Matrices are rig.ts's (column-major, points as M·p) and in voxels, like flattenShape's.
import { identity, multiply, rotation, translation, type FlatElement, type Mat4, type ShapeElement } from "./rig.ts";

/** The game's frame rate for shape animations: RunningAnimation.Progress adds 30 frames a second at speed 1. */
export const FRAMES_PER_SECOND = 30;

export type AnimationEnd = "Repeat" | "Hold" | "Stop" | "EaseOut";
export type ActivityStopped = "Rewind" | "Stop" | "PlayTillEnd" | "EaseOut";

/** One element's entry in a keyframe; a channel is set when any of its three values is. */
export interface KeyframeElement {
  offsetX?: number;
  offsetY?: number;
  offsetZ?: number;
  rotationX?: number;
  rotationY?: number;
  rotationZ?: number;
  stretchX?: number;
  stretchY?: number;
  stretchZ?: number;
  rotShortestDistanceX?: boolean;
  rotShortestDistanceY?: boolean;
  rotShortestDistanceZ?: boolean;
}

/** A shape's animation as the file holds it (keys as the game's editor writes them; the game reads them in any case). */
export interface ShapeAnimation {
  code?: string;
  name?: string;
  quantityframes: number;
  version?: number;
  onActivityStopped?: ActivityStopped;
  onAnimationEnd?: AnimationEnd;
  easeAnimationSpeed?: boolean;
  keyframes: { frame: number; elements?: Record<string, KeyframeElement> }[];
}

/** An element's pose: offset (voxels), rotation (degrees) and stretch, as ElementPose holds them (the game's offset is in blocks). */
export interface ElementPose {
  offset: [number, number, number];
  rotation: [number, number, number];
  stretch: [number, number, number];
  shortest: [boolean, boolean, boolean];
}

export function restPose(): ElementPose {
  return { offset: [0, 0, 0], rotation: [0, 0, 0], stretch: [1, 1, 1], shortest: [false, false, false] };
}

/** An animation resolved against a shape: a whole pose per keyframe and the keyframes either side of each whole frame. */
export interface CompiledAnimation {
  code: string;
  name: string;
  /** quantityframes: frames run 0 ≤ f < frames. */
  frames: number;
  version: number;
  onAnimationEnd: AnimationEnd;
  onActivityStopped: ActivityStopped;
  /** Each keyframe's frame, in the file's order (the game's). */
  keyFrames: number[];
  /** poses[k][i]: element i (flattenShape's index) at keyframe k; null for an element no keyframe names. */
  poses: (ElementPose | null)[][];
  /** For each whole frame, the indices of the resolved keyframes at or before it and after it. */
  leftRight: [number, number][];
  /** The elements (flattenShape's indices) some keyframe names. */
  keyed: number[];
}

const CHANNELS = [
  ["offsetX", "offsetY", "offsetZ"],
  ["rotationX", "rotationY", "rotationZ"],
  ["stretchX", "stretchY", "stretchZ"],
] as const;
type Channel = 0 | 1 | 2;

const ANIMATION_ENDS: readonly string[] = ["Repeat", "Hold", "Stop", "EaseOut"];
const ACTIVITY_STOPPED: readonly string[] = ["Rewind", "Stop", "PlayTillEnd", "EaseOut"];

/** The game reads a shape with Newtonsoft, whose property names match in any case: `quantityFrames` is `quantityframes`. */
function field(o: object, key: string): unknown {
  const want = key.toLowerCase();
  for (const [k, v] of Object.entries(o)) if (k.toLowerCase() === want) return v;
  return undefined;
}

/** The game's enums from JSON: by name in any case, or by number. */
function enumOf<T extends string>(raw: unknown, names: readonly string[], fallback: T, what: string): T {
  if (raw === undefined || raw === null) return fallback;
  if (typeof raw === "number" && Number.isInteger(raw) && raw >= 0 && raw < names.length) return names[raw] as T;
  if (typeof raw === "string") {
    const hit = names.find((n) => n.toLowerCase() === raw.toLowerCase());
    if (hit) return hit as T;
  }
  throw new Error(`${what} must be one of ${names.join(", ")}`);
}

function isSet(e: KeyframeElement | undefined, ch: Channel): boolean {
  if (!e) return false;
  return CHANNELS[ch].some((k) => typeof e[k] === "number");
}

/** The element each keyframe name resolves to: the game's dictionary by name, where the last element of a name in the walk wins. */
export function elementsByName(flat: readonly FlatElement[]): Map<string, number> {
  const out = new Map<string, number>();
  for (const f of flat) out.set(f.element.name, f.index);
  return out;
}

/** The shape's `animations`, normalised, or [] when it has none. Throws on one the game could not read. */
export function shapeAnimations(shape: { animations?: unknown }): ShapeAnimation[] {
  const raw = shape.animations;
  if (raw === undefined || raw === null) return [];
  if (!Array.isArray(raw)) throw new Error("animations must be an array");
  return raw.map((a, i) => {
    if (typeof a !== "object" || a === null) throw new Error(`animations[${i}] is not an object`);
    const code = field(a, "code");
    const name = field(a, "name");
    const at = `animation ${typeof code === "string" ? `"${code}"` : typeof name === "string" ? `"${name}"` : i}`;
    const frames = field(a, "quantityframes");
    if (typeof frames !== "number" || !Number.isInteger(frames) || frames < 1) throw new Error(`${at}: quantityframes must be a whole number above 0`);
    const kfs = field(a, "keyframes");
    if (!Array.isArray(kfs)) throw new Error(`${at}: needs a keyframes array`);
    const keyframes = kfs.map((kf, k) => {
      if (typeof kf !== "object" || kf === null) throw new Error(`${at}: keyframes[${k}] is not an object`);
      const frame = field(kf, "frame") ?? 0;
      if (typeof frame !== "number" || !Number.isInteger(frame)) throw new Error(`${at}: keyframes[${k}] needs a whole frame`);
      const els = field(kf, "elements");
      const elements: Record<string, KeyframeElement> = {};
      if (els !== undefined && els !== null) {
        if (typeof els !== "object" || Array.isArray(els)) throw new Error(`${at}: keyframes[${k}].elements must map element names to poses`);
        for (const [n, v] of Object.entries(els)) {
          if (typeof v !== "object" || v === null) throw new Error(`${at}: keyframe ${frame}, "${n}" is not an object`);
          const e: KeyframeElement = {};
          for (const ch of CHANNELS)
            for (const key of ch) {
              const x = field(v, key);
              if (x === undefined || x === null) continue;
              if (typeof x !== "number" || !Number.isFinite(x)) throw new Error(`${at}: keyframe ${frame}, "${n}".${key} must be a number`);
              e[key] = x;
            }
          for (const key of ["rotShortestDistanceX", "rotShortestDistanceY", "rotShortestDistanceZ"] as const) if (field(v, key) === true) e[key] = true;
          elements[n] = e;
        }
      }
      return { frame, elements };
    });
    const version = field(a, "version") ?? 0;
    if (version !== 0 && version !== 1) throw new Error(`${at}: version must be 0 or 1`);
    const easeAnimationSpeed = field(a, "easeAnimationSpeed");
    return {
      ...(typeof code === "string" ? { code } : {}),
      ...(typeof name === "string" ? { name } : {}),
      quantityframes: frames,
      version,
      onActivityStopped: enumOf<ActivityStopped>(field(a, "onActivityStopped"), ACTIVITY_STOPPED, "Rewind", `${at}: onActivityStopped`),
      onAnimationEnd: enumOf<AnimationEnd>(field(a, "onAnimationEnd"), ANIMATION_ENDS, "Repeat", `${at}: onAnimationEnd`),
      ...(easeAnimationSpeed === true ? { easeAnimationSpeed: true } : {}),
      keyframes,
    };
  });
}

/** An animation's code as the game keys it: its code, else its name, lower-cased (AnimatorBase). */
export function animationCode(a: ShapeAnimation): string {
  return (a.code ?? a.name ?? "").toLowerCase();
}

/** Animation.seekRightKeyFrame: the first keyframe after `frame` setting the channel, else the first setting it at all, else -1. */
function seekRight(keys: readonly (KeyframeElement | undefined)[], frames: readonly number[], frame: number, ch: Channel): number {
  let first = -1;
  for (let i = 0; i < keys.length; i++) {
    if (!isSet(keys[i], ch)) continue;
    if (first === -1) first = i;
    if (frames[i]! > frame) return i;
  }
  return first;
}

/** Animation.seekLeftKeyFrame: the nearest keyframe before index `right`, wrapping, that sets the channel. */
function seekLeft(keys: readonly (KeyframeElement | undefined)[], right: number, ch: Channel): number {
  const n = keys.length;
  for (let i = 0; i < n; i++) {
    const j = (((right - i - 1) % n) + n) % n;
    if (isSet(keys[j], ch)) return j;
  }
  return -1;
}

const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

/**
 * Resolves an animation against a flattened shape, as Animation.GenerateAllFrames does. Throws as
 * the game does on an animation it cannot run: no keyframes, or a keyframe at or past
 * quantityframes; and on a channel given only in part (offsetY without offsetX, say), where the
 * game's lerp reads a value that is not there.
 */
export function resolveAnimation(a: ShapeAnimation, flat: readonly FlatElement[], byName: Map<string, number> = elementsByName(flat)): CompiledAnimation {
  const code = animationCode(a);
  const at = `animation "${a.code ?? a.name ?? "?"}"`;
  const Q = a.quantityframes;
  if (a.keyframes.length === 0) throw new Error(`${at} has no keyframes`);
  const frames = a.keyframes.map((k) => k.frame);
  for (const f of frames) if (f < 0 || f >= Q) throw new Error(`${at}: a keyframe at frame ${f}, outside 0..${Q - 1} (quantityframes ${Q})`);
  // Each element's entry in each keyframe: per element, the keyframe list (undefined where not named).
  const byElement = new Map<number, (KeyframeElement | undefined)[]>();
  a.keyframes.forEach((kf, k) => {
    for (const [n, e] of Object.entries(kf.elements ?? {})) {
      const i = byName.get(n);
      if (i === undefined) continue; // the game ignores a name the shape does not have
      for (const ch of [0, 1, 2] as Channel[])
        if (isSet(e, ch) && !CHANNELS[ch].every((key) => typeof e[key] === "number"))
          throw new Error(`${at}: keyframe ${kf.frame}, "${n}" sets ${CHANNELS[ch].filter((key) => typeof e[key] === "number").join(", ")} but not all of ${CHANNELS[ch].join(", ")}, which the game needs`);
      let list = byElement.get(i);
      if (!list) byElement.set(i, (list = new Array<KeyframeElement | undefined>(a.keyframes.length).fill(undefined)));
      list[k] = e;
    }
  });
  const keyed = [...byElement.keys()].sort((x, y) => x - y);
  const poses: (ElementPose | null)[][] = frames.map((frameNumber) => {
    const row = new Array<ElementPose | null>(flat.length).fill(null);
    for (const [i, keys] of byElement) {
      const pose = restPose();
      // Animation.GenerateFrameForElement: each channel on its own.
      for (const ch of [0, 1, 2] as Channel[]) {
        const r = seekRight(keys, frames, frameNumber, ch);
        if (r === -1) continue;
        const l0 = seekLeft(keys, r, ch);
        const right = keys[r]!;
        const left = l0 === -1 ? right : keys[l0]!;
        const lf = l0 === -1 ? frames[r]! : frames[l0]!;
        const rf = frames[r]!;
        let t: number;
        if (left === right) t = 0;
        else if (rf < lf) t = (((frameNumber - lf) % Q) + Q) % Q / (rf + (Q - lf));
        else t = (frameNumber - lf) / (rf - lf);
        const target = ch === 0 ? pose.offset : ch === 1 ? pose.rotation : pose.stretch;
        CHANNELS[ch].forEach((key, axis) => (target[axis] = lerp(left[key]!, right[key]!, t)));
        // The game takes the flags from the left keyframe of each channel in turn, so the last channel set wins.
        pose.shortest = [left.rotShortestDistanceX === true, left.rotShortestDistanceY === true, left.rotShortestDistanceZ === true];
      }
      row[i] = pose;
    }
    return row;
  });
  // Animation.getLeftRightResolvedFrame, for each whole frame.
  const leftRight: [number, number][] = [];
  const n = frames.length;
  for (let f = 0; f < Q; f++) {
    let left = -1;
    for (let j = n - 1; j >= 0; j--)
      if (frames[j]! <= f) {
        left = j;
        break;
      }
    if (left === -1) left = n - 1;
    leftRight.push([left, (left + 1) % n]);
  }
  return {
    code,
    name: a.name ?? a.code ?? code,
    frames: Q,
    version: a.version ?? 0,
    onAnimationEnd: a.onAnimationEnd ?? "Repeat",
    onActivityStopped: a.onActivityStopped ?? "Rewind",
    keyFrames: frames,
    poses,
    leftRight,
    keyed,
  };
}

/** GameMath.AngleDegDistance: the signed shortest turn from `start` to `end`, degrees in [−180, 180). */
export function angleDegDistance(start: number, end: number): number {
  return (((end - start) % 360) + 540) % 360 - 180;
}

/**
 * Every element's pose at a frame, 0 ≤ frame < frames (fractional between whole frames), as
 * ClientAnimator.calculateMatrices and ElementPose.Add give it for one animation at full weight.
 * Elements no keyframe names are null (at rest).
 */
export function poseAt(anim: CompiledAnimation, frame: number): (ElementPose | null)[] {
  const Q = anim.frames;
  const f = frame >= Q ? frame % Q : Math.max(frame, 0);
  const whole = Math.floor(f);
  const [li, ri0] = anim.leftRight[whole]!;
  // Holding its end, an animation stays on its last keyframe's pose on its last frame.
  const ri = anim.onAnimationEnd === "Hold" && whole + 1 === Q ? li : ri0;
  const prev = anim.keyFrames[li]!;
  const next = anim.keyFrames[ri]!;
  const span = next > prev ? next - prev : Q - prev + next;
  const l = (f >= prev ? f - prev : Q - prev + f) / span;
  const L = anim.poses[li]!;
  const R = anim.poses[ri]!;
  return L.map((a, i) => {
    if (!a) return null;
    const b = R[i]!;
    const out = restPose();
    for (let k = 0; k < 3; k++) {
      // ElementPose.Add with weight 1.
      out.rotation[k] = a.shortest[k] ? a.rotation[k]! + angleDegDistance(a.rotation[k]!, b.rotation[k]!) * l : a.rotation[k]! * (1 - l) + b.rotation[k]! * l;
      out.stretch[k] = 1 + (a.stretch[k]! - 1) * (1 - l) + (b.stretch[k]! - 1) * l;
      out.offset[k] = a.offset[k]! * (1 - l) + b.offset[k]! * l;
    }
    return out;
  });
}

const DEG = Math.PI / 180;

/** Mat4f.RotateByXYZ: Rx·Ry·Rz. */
function rotateXYZ(x: number, y: number, z: number): Mat4 {
  return multiply(rotation("x", x * DEG), multiply(rotation("y", y * DEG), rotation("z", z * DEG)));
}

function scaling(x: number, y: number, z: number): Mat4 {
  const m = identity();
  m[0] = x;
  m[5] = y;
  m[10] = z;
  return m;
}

/** An element's scale, ShapeElement.ScaleX/Y/Z (1 when not given). */
function elementScale(e: ShapeElement): [number, number, number] {
  return [e.scaleX ?? 1, e.scaleY ?? 1, e.scaleZ ?? 1];
}

/**
 * ShapeElement.GetLocalTransformMatrix, in voxels: an element's frame (whose origin is its `from`)
 * in its parent's frame, with a pose. Version 0 (every shape the game ships): about the rotation
 * origin, the element's rotation plus the pose's, its scale times the stretch, then the offset in
 * that rotated frame. Version 1: the element's own transform, then the offset, stretch and rotation
 * about its `from`.
 */
export function localMatrix(e: ShapeElement, pose: ElementPose | null, version = 0): Mat4 {
  const p = pose ?? restPose();
  const o = e.rotationOrigin ?? [0, 0, 0];
  const s = elementScale(e);
  const rx = e.rotationX ?? 0;
  const ry = e.rotationY ?? 0;
  const rz = e.rotationZ ?? 0;
  const t = [e.from[0] + p.offset[0] - o[0], e.from[1] + p.offset[1] - o[1], e.from[2] + p.offset[2] - o[2]] as const;
  if (version === 1) {
    let m = translation(o[0], o[1], o[2]);
    m = multiply(m, scaling(s[0], s[1], s[2]));
    m = multiply(m, rotateXYZ(rx, ry, rz));
    m = multiply(m, translation(t[0], t[1], t[2]));
    m = multiply(m, scaling(p.stretch[0], p.stretch[1], p.stretch[2]));
    return multiply(m, rotateXYZ(p.rotation[0], p.rotation[1], p.rotation[2]));
  }
  let m = translation(o[0], o[1], o[2]);
  m = multiply(m, rotateXYZ(rx + p.rotation[0], ry + p.rotation[1], rz + p.rotation[2]));
  m = multiply(m, scaling(s[0] * p.stretch[0], s[1] * p.stretch[1], s[2] * p.stretch[2]));
  return multiply(m, translation(t[0], t[1], t[2]));
}

/** Each element's parent's index in flattenShape's walk, or -1 for a root element. */
export function parentsOf(flat: readonly FlatElement[]): number[] {
  const parents = new Array<number>(flat.length).fill(-1);
  const stack: number[] = [];
  for (const f of flat) {
    // A depth-first walk: an element's parent is the last element one level up.
    stack.length = f.chain.length - 1;
    parents[f.index] = stack.length > 0 ? stack[stack.length - 1]! : -1;
    stack.push(f.index);
  }
  return parents;
}

/** Each element's model matrix (its frame, from its `from`, to model voxels) at a pose; `poses` null is the rest pose. */
export function animatedWorlds(flat: readonly FlatElement[], parents: readonly number[], poses: readonly (ElementPose | null)[] | null, version = 0): Mat4[] {
  const out = new Array<Mat4>(flat.length);
  for (const f of flat) {
    const p = parents[f.index]!;
    const local = localMatrix(f.element, poses?.[f.index] ?? null, version);
    out[f.index] = p < 0 ? local : multiply(out[p]!, local);
  }
  return out;
}

/** The inverse of an affine matrix (rotation, scale and translation). */
export function invertAffine(m: Mat4): Mat4 {
  const [a, b, c, , d, e, f, , g, h, i, , x, y, z] = m as [number, number, number, number, number, number, number, number, number, number, number, number, number, number, number, number];
  const det = a * (e * i - f * h) - d * (b * i - c * h) + g * (b * f - c * e);
  if (Math.abs(det) < 1e-12) return identity();
  const r = identity();
  r[0] = (e * i - f * h) / det;
  r[1] = (c * h - b * i) / det;
  r[2] = (b * f - c * e) / det;
  r[4] = (f * g - d * i) / det;
  r[5] = (a * i - c * g) / det;
  r[6] = (c * d - a * f) / det;
  r[8] = (d * h - e * g) / det;
  r[9] = (b * g - a * h) / det;
  r[10] = (a * e - b * d) / det;
  r[12] = -(r[0] * x + r[4] * y + r[8] * z);
  r[13] = -(r[1] * x + r[5] * y + r[9] * z);
  r[14] = -(r[2] * x + r[6] * y + r[10] * z);
  return r;
}

/**
 * The joints: the elements any of the shape's animations names, as Shape.ResolveAndFindJoints
 * finds them. Each element moves with its joint, the nearest of these at or above it (-1, none:
 * it never moves). Elements of one joint move together, so a viewer draws them as one piece.
 */
export function jointOf(flat: readonly FlatElement[], parents: readonly number[], animations: readonly CompiledAnimation[]): number[] {
  const joints = new Set<number>();
  for (const a of animations) for (const i of a.keyed) joints.add(i);
  const out = new Array<number>(flat.length).fill(-1);
  for (const f of flat) {
    const p = parents[f.index]!;
    out[f.index] = joints.has(f.index) ? f.index : p < 0 ? -1 : out[p]!;
  }
  return out;
}

/** An animated shape: what posing it needs, worked out once. */
export interface AnimatedShape {
  animations: CompiledAnimation[];
  parents: number[];
  /** Each element's joint (an element index), or -1. */
  joints: number[];
  /** The distinct joints, in element order. */
  jointList: number[];
  /** The inverse of each element's rest model matrix (voxels), for the joints. */
  restInverse: Map<number, Mat4>;
  version: number;
}

/** Resolves every animation of a shape against its flattened elements. Throws on an animation the game would not run. */
export function animateShape(shape: { animations?: unknown }, flat: readonly FlatElement[]): AnimatedShape {
  const byName = elementsByName(flat);
  const animations = shapeAnimations(shape).map((a) => resolveAnimation(a, flat, byName));
  const parents = parentsOf(flat);
  const joints = jointOf(flat, parents, animations);
  const jointList = [...new Set(joints.filter((j) => j >= 0))].sort((a, b) => a - b);
  // The game warns of mixed versions and blends with the highest; one shape normally has one.
  const version = animations.reduce((v, a) => Math.max(v, a.version), 0);
  // The game's rest matrices (ShapeElement.CacheInverseTransformMatrixRecursive) are version 0's, whatever the animations'.
  const rest = animatedWorlds(flat, parents, null, 0);
  return { animations, parents, joints, jointList, restInverse: new Map(jointList.map((j) => [j, invertAffine(rest[j]!)])), version };
}

/**
 * Each joint's motion at a frame of an animation (null: the rest pose), in voxels: its animated
 * model matrix times the inverse of its rest one, the matrix the game's shader applies to the
 * joint's vertices. Keyed by joint (element index).
 */
export function jointDeltas(shape: AnimatedShape, flat: readonly FlatElement[], anim: CompiledAnimation | null, frame: number): Map<number, Mat4> {
  const out = new Map<number, Mat4>();
  if (!anim) {
    for (const j of shape.jointList) out.set(j, identity());
    return out;
  }
  const worlds = animatedWorlds(flat, shape.parents, poseAt(anim, frame), anim.version);
  for (const j of shape.jointList) out.set(j, multiply(worlds[j]!, shape.restInverse.get(j)!));
  return out;
}

/**
 * A piece's matrix as the scene poses it, blocks: its rig part's matrix (blocks) times its joint's
 * motion (voxels, jointDeltas; its translation rescaled to blocks), or the part's alone without one.
 * Neither depends on what else is drawn: a joint's motion comes from the whole hierarchy, so an
 * element moves the same whether the elements above or below it are shown or not.
 */
export function pieceMatrix(part: Mat4, joint: Mat4 | undefined): Mat4 {
  if (!joint) return part.slice();
  const j = joint.slice();
  j[12]! /= 16;
  j[13]! /= 16;
  j[14]! /= 16;
  return multiply(part, j);
}

/** Whether Play keeps going round by default: the animation repeats in the game. */
export function loopsByDefault(anim: CompiledAnimation): boolean {
  return anim.onAnimationEnd === "Repeat";
}

/**
 * Moves the frame on by `dt` seconds at `speed` (1 is the game's 30 frames a second). Looping, it
 * wraps round from the last frame to the first, as a repeating animation does; not looping, it
 * stops on the last whole frame, as one that holds its end does. Backwards (a negative speed) the
 * same the other way, stopping on frame 0.
 */
export function advanceFrame(anim: CompiledAnimation, frame: number, dt: number, speed: number, loop: boolean): { frame: number; ended: boolean } {
  const Q = anim.frames;
  const next = frame + FRAMES_PER_SECOND * dt * speed;
  if (loop) return { frame: ((next % Q) + Q) % Q, ended: false };
  const last = Q - 1;
  if (next >= last && speed >= 0) return { frame: last, ended: true };
  if (next <= 0 && speed < 0) return { frame: 0, ended: true };
  return { frame: Math.min(Math.max(next, 0), last), ended: false };
}

// ---- the manifest's scenario.animations

/** What the manifest says about a model's animations: which plays first, labels, game speeds and groups for the select. */
export interface AnimationsSpec {
  /** The animation selected when the page opens; without one, none is (the model at rest). */
  default?: string;
  /** A label per code, shown in place of the animation's own name. */
  labels?: Record<string, string>;
  /** Per code, the speed the game plays it at (its entity's animationSpeed), where the speed control starts. */
  speeds?: Record<string, number>;
  /** Groups for the select, in order; animations in none follow. */
  groups?: { label: string; codes: string[] }[];
}

/** Problems with a scenario's animations for a shape's animation codes (lower-case, as the game keys them). */
export function checkAnimations(spec: AnimationsSpec, codes: readonly string[]): string[] {
  const out: string[] = [];
  const has = (c: string) => codes.includes(c.toLowerCase());
  if (typeof spec !== "object" || spec === null) return ["animations must be an object"];
  if (codes.length === 0) out.push("the shape has no animations");
  if (spec.default !== undefined && (typeof spec.default !== "string" || !has(spec.default))) out.push(`animations.default "${String(spec.default)}" is not an animation of the shape`);
  for (const c of Object.keys(spec.labels ?? {})) if (!has(c)) out.push(`animations.labels: "${c}" is not an animation of the shape`);
  for (const [c, v] of Object.entries(spec.speeds ?? {})) {
    if (!has(c)) out.push(`animations.speeds: "${c}" is not an animation of the shape`);
    if (typeof v !== "number" || !(v > 0)) out.push(`animations.speeds: "${c}" must be above 0`);
  }
  const grouped = new Set<string>();
  for (const g of spec.groups ?? []) {
    if (typeof g.label !== "string" || g.label === "") out.push("an animation group needs a label");
    if (!Array.isArray(g.codes) || g.codes.length === 0) out.push(`animation group "${g.label}" needs codes`);
    for (const c of g.codes ?? []) {
      if (!has(c)) out.push(`animation group "${g.label}": "${c}" is not an animation of the shape`);
      if (grouped.has(c.toLowerCase())) out.push(`animation group "${g.label}": "${c}" is in another group too`);
      grouped.add(c.toLowerCase());
    }
  }
  return out;
}

export interface AnimationOption {
  code: string;
  label: string;
}

/** The select's options: the scenario's groups in order, then a group of the rest (label null) in the shape's order. */
export function animationOptions(animations: readonly CompiledAnimation[], spec: AnimationsSpec | undefined): { label: string | null; options: AnimationOption[] }[] {
  const lc = (o: Record<string, string> | undefined) => new Map(Object.entries(o ?? {}).map(([k, v]) => [k.toLowerCase(), v]));
  const labels = lc(spec?.labels);
  const option = (a: CompiledAnimation): AnimationOption => ({ code: a.code, label: labels.get(a.code) ?? (a.name !== a.code ? `${a.name} (${a.code})` : a.code) });
  const byCode = new Map(animations.map((a) => [a.code, a]));
  const out: { label: string | null; options: AnimationOption[] }[] = [];
  const done = new Set<string>();
  for (const g of spec?.groups ?? []) {
    const options = g.codes.map((c) => byCode.get(c.toLowerCase())).filter((a): a is CompiledAnimation => a !== undefined).map(option);
    options.forEach((o) => done.add(o.code));
    if (options.length > 0) out.push({ label: g.label, options });
  }
  const rest = animations.filter((a) => !done.has(a.code)).map(option);
  if (rest.length > 0) out.push({ label: null, options: rest });
  return out;
}

/** The speed an animation starts at: the scenario's for it, else 1 (the game's AnimationSpeed default). */
export function animationSpeed(spec: AnimationsSpec | undefined, code: string): number {
  for (const [k, v] of Object.entries(spec?.speeds ?? {})) if (k.toLowerCase() === code) return v;
  return 1;
}
