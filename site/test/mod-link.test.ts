// @vitest-environment jsdom
import { flushSync, mount, unmount } from "svelte";
import { afterEach, describe, expect, it } from "vitest";
import ModLink from "../src/components/ModLink.svelte";

let target: HTMLElement;
let component: ReturnType<typeof mount> | null = null;

function render(id: string, mods: Record<string, { name: string; assetId?: number }>) {
  target = document.createElement("div");
  document.body.append(target);
  component = mount(ModLink, { target, props: { id, mods } });
  flushSync();
  return target;
}

afterEach(() => {
  if (component) unmount(component);
  component = null;
  target.remove();
});

describe("ModLink", () => {
  it("links the mod's name to its ModDB page in a new tab", () => {
    const a = render("expandedfoods", { expandedfoods: { name: "Expanded Foods", assetId: 3363 } }).querySelector("a")!;
    expect(a.textContent).toBe("Expanded Foods");
    expect(a.getAttribute("href")).toBe("https://mods.vintagestory.at/show/mod/3363");
    expect(a.getAttribute("target")).toBe("_blank");
    expect(a.getAttribute("rel")).toBe("noopener noreferrer");
  });

  it("links the base game to the game's site", () => {
    expect(render("game", { game: { name: "Essentials" } }).querySelector("a")!.getAttribute("href")).toBe("https://www.vintagestory.at/");
  });

  it("names a mod without an asset id, unlinked, and falls back to its id", () => {
    const el = render("seraphexport", { seraphexport: { name: "Seraph export" } });
    expect(el.querySelector("a")).toBeNull();
    expect(el.textContent).toBe("Seraph export");
    expect(render("mystery", {}).textContent).toBe("mystery");
  });
});
