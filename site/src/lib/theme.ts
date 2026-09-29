// The reader's theme choice. "system" follows prefers-color-scheme; "light" and "dark"
// set data-theme on <html>, which pins color-scheme and so every light-dark() token in
// app.css. The inline script in index.html applies the saved choice before first paint,
// so it must read the same key and values.

export const THEMES = ["system", "light", "dark"] as const;
export type Theme = (typeof THEMES)[number];

export const THEME_KEY = "theme";

export function parseTheme(value: unknown): Theme {
  return THEMES.includes(value as Theme) ? (value as Theme) : "system";
}

/** The saved choice, or "system" when there is none or storage is unavailable. */
export function loadTheme(): Theme {
  try {
    return parseTheme(localStorage.getItem(THEME_KEY));
  } catch {
    return "system";
  }
}

export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement): void {
  if (theme === "system") delete root.dataset.theme;
  else root.dataset.theme = theme;
}

/** Applies `theme` and remembers it; the choice still applies if it cannot be saved. */
export function saveTheme(theme: Theme): void {
  applyTheme(theme);
  try {
    if (theme === "system") localStorage.removeItem(THEME_KEY);
    else localStorage.setItem(THEME_KEY, theme);
  } catch {
    // Private windows and blocked storage: the theme lasts until the page is closed.
  }
}
