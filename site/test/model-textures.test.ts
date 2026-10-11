import { describe, expect, it } from "vitest";
import { checkScenario, openingTextures, pickedTextures, type TextureChoice } from "../src/lib/model-scenario.ts";
import { buildModelView, withPickedTextures } from "../src/lib/model-view.ts";
import type { Rig, Shape } from "../src/lib/rig.ts";

// A scenario's texture selects: each sets one texture code's path and colour.
const rig: Rig = { parts: [] };
const brick: TextureChoice = {
  label: "Brick",
  code: "brick",
  default: "red",
  options: [
    { id: "red", label: "Red", path: "game:block/red", colour: "#aa3322" },
    { id: "cream", label: "Cream", path: "game:block/cream", colour: "#ddccaa" },
  ],
};
const shape: Shape = {
  textures: { brick: "game:block/shape-own", iron: "game:block/iron" },
  elements: [
    { name: "wall", from: [0, 0, 0], to: [16, 16, 16], faces: { north: { texture: "#brick" }, up: { texture: "#iron" } } },
    { name: "band", from: [0, 16, 0], to: [16, 17, 16], faces: { north: { texture: "#iron" } } },
  ],
} as Shape;

describe("texture selects", () => {
  it("passes a sound select whose code the shape's faces use", () => {
    expect(checkScenario({ textures: [brick] }, rig, [], [], ["brick", "iron"])).toEqual([]);
  });

  it("names a code no face uses, options listed twice, a default not among them and bad colours", () => {
    const bad: TextureChoice[] = [
      { ...brick, code: "stone" },
      {
        label: "",
        code: "iron",
        default: "blue",
        options: [
          { id: "a", label: "A", path: "game:a", colour: "#123456" },
          { id: "a", label: "A again", path: "", colour: "red" },
        ],
      },
      { ...brick, options: [brick.options[0]!] },
    ];
    expect(checkScenario({ textures: bad }, rig, [], [], ["brick", "iron"])).toEqual([
      'texture select "stone": no face of the shape uses #stone',
      'texture select "iron" needs a label',
      'texture select "iron": option "a" is there twice',
      'texture select "iron": option "a" needs a path',
      'texture select "iron": option "a" needs a colour as #rrggbb',
      'texture select "iron": default "blue" is not one of its options',
      'texture select "brick" needs two options or more',
    ]);
    expect(checkScenario({ textures: [brick, brick] }, rig, [], [], ["brick"])).toEqual(['texture select "brick" is there twice']);
  });

  it("opens at each select's default and overrides only the picked codes", () => {
    expect(openingTextures([brick])).toEqual({ brick: "red" });
    expect(pickedTextures([brick], { brick: "cream" })).toEqual(new Map([["brick", { path: "game:block/cream", colour: "#ddccaa" }]]));
    // An unknown pick takes nothing; no pick takes the default.
    expect(pickedTextures([brick], { brick: "blue" }).size).toBe(0);
    expect(pickedTextures([brick], {}).get("brick")?.colour).toBe("#aa3322");
    expect(pickedTextures(undefined, {}).size).toBe(0);
  });

  it("draws the code in the default's colour at first, and in the picked one's after", () => {
    const view = buildModelView(shape, rig, { textures: [brick] });
    const byCode = (ts: typeof view.textures) => Object.fromEntries(ts.map((t) => [t.code, { path: t.path, colour: t.colour }]));
    const plain = byCode(buildModelView(shape, rig).textures);
    expect(byCode(view.textures)).toEqual({ brick: { path: "game:block/red", colour: "#aa3322" }, iron: plain.iron });
    const picked = withPickedTextures(view.textures, pickedTextures([brick], { brick: "cream" }));
    expect(byCode(picked)).toEqual({ brick: { path: "game:block/cream", colour: "#ddccaa" }, iron: plain.iron });
    expect(picked.find((t) => t.code === "brick")!.elements).toEqual([0]);
  });
});
