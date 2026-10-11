// The eidolon's fitting sheet: what prepare-data writes for schema/examples/minimal.json's eidolon
// section, and what the page derives from a pick (stats, effects, textures, the bill), plus the
// textured scene's uv mapping and the model manifest's link.
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  bill,
  change,
  defaultPick,
  eidolonTexturePaths,
  isAgedWood,
  layerEffect,
  materialLabel,
  sashParts,
  statsFor,
  textureImages,
  texturePaths,
  type EidolonData,
} from "../src/lib/eidolon-fittings.ts";
import type { RecipeExport } from "../src/lib/export.ts";
import type { Meta } from "../src/lib/format.ts";
import { checkManifest } from "../src/lib/model-manifest.ts";
import { prepareData } from "../src/lib/prepare.ts";
import { faceUv } from "../src/lib/rig.ts";

const minimal = JSON.parse(readFileSync(new URL("../../schema/examples/minimal.json", import.meta.url), "utf8")) as RecipeExport;
const STEEL = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPombbgPwAGNgLC5CSs+AAAAABJRU5ErkJggg==";
// The store has the steel plate; the rest are drawn as colours.
const e: EidolonData = { ...minimal.eidolon!, textures: { "game:block/metal/plate/steel": STEEL } };

describe("eidolon data", () => {
  it("writes eidolon.json as the export has it with the store's images of its textures, and names the items that link to it", () => {
    const { files, meta } = prepareData(minimal, { textures: { "game:block/metal/plate/steel": STEEL, "game:block/unnamed": STEEL } });
    expect(files.get("eidolon.json")).toEqual(e);
    expect((meta as Meta).eidolon).toEqual({ items: ["seraphhorizons:creature-eidolon", "seraphhorizons:eidolontorso"] });
    expect((prepareData(minimal).files.get("eidolon.json") as EidolonData).textures).toEqual({});
  });

  it("names every texture of the layers' materials and the shape once", () => {
    const paths = eidolonTexturePaths(minimal.eidolon!);
    expect(paths).toContain("game:block/machine/lattice");
    expect(paths).toContain("game:block/metal/plate/steel");
    expect(new Set(paths).size).toBe(paths.length);
  });

  it("writes neither without the section", () => {
    const { eidolon: _gone, ...rest } = minimal;
    const { files, meta } = prepareData(rest as RecipeExport);
    expect(files.has("eidolon.json")).toBe(false);
    expect((meta as Meta).eidolon).toBeUndefined();
  });
});

describe("a pick", () => {
  it("opens at each layer's default: the old all-steel build", () => {
    expect(defaultPick(e)).toEqual({ bones: "oak", joints: "steel", plates: "steel", sash: "leather-plain", skirt: "steel" });
  });

  it("takes its stats from the export's builds, and none for a build it lacks", () => {
    const pick = defaultPick(e);
    expect(statsFor(e, pick)?.maxHealth).toBe(300);
    expect(statsFor(e, { ...pick, plates: "brass", bones: "birch" })).toMatchObject({ maxHealth: 160, walkSpeed: 0.0275, defenceDamage: 8 });
    expect(statsFor(e, { ...pick, joints: "iron" })).toBeNull();
  });

  it("compares a stat with the default build's", () => {
    expect(change(160, 300)).toBeCloseTo(-0.4667, 4);
    expect(change(300.0001, 300)).toBe(0);
    expect(change(5, 0)).toBe(0);
  });

  it("says what a material changes, from the settings", () => {
    expect(layerEffect(e, "plates", "brass")).toEqual(["HP ×0.8", "damage ×0.8", "speed ×1.25"]);
    expect(layerEffect(e, "plates", "steel")).toEqual(["HP ×1.5"]);
    expect(layerEffect(e, "skirt", "steel")).toEqual(["HP ×1.25"]);
    expect(layerEffect(e, "bones", "oak")).toEqual([]);
  });

  it("names materials as the sheet shows them", () => {
    expect(materialLabel("plates", "stainlesssteel")).toBe("Stainless");
    expect(materialLabel("bones", "baldcypress")).toBe("Bald cypress");
    expect(materialLabel("bones", "veryaged")).toBe("Very aged");
    expect(materialLabel("sash", "cloth-red")).toBe("Red");
    expect(sashParts("leather-plain")).toEqual({ kind: "leather", colour: "plain" });
    expect(isAgedWood("veryaged")).toBe(true);
    expect(isAgedWood("oak")).toBe(false);
  });

  it("dresses the model: each layer's texture over the shape's own, and the images the export carries", () => {
    const pick = { ...defaultPick(e), plates: "brass", bones: "birch" };
    const paths = texturePaths(e, pick);
    expect(paths.get("plates")).toBe("game:block/metal/plate/brass");
    expect(paths.get("bones")).toBe("game:block/wood/debarked/birch");
    expect(paths.get("lattice")).toBe("game:block/machine/lattice");
    // Only the steel plate's image is in the example: the joints wear it, the brass plates have none.
    expect(textureImages(e, pick)).toEqual(new Map([["joints", STEEL]]));
    expect(textureImages(e, defaultPick(e))).toEqual(new Map([["joints", STEEL], ["plates", STEEL]]));
  });

  it("lists the bill stage by stage, the picked material first and then the layer's parts", () => {
    const rows = bill(e, { ...defaultPick(e), plates: "brass" }, { torso: "Torso", mind: "Mind" });
    expect(rows.map((r) => r.id)).toEqual(["torso", "bones", "joints", "plates", "sash", "skirt", "mind"]);
    expect(rows.find((r) => r.id === "plates")!.items).toEqual([{ stack: { code: "game:metalplate-brass", count: 17 }, chosen: true }]);
    expect(rows.find((r) => r.id === "joints")!.items.map((i) => [i.stack.code, i.stack.count, i.chosen])).toEqual([
      ["game:rod-steel", 4, true],
      ["game:metalnailsandstrips-steel", 20, true],
      ["game:jonasframes-joint01", 2, false],
      ["seraphhorizons:gear-stainless", 9, false],
    ]);
    expect(rows[0]!.items[0]).toEqual({ stack: { code: "seraphhorizons:gear-stainless", count: 3 }, chosen: false });
    expect(rows.at(-1)!.items[0]!.stack.code).toBe("game:gear-temporal");
  });
});

describe("the textured scene's uv", () => {
  it("maps a face's uv over its corners, v negated, as the game does", () => {
    // North: (u1, v1) at the top east corner (x to, y to, z from: corner 3).
    const uv = faceUv("north", { uv: [2, 4, 6, 10] });
    expect(uv.get(3)).toEqual([2, -4]);
    expect(uv.get(2)).toEqual([6, -4]);
    expect(uv.get(0)).toEqual([6, -10]);
    expect(uv.get(1)).toEqual([2, -10]);
  });

  it("turns the uv with the face's rotation, and covers the texture without a uv", () => {
    const turned = faceUv("up", { uv: [0, 0, 8, 8], rotation: 90 });
    expect(turned.get(2)).toEqual([0, -8]);
    expect(turned.get(3)).toEqual([0, -0]);
    expect(faceUv("south", {}).get(6)).toEqual([0, -0]);
    expect(faceUv("south", {}).get(5)).toEqual([16, -16]);
  });
});

describe("a model's link to the recipe browser", () => {
  const model = { id: "m", title: "M", description: "d", credit: "c", shape: "a/b.json" };
  it("needs a label and a page address", () => {
    expect(() => checkManifest({ models: [{ ...model, link: { label: "Fitting sheet", page: "eidolon" } }] })).not.toThrow();
    expect(() => checkManifest({ models: [{ ...model, link: { label: "", page: "eidolon" } }] })).toThrow(/link needs/);
    expect(() => checkManifest({ models: [{ ...model, link: { label: "x", page: "../x" } }] })).toThrow(/link needs/);
  });
});
