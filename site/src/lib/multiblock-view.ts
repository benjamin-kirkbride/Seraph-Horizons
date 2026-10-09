// What a multiblock's page shows, as pure functions: each part's colour, kind and count (the
// legend and bill of materials), each block's boxes in its cell (its real shape, flattened the
// way the model viewer flattens one), and which cells a slice leaves showing.
// docs/recipe-browser/multiblocks.md describes the page.
import type { BlockShape, ExportShapeElement, Multiblock, MultiblockPart } from "./export.ts";
import { partColour } from "./model-view.ts";
import { FACE_NAMES, about, flattenShape, multiply, rotation, translation, type FaceName, type Mat4, type ShapeElement, type Vec3 } from "./rig.ts";

/**
 * - `block`: a block goes here, drawn solid.
 * - `air`: the cell must be empty (`pattern` only matches air), or may be (a block that also
 *   fits is named): drawn as an outline, since a builder leaves it empty.
 * - `filler`: a block the structure fills itself, or the invisible part of a bigger block (the
 *   top half of a door): nothing of its own to draw or place, drawn as an outline.
 */
export type PartKind = "block" | "air" | "filler";

export interface ViewPart {
  index: number;
  part: MultiblockPart;
  kind: PartKind;
  colour: string;
  /** The block's name, or its code, or "Air". */
  label: string;
  /** Cells of the size shown that take this part. */
  count: number;
}

export interface Box {
  /** Element-local voxels (`from`..`to`) to cell-local blocks (0..1 is the cell). */
  m: Mat4;
  from: Vec3;
  to: Vec3;
  /** The faces drawn. */
  faces: readonly FaceName[];
}

export interface ViewCell {
  pos: Vec3;
  part: number;
}

export interface MultiblockView {
  structure: Multiblock;
  size: number;
  cells: ViewCell[];
  parts: ViewPart[];
  /** Boxes of each part's block, by part index; empty for one drawn as an outline. */
  boxes: Box[][];
  /** Smallest and largest cell coordinate on each axis. */
  min: Vec3;
  max: Vec3;
}

export const AIR_COLOUR = "#7a8ba0";
export const FILLER_COLOUR = "#b08cd8";

export function partKind(part: MultiblockPart, shape: BlockShape | undefined): PartKind {
  if (part.air || part.block === undefined) return "air";
  if (shape?.draw === "none") return "filler";
  return "block";
}

function toShapeElement(e: ExportShapeElement): ShapeElement {
  const s: ShapeElement = { name: e.name, from: e.from, to: e.to };
  if (e.rotationOrigin) s.rotationOrigin = e.rotationOrigin;
  if (e.rotationX) s.rotationX = e.rotationX;
  if (e.rotationY) s.rotationY = e.rotationY;
  if (e.rotationZ) s.rotationZ = e.rotationZ;
  if (e.faces) s.faces = Object.fromEntries(e.faces.map((f) => [f, {}]));
  if (e.children) s.children = e.children.map(toShapeElement);
  return s;
}

/** Voxels to blocks. */
const TO_BLOCKS: Mat4 = [1 / 16, 0, 0, 0, 0, 1 / 16, 0, 0, 0, 0, 1 / 16, 0, 0, 0, 0, 1];
const UNIT: Box = { m: TO_BLOCKS, from: [0, 0, 0], to: [16, 16, 16], faces: FACE_NAMES };

/**
 * A block's boxes in its cell: each element of its shape and overlays, turned as the block
 * turns them (rotateX, Y, Z about the cell's centre, VS's order), scaled about the centre,
 * then offset. A cube is one box; nothing to draw is none.
 */
export function blockBoxes(shape: BlockShape | undefined): Box[] {
  if (!shape || shape.draw === "cube") return [UNIT];
  if (shape.draw === "none") return [];
  const boxes: Box[] = [];
  const deg = Math.PI / 180;
  for (const c of shape.shapes ?? []) {
    const turn = about(multiply(rotation("x", (c.rotateX ?? 0) * deg), multiply(rotation("y", (c.rotateY ?? 0) * deg), rotation("z", (c.rotateZ ?? 0) * deg))), [8, 8, 8]);
    const s = c.scale ?? 1;
    const scale = about([s, 0, 0, 0, 0, s, 0, 0, 0, 0, s, 0, 0, 0, 0, 1], [8, 0, 8]);
    const o = c.offset ?? [0, 0, 0];
    const block = multiply(translation(o[0], o[1], o[2]), multiply(TO_BLOCKS, multiply(turn, scale)));
    for (const f of flattenShape(c.elements.map(toShapeElement))) {
      const e = f.element;
      const faces = e.faces ? FACE_NAMES.filter((n) => e.faces![n]) : FACE_NAMES;
      if (faces.length === 0) continue;
      boxes.push({ m: multiply(block, f.world), from: e.from, to: e.to, faces });
    }
  }
  return boxes;
}

export function partLabel(part: MultiblockPart): string {
  if (part.block === undefined) return "Air";
  return part.name ?? part.block;
}

export function buildMultiblockView(structure: Multiblock, shapes: Record<string, BlockShape>, size = structure.defaultSize ?? 0): MultiblockView {
  const s = Math.min(Math.max(0, size), structure.sizes.length - 1);
  const cells: ViewCell[] = structure.sizes[s]!.cells.map(([x, y, z, part]) => ({ pos: [x, y, z], part }));
  const counts = new Array<number>(structure.parts.length).fill(0);
  for (const c of cells) counts[c.part]!++;
  const parts = structure.parts.map((part, index): ViewPart => {
    const kind = partKind(part, part.block !== undefined ? shapes[part.block] : undefined);
    return {
      index,
      part,
      kind,
      colour: kind === "air" ? AIR_COLOUR : kind === "filler" ? FILLER_COLOUR : partColour(index),
      label: partLabel(part),
      count: counts[index]!,
    };
  });
  const boxes = parts.map((p) => (p.kind === "block" ? blockBoxes(shapes[p.part.block!]) : []));
  const min: Vec3 = [Infinity, Infinity, Infinity];
  const max: Vec3 = [-Infinity, -Infinity, -Infinity];
  for (const c of cells)
    for (let k = 0; k < 3; k++) {
      min[k] = Math.min(min[k]!, c.pos[k]!);
      max[k] = Math.max(max[k]!, c.pos[k]!);
    }
  if (cells.length === 0) {
    min.fill(0);
    max.fill(0);
  }
  return { structure, size: s, cells, parts, boxes, min, max };
}

/** The bill of materials: parts a builder places, most first, then by name. */
export function billOfMaterials(view: MultiblockView): ViewPart[] {
  return view.parts.filter((p) => p.kind === "block" && p.count > 0).sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
}

// ---- slicing

export type Axis = 0 | 1 | 2;

/** A cut on one axis: the cells up to `at`, or only those at `at`. */
export interface Cut {
  at: number;
  only: boolean;
}

export type Slice = [Cut, Cut, Cut];

/** No cut: everything shows. */
export function fullSlice(view: MultiblockView): Slice {
  return [0, 1, 2].map((k) => ({ at: view.max[k]!, only: false })) as Slice;
}

export function cellShown(pos: Vec3, slice: Slice): boolean {
  for (let k = 0; k < 3; k++) {
    const c = slice[k]!;
    if (c.only ? pos[k] !== c.at : pos[k]! > c.at) return false;
  }
  return true;
}

/** Which of the view's cells show under `slice`. */
export function shownCells(view: MultiblockView, slice: Slice): boolean[] {
  return view.cells.map((c) => cellShown(c.pos, slice));
}

/** Counts of each part among the cells that show. */
export function shownCounts(view: MultiblockView, shown: readonly boolean[]): number[] {
  const n = new Array<number>(view.parts.length).fill(0);
  view.cells.forEach((c, i) => {
    if (shown[i]) n[c.part]!++;
  });
  return n;
}
