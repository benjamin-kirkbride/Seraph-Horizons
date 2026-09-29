// Shared reactive state: one clock for every cycling slot (so they change together, like
// the handbook) and the icon index.
import type { IconIndex } from "./icons.ts";

export const CYCLE_MS = 1500;

export const clock = $state({ tick: 0 });
export const icons = $state<{ index: IconIndex | null }>({ index: null });

let timer: ReturnType<typeof setInterval> | null = null;

export function startClock(): void {
  timer ??= setInterval(() => {
    clock.tick++;
  }, CYCLE_MS);
}

export function prefersReducedMotion(): boolean {
  return typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
}
