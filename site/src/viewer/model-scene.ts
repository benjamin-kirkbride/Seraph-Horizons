// The model viewer's three.js scene. Only the model page loads this module (a dynamic import),
// so three.js never reaches the recipe browser's bundle. Everything it draws comes from a
// ModelView (src/lib/model-view.ts): one mesh per rig part and animation joint, posed by the
// part's matrix times the joint's (a shape's keyframe animation; keyframes.ts), and the rig's
// overlays. Posing only sets matrices: the geometry is built once. Units are blocks; the rig's frame is three's (y up, VS north is −z).
import {
  ArrowHelper,
  type Material,
  NearestFilter,
  SRGBColorSpace,
  Texture,
  TextureLoader,
  BoxGeometry,
  BufferAttribute,
  BufferGeometry,
  Color,
  DirectionalLight,
  DoubleSide,
  EdgesGeometry,
  Float32BufferAttribute,
  Group,
  HemisphereLight,
  LineBasicMaterial,
  LineSegments,
  Matrix4,
  Mesh,
  MeshBasicMaterial,
  MeshLambertMaterial,
  PerspectiveCamera,
  PlaneGeometry,
  Raycaster,
  Scene,
  SphereGeometry,
  Vector2,
  Vector3,
  WebGLRenderer,
  type Object3D,
} from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { pieceMatrix } from "../lib/keyframes.ts";
import { SIDE_NORMAL, cellBoxes, lidBox, sideArrow, type Anchor, type Bounds } from "../lib/model-anchors.ts";
import type { ModelView } from "../lib/model-view.ts";
import { TRACK_OVERLAY, type TrackLayout } from "../lib/model-vehicle.ts";
import { FACE_NAMES, collapsed, corners, faceUv, type FaceName, type Mat4, type Vec3 } from "../lib/rig.ts";

export interface SceneColours {
  background: string;
  edge: string;
  grid: string;
  highlight: string;
}

export type ColourMode = "part" | "texture";

export const VIEWS = ["angled", "opposite", "front", "side", "top"] as const;
export type ViewName = (typeof VIEWS)[number];

// Azimuth from south (+z) towards east (+x), and elevation, in degrees.
const VIEW_ANGLES: Record<ViewName, [number, number]> = {
  angled: [-35, 28],
  opposite: [145, 28],
  front: [0, 0],
  side: [-90, 0],
  top: [0, 89.9],
};

// Corner i of a box: bit 0 x, bit 1 y, bit 2 z set means `to` on that axis.
const FACE_CORNERS: Record<FaceName, [number, number, number, number]> = {
  west: [0, 4, 6, 2],
  east: [1, 3, 7, 5],
  down: [0, 1, 5, 4],
  up: [2, 6, 7, 3],
  north: [0, 2, 3, 1],
  south: [4, 5, 7, 6],
};
const DIMMED = new Color(0x5a6168);
const WHITE = new Color(0xffffff);
const EDGES: [number, number][] = [
  [0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7],
];

const OVERLAY_COLOURS = { cell: 0xe8590c, side: 0x2f9e44, point: 0x9c36b5, line: 0x8b5a2b, level: 0x1c7ed6, collision: 0x1d9bd6, lid: 0x7048e8, origin: 0xf08c00 };
const TRACK_COLOURS = { rail: 0x7d828b, sleeper: 0x6b4a2f };
const IDENTITY: readonly number[] = new Matrix4().elements;

function boxEdges(lo: readonly number[], hi: readonly number[]): number[] {
  const c = (i: number) => [(i & 1 ? hi : lo)[0]!, (i & 2 ? hi : lo)[1]!, (i & 4 ? hi : lo)[2]!];
  return EDGES.flatMap(([i, j]) => [...c(i), ...c(j)]);
}

function lines(points: number[], colour: number | Color, opacity = 1): LineSegments {
  const g = new BufferGeometry();
  g.setAttribute("position", new Float32BufferAttribute(points, 3));
  return new LineSegments(g, new LineBasicMaterial({ color: colour, transparent: opacity < 1, opacity }));
}

/** The elements of one rig part that move with one animation joint (-1: none), drawn as one mesh. */
interface PartObject {
  part: number;
  joint: number;
  mesh: Mesh;
  edges: LineSegments;
  colourPart: Float32Array;
  colourTexture: Float32Array;
  /** The element each triangle belongs to. */
  triangles: Int32Array;
  /** The vertex-coloured material (part or texture colours). */
  coloured: MeshLambertMaterial;
  /** The texture code of each of the geometry's groups, one per code its faces draw with. */
  groupCodes: string[];
  /** setGlow's copies of this part's materials, by the material each copies (the textured ones are shared). */
  glowCopies: Map<Material, MeshLambertMaterial>;
}

export class ModelScene {
  private readonly renderer: WebGLRenderer;
  private readonly scene = new Scene();
  private readonly camera = new PerspectiveCamera(32, 1, 0.05, 400);
  private readonly controls: OrbitControls;
  private readonly partObjects: PartObject[] = [];
  private readonly edgeMaterial = new LineBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.28 });
  private readonly gridMaterial = new LineBasicMaterial({ color: 0x888888, transparent: true, opacity: 0.55 });
  private readonly overlays = new Map<string, Group>();
  private readonly labels: { el: HTMLElement; at: Vector3; overlay: string; ride?: { part: number; pos: Vec3 } }[] = [];
  /** Point anchors that ride a part: their group, posed with the part. */
  private readonly riders: { group: Group; part: number }[] = [];
  private readonly rideMatrix = new Matrix4();
  private readonly cornersBlocks: Vec3[][];
  private readonly pickLines: LineSegments;
  private readonly hoverLines: LineSegments;
  private readonly prop = new Group();
  /** A vehicle's sleepers, which scroll along the track; null without a track. */
  private sleepers: Group | null = null;
  private trackAxis: 0 | 2 = 0;
  private readonly target: Vector3;
  private readonly radius: number;
  private matrices: Mat4[] = [];
  /** Each joint's motion, voxels (keyframes.ts's jointDeltas); empty at rest. */
  private joints: ReadonlyMap<number, Mat4> = new Map();
  private visible: boolean[] = [];
  /** The piece (index into partObjects) each element is drawn in. */
  private readonly elementObject: number[] = [];
  private picked: number | null = null;
  private hovered: number | null = null;
  private dirty = true;
  private frame = 0;
  private readonly resizeObserver: ResizeObserver;
  private readonly ray = new Raycaster();
  private readonly ndc = new Vector2();
  private readonly tmp = new Vector3();

  private readonly canvas: HTMLCanvasElement;
  private readonly labelLayer: HTMLElement;
  private readonly view: ModelView;
  private edgesOn = true;
  private colourMode: ColourMode = "part";
  /** setTextures's materials, one per texture code, and its textures by URL. */
  private readonly textured = new Map<string, MeshLambertMaterial>();
  private readonly textureCache = new Map<string, Texture>();
  private readonly loader = new TextureLoader();
  private dimmedExcept: string | null = null;
  /** setGlow's last call: which parts are hot, their glow colour and how far they are mixed towards it. */
  private glow: { parts: ReadonlySet<number>; rgb: readonly [number, number, number]; mix: number } | null = null;

  /** Builds the scene into `canvas`, with the overlays' labels in `labelLayer` (and a vehicle's track under it); throws when WebGL cannot start. */
  constructor(canvas: HTMLCanvasElement, labelLayer: HTMLElement, view: ModelView, colours: SceneColours, propColour = "#8b5a2b", track: TrackLayout | null = null) {
    this.canvas = canvas;
    this.labelLayer = labelLayer;
    this.view = view;
    // Throws when WebGL cannot start; the page shows its fallback then.
    this.renderer = new WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.12;
    this.controls.screenSpacePanning = true;
    this.controls.addEventListener("change", () => (this.dirty = true));

    this.scene.add(new HemisphereLight(0xffffff, 0x60584c, 2.3));
    const sun = new DirectionalLight(0xffffff, 2.0);
    sun.position.set(-4, 9, 7);
    const fill = new DirectionalLight(0xffffff, 0.7);
    fill.position.set(6, 3, -5);
    this.scene.add(sun, fill);

    this.cornersBlocks = view.flat.map((f) => corners(f).map((c) => [c[0] / 16, c[1] / 16, c[2] / 16] as Vec3));
    // One piece per part and joint: elements that move together are one mesh, so a shape with no
    // animations has one per part, and the eidolon one per animated limb.
    const joints = view.animation.joints;
    view.parts.forEach((vp, pi) => {
      const byJoint = new Map<number, number[]>();
      for (const ei of vp.elements) {
        const j = joints[ei] ?? -1;
        let list = byJoint.get(j);
        if (!list) byJoint.set(j, (list = []));
        list.push(ei);
      }
      for (const [j, elements] of byJoint) {
        for (const ei of elements) this.elementObject[ei] = this.partObjects.length;
        this.partObjects.push(this.buildPart(pi, j, elements));
      }
    });
    this.buildOverlays(view.bounds, view.anchors);
    if (track) this.buildTrack(track);

    const outline = (opacity: number) => {
      const g = new BufferGeometry();
      g.setAttribute("position", new BufferAttribute(new Float32Array(EDGES.length * 6), 3));
      const l = new LineSegments(g, new LineBasicMaterial({ color: 0xd9480f, depthTest: false, transparent: true, opacity }));
      l.renderOrder = 999;
      l.matrixAutoUpdate = false;
      l.visible = false;
      this.scene.add(l);
      return l;
    };
    this.pickLines = outline(1);
    this.hoverLines = outline(0.45);

    const propMesh = new Mesh(new BoxGeometry(1, 1, 1), new MeshLambertMaterial({ color: propColour, transparent: true, opacity: 0.38, depthWrite: false }));
    const propEdges = new LineSegments(new EdgesGeometry(new BoxGeometry(1, 1, 1)), new LineBasicMaterial({ color: new Color(propColour).multiplyScalar(0.7) }));
    this.prop.add(propMesh, propEdges);
    this.prop.visible = false;
    this.scene.add(this.prop);

    const { lo, hi } = view.bounds;
    this.target = new Vector3((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
    // The footprint's bounding sphere, with a margin for the overlays' arrows and labels.
    this.radius = Math.max(1, Math.hypot(hi[0] - lo[0], hi[1] - lo[1], hi[2] - lo[2]) / 2 + 1);

    this.setColours(colours);
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(canvas);
    this.resize();
    this.setView("angled");
    const loop = () => {
      this.frame = requestAnimationFrame(loop);
      if (this.controls.update()) this.dirty = true;
      if (this.dirty) {
        this.dirty = false;
        this.renderer.render(this.scene, this.camera);
        this.placeLabels();
      }
    };
    this.frame = requestAnimationFrame(loop);
  }

  private buildPart(pi: number, joint: number, elements: readonly number[]): PartObject {
    const vp = this.view.parts[pi]!;
    const partColour = new Color(vp.colour);
    const textureColour = new Map(this.view.textures.map((t) => [t.code, new Color(t.colour)]));
    const pos: number[] = [];
    const nor: number[] = [];
    const colPart: number[] = [];
    const colTex: number[] = [];
    const uvs: number[] = [];
    const tri: number[] = [];
    const edge: number[] = [];
    // Faces are drawn grouped by texture code, so a textured scene (setTextures) gives each code its material.
    const faceList: { ei: number; code: string; corners: number[]; uv: Map<number, [number, number]> }[] = [];
    for (const ei of elements) {
      const cs = this.cornersBlocks[ei]!;
      const e = this.view.flat[ei]!.element;
      const centre = cs.reduce((a, c) => [a[0] + c[0] / 8, a[1] + c[1] / 8, a[2] + c[2] / 8], [0, 0, 0]);
      // VS draws only the faces an element lists; a shape with no faces at all is drawn whole.
      const faces = e.faces ? FACE_NAMES.filter((n) => e.faces![n] && e.faces![n]!.enabled !== false) : FACE_NAMES;
      for (const name of faces) {
        const corners = [...FACE_CORNERS[name]];
        const [a, b, c] = corners.map((i) => cs[i]!) as [Vec3, Vec3, Vec3];
        const u = new Vector3(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
        const v = new Vector3(c[0] - a[0], c[1] - a[1], c[2] - a[2]);
        const out = new Vector3((a[0] + c[0]) / 2 - centre[0], (a[1] + c[1]) / 2 - centre[1], (a[2] + c[2]) / 2 - centre[2]);
        if (u.cross(v).dot(out) < 0) [corners[1], corners[3]] = [corners[3]!, corners[1]!];
        const face = e.faces?.[name];
        faceList.push({ ei, code: face?.texture?.replace(/^#/, "") ?? "?", corners, uv: faceUv(name, face) });
      }
      for (const [i, j] of EDGES) edge.push(...cs[i]!, ...cs[j]!);
    }
    const groupCodes: string[] = [];
    const byCode = new Map<string, typeof faceList>();
    for (const f of faceList) {
      let list = byCode.get(f.code);
      if (!list) {
        byCode.set(f.code, (list = []));
        groupCodes.push(f.code);
      }
      list.push(f);
    }
    const geo = new BufferGeometry();
    groupCodes.forEach((code, gi) => {
      const list = byCode.get(code)!;
      geo.addGroup(pos.length / 3, list.length * 6, gi);
      const tc = textureColour.get(code) ?? new Color(0xc0c0c0);
      for (const f of list) {
        const cs = this.cornersBlocks[f.ei]!;
        const [a, b, c] = f.corners.map((i) => cs[i]!) as [Vec3, Vec3, Vec3];
        const n = new Vector3(b[0] - a[0], b[1] - a[1], b[2] - a[2]).cross(new Vector3(c[0] - a[0], c[1] - a[1], c[2] - a[2])).normalize();
        for (const k of [0, 1, 2, 0, 2, 3]) {
          pos.push(...cs[f.corners[k]!]!);
          nor.push(n.x, n.y, n.z);
          uvs.push(...f.uv.get(f.corners[k]!)!);
          colPart.push(partColour.r, partColour.g, partColour.b);
          colTex.push(tc.r, tc.g, tc.b);
        }
        tri.push(f.ei, f.ei);
      }
    });
    geo.setAttribute("position", new Float32BufferAttribute(pos, 3));
    geo.setAttribute("normal", new Float32BufferAttribute(nor, 3));
    geo.setAttribute("uv", new Float32BufferAttribute(uvs, 2));
    const colourPart = new Float32Array(colPart);
    geo.setAttribute("color", new BufferAttribute(colourPart.slice(), 3));
    geo.computeBoundingSphere();
    const coloured = new MeshLambertMaterial({ vertexColors: true, side: DoubleSide });
    const mesh = new Mesh(geo, groupCodes.map(() => coloured));
    const edges = lines(edge, 0);
    edges.material = this.edgeMaterial;
    for (const o of [mesh, edges]) {
      o.matrixAutoUpdate = false;
      this.scene.add(o);
    }
    mesh.userData.object = this.partObjects.length;
    return { part: pi, joint, mesh, edges, colourPart, colourTexture: new Float32Array(colTex), triangles: new Int32Array(tri), coloured, groupCodes, glowCopies: new Map() };
  }

  private overlay(id: string): Group {
    let g = this.overlays.get(id);
    if (!g) {
      g = new Group();
      g.visible = false;
      this.overlays.set(id, g);
      this.scene.add(g);
    }
    return g;
  }

  private label(overlay: string, text: string, at: Vec3, ridePart?: number) {
    const el = document.createElement("span");
    el.textContent = text;
    el.hidden = true;
    this.labelLayer.appendChild(el);
    this.labels.push({ el, at: new Vector3(...at), overlay, ...(ridePart !== undefined ? { ride: { part: ridePart, pos: at } } : {}) });
  }

  private buildOverlays(bounds: Bounds, anchors: readonly Anchor[]) {
    const { lo, hi } = bounds;
    // Cells: every footprint cell outlined, the origin cell in its own colour, and a floor grid.
    const cellPts: number[] = [];
    const rigCells = this.view.cells;
    for (const c of rigCells) cellPts.push(...boxEdges(c.pos, c.pos.map((v) => v + 1)));
    for (let x = lo[0]; x <= hi[0]; x++) cellPts.push(x, 0, lo[2], x, 0, hi[2]);
    for (let z = lo[2]; z <= hi[2]; z++) cellPts.push(lo[0], 0, z, hi[0], 0, z);
    const grid = lines(cellPts, 0);
    grid.material = this.gridMaterial;
    this.overlay("cells").add(grid, lines(boxEdges([0, 0, 0], [1, 1, 1]), OVERLAY_COLOURS.origin));

    // Collision boxes: translucent, with outlines. The lids (collision-only decks over the
    // machine's top, which the game adds to the top cell of every column) in their own shade,
    // so the deck reads apart from the boxes under it.
    const collision = this.overlay("collision");
    const box = new BoxGeometry(1, 1, 1);
    const fill = new MeshBasicMaterial({ color: OVERLAY_COLOURS.collision, transparent: true, opacity: 0.16, depthWrite: false });
    const lidFill = new MeshBasicMaterial({ color: OVERLAY_COLOURS.lid, transparent: true, opacity: 0.28, depthWrite: false });
    const collisionPts: number[] = [];
    const lidPts: number[] = [];
    const draw = (b: Bounds, material: MeshBasicMaterial, pts: number[]) => {
      const m = new Mesh(box, material);
      m.scale.set(b.hi[0] - b.lo[0], b.hi[1] - b.lo[1], b.hi[2] - b.lo[2]);
      m.position.set((b.lo[0] + b.hi[0]) / 2, (b.lo[1] + b.hi[1]) / 2, (b.lo[2] + b.hi[2]) / 2);
      collision.add(m);
      pts.push(...boxEdges(b.lo, b.hi));
    };
    for (const c of rigCells) {
      for (const b of cellBoxes(c)) draw(b, fill, collisionPts);
      const lid = lidBox(c);
      if (lid) draw(lid, lidFill, lidPts);
    }
    collision.add(lines(collisionPts, OVERLAY_COLOURS.collision, 0.8), lines(lidPts, OVERLAY_COLOURS.lid, 0.8));

    for (const a of anchors) {
      const g = this.overlay(a.key);
      switch (a.kind) {
        case "cell": {
          g.add(lines(boxEdges(a.pos, a.pos.map((v) => v + 1)), OVERLAY_COLOURS.cell));
          let at: Vec3 = [a.pos[0] + 0.5, a.pos[1] + 1.2, a.pos[2] + 0.5];
          if (a.face) {
            const n = SIDE_NORMAL[a.face];
            const centre: Vec3 = [a.pos[0] + 0.5 + n[0] * 0.501, a.pos[1] + 0.5 + n[1] * 0.501, a.pos[2] + 0.5 + n[2] * 0.501];
            const plane = new Mesh(
              new PlaneGeometry(1, 1),
              new MeshBasicMaterial({ color: OVERLAY_COLOURS.cell, transparent: true, opacity: 0.45, side: DoubleSide, depthWrite: false }),
            );
            plane.position.set(...centre);
            plane.lookAt(centre[0] + n[0], centre[1] + n[1], centre[2] + n[2]);
            g.add(plane);
            at = centre.map((v, k) => v + n[k]! * 1.1) as Vec3;
            g.add(new ArrowHelper(new Vector3(-n[0], -n[1], -n[2]), new Vector3(...at), 1.0, OVERLAY_COLOURS.cell, 0.3, 0.18));
          }
          this.label(a.key, a.label, at);
          break;
        }
        case "side": {
          const { from, dir } = sideArrow(a.side, bounds, anchors);
          g.add(new ArrowHelper(new Vector3(...dir), new Vector3(...from), 1.2, OVERLAY_COLOURS.side, 0.32, 0.2));
          this.label(a.key, a.label, from);
          break;
        }
        case "point": {
          // A point that rides a part is drawn in a group the part's matrix poses.
          const part = a.part !== undefined ? this.view.parts.findIndex((p) => p.id === a.part) : -1;
          const holder = part >= 0 ? new Group() : g;
          if (part >= 0) {
            holder.matrixAutoUpdate = false;
            g.add(holder);
            this.riders.push({ group: holder, part });
          }
          const s = new Mesh(new SphereGeometry(0.09, 16, 12), new MeshBasicMaterial({ color: OVERLAY_COLOURS.point }));
          s.position.set(...a.pos);
          holder.add(s);
          const n = a.side ? SIDE_NORMAL[a.side] : null;
          if (n) holder.add(new ArrowHelper(new Vector3(...n), new Vector3(...a.pos), 0.8, OVERLAY_COLOURS.point, 0.25, 0.15));
          this.label(a.key, a.label, n ? (a.pos.map((v, k) => v + n[k]! * 0.9) as Vec3) : [a.pos[0], a.pos[1] + 0.4, a.pos[2]], part >= 0 ? part : undefined);
          break;
        }
        case "line": {
          const k = { x: 0, y: 1, z: 2 }[a.axis];
          const p0 = [...a.origin];
          const p1 = [...a.origin];
          p0[k] = a.origin[k]! - a.length / 2;
          p1[k] = a.origin[k]! + a.length / 2;
          // The line, and a tick across it at each end and at the origin.
          const across = k === 1 ? 0 : 2 - k;
          const tick = (p: number[]) => {
            const q0 = [...p];
            const q1 = [...p];
            q0[across] = p[across]! - 0.2;
            q1[across] = p[across]! + 0.2;
            return [...q0, ...q1];
          };
          g.add(lines([...p0, ...p1, ...tick(p0), ...tick(p1), ...tick([...a.origin])], OVERLAY_COLOURS.line));
          this.label(a.key, a.label, [p1[0]!, p1[1]! + 0.3, p1[2]!]);
          // Stations: an upright tick at each place along the axis, labelled.
          for (const mark of a.marks ?? []) {
            const at = [...a.origin];
            at[k] = mark.at;
            const up = k === 1 ? across : 1;
            const top = [...at];
            top[up] = at[up]! + 0.35;
            g.add(lines([...at, ...top], OVERLAY_COLOURS.line));
            this.label(a.key, mark.label, top as Vec3);
          }
          break;
        }
        case "level": {
          const y = a.y;
          g.add(lines([lo[0], y, lo[2], hi[0], y, lo[2], hi[0], y, lo[2], hi[0], y, hi[2], hi[0], y, hi[2], lo[0], y, hi[2], lo[0], y, hi[2], lo[0], y, lo[2]], OVERLAY_COLOURS.level));
          this.label(a.key, a.label, [hi[0] + 0.2, y, hi[2]]);
          break;
        }
      }
    }
  }

  /** A vehicle's track: the rails, and the sleepers in a group of their own that setTrackScroll moves. */
  private buildTrack(track: TrackLayout) {
    const g = this.overlay(TRACK_OVERLAY);
    const unit = new BoxGeometry(1, 1, 1);
    const add = (to: Group, boxes: TrackLayout["rails"], colour: number) => {
      const material = new MeshLambertMaterial({ color: colour });
      for (const b of boxes) {
        const m = new Mesh(unit, material);
        m.position.set(...b.centre);
        m.scale.set(...b.size);
        to.add(m);
      }
    };
    add(g, track.rails, TRACK_COLOURS.rail);
    this.sleepers = new Group();
    add(this.sleepers, track.sleepers, TRACK_COLOURS.sleeper);
    g.add(this.sleepers);
    this.trackAxis = track.axis === "z" ? 2 : 0;
  }

  /** Shifts the sleepers along the track by `offset` blocks (model-vehicle.ts's trackScroll). */
  setTrackScroll(offset: number) {
    if (!this.sleepers) return;
    this.sleepers.position.set(this.trackAxis === 0 ? offset : 0, 0, this.trackAxis === 2 ? offset : 0);
    this.dirty = true;
  }

  /** A piece's matrix, blocks: its part's, times its joint's motion (voxels) when it has one. */
  private objectMatrix(o: PartObject, out: Matrix4): Matrix4 {
    const part = (this.matrices[o.part] ?? IDENTITY) as Mat4;
    const d = o.joint >= 0 ? this.joints.get(o.joint) : undefined;
    return d ? out.fromArray(pieceMatrix(part, d)) : out.fromArray(part);
  }

  /** Whether a part is drawn: fitted, and not stretched to nothing (rig.ts's collapsed). */
  private shown(part: number): boolean {
    const m = this.matrices[part];
    return (this.visible[part] ?? true) && !(m && collapsed(m));
  }

  /**
   * Poses each part by its matrix (blocks) and shows the fitted ones, unless stretched to nothing;
   * `joints`, each animation joint's motion (voxels, keyframes.ts's jointDeltas), moves its elements
   * within their part.
   */
  pose(matrices: Mat4[], visible: boolean[], joints: ReadonlyMap<number, Mat4> | null = null) {
    this.matrices = matrices;
    this.visible = visible;
    this.joints = joints ?? new Map();
    for (const o of this.partObjects) {
      this.objectMatrix(o, o.mesh.matrix);
      o.edges.matrix.copy(o.mesh.matrix);
      for (const obj of [o.mesh, o.edges] as Object3D[]) obj.matrixWorldNeedsUpdate = true;
      o.mesh.visible = this.shown(o.part);
      o.edges.visible = o.mesh.visible && this.edgesOn;
    }
    for (const r of this.riders) {
      r.group.matrix.fromArray(matrices[r.part]!);
      r.group.matrixWorldNeedsUpdate = true;
    }
    for (const l of this.labels)
      if (l.ride) l.at.set(...l.ride.pos).applyMatrix4(this.rideMatrix.fromArray(matrices[l.ride.part]!));
    this.updateOutline(this.pickLines, this.picked);
    this.updateOutline(this.hoverLines, this.hovered);
    this.dirty = true;
  }

  /** Turns the camera slowly round the model (OrbitControls' autoRotate), or stops it. */
  setAutoRotate(on: boolean) {
    this.controls.autoRotate = on;
    this.controls.autoRotateSpeed = 1.2;
    this.dirty = true;
  }

  setEdges(on: boolean) {
    this.edgesOn = on;
    for (const o of this.partObjects) o.edges.visible = on && this.shown(o.part);
    this.dirty = true;
  }

  /**
   * Draws the parts in `parts` (indices into the view's parts) hot, the rest not: their lit colour mixed `mix` of the
   * way towards `rgb`, which is also their emission (so it shows in shadow), as the game's standard shader mixes a hot
   * part's (model-scenario's hotGlow). A mix of 0 (or no parts) draws everything as usual.
   */
  setGlow(parts: ReadonlySet<number>, rgb: readonly [number, number, number], mix: number) {
    this.glow = { parts, rgb, mix };
    this.applyGlow();
    this.dirty = true;
  }

  /** Each part's materials: its texture code's (setTextures), else its vertex-coloured one. */
  private baseMaterials(o: PartObject): Material[] {
    return o.groupCodes.map((code): Material => this.textured.get(code) ?? o.coloured);
  }

  /**
   * Gives a hot part copies of its materials, glowing (a textured material is shared by every part with its code, so
   * it is never changed itself), and every other part its own materials back.
   */
  private applyGlow() {
    const g = this.glow;
    for (const o of this.partObjects) {
      const f = g && g.parts.has(o.part) ? Math.min(1, Math.max(0, g.mix)) : 0;
      const base = this.baseMaterials(o);
      if (f === 0 || !g) {
        o.mesh.material = base;
        continue;
      }
      o.mesh.material = base.map((b) => {
        let c = o.glowCopies.get(b);
        if (!c) {
          c = (b as MeshLambertMaterial).clone();
          o.glowCopies.set(b, c);
        } else c.copy(b as MeshLambertMaterial);
        c.color.multiplyScalar(1 - f);
        c.emissive.setRGB(g.rgb[0] * f, g.rgb[1] * f, g.rgb[2] * f);
        c.needsUpdate = true;
        return c;
      });
    }
  }

  /** Redraws the faces of each texture code given in its colour (a scenario's texture select), in texture colours. */
  setTextureColours(colours: ReadonlyMap<string, string>) {
    for (const o of this.partObjects) {
      const groups = o.mesh.geometry.groups;
      o.groupCodes.forEach((code, gi) => {
        const hex = colours.get(code);
        const g = groups[gi];
        if (hex === undefined || !g) return;
        const c = new Color(hex);
        for (let v = g.start; v < g.start + g.count; v++) o.colourTexture.set([c.r, c.g, c.b], v * 3);
      });
    }
    this.colourBy(this.colourMode);
  }

  colourBy(mode: ColourMode) {
    this.colourMode = mode;
    for (const o of this.partObjects) {
      const a = o.mesh.geometry.getAttribute("color") as BufferAttribute;
      (a.array as Float32Array).set(mode === "texture" ? o.colourTexture : o.colourPart);
      a.needsUpdate = true;
    }
    this.dirty = true;
  }

  /**
   * Draws each texture code with its image (a URL, here a data URI), the game's texture on the
   * face's uv, or with the vertex colours again when `images` is null; a code with no image keeps
   * its vertex colour. `size` is the shape's texture size in its units (textureWidth, textureHeight).
   */
  setTextures(images: ReadonlyMap<string, string> | null, size: readonly [number, number] = [16, 16]) {
    for (const [code, m] of this.textured) {
      const url = images?.get(code);
      if (!url) {
        m.dispose();
        this.textured.delete(code);
      }
    }
    if (images) {
      for (const [code, url] of images) {
        let tex = this.textureCache.get(url);
        if (!tex) {
          tex = this.loader.load(url, () => (this.dirty = true));
          tex.magFilter = NearestFilter;
          tex.minFilter = NearestFilter;
          tex.generateMipmaps = false;
          tex.colorSpace = SRGBColorSpace;
          // The uv is in the shape's units with v negated: scale it to 0..1 and lift v into range.
          tex.repeat.set(1 / size[0], 1 / size[1]);
          tex.offset.set(0, 1);
          this.textureCache.set(url, tex);
        }
        const m = this.textured.get(code);
        if (m) {
          m.map = tex;
          m.needsUpdate = true;
        } else this.textured.set(code, new MeshLambertMaterial({ map: tex, side: DoubleSide, alphaTest: 0.1 }));
      }
    }
    this.dim(this.dimmedExcept);
  }

  /** Greys out and fades every textured code but `code` (null: none). */
  dim(code: string | null) {
    this.dimmedExcept = code;
    for (const [c, m] of this.textured) {
      const faded = code !== null && c !== code;
      m.color.copy(faded ? DIMMED : WHITE);
      m.transparent = faded;
      m.opacity = faded ? 0.5 : 1;
      m.depthWrite = !faded;
      m.needsUpdate = true;
    }
    this.applyGlow();
    this.dirty = true;
  }

  setOverlay(id: string, on: boolean) {
    const g = this.overlays.get(id);
    if (g) g.visible = on;
    for (const l of this.labels) if (l.overlay === id) l.el.hidden = !on;
    this.dirty = true;
  }

  /** Shows the prop box (centre and size in blocks), or hides it. */
  setProp(box: { centre: Vec3; size: Vec3 } | null) {
    this.prop.visible = box !== null;
    if (box) {
      this.prop.position.set(...box.centre);
      this.prop.scale.set(...box.size);
    }
    this.dirty = true;
  }

  /** Outlines the pinned element and the one under the pointer. */
  highlight(picked: number | null, hovered: number | null) {
    this.picked = picked;
    this.hovered = hovered === picked ? null : hovered;
    this.updateOutline(this.pickLines, this.picked);
    this.updateOutline(this.hoverLines, this.hovered);
    this.dirty = true;
  }

  private updateOutline(target: LineSegments, element: number | null) {
    if (element === null) {
      target.visible = false;
      return;
    }
    const o = this.partObjects[this.elementObject[element]!]!;
    const cs = this.cornersBlocks[element]!;
    const attr = target.geometry.getAttribute("position") as BufferAttribute;
    const arr = attr.array as Float32Array;
    EDGES.forEach(([i, j], k) => {
      arr.set(cs[i]!, k * 6);
      arr.set(cs[j]!, k * 6 + 3);
    });
    attr.needsUpdate = true;
    target.geometry.computeBoundingSphere();
    this.objectMatrix(o, target.matrix);
    target.matrixWorldNeedsUpdate = true;
    target.visible = this.shown(o.part);
  }

  /** The element under a point of the canvas, or null. */
  pick(clientX: number, clientY: number): number | null {
    const r = this.canvas.getBoundingClientRect();
    this.ndc.set(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    this.ray.setFromCamera(this.ndc, this.camera);
    const meshes = this.partObjects.filter((o) => o.mesh.visible).map((o) => o.mesh);
    const hit = this.ray.intersectObjects(meshes, false)[0];
    if (!hit || hit.faceIndex == null) return null;
    const o = this.partObjects[hit.object.userData.object as number]!;
    return o.triangles[hit.faceIndex] ?? null;
  }

  setView(name: ViewName | "reset") {
    const [az, el] = VIEW_ANGLES[name === "reset" ? "angled" : name];
    const a = (az * Math.PI) / 180;
    const e = (el * Math.PI) / 180;
    const dir = new Vector3(Math.sin(a) * Math.cos(e), Math.sin(e), Math.cos(a) * Math.cos(e));
    this.controls.target.copy(this.target);
    // Far enough that the sphere fits the narrower of the two fields of view (a phone is tall).
    const vfov = (this.camera.fov * Math.PI) / 180;
    const hfov = 2 * Math.atan(Math.tan(vfov / 2) * this.camera.aspect);
    const distance = this.radius / Math.sin(Math.min(vfov, hfov) / 2);
    this.camera.position.copy(this.target).addScaledVector(dir, distance);
    this.camera.up.set(0, 1, 0);
    this.camera.lookAt(this.target);
    this.controls.update();
    this.dirty = true;
  }

  setColours(c: SceneColours) {
    try {
      this.scene.background = new Color(c.background);
      this.edgeMaterial.color.set(c.edge);
      this.gridMaterial.color.set(c.grid);
      (this.pickLines.material as LineBasicMaterial).color.set(c.highlight);
      (this.hoverLines.material as LineBasicMaterial).color.set(c.highlight);
    } catch {
      // A colour three cannot parse keeps the previous one.
    }
    this.dirty = true;
  }

  resize() {
    const w = Math.max(1, this.canvas.clientWidth);
    const h = Math.max(1, this.canvas.clientHeight);
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    this.dirty = true;
  }

  private placeLabels() {
    const w = this.canvas.clientWidth;
    const h = this.canvas.clientHeight;
    for (const l of this.labels) {
      if (l.el.hidden) continue;
      this.tmp.copy(l.at).project(this.camera);
      const off = this.tmp.z > 1 || this.tmp.z < -1;
      l.el.style.visibility = off ? "hidden" : "visible";
      l.el.style.left = `${(((this.tmp.x + 1) / 2) * w).toFixed(1)}px`;
      l.el.style.top = `${(((1 - this.tmp.y) / 2) * h).toFixed(1)}px`;
    }
  }

  dispose() {
    cancelAnimationFrame(this.frame);
    this.resizeObserver.disconnect();
    this.controls.dispose();
    for (const l of this.labels) l.el.remove();
    for (const m of this.textured.values()) m.dispose();
    for (const o of this.partObjects) for (const c of o.glowCopies.values()) c.dispose();
    for (const t of this.textureCache.values()) t.dispose();
    this.scene.traverse((o) => {
      const m = o as Mesh;
      m.geometry?.dispose();
      const mat = m.material;
      if (Array.isArray(mat)) mat.forEach((x) => x.dispose());
      else mat?.dispose();
    });
    this.renderer.dispose();
  }
}
