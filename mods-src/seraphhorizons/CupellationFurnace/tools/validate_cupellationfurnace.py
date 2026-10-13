"""The cupellation furnace generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule.
Everything is in the build frame (voxels), on the model before the origin shift; `check_shipped` in make_shape.py
proves the shipped files are this model moved. The furnace's checks follow the crucible furnace's
(`CrucibleFurnace/tools/validate_cruciblefurnace.py`); the blowing engine's follow the parting furnace's v2 branch
(`741-parting-furnace-model-v2`: the yoke, the supports, the intended contacts).
"""

from __future__ import annotations

import math
import os
import re
from pathlib import Path

from machinegen.checks import bearing_margin, coplanar_faces, euler_round_trip, frame_floating, obb_obb
from machinegen.checks import supports as shaft_supports
from machinegen.geometry import aabb_of
from machinegen.rigmath import apply, part_of, posed


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

    def posed(self, pid, theta=0.0):
        key = (pid, round(theta, 9))
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.m.pm(self.parts, pid, (theta, None))
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def point(self, pid, theta, p):
        """A point (voxels) moved with part `pid` at `theta` (the part matrices are in blocks)."""
        q = apply(self.m.pm(self.parts, pid, (theta, None)), [c / self.m.B for c in p])
        return [c * self.m.B for c in q]

    def named(self, pid, rx, theta=0.0):
        return [e for e in self.posed(pid, theta) if re.match(rx, e.name)]

    def shown(self, scene, theta=0.0):
        """Every element drawn in `scene`, at `theta`."""
        return [e for pid in self.by_part if self.m.on_show(pid, scene) for e in self.posed(pid, theta)]


def inside(el, p, eps=0.0):
    """Whether point p lies inside element el (its box, turned), by more than eps."""
    d = [p[k] - el.c[k] for k in range(3)]
    for k in range(3):
        axis = [el.r[i][k] for i in range(3)]
        if abs(sum(d[i] * axis[i] for i in range(3))) > abs(el.size[k]) / 2 - eps:
            return False
    return True


def thetas(n):
    return [2 * math.pi * i / n for i in range(n)]


def same_box(a, b, tol=1e-6):
    return max(abs(a[0][k] - b[0][k]) for k in range(3)) < tol and max(abs(a[1][k] - b[1][k]) for k in range(3)) < tol


def brick(v):
    """Every brick box, the brickwork's and the lining's."""
    return [e for e in v.by_part["brickwork"] + v.by_part["lining"] if e.name.startswith(("bk_brick_", "ln_brick_"))]


def static(v):
    """The furnace's body, built: the iron frame, the brickwork and the lining."""
    return v.by_part["frame"] + v.by_part["brickwork"] + v.by_part["lining"]


def fixed(v):
    """Everything that stands still once built: the body, the test, the engine's fixed parts, the fittings."""
    return static(v) + v.by_part["test"] + v.by_part["engine"] + v.by_part["fittings"]


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
    counts = {p["id"]: len(v.by_part.get(p["id"], [])) for p in v.parts}
    print(f"parts: {len(v.parts)}, elements {len(v.els)}; the iron frame {counts['frame']}; "
          + ", ".join(f"{p} {n}" for p, n in counts.items() if p != "frame"))


def check_stages(v):
    """The build: at each stage (the iron frame alone, with the brickwork, the lining, the test, the engine, the
    fittings) nothing floats: every element drawn is joined to the ground (the engine's moving parts at rest). The iron
    frame stands on its own: it is iron only, and no thin post stands in it taller than half the furnace. Every tie is
    hidden once the brick is in: points just off every face of it lie in brick or in other iron."""
    m = v.m
    out = []
    for sid, fitted in m.STAGE_STATES:
        drawn = [e for p in v.parts if p["requires"] is None or p["requires"] in fitted for e in v.posed(p["id"])]
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
    others = [e for e in frame if not e.name.startswith("fr_tie")]
    bare = 0
    for t in ties:
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
                        if pnt[1] < 0.0 or any(inside(e, pnt) for e in others + ties if e is not t):
                            continue
                        if not any(inside(e, pnt) for e in v.by_part["brickwork"]):
                            bare += 1
    print(f"stages: nothing floats at any stage (elements joined to the ground: {', '.join(out)}); the iron frame iron "
          f"only, with no thin post over {m.H / 2:.0f} (its tallest thin piece {tallest:.1f}); {len(ties)} ties hidden "
          f"in the brickwork ({bare} points off their faces bare)")
    if bare:
        v.fail(f"{bare} points off the ties' faces are in no brickwork or iron: a tie shows")


ROLES = [
    (r"^bk_brick_", {"brick"}),
    (r"^ln_brick_", {"lining"}),
    (r"^fr_", {"iron"}),
    (r"^ts_(ring)", {"iron"}),
    (r"^ts_(ash|rim|breast)", {"boneash"}),
    (r"^en_chest\d_flap", {"leather"}),
    (r"^en_", {"iron"}),
    (r"^sh_oak", {"oak"}),
    (r"^sh_web", {"iron"}),
    (r"^sh_(shaft|pin)", {"steel"}),
    (r"^yk_(rod|slipper)", {"steel"}),
    (r"^yk_", {"iron"}),
    (r"^ft_", {"iron"}),
    (r"^coal_", {"coal"}),
    (r"^fire_", {"ember"}),
    (r"^lead_", {"lead"}),
    (r"^silver_", {"silver"}),
    (r"^(stream|litharge)_", {"litharge"}),
    (r"^damper(open|half|shut)_", {"iron"}),
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


# ---------------------------------------------------------------- containment, the sides, the walls
def check_containment(v):
    """Nothing leaves the furnace's eighteen cells over a whole turn of the shaft (every 7.5 degrees): nothing stands
    over the top, so the chimney's cell and every cell over the furnace are free."""
    m = v.m
    worst, where = -1e9, None
    for th in thetas(48):
        for pid in v.by_part:
            if pid not in m.MOVING and th:
                continue
            for el in v.posed(pid, th):
                lo, hi = el.aabb()
                o = max(-lo[0], hi[0] - m.W, -lo[1], hi[1] - m.H, -lo[2], hi[2] - m.D)
                if o > worst:
                    worst, where = o, (el.name, th)
    print(f"containment over a turn: worst overhang {worst:.3f} voxels ({where[0]} at theta {where[1]:.2f})")
    if worst > 1e-6:
        v.fail(f"{where[0]} leaves the furnace's cells at theta {where[1]}")


def check_sides(v):
    """Furnaces tile into a row: the west face is plain brickwork over its whole area, and the east face wherever the
    furnace reaches it (the front two rows and the back row's base; the engine's bay above the base is open to the
    east, and nothing in it reaches the plane); nothing but the brickwork reaches either side plane at any angle of the
    shaft, and two furnaces side by side (one moved a furnace's width along x) touch nowhere but at their walls."""
    m = v.m
    bricks = v.by_part["brickwork"]
    bad, n = [], 0
    for x in (0.05, m.W - 0.05):
        y = 0.25
        while y < m.H:
            z = 0.25
            while z < m.D:
                if x > m.W / 2 and y > m.BASE_TOP and z > m.BODY_D:
                    z += 0.5
                    continue
                n += 1
                if not any(inside(e, (x, y, z)) for e in bricks):
                    bad.append((x, y, z))
                z += 0.5
            y += 0.5
    if bad:
        v.fail(f"the side faces are not plain brickwork at {len(bad)} of {n} points, e.g. {bad[:4]}")
    reach = set()
    for th in thetas(24):
        for pid in v.by_part:
            if pid not in m.MOVING and th:
                continue
            for e in v.posed(pid, th):
                lo, hi = e.aabb()
                if (lo[0] < 1e-6 or hi[0] > m.W - 1e-6) and not e.name.startswith("bk_brick"):
                    reach.add(e.name)
    if reach:
        v.fail(f"not brickwork at a side plane: {sorted(reach)[:4]}")
    hits = 0
    for th in (0.0, math.pi / 2, math.pi, 3 * math.pi / 2):
        a = [e for pid in v.by_part for e in v.posed(pid, th)]
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
        v.fail(f"two furnaces side by side run into each other ({hits} pairs)")
    print(f"sides: plain brickwork at {n - len(bad)} of {n} points on the two side faces (the engine's bay open to the "
          f"east); nothing else reaches them; two furnaces side by side clear of each other but at their walls")


def check_walls(v):
    """Thick walls round the hot room: from the chamber's west face, the fireplace's east face, the back wall's face
    and the front wall's face (clear of the working door's bay, the fire door and the ash mouth), sampled every half
    voxel from the hearth (the grate, in the fireplace) to the springing, the brick (or iron set into it) runs at
    least MIN_WALL out to the furnace's face, or into the engine's bay."""
    m = v.m
    near = brick(v) + [e for e in v.by_part["frame"]]

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
    walls = {}
    (cx0, _), (cz0, cz1) = m.CH_X, m.CH_Z
    bay = (m.JAMBS[0][0] - 0.5, m.JAMBS[1][1] + 0.5)
    fire = (m.RECESS[0][0] - 0.5, m.RECESS[0][1] + 0.5)
    y = m.HEARTH_Y + 0.25
    while y < m.SPRING_Y:
        z = cz0 + 0.25
        while z < cz1:
            walls.setdefault("west", []).append((run((cx0, y, z), 0, -1, cx0), (y, z)))
            if y > m.GRATE_TOP:
                walls.setdefault("east", []).append((run((m.FIRE_X[1], y, z), 0, 1, m.W - m.FIRE_X[1]), (y, z)))
            z += 0.5
        x = cx0 + 0.25
        while x < m.FIRE_X[1]:
            first = (x, y, cz1 + 0.125)
            if solid(first):
                walls.setdefault("back", []).append((run((x, y, cz1), 2, 1, m.BODY_D - cz1), (x, y)))
            if not (bay[0] <= x <= bay[1] or fire[0] <= x <= fire[1]) and solid((x, y, cz0 - 0.125)):
                walls.setdefault("front", []).append((run((x, y, cz0), 2, -1, cz0), (x, y)))
            x += 0.5
        y += 0.5
    worst = {k: min(w) for k, w in walls.items()}
    print("walls: round the chamber and the fireplace at least " + ", ".join(f"{k} {t:.2f}" for k, (t, _) in worst.items())
          + f" (MIN_WALL {m.MIN_WALL}; the front's openings aside)")
    for k, (t, at) in worst.items():
        if t < m.MIN_WALL - 1e-9:
            v.fail(f"the {k} wall is {t:.2f} thick at {at}")


# ---------------------------------------------------------------- the lining, the build
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
    """The lining: on every hot face (the chamber's walls, floor and vault, the bridge, the fireplace from its bearers
    up, the flue opening, the back flue and the stack) the brick is the lining's for at least LINING_T from the face
    out, unless the brick ends sooner, sampled every voxel and walked out every quarter; the cave's and the ash mouth's
    faces are the brickwork's."""
    m = v.m
    t = m.LINING_T
    hot = m.hot_voids() + m.flue_voids()
    thinnest, where, n = 1e9, None, 0
    for lo, hi in hot:
        for k in range(3):
            for sign in (-1, 1):
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
    for lo, hi in (([m.CAVE[0][0], m.CAVE[1][0], m.CAVE[2][0]], [m.CAVE[0][1], m.CAVE[1][1], m.CAVE[2][1]]),
                   ([mx0, my0, 1.0], [mx1, my1, m.CAVE[2][0]])):
        for k in range(3):
            for sign in (-1, 1):
                for pnt in face_samples(lo, hi, k, sign, step=2.0):
                    q = list(pnt)
                    q[k] += sign * 0.125
                    w = what_is(v, q)
                    if w in ("ln", "bk"):
                        cold += 1
                        if w == "ln" and not (k == 1 and sign > 0):
                            wrong.append(q)
    if thinnest < 1e9:
        v.fail(f"the lining is {thinnest:.2f} thick at {where}")
    if wrong:
        v.fail(f"the cave or the ash mouth is lined at {len(wrong)} points, e.g. {wrong[:3]}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or "
          f"the lining reaching through; the cave and the ash mouth the brickwork's ({cold} points; the cave's roof is "
          f"the grate's)")


def check_build(v):
    """The stages' counts: the brickwork's and the lining's volumes, measured from their elements (each lattice box in
    exactly one brick box: their volumes add up to the lattice's), give the rig's quantities at the game's rates,
    rounded up; the test's bone meal its measured bone ash at the cupel's rate; every stage's requires is a part's."""
    m = v.m
    xs, ys, zs, mat = m.brick_lattice()
    lat = {"bk": 0.0, "ln": 0.0}
    for (i, j, k), w in mat.items():
        if w:
            lat[w] += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]) * (zs[k + 1] - zs[k])
    els = {w: sum(abs(e.size[0] * e.size[1] * e.size[2]) for e in brick(v) if e.name.startswith(f"{w}_brick_"))
           for w in ("bk", "ln")}
    for w in ("bk", "ln"):
        if abs(lat[w] - els[w]) > 0.2:             # the z-fighting fix steps a few faces in by 0.015
            v.fail(f"the {w} boxes hold {els[w]:.2f} cubic voxels, the lattice {lat[w]:.2f}")
    c = m.counts()
    st = {s["requires"]: s for s in v.rig["build"]["stages"]}
    got = {"bricks": st["brickwork"]["materials"][0]["quantity"], "lining": st["lining"]["materials"][0]["quantity"],
           "fireclay": st["lining"]["materials"][1]["quantity"], "bonemeal": st["test"]["materials"][0]["quantity"]}
    want = {"bricks": math.ceil(lat["bk"] / m.B ** 3 * m.BRICKS_PER_BLOCK - 1e-9),
            "lining": math.ceil(lat["ln"] / m.B ** 3 * m.LINING_PER_BLOCK - 1e-9)}
    want["fireclay"] = math.ceil(want["lining"] / m.LINING_PER_FIRECLAY - 1e-9)
    # the test's bone ash, measured again from its elements: sampled on a grid, a point in any bone-ash element
    ash = [e for e in v.by_part["test"] if not e.name.startswith("ts_ring")]
    lo, hi = aabb_of(ash)
    step = 0.25
    vol, x = 0.0, lo[0] + step / 2
    while x < hi[0]:
        z = lo[2] + step / 2
        while z < hi[2]:
            col = [e for e in ash if e.aabb()[0][0] <= x <= e.aabb()[1][0] and e.aabb()[0][2] <= z <= e.aabb()[1][2]]
            y = lo[1] + step / 2
            while y < hi[1] and col:
                if any(inside(e, (x, y, z)) for e in col):
                    vol += step ** 3
                y += step
            z += step
        x += step
    want["bonemeal"] = math.ceil(c["ashVolume"] * m.B ** 3 * m.BONEMEAL_PER_VOXEL - 1e-9)
    if got != want or any(got[k] != c[k] for k in got):
        v.fail(f"the stages' counts {got} are not the measured {want}")
    if abs(vol - c["ashVolume"] * m.B ** 3) > 0.03 * vol:
        v.fail(f"the test's bone ash measures {vol:.1f} cubic voxels, its count's {c['ashVolume'] * m.B ** 3:.1f}")
    if [s["requires"] for s in v.rig["build"]["stages"]] != list(m.STAGES):
        v.fail("the stages are not " + ", ".join(m.STAGES))
    if st["brickwork"]["materials"][0]["code"] != "game:burnedbrick-*" or not st["brickwork"].get("oneType"):
        v.fail("the brickwork does not take one type of any fired brick")
    requires = {p["requires"] for p in v.parts}
    if not set(m.STAGES) <= requires or any(f"damper{d}" not in requires for d in m.DAMPERS):
        v.fail("a stage's requires is no part's")
    print(f"build: brickwork {lat['bk'] / m.B ** 3:.2f} blocks -> {got['bricks']} fired bricks; lining "
          f"{lat['ln'] / m.B ** 3:.2f} blocks -> {got['lining']} refractory bricks and {got['fireclay']} fire clay; the "
          f"test's bone ash {vol:.0f} cubic voxels (counted {c['ashVolume'] * m.B ** 3:.0f}) -> {got['bonemeal']} bone "
          f"meal; the frame {dict(m.FRAME_RECIPE)}")


# ---------------------------------------------------------------- the test, the litharge's way, the blast's
def check_test(v):
    """The test stands on the chamber's hearth, centred under the working door, clear of the walls and the bridge; the
    ring round the bone ash on seven sides, the breast at the front with the gate cut in it as wide as the gutter;
    the dish's floor under the rim; the lead and the cake in the dish; the working door's sill level with the ring's
    rim; the tuyere on the test's middle line, its mouth over the ring's back side and between the rim and the
    springing, blowing towards the breast."""
    m = v.m
    test = v.by_part["test"]
    lo, hi = aabb_of(test)
    if abs(lo[1] - m.HEARTH_Y) > 1e-9:
        v.fail(f"the test does not stand on the hearth ({lo[1]})")
    room = min(lo[0] - m.CH_X[0], m.CH_X[1] - hi[0], lo[2] - m.CH_Z[0], m.CH_Z[1] - hi[2])
    if room < 0.5:
        v.fail(f"the test is {room:.2f} from the chamber's walls")
    if abs((lo[0] + hi[0]) / 2 - m.X_T) > 0.6 or not (m.DOOR[0][0] < m.X_T < m.DOOR[0][1]):
        v.fail("the test is not under the working door")
    rings = [e for e in test if e.name.startswith("ts_ring")]
    if len(rings) != 7 or any(abs(e.aabb()[1][1] - m.RING_TOP) > 0.05 for e in rings):     # the z-fighting fix steps a face
        v.fail("the ring is not seven sides to the rim")
    if abs(m.DOOR[1][0] - m.RING_TOP) > 1e-9:
        v.fail("the working door's sill is not level with the test's rim")
    gate = [e.aabb() for e in test if e.name.startswith("ts_breast_") and not e.name.endswith("foot")]
    gw = min(b[0][0] for b in gate if b[0][0] > m.X_T) - max(b[1][0] for b in gate if b[1][0] < m.X_T)
    if abs(gw - 2 * m.GUTTER_HALF) > 1e-6:
        v.fail(f"the gate is {gw:.2f} wide, not the gutter's {2 * m.GUTTER_HALF}")
    for what in ("lead", "silver"):
        flo, fhi = aabb_of(v.by_part[what])
        if abs(flo[1] - m.ASH_TOP) > 1e-9 or fhi[1] > m.BAND[1] - 0.2:
            v.fail(f"the {what} is not in the dish")
    tx, ty = m.TUYERE
    back = max(e.aabb()[1][2] for e in rings)
    if abs(tx - m.X_T) > 1e-9 or not (m.RING_TOP + m.TUYERE_R < ty < m.SPRING_Y - m.TUYERE_R + 0.5):
        v.fail("the tuyere is not over the test's middle line, between the rim and the springing")
    if not (back < m.TUYERE_Z[0] < m.CH_Z[1]):
        v.fail(f"the tuyere's mouth ({m.TUYERE_Z[0]}) is not just behind the ring's back ({back:.2f}) in the chamber")
    print(f"test: on the hearth, {room:.2f} from the walls, under the working door (its sill at the rim, {m.RING_TOP}); "
          f"the ring on seven sides, the breast's gate {gw:.2f} wide (the gutter's); the lead and the cake in the dish; "
          f"the tuyere's mouth at z {m.TUYERE_Z[0]}, {m.TUYERE_Z[0] - back:.2f} behind the ring, {ty - m.RING_TOP:.2f} "
          f"over the rim")


def check_litharge(v):
    """The litharge's way: through the gate (its floor the dish's), onto the gutter (its inner end level with the gate's
    floor at the breast's face), down the gutter (falling all the way, bedded at its outer end in the sill's groove),
    off its end over the pot's mouth, the stream falling inside the pot's walls onto the litharge in it; nothing of the
    furnace in the stream's way."""
    m = v.m
    (z0, z1), (y0, y1) = m.GUTTER_Z, m.GUTTER_Y
    if not (y0 < y1) or abs(y1 - m.ASH_TOP) > 1e-9 or abs(z1 - m.BREAST_Z[0]) > 1e-9:
        v.fail("the gutter does not fall from the gate's floor at the breast's face")
    bottom = next(e for e in v.by_part["fittings"] if e.name == "ft_gutter_bottom")
    bed = [p for p in ((m.X_T, y0 - 0.32, z) for z in (m.GROOVE[2][0] + 0.1, m.GROOVE[2][0] + 0.5))]
    if not all(inside(bottom, p) or p[1] < m.GROOVE[1][0] for p in bed):
        v.fail("the gutter is not bedded in its groove")
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = m.STREAM
    cx, cz = m.POT_C
    a_in = m.POT_A - m.POT_WALL
    if not (cx - a_in < sx0 and sx1 < cx + a_in and cz - a_in < sz0 and sz1 < cz + a_in):
        v.fail("the stream does not fall inside the pot's walls")
    if abs(sy1 - y0) > 1e-9 or abs(sz1 - z0) > 1e-9 or abs(sy0 - m.LITH_Y[1]) > 1e-9:
        v.fail("the stream does not run from the gutter's end onto the litharge")
    blocked = []
    others = fixed(v)
    y = sy0 + 0.25
    while y < sy1 - 0.05:
        p = ((sx0 + sx1) / 2, y, (sz0 + sz1) / 2)
        if any(inside(e, p) for e in others if not e.name.startswith("ft_gutter")):
            blocked.append(p)
        y += 0.25
    if blocked:
        v.fail(f"the stream's fall is blocked at {blocked[:3]}")
    print(f"litharge: through the gate onto the gutter at {y1}, down it to {y0} over {z1 - z0:.2f}, off its end at z {z0} "
          f"over the pot's mouth, falling {sy1 - sy0:.1f} inside its walls onto the litharge at {sy0}; the fall clear")


def check_blast(v):
    """The blast's way, joined piece to piece: each valve chest stands on the main; the main runs into the elbow, the
    elbow into the blast pipe, and the blast pipe ends at the back wall's face, where the tuyere's back end is; the
    tuyere runs through the back wall into the chamber."""
    m = v.m
    eng = v.by_part["engine"]
    main = aabb_of([e for e in eng if e.name.startswith("en_main")])
    elbow = aabb_of([e for e in eng if e.name == "en_elbow"])
    pipe = aabb_of([e for e in eng if e.name.startswith("en_blastpipe")])
    tuyere = aabb_of([e for e in v.by_part["frame"] if e.name.startswith("fr_tuyere")])
    for n in (1, 2):
        chest = aabb_of([e for e in eng if e.name == f"en_chest{n}"])
        if abs(chest[0][1] - main[1][1]) > 1e-6 or not (main[0][0] < chest[1][0] and chest[0][0] < main[1][0] + 2.4):
            v.fail(f"valve chest {n} does not stand on the blast main")
    if abs(main[0][0] - elbow[1][0]) > 0.02 or abs(pipe[1][2] - elbow[0][2]) > 0.02:
        v.fail("the main, the elbow and the blast pipe do not run into each other")
    if abs(pipe[0][2] - m.BODY_D) > 1e-6 or abs(tuyere[1][2] - m.BODY_D) > 1e-6:
        v.fail("the blast pipe and the tuyere do not meet at the back wall's face")
    if tuyere[0][2] > m.CH_Z[1]:
        v.fail("the tuyere does not reach into the chamber")
    print(f"blast: the chests on the main, the main into the elbow, the blast pipe to the back face at z {m.BODY_D}, the "
          f"tuyere on through the wall to z {tuyere[0][2]:.1f} in the chamber")


# ---------------------------------------------------------------- the engine
def check_engine(v):
    """The blowing engine: the shaft continues the vanilla axle at the power cell's centre, its oak end at the power
    face, and is carried by its two pedestal bearings, each round it at rest and turned 45 degrees; the crank pin stays
    on the yoke's slot's middle line (x) at every angle and inside its length (an exact Scotch yoke); the slippers run
    on the rail and under the top guide over the whole stroke; the piston rod stays in its gland, its end inside the
    barrel, at both ends of the stroke; the stroke clears the guides' standards."""
    m = v.m
    x, y = m.SHAFT
    cell = m.POWER_CELL
    if abs(x - (cell[0] + 0.5) * m.B) > 1e-9 or abs(y - (cell[1] + 0.5) * m.B) > 1e-9 or m.POWER_FACE != "south" or cell[2] != m.CELLS_Z - 1:
        v.fail("the shaft is not at the power cell's centre on its south face")
    oak = aabb_of(v.named("shaft", r"^sh_oak"))
    if abs(oak[1][2] - m.D) > 1e-9:
        v.fail("the shaft's oak end is not at the power face")
    eng = v.by_part["engine"]
    found, _ = shaft_supports(eng, v.named("shaft", r"^sh_shaft"), 2, m.SHAFT)
    named = sorted(n for n in found if re.match(r"^en_ped[ns]_bearing$", n))
    if len(named) != 2:
        v.fail(f"the shaft is not carried by its two pedestal bearings: {found}")
    for th in (0.0, math.pi / 4):
        els = v.named("shaft", r"^sh_shaft", th)
        for b in [e for e in eng if re.match(r"^en_ped[ns]_bearing$", e.name)]:
            mg = bearing_margin(b, els, 2)
            if not 0 < mg < 1e8:
                v.fail(f"{b.name} does not enclose the shaft at theta {th:.2f} (margin {mg:.3f})")
    zc = (m.YOKE_Z[0] + m.YOKE_Z[1]) / 2
    pin0 = [x, y - m.CRANK_R, zc]
    worst_x, worst_y = 0.0, 0.0
    for th in thetas(48):
        pin = v.point("shaft", th, pin0)
        slot = v.point("yoke", th, [x, y, zc])
        worst_x = max(worst_x, abs(pin[0] - slot[0]))
        worst_y = max(worst_y, abs(pin[1] - slot[1]))
    room = m.SLOT_L - m.PIN_R / math.cos(math.pi / 8)
    if worst_x > 1e-6 or worst_y > room + 1e-6:
        v.fail(f"the crank pin leaves the yoke's slot ({worst_x:.1e} off its middle line, {worst_y:.3f} along it)")
    rail = next(e for e in eng if e.name == "en_rail").aabb()
    top = next(e for e in eng if e.name == "en_topguide").aabb()
    cheek = max(e.aabb()[1][0] for e in eng if e.name.startswith("en_standardw"))
    east = min(e.aabb()[0][0] for e in eng if e.name == "en_standarde")
    rod_end, rod_x = [], []
    for th in thetas(24):
        sl = {e.name: e.aabb() for e in v.posed("yoke", th)}
        b, t = sl["yk_slipperbottom"], sl["yk_slippertop"]
        if abs(b[0][1] - rail[1][1]) > 1e-6 or abs(t[1][1] - top[0][1]) > 1e-6:
            v.fail(f"a slipper leaves its guide at theta {th:.2f}")
        if b[0][0] < rail[0][0] - 1e-6 or b[1][0] > rail[1][0] + 1e-6 or t[0][0] < top[0][0] or t[1][0] > top[1][0]:
            v.fail(f"a slipper runs off its guide's end at theta {th:.2f}")
        ylo = min(bb[0][0] for n, bb in sl.items() if n != "yk_rod")
        yhi = max(bb[1][0] for bb in sl.values())
        if ylo < cheek + 0.3 or yhi > east - 0.3:
            v.fail(f"the yoke runs into a standard at theta {th:.2f}")
        rod_end.append(sl["yk_rod"][0][0])
        rod_x.append(sl["yk_rod"][1][0])
    g0 = m.CYL_X[1] + m.FLANGE_T + m.COVER_T
    if min(rod_end) < m.CYL_X[0] + 1.0 or max(rod_end) > m.CYL_X[1] - 1.0 or min(rod_x) < g0 + m.GLAND[0]:
        v.fail(f"the piston rod's end leaves the barrel or the rod the gland ({min(rod_end):.2f}..{max(rod_end):.2f})")
    print(f"engine: the shaft at the power cell's centre, its oak end at the power face, in its bearings {', '.join(named)} "
          f"(enclosed at rest and at 45 degrees); the pin {worst_x:.1e} off the slot's middle line and {worst_y:.2f} along "
          f"it (room {room:.2f}); the slippers on the rail and under the top guide over the stroke {2 * m.CRANK_R}; the "
          f"rod's end {min(rod_end):.2f}..{max(rod_end):.2f} in the barrel ({m.CYL_X[0]}..{m.CYL_X[1]}), the rod always "
          f"through the gland")


# ---------------------------------------------------------------- clearances
# Intended contacts: (part a, element regex in a or None, part b, element regex in b or None), either way round.
ALLOWED = [
    ("shaft", r"^sh_shaft", "engine", r"^en_ped[ns]_bearing"),
    ("yoke", r"^yk_rod", "engine", r"^en_(cyl|flange|cover|gland)"),
    ("yoke", r"^yk_slipper", "engine", r"^en_(rail|topguide)"),
    ("fittings", r"^ft_gutter", "brickwork", None),
    ("fittings", r"^ft_gutter", "lining", None),
]


def allowed(pa, pb, ea, eb):
    for a, ra, b, rb in ALLOWED:
        for (p1, e1), (p2, e2) in (((pa, ea), (pb, eb)), ((pb, eb), (pa, ea))):
            if p1 == a and p2 == b and (ra is None or re.match(ra, e1.name)) and (rb is None or re.match(rb, e2.name)):
                return True
    return False


def touches(items, movers=None):
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
    """The furnace built: every state of the test and the fire with the damper open, every place of the damper with
    the outlet capped; and each stage of the build before it is complete."""
    return ([(st, "open", False, "complete") for st in m.WORK_STATE_IDS] + [("cold", d, True, "complete") for d in m.DAMPERS]
            + [("cold", "open", False, sid) for sid, _ in m.STAGE_STATES[:-1]])


def check_clearances(v, n):
    """In every scene, at rest, nothing of one part runs into another's (the brick, the iron and the engine are parts
    apart: the brick's voids hold the iron); and the shaft and the yoke, turned through n angles, touch nothing but
    their intended contacts."""
    m = v.m
    allhits = {}
    for scene in scenes(m):
        items = [(e.part, e, e.aabb()) for e in v.shown(scene, 0.0)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: (scene, 0.0) for h in hs})
    near = [e for e in v.shown(m.DEFAULT_SCENE, 0.0) if e.part not in m.MOVING and e.aabb()[1][2] > m.BODY_D - 1.0]
    for th in thetas(n):
        items = [(e.part, e, e.aabb()) for pid in m.MOVING for e in v.posed(pid, th)] + [(e.part, e, e.aabb()) for e in near]
        for key, hs in touches(items, movers=set(m.MOVING)).items():
            allhits.setdefault(key, {}).update({h: (m.DEFAULT_SCENE, th) for h in hs})
    print(f"clearances in {len(scenes(m))} scenes at rest, and the engine turned through {n} angles: "
          f"{'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s[0]}, theta {d:.2f})" for (a, b), (s, d) in ex))
    if allhits:
        v.fail("parts run into each other")


# ---------------------------------------------------------------- the damper, the outlet, the draught
def check_damper(v):
    """The ash-pit damper: in each of its three places a plate in its recess behind the front face, between its
    guides; shut it covers the whole ash mouth, half its upper half, open none of it; it never leaves the recess."""
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
        if abs(lo[0] - gx[0]) > 1e-9 or abs(hi[0] - gx[1]) > 1e-9 or lo[0] > mx0 or hi[0] < mx1:
            v.fail(f"the damper's plate is not between its guides across the mouth when {place}")
        if place == "shut" and not (lo[1] <= my0 + 1e-9 and hi[1] >= my1):
            v.fail("the damper does not cover the ash mouth when shut")
        if place == "half" and (abs(lo[1] - (my0 + my1) / 2) > 1e-9 or hi[1] < my1):
            v.fail("the half damper does not cover the mouth's upper half")
        if place == "open" and lo[1] < my1 - 1e-9:
            v.fail("the open damper does not clear the ash mouth")
    print(f"damper: in its recess between its guides in all three places; shut over the mouth ({my0}..{my1}), half over "
          f"its upper half, open clear of it")


def check_outlet(v):
    """The outlet: open, its mouth clear to the sky through the stack; capped, a flat iron plate flush with the top in
    the curb's seat, covering the stack's mouth; the curb the lining's refractory brick, a rim round the cap and a
    ledge under its edge all round."""
    m = v.m
    cap = aabb_of(v.by_part["outletcap"])
    (cx0, cx1), _, (cz0, cz1) = m.CURB
    r = m.CURB_RIM
    if not same_box(cap, ([cx0 + r, m.CAP_Y[0], cz0 + r], [cx1 - r, m.TOP, cz1 - r])):
        v.fail(f"the cap is not flush in the curb's seat: {cap}")
    lining = v.by_part["lining"]
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
    print(f"outlet: open to the sky through the stack; capped by a plate flush in the curb's seat over the stack's "
          f"mouth; the curb the lining's brick, rim and ledge ({len(rim) + len(ledge)} points)")


def empty(v, p):
    return not any(inside(e, p) for e in fixed(v))


def check_draught(v):
    """The way the air goes, natural draught only: in at the ash mouth into the cave, up between the firebars, through
    the fire, over the fire bridge, across the chamber over the test, out at the flue opening at the chamber's west
    end, along the back flue and up the stack to the outlet. Sampled points along it lie in no element of the
    furnace built (the brick, the frame, the test, the engine, the fittings), and the stack and the outlet's mouth in
    none at all at any angle but the cap. The firebars lie on their bearers and the bearers' ends are built into the
    bridge and the east wall."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    xm, ym = (mx0 + mx1) / 2, (my0 + my1) / 2
    (cx0, cx1), (cy0, cy1), (cz0, cz1) = m.CAVE
    path = [(xm, ym, z) for z in (-0.5, 0.5, 1.5) + tuple(range(3, int(cz0) + 1, 2))]
    path += [((cx0 + cx1) / 2, (cy0 + cy1) / 2, z + 0.5) for z in range(int(cz0), int(cz1), 2)]
    bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_bar")), key=lambda b: b[0][0])
    for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
        gx = (ahi[0] + blo[0]) / 2
        path += [(gx, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, cz0 + (cz1 - cz0) * f) for f in (0.15, 0.3, 0.5, 0.7, 0.85)]
    yf = (m.BRIDGE_TOP + m.SPRING_Y) / 2
    zm = (m.CH_Z[0] + m.CH_Z[1]) / 2
    path += [(x + 0.5, yf, zm) for x in range(int(m.FLUE_OPENING[0][0]) + 1, int(m.FIRE_X[1]) - 1)]
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = m.FLUE_OPENING
    fx, fy = (fx0 + fx1) / 2, (fy0 + fy1) / 2
    (bx0, bx1), (by0, by1), (bz0, bz1) = m.BACK_FLUE
    (sx0, sx1), (sy0, sy1), (sz0, sz1) = m.STACK
    sx, sz = (sx0 + sx1) / 2, (sz0 + sz1) / 2
    path += [(fx, fy, z + 0.5) for z in range(int(zm), int(fz1))]
    path += [((max(bx0, fx0) + min(bx1, fx1)) / 2, (by0 + by1) / 2, bz0 + 0.5 + q) for q in range(int(bz1 - bz0))]
    path += [(sx, sy0 + 0.5 + q * 0.5, sz) for q in range(int((m.TOP + m.B - sy0) * 2) - 1)]
    if not (fx1 > bx0 and fx0 < bx1 and abs(fz1 - bz0) < 1e-9 and sx0 >= bx0 and sx1 <= bx1 and sz0 >= bz0 and sz1 <= bz1
            and abs(sy0 - by0) < 1e-9):
        v.fail("the flue opening, the back flue and the stack do not run into each other")
    blocked = [p for p in path if not empty(v, p)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [sx0, sy0, sz0], [sx1, m.TOP + m.B, sz1]
    for th in thetas(12):
        for pid in v.by_part:
            if pid == "outletcap" or (pid not in m.MOVING and th):
                continue
            for e in v.posed(pid, th):
                lo, hi = e.aabb()
                if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the stack or over the outlet at theta {th}")
    frame = v.by_part["frame"]
    bearers = [e.aabb() for e in frame if e.name.startswith("fr_bearer")]
    for e in frame:
        if not e.name.startswith("fr_bar"):
            continue
        lo, hi = e.aabb()
        under = [b for b in bearers if b[0][2] < hi[2] and b[1][2] > lo[2]]
        if len(under) != 2 or any(abs(lo[1] - b[1][1]) > 1e-9 for b in under):
            v.fail(f"{e.name} does not lie on a bearer at each end")
    for blo, bhi in bearers:
        zc = (blo[2] + bhi[2]) / 2
        for x in (m.FIRE_X[0] - 0.5, m.FIRE_X[1] + 0.5):
            for y in (bhi[1] + 0.2, blo[1] - 0.2):
                if not any(inside(e, (x, y, zc)) for e in brick(v)):
                    v.fail(f"a bearer is not built into the wall at x {x}, z {zc}")
    gap = (m.FIRE_X[1] - m.FIRE_X[0] - m.BARS * m.BAR_W) / (m.BARS - 1)
    if gap < 0.8:
        v.fail(f"the firebars' gaps ({gap:.2f}) are too narrow for the draught")
    print(f"draught: natural, in at the ash mouth, up through {m.BARS - 1} gaps of {gap:.2f} between the firebars, over "
          f"the bridge ({m.BRIDGE_TOP}) and the test, out at the flue opening ({fy0}..{fy1}) at the chamber's west end, "
          f"along the back flue to the stack and the outlet: {len(path)} points clear; the stack's mouth clear but for "
          f"the cap; every firebar on both bearers, the bearers built into the bridge and the east wall")


# ---------------------------------------------------------------- z-fighting
def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = m.shown(v.parts, [e for pid in v.by_part for e in v.posed(pid, pose[0])], pose)
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
    check_sides(v)
    check_walls(v)
    check_lining(v)
    check_build(v)
    check_test(v)
    check_litharge(v)
    check_blast(v)
    check_engine(v)
    check_damper(v)
    check_outlet(v)
    check_draught(v)
    check_clearances(v, 12 if quick else 48)
    if not quick:
        check_zfight(v)
    return v.ok


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


def check_bricks(m, shape, ship, fail):
    """Any fired brick: the rig's bricks name the crucible furnace's types, each with its block and textures; the
    shape's own `brick` is the default type's colour layer. With the game's assets at hand, every texture of the
    mapping and of the shape is a file there."""
    bricks = ship["bricks"]
    types = [t for t, *_ in m.BRICKS]
    if list(bricks["types"]) != types or bricks["default"] not in types or bricks["code"] != "brick":
        fail("the rig's bricks are not the furnace's types")
    d = bricks["types"][bricks["default"]]
    if shape["textures"]["brick"] != (d.get("overlays") or [d["base"]])[-1]:
        fail("the shape's brick is not the default type's colour")
    assets = game_assets()
    if assets is None:
        print("bricks: SKIPPED the game's assets (no $VINTAGE_STORY or ~/Games/vintagestory): the textures are not "
              "checked against the game")
        return
    paths = {t: [d["base"]] + d.get("overlays", []) for t, d in bricks["types"].items()}
    missing = [(t, c) for t, cs in paths.items() for c in cs if game_texture(assets, c) is None]
    missing += [("shape", c) for c in shape["textures"].values() if game_texture(assets, c) is None]
    if missing:
        fail(f"textures that are not the game's: {missing}")
    print(f"bricks: {len(types)} types; their {sum(len(c) for c in paths.values())} textures and the shape's "
          f"{len(shape['textures'])} files in {assets}")


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
        want = m.GLOWS.get(e["name"].split("_")[0])
        got = {f.get("glow") for f in e["faces"].values()}
        if got and got != {want}:                 # an element with every face hidden (inside the brick) has none
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    if len(cells) != m.CELLS_X * m.CELLS_Y * m.CELLS_Z or any(not c.get("boxes") or c.get("hollow") for c in cells.values()):
        fail("the cells are not the furnace's eighteen, each with its boxes")
    lids = {p for p, c in cells.items() if "lid" in c}
    top = {p for p in cells if p[1] == m.CELLS_Y - 1 - m.ORIGIN_CELL[1]}
    if lids != top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"decks on {sorted(lids)}, not on the top cells {sorted(top)}")
    power = tuple(ship["powerCell"])
    face = {"south": (0, 0, 1)}[ship["powerFace"]]
    beyond = tuple(power[k] + face[k] for k in range(3))
    if power not in cells or beyond in cells:
        fail("the power cell is not a cell of the furnace whose face looks out of it")
    chim = tuple(m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    if chim in cells:
        fail("the chimney's cell is the furnace's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    line = ship["chimney"]
    if abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9 or line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY:
        fail("the chimney's line does not stand on the outlet")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    for key in ("ash", "fireDoor", "door", "litharge"):
        if ship.get(f"{key}Side") != "north":
            fail(f"{key} is not worked from the front")
    for key in ("ash", "fireDoor", "door"):
        if abs(ship[key]["pos"][2]) > 1e-9:
            fail(f"{key} is not on the front face")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(cells)} cells, decks on "
          f"{len(lids)}; glow on the burning coal, the lead, the cake and the running litharge only; the power cell "
          f"{list(power)} facing {ship['powerFace']}; the outlet under the chimney's cell {list(chim)}, the chimney "
          f"{m.MIN_CHIMNEY} blocks from it up; the ash mouth, the fire door and the working door on the front face")
    return ok
