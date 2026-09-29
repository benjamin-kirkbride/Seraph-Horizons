// @vitest-environment jsdom
import { flushSync, mount, unmount } from "svelte";
import { afterEach, describe, expect, it } from "vitest";
import Vtml from "../src/components/Vtml.svelte";
import { parseVtml } from "../src/lib/vtml.ts";

let target: HTMLElement;
let component: ReturnType<typeof mount> | null = null;

function render(source: string) {
  target = document.createElement("div");
  document.body.append(target);
  component = mount(Vtml, { target, props: { nodes: parseVtml(source), itemHref: (c: string) => `#/v/item/${c}` } });
  flushSync();
  return target;
}

afterEach(() => {
  if (component) unmount(component);
  component = null;
  target.remove();
  delete (window as unknown as { pwned?: boolean }).pwned;
});

describe("Vtml component", () => {
  it("does not create a script element and its text is not run", () => {
    const el = render('ok<script>window.pwned = true</script><img src=x onerror="window.pwned = true">');
    expect(el.querySelector("script")).toBeNull();
    expect(el.querySelector("img")).toBeNull();
    expect(el.textContent).toBe("ok");
    expect((window as unknown as { pwned?: boolean }).pwned).toBeUndefined();
  });

  it("renders the safe subset as elements", () => {
    const el = render('A <strong>b</strong><br><a href="handbook://item-flint">flint</a> <a href="https://x.org" onclick="evil()">x</a>');
    expect(el.querySelector("strong")?.textContent).toBe("b");
    expect(el.querySelectorAll("br")).toHaveLength(1);
    const [item, ext] = [...el.querySelectorAll("a")];
    expect(item!.getAttribute("href")).toBe("#/v/item/game:flint");
    expect(ext!.getAttribute("href")).toBe("https://x.org");
    expect(ext!.getAttribute("onclick")).toBeNull();
    expect(ext!.getAttribute("rel")).toContain("noopener");
  });

  it("shows markup that arrived encoded as literal text", () => {
    const el = render("&lt;b&gt;not bold&lt;/b&gt;");
    expect(el.querySelector("b")).toBeNull();
    expect(el.textContent).toBe("<b>not bold</b>");
  });
});
