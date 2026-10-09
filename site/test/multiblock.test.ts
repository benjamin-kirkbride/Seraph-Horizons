// The multiblock viewer's data and logic: what prepare-data writes for schema/examples/minimal.json's
// one structure, its view (kinds, counts, sizes), a block's boxes, and slicing.
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { BlockShape, RecipeExport } from "../src/lib/export.ts";
import type { Meta, MultiblockFile, MultiblockIndex } from "../src/lib/format.ts";
import { billOfMaterials, blockBoxes, buildMultiblockView, cellShown, fullSlice, shownCells, shownCounts } from "../src/lib/multiblock-view.ts";
import { multiblockFiles, prepareData } from "../src/lib/prepare.ts";
import { apply, type Vec3 } from "../src/lib/rig.ts";
import { formatRoute, parseRoute, withVersion } from "../src/lib/route.ts";

const minimal = JSON.parse(readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8")) as RecipeExport;
const file = () => multiblockFiles(minimal).files.get("multiblocks/0.json")!;

const close = (a: Vec3, b: Vec3) => a.forEach((v, i) => expect(v).toBeCloseTo(b[i]!, 6));

describe("multiblock routes", () => {
  it("reads and writes the list and a structure", () => {
    expect(parseRoute("#/main/multiblocks")).toEqual({ view: "multiblocks", version: "main" });
    expect(parseRoute("#/main/multiblock/smex:blastfurnacedoor")).toEqual({ view: "multiblock", version: "main", id: "smex:blastfurnacedoor" });
    expect(formatRoute({ view: "multiblock", version: "v1.0.0", id: "smex:blastfurnacedoor" })).toBe("#/v1.0.0/multiblock/smex:blastfurnacedoor");
    expect(formatRoute({ view: "multiblocks", version: "main" })).toBe("#/main/multiblocks");
    expect(parseRoute("#/main/multiblock/not an id")).toEqual({ view: "notfound", version: "main" });
    expect(parseRoute("#/main/multiblocks/extra")).toEqual({ view: "notfound", version: "main" });
  });

  it("keeps the structure when the version changes", () => {
    expect(withVersion({ view: "multiblock", version: "main", id: "a:b" }, "v1")).toEqual({ view: "multiblock", version: "v1", id: "a:b" });
  });
});

describe("multiblock data", () => {
  it("writes an index and a file per structure with only the shapes it uses", () => {
    const { files, meta } = prepareData(minimal);
    expect((meta as Meta).multiblockCount).toBe(1);
    expect(files.get("multiblocks.json") as MultiblockIndex).toEqual({
      multiblocks: [{ id: "examplemod:kiln", name: "Kiln", mod: "examplemod", cells: 6, sizes: ["Short", "Tall"], file: 0 }],
    });
    const f = files.get("multiblocks/0.json") as MultiblockFile;
    expect(f.structure.id).toBe("examplemod:kiln");
    expect(Object.keys(f.shapes).sort()).toEqual(["examplemod:kiln-north", "game:claybricks-fire", "game:multiblock-monolithic-0-p1-0"]);
  });

  it("writes nothing for an export without multiblocks", () => {
    const { files, meta } = prepareData({ ...minimal, multiblocks: undefined });
    expect(meta.multiblockCount).toBeUndefined();
    expect([...files.keys()].some((k) => k.startsWith("multiblocks"))).toBe(false);
  });
});

describe("multiblock view", () => {
  it("shows the default size, with a kind and count per part", () => {
    const { structure, shapes } = file();
    const v = buildMultiblockView(structure, shapes);
    expect(v.size).toBe(1);
    expect(v.cells).toHaveLength(6);
    expect(v.parts.map((p) => [p.kind, p.count, p.label])).toEqual([
      ["block", 1, "Kiln"],
      ["block", 2, "Fire bricks"],
      ["air", 2, "Air"],
      ["filler", 1, "game:multiblock-monolithic-0-p1-0"],
    ]);
    expect(v.min).toEqual([0, 0, 0]);
    expect(v.max).toEqual([0, 3, 1]);
    // Only what a builder places, most first.
    expect(billOfMaterials(v).map((p) => p.label)).toEqual(["Fire bricks", "Kiln"]);
    // Outline parts have nothing to draw.
    expect(v.boxes[2]).toEqual([]);
    expect(v.boxes[3]).toEqual([]);
  });

  it("takes another size, clamped", () => {
    const { structure, shapes } = file();
    expect(buildMultiblockView(structure, shapes, 0).cells).toHaveLength(5);
    expect(buildMultiblockView(structure, shapes, 9).size).toBe(1);
  });

  it("an air part that names a block is still left empty", () => {
    const { structure, shapes } = file();
    const s = structuredClone(structure);
    s.parts[1]!.air = true;
    expect(buildMultiblockView(s, shapes).parts[1]!.kind).toBe("air");
  });
});

describe("block boxes", () => {
  it("draws a cube as the whole cell", () => {
    const [b] = blockBoxes({ draw: "cube" });
    close(apply(b!.m, b!.from), [0, 0, 0]);
    close(apply(b!.m, b!.to), [1, 1, 1]);
    expect(blockBoxes({ draw: "none" })).toEqual([]);
    expect(blockBoxes(undefined)).toHaveLength(1);
  });

  it("turns the shape about the cell's centre and places children from their parent's from", () => {
    const shape = file().shapes["examplemod:kiln-north"]!;
    const boxes = blockBoxes(shape);
    expect(boxes).toHaveLength(2);
    // rotateY 180: the base still fills the bottom half of the cell.
    const base = boxes[0]!;
    close(apply(base.m, base.from), [1, 0, 1]);
    close(apply(base.m, base.to), [0, 0.5, 0]);
    expect(base.faces).toHaveLength(6);
    // The lid, a child: from [2, 8, 2] relative to the base's from (its origin), 8..10 voxels up.
    const lid = boxes[1]!;
    expect(lid.faces).toEqual(["up"]);
    const centre = apply(lid.m, [8, 9, 8]);
    close(centre, [0.5, 9 / 16, 0.5]);
  });

  it("applies an offset in blocks", () => {
    const s: BlockShape = { draw: "shape", shapes: [{ elements: [{ name: "a", from: [0, 0, 0], to: [16, 16, 16] }], offset: [0, -0.5, 0] }] };
    const [b] = blockBoxes(s);
    close(apply(b!.m, b!.from), [0, -0.5, 0]);
  });
});

describe("slicing", () => {
  it("shows everything uncut, then cuts each axis", () => {
    const { structure, shapes } = file();
    const v = buildMultiblockView(structure, shapes);
    const all = fullSlice(v);
    expect(shownCells(v, all).every(Boolean)).toBe(true);
    // Layers up to y 1.
    const upTo1 = structuredClone(all);
    upTo1[1] = { at: 1, only: false };
    expect(shownCells(v, upTo1).filter(Boolean)).toHaveLength(4);
    // Only layer 1: the filler above the kiln and the air in the chimney.
    const only1 = structuredClone(all);
    only1[1] = { at: 1, only: true };
    const shown = shownCells(v, only1);
    expect(shownCounts(v, shown)).toEqual([0, 0, 1, 1]);
    // A z cut keeps the front row.
    expect(cellShown([0, 3, 1], [all[0], all[1], { at: 0, only: false }])).toBe(false);
    expect(cellShown([0, 0, 0], [all[0], all[1], { at: 0, only: false }])).toBe(true);
  });
});
