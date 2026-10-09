// The multiblock viewer's three.js scene, loaded only by the multiblock page (a dynamic import).
// Each cell is a mesh sharing its part's geometry (the block's boxes, flat in the part's colour);
// a slice hides cells, a highlighted part dims the others. Cells to leave empty and cells a
// bigger block fills are outlines. Units are blocks, VS's frame (y up, north −z).
import {
  BufferGeometry,
  Color,
  DirectionalLight,
  DoubleSide,
  Float32BufferAttribute,
  HemisphereLight,
  LineBasicMaterial,
  LineSegments,
  Mesh,
  MeshLambertMaterial,
  PerspectiveCamera,
  Raycaster,
  Scene,
  Vector2,
  Vector3,
  WebGLRenderer,
  type Object3D,
} from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import type { Box, MultiblockView } from "../lib/multiblock-view.ts";
import { apply, type FaceName, type Vec3 } from "../lib/rig.ts";

export interface SceneColours {
  background: string;
  edge: string;
  grid: string;
}

export const VIEWS = ["angled", "opposite", "front", "side", "top"] as const;
export type ViewName = (typeof VIEWS)[number];

// Azimuth from south (+z) towards east (+x), and elevation, in degrees (as the model viewer's).
const VIEW_ANGLES: Record<ViewName, [number, number]> = {
  angled: [-35, 28],
  opposite: [145, 28],
  front: [0, 0],
  side: [-90, 0],
  top: [0, 89.9],
};

const FACE_CORNERS: Record<FaceName, [number, number, number, number]> = {
  west: [0, 4, 6, 2],
  east: [1, 3, 7, 5],
  down: [0, 1, 5, 4],
  up: [2, 6, 7, 3],
  north: [0, 2, 3, 1],
  south: [4, 5, 7, 6],
};
const EDGES: [number, number][] = [
  [0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7],
];
const DIM = 0.12;

function boxCorners(b: Box): Vec3[] {
  const out: Vec3[] = [];
  for (let i = 0; i < 8; i++) out.push(apply(b.m, [(i & 1 ? b.to : b.from)[0], (i & 2 ? b.to : b.from)[1], (i & 4 ? b.to : b.from)[2]]));
  return out;
}

/** A part's block as triangles and edge lines, in cell-local blocks. */
function partGeometry(boxes: readonly Box[]): { faces: BufferGeometry; edges: BufferGeometry } {
  const pos: number[] = [];
  const nor: number[] = [];
  const edge: number[] = [];
  for (const b of boxes) {
    const cs = boxCorners(b);
    const centre = cs.reduce((a, c) => [a[0] + c[0] / 8, a[1] + c[1] / 8, a[2] + c[2] / 8], [0, 0, 0]);
    for (const name of b.faces) {
      let [a, p, c, d] = FACE_CORNERS[name].map((i) => cs[i]!) as [Vec3, Vec3, Vec3, Vec3];
      const u = new Vector3(p[0] - a[0], p[1] - a[1], p[2] - a[2]);
      const v = new Vector3(c[0] - a[0], c[1] - a[1], c[2] - a[2]);
      const n = u.cross(v);
      const out = new Vector3((a[0] + c[0]) / 2 - centre[0], (a[1] + c[1]) / 2 - centre[1], (a[2] + c[2]) / 2 - centre[2]);
      if (n.dot(out) < 0) {
        [p, d] = [d, p];
        n.negate();
      }
      n.normalize();
      for (const q of [a, p, c, a, c, d]) {
        pos.push(...q);
        nor.push(n.x, n.y, n.z);
      }
    }
    for (const [i, j] of EDGES) edge.push(...cs[i]!, ...cs[j]!);
  }
  const faces = new BufferGeometry();
  faces.setAttribute("position", new Float32BufferAttribute(pos, 3));
  faces.setAttribute("normal", new Float32BufferAttribute(nor, 3));
  faces.computeBoundingSphere();
  const edges = new BufferGeometry();
  edges.setAttribute("position", new Float32BufferAttribute(edge, 3));
  return { faces, edges };
}

/** A cell's outline, a little inside it so neighbours' outlines do not overlap. */
function outlineGeometry(): BufferGeometry {
  const lo = 0.06;
  const hi = 0.94;
  const c = (i: number) => [i & 1 ? hi : lo, i & 2 ? hi : lo, i & 4 ? hi : lo];
  const g = new BufferGeometry();
  g.setAttribute("position", new Float32BufferAttribute(EDGES.flatMap(([i, j]) => [...c(i), ...c(j)]), 3));
  return g;
}

interface CellObject {
  objects: Object3D[];
  part: number;
}

export class MultiblockScene {
  private readonly renderer: WebGLRenderer;
  private readonly scene = new Scene();
  private readonly camera = new PerspectiveCamera(32, 1, 0.05, 600);
  private readonly controls: OrbitControls;
  private readonly cells: CellObject[] = [];
  private readonly solids: Mesh[] = [];
  private readonly materials: MeshLambertMaterial[] = [];
  private readonly outlineMaterials: LineBasicMaterial[] = [];
  private readonly edgeMaterial = new LineBasicMaterial({ color: 0x000000, transparent: true, opacity: 0.3 });
  private readonly grid: LineSegments;
  private readonly target: Vector3;
  private readonly radius: number;
  private shown: boolean[] = [];
  private ghosts = true;
  private edgesOn = true;
  private highlighted: number | null = null;
  private dirty = true;
  private frame = 0;
  private readonly resizeObserver: ResizeObserver;
  private readonly ray = new Raycaster();
  private readonly ndc = new Vector2();
  private readonly canvas: HTMLCanvasElement;
  private readonly view: MultiblockView;

  /** Throws when WebGL cannot start. */
  constructor(canvas: HTMLCanvasElement, view: MultiblockView, colours: SceneColours) {
    this.canvas = canvas;
    this.view = view;
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

    const outline = outlineGeometry();
    const geometries = view.parts.map((p) => (p.kind === "block" ? partGeometry(view.boxes[p.index]!) : null));
    for (const p of view.parts) {
      this.materials.push(new MeshLambertMaterial({ color: p.colour, side: DoubleSide }));
      this.outlineMaterials.push(new LineBasicMaterial({ color: p.colour, transparent: true, opacity: 0.8 }));
    }
    for (const c of view.cells) {
      const geo = geometries[c.part];
      const objects: Object3D[] = [];
      if (geo) {
        const mesh = new Mesh(geo.faces, this.materials[c.part]);
        mesh.userData.part = c.part;
        mesh.userData.pos = c.pos;
        const edges = new LineSegments(geo.edges, this.edgeMaterial);
        objects.push(mesh, edges);
        this.solids.push(mesh);
      } else {
        objects.push(new LineSegments(outline, this.outlineMaterials[c.part]));
      }
      for (const o of objects) {
        o.position.set(c.pos[0], c.pos[1], c.pos[2]);
        this.scene.add(o);
      }
      this.cells.push({ objects, part: c.part });
    }

    // A floor grid under the lowest layer, a block wider than the footprint.
    const { min, max } = view;
    const pts: number[] = [];
    const y = min[1];
    for (let x = min[0] - 1; x <= max[0] + 2; x++) pts.push(x, y, min[2] - 1, x, y, max[2] + 2);
    for (let z = min[2] - 1; z <= max[2] + 2; z++) pts.push(min[0] - 1, y, z, max[0] + 2, y, z);
    const g = new BufferGeometry();
    g.setAttribute("position", new Float32BufferAttribute(pts, 3));
    this.grid = new LineSegments(g, new LineBasicMaterial({ color: 0x888888, transparent: true, opacity: 0.45 }));
    this.scene.add(this.grid);

    this.target = new Vector3((min[0] + max[0] + 1) / 2, (min[1] + max[1] + 1) / 2, (min[2] + max[2] + 1) / 2);
    this.radius = Math.max(1.5, Math.hypot(max[0] - min[0] + 1, max[1] - min[1] + 1, max[2] - min[2] + 1) / 2 + 0.5);

    this.shown = view.cells.map(() => true);
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
      }
    };
    this.frame = requestAnimationFrame(loop);
  }

  /** Which cells show (multiblock-view.ts's shownCells). */
  setShown(shown: readonly boolean[]) {
    this.shown = [...shown];
    this.applyVisibility();
  }

  /** Whether the outlines of cells to leave empty, or that fill themselves, are drawn. */
  setGhosts(on: boolean) {
    this.ghosts = on;
    this.applyVisibility();
  }

  setEdges(on: boolean) {
    this.edgesOn = on;
    this.applyVisibility();
  }

  private applyVisibility() {
    this.cells.forEach((c, i) => {
      const shown = this.shown[i] ?? true;
      const ghost = this.view.parts[c.part]!.kind !== "block";
      // A highlighted outline part shows whatever the toggle says, so its legend entry finds it.
      const visible = shown && (!ghost || this.ghosts || this.highlighted === c.part);
      c.objects.forEach((o, k) => (o.visible = visible && (ghost || k === 0 || this.edgesOn)));
    });
    this.dirty = true;
  }

  /** Dims every part but `part`; null shows them all alike. */
  highlight(part: number | null) {
    this.highlighted = part;
    this.materials.forEach((m, i) => {
      const dim = part !== null && i !== part;
      m.transparent = dim;
      m.opacity = dim ? DIM : 1;
      m.depthWrite = !dim;
      m.needsUpdate = true;
    });
    this.outlineMaterials.forEach((m, i) => (m.opacity = part !== null && i !== part ? DIM : 0.8));
    this.edgeMaterial.opacity = part !== null ? 0.08 : 0.3;
    this.applyVisibility();
  }

  /** The part and cell of the solid block under a point of the canvas, or null. */
  pick(clientX: number, clientY: number): { part: number; pos: Vec3 } | null {
    const r = this.canvas.getBoundingClientRect();
    this.ndc.set(((clientX - r.left) / r.width) * 2 - 1, -((clientY - r.top) / r.height) * 2 + 1);
    this.ray.setFromCamera(this.ndc, this.camera);
    const hits = this.ray.intersectObjects(this.solids.filter((m) => m.visible), false);
    // Through dimmed parts to the highlighted one, when one is.
    const hit = hits.find((h) => this.highlighted === null || h.object.userData.part === this.highlighted) ?? null;
    return hit ? { part: hit.object.userData.part as number, pos: hit.object.userData.pos as Vec3 } : null;
  }

  setColours(c: SceneColours) {
    this.renderer.setClearColor(new Color(c.background));
    (this.grid.material as LineBasicMaterial).color.set(c.grid);
    this.edgeMaterial.color.set(c.edge);
    this.dirty = true;
  }

  setView(name: ViewName) {
    const [az, el] = VIEW_ANGLES[name];
    const a = (az * Math.PI) / 180;
    const e = (el * Math.PI) / 180;
    const dir = new Vector3(Math.sin(a) * Math.cos(e), Math.sin(e), Math.cos(a) * Math.cos(e));
    this.controls.target.copy(this.target);
    const vfov = (this.camera.fov * Math.PI) / 180;
    const hfov = 2 * Math.atan(Math.tan(vfov / 2) * this.camera.aspect);
    const distance = this.radius / Math.sin(Math.min(vfov, hfov) / 2);
    this.camera.position.copy(this.target).addScaledVector(dir, distance);
    this.camera.up.set(0, 1, 0);
    this.camera.lookAt(this.target);
    this.controls.update();
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

  dispose() {
    cancelAnimationFrame(this.frame);
    this.resizeObserver.disconnect();
    this.controls.dispose();
    const geometries = new Set<BufferGeometry>();
    const materials = new Set<{ dispose(): void }>();
    this.scene.traverse((o) => {
      const m = o as Mesh;
      if (m.geometry) geometries.add(m.geometry);
      const mat = m.material;
      if (Array.isArray(mat)) mat.forEach((x) => materials.add(x));
      else if (mat) materials.add(mat);
    });
    geometries.forEach((g) => g.dispose());
    materials.forEach((m) => m.dispose());
    this.renderer.dispose();
  }
}
