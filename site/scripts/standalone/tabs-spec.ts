// The tabbed standalone page's spec (scripts/standalone-tabs.ts): what each tab shows and where it
// comes from. Pure, so it is tested without a build (test/standalone-tabs.test.ts).
//
//   {
//     "title": "Ore processing machines",
//     "tabs": [
//       { "kind": "html", "label": "Processing line", "file": "ore-line.ts" },
//       { "kind": "model", "label": "Rocker", "repo": "$TMPDIR/m-rocker", "id": "rocker" }
//     ]
//   }
//
// Paths may use ~, $VAR or ${VAR}, and a relative path is from the spec file's folder. An html tab's
// file is an HTML fragment, or a .ts module whose default export is the fragment or returns it. A model
// tab reads site/models.json, the shape and the rig from the checkout at `repo`, and is drawn by that
// checkout's own viewer code (site/src), so a branch that changed the viewer shows its model as it would
// on the site; tabs whose site/src are the same share one bundle. "viewer": "builder" draws every tab
// with this checkout's viewer instead (one bundle).
import { isAbsolute, resolve } from "node:path";

export interface HtmlTab {
  kind: "html";
  label: string;
  slug: string;
  /** Absolute. */
  file: string;
}

export interface ModelTab {
  kind: "model";
  label: string;
  slug: string;
  /** The checkout's root, absolute. */
  repo: string;
  /** The model's id in that checkout's site/models.json. */
  id: string;
  /** Shown above the viewer. */
  note?: string;
}

export type Tab = HtmlTab | ModelTab;

export interface TabsSpec {
  title: string;
  /** Whose viewer code draws the models: each tab's own checkout (the default), or this one's. */
  viewer: "own" | "builder";
  tabs: Tab[];
}

/** A tab's slug in the page's hash: its label in lower case, words joined by dashes. */
export function slugify(label: string): string {
  return (
    label
      .toLowerCase()
      .normalize("NFKD")
      .replace(/[^\w\s-]/g, "")
      .trim()
      .replace(/[\s_-]+/g, "-") || "tab"
  );
}

/** Expands ~, $VAR and ${VAR}, and resolves a relative path from `base`. An unset variable is an error. */
export function expandPath(path: string, base: string, env: Record<string, string | undefined>): string {
  let p = path.replace(/^~(?=$|\/)/, () => {
    const home = env.HOME;
    if (!home) throw new Error(`${path}: HOME is not set`);
    return home;
  });
  p = p.replace(/\$(?:\{(\w+)\}|(\w+))/g, (_m, braced: string | undefined, bare: string | undefined) => {
    const name = (braced ?? bare)!;
    const value = env[name];
    if (value === undefined || value === "") throw new Error(`${path}: ${name} is not set`);
    return value;
  });
  return isAbsolute(p) ? resolve(p) : resolve(base, p);
}

function text(v: unknown, where: string): string {
  if (typeof v !== "string" || v.trim() === "") throw new Error(`${where} must be a non-empty string`);
  return v;
}

/** Checks a spec and makes its paths absolute; `base` is the spec file's folder. */
export function parseSpec(raw: unknown, base: string, env: Record<string, string | undefined> = process.env): TabsSpec {
  if (typeof raw !== "object" || raw === null) throw new Error("the spec must be an object");
  const r = raw as Record<string, unknown>;
  const title = text(r.title, "title");
  const viewer = r.viewer ?? "own";
  if (viewer !== "own" && viewer !== "builder") throw new Error(`viewer must be "own" or "builder", not ${JSON.stringify(viewer)}`);
  if (!Array.isArray(r.tabs) || r.tabs.length === 0) throw new Error("tabs must be a non-empty list");
  const slugs = new Set<string>();
  const tabs = r.tabs.map((t: unknown, i): Tab => {
    const where = `tabs[${i}]`;
    if (typeof t !== "object" || t === null) throw new Error(`${where} must be an object`);
    const o = t as Record<string, unknown>;
    const label = text(o.label, `${where}.label`);
    const slug = o.slug === undefined ? slugify(label) : text(o.slug, `${where}.slug`);
    if (!/^[a-z0-9][a-z0-9-]*$/.test(slug)) throw new Error(`${where}: slug "${slug}" must be lower-case letters, digits and dashes`);
    if (slugs.has(slug)) throw new Error(`${where}: slug "${slug}" is used twice (give one tab a "slug")`);
    slugs.add(slug);
    if (o.kind === "html") return { kind: "html", label, slug, file: expandPath(text(o.file, `${where}.file`), base, env) };
    if (o.kind === "model") {
      const note = o.note === undefined ? undefined : text(o.note, `${where}.note`);
      return {
        kind: "model",
        label,
        slug,
        repo: expandPath(text(o.repo, `${where}.repo`), base, env),
        id: text(o.id, `${where}.id`),
        ...(note ? { note } : {}),
      };
    }
    throw new Error(`${where}.kind must be "html" or "model"`);
  });
  return { title, viewer, tabs };
}

/** Groups items by key, keeping the order each key first appears in. */
export function groupBy<T>(items: readonly T[], key: (item: T) => string): Map<string, T[]> {
  const out = new Map<string, T[]>();
  for (const item of items) {
    const k = key(item);
    const list = out.get(k);
    if (list) list.push(item);
    else out.set(k, [item]);
  }
  return out;
}

/** Escapes text for HTML content and attribute values. */
export function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
}
