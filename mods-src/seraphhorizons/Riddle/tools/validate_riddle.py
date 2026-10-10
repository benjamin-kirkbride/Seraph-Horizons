"""The riddle generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its
rule. Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in
make_shape.py proves the shipped files are this model moved.
"""

from __future__ import annotations

import math
import re

from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed

MIN_THICK = 0.75                             # no structural part thinner than this (the charge's layers are a heap, not parts)
MAX_ELEMENTS = 36                            # kept low: readable at block scale


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.moving = {p["id"] for p in parts if p["drivers"] or p.get("ride")}
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

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
        """A part's elements posed in another part's frame: as the rig poses them, the other part's motion undone."""
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
    print(f"euler round trip: worst {worst:.1e}")
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
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()))


def check_readable(v):
    """Readable at block scale: few elements, and no part of the riddle or the frame thinner than MIN_THICK
    (the charge's layers are steps of a heap, drawn in the ore's texture, and are left out)."""
    thin = [(el.name, round(min(abs(s) for s in el.size), 3)) for el in v.els
            if not re.match(r"^c[12]", el.name) and min(abs(s) for s in el.size) < MIN_THICK - 1e-9]
    print(f"readable: {len(v.els)} elements (at most {MAX_ELEMENTS}); thinnest structural part "
          f"{min(min(abs(s) for s in el.size) for el in v.els if not re.match(r'^c[12]', el.name)):.2f} voxels")
    if len(v.els) > MAX_ELEMENTS:
        v.fail(f"{len(v.els)} elements: more than {MAX_ELEMENTS}")
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
    print(f"containment over {len(poses)} poses: worst overhang {worst:.3f} voxels ({where[0]})")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the cell at {where[1]}")


def check_floating(v):
    seen, loose = frame_floating(v.by_part["frame"])
    print(f"frame: {len(seen)} elements joined to the ground, {len(loose)} not")
    if loose:
        v.fail(f"frame elements float: {loose[:8]}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^fr_box_(wall|bottom)", {"planks"}),
    (r"^fr_bearer", {"oak"}),
    (r"^riddle_(lower|upper)", {"oak"}),
    (r"^riddle_wire", {"iron"}),
    (r"^c[12](base|top|mid|fines)", {"ore"}),
]


def check_textures(v):
    bad = []
    for el in v.els:
        tex = {f["texture"].lstrip("#") for f in el.faces.values()}
        rule = next((want for rx, want in ROLES if re.match(rx, el.name)), None)
        if rule is None or not tex <= rule:
            bad.append((el.name, sorted(tex), sorted(rule or [])))
    print(f"textures by role: {len(v.els) - len(bad)} of {len(v.els)} elements as their role says")
    if bad:
        v.fail(f"textures off their role: {bad[:6]}")


def check_mesh(v):
    """The mesh: the wires along z lie over those along x, crimped into them, every wire's ends let into the
    lower tier's boards, and the openings between them as wide as designed."""
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
    print(f"mesh: {len(xs)} wires along x under {len(zs)} along z, crimped {crimp:.2f}; openings {gaps[0]:.2f}; "
          f"every end in the lower tier: {'yes' if ok else 'NO'}")
    if abs(gaps[0] - (2.0 - m.WIRE)) > 1e-6:
        v.fail("the mesh's openings are not as designed")


def inside(v, inner, outer, what):
    """Every element of `inner` lies inside the box `outer` (one element, axis-aligned in this frame) by HIDE_MARGIN."""
    lo, hi = aabb_of(outer)
    ilo, ihi = aabb_of(inner)
    margin = min(min(ilo[k] - lo[k], hi[k] - ihi[k]) for k in range(3))
    if margin < v.m.HIDE_MARGIN - 1e-6:
        v.fail(f"{what} is not hidden (margin {margin:.3f})")
    return margin


def check_charge(v):
    """At W 0 the heap lies as loaded and the fines are hidden in the box's bottom; the bed moves with the riddle
    the whole time; every layer of fines sinks inside the bed (a full charge's top first inside its lower
    layer) and the fines lie in the box at W 1, the first layer on the bottom and the second on it; the bed,
    the oversize, is left in the riddle and the riddle set down on the bearers."""
    m = v.m
    worst = 1e9
    for k, pre in ((1, "c1"), (2, "c2")):
        start, done = m.pose_at(k, 0.0), m.pose_at(k, 1.0)
        for pid in v.by_part:
            if pid.startswith(pre):
                if any(abs(a - b) > 1e-9 for ea, eb in zip(v.posed(pid, start), v.by_part[pid]) for a, b in zip(ea.c, eb.c)):
                    v.fail(f"{pid} is not as loaded at W 0")
        for w in [i * 0.01 for i in range(101)]:
            for psi in (0.0, 1.3, 2.9):
                a, b = v.mat(f"{pre}base", (psi, w, k, 1.0)), v.mat("riddle", (psi, w, k, 1.0))
                if max(abs(a[i][j] - b[i][j]) for i in range(3) for j in range(4)) > 1e-9:
                    v.fail(f"the oversize bed ({pre}) leaves the riddle at W {w:.2f}")
                    break
        sunk = ("c1top",) if k == 1 else ("c2mid", "c2top")
        bed = [e for e in v.relative(f"{pre}base", f"{pre}base", done) if e.name.endswith("_bed")]
        for pid in sunk:
            worst = min(worst, inside(v, v.relative(pid, f"{pre}base", done), bed, f"{pid} at W 1"))
        if k == 2:
            t = m.pose_at(2, (m.T["top"][1] + m.T["mid"][0]) / 2)
            worst = min(worst, inside(v, v.relative("c2top", "c2mid", t), v.relative("c2mid", "c2mid", t), "c2top in c2mid"))
        fines = ("c1fines",) if k == 1 else ("c2fines1",)
        bottom = v.named("frame", r"box_bottom", start)
        for pid in fines:
            worst = min(worst, inside(v, v.posed(pid, start), bottom, f"{pid} at W 0"))
            low = aabb_of(v.posed(pid, done))[0][1]
            if abs(low - m.BOX_BOTTOM) > 1e-4:
                v.fail(f"{pid} does not lie on the box's bottom at W 1 ({low:.4f})")
        if k == 2:
            worst = min(worst, inside(v, v.posed("c2fines2", start), v.posed("c2fines1", start), "c2fines2 at W 0"))
            top1 = aabb_of(v.posed("c2fines1", done))[1][1]
            low2 = aabb_of(v.posed("c2fines2", done))[0][1]
            if abs(low2 - top1) > 1e-4:
                v.fail(f"the second layer of fines does not lie on the first at W 1 ({low2:.4f} on {top1:.4f})")
        # the oversize left in the riddle, the riddle at rest on the bearers
        rest = max(abs(v.mat("riddle", done)[i][j] - (1.0 if i == j else 0.0)) for i in range(3) for j in range(4))
        bed_low = aabb_of(v.named(f"{pre}base", r"_bed$", done))[0][1]
        if rest > 1e-9 or abs(bed_low - (m.Y0 + m.MESH_TOP)) > 1e-6:
            v.fail(f"the oversize ({pre}) is not left on the mesh with the riddle set down at W 1")
        # the lumps stand on the bed, their feet in it, and well out of it
        lumps = v.named(f"{pre}base", r"_lump", done)
        b_top = m.Y0 + m.MESH_TOP + m.BASE[1]
        for lump in lumps:
            lo, hi = lump.aabb()
            if not (m.Y0 + m.MESH_TOP < lo[1] < b_top < hi[1]) or max(abs(lo[0] - m.CX), abs(hi[0] - m.CX), abs(lo[2] - m.CZ),
                                                                       abs(hi[2] - m.CZ)) > m.BASE[0]:
                v.fail(f"{lump.name} does not stand on the bed, its foot in it")
        lumps_out = max(e.aabb()[1][1] for e in v.named(f"{pre}base", r"_lump", start)) - b_top
        if not lumps or lumps_out < 0.75:
            v.fail(f"the oversize ({pre}) has no lumps standing out of the bed")
    heap = aabb_of(v.posed("c2top", m.pose_at(2, 0.0)))[1][1]
    print(f"charge: as loaded at W 0 (a full charge heaped {heap - m.Y0 - m.RIM_H:.2f} over the rim); the bed moves with the "
          f"riddle; every layer of fines hidden when sunk or before it rises, by at least {worst:.3f}; the fines in the "
          "box and the oversize on the mesh at W 1, the riddle set down")


def riddle_centre(v, pose):
    m = v.m
    return v.point("riddle", pose, [m.CX, m.Y0 + m.RIM_H / 2, m.CZ])


def check_shake(v):
    """The clock's travel moves nothing outside the shaking window; inside it the riddle goes to and fro and
    side to side as far as designed."""
    m = v.m
    psis = [i * math.pi / 8 for i in range(16)]
    outside = (0.0, 0.02, m.T["shake"][3] + 0.01, 0.9, 1.0)
    for k in (1, 2):
        for w in outside:
            ref = {p["id"]: v.mat(p["id"], (0.0, w, k, 1.0)) for p in v.parts}
            for psi in psis:
                for p in v.parts:
                    a = v.mat(p["id"], (psi, w, k, 1.0))
                    if max(abs(a[i][j] - ref[p["id"]][i][j]) for i in range(3) for j in range(4)) > 1e-12:
                        v.fail(f"the clock moves {p['id']} outside the shaking window (W {w})")
                        return
    rest = riddle_centre(v, m.REST)
    w = (m.T["shake"][1] + m.T["shake"][2]) / 2
    dz = [riddle_centre(v, (psi, w, 2, 1.0))[2] - rest[2] for psi in psis]
    dx = [riddle_centre(v, (psi, w, 2, 1.0))[0] - rest[0] for psi in psis]
    got_z, got_x = max(abs(d) for d in dz), max(abs(d) for d in dx)
    print(f"shake: the clock moves nothing outside the window; in it the riddle goes {got_z:.3f} to and fro (want {m.SHAKE_Z}), "
          f"{got_x:.3f} side to side (want {m.SHAKE_X})")
    if abs(got_z - m.SHAKE_Z) > 0.05 or abs(got_x - m.SHAKE_X) > 0.02:
        v.fail("the shake is not as designed")


def check_bearers(v):
    """The riddle rests on the bearers, each under a whole board (north and south), and is lifted clear of
    them while it is shaken; the bearers lie on the box's walls."""
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
    print(f"bearers: on the walls, each under a whole board; the riddle rests on both; shaken, it clears them by at least {worst:.3f}")
    if worst < 0.05:
        v.fail("the shaken riddle rubs on the bearers")


ALLOWED = [
    # (part, element regex, part, element regex): intended contacts
    ("riddle", r"_lower_", "frame", r"fr_bearer"),      # the riddle at rest on the bearers
    ("c[12]fines\\d?", None, "frame", r"box_bottom"),   # the fines hidden in the box's bottom, then on it
    ("c[12]base", None, "riddle", r"_wire"),            # the bed on the mesh
    ("c[12](top|mid)", None, "c[12](base|mid)", None),  # each layer sinking into the one under it
    ("c2fines2", None, "c2fines1", None),
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
    print(f"{label} over {len(poses)} poses: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at psi {p[0]:.2f} W {p[1]:.3f} k {p[2]}" for (a, b), p in ex))
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
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(poses)} poses")
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
    check_shake(v)
    check_bearers(v)
    check_clearances(v, [m.REST] + grid_poses(v), "clearances")
    if not quick:
        check_clearances(v, [m.REST] + cycle_poses(v, 0.0025), "swept paths (every 0.0025 of the cycle at the pace, both charges)")
        check_zfight(v)
    return v.ok


def validate_files(m, shape, frame_shape, ship):
    """Every texture declared; the one cell, boxed inside itself and lidded; no power cell."""
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
    return ok
