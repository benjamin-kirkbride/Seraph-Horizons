// VTML is the game's small markup for handbook text. It is parsed here into a tree of
// the few things the site renders, and components build DOM nodes from that tree.
// Nothing from the data is ever handed to the browser as HTML.

export type VNode =
  | { t: "text"; text: string }
  | { t: "br" }
  | { t: "b"; children: VNode[] }
  | { t: "i"; children: VNode[] }
  | { t: "link"; href: string; children: VNode[] }
  | { t: "item"; code: string; children: VNode[] }
  | { t: "color"; color: string; children: VNode[] };

type Container = Extract<VNode, { children: VNode[] }> | { t: "root"; children: VNode[] };

/** Tags whose content is dropped with them, not shown as text. */
const DROP_WITH_CONTENT = new Set(["script", "style", "iframe", "object", "embed", "template", "noscript", "textarea", "svg", "math"]);

const ENTITIES: Record<string, string> = { amp: "&", lt: "<", gt: ">", quot: '"', apos: "'", nbsp: " " };

export function decodeEntities(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#[0-9]+|[a-z]+);/gi, (whole, body: string) => {
    if (body[0] === "#") {
      const n = body[1] === "x" || body[1] === "X" ? parseInt(body.slice(2), 16) : parseInt(body.slice(1), 10);
      return Number.isFinite(n) && n > 0 && n <= 0x10ffff ? String.fromCodePoint(n) : whole;
    }
    return ENTITIES[body.toLowerCase()] ?? whole;
  });
}

function parseAttributes(text: string): Record<string, string> {
  const attrs: Record<string, string> = {};
  const re = /([a-z_][\w-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))/gi;
  for (let m = re.exec(text); m; m = re.exec(text)) {
    attrs[m[1]!.toLowerCase()] = decodeEntities(m[2] ?? m[3] ?? m[4] ?? "");
  }
  return attrs;
}

/** A handbook page link to an item, `handbook://item-plank-oak`, as a full code. */
function handbookItemCode(href: string): string | null {
  const m = /^handbook:\/\/(?:item|block)-(.+)$/i.exec(href);
  if (!m) return null;
  const code = m[1]!.toLowerCase();
  return code.includes(":") ? code : `game:${code}`;
}

function openNode(tag: string, attrs: Record<string, string>): Container | null {
  switch (tag) {
    case "b":
    case "strong":
      return { t: "b", children: [] };
    case "i":
    case "em":
      return { t: "i", children: [] };
    case "a": {
      const href = (attrs.href ?? "").trim();
      const code = handbookItemCode(href);
      if (code) return { t: "item", code, children: [] };
      if (/^https?:\/\//i.test(href)) return { t: "link", href, children: [] };
      return null;
    }
    case "font": {
      const color = attrs.color ?? "";
      if (/^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i.test(color)) return { t: "color", color, children: [] };
      if ((attrs.weight ?? "").toLowerCase() === "bold") return { t: "b", children: [] };
      return null;
    }
    default:
      return null;
  }
}

export function parseVtml(source: string): VNode[] {
  const root: Container = { t: "root", children: [] };
  // Each open tag is on the stack, with the node it created (null for transparent tags).
  const stack: { tag: string; node: Container | null }[] = [];
  const current = (): Container => {
    for (let i = stack.length - 1; i >= 0; i--) {
      const n = stack[i]!.node;
      if (n) return n;
    }
    return root;
  };
  const pushText = (text: string) => {
    if (!text) return;
    const parent = current();
    const last = parent.children[parent.children.length - 1];
    if (last && last.t === "text") last.text += text;
    else parent.children.push({ t: "text", text });
  };

  const tagRe = /<!--[\s\S]*?(?:-->|$)|<(\/?)([a-z][\w-]*)((?:[^>"']|"[^"]*"|'[^']*')*)>/gi;
  let pos = 0;
  let dropping: string | null = null;
  for (let m = tagRe.exec(source); m; m = tagRe.exec(source)) {
    if (!dropping) pushText(decodeEntities(source.slice(pos, m.index)));
    pos = m.index + m[0].length;
    if (m[2] === undefined) continue; // comment
    const closing = m[1] === "/";
    const tag = m[2].toLowerCase();
    if (dropping) {
      if (closing && tag === dropping) dropping = null;
      continue;
    }
    if (DROP_WITH_CONTENT.has(tag)) {
      if (!closing && !/\/\s*$/.test(m[3] ?? "")) dropping = tag;
      continue;
    }
    if (tag === "br") {
      current().children.push({ t: "br" });
      continue;
    }
    if (closing) {
      const at = stack.map((s) => s.tag).lastIndexOf(tag);
      if (at >= 0) stack.length = at;
      if (tag === "p") current().children.push({ t: "br" });
      continue;
    }
    if (/\/\s*$/.test(m[3] ?? "")) continue; // self-closing, e.g. <icon name=... />
    const node = openNode(tag, parseAttributes(m[3] ?? ""));
    if (node) current().children.push(node as VNode);
    stack.push({ tag, node });
  }
  if (!dropping) {
    // A stray `<` that starts no tag is text.
    pushText(decodeEntities(source.slice(pos)));
  }
  return root.children;
}

/** Plain text of a VTML string, for places that cannot hold markup (titles, labels). */
export function vtmlText(source: string): string {
  const out: string[] = [];
  const walk = (nodes: VNode[]) => {
    for (const n of nodes) {
      if (n.t === "text") out.push(n.text);
      else if (n.t === "br") out.push("\n");
      else walk(n.children);
    }
  };
  walk(parseVtml(source));
  return out.join("");
}
