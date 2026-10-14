"""The Parkes kettle generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved. Many checks are the crucible furnace's (its validator is the template),
adapted; the gearing, the supports and the reverse checks are the parting furnace's v2 branch's.
"""

from __future__ import annotations

import math
import os
import re
from pathlib import Path

from machinegen.checks import bearing_margin, cells_touched, coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply as _apply
from machinegen.rigmath import driver_input, part_of, posed

TAU = 2 * math.pi


class V:
    def __init__(self, m, els, parts, rig):
        self.m, self.els, self.parts, self.rig = m, els, parts, rig
        self.by_part = {}
        for el in els:
            self.by_part.setdefault(el.part, []).append(el)
        self.cache = {}
        self.ok = True

    def fail(self, msg):
        self.ok = False
        print("FAIL", msg)

    def mat(self, pid, theta=0.0, depth=0.0):
        return self.m.pm(self.parts, pid, (theta, depth, None))

    def posed(self, pid, theta=0.0, depth=0.0):
        if pid not in self.m.MOVING:
            theta, depth = 0.0, 0.0
        key = (pid, round(theta, 9), round(depth, 9))
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.mat(pid, theta, depth)
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def named(self, pid, rx, theta=0.0, depth=0.0):
        r = re.compile(rx)
        return [e for e in self.posed(pid, theta, depth) if r.search(e.name)]

    def shown(self, scene, theta=0.0, depth=0.0):
        """Every element drawn in `scene`, posed."""
        return [e for pid in self.by_part if self.m.on_show(pid, scene) for e in self.posed(pid, theta, depth)]


def inside(el, p, eps=0.0):
    """Whether point p lies inside element el (its box, turned), by more than eps."""
    d = [p[k] - el.c[k] for k in range(3)]
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        if abs(sum(d[i] * axis[i] for i in range(3))) > abs(el.size[k]) / 2 - eps:
            return False
    return True


def depths(step):
    n = int(round(1 / step))
    return [i * step for i in range(n + 1)]


def thetas(n, sign=1):
    return [sign * TAU * i / n for i in range(n)]


def same_box(a, b, tol=1e-6):
    return max(abs(a[0][k] - b[0][k]) for k in range(3)) < tol and max(abs(a[1][k] - b[1][k]) for k in range(3)) < tol


def brick(v):
    """Every brick box, the brickwork's and the lining's."""
    return [e for e in v.by_part["brickwork"] + v.by_part["lining"] if e.name.startswith(("bk_brick_", "ln_brick_"))]


def static(v):
    """What stands still and holds the fire and the flame: the iron frame, the brickwork and the lining."""
    return v.by_part["frame"] + v.by_part["brickwork"] + v.by_part["lining"]


THIN_POST = 1.5                              # a post: a vertical element under this across both ways


# ---------------------------------------------------------------- parts, stages, textures
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
    moving = {p["id"] for p in v.parts if p["drivers"]}
    if moving != set(v.m.MOVING):
        v.fail(f"the moving parts are {sorted(moving)}, not {sorted(v.m.MOVING)}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}; " + ", ".join(f"{p} {n}" for p, n in counts.items()))


def check_stages(v):
    """The build: at each stage (the iron frame alone, with the brickwork, the lining, the kettle and its doors, the
    stirrer with its gallows and drive) nothing floats: every element drawn is joined to the ground. The iron frame
    stands on its own: it is iron only, and no thin post stands in it taller than half the setting. Every tie is
    hidden once the brickwork is in."""
    m = v.m
    out = []
    for sid, fitted in m.STAGE_STATES:
        drawn = [e for p in v.parts if p["requires"] is None or p["requires"] in fitted for e in v.posed(p["id"])
                 if p["id"] in ("frame", "brickwork", "lining", "kettle", "firedoor", "gallows", "drive", "stirrer")]
        seen, loose = frame_floating(drawn)
        out.append(f"{sid} {len(seen)}")
        if loose:
            v.fail(f"at the {sid} stage, elements float: {loose[:8]}")
    frame = v.by_part["frame"]
    tex = {f["texture"].lstrip("#") for e in frame for f in e.faces.values()}
    if tex != {"iron"}:
        v.fail(f"the iron frame is not iron only: {sorted(tex)}")
    posts, tallest = [], 0.0
    for e in frame:
        lo, hi = e.aabb()
        if hi[0] - lo[0] < THIN_POST and hi[2] - lo[2] < THIN_POST:
            tallest = max(tallest, hi[1] - lo[1])
            if hi[1] - lo[1] > m.H / 2:
                posts.append(e.name)
    if posts:
        v.fail(f"thin posts stand in the iron frame: {posts}")
    ties = [e for e in frame if e.name.startswith("fr_tie")]
    bk = v.by_part["brickwork"]
    bare = 0
    for t in ties:
        others = [e for e in frame if e is not t]
        lo, hi = t.aabb()
        for k in range(3):
            for side in (lo[k] - 0.02, hi[k] + 0.02):
                a, b = [q for q in range(3) if q != k]
                for fa in (0.1, 0.5, 0.9):
                    for fb in (0.1, 0.5, 0.9):
                        pnt = [0.0, 0.0, 0.0]
                        pnt[k] = side
                        pnt[a] = lo[a] + (hi[a] - lo[a]) * fa
                        pnt[b] = lo[b] + (hi[b] - lo[b]) * fb
                        if pnt[1] < 0.0 or any(inside(e, pnt) for e in others):
                            continue
                        if not any(inside(e, pnt) for e in bk):
                            bare += 1
    print(f"stages: nothing floats at any stage (elements joined to the ground: {', '.join(out)}); the iron frame iron "
          f"only, with no thin post over {m.H / 2:.0f} (its tallest thin piece {tallest:.1f}); {len(ties)} ties hidden in "
          f"the brickwork ({bare} points off their faces bare)")
    if bare:
        v.fail(f"{bare} points off the ties' faces are in no brickwork or iron: a tie shows")


ROLES = [
    (r"^bk_brick_", {"brick"}),
    (r"^ln_brick_", {"lining"}),
    (r"^fr_", {"iron"}),
    (r"^kt_", {"iron"}),
    (r"^fd_", {"iron"}),
    (r"^lead\d_", {"lead"}),
    (r"^zinc_", {"zinc"}),
    (r"^crust_", {"crust"}),
    (r"^fuel\d_", {"fuel"}),
    (r"^fire\d_", {"ember"}),
    (r"^damper(open|half|shut)_", {"iron"}),
    (r"^outletcap_", {"iron"}),
    (r"^gl_", {"iron"}),
    (r"^dr_oak", {"oak"}),
    (r"^dr_", {"steel"}),
    (r"^st_paddle", {"iron"}),
    (r"^st_", {"steel"}),
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


# ---------------------------------------------------------------- containment, the top, the sides
def overhang(m, el):
    """How far (voxels) an element pokes out of the setting's box and the cells over it that are the machine's;
    negative when inside. Over the setting, an element reaching (more than 0.01) into a cell that is not the
    machine's counts as poking out by how far it reaches into it: by its box when it is square to the axes, else by
    points through its volume (a turned box's bounds overstate it)."""
    lo, hi = el.aabb()
    worst = max(-lo[0], hi[0] - m.W, -lo[1], -lo[2], hi[2] - m.D)
    if hi[1] <= m.H:
        return worst
    mine = set(m.UPPER_CELLS + m.HOLLOW_CELLS)
    if not any(c[1] >= m.CELLS_Y and c not in mine for c in cells_touched(lo, hi)):
        return worst
    if all(el.local_axis_for(k) is not None for k in range(3)):
        pts = [lo, hi]
        boxes = [(lo, hi)]
    else:
        n = 8
        pts = []
        for i in range(n + 1):
            for j in range(n + 1):
                for k in range(n + 1):
                    loc = [(i / n - 0.5) * el.size[0], (j / n - 0.5) * el.size[1], (k / n - 0.5) * el.size[2]]
                    pts.append([el.c[q] + sum(el.r[q][s] * loc[s] for s in range(3)) for q in range(3)])
        boxes = [(p, p) for p in pts]
    for blo, bhi in boxes:
        for c in cells_touched(blo, bhi):
            if c[1] >= m.CELLS_Y and c not in mine:
                worst = max(worst, min(bhi[0] - c[0] * m.B, (c[0] + 1) * m.B - blo[0], bhi[2] - c[2] * m.B,
                                       (c[2] + 1) * m.B - blo[2], bhi[1] - c[1] * m.B))
    return worst


def check_containment(v):
    """Nothing leaves the setting and the cells over it that are the machine's (the gallows' and the drive's, and the
    hollow ones over the kettle's mouth) over a turn of the axle either way and the fire door's whole lift, so nothing
    reaches the chimney's cell or the free cell beside it; over the floor only the gallows, the drive, the stirrer and
    the flange's proud top stand."""
    m = v.m
    worst, where = -1e9, None
    over = set()
    poses = [(th, 0.0) for th in thetas(24) + thetas(24, -1)] + [(0.0, dp) for dp in depths(0.05)]
    for th, dp in poses:
        for pid in v.by_part:
            if pid not in m.MOVING and (th, dp) != (0.0, 0.0):
                continue
            for el in v.posed(pid, th, dp):
                lo, hi = el.aabb()
                o = overhang(m, el)
                if o > worst:
                    worst, where = o, (el.name, th, dp)
                if hi[1] > m.H + 1e-4:
                    over.add(pid)
    flange = max(e.aabb()[1][1] for e in v.by_part["kettle"]) - m.H
    print(f"containment over a turn either way and the fire door's lift: worst overhang {worst:.3f} voxels ({where[0]} at "
          f"theta {where[1]:.2f}, depth {where[2]:.2f}); over the floor: {sorted(over)} (the flange {flange:.2f} proud)")
    if worst > 0.01:
        v.fail(f"{where[0]} leaves the machine's cells at theta {where[1]}, depth {where[2]}")
    if over - {"gallows", "drive", "stirrer", "kettle"}:
        v.fail(f"over the floor stand {sorted(over - {'gallows', 'drive', 'stirrer', 'kettle'})}")
    if abs(flange - m.FLANGE_PROUD) > 1e-6:
        v.fail(f"the kettle stands {flange:.3f} over the floor, not its flange's {m.FLANGE_PROUD}")


def check_top(v):
    """The floor is flat: every half voxel of the top, but the kettle's mouth (inside the flange) and the outlet's cap
    seat, is brick whose top is the floor, or the flange (FLANGE_PROUD over it), sampled just under the floor."""
    m = v.m
    solid = static(v)
    flange = [e for e in v.by_part["kettle"] if e.name.startswith("kt_flange")]
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    bad, n, on_flange = [], 0, 0
    x = 0.25
    while x < m.W:
        z = 0.25
        while z < m.D:
            if not (cx0 + m.CURB_RIM <= x <= cx1 - m.CURB_RIM and cz0 + m.CURB_RIM <= z <= cz1 - m.CURB_RIM) and \
                    not octagon_has(m.KX, m.KZ, m.RI, x, z):
                n += 1
                p = (x, m.FLOOR_TOP - 0.05, z)
                if any(inside(e, p) for e in flange):
                    on_flange += 1
                else:
                    under = [e for e in solid if inside(e, p)]
                    if not under or any(abs(e.aabb()[1][1] - m.FLOOR_TOP) > 1e-6 for e in under):
                        bad.append((x, z))
            z += 0.5
        x += 0.5
    print(f"top: flat at the floor at {n - len(bad)} of {n} points (every half voxel; the kettle's mouth and the outlet "
          f"aside), {on_flange} of them the flange's")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")


def check_sides(v):
    """Kettles tile into a row: each side face is flush, brick over its whole area (sampled every half voxel) but for
    the binding's side legs (the corner angles' and the side top bands') set flush in it, and nothing but brick and those angles reaches either side plane, of any
    part in any pose; two side by side (one moved a setting's width along x) touch nowhere but at their shared walls,
    the stirrers turning."""
    m = v.m
    flush = v.by_part["brickwork"] + [e for e in v.by_part["frame"] if e.name.startswith(("fr_angle_", "fr_band_"))]
    bad, n = [], 0
    for x in (0.05, m.W - 0.05):
        y = 0.25
        while y < m.H:
            z = 0.25
            while z < m.D:
                n += 1
                if not any(inside(e, (x, y, z)) for e in flush):
                    bad.append((x, y, z))
                z += 0.5
            y += 0.5
    if bad:
        v.fail(f"the side faces are not brick and the binding at {len(bad)} of {n} points, e.g. {bad[:4]}")
    reach = set()
    for th, dp in [(t, 0.0) for t in thetas(12)] + [(0.0, d) for d in depths(0.25)]:
        for pid in v.by_part:
            for e in v.posed(pid, th, dp):
                lo, hi = e.aabb()
                if lo[0] < -1e-6 or hi[0] > m.W + 1e-6:
                    reach.add(e.name)
                elif (lo[0] < 1e-6 or hi[0] > m.W - 1e-6) and not e.name.startswith(("bk_brick", "fr_angle_", "fr_band_")):
                    reach.add(e.name)
    if reach:
        v.fail(f"not brick at a side plane: {sorted(reach)[:4]}")
    hits = 0
    for th in (0.0, 0.7, 2.1):
        a = [e for pid in v.by_part for e in v.posed(pid, th, 1.0)]
        near_a = [e for e in a if e.aabb()[1][0] > m.W - 2.0]
        near_b = []
        for e in a:
            if e.aabb()[0][0] < 2.0:
                c = e.clone()
                c.c = [c.c[0] + m.W, c.c[1], c.c[2]]
                near_b.append(c)
        for ea in near_a:
            for eb in near_b:
                alo, ahi = ea.aabb()
                blo, bhi = eb.aabb()
                if all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)) and obb_obb(ea, eb):
                    hits += 1
    if hits:
        v.fail(f"two kettles side by side run into each other ({hits} pairs)")
    print(f"sides: flush brick or binding at {n - len(bad)} of {n} points on the two side faces; nothing else "
          f"reaches them; two kettles side by side clear of each other but at the shared walls")


def check_binding(v):
    """The binding is angle iron (the owner's rule for brick structures): no flat strap or band. Each vertical corner
    of the setting has an angle from the ground to the floor, two legs at right angles, each BIND_T thick and BIND_LEG
    from the corner, flush with its face; the brick behind each leg's inside face and at its inside corner (sampled up
    its height), so the legs bear on the corner's two brick faces. Along the top edge of the front and of the back an
    angle band wraps the edge (a leg down the face, a leg on the top, flush with the floor), the brick behind both its
    inside faces and at its inside corner, and its ends meet the corner angles' face legs."""
    m = v.m
    frame = v.by_part["frame"]
    bricks = brick(v)
    t, leg = m.BIND_T, m.BIND_LEG
    binders = [e for e in frame if e.name.startswith(("fr_angle_", "fr_band_"))]
    if any(re.match(r"^fr_(strap|kerb)", e.name) for e in frame):
        v.fail("a flat strap or band is left in the binding")

    def is_brick(p):
        return any(inside(e, p) for e in bricks)

    def iron(p):
        return any(inside(e, p, eps=-1e-6) for e in binders)
    bad, pts = [], 0
    ys = [0.5 + 1.0 * k for k in range(int(m.H)) if 0.5 + k < m.H - t]   # under the top bands, which cap the corners
    for tag, xc, zc, sx, sz in m.CORNERS:
        els = [e for e in frame if e.name.startswith(f"fr_angle_{tag}_")]
        lo, hi = aabb_of(els)
        want = ([min(xc, xc + sx * leg), 0.0, min(zc, zc + sz * leg)], [max(xc, xc + sx * leg), m.H, max(zc, zc + sz * leg)])
        if not same_box((lo, hi), want, tol=0.05):
            v.fail(f"the {tag} corner's angle is not {leg} each way from the corner, ground to floor: {lo}..{hi}")
        for y in ys:
            for u in (0.25, t - 0.25, t + 0.25, leg - 0.25):
                # the section: iron along both legs, and nothing beyond their thickness
                for p, want_iron in (((xc + sx * u, y, zc + sz * 0.5 * t), True), ((xc + sx * 0.5 * t, y, zc + sz * u), True),
                                     ((xc + sx * (t + 0.25), y, zc + sz * (t + 0.25)), False)):
                    pts += 1
                    if iron(p) != want_iron:
                        bad.append((tag, "section", p))
            for u in (t + 0.25, 0.5 * (t + leg), leg - 0.25):
                for p in ((xc + sx * u, y, zc + sz * (t + 0.05)), (xc + sx * (t + 0.05), y, zc + sz * u)):
                    pts += 1
                    if not is_brick(p):
                        bad.append((tag, "leg's inside face", p))
            pts += 1
            if not is_brick((xc + sx * (t + 0.05), y, zc + sz * (t + 0.05))):
                bad.append((tag, "inside corner", (xc, y, zc)))
    # the top bands, one along the top edge of every face: (face, the leg down the face's axis, its plane, inward)
    for face, ax, c, sgn in (("n", 2, 0.0, 1), ("s", 2, m.D, -1), ("w", 0, 0.0, 1), ("e", 0, m.W, -1)):
        al = 0 if ax == 2 else 2                                  # the band's long axis
        length = m.W if ax == 2 else m.D
        els = [e for e in frame if e.name.startswith(f"fr_band_{face}_")]
        if not els:
            v.fail(f"no top band along the {face} face's top edge")
            continue
        lo, hi = aabb_of(els)
        want_lo, want_hi = [0.0, m.H - leg, 0.0], [0.0, m.H, 0.0]
        want_lo[ax], want_hi[ax] = min(c, c + sgn * leg), max(c, c + sgn * leg)
        want_lo[al], want_hi[al] = (t, length - t) if ax == 2 else (leg, length - leg)
        if not same_box((lo, hi), (want_lo, want_hi), tol=0.05):
            v.fail(f"the {face} top band does not wrap the top edge from corner to corner: {lo}..{hi}")

        def at(along, y, inward):
            p = [0.0, y, 0.0]
            p[al], p[ax] = along, c + sgn * inward
            return tuple(p)
        for x in [leg + 0.5 + 2.0 * k for k in range(int((length - 2 * leg) / 2.0))]:
            for u in (0.25, t - 0.25):
                for p in (at(x, m.H - leg + 0.25, u), at(x, m.H - u, leg - 0.25)):
                    pts += 1
                    if not iron(p):
                        bad.append((face, "band's section", p))
            for p in (at(x, m.H - 0.5 * (leg + t), t + 0.05), at(x, m.H - t - 0.05, 0.5 * (t + leg)),
                      at(x, m.H - t - 0.05, t + 0.05)):
                pts += 1
                if not is_brick(p):
                    bad.append((face, "band's inside face", p))
    joints, jpts = check_binding_joints(v, binders)
    pts += jpts
    bad += joints
    if bad:
        v.fail(f"the binding is not a closed angle-iron frame bearing on the brick at {len(bad)} of {pts} points, e.g. {bad[:4]}")
    print(f"binding: angle iron only ({len(binders)} pieces, legs {t} thick and {leg} long): an angle at each of the 4 "
          f"vertical corners, ground to floor, and an angle band along the top edge of every face; every band end on an "
          f"upright's head, every corner's top iron all over, one connected frame; the brick behind every leg and at "
          f"every inside corner ({pts} points)")


def check_binding_joints(v, binders):
    """The binding is one closed frame, the owner's rule that members meet (no gaps, no floating ends). Every end face
    of every top band's leg lies on iron over its whole section (sampled just past it), and at each end of each band
    at least one of its legs lands on an upright (a corner angle); the top of each corner is iron all over, leg by
    leg (the bands run onto the uprights' heads, so the joint is closed); and the pieces, joined wherever two touch
    over an area, are one connected frame."""
    m = v.m
    t, leg = m.BIND_T, m.BIND_LEG
    uprights = [e for e in binders if e.name.startswith("fr_angle_")]
    bad, pts = [], 0

    def on(p, els):
        return any(inside(e, p, eps=-1e-6) for e in els)
    for face in ("n", "s", "w", "e"):
        legs = {}
        for e in binders:
            mt = re.match(rf"^fr_band_{face}_([ab])", e.name)
            if mt:
                legs.setdefault(mt.group(1), []).append(e)
        if sorted(legs) != ["a", "b"]:
            bad.append((face, "no angle band (two legs) along the top edge"))
            continue
        for end in (0, 1):
            landed = False
            for which, els in sorted(legs.items()):
                lo, hi = aabb_of(els)
                al = max(range(3), key=lambda k: hi[k] - lo[k])
                across = [k for k in range(3) if k != al]
                x = (lo[al] - 0.05) if end == 0 else (hi[al] + 0.05)
                samples = []
                for f0 in (0.1, 0.5, 0.9):
                    for f1 in (0.1, 0.5, 0.9):
                        p = [0.0, 0.0, 0.0]
                        p[al] = x
                        p[across[0]] = lo[across[0]] + f0 * (hi[across[0]] - lo[across[0]])
                        p[across[1]] = lo[across[1]] + f1 * (hi[across[1]] - lo[across[1]])
                        samples.append(tuple(p))
                pts += len(samples)
                if not all(on(p, binders) for p in samples):
                    bad.append((face, which, "band end on no iron", end))
                if all(on(p, uprights) for p in samples):
                    landed = True
            pts += 1
            if not landed:
                bad.append((face, "band end on no upright", end))
    for tag, xc, zc, sx, sz in m.CORNERS:
        for u in (0.25, 0.5 * leg, leg - 0.25):
            for w in (0.25, 0.5 * leg, leg - 0.25):
                pts += 1
                if not on((xc + sx * u, m.H - 0.1, zc + sz * w), binders):
                    bad.append((tag, "corner's top not closed", (u, w)))
    # one connected frame: two pieces join where they overlap or touch over an area (not an edge or a point)
    boxes = [e.aabb() for e in binders]

    def joined(a, b):
        gaps = [min(a[1][k], b[1][k]) - max(a[0][k], b[0][k]) for k in range(3)]
        return all(g > -1e-6 for g in gaps) and sum(g > 0.2 for g in gaps) >= 2
    seen, todo = {0}, [0]
    while todo:
        i = todo.pop()
        for j in range(len(boxes)):
            if j not in seen and joined(boxes[i], boxes[j]):
                seen.add(j)
                todo.append(j)
    pts += 1
    if len(seen) != len(boxes):
        bad.append(("frame", "not one connected frame", sorted(binders[j].name for j in range(len(boxes)) if j not in seen)[:4]))
    return bad, pts


def check_walls(v):
    """Everything centred, thick walls: the kettle, the ash mouth, the fire door and its recess are centred left to
    right; the brick round the setting's inside, sampled every half voxel from the grate's room to the seat along the
    west, east and front walls (and the back outside the flue's way), is solid (brick, or iron set into it) out to the
    setting's face for at least MIN_WALL."""
    m = v.m
    mid = m.W / 2
    for what, (x0, x1) in (("kettle", (m.KX - m.RF, m.KX + m.RF)), ("ash mouth", m.MOUTH[0]), ("fire door", m.FIRE_DOOR[0]),
                           ("fire door's recess", m.F_RECESS[0]), ("cavity", m.CAVITY[0]), ("iron front", m.FRONT[0])):
        if abs((x0 + x1) / 2 - mid) > 1e-9:
            v.fail(f"the {what} is not centred left to right")
    near = brick(v) + [e for e in v.by_part["frame"] if e.name.startswith(("fr_angle", "fr_band", "fr_front", "fr_tie",
                                                                              "fr_guide", "fr_rail", "fr_bearer"))]

    def solid(p):
        return any(inside(e, p, eps=-1e-9) for e in near)

    def run(p, axis, sign, limit):
        t = 0.0
        while t < limit:
            q = list(p)
            q[axis] += sign * (t + 0.125)
            if not solid(q):
                return t
            t += 0.25
        return limit
    (cx0, cx1), (cy0, cy1), (cz0, cz1) = m.CAVITY
    walls = {}
    ys = [cy0 + 0.25 + 0.5 * k for k in range(int((cy1 - cy0) * 2))]
    (fx0, fx1), (fy0, fy1) = m.FIRE_DOOR
    for y in ys:
        for z in [cz0 + 0.25 + 0.5 * k for k in range(int((cz1 - cz0) * 2))]:
            walls.setdefault("west", []).append((run((cx0, y, z), 0, -1, cx0), (y, z)))
            walls.setdefault("east", []).append((run((cx1, y, z), 0, 1, m.W - cx1), (y, z)))
        for x in [cx0 + 0.25 + 0.5 * k for k in range(int((cx1 - cx0) * 2))]:
            if not (fx0 - 0.5 <= x <= fx1 + 0.5 and fy0 - 0.5 <= y <= fy1 + 0.5):
                walls.setdefault("front", []).append((run((x, y, cz0), 2, -1, cz0), (x, y)))
            if not (m.FLUE_OPENING[0][0] - 0.5 <= x and m.FLUE_OPENING[1][0] - 3.5 <= y):
                walls.setdefault("back", []).append((run((x, y, cz1), 2, 1, m.D - cz1), (x, y)))
    worst = {k: min(w) for k, w in walls.items()}
    print("walls: the kettle, the ash mouth, the fire door and the setting's inside centred; brick round the inside at "
          "least " + ", ".join(f"{k} {t:.2f}" for k, (t, _) in worst.items()) + f" (MIN_WALL {m.MIN_WALL})")
    for k, (t, at) in worst.items():
        if t < m.MIN_WALL - 1e-9:
            v.fail(f"the {k} wall is {t:.2f} thick at {at}")


def what_is(v, p):
    """What lies at p: "ln" (the lining's brick), "bk" (the brickwork's), "iron" (the frame), or None (open)."""
    for e in v.by_part["lining"]:
        if inside(e, p):
            return "ln"
    for e in v.by_part["brickwork"]:
        if inside(e, p):
            return "bk"
    if any(inside(e, p) for e in v.by_part["frame"]):
        return "iron"
    return None


def face_samples(lo, hi, k, sign, step=1.0):
    """Points on the face of box (lo, hi) normal to axis k on the `sign` side, every `step`, a quarter in from its
    edges."""
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


def check_lining(v):
    """The lining: on the hot faces (the setting's inside, from the grate up to the seat, and every face of the flue
    opening, the back flue and the stack that brick stands behind) the brick is the lining's for at least LINING_T
    from the face out, unless the brick ends sooner; the ash pit's and the ash mouth's faces are the brickwork's."""
    m = v.m
    t = m.LINING_T
    hot = []
    for (x0, x1), (y0, y1), (z0, z1) in (m.CAVITY, m.FLUE_OPENING, m.BACK_FLUE, m.STACK):
        hot.append(([x0, y0, z0], [x1, y1, z1]))
    thinnest, where, n = 1e9, None, 0
    for lo, hi in hot:
        for k in range(3):
            for sign in (-1, 1):
                if k == 1 and lo is hot[0][0]:
                    continue                                   # the cavity's floor is the grate, its top the seat
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
                        w = what_is(v, q) if 0.0 < q[k] < (m.W, m.H, m.D)[k] else None
                        if w != "ln":
                            if w == "bk" and d - 0.125 < t - 1e-6 and d - 0.125 < thinnest:
                                thinnest, where = d - 0.125, pnt
                            break
                        d += 0.25
    cold, wrong = 0, []
    (mx0, mx1), (my0, my1) = m.MOUTH
    for (lo, hi) in ((m.CAVE[0][0], m.CAVE[1][0], m.CAVE[2][0]), (m.CAVE[0][1], m.CAVE[1][1], m.CAVE[2][1])), \
                    ((mx0, my0, 2.0), (mx1, my1, m.CAVE[2][0])):
        for k in range(3):
            for sign in (-1, 1):
                for pnt in face_samples(lo, hi, k, sign, step=2.0):
                    q = list(pnt)
                    q[k] += sign * 0.125
                    w = what_is(v, q)
                    if w in ("ln", "bk"):
                        cold += 1
                        if w == "ln":
                            wrong.append(q)
    if thinnest < 1e9:
        v.fail(f"the lining is {thinnest:.2f} thick at {where}")
    if wrong:
        v.fail(f"the ash pit or the ash mouth is lined at {len(wrong)} points, e.g. {wrong[:3]}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or "
          f"the lining reaching through; the ash pit and the ash mouth the brickwork's ({cold} points)")


def check_build(v):
    """The stages' counts: the brickwork's and the lining's volumes, measured from their elements (each lattice box
    in exactly one brick box), give the rig's quantities at the game's rates, rounded up; the stages are the build's,
    each a part's requires; the kettle and the stirrer stages fit their parts with them."""
    m = v.m
    xs, ys, zs, mat = m.brick_lattice()
    lat = {"bk": 0.0, "ln": 0.0}
    for (i, j, k), w in mat.items():
        if w:
            lat[w] += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]) * (zs[k + 1] - zs[k])
    els = {w: sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in brick(v) if e.name.startswith(f"{w}_brick_"))
           for w in ("bk", "ln")}
    for w in ("bk", "ln"):
        if abs(lat[w] - els[w]) > 0.05:
            v.fail(f"the {w} boxes hold {els[w]:.2f} cubic voxels, the lattice {lat[w]:.2f}")
    c = m.build_counts(m.build_frame())
    build = v.rig["build"]
    st = {s["requires"]: s for s in build["stages"]}
    got = {"bricks": st["brickwork"]["materials"][0]["quantity"], "lining": st["lining"]["materials"][0]["quantity"],
           "fireclay": st["lining"]["materials"][1]["quantity"]}
    want = {"bricks": math.ceil(lat["bk"] / m.B ** 3 * m.BRICKS_PER_BLOCK - 1e-9),
            "lining": math.ceil(lat["ln"] / m.B ** 3 * m.LINING_PER_BLOCK - 1e-9)}
    want["fireclay"] = math.ceil(want["lining"] / m.LINING_PER_FIRECLAY - 1e-9)
    if got != want or any(got[k] != c[k] for k in got):
        v.fail(f"the stages' counts {got} are not the measured {want}")
    if [s["requires"] for s in build["stages"]] != list(m.STAGES):
        v.fail(f"the stages are not {m.STAGES}")
    if st["brickwork"]["materials"][0]["code"] != "game:burnedbrick-*" or not st["brickwork"].get("oneType"):
        v.fail("the brickwork does not take one type of any fired brick")
    requires = {p["requires"] for p in v.parts}
    if not set(m.STAGES) <= requires or any(f"damper{d}" not in requires for d in m.DAMPERS):
        v.fail("a stage's requires is no part's")
    if st["kettle"].get("alsoParts") != ["firedoor"] or st["stirrer"].get("alsoParts") != ["gallows", "drive"]:
        v.fail("the kettle stage does not fit the fire door, or the stirrer stage the gallows and the drive")
    for pid, stage in (("firedoor", "kettle"), ("gallows", "stirrer"), ("drive", "stirrer")):
        if next(p for p in v.parts if p["id"] == pid)["requires"] != stage:
            v.fail(f"{pid} is not fitted with the {stage} stage")
    if [(x["code"], x["quantity"]) for x in build["frame"]["ingredients"]] != list(m.FRAME_RECIPE):
        v.fail("the frame's recipe is not the generator's")
    iron = {pid: sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in v.by_part[pid]) / m.B ** 3
            for pid in ("kettle", "firedoor", "gallows", "drive", "stirrer")}
    print(f"build: brickwork {lat['bk'] / m.B ** 3:.2f} blocks -> {got['bricks']} fired bricks; lining "
          f"{lat['ln'] / m.B ** 3:.2f} blocks -> {got['lining']} refractory bricks and {got['fireclay']} fire clay; the "
          f"kettle stage {dict(m.KETTLE_STAGE)} (its iron {iron['kettle']:.3f} blocks, the fire door's "
          f"{iron['firedoor']:.3f}); the stirrer stage {dict(m.STIRRER_STAGE)} (the gallows {iron['gallows']:.3f}, the "
          f"drive {iron['drive']:.3f}, the stirrer {iron['stirrer']:.3f} blocks of metal, boxes summed); the frame "
          f"{dict(m.FRAME_RECIPE)}")


# ---------------------------------------------------------------- the kettle
def octagon_has(cx, cz, a, x, z):
    dx, dz = abs(x - cx), abs(z - cz)
    return dx <= a and dz <= a and (dx + dz) <= a * math.sqrt(2.0)


def ray_hits(el, origin, d, length):
    """Whether the ray origin + t d, 0 <= t <= length, passes through element el (the slab test in its own axes)."""
    lo_t, hi_t = 0.0, length
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        o = sum((origin[i] - el.c[i]) * axis[i] for i in range(3))
        dd = sum(d[i] * axis[i] for i in range(3))
        h = abs(el.size[k]) / 2
        if abs(dd) < 1e-12:
            if abs(o) > h:
                return False
            continue
        t0, t1 = (-h - o) / dd, (h - o) / dd
        if t0 > t1:
            t0, t1 = t1, t0
        lo_t, hi_t = max(lo_t, t0), min(hi_t, t1)
        if lo_t > hi_t:
            return False
    return True


def walls_inner(v):
    """The bowl's wall facets as (point on the inner face, inward normal, the azimuth's unit vector), measured from
    the elements: each kt_wall element's thin axis is its normal."""
    m = v.m
    out = []
    for e in v.by_part["kettle"]:
        if not e.name.startswith("kt_wall"):
            continue
        k = min(range(3), key=lambda q: abs(e.size[q]))
        n = [e.r[i][k] for i in range(3)]
        to_axis = [m.KX - e.c[0], 0.0, m.KZ - e.c[2]]
        if sum(n[i] * to_axis[i] for i in range(3)) < 0:
            n = [-x for x in n]
        q = [e.c[i] + n[i] * abs(e.size[k]) / 2 for i in range(3)]
        h = math.hypot(n[0], n[2])
        u = [-n[0] / h, 0.0, -n[2] / h]
        out.append((q, n, u))
    return out


def measured_apothem(v, faces, y):
    """The bowl's inside at height y, from its facets' inner faces: the nearest one's distance from the axis along its
    own azimuth (they are all alike), 0 under the bottom's top."""
    m = v.m
    bottom = max(e.aabb()[1][1] for e in v.by_part["kettle"] if e.name.startswith("kt_bottom"))
    if y < bottom:
        return 0.0
    best = 1e9
    for q, n, u in faces:
        # the point (KX, y, KZ) + s u on the plane through q with normal n
        num = sum((q[i] - (m.KX, y, m.KZ)[i]) * n[i] for i in range(3))
        den = sum(u[i] * n[i] for i in range(3))
        best = min(best, num / den)
    return best


def measured_volume(v, faces, y1, dy=0.01):
    bottom = max(e.aabb()[1][1] for e in v.by_part["kettle"] if e.name.startswith("kt_bottom"))
    vol, y = 0.0, bottom + dy / 2
    while y < y1:
        vol += v.m.oct_area(measured_apothem(v, faces, y)) * min(dy, y1 - (y - dy / 2))
        y += dy
    return vol


def check_kettle(v):
    """The kettle: its bowl closed (points just inside its wall, every few degrees round and down, and over its
    bottom, all in its elements); each level of the bath holds its quarter of a charge, measured from the facets'
    inner faces (within 2 %), the full level at least a voxel under the rim; the bath, the zinc and the crust inside
    the bowl; the flange FLANGE_PROUD over the floor, its inner edge the bowl's rim, lying on the seat's brick all
    round; the seat's opening holding the bowl's outer octagon and inside the flange's; the bowl clear of the setting's
    walls; the flange clear of the chimney's cell behind; the bowl's bottom over the fire."""
    m = v.m
    kettle = v.by_part["kettle"]
    # closed: every ray from the rim's centre down and out (every 1.5 degrees down to straight down, every 2 round)
    # passes through an element of the bowl before it leaves the setting's inside
    holes, rays = [], 0
    centre = (m.KX, m.RIM_Y, m.KZ)
    for ei in range(1, 61):
        e = math.radians(ei * 1.5)
        for ai in range(0, 180):
            a = math.radians(ai * 2.0 + 0.7)
            d = (math.cos(e) * math.cos(a), -math.sin(e), math.cos(e) * math.sin(a))
            rays += 1
            if not any(ray_hits(k, centre, d, 20.0) for k in kettle):
                holes.append((ei * 1.5, ai * 2.0))
    if holes:
        v.fail(f"{len(holes)} of {rays} rays from the rim's centre leave the bowl through no element, e.g. {holes[:4]}")
    faces = walls_inner(v)
    held = []
    for n, level in enumerate(m.BATH_LEVELS, 1):
        units = measured_volume(v, faces, level) / m.VOXELS_PER_UNIT
        want = m.CHARGE_UNITS * n / m.LEVELS
        held.append(units)
        if abs(units - want) > 0.02 * want:
            v.fail(f"lead{n}'s level {level} holds {units:.0f} units, not {want:.0f}")
        top = max(e.aabb()[1][1] for e in v.by_part[f"lead{n}"])
        if abs(top - level) > 1e-6:
            v.fail(f"lead{n}'s surface is at {top}, not its level {level}")
    if m.RIM_Y - m.FULL < 1.0:
        v.fail(f"the full bath stands {m.RIM_Y - m.FULL:.2f} under the rim")
    # contents inside the bowl: their points (corners pulled in a little) inside the measured inside
    escapes = []
    for pid in [f"lead{n}" for n in range(1, m.LEVELS + 1)] + ["zinc", "crust"]:
        for e in v.by_part[pid]:
            if "_lump" in e.name:
                continue
            for c in e.corners():
                if not octagon_has(m.KX, m.KZ, measured_apothem(v, faces, min(c[1], m.RIM_Y)) + 0.6, c[0], c[2]):
                    escapes.append(e.name)
    if escapes:
        v.fail(f"contents outside the bowl: {sorted(set(escapes))[:4]}")
    flange = [e for e in kettle if e.name.startswith("kt_flange")]
    lo, hi = aabb_of(flange)
    if abs(hi[1] - (m.FLOOR_TOP + m.FLANGE_PROUD)) > 1e-6 or abs(lo[1] - m.RIM_Y) > 1e-6:
        v.fail(f"the flange is not from the rim to {m.FLANGE_PROUD} over the floor: y {lo[1]}..{hi[1]}")
    # lying on the seat's brick all round: points under its outer rim (just inside its edge) are brick
    seat = brick(v)
    unsupported = 0
    for ai in range(0, 360, 5):
        a = math.radians(ai)
        r = (m.RF - 0.2) / math.cos((a + math.pi / 8) % (math.pi / 4) - math.pi / 8)
        p = (m.KX + r * math.cos(a), m.RIM_Y + 0.5, m.KZ + r * math.sin(a))
        if not any(inside(e, p) for e in seat):
            unsupported += 1
    if unsupported:
        v.fail(f"the flange lies on no brick at {unsupported} of 72 points round its edge")
    # the seat's opening: the bowl's outer octagon inside it, it inside the flange's octagon
    rects = m.MOUTH_RECTS

    def in_opening(x, z):
        return any(abs(x - m.KX) <= hx and abs(z - m.KZ) <= hz for hx, hz in rects)
    tight = 1e9
    for ai in range(0, 3600, 5):
        a = math.radians(ai / 10)
        sec = 1 / math.cos((a + math.pi / 8) % (math.pi / 4) - math.pi / 8)
        r = m.outer_apothem(m.RIM_Y) * sec
        if not in_opening(m.KX + r * math.cos(a), m.KZ + r * math.sin(a)):
            v.fail(f"the bowl's top is outside the seat's opening at {ai / 10} degrees")
            break
    for hx, hz in rects:
        for sx in (-1, 1):
            for sz in (-1, 1):
                x, z = m.KX + sx * hx, m.KZ + sz * hz
                tight = min(tight, (m.RF * math.sqrt(2) - (hx + hz)) / math.sqrt(2), m.RF - max(hx, hz))
                if not octagon_has(m.KX, m.KZ, m.RF, x, z):
                    v.fail(f"the seat's opening reaches past the flange at {(x, z)}")
    # the bowl clear of the setting's inside walls (below the seat), its bottom over the fire
    bowl = [e for e in kettle if not e.name.startswith("kt_flange")]
    blo, bhi = aabb_of(bowl)
    (cx0, cx1), _, (cz0, cz1) = m.CAVITY
    room = min(blo[0] - cx0, cx1 - bhi[0], blo[2] - cz0, cz1 - bhi[2])
    if room < 0.3:
        v.fail(f"the bowl comes {room:.2f} from the setting's inside walls")
    fuel_top = max(e.aabb()[1][1] for j in (1, 2) for e in v.by_part[f"fuel{j}"])
    if blo[1] - fuel_top < 2.0:
        v.fail(f"the bowl's bottom is {blo[1] - fuel_top:.2f} over the coal")
    reach = max((c[2] for e in flange for c in e.corners() if c[0] > m.CHIMNEY_CELL[0] * m.B), default=0.0)
    if reach > m.CHIMNEY_CELL[2] * m.B - 0.25:
        v.fail(f"the flange reaches z {reach:.2f} under the chimney's cell")
    print(f"kettle: the bowl closed ({rays} rays from the rim's centre all meet it); the bath's levels {', '.join(str(x) for x in m.BATH_LEVELS)} hold "
          f"{', '.join(f'{u:.0f}' for u in held)} units (a charge {m.CHARGE_UNITS} at {m.VOXELS_PER_UNIT} cubic voxels a "
          f"unit, measured from the facets), the full bath {m.RIM_Y - m.FULL:.2f} under the rim; the contents inside the "
          f"bowl; the flange {m.FLANGE_PROUD} proud, on the seat all round; the seat's opening ({len(rects)} steps) holds "
          f"the bowl's top and stays {tight:.2f} inside the flange; the bowl {room:.2f} off the setting's walls and "
          f"{blo[1] - fuel_top:.2f} over the coal; the flange's back edge at z {reach:.2f} under the chimney's cell")


# ---------------------------------------------------------------- the stirrer and its drive
def check_gearing(v):
    """The mitre pair: both axes pass through the cone apex, the pitch radii are in the teeth's ratio, and the pitch
    point (where the pitch cones touch at their outer ends) moves alike on both (finite differences of the posed rig,
    both ways round), so they roll without slipping: the stirrer turns the rig's ratio of the axle."""
    m = v.m
    ap = m.APEX
    if abs(m.AXLE[0] - ap[0]) > 1e-9 or abs(m.AXLE[1] - ap[1]) > 1e-9 or abs(m.KX - ap[0]) > 1e-9 or abs(m.KZ - ap[2]) > 1e-9:
        v.fail("the mitre pair's axes do not cross at its apex")
    if abs(m.R_P / m.R_W - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-9 or abs(abs(m.STIR_RATIO) - m.PINION_TEETH / m.WHEEL_TEETH) > 1e-9:
        v.fail("the stirrer's ratio is not the pair's teeth")
    p = [ap[0], ap[1] - m.R_P, ap[2] + m.R_W]       # the wheel under the apex, the pinion behind it (+z)
    worst = 0.0
    for th in (0.0, 0.4, 1.9, -2.7, 5.3, 11.0):
        def moved(pid):
            ma, mb = v.mat(pid, th), v.mat(pid, th + 1e-4)
            r = [row[:3] for row in ma[:3]]
            t = [ma[i][3] * 16 for i in range(3)]
            loc = [sum(r[j][i] * (p[j] - t[j]) for j in range(3)) for i in range(3)]
            return [sum(mb[i][j] * loc[j] for j in range(3)) + mb[i][3] * 16 - p[i] for i in range(3)]
        da, db = moved("drive"), moved("stirrer")
        size = max(math.sqrt(sum(x * x for x in da)), 1e-12)
        err = math.sqrt(sum((da[i] - db[i]) ** 2 for i in range(3))) / size
        worst = max(worst, err)
        if err > 1e-3 or size < 1e-6:
            v.fail(f"the mitre pair slips at theta {th}: pinion {da}, wheel {db}")
    print(f"gearing: a mitre pair, {m.PINION_TEETH} to {m.WHEEL_TEETH} (module {m.GEAR_MOD}), axes crossing at "
          f"{[round(c, 2) for c in ap]}, the pitch point moving alike on both (worst {worst:.1e}): the stirrer turns "
          f"{m.STIR_RATIO} of the axle")


def check_supports(v):
    """The drive shaft in two bearings (the back pedestal's and the gallows' middle boss) and the stirrer's shaft in
    two (the gallows' lower and upper bosses), each enclosing its shaft at rest and turned 45 degrees; the stirrer
    hung by its collar on the upper boss; the axle's oak continuation reaching the power face."""
    m = v.m
    gal = v.by_part["gallows"]
    for what, pid, rx, axis, c, want in (
            ("the drive shaft", "drive", r"^dr_shaft", 2, (m.AXLE[0], m.AXLE[1]), {"gl_ped_bearing", "gl_boss_middle"}),
            ("the stirrer's shaft", "stirrer", r"^st_shaft", 1, (m.KX, m.KZ), {"gl_boss_lower", "gl_boss_upper"})):
        shaft = v.named(pid, rx)
        found, _ = shaft_supports(gal, shaft, axis, c)
        # the bosses and the beams they are cast on (the shaft runs through both where the boss is)
        if not want <= set(found) or any(n not in want and not re.match(r"^gl_(lower|middle|upper)\d*$", n) for n in found):
            v.fail(f"{what} is carried by {sorted(found)}, not {sorted(want)}")
        for th in (0.0, math.pi / 4):
            els = v.named(pid, rx, th)
            for b in [g for g in gal if g.name in want]:
                mg = bearing_margin(b, els, axis)
                if not 0 < mg < 1e8:
                    v.fail(f"{b.name} does not enclose {what} at theta {th:.2f} (margin {mg:.3f})")
        print(f"support {what}: {', '.join(sorted(found))}")
    collar = aabb_of(v.named("stirrer", r"^st_collar"))
    boss = next(g for g in gal if g.name == "gl_boss_upper").aabb()
    if not (0.0 <= collar[0][1] - boss[1][1] <= 0.1):
        v.fail(f"the stirrer's collar is {collar[0][1] - boss[1][1]:.3f} over its boss: it does not hang from it")
    oak = aabb_of(v.named("drive", r"^dr_oak"))
    if abs(oak[1][2] - m.D) > 1e-9 or abs((oak[0][0] + oak[1][0]) / 2 - m.AXLE[0]) > 1e-9 or \
            abs((oak[0][1] + oak[1][1]) / 2 - m.AXLE[1]) > 1e-9:
        v.fail("the axle's oak continuation does not meet the power face at its centre")


def check_reverse(v):
    """Flat paddles stir alike either way, so no rectifier: no driver reads the shaft's travel or anything else that
    knows the direction; the paddles lie symmetric about the stirrer's axis; the clearances hold turning backwards
    (check_clearances turns both ways)."""
    m = v.m
    for p in v.parts:
        for d in p["drivers"]:
            if d["type"] in ("rotate", "swing", "slide") and (driver_input(d) != "theta" or d.get("rectified")):
                v.fail(f"reverse: {p['id']} reads {driver_input(d)}, which knows the direction")
    for e in v.named("stirrer", r"^st_paddle"):
        lo, hi = e.aabb()
        if max(abs((lo[0] + hi[0]) / 2 - m.KX), abs((lo[2] + hi[2]) / 2 - m.KZ)) > 1e-6:
            v.fail(f"reverse: {e.name} is not symmetric about the stirrer's axis")
    print("reverse: every driver reads theta alone; the paddles symmetric about the stirrer's axis")


def check_paddles(v):
    """The paddles reach into the bath at a quarter full (the load follows the charge: they stir what is there) and
    break the full bath's surface; the zinc floats beyond their reach, inside the bowl."""
    m = v.m
    paddles = v.named("stirrer", r"^st_paddle")
    lo, hi = aabb_of(paddles)
    if lo[1] > m.BATH_LEVELS[0] - 1.0:
        v.fail(f"the paddles' foot ({lo[1]:.2f}) does not reach a voxel into the quarter-full bath ({m.BATH_LEVELS[0]})")
    if hi[1] < m.FULL + 0.2:
        v.fail(f"the paddles do not break the full bath's surface ({hi[1]:.2f} against {m.FULL})")
    reach = max(math.hypot(c[0] - m.KX, c[2] - m.KZ) for e in paddles for c in e.corners())
    zinc = min(math.hypot(c[0] - m.KX, c[2] - m.KZ) for e in v.by_part["zinc"] for c in e.corners())
    if zinc - reach < 0.3:
        v.fail(f"the zinc floats {zinc - reach:.2f} beyond the paddles' reach")
    print(f"paddles: from {lo[1]:.2f} (a quarter full is {m.BATH_LEVELS[0]}) to {hi[1]:.2f} (full {m.FULL}), reaching "
          f"{reach:.2f} from the axis; the zinc from {zinc:.2f}")


# ---------------------------------------------------------------- doors, the outlet, the draught
def check_doors(v):
    """The fire door: shut over its whole opening, lifted open clear of it, between its guides and in its recess at
    every depth. The ash-pit damper: in each of its three places a plate in its recess on its rails; shut it covers
    the whole ash mouth, half its east half, open none of it."""
    m = v.m
    (fx0, fx1), (fy0, fy1) = m.FIRE_DOOR
    (rx, ry, rz) = m.F_RECESS
    gx = (m.F_GUIDES[0][1], m.F_GUIDES[1][0])
    for dp in depths(0.05):
        els = v.posed("firedoor", 0.0, dp)
        lo, hi = aabb_of(els)
        if lo[0] < gx[0] - 1e-9 or hi[0] > gx[1] + 1e-9 or lo[1] < ry[0] - 1e-9 or hi[1] > ry[1] + 1e-9 or \
                lo[2] < rz[0] - 1e-9 or hi[2] > rz[1] + 1e-9:
            v.fail(f"the fire door leaves its recess at depth {dp}")
    plate = lambda dp: next(e for e in v.posed("firedoor", 0.0, dp) if e.name == "fd_plate").aabb()  # noqa: E731
    shut, opened = plate(0.0), plate(1.0)
    if not (shut[0][0] <= fx0 and shut[1][0] >= fx1 and shut[0][1] <= fy0 and shut[1][1] >= fy1):
        v.fail("the fire door does not cover its opening when shut")
    if opened[0][1] < fy1 - 1e-9:
        v.fail(f"the fire door open ({opened[0][1]:.2f}) does not clear its opening ({fy1})")
    print(f"fire door: shut over its opening, lifted {m.FD_LIFT} to clear it ({opened[0][1]:.2f} over {fy1}), in its "
          f"recess between its guides over the whole lift")
    (mx0, mx1), (my0, my1) = m.MOUTH
    (dx, dy, dz) = m.D_RECESS
    for place in m.DAMPERS:
        els = v.by_part[f"damper{place}"]
        pl = next(e for e in els if e.name.endswith("_plate")).aabb()
        allo, alhi = aabb_of(els)
        if allo[0] < dx[0] - 1e-9 or alhi[0] > dx[1] + 1e-9 or allo[2] < dz[0] - 1e-9 or alhi[2] > dz[1] + 1e-9 or \
                abs(pl[0][1] - m.D_RAILS[0][1]) > 1e-9 or pl[1][1] > m.D_RAILS[1][0]:
            v.fail(f"the damper is not in its recess on its rails when {place}")
        cover = max(0.0, min(pl[1][0], mx1) - max(pl[0][0], mx0)) / (mx1 - mx0)
        want = {"shut": 1.0, "half": 0.5, "open": 0.0}[place]
        if abs(cover - want) > 1e-9 or pl[0][1] > my0 + 1e-9 or pl[1][1] < my1 - 1e-9:
            v.fail(f"the damper {place} covers {cover:.2f} of the ash mouth, not {want}")
        print(f"damper {place}: covers {cover:.2f} of the ash mouth's width, its full height; on its rails in its recess")


def check_outlet(v):
    """The outlet: capped, a flat iron plate flush with the floor in the curb's seat, covering the stack's mouth; the
    curb the lining's brick, a rim round the cap and a ledge under its edge; the stack in the outlet's cell, under
    the chimney's."""
    m = v.m
    cap = aabb_of(v.by_part["outletcap"])
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    r = m.CURB_RIM
    if not same_box(cap, ([cx0 + r, m.CAP_Y[0], cz0 + r], [cx1 - r, m.FLOOR_TOP, cz1 - r])):
        v.fail(f"the cap is not flush in the curb's seat: {cap}")
    lining = [e for e in v.by_part["lining"] if e.name.startswith("ln_brick_")]
    (sx0, sx1), _, (sz0, sz1) = m.STACK
    xs = [cx0 + 0.25 + k for k in range(int(cx1 - cx0))]
    zs = [cz0 + 0.25 + k for k in range(int(cz1 - cz0))]
    rim = [(x, m.FLOOR_TOP - 0.25, z) for x in xs for z in zs
           if not (cx0 + r <= x <= cx1 - r and cz0 + r <= z <= cz1 - r)]
    ledge = [(x, m.CAP_Y[0] - 0.1, z) for x in xs for z in zs
             if cx0 + r <= x <= cx1 - r and cz0 + r <= z <= cz1 - r and not (sx0 <= x <= sx1 and sz0 <= z <= sz1)]
    missing = [p for p in rim + ledge if not any(inside(e, p) for e in lining)]
    if missing:
        v.fail(f"the curb is not the lining's brick at {len(missing)} points, e.g. {missing[:3]}")
    if not (cap[0][0] < sx0 and cap[1][0] > sx1 and cap[0][2] < sz0 and cap[1][2] > sz1):
        v.fail("the cap does not cover the stack's mouth")
    cell = tuple(int(c // m.B) for c in ((cx0 + cx1) / 2, m.FLOOR_TOP - 1, (cz0 + cz1) / 2))
    if cell != m.outlet_cell() or (cell[0], cell[1] + 1, cell[2]) != m.CHIMNEY_CELL or \
            not (cx0 >= cell[0] * m.B and cx1 <= (cell[0] + 1) * m.B and cz0 >= cell[2] * m.B and cz1 <= (cell[2] + 1) * m.B):
        v.fail("the outlet's curb is not in the cell under the chimney's")
    print(f"outlet: the cap a plate flush in the curb's seat over the stack's mouth; the curb the lining's brick "
          f"({len(rim) + len(ledge)} points), in the outlet's cell {list(cell)} under the chimney's")


def empty(v, p):
    return not any(inside(e, p) for e in static(v))


def check_draught(v):
    """The way the air goes, natural draught only: in at the ash mouth into the ash pit, up between the firebars,
    through the fire round the kettle's bowl, out at the flue opening in the setting's back wall, along the back flue
    and up the stack to the outlet. Sampled points along it lie in no element of the brick or the frame, and the
    stack and the outlet's mouth in no element at all in any pose but the cap. Every firebar lies on a bearer at each
    end, the bearers built into the walls."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    xm, ym = (mx0 + mx1) / 2, (my0 + my1) / 2
    (cx0, cx1), (cy0, cy1), (cz0, cz1) = m.CAVE
    path = [(xm, ym, z) for z in (-0.5, 0.5, 1.5) + tuple(range(3, int(cz0) + 1, 2))]
    path += [(x + 0.5, (cy0 + cy1) / 2, m.KZ) for x in range(int(cx0), int(cx1), 2)]
    bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_bar") and e.name.endswith("w")),
                  key=lambda b: b[0][2])
    for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
        gz = (ahi[2] + blo[2]) / 2
        path += [(cx0 + (cx1 - cx0) * f, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz) for f in (0.15, 0.3, 0.4, 0.6, 0.7, 0.85)]
    # round the bowl: up the setting's inside at its corners, past the kettle, to the flue opening
    (kx0, kx1), (ky0, ky1), (kz0, kz1) = m.CAVITY
    for x, z in ((kx0 + 1.0, kz0 + 1.0), (kx1 - 1.0, kz0 + 1.0), (kx0 + 1.0, kz1 - 1.0), (kx1 - 1.0, kz1 - 1.0)):
        path += [(x, y + 0.5, z) for y in range(int(ky0), int(ky1) - 1)]
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = m.FLUE_OPENING
    fx, fy = (fx0 + fx1) / 2, (fy0 + fy1) / 2
    (bx0, bx1), (by0, by1), (bz0, bz1) = m.BACK_FLUE
    (sx0, sx1), (sy0, _), (sz0, sz1) = m.STACK
    sx, sz = (sx0 + sx1) / 2, (sz0 + sz1) / 2
    path += [(fx, fy, kz1 - 0.5)] + [(fx, fy, fz0 + 0.5 + q) for q in range(int(fz1 - fz0))]
    path += [((bx0 + bx1) / 2, (by0 + by1) / 2, bz0 + 0.5 + q) for q in range(int(bz1 - bz0))]
    path += [(bx0 + 0.5 + q, (by0 + by1) / 2, sz) for q in range(int(bx1 - bx0))]
    path += [(sx, sy0 + 0.5 + q * 0.5, sz) for q in range(int((m.FLOOR_TOP + m.B - sy0) * 2) - 1)]
    if not (fx0 >= bx0 and fx1 <= bx1 and fy0 >= by0 and fy1 <= by1 and abs(fz1 - bz0) < 1e-9 and sx0 >= bx0 and
            sx1 <= bx1 and sz0 >= bz0 and sz1 <= bz1 and sy0 <= by0 and fy1 <= m.SEAT_Y):
        v.fail("the flue opening, the back flue and the stack do not run into each other under the seat")
    blocked = [p for p in path if not empty(v, p)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    kettle_in = [p for p in path if any(inside(e, p) for e in v.by_part["kettle"])]
    if kettle_in:
        v.fail(f"the kettle stands in the draught's way at {kettle_in[:4]}")
    mouth_lo, mouth_hi = [sx0, sy0, sz0], [sx1, m.FLOOR_TOP + m.B, sz1]
    for th, dp in [(t, 0.0) for t in thetas(8)] + [(0.0, 1.0)]:
        for pid in v.by_part:
            if pid == "outletcap":
                continue
            for e in v.posed(pid, th, dp):
                lo, hi = e.aabb()
                if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the stack or over the outlet")
    frame = v.by_part["frame"]
    bearers = [e.aabb() for e in frame if e.name.startswith("fr_bearer")]
    for e in frame:
        if not e.name.startswith("fr_bar"):
            continue
        lo, hi = e.aabb()
        under = [b for b in bearers if b[0][0] < hi[0] and b[1][0] > lo[0]]
        if len(under) != 2 or any(abs(lo[1] - b[1][1]) > 1e-9 or not (b[0][2] <= lo[2] and b[1][2] >= hi[2]) for b in under):
            v.fail(f"{e.name} does not lie on a bearer at each end")
    for blo, bhi in bearers:
        xc = (blo[0] + bhi[0]) / 2
        for z in (m.GRATE[2][0] - 0.5, m.GRATE[2][1] + 0.5):
            for y in (bhi[1] + 0.2, blo[1] - 0.2):
                if not any(inside(e, (xc, y, z)) for e in brick(v)):
                    v.fail(f"a bearer is not built into the wall at x {xc}, z {z}")
    gap = (m.GRATE[2][1] - m.GRATE[2][0] - m.BARS * m.BAR_W) / (m.BARS - 1)
    if gap < 0.8:
        v.fail(f"the firebars' gaps ({gap:.2f}) are too narrow for the draught")
    print(f"draught: natural, in at the ash mouth, up through {m.BARS - 1} gaps of {gap:.2f} between the firebars, round "
          f"the bowl, out at the flue opening ({fy0}..{fy1}, under the seat at {m.SEAT_Y}), along the back flue to the "
          f"stack and the outlet: {len(path)} points clear; the stack's mouth clear in every pose but for the cap; every "
          f"firebar on a bearer at each end, the bearers built into the walls")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a regex, element regex in a or None, part b regex, element regex in b or None).
ALLOWED = [
    (r"stirrer", r"^st_shaft", r"gallows", r"^gl_(boss_(lower|upper)|lower|upper)"),
    (r"drive", r"^dr_shaft", r"gallows", r"^gl_(ped_bearing|boss_middle|middle)"),
    (r"stirrer", r"^st_(shaft|paddle|hub)", r"lead\d|crust", None),
    (r"kettle", r"^kt_flange", r"lining", r"^ln_brick"),
    (r"lead\d|crust", None, r"kettle", r"^kt_wall"),
    (r"zinc", None, r"lead4", None),
]


def allowed(pa, pb, ea, eb):
    for xa, ra, xb, rb in ALLOWED:
        for (p1, e1, p2, e2) in ((pa, ea, pb, eb), (pb, eb, pa, ea)):
            if re.fullmatch(xa, p1) and re.fullmatch(xb, p2) and (ra is None or re.search(ra, e1.name)) and \
                    (rb is None or re.search(rb, e2.name)):
                return True
    ra, rb = re.fullmatch(r"(fuel|fire)(\d)", pa), re.fullmatch(r"(fuel|fire)(\d)", pb)
    if not (ra and rb and ra.group(1) == rb.group(1)):
        return False
    ja, jb = int(ra.group(2)), int(rb.group(2))
    lower = ea if ja < jb else eb
    return abs(ja - jb) == 1 and "_lump" in lower.name


def touches(items, movers=None):
    """Pairs of elements of different parts that overlap; with `movers` (part ids), only pairs with one of them."""
    hits = {}
    for x in range(len(items)):
        pa, ea, (alo, ahi) = items[x]
        for y in range(x + 1, len(items)):
            pb, eb, (blo, bhi) = items[y]
            if pa == pb or (movers is not None and pa not in movers and pb not in movers):
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, pb, ea, eb):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def scenes(m):
    """The machine built in each state of the kettle (the fire lit, the damper open, the outlet open), the fire's
    other states and the damper's other places with the outlet capped; and each stage of the build before it is."""
    out = [(k[0], "lit", "open", False, "complete") for k in m.KETTLE_STATES]
    out += [("empty", "laid", "half", True, "complete"), ("full", "low", "shut", True, "complete"),
            ("empty", "cold", "open", False, "frame"), ("empty", "cold", "open", False, "brickwork"),
            ("empty", "cold", "open", True, "lining"), ("zinc", "lit", "open", True, "kettle")]
    return out


def check_clearances(v, n_theta, step):
    """In every scene at rest nothing of one part runs into another's (intended contacts aside: the shafts in their
    bearings, the paddles in the bath and the crust, the flange bedded in its seat, the bath's edge in the bowl's wall,
    the zinc half sunk, a lump of coal in the layer over it); the drive and the stirrer, turned through a turn either
    way, touch nothing they should not (the teeth of the mitre pair among them); the fire door, lifted every `step`,
    touches nothing."""
    m = v.m
    allhits = {}
    for scene in scenes(m):
        items = [(e.part, e, e.aabb()) for e in v.shown(scene)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: (scene[0], scene[4], 0.0, 0.0) for h in hs})
    # the turning parts, in the busiest scene, both ways round
    scene = ("zinc", "lit", "open", True, "complete")
    zone_lo, zone_hi = [12.0, 18.0, 6.0], [36.0, 48.0, 48.0]
    still = [(e.part, e, e.aabb()) for e in v.shown(scene) if e.part not in ("drive", "stirrer")
             and all(e.aabb()[0][q] < zone_hi[q] and e.aabb()[1][q] > zone_lo[q] for q in range(3))]
    for th in thetas(n_theta) + thetas(n_theta, -1)[1:]:
        items = still + [(e.part, e, e.aabb()) for pid in ("drive", "stirrer") for e in v.posed(pid, th)]
        for key, hs in touches(items, movers={"drive", "stirrer"}).items():
            allhits.setdefault(key, {}).update({h: (scene[0], scene[4], th, 0.0) for h in hs})
    front = [(e.part, e, e.aabb()) for e in v.shown(scene) if e.part != "firedoor" and e.aabb()[0][2] < 3.0]
    for dp in depths(step):
        items = front + [(e.part, e, e.aabb()) for e in v.posed("firedoor", 0.0, dp)]
        for key, hs in touches(items, movers={"firedoor"}).items():
            allhits.setdefault(key, {}).update({h: (scene[0], scene[4], 0.0, dp) for h in hs})
    print(f"clearances in {len(scenes(m))} scenes at rest, the drive and the stirrer turned every {360 / n_theta:g} "
          f"degrees either way, the fire door lifted every {step}: "
          f"{'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s})" for (a, b), s in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = m.shown(v.parts, [e for pid in v.by_part for e in v.posed(pid, pose[0], pose[1])], pose)
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose[:2]} {pose[2]}: {len(pairs)}, e.g. {pairs[:3]}")
    print(f"z-fighting: {bad} coplanar overlapping face pairs over {len(m.coplanar_poses())} poses")
    if bad:
        v.fail("faces z-fight")


def validate(m, els, parts, rig, quick=False):
    v = V(m, els, parts, rig)
    check_basic(v)
    check_stages(v)
    check_textures(v)
    check_containment(v)
    check_top(v)
    check_sides(v)
    check_binding(v)
    check_walls(v)
    check_lining(v)
    check_build(v)
    check_kettle(v)
    check_gearing(v)
    check_supports(v)
    check_reverse(v)
    check_paddles(v)
    check_doors(v)
    check_outlet(v)
    check_draught(v)
    check_clearances(v, 12 if quick else 48, 0.1 if quick else 0.02)
    if not quick:
        check_zfight(v)
    return v.ok


# ---------------------------------------------------------------- the files
def game_assets():
    """The game's assets folder: $VINTAGE_STORY/assets, else ~/Games/vintagestory/assets; None if neither is there."""
    for root in (os.environ.get("VINTAGE_STORY"), str(Path.home() / "Games" / "vintagestory")):
        if root and (Path(root) / "assets" / "survival").is_dir():
            return Path(root) / "assets"
    return None


def game_texture(assets, code):
    rel = code.split(":", 1)[1] + ".png"
    for domain in ("survival", "game"):
        f = assets / domain / "textures" / rel
        if f.is_file():
            return f
    return None


def game_brick_types(assets):
    text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
    found = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text)
    return re.findall(r'"([^"]+)"', found.group(1)) if found else []


def game_block_exists(assets, code):
    clay = assets / "survival" / "blocktypes" / "clay"
    if code == "game:claybricks-good-fire":
        text = (clay / "brick.json").read_text()
        return '"good"' in text and '"fire"' in text
    hit = re.fullmatch(r"game:brickcourse-four-running-(\w+)", code)
    if not hit:
        return False
    text = (clay / "brickcourse.json").read_text()
    colours = re.search(r'"code":\s*"color",\s*"states":\s*\[([^\]]*)\]', text)
    return bool(colours) and f'"{hit.group(1)}"' in colours.group(1) and '"four"' in text and '"running"' in text


def check_bricks(m, shape, ship, fail):
    """Any fired brick: the rig's bricks name every type of the game's burnedbrick, each with the block its bricks
    make and that block's textures; the shape's own `brick` is the default type's colour layer. With the game's assets
    at hand, every texture of the mapping and of the shape is a file there, every block one of the game's, and the
    types the game's."""
    bricks = ship["bricks"]
    types = [t for t, *_ in m.BRICKS]
    if list(bricks["types"]) != types or bricks["default"] not in types or bricks["code"] != "brick":
        fail("the rig's bricks are not the setting's types")
    d = bricks["types"][bricks["default"]]
    if shape["textures"]["brick"] != (d.get("overlays") or [d["base"]])[-1]:
        fail("the shape's brick is not the default type's colour")
    assets = game_assets()
    if assets is None:
        print("bricks: SKIPPED the game's assets (no $VINTAGE_STORY or ~/Games/vintagestory): the textures, blocks and "
              "types are not checked against the game")
        return
    paths = {t: [d["base"]] + d.get("overlays", []) for t, d in bricks["types"].items()}
    missing = [(t, c) for t, cs in paths.items() for c in cs if game_texture(assets, c) is None]
    missing += [("shape", c) for c in shape["textures"].values() if game_texture(assets, c) is None]
    if missing:
        fail(f"textures that are not the game's: {missing}")
    blocks = [d["block"] for d in bricks["types"].values() if not game_block_exists(assets, d["block"])]
    if blocks:
        fail(f"blocks that are not the game's: {blocks}")
    game = game_brick_types(assets)
    if sorted(game) != sorted(types):
        fail(f"the game's fired brick types are {game}, the rig's {types}")
    print(f"bricks: {len(types)} types, the game's burnedbrick types; each type's block one of the game's and its "
          f"{sum(len(c) for c in paths.values())} textures, and the shape's {len(shape['textures'])}, files in {assets}")


def validate_files(m, shape, frame_shape, ship):
    ok = True

    def fail(msg):
        nonlocal ok
        ok = False
        print("FAIL", msg)
    check_bricks(m, shape, ship, fail)
    if {f["texture"].lstrip("#") for e in frame_shape["elements"] for f in e["faces"].values()} != {"iron"}:
        fail("the frame's shape is not the iron frame alone")
    used = {f["texture"].lstrip("#") for e in shape["elements"] for f in e["faces"].values()}
    if used - set(shape["textures"]):
        fail(f"textures used but not declared: {used - set(shape['textures'])}")
    for e in shape["elements"]:
        want = m.FIRE_GLOW if re.fullmatch(r"fire\d", e["name"].split("_")[0]) else None
        got = {f.get("glow") for f in e["faces"].values()}
        if got != {want}:
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    if any(k.startswith("air") or k.startswith("water") or k.startswith("input") or k.startswith("output") for k in ship):
        fail("a batch kettle on natural draught has no air port, water or chute anchor")
    org = m.ORIGIN_CELL
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    sh = lambda c: tuple(c[k] - org[k] for k in range(3))  # noqa: E731
    hollow = {p for p, c in cells.items() if c.get("hollow")}
    if hollow != {sh(c) for c in m.HOLLOW_CELLS} or any(cells[p].get("boxes") for p in hollow):
        fail(f"the hollow cells are {sorted(hollow)}, not the kettle mouth's")
    setting_top = {sh((x, m.CELLS_Y - 1, z)) for x in range(m.CELLS_X) for z in range(m.CELLS_Z)}
    lids = {p for p, c in cells.items() if "lid" in c}
    if lids != setting_top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"decks on {sorted(lids)}, not at the floor on the setting's top cells")
    if any(not cells[p].get("boxes") for p in cells if p not in hollow):
        fail("a cell of the machine has no boxes")
    want = {sh(c) for c in m.footprint()}
    if set(cells) != want:
        fail(f"the cells are not the setting's, the gallows' and the kettle mouth's: {sorted(set(cells) ^ want)}")
    chim = sh(m.CHIMNEY_CELL)
    if chim in cells or any(sh(c) in cells for c in m.FREE_CELLS):
        fail("the chimney's cell or the free cell beside it is the machine's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up" or out not in cells:
        fail("the outlet is not a cell of the setting under the chimney's cell")
    line = ship["chimney"]
    if line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY or \
            abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9:
        fail("the chimney's line is not the minimum standing on the outlet")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    pc = tuple(ship["powerCell"])
    if pc not in cells or pc in hollow or ship["powerFace"] != "south" or pc[2] != m.CELLS_Z - 1 - org[2]:
        fail("the power cell is not a cell of the machine on its back face")
    elif (pc[0], pc[1], pc[2] + 1) in cells:
        fail("the power face is against another of the machine's cells")
    kc = tuple(ship["kettleCell"])
    mouth = ship["mouth"]["pos"]
    if ship["kettleFace"] != "up" or kc not in cells or kc[1] != m.CELLS_Y - 1 or \
            any(not (kc[k] <= mouth[k] <= kc[k] + 1) for k in range(3)):
        fail("the kettle's cell is not the setting's top cell under the kettle's mouth")
    for key, side in (("fireDoor", "fireDoorSide"), ("ash", "ashSide")):
        p = ship[key]["pos"]
        if ship[side] != "north" or abs(p[2]) > 1e-9 or not (0.0 <= p[0] <= 1.0 and 0.0 <= p[1] <= 1.0 + 1e-9):
            fail(f"the {key} is not on the controller cell's front face")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"{len(hollow)} hollow (over the kettle's mouth), decks on {len(lids)}; glow on the burning coal only; no air, "
          f"water or chute anchor; the power cell {list(pc)} south on the back face; the kettle's cell {list(kc)}; the "
          f"fire door and the ash mouth on the controller's front face; the outlet under the chimney's cell "
          f"{list(chim)}, the chimney {m.MIN_CHIMNEY} blocks from it up; the cell beside it free")
    return ok
