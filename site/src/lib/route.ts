// Routes live in the URL hash (#/<version>/item/<code>), so GitHub Pages only ever
// serves index.html and a deep link survives a reload. The version is always in the
// route so a shared link keeps pointing at the data it was made from. The model viewer
// (#/models, #/models/<id>) is the exception: it does not read the recipe data, so its
// routes have no version, and "models" can never be a version id.

export type Route =
  | { view: "root" }
  | { view: "home"; version: string }
  | { view: "search"; version: string; query: string }
  | { view: "item"; version: string; code: string }
  | { view: "type"; version: string; code: string; page?: number }
  | { view: "entities"; version: string }
  | { view: "entity"; version: string; code: string; variant?: string }
  | { view: "credits"; version: string }
  | { view: "models" }
  | { view: "model"; id: string }
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
  if (parts[0] === "models") {
    if (parts.length === 1) return { view: "models" };
    const id = parts.length === 2 ? dec(parts[1]!) : null;
    return id ? { view: "model", id } : { view: "notfound" };
  }
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
    case "type": {
      // Recipe type codes are `grid` or `mymod:press`: a domain is optional.
      const code = dec(rest.join("/"));
      if (!code || !/^\S+$/.test(code)) break;
      const page = Number(params.get("page") ?? "1");
      // Page 1 is the bare address; a page past the end is the type page's to clamp.
      return Number.isSafeInteger(page) && page > 1 ? { view: "type", version, code, page } : { view: "type", version, code };
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
    case "type":
      return `#/${enc(route.version)}/type/${enc(route.code)}${route.page && route.page > 1 ? `?page=${route.page}` : ""}`;
    case "entities":
      return `#/${enc(route.version)}/entities`;
    case "entity":
      return `#/${enc(route.version)}/entity/${enc(route.code)}${route.variant ? `?${new URLSearchParams({ variant: route.variant }).toString()}` : ""}`;
    case "credits":
      return `#/${enc(route.version)}/credits`;
    case "models":
      return "#/models";
    case "model":
      return `#/models/${enc(route.id)}`;
    case "notfound":
      return route.version ? `#/${enc(route.version)}/not-found` : "#/not-found";
  }
}

/** The same page in another version, used by the version switcher. */
export function withVersion(route: Route, version: string): Route {
  switch (route.view) {
    case "root":
    case "notfound":
    case "models":
    case "model":
      return { view: "home", version };
    case "item":
      return { view: "item", version, code: route.code };
    case "type":
      // Another version can have fewer recipes of the type; start again at its first page.
      return { view: "type", version, code: route.code };
    default:
      return { ...route, version };
  }
}
