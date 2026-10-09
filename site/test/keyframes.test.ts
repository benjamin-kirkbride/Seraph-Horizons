import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  advanceFrame,
  angleDegDistance,
  animateShape,
  animationOptions,
  animationSpeed,
  animatedWorlds,
  checkAnimations,
  invertAffine,
  jointDeltas,
  localMatrix,
  loopsByDefault,
  parentsOf,
  poseAt,
  resolveAnimation,
  restPose,
  shapeAnimations,
  type ElementPose,
} from "../src/lib/keyframes.ts";
import { publishModels } from "../src/lib/model-manifest.ts";
import { apply, flattenShape, multiply, translation, type Mat4, type Shape, type ShapeElement, type Vec3 } from "../src/lib/rig.ts";

const shape = JSON.parse(readFileSync(new URL("./fixtures/keyframes.json", import.meta.url), "utf8")) as Shape;
const flat = flattenShape(shape.elements);
const index = (name: string) => flat.findIndex((f) => f.element.name === name);
const [BASE, ARM, HAND, FINGER, WHEEL] = ["base", "arm", "hand", "finger", "wheel"].map(index) as [number, number, number, number, number];
const animated = animateShape(shape, flat);
const anim = (code: string) => animated.animations.find((a) => a.code === code)!;

const close = (a: readonly number[], b: readonly number[], digits = 6) => {
  expect(a.length).toBe(b.length);
  a.forEach((v, i) => expect(v, `[${i}] of ${JSON.stringify(a)} vs ${JSON.stringify(b)}`).toBeCloseTo(b[i]!, digits));
};

// ---- the game's matrix code, ported line by line from the decompiled VintagestoryAPI.dll
// (Mat4f.Translate, Mat4f.Scale, Mat4f.RotateByXYZ on a Span, and ShapeElement.GetLocalTransformMatrix),
// in blocks as the game has it, to hold keyframes.ts's own composition to.
function gameTranslate(m: number[], x: number, y: number, z: number) {
  m[12]! += m[0]! * x + m[4]! * y + m[8]! * z;
  m[13]! += m[1]! * x + m[5]! * y + m[9]! * z;
  m[14]! += m[2]! * x + m[6]! * y + m[10]! * z;
  m[15]! += m[3]! * x + m[7]! * y + m[11]! * z;
}
function gameScale(a: number[], x: number, y: number, z: number) {
  for (let i = 0; i < 4; i++) {
    a[i]! *= x;
    a[4 + i]! *= y;
    a[8 + i]! *= z;
  }
}
function gameRotateByXYZ(m: number[], radX: number, radY: number, radZ: number) {
  if (radX === 0 && radY === 0 && radZ === 0) return;
  const num = Math.sin(radX), num2 = Math.cos(radX), num3 = Math.sin(radY), num4 = Math.cos(radY), num5 = Math.sin(radZ), num6 = Math.cos(radZ);
  const num7 = num * num3, num8 = -num2 * num3, num9 = num4 * num6, num10 = num7 * num6 + num2 * num5, num11 = num8 * num6 + num * num5;
  const num12 = -num4 * num5, num13 = num2 * num6 - num7 * num5, num14 = num * num6 - num8 * num5, num15 = -num * num4, num16 = num2 * num4;
  const o = m.slice(0, 12);
  for (let i = 0; i < 4; i++) {
    m[i] = num9 * o[i]! + num10 * o[4 + i]! + num11 * o[8 + i]!;
    m[4 + i] = num12 * o[i]! + num13 * o[4 + i]! + num14 * o[8 + i]!;
    m[8 + i] = num3 * o[i]! + num15 * o[4 + i]! + num16 * o[8 + i]!;
  }
}
function gameLocal(e: ShapeElement & { scaleX?: number; scaleY?: number; scaleZ?: number }, version: number, tf: ElementPose): number[] {
  const m = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  const [num, num2, num3] = (e.rotationOrigin ?? [0, 0, 0]).map((v) => v / 16) as Vec3;
  const D = Math.PI / 180;
  const [tx, ty, tz] = tf.offset.map((v) => v / 16) as Vec3;
  const [sx, sy, sz] = [e.scaleX ?? 1, e.scaleY ?? 1, e.scaleZ ?? 1];
  const [rx, ry, rz] = [e.rotationX ?? 0, e.rotationY ?? 0, e.rotationZ ?? 0];
  if (version === 1) {
    gameTranslate(m, num, num2, num3);
    gameScale(m, sx, sy, sz);
    gameRotateByXYZ(m, rx * D, ry * D, rz * D);
    gameTranslate(m, -num + e.from[0] / 16 + tx, -num2 + e.from[1] / 16 + ty, -num3 + e.from[2] / 16 + tz);
    gameScale(m, ...tf.stretch);
    gameRotateByXYZ(m, tf.rotation[0] * D, tf.rotation[1] * D, tf.rotation[2] * D);
  } else {
    gameTranslate(m, num, num2, num3);
    gameRotateByXYZ(m, (rx + tf.rotation[0]) * D, (ry + tf.rotation[1]) * D, (rz + tf.rotation[2]) * D);
    gameScale(m, sx * tf.stretch[0], sy * tf.stretch[1], sz * tf.stretch[2]);
    gameTranslate(m, e.from[0] / 16 + tx - num, e.from[1] / 16 + ty - num2, e.from[2] / 16 + tz - num3);
  }
  return m;
}
const toBlocks = (m: Mat4) => m.map((v, i) => (i >= 12 && i <= 14 ? v / 16 : v));

describe("element matrices (ShapeElement.GetLocalTransformMatrix)", () => {
  const element: ShapeElement & { scaleX: number; scaleY: number; scaleZ: number } = {
    name: "e",
    from: [3, 5, -2],
    to: [7, 9, 4],
    rotationOrigin: [4, 6, 1],
    rotationX: 22.5,
    rotationY: -40,
    rotationZ: 67,
    scaleX: 1.25,
    scaleY: 0.75,
    scaleZ: 1.5,
  };
  const pose: ElementPose = { offset: [1.5, -2, 0.25], rotation: [12, 200, -33], stretch: [1.1, 0.9, 2], shortest: [false, false, false] };

  it("compose offset, rotation and stretch as the game does, in both versions", () => {
    for (const version of [0, 1]) {
      close(toBlocks(localMatrix(element, pose, version)), gameLocal(element, version, pose));
      close(toBlocks(localMatrix(element, null, version)), gameLocal(element, version, restPose()));
    }
  });

  it("at rest, place each element where flattenShape does (its frame starting at its from)", () => {
    const worlds = animatedWorlds(flat, parentsOf(flat), null);
    for (const f of flat) close(worlds[f.index]!, multiply(f.world, translation(...f.element.from)));
    // and with an element scale too, which rig.ts's flattening now honours
    const scaled = flattenShape([element]);
    close(animatedWorlds(scaled, [-1], null)[0]!, multiply(scaled[0]!.world, translation(...element.from)));
  });

  it("inverts an affine matrix", () => {
    const m = localMatrix(element, pose);
    close(multiply(m, invertAffine(m)), [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
  });

  it("finds each element's parent in the walk", () => {
    expect(parentsOf(flat)).toEqual([-1, BASE, ARM, HAND, -1]);
  });
});

describe("reading a shape's animations", () => {
  it("reads the fixture's three, by code", () => {
    expect(animated.animations.map((a) => [a.code, a.name, a.frames, a.onAnimationEnd])).toEqual([
      ["swing", "Swing", 20, "Repeat"],
      ["spin", "Spin", 40, "Repeat"],
      ["drop", "Drop", 10, "Hold"],
    ]);
    expect(loopsByDefault(anim("swing"))).toBe(true);
    expect(loopsByDefault(anim("drop"))).toBe(false);
  });

  it("matches keys in any case and codes by name when there is no code, as the game does", () => {
    const [a] = shapeAnimations({ animations: [{ Name: "Wave", QuantityFrames: 4, OnAnimationEnd: "hold", KeyFrames: [{ Frame: 0, Elements: { arm: { RotationX: 1, RotationY: 2, RotationZ: 3 } } }] }] });
    expect(a!.quantityframes).toBe(4);
    expect(a!.onAnimationEnd).toBe("Hold");
    const r = resolveAnimation(a!, flat);
    expect(r.code).toBe("wave");
    expect(r.poses[0]![ARM]!.rotation).toEqual([1, 2, 3]);
  });

  it("refuses what the game cannot run", () => {
    const one = (a: object) => resolveAnimation(shapeAnimations({ animations: [a] })[0]!, flat);
    expect(() => one({ code: "x", quantityframes: 5, keyframes: [] })).toThrow(/no keyframes/);
    expect(() => one({ code: "x", quantityframes: 5, keyframes: [{ frame: 5, elements: {} }] })).toThrow(/frame 5, outside 0..4/);
    expect(() => one({ code: "x", quantityframes: 5, keyframes: [{ frame: 0, elements: { arm: { offsetY: 2 } } }] })).toThrow(/sets offsetY but not all of offsetX, offsetY, offsetZ/);
    expect(() => shapeAnimations({ animations: [{ code: "x", quantityframes: 0, keyframes: [] }] })).toThrow(/quantityframes/);
    expect(() => shapeAnimations({ animations: [{ code: "x", quantityframes: 2, onAnimationEnd: "Bounce", keyframes: [] }] })).toThrow(/onAnimationEnd/);
    expect(shapeAnimations({})).toEqual([]);
  });

  it("ignores names the shape does not have, and lists the elements it moves", () => {
    expect(anim("swing").keyed).toEqual([ARM, HAND]);
    expect(animated.jointList).toEqual([BASE, ARM, HAND, WHEEL]);
    // the finger has no keyframes: it moves with the hand
    expect(animated.joints[FINGER]).toBe(HAND);
  });
});

describe("resolving keyframes (Animation.GenerateAllFrames)", () => {
  const swing = anim("swing");

  it("interpolates each channel between the keyframes that set it, wrapping round the end", () => {
    // the arm's rotation is set at 0 and 10 only: at keyframe 15 it is half-way from 10 back round to 0
    expect(swing.poses.map((p) => p[ARM]!.rotation[2])).toEqual([0, 90, 45]);
    // its offset is set at 15 only, so it holds everywhere
    expect(swing.poses.map((p) => p[ARM]!.offset)).toEqual([[0, 2, 0], [0, 2, 0], [0, 2, 0]]);
    // the hand's rotation is set at 10 only
    expect(swing.poses.map((p) => p[HAND]!.rotation[2])).toEqual([-90, -90, -90]);
    expect(swing.poses[0]![WHEEL]).toBeNull();
  });

  it("knows the keyframes either side of every frame", () => {
    expect(swing.leftRight[0]).toEqual([0, 1]);
    expect(swing.leftRight[9]).toEqual([0, 1]);
    expect(swing.leftRight[10]).toEqual([1, 2]);
    expect(swing.leftRight[19]).toEqual([2, 0]);
  });
});

describe("posing at a frame (ClientAnimator, ElementPose.Add)", () => {
  it("interpolates between resolved keyframes, round from the last to the first", () => {
    const rz = (f: number) => poseAt(anim("swing"), f)[ARM]!.rotation[2];
    expect(rz(5)).toBeCloseTo(45);
    expect(rz(12.5)).toBeCloseTo(67.5);
    expect(rz(17.5)).toBeCloseTo(22.5);
    expect(rz(19.999)).toBeCloseTo(0.009, 2);
  });

  it("turns the short way round with rotShortestDistance, and straight through without", () => {
    const spin = anim("spin");
    const at = (f: number) => poseAt(spin, f);
    // 350° to 10°: through 360°, not back through 180°
    expect(at(10)[WHEEL]!.rotation[0]).toBeCloseTo(360);
    expect(at(30)[WHEEL]!.rotation[0]).toBeCloseTo(0);
    expect(at(10)[ARM]!.rotation[1]).toBeCloseTo(180);
    expect(at(10)[WHEEL]!.stretch).toEqual([1, 1.5, 1]);
    expect(angleDegDistance(350, 10)).toBeCloseTo(20);
    expect(angleDegDistance(10, 350)).toBeCloseTo(-20);
    expect(angleDegDistance(0, 180)).toBeCloseTo(-180);
  });

  it("holds the end of an animation that holds, and wraps one that repeats", () => {
    const drop = anim("drop");
    expect(poseAt(drop, 4.5)[BASE]!.offset[1]).toBeCloseTo(-2);
    expect(poseAt(drop, 9.5)[BASE]!.offset[1]).toBeCloseTo(-4);
    const repeating = resolveAnimation({ ...shapeAnimations(shape)[2]!, onAnimationEnd: "Repeat" }, flat);
    expect(poseAt(repeating, 9.5)[BASE]!.offset[1]).toBeCloseTo(-2);
  });
});

describe("joint matrices", () => {
  const moved = (j: number, d: Map<number, Mat4>, p: Vec3) => apply(d.get(j)!, p);

  it("move the arm about its origin, offset in its turned frame, and the hand with it", () => {
    // At frame 10 the arm is turned 90° about z at (8, 2, 8) and offset 2 up in its own frame:
    // the hand's pivot, (8, 12, 8) at rest, goes to (-4, 2, 8).
    const d = jointDeltas(animated, flat, anim("swing"), 10);
    close(moved(ARM, d, [8, 12, 8]), [-4, 2, 8]);
    // the hand turns back by 90°, so it is only carried: a pure translation
    const hand = d.get(HAND)!;
    close([hand[0]!, hand[1]!, hand[2]!, hand[4]!, hand[5]!, hand[6]!, hand[8]!, hand[9]!, hand[10]!], [1, 0, 0, 0, 1, 0, 0, 0, 1]);
    close(moved(HAND, d, [8, 12, 8]), [-4, 2, 8]);
    // the base and wheel are not in this animation
    close(d.get(BASE)!, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
  });

  it("carry a child the animation does not name with its parent", () => {
    const d = jointDeltas(animated, flat, anim("drop"), 9);
    for (const j of [BASE, ARM, HAND]) close(moved(j, d, [1, 2, 3]), [1, -2, 3]);
    close(moved(WHEEL, d, [1, 2, 3]), [1, 2, 3]);
  });

  it("are all the identity at rest", () => {
    for (const m of jointDeltas(animated, flat, null, 0).values()) close(m, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
  });
});

describe("playing", () => {
  it("runs 30 frames a second at speed 1, wrapping when it loops and stopping at the end when not", () => {
    const swing = anim("swing");
    expect(advanceFrame(swing, 0, 0.1, 1, true)).toEqual({ frame: 3, ended: false });
    expect(advanceFrame(swing, 0, 0.1, 2, true).frame).toBeCloseTo(6);
    expect(advanceFrame(swing, 18, 0.1, 1, true).frame).toBeCloseTo(1);
    expect(advanceFrame(swing, 18, 0.1, 1, false)).toEqual({ frame: 19, ended: true });
    expect(advanceFrame(swing, 1, 0.1, -1, false)).toEqual({ frame: 0, ended: true });
    expect(advanceFrame(swing, 1, 0.1, -1, true).frame).toBeCloseTo(18);
  });
});

describe("scenario.animations", () => {
  const codes = animated.animations.map((a) => a.code);

  it("must name animations the shape has", () => {
    expect(checkAnimations({ default: "swing", labels: { spin: "Spinning" }, speeds: { drop: 1.5 }, groups: [{ label: "Arm", codes: ["swing"] }] }, codes)).toEqual([]);
    expect(checkAnimations({ default: "Swing" }, codes)).toEqual([]);
    expect(checkAnimations({ default: "fly" }, codes)).toEqual(['animations.default "fly" is not an animation of the shape']);
    expect(checkAnimations({ speeds: { swing: 0 }, labels: { x: "X" } }, codes)).toEqual(['animations.labels: "x" is not an animation of the shape', 'animations.speeds: "swing" must be above 0']);
    expect(checkAnimations({ groups: [{ label: "A", codes: ["swing"] }, { label: "B", codes: ["swing", "nope"] }] }, codes)).toEqual([
      'animation group "B": "swing" is in another group too',
      'animation group "B": "nope" is not an animation of the shape',
    ]);
    expect(checkAnimations({}, [])).toEqual(["the shape has no animations"]);
  });

  it("orders the select by its groups, then the rest, with labels", () => {
    expect(animationOptions(animated.animations, { labels: { drop: "Falling" }, groups: [{ label: "Moving", codes: ["spin", "drop"] }] })).toEqual([
      { label: "Moving", options: [{ code: "spin", label: "Spin (spin)" }, { code: "drop", label: "Falling" }] },
      { label: null, options: [{ code: "swing", label: "Swing (swing)" }] },
    ]);
    expect(animationSpeed({ speeds: { Spin: 1.5 } }, "spin")).toBe(1.5);
    expect(animationSpeed(undefined, "spin")).toBe(1);
  });
});

describe("the manifest", () => {
  const model = { id: "fixture", title: "t", description: "d", credit: "c", shape: "s.json" };
  const publish = (m: object, s: unknown = shape) => publishModels({ models: [{ ...model, ...m }] }, () => s);

  it("takes a scenario of only animations without a rig, and checks it against the shape", () => {
    expect(publish({ scenario: { animations: { default: "spin", groups: [{ label: "Arm", codes: ["swing"] }] } } }).index.models[0]!.scenario).toEqual({
      animations: { default: "spin", groups: [{ label: "Arm", codes: ["swing"] }] },
    });
    expect(() => publish({ scenario: { animations: { default: "fly" } } })).toThrow(/scenario: animations.default "fly" is not an animation of the shape/);
    expect(() => publish({ scenario: { animations: {}, requires: {} } })).toThrow(/a scenario needs a rig, unless it only describes the shape's animations/);
  });

  it("refuses a shape with an animation the game could not run", () => {
    const broken = { ...shape, animations: [{ code: "x", quantityframes: 4, keyframes: [{ frame: 4, elements: {} }] }] };
    expect(() => publish({}, broken)).toThrow(/s.json: animation "x": a keyframe at frame 4/);
  });
});
