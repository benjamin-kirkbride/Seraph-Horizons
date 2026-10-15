"""The riddle generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

MIN_THICK = 0.75                             # no structural part thinner than this (the charge's layers are a heap, not parts)
MAX_ELEMENTS = 56                            # kept low: readable at block scale
CHARGE = r"^(oversize|heap|fines)_"          # the charge's and the fines' elements
THIN_OK = r"^fr_strap_"                      # iron straps and their nail heads are thin by nature


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.tag = ""
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.moving = {p["id"] for p in parts if p["drivers"] or p.get("ride")}
        self.cache = {}
        self.ok = True

    def say(self, msg):
        print(self.tag + msg)

    def fail(self, msg):
        self.ok = False
        print("FAIL " + self.tag + msg)

    def mat(self, pid, pose):
        return self.m.pm(self.parts, pid, pose)

    def posed(self, pid, pose):
        key = (pid, pose)
        if key not in self.cache:
            if len(self.cache) > 60000:
                self.cache.clear()
            mm = self.mat(pid, pose)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, pose, p):
        q = _apply(self.mat(pid, pose), [v / 16 for v in p])
        return [v * 16 for v in q]

    def named(self, pid, rx, pose):
        r = re.compile(rx)
        return [e for e in self.posed(pid, pose) if r.search(e.name)]

    def relative(self, pid, frame_pid, pose):
        """A part's elements posed in another (rigid) part's frame: as the rig poses them, the other part's motion undone."""
        f = self.mat(frame_pid, pose)
        rt = [[f[j][i] for j in range(3)] for i in range(3)]
        t = [-sum(rt[i][j] * f[j][3] for j in range(3)) for i in range(3)]
        inv = [rt[0] + [t[0]], rt[1] + [t[1]], rt[2] + [t[2]], [0.0, 0.0, 0.0, 1.0]]
        mm = self.mat(pid, pose)
        rel = [[sum(inv[i][k] * mm[k][j] for k in range(4)) for j in range(4)] for i in range(4)]
        return [posed(el, rel) for el in self.by_part.get(pid, [])]


def present(m, pid, k):
    return m.on_show(pid, k)


def cycle_poses(v, step=0.005):
    n = int(round(1 / step))
    return [v.m.pose_at(i * step) for i in range(n + 1)]


def grid_poses(v):
    """W over the cycle against the shake's phase (psi an eighth of a turn apart), and a few at half presence."""
    ws = (0.0, 0.03, 0.06, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.64, 0.7, 0.8, 0.82, 0.9, 1.0)
    psis = [i * math.pi / 8 for i in range(16)]
    return [(psi, w, 1, 1.0) for w in ws for psi in psis] + [(psi, 0.5, 1, 0.5) for psi in psis[::2]]


def check_basic(v):
    worst = euler_round_trip(v.els)
    v.say(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    names = [el.name for el in v.els]
    if len(names) != len(set(names)):
        v.fail("duplicate element names")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    v.say(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()))


def check_readable(v):
    """Readable at block scale: few elements, and no part of the riddle or the frame thinner than MIN_THICK (the
    charge's lumps and layers and the fines are a heap, drawn in the ore's texture, and the corner straps and their
    nails are iron, thin by nature: they are left out)."""
    structural = [el for el in v.els if not re.match(CHARGE, el.name) and not re.match(THIN_OK, el.name)]
    thin = [(el.name, round(min(abs(s) for s in el.size), 3)) for el in structural if min(abs(s) for s in el.size) < MIN_THICK - 1e-9]
    most = MAX_ELEMENTS
    v.say(f"readable: {len(v.els)} elements (at most {most}); thinnest structural part "
          f"{min(min(abs(s) for s in el.size) for el in structural):.2f} voxels")
    if len(v.els) > most:
        v.fail(f"{len(v.els)} elements: more than {most}")
    if thin:
        v.fail(f"parts thinner than {MIN_THICK}: {thin[:6]}")


def check_containment(v, poses):
    """Nothing leaves the cell over the cycle and the shake's phase."""
    m = v.m
    size = [n * 16.0 for n in m.CELLS]
    worst, where = -1e9, None
    for pose in poses:
        for pid in v.by_part:
            if not present(m, pid, pose[2]):
                continue
            for el in v.posed(pid, pose):
                lo, hi = el.aabb()
                o = max(-lo[0], -lo[1], -lo[2], hi[0] - size[0], hi[1] - size[1], hi[2] - size[2])
                if o > worst:
                    worst, where = o, (el.name, pose)
    v.say(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the cell at {where[1]}")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    v.say(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_box_(wall|bottom)", {"planks"}),
    (r"^fr_bearer", {"oak"}),
    (r"^fr_strap_", {"iron"}),
    (r"^riddle_(lower|upper)", {"oak"}),
    (r"^riddle_wire", {"iron"}),
    (r"^(oversize|heap|fines)_", {"ore"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    v.say(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


def check_mesh(v):
    """The mesh: the wires along z lie over those along x, crimped into them, every wire's ends let into the lower
    tier's boards, and the openings between them as wide as designed."""
    m = v.m
    rest = m.REST
    lower = [e.aabb() for e in v.named("riddle", r"_lower_", rest)]
    wires = v.named("riddle", r"_wire", rest)
    ok = True
    for w in wires:
        lo, hi = w.aabb()
        along = 0 if "wirex" in w.name else 2
        for end in (lo[along], hi[along]):
            inside = [b for b in lower if b[0][along] < end < b[1][along]
                      and all(b[0][q] <= lo[q] and hi[q] <= b[1][q] for q in (0, 2) if q != along)]
            if not inside:
                ok = False
                v.fail(f"{w.name}'s end at {end:.2f} is not let into the lower tier")
    xs = [e.aabb() for e in wires if "wirex" in e.name]
    zs = [e.aabb() for e in wires if "wirez" in e.name]
    crimp = min(a[1][1] - b[0][1] for a in xs for b in zs)
    if not (0 < crimp < m.WIRE) or min(b[0][1] for b in zs) <= min(a[0][1] for a in xs):
        ok = False
        v.fail("the wires along z do not lie over those along x, crimped into them")
    gaps = sorted(b[0][2] - a[1][2] for a, b in zip(sorted(xs), sorted(xs)[1:]))
    v.say(f"mesh: {len(xs)} wires along x under {len(zs)} along z, crimped {crimp:.2f}; openings {gaps[0]:.2f}; "
          f"every end in the lower tier: {'yes' if ok else 'NO'}")
    if abs(gaps[0] - (2.0 - m.WIRE)) > 1e-6:
        v.fail("the mesh's openings are not as designed")


def same(a, b, tol=1e-9):
    return max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) <= tol


def layer(els):
    """A stretched part's elements: (vanished, bottom, height)."""
    if all(e.vanished for e in els):
        return True, None, 0.0
    lo, hi = aabb_of([e for e in els if not e.vanished])
    return False, lo[1], hi[1] - lo[1]


def check_charge(v):
    """The charge through one riddling: as loaded at W 0 (the layer of fines on the mesh whole, nothing in the box);
    the lumps riding the riddle; the layer shrinking onto its own underside in the riddle's frame, never growing, and
    gone by the end of its window; the fines in the box growing from the box's bottom, never shrinking, from nothing
    at the start of their window to whole at its end; at W 1 the riddle at rest with the lumps, the oversize, on the
    mesh, each standing on a wire along z, and the fines whole in the box. The lumps stand out of the layer from the
    start and inside the riddle's top tier. With no charge on, the fines are drawn whole: the box keeps them."""
    m = v.m
    a, b = m.T["fines"]
    ws = [i * 0.005 for i in range(201)]
    start, done = m.pose_at(0.0), m.pose_at(1.0)
    for pid in ("oversize", "heap"):
        if any(abs(x - y) > 1e-9 for ea, eb in zip(v.posed(pid, start), v.by_part[pid]) for x, y in zip(ea.c + ea.size, eb.c + eb.size)):
            v.fail(f"{pid} is not as loaded at W 0")
    for w in ws[::4]:
        for psi in (0.0, 1.3, 2.9):
            if not same(v.mat("oversize", (psi, w, 1, 1.0)), v.mat("riddle", (psi, w, 1, 1.0))):
                v.fail(f"the oversize leaves the riddle at W {w:.2f}")
                break
    whole = v.by_part["heap"][0].size[1]
    fines_whole = aabb_of(v.by_part["fines"])
    fines_h = fines_whole[1][1] - fines_whole[0][1]
    last_heap, last_fines = None, None
    for w in ws:
        gone, bottom, height = layer(v.relative("heap", "riddle", m.pose_at(w)))
        if not gone and abs(bottom - (m.Y0 + m.HEAP_Y)) > 1e-6:
            v.fail(f"the heap leaves its underside at W {w:.3f} ({bottom:.4f})")
        if last_heap is not None and height > last_heap + 1e-9:
            v.fail(f"the heap grows at W {w:.3f}")
        if w <= a + 1e-9 and abs(height - whole) > 1e-6:
            v.fail(f"the heap is not whole before its window, at W {w:.3f}")
        if w >= b - 1e-9 and not gone:
            v.fail(f"the heap is not gone after its window, at W {w:.3f}")
        last_heap = height
        gone, bottom, height = layer(v.posed("fines", m.pose_at(w)))
        if not gone and abs(bottom - m.BOX_BOTTOM) > 1e-6:
            v.fail(f"the fines leave the box's bottom at W {w:.3f} ({bottom:.4f})")
        if last_fines is not None and height < last_fines - 1e-9:
            v.fail(f"the fines shrink at W {w:.3f}")
        if w <= a + 1e-9 and not gone:
            v.fail(f"the fines are in the box before their window, at W {w:.3f}")
        if w >= b - 1e-9 and abs(height - fines_h) > 1e-6:
            v.fail(f"the fines are not whole after their window, at W {w:.3f}")
        last_fines = height
    # with no charge on, at any W and clock, the box's fines are whole and where they were built
    for pose in ((0.0, 0.0, 0, 0.0), (2.3, 0.5, 0, 0.0), (0.0, 1.0, 0, 1.0)):
        got = aabb_of(v.posed("fines", pose))
        if any(abs(got[i][j] - fines_whole[i][j]) > 1e-9 for i in range(2) for j in range(3)):
            v.fail(f"the fines are not whole in the box with no charge on, at {pose}")
    # the fines lie in the box, inside its walls, under the riddle's lowest point
    walls = [e.aabb() for e in v.named("frame", r"box_wall", m.REST)]
    inner = (max(w[1][0] for w in walls if w[1][0] < m.CX), min(w[0][0] for w in walls if w[0][0] > m.CX))
    if not (inner[0] < fines_whole[0][0] and fines_whole[1][0] < inner[1] and fines_whole[1][1] < m.BOX_H - 1.0):
        v.fail("the fines do not lie in the box, inside its walls and under its rim")
    # delivered: the riddle at rest, the lumps on the mesh, each on a wire along z
    if not same(v.mat("riddle", done), [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]]):
        v.fail("the riddle is not at rest at W 1")
    wires = [e.aabb() for e in v.named("riddle", r"_wirez_", done)]
    mesh_top = m.Y0 + m.MESH_TOP
    heap_top = mesh_top + m.HEAP[1]
    lumps = v.posed("oversize", done)
    if len(lumps) != 3:
        v.fail(f"a charge is three lumps (#714), not {len(lumps)}")
    for lump in lumps:
        lo, hi = lump.aabb()
        on_wire = any(w0[0] <= lump.c[0] <= w1[0] for w0, w1 in wires)
        inside = max(abs(lo[0] - m.CX), abs(hi[0] - m.CX), abs(lo[2] - m.CZ), abs(hi[2] - m.CZ)) <= m.UPPER_HALF - m.BOARD - 0.2
        if abs(lo[1] - mesh_top) > 1e-6 or not on_wire or not inside or hi[1] - heap_top < 0.25:
            v.fail(f"{lump.name} does not stand on a wire of the mesh, inside the riddle and out of the heap")
    for i, x in enumerate(lumps):
        for y in lumps[i + 1:]:
            if obb_obb(x, y):
                v.fail(f"{x.name} and {y.name} overlap")
    v.say(f"charge: {len(lumps)} lumps, as loaded at W 0; the lumps ride the riddle; the layer of fines shrinks onto its "
          f"underside as the fines grow in the box from its bottom ({fines_h:.2f} deep at the end); at W 1 the riddle at "
          "rest, the lumps on the mesh's wires; with no charge on the fines lie whole in the box")


def riddle_centre(v, pose):
    m = v.m
    return v.point("riddle", pose, [m.CX, m.Y0 + m.RIM_H / 2, m.CZ])


def check_clock(v):
    """The clock's travel moves nothing outside the shaking window, nor with no charge."""
    m = v.m
    psis = [i * math.pi / 8 for i in range(16)]
    outside = [(w, 1) for w in (0.0, 0.02, m.T["shake"][3] + 0.01, 0.9, 1.0)] + [(0.5, 0)]
    for w, k in outside:
        ref = {p["id"]: v.mat(p["id"], (0.0, w, k, 1.0 if k else 0.0)) for p in v.parts}
        for psi in psis:
            for p in v.parts:
                if not same(v.mat(p["id"], (psi, w, k, 1.0 if k else 0.0)), ref[p["id"]], 1e-12):
                    v.fail(f"the clock moves {p['id']} outside the shaking window (W {w}, k {k})")
                    return
    v.say("clock: moves nothing outside the shaking window or with no charge")


def check_shake(v):
    """Inside the window the riddle goes to and fro and side to side as far as designed."""
    m = v.m
    psis = [i * math.pi / 8 for i in range(16)]
    rest = riddle_centre(v, m.REST)
    w = (m.T["shake"][1] + m.T["shake"][2]) / 2
    dz = [riddle_centre(v, (psi, w, 1, 1.0))[2] - rest[2] for psi in psis]
    dx = [riddle_centre(v, (psi, w, 1, 1.0))[0] - rest[0] for psi in psis]
    got_z, got_x = max(abs(d) for d in dz), max(abs(d) for d in dx)
    v.say(f"shake: the riddle goes {got_z:.3f} to and fro (want {m.SHAKE_Z}), {got_x:.3f} side to side (want {m.SHAKE_X})")
    if abs(got_z - m.SHAKE_Z) > 0.05 or abs(got_x - m.SHAKE_X) > 0.02:
        v.fail("the shake is not as designed")


def check_bearers(v):
    """The riddle rests on the bearers, each under a whole board (north and south), and is lifted clear of them while
    it is shaken; the bearers lie on the box's walls."""
    m = v.m
    low = aabb_of(v.posed("riddle", m.REST))[0][1]
    if abs(low - m.Y0) > 1e-6:
        v.fail(f"the riddle does not rest on the bearers ({low:.3f})")
    bearers = [e.aabb() for e in v.named("frame", r"bearer", m.REST)]
    boards = {s: v.named("riddle", rf"_lower_{s}$", m.REST)[0].aabb() for s in ("n", "s")}
    for (blo, bhi), s in zip(sorted(bearers, key=lambda b: b[0][2]), ("n", "s")):
        lo, hi = boards[s]
        if not (blo[2] >= lo[2] - 1e-6 and bhi[2] <= hi[2] + 1e-6 and blo[0] < lo[0] and hi[0] < bhi[0]
                and abs(bhi[1] - lo[1]) < 1e-6 and abs(blo[1] - m.BOX_H) < 1e-6):
            v.fail(f"the {s} bearer does not lie on the walls under the riddle's {s} board")
    worst = 1e9
    for pose in grid_poses(v):
        if not (m.T["shake"][1] <= pose[1] <= m.T["shake"][2]):
            continue
        for e in v.posed("riddle", pose):
            lo, hi = e.aabb()
            for blo, bhi in bearers:
                if lo[0] < bhi[0] and blo[0] < hi[0] and lo[2] < bhi[2] and blo[2] < hi[2]:
                    worst = min(worst, lo[1] - bhi[1])
    v.say(f"bearers: on the walls, each under a whole board; the riddle rests on both; shaken, it clears them by at least {worst:.3f}")
    if worst < 0.05:
        v.fail("the shaken riddle rubs on the bearers")


def box_gap(a, b):
    """The gap between two boxes (negative: they overlap by that much along the axis that separates them least)."""
    return max(max(a[0][i] - b[1][i], b[0][i] - a[1][i]) for i in range(3))


STRAP_CLEAR = 0.25                           # the corner irons keep this far from anything that moves, and from the bearers


def check_corner_straps(v):
    """Every vertical corner of the box (found from its walls) is bound by an angle-iron strap: a leg flat on each of
    the corner's two outer faces, the two meeting along the corner in an L that wraps it; each leg reaching past the
    end of the wall it laps onto the other wall's face (so it binds both), running most of the box's height, and
    nailed with two heads or more on its outer face; the north and south legs' nails over the end walls, the west and
    east legs' over the side walls, so both walls of the corner are nailed. The irons stop under the rim, stay clear
    of the bearers, and keep STRAP_CLEAR from the riddle and its charge over the whole swept path."""
    m = v.m
    rest = m.REST
    walls = {e.name[-1]: e.aabb() for e in v.named("frame", r"^fr_box_wall_[nswe]$", rest)}
    lo = [min(b[0][i] for b in walls.values()) for i in range(3)]
    hi = [max(b[1][i] for b in walls.values()) for i in range(3)]
    straps = {e.name: e.aabb() for e in v.named("frame", r"^fr_strap_", rest)}
    eps = 1e-6
    found = []
    for xc, sx in ((lo[0], -1), (hi[0], 1)):
        for zc, sz in ((lo[2], -1), (hi[2], 1)):
            where = f"({xc:g}, {zc:g})"
            # the leg on the face across x (north or south: its inner face on the box's z face) and across z
            lx = [(n, b) for n, b in straps.items() if "nail" not in n and abs((b[0][2] if sz > 0 else b[1][2]) - zc) < eps
                  and min(abs(b[0][0] - xc), abs(b[1][0] - xc)) < 0.5]
            lz = [(n, b) for n, b in straps.items() if "nail" not in n and abs((b[0][0] if sx > 0 else b[1][0]) - xc) < eps
                  and min(abs(b[0][2] - zc), abs(b[1][2] - zc)) < 0.5]
            if len(lx) != 1 or len(lz) != 1:
                v.fail(f"the box's corner {where} has no angle-iron strap (legs {[n for n, _ in lx + lz]})")
                continue
            (nx, bx), (nz, bz) = lx[0], lz[0]
            # an L: the two legs share the corner's edge (the x leg laps the z leg's edge) and wrap it
            outer_x = bx[1][0] if sx > 0 else bx[0][0]
            outer_z = bz[1][2] if sz > 0 else bz[0][2]
            t_z = bz[1][0] - bz[0][0]
            if abs(box_gap(bx, bz)) > eps or abs(abs(outer_x - xc) - t_z) > eps or abs(outer_z - zc) > eps:
                v.fail(f"the strap's legs at {where} do not meet along the corner in an L ({nx}, {nz})")
            # each leg reaches past the wall it laps: the x leg past the side wall's thickness, the z leg past the end
            # wall's, onto the other wall
            reach_x = abs((bx[0][0] if sx > 0 else bx[1][0]) - xc)
            reach_z = abs((bz[0][2] if sz > 0 else bz[1][2]) - zc)
            if reach_x <= m.WALL + 0.25 or reach_z <= m.WALL + 0.25:
                v.fail(f"the strap at {where} does not reach past the end of each wall ({reach_x:.2f}, {reach_z:.2f})")
            for n, b in ((nx, bx), (nz, bz)):
                if b[1][1] - b[0][1] < 0.75 * (hi[1] - lo[1]) or b[1][1] > m.BOX_H - 0.1 or b[0][1] < -eps:
                    v.fail(f"{n} does not run most of the box's height under its rim")
                nails = [k for k, nb in straps.items() if "nail" in k and abs(box_gap(nb, b)) < eps]
                if len(nails) < 2:
                    v.fail(f"{n} is not nailed (nails on it: {nails})")
                # the z leg's nails past the end wall, over the side wall; the x leg's over the end wall
                for k in nails:
                    nb = straps[k]
                    if n == nz:
                        side = "w" if sx < 0 else "e"
                        ok = walls[side][0][2] < nb[0][2] and nb[1][2] < walls[side][1][2]
                    else:
                        end = "n" if sz < 0 else "s"
                        ok = walls[end][0][0] < nb[0][0] and nb[1][0] < walls[end][1][0]
                    if not ok:
                        v.fail(f"{k} is not over the wall its leg binds")
            found.append(f"{where} {nx[9:11]}")
    bearers = [e.aabb() for e in v.named("frame", r"bearer", rest)]
    to_bearers = min(box_gap(s, b) for s in straps.values() for b in bearers)
    # the swept path: the riddle and its charge over the grid of poses and the cycle at the pace
    near = 1e9
    strap_boxes = list(straps.values())
    for pose in grid_poses(v) + cycle_poses(v, 0.01):
        for pid in ("riddle", "oversize", "heap"):
            for e in v.posed(pid, pose):
                if e.vanished:
                    continue
                eb = e.aabb()
                for s in strap_boxes:
                    near = min(near, box_gap(eb, s))
    v.say(f"corner straps: {len(found)} corners bound, each an L of two nailed legs binding both walls; "
          f"{to_bearers:.2f} from the bearers, {near:.2f} from the riddle's swept path at the nearest")
    if len(found) != 4:
        v.fail("not every corner of the box is strapped")
    if to_bearers < STRAP_CLEAR - eps or near < STRAP_CLEAR - eps:
        v.fail("a corner strap comes near the bearers or the moving riddle")


ALLOWED = [
    # (part, element regex, part, element regex): intended contacts
    ("riddle", r"_lower_", "frame", r"fr_bearer"),              # the riddle at rest on the bearers
    ("fines", None, "frame", r"box_bottom"),                    # the fines on the box's bottom
    ("oversize", None, "riddle", r"_wire"),                     # the lumps on the mesh
    ("heap", None, "riddle", r"_wire"),                         # the layer of fines settled into the mesh
    ("heap", None, "oversize", None),                           # the lumps standing in it
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def touching_pairs(v, pose, statics):
    k = pose[2]
    items = []
    for pid in v.by_part:
        if present(v.m, pid, k):
            for el in v.posed(pid, pose):
                if not el.vanished:
                    items.append((pid, el, el.aabb()))
    hits = {}
    for i in range(len(items)):
        pa, ea, (alo, ahi) = items[i]
        for j in range(i + 1, len(items)):
            pb, eb, (blo, bhi) = items[j]
            if pa == pb or (not statics and pa not in v.moving and pb not in v.moving):
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, ea.name, pb, eb.name):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def check_clearances(v, poses, label):
    allhits = {}
    for n, pose in enumerate(poses):
        for key, hs in touching_pairs(v, pose, statics=(n == 0)).items():
            allhits.setdefault(key, {})
            for h in hs:
                allhits[key].setdefault(h, pose)
    v.say(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        v.say(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at psi {p[0]:.2f} W {p[1]:.3f} k {p[2]}" for (a, b), p in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    poses = m.coplanar_poses()
    for pose in poses:
        k = pose[2]
        els = [e for pid in v.by_part for e in v.posed(pid, pose)]
        pairs = coplanar_faces([e for e in m.shown(els, pose) if e.c[1] > -500 and present(m, e.part, k)])
        if pairs:
            bad += len(pairs)
            v.say(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    v.say(f"z-fighting: {bad} coplanar overlapping face pairs over {len(poses)} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_readable(v)
    check_floating(v)
    check_textures(v)
    check_mesh(v)
    check_containment(v, [m.REST] + grid_poses(v) + cycle_poses(v, 0.005))
    check_charge(v)
    check_clock(v)
    check_shake(v)
    check_bearers(v)
    check_corner_straps(v)
    check_clearances(v, [m.REST] + grid_poses(v), "clearances")
    if not quick:
        check_clearances(v, [m.REST] + cycle_poses(v, 0.0025), "swept paths (every 0.0025 of the cycle at the pace)")
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """Every texture declared; the one cell, boxed inside itself and lidded; no power cell; no automated input or output
    (no infeed or output side, face, cell or point), only the operator's side; the contents and the pace as #714 has
    them."""
    ok = True
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    cells = sorted(tuple(c["pos"]) for c in ship["cells"])
    want = sorted(tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.footprint())
    boxes = [b for c in ship["cells"] for b in c.get("boxes", [])]
    inside_cell = all(0.0 <= b[i] <= 1.0 and b[i] < b[i + 3] <= 1.0 for b in boxes for i in range(3))
    lids = all(0.0 < c.get("lid", 0.0) <= 1.0 for c in ship["cells"])
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; cells {cells}, {hollow} hollow; "
          f"{len(boxes)} boxes, all inside their cells: {'yes' if inside_cell else 'NO'}; lid: "
          f"{'yes' if not gaps and lids else gaps}")
    if gaps or hollow or cells != want or not inside_cell or not lids:
        print("FAIL the cell is not the footprint's, boxed inside it and lidded")
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print("FAIL a hand station has no power cell")
        ok = False
    # no automated input or output: no side, face, cell or point for a chute or a hopper, only the operator's side
    transfer = sorted(k for k in ship if k in m.NO_TRANSFER or (k.endswith(("Side", "Face", "Cell")) and k != "operatorSide"))
    print(f"no automated input or output: sides {[k for k in ship if k.endswith('Side')]}; transfer keys {transfer or 'none'}")
    if transfer or ship.get("operatorSide") != "north":
        print(f"FAIL a hand station takes no automated input or output: {transfer}")
        ok = False
    # what it holds: each contents value a requires of the rig's parts; one charge, three lumps in twelve shakes (#714)
    reqs = {p["requires"] for p in ship["parts"]}
    contents = {k: val for k, val in ship.get("contents", {}).items() if not k.startswith("_")}
    pace = ship["riddling"]
    print(f"contents {contents}; a charge {pace['lumpsPerCharge']} lumps in {pace['shakesPerCharge']} shakes; "
          f"work ends {ship['work']['end']}")
    if set(contents) != {"charge", "fines"} or not set(contents.values()) <= reqs:
        print("FAIL the contents are not the charge and the fines, each a requires of the rig")
        ok = False
    if pace["lumpsPerCharge"] != 3 or pace["shakesPerCharge"] != {"thin": 12.0} or ship["work"]["end"] != {"thin": 1.0}:
        print("FAIL a charge is not three lumps riddled in twelve shakes (#714)")
        ok = False
    return ok
