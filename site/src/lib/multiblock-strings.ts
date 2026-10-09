// The multiblock viewer's strings (#/<version>/multiblocks), apart from strings.ts so that they
// load with the viewer. Same rules as strings.ts: another language is one more object of the
// same shape.

const en = {
  heading: "Multiblocks",
  intro:
    "Structures you build block by block: place the blocks as shown and the block at the heart of it checks the layout. Each block is drawn in its own shape, one flat colour per kind; slice through the layers to see inside.",
  none: "This version has no multiblocks.",
  loadFailed: "Could not load the multiblocks.",
  unknown: (id: string) => `There is no multiblock “${id}” in this version.`,
  all: "All multiblocks",
  cells: (n: number) => `${n.toLocaleString("en")} cell${n === 1 ? "" : "s"}`,
  sizes: (labels: string[]) => `${labels.length} sizes`,
  checkedBy: "Checked by",
  variants: (n: number) => `and ${n - 1} other variant${n === 2 ? "" : "s"}`,
  from: "From",
  size: "Size",
  noWebgl: "This browser could not start WebGL, so the structure cannot be drawn. The materials and the layers below still work.",
  viewerFailed: "The 3D viewer could not be loaded. Reload the page to try again.",
  loadingViewer: "Loading the 3D view…",
  stageLabel: (name: string) => `3D view of the ${name}. Drag to orbit, right-drag or two fingers to pan, scroll or pinch to zoom; click a block to pick its kind.`,
  views: "Camera",
  viewNames: { angled: "Angled", opposite: "Opposite", front: "Front", side: "Side", top: "Top" },
  slice: "Slice",
  axes: ["West → east (x)", "Layer (y)", "North → south (z)"] as const,
  upTo: (at: number, last: number) => (at >= last ? `all ${last}` : `1 to ${at} of ${last}`),
  only: (at: number, last: number) => `${at} of ${last} only`,
  onlyThis: "Only this one",
  resetSlice: "Show everything",
  display: "Display",
  outlines: "Outline empty and self-filling cells",
  edges: "Block edges",
  materials: "Materials",
  materialsHint: "Click a kind to pick it out; click it again to show all.",
  shownOf: (shown: number, total: number) => (shown === total ? `${total}` : `${shown} / ${total}`),
  mustBeEmpty: "Must be left empty",
  emptyOr: (name: string) => `Empty, or ${name}`,
  filler: "Fills itself: part of a bigger block or of the machine",
  pattern: "Layout code",
  accepts: "Any of",
  more: (n: number) => `and ${n.toLocaleString("en")} more`,
  picked: (name: string, x: number, y: number, z: number) => `${name} at x ${x}, y ${y}, z ${z}`,
  frame: "Coordinates are blocks from the checking block, in the layout's own frame (north is −z), as it is written before the game turns it to face the way you placed it.",
};

export type MultiblockStrings = typeof en;

export const mbt: MultiblockStrings = en;
