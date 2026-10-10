// What the model page shows for a shape and an optional rig: elements grouped into the rig's
// parts (or one part without a rig), texture groups, colours, the controls the rig's inputs
// need, and its overlays. Pure, so the page and the tests read the same thing.
import { animateShape, type AnimatedShape } from "./keyframes.ts";
import { discoverAnchors, footprintBounds, type Anchor, type Bounds } from "./model-anchors.ts";
import type { Scenario } from "./model-scenario.ts";
import {
  compileGlobs,
  corners,
  flattenShape,
  partOf,
  requiresValues,
  rideOrder,
  rigInputs,
  textureCodes,
  workOf,
  type FlatElement,
  type Rig,
  type RigCell,
  type RigInputs,
  type RigPart,
  type Shape,
  type Work,
  type Vec3,
} from "./rig.ts";

export interface ViewPart {
  id: string;
  /** The rig's part, or a stand-in without drivers ("model" with no rig, "unmatched" for elements no glob claims). */
  part: RigPart;
  colour: string;
  /** Element indices, in shape order. */
  elements: number[];
  moving: boolean;
}

export interface ViewTexture {
  code: string;
  path: string | null;
  colour: string;
  elements: number[];
}

export interface ModelView {
  flat: FlatElement[];
  /** Index into parts of each element. */
  elementPart: number[];
  /** Texture codes of each element's drawn faces. */
  elementTextures: string[][];
  parts: ViewPart[];
  /** The rig's parts in ride order, or the stand-in's. */
  order: number[];
  textures: ViewTexture[];
  inputs: RigInputs;
  requires: { value: string; label: string }[];
  anchors: Anchor[];
  unrecognised: string[];
  /** The rig's footprint cells, or none. */
  cells: RigCell[];
  /** The footprint from the rig's cells, or else the model's bounding box in whole blocks. */
  bounds: Bounds;
  hasRig: boolean;
  /** The rig's progress (its work or trunkPath), which gauge and roll drivers and a travelling prop read; null without one. */
  path: Work | null;
  /** The shape's own keyframe animations, resolved, and each element's joint (keyframes.ts); no animations when it has none. */
  animation: AnimatedShape;
}

export const STATIC_COLOUR = "#c8b090";
const UNMATCHED_COLOUR = "#e03131";

/** A distinct colour per moving part: hues a golden angle apart. */
export function partColour(i: number): string {
  return hslHex((i * 137.508 + 8) % 360, 0.62, 0.5);
}

const TEXTURE_FAMILIES: [RegExp, string][] = [
  [/glass/, "#cfe3ea"],
  [/^oil\b|honey|lubric/, "#c8902a"],
  [/oak|wood|plank|log|debarked|bark|timber/, "#b88a58"],
  // Contents that would otherwise take the metal colour of the pan they lie in (the amalgam pan's).
  [/mercury|quicksilver/, "#dce2ea"],
  [/amalgam/, "#b0aa9c"],
  // The rocker's water, and its concentrate (black sand), which would otherwise take the stone colour of its gravel.
  [/water/, "#6f9fcf"],
  [/concentrate/, "#57534c"],
  [/metal|iron|steel|copper|bronze|tin|plate|ingot|gold|silver/, "#8e98a4"],
  [/rope|cloth|linen|wool|flax|twine/, "#c9b27c"],
  [/stone|rock|granite|andesite|cobble|brick|clay/, "#9a9a92"],
  [/leather|hide/, "#8a5a3c"],
];

/** A colour for a texture: by its material when the code or path names one, else hashed from the code. */
export function textureColour(code: string, path: string | null): string {
  const text = `${code} ${path ?? ""}`.toLowerCase();
  for (const [re, colour] of TEXTURE_FAMILIES) if (re.test(text)) return colour;
  let h = 0;
  for (const ch of code) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return hslHex(h % 360, 0.35, 0.6);
}

function hslHex(h: number, s: number, l: number): string {
  const f = (n: number) => {
    const k = (n + h / 30) % 12;
    const c = l - s * Math.min(l, 1 - l) * Math.max(-1, Math.min(k - 3, 9 - k, 1));
    return Math.round(c * 255)
      .toString(16)
      .padStart(2, "0");
  };
  return `#${f(0)}${f(8)}${f(4)}`;
}

export function buildModelView(shape: Shape, rig: Rig | null, scenario?: Scenario): ModelView {
  const flat = flattenShape(shape.elements);
  const rigParts = rig?.parts ?? [];
  const parts: RigPart[] = rigParts.length > 0 ? [...rigParts] : [{ id: "model", match: ["*"], requires: null, drivers: [] }];
  const order = rideOrder(parts);
  const globs = compileGlobs(parts);
  const elementPart = flat.map((f) => partOf(globs, f.chain));
  const unmatched = elementPart.some((p) => p < 0);
  if (unmatched) {
    const u = parts.length;
    parts.push({ id: "unmatched", match: [], requires: null, drivers: [] });
    order.push(u);
    elementPart.forEach((p, i) => {
      if (p < 0) elementPart[i] = u;
    });
  }
  let colourIndex = 0;
  const moves = (p: RigPart) => (p.drivers?.length ?? 0) > 0 || p.ride != null;
  // A rig of static parts only (the eidolon's build stages) gives each its own colour, as there is no
  // moving part for the static colour to stand apart from.
  const allStatic = rigParts.length > 1 && !rigParts.some(moves);
  const view: ViewPart[] = parts.map((p, i) => {
    const moving = moves(p);
    const colour = unmatched && i === parts.length - 1 ? UNMATCHED_COLOUR : moving || allStatic ? partColour(colourIndex++) : STATIC_COLOUR;
    return { id: p.id, part: p, colour, elements: [], moving };
  });
  elementPart.forEach((p, i) => view[p]!.elements.push(i));

  const elementTextures = flat.map((f) => textureCodes(f.element));
  const byCode = new Map<string, number[]>();
  elementTextures.forEach((codes, i) => {
    for (const c of codes.length > 0 ? codes : ["?"]) {
      const list = byCode.get(c) ?? [];
      list.push(i);
      byCode.set(c, list);
    }
  });
  const textures = [...byCode.entries()]
    .sort((a, b) => b[1].length - a[1].length || a[0].localeCompare(b[0]))
    .map(([code, elements]) => {
      const path = shape.textures?.[code] ?? null;
      return { code, path, colour: textureColour(code, path), elements };
    });

  const labels = scenario?.requires ?? {};
  const requires = requiresValues(parts).map((value) => ({ value, label: labels[value] ?? value }));
  const { anchors, unrecognised } = rig ? discoverAnchors(rig) : { anchors: [], unrecognised: [] };
  const bounds = footprintBounds(rig?.cells) ?? modelBounds(flat);
  return {
    flat,
    elementPart,
    elementTextures,
    parts: view,
    order,
    textures,
    inputs: rigInputs(parts),
    requires,
    anchors,
    unrecognised,
    cells: rig?.cells ?? [],
    bounds,
    hasRig: rig !== null,
    path: workOf(rig),
    animation: animateShape(shape, flat),
  };
}

/** The model's bounding box, in whole blocks. */
export function modelBounds(flat: readonly FlatElement[]): Bounds {
  if (flat.length === 0) return { lo: [0, 0, 0], hi: [1, 1, 1] };
  const lo: Vec3 = [Infinity, Infinity, Infinity];
  const hi: Vec3 = [-Infinity, -Infinity, -Infinity];
  for (const f of flat)
    for (const c of corners(f))
      for (let k = 0; k < 3; k++) {
        lo[k] = Math.min(lo[k]!, c[k]! / 16);
        hi[k] = Math.max(hi[k]!, c[k]! / 16);
      }
  return { lo: lo.map(Math.floor) as Vec3, hi: hi.map(Math.ceil) as Vec3 };
}

const num = (v: number) => String(Math.round(v * 10000) / 10000);
const vec = (v: readonly number[]) => `[${v.map(num).join(", ")}]`;

/** An element's details as label/value rows, for the pinned panel. */
export function elementDetails(view: ModelView, i: number): { label: string; value: string }[] {
  const f = view.flat[i]!;
  const e = f.element;
  const rows = [
    { label: "from", value: vec(e.from) },
    { label: "to", value: vec(e.to) },
  ];
  const rot = (["X", "Y", "Z"] as const).filter((k) => e[`rotation${k}`]).map((k) => `${k.toLowerCase()} ${num(e[`rotation${k}`]!)}°`);
  if (rot.length > 0) rows.push({ label: "rotation", value: rot.join(", ") });
  if (e.rotationOrigin && rot.length > 0) rows.push({ label: "origin", value: vec(e.rotationOrigin) });
  return rows;
}
