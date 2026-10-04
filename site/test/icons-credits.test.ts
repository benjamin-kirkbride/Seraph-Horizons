import { describe, expect, it } from "vitest";
import { creditRows, modLink, modWebsite } from "../src/lib/credits.ts";
import { iconPath, initials, readIconIndex } from "../src/lib/icons.ts";

describe("initials", () => {
  it("takes the first letter of the first two words", () => {
    expect(initials("Copper ingot")).toBe("CI");
    expect(initials("Torch")).toBe("T");
    expect(initials("Simple fishing pole (wood)")).toBe("SF");
  });

  it("skips brackets and dashes, and copes with an empty name", () => {
    expect(initials("(Extinguished) torch")).toBe("ET");
    expect(initials("tin-bronze")).toBe("TB");
    expect(initials("")).toBe("");
  });
});

const hash = "3fa9c1d2e4b5a6978877665544332211ffeeddccbbaa99887766554433221100";

describe("iconPath", () => {
  const index = { schemaVersion: 1, size: 64, icons: { "game:stick": hash, "game:evil": "../../etc/passwd" } };

  it("puts the file under the first two hex digits of its hash", () => {
    expect(iconPath(index, "game:stick")).toBe(`icons/3f/${hash}.png`);
  });

  it("gives null for a missing item, a missing index or a hash that is not hex", () => {
    expect(iconPath(index, "game:flint")).toBeNull();
    expect(iconPath(null, "game:stick")).toBeNull();
    expect(iconPath(index, "game:evil")).toBeNull();
  });

  it("only accepts an index in the documented shape", () => {
    expect(readIconIndex({ schemaVersion: 2, icons: {} })).toBeNull();
    expect(readIconIndex("<html>404</html>")).toBeNull();
    expect(readIconIndex({ schemaVersion: 1, icons: { a: hash } })).toEqual({ schemaVersion: 1, size: 64, icons: { a: hash } });
  });
});

describe("credits", () => {
  it("links a mod's ModDB page by asset id, the base game the game's site, and nothing without an id", () => {
    // moreroads is at /moreroadsandpaths, so /<modid> would be a 404.
    expect(modLink("moreroads", { assetId: 26 })).toBe("https://mods.vintagestory.at/show/mod/26");
    expect(modLink("survival", undefined)).toBe("https://www.vintagestory.at/");
    expect(modLink("seraphexport", {})).toBeNull();
    expect(modLink("droppedmod", undefined)).toBeNull();
  });

  it("keeps a mod's own http(s) website as a second link", () => {
    expect(modWebsite("primitivesurvival", { website: " https://github.com/SpearAndFang/primitive-survival " })).toBe(
      "https://github.com/SpearAndFang/primitive-survival",
    );
    expect(modWebsite("expandedfoods", {})).toBeNull();
    expect(modWebsite("expandedfoods", { website: "" })).toBeNull();
    expect(modWebsite("x", { website: "javascript:alert(1)" })).toBeNull();
    expect(modWebsite("x", { website: "https://mods.vintagestory.at/show/mod/7", assetId: 7 })).toBeNull();
    expect(modWebsite("x", { website: "https://mods.vintagestory.at/x" })).toBe("https://mods.vintagestory.at/x");
  });

  it("lists every mod, base game first, then by name", () => {
    const rows = creditRows({
      zeta: { name: "Alpha Mod", version: "1", authors: ["A", "B"], website: "https://example.org/", assetId: 12 },
      game: { name: "Essentials", version: "1.22.7" },
      alpha: { name: "Zulu Mod", version: "2" },
    });
    expect(rows.map((r) => r.id)).toEqual(["game", "zeta", "alpha"]);
    expect(rows[1]).toEqual({
      id: "zeta",
      name: "Alpha Mod",
      version: "1",
      authors: ["A", "B"],
      link: "https://mods.vintagestory.at/show/mod/12",
      website: "https://example.org/",
    });
    expect(rows[2]!.link).toBeNull();
  });
});
