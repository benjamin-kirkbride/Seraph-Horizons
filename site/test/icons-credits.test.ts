import { describe, expect, it } from "vitest";
import { creditRows, modLink } from "../src/lib/credits.ts";
import { iconPath, readIconIndex } from "../src/lib/icons.ts";

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
  it("links a mod's website when it has one, else its ModDB page", () => {
    expect(modLink("primitivesurvival", { website: "https://github.com/SpearAndFang/primitive-survival" })).toBe(
      "https://github.com/SpearAndFang/primitive-survival",
    );
    expect(modLink("expandedfoods", {})).toBe("https://mods.vintagestory.at/expandedfoods");
    expect(modLink("expandedfoods", { website: "" })).toBe("https://mods.vintagestory.at/expandedfoods");
    expect(modLink("x", { website: "javascript:alert(1)" })).toBe("https://mods.vintagestory.at/x");
  });

  it("lists every mod, base game first, then by name", () => {
    const rows = creditRows({
      zeta: { name: "Alpha Mod", version: "1", authors: ["A", "B"] },
      game: { name: "Essentials", version: "1.22.7" },
      alpha: { name: "Zulu Mod", version: "2" },
    });
    expect(rows.map((r) => r.id)).toEqual(["game", "zeta", "alpha"]);
    expect(rows[1]).toEqual({ id: "zeta", name: "Alpha Mod", version: "1", authors: ["A", "B"], link: "https://mods.vintagestory.at/zeta" });
  });
});
