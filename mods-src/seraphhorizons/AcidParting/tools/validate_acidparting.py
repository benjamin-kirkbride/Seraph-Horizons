"""The acid parting vessel generator's validation: make_shape.py calls `validate` and `validate_files`.

Every check prints what it measured and fails the run (the script exits non-zero) when the model breaks its rule. The
catalogue follows the crucible furnace's (`CrucibleFurnace/tools/validate_cruciblefurnace.py`), for what this vessel
takes from it (the stages, the brick's roles, the row, the lining, the damper, the outlet, the draught), and adds its
own (the fire door, the sand bath and the vessel, the precipitating tub and its siphon, the hood). Everything is in
the native
frame (voxels); `check_shipped` in make_shape.py proves the shipped files are this model moved.
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

    def posed(self, pid, depth, theta=0.0):
        key = (pid, round(depth, 9), theta)
        if key not in self.cache:
            if len(self.cache) > 20000:
                self.cache.clear()
            mm = self.m.pm(self.parts, pid, (theta, depth, None))
            self.cache[key] = [posed(el, mm) for el in self.by_part.get(pid, [])]
        return self.cache[key]

    def shown(self, scene, depth):
        """Every element drawn in `scene`, at `depth`."""
        return [e for pid in self.by_part if self.m.on_show(pid, scene) for e in self.posed(pid, depth)]

    def named(self, pid, rx):
        return [e for e in self.by_part.get(pid, []) if re.match(rx, e.name)]


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


def same_box(a, b, tol=1e-6):
    return max(abs(a[0][k] - b[0][k]) for k in range(3)) < tol and max(abs(a[1][k] - b[1][k]) for k in range(3)) < tol


def brick(v):
    """Every brick box, the brickwork's and the lining's."""
    return [e for e in v.by_part["brickwork"] + v.by_part["lining"] if e.name.startswith(("bk_brick_", "ln_brick_"))]


def iron(v):
    """The iron that stands still and is built in: the frame's and the hood's lintel."""
    return v.by_part["frame"] + v.by_part["hoodiron"]


def static(v):
    """Everything built in that stands still, the vessel built: the iron frame, the brickwork with the hood's lintel,
    the lining, the sand bath, the vessel, the tub and its siphon."""
    return (iron(v) + v.by_part["brickwork"] + v.by_part["lining"] + v.by_part["bath"] + v.by_part["vessel"]
            + v.by_part["tub"] + v.by_part["siphon"])


THIN_POST = 1.5                              # a post: a vertical element under this across both ways (an angle's leg is wider)


# ---------------------------------------------------------------- parts, the stages, textures
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
    """The build: at each stage (the iron frame alone, with the brickwork and the hood's lintel, with the lining, with
    the sand bath, with the vessel and the tub, with the services' siphon) nothing floats: every element drawn is joined to the ground. The iron frame stands on its own: it is
    iron only, and no thin post stands in it taller than half the vessel (a vertical element under THIN_POST across
    both ways; the corner angles' legs are wider): the hood's lintel comes with the brickwork that carries it. Every tie is
    hidden once the brick is in: points just off every face of it lie in brick (the brickwork's) or in other iron."""
    m = v.m
    out = []
    for sid, fitted in m.STAGE_STATES:
        drawn = [e for p in v.parts if p["requires"] is None or (p["requires"] in fitted and not p["drivers"]
                                                                  and p["id"] in ("brickwork", "hoodiron", "lining", "bath", "vessel",
                                                                                  "tub", "siphon"))
                 for e in v.by_part[p["id"]]]
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
    if any(e.name.startswith("hi_") for e in frame) or not v.by_part.get("hoodiron"):
        v.fail("the hood's lintel is not the brickwork stage's")
    allin = iron(v)
    ties = [e for e in allin if e.name.startswith("fr_tie")]
    bk = v.by_part["brickwork"]
    bare, showing = 0, set()
    for t in ties:
        others = [e for e in allin if e is not t]
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
                            continue                       # the ground, or other iron
                        if not any(inside(e, pnt) for e in brick(v)):
                            bare += 1
                        elif not any(inside(e, pnt) for e in bk):
                            showing.add(t.name)
    print(f"stages: nothing floats at any stage (elements joined to the ground: {', '.join(out)}); the iron frame iron "
          f"only, with no thin post over {m.H / 2:.0f} (its tallest thin piece {tallest:.2f}); {len(ties)} ties hidden "
          f"in the brickwork once it is in ({bare} points off their faces bare, {len(showing)} in the lining's place)")
    if bare:
        v.fail(f"{bare} points off the ties' faces are in no brick or iron: a tie shows")
    if showing:
        v.fail(f"a tie stands in the lining's place, so it shows before the lining is in: {sorted(showing)}")


ROLES = [
    (r"^bk_brick_", {"brick"}),
    (r"^ln_brick_", {"lining"}),
    (r"^fr_(bearer|bar|lintel|guide|angle|band|tie)", {"iron"}),
    (r"^hi_lintel", {"iron"}),
    (r"^bath_(tray|rim)", {"iron"}),
    (r"^bath_sand", {"sand"}),
    (r"^vessel_", {"ceramic"}),
    (r"^door_", {"iron"}),
    (r"^damper(open|half|shut)_", {"iron"}),
    (r"^tub_", {"oak"}),
    (r"^siphon_", {"lead"}),
    (r"^fuel_", {"fuel"}),
    (r"^fire_", {"ember"}),
    (r"^charge_", {"dore"}),
    (r"^acid_", {"acid"}),
    (r"^solution_", {"solution"}),
    (r"^gold_", {"gold"}),
    (r"^wash_", {"water"}),
    (r"^copper_", {"copper"}),
    (r"^liquor_", {"solution"}),
    (r"^silver_", {"silver"}),
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


# ---------------------------------------------------------------- containment, the top, the sides, the walls
def check_containment(v):
    """Nothing leaves the vessel's four cells over the door's whole travel (every 0.01), so nothing reaches the
    chimney's cell; nothing stands over the top."""
    m = v.m
    worst, where = -1e9, None
    for dp in depths(0.01):
        for pid in v.by_part:
            for el in v.posed(pid, dp):
                lo, hi = el.aabb()
                o = max(-lo[0], hi[0] - m.W, -lo[1], hi[1] - m.H, -lo[2], hi[2] - m.D)
                if o > worst:
                    worst, where = o, (el.name, dp)
    print(f"containment over the door's travel (every 0.01): worst overhang {worst:.3f} voxels ({where[0]} at depth "
          f"{where[1]:.2f}); nothing over the top")
    if worst > 1e-6:
        v.fail(f"{where[0]} leaves the vessel's cells at depth {where[1]}")


def check_top(v):
    """The top is flat and whole: every half voxel of it, just under the top, is brick, the lining's curb or iron set
    flush in it (the angles' tops), whose top is the top, but for the outlet's seat; nothing stands above it."""
    m = v.m
    solid = static(v)
    (sx0, sx1), _, (sz0, sz1) = m.CAP_SEAT
    bad, n = [], 0
    x = 0.25
    while x < m.W:
        z = 0.25
        while z < m.D:
            if not (sx0 <= x <= sx1 and sz0 <= z <= sz1):
                n += 1
                under = [e for e in solid if inside(e, (x, m.TOP - 0.05, z))]
                if not under or any(abs(e.aabb()[1][1] - m.TOP) > 1e-6 for e in under):
                    bad.append((x, z))
            z += 0.5
        x += 0.5
    print(f"top: flat and whole at the top at {n - len(bad)} of {n} points (every half voxel, the outlet's seat aside)")
    if bad:
        v.fail(f"the top is not flat at {len(bad)} points, e.g. {bad[:4]}")


SIDE_IRON = ("fr_angle_", "fr_band_w_", "fr_band_e_")   # the binding's iron on a side face: flush, angle against angle


def check_sides(v):
    """Vessels tile into a row: each side face is flush, plain brick over its whole area (sampled every half voxel)
    but for the corner angles' side legs and the side's top angle, flush with it; nothing but the brick and that iron
    reaches either side
    plane, of any part at any depth; two vessels side by side (one moved a vessel's width along x) touch nowhere but at
    their shared walls (brick against brick, angle against angle), over the door's whole travel."""
    m = v.m
    bricks = v.by_part["brickwork"] + [e for e in v.by_part["frame"] if e.name.startswith(SIDE_IRON)]
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
    reach = set()
    for dp in depths(0.05):
        for pid in v.by_part:
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if (lo[0] < 1e-6 or hi[0] > m.W - 1e-6) and not e.name.startswith(("bk_brick",) + SIDE_IRON):
                    reach.add(e.name)
    if reach:
        v.fail(f"not brick at a side plane: {sorted(reach)[:4]}")
    hits = 0
    for dp in (0.0, 0.5, 1.0):
        a = [e for pid in v.by_part for e in v.posed(pid, dp)]
        b = []
        for e in a:
            c = e.clone()
            c.c = [c.c[0] + m.W, c.c[1], c.c[2]]
            b.append(c)
        near_a = [e for e in a if e.aabb()[1][0] > m.W - 2.0]
        near_b = [e for e in b if e.aabb()[0][0] < m.W + 2.0]
        for ea in near_a:
            for eb in near_b:
                alo, ahi = ea.aabb()
                blo, bhi = eb.aabb()
                if all(alo[q] < bhi[q] - 0.02 and blo[q] < ahi[q] - 0.02 for q in range(3)) and obb_obb(ea, eb):
                    hits += 1
    if hits:
        v.fail(f"two vessels side by side run into each other ({hits} pairs)")
    print(f"sides: plain brick at {n - len(bad)} of {n} points on the two side faces; nothing else reaches them; two "
          f"vessels side by side clear of each other but at the shared walls")


def check_walls(v):
    """Walls, everything centred: the fire chamber, the ash mouth, the fire door's opening, the recess, the sand bath,
    the vessel, the tub, the hood, the stack and the outlet's curb are centred left to right; the brick beside
    the fire chamber, sampled every half voxel from the grate to its top, runs at least MIN_WALL out to each side face,
    and at least FRONT_WALL to the recess in front (the door's opening aside)."""
    m = v.m
    mid = m.W / 2
    spans = (("fire chamber", m.FIRE_X), ("ash mouth", m.MOUTH[0]), ("fire door", m.DOOR_HOLE[0]), ("recess", m.RECESS[0]),
             ("sand bath", m.TRAY[0]), ("vessel", (m.VESSEL_C[0] - 1, m.VESSEL_C[0] + 1)), ("tub", m.TUB[0]),
             ("hood", m.HOOD[0]), ("stack", m.STACK[0]), ("outlet's curb", m.CURB[0]))
    for what, (x0, x1) in spans:
        if abs((x0 + x1) / 2 - mid) > 1e-9:
            v.fail(f"the {what} is not centred left to right")
    near = brick(v) + [e for e in iron(v) if e.name.startswith(("fr_angle", "fr_band", "fr_bearer", "fr_tie", "fr_lintel"))]

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
    (dx0, dx1), (dy0, dy1) = m.DOOR_HOLE
    ys = [m.GRATE_TOP + 0.25 + 0.5 * k for k in range(int((m.FIRE_TOP - m.GRATE_TOP) * 2))]
    for y in ys:
        for z in [m.FIRE_Z[0] + 0.25 + 0.5 * k for k in range(int((m.FIRE_Z[1] - m.FIRE_Z[0]) * 2))]:
            if not (m.FIRE_FLUE[1][0] <= y <= m.FIRE_FLUE[1][1] and z > m.FIRE_Z[1] - 0.5):
                walls.setdefault("west", []).append((run((m.FIRE_X[0], y, z), 0, -1, m.FIRE_X[0]), (y, z)))
                walls.setdefault("east", []).append((run((m.FIRE_X[1], y, z), 0, 1, m.W - m.FIRE_X[1]), (y, z)))
        for x in [m.FIRE_X[0] + 0.25 + 0.5 * k for k in range(int((m.FIRE_X[1] - m.FIRE_X[0]) * 2))]:
            if not (dx0 <= x <= dx1 and dy0 <= y <= dy1):
                walls.setdefault("front", []).append((run((x, y, m.FIRE_Z[0]), 2, -1, m.FIRE_Z[0]), (x, y)))
    worst = {k: min(w) for k, w in walls.items()}
    print("walls: the fire, the ash mouth, the door, the bath, the vessel, the tub, the hood, the stack and the curb "
          "centred; brick beside the fire chamber at least " + ", ".join(f"{k} {t:.2f}" for k, (t, _) in worst.items())
          + f" (MIN_WALL {m.MIN_WALL} at the sides, FRONT_WALL {m.FRONT_WALL} to the recess)")
    for k, (t, at) in worst.items():
        need = m.FRONT_WALL if k == "front" else m.MIN_WALL
        if t < need - 1e-9:
            v.fail(f"the {k} wall is {t:.2f} thick at {at}")


# ---------------------------------------------------------------- the binding: angle iron
def check_binding(v):
    """The binding is angle iron (the owner's rule for furnace-like brickwork): every binder is an L of two legs
    ANGLE_T thick that meet in its corner, each leg's outer face flush with one of the two faces of the brick corner it
    wraps, and each leg's inner face bedded against the brick: points just behind every inner face (every voxel along
    it, a tenth in) are brick (either role) or iron set into it, at least nine in ten of them brick. The corner angles
    stand on the ground the block's full height at its four vertical corners; the top angles run along all four top
    edges. Every top angle's end meets an upright (it shares a face with the corner angle at that end), and each of
    its legs' end faces is closed: the points just past it (every voxel over it, a tenth out) are all in the binding's
    iron, so no brick shows between members at a corner. The binding is one connected frame (its angles joined by
    shared faces). No flat strap or band is left."""
    m = v.m
    frame = v.by_part["frame"]
    flats = [e.name for e in frame if e.name.startswith(("fr_strap", "fr_kerb"))]
    if flats:
        v.fail(f"flat binding left: {flats}")
    built_iron = iron(v)
    out = []
    for name, corner, legs in m.angles():
        els = {leg: [e for e in frame if e.name.startswith(f"{name}_{leg}")] for leg in ("a", "b")}
        if not els["a"] or not els["b"]:
            v.fail(f"{name} has not both legs")
            continue
        boxes = {leg: aabb_of(es) for leg, es in els.items()}
        thick = [min(hi[k] - lo[k] for k in range(3)) for lo, hi in boxes.values()]
        if any(abs(t - m.ANGLE_T) > 1e-9 for t in thick):
            v.fail(f"{name}'s legs are not {m.ANGLE_T} thick")
        (alo, ahi), (blo, bhi) = boxes["a"], boxes["b"]
        ov = [min(ahi[k], bhi[k]) - max(alo[k], blo[k]) for k in range(3)]
        if not (sum(o > 0.05 for o in ov) == 2 and min(ov) > -1e-9):
            v.fail(f"{name}'s legs do not meet in its corner")
        bad, total, bricks = 0, 0, 0
        for leg, (axis, plane) in zip(("a", "b"), corner):
            lo, hi = boxes[leg]
            if abs((lo[axis] if plane < 1e-9 else hi[axis]) - plane) > 1e-9:
                v.fail(f"{name}'s {leg} leg is not flush with its face")
            inward = 1.0 if abs(lo[axis] - plane) < 1e-9 else -1.0
            back = (hi[axis] if inward > 0 else lo[axis]) + inward * 0.1
            others = [q for q in range(3) if q != axis]
            other = boxes["b" if leg == "a" else "a"]
            for ua in [lo[others[0]] + 0.25 + k for k in range(int(hi[others[0]] - lo[others[0]]))]:
                for ub in [lo[others[1]] + 0.25 + k for k in range(int(hi[others[1]] - lo[others[1]]))]:
                    pnt = [0.0, 0.0, 0.0]
                    pnt[axis], pnt[others[0]], pnt[others[1]] = back, ua, ub
                    if all(other[0][k] - 1e-9 <= pnt[k] <= other[1][k] + 1e-9 for k in range(3)):
                        continue                         # behind this leg is the other leg
                    total += 1
                    w = what_is(v, pnt)
                    if w in ("ln", "bk"):
                        bricks += 1
                    elif not any(inside(e, pnt) for e in built_iron if not e.name.startswith(name)):
                        bad += 1
        if bad or bricks < 0.9 * total:
            v.fail(f"{name}'s legs are not bedded against the brick ({bad} open of {total}, {bricks} brick)")
        out.append(f"{name} {bricks}/{total}")
        if name.startswith("fr_angle") and (min(alo[1], blo[1]) > 1e-9 or max(ahi[1], bhi[1]) < m.H - 1e-9):
            v.fail(f"{name} does not stand the block's full height")
    joints = check_binding_joints(v)
    print(f"binding: {len(m.angles())} angles, {joints}; each two legs {m.ANGLE_T} thick meeting in the brick's corner, flush with "
          f"its faces, bedded on the brick (points behind the legs that are brick: {', '.join(out)})")


def faces_meet(a, b):
    """Two boxes share a face of some area (or overlap)."""
    ov = [min(a[1][k], b[1][k]) - max(a[0][k], b[0][k]) for k in range(3)]
    return min(ov) > -1e-9 and sum(o > 1e-6 for o in ov) >= 2


def check_binding_joints(v):
    """The binding's joints (`check_binding`): each top angle's two ends meet the corner angle there and close every
    leg's end face; all the angles one connected frame."""
    m = v.m
    frame = v.by_part["frame"]
    boxes = {name: [aabb_of([e for e in frame if e.name.startswith(f"{name}_{leg}")]) for leg in ("a", "b")]
             for name, _, _ in m.angles()}
    iron_boxes = [b for bs in boxes.values() for b in bs]
    ends = 0
    for name, legs in boxes.items():
        if not name.startswith("fr_band_"):
            continue
        axis = 0 if name[-1] in "ns" else 2                      # the band's long axis
        for end in (0, 1):
            ref = [0.0, 0.0, 0.0]
            ref[axis] = (min(lo[axis] for lo, _ in legs), max(hi[axis] for _, hi in legs))[end]
            ref[2 - axis] = sum((lo[2 - axis] + hi[2 - axis]) / 2 for lo, hi in legs) / 2
            ups = [n for n in boxes if n.startswith("fr_angle_")]
            up = min(ups, key=lambda n: sum(((boxes[n][0][0][k] + boxes[n][0][1][k]) / 2 - ref[k]) ** 2 for k in (0, 2)))
            if not any(faces_meet(lb, ub) for lb in legs for ub in boxes[up]):
                v.fail(f"{name}'s end {'lo' if end == 0 else 'hi'} does not meet the upright {up}")
            for leg, (lo, hi) in zip(("a", "b"), legs):
                at = lo[axis] - 0.1 if end == 0 else hi[axis] + 0.1
                others = [q for q in range(3) if q != axis]
                open_ = []
                for ua in [lo[others[0]] + 0.25 + k * 0.5 for k in range(int(2 * (hi[others[0]] - lo[others[0]])))]:
                    for ub in [lo[others[1]] + 0.25 + k * 0.5 for k in range(int(2 * (hi[others[1]] - lo[others[1]])))]:
                        p = [0.0, 0.0, 0.0]
                        p[axis], p[others[0]], p[others[1]] = at, ua, ub
                        if not any(all(b[0][k] - 1e-9 <= p[k] <= b[1][k] + 1e-9 for k in range(3)) for b in iron_boxes):
                            open_.append(tuple(p))
                if open_:
                    v.fail(f"{name}_{leg}'s {'lo' if end == 0 else 'hi'} end is not closed by iron at {len(open_)} "
                           f"points, e.g. {open_[:3]}")
            ends += 1
    names = list(boxes)
    seen, todo = {names[0]}, [names[0]]
    while todo:
        n = todo.pop()
        for o in names:
            if o not in seen and any(faces_meet(a, b) for a in boxes[n] for b in boxes[o]):
                seen.add(o)
                todo.append(o)
    if len(seen) != len(names):
        v.fail(f"the binding is not one connected frame: {sorted(set(names) - seen)} apart from {names[0]}")
    return (f"{ends} top-angle ends each meeting its upright, every leg's end face closed by iron; one connected frame "
            f"of {len(names)}")


# ---------------------------------------------------------------- the lining
def what_is(v, p):
    """What lies at p: "ln" (the lining's brick), "bk" (the brickwork's), "iron" (the frame), or None (open)."""
    for e in v.by_part["lining"]:
        if inside(e, p):
            return "ln"
    for e in v.by_part["brickwork"]:
        if inside(e, p):
            return "bk"
    if any(inside(e, p) for e in iron(v) + v.by_part["bath"]):
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
    """The lining: on the hot faces (the fire chamber's walls from the bearers up, every face of the fire's flue and
    of the stack that brick stands behind) the brick is the lining's for at least LINING_T from the face out, unless
    the brick ends sooner (the lining then reaches through), sampled every voxel and walked out every quarter; the ash
    pit's, the ash mouth's and the hood's faces are the brickwork's."""
    m = v.m
    t = m.LINING_T
    chamber = ([m.FIRE_X[0], m.BEARER_Y[0], m.FIRE_Z[0]], [m.FIRE_X[1], m.FIRE_TOP, m.FIRE_Z[1]])
    hot = [chamber, m.span(m.FIRE_FLUE), m.span(m.STACK)]
    thinnest, where, n = 1e9, None, 0
    for lo, hi in hot:
        for k in range(3):
            if k == 1 and lo is chamber[0]:
                continue                                   # the chamber's floor is the grate, its roof the sand bath
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
                            if w == "bk" and d - 0.125 < t - 1e-6:
                                if d - 0.125 < thinnest:
                                    thinnest, where = d - 0.125, pnt
                            break
                        d += 0.25
    cold, wrong = 0, []
    (mx0, mx1), (my0, my1) = m.MOUTH
    cave = ([m.FIRE_X[0], m.CAVE_Y[0], m.FIRE_Z[0]], [m.FIRE_X[1], m.CAVE_Y[1], m.FIRE_Z[1]])
    for lo, hi in (cave, ([mx0, my0, 1.0], [mx1, my1, m.FIRE_Z[0]]), m.span(m.HOOD), m.span(m.HOOD_OPEN)):
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
        v.fail(f"the ash pit, the ash mouth or the hood is lined at {len(wrong)} points, e.g. {wrong[:3]}")
    print(f"lining: at least {t} of the lining's brick behind the hot faces ({n} points, walked out every quarter) or "
          f"the lining reaching through; the ash pit, the ash mouth and the hood the brickwork's ({cold} points)")


def check_build(v):
    """The stages' counts: the brickwork's and the lining's volumes, measured from their elements (each lattice box in
    exactly one brick box: their volumes add up to the lattice's), give the rig's quantities at the game's rates,
    rounded up; the frame's recipe, the other stages' materials and the cap's are the generator's; the stages are the
    owner's order, each one requires value fitting its parts, the siphon (the services) last, after both the vessel
    and the tub it hangs between; there is no water line."""
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
    if ([s["requires"] for s in build["stages"]] != list(m.STAGES)
            or list(m.STAGES) != ["brickwork", "lining", "sandbath", "vesseltub", "services"]):
        v.fail("the stages are not brickwork, lining, sandbath, vesseltub, services")
    if st["brickwork"]["materials"][0]["code"] != "game:burnedbrick-*" or not st["brickwork"].get("oneType"):
        v.fail("the brickwork does not take one type of any fired brick")
    if st["brickwork"].get("alsoParts") != ["hoodiron"]:
        v.fail("the hood's lintel does not come with the brickwork")
    for sid, want_m in (("sandbath", m.SANDBATH_STAGE), ("vesseltub", m.VESSELTUB_STAGE), ("services", m.SERVICES_STAGE)):
        if [(x["code"], x["quantity"]) for x in st[sid]["materials"]] != list(want_m):
            v.fail(f"the {sid} stage's materials are not the generator's")
    if (m.VESSEL_ITEM, 1) not in m.VESSELTUB_STAGE or not m.VESSEL_ITEM.startswith("game:crucible-"):
        v.fail("the vessel is not built from a fired crucible")
    if [(x["code"], x["quantity"]) for x in build["frame"]["ingredients"]] != list(m.FRAME_RECIPE):
        v.fail("the frame's recipe is not the generator's")
    by_id = {p["id"]: p for p in v.parts}
    for sid in m.STAGES[2:]:
        if any(by_id[p]["requires"] != sid for p in st[sid]["parts"]):
            v.fail(f"a part of the {sid} stage is not fitted by it")
    staged = {p["id"]: p["requires"] for p in v.parts if p["requires"] in m.STAGES}
    if any(sorted(pid for pid, r in staged.items() if r == sid) != sorted(m.STAGE_PARTS[sid]) for sid in m.STAGES):
        v.fail(f"the stages' parts are not the generator's: {staged}")
    want_parts = {"sandbath": {"bath", "door"}, "vesseltub": {"vessel", "tub"}, "services": {"siphon"}}
    if any(set(st[sid]["parts"]) != ps for sid, ps in want_parts.items()) or "waterLine" in build:
        v.fail("the stages after the lining are not the bath and door, the vessel and tub, the siphon, with no water line")
    requires = {p["requires"] for p in v.parts}
    if not set(m.STAGES) <= requires or any(a not in requires for a in st["sandbath"]["also"]):
        v.fail("a stage's requires is no part's")
    print(f"build: brickwork {lat['bk'] / m.B ** 3:.2f} blocks -> {got['bricks']} fired bricks; lining "
          f"{lat['ln'] / m.B ** 3:.2f} blocks -> {got['lining']} refractory bricks and {got['fireclay']} fire clay; "
          f"sandbath {dict(m.SANDBATH_STAGE)}, vesseltub {dict(m.VESSELTUB_STAGE)}, services "
          f"{dict(m.SERVICES_STAGE)}; the frame {dict(m.FRAME_RECIPE)}; no water line")


# ---------------------------------------------------------------- the fire door, the damper, the fire
def check_door(v):
    """The fire door: at every depth (every 0.01) a plate in the recess behind the front face, between its guides,
    over the fire door's opening; shut (depth 1) it covers the opening with a margin all round, open (depth 0, the rest
    pose) it is clear above it; it never leaves the recess; theta moves nothing; and it is the only part that moves."""
    m = v.m
    (rx, ry, rz) = m.RECESS
    gx = (m.GUIDES[0][1], m.GUIDES[1][0])
    (dx0, dx1), (dy0, dy1) = m.DOOR_HOLE
    for dp in depths(0.01):
        els = v.posed("door", dp)
        lo, hi = aabb_of(els)
        if lo[2] < rz[0] - 1e-9 or hi[2] > rz[1] + 1e-9 or lo[1] < ry[0] - 1e-9 or hi[1] > ry[1] + 1e-9:
            v.fail(f"the fire door leaves its recess at depth {dp:.2f}")
            break
        plate = next(e for e in els if e.name == "door_plate").aabb()
        if abs(plate[0][0] - gx[0]) > 1e-9 or abs(plate[1][0] - gx[1]) > 1e-9:
            v.fail(f"the fire door is not between its guides at depth {dp:.2f}")
            break
    shut = next(e for e in v.posed("door", 1.0) if e.name == "door_plate").aabb()
    open_ = next(e for e in v.posed("door", 0.0) if e.name == "door_plate").aabb()
    margin = min(dx0 - shut[0][0], shut[1][0] - dx1, dy0 - shut[0][1], shut[1][1] - dy1)
    if margin < 0.25:
        v.fail(f"the shut fire door covers its opening with a margin of {margin:.2f}")
    if open_[0][1] < dy1 - 1e-9:
        v.fail("the open fire door does not clear its opening")
    moving = sorted(p["id"] for p in v.parts if any(d["type"] != "rotate" or d.get("ratio", 1.0) != 0.0 for d in p["drivers"]))
    if moving != ["door"]:
        v.fail(f"parts that move: {moving}, not the fire door alone")
    a, b = v.posed("door", 0.6, 0.0), v.posed("door", 0.6, 3.7)
    if any(not same_box(x.aabb(), y.aabb()) for x, y in zip(a, b)):
        v.fail("theta moves the fire door")
    print(f"fire door: in its recess between its guides at every depth; shut over its opening with {margin:.2f} round, "
          f"open {open_[0][1] - dy1:.2f} clear above it; the only part that moves, theta moving nothing")


def check_damper(v):
    """The ash-pit damper: in each of its three places a plate in the recess behind the front face, between the
    guides; shut it covers the whole ash mouth, half its upper half (the mouth open below its middle), open none of it;
    it never leaves the recess, and never reaches the fire door, open or shut."""
    m = v.m
    (mx0, mx1), (my0, my1) = m.MOUTH
    (rx, ry, rz) = m.RECESS
    gx = (m.GUIDES[0][1], m.GUIDES[1][0])
    door_low = min(aabb_of(v.posed("door", dp))[0][1] for dp in (0.0, 1.0))
    for place in m.DAMPERS:
        els = v.by_part[f"damper{place}"]
        plate = next(e for e in els if e.name.endswith("_plate"))
        lo, hi = plate.aabb()
        allo, alhi = aabb_of(els)
        if allo[2] < rz[0] - 1e-9 or alhi[2] > rz[1] + 1e-9 or allo[1] < ry[0] - 1e-9 or alhi[1] > ry[1] + 1e-9:
            v.fail(f"the damper leaves its recess when {place}")
        if alhi[1] > door_low - 0.5:
            v.fail(f"the damper reaches the fire door when {place}")
        if abs(lo[0] - gx[0]) > 1e-9 or abs(hi[0] - gx[1]) > 1e-9 or lo[0] > mx0 or hi[0] < mx1:
            v.fail(f"the damper's plate is not between its guides across the mouth when {place}")
        if place == "shut" and not (lo[1] <= my0 + 1e-9 and hi[1] >= my1):
            v.fail("the damper does not cover the ash mouth when shut")
        if place == "half" and (abs(lo[1] - (my0 + my1) / 2) > 1e-9 or hi[1] < my1):
            v.fail("the half damper does not cover the mouth's upper half")
        if place == "open" and lo[1] < my1 - 1e-9:
            v.fail("the open damper does not clear the ash mouth")
        print(f"damper {place}: covers the mouth from {max(lo[1], my0):.2f} to {max(max(lo[1], my0), min(hi[1], my1)):.2f} "
              f"(the mouth {my0}..{my1}); in its recess between its guides, {door_low - alhi[1]:.2f} under the fire door")


def check_fire(v):
    """The fire: the fuel's bed lies on the firebars, across the chamber from wall to wall, its top at the fire door's
    sill, lumps and all under the flue's floor's top and the door's opening's top; the burning fuel is the same boxes
    as the laid; its cinders lie on the ash pit's floor. The grate: every firebar on both bearers, the bearers' ends
    built into the front and back walls, the bars' gaps wide enough for the draught."""
    m = v.m
    bed = next(e for e in v.by_part["fuel"] if e.name == "fuel_bed").aabb()
    if abs(bed[0][1] - m.GRATE_TOP) > 1e-9 or abs(bed[1][1] - m.DOOR_HOLE[1][0]) > 1e-9:
        v.fail("the fuel's bed is not on the grate up to the fire door's sill")
    if not same_box(bed, ([m.FIRE_X[0], bed[0][1], m.FIRE_Z[0]], [m.FIRE_X[1], bed[1][1], m.FIRE_Z[1]])):
        v.fail("the fuel's bed does not fill the chamber from wall to wall")
    top = max(e.aabb()[1][1] for e in v.by_part["fuel"])
    if top > m.FIRE_FLUE[1][1] - 1.0 or top > m.DOOR_HOLE[1][1] - 1.0:
        v.fail(f"the fuel stands {top:.2f} high, into the flue's or the door's way")
    fire = {e.name.split("_", 1)[1]: e.aabb() for e in v.by_part["fire"]}
    for e in v.by_part["fuel"]:
        f = fire.get(e.name.split("_", 1)[1])
        if f is None or not same_box(e.aabb(), f):
            v.fail(f"{e.name} has no burning twin")
    cinders = [e.aabb() for e in v.by_part["fire"] if "_cinder" in e.name]
    if not cinders or any(abs(lo[1] - m.CAVE_Y[0]) > 0.05 or not (m.FIRE_X[0] < lo[0] and hi[0] < m.FIRE_X[1] and m.FIRE_Z[0] < lo[2]
                                                               and hi[2] < m.FIRE_Z[1]) for lo, hi in cinders):
        v.fail("the cinders do not lie on the ash pit's floor")
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
        for z in (m.FIRE_Z[0] - 0.5, m.FIRE_Z[1] + 0.5):
            for y in (bhi[1] + 0.2, blo[1] - 0.2):
                if not any(inside(e, (xc, y, z)) for e in brick(v)):
                    v.fail(f"a bearer is not built into the wall at x {xc}, z {z}")
    gap = (m.FIRE_Z[1] - m.FIRE_Z[0] - m.BARS * m.BAR_W) / (m.BARS - 1)
    if gap < 0.8:
        v.fail(f"the firebars' gaps ({gap:.2f}) are too narrow for the draught")
    print(f"fire: the bed on the grate ({m.GRATE_TOP}) to the door's sill ({bed[1][1]}), lumps to {top:.2f}; the burning "
          f"bed the same boxes, its cinders on the ash pit's floor; every firebar on both bearers, the bearers built into "
          f"the walls, the bars' gaps {gap:.2f}")


# ---------------------------------------------------------------- the sand bath, the vessel, its contents
def inside_oct(cx, cz, a, x, z):
    dx, dz = abs(x - cx), abs(z - cz)
    return dx <= a and dz <= a and (dx + dz) <= a * math.sqrt(2.0)


def check_bath(v):
    """The sand bath: its tray let into the masonry, its bottom the fire chamber's roof, bearing on the chamber's walls
    (the lining) all round by at least a voxel, its rim flush with the bench; its sand fills it to SAND_TOP round the
    vessel with no gap (sampled), and under it. The vessel stands on the sand under it, set in it, inside the hood
    under the roof, its mouth in the vessel's cell; its inside is open from its floor to its lip."""
    m = v.m
    tray = next(e for e in v.by_part["bath"] if e.name == "bath_tray").aabb()
    (x0, x1), (y0, y1), (z0, z1) = m.TRAY
    if abs(tray[0][1] - m.FIRE_TOP) > 1e-9:
        v.fail("the sand bath's bottom is not the fire chamber's roof")
    bearing = min(m.FIRE_X[0] - x0, x1 - m.FIRE_X[1], m.FIRE_Z[0] - z0, z1 - m.FIRE_Z[1])
    if bearing < 1.0:
        v.fail(f"the sand bath bears {bearing:.2f} on the fire chamber's walls")
    under = [(x, m.FIRE_TOP - 0.1, z) for x in (x0 + 0.25, x1 - 0.25) for z in (z0 + 0.25, (z0 + z1) / 2, z1 - 0.25)]
    under += [(x, m.FIRE_TOP - 0.1, z) for z in (z0 + 0.25, z1 - 0.25) for x in ((x0 + x1) / 2,)]
    if any(what_is(v, p) != "ln" for p in under):
        v.fail("the sand bath does not bear on the lining all round")
    rim = max(e.aabb()[1][1] for e in v.by_part["bath"] if e.name.startswith("bath_rim"))
    bench = [e for e in v.by_part["brickwork"] if inside(e, (x0 - 0.25, m.BENCH - 0.05, (z0 + z1) / 2))]
    if abs(rim - m.BENCH) > 1e-9 or not bench or abs(bench[0].aabb()[1][1] - m.BENCH) > 1e-9:
        v.fail("the sand bath's rim is not flush with the bench")
    sand = [e for e in v.by_part["bath"] if e.name.startswith("bath_sand")]
    cx, cz = m.VESSEL_C
    holes, n = 0, 0
    for y in (m.TRAY[1][0] + m.TRAY_T + 0.1, m.VESSEL_Y[0] + 0.1, m.SAND_TOP - 0.1):
        for p in range(41):
            for q in range(49):
                x = x0 + m.RIM_T + 0.05 + (x1 - x0 - 2 * m.RIM_T - 0.1) * p / 40
                z = z0 + m.RIM_T + 0.05 + (z1 - z0 - 2 * m.RIM_T - 0.1) * q / 48
                if y > m.VESSEL_Y[0] and inside_oct(cx, cz, m.VESSEL_A + 0.01, x, z):
                    continue
                n += 1
                if not any(inside(e, (x, y, z), eps=-0.001) for e in sand):
                    holes += 1
    if holes:
        v.fail(f"{holes} of {n} sampled points of the sand bath are neither sand nor the vessel's place")
    vessel = aabb_of(v.by_part["vessel"])
    sand_under = next(e for e in sand if e.name == "bath_sand_under").aabb()
    if abs(vessel[0][1] - sand_under[1][1]) > 1e-9 or vessel[0][1] >= m.SAND_TOP - 1.0:
        v.fail("the vessel does not stand in the sand")
    (hx0, hx1), (hy0, hy1), (hz0, hz1) = m.HOOD
    (ox0, ox1), _, (oz0, _) = m.HOOD_OPEN
    if not (hx0 < vessel[0][0] and vessel[1][0] < hx1 and oz0 <= vessel[0][2] and vessel[1][2] < hz1 and vessel[1][1] < hy1):
        v.fail("the vessel is not under the hood")
    cell = tuple(int(c // m.B) for c in (cx, m.VESSEL_Y[3] - 0.5, cz))
    if cell != m.vessel_cell():
        v.fail(f"the vessel's mouth is not in its cell {m.vessel_cell()}")
    print(f"sand bath: its bottom the fire chamber's roof, bearing {bearing:.1f} on the lining all round, its rim flush "
          f"with the bench; sand round the vessel with no gap ({n} points), {sand_under[1][1] - sand_under[0][1]:.2f} under "
          f"it; the vessel set {m.SAND_TOP - vessel[0][1]:.2f} into the sand, under the hood, its lip at {vessel[1][1]}")


def check_contents(v):
    """The vessel's contents: the dore, the gold and the liquids lie inside the vessel (within its inner apothem, over
    its floor, under its lip), the liquids a hundredth into its walls so no gap shows round them; the acid and the
    solution at one level, the wash water under them; the charge's grains and the gold's lumps on their layers."""
    m = v.m
    cx, cz = m.VESSEL_C
    floor = m.VESSEL_Y[1]
    out = []
    for pid in ("charge", "acid", "solution", "gold", "wash"):
        lo, hi = aabb_of(v.by_part[pid])
        ok = lo[1] >= floor - 1e-9 and hi[1] <= m.VESSEL_Y[3] - 1.0
        for e in v.by_part[pid]:
            for c in e.corners():
                if not inside_oct(cx, cz, m.LIQUID_A + 1e-6, c[0], c[2]):
                    ok = False
        out.append((pid, ok, hi[1]))
        if not ok:
            v.fail(f"the {pid} is not inside the vessel")
    acid, sol, wash = (aabb_of(v.by_part[p]) for p in ("acid", "solution", "wash"))
    if not same_box(acid, sol) or wash[1][1] > acid[0][1] + 1e-9:
        v.fail("the acid and the solution are not at one level, the wash water under them")
    print("contents: " + "; ".join(f"{pid} inside, to {top:.2f}" for pid, _, top in out)
          + f"; the liquids {m.LIQUID_A - m.INNER_A:.2f} into the walls")


# ---------------------------------------------------------------- the precipitating tub and its siphon
def check_tub(v):
    """The precipitating tub (#931): a wooden vat on the cold front of the bench, under the hood's open front, in front
    of the bath and clear of it and of the fire chamber below (nothing hot under it: its floor on the brickwork's
    bench, over the front wall); in the vessel's cell, centred. The siphon: its leg dips into the vessel (inside its
    inner octagon, over the gold and the charge, under the acid's surface), its run lies on the vessel's lip, its
    outlet leg dips into the tub (inside it, under its rim, into the liquor and under the vessel's liquid surface, so it
    runs), and it touches nothing hot. The copper: a rod resting on the tub's two side rims, the plates hanging from
    it into the liquor, clear of the siphon and the sludge, inside the tub. The silver sludge lies on the tub's floor,
    its lumps under the plates' feet; the liquor's surface is inside the tub, under the vessel's."""
    m = v.m
    bad = []
    tub = aabb_of(v.by_part["tub"])
    (x0, x1), (y0, y1), (z0, z1) = m.TUB
    if not same_box(tub, ([x0, y0, z0], [x1, y1, z1])):
        bad.append(f"the tub is not where TUB puts it: {tub}")
    if abs(y0 - m.BENCH) > 1e-9 or z1 > m.TRAY[2][0] - 0.5 or z1 > m.FIRE_Z[0] - 1.0:
        bad.append("the tub is not on the cold bench, in front of the bath and the fire")
    under = [(x, m.BENCH - 0.1, z) for x in (x0 + 0.5, (x0 + x1) / 2, x1 - 0.5) for z in (z0 + 0.5, (z0 + z1) / 2, z1 - 0.5)]
    if any(what_is(v, q) != "bk" for q in under):
        bad.append("the tub does not stand on the brickwork's bench")
    (hx0, hx1), _, (hz0, _) = m.HOOD_OPEN
    if not (hx0 < x0 and x1 < hx1 and hz0 <= z0 and y1 < m.HOOD_OPEN[1][1] - 4.0):
        bad.append("the tub is not under the hood's open front")
    cell = tuple(int(c // m.B) for c in m.tub_point())
    if (cell[0], int((m.tub_point()[1] - 0.5) // m.B), cell[2]) != m.tub_cell():
        bad.append("the tub's mouth is not in its cell")
    # the siphon
    legs = {e.name: e.aabb() for e in v.by_part["siphon"]}
    vleg, run, tleg = legs["siphon_vesselleg"], legs["siphon_run"], legs["siphon_tubleg"]
    cx, cz = m.VESSEL_C
    corners = [(x, z) for x in (vleg[0][0], vleg[1][0]) for z in (vleg[0][2], vleg[1][2])]
    if not all(inside_oct(cx, cz, m.INNER_A, x, z) for x, z in corners):
        bad.append("the siphon's leg is not inside the vessel")
    top_gold = max(aabb_of(v.by_part[p])[1][1] for p in ("gold", "charge"))
    if not (top_gold + 0.2 < vleg[0][1] < m.ACID_Y[0]):
        bad.append(f"the siphon's leg does not dip into the liquid over the gold ({vleg[0][1]:.2f})")
    if abs(run[0][1] - m.VESSEL_Y[3]) > 1e-9:
        bad.append("the siphon's run does not lie on the vessel's lip")
    (ix0, ix1), (iy0, _), (iz0, iz1) = m.TUB_IN
    if not (ix0 < tleg[0][0] and tleg[1][0] < ix1 and iz0 < tleg[0][2] and tleg[1][2] < iz1):
        bad.append("the siphon's outlet leg is not inside the tub")
    if not (m.SLUDGE_Y[1] + 0.5 < tleg[0][1] < m.LIQUOR_Y[0] and tleg[0][1] < m.ACID_Y[1]):
        bad.append(f"the siphon's outlet does not dip into the liquor under the vessel's surface ({tleg[0][1]:.2f})")
    if m.LIQUOR_Y[1] >= m.ACID_Y[1]:
        bad.append("the tub's liquor stands as high as the vessel's: the siphon would not run")
    if any(obb_obb(e, f) for e in v.by_part["siphon"] for f in v.by_part["frame"] + v.by_part["hoodiron"] + v.by_part["brickwork"]):
        bad.append("the siphon runs into the hood")
    # the copper
    rod = next(e for e in v.by_part["copper"] if e.name == "copper_rod").aabb()
    if abs(rod[0][1] - y1) > 1e-9 or rod[0][0] > x0 + 1e-9 or rod[1][0] < x1 - 1e-9 or not (iz0 < rod[0][2] and rod[1][2] < iz1):
        bad.append("the copper rod does not rest on the tub's side rims")
    plates = [e.aabb() for e in v.by_part["copper"] if e.name.startswith("copper_plate")]
    for lo, hi in plates:
        if not (ix0 < lo[0] and hi[0] < ix1 and iz0 < lo[2] and hi[2] < iz1 and lo[1] > m.SLUDGE_Y[1]
                and lo[1] < m.LIQUOR_Y[0] and hi[1] > rod[0][1] and hi[1] <= rod[1][1]):
            bad.append(f"a copper plate does not hang from the rod into the liquor: {lo}, {hi}")
    if len(plates) < 2:
        bad.append("fewer than two copper plates")
    # the silver and the liquor
    sludge = aabb_of(v.by_part["silver"])
    if abs(sludge[0][1] - iy0) > 1e-9 or not (ix0 - 1e-9 <= sludge[0][0] and sludge[1][0] <= ix1 + 1e-9
                                              and iz0 - 1e-9 <= sludge[0][2] and sludge[1][2] <= iz1 + 1e-9):
        bad.append("the silver sludge does not lie on the tub's floor, inside it")
    if sludge[1][1] >= min(lo[1] for lo, _ in plates):
        bad.append("the sludge reaches the copper plates' feet")
    liquor = aabb_of(v.by_part["liquor"])
    if not same_box(liquor, ([ix0, m.LIQUOR_Y[0], iz0], [ix1, m.LIQUOR_Y[1], iz1])):
        bad.append("the liquor is not the tub's inside at its level")
    for msg in bad:
        v.fail(f"tub: {msg}")
    print(f"tub: on the cold bench in front of the bath ({m.TRAY[2][0] - z1:.1f} clear), under the hood's open front, its "
          f"mouth in {list(m.tub_cell())}; the siphon from over the gold in the vessel, on its lip, to {tleg[0][1]:.2f} in "
          f"the tub, under the vessel's surface ({m.ACID_Y[1]}); {len(plates)} copper plates on a rod on the rims, into the "
          f"liquor; the sludge on the floor to {sludge[1][1]:.2f}")


# ---------------------------------------------------------------- the outlet, the draught
def check_outlet(v):
    """The outlet: open, its mouth clear to the sky through the stack; capped, a flat iron plate flush with the top in
    the curb's seat, covering the stack's mouth; the curb, the lining's refractory brick, a rim round the cap (sampled
    every voxel) and a ledge under its edge all round."""
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
    # the outlet square and centred under the chimney: the stack's mouth and the curb both square, their centres on the
    # chimney's line, and that line through the middle of the chimney's cell
    ch = v.rig["chimney"]["origin"]
    line = (ch[0] * m.B, ch[2] * m.B)
    cell = ((m.CHIMNEY_CELL[0] + 0.5) * m.B, (m.CHIMNEY_CELL[2] + 0.5) * m.B)
    if any(abs(a - b) > 1e-6 for a, b in zip(line, cell)):
        v.fail(f"the chimney's line {line} is not through the middle of its cell {cell}")
    for what, (xs, _, zs) in (("stack's mouth", m.STACK), ("curb", m.CURB)):
        if abs((xs[1] - xs[0]) - (zs[1] - zs[0])) > 1e-6:
            v.fail(f"the {what} is not square: {xs[1] - xs[0]} x {zs[1] - zs[0]}")
        if abs((xs[0] + xs[1]) / 2 - line[0]) > 1e-6 or abs((zs[0] + zs[1]) / 2 - line[1]) > 1e-6:
            v.fail(f"the {what} is not centred on the chimney's line {line}: {((xs[0] + xs[1]) / 2, (zs[0] + zs[1]) / 2)}")
    print(f"outlet: the stack's mouth {sx1 - sx0:.0f} square and the curb {cx1 - cx0:.0f} square, centred on the chimney's "
          f"line through the middle of its cell ({line[0]:.1f}, {line[1]:.1f}); open to the sky through the stack; capped by a plate flush in the curb's seat ({cap[1][0] - cap[0][0]:.0f} "
          f"square, {m.CAP_Y[1] - m.CAP_Y[0]} thick), over the stack's mouth; the curb the lining's brick, rim and ledge "
          f"({len(rim) + len(ledge)} points)")


def empty(v, p, solid):
    return not any(inside(e, p) for e in solid)


def check_draught(v):
    """The ways the air goes, natural draught only. The fire's: in at the ash mouth into the ash pit, up between the
    firebars, over the fuel, out at the flue near the top of the chamber's back wall and along it into the stack, up
    the stack to the outlet. The hood's: in at its open front over the tub, over the vessel, back under the roof, out
    at its flue near the top of its back wall into the stack. Sampled points along both lie in no element built in (the
    iron, the brick, the bath, the vessel, the tub and its siphon), and the stack and the outlet's mouth in no element
    at all at any depth but the cap. The flues and the stack run into each other."""
    m = v.m
    solid = static(v) + v.by_part["copper"]
    (mx0, mx1), (my0, my1) = m.MOUTH
    xm, ym = (mx0 + mx1) / 2, (my0 + my1) / 2
    path = [(xm, ym, z) for z in [-0.5] + [0.5 + q for q in range(int(m.FIRE_Z[0]))]]
    path += [(xm, (m.CAVE_Y[0] + m.CAVE_Y[1]) / 2, m.FIRE_Z[0] + 0.5 + q) for q in range(int(m.FIRE_Z[1] - m.FIRE_Z[0]))]
    bars = sorted((e.aabb() for e in v.by_part["frame"] if e.name.startswith("fr_bar")), key=lambda b: b[0][2])
    for (alo, ahi), (blo, _) in zip(bars, bars[1:]):
        gz = (ahi[2] + blo[2]) / 2
        path += [(m.FIRE_X[0] + (m.FIRE_X[1] - m.FIRE_X[0]) * f, (m.BAR_Y[0] + m.BAR_Y[1]) / 2, gz) for f in (0.2, 0.5, 0.8)]
    (fx0, fx1), (fy0, fy1), (fz0, fz1) = m.FIRE_FLUE
    fx, fy = (fx0 + fx1) / 2, (fy0 + fy1) / 2
    path += [(fx, 11.0, m.FIRE_Z[0] + d) for d in (1.0, 4.0, 7.0)] + [(fx, fy, fz0 + 0.5 + q) for q in range(int(fz1 - fz0))]
    (sx0, sx1), (sy0, _), (sz0, sz1) = m.STACK
    sx, sz = (sx0 + sx1) / 2, (sz0 + sz1) / 2
    path += [(sx, sy0 + 0.5 + q * 0.5, sz) for q in range(int((m.TOP + m.B - sy0) * 2) - 1)]
    (hx0, hx1), (hy0, hy1), (hz0, hz1) = m.HOOD_FLUE
    hood = [(8.0, 22.5, z) for z in (-0.5, 0.5, 2.0, 3.5)] + [(3.5, 22.5, z) for z in (3.5, 4.0)]
    hood += [(3.5, y, 4.0) for y in (24.0, 26.0, 28.0, 29.5)] + [(3.5, 28.0, z) for z in range(4, int(m.HOOD[2][1]))]
    hood += [((hx0 + hx1) / 2, (hy0 + hy1) / 2, hz0 + 0.5 + q) for q in range(int(hz1 - hz0))]
    if not (fx0 >= sx0 and fx1 <= sx1 and abs(fz1 - sz0) < 1e-9 and abs(fy0 - sy0) < 1e-9 and hx0 >= sx0 and hx1 <= sx1
            and abs(hz1 - sz0) < 1e-9 and abs(hy1 - m.HOOD[1][1]) < 1e-9 and abs(fz0 - m.FIRE_Z[1]) < 1e-9
            and abs(hz0 - m.HOOD[2][1]) < 1e-9):
        v.fail("the fire's flue, the hood's flue and the stack do not run into each other")
    blocked = [p for p in path + hood if not empty(v, p, solid)]
    if blocked:
        v.fail(f"the draught's way is blocked at {blocked[:5]}")
    mouth_lo, mouth_hi = [sx0, sy0, sz0], [sx1, m.TOP + m.B, sz1]
    for dp in depths(0.05):
        for pid in v.by_part:
            if pid == "outletcap":
                continue
            for e in v.posed(pid, dp):
                lo, hi = e.aabb()
                if all(lo[q] < mouth_hi[q] - 0.02 and mouth_lo[q] < hi[q] - 0.02 for q in range(3)):
                    v.fail(f"{e.name} is in the stack or over the outlet at depth {dp}")
    print(f"draught: natural; the fire's in at the ash mouth, up through {len(bars) - 1} gaps between the firebars, out at "
          f"its flue ({fy0}..{fy1}) into the stack; the hood's in at its front, out at its flue ({hy0}..{hy1}) into the "
          f"stack; {len(path) + len(hood)} points clear; the stack's mouth clear at every depth but for the cap")


# ---------------------------------------------------------------- clearances, z-fighting
LIQUIDS = {"acid", "solution", "wash", "liquor"}


def allowed(pa, pb, ea, eb):
    """Intended overlaps: the siphon's legs dipping through a liquid's surface (the vessel's, the tub's), and the
    copper plates hanging through the tub's liquor."""
    pair = {pa, pb}
    return ("siphon" in pair and bool(pair & LIQUIDS)) or pair == {"copper", "liquor"}


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
    """The vessel built: every state of the vessel with the tub precipitating, the fire burning and the damper open;
    every state of the tub (with the vessel parting and washing); every state of the fire; every place of the damper
    with the outlet capped; and each stage of the build before it is complete."""
    out = [(vs, "precipitating", "burning", "open", False, m.COMPLETE) for vs, _, _ in m.VESSEL_STATES]
    out += [(vs, ts, "burning", "open", False, m.COMPLETE) for ts, _, _ in m.TUB_STATES for vs in ("parting", "washing")]
    out += [("empty", "empty", f, "open", False, m.COMPLETE) for f, _, _ in m.FIRE_STATES]
    out += [("washing", "sludge", "laid", d, True, m.COMPLETE) for d in m.DAMPERS]
    out += [("empty", "empty", "none", "open", False, "frame"), ("empty", "empty", "none", "open", False, "brickwork"),
            ("empty", "empty", "none", "open", True, "lining"), ("empty", "empty", "none", "shut", True, "sandbath"),
            ("empty", "empty", "none", "half", False, "vesseltub")]
    return out


def check_clearances(v, step):
    """In every scene, nothing of one part runs into another's (the brick, the iron and the fittings are parts apart)
    with the door open; and the door, slid every `step` of depth, touches nothing."""
    m = v.m
    allhits = {}
    for scene in scenes(m):
        items = [(e.part, e, e.aabb()) for e in v.shown(scene, 0.0)]
        for key, hs in touches(items).items():
            allhits.setdefault(key, {}).update({h: (scene, 0.0) for h in hs})
        for dp in depths(step):
            items = [(e.part, e, e.aabb()) for e in v.shown(scene, dp)]
            for key, hs in touches(items, movers={"door"}).items():
                allhits.setdefault(key, {}).update({h: (scene, dp) for h in hs})
    print(f"clearances in {len(scenes(m))} scenes with the door open and slid every {step} of depth: "
          f"{'all clear' if not allhits else f'{len(allhits)} pairs of parts touch'}")
    for key, hs in sorted(allhits.items()):
        ex = sorted(hs.items())[:3]
        print(f"  TOUCH {key[0]} x {key[1]}: {len(hs)}, e.g. " + "; ".join(f"{a}/{b} ({s[0]}, depth {d:.2f})" for (a, b), (s, d) in ex))
    if allhits:
        v.fail("parts run into each other")


def check_zfight(v):
    m = v.m
    bad = 0
    for pose in m.coplanar_poses():
        els = m.shown(v.parts, [e for pid in v.by_part for e in v.posed(pid, pose[1])], pose)
        pairs = coplanar_faces([e for e in els if e.c[1] > -500.0])
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
    check_binding(v)
    check_walls(v)
    check_lining(v)
    check_build(v)
    check_door(v)
    check_damper(v)
    check_fire(v)
    check_bath(v)
    check_contents(v)
    check_tub(v)
    check_outlet(v)
    check_draught(v)
    check_clearances(v, 0.1 if quick else 0.02)
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
    """The file of a game texture code (game:block/...), in the survival or the game domain, or None."""
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
    the game's assets at hand, every texture of the mapping and of the shape is a file there (the shape's are all the
    game's: no new texture), every block one of the game's, and the types the game's."""
    bricks = ship["bricks"]
    types = [t for t, *_ in m.BRICKS]
    if list(bricks["types"]) != types or bricks["default"] not in types or bricks["code"] != "brick":
        fail("the rig's bricks are not the brickwork's types")
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
        want = m.FIRE_GLOW if e["name"].split("_")[0] == "fire" else None
        got = {f.get("glow") for f in e["faces"].values()}
        if not got <= {want}:
            fail(f"{e['name']}'s faces glow {got}, not {want}")
    if "powerCell" in ship or "powerFace" in ship:
        fail("a vessel fired by hand has no power cell")
    if any(k.startswith("air") for k in ship):
        fail("a natural-draught fire has no air port")
    if any(k.startswith(("input", "output", "infeed", "outfeed", "chute")) for k in ship):
        fail("batch equipment has no chute anchor")
    if any(k.startswith("return") for k in ship):
        fail("the water is used up: no return-water anchor")
    if any(k.startswith(("water", "cock", "pipe")) for k in ship) or "pipeMetals" in ship.get("parting", {}):
        fail("watered by bucket, at the vessel: no water anchor, no water line")
    if any(p["id"].startswith(("pipe", "cock")) for p in ship["parts"]) or set(shape["textures"]) & {"pipecopper", "pipelead", "solder", "brass"}:
        fail("watered by bucket: no water line or cock in the model")
    cells = {tuple(c["pos"]): c for c in ship["cells"]}
    want_cells = {tuple(c[k] - m.ORIGIN_CELL[k] for k in range(3)) for c in m.footprint()}
    if set(cells) != want_cells or any(c.get("hollow") or not c.get("boxes") for c in cells.values()):
        fail("the cells are not the four of the vessel, each with boxes")
    lids = {p for p, c in cells.items() if "lid" in c}
    top = {p for p in cells if p[1] == m.CELLS_Y - 1}
    if lids != top or any(cells[p]["lid"] != 1.0 for p in lids):
        fail(f"decks on {sorted(lids)}, not at the top of the top cells {sorted(top)}")
    chim = tuple(m.CHIMNEY_CELL[k] - m.ORIGIN_CELL[k] for k in range(3))
    if chim in cells:
        fail("the chimney's cell is the vessel's")
    out = tuple(ship["outletCell"])
    if (out[0], out[1] + 1, out[2]) != chim or ship["outletFace"] != "up":
        fail("the outlet is not under the chimney's cell")
    if out[2] != m.CELLS_Z - 1 - m.ORIGIN_CELL[2] or out[1] != m.CELLS_Y - 1:
        fail("the outlet is not on the back row's top")
    line = ship["chimney"]
    if line["axis"] != "y" or line["length"] != m.MIN_CHIMNEY or abs(line["origin"][1] - line["length"] / 2 - m.CELLS_Y) > 1e-9:
        fail("the chimney's line does not stand on the outlet, the minimum counted from it up")
    if not (out[0] <= line["origin"][0] <= out[0] + 1 and out[2] <= line["origin"][2] <= out[2] + 1):
        fail("the chimney's line is not over the outlet")
    vc = tuple(ship["vesselCell"])
    if ship["vesselFace"] != "north" or vc not in cells or vc[2] != 0:
        fail("the vessel's cell is not worked from the front")
    vp = ship["vessel"]["pos"]
    if any(not (vc[k] <= vp[k] <= vc[k] + 1) for k in range(3)):
        fail("the vessel's mouth is not in its cell")
    tc = tuple(ship["tubCell"])
    tp = ship["tub"]["pos"]
    if ship["tubFace"] != "north" or tc not in cells or tc[2] != 0 or any(not (tc[k] <= tp[k] <= tc[k] + 1) for k in range(3)):
        fail("the tub's cell is not worked from the front, or its mouth is not in it")
    if not tp[2] < vp[2] or not tp[1] < vp[1]:
        fail("the tub is not in front of the vessel and under its mouth")
    for key in ("ash", "firedoor"):
        p = ship[key]["pos"]
        if ship[f"{key}Side"] != "north" or abs(p[2]) > 1e-9 or not (0.0 <= p[0] <= 1.0 and 0.0 <= p[1] <= 1.0):
            fail(f"the {key} is not on the controller cell's front face")
    if ship["ash"]["pos"][1] >= ship["firedoor"]["pos"][1]:
        fail("the ash mouth is not under the fire door")
    print(f"files: {len(shape['elements'])} elements, frame {len(frame_shape['elements'])}; {len(ship['cells'])} cells, "
          f"decks on {len(lids)}; glow on the burning fuel only; no power cell, no air port, no chute or return-water "
          f"anchor, no water anchor or water line; the fire door and the ash mouth on the controller's front face; the "
          f"vessel (the gold) and the tub in front of it (the silver) worked from the front of {list(vc)}; the outlet "
          f"under the chimney's cell {list(chim)}, the chimney {m.MIN_CHIMNEY} blocks from it up")
    return ok
