// @vitest-environment jsdom
import { flushSync, mount, tick, unmount } from "svelte";
import { afterEach, describe, expect, it } from "vitest";
import PowerPage from "../src/components/PowerPage.svelte";
import type { VersionData } from "../src/lib/data.ts";
import type { Meta } from "../src/lib/format.ts";
import type { PowerData } from "../src/lib/power-data.ts";
import { POWER } from "./fixtures/power.ts";

// jsdom has no layout; the charts measure their width with bind:clientWidth.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

let target: HTMLElement;
let component: ReturnType<typeof mount> | null = null;

const meta = { power: true, mods: { game: { name: "Vintage Story" }, millwright: { name: "Millwright" } } } as unknown as Meta;

async function render(power: PowerData | null, metaPower = true) {
  target = document.createElement("div");
  document.body.append(target);
  const data = { id: "main", power: () => Promise.resolve(power), nameOf: (c: string) => c } as unknown as VersionData;
  component = mount(PowerPage, { target, props: { data, meta: { ...meta, power: metaPower } } });
  flushSync();
  await Promise.resolve();
  await tick();
  flushSync();
  return target;
}

const cell = (el: HTMLElement, table: string, row: string, col: string) =>
  el.querySelector(`[data-testid="${table}"] tr[data-producer="${row}"] [data-col="${col}"]`)?.textContent?.trim();

afterEach(() => {
  if (component) unmount(component);
  component = null;
  target.remove();
});

describe("PowerPage", () => {
  it("says so when the version has no power data", async () => {
    const el = await render(POWER, false);
    expect(el.querySelector('[data-testid="power-none"]')).not.toBeNull();
    component && unmount(component);
    component = null;
    target.remove();
  });

  it("also when meta.json says it has power data but the file is null", async () => {
    const el = await render(null);
    expect(el.querySelector('[data-testid="power-none"]')?.textContent).toBe("This version's export has no power data.");
    expect(el.querySelector("section")).toBeNull();
  });

  it("lists producers with their figures, linking those that are items", async () => {
    const el = await render(POWER);
    expect(el.querySelectorAll('[data-testid="producers"] tbody tr')).toHaveLength(5);
    expect(cell(el, "producers", "game:windmillrotor-metal@10", "stall")).toBe("1.88");
    expect(cell(el, "producers", "game:windmillrotor-metal@10", "peak")).toBe("0.281");
    expect(cell(el, "producers", "ppex:engine-watt", "free")).toBe("0.503");
    const link = el.querySelector('[data-testid="producers"] tr[data-producer="game:handcrank"] a.item-link');
    expect(link?.getAttribute("href")).toBe("#/main/item/game:handcrank-north");
    expect(el.querySelector('[data-testid="producers"] tr[data-producer="game:waterwheel"] a.item-link')).toBeNull();
    // The water wheel's torque factor is the exporter's default.
    expect(el.querySelector('[data-testid="producers"] tr[data-producer="game:waterwheel"] .fallback')).not.toBeNull();
    expect(el.querySelectorAll('[data-testid="torque-chart"] path.curve')).toHaveLength(5);
    expect(el.querySelector('[data-testid="reference-load"]')?.textContent).toContain("0.125");
  });

  it("hides a family's curves and bars when it is unticked", async () => {
    const el = await render(POWER);
    const wind = [...el.querySelectorAll<HTMLLabelElement>(".families label")].find((l) => l.textContent?.includes("Wind"))!;
    wind.querySelector("input")!.click();
    flushSync();
    expect(el.querySelectorAll('[data-testid="torque-chart"] path.curve')).toHaveLength(3);
    expect(el.querySelectorAll('[data-testid="peak-chart"] li.row')).toHaveLength(3);
  });

  it("fits the torque axis to the producers left on", async () => {
    const el = await render(POWER);
    const top = () => Math.max(...[...el.querySelectorAll('[data-testid="torque-chart"] text.tick')].map((t) => Number(t.textContent)));
    expect(top()).toBe(12.5);
    const millwright = [...el.querySelectorAll<HTMLLabelElement>('[data-testid="mod-toggles"] label')].find((l) => l.textContent === "Millwright")!;
    millwright.querySelector("input")!.click();
    flushSync();
    expect(el.querySelectorAll('[data-testid="torque-chart"] path.curve')).toHaveLength(4);
    // The Watt engine's 2.4 is now the highest stall torque.
    expect(top()).toBe(2.5);
  });

  it("gives each wind pattern's mean duration and its range", async () => {
    const el = await render(POWER);
    // Still: 12 h + 6 h × U1 × U2, a mean of 13.5 h.
    expect(el.querySelector('[data-testid="wind-patterns"] tbody tr')?.textContent).toContain("13.5 h on average (12–18 h)");
  });

  it("averages the windmills over the wind at the height picked", async () => {
    const el = await render(POWER);
    expect(el.querySelector('[data-testid="wind-stats"]')?.textContent).toContain("25%");
    expect(el.querySelector('[data-testid="power-wind"] [data-testid="sources"]')?.textContent).toContain("windpatterns.json");
    const slider = el.querySelector<HTMLInputElement>('[data-testid="power-wind"] input[type="range"]')!;
    slider.value = "50";
    slider.dispatchEvent(new Event("input", { bubbles: true }));
    flushSync();
    expect(el.querySelector('[data-testid="height"]')?.textContent).toBe("50 blocks");
    expect(el.querySelector('[data-testid="wind-stats"]')?.textContent).toContain("75%");
  });

  it("puts every producer to the load picked, a dry machine counting double", async () => {
    const el = await render(POWER);
    const buttons = [...el.querySelectorAll<HTMLButtonElement>('[data-testid="power-explorer"] button')];
    buttons.find((b) => b.textContent === "4 helve hammers")!.click();
    flushSync();
    expect(el.querySelector('[data-testid="total-load"]')?.textContent).toBe("Total load 0.5");
    expect(cell(el, "explorer", "game:windmillrotor-metal@10", "speed")).toBe("0.44");
    expect(cell(el, "explorer", "game:handcrank", "speed")).toBe("Stalls");
    // Two bars for each windmill: averaged and at full wind.
    expect(el.querySelectorAll('[data-testid="explorer-chart"] li.row')).toHaveLength(7);

    const select = el.querySelector<HTMLSelectElement>('[data-testid="picks"] select')!;
    select.value = "seraphhorizons:millframe";
    select.dispatchEvent(new Event("change", { bubbles: true }));
    flushSync();
    expect(el.querySelector('[data-testid="total-load"]')?.textContent).toBe("Total load 1.6");
    [...el.querySelectorAll<HTMLLabelElement>("label.check")][0]!.querySelector("input")!.click();
    flushSync();
    expect(el.querySelector('[data-testid="total-load"]')?.textContent).toBe("Total load 3.2");
  });

  it("charts machines by mod and keeps transmission parts apart", async () => {
    const el = await render(POWER);
    expect(el.querySelectorAll('[data-testid="consumers"] tbody tr')).toHaveLength(4);
    expect(el.querySelectorAll('[data-testid="transmission"] tbody tr')).toHaveLength(1);
    const chart = el.querySelector('[data-testid="consumers-chart"]')!;
    expect([...chart.querySelectorAll(".group")].map((g) => g.textContent)).toEqual(["ppex", "seraphhorizons", "Vintage Story"]);
    expect(chart.querySelector('li[data-key="ppex:blower"]')?.getAttribute("aria-label")).toBe("Blower: 0.05–0.15");
    expect(chart.querySelector('li[data-key="seraphhorizons:millframe"] .hatch')).not.toBeNull();
    expect(el.querySelector('[data-testid="consumers"] tr[data-consumer="ppex:blower"] [data-col="load"]')?.textContent).toBe("0.05–0.15");
  });
});
