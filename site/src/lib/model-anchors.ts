// The overlays a model viewer draws for a rig: its footprint cells and collision boxes, and
// every anchor the rig declares. Anchors are recognised by shape, not by name, so a new
// machine's rig gets overlays without changes here:
//
//   "<name>Cell": [x, y, z]               a cell, outlined (with "<name>Face": a side, that face shaded)
//   "<name>Side": "west"                  a side of the footprint, an arrow pointing in
//   "<name>": { "pos": [x, y, z] }        a point (with "<name>Side", an arrow out that way)
//   "<name>": { "origin", "axis", "length" }   a line along an axis, centred on origin; with
//                                         "stations": { "<station>": number }, a mark at each
//                                         place along the axis (a trunk path's stations)
//   "<name>": { "<something>Y": number }  a height, a level line across the footprint
//
// Keys starting with "_" are comments; "cells" and "parts" are not anchors. Everything is in
// blocks, in the rig's frame. A cell with "hollow": true and no boxes of its own has no
// collision box (a trunk path's cells: solid only where the trunk is, which the game adds).
import type { Axis, Rig, RigCell, Vec3 } from "./rig.ts";

export const SIDES = ["north", "east", "south", "west", "up", "down"] as const;
export type Side = (typeof SIDES)[number];

/** Outward normals in VS's frame: north is −z, east +x. */
export const SIDE_NORMAL: Record<Side, Vec3> = {
  north: [0, 0, -1],
  south: [0, 0, 1],
  east: [1, 0, 0],
  west: [-1, 0, 0],
  up: [0, 1, 0],
  down: [0, -1, 0],
};

export type Anchor =
  | { kind: "cell"; key: string; label: string; pos: Vec3; face?: Side }
  | { kind: "side"; key: string; label: string; side: Side }
  | { kind: "point"; key: string; label: string; pos: Vec3; side?: Side }
  | { kind: "line"; key: string; label: string; origin: Vec3; axis: Axis; length: number; marks?: LineMark[] }
  | { kind: "level"; key: string; label: string; y: number };

/** A named place along a line anchor's axis: the coordinate on that axis, blocks. */
export interface LineMark {
  name: string;
  label: string;
  at: number;
}

const isSide = (v: unknown): v is Side => typeof v === "string" && (SIDES as readonly string[]).includes(v);
const isVec3 = (v: unknown): v is Vec3 => Array.isArray(v) && v.length === 3 && v.every((n) => typeof n === "number" && Number.isFinite(n));
const isAxis = (v: unknown): v is Axis => v === "x" || v === "y" || v === "z";
const isObject = (v: unknown): v is Record<string, unknown> => typeof v === "object" && v !== null && !Array.isArray(v);

/** "powerCell" → "Power cell", "trunkBed" → "Trunk bed". */
export function humanize(key: string): string {
  const words = key
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[_-]+/g, " ")
    .trim()
    .toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** Every anchor the rig declares, in file order, and the keys that are not recognised. */
export function discoverAnchors(rig: Rig): { anchors: Anchor[]; unrecognised: string[] } {
  const anchors: Anchor[] = [];
  const unrecognised: string[] = [];
  for (const [key, value] of Object.entries(rig)) {
    if (key.startsWith("_") || key === "cells" || key === "parts") continue;
    const stem = key.replace(/(Cell|Side|Face)$/, "");
    if (key.endsWith("Cell") && isVec3(value)) {
      const face = rig[`${stem}Face`];
      anchors.push({ kind: "cell", key, label: isSide(face) ? `${humanize(key)} (${face} face)` : humanize(key), pos: value, ...(isSide(face) ? { face } : {}) });
    } else if (key.endsWith("Face") && isSide(value) && isVec3(rig[`${stem}Cell`])) {
      // Taken with its cell, wherever the cell comes in the file.
      continue;
    } else if (key.endsWith("Side") && isSide(value)) {
      anchors.push({ kind: "side", key, label: `${humanize(stem)} (${value})`, side: value });
    } else if (isObject(value) && isVec3(value.pos)) {
      const side = rig[`${key}Side`];
      anchors.push({ kind: "point", key, label: humanize(key), pos: value.pos, ...(isSide(side) ? { side } : {}) });
    } else if (isObject(value) && isVec3(value.origin) && isAxis(value.axis) && typeof value.length === "number") {
      const marks = isObject(value.stations)
        ? Object.entries(value.stations)
            .filter((e): e is [string, number] => typeof e[1] === "number" && Number.isFinite(e[1]))
            .map(([name, at]) => ({ name, label: humanize(name), at }))
        : [];
      anchors.push({ kind: "line", key, label: humanize(key), origin: value.origin, axis: value.axis, length: value.length, ...(marks.length > 0 ? { marks } : {}) });
    } else if (isObject(value) && Object.entries(value).some(([k, v]) => /Y$/.test(k) && typeof v === "number")) {
      for (const [k, v] of Object.entries(value))
        if (/Y$/.test(k) && typeof v === "number") anchors.push({ kind: "level", key: `${key}.${k}`, label: `${humanize(key)}: ${k}`, y: v });
    } else {
      unrecognised.push(key);
    }
  }
  return { anchors, unrecognised };
}

export interface Bounds {
  lo: Vec3;
  hi: Vec3;
}

/** The blocks the cells cover, always including the origin cell [0, 0, 0]; null without cells. */
export function footprintBounds(cells: readonly RigCell[] | undefined): Bounds | null {
  if (!cells || cells.length === 0) return null;
  const lo: Vec3 = [0, 0, 0];
  const hi: Vec3 = [1, 1, 1];
  for (const c of cells)
    for (let k = 0; k < 3; k++) {
      lo[k] = Math.min(lo[k]!, c.pos[k]!);
      hi[k] = Math.max(hi[k]!, c.pos[k]! + 1);
    }
  return { lo, hi };
}

/** A cell's collision boxes in blocks: its `boxes` (cell-local 0..1) moved to the cell, else none for a hollow cell and the whole cell for any other. */
export function cellBoxes(cell: RigCell): Bounds[] {
  const own = cell.boxes && cell.boxes.length > 0 ? cell.boxes : null;
  if (!own && cell.hollow === true) return [];
  const boxes = own ?? [[0, 0, 0, 1, 1, 1]];
  const [x, y, z] = cell.pos;
  return boxes.map((b) => ({ lo: [x + b[0]!, y + b[1]!, z + b[2]!], hi: [x + b[3]!, y + b[4]!, z + b[5]!] }));
}

/**
 * Where a side's arrow goes: just outside the footprint on that side, pointing in. Its other
 * coordinates follow the first line anchor running along the side's normal (material enters
 * along a bed, so the arrow sits on it), or else the middle of the side at the floor.
 */
export function sideArrow(side: Side, bounds: Bounds, anchors: readonly Anchor[], gap = 1.3): { from: Vec3; dir: Vec3 } {
  const n = SIDE_NORMAL[side];
  const k = n.findIndex((v) => v !== 0);
  const axis = (["x", "y", "z"] as const)[k]!;
  const line = anchors.find((a): a is Extract<Anchor, { kind: "line" }> => a.kind === "line" && a.axis === axis);
  const at: Vec3 = line
    ? [line.origin[0], line.origin[1] + 0.5, line.origin[2]]
    : [(bounds.lo[0] + bounds.hi[0]) / 2, bounds.lo[1] + 0.5, (bounds.lo[2] + bounds.hi[2]) / 2];
  at[k] = n[k]! > 0 ? bounds.hi[k]! : bounds.lo[k]!;
  return { from: at.map((v, i) => v + n[i]! * gap) as Vec3, dir: n.map((v) => -v) as Vec3 };
}
