"""The stall roaster generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved. Many checks are the crucible furnace's
(CrucibleFurnace/tools/validate_cruciblefurnace.py), whose conventions the stall follows.
"""

from __future__ import annotations

import math
import os
import re
from pathlib import Path

from machinegen.checks import coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.geometry import aabb_of
from machinegen.rigmath import part_of, posed


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

    def posed(self, pid, theta=0.0, depth=0.0):
        key = (pid, theta, depth)
        if key not in self.cache:
            mm = self.m.pm(self.parts, pid, (theta, depth, None))
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def shown(self, scene):
        """Every element drawn in `scene`."""
        return [e for pid in self.by_part if self.m.on_show(pid, scene) for e in self.posed(pid)]


def inside(el, p, eps=0.0):
    """Whether point p lies inside element el (its box, turned), by more than eps."""
    d = [p[k] - el.c[k] for k in range(3)]
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        if abs(sum(d[i] * axis[i] for i in range(3))) > abs(el.size[k]) / 2 - eps:
            return False
    return True


def same_box(a, b, tol=1e-6):
    return max(abs(a[0][k] - b[0][k]) for k in range(3)) < tol and max(abs(a[1][k] - b[1][k]) for k in range(3)) < tol


def brick(v):
    """Every brick box, the brickwork's and the lining's."""
    return [e for e in v.by_part["brickwork"] + v.by_part["lining"] if e.name.startswith(("bk_brick_", "ln_brick_"))]


def static(v):
    """Everything that stands still and is not the bay's contents, the stall built: the iron frame, the brickwork and
    the lining."""
    return v.by_part["frame"] + v.by_part["brickwork"] + v.by_part["lining"]


THIN_POST = 1.5                              # a post: a vertical element under this across both ways (a strap is wider)


# ---------------------------------------------------------------- parts, frame, textures
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
    moving = [p["id"] for p in v.parts if p["drivers"] or p["ride"]]
    if moving:
        v.fail(f"parts that move, in a stall where nothing moves: {moving}")
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}, none moving; the iron frame {counts['frame']}; "
          + ", ".join(f"{p} {n}" for p, n in counts.items() if p != "frame"))


def check_stages(v):
    """The build: at each stage (the iron frame alone, with the brickwork, with the lining too) nothing floats: every
    element drawn is joined to the ground. The iron frame stands on its own: it is iron only, and no thin post stands
    in it taller than half the stall (a vertical element under THIN_POST across both ways; the jambs and the straps
    are angles and bands, wider)."""
    m = v.m
    out = []
    for sid, fitted in m.STAGE_STATES:
        drawn = [e for p in v.parts if p["requires"] is None or p["requires"] in fitted for e in v.by_part[p["id"]]]
        seen, loose = frame_floating(drawn)
        out.append(f"{sid} {len(seen)}")
        if loose:
            v.fail(f"at the {sid} stage, elements float: {loose[:8]}")
    frame = v.by_part["frame"]
    tex = {f["texture"].lstrip("#") for e in frame for f in e.faces.values()}
    if tex != {"iron"}:
        v.fail(f"the iron frame is not iron only: {sorted(tex)}")
    posts = []
    for e in frame:
        lo, hi = e.aabb()
        if hi[1] - lo[1] > m.H / 2 and hi[0] - lo[0] < THIN_POST and hi[2] - lo[2] < THIN_POST:
            posts.append(e.name)
    if posts:
        v.fail(f"thin posts stand in the iron frame: {posts}")
    print(f"stages: nothing floats at any stage (elements joined to the ground: {', '.join(out)}); the iron frame iron "
          f"only, with no thin post over {m.H / 2:.0f}")


ROLES = [
    (r"^bk_brick_", {"brick"}),
    (r"^ln_brick_", {"lining"}),
    (r"^fr_(jamb|sill|tiebar|strap|kerb)", {"iron"}),
    (r"^wood_", {"wood"}),
    (r"^kindling_", {"ember"}),
    (r"^ore\d_", {"ore"}),
    (r"^fire\d_", {"ember"}),
    (r"^roasted\d_", {"roasted"}),
    (r"^sulfur_", {"sulfur"}),
    (r"^front(half|walled)_brick", {"brick"}),
    (r"^outletcap_", {"iron"}),
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
def check_containment(v):
    """Nothing leaves the stall's eight cells; nothing stands over its top, so the chimney's cell, the cells beside it
    and the cells over the bay are clear."""
    m = v.m
    worst, where = -1e9, None
    for pid in v.by_part:
        for el in v.posed(pid):
            lo, hi = el.aabb()
            o = max(-lo[0], hi[0] - m.W, -lo[1], hi[1] - m.H, -lo[2], hi[2] - m.D)
            if o > worst:
                worst, where = o, el.name
    print(f"containment: worst overhang {worst:.3f} voxels ({where}); nothing over the top")
    if worst > 1e-6:
        v.fail(f"{where} leaves the stall's cells")


def check_top(v):
    """The top is flat: every half voxel of it but the bay and the outlet's seat, just under the top, is brick or
    flush iron whose top is the top."""
    m = v.m
    solid = static(v)
    (bx0, bx1), (bz0, bz1) = m.BAY_X, m.BAY_Z
    (cx0, cx1), _, (cz0, cz1) = m.CAP_SEAT
    bad, n = [], 0
    x = 0.25
    while x < m.W:
        z = 0.25
        while z < m.D:
            if not (bx0 <= x <= bx1 and bz0 <= z <= bz1) and not (cx0 <= x <= cx1 and cz0 <= z <= cz1):
                n += 1
                under = [e for e in solid if inside(e, (x, m.TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.TOP) > 1e-6 for e in under):
                    bad.append((x, z))
            z += 0.5
        x += 0.5
    print(f"top: flat at {n - len(bad)} of {n} points (every half voxel, the bay and the outlet's seat aside)")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")


def check_sides(v):
    """Stalls tile into a row: each side face is flush, plain brick over its whole area (sampled every half voxel),
    and nothing but the brickwork reaches either side plane, in any part; two stalls side by side (one moved a stall's
    width along x) touch nowhere but at their shared walls, whatever either holds."""
    m = v.m
    bricks = v.by_part["brickwork"]
    bad, n = [], 0
    for x in (0.05, m.W - 0.05):
        y = 0.25
        while y < m.H:
            z = 0.25
            while z < m.D:
                n += 1
                if not any(inside(e, (x, y, z)) for e in bricks):
                    bad.append((x, y, z))
                z += 0.5
            y += 0.5
    if bad:
        v.fail(f"the side faces are not plain brick at {len(bad)} of {n} points, e.g. {bad[:4]}")
    reach = sorted(e.name for pid in v.by_part for e in v.posed(pid)
                   if (e.aabb()[0][0] < 1e-6 or e.aabb()[1][0] > m.W - 1e-6) and not e.name.startswith("bk_brick"))
    if reach:
        v.fail(f"not brick at a side plane: {reach[:4]}")
    a = [e for pid in v.by_part for e in v.posed(pid)]
    b = []
    for e in a:
        c = e.clone()
        c.c = [c.c[0] + m.W, c.c[1], c.c[2]]
        b.append(c)
    near_a = [e for e in a if e.aabb()[1][0] > m.W - 2.0]
    near_b = [e for e in b if e.aabb()[0][0] < m.W + 2.0]
    hits = 0
    for ea in near_a:
        for eb in near_b:
            alo, ahi = ea.aabb()
            blo, bhi = eb.aabb()
            if all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)) and obb_obb(ea, eb):
                hits += 1
    if hits:
        v.fail(f"two stalls side by side run into each other ({hits} pairs)")
    print(f"sides: plain brick at {n - len(bad)} of {n} points on the two side faces; nothing else reaches them; two "
          f"stalls side by side clear of each other but at the shared walls")


def check_walls(v):
    """The walls: the bay and the outlet's curb sit where they are meant to (the bay centred left to right, the curb
    in the outlet's cell); each side wall is solid (brick, or iron set into it) for at least WALL_T from the bay out
    to the side face, sampled every half voxel from the floor to the top; the back face is solid for at least
    BACK_MIN in from it everywhere (the brick and the lining behind the flue), and the bay's back face for at least
    LINING_T back from it but at the flue mouth (under the outlet's seat: the curb is checked on its own)."""
    m = v.m
    if abs(sum(m.BAY_X) - m.W) > 1e-9:
        v.fail("the bay is not centred left to right")
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    oc = m.outlet_cell()
    if not (oc[0] * m.B <= cx0 and cx1 <= (oc[0] + 1) * m.B and oc[2] * m.B <= cz0 and cz1 <= (oc[2] + 1) * m.B):
        v.fail("the outlet's curb is not in the outlet's cell")
    near = static(v)

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
    worst = {}
    ys = [m.FLOOR_Y + 0.25 + 0.5 * k for k in range(int((m.TOP - m.FLOOR_Y) * 2))]
    for y in ys:
        for z in [m.BAY_Z[0] + 0.25 + 0.5 * k for k in range(int((m.BAY_Z[1] - m.BAY_Z[0]) * 2))]:
            for side, x, sign in (("west", m.BAY_X[0], -1), ("east", m.BAY_X[1], 1)):
                t = run((x, y, z), 0, sign, m.WALL_T)
                worst[side] = min(worst.get(side, (1e9, None)), (t, (y, z)))
        (mx0, mx1), (my0, my1), _ = m.MOUTH
        for x in [m.BAY_X[0] + 0.25 + 0.5 * k for k in range(int((m.BAY_X[1] - m.BAY_X[0]) * 2))]:
            if mx0 - 0.25 <= x <= mx1 + 0.25 and y <= my1 + 0.25:
                continue
            t = run((x, y, m.BAY_Z[1]), 2, 1, m.LINING_T)
            worst["back of the bay"] = min(worst.get("back of the bay", (1e9, None)), (t, (x, y)))
    for y in [0.25 + 0.5 * k for k in range(int(m.CAP_Y[0] * 2))]:           # under the outlet's seat (the curb's own)
        for x in [0.25 + 0.5 * k for k in range(int(m.W * 2))]:
            t = run((x, y, m.D), 2, -1, m.BACK_MIN)
            worst["back face"] = min(worst.get("back face", (1e9, None)), (t, (x, y)))
    want = {"west": m.WALL_T, "east": m.WALL_T, "back of the bay": m.LINING_T, "back face": m.BACK_MIN}
    print("walls: the bay centred, the curb in the outlet's cell; solid at least "
          + ", ".join(f"{k} {t:.2f}" for k, (t, _) in worst.items()))
    for k, (t, at) in worst.items():
        if t < want[k] - 1e-9:
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
    edges, with the direction out of the box."""
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
    """The lining: on every hot face (the bay's floor, its side faces and its back face, and every face of the flue
    mouth, the back flue and the stack that brick stands behind) the brick is the lining's for at least LINING_T from
    the face out, unless the brick ends sooner (the lining then reaches through), sampled every voxel and walked out
    every quarter; nowhere is a hot face the brickwork's. Iron set flush into a face (the jambs' inner legs, the sill)
    is the face there."""
    m = v.m
    t = m.LINING_T
    bay = ([m.BAY_X[0], m.FLOOR_Y, m.BAY_Z[0]], [m.BAY_X[1], m.TOP, m.BAY_Z[1]])
    hot = [(bay, ((0, -1), (0, 1), (1, -1), (2, 1)))]           # the bay: its sides, its floor, its back
    for (x0, x1), (y0, y1), (z0, z1) in (m.MOUTH, m.BACK_FLUE, m.STACK):
        hot.append((([x0, y0, z0], [x1, y1, z1]), tuple((k, s) for k in range(3) for s in (-1, 1))))
    thinnest, where, n, brickwork = 1e9, None, 0, []
    for (lo, hi), sides in hot:
        for k, sign in sides:
            for pnt in face_samples(lo, hi, k, sign):
                first = list(pnt)
                first[k] += sign * 0.125
                w0 = what_is(v, first)
                if w0 != "ln":
                    if w0 == "bk":
                        brickwork.append(first)
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
    if brickwork:
        v.fail(f"a hot face is the brickwork's at {len(brickwork)} points, e.g. {brickwork[:3]}")
    if thinnest < 1e9:
        v.fail(f"the lining is {thinnest:.2f} thick at {where}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or "
          f"the lining reaching through; no hot face of the brickwork")


def check_build(v):
    """The stages' counts: the brickwork's and the lining's volumes, measured from their elements (each lattice box in
    exactly one brick box: their volumes add up to the lattice's), give the rig's quantities at the game's rates,
    rounded up; the frame's recipe is as proposed; every stage's requires is a part's; the front's walling is one
    brick a brick drawn."""
    m = v.m
    xs, ys, zs, mat = m.brick_lattice()
    lat = {"bk": 0.0, "ln": 0.0}
    for (i, j, k), w in mat.items():
        if w:
            lat[w] += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]) * (zs[k + 1] - zs[k])
    els = {w: sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in brick(v) if e.name.startswith(f"{w}_brick_"))
           for w in ("bk", "ln")}
    for w in ("bk", "ln"):
        if abs(lat[w] - els[w]) > 0.05:             # the z-fighting fix steps a few faces in by 0.015
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
        v.fail("the stages are not brickwork, lining")
    if st["brickwork"]["materials"][0]["code"] != "game:burnedbrick-*" or not st["brickwork"].get("oneType"):
        v.fail("the brickwork does not take one type of any fired brick")
    if [(x["code"], x["quantity"]) for x in build["frame"]["ingredients"]] != list(m.FRAME_RECIPE):
        v.fail("the frame's recipe is not the proposed one")
    front = build["front"]
    drawn = {p: len(v.by_part[f"front{p}"]) for p in ("half", "walled")}
    if front["materials"] != [{"code": "game:burnedbrick-*", "quantity": drawn["walled"]}] or front["half"] != drawn["half"]:
        v.fail(f"the front's walling is not a brick a brick drawn: {front} against {drawn}")
    requires = {p["requires"] for p in v.parts}
    if not set(m.STAGES) <= requires or not set(front["places"]) <= requires:
        v.fail("a stage's or a place's requires is no part's")
    print(f"build: brickwork {lat['bk'] / m.B ** 3:.2f} blocks -> {got['bricks']} fired bricks; lining "
          f"{lat['ln'] / m.B ** 3:.2f} blocks -> {got['lining']} refractory bricks and {got['fireclay']} fire clay; the "
          f"frame {dict(m.FRAME_RECIPE)}; the front's walling {drawn['walled']} loose bricks ({drawn['half']} half)")


# ---------------------------------------------------------------- the charge
def check_heap(v):
    """The bay's contents: the bed lies on the floor between the walls, behind the walling's place and clear of the
    back wall; the courses stack from the bed's top, each across the bay from wall to wall (the top one mounded in from
    them), stepping back at the front, against the back wall's lining, all under the walls' top; the charge is
    CHARGE rock, PER_COURSE a course; the burning and the roasted courses are the cold ones' boxes; the burning bed is
    the bed's; the sulfur lies on the top course's top."""
    m = v.m
    bed = aabb_of(v.by_part["wood"])
    if abs(bed[0][1] - m.FLOOR_Y) > 1e-9 or bed[0][0] < m.BAY_X[0] or bed[1][0] > m.BAY_X[1]:
        v.fail(f"the bed is not on the floor between the walls: {bed}")
    if bed[0][2] < m.FRONT_Z[1] + max(m.JITTER) or bed[1][2] > m.BAY_Z[1]:
        v.fail("the bed reaches the walling's place or the back wall")
    if abs(bed[1][1] - m.BED_TOP) > 1e-9:
        v.fail("the bed's top is not BED_TOP")
    kin = {e.name.split("_", 1)[1]: e.aabb() for e in v.by_part["kindling"]}
    for e in v.by_part["wood"]:
        if not same_box(e.aabb(), kin.get(e.name.split("_", 1)[1], ([0] * 3, [0] * 3))):
            v.fail(f"{e.name} has no burning twin")
    if m.CHARGE != m.PER_COURSE * len(m.COURSES):
        v.fail("the courses do not hold the charge")
    if abs(m.COURSES[0][0] - m.BED_TOP) > 1e-9 or any(abs(m.COURSES[k][1] - m.COURSES[k + 1][0]) > 1e-9 for k in range(len(m.COURSES) - 1)):
        v.fail("the courses do not stack from the bed")
    fronts = []
    for j in range(1, len(m.COURSES) + 1):
        lo, hi = m.course_box(j)
        body = next(e for e in v.by_part[f"ore{j}"] if e.name.endswith("_course"))
        if not same_box(body.aabb(), (lo, hi)):
            v.fail(f"course {j} is not its box")
        x0, x1 = m.TOP_COURSE_X if j == len(m.COURSES) else m.BAY_X
        if abs(lo[0] - x0) > 1e-9 or abs(hi[0] - x1) > 1e-9 or abs(hi[2] - m.BAY_Z[1]) > 1e-9:
            v.fail(f"course {j} does not lie across the bay against the back wall")
        fronts.append(lo[2])
        for kind in ("fire", "roasted"):
            twin = {e.name.split("_", 1)[1]: e.aabb() for e in v.by_part[f"{kind}{j}"]}
            for e in v.by_part[f"ore{j}"]:
                if not same_box(e.aabb(), twin.get(e.name.split("_", 1)[1], ([0] * 3, [0] * 3))):
                    v.fail(f"{e.name} has no {kind} twin")
    if any(b <= a for a, b in zip(fronts, fronts[1:])) or fronts[0] < m.FRONT_Z[1] + max(m.JITTER):
        v.fail(f"the courses do not step back at the front behind the walling: {fronts}")
    top = max(e.aabb()[1][1] for j in range(1, len(m.COURSES) + 1) for e in v.by_part[f"ore{j}"])
    if top > m.TOP - 3.0:
        v.fail(f"the heap stands to {top:.2f}, near the walls' top")
    crust = m.COURSES[-1][1]
    for e in v.by_part["sulfur"]:
        lo, hi = e.aabb()
        tlo, thi = m.course_box(len(m.COURSES))
        if abs(lo[1] - crust) > 1e-9 or lo[0] < tlo[0] or hi[0] > thi[0] or lo[2] < tlo[2] or hi[2] > thi[2]:
            v.fail(f"{e.name} does not lie on the top course's crust")
    print(f"heap: the bed on the floor ({m.FLOOR_Y}) to {m.BED_TOP}, {len(m.COURSES)} courses of {m.PER_COURSE} to "
          f"{m.COURSES[-1][1]} (lumps to {top:.2f}, the walls {m.TOP}), fronts stepped back {fronts}; burning and roasted "
          f"twins the same boxes; the sulfur on the crust")


def check_front(v):
    """The front's walling: in each place its bricks stand in the opening between the jambs, behind their front legs,
    each on the sill and the floor or on bricks of the course below; half walled is FRONT_COURSES['half'] courses,
    walled up FRONT_COURSES['walled'], its top under the tie bar; the bottom course leaves two draught holes at the
    floor; no two bricks overlap."""
    m = v.m
    for place in m.FRONTS[1:]:
        els = v.by_part[f"front{place}"]
        lo, hi = aabb_of(els)
        if lo[0] < m.BAY_X[0] or hi[0] > m.BAY_X[1] or lo[2] < m.IRON_T - 0.15 or abs(lo[1] - m.FLOOR_Y) > 1e-9:
            v.fail(f"the {place} walling is not in the opening, on the floor: {lo}, {hi}")
        if hi[2] >= m.COURSE_FRONT[0]:
            v.fail(f"the {place} walling reaches the charge")
        courses = round((hi[1] - lo[1]) / m.BRICK_H)
        if courses != m.FRONT_COURSES[place]:
            v.fail(f"the {place} walling is {courses} courses, not {m.FRONT_COURSES[place]}")
        if hi[1] > m.TIE_Y[0] - 0.5:
            v.fail(f"the {place} walling reaches the tie bar")
        loose = []
        for e in els:
            elo, ehi = e.aabb()
            if abs(elo[1] - m.FLOOR_Y) < 1e-9:
                continue
            under = [f for f in els if abs(f.aabb()[1][1] - elo[1]) < 1e-9 and f.aabb()[0][0] < ehi[0] and f.aabb()[1][0] > elo[0]]
            if not under:
                loose.append(e.name)
        if loose:
            v.fail(f"bricks of the {place} walling stand on nothing: {loose}")
        bottom = sorted((e.aabb()[0][0], e.aabb()[1][0]) for e in els if abs(e.aabb()[0][1] - m.FLOOR_Y) < 1e-9)
        holes = [b[0] - a[1] for a, b in zip(bottom, bottom[1:]) if b[0] - a[1] > 1.5]
        if len(holes) != 2:
            v.fail(f"the {place} walling's bottom course has {len(holes)} draught holes")
        for i, a in enumerate(els):
            for b in els[i + 1:]:
                alo, ahi = a.aabb()
                blo, bhi = b.aabb()
                if all(alo[q] < bhi[q] - 1e-6 and blo[q] < ahi[q] - 1e-6 for q in range(3)):
                    v.fail(f"{a.name} and {b.name} overlap")
        print(f"front {place}: {len(els)} loose bricks in {courses} courses, to {hi[1]:.1f} (the tie bar at "
              f"{m.TIE_Y[0]}), draught holes {', '.join(f'{h:.1f}' for h in holes)} wide at the floor")


# ---------------------------------------------------------------- the outlet, the draught
def check_outlet(v):
    """The outlet: capped, a flat iron plate flush with the top in the curb's seat, covering the stack's mouth; the
    curb, the lining's refractory brick, a rim round the cap (sampled every voxel) and a ledge under its edge all
    round."""
    m = v.m
    cap = aabb_of(v.by_part["outletcap"])
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    r = m.CURB_RIM
    if not same_box(cap, ([cx0 + r, m.CAP_Y[0], cz0 + r], [cx1 - r, m.TOP, cz1 - r])):
        v.fail(f"the cap is not flush in the curb's seat: {cap}")
    lining = [e for e in v.by_part["lining"] if e.name.startswith("ln_brick_")]
    (sx0, sx1), _, (sz0, sz1) = m.STACK
    rim = [(x + 0.25, m.TOP - 0.25, z + 0.25) for x in range(int(cx0), int(cx1)) for z in range(int(cz0), int(cz1))
           if not (cx0 + r <= x + 0.25 <= cx1 - r and cz0 + r <= z + 0.25 <= cz1 - r)]
    ledge = [(x + 0.25, m.CAP_Y[0] - 0.1, z + 0.25) for x in range(int(cx0 + r), int(cx1 - r)) for z in range(int(cz0 + r), int(cz1 - r))
             if not (sx0 <= x + 0.25 <= sx1 and sz0 <= z + 0.25 <= sz1)]
    missing = [p for p in rim + ledge if not any(inside(e, p) for e in lining)]
    if missing:
        v.fail(f"the curb is not the lining's brick at {len(missing)} points, e.g. {missing[:3]}")
    if not (cap[0][0] < sx0 and cap[1][0] > sx1 and cap[0][2] < sz0 and cap[1][2] > sz1):
        v.fail("the cap does not cover the stack's mouth")
    print(f"outlet: capped by a plate flush in the curb's seat ({cap[1][0] - cap[0][0]:.0f} square, "
          f"{m.CAP_Y[1] - m.CAP_Y[0]} thick), over the stack's mouth; the curb the lining's brick, rim and ledge "
          f"({len(rim) + len(ledge)} points)")


def check_draught(v):
    """The way the air goes, natural draught only: in at the front (the open front, or the walling's draught holes),
    back through the bay, into the flue mouth at the foot of its back face, along the back flue and up the stack to
    the outlet. Sampled points along it lie in no element of the brick or the frame (or of the walling, through its
    holes); the flue mouth, the back flue and the stack run into each other; the stack and the outlet's mouth lie in no
    element at all but the cap's."""
    m = v.m
    (mx0, mx1), (my0, my1), (mz0, mz1) = m.MOUTH
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = m.BACK_FLUE
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = m.STACK
    hole = [(m.DRAUGHT_COURSE[0][1] + m.DRAUGHT_COURSE[1][0]) / 2, (m.DRAUGHT_COURSE[1][1] + m.DRAUGHT_COURSE[2][0]) / 2]
    y = (my0 + my1) / 2
    path = [(hx, m.FLOOR_Y + 2.0, z) for hx in hole for z in (-0.5, 0.25, 1.0, 2.0, 3.0, 4.0, 5.0)]
    path += [((mx0 + mx1) / 2, y, z + 0.5) for z in range(int(m.FRONT_Z[1] + 1.0), int(mz1))]
    path += [(x + 0.5, y, (fz0 + fz1) / 2) for x in range(int(fx0), int(fx1))]
    path += [((sx0 + sx1) / 2, sy0 + 0.5 + q * 0.5, (sz0 + sz1) / 2) for q in range(int((m.TOP + m.B - sy0) * 2) - 1)]
    if not (mx0 >= fx0 and mx1 <= fx1 and my0 >= fy0 and my1 <= fy1 and abs(mz1 - fz0) < 1e-9 and sx0 >= fx0 and
            sx1 <= fx1 and sz0 >= fz0 and sz1 <= fz1 and sy0 <= fy0):
        v.fail("the flue mouth, the back flue and the stack do not run into each other")
    walls = static(v) + v.by_part["frontwalled"]
    blocked = [p for p in path if any(inside(e, p) for e in walls)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [sx0, sy0, sz0], [sx1, m.TOP + m.B, sz1]
    for pid in v.by_part:
        if pid == "outletcap":
            continue
        for e in v.posed(pid):
            lo, hi = e.aabb()
            if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                v.fail(f"{e.name} is in the stack or over the outlet")
    print(f"draught: natural, in at the front (through the walling's draught holes when walled up), through the bay, "
          f"into the flue mouth ({mx1 - mx0:.0f} wide, {my1 - my0:.0f} high, at the foot of the back face), along the "
          f"back flue and up the stack to the outlet: {len(path)} points clear; the stack's mouth clear but for the cap")


# ---------------------------------------------------------------- clearances
def allowed(pa, pb, ea, eb):
    """Intended overlaps: a lump lying on a course's top is inside the course over it (the same kind)."""
    ra, rb = re.fullmatch(r"(ore|fire|roasted)(\d)", pa), re.fullmatch(r"(ore|fire|roasted)(\d)", pb)
    if not (ra and rb):
        return False
    ja, jb = int(ra.group(2)), int(rb.group(2))
    lower = ea if ja < jb else eb
    return abs(ja - jb) == 1 and "_lump" in lower.name


def touches(items):
    """Pairs of elements of different parts that overlap."""
    hits = {}
    for x in range(len(items)):
        pa, ea, (alo, ahi) = items[x]
        for y in range(x + 1, len(items)):
            pb, eb, (blo, bhi) = items[y]
            if pa == pb:
                continue
            if not all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)):
                continue
            if allowed(pa, pb, ea, eb):
                continue
            if obb_obb(ea, eb):
                hits.setdefault(tuple(sorted((pa, pb))), set()).add(tuple(sorted((ea.name, eb.name))))
    return hits


def scenes(m):
    """The stall built: every state of the heap with the front walled up and the outlet capped, and with the front
    half walled and open; and each stage of the build before it is."""
    out = [(st, "walled", True, "complete") for st in m.HEAP_STATE_IDS]
    out += [(st, "half", False, "complete") for st in ("charged", "roasted")]
    out += [("empty", "open", False, "frame"), ("empty", "open", False, "brickwork")]
    return out


def check_clearances(v):
    """In every scene, nothing of one part runs into another's (the brick and the iron are parts apart: the brick's
    voids hold the iron)."""
    m = v.m
    allhits = {}
    for scene in scenes(m):
        items = [(e.part, e, e.aabb()) for e in v.shown(scene)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: scene for h in hs})
    print(f"clearances in {len(scenes(m))} scenes: {'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s[0]}, {s[1]})" for (a, b), s in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = m.shown(v.parts, [e for pid in v.by_part for e in v.posed(pid)], pose)
        pairs = coplanar_faces(els)
        if pairs:
            bad += len(pairs)
            print(f"  coplanar at {pose}: {len(pairs)}, e.g. {pairs[:3]}")
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
    check_walls(v)
    check_lining(v)
    check_build(v)
    check_heap(v)
    check_front(v)
    check_outlet(v)
    check_draught(v)
    check_clearances(v)
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
    """The file of a game texture code (game:block/..., game:item/...), in the survival or the game domain, or None."""
    rel = code.split(":", 1)[1] + ".png"
    for domain in ("survival", "game"):
        f = assets / domain / "textures" / rel
        if f.is_file():
            return f
    return None


def game_brick_types(assets):
    """The type states of the game's burnedbrick item (its itemtype file is JSON5, so read by pattern)."""
    text = (assets / "survival" / "itemtypes" / "resource" / "burnedbrick.json").read_text()
    found = re.search(r'code:\s*"type",\s*states:\s*\[([^\]]*)\]', text)
    return re.findall(r'"([^"]+)"', found.group(1)) if found else []


def game_block_exists(assets, code):
    """Whether a brick block code is one of the game's: claybricks-good-fire, or a four-high running brick course of a
    colour its brickcourse block type has."""
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
    """Any fired brick: the rig's bricks name every type of the game's burnedbrick and nothing else, each with the
    block its bricks make and that block's textures; the shape's own `brick` is the default type's colour layer. With
    the game's assets at hand, every texture of the mapping and of the shape is a file there, every block one of the
    game's, and the types the game's."""
    bricks = ship["bricks"]
    types = [t for t, *_ in m.BRICKS]
    if list(bricks["types"]) != types or bricks["default"] not in types or bricks["code"] != "brick":
        fail("the rig's bricks are not the stall's types")
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
    print(f"bricks: {len(types)} types ({', '.join(types)}), the game's burnedbrick types; each type's block one of the "
          f"game's and its {sum(len(c) for c in paths.values())} textures, and the shape's {len(shape['textures'])}, "
          f"files in {assets}")


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
    missing = used - set(shape["textures"])
    if missing:
        fail(f"textures used but not declared: {missing}")
    for e in shape["elements"]:
        stem = e["name"].split("_")[0]
        want = m.KINDLING_GLOW if stem == "kindling" else m.HEAP_GLOW if re.fullmatch(r"fire\d", stem) else None
        got = {f.get("glow") for f in e["faces"].values()}
        if got != {want}:
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    if "powerCell" in ship or "powerFace" in ship:
        fail("a stall roaster takes no power")
    if any(k.startswith(("air", "infeed", "output", "chute")) for k in ship):
        fail("a batch stall worked by hand has no air port and no chute anchor")
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    want = {tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.footprint()}
    if set(cells) != want or any(c.get("hollow") for c in cells.values()) or any(not c.get("boxes") for c in cells.values()):
        fail(f"the cells are {sorted(cells)}, not the stall's eight with boxes")
    lids = {p for p, c in cells.items() if "lid" in c}
    top = {p for p in cells if p[1] == m.CELLS_Y - 1 - m.ORIGIN_CELL[1]}
    if lids != top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"decks on {sorted(lids)}, not at the top on the top cells {sorted(top)}")
    chim = tuple(m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    free = [tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.FREE_CELLS]
    if chim in cells or any(f in cells for f in free):
        fail("the chimney's cell or a cell beside it or over the bay is the stall's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    if out[0] != 0 or out[2] != m.CELLS_Z - 1 - m.ORIGIN_CELL[2]:
        fail("the outlet is not in the back row of the controller's column")
    line = ship["chimney"]
    if abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9 or line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY:
        fail("the chimney's line does not stand on the outlet")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    front = ship["front"]["pos"]
    if ship["frontSide"] != "north" or abs(front[2]) > 1e-9 or abs(front[0] * m.B - sum(m.BAY_X) / 2) > 1e-6:
        fail("the front's anchor is not in the middle of the opening on the front face")
    heap = ship["heap"]["pos"]
    if not (m.BAY_X[0] <= heap[0] * m.B <= m.BAY_X[1] and m.BAY_Z[0] <= heap[2] * m.B <= m.BAY_Z[1]):
        fail("the heap's anchor is not over the bay")
    fur = ship["furnace"]
    if (fur["charge"], fur["courses"], fur["perCourse"], fur["minChimney"], fur["rowAxis"]) != (
            m.CHARGE, len(m.COURSES), m.PER_COURSE, m.MIN_CHIMNEY, "x"):
        fail(f"the rig's furnace block is not the model's: {fur}")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"decks on {len(lids)}; glow on the burning bed and the burning charge only; no power cell, no air port, no "
          f"chute; the front's anchor in the opening's middle; the outlet under the chimney's cell {list(chim)}, the "
          f"chimney {m.MIN_CHIMNEY} blocks from it up; the cells round the chimney and over the bay ({len(free)}) left free")
    return ok
