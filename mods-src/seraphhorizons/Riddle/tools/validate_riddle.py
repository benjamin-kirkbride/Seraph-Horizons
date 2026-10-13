"""The riddle generator's validation: make_shape.py calls `validate` (per form), `check_same_riddle` and
`validate_files` (per form).

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
MAX_ELEMENTS = {"hand": 40, "stand": 64}     # kept low: readable at block scale
CHARGE = r"^c[12]"                           # the charge's parts and elements


class V:
    def __init__(self, m, form, els, parts, rig):
        self.m, self.form, self.els, self.parts, self.rig = m, form, els, parts, rig
        self.tag = f"[{form}] "
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
    return [v.m.pose_at(k, i * step) for k in (1, 2) for i in range(n + 1)]


def grid_poses(v):
    """W over the cycle against the shake's phase (psi a quarter turn apart and between), both classes."""
    ws = (0.0, 0.03, 0.06, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.64, 0.7, 0.8, 0.82, 0.9, 1.0)
    psis = [i * math.pi / 4 for i in range(8)]
    return ([(psi, w, k, 1.0) for k in (1, 2) for w in ws for psi in psis]
            + [(psi, 0.5, k, 0.5) for k in (1, 2) for psi in psis[::2]])


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
    """Readable at block scale: few elements, and no part of the riddle, the stand or the frame thinner than MIN_THICK
    (the charge's lumps and layers are a heap, drawn in the ore's texture, and are left out)."""
    structural = [el for el in v.els if not re.match(CHARGE, el.name)]
    thin = [(el.name, round(min(abs(s) for s in el.size), 3)) for el in structural if min(abs(s) for s in el.size) < MIN_THICK - 1e-9]
    most = MAX_ELEMENTS[v.form]
    v.say(f"readable: {len(v.els)} elements (at most {most}); thinnest structural part "
          f"{min(min(abs(s) for s in el.size) for el in structural):.2f} voxels")
    if len(v.els) > most:
        v.fail(f"{len(v.els)} elements: more than {most}")
    if thin:
        v.fail(f"parts thinner than {MIN_THICK}: {thin[:6]}")


def check_containment(v, poses):
    """Nothing leaves the cell over the cycle of both charges and the shake's phase."""
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
    (r"^fr_(bearer|sill|post|head|brace|tie)", {"oak"}),
    (r"^riddle_(lower|upper)", {"oak"}),
    (r"^riddle_wire", {"iron"}),
    (r"^riddle_grip_bar", {"oak"}),
    (r"^riddle_(grip_post|pin)", {"iron"}),
    (r"^hanger_", {"iron"}),
    (r"^c[12](over|heap|top|fines)", {"ore"}),
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


HEAPS = {1: (("c1heap", "HEAP_Y", "top1"),), 2: (("c2top", "LOW_TOP", "top"), ("c2heap", "HEAP_Y", "mid"))}
FINES = {1: (("c1fines", "BOX_BOTTOM", "top1"),), 2: (("c2fines1", "BOX_BOTTOM", "top"), ("c2fines2", "F2_FLOOR", "mid"))}


def layer(els):
    """One layer's element: (vanished, bottom, height)."""
    e = els[0]
    if e.vanished:
        return True, None, 0.0
    lo, hi = e.aabb()
    return False, lo[1], hi[1] - lo[1]


def check_charge(v):
    """The charge through one riddling, each class: as loaded at W 0 (the heap whole, no fines in the box); the lumps
    riding the riddle; every layer of the heap shrinking onto its own underside in the riddle's frame, never growing,
    and gone by the end of its window (a full charge's top before its lower layer starts); every layer of fines in the
    box growing from its underside, never shrinking, from nothing at the start of its window to whole at its end; at
    W 1 the riddle at rest with the lumps, the oversize, on the mesh, each standing on a wire along z, and the fines in
    the box. The lumps stand out of the heap from the start and inside the riddle's top tier."""
    m = v.m
    floors = {"HEAP_Y": m.Y0 + m.HEAP_Y, "LOW_TOP": m.Y0 + m.LOW_TOP, "BOX_BOTTOM": m.BOX_BOTTOM,
              "F2_FLOOR": m.BOX_BOTTOM + m.F1[1]}
    ws = [i * 0.005 for i in range(201)]
    for k, pre in ((1, "c1"), (2, "c2")):
        start, done = m.pose_at(k, 0.0), m.pose_at(k, 1.0)
        for pid in v.by_part:
            if pid.startswith(pre) and not pid.endswith("fines") and not re.match(r"c2fines\d", pid):
                if any(abs(a - b) > 1e-9 for ea, eb in zip(v.posed(pid, start), v.by_part[pid]) for a, b in zip(ea.c + ea.size, eb.c + eb.size)):
                    v.fail(f"{pid} is not as loaded at W 0")
        for w in ws[::4]:
            for psi in (0.0, 1.3, 2.9):
                if not same(v.mat(f"{pre}over", (psi, w, k, 1.0)), v.mat("riddle", (psi, w, k, 1.0))):
                    v.fail(f"the oversize ({pre}) leaves the riddle at W {w:.2f}")
                    break
        # the heap, in the riddle's frame
        for pid, floor, win in HEAPS[k]:
            a, b = m.T[win]
            want = floors[floor]
            last = None
            for w in ws:
                gone, bottom, height = layer(v.relative(pid, "riddle", m.pose_at(k, w)))
                if not gone and abs(bottom - want) > 1e-6:
                    v.fail(f"{pid} leaves its underside at W {w:.3f} ({bottom:.4f}, want {want:.4f})")
                if last is not None and height > last + 1e-9:
                    v.fail(f"{pid} grows at W {w:.3f}")
                if w <= a + 1e-9 and abs(height - v.by_part[pid][0].size[1]) > 1e-6:
                    v.fail(f"{pid} is not whole before its window, at W {w:.3f}")
                if w >= b - 1e-9 and not gone:
                    v.fail(f"{pid} is not gone after its window, at W {w:.3f}")
                last = height
        if k == 2 and m.T["top"][1] > m.T["mid"][0]:
            v.fail("a full charge's lower layer starts to shrink before its top is gone")
        # the fines in the box
        for pid, floor, win in FINES[k]:
            a, b = m.T[win]
            want = floors[floor]
            last = None
            for w in ws:
                gone, bottom, height = layer(v.posed(pid, m.pose_at(k, w)))
                if not gone and abs(bottom - want) > 1e-6:
                    v.fail(f"{pid} leaves its underside at W {w:.3f} ({bottom:.4f}, want {want:.4f})")
                if last is not None and height < last - 1e-9:
                    v.fail(f"{pid} shrinks at W {w:.3f}")
                if w <= a + 1e-9 and not gone:
                    v.fail(f"{pid} is in the box before its window, at W {w:.3f}")
                if w >= b - 1e-9 and abs(height - v.by_part[pid][0].size[1]) > 1e-6:
                    v.fail(f"{pid} is not whole after its window, at W {w:.3f}")
                last = height
        # delivered: the riddle at rest, the lumps on the mesh, each on a wire along z
        if not same(v.mat("riddle", done), [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]]):
            v.fail(f"the riddle is not at rest at W 1 ({pre})")
        wires = [e.aabb() for e in v.named("riddle", r"_wirez_", done)]
        mesh_top = m.Y0 + m.MESH_TOP
        heap_top = mesh_top + (m.PART[1] if k == 1 else m.LOW[1])
        lumps = v.posed(f"{pre}over", done)
        for lump in lumps:
            lo, hi = lump.aabb()
            on_wire = any(w0[0] <= lump.c[0] <= w1[0] for w0, w1 in wires)
            inside = max(abs(lo[0] - m.CX), abs(hi[0] - m.CX), abs(lo[2] - m.CZ), abs(hi[2] - m.CZ)) <= m.UPPER_HALF - m.BOARD - 0.2
            if abs(lo[1] - mesh_top) > 1e-6 or not on_wire or not inside or hi[1] - heap_top < 0.25:
                v.fail(f"{lump.name} does not stand on a wire of the mesh, inside the riddle and out of the heap")
        for i, a in enumerate(lumps):
            for b in lumps[i + 1:]:
                if obb_obb(a, b):
                    v.fail(f"{a.name} and {b.name} overlap")
    v.say("charge: as loaded at W 0; the lumps ride the riddle; each layer of the heap shrinks onto its underside, and each "
          "layer of fines grows in the box from its underside, in turn; at W 1 the riddle at rest, the lumps on the mesh's "
          "wires and the fines in the box")


def riddle_centre(v, pose):
    m = v.m
    return v.point("riddle", pose, [m.CX, m.Y0 + m.RIM_H / 2, m.CZ])


def check_clock(v):
    """The clock's travel moves nothing outside the shaking window, nor with no charge."""
    m = v.m
    psis = [i * math.pi / 8 for i in range(16)]
    outside = [(w, k) for k in (1, 2) for w in (0.0, 0.02, m.T["shake"][3] + 0.01, 0.9, 1.0)] + [(0.5, 0)]
    for w, k in outside:
        ref = {p["id"]: v.mat(p["id"], (0.0, w, k, 1.0 if k else 0.0)) for p in v.parts}
        for psi in psis:
            for p in v.parts:
                if not same(v.mat(p["id"], (psi, w, k, 1.0 if k else 0.0)), ref[p["id"]], 1e-12):
                    v.fail(f"the clock moves {p['id']} outside the shaking window (W {w}, k {k})")
                    return
    v.say("clock: moves nothing outside the shaking window or with no charge")


def check_shake(v):
    """The hand riddle: inside the window the riddle goes to and fro and side to side as far as designed."""
    m = v.m
    psis = [i * math.pi / 8 for i in range(16)]
    rest = riddle_centre(v, m.REST)
    w = (m.T["shake"][1] + m.T["shake"][2]) / 2
    dz = [riddle_centre(v, (psi, w, 2, 1.0))[2] - rest[2] for psi in psis]
    dx = [riddle_centre(v, (psi, w, 2, 1.0))[0] - rest[0] for psi in psis]
    got_z, got_x = max(abs(d) for d in dz), max(abs(d) for d in dx)
    v.say(f"shake: the riddle goes {got_z:.3f} to and fro (want {m.SHAKE_Z}), {got_x:.3f} side to side (want {m.SHAKE_X})")
    if abs(got_z - m.SHAKE_Z) > 0.05 or abs(got_x - m.SHAKE_X) > 0.02:
        v.fail("the shake is not as designed")


def check_bearers(v):
    """The hand riddle rests on the bearers, each under a whole board (north and south), and is lifted clear of them
    while it is shaken; the bearers lie on the box's walls."""
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


def pin_points(m):
    """Each strap's two pins (voxels, at rest): {part: [(top pivot, its pin into the riddle), ...]}."""
    out = {}
    for part, z in (("hangernorth", m.HANG_Z[0]), ("hangersouth", m.HANG_Z[1])):
        out[part] = [([x, m.PIVOT_Y, z], [x, m.PIN_Y, z]) for x in ((m.HANGER_X[0] + m.HANGER_X[1]) / 2,
                                                                 16.0 - (m.HANGER_X[0] + m.HANGER_X[1]) / 2)]
    return out


def check_swing(v):
    """The riddle on its stand: inside the window the riddle stays level (no turn at all) and goes to and fro as far as
    designed, never sideways, rising a little at the ends of its swing as it would on straps; every strap's top pin stays
    put in its head and its bottom pin moves with the riddle's (the joints stay pinned), at every pose."""
    m = v.m
    a = m.swing_amplitude()
    rise = (m.PIVOT_Y - m.PIN_Y) * (1 - math.cos(a))
    rest = riddle_centre(v, m.REST)
    worst_pin, worst_turn, dzs, dxs, dys = 0.0, 0.0, [], [], []
    pins = pin_points(m)
    for pose in grid_poses(v) + cycle_poses(v, 0.01):
        r = v.mat("riddle", pose)
        worst_turn = max(worst_turn, max(abs(r[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(3)))
        c = riddle_centre(v, pose)
        dzs.append(c[2] - rest[2])
        dxs.append(c[0] - rest[0])
        dys.append(c[1] - rest[1])
        for part, joints in pins.items():
            for top, low in joints:
                t = v.point(part, pose, top)
                on_strap, on_riddle = v.point(part, pose, low), v.point("riddle", pose, low)
                worst_pin = max(worst_pin, max(abs(t[i] - top[i]) for i in range(3)),
                                max(abs(on_strap[i] - on_riddle[i]) for i in range(3)))
    got_z = max(abs(d) for d in dzs)
    v.say(f"swing: the riddle goes {got_z:.3f} to and fro (want {m.SWING}), {max(abs(d) for d in dxs):.1e} sideways, rising "
          f"{max(dys):.3f} (at most {rise:.3f}), turning {worst_turn:.1e}; the pins stay pinned within {worst_pin:.1e} voxels")
    if abs(got_z - m.SWING) > 0.03 or max(abs(d) for d in dxs) > 1e-6:
        v.fail("the swing is not as designed")
    if worst_turn > 1e-9 or min(dys) < -1e-9 or max(dys) > rise + 1e-6:
        v.fail("the riddle does not stay level, hung on its straps")
    if worst_pin > 1e-6:
        v.fail("a strap comes off its pins")


def check_stand(v):
    """The stand carries the riddle: each strap lies between its head's inner face and the riddle's side (nothing to
    rub), its top pin through its head and its bottom pin into the riddle's upper side board; the riddle hangs clear
    of the box's walls; each post stands on its sill under its head, and each brace runs from inside its post to inside
    its head."""
    m = v.m
    rest = m.REST
    heads = {e.name[-1]: e.aabb() for e in v.named("frame", r"^fr_head_", rest)}
    upper = {e.name[-1]: e.aabb() for e in v.named("riddle", r"_upper_[we]$", rest)}
    ok = True
    for part, joints in pin_points(m).items():
        for (top, low), side in zip(joints, ("w", "e")):
            strap = [e for e in v.posed(part, rest) if e.name.endswith(f"_{side}")][0]
            lo, hi = strap.aabb()
            hlo, hhi = heads[side]
            blo, bhi = upper[side]
            inner_head = hhi[0] if side == "w" else hlo[0]
            outer_riddle = blo[0] if side == "w" else bhi[0]
            between = abs((lo[0] if side == "w" else hi[0]) - inner_head) < 1e-6 and abs((hi[0] if side == "w" else lo[0]) - outer_riddle) < 1e-6
            in_head = hlo[1] < top[1] < hhi[1] and hlo[2] < top[2] < hhi[2]
            h = m.PIN / 2 - 1e-9
            in_board = blo[1] + h <= low[1] <= bhi[1] - h and blo[2] + h <= low[2] <= bhi[2] - h
            if not (between and in_head and in_board):
                ok = False
                v.fail(f"{strap.name}: between its head and the riddle {between}, its top pin in the head {in_head}, "
                       f"its bottom pin in the riddle's side board {in_board}")
    walls = max(e.aabb()[1][1] for e in v.named("frame", r"box_wall", rest))
    low = min(aabb_of(v.posed("riddle", pose))[0][1] for pose in grid_poses(v))
    for side in ("w", "e"):
        post = v.named("frame", rf"^fr_post_{side}$", rest)[0].aabb()
        sill = v.named("frame", rf"^fr_sill_{side}$", rest)[0].aabb()
        head = heads[side]
        if abs(post[0][1] - sill[1][1]) > 1e-6 or abs(post[1][1] - head[0][1]) > 1e-6:
            ok = False
            v.fail(f"the {side} post does not stand on its sill under its head")
        for brace in v.named("frame", rf"^fr_brace_{side}", rest):
            ends = [[brace.c[i] + s * brace.r[i][1] * brace.size[1] / 2 for i in range(3)] for s in (-1, 1)]
            low_end, high_end = sorted(ends, key=lambda p: p[1])
            in_post = post[0][1] < low_end[1] < post[1][1] and post[0][2] < low_end[2] < post[1][2]
            in_head = head[0][1] < high_end[1] < head[1][1] and head[0][2] < high_end[2] < head[1][2]
            if not (in_post and in_head):
                ok = False
                v.fail(f"{brace.name} does not run from inside its post to inside its head")
    v.say(f"stand: every strap between its head and the riddle, pinned through the head and into the riddle's side board: "
          f"{'yes' if ok else 'NO'}; the riddle hangs {low - walls:.3f} over the box's walls at its lowest")
    if low - walls < 0.5:
        v.fail("the hung riddle comes near the box's walls")


ALLOWED = {
    # (part, element regex, part, element regex): intended contacts
    "hand": [
        ("riddle", r"_lower_", "frame", r"fr_bearer"),          # the riddle at rest on the bearers
    ],
    "stand": [],
}
ALLOWED_CHARGE = [
    ("c[12]fines\\d?", None, "frame", r"box_bottom"),           # the fines on the box's bottom
    ("c[12]over", None, "riddle", r"_wire"),                    # the lumps on the mesh
    ("c[12](heap|top)", None, "riddle", r"_wire"),              # the heap settled into the mesh
    ("c[12](heap|top)", None, "c[12]over", None),               # the lumps standing in the heap
    ("c2top", None, "c2heap", None),                            # the heap's top on its lower layer
    ("c2fines2", None, "c2fines1", None),                       # the second layer of fines on the first
]


def allowed(form, pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED[form] + ALLOWED_CHARGE:
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
            if allowed(v.form, pa, ea.name, pb, eb.name):
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


def validate(m, form, els, parts, rig, quick=False):
    v = V(m, form, els, parts, rig)
    check_basic(v)
    check_readable(v)
    check_floating(v)
    check_textures(v)
    check_mesh(v)
    check_containment(v, [m.REST] + grid_poses(v) + cycle_poses(v, 0.005))
    check_charge(v)
    check_clock(v)
    if form == "hand":
        check_shake(v)
        check_bearers(v)
    else:
        check_swing(v)
        check_stand(v)
    check_clearances(v, [m.REST] + grid_poses(v), "clearances")
    if not quick:
        check_clearances(v, [m.REST] + cycle_poses(v, 0.0025), "swept paths (every 0.0025 of the cycle at the pace, both charges)")
        check_zfight(v)
    return v.ok


def check_same_riddle(hand, stand, tol=0.05):
    """The stand's riddle is the hand riddle's: every element of the hand riddle is in the stand's riddle part, the same
    box in the same place, up to the z-fighting fix's insets (the stand adds its grip and pins)."""
    ours = {e.name: e for e in stand if e.part == "riddle"}
    bad = []
    for e in (e for e in hand if e.part == "riddle"):
        s = ours.get(e.name)
        if s is None or max(abs(a - b) for a, b in zip(e.c + e.size, s.c + s.size)) > tol or e.r != s.r:
            bad.append(e.name)
    extra = sorted(n for n in ours if not any(e.name == n for e in hand))
    print(f"same riddle: the hand riddle's {sum(1 for e in hand if e.part == 'riddle')} elements in the stand's riddle "
          f"{'unchanged' if not bad else f'CHANGED {bad[:4]}'}; the stand adds {extra}")
    if bad or any(not re.match(r"riddle_(grip|pin)_", n) for n in extra):
        print("FAIL the stand's riddle is not the hand riddle's")
        return False
    return True


def validate_files(m, form, shape, frame_shape, ship):
    """Every texture declared; the one cell, boxed inside itself and lidded; no power cell; no automated input or output
    (no infeed or output side, face, cell or point), only the operator's side."""
    ok = True
    tag = f"[{form}] "
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        print(f"FAIL {tag}textures used but not declared: {missing}")
        ok = False
    gaps = lid_gaps(ship["cells"])
    hollow = sum(1 for c in ship["cells"] if c.get("hollow"))
    cells = sorted(tuple(c["pos"]) for c in ship["cells"])
    want = sorted(tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.footprint())
    boxes = [b for c in ship["cells"] for b in c.get("boxes", [])]
    inside_cell = all(0.0 <= b[i] <= 1.0 and b[i] < b[i + 3] <= 1.0 for b in boxes for i in range(3))
    lids = all(0.0 < c.get("lid", 0.0) <= 1.0 for c in ship["cells"])
    print(f"{tag}files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; cells {cells}, {hollow} hollow; "
          f"{len(boxes)} boxes, all inside their cells: {'yes' if inside_cell else 'NO'}; lid: "
          f"{'yes' if not gaps and lids else gaps}")
    if gaps or hollow or cells != want or not inside_cell or not lids:
        print(f"FAIL {tag}the cell is not the footprint's, boxed inside it and lidded")
        ok = False
    if "powerCell" in ship or "powerFace" in ship:
        print(f"FAIL {tag}a hand station has no power cell")
        ok = False
    # no automated input or output: no side, face, cell or point for a chute or a hopper, only the operator's side
    transfer = sorted(k for k in ship if k in m.NO_TRANSFER or (k.endswith(("Side", "Face", "Cell")) and k != "operatorSide"))
    print(f"{tag}no automated input or output: sides {[k for k in ship if k.endswith('Side')]}; transfer keys {transfer or 'none'}")
    if transfer or ship.get("operatorSide") != "north":
        print(f"FAIL {tag}a hand station takes no automated input or output: {transfer}")
        ok = False
    return ok
