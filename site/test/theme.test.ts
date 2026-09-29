// @vitest-environment jsdom
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { THEME_KEY, applyTheme, loadTheme, parseTheme, saveTheme } from "../src/lib/theme.ts";

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  delete document.documentElement.dataset.theme;
});

describe("theme", () => {
  it("reads anything unknown as the system theme", () => {
    expect(parseTheme("dark")).toBe("dark");
    expect(parseTheme("light")).toBe("light");
    expect(parseTheme(null)).toBe("system");
    expect(parseTheme("sepia")).toBe("system");
  });

  it("sets data-theme only for an explicit choice", () => {
    applyTheme("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    applyTheme("system");
    expect(document.documentElement.dataset.theme).toBeUndefined();
  });

  it("remembers a choice and forgets it on going back to the system theme", () => {
    saveTheme("light");
    expect(localStorage.getItem(THEME_KEY)).toBe("light");
    expect(loadTheme()).toBe("light");
    saveTheme("system");
    expect(localStorage.getItem(THEME_KEY)).toBeNull();
    expect(loadTheme()).toBe("system");
  });

  it("still applies a choice when storage throws", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    saveTheme("dark");
    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(loadTheme()).toBe("system");
  });

  it("matches the pre-paint script in index.html", () => {
    // Under jsdom import.meta.url is not a file URL; Vitest runs from site/.
    const html = readFileSync(join(process.cwd(), "index.html"), "utf8");
    expect(html).toContain(`localStorage.getItem("${THEME_KEY}")`);
    expect(html).toContain(`theme === "light" || theme === "dark"`);
  });
});
