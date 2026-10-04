import { describe, expect, it } from "vitest";
import { formatRoute, parseRoute, withVersion } from "../src/lib/route.ts";

describe("parseRoute", () => {
  it("reads the bare address as the root", () => {
    expect(parseRoute("")).toEqual({ view: "root" });
    expect(parseRoute("#/")).toEqual({ view: "root" });
  });

  it("reads versioned pages", () => {
    expect(parseRoute("#/v0.1.0")).toEqual({ view: "home", version: "v0.1.0" });
    expect(parseRoute("#/main/credits")).toEqual({ view: "credits", version: "main" });
    expect(parseRoute("#/main/item/game:ingot-copper")).toEqual({ view: "item", version: "main", code: "game:ingot-copper" });
    expect(parseRoute("#/main/search?q=ingot%20cop")).toEqual({ view: "search", version: "main", query: "ingot cop" });
    expect(parseRoute("#/main/entities")).toEqual({ view: "entities", version: "main" });
    expect(parseRoute("#/main/entity/game:wolf-eurasian-adult-male")).toEqual({
      view: "entity",
      version: "main",
      code: "game:wolf-eurasian-adult-male",
    });
    expect(parseRoute("#/main/entity/game:drifter?variant=game%3Adrifter-normal")).toEqual({
      view: "entity",
      version: "main",
      code: "game:drifter",
      variant: "game:drifter-normal",
    });
    expect(parseRoute("#/main/entity/wolf")).toEqual({ view: "notfound", version: "main" });
  });

  it("decodes an encoded code and rejects one without a domain", () => {
    expect(parseRoute("#/main/item/game%3Aplank-*")).toEqual({ view: "item", version: "main", code: "game:plank-*" });
    expect(parseRoute("#/main/item/stick")).toEqual({ view: "notfound", version: "main" });
    expect(parseRoute("#/main/item/%E0%A4%A")).toEqual({ view: "notfound", version: "main" });
  });

  it("reads the model viewer's pages, which have no version", () => {
    expect(parseRoute("#/models")).toEqual({ view: "models" });
    expect(parseRoute("#/models/")).toEqual({ view: "models" });
    expect(parseRoute("#/models/bucking-sawmill")).toEqual({ view: "model", id: "bucking-sawmill" });
    expect(parseRoute("#/models/a/b")).toEqual({ view: "notfound" });
    expect(parseRoute("#/models/%E0%A4%A")).toEqual({ view: "notfound" });
  });

  it("reports anything else as not found, keeping the version", () => {
    expect(parseRoute("#/main/nowhere")).toEqual({ view: "notfound", version: "main" });
    expect(parseRoute("#/main/credits/extra")).toEqual({ view: "notfound", version: "main" });
  });
});

describe("formatRoute", () => {
  it("writes the addresses by hand-checked form", () => {
    expect(formatRoute({ view: "item", version: "v0.1.0", code: "game:ingot-copper" })).toBe("#/v0.1.0/item/game:ingot-copper");
    expect(formatRoute({ view: "search", version: "main", query: "ingot cop" })).toBe("#/main/search?q=ingot+cop");
    expect(formatRoute({ view: "item", version: "main", code: "a:b/c" })).toBe("#/main/item/a:b%2Fc");
  });

  it("round-trips through parseRoute", () => {
    for (const hash of ["#/main/item/mymod:thing-1", "#/v1/search?q=a+%26+b", "#/main/credits", "#/main/entities", "#/main/entity/game:wolf", "#/main/entity/game:wolf?variant=game%3Awolf-male", "#/models", "#/models/bucking-sawmill"]) {
      expect(formatRoute(parseRoute(hash))).toBe(hash);
    }
    expect(parseRoute(formatRoute({ view: "item", version: "main", code: "a:b/c" }))).toEqual({ view: "item", version: "main", code: "a:b/c" });
  });
});

describe("withVersion", () => {
  it("keeps the item when switching", () => {
    expect(withVersion({ view: "item", version: "main", code: "game:stick" }, "v1")).toEqual({ view: "item", version: "v1", code: "game:stick" });
    expect(withVersion({ view: "notfound", version: "main" }, "v1")).toEqual({ view: "home", version: "v1" });
    expect(withVersion({ view: "entity", version: "main", code: "game:wolf-male" }, "v1")).toEqual({ view: "entity", version: "v1", code: "game:wolf-male" });
  });
});
