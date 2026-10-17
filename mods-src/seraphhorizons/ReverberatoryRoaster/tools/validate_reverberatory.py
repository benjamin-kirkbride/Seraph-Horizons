"""The reverberatory roaster generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved. Checks that look at parts together look at one scene at a time (what is
fitted: make_shape.fitted): a stage of the build, the furnace cold or running, the damper's place, the pipes' metal, the
cap.
"""

from __future__ import annotations

import json
import math
import os
import re
from pathlib import Path

from machinegen.checks import box_overhang, cells_touched, coplanar_faces, euler_round_trip, frame_floating, lid_gaps, obb_obb, supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import part_of, posed


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.req = {p["id"]: p["requires"] for p in parts}
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def mat(self, pid, theta):
        return self.m.pm(self.parts, pid, (theta, self.m.DEFAULT_SCENE))

    def posed(self, pid, theta):
        key = (pid, theta)
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, theta)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, theta, p):
        return [v * 16 for v in _apply(self.mat(pid, theta), [c / 16 for c in p])]

    def shows(self, pid, scene):
        r = self.req[pid]
        return r is None or r in self.m.fitted(scene)

    def drawn(self, scene, theta=0.0, moving=True):
        """Every element drawn in `scene` at `theta` (without the moving parts unless `moving`)."""
        return [e for pid in self.by_part if self.shows(pid, scene) and (moving or pid not in self.m.MOVING) for e in self.posed(pid, theta)]

    def named(self, pid, rx, theta=None):
        src = self.posed(pid, theta) if theta is not None else self.by_part.get(pid, [])
        r = re.compile(rx)
        return [e for e in src if r.search(e.name)]


def thetas(n=48, turns=2):
    """Angles over the rig's whole cycle (the rabbles turn half as fast as the axle: two turns)."""
    return [turns * math.tau * i / n for i in range(n)]


def inside(p, el, eps=0.0):
    """Whether point p (voxels) is inside element el (a rotated box), by more than eps."""
    d = [p[k] - el.c[k] for k in range(3)]
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        if abs(sum(d[i] * axis[i] for i in range(3))) > abs(el.size[k]) / 2 - eps:
            return False
    return True


def scene(run="running", build="services", damper="open", metal="copper", capped=False):
    return (build, run, damper, metal, capped)


STRUCTURE_PARTS = ("frame", "brickwork", "frames", "lining", "arch", "ironwork")
THIN_POST = 1.5


# ---------------------------------------------------------------- parts, textures
def check_basic(v):
    worst = euler_round_trip(v.els)
    print(f"euler round trip: worst {worst:.1e}")
    if worst > 1e-9:
        v.fail("an element's rotation does not survive being written as Euler angles")
    seen, dup = set(), set()
    for el in v.els:
        (dup if el.name in seen else seen).add(el.name)
    if dup:
        v.fail(f"duplicate element names: {sorted(dup)[:5]}")
    wrong = [(el.name, el.part, part_of(v.parts, el.name)) for el in v.els if part_of(v.parts, el.name) != el.part]
    if wrong:
        v.fail(f"{len(wrong)} elements land in the wrong part, e.g. {wrong[:4]}")
    empty = [p["id"] for p in v.parts if not v.by_part.get(p["id"])]
    if empty:
        v.fail(f"parts with no elements: {empty}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}: " + ", ".join(f"{k} {n}" for k, n in counts.items()))
    for sid, sc in (("built", scene("cold", build="masonry")), ("cold", scene("cold")), ("running", scene())):
        n = sum(len(v.by_part[p["id"]]) for p in v.parts if v.shows(p["id"], sc))
        print(f"elements drawn {sid}: {n}")


ROLES = [
    # (element regex, the textures it may wear)
    (r"^bk_brick_", {"brick"}),
    (r"^ln_brick_", {"lining"}),
    (r"^arch_", {"lining"}),
    (r"^(fr|fm|iw)_", {"iron"}),
    (r"^(hopper|firedoor|doors|discharge|damper(open|half|shut)|outletcap)_", {"iron"}),
    (r"^services_(infeed|fuel|output)chute", {"chute"}),
    (r"^(services|stoker|output)_", {"iron"}),
    (r"^entry_", {"oak"}),
    (r"^(gearcase|lineshaft|pedestal|rabble|feedroll)", {"iron"}),
    (r"^pipecopper_", {"pipecopper", "solder"}),
    (r"^pipelead_", {"pipelead", "solder"}),
    (r"^charge_", {"ore"}),
    (r"^fire_bed", {"ember"}),
    (r"^fire_coal", {"coal"}),
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


# ---------------------------------------------------------------- the build
def check_stages(v):
    """The build: at each stage (the iron frame alone, with the brickwork and the iron set into it, with the lining, with
    the arch and its ironwork, then each fitting in turn and last the services) and running, nothing floats: every
    static element drawn is joined to the ground (the moving parts are carried by their bearings: check_supports). The
    iron frame is iron only and has no thin post standing alone taller than half the body; its ties are hidden in the
    brickwork once it is in. Every stage's fittings need the ones before: each stage's parts touch what the stages
    before it put in. The contents are no stage's; every requires value is a stage's, a state's, a content or the cap."""
    m = v.m
    out = []
    scenes = [(sid, scene("cold", build=sid)) for sid, _ in m.BUILD_STATES]
    scenes += [("running", scene()), ("running lead, capped", scene(metal="lead", capped=True))]
    for sid, sc in scenes:
        drawn = [e for pid in v.by_part if pid not in m.MOVING and v.shows(pid, sc) for e in v.by_part[pid]]
        seen, loose = frame_floating(drawn)
        out.append(f"{sid} {len(seen)}")
        if loose:
            v.fail(f"at {sid}, elements float: {loose[:8]}")
    # and within each stage, after each of its fits in turn (the damper open with the doors)
    loose_fits = []
    for i, r in enumerate(m.STAGES):
        fit = set(m.STAGES[:i + 1]) | ({"damperopen"} if "doors" in m.STAGES[:i + 1] else set())
        drawn = [e for pid in v.by_part if pid not in m.MOVING and (v.req[pid] is None or v.req[pid] in fit) for e in v.by_part[pid]]
        if frame_floating(drawn)[1]:
            loose_fits.append(r)
    if loose_fits:
        v.fail(f"elements float after the fits {loose_fits}")
    frame = v.by_part["frame"]
    tex = {f["texture"].lstrip("#") for e in frame for f in e.faces.values()}
    if tex != {"iron"}:
        v.fail(f"the iron frame is not iron only: {sorted(tex)}")
    posts = [e.name for e in frame if (lambda lo, hi: hi[1] - lo[1] > m.H / 2 and hi[0] - lo[0] < THIN_POST and hi[2] - lo[2] < THIN_POST)(*e.aabb())]
    if posts:
        v.fail(f"thin posts stand in the iron frame: {posts}")
    bk = v.by_part["brickwork"]
    bare = 0
    for t in [e for e in frame if e.name.startswith("fr_tie")]:
        lo, hi = t.aabb()
        others = [e for e in frame if e is not t]
        for k in range(3):
            for side in (lo[k] - 0.02, hi[k] + 0.02):
                a, b = [q for q in range(3) if q != k]
                for fa in (0.1, 0.5, 0.9):
                    for fb in (0.1, 0.5, 0.9):
                        pnt = [0.0, 0.0, 0.0]
                        pnt[k] = side
                        pnt[a] = lo[a] + (hi[a] - lo[a]) * fa
                        pnt[b] = lo[b] + (hi[b] - lo[b]) * fb
                        if pnt[1] < 0.0 or any(inside(pnt, e) for e in others):
                            continue
                        if not any(inside(pnt, e) for e in bk):
                            bare += 1
    if bare:
        v.fail(f"{bare} points off the frame's ties are in no brickwork: a tie shows")
    # each stage stands on what came before it: each of its fits' parts (moving ones at rest) touch what the earlier
    # stages and its own earlier fits put in
    unborne = []
    for i, r in enumerate(m.STAGES):
        before = set(m.STAGES[:i]) | {None}
        prior = [e for pid in v.by_part if v.req[pid] in before for e in v.posed(pid, 0.0)]
        own = [e for pid in v.by_part if v.req[pid] == r for e in v.posed(pid, 0.0)]
        if not any(touching(a.aabb(), b.aabb()) for a in own for b in prior):
            unborne.append(r)
    if unborne:
        v.fail(f"fits whose parts touch nothing put in before them: {unborne}")
    contents = set(m.CONTENTS)
    if contents & set(m.STAGES):
        v.fail("contents are fitted as stages")
    values = {p["requires"] for p in v.parts if p["requires"] is not None}
    named = set(m.STAGES) | contents | {f"damper{d}" for d in m.DAMPERS} | {f"pipe{x}" for x in m.PIPE_METALS} | {"outletcap"}
    if values != named:
        v.fail(f"requires values in no stage, state or content: {sorted(values ^ named)}")
    print(f"stages: nothing floats ({'; '.join(out)}); the iron frame iron only, no thin post over {m.H / 2:.0f}, its ties "
          f"hidden; each stage's fits on what came before them ({'; '.join(f'{g}: ' + ', '.join(rs) for g, rs in m.BUILD_GROUPS)})")


# ---------------------------------------------------------------- containment, the sides, the row
def check_containment(v):
    """Every element, at every sampled angle, inside the footprint's cells (not just the machine box: the footprint is
    the body and the drive's row), and nothing in the chimney's cell or under the drive's row's outlet clash."""
    m = v.m
    cells = set(m.footprint())
    box = (m.CELLS_X, m.CELLS_Y, m.CELLS_Z)
    worst, where, outside = -1e9, None, set()
    for th in thetas(24):
        for pid in v.by_part:
            if th and pid not in m.MOVING:
                continue
            for el in v.posed(pid, th):
                lo, hi = el.aabb()
                o = box_overhang(lo, hi, box)
                if o > worst:
                    worst, where = o, el.name
                for c in cells_touched(lo, hi):
                    if c not in cells:
                        outside.add((el.name, c))
    print(f"containment: {len(cells)} cells; worst overhang of the machine box {worst:.3f} voxels ({where}); "
          f"{len(outside)} elements outside the cells")
    if worst > 0.01:
        v.fail(f"{where} leaves the machine box")
    if outside:
        v.fail(f"elements outside the footprint's cells: {sorted(outside)[:6]}")
    if m.CHIMNEY_CELL in cells:
        v.fail("the chimney's cell is the furnace's")


FLUSH_WEST = ("fr_angle_nw_", "fr_angle_sw_", "fr_band_w_")   # the box frame's legs set flush in the shared wall's face


def check_sides(v):
    """The shared long wall (west) is flush, plain brick over its whole face (sampled every half voxel) but for its
    corner angles' and its band's legs, set in flush, and nothing else reaches its plane (no buckstay, nut or door stands beyond it); the
    work doors, the buckstays and every door's iron are on the east side or the end faces; two furnaces back to back (the
    second turned half round about the shared plane's middle, x 0, z 40) touch nowhere but at their shared walls, at rest
    and turning."""
    m = v.m
    bricks = v.by_part["brickwork"] + [e for e in v.by_part["frame"] if e.name.startswith(FLUSH_WEST)]
    bad, n = [], 0
    y = 0.25
    while y < m.TOP:
        z = 0.25
        while z < m.D:
            n += 1
            if not any(inside((0.05, y, z), e) for e in bricks):
                bad.append((0.05, y, z))
            z += 0.5
        y += 0.5
    if bad:
        v.fail(f"the shared wall's face is not plain brick at {len(bad)} of {n} points, e.g. {bad[:4]}")
    reach = set()
    for pid in v.by_part:
        for e in v.by_part[pid]:
            if e.aabb()[0][0] < 1e-6 and not e.name.startswith(("bk_brick",) + FLUSH_WEST):
                reach.add(e.name)
    if reach:
        v.fail(f"not brickwork at the shared wall's plane: {sorted(reach)[:4]}")
    doors = [e.name for pid in ("frames", "doors", "discharge", "ironwork") for e in v.by_part[pid]
             if e.aabb()[0][0] < m.EAST[0] and not e.name.startswith(("fm_fdlintel", "fm_fdsill", "iw_rod", "iw_anchor"))]
    if doors:
        v.fail(f"door or buckstay iron off the east side: {doors[:4]}")
    hits = 0
    for th in (0.0, 1.1):
        sc = m.DEFAULT_SCENE
        a = [e for e in v.drawn(sc, th) if e.aabb()[0][0] < 2.0]
        b = []
        for e in a:
            c = e.clone()
            c.c = [-c.c[0], c.c[1], m.D - c.c[2]]
            c.r = [[-c.r[0][0], -c.r[0][1], -c.r[0][2]], list(c.r[1]), [-c.r[2][0], -c.r[2][1], -c.r[2][2]]]
            b.append(c)
        for ea in a:
            alo, ahi = ea.aabb()
            for eb in b:
                blo, bhi = eb.aabb()
                if all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)) and obb_obb(ea, eb):
                    hits += 1
    if hits:
        v.fail(f"two furnaces back to back run into each other ({hits} pairs)")
    print(f"sides: the shared west wall plain brick (its corner angles and band flush) at {n - len(bad)} of {n} points, nothing else at "
          f"its plane; the doors and buckstays on the east; two back to back clear of each other but at the shared walls")


def touching(a, b, eps=1e-6):
    """Two boxes (lo, hi) meet: they overlap or share a face, over an area (not just along an edge)."""
    gap = [max(a[0][k], b[0][k]) - min(a[1][k], b[1][k]) for k in range(3)]   # > 0 apart, 0 touching, < 0 overlapping
    if any(g > eps for g in gap):
        return False
    return sum(1 for g in gap if g < -eps) >= 2


def components(boxes):
    """The connected groups of named boxes {name: (lo, hi)} (touching()), as sorted lists of names."""
    left, out = set(boxes), []
    while left:
        seen, todo = set(), [min(left)]
        while todo:
            n = todo.pop()
            if n in seen:
                continue
            seen.add(n)
            todo += [o for o in left if o not in seen and touching(boxes[n], boxes[o])]
        left -= seen
        out.append(sorted(seen))
    return out


def leg_els(v, name):
    """A leg's elements: by its name, or its pieces cut at the block grid (its name and a number)."""
    rx = re.compile(re.escape(name) + r"\d*$")
    return [e for pid in ("frame", "ironwork") for e in v.by_part[pid] if rx.match(e.name)]


def leg_box(v, name):
    """The box of a leg (its pieces cut at the block grid), from the frame or the ironwork."""
    els = leg_els(v, name)
    return aabb_of(els) if els else None


def _span(a, b, step=1.0):
    """Sample positions across a..b: every `step` from a quarter in, and a quarter short of b."""
    out, q = [], a + 0.25
    while q < b - 0.2:
        out.append(q)
        q += step
    if b - a > 0.5:
        out.append(b - 0.25)
    return out


def check_binding(v):
    """The binding is angle iron (the owner's rule), every member of the box frame (make_shape.binding_members) as
    drawn: each leg its box, ANGLE_T thick across the face it is set into, its outer face flush in that face's plane with
    no brick proud of it, its inner face bedded against the brick (or the iron it joins, or the arch) at every voxel but
    where it spans the discharge door's recess as its lip; each angle's legs meeting over an area. Each buckstay is an
    angle: its flange on the east face, bedded against the wall and the skewback angle, its web standing out from the
    flange's edge to the footprint's face, its rod's nut on the flange."""
    m = v.m
    t = m.ANGLE_T
    brick_els = v.by_part["brickwork"] + v.by_part["lining"]
    backing = brick_els + v.by_part["arch"] + v.by_part["frame"] + v.by_part["ironwork"]
    (rx, ry, rz), (sx, sy) = m.D_RECESS, m.D_SLOT

    def lip(p):
        return rz[0] <= p[2] <= rz[1] and ((rx[0] <= p[0] <= rx[1] and ry[0] <= p[1] <= ry[1])
                                           or (sx[0] <= p[0] <= sx[1] and sy[0] <= p[1] <= sy[1]))
    bad, n, legs_n = [], 0, 0
    members = m.binding_members()
    for member, kind, axis, legs in members:
        boxes = {}
        for name, lo, hi, (k, c, sg) in legs:
            legs_n += 1
            bx = leg_box(v, name)
            if bx is None:
                bad.append(f"{name}: missing")
                continue
            boxes[name] = bx
            if any(abs(bx[0][q] - lo[q]) > 1e-6 or abs(bx[1][q] - hi[q]) > 1e-6 for q in range(3)):
                bad.append(f"{name}: not its box")
            if abs(hi[k] - lo[k] - t) > 1e-6 or abs((hi[k] if sg > 0 else lo[k]) - c) > 1e-6:
                bad.append(f"{name}: not ANGLE_T thick with its outer face in its face's plane")
            a, b = [q for q in range(3) if q != k]
            for pa in _span(lo[a], hi[a]):
                for pb in _span(lo[b], hi[b]):
                    n += 1
                    p_out, p_in = [0.0] * 3, [0.0] * 3
                    p_out[a] = p_in[a] = pa
                    p_out[b] = p_in[b] = pb
                    p_out[k], p_in[k] = c + sg * 0.05, c - sg * (t + 0.05)
                    if any(inside(p_out, e) for e in brick_els):
                        bad.append(f"{name}: brick stands proud of it at {p_out}")
                    if not lip(p_in) and not any(inside(p_in, e) for e in backing):
                        bad.append(f"{name}: not bedded at {[round(q, 3) for q in p_in]}")
        if kind != "knee" and len(boxes) == len(legs) and len(components(boxes)) != 1:
            bad.append(f"{member}: its legs do not meet")
    for i, zc in enumerate(m.STAY_Z, 1):
        fl, wb = leg_box(v, f"iw_stay{i}_flange"), leg_box(v, f"iw_stay{i}_web")
        if fl is None or wb is None:
            bad.append(f"buckstay {i}: a leg missing")
            continue
        if abs(fl[0][0] - m.FACE_E) > 1e-6 or abs(fl[1][0] - fl[0][0] - t) > 1e-6 or abs(wb[0][0] - fl[1][0]) > 1e-6 \
                or abs(wb[1][2] - fl[1][2]) > 1e-6 or abs(wb[1][2] - wb[0][2] - t) > 1e-6 or abs(wb[1][0] - m.W) > 1e-6:
            bad.append(f"buckstay {i}: not an angle with its flange on the east face and its web standing out from its edge")
        backs = v.by_part["brickwork"] + [e for e in v.by_part["frame"] if e.name.startswith("fr_skew_")]
        for y in [m.HEARTH_Y + 0.5 + k for k in range(int(m.SKEW[1][1] - m.HEARTH_Y))]:
            n += 1
            if not any(inside((m.FACE_E - 0.05, y, zc), e) for e in backs):
                bad.append(f"buckstay {i}: its flange is not bedded against the wall at y {y}")
        nut = next(e for e in v.by_part["ironwork"] if e.name == f"iw_nut{i}").aabb()
        if abs(nut[0][0] - fl[1][0]) > 1e-6:
            bad.append(f"buckstay {i}: its nut does not bear on the flange")
    print(f"binding: {len(members)} members of angle iron ({legs_n} legs, {t} thick), {len(m.STAY_Z)} angle buckstays: each "
          f"leg its box, flush in its face, bedded ({n} points); each angle's legs meeting")
    if bad:
        v.fail(f"the binding is not angle iron wrapping its corners: {bad[:5]} ({len(bad)})")


def outline_heights(v):
    """The brickwork's outline as a height over each column of a voxel (x, z): the top of the brick, the lining, the arch
    and the box frame (and the tie rods and anchors where they lie in the shared wall's top, in their chases), built
    and nothing fitted, the openings in its top filled (to the top they open in): the furnace's 2.5-D envelope, its openings in the faces
    ignored."""
    m = v.m
    nx, nz = int(m.W), int(m.D)
    hgt = [[0.0] * nz for _ in range(nx)]
    els = [e for pid in ("brickwork", "lining", "arch", "frame") for e in v.by_part[pid]]
    clip = {e.name: m.IN_X[0] for e in v.by_part["ironwork"] if e.name.startswith(("iw_rod", "iw_anchor"))}
    els += [e for e in v.by_part["ironwork"] if e.name in clip]
    for e in els:
        lo, hi = e.aabb()
        x1 = min(hi[0], clip.get(e.name, hi[0]))
        square = all(abs(abs(e.r[i][i]) - 1.0) < 1e-9 for i in range(3))
        for i in range(max(0, int(math.floor(lo[0]))), min(nx, int(math.ceil(x1)))):
            for k in range(max(0, int(math.floor(lo[2]))), min(nz, int(math.ceil(hi[2])))):
                p = (i + 0.5, k + 0.5)
                if not (lo[0] <= p[0] <= x1 and lo[2] <= p[1] <= hi[2]):   # (a box edge on a column's middle counts)
                    continue
                if square:
                    top = hi[1]
                else:
                    top, y = None, hi[1]
                    while y > lo[1]:
                        if inside((p[0], y, p[1]), e):
                            top = y
                            break
                        y -= 0.0625
                    if top is None:
                        continue
                hgt[i][k] = max(hgt[i][k], top)
    # the openings in the top (the well and its seat, the outlet's seat and bore, the feed hole; the glands' holes in
    # the arch's crown) count as the top they open in
    holes = [((m.WELL_SEAT[0], m.WELL_SEAT[2]), m.TOP), (m.CAP_SEAT, m.TOP), (m.UPTAKE, m.TOP), (m.FEED_HOLE, m.TOP)]
    holes += [((m.CROWN_X, (z0, z1)), m.CROWN_Y[1]) for z0, z1 in m.crown_holes()]
    for ((x0, x1), (z0, z1)), top in holes:
        for i in range(int(x0), int(math.ceil(x1))):
            for k in range(int(z0), int(math.ceil(z1))):
                hgt[i][k] = max(hgt[i][k], top)
    return hgt


EDGE_DROP = 3.0                              # a step in the outline at least this high is a top edge to be banded


def check_box_frame(v):
    """The box frame (the owner's rule): the iron reads as one closed box round the whole furnace, following its outline
    (outline_heights). Every top edge (a step of at least EDGE_DROP from a column of the outline to the next or to the
    ground beyond it) is wrapped by a band: iron in its face just under the edge and in its top just in from it, all
    along it. Every outside vertical corner of the outline (sampled every voxel up to the bands) has an angle: iron in
    both its faces beside the corner. Every band's and upright's ends meet another member of the frame (an upright's foot
    may stand on the ground), over an area. The box frame is one connected frame, and with the ironwork (buckstays,
    rods, anchors, nuts) still one."""
    m = v.m
    if not hasattr(v, "outline_h"):
        v.outline_h = outline_heights(v)
    hgt = v.outline_h
    nx, nz = len(hgt), len(hgt[0])
    iron_els = v.by_part["frame"] + v.by_part["ironwork"]

    def iron(p):
        return any(inside(p, e) for e in iron_els)
    bad, edges, corners = [], 0, 0
    for i in range(nx):
        for k in range(nz):
            ha = hgt[i][k]
            for di, dk in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                a, b = i + di, k + dk
                hb = hgt[a][b] if 0 <= a < nx and 0 <= b < nz else 0.0
                if ha - hb < EDGE_DROP:
                    continue
                edges += 1
                # the boundary between the columns, and probes in column (i, k)
                bx = i + 0.5 + di * 0.5
                bz = k + 0.5 + dk * 0.5
                p1 = (bx - di * 0.3 if di else i + 0.5, ha - 1.0, bz - dk * 0.3 if dk else k + 0.5)
                p2 = (bx - di * 1.0 if di else i + 0.5, ha - 0.3, bz - dk * 1.0 if dk else k + 0.5)
                for p in (p1, p2):
                    if not iron(p):
                        bad.append(f"a top edge unbanded at {p}")
    ytop = m.TOP - m.ANGLE_L
    y = 0.5
    while y < ytop:
        for xv in range(nx + 1):
            for zv in range(nz + 1):
                ins = []
                for ci, ck in ((xv - 1, zv - 1), (xv, zv - 1), (xv - 1, zv), (xv, zv)):
                    if 0 <= ci < nx and 0 <= ck < nz and hgt[ci][ck] > y:
                        ins.append((ci, ck))
                if len(ins) != 1:
                    continue
                corners += 1
                ci, ck = ins[0]
                sx = 1.0 if ci == xv - 1 else -1.0      # outward from the inside column across the corner
                sz = 1.0 if ck == zv - 1 else -1.0
                for p in ((xv - sx * 0.3, y, zv - sz * 1.0), (xv - sx * 1.0, y, zv - sz * 0.3)):
                    if not iron(p):
                        bad.append(f"an outside corner without its angle at {p}")
        y += 1.0
    # every band's and upright's ends meet another member; one frame
    members = m.binding_members()
    boxes = {}
    owner = {}
    for member, kind, axis, legs in members:
        for name, *_ in legs:
            bx = leg_box(v, name)
            if bx is not None:
                boxes[name] = bx
                owner[name] = member
    for member, kind, axis, legs in members:
        if kind == "knee":
            continue
        mine = [n for n, *_ in legs if n in boxes]
        theirs = [n for n in boxes if owner[n] != member]
        lo_end = min(boxes[n][0][axis] for n in mine)
        hi_end = max(boxes[n][1][axis] for n in mine)
        for end, at in ((0, lo_end), (1, hi_end)):
            if kind == "upright" and end == 0 and at < 1e-6:
                continue
            ok = any(touching(boxes[n], boxes[o]) for n in mine if abs(boxes[n][end][axis] - at) < 1e-6 for o in theirs)
            if not ok:
                bad.append(f"{member}'s {'low' if end == 0 else 'high'} end ({'xyz'[axis]} {at:.3f}) meets no other member")
    groups = components(boxes)
    if len(groups) != 1:
        bad.append(f"the box frame is {len(groups)} pieces: {[g[:3] for g in groups]}")
    work = {e.name: e.aabb() for e in v.by_part["ironwork"]}
    allg = components({**boxes, **work})
    if len(allg) != 1:
        bad.append(f"the box frame and the ironwork are {len(allg)} pieces: {[g[:3] for g in allg]}")
    print(f"box frame: {edges} column edges of the outline's top edges banded, {corners} outside corner stations (every "
          f"voxel to y {ytop:g}) with their angles; every band's and upright's ends meet another member; {len(boxes)} legs "
          f"one frame, and one with the {len(work)} pieces of ironwork")
    for b in bad[:6]:
        v.fail(f"the box frame: {b}")
    if bad:
        v.fail(f"the box frame: {len(bad)} faults")


# ---------------------------------------------------------------- slivers
def material_lattice(v, sc):
    """The structure drawn in scene `sc` (no moving part) as a rectilinear lattice: breakpoints along each axis (every
    axis-aligned element's faces; every half voxel across the arch's span, whose strips are turned) and for each cell
    what fills its middle: "bk" (the brickwork), "ln" (the lining), "arch", "iron" or None. Every box is taken to the
    quarter voxel (the z-fighting fix moves faces in by up to an eighth: not a piece of brick), the arch's strips a
    little large for the same reason."""
    import bisect
    m = v.m
    els = [e for pid in v.by_part if pid not in m.MOVING and v.shows(pid, sc) for e in v.by_part[pid]]
    turned = [e for e in els if e.part == "arch" and not all(abs(abs(e.r[i][i]) - 1.0) < 1e-9 for i in range(3))]
    square = [e for e in els if e not in turned]   # (any other turned part, small, by its box)
    lim = (m.W, m.CELLS_Y * m.B, m.D)
    axes = [{0.0, lim[k]} for k in range(3)]
    def snap(q):
        return round(q * 4.0) / 4.0
    for e in square:
        lo, hi = e.aabb()
        for k in range(3):
            axes[k].add(snap(min(max(lo[k], 0.0), lim[k])))
            axes[k].add(snap(min(max(hi[k], 0.0), lim[k])))
    tlo = [min(e.aabb()[0][k] for e in turned) for k in range(3)] if turned else None
    thi = [max(e.aabb()[1][k] for e in turned) for k in range(3)] if turned else None
    if turned:
        for k in (0, 1):
            q = math.floor(tlo[k] * 2) / 2
            while q <= thi[k]:
                axes[k].add(round(min(max(q, 0.0), lim[k]), 6))
                q += 0.5
    xs, ys, zs = (sorted(a) for a in axes)
    n = (len(xs) - 1, len(ys) - 1, len(zs) - 1)
    grid = {}
    rank = {"brickwork": "bk", "lining": "ln", "arch": "arch"}
    order = sorted(square, key=lambda e: 0 if e.part in rank else 1)    # the iron over the brick it is set into
    for e in order:
        tag = rank.get(e.part, "iron")
        lo, hi = e.aabb()
        r = []
        for k, vals in enumerate((xs, ys, zs)):
            a = bisect.bisect_left(vals, snap(max(lo[k], 0.0)) - 1e-6)
            b = bisect.bisect_left(vals, snap(min(hi[k], lim[k])) - 1e-6)
            r.append(range(a, b))
        for i in r[0]:
            for j in r[1]:
                for k in r[2]:
                    grid[(i, j, k)] = tag
    if turned:
        boxes = [(e, e.aabb()) for e in turned]
        rr = []
        for k, vals in enumerate((xs, ys, zs)):
            rr.append(range(max(0, bisect.bisect_right(vals, tlo[k]) - 1), min(n[k], bisect.bisect_left(vals, thi[k]) + 1)))
        for i in rr[0]:
            for j in rr[1]:
                for k in rr[2]:
                    if (i, j, k) in grid:
                        continue
                    p = ((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2, (zs[k] + zs[k + 1]) / 2)
                    if any(lo[0] - 0.13 < p[0] < hi[0] + 0.13 and lo[1] - 0.13 < p[1] < hi[1] + 0.13
                           and lo[2] - 0.13 < p[2] < hi[2] + 0.13 and inside(p, e, eps=-0.13) for e, (lo, hi) in boxes):
                        grid[(i, j, k)] = "arch"
    return (xs, ys, zs), n, grid


def slivers(v, sc):
    """Every piece of brick showing in scene `sc` narrower than MIN_BRICK either way on its face: [(width, material,
    the face's normal, a point on it)]. A face cell of the brickwork or the lining shows when the cell beyond it is
    empty; its width each way along the face is the run of showing cells of its own material through it. A run ends at
    iron, at the other brick, at the arch, at an outer edge (the face turns away: the cell beyond is empty) or in a
    corner (the face meets another rising from it: the cell beyond is solid and does not show). A run from an outer
    edge into a corner is a recess's reveal, not a piece of brick, and counts as wide. Only faces seen from outside
    count (seen_from_outside: not the hearth's and the firebox's insides), not an underside, and not a seat's rebate
    (the bunker's rim's and the outlet cap's)."""
    m = v.m
    seats = [([x[0], y[0], z[0]], [x[1], y[1], z[1]]) for x, y, z in (m.WELL_SEAT, (m.CAP_SEAT[0], m.CAP_Y, m.CAP_SEAT[1]))]
    if not hasattr(v, "outline_h"):
        v.outline_h = outline_heights(v)
    hgt = v.outline_h
    (xs, ys, zs), n, grid = material_lattice(v, sc)
    vals = (xs, ys, zs)
    out = []
    seen = set()
    for c, mat in grid.items():
        if mat not in ("bk", "ln"):
            continue
        for ax in range(3):
            for sg in (-1, 1):
                q = list(c)
                q[ax] += sg
                if 0 <= q[ax] < n[ax] and grid.get(tuple(q)) is not None:
                    continue
                if ax == 1 and sg < 0:
                    continue                                # an underside: on the ground, or under an overhang
                beyond = tuple((vals[d][q[d]] + vals[d][q[d] + 1]) / 2 if 0 <= q[d] < n[d] else -1.0 for d in range(3))
                if any(all(lo[d] < beyond[d] < hi[d] for d in range(3)) for lo, hi in seats):
                    continue                                # a seat's floor or side: the rebate a rim or cap sits in

                def shows(cc):
                    if grid.get(cc) != mat:
                        return False
                    qq = list(cc)
                    qq[ax] += sg
                    return not (0 <= qq[ax] < n[ax] and grid.get(tuple(qq)) is not None)
                widths = []
                for d in range(3):
                    if d == ax:
                        continue
                    w = vals[d][c[d] + 1] - vals[d][c[d]]
                    ends = []
                    for dirn in (-1, 1):
                        cc = list(c)
                        while True:
                            cc[d] += dirn
                            if not (0 <= cc[d] < n[d]):
                                ends.append("edge")
                                break
                            t = tuple(cc)
                            if shows(t):
                                w += vals[d][cc[d] + 1] - vals[d][cc[d]]
                                continue
                            g = grid.get(t)
                            if g is None:
                                ends.append("edge")
                            elif g in ("bk", "ln"):
                                qq = list(cc)
                                qq[ax] += sg
                                bare = not (0 <= qq[ax] < n[ax] and grid.get(tuple(qq)) is not None)
                                ends.append(g if bare else "corner")
                            else:
                                ends.append(g)
                            break
                    if sorted(ends) == ["corner", "edge"]:
                        w = max(w, m.MIN_BRICK)
                    widths.append(w)
                if min(widths) < m.MIN_BRICK - 1e-6:
                    p = [(vals[k][c[k]] + vals[k][c[k] + 1]) / 2 for k in range(3)]
                    p[ax] = vals[ax][c[ax] + 1] if sg > 0 else vals[ax][c[ax]]
                    if seen_from_outside(m, vals, n, grid, hgt, p, ax, sg):
                        out.append((round(min(widths), 3), mat, "xyz"[ax] + ("+" if sg > 0 else "-"), tuple(round(q, 3) for q in p)))
    return out


SEEN_DEPTH = 8.0                             # a face farther than this inside the outline is not seen (the hearth down the uptake)


def seen_from_outside(m, vals, n, grid, hgt, p, ax, sg):
    """Whether a face at p (normal ax, sg) is seen from outside: a ray from it straight out along its normal, or (a side
    face) out and up at 45 degrees, or (a top face) up and out at 45 degrees, reaches the air beyond the machine box through nothing solid, having run at most
    SEEN_DEPTH below the outline's height (outline_heights) on the way. A top face is seen straight up or at 45 degrees
    each way (under the hopper, beside its base plate)."""
    import bisect
    lim = (m.W, m.CELLS_Y * m.B, m.D)
    dirs = [[0.0, 0.0, 0.0]]
    dirs[0][ax] = float(sg)
    r = 1.0 / math.sqrt(2.0)
    if ax != 1:
        d = [0.0, 0.0, 0.0]
        d[ax], d[1] = sg * r, r
        dirs.append(d)
    else:
        for k in (0, 2):
            for side in (-1.0, 1.0):
                d = [0.0, sg * r, 0.0]
                d[k] = side * r
                dirs.append(d)
    for d in dirs:
        q, inside_len, step, ok = list(p), 0.0, 0.25, False
        q = [q[k] + d[k] * 0.01 for k in range(3)]
        while True:
            q = [q[k] + d[k] * step for k in range(3)]
            if not all(0.0 < q[k] < lim[k] for k in range(3)):
                ok = True
                break
            cell = tuple(min(n[k] - 1, max(0, bisect.bisect_right(vals[k], q[k]) - 1)) for k in range(3))
            if grid.get(cell) is not None:
                break
            i, kk = int(q[0]), int(q[2])
            if 0 <= i < len(hgt) and 0 <= kk < len(hgt[0]) and q[1] < hgt[i][kk]:
                inside_len += step
                if inside_len > SEEN_DEPTH:
                    break
        if ok:
            return True
    return False


def check_slivers(v):
    """No sliver of brick (the owner's rule): built with nothing fitted, and finished, no piece of the brickwork or the
    lining that shows from outside is narrower than MIN_BRICK either way along its face (slivers())."""
    m = v.m
    bad, out = [], []
    for sid, sc in (("built", scene("cold", build="masonry")), ("finished", scene())):
        found = slivers(v, sc)
        out.append(f"{sid} {len(found)}")
        bad += [(sid,) + f for f in found]
    print(f"slivers: no piece of brick showing narrower than {m.MIN_BRICK:g} either way ({'; '.join(out)} face cells too narrow)")
    seen = set()
    for sid, w, mat, face, p in bad:
        key = (mat, face, p[0] // 4, p[1] // 4, p[2] // 4)
        if key in seen:
            continue
        seen.add(key)
        if len(seen) > 12:
            break
        v.fail(f"a sliver of {'brickwork' if mat == 'bk' else 'lining'} {w} wide on its {face} face at {p} ({sid})")


# ---------------------------------------------------------------- the brick
def what_is(v, p):
    """What lies at p: "ln" (the lining's brick or the arch), "bk" (the brickwork's), "iron" (the frame or the iron set
    in), or None (open)."""
    for e in v.by_part["lining"] + v.by_part["arch"]:
        if inside(p, e):
            return "ln"
    for e in v.by_part["brickwork"]:
        if inside(p, e):
            return "bk"
    if any(inside(p, e) for pid in ("frame", "frames", "ironwork") for e in v.by_part[pid]):
        return "iron"
    return None


def face_samples(lo, hi, k, sign, step=1.0):
    a, b = [q for q in range(3) if q != k]
    out = []
    ua = lo[a] + 0.25
    while ua < hi[a] - 0.2:
        ub = lo[b] + 0.25
        while ub < hi[b] - 0.2:
            pnt = [0.0, 0.0, 0.0]
            pnt[k] = hi[k] if sign > 0 else lo[k]
            pnt[a], pnt[b] = ua, ub
            out.append(pnt)
            ub += step
        ua += step
    return out


def hot_spaces(m):
    """The hot spaces as boxes, and which of their faces are hot: (lo, hi, faces), faces a set of (axis, sign)."""
    x0, x1 = m.IN_X
    (ux, uz), (fx, fz) = m.UPTAKE, m.FEED_HOLE
    (tx, ty, tz) = m.THROAT
    grate = min(top for _, _, top in m.steps()) - m.STEP_T   # the step grate's lowest tread's underside
    return [
        ([x0, grate, m.FIREBOX_Z[0]], [x1, m.SPRING_Y, m.FIREBOX_Z[1]], {(0, -1), (0, 1), (2, -1), (1, 1)}),   # the firebox
        ([x0, m.HEARTH_Y, m.HEARTH_Z[0]], [x1, m.SPRING_Y, m.HEARTH_Z[1]], {(0, -1), (0, 1), (2, 1), (1, -1)}),     # the hearth
        ([x0, m.SPRING_Y, m.FLUE_BLOCK[0]], [x1, m.CAVITY_TOP, m.HEARTH_Z[1]], {(0, -1), (0, 1), (2, 1), (1, 1)}),  # the cavity
        ([x0, m.BRIDGE_TOP, m.BRIDGE_Z[0]], [x1, m.SPRING_Y, m.BRIDGE_Z[1]], {(1, -1)}),                            # the bridge's top
        ([x0, grate, m.FIREBOX_Z[1]], [x1, m.BRIDGE_TOP, m.FIREBOX_Z[1] + 0.01], {(2, 1)}),                    # the bridge's face
        ([ux[0], m.CAVITY_TOP, uz[0]], [ux[1], m.TOP - 0.6, uz[1]], {(0, -1), (0, 1), (2, -1), (2, 1)}),            # the uptake
        ([fx[0], m.CAVITY_TOP, fz[0]], [fx[1], m.TOP - 0.6, fz[1]], {(0, -1), (0, 1), (2, -1), (2, 1)}),            # the feed hole
        ([tx[0], ty[0], tz[0]], [tx[1], ty[1], tz[1]], {(0, -1), (0, 1), (2, -1), (2, 1)}),                         # the throat
    ]


def check_lining(v):
    """The lining: on every hot face (the firebox's walls and roof, the bridge, the hearth's bed and walls, the flue
    end's cavity, the uptake, the feed hole and the throat) the brick is the lining's for at least LINING_T from the face
    out, or to where the brick ends, sampled every voxel and walked out every quarter; the ash pit's faces, the ash
    mouth's and the tunnel's are the brickwork's; the arch is the lining's refractory throughout."""
    m = v.m
    t = m.LINING_T
    thinnest, where, n = 1e9, None, 0
    for lo, hi, faces in hot_spaces(m):
        for k, sign in sorted(faces):
            for pnt in face_samples(lo, hi, k, sign):
                first = list(pnt)
                first[k] += sign * 0.125
                w0 = what_is(v, first)
                if w0 != "ln":
                    if w0 == "bk":
                        v.fail(f"a hot face is the brickwork's at {first}")
                    continue
                n += 1
                d = 0.125
                while d < t + 0.5:
                    q = list(pnt)
                    q[k] += sign * d
                    w = what_is(v, q) if 0.0 < q[k] < (m.W, m.TOP, m.D)[k] else None
                    if w != "ln":
                        if w == "bk" and d - 0.125 < t - 1e-6 and d - 0.125 < thinnest:
                            thinnest, where = d - 0.125, pnt
                        break
                    d += 0.25
    cold, wrong = 0, []
    x0, x1 = m.IN_X
    (mx, my), (tx, ty, tz) = m.MOUTH, m.TUNNEL
    for lo, hi, warm in (([x0, m.ASHPIT_Y[0], m.FIREBOX_Z[0]], [x1, m.FIREBOX_LINING_Y - 0.5, m.FIREBOX_Z[1]], ()),
                         ([mx[0], my[0], 1.0], [mx[1], my[1], m.FRONT_Z[1]], ()),
                         ([tx[0], ty[0], tz[0]], [m.EAST[0], ty[1], tz[1]], ((1, 1),))):   # under the hearth's bed: its lining
        for k in range(3):
            for sign in (-1, 1):
                if (k, sign) in warm:
                    continue
                for pnt in face_samples(lo, hi, k, sign, step=2.0):
                    q = list(pnt)
                    q[k] += sign * 0.125
                    w = what_is(v, q)
                    if w in ("ln", "bk"):
                        cold += 1
                        if w == "ln":
                            wrong.append(q)
    tex = {f["texture"] for e in v.by_part["arch"] for f in e.faces.values()}
    if tex != {"#lining"}:
        v.fail(f"the arch is not the lining's refractory: {tex}")
    if thinnest < 1e9:
        v.fail(f"the lining is {thinnest:.2f} thick at {where}")
    if wrong:
        v.fail(f"the ash pit, the ash mouth or the tunnel is lined at {len(wrong)} points, e.g. {wrong[:3]}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or the "
          f"lining reaching through; the ash pit, the ash mouth and the tunnel the brickwork's ({cold} points); the arch "
          f"refractory")


def check_build(v):
    """The stages' counts: the brickwork's and the lining's volumes, measured from their elements (each lattice box in
    exactly one brick box: their volumes add up to the lattice's), and the arch's, give the rig's quantities at the
    game's rates, rounded up; the stages are the structure's, the fittings' and the services', in order, and every
    stage's requires, its parts and its states are parts'."""
    m = v.m
    xs, ys, zs, mat = m.brick_lattice()
    lat = {"bk": 0.0, "ln": 0.0}
    for (i, j, k), w in mat.items():
        if w:
            lat[w] += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]) * (zs[k + 1] - zs[k])
    els = {w: sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in v.by_part[{"bk": "brickwork", "ln": "lining"}[w]]
                  if e.name.startswith(f"{w}_brick_")) for w in ("bk", "ln")}
    for w in ("bk", "ln"):
        if abs(lat[w] - els[w]) > 0.5:             # the z-fighting fix steps a few faces in by 0.015
            v.fail(f"the {w} boxes hold {els[w]:.2f} cubic voxels, the lattice {lat[w]:.2f}")
    c = m.counts()
    build = v.rig["build"]
    flat = [f for s in build["stages"] for f in s["fits"]]
    st = {f["requires"]: f for f in flat}
    got = {"bricks": st["brickwork"]["materials"][0]["quantity"], "lining": st["lining"]["materials"][0]["quantity"],
           "fireclay": st["lining"]["materials"][1]["quantity"], "arch": st["arch"]["materials"][0]["quantity"]}
    want = {"bricks": math.ceil(lat["bk"] / m.B ** 3 * m.BRICKS_PER_BLOCK - 1e-9),
            "lining": math.ceil(lat["ln"] / m.B ** 3 * m.LINING_PER_BLOCK - 1e-9)}
    want["fireclay"] = math.ceil(want["lining"] / m.LINING_PER_FIRECLAY - 1e-9)
    want["arch"] = c["arch"]
    if got != want:
        v.fail(f"the stages' counts {got} are not the measured {want}")
    groups = [(s["id"], tuple(f["requires"] for f in s["fits"])) for s in build["stages"]]
    if groups != list(m.BUILD_GROUPS) or groups[-1] != ("services", ("services",)) or groups[0][0] != "masonry":
        v.fail(f"the stages are not {m.BUILD_GROUPS}, masonry first and the services last")
    requires = {p["requires"] for p in v.parts}
    fits = {r for s in flat for r in [s["requires"]] + s.get("also", [])}
    if not fits <= requires:
        v.fail(f"a stage's requires or state is no part's: {sorted(fits - requires)}")
    wrong = [(s["requires"], q) for s in flat for q in s.get("alsoParts", []) if v.req.get(q) != s["requires"]]
    if wrong:
        v.fail(f"a stage's alsoParts are not fitted with it: {wrong}")
    print(f"build (provisional): brickwork {lat['bk'] / m.B ** 3:.2f} blocks -> {got['bricks']} fired bricks; lining "
          f"{lat['ln'] / m.B ** 3:.2f} blocks -> {got['lining']} refractory bricks and {got['fireclay']} fire clay; the arch "
          f"{c['archVolume']:.2f} blocks -> {got['arch']} refractory bricks and {c['archFireclay']} fire clay")


# ---------------------------------------------------------------- the moving parts
ALLOWED = [
    ("entry", None, "gearcase", None),
    ("lineshaft", r"_rod", "gearcase", None),
    ("entry", None, "lineshaft", r"_rod"),
    ("lineshaft", r"_rod", "pedestals", r"_bearing"),
    ("lineshaft", r"_rod", "hopper", r"_neck_[ns]"),
    ("lineshaft", r"_rod", "feedroll", None),
    ("lineshaft", r"_pinion\d_tooth", r"rabble\d", r"_wheel_tooth"),
    (r"rabble\d", r"_spindle", "rabblemounts", r"_(gland|base|swivel)"),
    (r"rabble\d", r"_spindle", "arch", r"arch_v5_"),
    (r"rabble\d", r"_wheelhub", "rabblemounts", r"_swivel"),
    (r"rabble\d", r"_(arm\dplough|hub|spindle)", "charge", None),
]


def allowed(pa, na, pb, nb):
    for a, ra, b, rb in ALLOWED:
        for x, nx, y, ny in ((pa, na, pb, nb), (pb, nb, pa, na)):
            if re.fullmatch(a, x) and re.fullmatch(b, y) and (ra is None or re.search(ra, nx)) and (rb is None or re.search(rb, ny)):
                return True
    return False


def overlap(a, b, eps=0.02):
    return all(a[0][q] < b[1][q] - eps and b[0][q] < a[1][q] - eps for q in range(3))


def check_clearances(v, n, label):
    """Every moving part against everything drawn with it, the furnace finished and running, over the rig's cycle: the rabbles clear
    the hearth, the bridge, the walls and each other, the feed roll the neck, the gears everything but their mates, the
    wheels the water pipes."""
    m = v.m
    sc = m.DEFAULT_SCENE
    ths = thetas(n)
    statics = [(pid, el, el.aabb()) for pid in v.by_part if pid not in m.MOVING and v.shows(pid, sc) for el in v.by_part[pid]]
    cands = {}
    for pid in m.MOVING:
        boxes = [e.aabb() for th in ths for e in v.posed(pid, th)]
        swept = ([min(b[0][q] for b in boxes) for q in range(3)], [max(b[1][q] for b in boxes) for q in range(3)])
        cands[pid] = [s for s in statics if overlap(s[2], swept)]
    allhits = {}
    for th in ths:
        moving = [(pid, el, el.aabb()) for pid in m.MOVING for el in v.posed(pid, th)]
        for pa, ea, (alo, ahi) in moving:
            others = cands[pa] + [x for x in moving if x[0] != pa and m.MOVING.index(x[0]) > m.MOVING.index(pa)]
            for pb, eb, (blo, bhi) in others:
                if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                    continue
                if allowed(pa, ea.name, pb, eb.name):
                    continue
                if obb_obb(ea, eb):
                    allhits.setdefault(tuple(sorted((pa, pb))), {}).setdefault(tuple(sorted((ea.name, eb.name))), th)
    print(f"{label} over {len(ths)} angles, running: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} at theta {t:.3f}" for (a, b), t in ex))
    if allhits:
        v.fail("moving parts run into each other or the furnace")


def check_supports(v):
    """The line shaft in the gear case's south boss and at least two bearings (the pedestals, the neck's end walls), the
    entry in the north boss, each spindle in its gland and its swivel, each wheel's hub resting on its swivel."""
    m = v.m
    statics = [el for pid in ("pedestals", "rabblemounts", "gearcase", "hopper") for el in v.by_part[pid]]
    found, span = supports(statics, v.named("lineshaft", r"_rod"), 2, (m.SHAFT[0], m.SHAFT[1]))
    bearings = sorted({re.sub(r"_[0-9]+$", "", n) for n in found if n.endswith("_bearing") or n.startswith(("gearcase_boss", "hopper_neck"))})
    print(f"support line shaft: {len(bearings)} bearings ({', '.join(bearings)}), shaft z {span}")
    if len([b for b in bearings if b.endswith("_bearing")]) < 2 or "gearcase_bosss" not in bearings or not any(b.startswith("hopper_neck") for b in bearings):
        v.fail("the line shaft is not carried by the gear case's boss, two pedestals and the neck")
    found, _ = supports(statics, v.named("entry", r"."), 2, (m.SHAFT[0], m.SHAFT[1]))
    if not any(n.startswith("gearcase_bossn") for n in found):
        v.fail("the entry does not run into the gear case's boss")
    walls = sorted((e.aabb() for e in v.named("hopper", r"_neck_[ns]")), key=lambda b: b[0][2])
    if len(walls) != 2 or not all(b[0][2] < span[1] and span[0] < b[1][2] for b in walls) or span[1] > walls[1][1][2] + 1e-6:
        v.fail("the line shaft's end does not run in the neck's end walls")
    for k, zs in enumerate(m.RABBLE_Z, 1):
        found, sp = supports(statics + v.by_part["arch"], v.named(f"rabble{k}", r"_spindle"), 1, (m.SHAFT[0], zs))
        names = sorted({re.sub(r"_[0-9]+$", "", n) for n in found if n.startswith("rabblemount")})
        print(f"support rabble {k}: spindle y {sp}, carried by {', '.join(names)}")
        if not any(n.endswith("_gland") for n in names) or not any(n.endswith("_swivel") for n in names):
            v.fail(f"rabble {k}'s spindle is not in its gland and its swivel")
        hub = min(e.aabb()[0][1] for e in v.named(f"rabble{k}", r"_wheelhub"))
        seat = max(e.aabb()[1][1] for e in v.by_part["rabblemounts"] if e.name.startswith(f"rabblemount{k}_swivel"))
        if abs(hub - seat) > 0.05:
            v.fail(f"rabble {k}'s wheel hub does not rest on its swivel ({hub:.3f} against {seat:.3f})")


def overlap_depth(a, b):
    if not obb_obb(a, b, eps=0.0):
        return 0.0
    lo, hi = 0.0, 1.5
    for _ in range(18):
        mid = (lo + hi) / 2
        if obb_obb(a, b, eps=mid):
            lo = mid
        else:
            hi = mid
    return 2 * lo


MESH_LIMIT = 0.3


def check_gearing(v):
    """Each rabble's bevel pair: the pitch cones share the apex where the axes meet; the rig turns the spindle at -NP/NW
    of the shaft, so the contact point moves alike on both either way round; tooth and gap stay within a tenth of a
    tooth at the contact; the box teeth run no more than MESH_LIMIT into each other over a tooth's cycle."""
    m = v.m
    want = -m.NP / m.NW
    worst_v, worst_g, worst_d, where = 0.0, 0.0, 0.0, None
    d = 1e-4
    for k, zs in enumerate(m.RABBLE_Z, 1):
        dr = next(p for p in v.parts if p["id"] == f"rabble{k}")["drivers"][0]
        if abs(dr["ratio"] - want) > 1e-6:
            v.fail(f"rabble {k} turns at {dr['ratio']}, not {want}")
        zp = m.pinion_z(zs)
        if abs(zp + m.RW - zs) > 1e-9 or abs(m.WHEEL_Y + m.RP - m.SHAFT[1]) > 1e-9:
            v.fail(f"bevel pair {k}: the pitch cones' apexes are not where the axes meet")
        contact = [m.SHAFT[0], m.WHEEL_Y, zp]
        for sgn in (1.0, -1.0):
            a0, a1 = v.point("lineshaft", 0.0, contact), v.point("lineshaft", sgn * d, contact)
            b0, b1 = v.point(f"rabble{k}", 0.0, contact), v.point(f"rabble{k}", sgn * d, contact)
            va = [(a1[i] - a0[i]) / d for i in range(3)]
            vb = [(b1[i] - b0[i]) / d for i in range(3)]
            mag = max(math.sqrt(sum(x * x for x in va)), 1e-12)
            worst_v = max(worst_v, math.sqrt(sum((va[i] - vb[i]) ** 2 for i in range(3))) / mag)
        for th in thetas(72):
            ang = [math.pi + math.tau * i / m.NP + th for i in range(m.NP)]
            a = min(ang, key=lambda t: abs(math.remainder(t - math.pi, math.tau)))
            xp = -m.RP * math.sin(a)
            gaps = [math.pi + math.tau * j / m.NW + m.RATIO * th for j in range(m.NW)]
            g = min(gaps, key=lambda t: abs(math.remainder(t - math.pi, math.tau)))
            xc = m.RW * math.sin(g)
            worst_g = max(worst_g, abs(xp - xc))
        for i in range(48):
            th = 0.31 + (math.tau / m.NP) * i / 48
            for a in v.named("lineshaft", rf"_pinion{k}_tooth", th):
                alo, ahi = a.aabb()
                for b in v.named(f"rabble{k}", r"_wheel_tooth", th):
                    blo, bhi = b.aabb()
                    if all(alo[q] < bhi[q] and blo[q] < ahi[q] for q in range(3)):
                        dd = overlap_depth(a, b)
                        if dd > worst_d:
                            worst_d, where = dd, (a.name, b.name, round(th, 3))
    pitch = math.pi * m.MODULE
    print(f"gearing: {m.NP}-tooth bevel pinions on the {m.NW}-tooth wheels' north rims, the spindles at {want:.4f} of the shaft; contact "
          f"speeds differ by {100 * worst_v:.3f} %; tooth and gap within {worst_g:.4f} voxels (a tooth's pitch is {pitch:.3f}); teeth "
          f"run at most {worst_d:.3f} into each other (limit {MESH_LIMIT}){'' if where is None else f' {where}'}")
    if worst_v > 0.01:
        v.fail("a bevel pair's contact point does not move alike on both gears")
    if worst_g > 0.1 * pitch:
        v.fail("a pinion's teeth drift out of the wheel's gaps")
    if worst_d > MESH_LIMIT:
        v.fail("a bevel pair's teeth run too far into each other")


def check_reverse(v):
    """Every machine takes the axle turning either way: the entry turns with the axle, and everything beyond the gear
    case's rectifier (the line shaft, the rabbles, the feed roll) is posed alike for the axle turned back as forward by
    as much; nothing moves before the drive is fitted."""
    m = v.m
    worst, entry_moves = 0.0, True
    for th in (0.7, 2.9, 7.3):
        for pid in m.RECTIFIED:
            a, b = v.mat(pid, th), v.mat(pid, -th)
            worst = max(worst, max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)))
        a, b = v.mat("entry", th), v.mat("entry", -th)
        entry_moves = entry_moves and max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)) > 1e-3
    early = [pid for pid in m.MOVING if v.shows(pid, scene("cold", build="hopperdoors"))]
    print(f"reverse: beyond the rectifier {', '.join(m.RECTIFIED)} are posed alike for the axle turned either way (worst "
          f"difference {worst:.1e}); the entry turns back with the axle; moving before the drive: {early or 'nothing'}")
    if worst > 1e-12 or not entry_moves or set(m.MOVING) - set(m.RECTIFIED) != {"entry"} or early:
        v.fail("a working part turns back when the axle does")


def check_feedroll(v):
    """The feed roll is keyed on the line shaft, sits under the hopper's slot inside the neck and over the feed chute,
    its flutes inside the slot's width; the feed chute runs down through the flue end block's roof into the cavity."""
    m = v.m
    worst = 0.0
    for th in thetas(12):
        a, b = v.mat("lineshaft", th), v.mat("feedroll", th)
        worst = max(worst, max(abs(a[r][c] - b[r][c]) for r in range(3) for c in range(4)))
    lo, hi = aabb_of(v.named("feedroll", r"."))
    (sx0, sx1), (sz0, sz1) = m.HOPPER_SLOT
    ok = sx0 < lo[0] and hi[0] < sx1 and sz0 <= lo[2] and hi[2] <= sz1 and lo[1] > m.FEED_CHUTE[1]
    chute = aabb_of(v.named("hopper", r"_chute"))
    ok2 = chute[0][1] < m.CAVITY_TOP and chute[1][1] > m.TOP
    print(f"feed roll: turns with the line shaft (worst {worst:.1e}); x {lo[0]:.2f}..{hi[0]:.2f} inside the slot {sx0:g}..{sx1:g}, "
          f"z {lo[2]:.2f}..{hi[2]:.2f}; its foot at y {lo[1]:.2f} over the feed chute's top {m.FEED_CHUTE[1]:g}; the chute y "
          f"{chute[0][1]:g}..{chute[1][1]:g} through the roof")
    if worst > 1e-12 or not ok:
        v.fail("the feed roll is not keyed on the shaft, under the slot and over the chute")
    if not ok2:
        v.fail("the feed chute does not run through the flue end block's roof")


# ---------------------------------------------------------------- the charge's path, the firing, the doors
def sweep(v, k):
    zs = v.m.RABBLE_Z[k - 1]
    return max(math.hypot(p[0] - v.m.SHAFT[0], p[2] - zs) for e in v.named(f"rabble{k}", r"plough") for p in e.corners())


def check_path(v):
    """The charge's way: it drops from the feed chute in the second rabble's sweep, each rabble's sweep meets the next's,
    and the discharge hole is in the first rabble's sweep, past the bridge, over the tunnel; the sweeps clear the bridge,
    the flue end wall and the side walls by a voxel."""
    m = v.m
    (dx0, dx1), (dz0, dz1) = m.DISCHARGE
    (tx, ty, tz) = m.TUNNEL
    if not (tx[0] <= dx0 and dx1 <= tx[1] and tz[0] <= dz0 and dz1 <= tz[1]):
        v.fail("the discharge hole is not over the tunnel")
    r = [sweep(v, k) for k in (1, 2)]
    z1, z2 = m.RABBLE_Z
    hole = math.hypot((dx0 + dx1) / 2 - m.SHAFT[0], (dz0 + dz1) / 2 - z1)
    (fx, fz) = m.FEED_HOLE
    feed = math.hypot((fx[0] + fx[1]) / 2 - m.SHAFT[0], (fz[0] + fz[1]) / 2 - z2)
    if not (m.BRIDGE_Z[1] < dz0 and hole < r[0]):
        v.fail("the discharge hole is not in the first rabble's sweep, past the bridge")
    if not feed < r[1]:
        v.fail(f"the charge drops {feed:.2f} from the second spindle, beyond its sweep ({r[1]:.2f})")
    if not z1 + r[0] >= z2 - r[1] - 0.25:
        v.fail("the rabbles' sweeps do not meet")
    if not (z1 - r[0] > m.BRIDGE_Z[1] + 1.0 and z2 + r[1] < m.HEARTH_Z[1] - 1.0):
        v.fail("a rabble's sweep comes within a voxel of the bridge or the flue end wall")
    if not (m.SHAFT[0] - max(r) > m.IN_X[0] + 1.0 and m.SHAFT[0] + max(r) < m.IN_X[1] - 1.0):
        v.fail("a rabble's sweep comes within a voxel of a side wall")
    print(f"charge's path: drops {feed:.2f} from the second spindle (sweep {r[1]:.2f}); sweeps z {z1 - r[0]:.2f}..{z1 + r[0]:.2f} and "
          f"{z2 - r[1]:.2f}..{z2 + r[1]:.2f} (bridge to {m.BRIDGE_Z[1]:g}, the flue end wall from {m.HEARTH_Z[1]:g}); the discharge "
          f"hole {hole:.2f} from the first spindle, over the tunnel")


def check_firing(v):
    """The firing: the treads step down to the bridge, the throat's foot is over the top tread and its top in the
    bunker's floor, the bunker in the well under the fuel cell's top, hung by its rim flush in the seat round the well's
    mouth (out to the bands: no strip of brick round it); the fire door over the treads, the ash mouth under them. On
    the fire end face only the fire door and the ash mouth open."""
    m = v.m
    (dx, dy), (mx, my) = m.FIRE_DOOR, m.MOUTH
    treads = sorted(v.named("stoker", r"_step\d"), key=lambda e: e.c[2])
    tops = [e.aabb()[1][1] for e in treads]
    if not all(a > b for a, b in zip(tops, tops[1:])) or abs(treads[-1].aabb()[1][2] - m.BRIDGE_Z[0]) > 1e-6:
        v.fail("the step grate does not step down to the bridge")
    if not (dy[1] > tops[-1] and my[1] < min(e.aabb()[0][1] for e in treads)):
        v.fail("the fire door is not over the step grate, or the ash mouth not under it")
    throat = aabb_of(v.named("stoker", r"_throat"))
    t0 = treads[0].aabb()
    if not (throat[0][1] > t0[1][1] and throat[0][2] < t0[1][2] and throat[1][2] > t0[0][2]):
        v.fail("the throat's foot is not over the top tread")
    bunker = aabb_of(v.named("stoker", r"_bunker"))
    fc = [m.FUEL_CELL[k] * 16 for k in range(3)]
    if not (bunker[0][0] < fc[0] + 16 and bunker[1][0] > fc[0] and bunker[0][2] < fc[2] + 16 and bunker[1][1] <= m.TOP):
        v.fail("the bunker is not under the fuel cell's top")
    rim = aabb_of(v.named("stoker", r"_rim"))
    (sx, sy, sz) = m.WELL_SEAT
    if any(abs(a - b) > 1e-6 for a, b in zip(rim[0] + rim[1], [sx[0], sy[0], sz[0], sx[1], sy[1], sz[1]])):
        v.fail(f"the bunker's rim is not flush in its seat: {rim}")
    holes = []
    xs = [x + 0.5 for x in range(0, int(m.FACE_E))]
    ys = [y + 0.5 for y in range(0, int(m.TOP))]
    solid = v.by_part["brickwork"] + v.by_part["frame"] + v.by_part["frames"]
    for x in xs:
        for y in ys:
            if not any(inside((x, y, 1.5), e) for e in solid):
                holes.append((x, y))
    odd = [(x, y) for x, y in holes if not ((dx[0] <= x <= dx[1] and dy[0] <= y <= dy[1]) or (mx[0] <= x <= mx[1] and my[0] <= y <= my[1]))]
    if odd:
        v.fail(f"the fire end face opens elsewhere than the fire door and the ash mouth: {odd[:4]}")
    print(f"firing: {len(treads)} treads at {', '.join(f'{t:g}' for t in tops)} to the bridge, the fire door {dy[0]:g}..{dy[1]:g} "
          f"over them, the ash mouth under; the throat's foot at {throat[0][1]:g}; the bunker's rim flush in its seat; the fire "
          f"end opens only at the fire door and the ash mouth ({len(holes)} points)")


def in_rect(r, y, z, eps=1e-6):
    (ya, yb), (za, zb) = r
    return ya + eps < y < yb - eps and za + eps < z < zb - eps


def peep_hole(m, k):
    """Door k's peep hole in its plate, as ((y0, y1), (z0, z1)), or None."""
    if k != m.PEEP_DOOR:
        return None
    zc = m.RABBLE_Z[k - 1]
    ph, (pa, pb) = m.PEEP
    py0 = m.DOOR_PLATE[0]
    return (py0 + pa, py0 + pb), (zc - ph, zc + ph)


def check_peep(v):
    """The owner's ruling (the work doors are shut, so the rabbles showed only from above): door PEEP_DOOR's plate has a
    peep hole over the opening, open, with no shutter; the hole
    lies inside the door's opening, so through it only the opening and the hearth show (nothing of the lining but
    through the hole); and sight lines from outside through it, level and looking down, cross nothing of the finished
    furnace (but the charge) and meet the rabble's spindle, hub or arms at every angle of its turn."""
    m = v.m
    k = m.PEEP_DOOR
    zc = m.RABBLE_Z[k - 1]
    hw, (y0, y1) = m.WORK_DOOR
    (ha, hb), (za, zb) = peep_hole(m, k)
    if not (y0 < ha and hb < y1 and zc - hw < za and zb < zc + hw):
        v.fail(f"door {k}'s peep hole {(ha, hb), (za, zb)} is not inside its opening")
    statics = [e for e in v.drawn(scene(), moving=False) if e.part != "charge"]
    ym = (ha + hb) / 2
    xs = m.FACE_E + 1.3
    lines = [([m.W + 1.0, ym, z], [m.SHAFT[0], ym, zc]) for z in (za + 0.3, zc, zb - 0.3)]
    arm = (m.ARM_Y[0] + m.ARM_Y[1]) / 2
    top, run = hb - 0.4, xs - m.SHAFT[0]         # looking down: in under the hole's top edge to the spindle at the arms' height
    lines.append(([m.W + 4.0, top + (top - arm) * (m.W + 4.0 - xs) / run, zc], [m.SHAFT[0], arm, zc]))
    rabble = f"rabble{k}"
    seen = 0
    for a, b in lines:
        dx = b[0] - a[0]
        at_hole = [a[q] + (b[q] - a[q]) * (xs - a[0]) / dx for q in range(3)]
        if not in_rect(((ha, hb), (za, zb)), at_hole[1], at_hole[2]):
            v.fail(f"a peep sight line {a} -> {b} misses the hole ({at_hole})")
            continue
        pts = [[a[q] + (b[q] - a[q]) * i / 300 for q in range(3)] for i in range(1, 301)]
        hit = next((e.name for p in pts for e in statics if inside(p, e, 0.01)), None)
        if hit:
            v.fail(f"the view through door {k}'s peep hole {a} -> {b} is blocked by {hit}")
            continue
        miss = [th for th in thetas(48) if not any(inside(p, e) for p in pts for e in v.posed(rabble, th))]
        if miss:
            v.fail(f"the view through door {k}'s peep hole {a} -> {b} misses the rabble at {len(miss)} angles")
            continue
        seen += 1
    a, b = lines[-1]
    pts = [[a[q] + (b[q] - a[q]) * i / 300 for q in range(3)] for i in range(1, 301)]
    arms = sum(1 for th in thetas(144) if any(inside(p, e) for p in pts for e in v.posed(rabble, th) if "_arm" in e.name))
    if arms == 0:
        v.fail(f"no arm of rabble {k} passes the downward view through the peep hole")
    return f"{seen} sight lines through it clear to rabble {k} at every angle, its arms across the downward one at {arms} of 144"


def check_doors(v):
    """The work doors, on the east side only (kept for clearing accretions, shut): the openings through the east wall at
    each rabble, each plate over its opening but for door PEEP_DOOR's peep hole. No opening in the shared wall. The
    discharge door is clear of the tunnel's mouth (the output chute's)."""
    m = v.m
    hw, (y0, y1) = m.WORK_DOOR
    for k, zc in enumerate(m.RABBLE_Z, 1):
        plates = v.named("doors", rf"_plate{k}")
        shut = aabb_of(plates)
        hole = peep_hole(m, k)
        gaps = [(y, z) for y in _span(y0, y1, 0.25) for z in _span(zc - hw, zc + hw, 0.25)
                if not any(inside([m.FACE_E + 1.3, y, z], e) for e in plates) and not (hole and in_rect(hole, y, z))]
        if gaps or not (shut[0][1] <= y0 and shut[1][1] >= y1 and shut[0][2] <= zc - hw and shut[1][2] >= zc + hw):
            v.fail(f"door {k}'s plate does not cover its opening shut (but for a peep hole): {gaps[:3]}")
    peeps = check_peep(v)
    west = [p for p in ((4.0, (y0 + y1) / 2, z) for z in range(1, int(m.D))) if not any(inside(p, e) for e in v.by_part["brickwork"] + v.by_part["lining"])]
    if west:
        v.fail(f"the shared wall opens at {west[:3]}")
    (tx, ty, tz) = m.TUNNEL
    op = v.named("discharge", r"_plate")[0].aabb()
    (rx, ry, rz) = m.D_RECESS
    if op[0][2] < tz[1] or op[1][2] > rz[1] + 1e-6:
        v.fail("the discharge door is not clear of the tunnel's mouth, in its recess")
    print(f"work doors: on the east only, shut, each plate over its opening but for door {m.PEEP_DOOR}'s peep hole ({peeps}); "
          f"the shared wall unbroken; the discharge door clear of the mouth, in its recess")


# ---------------------------------------------------------------- the chutes, the water, the outlet, the damper, the draught
def game_root():
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root)
    return None


def vanilla_chute():
    """The vanilla straight chute's elements (from, to), from the game's own shape when the game is here; None when it is
    not (CI), where the recorded walls stand."""
    root = game_root()
    if root is None:
        return None
    f = root / "assets" / "survival" / "shapes" / "block" / "metal" / "chute" / "straight.json"
    shape = json.loads(f.read_text())
    return [(e["from"], e["to"]) for e in shape["elements"]], str(f)


def check_chutes(v):
    """Each chute the machine takes (the ore infeed's, the fuel's, the output's) ends in a section of the vanilla chute
    whose walls are the game's own (cross-section, thickness, place in the cell), in its texture, reaching its anchor's
    face, so a vanilla chute placed in the next cell meets it with no gap or step; the sections are the services' (the
    last stage, the owner's ruling for every machine)."""
    m = v.m
    game = vanilla_chute()
    want_ud = sorted(tuple(w) for w in m.CHUTE_UD_WALLS)
    want_we = sorted(tuple(w) for w in m.CHUTE_WE_WALLS)
    if game is not None:
        we = sorted((round(f[1], 6), round(t[1], 6), round(f[2], 6), round(t[2], 6)) for f, t in game[0])
        ud = sorted((round(16 - t[1], 6), round(16 - f[1], 6), round(f[2], 6), round(t[2], 6)) for f, t in game[0])
        if we != want_we or ud != want_ud:
            v.fail(f"the recorded chute walls are not the game's ({game[1]}): we {we}, ud {ud}")
    for key, cell, face, pid, rx, want in (("infeed", m.INFEED_CELL, m.INFEED_FACE, "services", r"_infeedchute", want_ud),
                                           ("fuel", m.FUEL_CELL, m.FUEL_FACE, "services", r"_fuelchute", want_ud),
                                           ("output", m.OUTPUT_CELL, m.OUTPUT_FACE, "services", r"_outputchute", want_we)):
        o = [cell[k] * 16 for k in range(3)]
        got = []
        for e in v.named(pid, rx):
            lo, hi = e.aabb()
            if face == "up":
                got.append((round(lo[0] - o[0], 6), round(hi[0] - o[0], 6), round(lo[2] - o[2], 6), round(hi[2] - o[2], 6)))
                reach = abs(hi[1] - (o[1] + 16)) < 1e-6
            else:
                got.append((round(lo[1] - o[1], 6), round(hi[1] - o[1], 6), round(lo[2] - o[2], 6), round(hi[2] - o[2], 6)))
                reach = abs(hi[0] - (o[0] + 16)) < 1e-6
            if not reach:
                v.fail(f"the {key} chute's {e.name} does not reach the cell's {face} face")
            if {f["texture"] for f in e.faces.values()} != {"#chute"}:
                v.fail(f"the {key} chute's {e.name} is not in the chute's texture")
        if sorted(got) != want:
            v.fail(f"the {key} chute's walls {sorted(got)} are not the vanilla chute's {want}")
    print(f"chutes: the infeed's and the fuel's sections up and down, the output's along x, the vanilla chute's walls in "
          f"their cells to their faces; checked against {game[1] if game else 'the recorded walls (the game is not here)'}")


def check_water(v):
    """The water: the manifold is ppex's pipe (6 across) against the water cell's east face (the owner's
    ruling: the player's pipe comes in from the side), at the face's 5..11 both ways, open to it, nothing in the way of
    the player's pipe beyond it; a full-size connector out of its side, its end plugged round the small pipe's bore
    with a ring of solder (the vanner's reducer); the small pipe the vanner's, 2.4 across, every run; it joins the
    connector to each swivel's port, every piece touching the next;
    the copper and lead parts are the same boxes; the pipes are the services' (drawn only with the last stage)."""
    m = v.m
    cell = [m.WATER_CELL[k] * 16 for k in range(3)]
    man = aabb_of(v.named("pipecopper", r"_manifold"))
    # the owner's ruling: the player's pipe comes in from the side, so the water face is a side face of the drive's row
    if m.WATER_FACE != "east" or m.WATER_CELL[1] != 2:
        v.fail(f"the water anchor {m.WATER_CELL} {m.WATER_FACE} is not on the drive row's east side")
    want = ([cell[0] + 16 - 6, cell[1] + 5, cell[2] + 5], [cell[0] + 16, cell[1] + 11, cell[2] + 11])
    if any(abs(a - b) > 1e-6 for a, b in zip(man[0] + man[1], want[0] + want[1])):
        v.fail(f"the manifold {man} is not ppex's pipe against the water cell's east face {want}")
    mid = [cell[0] + 15.5, cell[1] + 8, cell[2] + 8]
    if any(inside(mid, e) for e in v.named("pipecopper", r"_manifold")):
        v.fail("the manifold is closed at the water face")
    way = (want[0], [want[1][0] + 16, want[1][1], want[1][2]])
    near = [e.name for e in v.drawn(scene()) if not e.part.startswith("pipe") and overlap(e.aabb(), way)]
    if near:
        v.fail(f"the player's pipe's way to the water face is blocked: {near[:3]}")
    pipes = v.by_part["pipecopper"]
    ports = [e for e in v.by_part["rabblemounts"] if "_port" in e.name]
    group = pipes + ports
    seen, todo = {0}, [0]
    boxes = [e.aabb() for e in group]
    while todo:
        i = todo.pop()
        for j in range(len(group)):
            if j in seen:
                continue
            ov = [min(boxes[i][1][q], boxes[j][1][q]) - max(boxes[i][0][q], boxes[j][0][q]) for q in range(3)]
            if min(ov) > -0.02 and sum(o > 0.05 for o in ov) >= 2:
                seen.add(j)
                todo.append(j)
    loose = [group[i].name for i in range(len(group)) if i not in seen]
    if loose:
        v.fail(f"the water's pipes do not join the manifold to every swivel: {loose[:5]}")
    # the owner's ruling: the vanner's soldered reducer, a full-size connector, its end plugged round the small pipe's bore,
    # a ring of solder where the small pipe leaves it, and the small pipe the vanner's (2.4 across, a 1.6 bore)
    def across(rx, axis):
        lo, hi = aabb_of(v.named("pipecopper", rx))
        return [round(hi[q] - lo[q], 6) for q in range(3) if q != axis]
    con = aabb_of(v.named("pipecopper", r"_connector_"))
    big = 2 * m.solderedpipe.HALF
    if across(r"_connector_", 2) != [big, big] or abs(con[0][2] - (m.MANIFOLD[2] + m.solderedpipe.HALF)) > 1e-6:
        v.fail(f"the connector is not ppex's pipe ({big} across) out of the manifold's side: {con}")
    plug = v.named("pipecopper", r"_plug_")
    si = m.SMALL_HALF - m.SMALL_WALL
    if len(plug) != 4 or any(inside([m.SMALL_X + dx, m.MANIFOLD[1] + dy, m.CONNECTOR_Z - 0.5], e)
                             for e in plug for dx in (-si + 0.05, 0.0, si - 0.05) for dy in (-si + 0.05, 0.0, si - 0.05)):
        v.fail("the connector's end is not plugged round the small pipe's open bore")
    ring = aabb_of(v.named("pipecopper", r"_reducer_"))
    if across(r"_reducer_", 2) != [round(2 * m.SMALL_BEND, 6)] * 2 or abs(ring[0][2] - m.CONNECTOR_Z) > 1e-6:
        v.fail(f"the reducer's ring of solder is not round the small pipe at the plug: {ring}")
    runs = sorted({re.sub(r"_(north|south|east|west|up|down)$", "", e.name) for e in pipes if re.search(r"_small_(south|down|along\d|in\d)_", e.name)})
    small = 2 * m.SMALL_HALF
    for r in runs:
        lo, hi = aabb_of([e for e in pipes if e.name.startswith(r + "_")])
        ax = {"south": 2, "down": 1, "along": 2, "in": 0}[re.sub(r"\d$", "", r.rsplit("_", 1)[1])]
        sz = [round(hi[q] - lo[q], 6) for q in range(3) if q != ax]
        if sz != [small, small]:
            v.fail(f"{r} is not the vanner's small pipe ({small} across): {sz}")
    if m.SMALL_HALF != 1.2 or m.SMALL_WALL != 0.4:
        v.fail("the small pipe is not the vanner's size (2.4 across round a 1.6 bore)")
    lead = {e.name.replace("pipelead", "pipecopper"): e for e in v.by_part["pipelead"]}
    diff = [e.name for e in pipes if e.name not in lead or e.aabb() != lead[e.name].aabb()]
    if diff:
        v.fail(f"the lead pipes are not the copper ones: {diff[:3]}")
    if v.shows("pipecopper", scene("cold", build="feeding")) or not v.shows("pipecopper", scene()):
        v.fail("the pipes are not drawn with the services only")
    print(f"water: the manifold ppex's pipe against the water cell's east face, open to it, the way beyond clear; a full-size connector out of it, plugged round "
          f"the small pipe's bore with a ring of solder (the vanner's reducer); {len(runs)} runs of the vanner's {small}-across "
          f"small pipe, {len(pipes) - len(v.named('pipecopper', r'_manifold'))} pieces in all, join it to {len(ports)} swivels' ports; "
          f"copper and lead alike")


def check_damper(v):
    """The ash mouth's damper (the crucible furnace's): in each place a plate in its recess between its guides; shut it
    covers the whole mouth, half its upper half, open none of it; it never leaves the recess."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    (rx, ry, rz) = m.RECESS
    gx = (m.GUIDES[0][1], m.GUIDES[1][0])
    for place in m.DAMPERS:
        els = v.by_part[f"damper{place}"]
        plate = next(e for e in els if e.name.endswith("_plate"))
        lo, hi = plate.aabb()
        allo, alhi = aabb_of(els)
        if allo[2] < rz[0] - 1e-9 or alhi[2] > rz[1] + 1e-9 or allo[1] < ry[0] - 1e-9 or alhi[1] > ry[1] + 1e-9:
            v.fail(f"the damper leaves its recess when {place}")
        if abs(lo[0] - gx[0]) > 1e-9 or abs(hi[0] - gx[1]) > 1e-9:
            v.fail(f"the damper's plate is not between its guides when {place}")
        if place == "shut" and not (lo[1] <= my0 + 1e-9 and hi[1] >= my1):
            v.fail("the damper does not cover the ash mouth when shut")
        if place == "half" and (abs(lo[1] - (my0 + my1) / 2) > 1e-9 or hi[1] < my1):
            v.fail("the half damper does not cover the mouth's upper half")
        if place == "open" and lo[1] < my1 - 1e-9:
            v.fail("the open damper does not clear the ash mouth")
    print(f"damper: open, half and shut in its recess between its guides over the ash mouth ({my0:g}..{my1:g})")


def check_outlet(v):
    """The outlet: on the flue end block's top in the west column, beside the drive's row, never under it, the hopper's
    column or the infeed's (the v2 clash); open, its bore clear to the sky from the cavity at every angle but for the cap;
    capped, a flat iron plate flush in the curb's seat over the bore; the curb the lining's brick, its rim and ledge."""
    m = v.m
    oc = m.OUTLET_CELL
    if (oc[0], oc[2]) in {(c[0], c[2]) for c in m.DRIVE_CELLS} or (oc[0], oc[2]) == (m.INFEED_CELL[0], m.INFEED_CELL[2]):
        v.fail("the outlet is under the drive's row or the infeed")
    if (oc[0], oc[1] + 1, oc[2]) != m.CHIMNEY_CELL or oc[1] != m.BODY_Y - 1:
        v.fail("the outlet is not a top cell under the chimney's")
    (ux, uz) = m.UPTAKE
    if not (oc[0] * 16 <= ux[0] and ux[1] <= oc[0] * 16 + 16 and oc[2] * 16 <= uz[0] and uz[1] <= oc[2] * 16 + 16):
        v.fail("the uptake's bore is not under the outlet cell's top")
    cap = aabb_of(v.by_part["outletcap"])
    (cx, cz) = m.CAP_SEAT
    if any(abs(a - b) > 1e-6 for a, b in zip(cap[0] + cap[1], [cx[0], m.CAP_Y[0], cz[0], cx[1], m.CAP_Y[1], cz[1]])):
        v.fail(f"the cap is not flush in its seat: {cap}")
    if not (cap[0][0] < ux[0] + 1e-9 and cap[1][0] > ux[1] - 1e-9 and cap[0][2] < uz[0] and cap[1][2] > uz[1]):
        v.fail("the cap does not cover the bore")
    lining = v.by_part["lining"]
    (kx, kz) = m.CURB
    rim = [(x + 0.25, m.TOP - 0.25, z + 0.25) for x in range(int(kx[0]), int(kx[1])) for z in range(int(kz[0]), int(kz[1]))
           if not (cx[0] <= x + 0.25 <= cx[1] and cz[0] <= z + 0.25 <= cz[1])]
    ledge = [(x + 0.25, m.CAP_Y[0] - 0.1, z + 0.25) for x in range(int(cx[0]), int(cx[1])) for z in range(int(cz[0]), int(cz[1]))
             if not (ux[0] <= x + 0.25 <= ux[1] and uz[0] <= z + 0.25 <= uz[1])]
    missing = [p for p in rim + ledge if not any(inside(p, e) for e in lining)]
    if missing:
        v.fail(f"the curb is not the lining's brick at {len(missing)} points, e.g. {missing[:3]}")
    col_lo, col_hi = [ux[0], m.CAVITY_TOP - 0.5, uz[0]], [ux[1], (m.CHIMNEY_CELL[1] + 1) * 16, uz[1]]
    for th in thetas(12):
        for pid in v.by_part:
            if pid == "outletcap" or (th and pid not in m.MOVING):
                continue
            for e in v.posed(pid, th):
                lo, hi = e.aabb()
                if all(lo[q] < col_hi[q] - 0.02 and col_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the uptake or over the outlet")
    print(f"outlet: cell {list(oc)} beside the drive's row, under the chimney's {list(m.CHIMNEY_CELL)}; the bore clear from the "
          f"cavity to the sky but for the cap; the cap flush in the curb's seat; the curb the lining's ({len(rim) + len(ledge)} points)")


def check_draught(v):
    """The way the air goes, natural draught only: in at the ash mouth into the ash pit, up between the step grate's
    treads, through the fire, over the bridge, along the hearth under the
    arch, into the flue end's cavity and up the uptake to the outlet. Sampled points along it lie in no element of the
    structure or of the grate drawn."""
    m = v.m
    (mx, my) = m.MOUTH
    xm, ym = (mx[0] + mx[1]) / 2, (my[0] + my[1]) / 2
    path = [(xm, ym, z) for z in (-0.5, 0.5, 1.5, 3.0, 5.0, 7.0, 10.0, 14.0)]
    grate = [(24.0, a[2] - m.STEP_T - (m.STEP_DROP - m.STEP_T) / 2, a[1]) for a in m.steps()[:-1]]   # the riser gaps
    over = [(24.0, 19.0, z) for z in (10.0, 15.0)] + [(24.0, 21.0, 22.5)]
    hearth = [(24.0, 20.0, z) for z in range(26, 74, 3)] + [(24.0, 24.0, 70.0), (12.0, 25.0, 71.0)]
    (ux, uz) = m.UPTAKE
    up = [((ux[0] + ux[1]) / 2, y + 0.5, (uz[0] + uz[1]) / 2) for y in range(int(m.CAVITY_TOP), int(m.TOP) + 20)]
    blocked = []
    sc = scene()
    solid = [e for pid in STRUCTURE_PARTS + ("stoker", "firedoor", "hopper") if v.shows(pid, sc) for e in v.by_part[pid]]
    for p in path + grate + over + hearth + up:
        if any(inside(p, e) for e in solid):
            blocked.append(p)
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    print(f"draught: in at the ash mouth, up between the step grate's treads ({len(grate)} riser gaps), over the bridge, "
          f"along the hearth, up the uptake to the outlet: {len(path) + len(grate) + len(over) + len(hearth) + len(up)} points clear")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = m.shown(v.parts, [e for pid in v.by_part for e in v.posed(pid, pose[0])], pose)
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose[0]:.2f} {pose[1]}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_textures(v)
    check_stages(v)
    check_containment(v)
    check_sides(v)
    check_binding(v)
    check_box_frame(v)
    check_slivers(v)
    check_lining(v)
    check_build(v)
    check_path(v)
    check_firing(v)
    check_doors(v)
    check_supports(v)
    check_gearing(v)
    check_feedroll(v)
    check_reverse(v)
    check_chutes(v)
    check_water(v)
    check_damper(v)
    check_outlet(v)
    check_draught(v)
    check_clearances(v, 24, "clearances")
    if not quick:
        check_clearances(v, 144, "swept paths (every 5 degrees of the shaft over the rabbles' two turns)")
        check_zfight(v)
    return v.ok


# ---------------------------------------------------------------- the files
def game_texture(root, code):
    rel = code.split(":", 1)[1] + ".png"
    for domain in ("survival", "game"):
        f = root / "assets" / domain / "textures" / rel
        if f.is_file():
            return f
    return None


def check_bricks(m, shape, ship, fail):
    """Any fired brick: the rig's bricks are the crucible furnace's (every type of the game's burnedbrick, each with its
    block and textures); the shape's own `brick` is the default type's colour layer; with the game at hand, every texture
    of the mapping and of the shape is a file there."""
    bricks = ship["bricks"]
    types = [t for t, *_ in m.BRICKS]
    if list(bricks["types"]) != types or bricks["default"] not in types or bricks["code"] != "brick":
        fail("the rig's bricks are not the furnace's types")
    d = bricks["types"][bricks["default"]]
    if shape["textures"]["brick"] != (d.get("overlays") or [d["base"]])[-1]:
        fail("the shape's brick is not the default type's colour")
    root = game_root()
    if root is None:
        print("bricks: SKIPPED the game's textures (no $VINTAGE_STORY or ~/Games/vintagestory)")
        return
    paths = [c for t in bricks["types"].values() for c in [t["base"]] + t.get("overlays", [])] + list(shape["textures"].values())
    missing = [c for c in paths if game_texture(root, c) is None]
    if missing:
        fail(f"textures that are not the game's: {missing}")
    print(f"bricks: {len(types)} types; their textures and the shape's {len(shape['textures'])} are files in {root}")


def validate_files(m, shape, frame_shape, ship):
    ok = True

    def fail(msg):
        nonlocal ok
        ok = False
        print("FAIL", msg)
    check_bricks(m, shape, ship, fail)
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    missing = used - set(shape["textures"])
    if missing:
        fail(f"textures used but not declared: {missing}")
    if {f["texture"].lstrip("#") for e in frame_shape["elements"] for f in e["faces"].values()} != {"iron"}:
        fail("the frame's shape is not the iron frame alone")
    for e in shape["elements"]:
        want = m.FIRE_GLOW if e["name"].startswith("fire_bed") else None
        if {f.get("glow") for f in e["faces"].values()} != {want}:
            fail(f"{e['name']}'s faces glow wrongly")
    gaps = lid_gaps(ship["cells"])
    empty = [c["pos"] for c in ship["cells"] if not c.get("boxes")]
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, {len(empty)} "
          f"with no boxes ({empty}); decks over every column: {'yes' if not gaps else gaps}")
    if gaps:
        fail("a column has no deck")
    cells = [c["pos"] for c in ship["cells"]]
    for key in m.CELL_ANCHORS:
        if ship[key] not in cells:
            fail(f"the {key} is not one of the cells")
    chim = [m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3)]
    if chim in cells:
        fail("the chimney's cell is the furnace's")
    line = ship["chimney"]
    out = ship["outletCell"]
    if (abs(line["origin"][1] - line["length"] / 2 - m.BODY_Y) > 1e-9 or line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY
            or not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1)):
        fail("the chimney's line does not stand on the outlet")
    if "airCell" in ship or any(k.startswith("return") for k in ship):
        fail("a natural-draught furnace has no air port, and its water is used up (no return)")
    return ok
