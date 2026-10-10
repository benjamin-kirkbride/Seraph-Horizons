// The tabbed standalone page's shell (scripts/standalone-tabs.ts): switches tabs from the hash, mounts
// the shown model tab's viewer from its bundle (tab-viewer.ts) and takes the last one down first, its
// WebGL context released, so only one three.js scene is alive at a time. HTML tabs are in the page.
import { applyTheme, loadTheme, parseTheme, saveTheme } from "../../src/lib/theme.ts";
import { tabForHash } from "./tabs-hash.ts";
import type { TabBundle } from "./tab-viewer.ts";

const registry = (): Record<string, TabBundle> => (globalThis as { __standaloneTabs?: Record<string, TabBundle> }).__standaloneTabs ?? {};

const links = [...document.querySelectorAll<HTMLAnchorElement>(".tab-bar [role=tab]")];
const slugs = links.map((a) => a.dataset.slug ?? "");
const panel = (slug: string) => document.getElementById(`panel-${slug}`);

let active: string | null = null;
let takeDown: (() => void) | null = null;

/** Lets the browser free a dropped scene's GL context now rather than at garbage collection. */
function release(canvas: HTMLCanvasElement): void {
  try {
    (canvas.getContext("webgl2") as WebGL2RenderingContext | null)?.getExtension("WEBGL_lose_context")?.loseContext();
  } catch {
    // Nothing to release.
  }
}

function unmountActive(): void {
  if (!active) return;
  const mountPoint = panel(active)?.querySelector<HTMLElement>(".model-mount");
  const canvases = mountPoint ? [...mountPoint.querySelectorAll("canvas")] : [];
  if (takeDown) {
    try {
      takeDown();
    } catch (e) {
      console.error(e);
    }
    takeDown = null;
  }
  canvases.forEach(release);
  mountPoint?.replaceChildren();
}

function fail(target: HTMLElement, message: string): void {
  const p = document.createElement("p");
  p.className = "tab-error";
  p.textContent = message;
  target.replaceChildren(p);
}

function show(slug: string): void {
  if (slug === active) return;
  unmountActive();
  active = slug;
  for (const a of links) {
    const on = a.dataset.slug === slug;
    a.setAttribute("aria-selected", String(on));
    a.tabIndex = on ? 0 : -1;
    const p = panel(a.dataset.slug ?? "");
    if (p) p.hidden = !on;
  }
  const p = panel(slug);
  const group = p?.dataset.group;
  const target = p?.querySelector<HTMLElement>(".model-mount");
  if (!group || !target) return;
  const bundle = registry()[group];
  if (!bundle) return fail(target, `This tab's viewer did not load (bundle ${group}).`);
  try {
    takeDown = bundle.mount(target, slug);
  } catch (e) {
    fail(target, `This tab's viewer failed: ${(e as Error).message}`);
  }
}

function fromHash(): void {
  const slug = tabForHash(location.hash, slugs);
  if (slug) show(slug);
  else if (!active) show(slugs[0]!);
}

// Arrow keys move along the tab bar, as in any tab list.
document.querySelector(".tab-bar")?.addEventListener("keydown", (e) => {
  const key = (e as KeyboardEvent).key;
  const at = links.findIndex((a) => a === document.activeElement);
  if (at < 0 || !["ArrowLeft", "ArrowRight", "Home", "End"].includes(key)) return;
  e.preventDefault();
  const next = key === "Home" ? 0 : key === "End" ? links.length - 1 : (at + (key === "ArrowRight" ? 1 : -1) + links.length) % links.length;
  links[next]!.focus();
  links[next]!.click();
});

const theme = document.querySelector<HTMLSelectElement>("#theme");
if (theme) {
  theme.value = loadTheme();
  applyTheme(parseTheme(theme.value));
  theme.addEventListener("change", () => saveTheme(parseTheme(theme.value)));
}

addEventListener("hashchange", fromHash);
fromHash();
