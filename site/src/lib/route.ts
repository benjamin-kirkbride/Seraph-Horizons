// Routes live in the URL hash (#/<version>/item/<code>), so GitHub Pages only ever
// serves index.html and a deep link survives a reload. The version is always in the
// route so a shared link keeps pointing at the data it was made from.

export type Route =
  | { view: "root" }
  | { view: "home"; version: string }
  | { view: "search"; version: string; query: string }
  | { view: "item"; version: string; code: string }
  | { view: "entities"; version: string }
  | { view: "entity"; version: string; code: string; variant?: string }
  | { view: "credits"; version: string }
  | { view: "notfound"; version?: string };

// Codes are mostly URL-safe; keep `:` readable instead of %3A.
const enc = (s: string) => encodeURIComponent(s).replace(/%3A/gi, ":");

function dec(s: string): string | null {
  try {
    return decodeURIComponent(s);
  } catch {
    return null;
  }
}

export function parseRoute(hash: string): Route {
  let h = hash.startsWith("#") ? hash.slice(1) : hash;
  if (h.startsWith("/")) h = h.slice(1);
  const q = h.indexOf("?");
  const params = new URLSearchParams(q >= 0 ? h.slice(q + 1) : "");
  const path = q >= 0 ? h.slice(0, q) : h;
  const parts = path.split("/").filter((p) => p !== "");
  if (parts.length === 0) return { view: "root" };
  const version = dec(parts[0]!);
  if (version === null || version === "") return { view: "notfound" };
  if (parts.length === 1) return { view: "home", version };
  const [, page, ...rest] = parts;
  switch (page) {
    case "search":
      if (rest.length === 0) return { view: "search", version, query: params.get("q") ?? "" };
      break;
    case "item":
    case "entity": {
      // A code has no `/` in practice, but joining keeps one intact if it ever does.
      const code = dec(rest.join("/"));
      if (!code || !/^[^:\s]+:\S+$/.test(code)) break;
      if (page === "item") return { view: "item", version, code };
      const variant = params.get("variant");
      return variant ? { view: "entity", version, code, variant } : { view: "entity", version, code };
    }
    case "entities":
      if (rest.length === 0) return { view: "entities", version };
      break;
    case "credits":
      if (rest.length === 0) return { view: "credits", version };
      break;
  }
  return { view: "notfound", version };
}

export function formatRoute(route: Route): string {
  switch (route.view) {
    case "root":
      return "#/";
    case "home":
      return `#/${enc(route.version)}`;
    case "search":
      return `#/${enc(route.version)}/search?${new URLSearchParams({ q: route.query }).toString()}`;
    case "item":
      return `#/${enc(route.version)}/item/${enc(route.code)}`;
    case "entities":
      return `#/${enc(route.version)}/entities`;
    case "entity":
      return `#/${enc(route.version)}/entity/${enc(route.code)}${route.variant ? `?${new URLSearchParams({ variant: route.variant }).toString()}` : ""}`;
    case "credits":
      return `#/${enc(route.version)}/credits`;
    case "notfound":
      return route.version ? `#/${enc(route.version)}/not-found` : "#/not-found";
  }
}

/** The same page in another version, used by the version switcher. */
export function withVersion(route: Route, version: string): Route {
  switch (route.view) {
    case "root":
    case "notfound":
      return { view: "home", version };
    case "item":
      return { view: "item", version, code: route.code };
    default:
      return { ...route, version };
  }
}
