// @vitest-environment jsdom
import { flushSync, mount, unmount } from "svelte";
import { afterEach, describe, expect, it } from "vitest";
import ModLink from "../src/components/ModLink.svelte";

let target: HTMLElement;
let component: ReturnType<typeof mount> | null = null;

function render(id: string, mods: Record<string, { name: string }>) {
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
    const a = render("expandedfoods", { expandedfoods: { name: "Expanded Foods" } }).querySelector("a")!;
    expect(a.textContent).toBe("Expanded Foods");
    expect(a.getAttribute("href")).toBe("https://mods.vintagestory.at/expandedfoods");
    expect(a.getAttribute("target")).toBe("_blank");
    expect(a.getAttribute("rel")).toBe("noopener noreferrer");
  });

  it("falls back to the id for a mod the data does not name", () => {
    expect(render("mystery", {}).querySelector("a")!.textContent).toBe("mystery");
  });

  it("shows the exporter's name without a link", () => {
    const el = render("seraphexport", { seraphexport: { name: "Seraph export" } });
    expect(el.querySelector("a")).toBeNull();
    expect(el.textContent).toBe("Seraph export");
  });
});
