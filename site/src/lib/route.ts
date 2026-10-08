// Routes live in the URL hash (#/<version>/item/<code>), so GitHub Pages only ever
// serves index.html and a deep link survives a reload. The version is always in the
// route so a shared link keeps pointing at the data it was made from. The model viewer
// (#/models, #/models/<id>) is the exception: it does not read the recipe data, so its
// routes have no version, and "models" can never be a version id.

export type Route =
  | { view: "root" }
  | { view: "home"; version: string }
  | { view: "search"; version: string; query: string; sort?: SearchSort }
  | { view: "item"; version: string; code: string }
  | { view: "type"; version: string; code: string; page?: number }
  | { view: "entities"; version: string }
  | { view: "entity"; version: string; code: string; variant?: string }
  | { view: "credits"; version: string }
  | ({ view: "values"; version: string } & ValuesView)
  | { view: "models" }
  | { view: "model"; id: string }
  | { view: "notfound"; version?: string };

/**
 * The values page's state, in its address so a link keeps it: the filter text (`q`), the
 * order (`sort`, `<column>-<dir>`), whether items without a value are listed (`unvalued=1`),
 * the kind (`kind`) and the worthless and not-in-handbook filters (`worthless`, `unlisted`:
 * `only` or `hide`). A default is left out: value highest first, every kind, everything shown.
 */
export interface ValuesView {
  q?: string;
  sort?: ValuesSort;
  unvalued?: true;
  kind?: "items" | "blocks" | "liquids";
  worthless?: "only" | "hide";
  unlisted?: "only" | "hide";
}
export type ValuesSort = "name-asc" | "name-desc" | "mod-asc" | "mod-desc" | "value-asc";
const VALUES_SORTS: readonly string[] = ["name-asc", "name-desc", "mod-asc", "mod-desc", "value-asc"];
const VALUES_KINDS: readonly string[] = ["items", "blocks", "liquids"];
const FLAG_FILTERS: readonly string[] = ["only", "hide"];

function parseValuesView(params: URLSearchParams): ValuesView {
  const v: ValuesView = {};
  const q = params.get("q");
  if (q) v.q = q;
  const sort = params.get("sort");
  if (sort !== null && VALUES_SORTS.includes(sort)) v.sort = sort as ValuesSort;
  if (params.get("unvalued") === "1") v.unvalued = true;
  const kind = params.get("kind");
  if (kind !== null && VALUES_KINDS.includes(kind)) v.kind = kind as ValuesView["kind"];
  for (const key of ["worthless", "unlisted"] as const) {
    const f = params.get(key);
    if (f !== null && FLAG_FILTERS.includes(f)) v[key] = f as "only" | "hide";
  }
  return v;
}

function formatValuesView(v: ValuesView): string {
  const p = new URLSearchParams();
  if (v.q) p.set("q", v.q);
  if (v.sort) p.set("sort", v.sort);
  if (v.unvalued) p.set("unvalued", "1");
  if (v.kind) p.set("kind", v.kind);
  if (v.worthless) p.set("worthless", v.worthless);
  if (v.unlisted) p.set("unlisted", v.unlisted);
  const s = p.toString();
  return s ? `?${s}` : "";
}

/** Search results in their own order (best match first), or by value. */
export type SearchSort = "value-asc" | "value-desc";
const SEARCH_SORTS: readonly string[] = ["value-asc", "value-desc"];

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
      if (rest.length === 0) {
        const sort = params.get("sort");
        const query = params.get("q") ?? "";
        return sort !== null && SEARCH_SORTS.includes(sort) ? { view: "search", version, query, sort: sort as SearchSort } : { view: "search", version, query };
      }
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
    case "values":
      if (rest.length === 0) return { view: "values", version, ...parseValuesView(params) };
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
      return `#/${enc(route.version)}/search?${new URLSearchParams({ q: route.query, ...(route.sort ? { sort: route.sort } : {}) }).toString()}`;
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
    case "values":
      return `#/${enc(route.version)}/values${formatValuesView(route)}`;
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
